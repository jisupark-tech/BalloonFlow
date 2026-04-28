using System.Collections.Generic;
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

            // Inspector 가 비어있으면 임시 데이터로 채움
            if (_products == null || _products.Length == 0)
                _products = BuildDefaultTempProducts();

            ResetAndLoadProducts();
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

        private void UpdateMoreButton()
        {
            if (_btnMoreProducts != null)
                _btnMoreProducts.gameObject.SetActive(_products != null && _displayedCount < _products.Length);
        }

        /// <summary>상품 구매 콜백 → ShopManager 라우팅.</summary>
        private void OnProductBuy(ShopProductData product)
        {
            Debug.Log($"[UIShop] Buy: {product.productId}, {product.title}, {product.price}");

            if (ShopManager.HasInstance)
                ShopManager.Instance.PurchaseProduct(product.productId);
        }

        /// <summary>
        /// Inspector 가 비어있을 때 사용할 임시 상품 데이터.
        /// ShopManager.BuildCatalogue 와 productId 정합 — 실제 구매 라우팅 가능.
        /// </summary>
        private static ShopProductData[] BuildDefaultTempProducts()
        {
            return new[]
            {
                // ── 골드 팩 6종 (IAP) — Gold prefab ──
                new ShopProductData { productId = "gold_1k",   title = "1,000 골드",   price = "₩2,751",
                                       category = ShopItemCategory.Gold },
                new ShopProductData { productId = "gold_5k",   title = "5,000 골드",   price = "₩11,007",
                                       hasDiscount = true, discountPercent = 10,
                                       category = ShopItemCategory.Gold },
                new ShopProductData { productId = "gold_10k",  title = "10,000 골드",  price = "₩20,638",
                                       hasDiscount = true, discountPercent = 15,
                                       category = ShopItemCategory.Gold },
                new ShopProductData { productId = "gold_25k",  title = "25,000 골드",  price = "₩41,277",
                                       hasDiscount = true, discountPercent = 20,
                                       category = ShopItemCategory.Gold },
                new ShopProductData { productId = "gold_50k",  title = "50,000 골드",  price = "₩75,675",
                                       hasDiscount = true, discountPercent = 25,
                                       category = ShopItemCategory.Gold },
                new ShopProductData { productId = "gold_100k", title = "100,000 골드", price = "₩137,591",
                                       hasDiscount = true, discountPercent = 30,
                                       category = ShopItemCategory.Gold },

                // ── 번들 3종 — 시간 한정 — General prefab (특가/번들) ──
                new ShopProductData { productId = "bundle_small",  title = "소형 번들",   price = "₩13,759",
                                       hasDiscount = true, discountPercent = 40,
                                       hasTimeLimit = true, timeLimitSeconds = 86400f,
                                       category = ShopItemCategory.General },
                new ShopProductData { productId = "bundle_medium", title = "중형 번들",   price = "₩27,518",
                                       hasDiscount = true, discountPercent = 50,
                                       hasTimeLimit = true, timeLimitSeconds = 86400f,
                                       category = ShopItemCategory.General },
                new ShopProductData { productId = "bundle_ultra",  title = "울트라 번들", price = "₩59,000",
                                       hasDiscount = true, discountPercent = 60,
                                       hasTimeLimit = true, timeLimitSeconds = 86400f,
                                       category = ShopItemCategory.General },

                // ── 광고 보상 — Ad prefab ──
                new ShopProductData { productId = "ad_gold_100",   title = "광고 보상 — 100 골드",  price = "WATCH AD",
                                       category = ShopItemCategory.Ad,
                                       hasTimeLimit = true, timeLimitSeconds = 3600f /* 1시간 쿨타임 */ },

                // ── 광고 제거 + 하트 — General prefab ──
                new ShopProductData { productId = "remove_ads",    title = "광고 제거",   price = "₩5,500",
                                       category = ShopItemCategory.General },
                new ShopProductData { productId = "heart_refill",  title = "하트 충전",   price = "₩1,100",
                                       category = ShopItemCategory.General },

                // ── 부스터 (코인 구매) — productId는 BoosterManager 상수와 정확 일치 — General prefab ──
                new ShopProductData { productId = "select_tool",   title = "Hand 부스터",         price = "1,900 coins",
                                       category = ShopItemCategory.General },
                new ShopProductData { productId = "shuffle",       title = "Shuffle 부스터",      price = "1,500 coins",
                                       category = ShopItemCategory.General },
                new ShopProductData { productId = "color_remove",  title = "Color Remove 부스터", price = "2,900 coins",
                                       category = ShopItemCategory.General },
            };
        }
    }
}
