using UnityEngine;

/// <summary>
/// Модуль принятия решения о сдаче
/// Враг ДУМАЕТ перед выбором:  сдаться или драться
/// </summary>
public class AISurrenderDecisionModule : MonoBehaviour
{
    [Header("Decision Timing")]
    [SerializeField] private float decisionDuration = 3f; // Как долго думает
    [SerializeField] private float fightDecisionBonus = 0.2f; // Бонус к решению драться

    [Header("Decision Weights")]
    [SerializeField, Range(0f, 1f)] private float healthWeight = 0.3f;
    [SerializeField, Range(0f, 1f)] private float stressWeight = 0.4f;
    [SerializeField, Range(0f, 1f)] private float moraleWeight = 0.3f;

    [Header("Debug")]
    [SerializeField] private bool showDebugLogs = false;

    private SmartEnemyAI core;
    private bool isDeciding = false;
    private float decisionStartTime = 0f;
    private bool decisionMade = false;

    public void Initialize(SmartEnemyAI coreAI)
    {
        core = coreAI;
    }

    public void UpdateModule()
    {
        if (!isDeciding) return;

        // Ждем окончания раздумья
        float elapsed = Time.time - decisionStartTime;

        if (elapsed >= decisionDuration && !decisionMade)
        {
            MakeDecision();
        }
    }

    /// <summary>
    /// Начать процесс принятия решения
    /// </summary>
    public void StartDecision()
    {
        if (isDeciding || core.IsDead()) return;

        isDeciding = true;
        decisionStartTime = Time.time;
        decisionMade = false;

        // Установить флаг в blackboard
        core.Blackboard.SetBool(BlackboardKey.IsDeciding, true);

        // НОВОЕ: Запустить анимацию раздумия
        core.Animation?.TriggerDeciding();

        // Остановить движение
        core.Agent.isStopped = true;

        // Опустить оружие (нейтральная поза)
        core.WeaponController?.LowerWeapon();

        if (showDebugLogs)
        {
            Debug.Log($"[Decision] {core.name} - Started decision (duration: {decisionDuration}s)");
        }
    }

    /// <summary>
    /// Принять финальное решение
    /// </summary>
    public void MakeDecision()
    {
        decisionMade = true;

        if (core.Psychology == null) return;

        // Получить вероятность сдачи из Psychology (0-1)
        float surrenderProbability = core.Psychology.GetSurrenderProbability();
        
        // Добавить случайность
        float randomFactor = Random.Range(0.8f, 1.2f);
        float finalChance = surrenderProbability * randomFactor;

        // Порог для сдачи (если probability > 0.5 = сдаться)
        bool decidedToSurrender = finalChance > 0.5f;

        if (showDebugLogs)
        {
            Debug.Log($"[Decision] {core.name} - Surrender Probability: {surrenderProbability:F2}, Final: {finalChance:F2} → {(decidedToSurrender ? "SURRENDER" : "FIGHT")}");
        }

        if (decidedToSurrender)
        {
            // СДАТЬСЯ
            core.Psychology.ForceSurrender();
        }
        else
        {
            // ДРАТЬСЯ
            core.Blackboard.SetBool(BlackboardKey.DecidedToFight, true);
            core.WeaponController?.RaiseWeapon();
            core.Agent.isStopped = false;
        }

        // Завершить раздумье
        isDeciding = false;
        core.Blackboard.SetBool(BlackboardKey.IsDeciding, false);
        
        // НОВОЕ: Остановить анимацию раздумия
        core.Animation?.StopDeciding();
    }

    /// <summary>
    /// Прервать раздумье (например если получил урон)
    /// </summary>
    public void InterruptDecision()
    {
        if (!isDeciding) return;

        // Автоматически решил драться
        core.Blackboard.SetBool(BlackboardKey.DecidedToFight, true);
        core.WeaponController?.RaiseWeapon();

        isDeciding = false;
        decisionMade = true;
        core.Blackboard.SetBool(BlackboardKey.IsDeciding, false);
        
        // НОВОЕ: Остановить анимацию раздумия
        core.Animation?.StopDeciding();

        if (showDebugLogs)
        {
            Debug.Log($"[Decision] {core.name} - INTERRUPTED → decided to FIGHT");
        }
    }

    public bool IsDeciding() => isDeciding;

    public bool HasDecidedToSurrender() => decisionMade && core.Psychology != null && core.Psychology.HasSurrendered();
}