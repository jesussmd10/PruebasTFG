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

        // Extraer las acciones entre *asteriscos* (ej: *tiembla*, *se calma*)
        var acciones = ExtraerAcciones(textoCompleto);

        if (acciones.Count > 0)
        {
            Debug.Log($"🎭 Acciones detectadas ({acciones.Count}):");
            foreach (var accion in acciones)
            {
                Debug.Log($"   → \"{accion}\"");
            }

            // Detectar emociones SOLO a partir de las acciones extraídas
            foreach (var accion in acciones)
            {
                string accionLow = accion.ToLower();

                if (accionLow.Contains("tiembla") || accionLow.Contains("miedo") || 
                    accionLow.Contains("nervioso") || accionLow.Contains("asusta") || 
                    accionLow.Contains("tartamudea") || accionLow.Contains("suda") ||
                    accionLow.Contains("tensa") || accionLow.Contains("agita"))
                {
                    EventSystem.OnEmotionChanged.Invoke(EmotionState.Nervioso);
                }
                else if (accionLow.Contains("calma") || accionLow.Contains("respira") || 
                         accionLow.Contains("tranquil") || accionLow.Contains("suspira") ||
                         accionLow.Contains("relaja"))
                {
                    EventSystem.OnEmotionChanged.Invoke(EmotionState.Calmado);
                }
            }
        }

        // Siempre animar mientras habla (si hay texto de diálogo)
        string textoDialogo = LimpiarTexto(textoCompleto);
        if (!string.IsNullOrEmpty(textoDialogo))
        {
            EventSystem.OnEmotionChanged.Invoke(EmotionState.Hablando);
        }
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
