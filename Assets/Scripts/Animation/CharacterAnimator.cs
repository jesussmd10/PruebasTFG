using UnityEngine;


public class CharacterAnimator : MonoBehaviour
{
    [SerializeField] private Animator animador;

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

        switch (emotion)
        {
            case EmotionState.Nervioso:
                animador.SetTrigger("NERVIOSO");
                Debug.Log(" Alex se pone nervioso");
                break;

            case EmotionState.Calmado:
                animador.SetTrigger("IDLE");
                Debug.Log("Alex se calma");
                break;

            case EmotionState.Hablando:
                animador.SetTrigger("HABLAR");
                break;
        }
    }

    public void Sentarse()
    {
        if (animador != null)
        {
            animador.SetTrigger("A_SENTARSE");
            Debug.Log("Alex se sienta");
        }
    }
}
