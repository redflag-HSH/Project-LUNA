using UnityEngine;

/// <summary>
/// Attach to a trigger Collider2D placed in a scene. Reports arrival to QuestManager,
/// which forwards it to any active quest's ReachLocation objective matching locationId.
/// </summary>
public class QuestLocationTrigger : MonoBehaviour
{
    [SerializeField] string locationId;

    void OnTriggerEnter2D(Collider2D other)
    {
        if (!other.CompareTag("Player")) return;
        if (QuestManager.Instance == null) return;

        QuestManager.Instance.ReportLocationReached(locationId);
    }
}
