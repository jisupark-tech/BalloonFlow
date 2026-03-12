using System.Collections;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;
using Touchscreen = UnityEngine.InputSystem.Touchscreen;

namespace BalloonFlow
{
    /// <summary>
    /// Game flow controller — manages page transitions, button wiring,
    /// and overall game lifecycle from title screen to gameplay to results.
    ///
    /// Three-tier reference resolution:
    ///   1. [SerializeField] wired by SceneBuilder (preferred)
    ///   2. Find existing scene objects by name (fallback if wiring failed)
    ///   3. Create from scratch at runtime (last resort)
    /// </summary>
    /// <remarks>
    /// Layer: Core | Genre: Puzzle | Role: Controller | Phase: 0
    /// </remarks>
    public class GameBootstrap : MonoBehaviour
    {
        #region Serialized Fields

        [Header("Pages (CanvasGroup)")]
        [SerializeField] private CanvasGroup _titlePage;
        [SerializeField] private CanvasGroup _mainPage;
        [SerializeField] private CanvasGroup _gamePage;
        [SerializeField] private CanvasGroup _resultPage;
        [SerializeField] private CanvasGroup _shopPage;

        [Header("Title Page")]
        [SerializeField] private float _titleDuration = 2.5f;

        [Header("Main Page Buttons")]
        [SerializeField] private Button _playButton;
        [SerializeField] private Button _coinButton;
        [SerializeField] private Button _gemButton;

        [Header("Main Page - Currency Display")]
        [SerializeField] private Text _coinDisplayText;
        [SerializeField] private Text _gemDisplayText;
        [SerializeField] private Text _playButtonLabel;

        [Header("Result Page")]
        [SerializeField] private Text _resultTitleText;
        [SerializeField] private Text _resultScoreText;
        [SerializeField] private Button _nextButton;
        [SerializeField] private Button _retryButton;
        [SerializeField] private Button _homeButton;

        [Header("Shop Page")]
        [SerializeField] private Button _shopCloseButton;
        [SerializeField] private Transform _shopContentRoot;

        #endregion

        #region Constants

        private const int REF_WIDTH = 1080;
        private const int REF_HEIGHT = 1920;

        #endregion

        #region Colors

        private static readonly Color BG_TITLE   = new Color(0.08f, 0.08f, 0.16f, 1f);
        private static readonly Color BG_MAIN    = new Color(0.06f, 0.10f, 0.18f, 1f);
        private static readonly Color BG_GAME    = new Color(0.04f, 0.04f, 0.08f, 1f);
        private static readonly Color BG_OVERLAY = new Color(0f, 0f, 0f, 0.85f);
        private static readonly Color COL_PLAY   = new Color(0.15f, 0.75f, 0.3f, 1f);
        private static readonly Color COL_RETRY  = new Color(0.9f, 0.55f, 0.1f, 1f);
        private static readonly Color COL_NEXT   = new Color(0.2f, 0.6f, 0.95f, 1f);
        private static readonly Color COL_HOME   = new Color(0.5f, 0.5f, 0.55f, 1f);
        private static readonly Color COL_HUD    = new Color(0f, 0f, 0f, 0.5f);
        private static readonly Color COL_SHOP_BG  = new Color(0.06f, 0.08f, 0.16f, 1f);
        private static readonly Color COL_SHOP_ITEM = new Color(0.12f, 0.14f, 0.24f, 1f);
        private static readonly Color COL_BUY      = new Color(0.2f, 0.65f, 0.9f, 1f);
        private static readonly Color COL_COIN_BTN = new Color(0.85f, 0.75f, 0.1f, 1f);
        private static readonly Color COL_GEM_BTN  = new Color(0.3f, 0.6f, 0.95f, 1f);

        #endregion

        #region Fields

        private bool _titleTapped;
        private bool _pendingResultIsWin;
        private Font _font;

        #endregion

        #region Lifecycle

        private void Awake()
        {
            _font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            if (_font == null) _font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            if (_font == null) _font = Font.CreateDynamicFontFromOSFont("Arial", 14);
            if (_font == null) Debug.LogError("[GameBootstrap] No font found! UI text will be invisible.");
            else Debug.Log($"[GameBootstrap] Font loaded: {_font.name}");

            EnsureEventSystem();

            // Diagnostic: log what SceneBuilder wired
            Debug.Log($"[GameBootstrap] Awake — Tier 1 check: title={_titlePage != null}, main={_mainPage != null}, " +
                      $"game={_gamePage != null}, result={_resultPage != null}, play={_playButton != null}");

            // Three-tier resolution: SerializeField → Find by name → Create
            bool needsResolve = _titlePage == null || _mainPage == null || _gamePage == null
                             || _resultPage == null || _playButton == null;

            // Also check if pages exist but have no children (SceneBuilder created empty shells)
            if (!needsResolve && _mainPage != null && _mainPage.transform.childCount == 0)
            {
                Debug.LogWarning("[GameBootstrap] MainPage exists but has 0 children — forcing UI rebuild.");
                needsResolve = true;
            }

            if (needsResolve)
            {
                Debug.Log("[GameBootstrap] Missing references or empty pages — entering ResolveOrBuildUI.");
                ResolveOrBuildUI();
            }

            // Final verification — force Tier 3 if still broken
            if (_mainPage == null || _playButton == null)
            {
                Debug.LogWarning("[GameBootstrap] Still missing references after resolve. Forcing full UI build.");
                Canvas canvas = FindAnyObjectByType<Canvas>();
                Transform root = canvas != null ? canvas.transform : CreateCanvasGO().transform;
                // Destroy any partial pages
                if (_titlePage != null) { Destroy(_titlePage.gameObject); _titlePage = null; }
                if (_mainPage != null) { Destroy(_mainPage.gameObject); _mainPage = null; }
                if (_gamePage != null) { Destroy(_gamePage.gameObject); _gamePage = null; }
                if (_resultPage != null) { Destroy(_resultPage.gameObject); _resultPage = null; }
                if (_shopPage != null) { Destroy(_shopPage.gameObject); _shopPage = null; }
                BuildFullUI(root);
            }

            Debug.Log($"[GameBootstrap] Final state: title={_titlePage != null}, main={_mainPage != null}({(_mainPage != null ? _mainPage.transform.childCount : 0)} children), " +
                      $"game={_gamePage != null}, result={_resultPage != null}, play={_playButton != null}");

            EnsureManagers();
        }

