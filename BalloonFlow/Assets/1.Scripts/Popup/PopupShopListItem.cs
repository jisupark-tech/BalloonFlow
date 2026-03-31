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
