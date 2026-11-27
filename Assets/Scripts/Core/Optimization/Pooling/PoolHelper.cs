using UnityEngine;

namespace GameLogic.Core
{
    public static class PoolHelper
    {
        public static void Populate(this GameObject prefab, int count)
        {
            Pool.GetPoolByPrefab(prefab).Populate(count);
        }

        public static GameObject Reuse(this GameObject prefab, Vector3 position, Quaternion rotation)
        {
            return Pool.GetPoolByPrefab(prefab).Reuse(position, rotation);
        }

        public static void Release(this GameObject instance)
        {
            var isPooled = Pool.GetPoolByInstance(instance, out var pool);
            if (isPooled)
                pool.Release(instance);
            else
                Object.Destroy(instance);
        }
    }
}
