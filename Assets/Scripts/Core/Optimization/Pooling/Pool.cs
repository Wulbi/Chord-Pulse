using System.Collections.Generic;
using UnityEngine;

namespace GameLogic.Core
{
    public sealed class Pool
    {
        GameObject _source;
        Poolable _prototype;
        Stack<Poolable> _instances;
        List<GameObject> _allInstances;
        readonly Quaternion _rotation;
        readonly Vector3 _scale;
        readonly bool _prototypeIsNotSource;

        static readonly Dictionary<GameObject, Pool> PrefabLookup = new Dictionary<GameObject, Pool>(64);
        static readonly Dictionary<GameObject, Pool> InstanceLookup = new Dictionary<GameObject, Pool>(512);

        const int InitialSize = 128;

        static Transform _cacheObject;
        static Transform CacheTransform
        {
            get
            {
                if (_cacheObject == null)
                    _cacheObject = new GameObject("PoolCenter").transform;
                return _cacheObject;
            }
        }

        static Transform _sourceObject;
        static Transform SourceTransform
        {
            get
            {
                if (_sourceObject == null)
                {
                    _sourceObject = new GameObject("PoolSources").transform;
                    Object.DontDestroyOnLoad(_sourceObject.gameObject);
                }

                return _sourceObject;
            }
        }

        public Pool(GameObject prefab)
        {
            _source = prefab;
            _prototype = prefab.GetComponent<Poolable>();

            if (_prototype == null)
            {
                _prototype = Object.Instantiate(prefab, SourceTransform).AddComponent<Poolable>();
                _prototype.name = prefab.name;
                _prototype.gameObject.SetActive(false);
                _prototypeIsNotSource = true;
            }

            _instances = new Stack<Poolable>(InitialSize);
            _allInstances = new List<GameObject>(InitialSize);
            PrefabLookup.Add(_source, this);

            var transform = prefab.transform;
            _rotation = transform.rotation;
            _scale = transform.localScale;
        }

        public static Pool GetPoolByPrefab(GameObject prefab, bool create = true)
        {
            var hasPool = PrefabLookup.TryGetValue(prefab, out var pool);
            if (!hasPool && create)
                pool = new Pool(prefab);
            return pool;
        }

        public static bool GetPoolByInstance(GameObject instance, out Pool pool)
        {
            return InstanceLookup.TryGetValue(instance, out pool);
        }

        public static void Remove(GameObject instance)
        {
            InstanceLookup.Remove(instance);
        }

        public void Populate(int count)
        {
            for (var i = 0; i < count; i++)
            {
                var instance = CreateInstance();
                instance.gameObject.SetActive(false);
                _instances.Push(instance);
            }
        }

        public void Clear(bool destroyActive)
        {
            PrefabLookup.Remove(_source);

            foreach (var instance in _allInstances)
            {
                if (instance == null)
                    continue;

                InstanceLookup.Remove(instance);

                if (!destroyActive && instance.activeInHierarchy)
                    continue;

                Object.Destroy(instance);
            }

            if (_prototypeIsNotSource)
                Object.Destroy(_prototype.gameObject);

            _source = null;
            _prototype = null;
            _instances = null;
            _allInstances = null;
        }

        public GameObject Reuse()
        {
            return GetInstance().gameObject;
        }

        public GameObject Reuse(Transform parent)
        {
            var instance = GetInstance();
            instance.transform.SetParent(parent);
            return instance.gameObject;
        }

        public GameObject Reuse(Transform parent, bool worldPositionStays)
        {
            var instance = GetInstance();
            instance.transform.SetParent(parent, worldPositionStays);
            return instance.gameObject;
        }

        public GameObject Reuse(Vector3 position, Quaternion rotation)
        {
            var instance = GetInstance();
            instance.transform.SetPositionAndRotation(position, rotation);
            return instance.gameObject;
        }

        public GameObject Reuse(Vector3 position, Quaternion rotation, Transform parent)
        {
            var instance = GetInstance();
            var instanceTransform = instance.transform;
            instanceTransform.SetPositionAndRotation(position, rotation);
            instanceTransform.SetParent(parent);
            return instance.gameObject;
        }

        public void Release(GameObject instance)
        {
            var poolable = instance.GetComponent<Poolable>();
            poolable.OnRelease();

            instance.SetActive(false);

            var instanceTransform = instance.transform;
            instanceTransform.SetParent(CacheTransform);
            instanceTransform.rotation = _rotation;
            instanceTransform.localScale = _scale;

            _instances.Push(poolable);
        }

        public void ReleaseAllActive()
        {
            for (var i = _allInstances.Count - 1; i >= 0; i--)
            {
                var instance = _allInstances[i];
                if (instance != null && instance.activeInHierarchy)
                    Release(instance);
            }
        }

        Poolable GetInstance()
        {
            var count = _instances.Count;

            if (count != 0)
            {
                var instance = _instances.Pop();

                if (instance == null)
                {
                    count--;
                    while (count != 0)
                    {
                        instance = _instances.Pop();
                        if (instance != null)
                        {
                            instance.OnReuse();
                            instance.gameObject.SetActive(true);
                            return instance;
                        }

                        count--;
                    }

                    instance = CreateInstance();
                    instance.gameObject.SetActive(true);
                    return instance;
                }

                instance.OnReuse();
                instance.gameObject.SetActive(true);
                return instance;
            }

            var created = CreateInstance();
            created.gameObject.SetActive(true);
            return created;
        }

        Poolable CreateInstance()
        {
            var instance = Object.Instantiate(_prototype, CacheTransform);
            var instanceGameObject = instance.gameObject;
            instance.name = $"{_prototype.name}[{_allInstances.Count}]";

            InstanceLookup.Add(instanceGameObject, this);
            _allInstances.Add(instanceGameObject);
            return instance;
        }
    }
}
