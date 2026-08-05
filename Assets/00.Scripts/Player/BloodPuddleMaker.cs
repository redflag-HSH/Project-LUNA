using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Pool;

public class BloodPuddleMaker : MonoBehaviour
{
    [Header("Normal Puddle")]
    public GameObject[] bloodPuddlePrefabs;

    [Header("Strong Puddle")]
    public GameObject[] strongPuddlePrefabs;

    [Header("Shared Settings")]
    public LayerMask groundLayer;
    public float groundRayLength = 10f;
    public float puddleLifetime = 5f;
    [Range(0f, 1f)]
    public float fadeStartRatio = 0.65f;
    public float scaleVariance = 0.3f;

    [Header("Pool")]
    public int poolSize = 15;

    ObjectPool<GameObject>[] _pools;
    ObjectPool<GameObject>[] _strongPools;

    readonly Dictionary<GameObject, SpriteRenderer> _srCache = new();
    readonly Dictionary<GameObject, ObjectPool<GameObject>> _objPool = new();

    // ── Unity ─────────────────────────────────────────────────────────────────

    void Awake()
    {
        _pools = BuildPools(bloodPuddlePrefabs);
        _strongPools = BuildPools(strongPuddlePrefabs);
    }

    // ── Public API ────────────────────────────────────────────────────────────

    public void SpawnPuddle(Vector2 position, int moneyValue = 0, float hpHeal = 0f) => Spawn(position, _pools, 1f, moneyValue, hpHeal);
    public void SpawnStrongPuddle(Vector2 position, int moneyValue = 0, float hpHeal = 0f) => Spawn(position, _strongPools, 1f, moneyValue, hpHeal);

    // ── Internal ──────────────────────────────────────────────────────────────

    void Spawn(Vector2 position, ObjectPool<GameObject>[] pools, float scaleMultiplier, int moneyValue = 0, float hpHeal = 0f)
    {
        if (pools == null || pools.Length == 0) return;

        var pool = pools[Random.Range(0, pools.Length)];
        GameObject obj = pool.Get();

        if (obj.TryGetComponent<BloodPond>(out var pond))
        {
            if (moneyValue > 0) pond.moneyValue = moneyValue;
            if (hpHeal > 0f) pond.hpHeal = hpHeal;
        }

        RaycastHit2D groundHit = Physics2D.Raycast(position, Vector2.down, groundRayLength, groundLayer);
        Vector2 spawnPos = groundHit.collider != null ? groundHit.point : position;

        obj.transform.position = new Vector3(spawnPos.x, spawnPos.y, 0.5f);
        obj.transform.rotation = Quaternion.Euler(0f, 0f, Random.Range(0f, 360f));
        obj.transform.localScale = Vector3.one * scaleMultiplier * (1f + Random.Range(-scaleVariance, scaleVariance));

        _srCache.TryGetValue(obj, out var sr);
        if (sr != null)
        {
            Color c = sr.color; c.a = 1f; sr.color = c;
        }

        StartCoroutine(FadeAndReturn(obj));
    }

    ObjectPool<GameObject>[] BuildPools(GameObject[] prefabs)
    {
        if (prefabs == null || prefabs.Length == 0) return null;

        var pools = new ObjectPool<GameObject>[prefabs.Length];
        for (int i = 0; i < prefabs.Length; i++)
        {
            var prefab = prefabs[i];
            var pool = default(ObjectPool<GameObject>);
            pool = new ObjectPool<GameObject>(
                createFunc: () =>
                {
                    GameObject obj = Instantiate(prefab);
                    _srCache[obj] = obj.GetComponent<SpriteRenderer>();
                    _objPool[obj] = pool;
                    if (obj.TryGetComponent<BloodPond>(out var pond))
                        pond.onComplete = () => { if (_objPool.TryGetValue(obj, out var p)) p.Release(obj); };
                    obj.SetActive(false);
                    return obj;
                },
                actionOnGet: obj =>
                {
                    if (obj == null) return;
                    if (obj.TryGetComponent<BloodPond>(out var pond)) pond.ResetState();
                    obj.SetActive(true);
                },
                actionOnRelease: obj => obj.SetActive(false),
                actionOnDestroy: obj => { _srCache.Remove(obj); _objPool.Remove(obj); Destroy(obj); },
                collectionCheck: false,
                defaultCapacity: Mathf.Max(1, poolSize / prefabs.Length),
                maxSize: poolSize
            );
            pools[i] = pool;
        }
        return pools;
    }

    // ── Fade coroutine ────────────────────────────────────────────────────────

    IEnumerator FadeAndReturn(GameObject obj)
    {
        float elapsed = 0f;
        float fadeStart = puddleLifetime * fadeStartRatio;

        _srCache.TryGetValue(obj, out var sr);
        Color startColor = sr != null ? sr.color : Color.white;

        while (elapsed < puddleLifetime && obj.activeInHierarchy)
        {
            elapsed += Time.deltaTime;

            if (sr != null && elapsed > fadeStart)
            {
                float t = (elapsed - fadeStart) / (puddleLifetime - fadeStart);
                Color c = startColor;
                c.a = Mathf.Lerp(1f, 0f, t);
                sr.color = c;
            }

            yield return null;
        }

        if (obj != null && obj.activeSelf && _objPool.TryGetValue(obj, out var pool))
            pool.Release(obj);
    }
}
