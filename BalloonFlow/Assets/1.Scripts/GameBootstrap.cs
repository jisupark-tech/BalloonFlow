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
    /// InGame scene controller — manages the gameplay lifecycle:
    /// level load, play, result display, retry/next/home.
    /// Builds the in-game HUD with: Settings (left) | Level (center) | Gold+btn (right).
    /// Creates scene-specific managers and builds runtime UI.
    /// </summary>
    /// <remarks>
    /// Layer: Core | Genre: Puzzle | Role: Controller | Phase: 0
    /// </remarks>
    public class GameBootstrap : MonoBehaviour
    {
        #region Constants

        private const int REF_WIDTH  = 1080;
        private const int REF_HEIGHT = 1920;

        #endregion

        #region Colors

        private static readonly Color BG_GAME    = new Color(0.04f, 0.04f, 0.08f, 1f);
        private static readonly Color BG_OVERLAY = new Color(0f, 0f, 0f, 0.85f);
        private static readonly Color COL_PLAY   = new Color(0.15f, 0.75f, 0.3f, 1f);
        private static readonly Color COL_RETRY  = new Color(0.9f, 0.55f, 0.1f, 1f);
        private static readonly Color COL_NEXT   = new Color(0.2f, 0.6f, 0.95f, 1f);
        private static readonly Color COL_HOME   = new Color(0.5f, 0.5f, 0.55f, 1f);
        private static readonly Color COL_HUD    = new Color(0f, 0f, 0f, 0.5f);
        private static readonly Color COL_COIN_BTN = new Color(0.85f, 0.75f, 0.1f, 1f);
        private static readonly Color COL_SETTINGS = new Color(0.4f, 0.4f, 0.5f, 1f);

        #endregion

        #region Serialized Fields

        [Header("Result Page")]
        [SerializeField] private CanvasGroup _resultPage;
        [SerializeField] private Text _resultTitleText;
        [SerializeField] private Text _resultScoreText;
        [SerializeField] private Button _nextButton;
        [SerializeField] private Button _retryButton;
        [SerializeField] private Button _homeButton;

        #endregion

        #region Fields

        private bool _pendingResultIsWin;
        private Font _font;
        private CanvasGroup _gamePage;

        #endregion

        #region Lifecycle

        private void Awake()
        {
            _font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            if (_font == null) _font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            if (_font == null) _font = Font.CreateDynamicFontFromOSFont("Arial", 14);

            EnsureEventSystem();
            EnsureSceneManagers();
            BuildInGameUI();

            Debug.Log("[GameBootstrap] InGame scene initialized.");
        }

        private void Start()
        {
            // Load the pending level
            int pendingLevel = PlayerPrefs.GetInt("BF_PendingLevelId", 0);
            if (pendingLevel <= 0)
            {
                // Fallback: use highest completed + 1
                if (LevelManager.HasInstance)
                {
                    int highest = LevelManager.Instance.GetHighestCompletedLevel();
                    pendingLevel = highest > 0 ? highest + 1 : 1;
                }
                else
                {
                    pendingLevel = 1;
                }
            }

            // Clear the pending flag
            PlayerPrefs.DeleteKey("BF_PendingLevelId");

            if (LevelManager.HasInstance)
            {
                LevelManager.Instance.LoadLevel(pendingLevel);
            }
        }

        private void OnEnable()
        {
            EventBus.Subscribe<OnLevelCompleted>(HandleLevelCompleted);
            EventBus.Subscribe<OnLevelFailed>(HandleLevelFailed);

            if (_nextButton != null) _nextButton.onClick.AddListener(OnNextClicked);
            if (_retryButton != null) _retryButton.onClick.AddListener(OnRetryClicked);
            if (_homeButton != null) _homeButton.onClick.AddListener(OnHomeClicked);
        }

        private void OnDisable()
        {
            EventBus.Unsubscribe<OnLevelCompleted>(HandleLevelCompleted);
            EventBus.Unsubscribe<OnLevelFailed>(HandleLevelFailed);

            if (_nextButton != null) _nextButton.onClick.RemoveListener(OnNextClicked);
            if (_retryButton != null) _retryButton.onClick.RemoveListener(OnRetryClicked);
            if (_homeButton != null) _homeButton.onClick.RemoveListener(OnHomeClicked);
        }

        #endregion

        #region Scene Managers

        /// <summary>
        /// Creates scene-specific managers that only exist in the InGame scene.
        /// Persistent managers are already created by GameManager.
        /// </summary>
        private void EnsureSceneManagers()
        {
            // Ensure GameManager exists (in case scene is loaded directly for testing)
            if (!GameManager.HasInstance)
            {
                var go = new GameObject("GameManager");
                go.AddComponent<GameManager>();
            }

            if (!ResourceManager.HasInstance)
            {
                var go = new GameObject("Mgr_Resource");
                go.AddComponent<ResourceManager>();
            }

            // Scene-specific managers
            EnsureSingleton<InputHandler>("Mgr_Input");

            var railGO = EnsureSingleton<RailManager>("Mgr_Rail");
            if (railGO.GetComponent<RailRenderer>() == null)
                railGO.AddComponent<RailRenderer>();

            EnsureSingleton<ScoreManager>("Mgr_Score");
            EnsureSingleton<BoardStateManager>("Mgr_BoardState");
            EnsureSingleton<HolderManager>("Mgr_Holder");
            EnsureSingleton<DartManager>("Mgr_Dart");
            EnsureSingleton<BalloonController>("Mgr_Balloon");
            EnsureSingleton<PopProcessor>("Mgr_Pop");
            EnsureSingleton<HUDController>("Mgr_HUD");
            EnsureSingleton<FeedbackController>("Mgr_Feedback");
            EnsureSingleton<GimmickManager>("Mgr_Gimmick");
            EnsureSingleton<BalanceProcessor>("Mgr_Balance");
            EnsureSingleton<TutorialController>("Mgr_TutorialCtrl");
            EnsureSingleton<TutorialManager>("Mgr_TutorialMgr");
            EnsureSingleton<HolderVisualManager>("Mgr_HolderVisual");
            EnsureSingleton<LevelGenerator>("Mgr_LevelGen");

            // Wire InputHandler._gameCamera
            var inputHandler = FindAnyObjectByType<InputHandler>();
            if (inputHandler != null)
            {
                Camera mainCam = Camera.main;
                if (mainCam != null)
                {
                    var field = typeof(InputHandler).GetField("_gameCamera",
                        System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                    if (field != null) field.SetValue(inputHandler, mainCam);
                }
            }
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

        #region UI Construction

        private void BuildInGameUI()
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

            // ── Game Page (background + HUD) ──
            _gamePage = CreatePage("GamePage", root, BG_GAME);
            ShowCG(_gamePage);

            // ── HUD Row 1: Settings | Level | Gold ──
            var hudTop = MakePanel("HUD_Top", _gamePage.transform, COL_HUD);
            SetTopStretch(hudTop, 80);

            // Settings button (LEFT)
            var settingsBtn = MakeButton("SettingsBtn", hudTop.transform, "SET", COL_SETTINGS, 18,
                V(0, 0), V(80, 50));
            AnchorLeft(settingsBtn.gameObject);
            settingsBtn.GetComponent<RectTransform>().anchoredPosition = new Vector2(50, -40);

            // Level text (CENTER)
            var levelTextGO = MakeText("LevelText", hudTop.transform, "Level 1", 26,
                TextAnchor.MiddleCenter, Color.white, V(0, -40), V(250, 40));

            // Gold display (RIGHT) — coin icon text + amount + "+" button
            var goldPanel = MakePanel("GoldPanel", hudTop.transform, new Color(0, 0, 0, 0.3f));
            var goldRT = goldPanel.GetComponent<RectTransform>();
            goldRT.anchorMin = new Vector2(1, 0.5f);
            goldRT.anchorMax = new Vector2(1, 0.5f);
            goldRT.pivot = new Vector2(1, 0.5f);
            goldRT.anchoredPosition = new Vector2(-10, -2);
            goldRT.sizeDelta = new Vector2(200, 45);

            var goldTextGO = MakeText("GoldText", goldPanel.transform, "1000", 22,
                TextAnchor.MiddleCenter, Color.yellow, V(-20, 0), V(120, 35));

            var goldPlusBtn = MakeButton("GoldPlusBtn", goldPanel.transform, "+", COL_COIN_BTN, 24,
                V(0, 0), V(45, 40));
            var plusRT = goldPlusBtn.GetComponent<RectTransform>();
            plusRT.anchorMin = new Vector2(1, 0.5f);
            plusRT.anchorMax = new Vector2(1, 0.5f);
            plusRT.pivot = new Vector2(1, 0.5f);
            plusRT.anchoredPosition = new Vector2(-5, 0);

            // ── HUD Row 2: Balloons | Score | On-Rail ──
            var hudSub = MakePanel("HUD_Sub", _gamePage.transform, new Color(0, 0, 0, 0.3f));
            var hudSubRT = hudSub.GetComponent<RectTransform>();
            hudSubRT.anchorMin = new Vector2(0, 1);
            hudSubRT.anchorMax = new Vector2(1, 1);
            hudSubRT.pivot = new Vector2(0.5f, 1);
            hudSubRT.offsetMin = new Vector2(0, -130);
            hudSubRT.offsetMax = new Vector2(0, -80);

            var ballGO = MakeText("RemainingText", hudSub.transform, "Balloons: --", 18,
                TextAnchor.MiddleLeft, new Color(0.9f, 0.7f, 0.3f), V(20, 0), V(200, 35));
            AnchorLeft(ballGO);
            ballGO.GetComponent<RectTransform>().anchoredPosition = new Vector2(20, 0);

            var scoreGO = MakeText("ScoreText", hudSub.transform, "0", 28,
                TextAnchor.MiddleCenter, Color.white, V(0, 0), V(200, 35));

            var holderGO = MakeText("HolderText", hudSub.transform, "On Rail: 0/9", 18,
                TextAnchor.MiddleRight, new Color(0.3f, 0.9f, 0.7f), V(-20, 0), V(200, 35));
            AnchorRight(holderGO);
            holderGO.GetComponent<RectTransform>().anchoredPosition = new Vector2(-20, 0);

            // ── Result Page (overlay) ──
            _resultPage = CreatePage("ResultPage", root, BG_OVERLAY);
            HideCG(_resultPage);

            var resultPanel = MakePanel("ResultPanel", _resultPage.transform, new Color(0.12f, 0.12f, 0.22f));
            SetCenter(resultPanel, V(0, 0), V(700, 750));

            _resultTitleText = MakeText("ResultTitle", resultPanel.transform, "Level Clear!", 48,
                TextAnchor.MiddleCenter, new Color(1f, 0.85f, 0.1f), V(0, 270), V(600, 80)).GetComponent<Text>();
            _resultScoreText = MakeText("ResultScore", resultPanel.transform, "Score: 0\n★ 0/3", 32,
                TextAnchor.MiddleCenter, Color.white, V(0, 130), V(500, 120)).GetComponent<Text>();

            _nextButton  = MakeButton("NextButton", resultPanel.transform, "NEXT", COL_NEXT, 28, V(0, -30), V(260, 75));
            _retryButton = MakeButton("RetryButton", resultPanel.transform, "RETRY", COL_RETRY, 28, V(0, -120), V(260, 75));
            _homeButton  = MakeButton("HomeButton", resultPanel.transform, "HOME", COL_HOME, 24, V(0, -210), V(200, 60));

            // Re-wire result buttons (OnEnable may have fired before buttons were created)
            _nextButton.onClick.AddListener(OnNextClicked);
            _retryButton.onClick.AddListener(OnRetryClicked);
            _homeButton.onClick.AddListener(OnHomeClicked);

            // ── Settings Popup (on PopupCanvas or same canvas) ──
            var settingsPopup = BuildSettingsPopup(root);
            var settingsCloseBtn = settingsPopup.transform.Find("SettingsPanel/SettingsCloseBtn")?.GetComponent<Button>();

            // ── Gold Shop Popup ──
            var goldShopPopup = BuildGoldShopPopup(root);
            var goldShopCloseBtn = goldShopPopup.transform.Find("GoldShopPanel/GoldShopCloseBtn")?.GetComponent<Button>();
            var shopContent = goldShopPopup.transform.Find("GoldShopPanel/ShopContent");

            // ── Wire HUDController ──
            if (HUDController.HasInstance)
            {
                var hud = HUDController.Instance;
                WireFieldReflection(hud, "_scoreText", scoreGO.GetComponent<Text>());
                WireFieldReflection(hud, "_remainingText", ballGO.GetComponent<Text>());
                WireFieldReflection(hud, "_holderCountText", holderGO.GetComponent<Text>());
                WireFieldReflection(hud, "_levelText", levelTextGO.GetComponent<Text>());
                WireFieldReflection(hud, "_goldText", goldTextGO.GetComponent<Text>());

                hud.SetSettingsButton(settingsBtn);
                hud.SetGoldPlusButton(goldPlusBtn);
                hud.SetSettingsPopup(settingsPopup, settingsCloseBtn);
                hud.SetGoldShopPopup(goldShopPopup, goldShopCloseBtn, shopContent);
            }
        }

        private CanvasGroup BuildSettingsPopup(Transform root)
        {
            var go = CreatePage("SettingsPopup", root, new Color(0, 0, 0, 0.85f));
            HideCG(go);

            var panel = MakePanel("SettingsPanel", go.transform, new Color(0.12f, 0.12f, 0.22f));
            SetCenter(panel, V(0, 0), V(600, 500));

            MakeText("SettingsTitle", panel.transform, "Settings", 36,
                TextAnchor.MiddleCenter, Color.white, V(0, 180), V(400, 60));
            MakeText("SoundLabel", panel.transform, "Sound: ON", 24,
                TextAnchor.MiddleCenter, Color.white, V(0, 60), V(300, 40));
            MakeText("MusicLabel", panel.transform, "Music: ON", 24,
                TextAnchor.MiddleCenter, Color.white, V(0, 10), V(300, 40));
            MakeButton("SettingsCloseBtn", panel.transform, "CLOSE", COL_HOME, 24,
                V(0, -180), V(200, 60));

            return go;
        }

        private CanvasGroup BuildGoldShopPopup(Transform root)
        {
            var go = CreatePage("GoldShopPopup", root, new Color(0, 0, 0, 0.85f));
            HideCG(go);

            var panel = MakePanel("GoldShopPanel", go.transform, new Color(0.08f, 0.1f, 0.2f));
            SetCenter(panel, V(0, 0), V(700, 700));

            MakeText("ShopTitle", panel.transform, "Gold Shop", 36,
                TextAnchor.MiddleCenter, Color.yellow, V(0, 280), V(400, 60));

            var contentGO = new GameObject("ShopContent");
            contentGO.layer = LayerMask.NameToLayer("UI");
            contentGO.transform.SetParent(panel.transform, false);
            var contentRT = contentGO.AddComponent<RectTransform>();
            contentRT.anchorMin = new Vector2(0, 0);
            contentRT.anchorMax = new Vector2(1, 1);
            contentRT.pivot = new Vector2(0.5f, 1);
            contentRT.offsetMin = new Vector2(20, 80);
            contentRT.offsetMax = new Vector2(-20, -80);

            MakeButton("GoldShopCloseBtn", panel.transform, "CLOSE", COL_HOME, 24,
                V(0, -280), V(200, 60));

            return go;
        }

        private void WireFieldReflection(object target, string fieldName, object value)
        {
            if (target == null || value == null) return;
            var field = target.GetType().GetField(fieldName,
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            if (field != null) field.SetValue(target, value);
        }

        #endregion

        #region Game Flow

        private void OnNextClicked()
        {
            _pendingResultIsWin = false;
            HideCG(_resultPage);
            ShowCG(_gamePage);

            if (LevelManager.HasInstance)
            {
                int highestCleared = LevelManager.Instance.GetHighestCompletedLevel();
                int nextLevel = highestCleared + 1;
                LevelManager.Instance.LoadLevel(nextLevel);
            }
        }

        private void OnRetryClicked()
        {
            _pendingResultIsWin = false;
            HideCG(_resultPage);
            ShowCG(_gamePage);

            if (LevelManager.HasInstance)
                LevelManager.Instance.RetryLevel();
        }

        private void OnHomeClicked()
        {
            HideCG(_resultPage);
            if (GameManager.HasInstance)
            {
                GameManager.Instance.GoToLobby();
            }
        }

        #endregion

        #region Event Handlers

        private void HandleLevelCompleted(OnLevelCompleted evt)
        {
            Debug.Log($"[GameBootstrap] Level completed: level={evt.levelId}, score={evt.score}, stars={evt.starCount}");
            _pendingResultIsWin = true;
            StopCoroutine(nameof(DelayedShowResultCoroutine));
            StartCoroutine(DelayedShowResultCoroutine(true, evt.score, evt.starCount));
        }

        private void HandleLevelFailed(OnLevelFailed evt)
        {
            if (ContinueHandler.HasInstance && ContinueHandler.Instance.CanContinue())
                return;
            if (_pendingResultIsWin) return;
            StartCoroutine(DelayedShowResultCoroutine(false, 0, 0));
        }

        private IEnumerator DelayedShowResultCoroutine(bool isWin, int score, int stars)
        {
            yield return new WaitForSeconds(0.8f);
            ShowResultPage(isWin, score, stars);
        }

        private void ShowResultPage(bool isWin, int score, int stars)
        {
            ShowCG(_resultPage);

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

        #endregion

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
            go.layer = LayerMask.NameToLayer("UI");

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
                canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            }

            return go;
        }

        #endregion

        #region UI Helpers

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

        private void ShowCG(CanvasGroup cg)
        {
            if (cg == null) return;
            cg.alpha = 1f;
            cg.interactable = true;
            cg.blocksRaycasts = true;
            cg.gameObject.SetActive(true);
        }

        private void HideCG(CanvasGroup cg)
        {
            if (cg == null) return;
            cg.alpha = 0f;
            cg.interactable = false;
            cg.blocksRaycasts = false;
        }

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
