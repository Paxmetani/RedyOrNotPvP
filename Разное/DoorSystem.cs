using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// Тактическая дверь (Ready Or Not style)
/// Полностью анимационная система — стабильная, без HingeJoint
/// Rigidbody kinematic для коллизий
/// </summary>
public class DoorSystem : MonoBehaviour
{
    public enum DoorState { Closed, Cracked, Open, Locked }
    public enum InteractionPoint { A_Pull, B_Push, C_Kick }

    [Header("Setup")]
    [SerializeField] private Transform doorPivot;
    
    [Header("State")]
    [SerializeField] private DoorState currentState = DoorState.Closed;
    [SerializeField] private bool startsLocked;

    [Header("Angles")]
    [SerializeField] private float crackAngle = 12f;
    [SerializeField] private float openAngle = 95f;

    [Header("Speed (градусов/сек)")]
    [SerializeField] private float pullSpeed = 35f;
    [SerializeField] private float pushSpeed = 120f;
    [SerializeField] private float kickSpeed = 350f;
    [SerializeField] private float closeSpeed = 70f;
    [SerializeField] private float playerPushSpeed = 60f;

    [Header("Kick")]
    [SerializeField] private int kicksToBreak = 2;
    [SerializeField] private float stunRadius = 2f;
    [SerializeField] private float stunDuration = 2f;

    [Header("Audio")]
    [SerializeField] private AudioSource audioSource;
    [SerializeField] private AudioClip handleSound;
    [SerializeField] private AudioClip pushSound;
    [SerializeField] private AudioClip kickSound;
    [SerializeField] private AudioClip closeSound;
    [SerializeField] private AudioClip lockedSound;
    [SerializeField] private AudioClip breakSound;

    [Header("Events")]
    public UnityEvent OnOpened;
    public UnityEvent OnClosed;
    public UnityEvent OnKicked;

    // Внутреннее состояние
    private float currentAngle;
    private float targetAngle;
    private float currentSpeed;
    private int kickCount;
    public int openDirection = 1;
    private Quaternion closedRotation;
    private Transform playerTransform;
    private bool isPlayerPushing;

    #region Unity Lifecycle

    private void Awake()
    {
        // Сохранить начальное положение
        if (doorPivot != null)
            closedRotation = doorPivot.localRotation;
        
        if (startsLocked)
            currentState = DoorState.Locked;

        // Кешировать игрока
        var player = FindFirstObjectByType<PlayerController>();
        if (player != null)
            playerTransform = player.transform;

        // Audio
        if (audioSource == null)
            audioSource = gameObject.AddComponent<AudioSource>();

        // Убрать HingeJoint если есть — он не нужен
        RemoveHingeJoint();
        
        // Настроить Rigidbody как kinematic
        SetupRigidbody();
    }

    private void Update()
    {
        // Движение к целевому углу
        if (!Mathf.Approximately(currentAngle, targetAngle))
        {
            float speed = isPlayerPushing ? playerPushSpeed : currentSpeed;
            currentAngle = Mathf.MoveTowards(currentAngle, targetAngle, speed * Time.deltaTime);
            ApplyRotation();
        }
        else
        {
            isPlayerPushing = false;
        }

        // Синхронизация состояния с углом
        SyncStateWithAngle();
    }

    #endregion

    #region Setup

    private void RemoveHingeJoint()
    {
        if (doorPivot == null) return;
        
        var hinge = doorPivot.GetComponent<HingeJoint>();
        if (hinge != null)
        {
            DestroyImmediate(hinge);
        }
    }

    private void SetupRigidbody()
    {
        if (doorPivot == null) return;

        var rb = doorPivot.GetComponent<Rigidbody>();
        if (rb != null)
        {
            rb.isKinematic = true;  // Полностью контролируем сами
            rb.useGravity = false;
        }
    }

    private void ApplyRotation()
    {
        if (doorPivot != null)
            doorPivot.localRotation = closedRotation * Quaternion.Euler(0f, currentAngle, 0f);
    }

    private void SyncStateWithAngle()
    {
        if (currentState == DoorState.Locked) return;

        float absAngle = Mathf.Abs(currentAngle);

        if (absAngle < 2f)
            currentState = DoorState.Closed;
        else if (absAngle < crackAngle + 10f)
            currentState = DoorState.Cracked;
        else
            currentState = DoorState.Open;
    }

    #endregion

    #region Public API

