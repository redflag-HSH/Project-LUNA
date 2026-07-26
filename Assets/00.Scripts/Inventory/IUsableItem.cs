/// <summary>
/// Implement this on any MonoBehaviour that represents a usable item effect.
/// ItemConsumer will call OnUse() and consume one stack from the Inventory.
/// </summary>
/// 
using UnityEngine;
public interface IUsableItem
{
    /// <summary>Must match the itemName stored in the Inventory slot.</summary>
    string ItemName { get; }

    int ItemCode { get; }

    /// <summary>If false, the item is not consumed from inventory on use.</summary>
    bool IsConsumable { get; }

    /// <summary>Seconds before this item can be used again. 0 = no cooldown.</summary>
    float CooldownDuration { get; }

    /// <summary>Apply the item's effect to the player.</summary>
    void OnUse(PlayerControl player);
}
