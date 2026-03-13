using UnityEngine;

/// <summary>
/// Настройки взаимодействия для объектов
/// Поддерживает динамическое обновление текста для врагов
/// </summary>
public class InteractableSettings : MonoBehaviour
{
    [Header("Basic Info")]
    public string interactInfo = "Interact";

    [Header("Enemy Specific")]
    public bool isEnemy = false;
    public string defaultInfo = "Enemy";
    public string surrenderedInfo = "Arrest (Hold E)";
    public string arrestingInfo = "Arresting...";
    public string arrestedInfo = "Arrested";
    private SmartEnemyAI enemyAI;
    private bool wasInteracting = false;

    private void Awake()
    {
        enemyAI = GetComponent<SmartEnemyAI>();
        if (enemyAI != null)
        {
            isEnemy = true;
        }
    }

    private void Update()
    {
        if (!isEnemy || enemyAI == null) return;

        UpdateEnemyInteractInfo();
    }

    private void UpdateEnemyInteractInfo()
    {
        // Проверяем состояние психологии
        if (enemyAI.Psychology != null && enemyAI.Psychology.HasSurrendered())
        {
            // Проверяем процесс ареста
            bool isBeingArrested = enemyAI.Blackboard.GetBool(BlackboardKey.IsBeingArrested, false);
            bool isArrested = enemyAI.Blackboard.GetBool(BlackboardKey.IsArrested, false);

            if (isArrested)
            {
                interactInfo = arrestedInfo;
            }
            else if (isBeingArrested)
            {
                interactInfo = arrestingInfo;
            }
            else
            {
                interactInfo = surrenderedInfo;
            }
        }
        else if (enemyAI.IsDead())
        {
            interactInfo = "Dead";
        }
        else
        {
            // Показываем уровень стресса для отладки (можно убрать)
            if (enemyAI.Psychology != null)
            {
                float stress = enemyAI.Psychology.GetStress();
                interactInfo = $"Stress:  {stress:F0}%";
            }
            else
            {
                interactInfo = defaultInfo;
            }
        }
    }
}