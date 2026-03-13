using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// FIXED Perception - слух с фильтрацией команд
/// </summary>
public class AIPerceptionModule : MonoBehaviour
{
    [Header("Central Vision (дальнее)")]
    [SerializeField] private float visionRange = 50f;
    [SerializeField] private float visionAngle = 90f;

    [Header("Peripheral Vision (боковое)")]
    [SerializeField] private float peripheralRange = 20f;
    [SerializeField] private float peripheralAngle = 180f;

    [Header("Performance")]
    [SerializeField] private float visionCheckInterval = 0.15f;
    [SerializeField] private LayerMask targetLayers;
    [SerializeField] private LayerMask obstacleLayers;

    [Header("Hearing")]
    [SerializeField] private float gunshotHearingRange = 60f;
    [SerializeField] private float footstepHearingRange = 15f;

    [Header("Debug")]
    [SerializeField] private bool showDebugLogs = false;
    [SerializeField] private bool showGizmos = true;

    [Header("PvP Mode")]
    [SerializeField] private bool   pvpMode          = false;
    [SerializeField] private string myTeamTag        = "Enemy";
    [SerializeField] private string pvpEnemyTeamTag  = "TeamA";

    private SmartEnemyAI core;
    private Transform currentTarget = null;
    private float nextVisionCheckTime = 0f;
    private float alertness = 0f;
    private float lastTacticalReactionTime = -999f;  // cooldown между тактическими реакциями
    private const float TacticalReactionCooldown = 4f;

    public void Initialize(SmartEnemyAI coreAI)
    {
        core = coreAI;
    }

    /// <summary>
    /// Enable PvP mode so this bot detects all enemies by team tag instead of only the Player tag.
    /// Called by PvPMatchManager.ConfigureBotForPvP().
    /// </summary>
    public void SetPvPMode(bool enabled, string thisTeamTag, string enemyTeamTag)
    {
        pvpMode         = enabled;
        myTeamTag       = thisTeamTag;
        pvpEnemyTeamTag = enemyTeamTag;
    }

    public void UpdatePerception()
    {
        if (Time.time >= nextVisionCheckTime)
        {
            nextVisionCheckTime = Time.time + visionCheckInterval;
            CheckVision();
        }

        if (alertness > 0f)
        {
            alertness -= 0.1f * Time.deltaTime;
            alertness = Mathf.Max(0f, alertness);
        }
    }

    private void CheckVision()
    {
        if (pvpMode)
        {
            CheckVisionPvP();
            return;
        }

        // ── Campaign mode: target only the player ────────────────────────────
        GameObject player = GameObject.FindGameObjectWithTag("Player");

        if (player == null)
        {
            ClearTarget();
            return;
        }

        Transform playerTransform = player.transform;
        Vector3 toPlayer = playerTransform.position - core.Transform.position;
        float distance = toPlayer.magnitude;

        // Central vision
        if (distance <= visionRange)
        {
            Vector3 dirToPlayer = toPlayer.normalized;
            dirToPlayer.y = 0;

            Vector3 forward = core.Transform.forward;
            forward.y = 0;

            float angle = Vector3.Angle(forward, dirToPlayer);
            float effectiveAngle = visionAngle * (1f + alertness * 0.5f);

            if (angle <= effectiveAngle * 0.5f)
            {
                if (HasLineOfSight(playerTransform))
                {
                    SetTarget(playerTransform, true);
                    return;
                }
            }
        }

        // Peripheral vision
        if (distance <= peripheralRange)
        {
            Vector3 dirToPlayer = toPlayer.normalized;
            dirToPlayer.y = 0;

            Vector3 forward = core.Transform.forward;
            forward.y = 0;

            float angle = Vector3.Angle(forward, dirToPlayer);

            if (angle <= peripheralAngle * 0.5f)
            {
                if (HasLineOfSight(playerTransform))
                {
                    SetTarget(playerTransform, false);
                    return;
                }
            }
        }

        ClearTarget();
    }

