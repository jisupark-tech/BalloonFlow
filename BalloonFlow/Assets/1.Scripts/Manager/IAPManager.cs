#if UNITY_IAP
using UnityEngine.Purchasing;
using UnityEngine.Purchasing.Extension;
#endif

using System.Collections.Generic;
using UnityEngine;

namespace BalloonFlow
{
    /// <summary>
    /// Unity IAP wrapper. 상품 목록과 보상은 Firestore /products (ShopCatalogService) 가 진실 소스.
    /// ShopCatalogService 로드 완료 후 자동 init. UNITY_IAP 미정의 시 simulation 모드.
    /// </summary>
    public class IAPManager : Singleton<IAPManager>
#if UNITY_IAP
        , IDetailedStoreListener
#endif
    {
        private const string LOG_TAG = "[IAPManager]";

        // 카테고리: 문서/seed 와 일치 (coin / bundle / noads / offer)
        public const string CAT_NOADS = "noads";

        private bool _isInitialized;
        private bool _initStarted;
        private readonly Dictionary<string, string> _cachedPrices = new Dictionary<string, string>();

#if UNITY_IAP
        private IStoreController    _storeController;
        private IExtensionProvider  _extensionProvider;
#endif

        /// <summary>광고 영구 제거 여부. Firestore UserData.removedAds 가 진실 소스. 미준비 시 PlayerPrefs fallback.</summary>
        public bool AdsRemoved
        {
            get
            {
                if (UserDataService.HasInstance && UserDataService.Instance.IsReady
                    && UserDataService.Instance.CurrentUser != null)
                    return UserDataService.Instance.CurrentUser.removedAds;
                return PlayerPrefs.GetInt("BalloonFlow_AdRemoved", 0) == 1;
            }
        }

        protected override void OnSingletonAwake()
        {
            TryStartInit();
        }

        private void TryStartInit()
        {
            if (_initStarted || _isInitialized) return;

            if (!ShopCatalogService.HasInstance)
            {
                // 부트 순서상 거의 발생하지 않음 — SdkBootstrap 이 둘 다 같이 attach
                Debug.LogWarning($"{LOG_TAG} ShopCatalogService 미준비 — 다음 프레임 재시도");
                return;
            }

            if (ShopCatalogService.Instance.IsLoaded)
            {
                StartInit(ShopCatalogService.Instance.All);
                return;
            }

            ShopCatalogService.Instance.OnCatalogLoaded += HandleCatalogLoaded;
        }

        private void HandleCatalogLoaded()
        {
            if (ShopCatalogService.HasInstance)
                ShopCatalogService.Instance.OnCatalogLoaded -= HandleCatalogLoaded;
            StartInit(ShopCatalogService.Instance.All);
        }

        private void StartInit(IReadOnlyList<ShopProductDoc> catalog)
        {
            if (_initStarted || _isInitialized) return;
            _initStarted = true;

#if UNITY_IAP
            var builder = ConfigurationBuilder.Instance(StandardPurchasingModule.Instance());
            int registered = 0;
            foreach (var p in catalog)
            {
                if (string.IsNullOrEmpty(p.productId)) continue;
                builder.AddProduct(p.productId, ResolveProductType(p));
                registered++;
            }
            UnityPurchasing.Initialize(this, builder);
            Debug.Log($"{LOG_TAG} Unity IAP init started — {registered} products registered.");
#else
            // Simulation: catalog 가격 그대로 캐시. 결제 시 보상 즉시 지급
            _cachedPrices.Clear();
            foreach (var p in catalog)
            {
                if (!string.IsNullOrEmpty(p.productId))
                    _cachedPrices[p.productId] = $"${p.priceUsd:F2}";
            }
            _isInitialized = true;
            Debug.Log($"{LOG_TAG} Sim 모드 init — {catalog.Count} products cached.");
#endif
        }

#if UNITY_IAP
        private static ProductType ResolveProductType(ShopProductDoc p)
        {
            // 영구 광고 제거 / 1회 한정 상품 → NonConsumable. 나머지 → Consumable
            if (p.category == CAT_NOADS) return ProductType.NonConsumable;
            if (p.maxPurchases == 1)     return ProductType.NonConsumable;
            return ProductType.Consumable;
        }
#endif

        public bool IsInitialized() => _isInitialized;

        /// <summary>구매 시도. ShopCatalogService 의 상품 ID 사용 (full Store SKU).</summary>
        public void PurchaseProduct(string productId)
        {
            if (string.IsNullOrEmpty(productId))
            {
                Debug.LogWarning($"{LOG_TAG} PurchaseProduct null/empty");
                return;
            }

            // 1회 한정 차단 (UserData.purchasedOnce)
            if (IsLimitedAndAlreadyPurchased(productId))
            {
                Debug.LogWarning($"{LOG_TAG} {productId} 는 1회 한정 — 이미 구매됨");
                PublishPurchaseResult(productId, false);
                return;
            }

#if UNITY_IAP
            if (!_isInitialized)
            {
                Debug.LogWarning($"{LOG_TAG} 미초기화 상태 — 구매 불가");
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
                Debug.LogWarning($"{LOG_TAG} {productId} 미등록/구매불가");
                PublishPurchaseResult(productId, false);
            }
#else
            Debug.Log($"{LOG_TAG} Sim — {productId} 구매");
            ProcessPurchaseReward(productId);
            PublishPurchaseResult(productId, true);
#endif
        }

        public void RestorePurchases()
        {
#if UNITY_IAP
            if (!_isInitialized)
            {
                Debug.LogWarning($"{LOG_TAG} 미초기화 — restore 불가");
                return;
            }

            if (Application.platform == RuntimePlatform.IPhonePlayer ||
                Application.platform == RuntimePlatform.OSXPlayer)
            {
                var apple = _extensionProvider.GetExtension<IAppleExtensions>();
                apple.RestoreTransactions((result, error) =>
                {
                    if (result) Debug.Log($"{LOG_TAG} Restore 성공");
                    else        Debug.LogWarning($"{LOG_TAG} Restore 실패: {error}");
                });
            }
            else
            {
                Debug.Log($"{LOG_TAG} Restore 불필요 (Google Play 자동)");
            }
#else
            Debug.Log($"{LOG_TAG} Sim restore");
#endif
        }

        public string GetProductPrice(string productId)
        {
            if (string.IsNullOrEmpty(productId)) return "$?.??";

#if UNITY_IAP
            if (!_isInitialized || _storeController == null)
                return _cachedPrices.TryGetValue(productId, out var cached) ? cached : "$?.??";

            Product product = _storeController.products.WithID(productId);
            if (product != null && product.availableToPurchase)
                return product.metadata.localizedPriceString;

            return _cachedPrices.TryGetValue(productId, out var fallback) ? fallback : "$?.??";
#else
            return _cachedPrices.TryGetValue(productId, out var p) ? p : "$?.??";
#endif
        }

        private bool IsLimitedAndAlreadyPurchased(string productId)
        {
            var doc = ShopCatalogService.HasInstance ? ShopCatalogService.Instance.Get(productId) : null;
            if (doc == null || doc.maxPurchases != 1) return false;

            if (UserDataService.HasInstance && UserDataService.Instance.IsReady
                && UserDataService.Instance.CurrentUser != null
                && UserDataService.Instance.CurrentUser.purchasedOnce.TryGetValue(productId, out var purchased))
                return purchased;
            return false;
        }

#if UNITY_IAP
        public void OnInitialized(IStoreController controller, IExtensionProvider extensions)
        {
            _storeController = controller;
            _extensionProvider = extensions;
            _isInitialized = true;

            foreach (Product product in controller.products.all)
            {
                if (product.availableToPurchase)
                    _cachedPrices[product.definition.id] = product.metadata.localizedPriceString;
            }
            Debug.Log($"{LOG_TAG} Store 초기화 완료 — {_cachedPrices.Count} products");
        }

        public void OnInitializeFailed(InitializationFailureReason error)
            => Debug.LogError($"{LOG_TAG} Store init 실패: {error}");

        public void OnInitializeFailed(InitializationFailureReason error, string message)
            => Debug.LogError($"{LOG_TAG} Store init 실패: {error} — {message}");

        public PurchaseProcessingResult ProcessPurchase(PurchaseEventArgs args)
        {
            string productId = args.purchasedProduct.definition.id;
            Debug.Log($"{LOG_TAG} 구매 성공: {productId}");

            ProcessPurchaseReward(productId);
            PublishPurchaseResult(productId, true);

            // TODO: Phase 3 — Cloud Functions validatePurchase 호출 후 보상 지급으로 라우팅 변경 예정
            return PurchaseProcessingResult.Complete;
        }

        public void OnPurchaseFailed(Product product, PurchaseFailureReason failureReason)
        {
            Debug.LogWarning($"{LOG_TAG} 구매 실패: {product.definition.id} — {failureReason}");
            PublishPurchaseResult(product.definition.id, false);
        }

        public void OnPurchaseFailed(Product product, PurchaseFailureDescription failureDescription)
        {
            Debug.LogWarning($"{LOG_TAG} 구매 실패: {product.definition.id} — {failureDescription.message}");
            PublishPurchaseResult(product.definition.id, false);
        }
#endif

        /// <summary>ShopCatalogService 의 보상 정의를 읽어 매니저에 위임. 클라 단독 처리 (Phase 3 전).</summary>
        private void ProcessPurchaseReward(string productId)
        {
            var doc = ShopCatalogService.HasInstance ? ShopCatalogService.Instance.Get(productId) : null;
            if (doc == null)
            {
                Debug.LogWarning($"{LOG_TAG} {productId} 카탈로그 lookup 실패 — 보상 지급 안 함");
                return;
            }

            var r = doc.rewards;
            if (r != null)
            {
                if (r.coins > 0 && CurrencyManager.HasInstance)
                    CurrencyManager.Instance.AddCoins(r.coins, CurrencyManager.CoinSource.IAP);

                if (r.boosters != null && BoosterManager.HasInstance)
                {
                    if (r.boosters.hand    > 0) BoosterManager.Instance.AddBooster(BoosterManager.HAND,    r.boosters.hand);
                    if (r.boosters.shuffle > 0) BoosterManager.Instance.AddBooster(BoosterManager.SHUFFLE, r.boosters.shuffle);
                    if (r.boosters.zap     > 0) BoosterManager.Instance.AddBooster(BoosterManager.ZAP,     r.boosters.zap);
                }

                if (r.infiniteHeartsSeconds > 0 && LifeManager.HasInstance)
                    LifeManager.Instance.ActivateInfiniteHearts(r.infiniteHeartsSeconds);

                if (r.removeAds)
                {
                    PlayerPrefs.SetInt("BalloonFlow_AdRemoved", 1);
                    PlayerPrefs.Save();
                    if (UserDataService.HasInstance && UserDataService.Instance.IsReady)
                        UserDataService.Instance.SetRemovedAds(true);
                }
            }

            // 1회 한정 마킹 (UserData.purchasedOnce)
            if (doc.maxPurchases == 1
                && UserDataService.HasInstance && UserDataService.Instance.IsReady)
            {
                UserDataService.Instance.SetPurchasedOnce(productId, true);
            }

            // NPU 해제
            if (UserDataService.HasInstance && UserDataService.Instance.IsReady)
                UserDataService.Instance.MarkPaying();
        }

        private static void PublishPurchaseResult(string productId, bool success)
        {
            EventBus.Publish(new OnPurchaseCompleted { productId = productId, success = success });
        }
    }
}
