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
        [Tooltip("BtnMoreProducts 부모 컨테이너 GameObject — 클릭 후 전체 숨김 대상. 미할당 시 _btnMoreProducts.transform.parent.gameObject 폴백.")]
        [SerializeField] private GameObject _btnMoreProductsRoot;

        [Header("[List Item Prefab — 카테고리별]")]
        [Tooltip("Resources/UI/UIAssets/ShopListGold")]
        [SerializeField] private GameObject _prefabGold;
        [Tooltip("Resources/UI/UIAssets/ShopListGoldAlign.prefab (코인 상품들을 가로 정렬해 담는 컨테이너)")]
        [SerializeField] private GameObject _prefabGoldAlign;
        [Tooltip("Resources/UI/UIAssets/ShopListItem.prefab (일반/특가/번들/부스터)")]
        [SerializeField] private GameObject _prefabGeneral;
        [Tooltip("Resources/UI/UIAssets/ShopListAd.prefab")]
        [SerializeField] private GameObject _prefabAd;

        [Tooltip("Inspector 미할당 시 Resources 폴백 자동 로드.")]
        [SerializeField] private bool _autoLoadFromResources = true;

        [Header("[상품 데이터 — 비어있으면 임시 데이터 사용]")]
        [SerializeField] private ShopProductData[] _products;

        public RectTransform ContentRoot => _contentRoot;

        // 부모 컨테이너 전체 비활성/활성 — 미할당 시 Button의 부모로 자동 폴백.
        private GameObject MoreButtonRoot
        {
            get
            {
                if (_btnMoreProductsRoot != null) return _btnMoreProductsRoot;
                if (_btnMoreProducts != null && _btnMoreProducts.transform.parent != null)
                    return _btnMoreProducts.transform.parent.gameObject;
                return _btnMoreProducts != null ? _btnMoreProducts.gameObject : null;
            }
        }

        private const int ITEMS_PER_PAGE = 6;
        private const float DEFAULT_ITEM_HEIGHT = 200f;

        [Header("[Layout]")]
        [Tooltip("동적 아이템에 적용할 preferredHeight (LayoutElement). 프리팹 자체 size 사용 시 0.")]
        [SerializeField] private float _itemHeightOverride = 0f;

        private int _displayedCount;
        private bool _userExpandedMore;
        private readonly List<PopupShopListItem> _spawnedItems = new List<PopupShopListItem>();

        protected override void Awake()
        {
            base.Awake();
            if (_txtTitle != null) _txtTitle.text = "Shop";
            if (_txtTitleOutline != null) _txtTitleOutline.text = "Shop";

            if (_btnMoreProducts != null)
                _btnMoreProducts.onClick.AddListener(OnMoreProductsClicked);

            // Resources 폴백 — Inspector 미할당 시 prefab 자동 로드
            if (_autoLoadFromResources)
            {
                if (_prefabGold == null)
                    _prefabGold = Resources.Load<GameObject>("UI/UIAssets/ShopListGold");
                if (_prefabGoldAlign == null)
                    _prefabGoldAlign = Resources.Load<GameObject>("UI/UIAssets/ShopListGoldAlign");
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
            var category = MapCategory(doc.category);
            string title;
            if (!string.IsNullOrEmpty(doc.title_loc_key))
            {
                title = doc.title_loc_key;
            }
            else if (category == ShopItemCategory.Gold && doc.rewards != null && doc.rewards.coins > 0)
            {
                // coin 카테고리는 title_loc_key 비어있을 때 productId 대신 코인 수량 표시 — ShopListGold prefab의 TextPrice가 _txtTitle 슬롯에 wire 되어 있어 발생한 productId 노출 회귀 fix.
                title = doc.rewards.coins.ToString("N0");
            }
            else
            {
                title = doc.productId;
            }

            return new ShopProductData
            {
                productId        = doc.productId,
                title            = title,
                price            = $"${doc.priceUsd:F2}",
                hasDiscount      = doc.discountPercent > 0,
                discountPercent  = doc.discountPercent,
                hasTimeLimit     = doc.hasTimeLimit,
                timeLimitSeconds = doc.timeLimitSeconds,
                category         = category,
                imageKey         = doc.imageKey,
                goldIconKey      = doc.goldIconKey ?? string.Empty,
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
        /// BtnMoreProducts 도 _contentRoot 자식이면 LayoutElement 보장 + Awake 단계부터
        /// last sibling 강제. 이후 모든 분기(catalog 로드/dynamic spawn/재로드)에서도 유지.
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
            vlg.childControlWidth  = false;
            vlg.childForceExpandHeight = false;
            vlg.childForceExpandWidth  = false;
            vlg.childAlignment = TextAnchor.UpperCenter;
            if (vlg.spacing < 1f) vlg.spacing = 20f;
            // 스크롤 끝에 마지막 카드가 붙지 않도록 breathing space — UIShop scroll 답답함 fix
            if (vlg.padding.bottom < 500) vlg.padding.bottom = 500;

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

            // Awake 단계부터 last sibling 강제 — 정적 placeholder 가 ShopContent 안에 있어도
            // BtnMore 가 항상 스크롤 최하단에 위치. (parent != _contentRoot 면 designer 의도일
            // 수 있으므로 reparent 는 하지 않고 위 LogWarning 에서 종료.)
            _btnMoreProducts.transform.SetAsLastSibling();
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

        /// <summary>유저가 다른 페이지에서 Shop 탭으로 재진입할 때 호출 — 더보기 상태/리스트/스크롤 위치를 초기 상태로 되돌린다.</summary>
        public void ResetView()
        {
            _userExpandedMore = false;

            // 누적된 스크롤 오프셋 wipe — 탭 재진입 시 상단 공백 fix
            if (_contentRoot != null)
                _contentRoot.anchoredPosition = Vector2.zero;

            ResetAndLoadProducts();

            // ScrollRect 내부 viewport/content 캐시 flush — 새 content size 기반 normalizedPosition 보장
            Canvas.ForceUpdateCanvases();

            if (_contentRoot != null)
            {
                var sr = _contentRoot.GetComponentInParent<ScrollRect>();
                if (sr != null)
                {
                    sr.StopMovement();
                    sr.verticalNormalizedPosition = 1f;
                }

                // belt-and-suspenders — 어떤 ScrollRect 캐시 상태에서도 첫 진입과 동일한 상단 위치 보장
                _contentRoot.anchoredPosition = Vector2.zero;
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

        /// <summary>사용자가 BtnMoreProducts를 직접 클릭했을 때만 호출 — 추가 로드 후 버튼 영구 숨김.</summary>
        private void OnMoreProductsClicked()
        {
            _userExpandedMore = true;
            // More 버튼 1회 클릭 시 남은 전체 로드 (6번째 coin 이상 잔존 상품 누락 fix)
            LoadMoreProducts(_products != null ? _products.Length - _displayedCount : -1);
            // 부모 컨테이너 전체 비활성 — Button GameObject 단독이 아닌 root 기준.
            if (MoreButtonRoot != null && MoreButtonRoot.activeSelf)
                MoreButtonRoot.SetActive(false);
        }

        /// <summary>다음 페이지 상품 추가. 카테고리별 prefab 자동 선택.
        /// 각 아이템에 LayoutElement 자동 부착 (preferredHeight) → VerticalLayoutGroup 정상 배치.
        /// 끝에 LayoutRebuilder 호출 → ScrollRect Content 크기 갱신.</summary>
        private void LoadMoreProducts(int loadOverride = -1)
        {
            if (_products == null || _contentRoot == null) return;

            GameObject goldContainer = null;

            int remaining = _products.Length - _displayedCount;
            int loadCount = (loadOverride > 0) ? Mathf.Min(loadOverride, remaining) : Mathf.Min(ITEMS_PER_PAGE, remaining);
            for (int i = 0; i < loadCount; i++)
            {
                int idx = _displayedCount + i;
                var data = _products[idx];

                if (data.category == ShopItemCategory.Gold && _prefabGoldAlign != null && _prefabGold != null)
                {
                    // Gold 그룹 컨테이너 1개를 만들고 코인 상품들을 그 자식으로 spawn.
                    if (goldContainer == null)
                    {
                        goldContainer = Instantiate(_prefabGoldAlign, _contentRoot);
                        goldContainer.SetActive(true);

                        // 부모 컨테이너 root 기준으로 sibling 정렬 — Blue 자식이 _contentRoot 직속이 아니므로 root 사용.
                        if (MoreButtonRoot != null && MoreButtonRoot.transform.parent == _contentRoot)
                            goldContainer.transform.SetSiblingIndex(MoreButtonRoot.transform.GetSiblingIndex());

                        var containerRT = goldContainer.transform as RectTransform;
                        var containerLE = goldContainer.GetComponent<LayoutElement>();
                        if (containerLE == null) containerLE = goldContainer.AddComponent<LayoutElement>();
                        if (containerLE.preferredHeight <= 0f)
                        {
                            if (_itemHeightOverride > 0f)
                                containerLE.preferredHeight = _itemHeightOverride;
                            else if (containerRT != null && containerRT.rect.height > 1f)
                                containerLE.preferredHeight = containerRT.rect.height;
                            else
                                containerLE.preferredHeight = DEFAULT_ITEM_HEIGHT;
                        }

                        // prefab 에 미리 박혀있는 placeholder 자식(ShopListGold(1)/(2)) 제거 —
                        // 데이터 없는 빈 카드 노출 방지.
                        for (int c = goldContainer.transform.childCount - 1; c >= 0; c--)
                            Destroy(goldContainer.transform.GetChild(c).gameObject);
                    }

                    var goldGo = Instantiate(_prefabGold, goldContainer.transform);
                    goldGo.SetActive(true);

                    var goldItem = goldGo.GetComponent<PopupShopListItem>();
                    if (goldItem != null)
                    {
                        goldItem.Setup(data, OnProductBuy);
                        _spawnedItems.Add(goldItem);
                    }
                    else
                    {
                        Debug.LogWarning($"[UIShop] {_prefabGold.name} 에 PopupShopListItem 컴포넌트 없음 — Setup 호출 불가. " +
                                         "Inspector에서 카드 자체 컴포넌트로 attach 필요.");
                    }
                    continue;
                }

                // 비-Gold 카테고리: 기존 직접-spawn 경로 유지.
                goldContainer = null;

                GameObject prefab = GetPrefabForCategory(data.category);
                if (prefab == null) continue; // prefab 미할당 → skip

                var go = Instantiate(prefab, _contentRoot);
                go.SetActive(true);

                // 부모 컨테이너 root 기준으로 그 직전에 배치 (스크롤 끝에 더보기 유지)
                if (MoreButtonRoot != null && MoreButtonRoot.transform.parent == _contentRoot)
                    go.transform.SetSiblingIndex(MoreButtonRoot.transform.GetSiblingIndex());

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

            // 동적 spawn 후에도 부모 컨테이너 root 가 항상 마지막 sibling 이 되도록 한 번 더 보정.
            if (MoreButtonRoot != null && MoreButtonRoot.transform.parent == _contentRoot)
                MoreButtonRoot.transform.SetAsLastSibling();

            // VerticalLayoutGroup + ContentSizeFitter 강제 재계산 → ScrollRect 활성
            if (_contentRoot != null)
                LayoutRebuilder.ForceRebuildLayoutImmediate(_contentRoot);

            UpdateMoreButton();
        }

        /// <summary>
        /// BtnMoreProducts 노출 정책 — 클릭 후에는 영구히 숨김.
        /// Awake 단계부터 last sibling 강제, 이후 모든 분기(활성/비활성)에서도 유지하여
        /// 다음 ResetAndLoadProducts 시 sibling 위치가 안정되도록 보장.
        /// </summary>
        private void UpdateMoreButton()
        {
            if (_btnMoreProducts == null) return;
            var root = MoreButtonRoot;
            if (_userExpandedMore)
            {
                // 부모 컨테이너 전체 비활성.
                if (root != null && root.activeSelf)
                    root.SetActive(false);
                // 비활성화돼도 마지막 sibling 위치 유지 — 다음 reload 안정.
                if (root != null && root.transform.parent == _contentRoot)
                    root.transform.SetAsLastSibling();
                return;
            }
            // 부모 컨테이너 전체 활성.
            if (root != null && !root.activeSelf)
                root.SetActive(true);
            if (root != null && root.transform.parent == _contentRoot)
                root.transform.SetAsLastSibling();
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