    // ─── PvP Vision: detect nearest visible enemy by team tag ────────────────

    private void CheckVisionPvP()
    {
        Transform bestTarget   = null;
        float     bestPriority = float.MinValue;   // higher priority = better candidate

        // 1. Check the human player
        GameObject playerObj = GameObject.FindGameObjectWithTag("Player");
        if (playerObj != null)
        {
            var playerHM = playerObj.GetComponent<HealthManager>();
            bool isEnemy = (playerHM == null) || (playerHM.TeamTag != myTeamTag);

            if (isEnemy && (playerHM == null || !playerHM.IsDead))
            {
                float p = EvaluateTargetPriority(playerObj.transform);
                if (p > bestPriority) { bestPriority = p; bestTarget = playerObj.transform; }
            }
        }

        // 2. Check enemy bots (use AISquadManager list when available to avoid FindObjectsOfType)
        if (AISquadManager.Instance != null)
        {
            foreach (var ai in AISquadManager.Instance.GetAllRegisteredAI())
            {
                if (ai == null || ai == core || ai.IsDead()) continue;
                if (ai.Health != null && ai.Health.TeamTag == myTeamTag) continue;

                float p = EvaluateTargetPriority(ai.Transform);
                if (p > bestPriority) { bestPriority = p; bestTarget = ai.Transform; }
            }
        }
        else
        {
            // Fallback: iterate scene objects
            var allAI = FindObjectsByType<SmartEnemyAI>(FindObjectsSortMode.None);
            foreach (var ai in allAI)
            {
                if (ai == null || ai == core || ai.IsDead()) continue;
                if (ai.Health != null && ai.Health.TeamTag == myTeamTag) continue;

                float p = EvaluateTargetPriority(ai.Transform);
                if (p > bestPriority) { bestPriority = p; bestTarget = ai.Transform; }
            }
        }

        if (bestTarget != null)
            SetTarget(bestTarget, true);
        else
            ClearTarget();
    }

    /// <summary>
    /// Returns a priority score for a candidate target (higher = better).
    /// Returns float.MinValue when the target is outside vision cone or blocked.
    /// </summary>
    private float EvaluateTargetPriority(Transform target)
    {
        if (target == null) return float.MinValue;

        Vector3 toTarget = target.position - core.Transform.position;
        float   distance = toTarget.magnitude;

        if (distance > visionRange) return float.MinValue;

        Vector3 dirToTarget = toTarget.normalized;
        dirToTarget.y = 0;

        Vector3 forward = core.Transform.forward;
        forward.y = 0;

        float angle          = Vector3.Angle(forward, dirToTarget);
        float effectiveAngle = visionAngle * (1f + alertness * 0.5f);

        // Central vision
        if (angle <= effectiveAngle * 0.5f && HasLineOfSight(target))
            return 1f / Mathf.Max(0.01f, distance);

        // Peripheral vision
        if (distance <= peripheralRange && angle <= peripheralAngle * 0.5f && HasLineOfSight(target))
            return 0.5f / Mathf.Max(0.01f, distance);

        return float.MinValue;
    }

    // ─────────────────────────────────────────────────────────────────────────

    private bool HasLineOfSight(Transform target)
    {
        Vector3 eyePos = core.Transform.position + Vector3.up * 1.6f;
        Vector3 targetPos = target.position + Vector3.up * 1.0f;

        Vector3 rayDir = (targetPos - eyePos).normalized;
        float rayDistance = Vector3.Distance(eyePos, targetPos);

        RaycastHit hit;
        if (Physics.Raycast(eyePos, rayDir, out hit, rayDistance, obstacleLayers))
        {
            if (hit.transform != target && !hit.transform.IsChildOf(target))
            {
                return false;
            }
        }

        return true;
    }

