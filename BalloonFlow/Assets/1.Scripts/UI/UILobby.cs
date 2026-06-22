using UnityEngine;
using UnityEngine.UI;
using UnityEngine.InputSystem;
using DG.Tweening;
using TMPro;
using System.Collections;

namespace BalloonFlow
{
    /// <summary>
    /// Lobby UI with horizontal page-swipe navigation.
    /// 3 pages: Shop (left), Home (center), Setting (right).
    /// Prefab already contains Shop/Lobby/Setting as direct children.
    /// On Awake, a PageContainer is auto-created and the 3 pages are reparented into it.
    /// BottomNav buttons slide the PageContainer left/right with DOTween.
    /// </summary>
    /// <remarks>Project-wide convention: flat 'namespace BalloonFlow' — do not nest.</remarks>
    /// <remarks>Not a singleton — scene-level MonoBehaviour managed by Unity lifecycle.</remarks>
    public class UILobby : UIBase
    {
        // [#15] 씬 UI(로비 메인) — 백버튼 비소비. 라우터가 로비 컨텍스트 Quit Game 처리.
        public override bool ConsumesBackButton => false;

        #region Constants

        private static readonly Color COLOR_NAV_ACTIVE   = Color.white;
        private static readonly Color COLOR_NAV_INACTIVE  = new Color(0x80 / 255f, 0x80 / 255f, 0x80 / 255f); // #808080

        private const float PAGE_SWIPE_DURATION = 0.3f;
        private const float NAV_TEXT_ANIM_DURATION = 0.2f;
        private const float ICON_SCALE_ACTIVE = 1.1f;
        private const float ICON_SCALE_INACTIVE = 0.9f;
        private const float ICON_Y_OFFSET = 25f; // 활성 +25, 비활성 -25
        private const float ICON_SCALE_DURATION = 0.2f;
        private const float WS_FIRE_FLY_DURATION = 0.55f;
        // FXItem_WinningStreak_Fly 비행 동안 동일 duration 으로 4.0 → 2.0 축소.
        // 이동 트윈과 Sequence.Join 으로 결합 — 시작/종료 시점 정확히 일치.
        private const float WS_FIRE_FLY_SCALE_START = 4.0f;
        private const float WS_FIRE_FLY_SCALE_END   = 2.0f;
        private const float WS_FIRE_PULSE_DURATION = 0.18f;
        private const float WS_SLIDER_FILL_DURATION = 0.45f;
        private const float WS_REWARD_RISE_DURATION = 0.55f;
        private const float WS_REWARD_RISE_Y = 130f;
        private const int WS_LEVEL_CLEAR_GOLD_FLY_COUNT = 10;
        // MultiplierMaskArea/Multiplier 슬라이드 — 연출 중 X=10 으로 튕겨 들어오고, 연출 종료 시 X=-725 로 빠짐.
        private const float WS_MULTIPLIER_SHOWN_X = 10f;
        private const float WS_MULTIPLIER_HIDDEN_X = -725f;
        private const float WS_MULTIPLIER_SLIDE_IN_DURATION = 0.4f;
        private const float WS_MULTIPLIER_SLIDE_OUT_DURATION = 0.35f;
        // [Multiplier 등장 띠용 완화 2026-06-22] DOTween OutBack 기본 1.70158 → 0.4 로 약화. owner 출처: 본 ProjectHub task [사용자 추가 지시] 2026-06-22 — 시작/최종 X 위치(WS_MULTIPLIER_HIDDEN_X / WS_MULTIPLIER_SHOWN_X)는 불변, overshoot 거리만 축소. 값 근거: OutBack overshoot 는 종점 초과 비율을 키우는 계수라 1.7 → 0.4 시 튀어나오는 정도가 약 4~5배 축소돼 '살짝 앞으로 이동 후 안착' 체감과 일치. (롤백: 1.7f 로 복원하면 직전 동작 재현. 0 으로 두면 OutQuad 수준이 됨.)
        private const float WS_MULTIPLIER_SLIDE_IN_OVERSHOOT = 0.4f;
        // 왜 0.5초인가: 텍스트 갱신+FXFire+Scale punch(0.20s)가 끝난 직후 즉시 slide-out이 시작되면 플레이어가 새 배수를 인지하지 못함. owner 추가 피드백(2026-06-22, task 6a34e951 후속 코멘트) 반영.
        private const float WS_HOLD_AFTER_TEXT_FIRE_DURATION = 0.5f;

        // Rail 슬라이드 인 연출 파라미터.
        // Top/Bottom Rail 모두 화면 위쪽 +120 에서 시작해 OutCubic 으로 제자리(0) 로 내려오는
        // 일관된 톱-다운 슬라이드인 연출. (사용자 피드백: 두 Rail 이 반대 방향으로 들어오는 것보다
        // 동일 방향으로 내려오는 쪽이 시각적 일체감이 좋음)
        // 부호 의도를 코드 자체로 표현하기 위해 두 상수를 분리해 둠. (값을 바꾸려면 각각 조정)
        private const float RAIL_TOP_ENTER_OFFSET_Y    = +120f;
        private const float RAIL_BOTTOM_ENTER_OFFSET_Y = +120f;
        private const float RAIL_ENTER_DURATION = 0.45f;

        // Rail 풀다운(아래방향 드래그) 연출 파라미터. 아래로 RAIL_PULL_DOWN_Y 까지 끌렸다가 0 으로 OutBack 복귀.
        private const float RAIL_PULL_DOWN_Y = -118.3002f;
        private const float RAIL_PULL_DOWN_DURATION = 0.18f;
        private const float RAIL_PULL_DOWN_RETURN_DURATION = 0.32f;

        private const float LEVEL_OBJECT_ENTER_START_Y = 1816f;
        private const float LEVEL_OBJECT_ENTER_END_Y   = 1145f;
        private const float LEVEL_OBJECT_ENTER_DURATION = 0.45f;

        #endregion

        #region Serialized Fields

        [Header("[TopBarArea]")]
        [SerializeField] private RectTransform _topBarArea;

        [Header("[TopBar — GoldPanel]")]
        [SerializeField] private TMP_Text _txtGold;
        [SerializeField] private TMP_Text _txtGoldOutline;
        [SerializeField] private Button _btnGoldPlus;
        [Tooltip("코인 fly 도착점 override (fly 전용). 미할당 시 _txtGold 사용. 펄스 연출은 _txtGold 부모 그대로.")]
        [SerializeField] private RectTransform _goldFlyTargetOverride;
        [Tooltip("GoldPanel 자식 FXFire ParticleSystem 원본(템플릿). 자체 재생 X — PlayGoldPanelFxFire() 호출마다 Instantiate 되어 N번째 발화가 N-1번째 인스턴스를 중단/리셋하지 않음. 미할당 시 _txtGold 부모(GoldPanel) 하위에서 'FXFire'/'FxFire' 이름으로 자동 탐색.")]
        [SerializeField] private GameObject _goldPanelFxFire;
        /// <summary>직전 PlayGoldPanelFxFire 가 Instantiate 한 인스턴스 — 살아있는 동안 중복 발화 차단.</summary>
        private GameObject _activeGoldPanelFxFireInstance;

        [Header("[TopBar — LifePanel]")]
        [SerializeField] private TMP_Text _txtLife;
        [SerializeField] private TMP_Text _txtLifeOutline;
        [SerializeField] private Button _btnLifePlus;
        [Tooltip("아이템 fly 도착점 override (fly 전용). 미할당 시 _txtLife 사용. 펄스 연출은 _txtLife 부모 그대로.")]
        [SerializeField] private RectTransform _lifeFlyTargetOverride;
        [SerializeField] private Image _imgLifeTimer;
        [SerializeField] private TMP_Text _txtLifeTimer;
        [SerializeField] private TMP_Text _txtLifeTimerOutline;
        [SerializeField] private Button _btnLifeBar;
        [SerializeField] private GameObject _imgInfinite;
        [Tooltip("LifePanel 자식 FXFire ParticleSystem 원본(템플릿). 자체 재생 X — PlayLifePanelFxFire() 호출마다 Instantiate 되어 N번째 발화가 N-1번째 인스턴스를 중단/리셋하지 않음. 미할당 시 _txtLife 부모(LifePanel) 하위에서 'FXFire'/'FxFire' 이름으로 자동 탐색.")]
        [SerializeField] private GameObject _lifePanelFxFire;
        /// <summary>직전 PlayLifePanelFxFire 가 Instantiate 한 인스턴스 — 살아있는 동안 중복 발화 차단.</summary>
        private GameObject _activeLifePanelFxFireInstance;
        // Life 증가 감지(이전 표시값 캐시) + multi-heart 시퀀스에서 FxFire 1회로 coalesce 하기 위한 debounce.
        private int _lastShownLife = -1;
        private float _lastFxFireTime = -1f;
        private const float FX_FIRE_DEBOUNCE_SEC = 1.5f;

        [Header("[Shop — 골드 표시]")]
        [SerializeField] private TMP_Text _txtShopGold;
        [SerializeField] private TMP_Text _txtShopGoldOutline;

        [Header("[Pages — 프리팹 내 기존 오브젝트 (Shop/Lobby/Setting)]")]
        [SerializeField] private RectTransform _pageShop;
        [SerializeField] private RectTransform _pageLobby;
        [SerializeField] private RectTransform _pageSetting;

        [Header("[BottomNavArea]")]
        [SerializeField] private Button _btnShop;
        [SerializeField] private Button _btnHome;
        [SerializeField] private Button _btnSetting;

        [Header("[BottomNav — ImageOnClick (활성 하이라이트)]")]
        [SerializeField] private GameObject _imgOnClickShop;
        [SerializeField] private GameObject _imgOnClickHome;
        [SerializeField] private GameObject _imgOnClickSetting;

        [Header("[BottomNav — ImageLine (Home 활성 시 숨김)]")]
        [SerializeField] private GameObject _imgLineShop;
        [SerializeField] private GameObject _imgLineSetting;

        [Header("[BottomNav — Icon Images]")]
        [SerializeField] private Image _iconShop;
        [SerializeField] private Image _iconHome;
        [SerializeField] private Image _iconSetting;

        [Header("[BottomNav — Label Texts (활성 시에만 표시)]")]
        [SerializeField] private TMP_Text _txtShop;
        [SerializeField] private TMP_Text _txtHome;
        [SerializeField] private TMP_Text _txtSetting;

        [Header("[Home Page — LevelObject]")]
        [SerializeField] private RectTransform _levelBoxContainer;
        [SerializeField] private GameObject _lobbyRailBoxPrefab;
        [SerializeField] private int _visibleBoxCount = 5;

        [Header("[Home Page — Play Button]")]
        [SerializeField] private Button _btnPlay;
        [SerializeField] private Image _imgPlayButton;
        [SerializeField] private TMP_Text _txtPlay;
        [SerializeField] private TMP_Text _txtPlayOutline;
        [SerializeField] private TMP_Text _txtPlayLevel;
        [SerializeField] private TMP_Text _txtPlayLevelOutline;
        private int _currentPlayLevelId;
        private DifficultyPurpose _currentPlayDifficulty = DifficultyPurpose.Normal;
        [SerializeField] private Sprite _sprBtnGreen;
        [SerializeField] private Sprite _sprBtnPurple;
        [SerializeField] private Sprite _sprBtnRed;

        [Header("[TxtPlayOutline — 난이도별 Material Preset]")]
        [SerializeField] private Material _matPlayOutlineNormal;
        [SerializeField] private Material _matPlayOutlineHard;
        [SerializeField] private Material _matPlayOutlineSuperHard;

        [Header("[TxtPlayLevelOutline — 난이도별 Material Preset]")]
        [SerializeField] private Material _matPlayLevelOutlineNormal;
        [SerializeField] private Material _matPlayLevelOutlineHard;
        [SerializeField] private Material _matPlayLevelOutlineSuperHard;

        [Header("[Home Page — Play Button Badge (x3/x5)]")]
        [SerializeField] private Image _imgPlayBadge;
        [SerializeField] private Sprite _sprBadgeX3;
        [SerializeField] private Sprite _sprBadgeX5;

        [Header("[Home Page — Play Button Change Animator]")]
        [SerializeField] private Animator _animPlayBtn;

        [Tooltip("PlayButton 자식 Sparks ParticleSystem 원본(템플릿). 자체 재생 X — PlayPlayButtonSparks() 호출마다 Instantiate. 미할당 시 _btnPlay 하위에서 'Sparks'/'FXSparks'/'FxSparks' 이름으로 자동 탐색.")]
        [SerializeField] private GameObject _playButtonSparks;
        private GameObject _activePlayButtonSparksInstance;

        [Header("[RightArea — Lobby Page]")]
        [SerializeField] private Button _btnNoAds;
        [SerializeField] private Button _btnProfilePanel;
        /// <summary>UILobby.prefab 의 RightArea — WinningStreak 버튼. 미할당 시 클릭 핸들러는 등록되지 않음(silent skip).</summary>
        [Tooltip("WinningStreak 팝업 진입 버튼 — 클릭 시 PopupWinningStreak 오픈")]
        [SerializeField] private Button _btnWinningStreak;

        [Header("[WinningStreak — 로비 미니 표시 (미할당 시 해당 항목 갱신 skip)]")]
        [Tooltip("연승 진행 게이지 Slider — 현재 stage 포인트/요구치 비율(0~1).")]
        [SerializeField] private Slider _wsProgressSlider;
        [Tooltip("WS 미니 표시 root — 내부에서 배수(TextGauge/Outline)·시간(TextTimer/Outline)을 이름으로 탐색. 미할당 시 WS 버튼 기준.")]
        [SerializeField] private GameObject _wsDisplayRoot;
        [Tooltip("현재 stage 대표 보상 — RewardItem 구조(내부 아이콘 Image + TextReward/Outline). root 를 할당.")]
        [SerializeField] private GameObject _wsRewardItem;
        // ROLLBACK_WS_REWARD_VARIANT_SERIALIZE_20260616: 변형 GO 를 이름 탐색(FindDirectChildGO) 대신 직접 참조.
        //   노드 리네임(RewardGold/RewardItem ↔ Gold/Item)에 깨지지 않음. 미할당 시 기존 이름 탐색으로 폴백(회귀 0).
        [Tooltip("코인 보상 변형 GO (RewardItem>Gold). 미할당 시 이름 'Gold'/'RewardGold' 로 자동 탐색.")]
        [SerializeField] private GameObject _wsRewardGoldVariant;
        [Tooltip("아이템 보상 변형 GO (RewardItem>Item). 미할당 시 이름 'Item'/'RewardItem' 로 자동 탐색.")]
        [SerializeField] private GameObject _wsRewardItemVariant;

        [Header("[WinningStreak — 로비 FX 연출 참조 (미할당 시 이름으로 자동 탐색)]")]
        [Tooltip("WinningStreak/FXFire — 게이지 위 불꽃. 펄스(커졌다 작아짐) 대상. 미할당 시 root 하위 'FxFire'/'FXFire' 탐색.")]
        [SerializeField] private GameObject _wsFxFire;
        [Tooltip("WinningStreak/FXLight - disabled on lobby entry; enabled only by pending WS reward animation.")]
        [SerializeField] private GameObject _wsFxLight;
        [Tooltip("WinningStreak/FXReward — 보상 상승·페이드 연출 오브젝트. 미할당 시 root 하위 'FxReward'/'FXReward' 탐색.")]
        [SerializeField] private GameObject _wsFxReward;
        [Tooltip("날아온 FXFire 가 도착할 위치 RectTransform. 보통 FXFire 의 RectTransform. 미할당 시 _wsFxFire 기준.")]
        [SerializeField] private RectTransform _wsFxFireTarget;
        [Tooltip("WinningStreak/MultiplierMaskArea/Multiplier RectTransform. 연출 중 X=10 으로 튕겨 들어오고 종료 시 X=-725 로 빠짐. 미할당 시 'MultiplierMaskArea'→'Multiplier' 탐색.")]
        [SerializeField] private RectTransform _wsMultiplier;
        [Tooltip("WinningIcon > Multiple > FXFire — 배수(TextGauge/Outline) 수치 변경 시 1회 재생. Loop=off. 미할당 시 root 하위 WinningIcon→Multiple→FXFire/FxFire 명시 경로 탐색.")]
        [SerializeField] private GameObject _wsMultipleFxFire;
        private float _wsTimerTick;

        // 이름 기반 텍스트 쌍 캐시 (outline + main).
        private TMP_Text _wsTxtTimer, _wsTxtTimerOutline, _wsTxtGauge, _wsTxtGaugeOutline;
        // [WS 게이지 텍스트 2026-06-11] 프리팹엔 TextGauge/Outline 쌍이 둘 — Gauge 하단(포인트 {n}/{n})과
        // WinningIcon 하단(배수 x{n}). 전역 단일 탐색은 첫 쌍에 배수를 써버려 포인트 표기가 없었음.
        private TMP_Text _wsTxtPoints, _wsTxtPointsOutline;
        private string _wsLastMultiplierText;
        // PlayWinningStreakLobbyFx 진행 중 텍스트/FXFire 발화 보류 플래그.
        // true 인 동안 SetWinningIconMultiplierText 는 _wsTxtGauge/Outline 쓰기와
        // PlayWsMultipleFxFire 호출을 둘 다 skip — 릴리즈 시점은 SelectFrame 이동(0.18s) 완료 직후
        // → 텍스트/FX 1회 발사 → 이후 Multiplier 슬라이드 아웃. (기획 시퀀스: gauge → multiplier in
        // → SelectFrame move → TextGauge/FXFire → multiplier out.)
        // SUPERSEDES [2026-06-22 직전 결정: 슬라이드-아웃 완전 종료 후 FX] — 본 태스크 사용자 추가 지시 (2026-06-22, hermes task 6a34e951)로 변경.
        private bool _wsHoldMultiplierTextDuringAnim;
        private bool _wsTextsResolved;
        private Coroutine _wsLobbyFxCoroutine;
        private Sequence _wsLobbyFxSequence;
        // [WS 배수 텍스트 펀치 2026-06-22] 별도 Sequence — _wsLobbyFxSequence 는 lobby FX 전 경로에서
        // Kill/재할당되어 punch 가 clobber 될 수 있어 분리.
        private Sequence _wsMultiplierTextPunchSeq;
        // ROLLBACK_WS_FX_ARMED_20260616:
        // TriggerPendingWinningStreakLobbyFx 가 _wsLobbyFxCoroutine 을 StartCoroutine 으로 띄운 직후라도
        // 본문의 첫 yield 가 실행되기 전 한 프레임 동안은 외부에서 보면 '곧 시작될 예정' 상태다.
        // 같은 프레임에 RefreshDisplay → PlayLobbyBtnChangeAnim 이 호출되면 코루틴 시작 윈도우에 끼어들어
        // 두 연출(WS 보상 팝업 + LobbyBtn 변경)이 겹쳐 보였음. armed 비트로 '예약됨'을 노출해 외부에서 게이트.
        private bool _wsLobbyFxArmed;

        [Header("[Profile Display — 좌상단 표시 sprite]")]
        [Tooltip("PopupProfile 과 동일한 ProfileAssets ScriptableObject. 아이콘/프레임 sprite 카탈로그.")]
        [SerializeField] private ProfileAssets _profileAssets;
        [Tooltip("Lobby 좌상단 프로필 아이콘 Image. UserData.profileIconNumber 로 sprite 자동 설정.")]
        [SerializeField] private Image _imgProfileIcon;
        [Tooltip("Lobby 좌상단 프로필 프레임 Image. UserData.profileFrameNumber 로 sprite 자동 설정.")]
        [SerializeField] private Image _imgProfileFrame;

        [Header("[Rail Enter Animation]")]
        [SerializeField] private RectTransform _railTop;
        [SerializeField] private RectTransform _railBottom;

        #endregion

        #region Fields

        private int _currentPageIndex = 1; // 0=Shop, 1=Home(Lobby), 2=Setting
        private Tweener _pageTween;
        private LobbyRailBox[] _railBoxes;
        private RectTransform _pageContainer;
        private float _pageWidth;
        private UIShop _uiShop;

        // Gold text display tween — 카운트업/다운 연출용 캐시값
        private int _displayedCoins;
        private Tweener _goldTween;

        // Rail 슬라이드 인 / 풀다운 트윈 캐시 (Sequence 도 담을 수 있도록 Tween 타입).
        private Tween _railTopTween;
        private Tween _railBottomTween;
        private Tweener _levelObjectEnterTween;

        // Nav text base Y positions (cached before animation)
        private float _baseYShop, _baseYHome, _baseYSetting;
        // Nav icon base Y positions
        private float _iconBaseYShop, _iconBaseYHome, _iconBaseYSetting;
        private bool _baseYCached;

        // Swipe drag
        private bool _isDragging;
        private float _dragStartScreenX;
        private float _dragStartPageX;
        private float _dragLastScreenX; // 마지막 터치 위치 저장
        private float _dragLastScreenY; // 풀다운 판정용 — 종료 시점 deltaY 계산
        private const float SWIPE_THRESHOLD_RATIO = 0.2f; // 화면 폭의 20%

        // 직전 OnPageArrived 도착 페이지. 같은 Shop 탭 내 vertical drag/short tap 으로 재도착 시
        // ResetView 가 다시 불려 스크롤이 상단으로 점프하던 버그를 막기 위한 가드용 캐시.
        private int _lastArrivedPageIndex = -1;

