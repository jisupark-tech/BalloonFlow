#if UNITY_IAP
using UnityEngine.Purchasing;
using UnityEngine.Purchasing.Extension;
#endif

using System.Collections;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using BalloonFlow.Analytics;

namespace BalloonFlow
{
    /// <summary>
    /// Unity IAP wrapper. 상품 목록과 보상은 Firestore /products (ShopCatalogService) 가 진실 소스.
    /// ShopCatalogService 로드 완료 후 자동 init. UNITY_IAP 미정의 시 simulation 모드.
    /// [2026-07-06 IAP_SERVER_VALIDATE] 방식 B — 디바이스 빌드는 ProcessPurchase 에서 Pending 반환 후
    ///   Cloud Function(validatePurchase) 검증 통과 시에만 보상 지급 + ConfirmPendingPurchase.
    ///   검증 불가(네트워크 등) 시 소비 보류 → 다음 실행 시 스토어가 재전달해 재검증(보상 소실 없음).
    ///   Editor 는 FakeStore 라 purchaseToken 이 없어 기존 로컬 지급 유지.
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
        private bool _catalogSubscribed;
        private const float PurchaseInitWaitTimeoutSeconds = 20f;
        // ROLLBACK_IAP_INIT_RETRY_20260615: init 실패 시 재시도용. 기존엔 1회 실패하면 _initStarted=true 로 남아
        //   세션 내 IAP 영구 불능이었음(에디터 FakeStore 는 실패가 없어 미노출). 아래 OnInitializeFailed 참조.
        private int _initRetryCount;
        private const int MAX_INIT_RETRIES = 3;
        private const float INIT_RETRY_DELAY_SECONDS = 3f;
        private readonly HashSet<string> _pendingInitPurchases = new HashSet<string>();
        private readonly HashSet<string> _userInitiatedPurchases = new HashSet<string>();
        private readonly Dictionary<string, string> _cachedPrices = new Dictionary<string, string>();

#if UNITY_IAP
        private IStoreController    _storeController;
        private IExtensionProvider  _extensionProvider;
        private readonly Dictionary<string, ProductType> _registeredProductTypes = new Dictionary<string, ProductType>();

        // [2026-07-06 IAP_SERVER_VALIDATE] 영수증 서버 검증 (방식 B).
        // 엔드포인트는 AnalyticsManager.INGEST_URL 과 동일 배포 규칙 — 배포 후 실제 URL 확인/교체.
        private const string VALIDATE_URL =
            "https://us-central1-balloonloop-d855d.cloudfunctions.net/validatePurchase";
        private const int   VALIDATE_MAX_ATTEMPTS        = 3;
        private const float VALIDATE_RETRY_DELAY_SECONDS = 5f;
        private const float VALIDATE_AUTH_WAIT_TIMEOUT   = 30f;
        // 스토어가 같은 미소비 구매를 세션 내 재전달할 때 검증 코루틴 중복 기동 방지.
        private readonly HashSet<string> _validatingTokens = new HashSet<string>();
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

            // 카탈로그 로드 완료 시 init. 중복 구독 방지(미초기화 구매 시 TryStartInit 재호출될 수 있음).
            if (!_catalogSubscribed)
            {
                ShopCatalogService.Instance.OnCatalogLoaded += HandleCatalogLoaded;
                _catalogSubscribed = true;
            }
        }

        private void HandleCatalogLoaded()
        {
            if (ShopCatalogService.HasInstance)
                ShopCatalogService.Instance.OnCatalogLoaded -= HandleCatalogLoaded;
            _catalogSubscribed = false;
            StartInit(ShopCatalogService.Instance.All);
        }

        private void StartInit(IReadOnlyList<ShopProductDoc> catalog)
        {
            if (_initStarted || _isInitialized) return;
            _initStarted = true;

#if UNITY_IAP
            var builder = ConfigurationBuilder.Instance(StandardPurchasingModule.Instance());
            int registered = 0;
            _registeredProductTypes.Clear();
            Debug.Log($"{LOG_TAG} IAP catalog received. count={(catalog == null ? 0 : catalog.Count)} platform={Application.platform} bundleId={Application.identifier}");
            foreach (var p in catalog)
            {
                if (string.IsNullOrEmpty(p.productId)) continue;
                ProductType productType = ResolveProductType(p);
                builder.AddProduct(p.productId, productType);
                _registeredProductTypes[p.productId] = productType;
                registered++;
                Debug.Log($"{LOG_TAG} IAP register product id={p.productId} category={p.category} type={productType} priceUsd={p.priceUsd:F2} maxPurchases={p.maxPurchases}");
            }
            // ROLLBACK_IAP_INIT_FULL_LOG_20260619: UnityPurchasing type-init 예외의 inner/stack 전체를 로깅(진짜 원인 확정용).
            //   기존엔 ShopCatalogService 가 e.Message 만 찍어 "type initializer ... threw" 까지만 보이고 원인이 안 보였음.
            try
            {
                UnityPurchasing.Initialize(this, builder);
                Debug.Log($"{LOG_TAG} Unity IAP init started — {registered} products registered.");
            }
            catch (System.Exception initEx)
            {
                Debug.LogError($"{LOG_TAG} ★UnityPurchasing.Initialize 예외(full)★: {initEx}");
                if (initEx.InnerException != null)
                    Debug.LogError($"{LOG_TAG} ★INNER★: {initEx.InnerException}");
                _initStarted = false; // 재시도 허용
                throw;                // 기존 흐름 유지 (상위에서 캐치/처리)
            }
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
                Debug.LogWarning($"{LOG_TAG} 미초기화 상태 — 구매 불가. 카탈로그/IAP 재초기화 시도(다음 구매부터 가능).");
                // 자가 복구: 카탈로그 미로드(콜드스타트 일시 PermissionDenied 등)가 결제 Fail 의 주 원인.
                // 재fetch 성공 시 OnCatalogLoaded → StartInit → 이후 구매 가능. 이번 구매는 실패 처리.
                if (ShopCatalogService.HasInstance) ShopCatalogService.Instance.RetryFetch();
                TryStartInit();
                MarkUserInitiatedPurchase(productId);
                if (_pendingInitPurchases.Add(productId))
                    StartCoroutine(PurchaseAfterInit(productId));
                else
                    Debug.LogWarning($"{LOG_TAG} Purchase already waiting for IAP init: {productId}");
                return;
            }

            Product product = _storeController.products.WithID(productId);
            if (product != null && product.availableToPurchase)
            {
                LogProductDetails("PurchaseProduct-ready", product);
                MarkUserInitiatedPurchase(productId);
                _storeController.InitiatePurchase(product);
            }
            else
            {
                Debug.LogWarning($"{LOG_TAG} {productId} not available for purchase.");
                LogProductDetails("PurchaseProduct-unavailable", product, productId);
                DumpIapProductDetails("PurchaseProduct-unavailable");
                Debug.LogWarning($"{LOG_TAG} {productId} 미등록/구매불가");
                PublishPurchaseResult(productId, false);
            }
