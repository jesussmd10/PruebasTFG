using UnityEngine;
using UnityEngine.SceneManagement;
using TMPro;
using System.Threading.Tasks;

public class MainMenuManager : MonoBehaviour
{
    [SerializeField] private IAConfig iaConfig;
    [SerializeField] private CaseGenerator caseGenerator;
    
    [Header("Escena a cargar")]
    [SerializeField] private string nombreEscenaJuego = "SalaInterrogatorio";
    
    [Header("UI Elements TTS")]
    [SerializeField] private TMP_Dropdown ttsProviderDropdown;
    [SerializeField] private TMP_InputField apiKeyInput;
    [SerializeField] private TMP_InputField voiceIdInput;

    [Header("UI Elements IA Casos (Pesada)")]
    [SerializeField] private TMP_InputField urlCasosInput;
    [SerializeField] private TMP_InputField modeloCasosInput;

    [Header("UI Elements IA Diálogo (Ligera)")]
    [SerializeField] private TMP_InputField urlDialogoInput;
    [SerializeField] private TMP_InputField modeloDialogoInput;
    
    [Header("UI Duración")]
    [SerializeField] private TMP_Dropdown duracionDropdown;
    
    [Header("UI Estado")]
    [SerializeField] private TextMeshProUGUI textoEstadoMenu; // Opcional, para mostrar "Generando caso en background..."

    private const string PREF_PROVIDER = "TTS_Provider";
    private const string PREF_API_KEY = "TTS_ApiKey";
    private const string PREF_VOICE_ID = "TTS_VoiceId";
    private const string PREF_URL_CASOS = "IA_UrlCasos";
    private const string PREF_MOD_CASOS = "IA_ModCasos";
    private const string PREF_URL_DIALOG = "IA_UrlDialog";
    private const string PREF_MOD_DIALOG = "IA_ModDialog";

    private GameContext.CasoDelito casoPreGenerado;
    private bool estaGenerando = false;
    private AsyncOperation operacionCargaEscena;

    private void Awake()
    {
        // Limpieza automática a prueba de balas para Escena Única:
        // Buscamos si el InterrogationManager está en la misma escena y lo obligamos a apagar su UI,
        // pero dejamos el GameObject encendido para que el CaseGenerator pueda precargar de fondo.
        InterrogationManager interrogation = FindAnyObjectByType<InterrogationManager>(FindObjectsInactive.Include);
        if (interrogation != null)
        {
            interrogation.ForzarApagadoUI();
        }

        // Ocultar al NPC en el menú principal para que no camine ni active la puerta antes de jugar
        NPCMovement npcMovement = FindAnyObjectByType<NPCMovement>(FindObjectsInactive.Include);
        if (npcMovement != null)
        {
            npcMovement.GuardarPosicion(); // Guardar su posición original antes de apagarlo
            npcMovement.gameObject.SetActive(false);
        }

        // Asegurar que la puerta sea visible pero su animación no se ejecute en el menú principal
        Animator[] animators = FindObjectsByType<Animator>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        foreach (var anim in animators)
        {
            string nm = anim.gameObject.name.ToLower();
            if (nm.Contains("puerta") || nm.Contains("door"))
            {
                anim.gameObject.SetActive(true); // Que se vea la puerta (por si el usuario la ocultó)
                anim.enabled = false;            // Pero que no se anime
            }
        }
    }

    private void Start()
    {
        CargarPreferencias();

        if (ttsProviderDropdown != null)
        {
            ttsProviderDropdown.onValueChanged.AddListener(OnProviderChanged);
            OnProviderChanged(ttsProviderDropdown.value);
        }

        // Si tenemos configuración, empezar a generar el caso en background
        AplicarAConfig();
        if (caseGenerator != null && !string.IsNullOrEmpty(iaConfig.urlModeloCasos))
        {
            _ = GenerarCasoBackgroundAsync();
        }
    }

    private async Task GenerarCasoBackgroundAsync()
    {
        if (estaGenerando) return;
        estaGenerando = true;
        
        try
        {
            if (textoEstadoMenu != null) textoEstadoMenu.text = "Precargando caso...";
            
            // Se asume que caseGenerator usa la IAConfig actual
            casoPreGenerado = await caseGenerator.GenerarCasoAsync();
            
            // OPTIMIZACIÓN: Si usamos el mismo LLM para crear el caso y para hablar,
            // no hace falta hacer un "ping" para cargarlo en VRAM porque ¡ya está cargado!
            bool esMismoModelo = (iaConfig.urlModeloCasos == iaConfig.urlModeloDialogo) && 
                                 (iaConfig.nombreModeloCasos == iaConfig.nombreModeloDialogo);
                                 
            if (!esMismoModelo)
            {
                if (textoEstadoMenu != null) textoEstadoMenu.text = "Precargando motor de diálogo en VRAM...";
                await PrecargarModeloDialogo();
            }
            else
            {
                Debug.Log("[MainMenuManager] El modelo de casos y diálogo es el mismo. Se omite el ping de precarga por eficiencia.");
            }
            
            if (textoEstadoMenu != null) 
                textoEstadoMenu.text = casoPreGenerado != null ? "¡Sistemas listos!" : "Error al precargar. Se generará al jugar.";

            // Iniciar la precarga en segundo plano de la escena del juego (VR)
            PrecargarEscenaJuegoBackground();
        }
        finally
        {
            estaGenerando = false;
        }
    }

