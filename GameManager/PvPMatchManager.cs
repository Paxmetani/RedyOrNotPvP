using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// PvP Match Flow Manager — 6v6 bomb defuse/defend
/// Flow: Briefing (first-person cutscene) → InProgress → Results → Menu
///
/// Team A = player's team  (player + 5 bots, tag: "TeamA")
/// Team B = enemy team     (6 bots,           tag: "TeamB")
///
/// Each team starts with <teamRespawnCredits> (default 4) shared respawns.
/// Bomb is already placed; attackers must defuse it, defenders must prevent defusal.
/// Match length ≈ 8 minutes.
/// </summary>
public class PvPMatchManager : MonoBehaviour
{
    public static PvPMatchManager Instance { get; private set; }

    // ─── Match State ─────────────────────────────────────────────────────────

    public enum MatchState { None, Briefing, InProgress, MatchOver }

    [Header("State")]
    [SerializeField] private MatchState currentState = MatchState.None;
    public MatchState CurrentState => currentState;

    // ─── Match Settings ──────────────────────────────────────────────────────

    [Header("Match Settings")]
    [SerializeField] private float matchDuration = 480f;      // ~8 minutes
    [SerializeField] private int teamRespawnCredits = 4;      // shared per team
    [SerializeField] private float respawnDelay = 10f;
    [SerializeField] private string playerTeamTag = "TeamA";
    [SerializeField] private string enemyTeamTag  = "TeamB";

    // ─── Teams ───────────────────────────────────────────────────────────────

    [Header("Teams")]
    [SerializeField] private SmartEnemyAI[] teamABots;        // 5 bots on player's team
    [SerializeField] private SmartEnemyAI[] teamBBots;        // 6 enemy bots
    [SerializeField] private Transform[]    teamASpawnPoints;
    [SerializeField] private Transform[]    teamBSpawnPoints;
    [SerializeField] private PlayerController player;

    private int teamARespawnCredits;
    private int teamBRespawnCredits;

    // ─── Bomb Objective ──────────────────────────────────────────────────────

    [Header("Bomb Objective")]
    [SerializeField] private BombObjective bombObjective;
    /// <summary>
    /// When true  → player team attacks (tries to defuse).
    /// When false → player team defends (protects bomb from defusal).
    /// </summary>
    [SerializeField] private bool playerTeamAttacks = false;

    // ─── Score / Results ─────────────────────────────────────────────────────

    [Header("Score")]
    private float   matchTimer;
    private int     teamAKills;
    private int     teamBKills;
    private bool    playerWon;
    public  PvPMatchResults Results { get; private set; }

    // ─── Events ──────────────────────────────────────────────────────────────

    [Header("Events")]
    public UnityEvent                    OnMatchStart;
    public UnityEvent<PvPMatchResults>   OnMatchOver;
    public UnityEvent<int>               OnTeamARespawnCreditsChanged;
    public UnityEvent<int>               OnTeamBRespawnCreditsChanged;
    public UnityEvent                    OnPlayerDied;

    // ─── References ──────────────────────────────────────────────────────────

    [Header("References")]
    [SerializeField] private PvPBriefingController briefingController;

