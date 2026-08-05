using UnityEngine;
using UnityEngine.EventSystems;

/// <summary>
/// Attach to the dialog box GameObject (needs a raycastable Image). Left-clicking it
/// advances the dialog — the mouse alternative to the Interact key. Sits alongside
/// DialogBoxLogTrigger, which handles right-click on the same box.
/// </summary>
public class DialogBoxAdvanceTrigger : MonoBehaviour, IPointerClickHandler
{
    public void OnPointerClick(PointerEventData eventData)
    {
        if (eventData.button != PointerEventData.InputButton.Left) return;
        DialogSystem.Instance?.Advance();
    }
}
