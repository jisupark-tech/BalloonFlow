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
    // ImageGoldIcon override 우선순위 표는 ApplyGoldIcon() docstring 참조.
    // OffPercent UI는 coin 카테고리(_coinGoldIconKeys.ContainsKey(productId))에서는 항상 비활성 -- coin 상품군 할인율 표시 정책상 숨김.
    public class PopupShopListItem : MonoBehaviour
    {
        // BoostArea 인스턴스 ImageItem 축소 비율 -- ItemArea 쪽은 적용 금지 (사용자 요구: BoostArea 한정).
        // prefab 자체 m_LocalScale을 건드리면 ItemArea 쪽도 같이 줄어들기 때문에 인스턴스 단위로만 조정.
        private const float BoostIconScaleX = 0.8f;
        private const float BoostIconScaleY = 0.8f;

        // tier1~5 bundle은 데이터의 discountPercent 유무와 무관하게 Normal Bundle 스타일로 고정
        private static readonly HashSet<string> _normalBundleProductIds = new HashSet<string>
        {
            "xyz.aimed.balloonloop.bundle.tier1",
            "xyz.aimed.balloonloop.bundle.tier2",
            "xyz.aimed.balloonloop.bundle.tier3",
            "xyz.aimed.balloonloop.bundle.tier4",
            "xyz.aimed.balloonloop.bundle.tier5",
        };

        // ImageGoldIcon override 우선순위 표는 ApplyGoldIcon() docstring 참조.
        private const string GoldIconStarterOfferProductId = "xyz.aimed.balloonloop.offer.starter";

        // Coin 카테고리 productId -- 매직 스트링 인라인 금지, _coinGoldIconKeys / OffPercent 분기에서 deterministic 키로 사용.
        private const string CoinProductId1000   = "xyz.aimed.balloonloop.coin.1000";
        private const string CoinProductId5000   = "xyz.aimed.balloonloop.coin.5000";
        private const string CoinProductId10000  = "xyz.aimed.balloonloop.coin.10000";
        private const string CoinProductId25000  = "xyz.aimed.balloonloop.coin.25000";
        private const string CoinProductId50000  = "xyz.aimed.balloonloop.coin.50000";
        private const string CoinProductId100000 = "xyz.aimed.balloonloop.coin.100000";

        // ImageGoldIcon override 우선순위 표는 ApplyGoldIcon() docstring 참조.
        // 사용자 스펙 (Product ID → Asset 경로) — 변경 시 user feedback 재확인 필수:
        //   bundle.tier1 → Assets/2.Sprite/UI/gold03.png  (스펙 미명시, 패턴 추정)
        //   bundle.tier2 → Assets/2.Sprite/UI/gold04.png
        //   bundle.tier3 → Assets/2.Sprite/UI/gold05.png
        //   bundle.tier4 → Assets/2.Sprite/UI/gold06.png
        //   bundle.tier5 → Assets/2.Sprite/UI/gold07.png
        // _normalBundleProductIds(프레임/할인 분기용)와 의도적으로 별개 컬렉션 -- 두 override 메커니즘이 독립 변경될 여지 보존.
        private static readonly Dictionary<string, string> _normalBundleGoldIconKeys = new Dictionary<string, string>
        {
            { "xyz.aimed.balloonloop.bundle.tier1", Const.SPR_GOLD03 },
            { "xyz.aimed.balloonloop.bundle.tier2", Const.SPR_GOLD04 },
            { "xyz.aimed.balloonloop.bundle.tier3", Const.SPR_GOLD05 },
            { "xyz.aimed.balloonloop.bundle.tier4", Const.SPR_GOLD06 },
            { "xyz.aimed.balloonloop.bundle.tier5", Const.SPR_GOLD07 },
        };

        // Coin 카테고리 ImageGoldIcon override -- 1000/5000/10000/25000/50000/100000 -> gold01/03/04/05/06/08 sprite key (gold01~07 atlas 패킹, gold08 Resources/UI/Sprites/gold08.png 폴백).
        // ContainsKey 검사로 OffPercent 강제 비활성화 카테고리 식별에도 재사용 (deterministic, prefix 매칭보다 안전).
        private static readonly Dictionary<string, string> _coinGoldIconKeys = new Dictionary<string, string>
        {
            { CoinProductId1000,   Const.SPR_GOLD01 },
            { CoinProductId5000,   Const.SPR_GOLD03 },
            { CoinProductId10000,  Const.SPR_GOLD04 },
            { CoinProductId25000,  Const.SPR_GOLD05 },
            { CoinProductId50000,  Const.SPR_GOLD06 },
            { CoinProductId100000, Const.SPR_GOLD08 },
        };

        private static bool IsNormalBundleProduct(string productId)
        {
            return !string.IsNullOrEmpty(productId) && _normalBundleProductIds.Contains(productId);
        }

        [Header("[상품 정보]")]
        [SerializeField] private Image _imgProducts;
        [SerializeField] private TMP_Text _txtTitle;
        [SerializeField] private TMP_Text _txtTitleOutline;

        [Header("[Title Outline Material Preset]")]
        [SerializeField] private Material _matTitleOutlineNormalBundle;
        [SerializeField] private Material _matTitleOutlineSpecialBundle;

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
        private Color _defaultPriceOutlineColor;
        private bool _priceOutlineColorCaptured;
        private static readonly Color Coin1000PriceOutlineColor = new Color32(0x6A, 0x4A, 0x30, 0xFF);

        // ShopListItem.prefab 이 바이너리 직렬화라 TxtTitleOutline Material Preset 을 텍스트 편집으로 교체 불가.
        // Resources.Load 캐시 — PopupWinningStreak.EnsureStreakSprites 의 _fontMatGreenOutline/_fontMatPurpleOutline 패턴 미러.
        private Material _resTitleOutlineNormalBundle;
        private Material _resTitleOutlineSpecialBundle;

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
            SetTextWithOutline(_txtTitle, _txtTitleOutline, ResolveLocalizedTitle(data));

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

            // 할인율 -- coin 카테고리는 정책상 OffPercent UI 항상 비활성 (productId 기준, prefix 매칭보다 deterministic)
            bool isCoinProduct = !string.IsNullOrEmpty(data?.productId) && _coinGoldIconKeys.ContainsKey(data.productId);
            if (_offPercentRoot != null)
            {
                _offPercentRoot.SetActive(!isCoinProduct && !forceNormalBundle && data.hasDiscount && data.discountPercent > 0);
                if (!isCoinProduct && !forceNormalBundle && data.hasDiscount)
                    SetTextWithOutline(_txtOffPer, _txtOffPerOutline,
                        LocalizationService.GetWith("shoplistitem.textsale", "n", data.discountPercent));
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
            ApplyCoin1000PriceOutlineColor(data);

            // 좌측 ImageGoldIcon — Firestore goldIconKey 명시 시 atlas 교체, 미지정 시 Const.SPR_ICONGOLD 기본.
            ApplyGoldIcon(data);

            // 동적 보상 표시 (ItemArea / BoostArea)
            SetupRewards(data.rewards);
        }

        /// <summary>
        /// 좌측 가격 영역 ImageGoldIcon sprite 결정.
        /// data.goldIconKey 비어있으면 Const.SPR_ICONGOLD 기본. atlas miss 시 기존 sprite 유지
        /// (ResolveProductSprite 의 fallback 패턴과 동일).
        /// tier1~5 / coin 분기는 atlas miss 시 경고 로그 -- 패킹 누락 추적용.
        ///
        /// override 우선순위 (클래스 헤더 / _normalBundleGoldIconKeys / _coinGoldIconKeys 선언부와 동일 문구로 동기화):
        ///   1) starter offer override -> Const.SPR_GOLD01
        ///   2) bundle tier1~5 override -> Const.SPR_GOLD03~SPR_GOLD07
        ///   3) coin productId override -> _coinGoldIconKeys (gold01/03/04/05/06/08)
        ///   4) data.goldIconKey
        ///   5) Const.SPR_ICONGOLD fallback
        /// </summary>
        private void ApplyGoldIcon(ShopProductData data)
        {
            if (_imageGoldIcon == null) return;
            if (!ResourceManager.HasInstance) return;
            var rm = ResourceManager.Instance;
            string productId = data?.productId;

            // 1) starter offer override
            if (!string.IsNullOrEmpty(productId) && productId == GoldIconStarterOfferProductId)
            {
                var starterSprite = rm.GetUISprite(Const.SPR_GOLD01);
                if (starterSprite != null) _imageGoldIcon.sprite = starterSprite;
                else Debug.LogWarning($"[PopupShopListItem] starter offer '{productId}' gold icon key '{Const.SPR_GOLD01}' atlas miss -- UI.spriteatlas 의 packables 에 Assets/2.Sprite/UI/{Const.SPR_GOLD01}.png 가 포함됐는지 확인");
                return;
            }

            // 2) bundle tier1~5 override
            if (!string.IsNullOrEmpty(productId) && _normalBundleGoldIconKeys.TryGetValue(productId, out var tierKey))
            {
                var tierSprite = rm.GetUISprite(tierKey);
                if (tierSprite != null) _imageGoldIcon.sprite = tierSprite;
                else Debug.LogWarning($"[PopupShopListItem] tier bundle '{productId}' gold icon key '{tierKey}' atlas miss -- UI.spriteatlas 의 packables 에 Assets/2.Sprite/UI/{tierKey}.png 가 포함됐는지 확인");
                return;
            }

            // 3) coin productId override
            if (!string.IsNullOrEmpty(productId) && _coinGoldIconKeys.TryGetValue(productId, out var coinKey))
            {
                var coinSprite = rm.GetUISprite(coinKey);
                // atlas 미포함 sprite (예: gold08) Resources fallback — Resources/UI/Sprites/{key}.png 사본 필요
                if (coinSprite == null) coinSprite = Resources.Load<Sprite>("UI/Sprites/" + coinKey);
                if (coinSprite != null) _imageGoldIcon.sprite = coinSprite;
                else Debug.LogWarning($"[PopupShopListItem] coin '{productId}' gold icon key '{coinKey}' atlas miss -- UI.spriteatlas 의 packables 에 Assets/2.Sprite/UI/{coinKey}.png 가 포함됐는지 확인");
                return;
            }

            // 4) data.goldIconKey / 5) Const.SPR_ICONGOLD fallback
            string key = string.IsNullOrEmpty(data?.goldIconKey) ? Const.SPR_ICONGOLD : data.goldIconKey;
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
            SetRewardAreaVisible(_itemArea, false);
            SetRewardAreaVisible(_boostArea, false);

            var prefab = GetShopItemPrefab();
            if (prefab == null)
            {
                Debug.LogWarning("[PopupShopListItem] ShopItem prefab 미발견. Resources/UI/UIAssets/ShopItem 확인");
                return;
            }

            // ItemArea 선두 고정 노출 (rewards 무관, 기존 ShopItem보다 앞)
            if (UseLegacyAlwaysOnNoAdsRewardIcon() && _iconNoAdsBig != null)
                SpawnIconOnlyItem(prefab, _itemArea, _iconNoAdsBig);
            else if (UseLegacyAlwaysOnNoAdsRewardIcon())
                Debug.LogWarning("[PopupShopListItem] _iconNoAdsBig 미할당 — Inspector에서 noAdsBig 스프라이트 와이어 필요");

            if (rewards == null) return;

            bool hasItemRewards = false;
            bool hasBoostRewards = false;

            // ── ItemArea ──
            // 사용자 요구: RightArea 의 coin reward 표시 제거 — LeftArea 의 _txtPrice (TextPrice/Outline)
            // 가 이미 골드 표시. 두 곳에 골드 아이콘 보이면 시각적 중복.
            // if (rewards.coins > 0)
            //     SpawnRewardItem(prefab, _itemArea, _iconCoin, FormatCoins(rewards.coins));

            if (rewards.infiniteHeartsSeconds > 0)
            {
                SpawnRewardItem(prefab, _itemArea, _iconInfiniteHearts, FormatHours(rewards.infiniteHeartsSeconds));
                hasItemRewards = true;
            }

            if (UseLegacyAlwaysOnNoAdsRewardIcon() && rewards.removeAds)
                SpawnRewardItem(prefab, _itemArea, _iconRemoveAds, ""); // 카운트 비움 — 아이콘만

            // ── BoostArea ──
            // BoostArea의 ImageItem만 축소 (사용자 요구). prefab 공유로 인해 ItemArea 쪽엔 적용 금지.
            if (rewards.removeAds)
            {
                // ROLLBACK_SHOP_REWARD_AREA_VISIBILITY_20260622:
                // Show ad-remove reward only when Firestore rewards.removeAds is true.
                SpawnIconOnlyItem(prefab, _itemArea, _iconNoAdsBig != null ? _iconNoAdsBig : _iconRemoveAds);
                hasItemRewards = true;
            }

            if (rewards.boosters != null)
            {
                if (rewards.boosters.hand > 0)
                {
                    var view = SpawnRewardItem(prefab, _boostArea, _iconHand, $"x{rewards.boosters.hand}");
                    view?.ApplyIconScale(BoostIconScaleX, BoostIconScaleY);
                    hasBoostRewards = true;
                }
                if (rewards.boosters.shuffle > 0)
                {
                    var view = SpawnRewardItem(prefab, _boostArea, _iconShuffle, $"x{rewards.boosters.shuffle}");
                    view?.ApplyIconScale(BoostIconScaleX, BoostIconScaleY);
                    hasBoostRewards = true;
                }
                if (rewards.boosters.zap > 0)
                {
                    var view = SpawnRewardItem(prefab, _boostArea, _iconZap, $"x{rewards.boosters.zap}");
                    view?.ApplyIconScale(BoostIconScaleX, BoostIconScaleY);
                    hasBoostRewards = true;
                }
            }

            SetRewardAreaVisible(_itemArea, hasItemRewards);
            SetRewardAreaVisible(_boostArea, hasBoostRewards);
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

        private ShopItemView SpawnRewardItem(GameObject prefab, RectTransform area, Sprite icon, string countText)
        {
            if (area == null) return null;

            var go = Instantiate(prefab, area);
            go.SetActive(true);

            // ShopItemView 자동 attach (prefab 에 미리 붙어있지 않으면)
            var view = go.GetComponent<ShopItemView>();
            if (view == null) view = go.AddComponent<ShopItemView>();
            view.Setup(icon, countText);
            return view;
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
        private static void SetRewardAreaVisible(RectTransform area, bool visible)
        {
            if (area != null && area.gameObject.activeSelf != visible)
                area.gameObject.SetActive(visible);
        }

        private static bool UseLegacyAlwaysOnNoAdsRewardIcon()
        {
            // ROLLBACK_SHOP_REWARD_AREA_VISIBILITY_20260622:
            // Return true to restore the previous behavior where noAdsBig was shown regardless
            // of Firestore rewards.removeAds. Current spec: show only when removeAds is true.
            return false;
        }

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

        private void CapturePriceOutlineColorIfNeeded()
        {
            if (_priceOutlineColorCaptured) return;
            _priceOutlineColorCaptured = true;

            _defaultPriceOutlineColor = _txtPriceOutline != null ? _txtPriceOutline.color : Color.white;
        }

        private void ApplyCoin1000PriceOutlineColor(ShopProductData data)
        {
            // ROLLBACK_BROWN_1000_COIN_PRICE_OUTLINE_20260707:
            // ShopListGold.prefab is shared by every coin product. Keep the prefab TMP
            // material/outline width intact and recolor only the 1000 coin outline text.
            CapturePriceOutlineColorIfNeeded();

            bool isCoin1000 = data != null && data.productId == CoinProductId1000;
            UIOutlineStyle.ApplyColor(_txtPriceOutline, isCoin1000 ? Coin1000PriceOutlineColor : _defaultPriceOutlineColor);
        }

        private static string FormatHours(int seconds)
        {
            if (seconds <= 0) return "";
            float hours = seconds / 3600f;
            if (hours >= 1f) return $"{Mathf.RoundToInt(hours)}h";
            int minutes = Mathf.RoundToInt(seconds / 60f);
            return $"{minutes}m";
        }

        #endregion

        private void EnsureTitleOutlineMaterials()
        {
            if (_resTitleOutlineNormalBundle == null)  _resTitleOutlineNormalBundle  = Resources.Load<Material>(Const.FONT_MAT_POPPINS_BOLD_PURPLE_OUTLINE);
            if (_resTitleOutlineSpecialBundle == null) _resTitleOutlineSpecialBundle = Resources.Load<Material>(Const.FONT_MAT_POPPINS_BOLD_RED_OUTLINE);
        }

        // [SHOP_TITLE_OUTLINE_20260615] Starter=Red / Pop·Super·Mega·Giant·Ultimate=Purple. 프리팹이 바이너리 직렬화이므로 런타임 Resources.Load 로 강제. SerializeField 슬롯은 폴백.
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

            EnsureTitleOutlineMaterials();
            Material titleOutlineMat = isSpecial
                ? (_resTitleOutlineSpecialBundle ?? _matTitleOutlineSpecialBundle)
                : (_resTitleOutlineNormalBundle  ?? _matTitleOutlineNormalBundle);
            // ROLLBACK_OUTLINE_LANG_FONT_20260714: 언어 인지(KO=Chiron 동일 색 프리셋 + 폰트) + fill 폰트 동기화(이격 방지).
            UIOutlineStyle.ApplyLanguageAwareOutline(_txtTitleOutline, titleOutlineMat, UIOutlineStyle.ForShopBundle(isSpecial));
            if (_txtTitle != null && _txtTitleOutline != null && _txtTitleOutline.font != null && _txtTitle.font != _txtTitleOutline.font)
                _txtTitle.font = _txtTitleOutline.font;
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

        private static string ResolveLocalizedText(string value)
        {
            if (string.IsNullOrEmpty(value)) return string.Empty;
            return LocalizationService.Has(value) ? LocalizationService.Get(value) : value;
        }

        private static string ResolveLocalizedTitle(ShopProductData data)
        {
            if (data == null) return string.Empty;

            if (data.category == ShopItemCategory.Gold && data.rewards != null && data.rewards.coins > 0)
                return FormatCoins(data.rewards.coins);

            string title = data.title;
            if (!string.IsNullOrEmpty(title) && LocalizationService.Has(title))
                return LocalizationService.Get(title);

            string productKey = UIShop.ProductTitleKeyFromId(data.productId);
            if (!string.IsNullOrEmpty(productKey) && LocalizationService.Has(productKey))
                return LocalizationService.Get(productKey);

            return ResolveLocalizedText(title);
        }

        // RectTransform.GetWorldCorners 결과 재사용용 — 매 프레임 호출 시 GC alloc 방지.
        private static readonly Vector3[] _vpCorners = new Vector3[4];
        private static readonly Vector3[] _pCorners  = new Vector3[4];

        /// <summary>
        /// _particleLight GameObject를 viewport 영역에 들어왔는지 여부에 따라 SetActive로 컬링.
        /// 이유: ScrollRect의 RectMask2D는 graphic만 클리핑하고 ParticleSystem CPU는 계속 돌므로
        /// 화면 밖 카드의 particle을 명시적으로 OFF 시켜야 한다.
        /// gold/ad 카드처럼 _particleLight 미할당이면 즉시 return — NRE 방지.
        /// </summary>
        public void RefreshParticleLightVisibility(RectTransform viewport)
        {
            if (_particleLight == null) return;

            if (viewport == null)
            {
                if (!_particleLight.activeSelf) _particleLight.SetActive(true);
                return;
            }

            var particleRT = _particleLight.transform as RectTransform;
            if (particleRT == null)
            {
                if (!_particleLight.activeSelf) _particleLight.SetActive(true);
                return;
            }

            viewport.GetWorldCorners(_vpCorners);
            particleRT.GetWorldCorners(_pCorners);

            // GetWorldCorners 순서: [0]BL, [1]TL, [2]TR, [3]BR.
            Rect vpRect = new Rect(
                _vpCorners[0].x, _vpCorners[0].y,
                _vpCorners[2].x - _vpCorners[0].x,
                _vpCorners[2].y - _vpCorners[0].y);
            Rect pRect = new Rect(
                _pCorners[0].x, _pCorners[0].y,
                _pCorners[2].x - _pCorners[0].x,
                _pCorners[2].y - _pCorners[0].y);

            bool overlaps = vpRect.Overlaps(pRect);
            if (_particleLight.activeSelf != overlaps)
                _particleLight.SetActive(overlaps);
        }
    }
}