    // ─────────────────────────────────────────────────────────────────────────

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
    }

    private void Start()
    {
        if (player == null)
            player = FindFirstObjectByType<PlayerController>();

        if (bombObjective == null)
            bombObjective = FindFirstObjectByType<BombObjective>();

        SetupTeams();

        if (currentState == MatchState.None)
            EnterBriefing();
    }

    private void Update()
    {
        if (currentState != MatchState.InProgress) return;

        matchTimer += Time.deltaTime;

        if (matchTimer >= matchDuration)
        {
            // Time expired: defenders win (bomb was never defused)
            EndMatch(!playerTeamAttacks);
        }
    }

    // ─── Team Setup ──────────────────────────────────────────────────────────

    private void SetupTeams()
    {
        teamARespawnCredits = teamRespawnCredits;
        teamBRespawnCredits = teamRespawnCredits;

        // Configure Team A bots (player's side)
        if (teamABots != null)
        {
            foreach (var bot in teamABots)
            {
                if (bot == null) continue;
                ConfigureBotForPvP(bot, playerTeamTag, enemyTeamTag);
                SmartEnemyAI captured = bot;
                bot.Health.OnDeath.AddListener(() => OnBotDied(captured, playerTeamTag));
            }
        }

        // Configure Team B bots (enemy side)
        if (teamBBots != null)
        {
            foreach (var bot in teamBBots)
            {
                if (bot == null) continue;
                ConfigureBotForPvP(bot, enemyTeamTag, playerTeamTag);
                SmartEnemyAI captured = bot;
                bot.Health.OnDeath.AddListener(() => OnBotDied(captured, enemyTeamTag));
            }
        }

        // Configure player's HealthManager
        if (player != null)
        {
            var playerHM = player.GetComponent<HealthManager>();
            if (playerHM != null)
            {
                playerHM.teamTag = playerTeamTag;
                playerHM.OnDeath.AddListener(OnPlayerDied.Invoke);
                playerHM.OnDeath.AddListener(() => HandlePlayerDeath(playerHM));
            }
        }
    }

    private void ConfigureBotForPvP(SmartEnemyAI bot, string myTeam, string enemyTeam)
    {
        if (bot.Health != null)
            bot.Health.teamTag = myTeam;

        var perception = bot.GetComponent<AIPerceptionModule>();
        if (perception != null)
            perception.SetPvPMode(true, myTeam, enemyTeam);
    }

    // ─── Match Flow ──────────────────────────────────────────────────────────

    private void EnterBriefing()
    {
        currentState = MatchState.Briefing;

        if (briefingController != null)
            briefingController.PlayBriefing(OnBriefingComplete);
        else
            OnBriefingComplete();
    }

    private void OnBriefingComplete()
    {
        StartMatch();
    }

    private void StartMatch()
    {
        currentState = MatchState.InProgress;
        matchTimer    = 0f;
        teamAKills    = 0;
        teamBKills    = 0;

        if (bombObjective != null)
        {
            // Determine which team is "attacking" (needs to defuse)
            string attackerTag = playerTeamAttacks ? playerTeamTag : enemyTeamTag;
            bombObjective.Initialize(attackerTag);
            bombObjective.OnBombDefused.AddListener(OnBombDefused);
            bombObjective.OnBombExploded.AddListener(OnBombExploded);
        }

        OnMatchStart?.Invoke();
        Debug.Log("[PvPMatch] Match started!");
    }

    private void OnBombDefused()
    {
        // Attackers win (they defused it)
        EndMatch(playerTeamAttacks);
    }

    private void OnBombExploded()
    {
        // Defenders win (bomb went off)
        EndMatch(!playerTeamAttacks);
    }

    /// <summary>
    /// Ends the match and builds the results.
    /// </summary>
    public void EndMatch(bool playerTeamWon)
    {
        if (currentState != MatchState.InProgress) return;

        currentState = MatchState.MatchOver;
        playerWon    = playerTeamWon;

        Results = new PvPMatchResults
        {
            matchTime                = matchTimer,
            playerTeamWon            = playerTeamWon,
            teamAKills               = teamAKills,
            teamBKills               = teamBKills,
            teamARespawnCreditsUsed  = teamRespawnCredits - teamARespawnCredits,
            teamBRespawnCreditsUsed  = teamRespawnCredits - teamBRespawnCredits,
            efficiencyScore          = CalculateEfficiencyScore()
        };

        OnMatchOver?.Invoke(Results);
        Debug.Log($"[PvPMatch] Over! Player {(playerTeamWon ? "WON" : "LOST")}. " +
                  $"Score: {Results.efficiencyScore}  Time: {Results.FormattedTime}");
    }

    // ─── Death & Respawn ─────────────────────────────────────────────────────

    private void OnBotDied(SmartEnemyAI bot, string teamTag)
    {
        if (currentState != MatchState.InProgress) return;

        if (teamTag == playerTeamTag)
        {
            teamBKills++;
            if (teamARespawnCredits > 0)
            {
                teamARespawnCredits--;
                OnTeamARespawnCreditsChanged?.Invoke(teamARespawnCredits);
                StartCoroutine(RespawnBot(bot, playerTeamTag));
            }
            else
            {
                CheckElimWinCondition();
            }
        }
        else
        {
            teamAKills++;
            if (teamBRespawnCredits > 0)
            {
                teamBRespawnCredits--;
                OnTeamBRespawnCreditsChanged?.Invoke(teamBRespawnCredits);
                StartCoroutine(RespawnBot(bot, enemyTeamTag));
            }
            else
            {
                CheckElimWinCondition();
            }
        }
    }

    private void HandlePlayerDeath(HealthManager playerHM)
    {
        if (currentState != MatchState.InProgress) return;

        teamBKills++;

        if (teamARespawnCredits > 0)
        {
            teamARespawnCredits--;
            OnTeamARespawnCreditsChanged?.Invoke(teamARespawnCredits);
            StartCoroutine(RespawnPlayer(playerHM));
        }
        else
        {
            CheckElimWinCondition();
        }
    }

    private IEnumerator RespawnBot(SmartEnemyAI bot, string teamTag)
    {
        yield return new WaitForSeconds(respawnDelay);
        if (currentState != MatchState.InProgress) yield break;

        Transform spawnPoint = GetSpawnPoint(teamTag);
        if (spawnPoint != null)
            bot.transform.position = spawnPoint.position;

        bot.Health.Revive(bot.Health.maxHealth);
        bot.Blackboard.Clear();
        bot.Blackboard.SetBool(BlackboardKey.HasSurrendered, false);
        bot.enabled = true;
    }

    private IEnumerator RespawnPlayer(HealthManager playerHM)
    {
        yield return new WaitForSeconds(respawnDelay);
        if (currentState != MatchState.InProgress) yield break;

        Transform spawnPoint = GetSpawnPoint(playerTeamTag);
        if (spawnPoint != null && player != null)
            player.transform.position = spawnPoint.position;

        playerHM.Revive(playerHM.maxHealth);

        var ps = player?.GetComponent<PlayerState>();
        if (ps != null) ps.isDead = false;
    }

    private Transform GetSpawnPoint(string teamTag)
    {
        Transform[] points = (teamTag == playerTeamTag) ? teamASpawnPoints : teamBSpawnPoints;
        if (points == null || points.Length == 0) return null;
        return points[Random.Range(0, points.Length)];
    }

    private void CheckElimWinCondition()
    {
        bool teamAWiped = IsTeamWiped(playerTeamTag);
        bool teamBWiped = IsTeamWiped(enemyTeamTag);

        if (teamAWiped && !teamBWiped)
            EndMatch(false);   // enemy team wins
        else if (teamBWiped && !teamAWiped)
            EndMatch(true);    // player team wins
        else if (teamAWiped)
            EndMatch(false);   // both wiped → loss for player
    }

    private bool IsTeamWiped(string teamTag)
    {
        SmartEnemyAI[] bots = (teamTag == playerTeamTag) ? teamABots : teamBBots;

        if (bots != null)
        {
            foreach (var bot in bots)
            {
                if (bot != null && !bot.IsDead())
                    return false;
            }
        }

        // Also check the player for Team A
        if (teamTag == playerTeamTag)
        {
            var playerHM = player?.GetComponent<HealthManager>();
            if (playerHM != null && !playerHM.IsDead)
                return false;
        }

        return true;
    }

    // ─── Score ───────────────────────────────────────────────────────────────

    private int CalculateEfficiencyScore()
    {
        int score = 0;

        // Win bonus
        if (playerWon) score += 300;

        // Time bonus (faster win = better)
        if (playerWon)
        {
            float timeRatio = 1f - (matchTimer / matchDuration);
            score += Mathf.RoundToInt(timeRatio * 500f);
        }

        // Kill bonus
        score += teamAKills * 50;

        // Respawn efficiency (fewer credits burned = better)
        score += teamARespawnCredits * 75;

        // Objective bonus: bomb defused by player team
        if (bombObjective != null && bombObjective.IsDefused && playerTeamAttacks)
            score += 200;

        return Mathf.Max(0, score);
    }

    // ─── Public Helpers ──────────────────────────────────────────────────────

    /// <summary>Returns remaining time in seconds, or -1 if no limit.</summary>
    public float TimeRemaining => matchDuration - matchTimer;

    /// <summary>Player team's remaining respawn credits.</summary>
    public int PlayerTeamCredits => teamARespawnCredits;
}

// ─── Result Data ─────────────────────────────────────────────────────────────

/// <summary>
/// Holds all data shown on the post-match results screen.
/// </summary>
[System.Serializable]
public class PvPMatchResults
{
    public float matchTime;
    public bool  playerTeamWon;
    public int   teamAKills;
    public int   teamBKills;
    public int   teamARespawnCreditsUsed;
    public int   teamBRespawnCreditsUsed;
    public int   efficiencyScore;

    /// <summary>Time formatted as MM:SS.</summary>
    public string FormattedTime =>
        $"{Mathf.FloorToInt(matchTime / 60):00}:{(matchTime % 60):00}";
}
