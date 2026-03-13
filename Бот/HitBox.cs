using UnityEngine;

/// <summary>
/// Компонент для зон попадания (голова, тело, конечности)
/// </summary>
public class HitBox : MonoBehaviour
{
    [SerializeField] private HealthManager healthComponent;
    [SerializeField] private float damageMultiplier = 1f;
    [SerializeField] private HitBoxType hitBoxType;

    private void Awake()
    {
        if (healthComponent == null)
        {
            healthComponent = GetComponentInParent<HealthManager>();
        }
    }

    public void TakeDamage(float damage, Vector3 hitPoint, Vector3 hitDirection)
    {
        if (healthComponent != null)
        {
            healthComponent.TakeDamage(damage * damageMultiplier, hitPoint, hitDirection);
        }
    }

    public enum HitBoxType
    {
        Head,
        Body,
        Limb
    }
}