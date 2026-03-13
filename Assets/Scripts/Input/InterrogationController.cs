using UnityEngine;
using Whisper;
using UnityEngine.InputSystem;
using System.Threading.Tasks;


public class InterrogationController : MonoBehaviour
{
    [SerializeField] private WhisperManager whisper;
    [SerializeField] private InputActionReference botonHablarVR;

    [Header("Detección de Grito")]
    [Range(0.01f, 1f)]
    [SerializeField] private float umbralGrito = 0.2f;
    [SerializeField] private bool usarMetodoPico = true;

    private int posicionInicio;
    private AudioClip clipGrabado;
    private string microfonoActual;
    private bool grabando = false;

    private void Start()
    {
        InicializarMicrófono();
    }

    private void OnEnable()
    {
        if (botonHablarVR != null)
        {
            botonHablarVR.action.started += ComenzarGrabacion;
            botonHablarVR.action.canceled += TerminarGrabacion;
        }
    }

    private void OnDisable()
    {
        if (botonHablarVR != null)
        {
            botonHablarVR.action.started -= ComenzarGrabacion;
            botonHablarVR.action.canceled -= TerminarGrabacion;
        }
    }

    private void InicializarMicrófono()
    {
        // Primero: intentar encontrar micrófono de gafas VR
        foreach (var device in Microphone.devices)
        {
            if ( device.Contains("Microphone Array"))
            {
                microfonoActual = device;
                Debug.Log("✅ Micrófono VR encontrado: " + microfonoActual);
                break;
            }
        }

        // Fallback: Si no hay VR, usar primer micrófono disponible (portátil)
        if (string.IsNullOrEmpty(microfonoActual) && Microphone.devices.Length > 0)
        {
            microfonoActual = Microphone.devices[0];
            Debug.Log("⚠️ Micrófono VR no encontrado. Usando portátil: " + microfonoActual);
        }

        if (string.IsNullOrEmpty(microfonoActual))
        {
            Debug.LogError("❌ No hay micrófono disponible");
            return;
        }

        Debug.Log("🎤 Micrófono activo: " + microfonoActual);

        // Grabar continuamente
        clipGrabado = Microphone.Start(microfonoActual, true, 3599, 16000);
    }

    private void ComenzarGrabacion(InputAction.CallbackContext context)
    {
        if (grabando) return;
        grabando = true;
        posicionInicio = Microphone.GetPosition(microfonoActual);
        Debug.Log("🔴 Grabando...");
    }

    private void TerminarGrabacion(InputAction.CallbackContext context)
    {
        if (!grabando) return;
        grabando = false;

        int posicionFinal = Microphone.GetPosition(microfonoActual);

        // Rescatar desde medio segundo antes
        int inicioSeguro = Mathf.Max(0, posicionInicio - 8000);
        int cantidadMuestras = posicionFinal - inicioSeguro;

        if (cantidadMuestras <= 0) return;

        // Crear clip limpio
        float[] muestrasRecortadas = new float[cantidadMuestras];
        clipGrabado.GetData(muestrasRecortadas, inicioSeguro);

        AudioClip clipRecortado = AudioClip.Create("VozLimpia", cantidadMuestras, 1, 16000, false);
        clipRecortado.SetData(muestrasRecortadas, 0);

        // Detectar grito
        float volumenDetectado = usarMetodoPico 
            ? CalcularVolumenPico(clipRecortado) 
            : CalcularVolumenRMS(clipRecortado);
        
        bool estaGritando = volumenDetectado > umbralGrito;

        Debug.Log($"📊 Volumen: {volumenDetectado:F4} | Gritando: {estaGritando}");

        // Procesar con Whisper
        ProcesarAudio(clipRecortado, estaGritando);
    }

    private async void ProcesarAudio(AudioClip clip, bool estaGritando)
    {
        var resultado = await whisper.GetTextAsync(clip);
        Debug.Log($"🗣️ Transcrito: {resultado.Result}");

        // Enviar evento para que otros sistemas lo procesen
        EventSystem.OnInterrogacionRecibida.Invoke(resultado.Result, estaGritando);
    }

    private float CalcularVolumenRMS(AudioClip clip)
    {
        float[] muestras = new float[clip.samples];
        clip.GetData(muestras, 0);
        float suma = 0;

        foreach (float muestra in muestras)
        {
            suma += muestra * muestra;
        }

        return Mathf.Sqrt(suma / muestras.Length);
    }

    private float CalcularVolumenPico(AudioClip clip)
    {
        float[] muestras = new float[clip.samples];
        clip.GetData(muestras, 0);
        float picoMaximo = 0;

        foreach (float muestra in muestras)
        {
            float valorAbsoluto = Mathf.Abs(muestra);
            if (valorAbsoluto > picoMaximo)
            {
                picoMaximo = valorAbsoluto;
            }
        }

        return picoMaximo;
    }
}
