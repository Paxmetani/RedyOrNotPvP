using UnityEngine;
using System.Collections.Generic;

// ═══════════════════════════════════════════════════════════════════════════
//  PvP PERCEPTION MODULE
//
//  Enhanced sensory system for PvP team combat.
//  • Central + Peripheral vision with alert modifiers.
//  • Categorised hearing (gunshot, footstep, explosion, reload, breach).
//  • Contact tracking — maintains a short list of recent contacts with
//    timestamp and confidence, feeding the Intel system.
//  • Operates on same-team filtering via team tags.
// ═══════════════════════════════════════════════════════════════════════════

public class PvPPerceptionModule : MonoBehaviour
{
    // ─── Vision Settings ─────────────────────────────────────────────────

    [Header("Vision")]
    [SerializeField] private float centralVisionRange  = 60f;
    [SerializeField] private float centralVisionAngle  = 90f;
    [SerializeField] private float peripheralRange     = 25f;
    [SerializeField] private float peripheralAngle     = 180f;
    [SerializeField] private float alertVisionBonus    = 1.4f;  // ×1.4 when alert
    [SerializeField] private float visionCheckInterval = 0.15f;
    [SerializeField] private LayerMask visionMask      = ~0;

    // ─── Hearing Settings ────────────────────────────────────────────────

    [Header("Hearing Ranges")]
    [SerializeField] private float hearGunshot    = 70f;
    [SerializeField] private float hearFootsteps  = 18f;
    [SerializeField] private float hearExplosion  = 90f;
    [SerializeField] private float hearReload     = 12f;
    [SerializeField] private float hearDoorBreach = 30f;
    [SerializeField] private float hearVoice      = 25f;

    // ─── Contact Tracking ────────────────────────────────────────────────

    [Header("Contact Tracking")]
    [SerializeField] private float contactMemory = 12f;  // seconds to track a contact
    [SerializeField] private int   maxContacts   = 8;

    // ─── Team ────────────────────────────────────────────────────────────

    private string myTeamTag    = "TeamA";
    private string enemyTeamTag = "TeamB";

    // ─── Internal ────────────────────────────────────────────────────────

    private PvPBotController core;
    private float nextVisionCheck = 0f;
    private Transform currentVisibleTarget = null;

    /// <summary>
    /// Known enemy contacts (pruned every vision tick).
    /// </summary>
    private List<ContactEntry> contacts = new List<ContactEntry>();

    public IReadOnlyList<ContactEntry> Contacts => contacts;
    public Transform VisibleTarget => currentVisibleTarget;

    // ═════════════════════════════════════════════════════════════════════

    public void Initialize(PvPBotController controller)
    {
        core = controller;
    }

    public void SetTeams(string myTeam, string enemyTeam)
    {
        myTeamTag    = myTeam;
        enemyTeamTag = enemyTeam;
    }

    // ═════════════════════════════════════════════════════════════════════
    //  UPDATE (called every frame by PvPBotController)
    // ═════════════════════════════════════════════════════════════════════

    public void UpdatePerception()
    {
        if (Time.time >= nextVisionCheck)
        {
            nextVisionCheck = Time.time + visionCheckInterval;
            ScanForEnemies();
            PruneStaleContacts();
        }
    }

    // ═════════════════════════════════════════════════════════════════════
    //  VISION
    // ═════════════════════════════════════════════════════════════════════

    private void ScanForEnemies()
    {
        currentVisibleTarget = null;
        float closestDist = float.MaxValue;
        bool isAlert = core.Blackboard.GetBool(PvPBlackboardKey.IsAlert);
        float alertMul = isAlert ? alertVisionBonus : 1f;

        // Use PvPSquadManager to get all registered bots
        var manager = PvPSquadManager.Instance;
        if (manager == null) return;

        var allBots = manager.GetAllBots();
        for (int i = 0; i < allBots.Count; i++)
        {
            PvPBotController other = allBots[i];
            if (other == null || other == core || other.IsDead()) continue;
            if (other.MyTeamTag == myTeamTag) continue; // same team

            Transform t = other.Transform;
            float dist = Vector3.Distance(core.Transform.position, t.position);

            // Central vision check
            float effectiveRange = centralVisionRange * alertMul;
            float effectiveAngle = centralVisionAngle * alertMul;

            bool inCentral = dist <= effectiveRange &&
                             IsWithinAngle(t, effectiveAngle);

            // Peripheral vision check
            bool inPeripheral = dist <= peripheralRange * alertMul &&
                                IsWithinAngle(t, peripheralAngle * alertMul);

            if (!inCentral && !inPeripheral) continue;

            // Raycast LOS check
            if (!HasClearLOS(t)) continue;

            // Confidence: central is higher than peripheral
            float confidence = inCentral ? 1f : 0.6f;

            // Track / update contact
            UpdateContact(t, confidence);

            if (dist < closestDist)
            {
                closestDist = dist;
                currentVisibleTarget = t;
            }
        }

        // Write to blackboard
        if (currentVisibleTarget != null)
        {
            core.Blackboard.SetTransform(PvPBlackboardKey.CurrentTarget, currentVisibleTarget);
            core.Blackboard.SetBool(PvPBlackboardKey.TargetVisible, true);
            core.Blackboard.SetVector3(PvPBlackboardKey.LastKnownEnemyPosition,
                currentVisibleTarget.position);
            core.Blackboard.SetFloat(PvPBlackboardKey.LastContactTime, Time.time);
            core.Blackboard.Set(PvPBlackboardKey.ThreatLevel, PvPThreatLevel.Confirmed);
        }
        else
        {
            core.Blackboard.SetBool(PvPBlackboardKey.TargetVisible, false);
        }
    }

