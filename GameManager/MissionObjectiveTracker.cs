using UnityEngine;
using UnityEngine.Events;
using System.Collections.Generic;

/// <summary>
/// Трекер целей миссии
/// Автоматически отслеживает выполнение целей и сообщает GameManager
/// </summary>
public class MissionObjectiveTracker : MonoBehaviour
{
    public static MissionObjectiveTracker Instance { get; private set; }

    [Header("Auto-detect")]
    [SerializeField] private bool autoDetectOnStart = true;

    [Header("Tracked Entities")]
    [SerializeField] private List<TrackedCivilian> civilians = new List<TrackedCivilian>();
    [SerializeField] private List<TrackedSuspect> suspects = new List<TrackedSuspect>();
    [SerializeField] private List<TrackedEvidence> evidence = new List<TrackedEvidence>();
    [SerializeField] private List<TrackedZone> zones = new List<TrackedZone>();

    [Header("Events")]
    public UnityEvent OnAllSuspectsDealt;
    public UnityEvent OnAllCiviliansSecured;
    public UnityEvent OnAllEvidenceCollected;

    private GameManagerTactical gameManager;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
    }

    private void Start()
    {
        gameManager = GameManagerTactical.Instance;

        if (autoDetectOnStart)
            AutoDetectEntities();
    }

    #region Auto Detection

    /// <summary>
    /// Автоматически найти все отслеживаемые объекты на сцене
    /// </summary>
    public void AutoDetectEntities()
    {
        // Найти подозреваемых (AI с определённым тегом или компонентом)
        var allAI = FindObjectsByType<SmartEnemyAI>(FindObjectsSortMode.None);
        foreach (var ai in allAI)
        {
            if (!IsSuspectTracked(ai.gameObject))
            {
                RegisterSuspect(ai);
            }
        }

        // Найти улики
        var allEvidence = FindObjectsByType<EvidenceItem>(FindObjectsSortMode.None);
        foreach (var item in allEvidence)
        {
            if (!IsEvidenceTracked(item.gameObject))
            {
                RegisterEvidence(item);
            }
        }

        // Найти зоны
        var allZones = FindObjectsByType<ObjectiveZone>(FindObjectsSortMode.None);
        foreach (var zone in allZones)
        {
            if (!IsZoneTracked(zone.gameObject))
            {
                RegisterZone(zone);
            }
        }

        Debug.Log($"[ObjectiveTracker] Detected: {suspects.Count} suspects, {evidence.Count} evidence, {zones.Count} zones");
    }

    #endregion

    #region Registration

    public void RegisterCivilian(GameObject civilianObj, bool isHostage = false)
    {
        if (IsCivilianTracked(civilianObj)) return;

        var tracked = new TrackedCivilian
        {
            gameObject = civilianObj,
            data = new CivilianData
            {
                id = civilianObj.name,
                gameObject = civilianObj,
                isHostage = isHostage
            }
        };

        civilians.Add(tracked);
    }

    public void RegisterSuspect(SmartEnemyAI ai)
    {
        if (IsSuspectTracked(ai.gameObject)) return;

        var tracked = new TrackedSuspect
        {
            gameObject = ai.gameObject,
            ai = ai,
            data = new SuspectData
            {
                id = ai.name,
                gameObject = ai.gameObject,
                ai = ai,
                wasArmed = true // Предполагаем что вооружён
            }
        };

        // Подписаться на события AI
        SubscribeToAI(tracked);

        suspects.Add(tracked);
    }

    public void RegisterEvidence(EvidenceItem item)
    {
        if (IsEvidenceTracked(item.gameObject)) return;

        var tracked = new TrackedEvidence
        {
            gameObject = item.gameObject,
            item = item,
            data = new EvidenceData
            {
                id = item.gameObject.name,
                description = item.description,
                gameObject = item.gameObject
            }
        };

        // Подписаться на сбор
        item.OnCollected.AddListener(() => OnEvidenceCollectedHandler(tracked));

        evidence.Add(tracked);
    }

    public void RegisterZone(ObjectiveZone zone)
    {
        if (IsZoneTracked(zone.gameObject)) return;

        var tracked = new TrackedZone
        {
            gameObject = zone.gameObject,
            zone = zone
        };

        zone.OnObjectiveCompleted.AddListener(() => OnZoneCompleted(tracked));

        zones.Add(tracked);
    }

    #endregion

    #region Event Handlers

    private void SubscribeToAI(TrackedSuspect tracked)
    {
        if (tracked.ai == null) return;

        // Подписаться на смерть
        var health = tracked.ai.GetComponent<HealthManager>();
        if (health != null)
        {
            // Проверяем состояние в Update или через событие
        }
    }

    /// <summary>
    /// Вызывается когда подозреваемый арестован
    /// </summary>
    public void ReportSuspectArrested(SmartEnemyAI ai)
    {
        var tracked = GetTrackedSuspect(ai.gameObject);
        if (tracked == null) return;

        tracked.data.isArrested = true;

        gameManager?.ReportSuspectArrested(tracked.data);

        CheckAllSuspectsDealt();
    }

    /// <summary>
    /// Вызывается когда подозреваемый убит
    /// </summary>
    public void ReportSuspectKilled(SmartEnemyAI ai, bool wasHostile)
    {
        var tracked = GetTrackedSuspect(ai.gameObject);
        if (tracked == null) return;

        tracked.data.isNeutralized = true;
        tracked.data.wasHostile = wasHostile;

        gameManager?.ReportSuspectNeutralized(tracked.data, tracked.data.wasArmed, wasHostile);

        CheckAllSuspectsDealt();
    }

    /// <summary>
    /// Вызывается когда подозреваемый сбежал
    /// </summary>
    public void ReportSuspectEscaped(SmartEnemyAI ai)
    {
        var tracked = GetTrackedSuspect(ai.gameObject);
        if (tracked == null) return;

        tracked.data.hasEscaped = true;

        gameManager?.ReportSuspectEscaped(tracked.data);
    }

    /// <summary>
    /// Вызывается когда гражданский спасён
    /// </summary>
    public void ReportCivilianRescued(GameObject civilianObj)
    {
        var tracked = GetTrackedCivilian(civilianObj);
        if (tracked == null) return;

        tracked.data.isRescued = true;

        gameManager?.ReportCivilianSaved(tracked.data);

        CheckAllCiviliansSecured();
    }

    /// <summary>
    /// Вызывается когда гражданский погиб
    /// </summary>
    public void ReportCivilianKilled(GameObject civilianObj, bool byPlayer)
    {
        var tracked = GetTrackedCivilian(civilianObj);
        if (tracked == null) return;

        tracked.data.isDead = true;

        gameManager?.ReportCivilianKilled(tracked.data, byPlayer);
    }

    private void OnEvidenceCollectedHandler(TrackedEvidence tracked)
    {
        tracked.data.isCollected = true;

        gameManager?.ReportEvidenceCollected(tracked.data);

        CheckAllEvidenceCollected();
    }

    private void OnZoneCompleted(TrackedZone tracked)
    {
        // Найти соответствующую цель и отметить выполненной
        if (gameManager?.CurrentMission != null)
        {
            foreach (var obj in gameManager.CurrentMission.objectives)
            {
                if (obj.objectiveId == tracked.zone.objectiveId && !obj.isCompleted)
                {
                    gameManager.ReportObjectiveCompleted(obj);
                    break;
                }
            }
        }
    }

    #endregion

    #region Checks

    private void CheckAllSuspectsDealt()
    {
        foreach (var suspect in suspects)
        {
            if (!suspect.data.isArrested && !suspect.data.isNeutralized && !suspect.data.hasEscaped)
                return;
        }

        OnAllSuspectsDealt?.Invoke();

        // Отметить цель
        CompleteObjectiveByType(ObjectiveType.EliminateAllThreats);
    }

    private void CheckAllCiviliansSecured()
    {
        foreach (var civilian in civilians)
        {
            if (!civilian.data.isRescued && !civilian.data.isDead)
                return;
        }

        OnAllCiviliansSecured?.Invoke();

        // Отметить цель
        CompleteObjectiveByType(ObjectiveType.RescueHostages);
    }

    private void CheckAllEvidenceCollected()
    {
        foreach (var ev in evidence)
        {
            if (!ev.data.isCollected)
                return;
        }

        OnAllEvidenceCollected?.Invoke();

        // Отметить цель
        CompleteObjectiveByType(ObjectiveType.SecureEvidence);
    }

    private void CompleteObjectiveByType(ObjectiveType type)
    {
        if (gameManager?.CurrentMission == null) return;

        foreach (var obj in gameManager.CurrentMission.objectives)
        {
            if (obj.type == type && !obj.isCompleted)
            {
                gameManager.ReportObjectiveCompleted(obj);
                break;
            }
        }
    }

    #endregion

    #region Queries

    private bool IsCivilianTracked(GameObject obj)
    {
        foreach (var c in civilians)
            if (c.gameObject == obj) return true;
        return false;
    }

    private bool IsSuspectTracked(GameObject obj)
    {
        foreach (var s in suspects)
            if (s.gameObject == obj) return true;
        return false;
    }

    private bool IsEvidenceTracked(GameObject obj)
    {
        foreach (var e in evidence)
            if (e.gameObject == obj) return true;
        return false;
    }

    private bool IsZoneTracked(GameObject obj)
    {
        foreach (var z in zones)
            if (z.gameObject == obj) return true;
        return false;
    }

    private TrackedCivilian GetTrackedCivilian(GameObject obj)
    {
        foreach (var c in civilians)
            if (c.gameObject == obj) return c;
        return null;
    }

    private TrackedSuspect GetTrackedSuspect(GameObject obj)
    {
        foreach (var s in suspects)
            if (s.gameObject == obj) return s;
        return null;
    }

    #endregion

    #region Stats

    public int GetTotalSuspects() => suspects.Count;
    public int GetArrestedCount() => suspects.FindAll(s => s.data.isArrested).Count;
    public int GetNeutralizedCount() => suspects.FindAll(s => s.data.isNeutralized).Count;
    public int GetEscapedCount() => suspects.FindAll(s => s.data.hasEscaped).Count;

    public int GetTotalCivilians() => civilians.Count;
    public int GetRescuedCount() => civilians.FindAll(c => c.data.isRescued).Count;
    public int GetCivilianCasualties() => civilians.FindAll(c => c.data.isDead).Count;

    public int GetTotalEvidence() => evidence.Count;
    public int GetCollectedEvidence() => evidence.FindAll(e => e.data.isCollected).Count;

    #endregion
}

#region Tracked Classes

[System.Serializable]
public class TrackedCivilian
{
    public GameObject gameObject;
    public CivilianData data;
}

[System.Serializable]
public class TrackedSuspect
{
    public GameObject gameObject;
    public SmartEnemyAI ai;
    public SuspectData data;
}

[System.Serializable]
public class TrackedEvidence
{
    public GameObject gameObject;
    public EvidenceItem item;
    public EvidenceData data;
}

[System.Serializable]
public class TrackedZone
{
    public GameObject gameObject;
    public ObjectiveZone zone;
}

#endregion
