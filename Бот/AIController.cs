using UnityEngine;

/// <summary>
/// CORE AI CONTROLLER - Orchestrates all modules
/// REFACTORED & OPTIMIZED
/// </summary>
public class SmartEnemyAI : MonoBehaviour
{
    [Header("AI Modules")]
    public AIPerceptionModule Perception;
    public AIReactiveCombatLayer ReactiveCombat;
    public AIUtilityBrain UtilityBrain;
    public AIActionExecutor ActionExecutor;
    public AIAnimationModule Animation;
    public AICombatModule Combat;
    public AIMovementModule Movement;
    public AISquadModule Squad;
    public AIPsychologyModule Psychology;
    public AIArrestModule Arrest;
    public AISurrenderDecisionModule SurrenderDecision;
    public AIWeaponController WeaponController;
    public AILineOfSightSystem LineOfSight;

    [HideInInspector] public UnityEngine.AI.NavMeshAgent Agent;
    [HideInInspector] public HealthManager Health;
    [HideInInspector] public Transform Transform;

    public AIBlackboard Blackboard = new AIBlackboard();

    [Header("Core Settings")]
    [SerializeField] private float decisionInterval = 0.3f;
    [SerializeField] private float combatMemoryDuration = 8f;  // НОВОЕ: как долго помнить врага (8 сек)
    [SerializeField] private bool showDebugLogs = false;

    private float nextDecisionTime = 0f;
    private bool isInitialized = false;

    private void Awake()
    {
        // Cache components FIRST
        Agent = GetComponent<UnityEngine.AI.NavMeshAgent>();
        Health = GetComponent<HealthManager>();
        Transform = transform;

        if (Agent == null)
        {
            Debug.LogError($"[SmartAI] {name} - NO NavMeshAgent!");
            enabled = false;
            return;
        }

        if (Health == null)
        {
            Debug.LogError($"[SmartAI] {name} - NO HealthManager!");
            enabled = false;
            return;
        }

        // Initialize modules
        InitializeModules();

        // Subscribe to events AFTER initialization
        Health.OnDeath.AddListener(OnDeath);
        Health.OnDamage.AddListener(OnDamage);

        isInitialized = true;
    }

    private void InitializeModules()
    {
        // Create modules if missing
        if (Perception == null) Perception = gameObject.AddComponent<AIPerceptionModule>();
        if (ReactiveCombat == null) ReactiveCombat = gameObject.AddComponent<AIReactiveCombatLayer>();
        if (UtilityBrain == null) UtilityBrain = gameObject.AddComponent<AIUtilityBrain>();
        if (ActionExecutor == null) ActionExecutor = gameObject.AddComponent<AIActionExecutor>();
        if (Combat == null) Combat = gameObject.AddComponent<AICombatModule>();
        if (Movement == null) Movement = gameObject.AddComponent<AIMovementModule>();
        if (Squad == null) Squad = gameObject.AddComponent<AISquadModule>();
        if (Psychology == null) Psychology = gameObject.AddComponent<AIPsychologyModule>();
        if (Arrest == null) Arrest = gameObject.AddComponent<AIArrestModule>();
        if (SurrenderDecision == null) SurrenderDecision = gameObject.AddComponent<AISurrenderDecisionModule>();
        if (WeaponController == null) WeaponController = gameObject.AddComponent<AIWeaponController>();
        if (LineOfSight == null) LineOfSight = gameObject.AddComponent<AILineOfSightSystem>();

        // Animation might be on child
        if (Animation == null) Animation = GetComponentInChildren<AIAnimationModule>();

        // Initialize ALL modules
        Perception?.Initialize(this);
        ReactiveCombat?.Initialize(this);
        UtilityBrain?.Initialize(this);
        ActionExecutor?.Initialize(this);
        Animation?.Initialize(this);
        Combat?.Initialize(this);
        Movement?.Initialize(this);
        Squad?.Initialize(this);
        Psychology?.Initialize(this);
        Arrest?.Initialize(this);
        SurrenderDecision?.Initialize(this);
        WeaponController?.Initialize(this);
        LineOfSight?.Initialize(this);

        if (showDebugLogs)
            Debug.Log($"[SmartAI] {name} - All modules initialized");
    }

    private void Start()
    {
        if (!isInitialized)
        {
            Debug.LogError($"[SmartAI] {name} - Not initialized in Awake!");
            enabled = false;
            return;
        }

        if (!Agent.isOnNavMesh)
        {
            Debug.LogError($"[SmartAI] {name} - NOT ON NAVMESH!");
            enabled = false;
            return;
        }

        // Start initial behavior
        Movement?.Patrol();
        Squad?.RegisterWithSquad();

        if (showDebugLogs)
            Debug.Log($"[SmartAI] {name} - Started");
    }

    private void Update()
    {
        if (!isInitialized || Health.IsDead) return;

        // === ГЛАВНЫЙ UPDATECORE ===
        UpdateCore();
    }

