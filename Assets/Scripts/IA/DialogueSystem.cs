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
        string prompt = $"Eres Alex, un hombre de 28 años, principal sospechoso de {caso.DescripcionPrompt}, en la sala de interrogatorios de la comisaría. {caso.Actitud} ";
        prompt += "Responde SIEMPRE en español de España. Habla como una persona real: a veces con frases largas cuando te explicas o te pones nervioso, a veces con frases cortísimas cuando te quedas en shock o no sabes qué decir. Varía la longitud de forma natural según la situación. ";
        prompt += "ES OBLIGATORIO incluir (lenguaje corporal entre paréntesis) al principio o en medio de tus frases. ";
        prompt += "Usa EXACTAMENTE estas palabras clave dentro de los paréntesis: ";
        prompt += "Cuando contradices al detective o rechazas su teoría con fuerza: (niega, mueve la cabeza, rechaza). ";
        prompt += "Cuando tienes pánico o estrés: (tiembla, suda, muy nervioso, se asusta, tartamudea). ";
        prompt += "Cuando te calmas: (se calma, respira, suspira, se relaja). ";

        if (esCulpable)
        {
            prompt += $"\n\n¡ERES CULPABLE del crimen! Tu coartada FALSA es que estabas {caso.Coartada}. ";
            prompt += "Mantienes tu mentira con firmeza al principio, pero bajo presión te pones nervioso, tu historia tiene huecos y vacilaciones. Puedes inventar detalles al vuelo que a veces no cuadran. Eres listo e intentas parecer inocente, no eres estúpido. ";
            if (iaConfig != null && !string.IsNullOrEmpty(iaConfig.promptCulpable)) 
                prompt += iaConfig.promptCulpable + " ";
        }
        else
        {
            prompt += $"\n\n¡ERES TOTALMENTE INOCENTE! Tu coartada REAL es que estabas {caso.Coartada}. ";
            prompt += "Dices la verdad, pero tienes miedo de ir a prisión por error. Los nervios y el estrés te hacen expresarte mal a veces. Cuando el detective te grita o presiona, los nervios pueden hacerte confundir detalles o decir algo raro sin querer, aunque tu historia sea verdadera. ";
            if (iaConfig != null && !string.IsNullOrEmpty(iaConfig.promptInocente)) 
                prompt += iaConfig.promptInocente + " ";
        }

        prompt += $"\n\nDefiende tu coartada de que estabas: {caso.Coartada}. NO repitas tu historia como un robot. NUNCA digas 'como ya te he dicho' ni 'vuelvo a repetir'. Habla natural, con detalles nuevos cada vez. Si te acorralan, enfádate, usa sarcasmo o incluso palabrotas.";

        // PISTA: MUY restringido
        prompt += $"\n\nSISTEMA DE JUEGO - TAG {iaConfig.tagPista}: Este tag es MUY RARO y ESPECIAL. Añádelo al FINAL de tu respuesta SOLO cuando ocurra algo REALMENTE significativo para la investigación. Estas son las ÚNICAS situaciones válidas:";
        prompt += "\n1) Te contradices claramente con algo que dijiste ANTES en la conversación (ej: antes dijiste que estabas solo y ahora mencionas que estabas con alguien).";
        prompt += "\n2) Revelas sin querer un detalle del crimen que el detective NO te había contado (ej: mencionas el arma usada sin que te lo hayan dicho).";
        prompt += "\n3) Tu coartada se derrumba porque el detective te pilla en una mentira clara con pruebas.";
        prompt += $"\nIMPORTANTE: NO generes {iaConfig.tagPista} por estar nervioso, por tartamudear, ni por simple estrés. Solo por CONTRADICCIONES FACTUALES o REVELACIONES CLAVE. Máximo 1 pista cada 4-5 intercambios como mínimo. La mayoría de tus respuestas NO deben tener este tag.";

        // CONTEXTO DEL CASO: Recordatorio explícito para modelos pequeños
        prompt += $"\n\nRECUERDA SIEMPRE: El crimen del que se te acusa es ESPECÍFICAMENTE: {caso.DescripcionPrompt}. NO inventes otro crimen diferente. NO cambies los detalles del caso. Tu coartada es SIEMPRE que estabas {caso.Coartada}. NO inventes otra coartada diferente.";

        historialDialogo.Clear();
        historialDialogo.Add(new { role = "system", content = prompt });

        // PRE-SEED: Anclar al modelo con un primer intercambio que establece el caso
        // Esto es CRUCIAL para modelos pequeños que tienden a alucinar e ignorar el system prompt
        historialDialogo.Add(new { role = "user", content = $"Alex, sabes por qué estás aquí. Se te acusa de {caso.DescripcionPrompt}. ¿Qué tienes que decir?" });
        historialDialogo.Add(new { role = "assistant", content = $"(muy nervioso) Mire, yo... yo no tengo nada que ver con eso. Estaba {caso.Coartada} cuando todo eso pasó, se lo juro. No sé por qué me han traído aquí." });

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
            LatencyMetrics.Instance.IniciarMedicion(iaConfig.nombreModelo, "dialogo");

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
        string url = iaConfig.urlModelo;
        float temp = iaConfig.temperatura;
        string modelo = iaConfig.nombreModelo;
        int maxTokens = iaConfig.maxTokensRespuesta;

        var datos = new
        {
            model = modelo,
            messages = messages,
            temperature = temp,
            max_tokens = maxTokens,
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
                string fraseActual = "";

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
                                fraseActual += delta;

                                // Comprobar si hay frase completa
                                if (EsFraseCompleta(fraseActual))
                                {
                                    if (!string.IsNullOrWhiteSpace(fraseActual))
                                    {
                                        colaFrasesParaTTS.Enqueue(fraseActual);
                                    }
                                    fraseActual = "";
                                }
                            }
                        }
                        catch { /* Ignorar chunks mal formados */ }
                    }
                }

                // Procesar última frase si quedó sin emitir
                if (!string.IsNullOrWhiteSpace(fraseActual))
                {
                    colaFrasesParaTTS.Enqueue(fraseActual);
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
            model = iaConfig.nombreModelo,
            messages = historialDialogo,
            temperature = iaConfig.temperatura,
            max_tokens = iaConfig.maxTokensRespuesta
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

    // Clases para deserializar JSON de OpenAI-compatible
    private class RespuestaLLM { public List<Choice> choices; }
    private class Choice { public Message message; }
    private class Message { public string content; }
}
