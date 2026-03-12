using UnityEngine;
using TMPro;

public class GameManager : MonoBehaviour
{
    [Header("Configuración del Caso")]
    public float tiempoPartida = 300f; // 300 segundos = 5 minutos
    public bool esCulpable;
    private bool juegoTerminado = false;

    [Header("Referencias")]
    public RespuestaIA cerebroIA;
    public TextMeshProUGUI textoRelojVR;
    public TextMeshProUGUI textoFolioVR;

    private string pistasDescubiertas = "";

    void Start()
    {
        // 1. Tiramos la moneda: ¿Culpable o Inocente? (50% de probabilidad)
        esCulpable = Random.value > 0.5f;

        Debug.Log(esCulpable ? "<color=red>EL SOSPECHOSO ES CULPABLE</color>" : "<color=green>EL SOSPECHOSO ES INOCENTE</color>");

        // 2. Le inyectamos esta personalidad a Llama
        cerebroIA.ConfigurarPersonalidadInicial(esCulpable);

        // 3. Escribimos los datos iniciales en el folio
        ActualizarFolio("Inicio del interrogatorio.\nMotivo: Robo en la joyería.\n\n");
    }

    void Update()
    {
        if (juegoTerminado) return;

        // Cuenta atrás
        tiempoPartida -= Time.deltaTime;

        // Actualizar reloj en VR (formato Minutos:Segundos)
        int minutos = Mathf.FloorToInt(tiempoPartida / 60);
        int segundos = Mathf.FloorToInt(tiempoPartida % 60);
        textoRelojVR.text = string.Format("{0:00}:{1:00}", minutos, segundos);

        if (tiempoPartida <= 0)
        {
            TerminarInterrogatorio();
        }
    }

    void TerminarInterrogatorio()
    {
        juegoTerminado = true;
        textoRelojVR.text = "00:00";

        string veredicto = esCulpable ? "<color=red>¡ERA CULPABLE!</color>" : "<color=green>¡ERA INOCENTE!</color>";
        ActualizarFolio($"\n\n<b>TIEMPO AGOTADO</b>\nLa verdad era: {veredicto}");

        Debug.Log("Fin del juego: " + veredicto);
    }

    // Función que llamaremos cuando la IA revele una pista
    public void AñadirPista(string nuevaPista)
    {
        pistasDescubiertas += "- " + nuevaPista + "\n";
        ActualizarFolio("<b>PISTAS OBTENIDAS:</b>\n" + pistasDescubiertas);
    }

    void ActualizarFolio(string textoExtra)
    {
        if (textoFolioVR != null)
        {
            textoFolioVR.text = "<b>CASO: 042 - ROBO</b>\nSospechoso: Alex\n\n" + textoExtra;
        }
    }
}