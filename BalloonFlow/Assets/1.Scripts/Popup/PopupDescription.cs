using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace BalloonFlow
{
    /// <summary>
    /// 설명/정보 팝업. 범용 텍스트 표시용.
    /// PopupCommonFrame 사용. Single 버튼 (확인).
    /// </summary>
    public class PopupDescription : UIBase
    {
        public static bool IsShowing { get; private set; }

        [Header("[Common Frame]")]
        [SerializeField] private PopupCommonFrame _frame;

        [Header("[Content]")]
        [SerializeField] private TMP_Text _txtDescription;
        [SerializeField] private Image _imgInnerFrame;

        private System.Action _onConfirm;
        private bool _exitClosesOnly;

        protected override void Awake()
        {
            base.Awake();
            if (_frame != null)
            {
                if (_frame.BtnSingle != null) _frame.BtnSingle.onClick.AddListener(OnConfirm);
                if (_frame.BtnExit != null) _frame.BtnExit.onClick.AddListener(OnExitClicked);
            }
        }

        private void OnEnable() => IsShowing = true;
        private void OnDisable() => IsShowing = false;

        protected override void OnDestroy()
        {
            base.OnDestroy();
            if (_frame != null)
            {
                if (_frame.BtnSingle != null) _frame.BtnSingle.onClick.RemoveAllListeners();
                if (_frame.BtnExit != null) _frame.BtnExit.onClick.RemoveAllListeners();
            }
            IsShowing = false;
        }

        /// <summary>타이틀 + 설명 텍스트 설정 후 열기.</summary>
        public void Show(string title, string description)
        {
            Show(title, description, LocalizationService.Get("ui.common.ok"), null);
        }

        /// <summary>타이틀 + 설명 + 버튼 텍스트 + 콜백.</summary>
        public void Show(string title, string description, string buttonText,
                         System.Action onConfirm = null)
        {
            Show(title, description, buttonText, onConfirm, false);
        }

        /// <summary>타이틀 + 설명 + 버튼 텍스트 + 콜백 + X버튼 동작 분리.</summary>
        public void Show(string title, string description, string buttonText,
                         System.Action onConfirm, bool exitClosesOnly)
        {
            _onConfirm = onConfirm;
            _exitClosesOnly = exitClosesOnly;

            if (_frame != null)
            {
                _frame.SetTitle(title);
                _frame.SetButtonLayout(PopupCommonFrame.ButtonLayout.Single);
                _frame.SetSingleButtonText(buttonText);
                _frame.ShowExitButton(true);
            }

            if (_txtDescription != null) _txtDescription.text = description;

            IsShowing = true;
            OpenUI();
        }

        private void OnConfirm()
        {
            IsShowing = false;
            _onConfirm?.Invoke();
            CloseUI();
        }

        // X 버튼 = 취소(콜백 미발화), Single 버튼 = 확정
        private void OnExitClicked()
        {
            if (_exitClosesOnly)
            {
                IsShowing = false;
                CloseUI();
                return;
            }
            OnConfirm();
        }
    }
}
