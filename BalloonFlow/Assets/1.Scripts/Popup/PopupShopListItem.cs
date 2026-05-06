using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Serialization;
using UnityEngine.UI;
using TMPro;

namespace BalloonFlow
{
    /// <summary>
    /// 상점 상품 리스트 아이템.
    /// Inspector에서 UI 링크 연결.
    /// </summary>
    // BtnBuyGreen 본체/프레임 sprite는 런타임에 ApplyProductTypeVisual()에서 isSpecial 분기에 따라 _imgBtnBuy(_sprBtnGreen/_sprBtnGreenSpecial)·_imgBtnBuyFrame(_sprBtnFramePurple/_sprBtnFrameRed)으로 swap 됨.
    // ImageGoldIcon은 productId == "xyz.aimed.balloonloop.offer.starter" 시 Const.SPR_GOLD01("gold01")로 override 되며, bundle.tier1~tier5는 각각 Const.SPR_GOLD03~SPR_GOLD07("gold03"~"gold07")로 override 됨.
    public class PopupShopListItem : MonoBehaviour
    {
        // tier1~5 bundle은 데이터의 discountPercent 유무와 무관하게 Normal Bundle 스타일로 고정
        private static readonly HashSet<string> _normalBundleProductIds = new HashSet<string>
        {
            "xyz.aimed.balloonloop.bundle.tier1",
            "xyz.aimed.balloonloop.bundle.tier2",
            "xyz.aimed.balloonloop.bundle.tier3",
            "xyz.aimed.balloonloop.bundle.tier4",
            "xyz.aimed.balloonloop.bundle.tier5",
        };

        // ImageGoldIcon override 우선순위: (a) 이 productId(starter offer) → SPR_GOLD01,
        // (b) _normalBundleGoldIconKeys 매핑(tier1~5) → SPR_GOLD03~SPR_GOLD07, (c) data.goldIconKey, (d) SPR_ICONGOLD fallback.
        private const string GoldIconStarterOfferProductId = "xyz.aimed.balloonloop.offer.starter";

        // tier1~5 bundle은 ImageGoldIcon을 tier별 gold03~gold07 atlas key로 강제 — data.goldIconKey 우선순위보다 위.
        // _normalBundleProductIds(프레임/할인 분기용)와 의도적으로 별개 컬렉션 — 두 override 메커니즘이 독립 변경될 여지 보존.
        private static readonly Dictionary<string, string> _normalBundleGoldIconKeys = new Dictionary<string, string>
        {
            { "xyz.aimed.balloonloop.bundle.tier1", Const.SPR_GOLD03 },
            { "xyz.aimed.balloonloop.bundle.tier2", Const.SPR_GOLD04 },
            { "xyz.aimed.balloonloop.bundle.tier3", Const.SPR_GOLD05 },
            { "xyz.aimed.balloonloop.bundle.tier4", Const.SPR_GOLD06 },
            { "xyz.aimed.balloonloop.bundle.tier5", Const.SPR_GOLD07 },
        };

        private static bool IsNormalBundleProduct(string productId)
        {
            return !string.IsNullOrEmpty(productId) && _normalBundleProductIds.Contains(productId);
        }

        [Header("[상품 정보]")]
        [SerializeField] private Image _imgProducts;
        [SerializeField] private TMP_Text _txtTitle;
        [SerializeField] private TMP_Text _txtTitleOutline;

        [Header("[시간 한정 할인]")]
        [SerializeField] private GameObject _timeOffRoot;
        [SerializeField] private TMP_Text _txtTimeOff;
        [SerializeField] private TMP_Text _txtTimeOffOutline;

        [Header("[할인율]")]
        [SerializeField] private GameObject _offPercentRoot;
        [SerializeField] private TMP_Text _txtOffPer;
        [SerializeField] private TMP_Text _txtOffPerOutline;

        [Header("[구매 버튼]")]
        [SerializeField] private Button _btnBuy;
        [SerializeField] private TMP_Text _txtBtnBuy;
        [SerializeField] private TMP_Text _txtBtnBuyOutline;

        [Header("[타입별 프레임 — 상단/하단]")]
        [SerializeField] private Image _imgTop;
        [SerializeField] private Image _imgBottom;
        [SerializeField] private Image _imgBtnBuyFrame;
        [Tooltip("BtnBuyGreen 자식 노드의 Image — 버튼 본체 그린 BG")]
        [SerializeField] private Image _imgBtnBuy;
        [SerializeField] private GameObject _imgSale;
        [SerializeField] private GameObject _particleLight;

