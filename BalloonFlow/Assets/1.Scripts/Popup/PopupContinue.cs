using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace BalloonFlow
{
    public class PopupContinue : UIBase
    {
        // [#15] 이어하기 팝업(① Out of Space! / ② Continue?) — 백버튼 차단 (결제/광고 의사결정 보호, UX플로우 §5-3-0).
        // 명시적 [No thanks]/[X] 탭만 다음 단계 진행 가능.
        // ROLLBACK_POPUP_CONTINUE_BACK_EXIT_20260624:
        // Previously Android back/ESC returned BackResult.Blocked for the Out of Space / Continue popup.
        // Back now follows the same path as the X button so PopupManager state and fail-flow transitions stay in sync.
        // Rollback: restore `public override BackResult OnBackPressed() => BackResult.Blocked;`.
        public override BackResult OnBackPressed()
        {
            Button exit = ExitBtn;
            if (exit != null && exit.gameObject.activeInHierarchy && exit.interactable)
            {
                exit.onClick.Invoke();
                return BackResult.Handled;
            }

            OnDeclineDuplicateClicked();
            return BackResult.Handled;
        }

        private const string DECLINE_DUP_NAME = "DeclineButton (1)";
        private const string LOSELIFE_NAME = "LoseLife";
        private const string WINNINGSTREAK_NAME = "WinningStreak";
        private const string MULTIPLIER_NAME = "Multiplier";
        private const string GOLD_PLUS_BUTTON_NAME = "GoldPlusBtn";
        private const string POPUP_QUIT_RESOURCE_PATH = "Popup/PopupQuit";
        private const string POPUP_FAIL01_RESOURCE_PATH = "Popup/PopupFail01";

        // ROLLBACK_POPUP_CONTINUE_MULTIPLIER_X5_20260615:
        // Multiplier(연승 배수) 경고 단계를 노출하는 최소 배수. x5 이상에서만 LoseLife→Multiplier→Lobby 플로우,
        // x5 미만은 기존 종료 플로우(닫고 fail02) 유지. (기존 임계 <=1 → <5). 롤백: 사용처 조건을 multiplier <= 1 로 환원.
        private const int MULTIPLIER_VIEW_MIN_MULTIPLIER = 5;

        private const int OVERLAY_SORT_ORDER = 260; // Tutorial(=250) 위에 항상 표시 — 사용자 요청 2026-06-04
        private Canvas _overrideCanvas;

        private enum ContinueView { LoseLife, WinningStreak }

        [Header("[Common Frame]")]
        [SerializeField] private PopupCommonFrame _frame;

        private Button _btnDeclineDuplicate;
        private bool _declineDuplicateSearched;

        private GameObject _loseLifeView;
        private GameObject _winningStreakView;
        private bool _stateViewsSearched;
        private ContinueView _currentView = ContinueView.LoseLife;
        private TMP_Text _txtContinueTitle;
        private TMP_Text _txtContinueTitleOutline;
        private bool _continueTitleSearched;

        [Header("[Buttons — 직접 할당]")]
        [SerializeField] private Button _btnContinue;
        [SerializeField] private Button _btnDecline;
        [SerializeField] private Button _btnExit;
        [SerializeField] private Button _btnGoldPlus;

        [Header("[코스트 텍스트]")]
        [SerializeField] private Text _costText;
        // ROLLBACK_CONTINUE_GOLD_COST_TEXT_20260616: ContinueButton 내 이어하기 골드량(TxtContinueGold/Outline)이
        //   코드로 안 채워져 프리팹 placeholder "{n}" 노출 → 비용 값으로 직접 세팅. 미할당 시 무시(기존 동작 유지).
        [SerializeField] private TMP_Text _txtContinueGold;
        [SerializeField] private TMP_Text _txtContinueGoldOutline;

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
            // ROLLBACK_ANALYTICS_NULLFILL_20260625: continue_popup_count 계측 — 팝업 표시마다 +1(내부에서 활성 play 가드).
            BalloonFlow.Analytics.AnalyticsLevelTracker.NotifyContinuePopupShown();

            UpdateCostDisplay();
            GoldPanelFxFireUtil.DisableUnderTopBarRoot(transform);

            DifficultyPurpose diff = DifficultyPurpose.Normal;
            if (LevelManager.HasInstance)
                diff = LevelManager.Instance.GetLevelDifficulty(LevelManager.Instance.CurrentLevelId);

            // [v1.2.40] PopupManager.ShowPopup 경로(PopupFail01)에서 진입 시 Show() 미호출 → 프리팹 placeholder가 노출되던 P0 버그.
            // OnEnable에서 항상 텍스트를 주입해 진입 경로와 무관하게 일관된 표시 보장.
            if (_frame != null)
            {
                _frame.ApplyDifficulty(diff);
                _frame.SetTitle("Continue?");
                _frame.SetDescription("Spend coins to keep playing.");
                _frame.SetButtonLayout(PopupCommonFrame.ButtonLayout.Horizontal);
                _frame.SetHorizGreenText("Continue");
                _frame.SetHorizRedText("Give Up");
                _frame.ShowExitButton(true);
            }
            ApplyContinueTitleBlackOutline();
            ApplyContinuePanelDifficulty(diff);
        }

        protected override void Awake()
        {
            base.Awake();
            EnsureOverlaySorting();

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
            // X(Exit) button: multiplier>=5인 경우 OnDeclineDuplicateClicked의 LoseLife→Multiplier→Lobby 2-stage 사용. multiplier==1이면 내부 fallback으로 OnDeclineClicked(=popup_fail02) 호출 — 기존 동작 보존.
            if (ExitBtn != null) ExitBtn.onClick.AddListener(OnDeclineDuplicateClicked);

            CacheStateViews();
            CacheDeclineDuplicateButton();
            if (_btnDeclineDuplicate != null)
                _btnDeclineDuplicate.onClick.AddListener(OnDeclineDuplicateClicked);

            EnsureGoldPlusButton();
            if (_btnGoldPlus != null) _btnGoldPlus.onClick.AddListener(OnGoldPlusClicked);

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
            if (winningStreak != null)
            {
                _winningStreakView = winningStreak.gameObject;
                EnsureWinningStreakMultiplierFromQuitPrefab();
            }
        }

        /// <summary>
        /// ROLLBACK_POPUP_CONTINUE_MULTIPLIER_20260605:
        /// PopupContinue prefab is binary in this branch, so keep the existing prefab untouched and clone
        /// PopupQuit/WinningStreak/Multiplier at runtime when PopupContinue is missing the same object.
        /// </summary>
        private void EnsureWinningStreakMultiplierFromQuitPrefab()
        {
            if (_winningStreakView == null) return;
            if (FindChildRecursive(_winningStreakView.transform, MULTIPLIER_NAME) != null) return;

            var quitPrefab = Resources.Load<GameObject>(POPUP_QUIT_RESOURCE_PATH);
            if (quitPrefab == null) return;

            Transform quitWinningStreak = FindChildRecursive(quitPrefab.transform, WINNINGSTREAK_NAME);
            Transform sourceMultiplier = quitWinningStreak != null
                ? FindChildRecursive(quitWinningStreak, MULTIPLIER_NAME)
                : FindChildRecursive(quitPrefab.transform, MULTIPLIER_NAME);
            if (sourceMultiplier == null) return;

            GameObject clone = Instantiate(sourceMultiplier.gameObject, _winningStreakView.transform, false);
            clone.name = MULTIPLIER_NAME;

            if (sourceMultiplier is RectTransform sourceRect && clone.transform is RectTransform cloneRect)
                CopyRectTransform(sourceRect, cloneRect);
        }

        private static void CopyRectTransform(RectTransform source, RectTransform target)
        {
            if (source == null || target == null) return;
            target.anchorMin = source.anchorMin;
            target.anchorMax = source.anchorMax;
            target.anchoredPosition = source.anchoredPosition;
            target.sizeDelta = source.sizeDelta;
            target.pivot = source.pivot;
            target.localRotation = source.localRotation;
            target.localScale = source.localScale;
            target.offsetMin = source.offsetMin;
            target.offsetMax = source.offsetMax;
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
            if (_btnGoldPlus != null) _btnGoldPlus.onClick.RemoveAllListeners();
        }

        /// <summary>
        /// PopupContinue는 Tutorial(sortingOrder=250) 위에 항상 표시되어야 함 — 사용자 요청 2026-06-04.
        /// Tutorial이 자체 Canvas.overrideSorting=true 로 PopupCanvas(=200)을 덮어쓰므로,
        /// 같은 메커니즘으로 PopupContinue 에도 Canvas+GraphicRaycaster 런타임 부착 + sortingOrder 260 부여.
        /// </summary>
        private void EnsureOverlaySorting()
        {
            _overrideCanvas = GetComponent<Canvas>();
            if (_overrideCanvas == null) _overrideCanvas = gameObject.AddComponent<Canvas>();
            _overrideCanvas.overrideSorting = true;
            _overrideCanvas.sortingOrder = OVERLAY_SORT_ORDER;
            if (GetComponent<GraphicRaycaster>() == null) gameObject.AddComponent<GraphicRaycaster>();
        }

        private void EnsureTopBarBinding()
        {
            Transform topBar = FindChildRecursive(transform, "TopBarArea");
            Transform gold = topBar != null ? FindChildRecursive(topBar, "GoldPanel") : null;
            if (gold != null) GoldPanelFxFireUtil.DisableUnderGoldPanel(gold);
            Transform txt = gold != null ? FindChildRecursive(gold, "TxtGold") : null;
            if (txt != null && txt.GetComponent<AnimatedCoinLabel>() == null)
                txt.gameObject.AddComponent<AnimatedCoinLabel>();
        }

        private void EnsureGoldPlusButton()
        {
            if (_btnGoldPlus == null)
            {
                Transform found = FindChildRecursive(transform, GOLD_PLUS_BUTTON_NAME);
                if (found != null) _btnGoldPlus = found.GetComponent<Button>();
            }
            if (_btnGoldPlus != null) return;

            // ROLLBACK_POPUP_CONTINUE_GOLDPLUS_20260619:
            // PopupContinue prefab is binary in this branch and currently has no GoldPlusBtn,
            // so clone the same top-bar button from PopupFail01 at runtime.
            Transform targetGoldPanel = FindChildRecursive(transform, "GoldPanel");
            if (targetGoldPanel == null) return;

            var failPrefab = Resources.Load<GameObject>(POPUP_FAIL01_RESOURCE_PATH);
            if (failPrefab == null) return;

            Transform source = FindChildRecursive(failPrefab.transform, GOLD_PLUS_BUTTON_NAME);
            if (source == null) return;

            GameObject clone = Instantiate(source.gameObject, targetGoldPanel, false);
            clone.name = GOLD_PLUS_BUTTON_NAME;
            if (source is RectTransform sourceRect && clone.transform is RectTransform cloneRect)
                CopyRectTransform(sourceRect, cloneRect);

            _btnGoldPlus = clone.GetComponent<Button>();
        }

        private static Transform FindChildRecursive(Transform parent, string childName)
        {
            if (parent == null) return null;
            for (int i = 0; i < parent.childCount; i++)
            {
                Transform child = parent.GetChild(i);
                if (child.name == childName) return child;
                Transform deep = FindChildRecursive(child, childName);
                if (deep != null) return deep;
            }
            return null;
        }

        private void ApplyContinueTitleBlackOutline()
        {
            // ROLLBACK_BLACK_TITLE_OUTLINE_20260619:
            // PopupContinue title is not serialized on this component in the current prefab, so
            // resolve by hierarchy name and recolor only the paired outline text at popup enable/show.
            if (!_continueTitleSearched)
            {
                _continueTitleSearched = true;

                Transform title = FindChildRecursive(transform, "TxtContinueTitle");
                if (title != null) _txtContinueTitle = title.GetComponent<TMP_Text>();

                Transform titleOutline = FindChildRecursive(transform, "TxtContinueTitleOutline");
                if (titleOutline != null) _txtContinueTitleOutline = titleOutline.GetComponent<TMP_Text>();
            }

            UIOutlineStyle.ApplyColor(_txtContinueTitleOutline, Color.black);
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
                _frame.SetDescription("Spend coins to keep playing.");
                _frame.SetButtonLayout(PopupCommonFrame.ButtonLayout.Horizontal);
                _frame.SetHorizGreenText("Continue");
                _frame.SetHorizRedText("Give Up");
                _frame.ShowExitButton(true);
            }
            ApplyContinueTitleBlackOutline();
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
            if (CurrencyManager.HasInstance) CurrencyManager.Instance.PublishCoinSync();

            int coins = CurrencyManager.HasInstance ? CurrencyManager.Instance.Coins : -1;
            if (cost > 0 && (!CurrencyManager.HasInstance || !CurrencyManager.Instance.HasEnoughCoins(cost)))
            {
                Debug.LogWarning($"[PopupContinue] Continue blocked by coins. have={coins}, need={cost}");
                Debug.Log("[PopupContinue] 골드 부족 → GoldShop 안내");
                // ROLLBACK_NOGOLD_GOLDSHOP_20260616: 골드 부족 시 PopupError(아래 팝업과 겹쳐 노출되던 문제) 대신
                //   GoldShop 을 띄우고, 닫을 때 충전됐으면 이어하기 재표시 / 미구매면 Retry(fail02). 롤백: 아래 한 줄을
                //   `var err = UIManager.Instance.OpenUI<PopupError>("Popup/PopupError"); err?.ShowPaymentFailed("Not enough coins.");` 로 환원.
                OpenGoldShopThenContinueOrRetry(cost);
                return;
            }

            bool success = ContinueHandler.Instance.Continue();
            if (success)
            {
                if (PopupManager.HasInstance) PopupManager.Instance.ClosePopup("popup_continue");
            }
            else
            {
                if (UIManager.HasInstance)
                {
                    var err = UIManager.Instance.OpenUI<PopupError>("Popup/PopupError");
                    if (err != null) err.Show("Continue Failed", "Continue could not be completed. Please try again.");
                }
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

        /// <summary>[ROLLBACK_NOGOLD_GOLDSHOP_20260616] 골드 부족 시 GoldShop 안내.
        /// 닫을 때 충전(잔액 재확인)됐으면 이어하기 팝업 재표시, 미구매면 Retry 팝업(fail02).
        /// GoldShop 미가용 시 Retry 폴백. (골드 충분한 정상 플로우는 호출되지 않음.)</summary>
        private void OnGoldPlusClicked()
        {
            if (PopupManager.HasInstance)
                PopupManager.Instance.ClosePopup("popup_continue");

            if (HUDController.HasInstance && HUDController.Instance.GoldShopPopup != null)
            {
                HUDController.Instance.GoldShopPopup.OpenWithCloseCallback(() =>
                {
                    if (PopupManager.HasInstance)
                        PopupManager.Instance.ShowPopup("popup_continue", 50);
                });
            }
        }

        private void OpenGoldShopThenContinueOrRetry(int cost)
        {
            if (PopupManager.HasInstance) PopupManager.Instance.ClosePopup("popup_continue");

            if (HUDController.HasInstance && HUDController.Instance.GoldShopPopup != null)
            {
                HUDController.Instance.GoldShopPopup.OpenWithCloseCallback(() =>
                {
                    bool nowEnough = CurrencyManager.HasInstance && CurrencyManager.Instance.HasEnoughCoins(cost);
                    if (!PopupManager.HasInstance) return;
                    if (nowEnough) PopupManager.Instance.ShowPopup("popup_continue", 50);  // 충전 → 다시 이어하기
                    else           PopupManager.Instance.ShowPopup("popup_fail02", 50);     // 미구매 → Retry
                });
            }
            else if (PopupManager.HasInstance)
            {
                PopupManager.Instance.ShowPopup("popup_fail02", 50);                        // GoldShop 미가용 폴백
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
                // ROLLBACK_POPUP_CONTINUE_MULTIPLIER_X5_20260615: x5 미만이면 Multiplier 경고 단계 skip → 기존 종료 플로우.
                //   (기존: multiplier <= 1. 변경: x5 이상만 LoseLife→Multiplier→Lobby 노출.)
                if (multiplier < MULTIPLIER_VIEW_MIN_MULTIPLIER)
                {
                    // x5 미만 → WinningStreak skip, 기존 종료 플로우 (Give Up = 팝업 닫고 fail02)
                    OnDeclineClicked();
                    return;
                }

                if (_loseLifeView != null) _loseLifeView.SetActive(false);
                if (_winningStreakView != null)
                {
                    _winningStreakView.SetActive(true);
                    EnsureWinningStreakMultiplierFromQuitPrefab();
                    // PopupQuit과 동일 — 노출 즉시 애니메이터 루프 진입
                    WinningStreakUI.PlayMultiplierAnimationForPopupQuit(_winningStreakView, multiplier);
                }
                // [2026-06-11] 하드코딩 영어 → TextData 키 + {n} 치환 (placeholder 키는 반드시 Format 소비 룰).
                if (_frame != null)
                    _frame.SetDescription(LocalizationService.GetWith("popupcontinue.txtdescription.multiplier", "n", multiplier));
                _currentView = ContinueView.WinningStreak;
                return;
            }

            if (PopupManager.HasInstance) PopupManager.Instance.ClosePopup("popup_continue");
            if (GameManager.HasInstance)
            {
                GameManager.Instance.ResumeGame();
                if (GameManager.IsTestPlayMode)
                {
                    GameManager.Instance.GoToMapMaker();
                }
                else
                {
                    // [WS quit-fail 2026-06-10] WS 경고 2단계에서 포기(미클리어 로비 이동) = 실패 — streak 리셋 + 로비 드롭 연출 예약.
                    if (WinningStreakManager.HasInstance) WinningStreakManager.Instance.OnLevelAbandoned();
                    GameManager.Instance.GoToLobby();
                }
            }
        }

        private void UpdateCostDisplay()
        {
            if (!ContinueHandler.HasInstance) return;
            int cost = ContinueHandler.Instance.GetContinueCost();
            // [v1.2.40] 'FREE' 문구 제거 — 항상 동적 가격(코인)을 표기. cost<=0이면 빈 문자열.
            string costStr = cost > 0 ? cost.ToString("N0") : string.Empty;
            if (_costText != null) _costText.text = costStr;
            // ROLLBACK_CONTINUE_GOLD_COST_TEXT_20260616: ContinueButton 골드량 텍스트(+아웃라인)도 비용으로 채움.
            if (_txtContinueGold != null) _txtContinueGold.text = costStr;
            if (_txtContinueGoldOutline != null) _txtContinueGoldOutline.text = costStr;
        }

        /// <summary>PopupQuit과 동일한 Multiplier reset 시퀀스. 닫힐 때 animator를 MultiplierDefault로 되돌려야 다음 노출 시 깨끗하게 시작됨.</summary>
        public override void CloseUI()
        {
            if (_winningStreakView != null && _winningStreakView.activeInHierarchy)
                WinningStreakUI.ResetPopupQuitMultiplierAnimation(_winningStreakView);
            base.CloseUI();
        }
    }
}
