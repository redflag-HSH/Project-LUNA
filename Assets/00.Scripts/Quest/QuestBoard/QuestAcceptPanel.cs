using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Small card shown when a quest board pin is clicked — just the quest's name and
/// info, with a choice to accept it or not. Replaces opening a full dedicated
/// dialog per quest.
///
/// UI hierarchy to build in the Inspector:
///   Canvas
///   └─ QuestAcceptPanel (this script)
///      └─ Panel            (background, assign as `panel`)
///         ├─ NameText      (TextMeshProUGUI)
///         ├─ InfoText      (TextMeshProUGUI)
///         ├─ AcceptButton  (Button)
///         └─ DeclineButton (Button)
/// </summary>
public class QuestAcceptPanel : MonoBehaviour
{
    public static QuestAcceptPanel Instance { get; private set; }

    [SerializeField] GameObject panel;
    [SerializeField] TextMeshProUGUI nameText;
    [SerializeField] TextMeshProUGUI infoText;
    [SerializeField] Button acceptButton;
    [SerializeField] Button declineButton;

    QuestData _quest;

    void Awake()
    {
        if (Instance != null) { Destroy(gameObject); return; }
        Instance = this;

        if (acceptButton != null) acceptButton.onClick.AddListener(Accept);
        if (declineButton != null) declineButton.onClick.AddListener(Hide);
        if (panel != null) panel.SetActive(false);
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    public void Show(QuestData quest)
    {
        if (quest == null) return;

        _quest = quest;
        if (nameText != null) nameText.text = quest.questName;
        if (infoText != null) infoText.text = quest.info;
        if (panel != null) panel.SetActive(true);
    }

    public void Hide()
    {
        _quest = null;
        if (panel != null) panel.SetActive(false);
    }

    void Accept()
    {
        if (_quest != null && QuestManager.Instance != null)
            QuestManager.Instance.AddQuest(_quest);
        Hide();
    }
}
