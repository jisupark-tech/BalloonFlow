using UnityEngine;
using UnityEngine.UI;

namespace BalloonFlow
{
    /// <summary>
    /// In-game HUD controller. New layout:
    /// Row 1: [Settings btn] | [Level X] | [Gold display + button]
    /// Row 2: [Balloons: N] | [Score] | [On Rail: N/M]
    /// Settings button → opens settings popup, pauses game.
    /// Gold + button → opens gold shop popup, pauses game.
    /// </summary>
    /// <remarks>
    /// Layer: Game | Genre: Puzzle | Role: Controller | Phase: 3
    /// </remarks>
    public class HUDController : SceneSingleton<HUDController>
    {
        #region Serialized Fields

        [Header("Score")]
        [SerializeField] private Text _scoreText;

        [Header("Stars")]
        [SerializeField] private Image[] _starImages;
        [SerializeField] private Sprite _starFilledSprite;
        [SerializeField] private Sprite _starEmptySprite;

        [Header("Balloons")]
        [SerializeField] private Text _remainingText;

        [Header("On-Rail Holders")]
        [SerializeField] private Text _holderCountText;
        [SerializeField] private Text _magazineCountText;

        [Header("Level Info")]
        [SerializeField] private Text _levelText;

        [Header("Gold Display")]
        [SerializeField] private Text _goldText;

        [Header("Move Count")]
        [SerializeField] private Text _moveCountText;

        #endregion

        #region Fields

        private int _currentStars;
        private Button _settingsButton;
        private Button _goldPlusButton;

        // Settings popup
        private CanvasGroup _settingsPopup;
        private Button _settingsCloseButton;

        // Gold shop popup
        private CanvasGroup _goldShopPopup;
        private Button _goldShopCloseButton;
        private Transform _goldShopContentRoot;

        #endregion

        #region Lifecycle

        private void OnEnable()
        {
            EventBus.Subscribe<OnScoreChanged>(HandleScoreChanged);
            EventBus.Subscribe<OnBoardStateChanged>(HandleBoardStateChanged);
            EventBus.Subscribe<OnHolderSelected>(HandleHolderSelected);
            EventBus.Subscribe<OnMagazineEmpty>(HandleMagazineEmpty);
            EventBus.Subscribe<OnHolderReturned>(HandleHolderReturned);
            EventBus.Subscribe<OnLevelLoaded>(HandleLevelLoaded);
            EventBus.Subscribe<OnCoinChanged>(HandleCoinChanged);
        }

        private void OnDisable()
        {
            EventBus.Unsubscribe<OnScoreChanged>(HandleScoreChanged);
            EventBus.Unsubscribe<OnBoardStateChanged>(HandleBoardStateChanged);
            EventBus.Unsubscribe<OnHolderSelected>(HandleHolderSelected);
            EventBus.Unsubscribe<OnMagazineEmpty>(HandleMagazineEmpty);
            EventBus.Unsubscribe<OnHolderReturned>(HandleHolderReturned);
            EventBus.Unsubscribe<OnLevelLoaded>(HandleLevelLoaded);
            EventBus.Unsubscribe<OnCoinChanged>(HandleCoinChanged);

            if (_settingsButton != null) _settingsButton.onClick.RemoveListener(OnSettingsClicked);
            if (_goldPlusButton != null) _goldPlusButton.onClick.RemoveListener(OnGoldPlusClicked);
            if (_settingsCloseButton != null) _settingsCloseButton.onClick.RemoveListener(OnSettingsCloseClicked);
            if (_goldShopCloseButton != null) _goldShopCloseButton.onClick.RemoveListener(OnGoldShopCloseClicked);
        }

        #endregion

        #region Public Methods

        public void UpdateScore(int score)
        {
            if (_scoreText != null) _scoreText.text = score.ToString("N0");
        }

        public void UpdateStars(int count)
        {
            _currentStars = Mathf.Clamp(count, 0, 3);
            if (_starImages == null) return;

            for (int i = 0; i < _starImages.Length; i++)
            {
                if (_starImages[i] == null) continue;
                _starImages[i].sprite = (i < _currentStars) ? _starFilledSprite : _starEmptySprite;
            }
        }

        public void UpdateRemainingBalloons(int count)
        {
            if (_remainingText != null) _remainingText.text = $"Balloons: {count}";
        }

        public void UpdateHolderInfo(int holderCount, int maxHolders)
        {
            if (_holderCountText != null) _holderCountText.text = $"On Rail: {holderCount}/{maxHolders}";
        }

