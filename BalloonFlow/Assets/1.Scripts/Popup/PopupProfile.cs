using UnityEngine;
using UnityEngine.UI;

namespace BalloonFlow
{
    /// <summary>
    /// 프로필 팝업. PopupCommonFrame 사용. ExitButton 으로 닫기만 수행.
    /// PopupSettings 의 ExitButton 자체 바인딩 패턴을 그대로 따른다 (Sound/Music/Haptic 토글 제외).
    /// </summary>
    public class PopupProfile : UIBase
    {
        [Header("[Common Frame]")]
        [SerializeField] private PopupCommonFrame _frame;

        public Button CloseButton => _frame != null ? _frame.BtnExit : null;

        protected override void Awake()
        {
            base.Awake();

            if (_frame != null && _frame.BtnExit != null)
                _frame.BtnExit.onClick.AddListener(OnExitClickedSelf);
        }

        private void OnExitClickedSelf()
        {
            CloseUI();
        }

        protected override void OnDestroy()
        {
            base.OnDestroy();
            if (_frame != null && _frame.BtnExit != null)
                _frame.BtnExit.onClick.RemoveListener(OnExitClickedSelf);
        }

        public override void OpenUI()
        {
            if (_frame != null)
            {
                _frame.SetTitle("Profile");
                _frame.SetButtonLayout(PopupCommonFrame.ButtonLayout.Horizontal);
                _frame.ShowExitButton(true);
            }

            base.OpenUI();

            // 애니메이션 사용 시 base.OpenUI 가 interactable=false 로 시작 → ExitButton 클릭 안 됨.
            // 즉시 클릭 가능하도록 강제 (PopupSettings 와 동일 패턴).
            if (_canvasGroup != null)
            {
                _canvasGroup.interactable = true;
                _canvasGroup.blocksRaycasts = true;
            }
            if (_frame != null && _frame.BtnExit != null)
            {
                _frame.BtnExit.interactable = true;
                _frame.BtnExit.gameObject.SetActive(true);
            }
        }
    }
}
