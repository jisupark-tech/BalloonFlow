using UnityEngine;

namespace BalloonFlow
{
    /// <summary>
    /// Singleton base for scene-specific managers that should NOT persist across scene transitions.
    /// Unlike Singleton&lt;T&gt;, this does NOT call DontDestroyOnLoad.
    /// When the scene unloads, the instance is destroyed and _instance is nulled.
    /// </summary>
    /// <remarks>
    /// Layer: Core | Genre: Puzzle | Role: Helper | Phase: 0
    /// </remarks>
    public abstract class SceneSingleton<T> : MonoBehaviour where T : MonoBehaviour
    {
        private static T _instance;
        private static bool _applicationIsQuitting;

        public static T Instance
        {
            get
            {
                if (_applicationIsQuitting)
                {
                    return null;
                }
                return _instance;
            }
        }

        public static bool HasInstance => _instance != null && !_applicationIsQuitting;

        protected virtual void Awake()
        {
            if (_instance != null && _instance != this)
            {
                Debug.LogWarning($"[SceneSingleton] Duplicate {typeof(T).Name} destroyed on '{gameObject.name}'.");
                Destroy(gameObject);
                return;
            }

            _instance = this as T;
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

        protected virtual void OnSingletonAwake() { }
    }
}
