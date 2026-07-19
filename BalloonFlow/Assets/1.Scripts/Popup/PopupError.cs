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
                _frame.SetSingleButtonText(LocalizationService.Get("ui.common.continue"));
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

        /// <summary>결제 실패 팝업 (iconCancel). 텍스트는 CSV(TextData) Key 로드. description=null 이면 CSV 기본문구.</summary>
        public void ShowPaymentFailed(string description = null)
        {
            Show(LocalizationService.Get("popup.txttitle.purchaseerror"),
                 description ?? LocalizationService.Get("popup.txtdescription.purchaseerror"), _sprIconCancel);
        }

        /// <summary>인터넷 연결 없음 팝업 (iconWifi).</summary>
        public void ShowNoInternet(string description = null)
        {
            Show(LocalizationService.Get("popup.txttitle.networkerror"),
                 description ?? LocalizationService.Get("popup.txtdescription.networkerror"), _sprIconWifi);
        }

        /// <summary>결제 성공 팝업 (iconCheck). OK (또는 X) 누르면 onConfirm 콜백.
        /// CloseUI 먼저 → 콜백 호출 (콜백 안에서 새 popup 띄울 때 race 회피).</summary>
        public void ShowPurchaseSuccess(string description = null, System.Action onConfirm = null)
        {
            Show(LocalizationService.Get("popup.txttitle.purchasesuccess"),
                 description ?? LocalizationService.Get("popup.txtdescription.purchasesuccess"), _sprIconCheck);

            // Success popup 은 OK 만 — X 닫기 누르면 보상 연출이 skip 되어 사용자가 혼란.
            if (_frame != null) _frame.ShowExitButton(true);

            bool handled = false;
            void ConfirmAndClose()
            {
                if (handled) return;
                handled = true;
                CloseUI();
                onConfirm?.Invoke();
            }

            if (_frame != null && _frame.BtnSingle != null)
            {
                _frame.BtnSingle.onClick.RemoveAllListeners();
                _frame.BtnSingle.onClick.AddListener(ConfirmAndClose);
            }

            if (_frame != null && _frame.BtnExit != null)
            {
                // Purchase success uses PopupError as a reward gate. Exit mirrors OK.
                _frame.BtnExit.onClick.RemoveAllListeners();
                _frame.BtnExit.onClick.AddListener(ConfirmAndClose);
            }
        }

        /// <summary>
        /// 구매 확인 팝업 (iconCheck + 2버튼 Yes/No).
        /// Yes (Horizontal Green): onYes 호출 후 닫힘. No (Horizontal Red) 또는 X: onNo 후 닫힘.
        /// 같은 prefab(PopupError) 재사용 — 별도 prefab 불필요.
        /// </summary>
        // ROLLBACK_FORCE_UPDATE_20260715: 강제 업데이트 — 닫기 불가 단일버튼(업데이트→스토어).
        //   X/취소 없음. 버튼 눌러 스토어로 가도 CloseUI 하지 않음(복귀 시 팝업 유지 = 계속 차단).
        public void ShowForceUpdate(string title, string description, string buttonText, System.Action onUpdate)
        {
            if (_frame != null)
            {
                _frame.SetTitle(title);
                _frame.SetButtonLayout(PopupCommonFrame.ButtonLayout.Single);
                _frame.SetSingleButtonText(buttonText);
                _frame.ShowExitButton(false); // 닫기 불가 — 버튼 비활성
                // ExitButton 은 버튼만 꺼선 프레임/컨테이너가 남아 보임 → 부모까지 비활성해 완전 비표시.
                //   (강제업뎃은 터미널 상태라 재사용 side-effect 없음)
                if (_frame.BtnExit != null && _frame.BtnExit.transform.parent != null)
                    _frame.BtnExit.transform.parent.gameObject.SetActive(false);
            }
            if (_txtDescription != null) _txtDescription.text = description;
            if (_imgIcon != null) _imgIcon.gameObject.SetActive(true); // 아이콘 표시(프리팹 스프라이트 사용)
            if (_frame != null && _frame.BtnSingle != null)
            {
                _frame.BtnSingle.onClick.RemoveAllListeners();
                _frame.BtnSingle.onClick.AddListener(() => onUpdate?.Invoke()); // CloseUI 없음 → 팝업 유지
            }
            OpenUI();
        }

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

        /// <summary>
        /// ROLLBACK_DELETE_ACCOUNT_CONFIRM_20260622:
        /// Delete-account confirmation uses the horizontal layout with Green=Stay and Red=Delete.
        /// Restore to ShowConfirm(...) if the shared confirm button direction is standardized later.
        /// </summary>
        public void ShowDeleteAccountConfirm(System.Action onDelete)
        {
            if (_frame != null)
            {
                _frame.SetTitle(LocalizationService.Get("popup.txttitle.deleteaccount"));
                _frame.SetButtonLayout(PopupCommonFrame.ButtonLayout.Horizontal);
                _frame.SetHorizGreenText(LocalizationService.Get("ui.common.stay"));
                _frame.SetHorizRedText(LocalizationService.Get("ui.common.delete"));
                _frame.ShowExitButton(true);
            }

            if (_txtDescription != null)
                _txtDescription.text = LocalizationService.Get("popup.txtdescription.deleteaccount");

            if (_imgIcon != null)
            {
                if (_sprIconCancel != null)
                {
                    _imgIcon.sprite = _sprIconCancel;
                    _imgIcon.gameObject.SetActive(true);
                }
                else
                {
                    _imgIcon.gameObject.SetActive(false);
                }
            }

            if (_frame != null)
            {
                if (_frame.BtnHorizGreen != null)
                {
                    _frame.BtnHorizGreen.onClick.RemoveAllListeners();
                    _frame.BtnHorizGreen.onClick.AddListener(CloseUI);
                }

                if (_frame.BtnHorizRed != null)
                {
                    _frame.BtnHorizRed.onClick.RemoveAllListeners();
                    _frame.BtnHorizRed.onClick.AddListener(() =>
                    {
                        CloseUI();
                        onDelete?.Invoke();
                    });
                }

                if (_frame.BtnExit != null)
                {
                    _frame.BtnExit.onClick.RemoveAllListeners();
                    _frame.BtnExit.onClick.AddListener(CloseUI);
                }
            }

            OpenUI();
        }
    }
}
