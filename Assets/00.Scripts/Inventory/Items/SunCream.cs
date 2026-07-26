using UnityEngine;

/// <summary>
/// Restores the player's SunproofGuard to its maximum value.
/// Assign to an ItemConsumer quick slot.
/// </summary>
public class SunCream : MonoBehaviour, IUsableItem
{
    [Tooltip("Must match the item name in the Inventory slot.")]
    [SerializeField] private string itemName = "Sun Cream";

    [Tooltip("Unique item code.")]
    [SerializeField] private int itemCode = 1;

    [Tooltip("Seconds before this item can be used again. 0 = no cooldown.")]
    [SerializeField] private float cooldownDuration = 0f;

    public string ItemName => itemName;
    public int ItemCode => itemCode;
    public bool IsConsumable => true;
    public float CooldownDuration => cooldownDuration;

    public void OnUse(PlayerControl player)
    {
        if (player.maxSunproofGuard <= 0f)
        {
            Debug.Log("[SunCream] SunproofGuard is disabled on this player.");
            return;
        }

        player.SetSunproofGuard(player.maxSunproofGuard);
        Debug.Log("[SunCream] SunproofGuard fully restored.");
    }
}