    private void SetTarget(Transform target, bool centralVision)
    {
        bool wasNull = currentTarget == null;
        currentTarget = target;

        core.Blackboard.SetTransform(BlackboardKey.CurrentThreat, target);
        core.Blackboard.SetVector3(BlackboardKey.LastKnownThreatPosition, target.position);
        core.Blackboard.SetFloat(BlackboardKey.LastSeenThreatTime, Time.time);
        core.Blackboard.Set(BlackboardKey.ThreatLevel, ThreatLevel.Confirmed);

        alertness = Mathf.Min(1f, alertness + 0.3f);

        if (wasNull && showDebugLogs)
        {
            Debug.Log($"[Perception] {core.name} - SPOTTED PLAYER!");
        }

        core.Squad?.ShareThreatIntel(target.position, true);
    }

    private void ClearTarget()
    {
        if (currentTarget != null)
        {
            // НОВОЕ: НЕ ЗАБЫВАТЬ сразу! Сохранить последнюю позицию
            Vector3 lastPosition = currentTarget.position;
            core.Blackboard.SetVector3(BlackboardKey.LastKnownThreatPosition, lastPosition);
            core.Blackboard.SetVector3(BlackboardKey.PredictedThreatPosition, lastPosition);
            
            currentTarget = null;
            core.Blackboard.Remove(BlackboardKey.CurrentThreat);
            core.Blackboard.Set(BlackboardKey.ThreatLevel, ThreatLevel.Suspected);
            
            // НОВОЕ: Бот помнит что видел угрозу недавно
            core.Blackboard.SetFloat(BlackboardKey.LastSeenThreatTime, Time.time);
            
            if (showDebugLogs)
                Debug.Log($"[Perception] {core.name} - Lost visual but remembering last position");
        }
    }

    #region Sound System - FIXED & SIMPLIFIED

    /// <summary>
    /// FIXED: Услышал звук - простая фильтрация источника
    /// Реакция обрабатывается в ReactiveCombatLayer!
    /// </summary>
    public void OnHearSound(Vector3 position, SoundType type, float intensity = 1f)
    {
        float distance = Vector3.Distance(core.Transform.position, position);

        // ПРОВЕРКА ДИСТАНЦИИ
        float maxRange = type == SoundType.Gunshot ? gunshotHearingRange : footstepHearingRange;
        if (distance > maxRange) return;

        // ПРОВЕРКА ИСТОЧНИКА - не союзник ли?
        bool isAllySound = CheckIfAllyAtPosition(position);
        if (isAllySound)
        {
            if (showDebugLogs && Time.frameCount % 120 == 0)
                Debug.Log($"[Perception] {core.name} - Ignored {type} from ally at {distance:F1}m");
            return;
        }

        // ========== ОБНОВИТЬ BLACKBOARD ==========
        core.Blackboard.SetVector3(BlackboardKey.LastHeardSoundPosition, position);
        core.Blackboard.SetFloat(BlackboardKey.LastSuspectedThreatTime, Time.time);
        core.Blackboard.Set(BlackboardKey.LastSoundType, type);

        // ========== РЕАКЦИЯ НА ТИП ЗВУКА ==========
        switch (type)
        {
            case SoundType.Gunshot:
                HandleGunshotSound(position, distance);
                break;

            case SoundType.VoiceShout:
                HandleVoiceCommand(position, distance);
                break;

            case SoundType.Footsteps:
                HandleFootstepSound(position, distance);
                break;

            default:
                // Другие звуки - просто повысить alertness
                alertness = Mathf.Min(1f, alertness + 0.2f);
                core.Blackboard.Set(BlackboardKey.ThreatLevel, ThreatLevel.Suspected);
                break;
        }
    }

