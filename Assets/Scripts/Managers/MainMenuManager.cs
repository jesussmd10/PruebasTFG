using UnityEngine;
using UnityEngine.SceneManagement;
using TMPro;

public class MainMenuManager : MonoBehaviour
{
    [SerializeField] private IAConfig iaConfig;
    
    [Header("UI Elements")]
    [SerializeField] private TMP_Dropdown ttsProviderDropdown;
    [SerializeField] private TMP_InputField apiKeyInput;
    [SerializeField] private TMP_InputField voiceIdInput;
    
    [Header("Escena a cargar")]
    [SerializeField] private string nombreEscenaJuego = "SalaInterrogatorio";

    private const string PREF_PROVIDER = "TTS_Provider";
    private const string PREF_API_KEY = "TTS_ApiKey";
    private const string PREF_VOICE_ID = "TTS_VoiceId";

    private void Start()
    {
        // Cargar datos guardados si existen
        CargarPreferencias();

        // Escuchar cambios en el dropdown para mostrar/ocultar campos (opcional si quieres hacerlo visual)
        if (ttsProviderDropdown != null)
        {
            ttsProviderDropdown.onValueChanged.AddListener(OnProviderChanged);
            OnProviderChanged(ttsProviderDropdown.value); // Forzar actualización inicial
        }
    }

    private void CargarPreferencias()
    {
        if (ttsProviderDropdown != null)
            ttsProviderDropdown.value = PlayerPrefs.GetInt(PREF_PROVIDER, 0);
            
        if (apiKeyInput != null)
            apiKeyInput.text = PlayerPrefs.GetString(PREF_API_KEY, "");
            
        if (voiceIdInput != null)
            voiceIdInput.text = PlayerPrefs.GetString(PREF_VOICE_ID, "");
    }

    private void GuardarPreferencias()
    {
        if (ttsProviderDropdown != null)
            PlayerPrefs.SetInt(PREF_PROVIDER, ttsProviderDropdown.value);
            
        if (apiKeyInput != null)
            PlayerPrefs.SetString(PREF_API_KEY, apiKeyInput.text);
            
        if (voiceIdInput != null)
            PlayerPrefs.SetString(PREF_VOICE_ID, voiceIdInput.text);
            
        PlayerPrefs.Save();
    }

    private void OnProviderChanged(int index)
    {
        IAConfig.TTSProvider provider = (IAConfig.TTSProvider)index;
        
        // Si eligen EdgeTTS, no necesitan API Key, podríamos ocultar el input field
        bool necesitaApi = (provider == IAConfig.TTSProvider.OpenAI || provider == IAConfig.TTSProvider.ElevenLabs);
        
        if (apiKeyInput != null)
        {
            apiKeyInput.gameObject.SetActive(necesitaApi);
        }
        
        if (voiceIdInput != null)
        {
            // EdgeTTS no usa este Voice ID personalizado, usa el que tiene hardcodeado o podríamos adaptarlo
            voiceIdInput.gameObject.SetActive(necesitaApi);
            
            // Sugerencias por defecto si el campo está vacío eliminadas a petición

        }
    }

    public void EmpezarJuego()
    {
        GuardarPreferencias();

        // Aplicar a IAConfig
        if (iaConfig != null)
        {
            if (ttsProviderDropdown != null)
                iaConfig.ttsProvider = (IAConfig.TTSProvider)ttsProviderDropdown.value;
                
            if (apiKeyInput != null)
                iaConfig.ttsApiKey = apiKeyInput.text;
                
            if (voiceIdInput != null)
                iaConfig.ttsVoiceId = voiceIdInput.text;
        }
        else
        {
            Debug.LogError("IAConfig no está asignado en MainMenuManager");
        }

        // Cargar Escena
        SceneManager.LoadScene(nombreEscenaJuego);
    }
}
