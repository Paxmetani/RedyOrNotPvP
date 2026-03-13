using UnityEngine;

public class AICombatModule : MonoBehaviour
{
    [Header("Combat Settings")]
    [SerializeField] private Transform firePoint;
    [SerializeField] private ParticleSystem muzzleFlash;
    [SerializeField] private float fireRate = 0.25f;
    [SerializeField] private float weaponDamage = 20f;
    [SerializeField] private float weaponRange = 50f;
    [SerializeField, Range(0f, 1f)] private float baseAccuracy = 0.8f;

    [Header("Body Rotation - FIXED")]
    [SerializeField] private bool disableNavMeshRotation = true;
    [SerializeField] private float combatRotationSpeed = 540f;
    [SerializeField] private float minAngleToShoot = 25f;

    [Header("Debug")]
    [SerializeField] private bool showDebugLogs = false;

    [Header("Suppression Settings")]
    [SerializeField] private float suppressionRange = 50f; // �������� ���� ��������
    [SerializeField] private float suppressionFireRate = 0.4f; // ��������� ��� ������� ��������
    [SerializeField] private bool allowLongRangeSuppression = true; // NEW

    private SmartEnemyAI core;
    private float nextFireTime = 0f;

    private float aggressionLevel = 0.5f;

    public void Initialize(SmartEnemyAI coreAI)
    {
        core = coreAI;
        //aggressionLevel = core.Blackboard.GetFloat("aggression", 0.5f);

        if (disableNavMeshRotation && core.Agent != null)
        {
            core.Agent.updateRotation = false;
        }
    }

    public void UpdateModule()
    {
        if (core.Blackboard.GetBool(BlackboardKey.IsSuppressed))
        {
            float suppressionEnd = core.Blackboard.GetFloat(BlackboardKey.SuppressionEndTime);
            if (Time.time > suppressionEnd)
            {
                core.Blackboard.SetBool(BlackboardKey.IsSuppressed, false);
            }
        }

        // ГЛАВНАЯ ЛОГИКА БОЯ - синхронизирована с AIController.UpdateCore
        Transform threat = core.Blackboard.GetTransform(BlackboardKey.CurrentThreat);
        bool inCombat = core.Blackboard.GetBool(BlackboardKey.InCombat);

        if (threat != null && inCombat)
        {
            // Враг видим - целимся и стреляем
            RotateToTarget(threat.position, combatRotationSpeed);
            
            // Стрельба если цель в угле
            Vector3 dirToTarget = (threat.position - core.Transform.position).normalized;
            dirToTarget.y = 0;
            
            if (dirToTarget != Vector3.zero)
            {
                float angleToTarget = Vector3.Angle(core.Transform.forward, dirToTarget);
                float dynamicAngle = Mathf.Lerp(minAngleToShoot, 60f, aggressionLevel);
                
                if (angleToTarget < dynamicAngle && Time.time >= nextFireTime)
                {
                    Fire(threat);
                }
            }
        }
        else if (!inCombat && core.Agent.updateRotation == false && core.Agent.hasPath)
        {
            // В патруле - поворачиваться в направлении движения
            RotateTowardsMovementDirection();
        }
    }

    /// <summary>
    /// �������� �������� �� ����. ����������� ���� ��������� ����� ���������.
    /// </summary>
    public void EngageTarget(Transform target)
    {
        if (target == null) return;

        bool weaponRaised = core.Blackboard.GetBool(BlackboardKey.WeaponRaised, false);
        if (!weaponRaised) return;

        Vector3 dirToTarget = (target.position - core.Transform.position).normalized;
        dirToTarget.y = 0;
        if (dirToTarget == Vector3.zero) return;

        float angleToTarget = Vector3.Angle(core.Transform.forward, dirToTarget);

        float dynamicAngle = Mathf.Lerp(minAngleToShoot, 60f, aggressionLevel);
        bool canShoot = angleToTarget < dynamicAngle;

        if (canShoot && Time.time >= nextFireTime)
        {
            Fire(target);
        }
    }

    private void Fire(Transform target)
    {
        if (firePoint == null) return;

        float rateModifier = Mathf.Lerp(1.0f, 0.4f, aggressionLevel);
        nextFireTime = Time.time + fireRate * rateModifier;

        if (muzzleFlash != null)
        {
            muzzleFlash.Play();
            SoundEmitter.Instance.PlaySoundAtPosition("Gunshot", firePoint.position);
        }

        // Уведомить других врагов о выстреле (слышат звук)
        var notifier = FindObjectOfType<GunshotNotifier>();
        if (notifier != null)
        {
            notifier.NotifyGunshot(firePoint.position, 1f, core.Health);
        }

        float finalAccuracy = CalculateAccuracy();

        // Целимся на центр массы врага
        Vector3 targetPoint = target.position + Vector3.up * 1.0f; // Центр грудной клетки
        Vector3 aimDir = (targetPoint - firePoint.position).normalized;

        // Добавить неточность если точность < 1.0
        if (finalAccuracy < 1.0f)
        {
            float spread = (1f - finalAccuracy) * 3f;
            aimDir += new Vector3(
                Random.Range(-spread, spread),
                Random.Range(-spread, spread),
                Random.Range(-spread, spread)
            ) * 0.05f;
            aimDir.Normalize();
        }

        if (showDebugLogs)
        {
            Debug.DrawRay(firePoint.position, aimDir * weaponRange, Color.red, 1f);
            Debug.DrawLine(firePoint.position, targetPoint, Color.green, 1f);
            Debug.Log($"[Combat] {core.name} - FIRING at {target.name}, accuracy: {finalAccuracy:F2}");
        }

        RaycastHit hit;
        if (Physics.Raycast(firePoint.position, aimDir, out hit, weaponRange))
        {
            // КРИТИЧНО: Проверить что попали в ВРАГА а не в союзника
            var targetHealth = hit.collider.GetComponent<HealthManager>();
            if (targetHealth == null)
                targetHealth = hit.collider.GetComponentInParent<HealthManager>();

            if (targetHealth != null)
            {
                // Проверить на врага
                if (targetHealth.TeamTag != core.Health.TeamTag)
                {
                    targetHealth.TakeDamage(weaponDamage, hit.point, aimDir);

                    if (showDebugLogs)
                        Debug.Log($"[Combat] {core.name} - HIT ENEMY {hit.collider.name} at {hit.point}!");
                }
                else
                {
                    // ПОПАЛИ В СОЮЗНИКА - не наносим урон!
                    if (showDebugLogs)
                        Debug.Log($"[Combat] {core.name} - HIT FRIENDLY {hit.collider.name} - NO DAMAGE");
                }
            }
            else if (showDebugLogs)
            {
                Debug.Log($"[Combat] {core.name} - Hit {hit.collider.name} but no HealthManager");
            }
        }
        else if (showDebugLogs)
        {
            Debug.Log($"[Combat] {core.name} - MISSED (no hit)");
        }
    }

