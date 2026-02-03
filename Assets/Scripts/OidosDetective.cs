using UnityEngine;
using Whisper;

public class OidosDetective : MonoBehaviour
{
    public WhisperManager whisper;
    public RespuestaIA cerebro;
    public KeyCode teclaHablar = KeyCode.Space;

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
            if (device.Contains("Auriculares") || device.Contains("Microphone Array")) 
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
    }

    private void Update()
    {
        if (Input.GetKeyDown(teclaHablar) && !grabando) ComenzarGrabacion();
        if (Input.GetKeyUp(teclaHablar) && grabando) TerminarGrabacion();
    }

    void ComenzarGrabacion()
    {
        grabando = true;
        clipGrabado = Microphone.Start(microfonoActual, false, 10, 16000);
        Debug.Log("Grabando...");
    }

    async void TerminarGrabacion()
    {
        grabando = false;
        Microphone.End(microfonoActual);
        Debug.Log("Procesando...");

        // ELEGIR ALGORITMO
        float volumenDetectado = 0f;
        if (usarMetodoPico)
        {
            volumenDetectado = CalcularVolumenPico(clipGrabado);
        }
        else
        {
            volumenDetectado = CalcularVolumenRMS(clipGrabado);
        }

        
        bool estaGritando = volumenDetectado > umbralGrito;

        // Muestra colores en la consola para verlo fácil
        string colorLog = estaGritando ? "<color=red>GRITO</color>" : "<color=green>NORMAL</color>";
        Debug.Log($" Volumen: {volumenDetectado.ToString("F4")} | Umbral: {umbralGrito} | Resultado: {colorLog}");

        var resultado = await whisper.GetTextAsync(clipGrabado);
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