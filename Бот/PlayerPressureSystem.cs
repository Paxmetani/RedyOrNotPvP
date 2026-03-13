using UnityEngine;
using System.Collections;
using System.Collections.Generic;

/// <summary>
/// Система давления игрока с защитой от спама
/// Голосовые команды через Interact (удержание)
/// </summary>
public class PlayerPressureSystem : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private CameraRayCast cameraRaycast;
    [SerializeField] private InputManager inputManager;

    [Header("Pressure Settings")]
    [SerializeField] private float pressureRange = 10f;
    [SerializeField] private float aimingPressurePerSecond = 8f;
    [SerializeField] private float weaponRaisedMultiplier = 2f;

    [Header("Voice Command")]
    [SerializeField] private float voiceCommandCooldown = 1.3f;
    [SerializeField] private float voiceCommandHoldTime = 0.3f;
    [SerializeField] private float voiceAnimationDuration = 1.0f;

    [Header("Voice Power")]
    [SerializeField, Range(0f, 100f)] private float voiceIntensity = 50f;
    [SerializeField] private float baseStressFromVoice = 25f;
    [SerializeField] private float maxStressFromVoice = 50f;

    [Header("ANTI-SPAM Protection")]
    [SerializeField] private float diminishingReturnsRate = 0.5f;
    [SerializeField] private int maxCommandsBeforeDiminish = 3;
    [SerializeField] private float diminishRecoveryTime = 10f;

    [Header("Audio")]
    [SerializeField] private AudioClip[] surrenderCommandSounds;
    [SerializeField] private AudioSource audioSource;

    [Header("Debug")]
    [SerializeField] private bool showDebugLogs = false;

    private SmartEnemyAI currentTargetAI;
    private GunshotNotifier soundN;
    private float lastVoiceCommandTime = -999f;
    private bool canUseVoiceCommand = true;
    private float interactPressTime = 0f;
    private bool isPlayingVoiceAnimation = false;

    // Anti-spam tracking PER AI
    private Dictionary<SmartEnemyAI, VoiceCommandData> aiVoiceData = new Dictionary<SmartEnemyAI, VoiceCommandData>();

    private void Awake()
    {
        if (cameraRaycast == null)
            cameraRaycast = GetComponent<CameraRayCast>();

        if (inputManager == null)
            inputManager = GetComponentInParent<PlayerController>()?.inputManager;

        if (audioSource == null)
            audioSource = gameObject.AddComponent<AudioSource>();

        soundN = GetComponent<GunshotNotifier>();
    }

    private void OnEnable()
    {
        if (inputManager != null)
        {
            inputManager.onInteractInputDown.AddListener(OnInteractPressed);
            inputManager.onInteractInputUp.AddListener(OnInteractReleased);
        }
    }

    private void OnDisable()
    {
        if (inputManager != null)
        {
            inputManager.onInteractInputDown.RemoveListener(OnInteractPressed);
            inputManager.onInteractInputUp.RemoveListener(OnInteractReleased);
        }
    }

    private void Update()
    {
        UpdatePressureTarget();
        ApplyContinuousPressure();
        UpdateCooldown();
        UpdateDiminishingReturns();
    }

    #region Target Detection

    private void UpdatePressureTarget()
    {
        currentTargetAI = null;

        if (cameraRaycast == null || !cameraRaycast.hit.collider)
            return;

        float distance = Vector3.Distance(cameraRaycast.hit.point, transform.position);
        if (distance > pressureRange)
            return;

        var enemyAI = cameraRaycast.hit.collider.GetComponent<SmartEnemyAI>();
        if (enemyAI == null)
            enemyAI = cameraRaycast.hit.collider.GetComponentInParent<SmartEnemyAI>();

        if (enemyAI != null && !enemyAI.IsDead())
        {
            currentTargetAI = enemyAI;
        }
    }

    #endregion

    #region Continuous Pressure

    private void ApplyContinuousPressure()
    {
        if (currentTargetAI == null || currentTargetAI.Psychology == null)
            return;

        float pressure = aimingPressurePerSecond * Time.deltaTime;

        if (inputManager != null && inputManager.aim)
        {
            pressure *= weaponRaisedMultiplier;
        }

        currentTargetAI.Psychology.OnPlayerPressure(pressure);
    }

    #endregion

    #region Voice Command

    private void OnInteractPressed()
    {
        interactPressTime = Time.time;

        bool hasArrestTarget = currentTargetAI != null && currentTargetAI.CanBeArrested();
        bool isInteract = cameraRaycast.interactableDetected;

        if (!hasArrestTarget && canUseVoiceCommand && !isPlayingVoiceAnimation && !isInteract)
        {
            StartCoroutine(PlayVoiceCommandSequence());
        }
    }

    private void OnInteractReleased()
    {
        float holdDuration = Time.time - interactPressTime;

        bool hasArrestTarget = currentTargetAI != null && currentTargetAI.CanBeArrested();
        bool isInteract = cameraRaycast.interactableDetected;

        if (!hasArrestTarget && holdDuration >= voiceCommandHoldTime && canUseVoiceCommand && !isInteract)
        {
            ExecuteVoiceCommand();
        }
    }

    private IEnumerator PlayVoiceCommandSequence()
    {
        isPlayingVoiceAnimation = true;

        // Анимация
        var itemManager = GetComponentInParent<ItemManager>();
        if (itemManager != null)
        {
            var gun = itemManager.GetComponentInChildren<Gun>(includeInactive: false);
            if (gun != null)
            {
                gun.PlayVoiceCommandAnimation();
            }
        }

        // Звук
        if (surrenderCommandSounds != null)
        {
           SoundEmitter.Instance.PlaySound("Voice_Command");
            soundN.NotifySound(transform.position, SoundType.VoiceShout);
        }

        yield return new WaitForSeconds(voiceAnimationDuration);

        // Вернуть в Idle
        if (itemManager != null)
        {
            var gun = itemManager.GetComponentInChildren<Gun>(includeInactive: false);
            if (gun != null && gun.gunAnimator != null)
            {
                gun.gunAnimator.SetInteger("state", 0);
            }
        }

        isPlayingVoiceAnimation = false;
    }

    private void ExecuteVoiceCommand()
    {
        canUseVoiceCommand = false;
        lastVoiceCommandTime = Time.time;

        Collider[] targets = Physics.OverlapSphere(transform.position, pressureRange);
        int affectedCount = 0;

        foreach (var col in targets)
        {
            var enemyAI = col.GetComponent<SmartEnemyAI>();
            if (enemyAI == null)
                enemyAI = col.GetComponentInParent<SmartEnemyAI>();

            if (enemyAI == null || enemyAI.IsDead())
                continue;

            Vector3 dirToEnemy = (enemyAI.Transform.position - transform.position).normalized;
            float angle = Vector3.Angle(transform.forward, dirToEnemy);

            // Игрок должен смотреть примерно на врага (конус 60°)
            if (angle > 60f)
                continue;

            // УДАЛЕНО: Ограничение угла врага - теперь бот реагирует даже со спины!
            // Голос слышен всегда, реакция обрабатывается в AIPerceptionModule.HandleVoiceCommand()
            
            // ANTI-SPAM:  Эффективность
            float effectiveness = GetVoiceEffectiveness(enemyAI);

            if (effectiveness <= 0.1f)
            {
                if (showDebugLogs)
                    Debug.Log($"[Pressure] {enemyAI.name} - Ignoring command (effectiveness too low)");
                continue;
            }

            float distance = Vector3.Distance(transform.position, enemyAI.Transform.position);
            float distanceFactor = 1f - (distance / pressureRange);

            float voiceFactor = voiceIntensity / 100f;
            float stressAmount = Mathf.Lerp(baseStressFromVoice, maxStressFromVoice, voiceFactor);
            stressAmount *= distanceFactor;
            stressAmount *= effectiveness; // ПРИМЕНЯЕМ ЭФФЕКТИВНОСТЬ

            enemyAI.Psychology?.OnPlayerPressure(stressAmount);
            enemyAI.Perception?.HandleVoiceCommand(transform.position, stressAmount);

            // Отследить команду
            TrackVoiceCommand(enemyAI, stressAmount);

            affectedCount++;

            if (showDebugLogs)
            {
                Debug.Log($"[Pressure] {enemyAI.name} - Stress +{stressAmount:F1} (Effectiveness:  {effectiveness:F2})");
            }
        }

        if (showDebugLogs)
        {
            Debug.Log($"[Pressure] Voice command affected {affectedCount} enemies");
        }
    }

    #endregion

    #region Anti-Spam System

    private float GetVoiceEffectiveness(SmartEnemyAI ai)
    {
        if (!aiVoiceData.ContainsKey(ai))
        {
            aiVoiceData[ai] = new VoiceCommandData();
        }

        var data = aiVoiceData[ai];

        // Восстановление эффективности со временем
        float timeSinceLastCommand = Time.time - data.lastCommandTime;
        if (timeSinceLastCommand > diminishRecoveryTime)
        {
            data.commandCount = 0;
            data.effectiveness = 1f;
        }

        return data.effectiveness;
    }

    private void TrackVoiceCommand(SmartEnemyAI ai, float stressApplied)
    {
        if (!aiVoiceData.ContainsKey(ai))
        {
            aiVoiceData[ai] = new VoiceCommandData();
        }

        var data = aiVoiceData[ai];
        data.commandCount++;
        data.lastCommandTime = Time.time;
        data.totalStressApplied += stressApplied;

        // Уменьшение эффективности
        if (data.commandCount > maxCommandsBeforeDiminish)
        {
            int excessCommands = data.commandCount - maxCommandsBeforeDiminish;
            data.effectiveness = Mathf.Max(0.1f, 1f - (excessCommands * diminishingReturnsRate));

            if (showDebugLogs)
            {
                Debug.Log($"[Pressure] {ai.name} - Effectiveness diminished to {data.effectiveness:F2}");
            }
        }
    }

    private void UpdateDiminishingReturns()
    {
        var keysToRemove = new List<SmartEnemyAI>();

        foreach (var kvp in aiVoiceData)
        {
            if (kvp.Key == null || kvp.Key.IsDead())
            {
                keysToRemove.Add(kvp.Key);
                continue;
            }

            // Естественное восстановление
            float timeSinceLast = Time.time - kvp.Value.lastCommandTime;
            if (timeSinceLast > diminishRecoveryTime)
            {
                kvp.Value.commandCount = Mathf.Max(0, kvp.Value.commandCount - 1);
                kvp.Value.effectiveness = Mathf.Min(1f, kvp.Value.effectiveness + 0.1f);
            }
        }

        foreach (var key in keysToRemove)
        {
            aiVoiceData.Remove(key);
        }
    }

    #endregion

    #region Cooldown

    private void UpdateCooldown()
    {
        if (!canUseVoiceCommand)
        {
            if (Time.time - lastVoiceCommandTime >= voiceCommandCooldown)
            {
                canUseVoiceCommand = true;
            }
        }
    }

    #endregion

    #region Public API

    public bool CanUseVoiceCommand() => canUseVoiceCommand && !isPlayingVoiceAnimation;

    public float GetVoiceCommandCooldown()
    {
        if (canUseVoiceCommand && !isPlayingVoiceAnimation) return 0f;
        return voiceCommandCooldown - (Time.time - lastVoiceCommandTime);
    }

    public float GetCurrentTargetEffectiveness()
    {
        if (currentTargetAI == null) return 0f;
        return GetVoiceEffectiveness(currentTargetAI);
    }

    #endregion
}

public class VoiceCommandData
{
    public int commandCount = 0;
    public float lastCommandTime = -999f;
    public float effectiveness = 1f;
    public float totalStressApplied = 0f;
}