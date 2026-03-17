using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace BalloonFlow
{
    /// <summary>
    /// 골드 상점 팝업 — 레퍼런스 게임 구조 재현.
    /// 섹션 1: 골드 팩 (3x2 그리드, IAP)
    /// 섹션 2: 번들 (세로 카드 리스트, IAP — 골드+부스터+무한하트)
    /// 하단 탭바: 상점 | 돼지저금통(미구현) | 잠금(미구현)
    /// </summary>
    public class PopupGoldShop : UIBase
    {
        [Header("[버튼]")]
        [SerializeField] private Button _closeButton;

        [Header("[상품 리스트 부모]")]
        [SerializeField] private Transform _contentRoot;

        private Font _font;
        private ScrollRect _scrollRect;

        public Button CloseButton => _closeButton;
        public Transform ContentRoot => _contentRoot;

        // ── Colors (레퍼런스 매칭) ──
        private static readonly Color COL_BG         = new Color(0.06f, 0.12f, 0.28f);
        private static readonly Color COL_SECTION_BAR = new Color(0.10f, 0.14f, 0.32f);
        private static readonly Color COL_CARD_GOLD   = new Color(0.95f, 0.85f, 0.55f);
        private static readonly Color COL_CARD_SMALL  = new Color(0.35f, 0.70f, 0.95f);
        private static readonly Color COL_CARD_MED    = new Color(0.55f, 0.30f, 0.70f);
        private static readonly Color COL_CARD_ULTRA  = new Color(0.85f, 0.55f, 0.15f);
        private static readonly Color COL_PRICE_BTN   = new Color(0.20f, 0.70f, 0.30f);
        private static readonly Color COL_TAB_ACTIVE  = new Color(0.25f, 0.55f, 0.85f);
        private static readonly Color COL_TAB_INACTIVE = new Color(0.15f, 0.20f, 0.35f);

        // ── Gold Pack 데이터 (레퍼런스 기준) ──
        private static readonly int[] GOLD_AMOUNTS = { 1000, 5000, 10000, 25000, 50000, 100000 };
        private static readonly string[] GOLD_PRICES = { "KRW 2,751", "KRW 11,007", "KRW 20,638", "KRW 41,277", "KRW 75,675", "KRW 137,591" };
        private static readonly string[] GOLD_IDS = { "gold_1k", "gold_5k", "gold_10k", "gold_25k", "gold_50k", "gold_100k" };

        // ── Bundle 데이터 (레퍼런스 기준) ──
        private struct BundleData
        {
            public string id, name, price;
            public int gold;
            public int boosterMultiplier;
            public string heartDuration;
            public Color cardColor;
        }

        private static readonly BundleData[] BUNDLES =
        {
            new BundleData { id = "bundle_small",  name = "소형 번들",   price = "KRW 13,759", gold = 5000,  boosterMultiplier = 1, heartDuration = "2시간", cardColor = COL_CARD_SMALL },
            new BundleData { id = "bundle_medium", name = "중형 번들",   price = "KRW 27,518", gold = 10000, boosterMultiplier = 2, heartDuration = "2시간", cardColor = COL_CARD_MED },
            new BundleData { id = "bundle_ultra",  name = "울트라 번들", price = "KRW 59,000", gold = 25000, boosterMultiplier = 5, heartDuration = "6시간", cardColor = COL_CARD_ULTRA },
        };

        // ── Booster names (아이콘 대용 텍스트) ──
        private static readonly string[] BOOSTER_LABELS = { "+Tray", "Shuffle", "Select", "ColorRm" };
        private static readonly Color[] BOOSTER_COLORS =
        {
            new Color(0.3f, 0.8f, 0.3f), // green
            new Color(0.9f, 0.6f, 0.2f), // orange
            new Color(0.4f, 0.6f, 0.9f), // blue
            new Color(0.9f, 0.3f, 0.4f), // red
        };

        protected override void Awake()
        {
            base.Awake();
            _font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            if (_font == null) _font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            if (_font == null) _font = Font.CreateDynamicFontFromOSFont("Arial", 14);
        }

        public override void OpenUI()
        {
            BuildShopUI();
            base.OpenUI();
        }

        #region Build Shop UI

        private void BuildShopUI()
        {
            if (_contentRoot == null) return;

            // Clear existing
            for (int i = _contentRoot.childCount - 1; i >= 0; i--)
                Destroy(_contentRoot.GetChild(i).gameObject);

            // Add VerticalLayoutGroup + ContentSizeFitter to content root for auto-sizing
            var vlg = _contentRoot.gameObject.GetComponent<VerticalLayoutGroup>();
            if (vlg == null) vlg = _contentRoot.gameObject.AddComponent<VerticalLayoutGroup>();
            vlg.childAlignment = TextAnchor.UpperCenter;
            vlg.spacing = 10;
            vlg.padding = new RectOffset(15, 15, 10, 10);
            vlg.childForceExpandWidth = true;
            vlg.childForceExpandHeight = false;
            vlg.childControlWidth = true;
            vlg.childControlHeight = false;

            var csf = _contentRoot.gameObject.GetComponent<ContentSizeFitter>();
            if (csf == null) csf = _contentRoot.gameObject.AddComponent<ContentSizeFitter>();
            csf.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            // ── Section: 골드 팩 ──
            BuildSectionHeader("골드 팩");
            BuildGoldPackGrid();

            // ── Section: 번들 ──
            BuildSectionHeader("번들");
            foreach (var bundle in BUNDLES)
                BuildBundleCard(bundle);

            // Spacer at bottom
            BuildSpacer(20);
        }

        #endregion

        #region Section: Gold Pack Grid

        private void BuildSectionHeader(string title)
        {
            var go = MakeGO("Section_" + title, _contentRoot);
            var le = go.AddComponent<LayoutElement>();
            le.preferredHeight = 45;
            var img = go.AddComponent<Image>();
            img.color = COL_SECTION_BAR;
            img.raycastTarget = false;

            // Rounded look (slightly lighter)
            var txt = MakeText(go.transform, title, 24, FontStyle.Bold, TextAnchor.MiddleCenter, Color.white);
            StretchFill(txt.rectTransform);
        }

        private void BuildGoldPackGrid()
        {
            // Container with GridLayoutGroup (3 columns, 2 rows)
            var gridGO = MakeGO("GoldGrid", _contentRoot);
            var le = gridGO.AddComponent<LayoutElement>();
            le.preferredHeight = 380; // 2 rows

            var glg = gridGO.AddComponent<GridLayoutGroup>();
            glg.cellSize = new Vector2(200, 170);
            glg.spacing = new Vector2(12, 12);
            glg.startAxis = GridLayoutGroup.Axis.Horizontal;
            glg.startCorner = GridLayoutGroup.Corner.UpperLeft;
            glg.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            glg.constraintCount = 3;
            glg.childAlignment = TextAnchor.UpperCenter;
            glg.padding = new RectOffset(5, 5, 5, 5);

            for (int i = 0; i < GOLD_AMOUNTS.Length; i++)
                BuildGoldCard(gridGO.transform, i);
        }

        private void BuildGoldCard(Transform parent, int index)
        {
            var card = MakeGO("Gold_" + GOLD_AMOUNTS[index], parent);
            var cardImg = card.AddComponent<Image>();
            cardImg.color = COL_CARD_GOLD;
            cardImg.raycastTarget = true;

            // Gold icon placeholder (circle)
            var iconGO = MakeGO("Icon", card.transform);
            var iconRT = iconGO.GetComponent<RectTransform>();
            iconRT.anchorMin = new Vector2(0.5f, 1f);
            iconRT.anchorMax = new Vector2(0.5f, 1f);
            iconRT.pivot = new Vector2(0.5f, 1f);
            iconRT.anchoredPosition = new Vector2(0, -10);
            iconRT.sizeDelta = new Vector2(70, 70);
            var iconImg = iconGO.AddComponent<Image>();
            iconImg.color = new Color(0.95f, 0.75f, 0.1f);
            iconImg.raycastTarget = false;

            // Amount text
            var amountTxt = MakeText(card.transform, GOLD_AMOUNTS[index].ToString("N0"), 22,
                FontStyle.Bold, TextAnchor.MiddleCenter, new Color(0.3f, 0.2f, 0.05f));
            var amtRT = amountTxt.rectTransform;
            amtRT.anchorMin = new Vector2(0, 0.25f);
            amtRT.anchorMax = new Vector2(1, 0.55f);
            amtRT.offsetMin = Vector2.zero;
            amtRT.offsetMax = Vector2.zero;

            // Price button
            var btnGO = MakeGO("PriceBtn", card.transform);
            var btnRT = btnGO.GetComponent<RectTransform>();
            btnRT.anchorMin = new Vector2(0.1f, 0f);
            btnRT.anchorMax = new Vector2(0.9f, 0f);
            btnRT.pivot = new Vector2(0.5f, 0f);
            btnRT.anchoredPosition = new Vector2(0, 8);
            btnRT.sizeDelta = new Vector2(0, 36);
            var btnImg = btnGO.AddComponent<Image>();
            btnImg.color = COL_PRICE_BTN;
            var btn = btnGO.AddComponent<Button>();

            var priceTxt = MakeText(btnGO.transform, GOLD_PRICES[index], 14,
                FontStyle.Bold, TextAnchor.MiddleCenter, Color.white);
            StretchFill(priceTxt.rectTransform);

            // Purchase handler
            string capturedId = GOLD_IDS[index];
            int capturedAmount = GOLD_AMOUNTS[index];
            btn.onClick.AddListener(() => OnGoldPackClicked(capturedId, capturedAmount));
        }

        #endregion

        #region Section: Bundle Cards

        private void BuildBundleCard(BundleData data)
        {
            var card = MakeGO("Bundle_" + data.id, _contentRoot);
            var le = card.AddComponent<LayoutElement>();
            le.preferredHeight = 130;
            var cardImg = card.AddComponent<Image>();
            cardImg.color = data.cardColor;
            cardImg.raycastTarget = true;

            // ── Left: Gold amount ──
            var goldArea = MakeGO("GoldArea", card.transform);
            var goldRT = goldArea.GetComponent<RectTransform>();
            goldRT.anchorMin = new Vector2(0, 0);
            goldRT.anchorMax = new Vector2(0.22f, 1);
            goldRT.offsetMin = new Vector2(8, 8);
            goldRT.offsetMax = new Vector2(0, -8);

            // Gold icon
            var gIconGO = MakeGO("GoldIcon", goldArea.transform);
            var gIconRT = gIconGO.GetComponent<RectTransform>();
            gIconRT.anchorMin = new Vector2(0.5f, 0.5f);
            gIconRT.anchorMax = new Vector2(0.5f, 0.5f);
            gIconRT.sizeDelta = new Vector2(50, 50);
            gIconRT.anchoredPosition = new Vector2(0, 10);
            var gIconImg = gIconGO.AddComponent<Image>();
            gIconImg.color = new Color(0.95f, 0.75f, 0.1f);
            gIconImg.raycastTarget = false;

            var goldTxt = MakeText(goldArea.transform, data.gold.ToString("N0"), 18,
                FontStyle.Bold, TextAnchor.LowerCenter, Color.white);
            var goldTxtRT = goldTxt.rectTransform;
            goldTxtRT.anchorMin = new Vector2(0, 0);
            goldTxtRT.anchorMax = new Vector2(1, 0.3f);
            goldTxtRT.offsetMin = Vector2.zero;
            goldTxtRT.offsetMax = Vector2.zero;

            // ── Center: Boosters x multiplier ──
            var boosterArea = MakeGO("BoosterArea", card.transform);
            var boosterRT = boosterArea.GetComponent<RectTransform>();
            boosterRT.anchorMin = new Vector2(0.22f, 0.25f);
            boosterRT.anchorMax = new Vector2(0.72f, 0.95f);
            boosterRT.offsetMin = Vector2.zero;
            boosterRT.offsetMax = Vector2.zero;

            var hlg = boosterArea.AddComponent<HorizontalLayoutGroup>();
            hlg.spacing = 4;
            hlg.childAlignment = TextAnchor.MiddleCenter;
            hlg.childForceExpandWidth = true;
            hlg.childForceExpandHeight = true;
            hlg.childControlWidth = true;
            hlg.childControlHeight = true;

            for (int b = 0; b < BOOSTER_LABELS.Length; b++)
            {
                var bGO = MakeGO("B" + b, boosterArea.transform);
                var bImg = bGO.AddComponent<Image>();
                bImg.color = BOOSTER_COLORS[b];
                bImg.raycastTarget = false;
                var bTxt = MakeText(bGO.transform, $"x{data.boosterMultiplier}", 14,
                    FontStyle.Bold, TextAnchor.MiddleCenter, Color.white);
                StretchFill(bTxt.rectTransform);
            }

            // ── Right: Infinite heart + duration ──
            var heartArea = MakeGO("HeartArea", card.transform);
            var heartRT = heartArea.GetComponent<RectTransform>();
            heartRT.anchorMin = new Vector2(0.72f, 0.25f);
            heartRT.anchorMax = new Vector2(0.92f, 0.95f);
            heartRT.offsetMin = Vector2.zero;
            heartRT.offsetMax = Vector2.zero;

            // Heart icon
            var hIconGO = MakeGO("HeartIcon", heartArea.transform);
            var hIconRT = hIconGO.GetComponent<RectTransform>();
            hIconRT.anchorMin = new Vector2(0.5f, 0.55f);
            hIconRT.anchorMax = new Vector2(0.5f, 0.55f);
            hIconRT.sizeDelta = new Vector2(40, 40);
            var hIconImg = hIconGO.AddComponent<Image>();
            hIconImg.color = new Color(0.95f, 0.2f, 0.3f);
            hIconImg.raycastTarget = false;

            // Infinity symbol on heart
            var infTxt = MakeText(hIconGO.transform, "\u221E", 22, // ∞
                FontStyle.Bold, TextAnchor.MiddleCenter, Color.white);
            StretchFill(infTxt.rectTransform);

            // Duration text
            var durTxt = MakeText(heartArea.transform, data.heartDuration, 14,
                FontStyle.Bold, TextAnchor.LowerCenter, Color.white);
            var durRT = durTxt.rectTransform;
            durRT.anchorMin = new Vector2(0, 0);
            durRT.anchorMax = new Vector2(1, 0.35f);
            durRT.offsetMin = Vector2.zero;
            durRT.offsetMax = Vector2.zero;

            // ── Bottom: Name + Price button ──
            var nameTxt = MakeText(card.transform, data.name, 18,
                FontStyle.Bold, TextAnchor.MiddleLeft, Color.white);
            var nameRT = nameTxt.rectTransform;
            nameRT.anchorMin = new Vector2(0.02f, 0);
            nameRT.anchorMax = new Vector2(0.55f, 0.25f);
            nameRT.offsetMin = new Vector2(8, 0);
            nameRT.offsetMax = Vector2.zero;

            var priceBtnGO = MakeGO("PriceBtn", card.transform);
            var priceBtnRT = priceBtnGO.GetComponent<RectTransform>();
            priceBtnRT.anchorMin = new Vector2(0.55f, 0.02f);
            priceBtnRT.anchorMax = new Vector2(0.98f, 0.23f);
            priceBtnRT.offsetMin = Vector2.zero;
            priceBtnRT.offsetMax = Vector2.zero;
            var priceBtnImg = priceBtnGO.AddComponent<Image>();
            priceBtnImg.color = COL_PRICE_BTN;
            var priceBtn = priceBtnGO.AddComponent<Button>();

            var pTxt = MakeText(priceBtnGO.transform, data.price, 14,
                FontStyle.Bold, TextAnchor.MiddleCenter, Color.white);
            StretchFill(pTxt.rectTransform);

            string capturedId = data.id;
            priceBtn.onClick.AddListener(() => OnBundleClicked(capturedId));
        }

        #endregion

        #region Purchase Handlers

        private void OnGoldPackClicked(string productId, int goldAmount)
        {
            if (ShopManager.HasInstance)
            {
                ShopManager.Instance.PurchaseProduct(productId);
                if (CurrencyManager.HasInstance)
                    EventBus.Publish(new OnCoinChanged { currentCoins = CurrencyManager.Instance.Coins });
            }
            Debug.Log($"[PopupGoldShop] Gold pack purchased: {productId} ({goldAmount} gold)");
        }

        private void OnBundleClicked(string bundleId)
        {
            if (ShopManager.HasInstance)
            {
                ShopManager.Instance.PurchaseProduct(bundleId);
                if (CurrencyManager.HasInstance)
                    EventBus.Publish(new OnCoinChanged { currentCoins = CurrencyManager.Instance.Coins });
            }
            Debug.Log($"[PopupGoldShop] Bundle purchased: {bundleId}");
        }

        #endregion

        #region Helpers

        private void BuildSpacer(float height)
        {
            var go = MakeGO("Spacer", _contentRoot);
            var le = go.AddComponent<LayoutElement>();
            le.preferredHeight = height;
        }

        private GameObject MakeGO(string name, Transform parent)
        {
            var go = new GameObject(name);
            go.layer = LayerMask.NameToLayer("UI");
            go.transform.SetParent(parent, false);
            go.AddComponent<RectTransform>();
            return go;
        }

        private Text MakeText(Transform parent, string content, int fontSize,
            FontStyle style, TextAnchor align, Color color)
        {
            var go = new GameObject("Txt");
            go.layer = LayerMask.NameToLayer("UI");
            go.transform.SetParent(parent, false);
            var rt = go.AddComponent<RectTransform>();
            var txt = go.AddComponent<Text>();
            txt.text = content;
            txt.fontSize = fontSize;
            txt.fontStyle = style;
            txt.alignment = align;
            txt.color = color;
            txt.font = _font;
            txt.raycastTarget = false;
            txt.horizontalOverflow = HorizontalWrapMode.Overflow;
            txt.verticalOverflow = VerticalWrapMode.Overflow;
            return txt;
        }

        private void StretchFill(RectTransform rt)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
        }

        #endregion
    }
}
