using System;
using System.Collections.Generic;
using GameLogic.Core;
using UnityEngine;

/// <summary>
/// 오브젝트 풀링을 관리하는 싱글톤 Manager
/// </summary>
[DefaultExecutionOrder(-9999), DisallowMultipleComponent]
public sealed class PoolManager : SingletonBehaviour<PoolManager>
{
    public const string FallingBlockKey = "FallingBlock";

    [SerializeField] List<PoolContainer> pools = new List<PoolContainer>();

    readonly Dictionary<string, PoolContainer> _lookup = new Dictionary<string, PoolContainer>();

    protected override void Awake()
    {
        base.Awake();

        _lookup.Clear();
        foreach (var pool in pools)
        {
            if (string.IsNullOrEmpty(pool.key) || pool.prefab == null)
                continue;

            _lookup[pool.key] = pool;
            pool.prefab.Populate(Mathf.Max(1, pool.startCount));
        }
    }

    public void RegisterPool(string key, GameObject prefab, int startCount = 10)
    {
        if (string.IsNullOrEmpty(key) || prefab == null)
            return;

        var container = new PoolContainer
        {
            key = key,
            prefab = prefab,
            startCount = startCount
        };

        _lookup[key] = container;

        var exists = false;
        for (var i = 0; i < pools.Count; i++)
        {
            if (pools[i].key != key)
                continue;
            pools[i] = container;
            exists = true;
            break;
        }

        if (!exists)
            pools.Add(container);

        prefab.Populate(Mathf.Max(1, startCount));
    }

    public GameObject GetFromPool(string key, Vector3 position, Quaternion rotation)
    {
        if (!_lookup.TryGetValue(key, out var pool) || pool.prefab == null)
            return null;

        return pool.prefab.Reuse(position, rotation);
    }

    public void ReturnToPool(GameObject instance)
    {
        if (instance == null)
            return;

        instance.Release();
    }

    public void ReturnAll(string key)
    {
        if (!_lookup.TryGetValue(key, out var pool) || pool.prefab == null)
            return;

        var p = Pool.GetPoolByPrefab(pool.prefab, false);
        p?.ReleaseAllActive();
    }

    [Serializable]
    public struct PoolContainer
    {
        public string key;
        public GameObject prefab;
        [Min(1)] public int startCount;
    }
}
