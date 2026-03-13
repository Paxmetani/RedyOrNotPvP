using UnityEngine;

/// <summary>
/// ���������� ������� AI - ��������, ���������, ��������
/// </summary>
public class AIWeaponController : MonoBehaviour
{
    [Header("Weapon Model")]
    [SerializeField] private GameObject weaponModel;
    [SerializeField] private Transform weaponHoldPoint;

    [Header("Drop Settings")]
    [SerializeField] private GameObject droppedWeaponPrefab; // ����� � collider
    [SerializeField] private float dropForce = 3f;

    private SmartEnemyAI core;
    private bool hasWeapon = true;
    private bool isRaised = false;
    private bool lastRaisedState = false;  // НОВОЕ: Кеш последнего состояния

    public void Initialize(SmartEnemyAI coreAI)
    {
        core = coreAI;

        if (weaponModel != null)
        {
            weaponModel.SetActive(hasWeapon);
        }
    }

    public void UpdateModule()
    {
        // Sync weapon state with blackboard
        // ОПТИМИЗАЦИЯ: Проверяем только если есть изменение
        bool shouldBeRaised = core.Blackboard.GetBool(BlackboardKey.WeaponRaised, false);

        // Если состояние не изменилось - выходим
        if (shouldBeRaised == lastRaisedState) return;
        
        lastRaisedState = shouldBeRaised;

        if (shouldBeRaised)
            RaiseWeapon();
        else
            LowerWeapon();
    }

    #region Weapon Control

    public void RaiseWeapon()
    {
        if (!hasWeapon || isRaised) return;  // ОПТИМИЗАЦИЯ: Выход если уже поднято

        isRaised = true;
        core.Blackboard.SetBool(BlackboardKey.WeaponRaised, true);

        // НОВОЕ: Воспроизвести анимацию поднятия оружия
        if (core.Animation != null)
        {
            core.Animation.PlayAnimation("RaiseWeapon", 0.3f);
        }

        Debug.Log($"[Weapon] {core.name} - Weapon RAISED");
    }

    public void LowerWeapon()
    {
        if (!hasWeapon || !isRaised) return;  // ОПТИМИЗАЦИЯ: Выход если уже опущено

        isRaised = false;
        core.Blackboard.SetBool(BlackboardKey.WeaponRaised, false);

        // НОВОЕ: Воспроизвести анимацию опускания оружия
        if (core.Animation != null)
        {
            core.Animation.PlayAnimation("LowerWeapon", 0.3f);
        }

        Debug.Log($"[Weapon] {core.name} - Weapon LOWERED");
    }

    /// <summary>
    /// Целиться на цель
    /// </summary>
    public void AimAtTarget(Transform target)
    {
        if (target == null || !hasWeapon) return;

        Vector3 toTarget = target.position - core.Transform.position;
        core.Transform.rotation = Quaternion.LookRotation(toTarget, Vector3.up);
    }

    /// <summary>
    /// Стрелять (вызывает Gun.Fire если есть)
    /// </summary>
    public void Fire()
    {
        if (!hasWeapon || !isRaised) return;

        // Найти Gun компонент на оружии
        if (weaponModel != null)
        {
            var gun = weaponModel.GetComponent<Gun>();
            if (gun != null)
            {
                gun.Fire();
            }
        }

        core.Blackboard.SetFloat(BlackboardKey.LastShotTime, Time.time);
    }

    public void DropWeapon()
    {
        if (!hasWeapon) return;

        hasWeapon = false;
        isRaised = false;

        core.Blackboard.SetBool(BlackboardKey.HasWeapon, false);
        core.Blackboard.SetBool(BlackboardKey.WeaponRaised, false);

        // Hide original model
        if (weaponModel != null)
        {
            weaponModel.SetActive(false);
        }

        // Spawn dropped copy
        if (droppedWeaponPrefab != null && weaponHoldPoint != null)
        {
            GameObject dropped = Instantiate(
                droppedWeaponPrefab,
                weaponHoldPoint.position,
                weaponHoldPoint.rotation
            );

            // Add physics
            Rigidbody rb = dropped.GetComponent<Rigidbody>();
            if (rb == null)
                rb = dropped.AddComponent<Rigidbody>();

            // Ensure collider exists
            if (dropped.GetComponent<Collider>() == null)
            {
                dropped.AddComponent<BoxCollider>();
            }

            // Throw forward
            Vector3 throwDirection = core.Transform.forward + Vector3.up * 0.5f;
            rb.AddForce(throwDirection * dropForce, ForceMode.Impulse);
            rb.AddTorque(Random.insideUnitSphere * 2f, ForceMode.Impulse);

            Debug.Log($"[Weapon] {core.name} - Dropped weapon");
        }
    }

    #endregion

    #region Public Getters

    public bool HasWeapon() => hasWeapon;
    public bool IsWeaponRaised() => isRaised;

    #endregion
}