using UnityEngine;
using UnityEngine.EventSystems;

/// <summary>
/// Attach to the dialog box GameObject (needs a raycastable Image). Right-clicking it
/// toggles the log screen — the alternate trigger to the L key (DialogLogScreen.Update()).
/// Sits alongside DialogBoxAdvanceTrigger, which handles left-click on the same box.
/// </summary>
public class DialogBoxLogTrigger : MonoBehaviour, IPointerClickHandler
{
    public void OnPointerClick(PointerEventData eventData)
    {
        if (eventData.button != PointerEventData.InputButton.Right) return;
        DialogLogScreen.Instance?.Toggle();
    }
}
