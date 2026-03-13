using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// MODULE:    Movement & Rotation
/// ������ �������� ��� ������������ � ��������� AI
/// �������� � Reactive Layer
/// </summary>
public class AIMovementModule : MonoBehaviour
{
    [Header("Movement Speeds")]
    [SerializeField] private float sweepSpeed = 1.5f;
    [SerializeField] private float walkSpeed = 2f;
    [SerializeField] private float runSpeed = 5f;

    [Header("Rotation Settings")]
    [SerializeField] private float combatRotationSpeed = 720f;
    [SerializeField] private float normalRotationSpeed = 180f;
    [SerializeField] private float idleRotationSpeed = 30f;

    [Header("Patrol Settings")]
    [SerializeField] private float patrolRadius = 15f;
    [SerializeField] private int patrolPoints = 8;
    [SerializeField] private float patrolWaitTime = 3f;

    [Header("Defensive Patrol - НОВОЕ")]
    [SerializeField] private bool useDefensivePatrol = false;  // Опция включить защитный патруль
    [SerializeField] private Vector3 spawnPoint = Vector3.zero;

    [Header("Cover Settings")]
    [SerializeField] private float coverSearchRadius = 10f;
    [SerializeField] private LayerMask obstacleLayer;

    [Header("Combat Movement")]
    [SerializeField] private bool enableCombatStrafe = true;
    [SerializeField] private float strafeSpeed = 2.5f;
    [SerializeField] private float strafeChangeInterval = 2f;

    [Header("Clustering Avoidance")]
    [SerializeField] private float minSpacingFromAllies = 2.5f;
    [SerializeField] private float spacingSearchRadius = 15f;
    [SerializeField] private float spacingForceMultiplier = 1f;
    [SerializeField] private float minObstacleDistance = 1.5f;

    [Header("Door Interaction")]
    [SerializeField] private float doorDetectionRange = 3f;
    [SerializeField] private float doorInteractionRange = 1.5f;
    [SerializeField] private float doorWaitTimeout = 5f;

    [Header("Debug")]
    [SerializeField] private bool showDebugLogs = false;

    private SmartEnemyAI core;

    // Patrol
    private List<Vector3> sweepWaypoints = new List<Vector3>();
    private int currentWaypointIndex = 0;
    private float waypointWaitTimer = 0f;
    private float sweepRotation = 0f;

    // Cover
    private Vector3 currentCoverPosition;

    // Strafe
    private Vector3 strafeDirection = Vector3.zero;
    private float nextStrafeChangeTime = 0f;

    // Rotation Control
    private RotationMode currentRotationMode = RotationMode.None;
    private Vector3 targetLookDirection = Vector3.zero;
    private float currentRotationSpeed = 180f;
    private bool combatOverride = false;

    // Clustering Avoidance
    private Vector3 spacingAvoidanceVector = Vector3.zero;
    private float nextSpacingCheckTime = 0f;

    // Door Interaction
    private DoorInteractionPoint currentDoor = null;
    private float doorInteractionTimer = 0f;
    private DoorInteractionPoint doorUsedAsCover = null;
    private DoorInteractionPoint lastPassedDoor = null;  // НОВОЕ: Последняя пройденная дверь
    private float doorPassedTime = 0f;

    public void Initialize(SmartEnemyAI coreAI)
    {
        core = coreAI;
    }

    public void UpdateModule()
    {
        UpdateCoverStatus();
        UpdateRotation();
        
        // Проверка дверей и расстояния между врагами
        UpdateDoorInteraction();
        UpdateSpacingAvoidance();
        
        // НОВОЕ: Закрыть дверь за собой
        UpdateCloseDoorBehind();
    }

    #region Close Door Behind - НОВОЕ
    
