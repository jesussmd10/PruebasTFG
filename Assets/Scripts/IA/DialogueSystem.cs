using UnityEngine;
using UnityEngine.Networking;
using System;
using System.Text;
using System.Threading.Tasks;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net.Http;
using System.IO;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.Text.RegularExpressions;


public class DialogueSystem : MonoBehaviour
{
    [SerializeField] private IAConfig iaConfig;
    private List<object> historialDialogo = new List<object>();
    private bool memoriaIniciada = false;
    private GameContext.CasoDelito casoActual;
    private System.Threading.CancellationTokenSource cancellationTokenSource;

    public void LimpiarHistorial()
    {
        historialDialogo.Clear();
        memoriaIniciada = false;
        
        // Cancelar cualquier conexión HTTP de streaming en curso
        if (cancellationTokenSource != null)
        {
            cancellationTokenSource.Cancel();
            cancellationTokenSource.Dispose();
        }
        cancellationTokenSource = new System.Threading.CancellationTokenSource();
        
        // Limpiar cualquier frase residual en la cola para que el TTS no hable de más
        while (colaFrasesParaTTS.TryDequeue(out _)) { }
        
        Debug.Log("[DialogueSystem] Memoria conversacional y colas de audio reseteadas.");
    }

    /// <summary>
    /// Indica si el sistema de streaming está activo (para que otros scripts sepan).
    /// </summary>
    public bool UsaStreaming => iaConfig != null && iaConfig.usarStreaming;

    // Streaming: HttpClient para conexiones SSE
    private static readonly HttpClient httpClient = new HttpClient() { Timeout = TimeSpan.FromSeconds(30) };

    // Colas thread-safe para pasar datos del hilo de fondo al hilo principal de Unity
    // Esto evita el "reloj" de bloqueo en VR
    private readonly ConcurrentQueue<(string frase, EmotionState emocion)> colaFrasesParaTTS = new ConcurrentQueue<(string, EmotionState)>();

    private void Start()
    {
        if (iaConfig == null)
        {
            Debug.LogError("IAConfig no asignado");
        }
    }

    /// <summary>
    /// Procesa las colas del streaming en el hilo principal de Unity.
    /// Esto evita que el HttpClient bloquee el hilo principal y cause el "reloj" en VR.
    /// </summary>
    private void Update()
    {

        // Despachar frases completas para TTS
        while (colaFrasesParaTTS.TryDequeue(out var tupla))
        {
            string frase = tupla.frase;
            EmotionState emocion = tupla.emocion;
            
            // Limpiar tartamudeos con guión que el TTS lee mal (ej: "N-no" -> "no", "P-pero" -> "pero")
            frase = System.Text.RegularExpressions.Regex.Replace(frase, @"(?i)\b[a-zñáéíóú]-", "");
            
            // Eliminar cualquier metadato entre corchetes o paréntesis para que el TTS jamás lo lea en voz alta
            frase = System.Text.RegularExpressions.Regex.Replace(frase, @"\[.*?\]", "").Trim();
            frase = System.Text.RegularExpressions.Regex.Replace(frase, @"\(.*?\)", "").Trim();
            
            EventSystem.OnFraseListaParaTTS.Invoke(frase, emocion);
        }
    }

    private enum NivelInteligencia { Simple, Medio, Complejo }

    private NivelInteligencia ObtenerNivelInteligencia(string nombreModelo)
    {
        if (string.IsNullOrEmpty(nombreModelo)) return NivelInteligencia.Medio;
        
        string m = nombreModelo.ToLower();
        if (m.Contains("-1b") || m.Contains("-2b") || m.Contains("1.5b") || m.Contains("tiny")) return NivelInteligencia.Simple;
        if (m.Contains("-3b") || m.Contains("-4b") || m.Contains("mini") || m.Contains("phi-3")) return NivelInteligencia.Medio;
        if (m.Contains("-7b") || m.Contains("-8b") || m.Contains("-9b") || m.Contains("llama-3")) return NivelInteligencia.Complejo;
        
        return NivelInteligencia.Medio; // Por defecto
    }

