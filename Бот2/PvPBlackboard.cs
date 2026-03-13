using System.Collections.Generic;
using UnityEngine;

// ═══════════════════════════════════════════════════════════════════════════
//  PvP TACTICAL BLACKBOARD
//  Enhanced shared-state store for PvP team combat.
//  Extends the standard Blackboard pattern with typed Intel records,
//  sector status tracking, and contact-report history.
// ═══════════════════════════════════════════════════════════════════════════

/// <summary>
/// Shared data-store used by every module on a PvP tactical bot.
/// Uses the same Dictionary&lt;key, object&gt; pattern as AIBlackboard
/// but adds first-class Intel record support.
/// </summary>
public class PvPBlackboard
{
    // ─── Generic Storage ─────────────────────────────────────────────────

    private readonly Dictionary<PvPBlackboardKey, object> data =
        new Dictionary<PvPBlackboardKey, object>();

    public void Set(PvPBlackboardKey key, object value) => data[key] = value;

    public T Get<T>(PvPBlackboardKey key, T defaultValue = default)
    {
        if (data.TryGetValue(key, out object value) && value is T typed)
            return typed;
        return defaultValue;
    }

    public bool Has(PvPBlackboardKey key) => data.ContainsKey(key);
    public void Remove(PvPBlackboardKey key) => data.Remove(key);
    public void Clear() => data.Clear();

    // ─── Typed Helpers ───────────────────────────────────────────────────

    public void SetBool(PvPBlackboardKey k, bool v) => Set(k, v);
    public bool GetBool(PvPBlackboardKey k, bool d = false) => Get(k, d);

    public void SetFloat(PvPBlackboardKey k, float v) => Set(k, v);
    public float GetFloat(PvPBlackboardKey k, float d = 0f) => Get(k, d);

    public void SetInt(PvPBlackboardKey k, int v) => Set(k, v);
    public int GetInt(PvPBlackboardKey k, int d = 0) => Get(k, d);

    public void SetVector3(PvPBlackboardKey k, Vector3 v) => Set(k, v);
    public Vector3 GetVector3(PvPBlackboardKey k) => Get(k, Vector3.zero);

    public void SetTransform(PvPBlackboardKey k, Transform v) => Set(k, v);
    public Transform GetTransform(PvPBlackboardKey k) => Get<Transform>(k, null);

    // ─── Intel Records ───────────────────────────────────────────────────

    private readonly List<IntelRecord> intelLog = new List<IntelRecord>();
    private const int MaxIntelRecords = 64;

    public IReadOnlyList<IntelRecord> IntelLog => intelLog;

    /// <summary>
    /// Push a new intel record.  Oldest records are evicted when the log
    /// exceeds <see cref="MaxIntelRecords"/>.
    /// </summary>
    public void PushIntel(IntelRecord record)
    {
        intelLog.Add(record);
        if (intelLog.Count > MaxIntelRecords)
            intelLog.RemoveAt(0);
    }

    /// <summary>
    /// Return the most recent intel record of a given type, or null.
    /// </summary>
    public IntelRecord GetLatestIntel(IntelType type)
    {
        for (int i = intelLog.Count - 1; i >= 0; i--)
        {
            if (intelLog[i].type == type) return intelLog[i];
        }
        return null;
    }

    /// <summary>
    /// Return all intel records that are still "fresh" (within maxAge seconds).
    /// </summary>
    public List<IntelRecord> GetFreshIntel(float maxAge)
    {
        float cutoff = Time.time - maxAge;
        var result = new List<IntelRecord>();
        for (int i = intelLog.Count - 1; i >= 0; i--)
        {
            if (intelLog[i].timestamp >= cutoff)
                result.Add(intelLog[i]);
            else
                break; // older records are before this index
        }
        return result;
    }

    public void ClearIntel() => intelLog.Clear();
}

// ═══════════════════════════════════════════════════════════════════════════
//  INTEL RECORD
// ═══════════════════════════════════════════════════════════════════════════

/// <summary>
/// A single piece of intelligence gathered by perception or squad comms.
/// </summary>
[System.Serializable]
public class IntelRecord
{
    public IntelType type;
    public Vector3   position;
    public float     timestamp;
    public float     confidence;   // 0 = rumor, 1 = confirmed visual
    public Transform source;       // may be null if origin unknown

    public float Age => Time.time - timestamp;
    public bool  IsFresh(float maxAge) => Age <= maxAge;
}

