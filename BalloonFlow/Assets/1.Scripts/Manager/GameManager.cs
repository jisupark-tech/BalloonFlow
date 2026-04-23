using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;
using DG.Tweening;

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
        [Tooltip("풍선 간 월드 간격. 작을수록 빈틈 없음. (default: 0.55)")]
        public float cellSpacing = 0.55f;

        [Tooltip("풍선 스케일. (default: 0.5, range: 0.2~1.0)")]
        [Range(0.2f, 1.0f)]
        public float balloonScale = 0.5f;

        [Tooltip("풍선 팝 시 스케일업 배율 (1=원본, 1.5=1.5배 부풀기). 동적 반영. (default: 2)")]
        [Range(1f, 3f)]
        public float popScaleMultiplier = 2f;

        [Tooltip("풍선 팝 시 스케일업 시간(초). 이 시간 후 파티클 재생. 동적 반영. (default: 0.2)")]
        [Range(0f, 1f)]
        public float popScaleDuration = 0.2f;

        [Header("[풍선 타일 영역 — 동적 조정]")]
        [Tooltip("풍선 타일 영역 가로 배율. 1.0=기본, 인게임 동적 반영. (default: 1.39)")]
        [Range(0.5f, 2f)]
        public float balloonFieldWidthMult = 1.39f;

        [Tooltip("풍선 타일 영역 세로 배율. 1.0=기본, 인게임 동적 반영. (default: 1.44)")]
        [Range(0.5f, 2f)]
        public float balloonFieldHeightMult = 1.44f;

        [Header("[다트 — Dart]")]
        [Tooltip("다트 비행 시간(초). 발사→풍선 도달까지 걸리는 시간. 클수록 느림. 동적 반영. (default: 0.1)")]
        public float dartFlightTime = 0.1f;

        [Tooltip("다트 레일 이동 속도. 현재 미사용 (railRotationSpeed가 벨트 속도 담당). (default: 8)")]
        public float dartRailSpeed = 8f;

        [Tooltip("공격 스캔 배율. railSpeed에 비례하여 스캔 빈도 결정. 높을수록 공격 빠름. 동적 반영. (default: 1.0)")]
        [Range(0.5f, 10f)]
        public float attackSpeedMultiplier = 1f;

        [Tooltip("프레임당 최대 발사 수. 1=단발 연속, 높으면 직선상 같은 색 풍선 연속 공격 가능. 동적 반영. (default: 1)")]
        [Range(1, 10)]
        public int maxFiresPerFrame = 1;

        [Tooltip("순차 발사 제한 해제. ON=모든 다트 동시 공격 가능, OFF=선행 다트 우선 (기본). 동적 반영.")]
        public bool dartFreeFireMode = false;

        [Tooltip("풍선 간격 동기 발사. ON=다트가 풍선 하나 거리를 이동하는 시간을 발사 인터벌로 사용 (외곽 스윕 연출). OFF=기존 스캔 주기. 동적 반영.")]
        public bool dartBalloonSyncedFireMode = false;

        [HideInInspector] public float dartSpawnInterval = 0.02f;
        [HideInInspector] public float conveyorArrowSpeed = 4f;

        [Header("[다트 비주얼 — Dart Visual (인게임 동적 조정)]")]
        [Tooltip("다트 오브젝트 스케일. 동적 반영. (default: 0.275)")]
        [Range(0.1f, 3f)]
        public float dartScale = 0.275f;

        [Tooltip("다트 간격 배율. 크면 다트 사이 간격 넓어짐. 동적 반영. (default: 1.1)")]
        [Range(0.2f, 3f)]
        public float dartSpacingMultiplier = 1.1f;

        [Tooltip("다트 경로 오프셋. 벨트 중심에서 안쪽(+)/바깥쪽(-) 이동. 동적 반영. (default: -0.15)")]
        [Range(-2f, 2f)]
        public float dartPathOffset = -0.15f;

        [Tooltip("비행 중 다트 스케일을 풍선 크기로 보간. 끄면 발사 시 스케일 유지. 동적 반영. (default: ON)")]
        public bool dartScaleLerpToBalloon = true;

        [Tooltip("비행 보간 강도. 0=원본 스케일 유지, 1=풍선 스케일에 정확히 맞춤. 동적 반영. (default: 1)")]
        [Range(0f, 1f)]
        public float dartScaleLerpStrength = 1f;

        [Header("[Cave 스케일 — 면수별 (FadeStart/FadeEnd, 전체 경로 대비 비율)]")]
        [Tooltip("1면(일자) Cave Fade Start. 클수록 안쪽에서 스케일 변화. (default: 0.0315)")]
        public float caveFadeStart1Side = 0.0315f;
        [Tooltip("1면(일자) Cave Fade End. (default: 0.03)")]
        public float caveFadeEnd1Side = 0.03f;

        [Tooltip("2면(ㄴ자) Cave Fade Start. (default: 0.0315)")]
        public float caveFadeStart2Side = 0.0315f;
        [Tooltip("2면(ㄴ자) Cave Fade End. (default: 0.03)")]
        public float caveFadeEnd2Side = 0.03f;

        [Tooltip("3면(ㄷ자) Cave Fade Start. (default: 0.0315)")]
        public float caveFadeStart3Side = 0.0315f;
        [Tooltip("3면(ㄷ자) Cave Fade End. (default: 0.03)")]
        public float caveFadeEnd3Side = 0.03f;

        [Tooltip("4면(ㅁ자) Cave Fade Start. (default: 0.0315)")]
        public float caveFadeStart4Side = 0.0315f;
        [Tooltip("4면(ㅁ자) Cave Fade End. (default: 0.03)")]
        public float caveFadeEnd4Side = 0.03f;

        [Header("[골드 연출 — Coin Fly]")]
        [Tooltip("코인 비행 최소 시간(초). (default: 0.3)")]
        public float coinFlyDurationMin = 0.3f;
        [Tooltip("코인 비행 최대 시간(초). (default: 0.55)")]
        public float coinFlyDurationMax = 0.55f;
        [Tooltip("코인 생성 간격 최소(초). (default: 0.005)")]
        public float coinSpawnDelayMin = 0.005f;
        [Tooltip("코인 생성 간격 최대(초). (default: 0.02)")]
        public float coinSpawnDelayMax = 0.02f;
        [Tooltip("코인 이펙트 스케일. (default: 15)")]
        public float coinFlyScale = 15f;

        [Header("[레일 — Rail (컨베이어벨트)]")]
        [Tooltip("레일 슬롯 수. 다트가 점유하는 칸 수. 레벨 데이터에서 자동 계산됨. (default: 200)")]
        public int railSlotCount = 200;

        [Tooltip("레일 회전 속도(슬롯/초). 벨트+다트+화살표 이동 속도 통일 기준. (default: 37)")]
        public float railRotationSpeed = 37f;

        [Tooltip("보드 가장자리 ~ 레일 간격. (default: 1.5)")]
        public float railPadding = 1.5f;

        [Tooltip("레일 높이(Y축). (default: 0.1)")]
        public float railHeight = 0.1f;

        [Header("[보드 — Board]")]
        [Tooltip("보드 중심 X좌표. (default: 0)")]
        public float boardCenterX = 0f;

        [Tooltip("보드 중심 Z좌표. (default: 2.4)")]
        public float boardCenterZ = 2.4f;

        [Tooltip("풍선 중심 Z좌표. 벨트(boardCenterZ)와 독립. 동적 반영. (default: 2)")]
        public float balloonCenterZ = 2f;

        [Tooltip("풍선 그리드 Z 오프셋. 추가 미세 보정. 동적 반영. (default: 0)")]
        [Range(-5f, 5f)]
        public float balloonGridZOffset = 0f;

        [Header("[연출 — Visual Effects]")]
        [Tooltip("보관함 다트 배치 시 펀치 스케일 연출 사용 여부. (default: false)")]
        public bool useDeployPunchScale = false;

        [Header("[실패 판정 — Fail Detection]")]
        [Tooltip("실패 유예 시간(초). 99.5%+ 점유 + 최외곽 매칭 불가 시 이 시간 후 실패. (default: 1.5)")]
        public float failGraceDelay = 1.5f;

        [Tooltip("실패 임계 점유율 (0~1). 기본 0.995 = 199/200슬롯")]
        public float failOccupancyThreshold = 0.995f;

        [Tooltip("실패 판정 비활성화. ON=레일 초과/교착 등 어떤 조건에서도 게임오버 트리거 안 됨. 경고 UI(게이지)는 그대로 표시. 동적 반영.")]
        public bool disableFail = false;
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

            // 모바일 빌드: Debug.Log 비활성화 (프레임 드랍 주범)