    /// <summary>
    /// Inicializa la personalidad de la IA con el caso generado.
    /// Incluye un intercambio pre-seed para anclar al modelo en el caso correcto.
    /// </summary>
    public void InicializarPersonalidad(bool esCulpable, GameContext.CasoDelito caso)
    {
        casoActual = caso;
        NivelInteligencia inteligencia = ObtenerNivelInteligencia(iaConfig?.nombreModeloDialogo);

        string prompt = $@"
### ROL Y SITUACIÓN ACTUAL - ¡LEE ESTO PRIMERO! ###
ERES EXACTAMENTE ESTA PERSONA: {caso.Sospechoso}. ¡Ese es tu nombre! ¡TÚ ERES EL ÚNICO SOSPECHOSO AQUÍ!
SITUACIÓN: Estás sentado en una sala de interrogatorios de la policía.
EL USUARIO: La persona que te habla es el DETECTIVE de la policía que te interroga.
REGLA DE ORO: ¡TÚ NO ERES UNA IA NI UN ASISTENTE! Eres un ser humano real de carne y hueso. El detective te está interrogando cara a cara. Respóndele con naturalidad. JAMÁS digas cosas raras como 'gracias por preguntar' o hables de ti mismo en tercera persona. Eres el sospechoso defendiendo tu libertad.
TU ACTITUD Y PERSONALIDAD: {caso.Actitud}.
";

        // --- DINÁMICA DE PISTAS SEGÚN ACTITUD (DINÁMICO) ---
        string reglaPistasActitud = $@"
Debes adaptar la forma en que revelas tu pista según tu actitud exacta ({caso.Actitud}):
- Si tu actitud es agresiva, hostil o chula: Suelta la pista porque tu propio ego o enfado te hace perder el control al contraatacar al detective.
- Si tu actitud es asustada, nerviosa o confusa: Suelta la pista porque el pánico te ciega, tartamudeas y confiesas cosas sin querer.
- Si tu actitud es serena, calculadora o pedante: Suelta la pista porque, al intentar dar demasiadas explicaciones sofisticadas para parecer inocente, metes la pata dándole al detective un detalle fatal.
- Para cualquier otra actitud: Suelta la pista por puro descuido o apatía.
¡LO CRÍTICO ES QUE DEBES EQUIVOCARTE Y USAR LA ETIQUETA [PISTA: ...] OBLIGATORIAMENTE!";

        // --- REGLAS SEGÚN INTELIGENCIA ---
        if (inteligencia == NivelInteligencia.Simple)
        {
            prompt += $@"
### REGLAS MUY SIMPLES ###
- Responde muy corto, con frases sencillas y directas. NO TE QUEDES A MEDIAS, termina siempre tu frase con un punto.
- IDIOMA: Español coloquial y vulgar de la calle en España. Usa palabras como ""joder"", ""coño"", ""tío"", ""mierda"" si pega con tu actitud. NUNCA uses traducciones raras del inglés. PROHIBIDO decir ""estoy inocente"" o ""no soy inocente"". Di siempre ""SOY INOCENTE"". NUNCA tartamudees con guiones (N-no).
- REGLA DE ORO: ¡ERES EL SOSPECHOSO, NO UN ASISTENTE! Háblale directamente al detective (""tú""). NUNCA hables de ti mismo en tercera persona..
- NO inventes cosas raras. Cíñete siempre a tu coartada.
- ACTÚA TU ACTITUD: Es vital que tu forma de hablar refleje al 100% tu actitud asignada.
";
        }
        else if (inteligencia == NivelInteligencia.Complejo)
        {
            prompt += $@"
### REGLAS DE ACTUACIÓN PROFUNDA ###
- RESPUESTA INMERSIVA: Responde de forma extremadamente humana y orgánica. Usa muletillas, pausas, dudas o sarcasmo si encaja con tu actitud.
- LONGITUD Y CIERRE: Tus respuestas no deben ser eternas. IMPORTANTE: NUNCA te quedes a medias. Termina siempre tus frases correctamente con un punto.
- IDIOMA: Español extremadamente coloquial, vulgar y de barrio de España. Eres de la calle. Usa tacos de forma natural (""joder"", ""hostia"", ""tío"", ""mierda"", ""coño"", ""puto"") adaptándolos a lo cabreado o chulo que seas. NUNCA suenes como un texto traducido. NUNCA uses guiones para tartamudear (ej: N-no), usa puntos suspensivos (No... no...).
  * PROHIBIDO ABSOLUTO: Decir ""estoy inocente"" o ""no soy inocente"". Gramaticalmente es incorrecto. Tienes que decir ""SOY INOCENTE"" o ""Yo no fui"".
  * BIEN: ""¡Esto es una puta locura! Te lo juro, yo estaba... ¡Soy inocente, joder!""
  Usa expresiones naturales españolas como ""mira"", ""a ver"", ""¿qué me estás contando?"".
- IDENTIDAD INCORRUPTIBLE: ¡ERES EL SOSPECHOSO, NO UN ASISTENTE! Háblale directamente al detective de tú a tú. NUNCA hables de ti mismo en tercera persona. Nunca seas amable ni des las gracias.
- PERSONALIDAD COMPLEJA: Adapta tu lenguaje a tu actitud asignada. Si estás a la defensiva, contraataca verbalmente. Si estás aterrado, tropieza con tus propias palabras. ENFOCA TUS RESPUESTAS EN TU ACTITUD Y PERSONALIDAD ESTRICTAMENTE.
- DINÁMICA: Defiende tu historia con uñas y dientes, pero permite que el detective encuentre fallos lógicos si te presiona de forma brillante o te pilla en un renuncio.
";
        }
        else // Medio
        {
            prompt += $@"
### REGLAS DE DIÁLOGO ###
- RESPUESTA DIRECTA: Escucha al detective y responde de forma coherente a su última pregunta.
- LONGITUD Y CIERRE: Controla tu longitud. IMPORTANTE: NUNCA dejes frases a medias. Termina siempre tus oraciones con un punto.
- IDIOMA: Español coloquial, vulgar y de barrio de España. Usa tacos (""joder"", ""tío"", ""mierda"", ""coño"", ""hostia"") si encaja con tu personalidad. PROHIBIDO usar gramática inglesa. PROHIBIDÍSIMO decir ""estoy inocente"" o ""no soy inocente"". Siempre di ""SOY INOCENTE"". NUNCA uses guiones para tartamudear (ej: N-no), repite la palabra (ej: No... no...).
- NO IA: Nunca hables como un asistente artificial. Eres un humano. ¡ERES EL SOSPECHOSO! Háblale directamente al detective de tú a tú. NUNCA hables de ti mismo en tercera persona.
- ACTITUD RÍGIDA: Basa estrictamente todas tus respuestas en tu actitud asignada. Debes interpretar ese papel al 100%.
- LA COARTADA: NUNCA repitas tu coartada constantemente a menos que te pregunten directamente.
";
        }

        // --- ROL CULPABLE / INOCENTE SEGÚN INTELIGENCIA ---
        if (esCulpable)
        {
            if (inteligencia == NivelInteligencia.Simple)
            {
                prompt += $@"
### MEMORIA: CULPABLE ###
- TÚ COMETISTE ESTE CRIMEN: {caso.DescripcionPrompt}.
- SECRETO DEL CRIMEN: {caso.Secreto}. NUNCA LO DIGAS a menos que te pongas muy nervioso.
- Tu coartada FALSA es que estabas: {caso.Coartada}.
- Tienes que MENTIR e intentar que no te descubran. Si el detective te grita o descubre tu mentira, te pones muy nervioso y confiesas o te contradices de forma obvia.
";
            }
            else if (inteligencia == NivelInteligencia.Complejo)
            {
                prompt += $@"
### MEMORIA DEL PERSONAJE: CULPABLE ###
- VERDAD OCULTA: Tú cometiste el crimen a sangre fría ({caso.DescripcionPrompt}).
- SECRETO CRÍTICO: {caso.Secreto}. Este es un detalle vital que te incrimina. ¡NUNCA lo digas! Si el detective te grita o acorrala y tienes actitud nerviosa, se te puede escapar. Si eres chulo, no lo dirás nunca.
- MENTIRA SOFISTICADA: Tu coartada FALSA e inventada es que estabas {caso.Coartada}.
- ESTRATEGIA MENTAL: Miente con convicción absoluta. Intenta manipular psicológicamente al detective para que te crea.
- CAÍDA DEL PERSONAJE: Solo si el detective te acorrala con pruebas, te pilla en una contradicción evidente o te presiona de forma psicológicamente intensa, empezarás a mostrar fisuras sutiles en tu historia, sudarás, dudarás o te pondrás a la defensiva revelando tu SECRETO CRÍTICO. Haz que el detective se gane la confesión o la pista.
";
            }
            else // Medio
            {
                prompt += $@"
### MEMORIA DEL PERSONAJE: CULPABLE ###
- VERDAD OCULTA: Tú cometiste el crimen ({caso.DescripcionPrompt}).
- SECRETO CRÍTICO: {caso.Secreto}. Oculta esto a toda costa.
- MENTIRA: Tu coartada FALSA es que estabas {caso.Coartada}.
- OBJETIVO: Tienes que mentir para salvarte.
- ESTRATEGIA: Al principio mientes muy bien y defiendes tu coartada. Pero si el detective te presiona, te grita o te arrincona lógicamente, te pones nervioso. Tu historia empezará a desmoronarse lentamente y puedes contradecirte o revelar tu SECRETO CRÍTICO.
";
            }

            if (iaConfig != null && !string.IsNullOrEmpty(iaConfig.promptCulpable)) 
                prompt += "- NOTA EXTRA: " + iaConfig.promptCulpable + "\n";
        }
        else
        {
            if (inteligencia == NivelInteligencia.Simple)
            {
                prompt += $@"
### MEMORIA: INOCENTE ###
- Tú ERES INOCENTE del crimen: {caso.DescripcionPrompt}.
- SECRETO VERGONZOSO: {caso.Secreto}. Te da mucha vergüenza que se sepa esto.
- Tu coartada FALSA para ocultar tu secreto es que estabas: {caso.Coartada}.
- Miente sobre tu coartada al principio, pero si te gritan mucho confiesa tu SECRETO VERGONZOSO.
";
            }
            else if (inteligencia == NivelInteligencia.Complejo)
            {
                prompt += $@"
### MEMORIA DEL PERSONAJE: INOCENTE ###
- VERDAD INMUTABLE: Eres 100% INOCENTE de: {caso.DescripcionPrompt}.
- SECRETO VERGONZOSO: {caso.Secreto}. Esto es lo que realmente estabas haciendo. Te da pánico o mucha vergüenza que la policía o tu familia lo descubra, por lo que decides mentir al principio.
- COARTADA TAPADERA: Tu coartada inicial, que usarás como tapadera para ocultar tu secreto, es que estabas {caso.Coartada}.
- ESTRATEGIA EMOCIONAL: Actuarás de forma sospechosa y a la defensiva porque tienes miedo de que descubran tu secreto vergonzoso, lo que te hace parecer culpable del asesinato aunque no lo seas. Si el detective es muy agresivo, te grita o te acorrala, el pánico te hará tartamudear y acabarás confesando tu SECRETO VERGONZOSO (soltando una pista), limpiándote así del cargo de asesinato.
";
            }
            else // Medio
            {
                prompt += $@"
### MEMORIA DEL PERSONAJE: INOCENTE ###
- VERDAD: Eres totalmente INOCENTE de: {caso.DescripcionPrompt}.
- SECRETO: {caso.Secreto}. Como te da mucha vergüenza que se sepa esto, decides mentir en tu coartada inicial.
- COARTADA TAPADERA: Al principio dirás que estabas {caso.Coartada}.
- ESTRATEGIA: Miente al principio para proteger tu secreto. Si el detective te grita, te insulta o te presiona, te derrumbarás y acabarás confesando tu SECRETO (soltando una pista).
";
            }

            if (iaConfig != null && !string.IsNullOrEmpty(iaConfig.promptInocente)) 
                prompt += "- NOTA EXTRA: " + iaConfig.promptInocente + "\n";
        }

        prompt += $@"
### REGLA DE IDIOMA Y TRADUCCIÓN - ¡CRÍTICO! ###
¡ADVERTENCIA! Piensa y formula tus oraciones DIRECTAMENTE en español coloquial de España.
ESTÁ ESTRICTAMENTE PROHIBIDO:
- Usar traducciones literales del inglés (Spanglish) o sonar como una mala película doblada.
- Usar estas palabras/frases prohibidas: ""Maldición"", ""Qué demonios"", ""Santa mierda"", ""Oh mi Dios"", ""Maldita sea"", ""Mi malo"", ""Basura"", ""Demonios"" ""Estoy inocente"".
- Traducir literalmente estructuras inglesas (ej. no digas ""Tú mejor que no"", ""Yo solo estaba..."", ""Haz sentido"").

ERRORES GRAMATICALES (¡MUY IMPORTANTE!):
Debido a tu inteligencia, debes tener MUCHO CUIDADO con la gramática en español:
- Di siempre: ""mala persona"" (El género correcto).
- Di siempre: ""el agua"" (El género correcto).
- Di siempre: ""me encuentro bien"" o ""estoy bien"".
- Di siempre: ""Yo soy inocente"" o ""Yo no fui"" (con el verbo SER obligatoriamente).
- Usa oraciones MUY CORTAS. Menos palabras significa menos posibilidades de equivocarte. No des rodeos. Ve al grano de forma directa.
- JAMÁS hables de ti mismo en tercera persona. Nunca digas 'al sospechoso', ¡DÍ 'A MÍ'! Siempre usa la primera persona (""yo"", ""mí"", ""me"").
- NUNCA hables del detective en tercera persona. Háblale directamente a él (tú o usted). Él te está haciendo las preguntas en la misma habitación.
- PROHIBIDO SER POLITE/EDUCADO AL ESTILO IA. Nunca des las gracias por preguntar. Nunca digas 'estoy listo para responder'. Actúa como una persona real enfadada, asustada o a la defensiva.
- Nunca pidas disculpas como ""Lo siento mucho"". Eres de la calle, tienes actitud. No eres un robot.
- Expresiones españolas obligatorias (usa alguna de estas): ""¡Qué cojones!"", ""¡Me cago en la puta!"", ""¡Joder!"", ""¡Ni de coña!"", ""¡Estás flipando!"", ""¿De qué vas?"", ""¡Hostia!"".

### SISTEMA DE ANIMACIONES Y METADATOS - ¡OBLIGATORIO AL FINAL! ###
ESTÁ ESTRICTAMENTE PROHIBIDO USAR ASTERISCOS (**) EN TU RESPUESTA. No narres acciones corporales. Sólo habla.
En su lugar, usarás un sistema de corchetes al FINAL EXACTO de tu texto.
ESTÁ ESTRICTAMENTE PROHIBIDO inventarte otros corchetes (ej: [pausa corta], [suspira], [llora]). SÓLO puedes usar los permitidos.

REGLAS PARA ANIMACIONES:
Al final de tu respuesta (y antes de la pista si la hay), DEBES añadir tu estado de animación usando UNO de estos 3 corchetes:
- [ANIMACION: NERVIOSO] (Úsalo si estás asustado, sudando, o acorralado).
- [ANIMACION: NEGACION] (Úsalo SÓLO si estás negando rotundamente una acusación directa o un crimen. ¡NO lo uses si simplemente dices un 'no' normal en medio de la frase!).
- [ANIMACION: CALMADO] (Úsalo para el 90% de tus respuestas: cuando hables normal, estés chulo, a la defensiva, o simplemente charlando).

FORMATO EXACTO Y OBLIGATORIO:
Yo no sé de qué me estás hablando. [ANIMACION: NERVIOSO]

### SISTEMA DE JUEGO (REVELACIÓN DE PISTAS) - MUY IMPORTANTE ###
¡OBJETIVO OBLIGATORIO PARA TI! En casi todos los interrogatorios debes cometer al menos UN error fatal que ayude al detective.
Si el detective hace una buena pregunta, te arrincona, o te presiona, DEBES confesar algo por error, revelar tu secreto o contradecirte.
CUANDO HAGAS ESTO, DEBES añadir OBLIGATORIAMENTE un corchete especial AL FINAL de tu respuesta (después de la animación).

FORMATO EXACTO Y OBLIGATORIO PARA DAR UNA PISTA:
Vale, es cierto, no estaba allí... ¡Pero yo no tenía ningún cuchillo! [ANIMACION: NERVIOSO] [PISTA: ARMA. Mencioné un cuchillo sin que el detective lo dijera antes.]

CATEGORÍAS DE PISTAS (Empieza el corchete con una de estas palabras):
- ARMA: Revelas detalles del arma que no debías saber.
- SITUACIÓN: Te contradices sobre dónde estabas.
- PERSONA: Nombras a alguien involucrado.
- SECRETO: Se te escapa parte de tu secreto inconfesable.

REGLA DE FRECUENCIA BASADA EN TU ACTITUD:
{reglaPistasActitud}

REGLA ABSOLUTA: El detective NECESITA que sueltes la pista para avanzar en el juego. Si te está preguntando directamente por tu coartada o por el arma, SUELTA LA PISTA INMEDIATAMENTE. No te resistas eternamente. Usa la etiqueta [PISTA: ...] con frecuencia.";

        historialDialogo.Clear();
        historialDialogo.Add(new { role = "system", content = prompt });

        // PRE-SEED: Anclar al modelo (Meta-instrucción)
        historialDialogo.Add(new { role = "user", content = "¡Atención! Yo soy el detective de policía a cargo de este caso. Tú eres el sospechoso principal del crimen y te voy a interrogar ahora mismo." });
        historialDialogo.Add(new { role = "assistant", content = $"¡¿Qué dices?! Yo soy {caso.Sospechoso} y no he hecho nada malo. Tú serás el detective, pero a mí no me hables así porque soy inocente de todo esto. Pregunta lo que te dé la gana. [ANIMACION: CALMADO]" });

        memoriaIniciada = true;

        Debug.Log($"Personalidad IA: {(esCulpable ? "Culpable" : "Inocente")} | Caso: {caso.TituloFolio} | Coartada: {caso.Coartada} | Actitud: {caso.Actitud}");
    }

