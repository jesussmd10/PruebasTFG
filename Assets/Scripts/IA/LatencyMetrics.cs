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

    private string rutaCSV;
    private readonly List<MetricaRespuesta> sesionActual = new List<MetricaRespuesta>();

    // Datos de la medición en curso
    private float tiempoInicio;
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
        public int totalTokens;
        public float tokensPerSeg;
        public int longitudRespuesta;
        public bool tienePista;
    }

    private void Awake()
    {
        if (Instance != null) { Destroy(gameObject); return; }
        Instance = this;

        rutaCSV = Path.Combine(Application.persistentDataPath, "metricas_llm.csv");

        // Crear CSV con cabeceras si no existe
        if (!File.Exists(rutaCSV))
        {
            File.WriteAllText(rutaCSV,
                "timestamp,modelo,tipo,primerToken_ms,tiempoTotal_ms,totalTokens,tokens_seg,longitudRespuesta,tienePista\n",
                Encoding.UTF8);
        }

        Debug.Log($"[Métricas] CSV en: {rutaCSV}");
    }

    /// <summary>
    /// Llamar cuando se envía una petición al LLM.
    /// </summary>
    public void IniciarMedicion(string nombreModelo, string tipo = "dialogo")
    {
        modeloActual = nombreModelo;
        tipoActual = tipo;
        tiempoInicio = Time.realtimeSinceStartup;
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
            tiempoPrimerToken = (Time.realtimeSinceStartup - tiempoInicio) * 1000f;
        }
    }

    /// <summary>
    /// Llamar cuando la respuesta está completa. Guarda la métrica al CSV.
    /// </summary>
    public void FinalizarMedicion(string respuestaCompleta, bool tienePista, int exactTokens = -1)
    {
        float tiempoTotal = (Time.realtimeSinceStartup - tiempoInicio) * 1000f;

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
            string linea = $"{m.timestamp},{m.modelo},{m.tipo},{m.tiempoPrimerToken_ms:F0},{m.tiempoTotal_ms:F0},{m.totalTokens},{m.tokensPerSeg:F1},{m.longitudRespuesta},{m.tienePista}\n";
            File.AppendAllText(rutaCSV, linea, Encoding.UTF8);
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

        // Agrupar por modelo
        var porModelo = new Dictionary<string, List<MetricaRespuesta>>();
        foreach (var m in sesionActual)
        {
            if (!porModelo.ContainsKey(m.modelo))
                porModelo[m.modelo] = new List<MetricaRespuesta>();
            porModelo[m.modelo].Add(m);
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