    /// <summary>
    /// ЕДИНСТВЕННЫЙ ГЛАВНЫЙ МЕТОД ОБНОВЛЕНИЯ
    /// Боt как настоящий человек принимает ДНА решения:
    /// 1. Best Action (что делать: сдаться, уйти, драться)
    /// 2. Best Combat Move (как драться: стрелять, флангировать, укрыться)
    /// </summary>
    private void UpdateCore()
    {
        // 1. PERCEPTION (всегда видит/слышит)
        Perception?.UpdatePerception();

        // 2. PSYCHOLOGY - ГЛАВНОЕ РЕШЕНИЕ: сдаться?
        Psychology?.UpdateModule();

        if (Psychology != null && Psychology.HasSurrendered())
        {
            // Сдался - всё остальное игнорируем
            Agent.isStopped = true;
            WeaponController?.DropWeapon();
            Blackboard.SetBool(BlackboardKey.HasSurrendered, true);
            return;
        }

        // 3. SURRENDER DECISION (раздумье)
        if (SurrenderDecision != null && SurrenderDecision.IsDeciding())
        {
            SurrenderDecision.UpdateModule();
            return; // Ждём решения
        }

        // 4. DECISION SYSTEM - переоценить Best Action & Combat Move
        if (Time.time >= nextDecisionTime)
        {
            nextDecisionTime = Time.time + decisionInterval;
            EvaluateDecisions();
        }

        // 5. EXECUTE DECISION
        ExecuteCurrentDecision();

        // 6. UPDATE MODULES
        Animation?.UpdateModule();
        Combat?.UpdateModule();
        Movement?.UpdateModule();
        WeaponController?.UpdateModule();
    }

    /// <summary>
    /// Оценить Best Action и Best Combat Move
    /// УЛУЧШЕНО: Боты помнят врага до 8 сек даже если тот исчез из вида
    /// Это похоже на поведение реального человека в бою
    /// </summary>
    private void EvaluateDecisions()
    {
        var visibleThreat = Perception?.GetVisibleTarget();

        // СЛУЧАЙ 1: ВИДИМ ВРАГА ПРЯМО СЕЙЧАС
        if (visibleThreat != null)
        {
            // Обновить "последнее время когда видели"
            Blackboard.SetFloat(BlackboardKey.LastSeenThreatTime, Time.time);
            Blackboard.SetVector3(BlackboardKey.LastKnownThreatPosition, visibleThreat.position);
            Blackboard.SetTransform(BlackboardKey.CurrentThreat, visibleThreat);
            Blackboard.SetBool(BlackboardKey.InCombat, true);

            // Best Combat Move = зависит от дистанции
            float distanceToThreat = Vector3.Distance(Transform.position, visibleThreat.position);
            
            if (distanceToThreat < 5f)
            {
                // Близко - стоим и готовимся стрелять
                Agent.isStopped = true;
                WeaponController?.RaiseWeapon();
            }
            else
            {
                // Далеко - флангируем и приближаемся
                Agent.isStopped = false;
                MoveToFlanking(visibleThreat);
                WeaponController?.RaiseWeapon();
            }

            if (showDebugLogs && Time.frameCount % 60 == 0)
            {
                Debug.Log($"[SmartAI] {name} - VISIBLE THREAT at {distanceToThreat:F1}m");
            }
        }
        // СЛУЧАЙ 2: НЕ ВИДИМ но помним врага (боевая память)
        else
        {
            float lastSeenTime = Blackboard.GetFloat(BlackboardKey.LastSeenThreatTime, -999f);
            float timeSinceLastSeen = Time.time - lastSeenTime;

            // Помним врага до combatMemoryDuration (8 сек)
            if (timeSinceLastSeen < combatMemoryDuration)
            {
                // УЛУЧШЕНО: Остаёмся в боевом режиме и ИЩЕМ врага
                Blackboard.SetBool(BlackboardKey.InCombat, true);

                Vector3 lastKnownPos = Blackboard.GetVector3(BlackboardKey.LastKnownThreatPosition);
                
                if (lastKnownPos != Vector3.zero)
                {
                    // Поднять оружие - готовимся к встрече
                    WeaponController?.RaiseWeapon();

                    // Временная шкала поведения:
                    // 0-3 сек: Подавляющий огонь по последней позиции
                    // 3-8 сек: Поиск + подготовка
                    if (timeSinceLastSeen < 3f)
                    {
                        // Свежие воспоминания - давим огнём!
                        Combat?.SuppressPosition(lastKnownPos);
                        
                        if (showDebugLogs && Time.frameCount % 60 == 0)
                        {
                            Debug.Log($"[SmartAI] {name} - SUPPRESSING last known position ({timeSinceLastSeen:F1}s)");
                        }
                    }
                    else
                    {
                        // Старые воспоминания - осторожный поиск
                        Movement?.MoveToPosition(lastKnownPos, moveSpeed: 2f);
                        
                        if (showDebugLogs && Time.frameCount % 120 == 0)
                        {
                            Debug.Log($"[SmartAI] {name} - Searching for threat ({timeSinceLastSeen:F1}s since seen)");
                        }
                    }
                }
            }
            // СЛУЧАЙ 3: ДАВНО ПОТЕРЯЛИ ВРАГА (> 8 сек)
            else
            {
                // Выходим из боевого режима
                Blackboard.SetBool(BlackboardKey.InCombat, false);
                Blackboard.SetTransform(BlackboardKey.CurrentThreat, null);

                // Проверить - может мы услышали что-то?
                var threatLevel = Blackboard.Get(BlackboardKey.ThreatLevel, ThreatLevel.None);
                
                if (threatLevel == ThreatLevel.Suspected || Blackboard.GetBool(BlackboardKey.IsAlert, false))
                {
                    // Есть подозрение - используем Utility Brain для решения
                    UtilityBrain?.EvaluateActions();
                    
                    if (showDebugLogs && Time.frameCount % 120 == 0)
                    {
                        Debug.Log($"[SmartAI] {name} - Combat over, now investigating");
                    }
                }
                else
                {
                    // Полный мир - вернуться к патрулю
                    if (showDebugLogs && Time.frameCount % 120 == 0)
                    {
                        Debug.Log($"[SmartAI] {name} - Back to patrol");
                    }
                }
            }
        }
    }

