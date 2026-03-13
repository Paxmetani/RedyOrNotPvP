using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// ScriptableObject для создания миссий (Ready Or Not style)
/// Создать: ПКМ ? Create ? Tactical ? Mission Data
/// </summary>
[CreateAssetMenu(fileName = "New Mission", menuName = "Tactical/Mission Data")]
public class MissionDataSO : ScriptableObject
{
    [Header("Basic Info")]
    public string missionId;
    public string missionName;
    [TextArea(5, 10)]
    public string briefingText;
    public Sprite missionImage;
    public string sceneName;

    [Header("Objectives")]
    public List<ObjectiveDataSO> objectives = new List<ObjectiveDataSO>();

    [Header("Time Settings")]
    public bool hasTimeLimit = true;
    public float timeLimitMinutes = 30f;

    [Header("Failure Conditions")]
    public int maxCivilianCasualties = 3;
    public int maxOfficerCasualties = 1;

    [Header("Hostage Settings")]
    public bool requireHostageRescue = false;
    public int totalHostages = 0;
    public int minHostagesToRescue = 0;

    [Header("Suspect Settings")]
    public int totalSuspects = 0;
    public bool requireAllSuspectsDealt = false;

    [Header("Rewards")]
    public int baseReward = 1000;
    public int sRankBonus = 500;
    public int aRankBonus = 300;
    public int bRankBonus = 100;

    [Header("Difficulty")]
    public MissionDifficulty difficulty = MissionDifficulty.Normal;
    public float suspectAggressionMultiplier = 1f;
    public float suspectAccuracyMultiplier = 1f;

    /// <summary>
    /// Конвертировать в рантайм данные
    /// </summary>
    public MissionData ToRuntimeData()
    {
        var data = new MissionData
        {
            missionId = missionId,
            missionName = missionName,
            briefingText = briefingText,
            missionImage = missionImage,
            hasTimeLimit = hasTimeLimit,
            timeLimit = timeLimitMinutes * 60f,
            maxCivilianCasualties = maxCivilianCasualties,
            requireHostageRescue = requireHostageRescue,
            totalHostages = totalHostages,
            totalSuspects = totalSuspects,
            baseReward = baseReward
        };

        // Конвертировать цели
        foreach (var obj in objectives)
        {
            if (obj != null)
            {
                data.objectives.Add(new ObjectiveData
                {
                    objectiveId = obj.objectiveId,
                    description = obj.description,
                    isRequired = obj.isRequired,
                    type = obj.type,
                    isCompleted = false
                });
            }
        }

        return data;
    }
}

/// <summary>
/// ScriptableObject для целей миссии
/// </summary>
[CreateAssetMenu(fileName = "New Objective", menuName = "Tactical/Objective")]
public class ObjectiveDataSO : ScriptableObject
{
    public string objectiveId;
    [TextArea(2, 4)]
    public string description;
    public bool isRequired = true;
    public ObjectiveType type;

    [Header("Target (optional)")]
    public string targetId;
    public int targetCount = 1;
}

public enum MissionDifficulty
{
    Easy,
    Normal,
    Hard,
    Extreme
}
