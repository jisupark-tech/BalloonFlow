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

        // IAP product IDs must match store listings.
        private const string ProdCoins500    = "coins_500";
        private const string ProdCoins1200   = "coins_1200";
        private const string ProdCoins3000   = "coins_3000";
        private const string ProdCoins8000   = "coins_8000";
        private const string ProdCoins20000  = "coins_20000";
        private const string ProdStarterPack = "starter_pack";
        private const string ProdWeekend     = "weekend_bundle";
        private const string ProdNoAds       = "remove_ads";
        private const string ProdHeartRefill = "heart_refill";

        // PlayerPrefs key for one-time purchase flags
        private const string PrefsStarterOwned = "BalloonFlow_StarterOwned";
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

            // ── Coin packs (IAP) ──
            _products.Add(MakeIAP(ProdCoins500,   "500 Coins",    "Small coin pack",           "$0.99",  "coin_pack"));
            _products.Add(MakeIAP(ProdCoins1200,  "1200 Coins",   "Medium coin pack",          "$1.99",  "coin_pack"));
            _products.Add(MakeIAP(ProdCoins3000,  "3000 Coins",   "Large coin pack",           "$4.99",  "coin_pack"));
            _products.Add(MakeIAP(ProdCoins8000,  "8000 Coins",   "Extra-large coin pack",     "$9.99",  "coin_pack"));
            _products.Add(MakeIAP(ProdCoins20000, "20000 Coins",  "Mega coin pack",            "$19.99", "coin_pack"));

            // ── Bundles (IAP, one-time purchase gated) ──
            if (!PlayerPrefs.HasKey(PrefsStarterOwned))
            {
                _products.Add(MakeIAP(ProdStarterPack, "Starter Pack", "One-time welcome bundle", "$2.99", "bundle"));
            }

            _products.Add(MakeIAP(ProdWeekend, "Weekend Bundle", "Limited-time value bundle", "$4.99", "bundle"));

            // ── Ad removal (IAP, non-consumable) ──
            if (!PlayerPrefs.HasKey(PrefsNoAdsOwned))
            {
                _products.Add(MakeIAP(ProdNoAds, "Remove Ads", "Play without ads forever", "$3.99", "ad_removal"));
            }

            // ── Heart refill (IAP) ──
            _products.Add(MakeIAP(ProdHeartRefill, "Heart Refill", "Refill all lives instantly", "$0.99", "heart"));

            // ── Booster packs (coin-purchasable) ──
            // Pre-play boosters (design: outgame.yaml §19-32)
            _products.Add(MakeCoin(BoosterManager.BF_PRE_01, "Extra Tray",   "Add +1 tray slot temporarily",     500,  "booster"));
            _products.Add(MakeCoin(BoosterManager.BF_PRE_02, "Shuffle",      "Shuffle all balloon positions",    800,  "booster"));
            // BF_PRE_03 (Color Hint, 20 gems) — handled via GemManager, not coin shop

            // In-play boosters (design: outgame.yaml §34-45)
            _products.Add(MakeCoin(BoosterManager.BF_IN_01,  "Extra Mag",    "Add +5 magazine to active holder", 600,  "booster"));
            _products.Add(MakeCoin(BoosterManager.BF_IN_02,  "Pop Any",      "Pop any one balloon regardless",   700,  "booster"));
            // BF_IN_03 (Remove Color, 15 gems) — handled via GemManager, not coin shop
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
                case ProdCoins500:   CurrencyManager.Instance.AddCoins(500, CurrencyManager.CoinSource.IAP);   break;
                case ProdCoins1200:  CurrencyManager.Instance.AddCoins(1200, CurrencyManager.CoinSource.IAP);  break;
                case ProdCoins3000:  CurrencyManager.Instance.AddCoins(3000, CurrencyManager.CoinSource.IAP);  break;
                case ProdCoins8000:  CurrencyManager.Instance.AddCoins(8000, CurrencyManager.CoinSource.IAP);  break;
                case ProdCoins20000: CurrencyManager.Instance.AddCoins(20000, CurrencyManager.CoinSource.IAP); break;
                case ProdStarterPack:
                    CurrencyManager.Instance.AddCoins(500, CurrencyManager.CoinSource.IAP);
                    BoosterManager.Instance?.AddBooster(BoosterManager.BF_PRE_02, 1);
                    PlayerPrefs.SetInt(PrefsStarterOwned, 1);
                    PlayerPrefs.Save();
                    break;
                case ProdWeekend:
                    CurrencyManager.Instance.AddCoins(1200, CurrencyManager.CoinSource.IAP);
                    BoosterManager.Instance?.AddBooster(BoosterManager.BF_IN_01, 1);
                    break;
                case ProdNoAds:
                    PlayerPrefs.SetInt(PrefsNoAdsOwned, 1);
                    PlayerPrefs.Save();
                    break;
                case ProdHeartRefill:
                    // Life refill routed to CurrencyManager or future LifeManager.
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
