using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// THINKING LAYER - Тактические решения
/// УЛУЧШЕНО: Психология влияет на все решения
/// </summary>
public class AIUtilityBrain : MonoBehaviour
{
    [Header("Base Personality")]
    [SerializeField, Range(0f, 1f)] private float baseAggression = 0.5f;
    [SerializeField, Range(0f, 1f)] private float baseCaution = 0.5f;
    [SerializeField, Range(0f, 1f)] private float training = 0.6f;

    [Header("Tactical Behavior")]
    [SerializeField] private float ambushChance = 0.7f;
    [SerializeField] private bool preferDefensivePosition = true;

    [Header("Debug")]
    [SerializeField] private bool showUtilityScores = false;

    private SmartEnemyAI core;
    private Dictionary<AIAction, float> lastScores = new Dictionary<AIAction, float>();

    // Эффективные значения (модифицированные психологией)
    private float aggression;
    private float caution;

    // Тактические действия
    private List<AIAction> tacticalActions = new List<AIAction>();

    public void Initialize(SmartEnemyAI coreAI)
    {
        core = coreAI;
        
        aggression = baseAggression;
        caution = baseCaution;

        tacticalActions.Add(AIAction.Patrol);
        tacticalActions.Add(AIAction.HoldPosition);
        tacticalActions.Add(AIAction.Ambush);
        tacticalActions.Add(AIAction.TacticalAdvance);
        tacticalActions.Add(AIAction.Flank);
        tacticalActions.Add(AIAction.Retreat);
        tacticalActions.Add(AIAction.InvestigateThreat);
    }

    /// <summary>
    /// НОВОЕ: Обновить параметры на основе психологии
    /// </summary>
    private void UpdatePsychologyInfluence()
    {
        if (core.Psychology == null) return;

        float stress = core.Psychology.GetStress() / 100f;
        float morale = core.Psychology.GetMorale() / 100f;
        
        // Высокий стресс = меньше агрессии
        aggression = baseAggression * morale * (1f - stress * 0.5f);
        
        // Низкая мораль = больше осторожности
        caution = baseCaution + (1f - morale) * 0.4f + stress * 0.3f;
        
        aggression = Mathf.Clamp01(aggression);
        caution = Mathf.Clamp01(caution);
    }

    public AIAction EvaluateActions()
    {
        UpdatePsychologyInfluence();
        
        lastScores.Clear();

        AIAction bestAction = AIAction.HoldPosition;
        float bestUtility = 0f;

        foreach (var action in tacticalActions)
        {
            float utility = CalculateUtility(action);
            lastScores[action] = utility;

            if (utility > bestUtility)
            {
                bestUtility = utility;
                bestAction = action;
            }
        }

        lastAction = bestAction;
        return bestAction;
    }

    private AIAction lastAction = AIAction.Patrol;

    /// <summary>
    /// Получить последнее вычисленное действие
    /// </summary>
    public AIAction GetLastAction() => lastAction;

    private float CalculateUtility(AIAction action)
    {
        // ========== ALERT STATE PRIORITY ==========
        // Если враги услышали звук - Alert State имеет приоритет
        bool isAlert = core.Blackboard.GetBool(BlackboardKey.IsAlert, false);
        if (isAlert)
        {
            // В Alert State - должны подготавливать засаду
            switch (action)
            {
                case AIAction.Ambush:
                    return 1.0f; // Максимальный приоритет!
                case AIAction.HoldPosition:
                    return 0.8f; // Вторичный
                case AIAction.Patrol:
                case AIAction.TacticalAdvance:
                case AIAction.Flank:
                    return 0.0f; // Запрещено в Alert State
                default:
                    return CalculateUtility_Normal(action);
            }
        }

        // ========== НОРМАЛЬНЫЙ РЕЖИМ ==========
        return CalculateUtility_Normal(action);
    }

    private float CalculateUtility_Normal(AIAction action)
    {
        switch (action)
        {
            case AIAction.Patrol: return Utility_Patrol();
            case AIAction.HoldPosition: return Utility_HoldPosition();
            case AIAction.Ambush: return Utility_Ambush();
            case AIAction.TacticalAdvance: return Utility_TacticalAdvance();
            case AIAction.Flank: return Utility_Flank();
            case AIAction.Retreat: return Utility_Retreat();
            case AIAction.InvestigateThreat: return Utility_InvestigateThreat();
            default: return 0f;
        }
    }

    #region Tactical Utilities

    private float Utility_Patrol()
    {
        var threatLevel = core.Blackboard.Get(BlackboardKey.ThreatLevel, ThreatLevel.None);
        
        // УЛУЧШЕНО: Не патрулировать если есть угроза или высокий стресс
        if (threatLevel != ThreatLevel.None) return 0f;
        if (core.Psychology != null && core.Psychology.GetStress() > 30f) return 0f;
        
        // Предпочитать оставаться на месте если включена оборона
        if (preferDefensivePosition) return 0.2f;

        return 0.4f;
    }