#else
            Debug.Log($"{LOG_TAG} Sim — {productId} 구매");
            ProcessPurchaseReward(productId, transactionId: "", verified: false);
            PublishPurchaseResult(productId, true);
#endif
        }

#if UNITY_IAP
        private IEnumerator PurchaseAfterInit(string productId)
        {
            float t = 0f;
            while (t < PurchaseInitWaitTimeoutSeconds && !_isInitialized)
            {
                if (ShopCatalogService.HasInstance) ShopCatalogService.Instance.RetryFetch();
                TryStartInit();
                t += Time.unscaledDeltaTime;
                yield return null;
            }

            _pendingInitPurchases.Remove(productId);

            if (!_isInitialized)
            {
                Debug.LogWarning($"{LOG_TAG} IAP init wait timed out. Purchase failed: {productId}");
                ClearUserInitiatedPurchase(productId);
                PublishPurchaseResult(productId, false);
                yield break;
            }

            PurchaseProduct(productId);
        }
#endif

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

        // ROLLBACK_SHOP_KRW_DISPLAY_20260715: 표시 가격 — 원화(KRW)는 "5,800 KRW" 형식(금액+통화코드),
        //   그 외 통화는 스토어 기본 문자열($4.99, €3,99 …). 스토어 계정 지역 기준(앱 언어 무관).
        //   미초기화/미조회 시 GetProductPrice 폴백($priceUsd).
        public string GetDisplayPrice(string productId)
        {
            if (string.IsNullOrEmpty(productId)) return "$?.??";
#if UNITY_IAP
            if (_isInitialized && _storeController != null)
            {
                Product product = _storeController.products.WithID(productId);
                if (product != null && product.availableToPurchase && product.metadata != null)
                {
                    var m = product.metadata;
                    if (m.isoCurrencyCode == "KRW")
                        return $"{m.localizedPrice:N0} KRW";   // 예: 5,800 KRW (원화는 소수점 없음)
                    return m.localizedPriceString;             // 그 외: 스토어 기본($4.99 등)
                }
            }
#endif
            return GetProductPrice(productId);
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
            Debug.Log($"{LOG_TAG} Store init completed - {_cachedPrices.Count} available products cached.");
            DumpIapProductDetails("OnInitialized");
            RestoreOwnedEntitlements();
        }

        private void DumpIapProductDetails(string context)
        {
            if (_storeController == null)
            {
                Debug.LogWarning($"{LOG_TAG} IAP ProductDetails dump skipped ({context}) - storeController is null. registered={_registeredProductTypes.Count}");
                DumpRegisteredProducts(context);
                return;
            }

            Product[] products = _storeController.products.all;
            int total = products != null ? products.Length : 0;
            int available = 0;
            int unavailable = 0;

            Debug.Log($"{LOG_TAG} IAP ProductDetails dump start ({context}) platform={Application.platform} bundleId={Application.identifier} registered={_registeredProductTypes.Count} returned={total}");

            foreach (var pair in _registeredProductTypes)
            {
                Product product = _storeController.products.WithID(pair.Key);
                if (product != null && product.availableToPurchase) available++;
                else unavailable++;

                LogProductDetails($"registered type={pair.Value}", product, pair.Key);
            }

            Debug.Log($"{LOG_TAG} IAP ProductDetails dump end ({context}) available={available} unavailable={unavailable}");
        }

        private void DumpRegisteredProducts(string context)
        {
            if (_registeredProductTypes.Count == 0)
            {
                Debug.LogWarning($"{LOG_TAG} IAP registered products empty ({context}). ShopCatalogService may not have loaded.");
                return;
            }

            var sb = new StringBuilder();
            sb.Append(LOG_TAG).Append(" IAP registered products (").Append(context).Append("): ");
            bool first = true;
            foreach (var pair in _registeredProductTypes)
            {
                if (!first) sb.Append(", ");
                first = false;
                sb.Append(pair.Key).Append(":").Append(pair.Value);
            }
            Debug.Log(sb.ToString());
        }

        private void LogProductDetails(string context, Product product, string expectedId = null)
        {
            if (product == null)
            {
                Debug.LogWarning($"{LOG_TAG} IAP ProductDetails {context}: id={expectedId ?? "<null>"} product=null");
                return;
            }

            ProductDefinition definition = product.definition;
            ProductMetadata metadata = product.metadata;
            string id = definition != null ? definition.id : "<null>";
            string storeSpecificId = definition != null ? definition.storeSpecificId : "<null>";
            string type = definition != null ? definition.type.ToString() : "<null>";
            string price = metadata != null ? metadata.localizedPriceString : "<null>";
            string currency = metadata != null ? metadata.isoCurrencyCode : "<null>";
            string title = metadata != null ? metadata.localizedTitle : "<null>";
            string transactionId = string.IsNullOrEmpty(product.transactionID) ? "<empty>" : product.transactionID;

            Debug.Log($"{LOG_TAG} IAP ProductDetails {context}: id={id} storeId={storeSpecificId} type={type} available={product.availableToPurchase} hasReceipt={product.hasReceipt} price='{price}' currency='{currency}' title='{title}' tx='{transactionId}'");
        }

        private void RestoreOwnedEntitlements()
        {
            if (_storeController == null || !ShopCatalogService.HasInstance) return;

            foreach (Product product in _storeController.products.all)
            {
                if (product == null || !product.hasReceipt) continue;
                string productId = product.definition != null ? product.definition.id : "";
                var doc = ShopCatalogService.Instance.Get(productId);
                if (!IsRemoveAdsEntitlement(productId, doc)) continue;

                ApplyNoAdsRestoreWithoutRewardPopup(productId, doc, "restore-owned-entitlement");
            }
        }

        public void OnInitializeFailed(InitializationFailureReason error)
            => HandleInitializeFailed(error, null);

        public void OnInitializeFailed(InitializationFailureReason error, string message)
            => HandleInitializeFailed(error, message);

        // ROLLBACK_IAP_INIT_RETRY_20260615: START
        // 기존엔 init 실패 시 LogError 만 하고 _initStarted=true 가 그대로 남아 TryStartInit/StartInit 이 영구 no-op
        //   → 세션 내 IAP 영구 불능(모든 결제 실패). 디바이스 콜드부팅 Billing warmup 지연 / 네트워크 순간끊김 /
        //   AAB 게시 직후 전파 지연 등 '일시적' 실패에도 복구 불가였다. 실패 시 _initStarted 리셋 + 제한 재시도.
        //   (PurchasingUnavailable = Play 스토어 자체 부재 → 영구 불가라 재시도 무의미, skip.)
        //   롤백: 이 핸들러/RetryInit 코루틴/관련 필드 제거하고 위 두 메서드를 LogError 단문으로 환원.
        private void HandleInitializeFailed(InitializationFailureReason error, string message)
        {
            Debug.LogError($"{LOG_TAG} Store init 실패: {error}{(string.IsNullOrEmpty(message) ? "" : $" — {message}")}");

            _initStarted = false;   // 재초기화 가능하도록 리셋 (이게 없으면 영구 불능)

            if (error == InitializationFailureReason.PurchasingUnavailable)
            {
                Debug.LogWarning($"{LOG_TAG} PurchasingUnavailable — 재시도 안 함(스토어/결제 불가 기기·환경).");
                return;
            }
            if (_initRetryCount >= MAX_INIT_RETRIES)
            {
                Debug.LogWarning($"{LOG_TAG} init 재시도 {MAX_INIT_RETRIES}회 모두 실패 — 중단. (구매 시 다시 시도됨)");
                return;
            }
            _initRetryCount++;
            Debug.Log($"{LOG_TAG} init 재시도 예약 {_initRetryCount}/{MAX_INIT_RETRIES} ({INIT_RETRY_DELAY_SECONDS}s 후)");
            StartCoroutine(RetryInitAfterDelay());
        }

        private IEnumerator RetryInitAfterDelay()
        {
            yield return new WaitForSecondsRealtime(INIT_RETRY_DELAY_SECONDS);
            if (!_isInitialized) TryStartInit();
        }
        // ROLLBACK_IAP_INIT_RETRY_20260615: END

        public PurchaseProcessingResult ProcessPurchase(PurchaseEventArgs args)
        {
            string productId     = args.purchasedProduct.definition.id;
            string transactionId = args.purchasedProduct.transactionID ?? "";
            Debug.Log($"{LOG_TAG} 구매 성공: {productId} txId={transactionId}");

            bool userInitiated = ConsumeUserInitiatedPurchase(productId);
            var doc = ShopCatalogService.HasInstance ? ShopCatalogService.Instance.Get(productId) : null;
            bool removeAdsEntitlement = IsRemoveAdsEntitlement(productId, doc);
            Debug.Log($"{LOG_TAG} ProcessPurchase route productId={productId} userInitiated={userInitiated} category={(doc != null ? doc.category : "<null>")} removeAdsReward={(doc != null && doc.rewards != null && doc.rewards.removeAds)} isRemoveAdsEntitlement={removeAdsEntitlement}");

            if (!userInitiated && removeAdsEntitlement)
            {
                ApplyNoAdsRestoreWithoutRewardPopup(productId, doc, "restore-process-purchase");
                return PurchaseProcessingResult.Complete;
            }

#if UNITY_EDITOR
            // Editor FakeStore — purchaseToken 이 없어 서버 검증 불가. 기존 로컬 지급(미검증) 유지.
            ProcessPurchaseReward(productId, transactionId, verified: false);
            PublishPurchaseResult(productId, true);
            return PurchaseProcessingResult.Complete;
#else
            // [2026-07-06 IAP_SERVER_VALIDATE] 검증 완료 전 Complete 반환 금지 — Complete 는 트랜잭션을
            // 즉시 소비 확정해 위조/검증실패 시 복구 불가. Pending 유지 후 검증 코루틴이 확정한다.
            string purchaseToken = ExtractGooglePurchaseToken(args.purchasedProduct.receipt, out string orderId);
            if (string.IsNullOrEmpty(purchaseToken))
            {
                Debug.LogWarning($"{LOG_TAG} purchaseToken 파싱 실패 — 소비 보류(다음 실행 재전달). productId={productId}");
                if (userInitiated) PublishPurchaseResult(productId, false);
                return PurchaseProcessingResult.Pending;
            }
            if (!_validatingTokens.Add(purchaseToken))
            {
                Debug.Log($"{LOG_TAG} 동일 토큰 검증 진행 중 — 재전달 무시. productId={productId}");
                return PurchaseProcessingResult.Pending;
            }
            StartCoroutine(ValidateAndGrantRoutine(args.purchasedProduct, productId, purchaseToken,
                string.IsNullOrEmpty(orderId) ? transactionId : orderId, userInitiated));
            return PurchaseProcessingResult.Pending;
#endif
        }

