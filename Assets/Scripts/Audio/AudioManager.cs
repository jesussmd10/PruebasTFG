using UnityEngine;
using UnityEngine.Networking;
using System.Collections;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;
public class AudioManager : MonoBehaviour
{
    [SerializeField] private AudioSource audioSource;
    [SerializeField] private IAConfig iaConfig;
    
    [SerializeField] private string nombreVoz = "es-ES-AlvaroNeural";

    private string outputMp3Path;

    private void Start()
    {
        if (audioSource == null) audioSource = GetComponent<AudioSource>();
        outputMp3Path = Path.Combine(Application.persistentDataPath, "acusado_azure.mp3");
    }

    public void ReproducirTexto(string texto)
    {
        if (iaConfig == null)
        {
            UnityEngine.Debug.LogWarning("IAConfig no asignado en AudioManager. Usando EdgeTTS por defecto.");
            StartCoroutine(GenerarYReproducirEdge(texto));
            return;
        }

        switch (iaConfig.ttsProvider)
        {
            case IAConfig.TTSProvider.OpenAI:
                if (!string.IsNullOrEmpty(iaConfig.ttsApiKey))
                    StartCoroutine(GenerarYReproducirOpenAI(texto));
                else
                    StartCoroutine(GenerarYReproducirEdge(texto));
                break;
            case IAConfig.TTSProvider.ElevenLabs:
                if (!string.IsNullOrEmpty(iaConfig.ttsApiKey))
                    StartCoroutine(GenerarYReproducirElevenLabs(texto));
                else
                    StartCoroutine(GenerarYReproducirEdge(texto));
                break;
            case IAConfig.TTSProvider.EdgeTTS:
            default:
                StartCoroutine(GenerarYReproducirEdge(texto));
                break;
        }
    }

    private IEnumerator GenerarYReproducirEdge(string texto)
    {
        if (string.IsNullOrEmpty(texto)) yield break;

        // Limpiamos las comillas del texto para que no rompan la consola de comandos
        string textoLimpio = texto.Replace("\"", "'");

        // Preparamos el comando para edge-tts
        ProcessStartInfo startInfo = new ProcessStartInfo();
        
        startInfo.FileName = @"C:\Users\Jesus Santacruz\anaconda3\Scripts\edge-tts.exe"; 
        
        // Le pasamos los argumentos directamente
        startInfo.Arguments = $"--voice {nombreVoz} --text \"{textoLimpio}\" --write-media \"{outputMp3Path}\"";
        
        startInfo.UseShellExecute = false;
        startInfo.CreateNoWindow = true;

        Process edgeProcess = new Process();
        edgeProcess.StartInfo = startInfo;
        edgeProcess.Start();

        // Esperamos a que se descargue el audio de Microsoft
        while (!edgeProcess.HasExited)
        {
            yield return null; 
        }

        // Cargamos el MP3 en Unity
        if (File.Exists(outputMp3Path))
        {
            using (UnityWebRequest www = UnityWebRequestMultimedia.GetAudioClip("file://" + outputMp3Path, AudioType.MPEG))
            {
                yield return www.SendWebRequest();

                if (www.result == UnityWebRequest.Result.Success)
                {
                    AudioClip clip = DownloadHandlerAudioClip.GetContent(www);
                    
                    if (audioSource.clip != null) Destroy(audioSource.clip);
                    
                    audioSource.clip = clip;
                    audioSource.Play();
                    UnityEngine.Debug.Log("Azure hablando mediante edge-tts");
                }
                else
                {
                    UnityEngine.Debug.LogError("Error cargando el MP3: " + www.error);
                }
            }
        }
    }

    private IEnumerator GenerarYReproducirOpenAI(string texto)
    {
        if (string.IsNullOrEmpty(texto)) yield break;

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
                if (audioSource.clip != null) Destroy(audioSource.clip);
                audioSource.clip = clip;
                audioSource.Play();
                UnityEngine.Debug.Log("Hablando mediante OpenAI TTS");
            }
            else
            {
                UnityEngine.Debug.LogError("Error en OpenAI TTS: " + request.error);
                // Fallback
                StartCoroutine(GenerarYReproducirEdge(texto));
            }
        }
    }

    private IEnumerator GenerarYReproducirElevenLabs(string texto)
    {
        if (string.IsNullOrEmpty(texto)) yield break;

        string voiceId = string.IsNullOrEmpty(iaConfig.ttsVoiceId) ? "21m00Tcm4TlvDq8ikWAM" : iaConfig.ttsVoiceId; // Rachel default
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
                if (audioSource.clip != null) Destroy(audioSource.clip);
                audioSource.clip = clip;
                audioSource.Play();
                UnityEngine.Debug.Log("Hablando mediante ElevenLabs TTS");
            }
            else
            {
                UnityEngine.Debug.LogError("Error en ElevenLabs TTS: " + request.error);
                // Fallback
                StartCoroutine(GenerarYReproducirEdge(texto));
            }
        }
    }
}