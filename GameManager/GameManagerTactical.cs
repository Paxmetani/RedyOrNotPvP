using System;
using System.Collections;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Events;
using YG;

/// <summary>
/// Тактический GameManager (Ready Or Not style)
/// State Machine архитектура для управления игровым процессом
/// </summary>
public class GameManagerTactical : MonoBehaviour
{
    public static GameManagerTactical Instance { get; private set; }
    public PlayerController playerController;

    #region Game States

    public enum GameState
    {
        None,
        Briefing,       // Брифинг перед миссией
        Deploying,      // Выбор точки высадки / подготовка
        InProgress,     // Миссия активна
        Paused,         // Пауза
        MissionSuccess, // Успех
        MissionFailed,  // Провал
        Debriefing      // Подведение итогов
    }

    [Header("State")]
    [SerializeField] private GameState currentState = GameState.None;
    [SerializeField] private GameState previousState = GameState.None;

    public GameState CurrentState => currentState;
    public GameState PreviousState => previousState;

    #endregion

    #region Mission Data

    [Header("Mission")]
    [SerializeField] private MissionData currentMission;
    [SerializeField] private float missionTimer;
    [SerializeField] private bool hasTimeLimit = true;
    [SerializeField] private float maxMissionTime = 1800f; // 30 минут

    public MissionData CurrentMission => currentMission;
    public float MissionTimer => missionTimer;
    public float MissionTimeRemaining => hasTimeLimit ? Mathf.Max(0, maxMissionTime - missionTimer) : -1f;


    #endregion

    #region Score & Stats

    [Header("Score")]
    [SerializeField] private MissionScore score;
    private bool reviveUsed;

    public MissionScore Score => score;
    /// <summary>Игрок может воспользоваться ревайвом — только один раз за миссию.</summary>
    public bool CanRevive => !reviveUsed && currentState == GameState.MissionFailed;

    #endregion

    #region Events

    [Header("Events")]
    public UnityEvent<GameState> OnStateChanged;
    public UnityEvent OnMissionStart;
    public UnityEvent OnMissionSuccess;
    public UnityEvent OnMissionFailed;
    public UnityEvent<ObjectiveData> OnObjectiveCompleted;
    public UnityEvent<ObjectiveData> OnObjectiveFailed;
    public UnityEvent<CivilianData> OnCivilianSaved;
    public UnityEvent<CivilianData> OnCivilianKilled;
    public UnityEvent<SuspectData> OnSuspectArrested;
    public UnityEvent<SuspectData> OnSuspectNeutralized;
    public UnityEvent<SuspectData> OnSuspectEscaped;
    public UnityEvent<EvidenceData> OnEvidenceCollected;
    public UnityEvent OnPlayerDown;
    public UnityEvent OnSquadWiped;

    #endregion

    #region References

    [Header("References")]
    [SerializeField] private TacticalUIManager uiManager;
    [SerializeField] private PlayerController player;
    [SerializeField] private AISquadManager squadManager;
    [SerializeField] private Transform[] deploymentPoints;

    #endregion

    #region Unity Lifecycle

    private void Awake()
    {
        // Singleton
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;

        InitializeScore();
    }

    private void Start()
    {
        // Найти ссылки если не назначены
        if (player == null)
            player = FindFirstObjectByType<PlayerController>();

        if (squadManager == null)
            squadManager = FindFirstObjectByType<AISquadManager>();

        // Начать с брифинга или сразу миссию
        if (currentMission != null)
            SetState(GameState.Briefing);

    }

    private void Update()
    {
        if (currentState == GameState.InProgress)
        {
            UpdateMissionTimer();
            CheckMissionConditions();
        }
    }

    #endregion

    #region State Machine

    /// <summary>
    /// Изменить состояние игры
    /// </summary>
    public void SetState(GameState newState)
    {
        if (currentState == newState) return;

        // Выход из текущего состояния
        OnExitState(currentState);

        previousState = currentState;
        currentState = newState;

        // Вход в новое состояние
        OnEnterState(newState);

        OnStateChanged?.Invoke(newState);

        Debug.Log($"[GameManager] State: {previousState} ? {newState}");
    }

