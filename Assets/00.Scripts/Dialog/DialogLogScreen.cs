using System.Text;
using TMPro;
using UnityEngine;

/// <summary>
/// Simple backlog panel — shows every dialog line seen this session
/// (DialogSystem.History) in one scrollable text block.
///
/// UI hierarchy to build in the Inspector:
///   LogPanel
///   └─ Scroll View (ScrollRect)
///      └─ Viewport
///         └─ Content
///            └─ LogText (TextMeshProUGUI, with a ContentSizeFitter so it grows with the text)
///
/// Open/close this from a "Log" button placed in the dialog box (Button.onClick → Toggle()).
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
