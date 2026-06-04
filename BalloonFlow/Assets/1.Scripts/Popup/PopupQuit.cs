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

        // [2026-06-04] InGame 중 Quit 팝업 열림 시 게임 일시정지 + 보관함(Holder) 터치 차단.
        // PopupSettings 와 동일 패턴 (_paused 가드로 OnEnable 중복 호출 방어).
        private bool _paused;
        private void OnEnable()
        {
            if (!_paused) { PauseManager.Pause(); _paused = true; }
            if (InputHandler.HasInstance) InputHandler.Instance.DisableInput();
        }
        private void OnDisable()
        {
            if (_paused) { PauseManager.Resume(); _paused = false; }
            if (InputHandler.HasInstance) InputHandler.Instance.EnableInput();
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
                _frame.SetTitle("Quit Level?");
                _frame.SetDescription("You will lose a life.");
                _frame.SetButtonLayout(PopupCommonFrame.ButtonLayout.Horizontal);
                _frame.SetHorizGreenText("Stay");
                _frame.SetHorizRedText("Quit");
                _frame.ShowExitButton(true);
            }
            ResetToLoseLife();
            base.OpenUI();
            ResetToLoseLife();
        }

        /// <summary>
        /// HomeButton 클릭 시 호출. LoseLife 상태이면 WinningStreak로 전환하고 true 반환(클릭 소비),
        /// 이미 WinningStreak이거나 자식 GameObject가 미배선이면 false 반환(Caller가 로비 이동 수행).
        /// 1배 (currentStreak=1) 인 경우엔 WinningStreak 노출 skip — 즉시 false 반환해서 로비로.
        /// </summary>
        public bool TryAdvanceHomeButton()
        {
            if (!_stateViewsSearched) CacheStateViews();

            // 프리팹 미배선 fallback — 기존 동작(즉시 로비 이동) 유지.
            if (_loseLifeView == null || _winningStreakView == null) return false;

            if (_currentView == QuitView.LoseLife)
            {
                int multiplier = WinningStreakUI.ResolveCurrentMultiplier();
                if (multiplier <= 1)
                {
                    // 1배 상태 → WinningStreak 노출 skip. caller 가 로비 이동.
                    return false;
                }

                _loseLifeView.SetActive(false);
                _winningStreakView.SetActive(true);
                _currentView = QuitView.WinningStreak;
                if (_frame != null) _frame.SetDescription($"You will lose your x{multiplier} multiplier!");
                WinningStreakUI.PlayMultiplierIdle(_winningStreakView, multiplier);
                return true;
            }
            return false;
        }
    }
}