    /// <summary>
    /// НОВОЕ: Обработать звук выстрела
    /// ReactiveCombatLayer обработает близкие выстрелы в Reflex_ReactToNearbyGunfire()
    /// Здесь мы просто обновляем информацию
    /// </summary>
    private void HandleGunshotSound(Vector3 soundPosition, float distance)
    {
        alertness = 1f;

        var currentThreatLevel = core.Blackboard.Get(BlackboardKey.ThreatLevel, ThreatLevel.None);

        core.Blackboard.SetVector3(BlackboardKey.PredictedThreatPosition, soundPosition);

        if (currentThreatLevel == ThreatLevel.None)
            core.Blackboard.Set(BlackboardKey.ThreatLevel, ThreatLevel.Suspected);

        core.Squad?.ShareThreatIntel(soundPosition, currentThreatLevel == ThreatLevel.Confirmed);

        float stressAmount = Mathf.Lerp(20f, 5f, distance / gunshotHearingRange);
        core.Psychology?.AddStress(stressAmount);

        // Тактическая реакция — только когда игрок нас НЕ видит
        if (currentTarget == null && !IsPlayerLookingAtMe())
            ReactToThreatSound(soundPosition);

        if (showDebugLogs && Time.frameCount % 60 == 0)
            Debug.Log($"[Perception] {core.name} - Heard GUNSHOT at {distance:F1}m, stress +{stressAmount:F0}");
    }

    /// <summary>
    /// НОВОЕ: Обработать звуки шагов
    /// Низкий приоритет - только повышаем осторожность
    /// </summary>
    private void HandleFootstepSound(Vector3 soundPosition, float distance)
    {
        alertness = Mathf.Min(1f, alertness + 0.15f);
        core.Blackboard.Set(BlackboardKey.ThreatLevel, ThreatLevel.Suspected);

        // Небольшой стресс от неизвестных шагов
        core.Psychology?.AddStress(3f);

        if (showDebugLogs && Time.frameCount % 120 == 0)
        {
            Debug.Log($"[Perception] {core.name} - Heard footsteps at {distance:F1}m");
        }
    }

    /// <summary>
    /// НОВЫЙ МЕТОД:  Проверить есть ли союзник в позиции звука
    /// </summary>
    private bool CheckIfAllyAtPosition(Vector3 soundPosition)
    {
        // Найти всех AI рядом с источником звука (в радиусе 3м)
        Collider[] nearbyColliders = Physics.OverlapSphere(soundPosition, 3f);

        foreach (var col in nearbyColliders)
        {
            SmartEnemyAI otherAI = col.GetComponent<SmartEnemyAI>();
            if (otherAI == null)
                otherAI = col.GetComponentInParent<SmartEnemyAI>();

            if (otherAI == null || otherAI == core) continue;

            // Проверить команду
            if (otherAI.Health != null && core.Health != null)
            {
                if (otherAI.Health.TeamTag == core.Health.TeamTag)
                {
                    // Нашли союзника в источнике звука
                    return true;
                }
            }
        }

        return false; // Никого не нашли или враг
    }

    public void OnBulletHitFromDirection(Vector3 hitPoint, Vector3 direction)
    {
        Vector3 estimatedShooter = hitPoint - direction.normalized * 20f;

        // Проверить - не союзник ли?
        bool isAllyHit = CheckIfAllyAtPosition(estimatedShooter);

        if (isAllyHit)
        {
            if (showDebugLogs)
                Debug.Log($"[Perception] {core.name} - Friendly fire, ignoring");

            return; // Дружественный огонь - игнорируем
        }

        core.Blackboard.SetVector3(BlackboardKey.PredictedThreatPosition, estimatedShooter);
        core.Blackboard.SetFloat(BlackboardKey.LastSuspectedThreatTime, Time.time);
        core.Blackboard.Set(BlackboardKey.ThreatLevel, ThreatLevel.Suspected);

        alertness = 1f;

        if (showDebugLogs)
        {
            Debug.Log($"[Perception] {core.name} - HIT FROM ENEMY!");
        }
    }

    #endregion

    public bool HasVisibleTarget() => currentTarget != null;
    public Transform GetVisibleTarget() => currentTarget;
    public Transform GetCurrentTarget() => currentTarget;
    public float GetAlertness() => alertness;

