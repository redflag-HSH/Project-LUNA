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
/// Opened/closed via the L key (while a dialog is open) or by right-clicking the dialog
/// box — see DialogBoxLogTrigger, which should sit on the same GameObject as the dialog box.
/// </summary>
public class DialogLogScreen : MonoBehaviour
{
    public static DialogLogScreen Instance { get; private set; }

    [Header("Panel")]
    public GameObject logPanel;

    [Header("Text")]
    public TextMeshProUGUI logText;

    public bool IsOpen { get; private set; }

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        if (logPanel != null) logPanel.SetActive(false);
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    void Update()
    {
        if (DialogSystem.Instance == null || !DialogSystem.Instance.IsOpen) return;
        if (Keyboard.current != null && Keyboard.current.lKey.wasPressedThisFrame)
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
