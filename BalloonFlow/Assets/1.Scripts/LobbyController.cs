using UnityEngine;
using UnityEngine.UI;

namespace BalloonFlow
{
    /// <summary>
    /// Lobby (Main Menu) scene controller. Shows play button with current stage,
    /// currency display, settings access, and shop entry.
    /// </summary>
    /// <remarks>
    /// Layer: Game | Genre: Puzzle | Role: Controller | Phase: 0
    /// </remarks>
    public class LobbyController : MonoBehaviour
    {
        #region Constants

        private const int REF_WIDTH  = 1080;
        private const int REF_HEIGHT = 1920;

        private static readonly Color BG_MAIN     = new Color(0.06f, 0.10f, 0.18f, 1f);
        private static readonly Color COL_PLAY    = new Color(0.15f, 0.75f, 0.3f, 1f);
        private static readonly Color COL_COIN_BTN = new Color(0.85f, 0.75f, 0.1f, 1f);
        private static readonly Color COL_SETTINGS = new Color(0.4f, 0.4f, 0.5f, 1f);
        private static readonly Color COL_HOME     = new Color(0.5f, 0.5f, 0.55f, 1f);

        #endregion

        #region Fields

        private Button _playButton;
        private Button _settingsButton;
        private Button _coinButton;
        private Text _playButtonLabel;
        private Text _coinDisplayText;
        private Font _font;

        // Settings popup
        private CanvasGroup _settingsPopup;
        private Button _settingsCloseButton;

        // Gold shop popup
        private CanvasGroup _goldShopPopup;
        private Button _goldShopCloseButton;
        private Transform _goldShopContentRoot;

        #endregion

        #region Lifecycle

        private void Awake()
        {
            _font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            if (_font == null) _font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            if (_font == null) _font = Font.CreateDynamicFontFromOSFont("Arial", 14);

            BuildUI();
        }

        private void Start()
        {
            RefreshDisplay();
        }

        private void OnEnable()
        {
            EventBus.Subscribe<OnCoinChanged>(HandleCoinChanged);

            if (_playButton != null) _playButton.onClick.AddListener(OnPlayClicked);
            if (_settingsButton != null) _settingsButton.onClick.AddListener(OnSettingsClicked);
            if (_coinButton != null) _coinButton.onClick.AddListener(OnCoinClicked);
            if (_settingsCloseButton != null) _settingsCloseButton.onClick.AddListener(OnSettingsCloseClicked);
            if (_goldShopCloseButton != null) _goldShopCloseButton.onClick.AddListener(OnGoldShopCloseClicked);
        }

        private void OnDisable()
        {
            EventBus.Unsubscribe<OnCoinChanged>(HandleCoinChanged);

            if (_playButton != null) _playButton.onClick.RemoveListener(OnPlayClicked);
            if (_settingsButton != null) _settingsButton.onClick.RemoveListener(OnSettingsClicked);
            if (_coinButton != null) _coinButton.onClick.RemoveListener(OnCoinClicked);
            if (_settingsCloseButton != null) _settingsCloseButton.onClick.RemoveListener(OnSettingsCloseClicked);
            if (_goldShopCloseButton != null) _goldShopCloseButton.onClick.RemoveListener(OnGoldShopCloseClicked);
        }

        #endregion

        #region Button Handlers

        private void OnPlayClicked()
        {
            if (!GameManager.HasInstance) return;

            int levelId = 1;
            if (LevelManager.HasInstance)
            {
                int highest = LevelManager.Instance.GetHighestCompletedLevel();
                levelId = highest > 0 ? highest + 1 : 1;
            }

            GameManager.Instance.StartLevel(levelId);
        }

        private void OnSettingsClicked()
        {
            ShowPopup(_settingsPopup);
        }

        private void OnSettingsCloseClicked()
        {
            HidePopup(_settingsPopup);
        }

        private void OnCoinClicked()
        {
            BuildGoldShopItems();
            ShowPopup(_goldShopPopup);
        }

        private void OnGoldShopCloseClicked()
        {
            HidePopup(_goldShopPopup);
        }

        #endregion

