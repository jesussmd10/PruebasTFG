using UnityEngine;

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

    // ---> NUEVO: Creamos esta variable para que la IA sepa que estamos aparcando <---
    public bool EstaLlegando { get; private set; } = false;

    private Vector3 posicionInicial;
    private Quaternion rotacionInicial;
    private bool posicionGuardada = false;

    private void Awake()
    {
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
        GuardarPosicion();
        
        transform.position = posicionInicial;
        transform.rotation = rotacionInicial;
        yaSeHaSentado = false;
        EstaLlegando = false;
        
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

        // ---> AQUÍ LO PONES: Justo después de saber a qué distancia estamos <---
        if (distancia < 0.8f)
        {
            EstaLlegando = true; // Avisamos de que estamos a menos de 80cm
        }

        if (distancia > distanciaMinima)
        {
            // Moverse hacia la silla
            transform.position = Vector3.MoveTowards(
                transform.position,
                silla.position,
                velocidad * Time.deltaTime
            );

            // Mirar hacia la silla (Solo en el eje Y para que no se incline hacia el suelo)
            Vector3 direccionSilla = silla.position - transform.position;
            direccionSilla.y = 0; // Mantener la rotación plana
            if (direccionSilla.sqrMagnitude > 0.001f)
            {
                transform.rotation = Quaternion.LookRotation(direccionSilla);
            }
        }
        else
        {
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

        Debug.Log("Llegué. Me siento.");
    }
}