    private float Utility_HoldPosition()
    {
        var threatLevel = core.Blackboard.Get(BlackboardKey.ThreatLevel, ThreatLevel.Suspected);
        
        // УЛУЧШЕНО: Высокий приоритет держать позицию при угрозе
        if (threatLevel == ThreatLevel.Suspected || threatLevel == ThreatLevel.Confirmed)
        {
            float utility = 0.6f + caution * 0.3f;
            
            // Ещё выше если предпочитаем оборону
            if (preferDefensivePosition) utility += 0.2f;
            
            return Mathf.Clamp01(utility);
        }

        float timeSinceSound = Time.time - core.Blackboard.GetFloat(BlackboardKey.LastSuspectedThreatTime, -999f);
        if (timeSinceSound > 10f) return 0.3f;

        return ambushChance * 0.8f;
    }

    private float Utility_Ambush()
    {
        // ========== ALERT STATE AMBUSH ==========
        bool isAlert = core.Blackboard.GetBool(BlackboardKey.IsAlert, false);
        if (isAlert)
        {
            // В Alert State - МАКСИМУМ для засады!
            float timeSinceAlert = Time.time - core.Blackboard.GetFloat(BlackboardKey.AlertStartTime, -999f);
            
            // Если недавно услышали угрозу - срочно готовить засаду
            if (timeSinceAlert < 30f) // 30 сек окна для подготовки
            {
                float ambushUtility = ambushChance;
                ambushUtility += training * 0.3f; // Обученные бойцы лучше готовят засаду
                
                // Бонус если есть союзники рядом
                if (core.Squad != null && core.Squad.GetNearbyAllyCount(20f) > 0)
                {
                    ambushUtility += 0.2f;
                }
                
                return Mathf.Clamp01(ambushUtility);
            }
            
            // Если давно не было звуков - Alert становится слабее
            return Mathf.Clamp01(ambushChance * (1f - timeSinceAlert / 30f));
        }

        // ========== НОРМАЛЬНАЯ ЗАСАДА (НЕ Alert) ==========
        var threatLevel = core.Blackboard.Get(BlackboardKey.ThreatLevel, ThreatLevel.None);
        if (threatLevel != ThreatLevel.Suspected) return 0f;

        float normalUtility = ambushChance;
        normalUtility += training * 0.2f;

        return Mathf.Clamp01(normalUtility);
    }

    private float Utility_TacticalAdvance()
    {
        // Только если есть подтвержденная угроза но нет LOS
        Transform threat = core.Blackboard.GetTransform(BlackboardKey.CurrentThreat);
        if (threat == null) return 0f;

        bool hasLOS = core.LineOfSight != null && core.LineOfSight.HasLineOfSight(threat);
        if (hasLOS) return 0f; // Если видим - Reactive Layer уже стреляет

        return aggression * 0.6f + training * 0.3f;
    }

    private float Utility_Flank()
    {
        Transform threat = core.Blackboard.GetTransform(BlackboardKey.CurrentThreat);
        if (threat == null) return 0f;

        bool hasLOS = core.LineOfSight != null && core.LineOfSight.HasLineOfSight(threat);
        if (hasLOS) return 0f;

        return training * 0.7f + aggression * 0.2f;
    }

    private float Utility_Retreat()
    {
        float healthPercent = core.GetHealthPercentage();

        // УЛУЧШЕНО: Отступать при низком здоровье ИЛИ высоком стрессе ИЛИ низкой морали
        float utility = 0f;
        
        // Низкое здоровье
        if (healthPercent < 0.5f)
        {
            utility += (1f - healthPercent) * 0.5f;
        }
        
        // Высокий стресс
        if (core.Psychology != null)
        {
            float stress = core.Psychology.GetStress();
            float morale = core.Psychology.GetMorale();
            
            if (stress > 50f)
            {
                utility += (stress / 100f) * 0.4f;
            }
            
            // Низкая мораль
            if (morale < 40f)
            {
                utility += ((100f - morale) / 100f) * 0.3f;
            }
        }
        
        // Осторожность влияет
        utility += caution * 0.2f;

        return Mathf.Clamp01(utility);
    }

    private float Utility_InvestigateThreat()
    {
        var threatLevel = core.Blackboard.Get(BlackboardKey.ThreatLevel, ThreatLevel.None);
        if (threatLevel != ThreatLevel.Suspected) return 0f;

        return 0.6f + aggression * 0.2f;
    }

    #endregion

    public bool CanSurrender()
    {
        // Логика сдачи
        bool isStunned = core.Blackboard.GetBool(BlackboardKey.IsStunned, false);
        bool criticalHealth = core.GetHealthPercentage() < 0.1f;

        return isStunned || criticalHealth;
    }
}

public enum AIAction
{
    // Tactical only
    Patrol,
    HoldPosition,
    Ambush,
    TacticalAdvance,
    Flank,
    Retreat,
    InvestigateThreat,

    // Combat actions removed (handled by Reactive Layer)
}

public enum ThreatLevel
{
    None,
    Suspected,
    Confirmed
}