    #region Voice Command Reaction - НОВОЕ

    /// <summary>
    /// НОВОЕ: Обработать голосовую команду игрока (сдача/давление)
    /// Боты ВСЕГДА реагируют на голос - даже если со спины!
    /// </summary>
    public void HandleVoiceCommand(Vector3 soundPosition, float distance)
    {
        alertness = 1f;

        Vector3 dirToSound = (soundPosition - core.Transform.position).normalized;
        dirToSound.y = 0;

        core.Blackboard.SetVector3(BlackboardKey.PredictedThreatPosition, soundPosition);
        core.Blackboard.SetVector3(BlackboardKey.LastKnownThreatPosition, soundPosition);
        core.Blackboard.Set(BlackboardKey.ThreatLevel, ThreatLevel.Confirmed);

        float stressFromVoice = core.Psychology?.GetVoiceVulnerability() ?? 0.5f;
        core.Psychology?.AddStress(stressFromVoice * 30f);

        float dotProduct = Vector3.Dot(core.Transform.forward, dirToSound);

        if (dotProduct < -0.3f)
        {
            core.Transform.rotation = Quaternion.LookRotation(dirToSound);
            core.Psychology?.AddDisorientation(20f);

            if (showDebugLogs)
                Debug.Log($"[Perception] {core.name} - VOICE FROM BEHIND at {distance:F1}m! Turning...");
        }
        else if (dotProduct < 0.5f)
        {
            core.Movement?.SetLookTarget(dirToSound, RotationMode.Combat);
            core.Psychology?.AddDisorientation(10f);

            if (showDebugLogs)
                Debug.Log($"[Perception] {core.name} - Voice from side at {distance:F1}m");
        }

        // Тактическая реакция — только когда игрок нас НЕ видит
        if (currentTarget == null && !IsPlayerLookingAtMe())
        {
            ReactToThreatSound(soundPosition);
        }
        else if (distance < 8f && core.SurrenderDecision != null && !core.SurrenderDecision.IsDeciding())
        {
            // Игрок близко И смотрит — раздумье о сдаче
            core.SurrenderDecision.StartDecision();

            if (showDebugLogs)
                Debug.Log($"[Perception] {core.name} - Voice command triggered DECISION ({distance:F1}m)");
        }
        else
        {
            core.WeaponController?.RaiseWeapon();
        }

        core.Squad?.ShareThreatIntel(soundPosition, true);
    }

    #endregion

    #region Tactical Response

    // ── Проверка: смотрит ли игрок НА этого бота ─────────────────────────────
    private bool IsPlayerLookingAtMe()
    {
        Camera cam = Camera.main;
        if (cam == null) return false;

        Vector3 toBot = (core.Transform.position + Vector3.up * 1.5f - cam.transform.position).normalized;
        float angle = Vector3.Angle(cam.transform.forward, toBot);
        float halfFOV = cam.fieldOfView * 0.5f;

        return angle < halfFOV;
    }

    // ── Храбрость: courage 0-100 + morale 0-100 + stress 0-100 ───────────────
    private bool IsBrave()
    {
        if (core.Psychology == null) return true;

        float courage = core.Psychology.GetCourageStat();   // 0-100
        float morale  = core.Psychology.GetMorale();         // 0-100
        float stress  = core.Psychology.GetStress();         // 0-100

        // Смелость = высокое мужество + высокий моральный дух - большой стресс
        float braveScore = (courage / 100f) * 0.5f
                         + (morale  / 100f) * 0.3f
                         - (stress  / 100f) * 0.2f;

        return braveScore > 0.4f;
    }

