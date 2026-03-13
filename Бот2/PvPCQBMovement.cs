using UnityEngine;

// ═══════════════════════════════════════════════════════════════════════════
//  PvP CQB MOVEMENT MODULE
//
//  Handles all navigation for the PvP tactical bot:
//    • Patrol  — route-based map sweep.
//    • Tactical Advance — cautious approach (weapon up, slower speed).
//    • Pie Slicing  — gradual corner clearing (industry CQB standard).
//    • Room Clearing — stack at door, dynamic entry, sector scan.
//    • Cover-to-Cover — move only between cover positions.
//    • Flanking — circle enemy from the side.
//    • Fall Back — disengage toward squad / spawn.
//    • Formation — maintain spacing relative to squad.
//
//  Uses UnityEngine.AI.NavMeshAgent for pathfinding.
// ═══════════════════════════════════════════════════════════════════════════

[RequireComponent(typeof(UnityEngine.AI.NavMeshAgent))]
public class PvPCQBMovement : MonoBehaviour
{
    // ─── Speed Profiles ──────────────────────────────────────────────────

    [Header("Speed")]
    [SerializeField] private float patrolSpeed    = 2.0f;
    [SerializeField] private float sweepSpeed     = 1.2f;  // CQB slow clear
    [SerializeField] private float advanceSpeed   = 2.5f;
    [SerializeField] private float runSpeed       = 5.0f;
    [SerializeField] private float strafeSpeed    = 2.0f;

    // ─── Rotation ────────────────────────────────────────────────────────

    [Header("Rotation")]
    [SerializeField] private float normalRotSpeed  = 180f;
    [SerializeField] private float combatRotSpeed   = 540f;
    [SerializeField] private float sweepRotSpeed    = 90f;

    // ─── Patrol ──────────────────────────────────────────────────────────

    [Header("Patrol")]
    [SerializeField] private float patrolRadius      = 50f;
    [SerializeField] private int   patrolWaypoints   = 10;
    [SerializeField] private float waypointThreshold = 2f;

    // ─── CQB ─────────────────────────────────────────────────────────────

    [Header("CQB")]
    [SerializeField] private float pieSweepAngle    = 15f;   // degrees per step
    [SerializeField] private float piePauseTime     = 0.3f;  // pause between steps
    [SerializeField] private float stackDistance     = 1.5f;  // distance from door
    [SerializeField] private float sectorScanRadius = 6f;

    // ─── Cover ───────────────────────────────────────────────────────────

    [Header("Cover")]
    [SerializeField] private float coverSearchRadius = 20f;
    [SerializeField] private float minAllySpacing    = 3.0f;

    // ─── Internal ────────────────────────────────────────────────────────

    private PvPBotController core;
    private UnityEngine.AI.NavMeshAgent agent;

    private Vector3[] patrolRoute;
    private int  currentPatrolIndex = 0;
    private bool isPatrolling       = false;

    // CQB state
    private float  pieSweepTimer   = 0f;
    private float  pieSweepYaw     = 0f;
    private int    pieSweepDir     = 1;

    // ═════════════════════════════════════════════════════════════════════

    public void Initialize(PvPBotController controller)
    {
        core  = controller;
        agent = core.Agent;
    }

    // ═════════════════════════════════════════════════════════════════════
    //  UPDATE (called per-frame by PvPBotController)
    // ═════════════════════════════════════════════════════════════════════

    public void UpdateModule()
    {
        if (core.IsDead()) return;

        UpdateRotation();
        UpdatePatrol();
        EnforceAllySpacing();
    }

    // ═════════════════════════════════════════════════════════════════════
    //  PATROL
    // ═════════════════════════════════════════════════════════════════════

    public void BeginPatrol()
    {
        if (patrolRoute == null || patrolRoute.Length == 0)
            GeneratePatrolRoute();

        isPatrolling = true;
        currentPatrolIndex = 0;
        SetSpeed(patrolSpeed);
        MoveToWaypoint(currentPatrolIndex);
    }