    public void Interact(InteractionPoint point)
    {
        // Если открыта — любая точка закрывает
        if (currentState == DoorState.Open)
        {
            Close();
            return;
        }

        switch (point)
        {
            case InteractionPoint.A_Pull: Pull(); break;
            case InteractionPoint.B_Push: Push(); break;
            case InteractionPoint.C_Kick: Kick(); break;
        }
    }

    public string GetInteractionPrompt(InteractionPoint point)
    {
        if (currentState == DoorState.Open) return "Закрыть";

        return point switch
        {
            InteractionPoint.A_Pull => currentState switch
            {
                DoorState.Locked => "Заперто",
                DoorState.Closed => "Приоткрыть",
                DoorState.Cracked => "Закрыть",
                _ => ""
            },
            InteractionPoint.B_Push => currentState == DoorState.Cracked ? "Открыть" : "",
            InteractionPoint.C_Kick => currentState == DoorState.Locked 
                ? $"Выбить ({kicksToBreak - kickCount})" 
                : "Выбить",
            _ => ""
        };
    }

    public bool CanInteract(InteractionPoint point)
    {
        if (currentState == DoorState.Open) return true;

        return point switch
        {
            InteractionPoint.A_Pull => true,
            InteractionPoint.B_Push => currentState != DoorState.Locked,  // ИСПРАВЛЕНО: Push работает всегда если не заперто
            InteractionPoint.C_Kick => true,
            _ => false
        };
    }

    /// <summary>
    /// Толкание игроком при физическом контакте
    /// </summary>
    public void OnPlayerPush(Vector3 pushDirection, float force)
    {
        // Только если дверь приоткрыта или открыта
        if (currentState == DoorState.Closed || currentState == DoorState.Locked) return;
        if (force < 0.1f) return;

        // Толкать в том же направлении что и openDirection (ОТ игрока)
        // Не пересчитываем направление — используем уже установленное
        float addAngle = openDirection * force * 15f;
        float newTarget = Mathf.Clamp(currentAngle + addAngle, -openAngle, openAngle);

        // Применить только если значительное изменение и в правильную сторону
        if (Mathf.Abs(newTarget) > Mathf.Abs(currentAngle) && Mathf.Abs(newTarget - targetAngle) > 2f)
        {
            targetAngle = newTarget;
            isPlayerPushing = true;
        }
    }

    public void Unlock()
    {
        if (currentState == DoorState.Locked)
        {
            currentState = DoorState.Closed;
            kickCount = 0;
        }
    }

    public void Lock()
    {
        if (currentState == DoorState.Closed)
            currentState = DoorState.Locked;
    }

    public DoorState GetCurrentState() => currentState;
    public bool IsPhysicsMode => currentState == DoorState.Cracked || currentState == DoorState.Open;

    #endregion

    #region Interactions

    private void Pull()
    {
        if (currentState == DoorState.Locked)
        {
            PlaySound(lockedSound);
            return;
        }

        if (currentState == DoorState.Cracked)
        {
            Close();
            return;
        }

        // Closed → Cracked
        SetOpenDirection();
        targetAngle = crackAngle * openDirection;
        currentSpeed = pullSpeed;
        PlaySound(handleSound);
    }

    private void Push()
    {
        // ИСПРАВЛЕНО: Push работает напрямую, открывает дверь из любого состояния
        if (currentState == DoorState.Locked)
        {
            PlaySound(lockedSound);
            return;
        }

        // Если закрыта - сначала приоткрыть, потом открыть
        if (currentState == DoorState.Closed)
        {
            SetOpenDirection();
        }

        // Открыть полностью
        targetAngle = openAngle * openDirection;
        currentSpeed = pushSpeed;
        PlaySound(pushSound);
        OnOpened?.Invoke();
    }

    private void Kick()
    {
        PlaySound(kickSound);
        OnKicked?.Invoke();

        if (currentState == DoorState.Locked)
        {
            kickCount++;
            if (kickCount >= kicksToBreak)
            {
                PlaySound(breakSound);
                currentState = DoorState.Closed;
                kickCount = 0;
                KickOpen();
            }
        }
        else
        {
            KickOpen();
        }
    }

    private void KickOpen()
    {
        SetOpenDirection();
        targetAngle = openAngle * openDirection;
        currentSpeed = kickSpeed;
        StunEnemies();
        OnOpened?.Invoke();
    }