#if !UNITY_EDITOR
        /// <summary>
        /// Cloud Function(validatePurchase) 검증 → 통과 시 보상 지급 + ConfirmPendingPurchase.
        /// 검증 불가(네트워크/서버 일시 오류) 시 confirm 하지 않음 — 스토어가 다음 실행 시 재전달.
        /// </summary>
        private IEnumerator ValidateAndGrantRoutine(
            Product product, string productId, string purchaseToken, string orderId, bool userInitiated)
        {
            try
            {
                // 1) Firebase Auth ID 토큰 대기 — UserDataService 가 부트에서 익명 로그인 수행.
                string idToken = null;
                float waited = 0f;
                while (waited < VALIDATE_AUTH_WAIT_TIMEOUT)
                {
                    if (FirebaseManager.HasInstance && FirebaseManager.Instance.IsReady
                        && FirebaseManager.Instance.Auth != null
                        && FirebaseManager.Instance.Auth.CurrentUser != null)
                    {
                        var tokenTask = FirebaseManager.Instance.Auth.CurrentUser.TokenAsync(false);
                        while (!tokenTask.IsCompleted) yield return null;
                        if (!tokenTask.IsFaulted && !tokenTask.IsCanceled && !string.IsNullOrEmpty(tokenTask.Result))
                        {
                            idToken = tokenTask.Result;
                            break;
                        }
                    }
                    waited += 1f;
                    yield return new WaitForSecondsRealtime(1f);
                }

                if (idToken == null)
                {
                    Debug.LogWarning($"{LOG_TAG} 검증용 Auth 토큰 확보 실패 — Pending 유지(다음 실행 재검증). productId={productId}");
                    if (userInitiated) PublishPurchaseResult(productId, false);
                    yield break;
                }

                string body = JsonConvert.SerializeObject(new { productId, purchaseToken, orderId });

                for (int attempt = 1; attempt <= VALIDATE_MAX_ATTEMPTS; attempt++)
                {
                    long code;
                    bool networkErr;
                    string responseText;
                    using (var req = new UnityWebRequest(VALIDATE_URL, "POST"))
                    {
                        req.uploadHandler   = new UploadHandlerRaw(Encoding.UTF8.GetBytes(body));
                        req.downloadHandler = new DownloadHandlerBuffer();
                        req.SetRequestHeader("Content-Type", "application/json");
                        req.SetRequestHeader("Authorization", "Bearer " + idToken);
                        req.timeout = 30;

                        yield return req.SendWebRequest();

                        code = req.responseCode;
                        networkErr = req.result == UnityWebRequest.Result.ConnectionError
                                  || req.result == UnityWebRequest.Result.DataProcessingError;
                        responseText = req.downloadHandler.text;
                    }

                    if (!networkErr && code == 200)
                    {
                        bool valid = false, definitive = false, alreadyProcessed = false;
                        string serverOrderId = orderId;
                        try
                        {
                            var resp = JObject.Parse(responseText);
                            valid            = resp.Value<bool?>("valid") ?? false;
                            definitive       = resp.Value<bool?>("definitive") ?? false;
                            alreadyProcessed = resp.Value<bool?>("alreadyProcessed") ?? false;
                            string oid = resp.Value<string>("orderId");
                            if (!string.IsNullOrEmpty(oid)) serverOrderId = oid;
                        }
                        catch (System.Exception e)
                        {
                            Debug.LogWarning($"{LOG_TAG} 검증 응답 파싱 실패: {e.Message}");
                        }

                        if (valid)
                        {
                            if (alreadyProcessed)
                            {
                                // 이전 세션에서 지급 후 confirm 전 종료 → 재전달 케이스. 중복 지급 방지, 소비만 확정.
                                Debug.Log($"{LOG_TAG} 검증 OK(이미 지급됨) — confirm only. productId={productId}");
                            }
                            else
                            {
                                ProcessPurchaseReward(productId, serverOrderId, verified: true);
                                PublishPurchaseResult(productId, true);
                            }
                            ConfirmPending(product);
                            yield break;
                        }

                        if (definitive)
                        {
                            // 위조/취소 — 지급 없이 소비 확정(재전달 루프 방지).
                            Debug.LogWarning($"{LOG_TAG} 영수증 검증 거부 — 지급 안 함. productId={productId} resp={responseText}");
                            ConfirmPending(product);
                            if (userInitiated) PublishPurchaseResult(productId, false);
                            yield break;
                        }
                        // valid=false & definitive=false (결제 보류 등) — confirm 보류, 아래 재시도.
                    }
                    else if (!networkErr && code == 401)
                    {
                        // stale 토큰 가능성 — forceRefresh 후 재시도 (Firestore SDK quirk 과 동일 패턴).
                        if (FirebaseManager.HasInstance && FirebaseManager.Instance.Auth?.CurrentUser != null)
                        {
                            var refreshTask = FirebaseManager.Instance.Auth.CurrentUser.TokenAsync(true);
                            while (!refreshTask.IsCompleted) yield return null;
                            if (!refreshTask.IsFaulted && !refreshTask.IsCanceled && !string.IsNullOrEmpty(refreshTask.Result))
                                idToken = refreshTask.Result;
                        }
                    }
                    // 그 외 4xx/5xx/네트워크 오류 — 재시도.

                    if (attempt < VALIDATE_MAX_ATTEMPTS)
                        yield return new WaitForSecondsRealtime(VALIDATE_RETRY_DELAY_SECONDS * attempt);
                }

                // 세션 내 검증 실패 — confirm 보류. 다음 실행 시 스토어가 ProcessPurchase 를 재전달해
                // 재검증되므로 결제 자체가 소실되지는 않음 (유저에겐 이번 시도 실패로 안내).
                Debug.LogWarning($"{LOG_TAG} 영수증 검증 {VALIDATE_MAX_ATTEMPTS}회 실패 — Pending 유지. productId={productId}");
                if (userInitiated) PublishPurchaseResult(productId, false);
            }
            finally
            {
                _validatingTokens.Remove(purchaseToken);
            }
        }

        private void ConfirmPending(Product product)
        {
            if (_storeController != null && product != null)
                _storeController.ConfirmPendingPurchase(product);
        }

        /// <summary>
        /// Unity IAP 영수증(JSON wrapper)에서 Google Play purchaseToken/orderId 추출.
        /// 구조: { Store, TransactionID, Payload } → Payload{ json, signature } → json{ purchaseToken, orderId … }
        /// </summary>
        private static string ExtractGooglePurchaseToken(string unityReceipt, out string orderId)
        {
            orderId = "";
            if (string.IsNullOrEmpty(unityReceipt)) return "";
            try
            {
                var wrapper = JObject.Parse(unityReceipt);
                if (!string.Equals(wrapper.Value<string>("Store"), "GooglePlay", System.StringComparison.OrdinalIgnoreCase))
                    return "";
                string payload = wrapper.Value<string>("Payload");
                if (string.IsNullOrEmpty(payload)) return "";
                var payloadObj = JObject.Parse(payload);
                string innerJson = payloadObj.Value<string>("json");
                if (string.IsNullOrEmpty(innerJson)) return "";
                var purchase = JObject.Parse(innerJson);
                orderId = purchase.Value<string>("orderId") ?? "";
                return purchase.Value<string>("purchaseToken") ?? "";
            }
            catch (System.Exception e)
            {
                Debug.LogWarning($"[IAPManager] 영수증 파싱 실패: {e.Message}");
                return "";
            }
        }