    private void GeneratePatrolRoute()
    {
        patrolRoute = new Vector3[patrolWaypoints];
        Vector3 origin = core.Transform.position;

        for (int i = 0; i < patrolWaypoints; i++)
        {
            Vector3 random = origin + Random.insideUnitSphere * patrolRadius;
            random.y = origin.y;

            if (UnityEngine.AI.NavMesh.SamplePosition(random, out var hit, 10f,
                    UnityEngine.AI.NavMesh.AllAreas))
            {
                patrolRoute[i] = hit.position;
            }
            else
            {
                patrolRoute[i] = origin;
            }
        }
    }

    private void UpdatePatrol()
    {
        if (!isPatrolling || patrolRoute == null) return;
        if (agent.pathPending) return;

        if (!agent.hasPath || agent.remainingDistance < waypointThreshold)
        {
            currentPatrolIndex = (currentPatrolIndex + 1) % patrolRoute.Length;
            MoveToWaypoint(currentPatrolIndex);
        }
    }

    private void MoveToWaypoint(int index)
    {
        if (patrolRoute == null || index >= patrolRoute.Length) return;
        agent.SetDestination(patrolRoute[index]);
    }

    // ═════════════════════════════════════════════════════════════════════
    //  TACTICAL ADVANCE  (cautious approach, weapon up)
    // ═════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Move cautiously toward a position (weapon raised, moderate speed).
    /// </summary>
    public void TacticalAdvanceTo(Vector3 position)
    {
        isPatrolling = false;
        SetSpeed(advanceSpeed);
        agent.isStopped = false;
        agent.SetDestination(position);
        core.WeaponController?.RaiseWeapon();
    }

    // ═════════════════════════════════════════════════════════════════════
    //  ROOM CLEARING (CQB)
    // ═════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Begin a room-clear sequence from a designated entry point.
    /// Transitions through: Approaching → StackingUp → PieSlicing → DynamicEntry → Clearing.
    /// </summary>
    public void BeginRoomClear(Vector3 entryPoint)
    {
        isPatrolling = false;
        core.Blackboard.Set(PvPBlackboardKey.CQBState, PvPCQBState.Approaching);
        core.Blackboard.SetVector3(PvPBlackboardKey.RoomEntryPoint, entryPoint);
        core.Blackboard.SetBool(PvPBlackboardKey.IsClearing, true);

        SetSpeed(sweepSpeed);
        agent.SetDestination(entryPoint);
    }

    /// <summary>
    /// Called each tick while in CQB states.
    /// Managed from ThinkingLayer / UpdateModule when CQBState is active.
    /// </summary>
    public void UpdateCQB()
    {
        var state = core.Blackboard.Get(PvPBlackboardKey.CQBState, PvPCQBState.None);

        switch (state)
        {
            case PvPCQBState.Approaching:
                if (!agent.pathPending && agent.remainingDistance < stackDistance + 0.5f)
                {
                    core.Blackboard.Set(PvPBlackboardKey.CQBState, PvPCQBState.StackingUp);
                    agent.isStopped = true;
                }
                break;

            case PvPCQBState.StackingUp:
                // Brief pause before entry
                core.Blackboard.Set(PvPBlackboardKey.CQBState, PvPCQBState.PieSlicing);
                pieSweepYaw = core.Transform.eulerAngles.y - 45f;
                pieSweepTimer = 0f;
                pieSweepDir = 1;
                break;

            case PvPCQBState.PieSlicing:
                UpdatePieSlice();
                break;

            case PvPCQBState.DynamicEntry:
                // Move quickly into the room
                Vector3 entry = core.Blackboard.GetVector3(PvPBlackboardKey.RoomEntryPoint);
                Vector3 intoRoom = entry + core.Transform.forward * 4f;

                if (UnityEngine.AI.NavMesh.SamplePosition(intoRoom, out var hit, 5f,
                        UnityEngine.AI.NavMesh.AllAreas))
                {
                    SetSpeed(runSpeed);
                    agent.isStopped = false;
                    agent.SetDestination(hit.position);
                }

                core.Blackboard.Set(PvPBlackboardKey.CQBState, PvPCQBState.ClearingSector);
                break;

            case PvPCQBState.ClearingSector:
                // Scan room sectors
                if (!agent.pathPending && agent.remainingDistance < 1.5f)
                {
                    core.Blackboard.Set(PvPBlackboardKey.CQBState, PvPCQBState.SectorClear);
                    core.Blackboard.SetBool(PvPBlackboardKey.IsClearing, false);

                    core.Blackboard.PushIntel(new IntelRecord
                    {
                        type       = IntelType.SectorClear,
                        position   = core.Transform.position,
                        timestamp  = Time.time,
                        confidence = 0.9f
                    });
                }
                break;

            case PvPCQBState.SectorClear:
            case PvPCQBState.Holding:
                // Room cleared — hold or ThinkingLayer decides next
                break;
        }
    }

