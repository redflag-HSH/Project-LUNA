using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// Simple inventory system. Attach to the Player GameObject.
///
/// Hookup: On each Item in the scene, add a listener to its onInteract UnityEvent
/// and point it to this component's CollectItem(GameObject) method.
/// </summary>
public class Inventory : MonoBehaviour
{
    [System.Serializable]
    public class InventorySlot
    {
        public string itemName;
        public int itemCode;
        public string description;
        public Item.ItemType itemType;
        public Sprite icon;
        public int quantity;

        public InventorySlot() { }

        public InventorySlot(Item item)
        {
            itemName    = item.itemName;
            itemCode    = item.itemCode;
            description = item.description;
            itemType    = item.itemType;
            icon        = item.icon;
            quantity    = 1;
        }
    }

    [Header("Settings")]
    public int maxSlots = 20;

    [Header("Events")]
    public UnityEvent<InventorySlot> onItemAdded;
    public UnityEvent<InventorySlot> onItemRemoved;
    public UnityEvent onInventoryFull;

    /// <summary>Fires alongside onItemAdded on every Inventory instance. Lets systems like
    /// QuestManager react to item pickups without holding a reference to the player.</summary>
    public static event System.Action<InventorySlot> OnAnyItemAdded;

    [Header("Debug (read-only)")]
    [SerializeField] private List<InventorySlot> slots = new();

    public IReadOnlyList<InventorySlot> Slots => slots;

    // ── Public API ──────────────────────────────────────────────────────────

    /// <summary>
    /// Wire this to Item.onInteract in the Inspector (matches UnityEvent&lt;GameObject&gt;).
    /// </summary>
    public void CollectItem(GameObject itemObject)
    {
        if (itemObject.TryGetComponent(out Item item))
            AddItem(item);
    }

    /// <summary>Add an item directly. Stacks by code if code > 0, otherwise by name.</summary>
    public bool AddItem(Item item)
    {
        InventorySlot existing = FindSlot(item.itemCode, item.itemName);
        if (existing != null)
        {
            existing.quantity++;
            onItemAdded.Invoke(existing);
            OnAnyItemAdded?.Invoke(existing);
            return true;
        }

        if (slots.Count >= maxSlots)
        {
            Debug.Log($"[Inventory] Full – could not add {item.itemName}.");
            onInventoryFull.Invoke();
            return false;
        }

        InventorySlot newSlot = new(item);
        slots.Add(newSlot);
        onItemAdded.Invoke(newSlot);
        OnAnyItemAdded?.Invoke(newSlot);
        Debug.Log($"[Inventory] Added: {item.itemName} (x{newSlot.quantity})");
        return true;
    }

    /// <summary>Remove one of an item by code (preferred) or name. Returns true if removed.</summary>
    public bool RemoveItem(int itemCode, string itemName = null)
    {
        InventorySlot slot = FindSlot(itemCode, itemName);
        if (slot == null) return false;

        slot.quantity--;
        if (slot.quantity <= 0)
            slots.Remove(slot);

        onItemRemoved.Invoke(slot);
        Debug.Log($"[Inventory] Removed: {slot.itemName}");
        return true;
    }

    /// <summary>Remove one of an item by name. Returns true if removed.</summary>
    public bool RemoveItem(string itemName) => RemoveItem(0, itemName);

    /// <summary>Use a Usable item by code or name (removes one). Returns false if not found or wrong type.</summary>
    public bool UseItem(int itemCode, string itemName = null)
    {
        InventorySlot slot = FindSlot(itemCode, itemName);
        if (slot == null || slot.itemType != Item.ItemType.Usable)
        {
            Debug.Log($"[Inventory] Cannot use '{itemName ?? itemCode.ToString()}' – not found or not Usable.");
            return false;
        }

        Debug.Log($"[Inventory] Used: {slot.itemName}");
        return RemoveItem(itemCode, itemName);
    }

    /// <summary>Use a Usable item by name.</summary>
    public bool UseItem(string itemName) => UseItem(0, itemName);

    /// <summary>Check if inventory contains at least one item matching code or name.</summary>
    public bool HasItem(int itemCode, string itemName = null) => FindSlot(itemCode, itemName) != null;

    /// <summary>Check if inventory contains at least one item by name.</summary>
    public bool HasItem(string itemName) => HasItem(0, itemName);

    /// <summary>Total quantity by code or name.</summary>
    public int GetQuantity(int itemCode, string itemName = null) => FindSlot(itemCode, itemName)?.quantity ?? 0;

    /// <summary>Total quantity by name.</summary>
    public int GetQuantity(string itemName) => GetQuantity(0, itemName);

    /// <summary>
    /// Finds a slot by code first (if code > 0), then falls back to name.
    /// </summary>
    public InventorySlot FindSlot(int itemCode, string itemName = null)
    {
        if (itemCode > 0)
        {
            InventorySlot byCode = slots.Find(s => s.itemCode == itemCode);
            if (byCode != null) return byCode;
        }
        if (!string.IsNullOrEmpty(itemName))
            return slots.Find(s => s.itemName == itemName);
        return null;
    }

    // ── Save / load helpers (used by InventorySave) ──────────────────────────

    /// <summary>Remove all slots without firing events. Used by InventorySave.</summary>
    public void ClearInventory() => slots.Clear();

    /// <summary>Add a pre-built slot directly without firing events. Used by InventorySave.</summary>
    public void AddSlotDirect(InventorySlot slot) => slots.Add(slot);
}