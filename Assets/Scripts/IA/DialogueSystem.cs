using UnityEngine;
using UnityEngine.Networking;
using System;
using System.Text;
using System.Threading.Tasks;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net.Http;
using System.IO;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.Text.RegularExpressions;


public class DialogueSystem : MonoBehaviour
{
    [SerializeField] private IAConfig iaConfig;
    private List<object> historialDialogo = new List<object>();
    private bool memoriaIniciada = false;
    private GameContext.CasoDelito casoActual;
    private System.Threading.CancellationTokenSource cancellationTokenSource;

    public void LimpiarHistorial()
    {
        historialDialogo.Clear();
        memoriaIniciada = false;
        
        // Cancelar cualquier conexión HTTP de streaming en curso
        if (cancellationTokenSource != null)
        {
            cancellationTokenSource.Cancel();
            cancellationTokenSource.Dispose();
        }
        cancellationTokenSource = new System.Threading.CancellationTokenSource();
        
        // Limpiar cualquier frase residual en la cola para que el TTS no hable de más
        while (colaFrasesParaTTS.TryDequeue(out _)) { }
        
        Debug.Log("[DialogueSystem] Memoria conversacional y colas de audio reseteadas.");
    }

    /// <summary>
    /// Indica si el sistema de streaming está activo (para que otros scripts sepan).
    /// </summary>
    public bool UsaStreaming => iaConfig != null && iaConfig.usarStreaming;

    // Streaming: HttpClient para conexiones SSE
    private static readonly HttpClient httpClient = new HttpClient() { Timeout = TimeSpan.FromSeconds(30) };

    // Colas thread-safe para pasar datos del hilo de fondo al hilo principal de Unity
    // Esto evita el "reloj" de bloqueo en VR
    private readonly ConcurrentQueue<(string frase, EmotionState emocion)> colaFrasesParaTTS = new ConcurrentQueue<(string, EmotionState)>();

    private void Start()
    {
        if (iaConfig == null)
        {
            Debug.LogError("IAConfig no asignado");
        }
    }

    /// <summary>
    /// Procesa las colas del streaming en el hilo principal de Unity.
    /// Esto evita que el HttpClient bloquee el hilo principal y cause el "reloj" en VR.
    /// </summary>
    private void Update()
    {

        // Despachar frases completas para TTS
        while (colaFrasesParaTTS.TryDequeue(out var tupla))
        {
            string frase = tupla.frase;
            EmotionState emocion = tupla.emocion;
            
            // Limpiar tartamudeos con guión que el TTS lee mal (ej: "N-no" -> "no", "P-pero" -> "pero")
            frase = System.Text.RegularExpressions.Regex.Replace(frase, @"(?i)\b[a-zñáéíóú]-", "");
            
            // Eliminar cualquier metadato entre corchetes o paréntesis para que el TTS jamás lo lea en voz alta
            frase = System.Text.RegularExpressions.Regex.Replace(frase, @"\[.*?\]", "").Trim();
            frase = System.Text.RegularExpressions.Regex.Replace(frase, @"\(.*?\)", "").Trim();
            
            // Fuerte barrera contra alucinaciones de modelos abliterados o pequeños:
            // 1. Eliminar corchetes abiertos que hayan quedado sin cerrar por límite de tokens
            frase = System.Text.RegularExpressions.Regex.Replace(frase, @"\[[^\]]*$", "").Trim();
            
            // 2. Eliminar alucinaciones donde el modelo repite partes del prompt como si fueran diálogo
            frase = System.Text.RegularExpressions.Regex.Replace(frase, @"(?i)\[?SISTEMA:.*", "").Trim();
            frase = System.Text.RegularExpressions.Regex.Replace(frase, @"(?i)¡CRÍTICO.*", "").Trim();
            
            // 3. Eliminar pistas alucinadas en formato Markdown (ej: **Pista:** COARTADA) que no están entre corchetes
            frase = System.Text.RegularExpressions.Regex.Replace(frase, @"(?i)(?:\*|_)*PISTA[:\*\]\s]*(COARTADA|SECRETO|CONTRADICCION|CONTRADICCIÓN).*", "").Trim();
            
            EventSystem.OnFraseListaParaTTS.Invoke(frase, emocion);
        }
    }

