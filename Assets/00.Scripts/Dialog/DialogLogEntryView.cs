using TMPro;
using UnityEngine;

/// <summary>
/// One row in the dialog log — a speaker name and their line. Attach to the root
/// of the log entry prefab assigned to DialogLogScreen.logEntryPrefab.
/// </summary>
public class DialogLogEntryView : MonoBehaviour
{
    [SerializeField] TextMeshProUGUI nameText;
    [SerializeField] TextMeshProUGUI contentText;

    public void Setup(string speakerName, string content)
    {
        bool hasName = !string.IsNullOrWhiteSpace(speakerName);
        if (nameText != null)
        {
            nameText.gameObject.SetActive(hasName);
            nameText.text = speakerName;
        }
        if (contentText != null) contentText.text = content;
    }
}