        [Header("[Special Offer 스프라이트]")]
        [SerializeField] private Sprite _sprFrameSpecial;
        [SerializeField] private Sprite _sprFrameRed;
        [SerializeField] private Sprite _sprBtnFrameRed;
        [Tooltip("Special Offer 버튼 본체 BG sprite — Normal Bundle의 _sprBtnGreen와 분리 관리")]
        [SerializeField] private Sprite _sprBtnGreenSpecial;

        [Header("[Normal Bundle 스프라이트]")]
        [SerializeField] private Sprite _sprFrameNormal;
        [SerializeField] private Sprite _sprFramePurple;
        [SerializeField] private Sprite _sprBtnFramePurple;
        [Tooltip("Assets/2.Sprite/UI/shopBtnGreen.png — Normal Bundle용 그린 버튼 BG")]
        [SerializeField] private Sprite _sprBtnGreen;

        [Header("[보상 표시 — 동적 생성]")]
        [Tooltip("ShopItem.prefab. 미할당 시 Resources/UI/UIAssets/ShopItem 자동 로드")]
        [SerializeField] private GameObject _shopItemPrefab;
        [Tooltip("ItemArea (코인/무한하트/광고제거). 미할당 시 transform.Find('ItemArea') 자동 검색")]
        [SerializeField] private RectTransform _itemArea;
        [Tooltip("BoostArea (부스터 3종). 미할당 시 transform.Find('BoostArea') 자동 검색")]
        [SerializeField] private RectTransform _boostArea;

        [Header("[가격 (왼쪽 골드 영역) — TextPrice / TextPriceOutline]")]
        [SerializeField] private TMP_Text _txtPrice;
        [SerializeField] private TMP_Text _txtPriceOutline;

        [Header("[보상 아이콘 — Inspector fallback. Awake 시 Addressable atlas 에서 override]")]
        [SerializeField] private Sprite _iconCoin;
        [SerializeField] private Sprite _iconInfiniteHearts;
        [SerializeField] private Sprite _iconRemoveAds;
        [FormerlySerializedAs("_iconSelectTool")]
        [SerializeField] private Sprite _iconHand;
        [SerializeField] private Sprite _iconShuffle;
        [FormerlySerializedAs("_iconColorRemove")]
        [SerializeField] private Sprite _iconZap;

        [Tooltip("Assets/2.Sprite/UI/noAdsBig 할당 — ItemArea 선두 고정 노출용")]
        [SerializeField] private Sprite _iconNoAdsBig;

        // 좌측 TextPrice 옆 골드 아이콘 (ImageGoldIcon). Awake 1회 캐시.
        private Image _imageGoldIcon;

        private void Awake()
        {
            if (ResourceManager.HasInstance)
            {
                var rm = ResourceManager.Instance;
                _iconCoin           = rm.UISpriteOr(Const.SPR_ICONGOLD,           _iconCoin);
                _iconInfiniteHearts = rm.UISpriteOr(Const.SPR_ICONHEARINFINITE,   _iconInfiniteHearts);
                _iconRemoveAds      = rm.UISpriteOr(Const.SPR_ICONAD,             _iconRemoveAds);
                _iconHand           = rm.UISpriteOr(Const.SPR_ICONHAND,           _iconHand);
                _iconShuffle        = rm.UISpriteOr(Const.SPR_ICONSUFFLE,         _iconShuffle);
                _iconZap            = rm.UISpriteOr(Const.SPR_ICONZAP,            _iconZap);
            }

            // ImageGoldIcon — 직계 자식 우선, 없으면 deep 탐색 fallback (prefab 구조에 따라 위치 다름)
            var direct = transform.Find("ImageGoldIcon");
            _imageGoldIcon = direct != null ? direct.GetComponent<Image>() : null;
            if (_imageGoldIcon == null)
                _imageGoldIcon = GetComponentsInChildren<Image>(true).FirstOrDefault(i => i.name == "ImageGoldIcon");
        }

        private ShopProductData _data;
        private System.Action<ShopProductData> _onBuy;
        private float _remainingTime;
        private bool _timerActive;

