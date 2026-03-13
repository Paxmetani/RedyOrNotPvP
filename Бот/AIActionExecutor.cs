using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Роли в Squad
/// </summary>
public enum SquadRole
{
    Leader,        // Ведёт команду, принимает решения, наступает
    Suppressor,    // Подавляющий огонь, сдерживает врага, стоит и давит
    Flanker,       // Фланговый маневр, атака с неожиданной стороны, обход
    Support        // Поддержка, прикрывает, помогает
}

/// <summary>
/// MODULE:  Action Executor
/// Выполнение агрессивных действий
/// </summary>
public class AIActionExecutor : MonoBehaviour
{
    private SmartEnemyAI core;
    private AIAction currentAction = AIAction.Patrol;

    // Ambush state
    private Vector3 ambushPosition;
    private bool isInAmbush = false;
    private float ambushStartTime = 0f;

    // Shoot and move state
    private Vector3 moveTarget;
    private float nextMoveTime = 0f;

    //Role
    [SerializeField] private float roleReassignInterval = 5f;
    [SerializeField] private bool enableRoles = true;

    private AISquadManager squadManager;
    private float nextRoleAssignTime = 0f;

    public void Initialize(SmartEnemyAI coreAI)
    {
        core = coreAI;
    }

    private void Update()
    {
        if (!enableRoles || squadManager == null) return;

        if (Time.time >= nextRoleAssignTime)
        {
            nextRoleAssignTime = Time.time + roleReassignInterval;
            AssignSquadRoles();
        }
    }
    // Role Assign - УЛУЧШЕНО: Рандомное распределение ролей
    private void AssignSquadRoles()
    {
        // Получить список активных AI
        var allAI = GetAllRegisteredAI();

        if (allAI.Count == 0) return;

        // Очистить старые роли
        foreach (var ai in allAI)
        {
            if (ai == null || ai.IsDead()) continue;
            ai.Blackboard.Set(BlackboardKey.SquadRole, SquadRole.Support);
        }

        // НОВОЕ: Перемешать список для рандомного распределения
        ShuffleList(allAI);

        // Распределить роли
        if (allAI.Count >= 1)
        {
            AssignRole(allAI[0], SquadRole.Leader);
        }

        if (allAI.Count >= 2)
        {
            AssignRole(allAI[1], SquadRole.Suppressor);
        }

        if (allAI.Count >= 3)
        {
            AssignRole(allAI[2], SquadRole.Flanker);
        }

        if (allAI.Count >= 4)
        {
            AssignRole(allAI[3], SquadRole.Support);
        }

        // Остальные - рандом между Support и Flanker
        for (int i = 4; i < allAI.Count; i++)
        {
            SquadRole randomRole = Random.value > 0.5f ? SquadRole.Support : SquadRole.Flanker;
            AssignRole(allAI[i], randomRole);
        }
    }

    /// <summary>
    /// НОВОЕ: Перемешать список (Fisher-Yates shuffle)
    /// </summary>
    private void ShuffleList<T>(System.Collections.Generic.List<T> list)
    {
        for (int i = list.Count - 1; i > 0; i--)
        {
            int j = Random.Range(0, i + 1);
            T temp = list[i];
            list[i] = list[j];
            list[j] = temp;
        }
    }

    /// <summary>
    /// Назначить роль врагу
    /// </summary>
    private void AssignRole(SmartEnemyAI ai, SquadRole role)
    {
        if (ai == null || ai.IsDead()) return;

        ai.Blackboard.Set(BlackboardKey.SquadRole, role);

        Debug.Log($"[SquadRoles] {ai.name} assigned role: {role}");
    }

