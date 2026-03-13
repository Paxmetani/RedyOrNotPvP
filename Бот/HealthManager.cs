using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// Универсальный HealthManager с отладкой
/// </summary>
public class HealthManager : MonoBehaviour, IDamageable
{
    [Header("Health")]
    [SerializeField] public float maxHealth = 100f;
    [SerializeField] public float currentHealth;
    [SerializeField] public bool canTakeDamage = true;

    [Header("Damage Multipliers")]
    [SerializeField] public float headshotMultiplier = 2f;
    [SerializeField] public float bodyMultiplier = 1f;
    [SerializeField] public float limbMultiplier = 0.7f;

    [Header("Team")]
    public string teamTag = "Enemy"; // Сделал public для видимости

    [Header("Debug")]
    [SerializeField] public bool showDebugLogs = true;
    [HideInInspector] public Vector3 lastHitPoint;
    [HideInInspector] public Vector3 hitDirections;

    [Header("Events")]
    public UnityEvent OnDeath;
    public UnityEvent OnDamage;
    public UnityEvent OnHealth;

    public bool IsDead { get; private set; }
    public float HealthPercentage => maxHealth > 0 ? currentHealth / maxHealth : 0f;
    public string TeamTag => teamTag;

    private void Awake()
    {
        currentHealth = maxHealth;
        IsDead = false;
        canTakeDamage = true;
        if (showDebugLogs)
        {
            Debug.Log($"[HealthManager] {gameObject.name} initialized.  HP: {currentHealth}/{maxHealth}, Team: {teamTag}");
        }
    }

    public void TakeDamage(float damage, Vector3 hitPoint, Vector3 hitDirection)
    {
        if(!canTakeDamage)
        {
            return;
        }

        if (showDebugLogs)
        {
            Debug.Log($"[HealthManager] {gameObject.name} TakeDamage called. Damage: {damage}, IsDead: {IsDead}");
        }

        if (IsDead)
        {
            if (showDebugLogs)
                Debug.Log($"[HealthManager] {gameObject.name} is already dead, ignoring damage");
            return;
        }

        // Calculate damage based on hit location
        float finalDamage = damage * GetDamageMultiplier(hitPoint);

        if (showDebugLogs)
        {
            Debug.Log($"[HealthManager] {gameObject.name} taking {finalDamage} damage (before:  {currentHealth})");
        }

        currentHealth -= finalDamage;
        currentHealth = Mathf.Clamp(currentHealth, 0f, maxHealth);
        hitDirections = hitDirection;
        lastHitPoint = hitPoint;

        if (showDebugLogs)
        {
            Debug.Log($"[HealthManager] {gameObject.name} HP after damage: {currentHealth}/{maxHealth} ({HealthPercentage * 100}%)");
        }

        OnDamage?.Invoke();

        if (currentHealth <= 0f)
        {
            Die();
        }
    }

    private float GetDamageMultiplier(Vector3 hitPoint)
    {
        // Простой расчёт на основе высоты
        float heightPercent = (hitPoint.y - transform.position.y) / 2f;

        if (heightPercent > 0.8f)
        {
            if (showDebugLogs) Debug.Log($"[HealthManager] Headshot!  Multiplier: {headshotMultiplier}");
            return headshotMultiplier;
        }
        if (heightPercent > 0.3f)
        {
            if (showDebugLogs) Debug.Log($"[HealthManager] Body shot! Multiplier: {bodyMultiplier}");
            return bodyMultiplier;
        }

        if (showDebugLogs) Debug.Log($"[HealthManager] Limb shot! Multiplier: {limbMultiplier}");
        return limbMultiplier;
    }

    private void Die()
    {
        if (IsDead) return;

        if (showDebugLogs)
        {
            Debug.Log($"[HealthManager] {gameObject.name} DIED!");
        }



       IsDead = true;
        OnDeath?.Invoke();
    }

    public void Heal(float amount)
    {
        if (IsDead) return;

        currentHealth = Mathf.Clamp(currentHealth + amount, 0f, maxHealth);
        OnHealth?.Invoke();

        if (showDebugLogs)
        {
            Debug.Log($"[HealthManager] {gameObject.name} healed {amount}.  HP: {currentHealth}/{maxHealth}");
        }
    }

    /// <summary>
    /// Воскресить мёртвого игрока (рекламный ревайв).
    /// В отличие от Heal(), сбрасывает IsDead и устанавливает HP напрямую.
    /// </summary>
    public void Revive(float healthAmount)
    {
        IsDead = false;
        currentHealth = Mathf.Clamp(healthAmount, 1f, maxHealth);
        OnHealth?.Invoke();

        if (showDebugLogs)
            Debug.Log($"[HealthManager] {gameObject.name} REVIVED. HP: {currentHealth}/{maxHealth}");
    }

    public bool IsOnTeam(string team)
    {
        return teamTag == team;
    }
}