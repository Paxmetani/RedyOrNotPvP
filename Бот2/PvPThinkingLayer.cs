using UnityEngine;
using System.Collections.Generic;

// ═══════════════════════════════════════════════════════════════════════════
//  PvP THINKING LAYER  (Interval-based analysis & planning)
//
//  Runs on a configurable interval (~1 second).
//  Analyses accumulated intel, evaluates the tactical situation,
//  and selects a PvPTacticalAction for the bot to pursue until
//  the next evaluation — unless the ReactiveLayer overrides.
//
//  Uses Utility AI scoring for all candidate actions.
//  Industry pattern: Utility-Based Planning + Blackboard + Observer.
// ═══════════════════════════════════════════════════════════════════════════

public class PvPThinkingLayer : MonoBehaviour
{
    [Header("Personality (affects utility curves)")]
    [SerializeField, Range(0f, 1f)] private float aggression = 0.5f;
    [SerializeField, Range(0f, 1f)] private float caution    = 0.5f;
    [SerializeField, Range(0f, 1f)] private float discipline = 0.7f;

    [Header("Intel Freshness")]
    [Tooltip("Seconds before intel is considered stale.")]
    [SerializeField] private float intelFreshnessWindow = 15f;

    [Header("Debug")]
    [SerializeField] private bool showUtilityScores = false;

    private PvPBotController core;
    private PvPTacticalAction currentAction = PvPTacticalAction.Patrol;
    private Dictionary<PvPTacticalAction, float> lastScores =
        new Dictionary<PvPTacticalAction, float>();

    public PvPTacticalAction CurrentAction => currentAction;

    public void Initialize(PvPBotController controller)
    {
        core = controller;
    }

    // ═════════════════════════════════════════════════════════════════════
    //  EVALUATE — called on interval by PvPBotController
    // ═════════════════════════════════════════════════════════════════════

    public void Evaluate()
    {
        lastScores.Clear();

        PvPTacticalAction best = PvPTacticalAction.Patrol;
        float bestScore = 0f;

        foreach (PvPTacticalAction action in System.Enum.GetValues(typeof(PvPTacticalAction)))
        {
            float score = ScoreAction(action);
            lastScores[action] = score;

            if (score > bestScore)
            {
                bestScore = score;
                best = action;
            }
        }

        currentAction = best;
        core.Blackboard.SetFloat(PvPBlackboardKey.LastThinkTime, Time.time);

        // Execute the chosen tactical action
        ExecuteAction(best);

        if (showUtilityScores)
        {
            string log = $"[Thinking] {core.name} → {best} ({bestScore:F2})\n";
            foreach (var kv in lastScores)
                log += $"  {kv.Key}: {kv.Value:F2}\n";
            Debug.Log(log);
        }
    }

    // ═════════════════════════════════════════════════════════════════════
    //  UTILITY SCORING
    // ═════════════════════════════════════════════════════════════════════

    private float ScoreAction(PvPTacticalAction action)
    {
        switch (action)
        {
            case PvPTacticalAction.Patrol:            return Score_Patrol();
            case PvPTacticalAction.Advance:           return Score_Advance();
            case PvPTacticalAction.HoldPosition:      return Score_HoldPosition();
            case PvPTacticalAction.ClearRoom:         return Score_ClearRoom();
            case PvPTacticalAction.Flank:             return Score_Flank();
            case PvPTacticalAction.FallBack:          return Score_FallBack();
            case PvPTacticalAction.InvestigateIntel:  return Score_InvestigateIntel();
            case PvPTacticalAction.SetAmbush:         return Score_SetAmbush();
            case PvPTacticalAction.SupportAlly:       return Score_SupportAlly();
            case PvPTacticalAction.Regroup:           return Score_Regroup();
            default: return 0f;
        }
    }

    // ─── Individual Utility Functions ────────────────────────────────────

    /// <summary>Patrol: default behaviour when no intel exists.</summary>
    private float Score_Patrol()
    {
        var tl = core.Blackboard.Get(PvPBlackboardKey.ThreatLevel, PvPThreatLevel.None);
        if (tl != PvPThreatLevel.None) return 0.1f; // low if any threat info

        // No threat info at all — patrol is best
        return 0.55f + discipline * 0.1f;
    }

    /// <summary>Advance: push forward when we know enemy is ahead.</summary>
    private float Score_Advance()
    {
        var tl = core.Blackboard.Get(PvPBlackboardKey.ThreatLevel, PvPThreatLevel.None);
        if (tl == PvPThreatLevel.None) return 0.05f;

        float hp = core.Blackboard.GetFloat(PvPBlackboardKey.HealthPercent, 1f);
        if (hp < 0.3f) return 0.0f; // too hurt to advance

        float score = aggression * 0.5f;

        // Bonus if we have confirmed position and no visual
        if (tl == PvPThreatLevel.Confirmed)
            score += 0.2f;

        // Penalty if already engaged (reactive layer handles combat)
        if (tl == PvPThreatLevel.Engaged)
            score -= 0.3f;

        return Mathf.Clamp01(score);
    }

