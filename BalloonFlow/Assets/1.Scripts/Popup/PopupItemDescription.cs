using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace BalloonFlow
{
    /// <summary>
    /// 아이템 해금 레벨에서 뜨는 '아이템 설명' 팝업. 구조는 PopupDescription 과 동일
    /// (PopupCommonFrame + 설명 텍스트 + Single 버튼)이되, 튜토리얼 시작 게이트를 위해
    /// static <see cref="IsShowing"/> 을 노출한다.
    ///
    /// ROLLBACK_POPUP_ITEM_DESC_TUTORIAL_GATE_20260622:
    /// TutorialController.StartTutorialAfterLoad 가 'waitForItemDescription' 으로 표시된 튜토리얼에 대해
    /// IsShowing 이 false 가 될 때까지(= ButtonSingle/Exit 로 닫힐 때까지) 대기 → "PopupItemDescription
    /// 의 ButtonSingle 클릭 후에 튜토리얼이 나온다" 를 보장(동시 노출 방지).
    /// 프리팹(Popup/PopupItemDescription)에 이 컴포넌트를 부착하고 PopupCommonFrame 을 할당해 사용.
    /// </summary>
    public class PopupItemDescription : UIBase
    {
        /// <summary>현재 이 팝업이 떠 있는지(튜토리얼 시작 게이트용). 열릴 때 true, ButtonSingle/Exit/파괴 시 false.</summary>
        public static bool IsShowing { get; private set; }

        // 설명 강제 확인 — 하드웨어 백버튼으로 닫히지 않게(튜토리얼 흐름 보호).
        public override BackResult OnBackPressed() => BackResult.Blocked;

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
                // ButtonSingle(확인) 과 Exit(X) 모두 '확인 후 닫기' — 닫히면 튜토리얼 게이트가 풀린다.
                if (_frame.BtnSingle != null) _frame.BtnSingle.onClick.AddListener(OnConfirm);
                if (_frame.BtnExit != null) _frame.BtnExit.onClick.AddListener(OnConfirm);
            }
        }

        // ROLLBACK_POPUP_ITEM_DESC_TUTORIAL_GATE_20260622: 생명주기 기반 IsShowing —
        //   오프너가 Show() 를 부르든(권장), OpenUI/SetActive 로 띄우든 '활성화되면 떠 있는 것'으로 간주.
        //   → 컴포넌트 부착 + Common Frame 할당만으로도 게이트가 동작(오프너 방식 무관).
        private void OnEnable() => IsShowing = true;
        private void OnDisable() => IsShowing = false; // CloseUI(SetActive false)/풀 반납/씬 전환 모두 커버.

        protected override void OnDestroy()
        {
            base.OnDestroy();
            if (_frame != null)
            {
                if (_frame.BtnSingle != null) _frame.BtnSingle.onClick.RemoveAllListeners();
                if (_frame.BtnExit != null) _frame.BtnExit.onClick.RemoveAllListeners();
            }
            IsShowing = false; // 파괴 시에도 게이트가 영구히 막히지 않도록 방어.
        }

        /// <summary>타이틀 + 설명 텍스트로 열기. buttonText 비우면 프리팹 기본 텍스트 유지.</summary>
        public void Show(string title, string description, string buttonText = null, System.Action onConfirm = null)
        {
            _onConfirm = onConfirm;

            if (_frame != null)
            {
                _frame.SetTitle(title);
                _frame.SetButtonLayout(PopupCommonFrame.ButtonLayout.Single);
                if (!string.IsNullOrEmpty(buttonText)) _frame.SetSingleButtonText(buttonText);
                _frame.ShowExitButton(true);
            }

            if (_txtDescription != null) _txtDescription.text = description;

            IsShowing = true; // OpenUI 전에 set — 같은 OnLevelLoaded 프레임에 튜토리얼 게이트가 관측하도록.
            OpenUI();
        }

        private void OnConfirm()
        {
            IsShowing = false; // ButtonSingle/Exit 클릭 → 게이트 해제 → 튜토리얼 진행.
            _onConfirm?.Invoke();
            CloseUI();
        }
    }
}
