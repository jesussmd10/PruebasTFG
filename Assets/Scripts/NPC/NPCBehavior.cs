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
        textoLimpio = Regex.Replace(textoLimpio, @"\s{2,}", " ").Trim();

        // 6. Escapar comillas dobles cambiándolas por simples para que no rompan los argumentos del TTS en la consola
        textoLimpio = textoLimpio.Replace("\"", "'");

        // 6. FALLBACK A PRUEBA DE BALAS: Si el LLM metió TODO el texto entre asteriscos/paréntesis 
        // y el borrado agresivo nos dejó un texto vacío o solo con puntuación (lo que crashea el TTS)
        if (string.IsNullOrWhiteSpace(textoLimpio) || Regex.IsMatch(textoLimpio, @"^[\p{P}\s]*$"))
        {
            // Revertimos al texto original pero solo le quitamos los caracteres problemáticos
            string textoRescate = textoCompleto.Replace("*", "").Replace("(", "").Replace(")", "").Replace("[", "").Replace("]", "");
            textoRescate = Regex.Replace(textoRescate, @"\[?\s*PISTA\s*\]?", "", RegexOptions.IgnoreCase);
            textoLimpio = Regex.Replace(textoRescate, @"\s{2,}", " ").Trim();
            UnityEngine.Debug.LogWarning("[NPCBehavior] El texto limpio quedó vacío (el LLM usó mal los asteriscos). Usando texto de rescate: " + textoLimpio);
        }

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
