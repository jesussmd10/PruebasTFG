using UnityEngine;
using Whisper;
using UnityEngine.InputSystem;
using System.Threading.Tasks;
using System.Collections;


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

    private void OnDestroy()
    {
        if (!string.IsNullOrEmpty(microfonoActual) && Microphone.IsRecording(microfonoActual))
        {
            Microphone.End(microfonoActual);
            Debug.Log("Micrófono liberado correctamente en OnDestroy.");
        }
        else if (Microphone.IsRecording(null))
        {
            Microphone.End(null);
        }
    }

    // Palabras clave de micrófonos de gafas VR conocidos
    private readonly string[] palabrasClaveVR = new string[]
    {
        "Quest",           // Meta Quest 2 / 3 / Pro
        "Oculus",          // Oculus Rift / Rift S
        "Headset Microphone", // Genérico VR
        "Microphone Array", // Quest Link
        "HMD Mic",         // Algunos HMD
        "Vive",            // HTC Vive
        "Pico",            // Pico Neo / Pico 4
        "Index",           // Valve Index
        "WMR",             // Windows Mixed Reality
    };

    private bool microfonoVREncontrado = false;

    private void InicializarMicrófono()
    {
        Debug.Log("Micrófonos disponibles (" + Microphone.devices.Length + "):");
        for (int i = 0; i < Microphone.devices.Length; i++)
        {
            Debug.Log($"   [{i}] \"{Microphone.devices[i]}\"");
        }

        microfonoVREncontrado = BuscarMicrofonoVR();

        if (!microfonoVREncontrado)
        {
            if (Microphone.devices.Length > 0)
            {
                microfonoActual = Microphone.devices[0];
                Debug.Log("Micrófono VR no encontrado. Usando fallback: " + microfonoActual);
                StartCoroutine(ReintentarBusquedaVR());
            }
            else
            {
                Debug.LogError("No hay ningún micrófono disponible");
                StartCoroutine(ReintentarBusquedaVR());
                return;
            }
        }

        IniciarGrabacion();
    }

    /// <summary>
    /// Busca un micrófono VR entre los dispositivos disponibles
    /// </summary>
    private bool BuscarMicrofonoVR()
    {
        foreach (var device in Microphone.devices)
        {
            foreach (var palabraClave in palabrasClaveVR)
            {
                if (device.IndexOf(palabraClave, System.StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    microfonoActual = device;
                    Debug.Log("Micrófono VR encontrado: " + microfonoActual);
                    return true;
                }
            }
        }
        return false;
    }

    /// <summary>
    /// Reintenta buscar el micrófono VR cada 2 segundos (por si las gafas se conectan tarde)
    /// </summary>
    private IEnumerator ReintentarBusquedaVR()
    {
        while (!microfonoVREncontrado)
        {
            yield return new WaitForSeconds(2f);

            if (BuscarMicrofonoVR())
            {
                microfonoVREncontrado = true;
                Debug.Log("Micrófono VR detectado tras reintento: " + microfonoActual);

                // Detener la grabación anterior y reiniciar con el micrófono VR
                if (Microphone.IsRecording(null))
                {
                    Microphone.End(null);
                }
                IniciarGrabacion();
            }
        }
    }

    private void IniciarGrabacion()
    {
        if (string.IsNullOrEmpty(microfonoActual))
        {
            Debug.LogError("No hay micrófono para iniciar grabación");
            return;
        }

        Debug.Log("Micrófono activo: " + microfonoActual);

        // Grabar continuamente
        clipGrabado = Microphone.Start(microfonoActual, true, 3599, 16000);
    }

    private void ComenzarGrabacion(InputAction.CallbackContext context)
    {
        if (grabando) return;
        grabando = true;
        posicionInicio = Microphone.GetPosition(microfonoActual);
        Debug.Log("Grabando...");
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

        Debug.Log($"Volumen: {volumenDetectado:F4} | Gritando: {estaGritando}");

        // Procesar con Whisper
        ProcesarAudio(clipRecortado, estaGritando);
    }

    private async void ProcesarAudio(AudioClip clip, bool estaGritando)
    {
        var resultado = await whisper.GetTextAsync(clip);
        Debug.Log($"Transcrito: {resultado.Result}");

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
