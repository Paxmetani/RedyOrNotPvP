using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Типизированный Blackboard
/// </summary>
public class AIBlackboard
{
    private Dictionary<BlackboardKey, object> data = new Dictionary<BlackboardKey, object>();

    public void Set(BlackboardKey key, object value) => data[key] = value;

    public T Get<T>(BlackboardKey key, T defaultValue = default)
    {
        if (data.TryGetValue(key, out object value) && value is T typedValue)
            return typedValue;
        return defaultValue;
    }

    public bool Has(BlackboardKey key) => data.ContainsKey(key);

    public void Remove(BlackboardKey key) => data.Remove(key);

    public void Clear() => data.Clear();

    // Специализированные методы
    public void SetBool(BlackboardKey key, bool value) => Set(key, value);
    public bool GetBool(BlackboardKey key, bool defaultValue = false) => Get(key, defaultValue);

    public void SetFloat(BlackboardKey key, float value) => Set(key, value);
    public float GetFloat(BlackboardKey key, float defaultValue = 0f) => Get(key, defaultValue);

    public void SetVector3(BlackboardKey key, Vector3 value) => Set(key, value);
    public Vector3 GetVector3(BlackboardKey key) => Get(key, Vector3.zero);

    public void SetTransform(BlackboardKey key, Transform value) => Set(key, value);
    public Transform GetTransform(BlackboardKey key) => Get<Transform>(key, null);
}

/// <summary>
/// ВСЕ ключи Blackboard
/// </summary>
public enum BlackboardKey
{
    // === THREAT ===
    CurrentThreat,
    LastKnownThreatPosition,
    PredictedThreatPosition,
    LastSeenThreatTime,
    LastSuspectedThreatTime,
    ThreatLevel,
    ThreatVisible,
    ThreatOrigin,
    AlertSoundType,
    AlertStartTime,

    // === SOUND ===
    LastHeardSoundPosition,
    LastSoundType,

    // === STATE ===
    IsDeciding,
    HasSurrendered,
    IsStunned,
    IsSuppressed,
    IsInCover,
    IsBeingArrested,
    IsArrested,
    InCombat,
    IsAiming,
    IsAlert,
    IsAmbushing,

    // === WEAPON ===
    WeaponRaised,
    HasWeapon,
    LastShotTime,
    CanShoot,

    // === DECISION ===
    DecidedToFight,

    // === PROGRESS ===
    ArrestProgress,

    // === TIMERS ===
    SuppressionEndTime,
    StunnedUntil,

    // === COVER ===
    CurrentCoverPosition,

    // === MISC ===
    Alertness,
    JustTookDamage,
    LastDamageTime,
    CalledForBackup,
    BackupRequestPosition,
    
    // === SQUAD & ROLES ===
    SquadRole,
    AvoidanceTarget
}