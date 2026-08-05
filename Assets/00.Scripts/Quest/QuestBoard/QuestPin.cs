using UnityEngine;

public class QuestPin : MonoBehaviour
{
    [Tooltip("Quest this pin represents. Set by QuestBoard when the pin is spawned.")]
    public QuestData quest;

    void OnEnable() => QuestManager.OnQuestsChanged += HandleQuestsChanged;
    void OnDisable() => QuestManager.OnQuestsChanged -= HandleQuestsChanged;

    void HandleQuestsChanged()
    {
        // Quest left the available list (accepted or removed) — this pin is stale.
        if (quest == null || QuestManager.Instance == null) return;
        if (!QuestManager.Instance.IsAvailable(quest))
            Destroy(gameObject);
    }

    public void PinClicked()
    {
        if (QuestAcceptPanel.Instance != null)
            QuestAcceptPanel.Instance.Show(quest);
    }
}