        // LobbyBtnChange 애니메이션 — 레벨업 시 PlayButton 표기 갱신을 20프레임 시점에 일괄 처리.
        // AnimationEvent(.anim) 와 코루틴 폴백 양쪽에서 동일 메서드를 호출하므로
        // _hasPendingChange 가드로 1회만 적용한다.
        private int _pendingLevelId;
        private DifficultyPurpose _pendingDifficulty;
        private System.Action _onChangeAnimFrameEvent;
        private bool _hasPendingChange;
        private Coroutine _changeAnimCoroutine;
        private const float LOBBY_BTN_CHANGE_FRAME_TIME = 20f / 60f; // 20프레임 @ 60fps
        private const string LOBBY_BTN_CHANGE_ANIM_NAME = "LobbyBtnChange";
        private const string LOBBY_BTN_IDLE_ANIM_NAME = "LobbyBtn";
        // Change 애니가 끝까지 재생된 직후 idle 로 자연 복귀가 보장되지 않을 때를 대비한 안전망 지연.
        private const float LOBBY_BTN_RETURN_TO_IDLE_DELAY = LOBBY_BTN_CHANGE_FRAME_TIME + 0.4f;

        #endregion

        #region Properties

        public Button BtnPlay => _btnPlay;

        // ROLLBACK_LOBBY_PLAY_BLOCK_DURING_WSFX_20260615:
        // WS 로비 연출 재생 중 여부 — PlayButton 인게임 진입 차단용.
        //   _wsLobbyFxArmed             : 코루틴 StartCoroutine 직후 ~ 본문 첫 yield 사이의 한 프레임 윈도우 보정
        //   _wsLobbyFxCoroutine != null  : FXItem 비행 → 게이지 채움 → 배수/보상 연출(PlayPendingWinningStreakLobbyFxDeferred) 진행 중
        //   PopupWinningStreakReward.IsShowing : 0단계 Dim 보상 팝업(수 연산 카운터) 표시 중
        //   셋 중 하나라도 true 면 연출 중 → 진입 금지. 모두 끝나면 진입 가능.
        public bool IsWinningStreakFxPlaying => _wsLobbyFxArmed || _wsLobbyFxCoroutine != null || PopupWinningStreakReward.IsShowing;

        public Button BtnGoldPlus => _btnGoldPlus;
        public Button BtnLifePlus => _btnLifePlus;
        public Button BtnLifeBar => _btnLifeBar;
        public Button BtnShop => _btnShop;
        public Button BtnHome => _btnHome;
        public Button BtnSetting => _btnSetting;
        public Button BtnNoAds => _btnNoAds;
        public Button BtnProfilePanel => _btnProfilePanel;
        public Button BtnWinningStreak => _btnWinningStreak;
        public int CurrentPageIndex => _currentPageIndex;

        #endregion

        #region Lifecycle

        protected override void Awake()
        {
            base.Awake();

            BuildPageContainer();
            _uiShop = _pageShop != null ? _pageShop.GetComponent<UIShop>() : null;
            CacheNavTextBaseY();
            ResolveRailRefs();
            DisableDynamicPlayLevelLocalization();
            LocalizationService.OnLanguageChanged += HandleLobbyLanguageChanged;

            // [2026-05-12] RemoveAllListeners — Inspector 의 onClick wire (의도되지 않은 prefab wire) + 코드 wire 중복 방지.
            // 증상: Shop 패널 열 때 연출 2번 발생 (Inspector + code 둘 다 GoToPage(0) 호출).
            if (_btnShop != null)
            {
                _btnShop.onClick.RemoveAllListeners();
                _btnShop.onClick.AddListener(() => GoToPage(0));
            }
            if (_btnHome != null)
            {
                _btnHome.onClick.RemoveAllListeners();
                _btnHome.onClick.AddListener(() => GoToPage(1));
            }
            if (_btnSetting != null)
            {
                _btnSetting.onClick.RemoveAllListeners();
                _btnSetting.onClick.AddListener(() => GoToPage(2));
            }
            if (_btnPlay != null)
            {
                _btnPlay.onClick.RemoveAllListeners();
            }
            if (_btnWinningStreak != null)
            {
                _btnWinningStreak.onClick.RemoveAllListeners();
                _btnWinningStreak.onClick.AddListener(() =>
                {
                    // 미해금 상태에서도 prefab 단계 wire 가 살아있을 수 있으므로 클릭 시점에도 게이트 재확인.
                    if (!IsWinningStreakUnlocked()) return;
                    if (UIManager.HasInstance)
                        UIManager.Instance.OpenUI<PopupWinningStreak>(Const.POPUP_WINNING_STREAK);
                });
            }

            AutoConfigureShopScroll();

            // [PopupTextInventory 정합] prefab 정적 텍스트 일괄 정정 — 'Setting'→'Settings', 'No Ads'→'NO ADS' 등.
            // prefab 이 binary 직렬화라 m_text 직접 수정 불가 → 런타임 OnEnable 단계에서 강제 덮어쓰기.
            ApplyStaticTextOverrides();
            EventBus.Subscribe<OnAdsRemovedChanged>(HandleAdsRemovedChanged);

            // Start on Home(Lobby) page
            SetPageImmediate(1);

            // FxGold 도착+PulseGoldPanel 결합 전 prefab Play-On-Awake 자동 재생 차단.
            ResolveGoldPanelFxFire();
            DisableGoldPanelFxFireOnEnter();
            ResolveLifePanelFxFire();
            DisableLifePanelFxFireOnEnter();
            ResolvePlayButtonSparks();
            DisablePlayButtonSparksOnEnter();
            ResolveWsFxRefs();
            DisableWinningStreakFxOnEnter();

            // 최초 진입 시 Rail 슬라이드 인 연출 1회 재생
            PlayRailEnterAnimation();
            PlayLevelObjectEnterAnimation();

            // AnimatorController(LobbyPlayBtn.controller)의 default state 가 LobbyBtnChange 로 잡혀
            // 프리팹 enable 직후 의도치 않게 변경 애니가 자동 재생되는 문제 차단.
            // 첫 프레임 전에 idle 로 강제 고정한다.
            EnsureLobbyBtnIdle();

            // [2026-05-13] 프로필 표시 — UserData ready 이후 1회 + 변경 시마다 refresh.
            HookProfileEvents();
            RefreshProfileDisplay();

            // [2026-05-20] WinningStreak 버튼 — unlockLevel(34) 도달 전엔 숨김.
            HookWinningStreakEvents();
            RefreshWinningStreakVisibility();
            RefreshNoAdsVisibility();
            // WS 로비 연출 트리거는 OpenUI() 로 이동 — UILobby 는 UiTr(DontDestroyOnLoad) 아래 지속 인스턴스라
            // Awake 는 최초 1회만 실행됨. 매 로비 진입(인게임→로비 재진입 포함)마다 연출되도록 OpenUI 에서 발동한다.
        }

        /// <summary>로비가 열릴 때마다(신규 생성·재사용 모두 OpenUI 경유) 호출. 대기 중인 WS 로비 연출을 재생한다.</summary>
        public override void OpenUI()
        {
            base.OpenUI();
            RefreshNoAdsVisibility();
            DisableWinningStreakFxOnEnter();
            RefreshWinningStreakVisibility();
            // [2026-06-15] PopupWinningStreak(라운드 자동 팝업) 먼저 오픈 → 닫힌 뒤 Reward 연출 진행. 동시 2겹 노출 방지.
            TryAutoOpenWinningStreakPopup();
            TriggerPendingWinningStreakLobbyFx();
        }

        /// <summary>대기 중인 WS 로비 연출 코루틴을 (재)시작. 진행 중이면 중단 후 재시작해 매 진입마다 확실히 발동.</summary>
        private void TriggerPendingWinningStreakLobbyFx()
        {
            // ROLLBACK_WINNING_STREAK_KEEP_RUNNING_LOBBY_FX_20260617:
            // Previous behavior stopped the running coroutine on every OpenUI call. If the coroutine
            // had already dequeued a PendingLobbyAnimation, stopping it could lose the lobby FX.
            if (_wsLobbyFxCoroutine != null) return;

            // ROLLBACK_WS_LOBBY_FX_PENDING_GATE_20260618:
            // Lv.34 clear opens the unlock info only; scoring starts after Lv.35 clear.
            // If there is no queued reward/fail FX, do not arm IsWinningStreakFxPlaying because
            // that can keep the lobby Play button blocked while an unrelated info popup is open.
            if (!WinningStreakManager.HasInstance || !WinningStreakManager.Instance.HasPendingLobbyFx)
            {
                if (WinningStreakManager.HasInstance)
                    WinningStreakManager.Instance.ClaimAllAchievedStages();
                _wsLobbyFxArmed = false;
                return;
            }

            _wsLobbyFxArmed = true;
            _wsLobbyFxCoroutine = StartCoroutine(PlayPendingWinningStreakLobbyFxDeferred());
        }

        // ── WinningStreak 자동 팝업 (명세 §5.4 해금 안내 1회 + §11.2 회차 첫 진입 1회) ──
        private const string WS_PREFS_UNLOCK_POPUP_SHOWN = "BF_WS_UnlockPopupShown";
        private const string WS_PREFS_ROUND_POPUP_SHOWN  = "BF_WS_RoundPopupShown";

        /// <summary>WS 자동 팝업. 해금 안내는 Lv.34 클리어 후 다음 레벨이 35가 된 시점(IsUnlocked), 메인 회차 팝업은 실제 적립 시작 이후(IsScoringActive).</summary>
        private void TryAutoOpenWinningStreakPopup()
        {
            if (!WinningStreakManager.HasInstance || !UIManager.HasInstance) return;
            var wsm = WinningStreakManager.Instance;
            // ROLLBACK_WS_UNLOCK_INFO_AT_REACHED_LEVEL_20260618:
            // The first info popup must appear after clearing Lv.34, when the lobby's next level is Lv.35.
            // IsScoringActive is intentionally later (after clearing Lv.35) and caused the popup to be
            // delayed by one stage, colliding with the first reward animation.
            if (!wsm.IsUnlocked) return;

            // 1) 최초 해금 안내(튜토리얼) 1회 — 영구 플래그
            if (PlayerPrefs.GetInt(WS_PREFS_UNLOCK_POPUP_SHOWN, 0) == 0)
            {
                PlayerPrefs.SetInt(WS_PREFS_UNLOCK_POPUP_SHOWN, 1);
                PlayerPrefs.Save();
                // ROLLBACK_WS_INTRO_SCROLL_THEN_INFO_20260619: 최초 해금 = PopupWinningStreak 오픈 → item1→25 자동
                //   스크롤(2.5s, 입력잠금) → PopupWinningStreakInfo. Info 닫으면 둘 다 닫혀 로비 복귀.
                var wsIntro = UIManager.Instance.OpenUI<PopupWinningStreak>(Const.POPUP_WINNING_STREAK);
                if (wsIntro != null) wsIntro.PlayIntroScrollThenInfo();
                else UIManager.Instance.OpenUI<PopupWinningStreakInfo>(Const.POPUP_WINNING_STREAK_INFO); // 폴백
                return;
            }

            if (IsWinningStreakFxPlaying) return;

            // ROLLBACK_WS_REWARD_FX_BEFORE_ROUND_POPUP_20260618:
            // If a reward/fail lobby FX is waiting, let it play first. Auto-opening the round popup
            // here makes the FX coroutine wait on the popup and keeps the Play button blocked.
            if (wsm.HasPendingLobbyFx) return;

            // 2) 회차 메인 팝업은 점수/보상 적립이 실제로 시작된 뒤부터만 자동 노출.
            if (!wsm.IsScoringActive) return;

            // 3) 회차 첫 로비 진입 1회 — activeRoundId 가 바뀌면(새 회차) 재노출
            string roundId = wsm.State != null ? wsm.State.activeRoundId : null;
            if (string.IsNullOrEmpty(roundId)) return;
            if (PlayerPrefs.GetString(WS_PREFS_ROUND_POPUP_SHOWN, "") == roundId) return;
            PlayerPrefs.SetString(WS_PREFS_ROUND_POPUP_SHOWN, roundId);
            PlayerPrefs.Save();
            UIManager.Instance.OpenUI<PopupWinningStreak>(Const.POPUP_WINNING_STREAK);
        }

        private void HookProfileEvents()
        {
            if (!UserDataService.HasInstance) return;
            var svc = UserDataService.Instance;
            svc.OnUserDataReady += RefreshProfileDisplay;
            svc.OnProfileChanged += RefreshProfileDisplay;
            svc.OnUserDataReady += RefreshNoAdsVisibility;
        }

        private void UnhookProfileEvents()
        {
            if (!UserDataService.HasInstance) return;
            var svc = UserDataService.Instance;
            svc.OnUserDataReady -= RefreshProfileDisplay;
            svc.OnProfileChanged -= RefreshProfileDisplay;
            svc.OnUserDataReady -= RefreshNoAdsVisibility;
        }

        // ── WinningStreak 버튼 노출 게이트 ─────────────────────────

        /// <summary>UserData/Config 가 ready 되면 버튼 가시성 자동 갱신.
        /// - UserDataService.OnUserDataReady       : 신규 유저 또는 첫 진입
        /// - WinningStreakConfigService.OnConfigLoaded : unlockLevel 이 Firestore 에서 도착
        /// - WinningStreakManager.OnStateChanged   : 레벨 클리어로 highestClearedLevel 증가 시 즉시 반영</summary>
        private void HookWinningStreakEvents()
        {
            if (UserDataService.HasInstance)
                UserDataService.Instance.OnUserDataReady += HandleWinningStreakRuntimeChanged;
            if (WinningStreakConfigService.HasInstance)
                WinningStreakConfigService.Instance.OnConfigLoaded += HandleWinningStreakRuntimeChanged;
            if (WinningStreakManager.HasInstance)
                WinningStreakManager.Instance.OnStateChanged += HandleWinningStreakRuntimeChanged;
        }

        private void UnhookWinningStreakEvents()
        {
            if (UserDataService.HasInstance)
                UserDataService.Instance.OnUserDataReady -= HandleWinningStreakRuntimeChanged;
            if (WinningStreakConfigService.HasInstance)
                WinningStreakConfigService.Instance.OnConfigLoaded -= HandleWinningStreakRuntimeChanged;
            if (WinningStreakManager.HasInstance)
                WinningStreakManager.Instance.OnStateChanged -= HandleWinningStreakRuntimeChanged;
        }

        private void HandleWinningStreakRuntimeChanged()
        {
            RefreshWinningStreakVisibility();
            if (!isActiveAndEnabled || !gameObject.activeInHierarchy) return;

            // ROLLBACK_WS_LATE_READY_LOBBY_FX_20260618:
            // Device builds can receive user data/config after UILobby.OpenUI already tried to
            // trigger the WS popup/FX. Re-run the same gates on state/config readiness so the
            // reward animation plays immediately instead of only after app restart/re-enter.
            TryAutoOpenWinningStreakPopup();
            TriggerPendingWinningStreakLobbyFx();
        }

        private bool IsWinningStreakUnlocked()
        {
            // 엄격 서버 기준: 서버 config 기반 Manager 판정만 사용. config 미로드 시 IsUnlocked=false → 미노출.
            return WinningStreakManager.HasInstance && WinningStreakManager.Instance.IsUnlocked;
        }

        private void RefreshWinningStreakVisibility()
        {
            bool unlocked = IsWinningStreakUnlocked();

            // ROLLBACK_WS_DISPLAY_ROOT_HIDE_20260619: 버튼뿐 아니라 WS 표시 root(로비 미니 게이지/FX 패널)도 unlock
            //   게이트로 토글. 기존엔 버튼만 숨기고 root 는 안 건드려 → 미해금(UserData reset 등)에도 게이지/FX 가
            //   그대로 남아 "WS 가 나옴". reset → OnUserDataReady → 이 메서드 호출 시 root 까지 숨겨 WS 완전 제거.
            if (_wsDisplayRoot != null && _wsDisplayRoot.activeSelf != unlocked)
                _wsDisplayRoot.SetActive(unlocked);

            if (_btnWinningStreak != null && _btnWinningStreak.gameObject.activeSelf != unlocked)
                _btnWinningStreak.gameObject.SetActive(unlocked);

            if (unlocked) RefreshWinningStreakDisplay();
        }

        private void HandleAdsRemovedChanged(OnAdsRemovedChanged evt)
        {
            if (evt.removed) RefreshNoAdsVisibility();
        }

        private void RefreshNoAdsVisibility()
        {
            if (_btnNoAds == null) return;
            bool shouldShow = !IsAdsRemoved();
            if (_btnNoAds.gameObject.activeSelf != shouldShow)
                _btnNoAds.gameObject.SetActive(shouldShow);
        }

        private static bool IsAdsRemoved()
        {
            if (IAPManager.HasInstance && IAPManager.Instance.AdsRemoved)
                return true;
            if (UserDataService.HasInstance && UserDataService.Instance.IsReady
                && UserDataService.Instance.CurrentUser != null
                && UserDataService.Instance.CurrentUser.removedAds)
                return true;
            return PlayerPrefs.GetInt(Const.PREFS_AD_REMOVED, 0) == 1
                || PlayerPrefs.GetInt(Const.PREFS_NO_ADS_OWNED, 0) == 1;
        }

        /// <summary>[프리뷰 전용] unlock 게이트와 무관하게 WS UI(표시 root + 버튼)를 무조건 활성화.
        /// _wsDisplayRoot 미할당 시 FXFire 의 부모(WinningStreak 루트)로 폴백.</summary>
        private void ForceShowWinningStreakUI()
        {
            ResolveWsFxRefs();
            GameObject root = _wsDisplayRoot != null
                ? _wsDisplayRoot
                : (_wsFxFire != null && _wsFxFire.transform.parent != null ? _wsFxFire.transform.parent.gameObject : null);
            if (root != null && !root.activeSelf) root.SetActive(true);
            if (_btnWinningStreak != null && !_btnWinningStreak.gameObject.activeSelf)
                _btnWinningStreak.gameObject.SetActive(true);

            // 강제표시(프리뷰)에서도 현재 배율 숫자(TextGauge)·Multiplier 가 보이게.
            RefreshWsMultiplierDisplay();
        }

        /// <summary>로비 WS 미니 표시(진행 게이지/배수/타이머/보상 아이콘) 갱신.
        /// OnStateChanged/OnConfigLoaded/OnUserDataReady 시 호출. 타이머는 Update 에서 매초 추가 갱신.</summary>
        private void RefreshWinningStreakDisplay()
        {
            if (!WinningStreakManager.HasInstance) return;
            var mgr = WinningStreakManager.Instance;
            if (!mgr.IsUnlocked) return;

            var state = mgr.State;
            ResolveWsTexts();

            // 현재 배율(TextGauge/Outline 숫자 + Multiplier SelectFrame/TextYellow 위치) 반영.
            RefreshWsMultiplierDisplay();

            // 회차 남은 시간 (TextTimer + Outline). Update 가 매초 갱신하지만 즉시 1회도 세팅.
            {
                string time = FormatWsRoundRemaining(mgr.RoundRemaining);
                if (_wsTxtTimer != null) _wsTxtTimer.text = time;
                if (_wsTxtTimerOutline != null) _wsTxtTimerOutline.text = time;
            }

            // 현재 stage 진행 게이지 + 대표 보상(RewardItem)
            WinningStreakStage stage = (state != null && WinningStreakConfigService.HasInstance)
                ? WinningStreakConfigService.Instance.GetStage(state.currentStage) : null;

            if (_wsProgressSlider != null)
            {
                float ratio = (stage != null && stage.requiredPoints > 0 && state != null)
                    ? Mathf.Clamp01((float)state.currentStagePoints / stage.requiredPoints) : 0f;
                _wsProgressSlider.value = ratio;
            }

            // Gauge 하단 포인트 텍스트 — "현재 포인트/필요 포인트".
            SetWsPointsText(state != null ? state.currentStagePoints : 0, stage);

            BindWsRewardItem(stage);
        }

        /// <summary>현재 배율(ResolveCurrentMultiplier)을 WinningIcon 하단 TextGauge/Outline 숫자 + Multiplier SelectFrame/TextYellow 위치에 반영.
        /// [2026-06-10] Animator 방식 폐기 → 코드 트윈(PlayMultiplierSelect). Animator 가 위치를 덮어써 이동이 안 먹던 문제 해소.
        /// IsUnlocked 게이트와 무관하게 호출 가능 — 로비 표시·프리뷰(강제표시) 양쪽에서 항상 현재 배율 숫자가 보이게 한다.</summary>
        private void RefreshWsMultiplierDisplay()
        {
            ResolveWsTexts();
            int curMultiplier = WinningStreakUI.ResolveCurrentMultiplier();
            string mult = $"x{curMultiplier}";
            SetWinningIconMultiplierText(mult);
            ResolveWsFxRefs();
            if (_wsMultiplier != null)
                WinningStreakUI.PlayLobbyMultiplierSelect(_wsMultiplier, curMultiplier, curMultiplier);
        }

        // 배수 텍스트 이전 값과 달라질 때마다 1회 발화.
        private void SetWinningIconMultiplierText(string mult)
        {
            if (_wsHoldMultiplierTextDuringAnim) return; // Multiplier 연출 중에는 텍스트/FXFire 모두 보류
            ResolveWsTexts();
            bool changed = _wsLastMultiplierText != null && _wsLastMultiplierText != mult;
            if (_wsTxtGauge != null) _wsTxtGauge.text = mult;
            if (_wsTxtGaugeOutline != null) _wsTxtGaugeOutline.text = mult;
            _wsLastMultiplierText = mult;
            if (changed) PlayWsMultipleFxFire();
            // [2026-06-22] 배수 텍스트 펀치 — PlayWsMultipleFxFire 와 동일 changed-edge 에서 호출 (1/1, INC+DEC 공용).
            if (changed) PlayWsMultiplierTextPunch();
        }

        private void PlayWsMultipleFxFire()
        {
            ResolveWsFxRefs();
            if (_wsMultipleFxFire == null) return;
            _wsMultipleFxFire.SetActive(true);
            var systems = _wsMultipleFxFire.GetComponentsInChildren<ParticleSystem>(true);
            for (int i = 0; i < systems.Length; i++)
            {
                if (systems[i] == null) continue;
                systems[i].Clear(true);
                systems[i].Play(true);
            }
        }

