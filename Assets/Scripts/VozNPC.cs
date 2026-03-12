using UnityEngine;
using UnityEngine.Networking;
using System.Collections;
using System.Text;
using Newtonsoft.Json;

public class VozNPC : MonoBehaviour
{
    public AudioSource garganta;
    
    
    private string apiKey = "0f4d01eb7b2cf29c590a32b6d4f031b721f684b194a5ca0f39131cece6270072"; 
    
  
    private string voiceId = "2EiwWnXFnvU5JabPnv8n"; // Voz en español de Alex

    public void DiLaFrase(string texto)
    {
        StartCoroutine(DescargarYReproducir(texto));
    }

    IEnumerator DescargarYReproducir(string texto)
    {
        // Prepara la petición
        string url = "https://api.elevenlabs.io/v1/text-to-speech/" + voiceId;
        
        
        var datos = new
        {
            text = texto,
            model_id = "eleven_multilingual_v2", // Modelo que habla español perfecto
            voice_settings = new { stability = 0.3f, similarity_boost = 0.8f }
        };

        string json = JsonConvert.SerializeObject(datos);
        byte[] bodyRaw = Encoding.UTF8.GetBytes(json);

        // Enviar a ElevenLabs
        using (UnityWebRequest www = new UnityWebRequest(url, "POST"))
        {
            www.uploadHandler = new UploadHandlerRaw(bodyRaw);
            www.downloadHandler = new DownloadHandlerAudioClip(url, AudioType.MPEG);
            www.SetRequestHeader("Content-Type", "application/json");
            www.SetRequestHeader("xi-api-key", apiKey);

            Debug.Log("Generando voz...");
            yield return www.SendWebRequest();

            if (www.result == UnityWebRequest.Result.Success)
            {
                // Reproducir sonido
                AudioClip clip = DownloadHandlerAudioClip.GetContent(www);
                garganta.clip = clip;
                garganta.Play();
                
            
                Debug.Log("Alex está hablando.");
            }
            else
            {
                Debug.LogError("❌ Error de Voz: " + www.error);
            }
        }
    }
}