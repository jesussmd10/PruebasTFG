using UnityEngine;

public class CaminarHaciaSilla : MonoBehaviour
{
    public Transform silla;      
    public Animator animador;    
    public float velocidad = 2.0f;
    
    private bool yaSeHaSentado = false;

    void Update()
    {
        if (yaSeHaSentado) return; // Si ya está sentado, no hacer nada más

        // Calcular distancia a la silla
        float distancia = Vector3.Distance(transform.position, silla.position);

        // Si está lejos (> 0.1 metros), seguir caminando
        if (distancia > 0.1f)
        {
            // Moverse hacia la silla
            transform.position = Vector3.MoveTowards(transform.position, silla.position, velocidad * Time.deltaTime);
            
            // Mirar hacia la silla 
            transform.LookAt(silla);
        }
        else
        {
            Debug.Log(" Ya estoy en la silla.");
            Sentarse();
        }
    }

    void Sentarse()
    {
        yaSeHaSentado = true;
        transform.position = silla.position;
        transform.rotation = silla.rotation;

        // Activar la animación
        animador.SetTrigger("A_SENTARSE");
        Debug.Log("Llegué. Me siento.");
    }
}