        private void Start()
        {
            HideAllPages();
            ShowPage(_titlePage);
            StartCoroutine(TitleSequence());
            RefreshMainPageInfo();
            Debug.Log("[GameBootstrap] Game started. Title page shown.");
        }

        private void Update()
        {
            if (_titlePage != null && _titlePage.alpha > 0.5f && !_titleTapped)
            {
                bool mousePressed = Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame;
                bool touchPressed = Touchscreen.current != null && Touchscreen.current.primaryTouch.press.wasPressedThisFrame;
                if (mousePressed || touchPressed)
                {
                    _titleTapped = true;
                    StopAllCoroutines();
                    TransitionToMain();
                }
            }
        }

        private void OnEnable()
        {
            EventBus.Subscribe<OnLevelCompleted>(HandleLevelCompleted);
            EventBus.Subscribe<OnLevelFailed>(HandleLevelFailed);
            EventBus.Subscribe<OnCoinChanged>(HandleCoinChanged);
            EventBus.Subscribe<OnGemChanged>(HandleGemChanged);

            if (_playButton != null) _playButton.onClick.AddListener(OnPlayClicked);
            if (_nextButton != null) _nextButton.onClick.AddListener(OnNextClicked);
            if (_retryButton != null) _retryButton.onClick.AddListener(OnRetryClicked);
            if (_homeButton != null) _homeButton.onClick.AddListener(OnHomeClicked);
            if (_coinButton != null) _coinButton.onClick.AddListener(OnShopClicked);
            if (_gemButton != null) _gemButton.onClick.AddListener(OnShopClicked);
            if (_shopCloseButton != null) _shopCloseButton.onClick.AddListener(OnShopCloseClicked);
        }

        private void OnDisable()
        {
            EventBus.Unsubscribe<OnLevelCompleted>(HandleLevelCompleted);
            EventBus.Unsubscribe<OnLevelFailed>(HandleLevelFailed);
            EventBus.Unsubscribe<OnCoinChanged>(HandleCoinChanged);
            EventBus.Unsubscribe<OnGemChanged>(HandleGemChanged);

            if (_playButton != null) _playButton.onClick.RemoveListener(OnPlayClicked);
            if (_nextButton != null) _nextButton.onClick.RemoveListener(OnNextClicked);
            if (_retryButton != null) _retryButton.onClick.RemoveListener(OnRetryClicked);
            if (_homeButton != null) _homeButton.onClick.RemoveListener(OnHomeClicked);
            if (_coinButton != null) _coinButton.onClick.RemoveListener(OnShopClicked);
            if (_gemButton != null) _gemButton.onClick.RemoveListener(OnShopClicked);
            if (_shopCloseButton != null) _shopCloseButton.onClick.RemoveListener(OnShopCloseClicked);
        }

        #endregion

        // ═══════════════════════════════════════════
        // UI RESOLUTION (Tier 2: find by name, Tier 3: create)
        // ═══════════════════════════════════════════

        #region UI Resolution

        private void ResolveOrBuildUI()
        {
            Canvas canvas = FindAnyObjectByType<Canvas>();
            Transform root;

            if (canvas != null)
            {
                root = canvas.transform;
            }
            else
            {
                root = CreateCanvasGO().transform;
            }

            // Tier 2: Try to find SceneBuilder-created pages by name
            if (_titlePage == null) _titlePage = FindCG(root, "TitlePage");
            if (_mainPage == null) _mainPage = FindCG(root, "MainPage");
            if (_gamePage == null) _gamePage = FindCG(root, "GamePage");
            if (_resultPage == null) _resultPage = FindCG(root, "ResultPage");
            if (_shopPage == null) _shopPage = FindCG(root, "ShopPage");

            bool pagesFound = _titlePage != null && _mainPage != null
                           && _gamePage != null && _resultPage != null;

            if (pagesFound)
            {
                // SceneBuilder created the pages — resolve buttons/text by name
                ResolveSubElements();

                // If essential elements are still missing after Tier 2, build the full UI
                if (_playButton == null)
                {
                    Debug.LogWarning("[GameBootstrap] Pages found but essential sub-elements missing. Rebuilding UI.");
                    // Destroy the empty SceneBuilder pages so Tier 3 can create complete ones
                    if (_titlePage != null) Destroy(_titlePage.gameObject);
                    if (_mainPage != null) Destroy(_mainPage.gameObject);
                    if (_gamePage != null) Destroy(_gamePage.gameObject);
                    if (_resultPage != null) Destroy(_resultPage.gameObject);
                    if (_shopPage != null) Destroy(_shopPage.gameObject);
                    _titlePage = null; _mainPage = null; _gamePage = null; _resultPage = null; _shopPage = null;
                    BuildFullUI(root);
                    Debug.Log("[GameBootstrap] UI rebuilt from scratch after incomplete SceneBuilder setup.");
                }
                else
                {
                    Debug.Log("[GameBootstrap] UI resolved from existing scene objects.");
                }
            }
            else
            {
                // Tier 3: Nothing found — build everything from scratch
                BuildFullUI(root);
                Debug.Log("[GameBootstrap] UI built from scratch (runtime fallback).");
            }
        }

