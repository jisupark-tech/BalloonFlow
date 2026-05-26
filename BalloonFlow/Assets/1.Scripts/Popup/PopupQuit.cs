using UnityEngine;
using UnityEngine.UI;

namespace BalloonFlow
{
    /// <summary>
    /// 나가기 확인 팝업. PopupCommonFrame 사용.
    /// Horizontal 레이아웃 (Green=Continue, Red=Home).
    /// HomeButton은 2단계 상태머신: 1차 클릭 → LoseLife/WinningStreak 자식 토글, 2차 클릭 → 로비 이동.
    /// </summary>
    public class PopupQuit : UIBase
    {
        private const string EXIT_DUP_NAME = "ExitButton (1)";
        private const string LOSELIFE_NAME = "LoseLife";
        private const string WINNINGSTREAK_NAME = "WinningStreak";

        private enum QuitView { LoseLife, WinningStreak }

        [Header("[Common Frame]")]
        [SerializeField] private PopupCommonFrame _frame;

        private Button _btnExitDuplicate;
        private bool _exitDuplicateSearched;

        private GameObject _loseLifeView;
        private GameObject _winningStreakView;
        private bool _stateViewsSearched;
        private QuitView _currentView = QuitView.LoseLife;

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
            CacheStateViews();
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

        private void CacheStateViews()
        {
            _stateViewsSearched = true;

            Transform loseLife = transform.Find(LOSELIFE_NAME);
            Transform winningStreak = transform.Find(WINNINGSTREAK_NAME);

            if (loseLife == null || winningStreak == null)
            {
                var allChildren = GetComponentsInChildren<Transform>(true);
                for (int i = 0; i < allChildren.Length; i++)
                {
                    string n = allChildren[i].name;
                    if (loseLife == null && n == LOSELIFE_NAME) loseLife = allChildren[i];
                    else if (winningStreak == null && n == WINNINGSTREAK_NAME) winningStreak = allChildren[i];
                    if (loseLife != null && winningStreak != null) break;
                }
            }

            if (loseLife != null) _loseLifeView = loseLife.gameObject;
            if (winningStreak != null) _winningStreakView = winningStreak.gameObject;
        }

        private void ResetToLoseLife()
        {
            if (!_stateViewsSearched) CacheStateViews();
            if (_loseLifeView != null) _loseLifeView.SetActive(true);
            if (_winningStreakView != null) _winningStreakView.SetActive(false);
            _currentView = QuitView.LoseLife;
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
            ResetToLoseLife();
            base.OpenUI();
            ResetToLoseLife();
        }

        /// <summary>
        /// HomeButton 클릭 시 호출. LoseLife 상태이면 WinningStreak로 전환하고 true 반환(클릭 소비),
        /// 이미 WinningStreak이거나 자식 GameObject가 미배선이면 false 반환(Caller가 로비 이동 수행).
        /// </summary>
        public bool TryAdvanceHomeButton()
        {
            if (!_stateViewsSearched) CacheStateViews();

            // 프리팹 미배선 fallback — 기존 동작(즉시 로비 이동) 유지.
            if (_loseLifeView == null || _winningStreakView == null) return false;

            if (_currentView == QuitView.LoseLife)
            {
                _loseLifeView.SetActive(false);
                _winningStreakView.SetActive(true);
                _currentView = QuitView.WinningStreak;
                return true;
            }
            return false;
        }
    }
}
