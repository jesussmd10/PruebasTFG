using UnityEngine;
using System;
using System.IO;
using System.Text;
using System.Collections.Generic;

/// <summary>
/// Registra métricas de latencia de cada respuesta del LLM en un CSV.
/// Archivo: Application.persistentDataPath/metricas_llm.csv
/// </summary>
public class LatencyMetrics : MonoBehaviour
{
    public static LatencyMetrics Instance { get; private set; }

    private string rutaCSV_Dialogo;
    private string rutaCSV_Caso;
    private string rutaCSV_E2E;
    private readonly List<MetricaRespuesta> sesionActual = new List<MetricaRespuesta>();

    // Datos de la medición en curso (LLM)
    private System.Diagnostics.Stopwatch stopwatch;
    private float tiempoPrimerToken;
    private bool primerTokenRecibido;
    private int tokensContados;
    private string modeloActual;
    private string tipoActual;

    // Datos E2E
    private System.Diagnostics.Stopwatch sttStopwatch;
    private DateTime inicioE2E;
    private DateTime inicioTTS;
    private float ultimoSTT_ms = 0f;
    private float ultimoTTS_ms = 0f;
    private float ultimoE2E_ms = 0f;

    [System.Serializable]
    public class MetricaRespuesta
    {
        public string timestamp;
        public string modelo;
        public string tipo; // "dialogo" o "caso"
        public float tiempoPrimerToken_ms;
        public float tiempoTotal_ms;
        public float tiempoTotal_seg; 
        public int totalTokens;
        public float tokensPerSeg;
        public int longitudRespuesta;
        public bool tienePista;
        public bool exito_Formato; // Para medir robustez
    }

    private void Awake()
    {
        if (Instance != null) { Destroy(gameObject); return; }
        Instance = this;

        rutaCSV_Dialogo = Path.Combine(Application.persistentDataPath, "metricas_llm_dialogo.csv");
        rutaCSV_Caso = Path.Combine(Application.persistentDataPath, "metricas_llm_caso.csv");
        rutaCSV_E2E = Path.Combine(Application.persistentDataPath, "metricas_pipeline_e2e.csv");

        string cabeceraLLM = "timestamp;modelo;tipo;primerToken_ms;tiempoTotal_ms;tiempoTotal_seg;totalTokens;tokens_seg;longitudRespuesta;tienePista;exito_Formato\n";
        string cabeceraE2E = "timestamp;latencia_STT_ms;latencia_LLM_PrimerToken_ms;latencia_TTS_ms;latencia_E2E_Total_ms;VRAM_LMStudio_GB;VRAM_UnityVR_GB\n";

        // Crear CSV con cabeceras si no existe
        if (!File.Exists(rutaCSV_Dialogo)) File.WriteAllText(rutaCSV_Dialogo, cabeceraLLM, Encoding.UTF8);
        if (!File.Exists(rutaCSV_Caso)) File.WriteAllText(rutaCSV_Caso, cabeceraLLM, Encoding.UTF8);
        if (!File.Exists(rutaCSV_E2E)) File.WriteAllText(rutaCSV_E2E, cabeceraE2E, Encoding.UTF8);

        Debug.Log($"[Métricas] CSVs en: {Application.persistentDataPath}");
    }

    // ========================================
    // MÉTRICAS PIPELINE E2E
    // ========================================

    public void MarcarFinGrabacionMicrofono() 
    { 
        inicioE2E = DateTime.Now; 
    }

    public void IniciarMedicionSTT()
    {
        sttStopwatch = new System.Diagnostics.Stopwatch();
        sttStopwatch.Start();
    }

    public void FinalizarMedicionSTT()
    {
        if (sttStopwatch != null)
        {
            sttStopwatch.Stop();
            ultimoSTT_ms = (float)sttStopwatch.Elapsed.TotalMilliseconds;
        }
    }

    public void MarcarInicioGeneracionTTS() 
    { 
        inicioTTS = DateTime.Now; 
    }

    public void MarcarInicioReproduccionAudio()
    {
        if (inicioE2E != default)
        {
            ultimoE2E_ms = (float)(DateTime.Now - inicioE2E).TotalMilliseconds;
            inicioE2E = default; // Reiniciar
        }

        if (inicioTTS != default)
        {
            ultimoTTS_ms = (float)(DateTime.Now - inicioTTS).TotalMilliseconds;
            inicioTTS = default;
        }

        GuardarMetricaE2E();
    }