    /// <summary>
    /// НОВОЕ: Закрыть дверь за собой после прохождения
    /// </summary>
    private void UpdateCloseDoorBehind()
    {
        if (lastPassedDoor == null) return;
        
        // Подождать 1.5 сек после прохода
        if (Time.time - doorPassedTime < 1.5f) return;
        
        // Проверить что бот отошёл от двери
        float distToDoor = Vector3.Distance(core.Transform.position, lastPassedDoor.transform.position);
        
        if (distToDoor > doorDetectionRange && lastPassedDoor.IsOpen)
        {
            // Закрыть дверь за собой
            lastPassedDoor.OnAIClose(core.gameObject);
            
            if (showDebugLogs)
                Debug.Log($"[Movement] {core.name} - Closed door behind");
        }
        
        lastPassedDoor = null;
    }
    
    #endregion

    #region Rotation Control

    public void SetLookTarget(Vector3 direction, RotationMode mode, float? customSpeed = null)
    {
        direction.y = 0;
        if (direction == Vector3.zero) return;

        if (!CanOverrideRotation(mode))
        {
            return;
        }

        targetLookDirection = direction.normalized;
        currentRotationMode = mode;

        if (customSpeed.HasValue)
        {
            currentRotationSpeed = customSpeed.Value;
        }
        else
        {
            switch (mode)
            {
                case RotationMode.Combat:
                    currentRotationSpeed = combatRotationSpeed;
                    combatOverride = true;
                    break;
                case RotationMode.Movement:
                    currentRotationSpeed = normalRotationSpeed;
                    break;
                case RotationMode.Idle:
                    currentRotationSpeed = idleRotationSpeed;
                    break;
            }
        }
    }

    public void SnapToDirection(Vector3 direction)
    {
        direction.y = 0;
        if (direction == Vector3.zero) return;

        core.Transform.rotation = Quaternion.LookRotation(direction.normalized);
        targetLookDirection = direction.normalized;
        currentRotationMode = RotationMode.Combat;
        combatOverride = true;

        if (showDebugLogs)
            Debug.Log($"[Movement] {core.name} - SNAP to combat");
    }

    public void ClearLookTarget(RotationMode mode)
    {
        if (currentRotationMode == mode)
        {
            targetLookDirection = Vector3.zero;
            currentRotationMode = RotationMode.None;

            if (mode == RotationMode.Combat)
            {
                combatOverride = false;
            }
        }
    }

    public bool IsLookingAt(Vector3 position, float angleThreshold = 15f)
    {
        Vector3 dirToTarget = (position - core.Transform.position).normalized;
        dirToTarget.y = 0;

        float angle = Vector3.Angle(core.Transform.forward, dirToTarget);
        return angle < angleThreshold;
    }

    private void UpdateRotation()
    {
        if (targetLookDirection == Vector3.zero) return;

        Quaternion targetRotation = Quaternion.LookRotation(targetLookDirection);
        core.Transform.rotation = Quaternion.RotateTowards(
            core.Transform.rotation,
            targetRotation,
            currentRotationSpeed * Time.deltaTime
        );
    }

    private bool CanOverrideRotation(RotationMode newMode)
    {
        if (combatOverride && newMode != RotationMode.Combat)
        {
            return false;
        }

        int currentPriority = GetRotationPriority(currentRotationMode);
        int newPriority = GetRotationPriority(newMode);

        return newPriority >= currentPriority;
    }

    private int GetRotationPriority(RotationMode mode)
    {
        switch (mode)
        {
            case RotationMode.Combat: return 3;
            case RotationMode.Movement: return 2;
            case RotationMode.Idle: return 1;
            default: return 0;
        }
    }

    #endregion

    #region Patrol

    public void Patrol()
    {
        if (sweepWaypoints.Count == 0)
        {
            GenerateSweepPoints();
            return;
        }

        // НОВОЕ: Если включен защитный патруль и есть свободные союзники поблизости
        if (useDefensivePatrol)
        {
            PatrolDefensive();
        }
        else
        {
            PatrolStandard();
        }

        if (core.Blackboard.GetBool(BlackboardKey.IsInCover))
        {
            core.Blackboard.SetBool(BlackboardKey.IsInCover, false);
        }
    }

