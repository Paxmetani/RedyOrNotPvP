using System.Collections;
using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// Bomb defuse/defend objective (Ready Or Not / CS-style).
///
/// The bomb is already placed at match start.
/// - Attackers (defusing team): hold interact near the bomb to defuse it.
/// - Defenders: prevent defusal before <bombTimer> seconds elapse.
///
/// Win conditions:
///   Bomb defused   → attacker team wins.
///   Bomb explodes  → defender team wins.
///
/// Hookup: place a Collider (Is Trigger) on this GameObject as the defuse zone.
/// The player enters the trigger and the defuse bar advances automatically.
/// A <DoorInteractionPoint>-style UI can call StartDefusing / StopDefusing manually too.
/// </summary>
[RequireComponent(typeof(Collider))]
public class BombObjective : MonoBehaviour
{
    // ─── Settings ─────────────────────────────────────────────────────────

    [Header("Timers")]
    [SerializeField] private float bombTimer  = 45f;   // seconds until detonation
    [SerializeField] private float defuseTime =  8f;   // seconds to hold for full defuse

    [Header("State (read-only in inspector)")]
    [SerializeField] private bool  isBombActive    = true;
    [SerializeField] private bool  isBeingDefused  = false;
    [SerializeField] private float defuseProgress  = 0f;
    [SerializeField] private float bombTimeRemaining;

    private string attackerTeamTag;
    private bool   isDefused   = false;
    private bool   hasExploded = false;

    // ─── Public Accessors ─────────────────────────────────────────────────

    public bool  IsDefused         => isDefused;
    public bool  HasExploded       => hasExploded;
    public float BombTimeRemaining => bombTimeRemaining;
    /// <summary>Defuse progress [0..1].</summary>
    public float DefuseProgress    => defuseTime > 0f ? defuseProgress / defuseTime : 0f;

    // ─── Events ───────────────────────────────────────────────────────────

    [Header("Events")]
    public UnityEvent        OnBombDefused;
    public UnityEvent        OnBombExploded;
    public UnityEvent<float> OnBombTimerUpdated;     // remaining seconds
    public UnityEvent<float> OnDefuseProgressUpdated; // [0..1]

    // ─────────────────────────────────────────────────────────────────────

    private void Awake()
    {
        // Make sure the collider on this object is a trigger zone
        var col = GetComponent<Collider>();
        col.isTrigger = true;

        bombTimeRemaining = bombTimer;
    }

    /// <summary>
    /// Call this from PvPMatchManager when the match starts.
    /// </summary>
    /// <param name="attackerTag">TeamTag of the team that must defuse the bomb.</param>
    public void Initialize(string attackerTag)
    {
        attackerTeamTag  = attackerTag;
        bombTimeRemaining = bombTimer;
        isDefused        = false;
        hasExploded      = false;
        isBeingDefused   = false;
        defuseProgress   = 0f;
        isBombActive     = true;
    }

    private void Update()
    {
        if (!isBombActive || isDefused || hasExploded) return;

        // Tick the bomb countdown
        bombTimeRemaining -= Time.deltaTime;
        OnBombTimerUpdated?.Invoke(bombTimeRemaining);

        // Advance / decay defuse bar
        if (isBeingDefused)
        {
            defuseProgress += Time.deltaTime;
            OnDefuseProgressUpdated?.Invoke(DefuseProgress);

            if (defuseProgress >= defuseTime)
                Defuse();
        }
        else
        {
            // Slowly decay progress if the player stepped away
            defuseProgress = Mathf.Max(0f, defuseProgress - Time.deltaTime * 0.5f);
        }

        if (bombTimeRemaining <= 0f)
            Explode();
    }

    // ─── Public Defuse API ────────────────────────────────────────────────

    /// <summary>
    /// Begin defusing. Only accepted from members of the attacker team.
    /// </summary>
    public void StartDefusing(string callerTeamTag)
    {
        if (isDefused || hasExploded || !isBombActive) return;
        if (!string.IsNullOrEmpty(attackerTeamTag) && callerTeamTag != attackerTeamTag) return;

        isBeingDefused = true;
    }

    /// <summary>
    /// Stop defusing (player moved away / interrupted).
    /// </summary>
    public void StopDefusing()
    {
        isBeingDefused = false;
    }

    // ─── Trigger Zone (auto-defuse for player) ────────────────────────────

    private void OnTriggerStay(Collider other)
    {
        if (isDefused || hasExploded || !isBombActive) return;

        var playerController = other.GetComponentInParent<PlayerController>();
        if (playerController != null)
        {
            var hm = playerController.GetComponent<HealthManager>();
            if (hm != null && !hm.IsDead)
                StartDefusing(hm.TeamTag);
        }
    }

    private void OnTriggerExit(Collider other)
    {
        if (other.GetComponentInParent<PlayerController>() != null)
            StopDefusing();
    }

    // ─── Internal Resolution ─────────────────────────────────────────────

    private void Defuse()
    {
        isDefused      = true;
        isBombActive   = false;
        isBeingDefused = false;
        defuseProgress = defuseTime;
        Debug.Log("[BombObjective] BOMB DEFUSED!");
        OnBombDefused?.Invoke();
    }

    private void Explode()
    {
        hasExploded  = true;
        isBombActive = false;
        Debug.Log("[BombObjective] BOMB EXPLODED!");
        OnBombExploded?.Invoke();
    }

    // ─── Gizmo ───────────────────────────────────────────────────────────

#if UNITY_EDITOR
    private void OnDrawGizmosSelected()
    {
        Gizmos.color = isDefused ? Color.green : (hasExploded ? Color.red : Color.yellow);
        var col = GetComponent<Collider>();
        if (col != null)
            Gizmos.DrawWireCube(transform.position, col.bounds.size);
    }
#endif
}
