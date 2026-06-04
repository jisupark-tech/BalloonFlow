using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;
using DG.Tweening;

namespace BalloonFlow
{
    [System.Serializable]
    public class CaveResolutionOffset
    {
        public string label = "Resolution";
        [Tooltip("Inclusive minimum screen aspect ratio. aspect = Screen.width / Screen.height.")]
        public float minAspect = 0.0f;
        [Tooltip("Inclusive maximum screen aspect ratio. aspect = Screen.width / Screen.height.")]
        public float maxAspect = 1.0f;
        [Tooltip("Extra cave offset for bottom/start cave. X=world X, Y=world Z.")]
        public Vector2 bottomOffset = Vector2.zero;
        [Tooltip("Extra cave offset for 2-side top/end cave. X=world X, Y=world Z.")]
        public Vector2 top2SideOffset = Vector2.zero;
        [Tooltip("Extra cave offset for 3-side top/end cave. X=world X, Y=world Z.")]
        public Vector2 top3SideOffset = Vector2.zero;

        public bool Matches(float aspect)
        {
            return aspect >= minAspect && aspect <= maxAspect;
        }

        public Vector2 GetOffset(int sides, int tunnelIndex)
        {
            if (tunnelIndex == 0 || sides == 1) return bottomOffset;
            if (sides == 2) return top2SideOffset;
            if (sides == 3) return top3SideOffset;
            return Vector2.zero;
        }
    }

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

        [Tooltip("풍선 팝 시 스케일업 시간(초). 이 시간 후 파티클 재생. 동적 반영. (default: 0.1)")]
        [Range(0f, 1f)]
        public float popScaleDuration = 0.1f;

        [Header("[풍선 타일 영역 — 동적 조정]")]
        [Tooltip("풍선 타일 영역 가로 배율. 1.0=기본, 인게임 동적 반영. (default: 1.39)")]
        [Range(0.5f, 2f)]
        public float balloonFieldWidthMult = 1.39f;

        [Tooltip("풍선 타일 영역 세로 배율. 1.0=기본, 인게임 동적 반영. (default: 1.44)")]
        [Range(0.5f, 2f)]
        public float balloonFieldHeightMult = 1.44f;

        [Header("[다트 — Dart]")]
        // [ROLLBACK_DART_FLIGHT_TIME_TO_SPEED_MULT]
        // 비행 속도 결정을 시간(초) 대신 배수로 변경. 롤백 시 아래 주석 해제 + CalculateProjectileFlightTime 원복.
        // [Tooltip("다트 비행 시간(초). 발사→풍선 도달까지 걸리는 시간. 클수록 느림. 동적 반영. (default: 0.1)")]
        // public float dartFlightTime = 0.1f;

        // [ROLLBACK_DART_RAIL_SPEED_DEAD]
        // dartRailSpeed 는 railRotationSpeed 가 벨트 속도를 담당하므로 미사용 — dead code.
        // [Tooltip("다트 레일 이동 속도. 현재 미사용 (railRotationSpeed가 벨트 속도 담당). (default: 8)")]
        // public float dartRailSpeed = 8f;

        [Tooltip("다트 발사→풍선 비행 DOTween Ease 곡선. Linear=등속, OutQuad=감속, InQuad=가속, InOutSine=완만한 가감속 등. 동적 반영. (default: Linear)")]
        public Ease dartFlightEase = Ease.Linear;

        [Tooltip("다트 비행 속도 배수 (셀/초). 1 = 1초당 1셀 이동, 10 = 0.1초당 1셀. 클수록 빠름. 동적 반영. (default: 10.00)")]
        [Range(0.10f, 100.00f)]
        // ROLLBACK_DART_FLIGHT_SPACING_TUNE_20260601:
        // Previous value: 60.00f. Bumped to 66 per design directive 2026-06-01.
        public float dartFlightSpeedMultiplier = 66.00f;