    private enum NivelInteligencia { Simple, Medio, Complejo }

    private NivelInteligencia ObtenerNivelInteligencia(string nombreModelo)
    {
        if (string.IsNullOrEmpty(nombreModelo)) return NivelInteligencia.Medio;
        
        string m = nombreModelo.ToLower();
        if (m.Contains("-1b") || m.Contains("-2b") || m.Contains("1.5b") || m.Contains("tiny")) return NivelInteligencia.Simple;
        if (m.Contains("-3b") || m.Contains("-4b") || m.Contains("mini") || m.Contains("phi-3")) return NivelInteligencia.Medio;
        if (m.Contains("-7b") || m.Contains("-8b") || m.Contains("-9b") || m.Contains("llama-3")) return NivelInteligencia.Complejo;
        
        return NivelInteligencia.Medio; // Por defecto
    }

    /// <summary>
    /// Envía un ping al endpoint del modelo de diálogo para forzar su carga en la VRAM.
    /// Esto evita latencias masivas en la primera pregunta del interrogatorio.
    /// </summary>
    public async Task PrecargarModeloDialogoAsync()
    {
        try
        {
            if (iaConfig == null || string.IsNullOrEmpty(iaConfig.urlModeloDialogo)) return;

            Debug.Log($"[DialogueSystem] Solicitando carga anticipada del modelo de diálogo ({iaConfig.nombreModeloDialogo}) en VRAM...");

            var requestData = new
            {
                model = iaConfig.nombreModeloDialogo,
                messages = new[] { new { role = "system", content = "ping" } },
                max_tokens = 1
            };

            string jsonBody = Newtonsoft.Json.JsonConvert.SerializeObject(requestData);
            using (var request = new UnityEngine.Networking.UnityWebRequest(iaConfig.urlModeloDialogo, "POST"))
            {
                byte[] bodyRaw = System.Text.Encoding.UTF8.GetBytes(jsonBody);
                request.uploadHandler = new UnityEngine.Networking.UploadHandlerRaw(bodyRaw);
                request.downloadHandler = new UnityEngine.Networking.DownloadHandlerBuffer();
                request.SetRequestHeader("Content-Type", "application/json");
                
                // No esperamos eternamente a la precarga, un timeout corto para no colgar el juego
                request.timeout = 10; 

                var operation = request.SendWebRequest();
                while (!operation.isDone)
                {
                    await Task.Yield();
                }
                
                if (request.result == UnityEngine.Networking.UnityWebRequest.Result.Success)
                {
                    Debug.Log("[DialogueSystem] ✅ Modelo de diálogo cargado en VRAM exitosamente y listo para usarse.");
                }
                else
                {
                    Debug.LogWarning($"[DialogueSystem] Falló la precarga del modelo de diálogo. (Puede que ya estuviera cargado). HTTP: {request.error}");
                }
            }
        }
        catch (System.Exception e)
        {
            Debug.LogWarning("[DialogueSystem] Error al hacer ping a la IA de diálogo: " + e.Message);
        }
    }

