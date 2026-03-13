using UnityEngine;

// ═══════════════════════════════════════════════════════════════════════════
//  PvP BOT CONTROLLER  (Main Orchestrator)
//
//  Two-layer decision architecture:
//    1. REACTIVE LAYER  – Utility-based, runs every frame.
//       Handles instant reflexes: return fire, engage visible, take cover.
//    2. THINKING LAYER   – Interval-based (configurable, default ~1 s).
//       Analyses accumulated intel, plans tactical movement,
//       assigns CQB tasks, coordinates with squad.
//
//  Both attackers and defenders use the same controller.
//  Behaviour differences come from spawn location + squad assignments.
// ═══════════════════════════════════════════════════════════════════════════

public class PvPBotController : MonoBehaviour
{
    // ─── Module References ───────────────────────────────────────────────

    [Header("Modules")]
    public PvPPerceptionModule  Perception;
    public PvPReactiveLayer     ReactiveLayer;
    public PvPThinkingLayer     ThinkingLayer;
    public PvPCombatModule      Combat;
    public PvPCQBMovement       Movement;
    public PvPSquadCoordinator  Squad;
    public PvPIntelProcessor    Intel;

    // Re-use existing shared systems from "Бот"
    public AIWeaponController   WeaponController;
    public AIAnimationModule    Animation;
    public AILineOfSightSystem  LineOfSight;

    [HideInInspector] public UnityEngine.AI.NavMeshAgent Agent;
    [HideInInspector] public HealthManager Health;
    [HideInInspector] public Transform     Transform;

    // ─── Shared State ────────────────────────────────────────────────────

    public PvPBlackboard Blackboard = new PvPBlackboard();

    [Header("Team")]
    [SerializeField] private string myTeamTag    = "TeamA";
    [SerializeField] private string enemyTeamTag = "TeamB";
    public string MyTeamTag    => myTeamTag;
    public string EnemyTeamTag => enemyTeamTag;

    [Header("Core Settings")]
    [Tooltip("How often the Thinking Layer analyses intel and plans (seconds).")]
    [SerializeField] private float thinkInterval = 1.0f;

    [Tooltip("Seconds to remember an enemy after losing visual.")]
    [SerializeField] private float combatMemoryDuration = 10f;

    [SerializeField] private bool showDebugLogs = false;

    // ─── Internal ────────────────────────────────────────────────────────

    private float nextThinkTime  = 0f;
    private bool  isInitialized  = false;

    // ═════════════════════════════════════════════════════════════════════
    //  LIFECYCLE
    // ═════════════════════════════════════════════════════════════════════

    private void Awake()
    {
        Agent     = GetComponent<UnityEngine.AI.NavMeshAgent>();
        Health    = GetComponent<HealthManager>();
        Transform = transform;

        if (Agent == null)
        {
            Debug.LogError($"[PvPBot] {name} — missing NavMeshAgent!");
            enabled = false;
            return;
        }
        if (Health == null)
        {
            Debug.LogError($"[PvPBot] {name} — missing HealthManager!");
            enabled = false;
            return;
        }

        InitializeModules();

        Health.OnDeath.AddListener(OnDeath);
        Health.OnDamage.AddListener(OnDamage);

        isInitialized = true;
    }

    private void Start()
    {
        if (!isInitialized) { enabled = false; return; }
        if (!Agent.isOnNavMesh)
        {
            Debug.LogError($"[PvPBot] {name} — NOT on NavMesh!");
            enabled = false;
            return;
        }

        Squad?.RegisterWithManager();
        Movement?.BeginPatrol();

        if (showDebugLogs)
            Debug.Log($"[PvPBot] {name} — started (team={myTeamTag})");
    }

    private void Update()
    {
        if (!isInitialized || Health.IsDead) return;
        UpdateCore();
    }

    // ═════════════════════════════════════════════════════════════════════
    //  MAIN UPDATE LOOP
    // ═════════════════════════════════════════════════════════════════════

    private void UpdateCore()
    {
        // ── 1. PERCEPTION (every frame) ──────────────────────────────────
        Perception?.UpdatePerception();

        // ── 2. REACTIVE LAYER (every frame, utility-scored) ──────────────
        //    If a reflex fires it may block the thinking layer this frame.
        bool reflexFired = ReactiveLayer != null && ReactiveLayer.ProcessReflexes();

        // ── 3. THINKING LAYER (interval-based analysis) ──────────────────
        if (!reflexFired && Time.time >= nextThinkTime)
        {
            nextThinkTime = Time.time + thinkInterval;
            ThinkingLayer?.Evaluate();
        }

        // ── 4. COMBAT MEMORY DECAY ──────────────────────────────────────
        UpdateCombatMemory();

        // ── 5. MODULE TICKS ──────────────────────────────────────────────
        Combat?.UpdateModule();
        Movement?.UpdateModule();
        Animation?.UpdateModule();
        WeaponController?.UpdateModule();
        Intel?.UpdateModule();
    }