    private void PrecargarEscenaJuegoBackground()
    {
        // Si estamos en arquitectura de Escena Única, NO hay que cargar ninguna escena
        if (FindAnyObjectByType<InterrogationManager>(FindObjectsInactive.Include) != null)
        {
            return;
        }

        if (operacionCargaEscena == null)
        {
            // Bajar la prioridad para que la precarga de la escena no dé tirones en el menú
            Application.backgroundLoadingPriority = ThreadPriority.Low;
            
            operacionCargaEscena = SceneManager.LoadSceneAsync(nombreEscenaJuego);
            if (operacionCargaEscena != null)
            {
                operacionCargaEscena.allowSceneActivation = false; // Detenemos la activación hasta darle al botón
            }
        }
    }

    private async Task PrecargarModeloDialogo()
    {
        try
        {
            if (iaConfig == null || string.IsNullOrEmpty(iaConfig.urlModeloDialogo)) return;

            var requestData = new
            {
                model = iaConfig.nombreModeloDialogo,
                messages = new[] { new { role = "user", content = "ping" } },
                max_tokens = 1
            };

            string jsonBody = Newtonsoft.Json.JsonConvert.SerializeObject(requestData);
            using (var request = new UnityEngine.Networking.UnityWebRequest(iaConfig.urlModeloDialogo, "POST"))
            {
                byte[] bodyRaw = System.Text.Encoding.UTF8.GetBytes(jsonBody);
                request.uploadHandler = new UnityEngine.Networking.UploadHandlerRaw(bodyRaw);
                request.downloadHandler = new UnityEngine.Networking.DownloadHandlerBuffer();
                request.SetRequestHeader("Content-Type", "application/json");

                var operation = request.SendWebRequest();
                while (!operation.isDone)
                {
                    await Task.Yield();
                }
                Debug.Log("[MainMenuManager] Modelo de diálogo cargado en VRAM exitosamente.");
            }
        }
        catch (System.Exception e)
        {
            Debug.LogWarning("[MainMenuManager] Error al hacer ping a la IA de diálogo: " + e.Message);
        }
    }

    private void CargarPreferencias()
    {
        if (ttsProviderDropdown != null) ttsProviderDropdown.value = PlayerPrefs.GetInt(PREF_PROVIDER, (int)IAConfig.TTSProvider.EdgeTTS);
        if (apiKeyInput != null) apiKeyInput.text = PlayerPrefs.GetString(PREF_API_KEY, "");
        if (voiceIdInput != null) voiceIdInput.text = PlayerPrefs.GetString(PREF_VOICE_ID, "");

        if (urlCasosInput != null) urlCasosInput.text = PlayerPrefs.GetString(PREF_URL_CASOS, "http://localhost:11434/v1/chat/completions");
        if (modeloCasosInput != null) modeloCasosInput.text = PlayerPrefs.GetString(PREF_MOD_CASOS, "llama3");

        if (urlDialogoInput != null) urlDialogoInput.text = PlayerPrefs.GetString(PREF_URL_DIALOG, "http://localhost:11434/v1/chat/completions");
        if (modeloDialogoInput != null) modeloDialogoInput.text = PlayerPrefs.GetString(PREF_MOD_DIALOG, "llama3");
        
        if (duracionDropdown != null) duracionDropdown.value = PlayerPrefs.GetInt("DuracionInterrogatorio", 1); // 1 = 5 minutos por defecto
    }

    private void GuardarPreferencias()
    {
        if (ttsProviderDropdown != null) PlayerPrefs.SetInt(PREF_PROVIDER, ttsProviderDropdown.value);
        if (apiKeyInput != null) PlayerPrefs.SetString(PREF_API_KEY, apiKeyInput.text);
        if (voiceIdInput != null) PlayerPrefs.SetString(PREF_VOICE_ID, voiceIdInput.text);
        
        if (urlCasosInput != null) PlayerPrefs.SetString(PREF_URL_CASOS, urlCasosInput.text);
        if (modeloCasosInput != null) PlayerPrefs.SetString(PREF_MOD_CASOS, modeloCasosInput.text);
        
        if (urlDialogoInput != null) PlayerPrefs.SetString(PREF_URL_DIALOG, urlDialogoInput.text);
        if (modeloDialogoInput != null) PlayerPrefs.SetString(PREF_MOD_DIALOG, modeloDialogoInput.text);
        if (duracionDropdown != null) PlayerPrefs.SetInt("DuracionInterrogatorio", duracionDropdown.value);
            
        PlayerPrefs.Save();
    }

