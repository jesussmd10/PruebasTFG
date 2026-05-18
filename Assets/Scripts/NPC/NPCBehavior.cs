using UnityEngine;
using System.Text.RegularExpressions;

public class NPCBehavior : MonoBehaviour
{
    [SerializeField] private CharacterAnimator characterAnimator;
    [SerializeField] private NPCMovement npcMovement;
    private void OnEnable()
    {
        EventSystem.OnRespuestaIA.AddListener(ProcesarRespuesta);
    }

    private void OnDisable()
    {
        EventSystem.OnRespuestaIA.RemoveListener(ProcesarRespuesta);
    }

    /// <summary>
    /// Analiza la respuesta de la IA: extrae acciones entre *asteriscos* para emociones
    /// y deja el resto como diálogo limpio para TTS
    /// </summary>
    private void ProcesarRespuesta(string textoCompleto)
    {
        if (string.IsNullOrEmpty(textoCompleto) || characterAnimator == null)
            return;

        // Si estamos en modo streaming, ignoramos esta respuesta bulk final
        // porque AudioManager gestiona la sincronización en tiempo real frase a frase.
        var dialogueSystem = FindFirstObjectByType<DialogueSystem>();
        if (dialogueSystem != null && dialogueSystem.UsaStreaming)
        {
            return;
        }

        if (npcMovement != null && !npcMovement.YaEstaSentado)
        {
            Debug.LogWarning("El LLM ha respondido muy rápido. El NPC sigue caminando, ignoramos el cambio de emoción para que no vuele.");
            return; 
        }
        var acciones = ExtraerAcciones(textoCompleto);
        
        bool hayEmocion = false;
        EmotionState emocionDetectada = EmotionState.Hablando; // Valor por defecto

        if (acciones.Count > 0)
        {
            Debug.Log($"Acciones detectadas ({acciones.Count}):");
            foreach (var accion in acciones)
            {
                Debug.Log($"   → \"{accion}\"");
                string accionLow = accion.ToLower();

                // Si detecta nerviosismo (incluye enfado y pánico)
                if (accionLow.Contains("tiembla") || accionLow.Contains("miedo") || 
                    accionLow.Contains("nervioso") || accionLow.Contains("asusta") || 
                    accionLow.Contains("tartamudea") || accionLow.Contains("suda") ||
                    accionLow.Contains("tensa") || accionLow.Contains("agita") ||
                    accionLow.Contains("furioso") || accionLow.Contains("agresivo") ||
                    accionLow.Contains("altera") || accionLow.Contains("pánico") ||
                    accionLow.Contains("duda") || accionLow.Contains("enfada") ||
                    accionLow.Contains("llora") || accionLow.Contains("desespera"))
                {
                    emocionDetectada = EmotionState.Nervioso;
                    hayEmocion = true;
                }
                // Si detecta negación
                else if (accionLow.Contains("niega") || 
                         accionLow.Contains("cabeza") || accionLow.Contains("rechaza"))
                {
                    emocionDetectada = EmotionState.Negando;
                    hayEmocion = true;
                }
                // Si detecta calma (quieta/idle)
                else if (accionLow.Contains("calma") || accionLow.Contains("respira") || 
                         accionLow.Contains("tranquil") || accionLow.Contains("suspira") ||
                         accionLow.Contains("relaja"))
                {
                    emocionDetectada = EmotionState.Calmado;
                    hayEmocion = true;
                }
            }
        }

        string textoDialogo = LimpiarTexto(textoCompleto);
        
        if (!hayEmocion && !string.IsNullOrEmpty(textoDialogo))
        {
            string tx = textoDialogo.ToLower();
            // 1. Buscamos primero negaciones o contradicciones fuertes en el diálogo hablado
            if (tx.Contains("no, no") || tx.Contains("eso no es verdad") || 
                tx.Contains("es mentira") || tx.Contains("falso") || 
                tx.Contains("montaje") || tx.Contains("injusto") || 
                tx.Contains("jamás") || tx.Contains("me niego"))
            {
                emocionDetectada = EmotionState.Negando;
                hayEmocion = true;
                Debug.Log("Emoción deducida del diálogo: Ocurre una negación fuerte, forzamos NEGACION");
            }
        }

        // Aquí decidimos el orden lógico:
        if (!string.IsNullOrEmpty(textoDialogo))
        {
            if (hayEmocion)
            {
                // Disparamos la corrutina para darle tiempo a hacer la emoción antes de hablar
                StartCoroutine(AplicarEmocionYHablar(emocionDetectada));
            }
            else
            {
                // Si no hay acción extra, habla directamente
                EventSystem.OnEmotionChanged.Invoke(EmotionState.Hablando);
            }
        }
        else
        {
            // Salta de postura si solo es una interjección sin diálogo
            if (hayEmocion)
            {
                EventSystem.OnEmotionChanged.Invoke(emocionDetectada);
            }
        }
    }

    private System.Collections.IEnumerator AplicarEmocionYHablar(EmotionState emocionPrevia)
    {
        // Dispara la animación de su estado emocional (ej. Negar)
        EventSystem.OnEmotionChanged.Invoke(emocionPrevia);

        // Esperamos un poco para darle tiempo al modelo de hacer el gesto animado
        // 1.5 a 2 segundos suele ser ideal para que se vea claro el gesto de negar
        yield return new WaitForSeconds(1.5f);

        // Mandamos la señal de hablar
        EventSystem.OnEmotionChanged.Invoke(EmotionState.Hablando);
    }

