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
        public float dartFlightTime = 0.1f;

        [Tooltip("다트 발사 인터벌 (초). 보관함이 다트를 연속 발사하는 간격")]
        public float dartFireInterval = 0.02f;

        [Tooltip("다트 레일 이동 속도 (DartManager용)")]
        public float dartRailSpeed = 8f;

        [Tooltip("보관함에서 다트 소환 간격 (초). 벨트 속도와 동기화 필요")]
        public float dartSpawnInterval = 0.02f;

        [Tooltip("컨베이어 화살표 이동 속도")]
        public float conveyorArrowSpeed = 4f;


        [Header("[다트 비주얼 — Dart Visual (인게임 동적 조정)]")]
        [Tooltip("다트 스케일 (1.0 = 기본)")]
        [Range(0.1f, 3f)]
        public float dartScale = 1f;

        [Tooltip("다트 간격 배율 (1.0 = 기본). 크면 다트 사이 간격 넓어짐")]
        [Range(0.2f, 3f)]
        public float dartSpacingMultiplier = 1f;

        [Header("[레일 — Rail (컨베이어벨트)]")]
        [Tooltip("레일 슬롯 수 (기본 200). 다트가 슬롯을 점유")]
        public int railSlotCount = 200;

        [Tooltip("레일 회전 속도 (슬롯/초). 컨베이어벨트 속도")]
        public float railRotationSpeed = 37f;

        [Tooltip("보드 가장자리 ~ 레일 간격")]
        public float railPadding = 1.5f;

        [Tooltip("레일 높이 (Y축)")]
        public float railHeight = 0.1f;

        [Header("[보드 — Board]")]
        [Tooltip("보드 중심 X좌표")]
        public float boardCenterX = 0f;

        [Tooltip("보드 중심 Z좌표")]
        public float boardCenterZ = 2f;

        [Header("[연출 — Visual Effects]")]
        [Tooltip("보관함 다트 배치 시 펀치 스케일 연출 사용 여부")]
        public bool useDeployPunchScale = false;

        [Header("[실패 판정 — Fail Detection]")]
        [Tooltip("실패 유예 시간 (초). 99.5%+ 점유 + 최외곽 매칭 불가 시 이 시간 후 실패")]
        public float failGraceDelay = 1.5f;

        [Tooltip("실패 임계 점유율 (0~1). 기본 0.995 = 199/200슬롯")]
        public float failOccupancyThreshold = 0.995f;
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

        /// <summary>
        /// TEST ITEM 모드. true면 아이템(부스터) 무제한 사용 가능.
        /// Inspector 또는 런타임 디버그 UI에서 토글.
        /// </summary>
        public static bool IsTestItemMode;

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
            CreateChild<BoosterExecutor>("Mgr_BoosterExec");
            CreateChild<PopupManager>("Mgr_Popup");

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
            // 풀 오브젝트 정리 (DontDestroyOnLoad라 씬 전환에서 안 사라짐)
            if (BalloonController.HasInstance)
                BalloonController.Instance.ClearAllBalloons();
            if (DartManager.HasInstance)
                DartManager.Instance.ClearAllDarts();
            if (HolderVisualManager.HasInstance)
                HolderVisualManager.Instance.ClearAllVisuals();
            if (RailManager.HasInstance)
                RailManager.Instance.ResetAll();
            if (PopupManager.HasInstance)
                PopupManager.Instance.CloseAllPopups();

            if (_inGameRoot != null)
            {
                Destroy(_inGameRoot);
                _inGameRoot = null;
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

            EventBus.Publish(new OnSceneTransitionStarted
            {
                fromScene = _fromScene ?? string.Empty,
                toScene = _sceneName
            });

            // Fade Out 먼저 (화면 가린 후 정리)
            Sprite _fadeSprite = _transitionSprite;
            _transitionSprite = null;
            if (UIManager.HasInstance)
            {
                UIManager.Instance.FadeOut(0.5f, _fadeSprite);
                yield return new WaitForSecondsRealtime(0.55f);
                UIManager.Instance.CloseUIAll();
            }

            // 페이드 완료 후 InGame 매니저 정리
            if (_fromScene == SCENE_INGAME)
                CleanupInGame();

            // 씬 로드
            AsyncOperation _op = SceneManager.LoadSceneAsync(_sceneName);
            if (_op != null)
                while (!_op.isDone) yield return null;

            _currentScene = _sceneName;
            _isTransitioning = false;

            // 카메라 설정 (MapMaker has its own camera setup)
            if (CameraManager.HasInstance)
            {
                if (_sceneName == SCENE_MAPMAKER)
                {
                    CameraManager.Instance.ReleaseEnforcement();
                }
                else
                {
                    switch (_sceneName)
                    {
                        case SCENE_TITLE:  CameraManager.Instance.ConfigureTitle();  break;
                        case SCENE_LOBBY:  CameraManager.Instance.ConfigureLobby();  break;
                        case SCENE_INGAME: CameraManager.Instance.ConfigureInGame(); break;
                    }
                }
            }

            // Reset test play mode when leaving InGame to non-MapMaker destinations
            if (_fromScene == SCENE_INGAME && _sceneName != SCENE_INGAME)
            {
                IsTestPlayMode = false;
            }

            // Fade In
            if (UIManager.HasInstance)
                UIManager.Instance.FadeIn(0.5f, _fadeSprite);

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

        #region Debug UI (InGame Only)

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        private GUIStyle _debugBtnStyle;

        private void OnGUI()
        {
            if (_currentScene != SCENE_INGAME && _currentScene != SCENE_MAPMAKER) return;

            // 스타일 초기화 (한번만)
            if (_debugBtnStyle == null)
            {
                _debugBtnStyle = new GUIStyle(GUI.skin.button);
                _debugBtnStyle.fontSize = 18;
                _debugBtnStyle.fontStyle = FontStyle.Bold;
            }

            float w = 220f;
            float h = 50f;
            float gap = 6f;
            float x = Screen.width - w - 15f;
            float y = 15f;

            int currentLevel = PlayerPrefs.GetInt("BF_PendingLevelId", 1);

            // ── TEST ITEM 토글 ──
            string itemLabel = IsTestItemMode ? "TEST ITEM: ON" : "TEST ITEM: OFF";
            GUI.backgroundColor = IsTestItemMode ? Color.green : Color.gray;
            if (GUI.Button(new Rect(x, y, w, h), itemLabel, _debugBtnStyle))
            {
                IsTestItemMode = !IsTestItemMode;
                Debug.Log($"[GameManager] TEST ITEM = {IsTestItemMode}");
            }
            y += h + gap;

            // ── 강제 실패 ──
            GUI.backgroundColor = new Color(1f, 0.3f, 0.3f);
            if (GUI.Button(new Rect(x, y, w, h), "FORCE FAIL", _debugBtnStyle))
            {
                ForceShowPopup("popup_fail01", "PopupFail01", "Popup/PopupFail01");
            }
            y += h + gap;

            // ── 강제 클리어 ──
            GUI.backgroundColor = new Color(0.3f, 1f, 0.3f);
            if (GUI.Button(new Rect(x, y, w, h), "FORCE CLEAR", _debugBtnStyle))
            {
                if (BoardStateManager.HasInstance)
                {
                    // 모든 풍선 제거 → 클리어 판정
                    if (BalloonController.HasInstance)
                    {
                        var all = BalloonController.Instance.GetAllBalloons();
                        if (all != null)
                        {
                            foreach (var b in all)
                            {
                                if (!b.isPopped)
                                    BalloonController.Instance.PopBalloon(b.balloonId);
                            }
                        }
                    }
                }
                Debug.Log("[GameManager] FORCE CLEAR triggered");
            }
            y += h + gap;

            // ── 이전 스테이지 ──
            GUI.backgroundColor = new Color(0.5f, 0.7f, 1f);
            if (GUI.Button(new Rect(x, y, w / 2 - gap / 2, h), $"◀ Lv.{currentLevel - 1}", _debugBtnStyle))
            {
                int prevLevel = Mathf.Max(1, currentLevel - 1);
                CleanupBeforeLevelSwitch();
                PlayerPrefs.SetInt("BF_PendingLevelId", prevLevel);
                LoadScene(SCENE_INGAME);
                Debug.Log($"[GameManager] → Level {prevLevel}");
            }

            // ── 다음 스테이지 ──
            if (GUI.Button(new Rect(x + w / 2 + gap / 2, y, w / 2 - gap / 2, h), $"Lv.{currentLevel + 1} ▶", _debugBtnStyle))
            {
                int nextLevel = currentLevel + 1;
                CleanupBeforeLevelSwitch();
                PlayerPrefs.SetInt("BF_PendingLevelId", nextLevel);
                LoadScene(SCENE_INGAME);
                Debug.Log($"[GameManager] → Level {nextLevel}");
            }
            y += h + gap;

            // ── 현재 레벨 표시 ──
            GUI.backgroundColor = new Color(0.2f, 0.2f, 0.2f);
            GUI.Button(new Rect(x, y, w, h * 0.6f), $"Current: Level {currentLevel}", _debugBtnStyle);

            GUI.backgroundColor = Color.white;
        }

        /// <summary>레벨 전환 전 정리. 게임 오브젝트 + 팝업 + 상태 초기화.</summary>
        private void CleanupBeforeLevelSwitch()
        {
            // 풍선 풀 반환 (Destroy 전에 정리해야 풀 오브젝트 유실 방지)
            if (BalloonController.HasInstance)
                BalloonController.Instance.ClearAllBalloons();

            // 다트 풀 반환
            if (DartManager.HasInstance)
                DartManager.Instance.ClearAllDarts();

            // 보관함 비주얼 풀 반환
            if (HolderVisualManager.HasInstance)
                HolderVisualManager.Instance.ClearAllVisuals();

            // 레일 슬롯 초기화
            if (RailManager.HasInstance)
                RailManager.Instance.ResetAll();

            // 팝업 전부 닫기
            if (PopupManager.HasInstance)
                PopupManager.Instance.CloseAllPopups();

            // 이어하기 횟수 리셋
            if (ContinueHandler.HasInstance)
                ContinueHandler.Instance.ResetContinueCount();
        }

        /// <summary>PopupManager에 등록된 팝업 표시. 미등록 시 자동 로드+등록.</summary>
        private void ForceShowPopup(string popupId, string logName, string resourcePath)
        {
            if (PopupManager.HasInstance)
            {
                if (PopupManager.Instance.HasPopup(popupId))
                {
                    PopupManager.Instance.ShowPopup(popupId, 50);
                }
                else if (UIManager.HasInstance)
                {
                    var go = UIManager.Instance.LoadPrefab(resourcePath, UIManager.Instance.UiTr);
                    if (go != null)
                    {
                        var cg = go.GetComponent<CanvasGroup>();
                        if (cg == null) cg = go.AddComponent<CanvasGroup>();
                        PopupManager.Instance.RegisterPopup(popupId, cg);
                        PopupManager.Instance.ShowPopup(popupId, 50);
                    }
                }
            }
            Debug.Log($"[GameManager] {logName} 표시");
        }
#endif

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
