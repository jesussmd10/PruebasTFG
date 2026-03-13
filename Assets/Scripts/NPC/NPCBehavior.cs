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
    /// Analiza la respuesta de la IA y anima el personaje según su contenido
    /// </summary>
    private void ProcesarRespuesta(string textoCompleto)
    {
        if (string.IsNullOrEmpty(textoCompleto) || characterAnimator == null)
            return;

        string textoLow = textoCompleto.ToLower();

        // Detectar emociones por palabras clave
        if (textoLow.Contains("tiembla") || textoLow.Contains("miedo") || textoLow.Contains("nervioso"))
        {
            EventSystem.OnEmotionChanged.Invoke(EmotionState.Nervioso);
        }
        else if (textoLow.Contains("calma") || textoLow.Contains("respira"))
        {
            EventSystem.OnEmotionChanged.Invoke(EmotionState.Calmado);
        }

        // Siempre animar mientras habla
        EventSystem.OnEmotionChanged.Invoke(EmotionState.Hablando);
    }

    /// <summary>
    /// Extrae el diálogo limpio (sin acciones entre asteriscos)
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
