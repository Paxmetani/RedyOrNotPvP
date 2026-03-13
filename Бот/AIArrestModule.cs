using UnityEngine;

/// <summary>
/// MODULE:  Arrest
/// ������������ ������ � ���������� ������� ���������
/// </summary>
public class AIArrestModule : MonoBehaviour, IInteractable
{
    [Header("Arrest Settings")]
    [SerializeField] private float arrestDuration = 3f;
    [SerializeField] private float arrestRange = 2.5f;

    [Header("Debug")]
    [SerializeField] private bool showDebugLogs = true;

    private SmartEnemyAI core;
    private float arrestProgress = 0f;
    private bool isBeingArrested = false;
    private Transform arrester = null;

    public void Initialize(SmartEnemyAI coreAI)
    {
        core = coreAI;
    }

    #region IInteractable Implementation

    public void Interact()
    {
        // ����� ������������ ������ ���������
        if (core.Psychology == null || !core.Psychology.HasSurrendered())
        {
            if (showDebugLogs)
                Debug.Log($"[Arrest] {core.name} - Cannot arrest, not surrendered");
            return;
        }

        if (core.IsDead())
        {
            if (showDebugLogs)
                Debug.Log($"[Arrest] {core.name} - Cannot arrest, dead");
            return;
        }

        // ��� ���������
        if (core.Blackboard.GetBool(BlackboardKey.IsArrested, false))
        {
            if (showDebugLogs)
                Debug.Log($"[Arrest] {core.name} - Already arrested");
            return;
        }

        // ������ ������
        if (!isBeingArrested)
        {
            StartArrest();
        }

        // ����������� ������
        ContinueArrest();
    }

    public void StopInteract()
    {
        if (isBeingArrested && !core.Blackboard.GetBool(BlackboardKey.IsArrested, false))
        {
            CancelArrest();
        }
    }

    #endregion

    #region Arrest Process

    private void StartArrest()
    {
        isBeingArrested = true;
        arrestProgress = 0f;

        // Найти игрока
        var player = GameObject.FindGameObjectWithTag("Player");
        if (player != null)
        {
            arrester = player.transform;
        }

        // Blackboard: Установить флаги
        core.Blackboard.SetBool(BlackboardKey.IsBeingArrested, true);
        core.Blackboard.SetFloat(BlackboardKey.ArrestProgress, 0f);

        // НОВОЕ: Запустить анимацию начала ареста
        core.Animation?.TriggerBeingArrested();

        if (showDebugLogs)
            Debug.Log($"[Arrest] {core.name} - Started arrest");
    }

    private void ContinueArrest()
    {
        // �������� ���������
        if (arrester != null)
        {
            float distance = Vector3.Distance(core.Transform.position, arrester.position);
            if (distance > arrestRange)
            {
                if (showDebugLogs)
                    Debug.Log($"[Arrest] {core.name} - Too far ({distance:F1}m)");

                CancelArrest();
                return;
            }
        }

        // ��������
        arrestProgress += Time.deltaTime / arrestDuration;
        arrestProgress = Mathf.Clamp01(arrestProgress);

        // �����: ��������� blackboard ������ ����
        core.Blackboard.SetFloat(BlackboardKey.ArrestProgress, arrestProgress);

        if (showDebugLogs && Time.frameCount % 30 == 0)
        {
            Debug.Log($"[Arrest] {core.name} - Progress: {arrestProgress * 100f: F0}%");
        }

        // ����������
        if (arrestProgress >= 1f)
        {
            CompleteArrest();
        }
    }

    private void CancelArrest()
    {
        isBeingArrested = false;
        arrestProgress = 0f;
        arrester = null;

        core.Blackboard.SetBool(BlackboardKey.IsBeingArrested, false);
        core.Blackboard.SetFloat(BlackboardKey.ArrestProgress, 0f);

        if (showDebugLogs)
            Debug.Log($"[Arrest] {core.name} - Arrest cancelled");
    }

     public void CompleteArrest()
    {
        isBeingArrested = false;

        // Blackboard: Установить флаг arrested
        core.Blackboard.SetBool(BlackboardKey.IsArrested, true);
        core.Blackboard.SetBool(BlackboardKey.IsBeingArrested, false);
        core.Blackboard.SetFloat(BlackboardKey.ArrestProgress, 1f);
        
        // НОВОЕ: Запустить анимацию завершения ареста
        core.Animation?.TriggerArrested();

        // Отключить AI
        core.enabled = false;

        // Начислить очки
        if (arrester != null)
        {
            var playerState = arrester.GetComponent<PlayerState>();
            if (playerState != null)
            {
                playerState.score += 200;
                playerState.help++;
            }
        }

        if (showDebugLogs)
            Debug.Log($"[Arrest] {core.name} - ARRESTED SUCCESSFULLY!");
        MissionObjectiveTracker.Instance?.ReportSuspectArrested(core);
    }

    #endregion
}