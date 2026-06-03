using UnityEngine;

public class CharacterAnimator : MonoBehaviour
{
    [SerializeField] private Animator animador;
    private EmotionState estadoActual = EmotionState.Calmado;

    private void OnEnable()
    {
        EventSystem.OnEmotionChanged.AddListener(CambiarEmocion);
    }

    private void OnDisable()
    {
        EventSystem.OnEmotionChanged.RemoveListener(CambiarEmocion);
    }

    private void CambiarEmocion(EmotionState emotion)
    {
        if (animador == null) return;

       
        if (emotion == EmotionState.Hablando)
        {
            animador.SetTrigger("HABLAR");
        }
        else
        {
            
            animador.ResetTrigger("NERVIOSO");
            animador.ResetTrigger("IDLE");
            animador.ResetTrigger("HABLAR");
            animador.ResetTrigger("NEGACION");

            switch (emotion)
            {
                case EmotionState.Nervioso:
                    animador.SetTrigger("NERVIOSO");
                    break;
                case EmotionState.Calmado:
                    // Solo forzamos el crossfade a Quieto si estábamos hablando.
                    // Si estamos al principio del juego (llegando a la mesa), queremos
                    // respetar la animación base/nerviosa por defecto en lugar de cortarla.
                    if (estadoActual == EmotionState.Hablando)
                    {
                        // Transición suave de 0.25s a la animación base de la capa 1
                        animador.CrossFade("Quieto", 0.1f, 1);
                    }
                    animador.SetTrigger("IDLE"); // Mantenemos el trigger por si otras capas lo necesitan
                    break;
                case EmotionState.Negando:
                    animador.SetTrigger("NEGACION");
                    Debug.Log("Alex niega con el dedo/cabeza y se prepara para hablar");
                    break;
            }
        }
        
        estadoActual = emotion;
    }

    public void Sentarse()
    {
        if (animador != null) animador.SetTrigger("A_SENTARSE");
    }
}