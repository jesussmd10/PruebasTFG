using UnityEngine;
using UnityEngine.Networking;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;
using System.Text.RegularExpressions;

public class AudioManager : MonoBehaviour
{
    [SerializeField] private AudioSource audioSource;
    [SerializeField] private IAConfig iaConfig;
    
    [SerializeField] private string nombreVoz = "es-ES-AlvaroNeural";

    // Estructura para almacenar clips de audio pre-descargados junto a su emoción original
    private struct ClipDeAudioListo
    {
        public AudioClip clip;
        public EmotionState emocion;
        public string fraseOriginal;
    }

    // Colas para la reproducción secuencial y pre-descarga paralela
    private readonly Queue<string> colaFrases = new Queue<string>();
    private readonly Queue<ClipDeAudioListo> colaClipsListos = new Queue<ClipDeAudioListo>();
    
    private bool reproduciendoCola = false;
    private bool generandoEnFondo = false;

    // Cache del path de edge-tts
    private string edgeTtsPath = null;
    private bool edgeTtsBuscado = false;

    private int contadorArchivos = 0;

    private void Start()
    {
        if (audioSource == null) audioSource = GetComponent<AudioSource>();

        // Buscar edge-tts al iniciar
        StartCoroutine(BuscarEdgeTTS());
    }

    private void OnEnable()
    {
        // Suscribirse al streaming: recibir frases individuales
        EventSystem.OnFraseListaParaTTS.AddListener(EncolarFrase);
    }

    private void OnDisable()
    {
        EventSystem.OnFraseListaParaTTS.RemoveListener(EncolarFrase);
    }

    /// <summary>
    /// Limpia todas las colas de audio y detiene cualquier reproducción y descarga en curso.
    /// Vital para reiniciar el juego sin arrastrar audios fantasma del caso anterior.
    /// </summary>
    public void PararYLimpiarAudio()
    {
        colaFrases.Clear();
        colaClipsListos.Clear();
        
        if (audioSource != null)
        {
            if (audioSource.isPlaying) audioSource.Stop();
            audioSource.clip = null; // CRÍTICO: Evita que PlayOnAwake reproduzca el último clip al reiniciar el NPC
        }
        
        reproduciendoCola = false;
        generandoEnFondo = false;
        
        StopAllCoroutines(); 

        // Si EdgeTTS estaba buscando su ruta al iniciar y lo hemos cortado, lo reactivamos
        if (iaConfig != null && iaConfig.ttsProvider == IAConfig.TTSProvider.EdgeTTS && !edgeTtsBuscado)
        {
            StartCoroutine(BuscarEdgeTTS());
        }

        UnityEngine.Debug.Log("[AudioManager] Colas de TTS limpiadas y audio detenido.");
    }

    /// <summary>
    /// Encola una frase para reproducción. Se usa desde el streaming.
    /// Inicia la descarga en segundo plano y la reproducción si no están activas.
    /// </summary>
    public void EncolarFrase(string frase)
    {
        if (string.IsNullOrWhiteSpace(frase)) return;

        colaFrases.Enqueue(frase);
        UnityEngine.Debug.Log($"[AudioQueue] Encolada frase: '{frase}' (Frases en cola: {colaFrases.Count})");

        // Iniciar la descarga en segundo plano si no está activa
        if (!generandoEnFondo)
        {
            StartCoroutine(PreDescargarClipsLoop());
        }

        // Si no estamos reproduciendo, empezar
        if (!reproduciendoCola)
        {
            StartCoroutine(ProcesarColaClips());
        }
    }

    /// <summary>
    /// Método clásico para reproducir un texto completo de una vez (no streaming).
    /// </summary>
    public void ReproducirTexto(string texto)
    {
        EncolarFrase(texto);
    }

