using System.Collections.Generic;
using UnityEngine;

namespace BalloonFlow
{
    /// <summary>
    /// Persistent resource manager. Loads assets from Resources/ folder
    /// and integrates with ObjectPoolManager for pooled instantiation.
    /// </summary>
    /// <remarks>
    /// Layer: Core | Genre: Puzzle | Role: Manager | Phase: 0
    /// </remarks>
    public class ResourceManager : Singleton<ResourceManager>
    {
        #region Fields

        private readonly Dictionary<string, Object> _cache = new Dictionary<string, Object>();

        #endregion

        #region Public Methods

        /// <summary>
        /// Loads an asset from Resources/ with caching.
        /// </summary>
        public T Load<T>(string path) where T : Object
        {
            if (_cache.TryGetValue(path, out Object cached))
            {
                return cached as T;
            }

            T asset = Resources.Load<T>(path);
            if (asset != null)
            {
                _cache[path] = asset;
            }
            else
            {
                Debug.LogWarning($"[ResourceManager] Asset not found: {path}");
            }

            return asset;
        }

        /// <summary>
        /// Loads a prefab and instantiates it.
        /// </summary>
        public GameObject Instantiate(string prefabPath, Vector3 position, Quaternion rotation)
        {
            GameObject prefab = Load<GameObject>(prefabPath);
            if (prefab == null) return null;
            return Object.Instantiate(prefab, position, rotation);
        }

        /// <summary>
        /// Instantiates a prefab under a parent transform.
        /// </summary>
        public GameObject Instantiate(string prefabPath, Transform parent)
        {
            GameObject prefab = Load<GameObject>(prefabPath);
            if (prefab == null) return null;
            return Object.Instantiate(prefab, parent);
        }

        /// <summary>
        /// Gets or creates an object from the pool. Falls back to Resources.Load if pool has no prefab registered.
        /// </summary>
        public GameObject GetPooled(string poolKey)
        {
            if (ObjectPoolManager.HasInstance)
            {
                return ObjectPoolManager.Instance.Get(poolKey);
            }

            Debug.LogWarning($"[ResourceManager] ObjectPoolManager not available for key '{poolKey}'. Using direct instantiate.");
            return Instantiate($"Prefabs/{poolKey}", Vector3.zero, Quaternion.identity);
        }

        /// <summary>
        /// Returns an object to the pool.
        /// </summary>
        public void ReturnPooled(string poolKey, GameObject obj)
        {
            if (ObjectPoolManager.HasInstance)
            {
                ObjectPoolManager.Instance.Return(poolKey, obj);
                return;
            }

            if (obj != null) Destroy(obj);
        }

        /// <summary>
        /// Clears the asset cache, releasing references.
        /// </summary>
        public void ClearCache()
        {
            _cache.Clear();
        }

        /// <summary>
        /// Removes a specific asset from the cache.
        /// </summary>
        public void Unload(string path)
        {
            _cache.Remove(path);
        }

        #endregion
    }
}
