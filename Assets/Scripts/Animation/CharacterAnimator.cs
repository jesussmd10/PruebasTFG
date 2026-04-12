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
                    animador.SetTrigger("IDLE");
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