    /// <summary>
    /// Bucle que pre-descarga y pre-genera los audios de la cola en segundo plano.
    /// Mientras el usuario escucha una frase, esta corrutina descarga las siguientes de ElevenLabs/OpenAI/Edge.
    /// </summary>
    private IEnumerator PreDescargarClipsLoop()
    {
        generandoEnFondo = true;

        while (colaFrases.Count > 0)
        {
            string fraseOriginal = colaFrases.Dequeue();

            // 1. Detectar emoción de esta frase específica
            EmotionState emocion = DetectarEmocionEnTexto(fraseOriginal);

            // 2. Limpiar texto para TTS
            string fraseLimpia = NPCBehavior.LimpiarTexto(fraseOriginal);

            if (string.IsNullOrWhiteSpace(fraseLimpia)) continue;

            UnityEngine.Debug.Log($"[AudioQueue] Iniciando pre-descarga de: '{fraseLimpia.Substring(0, Mathf.Min(30, fraseLimpia.Length))}...'");

            // 3. Descargar el clip
            AudioClip clipDescargado = null;
            bool descargaTerminada = false;

            yield return StartCoroutine(DescargarClip(fraseLimpia, (clip) =>
            {
                clipDescargado = clip;
                descargaTerminada = true;
            }));

            // Esperar a que el callback se invoque realmente
            while (!descargaTerminada)
            {
                yield return null;
            }

            if (clipDescargado != null)
            {
                colaClipsListos.Enqueue(new ClipDeAudioListo
                {
                    clip = clipDescargado,
                    emocion = emocion,
                    fraseOriginal = fraseOriginal
                });
                UnityEngine.Debug.Log($"[AudioQueue] Clip pre-descargado y encolado listo. (Clips listos en caché: {colaClipsListos.Count})");
            }
            else
            {
                UnityEngine.Debug.LogWarning($"[AudioQueue] Falló la descarga del clip para: '{fraseLimpia}'");
            }
        }

        generandoEnFondo = false;
    }

    /// <summary>
    /// Bucle de reproducción de audio. Lee los clips ya listos en la caché de forma secuencial y sin latencias.
    /// </summary>
    private IEnumerator ProcesarColaClips()
    {
        reproduciendoCola = true;

        // Continuar reproduciendo si hay clips listos, o si hay descargas pendientes en el fondo
        while (colaClipsListos.Count > 0 || generandoEnFondo || colaFrases.Count > 0)
        {
            if (colaClipsListos.Count > 0)
            {
                ClipDeAudioListo clipListo = colaClipsListos.Dequeue();
                
                // Reproducir de forma síncrona y esperar a que acabe
                yield return StartCoroutine(ReproducirClipConEmocion(clipListo));
            }
            else
            {
                // Si la cola de listos está vacía pero aún se están descargando cosas en el fondo,
                // esperamos un instante a que la descarga finalice
                yield return new WaitForSeconds(0.05f);
            }
        }

        reproduciendoCola = false;
    }

