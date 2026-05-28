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
        new GameContext.CasoDelito { ID = "042", TituloFolio = "ROBO EN EL MUSEO", DescripcionFolio = "Robo de un diamante maldito durante una gala de disfraces.", DescripcionPrompt = "un atraco en el museo de arte moderno donde se robó un diamante maldito durante una gala", Coartada = "encerrado en el baño de un tailandés", Actitud = "Paranoico", EsCulpable = true, Secreto = "El diamante está en su zapato." },
        new GameContext.CasoDelito { ID = "087", TituloFolio = "ENVENENAMIENTO", DescripcionFolio = "Asesinato por envenenamiento con pudin en el asilo de ancianos.", DescripcionPrompt = "el envenenamiento de un millonario usando pudin de chocolate caducado en un asilo", Coartada = "haciendo espiritismo clandestino en el sótano", Actitud = "Sarcástico", EsCulpable = false, Secreto = "Invocaba el fantasma de su hámster." },
        new GameContext.CasoDelito { ID = "104", TituloFolio = "SECUESTRO VIRTUAL", DescripcionFolio = "Secuestro de un famoso Youtuber mientras emitía en directo.", DescripcionPrompt = "el secuestro de un youtuber famoso irrumpiendo en su mansión en pleno directo", Coartada = "grabando TikToks con cosplay de Batman", Actitud = "Pedante", EsCulpable = true, Secreto = "Perdió su móvil en la mansión." },
        new GameContext.CasoDelito { ID = "019", TituloFolio = "SABOTAJE ANIMAL", DescripcionFolio = "Liberación ilegal de pingüinos pigmeos en el puerto de la ciudad.", DescripcionPrompt = "haber liberado cien pingüinos pigmeos de un carguero en el puerto", Coartada = "persiguiendo ovnis en el bosque oscuro", Actitud = "Lloriqueando", EsCulpable = false, Secreto = "Robaba el WiFi del McDonald's." },
        new GameContext.CasoDelito { ID = "055", TituloFolio = "AGRESIÓN FRIKI", DescripcionFolio = "Agresión con sables láser de juguete en la convención de cómics.", DescripcionPrompt = "haber agredido violentamente al organizador de una convención de cómics usando réplicas de sables láser", Coartada = "en una cita con 'El Rey Lagarto'", Actitud = "Sabelotodo", EsCulpable = true, Secreto = "Rompió su sable en la cabeza de la víctima." },
        new GameContext.CasoDelito { ID = "092", TituloFolio = "FALSIFICACIÓN", DescripcionFolio = "Falsificación de obras de arte contemporáneo usando macarrones con queso.", DescripcionPrompt = "una estafa vendiendo cuadros falsos de Picasso hechos de macarrones con queso y pintura", Coartada = "robando WiFi desde mi maletero", Actitud = "Seductor", EsCulpable = false, Secreto = "Huía de un prestamista colombiano." }
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

    private static string ObtenerPromptDinamico(NivelInteligencia inteligencia, bool esCulpable)
    {
        string estadoCulpa = esCulpable ? "CULPABLE" : "INOCENTE";
        string reglaSecreto = esCulpable 
            ? "3. El Secreto: Como el sospechoso es CULPABLE, genera una prueba física irrefutable que demuestre que cometió el crimen." 
            : "3. El Secreto: Como el sospechoso es INOCENTE, genera una verdad vergonzosa o humillante que demuestra que estaba haciendo su coartada, explicando por qué mintió y se ocultó.";

        string prompt = $@"Eres un generador de datos policiales. Debes rellenar una ficha policial con texto simple.
No escribas introducciones. Escribe en perfecto español.

INSTRUCCIONES DE GENERACIÓN:
Tienes que INVENTAR un caso policial, una coartada para el sospechoso y una actitud.
¡MUY IMPORTANTE! EL SOSPECHOSO DE ESTE CASO ES {estadoCulpa}. Debes generar SOLO UN SECRETO que concuerde con que es {estadoCulpa}.
";

        if (inteligencia == NivelInteligencia.Simple)
        {
            prompt += $@"
REGLAS PARA EL CASO (NIVEL BÁSICO):
1. El Crimen: Debe ser un robo o estafa común en un lugar específico.
2. La Coartada: El sospechoso afirma que estaba haciendo algo normal CERCA del lugar del crimen a esa misma hora.
{reglaSecreto}
4. Todo debe tener una lógica perfecta. Si el crimen es en una panadería, la coartada debe involucrar pan o comida, y el secreto debe relacionarse con eso.
5. FORMATO POLICIAL (MUY IMPORTANTE): Redacta TODOS los campos ('coartada', 'actitud', 'secreto') de forma neutral, en tercera persona o infinitivo. NUNCA uses ""Tú"" o ""Yo"".
6. ACTITUD BREVE: Usa solo 1 o 2 adjetivos (ej: ""Nervioso"").

EJEMPLO 1 (Asumiendo sospechoso {estadoCulpa}):
TITULO: ROBO DE COCHES
DESCRIPCION_FOLIO: Robo de un sedán en el aparcamiento del parque.
DESCRIPCION_PROMPT: el robo de un coche en el parque
COARTADA: Pasear al perro por el parque.
ACTITUD: Nervioso.
SECRETO: {(esCulpable ? "Tener las llaves del coche robado en el bolsillo." : "No tiene perro, estaba espiando a su ex pareja en el parque.")}

IMPORTANTE: RELLENA ESTA PLANTILLA EXACTAMENTE CON EL MISMO FORMATO Y LAS MISMAS ETIQUETAS EN MAYÚSCULAS:
TITULO: [TITULO CORTO]
DESCRIPCION_FOLIO: [Descripción corta]
DESCRIPCION_PROMPT: [Descripción detallada]
COARTADA: [Coartada inventada]
ACTITUD: [Actitud breve]
SECRETO: [Secreto crítico]
";
        }
        else if (inteligencia == NivelInteligencia.Complejo)
        {
            prompt += $@"
REGLAS PARA EL CASO (NIVEL EXPERTO):
1. El Crimen: Un crimen bizarro, muy específico y original.
2. La Coartada: Una excusa extraña pero creíble que ubica al sospechoso en la escena del crimen, haciendo otra cosa.
{reglaSecreto}
4. LÓGICA: El jugador debe poder conectar la coartada con el secreto de forma deductiva e intuitiva. 
5. FORMATO POLICIAL (MUY IMPORTANTE): Redacta TODOS los campos ('coartada', 'actitud', 'secreto') de forma neutral, en tercera persona o infinitivo. NUNCA uses ""Tú"" o ""Yo"".
6. ACTITUD BREVE: Usa solo 1 o 2 adjetivos (ej: ""Sarcástico"").

EJEMPLO DE INSPIRACIÓN (Asumiendo sospechoso {estadoCulpa}):
TITULO: SABOTAJE DEL ZOO
DESCRIPCION_FOLIO: Liberación de los pingüinos rompiendo el cristal.
DESCRIPCION_PROMPT: haber roto el cristal para liberar a los pingüinos
COARTADA: Comer un helado frente al recinto de los pingüinos.
ACTITUD: Sarcástico.
SECRETO: {(esCulpable ? "Ocultar un martillo manchado de hielo y agua." : "Llorar frente a los pingüinos porque su helado se había caído al suelo.")}

IMPORTANTE: RELLENA ESTA PLANTILLA EXACTAMENTE CON EL MISMO FORMATO Y LAS MISMAS ETIQUETAS EN MAYÚSCULAS:
TITULO: [TITULO CORTO]
DESCRIPCION_FOLIO: [Descripción corta]
DESCRIPCION_PROMPT: [Descripción detallada]
COARTADA: [Coartada inventada]
ACTITUD: [Actitud breve]
SECRETO: [Secreto crítico]
";
        }
        else // Medio
        {
            prompt += $@"
REGLAS PARA EL CASO (NIVEL MEDIO):
1. El Crimen: Un delito curioso (ej: sabotaje, hurto inusual).
2. La Coartada: Afirma que estaba ocupado en el mismo lugar del crimen haciendo una actividad paralela.
{reglaSecreto}
4. CONEXIÓN INTUITIVA: La coartada y el crimen deben estar tan relacionados que el secreto parezca obvio si el jugador piensa con lógica.
5. FORMATO POLICIAL (MUY IMPORTANTE): Redacta TODOS los campos ('coartada', 'actitud', 'secreto') de forma neutral, en tercera persona o infinitivo. NUNCA uses ""Tú"" o ""Yo"".
6. ACTITUD BREVE: Usa solo 1 o 2 adjetivos (ej: ""Arrogante"").

EJEMPLO (Asumiendo sospechoso {estadoCulpa}):
TITULO: SABOTAJE INFORMÁTICO
DESCRIPCION_FOLIO: Infección de un virus en la sala de ordenadores de la biblioteca.
DESCRIPCION_PROMPT: haber metido un virus en los ordenadores de la biblioteca
COARTADA: Jugar videojuegos en un portátil en la biblioteca.
ACTITUD: Arrogante.
SECRETO: {(esCulpable ? "El virus se transmitió desde su propio pendrive negro." : "Estaba jugando al solitario porque no tiene amigos online.")}

IMPORTANTE: RELLENA ESTA PLANTILLA EXACTAMENTE CON EL MISMO FORMATO Y LAS MISMAS ETIQUETAS EN MAYÚSCULAS:
TITULO: [TITULO CORTO]
DESCRIPCION_FOLIO: [Descripción corta]
DESCRIPCION_PROMPT: [Descripción detallada]
COARTADA: [Coartada inventada]
ACTITUD: [Actitud breve]
SECRETO: [Secreto crítico]
";
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

    /// <summary>
    /// Intenta generar un caso con la IA. Evita generaciones paralelas.
    /// </summary>
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

    private async Task<GameContext.CasoDelito> GenerarCasoInternoAsync()
    {
        if (iaConfig == null)
        {
            Debug.LogWarning("[CaseGenerator] IAConfig no asignado. Usando fallback.");
            return ObtenerFallback();
        }

        // PRE-DETERMINAR CULPABILIDAD ANTES DE LLAMAR A LA IA
        bool esCulpableRnd = Random.value > 0.5f;

        for (int intento = 0; intento < iaConfig.maxReintentos; intento++)
        {
            Debug.Log($"[CaseGenerator] Intento {intento + 1}/{iaConfig.maxReintentos} (Culpable: {esCulpableRnd})...");

            // Iniciar medición de métricas
            if (LatencyMetrics.Instance != null)
                LatencyMetrics.Instance.IniciarMedicion(iaConfig.nombreModeloCasos, "caso");

            string respuesta = await EnviarPeticion(esCulpableRnd);

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
                caso.EsCulpable = esCulpableRnd;

                if (LatencyMetrics.Instance != null)
                    LatencyMetrics.Instance.FinalizarMedicion(respuesta, false);

                Debug.Log($"[CaseGenerator] ✅ Caso generado: {caso.TituloFolio} | Coartada: {caso.Coartada} | Culpable: {caso.EsCulpable}");
                return caso;
            }

            Debug.LogWarning($"[CaseGenerator] Intento {intento + 1}: No se pudo parsear o hubo un error. Reintentando en 3 segundos...");
            await Task.Delay(3000); // Pausa para no saturar el LLM en caso de error
        }

        Debug.LogWarning("[CaseGenerator] Todos los intentos fallaron. Usando fallback.");
        return ObtenerFallback();
    }

    private async Task<string> EnviarPeticion(bool esCulpable)
    {
        NivelInteligencia inteligencia = ObtenerNivelInteligencia(iaConfig?.nombreModeloCasos);
        
        var messages = new List<object>
        {
            new { role = "user", content = ObtenerPromptDinamico(inteligencia, esCulpable) }
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
            
            // Forzar un timeout largo (mínimo 120s) porque los modelos pesados pueden tardar mucho en responder
            int timeoutAsignado = Mathf.RoundToInt(iaConfig.tiempoTimeout);
            request.timeout = timeoutAsignado < 120 ? 120 : timeoutAsignado; 

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
    /// Parsea el texto de forma MUY tolerante usando expresiones regulares.
    /// Extrae los valores basándose en etiquetas "CLAVE: Valor", ignorando formatos JSON problemáticos.
    /// </summary>
    private GameContext.CasoDelito ValidarYParsear(string texto)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(texto)) return null;

            texto = texto.Trim();

            string titulo = ExtraerValorRegex(texto, "TITULO");
            string descripcionFolio = ExtraerValorRegex(texto, "DESCRIPCION_FOLIO");
            string descripcionPrompt = ExtraerValorRegex(texto, "DESCRIPCION_PROMPT");
            string coartada = ExtraerValorRegex(texto, "COARTADA");
            string actitud = ExtraerValorRegex(texto, "ACTITUD");
            string secreto = ExtraerValorRegex(texto, "SECRETO");

            // Intentar con variantes comunes por si la IA es rebelde
            if (string.IsNullOrWhiteSpace(titulo)) titulo = ExtraerValorRegex(texto, "TÍTULO") ?? ExtraerValorRegex(texto, "TITLE");
            if (string.IsNullOrWhiteSpace(coartada)) coartada = ExtraerValorRegex(texto, "ALIBI") ?? ExtraerValorRegex(texto, "EXCUSA");
            if (string.IsNullOrWhiteSpace(secreto)) secreto = ExtraerValorRegex(texto, "SECRET") ?? ExtraerValorRegex(texto, "PISTA");

            // Validaciones mínimas
            if (string.IsNullOrWhiteSpace(titulo))
            {
                Debug.LogWarning("[CaseGenerator] Campo 'TITULO' vacío o no encontrado. Texto raw: " + texto);
                return null;
            }
            if (string.IsNullOrWhiteSpace(coartada))
            {
                Debug.LogWarning("[CaseGenerator] Campo 'COARTADA' vacío o no encontrado");
                return null;
            }

            // Rellenar campos faltantes con defaults razonables
            if (string.IsNullOrWhiteSpace(descripcionPrompt)) descripcionPrompt = descripcionFolio ?? titulo;
            if (string.IsNullOrWhiteSpace(descripcionFolio)) descripcionFolio = descripcionPrompt;
            if (string.IsNullOrWhiteSpace(actitud)) actitud = "Nervioso";

            return new GameContext.CasoDelito
            {
                ID = Random.Range(100, 999).ToString("000"),
                TituloFolio = titulo.ToUpper().Trim(),
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

    /// <summary>
    /// Extrae un valor de la forma "Etiqueta: Valor" hasta el final de la línea.
    /// Tolera sintaxis JSON (comillas, comas) por si la IA se confunde y genera JSON.
    /// </summary>
    private string ExtraerValorRegex(string texto, string etiqueta)
    {
        // Soporta tanto "ETIQUETA:" como "\"ETIQUETA\":"
        var match = Regex.Match(texto, $@"{etiqueta}\""?\s*:\s*([^\r\n]+)", RegexOptions.IgnoreCase);
        if (match.Success)
        {
            string valor = match.Groups[1].Value.Trim();
            
            // Limpiar coma final de JSON si existe
            if (valor.EndsWith(",")) 
                valor = valor.Substring(0, valor.Length - 1).Trim();
                
            // Limpiar comillas si las puso
            if (valor.StartsWith("\"") && valor.EndsWith("\"") && valor.Length > 1)
                valor = valor.Substring(1, valor.Length - 2).Trim();
                
            return valor;
        }
        return string.Empty;
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