    /// <summary>
    /// НОВОЕ: Стандартный патруль - полный радиус вокруг спавна
    /// </summary>
    private void PatrolStandard()
    {
        Vector3 targetPoint = sweepWaypoints[currentWaypointIndex];
        float distance = Vector3.Distance(core.Transform.position, targetPoint);

        if (distance < 1.5f)
        {
            core.Agent.isStopped = true;
            waypointWaitTimer += Time.deltaTime;

            sweepRotation += idleRotationSpeed * Time.deltaTime;
            Vector3 lookDir = Quaternion.Euler(0, sweepRotation, 0) * Vector3.forward;

            SetLookTarget(lookDir, RotationMode.Idle, idleRotationSpeed);

            if (waypointWaitTimer >= patrolWaitTime)
            {
                currentWaypointIndex = (currentWaypointIndex + 1) % sweepWaypoints.Count;
                waypointWaitTimer = 0f;
                sweepRotation = core.Transform.eulerAngles.y;
            }
        }
        else
        {
            core.Agent.isStopped = false;
            core.Agent.speed = sweepSpeed;
            core.Agent.SetDestination(targetPoint);

            ClearLookTarget(RotationMode.Movement);
        }
    }

    /// <summary>
    /// НОВОЕ: Защитный патруль - уклон на защиту слепых зон и безопасность
    /// Боты прочесывают области где нет видимости
    /// </summary>
    private void PatrolDefensive()
    {
        // Оставить боты рядом со спавном для защиты (если спавн определён)
        if (spawnPoint != Vector3.zero)
        {
            float distToSpawn = Vector3.Distance(core.Transform.position, spawnPoint);

            // Если уже далеко - вернуться
            if (distToSpawn > patrolRadius)
            {
                core.Agent.isStopped = false;
                core.Agent.speed = sweepSpeed;
                core.Agent.SetDestination(spawnPoint);
                return;
            }
        }

        // Стандартный патруль но в ограниченной зоне
        Vector3 targetPoint = sweepWaypoints[currentWaypointIndex];
        float distance = Vector3.Distance(core.Transform.position, targetPoint);

        if (distance < 1.5f)
        {
            core.Agent.isStopped = true;
            waypointWaitTimer += Time.deltaTime;

            // На каждой точке проверить слепые зоны (смотреть в разные стороны)
            sweepRotation += idleRotationSpeed * Time.deltaTime;
            Vector3 lookDir = Quaternion.Euler(0, sweepRotation, 0) * Vector3.forward;

            SetLookTarget(lookDir, RotationMode.Idle, idleRotationSpeed);

            // Дольше ждать чтобы тщательнее проверить область
            if (waypointWaitTimer >= patrolWaitTime * 1.5f)
            {
                currentWaypointIndex = (currentWaypointIndex + 1) % sweepWaypoints.Count;
                waypointWaitTimer = 0f;
                sweepRotation = core.Transform.eulerAngles.y;
            }
        }
        else
        {
            core.Agent.isStopped = false;
            core.Agent.speed = sweepSpeed;
            core.Agent.SetDestination(targetPoint);

            ClearLookTarget(RotationMode.Movement);
        }
    }

    /// <summary>
    /// Установить позицию спавна для защитного патруля
    /// </summary>
    public void SetSpawnPoint(Vector3 spawn)
    {
        spawnPoint = spawn;
    }

    /// <summary>
    /// Включить/отключить защитный патруль
    /// </summary>
    public void SetDefensivePatrol(bool enabled)
    {
        useDefensivePatrol = enabled;
    }

