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
    private readonly List<MetricaRespuesta> sesionActual = new List<MetricaRespuesta>();

    // Datos de la medición en curso
    private System.Diagnostics.Stopwatch stopwatch;
    private float tiempoPrimerToken;
    private bool primerTokenRecibido;
    private int tokensContados;
    private string modeloActual;
    private string tipoActual;

    [System.Serializable]
    public class MetricaRespuesta
    {
        public string timestamp;
        public string modelo;
        public string tipo; // "dialogo" o "caso"
        public float tiempoPrimerToken_ms;
        public float tiempoTotal_ms;
        public float tiempoTotal_seg; // <- Nueva columna para facilitar métricas
        public int totalTokens;
        public float tokensPerSeg;
        public int longitudRespuesta;
        public bool tienePista;
    }

    private void Awake()
    {
        if (Instance != null) { Destroy(gameObject); return; }
        Instance = this;

        rutaCSV_Dialogo = Path.Combine(Application.persistentDataPath, "metricas_llm_dialogo.csv");
        rutaCSV_Caso = Path.Combine(Application.persistentDataPath, "metricas_llm_caso.csv");

        string cabecera = "timestamp;modelo;tipo;primerToken_ms;tiempoTotal_ms;tiempoTotal_seg;totalTokens;tokens_seg;longitudRespuesta;tienePista\n";

        // Crear CSV con cabeceras si no existe
        if (!File.Exists(rutaCSV_Dialogo))
        {
            File.WriteAllText(rutaCSV_Dialogo, cabecera, Encoding.UTF8);
        }
        
        if (!File.Exists(rutaCSV_Caso))
        {
            File.WriteAllText(rutaCSV_Caso, cabecera, Encoding.UTF8);
        }

        Debug.Log($"[Métricas] CSVs en: {Application.persistentDataPath}");
    }

    /// <summary>
    /// Llamar cuando se envía una petición al LLM.
    /// </summary>
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

    /// <summary>
    /// Llamar cada vez que llega un token del streaming.
    /// </summary>
    public void RegistrarToken()
    {
        tokensContados++;
        if (!primerTokenRecibido)
        {
            primerTokenRecibido = true;
            if (stopwatch != null) tiempoPrimerToken = (float)stopwatch.Elapsed.TotalMilliseconds;
        }
    }

    /// <summary>
    /// Llamar cuando la respuesta está completa. Guarda la métrica al CSV.
    /// </summary>
    public void FinalizarMedicion(string respuestaCompleta, bool tienePista, int exactTokens = -1)
    {
        if (stopwatch != null) stopwatch.Stop();
        float tiempoTotal = stopwatch != null ? (float)stopwatch.Elapsed.TotalMilliseconds : 0f;

        // Si no hubo streaming (respuesta de golpe), primer token = tiempo total
        if (!primerTokenRecibido)
        {
            tiempoPrimerToken = tiempoTotal;
            // Usar tokens exactos si la API los proveyó, si no estimarlos
            if (exactTokens > 0)
            {
                tokensContados = exactTokens;
            }
            else
            {
                // Estimar tokens por palabras (aprox 1.3 tokens/palabra en español)
                tokensContados = Mathf.RoundToInt(respuestaCompleta.Split(' ').Length * 1.3f);
            }
        }
        else if (exactTokens > 0)
        {
            // Si hubo streaming pero nos pasan el count exacto final, usarlo
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
            tienePista = tienePista
        };

        sesionActual.Add(metrica);
        GuardarMetricaCSV(metrica);

        Debug.Log($"[Métricas] {modeloActual} ({tipoActual}) | 1er token: {tiempoPrimerToken:F0}ms | Total: {tiempoTotal:F0}ms | {tokensContados} tokens | {tokensPerSeg:F1} t/s");
    }

    private void GuardarMetricaCSV(MetricaRespuesta m)
    {
        try
        {
            string ruta = m.tipo.ToLower() == "caso" ? rutaCSV_Caso : rutaCSV_Dialogo;
            string linea = $"{m.timestamp};{m.modelo};{m.tipo};{m.tiempoPrimerToken_ms:F0};{m.tiempoTotal_ms:F0};{m.tiempoTotal_seg:F2};{m.totalTokens};{m.tokensPerSeg:F1};{m.longitudRespuesta};{m.tienePista}\n";
            File.AppendAllText(ruta, linea, Encoding.UTF8);
        }
        catch (Exception ex)
        {
            Debug.LogError($"[Métricas] Error escribiendo CSV: {ex.Message}");
        }
    }

    /// <summary>
    /// Devuelve un resumen de las métricas de la sesión actual.
    /// </summary>
    public string ObtenerResumenSesion()
    {
        if (sesionActual.Count == 0) return "Sin datos aún.";

        var sb = new StringBuilder();
        sb.AppendLine("=== RESUMEN SESIÓN ===");

        // Agrupar por modelo y tipo
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