    private void OnExitState(GameState state)
    {
        switch (state)
        {
            case GameState.Paused:
                Time.timeScale = 1f;
                break;

            case GameState.InProgress:
                // Сохранить статистику
                break;
        }
    }

    private void OnEnterState(GameState state)
    {
        switch (state)
        {
            case GameState.Briefing:
                EnterBriefing();
                break;

            case GameState.Deploying:
                EnterDeploying();
                break;

            case GameState.InProgress:
                EnterInProgress();
                break;

            case GameState.Paused:
                Time.timeScale = 0f;
                break;

            case GameState.MissionSuccess:
                EnterMissionSuccess();
                break;

            case GameState.MissionFailed:
                EnterMissionFailed();
                break;

            case GameState.Debriefing:
                EnterDebriefing();
                break;
        }
    }

    #endregion

    #region State Handlers

    private void EnterBriefing()
    {
        uiManager?.ShowBriefing(currentMission);
    }

    private void EnterDeploying()
    {
        uiManager?.ShowDeployment(deploymentPoints);
    }

    private void EnterInProgress()
    {
        missionTimer = 0f;
        uiManager?.ShowHUD();
        OnMissionStart?.Invoke();
    }

    private void EnterMissionSuccess()
    {
        CalculateFinalScore();
        uiManager?.ShowMissionComplete(true, score);
        OnMissionSuccess?.Invoke();
    }

    private void EnterMissionFailed()
    {
        OnMissionFailed?.Invoke();
    }

    private void EnterDebriefing()
    {
        uiManager?.ShowDebriefing(score);
    }

    #endregion

    #region Mission Logic

    private void UpdateMissionTimer()
    {
        missionTimer += Time.deltaTime;

        // Проверка лимита времени
        if (hasTimeLimit && missionTimer >= maxMissionTime)
        {
            // Время вышло — провал или успех зависит от objectives
            CheckTimeExpired();
        }
    }

    private void CheckMissionConditions()
    {
        if (currentMission == null) return;

        // Проверить условия провала
        if (CheckFailConditions())
        {
            SetState(GameState.MissionFailed);
            return;
        }

        // Проверить условия успеха
        if (CheckSuccessConditions())
        {
            SetState(GameState.MissionSuccess);
        }
    }

    private bool CheckFailConditions()
    {
        // Игрок погиб
        if (player != null && player.healthManager != null && player.healthManager.IsDead)
            return true;

        // Слишком много гражданских погибло
        if (score.civiliansKilled >= currentMission.maxCivilianCasualties)
            return true;

        // Все заложники погибли
        if (currentMission.requireHostageRescue && score.hostagesRescued == 0 && score.hostagesKilled >= currentMission.totalHostages)
            return true;

        return false;
    }

    private bool CheckSuccessConditions()
    {
        if (currentMission == null) return false;

        // Проверить цели из MissionData
        foreach (var objective in currentMission.objectives)
        {
            if (objective.isRequired && !objective.isCompleted)
            {
                // Авто-проверка для цели "Eliminate All Threats"
                if (objective.type == ObjectiveType.EliminateAllThreats)
                {
                    var tracker = MissionObjectiveTracker.Instance;
                    if (tracker != null)
                    {
                        int total = tracker.GetTotalSuspects();
                        int dealt = tracker.GetArrestedCount() + tracker.GetNeutralizedCount();

                        if (total > 0 && dealt >= total)
                        {
                            objective.isCompleted = true;
                            score.objectivesCompleted++;
                            OnObjectiveCompleted?.Invoke(objective);
                            continue;
                        }
                    }
                }

                return false; // Цель не выполнена
            }
        }

        return true;
    }

    private void CheckTimeExpired()
    {
        // Проверить сколько целей выполнено
        if (CheckSuccessConditions())
        {
            SetState(GameState.MissionSuccess);
        }
        else
        {
            SetState(GameState.MissionFailed);
        }
    }

    #endregion

    #region Public API - Game Flow