    /// <summary>
    /// Extrae todas las acciones entre *asteriscos* del texto
    /// Ejemplo: "*tiembla nerviosamente* Yo no fui *mira al suelo*"
    ///   → ["tiembla nerviosamente", "mira al suelo"]
    /// </summary>
    public static System.Collections.Generic.List<string> ExtraerAcciones(string textoCompleto)
    {
        var acciones = new System.Collections.Generic.List<string>();
        // Extraemos cualquier acción entre paréntesis o corchetes o asteriscos
        var matches = Regex.Matches(textoCompleto, @"\((.*?)\)|\[(.*?)\]|\*(.*?)\*", RegexOptions.Singleline);

        foreach (Match match in matches)
        {
            string accion = "";
            if (match.Groups[1].Success) accion = match.Groups[1].Value;
            else if (match.Groups[2].Success) accion = match.Groups[2].Value;
            else if (match.Groups[3].Success) accion = match.Groups[3].Value;

            accion = accion.Trim();
            if (!string.IsNullOrEmpty(accion))
            {
                acciones.Add(accion);
            }
        }

        return acciones;
    }

    /// <summary>
    /// Extrae el diálogo limpio (sin acciones entre asteriscos, paréntesis, corchetes, ni tags del sistema).
    /// Solo devuelve el texto que debe leerse en voz alta. Es agresivo para que NADA se escape al TTS.
    /// </summary>
    public static string LimpiarTexto(string textoCompleto)
    {
        if (string.IsNullOrEmpty(textoCompleto)) return "";

        string textoLimpio = textoCompleto;

        // Reparar paréntesis/corchetes/asteriscos huérfanos al inicio (si hay un cierre antes de una apertura)
        // Esto ocurre cuando el streaming corta el primer token (ej. "ueve la cabeza..." en vez de "(mueve la cabeza...")
        int idxCierreP = textoLimpio.IndexOf(')');
        int idxAperturaP = textoLimpio.IndexOf('(');
        if (idxCierreP >= 0 && (idxAperturaP < 0 || idxAperturaP > idxCierreP))
        {
            textoLimpio = "(" + textoLimpio;
        }

        int idxCierreC = textoLimpio.IndexOf(']');
        int idxAperturaC = textoLimpio.IndexOf('[');
        if (idxCierreC >= 0 && (idxAperturaC < 0 || idxAperturaC > idxCierreC))
        {
            textoLimpio = "[" + textoLimpio;
        }

        // 1. Eliminar [PISTA] y variantes ANTES de todo (case-insensitive, con o sin corchetes)
        textoLimpio = Regex.Replace(textoLimpio, @"\[?\s*PISTA\s*\]?", "", RegexOptions.IgnoreCase);

        // 2. Eliminar contenido entre delimitadores completos (parejas cerradas)
        textoLimpio = Regex.Replace(textoLimpio, @"\*[^*]+\*", "", RegexOptions.Singleline);
        textoLimpio = Regex.Replace(textoLimpio, @"\([^)]+\)", "", RegexOptions.Singleline);
        textoLimpio = Regex.Replace(textoLimpio, @"\[[^\]]+\]", "", RegexOptions.Singleline);

        // 3. Eliminar contenido huérfano (paréntesis/corchetes/asteriscos que se abrieron pero no se cerraron)
        //    Esto pasa en streaming cuando el token llega partido: "(tiembla nerviosamente" sin el ")"
        textoLimpio = Regex.Replace(textoLimpio, @"\([^)]*$", "", RegexOptions.Singleline);  // ( sin )
        textoLimpio = Regex.Replace(textoLimpio, @"\[[^\]]*$", "", RegexOptions.Singleline);  // [ sin ]
        textoLimpio = Regex.Replace(textoLimpio, @"\*[^*]*$", "", RegexOptions.Singleline);   // * sin cierre
        //    También el caso inverso: cierre sin apertura (ej: "nerviosamente)" al inicio)
        textoLimpio = Regex.Replace(textoLimpio, @"^[^(]*\)", "", RegexOptions.Singleline);
        textoLimpio = Regex.Replace(textoLimpio, @"^[^\[]*\]", "", RegexOptions.Singleline);

        // 4. Eliminar cualquier carácter delimitador suelto que haya sobrevivido
        textoLimpio = textoLimpio.Replace("*", "");
        textoLimpio = textoLimpio.Replace("(", "");
        textoLimpio = textoLimpio.Replace(")", "");
        textoLimpio = textoLimpio.Replace("[", "");
        textoLimpio = textoLimpio.Replace("]", "");

        // 5. Limpiar espacios múltiples y saltos de línea extra
        textoLimpio = Regex.Replace(textoLimpio, @"\s{2,}", " ");

        return textoLimpio.Trim();
    }

    /// <summary>
    /// Detecta si hay pista en la respuesta
    /// </summary>
    public static bool TienePista(string texto, string tagPista)
    {
        return texto.Contains(tagPista);
    }

    /// <summary>
    /// Extrae la pista del texto
    /// </summary>
    public static string ExtraerPista(string texto, string tagPista)
    {
        return texto.Replace(tagPista, "").Trim();
    }
}
