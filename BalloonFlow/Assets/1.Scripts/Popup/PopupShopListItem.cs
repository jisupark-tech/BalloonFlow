using UnityEngine;
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

        [Header("[시간 한정 할인]")]
        [SerializeField] private GameObject _timeOffRoot;
        [SerializeField] private TMP_Text _txtTimeOff;

        [Header("[할인율]")]
        [SerializeField] private GameObject _offPercentRoot;
        [SerializeField] private TMP_Text _txtOffPer;

        [Header("[구매 버튼]")]
        [SerializeField] private Button _btnBuy;
        [SerializeField] private TMP_Text _txtBtnBuy;

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

        private ShopProductData _data;
        private System.Action<ShopProductData> _onBuy;
        private float _remainingTime;
        private bool _timerActive;

        /// <summary>상품 데이터 세팅.</summary>
        public void Setup(ShopProductData data, System.Action<ShopProductData> onBuy)
        {
            _data = data;
            _onBuy = onBuy;

            // 상품 이미지
            if (_imgProducts != null && data.productImage != null)
                _imgProducts.sprite = data.productImage;

            // 타이틀
            if (_txtTitle != null)
                _txtTitle.text = data.title;

            // 가격
            if (_txtBtnBuy != null)
                _txtBtnBuy.text = data.price;

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
                if (_txtOffPer != null && data.hasDiscount)
                    _txtOffPer.text = $"{data.discountPercent}%";
            }

            // 구매 버튼
            if (_btnBuy != null)
            {
                _btnBuy.onClick.RemoveAllListeners();
                _btnBuy.onClick.AddListener(() => _onBuy?.Invoke(_data));
            }

            // 타입별 프레임/이미지 스왑: hasDiscount=true → Special Offer (Red) / false → Normal Bundle (Purple)
            ApplyProductTypeVisual(data.hasDiscount);
        }

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
            if (_txtTimeOff == null) return;

            int total = Mathf.CeilToInt(_remainingTime);
            int h = total / 3600;
            int m = (total % 3600) / 60;
            int s = total % 60;

            _txtTimeOff.text = h > 0 ? $"{h:D2}:{m:D2}:{s:D2}" : $"{m:D2}:{s:D2}";
        }
    }
}
