using UnityEngine;

/// <summary>
/// Оптимизированная система проверки линии видимости
/// Использует кэширование, интервалы проверки и умные хитрости
/// </summary>
public class AILineOfSightSystem : MonoBehaviour
{
    [Header("Line of Sight Settings")]
    [SerializeField] private float losCheckInterval = 0.2f; // Проверка каждые 0.2 сек вместо каждого кадра
    [SerializeField] private LayerMask visionBlockers; // Что блокирует видимость

    [Header("Smart Optimizations")]
    [SerializeField] private bool useDistanceBasedInterval = true; // Дальше = реже проверяем
    [SerializeField] private float nearDistance = 10f; // Близко = проверять часто
    [SerializeField] private float farDistance = 30f; // Далеко = проверять редко
    [SerializeField] private float nearCheckInterval = 0.1f; // Проверка близких целей
    [SerializeField] private float farCheckInterval = 0.4f; // Проверка дальних целей

    [Header("Multi-Point Check (хитрость для щелей)")]
    [SerializeField] private bool useMultiPointCheck = true;
    [SerializeField] private int checkPointsCount = 3; // Сколько точек проверять
    [SerializeField] private float checkPointSpread = 0.5f; // Разброс точек

    [Header("Cache & Performance")]
    [SerializeField] private bool useCaching = true;
    [SerializeField] private float cacheValidDuration = 0.15f; // Кэш валиден 0.15 сек

    [Header("Debug")]
    [SerializeField] private bool showDebugRays = false;
    [SerializeField] private bool showPerformanceStats = false;

    private SmartEnemyAI core;

    // Cache
    private bool cachedHasLOS = false;
    private float lastLOSCheckTime = -999f;
    private float lastCacheTime = -999f;
    private Transform lastTarget = null;

    // Performance tracking
    private int checksThisFrame = 0;
    private int totalChecksThisSecond = 0;
    private float lastStatsResetTime = 0f;

    public void Initialize(SmartEnemyAI coreAI)
    {
        core = coreAI;
    }

    /// <summary>
    /// ГЛАВНЫЙ МЕТОД:  Проверка линии видимости к цели
    /// Использует все оптимизации
    /// </summary>
    public bool HasLineOfSight(Transform target)
    {
        if (target == null) return false;

        // ХИТРОСТЬ 1: Кэширование
        if (useCaching && target == lastTarget)
        {
            float timeSinceCache = Time.time - lastCacheTime;

            if (timeSinceCache < cacheValidDuration)
            {
                // Используем кэшированный результат
                return cachedHasLOS;
            }
        }

        // ХИТРОСТЬ 2: Интервальная проверка
        float checkInterval = GetCheckInterval(target);
        float timeSinceLastCheck = Time.time - lastLOSCheckTime;

        if (timeSinceLastCheck < checkInterval)
        {
            // Слишком рано для новой проверки
            return cachedHasLOS;
        }

        // Выполнить проверку
        lastLOSCheckTime = Time.time;
        checksThisFrame++;
        totalChecksThisSecond++;

        bool hasLOS = PerformLOSCheck(target);

        // Сохранить в кэш
        cachedHasLOS = hasLOS;
        lastCacheTime = Time.time;
        lastTarget = target;

        return hasLOS;
    }

    /// <summary>
    /// Реальная проверка линии видимости
    /// </summary>
    private bool PerformLOSCheck(Transform target)
    {
        Vector3 eyePos = core.Transform.position + Vector3.up * 1.6f;
        Vector3 targetPos = target.position + Vector3.up * 1.5f; // Грудь цели

        // ХИТРОСТЬ 3: Быстрая проверка дистанции (без raycast)
        float distanceSqr = (targetPos - eyePos).sqrMagnitude;
        float maxRangeSqr = 50f * 50f; // Максимальная дальность видимости

        if (distanceSqr > maxRangeSqr)
        {
            return false; // Слишком далеко, даже не проверяем raycast
        }

        // ХИТРОСТЬ 4: Multi-point check (видит через щели)
        if (useMultiPointCheck)
        {
            return MultiPointLOSCheck(eyePos, targetPos, target);
        }
        else
        {
            return SingleRaycastCheck(eyePos, targetPos, target);
        }
    }

