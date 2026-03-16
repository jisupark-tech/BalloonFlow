#if UNITY_IAP
using UnityEngine.Purchasing;
using UnityEngine.Purchasing.Extension;
#endif

using System.Collections.Generic;
using UnityEngine;

namespace BalloonFlow
{
    /// <summary>
    /// Manages in-app purchases using Unity IAP SDK with conditional compilation.
    /// Provides simulation mode when UNITY_IAP is not defined.
    /// Products: coin packs, bundles, ad removal, heart refill.
    /// </summary>
    /// <remarks>
    /// Layer: Game | Genre: Puzzle | Role: Manager | Phase: 3
    /// DB Reference: No DB match found — generated from L3 YAML logicFlow (BM monetization)
    /// Requires: CurrencyManager (for coin grants after purchase)
    /// </remarks>
    public class IAPManager : Singleton<IAPManager>
#if UNITY_IAP
        , IDetailedStoreListener
#endif
    {
        #region Constants

        // Coin Pack product IDs
        public const string PRODUCT_COIN_500 = "coin_500";
        public const string PRODUCT_COIN_1200 = "coin_1200";
        public const string PRODUCT_COIN_3000 = "coin_3000";
        public const string PRODUCT_COIN_8000 = "coin_8000";
        public const string PRODUCT_COIN_20000 = "coin_20000";

        // Bundle product IDs
        public const string PRODUCT_STARTER_PACK = "starter_pack";
        public const string PRODUCT_WEEKEND_BUNDLE = "weekend_bundle";

        // Utility product IDs
        public const string PRODUCT_AD_REMOVE = "ad_remove";
        public const string PRODUCT_HEART_REFILL = "heart_refill_iap";

        private const string PREFS_KEY_AD_REMOVED = "BalloonFlow_AdRemoved";
        private const string PREFS_KEY_STARTER_PURCHASED = "BalloonFlow_StarterPurchased";

        #endregion

        #region Types

        /// <summary>
        /// Maps product IDs to their coin grant amounts.
        /// </summary>
        private struct ProductCoinGrant
        {
            public string productId;
            public int coinAmount;
        }

        #endregion

        #region Fields

        private bool _isInitialized;
        private readonly Dictionary<string, string> _cachedPrices = new Dictionary<string, string>();

        private static readonly ProductCoinGrant[] CoinGrants = new ProductCoinGrant[]
        {
            new ProductCoinGrant { productId = PRODUCT_COIN_500, coinAmount = 500 },
            new ProductCoinGrant { productId = PRODUCT_COIN_1200, coinAmount = 1200 },
            new ProductCoinGrant { productId = PRODUCT_COIN_3000, coinAmount = 3000 },
            new ProductCoinGrant { productId = PRODUCT_COIN_8000, coinAmount = 8000 },
            new ProductCoinGrant { productId = PRODUCT_COIN_20000, coinAmount = 20000 },
            new ProductCoinGrant { productId = PRODUCT_STARTER_PACK, coinAmount = 3000 },
            new ProductCoinGrant { productId = PRODUCT_WEEKEND_BUNDLE, coinAmount = 5000 },
            new ProductCoinGrant { productId = PRODUCT_HEART_REFILL, coinAmount = 0 },
            new ProductCoinGrant { productId = PRODUCT_AD_REMOVE, coinAmount = 0 },
        };

#if UNITY_IAP
        private IStoreController _storeController;
        private IExtensionProvider _extensionProvider;
#endif

        #endregion

        #region Properties

        /// <summary>
        /// Whether ads have been removed via IAP.
        /// </summary>
        public bool AdsRemoved => PlayerPrefs.GetInt(PREFS_KEY_AD_REMOVED, 0) == 1;

        /// <summary>
        /// Whether the one-time starter pack has been purchased.
        /// </summary>
        public bool StarterPackPurchased => PlayerPrefs.GetInt(PREFS_KEY_STARTER_PURCHASED, 0) == 1;

        #endregion

        #region Lifecycle

        protected override void OnSingletonAwake()
        {
            InitializeStore();
        }

        #endregion

        #region Public Methods

        /// <summary>
        /// Whether the IAP system has been initialized and is ready.
        /// </summary>
        public bool IsInitialized()
        {
            return _isInitialized;
        }

        /// <summary>
        /// Initiates a purchase for the given product ID.
        /// </summary>
        /// <param name="productId">The product ID to purchase.</param>
        public void PurchaseProduct(string productId)
        {
            if (string.IsNullOrEmpty(productId))
            {
                Debug.LogWarning("[IAPManager] PurchaseProduct called with null/empty productId.");
                return;
            }

#if UNITY_IAP
            if (!_isInitialized)
            {
                Debug.LogWarning("[IAPManager] Store not initialized. Cannot purchase.");
                PublishPurchaseResult(productId, false);
                return;
            }

            Product product = _storeController.products.WithID(productId);
            if (product != null && product.availableToPurchase)
            {
                _storeController.InitiatePurchase(product);
            }
            else
            {
                Debug.LogWarning($"[IAPManager] Product '{productId}' not found or not available.");
                PublishPurchaseResult(productId, false);
            }
#else
            // Simulation mode: auto-succeed purchase
            Debug.Log($"[IAPManager Sim] Simulating purchase: {productId}");
            ProcessPurchaseReward(productId);
            PublishPurchaseResult(productId, true);
#endif
        }

        /// <summary>
        /// Restores previously purchased non-consumable products (iOS).
        /// </summary>
        public void RestorePurchases()
        {
#if UNITY_IAP
            if (!_isInitialized)
            {
                Debug.LogWarning("[IAPManager] Store not initialized. Cannot restore.");
                return;
            }

            if (Application.platform == RuntimePlatform.IPhonePlayer ||
                Application.platform == RuntimePlatform.OSXPlayer)
            {
                var apple = _extensionProvider.GetExtension<IAppleExtensions>();
                apple.RestoreTransactions((result, error) =>
                {
                    if (result)
                    {
                        Debug.Log("[IAPManager] Restore purchases succeeded.");
                    }
                    else
                    {
                        Debug.LogWarning($"[IAPManager] Restore purchases failed: {error}");
                    }
                });
            }
            else
            {
                Debug.Log("[IAPManager] Restore not needed on this platform (auto-restore).");
            }
#else
            // Simulation mode: restore ad removal if previously "purchased"
            Debug.Log("[IAPManager Sim] Simulating restore purchases.");
            if (AdsRemoved)
            {
                EventBus.Publish(new OnPurchaseRestored { productId = PRODUCT_AD_REMOVE });
            }
#endif
        }

        /// <summary>
        /// Returns the localized price string for a product.
        /// Returns "$?.??" if product not found or store not initialized.
        /// </summary>
        /// <param name="productId">The product ID.</param>
        /// <returns>Localized price string.</returns>
        public string GetProductPrice(string productId)
        {
            if (string.IsNullOrEmpty(productId))
            {
                return "$?.??";
            }

#if UNITY_IAP
            if (!_isInitialized || _storeController == null)
            {
                return _cachedPrices.TryGetValue(productId, out string cached) ? cached : "$?.??";
            }

            Product product = _storeController.products.WithID(productId);
            if (product != null && product.availableToPurchase)
            {
                return product.metadata.localizedPriceString;
            }

            return "$?.??";
#else
            // Simulation mode: return hardcoded prices
            return GetSimulatedPrice(productId);
#endif
        }

        #endregion

        #region Private Methods — Initialization

        private void InitializeStore()
        {
#if UNITY_IAP
            if (_isInitialized)
            {
                return;
            }

            var builder = ConfigurationBuilder.Instance(StandardPurchasingModule.Instance());

            // Coin packs (consumable)
            builder.AddProduct(PRODUCT_COIN_500, ProductType.Consumable);
            builder.AddProduct(PRODUCT_COIN_1200, ProductType.Consumable);
            builder.AddProduct(PRODUCT_COIN_3000, ProductType.Consumable);
            builder.AddProduct(PRODUCT_COIN_8000, ProductType.Consumable);
            builder.AddProduct(PRODUCT_COIN_20000, ProductType.Consumable);

            // Bundles
            builder.AddProduct(PRODUCT_STARTER_PACK, ProductType.NonConsumable);
            builder.AddProduct(PRODUCT_WEEKEND_BUNDLE, ProductType.Consumable);

            // Utility
            builder.AddProduct(PRODUCT_AD_REMOVE, ProductType.NonConsumable);
            builder.AddProduct(PRODUCT_HEART_REFILL, ProductType.Consumable);

            UnityPurchasing.Initialize(this, builder);
#else
            // Simulation mode: mark as initialized immediately
            _isInitialized = true;
            CacheSimulatedPrices();
            Debug.Log("[IAPManager Sim] Initialized in simulation mode.");
#endif
        }

        #endregion

#if UNITY_IAP
        #region IDetailedStoreListener Implementation

        public void OnInitialized(IStoreController controller, IExtensionProvider extensions)
        {
            _storeController = controller;
            _extensionProvider = extensions;
            _isInitialized = true;

            // Cache prices for offline access
            foreach (Product product in controller.products.all)
            {
                if (product.availableToPurchase)
                {
                    _cachedPrices[product.definition.id] = product.metadata.localizedPriceString;
                }
            }

            Debug.Log("[IAPManager] Store initialized successfully.");
        }

        public void OnInitializeFailed(InitializationFailureReason error)
        {
            Debug.LogError($"[IAPManager] Store initialization failed: {error}");
        }

        public void OnInitializeFailed(InitializationFailureReason error, string message)
        {
            Debug.LogError($"[IAPManager] Store initialization failed: {error} — {message}");
        }

        public PurchaseProcessingResult ProcessPurchase(PurchaseEventArgs args)
        {
            string productId = args.purchasedProduct.definition.id;
            Debug.Log($"[IAPManager] Purchase succeeded: {productId}");

            ProcessPurchaseReward(productId);
            PublishPurchaseResult(productId, true);

            return PurchaseProcessingResult.Complete;
        }

        public void OnPurchaseFailed(Product product, PurchaseFailureReason failureReason)
        {
            Debug.LogWarning($"[IAPManager] Purchase failed: {product.definition.id} — {failureReason}");
            PublishPurchaseResult(product.definition.id, false);
        }

        public void OnPurchaseFailed(Product product, PurchaseFailureDescription failureDescription)
        {
            Debug.LogWarning($"[IAPManager] Purchase failed: {product.definition.id} — {failureDescription.message}");
            PublishPurchaseResult(product.definition.id, false);
        }

        #endregion
#endif

        #region Private Methods — Reward Processing

        private void ProcessPurchaseReward(string productId)
        {
            // Grant coins
            int coinAmount = GetCoinGrantForProduct(productId);
            if (coinAmount > 0 && CurrencyManager.HasInstance)
            {
                CurrencyManager.Instance.AddCoins(coinAmount, CurrencyManager.CoinSource.IAP);
            }

            // Handle non-consumable flags
            if (productId == PRODUCT_AD_REMOVE)
            {
                PlayerPrefs.SetInt(PREFS_KEY_AD_REMOVED, 1);
                PlayerPrefs.Save();
                Debug.Log("[IAPManager] Ads removed.");
            }
            else if (productId == PRODUCT_STARTER_PACK)
            {
                PlayerPrefs.SetInt(PREFS_KEY_STARTER_PURCHASED, 1);
                PlayerPrefs.Save();
                // Starter pack also grants 5 hearts + 3 boosters via events
                EventBus.Publish(new OnPurchaseRestored { productId = PRODUCT_STARTER_PACK });
            }
            else if (productId == PRODUCT_HEART_REFILL)
            {
                // Heart refill grants 5 hearts — handled by LifeManager listening to OnPurchaseCompleted
            }
        }

        private int GetCoinGrantForProduct(string productId)
        {
            if (string.IsNullOrEmpty(productId))
            {
                return 0;
            }

            for (int i = 0; i < CoinGrants.Length; i++)
            {
                if (CoinGrants[i].productId == productId)
                {
                    return CoinGrants[i].coinAmount;
                }
            }

            return 0;
        }

        private void PublishPurchaseResult(string productId, bool success)
        {
            EventBus.Publish(new OnPurchaseCompleted
            {
                productId = productId,
                success = success
            });
        }

        #endregion

        #region Private Methods — Simulation

#if !UNITY_IAP
        private void CacheSimulatedPrices()
        {
            _cachedPrices[PRODUCT_COIN_500] = "$0.99";
            _cachedPrices[PRODUCT_COIN_1200] = "$1.99";
            _cachedPrices[PRODUCT_COIN_3000] = "$4.99";
            _cachedPrices[PRODUCT_COIN_8000] = "$9.99";
            _cachedPrices[PRODUCT_COIN_20000] = "$19.99";
            _cachedPrices[PRODUCT_STARTER_PACK] = "$2.99";
            _cachedPrices[PRODUCT_WEEKEND_BUNDLE] = "$4.99";
            _cachedPrices[PRODUCT_AD_REMOVE] = "$3.99";
            _cachedPrices[PRODUCT_HEART_REFILL] = "$0.99";
        }

        private string GetSimulatedPrice(string productId)
        {
            if (_cachedPrices.TryGetValue(productId, out string price))
            {
                return price;
            }
            return "$?.??";
        }
#endif

        #endregion
    }
}