        // [WS 배수 텍스트 펀치 2026-06-22] When: SetWinningIconMultiplierText 의 changed-edge (배수 텍스트가 실제로 다른 값으로 갱신되는 단 한 순간).
        // What: _wsTxtGauge/_wsTxtGaugeOutline transform scale 1 → 1.1 → 1 (총 0.20s, 실시간).
        // Why: 사용자 피드백 — 배수 변경 순간 텍스트 강조. INCREASE/DECREASE 양쪽 모두 hold-and-fire 패턴으로
        //      RefreshWinningStreakDisplay() 후 changed=true 한 번에서 트리거되므로 양 케이스 자동 커버 (호출 1/1).
        private void PlayWsMultiplierTextPunch()
        {
            ResolveWsTexts();
            _wsMultiplierTextPunchSeq?.Kill();
            if (_wsTxtGauge == null && _wsTxtGaugeOutline == null) return;

            if (_wsTxtGauge != null) _wsTxtGauge.transform.localScale = Vector3.one;
            if (_wsTxtGaugeOutline != null) _wsTxtGaugeOutline.transform.localScale = Vector3.one;

            _wsMultiplierTextPunchSeq = DOTween.Sequence().SetUpdate(true);
            // up: 1 → 1.1 (0.08s) — 두 타겟을 동일 time slot 에 Append + Join.
            if (_wsTxtGauge != null)
                _wsMultiplierTextPunchSeq.Append(_wsTxtGauge.transform.DOScale(1.1f, 0.08f).SetEase(Ease.OutQuad));
            if (_wsTxtGaugeOutline != null)
                _wsMultiplierTextPunchSeq.Join(_wsTxtGaugeOutline.transform.DOScale(1.1f, 0.08f).SetEase(Ease.OutQuad));
            // down: 1.1 → 1 (0.12s)
            if (_wsTxtGauge != null)
                _wsMultiplierTextPunchSeq.Append(_wsTxtGauge.transform.DOScale(1.0f, 0.12f).SetEase(Ease.OutQuad));
            if (_wsTxtGaugeOutline != null)
                _wsMultiplierTextPunchSeq.Join(_wsTxtGaugeOutline.transform.DOScale(1.0f, 0.12f).SetEase(Ease.OutQuad));
        }

        /// <summary>Gauge 하단 TextGauge/Outline 에 "{현재 포인트}/{필요 포인트}" 기록. stage 미준비 시 빈 문자열.</summary>
        private void SetWsPointsText(int currentPoints, WinningStreakStage stage)
        {
            if (_wsTxtPoints == null && _wsTxtPointsOutline == null) return;
            string pts = (stage != null && stage.requiredPoints > 0)
                ? $"{Mathf.Max(0, currentPoints)}/{stage.requiredPoints}"
                : "";
            if (_wsTxtPoints != null) _wsTxtPoints.text = pts;
            if (_wsTxtPointsOutline != null) _wsTxtPointsOutline.text = pts;
        }

        /// <summary>WS 미니 표시의 배수(TextGauge/Outline)·시간(TextTimer/Outline) 텍스트를 이름으로 1회 해석·캐시.
        /// root 미준비 시 resolved 를 유지하지 않아 다음 호출에서 재시도.</summary>
        private IEnumerator PlayPendingWinningStreakLobbyFxDeferred()
        {
            // try/finally 로 감싸 예외 / 조기 StopCoroutine / 씬 재진입 시에도 armed 비트가 늘 정리되도록 보장.
            try
            {
                yield return null;
                yield return null;

                // [2026-06-15] WS 자동 팝업(PopupWinningStreak 회차 진입 / PopupWinningStreakInfo 최초 해금 안내)이
                //   열려 있으면 닫힐 때까지 대기. 두 자동 팝업은 TryAutoOpenWinningStreakPopup() 내부에서 배타적이지만
                //   각각 Reward(딤+카운터) 연출과 동시 트리거되어 2겹 노출되던 문제 — 게이트로 양쪽 모두 차단.
                //   사용자가 PopupWinningStreak 내 BtnInfo로 Info를 수동 오픈한 동안에도 자연스럽게 Reward 연출이 대기.
                while (UIManager.HasInstance &&
                       (UIManager.Instance.IsOpenUI<PopupWinningStreak>() ||
                        UIManager.Instance.IsOpenUI<PopupWinningStreakInfo>()))
                    yield return null;

                if (!WinningStreakManager.HasInstance) yield break;

                var mgr = WinningStreakManager.Instance;
                while (mgr.TryDequeuePendingLobbyAnimation(out var anim))
                {
                    // [WS 0단계 2026-06-12] 로비 연출 앞에 Dim 보상 팝업(연승 수치 상승) 먼저 재생 — 승리 복귀에서만.
                    yield return PlayWinningStreakRewardPopup(anim);
                    yield return PlayWinningStreakLobbyFx(anim);
                    yield return PlayWinningStreakLevelClearGoldFx(anim);
                }

                if (CurrencyManager.HasInstance)
                    SetGoldText(CurrencyManager.Instance.Coins);

                // [WS quit-fail 2026-06-10] 실패(중도 이탈 포함)로 streak 이 리셋됐으면 배수 드롭 연출 1회 재생.
                if (mgr.TryConsumePendingFailFx(out int failFromMultiplier))
                    yield return PlayWsMultiplierFailFx(failFromMultiplier);
            }
            finally
            {
                // ROLLBACK_WS_REWARD_RELIABLE_GRANT_20260618: 보상 지급 안전망.
                //   정상 재생이면 PlayWinningStreakLobbyFx 의 per-stage ClaimStage(806)로 이미 지급됨 → 여기선 no-op.
                //   reward 팝업 hang(워치독 경유)·예외·이전 진입에서 애니메이션이 ClaimStage 까지 못 간 경우엔
                //   여기서 '달성했으나 미수령'인 stage 보상을 확실히 지급(멱등). 달성은 영구 State 라 큐가 비어도 복구됨.
                if (WinningStreakManager.HasInstance)
                    WinningStreakManager.Instance.ClaimAllAchievedStages();
                _wsLobbyFxArmed = false;
                _wsHoldMultiplierTextDuringAnim = false;
                _wsLobbyFxCoroutine = null;
            }
        }

        /// <summary>[WS 0단계 2026-06-12] PopupWinningStreakReward — Dim + 획득 포인트 수 연산 연출.
        /// 모델(confirmed): 1(기본, FXItem 비행) × 난이도배수(FXBadge — Hard/SuperHard 만) × 연승배수(FXMultiple — x1 초과 시).
        /// 종료까지 대기 후 기존 로비 연출(flame 비행→게이지) 진행. streak 미캡처(구버전 큐 잔존, endStreak==0)면 skip.</summary>
        private IEnumerator PlayWinningStreakRewardPopup(WinningStreakManager.PendingLobbyAnimation anim)
        {
            if (anim == null || anim.endStreak <= 0) yield break;
            bool showBadge = anim.clearedDifficulty == DifficultyPurpose.Hard
                          || anim.clearedDifficulty == DifficultyPurpose.SuperHard;
            int diffMult = WinningStreakConfigService.HasInstance
                ? WinningStreakConfigService.Instance.ResolveDifficultyMultiplier(anim.clearedDifficulty) : 1;
            int streakMult = anim.endMultiplier > 0 ? anim.endMultiplier : WinningStreakUI.ResolveCurrentMultiplier();

            // ROLLBACK_WS_SKIP_X1_COEFF_FX_20260615: 계수가 실제로 곱해지지 않는 경우(노말 레벨 + 0/1연승 → flame +1 만)는
            //   '계수 적용 연출'(PopupWinningStreakReward) 을 생략한다. 난이도배수 미적용(!showBadge) AND 연승배수 ≤1 이면
            //   곱셈 카운팅이 x1 무의미 연출이라 노출하지 않음 — 로비 flame 비행 FX 만으로 +1 이 게이지에 반영됨.
            //   롤백: 아래 if 블록 제거.
            if (!showBadge && streakMult <= 1)
                yield break;

            var popup = PopupWinningStreakReward.Play(diffMult, streakMult, anim.gainedPoints, showBadge, anim.clearedDifficulty);
            // ROLLBACK_WS_REWARD_POPUP_HANG_FIX_20260618: IsFinished 가 어떤 이유로든(코루틴 미시작/예외) 안 떨어져도
            //   최대 maxWait 초 후 강제 종료 — 이 while 이 영구 대기하면 상위 deferred 코루틴이 finally 에 못 가
            //   _wsLobbyFxCoroutine 가 안 풀려 PlayButton 이 영구 차단됨(Bug1/2). 정상은 ~3s 내 종료. 롤백: 타이머/Destroy 제거.
            const float maxWait = 6f;
            float waited = 0f;
            while (popup != null && !popup.IsFinished && waited < maxWait)
            {
                waited += Time.unscaledDeltaTime;
                yield return null;
            }
            if (popup != null && !popup.IsFinished && popup.gameObject != null)
            {
                Debug.LogWarning("[UILobby] PopupWinningStreakReward 가 시간 내 종료 안 됨 — 강제 정리(버튼 차단 방지).");
                Destroy(popup.gameObject);
            }
        }

        /// <summary>[WS quit-fail 2026-06-10] 배수 드롭 실패 연출 — 이전 배수로 슬라이드 인 → 잠시 후 1 로 드롭(펀치) → 슬라이드 아웃.
        /// 실제 streak 은 이미 리셋된 상태라 연출 종료 후 표시 갱신만 수행. 에디터 실패 프리뷰(x 키)와 동일 시퀀스.</summary>
        private IEnumerator PlayWsMultiplierFailFx(int fromMultiplier)
        {
            ResolveWsFxRefs();
            // [WS 배수 하락 시퀀스 통일 2026-06-22] 사용자 추가 피드백(task 6a34e951): 상승/하락 무관 동일 시퀀스 —
            // 기존 숫자 표시 → Multiplier 등장 → SelectFrame 이동 → 숫자 갱신+FXFire+Scale punch → 0.5초 유지 → slide-out.
            // SUPERSEDES 직전 결정(slide-out 이후 텍스트 갱신, 0.6f/0.7f 임의 대기) — owner 출처: 본 ProjectHub 태스크
            // [사용자 추가 지시] 블록 (2026-06-22). 상승측 PlayWinningStreakLobbyFx L988-1001 과 1:1 대응.
            string preFromText = $"x{Mathf.Max(1, fromMultiplier)}";
            ResolveWsTexts();
            if (_wsTxtGauge != null) _wsTxtGauge.text = preFromText;
            if (_wsTxtGaugeOutline != null) _wsTxtGaugeOutline.text = preFromText;
            _wsLastMultiplierText = preFromText;
            _wsHoldMultiplierTextDuringAnim = true;
            if (_wsMultiplier == null) yield break;
            if (!_wsMultiplier.gameObject.activeSelf)
                _wsMultiplier.gameObject.SetActive(true);

            SetWsMultiplierX(WS_MULTIPLIER_HIDDEN_X);
            WinningStreakUI.PlayLobbyMultiplierSelect(_wsMultiplier, fromMultiplier, fromMultiplier);
            // [Multiplier 등장 띠용 완화 2026-06-22] OutBack overshoot=0.4 (기본 1.7) — 시작/최종 X 좌표 불변, 튀어나옴 거리만 축소.
            yield return PlayWsMultiplierSlide(WS_MULTIPLIER_SHOWN_X, WS_MULTIPLIER_SLIDE_IN_DURATION, Ease.OutBack, WS_MULTIPLIER_SLIDE_IN_OVERSHOOT);

            WinningStreakUI.PlayLobbyMultiplierSelect(_wsMultiplier, 1, 1);
            // [2026-06-11] '떨어지는' 펀치(덜컹거림) 비활성 — 사용자 요청. 롤백: 아래 2줄 주석 해제.
            // _wsMultiplier.DOPunchAnchorPos(new Vector2(0f, -36f), 0.4f, 8, 0.7f).SetUpdate(true);
            // _wsMultiplier.DOPunchScale(new Vector3(-0.18f, -0.18f, 0f), 0.4f, 8, 0.7f).SetUpdate(true);

            yield return new WaitForSecondsRealtime(WinningStreakUI.MULTIPLIER_SELECT_MOVE_DURATION);
            _wsHoldMultiplierTextDuringAnim = false;
            RefreshWinningStreakDisplay();
            yield return new WaitForSecondsRealtime(WS_HOLD_AFTER_TEXT_FIRE_DURATION);
            yield return PlayWsMultiplierSlide(WS_MULTIPLIER_HIDDEN_X, WS_MULTIPLIER_SLIDE_OUT_DURATION, Ease.InCubic);
        }

        private IEnumerator PlayWinningStreakLobbyFx(WinningStreakManager.PendingLobbyAnimation anim, bool grantRewards = true, bool forceVisible = false)
        {
            if (anim == null) yield break;

            // [WS 텍스트 보류 2026-06-22] SelectFrame 이동 완료 시점까지 TextGauge/Outline 갱신과 FXFire 발화를 보류
            // — fromMult 텍스트를 1회 직접 시드(가드 우회)해 기존 게이지 숫자 표시 유지.
            // SUPERSEDES 직전 2026-06-22 결정(슬라이드-아웃 후 FX) — 본 태스크 사용자 추가 지시로 변경 (hermes task 6a34e951, 2026-06-22).
            int preFromMult = anim.startMultiplier > 0 ? anim.startMultiplier : WinningStreakUI.ResolveCurrentMultiplier();
            string preFromText = $"x{preFromMult}";
            ResolveWsTexts();
            if (_wsTxtGauge != null) _wsTxtGauge.text = preFromText;
            if (_wsTxtGaugeOutline != null) _wsTxtGaugeOutline.text = preFromText;
            _wsLastMultiplierText = preFromText;
            _wsHoldMultiplierTextDuringAnim = true;

            ResolveWsFxRefs();

            // forceVisible(에디터 프리뷰): unlock 게이트 무시하고 WS UI 무조건 활성화.
            if (forceVisible) ForceShowWinningStreakUI();
            else RefreshWinningStreakVisibility();

            // 연출 시작 전 Multiplier 를 숨김 위치(-725)로 즉시 리셋.
            SetWsMultiplierX(WS_MULTIPLIER_HIDDEN_X);

            // [WS 배수 증가 연출 2026-06-11] 클리어 전 배수로 SelectFrame 을 세팅해 두고(숨김 상태),
            // 슬라이드 인 후 클리어 후 배수로 이동 → '증가' 가 보이게. 미캡처(0) 시 현재값 유지.
            int fromMult = anim.startMultiplier > 0 ? anim.startMultiplier : WinningStreakUI.ResolveCurrentMultiplier();
            int toMult   = anim.endMultiplier   > 0 ? anim.endMultiplier   : WinningStreakUI.ResolveCurrentMultiplier();
            if (_wsMultiplier != null)
                WinningStreakUI.PlayLobbyMultiplierSelect(_wsMultiplier, fromMult, fromMult);

            int stageForFill = Mathf.Max(1, anim.startStage);
            float startRatio = ResolveWsStageRatio(stageForFill, anim.startPoints);
            bool stageCompleted = anim.achievedStages != null && anim.achievedStages.Count > 0;
            float endRatio = stageCompleted
                ? 1f
                : ResolveWsStageRatio(Mathf.Max(1, anim.endStage), anim.endPoints);

            if (_wsProgressSlider != null)
                _wsProgressSlider.value = startRatio;

            // 게이지 하단 포인트 텍스트도 연출 시작 값으로 세팅 (채움 완료 후 아래에서 종료 값 갱신).
            WinningStreakStage fillStageDoc = WinningStreakConfigService.HasInstance
                ? WinningStreakConfigService.Instance.GetStage(stageForFill) : null;
            SetWsPointsText(anim.startPoints, fillStageDoc);

            // [사용자 추가 지시 2026-06-22] supersedes ROLLBACK_WS_LOBBY_GAIN_LABEL_20260621 — TxtGain 라벨 자체를 폐기. 획득 포인트는 PopupWinningStreakReward 카운터가 표시.
            yield return PlayWsFireFlyAndPulse();

            if (_wsProgressSlider != null)
            {
                _wsLobbyFxSequence?.Kill();
                _wsLobbyFxSequence = DOTween.Sequence().SetUpdate(true);
                _wsLobbyFxSequence.Append(_wsProgressSlider.DOValue(endRatio, WS_SLIDER_FILL_DURATION)
                    .SetEase(Ease.OutCubic));
                yield return _wsLobbyFxSequence.WaitForCompletion();
            }

            // 채움 종료 값으로 포인트 텍스트 갱신 — 스테이지 완주면 만땅 표기, 아니면 endPoints.
            if (stageCompleted)
                SetWsPointsText(fillStageDoc != null ? fillStageDoc.requiredPoints : anim.endPoints, fillStageDoc);
            else
                SetWsPointsText(anim.endPoints, WinningStreakConfigService.HasInstance
                    ? WinningStreakConfigService.Instance.GetStage(Mathf.Max(1, anim.endStage)) : null);

            if (stageCompleted)
            {
                for (int i = 0; i < anim.achievedStages.Count; i++)
                {
                    int achievedStage = anim.achievedStages[i];
                    var achievedDoc = WinningStreakConfigService.HasInstance
                        ? WinningStreakConfigService.Instance.GetStage(achievedStage)
                        : null;
                    BindWsRewardItem(achievedDoc);
                    // 빛(FXReward) 대신 RewardItem 위치에서 WinningStreakGetReward 스폰 → 위로 상승(프리팹 Animator).
                    yield return PlayWsGetRewardSpawn(achievedDoc);
                    if (grantRewards && WinningStreakManager.HasInstance && !WinningStreakManager.Instance.IsStageClaimed(achievedStage))
                        WinningStreakManager.Instance.ClaimStage(achievedStage);

                    // [2026-06-12 초과달성 연출] 보상 수령 후 초과분을 다음 스테이지 게이지로 캐리 —
                    // 중간 달성 스테이지는 0→가득, 마지막 캐리는 0→endPoints 비율까지 채움.
                    // (기존: 80/100 에서 +100 획득 시 다음 단계 300 기준 80 까지 차오르는 연출 부재)
                    bool isLastAchieved = i == anim.achievedStages.Count - 1;
                    int carryStage = isLastAchieved
                        ? Mathf.Max(achievedStage + 1, Mathf.Max(1, anim.endStage))
                        : anim.achievedStages[i + 1];
                    var carryDoc = WinningStreakConfigService.HasInstance
                        ? WinningStreakConfigService.Instance.GetStage(carryStage)
                        : null;
                    float carryTarget = isLastAchieved ? ResolveWsStageRatio(carryStage, anim.endPoints) : 1f;
                    if (_wsProgressSlider != null && carryDoc != null && carryTarget > 0f)
                    {
                        BindWsRewardItem(carryDoc); // 다음 스테이지 보상으로 전환 후 채움
                        _wsProgressSlider.value = 0f;
                        SetWsPointsText(0, carryDoc);
                        _wsLobbyFxSequence?.Kill();
                        _wsLobbyFxSequence = DOTween.Sequence().SetUpdate(true);
                        _wsLobbyFxSequence.Append(_wsProgressSlider.DOValue(carryTarget, WS_SLIDER_FILL_DURATION)
                            .SetEase(Ease.OutCubic));
                        yield return _wsLobbyFxSequence.WaitForCompletion();
                        SetWsPointsText(isLastAchieved ? anim.endPoints : carryDoc.requiredPoints, carryDoc);
                    }
                }
            }

            // [WS 2026-06-22] x100 연속 유지(fromMult==toMult==100) 시 Multiplier 슬라이드/SelectFrame 이동 연출만 스킵. 게이지·보상·텍스트 갱신은 모두 정상 진행. 최초 x25→x100 진입은 fromMult=25/toMult=100 이므로 가드 통과해 정상 재생됨.
            if (!(fromMult == 100 && toMult == 100))
            {
                // ROLLBACK_WS_MULTIPLIER_AFTER_GAUGE_AND_REWARD_20260619: 되돌리려면 아래 PlayWsMultiplierSlide(SHOWN_X)+PlayLobbyMultiplierSelect(toMult) 블록을 PlayWsFireFlyAndPulse 직후로 다시 이동.
                // 게이지 채움/보상 등장 종료 후 → Multiplier 가 X=10 으로 튕기듯 슬라이드 인.
                // [Multiplier 등장 띠용 완화 2026-06-22] OutBack overshoot=0.4 (기본 1.7) — 시작/최종 X 좌표 불변, 튀어나옴 거리만 축소.
                yield return PlayWsMultiplierSlide(WS_MULTIPLIER_SHOWN_X, WS_MULTIPLIER_SLIDE_IN_DURATION, Ease.OutBack, WS_MULTIPLIER_SLIDE_IN_OVERSHOOT);
                // ROLLBACK_WINNING_STREAK_MULTIPLIER_AFTER_ENTER_20260605:
                // Designer spec: after the Multiplier entrance motion, move SelectFrame/TextYellow to the current value.
                // [2026-06-10] Animator(PlayMultiplierState) → 코드 트윈(PlayMultiplierSelect) — Animator 가 위치를 덮어쓰던 문제 해소.
                // [2026-06-11] 이전 배수(fromMult)로 등장한 뒤 새 배수(toMult)로 이동 — 배수 '증가' 연출.
                if (_wsMultiplier != null)
                {
                    WinningStreakUI.PlayLobbyMultiplierSelect(_wsMultiplier, fromMult, toMult);
                    // [2026-06-11] '오르는' 펀치(덜컹거림) 비활성 — 사용자 요청. SelectFrame 이동만 유지.
                    // 롤백: 아래 if 블록 주석 해제.
                    // if (toMult > fromMult)
                    // {
                    //     _wsMultiplier.DOPunchAnchorPos(new Vector2(0f, 24f), 0.35f, 6, 0.6f).SetUpdate(true);
                    //     _wsMultiplier.DOPunchScale(new Vector3(0.12f, 0.12f, 0f), 0.35f, 6, 0.6f).SetUpdate(true);
                    // }
                }

                // SelectFrame fromMult→toMult 이동 완료 대기 — 감소 시 거리 비례 duration 사용(WinningStreakUI.ResolveLobbyMultiplierMoveDuration). SlideOut 이 이동을 자르지 않게 동기화. ROLLBACK_WS_MULTIPLIER_SELECT_WAIT_20260619
                // 2026-06-22 2nd-pass(v3): ResolveLobbyMultiplierMoveDuration 에 최소 0.35s floor 추가, 거리 비례 1000px/s 유지 — yield 라인은 그대로(헬퍼만 호출하므로 자동 반영).
                yield return new WaitForSecondsRealtime(WinningStreakUI.ResolveLobbyMultiplierMoveDuration(fromMult, toMult));

                // WHY: 기획 시퀀스(2026-06-22) — SelectFrame move(상승 0.18s / 감소 거리비례) → hold release → TextGauge/FXFire/Scale punch(0.20s) → 0.5s 노출 유지(WS_HOLD_AFTER_TEXT_FIRE_DURATION) → multiplier slide-out.
                // SUPERSEDES 동일 일자(2026-06-22) 직전 결정 — 그 결정은 "PlayWsMultiplierSlide(HIDDEN_X) 완료 후 hold 해제 + FXFire" 였음. 본 변경의 owner 확인 출처: 본 ProjectHub 태스크의 [사용자 추가 지시] 블록 (2026-06-22) via Hermes task id 6a34e951. 핵심 조건(기획 명시): Multiplier가 먼저 사라지면 안 됨, TextGauge 숫자 갱신 후 Multiplier 슬라이드 아웃, 숫자 갱신과 동시에 FXFire 노출 — 상승/하락 모두 동일.
                // SUPERSEDES [2026-06-22 직전 결정: 텍스트 갱신 직후 즉시 slide-out] — owner 추가 피드백: 플레이어가 새 배수 인지 못함, 0.5초 hold 추가.
                _wsHoldMultiplierTextDuringAnim = false;
                RefreshWinningStreakDisplay();

                // 텍스트 갱신+FXFire+Scale punch 끝난 뒤 0.5초 노출 유지 → slide-out (인지 시간 확보, owner 2026-06-22 추가 피드백).
                yield return new WaitForSecondsRealtime(WS_HOLD_AFTER_TEXT_FIRE_DURATION);

                // 나머지 연출이 끝나면 Multiplier 를 X=-725 로 슬라이드 아웃.
                yield return PlayWsMultiplierSlide(WS_MULTIPLIER_HIDDEN_X, WS_MULTIPLIER_SLIDE_OUT_DURATION, Ease.InCubic);
            }
            else
            {
                // Skip case (fromMult==toMult==100): Multiplier 슬라이드 연출이 스킵돼도 hold 해제와 표시 갱신은 1회 필요.
                _wsHoldMultiplierTextDuringAnim = false;
                RefreshWinningStreakDisplay();
            }
        }

