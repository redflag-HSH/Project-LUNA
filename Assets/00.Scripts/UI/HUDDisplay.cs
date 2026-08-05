using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class HUDDisplay : MonoBehaviour
{
    public static HUDDisplay Instance { get; private set; }

    [Header("Quest HUD")]
    public GameObject questHUD;
    public Transform questLineContainer;
    public GameObject questLinePrefab;

    // ── Lifecycle ────────────────────────────────────────────────────────────

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(GetComponentInParent<Canvas>().gameObject); return; }
        Instance = this;
        DontDestroyOnLoad(GetComponentInParent<Canvas>().gameObject);
    }

    void OnEnable()
    {
        QuestManager.OnQuestsChanged += RefreshQuestDisplay;
    }

    void OnDisable()
    {
        QuestManager.OnQuestsChanged -= RefreshQuestDisplay;
    }

    void Start()
    {
        RefreshQuestDisplay();
    }

    // ── Quest HUD ─────────────────────────────────────────────────────────────

    void RefreshQuestDisplay()
    {
        if (questHUD == null) return;

        if (questLineContainer != null)
        {
            foreach (Transform child in questLineContainer)
                Destroy(child.gameObject);
        }

        var quests = QuestManager.Instance != null ? QuestManager.Instance.ActiveQuests : null;
        bool hasAny = quests != null && quests.Count > 0;
        questHUD.SetActive(hasAny);

        if (!hasAny || questLinePrefab == null) return;

        foreach (QuestData quest in quests)
        {
            GameObject row = Instantiate(questLinePrefab, questLineContainer);
            if (row.TryGetComponent(out TextMeshProUGUI label))
            {
                bool done = QuestManager.Instance.IsCompleted(quest);
                string tag = done ? "<sprite name=\"checked\">" : "<sprite name=\"unchecked\">";
                label.text = $"{tag} {quest.questName}";
            }
        }
    }

    // ── Cutscene Animation ─────────────────────────────────────────────────────────────
    public void Cutscene()
    {
        Animator animator = GetComponentInParent<Animator>();
        animator.SetTrigger("Cutscene");
    }
}
