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
            Debug.LogError("IAConfig no asignado");
        }
    }

    private readonly string[] lugaresAleatorios = new string[] 
    { 
        "en un bar local tomando algo solo", 
        "paseando a tu perro por un parque cercano", 
        "trabajando hasta tarde en tu oficina", 
        "en casa de un amigo jugando a la consola", 
        "durmiendo en tu coche tras discutir con tu pareja",
        "haciendo la compra en el supermercado nocturno",
        "cenando solo en un restaurante de comida rápida"
    };

    private readonly string[] actitudesAleatorias = new string[]
    {
        "Estás aterrado, tartamudeas mucho y casi lloras.",
        "Te muestras a la defensiva, un poco borde e indignado de estar allí.",
        "Intentas hacerte el simpático y usas sarcasmo para ocultar tu enorme nerviosismo.",
        "Hablas excesivamente rápido, dando detalles inútiles porque entraste en pánico.",
        "Eres muy tímido, respondes con frases cortísimas y *miras mucho al suelo*."
    };

    /// <summary>
    /// Inicializa la personalidad de la IA según si es culpable o inocente, asignando una coartada aleatoria.
    /// </summary>
    public void InicializarPersonalidad(bool esCulpable)
    {
        string lugar = lugaresAleatorios[Random.Range(0, lugaresAleatorios.Length)];
        string actitud = actitudesAleatorias[Random.Range(0, actitudesAleatorias.Length)];
        var delito = GameContext.Instance.DelitoActual;

        string prompt = $"Eres Alex, principal sospechoso de {delito.DescripcionPrompt}, en la sala de interrogatorios de la comisaría. {actitud} ";
        prompt += "Responde de forma natural y fluida. ES OBLIGATORIO Y ESTRICTAMENTE NECESARIO que SIEMPRE incluyas (lenguaje corporal entre paréntesis) al principio o en medio de tus frases. ";
        prompt += "ATENCIÓN: DEBES usar EXACTAMENTE algunas de las siguientes palabras clave dentro de tus paréntesis para que el sistema reconozca tu estado físico: ";
        prompt += "Si el detective te acusa de algo y tú le contradices directamente o rechazas fuertemente su teoría: (niega, mueve la cabeza, rechaza). NO uses estas palabras para un simple 'no' (ej: 'no sé'), SOLO úsalas cuando me estés llevando la contraria o defendiendo tu coartada. ";
        prompt += "Si tienes pánico: (tiembla, suda, muy nervioso, se asusta, tartamudea). ";
        prompt += "Si logras relajarte un poco: (se calma, respira, suspira, se relaja). ";
        prompt += "Sin estas palabras exactas entre paréntesis, el sistema fallará. Úsalas de forma natural pero constante. ";

        if (esCulpable)
        {
            prompt += $"\n\n¡ERES CULPABLE del crimen! Tu coartada FALSA inventada es que estabas {lugar}. ";
            prompt += "Tratas de mantener tu mentira con firmeza, pero bajo mucha presión de las preguntas te pones muy nervioso, tu historia tiene huecos, e intentas cambiar de tema o inventar detalles al vuelo vacilando. ";
            if (iaConfig != null && !string.IsNullOrEmpty(iaConfig.promptCulpable)) 
                prompt += iaConfig.promptCulpable + " ";
        }
        else
        {
            prompt += $"\n\n¡ERES TOTALMENTE INOCENTE! No sabes nada. Tu coartada REAL, verdadera y comprobable es que estabas {lugar}. ";
            prompt += "Tienes muchísimo miedo de ir a prisión por un terrible error policial. Dices la verdad continuamente, pero los nervios, el agobio y las preguntas capciosas te aterran y te causan estrés extremo. ";
            if (iaConfig != null && !string.IsNullOrEmpty(iaConfig.promptInocente)) 
                prompt += iaConfig.promptInocente + " ";
        }

        prompt += $"\n\nMUY IMPORTANTE: Defiende tu coartada de que estabas: {lugar}. Sin embargo, NO te limites a repetir tu historia como un robot. NUNCA uses frases como 'como digo siempre', 'como ya te he dicho', o 'vuelvo a repetir'. Habla de forma natural, inventando detalles nuevos si hace falta. Si el policía te acorrala o te repite preguntas, DEBES perder los estribos, enfadarte, ponerte a la defensiva, usar sarcasmo e incluso decir palabrotas.";
        prompt += $"\n\nREGLA OBLIGATORIA DEL SISTEMA DE JUEGO: Añade el tag {iaConfig.tagPista} al FINAL de tu respuesta ÚNICAMENTE de forma casual y puntual cuando ocurra una de estas cosas: 1) Te contradices a ti mismo sin querer. 2) Revelas un dato clave o detalle jugoso por culpa del estrés. 3) El policía te pilla en una mentira evidente. NO repitas la pista constantemente, guárdala solo para los momentos donde cometas un error en tu testimonio. Ejemplo: '¡Que te jodan, yo no estuve ahí! Bueno, sí pasé cerca para ver a otra persona... {iaConfig.tagPista}'. ";
        
        if (esCulpable) {
            prompt += "Como eres culpable, generarás este tag [PISTA] cuando te pongas nervioso y accidentalmente reveles un fallo en tu mentira o cambies de versión.";
        } else {
            prompt += "Como eres inocente, generarás este tag [PISTA] cuando el pánico te haga dudar de tus propios recuerdos o digas algo muy extraño y sospechoso sin querer.";
        }

        historialDialogo.Clear();
        historialDialogo.Add(new { role = "system", content = prompt });
        memoriaIniciada = true;

        Debug.Log($"Personalidad IA: {(esCulpable ? "Culpable" : "Inocente")} | Coartada: {lugar} | Actitud: {actitud}");
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
