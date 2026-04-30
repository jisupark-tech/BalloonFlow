using UnityEngine;

namespace BalloonFlow
{
    /// <summary>
    /// 상점 UI 토글 + 구매 라우팅. 상품 카탈로그/보상은 ShopCatalogService(Firestore)/IAPManager 가 진실 소스.
    /// 코인 구매 부스터(hand/shuffle/zap)는 BoosterManager 로 라우팅.
    /// </summary>
    public class ShopManager : Singleton<ShopManager>
    {
        private const string PopupShopId = "popup_shop";

        [SerializeField] private CanvasGroup _shopPanelGroup;

        private bool _isOpen;

        public bool IsOpen => _isOpen;

        public void OpenShop()
        {
            if (_isOpen) return;
            _isOpen = true;

            if (PopupManager.HasInstance) PopupManager.Instance.ShowPopup(PopupShopId, priority: 50);
            else SetPanelVisible(true);

            Debug.Log("[ShopManager] Shop opened.");
        }

        public void CloseShop()
        {
            if (!_isOpen) return;
            _isOpen = false;

            if (PopupManager.HasInstance) PopupManager.Instance.ClosePopup(PopupShopId);
            else SetPanelVisible(false);

            Debug.Log("[ShopManager] Shop closed.");
        }

        /// <summary>
        /// 구매 라우팅. Firestore 카탈로그(IAP 13개) 매칭 → IAPManager. 코인 구매 부스터 → BoosterManager.
        /// </summary>
        public bool PurchaseProduct(string productId)
        {
            if (string.IsNullOrEmpty(productId))
            {
                Debug.LogWarning("[ShopManager] productId null/empty");
                return false;
            }

            // 1) Firestore /products 카탈로그 (IAP) — full Store SKU
            if (ShopCatalogService.HasInstance)
            {
                var doc = ShopCatalogService.Instance.Get(productId);
                if (doc != null)
                {
                    if (!IAPManager.HasInstance)
                    {
                        Debug.LogWarning("[ShopManager] IAPManager 미준비");
                        return false;
                    }
                    IAPManager.Instance.PurchaseProduct(productId);
                    return true; // 결과는 OnPurchaseCompleted 이벤트로 비동기 통지
                }
            }

            // 2) 코인 구매 부스터 (hand/shuffle/zap)
            if (BoosterManager.HasInstance && IsBoosterId(productId))
                return BoosterManager.Instance.PurchaseBooster(productId);

            Debug.LogWarning($"[ShopManager] {productId} 매칭 실패 — 카탈로그/부스터 어디에도 없음");
            return false;
        }

        private static bool IsBoosterId(string id)
            => id == BoosterManager.HAND || id == BoosterManager.SHUFFLE || id == BoosterManager.ZAP;

        private void SetPanelVisible(bool visible)
        {
            if (_shopPanelGroup == null) return;
            _shopPanelGroup.alpha          = visible ? 1f : 0f;
            _shopPanelGroup.interactable   = visible;
            _shopPanelGroup.blocksRaycasts = visible;
        }
    }
}