    private void OnProviderChanged(int index)
    {
        IAConfig.TTSProvider provider = (IAConfig.TTSProvider)index;
        bool necesitaApi = (provider == IAConfig.TTSProvider.OpenAI || provider == IAConfig.TTSProvider.ElevenLabs);
        
        if (apiKeyInput != null) apiKeyInput.gameObject.SetActive(necesitaApi);
        if (voiceIdInput != null) voiceIdInput.gameObject.SetActive(necesitaApi);
    }

    public void BotonRecargarCaso()
    {
        // Este método está diseñado para conectarlo a un Botón de Unity.
        // Aplica la nueva URL/Modelo que hayas escrito en la interfaz, guarda las preferencias, 
        // y le pide a la IA que genere un caso nuevo inmediatamente.
        GuardarPreferencias();
        AplicarAConfig();
        
        if (caseGenerator != null)
        {
             _ = GenerarCasoBackgroundAsync();
        }
    }

    private void AplicarAConfig()
    {
        if (iaConfig == null) return;

        if (ttsProviderDropdown != null) iaConfig.ttsProvider = (IAConfig.TTSProvider)ttsProviderDropdown.value;
        if (apiKeyInput != null) iaConfig.ttsApiKey = apiKeyInput.text;
        if (voiceIdInput != null) iaConfig.ttsVoiceId = voiceIdInput.text;

        if (urlCasosInput != null) iaConfig.urlModeloCasos = urlCasosInput.text;
        if (modeloCasosInput != null) iaConfig.nombreModeloCasos = modeloCasosInput.text;

        if (urlDialogoInput != null) iaConfig.urlModeloDialogo = urlDialogoInput.text;
        if (modeloDialogoInput != null) iaConfig.nombreModeloDialogo = modeloDialogoInput.text;
    }

    public async void EmpezarJuego()
    {
        GuardarPreferencias();
        AplicarAConfig();

        if (casoPreGenerado != null)
        {
            GameContext.CasoPrecargado = casoPreGenerado;
        }

        if (textoEstadoMenu != null)
        {
            textoEstadoMenu.text = "Transfiriendo a la sala de interrogatorios...";
        }

        // 1. Forzar recolección de basura ANTES de cambiar de escena
        System.GC.Collect();
        
        // 2. Dar un pequeño respiro al hilo principal
        await Task.Delay(200);

        // 3. Aplicar duración al GameContext
        if (duracionDropdown != null)
        {
            float tiempoElegido = 300f; // 5 min default
            switch (duracionDropdown.value)
            {
                case 0: tiempoElegido = 180f; break; // 3 min
                case 1: tiempoElegido = 300f; break; // 5 min
                case 2: tiempoElegido = 600f; break; // 10 min
                case 3: tiempoElegido = 900f; break; // 15 min
            }
            GameContext.Instance.SetTiempoPartida(tiempoElegido);
        }

        // BUSCAMOS SI ESTAMOS EN UNA ARQUITECTURA DE ESCENA ÚNICA
        InterrogationManager interrogation = FindAnyObjectByType<InterrogationManager>(FindObjectsInactive.Include);
        
        if (interrogation != null)
        {
            // CERO LAG: Solo ocultamos el menú y arrancamos el interrogatorio
            interrogation.gameObject.SetActive(true);
            
            // Ocultamos el canvas del menú de forma segura
            Canvas menuCanvas = null;
            if (textoEstadoMenu != null) menuCanvas = textoEstadoMenu.canvas;
            if (menuCanvas == null) menuCanvas = GetComponentInParent<Canvas>();

            if (menuCanvas != null) menuCanvas.gameObject.SetActive(false);
            else this.gameObject.SetActive(false);

            interrogation.PrepararNuevaPartida();
        }
        else
        {
            // FALLBACK: Si el usuario sigue usando 2 escenas separadas
            if (operacionCargaEscena != null)
            {
                Application.backgroundLoadingPriority = ThreadPriority.High;
                operacionCargaEscena.allowSceneActivation = true;
            }
            else
            {
                Application.backgroundLoadingPriority = ThreadPriority.High;
                SceneManager.LoadScene(nombreEscenaJuego);
            }
        }
    }
}
