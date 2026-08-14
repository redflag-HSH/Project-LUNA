using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Tracks which EquipmentData items are currently equipped and at what plus level,
/// and pushes their combined bonus into PlayerBasicStatContainer. Flat list, no slot
/// types or capacity limit — any owned equipment can be equipped or unequipped freely.
/// </summary>
public class PlayerEquipment : MonoBehaviour
{
    [Serializable]
    public class EquippedItem
    {
        public EquipmentData data;
        public int plusLevel;
    }

    [Serializable]
    public class EquippedSaveEntry
    {
        public string equipmentId;
        public int plusLevel;
    }

    [Tooltip("All EquipmentData assets in the project. Required for save/load lookup.")]
    [SerializeField] EquipmentData[] _registry;

    public List<EquippedItem> equippedItems = new();

    PlayerBasicStatContainer _stats;

    void Awake()
    {
        _stats = GetComponent<PlayerBasicStatContainer>();
        RecalculateBonuses();
    }

    // ── Public API ────────────────────────────────────────────────────────────

    public bool IsEquipped(EquipmentData data) => data != null && equippedItems.Exists(e => e.data == data);

    public void Equip(EquipmentData data, int plusLevel = 0)
    {
        if (data == null || IsEquipped(data)) return;
        equippedItems.Add(new EquippedItem { data = data, plusLevel = Mathf.Clamp(plusLevel, 0, data.MaxPlusLevel) });
        RecalculateBonuses();
    }

    public void Unequip(EquipmentData data)
    {
        if (equippedItems.RemoveAll(e => e.data == data) > 0)
            RecalculateBonuses();
    }

    public void SetPlusLevel(EquipmentData data, int plusLevel)
    {
        var entry = equippedItems.Find(e => e.data == data);
        if (entry == null) return;
        entry.plusLevel = Mathf.Clamp(plusLevel, 0, data.MaxPlusLevel);
        RecalculateBonuses();
    }

    public void RecalculateBonuses()
    {
        int attack = 0, defend = 0, health = 0, stamina = 0, moveSpeed = 0, attackSpeed = 0, luck = 0;

        foreach (var item in equippedItems)
        {
            if (item.data == null) continue;
            var bonus = item.data.GetPlusLevel(item.plusLevel);
            attack += bonus.attackPoint;
            defend += bonus.defendPoint;
            health += bonus.healthPoint;
            stamina += bonus.staminaPoint;
            moveSpeed += bonus.moveSpeedPoint;
            attackSpeed += bonus.attackSpeedPoint;
            luck += bonus.luckPoint;
        }

        if (_stats != null)
            _stats.SetEquipmentBonuses(attack, defend, health, stamina, moveSpeed, attackSpeed, luck);
    }

    // ── Save / Load ───────────────────────────────────────────────────────────

    public List<EquippedSaveEntry> ExportEquipped()
    {
        var result = new List<EquippedSaveEntry>();
        foreach (var item in equippedItems)
        {
            if (item.data == null) continue;
            result.Add(new EquippedSaveEntry { equipmentId = item.data.equipmentId, plusLevel = item.plusLevel });
        }
        return result;
    }

    public void LoadFromSave(List<EquippedSaveEntry> entries)
    {
        equippedItems.Clear();

        if (entries != null)
        {
            foreach (var entry in entries)
            {
                var data = FindById(entry.equipmentId);
                if (data == null)
                {
                    Debug.LogWarning($"[PlayerEquipment] No equipment found for id '{entry.equipmentId}' — skipped.");
                    continue;
                }
                equippedItems.Add(new EquippedItem { data = data, plusLevel = Mathf.Clamp(entry.plusLevel, 0, data.MaxPlusLevel) });
            }
        }

        RecalculateBonuses();
    }

    EquipmentData FindById(string id)
    {
        if (_registry == null) return null;
        foreach (var e in _registry)
            if (e != null && e.equipmentId == id) return e;
        return null;
    }
}
