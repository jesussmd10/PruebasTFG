using UnityEngine;
using UnityEngine.Networking;
using System.Collections;
using System.Text;
using Newtonsoft.Json;


public class AudioManager : MonoBehaviour
{
    [SerializeField] private AudioSource audioSource;
    [SerializeField] private string apiKey = "0f4d01eb7b2cf29c590a32b6d4f031b721f684b194a5ca0f39131cece6270072";
    [SerializeField] private string voiceId = "2EiwWnXFnvU5JabPnv8n";

    private const string ELEVENLABS_URL = "https://api.elevenlabs.io/v1/text-to-speech/{0}";
    private const int MAX_REINTENTOS = 3;

    private void Start()
    {
        if (audioSource == null)
        {
            audioSource = GetComponent<AudioSource>();
        }
    }

    public void ReproducirTexto(string texto)
    {
        StartCoroutine(DescargarYReproducirConReintentos(texto, 0));
    }

    private IEnumerator DescargarYReproducirConReintentos(string texto, int intento)
    {
        if (string.IsNullOrEmpty(texto))
        {
            Debug.LogWarning("Texto vacío para reproducir");
            yield break;
        }

       
        string idLimpio = voiceId.Trim();
        string keyLimpia = apiKey.Trim();

        
        if (string.IsNullOrEmpty(idLimpio))
        {
            Debug.LogError("❌ El Voice ID está vacío. Escríbelo en el Inspector de Unity.");
            yield break;
        }

        
        string url = string.Format(ELEVENLABS_URL, idLimpio);

        var datos = new
        {
            text = texto,
            model_id = "eleven_multilingual_v2",
            voice_settings = new { stability = 0.3f, similarity_boost = 0.8f }
        };

        string json = JsonConvert.SerializeObject(datos);
        byte[] bodyRaw = Encoding.UTF8.GetBytes(json);

        using (UnityWebRequest www = new UnityWebRequest(url, "POST"))
        {
            www.uploadHandler = new UploadHandlerRaw(bodyRaw);
            www.downloadHandler = new DownloadHandlerAudioClip(url, AudioType.MPEG);
            www.SetRequestHeader("Content-Type", "application/json");
            www.SetRequestHeader("xi-api-key", keyLimpia);

            yield return www.SendWebRequest();

            if (www.result == UnityWebRequest.Result.Success)
            {
                AudioClip clip = DownloadHandlerAudioClip.GetContent(www);
                audioSource.clip = clip;
                audioSource.Play();
                Debug.Log("🔊 Audio reproduciendo");
            }
            else    
            {
                if (intento < MAX_REINTENTOS)
                {
                    Debug.LogWarning($"Error de voz. Reintentando ({intento + 1}/{MAX_REINTENTOS})...");
                    yield return new WaitForSeconds(1f);
                    yield return DescargarYReproducirConReintentos(texto, intento + 1);
                }
                else
                {
                    Debug.LogError("Error de Voz después de reintentos: " + www.error);
                }
            }
        }
    }
}