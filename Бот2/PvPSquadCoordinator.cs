using UnityEngine;
using System.Collections.Generic;
using System.Linq;

// ═══════════════════════════════════════════════════════════════════════════
//  PvP SQUAD COORDINATOR
//
//  Per-bot module that interfaces with the global PvPSquadManager.
//  Responsibilities:
//    • Register / unregister with squad manager.
//    • Share contact reports with teammates.
//    • Maintain squad role (PointMan, Rifleman, Support, RearGuard).
//    • Provide helper queries (nearby ally count, leader position).
// ═══════════════════════════════════════════════════════════════════════════

public class PvPSquadCoordinator : MonoBehaviour
{
    [Header("Communication")]
    [SerializeField] private float commsRange = 60f;

    private PvPBotController core;

    // ═════════════════════════════════════════════════════════════════════

    public void Initialize(PvPBotController controller)
    {
        core = controller;
    }

    // ═════════════════════════════════════════════════════════════════════
    //  REGISTRATION
    // ═════════════════════════════════════════════════════════════════════

    public void RegisterWithManager()
    {
        if (PvPSquadManager.Instance != null)
            PvPSquadManager.Instance.Register(core);
    }

    public void UnregisterFromManager()
    {
        if (PvPSquadManager.Instance != null)
            PvPSquadManager.Instance.Unregister(core);
    }

    // ═════════════════════════════════════════════════════════════════════
    //  CONTACT REPORTS
    // ═════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Report a contact (visual or suspected) to nearby teammates.
    /// </summary>
    public void ReportContact(Vector3 position, bool confirmed)
    {
        if (PvPSquadManager.Instance == null) return;

        PvPSquadManager.Instance.ShareContact(core, position, confirmed);
    }

    /// <summary>
    /// Broadcast a firing event so nearby AI can hear the shot.
    /// </summary>
    public void BroadcastFiring(Vector3 position)
    {
        if (PvPSquadManager.Instance == null) return;

        PvPSquadManager.Instance.NotifySound(position, PvPSoundType.Gunshot, 1f, core);
    }

    /// <summary>
    /// Called by PvPSquadManager when a teammate shares intel.
    /// </summary>
    public void ReceiveIntel(Vector3 position, bool confirmed, float confidence)
    {
        var currentTL = core.Blackboard.Get(PvPBlackboardKey.ThreatLevel, PvPThreatLevel.None);

        // Don't downgrade confirmed intel
        if (currentTL == PvPThreatLevel.Confirmed && !confirmed) return;

        if (currentTL == PvPThreatLevel.None ||
            (confirmed && currentTL == PvPThreatLevel.Suspected))
        {
            core.Blackboard.SetVector3(PvPBlackboardKey.PredictedEnemyPosition, position);
            core.Blackboard.Set(PvPBlackboardKey.ThreatLevel,
                confirmed ? PvPThreatLevel.Confirmed : PvPThreatLevel.Suspected);
            core.Blackboard.SetBool(PvPBlackboardKey.IsAlert, true);
        }

        // Always push intel record for ThinkingLayer analysis
        core.Blackboard.PushIntel(new IntelRecord
        {
            type       = IntelType.SquadCallout,
            position   = position,
            timestamp  = Time.time,
            confidence = confidence
        });
    }

    /// <summary>
    /// Called by PvPSquadManager when a teammate dies.
    /// </summary>
    public void OnAllyKilled(Vector3 deathPosition)
    {
        core.Blackboard.PushIntel(new IntelRecord
        {
            type       = IntelType.AllyKilled,
            position   = deathPosition,
            timestamp  = Time.time,
            confidence = 1f
        });

        // Investigate if no current threat
        var tl = core.Blackboard.Get(PvPBlackboardKey.ThreatLevel, PvPThreatLevel.None);
        if (tl == PvPThreatLevel.None)
        {
            core.Blackboard.SetVector3(PvPBlackboardKey.PredictedEnemyPosition, deathPosition);
            core.Blackboard.Set(PvPBlackboardKey.ThreatLevel, PvPThreatLevel.Suspected);
        }
    }

    // ═════════════════════════════════════════════════════════════════════
    //  QUERIES
    // ═════════════════════════════════════════════════════════════════════

    /// <summary>Number of alive same-team allies within given radius.</summary>
    public int GetNearbyAllyCount(float radius)
    {
        if (PvPSquadManager.Instance == null) return 0;
        return PvPSquadManager.Instance.GetNearbyAllyCount(
            core.Transform.position, radius, core.MyTeamTag);
    }
}
