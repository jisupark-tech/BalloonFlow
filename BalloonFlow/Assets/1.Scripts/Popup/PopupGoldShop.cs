using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace BalloonFlow
{
    /// <summary>
    /// 골드 상점 팝업.
    /// Inspector에서 UI 링크 연결. 상품 리스트는 PopupShopListItem 프리팹으로 동적 생성.
    /// BtnMoreProducts: 스크롤에 아이템 추가.
    /// TopBar 코인 표시는 AnimatedCoinLabel 가 TopBarArea/GoldPanel/TxtGold 에 자동 부착되어 처리.
    /// </summary>
    public class PopupGoldShop : UIBase
    {
        [Header("[Common Frame]")]
        [SerializeField] private PopupCommonFrame _frame;

        [Header("[Exit Button — 직접 와이어링 (선택)]")]
        [Tooltip("prefab 안의 나가기 버튼을 Inspector에서 직접 연결. 미연결 시 _frame.BtnExit 와 이름 기반 자동 탐색으로 fallback.")]
        [SerializeField] private Button _btnExitDirect;

        [Header("[Top Panel — Fallback 즉시 스냅 전용]")]
        [Tooltip("prefab 와이어링이 TopBar 노드를 가리키면 AnimatedCoinLabel 이 우선 갱신. 미와이어링 fallback 으로 OpenUI 시 1회 즉시 스냅.")]
        [SerializeField] private TMP_Text _txtGold;

        public Button CloseButton => _frame != null ? _frame.BtnExit : null;

        [Header("[Main Panel — ScrollView]")]
        [SerializeField] private ScrollRect _scrollView;
        [SerializeField] private Transform _shopContent;
        [SerializeField] private Button _btnMoreProducts;

        [Header("[상품 아이템 프리팹]")]
        [SerializeField] private GameObject _listItemPrefab;

        [Header("[상품 데이터]")]
        [SerializeField] private ShopProductData[] _products;

        /// <summary>현재 표시된 상품 수.</summary>
        private int _displayedCount;

        /// <summary>한 번에 표시할 상품 수.</summary>
        private const int ITEMS_PER_PAGE = 6;

        /// <summary>생성된 아이템 리스트 (무한 스크롤 풀링용).</summary>
        private readonly List<PopupShopListItem> _spawnedItems = new List<PopupShopListItem>();

        private System.Action _onCloseCallback;
        private bool _userExpandedMore;
        private bool _moreOffersAvailable;

        private UserData CurrentUserOrNull =>
            (UserDataService.HasInstance && UserDataService.Instance.IsReady)
                ? UserDataService.Instance.CurrentUser
                : null;

        protected override void Awake()
        {
            base.Awake();

            var bound = new HashSet<Button>();
            BindExitClick(_frame != null ? _frame.BtnExit : null, bound);
            BindExitClick(_btnExitDirect, bound);

            string[] exitNameCandidates = { "ExitButton", "ExitButton(1)", "BtnExit", "BtnClose", "CloseButton", "Btn_Exit", "Btn_Close" };
            for (int i = 0; i < exitNameCandidates.Length; i++)
            {
                Transform found = FindChildRecursive(transform, exitNameCandidates[i]);
                if (found == null) continue;
                BindExitClick(found.GetComponent<Button>(), bound);
            }

            if (_btnMoreProducts != null)
                _btnMoreProducts.onClick.AddListener(OnMoreProductsClicked);

            EnsureTopBarBinding();
            SubscribeToCatalog();
        }

        private void OnEnable()
        {
            EventBus.Subscribe<OnPurchaseCompleted>(HandlePurchaseCompleted);
            EventBus.Subscribe<OnPurchaseRestored>(HandlePurchaseRestored);
        }

        private void OnDisable()
        {
            EventBus.Unsubscribe<OnPurchaseCompleted>(HandlePurchaseCompleted);
            EventBus.Unsubscribe<OnPurchaseRestored>(HandlePurchaseRestored);
        }

        protected override void OnDestroy()
        {
            base.OnDestroy();

            if (ShopCatalogService.HasInstance)
                ShopCatalogService.Instance.OnCatalogLoaded -= OnCatalogReady;
            EventBus.Unsubscribe<OnPurchaseCompleted>(HandlePurchaseCompleted);
            EventBus.Unsubscribe<OnPurchaseRestored>(HandlePurchaseRestored);
        }

        private void SubscribeToCatalog()
        {
            if (!ShopCatalogService.HasInstance) return;
            ShopCatalogService.Instance.OnCatalogLoaded += OnCatalogReady;
            if (ShopCatalogService.Instance.IsLoaded)
                RefreshProductExposure();
        }

        private void OnCatalogReady()
        {
            RefreshProductExposure();
            if (gameObject.activeInHierarchy)
                ResetAndLoadProducts(_userExpandedMore);
        }

        private void BindExitClick(Button btn, HashSet<Button> bound)
        {
            if (btn == null) return;
            if (!bound.Add(btn)) return;
            btn.onClick.RemoveListener(OnExitClicked);
            btn.onClick.AddListener(OnExitClicked);
        }

        private void OnExitClicked()
        {
            CloseUI();
        }

        private void EnsureTopBarBinding()
        {
            Transform topBar = FindChildRecursive(transform, "TopBarArea");
            Transform gold = topBar != null ? FindChildRecursive(topBar, "GoldPanel") : null;
            Transform txt = gold != null ? FindChildRecursive(gold, "TxtGold") : null;
            if (txt != null && txt.GetComponent<AnimatedCoinLabel>() == null)
                txt.gameObject.AddComponent<AnimatedCoinLabel>();
        }

        private static Transform FindChildRecursive(Transform parent, string childName)
        {
            for (int i = 0; i < parent.childCount; i++)
            {
                Transform child = parent.GetChild(i);
                if (child.name == childName) return child;
                Transform deep = FindChildRecursive(child, childName);
                if (deep != null) return deep;
            }
            return null;
        }

        public void OpenWithCloseCallback(System.Action onClose)
        {
            _onCloseCallback = onClose;
            OpenUI();
        }

        public override void CloseUI()
        {
            base.CloseUI();
            if (_onCloseCallback != null)
            {
                System.Action cb = _onCloseCallback;
                _onCloseCallback = null;
                cb.Invoke();
            }
        }

        public override void OpenUI()
        {
            if (_frame != null)
            {
                _frame.SetTitle("Shop");
                _frame.SetButtonLayout(PopupCommonFrame.ButtonLayout.None);
                _frame.ShowExitButton(true);
            }
            base.OpenUI();
            RefreshGold();
            ResetAndLoadProducts(expanded: false);
        }

        /// <summary>보유 골드 즉시 스냅 — prefab 의 _txtGold 와이어링이 TopBar 가 아닌 다른 노드를
        /// 가리킬 때 fallback. TopBar 잔액은 AnimatedCoinLabel 이 EventBus 로 자동 갱신.</summary>
        private void RefreshProductExposure()
        {
            if (!ShopCatalogService.HasInstance || !ShopCatalogService.Instance.IsLoaded)
            {
                _moreOffersAvailable = false;
                return;
            }

            var user = CurrentUserOrNull;
            var docs = StoreProductExposure.BuildProducts(
                ShopCatalogService.Instance.All,
                user,
                _userExpandedMore);
            var products = new List<ShopProductData>(docs.Count);
            for (int i = 0; i < docs.Count; i++)
                products.Add(UIShop.ConvertDocToData(docs[i]));
            _products = products.ToArray();
            _moreOffersAvailable = StoreProductExposure.CanExpand(ShopCatalogService.Instance.All, user);
        }

        private void HandlePurchaseCompleted(OnPurchaseCompleted evt)
        {
            if (evt.success && gameObject.activeInHierarchy)
                ResetAndLoadProducts(_userExpandedMore);
        }

        private void HandlePurchaseRestored(OnPurchaseRestored evt)
        {
            if (gameObject.activeInHierarchy)
                ResetAndLoadProducts(_userExpandedMore);
        }

        private void RefreshGold()
        {
            if (!CurrencyManager.HasInstance) return;
            if (_txtGold != null)
                _txtGold.text = CurrencyManager.Instance.Coins.ToString("N0");
        }

        /// <summary>상품 리스트 초기화 + 첫 페이지 로드.</summary>
        private void ResetAndLoadProducts(bool expanded = false)
        {
            _userExpandedMore = expanded;
            RefreshProductExposure();

            // 기존 아이템 제거
            foreach (var item in _spawnedItems)
            {
                if (item != null && item.gameObject != null)
                    Destroy(item.gameObject);
            }
            _spawnedItems.Clear();
            _displayedCount = 0;

            LoadMoreProducts(_userExpandedMore ? (_products != null ? _products.Length : -1) : -1);

            // 더 보기 버튼 상태
            UpdateMoreButton();
        }

        /// <summary>다음 페이지 상품 추가.</summary>
        private void OnMoreProductsClicked()
        {
            ResetAndLoadProducts(expanded: true);
        }

        private void LoadMoreProducts(int loadOverride = -1)
        {
            if (_products == null || _listItemPrefab == null || _shopContent == null) return;

            int remaining = _products.Length - _displayedCount;
            int loadCount = loadOverride > 0 ? Mathf.Min(loadOverride, remaining) : Mathf.Min(ITEMS_PER_PAGE, remaining);
            for (int i = 0; i < loadCount; i++)
            {
                int idx = _displayedCount + i;
                var go = Instantiate(_listItemPrefab, _shopContent);
                go.SetActive(true);
                // [2026-05-13] 동적 spawn 카드 buy 버튼 등에 더블 클릭 가드.
                UIButtonClickGuard.AttachToHierarchy(go);

                // BtnMoreProducts 바로 위에 배치
                if (_btnMoreProducts != null)
                    go.transform.SetSiblingIndex(_btnMoreProducts.transform.GetSiblingIndex());

                var item = go.GetComponent<PopupShopListItem>();
                if (item != null)
                {
                    item.Setup(_products[idx], OnProductBuy);
                    _spawnedItems.Add(item);
                }
            }

            _displayedCount += loadCount;
            UpdateMoreButton();
        }

        private void UpdateMoreButton()
        {
            if (_btnMoreProducts != null)
                _btnMoreProducts.gameObject.SetActive(!_userExpandedMore && _moreOffersAvailable);
        }

        /// <summary>상품 구매 콜백.</summary>
        private void OnProductBuy(ShopProductData product)
        {
            Debug.Log($"[PopupGoldShop] Buy: {product.productId}, {product.title}, {product.price}");

            if (ShopManager.HasInstance)
                ShopManager.Instance.PurchaseProduct(product.productId);

            RefreshGold();
        }
    }

    /// <summary>상점 상품 데이터.</summary>
    [System.Serializable]
    public class ShopProductData
    {
        public string productId;
        public string title;
        public string price;
        /// <summary>Inspector 임시 데이터용 직접 sprite 참조. Firestore 카탈로그 경로에선 사용 안 함 (imageKey 우선).</summary>
        public Sprite productImage;

        [Header("[할인]")]
        public bool hasDiscount;
        [Range(0, 100)]
        public int discountPercent;

        [Header("[시간 한정]")]
        public bool hasTimeLimit;
        public float timeLimitSeconds;

        [Header("[List Item Prefab 카테고리]")]
        [Tooltip("Gold = ShopListGold, General = ShopListItem (특가/번들), Ad = ShopListAd")]
        public ShopItemCategory category = ShopItemCategory.General;

        /// <summary>UI atlas (Const.ADDR_ATLAS_UI) 안의 sprite 이름. Firestore ShopProductDoc.imageKey 매핑.</summary>
        [HideInInspector]
        public string imageKey;

        /// <summary>좌측 가격 영역 골드 아이콘 atlas key. Firestore ShopProductDoc.goldIconKey 매핑. 빈 값이면 Const.SPR_ICONGOLD 기본.</summary>
        [HideInInspector]
        public string goldIconKey;

        /// <summary>Firestore ShopProductDoc.rewards 매핑. Inspector 임시 데이터에선 null.</summary>
        [HideInInspector]
        public ShopRewards rewards;
    }

    /// <summary>UI 의 List Item Prefab 선택용 카테고리.
    /// Resources/UI/UIAssets/Shop*.prefab 와 매핑.</summary>
    public enum ShopItemCategory
    {
        /// <summary>Gold/Coin pack — ShopListGold.prefab</summary>
        Gold = 0,
        /// <summary>일반/특가/번들/부스터 — ShopListItem.prefab</summary>
        General = 1,
        /// <summary>광고 보상 — ShopListAd.prefab</summary>
        Ad = 2
    }
}