    private void GuardarMetricaE2E()
    {
        try
        {
            // Nota: Dejamos las dos últimas columnas vacías para que el usuario escriba la VRAM a mano
            string linea = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss};{ultimoSTT_ms:F0};{tiempoPrimerToken:F0};{ultimoTTS_ms:F0};{ultimoE2E_ms:F0};;\n";
            File.AppendAllText(rutaCSV_E2E, linea, Encoding.UTF8);
            
            
            // Reset for next turn
            ultimoSTT_ms = 0f;
            ultimoTTS_ms = 0f;
            ultimoE2E_ms = 0f;
        }
        catch (Exception ex)
        {
            Debug.LogError($"[Métricas E2E] Error escribiendo CSV E2E: {ex.Message}");
        }
    }

    // ========================================
    // MÉTRICAS LLM
    // ========================================

    public void IniciarMedicion(string nombreModelo, string tipo = "dialogo")
    {
        modeloActual = nombreModelo;
        tipoActual = tipo;
        
        if (stopwatch == null) stopwatch = new System.Diagnostics.Stopwatch();
        stopwatch.Restart();
        
        tiempoPrimerToken = 0f;
        primerTokenRecibido = false;
        tokensContados = 0;
    }

    public void RegistrarToken()
    {
        tokensContados++;
        if (!primerTokenRecibido)
        {
            primerTokenRecibido = true;
            if (stopwatch != null) tiempoPrimerToken = (float)stopwatch.Elapsed.TotalMilliseconds;
        }
    }

    public void FinalizarMedicion(string respuestaCompleta, bool tienePista, bool exitoFormato = true, int exactTokens = -1)
    {
        if (stopwatch != null) stopwatch.Stop();
        float tiempoTotal = stopwatch != null ? (float)stopwatch.Elapsed.TotalMilliseconds : 0f;

        if (!primerTokenRecibido)
        {
            tiempoPrimerToken = tiempoTotal;
            if (exactTokens > 0)
            {
                tokensContados = exactTokens;
            }
            else
            {
                tokensContados = Mathf.RoundToInt(respuestaCompleta.Split(' ').Length * 1.3f);
            }
        }
        else if (exactTokens > 0)
        {
            tokensContados = exactTokens;
        }

        float tokensPerSeg = tiempoTotal > 0 ? (tokensContados / (tiempoTotal / 1000f)) : 0;

        var metrica = new MetricaRespuesta
        {
            timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
            modelo = modeloActual,
            tipo = tipoActual,
            tiempoPrimerToken_ms = tiempoPrimerToken,
            tiempoTotal_ms = tiempoTotal,
            tiempoTotal_seg = tiempoTotal / 1000f,
            totalTokens = tokensContados,
            tokensPerSeg = tokensPerSeg,
            longitudRespuesta = respuestaCompleta.Length,
            tienePista = tienePista,
            exito_Formato = exitoFormato
        };

        sesionActual.Add(metrica);
        GuardarMetricaCSV(metrica);

        
    }

    private void GuardarMetricaCSV(MetricaRespuesta m)
    {
        try
        {
            string ruta = m.tipo.ToLower() == "caso" ? rutaCSV_Caso : rutaCSV_Dialogo;
            string linea = $"{m.timestamp};{m.modelo};{m.tipo};{m.tiempoPrimerToken_ms:F0};{m.tiempoTotal_ms:F0};{m.tiempoTotal_seg:F2};{m.totalTokens};{m.tokensPerSeg:F1};{m.longitudRespuesta};{m.tienePista};{m.exito_Formato}\n";
            File.AppendAllText(ruta, linea, Encoding.UTF8);
        }
        catch (Exception ex)
        {
            Debug.LogError($"[Métricas LLM] Error escribiendo CSV: {ex.Message}");
        }
    }

    public string ObtenerResumenSesion()
    {
        if (sesionActual.Count == 0) return "Sin datos aún.";

        var sb = new StringBuilder();
        sb.AppendLine("=== RESUMEN SESIÓN ===");

        var porModelo = new Dictionary<string, List<MetricaRespuesta>>();
        foreach (var m in sesionActual)
        {
            string clave = $"{m.modelo} ({m.tipo})";
            if (!porModelo.ContainsKey(clave))
                porModelo[clave] = new List<MetricaRespuesta>();
            porModelo[clave].Add(m);
        }

        foreach (var kvp in porModelo)
        {
            float sumPrimerToken = 0, sumTotal = 0, sumTPS = 0;
            int count = kvp.Value.Count;

            foreach (var m in kvp.Value)
            {
                sumPrimerToken += m.tiempoPrimerToken_ms;
                sumTotal += m.tiempoTotal_ms;
                sumTPS += m.tokensPerSeg;
            }

            sb.AppendLine($"Modelo: {kvp.Key}");
            sb.AppendLine($"  Respuestas: {count}");
            sb.AppendLine($"  Avg 1er token: {sumPrimerToken / count:F0}ms");
            sb.AppendLine($"  Avg total: {sumTotal / count:F0}ms");
            sb.AppendLine($"  Avg tokens/s: {sumTPS / count:F1}");
        }

        return sb.ToString();
    }
}
