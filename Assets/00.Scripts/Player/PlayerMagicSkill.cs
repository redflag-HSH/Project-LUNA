using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class PlayerMagicSkill : MonoBehaviour
{
    public enum MagicSkillType
    {
        BloodSpear,
        Drain,
        Hedgehog,
        Eclipse,
        DaughterOfDragon,
        SteelBlood,
        HeatBlood,
        ColdBlood,
    }

    [Header("Magic Skill Level System")]
    public MagicSkillLevelData magicSkillLevelData;
    [Min(0)] public int bloodSpearLevel = 0;
    [Min(0)] public int drainLevel = 0;
    [Min(0)] public int hedgehogLevel = 0;
    [Min(0)] public int eclipseLevel = 0;
    [Min(0)] public int daughterOfDragonLevel = 0;
    [Min(0)] public int steelBloodLevel = 0;
    [Min(0)] public int heatBloodLevel  = 0;
    [Min(0)] public int coldBloodLevel  = 0;

    [Header("Current Magic Skill")]
    public MagicSkillType currentMagicSkill = MagicSkillType.BloodSpear;

    [Header("Blood Spear")]
    public GameObject bloodSpearPrefab;
    public int spearCount = 5;
    public float hoverRadius = 1.2f;
    public float spearDamage = 30f;
    [Tooltip("Blood cost consumed per spear as it is summoned.")]
    public float bloodCostPerSpear = 10f;
    [Tooltip("Seconds between each spear being summoned while holding.")]
    public float spearInterval = 0.25f;
    [Tooltip("Hard cap on total spears that can orbit at once.")]
    public int maxSpearCount = 10;

    [Header("Drain")]
    public float drainRange = 3f;
    public float drainDamage = 20f;
    [Tooltip("HP healed = total damage dealt × this ratio.")]
    public float drainHealRatio = 0.5f;
    public float drainBloodCost = 25f;
    public float drainCooldown = 2.0f;
    [Tooltip("Max number of enemies affected. Closest ones are picked first.")]
    public int drainTargetCount = 3;

    [Header("Hedgehog")]
    public float hedgehogRange = 4f;
    public float hedgehogDamage = 25f;
    public float hedgehogBloodCost = 20f;
    public float hedgehogCooldown = 3f;
    public float hedgehogSpikeSpeed = 12f;
    public float hedgehogSpikeLifetime = 2f;

    // ── Eclipse ── (stats TBD)
    // ── Daughter of Dragon ── (stats TBD)
    // ── Steel Blood ── (stats TBD)
    // ── Heat Blood ── (stats TBD)
    // ── Cold Blood ── (stats TBD)

    private float _drainCooldownTimer;
    private float _hedgehogCooldownTimer;
    private PlayerControl _player;
    private EffectGenerator _effects;
    private readonly List<BloodSpear> _activeSpears = new();
    private Coroutine _holdCoroutine;
    private PlayerBasicStatContainer _basicStats;

    void Awake()
    {
        _player = GetComponent<PlayerControl>();
        _effects = GetComponent<EffectGenerator>();
        _basicStats = GetComponent<PlayerBasicStatContainer>();
        if (magicSkillLevelData != null) ApplyMagicLevels();
    }

    private float ScaleOutgoingDamage(float baseDamage)
    {
        float result = baseDamage;
        if (PlayerDebuffer.Instance != null) result *= PlayerDebuffer.Instance.OutgoingDamageMultiplier;
        if (_basicStats != null)
        {
            result *= 1f + _basicStats.AttackPoint / 100f;
            if (Random.Range(0f, 100f) < _basicStats.LuckPoint) result *= 2f;
        }
        return result;
    }

    // ── Magic Skill Level API ─────────────────────────────────────────────────

    public void ApplyMagicLevels()
    {
        ApplyBloodSpearLevel(bloodSpearLevel);
        ApplyDrainLevel(drainLevel);
        ApplyHedgehogLevel(hedgehogLevel);
        ApplyEclipseLevel(eclipseLevel);
        ApplyDaughterOfDragonLevel(daughterOfDragonLevel);
        ApplySteelBloodLevel(steelBloodLevel);
        ApplyHeatBloodLevel(heatBloodLevel);
        ApplyColdBloodLevel(coldBloodLevel);
    }

    public void ApplyBloodSpearLevel(int level)
    {
        if (magicSkillLevelData == null) return;
        bloodSpearLevel = Mathf.Clamp(level, 0, magicSkillLevelData.MaxBloodSpearLevel);
        var d = magicSkillLevelData.GetBloodSpear(bloodSpearLevel);
        spearCount = d.spearCount;
        hoverRadius = d.hoverRadius;
        spearDamage = d.spearDamage;
        bloodCostPerSpear = d.bloodCostPerSpear;
        spearInterval = d.spearInterval;
        maxSpearCount = d.maxSpearCount;
    }

    public void ApplyDrainLevel(int level)
    {
        if (magicSkillLevelData == null) return;
        drainLevel = Mathf.Clamp(level, 0, magicSkillLevelData.MaxDrainLevel);
        var d = magicSkillLevelData.GetDrain(drainLevel);
        drainRange = d.drainRange;
        drainDamage = d.drainDamage;
        drainHealRatio = d.drainHealRatio;
        drainBloodCost = d.drainBloodCost;
        drainCooldown = d.drainCooldown;
        drainTargetCount = d.drainTargetCount;
    }

    public void ApplyHedgehogLevel(int level)
    {
        if (magicSkillLevelData == null) return;
        hedgehogLevel = Mathf.Clamp(level, 0, magicSkillLevelData.MaxHedgehogLevel);
        var d = magicSkillLevelData.GetHedgehog(hedgehogLevel);
        hedgehogRange = d.hedgehogRange;
        hedgehogDamage = d.hedgehogDamage;
        hedgehogBloodCost = d.hedgehogBloodCost;
        hedgehogCooldown = d.hedgehogCooldown;
        hedgehogSpikeSpeed = d.hedgehogSpikeSpeed;
        hedgehogSpikeLifetime = d.hedgehogSpikeLifetime;
    }

    public void ApplyEclipseLevel(int level)
    {
        if (magicSkillLevelData == null) return;
        eclipseLevel = Mathf.Clamp(level, 0, magicSkillLevelData.MaxEclipseLevel);
        // TODO: apply EclipseLevel stats
    }

    public void ApplyDaughterOfDragonLevel(int level)
    {
        if (magicSkillLevelData == null) return;
        daughterOfDragonLevel = Mathf.Clamp(level, 0, magicSkillLevelData.MaxDaughterOfDragonLevel);
        // TODO: apply DaughterOfDragonLevel stats
    }

    public void ApplySteelBloodLevel(int level)
    {
        if (magicSkillLevelData == null) return;
        steelBloodLevel = Mathf.Clamp(level, 0, magicSkillLevelData.MaxSteelBloodLevel);
        // TODO: apply SteelBloodLevel stats
    }

    public void ApplyHeatBloodLevel(int level)
    {
        if (magicSkillLevelData == null) return;
        heatBloodLevel = Mathf.Clamp(level, 0, magicSkillLevelData.MaxHeatBloodLevel);
        // TODO: apply HeatBloodLevel stats
    }

    public void ApplyColdBloodLevel(int level)
    {
        if (magicSkillLevelData == null) return;
        coldBloodLevel = Mathf.Clamp(level, 0, magicSkillLevelData.MaxColdBloodLevel);
        // TODO: apply ColdBloodLevel stats
    }

    void Update()
    {
        if (_drainCooldownTimer > 0f) _drainCooldownTimer -= Time.deltaTime;
        if (_hedgehogCooldownTimer > 0f) _hedgehogCooldownTimer -= Time.deltaTime;
    }

    public void OnMagicSkillPress()
    {
        switch (currentMagicSkill)
        {
            case MagicSkillType.BloodSpear: _holdCoroutine = StartCoroutine(BloodSpearHoldCoroutine()); break;
            case MagicSkillType.Drain: UseDrain(); break;
            case MagicSkillType.Hedgehog: UseHedgehog(); break;
            case MagicSkillType.Eclipse: UseEclipse(); break;
            case MagicSkillType.DaughterOfDragon: UseDaughterOfDragon(); break;
            case MagicSkillType.SteelBlood: UseSteelBlood(); break;
            case MagicSkillType.HeatBlood:  UseHeatBlood();  break;
            case MagicSkillType.ColdBlood:  UseColdBlood();  break;
        }
    }

    public void OnMagicSkillRelease()
    {
        if (_holdCoroutine != null)
        {
            StopCoroutine(_holdCoroutine);
            _holdCoroutine = null;
        }

        switch (currentMagicSkill)
        {
            case MagicSkillType.BloodSpear: BloodSpearRelease(); break;
        }
    }

    // ── Drain ─────────────────────────────────────────────────────────────────

    private void UseDrain()
    {
        if (_drainCooldownTimer > 0f && !_player.IsBerserker) return;
        if (!_player.ConsumeBloodGage(drainBloodCost)) return;
        if (!_player.IsBerserker) _drainCooldownTimer = drainCooldown;
        StartCoroutine(DrainCoroutine());
    }

    private IEnumerator DrainCoroutine()
    {
        Collider2D[] hits = Physics2D.OverlapCircleAll(transform.position, drainRange, _player.enemyLayer);

        // Sort by distance, then take only drainTargetCount closest
        System.Array.Sort(hits, (a, b) =>
            Vector2.SqrMagnitude(a.transform.position - transform.position)
            .CompareTo(Vector2.SqrMagnitude(b.transform.position - transform.position)));

        float totalDamage = 0f;
        int affected = 0;

        foreach (var col in hits)
        {
            if (affected >= drainTargetCount) break;

            if (col.TryGetComponent<IDamageable>(out var target))
            {
                float scaledDrainDamage = ScaleOutgoingDamage(drainDamage);
                target.TakeDamage(scaledDrainDamage);
                totalDamage += scaledDrainDamage;
            }

            if (col.TryGetComponent<IMonsterCore>(out var mm))
                mm.ApplyBloodloss(_player.bleedDps, _player.bleedDuration);

            affected++;
        }

        if (totalDamage > 0f)
            _player.Heal(totalDamage * drainHealRatio);

        yield break;
    }

    // ── Blood Spear ───────────────────────────────────────────────────────────

    private IEnumerator BloodSpearHoldCoroutine()
    {
        _activeSpears.RemoveAll(s => s == null);

        for (int i = 0; i < spearCount; i++)
        {
            if (_activeSpears.Count >= maxSpearCount) break;
            if (!_player.ConsumeBloodGage(bloodCostPerSpear)) break;

            float angle = 360f / maxSpearCount * _activeSpears.Count * Mathf.Deg2Rad;
            Vector2 offset = new(Mathf.Cos(angle) * hoverRadius, Mathf.Sin(angle) * hoverRadius);

            GameObject spear = Instantiate(bloodSpearPrefab, (Vector2)transform.position + offset, Quaternion.identity);
            if (spear.TryGetComponent<BloodSpear>(out var bs))
            {
                bs.InitHold(transform, offset, _player.FacingDir(), ScaleOutgoingDamage(spearDamage), _player.enemyLayer, _player.bloodPuddleMaker);
                _activeSpears.Add(bs);
            }

            yield return new WaitForSeconds(spearInterval);
        }

        _holdCoroutine = null;
    }

    private void BloodSpearRelease()
    {
        _activeSpears.RemoveAll(s => s == null);
        foreach (var spear in _activeSpears)
            spear.Fire();
        _activeSpears.Clear();
    }

    // ── Hedgehog ──────────────────────────────────────────────────────────────

    private void UseHedgehog()
    {
        if (_hedgehogCooldownTimer > 0f && !_player.IsBerserker) return;
        if (!_player.ConsumeBloodGage(hedgehogBloodCost)) return;
        if (!_player.IsBerserker) _hedgehogCooldownTimer = hedgehogCooldown;

        Vector2 origin = transform.position;
        Collider2D[] hits = Physics2D.OverlapCircleAll(origin, hedgehogRange, _player.enemyLayer);

        foreach (var col in hits)
        {
            if (col.TryGetComponent<IDamageable>(out var target))
                target.TakeDamage(ScaleOutgoingDamage(hedgehogDamage));

            _effects?.SpawnHedgehogSpike(origin, col.bounds.center, hedgehogSpikeSpeed);
        }
    }

    // ── Eclipse ───────────────────────────────────────────────────────────────

    private void UseEclipse() { }

    // ── Daughter of Dragon ────────────────────────────────────────────────────

    private void UseDaughterOfDragon() { }

    // ── Steel Blood ───────────────────────────────────────────────────────────

    private void UseSteelBlood() { }

    // ── Heat Blood ────────────────────────────────────────────────────────────

    private void UseHeatBlood() { }

    // ── Cold Blood ────────────────────────────────────────────────────────────

    private void UseColdBlood() { }
}