        /// <summary>
        /// Finds buttons and text elements inside SceneBuilder-created pages by name.
        /// </summary>
        private void ResolveSubElements()
        {
            // Play button in main page
            if (_playButton == null)
                _playButton = FindBtn(_mainPage.transform, "PlayButton");

            // Currency bar in main page
            Transform currBar = _mainPage.transform.Find("CurrencyBar");
            if (currBar != null)
            {
                if (_coinDisplayText == null) _coinDisplayText = FindTxt(currBar, "CoinText");
                if (_gemDisplayText == null) _gemDisplayText = FindTxt(currBar, "GemText");
            }

            // Result panel elements
            Transform resultPanel = _resultPage.transform.Find("ResultPanel");
            if (resultPanel != null)
            {
                if (_nextButton == null) _nextButton = FindBtn(resultPanel, "NextButton");
                if (_retryButton == null) _retryButton = FindBtn(resultPanel, "RetryButton");
                if (_homeButton == null) _homeButton = FindBtn(resultPanel, "HomeButton");
                if (_resultTitleText == null) _resultTitleText = FindTxt(resultPanel, "ResultTitle");
                if (_resultScoreText == null) _resultScoreText = FindTxt(resultPanel, "ResultScore");
            }
        }

        #endregion

        // ═══════════════════════════════════════════
        // RUNTIME UI CONSTRUCTION (Tier 3: create from scratch)
        // ═══════════════════════════════════════════

        #region Runtime UI Construction

