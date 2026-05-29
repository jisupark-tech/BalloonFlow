using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace BalloonFlow
{
    public class PopupContinue : UIBase
    {
        // [#15] 이어하기 팝업(① Out of Space! / ② Continue?) — 백버튼 차단 (결제/광고 의사결정 보호, UX플로우 §5-3-0).
        // 명시적 [No thanks]/[X] 탭만 다음 단계 진행 가능.
        public override BackResult OnBackPressed() => BackResult.Blocked;

        private const string DECLINE_DUP_NAME = "DeclineButton (1)";
        private const string LOSELIFE_NAME = "LoseLife";
        private const string WINNINGSTREAK_NAME = "WinningStreak";

        private enum ContinueView { LoseLife, WinningStreak }

        [Header("[Common Frame]")]
        [SerializeField] private PopupCommonFrame _frame;

        private Button _btnDeclineDuplicate;
        private bool _declineDuplicateSearched;

        private GameObject _loseLifeView;
        private GameObject _winningStreakView;
        private bool _stateViewsSearched;
        private ContinueView _currentView = ContinueView.LoseLife;

        [Header("[Buttons — 직접 할당]")]
        [SerializeField] private Button _btnContinue;
        [SerializeField] private Button _btnDecline;
        [SerializeField] private Button _btnExit;

        [Header("[코스트 텍스트]")]
        [SerializeField] private Text _costText;

        [Header("[골드 표시 — 보수적 보존(미사용). TopBar 잔액은 AnimatedCoinLabel 가 갱신.]")]
        [SerializeField] private TMP_Text _txtGold;
        [SerializeField] private TMP_Text _txtGoldOutline;

        [Header("[ContinuePanel — 난이도별 inner frame]")]
        [SerializeField] private Image _imageContinuePanel;
        [SerializeField] private Sprite _sprContinuePanelNormal;
        [SerializeField] private Sprite _sprContinuePanelHard;
        [SerializeField] private Sprite _sprContinuePanelSuperHard;

        private Button ContinueBtn => _btnContinue != null ? _btnContinue : (_frame != null ? _frame.BtnHorizGreen : null);
        private Button DeclineBtn => _btnDecline != null ? _btnDecline : (_frame != null ? _frame.BtnHorizRed : null);
        private Button ExitBtn => _btnExit != null ? _btnExit : (_frame != null ? _frame.BtnExit : null);

        public Button ContinueButton => ContinueBtn;
        public Button DeclineButton => DeclineBtn;

        // 'DeclineButton (1)' 복제 GameObject의 Button. 2단계 상태머신: 1차 클릭 → LoseLife/WinningStreak 토글, 2차 클릭 → 로비 이동.
        public Button DeclineDuplicateButton
        {
            get
            {
                if (!_declineDuplicateSearched) CacheDeclineDuplicateButton();
                return _btnDeclineDuplicate;
            }
        }

        private void OnEnable()
        {
            UpdateCostDisplay();

            DifficultyPurpose diff = DifficultyPurpose.Normal;
            if (LevelManager.HasInstance)
                diff = LevelManager.Instance.GetLevelDifficulty(LevelManager.Instance.CurrentLevelId);
            if (_frame != null) _frame.ApplyDifficulty(diff);
            ApplyContinuePanelDifficulty(diff);
        }

        protected override void Awake()
        {
            base.Awake();

            if (ResourceManager.HasInstance)
            {
                var rm = ResourceManager.Instance;
                _sprContinuePanelNormal    = rm.UISpriteOr(Const.SPR_FRAMEPOPUPCONTINUENORMAL,    _sprContinuePanelNormal);
                _sprContinuePanelHard      = rm.UISpriteOr(Const.SPR_FRAMEPOPUPCONTINUEHARD,      _sprContinuePanelHard);
                _sprContinuePanelSuperHard = rm.UISpriteOr(Const.SPR_FRAMEPOPUPCONTINUESUPERHARD, _sprContinuePanelSuperHard);
            }

            // 프리팹에 _imageContinuePanel 가 미와이어링이면 ContinuePanel 자식에서 자동 탐색
            if (_imageContinuePanel == null)
            {
                Transform panel = FindChildRecursive(transform, "ContinuePanel");
                if (panel != null) _imageContinuePanel = panel.GetComponent<Image>();
            }

            if (ContinueBtn != null) ContinueBtn.onClick.AddListener(OnContinueClicked);
            if (DeclineBtn != null) DeclineBtn.onClick.AddListener(OnDeclineClicked);
            if (ExitBtn != null) ExitBtn.onClick.AddListener(OnDeclineClicked);

            CacheStateViews();
            CacheDeclineDuplicateButton();
            if (_btnDeclineDuplicate != null)
                _btnDeclineDuplicate.onClick.AddListener(OnDeclineDuplicateClicked);

            EnsureTopBarBinding();
        }

        private void CacheDeclineDuplicateButton()
        {
            _declineDuplicateSearched = true;

            Transform found = transform.Find(DECLINE_DUP_NAME);
            if (found == null)
            {
                var allChildren = GetComponentsInChildren<Transform>(true);
                for (int i = 0; i < allChildren.Length; i++)
                {
                    if (allChildren[i].name == DECLINE_DUP_NAME)
                    {
                        found = allChildren[i];
                        break;
                    }
                }
            }
            if (found != null) _btnDeclineDuplicate = found.GetComponent<Button>();
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
            _currentView = ContinueView.LoseLife;
        }

        public override void OpenUI()
        {
            ResetToLoseLife();
            base.OpenUI();
            ResetToLoseLife();
        }

        protected override void OnDestroy()
        {
            base.OnDestroy();
            if (ContinueBtn != null) ContinueBtn.onClick.RemoveAllListeners();
            if (DeclineBtn != null) DeclineBtn.onClick.RemoveAllListeners();
            if (ExitBtn != null) ExitBtn.onClick.RemoveAllListeners();
            if (_btnDeclineDuplicate != null) _btnDeclineDuplicate.onClick.RemoveAllListeners();
        }

        private void EnsureTopBarBinding()
        {
            Transform topBar = FindChildRecursive(transform, "TopBarArea");
            Transform gold = topBar != null ? FindChildRecursive(topBar, "GoldPanel") : null;
            Transform txt = gold != null ? FindChildRecursive(gold, "TxtGold") : null;
            if (txt != null && txt.GetComponent<AnimatedCoinLabel>() == null)
                txt.gameObject.AddComponent<AnimatedCoinLabel>();
        }

        private static Transform FindChildRecursive(Transform parent, string childName)
        {
            for (int i = 0; i < parent.childCount; i++)
            {
                Transform child = parent.GetChild(i);
                if (child.name == childName) return child;
                Transform deep = FindChildRecursive(child, childName);
                if (deep != null) return deep;
            }
            return null;
        }

        public void Show()
        {
            DifficultyPurpose diff = DifficultyPurpose.Normal;
            if (LevelManager.HasInstance)
                diff = LevelManager.Instance.GetLevelDifficulty(LevelManager.Instance.CurrentLevelId);

            if (_frame != null)
            {
                _frame.ApplyDifficulty(diff);
                _frame.SetTitle("Continue?");
                _frame.SetButtonLayout(PopupCommonFrame.ButtonLayout.Horizontal);
                _frame.SetHorizGreenText("Continue");
                _frame.SetHorizRedText("Give Up");
                _frame.ShowExitButton(true);
            }
            ApplyContinuePanelDifficulty(diff);
            UpdateCostDisplay();
            ResetToLoseLife();
            OpenUI();
        }

        private void ApplyContinuePanelDifficulty(DifficultyPurpose difficulty)
        {
            if (_imageContinuePanel == null) return;
            Sprite chosen = difficulty switch
            {
                DifficultyPurpose.Hard      => _sprContinuePanelHard,
                DifficultyPurpose.SuperHard => _sprContinuePanelSuperHard,
                _                           => _sprContinuePanelNormal
            };
            if (chosen != null) _imageContinuePanel.sprite = chosen;
        }

        public void OnContinueClicked()
        {
            if (!ContinueHandler.HasInstance) return;

            int cost = ContinueHandler.Instance.GetContinueCost();
            if (CurrencyManager.HasInstance && CurrencyManager.Instance.Coins < cost && cost > 0)
            {
                Debug.Log("[PopupContinue] 골드 부족");
                return;
            }

            bool success = ContinueHandler.Instance.Continue();
            if (success)
            {
                if (PopupManager.HasInstance) PopupManager.Instance.ClosePopup("popup_continue");
            }
            else
            {
                OnDeclineClicked();
            }
        }

        public void OnDeclineClicked()
        {
            if (PopupManager.HasInstance)
            {
                PopupManager.Instance.ClosePopup("popup_continue");
                PopupManager.Instance.ShowPopup("popup_fail02", 50);
            }
        }

        /// <summary>
        /// DeclineButton (1) 클릭 핸들러. 2단계 상태머신:
        /// 1차 클릭 → LoseLife→WinningStreak 자식 토글, 2차 클릭 → 팝업 닫고 로비/MapMaker 이동.
        /// 자식 view가 미배선이면 기존 OnDeclineClicked() fallback(회귀 차단).
        /// </summary>
        public void OnDeclineDuplicateClicked()
        {
            if (!_stateViewsSearched) CacheStateViews();

            if (_loseLifeView == null && _winningStreakView == null)
            {
                OnDeclineClicked();
                return;
            }

            if (_currentView == ContinueView.LoseLife)
            {
                int multiplier = WinningStreakUI.ResolveCurrentMultiplier();
                if (multiplier <= 1)
                {
                    // 1배 → WinningStreak skip, 즉시 LoseLife 로직 (Give Up = 팝업 닫고 fail02)
                    OnDeclineClicked();
                    return;
                }

                if (_loseLifeView != null) _loseLifeView.SetActive(false);
                if (_winningStreakView != null)
                {
                    _winningStreakView.SetActive(true);
                    WinningStreakUI.PlayMultiplierIdle(_winningStreakView, multiplier);
                }
                _currentView = ContinueView.WinningStreak;
                return;
            }

            if (PopupManager.HasInstance) PopupManager.Instance.ClosePopup("popup_continue");
            if (GameManager.HasInstance)
            {
                GameManager.Instance.ResumeGame();
                if (GameManager.IsTestPlayMode)
                    GameManager.Instance.GoToMapMaker();
                else
                    GameManager.Instance.GoToLobby();
            }
        }

        private void UpdateCostDisplay()
        {
            if (_costText == null || !ContinueHandler.HasInstance) return;
            int cost = ContinueHandler.Instance.GetContinueCost();
            _costText.text = cost <= 0 ? "FREE" : cost.ToString("N0");
        }
    }
}