    /// <summary>
    /// Начать миссию (из брифинга)
    /// </summary>
    public void StartMission()
    {
        if (currentState == GameState.Briefing)
            SetState(GameState.Deploying);
        else if (currentState == GameState.Deploying)
            SetState(GameState.InProgress);
    }

    /// <summary>
    /// Выбрать точку высадки и начать
    /// </summary>
    public void Deploy(int deploymentIndex)
    {
        if (currentState != GameState.Deploying) return;
        if (deploymentPoints == null || deploymentIndex >= deploymentPoints.Length) return;

        // Телепортировать игрока
        if (player != null)
        {
            player.transform.position = deploymentPoints[deploymentIndex].position;
        }

        SetState(GameState.InProgress);
    }

    /// <summary>
    /// Пауза
    /// </summary>
    public void TogglePause()
    {
        if (currentState == GameState.InProgress)
            SetState(GameState.Paused);
        else if (currentState == GameState.Paused)
            SetState(GameState.InProgress);
    }

    /// <summary>
    /// Завершить миссию вручную (эвакуация)
    /// </summary>
    public void RequestExtraction()
    {
        if (currentState != GameState.InProgress) return;

        // Проверить можно ли эвакуироваться
        if (CheckSuccessConditions())
        {
            SetState(GameState.MissionSuccess);
        }
        else
        {
            // Показать что не все цели выполнены
            uiManager?.ShowExtractionDenied();
        }
    }

    /// <summary>
    /// Рестарт миссии
    /// </summary>
    public void RestartMission()
    {
        InitializeScore();
        SetState(GameState.Briefing);
    }

    #endregion

    #region Public API - Events

    /// <summary>
    /// Вызывается когда гражданский спасён
    /// </summary>
    public void ReportCivilianSaved(CivilianData civilian)
    {
        if (currentState != GameState.InProgress) return;

        score.civiliansRescued++;
        score.AddScore(ScoreType.CivilianRescued);

        OnCivilianSaved?.Invoke(civilian);
    }

    /// <summary>
    /// Вызывается когда гражданский погиб
    /// </summary>
    public void ReportCivilianKilled(CivilianData civilian, bool byPlayer)
    {
        if (currentState != GameState.InProgress) return;

        score.civiliansKilled++;

        if (byPlayer)
            score.AddPenalty(PenaltyType.CivilianKilledByPlayer);
        else
            score.AddPenalty(PenaltyType.CivilianKilled);

        OnCivilianKilled?.Invoke(civilian);
    }

    /// <summary>
    /// Вызывается когда подозреваемый арестован
    /// </summary>
    public void ReportSuspectArrested(SuspectData suspect)
    {
        if (currentState != GameState.InProgress) return;

        score.suspectsArrested++;
        score.AddScore(ScoreType.SuspectArrested);

        OnSuspectArrested?.Invoke(suspect);
    }

    /// <summary>
    /// Вызывается когда подозреваемый нейтрализован (убит)
    /// </summary>
    public void ReportSuspectNeutralized(SuspectData suspect, bool wasArmed, bool wasHostile)
    {
        if (currentState != GameState.InProgress) return;

        score.suspectsNeutralized++;

        if (wasHostile)
            score.AddScore(ScoreType.SuspectNeutralizedHostile);
        else if (wasArmed)
            score.AddScore(ScoreType.SuspectNeutralizedArmed);
        else
            score.AddPenalty(PenaltyType.UnauthorizedUseOfForce);

        OnSuspectNeutralized?.Invoke(suspect);
    }

    /// <summary>
    /// Вызывается когда подозреваемый сбежал
    /// </summary>
    public void ReportSuspectEscaped(SuspectData suspect)
    {
        if (currentState != GameState.InProgress) return;

        score.suspectsEscaped++;
        score.AddPenalty(PenaltyType.SuspectEscaped);

        OnSuspectEscaped?.Invoke(suspect);
    }

    /// <summary>
    /// Вызывается когда улика собрана
    /// </summary>
    public void ReportEvidenceCollected(EvidenceData evidence)
    {
        if (currentState != GameState.InProgress) return;

        score.evidenceCollected++;
        score.AddScore(ScoreType.EvidenceCollected);

        OnEvidenceCollected?.Invoke(evidence);
    }

