using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Small card shown when a quest board pin is clicked — just the quest's
/// info, with a choice to accept it or not. Replaces opening a full dedicated
/// dialog per quest.
///
/// UI hierarchy to build in the Inspector:
///   Canvas
///   └─ QuestAcceptPanel (this script)
///      └─ Panel            (background, assign as `panel`)
///         ├─ InfoText      (TextMeshProUGUI)
///         ├─ MessageText   (TextMeshProUGUI — shown when accepting fails, e.g. already have an active quest)
///         ├─ AcceptButton  (Button)
///         └─ DeclineButton (Button)
/// </summary>
public class QuestAcceptPanel : MonoBehaviour
{
    public static QuestAcceptPanel Instance { get; private set; }

    [SerializeField] GameObject panel;
    [SerializeField] TextMeshProUGUI infoText;
    [SerializeField] TextMeshProUGUI messageText;
    [SerializeField] Button acceptButton;
    [SerializeField] Button declineButton;

    [SerializeField] string cantAcceptMessage = "퀘스트는 한 번에 하나만 진행할 수 있습니다.";

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
        if (infoText != null) infoText.text = quest.info;
        SetMessage(null);
        if (panel != null) panel.SetActive(true);
    }

    public void Hide()
    {
        _quest = null;
        if (panel != null) panel.SetActive(false);
    }

    void Accept()
    {
        if (_quest == null || QuestManager.Instance == null) return;

        if (QuestManager.Instance.AddQuest(_quest))
            Hide();
        else
            SetMessage(cantAcceptMessage);
    }

    void SetMessage(string message)
    {
        if (messageText == null) return;
        messageText.text = message;
        messageText.gameObject.SetActive(!string.IsNullOrEmpty(message));
    }
}
