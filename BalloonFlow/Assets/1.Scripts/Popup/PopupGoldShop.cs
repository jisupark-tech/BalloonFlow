using UnityEngine;
using UnityEngine.UI;

namespace BalloonFlow
{
    /// <summary>
    /// 골드 상점 팝업. Resources/Popup/PopupGoldShop 프리팹에서 로드.
    /// Lobby, InGame 공용.
    /// </summary>
    public class PopupGoldShop : UIBase
    {
        [Header("[버튼]")]
        [SerializeField] private Button _closeButton;

        [Header("[상품 리스트 부모]")]
        [SerializeField] private Transform _contentRoot;

        private Font _font;

        public Button CloseButton => _closeButton;
        public Transform ContentRoot => _contentRoot;

        protected override void Awake()
        {
            base.Awake();
            _font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            if (_font == null) _font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            if (_font == null) _font = Font.CreateDynamicFontFromOSFont("Arial", 14);
        }

        /// <summary>열기 시 상품 리스트 빌드</summary>
        public override void OpenUI()
        {
            BuildItems();
            base.OpenUI();
        }

        #region 상품 리스트 빌드

        public void BuildItems()
        {
            if (_contentRoot == null) return;

            // 기존 아이템 제거
            for (int i = _contentRoot.childCount - 1; i >= 0; i--)
                Destroy(_contentRoot.GetChild(i).gameObject);

            if (!ShopManager.HasInstance) return;

            ShopProduct[] _products = ShopManager.Instance.GetProducts();
            float _yOffset = 0f;
            const float ITEM_HEIGHT = 90f;
            const float SPACING = 8f;

            foreach (ShopProduct _product in _products)
            {
                if (_product.currencyType != "coin" && _product.currencyType != "iap") continue;

                var _itemGO = new GameObject($"Item_{_product.productId}");
                _itemGO.layer = LayerMask.NameToLayer("UI");
                _itemGO.transform.SetParent(_contentRoot, false);
                var _rt = _itemGO.AddComponent<RectTransform>();
                _rt.anchorMin = new Vector2(0, 1);
                _rt.anchorMax = new Vector2(1, 1);
                _rt.pivot = new Vector2(0.5f, 1);
                _rt.offsetMin = new Vector2(20, _yOffset - ITEM_HEIGHT);
                _rt.offsetMax = new Vector2(-20, _yOffset);
                var _img = _itemGO.AddComponent<Image>();
                _img.color = new Color(0.12f, 0.14f, 0.24f);
                _img.raycastTarget = false;

                // 상품명
                CreateItemText(_itemGO.transform, "Name", _product.displayName, 22, Color.white,
                    new Vector2(0, 0.5f), new Vector2(0.6f, 0.5f), new Vector2(0, 0.5f),
                    new Vector2(10, -15), new Vector2(0, 15), TextAnchor.MiddleLeft);

                // 설명
                CreateItemText(_itemGO.transform, "Desc", _product.description, 14,
                    new Color(0.7f, 0.7f, 0.8f),
                    new Vector2(0, 0.5f), new Vector2(0.6f, 0.5f), new Vector2(0, 0.5f),
                    new Vector2(10, -30), new Vector2(0, -8), TextAnchor.MiddleLeft);

                // 구매 버튼
                string _buyLabel = _product.currencyType == "iap" ? _product.priceDisplay : $"{_product.coinPrice}";
                var _buyBtn = CreateBuyButton(_itemGO.transform, _buyLabel);

                string _capturedId = _product.productId;
                _buyBtn.onClick.AddListener(() =>
                {
                    if (ShopManager.HasInstance)
                    {
                        ShopManager.Instance.PurchaseProduct(_capturedId);
                        if (CurrencyManager.HasInstance)
                            EventBus.Publish(new OnCoinChanged { currentCoins = CurrencyManager.Instance.Coins });
                    }
                });

                _yOffset -= (ITEM_HEIGHT + SPACING);
            }
        }

        #endregion

        #region Helpers

        private void CreateItemText(Transform _parent, string _name, string _content, int _fontSize,
            Color _color, Vector2 _anchorMin, Vector2 _anchorMax, Vector2 _pivot,
            Vector2 _offsetMin, Vector2 _offsetMax, TextAnchor _alignment)
        {
            var _go = new GameObject(_name);
            _go.layer = LayerMask.NameToLayer("UI");
            _go.transform.SetParent(_parent, false);
            var _rt = _go.AddComponent<RectTransform>();
            _rt.anchorMin = _anchorMin;
            _rt.anchorMax = _anchorMax;
            _rt.pivot = _pivot;
            _rt.offsetMin = _offsetMin;
            _rt.offsetMax = _offsetMax;
            var _t = _go.AddComponent<Text>();
            _t.text = _content;
            _t.fontSize = _fontSize;
            _t.font = _font;
            _t.color = _color;
            _t.alignment = _alignment;
            _t.raycastTarget = false;
        }

        private Button CreateBuyButton(Transform _parent, string _label)
        {
            var _go = new GameObject("BuyBtn");
            _go.layer = LayerMask.NameToLayer("UI");
            _go.transform.SetParent(_parent, false);
            var _rt = _go.AddComponent<RectTransform>();
            _rt.anchorMin = new Vector2(1, 0.5f);
            _rt.anchorMax = new Vector2(1, 0.5f);
            _rt.pivot = new Vector2(1, 0.5f);
            _rt.anchoredPosition = new Vector2(-10, 0);
            _rt.sizeDelta = new Vector2(120, 50);
            var _img = _go.AddComponent<Image>();
            _img.color = new Color(0.2f, 0.65f, 0.9f);
            var _btn = _go.AddComponent<Button>();

            var _lblGO = new GameObject("Label");
            _lblGO.layer = LayerMask.NameToLayer("UI");
            _lblGO.transform.SetParent(_go.transform, false);
            var _lblRT = _lblGO.AddComponent<RectTransform>();
            _lblRT.anchorMin = Vector2.zero;
            _lblRT.anchorMax = Vector2.one;
            _lblRT.offsetMin = Vector2.zero;
            _lblRT.offsetMax = Vector2.zero;
            var _t = _lblGO.AddComponent<Text>();
            _t.text = _label;
            _t.fontSize = 18;
            _t.font = _font;
            _t.color = Color.white;
            _t.alignment = TextAnchor.MiddleCenter;
            _t.fontStyle = FontStyle.Bold;
            _t.raycastTarget = false;

            return _btn;
        }

        #endregion
    }
}
