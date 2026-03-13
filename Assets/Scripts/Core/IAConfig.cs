using UnityEngine;


[CreateAssetMenu(fileName = "IAConfig", menuName = "Configs/IA Config")]
public class IAConfig : ScriptableObject
{
    [Header("Modelo")]
    public string urlModelo = "http://localhost:1234/v1/chat/completions";
    public string nombreModelo = "llama-3.2-3b-instruct";
    public float temperatura = 0.7f;

    [Header("Personalidad")]
    public string promptCulpable = "Eres Alex, un sospechoso CULPABLE. Robaste la joyería. Debes mentir e inventar excusas.";
    public string promptInocente = "Eres Alex, un sospechoso INOCENTE. Estabas en el cine. Defiende tu inocencia.";
    
    [Header("Comportamiento")]
    public string tagPista = "[PISTA]";
    public int maxReintentos = 3;
    public float tiempoTimeout = 10f;
}