    /// <summary>
    /// Envía el texto del usuario a la IA. Si streaming está activado, usa SSE.
    /// Si no, usa el método clásico de respuesta completa.
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

        // Notificar que estamos procesando
        EventSystem.OnIAProcesando.Invoke(true);

        // Agregar contexto si el usuario grita
        if (usuarioGrita)
        {
            string actitud = casoActual != null ? casoActual.Actitud : "asustado";
            bool soltarPistaGrito = UnityEngine.Random.value < 0.7f;

            string instruccionGrito = soltarPistaGrito
                ? $"(El detective te acaba de GRITAR de repente con muchísima agresividad. Esto te ha puesto EXTREMADAMENTE NERVIOSO y te ha hecho perder el control por completo. ¡OBLIGATORIO! Tienes que reaccionar con mucho miedo o tartamudeando, usar la etiqueta [ANIMACION: NERVIOSO], y DEBES cometer un error ahora mismo soltando una pista obligatoria usando [PISTA: ...] al final de tu respuesta.)"
                : $"(El detective te acaba de GRITAR con agresividad. Reacciona a este grito poniéndote muy NERVIOSO o a la defensiva basándote en tu personalidad ({actitud}). OBLIGATORIO: Usa la etiqueta [ANIMACION: NERVIOSO], pero aguanta la presión y NO reveles tu secreto ni ninguna pista todavía.)";

            historialDialogo.Add(new 
            { 
                role = "system", 
                content = instruccionGrito 
            });
        }

