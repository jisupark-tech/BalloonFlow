using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace BalloonFlow
{
    /// <summary>
    /// 게임 매니저. Title 씬에서 SceneBuilder가 배치, DontDestroyOnLoad.
    ///
    /// Init 흐름:
    /// 1) Title 씬 로드 → GameManager.Awake (SceneBuilder가 배치)
    /// 2) Lobby 진입 → LobbyController가 InitLobby() 호출 → 경제/상점 매니저 생성
    /// 3) InGame 진입 → GameBootstrap이 InitInGame() 호출 → InGame 매니저 생성 (GameManager 자식)
    /// 4) InGame 퇴장 → CleanupInGame() → InGame 매니저 파괴
    /// </summary>
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

        // Init 플래그
        private bool _lobbyInitialized;

        // InGame 매니저 루트 (GameManager 자식)
        private GameObject _inGameRoot;

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
        }

        #endregion

        #region InitLobby — 경제/상점/레벨 매니저 (Lobby 최초 진입 시 1회)

        public void InitLobby()
        {
            if (_lobbyInitialized) return;
            _lobbyInitialized = true;

            // Economy
            EnsurePersistent<CurrencyManager>("Mgr_Currency");
            EnsurePersistent<GemManager>("Mgr_Gem");
            EnsurePersistent<LifeManager>("Mgr_Life");
            EnsurePersistent<DailyRewardManager>("Mgr_DailyReward");
            EnsurePersistent<BoosterManager>("Mgr_Booster");
            EnsurePersistent<ContinueHandler>("Mgr_Continue");

            // Shop / Monetization
            EnsurePersistent<ShopManager>("Mgr_Shop");
            EnsurePersistent<AdManager>("Mgr_Ad");
            EnsurePersistent<OfferManager>("Mgr_Offer");
            EnsurePersistent<IAPManager>("Mgr_IAP");

            // Level & Content
            var _levelGO = EnsurePersistent<LevelManager>("Mgr_Level");
            if (_levelGO.GetComponent<LevelDataProvider>() == null)
                _levelGO.AddComponent<LevelDataProvider>();
            EnsurePersistent<PackageManager>("Mgr_Package");

            WireLevelDataProvider(_levelGO);

            Debug.Log("[GameManager] InitLobby 완료");
        }

        #endregion

        #region InitInGame — InGame 매니저 (GameManager 자식으로 생성/파괴)

        public void InitInGame()
        {
            if (_inGameRoot != null) return;

            _inGameRoot = new GameObject("InGameManagers");
            _inGameRoot.transform.SetParent(transform);

            CreateChild<InputHandler>("Mgr_Input");
            var _railGO = CreateChild<RailManager>("Mgr_Rail");
            _railGO.AddComponent<RailRenderer>();
            CreateChild<ScoreManager>("Mgr_Score");
            CreateChild<BoardStateManager>("Mgr_BoardState");
            CreateChild<HolderManager>("Mgr_Holder");
            CreateChild<DartManager>("Mgr_Dart");
            CreateChild<BalloonController>("Mgr_Balloon");
            CreateChild<PopProcessor>("Mgr_Pop");
            CreateChild<HUDController>("Mgr_HUD");
            CreateChild<FeedbackController>("Mgr_Feedback");
            CreateChild<GimmickManager>("Mgr_Gimmick");
            CreateChild<BalanceProcessor>("Mgr_Balance");
            CreateChild<TutorialController>("Mgr_TutorialCtrl");
            CreateChild<TutorialManager>("Mgr_TutorialMgr");
            CreateChild<HolderVisualManager>("Mgr_HolderVisual");
            CreateChild<LevelGenerator>("Mgr_LevelGen");

            // InputHandler에 MainCamera 연결
            var _input = _inGameRoot.GetComponentInChildren<InputHandler>();
            if (_input != null && CameraManager.HasInstance)
            {
                var _field = typeof(InputHandler).GetField("_gameCamera",
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                if (_field != null) _field.SetValue(_input, CameraManager.Instance.MainCamera);
            }

            Debug.Log("[GameManager] InitInGame 완료");
        }

        /// <summary>InGame 매니저 전부 파괴. InGame 씬 퇴장 시 호출.</summary>
        public void CleanupInGame()
        {
            if (_inGameRoot != null)
            {
                Destroy(_inGameRoot);
                _inGameRoot = null;
                Debug.Log("[GameManager] CleanupInGame 완료");
            }
        }

        #endregion

        #region Scene 이동

        public void LoadScene(string _sceneName)
        {
            if (_isTransitioning) return;
            if (_isPaused) ResumeGame();
            StartCoroutine(LoadSceneCoroutine(_sceneName));
        }

        public void StartLevel(int _levelId)
        {
            PlayerPrefs.SetInt("BF_PendingLevelId", _levelId);
            LoadScene(SCENE_INGAME);
        }

        public void GoToLobby() { LoadScene(SCENE_LOBBY); }
        public void GoToTitle() { LoadScene(SCENE_TITLE); }

        IEnumerator LoadSceneCoroutine(string _sceneName)
        {
            _isTransitioning = true;
            string _fromScene = _currentScene;

            // InGame 퇴장 시 매니저 정리
            if (_fromScene == SCENE_INGAME)
                CleanupInGame();

            EventBus.Publish(new OnSceneTransitionStarted
            {
                fromScene = _fromScene ?? string.Empty,
                toScene = _sceneName
            });

            // Fade Out
            if (UIManager.HasInstance)
            {
                UIManager.Instance.CloseUIAll();
                UIManager.Instance.FadeOut(0.3f);
                yield return new WaitForSecondsRealtime(0.35f);
            }

            // 씬 로드
            AsyncOperation _op = SceneManager.LoadSceneAsync(_sceneName);
            if (_op != null)
                while (!_op.isDone) yield return null;

            _currentScene = _sceneName;
            _isTransitioning = false;

            // 카메라 설정
            if (CameraManager.HasInstance)
            {
                switch (_sceneName)
                {
                    case SCENE_TITLE:  CameraManager.Instance.ConfigureTitle();  break;
                    case SCENE_LOBBY:  CameraManager.Instance.ConfigureLobby();  break;
                    case SCENE_INGAME: CameraManager.Instance.ConfigureInGame(); break;
                }
            }

            // Fade In
            if (UIManager.HasInstance)
                UIManager.Instance.FadeIn(0.3f);

            EventBus.Publish(new OnSceneTransitionCompleted { sceneName = _sceneName });
        }

        #endregion

        #region Pause

        public void PauseGame()
        {
            if (_isPaused) return;
            _isPaused = true;
            Time.timeScale = 0f;
            EventBus.Publish(new OnGamePaused());
        }

        public void ResumeGame()
        {
            if (!_isPaused) return;
            _isPaused = false;
            Time.timeScale = 1f;
            EventBus.Publish(new OnGameResumed());
        }

        #endregion

        #region Helpers

        GameObject EnsurePersistent<T>(string _name) where T : Component
        {
            T _existing = FindAnyObjectByType<T>();
            if (_existing != null) return _existing.gameObject;
            var _go = new GameObject(_name);
            _go.AddComponent<T>();
            return _go;
        }

        GameObject CreateChild<T>(string _name) where T : Component
        {
            var _go = new GameObject(_name);
            _go.transform.SetParent(_inGameRoot.transform);
            _go.AddComponent<T>();
            return _go;
        }

        void WireLevelDataProvider(GameObject _levelGO)
        {
            var _mgr = _levelGO.GetComponent<LevelManager>();
            var _ldp = _levelGO.GetComponent<LevelDataProvider>();
            if (_mgr != null && _ldp != null)
            {
                var _field = typeof(LevelManager).GetField("_levelDataProvider",
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                if (_field != null && _field.GetValue(_mgr) == null)
                    _field.SetValue(_mgr, _ldp);
            }
        }

        #endregion
    }
}
