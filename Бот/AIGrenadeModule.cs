using UnityEngine;

/// <summary>
/// Optional AI grenade-throwing module.
///
/// Attach this alongside the other AI modules on a bot GameObject.
/// The module decides autonomously when to throw a grenade based on:
///   - Whether a confirmed or last-known threat position is available.
///   - Distance to the threat (min/max range guard).
///   - A random probability roll (with configurable chance).
///   - A per-throw cooldown to prevent spam.
///
/// Uses GrenadeCase directly (the physics grenade projectile) — the same
/// prefab referenced by the existing Grenade.cs player gadget.
/// No changes to SmartEnemyAI / AIController are required; this module
/// auto-initialises from Awake().
/// </summary>
public class AIGrenadeModule : MonoBehaviour
{
    // ─── Settings ─────────────────────────────────────────────────────────

    [Header("Grenade Settings")]
    [SerializeField] private int       grenadeCharges      = 2;
    [SerializeField] private float     throwForce          = 12f;
    [SerializeField] private float     upwardForce         = 4f;
    [SerializeField] private float     throwCooldown       = 15f;
    [SerializeField] private float     maxThrowRange       = 18f;
    [SerializeField] private float     minThrowRange       =  4f;  // don't throw too close
    [SerializeField] private GameObject grenadePrefab;             // GrenadeCase prefab
    [SerializeField] private Transform  throwPoint;                // optional spawn override

    [Header("Decision")]
    [SerializeField, Range(0f, 1f)] private float throwProbability        = 0.35f;
    [SerializeField, Range(0f, 1f)] private float lastKnownThrowProbability = 0.15f;

    // ─── State ────────────────────────────────────────────────────────────

    private SmartEnemyAI core;
    private int   currentCharges;
    private float nextThrowTime;

    public int RemainingCharges => currentCharges;

    // ─────────────────────────────────────────────────────────────────────

    private void Awake()
    {
        core           = GetComponent<SmartEnemyAI>();
        currentCharges = grenadeCharges;
    }

    private void Update()
    {
        if (core == null || core.IsDead()) return;
        if (currentCharges <= 0)          return;
        if (Time.time < nextThrowTime)    return;
        if (grenadePrefab == null)        return;

        // Priority 1: confirmed visible threat
        Transform threat = core.Blackboard.GetTransform(BlackboardKey.CurrentThreat);
        if (threat != null)
        {
            float dist = Vector3.Distance(core.Transform.position, threat.position);
            if (dist >= minThrowRange && dist <= maxThrowRange && Random.value < throwProbability)
                ThrowAt(threat.position);

            return;
        }

        // Priority 2: last-known position
        Vector3 lastKnown = core.Blackboard.GetVector3(BlackboardKey.LastKnownThreatPosition);
        if (lastKnown != Vector3.zero)
        {
            float dist = Vector3.Distance(core.Transform.position, lastKnown);
            if (dist >= minThrowRange && dist <= maxThrowRange &&
                Random.value < lastKnownThrowProbability)
            {
                ThrowAt(lastKnown);
            }
        }
    }

    // ─── Public API ───────────────────────────────────────────────────────

    /// <summary>
    /// Throw a grenade toward <paramref name="targetPosition"/>.
    /// Returns false if no charges remain or prefab is missing.
    /// </summary>
    public bool ThrowAt(Vector3 targetPosition)
    {
        if (currentCharges <= 0 || grenadePrefab == null) return false;

        Transform spawnTransform = throwPoint != null ? throwPoint : core.Transform;
        Vector3   spawnPos       = spawnTransform.position + Vector3.up * 0.5f;
        Vector3   throwDir       = (targetPosition - spawnPos).normalized;

        GameObject grenadeGO = Instantiate(grenadePrefab, spawnPos, Quaternion.identity);

        var grenadeCase = grenadeGO.GetComponent<GrenadeCase>();
        if (grenadeCase != null)
        {
            grenadeCase.Throw(spawnPos, throwDir * throwForce + Vector3.up * upwardForce);
        }
        else
        {
            // If the prefab is a different grenade type, destroy and bail
            Destroy(grenadeGO);
            return false;
        }

        currentCharges--;
        nextThrowTime = Time.time + throwCooldown;

        Debug.Log($"[AIGrenade] {core.name} threw grenade → {targetPosition}. " +
                  $"Charges left: {currentCharges}");
        return true;
    }
}