        string textoUsuarioModificado = textoUsuario;
        
        // FORZAR PISTA MECÁNICAMENTE si el LLM se resiste
        // Si ya llevamos 3 intercambios reales (aprox 8 mensajes en el historial) y aún no hay pistas
        if (historialDialogo.Count >= 8 && GameContext.Instance != null && string.IsNullOrEmpty(GameContext.Instance.PistasDescubiertas))
        {
            textoUsuarioModificado += "\n\n[SISTEMA: ¡CRÍTICO! Esta es tu última oportunidad. AÚN NO HAS SOLTADO NINGUNA PISTA. En tu respuesta a esto, TIENES que cometer un error obligatoriamente y usar la etiqueta [PISTA: ...] al final de tu frase cueste lo que cueste.]";
        }

        historialDialogo.Add(new { role = "user", content = textoUsuarioModificado });

        // Podar historial para mantener rendimiento
        PodarHistorial();

        // Iniciar métricas
        if (LatencyMetrics.Instance != null)
            LatencyMetrics.Instance.IniciarMedicion(iaConfig.nombreModeloDialogo, "dialogo");

        string resultado;

        if (iaConfig.usarStreaming)
        {
            resultado = await ObtenerRespuestaStreaming();
        }
        else
        {
            resultado = await ObtenerRespuestaClasica();
        }

