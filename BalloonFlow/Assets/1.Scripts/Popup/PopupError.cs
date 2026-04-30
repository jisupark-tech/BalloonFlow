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

        [Header("[Preset Icons — Inspector fallback. Awake 시 Addressable atlas 에서 override]")]
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

            // UI atlas 가 ResourceManager 에 사전 로드되어 있으면 sprite 교체. 미준비면 Inspector 값 그대로.
            if (ResourceManager.HasInstance)
            {
                var rm = ResourceManager.Instance;
                _sprIconCancel = rm.UISpriteOr("iconCancel", _sprIconCancel);
                _sprIconWifi   = rm.UISpriteOr("iconWifi",   _sprIconWifi);
                _sprIconCheck  = rm.UISpriteOr("iconCheck",  _sprIconCheck);
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

        /// <summary>결제 성공 팝업 (iconCheck). OK (또는 X) 누르면 onConfirm 콜백.
        /// CloseUI 먼저 → 콜백 호출 (콜백 안에서 새 popup 띄울 때 race 회피).</summary>
        public void ShowPurchaseSuccess(string description = "Purchase successful!", System.Action onConfirm = null)
        {
            Show("Success", description, _sprIconCheck);

            // Success popup 은 OK 만 — X 닫기 누르면 보상 연출이 skip 되어 사용자가 혼란.
            if (_frame != null) _frame.ShowExitButton(false);

            if (_frame != null && _frame.BtnSingle != null)
            {
                _frame.BtnSingle.onClick.RemoveAllListeners();
                _frame.BtnSingle.onClick.AddListener(() =>
                {
                    CloseUI();
                    onConfirm?.Invoke();
                });
            }
        }

        /// <summary>
        /// 구매 확인 팝업 (iconCheck + 2버튼 Yes/No).
        /// Yes (Horizontal Green): onYes 호출 후 닫힘. No (Horizontal Red) 또는 X: onNo 후 닫힘.
        /// 같은 prefab(PopupError) 재사용 — 별도 prefab 불필요.
        /// </summary>
        public void ShowConfirm(
            string title,
            string description,
            System.Action onYes,
            System.Action onNo = null,
            string yesText = "Buy",
            string noText  = "Cancel")
        {
            if (_frame != null)
            {
                _frame.SetTitle(title);
                _frame.SetButtonLayout(PopupCommonFrame.ButtonLayout.Horizontal);
                _frame.SetHorizGreenText(yesText);
                _frame.SetHorizRedText(noText);
                _frame.ShowExitButton(true);
            }

            if (_txtDescription != null) _txtDescription.text = description;
            if (_imgIcon != null)
            {
                if (_sprIconCheck != null)
                {
                    _imgIcon.sprite = _sprIconCheck;
                    _imgIcon.gameObject.SetActive(true);
                }
                else
                {
                    _imgIcon.gameObject.SetActive(false);
                }
            }

            if (_frame != null)
            {
                // CloseUI 를 먼저 호출 — 콜백(예: Buy → IAPManager → 새 PopupError 띄움) 이 같은 인스턴스를 재용도하는 race 회피.
                if (_frame.BtnHorizGreen != null)
                {
                    _frame.BtnHorizGreen.onClick.RemoveAllListeners();
                    _frame.BtnHorizGreen.onClick.AddListener(() =>
                    {
                        CloseUI();
                        onYes?.Invoke();
                    });
                }
                if (_frame.BtnHorizRed != null)
                {
                    _frame.BtnHorizRed.onClick.RemoveAllListeners();
                    _frame.BtnHorizRed.onClick.AddListener(() =>
                    {
                        CloseUI();
                        onNo?.Invoke();
                    });
                }
                if (_frame.BtnExit != null)
                {
                    _frame.BtnExit.onClick.RemoveAllListeners();
                    _frame.BtnExit.onClick.AddListener(() =>
                    {
                        CloseUI();
                        onNo?.Invoke();
                    });
                }
            }

            OpenUI();
        }
    }
}
