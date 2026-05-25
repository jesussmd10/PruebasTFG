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


public class DialogueSystem : MonoBehaviour
{
    [SerializeField] private IAConfig iaConfig;
    private List<object> historialDialogo = new List<object>();
    private bool memoriaIniciada = false;

    /// <summary>
    /// Indica si el sistema de streaming está activo (para que otros scripts sepan).
    /// </summary>
    public bool UsaStreaming => iaConfig != null && iaConfig.usarStreaming;

    // Streaming: HttpClient para conexiones SSE
    private static readonly HttpClient httpClient = new HttpClient() { Timeout = TimeSpan.FromSeconds(30) };

    // Colas thread-safe para pasar datos del hilo de fondo al hilo principal de Unity
    // Esto evita el "reloj" de bloqueo en VR
    private readonly ConcurrentQueue<string> colaFrasesParaTTS = new ConcurrentQueue<string>();
    private volatile bool streamingEnCurso = false;

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
        if (!streamingEnCurso) return;

        // Despachar frases completas para TTS
        while (colaFrasesParaTTS.TryDequeue(out var frase))
        {
            EventSystem.OnFraseListaParaTTS.Invoke(frase);
        }
    }

    /// <summary>
    /// Inicializa la personalidad de la IA con el caso generado.
    /// Incluye un intercambio pre-seed para anclar al modelo en el caso correcto.
    /// </summary>
    public void InicializarPersonalidad(bool esCulpable, GameContext.CasoDelito caso)
    {
        string prompt = $@"
[ROL]
Personaje: Alex (28 años).
Situación: Interrogatorio policial en la comisaría. Estás siendo interrogado por un detective.
Actitud: {caso.Actitud}

[REGLAS DE DIÁLOGO]
- RESPUESTA DIRECTA: Escucha al detective y responde de forma coherente a su última pregunta.
- IDIOMA: Español NATIVO de España. (Ejemplo: di 'soy inocente', nunca 'estoy inocente').
- NO IA: Nunca hables como un asistente artificial. Eres un humano.
- EXPRESIÓN CORPORAL: Al principio de cada frase pon entre paréntesis cómo te mueves. Ejemplos obligatorios: (niega), (tiembla), (se calma).
- LA COARTADA: NUNCA repitas tu coartada constantemente a menos que te pregunten directamente.
";

        if (esCulpable)
        {
            prompt += $@"
[MEMORIA DEL PERSONAJE: CULPABLE]
- VERDAD OCULTA: Tú cometiste el crimen ({caso.DescripcionPrompt}).
- MENTIRA: Tu coartada FALSA es que estabas {caso.Coartada}.
- OBJETIVO: Tienes que mentir para salvarte.
- ESTRATEGIA: Al principio mientes muy bien y defiendes tu coartada. Pero si el detective te presiona, te grita o te arrincona lógicamente, te pones nervioso. Tu historia empezará a desmoronarse lentamente y puedes contradecirte o dar pistas sutiles, pero intenta que no sea demasiado obvio ni demasiado fácil para el detective.
";
            if (iaConfig != null && !string.IsNullOrEmpty(iaConfig.promptCulpable)) 
                prompt += "- NOTA EXTRA: " + iaConfig.promptCulpable + "\n";
        }
        else
        {
            prompt += $@"
[MEMORIA DEL PERSONAJE: INOCENTE]
- VERDAD: Eres totalmente INOCENTE de: {caso.DescripcionPrompt}.
- COARTADA REAL: Tu coartada VERDADERA es que estabas {caso.Coartada}.
- ESTRATEGIA: Dices siempre la verdad. NUNCA te desvíes de tu coartada ni te la inventes. Mantenla siempre firme.
- DEBILIDAD: Tienes miedo a ir a prisión. SÓLO si el detective te grita, te insulta o te pone contra las cuerdas de forma muy agresiva, los nervios te traicionarán y empezarás a dudar de ti mismo, a tartamudear o a confundir pequeños detalles por el pánico, pero sigues siendo inocente.
";
            if (iaConfig != null && !string.IsNullOrEmpty(iaConfig.promptInocente)) 
                prompt += "- NOTA EXTRA: " + iaConfig.promptInocente + "\n";
        }

        prompt += $@"
[SISTEMA DE JUEGO (TAG PISTA)]
Si te contradices con algo que has dicho antes, si el detective te pilla en una mentira brutal, o si revelas algo que te incrimina, DEBES escribir obligatoriamente la palabra {iaConfig.tagPista} AL FINAL de tu respuesta.
SÓLO úsalo para fallos graves en tu historia, NUNCA por simple nerviosismo.";

        historialDialogo.Clear();
        historialDialogo.Add(new { role = "system", content = prompt });

        // PRE-SEED: Anclar al modelo con un primer intercambio que establece el caso
        // Esto es CRUCIAL para modelos pequeños que tienden a alucinar e ignorar el system prompt
        string respuestaPreseed = GenerarRespuestaPreseed(caso);
        historialDialogo.Add(new { role = "user", content = $"Alex, sabes por qué estás aquí. Se te acusa de {caso.DescripcionPrompt}. ¿Qué tienes que decir?" });
        historialDialogo.Add(new { role = "assistant", content = respuestaPreseed });

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
            historialDialogo.Add(new 
            { 
                role = "system", 
                content = "(El detective te acaba de GRITAR con mucha agresividad. Asústate mucho, tartamudea, tiembla y es muy probable que se te escape información por los nervios)" 
            });
        }

        historialDialogo.Add(new { role = "user", content = textoUsuario });

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
            bool tienePista = resultado.IndexOf("[PISTA]", StringComparison.OrdinalIgnoreCase) >= 0;
            LatencyMetrics.Instance.FinalizarMedicion(resultado, tienePista);
        }

        // Añadir respuesta al historial
        if (resultado != null)
        {
            historialDialogo.Add(new { role = "assistant", content = resultado });
        }

        EventSystem.OnIAProcesando.Invoke(false);
        return resultado;
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

        streamingEnCurso = true;

        try
        {
            // Todo el I/O HTTP corre en un hilo del ThreadPool, NO en el hilo principal de Unity
            string resultado = await Task.Run(async () =>
            {
                string textoAcumulado = "";

                var content = new StringContent(jsonBody, Encoding.UTF8, "application/json");
                var request = new HttpRequestMessage(HttpMethod.Post, url);
                request.Content = content;

                var response = await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
                response.EnsureSuccessStatusCode();

                using (var stream = await response.Content.ReadAsStreamAsync())
                using (var reader = new StreamReader(stream))
                {
                    while (!reader.EndOfStream)
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
                                // Registrar token para métricas
                                if (LatencyMetrics.Instance != null)
                                    LatencyMetrics.Instance.RegistrarToken();

                                textoAcumulado += delta;
                            }
                        }
                        catch { /* Ignorar chunks mal formados */ }
                    }
                }

                return textoAcumulado;
            });

            streamingEnCurso = false;
            Debug.Log($"[Streaming] Respuesta completa ({resultado.Length} chars)");
            return resultado;
        }
        catch (Exception ex)
        {
            streamingEnCurso = false;
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
            while (!operacion.isDone) await Task.Yield();

            if (request.result == UnityWebRequest.Result.Success)
            {
                try
                {
                    var respuesta = JsonConvert.DeserializeObject<RespuestaLLM>(request.downloadHandler.text);
                    string textoBruto = respuesta.choices[0].message.content;
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
        int maxMensajes = iaConfig.maxMensajesHistorial;

        // +3: system prompt + pre-seed user + pre-seed assistant
        if (historialDialogo.Count <= maxMensajes + 3) return;

        // Mantener los 3 primeros (system + pre-seed) y los últimos N
        var cabecera = historialDialogo.GetRange(0, 3);
        int inicio = historialDialogo.Count - maxMensajes;
        var recientes = historialDialogo.GetRange(inicio, maxMensajes);

        historialDialogo.Clear();
        historialDialogo.AddRange(cabecera);
        historialDialogo.AddRange(recientes);

        Debug.Log($"[Historial] Podado a {historialDialogo.Count} mensajes");
    }

    private string GenerarRespuestaPreseed(GameContext.CasoDelito caso)
    {
        string actitud = caso.Actitud.ToLower();
        string coartada = caso.Coartada;

        // Si la actitud denota miedo, nerviosismo o timidez
        if (actitud.Contains("aterrado") || actitud.Contains("nervio") || actitud.Contains("tímido") || actitud.Contains("miedo") || actitud.Contains("asustado") || actitud.Contains("pánico"))
        {
            return $"(muy nervioso, tartamudeando) Mire, yo... yo no tengo nada que ver con eso. Estaba {coartada} cuando todo eso pasó, se lo juro por mi vida. No entiendo qué hago aquí.";
        }
        // Si denota enfado, agresividad, bordería, furia, indignación o actitud desafiante
        else if (actitud.Contains("furioso") || actitud.Contains("indignado") || actitud.Contains("borde") || actitud.Contains("defensiva") || actitud.Contains("grita") || actitud.Contains("enfado") || actitud.Contains("desprecio") || actitud.Contains("sarcás") || actitud.Contains("sarcas"))
        {
            return $"(golpea la mesa, con tono desafiante) ¡Escuche! Esto es una maldita broma. Yo no he hecho absolutamente nada. Estaba {coartada} en ese momento. ¡No tienen derecho a retenerme ni a acusarme!";
        }
        // Si denota arrogancia, frialdad, tranquilidad, ciencia o prepotencia
        else if (actitud.Contains("arrogante") || actitud.Contains("frío") || actitud.Contains("calma") || actitud.Contains("prepotencia") || actitud.Contains("científic") || actitud.Contains("calculador"))
        {
            return $"(sonríe arrogantemente, con absoluta calma) Por favor, detective. Esto es una absoluta pérdida de tiempo. En el momento de los hechos, yo estaba tranquilamente {coartada}. No tienen ninguna base para tenerme aquí.";
        }
        // Si denota confusión o desorientación
        else if (actitud.Contains("confuso") || actitud.Contains("desorientado"))
        {
            return $"(mira al suelo, confuso y desorientado) ¿Qué? No... no entiendo... yo no he hecho nada de eso... estaba {coartada}, de verdad. ¿Por qué me acusan a mí?";
        }
        // Fallback genérico neutral
        return $"(serio) Mire, no tengo ninguna relación con ese asunto. Estaba {coartada} en ese momento. Se están equivocando de persona.";
    }

    // Clases para deserializar JSON de OpenAI-compatible
    private class RespuestaLLM { public List<Choice> choices; }
    private class Choice { public Message message; }
    private class Message { public string content; }
}
