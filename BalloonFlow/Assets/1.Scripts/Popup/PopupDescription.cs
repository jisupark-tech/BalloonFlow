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
        [Header("[Common Frame]")]
        [SerializeField] private PopupCommonFrame _frame;

        [Header("[Content]")]
        [SerializeField] private TMP_Text _txtDescription;
        [SerializeField] private Image _imgInnerFrame;

        private System.Action _onConfirm;

        protected override void Awake()
        {
            base.Awake();
            if (_frame != null)
            {
                if (_frame.BtnSingle != null) _frame.BtnSingle.onClick.AddListener(OnConfirm);
                if (_frame.BtnExit != null) _frame.BtnExit.onClick.AddListener(OnConfirm);
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

        /// <summary>타이틀 + 설명 텍스트 설정 후 열기.</summary>
        public void Show(string title, string description)
        {
            Show(title, description, "OK", null);
        }

        /// <summary>타이틀 + 설명 + 버튼 텍스트 + 콜백.</summary>
        public void Show(string title, string description, string buttonText,
                         System.Action onConfirm = null)
        {
            _onConfirm = onConfirm;

            if (_frame != null)
            {
                _frame.SetTitle(title);
                _frame.SetButtonLayout(PopupCommonFrame.ButtonLayout.Single);
                _frame.SetSingleButtonText(buttonText);
                _frame.ShowExitButton(true);
            }

            if (_txtDescription != null) _txtDescription.text = description;

            OpenUI();
        }

        private void OnConfirm()
        {
            _onConfirm?.Invoke();
            CloseUI();
        }
    }
}
