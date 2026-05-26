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
        new GameContext.CasoDelito { ID = "042", TituloFolio = "ROBO EN EL MUSEO", DescripcionFolio = "Robo de un diamante maldito durante una gala de disfraces.", DescripcionPrompt = "un atraco en el museo de arte moderno donde se robó un diamante maldito durante una gala", Coartada = "encerrado accidentalmente en el baño de un restaurante tailandés", Actitud = "Estás absolutamente paranoico, miras a todos lados y sudas profusamente.", SecretoCulpable = "El diamante lo tienes escondido en el zapato izquierdo.", SecretoInocente = "En realidad fuiste al restaurante para espiar a tu ex-pareja." },
        new GameContext.CasoDelito { ID = "087", TituloFolio = "ENVENENAMIENTO", DescripcionFolio = "Asesinato por envenenamiento con pudin en el asilo de ancianos.", DescripcionPrompt = "el envenenamiento de un millonario usando pudin de chocolate caducado en un asilo", Coartada = "participando en una sesión de espiritismo clandestina", Actitud = "Eres increíblemente sarcástico, usas humor negro y te burlas del detective.", SecretoCulpable = "Compraste el veneno por internet usando la tarjeta de tu abuela.", SecretoInocente = "En la sesión de espiritismo estabas intentando contactar a tu hámster muerto." },
        new GameContext.CasoDelito { ID = "104", TituloFolio = "SECUESTRO VIRTUAL", DescripcionFolio = "Secuestro de un famoso Youtuber mientras emitía en directo.", DescripcionPrompt = "el secuestro de un youtuber famoso irrumpiendo en su mansión en pleno directo", Coartada = "haciendo cosplay de Batman en tu habitación grabando TikToks", Actitud = "Tienes aires de grandeza, eres pedante y te sientes profundamente insultado.", SecretoCulpable = "Se te cayó tu móvil personal en la casa del Youtuber.", SecretoInocente = "Los TikToks que grababas eran bailando canciones infantiles en pijama." },
        new GameContext.CasoDelito { ID = "019", TituloFolio = "SABOTAJE ANIMAL", DescripcionFolio = "Liberación ilegal de pingüinos pigmeos en el puerto de la ciudad.", DescripcionPrompt = "haber liberado cien pingüinos pigmeos de un carguero en el puerto", Coartada = "perdido en el bosque persiguiendo a lo que creías que era un chupacabras", Actitud = "Estás en estado de negación absoluta, al borde del colapso nervioso y lloriqueando.", SecretoCulpable = "Aún tienes un pingüino escondido en la bañera de tu casa.", SecretoInocente = "No estabas en el bosque, estabas robando wifi en un McDonald's a esa hora." },
        new GameContext.CasoDelito { ID = "055", TituloFolio = "AGRESIÓN FRIKI", DescripcionFolio = "Agresión con sables láser de juguete en la convención de cómics.", DescripcionPrompt = "haber agredido violentamente al organizador de una convención de cómics usando réplicas de sables láser", Coartada = "en una cita a ciegas desastrosa con alguien que se hacía llamar 'El Rey Lagarto'", Actitud = "Eres el típico cuñado sabelotodo, interrumpes constantemente al detective para explicarle cómo hacer su trabajo.", SecretoCulpable = "Rompiste tu propio sable láser al golpear a la víctima.", SecretoInocente = "En tu cita a ciegas te dejaron plantado y te pusiste a llorar en el baño." },
        new GameContext.CasoDelito { ID = "092", TituloFolio = "FALSIFICACIÓN", DescripcionFolio = "Falsificación de obras de arte contemporáneo usando macarrones con queso.", DescripcionPrompt = "una estafa vendiendo cuadros falsos de Picasso hechos de macarrones con queso y pintura", Coartada = "intentando robar Wi-Fi de la cafetería de enfrente desde el maletero de tu coche", Actitud = "Eres extremadamente seductor y manipulador, intentando coquetear con el detective con calma perturbadora.", SecretoCulpable = "Usaste macarrones de la marca 'Buitoni' para el cuadro principal.", SecretoInocente = "Estabas en el maletero porque le debías dinero a un prestamista y te estabas escondiendo." }
    };

    private enum NivelInteligencia { Simple, Medio, Complejo }

    private NivelInteligencia ObtenerNivelInteligencia(string nombreModelo)
    {
        if (string.IsNullOrEmpty(nombreModelo)) return NivelInteligencia.Medio;
        string nombre = nombreModelo.ToLower();
        if (nombre.Contains("1b") || nombre.Contains("2b") || nombre.Contains("tiny") || nombre.Contains("qwen1.5-0.5b") || nombre.Contains("mini"))
            return NivelInteligencia.Simple;
        else if (nombre.Contains("7b") || nombre.Contains("8b") || nombre.Contains("14b") || nombre.Contains("32b") || nombre.Contains("70b"))
            return NivelInteligencia.Complejo;
        else
            return NivelInteligencia.Medio;
    }

    private static string ObtenerPromptDinamico(NivelInteligencia inteligencia)
    {
        string prompt = @"Eres un generador de datos policiales. Debes devolver ÚNICAMENTE un objeto JSON válido.
No escribas introducciones, no uses markdown. SOLO JSON. Escribe en perfecto español.

INSTRUCCIONES DE GENERACIÓN:
Tienes que INVENTAR un caso policial, una coartada para el sospechoso y una personalidad/actitud para él.
";

        if (inteligencia == NivelInteligencia.Simple)
        {
            prompt += @"
REGLAS PARA EL CASO (NIVEL BÁSICO):
- Inventa un crimen muy común (robo, pelea, estafa).
- Inventa una coartada (normal o peculiar, pero siempre creíble y relacionada con el contexto, lugar u hora del crimen).
- Inventa una actitud básica (nervioso, enfadado, triste).
- Sigue exactamente la estructura de estos ejemplos. IMPORTANTE: Todo debe estar relacionado. El `secretoCulpable` es un detalle clave que lo incrimina. El `secretoInocente` explica por qué mintió en su coartada falsa (por vergüenza o miedo) pero lo desvincula del crimen.

EJEMPLO 1:
{
  ""id"": ""001"",
  ""titulo"": ""ROBO DE COCHE"",
  ""descripcionFolio"": ""Robo de un vehículo sedán rojo en el aparcamiento."",
  ""descripcionPrompt"": ""el robo de un coche sedán rojo en el aparcamiento del supermercado"",
  ""coartada"": ""durmiendo en casa de mi hermano"",
  ""actitud"": ""Estás muy asustado y lloras."",
  ""secretoCulpable"": ""Robaste el coche a las 03:00 de la mañana rompiendo el cristal."",
  ""secretoInocente"": ""Tu hermano no estaba en casa, estabas durmiendo en un banco del parque.""
}

EJEMPLO 2:
{
  ""id"": ""002"",
  ""titulo"": ""PELEA EN EL BAR"",
  ""descripcionFolio"": ""Agresión física a un camarero en el bar central."",
  ""descripcionPrompt"": ""una agresión violenta al camarero del bar central lanzándole una botella"",
  ""coartada"": ""paseando a mi perro en el parque"",
  ""actitud"": ""Estás a la defensiva y cruzas los brazos."",
  ""secretoCulpable"": ""Le lanzaste una botella de ron añejo."",
  ""secretoInocente"": ""No estabas paseando al perro, estabas comprando droga en el parque.""
}

AHORA GENERA UN JSON CON UN CASO COMPLETAMENTE INVENTADO POR TI:
";
        }
        else if (inteligencia == NivelInteligencia.Complejo)
        {
            prompt += @"
REGLAS PARA EL CASO (NIVEL EXPERTO):
- Inventa crímenes extremadamente originales, bizarros, cinematográficos o complejos (ej: robo de reliquias, cibercrímenes extraños, crímenes pasionales rocambolescos).
- Inventa coartadas detalladas. La coartada DEBE estar inteligentemente conectada con los elementos del caso (lugar, hora, personas implicadas).
- Inventa actitudes que sean perfiles psicológicos muy complejos (ej: narcisista pedante, manipulador seductor, cuñado sabelotodo, místico zen, paranoico de conspiraciones).
- Inventa 2 secretos locos o bizarros, PERO con PERFECTO SENTIDO LÓGICO. Todo debe estar entrelazado como un puzle intuitivo: El `secretoCulpable` es la prueba irrefutable de que cometió el crimen. El `secretoInocente` es la razón embarazosa/ilegal por la que mintió en su coartada, lo cual demuestra que no es el asesino/ladrón.
- NO COPIES los ejemplos, úsalos solo como inspiración para ver el formato JSON. ¡Sé totalmente creativo!

EJEMPLO DE INSPIRACIÓN:
{
  ""id"": ""888"",
  ""titulo"": ""FALSIFICACIÓN BIZARRA"",
  ""descripcionFolio"": ""Falsificación de obras de arte usando macarrones con queso."",
  ""descripcionPrompt"": ""una estafa internacional vendiendo cuadros de Picasso falsos hechos de macarrones con queso"",
  ""coartada"": ""en una cita a ciegas desastrosa con alguien que se hacía llamar 'El Rey Lagarto'"",
  ""actitud"": ""Eres increíblemente sarcástico, usas humor negro y te burlas del detective constantemente."",
  ""secretoCulpable"": ""El pegamento que usaste para los macarrones era de la marca SuperGlue."",
  ""secretoInocente"": ""En la cita a ciegas te pusiste a llorar porque te acordaste de tu ex.""
}

AHORA GENERA UN JSON CON UN CASO COMPLETAMENTE NUEVO, ÚNICO, ORIGINAL Y CREATIVO:
";
        }
        else // Medio
        {
            prompt += @"
REGLAS PARA EL CASO (NIVEL MEDIO):
- Inventa crímenes variados: algunos cotidianos y otros más curiosos.
- Inventa coartadas concretas (normales o locas, pero creíbles y siempre conectadas al contexto del crimen).
- Inventa actitudes variadas (sarcástico, confundido, chulo, asustado).
- Inventa dos secretos: Todo debe estar muy bien atado para que el jugador pueda deducir. El `secretoCulpable` oculta una prueba directa del crimen. El `secretoInocente` oculta la verdadera razón de su coartada falsa (un motivo vergonzoso) demostrando su inocencia en el caso.
- Usa este ejemplo como guía para el formato JSON, pero cambia completamente la temática.

EJEMPLO:
{
  ""id"": ""055"",
  ""titulo"": ""SABOTAJE TECNOLÓGICO"",
  ""descripcionFolio"": ""Infección de virus en la red del ayuntamiento."",
  ""descripcionPrompt"": ""haber introducido un virus que borró los archivos del ayuntamiento"",
  ""coartada"": ""jugando a un torneo de videojuegos online en mi cuarto"",
  ""actitud"": ""Te muestras arrogante y miras con desprecio al detective."",
  ""secretoCulpable"": ""Introdujiste el virus usando un pendrive con forma de pato."",
  ""secretoInocente"": ""Estabas jugando al Barbie Horse Adventures, no a un torneo de videojuegos.""
}

AHORA GENERA UN JSON CON UN CASO INVENTADO POR TI:
";
        }

        return prompt;
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
        NivelInteligencia inteligencia = ObtenerNivelInteligencia(iaConfig?.nombreModeloCasos);
        
        var messages = new List<object>
        {
            new { role = "user", content = ObtenerPromptDinamico(inteligencia) }
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
            string secCulpable = ObtenerCampo(json, "secretoCulpable", "secreto_culpable");
            string secInocente = ObtenerCampo(json, "secretoInocente", "secreto_inocente");

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
                Actitud = actitud.Trim(),
                SecretoCulpable = secCulpable?.Trim() ?? "El crimen se cometió a una hora muy concreta que nadie te ha dicho.",
                SecretoInocente = secInocente?.Trim() ?? "Estabas haciendo algo muy vergonzoso en tu coartada y no quieres decirlo."
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