        /// <summary>상품 데이터 세팅.</summary>
        public void Setup(ShopProductData data, System.Action<ShopProductData> onBuy)
        {
            _data = data;
            _onBuy = onBuy;

            bool forceNormalBundle = IsNormalBundleProduct(data?.productId);

            // 상품 이미지: Firestore imageKey → atlas sprite (sync, atlas 가 Title 에서 사전 로드됨).
            // 키 미지정/atlas 미준비 시 Inspector 의 productImage 또는 prefab 기본값 유지.
            if (_imgProducts != null)
            {
                Sprite resolved = ResolveProductSprite(data);
                if (resolved != null) _imgProducts.sprite = resolved;
            }

            // 타이틀
            SetTextWithOutline(_txtTitle, _txtTitleOutline, data.title);

            // 가격
            SetTextWithOutline(_txtBtnBuy, _txtBtnBuyOutline, data.price);

            // 시간 한정
            if (_timeOffRoot != null)
            {
                bool showTimeOff = !forceNormalBundle && data.hasTimeLimit;
                _timeOffRoot.SetActive(showTimeOff);
                if (forceNormalBundle)
                {
                    _timerActive = false;
                }
                else if (data.hasTimeLimit)
                {
                    _remainingTime = data.timeLimitSeconds;
                    _timerActive = true;
                    UpdateTimerText();
                }
            }

            // 할인율
            if (_offPercentRoot != null)
            {
                _offPercentRoot.SetActive(!forceNormalBundle && data.hasDiscount && data.discountPercent > 0);
                if (!forceNormalBundle && data.hasDiscount)
                    SetTextWithOutline(_txtOffPer, _txtOffPerOutline, $"{data.discountPercent}%");
            }

            // 구매 버튼
            if (_btnBuy != null)
            {
                _btnBuy.onClick.RemoveAllListeners();
                _btnBuy.onClick.AddListener(() => _onBuy?.Invoke(_data));
            }

            // 타입별 프레임/이미지 스왑: hasDiscount=true → Special Offer (Red) / false → Normal Bundle (Purple)
            ApplyProductTypeVisual(!forceNormalBundle && data.hasDiscount);

            // 왼쪽 골드 표시 (TextPrice / TextPriceOutline) — 사용자 요구: 받을 골드 양 (rewards.coins).
            // 구매 가격은 우측 BtnBuy 의 _txtBtnBuy 가 표시. 여기서는 reward.coins 만.
            // coins 0 인 상품 (광고제거 등) 은 빈 텍스트 — TextPrice 자체 GameObject 는 그대로 노출 (디자인 결정 시 SetActive 조정).
            string coinsText = (data.rewards != null && data.rewards.coins > 0)
                ? FormatCoins(data.rewards.coins)
                : string.Empty;
            SetTextWithOutline(_txtPrice, _txtPriceOutline, coinsText);

            // 좌측 ImageGoldIcon — Firestore goldIconKey 명시 시 atlas 교체, 미지정 시 Const.SPR_ICONGOLD 기본.
            ApplyGoldIcon(data);

            // 동적 보상 표시 (ItemArea / BoostArea)
            SetupRewards(data.rewards);
        }

        /// <summary>
        /// 좌측 가격 영역 ImageGoldIcon sprite 결정.
        /// data.goldIconKey 비어있으면 Const.SPR_ICONGOLD 기본. atlas miss 시 기존 sprite 유지
        /// (ResolveProductSprite 의 fallback 패턴과 동일).
        /// </summary>
        private void ApplyGoldIcon(ShopProductData data)
        {
            if (_imageGoldIcon == null) return;
            if (!ResourceManager.HasInstance) return;
            var rm = ResourceManager.Instance;
            // 우선순위: (a) starter offer → SPR_GOLD01, (b) tier1~5 → SPR_GOLD03~07,
            //           (c) data.goldIconKey, (d) SPR_ICONGOLD fallback.
            string productId = data?.productId;
            string key;
            if (!string.IsNullOrEmpty(productId) && productId == GoldIconStarterOfferProductId)
                key = Const.SPR_GOLD01;
            else if (!string.IsNullOrEmpty(productId) && _normalBundleGoldIconKeys.TryGetValue(productId, out var tierKey))
                key = tierKey;
            else
                key = string.IsNullOrEmpty(data?.goldIconKey) ? Const.SPR_ICONGOLD : data.goldIconKey;
            var sprite = rm.UISpriteOr(key, _imageGoldIcon.sprite);
            if (sprite != null) _imageGoldIcon.sprite = sprite;
        }

