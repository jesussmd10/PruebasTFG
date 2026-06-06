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

        // El AudioManager ahora gestiona de forma centralizada la sincronización de emociones y la animación 
        // de hablar con el clip de voz real para asegurar que coincidan exactamente con el sonido.
    }

    /// <summary>
    /// Extrae el diálogo limpio (sin etiquetas XML residuales).
    /// </summary>
    public static string LimpiarTexto(string textoCompleto)
    {
        if (string.IsNullOrEmpty(textoCompleto)) return "";

        string textoLimpio = textoCompleto;

        // 1. Eliminar los metadatos [PISTA: ...] y [ANIMACION: ...] (incluso si la IA olvidó cerrarlos al final del texto)
        textoLimpio = Regex.Replace(textoLimpio, @"\[PISTA:.*?(?:\]|$)", "", RegexOptions.Singleline | RegexOptions.IgnoreCase);
        textoLimpio = Regex.Replace(textoLimpio, @"\[ANIMACION:.*?(?:\]|$)", "", RegexOptions.Singleline | RegexOptions.IgnoreCase);

        // 1.1 Eliminar cualquier otra etiqueta XML residual
        textoLimpio = Regex.Replace(textoLimpio, @"<.*?>", "", RegexOptions.Singleline);

        // 1.5. Eliminar asteriscos (animaciones)
        textoLimpio = Regex.Replace(textoLimpio, @"\*.*?\*", "", RegexOptions.Singleline);

        // 2. Escapar comillas dobles cambiándolas por simples para que no rompan los argumentos del TTS en la consola
        textoLimpio = textoLimpio.Replace("\"", "'");

        // 3. Limpiar espacios múltiples y saltos de línea extra
        textoLimpio = Regex.Replace(textoLimpio, @"\s{2,}", " ").Trim();

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
