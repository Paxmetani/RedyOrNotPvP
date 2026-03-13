using UnityEngine;

/// <summary>
/// Consumable door-locking key gadget for the player.
///
/// The player equips this item and activates it while standing near a closed door
/// to lock it (spending 1 charge), or near a locked door to unlock it (free).
///
/// Designed to work with the existing DoorSystem.Lock() / Unlock() API.
///
/// Usage:
///   Attach to the player's gadget slot GameObject (same pattern as Grenade.cs).
///   Call UseLock() from your player input handler (e.g. Gadget key).
///   Wire up the OnChargesChanged event to your HUD icon if desired.
/// </summary>
public class DoorLockGadget : MonoBehaviour
{
    // ─── Settings ─────────────────────────────────────────────────────────

    [Header("Gadget Settings")]
    [SerializeField] private int   startingCharges    = 3;
    [SerializeField] private float interactionRange   = 2.5f;

    [Header("Debug")]
    [SerializeField] private bool showDebugLogs = true;

    // ─── State ────────────────────────────────────────────────────────────

    private int currentCharges;

    public int Charges => currentCharges;

    // ─── Events ───────────────────────────────────────────────────────────

    [Header("Events")]
    public UnityEngine.Events.UnityEvent<int> OnChargesChanged; // fires after each use

    // ─────────────────────────────────────────────────────────────────────

    private void Start()
    {
        currentCharges = startingCharges;
        OnChargesChanged?.Invoke(currentCharges);
    }

    // ─── Public API ───────────────────────────────────────────────────────

    /// <summary>
    /// Attempt to lock or unlock the nearest eligible door.
    /// Locking costs 1 charge; unlocking is free.
    /// Returns true if an interaction occurred.
    /// </summary>
    public bool UseLock(Transform userTransform)
    {
        DoorSystem door = FindNearestDoor(userTransform);

        if (door == null)
        {
            if (showDebugLogs)
                Debug.Log("[DoorLockGadget] No door in range.");
            return false;
        }

        DoorSystem.DoorState state = door.GetCurrentState();

        if (state == DoorSystem.DoorState.Locked)
        {
            // Unlock — free action
            door.Unlock();
            if (showDebugLogs)
                Debug.Log("[DoorLockGadget] Door unlocked.");
            OnChargesChanged?.Invoke(currentCharges);
            return true;
        }

        if (state == DoorSystem.DoorState.Closed)
        {
            if (currentCharges <= 0)
            {
                if (showDebugLogs)
                    Debug.Log("[DoorLockGadget] No charges remaining — cannot lock.");
                return false;
            }

            door.Lock();
            currentCharges--;
            if (showDebugLogs)
                Debug.Log($"[DoorLockGadget] Door locked. Charges remaining: {currentCharges}");
            OnChargesChanged?.Invoke(currentCharges);
            return true;
        }

        // Door is open or cracked — must close it first
        if (showDebugLogs)
            Debug.Log("[DoorLockGadget] Door must be fully closed before locking.");
        return false;
    }

    // ─── Helpers ──────────────────────────────────────────────────────────

    private DoorSystem FindNearestDoor(Transform userTransform)
    {
        DoorSystem[] allDoors = FindObjectsOfType<DoorSystem>();
        DoorSystem   nearest  = null;
        float        nearestDist = interactionRange;

        foreach (var door in allDoors)
        {
            if (door == null) continue;

            float dist = Vector3.Distance(userTransform.position, door.transform.position);
            if (dist < nearestDist)
            {
                nearestDist = dist;
                nearest     = door;
            }
        }

        return nearest;
    }

#if UNITY_EDITOR
    private void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.cyan;
        Gizmos.DrawWireSphere(transform.position, interactionRange);
    }
#endif
}
