using UnityEngine;
using UnityEngine.Networking;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;
using System.Collections.Generic;
using System.Text.RegularExpressions;

public class RespuestaIA : MonoBehaviour
{
    public VozNPC sistemaDeVoz;
    public CuerpoSospechoso cuerpoAlex;

    private string url = "http://localhost:1234/v1/chat/completions";
    private string nombreModelo = "gemma-2-2b-it"; 

    // ESTA ES LA MEMORIA DE LA CONVERSACIÓN
    private List<object> historialDialogo = new List<object>(); 
    private bool memoriaIniciada = false;

    public async void ProcesarInterrogatorio(string textoUsuario, bool usuarioGrita)
    {
        Debug.Log($"VOZ: '{textoUsuario}' |  GRITANDO: {usuarioGrita}");

        // INICIAR MEMORIA SI ES LA PRIMERA VEZ
        if (!memoriaIniciada)
        {
            string promptInicial = "Eres Alex, un sospechoso de robo inocente pero muy miedoso. " +
                                   "Tu coartada es que estabas en el cine viendo 'Dune'. " +
                                   "Usa acciones entre asteriscos como *tiembla* o *nervioso* para expresar emociones. " +
                                   "Responde frases cortas.";
            
            // Añadimos el Prompt del sistema a la memoria
            historialDialogo.Add(new { role = "system", content = promptInicial });
            memoriaIniciada = true;
        }

        // AÑADIR EFECTO DE GRITO DEL USUARIO SI ES NECESARIO
        if (usuarioGrita)
        {
            historialDialogo.Add(new { role = "system", content = "(El detective te acaba de GRITAR. Asústate mucho, tartamudea y tiembla)" });
        }

        // AÑADIR PREGUNTA DEL USUARIO A LA MEMORIA
        historialDialogo.Add(new { role = "user", content = textoUsuario });

        // PREPARAR DATOS PARA ENVIAR (Enviamos TODA la lista)
        var datos = new
        {
            model = nombreModelo,
            messages = historialDialogo, // Enviamos todo el historial
            temperature = 0.7f
        };

        string jsonBody = JsonConvert.SerializeObject(datos);
        
        using (UnityWebRequest request = new UnityWebRequest(url, "POST"))
        {
            byte[] bodyRaw = Encoding.UTF8.GetBytes(jsonBody);
            request.uploadHandler = new UploadHandlerRaw(bodyRaw);
            request.downloadHandler = new DownloadHandlerBuffer();
            request.SetRequestHeader("Content-Type", "application/json");

            var operacion = request.SendWebRequest();
            while (!operacion.isDone) await Task.Yield();

            if (request.result == UnityWebRequest.Result.Success)
            {
                var respuesta = JsonConvert.DeserializeObject<RespuestaLLM>(request.downloadHandler.text);
                string textoBruto = respuesta.choices[0].message.content;

                Debug.Log("RAW: " + textoBruto);

                // GUARDAR LA RESPUESTA DE ALEX EN LA MEMORIA (Para que recuerde lo que dijo él mismo)
                historialDialogo.Add(new { role = "assistant", content = textoBruto });

                // Procesar actuación y voz
                AnalizarYAnimar(textoBruto);

                string textoLimpio = Regex.Replace(textoBruto, @"\*.*?\*", ""); 
                textoLimpio = Regex.Replace(textoLimpio, @"\(.*?\)", "").Trim();
                
                if (sistemaDeVoz != null) sistemaDeVoz.DiLaFrase(textoLimpio);
            }
            else
            {
                Debug.LogError("Error IA: " + request.error);
            }
        }
    }

 void AnalizarYAnimar(string textoCompleto)
    {
        if (cuerpoAlex == null) return;
        
        string textoLow = textoCompleto.ToLower();

        
        if (textoLow.Contains("tiembla") || textoLow.Contains("miedo") || textoLow.Contains("nervioso"))
        {
            cuerpoAlex.PonerNervioso();
        }
        else if (textoLow.Contains("calma") || textoLow.Contains("respira"))
        {
            cuerpoAlex.Calmar();
        }

        
        cuerpoAlex.GestosHablar();
    }

    public class RespuestaLLM { public List<Choice> choices; }
    public class Choice { public Message message; }
    public class Message { public string content; }
}