    /// <summary>
    /// Asigna el clip descargado al AudioSource, aplica el trigger de emoción en el momento exacto
    /// y gestiona el flujo de animaciones y labios de forma totalmente coordinada.
    /// </summary>
    private IEnumerator ReproducirClipConEmocion(ClipDeAudioListo clipListo)
    {
        if (clipListo.clip == null) yield break;

        // 1. Cargar clip e iniciar playback
        if (audioSource.clip != null) Destroy(audioSource.clip);
        audioSource.clip = clipListo.clip;
        audioSource.Play();

        UnityEngine.Debug.Log($"[AudioPlayback] Reproduciendo: '{clipListo.fraseOriginal.Substring(0, Mathf.Min(35, clipListo.fraseOriginal.Length))}...' con emoción: {clipListo.emocion}");

        // 2. Disparar el evento de emoción al Animator en el milisegundo exacto de empezar a hablar
        EventSystem.OnEmotionChanged.Invoke(clipListo.emocion);

        // 3. Controlar la duración del gesto emocional si es corto
        if (clipListo.emocion == EmotionState.Negando)
        {
            float tiempoGesto = 1.2f;
            while (audioSource.isPlaying && tiempoGesto > 0)
            {
                tiempoGesto -= Time.deltaTime;
                yield return null;
            }
            if (audioSource.isPlaying)
            {
                // Volver a mover los labios bajo el estado Hablando
                EventSystem.OnEmotionChanged.Invoke(EmotionState.Hablando);
            }
        }
        else if (clipListo.emocion == EmotionState.Nervioso || clipListo.emocion == EmotionState.Calmado)
        {
            // Pequeño delay de asentamiento corporal antes de habilitar los labios de Hablar
            yield return new WaitForSeconds(0.15f);
            if (audioSource.isPlaying)
            {
                EventSystem.OnEmotionChanged.Invoke(EmotionState.Hablando);
            }
        }
        else
        {
            // Por defecto, forzar hablar para mover los labios
            EventSystem.OnEmotionChanged.Invoke(EmotionState.Hablando);
        }

        // 4. Esperar a que la frase completa termine de sonar
        while (audioSource.isPlaying)
        {
            yield return null;
        }

        // 5. Devolver al sospechoso a Calmado/Idle en cuanto el clip termine.
        // Esto evita que siga moviendo los labios (animación Hablando) 
        // en los micro-cortes si está esperando a que descargue el siguiente audio.
        EventSystem.OnEmotionChanged.Invoke(EmotionState.Calmado);
    }

    /// <summary>
    /// Descarga un AudioClip utilizando el proveedor seleccionado de forma modular.
    /// </summary>
    private IEnumerator DescargarClip(string texto, System.Action<AudioClip> alTerminar)
    {
        if (iaConfig == null || iaConfig.ttsProvider == IAConfig.TTSProvider.EdgeTTS)
        {
            yield return StartCoroutine(DescargarEdge(texto, alTerminar));
        }
        else if (iaConfig.ttsProvider == IAConfig.TTSProvider.OpenAI && !string.IsNullOrEmpty(iaConfig.ttsApiKey))
        {
            yield return StartCoroutine(DescargarOpenAI(texto, alTerminar));
        }
        else if (iaConfig.ttsProvider == IAConfig.TTSProvider.ElevenLabs && !string.IsNullOrEmpty(iaConfig.ttsApiKey))
        {
            yield return StartCoroutine(DescargarElevenLabs(texto, alTerminar));
        }
        else
        {
            yield return StartCoroutine(DescargarEdge(texto, alTerminar));
        }
    }

    // =====================================================
    //  EDGE-TTS
    // =====================================================

