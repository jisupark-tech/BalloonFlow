using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace BalloonFlow
{
    /// <summary>
    /// 에러/경고 팝업.
    /// PopupCommonFrame: Single 버튼 (OK).
    /// 아이콘 + 설명 텍스트 표시.
    /// </summary>
    public class PopupError : UIBase
    {
        [Header("[Common Frame]")]
        [SerializeField] private PopupCommonFrame _frame;

        [Header("[Content]")]
        [SerializeField] private TMP_Text _txtDescription;
        [SerializeField] private Image _imgIcon;
        [SerializeField] private Image _imgInnerFrame;

        protected override void Awake()
        {
            base.Awake();
            // 버튼 연결은 Awake에서 (CloseUI 후에도 listener 유지)
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
    }
}