    // ── Главная точка входа ───────────────────────────────────────────────────
    /// <summary>
    /// Тактическая реакция на звук угрозы.
    /// Вызывается только когда игрок НЕ смотрит на этого бота.
    /// Смелый — атакует/фланкирует. Трус — прячется и засаживает из засады.
    /// Все боты — меняют позицию и перестают патрулировать.
    /// </summary>
    private void ReactToThreatSound(Vector3 soundPosition)
    {
        // Кулдаун — реагируем не чаще раза в N секунд
        if (Time.time - lastTacticalReactionTime < TacticalReactionCooldown) return;
        lastTacticalReactionTime = Time.time;

        // Прекратить патрулирование: пометить как тревогу
        core.Blackboard.SetBool(BlackboardKey.IsAlert, true);

        // Поднять оружие в боевую готовность
        core.WeaponController?.RaiseWeapon();

        if (IsBrave())
            ReactBrave(soundPosition);
        else
            ReactCoward(soundPosition);
    }

    // ── Смелый бот ────────────────────────────────────────────────────────────
    private void ReactBrave(Vector3 soundPosition)
    {
        // Получить роль из Blackboard, если есть
        SquadRole role = core.Blackboard.Get(BlackboardKey.SquadRole, SquadRole.Support);

        // Flanker и Leader агрессивно атакуют со спины/сбоку
        // Support и Suppressor могут занять огневую позицию с преимуществом
        bool useFlanking = (role == SquadRole.Flanker) ||
                           (role == SquadRole.Leader  && Random.value > 0.4f) ||
                           (role == SquadRole.Support  && Random.value > 0.7f);

        Vector3 targetPos;

        if (useFlanking)
        {
            targetPos = GetFlankPosition(soundPosition);
            if (showDebugLogs)
                Debug.Log($"[Perception/Tactics] {core.name} - BRAVE → FLANK from {soundPosition}");
        }
        else
        {
            // Продвинуться прямо к угрозе
            Vector3 toSound = (soundPosition - core.Transform.position).normalized;
            toSound.y = 0;
            Vector3 advance = core.Transform.position + toSound * Random.Range(8f, 15f);

            NavMeshHit hit;
            targetPos = NavMesh.SamplePosition(advance, out hit, 5f, NavMesh.AllAreas)
                ? hit.position
                : Vector3.zero;

            if (showDebugLogs)
                Debug.Log($"[Perception/Tactics] {core.name} - BRAVE → ADVANCE toward {soundPosition}");
        }

        if (targetPos != Vector3.zero)
            core.Movement?.MoveToPosition(targetPos, core.Agent.speed);
    }

    // ── Трусливый бот ─────────────────────────────────────────────────────────
    private void ReactCoward(Vector3 soundPosition)
    {
        // Попытаться найти укрытие с хорошим сектором обстрела
        Vector3 ambushPos = GetAmbushPosition(soundPosition);

        if (ambushPos != Vector3.zero)
        {
            core.Movement?.MoveToPosition(ambushPos, core.Agent.speed);
            core.Blackboard.SetBool(BlackboardKey.IsAmbushing, true);

            if (showDebugLogs)
                Debug.Log($"[Perception/Tactics] {core.name} - COWARD → AMBUSH at {ambushPos}");
        }
        else
        {
            // Запасной вариант: стандартный поиск укрытия
            core.Movement?.SeekCover();

            if (showDebugLogs)
                Debug.Log($"[Perception/Tactics] {core.name} - COWARD → SEEK COVER");
        }
    }

    // ── Позиция для флангового удара ──────────────────────────────────────────
    private Vector3 GetFlankPosition(Vector3 soundPos)
    {
        Vector3 toSound = (soundPos - core.Transform.position).normalized;
        toSound.y = 0;

        // Выбрать случайную сторону — левый или правый фланг
        Vector3 perp = Vector3.Cross(toSound, Vector3.up);
        if (Random.value > 0.5f) perp = -perp;

        float flankSide    = Random.Range(6f, 12f);
        float flankForward = Random.Range(5f, 12f);

        Vector3 target = core.Transform.position + toSound * flankForward + perp * flankSide;

        NavMeshHit hit;
        if (NavMesh.SamplePosition(target, out hit, 5f, NavMesh.AllAreas))
            return hit.position;

        // Запасной: просто вперёд
        target = core.Transform.position + toSound * flankForward;
        if (NavMesh.SamplePosition(target, out hit, 5f, NavMesh.AllAreas))
            return hit.position;

        return Vector3.zero;
    }