    // ═════════════════════════════════════════════════════════════════════
    //  COMBAT MEMORY
    // ═════════════════════════════════════════════════════════════════════

    private void UpdateCombatMemory()
    {
        float lastContact = Blackboard.GetFloat(PvPBlackboardKey.LastContactTime, -999f);
        float elapsed = Time.time - lastContact;

        if (Blackboard.GetBool(PvPBlackboardKey.InCombat))
        {
            if (elapsed > combatMemoryDuration)
            {
                // Lost track of enemy — exit combat mode
                Blackboard.SetBool(PvPBlackboardKey.InCombat, false);
                Blackboard.SetTransform(PvPBlackboardKey.CurrentTarget, null);
                Blackboard.Set(PvPBlackboardKey.ThreatLevel, PvPThreatLevel.None);

                if (showDebugLogs)
                    Debug.Log($"[PvPBot] {name} — combat memory expired ({combatMemoryDuration}s)");
            }
        }
    }

    // ═════════════════════════════════════════════════════════════════════
    //  INITIALIZATION
    // ═════════════════════════════════════════════════════════════════════

    private void InitializeModules()
    {
        if (Perception    == null) Perception    = gameObject.AddComponent<PvPPerceptionModule>();
        if (ReactiveLayer == null) ReactiveLayer = gameObject.AddComponent<PvPReactiveLayer>();
        if (ThinkingLayer == null) ThinkingLayer = gameObject.AddComponent<PvPThinkingLayer>();
        if (Combat        == null) Combat        = gameObject.AddComponent<PvPCombatModule>();
        if (Movement      == null) Movement      = gameObject.AddComponent<PvPCQBMovement>();
        if (Squad         == null) Squad         = gameObject.AddComponent<PvPSquadCoordinator>();
        if (Intel         == null) Intel         = gameObject.AddComponent<PvPIntelProcessor>();

        // Re-use "Бот" modules for weapon/animation/LOS
        if (WeaponController == null) WeaponController = gameObject.AddComponent<AIWeaponController>();
        if (LineOfSight      == null) LineOfSight      = gameObject.AddComponent<AILineOfSightSystem>();
        if (Animation        == null) Animation        = GetComponentInChildren<AIAnimationModule>();

        Perception?.Initialize(this);
        ReactiveLayer?.Initialize(this);
        ThinkingLayer?.Initialize(this);
        Combat?.Initialize(this);
        Movement?.Initialize(this);
        Squad?.Initialize(this);
        Intel?.Initialize(this);

        if (showDebugLogs)
            Debug.Log($"[PvPBot] {name} — all modules initialized");
    }

    // ═════════════════════════════════════════════════════════════════════
    //  CONFIGURATION (called by match controller)
    // ═════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Called by PvPMatchController during team setup.
    /// </summary>
    public void ConfigureTeam(string myTeam, string enemyTeam)
    {
        myTeamTag    = myTeam;
        enemyTeamTag = enemyTeam;

        if (Health != null)
            Health.teamTag = myTeam;

        Perception?.SetTeams(myTeam, enemyTeam);
    }

    // ═════════════════════════════════════════════════════════════════════
    //  EVENT HANDLERS
    // ═════════════════════════════════════════════════════════════════════

    private void OnDamage()
    {
        Blackboard.SetBool(PvPBlackboardKey.JustTookDamage, true);
        Blackboard.SetFloat(PvPBlackboardKey.LastDamageTime, Time.time);
        Blackboard.SetFloat(PvPBlackboardKey.HealthPercent,
            Health != null ? Health.HealthPercentage : 1f);

        if (Health != null)
        {
            Blackboard.SetVector3(PvPBlackboardKey.LastDamageDirection, Health.hitDirections);
        }

        // Log intel
        Vector3 hitDir = Blackboard.GetVector3(PvPBlackboardKey.LastDamageDirection);
        Vector3 estimatedSource = Transform.position + hitDir.normalized * 15f;

        Blackboard.PushIntel(new IntelRecord
        {
            type       = IntelType.DamageTaken,
            position   = estimatedSource,
            timestamp  = Time.time,
            confidence = 0.4f
        });

        Animation?.TriggerHit();

        // Notify squad
        Squad?.ReportContact(estimatedSource, false);

        if (showDebugLogs)
            Debug.Log($"[PvPBot] {name} — DAMAGED! HP={Health.HealthPercentage:P0}");
    }

    private void OnDeath()
    {
        Agent.isStopped = true;
        enabled = false;

        Animation?.TriggerDeath();
        Squad?.UnregisterFromManager();

        if (showDebugLogs)
            Debug.Log($"[PvPBot] {name} — KIA");
    }

    // ═════════════════════════════════════════════════════════════════════
    //  PUBLIC API
    // ═════════════════════════════════════════════════════════════════════

    public bool  IsDead()             => Health != null && Health.IsDead;
    public float GetHealthPercent()   => Health != null ? Health.HealthPercentage : 0f;
}
