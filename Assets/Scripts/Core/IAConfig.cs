using UnityEngine;


[CreateAssetMenu(fileName = "IAConfig", menuName = "Configs/IA Config")]
public class IAConfig : ScriptableObject
{
    [Header("Modelo")]
    public string urlModelo = "http://localhost:1234/v1/chat/completions";
    public string nombreModelo = "meta-llama-3.1-8b-instruct-abliterated";
    public float temperatura = 0.7f;

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
    public float tiempoTimeout = 10f;
}
