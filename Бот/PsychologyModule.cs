using UnityEngine;

/// <summary>
/// MODULE: Psychology
/// ��������� ��������, �������������� � ������� AI
/// ������� float �������� � ��������������
/// </summary>
public class AIPsychologyModule : MonoBehaviour
{
    #region State Values (0-100)

    [Header("Current State")]
    [SerializeField, Range(0f, 100f)] private float currentStress = 0f;
    [SerializeField, Range(0f, 100f)] private float currentDisorientation = 0f;
    [SerializeField, Range(0f, 100f)] private float currentMorale = 70f;

    #endregion

    #region Personality (������ �� ������� ��������)

    [Header("Personality Traits")]
    [SerializeField, Range(0f, 100f)] private float stressResistance = 50f; // �������������������
    [SerializeField, Range(0f, 100f)] private float baseMorale = 70f; // ������� ������
    [SerializeField, Range(0f, 100f)] private float courage = 60f; // ���������

    #endregion

    #region Thresholds (������ ��� ���������)

    [Header("Breaking Points")]
    [SerializeField] private float surrenderThreshold = 80f; // ����� ����� (������)
    [SerializeField] private float panicThreshold = 90f; // ����� ������
    [SerializeField] private float minimumMoraleToFight = 30f; // ����������� ������ ��� ���

    #endregion

    #region Decay Rates

    [Header("Recovery Rates")]
    [SerializeField] private float stressDecayRate = 5f; // � �������
    [SerializeField] private float disorientationDecayRate = 10f;
    [SerializeField] private float moraleRecoveryRate = 2f;

    #endregion

    [Header("Surrender Rules")]
    [SerializeField] private bool preventSurrenderInCombat = true; // NEW: ��������� ����� � ���
    [SerializeField] private float minDisorientationToSurrender = 80f; // NEW: ����������� �������������
    [SerializeField] private bool requireStunForCombatSurrender = true; // NEW:  ��������� ���������


    #region Debug

    [Header("Debug")]
    [SerializeField] private bool showDebugLogs = false;

    #endregion

    private SmartEnemyAI core;
    private bool hasSurrendered = false;

    public void Initialize(SmartEnemyAI coreAI)
    {
        core = coreAI;
        currentMorale = baseMorale;
    }

    public void UpdateModule()
    {
        // Natural decay
        DecayStress();
        DecayDisorientation();
        RecoverMorale();

        // Cross-influence
        ApplyCrossInfluence();

        // Check breaking points
        CheckPsychologicalState();
        
        // НОВОЕ: Проверить состояние подавления
        CheckSuppressionState();

        // Write to blackboard
        UpdateBlackboard();
    }

    #region Decay & Recovery
    
    /// <summary>
    /// НОВОЕ: Проверить и снять состояние подавления
    /// </summary>
    private void CheckSuppressionState()
    {
        bool isSuppressed = core.Blackboard.GetBool(BlackboardKey.IsSuppressed, false);
        
        if (isSuppressed)
        {
            float suppressionEndTime = core.Blackboard.GetFloat(BlackboardKey.SuppressionEndTime, 0f);
            
            if (Time.time > suppressionEndTime)
            {
                // Снять подавление
                core.Blackboard.SetBool(BlackboardKey.IsSuppressed, false);
                core.Animation?.StopSuppressed();
                
                if (showDebugLogs)
                    Debug.Log($"[Psychology] {core.name} - Suppression ended");
            }
        }
    }

    private void DecayStress()
    {
        if (currentStress > 0f)
        {
            // ������ ������ �� �������� �������� �������
            float moraleModifier = Mathf.Lerp(0.5f, 1.5f, currentMorale / 100f);
            currentStress -= stressDecayRate * moraleModifier * Time.deltaTime;
            currentStress = Mathf.Max(0f, currentStress);
        }
    }

    private void DecayDisorientation()
    {
        if (currentDisorientation > 0f)
        {
            currentDisorientation -= disorientationDecayRate * Time.deltaTime;
            currentDisorientation = Mathf.Max(0f, currentDisorientation);
        }
    }

    private void RecoverMorale()
    {
        // ������ ��������� � �������� ��������
        if (currentMorale < baseMorale)
        {
            currentMorale += moraleRecoveryRate * Time.deltaTime;
            currentMorale = Mathf.Min(baseMorale, currentMorale);
        }
    }

    #endregion

    #region Cross-Influence (�������������)

