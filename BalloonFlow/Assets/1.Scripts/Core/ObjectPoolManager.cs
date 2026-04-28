using System.Collections.Generic;
using UnityEngine;

namespace BalloonFlow
{
    /// <summary>
    /// Central registry for multiple ObjectPools, keyed by string identifier.
    /// Use this to manage pools for balloons, darts, effects, etc.
    /// </summary>
    /// <remarks>
    /// Layer: Core | Genre: Puzzle | Role: Manager | Phase: 0
    /// DB Reference: No DB match found — generated from L3 YAML logicFlow
    /// </remarks>
    public class ObjectPoolManager : Singleton<ObjectPoolManager>
    {
        #region Serialized Fields

        [System.Serializable]
        public struct PoolDefinition
        {
            public string key;
            public GameObject prefab;
            public int initialSize;
            public bool autoExpand;
        }

        [SerializeField] private PoolDefinition[] _poolDefinitions;

        #endregion

        #region Fields

        private readonly Dictionary<string, ObjectPool> _pools = new Dictionary<string, ObjectPool>();
        private Transform _poolRoot;

        #endregion

        #region Lifecycle

        protected override void OnSingletonAwake()
        {
            _poolRoot = new GameObject("_PoolRoot").transform;
            _poolRoot.SetParent(transform);

            bool hasDefinitions = false;

            if (_poolDefinitions != null)
            {
                foreach (var def in _poolDefinitions)
                {
                    if (!string.IsNullOrEmpty(def.key) && def.prefab != null)
                    {
                        CreatePool(def.key, def.prefab, def.initialSize, def.autoExpand);
                        hasDefinitions = true;
                    }
                }
            }

            // Fallback: auto-register pools from Resources/Prefabs/ if no definitions wired
            if (!hasDefinitions)
            {
                AutoRegisterFromResources();
            }
        }

        #endregion

        #region Public Methods

        /// <summary>
        /// Creates and registers a new pool.
        /// </summary>
        public void CreatePool(string key, GameObject prefab, int initialSize, bool autoExpand = true)
        {
            if (string.IsNullOrEmpty(key))
            {
                Debug.LogError("[ObjectPoolManager] Pool key cannot be null or empty.");
                return;
            }

            if (_pools.ContainsKey(key))
            {
                Debug.LogWarning($"[ObjectPoolManager] Pool '{key}' already exists. Skipping.");
                return;
            }

            if (prefab == null)
            {
                Debug.LogError($"[ObjectPoolManager] Prefab for pool '{key}' is null.");
                return;
            }

            Transform parent = new GameObject($"Pool_{key}").transform;
            parent.SetParent(_poolRoot);

            var pool = new ObjectPool(prefab, initialSize, parent, autoExpand);
            _pools.Add(key, pool);
        }

        /// <summary>
        /// Gets an object from the specified pool.
        /// </summary>
        public GameObject Get(string key)
        {
            if (_pools.TryGetValue(key, out ObjectPool pool))
            {
                return pool.Get();
            }

            Debug.LogError($"[ObjectPoolManager] Pool '{key}' not found.");
            return null;
        }

        /// <summary>
        /// Gets an object from the specified pool at a position and rotation.
        /// </summary>
        public GameObject Get(string key, Vector3 position, Quaternion rotation)
        {
            if (_pools.TryGetValue(key, out ObjectPool pool))
            {
                return pool.Get(position, rotation);
            }

            Debug.LogError($"[ObjectPoolManager] Pool '{key}' not found.");
            return null;
        }

        /// <summary>
        /// Returns an object to the specified pool.
        /// </summary>
        public void Return(string key, GameObject obj)
        {
            if (_pools.TryGetValue(key, out ObjectPool pool))
            {
                pool.Return(obj);
                return;
            }

            Debug.LogError($"[ObjectPoolManager] Pool '{key}' not found. Cannot return '{obj?.name}'.");
        }

        /// <summary>
        /// Returns all in-use objects for a specific pool.
        /// </summary>
        public void ReturnAll(string key)
        {
            if (_pools.TryGetValue(key, out ObjectPool pool))
            {
                pool.ReturnAll();
                return;
            }

            Debug.LogWarning($"[ObjectPoolManager] Pool '{key}' not found.");
        }

        /// <summary>
        /// Returns all objects across all pools.
        /// </summary>
        public void ReturnAllPools()
        {
            foreach (var kvp in _pools)
            {
                kvp.Value.ReturnAll();
            }
        }

        /// <summary>
        /// Whether a pool with the given key exists.
        /// </summary>
        public bool HasPool(string key)
        {
            return _pools.ContainsKey(key);
        }

        /// <summary>
        /// Gets the pool info for debugging.
        /// </summary>
        public (int available, int inUse) GetPoolInfo(string key)
        {
            if (_pools.TryGetValue(key, out ObjectPool pool))
            {
                return (pool.AvailableCount, pool.InUseCount);
            }
            return (0, 0);
        }

        /// <summary>
        /// Clears and destroys all pools.
        /// </summary>
        public void ClearAll()
        {
            foreach (var kvp in _pools)
            {
                kvp.Value.Clear();
            }
            _pools.Clear();
        }

        #endregion

        #region Private Methods

        /// <summary>
        /// Loads prefabs from Resources/Prefabs/ and auto-registers pools.
        /// Used as fallback when no PoolDefinition[] is wired via Inspector.
        /// </summary>
        private void AutoRegisterFromResources()
        {
            var defaultPools = new[]
            {
                new { key = "Balloon",     path = "Prefabs/Balloon",     initialSize = 50 },
                new { key = "Dart",        path = "Prefabs/Dart",        initialSize = 30 },
                new { key = "Holder",      path = "Prefabs/Holder",      initialSize = 20 },
                new { key = "Spawner",     path = "Prefabs/Spawner",     initialSize = 5 },
                new { key = "Key",         path = "Prefabs/Key",         initialSize = 5 },
                new { key = "Lock",        path = "Prefabs/Lock",        initialSize = 5 },
                // Gimmick visual variants (Lv.91+ unlock content)
                new { key = "Baricade",    path = "Prefabs/Baricade",    initialSize = 5 },  // Barricade gimmick (destructible wall)
                new { key = "IronBox",     path = "Prefabs/IronBox",     initialSize = 5 },  // Pinata_Box gimmick (Lv.161)
                new { key = "WoodenBoard", path = "Prefabs/WoodenBoard", initialSize = 8 },  // Pin gimmick (Lv.61, 1×N progressive)
                new { key = "FrozenLayer", path = "Prefabs/FrozenLayer", initialSize = 10 }, // Ice/Frozen_Dart overlay
            };

            foreach (var entry in defaultPools)
            {
                GameObject prefab = Resources.Load<GameObject>(entry.path);
                if (prefab != null)
                {
                    CreatePool(entry.key, prefab, entry.initialSize, true);
                    Debug.Log($"[ObjectPoolManager] Auto-registered pool '{entry.key}' from Resources/{entry.path}.");
                }
                else
                {
                    Debug.LogWarning($"[ObjectPoolManager] Prefab not found at Resources/{entry.path}. Pool '{entry.key}' not created.");
                }
            }
        }

        #endregion
    }
}
