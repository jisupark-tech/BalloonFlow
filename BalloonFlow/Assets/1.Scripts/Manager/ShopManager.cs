using System.Collections.Generic;
using UnityEngine;

namespace BalloonFlow
{
    // ─────────────────────────────────────────────────────────────────
    // Data class (defined outside ShopManager for shared access)
    // ─────────────────────────────────────────────────────────────────

    /// <summary>
    /// Describes a single product available in the shop.
    /// </summary>
    [System.Serializable]
    public class ShopProduct
    {
        public string productId;
        public string displayName;
        public string description;
        public string priceDisplay;   // "$0.99" or "300 coins"
        public int    coinPrice;       // 0 if IAP
        public string currencyType;   // "iap" or "coins"
        public string category;       // "coin_pack", "bundle", "booster", "ad_removal", "heart"
    }

    // ─────────────────────────────────────────────────────────────────

    /// <summary>
    /// Shop UI controller — holds product catalogue and handles purchase routing.
    /// Routes IAP products to IAPManager; coin products to CurrencyManager.
    /// </summary>
    /// <remarks>
    /// Layer: Game | Genre: Puzzle | Role: Manager | Phase: 3
    /// DB Reference: CurrencyManager (Expert Generic score 0.6) — coin spend pattern;
    ///               No DB match for puzzle shop system — generated from L3 YAML logicFlow
    /// </remarks>
    public class ShopManager : Singleton<ShopManager>
    {
        #region Constants

        // IAP product IDs — 레퍼런스 게임 구조 (골드팩 6종 + 번들 3종 + 기타)
        private const string ProdGold1K      = "gold_1k";
        private const string ProdGold5K      = "gold_5k";
        private const string ProdGold10K     = "gold_10k";
        private const string ProdGold25K     = "gold_25k";
        private const string ProdGold50K     = "gold_50k";
        private const string ProdGold100K    = "gold_100k";
        private const string ProdBundleSmall = "bundle_small";
        private const string ProdBundleMed   = "bundle_medium";
        private const string ProdBundleUltra = "bundle_ultra";
        private const string ProdNoAds       = "remove_ads";
        private const string ProdHeartRefill = "heart_refill";

        // PlayerPrefs key for one-time purchase flags
        private const string PrefsNoAdsOwned   = "BalloonFlow_NoAdsOwned";

        private const string PopupShopId = "popup_shop";

        #endregion

        #region Serialized Fields

        [SerializeField] private CanvasGroup _shopPanelGroup;

        #endregion

        #region Fields

        private readonly List<ShopProduct> _products = new List<ShopProduct>();
        private bool _isOpen;

        #endregion

        #region Properties

        /// <summary>Whether the shop UI is currently open.</summary>
        public bool IsOpen => _isOpen;

        #endregion

        #region Lifecycle

        protected override void OnSingletonAwake()
        {
            BuildCatalogue();
        }

        private void OnEnable()
        {
            // No event subscriptions for shop — driven by button callbacks.
        }

        private void OnDisable()
        {
            // Mirror of OnEnable.
        }

        #endregion

        #region Public Methods

        /// <summary>
        /// Opens the shop UI.
        /// </summary>
        public void OpenShop()
        {
            if (_isOpen) return;

            _isOpen = true;
            RefreshProducts();

            if (PopupManager.HasInstance)
            {
                PopupManager.Instance.ShowPopup(PopupShopId, priority: 50);
            }
            else if (_shopPanelGroup != null)
            {
                SetPanelVisible(true);
            }

            Debug.Log("[ShopManager] Shop opened.");
        }

        /// <summary>
        /// Closes the shop UI.
        /// </summary>
        public void CloseShop()
        {
            if (!_isOpen) return;

            _isOpen = false;

            if (PopupManager.HasInstance)
            {
                PopupManager.Instance.ClosePopup(PopupShopId);
            }
            else if (_shopPanelGroup != null)
            {
                SetPanelVisible(false);
            }

            Debug.Log("[ShopManager] Shop closed.");
        }

        /// <summary>
        /// Returns a copy of the current product catalogue.
        /// </summary>
        public ShopProduct[] GetProducts()
        {
            return _products.ToArray();
        }

        /// <summary>
        /// Attempts to purchase a product by ID.
        /// Returns true on success.
        /// </summary>
        /// <param name="productId">Product identifier from the catalogue.</param>
        public bool PurchaseProduct(string productId)
        {
            ShopProduct product = FindProduct(productId);
            if (product == null)
            {
                Debug.LogWarning($"[ShopManager] Product not found: {productId}");
                return false;
            }

            if (product.currencyType == "iap")
            {
                return ProcessIAPPurchase(product);
            }
            else if (product.currencyType == "coins")
            {
                return ProcessCoinPurchase(product);
            }

            Debug.LogWarning($"[ShopManager] Unknown currency type: {product.currencyType}");
            return false;
        }