        /// <summary>
        /// 상품 카드 상단 큰 이미지 sprite 결정 우선순위:
        ///   1) data.imageKey (Firestore 명시) → atlas
        ///   2) data.category 별 기본 sprite (코인=iconGold, 광고제거=iconAd, 그 외=iconGold)
        ///   3) Inspector 의 data.productImage (임시 데이터용)
        /// atlas 미로드 시에는 모두 null 가능 — 호출자 측에서 prefab 기본값 유지.
        /// </summary>
        private Sprite ResolveProductSprite(ShopProductData data)
        {
            if (data == null) return null;
            var rm = ResourceManager.HasInstance ? ResourceManager.Instance : null;

            // 1) imageKey 명시
            if (rm != null && !string.IsNullOrEmpty(data.imageKey))
            {
                var s = rm.GetUISprite(data.imageKey);
                if (s != null) return s;
            }

            // 2) 카테고리 fallback
            if (rm != null)
            {
                string fallbackKey = data.category switch
                {
                    ShopItemCategory.Gold => Const.SPR_ICONGOLD,
                    ShopItemCategory.Ad   => Const.SPR_ICONAD,
                    _                     => Const.SPR_ICONGOLD,
                };
                var s = rm.GetUISprite(fallbackKey);
                if (s != null) return s;
            }

            // 3) Inspector 임시 데이터
            return data.productImage;
        }

        #region Reward area dynamic build

        /// <summary>
        /// rewards 항목을 ItemArea / BoostArea 에 동적 생성.
        /// 분담:
        ///   ItemArea  — coins, infiniteHeartsSeconds, removeAds
        ///   BoostArea — hand, shuffle, zap
        /// </summary>
        private void SetupRewards(ShopRewards rewards)
        {
            EnsureRewardAreas();
            ApplyAreaPositions();

            ClearArea(_itemArea);
            ClearArea(_boostArea);

            var prefab = GetShopItemPrefab();
            if (prefab == null)
            {
                Debug.LogWarning("[PopupShopListItem] ShopItem prefab 미발견. Resources/UI/UIAssets/ShopItem 확인");
                return;
            }

            // ItemArea 선두 고정 노출 (rewards 무관, 기존 ShopItem보다 앞)
            if (_iconNoAdsBig != null)
                SpawnIconOnlyItem(prefab, _itemArea, _iconNoAdsBig);
            else
                Debug.LogWarning("[PopupShopListItem] _iconNoAdsBig 미할당 — Inspector에서 noAdsBig 스프라이트 와이어 필요");

            if (rewards == null) return;

            // ── ItemArea ──
            // 사용자 요구: RightArea 의 coin reward 표시 제거 — LeftArea 의 _txtPrice (TextPrice/Outline)
            // 가 이미 골드 표시. 두 곳에 골드 아이콘 보이면 시각적 중복.
            // if (rewards.coins > 0)
            //     SpawnRewardItem(prefab, _itemArea, _iconCoin, FormatCoins(rewards.coins));

            if (rewards.infiniteHeartsSeconds > 0)
                SpawnRewardItem(prefab, _itemArea, _iconInfiniteHearts, FormatHours(rewards.infiniteHeartsSeconds));

            if (rewards.removeAds)
                SpawnRewardItem(prefab, _itemArea, _iconRemoveAds, ""); // 카운트 비움 — 아이콘만

            // ── BoostArea ──
            if (rewards.boosters != null)
            {
                if (rewards.boosters.hand > 0)
                    SpawnRewardItem(prefab, _boostArea, _iconHand, $"x{rewards.boosters.hand}");
                if (rewards.boosters.shuffle > 0)
                    SpawnRewardItem(prefab, _boostArea, _iconShuffle, $"x{rewards.boosters.shuffle}");
                if (rewards.boosters.zap > 0)
                    SpawnRewardItem(prefab, _boostArea, _iconZap, $"x{rewards.boosters.zap}");
            }
        }

        // 프리팹이 binary 직렬화라 Inspector 좌표가 외부에서 바뀔 수 있음 → 매번 강제 보장
        private void ApplyAreaPositions()
        {
            if (_itemArea != null)
            {
                var p = _itemArea.anchoredPosition;
                p.y = 70f;
                _itemArea.anchoredPosition = p;
            }
            if (_boostArea != null)
            {
                var p = _boostArea.anchoredPosition;
                p.y = -70f;
                _boostArea.anchoredPosition = p;
            }
        }

        private void SpawnIconOnlyItem(GameObject prefab, RectTransform area, Sprite icon)
        {
            if (area == null) return;

            var go = Instantiate(prefab, area);
            go.SetActive(true);

            var view = go.GetComponent<ShopItemView>();
            if (view == null) view = go.AddComponent<ShopItemView>();
            view.SetupIconOnly(icon);
        }