    private float CalculateAccuracy()
    {
        float accuracy = Mathf.Lerp(baseAccuracy, 0.98f, aggressionLevel);

        if (core.Blackboard.GetBool(BlackboardKey.IsSuppressed))
        {
            accuracy *= 0.5f;
        }

        float healthPercent = core.GetHealthPercentage();
        accuracy *= Mathf.Lerp(0.7f, 1f, healthPercent);

        if (core.Blackboard.GetBool(BlackboardKey.IsInCover))
        {
            accuracy *= 0.9f;
        }

        float speed = core.Agent.velocity.magnitude;
        if (speed > 0.5f)
        {
            accuracy *= 0.75f;
        }

        return Mathf.Clamp01(accuracy);
    }

    private void RotateToTarget(Vector3 targetPosition, float rotSpeed)
    {
        Vector3 direction = (targetPosition - core.Transform.position).normalized;
        direction.y = 0;

        if (direction == Vector3.zero) return;

        Quaternion targetRotation = Quaternion.LookRotation(direction);
        core.Transform.rotation = Quaternion.RotateTowards(
            core.Transform.rotation,
            targetRotation,
            rotSpeed * Time.deltaTime
        );
    }

    private void RotateTowardsMovementDirection()
    {
        if (core.Agent.velocity.sqrMagnitude < 0.01f) return;

        Vector3 direction = core.Agent.velocity.normalized;
        direction.y = 0;

        if (direction == Vector3.zero) return;

        Quaternion targetRotation = Quaternion.LookRotation(direction);
        core.Transform.rotation = Quaternion.Slerp(
            core.Transform.rotation,
            targetRotation,
            5f * Time.deltaTime
        );
    }

    public void SuppressPosition(Vector3 position)
    {
        if (firePoint == null) return;

        bool weaponRaised = core.Blackboard.GetBool(BlackboardKey.WeaponRaised);
        if (!weaponRaised) return;

        // �������� ���������
        float distance = Vector3.Distance(core.Transform.position, position);

        // ��������� �������� �� ������� ���������
        if (distance > suppressionRange && !allowLongRangeSuppression)
        {
            return;
        }

        // ������� � �������
        Vector3 dirToPos = (position - core.Transform.position).normalized;
        dirToPos.y = 0;

        if (dirToPos != Vector3.zero)
        {
            core.Movement?.SetLookTarget(dirToPos, RotationMode.Combat);
        }

        if (Time.time >= nextFireTime)
        {
            nextFireTime = Time.time + suppressionFireRate;

            if (muzzleFlash != null)
            {
                muzzleFlash.Play();
                SoundEmitter.Instance.PlaySoundAtPosition("Gunshot", firePoint.position);
            }

            // Уведомить врагов о выстреле
            var notifier = FindObjectOfType<GunshotNotifier>();
            if (notifier != null)
            {
                notifier.NotifyGunshot(firePoint.position, 1f, core.Health);
            }

            Vector3 aimDir = (position - firePoint.position).normalized;

            // ������� ������� ��� ����������
            float spread = Mathf.Lerp(8f, 15f, distance / suppressionRange);
            aimDir += new Vector3(
                Random.Range(-spread, spread),
                Random.Range(-spread, spread),
                Random.Range(-spread, spread)
            ) * 0.1f;
            aimDir.Normalize();

            if (showDebugLogs)
            {
                Debug.DrawRay(firePoint.position, aimDir * weaponRange, Color.yellow, 0.5f);
            }

            RaycastHit hit;
            if (Physics.Raycast(firePoint.position, aimDir, out hit, weaponRange))
            {
                var targetHealth = hit.collider.GetComponent<HealthManager>();
                if (targetHealth == null)
                    targetHealth = hit.collider.GetComponentInParent<HealthManager>();

                if (targetHealth != null && targetHealth.TeamTag != core.Health.TeamTag)
                {
                    // ������ ����� ��� ����������
                    targetHealth.TakeDamage(weaponDamage * 0.5f, hit.point, aimDir);
                }
            }
        }
    }
}