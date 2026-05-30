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
            microfonoActual = Microphone.devices[0];
            clipMicrofono = Microphone.Start(microfonoActual, true, 999, 16000);
        }
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
        // Al cerrar el menú, apagamos el micro para que el juego pueda usarlo luego
        if (Microphone.IsRecording(microfonoActual))
        {
            Microphone.End(microfonoActual);
        }
    }
}