    private void ApplyCrossInfluence()
    {
        // 1. ������������� ������ �������� ������ (�� ������� �� ������)
        if (currentDisorientation > 30f)
        {
            float disorientationStress = (currentDisorientation - 30f) * 0.1f * Time.deltaTime;
            AddStress(disorientationStress, bypassMorale: true);
        }

        // 2. ������ ������ ��������� ������
        if (currentMorale < 40f)
        {
            float moralePenalty = (40f - currentMorale) * 0.05f * Time.deltaTime;
            currentStress += moralePenalty;
        }

        // 3. ������� ������ ������� ������ �������
        // (����������� � ������ AddStress)

        // Clamp values
        currentStress = Mathf.Clamp(currentStress, 0f, 100f);
        currentMorale = Mathf.Clamp(currentMorale, 0f, 100f);
        currentDisorientation = Mathf.Clamp(currentDisorientation, 0f, 100f);
    }

    #endregion

    #region Public API - Adding Values

    /// <summary>
    /// �������� ������ � ������ ������
    /// </summary>
    public void AddStress(float amount, bool bypassMorale = false)
    {
        float finalAmount = amount;

        if (!bypassMorale)
        {
            // ������ ������ �� ������
            float moraleModifier = Mathf.Lerp(1.5f, 0.5f, currentMorale / 100f);
            finalAmount *= moraleModifier;

            // �������������������
            float resistanceModifier = stressResistance / 100f;
            finalAmount *= (1f - resistanceModifier * 0.5f);
        }

        // ������:  ���������� ������� ��� ������� �������
        if (currentStress > 70f)
        {
            float diminishFactor = Mathf.InverseLerp(70f, 100f, currentStress);
            finalAmount *= Mathf.Lerp(1f, 0.2f, diminishFactor); // ��� 100 ������� - ������ 20% �������
        }

        currentStress += finalAmount;
        currentStress = Mathf.Clamp(currentStress, 0f, 100f);

        if (showDebugLogs && finalAmount > 5f)
        {
            Debug.Log($"[Psychology] {core.name} - Stress +{finalAmount:F1} (Total: {currentStress:F1})");
        }
    }

    /// <summary>
    /// �������� �������������
    /// </summary>
    public void AddDisorientation(float amount)
    {
        currentDisorientation += amount;
        currentDisorientation = Mathf.Clamp(currentDisorientation, 0f, 100f);

        if (showDebugLogs)
        {
            Debug.Log($"[Psychology] {core.name} - Disorientation +{amount:F1} (Total: {currentDisorientation:F1})");
        }
    }

    /// <summary>
    /// �������� ������ (����� ���� + ��� -)
    /// </summary>
    public void ModifyMorale(float delta)
    {
        currentMorale += delta;
        currentMorale = Mathf.Clamp(currentMorale, 0f, 100f);

        if (showDebugLogs && Mathf.Abs(delta) > 5f)
        {
            Debug.Log($"[Psychology] {core.name} - Morale {(delta > 0 ? "+" : "")}{delta:F1} (Total: {currentMorale:F1})");
        }
    }

    #endregion

    #region Situational Stress/Morale

    /// <summary>
    /// ������� ���� �����
    /// </summary>
    public void OnAllyKilled(float distance)
    {
        float proximityFactor = Mathf.Clamp01(1f - (distance / 20f)); // ����� = ����

        // ������ ������
        float moraleLoss = -15f * proximityFactor;
        ModifyMorale(moraleLoss);

        // ������ ������
        float stressGain = 20f * proximityFactor;
        AddStress(stressGain);

        if (showDebugLogs)
        {
            Debug.Log($"[Psychology] {core.name} - Ally killed nearby! Morale: {moraleLoss:F1}, Stress: +{stressGain:F1}");
        }
    }

    /// <summary>
    /// ������ ���� ��������
    /// </summary>
    public void OnSeeAllyCorpse()
    {
        ModifyMorale(-5f);
        AddStress(8f);
    }

    /// <summary>
    /// ������� ����
    /// </summary>
    public void OnTakeDamage(float damagePercent)
    {
        float stressAmount = 15f * damagePercent;
        AddStress(stressAmount);

        // ��� ������ HP ������ ������
        if (core.GetHealthPercentage() < 0.3f)
        {
            ModifyMorale(-10f);
        }
    }


