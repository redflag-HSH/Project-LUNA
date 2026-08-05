using UnityEngine;
using UnityEngine.EventSystems;

/// <summary>
/// Attach to the log panel's background (needs a raycastable Image). Right-clicking it
/// closes the log screen — the mouse alternative to pressing L again.
/// </summary>
public class DialogLogCloseTrigger : MonoBehaviour, IPointerClickHandler
{
    public void OnPointerClick(PointerEventData eventData)
    {
        if (eventData.button != PointerEventData.InputButton.Right) return;
        DialogLogScreen.Instance?.Hide();
    }
}
