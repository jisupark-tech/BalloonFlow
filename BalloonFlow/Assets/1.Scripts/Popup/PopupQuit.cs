using System;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

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
        private const string IMAGE_HEART_NAME = "ImageHeart";
        private const string TXT_TITLE_KEY = "popup.txttitle.settingquit";
        private const string TXT_DESC_LOSE_LIFE_KEY = "popup.txtdescription.settingquit";
        private const string TXT_DESC_MULTIPLIER_KEY = "popup.txtdescription.settingquit.multiplier";
        private const string TXT_DESC_INFINITE_HEART_KEY = "popup.txtdescription.quit";
        private const string TXT_DESC_INFINITE_HEART_FALLBACK = "Do you really want to quit?";

        private enum QuitView { LoseLife, WinningStreak }

        [Header("[Common Frame]")]
        [SerializeField] private PopupCommonFrame _frame;

        private Button _btnExitDuplicate;
        private bool _exitDuplicateSearched;

        private GameObject _loseLifeView;
        private GameObject _winningStreakView;
        private bool _stateViewsSearched;
        private QuitView _currentView = QuitView.LoseLife;

        private Image _imageHeart;
        private bool _imageHeartSearched;
        private Sprite _sprHeartInfinite;
        private Sprite _sprHeartBreak;
        private bool _inputDisabled;

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

        protected override void Awake()
        {
            // ROLLBACK_POPUPQUIT_UIBASE_AWAKE_20260622:
            // PopupQuit is preloaded/closed/reopened through UIBase. Without base.Awake(),
            // UIBase never caches the CanvasGroup, so a previously closed popup can reopen
            // with interactable/blocksRaycasts still false after repeated fail/quit flows.
            base.Awake();
            CacheExitDuplicateButton();
            CacheStateViews();
            CacheImageHeart();
            LoadHeartSprites();
            GoldPanelFxFireUtil.DisableUnderTopBarRoot(transform);
        }

        // [2026-06-04] InGame 중 Quit 팝업 열림 시 게임 일시정지 + 보관함(Holder) 터치 차단.
        // PopupSettings 와 동일 패턴 (_paused 가드로 OnEnable 중복 호출 방어).
        private bool _paused;
        private void OnEnable()
        {
            EnsureGamePaused();
        }
        private void OnDisable()
        {
            ReleaseGamePause();
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
            CoinFlyEffect.ClearActiveCoinsForPopup();
            GoldPanelFxFireUtil.DisableUnderTopBarRoot(transform);
            EnsureGamePaused();
            bool infiniteHearts = IsInfiniteHeartsActiveForQuitPopup();
            ApplyQuitFrameText(infiniteHearts);
            ResetToLoseLife();
            ApplyLoseLifeHeartVisual(infiniteHearts);
            base.OpenUI();

            // ROLLBACK_POPUPQUIT_INFINITE_HEART_COPY_20260624:
            // UIText components apply prefab keys during OnEnable. Reapply dynamic TextData
            // after base.OpenUI() so infinite-heart copy is not overwritten by prefab defaults.
            ResetToLoseLife();
            ApplyQuitFrameText(infiniteHearts);
            ApplyLoseLifeHeartVisual(infiniteHearts);
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
                string description = LocalizationService.GetWith(TXT_DESC_MULTIPLIER_KEY, "n", multiplier);
                if (_frame != null) _frame.SetDescription(description);
                ApplyCommonPanelDescriptionFallback(description);
                WinningStreakUI.PlayMultiplierAnimationForPopupQuit(_winningStreakView, multiplier);
                return true;
            }
            return false;
        }

        /// <summary>[2026-06-15] PopupQuit 닫힐 때 Multiplier 애니메이션을 MultiplierDefault 로 초기화 —
        /// 다음 오픈에서 잔존 state 진입 방지. base.CloseUI() 의 SetActive(false) 전에 실행해야 animator.Play 가 적용됨.
        /// 활성 체크는 OnDisable/CloseUI 중복 호출 및 LoseLife 단계에서 닫힌 케이스(WS view 미노출)에서 불필요한 작업을 막기 위함.</summary>
        public override void CloseUI()
        {
            if (_winningStreakView != null && _winningStreakView.activeInHierarchy)
                WinningStreakUI.ResetPopupQuitMultiplierAnimation(_winningStreakView);
            base.CloseUI();
            ReleaseGamePause();
        }

        private static DifficultyPurpose ResolveCurrentDifficulty()
        {
            // ROLLBACK_QUIT_SETTINGS_DIFFICULTY_FRAME_20260623:
            // Match PopupResult/PopupBuyItem frame color behavior for in-game quit popup.
            if (!LevelManager.HasInstance) return DifficultyPurpose.Normal;
            int levelId = LevelManager.Instance.CurrentLevelId;
            return levelId > 0
                ? LevelManager.Instance.GetLevelDifficulty(levelId)
                : DifficultyPurpose.Normal;
        }

        private void ApplyQuitFrameText(bool infiniteHearts)
        {
            if (_frame == null) return;

            string description = infiniteHearts
                ? GetInfiniteHeartQuitDescription()
                : LocalizationService.Get(TXT_DESC_LOSE_LIFE_KEY);

            _frame.ApplyDifficulty(ResolveCurrentDifficulty());
            _frame.SetTitle(LocalizationService.Get(TXT_TITLE_KEY));
            // ROLLBACK_TITLE_NOWRAP_20260715: KR 은 한 줄 강제(No Wrap), EN 은 기본 유지.
            _frame.SetTitleNoWrap(LocalizationService.CurrentLanguageCode == "KO");
            _frame.SetDescription(description);
            ApplyCommonPanelDescriptionFallback(description);
            _frame.SetButtonLayout(PopupCommonFrame.ButtonLayout.Horizontal);
            _frame.SetHorizGreenText(LocalizationService.Get("ui.common.stay"));
            _frame.SetHorizRedText(LocalizationService.Get("ui.common.quit"));
            _frame.ShowExitButton(true);
        }

        private static string GetInfiniteHeartQuitDescription()
        {
            string text = LocalizationService.Get(TXT_DESC_INFINITE_HEART_KEY);
            return string.IsNullOrEmpty(text) || text == TXT_DESC_INFINITE_HEART_KEY
                ? TXT_DESC_INFINITE_HEART_FALLBACK
                : text;
        }

        private void ApplyCommonPanelDescriptionFallback(string description)
        {
            // ROLLBACK_POPUPQUIT_COMMONPANEL_DESCRIPTION_20260624:
            // PopupQuit prefab revisions can have the visible CommonPanel description wired
            // differently from PopupCommonFrame._txtDescription. Keep only PopupQuit guarded,
            // and mirror the resolved TextData copy into visible TxtDescription nodes.
            Transform scope = FindChildRecursive(transform, "CommonPanel")
                           ?? (_frame != null ? _frame.transform : transform);
            var labels = scope.GetComponentsInChildren<TMP_Text>(true);
            for (int i = 0; i < labels.Length; i++)
            {
                TMP_Text label = labels[i];
                if (label == null) continue;
                string n = label.name;
                if (n == "TxtDescription" || n == "TxtDescriptionOutline")
                    label.text = description;
            }
        }

        private static bool IsInfiniteHeartsActiveForQuitPopup()
        {
            if (LifeManager.HasInstance && LifeManager.Instance.IsInfiniteHeartsActive)
                return true;

            if (UserDataService.HasInstance && UserDataService.Instance.IsReady)
            {
                var user = UserDataService.Instance.CurrentUser;
                if (user != null)
                {
                    DateTime until = user.infiniteHeartsUntil.ToDateTime();
                    if (until > DateTime.UtcNow)
                        return true;
                }
            }

            return false;
        }

        private void EnsureGamePaused()
        {
            if (!_paused)
            {
                // ROLLBACK_POPUPQUIT_OPENUI_PAUSE_20260624:
                // PopupQuit can be preloaded/closed through CanvasGroup and reopened without a
                // reliable OnEnable-only pause point. OpenUI must explicitly freeze gameplay.
                PauseManager.Pause();
                _paused = true;
            }

            if (!_inputDisabled && InputHandler.HasInstance)
            {
                InputHandler.Instance.DisableInput();
                _inputDisabled = true;
            }
        }

        private void ReleaseGamePause()
        {
            if (_paused)
            {
                PauseManager.Resume();
                _paused = false;
            }

            if (_inputDisabled && InputHandler.HasInstance)
            {
                InputHandler.Instance.EnableInput();
                _inputDisabled = false;
            }
        }

        private void CacheImageHeart()
        {
            _imageHeartSearched = true;

            Transform found = FindChildRecursive(transform, IMAGE_HEART_NAME);
            if (found != null) _imageHeart = found.GetComponent<Image>();
        }

        private void LoadHeartSprites()
        {
            if (!ResourceManager.HasInstance) return;

            var rm = ResourceManager.Instance;
            _sprHeartInfinite = rm.UISpriteOr(Const.SPR_ICONHEARINFINITE, _sprHeartInfinite);
            _sprHeartBreak = rm.UISpriteOr(Const.SPR_ICONHEARTBREAK, _sprHeartBreak);
        }

        private void ApplyLoseLifeHeartVisual(bool infiniteHearts)
        {
            if (!_imageHeartSearched) CacheImageHeart();
            if (_sprHeartInfinite == null || _sprHeartBreak == null) LoadHeartSprites();

            if (_imageHeart == null) return;

            Sprite sprite = infiniteHearts ? _sprHeartInfinite : _sprHeartBreak;
            if (sprite != null) _imageHeart.sprite = sprite;
        }

        private static Transform FindChildRecursive(Transform root, string childName)
        {
            if (root == null || string.IsNullOrEmpty(childName)) return null;

            if (root.name == childName) return root;

            for (int i = 0; i < root.childCount; i++)
            {
                Transform found = FindChildRecursive(root.GetChild(i), childName);
                if (found != null) return found;
            }

            return null;
        }
    }
}
