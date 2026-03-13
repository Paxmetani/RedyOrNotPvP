using UnityEngine;
using System.Collections.Generic;

// ═══════════════════════════════════════════════════════════════════════════
//  PvP REACTIVE LAYER  (Utility-based reflex system)
//
//  Runs every frame.  Each possible reflex is scored with a Utility value
//  and the highest-scoring reflex executes.  This is the "instant" layer
//  that keeps the bot alive in close-quarters firefights.
//
//  Industry pattern: Utility AI / Stimulus-Response with scored selection.
// ═══════════════════════════════════════════════════════════════════════════

public class PvPReactiveLayer : MonoBehaviour
{
    [Header("Reaction")]
    [SerializeField] private float reactionDelay = 0.08f;  // ~human twitch time

    [Header("Thresholds")]
    [SerializeField] private float nearGunfireRadius    = 35f;
    [SerializeField] private float suppressionFireRange = 25f;
    [SerializeField] private float immediateEngageRange = 40f;

    [Header("Debug")]
    [SerializeField] private bool showDebugLogs = false;

    private PvPBotController core;
    private float lastReflexTime = -999f;
    private bool  isReacting     = false;

    public bool IsReacting => isReacting;

    // ─── Reflex descriptors for utility scoring ──────────────────────────

    private struct ReflexCandidate
    {
        public float utility;
        public System.Action execute;
    }

    public void Initialize(PvPBotController controller)
    {
        core = controller;
    }

    // ═════════════════════════════════════════════════════════════════════
    //  MAIN ENTRY — called every frame by PvPBotController
    //  Returns TRUE if a reflex fired (blocks ThinkingLayer this frame).
    // ═════════════════════════════════════════════════════════════════════

    public bool ProcessReflexes()
    {
        if (core.IsDead()) return false;

        // Enforce minimum reaction delay
        if (Time.time - lastReflexTime < reactionDelay) return isReacting;

        // Build candidate list — every reflex self-scores
        var candidates = new List<ReflexCandidate>(6);

        ScoreEngageVisible(candidates);
        ScoreReturnFire(candidates);
        ScoreReactToGunfire(candidates);
        ScoreGetToCover(candidates);
        ScoreTrackLostTarget(candidates);
        ScoreReactToSound(candidates);

        // Pick highest utility
        ReflexCandidate best = default;
        float bestUtility = 0f;

        for (int i = 0; i < candidates.Count; i++)
        {
            if (candidates[i].utility > bestUtility)
            {
                bestUtility = candidates[i].utility;
                best = candidates[i];
            }
        }

        if (bestUtility > 0f && best.execute != null)
        {
            best.execute.Invoke();
            lastReflexTime = Time.time;
            isReacting = true;
            return true;
        }

        isReacting = false;
        return false;
    }

    // ═════════════════════════════════════════════════════════════════════
    //  REFLEX SCORERS
    // ═════════════════════════════════════════════════════════════════════

    /// <summary>
    /// R1 — Visible enemy → ENGAGE.
    /// Highest-priority reflex when we have confirmed line of sight.
    /// </summary>
    private void ScoreEngageVisible(List<ReflexCandidate> list)
    {
        Transform target = core.Blackboard.GetTransform(PvPBlackboardKey.CurrentTarget);
        if (target == null) return;

        bool hasLOS = core.LineOfSight != null &&
                      core.LineOfSight.HasLineOfSight(target);
        if (!hasLOS) return;

        float dist = Vector3.Distance(core.Transform.position, target.position);
        if (dist > immediateEngageRange * 2f) return; // beyond effective range

        // Utility: closer enemy = higher urgency
        float proximityFactor = 1f - Mathf.Clamp01(dist / (immediateEngageRange * 2f));
        float utility = 0.8f + proximityFactor * 0.2f; // 0.80 – 1.00

        list.Add(new ReflexCandidate
        {
            utility = utility,
            execute = () => ExecuteEngage(target)
        });
    }

