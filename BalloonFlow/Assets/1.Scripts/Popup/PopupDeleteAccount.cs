using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace BalloonFlow
{
    /// <summary>
    /// 계정 삭제(탈퇴) 확인 전용 팝업. Horizontal 레이아웃 — Green=Stay(취소), Red=Delete(삭제).
    /// 기존 PopupError.ShowDeleteAccountConfirm 의 동작을 전용 프리팹(Popup/PopupDeleteAccount)으로 분리.
    /// UISetting.OnDeleteAccountClicked 가 OpenUI&lt;PopupDeleteAccount&gt; 후 Show(onDelete) 호출.
    /// onDelete 는 Red 버튼 클릭 시에만 발화(닫은 뒤 호출). Green/X 는 닫기만(탈퇴 안 함).
    ///
    /// ROLLBACK_POPUP_DELETE_ACCOUNT_DEDICATED_20260623:
    /// 전용 팝업 폐기 시 이 스크립트 제거 + UISetting 을 PopupError.ShowDeleteAccountConfirm 로 환원.
    /// ※ 활성화: 프리팹 Popup/PopupDeleteAccount 에 이 컴포넌트 부착 + PopupCommonFrame(_frame)·설명텍스트(_txtDescription) 할당.
    /// </summary>
    public class PopupDeleteAccount : UIBase
    {
        [Header("[Common Frame]")]
        [SerializeField] private PopupCommonFrame _frame;

        [Header("[Content]")]
        [SerializeField] private TMP_Text _txtDescription;
        [SerializeField] private Image _imgIcon;
        [SerializeField] private Sprite _sprIconCancel;

        /// <summary>탈퇴 확인 팝업 표시. Red(삭제) 확정 시에만 onDelete 발화.</summary>
        public void Show(System.Action onDelete)
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
                    _frame.BtnHorizGreen.onClick.AddListener(CloseUI); // Stay = 닫기만
                }
                if (_frame.BtnHorizRed != null)
                {
                    _frame.BtnHorizRed.onClick.RemoveAllListeners();
                    _frame.BtnHorizRed.onClick.AddListener(() =>
                    {
                        CloseUI();
                        onDelete?.Invoke(); // Delete = 닫고 실제 탈퇴 콜백
                    });
                }
                if (_frame.BtnExit != null)
                {
                    _frame.BtnExit.onClick.RemoveAllListeners();
                    _frame.BtnExit.onClick.AddListener(CloseUI); // X = 닫기만(탈퇴 안 함)
                }
            }

            OpenUI();
        }
    }
}