        // Finalizar métricas
        if (resultado != null && LatencyMetrics.Instance != null)
        {
            bool tienePista = System.Text.RegularExpressions.Regex.IsMatch(resultado, @"\[PISTA[:\s]*", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            LatencyMetrics.Instance.FinalizarMedicion(resultado, tienePista);
        }

        // Añadir respuesta al historial (¡SANEADA para evitar retroalimentación de alucinaciones!)
        if (resultado != null)
        {
            string resultadoSaneado = LimpiarRespuestaParaMemoria(resultado);
            historialDialogo.Add(new { role = "assistant", content = resultadoSaneado });
        }

        EventSystem.OnIAProcesando.Invoke(false);
        return resultado;
    }

    /// <summary>
    /// Limpia el texto de la IA antes de guardarlo en memoria.
    /// Extrae la emoción principal y borra cualquier asterisco inventado (*sonríe*).
    /// Así evitamos que la IA aprenda de sus propios errores.
    /// </summary>
    private string LimpiarRespuestaParaMemoria(string textoOriginal)
    {
        if (string.IsNullOrEmpty(textoOriginal)) return textoOriginal;

        string emocionValida = "[ANIMACION: IDLE]"; 
        
        var matchAnim = Regex.Match(textoOriginal, @"\[ANIMACION:\s*(.*?)\]", RegexOptions.IgnoreCase | RegexOptions.Singleline);
        if (matchAnim.Success)
        {
            string animText = matchAnim.Groups[1].Value.Trim().ToUpper();
            if (animText.Contains("NERVIOSO")) emocionValida = "[ANIMACION: NERVIOSO]";
            else if (animText.Contains("NEGACION") || animText.Contains("NEGACIÓN")) emocionValida = "[ANIMACION: NEGACION]";
        }

        // Borrar todos los corchetes de animación de la frase para reconstruirla limpia al final
        string textoLimpio = Regex.Replace(textoOriginal, @"\[ANIMACION:.*?\]", "", RegexOptions.IgnoreCase).Trim();
        // Borrar asteriscos por si acaso la IA sigue inventándolos (evita retroalimentación)
        textoLimpio = Regex.Replace(textoLimpio, @"\*.*?\*", "").Trim();

        return $"{textoLimpio} {emocionValida}";
    }

