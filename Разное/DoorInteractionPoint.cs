using UnityEngine;

/// <summary>
/// Точка взаимодействия двери
/// Требует InteractableSettings на том же объекте для отображения UI
/// </summary>
[RequireComponent(typeof(InteractableSettings))]
public class DoorInteractionPoint : MonoBehaviour, IInteractable
{
    [Header("Setup")]
    [SerializeField] private DoorSystem door;
    [SerializeField] private DoorSystem.InteractionPoint pointType;

    private InteractableSettings settings;

    private void Awake()
    {
        if (door == null)
            door = GetComponentInParent<DoorSystem>();

        settings = GetComponent<InteractableSettings>();
    }

    public void Interact()
    {
        if (door != null)
            door.Interact(pointType);
    }

    public void StopInteract() { }

    // НОВОЕ: Методы для AI
    public bool IsOpen => door != null && door.IsOpen;
    public bool IsClosed => door != null && door.IsClosed;
    
    public void OnAIInteract(GameObject aiObject)
    {
        if (door != null)
            door.OnAIInteract(aiObject);
    }

    /// <summary>
    /// НОВОЕ: AI закрывает дверь для использования как укрытие
    /// </summary>
    public void OnAIClose(GameObject aiObject)
    {
        if (door != null)
            door.OnAIClose(aiObject);
    }

#if UNITY_EDITOR
    private void OnDrawGizmosSelected()
    {
        Gizmos.color = pointType switch
        {
            DoorSystem.InteractionPoint.A_Pull => Color.green,
            DoorSystem.InteractionPoint.B_Push => Color.blue,
            DoorSystem.InteractionPoint.C_Kick => Color.red,
            _ => Color.white
        };
        Gizmos.DrawWireSphere(transform.position, 0.15f);
    }
#endif
}