        public void UpdateMagazineDisplay(int holderId, int remaining)
        {
            if (_magazineCountText != null) _magazineCountText.text = remaining > 0 ? remaining.ToString() : "-";
        }

        public void ShowMoveCount(int total, int used)
        {
            if (_moveCountText != null) _moveCountText.text = $"{used}/{total}";
        }

        public void SetLevelInfo(int levelId, int packageId)
        {
            if (_levelText != null) _levelText.text = $"Level {levelId}";
        }

        public void UpdateGoldDisplay(int amount)
        {
            if (_goldText != null) _goldText.text = amount.ToString("N0");
        }

        /// <summary>
        /// Wires the settings button. Called by GameBootstrap after building HUD UI.
        /// </summary>
        public void SetSettingsButton(Button btn)
        {
            _settingsButton = btn;
            if (_settingsButton != null) _settingsButton.onClick.AddListener(OnSettingsClicked);
        }

        /// <summary>
        /// Wires the gold + button. Called by GameBootstrap after building HUD UI.
        /// </summary>
        public void SetGoldPlusButton(Button btn)
        {
            _goldPlusButton = btn;
            if (_goldPlusButton != null) _goldPlusButton.onClick.AddListener(OnGoldPlusClicked);
        }

        /// <summary>
        /// Wires the settings popup elements.
        /// </summary>
        public void SetSettingsPopup(CanvasGroup popup, Button closeBtn)
        {
            _settingsPopup = popup;
            _settingsCloseButton = closeBtn;
            if (_settingsCloseButton != null) _settingsCloseButton.onClick.AddListener(OnSettingsCloseClicked);
            HidePopup(_settingsPopup);
        }

        /// <summary>
        /// Wires the gold shop popup elements.
        /// </summary>
        public void SetGoldShopPopup(CanvasGroup popup, Button closeBtn, Transform contentRoot)
        {
            _goldShopPopup = popup;
            _goldShopCloseButton = closeBtn;
            _goldShopContentRoot = contentRoot;
            if (_goldShopCloseButton != null) _goldShopCloseButton.onClick.AddListener(OnGoldShopCloseClicked);
            HidePopup(_goldShopPopup);
        }

        #endregion

        #region Button Handlers

        private void OnSettingsClicked()
        {
            ShowPopup(_settingsPopup);
            if (GameManager.HasInstance) GameManager.Instance.PauseGame();
        }

        private void OnSettingsCloseClicked()
        {
            HidePopup(_settingsPopup);
            if (GameManager.HasInstance) GameManager.Instance.ResumeGame();
        }

        private void OnGoldPlusClicked()
        {
            BuildGoldShopItems();
            ShowPopup(_goldShopPopup);
            if (GameManager.HasInstance) GameManager.Instance.PauseGame();
        }