        /// <summary>
        /// Refreshes the product list (re-evaluates one-time purchase eligibility).
        /// </summary>
        public void RefreshProducts()
        {
            BuildCatalogue();
            Debug.Log("[ShopManager] Products refreshed.");
        }

        #endregion

        #region Private Methods

        private void BuildCatalogue()
        {
            _products.Clear();

            // ── 골드 팩 6종 (IAP, 레퍼런스 게임 구조) ──
            _products.Add(MakeIAP(ProdGold1K,   "1,000 골드",    "소형 골드 팩",     "KRW 2,751",   "coin_pack"));
            _products.Add(MakeIAP(ProdGold5K,   "5,000 골드",    "중형 골드 팩",     "KRW 11,007",  "coin_pack"));
            _products.Add(MakeIAP(ProdGold10K,  "10,000 골드",   "대형 골드 팩",     "KRW 20,638",  "coin_pack"));
            _products.Add(MakeIAP(ProdGold25K,  "25,000 골드",   "특대 골드 팩",     "KRW 41,277",  "coin_pack"));
            _products.Add(MakeIAP(ProdGold50K,  "50,000 골드",   "메가 골드 팩",     "KRW 75,675",  "coin_pack"));
            _products.Add(MakeIAP(ProdGold100K, "100,000 골드",  "울트라 골드 팩",   "KRW 137,591", "coin_pack"));

            // ── 번들 3종 (IAP, 골드+부스터+무한하트) ──
            _products.Add(MakeIAP(ProdBundleSmall, "소형 번들",   "골드 5,000 + 부스터 x1 + 무한하트 2시간", "KRW 13,759", "bundle"));
            _products.Add(MakeIAP(ProdBundleMed,   "중형 번들",   "골드 10,000 + 부스터 x2 + 무한하트 2시간", "KRW 27,518", "bundle"));
            _products.Add(MakeIAP(ProdBundleUltra, "울트라 번들", "골드 25,000 + 부스터 x5 + 무한하트 6시간", "KRW 59,000", "bundle"));

            // ── 광고 제거 (IAP, non-consumable) ──
            if (!PlayerPrefs.HasKey(PrefsNoAdsOwned))
            {
                _products.Add(MakeIAP(ProdNoAds, "광고 제거", "영구 광고 제거", "KRW 5,500", "ad_removal"));
            }

            // ── 하트 충전 (IAP) ──
            _products.Add(MakeIAP(ProdHeartRefill, "하트 충전", "하트 즉시 충전", "KRW 1,100", "heart"));

            // ── 부스터 4종 (코인 구매) ──
            _products.Add(MakeCoin(BoosterManager.EXTRA_TRAY,   "Extra Tray",    "+1 레일 슬롯",          300,  "booster"));
            _products.Add(MakeCoin(BoosterManager.SHUFFLE,      "Shuffle",       "대기열 순서 섞기",       1500, "booster"));
            _products.Add(MakeCoin(BoosterManager.SELECT_TOOL,  "Select Tool",   "원하는 홀더 선택",       1900, "booster"));
            _products.Add(MakeCoin(BoosterManager.COLOR_REMOVE, "Color Remove",  "한 색상 전부 제거",      2900, "booster"));
        }

        private ShopProduct MakeIAP(string id, string name, string desc, string price, string cat)
        {
            return new ShopProduct
            {
                productId    = id,
                displayName  = name,
                description  = desc,
                priceDisplay = price,
                coinPrice    = 0,
                currencyType = "iap",
                category     = cat
            };
        }

        private ShopProduct MakeCoin(string id, string name, string desc, int coins, string cat)
        {
            return new ShopProduct
            {
                productId    = id,
                displayName  = name,
                description  = desc,
                priceDisplay = $"{coins} coins",
                coinPrice    = coins,
                currencyType = "coins",
                category     = cat
            };
        }

        private ShopProduct FindProduct(string productId)
        {
            if (string.IsNullOrEmpty(productId)) return null;
            foreach (var p in _products)
            {
                if (p.productId == productId) return p;
            }
            return null;
        }

        private bool ProcessIAPPurchase(ShopProduct product)
        {
#if UNITY_IAP
            if (!IAPManager.HasInstance)
            {
                Debug.LogWarning("[ShopManager] IAPManager not available.");
                return false;
            }

            IAPManager.Instance.BuyProduct(product.productId);
            // Actual result comes async via IAPManager callback; we return true to indicate initiated.
            return true;
#else
            // Simulation mode — simulate purchase for editor testing.
            Debug.Log($"[ShopManager][IAP Sim] Purchased {product.productId} for {product.priceDisplay}");
            ApplyIAPReward(product.productId);
            return true;
#endif
        }

