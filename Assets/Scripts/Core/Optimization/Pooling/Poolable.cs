using System;
using UnityEngine;

namespace GameLogic.Core
{
    [DisallowMultipleComponent]
    public sealed class Poolable : MonoBehaviour
    {
        IPoolable[] _poolables = Array.Empty<IPoolable>();
        bool _isInitialized;

        void Awake()
        {
            _poolables = GetComponentsInChildren<IPoolable>(true);
            _isInitialized = true;
        }

        void OnDestroy()
        {
            Pool.Remove(gameObject);
        }

        public void OnReuse()
        {
            if (!_isInitialized)
                return;

            for (var i = 0; i < _poolables.Length; i++)
                _poolables[i].OnReuse();
        }

        public void OnRelease()
        {
            for (var i = 0; i < _poolables.Length; i++)
                _poolables[i].OnRelease();
        }
    }
}
