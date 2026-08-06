using UnityEngine;

/// <summary>
/// How an objective is driven to completion. Each type has one intended caller:
///   Manual        - QuestManager.CompleteObjective (dialog end, cutscene, other script)
///   KillCount     - QuestManager.ReportKill (from GameManager.OnEnemyKilledWithId)
///   CollectItem   - QuestManager.ReportItemQuantity (from Inventory.OnAnyItemAdded)
///   ReachLocation - QuestManager.ReportLocationReached (from QuestLocationTrigger)
///   WinBattle     - QuestManager.ReportBattleResult (from a future SRPG BattleManager)
/// </summary>
public enum QuestObjectiveType
{
    Manual,
    KillCount,
    CollectItem,
    ReachLocation,
    WinBattle,
}

[System.Serializable]
public class QuestObjective
{
    [Tooltip("Unique id within this quest. Referenced by dialog actions, conditions, and prerequisites.")]
    public string objectiveId;

    [TextArea(1, 3)] public string description;

    public QuestObjectiveType type;

    [Tooltip("Meaning depends on Type: monster id (KillCount), item code/name (CollectItem), " +
             "location id (ReachLocation), battle id (WinBattle). Unused for Manual.")]
    public string targetId;

    [Tooltip("Used by KillCount / CollectItem.")]
    public int requiredCount = 1;

    [Tooltip("Objective ids that must be complete before this one can progress. Empty = active from quest start.")]
    public string[] prerequisiteObjectiveIds;

    [Tooltip("If true, this objective does not block quest completion on its own.")]
    public bool optional;

    [Tooltip("If true, completing this objective alone completes the whole quest (branch / alternate ending).")]
    public bool completesQuest;
}
