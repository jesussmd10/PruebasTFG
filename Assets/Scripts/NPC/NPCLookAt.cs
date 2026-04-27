using UnityEngine;

[RequireComponent(typeof(Animator))]
public class NPCLookAt : MonoBehaviour
{
    private Animator animator;

    [Header("Objetivo a mirar")]
    [Tooltip("Arrastra aquí la cámara de VR del jugador")]
    public Transform objetivoMirada;

    [Header("Límites de Visión")]
    [Tooltip("Distancia máxima a la que el NPC te seguirá con la mirada (en metros)")]
    public float distanciaMaxima = 15f; 
    [Tooltip("Ángulo máximo del cuello. Si te pasas de aquí, dejará de mirarte.")]
    public float anguloMaximo = 80f; // <--- AQUÍ ESTÁ LA MAGIA ANTI-EXORCISTA

    [Header("Ajustes de Naturalidad (0 a 1)")]
    [Range(0f, 1f)] public float pesoGlobal = 1.0f;
    [Range(0f, 1f)] public float pesoCuerpo = 0.1f;  
    [Range(0f, 1f)] public float pesoCabeza = 0.8f;  
    [Range(0f, 1f)] public float pesoOjos = 1.0f;
    [Range(0f, 1f)] [Tooltip("1 = Rígido (No se parte el cuello), 0 = Goma (Niña del exorcista)")] 
    public float pesoClamp = 1.0f; // <--- ESTO BLOQUEA LAS ROTACIONES EXTREMAS

    [Header("Transición Suave")]
    public float velocidadGiro = 2.0f;
    private float pesoActual = 0f;

    void Start()
    {
        animator = GetComponent<Animator>();
    }

    private void OnAnimatorIK(int layerIndex)
    {
        if (animator == null || objetivoMirada == null) return;

        // 1. Calculamos la distancia
        float distancia = Vector3.Distance(transform.position, objetivoMirada.position);
        
        // 2. Calculamos el ángulo (¿Estás delante o detrás de él?)
        Vector3 direccionHaciaTi = objetivoMirada.position - transform.position;
        // Ignoramos la altura para que el ángulo sea solo de giro (izquierda/derecha)
        direccionHaciaTi.y = 0; 
        Vector3 haciaAdelante = transform.forward;
        haciaAdelante.y = 0;
        
        float angulo = Vector3.Angle(haciaAdelante, direccionHaciaTi);
        
        // 3. Solo te mira si estás cerca Y dentro de su ángulo de visión frontal
        float pesoDeseado = (distancia < distanciaMaxima && angulo < anguloMaximo) ? pesoGlobal : 0f; 

        // Suavizamos el movimiento
        pesoActual = Mathf.Lerp(pesoActual, pesoDeseado, Time.deltaTime * velocidadGiro);

        // Aplicamos los pesos (usando el pesoClamp en vez de tu 0.5f fijo)
        animator.SetLookAtWeight(pesoActual, pesoCuerpo, pesoCabeza, pesoOjos, pesoClamp);
        animator.SetLookAtPosition(objetivoMirada.position);
    }
}