        /// <summary>Winning Streak lobby FX 이후, 이미 지급된 클리어 골드를 로비 GoldPanel로 날려 보이는 전용 연출.</summary>
        private IEnumerator PlayWinningStreakLevelClearGoldFx(WinningStreakManager.PendingLobbyAnimation anim)
        {
            // ROLLBACK_WS_LOBBY_LEVEL_CLEAR_GOLD_FX_20260621:
            // CurrencyManager already grants level-clear coins in-game. Winning Streak clears
            // replay only the visual GoldPanel fly here after the WS lobby sequence.
            int coinsAdded = anim != null ? Mathf.Max(0, anim.levelClearCoins) : 0;
            if (coinsAdded <= 0 || !CurrencyManager.HasInstance) yield break;

            int finalCoins = CurrencyManager.Instance.Coins;
            int startCoins = Mathf.Min(_displayedCoins, Mathf.Max(0, finalCoins - coinsAdded));
            int targetCoins = Mathf.Min(finalCoins, startCoins + coinsAdded);
            SetGoldText(startCoins);

            Vector2 from = new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);
            Vector2 to = GetGoldPanelScreenPos();
            int count = Mathf.Max(1, WS_LEVEL_CLEAR_GOLD_FLY_COUNT);
            int perCoinDelta = Mathf.Max(1, coinsAdded / count);
            int remainder = coinsAdded - perCoinDelta * count;
            int landed = 0;
            bool complete = false;

            CoinFlyEffect.Play(from, to, count,
                onEachLand: () =>
                {
                    int delta = perCoinDelta + (landed == count - 1 ? remainder : 0);
                    landed++;
                    SetGoldText(Mathf.Min(targetCoins, _displayedCoins + delta));
                    PulseGoldPanel();
                    EventBus.Publish(new OnCoinFlyLanded());
                },
                onAllComplete: () =>
                {
                    SetGoldText(targetCoins);
                    complete = true;
                });

            while (!complete)
                yield return null;
        }

        /// <summary>[배치7-2] 클리어 후 획득 Flame 을 "+{n}" 토스트로 표시.
        /// ROLLBACK_WINNING_STREAK_FLAME_GAIN_TOAST_20260607: 되돌리려면 호출부(PlayWinningStreakLobbyFx)의
        /// ShowWsFlameGainToast(...) 한 줄과 이 메서드를 삭제. (토스트 위치=화면 중앙, 디자인 확정 시 조정)</summary>
        private void ShowWsFlameGainToast(int gained)
        {
            if (gained <= 0 || !UIManager.HasInstance) return;
            Transform parent = UIManager.Instance.PopupTr ?? UIManager.Instance.UiTr;
            if (parent == null) return;
            TxtToast.Spawn(parent, $"+{gained}", Vector2.zero);
        }

        /// <summary>MultiplierMaskArea/Multiplier 를 지정 X(anchoredPosition.x)로 즉시 세팅.</summary>
        private void SetWsMultiplierX(float x)
        {
            if (_wsMultiplier == null) return;
            var p = _wsMultiplier.anchoredPosition;
            p.x = x;
            _wsMultiplier.anchoredPosition = p;
        }

        /// <summary>Multiplier 를 지정 X 로 슬라이드(anchoredPosition.x 트윈). 미할당 시 즉시 종료.
        /// [2026-06-22 추가] overshootOrAmplitude 기본 1f — OutBack/Elastic 한정으로 의미. 호출측이 명시할 때만 약화.</summary>
        private IEnumerator PlayWsMultiplierSlide(float targetX, float duration, Ease ease, float overshootOrAmplitude = 1f)
        {
            if (_wsMultiplier == null) yield break;
            _wsLobbyFxSequence?.Kill();
            _wsLobbyFxSequence = DOTween.Sequence().SetUpdate(true);
            _wsLobbyFxSequence.Append(_wsMultiplier.DOAnchorPosX(targetX, duration).SetEase(ease, overshootOrAmplitude));
            yield return _wsLobbyFxSequence.WaitForCompletion();
        }

        /// <summary>FxItem(iconWinningStreak) 을 화면 중앙→target(ImageIcon) 으로 날린 뒤, target 이 커졌다 복귀하고,
        /// 마지막에 FxFire(반짝이 효과)를 활성화한다. (날아가는 것=FxItem, 도착지=ImageIcon, FxFire=반짝이 only)
        /// [사용자 추가 지시 2026-06-22] supersedes 이전 'gainedPoints 롤백용 잔존' 결정 — FXItem_WinningStreak_Fly의 TxtGain 자체를 제거하므로 gainedPoints 인자도 폐기.</summary>
        private IEnumerator PlayWsFireFlyAndPulse()
        {
            RectTransform target = ResolveWsFireTarget();   // ImageIcon
            Transform parent = ResolveWsFxParent();
            GameObject flyPrefab = Resources.Load<GameObject>(Const.PREFAB_FXITEM);

            if (flyPrefab != null && parent != null && target != null)
            {
                GameObject fly = Instantiate(flyPrefab, parent);
                fly.name = "FXItem_WinningStreak_Fly";

                // 날아가는 아이콘을 iconWinningStreak 으로 교체.
                var flyImg = fly.GetComponentInChildren<Image>(true);
                if (flyImg != null)
                {
                    if (ResourceManager.HasInstance)
                    {
                        var spr = ResourceManager.Instance.GetUISprite(Const.SPR_ICONWINNINGSTREAK);
                        if (spr != null) { flyImg.sprite = spr; flyImg.preserveAspect = true; }
                    }
                    flyImg.color = Color.white;
                    flyImg.raycastTarget = false;
                }

                RectTransform flyRt = fly.GetComponent<RectTransform>();
                RectTransform parentRt = parent as RectTransform;

                Vector3 startLocal;
                Vector3 targetLocal;
                ResolveWsFxLocalPoints(parentRt, target, out startLocal, out targetLocal);

                _wsLobbyFxSequence?.Kill();
                _wsLobbyFxSequence = DOTween.Sequence().SetUpdate(true);
                if (flyRt != null)
                {
                    flyRt.anchorMin = flyRt.anchorMax = new Vector2(0.5f, 0.5f);
                    flyRt.pivot = new Vector2(0.5f, 0.5f);
                    flyRt.anchoredPosition = startLocal;
                    flyRt.localScale = Vector3.one * WS_FIRE_FLY_SCALE_START;
                    _wsLobbyFxSequence.Append(flyRt.DOAnchorPos(targetLocal, WS_FIRE_FLY_DURATION)
                        .SetEase(Ease.InOutCubic));
                    _wsLobbyFxSequence.Join(flyRt.DOScale(WS_FIRE_FLY_SCALE_END, WS_FIRE_FLY_DURATION)
                        .SetEase(Ease.OutSine));
                }
                else
                {
                    fly.transform.localPosition = startLocal;
                    fly.transform.localScale = Vector3.one * WS_FIRE_FLY_SCALE_START;
                    _wsLobbyFxSequence.Append(fly.transform.DOLocalMove(targetLocal, WS_FIRE_FLY_DURATION)
                        .SetEase(Ease.InOutCubic));
                    _wsLobbyFxSequence.Join(fly.transform.DOScale(WS_FIRE_FLY_SCALE_END, WS_FIRE_FLY_DURATION)
                        .SetEase(Ease.OutSine));
                }
                yield return _wsLobbyFxSequence.WaitForCompletion();
                Destroy(fly);
            }

            // ROLLBACK_WS_FXFIRE_ON_MERGE_PULSE_20260618:
            // Show/replay FXFire at the merge moment, while WinningIcon scales up.
            SetWinningStreakFxActive(true);

            // 도착 후 target(ImageIcon) 펄스 — 커졌다 원래대로.
            if (target != null)
            {
                Vector3 baseScale = target.localScale;
                _wsLobbyFxSequence?.Kill();
                _wsLobbyFxSequence = DOTween.Sequence().SetUpdate(true);
                _wsLobbyFxSequence.Append(target.DOScale(baseScale * 1.25f, WS_FIRE_PULSE_DURATION).SetEase(Ease.OutBack));
                _wsLobbyFxSequence.Append(target.DOScale(baseScale, WS_FIRE_PULSE_DURATION).SetEase(Ease.OutCubic));
                yield return _wsLobbyFxSequence.WaitForCompletion();
            }

        }

        /// <summary>보상 수령 연출 — RewardItem 위치에서 WinningStreakGetReward 프리팹을 스폰해 위로 상승시킨다.
        /// (이전: FXReward 빛 상승. 변경: 빛 대신 WinningStreakGetReward 가 RewardItem 에서 나와 올라감.)
        /// 상승+페이드는 프리팹 자체 Animator 가 처리하므로 여기선 스폰 후 재생 시간만 대기.</summary>
        private IEnumerator PlayWsGetRewardSpawn(WinningStreakStage stage)
        {
            ResolveWsFxRefs();
            if (stage == null || stage.rewards == null) yield break;

            Vector2 anchor = ResolveWsRewardItemAnchor();
            WinningStreakGetRewardSpawner.Play(stage.rewards, anchor);

            // 프리팹 내부 상승/페이드 Animator 재생 대기.
            yield return new WaitForSecondsRealtime(WS_REWARD_RISE_DURATION + 0.25f);
        }

        /// <summary>RewardItem 의 화면 위치를 WinningStreakGetReward 스폰 부모(EffectTr) 로컬 좌표로 변환.</summary>
        private Vector2 ResolveWsRewardItemAnchor()
        {
            RectTransform rewardRt = _wsRewardItem != null ? _wsRewardItem.transform as RectTransform : null;
            RectTransform parentRt = ResolveWsFxParent() as RectTransform;
            if (rewardRt == null || parentRt == null) return Vector2.zero;

            Canvas canvas = parentRt.GetComponentInParent<Canvas>();
            Camera cam = (canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay) ? canvas.worldCamera : null;
            Vector2 screen = RectTransformUtility.WorldToScreenPoint(cam, rewardRt.position);
            if (RectTransformUtility.ScreenPointToLocalPointInRectangle(parentRt, screen, cam, out Vector2 local))
                return local;
            return Vector2.zero;
        }

        private void ResolveGoldPanelFxFire()
        {
            if (_goldPanelFxFire != null && !_goldPanelFxFire.scene.IsValid()) _goldPanelFxFire = null;
            if (_goldPanelFxFire != null) return;
            Transform root = (_txtGold != null && _txtGold.transform.parent != null) ? _txtGold.transform.parent : null;
            if (root == null) return;
            _goldPanelFxFire = FindChildComponentByName<Transform>(root, "FXFire")?.gameObject
                            ?? FindChildComponentByName<Transform>(root, "FxFire")?.gameObject;
        }

        private void DisableGoldPanelFxFireOnEnter()
        {
            // 템플릿은 항상 비활성/정지 상태로 보존 — 로비 진입 시 자동 재생 차단.
            // 실제 재생은 PlayGoldPanelFxFire() 가 Instantiate 한 인스턴스에서만 발생.
            if (_goldPanelFxFire == null) return;
            GoldPanelFxFireUtil.DisableUnderGoldPanel(_goldPanelFxFire.transform.parent);
        }

        public void PlayGoldPanelFxFire()
        {
            ResolveGoldPanelFxFire();
            if (_goldPanelFxFire == null) return;

            Transform parent = _goldPanelFxFire.transform.parent;
            if (parent == null) return;

            // 직전 인스턴스가 아직 살아있으면 신규 발화 금지 — '코인 N개 도착해도 FXFire 는 최초 1회만'.
            // 자연 종료(stopAction=Destroy + Destroy(life+1)) 후 Unity-null 이 되면 다음 호출에서 정상 생성.
            if (_activeGoldPanelFxFireInstance != null) return;

            // 최초 1회만 Instantiate — 인스턴스가 살아있는 동안 후속 호출(코인 2~N번째 도착)은 무시.
            _activeGoldPanelFxFireInstance = Instantiate(_goldPanelFxFire, parent, false);
            var srcTr = _goldPanelFxFire.transform;
            var dstTr = _activeGoldPanelFxFireInstance.transform;
            dstTr.localPosition = srcTr.localPosition;
            dstTr.localRotation = srcTr.localRotation;
            dstTr.localScale    = srcTr.localScale;
            _activeGoldPanelFxFireInstance.SetActive(true);

            var systems = _activeGoldPanelFxFireInstance.GetComponentsInChildren<ParticleSystem>(true);
            float maxLifetime = 0f;
            for (int i = 0; i < systems.Length; i++)
            {
                var ps = systems[i];
                var main = ps.main;
                // 루트 PS 가 자연 종료되면 Unity 가 GameObject 까지 자동 정리 — 누수 방지.
                if (i == 0) main.stopAction = ParticleSystemStopAction.Destroy;

                float dur  = main.duration;
                float life = main.startLifetime.constantMax > 0f ? main.startLifetime.constantMax : main.startLifetime.constant;
                float total = dur + life;
                if (total > maxLifetime) maxLifetime = total;

                ps.Clear(true);
                ps.Play(true);
            }

            // 안전망 — stopAction 이 looping 등의 이유로 발동되지 않는 케이스 대비 강제 정리.
            if (maxLifetime > 0f) Destroy(_activeGoldPanelFxFireInstance, maxLifetime + 1f);
        }

        private void ResolveLifePanelFxFire()
        {
            if (_lifePanelFxFire != null && !_lifePanelFxFire.scene.IsValid()) _lifePanelFxFire = null;
            if (_lifePanelFxFire != null) return;
            Transform root = (_txtLife != null && _txtLife.transform.parent != null) ? _txtLife.transform.parent : null;
            if (root == null) return;
            _lifePanelFxFire = FindChildComponentByName<Transform>(root, "FXFire")?.gameObject
                            ?? FindChildComponentByName<Transform>(root, "FxFire")?.gameObject;
        }

        private void DisableLifePanelFxFireOnEnter()
        {
            // 템플릿은 항상 비활성/정지 상태로 보존 — 로비 진입 시 자동 재생 차단.
            // 실제 재생은 PlayLifePanelFxFire() 가 Instantiate 한 인스턴스에서만 발생.
            if (_lifePanelFxFire == null) return;
            GoldPanelFxFireUtil.DisableUnderLifePanel(_lifePanelFxFire.transform.parent);
        }

        private void PlayLifePanelFxFire()
        {
            ResolveLifePanelFxFire();
            if (_lifePanelFxFire == null) return;

            Transform parent = _lifePanelFxFire.transform.parent;
            if (parent == null) return;

            // 직전 인스턴스가 아직 살아있으면 신규 발화 금지 — '하트 N개 도착해도 FXFire 는 최초 1회만'.
            // 자연 종료(stopAction=Destroy + Destroy(life+1)) 후 Unity-null 이 되면 다음 호출에서 정상 생성.
            if (_activeLifePanelFxFireInstance != null) return;

            // 최초 1회만 Instantiate — 인스턴스가 살아있는 동안 후속 호출(하트 2~N번째 도착)은 무시.
            _activeLifePanelFxFireInstance = Instantiate(_lifePanelFxFire, parent, false);
            var srcTr = _lifePanelFxFire.transform;
            var dstTr = _activeLifePanelFxFireInstance.transform;
            dstTr.localPosition = srcTr.localPosition;
            dstTr.localRotation = srcTr.localRotation;
            dstTr.localScale    = srcTr.localScale;
            _activeLifePanelFxFireInstance.SetActive(true);

            var systems = _activeLifePanelFxFireInstance.GetComponentsInChildren<ParticleSystem>(true);
            float maxLifetime = 0f;
            for (int i = 0; i < systems.Length; i++)
            {
                var ps = systems[i];
                var main = ps.main;
                // 루트 PS 가 자연 종료되면 Unity 가 GameObject 까지 자동 정리 — 누수 방지.
                if (i == 0) main.stopAction = ParticleSystemStopAction.Destroy;

                float dur  = main.duration;
                float life = main.startLifetime.constantMax > 0f ? main.startLifetime.constantMax : main.startLifetime.constant;
                float total = dur + life;
                if (total > maxLifetime) maxLifetime = total;

                ps.Clear(true);
                ps.Play(true);
            }

            // 안전망 — stopAction 이 looping 등의 이유로 발동되지 않는 케이스 대비 강제 정리.
            if (maxLifetime > 0f) Destroy(_activeLifePanelFxFireInstance, maxLifetime + 1f);
        }

        private void ResolvePlayButtonSparks()
        {
            if (_playButtonSparks != null && !_playButtonSparks.scene.IsValid()) _playButtonSparks = null;
            if (_playButtonSparks != null) return;
            Transform root = _btnPlay != null ? _btnPlay.transform : null;
            if (root == null) return;
            _playButtonSparks = FindChildComponentByName<Transform>(root, "Sparks")?.gameObject
                             ?? FindChildComponentByName<Transform>(root, "FXSparks")?.gameObject
                             ?? FindChildComponentByName<Transform>(root, "FxSparks")?.gameObject;
        }

        private void DisablePlayButtonSparksOnEnter()
        {
            if (_playButtonSparks == null) return;
            var systems = _playButtonSparks.GetComponentsInChildren<ParticleSystem>(true);
            foreach (var ps in systems)
            {
                if (ps == null) continue;
                ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            }
        }

        private void PlayPlayButtonSparks()
        {
            ResolvePlayButtonSparks();
            if (_playButtonSparks == null) return;

            Transform parent = _playButtonSparks.transform.parent;
            if (parent == null) return;

            if (_activePlayButtonSparksInstance != null) return;

            _activePlayButtonSparksInstance = Instantiate(_playButtonSparks, parent, false);
            var srcTr = _playButtonSparks.transform;
            var dstTr = _activePlayButtonSparksInstance.transform;
            dstTr.localPosition = srcTr.localPosition;
            dstTr.localRotation = srcTr.localRotation;
            dstTr.localScale    = srcTr.localScale;
            _activePlayButtonSparksInstance.SetActive(true);

            var systems = _activePlayButtonSparksInstance.GetComponentsInChildren<ParticleSystem>(true);
            float maxLifetime = 0f;
            for (int i = 0; i < systems.Length; i++)
            {
                var ps = systems[i];
                var main = ps.main;
                if (i == 0) main.stopAction = ParticleSystemStopAction.Destroy;

                float dur  = main.duration;
                float life = main.startLifetime.constantMax > 0f ? main.startLifetime.constantMax : main.startLifetime.constant;
                float total = dur + life;
                if (total > maxLifetime) maxLifetime = total;

                ps.Clear(true);
                ps.Play(true);
            }

            if (maxLifetime > 0f) Destroy(_activePlayButtonSparksInstance, maxLifetime + 1f);
        }

        private void DisableWinningStreakFxOnEnter()
        {
            ResolveWsFxRefs();
            SetWinningStreakFxActive(false);
            if (_wsMultipleFxFire != null) _wsMultipleFxFire.SetActive(false);
        }

        private void SetWinningStreakFxActive(bool active)
        {
            SetParticleObjectActive(_wsFxLight, active);
            SetParticleObjectActive(_wsFxFire, active);
        }

