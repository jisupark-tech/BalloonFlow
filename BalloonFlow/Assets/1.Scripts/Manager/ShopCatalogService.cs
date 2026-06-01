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
        private const string COLLECTION = "products";

        private readonly List<ShopProductDoc> _all = new List<ShopProductDoc>();
        private bool _isLoaded;

        private bool _isFetching;

        public bool IsLoaded => _isLoaded;
        public IReadOnlyList<ShopProductDoc> All => _all;
        public event Action OnCatalogLoaded;

        protected override void OnSingletonAwake()
        {
            _ = FetchAsync();
        }

        /// <summary>
        /// 카탈로그가 아직 로드되지 않았으면 재fetch (Shop 진입/구매 시도 시 복구용).
        /// 이미 로드됐거나 fetch 진행 중이면 무시. 결제 미초기화(카탈로그 미로드)에서 자가 복구 경로.
        /// </summary>
        public void RetryFetch()
        {
            if (_isLoaded || _isFetching) return;
            Debug.Log($"{LOG_TAG} RetryFetch 요청 — 카탈로그 재시도");
            _ = FetchAsync();
        }

        public async Task FetchAsync()
        {
            if (_isFetching) return;
            _isFetching = true;
            try
            {
                // FirebaseManager 의 dep check 완료까지 대기.
                // 직접 호출 시 "Don't call Firebase functions before CheckDependencies has finished" InvalidOperationException.
                for (int i = 0; i < 150 && !(FirebaseManager.HasInstance && FirebaseManager.Instance.IsReady); i++)
                    await Task.Delay(100);
                if (!FirebaseManager.HasInstance || !FirebaseManager.Instance.IsReady)
                {
                    Debug.LogError($"{LOG_TAG} FirebaseManager not ready (timeout) — fetch skipped");
                    return;
                }

                // [fix] Auth(익명 로그인) 완료 대기 — 미완료 상태에서 /products read 시 PermissionDenied(일시) 발생.
                // 콜드스타트의 가장 흔한 카탈로그 로드 실패 원인. 최대 10초 대기 후 진행(미준비여도 아래 retry 가 흡수).
                for (int i = 0; i < 100 && !(UserDataService.HasInstance && UserDataService.Instance.IsReady); i++)
                    await Task.Delay(100);

                const int MAX_RETRIES = 5;
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
                    // [fix] Unavailable 뿐 아니라 PermissionDenied 도 재시도 — Auth 가 살짝 늦게 완료되는 일시적 거부 흡수.
                    // (Rules 가 진짜로 막으면 retry 소진 후 에러 로그 — 그 경우 서버 Rules 수정 필요)
                    catch (FirestoreException fe) when (attempt < MAX_RETRIES &&
                        (fe.ErrorCode == FirestoreError.Unavailable || fe.ErrorCode == FirestoreError.PermissionDenied))
                    {
                        lastEx = fe;
                        Debug.LogWarning($"{LOG_TAG} {fe.ErrorCode} — retry {attempt}/{MAX_RETRIES} in {attempt}s (auth/rules 일시 미준비 가능)");
                        await Task.Delay(1000 * attempt);
                    }
                    catch (Exception e)
                    {
                        Debug.LogError($"{LOG_TAG} Fetch failed: {e.Message}");
                        return;
                    }
                }
                Debug.LogError($"{LOG_TAG} Fetch retries exhausted: {lastEx?.Message} — 서버 Rules(/products read) 확인 필요");
            }
            finally
            {
                _isFetching = false;
            }
        }

        /// <summary>StoreProductExposure.BuildProducts 로 위임 — 단계 판정은 거기서 단일 소스로 관리.</summary>
        public List<ShopProductDoc> GetVisibleForUser(UserData user)
        {
            return StoreProductExposure.BuildProducts(_all, user, expanded: true);
        }

        public ShopProductDoc Get(string productId)
        {
            for (int i = 0; i < _all.Count; i++)
                if (_all[i].productId == productId) return _all[i];
            return null;
        }
    }
}
