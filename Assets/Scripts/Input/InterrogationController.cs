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
    [Range(0.001f, 1f)]
    [Tooltip("Umbral de volumen (RMS) para considerar que se está gritando. En gafas VR, la voz suele ser más baja, un valor entre 0.05 y 0.15 suele funcionar bien.")]
    [SerializeField] private float umbralGrito = 0.08f;

    [Header("Detección de Silencio")]
    [Range(0.0001f, 0.1f)]
    [Tooltip("Si hablas y no te pilla, bájalo. Si Whisper se inventa subtítulos cuando no hablas, súbelo.")]
    [SerializeField] private float umbralSilencio = 0.005f;

    private int posicionInicio;
    private AudioClip clipGrabado;
    private string microfonoActual;
    private bool grabando = false;

    private void Start()
    {
        InicializarMicrófono();

        // Forzar idioma español y proveer un prompt inicial en Whisper con frases completas
        // para mejorar el contexto y evitar que "coartada" se traduzca como "cuarta".
        if (whisper != null)
        {
            whisper.language = "es";
            // Un prompt en formato de frase natural ayuda más a Whisper que palabras sueltas.
            whisper.initialPrompt = "Este es un interrogatorio policial en español. Las respuestas son directas. Vocabulario: coartada, crimen, sospechoso, pruebas, culpable, inocente, asesinato, fiscalía, delito, policía, detective.";
        }
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
        
        StartCoroutine(ProcesarCierreGrabacion());
    }

    private IEnumerator ProcesarCierreGrabacion()
    {
        yield return new WaitForSeconds(0.4f);

        int posicionFinal = Microphone.GetPosition(microfonoActual);

        // Rescatar desde medio segundo antes
        int inicioSeguro = posicionInicio - 8000;
        
        // Manejar el caso en el que el buffer circular del micrófono haya dado la vuelta
        int totalMuestrasClip = clipGrabado.samples;
        if (inicioSeguro < 0) inicioSeguro += totalMuestrasClip;
        
        int cantidadMuestras = posicionFinal - inicioSeguro;
        if (cantidadMuestras < 0) cantidadMuestras += totalMuestrasClip; // Dio la vuelta durante la grabación

        if (cantidadMuestras <= 0 || cantidadMuestras > totalMuestrasClip) yield break;

        // Crear clip limpio
        float[] muestrasRecortadas = new float[cantidadMuestras];
        clipGrabado.GetData(muestrasRecortadas, inicioSeguro);

        float volumenDetectado = CalcularVolumenRMS(muestrasRecortadas);
        bool estaGritando = volumenDetectado > umbralGrito;

        float maxVal = 0f;
        for (int i = 0; i < muestrasRecortadas.Length; i++)
        {
            if (Mathf.Abs(muestrasRecortadas[i]) > maxVal) maxVal = Mathf.Abs(muestrasRecortadas[i]);
        }

        Debug.Log($"<color=cyan>[Micrófono]</color> Volumen RMS: {volumenDetectado:F4} | Pico Máximo: {maxVal:F4} | Gritando: {estaGritando} (Umbral: {umbralGrito:F4})");

        if (volumenDetectado < umbralSilencio)
        {
            Debug.LogWarning($"<color=orange>[Whisper Ignorado]</color> El audio ({volumenDetectado:F4}) era puro silencio o ruido de fondo. Ajusta 'Umbral Silencio' en el inspector si tu voz no entra.");
            yield break;
        }

        if (maxVal > 0.001f)
        {
            // Amplificar la voz. En gafas VR (Quest, etc.) el micro suele estar muy bajo.
            // Aumentamos el límite del multiplicador de 8f a 25f para normalizar bien audios débiles
            // y que Whisper lo escuche "claro y fuerte".
            float multiplicador = Mathf.Min(0.9f / maxVal, 25f); 
            Debug.Log($"<color=cyan>[Audio Normalizado]</color> Multiplicado por {multiplicador:F2}x para Whisper.");
            for (int i = 0; i < muestrasRecortadas.Length; i++)
            {
                muestrasRecortadas[i] *= multiplicador;
            }
        }
        // --------------------------------------

        AudioClip clipRecortado = AudioClip.Create("VozLimpia", cantidadMuestras, 1, 16000, false);
        clipRecortado.SetData(muestrasRecortadas, 0);

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

    private float CalcularVolumenRMS(float[] muestras)
    {
        float suma = 0;
        foreach (float muestra in muestras)
        {
            suma += muestra * muestra;
        }

        return Mathf.Sqrt(suma / muestras.Length);
    }
}