        private static void SetParticleObjectActive(GameObject go, bool active)
        {
            if (go == null) return;
            if (active && !go.activeSelf)
                go.SetActive(true);

            var systems = go.GetComponentsInChildren<ParticleSystem>(true);
            for (int i = 0; i < systems.Length; i++)
            {
                if (systems[i] == null) continue;
                if (active)
                {
                    systems[i].Clear(true);
                    systems[i].Play(true);
                }
                else
                {
                    systems[i].Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                }
            }
            if (!active && go.activeSelf)
                go.SetActive(active);
        }

        private void ResolveWsFxRefs()
        {
            Transform root = _wsDisplayRoot != null ? _wsDisplayRoot.transform
                           : (_btnWinningStreak != null ? _btnWinningStreak.transform : transform);

            // [방어] _wsFxFire/_wsFxReward/_wsFxFireTarget 는 '하이어라키(씬) 인스턴스'여야 함.
            // 실수로 Project 의 FXFire.prefab(에셋)을 넣으면 위치가 씬 좌표가 아니라 에셋 좌표(0 등)라
            // 날아오는 불꽃이 엉뚱한 곳으로 감 → 에셋 참조는 버리고 아래에서 이름으로 재탐색.
            if (_wsFxFire != null && !_wsFxFire.scene.IsValid()) _wsFxFire = null;
            if (_wsFxLight != null && !_wsFxLight.scene.IsValid()) _wsFxLight = null;
            if (_wsFxReward != null && !_wsFxReward.scene.IsValid()) _wsFxReward = null;
            if (_wsFxFireTarget != null && !_wsFxFireTarget.gameObject.scene.IsValid()) _wsFxFireTarget = null;
            if (_wsMultipleFxFire != null && !_wsMultipleFxFire.scene.IsValid()) _wsMultipleFxFire = null;

            if (_wsFxFire == null)
                _wsFxFire = FindChildComponentByName<Transform>(root, "FxFire")?.gameObject
                         ?? FindChildComponentByName<Transform>(root, "FXFire")?.gameObject;
            if (_wsFxLight == null)
                _wsFxLight = FindChildComponentByName<Transform>(root, "FxLight")?.gameObject
                          ?? FindChildComponentByName<Transform>(root, "FXLight")?.gameObject;
            if (_wsFxReward == null)
                _wsFxReward = FindChildComponentByName<Transform>(root, "FxReward")?.gameObject
                           ?? FindChildComponentByName<Transform>(root, "FXReward")?.gameObject;
            // 날아가는 FxItem 의 도착지 = ImageIcon. 미할당 시 이름으로 탐색, 최후에만 FxFire 로 폴백.
            if (_wsFxFireTarget == null)
                _wsFxFireTarget = FindChildComponentByName<RectTransform>(root, "ImageIcon")
                               ?? (_wsFxFire != null ? _wsFxFire.transform as RectTransform : null);
            if (_wsMultiplier == null)
            {
                var maskArea = FindChildComponentByName<Transform>(root, "MultiplierMaskArea");
                _wsMultiplier = FindChildComponentByName<RectTransform>(maskArea != null ? maskArea : root, "Multiplier");
            }
            if (_wsMultipleFxFire == null)
            {
                var winningIcon = FindChildComponentByName<Transform>(root, "WinningIcon");
                var multiple = winningIcon != null ? FindChildComponentByName<Transform>(winningIcon, "Multiple") : null;
                if (multiple != null)
                {
                    var fxFire = FindChildComponentByName<Transform>(multiple, "FXFire")
                              ?? FindChildComponentByName<Transform>(multiple, "FxFire");
                    if (fxFire != null) _wsMultipleFxFire = fxFire.gameObject;
                }
            }
        }

        private RectTransform ResolveWsFireTarget()
        {
            ResolveWsFxRefs();
            if (_wsFxFireTarget != null) return _wsFxFireTarget;
            if (_btnWinningStreak != null) return _btnWinningStreak.transform as RectTransform;
            return transform as RectTransform;
        }

        private Transform ResolveWsFxParent()
        {
            if (UIManager.HasInstance)
            {
                var ui = UIManager.Instance;
                if (ui.EffectTr != null) return ui.EffectTr;
                if (ui.UiTr != null) return ui.UiTr;
            }
            return transform;
        }

        private void ResolveWsFxLocalPoints(RectTransform parentRt, RectTransform target, out Vector3 startLocal, out Vector3 targetLocal)
        {
            startLocal = Vector3.zero;
            targetLocal = Vector3.zero;
            if (parentRt == null || target == null)
            {
                if (target != null) targetLocal = target.localPosition;
                return;
            }

            Canvas canvas = parentRt.GetComponentInParent<Canvas>();
            Camera cam = (canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay) ? canvas.worldCamera : null;

            Vector2 start;
            if (RectTransformUtility.ScreenPointToLocalPointInRectangle(
                    parentRt, new Vector2(Screen.width * 0.5f, Screen.height * 0.5f), cam, out start))
                startLocal = start;

            Vector2 targetScreen = RectTransformUtility.WorldToScreenPoint(cam, target.position);
            Vector2 end;
            if (RectTransformUtility.ScreenPointToLocalPointInRectangle(parentRt, targetScreen, cam, out end))
                targetLocal = end;
        }

        private float ResolveWsStageRatio(int stage1Based, int points)
        {
            var stage = WinningStreakConfigService.HasInstance
                ? WinningStreakConfigService.Instance.GetStage(stage1Based)
                : null;
            if (stage == null || stage.requiredPoints <= 0) return 0f;
            return Mathf.Clamp01((float)Mathf.Max(0, points) / stage.requiredPoints);
        }

        /// <summary>PopupTextInventory 정합 — prefab 정적 텍스트(영문 리터럴) 런타임 강제 적용.
        /// prefab 이 binary 직렬화라 m_text 직접 수정이 불가능하므로 OnEnable 시점에 코드로 덮어쓴다.
        /// 로컬라이제이션 도입 시 이 메서드를 LocalizationKey 조회로 치환.</summary>
        private void ApplyStaticTextOverrides()
        {
            // BottomNav — 'Setting' → 'Settings' (P0-23). 텍스트는 CSV(TextData) Key 로드.
            if (_txtSetting != null) _txtSetting.text = LocalizationService.Get("ui.settings.title");

            // RightArea — 'No Ads' → 'NO ADS' (P0-3a)
            if (_btnNoAds != null)
            {
                var noAdsTexts = _btnNoAds.GetComponentsInChildren<TMP_Text>(true);
                for (int i = 0; i < noAdsTexts.Length; i++)
                {
                    if (noAdsTexts[i] != null) noAdsTexts[i].text = LocalizationService.Get("ui.lobby.noads");
                }
            }
        }

        /// <summary>WS 회차 남은 시간 표기 — [2026-06-12] "02:21" 형식이 의미 모호("2d 21h"로 요청) →
        /// 1d+: "{d}d {h}h", 1h+: "{h}h {m}m", &lt;1h: "{m}m {s}s".</summary>
        // Dynamic play-level localization guard.
        private void DisableDynamicPlayLevelLocalization()
        {
            // ROLLBACK_LOBBY_PLAY_LEVEL_TEXT_SYNC_20260616:
            // TxtLevelBalance/TxtLevelBalanceOutline are runtime difficulty labels. Prefab-side
            // UIText keys can re-apply different static keys (ex: ui.hard vs ui.hardlevel) after
            // UILobby sets the value, so disable only these dynamic label localizers.
            // Rollback: remove this method call if prefab UIText keys are manually unified.
            DisableUIText(_txtPlayLevel);
            DisableUIText(_txtPlayLevelOutline);
        }

        private static void DisableUIText(TMP_Text text)
        {
            if (text == null) return;
            var uiText = text.GetComponent<UIText>();
            if (uiText != null) uiText.enabled = false;
        }

        private void HandleLobbyLanguageChanged()
        {
            if (_currentPlayLevelId > 0)
            {
                string levelStr = "Level " + _currentPlayLevelId;
                if (_txtPlay != null) _txtPlay.text = levelStr;
                if (_txtPlayOutline != null) _txtPlayOutline.text = levelStr;
            }
            ApplyPlayLevelBalanceText(_currentPlayDifficulty);
            ApplyStaticTextOverrides();
        }

        private void ApplyPlayLevelBalanceText(DifficultyPurpose difficulty)
        {
            bool showBalance = difficulty == DifficultyPurpose.Hard || difficulty == DifficultyPurpose.SuperHard;
            string balanceStr = difficulty == DifficultyPurpose.SuperHard
                ? LocalizationService.Get("ui.superhard")
                : LocalizationService.Get("ui.hard");

            SetTextPair(_txtPlayLevel, _txtPlayLevelOutline, balanceStr, showBalance);
        }

        private static void SetTextPair(TMP_Text text, TMP_Text outline, string value, bool visible)
        {
            if (text != null)
            {
                text.gameObject.SetActive(visible);
                if (visible) text.text = value;
            }

            if (outline != null)
            {
                outline.gameObject.SetActive(visible);
                if (visible) outline.text = value;
            }
        }

        private static string FormatWsRoundRemaining(System.TimeSpan r)
        {
            if (r.Ticks < 0) r = System.TimeSpan.Zero;
            if (r.TotalDays >= 1.0)
                return $"{(int)r.TotalDays}d {r.Hours}h";
            if (r.TotalHours >= 1.0)
                return $"{r.Hours}h {r.Minutes}m";
            return $"{r.Minutes}m {r.Seconds}s";
        }

        private void ResolveWsTexts()
        {
            if (_wsTextsResolved) return;
            Transform root = _wsDisplayRoot != null ? _wsDisplayRoot.transform
                           : (_btnWinningStreak != null ? _btnWinningStreak.transform : null);
            if (root == null) return; // 아직 root 없음 → 다음 기회에 재시도

            _wsTxtTimer        = FindChildComponentByName<TMP_Text>(root, "TextTimer");
            _wsTxtTimerOutline = FindChildComponentByName<TMP_Text>(root, "TextTimerOutline");

            // [WS 게이지 텍스트 2026-06-11] 두 쌍을 부모 스코프로 분리 해석:
            //   Gauge 하단 TextGauge/Outline       = 현재 포인트 "{cur}/{required}"
            //   WinningIcon 하단 TextGauge/Outline = 현재 배수 "x{n}"
            Transform gaugeRoot = FindChildComponentByName<Transform>(root, "Gauge");
            Transform iconRoot  = FindChildComponentByName<Transform>(root, "WinningIcon");
            _wsTxtPoints        = gaugeRoot != null ? FindChildComponentByName<TMP_Text>(gaugeRoot, "TextGauge") : null;
            _wsTxtPointsOutline = gaugeRoot != null ? FindChildComponentByName<TMP_Text>(gaugeRoot, "TextGaugeOutline") : null;
            _wsTxtGauge         = iconRoot != null ? FindChildComponentByName<TMP_Text>(iconRoot, "TextGauge") : null;
            _wsTxtGaugeOutline  = iconRoot != null ? FindChildComponentByName<TMP_Text>(iconRoot, "TextGaugeOutline") : null;

            // 폴백 — 스코프 부모를 못 찾는 프리팹 변형에선 기존 전역 탐색으로 배수만이라도 표시.
            if (_wsTxtGauge == null && _wsTxtPoints == null)
            {
                _wsTxtGauge        = FindChildComponentByName<TMP_Text>(root, "TextGauge");
                _wsTxtGaugeOutline = FindChildComponentByName<TMP_Text>(root, "TextGaugeOutline");
            }

            // 핵심 텍스트를 실제로 찾았을 때만 캐시. 못 찾으면 다음 호출에서 재시도(영구 null 방지).
            _wsTextsResolved = _wsTxtGauge != null || _wsTxtPoints != null;
        }

        // RewardItem 구조 내부 아이콘 Image 후보 이름.
        // [2026-06-11 스펙] 로비 프리팹의 아이템 변형(RewardItem>RewardItem) 아이콘은 "ImageGold" 이름 —
        // 이 이미지를 해당 보상 sprite 로 교체한다 (RewardGold 변형의 ImageGold 는 골드 고정, 스코프 분리로 안전).
        // ImageGift 는 중첩 상자 이미지라 후보에서 제외 (오인 스왑 방지).
        private static readonly string[] WsRewardIconNames = { "ImageRewardItem", "ImageItem", "ImageGold", "ImageReward" };

        /// <summary>RewardItem(_wsRewardItem) 변형 전환 + 아이콘/카운트 바인딩.
        /// 구조: RewardGold(코인용·ImageGold 고정) / RewardItem(아이템용·ImageItem swap) / ImageGift(WS 미사용).
        /// 코인 → RewardGold 활성, 그 외 → RewardItem 변형 활성 + ImageItem sprite swap.
        /// 아이콘 키는 PopupWinningStreak 와 동일(SPR_ICON*)이라 팝업과 같은 이미지가 나오게 한다.</summary>
        private void BindWsRewardItem(WinningStreakStage stage)
        {
            if (_wsRewardItem == null) return;

            ResolveWsPrimaryReward(stage, out string spriteKey, out int count, out bool isCoin);
            int rewardEntryCount = CountWsRewardEntries(stage != null ? stage.rewards : null);
            bool useGift = rewardEntryCount >= 2;
            bool useGold = rewardEntryCount == 1 && isCoin;
            bool useItem = rewardEntryCount == 1 && !isCoin;

            Transform root = _wsRewardItem.transform;
            // ROLLBACK_WS_REWARD_VARIANT_SERIALIZE_20260616: 직접 참조(SerializeField) 우선 — 노드 리네임 무관.
            //   미할당 시 이름 탐색 폴백(Gold/RewardGold, Item/RewardItem 양쪽 지원). 롤백: 직접참조 우항 제거.
            GameObject goldVariant = _wsRewardGoldVariant != null
                ? _wsRewardGoldVariant
                : (FindDirectChildGO(root, "Gold") ?? FindDirectChildGO(root, "RewardGold"));
            GameObject itemVariant = _wsRewardItemVariant != null
                ? _wsRewardItemVariant
                : (FindDirectChildGO(root, "Item") ?? FindDirectChildGO(root, "RewardItem"));
            // [2026-06-11 fix] ImageGift 는 직계가 아니라 RewardItem 내부에 중첩된 프리팹 구조
            // (PopupWinningStreak 슬롯과 동일). 전체 탐색 + 중첩 시 부모(RewardItem)도 함께 활성화.
            GameObject giftVariant = FindDirectChildGO(root, "ImageGift")
                ?? FindChildComponentByName<Transform>(root, "ImageGift")?.gameObject;
            bool giftInsideItem = giftVariant != null && itemVariant != null
                && giftVariant.transform.IsChildOf(itemVariant.transform);

            // 타입별 변형 토글.
            // ROLLBACK_WINNING_STREAK_REWARDIMG_VARIANT_RULE:
            // RewardGold=gold only, RewardItem=single item/heart, ImageGift=composite rewards(상자만).
            if (goldVariant != null) goldVariant.SetActive(useGold);
            if (itemVariant != null) itemVariant.SetActive(useItem || (useGift && giftInsideItem));
            if (giftVariant != null) giftVariant.SetActive(useGift);

            Transform active = ((useGold ? goldVariant : useItem ? itemVariant : giftVariant) != null
                ? (useGold ? goldVariant : useItem ? itemVariant : giftVariant).transform
                : root);

            // 아이템 변형 내부 비주얼 정리 — Heart 보상은 sprite swap 대신 전용 ImageHeart 표시,
            // gift 모드에선 아이템 아이콘/깃발류를 숨겨 '상자만' 보이게.
            Transform itemScope = itemVariant != null ? itemVariant.transform : root;
            GameObject imageHeartGo = FindChildComponentByName<Transform>(itemScope, "ImageHeart")?.gameObject;
            bool isHeart = useItem && stage?.rewards != null && stage.rewards.infiniteHeartsSeconds > 0;
            if (imageHeartGo != null) imageHeartGo.SetActive(useItem && isHeart);
            if (useGift)
            {
                // 상자(선물) 모드 — 아이템 아이콘 후보(ImageItem/ImageGold 등) 전부 숨기고 깃발류도 끔 ('상자만').
                for (int i = 0; i < WsRewardIconNames.Length; i++)
                {
                    var go = FindChildComponentByName<Transform>(itemScope, WsRewardIconNames[i])?.gameObject;
                    if (go != null && go != giftVariant) go.SetActive(false);
                }
                SetWsItemFlagsActive(itemScope, false);
                // ROLLBACK_WS_GIFT_HEART_DYNAMIC_SPRITE_20260616: ImageGift(상자) sprite 를 코드로 동적 로드 —
                //   프리팹 정적 할당 의존 제거(부스터 아이콘 swap 과 동일 방식). 롤백: 이 한 줄 제거.
                ApplyWsDynamicSprite(giftVariant, Const.SPR_ICONGIFT);
            }
            else if (useItem && isHeart)
            {
                // Heart 전용 이미지 사용 — ImageItem 후보 아이콘 GO 전부 끔 (sprite swap 스킵).
                for (int i = 0; i < WsRewardIconNames.Length; i++)
                {
                    var go = FindChildComponentByName<Transform>(itemScope, WsRewardIconNames[i])?.gameObject;
                    if (go != null) go.SetActive(false);
                }
                SetWsItemFlagsActive(itemScope, true);
                // ROLLBACK_WS_GIFT_HEART_DYNAMIC_SPRITE_20260616: ImageHeart sprite 를 코드로 동적 로드(무한하트 아이콘) —
                //   프리팹 정적 할당 의존 제거. 롤백: 이 한 줄 제거.
                ApplyWsDynamicSprite(imageHeartGo, Const.SPR_ICONHEARINFINITE);
            }
            else if (useItem)
            {
                // [2026-06-11 스펙] 단일 아이템(상자 아님) — FlagBack (1)/FlagBack/RewardFlag 활성 + 수량/아이콘 표시.
                SetWsItemFlagsActive(itemScope, true);
            }

            // [2026-06-11 스펙 확정] 골드 보상 = RewardGold 변형 + 그 자식 ImageGold(골드 고정 비주얼) 표시.
            // 변형 GO 만 켜면 ImageGold 가 프리팹에서 꺼져 있을 때 이미지가 안 나옴 → 명시 활성화.
            if (useGold && goldVariant != null)
            {
                var goldIconTr = FindChildComponentByName<Transform>(goldVariant.transform, "ImageGold");
                if (goldIconTr != null)
                {
                    Image goldImg = goldIconTr.GetComponent<Image>();
                    if (goldImg == null) goldImg = goldIconTr.GetComponentInChildren<Image>(true);
                    if (goldImg != null)
                    {
                        goldImg.enabled = true;
                        for (Transform t = goldImg.transform; t != null && t != goldVariant.transform; t = t.parent)
                            if (!t.gameObject.activeSelf) t.gameObject.SetActive(true);
                        if (!goldIconTr.gameObject.activeSelf) goldIconTr.gameObject.SetActive(true);
                    }
                }
                else
                {
                    Debug.LogWarning("[UILobby] WS RewardGold 의 ImageGold 미발견 — 프리팹 구조 확인 필요.");
                }
            }

            // 아이템 보상 = RewardItem 변형 + 그 자식 ImageItem 을 보상 sprite 로 swap (코인은 ImageGold 고정 비주얼).
            // Heart 보상은 전용 ImageHeart 사용 → sprite swap 스킵.
            if (useItem && !isHeart && !string.IsNullOrEmpty(spriteKey) && ResourceManager.HasInstance)
            {
                // [2026-06-11 fix] '이름 일치 Image 컴포넌트' 탐색은 ImageItem 이 홀더(Image 없는 GO)인
                // 구조에서 무음 실패(텍스트만 갱신) → GO 기준 탐색 + Image 는 자신→자식 순 해석 (popup 동일).
                Image icon = null;
                for (int i = 0; i < WsRewardIconNames.Length && icon == null; i++)
                {
                    var iconTr = FindChildComponentByName<Transform>(active, WsRewardIconNames[i]);
                    if (iconTr == null) continue;
                    icon = iconTr.GetComponent<Image>();
                    if (icon == null) icon = iconTr.GetComponentInChildren<Image>(true);
                }
                var spr = ResourceManager.Instance.GetUISprite(spriteKey);
                if (icon != null && spr != null)
                {
                    icon.sprite = spr;
                    icon.enabled = true;
                    // ImageItem 은 프리팹 기본 비활성일 수 있고 Image 가 홀더의 자식일 수도 있어
                    // 변형 루트(active)까지 조상 활성화.
                    for (Transform t = icon.transform; t != null && t != active; t = t.parent)
                        if (!t.gameObject.activeSelf) t.gameObject.SetActive(true);
                }
                else
                {
                    Debug.LogWarning($"[UILobby] WS RewardItem 아이콘 갱신 실패 — icon={(icon != null)} sprite={(spr != null)} key={spriteKey}");
                }
            }

            // 카운트 텍스트 (활성 변형 내부 TextReward + Outline).
            // 포맷은 PopupWinningStreak 와 통일: 코인=숫자, 부스터=xN, 무한하트=24h, gift=빈칸(상자만).
            string countText = "";
            if (useGold && count > 0) countText = count.ToString();
            else if (useItem)
            {
                var r = stage != null ? stage.rewards : null;
                if (r != null && r.infiniteHeartsSeconds > 0) countText = FormatWsInfiniteHearts(r.infiniteHeartsSeconds);
                else if (count > 0) countText = $"x{count}";
            }
            var t1 = FindChildComponentByName<TMP_Text>(active, "TextReward");
            if (t1 != null) t1.text = countText;
            var t2 = FindChildComponentByName<TMP_Text>(active, "TextRewardOutline");
            if (t2 != null) t2.text = countText;
        }

