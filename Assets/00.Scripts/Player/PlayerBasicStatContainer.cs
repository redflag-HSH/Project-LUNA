using UnityEngine;

public class PlayerBasicStatContainer : MonoBehaviour
{
    public static PlayerBasicStatContainer Instance { get; private set; }

    private int _attackPoint = 30;
    private int _plusAttackPoint;
    private int _defendPoint = 6;
    private int _plusDefendPoint;
    private int _healthPoint = 100;
    private int _plusHealthPoint;
    private int _staminaPoint = 100;
    private int _plusStaminaPoint;
    private int _moveSpeedPoint = 20;
    private int _plusMoveSpeedPoint;
    private int _attackSpeedPoint = 10;
    private int _plusAttackSpeedPoint;
    private int _luckPoint = 5;
    private int _plusLuckPoint;

    public int AttackPoint => _attackPoint + _plusAttackPoint;
    public int DefendPoint => _defendPoint + _plusDefendPoint;
    public int HealthPoint => _healthPoint + _plusHealthPoint;
    public int StaminaPoint => _staminaPoint + _plusStaminaPoint;
    public int MoveSpeedPoint => _moveSpeedPoint + (int)(_plusMoveSpeedPoint * .1f);
    public int AttackSpeedPoint => _attackSpeedPoint + (int)(_plusAttackSpeedPoint * .1f);
    public int LuckPoint => _luckPoint + (int)(_plusLuckPoint * .1f);

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
    }

    /// <summary>Overwrites the equipment-derived bonus on every basic point. Called by
    /// PlayerEquipment whenever equipped items or their plus levels change.</summary>
    public void SetEquipmentBonuses(int attack, int defend, int health, int stamina, int moveSpeed, int attackSpeed, int luck)
    {
        _plusAttackPoint = attack;
        _plusDefendPoint = defend;
        _plusHealthPoint = health;
        _plusStaminaPoint = stamina;
        _plusMoveSpeedPoint = moveSpeed;
        _plusAttackSpeedPoint = attackSpeed;
        _plusLuckPoint = luck;
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }
}
