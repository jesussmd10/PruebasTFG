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
        
        if (textoEstadoMenu != null) textoEstadoMenu.text = "Precargando caso...";
        
        // Se asume que caseGenerator usa la IAConfig actual
        casoPreGenerado = await caseGenerator.GenerarCasoAsync();
        
        if (textoEstadoMenu != null) textoEstadoMenu.text = "Precargando motor de diálogo en VRAM...";
        
        // Hacer un ping a la IA ligera para que LM Studio la cargue en la tarjeta gráfica antes de empezar a jugar
        await PrecargarModeloDialogo();
        
        if (textoEstadoMenu != null) 
            textoEstadoMenu.text = casoPreGenerado != null ? "¡Sistemas listos! Pulsa Jugar." : "Error al precargar. Se generará al jugar.";
        
        estaGenerando = false;

        // Iniciar la precarga en segundo plano de la escena del juego (VR)
        PrecargarEscenaJuegoBackground();
    }

    private void PrecargarEscenaJuegoBackground()
    {
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
        if (ttsProviderDropdown != null) ttsProviderDropdown.value = PlayerPrefs.GetInt(PREF_PROVIDER, 0);
        if (apiKeyInput != null) apiKeyInput.text = PlayerPrefs.GetString(PREF_API_KEY, "");
        if (voiceIdInput != null) voiceIdInput.text = PlayerPrefs.GetString(PREF_VOICE_ID, "");

        if (urlCasosInput != null) urlCasosInput.text = PlayerPrefs.GetString(PREF_URL_CASOS, "http://localhost:1234/v1/chat/completions");
        if (modeloCasosInput != null) modeloCasosInput.text = PlayerPrefs.GetString(PREF_MOD_CASOS, "meta-llama-3.1-8b-instruct-abliterated");
        
        if (urlDialogoInput != null) urlDialogoInput.text = PlayerPrefs.GetString(PREF_URL_DIALOG, "http://localhost:1234/v1/chat/completions");
        if (modeloDialogoInput != null) modeloDialogoInput.text = PlayerPrefs.GetString(PREF_MOD_DIALOG, "meta-llama-3.1-8b-instruct-abliterated");
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
            
        PlayerPrefs.Save();
    }

    private void OnProviderChanged(int index)
    {
        IAConfig.TTSProvider provider = (IAConfig.TTSProvider)index;
        bool necesitaApi = (provider == IAConfig.TTSProvider.OpenAI || provider == IAConfig.TTSProvider.ElevenLabs);
        
        if (apiKeyInput != null) apiKeyInput.gameObject.SetActive(necesitaApi);
        if (voiceIdInput != null) voiceIdInput.gameObject.SetActive(necesitaApi);
    }

    public void OnValoresEditados()
    {
        // Se puede enlazar este método al OnEndEdit de los InputFields si se quiere
        // que al cambiar la IA se regenere el caso.
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

        if (operacionCargaEscena != null)
        {
            // Restaurar prioridad normal de carga para que la activación sea lo más rápida posible
            Application.backgroundLoadingPriority = ThreadPriority.High;
            
            // Activamos la escena pre-cargada
            operacionCargaEscena.allowSceneActivation = true;
        }
        else
        {
            // Fallback por si la asíncrona falló
            Application.backgroundLoadingPriority = ThreadPriority.High;
            SceneManager.LoadScene(nombreEscenaJuego);
        }
    }
}