        private void BuildFullUI(Transform canvasRoot)
        {
            // Create 5 pages
            _titlePage  = CreatePage("TitlePage", canvasRoot, BG_TITLE);
            _mainPage   = CreatePage("MainPage", canvasRoot, BG_MAIN);
            _gamePage   = CreatePage("GamePage", canvasRoot, BG_GAME);
            _resultPage = CreatePage("ResultPage", canvasRoot, BG_OVERLAY);
            _shopPage   = CreatePage("ShopPage", canvasRoot, COL_SHOP_BG);

            // ── Title Page ──
            MakeText("LogoText", _titlePage.transform, "BalloonFlow", 72,
                     TextAnchor.MiddleCenter, Color.white, V(0, 120), V(900, 140));
            MakeText("SubtitleText", _titlePage.transform, "Pop & Flow Puzzle", 28,
                     TextAnchor.MiddleCenter, new Color(0.7f, 0.7f, 0.8f), V(0, 20), V(600, 50));
            MakeText("TapToStart", _titlePage.transform, "Tap to Start", 24,
                     TextAnchor.MiddleCenter, new Color(1, 1, 1, 0.5f), V(0, -250), V(400, 50));

            // ── Main Page ──
            MakeText("MainTitle", _mainPage.transform, "BalloonFlow", 56,
                     TextAnchor.MiddleCenter, Color.white, V(0, 400), V(800, 100));

            var currBar = MakePanel("CurrencyBar", _mainPage.transform, new Color(0, 0, 0, 0.4f));
            SetTopStretch(currBar, 80);

            // Coin button (clickable → opens shop)
            _coinButton = MakeButton("CoinButton", currBar.transform, "", COL_COIN_BTN, 24,
                                     V(0, 0), V(220, 55));
            AnchorLeft(_coinButton.gameObject);
            var coinBtnRT = _coinButton.GetComponent<RectTransform>();
            coinBtnRT.anchoredPosition = new Vector2(130, 0);
            _coinDisplayText = _coinButton.GetComponentInChildren<Text>();
            if (_coinDisplayText != null)
            {
                _coinDisplayText.text = "2000";
                _coinDisplayText.color = Color.white;
                _coinDisplayText.alignment = TextAnchor.MiddleCenter;
            }

            // Gem button (clickable → opens shop)
            _gemButton = MakeButton("GemButton", currBar.transform, "", COL_GEM_BTN, 24,
                                    V(0, 0), V(180, 55));
            AnchorRight(_gemButton.gameObject);
            var gemBtnRT = _gemButton.GetComponent<RectTransform>();
            gemBtnRT.anchoredPosition = new Vector2(-110, 0);
            _gemDisplayText = _gemButton.GetComponentInChildren<Text>();
            if (_gemDisplayText != null)
            {
                _gemDisplayText.text = "50";
                _gemDisplayText.color = Color.white;
                _gemDisplayText.alignment = TextAnchor.MiddleCenter;
            }

            // Play button with stage info
            _playButton = MakeButton("PlayButton", _mainPage.transform, "Stage 1", COL_PLAY, 36,
                                     V(0, -50), V(380, 120));
            _playButtonLabel = _playButton.GetComponentInChildren<Text>();

            // ── Game Page (HUD managed by HUDController, not here) ──
            var hud = MakePanel("HUD_Top", _gamePage.transform, COL_HUD);
            SetTopStretch(hud, 130);
            MakeText("ScoreText", hud.transform, "0", 42,
                     TextAnchor.MiddleCenter, Color.white, V(0, -15), V(300, 55));
            MakeText("LevelText", hud.transform, "Level 1", 22,
                     TextAnchor.MiddleCenter, new Color(0.8f, 0.8f, 0.9f), V(0, -60), V(200, 35));
            var ballGO = MakeText("RemainingText", hud.transform, "Balloons: --", 20,
                         TextAnchor.MiddleLeft, new Color(0.9f, 0.7f, 0.3f), V(30, -15), V(220, 35));
            AnchorLeft(ballGO);
            var holdGO = MakeText("HolderText", hud.transform, "Holders: 0/5", 20,
                         TextAnchor.MiddleRight, new Color(0.3f, 0.9f, 0.7f), V(-30, -15), V(220, 35));
            AnchorRight(holdGO);

            // BoardArea and HolderArea removed — 3D scene renders directly, no 2D overlay needed.
            // GamePage only has HUD for score/level info.

            // ── Result Page ──
            var resultPanel = MakePanel("ResultPanel", _resultPage.transform, new Color(0.12f, 0.12f, 0.22f));
            SetCenter(resultPanel, V(0, 0), V(700, 750));

            _resultTitleText = MakeText("ResultTitle", resultPanel.transform, "Level Clear!", 48,
                               TextAnchor.MiddleCenter, new Color(1f, 0.85f, 0.1f), V(0, 270), V(600, 80)).GetComponent<Text>();
            _resultScoreText = MakeText("ResultScore", resultPanel.transform, "Score: 0\n★ 0/3", 32,
                               TextAnchor.MiddleCenter, Color.white, V(0, 130), V(500, 120)).GetComponent<Text>();

            _nextButton  = MakeButton("NextButton", resultPanel.transform, "NEXT", COL_NEXT, 28, V(0, -30), V(260, 75));
            _retryButton = MakeButton("RetryButton", resultPanel.transform, "RETRY", COL_RETRY, 28, V(0, -120), V(260, 75));
            _homeButton  = MakeButton("HomeButton", resultPanel.transform, "HOME", COL_HOME, 24, V(0, -210), V(200, 60));

            // ── Shop Page ──
            // Header bar
            var shopHeader = MakePanel("ShopHeader", _shopPage.transform, new Color(0, 0, 0, 0.5f));
            SetTopStretch(shopHeader, 100);
            MakeText("ShopTitle", shopHeader.transform, "SHOP", 42,
                     TextAnchor.MiddleCenter, Color.white, V(0, -10), V(300, 60));
            _shopCloseButton = MakeButton("ShopCloseButton", shopHeader.transform, "X", COL_HOME, 28,
                                          V(0, -10), V(70, 70));
            AnchorRight(_shopCloseButton.gameObject);
            var closeBtnRT = _shopCloseButton.GetComponent<RectTransform>();
            closeBtnRT.anchoredPosition = new Vector2(-50, -10);

            // Currency display in shop header
            var shopCoinGO = MakeText("ShopCoins", shopHeader.transform, "", 22,
                             TextAnchor.MiddleLeft, Color.yellow, V(40, -10), V(200, 35));
            AnchorLeft(shopCoinGO);
            var shopGemGO = MakeText("ShopGems", shopHeader.transform, "", 22,
                            TextAnchor.MiddleRight, new Color(0.5f, 0.8f, 1f), V(-130, -10), V(160, 35));
            AnchorRight(shopGemGO);

            // Scrollable content area
            var shopScrollArea = MakePanel("ShopScrollArea", _shopPage.transform, new Color(0, 0, 0, 0));
            var scrollRT = shopScrollArea.GetComponent<RectTransform>();
            scrollRT.anchorMin = new Vector2(0, 0);
            scrollRT.anchorMax = new Vector2(1, 1);
            scrollRT.offsetMin = new Vector2(0, 0);
            scrollRT.offsetMax = new Vector2(0, -110); // Below header

            // Content root for dynamic shop items
            var contentGO = new GameObject("ShopContent");
            contentGO.layer = LayerMask.NameToLayer("UI");
            contentGO.transform.SetParent(shopScrollArea.transform, false);
            var contentRT = contentGO.AddComponent<RectTransform>();
            contentRT.anchorMin = new Vector2(0, 1);
            contentRT.anchorMax = new Vector2(1, 1);
            contentRT.pivot = new Vector2(0.5f, 1);
            contentRT.offsetMin = new Vector2(0, 0);
            contentRT.offsetMax = new Vector2(0, 0);
            contentRT.sizeDelta = new Vector2(0, 1200); // Will be resized by BuildShopItems
            _shopContentRoot = contentGO.transform;
        }

        #endregion