        /// <summary>RewardItem 변형 내부의 깃발/카운트 배경(FlagBack*, RewardFlag) 토글 — gift 모드에서 '상자만' 표시용.</summary>
        private static void SetWsItemFlagsActive(Transform itemScope, bool active)
        {
            if (itemScope == null) return;
            var all = itemScope.GetComponentsInChildren<Transform>(true);
            for (int i = 0; i < all.Length; i++)
            {
                if (all[i] == null) continue;
                string n = all[i].gameObject.name;
                if (n.StartsWith("FlagBack") || n == "RewardFlag")
                    all[i].gameObject.SetActive(active);
            }
        }

        /// <summary>무한하트 보상 시간 표기 — PopupWinningStreak.FormatInfiniteHearts 와 동일 규칙.</summary>
        private static string FormatWsInfiniteHearts(int seconds)
        {
            if (seconds <= 0) return "";
            int h = seconds / 3600;
            if (h >= 1) return $"{h}h";
            int m = seconds / 60;
            return m > 0 ? $"{m}m" : $"{seconds}s";
        }

        /// <summary>root 의 직계 자식 중 이름 일치하는 첫 GameObject (비활성 포함). root 와 동명인 변형 child 탐색용.</summary>
        private static GameObject FindDirectChildGO(Transform root, string name)
        {
            if (root == null) return null;
            for (int i = 0; i < root.childCount; i++)
                if (root.GetChild(i).name == name) return root.GetChild(i).gameObject;
            return null;
        }

        private static int CountWsRewardEntries(ShopRewards rewards)
        {
            if (rewards == null) return 0;
            int count = 0;
            if (rewards.coins > 0) count++;
            if (rewards.boosters != null)
            {
                if (rewards.boosters.hand > 0) count++;
                if (rewards.boosters.shuffle > 0) count++;
                if (rewards.boosters.zap > 0) count++;
            }
            if (rewards.infiniteHeartsSeconds > 0) count++;
            return count;
        }

        /// <summary>stage 의 대표 보상(코인>핸드>셔플>잽>무한하트 우선) 아이콘 키 + 카운트 + 코인 여부.</summary>
        // ROLLBACK_WS_GIFT_HEART_DYNAMIC_SPRITE_20260616: go 의 Image(자신→자식)에 키로 로드한 sprite 설정 + 활성화.
        //   ImageGift(상자)·ImageHeart 를 프리팹 정적 할당 없이 코드로 채움(부스터 아이콘 swap 과 동일 방식).
        private static void ApplyWsDynamicSprite(GameObject go, string spriteKey)
        {
            if (go == null || string.IsNullOrEmpty(spriteKey) || !ResourceManager.HasInstance) return;
            Image img = go.GetComponent<Image>();
            if (img == null) img = go.GetComponentInChildren<Image>(true);
            var spr = ResourceManager.Instance.GetUISprite(spriteKey);
            if (img == null || spr == null)
            {
                Debug.LogWarning($"[UILobby] WS 동적 sprite 실패 — img={(img != null)} spr={(spr != null)} key={spriteKey}");
                return;
            }
            img.sprite = spr;
            img.enabled = true;
            if (!img.gameObject.activeSelf) img.gameObject.SetActive(true);
        }

        private static void ResolveWsPrimaryReward(WinningStreakStage stage, out string spriteKey, out int count, out bool isCoin)
        {
            spriteKey = null; count = 0; isCoin = false;
            if (stage == null || stage.rewards == null) return;
            var r = stage.rewards;
            if (r.coins > 0)                                       { spriteKey = Const.SPR_ICONGOLD;        count = r.coins; isCoin = true; }
            else if (r.boosters != null && r.boosters.hand > 0)    { spriteKey = Const.SPR_ICONHAND;    count = r.boosters.hand; }
            else if (r.boosters != null && r.boosters.shuffle > 0) { spriteKey = Const.SPR_ICONSUFFLE;  count = r.boosters.shuffle; }
            else if (r.boosters != null && r.boosters.zap > 0)     { spriteKey = Const.SPR_ICONZAP;     count = r.boosters.zap; }
            else if (r.infiniteHeartsSeconds > 0)             { spriteKey = Const.SPR_ICONHEARINFINITE; count = 0; } // 하트는 시간이라 카운트 미표시
        }

        /// <summary>root 하위에서 이름이 일치하는 첫 컴포넌트(T) 탐색 (비활성 포함).</summary>
        private static T FindChildComponentByName<T>(Transform root, string name) where T : Component
        {
            if (root == null) return null;
            var all = root.GetComponentsInChildren<T>(true);
            for (int i = 0; i < all.Length; i++)
                if (all[i] != null && all[i].gameObject.name == name) return all[i];
            return null;
        }

        /// <summary>좌상단 프로필 아이콘/프레임 sprite 를 UserData index 기반으로 갱신.
        /// _profileAssets/Image 미할당 시 silent skip → 디자이너 wire 전에도 안전.</summary>
        private void RefreshProfileDisplay()
        {
            if (_profileAssets == null) return;
            int iconIdx = 0, frameIdx = 0;
            if (UserDataService.HasInstance && UserDataService.Instance.IsReady
                && UserDataService.Instance.CurrentUser != null)
            {
                iconIdx  = UserDataService.Instance.CurrentUser.profileIconNumber;
                frameIdx = UserDataService.Instance.CurrentUser.profileFrameNumber;
            }
            if (_imgProfileIcon != null)
            {
                var sp = _profileAssets.GetIcon(iconIdx);
                _imgProfileIcon.sprite = sp;
                _imgProfileIcon.enabled = sp != null;
            }
            if (_imgProfileFrame != null)
            {
                var sp = _profileAssets.GetFrame(frameIdx);
                _imgProfileFrame.sprite = sp;
                _imgProfileFrame.enabled = sp != null;
            }
        }

        /// <summary>
        /// [1.0] Profile/Avatar 는 1.1 기능. 좌상단 프로필 패널(편집 진입 버튼 + 아이콘/프레임 표시)을
        /// 통째로 숨긴다. 버튼이 패널 root 이면 자식 아이콘/프레임도 함께 사라지며, 별도 배치된 경우에도
        /// 안전하도록 Image GameObject 도 명시적으로 비활성화한다. Const.PROFILE_ENABLED=true 시 노출 복귀.</summary>
        public void SetProfilePanelActive(bool active)
        {
            // [#3] Profile 숨김 시 GameObject 를 비활성화 → LayoutGroup 슬롯이 접히며 골드 패널이 Profile 자리로 이동(의도된 동작).
            if (_btnProfilePanel != null && _btnProfilePanel.gameObject.activeSelf != active)
                _btnProfilePanel.gameObject.SetActive(active);
            if (_imgProfileIcon != null && _imgProfileIcon.gameObject.activeSelf != active)
                _imgProfileIcon.gameObject.SetActive(active);
            if (_imgProfileFrame != null && _imgProfileFrame.gameObject.activeSelf != active)
                _imgProfileFrame.gameObject.SetActive(active);
        }

        /// <summary>
        /// Inspector 미할당 fallback — TopBarArea/BottomNav 하위에서 'Rail' 자식 RectTransform 을 탐색.
        /// 못 찾으면 조용히 null 유지 (애니메이션 스킵).
        /// </summary>
        private void ResolveRailRefs()
        {
            if (_railTop == null && _topBarArea != null)
            {
                var topRail = _topBarArea.Find("Rail");
                if (topRail != null) _railTop = topRail as RectTransform;
            }

            if (_railBottom == null)
            {
                Transform bottomRail = transform.Find("BottomNavIArea/Rail");
                if (bottomRail == null) bottomRail = transform.Find("BottomNav/Rail");
                if (bottomRail == null) bottomRail = transform.Find("BottomNavArea/Rail");
                if (bottomRail != null) _railBottom = bottomRail as RectTransform;
            }
        }

        /// <summary>
        /// Top/Bottom Rail 을 화면 위쪽 바깥(+120) 에서 원위치(0) 로 OutCubic 곡선 슬라이드.
        /// 진입/탭 복귀 시 호출. 시작값은 anchoredPosition 직접 할당으로 적용 (페이지 트윈 패턴과 일관).
        /// 방향: Top/Bottom 둘 다 위→아래로 동일 방향 슬라이드.
        /// </summary>
        public void PlayRailEnterAnimation()
        {
            _railTopTween?.Kill();
            _railBottomTween?.Kill();

            if (_railTop != null)
            {
                var p = _railTop.anchoredPosition;
                _railTop.anchoredPosition = new Vector2(p.x, RAIL_TOP_ENTER_OFFSET_Y);
                _railTopTween = _railTop.DOAnchorPosY(0f, RAIL_ENTER_DURATION)
                    .SetEase(Ease.OutCubic)
                    .SetUpdate(true);
            }

            if (_railBottom != null)
            {
                var p = _railBottom.anchoredPosition;
                _railBottom.anchoredPosition = new Vector2(p.x, RAIL_BOTTOM_ENTER_OFFSET_Y);
                _railBottomTween = _railBottom.DOAnchorPosY(0f, RAIL_ENTER_DURATION)
                    .SetEase(Ease.OutCubic)
                    .SetUpdate(true);
            }
        }

        /// <summary>
        /// 사용자가 아래방향으로 Rail 을 끌어내린 듯한 피드백 — RAIL_PULL_DOWN_Y 까지 내려갔다가 OutBack 으로 0 복귀.
        /// 진행 중 _railTopTween/_railBottomTween 캐시는 Kill 후 새 Sequence 로 교체.
        /// </summary>
        public void PlayRailPullDownAnimation()
        {
            _railTopTween?.Kill();
            _railBottomTween?.Kill();

            if (_railTop != null)
            {
                _railTopTween = DOTween.Sequence()
                    .Append(_railTop.DOAnchorPosY(RAIL_PULL_DOWN_Y, RAIL_PULL_DOWN_DURATION).SetEase(Ease.OutCubic))
                    .Append(_railTop.DOAnchorPosY(0f, RAIL_PULL_DOWN_RETURN_DURATION).SetEase(Ease.OutBack))
                    .SetUpdate(true);
            }

            if (_railBottom != null)
            {
                _railBottomTween = DOTween.Sequence()
                    .Append(_railBottom.DOAnchorPosY(RAIL_PULL_DOWN_Y, RAIL_PULL_DOWN_DURATION).SetEase(Ease.OutCubic))
                    .Append(_railBottom.DOAnchorPosY(0f, RAIL_PULL_DOWN_RETURN_DURATION).SetEase(Ease.OutBack))
                    .SetUpdate(true);
            }
        }

        // 인게임 종료 후 로비 복귀 시 LevelObject 가 1816→1145 로 OutCubic 슬라이드 다운 (Rail Enter Animation 과 동시 재생)
        public void PlayLevelObjectEnterAnimation()
        {
            _levelObjectEnterTween?.Kill();
            if (_levelBoxContainer == null) return;

            var p = _levelBoxContainer.anchoredPosition;
            _levelBoxContainer.anchoredPosition = new Vector2(p.x, LEVEL_OBJECT_ENTER_START_Y);
            _levelObjectEnterTween = _levelBoxContainer.DOAnchorPosY(LEVEL_OBJECT_ENTER_END_Y, LEVEL_OBJECT_ENTER_DURATION)
                .SetEase(Ease.OutCubic)
                .SetUpdate(true);
        }

        /// <summary>
        /// Shop 페이지 하위 ScrollRect를 런타임에 강제로 올바른 설정으로 맞춤.
        /// 프리팹 Inspector 세팅 누락을 코드에서 보정.
        /// </summary>
        private void AutoConfigureShopScroll()
        {
            if (_pageShop == null) return;

            var scrolls = _pageShop.GetComponentsInChildren<ScrollRect>(true);
            for (int i = 0; i < scrolls.Length; i++)
            {
                ConfigureScrollRect(scrolls[i]);
            }
        }

        private const float DEFAULT_ITEM_PREFERRED_HEIGHT = 200f;
        private const float DEFAULT_LAYOUT_SPACING = 20f;

        private static void ConfigureScrollRect(ScrollRect sr)
        {
            if (sr == null) return;

            // 1. ScrollRect 자체
            sr.vertical = true;
            sr.horizontal = false;
            sr.movementType = ScrollRect.MovementType.Clamped;
            sr.inertia = false;
            sr.scrollSensitivity = 20f;

            // 2. Viewport
            RectTransform viewport = sr.viewport;
            if (viewport == null)
            {
                viewport = sr.GetComponent<RectTransform>();
                sr.viewport = viewport;
            }
            if (viewport != null)
            {
                var vpImage = viewport.GetComponent<Image>();
                if (vpImage == null)
                {
                    vpImage = viewport.gameObject.AddComponent<Image>();
                    vpImage.color = new Color(1f, 1f, 1f, 0f); // 투명
                }
                vpImage.raycastTarget = true;

                if (viewport.GetComponent<Mask>() == null &&
                    viewport.GetComponent<RectMask2D>() == null)
                {
                    viewport.gameObject.AddComponent<RectMask2D>();
                }
            }

            // 3. Content
            RectTransform content = sr.content;
            if (content == null) return;

            // Anchor/Pivot: Top-Stretch, 상단 기준
            content.anchorMin = new Vector2(0f, 1f);
            content.anchorMax = new Vector2(1f, 1f);
            content.pivot = new Vector2(0.5f, 1f);

            // VerticalLayoutGroup
            var vlg = content.GetComponent<VerticalLayoutGroup>();
            if (vlg == null) vlg = content.gameObject.AddComponent<VerticalLayoutGroup>();
            vlg.childControlHeight = true;
            vlg.childControlWidth = true;
            vlg.childForceExpandHeight = false;
            vlg.childForceExpandWidth = true;
            vlg.childAlignment = TextAnchor.UpperCenter;
            if (vlg.spacing < 1f) vlg.spacing = DEFAULT_LAYOUT_SPACING;

            // ContentSizeFitter
            var csf = content.GetComponent<ContentSizeFitter>();
            if (csf == null) csf = content.gameObject.AddComponent<ContentSizeFitter>();
            csf.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            csf.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;

            // 4. 하위 아이템: LayoutElement 자동 부착 (preferred height 제공)
            for (int i = 0; i < content.childCount; i++)
            {
                var child = content.GetChild(i) as RectTransform;
                if (child == null) continue;
                if (child.GetComponent<LayoutElement>() != null) continue;

                var le = child.gameObject.AddComponent<LayoutElement>();
                // 기존 아이템 높이가 있으면 존중, 없으면 기본값
                float h = child.rect.height;
                le.preferredHeight = h > 1f ? h : DEFAULT_ITEM_PREFERRED_HEIGHT;
            }

            // 레이아웃 재계산
            LayoutRebuilder.ForceRebuildLayoutImmediate(content);
            sr.verticalNormalizedPosition = 1f;
        }

        protected override void OnDestroy()
        {
            if (_btnWinningStreak != null) _btnWinningStreak.onClick.RemoveAllListeners();
            base.OnDestroy();
            LocalizationService.OnLanguageChanged -= HandleLobbyLanguageChanged;
            EventBus.Unsubscribe<OnAdsRemovedChanged>(HandleAdsRemovedChanged);
            UnhookProfileEvents();
            UnhookWinningStreakEvents();
            _pageTween?.Kill();
            _goldTween?.Kill();
            _railTopTween?.Kill();
            _railBottomTween?.Kill();
            _levelObjectEnterTween?.Kill();
            if (_wsLobbyFxCoroutine != null)
            {
                StopCoroutine(_wsLobbyFxCoroutine);
                _wsLobbyFxCoroutine = null;
            }
            _wsLobbyFxArmed = false;
            _wsLobbyFxSequence?.Kill();
            _wsLobbyFxSequence = null;
            _wsMultiplierTextPunchSeq?.Kill();
            _wsMultiplierTextPunchSeq = null;
        }

        private void Update()
        {
            HandleSwipeDrag();

            // WS 회차 타이머 1초 갱신 (버튼 노출 중일 때만).
            if (_btnWinningStreak != null && _btnWinningStreak.gameObject.activeSelf)
            {
                _wsTimerTick += Time.unscaledDeltaTime;
                if (_wsTimerTick >= 1f)
                {
                    _wsTimerTick = 0f;
                    if (WinningStreakManager.HasInstance && WinningStreakManager.Instance.IsUnlocked)
                    {
                        ResolveWsTexts();
                        string time = FormatWsRoundRemaining(WinningStreakManager.Instance.RoundRemaining);
                        if (_wsTxtTimer != null) _wsTxtTimer.text = time;
                        if (_wsTxtTimerOutline != null) _wsTxtTimerOutline.text = time;
                    }
                }
            }

#if UNITY_EDITOR
            // [에디터 전용] z 키 → WinningStreak 로비 연출 전체를 세팅한 대로 1회 재생(검증용, 실제 보상 미지급).
            if (Keyboard.current != null && Keyboard.current.zKey.wasPressedThisFrame)
                StartWinningStreakLobbyFxPreview();
            // [에디터 전용] x 키 → 실패 연출 미리보기 (Multiplier 가 높은 배수에서 1 로 떨어지는 연출, 실제 state 변경 없음).
            if (Keyboard.current != null && Keyboard.current.xKey.wasPressedThisFrame)
                StartWinningStreakFailPreview();
            // [에디터 전용] c 키 → PopupWinningStreak 바로 오픈 (해금 게이트 무시 — 팝업 안에서 1~5 키로 배수 연출 프리뷰).
            if (Keyboard.current != null && Keyboard.current.cKey.wasPressedThisFrame && UIManager.HasInstance)
                UIManager.Instance.OpenUI<PopupWinningStreak>(Const.POPUP_WINNING_STREAK);
            // [에디터 전용] 1~5 키 → 로비 WS Multiplier 배수 연출 프리뷰 (x1/x5/x10/x25/x100).
            //   숨김 상태면 슬라이드 인부터, 표시 중이면 SelectFrame/TextYellow 이동만. z 연출과 조합해 확인용.
            //   PopupWinningStreak 가 열려 있으면 팝업 쪽 1~5 키가 우선 — 로비는 무시.
            if (Keyboard.current != null)
            {
                int wsPreviewMultiplier = 0;
                if (Keyboard.current.digit1Key.wasPressedThisFrame) wsPreviewMultiplier = 1;
                else if (Keyboard.current.digit2Key.wasPressedThisFrame) wsPreviewMultiplier = 5;
                else if (Keyboard.current.digit3Key.wasPressedThisFrame) wsPreviewMultiplier = 10;
                else if (Keyboard.current.digit4Key.wasPressedThisFrame) wsPreviewMultiplier = 25;
                else if (Keyboard.current.digit5Key.wasPressedThisFrame) wsPreviewMultiplier = 100;
                if (wsPreviewMultiplier > 0 && FindObjectOfType<PopupWinningStreak>() == null)
                    StartCoroutine(PreviewWsMultiplierSelect(wsPreviewMultiplier));
            }
#endif
        }

#if UNITY_EDITOR
        /// <summary>[에디터 전용] WS 로비 연출 전체를 샘플 데이터로 재생. stage 보상 연출까지 포함하되 ClaimStage 는 호출하지 않음.</summary>
        private void StartWinningStreakLobbyFxPreview()
        {
            if (_wsLobbyFxCoroutine != null) StopCoroutine(_wsLobbyFxCoroutine);
            _wsLobbyFxCoroutine = StartCoroutine(PlayWinningStreakLobbyFxPreviewRoutine());
        }

        private IEnumerator PlayWinningStreakLobbyFxPreviewRoutine()
        {
            int stage = (WinningStreakManager.HasInstance && WinningStreakManager.Instance.State != null)
                ? Mathf.Max(1, WinningStreakManager.Instance.State.currentStage)
                : 1;
            int previewStreak = (WinningStreakManager.HasInstance && WinningStreakManager.Instance.State != null)
                ? Mathf.Max(1, WinningStreakManager.Instance.State.currentStreak)
                : 1;
            // [WS 0단계 2026-06-12] 프리뷰 gainedPoints 를 수 연산 모델(1×난이도×배수)과 일치시킴 —
            // 어긋나면 팝업의 최종 보정이 단계 곱을 덮어써 프리뷰가 어색해짐.
            int previewDiffMult = WinningStreakConfigService.HasInstance
                ? WinningStreakConfigService.Instance.ResolveDifficultyMultiplier(DifficultyPurpose.Hard) : 1;
            int previewStreakMult = WinningStreakUI.ResolveCurrentMultiplier();
            var preview = new WinningStreakManager.PendingLobbyAnimation
            {
                startStage = stage,
                startPoints = 0,
                endStage = stage,
                endPoints = 0,
                gainedPoints = Mathf.Max(1, previewDiffMult) * Mathf.Max(1, previewStreakMult),
                achievedStages = new System.Collections.Generic.List<int> { stage },
                // [WS 0단계 2026-06-12] Dim 보상 팝업 프리뷰 — Hard 클리어 가정(FXBadge 노출 확인용).
                startStreak = previewStreak,
                endStreak = previewStreak + 1,
                clearedDifficulty = DifficultyPurpose.Hard,
            };
            yield return PlayWinningStreakRewardPopup(preview);
            yield return PlayWinningStreakLobbyFx(preview, grantRewards: false, forceVisible: true);
            _wsLobbyFxCoroutine = null;
        }

        /// <summary>[에디터 전용] 실패 연출 미리보기 — Multiplier 를 높은 배수로 띄운 뒤 1 로 떨어뜨려 실패 시 배수 리셋을 시각화.
        /// 실제 WS state(streak/points)는 건드리지 않음.</summary>
        private void StartWinningStreakFailPreview()
        {
            if (_wsLobbyFxCoroutine != null) StopCoroutine(_wsLobbyFxCoroutine);
            _wsLobbyFxCoroutine = StartCoroutine(PlayWinningStreakFailPreviewRoutine());
        }