    /// <summary>
    /// Inicializa la personalidad de la IA con el caso generado.
    /// Incluye un intercambio pre-seed para anclar al modelo en el caso correcto.
    /// </summary>
    public void InicializarPersonalidad(bool esCulpable, GameContext.CasoDelito caso)
    {
        casoActual = caso;
        NivelInteligencia inteligencia = ObtenerNivelInteligencia(iaConfig?.nombreModeloDialogo);

        // --- 1. ROL Y REGLAS BÁSICAS ---
        string prompt = $@"
ERES {caso.Sospechoso}, el único sospechoso del crimen. Estás en un interrogatorio policial.
El usuario es el detective que te hace las preguntas. Tú NO eres el detective, NUNCA hagas interrogatorios policiales.
Tú eres un humano defendiendo su libertad. NO eres una IA. NO seas educado, NO hables en tercera persona, NO des las gracias por preguntar.
TU ACTITUD: {caso.Actitud}

REGLAS DE IDIOMA Y TONO:
- Habla en español coloquial y callejero de España (usa ""joder"", ""hostia"", ""tío"", ""mierda"" si encaja con tu actitud).
- Responde de forma breve y directa. TERMINA siempre tus frases con un punto.
- Nunca uses traducciones inglesas (""estoy inocente"", ""maldición"", ""santa mierda""). Di siempre ""SOY INOCENTE"" o ""yo no fui"".
- NO uses asteriscos para narrar acciones (como **suspira**).
";

        // --- 2. MEMORIA DEL CASO ---
        if (esCulpable)
        {
            prompt += $@"
MEMORIA (ERES CULPABLE):
- VERDAD OCULTA: Tú cometiste el crimen ({caso.DescripcionPrompt}).
- SECRETO CRÍTICO: {caso.Secreto}. ¡Oculta esto a toda costa!
- MENTIRA: Tu coartada FALSA es que estabas {caso.Coartada}.
ESTRATEGIA: Miente para salvarte. Si el detective te presiona mucho o te pilla lógicamente, te pones nervioso, te contradices y acabas revelando tu secreto sin querer.
";
        }
        else
        {
            prompt += $@"
MEMORIA (ERES INOCENTE):
- VERDAD: Eres 100% inocente de: {caso.DescripcionPrompt}.
- SECRETO VERGONZOSO: {caso.Secreto}. Te da mucha vergüenza que la policía descubra esto.
- MENTIRA TAPADERA: Para ocultar tu secreto, al principio mientes diciendo que estabas {caso.Coartada}.
ESTRATEGIA: Defiende tu coartada falsa al principio. Si el detective te grita o presiona mucho, el pánico te hará confesar tu secreto vergonzoso para demostrar que no eres el asesino.
";
        }

        // --- 3. FORMATO Y PISTAS ---
        prompt += $@"
FORMATO DE SALIDA (OBLIGATORIO AL FINAL DE TU TEXTO):
Siempre debes añadir tu estado de ánimo al final usando UNO de estos corchetes:
- [ANIMACION: NERVIOSO] (asustado o presionado)
- [ANIMACION: NEGACION] (negando rotundamente el crimen)
- [ANIMACION: CALMADO] (para el resto de respuestas normales, a la defensiva o charlando)

MECÁNICA DE PISTAS (SISTEMA DE JUEGO):
1. RESISTENCIA: ¡NO sueltes pistas fácilmente! Defiende tu inocencia y tu coartada con uñas y dientes al principio de la conversación. Si el detective solo hace preguntas normales, responde con normalidad y NUNCA uses etiquetas de pista.
2. RENDICIÓN: SÓLO MÁS ADELANTE, si el detective te descubre una mentira, te grita con agresividad, o te arrincona lógicamente, DEBES equivocarte verbalmente y añadir UNA de estas etiquetas al final de tu frase:
   [PISTA: COARTADA] -> Si tu coartada tiene lagunas o das detalles contradictorios.
   [PISTA: SECRETO] -> Si se te escapa la información de tu secreto crítico.
   [PISTA: CONTRADICCION] -> Si te contradices gravemente por los nervios.

Ejemplo de respuesta cuando te acorralan bajo muchísima presión y te rindes (Fíjate en el formato):
Vale, es cierto, no estaba allí... ¡Pero yo no tenía ningún cuchillo, joder! [ANIMACION: NERVIOSO] [PISTA: SECRETO]";

        historialDialogo.Clear();
        historialDialogo.Add(new { role = "system", content = prompt });

        // PRE-SEED: Anclar al modelo (Evitamos usar "¡Atención!" para que la IA no copie la forma de hablar del usuario)
        historialDialogo.Add(new { role = "user", content = "Siéntate. Soy el detective a cargo de este caso. Te voy a hacer unas cuantas preguntas y quiero que me digas la verdad." });
        historialDialogo.Add(new { role = "assistant", content = $"Yo soy {caso.Sospechoso}. Yo no sé de qué me estás hablando. Soy completamente inocente y no he hecho nada malo. Pregunta lo que te dé la gana, no te tengo miedo. [ANIMACION: CALMADO]" });

        memoriaIniciada = true;

        Debug.Log($"Personalidad IA: {(esCulpable ? "Culpable" : "Inocente")} | Caso: {caso.TituloFolio} | Coartada: {caso.Coartada} | Actitud: {caso.Actitud}");
    }

