using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using DG.Tweening;

namespace BalloonFlow
{
    /// <summary>
    /// 골드 상점 팝업.
    /// Inspector에서 UI 링크 연결. 상품 리스트는 PopupShopListItem 프리팹으로 동적 생성.
    /// BtnMoreProducts: 스크롤에 아이템 추가.
    /// </summary>
    public class PopupGoldShop : UIBase
    {
        [Header("[Common Frame]")]
        [SerializeField] private PopupCommonFrame _frame;

        [Header("[Exit Button — 직접 와이어링 (선택)]")]
        [Tooltip("prefab 안의 나가기 버튼을 Inspector에서 직접 연결. 미연결 시 _frame.BtnExit 와 이름 기반 자동 탐색으로 fallback.")]
        [SerializeField] private Button _btnExitDirect;

        [Header("[Top Panel]")]
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

        private int _displayedCoins;
        private Tweener _goldTween;

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
                _btnMoreProducts.onClick.AddListener(LoadMoreProducts);
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
            ResetAndLoadProducts();
        }

        private void OnEnable()
        {
            EventBus.Subscribe<OnCoinChanged>(HandleCoinChanged);
            if (CurrencyManager.HasInstance)
            {
                _displayedCoins = CurrencyManager.Instance.Coins;
                ApplyTopBarGold(_displayedCoins);
            }
        }

        private void OnDisable()
        {
            EventBus.Unsubscribe<OnCoinChanged>(HandleCoinChanged);
            _goldTween?.Kill();
        }

        protected override void OnDestroy()
        {
            base.OnDestroy();
            _goldTween?.Kill();
        }

        private void HandleCoinChanged(OnCoinChanged evt)
        {
            if (evt.delta == 0)
            {
                _goldTween?.Kill();
                _displayedCoins = evt.currentCoins;
                ApplyTopBarGold(evt.currentCoins);
                return;
            }
            AnimateTopBarGold(evt.currentCoins);
        }

        private void AnimateTopBarGold(int target)
        {
            _goldTween?.Kill();
            if (_displayedCoins == target)
            {
                ApplyTopBarGold(target);
                return;
            }
            _goldTween = DOTween.To(
                    () => _displayedCoins,
                    v => { _displayedCoins = v; ApplyTopBarGold(v); },
                    target,
                    0.45f)
                .SetEase(Ease.OutCubic)
                .SetUpdate(true)
                .OnComplete(() =>
                {
                    _displayedCoins = target;
                    ApplyTopBarGold(target);
                });
        }

        private void ApplyTopBarGold(int value)
        {
            if (_txtGold != null) _txtGold.text = value.ToString("N0");
        }

        /// <summary>보유 골드 갱신 — 즉시 스냅(트윈 종료) + 캐시 동기화.</summary>
        private void RefreshGold()
        {
            if (!CurrencyManager.HasInstance) return;
            _goldTween?.Kill();
            _displayedCoins = CurrencyManager.Instance.Coins;
            ApplyTopBarGold(_displayedCoins);
        }

        /// <summary>상품 리스트 초기화 + 첫 페이지 로드.</summary>
        private void ResetAndLoadProducts()
        {
            // 기존 아이템 제거
            foreach (var item in _spawnedItems)
            {
                if (item != null && item.gameObject != null)
                    Destroy(item.gameObject);
            }
            _spawnedItems.Clear();
            _displayedCount = 0;

            LoadMoreProducts();

            // 더 보기 버튼 상태
            UpdateMoreButton();
        }

        /// <summary>다음 페이지 상품 추가.</summary>
        private void LoadMoreProducts()
        {
            if (_products == null || _listItemPrefab == null || _shopContent == null) return;

            int loadCount = Mathf.Min(ITEMS_PER_PAGE, _products.Length - _displayedCount);
            for (int i = 0; i < loadCount; i++)
            {
                int idx = _displayedCount + i;
                var go = Instantiate(_listItemPrefab, _shopContent);
                go.SetActive(true);

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
                _btnMoreProducts.gameObject.SetActive(_products != null && _displayedCount < _products.Length);
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
