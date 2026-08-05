using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Playables;
using UnityEngine.UI;

/// <summary>One recorded line for the log screen — speaker name (may be blank) and body text.</summary>
public readonly struct DialogLogEntry
{
    public readonly string speakerName;
    public readonly string text;

    public DialogLogEntry(string speakerName, string text)
    {
        this.speakerName = speakerName;
        this.text = text;
    }
}

/// <summary>
/// Blue-Archive-style dialog system.
///
/// UI hierarchy to build in the Inspector:
///   Canvas
///   └─ DialogPanel
///      ├─ PortraitLeft   (Image)
///      ├─ PortraitRight  (Image)
///      └─ DialogBox      (bottom bar)
///         ├─ NamePlate   (Image)
///         │  └─ NameText (TextMeshProUGUI)
///         ├─ BodyText    (TextMeshProUGUI)
///         └─ NextIndicator (GameObject with Image — e.g. "▶")
/// </summary>
public class DialogSystem : MonoBehaviour
{
    public static DialogSystem Instance { get; private set; }

    [Header("Panel")]
    public GameObject dialogPanel;

    [Header("Portraits")]
    public DialogIlust dialogIlust;

    [Header("Name Plate")]
    public Image namePlateBackground;
    public TextMeshProUGUI nameText;

    [Header("Body")]
    public TextMeshProUGUI bodyText;

    [Header("Typewriter")]
    [Tooltip("Characters per second. 0 = instant.")]
    public float charsPerSecond = 40f;

    [Header("Fast Forward")]
    [Tooltip("Hold Left Ctrl to instantly display each line and auto-advance without waiting for Interact.")]
    [SerializeField] float fastForwardAdvanceDelay = 0.15f;

    [Header("Choices")]
    [Tooltip("Parent transform where choice buttons are spawned.")]
    public Transform choiceContainer;
    [Tooltip("Prefab with Button + DialogChoiceButton + TMP label.")]
    public GameObject choiceButtonPrefab;

    [Header("Registry")]
    [Tooltip("All Dialog assets that need to be found by id at runtime (e.g. from QuestData.dialogID).")]
    [SerializeField] Dialog[] dialogRegistry;

    public bool IsOpen { get; private set; }

    // ── Log ───────────────────────────────────────────────────────────────────

    const int MaxHistoryLines = 30;

    readonly List<DialogLogEntry> _history = new();
    public IReadOnlyList<DialogLogEntry> History => _history;

    // ── State ─────────────────────────────────────────────────────────────────

    Dialog current;
    int lineIndex;
    bool isTyping;
    bool isShowingChoices;

    Coroutine typewriterRoutine;
    PlayableDirector timelineDirector;
    bool restoreInputOnClose = true;

    _2DActions actions;
    float fastForwardTimer;


    // ── Unity ─────────────────────────────────────────────────────────────────

    void Awake()
    {
        if (Instance != null) { Destroy(gameObject); return; }
        Instance = this;
        dialogPanel.SetActive(false);
        actions = new _2DActions();
    }

    void OnEnable()
    {
        actions.Player2D.DialogFastForward.Enable();
    }

    void OnDisable()
    {
        actions.Player2D.DialogFastForward.Disable();
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
        actions?.Dispose();
    }

    void Update()
    {
        if (!IsOpen || isShowingChoices)
        {
            fastForwardTimer = 0f;
            return;
        }

        if (!actions.Player2D.DialogFastForward.IsPressed())
        {
            fastForwardTimer = 0f;
            return;
        }

        if (isTyping)
        {
            SkipTypewriter();
            fastForwardTimer = 0f;
            return;
        }

        fastForwardTimer += Time.deltaTime;
        if (fastForwardTimer >= fastForwardAdvanceDelay)
        {
            fastForwardTimer = 0f;
            Advance();
        }
    }

#if UNITY_EDITOR
    // Keep the registry filled automatically — a Dialog asset missing from it
    // makes GetDialogById fail silently at runtime (dead quest pins etc.).
    void OnValidate()
    {
        var guids = UnityEditor.AssetDatabase.FindAssets("t:Dialog");
        var found = new System.Collections.Generic.List<Dialog>(guids.Length);
        foreach (var guid in guids)
        {
            var d = UnityEditor.AssetDatabase.LoadAssetAtPath<Dialog>(
                UnityEditor.AssetDatabase.GUIDToAssetPath(guid));
            if (d != null) found.Add(d);
        }

        if (dialogRegistry == null || dialogRegistry.Length != found.Count)
        {
            dialogRegistry = found.ToArray();
            UnityEditor.EditorUtility.SetDirty(this);
        }
    }
#endif

    // ── Public API ────────────────────────────────────────────────────────────

    public Dialog GetDialogById(int id)
    {
        if (dialogRegistry != null)
            foreach (var d in dialogRegistry)
                if (d != null && d.dialogId == id) return d;

        Debug.LogWarning($"[DialogSystem] No dialog found for id {id}.");
        return null;
    }

    public void OpenFromTimeline(Dialog dialog, PlayableDirector director)
    {
        timelineDirector = director;
        if (timelineDirector != null) timelineDirector.Pause();
        Open(dialog);
    }

    public void Open(Dialog dialog) => Open(dialog, restoreInput: true);

    /// <param name="restoreInput">Pass false when something else (e.g. the quest board)
    /// disabled player input and should stay in control after this dialog closes.</param>
    public void Open(Dialog dialog, bool restoreInput)
    {
        if (dialog == null || dialog.lines == null || dialog.lines.Length == 0) return;

        restoreInputOnClose = restoreInput;
        current = dialog;
        lineIndex = 0;
        IsOpen = true;

        dialogPanel.SetActive(true);
        dialogIlust.ToggleShow();
        if (PlayerControl.Instance != null) PlayerControl.Instance.SetInputEnabled(false);
        ShowLine(0);
    }

