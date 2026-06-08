using UnityEngine;
using UnityEngine.AI;

public class NPCMovement : MonoBehaviour
{
    [SerializeField] private Transform silla;
    [SerializeField] private CharacterAnimator characterAnimator;
    [SerializeField] private float velocidad = 2.0f;
    [SerializeField] private float distanciaMinima = 0.1f;

    [Header("Ajustes de la Silla")]
    [Tooltip("Ajusta esto si el personaje queda flotando o atravesando la silla")]
    [SerializeField] private Vector3 offsetPosicionSilla = Vector3.zero;
    [Tooltip("Ajusta esto (ej: Y=180) si el personaje mira al lado contrario al sentarse")]
    [SerializeField] private Vector3 offsetRotacionSilla = Vector3.zero;

    private bool yaSeHaSentado = false;
    public bool YaEstaSentado => yaSeHaSentado;

    public bool EstaLlegando { get; private set; } = false;

    private Vector3 posicionInicial;
    private Quaternion rotacionInicial;
    private bool posicionGuardada = false;
    
    private NavMeshAgent navAgent;

    private void Awake()
    {
        navAgent = GetComponent<NavMeshAgent>();
        GuardarPosicion();
    }

    private void Start()
    {
        GuardarPosicion();
    }

    public void GuardarPosicion()
    {
        if (!posicionGuardada)
        {
            posicionInicial = transform.position;
            rotacionInicial = transform.rotation;
            posicionGuardada = true;
        }
    }

    public void ReiniciarMovimiento()
    {
        if (navAgent != null) navAgent.enabled = false; // Apagar para teleportar

        GuardarPosicion();
        
        transform.position = posicionInicial;
        transform.rotation = rotacionInicial;
        yaSeHaSentado = false;
        EstaLlegando = false;
        
        if (navAgent != null) navAgent.enabled = true; // Volver a encender

        if (characterAnimator != null)
        {
            // Forzamos al animador a volver a su estado base (caminar)
            Animator anim = characterAnimator.GetComponent<Animator>();
            if (anim != null) anim.Rebind();
        }
    }

    private void Update()
    {
        if (yaSeHaSentado) return;

        if (silla == null)
        {
            Debug.LogError(" Silla no asignada");
            return;
        }

        // Ignorar la diferencia de altura para calcular la distancia de llegada
        Vector3 posicionPlanaNPC = new Vector3(transform.position.x, 0, transform.position.z);
        Vector3 posicionPlanaSilla = new Vector3(silla.position.x, 0, silla.position.z);
        
        float distancia = Vector3.Distance(posicionPlanaNPC, posicionPlanaSilla);

        if (distancia < 1.5f)
        {
            EstaLlegando = true;
        }

        if (distancia > distanciaMinima)
        {
            // Usar NavMesh solo si está lejos (a más de 1.5 metros). 
            // Esto evita que intente rodear la silla o la mesa si el NavMesh tiene un hueco ahí.
            if (navAgent != null && navAgent.isOnNavMesh && distancia > 1.5f)
            {
                navAgent.speed = velocidad;
                navAgent.SetDestination(silla.position);
            }
            else
            {
                if (navAgent != null) navAgent.enabled = false; // Apagar agente en el tramo final

                // Aproximación final en línea recta perfecta
                transform.position = Vector3.MoveTowards(
                    transform.position,
                    silla.position,
                    velocidad * Time.deltaTime
                );

                Vector3 direccionSilla = silla.position - transform.position;
                direccionSilla.y = 0; 
                
                // Congelamos la rotación cuando está muy cerca para que no dé ninguna vuelta ni baile
                if (direccionSilla.sqrMagnitude > 0.05f)
                {
                    transform.rotation = Quaternion.LookRotation(direccionSilla);
                }
            }
        }
        else
        {
            if (navAgent != null) navAgent.enabled = false; 
            Sentarse();
        }
    }

    private void Sentarse()
    {
        yaSeHaSentado = true;
        EstaLlegando = false; 
        
        // Aplicar la posición exacta de la silla más el ajuste (offset)
        transform.position = silla.position + silla.TransformDirection(offsetPosicionSilla);
        // Aplicar la rotación de la silla más el ajuste
        transform.rotation = silla.rotation * Quaternion.Euler(offsetRotacionSilla);

        if (characterAnimator != null)
        {   
            characterAnimator.Sentarse();
        }

    }
}