    /// <summary>
    /// Pie-slicing: incrementally sweep the doorway/corner in small angular steps.
    /// </summary>
    private void UpdatePieSlice()
    {
        pieSweepTimer += Time.deltaTime;

        if (pieSweepTimer >= piePauseTime)
        {
            pieSweepTimer = 0f;
            pieSweepYaw += pieSweepAngle * pieSweepDir;

            // After sweeping 90 degrees, enter the room
            float totalSwept = Mathf.Abs(pieSweepYaw - (core.Transform.eulerAngles.y - 45f));
            if (totalSwept >= 90f)
            {
                core.Blackboard.Set(PvPBlackboardKey.CQBState, PvPCQBState.DynamicEntry);
                agent.isStopped = false;
                return;
            }
        }

        // Rotate to current sweep angle
        Quaternion targetRot = Quaternion.Euler(0f, pieSweepYaw, 0f);
        core.Transform.rotation = Quaternion.RotateTowards(
            core.Transform.rotation, targetRot, sweepRotSpeed * Time.deltaTime);
    }

    // ═════════════════════════════════════════════════════════════════════
    //  COVER
    // ═════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Find and move to the nearest available cover point.
    /// Re-uses the CoverPoint component from "Бот".
    /// </summary>
    public void SeekCover()
    {
        isPatrolling = false;
        CoverPoint[] covers = FindObjectsByType<CoverPoint>(FindObjectsSortMode.None);
        CoverPoint best = null;
        float bestDist = float.MaxValue;

        Vector3 pos = core.Transform.position;

        foreach (var cp in covers)
        {
            if (cp.isOccupied) continue;
            float d = Vector3.Distance(pos, cp.transform.position);
            if (d > coverSearchRadius) continue;
            if (d < bestDist)
            {
                bestDist = d;
                best = cp;
            }
        }

        if (best != null)
        {
            best.isOccupied = true;
            SetSpeed(runSpeed);
            agent.isStopped = false;
            agent.SetDestination(best.transform.position);
            core.Blackboard.SetBool(PvPBlackboardKey.IsInCover, true);
            core.Blackboard.SetVector3(PvPBlackboardKey.CurrentCoverPosition,
                best.transform.position);
        }
    }

    // ═════════════════════════════════════════════════════════════════════
    //  FLANKING
    // ═════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Move to a position 90° off the line between self and the enemy.
    /// </summary>
    public void FlankPosition(Vector3 enemyPos)
    {
        isPatrolling = false;
        Vector3 toEnemy = enemyPos - core.Transform.position;
        Vector3 perp = Vector3.Cross(toEnemy, Vector3.up).normalized;

        if (Random.value > 0.5f) perp = -perp;

        Vector3 flankTarget = core.Transform.position + perp * 12f + toEnemy.normalized * 5f;

        if (UnityEngine.AI.NavMesh.SamplePosition(flankTarget, out var hit, 8f,
                UnityEngine.AI.NavMesh.AllAreas))
        {
            SetSpeed(advanceSpeed);
            agent.isStopped = false;
            agent.SetDestination(hit.position);
        }
    }