        private IEnumerator PlayWinningStreakFailPreviewRoutine()
        {
            ForceShowWinningStreakUI();
            // 데모용으로 현재 배수(최소 x5 보장)를 먼저 보여준 뒤 1 로 드롭.
            // [WS quit-fail 2026-06-10] 본체는 런타임 실패 연출(PlayWsMultiplierFailFx) 재사용 — 프리뷰/실연출 동일 시퀀스 보장.
            int shownMultiplier = Mathf.Max(5, WinningStreakUI.ResolveCurrentMultiplier());
            yield return PlayWsMultiplierFailFx(shownMultiplier);
            _wsLobbyFxCoroutine = null;
        }

        /// <summary>[에디터 전용] 1~5 키 — 로비 WS Multiplier 배수 연출 프리뷰.
        /// 숨김 위치면 슬라이드 인 후 SelectFrame/TextYellow 이동, 이미 표시 중이면 이동만 (배수 전환 비교용).
        /// 실제 state 변경 없음.</summary>
        private IEnumerator PreviewWsMultiplierSelect(int multiplier)
        {
            ForceShowWinningStreakUI();   // 미해금이어도 WS UI 강제 표시
            ResolveWsFxRefs();
            if (_wsMultiplier == null) yield break;
            if (!_wsMultiplier.gameObject.activeSelf)
                _wsMultiplier.gameObject.SetActive(true);

            // 숨김 위치(-725 쪽)에 있으면 등장 슬라이드부터 — 등장 후 select 이동 순서 그대로 재현.
            // [Multiplier 등장 띠용 완화 2026-06-22] OutBack overshoot=0.4 (기본 1.7) — 시작/최종 X 좌표 불변, 튀어나옴 거리만 축소.
            if (_wsMultiplier.anchoredPosition.x < WS_MULTIPLIER_SHOWN_X - 1f)
                yield return PlayWsMultiplierSlide(WS_MULTIPLIER_SHOWN_X, WS_MULTIPLIER_SLIDE_IN_DURATION, Ease.OutBack, WS_MULTIPLIER_SLIDE_IN_OVERSHOOT);

            WinningStreakUI.PlayLobbyMultiplierSelect(_wsMultiplier, multiplier, multiplier);
        }
#endif

        /// <summary>
        /// Canvas/screen 사이즈가 바뀔 때(해상도·회전 등) 페이지 레이아웃 재계산.
        /// Awake 시점에 rect가 0이어서 1242f fallback으로 잡히는 케이스도 커버.
        /// </summary>
        private void OnRectTransformDimensionsChange()
        {
            if (_pageContainer == null) return;
            RefreshPageLayout();
        }

        #endregion

        #region PageContainer Auto-Build

        /// <summary>
        /// Creates a PageContainer RectTransform at runtime,
        /// reparents Shop/Lobby/Setting into it side by side,
        /// and positions them so sliding the container shows one at a time.
        /// </summary>
        private void BuildPageContainer()
        {
            // Determine page width from our own RectTransform
            _pageWidth = ResolvePageWidth();

            // Create container
            var containerGO = new GameObject("PageContainer");
            containerGO.layer = gameObject.layer;
            _pageContainer = containerGO.AddComponent<RectTransform>();
            _pageContainer.SetParent(transform, false);

            // Container: same height as parent, width = 3 pages
            _pageContainer.anchorMin = new Vector2(0f, 0f);
            _pageContainer.anchorMax = new Vector2(0f, 1f);
            _pageContainer.pivot = new Vector2(0f, 0.5f);
            _pageContainer.sizeDelta = new Vector2(_pageWidth * 3f, 0f);
            _pageContainer.anchoredPosition = Vector2.zero;

            // Place PageContainer right after Background so it's visible
            // but behind TopBar/BottomNav
            var bgTr = transform.Find("Background");
            if (bgTr != null)
            {
                int bgIndex = bgTr.GetSiblingIndex();
                _pageContainer.SetSiblingIndex(bgIndex + 1);
            }
            else
            {
                _pageContainer.SetAsFirstSibling();
            }

            // Reparent pages into container and position them
            ReparentPage(_pageShop, 0);
            ReparentPage(_pageLobby, 1);
            ReparentPage(_pageSetting, 2);
        }

        /// <summary>selfRT.rect.width를 실측. 아직 레이아웃이 안 잡혔으면 Screen.width로 fallback.</summary>
        private float ResolvePageWidth()
        {
            var selfRT = GetComponent<RectTransform>();
            float w = selfRT != null ? selfRT.rect.width : 0f;
            if (w <= 0f)
            {
                // Canvas 레이아웃 전: 현재 화면의 가로 픽셀을 사용 (CanvasScaler가 자동 스케일)
                w = Screen.width > 0 ? Screen.width : 1242f;
            }
            return w;
        }

        /// <summary>
        /// 해상도 변경 시 PageContainer와 하위 페이지들의 크기·위치를 재적용.
        /// 현재 보이는 페이지 오프셋도 함께 갱신.
        /// </summary>
        private void RefreshPageLayout()
        {
            float newWidth = ResolvePageWidth();
            if (newWidth <= 0f) return;
            if (Mathf.Approximately(newWidth, _pageWidth)) return;

            _pageWidth = newWidth;
            _pageContainer.sizeDelta = new Vector2(_pageWidth * 3f, 0f);

            ReparentPage(_pageShop, 0);
            ReparentPage(_pageLobby, 1);
            ReparentPage(_pageSetting, 2);

            // 진행 중인 스와이프 트윈을 죽이고 현재 페이지 위치로 스냅
            _pageTween?.Kill();
            _pageContainer.anchoredPosition = new Vector2(-_currentPageIndex * _pageWidth, _pageContainer.anchoredPosition.y);
        }

        private void ReparentPage(RectTransform page, int index)
        {
            if (page == null) return;

            page.SetParent(_pageContainer, false);

            // Each page fills one _pageWidth slot (기존 원본 구조 유지)
            page.anchorMin = Vector2.zero;
            page.anchorMax = Vector2.zero;
            page.pivot = new Vector2(0f, 0f);
            page.anchoredPosition = new Vector2(index * _pageWidth, 0f);
            page.sizeDelta = new Vector2(_pageWidth, _pageContainer.rect.height > 0 ? _pageContainer.rect.height : 1920f);

            // Stretch height
            page.anchorMin = new Vector2(0f, 0f);
            page.anchorMax = new Vector2(0f, 1f);
            page.offsetMin = new Vector2(index * _pageWidth, 0f);
            page.offsetMax = new Vector2(index * _pageWidth + _pageWidth, 0f);
        }

        #endregion

        #region Public Methods — Display

        /// <summary>
        /// 골드 텍스트 즉시 스냅 (초기/비연출 케이스). 진행 중인 카운트 트윈은 종료.
        /// </summary>
        public void SetGoldText(int coins)
        {
            _goldTween?.Kill();
            _displayedCoins = coins;
            ApplyGoldText(coins);
        }

        /// <summary>
        /// 현재 표시값 → target 까지 카운트 트윈으로 연출. timeScale 영향 회피(SetUpdate(true)).
        /// </summary>
        public void SetGoldTextAnimated(int targetCoins, float duration = 0.45f)
        {
            _goldTween?.Kill();

            int from = _displayedCoins;
            if (from == targetCoins)
            {
                ApplyGoldText(targetCoins);
                return;
            }

            _goldTween = DOTween.To(
                    () => _displayedCoins,
                    v => { _displayedCoins = v; ApplyGoldText(v); },
                    targetCoins,
                    duration)
                .SetEase(Ease.OutCubic)
                .SetUpdate(true)
                .OnComplete(() =>
                {
                    _displayedCoins = targetCoins;
                    ApplyGoldText(targetCoins);
                });
        }

        /// <summary>
        /// 표시값에 delta 만큼 즉시 더하기 — CoinFlyEffect 코인 한 알 도착 시 +1 카운트업용.
        /// </summary>
        public void AddDisplayedGold(int delta)
        {
            _goldTween?.Kill();
            _displayedCoins += delta;
            ApplyGoldText(_displayedCoins);
        }

        /// <summary>
        /// GoldPanel (TopBar 골드 텍스트) 의 화면 좌표. CoinFlyEffect 의 도착 지점으로 사용.
        /// _goldFlyTargetOverride 가 할당돼 있으면 우선 사용 → 디자이너가 Inspector 에서 도착점 선택 가능.
        /// _txtGold 미할당 시 화면 우상단 추정값 fallback.
        /// </summary>
        public Vector2 GetGoldPanelScreenPos()
        {
            RectTransform rt = _goldFlyTargetOverride != null ? _goldFlyTargetOverride
                : (_txtGold != null ? _txtGold.rectTransform : null);
            if (rt == null) return new Vector2(Screen.width * 0.85f, Screen.height * 0.92f);
            var canvas = rt.GetComponentInParent<Canvas>();
            Camera cam = (canvas != null && canvas.renderMode == RenderMode.ScreenSpaceCamera)
                ? canvas.worldCamera : null;
            return RectTransformUtility.WorldToScreenPoint(cam, rt.position);
        }

        /// <summary>
        /// [2026-05-15] LifePanel (TopBar 라이프 텍스트) 의 화면 좌표. ItemFlyEffect 의 도착 지점.
        /// _lifeFlyTargetOverride 가 할당돼 있으면 우선 사용.
        /// _txtLife 미할당 시 화면 좌상단 추정값 fallback.
        /// </summary>
        public Vector2 GetLifePanelScreenPos()
        {
            RectTransform rt = _lifeFlyTargetOverride != null ? _lifeFlyTargetOverride
                : (_txtLife != null ? _txtLife.rectTransform : null);
            if (rt == null) return new Vector2(Screen.width * 0.15f, Screen.height * 0.92f);
            var canvas = rt.GetComponentInParent<Canvas>();
            Camera cam = (canvas != null && canvas.renderMode == RenderMode.ScreenSpaceCamera)
                ? canvas.worldCamera : null;
            return RectTransformUtility.WorldToScreenPoint(cam, rt.position);
        }

        public Vector2 GetGameStartButtonScreenPos()
        {
            if (_btnPlay == null) return new Vector2(Screen.width * 0.5f, Screen.height * 0.25f);
            var rt = _btnPlay.transform as RectTransform;
            if (rt == null) return RectTransformUtility.WorldToScreenPoint(null, _btnPlay.transform.position);

            var canvas = rt.GetComponentInParent<Canvas>();
            Camera cam = (canvas != null && canvas.renderMode == RenderMode.ScreenSpaceCamera)
                ? canvas.worldCamera : null;
            return RectTransformUtility.WorldToScreenPoint(cam, rt.position);
        }

        private Vector3 _lifePanelOriginalScale;
        private bool _lifePanelOriginalCaptured;

        /// <summary>
        /// LifePanel 펄스 연출 — booster/life/infiniteHearts 도착 시 호출.
        /// 수량 증가일 때만 스케일 펀치 + FxFire 발화(debounce 1.5s 로 multi-heart 시퀀스 1회 coalesce).
        /// 초기 로비 진입(첫 호출, _lastShownLife==-1) 또는 감소 시는 no-op.
        /// </summary>
        public void PulseLifePanel(int newLifeCount, float strength = 0.25f, float duration = 0.5f, int vibrato = 6)
        {
            bool isIncrease = (_lastShownLife >= 0) && (newLifeCount > _lastShownLife);
            _lastShownLife = newLifeCount;
            if (!isIncrease) return;

            Transform target = (_txtLife != null && _txtLife.transform.parent != null)
                ? _txtLife.transform.parent
                : (_txtLife != null ? _txtLife.transform : null);
            if (target == null) return;

            if (!_lifePanelOriginalCaptured)
            {
                _lifePanelOriginalScale = target.localScale;
                _lifePanelOriginalCaptured = true;
            }

            // 스케일 펀치 시작 시점에 FxFire 1회 발화(debounce). multi-heart 시퀀스에서 첫 1회만 통과.
            if ((Time.unscaledTime - _lastFxFireTime) > FX_FIRE_DEBOUNCE_SEC)
            {
                PlayLifePanelFxFire();
                _lastFxFireTime = Time.unscaledTime;
            }

            target.DOKill();
            target.localScale = _lifePanelOriginalScale;
            target.DOPunchScale(_lifePanelOriginalScale * strength, duration, vibrato, 1f).SetUpdate(true);
        }

        private Vector3 _goldPanelOriginalScale;
        private bool _goldPanelOriginalCaptured;

        /// <summary>
        /// GoldPanel 펄스 연출 — DOPunchScale 로 커지고-작아지고 반복.
        /// FxGold 코인 도착 시 호출. 매 호출 시 원본 scale 로 복원 후 새 punch 시작 (중첩 호출 시 누적 변형 방지).
        /// </summary>
        public void PulseGoldPanel(float strength = 0.25f, float duration = 0.5f, int vibrato = 6)
        {
            Transform target = (_txtGold != null && _txtGold.transform.parent != null)
                ? _txtGold.transform.parent
                : (_txtGold != null ? _txtGold.transform : null);
            if (target == null) return;

            // FxGold 한 알 도착 + 스케일 펀치 1회와 동기 발화 → 코인당 정확히 1회 FXFire.
            PlayGoldPanelFxFire();

            // 원본 scale 캡처 (1회만, prefab 기본값 보존)
            if (!_goldPanelOriginalCaptured)
            {
                _goldPanelOriginalScale = target.localScale;
                _goldPanelOriginalCaptured = true;
            }

            // 진행 중 트윈 즉시 종료 + 원본 scale 강제 복원 → 새 punch 가 깨끗한 상태에서 시작
            target.DOKill();
            target.localScale = _goldPanelOriginalScale;

            target.DOPunchScale(Vector3.one * strength, duration, vibrato, elasticity: 0.5f)
                  .SetUpdate(true)
                  .OnComplete(() => target.localScale = _goldPanelOriginalScale)
                  .OnKill(()     => target.localScale = _goldPanelOriginalScale);
        }

        /// <summary>4개 골드 텍스트(메인+Outline, Shop+Outline) 동기화. N0 포맷.</summary>
        private Vector3 _gameStartButtonOriginalScale;
        private bool _gameStartButtonOriginalCaptured;

        public void PulseGameStartButton(float strength = 0.25f, float duration = 0.5f, int vibrato = 6)
        {
            Transform target = _btnPlay != null ? _btnPlay.transform : null;
            if (target == null) return;

            PlayPlayButtonSparks();

            if (!_gameStartButtonOriginalCaptured)
            {
                _gameStartButtonOriginalScale = target.localScale;
                _gameStartButtonOriginalCaptured = true;
            }

            target.DOKill();
            target.localScale = _gameStartButtonOriginalScale;
            target.DOPunchScale(Vector3.one * strength, duration, vibrato, elasticity: 0.5f)
                  .SetUpdate(true)
                  .OnComplete(() => target.localScale = _gameStartButtonOriginalScale)
                  .OnKill(()     => target.localScale = _gameStartButtonOriginalScale);
        }

        private void ApplyGoldText(int value)
        {
            string formatted = value.ToString("N0");
            if (_txtGold != null) _txtGold.text = formatted;
            if (_txtGoldOutline != null) _txtGoldOutline.text = formatted;
            // Shop 패널 골드도 연동
            if (_txtShopGold != null) _txtShopGold.text = formatted;
            if (_txtShopGoldOutline != null) _txtShopGoldOutline.text = formatted;
        }

        public void SetLifeText(int current, int max)
        {
            // 남은 하트 개수만 표시 (최대치 표기 제거)
            string formatted = current.ToString();
            if (_txtLife != null) _txtLife.text = formatted;
            if (_txtLifeOutline != null) _txtLifeOutline.text = formatted;
        }

        public void SetLifeTimerText(string timeText)
        {
            bool hasTimer = !string.IsNullOrEmpty(timeText);
            if (_txtLifeTimer != null) _txtLifeTimer.text = hasTimer ? timeText : "";
            if (_txtLifeTimerOutline != null) _txtLifeTimerOutline.text = hasTimer ? timeText : "";
            if (_imgLifeTimer != null) _imgLifeTimer.gameObject.SetActive(hasTimer);
        }

        public void SetLifePlusButtonVisible(bool visible)
        {
            if (_btnLifePlus != null) _btnLifePlus.gameObject.SetActive(visible);
        }

        /// <summary>무한 하트 상태에서 _imgInfinite 와 _txtLife/_txtLifeOutline 이 상호배타로 토글됨.</summary>
        public void SetInfiniteImageVisible(bool visible)
        {
            if (_imgInfinite != null) _imgInfinite.SetActive(visible);
            if (_txtLife != null) _txtLife.gameObject.SetActive(!visible);
            if (_txtLifeOutline != null) _txtLifeOutline.gameObject.SetActive(!visible);
        }

        #endregion

        #region Public Methods — Play Button

        public void UpdatePlayButton(int levelId, DifficultyPurpose difficulty)
        {
            _currentPlayLevelId = levelId;
            _currentPlayDifficulty = difficulty;

            // ROLLBACK_PLAYBUTTON_LEVEL_DIFFICULTY_SPLIT_20260615: START
            // PlayButton 분리(사용자 지시 2026-06-15):
            //   TxtPlay/TxtPlayOutline(=_txtPlay*)            → "Level {n}" (레벨 번호)
            //   TxtLevelBalance/TxtLevelBalanceOutline(=_txtPlayLevel*) → 난이도 라벨(Normal/Hard/SuperHard, 항상 노출)
            //   (오브젝트가 TxtPlayLevel→TxtLevelBalance 로 리네임됐어도 SerializeField 는 fileID 바인딩이라 그대로 유효.)
            //   이전: TxtPlay 에 SuperHard 텍스트를 넣고 난이도라벨은 Normal 시 숨김. 롤백: 그 버전으로 환원.
            string levelStr = "Level " + levelId;
            if (_txtPlay != null) _txtPlay.text = levelStr;
            if (_txtPlayOutline != null) _txtPlayOutline.text = levelStr;

            // Normal 은 난이도 라벨 숨김(사용자 지시 2026-06-15). Hard/SuperHard 만 노출.
            ApplyPlayLevelBalanceText(difficulty);
            // ROLLBACK_PLAYBUTTON_LEVEL_DIFFICULTY_SPLIT_20260615: END

            // 버튼 배경 스프라이트(난이도별)
            switch (difficulty)
            {
                case DifficultyPurpose.Hard:
                    if (_imgPlayButton != null && _sprBtnPurple != null) _imgPlayButton.sprite = _sprBtnPurple;
                    break;
                case DifficultyPurpose.SuperHard:
                    if (_imgPlayButton != null && _sprBtnRed != null) _imgPlayButton.sprite = _sprBtnRed;
                    break;
                default: // Normal, Tutorial, Rest, Intro
                    if (_imgPlayButton != null && _sprBtnGreen != null) _imgPlayButton.sprite = _sprBtnGreen;
                    break;
            }

            // Play 텍스트 아웃라인 머티리얼
            Material playOutlineMat = difficulty switch
            {
                DifficultyPurpose.Hard      => _matPlayOutlineHard,
                DifficultyPurpose.SuperHard => _matPlayOutlineSuperHard,
                _                           => _matPlayOutlineNormal
            };
            UIOutlineStyle.ApplyMaterialOrColor(_txtPlayOutline, playOutlineMat, UIOutlineStyle.ForDifficulty(difficulty));

            // PlayLevel 텍스트 아웃라인 머티리얼
            Material playLevelOutlineMat = difficulty switch
            {
                DifficultyPurpose.Hard      => _matPlayLevelOutlineHard,
                DifficultyPurpose.SuperHard => _matPlayLevelOutlineSuperHard,
                _                           => _matPlayLevelOutlineNormal
            };
            UIOutlineStyle.ApplyMaterialOrColor(_txtPlayLevelOutline, playLevelOutlineMat, UIOutlineStyle.ForDifficulty(difficulty));

            ApplyPlayBadge(difficulty);
        }

        /// <summary>
        /// Play 버튼 Badge: Normal=숨김 / Hard=badgex3 / SuperHard=badgex5.
        /// 표시용(내부 보상 배율 수치와 별개).
        /// </summary>
        private void ApplyPlayBadge(DifficultyPurpose difficulty)
        {
            if (_imgPlayBadge == null) return;

            Sprite badge = difficulty switch
            {
                DifficultyPurpose.SuperHard => _sprBadgeX5,
                DifficultyPurpose.Hard      => _sprBadgeX3,
                _                            => null
            };

            if (badge != null)
            {
                _imgPlayBadge.sprite = badge;
                _imgPlayBadge.gameObject.SetActive(true);
            }
            else
            {
                _imgPlayBadge.gameObject.SetActive(false);
            }
        }

        public void PlayButtonPressAnim()
        {
            if (_btnPlay == null) return;
            var rt = _btnPlay.transform;
            rt.DOKill();
            rt.DOScale(0.95f, 0.08f).SetEase(Ease.InQuad)
              .OnComplete(() => rt.DOScale(1f, 0.08f).SetEase(Ease.OutQuad));
        }

        /// <summary>
        /// 레벨업 케이스 전용 — pending(NEW) 레벨/난이도를 캐시한 뒤 LobbyBtnChange 애니메이션을 시작.
        /// 20프레임(0.333s) 시점에 <see cref="OnLobbyBtnChangeFrameEvent"/> 가 호출되어
        /// UpdatePlayButton 으로 NEW 표기 교체 + onFrameEvent 콜백(부수 UI 갱신) 트리거.
        /// Animator 미할당 시에도 코루틴 폴백으로 동일 시점 트리거 보장.
        /// non-levelup 케이스(first-launch / 레벨 변화 없는 로비 복귀)에서는 절대 호출되지 않는다.
        /// </summary>
        public void PlayLobbyBtnChangeAnim(int pendingLevelId, DifficultyPurpose pendingDifficulty, System.Action onFrameEvent = null)
        {
            // 사용자 피드백 가드: 동일 레벨/난이도 재호출은 의미 없으므로 silent no-op.
            // _hasPendingChange=false 인 시점(이전 Change 가 이미 완료) + 동일 파라미터일 때만 차단해
            // 첫 호출(_pendingLevelId 디폴트 0)은 정상 통과시킨다.
            if (_pendingLevelId == pendingLevelId && _pendingDifficulty == pendingDifficulty && !_hasPendingChange)
            {
                EnsureLobbyBtnIdle();
                return;
            }

            _pendingLevelId = pendingLevelId;
            _pendingDifficulty = pendingDifficulty;
            _onChangeAnimFrameEvent = onFrameEvent;
            _hasPendingChange = true;

            if (_animPlayBtn != null && _animPlayBtn.runtimeAnimatorController != null)
            {
                _animPlayBtn.Play(LOBBY_BTN_CHANGE_ANIM_NAME, 0, 0f);
            }

            if (_changeAnimCoroutine != null) StopCoroutine(_changeAnimCoroutine);
            _changeAnimCoroutine = StartCoroutine(InvokeLobbyBtnChangeFrameCoroutine());
        }

