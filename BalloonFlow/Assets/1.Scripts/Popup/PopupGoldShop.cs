using UnityEngine;
using UnityEngine.UI;

namespace BalloonFlow
{
    /// <summary>
    /// Gold shop popup view. Loaded from Resources/Popup/PopupGoldShop prefab.
    /// Shared between Lobby and InGame scenes.
    /// Handles dynamic shop item creation from ShopManager products.
    /// All child references wired via UIPrefabBuilder at editor-time.
    /// </summary>
    public class PopupGoldShop : MonoBehaviour
    {
        [SerializeField] private Button _closeButton;
        [SerializeField] private Transform _contentRoot;

        private CanvasGroup _canvasGroup;
        private Font _font;

        public Button CloseButton => _closeButton;
        public Transform ContentRoot => _contentRoot;

        private void Awake()
        {
            _canvasGroup = GetComponent<CanvasGroup>();
            _font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            if (_font == null) _font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            if (_font == null) _font = Font.CreateDynamicFontFromOSFont("Arial", 14);
        }

        public void Show()
        {
            BuildItems();
            if (_canvasGroup == null) return;
            _canvasGroup.alpha = 1f;
            _canvasGroup.interactable = true;
            _canvasGroup.blocksRaycasts = true;
            gameObject.SetActive(true);
        }

        public void Hide()
        {
            if (_canvasGroup == null) return;
            _canvasGroup.alpha = 0f;
            _canvasGroup.interactable = false;
            _canvasGroup.blocksRaycasts = false;
        }

        public void BuildItems()
        {
            if (_contentRoot == null) return;

            // Clear existing
            for (int i = _contentRoot.childCount - 1; i >= 0; i--)
                Destroy(_contentRoot.GetChild(i).gameObject);

            if (!ShopManager.HasInstance) return;

            ShopProduct[] products = ShopManager.Instance.GetProducts();
            float yOffset = 0f;
            const float itemHeight = 90f;
            const float spacing = 8f;

            foreach (ShopProduct product in products)
            {
                if (product.currencyType != "coin" && product.currencyType != "iap") continue;

                var itemGO = new GameObject($"Item_{product.productId}");
                itemGO.layer = LayerMask.NameToLayer("UI");
                itemGO.transform.SetParent(_contentRoot, false);
                var rt = itemGO.AddComponent<RectTransform>();
                rt.anchorMin = new Vector2(0, 1);
                rt.anchorMax = new Vector2(1, 1);
                rt.pivot = new Vector2(0.5f, 1);
                rt.offsetMin = new Vector2(20, yOffset - itemHeight);
                rt.offsetMax = new Vector2(-20, yOffset);
                var img = itemGO.AddComponent<Image>();
                img.color = new Color(0.12f, 0.14f, 0.24f);
                img.raycastTarget = false;

                // Product name
                CreateItemText(itemGO.transform, "Name", product.displayName, 22, Color.white,
                    new Vector2(0, 0.5f), new Vector2(0.6f, 0.5f), new Vector2(0, 0.5f),
                    new Vector2(10, -15), new Vector2(0, 15), TextAnchor.MiddleLeft);

                // Description
                CreateItemText(itemGO.transform, "Desc", product.description, 14,
                    new Color(0.7f, 0.7f, 0.8f),
                    new Vector2(0, 0.5f), new Vector2(0.6f, 0.5f), new Vector2(0, 0.5f),
                    new Vector2(10, -30), new Vector2(0, -8), TextAnchor.MiddleLeft);

                // Buy button
                string buyLabel = product.currencyType == "iap" ? product.priceDisplay : $"{product.coinPrice}";
                var buyBtn = CreateBuyButton(itemGO.transform, buyLabel);

                string capturedId = product.productId;
                buyBtn.onClick.AddListener(() =>
                {
                    if (ShopManager.HasInstance)
                    {
                        ShopManager.Instance.PurchaseProduct(capturedId);
                        if (CurrencyManager.HasInstance)
                            EventBus.Publish(new OnCoinChanged { currentCoins = CurrencyManager.Instance.Coins });
                    }
                });

                yOffset -= (itemHeight + spacing);
            }
        }

        private void CreateItemText(Transform parent, string name, string content, int fontSize,
            Color color, Vector2 anchorMin, Vector2 anchorMax, Vector2 pivot,
            Vector2 offsetMin, Vector2 offsetMax, TextAnchor alignment)
        {
            var go = new GameObject(name);
            go.layer = LayerMask.NameToLayer("UI");
            go.transform.SetParent(parent, false);
            var rt = go.AddComponent<RectTransform>();
            rt.anchorMin = anchorMin;
            rt.anchorMax = anchorMax;
            rt.pivot = pivot;
            rt.offsetMin = offsetMin;
            rt.offsetMax = offsetMax;
            var t = go.AddComponent<Text>();
            t.text = content;
            t.fontSize = fontSize;
            t.font = _font;
            t.color = color;
            t.alignment = alignment;
            t.raycastTarget = false;
        }

        private Button CreateBuyButton(Transform parent, string label)
        {
            var go = new GameObject("BuyBtn");
            go.layer = LayerMask.NameToLayer("UI");
            go.transform.SetParent(parent, false);
            var rt = go.AddComponent<RectTransform>();
            rt.anchorMin = new Vector2(1, 0.5f);
            rt.anchorMax = new Vector2(1, 0.5f);
            rt.pivot = new Vector2(1, 0.5f);
            rt.anchoredPosition = new Vector2(-10, 0);
            rt.sizeDelta = new Vector2(120, 50);
            var img = go.AddComponent<Image>();
            img.color = new Color(0.2f, 0.65f, 0.9f);
            var btn = go.AddComponent<Button>();

            var lblGO = new GameObject("Label");
            lblGO.layer = LayerMask.NameToLayer("UI");
            lblGO.transform.SetParent(go.transform, false);
            var lblRT = lblGO.AddComponent<RectTransform>();
            lblRT.anchorMin = Vector2.zero;
            lblRT.anchorMax = Vector2.one;
            lblRT.offsetMin = Vector2.zero;
            lblRT.offsetMax = Vector2.zero;
            var t = lblGO.AddComponent<Text>();
            t.text = label;
            t.fontSize = 18;
            t.font = _font;
            t.color = Color.white;
            t.alignment = TextAnchor.MiddleCenter;
            t.fontStyle = FontStyle.Bold;
            t.raycastTarget = false;

            return btn;
        }
    }
}