    /// <summary>Press Interact to advance. First press skips typewriter; second advances.</summary>
    public void Advance()
    {
        if (!IsOpen || isShowingChoices) return;
        if (DialogLogScreen.Instance != null && DialogLogScreen.Instance.IsOpen) return;

        if (isTyping)
        {
            SkipTypewriter();
            return;
        }

        DialogLine line = current.lines[lineIndex];
        if (line.choices != null && line.choices.Length > 0)
        {
            ShowChoices(line.choices);
            return;
        }

        lineIndex++;
        if (lineIndex < current.lines.Length)
            ShowLine(lineIndex);
        else
            Close();
    }

    public void Close()
    {
        ApplyQuestAction(current);

        StopAllCoroutines();
        isTyping = false;
        isShowingChoices = false;
        IsOpen = false;
        current = null;
        ClearChoices();
        dialogPanel.SetActive(false);
        dialogIlust.ToggleShow();
        if (timelineDirector != null)
        {
            timelineDirector.Resume();
            timelineDirector = null;
        }
        else
        {
            if (restoreInputOnClose && PlayerControl.Instance != null)
                PlayerControl.Instance.SetInputEnabled(true);
        }
        restoreInputOnClose = true;
    }

    void ApplyQuestAction(Dialog dialog)
    {
        if (dialog == null || dialog.questAction == DialogQuestAction.None || dialog.quest == null) return;
        if (QuestManager.Instance == null)
        {
            Debug.LogWarning($"[DialogSystem] Dialog '{dialog.name}' wants to grant quest '{dialog.quest.questId}' but no QuestManager exists.");
            return;
        }

        switch (dialog.questAction)
        {
            case DialogQuestAction.AddAvailable: QuestManager.Instance.AddAvailableQuest(dialog.quest); break;
            case DialogQuestAction.AddActive:    QuestManager.Instance.AddQuest(dialog.quest); break;
            case DialogQuestAction.AdvanceStage: QuestManager.Instance.AdvanceStage(dialog.quest); break;
        }
    }

    void ShowChoices(DialogChoice[] choices)
    {
        isShowingChoices = true;
        if (choiceContainer != null) choiceContainer.gameObject.SetActive(true);

        foreach (var choice in choices)
        {
            var go = Instantiate(choiceButtonPrefab, choiceContainer);
            if (go.TryGetComponent<DialogChoiceButton>(out var btn))
            {
                var captured = choice;
                btn.Setup(captured.text, () => OnChoicePicked(captured.nextDialog));
            }
            else
            {
                Debug.LogError("[DialogSystem] Choice button prefab has no DialogChoiceButton on its root — the button will do nothing.", go);
            }
        }
    }

    void OnChoicePicked(Dialog nextDialog)
    {
        ClearChoices();
        isShowingChoices = false;

        if (nextDialog != null)
        {
            // The outgoing dialog ends here without Close() — fire its action now.
            ApplyQuestAction(current);
            Open(nextDialog, restoreInputOnClose);
        }
        else
            Close();
    }

    void ClearChoices()
    {
        if (choiceContainer == null) return;
        choiceContainer.gameObject.SetActive(false);
        for (int i = choiceContainer.childCount - 1; i >= 0; i--)
            Destroy(choiceContainer.GetChild(i).gameObject);
    }

    // ── Internal ──────────────────────────────────────────────────────────────

    void ShowLine(int index)
    {
        DialogLine line = current.lines[index];

        // A line with neither speaker name nor text (and no choices) is empty —
        // treat it as the end of the dialog and close automatically.
        if (string.IsNullOrWhiteSpace(line.speakerName) &&
            string.IsNullOrWhiteSpace(line.text) &&
            (line.choices == null || line.choices.Length == 0))
        {
            Close();
            return;
        }

        _history.Add(new DialogLogEntry(line.speakerName, line.text));
        if (_history.Count > MaxHistoryLines)
            _history.RemoveAt(0);

        // ── Portraits ──
        if (dialogIlust != null) dialogIlust.Apply(line);

        // ── Name plate ──
        bool hasName = !string.IsNullOrWhiteSpace(line.speakerName);
        if (nameText != null)
        {
            nameText.gameObject.SetActive(hasName);
            nameText.text = line.speakerName;
        }
        if (namePlateBackground != null)
            namePlateBackground.color = line.namePlateColor;

        // ── Typewriter ──
        if (typewriterRoutine != null) StopCoroutine(typewriterRoutine);
        if (charsPerSecond > 0f)
            typewriterRoutine = StartCoroutine(Typewriter(line.text));
        else
            bodyText.text = line.text;
    }

    void SkipTypewriter()
    {
        if (typewriterRoutine != null) StopCoroutine(typewriterRoutine);
        bodyText.text = current.lines[lineIndex].text;
        bodyText.maxVisibleCharacters = int.MaxValue;
        isTyping = false;
    }

    // ── Coroutines ────────────────────────────────────────────────────────────

    IEnumerator Typewriter(string line)
    {
        isTyping = true;
        bodyText.text = line;
        bodyText.maxVisibleCharacters = 0;
        bodyText.ForceMeshUpdate();

        int total = bodyText.textInfo.characterCount;
        float delay = 1f / charsPerSecond;

        for (int i = 0; i <= total; i++)
        {
            bodyText.maxVisibleCharacters = i;
            yield return new WaitForSeconds(delay);
        }

        isTyping = false;
    }
}
