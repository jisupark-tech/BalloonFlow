using UnityEngine;
using UnityEngine.UI;
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

        #endregion

        #region Fields

        private int _currentPageIndex = 1; // 0=Shop, 1=Home(Lobby), 2=Setting
        private Tweener _pageTween;
        private LobbyRailBox[] _railBoxes;
        private RectTransform _pageContainer;
        private float _pageWidth;

        // Nav text base Y positions (cached before animation)
        private float _baseYShop, _baseYHome, _baseYSetting;
        // Nav icon base Y positions
        private float _iconBaseYShop, _iconBaseYHome, _iconBaseYSetting;
        private bool _baseYCached;

        #endregion

        #region Properties

        public Button BtnPlay => _btnPlay;
        public Button BtnGoldPlus => _btnGoldPlus;
        public Button BtnLifePlus => _btnLifePlus;
        public Button BtnLifeBar => _btnLifeBar;
        public Button BtnShop => _btnShop;
        public Button BtnHome => _btnHome;
        public Button BtnSetting => _btnSetting;
        public int CurrentPageIndex => _currentPageIndex;

        #endregion

        #region Lifecycle

        protected override void Awake()
        {
            base.Awake();

            BuildPageContainer();
            CacheNavTextBaseY();

            if (_btnShop != null) _btnShop.onClick.AddListener(() => { PlayTouchSFX(); GoToPage(0); });
            if (_btnHome != null) _btnHome.onClick.AddListener(() => { PlayTouchSFX(); GoToPage(1); });
            if (_btnSetting != null) _btnSetting.onClick.AddListener(() => { PlayTouchSFX(); GoToPage(2); });
            if (_btnPlay != null) _btnPlay.onClick.AddListener(PlayTouchSFX);

            // Start on Home(Lobby) page
            SetPageImmediate(1);
        }

        private new void OnDestroy()
        {
            base.OnDestroy();
            _pageTween?.Kill();
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
            var selfRT = GetComponent<RectTransform>();
            _pageWidth = selfRT != null ? selfRT.rect.width : 1242f;
            if (_pageWidth <= 0f) _pageWidth = 1242f;

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

        private void ReparentPage(RectTransform page, int index)
        {
            if (page == null) return;

            page.SetParent(_pageContainer, false);

            // Each page fills one _pageWidth slot
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

        public void SetGoldText(int coins)
        {
            string formatted = coins.ToString("N0");
            if (_txtGold != null) _txtGold.text = formatted;
            if (_txtGoldOutline != null) _txtGoldOutline.text = formatted;
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
        }

        public void PlayButtonPressAnim()
        {
            if (_btnPlay == null) return;
            var rt = _btnPlay.transform;
            rt.DOKill();
            rt.DOScale(0.95f, 0.08f).SetEase(Ease.InQuad)
              .OnComplete(() => rt.DOScale(1f, 0.08f).SetEase(Ease.OutQuad));
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
            if (pageIndex == _currentPageIndex) return;

            _currentPageIndex = pageIndex;
            AnimateToPage(pageIndex);
            UpdateNavState(pageIndex);
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

        private void AnimateToPage(int pageIndex)
        {
            if (_pageContainer == null) return;

            _pageTween?.Kill();

            float targetX = -pageIndex * _pageWidth;
            _pageTween = _pageContainer.DOAnchorPosX(targetX, PAGE_SWIPE_DURATION)
                .SetEase(Ease.OutCubic)
                .SetUpdate(true);
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