        #region Private Methods

        private void ShowPopup(CanvasGroup popup)
        {
            if (popup == null) return;
            popup.alpha = 1f;
            popup.interactable = true;
            popup.blocksRaycasts = true;
        }

        private void HidePopup(CanvasGroup popup)
        {
            if (popup == null) return;
            popup.alpha = 0f;
            popup.interactable = false;
            popup.blocksRaycasts = false;
        }

        private void HandleCoinChanged(OnCoinChanged evt)
        {
            if (_coinDisplayText != null) _coinDisplayText.text = evt.currentCoins.ToString("N0");
        }

        private void RefreshDisplay()
        {
            // Update coin display
            if (_coinDisplayText != null && CurrencyManager.HasInstance)
                _coinDisplayText.text = CurrencyManager.Instance.Coins.ToString("N0");

            // Update play button label
            if (_playButtonLabel != null)
            {
                int currentStage = 1;
                if (LevelManager.HasInstance)
                {
                    int highest = LevelManager.Instance.GetHighestCompletedLevel();
                    currentStage = highest > 0 ? highest + 1 : 1;
                }
                _playButtonLabel.text = $"Stage {currentStage}";
            }
        }

        private void BuildGoldShopItems()
        {
            if (_goldShopContentRoot == null) return;

            // Clear existing
            for (int i = _goldShopContentRoot.childCount - 1; i >= 0; i--)
                Destroy(_goldShopContentRoot.GetChild(i).gameObject);

            if (!ShopManager.HasInstance) return;

            ShopProduct[] products = ShopManager.Instance.GetProducts();
            float yOffset = 0f;
            const float itemHeight = 90f;
            const float spacing = 8f;

            foreach (ShopProduct product in products)
            {
                // Only show coin-related products
                if (product.currencyType != "coin" && product.currencyType != "iap") continue;

                var itemGO = new GameObject($"Item_{product.productId}");
                itemGO.layer = LayerMask.NameToLayer("UI");
                itemGO.transform.SetParent(_goldShopContentRoot, false);
                var rt = itemGO.AddComponent<RectTransform>();
                rt.anchorMin = new Vector2(0, 1);
                rt.anchorMax = new Vector2(1, 1);
                rt.pivot = new Vector2(0.5f, 1);
                rt.offsetMin = new Vector2(20, yOffset - itemHeight);
                rt.offsetMax = new Vector2(-20, yOffset);
                var img = itemGO.AddComponent<Image>();
                img.color = new Color(0.12f, 0.14f, 0.24f);
                img.raycastTarget = false;

                CreateText($"Name_{product.productId}", itemGO.transform, product.displayName, 22,
                    TextAnchor.MiddleLeft, Color.white, new Vector2(-100, 10), new Vector2(260, 30));
                CreateText($"Desc_{product.productId}", itemGO.transform, product.description, 14,
                    TextAnchor.MiddleLeft, new Color(0.7f, 0.7f, 0.8f), new Vector2(-100, -12), new Vector2(260, 22));

                string buyLabel = product.currencyType == "iap" ? product.priceDisplay : $"{product.coinPrice}";
                var buyBtn = CreateButton($"Buy_{product.productId}", itemGO.transform, buyLabel,
                    new Color(0.2f, 0.65f, 0.9f), 18, new Vector2(200, 0), new Vector2(120, 50));

                string capturedId = product.productId;
                buyBtn.onClick.AddListener(() =>
                {
                    if (ShopManager.HasInstance)
                    {
                        ShopManager.Instance.PurchaseProduct(capturedId);
                        RefreshDisplay();
                    }
                });

                yOffset -= (itemHeight + spacing);
            }
        }

        #endregion

        #region UI Construction