    /// <summary>
    /// Получить список всех зарегистрированных AI
    /// </summary>
    private List<SmartEnemyAI> GetAllRegisteredAI()
    {
        // Получить через reflection (для доступа к private field registeredAI)
        var field = typeof(AISquadManager).GetField("registeredAI",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

        if (field != null)
        {
            return (List<SmartEnemyAI>)field.GetValue(squadManager);
        }

        return new List<SmartEnemyAI>();
    }
    //ActionExecute
    public void ExecuteAction(AIAction action)
    {
        if (currentAction != action)
        {
            OnActionChanged(currentAction, action);
            currentAction = action;
        }

        // ========== ПРИМЕНИТЬ РОЛЬ-СПЕЦИФИЧНОЕ ПОВЕДЕНИЕ ==========
        ExecuteRoleAction(action);
    }

    /// <summary>
    /// НОВОЕ: Выполнить действие с учётом роли в Squad
    /// </summary>
    private void ExecuteRoleAction(AIAction action)
    {
        var role = core.Blackboard.Get(BlackboardKey.SquadRole, SquadRole.Support);

        switch (role)
        {
            case SquadRole.Leader:
                ExecuteLeaderAction(action);
                break;
            case SquadRole.Suppressor:
                ExecuteSuppressorAction(action);
                break;
            case SquadRole.Flanker:
                ExecuteFlankerAction(action);
                break;
            case SquadRole.Support:
            default:
                ExecuteDefaultAction(action);
                break;
        }
    }

    /// <summary>
    /// НОВОЕ: Лидер командует - наступает прямо
    /// </summary>
    private void ExecuteLeaderAction(AIAction action)
    {
        switch (action)
        {
            case AIAction.Patrol:
                core.Movement?.Patrol();
                core.WeaponController?.LowerWeapon();
                break;

            case AIAction.HoldPosition:
                core.Agent.isStopped = true;
                core.WeaponController?.RaiseWeapon();
                break;

            case AIAction.Ambush:
                Action_Ambush();
                break;

            case AIAction.InvestigateThreat:
                Vector3 threatPos = core.Blackboard.GetVector3(BlackboardKey.PredictedThreatPosition);
                core.Movement?.MoveToPosition(threatPos, moveSpeed: 3f);
                core.WeaponController?.RaiseWeapon();
                break;

            case AIAction.Retreat:
                Action_Retreat();
                break;
        }
    }

    /// <summary>
    /// НОВОЕ: Suppressor - подавляющий огонь
    /// Остаётся на месте и давит огнём
    /// </summary>
    private void ExecuteSuppressorAction(AIAction action)
    {
        Transform threat = core.Blackboard.GetTransform(BlackboardKey.CurrentThreat);

        switch (action)
        {
            case AIAction.Patrol:
                core.Movement?.Patrol();
                core.WeaponController?.LowerWeapon();
                break;

            case AIAction.HoldPosition:
                if (threat != null)
                {
                    // Подавляющий огонь - стой и стреляй
                    core.Agent.isStopped = true;
                    core.WeaponController?.RaiseWeapon();
                    core.Combat?.EngageTarget(threat);
                }
                else
                {
                    core.Agent.isStopped = true;
                    core.WeaponController?.RaiseWeapon();
                }
                break;

            case AIAction.Ambush:
                Action_Ambush_Suppressor(); // Специальная засада для suppressor
                break;

            case AIAction.InvestigateThreat:
                if (threat != null)
                {
                    // Двигаться но давить огнём
                    core.Movement?.StrafeWhileShooting(threat);
                    core.WeaponController?.RaiseWeapon();
                    core.Combat?.EngageTarget(threat);
                }
                break;
        }
    }

    /// <summary>
    /// НОВОЕ: Flanker - фланговый маневр
    /// Обходит с боку/сзади, стелс подход
    /// </summary>
    private void ExecuteFlankerAction(AIAction action)
    {
        Transform threat = core.Blackboard.GetTransform(BlackboardKey.CurrentThreat);

        switch (action)
        {
            case AIAction.Patrol:
                core.Movement?.Patrol();
                core.WeaponController?.LowerWeapon();
                break;

            case AIAction.HoldPosition:
                // Flanker ждёт но с оружием
                core.Agent.isStopped = true;
                core.WeaponController?.RaiseWeapon();
                break;

            case AIAction.Ambush:
                Action_Ambush_Flanker(); // Специальная засада для flanker
                break;

            case AIAction.InvestigateThreat:
                // Flanker обходит с боку
                if (threat != null)
                {
                    Vector3 threatPos = threat.position;
                    Vector3 dirToThreat = (threatPos - core.Transform.position).normalized;
                    Vector3 flankPos = threatPos + Vector3.Cross(dirToThreat, Vector3.up) * 5f; // Обход в сторону
                    
                    core.Movement?.MoveToPosition(flankPos, moveSpeed: 2f); // Тихое приближение
                    core.WeaponController?.RaiseWeapon();
                }
                break;
        }
    }

    /// <summary>
    /// НОВОЕ: Стандартное действие
    /// </summary>
    private void ExecuteDefaultAction(AIAction action)
    {
        switch (action)
        {
            case AIAction.Patrol:
                core.Movement?.Patrol();
                core.WeaponController?.LowerWeapon();
                break;

            case AIAction.HoldPosition:
                Action_HoldPosition();
                break;

            case AIAction.Ambush:
                Action_Ambush();
                break;

            case AIAction.InvestigateThreat:
                Vector3 threatPos = core.Blackboard.GetVector3(BlackboardKey.PredictedThreatPosition);
                core.Movement?.MoveToPosition(threatPos, moveSpeed: 2f);
                core.WeaponController?.RaiseWeapon();
                break;

            case AIAction.Retreat:
                Action_Retreat();
                break;
        }
    }

    #region Actions

    private void Action_AggressiveEngage()
    {
        Transform threat = core.Blackboard.GetTransform(BlackboardKey.CurrentThreat);
        if (threat == null) return;

        // НЕМЕДЛЕННО поднять оружие
        core.WeaponController?.RaiseWeapon();

        // Стрелять (с мгновенным поворотом)
        core.Combat?.EngageTarget(threat);
    }

    private void Action_ShootAndMove()
    {
        Transform threat = core.Blackboard.GetTransform(BlackboardKey.CurrentThreat);
        if (threat == null) return;

        core.WeaponController?.RaiseWeapon();

        // Страф
        core.Movement?.StrafeWhileShooting(threat);

        // Стрелять (с поворотом тела)
        core.Combat?.EngageTarget(threat);
    }
    private void Action_HoldPosition()
    {
        // Остановиться и ждать с поднятым оружием
        core.Agent.isStopped = true;
        core.WeaponController?.RaiseWeapon();

        // Медленно поворачиваться, сканируя область
        core.Transform.Rotate(Vector3.up, 20f * Time.deltaTime);
    }

    private void Action_Ambush()
    {
        if (!isInAmbush)
        {
            // ========== ИНИЦИАЛИЗАЦИЯ ЗАСАДЫ ==========
            
            // Получить позицию угрозы
            Vector3 threatOrigin = Vector3.zero;
            
            if (core.Blackboard.Has(BlackboardKey.ThreatOrigin))
            {
                threatOrigin = core.Blackboard.GetVector3(BlackboardKey.ThreatOrigin);
            }
            
            // Если нет точной позиции - используем Last Heard
            if (threatOrigin == Vector3.zero)
            {
                if (core.Blackboard.Has(BlackboardKey.LastHeardSoundPosition))
                {
                    threatOrigin = core.Blackboard.GetVector3(BlackboardKey.LastHeardSoundPosition);
                }
            }

            // Найти хорошее укрытие для засады
            ambushPosition = FindAmbushPosition(threatOrigin);

            if (ambushPosition != Vector3.zero)
            {
                // Начать движение к позиции засады
                core.Agent.SetDestination(ambushPosition);
                core.Agent.speed = 2f;
                isInAmbush = true;
                ambushStartTime = Time.time;

                // Указать что ждем угрозу
                core.Blackboard.SetBool(BlackboardKey.IsAmbushing, true);

                Debug.Log($"[Ambush] {core.name} setting up ambush at {ambushPosition}, threat from {threatOrigin}");
            }
        }
        else
        {
            // ========== В ПОЗИЦИИ ЗАСАДЫ - ЖДЁМ УГРОЗУ ==========
            
            float distance = Vector3.Distance(core.Transform.position, ambushPosition);

            if (distance < 1f)
            {
                // Достигли позиции засады - готовимся
                core.Agent.isStopped = true;
                core.WeaponController?.RaiseWeapon();

                // Смотреть в сторону предполагаемой угрозы
                Vector3 threatOrigin = Vector3.zero;
                
                if (core.Blackboard.Has(BlackboardKey.ThreatOrigin))
                {
                    threatOrigin = core.Blackboard.GetVector3(BlackboardKey.ThreatOrigin);
                }
                
                if (threatOrigin == Vector3.zero && core.Blackboard.Has(BlackboardKey.LastHeardSoundPosition))
                {
                    threatOrigin = core.Blackboard.GetVector3(BlackboardKey.LastHeardSoundPosition);
                }

                Vector3 dirToThreat = (threatOrigin - core.Transform.position).normalized;
                dirToThreat.y = 0;

                if (dirToThreat != Vector3.zero)
                {
                    Quaternion targetRot = Quaternion.LookRotation(dirToThreat);
                    core.Transform.rotation = Quaternion.Slerp(core.Transform.rotation, targetRot, Time.deltaTime * 2f);
                }

                // Активно слушать угрозу
                ListenForThreat();
                
                // Проверить истёк ли Alert timeout
                float timeSinceAlert = Time.time - core.Blackboard.GetFloat(BlackboardKey.AlertStartTime, -999f);
                if (timeSinceAlert > 60f) // 60 сек - Alert заканчивается
                {
                    core.Blackboard.SetBool(BlackboardKey.IsAlert, false);
                    core.Blackboard.SetBool(BlackboardKey.IsAmbushing, false);
                }
            }
        }
    }

    /// <summary>
    /// НОВОЕ: Активно слушать звуки угрозы
    /// Повышает готовность системы восприятия
    /// </summary>
    private void ListenForThreat()
    {
        // Увеличить чувствительность слуха во время засады
        // (в реальной игре это может быть связано с AnimationState или специальным режимом)
        
        if (core.Perception != null)
        {
            // Perception уже активно слушает в Alert State
            // Но можно добавить визуальные/аудио подсказки
            
            // Проверить услышал ли что-нибудь подозрительное
            var lastSoundType = core.Blackboard.Get(BlackboardKey.AlertSoundType, SoundType.None);
            
            if (lastSoundType != SoundType.None)
            {
                // Напряжение - готов к атаке
                core.Psychology?.AddStress(1f); // Небольшое напряжение во время ожидания
            }
        }
    }

    /// <summary>
    /// НОВОЕ: Специальная засада для Suppressor
    /// Стоит на месте и подавляет огнём
    /// </summary>
    private void Action_Ambush_Suppressor()
    {
        // Suppressor не движется - встаёт и давит огнём
        core.Agent.isStopped = true;
        core.WeaponController?.RaiseWeapon();

        // Смотреть в сторону угрозы
        Vector3 threatOrigin = Vector3.zero;
        
        if (core.Blackboard.Has(BlackboardKey.ThreatOrigin))
        {
            threatOrigin = core.Blackboard.GetVector3(BlackboardKey.ThreatOrigin);
        }
        
        if (threatOrigin == Vector3.zero && core.Blackboard.Has(BlackboardKey.LastHeardSoundPosition))
        {
            threatOrigin = core.Blackboard.GetVector3(BlackboardKey.LastHeardSoundPosition);
        }

        if (threatOrigin != Vector3.zero)
        {
            Vector3 dirToThreat = (threatOrigin - core.Transform.position).normalized;
            Quaternion targetRot = Quaternion.LookRotation(dirToThreat);
            core.Transform.rotation = Quaternion.Slerp(core.Transform.rotation, targetRot, Time.deltaTime * 2f);
        }

        // Suppressor давит огнём
        if (core.Combat != null)
        {
            core.Combat.SuppressPosition(threatOrigin);
        }

        core.Blackboard.SetBool(BlackboardKey.IsAmbushing, true);
    }

    /// <summary>
    /// НОВОЕ: Специальная засада для Flanker
    /// Обходит и занимает позицию сбоку/сзади
    /// </summary>
    private void Action_Ambush_Flanker()
    {
        if (!isInAmbush)
        {
            // Flanker должен обойти угрозу
            Vector3 threatOrigin = Vector3.zero;
            
            if (core.Blackboard.Has(BlackboardKey.ThreatOrigin))
            {
                threatOrigin = core.Blackboard.GetVector3(BlackboardKey.ThreatOrigin);
            }
            
            if (threatOrigin == Vector3.zero)
            {
                if (core.Blackboard.Has(BlackboardKey.LastHeardSoundPosition))
                {
                    threatOrigin = core.Blackboard.GetVector3(BlackboardKey.LastHeardSoundPosition);
                }
            }

            // Вычислить фланговую позицию (в сторону и назад)
            Vector3 dirToThreat = (threatOrigin - core.Transform.position).normalized;
            Vector3 rightFlank = Vector3.Cross(dirToThreat, Vector3.up).normalized;
            
            // Выбрать случайную сторону для фланга
            if (Random.value > 0.5f)
            {
                rightFlank = -rightFlank;
            }

            // Позиция фланга - в сторону и назад
            Vector3 flankAmbushPos = threatOrigin + rightFlank * 8f - dirToThreat * 3f;

            UnityEngine.AI.NavMeshHit hit;
            if (UnityEngine.AI.NavMesh.SamplePosition(flankAmbushPos, out hit, 10f, UnityEngine.AI.NavMesh.AllAreas))
            {
                ambushPosition = hit.position;
                core.Agent.SetDestination(ambushPosition);
                core.Agent.speed = 2f;
                isInAmbush = true;
                ambushStartTime = Time.time;

                core.Blackboard.SetBool(BlackboardKey.IsAmbushing, true);

                Debug.Log($"[Ambush-Flanker] {core.name} flanking to {ambushPosition}");
            }
        }
        else
        {
            // Flanker в позиции - ждёт и слушает
            float distance = Vector3.Distance(core.Transform.position, ambushPosition);

            if (distance < 1f)
            {
                core.Agent.isStopped = true;
                core.WeaponController?.RaiseWeapon();

                // Смотреть на основное направление
                Vector3 threatOrigin = Vector3.zero;
                
                if (core.Blackboard.Has(BlackboardKey.ThreatOrigin))
                {
                    threatOrigin = core.Blackboard.GetVector3(BlackboardKey.ThreatOrigin);
                }
                
                if (threatOrigin == Vector3.zero && core.Blackboard.Has(BlackboardKey.LastHeardSoundPosition))
                {
                    threatOrigin = core.Blackboard.GetVector3(BlackboardKey.LastHeardSoundPosition);
                }

                Vector3 dirToThreat = (threatOrigin - core.Transform.position).normalized;
                Quaternion targetRot = Quaternion.LookRotation(dirToThreat);
                core.Transform.rotation = Quaternion.Slerp(core.Transform.rotation, targetRot, Time.deltaTime * 2f);

                ListenForThreat();
            }
        }
    }

    private void Action_Suppress()
    {
        Vector3 targetPos = core.Blackboard.GetVector3(BlackboardKey.LastKnownThreatPosition);

        core.Agent.isStopped = true;
        core.WeaponController?.RaiseWeapon();

        // Стрелять в последнюю известную позицию
        core.Combat?.SuppressPosition(targetPos);
    }

    private void Action_StealthApproach()
    {
        // Тихое сближение к предполагаемой позиции угрозы
        Vector3 threatPos = core.Blackboard.GetVector3(BlackboardKey.PredictedThreatPosition);
        if (threatPos == Vector3.zero) return;

        // Идём медленно, с опущенным оружием, пока не подойдём достаточно близко
        float distance = Vector3.Distance(core.Transform.position, threatPos);

        if (distance > 5f)
        {
            core.WeaponController?.LowerWeapon();
            core.Movement?.MoveToPosition(threatPos, moveSpeed: 1.5f);
        }
        else
        {
            // На близкой дистанции поднимаем оружие и готовимся к бою
            core.WeaponController?.RaiseWeapon();
            core.Agent.isStopped = true;
        }
    }

    /// <summary>
    /// НОВОЕ: Тактическое отступление
    /// Бот отходит вглубь здания, ищет укрытие, закрывает двери
    /// </summary>
    private void Action_Retreat()
    {
        Vector3 threatPos = core.Blackboard.GetVector3(BlackboardKey.LastKnownThreatPosition);
        if (threatPos == Vector3.zero)
            threatPos = core.Blackboard.GetVector3(BlackboardKey.PredictedThreatPosition);

        // Направление ОТСТУПЛЕНИЯ (от угрозы)
        Vector3 retreatDirection = (core.Transform.position - threatPos).normalized;
        
        // Найти позицию для отступления (подальше от угрозы)
        Vector3 retreatTarget = core.Transform.position + retreatDirection * 10f;

        UnityEngine.AI.NavMeshHit hit;
        if (UnityEngine.AI.NavMesh.SamplePosition(retreatTarget, out hit, 15f, UnityEngine.AI.NavMesh.AllAreas))
        {
            core.Agent.isStopped = false;
            core.Agent.speed = 4f;  // Быстро отступать
            core.Agent.SetDestination(hit.position);
            
            // Опустить оружие для быстрого бега
            core.WeaponController?.LowerWeapon();
        }

        // После отступления - искать укрытие
        float distanceFromThreat = Vector3.Distance(core.Transform.position, threatPos);
        if (distanceFromThreat > 8f)
        {
            // Достаточно далеко - искать укрытие
            core.Movement?.SeekCover();
        }
    }

    #endregion

    #region Helpers

    private void PickRandomMovePosition()
    {
        // Случайная позиция в стороне (страфинг)
        Vector3 randomDir = Random.insideUnitCircle.normalized;
        Vector3 offset = new Vector3(randomDir.x, 0, randomDir.y) * Random.Range(3f, 5f);
        Vector3 targetPos = core.Transform.position + offset;

        UnityEngine.AI.NavMeshHit hit;
        if (UnityEngine.AI.NavMesh.SamplePosition(targetPos, out hit, 5f, UnityEngine.AI.NavMesh.AllAreas))
        {
            moveTarget = hit.position;
        }
    }

    private Vector3 FindAmbushPosition(Vector3 threatOrigin = default)
    {
        Vector3 currentPos = core.Transform.position;
        
        // Если нет позиции угрозы - использовать predicted
        if (threatOrigin == Vector3.zero)
        {
            threatOrigin = core.Blackboard.GetVector3(BlackboardKey.PredictedThreatPosition);
        }
        
        if (threatOrigin == Vector3.zero)
        {
            // Fallback
            threatOrigin = core.Transform.position + core.Transform.forward * 20f;
        }

        // ========== СТРАТЕГИЯ ЗАСАДЫ ==========
        // Позиция должна быть:
        // 1. Не на прямом пути врага
        // 2. Чтобы было прикрытие
        // 3. С хорошей линией огня
        
        Vector3 dirToThreat = (threatOrigin - currentPos).normalized;
        Vector3 perpendicular = Vector3.Cross(dirToThreat, Vector3.up).normalized;

        // Попробовать позицию слева от пути
        Vector3 ambushPosLeft = currentPos + perpendicular * Random.Range(4f, 8f);
        
        UnityEngine.AI.NavMeshHit hit;
        if (UnityEngine.AI.NavMesh.SamplePosition(ambushPosLeft, out hit, 10f, UnityEngine.AI.NavMesh.AllAreas))
        {
            return hit.position;
        }

        // Если не получилось слева - попробовать справа
        Vector3 ambushPosRight = currentPos - perpendicular * Random.Range(4f, 8f);
        if (UnityEngine.AI.NavMesh.SamplePosition(ambushPosRight, out hit, 10f, UnityEngine.AI.NavMesh.AllAreas))
        {
            return hit.position;
        }

        // Если дорога перекрыта - остаться на месте
        return currentPos;
    }

    private void OnActionChanged(AIAction from, AIAction to)
    {
        Debug.Log($"[ActionExecutor] {core.name}:  {from} → {to}");

        // Поворот теперь управляется постоянно в Combat.UpdateModule, флаг не нужен

        // Reset states
        if (from == AIAction.Ambush)
        {
            isInAmbush = false;
        }
    }

    #endregion
}