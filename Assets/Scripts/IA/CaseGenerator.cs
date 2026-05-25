using UnityEngine;
using UnityEngine.Networking;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

/// <summary>
/// Genera un caso de interrogatorio completo usando el LLM (Opción A: JSON + validación por reglas).
/// Optimizado para modelos pequeños (1.5B-3B) con one-shot example y parsing tolerante.
/// Si falla, usa un caso hardcodeado como fallback seguro.
/// </summary>
public class CaseGenerator : MonoBehaviour
{
    [SerializeField] private IAConfig iaConfig;

    // Casos de fallback (los originales hardcodeados) - pool ampliado
    private static readonly GameContext.CasoDelito[] casosFallback = new GameContext.CasoDelito[]
    {
        new GameContext.CasoDelito { ID = "042", TituloFolio = "ROBO EN JOYERÍA", DescripcionFolio = "Atraco a mano armada en la joyería central y robo de diamantes.", DescripcionPrompt = "un atraco a mano armada en la joyería del centro donde se robaron diamantes", Coartada = "en un bar local tomando algo solo", Actitud = "Estás aterrado, tartamudeas mucho y casi lloras." },
        new GameContext.CasoDelito { ID = "087", TituloFolio = "ASESINATO", DescripcionFolio = "Homicidio en primer grado en el callejón trasero del club.", DescripcionPrompt = "el brutal asesinato de una persona en un callejón oscuro detrás de una discoteca", Coartada = "paseando a tu perro por un parque cercano", Actitud = "Te muestras a la defensiva, un poco borde e indignado de estar allí." },
        new GameContext.CasoDelito { ID = "104", TituloFolio = "SECUESTRO", DescripcionFolio = "Secuestro y desaparición forzada del hijo de un empresario.", DescripcionPrompt = "el secuestro del hijo de un empresario local pidiendo un rescate millonario", Coartada = "trabajando hasta tarde en tu oficina", Actitud = "Intentas hacerte el simpático y usas sarcasmo para ocultar tu enorme nerviosismo." },
        new GameContext.CasoDelito { ID = "019", TituloFolio = "INCENDIO PROVOCADO", DescripcionFolio = "Incendio intencionado en un edificio de oficinas del centro.", DescripcionPrompt = "haber provocado intencionadamente un incendio que destruyó un edificio de oficinas", Coartada = "en casa de un amigo jugando a videojuegos", Actitud = "Hablas excesivamente rápido, dando detalles inútiles porque entraste en pánico." },
        new GameContext.CasoDelito { ID = "055", TituloFolio = "AGRESIÓN GRAVE", DescripcionFolio = "Asalto con violencia extrema a un transeúnte la pasada noche.", DescripcionPrompt = "haber agredido violentamente a una persona en el parque de madrugada", Coartada = "durmiendo en tu coche tras discutir con tu pareja", Actitud = "Eres muy tímido, respondes con frases cortísimas y miras mucho al suelo." },
        new GameContext.CasoDelito { ID = "092", TituloFolio = "TRÁFICO DE DROGAS", DescripcionFolio = "Venta y distribución ilegal de sustancias en el barrio sur.", DescripcionPrompt = "vender drogas ilegales en el barrio sur de la ciudad", Coartada = "haciendo la compra en el supermercado nocturno", Actitud = "Estás muy nervioso, no paras de mover las manos y de sudar." },
        new GameContext.CasoDelito { ID = "033", TituloFolio = "ESTAFA BANCARIA", DescripcionFolio = "Fraude informático contra clientes de varias entidades bancarias.", DescripcionPrompt = "una estafa informática que robó los ahorros de decenas de personas", Coartada = "cenando con tu madre en su casa", Actitud = "Te muestras frío y calculador, hablas con mucha calma sospechosa." },
        new GameContext.CasoDelito { ID = "071", TituloFolio = "ROBO A MANO ARMADA", DescripcionFolio = "Atraco con arma de fuego a una gasolinera a las 3 de la madrugada.", DescripcionPrompt = "el atraco con pistola a una gasolinera de madrugada", Coartada = "viendo una película solo en el cine", Actitud = "Estás furioso por estar detenido, gritas e insultas constantemente." },
        new GameContext.CasoDelito { ID = "118", TituloFolio = "EXTORSION", DescripcionFolio = "Amenazas y chantaje a un comerciante del barrio para cobrar proteccion.", DescripcionPrompt = "extorsionar y amenazar a comerciantes del barrio para cobrarles proteccion", Coartada = "jugando al futbol con unos amigos en el polideportivo", Actitud = "Sonries de forma arrogante y actuas como si todo fuera una broma." },
        new GameContext.CasoDelito { ID = "066", TituloFolio = "VANDALISMO GRAVE", DescripcionFolio = "Destruccion masiva de mobiliario urbano y vehiculos aparcados.", DescripcionPrompt = "haber destrozado coches y mobiliario urbano en una noche de vandalismo", Coartada = "en casa durmiendo porque tenias fiebre", Actitud = "Pareces confuso y desorientado, como si no entendieras que haces aqui." },
    };