    /// <summary>HoldPosition: good when alert or suppressed.</summary>
    private float Score_HoldPosition()
    {
        float score = caution * 0.3f;

        bool suppressed = core.Blackboard.GetBool(PvPBlackboardKey.IsSuppressed);
        bool inCover    = core.Blackboard.GetBool(PvPBlackboardKey.IsInCover);
        bool alert      = core.Blackboard.GetBool(PvPBlackboardKey.IsAlert);

        if (suppressed) score += 0.35f;
        if (inCover)    score += 0.15f;
        if (alert)      score += 0.10f;

        return Mathf.Clamp01(score);
    }

    /// <summary>ClearRoom: high when CQB state indicates a room ahead.</summary>
    private float Score_ClearRoom()
    {
        var cqb = core.Blackboard.Get(PvPBlackboardKey.CQBState, PvPCQBState.None);

        // If movement module has identified an entry point
        Vector3 entryPt = core.Blackboard.GetVector3(PvPBlackboardKey.RoomEntryPoint);
        if (entryPt == Vector3.zero) return 0.05f;

        float dist = Vector3.Distance(core.Transform.position, entryPt);
        if (dist > 15f) return 0.1f; // too far

        float score = discipline * 0.5f + 0.25f;

        // Bonus if intel suggests enemy in that area
        IntelRecord latest = core.Blackboard.GetLatestIntel(IntelType.GunfireHeard);
        if (latest != null && latest.IsFresh(intelFreshnessWindow))
        {
            float intelDist = Vector3.Distance(latest.position, entryPt);
            if (intelDist < 20f) score += 0.2f;
        }

        return Mathf.Clamp01(score);
    }

    /// <summary>Flank: when enemy position known but no LOS.</summary>
    private float Score_Flank()
    {
        var tl = core.Blackboard.Get(PvPBlackboardKey.ThreatLevel, PvPThreatLevel.None);
        if (tl == PvPThreatLevel.None) return 0f;

        Transform target = core.Blackboard.GetTransform(PvPBlackboardKey.CurrentTarget);
        if (target != null)
        {
            bool hasLOS = core.LineOfSight != null &&
                          core.LineOfSight.HasLineOfSight(target);
            if (hasLOS) return 0f; // reactive layer handles visible
        }

        float score = aggression * 0.4f + discipline * 0.3f;

        float hp = core.Blackboard.GetFloat(PvPBlackboardKey.HealthPercent, 1f);
        if (hp < 0.4f) score *= 0.5f; // risky when hurt

        return Mathf.Clamp01(score);
    }

    /// <summary>FallBack: when health is low or heavily suppressed.</summary>
    private float Score_FallBack()
    {
        float hp = core.Blackboard.GetFloat(PvPBlackboardKey.HealthPercent, 1f);
        bool suppressed = core.Blackboard.GetBool(PvPBlackboardKey.IsSuppressed);

        float score = 0f;
        if (hp < 0.25f) score += 0.6f;
        else if (hp < 0.5f) score += 0.3f;

        if (suppressed) score += 0.2f;

        score += caution * 0.15f;
        score -= aggression * 0.1f;

        return Mathf.Clamp01(score);
    }

    /// <summary>InvestigateIntel: move to a suspected location.</summary>
    private float Score_InvestigateIntel()
    {
        var freshIntel = core.Blackboard.GetFreshIntel(intelFreshnessWindow);
        if (freshIntel.Count == 0) return 0f;

        // Find highest-confidence unconfirmed intel
        float bestConf = 0f;
        foreach (var rec in freshIntel)
        {
            if (rec.type == IntelType.VisualContact) continue; // already engaged
            if (rec.confidence > bestConf)
                bestConf = rec.confidence;
        }

        float score = bestConf * 0.4f + aggression * 0.2f + discipline * 0.15f;
        return Mathf.Clamp01(score);
    }

    /// <summary>SetAmbush: when approaching a chokepoint with suspected enemy.</summary>
    private float Score_SetAmbush()
    {
        var tl = core.Blackboard.Get(PvPBlackboardKey.ThreatLevel, PvPThreatLevel.None);
        if (tl == PvPThreatLevel.None) return 0.05f;
        if (tl == PvPThreatLevel.Engaged) return 0f; // too late for ambush

        float score = caution * 0.3f + discipline * 0.3f;

        int nearAllies = core.Squad != null ? core.Squad.GetNearbyAllyCount(20f) : 0;
        if (nearAllies >= 1) score += 0.15f; // ambush works better with backup

        bool inCover = core.Blackboard.GetBool(PvPBlackboardKey.IsInCover);
        if (inCover) score += 0.1f;

        return Mathf.Clamp01(score);
    }

