using System.Collections.Generic;
using UnityEngine;

namespace BalloonFlow
{
    /// <summary>
    /// Generic object pool for reusable GameObjects.
    /// Supports configurable initial size and auto-expansion.
    /// </summary>
    /// <remarks>
    /// Layer: Core | Genre: Puzzle | Role: Pool | Phase: 0
    /// DB Reference: No DB match found — generated from L3 YAML logicFlow
    /// </remarks>
    public class ObjectPool
    {
        #region Fields

        private readonly GameObject _prefab;
        private readonly Transform _poolParent;
        private readonly Queue<GameObject> _available;
        private readonly HashSet<GameObject> _inUse;
        private readonly int _initialSize;
        private readonly bool _autoExpand;

        #endregion

        #region Properties

        /// <summary>
        /// Number of objects currently available in the pool.
        /// </summary>
        public int AvailableCount => _available.Count;

        /// <summary>
        /// Number of objects currently in use.
        /// </summary>
        public int InUseCount => _inUse.Count;

        /// <summary>
        /// Total objects managed by this pool.
        /// </summary>
        public int TotalCount => _available.Count + _inUse.Count;

        /// <summary>
        /// The prefab this pool instantiates.
        /// </summary>
        public GameObject Prefab => _prefab;

        #endregion

        #region Constructor

        /// <summary>
        /// Creates a new object pool.
        /// </summary>
        /// <param name="prefab">The prefab to pool.</param>
        /// <param name="initialSize">Number of objects to pre-instantiate.</param>
        /// <param name="poolParent">Parent transform for pooled objects.</param>
        /// <param name="autoExpand">Whether to create new objects when pool is empty.</param>
        public ObjectPool(GameObject prefab, int initialSize, Transform poolParent, bool autoExpand = true)
        {
            _prefab = prefab;
            _initialSize = initialSize;
            _poolParent = poolParent;
            _autoExpand = autoExpand;
            _available = new Queue<GameObject>(initialSize);
            _inUse = new HashSet<GameObject>();

            Prewarm();
        }

        #endregion

        #region Public Methods

        /// <summary>
        /// Gets an object from the pool.
        /// Returns null if pool is empty and auto-expand is disabled.
        /// </summary>
        public GameObject Get()
        {
            GameObject obj;

            if (_available.Count > 0)
            {
                obj = _available.Dequeue();
            }
            else if (_autoExpand)
            {
                obj = CreateInstance();
            }
            else
            {
                Debug.LogWarning($"[ObjectPool] Pool for '{_prefab.name}' is empty and auto-expand is disabled.");
                return null;
            }

            if (obj == null)
            {
                Debug.LogWarning($"[ObjectPool] Pooled object was destroyed externally. Creating replacement.");
                obj = CreateInstance();
            }

            obj.SetActive(true);
            _inUse.Add(obj);
            return obj;
        }

        /// <summary>
        /// Gets an object and positions it at the specified location.
        /// </summary>
        public GameObject Get(Vector3 position, Quaternion rotation)
        {
            GameObject obj = Get();
            if (obj != null)
            {
                obj.transform.SetPositionAndRotation(position, rotation);
            }
            return obj;
        }

        /// <summary>
        /// Returns an object to the pool.
        /// </summary>
        public void Return(GameObject obj)
        {
            if (obj == null)
            {
                return;
            }

            if (!_inUse.Remove(obj))
            {
                Debug.LogWarning($"[ObjectPool] Attempted to return object '{obj.name}' that is not tracked by this pool.");
                return;
            }

            obj.SetActive(false);
            // worldPositionStays=false: localScale/Position/Rotation 보존.
            // (worldPositionStays=true 가 기본이지만 UI 풀의 경우 Canvas 스케일 차이로
            //  매 cycle 마다 localScale이 누적 축소되는 버그 발생 — 토스트 클릭마다 작아지는 현상.)
            obj.transform.SetParent(_poolParent, false);
            _available.Enqueue(obj);
        }

        /// <summary>
        /// Returns all in-use objects to the pool.
        /// </summary>
        public void ReturnAll()
        {
            // Copy to avoid modification during iteration
            var inUseList = new List<GameObject>(_inUse);
            foreach (var obj in inUseList)
            {
                if (obj != null)
                {
                    Return(obj);
                }
            }
            _inUse.Clear();
        }

        /// <summary>
        /// Destroys all pooled objects and clears the pool.
        /// </summary>
        public void Clear()
        {
            foreach (var obj in _available)
            {
                if (obj != null)
                {
                    Object.Destroy(obj);
                }
            }

            foreach (var obj in _inUse)
            {
                if (obj != null)
                {
                    Object.Destroy(obj);
                }
            }

            _available.Clear();
            _inUse.Clear();
        }

        #endregion

        #region Private Methods

        private void Prewarm()
        {
            for (int i = 0; i < _initialSize; i++)
            {
                GameObject obj = CreateInstance();
                obj.SetActive(false);
                _available.Enqueue(obj);
            }
        }

        private GameObject CreateInstance()
        {
            GameObject obj = Object.Instantiate(_prefab, _poolParent);
            obj.name = $"{_prefab.name}_Pooled_{TotalCount}";
            return obj;
        }

        #endregion
    }
}