        // [ROLLBACK_DART_LAUNCH_INITIAL_PROGRESS]
        // 다트 발사 직후 시각 위치는 start 에서 시작하되 내부 elapsed 만 n초 앞당겨 진행.
        // 효과: 발사 직후 강한 가속처럼 보이며 비행 중 다트끼리 spacing 줄어듦.
        // n=0 = 비활성(기존 동작). n 값이 크면 spacing 작아짐. 단 duration 보다 작아야 의미 있음.
        [Tooltip("발사 직후 초기 elapsed offset(초). 0=비활성. 양수면 다트가 발사 직후 빠르게 가속한 듯 보이며 비행 다트 spacing 작아짐. 동적 반영. (default: 0)")]
        [Range(0f, 1f)]
        public float dartLaunchInitialProgress = 0f;


        [Tooltip("공격 스캔 배율. railSpeed에 비례하여 스캔 빈도 결정. 높을수록 공격 빠름. 동적 반영. (default: 1.0)")]
        [Range(0.5f, 10f)]
        public float attackSpeedMultiplier = 1f;

        [Tooltip("(legacy) 슬롯 기반 스캔 한도 — per-dart 시스템에선 무시됨. (default: 1)")]
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

        [Tooltip("비행 중 다트 스케일을 풍선 크기로 보간. ON=비행 중 다트가 풍선 사이즈로 점진 변환. 동적 반영. (default: ON)")]
        public bool dartScaleLerpToBalloon = true;

        [Tooltip("비행 보간 강도. 0=원본 스케일 유지, 1=풍선 스케일에 정확히 맞춤. 동적 반영. (default: 1.0 — 풍선 사이즈 정확히 맞춤)")]
        [Range(0f, 1f)]
        public float dartScaleLerpStrength = 1f;

        [Tooltip("발사 순간 다트가 풍선 사이즈로 펀치 스케일업 (overshoot). 동적 반영. (default: OFF — 발사 시 1.25배 커지는 효과 제거)")]
        public bool dartLaunchScalePunch = false;

        [Tooltip("펀치 스케일업 시간(초). (default: 0.10)")]
        [Range(0.02f, 0.4f)]
        public float dartLaunchScalePunchDuration = 0.10f;

        [Tooltip("펀치 오버슈트 배율(레일 사이즈 대비). 1=펀치 없음, 1.25=25% 더 크게 튀어 올랐다 복귀. (default: 1.25)")]
        [Range(1.0f, 1.5f)]
        public float dartLaunchScaleOvershoot = 1.25f;

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

        [Header("[Cave Position - Resolution Profiles]")]
        [Tooltip("Base cave art offset from the actual rail end. X=world X, Y=world Z. Keeps the previous default bottom cave position.")]
        public Vector2 caveBottomBaseOffset = new Vector2(0f, -0.22f);

        [Tooltip("Base cave art offset from the actual 2-side rail end. X=world X, Y=world Z. Keeps the previous default 2-side top cave position.")]
        public Vector2 caveTop2SideBaseOffset = new Vector2(0f, -0.80f);

        [Tooltip("Base cave art offset from the actual 3-side rail end. X=world X, Y=world Z. Keeps the previous default 3-side top cave position.")]
        public Vector2 caveTop3SideBaseOffset = new Vector2(0f, -0.24f);

        [Tooltip("Optional per-aspect cave offsets. Add entries for device groups that differ from the live target resolution.")]
        public CaveResolutionOffset[] caveResolutionOffsets = new CaveResolutionOffset[0];

        [Header("[골드 연출 — Coin Fly]")]
        [Tooltip("코인 비행 최소 시간(초). (default: 0.3)")]
        public float coinFlyDurationMin = 0.3f;
        [Tooltip("코인 비행 최대 시간(초). (default: 0.55)")]
        public float coinFlyDurationMax = 0.55f;
        [Tooltip("코인 생성 간격 최소(초). (default: 0.005)")]
        public float coinSpawnDelayMin = 0.005f;
        [Tooltip("코인 생성 간격 최대(초). (default: 0.02)")]
        public float coinSpawnDelayMax = 0.02f;
        [Tooltip("코인 이펙트 스케일. (default: 1)")]
        public float coinFlyScale = 1f;