        private void BuildUI()
        {
            Canvas canvas = FindAnyObjectByType<Canvas>();
            if (canvas == null)
            {
                var canvasGO = new GameObject("Canvas");
                canvas = canvasGO.AddComponent<Canvas>();
                canvas.renderMode = RenderMode.ScreenSpaceOverlay;
                var scaler = canvasGO.AddComponent<CanvasScaler>();
                scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
                scaler.referenceResolution = new Vector2(REF_WIDTH, REF_HEIGHT);
                scaler.matchWidthOrHeight = 0.5f;
                canvasGO.AddComponent<GraphicRaycaster>();
                canvasGO.layer = LayerMask.NameToLayer("UI");
            }

            Transform root = canvas.transform;

            // Background
            var bg = CreatePanel("Background", root, BG_MAIN);
            Stretch(bg.GetComponent<RectTransform>());

            // Title
            CreateText("MainTitle", root, "BalloonFlow", 56,
                TextAnchor.MiddleCenter, Color.white, new Vector2(0, 400), new Vector2(800, 100));

            // Top bar with currency
            var topBar = CreatePanel("TopBar", root, new Color(0, 0, 0, 0.4f));
            var topRT = topBar.GetComponent<RectTransform>();
            topRT.anchorMin = new Vector2(0, 1);
            topRT.anchorMax = new Vector2(1, 1);
            topRT.pivot = new Vector2(0.5f, 1);
            topRT.offsetMin = new Vector2(0, -80);
            topRT.offsetMax = Vector2.zero;

            // Coin display (left side of top bar)
            _coinButton = CreateButton("CoinButton", topBar.transform, "1000", COL_COIN_BTN, 22,
                new Vector2(0, 0), new Vector2(200, 50));
            var coinBtnRT = _coinButton.GetComponent<RectTransform>();
            coinBtnRT.anchorMin = new Vector2(0, 0.5f);
            coinBtnRT.anchorMax = new Vector2(0, 0.5f);
            coinBtnRT.pivot = new Vector2(0, 0.5f);
            coinBtnRT.anchoredPosition = new Vector2(20, 0);
            _coinDisplayText = _coinButton.GetComponentInChildren<Text>();

            // Settings button (right side of top bar)
            _settingsButton = CreateButton("SettingsBtn", topBar.transform, "Settings", COL_SETTINGS, 20,
                new Vector2(0, 0), new Vector2(140, 50));
            var setRT = _settingsButton.GetComponent<RectTransform>();
            setRT.anchorMin = new Vector2(1, 0.5f);
            setRT.anchorMax = new Vector2(1, 0.5f);
            setRT.pivot = new Vector2(1, 0.5f);
            setRT.anchoredPosition = new Vector2(-20, 0);

            // Play button
            _playButton = CreateButton("PlayButton", root, "Stage 1", COL_PLAY, 36,
                new Vector2(0, -50), new Vector2(380, 120));
            _playButtonLabel = _playButton.GetComponentInChildren<Text>();

            // Settings popup (hidden by default)
            _settingsPopup = BuildSettingsPopup(root);
            HidePopup(_settingsPopup);

            // Gold shop popup (hidden by default)
            _goldShopPopup = BuildGoldShopPopup(root);
            HidePopup(_goldShopPopup);
        }

        private CanvasGroup BuildSettingsPopup(Transform root)
        {
            var go = CreatePanel("SettingsPopup", root, new Color(0, 0, 0, 0.85f));
            Stretch(go.GetComponent<RectTransform>());
            var cg = go.AddComponent<CanvasGroup>();

            var panel = CreatePanel("SettingsPanel", go.transform, new Color(0.12f, 0.12f, 0.22f));
            var pRT = panel.GetComponent<RectTransform>();
            pRT.anchorMin = new Vector2(0.5f, 0.5f);
            pRT.anchorMax = new Vector2(0.5f, 0.5f);
            pRT.pivot = new Vector2(0.5f, 0.5f);
            pRT.sizeDelta = new Vector2(600, 500);

            CreateText("SettingsTitle", panel.transform, "Settings", 36,
                TextAnchor.MiddleCenter, Color.white, new Vector2(0, 180), new Vector2(400, 60));

            // Placeholder settings content
            CreateText("SoundLabel", panel.transform, "Sound: ON", 24,
                TextAnchor.MiddleCenter, Color.white, new Vector2(0, 60), new Vector2(300, 40));
            CreateText("MusicLabel", panel.transform, "Music: ON", 24,
                TextAnchor.MiddleCenter, Color.white, new Vector2(0, 10), new Vector2(300, 40));

            _settingsCloseButton = CreateButton("SettingsCloseBtn", panel.transform, "CLOSE", COL_HOME, 24,
                new Vector2(0, -180), new Vector2(200, 60));

            return cg;
        }

