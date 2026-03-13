using UnityEngine;

// ═══════════════════════════════════════════════════════════════════════════
//  PvP COMBAT MODULE
//
//  Handles weapon firing, target engagement, and suppression for PvP bots.
//  Re-uses AIWeaponController for low-level weapon state (raise/lower/fire)
//  and adds PvP-specific logic:
//    • Controlled pairs  (double-tap, industry standard).
//    • Suppressive fire  (area denial with increased spread).
//    • Fire discipline   (team-tag checking, no friendly fire).
//    • Accuracy modifiers (HP, movement, cover, suppression).
// ═══════════════════════════════════════════════════════════════════════════

public class PvPCombatModule : MonoBehaviour
{
    // ─── Fire Rate ───────────────────────────────────────────────────────

    [Header("Fire Rate")]
    [SerializeField] private float fireInterval        = 0.20f;   // seconds between shots
    [SerializeField] private float suppressionInterval = 0.35f;   // slower for suppression
    [SerializeField] private float controlledPairDelay = 0.08f;   // delay between pair shots

    // ─── Accuracy ────────────────────────────────────────────────────────

    [Header("Accuracy")]
    [SerializeField, Range(0f, 1f)] private float baseAccuracy      = 0.80f;
    [SerializeField]                private float aimSpreadDeg       = 2f;
    [SerializeField]                private float suppressionSpread  = 8f;

    // ─── Damage ──────────────────────────────────────────────────────────

    [Header("Damage")]
    [SerializeField] private float weaponDamage = 22f;
    [SerializeField] private float weaponRange  = 60f;

    // ─── Engagement ──────────────────────────────────────────────────────

    [Header("Engagement")]
    [SerializeField] private float maxEngageAngle = 30f; // must be facing within this angle
    [SerializeField] private LayerMask shootMask  = ~0;

    // ─── Debug ───────────────────────────────────────────────────────────

    [Header("Debug")]
    [SerializeField] private bool showDebugLogs = false;

    // ─── Internal ────────────────────────────────────────────────────────

    private PvPBotController core;
    private float lastFireTime        = -999f;
    private int   controlledPairCount = 0;

    private Transform     engageTarget     = null;
    private Vector3       suppressTarget   = Vector3.zero;
    private bool          isSuppressing    = false;

    // ═════════════════════════════════════════════════════════════════════

    public void Initialize(PvPBotController controller)
    {
        core = controller;
    }

    // ═════════════════════════════════════════════════════════════════════
    //  UPDATE (called per-frame by PvPBotController)
    // ═════════════════════════════════════════════════════════════════════

    public void UpdateModule()
    {
        if (core.IsDead()) return;

        if (engageTarget != null)
        {
            RotateToTarget(engageTarget.position);
            TryFire(engageTarget.position, false);
        }
        else if (isSuppressing && suppressTarget != Vector3.zero)
        {
            RotateToTarget(suppressTarget);
            TryFire(suppressTarget, true);
        }
    }

    // ═════════════════════════════════════════════════════════════════════
    //  PUBLIC API
    // ═════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Engage a visible target with aimed fire (controlled pairs).
    /// </summary>
    public void EngageTarget(Transform target)
    {
        engageTarget   = target;
        isSuppressing  = false;
        suppressTarget = Vector3.zero;
    }

    /// <summary>
    /// Fire suppressive rounds at a position (no confirmed target).
    /// </summary>
    public void SuppressPosition(Vector3 position)
    {
        suppressTarget = position;
        isSuppressing  = true;
        engageTarget   = null;
    }

    /// <summary>
    /// Stop all firing.
    /// </summary>
    public void CeaseFire()
    {
        engageTarget   = null;
        isSuppressing  = false;
        suppressTarget = Vector3.zero;
    }

    // ═════════════════════════════════════════════════════════════════════
    //  FIRING LOGIC
    // ═════════════════════════════════════════════════════════════════════

