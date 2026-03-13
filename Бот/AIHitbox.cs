using UnityEngine;

/// <summary>
/// Система хитбоксов для AI
/// Устанавливается на кости (Head, Chest, Legs и т.д.)
/// </summary>
public class AIHitbox : MonoBehaviour
{
    public enum HitboxType
    {
        Head,      // x3 урона
        Chest,     // x1 урона
        Stomach,   // x1 урона
        LeftArm,   // x0. 5 урона
        RightArm,  // x0.5 урона
        LeftLeg,   // x0.7 урона
        RightLeg   // x0.7 урона
    }

    [Header("Hitbox Settings")]
    [SerializeField] private HitboxType hitboxType = HitboxType.Chest;
    [SerializeField] private SmartEnemyAI ownerAI;

    [Header("Damage Multipliers")]
    [SerializeField] private float damageMultiplier = 1f;

    [Header("Debug")]
    [SerializeField] private bool showDebugLogs = false;

    private void Awake()
    {
        // Auto-find owner AI
        if (ownerAI == null)
        {
            ownerAI = GetComponentInParent<SmartEnemyAI>();
        }

        // Setup collider
        SetupCollider();

        // Set damage multiplier based on type
        SetDamageMultiplier();
    }

    private void SetupCollider()
    {
        // Ensure we have a collider
        Collider col = GetComponent<Collider>();

        if (col == null)
        {
            // Add appropriate collider based on bone
            col = gameObject.AddComponent<CapsuleCollider>();
        }

        // CRITICAL: Set to trigger so it doesn't interfere with NavMesh
        col.isTrigger = false; // НЕ trigger, но на отдельном слое

        // Set layer to "Hitbox" (create this layer!)
        gameObject.layer = LayerMask.NameToLayer("Enemy");
    }

    private void SetDamageMultiplier()
    {
        switch (hitboxType)
        {
            case HitboxType.Head:
                damageMultiplier = 3f;
                break;
            case HitboxType.Chest:
            case HitboxType.Stomach:
                damageMultiplier = 1f;
                break;
            case HitboxType.LeftArm:
            case HitboxType.RightArm:
                damageMultiplier = 0.5f;
                break;
            case HitboxType.LeftLeg:
            case HitboxType.RightLeg:
                damageMultiplier = 0.7f;
                break;
        }
    }

    /// <summary>
    /// Called when hit by raycast
    /// </summary>
    public void TakeDamage(float baseDamage, Vector3 hitPoint, Vector3 direction)
    {
        if (ownerAI == null || ownerAI.IsDead()) return;

        // Calculate final damage
        float finalDamage = baseDamage * damageMultiplier;

        // Apply to owner AI
        if (ownerAI.Health != null)
        {
            ownerAI.Health.TakeDamage(finalDamage, hitPoint, direction);
        }

        if (showDebugLogs)
        {
            Debug.Log($"[Hitbox] {ownerAI.name} - {hitboxType} hit for {finalDamage: F1} damage (x{damageMultiplier})");
        }

        // Visual feedback
        ShowHitEffect(hitPoint);
    }

    private void ShowHitEffect(Vector3 position)
    {
        // TODO: Spawn blood particle, impact effect, etc.
    }

    private void OnDrawGizmos()
    {
        // Show hitbox in editor
        Gizmos.color = GetGizmoColor();

        Collider col = GetComponent<Collider>();
        if (col != null)
        {
            Gizmos.matrix = transform.localToWorldMatrix;

            if (col is BoxCollider box)
            {
                Gizmos.DrawWireCube(box.center, box.size);
            }
            else if (col is CapsuleCollider capsule)
            {
                // Draw capsule approximation
                Gizmos.DrawWireSphere(Vector3.up * capsule.height * 0.5f, capsule.radius);
                Gizmos.DrawWireSphere(Vector3.down * capsule.height * 0.5f, capsule.radius);
            }
            else if (col is SphereCollider sphere)
            {
                Gizmos.DrawWireSphere(sphere.center, sphere.radius);
            }
        }
    }

    private Color GetGizmoColor()
    {
        switch (hitboxType)
        {
            case HitboxType.Head: return Color.red;
            case HitboxType.Chest: return Color.yellow;
            case HitboxType.Stomach: return Color.green;
            case HitboxType.LeftArm:
            case HitboxType.RightArm: return Color.cyan;
            case HitboxType.LeftLeg:
            case HitboxType.RightLeg: return Color.blue;
            default: return Color.white;
        }
    }
}