    private void GenerateSweepPoints()
    {
        sweepWaypoints.Clear();

        Vector3 center = core.Transform.position;

        for (int i = 0; i < patrolPoints; i++)
        {
            float angle = (360f / patrolPoints) * i;
            Vector3 offset = Quaternion.Euler(0, angle, 0) * Vector3.forward * patrolRadius;
            Vector3 point = center + offset;

            UnityEngine.AI.NavMeshHit hit;
            if (UnityEngine.AI.NavMesh.SamplePosition(point, out hit, patrolRadius * 2f, UnityEngine.AI.NavMesh.AllAreas))
            {
                sweepWaypoints.Add(hit.position);
            }
        }

        if (sweepWaypoints.Count == 0)
        {
            sweepWaypoints.Add(center);
        }
    }

    #endregion

    #region Navigation

    public void MoveToPosition(Vector3 position, float moveSpeed = 2f)
    {
        core.Agent.isStopped = false;
        core.Agent.speed = moveSpeed;
        
        // ========== ПРИМЕНИТЬ ИЗБЕЖАНИЕ КЛАСТЕРИЗАЦИИ ==========
        Vector3 targetPos = position;
        
        // Проверить есть ли вектор избегания
        Vector3 avoidanceVec = spacingAvoidanceVector;
        if (avoidanceVec.magnitude > 0.1f)
        {
            // Применить коррекцию пути
            targetPos += avoidanceVec.normalized * (minSpacingFromAllies * 0.5f);
        }
        
        // Проверить препятствия впереди
        Vector3 obstacleAvoid = GetObstacleAvoidanceVector();
        if (obstacleAvoid.magnitude > 0.1f)
        {
            targetPos += obstacleAvoid * (minObstacleDistance * 0.7f);
        }
        
        core.Agent.SetDestination(targetPos);

        core.Blackboard.SetBool(BlackboardKey.IsInCover, false);
        ClearLookTarget(RotationMode.Movement);
    }

    public void AdvanceToward(Transform target)
    {
        if (target == null) return;

        core.Agent.isStopped = false;
        core.Agent.speed = runSpeed;
        core.Agent.SetDestination(target.position);

        core.Blackboard.SetBool(BlackboardKey.IsInCover, false);
    }

    #endregion

    #region Combat Strafe

    public void StrafeWhileShooting(Transform target)
    {
        if (target == null || !enableCombatStrafe) return;

        if (Time.time >= nextStrafeChangeTime)
        {
            PickStrafeDirection(target);
            nextStrafeChangeTime = Time.time + strafeChangeInterval;
        }

        if (strafeDirection != Vector3.zero)
        {
            core.Agent.isStopped = false;
            core.Agent.speed = strafeSpeed;

            Vector3 targetPos = core.Transform.position + strafeDirection * 3f;

            UnityEngine.AI.NavMeshHit hit;
            if (UnityEngine.AI.NavMesh.SamplePosition(targetPos, out hit, 3f, UnityEngine.AI.NavMesh.AllAreas))
            {
                core.Agent.SetDestination(hit.position);
            }
        }
    }

    private void PickStrafeDirection(Transform target)
    {
        Vector3 dirToTarget = (target.position - core.Transform.position).normalized;

        int choice = Random.Range(0, 3);

        switch (choice)
        {
            case 0:
                strafeDirection = Vector3.Cross(dirToTarget, Vector3.up).normalized;
                break;
            case 1:
                strafeDirection = -Vector3.Cross(dirToTarget, Vector3.up).normalized;
                break;
            case 2:
                strafeDirection = -dirToTarget;
                break;
        }
    }

    #endregion

    #region Cover

    public void SeekCover()
    {
        // НОВОЕ: Сначала проверить можно ли использовать дверь как укрытие
        DoorInteractionPoint nearbyDoor = FindNearbyDoorForCover();
        
        if (nearbyDoor != null)
        {
            UseDoorAsCover(nearbyDoor);
            return;
        }

        // Стандартный поиск укрытия
        Vector3 coverPos = FindBestCover();

        if (coverPos != Vector3.zero)
        {
            currentCoverPosition = coverPos;
            core.Blackboard.SetVector3(BlackboardKey.CurrentCoverPosition, coverPos);

            core.Agent.isStopped = false;
            core.Agent.speed = runSpeed;
            core.Agent.SetDestination(coverPos);

            ClearLookTarget(RotationMode.Movement);
        }
    }