        private System.Collections.IEnumerator InvokeLobbyBtnChangeFrameCoroutine()
        {
            yield return new WaitForSecondsRealtime(LOBBY_BTN_CHANGE_FRAME_TIME);
            _changeAnimCoroutine = null;
            OnLobbyBtnChangeFrameEvent();
        }

        /// <summary>
        /// LobbyBtnChange 애니메이션 20프레임 시점 AnimationEvent 콜백 — 펜딩 NEW 정보로
        /// PlayButton 표기 + 부수 UI 일괄 교체. AnimationEvent(.anim) 와 코루틴 폴백 양쪽에서
        /// 호출 가능 — 가드로 1회만 처리한다.
        /// </summary>
        public void OnLobbyBtnChangeFrameEvent()
        {
            if (!_hasPendingChange) return;
            _hasPendingChange = false;

            UpdatePlayButton(_pendingLevelId, _pendingDifficulty);

            var cb = _onChangeAnimFrameEvent;
            _onChangeAnimFrameEvent = null;
            cb?.Invoke();

            ScheduleReturnToIdleAfterChange();
        }

        /// <summary>
        /// PlayBtn Animator 를 Idle(LobbyBtn) 상태로 강제 고정.
        /// AnimatorController 의 default state 가 LobbyBtnChange 로 잡혀있어
        /// 프리팹 enable 시 의도치 않게 변경 애니메이션이 자동 재생되는 것을 차단.
        /// 최초 진입 / 레벨업 없는 로비 복귀 / Change 애니 종료 후 모두 호출.
        /// </summary>
        public void EnsureLobbyBtnIdle()
        {
            if (_animPlayBtn == null || _animPlayBtn.runtimeAnimatorController == null) return;
            // normalizedTime=0f, layer=0 으로 명시 — Update 전에 Play 호출되면 default state 재생이 prempt 됨.
            _animPlayBtn.Play(LOBBY_BTN_IDLE_ANIM_NAME, 0, 0f);
            _animPlayBtn.Update(0f); // 즉시 evaluate — 첫 프레임 LobbyBtnChange 키프레임이 한 프레임 비치는 flicker 차단.
        }

        /// <summary>
        /// Change 애니 재생 완료 시점 이후 약간의 지연을 두고 idle 로 복귀.
        /// AnimatorController 에 Change → LobbyBtn 자동 전이가 없을 때를 대비한 안전망.
        /// </summary>
        private void ScheduleReturnToIdleAfterChange()
        {
            DOVirtual.DelayedCall(LOBBY_BTN_RETURN_TO_IDLE_DELAY, EnsureLobbyBtnIdle, ignoreTimeScale: true);
        }

        #endregion

        #region Public Methods — Level Boxes

        /// <summary>
        /// Creates LobbyRailBox instances.
        /// Current stage is at the BOTTOM, older (higher) stages stack above.
        /// </summary>
        public void SetupLevelBoxes(int currentLevel, int highestCompleted)
        {
            if (_levelBoxContainer == null || _lobbyRailBoxPrefab == null) return;

            ClearLevelBoxes();

            _railBoxes = new LobbyRailBox[_visibleBoxCount];
            for (int i = 0; i < _visibleBoxCount; i++)
            {
                // Bottom = currentLevel, going UP = currentLevel+1, +2, ...
                int levelId = currentLevel + i;
                var go = Instantiate(_lobbyRailBoxPrefab, _levelBoxContainer);
                // [2026-05-13] 레벨 박스 버튼 더블 클릭 가드 (멱등).
                UIButtonClickGuard.AttachToHierarchy(go);
                var box = go.GetComponent<LobbyRailBox>();
                if (box != null)
                {
                    bool isActive = (levelId == currentLevel);
                    bool isCompleted = (levelId <= highestCompleted);
                    bool isLocked = (levelId > highestCompleted + 1);

                    DifficultyPurpose diff = DifficultyPurpose.Normal;
                    if (LevelManager.HasInstance)
                        diff = LevelManager.Instance.GetLevelDifficulty(levelId);

                    box.Setup(levelId, isActive, isCompleted, isLocked, diff);
                }
                _railBoxes[i] = box;

                // Reverse sibling order: first spawned (current) goes to bottom
                // Last spawned (highest future) stays on top
                // SetAsFirstSibling makes each new one push above previous
                go.transform.SetAsFirstSibling();
            }
        }

        /// <summary>현재 레벨(active) RailBox 반환.</summary>
        public LobbyRailBox GetActiveRailBox()
        {
            if (_railBoxes == null) return null;
            for (int i = 0; i < _railBoxes.Length; i++)
                if (_railBoxes[i] != null && _railBoxes[i].IsActive) return _railBoxes[i];
            return null;
        }

        public void ClearLevelBoxes()
        {
            if (_railBoxes != null)
            {
                for (int i = 0; i < _railBoxes.Length; i++)
                {
                    if (_railBoxes[i] != null)
                        Destroy(_railBoxes[i].gameObject);
                }
                _railBoxes = null;
            }

            if (_levelBoxContainer != null)
            {
                for (int i = _levelBoxContainer.childCount - 1; i >= 0; i--)
                    Destroy(_levelBoxContainer.GetChild(i).gameObject);
            }
        }

        #endregion

        #region Page Navigation

        /// <summary>
        /// Navigates to the specified page with horizontal swipe animation.
        /// 0=Shop, 1=Home(Lobby), 2=Setting.
        /// </summary>
        public void GoToPage(int pageIndex)
        {
            pageIndex = Mathf.Clamp(pageIndex, 0, 2);
            _isDragging = false; // 탭 버튼 클릭 시 드래그 상태 리셋
            if (pageIndex == _currentPageIndex) return;

            // [2026-05-13] 진행 중인 page tween 동안 다른 탭 클릭 무시 — 첫 클릭 연출이 자연스럽게 끝나도록.
            // 드래그 swipe (HandleSwipeDrag → AnimateToPage 직접 호출) 는 그대로 통과 (user explicit input).
            if (_pageTween != null && _pageTween.IsActive() && !_pageTween.IsComplete()) return;

            _currentPageIndex = pageIndex;
            if (pageIndex == 1)
            {
                PlayRailEnterAnimation();
                PlayLevelObjectEnterAnimation();
            }
            AnimateToPage(pageIndex);
            UpdateNavState(pageIndex);
            if (pageIndex == 0 && _uiShop != null) _uiShop.ResetView();
        }

        /// <summary>
        /// Lobby scene entry must always expose the main/home page, even when UIManager
        /// reuses an inactive UILobby left under the persistent canvas after InGame.
        /// </summary>
        public void ShowMainPanelImmediate()
        {
            // ROLLBACK_LOBBY_RETURN_FORCE_MAIN_PANEL:
            // GameManager.CloseUIAll clears UIManager's list but does not destroy the
            // inactive UILobby under the persistent canvas. Reopening that instance can
            // preserve the previous Shop/Setting page, so LobbyController forces Home.
            _isDragging = false;
            _pageTween?.Kill();
            SetShopInnerScrollEnabled(true);
            SetPageImmediate(1);
            _lastArrivedPageIndex = 1;
        }

        private void SetPageImmediate(int pageIndex)
        {
            _currentPageIndex = pageIndex;
            if (_pageContainer != null)
            {
                float targetX = -pageIndex * _pageWidth;
                _pageContainer.anchoredPosition = new Vector2(targetX, _pageContainer.anchoredPosition.y);
            }
            UpdateNavStateImmediate(pageIndex);
        }

        #region Swipe Drag

        // 아래방향 드래그가 이 픽셀 이상이면 Rail 풀다운 연출 발동.
        private const float RAIL_PULL_DOWN_TRIGGER_PX = 60f;

        private float _dragStartScreenY;
        private bool _dragDirectionLocked;
        private bool _dragIsHorizontal;

        private void HandleSwipeDrag()
        {
            if (_pageContainer == null) return;

            // [2026-06-12] 팝업(WinningStreak 등)이 떠 있는 동안 로비 페이지 스와이프 차단 —
            // 팝업 뒤로 터치가 통과해 좌우 슬라이드로 샵 이동되던 문제. 드래그 진행 중이었다면 상태 리셋.
            // PopupWinningStreakReward(WS 0단계)는 UIBase 가 아니라 별도 정적 플래그로 게이트.
            if (PopupWinningStreakReward.IsShowing
                || (UIManager.HasInstance && UIManager.Instance.GetTopmostBackConsumingUI() != null))
            {
                _isDragging = false;
                _dragDirectionLocked = false;
                _dragIsHorizontal = false;
                return;
            }

            bool touching = false;
            float screenX = 0f;
            float screenY = 0f;

            if (Touchscreen.current != null && Touchscreen.current.primaryTouch.press.isPressed)
            {
                touching = true;
                var pos = Touchscreen.current.primaryTouch.position.ReadValue();
                screenX = pos.x;
                screenY = pos.y;
            }
            else if (Mouse.current != null && Mouse.current.leftButton.isPressed)
            {
                touching = true;
                var pos = Mouse.current.position.ReadValue();
                screenX = pos.x;
                screenY = pos.y;
            }

            if (touching && !_isDragging)
            {
                _isDragging = true;
                _dragStartScreenX = screenX;
                _dragStartScreenY = screenY;
                _dragLastScreenX = screenX;
                _dragLastScreenY = screenY;
                _dragStartPageX = _pageContainer.anchoredPosition.x;
                _dragDirectionLocked = false;
                _dragIsHorizontal = false;
                // [2026-05-13] 터치 press 시점 Kill 제거 — 단순 탭(=탭 버튼 클릭)도 여기로 진입하므로
                // 진행 중 page tween 이 매번 끊김. 확정된 가로 드래그 시점에서만 Kill (아래 분기).
            }
            else if (touching && _isDragging)
            {
                _dragLastScreenX = screenX;
                _dragLastScreenY = screenY;

                float deltaX = Mathf.Abs(screenX - _dragStartScreenX);
                float deltaY = Mathf.Abs(screenY - _dragStartScreenY);

                // 방향 판정: 첫 15px 이동 시 가로/세로 결정
                if (!_dragDirectionLocked && (deltaX > 15f || deltaY > 15f))
                {
                    _dragDirectionLocked = true;
                    _dragIsHorizontal = deltaX > deltaY;

                    // 가로 페이지 swipe로 결정된 순간, Shop 내부 세로 ScrollRect를 잠가
                    // 드래그 중 세로 스크롤이 동시에 일어나는 충돌을 방지.
                    if (_dragIsHorizontal)
                    {
                        // [2026-05-13] 가로 드래그 확정 시점에서만 진행 중 tween Kill — 드래그가 손가락 위치를 즉시 따라가야 하므로.
                        _pageTween?.Kill();
                        SetShopInnerScrollEnabled(false);
                    }
                }

                if (!_dragDirectionLocked || !_dragIsHorizontal) return;

                float deltaScreen = screenX - _dragStartScreenX;
                float scale = _pageWidth / (Screen.width > 0 ? Screen.width : 1242f);
                float newX = _dragStartPageX + deltaScreen * scale;
                newX = Mathf.Clamp(newX, -2f * _pageWidth, 0f);
                _pageContainer.anchoredPosition = new Vector2(newX, _pageContainer.anchoredPosition.y);
            }
            else if (!touching && _isDragging)
            {
                _isDragging = false;

                // 드래그 종료 — 가로 잠금 동안 비활성화했던 Shop 내부 ScrollRect를 항상 복구.
                SetShopInnerScrollEnabled(true);

                if (!_dragIsHorizontal)
                {
                    // 위→아래 방향 드래그 시 Unity 좌표계에서 deltaY 는 음수 — 일정 거리 이상 끌면 Rail 풀다운 연출.
                    float deltaY = _dragLastScreenY - _dragStartScreenY;
                    if (deltaY < -RAIL_PULL_DOWN_TRIGGER_PX)
                    {
                        PlayRailPullDownAnimation();
                    }

                    // [2026-05-13] 단순 탭/세로 드래그 release 에서 AnimateToPage 호출 제거 —
                    // 탭 버튼 클릭으로 진행 중인 page tween 을 Kill+재시작 시키던 회귀 fix.
                    // page 위치는 이미 정확하거나 (탭/세로) tween 이 자연스럽게 끝까지 도달.
                    return;
                }

                float dragDelta = _dragLastScreenX - _dragStartScreenX;
                float threshold = Screen.width * SWIPE_THRESHOLD_RATIO;

                if (Mathf.Abs(dragDelta) < 15f)
                {
                    AnimateToPage(_currentPageIndex);
                    return;
                }

                int targetPage = _currentPageIndex;
                if (dragDelta > threshold && _currentPageIndex > 0)
                    targetPage = _currentPageIndex - 1;
                else if (dragDelta < -threshold && _currentPageIndex < 2)
                    targetPage = _currentPageIndex + 1;

                int prev = _currentPageIndex;
                _currentPageIndex = targetPage;
                AnimateToPage(targetPage);
                UpdateNavState(targetPage);
                if (targetPage == 0 && prev != 0 && _uiShop != null) _uiShop.ResetView();
                if (targetPage == 1 && prev != 1)
                {
                    PlayRailEnterAnimation();
                    PlayLevelObjectEnterAnimation();
                }
            }
        }

        /// <summary>
        /// _pageShop 하위의 모든 ScrollRect를 일괄 enable/disable.
        /// 가로 페이지 swipe가 lock되는 순간 세로 스크롤이 동시에 일어나지 않도록
        /// false로 잠갔다가 드래그 종료 시 true로 복구한다.
        /// </summary>
        private void SetShopInnerScrollEnabled(bool enabled)
        {
            if (_pageShop == null) return;

            var scrolls = _pageShop.GetComponentsInChildren<ScrollRect>(true);
            for (int i = 0; i < scrolls.Length; i++)
            {
                var sr = scrolls[i];
                if (sr == null) continue;

                if (!enabled)
                {
                    // 진행 중이던 관성/드래그를 즉시 멈춰 잔여 이동을 방지.
                    sr.StopMovement();
                    sr.enabled = false;
                }
                else
                {
                    sr.enabled = true;
                }
            }
        }

        #endregion

        private void AnimateToPage(int pageIndex)
        {
            if (_pageContainer == null) return;

            _pageTween?.Kill();

            float targetX = -pageIndex * _pageWidth;
            _pageTween = _pageContainer.DOAnchorPosX(targetX, PAGE_SWIPE_DURATION)
                .SetEase(Ease.OutCubic)
                .SetUpdate(true)
                .OnComplete(() => OnPageArrived(pageIndex));
        }

        /// <summary>
        /// Page slide tween 완료 시 호출. Shop 탭 도착 시 layout 재계산 — 다른 탭 다녀온 후 상단 영역
        /// 넓어지는 이슈 fix (사용자 보고).
        /// 같은 Shop 탭에서 vertical drag/short tap 으로 재도착할 때 스크롤이 상단으로 점프하던 버그를
        /// 막기 위해, 다른 탭→Shop 진입(fresh arrival)인 경우에만 ResetView 호출.
        /// </summary>
        private void OnPageArrived(int pageIndex)
        {
            bool isFreshShopArrival = pageIndex == 0 && _lastArrivedPageIndex != 0;

            if (isFreshShopArrival && _pageShop != null)
            {
                var shop = _pageShop.GetComponent<UIShop>();
                if (shop != null) shop.ResetView();
            }

            _lastArrivedPageIndex = pageIndex;
        }

        #endregion

        #region Bottom Nav State

        private void CacheNavTextBaseY()
        {
            if (_baseYCached) return;
            _baseYShop = _txtShop != null ? _txtShop.rectTransform.anchoredPosition.y : 0f;
            _baseYHome = _txtHome != null ? _txtHome.rectTransform.anchoredPosition.y : 0f;
            _baseYSetting = _txtSetting != null ? _txtSetting.rectTransform.anchoredPosition.y : 0f;
            _iconBaseYShop = _iconShop != null ? _iconShop.rectTransform.anchoredPosition.y : 0f;
            _iconBaseYHome = _iconHome != null ? _iconHome.rectTransform.anchoredPosition.y : 0f;
            _iconBaseYSetting = _iconSetting != null ? _iconSetting.rectTransform.anchoredPosition.y : 0f;
            _baseYCached = true;
        }

        private void UpdateNavState(int activeIndex)
        {
            // ImageOnClick
            if (_imgOnClickShop != null) _imgOnClickShop.SetActive(activeIndex == 0);
            if (_imgOnClickHome != null) _imgOnClickHome.SetActive(activeIndex == 1);
            if (_imgOnClickSetting != null) _imgOnClickSetting.SetActive(activeIndex == 2);

            bool homeActive = activeIndex == 1;
            if (_imgLineShop != null) _imgLineShop.SetActive(!homeActive);
            if (_imgLineSetting != null) _imgLineSetting.SetActive(!homeActive);

            // Icon scale + Y position
            SetNavIcon(_iconShop, activeIndex == 0, true, _iconBaseYShop);
            SetNavIcon(_iconHome, activeIndex == 1, true, _iconBaseYHome);
            SetNavIcon(_iconSetting, activeIndex == 2, true, _iconBaseYSetting);

            // Text: animate (active = fade in + slide up, inactive = fade out + hide)
            AnimateNavText(_txtShop, activeIndex == 0, _baseYShop);
            AnimateNavText(_txtHome, activeIndex == 1, _baseYHome);
            AnimateNavText(_txtSetting, activeIndex == 2, _baseYSetting);
        }

        private void UpdateNavStateImmediate(int activeIndex)
        {
            if (_imgOnClickShop != null) _imgOnClickShop.SetActive(activeIndex == 0);
            if (_imgOnClickHome != null) _imgOnClickHome.SetActive(activeIndex == 1);
            if (_imgOnClickSetting != null) _imgOnClickSetting.SetActive(activeIndex == 2);

            bool homeActive = activeIndex == 1;
            if (_imgLineShop != null) _imgLineShop.SetActive(!homeActive);
            if (_imgLineSetting != null) _imgLineSetting.SetActive(!homeActive);

            SetNavIcon(_iconShop, activeIndex == 0, false, _iconBaseYShop);
            SetNavIcon(_iconHome, activeIndex == 1, false, _iconBaseYHome);
            SetNavIcon(_iconSetting, activeIndex == 2, false, _iconBaseYSetting);

            SetNavTextImmediate(_txtShop, activeIndex == 0);
            SetNavTextImmediate(_txtHome, activeIndex == 1);
            SetNavTextImmediate(_txtSetting, activeIndex == 2);
        }

        private void SetNavIcon(Image icon, bool active, bool animate, float baseY)
        {
            if (icon == null) return;

            float targetScale = active ? ICON_SCALE_ACTIVE : ICON_SCALE_INACTIVE;
            float targetY = active ? baseY + ICON_Y_OFFSET : -45f;
            var rt = icon.rectTransform;

            if (animate)
            {
                rt.DOKill();
                rt.DOScale(targetScale, ICON_SCALE_DURATION).SetEase(Ease.OutCubic);
                rt.DOAnchorPosY(targetY, ICON_SCALE_DURATION).SetEase(Ease.OutCubic);
            }
            else
            {
                rt.localScale = Vector3.one * targetScale;
                rt.anchoredPosition = new Vector2(rt.anchoredPosition.x, targetY);
            }
        }

        private void AnimateNavText(TMP_Text txt, bool active, float baseY)
        {
            if (txt == null) return;

            txt.DOKill();

            if (active)
            {
                txt.gameObject.SetActive(true);
                txt.alpha = 0f;
                var rt = txt.rectTransform;
                rt.anchoredPosition = new Vector2(rt.anchoredPosition.x, baseY - 8f);

                DOTween.Sequence()
                    .Join(DOTween.To(() => txt.alpha, v => txt.alpha = v, 1f, NAV_TEXT_ANIM_DURATION))
                    .Join(rt.DOAnchorPosY(baseY, NAV_TEXT_ANIM_DURATION).SetEase(Ease.OutCubic));
            }
            else
            {
                if (!txt.gameObject.activeSelf) return;

                DOTween.Sequence()
                    .Append(DOTween.To(() => txt.alpha, v => txt.alpha = v, 0f, NAV_TEXT_ANIM_DURATION * 0.5f))
                    .OnComplete(() => txt.gameObject.SetActive(false));
            }
        }

        private void SetNavTextImmediate(TMP_Text txt, bool active)
        {
            if (txt == null) return;
            txt.gameObject.SetActive(active);
            if (active) txt.alpha = 1f;
        }

        #endregion

        #region Legacy Compatibility

        public Button PlayButton => _btnPlay;
        public TMP_Text PlayButtonLabel => _txtGold;
        public Button CoinButton => _btnGoldPlus;
        public TMP_Text CoinDisplayText => _txtGold;
        public Button SettingsButton => _btnSetting;

        public void SetStageText(int stage) { }

        public void SetCoinText(int coins)
        {
            SetGoldText(coins);
        }

        #endregion
    }
}
