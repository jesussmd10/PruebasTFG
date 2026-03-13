using UnityEngine;
using System.Threading.Tasks;


public class InterrogationManager : MonoBehaviour
{
    [SerializeField] private DialogueSystem dialogueSystem;
    [SerializeField] private AudioManager audioManager;
    [SerializeField] private TMPro.TextMeshProUGUI textoRelojVR;
    [SerializeField] private TMPro.TextMeshProUGUI textoFolioVR;

    private float tiempoRestante;
    private bool juegoActivo = true;

    private void Start()
    {
        // Inicializar el contexto del juego
        bool esCulpable = Random.value > 0.5f;
        GameContext.Instance.ConfigurarCulpabilidad(esCulpable);
        Debug.Log(esCulpable 
            ? "<color=red>🔴 EL SOSPECHOSO ES CULPABLE</color>" 
            : "<color=green>🟢 EL SOSPECHOSO ES INOCENTE</color>");

        // Inicializar IA
        dialogueSystem.InicializarPersonalidad(esCulpable);
        tiempoRestante = GameContext.Instance.TiempoPartida;

        // Suscribirse a eventos
        EventSystem.OnInterrogacionRecibida.AddListener(ProcesarInterrogacion);
        EventSystem.OnInterrogatorioTerminado.AddListener(TerminarJuego);
        EventSystem.OnPistaDescubierta.AddListener(AñadirPistaAlFolio);

        ActualizarFolio("Inicio del interrogatorio.\nMotivo: Robo en la joyería.\n\n");
    }

    private void OnDisable()
    {
        EventSystem.OnInterrogacionRecibida.RemoveListener(ProcesarInterrogacion);
        EventSystem.OnInterrogatorioTerminado.RemoveListener(TerminarJuego);
        EventSystem.OnPistaDescubierta.RemoveListener(AñadirPistaAlFolio);
    }

    private void Update()
    {
        if (!juegoActivo) return;

        tiempoRestante -= Time.deltaTime;
        GameContext.Instance.ReducirTiempo(Time.deltaTime);

        // Actualizar reloj
        int minutos = Mathf.FloorToInt(tiempoRestante / 60);
        int segundos = Mathf.FloorToInt(tiempoRestante % 60);
        textoRelojVR.text = string.Format("{0:00}:{1:00}", minutos, segundos);
    }

    private async void ProcesarInterrogacion(string textoUsuario, bool usuarioGrita)
    {
        Debug.Log($"🎤 Usuario: '{textoUsuario}' | Gritando: {usuarioGrita}");

        // Obtener respuesta de IA
        string respuestaIA = await dialogueSystem.ObtenerRespuesta(textoUsuario, usuarioGrita);

        if (string.IsNullOrEmpty(respuestaIA))
        {
            Debug.LogError("Error: respuesta IA vacía");
            return;
        }

        // Detectar y procesar pista
        if (NPCBehavior.TienePista(respuestaIA, "[PISTA]"))
        {
            respuestaIA = NPCBehavior.ExtraerPista(respuestaIA, "[PISTA]");
            GameContext.Instance.AñadirPista("El sospechoso se ha contradicho o reveló un dato clave.");
        }

        // Emitir evento para que otros sistemas procesen
        EventSystem.OnRespuestaIA.Invoke(respuestaIA);

        // Reproducir audio limpio
        string textoLimpio = NPCBehavior.LimpiarTexto(respuestaIA);
        if (!string.IsNullOrEmpty(textoLimpio))
        {
            audioManager.ReproducirTexto(textoLimpio);
        }
    }

    private void AñadirPistaAlFolio(string pista)
    {
        ActualizarFolio($"<b>PISTA OBTENIDA:</b>\n- {pista}\n\n");
    }

    private void TerminarJuego()
    {
        juegoActivo = false;

        string veredicto = GameContext.Instance.EsCulpable 
            ? "<color=red>¡ERA CULPABLE!</color>" 
            : "<color=green>¡ERA INOCENTE!</color>";

        ActualizarFolio($"\n\n<b>TIEMPO AGOTADO</b>\nLa verdad era: {veredicto}");
        Debug.Log("🏁 Fin del juego: " + veredicto);
    }

    private void ActualizarFolio(string textoExtra)
    {
        if (textoFolioVR != null)
        {
            textoFolioVR.text = "<b>CASO: 042 - ROBO</b>\nSospechoso: Alex\n\n" + textoExtra;
        }
    }
}