        private void OnGoldShopCloseClicked()
        {
            HidePopup(_goldShopPopup);
            if (GameManager.HasInstance) GameManager.Instance.ResumeGame();
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

        private void BuildGoldShopItems()
        {
            if (_goldShopContentRoot == null) return;

            for (int i = _goldShopContentRoot.childCount - 1; i >= 0; i--)
                Destroy(_goldShopContentRoot.GetChild(i).gameObject);

            if (!ShopManager.HasInstance) return;

            ShopProduct[] products = ShopManager.Instance.GetProducts();
            float yOffset = 0f;
            Font font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            if (font == null) font = Resources.GetBuiltinResource<Font>("Arial.ttf");

            foreach (ShopProduct product in products)
            {
                if (product.currencyType != "coin" && product.currencyType != "iap") continue;

                var itemGO = new GameObject($"Item_{product.productId}");
                itemGO.layer = LayerMask.NameToLayer("UI");
                itemGO.transform.SetParent(_goldShopContentRoot, false);
                var rt = itemGO.AddComponent<RectTransform>();
                rt.anchorMin = new Vector2(0, 1);
                rt.anchorMax = new Vector2(1, 1);
                rt.pivot = new Vector2(0.5f, 1);
                rt.offsetMin = new Vector2(10, yOffset - 80);
                rt.offsetMax = new Vector2(-10, yOffset);
                var img = itemGO.AddComponent<Image>();
                img.color = new Color(0.12f, 0.14f, 0.24f);
                img.raycastTarget = false;

                // Product name
                var nameGO = new GameObject("Name");
                nameGO.layer = LayerMask.NameToLayer("UI");
                nameGO.transform.SetParent(itemGO.transform, false);
                var nameRT = nameGO.AddComponent<RectTransform>();
                nameRT.anchorMin = new Vector2(0, 0.5f);
                nameRT.anchorMax = new Vector2(0.6f, 0.5f);
                nameRT.pivot = new Vector2(0, 0.5f);
                nameRT.offsetMin = new Vector2(10, -15);
                nameRT.offsetMax = new Vector2(0, 15);
                var nameT = nameGO.AddComponent<Text>();
                nameT.text = product.displayName;
                nameT.fontSize = 20;
                nameT.font = font;
                nameT.color = Color.white;
                nameT.alignment = TextAnchor.MiddleLeft;
                nameT.raycastTarget = false;

                // Buy button
                string buyLabel = product.currencyType == "iap" ? product.priceDisplay : $"{product.coinPrice}";
                var buyGO = new GameObject("BuyBtn");
                buyGO.layer = LayerMask.NameToLayer("UI");
                buyGO.transform.SetParent(itemGO.transform, false);
                var buyRT = buyGO.AddComponent<RectTransform>();
                buyRT.anchorMin = new Vector2(1, 0.5f);
                buyRT.anchorMax = new Vector2(1, 0.5f);
                buyRT.pivot = new Vector2(1, 0.5f);
                buyRT.anchoredPosition = new Vector2(-10, 0);
                buyRT.sizeDelta = new Vector2(110, 45);
                var buyImg = buyGO.AddComponent<Image>();
                buyImg.color = new Color(0.2f, 0.65f, 0.9f);
                var buyBtn = buyGO.AddComponent<Button>();

                var lblGO = new GameObject("Label");
                lblGO.layer = LayerMask.NameToLayer("UI");
                lblGO.transform.SetParent(buyGO.transform, false);
                var lblRT = lblGO.AddComponent<RectTransform>();
                lblRT.anchorMin = Vector2.zero;
                lblRT.anchorMax = Vector2.one;
                lblRT.offsetMin = Vector2.zero;
                lblRT.offsetMax = Vector2.zero;
                var lblT = lblGO.AddComponent<Text>();
                lblT.text = buyLabel;
                lblT.fontSize = 18;
                lblT.font = font;
                lblT.color = Color.white;
                lblT.alignment = TextAnchor.MiddleCenter;
                lblT.fontStyle = FontStyle.Bold;
                lblT.raycastTarget = false;

                string capturedId = product.productId;
                buyBtn.onClick.AddListener(() =>
                {
                    if (ShopManager.HasInstance)
                    {
                        ShopManager.Instance.PurchaseProduct(capturedId);
                        if (CurrencyManager.HasInstance)
                            UpdateGoldDisplay(CurrencyManager.Instance.Coins);
                    }
                });

                yOffset -= 88;
            }
        }

        // ── EventBus handlers ──

        private void HandleScoreChanged(OnScoreChanged evt)
        {
            UpdateScore(evt.currentScore);
            if (ScoreManager.HasInstance) UpdateStars(ScoreManager.Instance.GetStarCount());
        }

        private void HandleBoardStateChanged(OnBoardStateChanged evt)
        {
            UpdateRemainingBalloons(evt.remainingBalloons);
        }

        private void HandleHolderSelected(OnHolderSelected evt)
        {
            UpdateMagazineDisplay(evt.holderId, evt.magazineCount);
            RefreshOnRailCount();
        }

        private void HandleMagazineEmpty(OnMagazineEmpty evt)
        {
            UpdateMagazineDisplay(evt.holderId, 0);
            RefreshOnRailCount();
        }

        private void HandleHolderReturned(OnHolderReturned evt)
        {
            UpdateMagazineDisplay(evt.holderId, evt.remainingMagazine);
            RefreshOnRailCount();
        }

        private void HandleLevelLoaded(OnLevelLoaded evt)
        {
            SetLevelInfo(evt.levelId, evt.packageId);
            UpdateScore(0);
            UpdateStars(0);
            RefreshOnRailCount();
            if (CurrencyManager.HasInstance) UpdateGoldDisplay(CurrencyManager.Instance.Coins);
        }

        private void HandleCoinChanged(OnCoinChanged evt)
        {
            UpdateGoldDisplay(evt.currentCoins);
        }

        private void RefreshOnRailCount()
        {
            if (!HolderVisualManager.HasInstance) return;
            int onRail = HolderVisualManager.Instance.GetOnRailCount();
            int max = HolderVisualManager.Instance.GetMaxOnRail();
            UpdateHolderInfo(onRail, max);
        }

        #endregion
    }
}
