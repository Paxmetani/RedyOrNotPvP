using UnityEngine;

/// <summary>
/// MODULE: Squad
/// ��������� ������, ����� �����������
/// </summary>
public class AISquadModule : MonoBehaviour
{
    [Header("Squad Settings")]
    [SerializeField] private float communicationRange = 50f;

    private SmartEnemyAI core;

    public void Initialize(SmartEnemyAI coreAI)
    {
        core = coreAI;
    }

    public void RegisterWithSquad()
    {
        if (AISquadManager.Instance != null)
        {
            AISquadManager.Instance.RegisterAI(core);
        }
    }

    public void UnregisterFromSquad()
    {
        if (AISquadManager.Instance != null)
        {
            AISquadManager.Instance.UnregisterAI(core);
        }
    }

    public void ShareThreatIntel(Vector3 threatPosition, bool confirmed)
    {
        if (AISquadManager.Instance != null)
        {
            AISquadManager.Instance.ShareThreatInformation(core, threatPosition, confirmed);
        }
    }

    /// <summary>
    /// НОВОЕ: Разослать Alert сигнал всей команде
    /// Команда перейдёт в режим защиты / подготовки засады
    /// </summary>
    public void BroadcastAlert(Vector3 threatPosition, SoundType soundType)
    {
        if (AISquadManager.Instance != null)
        {
            AISquadManager.Instance.BroadcastAlertState(core, threatPosition, soundType);
        }
    }

    /// <summary>
    /// Получить количество союзников в радиусе
    /// </summary>
    public int GetNearbyAllyCount(float radius)
    {
        if (AISquadManager.Instance == null) return 0;
        return AISquadManager.Instance.GetNearbyAllyCount(core.Transform.position, radius);
    }

    public void CallForBackup()
    {
        if (AISquadManager.Instance != null)
        {
            Vector3 position = core.Transform.position;
            AISquadManager.Instance.BroadcastBackupRequest(core, position);

            Debug.Log($"[Squad] {core.name} - Called for backup!");
        }
    }

    public void BroadcastFiring(Vector3 position)
    {
        // Notify GunshotNotifier
        var notifier = FindObjectOfType<GunshotNotifier>();
        if (notifier != null)
        {
            notifier.NotifySound(position, SoundType.Gunshot, 1f);
        }
    }

    // Called by squad manager
    public void ReceiveIntelligence(Vector3 threatPosition, bool confirmed)
    {
        // Не реагируем если уже видим подтвержденную угрозу
        var currentThreatLevel = core.Blackboard.Get(BlackboardKey.ThreatLevel, ThreatLevel.None);

        if (currentThreatLevel == ThreatLevel.Confirmed && confirmed == false)
        {
            // У нас более точная информация - игнорируем
            return;
        }

        // Обновляем информацию
        if (currentThreatLevel == ThreatLevel.None || (confirmed && currentThreatLevel == ThreatLevel.Suspected))
        {
            core.Blackboard.SetVector3(BlackboardKey.PredictedThreatPosition, threatPosition);
            core.Blackboard.SetFloat(BlackboardKey.LastSuspectedThreatTime, Time.time);
            core.Blackboard.Set(BlackboardKey.ThreatLevel, confirmed ? ThreatLevel.Confirmed : ThreatLevel.Suspected);
        }
    }

    public void OnAllyDeath(Vector3 position)
    {
        Debug.Log($"[Squad] {core.name} - Ally down at {position}!");

        // ��������������� �����������
        float distance = Vector3.Distance(core.Transform.position, position);
        core.Psychology?.OnAllyKilled(distance); // NEW

        // Investigate if no current threat
        if (core.Blackboard.Get(BlackboardKey.ThreatLevel, ThreatLevel.None) == ThreatLevel.None)
        {
            core.Blackboard.SetVector3(BlackboardKey.PredictedThreatPosition, position);
            core.Blackboard.SetFloat(BlackboardKey.LastSuspectedThreatTime, Time.time);
            core.Blackboard.Set(BlackboardKey.ThreatLevel, ThreatLevel.Suspected);
        }
    }
}