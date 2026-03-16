using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace BalloonFlow
{
    /// <summary>
    /// 보드 관련 수치를 Inspector에서 조절할 수 있는 설정 클래스.
    /// GameManager.Board를 통해 전체 InGame 시스템에서 참조.
    /// </summary>
    [System.Serializable]
    public class BoardConfig
    {
        [Header("[풍선 — Balloon]")]
        [Tooltip("풍선 간 월드 간격 (작을수록 빈틈 없음)")]
        public float cellSpacing = 0.55f;

        [Tooltip("풍선 스케일 (0.2~1.0)")]
        [Range(0.2f, 1.0f)]
        public float balloonScale = 0.5f;

        [Header("[다트 — Dart]")]
        [Tooltip("다트 비행 시간 (초). 클수록 느림")]
        public float dartFlightTime = 0.036f;

        [Tooltip("다트 레일 이동 속도 (DartManager용)")]
        public float dartRailSpeed = 5f;

        [Header("[레일 — Rail]")]
        [Tooltip("홀더 레일 이동 속도")]
        public float holderRailSpeed = 7f;

        [Tooltip("레일 위 최대 홀더 수")]
        public int maxOnRail = 9;

        [Tooltip("보드 가장자리 ~ 레일 간격")]
        public float railPadding = 1.5f;

        [Tooltip("레일 높이 (Y축)")]
        public float railHeight = 0.5f;

        [Header("[보드 — Board]")]
        [Tooltip("보드 중심 X좌표")]
        public float boardCenterX = 0f;

        [Tooltip("보드 중심 Z좌표")]
        public float boardCenterZ = 2f;
    }

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

        public const string SCENE_TITLE    = "Title";
        public const string SCENE_LOBBY    = "Lobby";
        public const string SCENE_INGAME   = "InGame";
        public const string SCENE_MAPMAKER = "MapMaker";

        #endregion

        #region Board Config

        [Header("[Board Config — Inspector에서 수치 조절]")]
        public BoardConfig Board = new BoardConfig();

        #endregion

        #region Fields

        private bool _isPaused;
        private bool _isTransitioning;
        private string _currentScene;

        /// <summary>
        /// True when playing a level from MapMaker test mode.
        /// Managers check this to provide unlimited items.
        /// Reset when leaving InGame to a non-MapMaker scene.
        /// </summary>
        public static bool IsTestPlayMode;

        // Init 플래그
        private bool _lobbyInitialized;

        // InGame 매니저 루트 (GameManager 자식)
        private GameObject _inGameRoot;

        /// <summary>
        /// Optional sprite shown during fade transitions.
        /// Set via SetTransitionImage() before LoadScene(). Consumed once per transition.
        /// </summary>
        private Sprite _transitionSprite;

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

            CreateChild<BoardTileManager>("Mgr_BoardTile");
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
        public void GoToMapMaker() { LoadScene(SCENE_MAPMAKER); }

        /// <summary>
        /// Set a custom image for the next scene transition fade.
        /// The sprite is consumed (cleared) after one transition.
        /// Pass null to use default solid black.
        /// </summary>
        public void SetTransitionImage(Sprite _sprite)
        {
            _transitionSprite = _sprite;
        }

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

            // Fade Out (with optional custom image)
            Sprite _fadeSprite = _transitionSprite;
            _transitionSprite = null; // consume once
            if (UIManager.HasInstance)
            {
                UIManager.Instance.CloseUIAll();
                UIManager.Instance.FadeOut(0.3f, _fadeSprite);
                yield return new WaitForSecondsRealtime(0.35f);
            }

            // 씬 로드
            AsyncOperation _op = SceneManager.LoadSceneAsync(_sceneName);
            if (_op != null)
                while (!_op.isDone) yield return null;

            _currentScene = _sceneName;
            _isTransitioning = false;

            // 카메라 설정 (MapMaker has its own camera setup)
            if (CameraManager.HasInstance && _sceneName != SCENE_MAPMAKER)
            {
                switch (_sceneName)
                {
                    case SCENE_TITLE:  CameraManager.Instance.ConfigureTitle();  break;
                    case SCENE_LOBBY:  CameraManager.Instance.ConfigureLobby();  break;
                    case SCENE_INGAME: CameraManager.Instance.ConfigureInGame(); break;
                }
            }

            // Reset test play mode when leaving InGame to non-MapMaker destinations
            if (_fromScene == SCENE_INGAME && _sceneName != SCENE_INGAME)
            {
                IsTestPlayMode = false;
            }

            // Fade In (with same custom image if set)
            if (UIManager.HasInstance)
                UIManager.Instance.FadeIn(0.3f, _fadeSprite);

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
