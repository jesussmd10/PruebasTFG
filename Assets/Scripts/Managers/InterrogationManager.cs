using UnityEngine;
using UnityEngine.SceneManagement;
using System.Threading.Tasks;


public class InterrogationManager : MonoBehaviour
{
    [SerializeField] private DialogueSystem dialogueSystem;
    [SerializeField] private AudioManager audioManager;
    [SerializeField] private CaseGenerator caseGenerator;
    [SerializeField] private TMPro.TextMeshProUGUI textoRelojVR;
    [SerializeField] private TMPro.TextMeshProUGUI textoFolioVR;

    [Header("UI Veredicto Final")]
    [SerializeField] private GameObject panelVeredicto;
    [SerializeField] private GameObject botonesEleccion;
    [SerializeField] private GameObject botonReinicio;
    [SerializeField] private TMPro.TextMeshProUGUI textoResultadoVeredicto;

    private string contenidoFolio = "";
    private bool juegoActivo = true;

    private async void Start()
    {


        
        if (panelVeredicto != null) panelVeredicto.SetActive(false);

        
        if (textoRelojVR != null)
        {
            textoRelojVR.isRightToLeftText = false; 
            textoRelojVR.alignment = TMPro.TextAlignmentOptions.Center;
        }

        
        if (textoResultadoVeredicto != null)
        {
            
            textoResultadoVeredicto.isRightToLeftText = false;
            textoResultadoVeredicto.alignment = TMPro.TextAlignmentOptions.Center;
            textoResultadoVeredicto.richText = true;
            
            textoResultadoVeredicto.textWrappingMode = TMPro.TextWrappingModes.NoWrap;
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

        // Mostrar mensaje de carga en el folio mientras la IA genera el caso
        ActualizarFolio("Generando caso...\n");

        // Generar caso con la IA (Opción A)
        GameContext.CasoDelito caso;
        if (caseGenerator != null)
        {
            caso = await caseGenerator.GenerarCasoAsync();
        }
        else
        {
            Debug.LogWarning("CaseGenerator no asignado. Usando caso por defecto.");
            caso = new GameContext.CasoDelito
            {
                ID = "001",
                TituloFolio = "ROBO EN JOYERÍA",
                DescripcionFolio = "Atraco a mano armada en la joyería central.",
                DescripcionPrompt = "un atraco a mano armada en la joyería del centro donde se robaron diamantes",
                Coartada = "en un bar local tomando algo solo",
                Actitud = "Estás aterrado, tartamudeas mucho y casi lloras."
            };
        }

        // Establecer caso en GameContext
        GameContext.Instance.EstablecerCaso(caso);

        // Culpabilidad aleatoria
        bool esCulpable = Random.value > 0.5f;
        GameContext.Instance.ConfigurarCulpabilidad(esCulpable);
        Debug.Log(esCulpable 
            ? "<color=red>🔴 EL SOSPECHOSO ES CULPABLE</color>" 
            : "<color=green>🟢 EL SOSPECHOSO ES INOCENTE</color>");

        // Inicializar IA con el caso generado
        dialogueSystem.InicializarPersonalidad(esCulpable, caso);

        // Suscribirse a eventos
        EventSystem.OnInterrogacionRecibida.AddListener(ProcesarInterrogacion);
        EventSystem.OnInterrogatorioTerminado.AddListener(TerminarJuego);
        EventSystem.OnPistaDescubierta.AddListener(AñadirPistaAlFolio);

        // Limpiar folio y mostrar caso real
        contenidoFolio = "";
        ActualizarFolio($"Inicio del interrogatorio.\nMotivo: {caso.DescripcionFolio}\n\n");
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

        // Obtener respuesta de IA (streaming o clásica según IAConfig)
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

        // Emitir evento para que NPCBehavior procese emociones (si no estamos en streaming)
        // En streaming, las emociones ya se detectaron en tiempo real
        EventSystem.OnRespuestaIA.Invoke(respuestaIA);

        // Si NO usamos streaming, reproducir audio de forma clásica
        // (en streaming, el TTS ya se está procesando via OnFraseListaParaTTS)
        bool streaming = dialogueSystem.UsaStreaming;
        
        if (!streaming)
        {
            string textoLimpio = NPCBehavior.LimpiarTexto(respuestaIA);
            if (!string.IsNullOrEmpty(textoLimpio))
            {
                audioManager.ReproducirTexto(textoLimpio);
            }
        }
    }

    private void AñadirPistaAlFolio(string pista)
    {
        ActualizarFolio($"<b>PISTA OBTENIDA:</b>\n- {pista}\n\n");
    }

    private void TerminarJuego()
    {
        juegoActivo = false;

        // Log de métricas de la sesión
        if (LatencyMetrics.Instance != null)
        {
            Debug.Log(LatencyMetrics.Instance.ObtenerResumenSesion());
        }

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
            
            string veredicto = GameContext.Instance.EsCulpable 
                ? "<color=red>¡ERA CULPABLE!</color>" 
                : "<color=green>¡ERA INOCENTE!</color>";

            ActualizarFolio($"\n\n<b>TIEMPO AGOTADO</b>\nLa verdad era: {veredicto}");
        }
        
        Debug.Log("Fin del tiempo. Esperando veredicto del jugador...");
    }


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

            var caso = GameContext.Instance.DelitoActual;
            if (caso != null)
            {
                textoFolioVR.text = $"<b>CASO: {caso.ID} - {caso.TituloFolio}</b>\nSospechoso: Alex\n\n" + contenidoFolio;
            }
            else
            {
                textoFolioVR.text = contenidoFolio;
            }

            Debug.Log($"Folio actualizado. Texto añadido: '{textoExtra}'");
        }
        else
        {
            Debug.LogError("textoFolioVR NO está asignado en el Inspector. El folio no se puede actualizar.");
        }
    }
}