    private bool IsWithinAngle(Transform target, float maxAngle)
    {
        Vector3 dir = (target.position - core.Transform.position).normalized;
        float angle = Vector3.Angle(core.Transform.forward, dir);
        return angle <= maxAngle * 0.5f;
    }

    private bool HasClearLOS(Transform target)
    {
        Vector3 origin = core.Transform.position + Vector3.up * 1.5f;
        Vector3 dest   = target.position + Vector3.up * 1.2f;
        Vector3 dir    = dest - origin;

        if (Physics.Raycast(origin, dir.normalized, out RaycastHit hit,
                            dir.magnitude, visionMask))
        {
            // Hit something — is it the target?
            return hit.transform.root == target.root;
        }
        return true; // nothing blocking
    }

    // ═════════════════════════════════════════════════════════════════════
    //  HEARING  (called externally by sound notifiers / squad comms)
    // ═════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Called when a sound event occurs nearby.
    /// </summary>
    public void OnHearSound(Vector3 position, PvPSoundType type, float intensity)
    {
        float dist = Vector3.Distance(core.Transform.position, position);
        float maxRange = GetHearingRange(type);

        if (dist > maxRange * intensity) return;

        // Write to blackboard
        core.Blackboard.SetVector3(PvPBlackboardKey.LastHeardPosition, position);
        core.Blackboard.Set(PvPBlackboardKey.LastSoundType, type);
        core.Blackboard.SetFloat(PvPBlackboardKey.LastSoundTime, Time.time);
        core.Blackboard.SetBool(PvPBlackboardKey.UnprocessedSoundEvent, true);

        // Upgrade threat level
        var current = core.Blackboard.Get(PvPBlackboardKey.ThreatLevel, PvPThreatLevel.None);
        if (current == PvPThreatLevel.None)
        {
            core.Blackboard.Set(PvPBlackboardKey.ThreatLevel, PvPThreatLevel.Suspected);
        }

        // Push intel
        IntelType intelType = SoundToIntelType(type);
        float confidence = Mathf.Clamp01(1f - dist / maxRange);

        core.Blackboard.PushIntel(new IntelRecord
        {
            type       = intelType,
            position   = position,
            timestamp  = Time.time,
            confidence = confidence
        });
    }

    /// <summary>
    /// Called when this bot is hit — estimate attacker direction.
    /// </summary>
    public void OnBulletHitFromDirection(Vector3 hitPoint, Vector3 hitDir)
    {
        Vector3 estimated = core.Transform.position + hitDir.normalized * 20f;

        core.Blackboard.SetVector3(PvPBlackboardKey.PredictedEnemyPosition, estimated);
        core.Blackboard.SetVector3(PvPBlackboardKey.ThreatDirection, hitDir);
        core.Blackboard.Set(PvPBlackboardKey.ThreatLevel, PvPThreatLevel.Confirmed);

        core.Blackboard.PushIntel(new IntelRecord
        {
            type       = IntelType.DamageTaken,
            position   = estimated,
            timestamp  = Time.time,
            confidence = 0.5f
        });
    }

    // ═════════════════════════════════════════════════════════════════════
    //  CONTACT TRACKING
    // ═════════════════════════════════════════════════════════════════════

    private void UpdateContact(Transform target, float confidence)
    {
        for (int i = 0; i < contacts.Count; i++)
        {
            if (contacts[i].target == target)
            {
                contacts[i] = new ContactEntry
                {
                    target     = target,
                    position   = target.position,
                    lastSeen   = Time.time,
                    confidence = Mathf.Max(contacts[i].confidence, confidence)
                };
                return;
            }
        }

        // New contact
        if (contacts.Count >= maxContacts)
            contacts.RemoveAt(0);

        contacts.Add(new ContactEntry
        {
            target     = target,
            position   = target.position,
            lastSeen   = Time.time,
            confidence = confidence
        });
    }

    private void PruneStaleContacts()
    {
        float cutoff = Time.time - contactMemory;
        contacts.RemoveAll(c => c.lastSeen < cutoff || c.target == null);
    }

    // ─── Helpers ─────────────────────────────────────────────────────────

    private float GetHearingRange(PvPSoundType type)
    {
        switch (type)
        {
            case PvPSoundType.Gunshot:      return hearGunshot;
            case PvPSoundType.Footsteps:    return hearFootsteps;
            case PvPSoundType.Explosion:    return hearExplosion;
            case PvPSoundType.Reload:       return hearReload;
            case PvPSoundType.DoorBreach:   return hearDoorBreach;
            case PvPSoundType.VoiceCallout: return hearVoice;
            default: return 20f;
        }
    }

    private IntelType SoundToIntelType(PvPSoundType type)
    {
        switch (type)
        {
            case PvPSoundType.Gunshot:   return IntelType.GunfireHeard;
            case PvPSoundType.Footsteps: return IntelType.FootstepsHeard;
            case PvPSoundType.Explosion: return IntelType.ExplosionHeard;
            default:                     return IntelType.GunfireHeard;
        }
    }

    // ═════════════════════════════════════════════════════════════════════
    //  PUBLIC API
    // ═════════════════════════════════════════════════════════════════════

    public Transform GetVisibleTarget() => currentVisibleTarget;
}

// ═══════════════════════════════════════════════════════════════════════════
//  CONTACT ENTRY (lightweight tracking record)
// ═══════════════════════════════════════════════════════════════════════════

[System.Serializable]
public struct ContactEntry
{
    public Transform target;
    public Vector3   position;
    public float     lastSeen;
    public float     confidence;

    public float Age => Time.time - lastSeen;
}
