using UnityEngine;
using System.Text.RegularExpressions;

public class NPCBehavior : MonoBehaviour
{
    [SerializeField] private CharacterAnimator characterAnimator;

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

                // Si detecta nerviosismo
                if (accionLow.Contains("tiembla") || accionLow.Contains("miedo") || 
                    accionLow.Contains("nervioso") || accionLow.Contains("asusta") || 
                    accionLow.Contains("tartamudea") || accionLow.Contains("suda") ||
                    accionLow.Contains("tensa") || accionLow.Contains("agita"))
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
        
        // EXTRA: Si no hizo ninguna acción entre asteriscos, pero el texto incluye la palabra "no" o "nunca"
        if (!hayEmocion && !string.IsNullOrEmpty(textoDialogo))
        {
            // Usamos Regex \bno\b para asegurarnos de que es la palabra exacta "no" y no palabras como "noche".
            if (Regex.IsMatch(textoDialogo.ToLower(), @"\bno\b") || textoDialogo.ToLower().Contains("nunca"))
            {
                emocionDetectada = EmotionState.Negando;
                hayEmocion = true;
                Debug.Log("Emoción deducida del diálogo: Dijo 'no' o 'nunca', forzamos NEGACION");
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
        var matches = Regex.Matches(textoCompleto, @"\*(.+?)\*");

        foreach (Match match in matches)
        {
            string accion = match.Groups[1].Value.Trim();
            if (!string.IsNullOrEmpty(accion))
            {
                acciones.Add(accion);
            }
        }

        return acciones;
    }

    /// <summary>
    /// Extrae el diálogo limpio (sin acciones entre asteriscos ni paréntesis)
    /// Solo devuelve el texto que debe leerse en voz alta
    /// </summary>
    public static string LimpiarTexto(string textoCompleto)
    {
        // Elimina *acciones entre asteriscos*
        string textoLimpio = Regex.Replace(textoCompleto, @"\*.*?\*", "");
        // Elimina (paréntesis)
        textoLimpio = Regex.Replace(textoLimpio, @"\(.*?\)", "").Trim();
        return textoLimpio;
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
