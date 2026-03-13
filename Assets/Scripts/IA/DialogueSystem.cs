using UnityEngine;
using UnityEngine.Networking;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;
using System.Collections.Generic;


public class DialogueSystem : MonoBehaviour
{
    [SerializeField] private IAConfig iaConfig;
    private List<object> historialDialogo = new List<object>();
    private bool memoriaIniciada = false;

    private void Start()
    {
        if (iaConfig == null)
        {
            Debug.LogError("❌ IAConfig no asignado");
        }
    }

    /// <summary>
    /// Inicializa la personalidad de la IA según si es culpable o inocente
    /// </summary>
    public void InicializarPersonalidad(bool esCulpable)
    {
        string prompt = "Eres Alex, un sospechoso en una sala de interrogatorios. Muy nervioso. ";
        prompt += "Responde con frases cortas. Usa *acciones entre asteriscos* para expresar emociones. ";

        if (esCulpable)
        {
            prompt += iaConfig.promptCulpable;
        }
        else
        {
            prompt += iaConfig.promptInocente;
        }

        prompt += $"\nREGLA SECRETA: Si revelas pista importante, añade EXACTAMENTE: {iaConfig.tagPista}";

        historialDialogo.Clear();
        historialDialogo.Add(new { role = "system", content = prompt });
        memoriaIniciada = true;

        Debug.Log(" Personalidad de IA inicializada");
    }

    /// <summary>
    /// Envía el texto del usuario a la IA y obtiene respuesta
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

        // Agregar contexto si el usuario grita
        if (usuarioGrita)
        {
            historialDialogo.Add(new 
            { 
                role = "system", 
                content = "(El detective te acaba de GRITAR. Asústate mucho, tartamudea y tiembla)" 
            });
        }

        historialDialogo.Add(new { role = "user", content = textoUsuario });

        var datos = new
        {
            model = iaConfig.nombreModelo,
            messages = historialDialogo,
            temperature = iaConfig.temperatura
        };

        string jsonBody = JsonConvert.SerializeObject(datos);
        byte[] bodyRaw = Encoding.UTF8.GetBytes(jsonBody);

        using (UnityWebRequest request = new UnityWebRequest(iaConfig.urlModelo, "POST"))
        {
            request.uploadHandler = new UploadHandlerRaw(bodyRaw);
            request.downloadHandler = new DownloadHandlerBuffer();
            request.SetRequestHeader("Content-Type", "application/json");

            var operacion = request.SendWebRequest();
            while (!operacion.isDone) await Task.Yield();

            if (request.result == UnityWebRequest.Result.Success)
            {
                try
                {
                    var respuesta = JsonConvert.DeserializeObject<RespuestaLLM>(request.downloadHandler.text);
                    string textoBruto = respuesta.choices[0].message.content;

                    historialDialogo.Add(new { role = "assistant", content = textoBruto });
                    return textoBruto;
                }
                catch (System.Exception ex)
                {
                    Debug.LogError("Error al parsear respuesta de IA: " + ex.Message);
                    return null;
                }
            }
            else
            {
                Debug.LogError(" Error de IA: " + request.error);
                return null;
            }
        }
    }

    // Clases para deserializar JSON de OpenAI-compatible
    private class RespuestaLLM { public List<Choice> choices; }
    private class Choice { public Message message; }
    private class Message { public string content; }
}