    /// <summary>
    /// НОВОЕ: Найти ближайшую дверь для использования как укрытие
    /// </summary>
    private DoorInteractionPoint FindNearbyDoorForCover()
    {
        Vector3 threatPos = core.Blackboard.GetVector3(BlackboardKey.LastKnownThreatPosition);
        if (threatPos == Vector3.zero)
            threatPos = core.Blackboard.GetVector3(BlackboardKey.PredictedThreatPosition);

        if (threatPos == Vector3.zero) return null;

        Collider[] doorsNearby = Physics.OverlapSphere(core.Transform.position, coverSearchRadius);
        
        DoorInteractionPoint bestDoor = null;
        float bestScore = 0f;

        foreach (var col in doorsNearby)
        {
            DoorInteractionPoint door = col.GetComponent<DoorInteractionPoint>();
            if (door == null) continue;

            float distance = Vector3.Distance(core.Transform.position, door.transform.position);
            
            // Дверь должна быть между ботом и угрозой
            Vector3 doorPos = door.transform.position;
            Vector3 toThreat = (threatPos - core.Transform.position).normalized;
            Vector3 toDoor = (doorPos - core.Transform.position).normalized;
            
            float dotProduct = Vector3.Dot(toThreat, toDoor);
            
            // Дверь должна быть примерно в направлении угрозы (чтобы закрыть путь)
            if (dotProduct < 0.3f) continue;
            
            // Близкая дверь лучше
            float score = 1f - (distance / coverSearchRadius);
            score += dotProduct * 0.5f;  // Бонус за направление
            
            if (score > bestScore)
            {
                bestScore = score;
                bestDoor = door;
            }
        }

        return bestDoor;
    }

    /// <summary>
    /// НОВОЕ: Использовать дверь как укрытие
    /// Подойти к двери, закрыть её, встать за ней
    /// </summary>
    private void UseDoorAsCover(DoorInteractionPoint door)
    {
        if (door == null) return;

        doorUsedAsCover = door;
        
        Vector3 doorPos = door.transform.position;
        float distanceToDoor = Vector3.Distance(core.Transform.position, doorPos);

        // Если далеко - подойти
        if (distanceToDoor > doorInteractionRange + 1f)
        {
            // Встать ЗА дверью (с противоположной стороны от угрозы)
            Vector3 threatPos = core.Blackboard.GetVector3(BlackboardKey.LastKnownThreatPosition);
            Vector3 dirFromThreat = (doorPos - threatPos).normalized;
            Vector3 coverPosition = doorPos + dirFromThreat * 1.5f;

            UnityEngine.AI.NavMeshHit hit;
            if (UnityEngine.AI.NavMesh.SamplePosition(coverPosition, out hit, 2f, UnityEngine.AI.NavMesh.AllAreas))
            {
                core.Agent.isStopped = false;
                core.Agent.speed = runSpeed;
                core.Agent.SetDestination(hit.position);
                currentCoverPosition = hit.position;
            }
        }
        else
        {
            // Уже рядом - закрыть дверь и встать в укрытие
            if (door.IsOpen)
            {
                door.OnAIClose(core.gameObject);
            }

            core.Agent.isStopped = true;
            core.Blackboard.SetBool(BlackboardKey.IsInCover, true);
            currentCoverPosition = core.Transform.position;
            
            // Смотреть на дверь (ждать врага)
            Vector3 dirToDoor = (doorPos - core.Transform.position).normalized;
            SetLookTarget(dirToDoor, RotationMode.Combat);

            if (showDebugLogs)
                Debug.Log($"[Movement] {core.name} - Using door as cover!");
        }
    }

