using UnityEngine;

/// <summary>
/// REACTIVE LAYER - ВСЕГДА БДИТЕЛЕН
/// Реагирует на ВСЁ:  звуки, движение, давление, выстрелы
/// </summary>
public class AIReactiveCombatLayer : MonoBehaviour
{
    [Header("Settings")]
    [SerializeField] private float reactionTime = 0.05f; // Почти мгновенно
    [SerializeField] private bool enableInstantReaction = true;

    [Header("Ambush Alertness")]
    [SerializeField] private bool stayAlertInAmbush = true; // NEW: Бдительность в засаде
    [SerializeField] private float ambushScanInterval = 0.5f; // Сканирование в засаде

    [Header("Debug")]
    [SerializeField] private bool showDebugLogs = false;

    private SmartEnemyAI core;
    private Transform currentThreat = null;
    private bool isReacting = false;
    private float lastAmbushScanTime = 0f;

    public void Initialize(SmartEnemyAI coreAI)
    {
        core = coreAI;
    }

    /// <summary>
    /// ГЛАВНЫЙ МЕТОД:  Проверка рефлексов (ВСЕГДА АКТИВЕН)
    /// </summary>
    public bool ProcessReflexes()
    {
        if (core.IsDead() || core.Psychology.HasSurrendered())
        {
            return false;
        }

        currentThreat = core.Blackboard.GetTransform(BlackboardKey.CurrentThreat);

        // КРИТИЧНО: Проверка звуков ВЫСТРЕЛОВ рядом (даже в засаде!)
        if (Reflex_ReactToNearbyGunfire())
        {
            return true;
        }

        // РЕФЛЕКС 1: Видит врага → СТРЕЛЯТЬ
        if (currentThreat != null)
        {
            return Reflex_EngageVisibleThreat();
        }

        // РЕФЛЕКС 2: Получил урон → ОТВЕТНЫЙ ОГОНЬ
        if (core.Blackboard.GetBool(BlackboardKey.JustTookDamage))
        {
            return Reflex_ReturnFire();
        }

        // РЕФЛЕКС 3: Подавлен → УКРЫТИЕ
        if (core.Blackboard.GetBool(BlackboardKey.IsSuppressed))
        {
            return Reflex_GetToCover();
        }

        // РЕФЛЕКС 4: В засаде - периодическое сканирование
        if (stayAlertInAmbush && IsInAmbush())
        {
            return Reflex_AmbushScan();
        }

        // РЕФЛЕКС 5: Услышал звук → ПОВЕРНУТЬСЯ
        if (core.Blackboard.Get(BlackboardKey.ThreatLevel, ThreatLevel.None) == ThreatLevel.Suspected)
        {
            return Reflex_ReactToSound();
        }

        isReacting = false;
        return false;
    }

    #region Reflexes

    /// <summary>
    /// НОВЫЙ РЕФЛЕКС:  Реакция на выстрелы рядом (даже если не видит)
    /// </summary>
    private bool Reflex_ReactToNearbyGunfire()
    {
        var soundType = core.Blackboard.Get(BlackboardKey.LastSoundType, SoundType.None);

        if (soundType != SoundType.Gunshot) return false;

        float timeSinceSound = Time.time - core.Blackboard.GetFloat(BlackboardKey.LastSuspectedThreatTime, -999f);

        // Выстрел был недавно (меньше 1 сек)
        if (timeSinceSound > 1f) return false;

        Vector3 soundPos = core.Blackboard.GetVector3(BlackboardKey.LastHeardSoundPosition);
        float distance = Vector3.Distance(core.Transform.position, soundPos);

        // Выстрел БЛИЗКО (< 30м) → НЕМЕДЛЕННАЯ РЕАКЦИЯ
        if (distance < 30f)
        {
            // Повернуться к звуку
            Vector3 dirToSound = (soundPos - core.Transform.position).normalized;
            dirToSound.y = 0;

            if (dirToSound != Vector3.zero)
            {
                core.Movement?.SnapToDirection(dirToSound);
            }

            // Поднять оружие
            core.WeaponController?.RaiseWeapon();

            // Установить предполагаемую позицию угрозы
            core.Blackboard.SetVector3(BlackboardKey.PredictedThreatPosition, soundPos);
            core.Blackboard.Set(BlackboardKey.ThreatLevel, ThreatLevel.Suspected);

            // ПОДАВЛЯЮЩИЙ ОГОНЬ в сторону звука (даже если не видим)
            if (distance < 20f) // Очень близко → стрелять
            {
                core.Combat?.SuppressPosition(soundPos);

                if (showDebugLogs)
                    Debug.Log($"[Reactive] {core.name} - SUPPRESSING gunfire source at {distance: F1}m");
            }

            isReacting = true;
            return true;
        }

        return false;
    }

