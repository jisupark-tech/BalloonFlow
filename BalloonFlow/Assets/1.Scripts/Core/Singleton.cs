using UnityEngine;

namespace BalloonFlow
{
    /// <summary>
    /// Generic singleton base for MonoBehaviour managers.
    /// Ensures a single instance with DontDestroyOnLoad.
    /// </summary>
    /// <remarks>
    /// Layer: Core | Genre: Puzzle | Role: Helper | Phase: 0
    /// DB Reference: No DB match found — generated from L3 YAML logicFlow
    /// </remarks>
    public abstract class Singleton<T> : MonoBehaviour where T : MonoBehaviour
    {
        private static T _instance;
        private static readonly object _lock = new object();
        private static bool _applicationIsQuitting;

        /// <summary>
        /// The singleton instance. Returns null if application is quitting.
        /// </summary>
        public static T Instance
        {
            get
            {
                if (_applicationIsQuitting)
                {
                    Debug.LogWarning(
                        $"[Singleton] Instance of {typeof(T)} already destroyed on application quit. Returning null.");
                    return null;
                }

                lock (_lock)
                {
                    return _instance;
                }
            }
        }

        /// <summary>
        /// Whether a valid instance currently exists.
        /// </summary>
        public static bool HasInstance => _instance != null && !_applicationIsQuitting;

        protected virtual void Awake()
        {
            if (_instance != null && _instance != this)
            {
                Debug.LogWarning(
                    $"[Singleton] Duplicate instance of {typeof(T)} detected. Destroying duplicate on '{gameObject.name}'.");
                Destroy(gameObject);
                return;
            }

            _instance = this as T;
            DontDestroyOnLoad(gameObject);
            OnSingletonAwake();
        }

        protected virtual void OnDestroy()
        {
            if (_instance == this)
            {
                _instance = null;
            }
        }

        protected virtual void OnApplicationQuit()
        {
            _applicationIsQuitting = true;
        }

        /// <summary>
        /// Called once when the singleton is first initialized.
        /// Override instead of Awake.
        /// </summary>
        protected virtual void OnSingletonAwake() { }
    }
}
