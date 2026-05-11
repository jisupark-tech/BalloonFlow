using UnityEngine;
using UnityEngine.UI;
using UnityEngine.InputSystem;
using DG.Tweening;
using TMPro;

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
        #region Constants

        private static readonly Color COLOR_NAV_ACTIVE   = Color.white;
        private static readonly Color COLOR_NAV_INACTIVE  = new Color(0x80 / 255f, 0x80 / 255f, 0x80 / 255f); // #808080

        private const float PAGE_SWIPE_DURATION = 0.3f;
        private const float NAV_TEXT_ANIM_DURATION = 0.2f;
        private const float ICON_SCALE_ACTIVE = 1.1f;
        private const float ICON_SCALE_INACTIVE = 0.9f;
        private const float ICON_Y_OFFSET = 25f; // 활성 +25, 비활성 -25
        private const float ICON_SCALE_DURATION = 0.2f;

        // Rail 슬라이드 인 연출 파라미터.
        // Top/Bottom Rail 모두 화면 위쪽 +120 에서 시작해 OutCubic 으로 제자리(0) 로 내려오는
        // 일관된 톱-다운 슬라이드인 연출. (사용자 피드백: 두 Rail 이 반대 방향으로 들어오는 것보다
        // 동일 방향으로 내려오는 쪽이 시각적 일체감이 좋음)
        // 부호 의도를 코드 자체로 표현하기 위해 두 상수를 분리해 둠. (값을 바꾸려면 각각 조정)
        private const float RAIL_TOP_ENTER_OFFSET_Y    = +120f;
        private const float RAIL_BOTTOM_ENTER_OFFSET_Y = +120f;
        private const float RAIL_ENTER_DURATION = 0.45f;

        private const float LEVEL_OBJECT_ENTER_START_Y = 1335f;
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

        [Header("[TopBar — LifePanel]")]
        [SerializeField] private TMP_Text _txtLife;
        [SerializeField] private TMP_Text _txtLifeOutline;
        [SerializeField] private Button _btnLifePlus;
        [SerializeField] private Image _imgLifeTimer;
        [SerializeField] private TMP_Text _txtLifeTimer;
        [SerializeField] private TMP_Text _txtLifeTimerOutline;
        [SerializeField] private Button _btnLifeBar;
        [SerializeField] private GameObject _imgInfinite;

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

        [Header("[RightArea — Lobby Page]")]
        [SerializeField] private Button _btnNoAds;
        [SerializeField] private Button _btnProfilePanel;

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

        // Rail 슬라이드 인 트윈 캐시 (중복 호출 시 Kill 후 재시작)
        private Tweener _railTopTween;
        private Tweener _railBottomTween;
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
        public Button BtnGoldPlus => _btnGoldPlus;
        public Button BtnLifePlus => _btnLifePlus;
        public Button BtnLifeBar => _btnLifeBar;
        public Button BtnShop => _btnShop;
        public Button BtnHome => _btnHome;
        public Button BtnSetting => _btnSetting;
        public Button BtnNoAds => _btnNoAds;
        public Button BtnProfilePanel => _btnProfilePanel;
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

            if (_btnShop != null) _btnShop.onClick.AddListener(() => { PlayTouchSFX(); GoToPage(0); });
            if (_btnHome != null) _btnHome.onClick.AddListener(() => { PlayTouchSFX(); GoToPage(1); });
            if (_btnSetting != null) _btnSetting.onClick.AddListener(() => { PlayTouchSFX(); GoToPage(2); });
            if (_btnPlay != null) _btnPlay.onClick.AddListener(PlayTouchSFX);

            AutoConfigureShopScroll();

            // Start on Home(Lobby) page
            SetPageImmediate(1);

            // 최초 진입 시 Rail 슬라이드 인 연출 1회 재생
            PlayRailEnterAnimation();
            PlayLevelObjectEnterAnimation();

            // AnimatorController(LobbyPlayBtn.controller)의 default state 가 LobbyBtnChange 로 잡혀
            // 프리팹 enable 직후 의도치 않게 변경 애니가 자동 재생되는 문제 차단.
            // 첫 프레임 전에 idle 로 강제 고정한다.
            EnsureLobbyBtnIdle();
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

        // 인게임 종료 후 로비 복귀 시 Rail Enter 완료 후 LevelObject 가 1335→1145 로 OutCubic 슬라이드 다운 (자연스러운 진입 연출)
        public void PlayLevelObjectEnterAnimation()
        {
            _levelObjectEnterTween?.Kill();
            if (_levelBoxContainer == null) return;

            var p = _levelBoxContainer.anchoredPosition;
            _levelBoxContainer.anchoredPosition = new Vector2(p.x, LEVEL_OBJECT_ENTER_START_Y);
            _levelObjectEnterTween = _levelBoxContainer.DOAnchorPosY(LEVEL_OBJECT_ENTER_END_Y, LEVEL_OBJECT_ENTER_DURATION)
                .SetDelay(RAIL_ENTER_DURATION)
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

        private new void OnDestroy()
        {
            base.OnDestroy();
            _pageTween?.Kill();
            _goldTween?.Kill();
            _railTopTween?.Kill();
            _railBottomTween?.Kill();
            _levelObjectEnterTween?.Kill();
        }

        private void Update()
        {
            HandleSwipeDrag();
        }

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
        /// _txtGold 미할당 시 화면 우상단 추정값 fallback.
        /// </summary>
        public Vector2 GetGoldPanelScreenPos()
        {
            if (_txtGold == null) return new Vector2(Screen.width * 0.85f, Screen.height * 0.92f);
            var rt = _txtGold.rectTransform;
            var canvas = rt.GetComponentInParent<Canvas>();
            Camera cam = (canvas != null && canvas.renderMode == RenderMode.ScreenSpaceCamera)
                ? canvas.worldCamera : null;
            return RectTransformUtility.WorldToScreenPoint(cam, rt.position);
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

        public void SetInfiniteImageVisible(bool visible)
        {
            if (_imgInfinite != null) _imgInfinite.SetActive(visible);
        }

        #endregion

        #region Public Methods — Play Button

        public void UpdatePlayButton(int levelId, DifficultyPurpose difficulty)
        {
            string levelStr = "Level " + levelId;
            if (_txtPlay != null) _txtPlay.text = levelStr;
            if (_txtPlayOutline != null) _txtPlayOutline.text = levelStr;

            switch (difficulty)
            {
                case DifficultyPurpose.Hard:
                    if (_imgPlayButton != null && _sprBtnPurple != null) _imgPlayButton.sprite = _sprBtnPurple;
                    if (_txtPlayLevel != null) { _txtPlayLevel.gameObject.SetActive(true); _txtPlayLevel.text = "Hard Level"; }
                    if (_txtPlayLevelOutline != null) { _txtPlayLevelOutline.gameObject.SetActive(true); _txtPlayLevelOutline.text = "Hard Level"; }
                    break;

                case DifficultyPurpose.SuperHard:
                    if (_imgPlayButton != null && _sprBtnRed != null) _imgPlayButton.sprite = _sprBtnRed;
                    if (_txtPlayLevel != null) { _txtPlayLevel.gameObject.SetActive(true); _txtPlayLevel.text = "Super Hard"; }
                    if (_txtPlayLevelOutline != null) { _txtPlayLevelOutline.gameObject.SetActive(true); _txtPlayLevelOutline.text = "Super Hard"; }
                    break;

                default: // Normal, Tutorial, Rest, Intro
                    if (_imgPlayButton != null && _sprBtnGreen != null) _imgPlayButton.sprite = _sprBtnGreen;
                    if (_txtPlayLevel != null) _txtPlayLevel.gameObject.SetActive(false);
                    if (_txtPlayLevelOutline != null) _txtPlayLevelOutline.gameObject.SetActive(false);
                    break;
            }

            // Play 텍스트 아웃라인 머티리얼
            Material playOutlineMat = difficulty switch
            {
                DifficultyPurpose.Hard      => _matPlayOutlineHard,
                DifficultyPurpose.SuperHard => _matPlayOutlineSuperHard,
                _                           => _matPlayOutlineNormal
            };
            if (playOutlineMat != null && _txtPlayOutline != null) _txtPlayOutline.fontMaterial = playOutlineMat;

            // PlayLevel 텍스트 아웃라인 머티리얼
            Material playLevelOutlineMat = difficulty switch
            {
                DifficultyPurpose.Hard      => _matPlayLevelOutlineHard,
                DifficultyPurpose.SuperHard => _matPlayLevelOutlineSuperHard,
                _                           => _matPlayLevelOutlineNormal
            };
            if (playLevelOutlineMat != null && _txtPlayLevelOutline != null) _txtPlayLevelOutline.fontMaterial = playLevelOutlineMat;

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

        private float _dragStartScreenY;
        private bool _dragDirectionLocked;
        private bool _dragIsHorizontal;

        private void HandleSwipeDrag()
        {
            if (_pageContainer == null) return;

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
                _dragStartPageX = _pageContainer.anchoredPosition.x;
                _dragDirectionLocked = false;
                _dragIsHorizontal = false;
                _pageTween?.Kill();
            }
            else if (touching && _isDragging)
            {
                _dragLastScreenX = screenX;

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
                    // 세로 드래그였으면 페이지 이동 없이 복귀
                    AnimateToPage(_currentPageIndex);
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

        private static void PlayTouchSFX()
        {
            if (AudioManager.HasInstance) AudioManager.Instance.PlayPopupTouch();
        }

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
