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

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    // ── Inspector ─────────────────────────────────────────────────────────────

    [Tooltip("All QuestData assets in the project. Required for save/load lookup.")]
    [SerializeField] QuestData[] _registry;

    // ── Events ────────────────────────────────────────────────────────────────

    public static event Action OnQuestsChanged;

    // ── State ─────────────────────────────────────────────────────────────────

    const int MaxAvailableQuests = 10;

    readonly List<QuestData> _available = new();
    readonly List<QuestData> _active = new();
    readonly HashSet<string> _completed = new();
    readonly Dictionary<string, QuestStage> _stages = new();   // questId → stage, active quests only

    public IReadOnlyList<QuestData> AvailableQuests => _available;
    public IReadOnlyList<QuestData> ActiveQuests => _active;
    public IEnumerable<string> CompletedQuestIds => _completed;

    // ── Public API ────────────────────────────────────────────────────────────

    public bool IsAvailable(QuestData quest) => _available.Contains(quest);
    public bool IsActive(QuestData quest) => _active.Contains(quest);
    public bool IsCompleted(QuestData quest) => _completed.Contains(quest.questId);

    /// <summary>Stage of an active quest. Returns false if the quest is not active.</summary>
    public bool TryGetStage(QuestData quest, out QuestStage stage)
    {
        stage = QuestStage.TalkToClient;
        return quest != null && _stages.TryGetValue(quest.questId, out stage);
    }

    public bool IsAtStage(QuestData quest, QuestStage stage) =>
        TryGetStage(quest, out var s) && s == stage;

    /// <summary>
    /// Moves an active quest to its next stage:
    /// TalkToClient → InProgress → ReportToClient → TalkToPygmalion → completed.
    /// </summary>
    public void AdvanceStage(QuestData quest)
    {
        if (quest == null || !_stages.TryGetValue(quest.questId, out var stage)) return;

        if (stage == QuestStage.TalkToPygmalion)
        {
            CompleteQuest(quest);
            return;
        }

        _stages[quest.questId] = stage + 1;
        OnQuestsChanged?.Invoke();
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

    public void AddQuest(QuestData quest)
    {
        if (quest == null || _active.Contains(quest)) return;
        if (!quest.repeatable && _completed.Contains(quest.questId)) return;
        _available.Remove(quest);
        _active.Add(quest);
        _stages[quest.questId] = QuestStage.TalkToClient;
        OnQuestsChanged?.Invoke();
    }

    public void CompleteQuest(QuestData quest)
    {
        if (quest == null) return;
        _active.Remove(quest);
        _stages.Remove(quest.questId);
        _completed.Add(quest.questId);
        OnQuestsChanged?.Invoke();
    }

    public void RemoveQuest(QuestData quest)
    {
        if (_active.Remove(quest))
        {
            _stages.Remove(quest.questId);
            OnQuestsChanged?.Invoke();
        }
    }

    // ── Save / Load ───────────────────────────────────────────────────────────

    /// <param name="activeStages">Stage per entry of <paramref name="activeIds"/> (same order).
    /// May be null/shorter (old saves) — missing entries default to TalkToClient.</param>
    public void LoadFromSave(List<string> availableIds, List<string> activeIds, List<string> completedIds, List<int> activeStages = null)
    {
        _available.Clear();
        _active.Clear();
        _completed.Clear();
        _stages.Clear();

        foreach (var id in completedIds)
            _completed.Add(id);

        foreach (var id in availableIds)
        {
            var quest = FindById(id);
            if (quest != null) _available.Add(quest);
            else Debug.LogWarning($"[QuestManager] No quest found for id '{id}' — skipped.");
        }

        for (int i = 0; i < activeIds.Count; i++)
        {
            var quest = FindById(activeIds[i]);
            if (quest == null)
            {
                Debug.LogWarning($"[QuestManager] No quest found for id '{activeIds[i]}' — skipped.");
                continue;
            }
            _active.Add(quest);
            _stages[quest.questId] = activeStages != null && i < activeStages.Count
                ? (QuestStage)activeStages[i]
                : QuestStage.TalkToClient;
        }

        OnQuestsChanged?.Invoke();
    }

    // ── Internal ──────────────────────────────────────────────────────────────

    QuestData FindById(string id)
    {
        if (_registry == null) return null;
        foreach (var q in _registry)
            if (q != null && q.questId == id) return q;
        return null;
    }
}