    // ═════════════════════════════════════════════════════════════════════
    //  FALL BACK
    // ═════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Retreat away from the current threat direction.
    /// </summary>
    public void FallBack()
    {
        isPatrolling = false;
        Vector3 threatDir = core.Blackboard.GetVector3(PvPBlackboardKey.ThreatDirection);
        if (threatDir == Vector3.zero)
            threatDir = -core.Transform.forward;

        Vector3 retreat = core.Transform.position - threatDir.normalized * 15f;

        if (UnityEngine.AI.NavMesh.SamplePosition(retreat, out var hit, 10f,
                UnityEngine.AI.NavMesh.AllAreas))
        {
            SetSpeed(runSpeed);
            agent.isStopped = false;
            agent.SetDestination(hit.position);
        }
    }

    // ═════════════════════════════════════════════════════════════════════
    //  AMBUSH
    // ═════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Stop and hold, facing the predicted threat direction.
    /// </summary>
    public void SetupAmbush()
    {
        isPatrolling = false;
        agent.isStopped = true;
        core.Blackboard.SetBool(PvPBlackboardKey.IsHoldingPosition, true);
    }

    // ═════════════════════════════════════════════════════════════════════
    //  HOLD POSITION
    // ═════════════════════════════════════════════════════════════════════

    public void HoldPosition()
    {
        isPatrolling = false;
        agent.isStopped = true;
        core.Blackboard.SetBool(PvPBlackboardKey.IsHoldingPosition, true);
    }

    // ═════════════════════════════════════════════════════════════════════
    //  GENERIC
    // ═════════════════════════════════════════════════════════════════════

    public void MoveToPosition(Vector3 pos, float speed)
    {
        isPatrolling = false;
        SetSpeed(speed);
        agent.isStopped = false;
        agent.SetDestination(pos);
    }

    /// <summary>Instant 180° snap toward direction.</summary>
    public void SnapToDirection(Vector3 dir)
    {
        if (dir == Vector3.zero) return;
        dir.y = 0f;
        core.Transform.rotation = Quaternion.LookRotation(dir);
    }

    /// <summary>Smooth look toward direction at current rotation speed.</summary>
    public void SetLookDirection(Vector3 dir)
    {
        if (dir == Vector3.zero) return;
        dir.y = 0f;
        Quaternion target = Quaternion.LookRotation(dir);
        core.Transform.rotation = Quaternion.RotateTowards(
            core.Transform.rotation, target, combatRotSpeed * Time.deltaTime);
    }

    // ─── Helpers ─────────────────────────────────────────────────────────

    private void SetSpeed(float speed)
    {
        if (agent != null) agent.speed = speed;
    }

    private void UpdateRotation()
    {
        // NavMeshAgent handles rotation when moving; we handle it in combat
        bool inCombat = core.Blackboard.GetBool(PvPBlackboardKey.InCombat);
        agent.angularSpeed = inCombat ? combatRotSpeed : normalRotSpeed;
    }

    private void EnforceAllySpacing()
    {
        if (PvPSquadManager.Instance == null) return;

        var allBots = PvPSquadManager.Instance.GetAllBots();
        for (int i = 0; i < allBots.Count; i++)
        {
            var other = allBots[i];
            if (other == null || other == core || other.IsDead()) continue;
            if (other.MyTeamTag != core.MyTeamTag) continue;

            float dist = Vector3.Distance(core.Transform.position, other.Transform.position);
            if (dist < minAllySpacing && dist > 0.01f)
            {
                Vector3 away = (core.Transform.position - other.Transform.position).normalized;
                agent.Move(away * Time.deltaTime * 1.5f);
            }
        }
    }
}
