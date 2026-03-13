using UnityEngine;


public class NPCMovement : MonoBehaviour
{
    [SerializeField] private Transform silla;
    [SerializeField] private CharacterAnimator characterAnimator;
    [SerializeField] private float velocidad = 2.0f;
    [SerializeField] private float distanciaMinima = 0.1f;

    private bool yaSeHaSentado = false;

    private void Update()
    {
        if (yaSeHaSentado) return;

        if (silla == null)
        {
            Debug.LogError("❌ Silla no asignada");
            return;
        }

        float distancia = Vector3.Distance(transform.position, silla.position);

        if (distancia > distanciaMinima)
        {
            // Moverse hacia la silla
            transform.position = Vector3.MoveTowards(
                transform.position,
                silla.position,
                velocidad * Time.deltaTime
            );

            // Mirar hacia la silla
            transform.LookAt(silla);
        }
        else
        {
            Sentarse();
        }
    }

    private void Sentarse()
    {
        yaSeHaSentado = true;
        transform.position = silla.position;
        transform.rotation = silla.rotation;

        if (characterAnimator != null)
        {
            characterAnimator.Sentarse();
        }

        Debug.Log("✅ Llegué. Me siento.");
    }
}