    /// <summary>
    /// Подавлен огнём (пули рядом)
    /// </summary>
    public void OnSuppressed()
    {
        AddStress(12f);
        AddDisorientation(15f);
        
        // НОВОЕ: Установить состояние подавления
        core.Blackboard.SetBool(BlackboardKey.IsSuppressed, true);
        core.Blackboard.SetFloat(BlackboardKey.SuppressionEndTime, Time.time + 3f);
        
        // Запустить анимацию подавления если стресс высокий
        if (currentStress > 60f)
        {
            core.Animation?.TriggerSuppressed();
        }
    }

    /// <summary>
    /// Ослеплён / гранатой оглушён
    /// </summary>
    public void OnFlashbanged()
    {
        AddDisorientation(80f); // Сильная дезориентация
        AddStress(25f, bypassMorale: true); // Сильный стресс
        
        // НОВОЕ: Подавление + анимация
        core.Blackboard.SetBool(BlackboardKey.IsSuppressed, true);
        core.Blackboard.SetFloat(BlackboardKey.SuppressionEndTime, Time.time + 5f);
        core.Animation?.TriggerSuppressed();
    }

    /// <summary>
    /// ����� ����� (������, ���������� ������)
    /// </summary>
    public void OnPlayerPressure(float intensity)
    {
        AddStress(intensity);
    }

    #endregion

    #region State Checks

    private void CheckPsychologicalState()
    {
        if (hasSurrendered) return;

        if (core.SurrenderDecision == null) return;

        // Условия для начала раздумья
        bool highStress = currentStress > 75f;
        bool lowMorale = currentMorale < 40f;
        bool lowHealth = core.GetHealthPercentage() < 0.3f;
        bool disoriented = currentDisorientation > 60f;

        // Если несколько условий выполнены → начать раздумье
        int conditions = 0;
        if (highStress) conditions++;
        if (lowMorale) conditions++;
        if (lowHealth) conditions++;
        if (disoriented) conditions++;

        if (conditions >= 2 && !core.SurrenderDecision.IsDeciding())
        {
            core.SurrenderDecision.StartDecision();
        }

        // Проверить принято ли решение сдаться
        if (core.SurrenderDecision.HasDecidedToSurrender())
        {
            if (!hasSurrendered)
            {
                TriggerSurrender();
            }
        }
    }
    private bool ShouldSurrender()
    {
        // ����� �������: ���� � ��� - ����� ���������� �������
        bool inCombat = core.Blackboard.GetBool(BlackboardKey.InCombat, false);
        bool weaponRaised = core.Blackboard.GetBool(BlackboardKey.WeaponRaised, false);
        bool criticalHealth;
        bool isStunned;

        if (preventSurrenderInCombat && (inCombat || weaponRaised))
        {
            // � ��� ����� ������� ������ ����:
            // 1. ������ ��������������� (��������)
            // 2. ��� ����������� ��������
            // 3. ��� �������

            bool heavilyDisoriented = currentDisorientation >= minDisorientationToSurrender;
            criticalHealth = core.GetHealthPercentage() < 0.1f;
            isStunned = core.Blackboard.GetBool(BlackboardKey.IsStunned, false);

            if (requireStunForCombatSurrender)
            {
                // ��������� ��������� + ���-�� ���
                return isStunned && (heavilyDisoriented || criticalHealth);
            }
            else
            {
                // ����� �� �������
                return heavilyDisoriented || criticalHealth || isStunned;
            }
        }

        // ��� ��� - ������� ������
        criticalHealth = core.GetHealthPercentage() < 0.15f;
        isStunned = core.Blackboard.GetBool(BlackboardKey.IsStunned, false);

        return criticalHealth || isStunned;
    }
    // �������� ����� TriggerSurrender: 

    private void TriggerSurrender()
    {
        hasSurrendered = true;
        core.Blackboard.SetBool(BlackboardKey.HasSurrendered, true);

        // НОВОЕ: Явно вызвать анимацию сдачи
        core.Animation?.TriggerSurrender();

        // Stop combat
        core.Agent.isStopped = true;

        // Drop weapon
        core.WeaponController?.DropWeapon();

        if (showDebugLogs)
        {
            Debug.Log($"[Psychology] {core.name} - SURRENDERED! Stress: {currentStress:F0}, Morale: {currentMorale:F0}");
        }
    }

    // ForceSurrender для внешнего вызова:
    public void ForceSurrender()
    {
        if (hasSurrendered) return;

        hasSurrendered = true;
        core.Blackboard.SetBool(BlackboardKey.HasSurrendered, true);

        // НОВОЕ: Явно вызвать анимацию сдачи
        core.Animation?.TriggerSurrender();

        // Stop combat
        core.Agent.isStopped = true;

        // Drop weapon
        core.WeaponController?.DropWeapon();

        Debug.Log($"[Psychology] {core.name} - FORCED SURRENDER!");
    }

