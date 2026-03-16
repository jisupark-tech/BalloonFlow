using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace BalloonFlow
{
    /// <summary>
    /// Persistent game manager. Handles scene transitions, pause/resume,
    /// and ensures persistent singletons exist on first load.
    /// </summary>
    /// <remarks>
    /// Layer: Core | Genre: Puzzle | Role: Manager | Phase: 0
    /// </remarks>
    public class GameManager : Singleton<GameManager>
    {
        #region Constants

        public const string SCENE_TITLE  = "Title";
        public const string SCENE_LOBBY  = "Lobby";
        public const string SCENE_INGAME = "InGame";

        #endregion

        #region Fields

        private bool _isPaused;
        private bool _isTransitioning;
        private string _currentScene;
        private bool _persistentManagersInitialized;

        #endregion

        #region Properties

        public bool IsPaused => _isPaused;
        public bool IsTransitioning => _isTransitioning;
        public string CurrentScene => _currentScene;

        #endregion

        #region Lifecycle

        protected override void OnSingletonAwake()
        {
            _isPaused = false;
            _isTransitioning = false;
            _currentScene = SceneManager.GetActiveScene().name;
            EnsurePersistentManagers();
        }

        #endregion

        #region Public Methods

        /// <summary>
        /// Loads the target scene with a fade transition.
        /// </summary>
        public void LoadScene(string sceneName)
        {
            if (_isTransitioning)
            {
                Debug.LogWarning($"[GameManager] Already transitioning. Ignoring LoadScene({sceneName}).");
                return;
            }

            // Always resume before scene transition
            if (_isPaused) ResumeGame();

            StartCoroutine(LoadSceneCoroutine(sceneName));
        }

        /// <summary>
        /// Pauses the game by setting Time.timeScale to 0.
        /// </summary>
        public void PauseGame()
        {
            if (_isPaused) return;
            _isPaused = true;
            Time.timeScale = 0f;
            EventBus.Publish(new OnGamePaused());
            Debug.Log("[GameManager] Game paused.");
        }

        /// <summary>
        /// Resumes the game by restoring Time.timeScale to 1.
        /// </summary>
        public void ResumeGame()
        {
            if (!_isPaused) return;
            _isPaused = false;
            Time.timeScale = 1f;
            EventBus.Publish(new OnGameResumed());
            Debug.Log("[GameManager] Game resumed.");
        }

        /// <summary>
        /// Loads the InGame scene and starts the specified level.
        /// </summary>
        public void StartLevel(int levelId)
        {
            LoadScene(SCENE_INGAME);
            // Level will be loaded by GameBootstrap in InGame scene after scene load
            PlayerPrefs.SetInt("BF_PendingLevelId", levelId);
        }

        /// <summary>
        /// Returns to lobby scene.
        /// </summary>
        public void GoToLobby()
        {
            LoadScene(SCENE_LOBBY);
        }

        /// <summary>
        /// Returns to title scene.
        /// </summary>
        public void GoToTitle()
        {
            LoadScene(SCENE_TITLE);
        }

        #endregion

        #region Private Methods

        private IEnumerator LoadSceneCoroutine(string sceneName)
        {
            _isTransitioning = true;
            string fromScene = _currentScene;

            EventBus.Publish(new OnSceneTransitionStarted
            {
                fromScene = fromScene ?? string.Empty,
                toScene = sceneName
            });

            // Fade out via UIManager if available
            if (UIManager.HasInstance)
            {
                UIManager.Instance.FadeOut(0.3f);
                yield return new WaitForSecondsRealtime(0.35f);
            }

            AsyncOperation op = SceneManager.LoadSceneAsync(sceneName);
            if (op != null)
            {
                while (!op.isDone)
                {
                    yield return null;
                }
            }

            _currentScene = sceneName;
            _isTransitioning = false;

            // Fade in
            if (UIManager.HasInstance)
            {
                UIManager.Instance.FadeIn(0.3f);
            }

            EventBus.Publish(new OnSceneTransitionCompleted
            {
                sceneName = sceneName
            });

            Debug.Log($"[GameManager] Scene loaded: {sceneName}");
        }

        /// <summary>
        /// Creates persistent managers that survive scene transitions.
        /// Called once on first initialization.
        /// </summary>
        private void EnsurePersistentManagers()
        {
            if (_persistentManagersInitialized) return;
            _persistentManagersInitialized = true;

            // Core persistent managers
            EnsureSingleton<ObjectPoolManager>("Mgr_ObjectPool");
            EnsureSingleton<UIManager>("Mgr_UI");
            EnsureSingleton<PopupManager>("Mgr_Popup");
            EnsureSingleton<PageController>("Mgr_Page");

            // Economy
            EnsureSingleton<CurrencyManager>("Mgr_Currency");
            EnsureSingleton<LifeManager>("Mgr_Life");
            EnsureSingleton<DailyRewardManager>("Mgr_DailyReward");
            EnsureSingleton<BoosterManager>("Mgr_Booster");
            EnsureSingleton<ContinueHandler>("Mgr_Continue");

            // Shop/Monetization
            EnsureSingleton<ShopManager>("Mgr_Shop");
            EnsureSingleton<AdManager>("Mgr_Ad");
            EnsureSingleton<OfferManager>("Mgr_Offer");
            EnsureSingleton<IAPManager>("Mgr_IAP");

            // Level & Content
            var levelGO = EnsureSingleton<LevelManager>("Mgr_Level");
            if (levelGO.GetComponent<LevelDataProvider>() == null)
                levelGO.AddComponent<LevelDataProvider>();

            EnsureSingleton<PackageManager>("Mgr_Package");

            // Wire LevelDataProvider
            var levelMgr = levelGO.GetComponent<LevelManager>();
            var ldp = levelGO.GetComponent<LevelDataProvider>();
            if (levelMgr != null && ldp != null)
            {
                var field = typeof(LevelManager).GetField("_levelDataProvider",
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                if (field != null && field.GetValue(levelMgr) == null)
                {
                    field.SetValue(levelMgr, ldp);
                }
            }

            Debug.Log("[GameManager] Persistent managers initialized.");
        }

        private GameObject EnsureSingleton<T>(string name) where T : Component
        {
            T existing = FindAnyObjectByType<T>();
            if (existing != null) return existing.gameObject;
            var go = new GameObject(name);
            go.AddComponent<T>();
            return go;
        }

        #endregion
    }
}
