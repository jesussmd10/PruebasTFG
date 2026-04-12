using UnityEngine;
using UnityEngine.SceneManagement;
using System.Threading.Tasks;


public class InterrogationManager : MonoBehaviour
{
    [SerializeField] private DialogueSystem dialogueSystem;
    [SerializeField] private AudioManager audioManager;
    [SerializeField] private TMPro.TextMeshProUGUI textoRelojVR;
    [SerializeField] private TMPro.TextMeshProUGUI textoFolioVR;

    [Header("UI Veredicto Final")]
    [SerializeField] private GameObject panelVeredicto;
    [SerializeField] private GameObject botonesEleccion;
    [SerializeField] private GameObject botonReinicio;
    [SerializeField] private TMPro.TextMeshProUGUI textoResultadoVeredicto;

    private string contenidoFolio = "";
    private bool juegoActivo = true;

    private void Start()
    {


        // Ocultar panel de veredicto si existe
        if (panelVeredicto != null) panelVeredicto.SetActive(false);

        // Forzar settings UI por código para sobreescribir errores del Inspector
        if (textoRelojVR != null)
        {
            textoRelojVR.isRightToLeftText = false; // Fix: 04:46 renderizado como 64:40
            textoRelojVR.alignment = TMPro.TextAlignmentOptions.Center;
        }

        
        if (textoResultadoVeredicto != null)
        {
            // Evitar que el texto se dibuje al revés en VR y asegurar que procese colores HTML
            textoResultadoVeredicto.isRightToLeftText = false;
            textoResultadoVeredicto.alignment = TMPro.TextAlignmentOptions.Center;
            textoResultadoVeredicto.richText = true;
            // Desactivar el word wrapping forzará a que no se divida en saltos de línea extraños por la caja estrecha
            textoResultadoVeredicto.enableWordWrapping = false;
            textoResultadoVeredicto.overflowMode = TMPro.TextOverflowModes.Overflow;
        }

        if (panelVeredicto != null)
        {
            if (botonReinicio == null)
            {
                UnityEngine.UI.Button[] btns = panelVeredicto.GetComponentsInChildren<UnityEngine.UI.Button>(true);
                foreach (var b in btns)
                {
                    string nm = b.gameObject.name.ToLower();
                    if (nm.Contains("reinici") || nm.Contains("restart") || nm.Contains("volver"))
                    {
                        botonReinicio = b.gameObject;
                        Debug.Log("✅ Botón de reinicio auto-encontrado: " + b.gameObject.name);
                        break;
                    }
                }
            }
        }
        
        if (textoFolioVR != null)
        {
            textoFolioVR.isRightToLeftText = false;
            textoFolioVR.alignment = TMPro.TextAlignmentOptions.TopLeft;
            
            // Forzar que el texto SIEMPRE se dibuje, incluso si el recuadro es super pequeño
            textoFolioVR.overflowMode = TMPro.TextOverflowModes.Overflow;
            textoFolioVR.textWrappingMode = TMPro.TextWrappingModes.Normal;
            
            // Quitar márgenes raros que pudieran estar empujando el texto fuera
            textoFolioVR.margin = UnityEngine.Vector4.zero;
            
            // Forzar color negro opaco (tinta sobre papel)
            textoFolioVR.color = UnityEngine.Color.black;
            
            textoFolioVR.enableAutoSizing = true;
            textoFolioVR.fontSizeMin = 10;
            textoFolioVR.fontSizeMax = 50;

            Canvas canvas = textoFolioVR.GetComponentInParent<Canvas>();
            if (canvas != null && canvas.transform.parent != null)
            {
                canvas.transform.localEulerAngles = new UnityEngine.Vector3(90, 0, 0);
                UnityEngine.Vector3 escalaPadre = canvas.transform.parent.lossyScale;
                canvas.transform.localScale = new UnityEngine.Vector3(
                    0.001f / escalaPadre.x,
                    0.001f / escalaPadre.z, 
                    1f 
                );

                canvas.transform.localPosition = new UnityEngine.Vector3(0, 0.51f, 0); 
                
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

        ActualizarFolio($"Inicio del interrogatorio.\nMotivo: {GameContext.Instance.DelitoActual.DescripcionFolio}\n\n");
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
        Debug.Log($"Usuario: '{textoUsuario}' | Gritando: {usuarioGrita}");

        // Obtener respuesta de IA
        string respuestaIA = await dialogueSystem.ObtenerRespuesta(textoUsuario, usuarioGrita);

        if (string.IsNullOrEmpty(respuestaIA))
        {
            Debug.LogError("Error: respuesta IA vacía");
            return;
        }

        // Log de la respuesta completa de la IA para depuración
        Debug.Log($"Respuesta IA completa: '{respuestaIA}'");

        // Detectar y procesar pista (case-insensitive)
        if (respuestaIA.IndexOf("[PISTA]", System.StringComparison.OrdinalIgnoreCase) >= 0)
        {
            Debug.Log("¡PISTA DETECTADA en la respuesta!");
            respuestaIA = respuestaIA.Replace("[PISTA]", "").Replace("[pista]", "").Replace("[Pista]", "").Trim();
            GameContext.Instance.AñadirPista("El sospechoso se ha contradicho o reveló un dato clave.");
        }
        else
        {
            Debug.Log("No se detectó [PISTA] en esta respuesta.");
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

        if (panelVeredicto != null)
        {
            // Mostrar ventana emergente en vez de escribir solo en el folio
            panelVeredicto.SetActive(true);
            if (botonesEleccion != null) botonesEleccion.SetActive(true);
            if (botonReinicio != null) botonReinicio.SetActive(false);
            
            if (textoResultadoVeredicto != null)
            {
                textoResultadoVeredicto.text = "¿Cuál es tu veredicto final sobre Alex?";
            }
        }
        else
        {
            // Fallback original si no has asignado la UI
            string veredicto = GameContext.Instance.EsCulpable 
                ? "<color=red>¡ERA CULPABLE!</color>" 
                : "<color=green>¡ERA INOCENTE!</color>";

            ActualizarFolio($"\n\n<b>TIEMPO AGOTADO</b>\nLa verdad era: {veredicto}");
        }
        
        Debug.Log("Fin del tiempo. Esperando veredicto del jugador...");
    }

    // --- MÉTODOS PÚBLICOS PARA LOS BOTONES DE LA INTERFAZ ---

    public void ElegirCulpable()
    {
        ProcesarVeredictoJugador(true);
    }

    public void ElegirInocente()
    {
        ProcesarVeredictoJugador(false);
    }

    private void ProcesarVeredictoJugador(bool eligioCulpable)
    {
        if (botonesEleccion != null)
        {
            if (botonReinicio != null && botonReinicio.transform.parent == botonesEleccion.transform)
            {
                foreach (Transform child in botonesEleccion.transform)
                {
                    if (child != botonReinicio.transform)
                    {
                        child.gameObject.SetActive(false);
                    }
                }
            }
            else
            {
                botonesEleccion.SetActive(false);
            }
        }

        if (botonReinicio != null) botonReinicio.SetActive(true);

        bool eraCulpable = GameContext.Instance.EsCulpable;
        bool acerto = (eligioCulpable == eraCulpable);

        if (textoResultadoVeredicto != null)
        {
            if (acerto)
            {
                textoResultadoVeredicto.text = "<color=green>¡CASO RESUELTO CORRECTAMENTE!</color>\nFelicidades, has descubierto la verdad.";
            }
            else
            {
                string laVerdaderaCulpabilidad = eraCulpable ? "Culpable" : "Inocente";
                textoResultadoVeredicto.text = $"<color=red>¡VEREDICTO INCORRECTO!</color>\nTe has equivocado. El sospechoso era {laVerdaderaCulpabilidad}.";
            }
        }
    }

    public void ReiniciarInterrogatorio()
    {
        Debug.Log("Reiniciando interrogatorio...");
        SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
    }

    private void ActualizarFolio(string textoExtra)
    {
        if (textoFolioVR != null)
        {
            contenidoFolio += textoExtra;
            textoFolioVR.text = $"<b>CASO: {GameContext.Instance.DelitoActual.ID} - {GameContext.Instance.DelitoActual.TituloFolio}</b>\nSospechoso: Alex\n\n" + contenidoFolio;
            Debug.Log($"Folio actualizado. Texto añadido: '{textoExtra}'");
        }
        else
        {
            Debug.LogError("textoFolioVR NO está asignado en el Inspector. El folio no se puede actualizar.");
        }
    }
}
