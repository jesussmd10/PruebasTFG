using UnityEngine;

public class GameContext : MonoBehaviour
{
    public static GameContext Instance { get; private set; }

    [SerializeField] private float tiempoPartida = 300f;
    private float tiempoRestante;
    private bool esCulpable;
    private string pistasDescubiertas = "";
    private bool juegoTerminado = false;

    // Clase de caso con coartada y actitud incluidas
    public class CasoDelito
    {
        public string ID;
        public string TituloFolio;
        public string DescripcionFolio;
        public string DescripcionPrompt;
        public string Coartada;
        public string Actitud;
    }

    private CasoDelito delitoActual;
    public CasoDelito DelitoActual => delitoActual;

    public bool EsCulpable => esCulpable;
    public float TiempoPartida => tiempoPartida;
    public float TiempoRestante => tiempoRestante;
    public bool JuegoTerminado => juegoTerminado;
    public string PistasDescubiertas => pistasDescubiertas;

    private void Awake()
    {
        if (Instance != null)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        tiempoRestante = tiempoPartida;
        // El caso se asigna después por CaseGenerator o InterrogationManager
        Debug.Log($"Tiempo de partida inicializado: {tiempoPartida}s ({Mathf.FloorToInt(tiempoPartida/60)}:{Mathf.FloorToInt(tiempoPartida%60):00})");
    }

    /// <summary>
    /// Establece el caso generado por la IA o el fallback.
    /// </summary>
    public void EstablecerCaso(CasoDelito caso)
    {
        delitoActual = caso;
        Debug.Log($"[GameContext] Caso establecido: {caso.ID} - {caso.TituloFolio}");
    }

    public void ConfigurarCulpabilidad(bool culpable)
    {
        esCulpable = culpable;
    }

    public void AñadirPista(string pista)
    {
        pistasDescubiertas += "- " + pista + "\n";
        EventSystem.OnPistaDescubierta.Invoke(pista);
    }

    public void ReducirTiempo(float cantidad)
    {
        tiempoRestante -= cantidad;
        if (tiempoRestante <= 0)
        {
            tiempoRestante = 0;
            TerminarJuego();
        }
    }

    public void TerminarJuego()
    {
        juegoTerminado = true;
        EventSystem.OnInterrogatorioTerminado.Invoke();
    }
}