    /// <summary>
    /// Вызывается когда цель выполнена
    /// </summary>
    public void ReportObjectiveCompleted(ObjectiveData objective)
    {
        if (currentState != GameState.InProgress) return;

        objective.isCompleted = true;
        score.AddScore(ScoreType.ObjectiveCompleted);

        OnObjectiveCompleted?.Invoke(objective);

        // Проверить условия победы
        CheckMissionConditions();
    }

    /// <summary>
    /// Вызывается когда игрок ранен/убит
    /// </summary>
    public void ReportPlayerDown()
    {
        OnPlayerDown?.Invoke();

        // Проверить провал
        CheckMissionConditions();
    }

    #endregion

    #region Score

    private void InitializeScore()
    {
        score = new MissionScore();
        reviveUsed = false;
    }

    private void CalculateFinalScore()
    {
        score.missionTime = missionTimer;

        // Бонус за скорость
        if (hasTimeLimit)
        {
            float timeRatio = 1f - (missionTimer / maxMissionTime);
            score.timeBonus = Mathf.RoundToInt(timeRatio * 500f);
        }

        // Рассчитать рейтинг
        score.CalculateRating();
    }

    #endregion

    #region Helpers

    /// <summary>
    /// Загрузить миссию
    /// </summary>
    public void LoadMission(MissionData mission)
    {
        currentMission = mission;
        hasTimeLimit = mission.hasTimeLimit;
        maxMissionTime = mission.timeLimit;

        InitializeScore();
        SetState(GameState.Briefing);
    }

    public void RevivePlayer()
    {
        if (!CanRevive) return;
        reviveUsed = true;

        // Воскресить (IsDead == true, обычный Heal() игнорирует мёртвых)
        HealthManager hm = playerController.GetComponent<HealthManager>();
        hm.Revive(50f);

        // Сбросить флаг смерти в PlayerState
        var ps = playerController.GetComponent<PlayerState>();
        if (ps != null) ps.isDead = false;
        playerController.defaultMovement.enabled = true;

        // Вернуть миссию в активный режим, сохранив таймер
        float savedTimer = missionTimer;
        SetState(GameState.InProgress);
        missionTimer = savedTimer;

        // Кратковременная неуязвимость
        StartCoroutine(InvisibleTime(hm, 3f));
    }

    private IEnumerator InvisibleTime(HealthManager hm, float time)
    {
        hm.canTakeDamage = false;
        yield return new WaitForSeconds(time);
        hm.canTakeDamage = true;
    }

    #endregion
}

#region Data Classes

/// <summary>
/// Данные миссии
/// </summary>
[Serializable]
public class MissionData
{
    public string missionId;
    public string missionName;
    public string briefingText;
    public Sprite missionImage;

    [Header("Objectives")]
    public List<ObjectiveData> objectives = new List<ObjectiveData>();

    [Header("Settings")]
    public bool hasTimeLimit = true;
    public float timeLimit = 1800f;
    public int maxCivilianCasualties = 3;
    public bool requireHostageRescue = false;
    public int totalHostages = 0;

    [Header("Suspects")]
    public int totalSuspects = 0;

    [Header("Rewards")]
    public int baseReward = 1000;
}

/// <summary>
/// Данные цели миссии
/// </summary>
[Serializable]
public class ObjectiveData
{
    public string objectiveId;
    public string description;
    public bool isRequired = true;
    public bool isCompleted = false;
    public ObjectiveType type;
}

public enum ObjectiveType
{
    EliminateAllThreats,
    RescueHostages,
    SecureEvidence,
    ArrestSuspect,
    ReachLocation,
    DefendArea,
    Custom
}

/// <summary>
/// Данные о гражданском
/// </summary>
[Serializable]
public class CivilianData
{
    public string id;
    public GameObject gameObject;
    public bool isHostage;
    public bool isRescued;
    public bool isDead;
}

