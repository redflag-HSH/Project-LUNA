using System;
using System.Collections.Generic;
using UnityEngine;

public class QuestManager : MonoBehaviour
{
    // ── Singleton ─────────────────────────────────────────────────────────────

    public static QuestManager Instance { get; private set; }

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        DontDestroyOnLoad(gameObject);
    }

    void OnEnable()
    {
        GameManager.OnEnemyKilledWithId += HandleEnemyKilled;
        Inventory.OnAnyItemAdded += HandleItemAdded;
    }

    void OnDisable()
    {
        GameManager.OnEnemyKilledWithId -= HandleEnemyKilled;
        Inventory.OnAnyItemAdded -= HandleItemAdded;
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    // ── Inspector ─────────────────────────────────────────────────────────────

    [Tooltip("All QuestData assets in the project. Required for save/load lookup.")]
    [SerializeField] QuestData[] _registry;

    [Tooltip("How many quests the player can have active at once.")]
    [SerializeField] int maxActiveQuests = 5;

    // ── Events ────────────────────────────────────────────────────────────────

    public static event Action OnQuestsChanged;

    // ── State ─────────────────────────────────────────────────────────────────

    const int MaxAvailableQuests = 10;

    class QuestProgress
    {
        public readonly HashSet<string> completedObjectiveIds = new();
        public readonly Dictionary<string, int> counters = new();
    }

    [Serializable]
    public class QuestProgressSave
    {
        public string questId;
        public List<string> completedObjectiveIds = new();
        public List<string> counterObjectiveIds = new();   // parallel to counterValues
        public List<int> counterValues = new();
    }

    readonly List<QuestData> _available = new();
    readonly List<QuestData> _active = new();
    readonly HashSet<string> _completed = new();
    readonly Dictionary<string, QuestProgress> _progress = new();   // questId → progress, active quests only

    public IReadOnlyList<QuestData> AvailableQuests => _available;
    public IReadOnlyList<QuestData> ActiveQuests => _active;
    public IEnumerable<string> CompletedQuestIds => _completed;

    // ── Public API ────────────────────────────────────────────────────────────

    public bool IsAvailable(QuestData quest) => _available.Contains(quest);
    public bool IsActive(QuestData quest) => _active.Contains(quest);
    public bool IsCompleted(QuestData quest) => _completed.Contains(quest.questId);

    public bool IsObjectiveComplete(QuestData quest, string objectiveId)
    {
        if (quest == null || string.IsNullOrEmpty(objectiveId)) return false;
        return _progress.TryGetValue(quest.questId, out var progress) &&
               progress.completedObjectiveIds.Contains(objectiveId);
    }

    /// <summary>True if the quest is active, this objective isn't complete yet, and every
    /// prerequisite objective (if any) is complete.</summary>
    public bool IsObjectiveActive(QuestData quest, string objectiveId)
    {
        if (quest == null || string.IsNullOrEmpty(objectiveId) || !_active.Contains(quest)) return false;
        if (IsObjectiveComplete(quest, objectiveId)) return false;

        var objective = FindObjective(quest, objectiveId);
        if (objective == null) return false;
        if (objective.prerequisiteObjectiveIds == null) return true;

        foreach (var prereq in objective.prerequisiteObjectiveIds)
            if (!IsObjectiveComplete(quest, prereq)) return false;

        return true;
    }

    /// <summary>Current progress count for a KillCount/CollectItem objective.</summary>
    public int GetObjectiveCount(QuestData quest, string objectiveId)
    {
        if (quest == null) return 0;
        return _progress.TryGetValue(quest.questId, out var progress) &&
               progress.counters.TryGetValue(objectiveId, out int count) ? count : 0;
    }

    /// <summary>Marks an objective complete (if it's currently active) and checks whether
    /// that finishes the whole quest. Called directly for Manual objectives (e.g. from dialog).</summary>
    public void CompleteObjective(QuestData quest, string objectiveId)
    {
        if (!IsObjectiveActive(quest, objectiveId)) return;

        GetOrCreateProgress(quest.questId).completedObjectiveIds.Add(objectiveId);
        OnQuestsChanged?.Invoke();

        CheckQuestCompletion(quest);
    }

    /// <summary>Drives KillCount objectives. Hook this up to enemy-death reporting with a monster id.</summary>
    public void ReportKill(string monsterId, int amount = 1)
    {
        if (string.IsNullOrEmpty(monsterId)) return;

        foreach (var quest in _active.ToArray())
            foreach (var objective in QuestObjectivesOf(quest))
            {
                if (objective.type != QuestObjectiveType.KillCount || objective.targetId != monsterId) continue;
                if (!IsObjectiveActive(quest, objective.objectiveId)) continue;

                var progress = GetOrCreateProgress(quest.questId);
                progress.counters.TryGetValue(objective.objectiveId, out int count);
                count += amount;
                progress.counters[objective.objectiveId] = count;

                if (count >= objective.requiredCount)
                    CompleteObjective(quest, objective.objectiveId);
            }

        OnQuestsChanged?.Invoke();
    }

    /// <summary>Drives CollectItem objectives. currentQuantity is the item's total held count,
    /// not a delta, so completed objectives never get out of sync with the inventory.</summary>
    public void ReportItemQuantity(string itemKey, int currentQuantity)
    {
        if (string.IsNullOrEmpty(itemKey)) return;

        foreach (var quest in _active.ToArray())
            foreach (var objective in QuestObjectivesOf(quest))
            {
                if (objective.type != QuestObjectiveType.CollectItem || objective.targetId != itemKey) continue;
                if (!IsObjectiveActive(quest, objective.objectiveId)) continue;

                GetOrCreateProgress(quest.questId).counters[objective.objectiveId] = currentQuantity;

                if (currentQuantity >= objective.requiredCount)
                    CompleteObjective(quest, objective.objectiveId);
            }

        OnQuestsChanged?.Invoke();
    }

    /// <summary>Drives ReachLocation objectives. Hook this up to a QuestLocationTrigger.</summary>
    public void ReportLocationReached(string locationId)
    {
        if (string.IsNullOrEmpty(locationId)) return;

        foreach (var quest in _active.ToArray())
            foreach (var objective in QuestObjectivesOf(quest))
            {
                if (objective.type != QuestObjectiveType.ReachLocation || objective.targetId != locationId) continue;
                if (IsObjectiveActive(quest, objective.objectiveId))
                    CompleteObjective(quest, objective.objectiveId);
            }
    }

    /// <summary>Drives WinBattle objectives. Call this from a future SRPG BattleManager when a
    /// tactical encounter concludes.</summary>
    public void ReportBattleResult(string battleId, bool won)
    {
        if (!won || string.IsNullOrEmpty(battleId)) return;

        foreach (var quest in _active.ToArray())
            foreach (var objective in QuestObjectivesOf(quest))
            {
                if (objective.type != QuestObjectiveType.WinBattle || objective.targetId != battleId) continue;
                if (IsObjectiveActive(quest, objective.objectiveId))
                    CompleteObjective(quest, objective.objectiveId);
            }
    }

    public void AddAvailableQuest(QuestData quest)
    {
        if (quest == null || _available.Contains(quest)) return;
        if (_active.Contains(quest)) return;
        // A completed quest can only be offered again if it is repeatable.
        if (!quest.repeatable && _completed.Contains(quest.questId)) return;
        if (_available.Count >= MaxAvailableQuests) return;
        _available.Add(quest);
        OnQuestsChanged?.Invoke();
    }

    public void RemoveAvailableQuest(QuestData quest)
    {
        if (_available.Remove(quest))
            OnQuestsChanged?.Invoke();
    }

    /// <returns>False if the quest could not be made active (already active/completed,
    /// or the player already has the maximum number of active quests).</returns>
    public bool AddQuest(QuestData quest)
    {
        if (quest == null || _active.Contains(quest)) return false;
        if (!quest.repeatable && _completed.Contains(quest.questId)) return false;
        if (_active.Count >= maxActiveQuests) return false;
        _available.Remove(quest);
        _active.Add(quest);
        _progress[quest.questId] = new QuestProgress();
        OnQuestsChanged?.Invoke();
        return true;
    }

    public void CompleteQuest(QuestData quest)
    {
        if (quest == null) return;
        _active.Remove(quest);
        _progress.Remove(quest.questId);
        _completed.Add(quest.questId);
        OnQuestsChanged?.Invoke();
    }

    public void RemoveQuest(QuestData quest)
    {
        if (_active.Remove(quest))
        {
            _progress.Remove(quest.questId);
            OnQuestsChanged?.Invoke();
        }
    }

    // ── Save / Load ───────────────────────────────────────────────────────────

    public List<QuestProgressSave> ExportProgress()
    {
        var result = new List<QuestProgressSave>();
        foreach (var quest in _active)
        {
            if (!_progress.TryGetValue(quest.questId, out var progress)) continue;

            var entry = new QuestProgressSave { questId = quest.questId };
            entry.completedObjectiveIds.AddRange(progress.completedObjectiveIds);
            foreach (var kv in progress.counters)
            {
                entry.counterObjectiveIds.Add(kv.Key);
                entry.counterValues.Add(kv.Value);
            }
            result.Add(entry);
        }
        return result;
    }

    public void LoadFromSave(List<string> availableIds, List<string> activeIds, List<string> completedIds,
        List<QuestProgressSave> activeProgress = null)
    {
        _available.Clear();
        _active.Clear();
        _completed.Clear();
        _progress.Clear();

        foreach (var id in completedIds)
            _completed.Add(id);

        foreach (var id in availableIds)
        {
            var quest = FindById(id);
            if (quest != null) _available.Add(quest);
            else Debug.LogWarning($"[QuestManager] No quest found for id '{id}' — skipped.");
        }

        foreach (var id in activeIds)
        {
            var quest = FindById(id);
            if (quest == null)
            {
                Debug.LogWarning($"[QuestManager] No quest found for id '{id}' — skipped.");
                continue;
            }
            _active.Add(quest);
            _progress[quest.questId] = new QuestProgress();
        }

        if (activeProgress != null)
        {
            foreach (var entry in activeProgress)
            {
                if (!_progress.TryGetValue(entry.questId, out var progress)) continue;

                foreach (var objectiveId in entry.completedObjectiveIds)
                    progress.completedObjectiveIds.Add(objectiveId);

                int pairCount = Mathf.Min(entry.counterObjectiveIds.Count, entry.counterValues.Count);
                for (int i = 0; i < pairCount; i++)
                    progress.counters[entry.counterObjectiveIds[i]] = entry.counterValues[i];
            }
        }

        OnQuestsChanged?.Invoke();
    }

    // ── Internal ──────────────────────────────────────────────────────────────

    void HandleEnemyKilled(string monsterId) => ReportKill(monsterId);

    void HandleItemAdded(Inventory.InventorySlot slot)
    {
        string key = slot.itemCode > 0 ? slot.itemCode.ToString() : slot.itemName;
        ReportItemQuantity(key, slot.quantity);
    }

    void CheckQuestCompletion(QuestData quest)
    {
        if (quest.objectives == null || quest.objectives.Length == 0) return;
        if (!_progress.TryGetValue(quest.questId, out var progress)) return;

        bool anyBranchComplete = false;
        bool allRequiredComplete = true;

        foreach (var objective in quest.objectives)
        {
            bool complete = progress.completedObjectiveIds.Contains(objective.objectiveId);
            if (complete && objective.completesQuest) anyBranchComplete = true;
            if (!complete && !objective.optional) allRequiredComplete = false;
        }

        if (anyBranchComplete || allRequiredComplete)
            CompleteQuest(quest);
    }

    QuestProgress GetOrCreateProgress(string questId)
    {
        if (!_progress.TryGetValue(questId, out var progress))
        {
            progress = new QuestProgress();
            _progress[questId] = progress;
        }
        return progress;
    }

    static IEnumerable<QuestObjective> QuestObjectivesOf(QuestData quest) =>
        (IEnumerable<QuestObjective>)quest.objectives ?? Array.Empty<QuestObjective>();

    static QuestObjective FindObjective(QuestData quest, string objectiveId)
    {
        if (quest.objectives == null) return null;
        foreach (var objective in quest.objectives)
            if (objective.objectiveId == objectiveId) return objective;
        return null;
    }

    QuestData FindById(string id)
    {
        if (_registry == null) return null;
        foreach (var q in _registry)
            if (q != null && q.questId == id) return q;
        return null;
    }
}