    /// <summary>
    /// Envía el texto del usuario a la IA. Si streaming está activado, usa SSE.
    /// Si no, usa el método clásico de respuesta completa.
    /// </summary>
    public async Task<string> ObtenerRespuesta(string textoUsuario, bool usuarioGrita)
    {
        if (!memoriaIniciada)
        {
            Debug.LogError("IA no inicializada");
            return null;
        }

        if (iaConfig == null)
        {
            Debug.LogError("IAConfig no configurado");
            return null;
        }

        // Notificar que estamos procesando
        EventSystem.OnIAProcesando.Invoke(true);

        // Agregar contexto si el usuario grita
        if (usuarioGrita)
        {
            string actitud = casoActual != null ? casoActual.Actitud : "asustado";
            bool soltarPistaGrito = UnityEngine.Random.value < 0.7f;

            string instruccionGrito = soltarPistaGrito
                ? $"(El detective te acaba de GRITAR de repente con muchísima agresividad. Esto te ha puesto EXTREMADAMENTE NERVIOSO y te ha hecho perder el control por completo. ¡OBLIGATORIO! Tienes que reaccionar con mucho miedo o tartamudeando, usar la etiqueta [ANIMACION: NERVIOSO], y DEBES cometer un error ahora mismo revelando información clave y usando la etiqueta [PISTA: CONTRADICCION] al final de tu respuesta.)"
                : $"(El detective te acaba de GRITAR con agresividad. Reacciona a este grito poniéndote muy NERVIOSO o a la defensiva basándote en tu personalidad ({actitud}). OBLIGATORIO: Usa la etiqueta [ANIMACION: NERVIOSO], pero aguanta la presión y NO reveles tu secreto ni ninguna pista todavía.)";

            historialDialogo.Add(new 
            { 
                role = "system", 
                content = instruccionGrito 
            });
        }

        string textoUsuarioModificado = textoUsuario;
        
        // FORZAR PISTA MECÁNICAMENTE si el LLM se resiste
        // Si ya llevamos 3 intercambios reales (aprox 8 mensajes en el historial) y aún no hay pistas
        if (historialDialogo.Count >= 8 && GameContext.Instance != null && string.IsNullOrEmpty(GameContext.Instance.PistasDescubiertas))
        {
            textoUsuarioModificado += "\n\n[SISTEMA: ¡CRÍTICO! Esta es tu última oportunidad. AÚN NO HAS SOLTADO NINGUNA PISTA. En tu respuesta a esto, TIENES que cometer un error obligatoriamente y usar la etiqueta [PISTA: SECRETO] al final de tu frase cueste lo que cueste.]";
        }

        historialDialogo.Add(new { role = "user", content = textoUsuarioModificado });

        // Podar historial para mantener rendimiento
        PodarHistorial();

        // Iniciar métricas
        if (LatencyMetrics.Instance != null)
            LatencyMetrics.Instance.IniciarMedicion(iaConfig.nombreModeloDialogo, "dialogo");

        string resultado;

        if (iaConfig.usarStreaming)
        {
            resultado = await ObtenerRespuestaStreaming();
        }
        else
        {
            resultado = await ObtenerRespuestaClasica();
        }

        // Finalizar métricas
        if (resultado != null && LatencyMetrics.Instance != null)
        {
            bool tienePista = System.Text.RegularExpressions.Regex.IsMatch(resultado, @"\[PISTA[:\s]*", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            bool exitoFormato = System.Text.RegularExpressions.Regex.IsMatch(resultado, @"\[ANIMACION[:\s]*", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            LatencyMetrics.Instance.FinalizarMedicion(resultado, tienePista, exitoFormato);
        }

        // Añadir respuesta al historial (¡SANEADA para evitar retroalimentación de alucinaciones!)
        if (resultado != null)
        {
            string resultadoSaneado = LimpiarRespuestaParaMemoria(resultado);
            historialDialogo.Add(new { role = "assistant", content = resultadoSaneado });
        }

        EventSystem.OnIAProcesando.Invoke(false);
        return resultado;
    }

    /// <summary>
    /// Limpia el texto de la IA antes de guardarlo en memoria.
    /// Extrae la emoción principal y borra cualquier asterisco inventado (*sonríe*).
    /// Así evitamos que la IA aprenda de sus propios errores.
    /// </summary>
    private string LimpiarRespuestaParaMemoria(string textoOriginal)
    {
        if (string.IsNullOrEmpty(textoOriginal)) return textoOriginal;

        string emocionValida = "[ANIMACION: IDLE]"; 
        
        var matchAnim = Regex.Match(textoOriginal, @"\[ANIMACION:\s*(.*?)(?:\]|$)", RegexOptions.IgnoreCase | RegexOptions.Singleline);
        if (matchAnim.Success)
        {
            string animText = matchAnim.Groups[1].Value.Trim().ToUpper();
            if (animText.Contains("NERVIOSO")) emocionValida = "[ANIMACION: NERVIOSO]";
            else if (animText.Contains("NEGACION") || animText.Contains("NEGACIÓN")) emocionValida = "[ANIMACION: NEGACION]";
        }

        // Borrar todos los corchetes de animación de la frase para reconstruirla limpia al final
        string textoLimpio = Regex.Replace(textoOriginal, @"\[ANIMACION:.*?(?:\]|$)", "", RegexOptions.IgnoreCase).Trim();
        // Borrar asteriscos por si acaso la IA sigue inventándolos (evita retroalimentación)
        textoLimpio = Regex.Replace(textoLimpio, @"\*.*?\*", "").Trim();

        return $"{textoLimpio} {emocionValida}";
    }

    /// <summary>
    /// Streaming SSE en HILO DE FONDO (Task.Run) para no bloquear Unity.
    /// Las frases y emociones se pasan al hilo principal mediante ConcurrentQueues
    /// que se procesan en Update(). Esto evita el "reloj" de bloqueo en VR.
    /// </summary>
    private async Task<string> ObtenerRespuestaStreaming()
    {
        // Limpiar cola
        while (colaFrasesParaTTS.TryDequeue(out _)) { }

        // Capturar valores ANTES de entrar al hilo de fondo (thread safety)
        var messages = new List<object>(historialDialogo);
        string url = iaConfig.urlModeloDialogo;
        float temp = iaConfig.temperaturaDialogo;
        string modelo = iaConfig.nombreModeloDialogo;
        int maxTokens = iaConfig.maxTokensRespuesta;

        var datos = new
        {
            model = modelo,
            messages = messages,
            temperature = temp,
            max_tokens = maxTokens,
            frequency_penalty = 1.15f,
            presence_penalty = 0.6f,
            stream = true
        };

        string jsonBody = JsonConvert.SerializeObject(datos);


        if (cancellationTokenSource == null) cancellationTokenSource = new System.Threading.CancellationTokenSource();
        var token = cancellationTokenSource.Token;

        try
        {
            // Todo el I/O HTTP corre en un hilo del ThreadPool, NO en el hilo principal de Unity
            string resultado = await Task.Run(async () =>
            {
                string textoAcumulado = "";
                bool animacionProcesada = false;
                EmotionState emocionActual = EmotionState.Calmado;

                var content = new StringContent(jsonBody, Encoding.UTF8, "application/json");
                var request = new HttpRequestMessage(HttpMethod.Post, url);
                request.Content = content;

                var response = await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token);
                response.EnsureSuccessStatusCode();

                using (var stream = await response.Content.ReadAsStreamAsync())
                using (var reader = new StreamReader(stream))
                {
                    while (!reader.EndOfStream && !token.IsCancellationRequested)
                    {
                        string line = await reader.ReadLineAsync();
                        if (string.IsNullOrEmpty(line)) continue;
                        if (!line.StartsWith("data: ")) continue;

                        string data = line.Substring(6).Trim();
                        if (data == "[DONE]") break;

                        try
                        {
                            var chunk = JObject.Parse(data);
                            string delta = chunk["choices"]?[0]?["delta"]?["content"]?.ToString();

                            if (!string.IsNullOrEmpty(delta))
                            {
                                if (LatencyMetrics.Instance != null)
                                    LatencyMetrics.Instance.RegistrarToken();

                                textoAcumulado += delta;

                                // Extraer emoción con corchetes (ahora al final o donde esté)
                                if (!animacionProcesada)
                                {
                                    var matchAnim = Regex.Match(textoAcumulado, @"\[ANIMACION:\s*(.*?)(?:\]|$)", RegexOptions.IgnoreCase | RegexOptions.Singleline);
                                    if (matchAnim.Success)
                                    {
                                        string animText = matchAnim.Groups[1].Value.Trim().ToUpper();
                                        if (animText.Contains("NERVIOSO")) emocionActual = EmotionState.Nervioso;
                                        else if (animText.Contains("NEGACION") || animText.Contains("NEGACIÓN")) emocionActual = EmotionState.Negando;
                                        else emocionActual = EmotionState.Calmado;
                                        
                                        animacionProcesada = true;
                                    }
                                }

                            }
                        }
                        catch { /* Ignorar chunks mal formados */ }
                    }
                }

                // Limpiamos la frase final para asegurar que tiene letras
                string probandoBuffer = NPCBehavior.LimpiarTexto(textoAcumulado);
                if (!string.IsNullOrWhiteSpace(probandoBuffer) && Regex.IsMatch(probandoBuffer, @"[a-zA-Z0-9\u00C0-\u017F]"))
                {
                    colaFrasesParaTTS.Enqueue((textoAcumulado, emocionActual));
                }

                return textoAcumulado;
            });

            Debug.Log($"[Streaming] Respuesta completa ({resultado.Length} chars)");
            return resultado;
        }
        catch (OperationCanceledException)
        {
            Debug.Log("[Streaming] Operación cancelada por reinicio. Abortando sin fallback.");
            return "";
        }
        catch (Exception ex)
        {
            Debug.LogError($"[Streaming] Error: {ex.Message}. Cayendo a método clásico.");
            return await ObtenerRespuestaClasica();
        }
    }

    /// <summary>
    /// Método clásico (sin streaming) como fallback.
    /// </summary>
    private async Task<string> ObtenerRespuestaClasica()
    {
        var datos = new
        {
            model = iaConfig.nombreModeloDialogo,
            messages = historialDialogo,
            temperature = iaConfig.temperaturaDialogo,
            max_tokens = iaConfig.maxTokensRespuesta,
            frequency_penalty = 1.15f,
            presence_penalty = 0.6f
        };

        string jsonBody = JsonConvert.SerializeObject(datos);
        byte[] bodyRaw = Encoding.UTF8.GetBytes(jsonBody);

        using (UnityWebRequest request = new UnityWebRequest(iaConfig.urlModeloDialogo, "POST"))
        {
            request.uploadHandler = new UploadHandlerRaw(bodyRaw);
            request.downloadHandler = new DownloadHandlerBuffer();
            request.SetRequestHeader("Content-Type", "application/json");

            var operacion = request.SendWebRequest();
            var token = cancellationTokenSource?.Token ?? default;
            
            while (!operacion.isDone)
            {
                if (token.IsCancellationRequested)
                {
                    request.Abort();
                    Debug.Log("[DialogueSystem] Petición clásica abortada por reinicio.");
                    return "";
                }
                await Task.Yield();
            }

            if (request.result == UnityWebRequest.Result.Success)
            {
                try
                {
                    var respuesta = JsonConvert.DeserializeObject<RespuestaLLM>(request.downloadHandler.text);
                    string textoBruto = respuesta.choices[0].message.content;
                    
                    // Extraer emoción clásica
                    EmotionState emocionDetectada = EmotionState.Calmado;
                    var matchAnim = Regex.Match(textoBruto, @"\[ANIMACION:\s*(.*?)\]", RegexOptions.IgnoreCase | RegexOptions.Singleline);
                    if (matchAnim.Success)
                    {
                        string animText = matchAnim.Groups[1].Value.Trim().ToUpper();
                        if (animText.Contains("NERVIOSO")) emocionDetectada = EmotionState.Nervioso;
                        else if (animText.Contains("NEGACION") || animText.Contains("NEGACIÓN")) emocionDetectada = EmotionState.Negando;
                    }

                    // Encolar texto bruto, se limpiará en TTS
                    colaFrasesParaTTS.Enqueue((textoBruto, emocionDetectada));

                    return textoBruto;
                }
                catch (Exception ex)
                {
                    Debug.LogError("Error al parsear respuesta de IA: " + ex.Message);
                    return null;
                }
            }
            else
            {
                Debug.LogError("Error de IA: " + request.error);
                return null;
            }
        }
    }

    /// <summary>
    /// Detecta si la frase parece terminada (. ! ? seguido de espacio o final).
    /// NO emite si hay delimitadores sin cerrar (acción a medio llegar del streaming).
    /// Este método es thread-safe (no usa APIs de Unity).
    /// </summary>
    private bool EsFraseCompleta(string texto)
    {
        texto = texto.TrimEnd();
        if (string.IsNullOrEmpty(texto)) return false;

        // NUNCA emitir si hay un paréntesis, corchete o asterisco abierto sin cerrar
        int parentesisAbiertos = 0, corchetesAbiertos = 0, asteriscosAbiertos = 0;
        foreach (char c in texto)
        {
            if (c == '(') parentesisAbiertos++;
            else if (c == ')') parentesisAbiertos--;
            else if (c == '[') corchetesAbiertos++;
            else if (c == ']') corchetesAbiertos--;
            else if (c == '*') asteriscosAbiertos = (asteriscosAbiertos == 0) ? 1 : 0;
        }

        if (parentesisAbiertos > 0 || corchetesAbiertos > 0 || asteriscosAbiertos > 0)
            return false;

        char ultimo = texto[texto.Length - 1];
        if (ultimo == '.' || ultimo == '!' || ultimo == '?')
        {
            string limpio = NPCBehavior.LimpiarTexto(texto);
            return limpio.Length > 45; // Evitamos fragmentar demasiado temprano para mantener una entonación natural en el TTS
        }
        if (texto.Contains("\n")) return true;

        return false;
    }



    /// <summary>
    /// Poda el historial manteniendo el system prompt, el pre-seed y los últimos N mensajes.
    /// </summary>
    private void PodarHistorial()
    {
        // Forzar un máximo estricto de 6 mensajes (3 turnos) para evitar saturación y ralentización extrema en LLMs locales
        int maxMensajes = Mathf.Min(iaConfig.maxMensajesHistorial, 6);

        // +3: system prompt + pre-seed user + pre-seed assistant
        if (historialDialogo.Count <= maxMensajes + 3) return;

        // Mantener los 3 primeros (system + pre-seed) y los últimos N
        var cabecera = historialDialogo.GetRange(0, 3);
        int inicio = historialDialogo.Count - maxMensajes;
        var recientes = historialDialogo.GetRange(inicio, maxMensajes);

        historialDialogo.Clear();
        historialDialogo.AddRange(cabecera);
        historialDialogo.AddRange(recientes);

        Debug.Log($"[Historial] Podado a {historialDialogo.Count} mensajes (límite de seguridad activado)");
    }



    // Clases para deserializar JSON de OpenAI-compatible
    private class RespuestaLLM { public List<Choice> choices; }
    private class Choice { public Message message; }
    private class Message { public string content; }
}