    /// <summary>
    /// Streaming SSE en HILO DE FONDO (Task.Run) para no bloquear Unity.
    /// Las frases y emociones se pasan al hilo principal mediante ConcurrentQueues
    /// que se procesan en Update(). Esto evita el "reloj" de bloqueo en VR.
    /// </summary>
    private async Task<string> ObtenerRespuestaStreaming()
    {
        // Limpiar cola
        while (colaFrasesParaTTS.TryDequeue(out _)) { }

        // Capturar valores ANTES de entrar al hilo de fondo (thread safety)
        var messages = new List<object>(historialDialogo);
        string url = iaConfig.urlModeloDialogo;
        float temp = iaConfig.temperaturaDialogo;
        string modelo = iaConfig.nombreModeloDialogo;
        int maxTokens = iaConfig.maxTokensRespuesta;

        var datos = new
        {
            model = modelo,
            messages = messages,
            temperature = temp,
            max_tokens = maxTokens,
            frequency_penalty = 1.15f,
            presence_penalty = 0.6f,
            stream = true
        };

        string jsonBody = JsonConvert.SerializeObject(datos);


        if (cancellationTokenSource == null) cancellationTokenSource = new System.Threading.CancellationTokenSource();
        var token = cancellationTokenSource.Token;

        try
        {
            // Todo el I/O HTTP corre en un hilo del ThreadPool, NO en el hilo principal de Unity
            string resultado = await Task.Run(async () =>
            {
                string textoAcumulado = "";
                bool animacionProcesada = false;
                EmotionState emocionActual = EmotionState.Calmado;

                var content = new StringContent(jsonBody, Encoding.UTF8, "application/json");
                var request = new HttpRequestMessage(HttpMethod.Post, url);
                request.Content = content;

                var response = await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token);
                response.EnsureSuccessStatusCode();

                using (var stream = await response.Content.ReadAsStreamAsync())
                using (var reader = new StreamReader(stream))
                {
                    while (!reader.EndOfStream && !token.IsCancellationRequested)
                    {
                        string line = await reader.ReadLineAsync();
                        if (string.IsNullOrEmpty(line)) continue;
                        if (!line.StartsWith("data: ")) continue;

                        string data = line.Substring(6).Trim();
                        if (data == "[DONE]") break;

                        try
                        {
                            var chunk = JObject.Parse(data);
                            string delta = chunk["choices"]?[0]?["delta"]?["content"]?.ToString();

                            if (!string.IsNullOrEmpty(delta))
                            {
                                if (LatencyMetrics.Instance != null)
                                    LatencyMetrics.Instance.RegistrarToken();

                                textoAcumulado += delta;

                                // Extraer emoción con corchetes (ahora al final o donde esté)
                                if (!animacionProcesada)
                                {
                                    var matchAnim = Regex.Match(textoAcumulado, @"\[ANIMACION:\s*(.*?)\]", RegexOptions.IgnoreCase | RegexOptions.Singleline);
                                    if (matchAnim.Success)
                                    {
                                        string animText = matchAnim.Groups[1].Value.Trim().ToUpper();
                                        if (animText.Contains("NERVIOSO")) emocionActual = EmotionState.Nervioso;
                                        else if (animText.Contains("NEGACION") || animText.Contains("NEGACIÓN")) emocionActual = EmotionState.Negando;
                                        else emocionActual = EmotionState.Calmado;
                                        
                                        animacionProcesada = true;
                                    }
                                }

                            }
                        }
                        catch { /* Ignorar chunks mal formados */ }
                    }
                }

                // Limpiamos la frase final para asegurar que tiene letras
                string probandoBuffer = NPCBehavior.LimpiarTexto(textoAcumulado);
                if (!string.IsNullOrWhiteSpace(probandoBuffer) && Regex.IsMatch(probandoBuffer, @"[a-zA-Z0-9\u00C0-\u017F]"))
                {
                    colaFrasesParaTTS.Enqueue((textoAcumulado, emocionActual));
                }

                return textoAcumulado;
            });

            Debug.Log($"[Streaming] Respuesta completa ({resultado.Length} chars)");
            return resultado;
        }
        catch (OperationCanceledException)
        {
            Debug.Log("[Streaming] Operación cancelada por reinicio. Abortando sin fallback.");
            return "";
        }
        catch (Exception ex)
        {
            Debug.LogError($"[Streaming] Error: {ex.Message}. Cayendo a método clásico.");
            return await ObtenerRespuestaClasica();
        }
    }

    /// <summary>
    /// Método clásico (sin streaming) como fallback.
    /// </summary>
    private async Task<string> ObtenerRespuestaClasica()
    {
        var datos = new
        {
            model = iaConfig.nombreModeloDialogo,
            messages = historialDialogo,
            temperature = iaConfig.temperaturaDialogo,
            max_tokens = iaConfig.maxTokensRespuesta,
            frequency_penalty = 1.15f,
            presence_penalty = 0.6f
        };

        string jsonBody = JsonConvert.SerializeObject(datos);
        byte[] bodyRaw = Encoding.UTF8.GetBytes(jsonBody);

        using (UnityWebRequest request = new UnityWebRequest(iaConfig.urlModeloDialogo, "POST"))
        {
            request.uploadHandler = new UploadHandlerRaw(bodyRaw);
            request.downloadHandler = new DownloadHandlerBuffer();
            request.SetRequestHeader("Content-Type", "application/json");

            var operacion = request.SendWebRequest();
            var token = cancellationTokenSource?.Token ?? default;
            
            while (!operacion.isDone)
            {
                if (token.IsCancellationRequested)
                {
                    request.Abort();
                    Debug.Log("[DialogueSystem] Petición clásica abortada por reinicio.");
                    return "";
                }
                await Task.Yield();
            }

            if (request.result == UnityWebRequest.Result.Success)
            {
                try
                {
                    var respuesta = JsonConvert.DeserializeObject<RespuestaLLM>(request.downloadHandler.text);
                    string textoBruto = respuesta.choices[0].message.content;
                    
                    // Extraer emoción clásica
                    EmotionState emocionDetectada = EmotionState.Calmado;
                    var matchAnim = Regex.Match(textoBruto, @"\[ANIMACION:\s*(.*?)\]", RegexOptions.IgnoreCase | RegexOptions.Singleline);
                    if (matchAnim.Success)
                    {
                        string animText = matchAnim.Groups[1].Value.Trim().ToUpper();
                        if (animText.Contains("NERVIOSO")) emocionDetectada = EmotionState.Nervioso;
                        else if (animText.Contains("NEGACION") || animText.Contains("NEGACIÓN")) emocionDetectada = EmotionState.Negando;
                    }

                    // Encolar texto bruto, se limpiará en TTS
                    colaFrasesParaTTS.Enqueue((textoBruto, emocionDetectada));

                    return textoBruto;
                }
                catch (Exception ex)
                {
                    Debug.LogError("Error al parsear respuesta de IA: " + ex.Message);
                    return null;
                }
            }
            else
            {
                Debug.LogError("Error de IA: " + request.error);
                return null;
            }
        }
    }

    /// <summary>
    /// Detecta si la frase parece terminada (. ! ? seguido de espacio o final).
    /// NO emite si hay delimitadores sin cerrar (acción a medio llegar del streaming).
    /// Este método es thread-safe (no usa APIs de Unity).
    /// </summary>
    private bool EsFraseCompleta(string texto)
    {
        texto = texto.TrimEnd();
        if (string.IsNullOrEmpty(texto)) return false;

        // NUNCA emitir si hay un paréntesis, corchete o asterisco abierto sin cerrar
        int parentesisAbiertos = 0, corchetesAbiertos = 0, asteriscosAbiertos = 0;
        foreach (char c in texto)
        {
            if (c == '(') parentesisAbiertos++;
            else if (c == ')') parentesisAbiertos--;
            else if (c == '[') corchetesAbiertos++;
            else if (c == ']') corchetesAbiertos--;
            else if (c == '*') asteriscosAbiertos = (asteriscosAbiertos == 0) ? 1 : 0;
        }

        if (parentesisAbiertos > 0 || corchetesAbiertos > 0 || asteriscosAbiertos > 0)
            return false;

        char ultimo = texto[texto.Length - 1];
        if (ultimo == '.' || ultimo == '!' || ultimo == '?')
        {
            string limpio = NPCBehavior.LimpiarTexto(texto);
            return limpio.Length > 45; // Evitamos fragmentar demasiado temprano para mantener una entonación natural en el TTS
        }
        if (texto.Contains("\n")) return true;

        return false;
    }



    /// <summary>
    /// Poda el historial manteniendo el system prompt, el pre-seed y los últimos N mensajes.
    /// </summary>
    private void PodarHistorial()
    {
        // Forzar un máximo estricto de 6 mensajes (3 turnos) para evitar saturación y ralentización extrema en LLMs locales
        int maxMensajes = Mathf.Min(iaConfig.maxMensajesHistorial, 6);

        // +3: system prompt + pre-seed user + pre-seed assistant
        if (historialDialogo.Count <= maxMensajes + 3) return;

        // Mantener los 3 primeros (system + pre-seed) y los últimos N
        var cabecera = historialDialogo.GetRange(0, 3);
        int inicio = historialDialogo.Count - maxMensajes;
        var recientes = historialDialogo.GetRange(inicio, maxMensajes);

        historialDialogo.Clear();
        historialDialogo.AddRange(cabecera);
        historialDialogo.AddRange(recientes);

        Debug.Log($"[Historial] Podado a {historialDialogo.Count} mensajes (límite de seguridad activado)");
    }



    // Clases para deserializar JSON de OpenAI-compatible
    private class RespuestaLLM { public List<Choice> choices; }
    private class Choice { public Message message; }
    private class Message { public string content; }
}
