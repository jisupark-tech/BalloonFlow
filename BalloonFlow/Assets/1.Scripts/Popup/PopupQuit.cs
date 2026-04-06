using UnityEngine;
using UnityEngine.UI;

namespace BalloonFlow
{
    /// <summary>
    /// 나가기 확인 팝업. PopupCommonFrame 사용.
    /// Horizontal 레이아웃 (Green=Continue, Red=Home).
    /// </summary>
    public class PopupQuit : UIBase
    {
        [Header("[Common Frame]")]
        [SerializeField] private PopupCommonFrame _frame;

        public Button HomeButton => _frame != null ? _frame.BtnHorizRed : null;
        public Button NextButton => _frame != null ? _frame.BtnHorizGreen : null;

        public override void OpenUI()
        {
            if (_frame != null)
            {
                _frame.SetTitle("Quit Game?");
                _frame.SetButtonLayout(PopupCommonFrame.ButtonLayout.Horizontal);
                _frame.SetHorizGreenText("Continue");
                _frame.SetHorizRedText("Home");
                _frame.ShowExitButton(true);
            }
            base.OpenUI();
        }
    }
}