public enum IntelType
{
    None,
    VisualContact,       // Direct line-of-sight to enemy
    GunfireHeard,        // Gunshot sound detected
    FootstepsHeard,      // Footstep sound detected
    ExplosionHeard,      // Explosion sound detected
    AllyKilled,          // Friendly KIA at position
    AllyDamaged,         // Friendly took damage
    DamageTaken,         // Self took damage from direction
    SquadCallout,        // Intel shared by squadmate
    SectorClear,         // Area confirmed empty
    EnemyPredicted       // Estimated position from analysis
}

// ═══════════════════════════════════════════════════════════════════════════
//  BLACKBOARD KEYS
// ═══════════════════════════════════════════════════════════════════════════

public enum PvPBlackboardKey
{
    // === THREAT ===
    CurrentTarget,               // Transform – active engagement target
    LastKnownEnemyPosition,      // Vector3
    PredictedEnemyPosition,      // Vector3 – from ThinkingLayer analysis
    LastContactTime,             // float – Time.time of last confirmed contact
    ThreatDirection,             // Vector3 – direction of most recent threat
    ThreatLevel,                 // PvPThreatLevel enum
    TargetVisible,               // bool

    // === SOUND / INTEL ===
    LastHeardPosition,           // Vector3
    LastSoundType,               // PvPSoundType enum
    LastSoundTime,               // float
    UnprocessedSoundEvent,       // bool – flag for ThinkingLayer

    // === SELF STATE ===
    InCombat,                    // bool
    IsMoving,                    // bool
    IsInCover,                   // bool
    IsSuppressed,                // bool
    IsClearing,                  // bool – currently doing CQB room clear
    IsHoldingPosition,           // bool
    IsStackedOnDoor,             // bool
    JustTookDamage,              // bool
    WeaponRaised,                // bool
    IsReloading,                 // bool
    IsAiming,                    // bool
    IsAlert,                     // bool

    // === HEALTH ===
    HealthPercent,               // float  0–1
    LastDamageTime,              // float
    LastDamageDirection,         // Vector3

    // === WEAPON ===
    LastShotTime,                // float
    AmmoPercent,                 // float  0–1
    CanShoot,                    // bool

    // === COVER ===
    CurrentCoverPosition,        // Vector3
    BestCoverScore,              // float

    // === SQUAD ===
    SquadRole,                   // PvPSquadRole enum
    FormationIndex,              // int  – position within squad formation
    SquadLeaderPosition,         // Vector3
    SquadCalloutPending,         // bool

    // === CQB ===
    ClearingSectorIndex,         // int – current pie-slice index
    RoomEntryPoint,              // Vector3
    CQBState,                    // PvPCQBState enum
    AssignedSector,              // Vector3 – sector center to clear

    // === TIMING ===
    AlertStartTime,              // float
    SuppressionEndTime,          // float
    LastThinkTime,               // float – when ThinkingLayer last ran

    // === NAVIGATION ===
    CurrentWaypoint,             // Vector3
    PatrolRouteIndex,            // int
    AvoidanceTarget              // Transform
}

// ═══════════════════════════════════════════════════════════════════════════
//  SUPPORTING ENUMS
// ═══════════════════════════════════════════════════════════════════════════

public enum PvPThreatLevel
{
    None,
    Suspected,    // Sound or squad callout
    Confirmed,    // Direct visual contact
    Engaged       // Currently in firefight
}

public enum PvPSoundType
{
    None,
    Footsteps,
    Gunshot,
    Explosion,
    VoiceCallout,
    DoorBreach,
    Reload
}

public enum PvPSquadRole
{
    PointMan,     // Leads the formation, first to enter rooms
    Rifleman,     // Standard combat role
    Support,      // Covers teammates, provides suppression
    RearGuard     // Watches behind the squad
}

public enum PvPCQBState
{
    None,
    Approaching,         // Moving toward room / sector
    StackingUp,          // Forming up at entry point
    PieSlicing,          // Gradually clearing corners
    DynamicEntry,        // Fast room entry
    ClearingSector,      // Scanning assigned sector inside room
    SectorClear,         // Current sector confirmed empty
    Holding              // Holding cleared area
}

/// <summary>
/// Tactical actions decided by the Thinking Layer.
/// The Reactive Layer may override these when immediate threats arise.
/// </summary>
public enum PvPTacticalAction
{
    Patrol,              // Route-based sweep of map
    Advance,             // Push toward objective / enemy territory
    HoldPosition,        // Defend current position
    ClearRoom,           // CQB room/sector clearing
    Flank,               // Approach enemy from the side
    FallBack,            // Retreat to safer position
    InvestigateIntel,    // Move to and inspect an intel location
    SetAmbush,           // Set up ambush at chokepoint
    SupportAlly,         // Move to assist a squadmate
    Regroup              // Return to squad formation
}