    private Vector3 FindBestCover()
    {
        Vector3 threatPos = core.Blackboard.GetVector3(BlackboardKey.LastKnownThreatPosition);
        if (threatPos == Vector3.zero)
            threatPos = core.Blackboard.GetVector3(BlackboardKey.PredictedThreatPosition);

        Vector3 bestCover = Vector3.zero;
        float bestScore = 0f;

        for (int i = 0; i < 8; i++)
        {
            float angle = i * 45f;
            Vector3 direction = Quaternion.Euler(0, angle, 0) * Vector3.forward;
            Vector3 checkPos = core.Transform.position + direction * coverSearchRadius;

            float score = EvaluateCoverPosition(checkPos, threatPos);

            if (score > bestScore)
            {
                UnityEngine.AI.NavMeshHit navHit;
                if (UnityEngine.AI.NavMesh.SamplePosition(checkPos, out navHit, 3f, UnityEngine.AI.NavMesh.AllAreas))
                {
                    bestScore = score;
                    bestCover = navHit.position;
                }
            }
        }

        return bestCover;
    }

    private float EvaluateCoverPosition(Vector3 position, Vector3 threatPos)
    {
        Vector3 dirToThreat = (threatPos - position).normalized;

        RaycastHit hit;
        if (Physics.Raycast(position + Vector3.up, dirToThreat, out hit, 10f, obstacleLayer))
        {
            if (hit.distance < 2f)
            {
                return 1f;
            }
        }

        return 0f;
    }

    private void UpdateCoverStatus()
    {
        // НОВОЕ: Проверить укрытие за дверью
        if (doorUsedAsCover != null)
        {
            float distanceToDoor = Vector3.Distance(core.Transform.position, doorUsedAsCover.transform.position);
            
            // Если рядом с дверью и она закрыта - в укрытии
            if (distanceToDoor < 3f && doorUsedAsCover.IsClosed)
            {
                if (!core.Blackboard.GetBool(BlackboardKey.IsInCover))
                {
                    core.Blackboard.SetBool(BlackboardKey.IsInCover, true);
                    core.Agent.isStopped = true;
                    
                    if (showDebugLogs)
                        Debug.Log($"[Movement] {core.name} - In cover behind closed door");
                }
                return;
            }
            // Если дверь открылась - укрытие потеряно
            else if (doorUsedAsCover.IsOpen)
            {
                doorUsedAsCover = null;
                core.Blackboard.SetBool(BlackboardKey.IsInCover, false);
                
                if (showDebugLogs)
                    Debug.Log($"[Movement] {core.name} - Door opened, cover lost!");
            }
        }

        // Стандартная проверка укрытия
        if (currentCoverPosition != Vector3.zero)
        {
            float distanceToCover = Vector3.Distance(core.Transform.position, currentCoverPosition);
            bool wasInCover = core.Blackboard.GetBool(BlackboardKey.IsInCover);
            bool isInCover = distanceToCover < 1.5f;

            if (wasInCover != isInCover)
            {
                core.Blackboard.SetBool(BlackboardKey.IsInCover, isInCover);

                if (isInCover)
                {
                    core.Agent.isStopped = true;
                }
            }
        }
    }

    #endregion

    #region Spacing Avoidance - НОВОЕ