    // ── Позиция засады (от угрозы, за укрытием) ───────────────────────────────
    private Vector3 GetAmbushPosition(Vector3 soundPos)
    {
        Vector3 awayFromThreat = (core.Transform.position - soundPos).normalized;
        awayFromThreat.y = 0;

        float[] distances  = { 10f, 14f, 8f };
        float[] sideAngles = { 0f, 40f, -40f, 80f, -80f };

        foreach (float dist in distances)
        {
            foreach (float angle in sideAngles)
            {
                Vector3 dir       = Quaternion.Euler(0, angle, 0) * awayFromThreat;
                Vector3 candidate = core.Transform.position + dir * dist;

                NavMeshHit hit;
                if (!NavMesh.SamplePosition(candidate, out hit, 5f, NavMesh.AllAreas)) continue;

                // Проверить наличие укрытия между позицией и угрозой
                Vector3 toThreat = (soundPos - hit.position + Vector3.up * 1f).normalized;
                float   rayDist  = Vector3.Distance(hit.position, soundPos);

                if (Physics.Raycast(hit.position + Vector3.up * 1f, toThreat, rayDist, obstacleLayers))
                    return hit.position; // Позиция за укрытием — идеальная засада
            }
        }

        // Запасной: просто отступить
        Vector3 fallback = core.Transform.position + awayFromThreat * 10f;
        NavMeshHit fbHit;
        if (NavMesh.SamplePosition(fallback, out fbHit, 5f, NavMesh.AllAreas))
            return fbHit.position;

        return Vector3.zero;
    }

    #endregion

    private void OnDrawGizmos()
    {
        if (!showGizmos || core == null) return;

        Vector3 eyePos = core.Transform.position + Vector3.up * 1.6f;

        // Central vision
        Gizmos.color = currentTarget != null ? Color.red : new Color(1f, 1f, 0f, 0.3f);
        Gizmos.DrawWireSphere(eyePos, visionRange);

        Vector3 leftCentral = Quaternion.Euler(0, -visionAngle * 0.5f, 0) * core.Transform.forward * visionRange;
        Vector3 rightCentral = Quaternion.Euler(0, visionAngle * 0.5f, 0) * core.Transform.forward * visionRange;

        Gizmos.DrawLine(eyePos, eyePos + leftCentral);
        Gizmos.DrawLine(eyePos, eyePos + rightCentral);

        // Peripheral vision
        Gizmos.color = new Color(0f, 1f, 1f, 0.2f);
        Gizmos.DrawWireSphere(eyePos, peripheralRange);

        Vector3 leftPeripheral = Quaternion.Euler(0, -peripheralAngle * 0.5f, 0) * core.Transform.forward * peripheralRange;
        Vector3 rightPeripheral = Quaternion.Euler(0, peripheralAngle * 0.5f, 0) * core.Transform.forward * peripheralRange;

        Gizmos.DrawLine(eyePos, eyePos + leftPeripheral);
        Gizmos.DrawLine(eyePos, eyePos + rightPeripheral);

        // Current target
        if (currentTarget != null)
        {
            Gizmos.color = Color.red;
            Gizmos.DrawLine(eyePos, currentTarget.position + Vector3.up * 1.0f);
        }

        // Alertness
        if (Application.isPlaying && alertness > 0.1f)
        {
            Gizmos.color = Color.Lerp(Color.yellow, Color.red, alertness);
            Gizmos.DrawWireSphere(core.Transform.position + Vector3.up * 2.5f, 0.3f * alertness);
        }
    }
}