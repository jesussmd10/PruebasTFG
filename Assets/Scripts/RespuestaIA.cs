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

    private List<object> historialDialogo = new List<object>();
    private bool memoriaIniciada = false;

    // --- EL GAMEMANAGER LLAMA A ESTO AL EMPEZAR ---
    public void ConfigurarPersonalidadInicial(bool esCulpable)
    {
        string promptInicial = "Eres Alex, un sospechoso en una sala de interrogatorios. Estás muy nervioso. Responde frases cortas. Usa acciones entre asteriscos como *tiembla* o *nervioso* para expresar emociones. ";

        if (esCulpable)
        {
            promptInicial += "ERES CULPABLE. Robaste la joyería, pero debes mentir e inventarte excusas para que no te pillen. Si el detective te grita, te pondrás muy nervioso y podrías cometer un error. ";
        }
        else
        {
            promptInicial += "ERES INOCENTE. Estabas en el cine a la hora del robo viendo 'Dune', pero estás aterrado de que te metan en la cárcel por error. Defiende tu inocencia. ";
        }

        promptInicial += "REGLA SECRETA: Si durante el interrogatorio el detective te pilla en una mentira, revelas una pista clave, o un nombre importante, añade EXACTAMENTE la palabra [PISTA] al final de tu respuesta.";

        historialDialogo.Add(new { role = "system", content = promptInicial });
        memoriaIniciada = true;
    }

    public async void ProcesarInterrogatorio(string textoUsuario, bool usuarioGrita)
    {
        if (!memoriaIniciada) return; // Esperamos a que el GameManager configure la IA

        Debug.Log($"VOZ: '{textoUsuario}' |  GRITANDO: {usuarioGrita}");

        if (usuarioGrita)
        {
            historialDialogo.Add(new { role = "system", content = "(El detective te acaba de GRITAR. Asústate mucho, tartamudea y tiembla)" });
        }

        historialDialogo.Add(new { role = "user", content = textoUsuario });

        var datos = new
        {
            model = nombreModelo,
            messages = historialDialogo,
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

                // --- DETECTAR SI LA IA NOS HA DADO UNA PISTA ---
                if (textoBruto.Contains("[PISTA]"))
                {
                    GameManager gm = FindObjectOfType<GameManager>();
                    if (gm != null) gm.AñadirPista("El sospechoso se ha contradicho o ha revelado un dato clave.");

                    // Borramos la palabra para que ElevenLabs no la lea en voz alta
                    textoBruto = textoBruto.Replace("[PISTA]", "");
                }

                historialDialogo.Add(new { role = "assistant", content = textoBruto });
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