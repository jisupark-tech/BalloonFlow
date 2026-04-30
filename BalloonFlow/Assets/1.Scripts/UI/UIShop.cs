using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace BalloonFlow
{
    /// <summary>
    /// Shop page — spawned inside UILobby PageContainer (left page).
    /// 상품 리스트는 PopupShopListItem 프리팹으로 동적 생성.
    /// Inspector 의 _products 가 비어있으면 BuildDefaultTempProducts() 임시 데이터 사용.
    /// 구매는 ShopManager.PurchaseProduct 로 라우팅.
    /// </summary>
    public class UIShop : UIBase
    {
        [Header("[Shop Title]")]
        [SerializeField] private TMP_Text _txtTitle;
        [SerializeField] private TMP_Text _txtTitleOutline;

        [Header("[Content — ScrollView]")]
        [SerializeField] private RectTransform _contentRoot;
        [SerializeField] private Button _btnMoreProducts;

        [Header("[List Item Prefab — 카테고리별]")]
        [Tooltip("Resources/UI/UIAssets/ShopListGold.prefab")]
        [SerializeField] private GameObject _prefabGold;
        [Tooltip("Resources/UI/UIAssets/ShopListItem.prefab (일반/특가/번들/부스터)")]
        [SerializeField] private GameObject _prefabGeneral;
        [Tooltip("Resources/UI/UIAssets/ShopListAd.prefab")]
        [SerializeField] private GameObject _prefabAd;

        [Tooltip("Inspector 미할당 시 Resources 폴백 자동 로드.")]
        [SerializeField] private bool _autoLoadFromResources = true;

        [Header("[상품 데이터 — 비어있으면 임시 데이터 사용]")]
        [SerializeField] private ShopProductData[] _products;

        public RectTransform ContentRoot => _contentRoot;

        private const int ITEMS_PER_PAGE = 6;
        private const float DEFAULT_ITEM_HEIGHT = 200f;

        [Header("[Layout]")]
        [Tooltip("동적 아이템에 적용할 preferredHeight (LayoutElement). 프리팹 자체 size 사용 시 0.")]
        [SerializeField] private float _itemHeightOverride = 0f;

        private int _displayedCount;
        private readonly List<PopupShopListItem> _spawnedItems = new List<PopupShopListItem>();

        protected override void Awake()
        {
            base.Awake();
            if (_txtTitle != null) _txtTitle.text = "Shop";
            if (_txtTitleOutline != null) _txtTitleOutline.text = "Shop";

            if (_btnMoreProducts != null)
                _btnMoreProducts.onClick.AddListener(LoadMoreProducts);

            // Resources 폴백 — Inspector 미할당 시 prefab 자동 로드
            if (_autoLoadFromResources)
            {
                if (_prefabGold == null)
                    _prefabGold = Resources.Load<GameObject>("UI/UIAssets/ShopListGold");
                if (_prefabGeneral == null)
                    _prefabGeneral = Resources.Load<GameObject>("UI/UIAssets/ShopListItem");
                if (_prefabAd == null)
                    _prefabAd = Resources.Load<GameObject>("UI/UIAssets/ShopListAd");
            }

            // 컨텐츠 루트의 VerticalLayoutGroup + ContentSizeFitter 보장 (UILobby 가
            // 미리 처리하지만, prefab 직접 띄우는 케이스 대비 fallback)
            EnsureContentLayout();

            // Firestore 카탈로그 우선. 매니저 미준비/실패 시 임시 데이터 fallback.
            SubscribeToCatalog();
        }

        private void OnDestroy()
        {
            if (ShopCatalogService.HasInstance)
                ShopCatalogService.Instance.OnCatalogLoaded -= OnCatalogReady;
        }

        /// <summary>ShopCatalogService 구독. 이미 로드 상태면 즉시 적용. 매니저 부재 시 fallback.</summary>
        private void SubscribeToCatalog()
        {
            if (ShopCatalogService.HasInstance)
            {
                ShopCatalogService.Instance.OnCatalogLoaded += OnCatalogReady;
                if (ShopCatalogService.Instance.IsLoaded)
                {
                    OnCatalogReady();
                }
                else
                {
                    // 로딩 대기 중. 일시적으로 임시 데이터 표시 (사용자 빈 화면 방지)
                    if (_products == null || _products.Length == 0)
                        _products = BuildDefaultTempProducts();
                    ResetAndLoadProducts();
                }
            }
            else
            {
                // 매니저 없음 (Editor 스탠드얼론 테스트 등) — 임시 데이터
                if (_products == null || _products.Length == 0)
                    _products = BuildDefaultTempProducts();
                ResetAndLoadProducts();
            }
        }

        /// <summary>Firestore 카탈로그 로드 완료 시 실행. UserData 기준 필터 + 변환 + 재구성.</summary>
        private void OnCatalogReady()
        {
            var user = (UserDataService.HasInstance && UserDataService.Instance.IsReady)
                ? UserDataService.Instance.CurrentUser
                : null;

            var visible = ShopCatalogService.Instance.GetVisibleForUser(user);
            _products = visible.Select(ConvertDocToData).ToArray();
            Debug.Log($"[UIShop] Catalog loaded — {_products.Length} products visible.");
            ResetAndLoadProducts();
        }

        /// <summary>ShopProductDoc(서버 모델) → ShopProductData(UI 모델) 변환.</summary>
        private static ShopProductData ConvertDocToData(ShopProductDoc doc)
        {
            return new ShopProductData
            {
                productId        = doc.productId,
                title            = string.IsNullOrEmpty(doc.title_loc_key) ? doc.productId : doc.title_loc_key,
                price            = $"${doc.priceUsd:F2}",
                hasDiscount      = doc.discountPercent > 0,
                discountPercent  = doc.discountPercent,
                hasTimeLimit     = doc.hasTimeLimit,
                timeLimitSeconds = doc.timeLimitSeconds,
                category         = MapCategory(doc.category),
                imageKey         = doc.imageKey,
                rewards          = doc.rewards   // 동적 보상 표시용
            };
        }

        /// <summary>Firestore /products 의 카테고리 문자열 → UI prefab 분기 enum.
        /// 실제 시드 카테고리: coin / bundle / noads / offer (1.0 기준).</summary>
        private static ShopItemCategory MapCategory(string cat)
        {
            if (string.IsNullOrEmpty(cat)) return ShopItemCategory.General;
            switch (cat.ToLowerInvariant())
            {
                case "coin":   return ShopItemCategory.Gold;    // ShopListGold prefab
                case "noads":  return ShopItemCategory.Ad;      // ShopListAd prefab
                case "bundle":
                case "offer":
                default:       return ShopItemCategory.General; // ShopListItem prefab
            }
        }

        /// <summary>
        /// _contentRoot 의 VerticalLayoutGroup + ContentSizeFitter 가 설정돼있는지 확인.
        /// 없으면 자동 추가 — 동적 아이템이 뭉치는 문제 방지.
        /// BtnMoreProducts 도 _contentRoot 자식이면 LayoutElement 보장 (안 보임 방지).
        /// </summary>
        private void EnsureContentLayout()
        {
            if (_contentRoot == null) return;

            // Anchor 보정 — Top stretch
            _contentRoot.anchorMin = new Vector2(0f, 1f);
            _contentRoot.anchorMax = new Vector2(1f, 1f);
            _contentRoot.pivot     = new Vector2(0.5f, 1f);

            // VerticalLayoutGroup
            var vlg = _contentRoot.GetComponent<VerticalLayoutGroup>();
            if (vlg == null) vlg = _contentRoot.gameObject.AddComponent<VerticalLayoutGroup>();
            vlg.childControlHeight = true;
            vlg.childControlWidth  = true;
            vlg.childForceExpandHeight = false;
            vlg.childForceExpandWidth  = true;
            vlg.childAlignment = TextAnchor.UpperCenter;
            if (vlg.spacing < 1f) vlg.spacing = 20f;

            // ContentSizeFitter
            var csf = _contentRoot.GetComponent<ContentSizeFitter>();
            if (csf == null) csf = _contentRoot.gameObject.AddComponent<ContentSizeFitter>();
            csf.verticalFit   = ContentSizeFitter.FitMode.PreferredSize;
            csf.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;

            // ScrollRect 스크롤감 보정 — inertia + elastic + 적절한 sensitivity
            var sr = _contentRoot.GetComponentInParent<ScrollRect>();
            if (sr != null)
            {
                sr.horizontal = false;
                sr.vertical = true;
                sr.movementType = ScrollRect.MovementType.Elastic;
                sr.elasticity = 0.1f;          // 끝에서 부드럽게 튕김
                sr.inertia = true;             // 손가락 떼고도 관성 스크롤
                sr.decelerationRate = 0.135f;  // Unity 기본값 (관성 감속)
                if (sr.scrollSensitivity < 30f) sr.scrollSensitivity = 60f;
            }

            // BtnMoreProducts 진단 + LayoutElement 보장
            if (_btnMoreProducts == null)
            {
                Debug.LogWarning("[UIShop] _btnMoreProducts 미할당 — Inspector 에서 More 버튼 드래그 필요. " +
                                 "(없으면 페이지네이션 비활성)");
                return;
            }

            var btnRT = _btnMoreProducts.transform as RectTransform;
            if (btnRT == null) return;

            // _contentRoot 자식이 아니면 경고 (VLG 가 처리 안 함)
            if (btnRT.parent != _contentRoot)
            {
                Debug.LogWarning($"[UIShop] _btnMoreProducts ({btnRT.name}) 가 _contentRoot 자식이 아님 — " +
                                 "VerticalLayoutGroup 처리 안 됨. 부모를 _contentRoot 로 옮기세요.");
                return;
            }

            // VLG 가 height 를 자동 0으로 처리하지 않도록 LayoutElement 부착 보장
            var le = _btnMoreProducts.GetComponent<LayoutElement>();
            if (le == null) le = _btnMoreProducts.gameObject.AddComponent<LayoutElement>();
            if (le.preferredHeight <= 0f)
            {
                float h = btnRT.rect.height;
                le.preferredHeight = h > 1f ? h : 120f; // 더보기 버튼 기본 높이
            }
        }

        private GameObject GetPrefabForCategory(ShopItemCategory cat)
        {
            switch (cat)
            {
                case ShopItemCategory.Gold:    return _prefabGold != null ? _prefabGold : _prefabGeneral;
                case ShopItemCategory.Ad:      return _prefabAd != null ? _prefabAd : _prefabGeneral;
                case ShopItemCategory.General:
                default:                        return _prefabGeneral;
            }
        }

        /// <summary>상품 리스트 초기화 + 첫 페이지 로드.</summary>
        private void ResetAndLoadProducts()
        {
            for (int i = 0; i < _spawnedItems.Count; i++)
            {
                if (_spawnedItems[i] != null && _spawnedItems[i].gameObject != null)
                    Destroy(_spawnedItems[i].gameObject);
            }
            _spawnedItems.Clear();
            _displayedCount = 0;

            LoadMoreProducts();
            UpdateMoreButton();
        }

        /// <summary>다음 페이지 상품 추가. 카테고리별 prefab 자동 선택.
        /// 각 아이템에 LayoutElement 자동 부착 (preferredHeight) → VerticalLayoutGroup 정상 배치.
        /// 끝에 LayoutRebuilder 호출 → ScrollRect Content 크기 갱신.</summary>
        private void LoadMoreProducts()
        {
            if (_products == null || _contentRoot == null) return;

            int loadCount = Mathf.Min(ITEMS_PER_PAGE, _products.Length - _displayedCount);
            for (int i = 0; i < loadCount; i++)
            {
                int idx = _displayedCount + i;
                var data = _products[idx];

                GameObject prefab = GetPrefabForCategory(data.category);
                if (prefab == null) continue; // prefab 미할당 → skip

                var go = Instantiate(prefab, _contentRoot);
                go.SetActive(true);

                // BtnMoreProducts 가 있으면 그 직전에 배치 (스크롤 끝에 더보기 유지)
                if (_btnMoreProducts != null && _btnMoreProducts.transform.parent == _contentRoot)
                    go.transform.SetSiblingIndex(_btnMoreProducts.transform.GetSiblingIndex());

                // VerticalLayoutGroup 이 size 줄 수 있도록 LayoutElement 보장
                var rt = go.transform as RectTransform;
                var le = go.GetComponent<LayoutElement>();
                if (le == null) le = go.AddComponent<LayoutElement>();
                if (le.preferredHeight <= 0f)
                {
                    if (_itemHeightOverride > 0f)
                        le.preferredHeight = _itemHeightOverride;
                    else if (rt != null && rt.rect.height > 1f)
                        le.preferredHeight = rt.rect.height;
                    else
                        le.preferredHeight = DEFAULT_ITEM_HEIGHT;
                }

                var item = go.GetComponent<PopupShopListItem>();
                if (item != null)
                {
                    item.Setup(data, OnProductBuy);
                    _spawnedItems.Add(item);
                }
                else
                {
                    Debug.LogWarning($"[UIShop] {prefab.name} 에 PopupShopListItem 컴포넌트 없음 — Setup 호출 불가. " +
                                     "Inspector에서 카드 자체 컴포넌트로 attach 필요.");
                }
            }

            _displayedCount += loadCount;

            // VerticalLayoutGroup + ContentSizeFitter 강제 재계산 → ScrollRect 활성
            if (_contentRoot != null)
                LayoutRebuilder.ForceRebuildLayoutImmediate(_contentRoot);

            UpdateMoreButton();
        }

        /// <summary>
        /// BtnMoreProducts 는 항상 활성 + 항상 _contentRoot 의 마지막 sibling (= 스크롤 최하단) 보장.
        /// 이전엔 모든 상품 로드 완료 시 SetActive(false) 했지만, 디자인 요청으로 노출 유지.
        /// </summary>
        private void UpdateMoreButton()
        {
            if (_btnMoreProducts == null) return;

            if (!_btnMoreProducts.gameObject.activeSelf)
                _btnMoreProducts.gameObject.SetActive(true);

            // 항상 스크롤 최하단으로
            if (_btnMoreProducts.transform.parent == _contentRoot)
                _btnMoreProducts.transform.SetAsLastSibling();
        }

        /// <summary>상품 구매 콜백 → 확인 popup → 확인 시 ShopManager 라우팅.</summary>
        private void OnProductBuy(ShopProductData product)
        {
            Debug.Log($"[UIShop] Buy clicked: {product.productId}, {product.title}, {product.price}");

            if (!UIManager.HasInstance)
            {
                ProceedPurchase(product);
                return;
            }

            var popup = UIManager.Instance.OpenUI<PopupError>("Popup/PopupError");
            if (popup == null)
            {
                ProceedPurchase(product);
                return;
            }

            string desc = $"Buy {product.title} for {product.price}?";
            popup.ShowConfirm(
                title:       "Confirm Purchase",
                description: desc,
                onYes:       () => ProceedPurchase(product),
                onNo:        null);
        }

        /// <summary>실제 구매 라우팅 — 확인 popup 의 Yes 콜백.</summary>
        private static void ProceedPurchase(ShopProductData product)
        {
            if (ShopManager.HasInstance)
                ShopManager.Instance.PurchaseProduct(product.productId);
        }

        /// <summary>
        /// Firestore /products fetch 가 미준비/실패한 짧은 시점에 빈 배열 반환.
        /// 실제 카탈로그는 ShopCatalogService 가 fetch 완료 시 OnCatalogReady 에서 채움.
        /// (이전 placeholder 들은 옛 ID 라 구매 시 IAPManager lookup 실패하므로 제거)
        /// </summary>
        private static ShopProductData[] BuildDefaultTempProducts()
        {
            return System.Array.Empty<ShopProductData>();
        }
    }
}
