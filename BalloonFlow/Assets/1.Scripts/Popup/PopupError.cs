using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace BalloonFlow
{
    /// <summary>
    /// 에러/경고 팝업.
    /// PopupCommonFrame: Single 버튼 (OK).
    /// 아이콘 + 설명 텍스트 표시.
    /// 결제 실패: iconCancel, 인터넷 연결X: iconWifi.
    /// </summary>
    public class PopupError : UIBase
    {
        [Header("[Common Frame]")]
        [SerializeField] private PopupCommonFrame _frame;

        [Header("[Content]")]
        [SerializeField] private TMP_Text _txtDescription;
        [SerializeField] private Image _imgIcon;
        [SerializeField] private Image _imgInnerFrame;

        [Header("[Preset Icons — Inspector에서 할당]")]
        [SerializeField] private Sprite _sprIconCancel;
        [SerializeField] private Sprite _sprIconWifi;
        [SerializeField] private Sprite _sprIconCheck;

        protected override void Awake()
        {
            base.Awake();
            if (_frame != null)
            {
                if (_frame.BtnSingle != null) _frame.BtnSingle.onClick.AddListener(() => CloseUI());
                if (_frame.BtnExit != null) _frame.BtnExit.onClick.AddListener(() => CloseUI());
            }
        }

        protected override void OnDestroy()
        {
            base.OnDestroy();
            if (_frame != null)
            {
                if (_frame.BtnSingle != null) _frame.BtnSingle.onClick.RemoveAllListeners();
                if (_frame.BtnExit != null) _frame.BtnExit.onClick.RemoveAllListeners();
            }
        }

        /// <summary>에러 팝업 표시.</summary>
        public void Show(string title, string description, Sprite icon = null)
        {
            if (_frame != null)
            {
                _frame.SetTitle(title);
                _frame.SetButtonLayout(PopupCommonFrame.ButtonLayout.Single);
                _frame.SetSingleButtonText("OK");
                _frame.ShowExitButton(true);
            }

            if (_txtDescription != null) _txtDescription.text = description;
            if (_imgIcon != null)
            {
                if (icon != null)
                {
                    _imgIcon.sprite = icon;
                    _imgIcon.gameObject.SetActive(true);
                }
                else
                {
                    _imgIcon.gameObject.SetActive(false);
                }
            }

            OpenUI();
        }

        /// <summary>결제 실패 팝업 (iconCancel).</summary>
        public void ShowPaymentFailed(string description = "Payment failed. Please try again.")
        {
            Show("Error", description, _sprIconCancel);
        }

        /// <summary>인터넷 연결 없음 팝업 (iconWifi).</summary>
        public void ShowNoInternet(string description = "No internet connection. Please check your network.")
        {
            Show("Connection Error", description, _sprIconWifi);
        }

        /// <summary>결제 성공 팝업 (iconCheck). OK 누르면 onConfirm 콜백.</summary>
        public void ShowPurchaseSuccess(string description = "Purchase successful!", System.Action onConfirm = null)
        {
            Show("Success", description, _sprIconCheck);

            if (_frame != null && _frame.BtnSingle != null && onConfirm != null)
            {
                _frame.BtnSingle.onClick.RemoveAllListeners();
                _frame.BtnSingle.onClick.AddListener(() =>
                {
                    onConfirm();
                    CloseUI();
                });
            }
        }
    }
}
