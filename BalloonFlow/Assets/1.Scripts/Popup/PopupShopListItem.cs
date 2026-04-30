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
    public class PopupShopListItem : MonoBehaviour
    {
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
        [SerializeField] private GameObject _imgSale;
        [SerializeField] private GameObject _particleLight;

        [Header("[Special Offer 스프라이트]")]
        [SerializeField] private Sprite _sprFrameSpecial;
        [SerializeField] private Sprite _sprFrameRed;
        [SerializeField] private Sprite _sprBtnFrameRed;

        [Header("[Normal Bundle 스프라이트]")]
        [SerializeField] private Sprite _sprFrameNormal;
        [SerializeField] private Sprite _sprFramePurple;
        [SerializeField] private Sprite _sprBtnFramePurple;

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
                _timeOffRoot.SetActive(data.hasTimeLimit);
                if (data.hasTimeLimit)
                {
                    _remainingTime = data.timeLimitSeconds;
                    _timerActive = true;
                    UpdateTimerText();
                }
            }

            // 할인율
            if (_offPercentRoot != null)
            {
                _offPercentRoot.SetActive(data.hasDiscount && data.discountPercent > 0);
                if (data.hasDiscount)
                    SetTextWithOutline(_txtOffPer, _txtOffPerOutline, $"{data.discountPercent}%");
            }

            // 구매 버튼
            if (_btnBuy != null)
            {
                _btnBuy.onClick.RemoveAllListeners();
                _btnBuy.onClick.AddListener(() => _onBuy?.Invoke(_data));
            }

            // 타입별 프레임/이미지 스왑: hasDiscount=true → Special Offer (Red) / false → Normal Bundle (Purple)
            ApplyProductTypeVisual(data.hasDiscount);

            // 왼쪽 가격 표시 (TextPrice / TextPriceOutline) — 버튼 가격과 동일 텍스트로 일단 동기화
            SetTextWithOutline(_txtPrice, _txtPriceOutline, data.price);

            // 동적 보상 표시 (ItemArea / BoostArea)
            SetupRewards(data.rewards);
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

            ClearArea(_itemArea);
            ClearArea(_boostArea);

            if (rewards == null) return;

            var prefab = GetShopItemPrefab();
            if (prefab == null)
            {
                Debug.LogWarning("[PopupShopListItem] ShopItem prefab 미발견. Resources/UI/UIAssets/ShopItem 확인");
                return;
            }

            // ── ItemArea ──
            if (rewards.coins > 0)
                SpawnRewardItem(prefab, _itemArea, _iconCoin, FormatCoins(rewards.coins));

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
                if (_imgSale != null) _imgSale.SetActive(true);
            }
            else
            {
                if (_imgTop != null && _sprFrameNormal != null) _imgTop.sprite = _sprFrameNormal;
                if (_imgBottom != null && _sprFramePurple != null) _imgBottom.sprite = _sprFramePurple;
                if (_imgBtnBuyFrame != null && _sprBtnFramePurple != null) _imgBtnBuyFrame.sprite = _sprBtnFramePurple;
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
