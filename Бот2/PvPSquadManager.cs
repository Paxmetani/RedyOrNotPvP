using UnityEngine;
using System.Collections.Generic;
using System.Linq;

// ═══════════════════════════════════════════════════════════════════════════
//  PvP SQUAD MANAGER  (Singleton – global coordination)
//
//  Central registry of all PvP bots.  Handles:
//    • Team-filtered intel sharing within comms range.
//    • Sound propagation to nearby enemy/friendly bots.
//    • Ally-death notifications.
//    • Queries (alive count, nearby allies, etc.).
//
//  Pattern: Singleton + Observer / Mediator.
// ═══════════════════════════════════════════════════════════════════════════

public class PvPSquadManager : MonoBehaviour
{
    public static PvPSquadManager Instance { get; private set; }

    [Header("Settings")]
    [SerializeField] private float commsRange     = 60f;
    [SerializeField] private float commsInterval  = 0.5f;

    private readonly List<PvPBotController> allBots = new List<PvPBotController>();
    private float nextCommsTime = 0f;

    // ═════════════════════════════════════════════════════════════════════

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
    }

    private void Update()
    {
        if (Time.time < nextCommsTime) return;
        nextCommsTime = Time.time + commsInterval;
        ProcessPeriodicComms();
    }

    // ═════════════════════════════════════════════════════════════════════
    //  REGISTRATION
    // ═════════════════════════════════════════════════════════════════════

    public void Register(PvPBotController bot)
    {
        if (!allBots.Contains(bot))
        {
            allBots.Add(bot);
            Debug.Log($"[PvPSquad] Registered {bot.name}  (total: {allBots.Count})");
        }
    }

    public void Unregister(PvPBotController bot)
    {
        allBots.Remove(bot);
        BroadcastAllyDeath(bot);
        Debug.Log($"[PvPSquad] Unregistered {bot.name}  (remaining: {allBots.Count})");
    }

    // ═════════════════════════════════════════════════════════════════════
    //  PERIODIC INTEL SHARING
    // ═════════════════════════════════════════════════════════════════════

    private void ProcessPeriodicComms()
    {
        foreach (var bot in allBots.ToList())
        {
            if (bot == null || bot.IsDead()) continue;

            // If this bot has a confirmed target, share with same-team nearby
            Transform target = bot.Blackboard.GetTransform(PvPBlackboardKey.CurrentTarget);
            if (target == null) continue;

            Vector3 enemyPos = bot.Blackboard.GetVector3(PvPBlackboardKey.LastKnownEnemyPosition);
            bool confirmed = bot.Blackboard.Get(PvPBlackboardKey.ThreatLevel, PvPThreatLevel.None)
                             == PvPThreatLevel.Confirmed ||
                             bot.Blackboard.Get(PvPBlackboardKey.ThreatLevel, PvPThreatLevel.None)
                             == PvPThreatLevel.Engaged;

            ShareContact(bot, enemyPos, confirmed);
        }
    }

    // ═════════════════════════════════════════════════════════════════════
    //  CONTACT SHARING
    // ═════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Share a contact report from <paramref name="sender"/> to same-team
    /// allies within comms range.
    /// </summary>
    public void ShareContact(PvPBotController sender, Vector3 position, bool confirmed)
    {
        var nearby = GetSameTeamNearby(sender, commsRange);
        float confidence = confirmed ? 0.9f : 0.5f;

        foreach (var ally in nearby)
        {
            ally.Squad?.ReceiveIntel(position, confirmed, confidence);
        }
    }

    // ═════════════════════════════════════════════════════════════════════
    //  SOUND PROPAGATION
    // ═════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Notify ALL bots (both teams) within hearing range of a sound event.
    /// Enemy bots hearing enemy fire will react via their Perception module.
    /// </summary>
    public void NotifySound(Vector3 position, PvPSoundType type, float intensity,
                            PvPBotController shooter = null)
    {
        foreach (var bot in allBots)
        {
            if (bot == null || bot.IsDead()) continue;
            if (bot == shooter) continue; // don't notify self

            bot.Perception?.OnHearSound(position, type, intensity);
        }
    }

    // ═════════════════════════════════════════════════════════════════════
    //  ALLY DEATH
    // ═════════════════════════════════════════════════════════════════════

    private void BroadcastAllyDeath(PvPBotController deadBot)
    {
        Vector3 deathPos = deadBot.Transform.position;
        string  team     = deadBot.MyTeamTag;

        foreach (var bot in allBots.ToList())
        {
            if (bot == null || bot == deadBot || bot.IsDead()) continue;
            if (bot.MyTeamTag != team) continue;

            float dist = Vector3.Distance(bot.Transform.position, deathPos);
            if (dist <= commsRange)
            {
                bot.Squad?.OnAllyKilled(deathPos);
            }
        }
    }

    // ═════════════════════════════════════════════════════════════════════
    //  QUERIES
    // ═════════════════════════════════════════════════════════════════════

    /// <summary>All currently registered bots (both teams).</summary>
    public IReadOnlyList<PvPBotController> GetAllBots() => allBots;

    /// <summary>Alive same-team allies within radius of a position.</summary>
    public int GetNearbyAllyCount(Vector3 pos, float radius, string teamTag)
    {
        int count = 0;
        foreach (var bot in allBots)
        {
            if (bot == null || bot.IsDead()) continue;
            if (bot.MyTeamTag != teamTag) continue;
            if (Vector3.Distance(pos, bot.Transform.position) <= radius)
                count++;
        }
        return count;
    }

    /// <summary>All alive bots on a given team.</summary>
    public List<PvPBotController> GetTeamBots(string teamTag)
    {
        var list = new List<PvPBotController>();
        foreach (var bot in allBots)
        {
            if (bot != null && !bot.IsDead() && bot.MyTeamTag == teamTag)
                list.Add(bot);
        }
        return list;
    }

    // ─── Helpers ─────────────────────────────────────────────────────────

    private List<PvPBotController> GetSameTeamNearby(PvPBotController origin, float range)
    {
        var result = new List<PvPBotController>();
        foreach (var bot in allBots)
        {
            if (bot == null || bot == origin || bot.IsDead()) continue;
            if (bot.MyTeamTag != origin.MyTeamTag) continue;
            if (Vector3.Distance(origin.Transform.position, bot.Transform.position) <= range)
                result.Add(bot);
        }
        return result;
    }

    // ─── Gizmo ───────────────────────────────────────────────────────────

#if UNITY_EDITOR
    private void OnDrawGizmosSelected()
    {
        foreach (var bot in allBots)
        {
            if (bot == null) continue;
            var nearby = GetSameTeamNearby(bot, commsRange);
            Gizmos.color = bot.MyTeamTag == "TeamA" ? Color.cyan : Color.red;
            foreach (var ally in nearby)
            {
                Gizmos.DrawLine(bot.Transform.position + Vector3.up,
                                ally.Transform.position + Vector3.up);
            }
        }
    }
#endif
}