    private static readonly string[] tematicas = new string[]
    {
        "un ASESINATO MACABRO en un callejón oscuro",
        "un CASO ABSURDO sobre el robo de una mascota famosa",
        "un SECUESTRO de un político importante",
        "un CRIMEN BIZARRO en un laboratorio clandestino",
        "un ATRACO VIOLENTO a un banco central",
        "un SABOTAJE en una fábrica de tecnología militar"
    };

    private static readonly string[] actitudes = new string[]
    {
        "Estás aterrado, lloras a lágrima viva y tartamudeas.",
        "Eres frío como el hielo, calculador y un poco psicópata.",
        "Eres arrogante, chulo, y te ríes del detective.",
        "Estás a la defensiva, muy enfadado y ofendido.",
        "Pareces confundido, desorientado y algo torpe.",
        "Estás extremadamente nervioso, sudando y moviendo las manos."
    };

    private static readonly string[] coartadas = new string[]
    {
        "viendo una película solo en el cine de la calle Mayor",
        "comprando leche y cereales en el supermercado 24h",
        "durmiendo en casa de un amigo después de una fiesta",
        "haciendo horas extra en tu oficina, solo frente al ordenador",
        "cenando en un restaurante barato a las afueras de la ciudad",
        "paseando a tu perro por el parque central, sin ver a nadie"
    };

    private static string ObtenerPromptDinamico()
    {
        string tema = tematicas[Random.Range(0, tematicas.Length)];
        string actitud = actitudes[Random.Range(0, actitudes.Length)];
        string coartada = coartadas[Random.Range(0, coartadas.Length)];
        
        return $@"Eres un generador de datos policiales. Debes devolver ÚNICAMENTE un objeto JSON válido.
No escribas introducciones, no uses markdown. SOLO JSON. Escribe en perfecto español.

INSTRUCCIONES DE GENERACIÓN:
Tienes que crear los detalles de este caso concreto:
- CRIMEN OBLIGATORIO: {tema}
- COARTADA DEL SOSPECHOSO: {coartada}
- ACTITUD DEL SOSPECHOSO: {actitud}

El JSON debe tener EXACTAMENTE este formato y claves:
{{
  ""id"": ""123"",
  ""titulo"": ""TÍTULO DEL CRIMEN EN MAYÚSCULAS"",
  ""descripcionFolio"": ""Resumen de una línea del delito para el expediente."",
  ""descripcionPrompt"": ""La descripción detallada del crimen que cometió."",
  ""coartada"": ""{coartada}"",
  ""actitud"": ""{actitud}""
}}";
    }