#endif

        public void OnPurchaseFailed(Product product, PurchaseFailureReason failureReason)
        {
            string productId = product != null && product.definition != null ? product.definition.id : "";
            Debug.LogWarning($"{LOG_TAG} Purchase failed: {productId} reason={failureReason}");
            LogProductDetails($"OnPurchaseFailed reason={failureReason}", product, productId);
            ClearUserInitiatedPurchase(productId);
            if (product == null || product.definition == null)
            {
                PublishPurchaseResult(productId, false);
                return;
            }
            Debug.LogWarning($"{LOG_TAG} 구매 실패: {product.definition.id} — {failureReason}");
            PublishPurchaseResult(productId, false);
        }

        public void OnPurchaseFailed(Product product, PurchaseFailureDescription failureDescription)
        {
            string productId = product != null && product.definition != null ? product.definition.id : "";
            Debug.LogWarning($"{LOG_TAG} Purchase failed: {productId} reason={failureDescription.reason} message={failureDescription.message}");
            LogProductDetails($"OnPurchaseFailed reason={failureDescription.reason}", product, productId);
            ClearUserInitiatedPurchase(productId);
            if (product == null || product.definition == null)
            {
                PublishPurchaseResult(productId, false);
                return;
            }
            Debug.LogWarning($"{LOG_TAG} 구매 실패: {product.definition.id} — {failureDescription.message}");
            PublishPurchaseResult(productId, false);
        }
