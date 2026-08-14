using System;
using UnityEngine;

/// <summary>
/// Bonus applied to each of PlayerBasicStatContainer's basic points at one enhancement
/// ("+N") level. Values are added straight into the matching _plusXxxPoint accumulator —
/// any scaling (e.g. MoveSpeedPoint/AttackSpeedPoint/LuckPoint's *0.1) happens there, not here.
/// </summary>
[Serializable]
public class EquipmentPlusLevel
{
    public int attackPoint;
    public int defendPoint;
    public int healthPoint;
    public int staminaPoint;
    public int moveSpeedPoint;
    public int attackSpeedPoint;
    public int luckPoint;
}

/// <summary>
/// Defines one specific piece of equipment: its identity plus how much it boosts each
/// basic point at every enhancement level. Index 0 = +0 (unenhanced), index 1 = +1, etc.
/// Create via: Assets > Create > Equipment > Equipment Data
/// </summary>
[CreateAssetMenu(fileName = "NewEquipmentData", menuName = "Equipment/Equipment Data")]
public class EquipmentData : ScriptableObject
{
    [Header("Identity")]
    public string equipmentId;
    public string equipmentName;
    [TextArea] public string description;
    public Sprite icon;

    [Header("Plus Levels")]
    [Tooltip("Bonus points granted at each enhancement (+N) level. Index 0 = +0 (unenhanced), index 1 = +1, etc.")]
    public EquipmentPlusLevel[] plusLevels = new EquipmentPlusLevel[1] { new() };

    public int MaxPlusLevel => plusLevels.Length - 1;

    public EquipmentPlusLevel GetPlusLevel(int level) => plusLevels[Mathf.Clamp(level, 0, MaxPlusLevel)];
}
