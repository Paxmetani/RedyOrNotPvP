using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

// ═══════════════════════════════════════════════════════════════════════════
//  PvP MATCH CONTROLLER  (Symmetric team elimination + bomb timer)
//
//  Both teams act identically — no "attacker" vs "defender" distinction
//  in bot behaviour.  The bomb sits at the map centre and functions purely
//  as a match timer (pre-planted, no defuse mechanic).
//
//  Win conditions:
//    1. Eliminate the entire enemy team (no respawns remaining).
//    2. Bomb timer expires — the team with more surviving members wins;
//       if tied the team with more kills wins.
//
//  Teams spawn at opposite ends of a large map.
// ═══════════════════════════════════════════════════════════════════════════

public class PvPMatchController : MonoBehaviour
{
    public static PvPMatchController Instance { get; private set; }

    // ─── State ───────────────────────────────────────────────────────────

    public enum MatchPhase { WaitingToStart, InProgress, MatchOver }

    [Header("State")]
    [SerializeField] private MatchPhase phase = MatchPhase.WaitingToStart;
    public MatchPhase Phase => phase;

    // ─── Settings ────────────────────────────────────────────────────────

    [Header("Match Settings")]
    [SerializeField] private float matchDuration       = 480f;  // 8 minutes
    [SerializeField] private int   respawnCreditsPerTeam = 4;
    [SerializeField] private float respawnDelay        = 10f;

    [Header("Teams")]
    [SerializeField] private string teamATag = "TeamA";
    [SerializeField] private string teamBTag = "TeamB";

    [Header("Bots")]
    [SerializeField] private PvPBotController[] teamABots;
    [SerializeField] private PvPBotController[] teamBBots;

    [Header("Spawn Points")]
    [SerializeField] private Transform[] teamASpawns;   // one end of the map
    [SerializeField] private Transform[] teamBSpawns;   // opposite end

    [Header("Bomb (timer only)")]
    [SerializeField] private float bombTimer = 480f;    // same as match duration
    [SerializeField] private Transform bombTransform;   // visual reference at map centre

    // ─── Score ───────────────────────────────────────────────────────────

    private float matchClock = 0f;
    private int teamAKills, teamBKills;
    private int teamACredits, teamBCredits;

    // ─── Events ──────────────────────────────────────────────────────────

    [Header("Events")]
    public UnityEvent               OnMatchStarted;
    public UnityEvent<string>       OnMatchEnded;       // winning team tag
    public UnityEvent<float>        OnTimerUpdated;     // remaining seconds

    // ═════════════════════════════════════════════════════════════════════

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
    }

    private void Start()
    {
        SetupTeams();

        if (phase == MatchPhase.WaitingToStart)
            StartMatch();
    }

    private void Update()
    {
        if (phase != MatchPhase.InProgress) return;

        matchClock += Time.deltaTime;
        float remaining = bombTimer - matchClock;
        OnTimerUpdated?.Invoke(remaining);

        if (remaining <= 0f)
            OnTimerExpired();
    }

    // ═════════════════════════════════════════════════════════════════════
    //  SETUP
    // ═════════════════════════════════════════════════════════════════════

    private void SetupTeams()
    {
        teamACredits = respawnCreditsPerTeam;
        teamBCredits = respawnCreditsPerTeam;

        ConfigureTeamBots(teamABots, teamATag, teamBTag);
        ConfigureTeamBots(teamBBots, teamBTag, teamATag);
    }

    private void ConfigureTeamBots(PvPBotController[] bots, string myTag, string enemyTag)
    {
        if (bots == null) return;

        foreach (var bot in bots)
        {
            if (bot == null) continue;
            bot.ConfigureTeam(myTag, enemyTag);

            PvPBotController captured = bot;
            bot.Health.OnDeath.AddListener(() => OnBotDied(captured));
        }
    }

    // ═════════════════════════════════════════════════════════════════════
    //  MATCH FLOW
    // ═════════════════════════════════════════════════════════════════════

    public void StartMatch()
    {
        phase      = MatchPhase.InProgress;
        matchClock = 0f;
        teamAKills = 0;
        teamBKills = 0;

        OnMatchStarted?.Invoke();
        Debug.Log("[PvPMatch] Match started — bomb timer active!");
    }

    private void OnTimerExpired()
    {
        // Bomb "explodes" (timer end) — determine winner by survivors, then kills
        int aliveA = CountAlive(teamABots);
        int aliveB = CountAlive(teamBBots);

        if (aliveA > aliveB)
            EndMatch(teamATag);
        else if (aliveB > aliveA)
            EndMatch(teamBTag);
        else
        {
            // Tie in alive — compare kills
            EndMatch(teamAKills >= teamBKills ? teamATag : teamBTag);
        }
    }

    public void EndMatch(string winnerTag)
    {
        if (phase != MatchPhase.InProgress) return;

        phase = MatchPhase.MatchOver;
        OnMatchEnded?.Invoke(winnerTag);

        Debug.Log($"[PvPMatch] Over! Winner: {winnerTag}  " +
                  $"Kills A:{teamAKills} B:{teamBKills}  " +
                  $"Time: {FormatTime(matchClock)}");
    }

    // ═════════════════════════════════════════════════════════════════════
    //  DEATH & RESPAWN
    // ═════════════════════════════════════════════════════════════════════

    private void OnBotDied(PvPBotController bot)
    {
        if (phase != MatchPhase.InProgress) return;

        string team = bot.MyTeamTag;

        if (team == teamATag)
        {
            teamBKills++;
            if (teamACredits > 0)
            {
                teamACredits--;
                StartCoroutine(RespawnBot(bot, teamASpawns));
            }
            else
            {
                CheckElimination();
            }
        }
        else
        {
            teamAKills++;
            if (teamBCredits > 0)
            {
                teamBCredits--;
                StartCoroutine(RespawnBot(bot, teamBSpawns));
            }
            else
            {
                CheckElimination();
            }
        }
    }

    private IEnumerator RespawnBot(PvPBotController bot, Transform[] spawns)
    {
        yield return new WaitForSeconds(respawnDelay);
        if (phase != MatchPhase.InProgress) yield break;

        Transform sp = GetSpawn(spawns);
        if (sp != null)
            bot.transform.position = sp.position;

        bot.Health.Revive(bot.Health.maxHealth);
        bot.Blackboard.Clear();
        bot.enabled = true;
    }

    private void CheckElimination()
    {
        bool aWiped = IsTeamWiped(teamABots);
        bool bWiped = IsTeamWiped(teamBBots);

        if (bWiped && !aWiped) EndMatch(teamATag);
        else if (aWiped)       EndMatch(teamBTag);
    }

    // ═════════════════════════════════════════════════════════════════════
    //  HELPERS
    // ═════════════════════════════════════════════════════════════════════

    private bool IsTeamWiped(PvPBotController[] bots)
    {
        if (bots == null) return true;
        foreach (var b in bots)
        {
            if (b != null && !b.IsDead()) return false;
        }
        return true;
    }

    private int CountAlive(PvPBotController[] bots)
    {
        int c = 0;
        if (bots == null) return c;
        foreach (var b in bots)
        {
            if (b != null && !b.IsDead()) c++;
        }
        return c;
    }

    private Transform GetSpawn(Transform[] spawns)
    {
        if (spawns == null || spawns.Length == 0) return null;
        return spawns[Random.Range(0, spawns.Length)];
    }

    public float TimeRemaining => Mathf.Max(0f, bombTimer - matchClock);

    private static string FormatTime(float t) =>
        $"{Mathf.FloorToInt(t / 60):00}:{Mathf.FloorToInt(t % 60):00}";
}
