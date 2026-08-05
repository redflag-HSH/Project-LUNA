using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Simple backlog panel — shows the last 30 dialog lines seen this session
/// (DialogSystem.History), one row per entry.
///
/// UI hierarchy to build in the Inspector:
///   LogPanel
///   └─ Scroll View (ScrollRect)
///      └─ Viewport
///         └─ Content (assign as `logContainer` — needs a layout group, e.g.
///                      Vertical Layout Group + ContentSizeFitter, so rows stack)
///
/// Entry prefab (assign as `logEntryPrefab`) needs a DialogLogEntryView on its
/// root, with a name text and a content text.
///
/// Opened via the Log action (2DActions/Player2D/Log, bound to the L key, only while a
/// dialog is open) or by right-clicking the dialog box — see DialogBoxLogTrigger, which
/// should sit on the same GameObject as the dialog box. Closed the same way (L toggles),
/// or by right-clicking the log panel itself — see DialogLogCloseTrigger, which should
/// sit on the log panel's background.
/// </summary>
public class DialogLogScreen : MonoBehaviour
{
    public static DialogLogScreen Instance { get; private set; }

    [Header("Panel")]
    public GameObject logPanel;

    [Header("Entries")]
    [Tooltip("Parent the entry rows are instantiated into, e.g. the Scroll View's Content.")]
    public Transform logContainer;
    [Tooltip("Prefab with a DialogLogEntryView on its root.")]
    public GameObject logEntryPrefab;

    public bool IsOpen { get; private set; }

    _2DActions actions;

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        if (logPanel != null) logPanel.SetActive(false);
        actions = new _2DActions();
    }

    void OnEnable()
    {
        actions.Player2D.Log.performed += OnLogPressed;
        actions.Player2D.Log.Enable();
    }

    void OnDisable()
    {
        actions.Player2D.Log.performed -= OnLogPressed;
        actions.Player2D.Log.Disable();
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
        actions?.Dispose();
    }

    void OnLogPressed(InputAction.CallbackContext _)
    {
        if (DialogSystem.Instance == null || !DialogSystem.Instance.IsOpen) return;
        Toggle();
    }

    public void Toggle()
    {
        if (IsOpen) Hide();
        else Show();
    }

    public void Show()
    {
        RefreshLog();
        IsOpen = true;
        if (logPanel != null) logPanel.SetActive(true);
    }

    public void Hide()
    {
        IsOpen = false;
        if (logPanel != null) logPanel.SetActive(false);
    }

    void RefreshLog()
    {
        if (logContainer == null || logEntryPrefab == null || DialogSystem.Instance == null) return;

        for (int i = logContainer.childCount - 1; i >= 0; i--)
            Destroy(logContainer.GetChild(i).gameObject);

        foreach (var entry in DialogSystem.Instance.History)
        {
            var go = Instantiate(logEntryPrefab, logContainer);
            if (go.TryGetComponent<DialogLogEntryView>(out var view))
                view.Setup(entry.speakerName, entry.text);
            else
                Debug.LogError("[DialogLogScreen] Log entry prefab has no DialogLogEntryView on its root — the row will be blank.", go);
        }
    }
}