    /// <summary>
    /// Выполнить выбранное действие
    /// </summary>
    private void ExecuteCurrentDecision()
    {
        if (Blackboard.GetBool(BlackboardKey.InCombat))
        {
            // БОЕВОЙ РЕЖИМ
            var threat = Blackboard.GetTransform(BlackboardKey.CurrentThreat);
            if (threat != null)
            {
                // Только если ВИДИМ - стреляем!
                WeaponController?.AimAtTarget(threat);
                
                // Ответственная стрельба - с интервалом 
                float timeSinceShot = Time.time - Blackboard.GetFloat(BlackboardKey.LastShotTime, -999f);
                if (timeSinceShot > 0.2f) // 200ms интервал
                {
                    WeaponController?.Fire();
                    Blackboard.SetFloat(BlackboardKey.LastShotTime, Time.time);
                    Debug.Log($"{name} Firing at ExecuteCurrentDecision()");
                }
            }
        }
        else
        {
            // РЕЖИМ ПАТРУЛЯ/ПОИСКА
            var lastAction = UtilityBrain?.GetLastAction() ?? AIAction.Patrol;
            ActionExecutor?.ExecuteAction(lastAction);
        }
    }

    /// <summary>
    /// Флангирование вокруг врага
    /// </summary>
    private void MoveToFlanking(Transform target)
    {
        Vector3 toTarget = target.position - Transform.position;
        Vector3 flankDirection = Vector3.Cross(toTarget, Vector3.up).normalized;

        // Выбрать сторону
        if (Random.value > 0.5f)
            flankDirection = -flankDirection;

        Vector3 flankPosition = Transform.position + flankDirection * 10f;

        // Найти точку на NavMesh
        if (UnityEngine.AI.NavMesh.SamplePosition(flankPosition, out var hit, 5f, UnityEngine.AI.NavMesh.AllAreas))
        {
            Agent.SetDestination(hit.position);
        }
    }

    #region Event Handlers

    private void OnDamage()
    {
        Blackboard.SetBool(BlackboardKey.JustTookDamage, true);
        Blackboard.SetFloat(BlackboardKey.LastDamageTime, Time.time);

        Animation?.TriggerHit();

        float damagePercent = 1f - Health.HealthPercentage;
        Psychology?.OnTakeDamage(damagePercent);
        Perception?.OnBulletHitFromDirection(Health.lastHitPoint, Health.hitDirections);

        if (showDebugLogs)
            Debug.Log($"[SmartAI] {name} - Damaged!  HP: {Health.HealthPercentage:P0}");
    }

    private void OnDeath()
    {
        Agent.isStopped = true;
        enabled = false;

        Animation?.TriggerDeath();
        Squad?.UnregisterFromSquad();

        MissionObjectiveTracker.Instance.ReportSuspectKilled(this, IsSurrendered());

        if (showDebugLogs)
            Debug.Log($"[SmartAI] {name} - Died");
    }

    #endregion

    #region Public API

    public bool IsDead() => Health != null && Health.IsDead;

    public float GetHealthPercentage() => Health != null ? Health.HealthPercentage : 0f;

    /// <summary>
    /// FIXED: Can arrest if surrendered AND alive
    /// </summary>
    public bool CanBeArrested()
    {
        return !IsDead() &&
               Psychology != null &&
               Psychology.HasSurrendered() &&
               !Blackboard.GetBool(BlackboardKey.IsArrested, false);
    }

    public bool IsSurrendered() => Psychology != null && Psychology.HasSurrendered();

    public bool IsDeciding() => SurrenderDecision != null && SurrenderDecision.IsDeciding();

    public void TakeDamageFromPlayer(float damage, Vector3 hitPoint, Vector3 dir)
    {
        if (IsDead()) return;
        Health.TakeDamage(damage, hitPoint, dir);
    }
    #endregion
}