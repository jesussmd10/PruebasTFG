using UnityEngine.Events;


public static class EventSystem
{
    // Eventos del sistema
    public static UnityEvent<string, bool> OnInterrogacionRecibida = new UnityEvent<string, bool>();
    public static UnityEvent<string> OnRespuestaIA = new UnityEvent<string>();
    public static UnityEvent<string> OnPistaDescubierta = new UnityEvent<string>();
    public static UnityEvent OnInterrogatorioTerminado = new UnityEvent();
    
    // Para animaciones emocionales
    public static UnityEvent<EmotionState> OnEmotionChanged = new UnityEvent<EmotionState>();

    // Streaming: frase completa lista para TTS
    public static UnityEvent<string> OnFraseListaParaTTS = new UnityEvent<string>();

    // Streaming: respuesta completa terminada
    public static UnityEvent<string> OnRespuestaIACompleta = new UnityEvent<string>();

    // Estado de procesamiento (para UI feedback)
    public static UnityEvent<bool> OnIAProcesando = new UnityEvent<bool>();
}

public enum EmotionState
{
    Nervioso,
    Negando,
    Calmado,
    Hablando
}