        // ═══════════════════════════════════════════
        // INFRASTRUCTURE
        // ═══════════════════════════════════════════

        #region Infrastructure

        private void EnsureEventSystem()
        {
            if (FindAnyObjectByType<EventSystem>() != null) return;
            var go = new GameObject("EventSystem");
            go.AddComponent<EventSystem>();
            go.AddComponent<InputSystemUIInputModule>();
        }

        private GameObject CreateCanvasGO()
        {
            var go = new GameObject("Canvas");
            var canvas = go.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceCamera;
            var scaler = go.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(REF_WIDTH, REF_HEIGHT);
            scaler.matchWidthOrHeight = 0.5f;
            go.AddComponent<GraphicRaycaster>();

            // Set canvas to UI layer (matches UICamera culling mask)
            go.layer = LayerMask.NameToLayer("UI");

            // Find UICamera for canvas rendering (3D scene uses a separate UI camera)
            Camera uiCam = null;
            Camera[] cameras = FindObjectsByType<Camera>(FindObjectsSortMode.None);
            foreach (var cam in cameras)
            {
                if (cam.gameObject.name == "UICamera")
                {
                    uiCam = cam;
                    break;
                }
            }
            if (uiCam != null)
            {
                canvas.worldCamera = uiCam;
                canvas.planeDistance = 1f;
            }
            else
            {
                // Fallback: if no UICamera found, use ScreenSpaceOverlay
                canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            }

            return go;
        }