    /// <summary>
    /// Получить вероятность сдачи (0-1)
    /// Используется SurrenderDecision для принятия решения
    /// Зависит от стресса, здоровья, морали и дезориентации
    /// </summary>
    public float GetSurrenderProbability()
    {
        if (hasSurrendered) return 1f;

        float probability = 0f;

        // ====== ФАКТОРЫ СДАЧИ ======

        // 1. СТРЕСС (0-100 → 0-40% вероятности)
        probability += (currentStress / 100f) * 0.4f;

        // 2. ЗДОРОВЬЕ (0-1 → 0-30%)
        float healthPercent = core.GetHealthPercentage();
        probability += (1f - healthPercent) * 0.3f;

        // 3. МОРАЛЬ (низкая = выше вероятность, 0-100 → 0-20%)
        probability += ((100f - currentMorale) / 100f) * 0.2f;

        // 4. ДЕЗОРИЕНТАЦИЯ (0-100 → 0-20%)
        probability += (currentDisorientation / 100f) * 0.2f;

        // ====== КОНТЕКСТНЫЕ МОДИФИКАТОРЫ ======

        // В БОЮ = намного сложнее сдаться
        bool inCombat = core.Blackboard.GetBool(BlackboardKey.InCombat, false);
        if (inCombat && preventSurrenderInCombat)
        {
            probability *= 0.3f; // Уменьшить в 3 раза
        }

        // Если оружие поднято = ещё сложнее
        bool weaponRaised = core.Blackboard.GetBool(BlackboardKey.WeaponRaised, false);
        if (weaponRaised && preventSurrenderInCombat)
        {
            probability *= 0.4f;
        }

        // Смелость влияет на базовую вероятность (смелые менее склонны сдаваться)
        probability *= (1f - (courage / 100f) * 0.5f);

        return Mathf.Clamp01(probability);
    }

    public bool IsPanicking()
    {
        return currentStress >= panicThreshold;
    }

    public bool IsDisoriented()
    {
        return currentDisorientation > 50f;
    }

    public bool HasSurrendered()
    {
        return hasSurrendered;
    }

    /// <summary>
    /// НОВОЕ: Получить текущий стресс (0-100)
    /// </summary>
    public float GetStress() => currentStress;

    /// <summary>
    /// НОВОЕ: Получить текущую мораль (0-100)
    /// </summary>
    public float GetMorale() => currentMorale;

    /// <summary>
    /// НОВОЕ: Получить дезориентацию (0-100)
    /// </summary>
    public float GetDisorientation() => currentDisorientation;

    /// <summary>
    /// НОВОЕ: Проверить готов ли бот к бою
    /// </summary>
    public bool IsReadyToFight()
    {
        return currentMorale > minimumMoraleToFight && currentStress < panicThreshold && !hasSurrendered;
    }

    #endregion

    #region Blackboard Integration

    private void UpdateBlackboard()
    {
        //core.Blackboard.SetFloat("stress", currentStress);
        //core.Blackboard.SetFloat("disorientation", currentDisorientation);
        //core.Blackboard.SetFloat("morale", currentMorale);
       // core.Blackboard.SetBool("isPanicking", IsPanicking());
       // core.Blackboard.SetBool("isDisoriented", IsDisoriented());
    }

    #endregion

    #region Public Getters

    public float GetCourageStat() => courage;

    /// <summary>
    /// ���������, ��������� �������� ��������� ���������� ��������
    /// </summary>
    public float GetVoiceVulnerability()
    {
        float moraleInfluence = Mathf.InverseLerp(0f, 100f, currentMorale);      // ������� ������ = ������ ���������������
        float courageInfluence = Mathf.InverseLerp(100f, 0f, courage);           // ������ �������� = ������ ������
        float stressInfluence = Mathf.InverseLerp(0f, 100f, currentStress);      // ������� ������ ��������� ���������������
        float resistanceInfluence = Mathf.Lerp(1.15f, 0.7f, stressResistance / 100f); // ������� ������������ ����� ������

        float combined = (moraleInfluence * 0.5f) + (courageInfluence * 0.3f) + (stressInfluence * 0.2f);
        float result = combined * resistanceInfluence;
        return Mathf.Clamp(result, 0.15f, 1.25f);
    }

    #endregion
}