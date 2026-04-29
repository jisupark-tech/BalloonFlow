using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;
using Firebase.Extensions;
using Firebase.Firestore;

namespace BalloonFlow
{
    /// <summary>
    /// Firestore /products 컬렉션 fetch + 메모리 캐시. UIShop 이 임시 BuildDefaultTempProducts 대신 사용.
    /// 콘솔에서 가격/구성 변경 시 다음 fetch 부터 자동 반영.
    /// 1.0 정책: 앱 시작 시 1회 fetch + 사용자 명시 갱신만. 실시간 listen 안 함 (latency·cost 절약).
    /// </summary>
    public class ShopCatalogService : Singleton<ShopCatalogService>
    {
        private const string LOG_TAG = "[ShopCatalogService]";
        // ⚠️ 비표준 — Firestore 콘솔에 "productId" 로 만들어졌고 임시로 매칭. 표준은 "products" (복수).
        // TODO: 컬렉션을 "products" 로 마이그레이션 후 이 상수도 변경 (Task 참조).
        private const string COLLECTION = "productId";

        private readonly List<ShopProductDoc> _all = new List<ShopProductDoc>();
        private bool _isLoaded;

        public bool IsLoaded => _isLoaded;
        public IReadOnlyList<ShopProductDoc> All => _all;
        public event Action OnCatalogLoaded;

        protected override void OnSingletonAwake()
        {
            _ = FetchAsync();
        }

        public async Task FetchAsync()
        {
            const int MAX_RETRIES = 3;
            Exception lastEx = null;

            for (int attempt = 1; attempt <= MAX_RETRIES; attempt++)
            {
                try
                {
                    var db   = FirebaseEnvironment.GetFirestore();
                    var snap = await db.Collection(COLLECTION).GetSnapshotAsync();

                    _all.Clear();
                    foreach (var doc in snap.Documents)
                    {
                        var product = doc.ConvertTo<ShopProductDoc>();
                        if (product == null) continue;
                        // productId 필드 누락 시 document ID 로 fallback (시드 JSON 컨벤션)
                        if (string.IsNullOrEmpty(product.productId))
                            product.productId = doc.Id;
                        _all.Add(product);
                    }
                    _all.Sort((a, b) => a.sortOrder.CompareTo(b.sortOrder));

                    _isLoaded = true;
                    Debug.Log($"{LOG_TAG} Fetched {_all.Count} products.");
                    OnCatalogLoaded?.Invoke();
                    return;
                }
                catch (FirestoreException fe) when (fe.ErrorCode == FirestoreError.Unavailable && attempt < MAX_RETRIES)
                {
                    lastEx = fe;
                    Debug.LogWarning($"{LOG_TAG} Firestore unavailable. Retry {attempt}/{MAX_RETRIES} in {attempt}s...");
                    await Task.Delay(1000 * attempt);
                }
                catch (Exception e)
                {
                    Debug.LogError($"{LOG_TAG} Fetch failed: {e.Message}");
                    return;
                }
            }
            Debug.LogError($"{LOG_TAG} Fetch retries exhausted: {lastEx?.Message}");
        }

        /// <summary>현재 유저 레벨 + 구매 이력 + visibleInShop 기준으로 노출 가능한 상품 필터.</summary>
        public List<ShopProductDoc> GetVisibleForUser(UserData user)
        {
            var result = new List<ShopProductDoc>(_all.Count);
            int playerLevel = user != null ? Mathf.Max(1, user.highestClearedLevel + 1) : 1;

            foreach (var p in _all)
            {
                if (!p.visibleInShop) continue;
                if (playerLevel < p.unlockLevel) continue;

                // maxPurchases=1 이고 이미 산 상품이면 숨김 (Best Value Pack, Remove Ads 등)
                if (p.maxPurchases == 1 && user != null
                    && user.purchasedOnce.TryGetValue(p.productId, out bool purchased) && purchased)
                    continue;

                result.Add(p);
            }
            return result;
        }

        public ShopProductDoc Get(string productId)
        {
            for (int i = 0; i < _all.Count; i++)
                if (_all[i].productId == productId) return _all[i];
            return null;
        }
    }
}
