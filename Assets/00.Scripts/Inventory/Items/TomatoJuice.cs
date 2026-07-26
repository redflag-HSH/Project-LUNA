using UnityEngine;

/// <summary>
/// Heals the player by healAmount, capped so total HP cannot exceed healCap.
/// Assign to an ItemConsumer quick slot.
/// </summary>
public class TomatoJuice : MonoBehaviour, IUsableItem
{
    [Tooltip("Must match the item name in the Inventory slot.")]
    [SerializeField] private string itemName = "Tomato Juice";

    [Tooltip("Unique item code.")]
    [SerializeField] private int itemCode = 0;

    [Tooltip("HP restored per use.")]
    public float healAmount = 30f;

    [Tooltip("HP cannot be healed beyond this value (0 = no cap, use maxHp).")]
    public float healCap = 0f;

    [Tooltip("Seconds before this item can be used again. 0 = no cooldown.")]
    public float cooldownDuration = 0f;

    public string ItemName => itemName;
    public int ItemCode => itemCode;
    public bool IsConsumable => false;
    public float CooldownDuration => cooldownDuration;

    public void OnUse(PlayerControl player)
    {
        float cap = healCap > 0f ? healCap : player.maxHp;
        float allowed = Mathf.Max(cap - player.CurrentHp, 0f);
        float actual = Mathf.Min(healAmount, allowed);

        if (actual <= 0f)
        {
            Debug.Log("[TomatoJuice] HP already at or above heal cap.");
            return;
        }

        player.Heal(actual);
        Debug.Log($"[TomatoJuice] Healed {actual} HP.");
    }
}
