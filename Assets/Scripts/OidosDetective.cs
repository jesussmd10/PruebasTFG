using UnityEngine;
using Whisper;
using UnityEngine.InputSystem;
public class OidosDetective : MonoBehaviour
{
    public WhisperManager whisper;
    public RespuestaIA cerebro;
    public InputActionReference botonHablarVR;
    private int posicionInicio;
    [Header("Configuración de Grito")]
    [Tooltip("Si el volumen supera este número, es un grito.")]
    [Range(0.01f, 1f)] 
    public float umbralGrito = 0.2f; 
    [Tooltip("Usa 'Peak' (pico máximo) para detectar gritos más fácilmente, o 'RMS' (promedio) para volumen general.")]
    public bool usarMetodoPico = true; 

    private AudioClip clipGrabado;
    private string microfonoActual;
    private bool grabando = false;

    private void Start()
    {
        foreach (var device in Microphone.devices)
        {
            if (device.Contains("Oculus") || device.Contains("Microphone Array"))
            {
                microfonoActual = device;
                break;
            }
        }
        if (string.IsNullOrEmpty(microfonoActual) && Microphone.devices.Length > 0)
        {
            microfonoActual = Microphone.devices[0];
        }
        Debug.Log("MICRÓFONO: " + microfonoActual);

        
        // Así nunca se apaga y evitamos el tirón al pulsar el gatillo.
        clipGrabado = Microphone.Start(microfonoActual, true, 3599, 16000);
    }

    private void Update()
    {
        // Si pulsamos el botón del mando VR y no estamos grabando
        if (botonHablarVR.action.WasPressedThisFrame() && !grabando)
        {
            ComenzarGrabacion();
        }

        // Si soltamos el botón del mando VR y estamos grabando
        if (botonHablarVR.action.WasReleasedThisFrame() && grabando)
        {
            TerminarGrabacion();
        }
    }
    void ComenzarGrabacion()
    {
        grabando = true;
        // Solo anotamos en qué "punto" empezamos a hablar
        posicionInicio = Microphone.GetPosition(microfonoActual);
        Debug.Log("Grabando...");
    }

    async void TerminarGrabacion()
    {
        grabando = false;
        int posicionFinal = Microphone.GetPosition(microfonoActual);
        Debug.Log("Procesando...");

        // Rescatamos medio segundo antes de pulsar el botón (8000 muestras a 16kHz)
        // Usamos Mathf.Max para no salirnos del principio del audio
        int inicioSeguro = Mathf.Max(0, posicionInicio - 8000);
        int cantidadMuestras = posicionFinal - inicioSeguro;

        // Si pulsas y sueltas súper rápido, ignoramos para no crashear
        if (cantidadMuestras <= 0) return;

        float[] muestrasRecortadas = new float[cantidadMuestras];
        clipGrabado.GetData(muestrasRecortadas, inicioSeguro);

        // Creamos el clip exactamente con lo que has hablado
        AudioClip clipRecortado = AudioClip.Create("VozLimpia", cantidadMuestras, 1, 16000, false);
        clipRecortado.SetData(muestrasRecortadas, 0);

        // Detectar si gritas (ahora con el volumen real, sin distorsionar)
        float volumenDetectado = usarMetodoPico ? CalcularVolumenPico(clipRecortado) : CalcularVolumenRMS(clipRecortado);
        bool estaGritando = volumenDetectado > umbralGrito;

        string colorLog = estaGritando ? "<color=red>GRITO</color>" : "<color=green>NORMAL</color>";
        Debug.Log($" Volumen: {volumenDetectado.ToString("F4")} | Umbral: {umbralGrito} | Resultado: {colorLog}");

        // Enviamos el audio limpio a Whisper
        var resultado = await whisper.GetTextAsync(clipRecortado);
        Debug.Log($"<color=cyan>Whisper entendió:</color> {resultado.Result}");

        cerebro.ProcesarInterrogatorio(resultado.Result, estaGritando);
    }

    // Opción A: RMS (Promedio de energía)
    float CalcularVolumenRMS(AudioClip clip)
    {
        float[] muestras = new float[clip.samples];
        clip.GetData(muestras, 0);
        float suma = 0;
        for (int i = 0; i < muestras.Length; i++)
        {
            suma += muestras[i] * muestras[i];
        }
        return Mathf.Sqrt(suma / muestras.Length);
    }

    // Opción B: Pico Máximo
    float CalcularVolumenPico(AudioClip clip)
    {
        float[] muestras = new float[clip.samples];
        clip.GetData(muestras, 0);
        float picoMaximo = 0;
        
        for (int i = 0; i < muestras.Length; i++)
        {
            float valorAbsoluto = Mathf.Abs(muestras[i]);
            if (valorAbsoluto > picoMaximo)
            {
                picoMaximo = valorAbsoluto;
            }
        }
        return picoMaximo;
    }
}