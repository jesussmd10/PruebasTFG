using UnityEngine;

public class GameContext : MonoBehaviour
{
    public static GameContext Instance { get; private set; }

    [SerializeField] private float tiempoPartida = 300f;
    private bool esCulpable;
    private string pistasDescubiertas = "";
    private bool juegoTerminado = false;

    public bool EsCulpable => esCulpable;
    public float TiempoPartida => tiempoPartida;
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
        tiempoPartida -= cantidad;
        if (tiempoPartida <= 0)
        {
            TerminarJuego();
        }
    }

    public void TerminarJuego()
    {
        juegoTerminado = true;
        EventSystem.OnInterrogatorioTerminado.Invoke();
    }
}