        private void SpawnRewardItem(GameObject prefab, RectTransform area, Sprite icon, string countText)
        {
            if (area == null) return;

            var go = Instantiate(prefab, area);
            go.SetActive(true);

            // ShopItemView 자동 attach (prefab 에 미리 붙어있지 않으면)
            var view = go.GetComponent<ShopItemView>();
            if (view == null) view = go.AddComponent<ShopItemView>();
            view.Setup(icon, countText);
        }

        private static void ClearArea(RectTransform area)
        {
            if (area == null) return;
            for (int i = area.childCount - 1; i >= 0; i--)
            {
                var child = area.GetChild(i);
                if (child != null) Destroy(child.gameObject);
            }
        }

        /// <summary>ShopItem prefab 미할당 시 Resources fallback.</summary>
        private GameObject GetShopItemPrefab()
        {
            if (_shopItemPrefab != null) return _shopItemPrefab;
            _shopItemPrefab = Resources.Load<GameObject>("UI/UIAssets/ShopItem");
            return _shopItemPrefab;
        }

        /// <summary>_itemArea / _boostArea 미할당 시 자식 GameObject 이름 기반 자동 검색.</summary>
        private void EnsureRewardAreas()
        {
            if (_itemArea == null)
                _itemArea = FindChildByName("ItemArea");
            if (_boostArea == null)
                _boostArea = FindChildByName("BoostArea");
        }

        private RectTransform FindChildByName(string name)
        {
            var rt = GetComponentsInChildren<RectTransform>(true)
                .FirstOrDefault(t => t.name == name && t != transform);
            return rt;
        }

        private static string FormatCoins(int coins) => coins.ToString("N0");

        private static string FormatHours(int seconds)
        {
            if (seconds <= 0) return "";
            float hours = seconds / 3600f;
            if (hours >= 1f) return $"{Mathf.RoundToInt(hours)}h";
            int minutes = Mathf.RoundToInt(seconds / 60f);
            return $"{minutes}m";
        }

        #endregion

        private void ApplyProductTypeVisual(bool isSpecial)
        {
            if (isSpecial)
            {
                if (_imgTop != null && _sprFrameSpecial != null) _imgTop.sprite = _sprFrameSpecial;
                if (_imgBottom != null && _sprFrameRed != null) _imgBottom.sprite = _sprFrameRed;
                if (_imgBtnBuyFrame != null && _sprBtnFrameRed != null) _imgBtnBuyFrame.sprite = _sprBtnFrameRed;
                if (_imgBtnBuy != null && _sprBtnGreenSpecial != null) _imgBtnBuy.sprite = _sprBtnGreenSpecial;
                if (_imgSale != null) _imgSale.SetActive(true);
            }
            else
            {
                if (_imgTop != null && _sprFrameNormal != null) _imgTop.sprite = _sprFrameNormal;
                if (_imgBottom != null && _sprFramePurple != null) _imgBottom.sprite = _sprFramePurple;
                if (_imgBtnBuyFrame != null && _sprBtnFramePurple != null) _imgBtnBuyFrame.sprite = _sprBtnFramePurple;
                if (_imgBtnBuy != null && _sprBtnGreen != null) _imgBtnBuy.sprite = _sprBtnGreen;
                if (_imgSale != null) _imgSale.SetActive(false);
            }
        }

        private void Update()
        {
            if (!_timerActive) return;

            _remainingTime -= Time.deltaTime;
            if (_remainingTime <= 0f)
            {
                _remainingTime = 0f;
                _timerActive = false;
                if (_timeOffRoot != null)
                    _timeOffRoot.SetActive(false);
            }

            UpdateTimerText();
        }

        private void UpdateTimerText()
        {
            if (_txtTimeOff == null && _txtTimeOffOutline == null) return;

            int total = Mathf.CeilToInt(_remainingTime);
            int h = total / 3600;
            int m = (total % 3600) / 60;
            int s = total % 60;

            string txt = h > 0 ? $"{h:D2}:{m:D2}:{s:D2}" : $"{m:D2}:{s:D2}";
            SetTextWithOutline(_txtTimeOff, _txtTimeOffOutline, txt);
        }

        /// <summary>본문 + outline TMP_Text 둘 다 동일 문자열로 갱신.</summary>
        private static void SetTextWithOutline(TMP_Text main, TMP_Text outline, string value)
        {
            if (main != null) main.text = value;
            if (outline != null) outline.text = value;
        }
    }
}