        [Header("[레일 — Rail (컨베이어벨트)]")]
        [Tooltip("레일 슬롯 수. 다트가 점유하는 칸 수. 레벨 데이터에서 자동 계산됨. (default: 200)")]
        public int railSlotCount = 200;

        [Tooltip("레일 회전 속도(슬롯/초). 벨트+다트+화살표 이동 속도 통일 기준. (default: 30)")]
        public float railRotationSpeed = 35f;

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
        [Tooltip("실패 유예 시간(초). 99.5%+ 점유 + 최외곽 매칭 불가 시 이 시간 후 실패. (명세 1.5s)")]
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

        #region Render Pipeline 분리 — Quality Level 기반 switch

        // Project Settings > Quality 에 등록된 Quality Level 이름.
        // 각 Level 의 "Render Pipeline Asset" 슬롯에 해당 RPAsset 을 할당해두면 빌드에 자동 포함됨.
        // GameManager 가 RPAsset 을 직접 reference 안 함 → Editor 전용 RPAsset 이 빌드에 끌려가는 누수 방지.
        [Header("[Quality Level Name — Project Settings > Quality 와 동일하게]")]
        [Tooltip("로비/타이틀/상점용 Quality Level 이름. Project Settings > Quality 에서 동일 이름으로 등록 + Mobile_RPAsset 할당.")]
        [SerializeField] private string _lobbyQualityName = "Mobile";
        [Tooltip("인게임 전용 Quality Level 이름. Project Settings > Quality 에서 등록 + InGame_RPAsset 할당.")]
        [SerializeField] private string _ingameQualityName = "InGame";

        /// <summary>씬에 맞는 Quality Level 로 switch — RPAsset 자동 변경.
        /// Quality Level 등록 전이면 not found warning + 변경 안 함 (안전 fallback).</summary>
        private void ApplyRPAssetForScene(string sceneName)
        {
            string target = sceneName == SCENE_INGAME ? _ingameQualityName : _lobbyQualityName;
            if (string.IsNullOrEmpty(target)) return;

            var names = QualitySettings.names;
            for (int i = 0; i < names.Length; i++)
            {
                if (names[i] == target)
                {
                    if (QualitySettings.GetQualityLevel() == i) return;
                    QualitySettings.SetQualityLevel(i, applyExpensiveChanges: true);
                    Debug.Log($"[GameManager] Quality switched → {target} (idx={i}, scene={sceneName})");
                    return;
                }
            }
            // Quality Level 미등록 — 빌드에 RPAsset 없음. 기존 default 유지.
            Debug.LogWarning($"[GameManager] Quality level '{target}' not found in Project Settings > Quality. RPAsset switch skipped.");
        }

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