        private CanvasGroup BuildGoldShopPopup(Transform root)
        {
            var go = CreatePanel("GoldShopPopup", root, new Color(0, 0, 0, 0.85f));
            Stretch(go.GetComponent<RectTransform>());
            var cg = go.AddComponent<CanvasGroup>();

            var panel = CreatePanel("GoldShopPanel", go.transform, new Color(0.08f, 0.1f, 0.2f));
            var pRT = panel.GetComponent<RectTransform>();
            pRT.anchorMin = new Vector2(0.5f, 0.5f);
            pRT.anchorMax = new Vector2(0.5f, 0.5f);
            pRT.pivot = new Vector2(0.5f, 0.5f);
            pRT.sizeDelta = new Vector2(700, 700);

            CreateText("ShopTitle", panel.transform, "Gold Shop", 36,
                TextAnchor.MiddleCenter, Color.yellow, new Vector2(0, 280), new Vector2(400, 60));

            // Content area for shop items
            var contentGO = new GameObject("ShopContent");
            contentGO.layer = LayerMask.NameToLayer("UI");
            contentGO.transform.SetParent(panel.transform, false);
            var contentRT = contentGO.AddComponent<RectTransform>();
            contentRT.anchorMin = new Vector2(0, 0);
            contentRT.anchorMax = new Vector2(1, 1);
            contentRT.pivot = new Vector2(0.5f, 1);
            contentRT.offsetMin = new Vector2(20, 80);
            contentRT.offsetMax = new Vector2(-20, -80);
            _goldShopContentRoot = contentGO.transform;

            _goldShopCloseButton = CreateButton("GoldShopCloseBtn", panel.transform, "CLOSE", COL_HOME, 24,
                new Vector2(0, -280), new Vector2(200, 60));

            return cg;
        }

        #endregion

        #region UI Helpers

        private GameObject CreatePanel(string name, Transform parent, Color color)
        {
            var go = new GameObject(name);
            go.layer = LayerMask.NameToLayer("UI");
            go.transform.SetParent(parent, false);
            go.AddComponent<RectTransform>();
            var img = go.AddComponent<Image>();
            img.color = color;
            img.raycastTarget = (color.a > 0.3f);
            return go;
        }

        private GameObject CreateText(string name, Transform parent, string content,
            int fontSize, TextAnchor alignment, Color color, Vector2 pos, Vector2 size)
        {
            var go = new GameObject(name);
            go.layer = LayerMask.NameToLayer("UI");
            go.transform.SetParent(parent, false);
            var rt = go.AddComponent<RectTransform>();
            rt.anchorMin = new Vector2(0.5f, 0.5f);
            rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = pos;
            rt.sizeDelta = size;
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

        private Button CreateButton(string name, Transform parent, string label,
            Color bgColor, int fontSize, Vector2 pos, Vector2 size)
        {
            var go = new GameObject(name);
            go.layer = LayerMask.NameToLayer("UI");
            go.transform.SetParent(parent, false);
            var rt = go.AddComponent<RectTransform>();
            rt.anchorMin = new Vector2(0.5f, 0.5f);
            rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = pos;
            rt.sizeDelta = size;
            var img = go.AddComponent<Image>();
            img.color = bgColor;
            var btn = go.AddComponent<Button>();

            var textGO = new GameObject("Label");
            textGO.layer = LayerMask.NameToLayer("UI");
            textGO.transform.SetParent(go.transform, false);
            var trt = textGO.AddComponent<RectTransform>();
            trt.anchorMin = Vector2.zero;
            trt.anchorMax = Vector2.one;
            trt.offsetMin = Vector2.zero;
            trt.offsetMax = Vector2.zero;
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

        private static void Stretch(RectTransform rt)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
        }

        #endregion
    }
}
