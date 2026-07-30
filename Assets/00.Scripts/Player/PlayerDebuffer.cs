using UnityEngine;

public class PlayerDebuffer : MonoBehaviour
{
    public static PlayerDebuffer Instance { get; private set; }

    [Header("Day / Night Hours")]
    [SerializeField] int dayStartHour = 8;    // inclusive
    [SerializeField] int nightStartHour = 18; // inclusive

    [Header("Outgoing Damage (player -> monster)")]
    [SerializeField] float dayOutgoingDamageMultiplier = 0.7f;
    [SerializeField] float nightOutgoingDamageMultiplier = 1f;

    [Header("Basic Stat (placeholder, not wired to any stat yet)")]
    [SerializeField] float dayBasicStatMultiplier = 0.5f;
    [SerializeField] float nightBasicStatMultiplier = 1f;

    public bool IsDaytime { get; private set; } = true;

    public float OutgoingDamageMultiplier => IsDaytime ? dayOutgoingDamageMultiplier : nightOutgoingDamageMultiplier;
    public float BasicStatMultiplier => IsDaytime ? dayBasicStatMultiplier : nightBasicStatMultiplier;
    public bool TickDamageActive => IsDaytime;

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        DontDestroyOnLoad(gameObject);
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    void Update()
    {
        int hour = TimeSystem.Instance != null ? TimeSystem.Instance.Hour : dayStartHour;
        IsDaytime = hour >= dayStartHour && hour < nightStartHour;
    }
}