            // Firebase는 Title부터 살아있어야 Analytics가 첫 화면 이벤트도 잡음
            EnsurePersistent<FirebaseManager>("Mgr_Firebase");
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
            // 메모리 회수(Resources.UnloadUnusedAssets) 는 transition 끝난 후 별도 코루틴에서 호출 —
            // 디바이스에선 동기 stall 위험. 여기선 호출 안 함.
        }

        /// <summary>
        /// 비동기 자원 회수. fade-in 후 background 로 진행.
        /// 모바일에서 동기 호출 시 main thread 0.5~수 초 block.
        /// </summary>
        IEnumerator UnloadUnusedAssetsAsync()
        {
            // 다음 frame 시작 후 진행 — transition 끝난 후 idle 상태에서 GC 수행.
            yield return null;
            Debug.Log("[GameManager] Resources.UnloadUnusedAssets 시작 (background)");
            float t0 = Time.realtimeSinceStartup;
            var op = Resources.UnloadUnusedAssets();
            while (!op.isDone) yield return null;
            Debug.Log($"[GameManager] Resources.UnloadUnusedAssets 완료 ({(Time.realtimeSinceStartup - t0)*1000f:F0}ms)");
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

        /// <summary>InGame 진입 시 강제 최소 로딩 시간 (초). fade-out + setup + warmup 합산이 이 값 미만이면 대기.</summary>
        private const float MIN_INGAME_LOAD_DURATION = 2.5f;

        IEnumerator LoadSceneCoroutine(string _sceneName)
        {
            _isTransitioning = true;
            string _fromScene = _currentScene;
            float _coStart = Time.realtimeSinceStartup;
            Debug.Log($"[GameManager] Transition START {_fromScene} → {_sceneName}");

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
            float _cleanupT0 = Time.realtimeSinceStartup;
            if (_fromScene == SCENE_INGAME)
                CleanupInGame();
            Debug.Log($"[GameManager] CleanupInGame {(Time.realtimeSinceStartup - _cleanupT0)*1000f:F0}ms");

            // 씬 로드
            float _loadT0 = Time.realtimeSinceStartup;
            AsyncOperation _op = SceneManager.LoadSceneAsync(_sceneName);
            if (_op != null)
                while (!_op.isDone) yield return null;
            Debug.Log($"[GameManager] LoadSceneAsync {(Time.realtimeSinceStartup - _loadT0)*1000f:F0}ms");

            _currentScene = _sceneName;
            _isTransitioning = false;

            // RPAsset 씬별 switch — InGame 은 더 minimal RPAsset, 그 외는 default.
            // Inspector 미할당이면 변경 안 함 (안전).
            ApplyRPAssetForScene(_sceneName);

            // 인게임에서 빠져나온 경우 — fade-in 후 비동기로 자원 회수.
            // 동기 호출 시 디바이스에서 main thread 0.5~수 초 block.
            if (_fromScene == SCENE_INGAME)
                StartCoroutine(UnloadUnusedAssetsAsync());

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

            // InGame 진입 시 LevelManager 셋업 완료까지 대기 (씬 보이기 전 모든 셋업 끝남).
            // GameBootstrap.Start 가 LoadPendingLevel → LevelManager.LoadLevel 코루틴 시작.
            if (_sceneName == SCENE_INGAME)
            {
                // 1) LevelManager 가 LoadLevel 시작할 때까지 대기 (최대 1초 timeout — 셋업 안 도는 케이스 방어).
                float waitStart = Time.realtimeSinceStartup;
                while (Time.realtimeSinceStartup - waitStart < 1.0f)
                {
                    if (LevelManager.HasInstance && LevelManager.Instance.IsLoading) break;
                    yield return null;
                }
                // 2) 셋업 완료까지 대기.
                while (LevelManager.HasInstance && LevelManager.Instance.IsLoading)
                    yield return null;

                // 3) Warmup — fade overlay 가 아직 opaque 인 상태에서 몇 프레임 렌더하여
                //    first-frame shader 컴파일 / material 바인드 / 풍선 메시 인스턴싱 등 첫 프레임 비용을
                //    로딩 화면 뒤에서 흡수. 풍선 수가 많을 때 시작 직후 프레임 드랍 방지.
                for (int i = 0; i < 3; i++) yield return new WaitForEndOfFrame();

                // 4) 강제 최소 로딩 시간 보장 — 셋업이 빠르게 끝나도 사용자에게 일정 시간 로딩 화면 노출.
                float elapsed = Time.realtimeSinceStartup - _coStart;
                if (elapsed < MIN_INGAME_LOAD_DURATION)
                    yield return new WaitForSecondsRealtime(MIN_INGAME_LOAD_DURATION - elapsed);
            }

            // Fade In — 셋업 완전 종료 후
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

#if (UNITY_EDITOR || DEVELOPMENT_BUILD) && BALLOONFLOW_ENABLE_IMGUI_DEBUG
        [Header("[Debug UI]")]
        [Tooltip("InGame 우상단 디버그 패널 (TEST ITEM / FORCE FAIL / Lv.N / RESET 등). " +
                 "OnGUI 는 매 프레임 IMGUI 레이아웃 비용 큼 — 모바일 큰 부하. perf 측정 시 OFF.")]
        [SerializeField] private bool _showDebugGui = false;

        private GUIStyle _debugBtnStyle;

        private void OnGUI()
        {
            if (!_showDebugGui) return;
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
