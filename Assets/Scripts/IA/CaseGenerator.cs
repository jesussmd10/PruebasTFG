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

    private static readonly GameContext.CasoDelito[] casosFallback = new GameContext.CasoDelito[]
    {
        new GameContext.CasoDelito { ID = "042", TituloFolio = "ROBO EN EL MUSEO", Sospechoso = "Alex", DescripcionFolio = "Robo de un diamante maldito durante una gala de disfraces.", DescripcionPrompt = "un atraco en el museo de arte moderno donde se robó un diamante maldito durante una gala", Coartada = "encerrado en el baño de un tailandés", Actitud = "Paranoico", EsCulpable = true, Secreto = "El diamante está en su zapato." },
        new GameContext.CasoDelito { ID = "087", TituloFolio = "ENVENENAMIENTO", Sospechoso = "Alex", DescripcionFolio = "Asesinato por envenenamiento con pudin en el asilo de ancianos.", DescripcionPrompt = "el envenenamiento de un millonario usando pudin de chocolate caducado en un asilo", Coartada = "haciendo espiritismo clandestino en el sótano", Actitud = "Sarcástico", EsCulpable = false, Secreto = "Invocaba el fantasma de su hámster." },
        new GameContext.CasoDelito { ID = "104", TituloFolio = "SECUESTRO VIRTUAL", Sospechoso = "Alex", DescripcionFolio = "Secuestro de un famoso Youtuber mientras emitía en directo.", DescripcionPrompt = "el secuestro de un youtuber famoso irrumpiendo en su mansión en pleno directo", Coartada = "grabando TikToks con cosplay de Batman", Actitud = "Pedante", EsCulpable = true, Secreto = "Perdió su móvil en la mansión." },
        new GameContext.CasoDelito { ID = "019", TituloFolio = "SABOTAJE ANIMAL", Sospechoso = "Alex", DescripcionFolio = "Liberación ilegal de pingüinos pigmeos en el puerto de la ciudad.", DescripcionPrompt = "haber liberado cien pingüinos pigmeos de un carguero en el puerto", Coartada = "persiguiendo ovnis en el bosque oscuro", Actitud = "Lloriqueando", EsCulpable = false, Secreto = "Robaba el WiFi del McDonald's." },
        new GameContext.CasoDelito { ID = "055", TituloFolio = "AGRESIÓN FRIKI", Sospechoso = "Alex", DescripcionFolio = "Agresión con sables láser de juguete en la convención de cómics.", DescripcionPrompt = "haber agredido violentamente al organizador de una convención de cómics usando réplicas de sables láser", Coartada = "en una cita con 'El Rey Lagarto'", Actitud = "Sabelotodo", EsCulpable = true, Secreto = "Rompió su sable en la cabeza de la víctima." },
        new GameContext.CasoDelito { ID = "092", TituloFolio = "FALSIFICACIÓN", Sospechoso = "Alex", DescripcionFolio = "Falsificación de obras de arte contemporáneo usando macarrones con queso.", DescripcionPrompt = "una estafa vendiendo cuadros falsos de Picasso hechos de macarrones con queso y pintura", Coartada = "robando WiFi desde mi maletero", Actitud = "Seductor", EsCulpable = false, Secreto = "Huía de un prestamista colombiano." }
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

    private static readonly string[] temasAleatorios = new string[]
    {
        "un robo de un objeto cotidiano y absurdo en una oficina",
        "un asesinato muy peliculero en una mansión durante una tormenta",
        "una estafa piramidal relacionada con productos de belleza caseros",
        "un secuestro de una mascota exótica (ej. una iguana) en un vecindario pijo",
        "un sabotaje en la cocina de un restaurante de comida rápida",
        "un acto de vandalismo pintando estatuas de color rosa neón",
        "un robo en una convención de cómics y cosplayers",
        "un envenenamiento fallido en un asilo de ancianos",
        "una trama de espionaje corporativo para robar una receta secreta de galletas",
        "un crimen relacionado con espiritismo, fantasmas o un culto ridículo",
        "un atraco chapucero a una gasolinera a las 3 de la mañana",
        "un chantaje en redes sociales a un influencer muy creído",
        "un robo de criptomonedas o hackeo desde el sótano de los padres",
        "una pelea callejera con armas absurdas (ej. sables láser de plástico o sartenes)",
        "un caso cotidiano de robo de paquetes en la puerta de las casas",
        "un robo de un décimo de lotería premiado en el bar del barrio",
        "un envenenamiento con laxantes en el termo de café del jefe de una oficina",
        "una estafa con entradas VIP falsas para el concierto de un artista famoso",
        "un sabotaje cortando los cables del router en medio de un torneo de eSports",
        "un caso de suplantación de identidad en una aplicación de citas de lujo",
        "un asalto a un furgón que transportaba cromos coleccionables extremadamente raros",
        "una venganza destruyendo el vestido de novia horas antes de una boda de la alta sociedad",
        "un robo de una reliquia histórica en un museo local muy polvoriento y olvidado",
        "un secuestro express del cantante principal de una banda tributo muy mala",
        "un incendio provocado en un gimnasio de barrio por culpa de unos esteroides adulterados",
        "un atropello y fuga con un carrito de golf en un club de campo para ricos",
        "un fraude en un concurso de talentos caninos de televisión local",
        "un intento de extorsión usando fotos embarazosas de una despedida de soltero",
        "un atraco a un banco donde los ladrones se quedaron encerrados en la cámara acorazada",
        "un sabotaje en un festival de música independiente cortando la electricidad del escenario principal"
    };

    private static string ObtenerPromptDinamico(NivelInteligencia inteligencia, bool esCulpable)
    {
        string estadoCulpa = esCulpable ? "CULPABLE" : "INOCENTE";
        string temaEscogido = temasAleatorios[Random.Range(0, temasAleatorios.Length)];

        string reglaSecreto = esCulpable 
            ? "3. SECRETO: Prueba irrefutable (ej. un objeto oculto, arma, recibo) que demuestra su culpabilidad y destroza su coartada. Tiene que tener mucha 'chicha' e intriga, y estar 100% relacionado con el crimen." 
            : "3. SECRETO: Actividad humillante, vergonzosa o un delito menor. Estaba en la escena ocultando este secreto con mucha 'chicha' (por eso mintió en su coartada), pero NO cometió el crimen principal. Debe estar 100% relacionado con el contexto del caso.";

        string[] ejemplosActitud = new string[] 
        {
            "A la defensiva y chulo",
            "Aterrado y tembloroso",
            "Sereno y calculador",
            "Nervioso pero intentando parecer calmado",
            "Indiferente y aburrido",
            "Agresivo y a la defensiva",
            "Lloroso y desesperado",
            "Desafiante y sarcástico",
            "Sudando y tartamudeando", 
            "Paranoico y suspicaz",
            "Pedante y sabelotodo",
            "Falsamente cooperativo",
            "Indignado y ofendido",
            "Despistado y confuso",
            "Dramático y victimista",
            "Hostil y gruñón",
            "Críptico y misterioso",
            "Agotado y apático",
            "Burlón y bromista"
        };
        string actitudAleatoria = ejemplosActitud[Random.Range(0, ejemplosActitud.Length)];

        string prompt = $@"Eres una IA generadora de expedientes policiales. Tu ÚNICA tarea es rellenar la plantilla XML con los datos del caso basándote en los parámetros.
NO REPITAS LAS INSTRUCCIONES. NO GENERES DIÁLOGO.

### PARÁMETROS DEL CASO ###
- SOSPECHOSO: {estadoCulpa}. 
- TEMA DEL CRIMEN: ""{temaEscogido}""

### REGLAS NARRATIVAS ###
1. CRIMEN: Delito original y muy descriptivo basado en el TEMA.
2. COARTADA: Actividad falsa pero detallada cerca de la escena. DEBE estar 100% relacionada lógicamente con el entorno del caso.
3. SECRETO: {reglaSecreto}
4. ACTITUD: 1 o 2 adjetivos (ej: {actitudAleatoria}).
5. FORMATO POLICIAL: TODO debe estar redactado de forma puramente objetiva, como un informe policial (en tercera persona). El sospechoso es un HOMBRE. PROHIBIDO usar 'yo' o 'tú' en los párrafos.

### REGLAS DE FORMATO (CRÍTICO) ###
- TODO EL TEXTO DEBE ESTAR 100% EN ESPAÑOL.
- Tienes que devolver EXACTAMENTE esta estructura XML rellenada, sin añadir texto antes ni después:

<caso>
<titulo>Título (máx 5 palabras)</titulo>
<sospechoso>Nombre masculino completo inventado</sospechoso>
<descripcion_folio>Resumen policial detallado del caso redactado en tercera persona (1 párrafo largo, máx 120 palabras)</descripcion_folio>
<coartada>Su coartada redactada de forma objetiva en tercera persona (1 párrafo largo, máx 120 palabras)</coartada>
<actitud>Adjetivos inventados, 1 o 2 máximo</actitud>
<secreto>El secreto real redactado en tercera persona (1 párrafo)</secreto>
</caso>";

        if (inteligencia == NivelInteligencia.Simple)
        {
            prompt += "\n(VOCABULARIO SIMPLE Y DIRECTO,MANTENIENDO COHESIÓN LÓGICA.)\n";
        }
        else if (inteligencia == NivelInteligencia.Complejo)
        {
            prompt += "\n(TRAMA CREATIVA Y REBUSCADA, MANTENIENDO COHESIÓN LÓGICA.)\n";
        }

        return prompt;
    }

    private static Task<GameContext.CasoDelito> ongoingGeneration = null;

#if UNITY_EDITOR
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        ongoingGeneration = null;
    }
