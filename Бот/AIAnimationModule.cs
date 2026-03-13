using UnityEngine;

/// <summary>
/// MODULE:  Animation
/// ��������� ������ � ����������� �����������
/// </summary>
public class AIAnimationModule : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private Animator animator;
    [SerializeField] private Transform headBone;

    [Header("Look At")]
    [SerializeField] private bool enableHeadTracking = true;
    [SerializeField] private float headTrackSpeed = 5f;
    [SerializeField] private float maxHeadAngle = 70f;

    [Header("Smoothing")]
    [SerializeField] private float movementSmoothTime = 0.1f;

    [Header("Debug")]
    [SerializeField] private bool showDebugLogs = false;

    // === ��������� ��������� ===
    private static readonly int SPEED = Animator.StringToHash("Speed");
    private static readonly int WEAPON_RAISED = Animator.StringToHash("WeaponRaised");
    private static readonly int DECIDING = Animator.StringToHash("Deciding");
    private static readonly int SUPPRESSED = Animator.StringToHash("Suppressed");
    private static readonly int SURRENDERED = Animator.StringToHash("Surrendered");
    private static readonly int IN_COVER = Animator.StringToHash("InCover");
    private static readonly int DEAD = Animator.StringToHash("IsDead");
    private static readonly int BEING_CUFFED = Animator.StringToHash("BeingCuffed");
    private static readonly int ARRESTED = Animator.StringToHash("IsArrested");
    private static readonly int HIT = Animator.StringToHash("Hit");

    private SmartEnemyAI core;

    // Smoothing
    private float currentSpeed = 0f;
    private float speedVelocity = 0f;

    // Look at
    private Quaternion originalHeadRotation;
    private Vector3 currentLookTarget;

    public void Initialize(SmartEnemyAI coreAI)
    {
        core = coreAI;

        if (animator == null)
            animator = GetComponentInChildren<Animator>();

        if (headBone != null)
        {
            originalHeadRotation = headBone.localRotation;
        }
    }

    public void UpdateModule()
    {
        if (animator == null || core == null) return;

        // НОВОЕ: Если мёртв - только анимация смерти, ничего больше
        if (core.Health != null && core.Health.IsDead)
        {
            animator.SetBool(DEAD, true);
            // Сбросить все остальные состояния
            animator.SetBool(DECIDING, false);
            animator.SetBool(SUPPRESSED, false);
            animator.SetBool(BEING_CUFFED, false);
            return;
        }

        UpdateAllParameters();
    }

    private void LateUpdate()
    {
        if (enableHeadTracking && headBone != null && core != null)
        {
            UpdateHeadTracking();
        }
    }

    #region Parameter Updates

    private void UpdateAllParameters()
    {
        // Speed (smoothed)
        float targetSpeed = core.Agent.velocity.magnitude;
        currentSpeed = Mathf.SmoothDamp(currentSpeed, targetSpeed, ref speedVelocity, movementSmoothTime);
        animator.SetFloat(SPEED, currentSpeed);

        // Weapon Raised (������ �������/�������)
        bool weaponRaised = core.Blackboard.GetBool(BlackboardKey.WeaponRaised, false);
        animator.SetBool(WEAPON_RAISED, weaponRaised);

        // Deciding (�����������)
        bool deciding = core.Blackboard.GetBool(BlackboardKey.IsDeciding, false);
        animator.SetBool(DECIDING, deciding);

        // Suppressed (������ �����, ���� ����� ����)
        bool suppressed = core.Blackboard.GetBool(BlackboardKey.IsSuppressed, false);
        animator.SetBool(SUPPRESSED, suppressed);

        // Surrendered (������, ���� ����� ����/���� �� �������)
        bool surrendered = core.Blackboard.GetBool(BlackboardKey.HasSurrendered, false);
        animator.SetBool(SURRENDERED, surrendered);

        // In Cover (� �������)
        bool inCover = core.Blackboard.GetBool(BlackboardKey.IsInCover, false);
        animator.SetBool(IN_COVER, inCover);

        // Being Cuffed (������� ������)
        bool beingCuffed = core.Blackboard.GetBool(BlackboardKey.IsBeingArrested, false);
        animator.SetBool(BEING_CUFFED, beingCuffed);

        // Arrested (��������� ���������)
        bool arrested = core.Blackboard.GetBool(BlackboardKey.IsArrested, false);
        animator.SetBool(ARRESTED, arrested);

        // Dead (�����)
        bool dead = core.Health.IsDead;
        animator.SetBool(DEAD, dead);

        // ========== НОВОЕ: DEBUG LOG для диагностики ==========
        if (showDebugLogs && Time.frameCount % 60 == 0)
        {
            if (deciding) Debug.Log($"[Animation] {core.name} - DECIDING=true");
            if (suppressed) Debug.Log($"[Animation] {core.name} - SUPPRESSED=true");
            if (surrendered) Debug.Log($"[Animation] {core.name} - SURRENDERED=true");
        }
    }

    #endregion

    #region Head Tracking

    private void UpdateHeadTracking()
    {
        Transform threat = core.Blackboard.GetTransform(BlackboardKey.CurrentThreat);

        if (threat != null)
        {
            Vector3 targetPos = threat.position + Vector3.up * 1.5f;
            currentLookTarget = Vector3.Lerp(currentLookTarget, targetPos, Time.deltaTime * headTrackSpeed);

            Vector3 dirToTarget = (currentLookTarget - headBone.position).normalized;
            float angleToTarget = Vector3.Angle(core.Transform.forward, dirToTarget);

            if (angleToTarget <= maxHeadAngle)
            {
                Quaternion targetRotation = Quaternion.LookRotation(dirToTarget);
                headBone.rotation = Quaternion.Slerp(headBone.rotation, targetRotation, Time.deltaTime * headTrackSpeed);
            }
            else
            {
                headBone.localRotation = Quaternion.Slerp(headBone.localRotation, originalHeadRotation, Time.deltaTime * headTrackSpeed);
            }
        }
        else
        {
            headBone.localRotation = Quaternion.Slerp(headBone.localRotation, originalHeadRotation, Time.deltaTime * headTrackSpeed);
        }
    }

    #endregion

    #region Public Triggers (������ �����������)

    /// <summary>
    /// ������� ��������� �����
    /// </summary>
    public void TriggerHit()
    {
        if (animator != null)
        {
            animator.SetTrigger(HIT);
        }
    }

    public void TriggerDeath()
    {
        if (animator != null)
        {
            animator.SetBool(DEAD, true);
            animator.SetBool(DECIDING, false);
        }
    }

    /// <summary>
    /// НОВОЕ: Запустить анимацию сдачи
    /// </summary>
    public void TriggerSurrender()
    {
        if (animator == null) return;

        // Принудительно установить состояние
        animator.SetBool(SURRENDERED, true);
        animator.SetBool(WEAPON_RAISED, false);
        animator.SetBool(DECIDING, false);
        
        if (showDebugLogs)
            Debug.Log($"[Animation] {core.name} - SURRENDER animation triggered");
    }

    /// <summary>
    /// НОВОЕ: Запустить анимацию раздумия (Deciding)
    /// </summary>
    public void TriggerDeciding()
    {
        if (animator == null) return;

        animator.SetBool(DECIDING, true);
        
        if (showDebugLogs)
            Debug.Log($"[Animation] {core.name} - DECIDING animation triggered");
    }

    /// <summary>
    /// НОВОЕ: Остановить анимацию раздумия
    /// </summary>
    public void StopDeciding()
    {
        if (animator == null) return;

        animator.SetBool(DECIDING, false);
        
        if (showDebugLogs)
            Debug.Log($"[Animation] {core.name} - DECIDING animation stopped");
    }

    /// <summary>
    /// НОВОЕ: Запустить анимацию начала ареста (BeingCuffed)
    /// </summary>
    public void TriggerBeingArrested()
    {
        if (animator == null) return;

        animator.SetBool(BEING_CUFFED, true);
        animator.SetBool(SURRENDERED, true);  // Должен быть сдавшимся
        
        if (showDebugLogs)
            Debug.Log($"[Animation] {core.name} - BEING_CUFFED animation triggered");
    }

    /// <summary>
    /// НОВОЕ: Запустить анимацию завершения ареста
    /// </summary>
    public void TriggerArrested()
    {
        if (animator == null) return;

        animator.SetBool(ARRESTED, true);
        animator.SetBool(BEING_CUFFED, false);
        
        if (showDebugLogs)
            Debug.Log($"[Animation] {core.name} - ARRESTED animation triggered");
    }

    /// <summary>
    /// НОВОЕ: Запустить анимацию подавления (Suppressed)
    /// Полу-сдался, руки подняты но оружие ещё в руках
    /// </summary>
    public void TriggerSuppressed()
    {
        if (animator == null) return;

        animator.SetBool(SUPPRESSED, true);
        animator.SetBool(WEAPON_RAISED, false);
        
        if (showDebugLogs)
            Debug.Log($"[Animation] {core.name} - SUPPRESSED animation triggered");
    }

    /// <summary>
    /// НОВОЕ: Остановить анимацию подавления
    /// </summary>
    public void StopSuppressed()
    {
        if (animator == null) return;

        animator.SetBool(SUPPRESSED, false);
        
        if (showDebugLogs)
            Debug.Log($"[Animation] {core.name} - SUPPRESSED animation stopped");
    }

    /// <summary>
    /// НОВОЕ: Проиграть анимацию по имени
    /// </summary>
    public void PlayAnimation(string animationName, float transitionDuration = 0.3f)
    {
        if (animator == null) return;

        // Переход в состояние по имени
        animator.CrossFadeInFixedTime(animationName, transitionDuration);
    }

    /// <summary>
    /// НОВОЕ: Установить параметр для анимации
    /// </summary>
    public void SetAnimationParameter(string paramName, bool value)
    {
        if (animator == null) return;
        animator.SetBool(paramName, value);
    }

    #endregion

    #region Public Getters

    public bool IsWeaponRaised() => animator != null && animator.GetBool(WEAPON_RAISED);

    #endregion
}