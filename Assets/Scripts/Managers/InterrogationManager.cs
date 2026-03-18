using UnityEngine;
using System.Threading.Tasks;


public class InterrogationManager : MonoBehaviour
{
    [SerializeField] private DialogueSystem dialogueSystem;
    [SerializeField] private AudioManager audioManager;
    [SerializeField] private TMPro.TextMeshProUGUI textoRelojVR;
    [SerializeField] private TMPro.TextMeshProUGUI textoFolioVR;

    private string contenidoFolio = "";
    private bool juegoActivo = true;

    private void Start()
    {
        // Forzar settings UI por código para sobreescribir errores del Inspector
        if (textoRelojVR != null)
        {
            textoRelojVR.isRightToLeftText = false; // Fix: 04:46 renderizado como 64:40
            textoRelojVR.alignment = TMPro.TextAlignmentOptions.Center;
        }
        
        if (textoFolioVR != null)
        {
            textoFolioVR.isRightToLeftText = false;
            textoFolioVR.alignment = TMPro.TextAlignmentOptions.TopLeft;
            
            // Forzar que el texto SIEMPRE se dibuje, incluso si el recuadro es super pequeño
            textoFolioVR.overflowMode = TMPro.TextOverflowModes.Overflow;
            textoFolioVR.enableWordWrapping = true;
            
            // Quitar márgenes raros que pudieran estar empujando el texto fuera
            textoFolioVR.margin = UnityEngine.Vector4.zero;
            
            // Forzar color negro opaco (tinta sobre papel)
            textoFolioVR.color = UnityEngine.Color.black;
            
            textoFolioVR.enableAutoSizing = true;
            textoFolioVR.fontSizeMin = 10;
            textoFolioVR.fontSizeMax = 50;

            // FIX GIGANTE REAL: El Canvas estaba de pie (escalado mal) en vez de tumbado sobre el papel.
            Canvas canvas = textoFolioVR.GetComponentInParent<Canvas>();
            if (canvas != null && canvas.transform.parent != null)
            {
                // 1. Tumbar el canvas 90 grados para que el texto esté "impreso" en el papel, no flotando estilo holograma
                canvas.transform.localEulerAngles = new UnityEngine.Vector3(90, 0, 0);

                // 2. Escalar correctamente. Como el canvas ahora está tumbado, su eje Y recae sobre el eje Z del padre.
                UnityEngine.Vector3 escalaPadre = canvas.transform.parent.lossyScale;
                canvas.transform.localScale = new UnityEngine.Vector3(
                    0.001f / escalaPadre.x,
                    0.001f / escalaPadre.z, 
                    1f 
                );

                // 3. Pegarlo a la superficie (Y=0.51 en el cubo para estar justo encima) y centrarlo
                canvas.transform.localPosition = new UnityEngine.Vector3(0, 0.51f, 0); 
                
                // Centrar el texto en el canvas por si quedó descentrado
                textoFolioVR.rectTransform.anchoredPosition = UnityEngine.Vector2.zero;
            }
        }

        // Inicializar el contexto del juego
        bool esCulpable = Random.value > 0.5f;
        GameContext.Instance.ConfigurarCulpabilidad(esCulpable);
        Debug.Log(esCulpable 
            ? "<color=red>🔴 EL SOSPECHOSO ES CULPABLE</color>" 
            : "<color=green>🟢 EL SOSPECHOSO ES INOCENTE</color>");

        // Inicializar IA
        dialogueSystem.InicializarPersonalidad(esCulpable);


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

        GameContext.Instance.ReducirTiempo(Time.deltaTime);

        // Actualizar reloj
        int minutos = Mathf.FloorToInt(GameContext.Instance.TiempoRestante / 60);
        int segundos = Mathf.FloorToInt(GameContext.Instance.TiempoRestante % 60);
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

        // Log de la respuesta completa de la IA para depuración
        Debug.Log($"🤖 Respuesta IA completa: '{respuestaIA}'");

        // Detectar y procesar pista (case-insensitive)
        if (respuestaIA.IndexOf("[PISTA]", System.StringComparison.OrdinalIgnoreCase) >= 0)
        {
            Debug.Log("🔍 ¡PISTA DETECTADA en la respuesta!");
            respuestaIA = respuestaIA.Replace("[PISTA]", "").Replace("[pista]", "").Replace("[Pista]", "").Trim();
            GameContext.Instance.AñadirPista("El sospechoso se ha contradicho o reveló un dato clave.");
        }
        else
        {
            Debug.Log("🔍 No se detectó [PISTA] en esta respuesta.");
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
            contenidoFolio += textoExtra;
            textoFolioVR.text = "<b>CASO: 042 - ROBO</b>\nSospechoso: Alex\n\n" + contenidoFolio;
            Debug.Log($"📋 Folio actualizado. Texto añadido: '{textoExtra}'");
        }
        else
        {
            Debug.LogError("❌ textoFolioVR NO está asignado en el Inspector. El folio no se puede actualizar.");
        }
    }
}
