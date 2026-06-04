using UnityEngine;
using UnityEngine.UI;

public class MicLevelVisualizer : MonoBehaviour
{
    [Header("UI Elements")]
    [Tooltip("El Slider que mostrará el nivel de voz actual")]
    [SerializeField] private Slider barraVolumen;
    
    [Tooltip("La imagen 'Fill' dentro del Slider que cambiará a color rojo")]
    [SerializeField] private Image fillArea;

    [Header("Configuración")]
    [Tooltip("El mismo umbral de grito que tienes en tu InterrogationController (ej. 0.05)")]
    [SerializeField] private float umbralGrito = 0.05f;

    [Tooltip("Si la barra apenas se mueve al gritar, sube esto (ej. 2 o 3) para amplificar tu voz matemáticamente.")]
    [SerializeField] private float multiplicadorGanancia = 1f;

    [SerializeField] private Color colorNormal = Color.green;
    [SerializeField] private Color colorGrito = Color.red;

    private string microfonoActual;
    private AudioClip clipMicrofono;
    private float[] muestras = new float[4000];
    private float tiempoRestanteRojo = 0f;

    private float maxRmsConsole = 0f;
    private float consoleTimer = 0f;

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

    private bool yoInicieElMicro = false;

    private void Awake()
    {
        // Guardar la calibración del Inspector para que el InterrogationController la use en la sala de juego
        PlayerPrefs.SetFloat("UmbralGritoGuardado", umbralGrito);
        PlayerPrefs.SetFloat("GananciaGuardada", multiplicadorGanancia);
        PlayerPrefs.Save();
        Debug.Log($"[Calibración] Umbral ({umbralGrito}) y Ganancia ({multiplicadorGanancia}) guardados para el juego.");
    }

    private void Start()
    {

        if (barraVolumen != null)
        {
            barraVolumen.minValue = 0;
            // Hacemos que el umbral de grito esté siempre al 80% de la barra.
            barraVolumen.maxValue = umbralGrito * 1.25f; 
        }

        if (Microphone.devices.Length > 0)
        {
            microfonoActual = BuscarMicrofonoVR();
            if (string.IsNullOrEmpty(microfonoActual))
            {
                microfonoActual = Microphone.devices[0]; // Fallback
            }
            
            if (!Microphone.IsRecording(microfonoActual))
            {
                clipMicrofono = Microphone.Start(microfonoActual, true, 999, 16000);
                yoInicieElMicro = true;
            }
            else
            {
                // El micrófono ya está siendo usado por otro script.
                // Si usamos Microphone.Start() ahora, le ROBAREMOS los datos de audio a ese script (clip vacío).
                InterrogationController ic = Object.FindAnyObjectByType<InterrogationController>();
                if (ic != null && ic.ClipGrabado != null)
                {
                    // ¡Encontramos el controlador principal! Usamos su clip de audio.
                    clipMicrofono = ic.ClipGrabado;
                    yoInicieElMicro = false;
                }
                else
                {
                    // Si no hay controlador, no nos queda otra que reiniciar.
                    clipMicrofono = Microphone.Start(microfonoActual, true, 999, 16000);
                    yoInicieElMicro = false;
                }
            }
        }
    }

    private string BuscarMicrofonoVR()
    {
        foreach (var device in Microphone.devices)
        {
            foreach (var palabraClave in palabrasClaveVR)
            {
                if (device.IndexOf(palabraClave, System.StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return device;
                }
            }
        }
        return null;
    }

    private void Update()
    {
        if (clipMicrofono == null || barraVolumen == null) return;

        int posMicrofono = Microphone.GetPosition(microfonoActual);
        
        int startPos = posMicrofono - 4000;
        if (startPos < 0)
        {
            int lengthFromEnd = -startPos;
            int lengthFromStart = 4000 - lengthFromEnd;

            float[] finalPart = new float[lengthFromEnd];
            clipMicrofono.GetData(finalPart, clipMicrofono.samples - lengthFromEnd);
            for (int i = 0; i < lengthFromEnd; i++) muestras[i] = finalPart[i];

            if (lengthFromStart > 0)
            {
                float[] startPart = new float[lengthFromStart];
                clipMicrofono.GetData(startPart, 0);
                for (int i = 0; i < lengthFromStart; i++) muestras[lengthFromEnd + i] = startPart[i];
            }
        }
        else
        {
            clipMicrofono.GetData(muestras, startPos);
        }

        float suma = 0;
        for (int i = 0; i < muestras.Length; i++)
        {
            suma += muestras[i] * muestras[i];
        }
        
        // Calculamos el RMS y le aplicamos el multiplicador de ganancia
        float rms = Mathf.Sqrt(suma / muestras.Length) * multiplicadorGanancia;

        // --- SISTEMA DE DIAGNÓSTICO EN CONSOLA ---
        if (rms > maxRmsConsole) maxRmsConsole = rms;
        consoleTimer += Time.deltaTime;
        if (consoleTimer >= 1f)
        {
            if (maxRmsConsole > 0.005f) // No loggear silencio absoluto
            {
                Debug.Log($"[MicVisualizer] Tu RMS máximo este último segundo fue: <b>{maxRmsConsole:F4}</b>. Ajusta tu UmbralGrito basándote en este número.");
            }
            maxRmsConsole = 0f;
            consoleTimer = 0f;
        }
        // ----------------------------------------

        // Movimiento asimétrico: Sube INSTANTÁNEAMENTE (sin lag), baja suavemente (fluidez visual)
        if (rms > barraVolumen.value)
        {
            barraVolumen.value = rms; 
        }
        else
        {
            barraVolumen.value = Mathf.Lerp(barraVolumen.value, rms, Time.deltaTime * 8f);
        }

        // Sistema de "Tiempo de Gracia" para el color rojo (evita parpadeos de 1 fotograma)
        if (rms >= umbralGrito)
        {
            tiempoRestanteRojo = 0.3f; // Mantener rojo durante al menos 300ms
        }

        if (fillArea != null)
        {
            if (tiempoRestanteRojo > 0f)
            {
                fillArea.color = colorGrito;
                tiempoRestanteRojo -= Time.deltaTime;
            }
            else
            {
                fillArea.color = colorNormal;
            }
        }
    }

    private void OnDisable()
    {
        // Al cerrar el menú, SOLO apagamos el micro si fuimos nosotros los que lo iniciamos.
        // Si el juego (InterrogationController) ya lo estaba usando, no se lo cortamos.
        if (yoInicieElMicro && !string.IsNullOrEmpty(microfonoActual) && Microphone.IsRecording(microfonoActual))
        {
            Microphone.End(microfonoActual);
            yoInicieElMicro = false;
        }
    }
}
