using System.Collections;
using UnityEngine;

/// <summary>
/// Optional AI tactical door-usage module.
///
/// Attach alongside the other AI modules on a bot GameObject.
/// Bots with this module will:
///   • Open closed/cracked doors that block their path when advancing.
///   • Close open doors behind them when taking cover.
///   • Occasionally lock a door for defensive tactical advantage
///     (uses a limited lock charge, costs nothing to attach).
///
/// Works via the existing DoorSystem.OnAIInteract() / OnAIClose() / Lock() API.
/// No changes to SmartEnemyAI / AIController are required.
/// </summary>
public class AITacticalDoorModule : MonoBehaviour
{
    // ─── Settings ─────────────────────────────────────────────────────────

    [Header("Door Interaction")]
    [SerializeField] private float doorCheckInterval  = 0.5f;
    [SerializeField] private float doorSearchRadius   = 2.5f;
    [SerializeField, Range(0f, 1f)] private float doorCloseProbability = 0.60f;

    [Header("Door Locking (defensive)")]
    [SerializeField] private bool  canLockDoors       = true;
    [SerializeField] private int   lockCharges        = 1;
    [SerializeField, Range(0f, 1f)] private float lockProbability     = 0.30f;

    // ─── State ────────────────────────────────────────────────────────────

    private SmartEnemyAI core;
    private float        nextCheckTime;
    private int          lockChargesRemaining;

    // ─────────────────────────────────────────────────────────────────────

    private void Awake()
    {
        core                 = GetComponent<SmartEnemyAI>();
        lockChargesRemaining = lockCharges;
    }

    private void Update()
    {
        if (core == null || core.IsDead())  return;
        if (Time.time < nextCheckTime)      return;

        nextCheckTime = Time.time + doorCheckInterval;
        CheckNearbyDoors();
    }

    // ─── Door Discovery ───────────────────────────────────────────────────

    private void CheckNearbyDoors()
    {
        Collider[] cols = Physics.OverlapSphere(core.Transform.position, doorSearchRadius);

        foreach (var col in cols)
        {
            DoorSystem door = col.GetComponent<DoorSystem>()
                           ?? col.GetComponentInParent<DoorSystem>();

            if (door == null) continue;

            HandleDoor(door);
            break; // one door per tick
        }
    }

    private void HandleDoor(DoorSystem door)
    {
        DoorSystem.DoorState state = door.GetCurrentState();

        bool inCover = core.Blackboard.GetBool(BlackboardKey.IsInCover, false);

        if (state == DoorSystem.DoorState.Open && inCover)
        {
            // Tactically close door for cover
            if (Random.value < doorCloseProbability)
            {
                door.OnAIClose(gameObject);

                // Possibly lock it to delay enemy entry
                if (canLockDoors && lockChargesRemaining > 0 &&
                    Random.value < lockProbability)
                {
                    StartCoroutine(LockAfterClose(door));
                }
            }
        }
        else if ((state == DoorSystem.DoorState.Closed ||
                  state == DoorSystem.DoorState.Cracked) && !inCover)
        {
            // Open door to advance
            door.OnAIInteract(gameObject);
        }
    }

    // ─── Lock Coroutine ───────────────────────────────────────────────────

    private IEnumerator LockAfterClose(DoorSystem door)
    {
        // Wait until the close animation likely finishes
        yield return new WaitForSeconds(0.6f);

        if (door != null &&
            door.GetCurrentState() == DoorSystem.DoorState.Closed &&
            lockChargesRemaining > 0)
        {
            door.Lock();
            lockChargesRemaining--;
            Debug.Log($"[AITacticalDoor] {core.name} locked a door (charges left: {lockChargesRemaining})");
        }
    }

    // ─── Helpers ──────────────────────────────────────────────────────────

    /// <summary>Force-open the nearest door in range (for navigation blockers).</summary>
    public void OpenNearestDoor()
    {
        Collider[] cols = Physics.OverlapSphere(core.Transform.position, doorSearchRadius);

        foreach (var col in cols)
        {
            DoorSystem door = col.GetComponent<DoorSystem>()
                           ?? col.GetComponentInParent<DoorSystem>();

            if (door == null || door.IsOpen) continue;

            door.OnAIInteract(gameObject);
            return;
        }
    }

#if UNITY_EDITOR
    private void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(0f, 1f, 1f, 0.3f);
        Gizmos.DrawWireSphere(transform.position, doorSearchRadius);
    }
#endif
}
