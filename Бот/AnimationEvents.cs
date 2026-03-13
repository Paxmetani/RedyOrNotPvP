using UnityEngine;

public class AnimationEvents : MonoBehaviour
{
    public AIArrestModule AIArrestModule;

    private void Awake()
    {

    }

    // Вызывается в конце анимации ареста (Animation Event)
    public void OnArrestAnimationComplete()
    {
        AIArrestModule.CompleteArrest();
    }

    // Пример: ивент для шага (можно использовать в клипах ходьбы)
    public void PlayFootstep()
    {

    }

    // Пример: ивент для удара/выстрела, если нужно
    public void PlayActionSound(string soundId)
    {

    }
}
