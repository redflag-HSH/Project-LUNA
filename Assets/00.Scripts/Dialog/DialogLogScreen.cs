using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Simple backlog panel — shows the last 30 dialog lines seen this session
/// (DialogSystem.History) in one scrollable text block.
///
/// UI hierarchy to build in the Inspector:
///   LogPanel
///   └─ Scroll View (ScrollRect)
///      └─ Viewport
///         └─ Content
///            └─ LogText (TextMeshProUGUI, with a ContentSizeFitter so it grows with the text)
///
/// Opened/closed via the Log action (2DActions/Player2D/Log, bound to the L key, only
/// while a dialog is open) or by right-clicking the dialog box — see DialogBoxLogTrigger,
/// which should sit on the same GameObject as the dialog box.
/// </summary>
public class DialogLogScreen : MonoBehaviour
{
    public static DialogLogScreen Instance { get; private set; }

    [Header("Panel")]
    public GameObject logPanel;

    [Header("Text")]
    public TextMeshProUGUI logText;

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
        if (logText == null || DialogSystem.Instance == null) return;

        var sb = new StringBuilder();
        foreach (var entry in DialogSystem.Instance.History)
        {
            if (!string.IsNullOrWhiteSpace(entry.speakerName))
                sb.Append(entry.speakerName).Append(": ");
            sb.AppendLine(entry.text);
            sb.AppendLine();
        }

        logText.text = sb.ToString();
    }
}