    /// <summary>
    /// НОВОЕ: Избегать кластеризации врагов
    /// Враги отходят друг от друга если слишком близко
    /// </summary>
    private void UpdateSpacingAvoidance()
    {
        if (Time.time < nextSpacingCheckTime) return;
        nextSpacingCheckTime = Time.time + 0.3f;

        spacingAvoidanceVector = Vector3.zero;

        if (core.Squad == null) return;

        // Найти ближайших союзников
        var nearbyAllies = Physics.OverlapSphere(core.Transform.position, spacingSearchRadius);

        foreach (var col in nearbyAllies)
        {
            SmartEnemyAI otherAI = col.GetComponent<SmartEnemyAI>();
            if (otherAI == null) otherAI = col.GetComponentInParent<SmartEnemyAI>();

            if (otherAI == null || otherAI == core || otherAI.IsDead()) continue;

            // Проверить что это союзник
            if (otherAI.Health == null || core.Health == null) continue;
            
            // Проверить тим-тег (вместо IsTeammate используем TeamTag)
            if (otherAI.Health.TeamTag != core.Health.TeamTag) continue;

            // Вычислить расстояние
            float distance = Vector3.Distance(core.Transform.position, otherAI.Transform.position);

            // Если слишком близко - отойти
            if (distance < minSpacingFromAllies && distance > 0.1f)
            {
                Vector3 dirAwayFromAlly = (core.Transform.position - otherAI.Transform.position).normalized;
                spacingAvoidanceVector += dirAwayFromAlly;
            }
        }

        // Если есть вектор избегания - применить
        if (spacingAvoidanceVector.magnitude > 0.1f)
        {
            ApplySpacingCorrection(spacingAvoidanceVector.normalized);
        }
    }

    /// <summary>
    /// НОВОЕ: Применить корректировку расстояния
    /// Враг отходит в сторону чтобы не скапливаться
    /// </summary>
    private void ApplySpacingCorrection(Vector3 avoidanceDirection)
    {
        avoidanceDirection.y = 0;

        // Попытаться переместиться в сторону
        Vector3 correctedPos = core.Transform.position + avoidanceDirection * (minSpacingFromAllies + 0.5f);

        UnityEngine.AI.NavMeshHit hit;
        if (UnityEngine.AI.NavMesh.SamplePosition(correctedPos, out hit, 2f, UnityEngine.AI.NavMesh.AllAreas))
        {
            // Установить как временную цель уклонения
            core.Blackboard.SetVector3(BlackboardKey.AvoidanceTarget, hit.position);
        }
    }

    /// <summary>
    /// Получить вектор избегания для использования в движении
    /// </summary>
    public Vector3 GetSpacingAvoidanceVector()
    {
        return spacingAvoidanceVector;
    }

    #endregion

    #region Door Interaction - НОВОЕ

    /// <summary>
    /// НОВОЕ: Проверить есть ли двери рядом
    /// УЛУЧШЕНО: Не подходить вплотную, держать дистанцию
    /// </summary>
    private void UpdateDoorInteraction()
    {
        // Если уже взаимодействуем с дверью
        if (currentDoor != null)
        {
            doorInteractionTimer += Time.deltaTime;

            // Если дверь открыта или timeout - продолжить движение
            if (currentDoor.IsOpen || doorInteractionTimer > doorWaitTimeout)
            {
                currentDoor = null;
                doorInteractionTimer = 0f;
                core.Agent.isStopped = false;  // НОВОЕ: Разблокировать движение
            }

            return;
        }

        // НОВОЕ: Проверить нужно ли вообще взаимодействовать с дверью
        // Если бот не движется к цели - не искать двери
        if (core.Agent.isStopped || !core.Agent.hasPath) return;

        // Поиск ближайшей двери НА ПУТИ
        Collider[] doorsNearby = Physics.OverlapSphere(core.Transform.position, doorDetectionRange);

        DoorInteractionPoint closestDoor = null;
        float closestDistance = doorDetectionRange;

        foreach (var col in doorsNearby)
        {
            DoorInteractionPoint door = col.GetComponent<DoorInteractionPoint>();
            if (door == null) continue;

            // НОВОЕ: Проверить что дверь на пути движения
            Vector3 doorPos = door.transform.position;
            Vector3 moveDir = core.Agent.velocity.normalized;
            
            if (moveDir == Vector3.zero && core.Agent.hasPath)
            {
                moveDir = (core.Agent.steeringTarget - core.Transform.position).normalized;
            }
            
            Vector3 toDoor = (doorPos - core.Transform.position).normalized;
            float dotProduct = Vector3.Dot(moveDir, toDoor);
            
            // Только если дверь впереди (в направлении движения)
            if (dotProduct < 0.5f) continue;

            float distance = Vector3.Distance(core.Transform.position, doorPos);
            if (distance < closestDistance)
            {
                closestDistance = distance;
                closestDoor = door;
            }
        }

        // Если нашли дверь на пути и она закрыта
        if (closestDoor != null && !closestDoor.IsOpen)
        {
            // Проверить находимся ли мы достаточно близко чтобы открыть
            if (closestDistance < doorInteractionRange)
            {
                // Открыть дверь
                TryInteractWithDoor(closestDoor);
            }
            // НОВОЕ: НЕ подходить ближе - ждать или обойти
            else if (closestDistance < doorDetectionRange * 0.7f)
            {
                // Остановиться на безопасном расстоянии
                core.Agent.isStopped = true;
                
                // Смотреть на дверь
                Vector3 dirToDoor = (closestDoor.transform.position - core.Transform.position).normalized;
                SetLookTarget(dirToDoor, RotationMode.Movement);
                
                // Попробовать открыть с расстояния
                TryInteractWithDoor(closestDoor);
            }
        }
    }