    private void Close()
    {
        targetAngle = 0f;
        currentSpeed = closeSpeed;
        PlaySound(closeSound);
        OnClosed?.Invoke();
    }

    #endregion

    #region Helpers

    private void SetOpenDirection()
    {
        if (playerTransform == null || doorPivot == null)
        {
            openDirection = 1;
            return;
        }

        Vector3 toPlayer = (playerTransform.position - doorPivot.position).normalized;
        float dot = Vector3.Dot(doorPivot.forward, toPlayer);
        
        // Дверь открывается ОТ игрока (в противоположную сторону)
        openDirection = dot > 0 ? -1 : 1;
    }

    private void PlaySound(AudioClip clip)
    {
        if (audioSource != null && clip != null)
            audioSource.PlayOneShot(clip);
    }

    private void StunEnemies()
    {
        if (playerTransform == null || doorPivot == null) return;

        Vector3 kickDir = (doorPivot.position - playerTransform.position).normalized;
        Vector3 stunCenter = doorPivot.position + kickDir * 1.5f;

        var colliders = Physics.OverlapSphere(stunCenter, stunRadius);
        foreach (var col in colliders)
        {
            var ai = col.GetComponentInParent<SmartEnemyAI>();
            if (ai != null && !ai.IsDead())
            {
                Vector3 toAI = (ai.Transform.position - doorPivot.position).normalized;
                if (Vector3.Dot(kickDir, toAI) > 0.3f)
                {
                    ai.Blackboard.SetBool(BlackboardKey.IsStunned, true);
                    ai.Blackboard.SetFloat(BlackboardKey.StunnedUntil, Time.time + stunDuration);
                    ai.Psychology?.AddDisorientation(50f);
                }
            }
        }
    }

    #endregion

    #region Debug

    private void OnDrawGizmosSelected()
    {
        if (doorPivot == null) return;

        // Текущее направление
        Gizmos.color = Color.white;
        Gizmos.DrawRay(doorPivot.position, doorPivot.forward * 1.5f);

        // Crack angle
        Gizmos.color = Color.yellow;
        var crackRot = doorPivot.rotation * Quaternion.Euler(0, crackAngle, 0);
        Gizmos.DrawRay(doorPivot.position, crackRot * Vector3.forward * 1.2f);

        // Open angle
        Gizmos.color = Color.green;
        var openRot = doorPivot.rotation * Quaternion.Euler(0, openAngle, 0);
        Gizmos.DrawRay(doorPivot.position, openRot * Vector3.forward * 1.2f);

        // Stun radius
        Gizmos.color = new Color(1f, 0.5f, 0f, 0.3f);
        Gizmos.DrawWireSphere(doorPivot.position + doorPivot.forward * 1.5f, stunRadius);
    }

    #endregion

    #region AI Interaction - НОВОЕ

    /// <summary>
    /// НОВОЕ: Проверить открыта ли дверь
    /// Используется для AI навигации
    /// </summary>
    public bool IsOpen => currentState == DoorState.Open;

    /// <summary>
    /// НОВОЕ: Проверить закрыта ли дверь
    /// </summary>
    public bool IsClosed => currentState == DoorState.Closed;

    /// <summary>
    /// НОВОЕ: Взаимодействие для AI
    /// AI открывает дверь для прохождения
    /// </summary>
    public void OnAIInteract(GameObject aiObject)
    {
        if (currentState == DoorState.Locked)
        {
            PlaySound(lockedSound);
            return;
        }

        if (currentState == DoorState.Open)
        {
            return; // Уже открыта
        }


        // Определить направление открытия на основе AI позиции
        Transform aiTransform = aiObject.transform;
        if (aiTransform != null && doorPivot != null)
        {
            Vector3 toAI = (aiTransform.position - doorPivot.position).normalized;
            float dot = Vector3.Dot(doorPivot.forward, toAI);
            openDirection = dot > 0 ? -1 : 1;
        }

        // Открыть дверь (как Pull)
        Interact(InteractionPoint.A_Pull);
        Interact(InteractionPoint.B_Push);

        Debug.Log($"[Door] AI opened door");
    }

    /// <summary>
    /// НОВОЕ: AI закрывает дверь (для укрытия)
    /// </summary>
    public void OnAIClose(GameObject aiObject)
    {
        if (currentState != DoorState.Open)
        {
            return; // Уже закрыта или в другом состоянии
        }

        Close();
        
        Debug.Log($"[Door] AI closed door for cover");
    }

    #endregion
}
