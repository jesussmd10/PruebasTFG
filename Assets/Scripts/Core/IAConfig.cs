using UnityEngine;


[CreateAssetMenu(fileName = "IAConfig", menuName = "Configs/IA Config")]
public class IAConfig : ScriptableObject
{
    [Header("Modelo Generación Casos (IA Pesada)")]
    public string urlModeloCasos = "http://localhost:1234/v1/chat/completions";
    public string nombreModeloCasos = "meta-llama-3.1-8b-instruct-abliterated"; // Por defecto, se cambiará en UI
    public float temperaturaCasos = 0.8f;

    [Header("Modelo Diálogo (IA Ligera)")]
    public string urlModeloDialogo = "http://localhost:1234/v1/chat/completions";
    public string nombreModeloDialogo = "meta-llama-3.1-8b-instruct-abliterated";
    public float temperaturaDialogo = 0.7f;

    [Header("Streaming")]
    [Tooltip("Activa streaming SSE para reducir latencia")]
    public bool usarStreaming = false;

    [Header("Límites de generación")]
    [Tooltip("Máximo de tokens por respuesta de diálogo. 300 permite respuestas dinámicas.")]
    public int maxTokensRespuesta = 300;

    [Tooltip("Máximo de tokens para generación de caso")]
    public int maxTokensCaso = 512;

    [Tooltip("Máximo de mensajes en el historial (sin contar system). 10 = 5 turnos.")]
    public int maxMensajesHistorial = 10;

    [Header("Text to Speech (Voz)")]
    public TTSProvider ttsProvider = TTSProvider.EdgeTTS;
    public string ttsApiKey = "";
    public string ttsVoiceId = "alloy"; // 'alloy', 'onyx' en OpenAI, o ID en ElevenLabs

    public enum TTSProvider { EdgeTTS, OpenAI, ElevenLabs }


    [Header("Personalidad")]
    public string promptCulpable = "Eres Alex, un sospechoso CULPABLE. Debes mentir e inventar excusas.";
    public string promptInocente = "Eres Alex, un sospechoso INOCENTE. Defiende tu inocencia.";
    
    [Header("Comportamiento")]
    public string tagPista = "[PISTA]";
    public int maxReintentos = 3;
    public float tiempoTimeout = 30f;
}