    /// <summary>
    /// Intenta generar un caso con la IA. Si falla, devuelve fallback.
    /// </summary>
    public async Task<GameContext.CasoDelito> GenerarCasoAsync()
    {
        if (iaConfig == null)
        {
            Debug.LogWarning("[CaseGenerator] IAConfig no asignado. Usando fallback.");
            return ObtenerFallback();
        }

        for (int intento = 0; intento < iaConfig.maxReintentos; intento++)
        {
            Debug.Log($"[CaseGenerator] Intento {intento + 1}/{iaConfig.maxReintentos}...");

            // Iniciar medición de métricas
            if (LatencyMetrics.Instance != null)
                LatencyMetrics.Instance.IniciarMedicion(iaConfig.nombreModeloCasos, "caso");

            string respuesta = await EnviarPeticion();

            if (string.IsNullOrEmpty(respuesta))
            {
                Debug.LogWarning($"[CaseGenerator] Intento {intento + 1}: Respuesta vacía");
                continue;
            }

            // Log COMPLETO de lo que devuelve el modelo para debug
            Debug.Log($"[CaseGenerator] Respuesta cruda del LLM:\n{respuesta}");

            var caso = ValidarYParsear(respuesta);
            if (caso != null)
            {
                if (LatencyMetrics.Instance != null)
                    LatencyMetrics.Instance.FinalizarMedicion(respuesta, false);

                Debug.Log($"[CaseGenerator] ✅ Caso generado: {caso.TituloFolio} | Coartada: {caso.Coartada}");
                return caso;
            }

            Debug.LogWarning($"[CaseGenerator] Intento {intento + 1}: No se pudo parsear. Reintentando...");
        }

        Debug.LogWarning("[CaseGenerator] Todos los intentos fallaron. Usando fallback.");
        return ObtenerFallback();
    }

    private async Task<string> EnviarPeticion()
    {
        var messages = new List<object>
        {
            new { role = "user", content = ObtenerPromptDinamico() }
        };

        // Usamos un mínimo de 512 tokens para asegurar que el JSON detallado no se corte
        int tokens = Mathf.Max(iaConfig.maxTokensCaso, 512);

        var datos = new
        {
            model = iaConfig.nombreModeloCasos,
            messages = messages,
            temperature = iaConfig.temperaturaCasos,
            max_tokens = tokens
        };

        string jsonBody = JsonConvert.SerializeObject(datos);
        byte[] bodyRaw = Encoding.UTF8.GetBytes(jsonBody);

        using (UnityWebRequest request = new UnityWebRequest(iaConfig.urlModeloCasos, "POST"))
        {
            request.uploadHandler = new UploadHandlerRaw(bodyRaw);
            request.downloadHandler = new DownloadHandlerBuffer();
            request.SetRequestHeader("Content-Type", "application/json");
            request.timeout = Mathf.RoundToInt(iaConfig.tiempoTimeout); // Usar timeout configurado (por defecto 30s)

            var operacion = request.SendWebRequest();
            while (!operacion.isDone) await Task.Yield();

            if (request.result == UnityWebRequest.Result.Success)
            {
                try
                {
                    var respuesta = JsonConvert.DeserializeObject<RespuestaLLM>(request.downloadHandler.text);
                    return respuesta.choices[0].message.content;
                }
                catch (System.Exception ex)
                {
                    Debug.LogError("[CaseGenerator] Error parseando respuesta HTTP: " + ex.Message);
                    Debug.LogError("[CaseGenerator] Respuesta raw: " + request.downloadHandler.text);
                    return null;
                }
            }
            else
            {
                Debug.LogError($"[CaseGenerator] Error HTTP ({request.responseCode}): {request.error}");
                Debug.LogError($"[CaseGenerator] Respuesta de error de LM Studio: {request.downloadHandler.text}");
                return null;
            }
        }
    }