#if !UNITY_EDITOR
            Debug.unityLogger.logEnabled = false;
#endif

            // 프레임 타겟 설정 (저사양 디바이스 안정성)
            Application.targetFrameRate = 60;
            QualitySettings.vSyncCount = 0;

            // 세로 고정 (상하 반전 방지)
            Screen.orientation = ScreenOrientation.Portrait;
            Screen.autorotateToPortrait = true;
            Screen.autorotateToPortraitUpsideDown = false;
            Screen.autorotateToLandscapeLeft = false;
            Screen.autorotateToLandscapeRight = false;
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

            // Settings
            EnsurePersistent<SettingsManager>("Mgr_Settings");

            // Audio
            EnsurePersistent<AudioManager>("Mgr_Audio");

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
            CreateChild<GameSpeedController>("Mgr_GameSpeed");
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

            // 오브젝트 풀 전체 반환 — 비활성 풀 오브젝트가 다음 InGame에서 오염되지 않도록
            if (ObjectPoolManager.HasInstance)
                ObjectPoolManager.Instance.ReturnAllPools();

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
            // 하트 소모: 실패 확정 시에만 (클리어/취소 시 소모 없음) — PopupFail02에서 처리
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

            // 보상 이펙트/SFX가 다음 씬으로 이어져 재생되는 문제 방지.
            CoinFlyEffect.StopAll();
            if (AudioManager.HasInstance) AudioManager.Instance.StopAllSfx();

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
                if (BalloonController.HasInstance)
                {
                    // 이전 팝 시퀀스/코루틴 정리 (반복 시 DOTween 누적 부하 방지)
                    BalloonController.Instance.StopAllCoroutines();
                    DOTween.KillAll(false);

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
            y += h * 0.6f + gap;

            // ── RESET USER DATA ──
            GUI.backgroundColor = new Color(1f, 0.5f, 0f);
            if (GUI.Button(new Rect(x, y, w, h), "RESET USER DATA", _debugBtnStyle))
            {
                PlayerPrefs.DeleteAll();
                PlayerPrefs.Save();
                // 다음 실행 시 모든 매니저가 초기값으로 로드 (골드, 하트, 레벨, 부스터 전부)
                LoadScene(SCENE_LOBBY);
            }

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
