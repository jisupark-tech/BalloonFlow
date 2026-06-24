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
        [Tooltip("BtnMoreProductsBlue처럼 실제 Button이 래퍼 안에 있을 때 Scroll content에서 배치/숨김 처리할 루트. 미할당 시 Button 부모로 폴백.")]
        [SerializeField] private GameObject _btnMoreProductsRoot;

        [Header("[상품 아이템 프리팹]")]
        [SerializeField] private GameObject _listItemPrefab;
        [SerializeField] private GameObject _prefabGold;
        [SerializeField] private GameObject _prefabGoldAlign;
        [SerializeField] private GameObject _prefabGeneral;
        [SerializeField] private GameObject _prefabAd;
        [SerializeField] private bool _autoLoadFromResources = true;
        [SerializeField] private float _itemHeightOverride = 0f;

        [Header("[상품 데이터]")]
        [SerializeField] private ShopProductData[] _products;

        /// <summary>현재 표시된 상품 수.</summary>
        private int _displayedCount;

        /// <summary>한 번에 표시할 상품 수.</summary>
        private const int ITEMS_PER_PAGE = 6;

        /// <summary>생성된 아이템 리스트 (무한 스크롤 풀링용).</summary>
        private readonly List<PopupShopListItem> _spawnedItems = new List<PopupShopListItem>();
        private readonly List<GameObject> _spawnedRoots = new List<GameObject>();

        private System.Action _onCloseCallback;
        private bool _userExpandedMore;
        private bool _moreOffersAvailable;
        private const float DEFAULT_ITEM_HEIGHT = 200f;
        private RectTransform _viewport;

        private GameObject MoreButtonRoot
        {
            get
            {
                if (_btnMoreProductsRoot != null) return _btnMoreProductsRoot;
                if (_btnMoreProducts != null && _btnMoreProducts.transform.parent != null)
                {
                    if (_shopContent != null && _btnMoreProducts.transform.parent == _shopContent)
                        return _btnMoreProducts.gameObject;
                    return _btnMoreProducts.transform.parent.gameObject;
                }
                return _btnMoreProducts != null ? _btnMoreProducts.gameObject : null;
            }
        }

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

            // ROLLBACK_GOLDSHOP_MORE_BUTTON_CLICKABLE_20260623: More 버튼이 미할당/비활성이면 텍스트만 보이고 클릭 불가.
            //   UIShop 처럼 견고하게 — 미할당 시 이름으로 자동 해결 + interactable/raycast 보장 + 리스너 재바인딩.
            if (_btnMoreProducts == null)
            {
                Transform mt = FindChildRecursive(transform, "BtnMoreProducts")
                            ?? FindChildRecursive(transform, "BtnMoreProductsBlue");
                if (mt != null) _btnMoreProducts = mt.GetComponent<Button>();
            }
            if (_btnMoreProducts != null)
            {
                _btnMoreProducts.interactable = true;
                if (_btnMoreProducts.targetGraphic != null) _btnMoreProducts.targetGraphic.raycastTarget = true;
                _btnMoreProducts.onClick.RemoveListener(OnMoreProductsClicked); // Awake 재진입 시 중복 방지
                _btnMoreProducts.onClick.AddListener(OnMoreProductsClicked);
                var moreTexts = _btnMoreProducts.GetComponentsInChildren<TMP_Text>(true);
                for (int i = 0; i < moreTexts.Length; i++)
                {
                    if (moreTexts[i] != null) moreTexts[i].text = LocalizationService.Get("ui.shop.more_offers");
                }
                EnsureMoreButtonLayout();
            }
            else
            {
                Debug.LogWarning("[PopupGoldShop] _btnMoreProducts 미할당 — Inspector 에서 More 버튼(BtnMoreProducts) 드래그 필요.");
            }

            LoadShopPrefabs();
            EnsureContentLayout();
            CacheScrollViewport();

            EnsureTopBarBinding();
            SubscribeToCatalog();
        }

        // InGame 중 GoldShop 열림 시 게임 일시 정지 (PopupSettings 패턴 동일).
        private bool _paused;
        private void OnEnable()
        {
            GoldPanelFxFireUtil.DisableUnderTopBarRoot(transform);
            EventBus.Subscribe<OnPurchaseCompleted>(HandlePurchaseCompleted);
            EventBus.Subscribe<OnPurchaseRestored>(HandlePurchaseRestored);
            EventBus.Subscribe<OnAdsRemovedChanged>(HandleAdsRemovedChanged);
            if (!_paused) { PauseManager.Pause(); _paused = true; }
        }

        private void OnDisable()
        {
            EventBus.Unsubscribe<OnPurchaseCompleted>(HandlePurchaseCompleted);
            EventBus.Unsubscribe<OnPurchaseRestored>(HandlePurchaseRestored);
            EventBus.Unsubscribe<OnAdsRemovedChanged>(HandleAdsRemovedChanged);
            if (_paused) { PauseManager.Resume(); _paused = false; }
        }

        protected override void OnDestroy()
        {
            base.OnDestroy();

            if (ShopCatalogService.HasInstance)
                ShopCatalogService.Instance.OnCatalogLoaded -= OnCatalogReady;
            EventBus.Unsubscribe<OnPurchaseCompleted>(HandlePurchaseCompleted);
            EventBus.Unsubscribe<OnPurchaseRestored>(HandlePurchaseRestored);
            EventBus.Unsubscribe<OnAdsRemovedChanged>(HandleAdsRemovedChanged);
        }

        private void SubscribeToCatalog()
        {
            if (!ShopCatalogService.HasInstance)
            {
                ClearProductsUntilCatalogReady();
                return;
            }
            ShopCatalogService.Instance.OnCatalogLoaded += OnCatalogReady;
            if (ShopCatalogService.Instance.IsLoaded)
            {
                RefreshProductExposure();
            }
            else
            {
                ClearProductsUntilCatalogReady();
                ShopCatalogService.Instance.RetryFetch();
            }
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
            if (gold != null) GoldPanelFxFireUtil.DisableUnderGoldPanel(gold);
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

        private void LoadShopPrefabs()
        {
            if (_prefabGeneral == null) _prefabGeneral = _listItemPrefab;
            if (!_autoLoadFromResources) return;

            if (_prefabGold == null)
                _prefabGold = LoadShopPrefab("ShopListGold");
            if (_prefabGoldAlign == null)
                _prefabGoldAlign = LoadShopPrefab("ShopListGoldAlign");
            if (_prefabGeneral == null)
                _prefabGeneral = LoadShopPrefab("ShopListItem");
            if (_prefabAd == null)
                _prefabAd = LoadShopPrefab("ShopListAd");

            if (_listItemPrefab == null)
                _listItemPrefab = _prefabGeneral;
        }

        private static GameObject LoadShopPrefab(string name)
        {
            return Resources.Load<GameObject>("UI/UIAssets/" + name)
                ?? Resources.Load<GameObject>("UI/" + name);
        }

        private void EnsureContentLayout()
        {
            var contentRoot = _shopContent as RectTransform;
            if (contentRoot == null) return;

            contentRoot.anchorMin = new Vector2(0f, 1f);
            contentRoot.anchorMax = new Vector2(1f, 1f);
            contentRoot.pivot = new Vector2(0.5f, 1f);

            var vlg = contentRoot.GetComponent<VerticalLayoutGroup>();
            if (vlg == null) vlg = contentRoot.gameObject.AddComponent<VerticalLayoutGroup>();
            vlg.childControlHeight = true;
            vlg.childControlWidth = false;
            vlg.childForceExpandHeight = false;
            vlg.childForceExpandWidth = false;
            vlg.childAlignment = TextAnchor.UpperCenter;
            if (vlg.spacing < 1f) vlg.spacing = 20f;
            if (vlg.padding.bottom < 500) vlg.padding.bottom = 500;

            var csf = contentRoot.GetComponent<ContentSizeFitter>();
            if (csf == null) csf = contentRoot.gameObject.AddComponent<ContentSizeFitter>();
            csf.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            csf.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;

            if (_scrollView != null)
            {
                _scrollView.horizontal = false;
                _scrollView.vertical = true;
                _scrollView.movementType = ScrollRect.MovementType.Elastic;
                _scrollView.elasticity = 0.1f;
                _scrollView.inertia = true;
                _scrollView.decelerationRate = 0.135f;
                if (_scrollView.scrollSensitivity < 30f) _scrollView.scrollSensitivity = 60f;
            }
            CacheScrollViewport();

            var moreRoot = MoreButtonRoot;
            EnsureMoreButtonLayout();
            if (moreRoot != null && moreRoot.transform.parent == contentRoot)
                moreRoot.transform.SetAsLastSibling();
        }

        private void EnsureMoreButtonLayout()
        {
            // ROLLBACK_POPUP_GOLD_SHOP_MORE_BLUE_LAYOUT_20260624:
            // BtnMoreProductsBlue can be the actual Button under a wrapper. The Scroll content's
            // VerticalLayoutGroup lays out the wrapper, not the child Button, so the wrapper must
            // have a stable preferred height or the button becomes invisible in-game.
            var root = MoreButtonRoot;
            if (root == null) return;
            RestoreMoreButtonGraphics(root);

            if (_btnMoreProducts != null)
            {
                _btnMoreProducts.gameObject.SetActive(true);
                _btnMoreProducts.interactable = true;
                if (_btnMoreProducts.targetGraphic != null)
                    _btnMoreProducts.targetGraphic.raycastTarget = true;
                RestoreMoreButtonGraphics(_btnMoreProducts.gameObject);
            }

            if (_shopContent == null || root.transform.parent != _shopContent)
                return;

            var le = root.GetComponent<LayoutElement>();
            if (le == null) le = root.AddComponent<LayoutElement>();
            le.ignoreLayout = false;

            if (le.preferredHeight <= 0f)
            {
                float height = 0f;
                if (root.transform is RectTransform rootRt && rootRt.rect.height > 1f)
                    height = rootRt.rect.height;
                else if (_btnMoreProducts != null && _btnMoreProducts.transform is RectTransform btnRt && btnRt.rect.height > 1f)
                    height = btnRt.rect.height;

                le.preferredHeight = height > 1f ? height : 120f;
            }

            root.transform.SetAsLastSibling();
        }

        private static void RestoreMoreButtonGraphics(GameObject root)
        {
            // ROLLBACK_POPUP_GOLD_SHOP_MORE_GRAPHICS_20260624:
            // Some prefab variants leave only the TMP label visible after wiring BtnMoreProductsBlue.
            // Re-enable decorative Images under the selected button/root without touching the text.
            if (root == null) return;
            var images = root.GetComponentsInChildren<Image>(true);
            for (int i = 0; i < images.Length; i++)
            {
                Image image = images[i];
                if (image == null) continue;
                image.gameObject.SetActive(true);
                image.enabled = true;
                Color c = image.color;
                if (c.a <= 0.01f) c.a = 1f;
                image.color = c;
                image.raycastTarget = true;
            }
        }

        private void CacheScrollViewport()
        {
            if (_scrollView == null)
            {
                _viewport = null;
                return;
            }
            _viewport = _scrollView.viewport != null ? _scrollView.viewport : _scrollView.transform as RectTransform;
        }

        private GameObject GetPrefabForCategory(ShopItemCategory category)
        {
            switch (category)
            {
                case ShopItemCategory.Gold:
                    return _prefabGold != null ? _prefabGold : _prefabGeneral;
                case ShopItemCategory.Ad:
                    return _prefabAd != null ? _prefabAd : _prefabGeneral;
                case ShopItemCategory.General:
                default:
                    return _prefabGeneral != null ? _prefabGeneral : _listItemPrefab;
            }
        }

        public void OpenWithCloseCallback(System.Action onClose)
        {
            SetCloseCallback(onClose);
            OpenUI();
        }

        public void SetCloseCallback(System.Action onClose)
        {
            _onCloseCallback = onClose;
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
                _frame.SetTitle(LocalizationService.Get("ui.shop.title"));
                _frame.SetButtonLayout(PopupCommonFrame.ButtonLayout.None);
                _frame.ShowExitButton(true);
            }
            base.OpenUI();
            RefreshGold();
            LoadShopPrefabs();
            EnsureContentLayout();
            EnsureCatalogForOpen();
            _userExpandedMore = false;
            ResetAndLoadProducts(expanded: false);
            ApplyScrollTop();
        }

        /// <summary>보유 골드 즉시 스냅 — prefab 의 _txtGold 와이어링이 TopBar 가 아닌 다른 노드를
        /// 가리킬 때 fallback. TopBar 잔액은 AnimatedCoinLabel 이 EventBus 로 자동 갱신.</summary>
        private void RefreshProductExposure()
        {
            if (!ShopCatalogService.HasInstance || !ShopCatalogService.Instance.IsLoaded)
            {
                ClearProductsUntilCatalogReady();
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
            {
                ResetAndLoadProducts(_userExpandedMore);
            }
        }

        private void HandlePurchaseRestored(OnPurchaseRestored evt)
        {
            if (gameObject.activeInHierarchy)
            {
                ResetAndLoadProducts(_userExpandedMore);
            }
        }

        private void HandleAdsRemovedChanged(OnAdsRemovedChanged evt)
        {
            if (evt.removed && gameObject.activeInHierarchy)
            {
                ResetAndLoadProducts(_userExpandedMore);
            }
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
            // ROLLBACK_POPUP_GOLD_SHOP_SHARED_LAYOUT_20260617:
            // PopupGoldShop mirrors UILobby UIShop category layout. Restore the previous
            // single _listItemPrefab destroy/spawn path if a separate in-game popup design is needed.
            if (_spawnedRoots.Count > 0)
            {
                for (int i = 0; i < _spawnedRoots.Count; i++)
                {
                    if (_spawnedRoots[i] != null)
                        Destroy(_spawnedRoots[i]);
                }
            }
            else
            {
                foreach (var item in _spawnedItems)
                {
                    if (item != null && item.gameObject != null)
                        Destroy(item.gameObject);
                }
            }
            _spawnedRoots.Clear();
            _spawnedItems.Clear();
            _displayedCount = 0;

            LoadMoreProducts(_userExpandedMore ? (_products != null ? _products.Length : -1) : -1);
            ForceRebuildAndRefresh();

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
            if (_products == null || _shopContent == null) return;

            int remaining = _products.Length - _displayedCount;
            int loadCount = loadOverride > 0 ? Mathf.Min(loadOverride, remaining) : Mathf.Min(ITEMS_PER_PAGE, remaining);
            if (UseSharedPopupShopLayout())
            {
                LoadMoreProductsShared(loadOverride);
                return;
            }
            if (_listItemPrefab == null) return;
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

        private bool UseSharedPopupShopLayout()
        {
            return _prefabGeneral != null || _prefabGold != null || _prefabAd != null;
        }

        private void LoadMoreProductsShared(int loadOverride = -1)
        {
            int remaining = _products.Length - _displayedCount;
            int loadCount = loadOverride > 0 ? Mathf.Min(loadOverride, remaining) : Mathf.Min(ITEMS_PER_PAGE, remaining);
            GameObject goldContainer = null;

            for (int i = 0; i < loadCount; i++)
            {
                int idx = _displayedCount + i;
                var data = _products[idx];

                if (data.category == ShopItemCategory.Gold && _prefabGoldAlign != null && _prefabGold != null)
                {
                    if (goldContainer == null)
                    {
                        goldContainer = Instantiate(_prefabGoldAlign, _shopContent);
                        goldContainer.SetActive(true);
                        _spawnedRoots.Add(goldContainer);

                        var moreRoot = MoreButtonRoot;
                        if (moreRoot != null && moreRoot.transform.parent == _shopContent)
                            goldContainer.transform.SetSiblingIndex(moreRoot.transform.GetSiblingIndex());

                        EnsurePreferredHeight(goldContainer);
                        for (int c = goldContainer.transform.childCount - 1; c >= 0; c--)
                            Destroy(goldContainer.transform.GetChild(c).gameObject);
                    }

                    var goldGo = Instantiate(_prefabGold, goldContainer.transform);
                    goldGo.SetActive(true);
                    UIButtonClickGuard.AttachToHierarchy(goldGo);

                    var goldItem = goldGo.GetComponent<PopupShopListItem>();
                    if (goldItem != null)
                    {
                        goldItem.Setup(data, OnProductBuy);
                        _spawnedItems.Add(goldItem);
                    }
                    continue;
                }

                goldContainer = null;
                GameObject prefab = GetPrefabForCategory(data.category);
                if (prefab == null) continue;

                var go = Instantiate(prefab, _shopContent);
                go.SetActive(true);
                _spawnedRoots.Add(go);
                UIButtonClickGuard.AttachToHierarchy(go);
                EnsurePreferredHeight(go);

                var root = MoreButtonRoot;
                if (root != null && root.transform.parent == _shopContent)
                    go.transform.SetSiblingIndex(root.transform.GetSiblingIndex());

                var item = go.GetComponent<PopupShopListItem>();
                if (item != null)
                {
                    item.Setup(data, OnProductBuy);
                    _spawnedItems.Add(item);
                }
            }

            _displayedCount += loadCount;
            var moreButtonRoot = MoreButtonRoot;
            if (moreButtonRoot != null && moreButtonRoot.transform.parent == _shopContent)
                moreButtonRoot.transform.SetAsLastSibling();

            var contentRoot = _shopContent as RectTransform;
            if (contentRoot != null)
            {
                LayoutRebuilder.ForceRebuildLayoutImmediate(contentRoot);
                Canvas.ForceUpdateCanvases();
            }

            UpdateMoreButton();
        }

        private void EnsurePreferredHeight(GameObject go)
        {
            if (go == null) return;
            var rt = go.transform as RectTransform;
            var le = go.GetComponent<LayoutElement>();
            if (le == null) le = go.AddComponent<LayoutElement>();
            if (le.preferredHeight > 0f) return;

            if (_itemHeightOverride > 0f)
                le.preferredHeight = _itemHeightOverride;
            else if (rt != null && rt.rect.height > 1f)
                le.preferredHeight = rt.rect.height;
            else
                le.preferredHeight = DEFAULT_ITEM_HEIGHT;
        }

        private void EnsureCatalogForOpen()
        {
            if (!ShopCatalogService.HasInstance)
            {
                ClearProductsUntilCatalogReady();
                return;
            }

            if (ShopCatalogService.Instance.IsLoaded)
            {
                RefreshProductExposure();
                return;
            }

            // ROLLBACK_POPUP_GOLD_SHOP_OPEN_AS_LOBBY_SHOP_20260619:
            // PopupGoldShop must behave like UILobby's Shop page when opened from in-game:
            // no stale prefab products, retry catalog fetch, then rebuild through OnCatalogReady.
            ClearProductsUntilCatalogReady();
            ShopCatalogService.Instance.RetryFetch();
        }

        private void ClearProductsUntilCatalogReady()
        {
            _products = System.Array.Empty<ShopProductData>();
            _moreOffersAvailable = false;
        }

        private void ForceRebuildAndRefresh()
        {
            var contentRoot = _shopContent as RectTransform;
            if (contentRoot != null)
            {
                LayoutRebuilder.ForceRebuildLayoutImmediate(contentRoot);
                Canvas.ForceUpdateCanvases();
            }
            RefreshAllParticleLights();
        }

        private void RefreshAllParticleLights()
        {
            for (int i = 0; i < _spawnedItems.Count; i++)
            {
                var item = _spawnedItems[i];
                if (item == null) continue;
                item.RefreshParticleLightVisibility(_viewport);
            }
        }

        private void ApplyScrollTop()
        {
            if (_scrollView != null)
            {
                _scrollView.StopMovement();
                _scrollView.verticalNormalizedPosition = 1f;
            }

            if (_shopContent is RectTransform contentRoot)
                contentRoot.anchoredPosition = Vector2.zero;
        }

        private void UpdateMoreButton()
        {
            var root = MoreButtonRoot;
            EnsureMoreButtonLayout();
            bool show = !_userExpandedMore && _moreOffersAvailable;
            if (root != null && root.activeSelf != show)
                root.SetActive(show);
            if (show && _btnMoreProducts != null)
            {
                _btnMoreProducts.gameObject.SetActive(true);
                _btnMoreProducts.interactable = true;
                if (_btnMoreProducts.targetGraphic != null)
                    _btnMoreProducts.targetGraphic.raycastTarget = true;
            }
            if (root != null && root.transform.parent == _shopContent)
                root.transform.SetAsLastSibling();
        }

        /// <summary>상품 구매 콜백.</summary>
        private void OnProductBuy(ShopProductData product)
        {
            Debug.Log($"[PopupGoldShop] Buy: {product.productId}, {product.title}, {product.price}");

            if (UIManager.HasInstance)
                UIManager.Instance.OpenUI<PopupLoadingSpinner>(Const.POPUP_LOADING_SPINNER);

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