    /// <summary>
    /// Одиночный raycast (быстро, но не видит щели)
    /// </summary>
    private bool SingleRaycastCheck(Vector3 from, Vector3 to, Transform target)
    {
        Vector3 direction = (to - from).normalized;
        float distance = Vector3.Distance(from, to);

        RaycastHit hit;

        if (showDebugRays)
        {
            Debug.DrawLine(from, to, Color.yellow, 0.2f);
        }

        if (Physics.Raycast(from, direction, out hit, distance, visionBlockers))
        {
            // Попали в препятствие
            if (hit.transform == target || hit.transform.IsChildOf(target))
            {
                // Попали в саму цель - это OK
                return true;
            }

            // Попали в стену/препятствие
            return false;
        }

        // Ничего не попали - чистая линия
        return true;
    }

    /// <summary>
    /// Multi-point check - проверка нескольких точек
    /// ХИТРОСТЬ:  Видит через маленькие щели
    /// </summary>
    private bool MultiPointLOSCheck(Vector3 from, Vector3 to, Transform target)
    {
        // Основная точка (центр)
        if (SingleRaycastCheck(from, to, target))
        {
            return true;
        }

        // Дополнительные точки (смещения)
        Vector3[] offsets = GenerateCheckOffsets();

        foreach (var offset in offsets)
        {
            Vector3 offsetFrom = from + offset;
            Vector3 offsetTo = to + offset;

            if (SingleRaycastCheck(offsetFrom, offsetTo, target))
            {
                if (showDebugRays)
                {
                    Debug.DrawLine(offsetFrom, offsetTo, Color.green, 0.2f);
                }

                return true; // Нашли щель! 
            }
        }

        return false; // Ни одна точка не прошла
    }

    /// <summary>
    /// Генерация точек проверки (оптимизировано)
    /// </summary>
    private Vector3[] GenerateCheckOffsets()
    {
        // Статический массив для избежания GC allocation
        if (checkOffsets == null || checkOffsets.Length != checkPointsCount)
        {
            checkOffsets = new Vector3[checkPointsCount];

            // Предрасчет смещений (один раз)
            for (int i = 0; i < checkPointsCount; i++)
            {
                float angle = (360f / checkPointsCount) * i;
                float rad = angle * Mathf.Deg2Rad;

                checkOffsets[i] = new Vector3(
                    Mathf.Cos(rad) * checkPointSpread,
                    0f,
                    Mathf.Sin(rad) * checkPointSpread
                );
            }
        }

        return checkOffsets;
    }

    private Vector3[] checkOffsets; // Кэш смещений

    /// <summary>
    /// ХИТРОСТЬ:  Динамический интервал проверки
    /// Близкие цели = проверяем часто
    /// Дальние цели = проверяем редко
    /// </summary>
    private float GetCheckInterval(Transform target)
    {
        if (!useDistanceBasedInterval)
        {
            return losCheckInterval;
        }

        float distance = Vector3.Distance(core.Transform.position, target.position);

        if (distance < nearDistance)
        {
            return nearCheckInterval; // 0.1 сек - часто
        }
        else if (distance > farDistance)
        {
            return farCheckInterval; // 0.4 сек - редко
        }
        else
        {
            // Интерполяция между near и far
            float t = (distance - nearDistance) / (farDistance - nearDistance);
            return Mathf.Lerp(nearCheckInterval, farCheckInterval, t);
        }
    }

    /// <summary>
    /// Быстрая проверка - есть ли ХОТЬ КАКАЯ-ТО видимость
    /// Без точных расчетов, только грубая оценка
    /// </summary>
    public bool HasApproximateLOS(Transform target)
    {
        if (target == null) return false;

        Vector3 dirToTarget = (target.position - core.Transform.position).normalized;
        float distance = Vector3.Distance(core.Transform.position, target.position);

        // Быстрый raycast без проверки деталей
        return !Physics.Raycast(
            core.Transform.position + Vector3.up * 1.6f,
            dirToTarget,
            distance - 1f, // Немного короче, чтобы не проверять саму цель
            visionBlockers
        );
    }

    /// <summary>
    /// Сброс кэша (вызывать при смене цели)
    /// </summary>
    public void InvalidateCache()
    {
        lastTarget = null;
        lastCacheTime = -999f;
    }

    private void Update()
    {
        checksThisFrame = 0;

        // Performance stats
        if (showPerformanceStats && Time.time - lastStatsResetTime >= 1f)
        {
            Debug.Log($"[LOS] {core.name} - Checks/sec: {totalChecksThisSecond}");
            totalChecksThisSecond = 0;
            lastStatsResetTime = Time.time;
        }
    }
}