/// <summary>
/// Данные о подозреваемом
/// </summary>
[Serializable]
public class SuspectData
{
    public string id;
    public GameObject gameObject;
    public SmartEnemyAI ai;
    public bool isArrested;
    public bool isNeutralized;
    public bool hasEscaped;
    public bool wasArmed;
    public bool wasHostile;
}

/// <summary>
/// Данные об улике
/// </summary>
[Serializable]
public class EvidenceData
{
    public string id;
    public string description;
    public GameObject gameObject;
    public bool isCollected;
}

/// <summary>
/// Счёт миссии
/// </summary>
[Serializable]
public class MissionScore
{
    // Статистика
    public int civiliansRescued;
    public int civiliansKilled;
    public int hostagesRescued;
    public int hostagesKilled;
    public int suspectsArrested;
    public int suspectsNeutralized;
    public int suspectsEscaped;
    public int evidenceCollected;
    public int objectivesCompleted;
    public float missionTime;

    // Очки
    public int totalScore;
    public int penalties;
    public int timeBonus;

    // Рейтинг
    public MissionRating rating;

    private List<ScoreEntry> scoreEntries = new List<ScoreEntry>();
    private List<PenaltyEntry> penaltyEntries = new List<PenaltyEntry>();

    public void AddScore(ScoreType type)
    {
        int points = GetScoreValue(type);
        totalScore += points;
        scoreEntries.Add(new ScoreEntry { type = type, points = points });
    }

    public void AddPenalty(PenaltyType type)
    {
        int points = GetPenaltyValue(type);
        penalties += points;
        totalScore -= points;
        penaltyEntries.Add(new PenaltyEntry { type = type, points = points });
    }

    public void CalculateRating()
    {
        int finalScore = totalScore + timeBonus;

        if (finalScore >= 5000)
            rating = MissionRating.S;
        else if (finalScore >= 4000)
            rating = MissionRating.A;
        else if (finalScore >= 3000)
            rating = MissionRating.B;
        else if (finalScore >= 2000)
            rating = MissionRating.C;
        else if (finalScore >= 1000)
            rating = MissionRating.D;
        else
            rating = MissionRating.F;
    }

    private int GetScoreValue(ScoreType type)
    {
        return type switch
        {
            ScoreType.CivilianRescued => 100,
            ScoreType.HostageRescued => 250,
            ScoreType.SuspectArrested => 200,
            ScoreType.SuspectNeutralizedHostile => 50,
            ScoreType.SuspectNeutralizedArmed => 25,
            ScoreType.EvidenceCollected => 75,
            ScoreType.ObjectiveCompleted => 500,
            _ => 0
        };
    }

    private int GetPenaltyValue(PenaltyType type)
    {
        return type switch
        {
            PenaltyType.CivilianKilled => 500,
            PenaltyType.CivilianKilledByPlayer => 1000,
            PenaltyType.HostageKilled => 750,
            PenaltyType.UnauthorizedUseOfForce => 300,
            PenaltyType.SuspectEscaped => 200,
            PenaltyType.OfficerDown => 250,
            _ => 0
        };
    }

    public List<ScoreEntry> GetScoreEntries() => scoreEntries;
    public List<PenaltyEntry> GetPenaltyEntries() => penaltyEntries;
}

public enum ScoreType
{
    CivilianRescued,
    HostageRescued,
    SuspectArrested,
    SuspectNeutralizedHostile,
    SuspectNeutralizedArmed,
    EvidenceCollected,
    ObjectiveCompleted
}

public enum PenaltyType
{
    CivilianKilled,
    CivilianKilledByPlayer,
    HostageKilled,
    UnauthorizedUseOfForce,
    SuspectEscaped,
    OfficerDown
}

public enum MissionRating
{
    S,  // 5000+
    A,  // 4000+
    B,  // 3000+
    C,  // 2000+
    D,  // 1000+
    F   // < 1000
}

[Serializable]
public struct ScoreEntry
{
    public ScoreType type;
    public int points;
}

[Serializable]
public struct PenaltyEntry
{
    public PenaltyType type;
    public int points;
}

#endregion