#endif

    public async Task<GameContext.CasoDelito> GenerarCasoAsync()
    {
        if (ongoingGeneration != null)
        {
            Debug.LogWarning("[CaseGenerator] Ya hay una generación en curso. Esperando a que termine para no saturar el LLM...");
            return await ongoingGeneration;
        }

        ongoingGeneration = GenerarCasoInternoAsync();
        try
        {
            return await ongoingGeneration;
        }
        finally
        {
            ongoingGeneration = null;
        }
    }

    private int ultimoTokensCaso = 0;

    private async Task<GameContext.CasoDelito> GenerarCasoInternoAsync()
    {
        if (iaConfig == null) return ObtenerFallback();

        bool esCulpableRnd = Random.value > 0.5f;

        for (int intento = 0; intento < iaConfig.maxReintentos; intento++)
        {
            Debug.Log($"[CaseGenerator] Intento {intento + 1}/{iaConfig.maxReintentos} (Culpable: {esCulpableRnd})...");
            
            string respuesta = await EnviarPeticion(esCulpableRnd);

            if (!string.IsNullOrEmpty(respuesta))
            {
                Debug.Log($"[CaseGenerator] Respuesta cruda del LLM:\n{respuesta}");

                bool xmlPerfecto = respuesta.Contains("<caso>") && respuesta.Contains("</caso>");
                var caso = ValidarYParsear(respuesta);
                
                if (LatencyMetrics.Instance != null)
                    LatencyMetrics.Instance.FinalizarMedicion(respuesta, false, xmlPerfecto, ultimoTokensCaso);

                if (caso != null)
                {
                    caso.EsCulpable = esCulpableRnd;

                    Debug.Log($"[CaseGenerator] ✅ Caso generado: {caso.TituloFolio} | Coartada: {caso.Coartada} | Culpable: {caso.EsCulpable}");
                    
                    // OPTIMIZACIÓN VRAM: Descargar modelo de casos antes de empezar el diálogo
                    await DescargarModeloCasoAsync();
                    
                    return caso;
                }

                Debug.LogWarning($"[CaseGenerator] Intento {intento + 1}: No se pudo parsear o hubo un error. Reintentando en 3 segundos...");
                await Task.Delay(3000); // Pausa para no saturar el LLM en caso de error
            }
        }

        Debug.LogWarning("[CaseGenerator] Todos los intentos fallaron. Usando fallback.");
        await DescargarModeloCasoAsync();
        return ObtenerFallback();
    }

    private async Task DescargarModeloCasoAsync()
    {
        if (iaConfig == null || string.IsNullOrEmpty(iaConfig.urlModeloCasos)) return;

        string baseUri = iaConfig.urlModeloCasos.Replace("/v1/chat/completions", "").Replace("/chat/completions", "");
        string getModelsUrl = baseUri + "/v1/models";

        Debug.Log($"[CaseGenerator] Solicitando descarga del modelo pesado ({iaConfig.nombreModeloCasos}) de VRAM...");

        // 1. Intentar el endpoint nativo de LM Studio (v1 API) usando 'model'
        bool exito = await IntentarDescarga(baseUri + "/api/v1/models/unload", new { model = iaConfig.nombreModeloCasos });
        
        // 2. Si falla, intentar el mismo endpoint pero usando 'instance_id' (requerido en versiones recientes)
        if (!exito)
        {
            exito = await IntentarDescarga(baseUri + "/api/v1/models/unload", new { instance_id = iaConfig.nombreModeloCasos });
        }

        // 3. Si falla, usar el endpoint clásico de compatibilidad OpenAI
        if (!exito)
        {
            exito = await IntentarDescarga(baseUri + "/v1/models/unload", new { model = iaConfig.nombreModeloCasos });
        }

        if (exito)
        {
            Debug.Log("[CaseGenerator] ✅ Petición de descarga aceptada por el servidor. Esperando 3 segundos para limpieza física de GPU...");
            await Task.Delay(3000);
        }
        else
        {
            Debug.LogWarning("[CaseGenerator] ❌ No se pudo forzar la descarga en LM Studio a través de ninguna API. Puede que tengas que expulsarlo a mano ('Eject') o usar un TTL corto.");
        }
    }

    private async Task<bool> IntentarDescarga(string url, object payload)
    {
        string jsonBody = JsonConvert.SerializeObject(payload);
        byte[] bodyRaw = Encoding.UTF8.GetBytes(jsonBody);

        using (UnityWebRequest request = new UnityWebRequest(url, "POST"))
        {
            request.uploadHandler = new UploadHandlerRaw(bodyRaw);
            request.downloadHandler = new DownloadHandlerBuffer();
            request.SetRequestHeader("Content-Type", "application/json");
            request.timeout = 3; 
            
            var operacion = request.SendWebRequest();
            while (!operacion.isDone) await Task.Yield();

            if (request.result == UnityWebRequest.Result.Success)
            {
                // LM Studio a veces devuelve 200 OK pero manda un JSON {"success": true/false} o similar
                string res = request.downloadHandler.text;
                if (!string.IsNullOrEmpty(res) && res.Contains("\"error\"")) return false;
                
                return true; 
            }
        }
        return false;
    }

    private async Task<string> EnviarPeticion(bool esCulpable)
    {
        NivelInteligencia inteligencia = ObtenerNivelInteligencia(iaConfig?.nombreModeloCasos);
        
        var messages = new List<object>
        {
            new { role = "user", content = ObtenerPromptDinamico(inteligencia, esCulpable) }
        };

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
            
            int timeoutAsignado = Mathf.RoundToInt(iaConfig.tiempoTimeout);
            request.timeout = timeoutAsignado < 120 ? 120 : timeoutAsignado; 

            if (LatencyMetrics.Instance != null)
                LatencyMetrics.Instance.IniciarMedicion(iaConfig.nombreModeloCasos, "caso");

            float startTime = Time.time;
            var operacion = request.SendWebRequest();
            while (!operacion.isDone) await Task.Yield();
            float timeTaken = Time.time - startTime;

            if (request.result == UnityWebRequest.Result.Success)
            {
                try
                {
                    var respuesta = JsonConvert.DeserializeObject<RespuestaLLM>(request.downloadHandler.text);
                    
                    int promptTokens = respuesta.usage?.prompt_tokens ?? 0;
                    int completionTokens = respuesta.usage?.completion_tokens ?? 0;
                    int totalTokens = respuesta.usage?.total_tokens ?? 0;

                    Debug.Log($"<color=cyan>[METRICAS LLM]</color> Caso generado en {timeTaken:F2} segundos. | Tokens: {promptTokens} prompt + {completionTokens} generados = {totalTokens} total.");

                    ultimoTokensCaso = totalTokens;

                    return respuesta.choices[0].message.content;
                }
                catch (System.Exception ex)
                {
                    Debug.LogError("[CaseGenerator] Error parseando respuesta HTTP: " + ex.Message);
                    return null;
                }
            }
            else
            {
                Debug.LogError($"[CaseGenerator] Error HTTP ({request.responseCode}): {request.error}");
                return null;
            }
        }
    }

    private GameContext.CasoDelito ValidarYParsear(string texto)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(texto)) return null;

            texto = texto.Trim();

            string titulo = ExtraerValorRegex(texto, "(?:t[ií]tulo|title)");
            string sospechoso = ExtraerValorRegex(texto, "(?:sospechoso|suspect)");
            string descripcionFolio = ExtraerValorRegex(texto, "(?:descripci[oó]n_folio|description_folio|descripcion)");
            string descripcionPrompt = ExtraerValorRegex(texto, "(?:descripci[oó]n_prompt|description_prompt|prompt)");
            string coartada = ExtraerValorRegex(texto, "(?:coartada|alibi)");
            string actitud = ExtraerValorRegex(texto, "(?:actitud|attitude)");
            string secreto = ExtraerValorRegex(texto, "(?:secreto|secret)");

            if (string.IsNullOrWhiteSpace(titulo)) return null;
            if (string.IsNullOrWhiteSpace(coartada)) return null;

            if (string.IsNullOrWhiteSpace(sospechoso)) sospechoso = "Alex (Desconocido)";
            if (string.IsNullOrWhiteSpace(descripcionPrompt)) descripcionPrompt = descripcionFolio ?? titulo;
            if (string.IsNullOrWhiteSpace(descripcionFolio)) descripcionFolio = descripcionPrompt;
            if (string.IsNullOrWhiteSpace(actitud)) actitud = "Nervioso";

            actitud = Regex.Replace(actitud, @"^(Su\s+)?actitud:?\s*", "", RegexOptions.IgnoreCase);
            coartada = Regex.Replace(coartada, @"^(Su\s+)?coartada(\s+falsa)?:?\s*", "", RegexOptions.IgnoreCase);
            secreto = Regex.Replace(secreto, @"^(Su\s+)?secreto(\s+vergonzoso|\s+criminal)?:?\s*", "", RegexOptions.IgnoreCase);

            return new GameContext.CasoDelito
            {
                ID = Random.Range(100, 999).ToString("000"),
                TituloFolio = titulo.ToUpper().Trim(),
                Sospechoso = sospechoso.Trim(),
                DescripcionFolio = descripcionFolio.Trim(),
                DescripcionPrompt = descripcionPrompt.Trim(),
                Coartada = coartada.Trim(),
                Actitud = actitud.Trim(),
                Secreto = string.IsNullOrWhiteSpace(secreto) ? "El crimen se cometió a una hora muy concreta que nadie te ha dicho." : secreto.Trim(),
            };
        }
        catch (System.Exception ex)
        {
            Debug.LogWarning($"[CaseGenerator] Error inesperado de parseo: {ex.Message}");
            return null;
        }
    }

    private string ExtraerValorRegex(string texto, string etiqueta)
    {
        // 1. XML estricto (Ideal)
        var matchXml = Regex.Match(texto, $@"<\s*{etiqueta}[^>]*>(.*?)</\s*{etiqueta}\s*>", RegexOptions.IgnoreCase | RegexOptions.Singleline);
        if (matchXml.Success && !string.IsNullOrWhiteSpace(matchXml.Groups[1].Value))
        {
            return matchXml.Groups[1].Value.Trim(' ', '\t', '\r', '\n', '"', '\'', '*');
        }

        // 2. XML Truncado (Falta el cierre, captura hasta la siguiente etiqueta o fin)
        var matchXmlTruncado = Regex.Match(texto, $@"<\s*{etiqueta}[^>]*>(.*?)(?:<\s*/?[a-zA-Z_]+>|$)", RegexOptions.IgnoreCase | RegexOptions.Singleline);
        if (matchXmlTruncado.Success && !string.IsNullOrWhiteSpace(matchXmlTruncado.Groups[1].Value))
        {
            return matchXmlTruncado.Groups[1].Value.Trim(' ', '\t', '\r', '\n', '"', '\'', '*', '<', '/');
        }

        // 3. Markdown o Texto Plano (ej. "**etiqueta**: valor" o "etiqueta: valor")
        string patronPlano = $@"(?:^|\n)\s*\**{etiqueta}\**\s*:\s*(.*?)(?=\n\s*\**[a-zA-Z_]+\**\s*:|$)";
        var matchPlano = Regex.Match(texto, patronPlano, RegexOptions.IgnoreCase | RegexOptions.Singleline);
        if (matchPlano.Success && !string.IsNullOrWhiteSpace(matchPlano.Groups[1].Value))
        {
            return matchPlano.Groups[1].Value.Trim(' ', '\t', '\r', '\n', '"', '\'', '*');
        }

        // 4. JSON Fallback por si la IA decide ignorar el XML (ej. "etiqueta": "valor")
        string patronJson = $@"""{etiqueta}""\s*:\s*""?(.*?)(?:""|,)";
        var matchJson = Regex.Match(texto, patronJson, RegexOptions.IgnoreCase | RegexOptions.Singleline);
        if (matchJson.Success && !string.IsNullOrWhiteSpace(matchJson.Groups[1].Value))
        {
            return matchJson.Groups[1].Value.Trim(' ', '\t', '\r', '\n', '"', '\'', '*');
        }

        return string.Empty;
    }

    private GameContext.CasoDelito ObtenerFallback()
    {
        return casosFallback[Random.Range(0, casosFallback.Length)];
    }

    // Clases para deserializar
    private class RespuestaLLM 
    { 
        public List<Choice> choices; 
        public Usage usage; 
    }
    private class Choice { public Message message; }
    private class Message { public string content; }
    private class Usage 
    { 
        public int prompt_tokens; 
        public int completion_tokens; 
        public int total_tokens; 
    }
}