        private bool ProcessCoinPurchase(ShopProduct product)
        {
            // Coin-purchased boosters route through BoosterManager.
            if (product.category == "booster")
            {
                if (!BoosterManager.HasInstance)
                {
                    Debug.LogWarning("[ShopManager] BoosterManager not available.");
                    return false;
                }

                return BoosterManager.Instance.PurchaseBooster(product.productId);
            }

            // Generic coin purchase (future extension point).
            if (!CurrencyManager.HasInstance)
            {
                Debug.LogWarning("[ShopManager] CurrencyManager not available.");
                return false;
            }

            if (!CurrencyManager.Instance.SpendCoins(product.coinPrice, CurrencyManager.CoinSink.Other))
            {
                Debug.LogWarning($"[ShopManager] Not enough coins for {product.productId}.");
                return false;
            }

            Debug.Log($"[ShopManager] Coin purchase complete: {product.productId}");
            return true;
        }

        private void ApplyIAPReward(string productId)
        {
            if (!CurrencyManager.HasInstance) return;

            switch (productId)
            {
                // 골드 팩 6종
                case ProdGold1K:   CurrencyManager.Instance.AddCoins(1000,   CurrencyManager.CoinSource.IAP); break;
                case ProdGold5K:   CurrencyManager.Instance.AddCoins(5000,   CurrencyManager.CoinSource.IAP); break;
                case ProdGold10K:  CurrencyManager.Instance.AddCoins(10000,  CurrencyManager.CoinSource.IAP); break;
                case ProdGold25K:  CurrencyManager.Instance.AddCoins(25000,  CurrencyManager.CoinSource.IAP); break;
                case ProdGold50K:  CurrencyManager.Instance.AddCoins(50000,  CurrencyManager.CoinSource.IAP); break;
                case ProdGold100K: CurrencyManager.Instance.AddCoins(100000, CurrencyManager.CoinSource.IAP); break;

                // 번들 3종 (골드 + 부스터 전종 x배수 + 무한하트)
                case ProdBundleSmall:
                    CurrencyManager.Instance.AddCoins(5000, CurrencyManager.CoinSource.IAP);
                    BoosterManager.Instance?.AddBooster(BoosterManager.EXTRA_TRAY, 1);
                    BoosterManager.Instance?.AddBooster(BoosterManager.SHUFFLE, 1);
                    BoosterManager.Instance?.AddBooster(BoosterManager.SELECT_TOOL, 1);
                    BoosterManager.Instance?.AddBooster(BoosterManager.COLOR_REMOVE, 1);
                    if (LifeManager.HasInstance) LifeManager.Instance.ActivateInfiniteHearts(2f * 3600f); // 2시간
                    break;
                case ProdBundleMed:
                    CurrencyManager.Instance.AddCoins(10000, CurrencyManager.CoinSource.IAP);
                    BoosterManager.Instance?.AddBooster(BoosterManager.EXTRA_TRAY, 2);
                    BoosterManager.Instance?.AddBooster(BoosterManager.SHUFFLE, 2);
                    BoosterManager.Instance?.AddBooster(BoosterManager.SELECT_TOOL, 2);
                    BoosterManager.Instance?.AddBooster(BoosterManager.COLOR_REMOVE, 2);
                    if (LifeManager.HasInstance) LifeManager.Instance.ActivateInfiniteHearts(2f * 3600f); // 2시간
                    break;
                case ProdBundleUltra:
                    CurrencyManager.Instance.AddCoins(25000, CurrencyManager.CoinSource.IAP);
                    BoosterManager.Instance?.AddBooster(BoosterManager.EXTRA_TRAY, 5);
                    BoosterManager.Instance?.AddBooster(BoosterManager.SHUFFLE, 5);
                    BoosterManager.Instance?.AddBooster(BoosterManager.SELECT_TOOL, 5);
                    BoosterManager.Instance?.AddBooster(BoosterManager.COLOR_REMOVE, 5);
                    if (LifeManager.HasInstance) LifeManager.Instance.ActivateInfiniteHearts(6f * 3600f); // 6시간
                    break;

                case ProdNoAds:
                    PlayerPrefs.SetInt(PrefsNoAdsOwned, 1);
                    PlayerPrefs.Save();
                    break;
                case ProdHeartRefill:
                    EventBus.Publish(new OnLifeChanged { currentLives = 5, maxLives = 5 });
                    break;
            }

            RefreshProducts();
        }

        private void SetPanelVisible(bool visible)
        {
            if (_shopPanelGroup == null) return;
            _shopPanelGroup.alpha          = visible ? 1f : 0f;
            _shopPanelGroup.interactable   = visible;
            _shopPanelGroup.blocksRaycasts = visible;
        }

        #endregion
    }
}