#endif

        /// <summary>
        /// ShopCatalogService 의 보상 정의를 읽어 매니저에 위임.
        /// 코인은 CurrencyManager.AddCoins(suppressEvent=true) 로 silent 지급 — UILobby 즉시 갱신 차단.
        /// 후속 OnPurchaseRewardGranted 이벤트로 PurchaseRewardEffect 가 success popup + FxGold 연출 처리.
        /// transactionId 는 purchase_event 의 receipt_id 컬럼에 그대로 들어감 (sim 모드면 "").
        /// verified: 서버(validatePurchase) 검증 통과 여부 — purchase_event.is_verified + af_purchase 발행 조건.
        /// </summary>
        private void ProcessPurchaseReward(string productId, string transactionId, bool verified)
        {
            var doc = ShopCatalogService.HasInstance ? ShopCatalogService.Instance.Get(productId) : null;
            if (doc == null)
            {
                Debug.LogWarning($"{LOG_TAG} {productId} 카탈로그 lookup 실패 — 보상 지급 안 함");
                return;
            }

            // Snapshot 누적 갱신 → 같은 이벤트의 total_spend_usd 가 post-purchase 상태를 반영하도록 emit 전에.
            if (UserSnapshotCache.HasInstance && doc.priceUsd > 0)
                UserSnapshotCache.Instance.OnPurchaseVerified(doc.priceUsd);

            EmitPurchaseEvent(doc, transactionId, verified);

            // [2026-07-06 AF_PURCHASE] AppsFlyer 표준 매출 이벤트 — ROAS/매출 리포트는 af_purchase
            // (af_revenue/af_currency) 표준 키로만 집계됨. 서버 검증 통과 구매만 발행(위조·sim 오염 방지).
            if (verified && AttributionManager.HasInstance)
                AttributionManager.Instance.LogPurchase(doc.priceUsd, "USD", doc.productId, transactionId);

            var r = doc.rewards;
            int coinsAdded = 0;
            if (r != null)
            {
                if (r.coins > 0 && CurrencyManager.HasInstance)
                {
                    // suppressEvent=true → UILobby 즉시 갱신 차단. 연출 끝에 PublishCoinSync 호출.
                    CurrencyManager.Instance.AddCoins(r.coins, CurrencyManager.CoinSource.IAP, suppressEvent: true);
                    coinsAdded = r.coins;
                }

                // [2026-07-06 IAP_REWARD_IMMEDIATE] 부스터/무한하트 즉시 지급.
                // 기존엔 PurchaseRewardEffect 가 성공 팝업 OK → FX 착지 후 지급했는데, 트랜잭션이 이미
                // 확정된 상태에서 팝업 확인 전 앱 종료 시 보상이 영구 소실됐다. 연출은 그대로, 지급만 여기로.
                if (r.boosters != null && BoosterManager.HasInstance)
                {
                    if (r.boosters.hand    > 0) BoosterManager.Instance.AddBooster(BoosterManager.HAND,    r.boosters.hand,    "iap_purchase", 0, "");
                    if (r.boosters.shuffle > 0) BoosterManager.Instance.AddBooster(BoosterManager.SHUFFLE, r.boosters.shuffle, "iap_purchase", 0, "");
                    if (r.boosters.zap     > 0) BoosterManager.Instance.AddBooster(BoosterManager.ZAP,     r.boosters.zap,     "iap_purchase", 0, "");
                }
                if (r.infiniteHeartsSeconds > 0 && LifeManager.HasInstance)
                    LifeManager.Instance.ActivateInfiniteHearts(r.infiniteHeartsSeconds);

                bool grantsRemoveAds = r.removeAds || IsNoAdsProduct(doc);
                if (grantsRemoveAds)
                {
                    GrantRemoveAdsEntitlement(productId, r.removeAds ? "reward" : "category");
                    // [2026-05-13] productId 함께 전달 → UserData 에 구매 시각/경로 같이 기록.
                }
            }
            else if (IsNoAdsProduct(doc))
            {
                GrantRemoveAdsEntitlement(productId, "category-no-rewards");
            }

            // 1회 한정 마킹 (UserData.purchasedOnce)
            if (doc.maxPurchases == 1
                && UserDataService.HasInstance && UserDataService.Instance.IsReady)
            {
                UserDataService.Instance.SetPurchasedOnce(productId, true);
            }

            if (doc.maxPurchases == 1 && doc.category == "offer")
            {
                PlayerPrefs.SetInt(Const.PREFS_STARTER_PURCHASED, 1);
                PlayerPrefs.Save();
            }

            // NPU 해제
            if (UserDataService.HasInstance && UserDataService.Instance.IsReady)
                UserDataService.Instance.MarkPaying();

            // 보상 연출 트리거
            EventBus.Publish(new OnPurchaseRewardGranted
            {
                productId   = productId,
                rewards     = r,
                coinsAdded  = coinsAdded
            });
        }

        private static bool IsNoAdsProduct(ShopProductDoc doc)
        {
            return doc != null && string.Equals(doc.category, CAT_NOADS, System.StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsRemoveAdsEntitlement(string productId, ShopProductDoc doc)
        {
            // ROLLBACK_NOADS_RESTORE_MATCH_20260622:
            // Startup receipts must restore silently even if Firestore category/rewards are temporarily
            // inconsistent. User-initiated purchases still go through the normal reward popup path.
            return IsNoAdsProduct(doc)
                || IsNoAdsProductId(productId)
                || (doc != null && doc.rewards != null && doc.rewards.removeAds);
        }

        private static bool IsNoAdsProductId(string productId)
        {
            return string.Equals(productId, "xyz.aimed.balloonloop.noads", System.StringComparison.OrdinalIgnoreCase);
        }

        private void MarkUserInitiatedPurchase(string productId)
        {
            if (!string.IsNullOrEmpty(productId))
                _userInitiatedPurchases.Add(productId);
        }

        private bool ConsumeUserInitiatedPurchase(string productId)
        {
            if (string.IsNullOrEmpty(productId))
                return false;

            bool exists = _userInitiatedPurchases.Contains(productId);
            if (exists) _userInitiatedPurchases.Remove(productId);
            return exists;
        }

        private void ClearUserInitiatedPurchase(string productId)
        {
            if (!string.IsNullOrEmpty(productId))
                _userInitiatedPurchases.Remove(productId);
        }

        private void ApplyNoAdsRestoreWithoutRewardPopup(string productId, ShopProductDoc doc, string reason)
        {
            string resolvedProductId = !string.IsNullOrEmpty(productId) ? productId : (doc != null ? doc.productId : "");
            if (string.IsNullOrEmpty(resolvedProductId)) return;

            // ROLLBACK_NOADS_RESTORE_SILENT_TITLE_20260622:
            // Google Play can report an existing non-consumable no-ads receipt during
            // Title/IAP initialization on a fresh install. That must restore the
            // entitlement and hide no-ads UI, but it is not a user-initiated purchase
            // in this session, so do not publish OnPurchaseRewardGranted or the success popup.
            GrantRemoveAdsEntitlement(resolvedProductId, reason);

            if (doc != null
                && doc.maxPurchases == 1
                && UserDataService.HasInstance && UserDataService.Instance.IsReady)
            {
                UserDataService.Instance.SetPurchasedOnce(resolvedProductId, true);
            }

            EventBus.Publish(new OnPurchaseRestored { productId = resolvedProductId });
            Debug.Log($"{LOG_TAG} NoAds receipt restored silently without purchase success popup. productId={resolvedProductId} reason={reason} category={(doc != null ? doc.category : "<null>")} removeAdsReward={(doc != null && doc.rewards != null && doc.rewards.removeAds)}");
        }

        private static void GrantRemoveAdsEntitlement(string productId, string reason)
        {
            // ROLLBACK_NOADS_CATEGORY_ENTITLEMENT_20260610:
            // No-ads is an entitlement category. Some product docs may omit rewards.removeAds,
            // so category=noads must still grant and hide no-ads entry points.
            PlayerPrefs.SetInt(Const.PREFS_AD_REMOVED, 1);
            PlayerPrefs.SetInt(Const.PREFS_NO_ADS_OWNED, 1);
            PlayerPrefs.Save();

            if (UserDataService.HasInstance && UserDataService.Instance.IsReady)
                UserDataService.Instance.SetRemovedAds(true, productId);

            EventBus.Publish(new OnAdsRemovedChanged
            {
                removed = true,
                productId = productId
            });
            Debug.Log($"{LOG_TAG} Ads removed entitlement granted. productId={productId}, reason={reason}");
        }

        private static void PublishPurchaseResult(string productId, bool success)
        {
            EventBus.Publish(new OnPurchaseCompleted { productId = productId, success = success });
        }

        /// <summary>
        /// purchase_event (BigQuery raw) emit. transactionId 가 비어 있으면 sim_<guid> 로 채움.
        /// 디바이스 빌드는 validatePurchase 검증 통과 후 호출되므로 verified=true 로 적재됨.
        /// </summary>
        private static void EmitPurchaseEvent(ShopProductDoc doc, string transactionId, bool verified)
        {
            if (doc == null) return;

            string txId = string.IsNullOrEmpty(transactionId)
                ? $"sim_{System.Guid.NewGuid():N}"
                : transactionId;

            var p = new Dictionary<string, object>(20);
            p[AnalyticsConsts.P_EVENT_ID]         = System.Guid.NewGuid().ToString("N");
            p[AnalyticsConsts.P_SESSION_ID]       = AnalyticsSessionTracker.HasInstance
                ? AnalyticsSessionTracker.Instance.CurrentSessionId : "";
            p[AnalyticsConsts.P_GAME_ID]          = AnalyticsConsts.GAME_ID;
            p[AnalyticsConsts.P_UID]              = AnalyticsSessionTracker.ResolveUid();
            p[AnalyticsConsts.P_EVENT_TS]         = System.DateTime.UtcNow.ToString("o");
            p[AnalyticsConsts.P_APP_VERSION]      = Application.version;
            p[AnalyticsConsts.P_GEO_COUNTRY]      = AnalyticsSessionTracker.ResolveGeoCountry();
            p[AnalyticsConsts.P_PLATFORM]         = AnalyticsSessionTracker.ResolvePlatform();
            p[AnalyticsConsts.P_PRODUCT_ID]       = doc.productId ?? "";
            p[AnalyticsConsts.P_PRODUCT_NAME]     = ResolveProductAnalyticsName(doc);
            p[AnalyticsConsts.P_PRODUCT_TYPE]     = ResolveProductAnalyticsType(doc);
            p[AnalyticsConsts.P_PRICE_USD]        = doc.priceUsd;
            p[AnalyticsConsts.P_PRICE_LOCAL]      = doc.priceUsd;
            // [BQ_DIRECT 2026-06-16] purchase 테이블 컬럼에 정렬: currency→currency_code, transaction_id→receipt_id.
            //   store / product_category / device_model 은 purchase 테이블에 컬럼 없음 → 미emit.
            //   (product_name/product_type/price_local/coin_granted/is_verified 등은 추후 계측 보강 — 현재 NULL.)
            p[AnalyticsConsts.P_CURRENCY_CODE]    = string.IsNullOrEmpty(doc.currency) ? "USD" : doc.currency;
            p[AnalyticsConsts.P_IAP_PLACEMENT]    = "shop_popup";
            p[AnalyticsConsts.P_LEVEL_NUMBER]     = LevelManager.HasInstance ? LevelManager.Instance.GetCurrentLevelId() : 0;
            p[AnalyticsConsts.P_COIN_GRANTED]     = doc.rewards != null ? doc.rewards.coins : 0;
            p[AnalyticsConsts.P_ITEMS_GRANTED]    = BuildGrantedItemsJson(doc.rewards);
            p[AnalyticsConsts.P_LIVES_GRANTED]    = 0;
            p[AnalyticsConsts.P_RECEIPT_ID]       = txId;
            // [2026-07-06 IAP_SERVER_VALIDATE] is_verified = validatePurchase 검증 결과.
            // Editor/sim 경로는 false, 디바이스 구매는 검증 통과 후에만 emit 되므로 true.
            p[AnalyticsConsts.P_IS_VERIFIED]      = verified;

            if (UserSnapshotCache.HasInstance)
                UserSnapshotCache.Instance.Stamp(p);

            AnalyticsSessionTracker.EmitEvent(AnalyticsConsts.EVT_PURCHASE, p);
        }

        private static string ResolveProductAnalyticsName(ShopProductDoc doc)
        {
            if (doc == null) return "";
            if (!string.IsNullOrEmpty(doc.title_loc_key))
            {
                string text = LocalizationService.Get(doc.title_loc_key);
                if (!string.IsNullOrEmpty(text)) return text;
            }
            return doc.productId ?? "";
        }

        private static string ResolveProductAnalyticsType(ShopProductDoc doc)
        {
            if (doc == null) return "standard";
            string category = doc.category ?? "";
            if (category == CAT_NOADS) return "one_shot";
            if (doc.maxPurchases == 1 || category == "offer" || doc.hasTimeLimit) return "limited";
            return "standard";
        }

        private static string BuildGrantedItemsJson(ShopRewards rewards)
        {
            if (rewards == null) return "";

            var sb = new StringBuilder();
            AppendItemJson(sb, "hand", rewards.boosters != null ? rewards.boosters.hand : 0);
            AppendItemJson(sb, "shuffle", rewards.boosters != null ? rewards.boosters.shuffle : 0);
            AppendItemJson(sb, "zap", rewards.boosters != null ? rewards.boosters.zap : 0);
            AppendItemJson(sb, "infinite_hearts_seconds", rewards.infiniteHeartsSeconds);
            AppendItemJson(sb, "remove_ads", rewards.removeAds ? 1 : 0);

            if (sb.Length == 0) return "";
            sb.Insert(0, "{");
            sb.Append("}");
            return sb.ToString();
        }

        private static void AppendItemJson(StringBuilder sb, string key, int value)
        {
            if (value <= 0) return;
            if (sb.Length > 0) sb.Append(",");
            sb.Append("\"").Append(key).Append("\":").Append(value);
        }
    }
}