    /// <summary>
    /// Parsea el JSON de forma MUY tolerante. Maneja:
    /// - JSON envuelto en texto extra
    /// - Bloques de código markdown (```json ... ```)
    /// - Campos con nombres ligeramente diferentes
    /// - Comillas simples o dobles
    /// </summary>
    private GameContext.CasoDelito ValidarYParsear(string texto)
    {
        try
        {
            texto = texto.Trim();

            // 1. Quitar bloques de código markdown si los hay
            texto = Regex.Replace(texto, @"```json\s*", "", RegexOptions.IgnoreCase);
            texto = Regex.Replace(texto, @"```\s*", "");

            // 2. Buscar el JSON entre la primera { y la última }
            int inicio = texto.IndexOf('{');
            int fin = texto.LastIndexOf('}');
            if (inicio < 0 || fin < 0 || fin <= inicio)
            {
                Debug.LogWarning("[CaseGenerator] No se encontró JSON válido (sin llaves)");
                return null;
            }
            texto = texto.Substring(inicio, fin - inicio + 1);

            // 3. Intentar parsear
            JObject json;
            try
            {
                json = JObject.Parse(texto);
            }
            catch
            {
                // Intentar arreglar comillas simples
                string arreglado = texto.Replace("'", "\"");
                try
                {
                    json = JObject.Parse(arreglado);
                }
                catch (System.Exception ex2)
                {
                    Debug.LogWarning($"[CaseGenerator] JSON inválido incluso tras arreglar comillas: {ex2.Message}");
                    return null;
                }
            }

            // 4. Extraer campos con nombres tolerantes (el modelo puede usar variantes)
            string id = ObtenerCampo(json, "id", "ID", "Id", "numero", "num");
            string titulo = ObtenerCampo(json, "titulo", "Titulo", "TITULO", "title", "crime", "crimen");
            string descripcionFolio = ObtenerCampo(json, "descripcionFolio", "descripcion_folio", "descripcion", "Descripcion", "description");
            string descripcionPrompt = ObtenerCampo(json, "descripcionPrompt", "descripcion_prompt", "prompt", "descripcionCrimen");
            string coartada = ObtenerCampo(json, "coartada", "Coartada", "alibi", "excusa");
            string actitud = ObtenerCampo(json, "actitud", "Actitud", "attitude", "comportamiento", "emocion");

            // 5. Validaciones mínimas (muy permisivas)
            if (string.IsNullOrWhiteSpace(titulo))
            {
                Debug.LogWarning("[CaseGenerator] Campo 'titulo' vacío o no encontrado");
                return null;
            }
            if (string.IsNullOrWhiteSpace(descripcionPrompt) && string.IsNullOrWhiteSpace(descripcionFolio))
            {
                Debug.LogWarning("[CaseGenerator] Ni 'descripcionPrompt' ni 'descripcionFolio' encontrados");
                return null;
            }
            if (string.IsNullOrWhiteSpace(coartada))
            {
                Debug.LogWarning("[CaseGenerator] Campo 'coartada' vacío o no encontrado");
                return null;
            }

            // 6. Rellenar campos faltantes con defaults razonables
            if (string.IsNullOrWhiteSpace(id)) id = Random.Range(100, 999).ToString();
            if (string.IsNullOrWhiteSpace(descripcionPrompt)) descripcionPrompt = descripcionFolio;
            if (string.IsNullOrWhiteSpace(descripcionFolio)) descripcionFolio = descripcionPrompt;
            if (string.IsNullOrWhiteSpace(actitud)) actitud = "Estás muy nervioso y no paras de moverte en la silla.";

            return new GameContext.CasoDelito
            {
                ID = id.PadLeft(3, '0').Length >= 3 ? id.PadLeft(3, '0').Substring(0, 3) : id,
                TituloFolio = titulo.ToUpper().Trim(),
                DescripcionFolio = descripcionFolio.Trim(),
                DescripcionPrompt = descripcionPrompt.Trim(),
                Coartada = coartada.Trim(),
                Actitud = actitud.Trim()
            };
        }
        catch (System.Exception ex)
        {
            Debug.LogWarning($"[CaseGenerator] Error inesperado de parseo: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Busca un campo en el JSON probando múltiples nombres posibles.
    /// </summary>
    private string ObtenerCampo(JObject json, params string[] nombres)
    {
        foreach (string nombre in nombres)
        {
            var valor = json[nombre];
            if (valor != null && !string.IsNullOrWhiteSpace(valor.ToString()))
                return valor.ToString();
        }
        return null;
    }

    private GameContext.CasoDelito ObtenerFallback()
    {
        return casosFallback[Random.Range(0, casosFallback.Length)];
    }

    // Clases para deserializar
    private class RespuestaLLM { public List<Choice> choices; }
    private class Choice { public Message message; }
    private class Message { public string content; }
}
