using UnityEngine;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// Глобальный менеджер команд AI
/// </summary>
public class AISquadManager : MonoBehaviour
{
    public static AISquadManager Instance { get; private set; }

    [Header("Settings")]
    [SerializeField] private float communicationRange = 50f;
    [SerializeField] private float communicationInterval = 0.5f;

    private List<SmartEnemyAI> registeredAI = new List<SmartEnemyAI>();
    private float nextCommunicationTime = 0f;

    private void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
        }
        else
        {
            Destroy(gameObject);
        }
    }

    private void Update()
    {
        if (Time.time >= nextCommunicationTime)
        {
            nextCommunicationTime = Time.time + communicationInterval;
            ProcessCommunication();
        }
    }

    public void RegisterAI(SmartEnemyAI ai)
    {
        if (!registeredAI.Contains(ai))
        {
            registeredAI.Add(ai);
            Debug.Log($"[SquadManager] Registered {ai.name}.  Total: {registeredAI.Count}");
        }
    }

    public void UnregisterAI(SmartEnemyAI ai)
    {
        registeredAI.Remove(ai);

        // Notify others
        BroadcastAllyDeath(ai);

        Debug.Log($"[SquadManager] Unregistered {ai.name}.  Remaining: {registeredAI.Count}");

        // In PvP mode PvPMatchManager tracks eliminations itself; skip campaign extraction.
        if (PvPMatchManager.Instance != null) return;

        if (registeredAI.Count <= 0)
        {
            Debug.Log("[SquadManager] No more registered AI.");
            GameManagerTactical.Instance?.RequestExtraction();
        }
    }

    private void ProcessCommunication()
    {
        foreach (var ai in registeredAI.ToList())
        {
            if (ai == null || ai.IsDead()) continue;

            // Share information with nearby
            ShareWithNearby(ai);
        }
    }

    private void ShareWithNearby(SmartEnemyAI sender)
    {
        if (!sender.Blackboard.Has(BlackboardKey.CurrentThreat)) return;

        Vector3 threatPos = sender.Blackboard.GetVector3(BlackboardKey.LastKnownThreatPosition);
        bool confirmed = sender.Blackboard.GetTransform(BlackboardKey.CurrentThreat) != null;

        var nearby = GetNearbyAI(sender, communicationRange);

        foreach (var ally in nearby)
        {
            ally.Squad?.ReceiveIntelligence(threatPos, confirmed);
        }
    }

    public void ShareThreatInformation(SmartEnemyAI sender, Vector3 position, bool confirmed)
    {
        var nearby = GetNearbyAI(sender, communicationRange);

        foreach (var ally in nearby)
        {
            ally.Squad?.ReceiveIntelligence(position, confirmed);
        }
    }

    public void BroadcastBackupRequest(SmartEnemyAI requester, Vector3 position)
    {
        var nearby = GetNearbyAI(requester, communicationRange);

        foreach (var ally in nearby)
        {
            ally.Blackboard.SetVector3(BlackboardKey.BackupRequestPosition, position);
        }

        Debug.Log($"[SquadManager] {requester.name} backup request to {nearby.Count} allies");
    }

    /// <summary>
    /// НОВОЕ: Разослать Alert сигнал всей команде
    /// Активирует режим защиты и подготовки засады
    /// </summary>
    public void BroadcastAlertState(SmartEnemyAI sender, Vector3 threatPosition, SoundType soundType)
    {
        var nearby = GetNearbyAI(sender, communicationRange);

        foreach (var ally in nearby)
        {
            if (ally == null || ally.IsDead()) continue;

            // Активировать Alert State
            ally.Blackboard.SetBool(BlackboardKey.IsAlert, true);
            ally.Blackboard.SetVector3(BlackboardKey.ThreatOrigin, threatPosition);
            ally.Blackboard.Set(BlackboardKey.AlertSoundType, soundType);
            ally.Blackboard.SetFloat(BlackboardKey.AlertStartTime, Time.time);

            if (ally.Perception != null)
            {
                // Уведомить perception модуль
                ally.Perception.OnHearSound(threatPosition, soundType, 1f);
            }
        }

        Debug.Log($"[SquadManager] ALERT BROADCAST from {sender.name} to {nearby.Count} allies at {threatPosition}");
    }

    /// <summary>
    /// Получить количество союзников в радиусе
    /// </summary>
    public int GetNearbyAllyCount(Vector3 position, float radius)
    {
        int count = 0;
        foreach (var ai in registeredAI)
        {
            if (ai == null || ai.IsDead()) continue;

            float distance = Vector3.Distance(position, ai.Transform.position);
            if (distance <= radius)
            {
                count++;
            }
        }
        return count;
    }


    private void BroadcastAllyDeath(SmartEnemyAI deadAI)
    {
        Vector3 deathPosition = deadAI.Transform.position;

        var nearby = GetNearbyAI(deadAI, communicationRange);

        foreach (var ally in nearby)
        {
            ally.Squad?.OnAllyDeath(deathPosition);
        }
    }

    private List<SmartEnemyAI> GetNearbyAI(SmartEnemyAI origin, float range)
    {
        List<SmartEnemyAI> nearby = new List<SmartEnemyAI>();

        foreach (var ai in registeredAI)
        {
            if (ai == null || ai == origin || ai.IsDead()) continue;

            // In PvP mode only share intel with same-team members
            if (origin.Health != null && ai.Health != null &&
                origin.Health.TeamTag != ai.Health.TeamTag) continue;

            float distance = Vector3.Distance(origin.Transform.position, ai.Transform.position);
            if (distance <= range)
            {
                nearby.Add(ai);
            }
        }

        return nearby;
    }

    /// <summary>
    /// Returns a snapshot of all currently registered AI.
    /// Used by AIPerceptionModule in PvP mode to iterate potential targets.
    /// </summary>
    public IReadOnlyList<SmartEnemyAI> GetAllRegisteredAI() => registeredAI;

    private void OnDrawGizmosSelected()
    {
        if (registeredAI.Count == 0) return;

        Gizmos.color = Color.green;

        foreach (var ai in registeredAI)
        {
            if (ai == null) continue;

            var nearby = GetNearbyAI(ai, communicationRange);
            foreach (var ally in nearby)
            {
                Gizmos.DrawLine(
                    ai.Transform.position + Vector3.up,
                    ally.Transform.position + Vector3.up
                );
            }
        }
    }
}