    /// <summary>SupportAlly: when a squad mate is engaged nearby.</summary>
    private float Score_SupportAlly()
    {
        if (core.Squad == null) return 0f;

        IntelRecord allyDamaged = core.Blackboard.GetLatestIntel(IntelType.AllyDamaged);
        IntelRecord allyKilled  = core.Blackboard.GetLatestIntel(IntelType.AllyKilled);

        float score = 0f;

        if (allyDamaged != null && allyDamaged.IsFresh(10f))
        {
            float dist = Vector3.Distance(core.Transform.position, allyDamaged.position);
            if (dist < 50f) score += 0.4f;
        }

        if (allyKilled != null && allyKilled.IsFresh(15f))
        {
            float dist = Vector3.Distance(core.Transform.position, allyKilled.position);
            if (dist < 50f) score += 0.3f;
        }

        score += discipline * 0.1f;
        return Mathf.Clamp01(score);
    }

    /// <summary>Regroup: when isolated from squad.</summary>
    private float Score_Regroup()
    {
        if (core.Squad == null) return 0f;

        int nearAllies = core.Squad.GetNearbyAllyCount(25f);
        if (nearAllies >= 1) return 0.05f; // not isolated

        float score = 0.30f + caution * 0.2f;
        float hp = core.Blackboard.GetFloat(PvPBlackboardKey.HealthPercent, 1f);
        if (hp < 0.5f) score += 0.15f;

        return Mathf.Clamp01(score);
    }

    // ═════════════════════════════════════════════════════════════════════
    //  ACTION EXECUTION
    // ═════════════════════════════════════════════════════════════════════

    private void ExecuteAction(PvPTacticalAction action)
    {
        switch (action)
        {
            case PvPTacticalAction.Patrol:
                core.Movement?.BeginPatrol();
                break;

            case PvPTacticalAction.Advance:
                Vector3 advTarget = core.Blackboard.GetVector3(PvPBlackboardKey.PredictedEnemyPosition);
                if (advTarget != Vector3.zero)
                    core.Movement?.TacticalAdvanceTo(advTarget);
                else
                    core.Movement?.BeginPatrol();
                break;

            case PvPTacticalAction.HoldPosition:
                core.Movement?.HoldPosition();
                core.WeaponController?.RaiseWeapon();
                break;

            case PvPTacticalAction.ClearRoom:
                Vector3 entry = core.Blackboard.GetVector3(PvPBlackboardKey.RoomEntryPoint);
                if (entry != Vector3.zero)
                    core.Movement?.BeginRoomClear(entry);
                break;

            case PvPTacticalAction.Flank:
                Vector3 enemyPos = core.Blackboard.GetVector3(PvPBlackboardKey.LastKnownEnemyPosition);
                if (enemyPos != Vector3.zero)
                    core.Movement?.FlankPosition(enemyPos);
                break;

            case PvPTacticalAction.FallBack:
                core.Movement?.FallBack();
                break;

            case PvPTacticalAction.InvestigateIntel:
                IntelRecord intel = GetBestIntelToInvestigate();
                if (intel != null)
                    core.Movement?.TacticalAdvanceTo(intel.position);
                break;

            case PvPTacticalAction.SetAmbush:
                core.Movement?.SetupAmbush();
                core.WeaponController?.RaiseWeapon();
                break;

            case PvPTacticalAction.SupportAlly:
                Vector3 allyPos = GetAllyInNeedPosition();
                if (allyPos != Vector3.zero)
                    core.Movement?.TacticalAdvanceTo(allyPos);
                break;

            case PvPTacticalAction.Regroup:
                Vector3 leaderPos = core.Blackboard.GetVector3(PvPBlackboardKey.SquadLeaderPosition);
                if (leaderPos != Vector3.zero)
                    core.Movement?.MoveToPosition(leaderPos, 4f);
                else
                    core.Movement?.BeginPatrol();
                break;
        }
    }

    // ─── Helpers ─────────────────────────────────────────────────────────

    private IntelRecord GetBestIntelToInvestigate()
    {
        var fresh = core.Blackboard.GetFreshIntel(intelFreshnessWindow);
        IntelRecord best = null;
        float bestScore = 0f;

        foreach (var rec in fresh)
        {
            if (rec.type == IntelType.VisualContact) continue;
            float score = rec.confidence * (1f - rec.Age / intelFreshnessWindow);
            if (score > bestScore)
            {
                bestScore = score;
                best = rec;
            }
        }
        return best;
    }

    private Vector3 GetAllyInNeedPosition()
    {
        IntelRecord allyDmg = core.Blackboard.GetLatestIntel(IntelType.AllyDamaged);
        IntelRecord allyKia = core.Blackboard.GetLatestIntel(IntelType.AllyKilled);

        if (allyDmg != null && allyDmg.IsFresh(10f)) return allyDmg.position;
        if (allyKia != null && allyKia.IsFresh(15f)) return allyKia.position;
        return Vector3.zero;
    }
}