    private IEnumerator BuscarEdgeTTS()
    {
        if (edgeTtsBuscado) yield break;
        edgeTtsBuscado = true;

        // 0. Buscar primero en StreamingAssets (para máxima portabilidad en builds y otros PCs)
        string pathStreamingAssets = Path.Combine(Application.streamingAssetsPath, "edge-tts.exe");
        if (File.Exists(pathStreamingAssets))
        {
            edgeTtsPath = pathStreamingAssets;
            UnityEngine.Debug.Log($"[EdgeTTS] ¡Encontrado en StreamingAssets (Portátil)!: {edgeTtsPath}");
            yield break;
        }

        string resultado = null;

        var tarea = Task.Run(() =>
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "where",
                    Arguments = "edge-tts",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    CreateNoWindow = true
                };
                var proc = Process.Start(psi);
                resultado = proc.StandardOutput.ReadLine();
                proc.WaitForExit(3000);
            }
            catch { }
        });

        float timeout = Time.time + 3.5f;
        while (!tarea.IsCompleted && Time.time < timeout)
        {
            yield return null;
        }

        if (!string.IsNullOrEmpty(resultado))
        {
            edgeTtsPath = resultado;
            UnityEngine.Debug.Log($"[EdgeTTS] Encontrado via PATH: {edgeTtsPath}");
            yield break;
        }

        string userProfile = System.Environment.GetFolderPath(System.Environment.SpecialFolder.UserProfile);

        // 1.5. Buscar en la carpeta de paquetes de la Tienda de Windows (Microsoft Store Python)
        string packagesPath = Path.Combine(userProfile, "AppData", "Local", "Packages");
        if (Directory.Exists(packagesPath))
        {
            try
            {
                var dirs = Directory.GetDirectories(packagesPath, "*PythonSoftwareFoundation.Python*");
                foreach (string dir in dirs)
                {
                    string localCache = Path.Combine(dir, "LocalCache", "local-packages");
                    if (Directory.Exists(localCache))
                    {
                        var pyDirs = Directory.GetDirectories(localCache, "Python*");
                        foreach (string pyDir in pyDirs)
                        {
                            string exePath = Path.Combine(pyDir, "Scripts", "edge-tts.exe");
                            if (File.Exists(exePath))
                            {
                                edgeTtsPath = exePath;
                                UnityEngine.Debug.Log($"[EdgeTTS] Encontrado dinámicamente en Windows Store Python: {edgeTtsPath}");
                                yield break;
                            }
                        }
                    }
                }
            }
            catch { }
        }

        string[] posiblesRutas = new string[]
        {
            Path.Combine(userProfile, "anaconda3", "Scripts", "edge-tts.exe"),
            Path.Combine(userProfile, "miniconda3", "Scripts", "edge-tts.exe"),
            Path.Combine(userProfile, "AppData", "Local", "Programs", "Python", "Python311", "Scripts", "edge-tts.exe"),
            Path.Combine(userProfile, "AppData", "Local", "Programs", "Python", "Python312", "Scripts", "edge-tts.exe"),
            Path.Combine(userProfile, "AppData", "Local", "Programs", "Python", "Python310", "Scripts", "edge-tts.exe"),
            Path.Combine(userProfile, "AppData", "Roaming", "Python", "Python311", "Scripts", "edge-tts.exe"),
            Path.Combine(userProfile, "AppData", "Roaming", "Python", "Python312", "Scripts", "edge-tts.exe"),
            Path.Combine(userProfile, ".local", "bin", "edge-tts"),
        };

        foreach (string ruta in posiblesRutas)
        {
            if (File.Exists(ruta))
            {
                edgeTtsPath = ruta;
                UnityEngine.Debug.Log($"[EdgeTTS] Encontrado en ruta conocida: {edgeTtsPath}");
                yield break;
            }
        }

        edgeTtsPath = "edge-tts";
        UnityEngine.Debug.LogWarning("[EdgeTTS] No se encontró el ejecutable en rutas típicas. Usando fallback de PATH.");
    }

    private IEnumerator DescargarEdge(string texto, System.Action<AudioClip> alTerminar)
    {
        if (string.IsNullOrEmpty(texto)) { alTerminar(null); yield break; }

        while (!edgeTtsBuscado) yield return null;

        contadorArchivos++;
        string outputPath = Path.Combine(Application.persistentDataPath, $"tts_output_{contadorArchivos}.mp3");
        string textoLimpio = texto.Replace("\"", "'").Replace("\n", " ").Replace("\r", "");
        
        // Guardar el texto en un archivo para evitar límites de longitud o cortes en la línea de comandos
        string textFilePath = Path.Combine(Application.persistentDataPath, $"tts_text_{contadorArchivos}.txt");
        File.WriteAllText(textFilePath, textoLimpio, Encoding.UTF8);

        ProcessStartInfo startInfo = new ProcessStartInfo
        {
            FileName = edgeTtsPath,
            Arguments = $"--voice {nombreVoz} -f \"{textFilePath}\" --write-media \"{outputPath}\"",
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardError = true
        };

        Process edgeProcess = new Process { StartInfo = startInfo };
        
        try
        {
            edgeProcess.Start();
        }
        catch (System.Exception ex)
        {
            UnityEngine.Debug.LogError($"[EdgeTTS] Error iniciando edge-tts: {ex.Message}. ¿Está instalado?");
            alTerminar(null);
            yield break;
        }

        while (!edgeProcess.HasExited)
        {
            yield return null; 
        }

        if (edgeProcess.ExitCode != 0)
        {
            string error = edgeProcess.StandardError.ReadToEnd();
            UnityEngine.Debug.LogError($"[EdgeTTS] Error en proceso: {error}");
            alTerminar(null);
            yield break;
        }

        if (File.Exists(outputPath))
        {
            using (UnityWebRequest www = UnityWebRequestMultimedia.GetAudioClip("file://" + outputPath, AudioType.MPEG))
            {
                yield return www.SendWebRequest();

                if (www.result == UnityWebRequest.Result.Success)
                {
                    AudioClip clip = DownloadHandlerAudioClip.GetContent(www);
                    alTerminar(clip);
                }
                else
                {
                    UnityEngine.Debug.LogError("[EdgeTTS] Error cargando clip: " + www.error);
                    alTerminar(null);
                }
            }

            StartCoroutine(LimpiarArchivosTemporales(outputPath, textFilePath, 4f));
        }
        else
        {
            alTerminar(null);
        }
    }

    private IEnumerator LimpiarArchivosTemporales(string pathAudio, string pathTexto, float delay)
    {
        yield return new WaitForSeconds(delay);
        try
        {
            if (File.Exists(pathAudio)) File.Delete(pathAudio);
            if (File.Exists(pathTexto)) File.Delete(pathTexto);
        }
        catch { }
    }

    // =====================================================
    //  OPENAI TTS
    // =====================================================

    private IEnumerator DescargarOpenAI(string texto, System.Action<AudioClip> alTerminar)
    {
        if (string.IsNullOrEmpty(texto)) { alTerminar(null); yield break; }

        string url = "https://api.openai.com/v1/audio/speech";
        string voice = string.IsNullOrEmpty(iaConfig.ttsVoiceId) ? "alloy" : iaConfig.ttsVoiceId;

        var datos = new
        {
            model = "tts-1",
            input = texto,
            voice = voice
        };

        string jsonBody = JsonConvert.SerializeObject(datos);
        byte[] bodyRaw = Encoding.UTF8.GetBytes(jsonBody);

        using (UnityWebRequest request = new UnityWebRequest(url, "POST"))
        {
            request.uploadHandler = new UploadHandlerRaw(bodyRaw);
            request.downloadHandler = new DownloadHandlerAudioClip(url, AudioType.MPEG);
            request.SetRequestHeader("Content-Type", "application/json");
            request.SetRequestHeader("Authorization", "Bearer " + iaConfig.ttsApiKey);

            yield return request.SendWebRequest();

            if (request.result == UnityWebRequest.Result.Success)
            {
                AudioClip clip = DownloadHandlerAudioClip.GetContent(request);
                alTerminar(clip);
            }
            else
            {
                UnityEngine.Debug.LogError("[OpenAI TTS] Error API: " + request.error);
                // Fallback robusto a EdgeTTS
                yield return StartCoroutine(DescargarEdge(texto, alTerminar));
            }
        }
    }

    // =====================================================
    //  ELEVENLABS TTS
    // =====================================================

    private IEnumerator DescargarElevenLabs(string texto, System.Action<AudioClip> alTerminar)
    {
        if (string.IsNullOrEmpty(texto)) { alTerminar(null); yield break; }

        string voiceId = string.IsNullOrEmpty(iaConfig.ttsVoiceId) ? "21m00Tcm4TlvDq8ikWAM" : iaConfig.ttsVoiceId;
        string url = $"https://api.elevenlabs.io/v1/text-to-speech/{voiceId}";

        var datos = new
        {
            text = texto,
            model_id = "eleven_multilingual_v2"
        };

        string jsonBody = JsonConvert.SerializeObject(datos);
        byte[] bodyRaw = Encoding.UTF8.GetBytes(jsonBody);

        using (UnityWebRequest request = new UnityWebRequest(url, "POST"))
        {
            request.uploadHandler = new UploadHandlerRaw(bodyRaw);
            request.downloadHandler = new DownloadHandlerAudioClip(url, AudioType.MPEG);
            request.SetRequestHeader("Content-Type", "application/json");
            request.SetRequestHeader("xi-api-key", iaConfig.ttsApiKey);

            yield return request.SendWebRequest();

            if (request.result == UnityWebRequest.Result.Success)
            {
                AudioClip clip = DownloadHandlerAudioClip.GetContent(request);
                alTerminar(clip);
            }
            else
            {
                UnityEngine.Debug.LogError("[ElevenLabs TTS] Error API: " + request.error);
                // Fallback robusto a EdgeTTS
                yield return StartCoroutine(DescargarEdge(texto, alTerminar));
            }
        }
    }

    // =====================================================
    //  DETECCIÓN DE EMOCIONES
    // =====================================================

    private EmotionState DetectarEmocionEnTexto(string texto)
    {
        EmotionState emocion = EmotionState.Hablando;

        // Reparar paréntesis/corchetes/asteriscos huérfanos al inicio (si hay un cierre antes de una apertura)
        int idxCierreP = texto.IndexOf(')');
        int idxAperturaP = texto.IndexOf('(');
        if (idxCierreP >= 0 && (idxAperturaP < 0 || idxAperturaP > idxCierreP))
        {
            texto = "(" + texto;
        }

        int idxCierreC = texto.IndexOf(']');
        int idxAperturaC = texto.IndexOf('[');
        if (idxCierreC >= 0 && (idxAperturaC < 0 || idxAperturaC > idxCierreC))
        {
            texto = "[" + texto;
        }

        var matches = Regex.Matches(texto, @"\((.*?)\)|\[(.*?)\]|\*(.*?)\*", RegexOptions.Singleline);
        foreach (Match match in matches)
        {
            string accion = "";
            if (match.Groups[1].Success) accion = match.Groups[1].Value;
            else if (match.Groups[2].Success) accion = match.Groups[2].Value;
            else if (match.Groups[3].Success) accion = match.Groups[3].Value;

            string accionLow = accion.ToLower();

            if (accionLow.Contains("tiembla") || accionLow.Contains("miedo") || 
                accionLow.Contains("nervioso") || accionLow.Contains("asusta") || 
                accionLow.Contains("tartamudea") || accionLow.Contains("suda") ||
                accionLow.Contains("tensa") || accionLow.Contains("agita") ||
                accionLow.Contains("furioso") || accionLow.Contains("agresivo") ||
                accionLow.Contains("altera") || accionLow.Contains("pánico") ||
                accionLow.Contains("duda") || accionLow.Contains("enfada") ||
                accionLow.Contains("llora") || accionLow.Contains("desespera"))
            {
                return EmotionState.Nervioso;
            }
            else if (accionLow.Contains("niega") || 
                     accionLow.Contains("cabeza") || accionLow.Contains("rechaza"))
            {
                return EmotionState.Negando;
            }
            else if (accionLow.Contains("calma") || accionLow.Contains("respira") || 
                     accionLow.Contains("tranquil") || accionLow.Contains("suspira") ||
                     accionLow.Contains("relaja"))
            {
                return EmotionState.Calmado;
            }
        }

        string tx = texto.ToLower();
        if (tx.Contains("no, no") || tx.Contains("eso no es verdad") || 
            tx.Contains("es mentira") || tx.Contains("falso") || 
            tx.Contains("montaje") || tx.Contains("injusto") || 
            tx.Contains("jamás") || tx.Contains("me niego"))
        {
            return EmotionState.Negando;
        }

        return emocion;
    }
}