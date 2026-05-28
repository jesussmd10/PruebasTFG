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
    [SerializeField] private TMPro.TextMeshProUGUI textoResumenVeredicto;

    private string contenidoFolio = "";
    private bool juegoActivo = false;

    // Se llama desde el MainMenuManager para asegurar que no se pise la UI al arrancar
    public void ForzarApagadoUI()
    {
        if (panelVeredicto != null) panelVeredicto.SetActive(false);
        if (textoFolioVR != null) textoFolioVR.text = "";
    }

    public async void PrepararNuevaPartida()
    {
        // juegoActivo se activará al final para evitar que el reloj corra mientras se genera el caso
        contenidoFolio = "";

        if (panelVeredicto != null) panelVeredicto.SetActive(false);

        // Buscar y reiniciar el movimiento del NPC para que vuelva a hacer la animación de llegada
        NPCMovement npcMovement = UnityEngine.Object.FindFirstObjectByType<NPCMovement>(FindObjectsInactive.Include);
        if (npcMovement != null)
        {
            // Apagamos y encendemos para forzar a que los triggers de la puerta (OnTriggerExit/Enter) se reseteen correctamente
            npcMovement.gameObject.SetActive(false);
            npcMovement.ReiniciarMovimiento();
            npcMovement.gameObject.SetActive(true);
        }

        // Buscar la puerta y reactivar su animador para que funcione al empezar/reiniciar
        Animator[] animators = UnityEngine.Object.FindObjectsByType<Animator>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        foreach (var anim in animators)
        {
            string nm = anim.gameObject.name.ToLower();
            if (nm.Contains("puerta") || nm.Contains("door"))
            {
                anim.gameObject.SetActive(true);
                anim.enabled = true; // Activar el animador
                anim.Rebind();       // Resetear la animación al inicio
                anim.Play(0, -1, 0f); // Forzar reproducción desde el principio
            }
        }

        
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

        // Lógica de inicio de partida

        GameContext.CasoDelito caso = GameContext.CasoPrecargado;

        if (caso == null)
        {
            ActualizarFolio("Generando caso en tiempo real...\n");
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
        }
        else
        {
            // Limpiar el caso precargado para que no se reutilice si se reinicia la escena directamente
            GameContext.CasoPrecargado = null;
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

        // Suscribirse a eventos (evitar duplicados)
        EventSystem.OnInterrogacionRecibida.RemoveListener(ProcesarInterrogacion);
        EventSystem.OnInterrogatorioTerminado.RemoveListener(TerminarJuego);
        EventSystem.OnPistaDescubierta.RemoveListener(AñadirPistaAlFolio);
        
        EventSystem.OnInterrogacionRecibida.AddListener(ProcesarInterrogacion);
        EventSystem.OnInterrogatorioTerminado.AddListener(TerminarJuego);
        EventSystem.OnPistaDescubierta.AddListener(AñadirPistaAlFolio);

        // Reiniciar estado del juego (tiempo, etc.)
        GameContext.Instance.ReiniciarEstado();
        
        // Reiniciar animación del personaje a estado tranquilo
        EventSystem.OnEmotionChanged.Invoke(EmotionState.Calmado);

        // Ahora sí, activar el juego para que el reloj empiece a correr
        juegoActivo = true;

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

        // Evitar que el timer salte bruscamente si hubo un parón largo (ej. generación en segundo plano)
        float dt = Time.deltaTime;
        if (dt > 1f) dt = 1f; 

        GameContext.Instance.ReducirTiempo(dt);

        // Actualizar reloj
        int minutos = Mathf.FloorToInt(GameContext.Instance.TiempoRestante / 60);
        int segundos = Mathf.FloorToInt(GameContext.Instance.TiempoRestante % 60);
        if (textoRelojVR != null)
        {
            textoRelojVR.text = string.Format("{0:00}:{1:00}", minutos, segundos);
        }
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

        // Detectar y procesar pista dinámica (e.g. [PISTA: Se contradijo con la hora])
        string patronPista = @"\[PISTA[:\s]*(.*?)\]";
        var matchPista = System.Text.RegularExpressions.Regex.Match(respuestaIA, patronPista, System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        
        if (matchPista.Success)
        {
            Debug.Log("¡PISTA DETECTADA en la respuesta!");
            
            string descripcionPista = matchPista.Groups[1].Value.Trim();
            if (string.IsNullOrEmpty(descripcionPista) || descripcionPista.Length < 3)
            {
                descripcionPista = "El sospechoso se ha contradicho o reveló un dato clave.";
            }
            
            GameContext.Instance.AñadirPista(descripcionPista);
            
            // Eliminar el tag de la respuesta para que no se escuche en TTS
            respuestaIA = System.Text.RegularExpressions.Regex.Replace(respuestaIA, patronPista, "", System.Text.RegularExpressions.RegexOptions.IgnoreCase).Trim();
        }
        else
        {
            Debug.Log("No se detectó [PISTA] en esta respuesta.");
        }

        // Emitir evento para que NPCBehavior procese emociones
        EventSystem.OnRespuestaIA.Invoke(respuestaIA);

        // Reproducir la respuesta completa de una sola vez para mantener la entonación y las emociones naturales
        if (!string.IsNullOrEmpty(respuestaIA))
        {
            audioManager.ReproducirTexto(respuestaIA); // Pasamos respuestaIA con las marcas de emoción para que AudioManager las detecte
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
            if (botonesEleccion != null) 
            {
                botonesEleccion.SetActive(true);
                // Reactivar los botones de Culpable/Inocente por si fueron desactivados en una partida anterior
                foreach (Transform child in botonesEleccion.transform)
                {
                    if (botonReinicio == null || child != botonReinicio.transform)
                    {
                        child.gameObject.SetActive(true);
                    }
                }
            }
            if (botonReinicio != null) botonReinicio.SetActive(false);
            
            if (textoResultadoVeredicto != null)
            {
                textoResultadoVeredicto.text = "¿Cuál es tu veredicto final sobre Alex?";
            }
            if (textoResumenVeredicto != null)
            {
                textoResumenVeredicto.text = ""; // Limpiar resumen
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

    private async void ProcesarVeredictoJugador(bool eligioCulpable)
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

        // NO activar todavía el botón de reinicio
        if (botonReinicio != null) botonReinicio.SetActive(false);

        bool eraCulpable = GameContext.Instance.EsCulpable;
        bool acerto = (eligioCulpable == eraCulpable);
        var caso = GameContext.Instance.DelitoActual;

        if (textoResultadoVeredicto != null)
        {
            textoResultadoVeredicto.text = acerto 
                ? "<align=center><size=150%><color=green>¡CASO RESUELTO CORRECTAMENTE!</color></size>\n<size=110%>Felicidades, has descubierto la verdad.</size></align>"
                : $"<align=center><size=150%><color=red>¡VEREDICTO INCORRECTO!</color></size>\n<size=110%>Te has equivocado. El sospechoso era {(eraCulpable ? "Culpable" : "Inocente")}.</size></align>";
        }

        string estadoSiguienteCaso = "<i>Generando próximo caso en segundo plano...</i>";
        string resumen = $"<align=center><size=130%><b>RESUMEN DEL CASO:</b></size>\n" +
                         $"<size=90%>- <b>Actitud:</b> {caso.Actitud}\n" +
                         $"- <b>Coartada falsa:</b> {caso.Coartada}\n" +
                         $"- <b>Secreto {(eraCulpable ? "Criminal" : "Vergonzoso")}:</b> {caso.Secreto}\n\n" +
                         $"{estadoSiguienteCaso}</size></align>";

        if (textoResumenVeredicto != null)
        {
            textoResumenVeredicto.text = resumen;
        }
        else if (textoResultadoVeredicto != null) // Fallback si no has asignado el texto en Unity aún
        {
            textoResultadoVeredicto.text += "\n\n" + resumen;
        }

        // Generar el próximo caso en segundo plano
        if (caseGenerator != null)
        {
            GameContext.CasoPrecargado = await caseGenerator.GenerarCasoAsync();
        }

        // Activar el botón de reinicio y actualizar texto
        string textoListo = "<b>¡Siguiente caso listo!</b>";
        if (textoResumenVeredicto != null)
        {
            textoResumenVeredicto.text = textoResumenVeredicto.text.Replace(estadoSiguienteCaso, textoListo);
        }
        else if (textoResultadoVeredicto != null)
        {
            textoResultadoVeredicto.text = textoResultadoVeredicto.text.Replace(estadoSiguienteCaso, textoListo);
        }
        
        if (botonReinicio != null) botonReinicio.SetActive(true);
    }

    public void ReiniciarInterrogatorio()
    {
        Debug.Log("Iniciando siguiente interrogatorio en Single-Scene...");
        
        System.GC.Collect();

        // Limpiar el texto del folio actual
        if (textoFolioVR != null) textoFolioVR.text = "";

        // En vez de destruir la escena, simplemente escondemos el veredicto 
        // y arrancamos la partida de nuevo con el caso que ya está precargado
        PrepararNuevaPartida();
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