        private void EnsureManagers()
        {
            EnsureSingleton<ObjectPoolManager>("Mgr_ObjectPool");
            EnsureSingleton<InputHandler>("Mgr_Input");
            EnsureSingleton<UIManager>("Mgr_UI");
            EnsureSingleton<PopupManager>("Mgr_Popup");

            var railGO = EnsureSingleton<RailManager>("Mgr_Rail");
            if (railGO.GetComponent<RailRenderer>() == null)
                railGO.AddComponent<RailRenderer>();

            EnsureSingleton<ScoreManager>("Mgr_Score");

            var levelGO = EnsureSingleton<LevelManager>("Mgr_Level");
            if (levelGO.GetComponent<LevelDataProvider>() == null)
                levelGO.AddComponent<LevelDataProvider>();

            EnsureSingleton<BoardStateManager>("Mgr_BoardState");
            EnsureSingleton<HolderManager>("Mgr_Holder");
            EnsureSingleton<DartManager>("Mgr_Dart");
            EnsureSingleton<BalloonController>("Mgr_Balloon");
            EnsureSingleton<PopProcessor>("Mgr_Pop");
            EnsureSingleton<HUDController>("Mgr_HUD");
            EnsureSingleton<PageController>("Mgr_Page");
            EnsureSingleton<FeedbackController>("Mgr_Feedback");
            EnsureSingleton<CurrencyManager>("Mgr_Currency");
            EnsureSingleton<GemManager>("Mgr_Gem");
            EnsureSingleton<LifeManager>("Mgr_Life");
            EnsureSingleton<DailyRewardManager>("Mgr_DailyReward");
            EnsureSingleton<BoosterManager>("Mgr_Booster");
            EnsureSingleton<ContinueHandler>("Mgr_Continue");
            EnsureSingleton<ShopManager>("Mgr_Shop");
            EnsureSingleton<AdManager>("Mgr_Ad");
            EnsureSingleton<OfferManager>("Mgr_Offer");
            EnsureSingleton<IAPManager>("Mgr_IAP");
            EnsureSingleton<PackageManager>("Mgr_Package");
            EnsureSingleton<GimmickManager>("Mgr_Gimmick");
            EnsureSingleton<BalanceProcessor>("Mgr_Balance");
            EnsureSingleton<TutorialController>("Mgr_TutorialCtrl");
            EnsureSingleton<TutorialManager>("Mgr_TutorialMgr");
            EnsureSingleton<HolderVisualManager>("Mgr_HolderVisual");
            EnsureSingleton<LevelGenerator>("Mgr_LevelGen");

            // Wire InputHandler._gameCamera via reflection (runtime only).
            // Must use Camera.main (tagged MainCamera) — the 3D game camera, not the UICamera.
            var inputHandler = FindAnyObjectByType<InputHandler>();
            if (inputHandler != null)
            {
                // Find the 3D main camera (not the UI camera)
                Camera mainCam = Camera.main; // Tagged as MainCamera
                if (mainCam != null)
                {
                    var field = typeof(InputHandler).GetField("_gameCamera",
                        System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                    if (field != null) field.SetValue(inputHandler, mainCam);
                }
            }

            // Wire LevelManager._levelDataProvider via reflection (runtime fallback)
            var levelMgr = FindAnyObjectByType<LevelManager>();
            if (levelMgr != null)
            {
                var ldp = levelMgr.GetComponent<LevelDataProvider>();
                if (ldp != null)
                {
                    var field = typeof(LevelManager).GetField("_levelDataProvider",
                        System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                    if (field != null && field.GetValue(levelMgr) == null)
                    {
                        field.SetValue(levelMgr, ldp);
                    }
                }
            }

            Debug.Log("[GameBootstrap] All managers initialized.");
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

        // ═══════════════════════════════════════════
        // GAME FLOW
        // ═══════════════════════════════════════════

        #region Game Flow

        private IEnumerator TitleSequence()
        {
            yield return new WaitForSeconds(_titleDuration);
            if (!_titleTapped) TransitionToMain();
        }

        private void TransitionToMain()
        {
            _titleTapped = true;
            Debug.Log($"[GameBootstrap] TransitionToMain — _mainPage={_mainPage != null}, " +
                      $"childCount={(_mainPage != null ? _mainPage.transform.childCount : -1)}");
            ShowPage(_mainPage);
            RefreshMainPageInfo();
        }

        private void OnPlayClicked()
        {
            _pendingResultIsWin = false;
            ShowPage(_gamePage);
            if (LevelManager.HasInstance)
            {
                int highest = LevelManager.Instance.GetHighestCompletedLevel();
                int levelId = highest > 0 ? highest + 1 : 1;
                LevelManager.Instance.LoadLevel(levelId);
            }
            else
            {
                Debug.LogWarning("[GameBootstrap] LevelManager not available.");
            }
        }

        private void OnNextClicked()
        {
            _pendingResultIsWin = false;
            ShowPage(_gamePage);
            if (LevelManager.HasInstance)
                LevelManager.Instance.LoadLevel(LevelManager.Instance.GetNextLevelId());
        }

        private void OnRetryClicked()
        {
            _pendingResultIsWin = false;
            ShowPage(_gamePage);
            if (LevelManager.HasInstance)
                LevelManager.Instance.RetryLevel();
        }

        private void OnHomeClicked()
        {
            ShowPage(_mainPage);
            RefreshMainPageInfo();
        }

        private void OnShopClicked()
        {
            BuildShopItems();
            ShowPage(_shopPage);

            if (ShopManager.HasInstance)
                ShopManager.Instance.OpenShop();
        }

        private void OnShopCloseClicked()
        {
            ShowPage(_mainPage);
            RefreshMainPageInfo();

            if (ShopManager.HasInstance)
                ShopManager.Instance.CloseShop();
        }

        #endregion

        // ═══════════════════════════════════════════
        // EVENT HANDLERS
        // ═══════════════════════════════════════════

        #region Event Handlers

        private void HandleLevelCompleted(OnLevelCompleted evt)
        {
            Debug.Log($"[GameBootstrap] HandleLevelCompleted: level={evt.levelId}, score={evt.score}, stars={evt.starCount}");
            // Clear always wins: cancel any pending fail popup, show clear instead
            _pendingResultIsWin = true;
            StopCoroutine(nameof(DelayedShowResultCoroutine));
            StartCoroutine(DelayedShowResultCoroutine(true, evt.score, evt.starCount));
        }

        private void HandleLevelFailed(OnLevelFailed evt)
        {
            if (ContinueHandler.HasInstance && ContinueHandler.Instance.CanContinue())
                return;
            // Only show fail if no clear has arrived yet
            if (_pendingResultIsWin) return;
            StartCoroutine(DelayedShowResultCoroutine(false, 0, 0));
        }

        private void HandleCoinChanged(OnCoinChanged evt)
        {
            if (_coinDisplayText != null) _coinDisplayText.text = $"Coins: {evt.currentCoins:N0}";
        }

        private void HandleGemChanged(OnGemChanged evt)
        {
            if (_gemDisplayText != null) _gemDisplayText.text = $"Gems: {evt.currentGems}";
        }

        #endregion

        // ═══════════════════════════════════════════
        // PAGE MANAGEMENT
        // ═══════════════════════════════════════════

        #region Page Management

        private IEnumerator DelayedShowResultCoroutine(bool isWin, int score, int stars)
        {
            yield return new WaitForSeconds(0.8f);
            ShowResultPage(isWin, score, stars);
        }

        private void ShowResultPage(bool isWin, int score, int stars)
        {
            Debug.Log($"[GameBootstrap] ShowResultPage: isWin={isWin}, score={score}, stars={stars}, resultPage={_resultPage != null}");

            // Safety: if _resultPage was lost (scene reload, etc.), try to recover
            if (_resultPage == null)
            {
                Debug.LogWarning("[GameBootstrap] _resultPage is NULL! Attempting recovery...");
                Canvas canvas = FindAnyObjectByType<Canvas>();
                if (canvas != null)
                {
                    _resultPage = CreatePage("ResultPage", canvas.transform, BG_OVERLAY);
                    var resultPanel = MakePanel("ResultPanel", _resultPage.transform, new Color(0.12f, 0.12f, 0.22f));
                    SetCenter(resultPanel, V(0, 0), V(700, 750));
                    _resultTitleText = MakeText("ResultTitle", resultPanel.transform, "", 48,
                        TextAnchor.MiddleCenter, Color.white, V(0, 270), V(600, 80)).GetComponent<Text>();
                    _resultScoreText = MakeText("ResultScore", resultPanel.transform, "", 32,
                        TextAnchor.MiddleCenter, Color.white, V(0, 130), V(500, 120)).GetComponent<Text>();
                    _nextButton  = MakeButton("NextButton", resultPanel.transform, "NEXT", COL_NEXT, 28, V(0, -30), V(260, 75));
                    _retryButton = MakeButton("RetryButton", resultPanel.transform, "RETRY", COL_RETRY, 28, V(0, -120), V(260, 75));
                    _homeButton  = MakeButton("HomeButton", resultPanel.transform, "HOME", COL_HOME, 24, V(0, -210), V(200, 60));
                    if (_nextButton != null) _nextButton.onClick.AddListener(OnNextClicked);
                    if (_retryButton != null) _retryButton.onClick.AddListener(OnRetryClicked);
                    if (_homeButton != null) _homeButton.onClick.AddListener(OnHomeClicked);
                }
            }

            ShowPage(_resultPage);

            if (_resultTitleText != null)
            {
                _resultTitleText.text = isWin ? "Level Clear!" : "Game Over";
                _resultTitleText.color = isWin ? new Color(1f, 0.85f, 0.1f) : new Color(1f, 0.3f, 0.3f);
            }
            if (_resultScoreText != null)
                _resultScoreText.text = isWin ? $"Score: {score:N0}\n★ {stars} / 3" : "Better luck next time!";
            if (_nextButton != null)
                _nextButton.gameObject.SetActive(isWin);
        }

        private void RefreshCurrencyDisplay()
        {
            if (_coinDisplayText != null && CurrencyManager.HasInstance)
                _coinDisplayText.text = $"{CurrencyManager.Instance.Coins:N0}";
            if (_gemDisplayText != null && GemManager.HasInstance)
                _gemDisplayText.text = $"{GemManager.Instance.Gems}";
        }

        /// <summary>
        /// Refreshes all main page info: currency display and play button stage text.
        /// </summary>
        private void RefreshMainPageInfo()
        {
            RefreshCurrencyDisplay();
            UpdatePlayButtonLabel();
        }

        private void UpdatePlayButtonLabel()
        {
            if (_playButtonLabel == null) return;

            int currentStage = 1;
            if (LevelManager.HasInstance)
            {
                int highest = LevelManager.Instance.GetHighestCompletedLevel();
                currentStage = highest > 0 ? highest + 1 : 1;
            }

            _playButtonLabel.text = $"Stage {currentStage}";
        }

        /// <summary>
        /// Populates the shop page with items from ShopManager catalogue.
        /// </summary>
        private void BuildShopItems()
        {
            if (_shopContentRoot == null) return;

            // Clear existing items
            for (int i = _shopContentRoot.childCount - 1; i >= 0; i--)
            {
                Destroy(_shopContentRoot.GetChild(i).gameObject);
            }

            if (!ShopManager.HasInstance) return;

            ShopProduct[] products = ShopManager.Instance.GetProducts();
            float yOffset = 0f;
            const float itemHeight = 100f;
            const float itemSpacing = 10f;

            foreach (ShopProduct product in products)
            {
                CreateShopItem(_shopContentRoot, product, yOffset);
                yOffset -= (itemHeight + itemSpacing);
            }

            // Resize content root for scrolling
            var contentRT = _shopContentRoot.GetComponent<RectTransform>();
            if (contentRT != null)
            {
                float totalHeight = Mathf.Abs(yOffset);
                contentRT.sizeDelta = new Vector2(contentRT.sizeDelta.x, totalHeight);
            }
        }

        private void CreateShopItem(Transform parent, ShopProduct product, float yPos)
        {
            // Item container
            var itemGO = new GameObject($"Item_{product.productId}");
            itemGO.layer = LayerMask.NameToLayer("UI");
            itemGO.transform.SetParent(parent, false);
            var rt = itemGO.AddComponent<RectTransform>();
            rt.anchorMin = new Vector2(0, 1);
            rt.anchorMax = new Vector2(1, 1);
            rt.pivot = new Vector2(0.5f, 1);
            rt.offsetMin = new Vector2(20, yPos - 100);
            rt.offsetMax = new Vector2(-20, yPos);
            var img = itemGO.AddComponent<Image>();
            img.color = COL_SHOP_ITEM;
            img.raycastTarget = false;

            // Product name
            MakeText($"Name_{product.productId}", itemGO.transform, product.displayName, 24,
                     TextAnchor.MiddleLeft, Color.white, V(-130, 15), V(300, 35));

            // Description
            MakeText($"Desc_{product.productId}", itemGO.transform, product.description, 16,
                     TextAnchor.MiddleLeft, new Color(0.7f, 0.7f, 0.8f), V(-130, -15), V(300, 25));

            // Buy button
            string buyLabel = product.currencyType == "iap" ? product.priceDisplay : $"{product.coinPrice}";
            var buyBtn = MakeButton($"Buy_{product.productId}", itemGO.transform,
                                    buyLabel, COL_BUY, 20, V(230, 0), V(140, 55));

            // Capture product ID for lambda
            string capturedId = product.productId;
            buyBtn.onClick.AddListener(() =>
            {
                if (ShopManager.HasInstance)
                {
                    bool success = ShopManager.Instance.PurchaseProduct(capturedId);
                    if (success) RefreshCurrencyDisplay();
                }
            });
        }

        private void ShowPage(CanvasGroup page)
        {
            HideAllPages();
            if (page != null)
            {
                page.alpha = 1f;
                page.interactable = true;
                page.blocksRaycasts = true;
                page.gameObject.SetActive(true);
                Debug.Log($"[GameBootstrap] ShowPage: {page.gameObject.name} (alpha=1, active=true, children={page.transform.childCount})");
            }
            else
            {
                Debug.LogError("[GameBootstrap] ShowPage called with NULL page!");
            }
        }

        private void HideAllPages()
        {
            HidePage(_titlePage);
            HidePage(_mainPage);
            HidePage(_gamePage);
            HidePage(_resultPage);
            HidePage(_shopPage);
        }

        private void HidePage(CanvasGroup page)
        {
            if (page == null) return;
            page.alpha = 0f;
            page.interactable = false;
            page.blocksRaycasts = false;
        }

        #endregion

        // ═══════════════════════════════════════════
        // FIND HELPERS (Tier 2 resolution)
        // ═══════════════════════════════════════════

        #region Find Helpers

        private static CanvasGroup FindCG(Transform parent, string name)
        {
            if (parent == null) return null;
            Transform child = parent.Find(name);
            return child != null ? child.GetComponent<CanvasGroup>() : null;
        }

        private static Button FindBtn(Transform parent, string name)
        {
            if (parent == null) return null;
            Transform child = parent.Find(name);
            return child != null ? child.GetComponent<Button>() : null;
        }

        private static Text FindTxt(Transform parent, string name)
        {
            if (parent == null) return null;
            Transform child = parent.Find(name);
            return child != null ? child.GetComponent<Text>() : null;
        }

        #endregion

        // ═══════════════════════════════════════════
        // UI BUILDER HELPERS (Tier 3 construction)
        // ═══════════════════════════════════════════

        #region UI Builders

        private CanvasGroup CreatePage(string name, Transform parent, Color bgColor)
        {
            var go = new GameObject(name);
            go.layer = LayerMask.NameToLayer("UI");
            go.transform.SetParent(parent, false);
            go.AddComponent<RectTransform>();
            Stretch(go.GetComponent<RectTransform>());
            var img = go.AddComponent<Image>();
            img.color = bgColor;
            img.raycastTarget = true;
            return go.AddComponent<CanvasGroup>();
        }

        private GameObject MakePanel(string name, Transform parent, Color color)
        {
            var go = new GameObject(name);
            go.layer = LayerMask.NameToLayer("UI");
            go.transform.SetParent(parent, false);
            go.AddComponent<RectTransform>();
            Stretch(go.GetComponent<RectTransform>());
            var img = go.AddComponent<Image>();
            img.color = color;
            img.raycastTarget = false;
            return go;
        }

        private GameObject MakeText(string name, Transform parent, string content,
            int fontSize, TextAnchor alignment, Color color, Vector2 pos, Vector2 size)
        {
            var go = new GameObject(name);
            go.layer = LayerMask.NameToLayer("UI");
            go.transform.SetParent(parent, false);
            go.AddComponent<RectTransform>();
            SetCenter(go, pos, size);
            var t = go.AddComponent<Text>();
            t.text = content;
            t.fontSize = fontSize;
            t.alignment = alignment;
            t.color = color;
            t.font = _font;
            t.horizontalOverflow = HorizontalWrapMode.Overflow;
            t.verticalOverflow = VerticalWrapMode.Overflow;
            t.raycastTarget = false;
            return go;
        }

        private Button MakeButton(string name, Transform parent, string label,
            Color bgColor, int fontSize, Vector2 pos, Vector2 size)
        {
            var go = new GameObject(name);
            go.layer = LayerMask.NameToLayer("UI");
            go.transform.SetParent(parent, false);
            go.AddComponent<RectTransform>();
            SetCenter(go, pos, size);
            var img = go.AddComponent<Image>();
            img.color = bgColor;
            var btn = go.AddComponent<Button>();

            var textGO = new GameObject("Label");
            textGO.layer = LayerMask.NameToLayer("UI");
            textGO.transform.SetParent(go.transform, false);
            Stretch(textGO.AddComponent<RectTransform>());
            var t = textGO.AddComponent<Text>();
            t.text = label;
            t.fontSize = fontSize;
            t.alignment = TextAnchor.MiddleCenter;
            t.color = Color.white;
            t.font = _font;
            t.fontStyle = FontStyle.Bold;
            t.raycastTarget = false;
            return btn;
        }

        #endregion

        #region Layout Helpers

        private static Vector2 V(float x, float y) => new Vector2(x, y);

        private static void Stretch(RectTransform rt)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
        }

        private static void SetCenter(GameObject go, Vector2 pos, Vector2 size)
        {
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(0.5f, 0.5f);
            rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = pos;
            rt.sizeDelta = size;
        }

        private static void SetTopStretch(GameObject go, float height)
        {
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(0, 1);
            rt.anchorMax = new Vector2(1, 1);
            rt.pivot = new Vector2(0.5f, 1);
            rt.offsetMin = new Vector2(0, -height);
            rt.offsetMax = Vector2.zero;
        }

        private static void SetBottomStretch(GameObject go, float height)
        {
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(0, 0);
            rt.anchorMax = new Vector2(1, 0);
            rt.pivot = new Vector2(0.5f, 0);
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = new Vector2(0, height);
        }

        private static void AnchorLeft(GameObject go)
        {
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(0, 0.5f);
            rt.anchorMax = new Vector2(0, 0.5f);
            rt.pivot = new Vector2(0, 0.5f);
        }

        private static void AnchorRight(GameObject go)
        {
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(1, 0.5f);
            rt.anchorMax = new Vector2(1, 0.5f);
            rt.pivot = new Vector2(1, 0.5f);
        }

        #endregion
    }
}
