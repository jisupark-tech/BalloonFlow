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
        private const string EXIT_DUP_NAME = "ExitButton (1)";

        [Header("[Common Frame]")]
        [SerializeField] private PopupCommonFrame _frame;

        private Button _btnExitDuplicate;
        private bool _exitDuplicateSearched;

        public Button HomeButton => _frame != null ? _frame.BtnHorizRed : null;
        public Button NextButton => _frame != null ? _frame.BtnHorizGreen : null;

        // 'ExitButton (1)' 복제 GameObject의 Button. 클릭 시 Continue/Next와 동일하게 팝업 닫고 인게임 복귀.
        public Button ExitDuplicateButton
        {
            get
            {
                if (!_exitDuplicateSearched) CacheExitDuplicateButton();
                return _btnExitDuplicate;
            }
        }

        private void Awake()
        {
            CacheExitDuplicateButton();
        }

        private void CacheExitDuplicateButton()
        {
            _exitDuplicateSearched = true;

            Transform found = transform.Find(EXIT_DUP_NAME);
            if (found == null)
            {
                var allChildren = GetComponentsInChildren<Transform>(true);
                for (int i = 0; i < allChildren.Length; i++)
                {
                    if (allChildren[i].name == EXIT_DUP_NAME)
                    {
                        found = allChildren[i];
                        break;
                    }
                }
            }
            if (found != null) _btnExitDuplicate = found.GetComponent<Button>();
        }

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