    private void ExecuteEngage(Transform target)
    {
        // Update blackboard with fresh contact
        core.Blackboard.SetTransform(PvPBlackboardKey.CurrentTarget, target);
        core.Blackboard.SetVector3(PvPBlackboardKey.LastKnownEnemyPosition, target.position);
        core.Blackboard.SetFloat(PvPBlackboardKey.LastContactTime, Time.time);
        core.Blackboard.SetBool(PvPBlackboardKey.InCombat, true);
        core.Blackboard.Set(PvPBlackboardKey.ThreatLevel, PvPThreatLevel.Engaged);

        // Orient and engage
        Vector3 dir = (target.position - core.Transform.position).normalized;
        dir.y = 0f;
        if (dir != Vector3.zero)
            core.Movement?.SnapToDirection(dir);

        core.WeaponController?.RaiseWeapon();
        core.Combat?.EngageTarget(target);

        // Push intel record
        core.Blackboard.PushIntel(new IntelRecord
        {
            type       = IntelType.VisualContact,
            position   = target.position,
            timestamp  = Time.time,
            confidence = 1f,
            source     = target
        });

        // Share with squad
        core.Squad?.ReportContact(target.position, true);

        if (showDebugLogs)
            Debug.Log($"[Reactive] {core.name} — ENGAGING {target.name}");
    }

    // ─────────────────────────────────────────────────────────────────────

    /// <summary>
    /// R2 — Just took damage → RETURN FIRE toward estimated source.
    /// </summary>
    private void ScoreReturnFire(List<ReflexCandidate> list)
    {
        if (!core.Blackboard.GetBool(PvPBlackboardKey.JustTookDamage)) return;

        Vector3 dmgDir = core.Blackboard.GetVector3(PvPBlackboardKey.LastDamageDirection);
        if (dmgDir == Vector3.zero) return;

        float hp = core.Blackboard.GetFloat(PvPBlackboardKey.HealthPercent, 1f);
        // Lower HP → higher urgency to return fire
        float utility = 0.70f + (1f - hp) * 0.15f;

        list.Add(new ReflexCandidate
        {
            utility = utility,
            execute = () => ExecuteReturnFire(dmgDir)
        });
    }

    private void ExecuteReturnFire(Vector3 dmgDir)
    {
        Vector3 estimatedSource = core.Transform.position + dmgDir.normalized * 15f;

        Vector3 lookDir = dmgDir.normalized;
        lookDir.y = 0f;
        if (lookDir != Vector3.zero)
            core.Movement?.SnapToDirection(lookDir);

        core.WeaponController?.RaiseWeapon();
        core.Combat?.SuppressPosition(estimatedSource);

        core.Blackboard.SetBool(PvPBlackboardKey.JustTookDamage, false);
        core.Blackboard.SetBool(PvPBlackboardKey.InCombat, true);
        core.Blackboard.Set(PvPBlackboardKey.ThreatLevel, PvPThreatLevel.Confirmed);
        core.Blackboard.SetVector3(PvPBlackboardKey.PredictedEnemyPosition, estimatedSource);

        if (showDebugLogs)
            Debug.Log($"[Reactive] {core.name} — RETURN FIRE!");
    }

    // ─────────────────────────────────────────────────────────────────────

    /// <summary>
    /// R3 — Nearby gunfire heard → orient and prepare.
    /// </summary>
    private void ScoreReactToGunfire(List<ReflexCandidate> list)
    {
        var soundType = core.Blackboard.Get(PvPBlackboardKey.LastSoundType, PvPSoundType.None);
        if (soundType != PvPSoundType.Gunshot) return;

        float timeSince = Time.time - core.Blackboard.GetFloat(PvPBlackboardKey.LastSoundTime, -999f);
        if (timeSince > 1.5f) return; // stale

        Vector3 soundPos = core.Blackboard.GetVector3(PvPBlackboardKey.LastHeardPosition);
        float dist = Vector3.Distance(core.Transform.position, soundPos);
        if (dist > nearGunfireRadius) return;

        float proximity = 1f - Mathf.Clamp01(dist / nearGunfireRadius);
        float utility = 0.50f + proximity * 0.25f; // 0.50 – 0.75

        list.Add(new ReflexCandidate
        {
            utility = utility,
            execute = () => ExecuteReactToGunfire(soundPos, dist)
        });
    }

    private void ExecuteReactToGunfire(Vector3 soundPos, float dist)
    {
        Vector3 dir = (soundPos - core.Transform.position).normalized;
        dir.y = 0f;
        if (dir != Vector3.zero)
            core.Movement?.SnapToDirection(dir);

        core.WeaponController?.RaiseWeapon();
        core.Blackboard.Set(PvPBlackboardKey.ThreatLevel, PvPThreatLevel.Suspected);
        core.Blackboard.SetVector3(PvPBlackboardKey.PredictedEnemyPosition, soundPos);
        core.Blackboard.SetBool(PvPBlackboardKey.IsAlert, true);

        // Very close → suppressive fire
        if (dist < suppressionFireRange)
        {
            core.Combat?.SuppressPosition(soundPos);
        }

        // Push intel
        core.Blackboard.PushIntel(new IntelRecord
        {
            type       = IntelType.GunfireHeard,
            position   = soundPos,
            timestamp  = Time.time,
            confidence = 0.5f
        });

        core.Squad?.ReportContact(soundPos, false);

        if (showDebugLogs)
            Debug.Log($"[Reactive] {core.name} — gunfire at {dist:F0}m");
    }

