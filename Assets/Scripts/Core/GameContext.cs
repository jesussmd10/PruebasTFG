using UnityEngine;

public class GameContext : MonoBehaviour
{
    public static GameContext Instance { get; private set; }

    [SerializeField] private float tiempoPartida = 300f;
    private float tiempoRestante;
    private bool esCulpable;
    private string pistasDescubiertas = "";
    private bool juegoTerminado = false;

    // NUEVO PARA CASOS ALEATORIOS
    public class CasoDelito
    {
        public string ID;
        public string TituloFolio;
        public string DescripcionFolio;
        public string DescripcionPrompt;
    }

    private CasoDelito delitoActual;
    public CasoDelito DelitoActual => delitoActual;

    private readonly CasoDelito[] casosDisponibles = new CasoDelito[]
    {
        new CasoDelito { ID = "042", TituloFolio = "ROBO EN JOYERÍA", DescripcionFolio = "Atraco a mano armada en la joyería central y robo de diamantes.", DescripcionPrompt = "un atraco a mano armada en la joyería del centro donde se robaron diamantes" },
        new CasoDelito { ID = "087", TituloFolio = "ASESINATO", DescripcionFolio = "Homicidio en primer grado en el callejón trasero del club.", DescripcionPrompt = "el brutal asesinato de una persona en un callejón oscuro" },
        new CasoDelito { ID = "104", TituloFolio = "SECUESTRO", DescripcionFolio = "Secuestro y desaparición forzada del miembro de una familia rica.", DescripcionPrompt = "el secuestro a plena luz del día de una persona importante pidiendo un inmenso rescate" },
        new CasoDelito { ID = "019", TituloFolio = "INCENDIO PROVOCADO", DescripcionFolio = "Incendio intencionado en un edificio comercial de la ciudad.", DescripcionPrompt = "haber provocado intencionadamente un incendio masivo que destruyó un edificio comercial" },
        new CasoDelito { ID = "055", TituloFolio = "AGRESIÓN GRAVE", DescripcionFolio = "Asalto con violencia extrema a un transeúnte la pasada noche.", DescripcionPrompt = "haber atacado violentamente y agredido a una joven en el parque de madrugada" },
        new CasoDelito { ID = "092", TituloFolio = "TRÁFICO DE DROGAS Y ARMAS", DescripcionFolio = "Venta y distribución ilegal de armamento militar modificado.", DescripcionPrompt = "vender armas de fuego ilegales a bandas criminales organizadas desde el maletero de tu coche" }
    };
    

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
        delitoActual = casosDisponibles[Random.Range(0, casosDisponibles.Length)];
        Debug.Log($"⏱️ Tiempo de partida inicializado: {tiempoPartida}s ({Mathf.FloorToInt(tiempoPartida/60)}:{Mathf.FloorToInt(tiempoPartida%60):00})");
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