    /// <summary>
    /// НОВОЕ: Попытаться взаимодействовать с дверью
    /// </summary>
    private void TryInteractWithDoor(DoorInteractionPoint door)
    {
        if (door == null) return;

        currentDoor = door;
        doorInteractionTimer = 0f;

        // Остановиться
        core.Agent.isStopped = true;

        // Активировать дверь
        door.OnAIInteract(core.gameObject);
        
        // НОВОЕ: Запомнить дверь чтобы закрыть за собой
        lastPassedDoor = door;
        doorPassedTime = Time.time;

        if (showDebugLogs)
        {
            Debug.Log($"[Movement] {core.name} - Interacting with door");
        }
    }

    /// <summary>
    /// НОВОЕ: Двигаться к кнопке двери (но не впритык!)
    /// </summary>
    private void MoveTowardsButton(Vector3 buttonPos, float stopDistance)
    {
        // Двигаться к кнопке но остановиться на расстоянии stopDistance
        Vector3 direction = (buttonPos - core.Transform.position).normalized;
        Vector3 targetPos = buttonPos - direction * stopDistance;

        UnityEngine.AI.NavMeshHit hit;
        if (UnityEngine.AI.NavMesh.SamplePosition(targetPos, out hit, 2f, UnityEngine.AI.NavMesh.AllAreas))
        {
            core.Agent.isStopped = false;
            core.Agent.speed = walkSpeed;
            core.Agent.SetDestination(hit.position);
        }
    }

    /// <summary>
    /// Получить информацию о дверном взаимодействии
    /// </summary>
    public bool IsWaitingForDoor()
    {
        return currentDoor != null && !currentDoor.IsOpen;
    }

    #endregion

    #region Obstacle Avoidance - НОВОЕ

    /// <summary>
    /// НОВОЕ: Проверить расстояние до препятствия впереди
    /// Враги не будут вплотную подходить к стенам
    /// </summary>
    public Vector3 GetObstacleAvoidanceVector()
    {
        RaycastHit hit;
        Vector3 forward = core.Transform.forward;

        // Проверка впереди
        if (Physics.Raycast(core.Transform.position + Vector3.up * 0.5f, forward, out hit, minObstacleDistance))
        {
            // Получить нормаль препятствия
            Vector3 avoidanceDir = Vector3.Cross(hit.normal, Vector3.up).normalized;

            if (showDebugLogs && Time.frameCount % 60 == 0)
            {
                Debug.Log($"[Movement] {core.name} - Obstacle detected, avoid direction: {avoidanceDir}");
            }

            return avoidanceDir;
        }

        return Vector3.zero;
    }

    #endregion
}

public enum RotationMode
{
    None,
    Idle,
    Movement,
    Combat
}