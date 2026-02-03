using UnityEngine;

public class CuerpoSospechoso : MonoBehaviour
{
    public Animator animador; 

    // Anima a Alex poniéndose nervioso
    public void PonerNervioso()
    {
        if(animador != null)
        {
            animador.SetTrigger("NERVIOSO"); //trigger para la animación de nerviosismo
            Debug.Log("Alex se pone nervioso (Animación)"); 
        }
    }

    // Anima a Alex volviendo a la calma
    public void Calmar()
    {
        if(animador != null)
        {
            animador.SetTrigger("IDLE"); // trigger para volver a la animación de calma
            Debug.Log("Alex se calma");
        }
    }

    // Anima a Alex gesticulando mientras habla
    public void GestosHablar()
    {
        if(animador != null) 
        {
            animador.SetTrigger("HABLAR");
        }
    }
}