    /// <summary>
    /// РЕФЛЕКС: Видит врага → Решение атаковать или подумать
    /// УЛУЧШЕНО: Учитывает психологическое состояние перед атакой
    /// </summary>
    private bool Reflex_EngageVisibleThreat()
    {
        if (!enableInstantReaction) return false;

        bool hasLOS = core.LineOfSight != null && core.LineOfSight.HasLineOfSight(currentThreat);

        if (hasLOS)
        {
            // Установить видимую угрозу в blackboard
            core.Blackboard.SetTransform(BlackboardKey.CurrentThreat, currentThreat);
            core.Blackboard.SetVector3(BlackboardKey.LastKnownThreatPosition, currentThreat.position);
            core.Blackboard.SetFloat(BlackboardKey.LastSeenThreatTime, Time.time);
            core.Blackboard.Set(BlackboardKey.ThreatLevel, ThreatLevel.Confirmed);

            // Повернуться мгновенно к врагу
            Vector3 dirToThreat = (currentThreat.position - core.Transform.position).normalized;
            dirToThreat.y = 0;

            if (dirToThreat != Vector3.zero)
            {
                core.Movement?.SnapToDirection(dirToThreat);
            }

            // УЛУЧШЕНО: Проверить психологическую готовность к бою
            bool inCombat = core.Blackboard.GetBool(BlackboardKey.InCombat, false);
            bool isReadyToFight = core.Psychology != null && core.Psychology.IsReadyToFight();

            // СЛУЧАЙ 1: УЖЕ в боевом состоянии И психологически готов → СРАЗУ СТРЕЛЯТЬ
            if (inCombat && isReadyToFight)
            {
                core.WeaponController?.RaiseWeapon();
                core.Combat?.EngageTarget(currentThreat);
                core.Blackboard.SetBool(BlackboardKey.InCombat, true);

                isReacting = true;

                if (showDebugLogs && Time.frameCount % 60 == 0)
                {
                    Debug.Log($"[Reactive] {core.name} - ENGAGING visible threat (in combat)");
                }

                return true;
            }

            // СЛУЧАЙ 2: НЕ в боевом состоянии ИЛИ психологически НЕ готов → РАЗДУМЬЯ
            if (!inCombat || !isReadyToFight)
            {
                // Поднять оружие но не стрелять
                core.WeaponController?.RaiseWeapon();

                // Запустить процесс принятия решения (если ещё не идёт)
                if (core.SurrenderDecision != null && !core.SurrenderDecision.IsDeciding())
                {
                    core.SurrenderDecision.StartDecision();

                    if (showDebugLogs)
                    {
                        Debug.Log($"[Reactive] {core.name} - Detected threat, starting decision process. InCombat: {inCombat}, Ready: {isReadyToFight}");
                    }
                }

                // Смотреть на врага пока раздумываем
                core.Movement?.SetLookTarget(dirToThreat, RotationMode.Combat);
                core.Blackboard.SetBool(BlackboardKey.InCombat, false); // Ещё не в боевом состоянии

                isReacting = true;
                return true;
            }

            return false;
        }
        else
        {
            // Нет LOS - УЛУЧШЕНО: держать фокус дольше
            Vector3 lastKnownPos = core.Blackboard.GetVector3(BlackboardKey.LastKnownThreatPosition);

            if (lastKnownPos != Vector3.zero)
            {
                float timeSinceSeen = Time.time - core.Blackboard.GetFloat(BlackboardKey.LastSeenThreatTime, -999f);

                // УЛУЧШЕНО: Держать фокус до 10 сек (было 3)
                if (timeSinceSeen < 10f)
                {
                    // Смотреть в сторону последней позиции
                    Vector3 dirToLastKnown = (lastKnownPos - core.Transform.position).normalized;
                    dirToLastKnown.y = 0;
                    
                    if (dirToLastKnown != Vector3.zero)
                    {
                        core.Movement?.SetLookTarget(dirToLastKnown, RotationMode.Combat);
                    }

                    core.WeaponController?.RaiseWeapon();
                    
                    // Первые 3 сек - подавляющий огонь
                    if (timeSinceSeen < 3f)
                    {
                        core.Combat?.SuppressPosition(lastKnownPos);
                    }
                    // 3-10 сек - ждать и целиться
                    else
                    {
                        // Не стрелять но держать прицел
                        core.Agent.isStopped = true;
                    }

                    isReacting = true;

                    if (showDebugLogs && Time.frameCount % 60 == 0)
                    {
                        Debug.Log($"[Reactive] {core.name} - Holding position, watching last known ({timeSinceSeen:F1}s ago)");
                    }

                    return true;
                }
            }
        }

        return false;
    }

