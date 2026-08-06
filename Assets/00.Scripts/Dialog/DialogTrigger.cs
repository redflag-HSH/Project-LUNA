using UnityEngine;

/// <summary>
/// Attach to any NPC or object. Assign a Dialog asset.
/// When the player interacts, opens the dialog; on subsequent presses, advances it.
/// Requires the Interactor component on the player and the IInteractable interface.
///
/// Conditional dialogs: entries are checked top to bottom and the first whose
/// condition matches is played. If none match, the default Dialog is used.
/// </summary>
public class DialogTrigger : MonoBehaviour, IInteractable
{
    public enum DialogCondition
    {
        Always,             // always matches
        QuestNotStarted,    // quest is not available, active, or completed
        QuestAvailable,     // quest is on the board / offered
        QuestActive,        // quest has been accepted
        QuestObjectiveComplete, // quest is active AND the given objective is complete
        QuestCompleted      // quest is done
    }

    [System.Serializable]
    public class ConditionalDialog
    {
        public DialogCondition condition = DialogCondition.Always;
        [Tooltip("Quest whose state is checked. Ignored for Always.")]
        public QuestData quest;
        [Tooltip("Objective id to check. Only used with QuestObjectiveComplete.")]
        public string objectiveId;
        public Dialog dialog;
    }

    [Tooltip("Checked top to bottom — the first matching entry is played.")]
    public ConditionalDialog[] conditionalDialogs;

    [Tooltip("Played when no conditional entry matches (or none are set).")]
    public Dialog dialog;

    public void Interact(GameObject interactor)
    {
        if (DialogSystem.Instance == null) return;

        if (DialogSystem.Instance.IsOpen)
            DialogSystem.Instance.Advance();
        else
            DialogSystem.Instance.Open(SelectDialog());
    }

    /// <summary>First conditional dialog whose condition matches, else the default.</summary>
    protected Dialog SelectDialog()
    {
        if (conditionalDialogs != null)
            foreach (var entry in conditionalDialogs)
                if (entry != null && entry.dialog != null && Matches(entry))
                    return entry.dialog;
        return dialog;
    }

    static bool Matches(ConditionalDialog entry)
    {
        if (entry.condition == DialogCondition.Always) return true;

        var qm = QuestManager.Instance;
        if (qm == null || entry.quest == null) return false;

        switch (entry.condition)
        {
            case DialogCondition.QuestAvailable: return qm.IsAvailable(entry.quest);
            case DialogCondition.QuestActive:    return qm.IsActive(entry.quest);
            case DialogCondition.QuestObjectiveComplete: return qm.IsObjectiveComplete(entry.quest, entry.objectiveId);
            case DialogCondition.QuestCompleted: return qm.IsCompleted(entry.quest);
            case DialogCondition.QuestNotStarted:
                return !qm.IsAvailable(entry.quest) && !qm.IsActive(entry.quest) && !qm.IsCompleted(entry.quest);
            default: return false;
        }
    }
}