    private void TryFire(Vector3 targetPos, bool suppression)
    {
        float interval = suppression ? suppressionInterval : fireInterval;

        // Controlled pair: two quick shots then normal interval
        if (!suppression && controlledPairCount == 1)
        {
            interval = controlledPairDelay;
        }

        if (Time.time - lastFireTime < interval) return;

        // Check angle to target
        Vector3 dir = (targetPos - core.Transform.position).normalized;
        float angle = Vector3.Angle(core.Transform.forward, dir);
        if (angle > maxEngageAngle) return;

        // Fire
        Vector3 origin = core.Transform.position + Vector3.up * 1.5f;
        Vector3 aimDir = CalculateAimDirection(origin, targetPos, suppression);

        if (Physics.Raycast(origin, aimDir, out RaycastHit hit, weaponRange, shootMask))
        {
            // Check team tag — fire discipline
            var hm = hit.collider.GetComponentInParent<HealthManager>();
            if (hm != null)
            {
                if (hm.TeamTag == core.MyTeamTag)
                {
                    // Friendly — do NOT fire
                    return;
                }

                hm.TakeDamage(weaponDamage, hit.point, aimDir);
            }
        }

        lastFireTime = Time.time;
        core.Blackboard.SetFloat(PvPBlackboardKey.LastShotTime, Time.time);

        // Controlled pair tracking
        if (!suppression)
        {
            controlledPairCount++;
            if (controlledPairCount >= 2) controlledPairCount = 0;
        }
        else
        {
            controlledPairCount = 0;
        }

        // Notify sound system
        core.Squad?.BroadcastFiring(core.Transform.position);

        if (showDebugLogs && Time.frameCount % 30 == 0)
            Debug.Log($"[Combat] {core.name} — {(suppression ? "SUPPRESS" : "ENGAGE")}");
    }

    // ═════════════════════════════════════════════════════════════════════
    //  AIM CALCULATION
    // ═════════════════════════════════════════════════════════════════════

    private Vector3 CalculateAimDirection(Vector3 origin, Vector3 targetPos, bool suppression)
    {
        Vector3 idealDir = (targetPos + Vector3.up * 1.2f - origin).normalized;

        float accuracy = CalculateAccuracy();
        float spread = suppression ? suppressionSpread : aimSpreadDeg * (1f - accuracy);

        if (spread > 0.01f)
        {
            float offX = Random.Range(-spread, spread);
            float offY = Random.Range(-spread, spread);
            idealDir = Quaternion.Euler(offY, offX, 0f) * idealDir;
        }

        return idealDir;
    }

    private float CalculateAccuracy()
    {
        float acc = baseAccuracy;

        // HP penalty
        float hp = core.Blackboard.GetFloat(PvPBlackboardKey.HealthPercent, 1f);
        if (hp < 0.5f) acc *= 0.85f;

        // Movement penalty
        if (core.Agent != null && core.Agent.velocity.sqrMagnitude > 1f)
            acc *= 0.80f;

        // Cover bonus
        if (core.Blackboard.GetBool(PvPBlackboardKey.IsInCover))
            acc *= 1.10f;

        // Suppression penalty
        if (core.Blackboard.GetBool(PvPBlackboardKey.IsSuppressed))
            acc *= 0.60f;

        return Mathf.Clamp01(acc);
    }

    // ═════════════════════════════════════════════════════════════════════
    //  ROTATION TOWARD TARGET
    // ═════════════════════════════════════════════════════════════════════

    private void RotateToTarget(Vector3 targetPos)
    {
        Vector3 dir = (targetPos - core.Transform.position).normalized;
        dir.y = 0f;
        if (dir == Vector3.zero) return;

        Quaternion desired = Quaternion.LookRotation(dir);
        core.Transform.rotation = Quaternion.RotateTowards(
            core.Transform.rotation, desired, 720f * Time.deltaTime);
    }
}