    /// <summary>
    /// РЕФЛЕКС: Получил урон → Ответный огонь
    /// </summary>
    private bool Reflex_ReturnFire()
    {
        Vector3 predictedAttackerPos = core.Blackboard.GetVector3(BlackboardKey.PredictedThreatPosition);

        if (predictedAttackerPos != Vector3.zero)
        {
            // Мгновенный поворот
            Vector3 dirToAttacker = (predictedAttackerPos - core.Transform.position).normalized;
            dirToAttacker.y = 0;

            if (dirToAttacker != Vector3.zero)
            {
                core.Movement?.SnapToDirection(dirToAttacker);
            }

            core.WeaponController?.RaiseWeapon();
            core.Combat?.SuppressPosition(predictedAttackerPos);

            core.Blackboard.SetBool(BlackboardKey.JustTookDamage, false);

            isReacting = true;

            if (showDebugLogs)
            {
                Debug.Log($"[Reactive] {core.name} - RETURN FIRE!");
            }

            return true;
        }

        return false;
    }

    /// <summary>
    /// РЕФЛЕКС: Услышал звук → Повернуться и готовиться
    /// </summary>
    private bool Reflex_ReactToSound()
    {
        Vector3 soundPos = core.Blackboard.GetVector3(BlackboardKey.LastHeardSoundPosition);

        if (soundPos != Vector3.zero)
        {
            Vector3 dirToSound = (soundPos - core.Transform.position).normalized;
            dirToSound.y = 0;

            if (dirToSound != Vector3.zero)
            {
                core.Movement?.SetLookTarget(dirToSound, RotationMode.Combat);
            }

            core.WeaponController?.RaiseWeapon();

            // НЕ блокируем Utility AI - пусть решит что делать дальше
            return false;
        }

        return false;
    }

    /// <summary>
    /// РЕФЛЕКС:  Подавлен → В укрытие
    /// </summary>
    private bool Reflex_GetToCover()
    {
        if (core.Blackboard.GetBool(BlackboardKey.IsInCover))
        {
            return true;
        }

        core.Movement?.SeekCover();

        isReacting = true;
        return true;
    }

    /// <summary>
    /// НОВЫЙ РЕФЛЕКС:  Сканирование в засаде
    /// Периодически оглядывается, проверяет звуки
    /// </summary>
    private bool Reflex_AmbushScan()
    {
        if (Time.time < lastAmbushScanTime + ambushScanInterval)
        {
            return false; // Еще рано сканировать
        }

        lastAmbushScanTime = Time.time;

        // Медленное оглядывание (не застывание!)
        float currentYaw = core.Transform.eulerAngles.y;
        float scanYaw = currentYaw + Random.Range(-30f, 30f);

        Vector3 scanDir = Quaternion.Euler(0, scanYaw, 0) * Vector3.forward;
        core.Movement?.SetLookTarget(scanDir, RotationMode.Idle, 60f);

        // Проверка звуков
        var threatLevel = core.Blackboard.Get(BlackboardKey.ThreatLevel, ThreatLevel.None);

        if (threatLevel == ThreatLevel.Suspected)
        {
            // Услышал что-то подозрительное → выйти из засады
            if (showDebugLogs)
            {
                Debug.Log($"[Reactive] {core.name} - Ambush alert!   Investigating");
            }

            return false; // Передать управление Utility AI
        }

        // Продолжаем засаду, но бдим
        return false;
    }

    #endregion

    #region Helpers

    private bool IsInAmbush()
    {
        // Проверка - в режиме засады или ожидания
        // Можно добавить флаг в Blackboard
        var threatLevel = core.Blackboard.Get(BlackboardKey.ThreatLevel, ThreatLevel.None);
        return threatLevel == ThreatLevel.Suspected;
    }

    #endregion

    public bool IsReacting() => isReacting;
}