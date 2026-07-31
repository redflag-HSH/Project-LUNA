using UnityEngine;

public class PlayerBasicStatContainer : MonoBehaviour
{
    public static PlayerBasicStatContainer Instance { get; private set; }

    //30+N
    private int _attackPoint;
    //6+N
    private int _defendPoint;
    //100+N
    private int _HealthPoint;
    //100+N
    private int _staminaPoint;
    //20+0.1*N
    private int _moveSpeedPoint;
    //10+0.1*N
    private int _attackSpeedPoint;
    //5+0.1*N
    private int _luckPoint;

    public int AttackPoint => _attackPoint;
    public int DefendPoint => _defendPoint;
    public int HealthPoint => _HealthPoint;
    public int StaminaPoint => _staminaPoint;
    public int MoveSpeedPoint => _moveSpeedPoint;
    public int AttackSpeedPoint => _attackSpeedPoint;
    public int LuckPoint => _luckPoint;

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }
}