    // ─────────────────────────────────────────────────────────────────────

    /// <summary>
    /// R4 — Under suppression → seek cover.
    /// </summary>
    private void ScoreGetToCover(List<ReflexCandidate> list)
    {
        if (!core.Blackboard.GetBool(PvPBlackboardKey.IsSuppressed)) return;
        if (core.Blackboard.GetBool(PvPBlackboardKey.IsInCover)) return; // already safe

        float utility = 0.65f;
        list.Add(new ReflexCandidate
        {
            utility = utility,
            execute = ExecuteGetToCover
        });
    }

    private void ExecuteGetToCover()
    {
        core.Movement?.SeekCover();
        isReacting = true;

        if (showDebugLogs)
            Debug.Log($"[Reactive] {core.name} — seeking cover!");
    }

    // ─────────────────────────────────────────────────────────────────────

    /// <summary>
    /// R5 — Lost visual on target but within memory window →
    ///       maintain facing, search last known position.
    /// </summary>
    private void ScoreTrackLostTarget(List<ReflexCandidate> list)
    {
        if (!core.Blackboard.GetBool(PvPBlackboardKey.InCombat)) return;

        Transform target = core.Blackboard.GetTransform(PvPBlackboardKey.CurrentTarget);
        if (target != null)
        {
            // Still have a reference — check LOS
            bool hasLOS = core.LineOfSight != null &&
                          core.LineOfSight.HasLineOfSight(target);
            if (hasLOS) return; // visible — handled by R1
        }

        Vector3 lastKnown = core.Blackboard.GetVector3(PvPBlackboardKey.LastKnownEnemyPosition);
        if (lastKnown == Vector3.zero) return;

        float timeLost = Time.time - core.Blackboard.GetFloat(PvPBlackboardKey.LastContactTime, -999f);
        if (timeLost > 8f) return; // memory about to decay via controller

        float utility = 0.40f + (1f - Mathf.Clamp01(timeLost / 8f)) * 0.20f; // 0.40 – 0.60

        list.Add(new ReflexCandidate
        {
            utility = utility,
            execute = () => ExecuteTrackLost(lastKnown, timeLost)
        });
    }

    private void ExecuteTrackLost(Vector3 lastKnown, float timeLost)
    {
        Vector3 dir = (lastKnown - core.Transform.position).normalized;
        dir.y = 0f;
        if (dir != Vector3.zero)
            core.Movement?.SetLookDirection(dir);

        core.WeaponController?.RaiseWeapon();

        // First 3 seconds — suppressive fire
        if (timeLost < 3f)
        {
            core.Combat?.SuppressPosition(lastKnown);
        }
        // 3-8 seconds — cautious advance toward last known
        else
        {
            core.Movement?.MoveToPosition(lastKnown, 2f);
        }

        if (showDebugLogs && Time.frameCount % 60 == 0)
            Debug.Log($"[Reactive] {core.name} — tracking lost target ({timeLost:F1}s)");
    }

    // ─────────────────────────────────────────────────────────────────────

    /// <summary>
    /// R6 — Non-gunshot sound → orient (low utility, does not block thinking).
    /// </summary>
    private void ScoreReactToSound(List<ReflexCandidate> list)
    {
        var tl = core.Blackboard.Get(PvPBlackboardKey.ThreatLevel, PvPThreatLevel.None);
        if (tl != PvPThreatLevel.Suspected) return;

        Vector3 soundPos = core.Blackboard.GetVector3(PvPBlackboardKey.LastHeardPosition);
        if (soundPos == Vector3.zero) return;

        float utility = 0.20f;

        list.Add(new ReflexCandidate
        {
            utility = utility,
            execute = () =>
            {
                Vector3 d = (soundPos - core.Transform.position).normalized;
                d.y = 0f;
                if (d != Vector3.zero) core.Movement?.SetLookDirection(d);
                core.WeaponController?.RaiseWeapon();
            }
        });
    }
}
