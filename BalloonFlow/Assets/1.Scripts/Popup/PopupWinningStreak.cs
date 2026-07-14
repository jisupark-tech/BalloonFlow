using System.Collections.Generic;
using DG.Tweening;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BalloonFlow
{
    /// <summary>
    /// WinningStreak 이벤트 메인 팝업. 25 stage 보상 리스트를 virtual scroll 로 표시.
    /// 데이터: WinningStreakConfigService.Config + UserData.winningStreak (WinningStreakManager 경유).
    /// 슬롯 BtnReward 클릭 → 달성/미수령 stage 면 ClaimStage 호출 + WinningStreakClickInfo 툴팁 표시 (slot 기준 자동 플립 + clamp).
    /// Claim 과 툴팁 동작은 공존 — 클릭 한 번으로 보상 수령(가능 시)과 보상 내용 확인이 동시에 일어남.
    /// </summary>
    public class PopupWinningStreak : UIBase
    {
        private const int FallbackDataCount = 25;
        private const int VisibleSlotCount = 5;
        private const int ExtraPoolSlots = 2;
        private const int FallbackPoolSlots = 8;
        private const float ScrollElasticity = 0.18f;
        private const float ScrollDecelerationRate = 0.12f;
        private const string SlotPrefabResource = "UI/UIAssets/SlotWinningStreak";
        private const float SlotFixedWidth = 900f;
        private const float SlotFixedHeight = 300f;
        private const string TooltipPrefabResource = "UI/UIAssets/WinningStreakClickInfo";
        private const string EXIT_DUP_NAME = "ExitButton (1)";
        private const float TooltipViewportFlipThreshold = 0.5f;
        private const float TooltipPopDuration = 0.22f;

        [Header("[Common Frame]")]
        [SerializeField] private PopupCommonFrame _frame;
        [SerializeField] private Button _btnInfo;
        private Button _btnExitDuplicate;
        private bool _exitDuplicateSearched;

        [Header("[Key Blaze Slots]")]
        [SerializeField] private RectTransform _keyBlazeContents;
        [SerializeField] private GameObject _slotKeyBlazePrefab;
        [SerializeField] private ScrollRect _scrollRect;

        [Header("[Header — 현재 streak/진행 표시]")]
        [Tooltip("ImageMultiplier 루트 GameObject — 자식으로 SlotMultiplier(0..4) 5개를 가진 컨테이너. 각 SlotMultiplier 안의 TextMultiplier 에 streak1..streak5+ 배수 텍스트를 Firestore config 에서 채움.")]
        [SerializeField] private Transform _multiplierSlotsRoot;
        [Tooltip("진행 상태 텍스트 (inner). TxtDescriptionOutline 의 자식 TxtDescription.")]
        [SerializeField] private TMP_Text _txtDescription;
        [Tooltip("진행 상태 텍스트 (outline). 부모 TxtDescriptionOutline. _txtDescription 과 같은 내용으로 동기 갱신.")]
        [SerializeField] private TMP_Text _txtDescriptionOutline;

        private readonly List<TMP_Text> _multiplierTexts = new List<TMP_Text>(5);
        private bool _multiplierTextsResolved;

        // SlotWinningStreak 상태 스프라이트 캐시 — atlas_ui 가 늦게 로드될 수 있어 lazy fetch.
        private Sprite _sprFrameNumberDefault;
        private Sprite _sprFrameNumberComplete;
        private Sprite _sprArrow;
        private Sprite _sprArrowComplete;
        private Sprite _sprSlot;
        private Sprite _sprSlotComplete;

        // TMP font materials — Resources/TextMesh Pro/Fonts & Materials/.
        private Material _fontMatGreenOutline;
        private Material _fontMatPurpleOutline;

        private readonly List<PooledSlot> _pooledSlots = new List<PooledSlot>(FallbackPoolSlots);
        private bool _slotsBuilt;
        private bool _scrollListenerBound;
        private bool _suppressScrollCallback;
        private bool _eventsSubscribed;
        // BtnSingle(PLAY) 1회 가드 — Awake 등록, OpenUI 리셋, OnSingleFrameClicked 진입 시 set.
        private bool _singleClickHandled;
        private float _slotHeightY;
        private float _slotSpacingY;
        private float _slotStrideY;
        private float _contentTopPadding;
        private float _contentBottomPadding;

        // BtnReward 클릭 시 표시되는 WinningStreakClickInfo 툴팁 — popup root 자식으로 lazy instantiate.
        // _keyBlazeContents 자식으로 두면 ScrollRect Mask 에 잘리므로 절대 사용 금지.
        private GameObject _tooltipInstance;
        private RectTransform _tooltipRect;
        private GameObject _tooltipArrowTop;
        private GameObject _tooltipArrowBottom;
        private int _activeTooltipStage = -1;
        private Tween _tooltipPopTween;

        private int DataCount
        {
            get
            {
                if (WinningStreakManager.HasInstance)
                {
                    int n = WinningStreakManager.Instance.TotalStageCount;
                    if (n > 0) return n;
                }
                return FallbackDataCount;
            }
        }

        protected override void Awake()
        {
            base.Awake();

            ResolveReferences();

            BindExitButtons();

            if (_frame != null && _frame.BtnSingle != null)
            {
                _frame.BtnSingle.onClick.RemoveAllListeners();
                _frame.BtnSingle.onClick.AddListener(OnSingleFrameClicked);
            }

            if (_btnInfo != null)
            {
                _btnInfo.onClick.RemoveAllListeners();
                _btnInfo.onClick.AddListener(() =>
                {
                    if (UIManager.HasInstance)
                        UIManager.Instance.OpenUI<PopupWinningStreakInfo>(Const.POPUP_WINNING_STREAK_INFO);
                });
            }

            BindScrollListener();
        }

        protected override void OnDestroy()
        {
            base.OnDestroy();

            if (_frame != null && _frame.BtnExit != null)
                _frame.BtnExit.onClick.RemoveAllListeners();
            if (_btnExitDuplicate != null)
                _btnExitDuplicate.onClick.RemoveAllListeners();
            if (_frame != null && _frame.BtnSingle != null)
                _frame.BtnSingle.onClick.RemoveAllListeners();
            if (_btnInfo != null)
                _btnInfo.onClick.RemoveAllListeners();
            if (_scrollRect != null && _scrollListenerBound)
                _scrollRect.onValueChanged.RemoveListener(HandleScrollValueChanged);

            UnsubscribeStateEvents();

            for (int i = 0; i < _pooledSlots.Count; i++)
            {
                var p = _pooledSlots[i];
                if (p?.button != null)
                    p.button.onClick.RemoveAllListeners();
            }

            _pooledSlots.Clear();

            _tooltipPopTween?.Kill();
            _tooltipPopTween = null;

            if (_tooltipInstance != null)
            {
                Destroy(_tooltipInstance);
                _tooltipInstance = null;
                _tooltipRect = null;
                _tooltipArrowTop = null;
                _tooltipArrowBottom = null;
            }
            _activeTooltipStage = -1;
        }

        private void CacheExitDuplicateButton()
        {
            if (_exitDuplicateSearched) return;
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

            if (found != null)
                _btnExitDuplicate = ResolveButtonOnExitTransform(found);
        }

        private static Button ResolveButtonOnExitTransform(Transform root)
        {
            if (root == null) return null;
            Button button = root.GetComponent<Button>();
            if (button != null) return button;

            // ROLLBACK_WINNING_STREAK_EXIT_REBIND_20260610:
            // ExitButton (1) can be a visual wrapper with the actual Button on a child.
            // If only the wrapper is found, it blocks the normal exit button but never closes.
            return root.GetComponentInChildren<Button>(true);
        }

        private void BindExitButtons()
        {
            ResolveReferences();

            if (_frame != null && _frame.BtnExit != null)
            {
                _frame.BtnExit.onClick.RemoveListener(CloseUI);
                _frame.BtnExit.onClick.AddListener(CloseUI);
                _frame.BtnExit.interactable = true;
            }

            CacheExitDuplicateButton();
            if (_btnExitDuplicate != null)
            {
                _btnExitDuplicate.onClick.RemoveListener(CloseUI);
                _btnExitDuplicate.onClick.AddListener(CloseUI);
                _btnExitDuplicate.interactable = true;
            }
        }

        public override void OpenUI()
        {
            ResolveReferences();
            BindExitButtons();
            BindScrollListener();
            SubscribeStateEvents();

            if (_frame != null)
            {
                // [WS_TITLE_PURPLE_OUTLINE_20260615] TxtTitleOutline Material Preset 을 Blue → Poppins-Bold-PurpleOutline 으로 강제. 프리팹 바이너리 직렬화 이슈로 런타임 오버라이드.
                EnsureStreakSprites();
                if (_fontMatPurpleOutline != null) _frame.OverrideTitleOutlineAllDifficultyMaterials(_fontMatPurpleOutline);
                // ROLLBACK_WINNING_STREAK_TITLE_USE_CSV_20260607: 이전 = SetTitle("Winning Streak"). 방침: CSV 영문 사용.
                _frame.SetTitle(LocalizationService.Get("popupwinningstreak.txttitle"));
                _frame.ShowExitButton(true);
                _frame.SetButtonLayout(PopupCommonFrame.ButtonLayout.Single);
                // ROLLBACK_WINNING_STREAK_PLAYBTN_USE_CSV_20260607: 이전 = SetSingleButtonText("PLAY").
                _frame.SetSingleButtonText(LocalizationService.Get("popupwinningstreak.txtbtnsingle"));
                if (_frame.BtnSingle != null) _frame.BtnSingle.interactable = true;
                _singleClickHandled = false;
            }

            base.OpenUI();
            BuildVirtualSlots();
            ResetScrollPosition();
            RefreshHeader();

            _timerTick = 0f;
            UpdateRoundTimer();   // 열자마자 회차 카운트다운 즉시 표시 + 경계 체크
        }

        // ── ROLLBACK_WS_INTRO_SCROLL_THEN_INFO_20260619 ─────────────────────────────
        //   최초 해금 인트로: item1(bottom, vnp=0) → item25(top, vnp=1) 자동 스크롤(2.5s, 입력잠금) 후
        //   PopupWinningStreakInfo 표시. Info 닫으면 콜백으로 이 팝업도 닫아 로비 복귀.
        private const float IntroScrollDuration = 2.5f;
        private bool _introPlaying;

        public void PlayIntroScrollThenInfo()
        {
            if (_introPlaying) return;
            _introPlaying = true;
            StartCoroutine(IntroScrollThenInfoRoutine());
        }

        // ROLLBACK_WS_SEASON_START_SCROLL_ONLY_20260626:
        // Later Winning Streak seasons reuse the reward-scroll reveal without showing the how-to-play info popup again.
        public void PlaySeasonStartScrollOnly()
        {
            if (_introPlaying) return;
            _introPlaying = true;
            StartCoroutine(SeasonStartScrollOnlyRoutine());
        }

        private System.Collections.IEnumerator IntroScrollThenInfoRoutine()
        {
            yield return ScrollRewardsToGrandPrizeRoutine();

            // First unlock flow: show the how-to-play info after the reward-scroll reveal.
            // WS_INFO_CLOSE_ONLY_20260628: 인포(how-to-play)를 터치로 닫아도 메인 Winning Streak 팝업은 유지한다.
            //   이전엔 SetCloseCallback(CloseUI) 로 인포 닫힘이 메인 팝업까지 닫아 로비로 복귀시켰음(터치 1번에 둘 다 닫힘).
            //   이제 인포만 닫히고, 메인 팝업은 자체 OK/X 버튼으로 닫는다. 인포 오픈 실패 시에만 폴백으로 메인 닫음.
            if (UIManager.HasInstance)
            {
                var info = UIManager.Instance.OpenUI<PopupWinningStreakInfo>(Const.POPUP_WINNING_STREAK_INFO);
                if (info == null) CloseUI();
            }
            else CloseUI();
        }

        private System.Collections.IEnumerator SeasonStartScrollOnlyRoutine()
        {
            yield return ScrollRewardsToGrandPrizeRoutine();
        }

        private System.Collections.IEnumerator ScrollRewardsToGrandPrizeRoutine()
        {
            SetIntroInputLocked(true);

            if (_scrollRect != null && _slotsBuilt)
            {
                _suppressScrollCallback = false;                 // 스크롤 중 슬롯 재활용 유지
                _scrollRect.StopMovement();
                _scrollRect.velocity = Vector2.zero;
                _scrollRect.verticalNormalizedPosition = 0f;     // item1(bottom) 시작
                Canvas.ForceUpdateCanvases();
                RefreshVisibleSlots();
                yield return null;

                float t = 0f;
                while (t < IntroScrollDuration)
                {
                    t += Time.unscaledDeltaTime;
                    float v = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t / IntroScrollDuration));
                    _scrollRect.verticalNormalizedPosition = v;  // → item25(top). onValueChanged 가 RefreshVisibleSlots 호출
                    _scrollRect.velocity = Vector2.zero;
                    yield return null;
                }
                _scrollRect.verticalNormalizedPosition = 1f;
                _scrollRect.velocity = Vector2.zero;
                RefreshVisibleSlots();
            }

            SetIntroInputLocked(false);
            _introPlaying = false;
        }

        // 인트로 자동스크롤 중 사용자 입력 차단 — 버튼 비활성 + 스크롤 드래그 차단(프로그램적 vnp 설정은 계속 동작).
        private void SetIntroInputLocked(bool locked)
        {
            bool interactable = !locked;
            if (_frame != null && _frame.BtnSingle != null) _frame.BtnSingle.interactable = interactable;
            if (_btnInfo != null) _btnInfo.interactable = interactable;
            if (_frame != null && _frame.BtnExit != null) _frame.BtnExit.interactable = interactable;
            if (_scrollRect != null) _scrollRect.vertical = interactable;
        }

        // ── 회차 카운트다운 (클라 UTC 스케줄) ─────────────────────
        private float _timerTick;

        private void Update()
        {
#if UNITY_EDITOR
            // [에디터 전용] 1~5 키 → Multiplier 등장 연출 + 배수 위치 프리뷰 (x1/x5/x10/x25/x100). 실제 state 변경 없음.
            // (z/x 로비 프리뷰와 별개 — 이 팝업의 배수 연출 검증용은 숫자 키 사용. 타이머 틱 early-return 보다 먼저 체크.)
            var kb = UnityEngine.InputSystem.Keyboard.current;
            if (kb != null)
            {
                if (kb.digit1Key.wasPressedThisFrame) PreviewMultiplierFx(1);
                if (kb.digit2Key.wasPressedThisFrame) PreviewMultiplierFx(5);
                if (kb.digit3Key.wasPressedThisFrame) PreviewMultiplierFx(10);
                if (kb.digit4Key.wasPressedThisFrame) PreviewMultiplierFx(25);
                if (kb.digit5Key.wasPressedThisFrame) PreviewMultiplierFx(100);
            }
#endif
            _timerTick += Time.unscaledDeltaTime;
            if (_timerTick < 1f) return;   // 시:분 단위 표시라 1초 주기면 충분
            _timerTick = 0f;
            UpdateRoundTimer();
        }

#if UNITY_EDITOR
        /// <summary>[에디터 전용] 지정 배수로 SelectFrame/TextYellow 동시 이동 재생 (root 슬라이드 없음).</summary>
        private void PreviewMultiplierFx(int multiplier)
        {
            var multiplierGo = FindChildGOByName(gameObject, "Multiplier");
            if (multiplierGo == null) return;
            WinningStreakUI.PlayMultiplierSelect(multiplierGo.transform, multiplier);
        }
#endif

        /// <summary>회차 남은시간을 "Xd YYh" 로 Timer 에 표시. 경계 통과(남은시간 0) 시 CheckRoundBoundary 가 리셋.</summary>
        private void UpdateRoundTimer()
        {
            if (_frame == null) return;
            if (!WinningStreakManager.HasInstance) { _frame.ShowTimer(false); return; }
            var mgr = WinningStreakManager.Instance;

            mgr.CheckRoundBoundary();   // 경계 넘었으면 리셋 + OnStateChanged → 헤더/슬롯 갱신

            var remaining = mgr.RoundRemaining;
            int days = (int)remaining.TotalDays;
            int hours = remaining.Hours;
            _frame.ShowTimer(true);
            // ROLLBACK_WS_TIME_LOCALIZE_20260714: CSV uilobby.texttimer ("{}d {}h" / "{}일 {}시간") 사용 — 일+시간.
            _frame.SetTimerText(LocalizationService.GetFilled("uilobby.texttimer", days, hours));
        }

        // BtnSingle(PLAY) — LobbyController.OnPlayClicked 패턴 모사. 라이프 부족 시 PopupMoreLive 분기.
        private void OnSingleFrameClicked()
        {
            if (_singleClickHandled) return;
            _singleClickHandled = true;
            if (_frame != null && _frame.BtnSingle != null)
                _frame.BtnSingle.interactable = false;

            if (!GameManager.HasInstance) return;

            // 라이프 부족 → PopupMoreLive. WinningStreak 팝업은 닫지 않음 — MoreLive 흐름 우선.
            if (LifeManager.HasInstance && !LifeManager.Instance.IsInfiniteHeartsActive && !LifeManager.Instance.HasLife())
            {
                if (UIManager.HasInstance)
                    UIManager.Instance.OpenUI<PopupMoreLive>("Popup/PopupMoreLive");
                // 라이프 회복 후 재시도 가능하도록 가드 해제.
                _singleClickHandled = false;
                if (_frame != null && _frame.BtnSingle != null)
                    _frame.BtnSingle.interactable = true;
                return;
            }

            int levelId = 1;
            if (LevelManager.HasInstance)
            {
                int highest = LevelManager.Instance.GetHighestCompletedLevel();
                levelId = highest > 0 ? highest + 1 : 1;
            }

            CloseUI();
            GameManager.Instance.StartLevel(levelId);
        }

        // ── State 이벤트 ─────────────────────────────────────────

        private void SubscribeStateEvents()
        {
            if (_eventsSubscribed) return;
            if (WinningStreakManager.HasInstance)
                WinningStreakManager.Instance.OnStateChanged += HandleStateChanged;
            if (WinningStreakConfigService.HasInstance)
                WinningStreakConfigService.Instance.OnConfigLoaded += HandleStateChanged;
            _eventsSubscribed = true;
        }

        private void UnsubscribeStateEvents()
        {
            if (!_eventsSubscribed) return;
            if (WinningStreakManager.HasInstance)
                WinningStreakManager.Instance.OnStateChanged -= HandleStateChanged;
            if (WinningStreakConfigService.HasInstance)
                WinningStreakConfigService.Instance.OnConfigLoaded -= HandleStateChanged;
            _eventsSubscribed = false;
        }

        private void HandleStateChanged()
        {
            if (!gameObject.activeInHierarchy) return;
            // config 가 늦게 도착하면 content height 재계산 필요할 수 있음.
            SetVirtualContentHeight();
            RefreshVisibleSlots();
            RefreshHeader();
        }

        // ── Header (multiplier 슬롯 + 진행 텍스트) ───────────────

        private void RefreshHeader()
        {
            var mgr = WinningStreakManager.HasInstance ? WinningStreakManager.Instance : null;
            var state = mgr?.State;
            var cfg = mgr?.Config;

            RefreshMultiplierSlots(cfg);
            RefreshMultiplierSelection();
            RefreshDescriptionText(state, cfg);
        }

        /// <summary>"Multiplier" GameObject — SelectFrame / TextYellow 만 현재 배수 위치로 동시 이동 (코드 트윈, Animator 미사용).
        /// 이 팝업에선 Multiplier root 슬라이드 불필요 (root 슬라이드는 UILobby WS 쪽만). Mask 위치 고정.
        /// x1: SF -338 / TY 358, x5: -150/170, x10: 30/-10, x25: 210/-190, x100: 390/-370.</summary>
        private void RefreshMultiplierSelection()
        {
            var multiplierGo = FindChildGOByName(gameObject, "Multiplier");
            if (multiplierGo == null) return;
            int multiplier = WinningStreakUI.ResolveCurrentMultiplier();
            WinningStreakUI.PlayMultiplierSelect(multiplierGo.transform, multiplier);
        }

        /// <summary>ImageMultiplier 아래의 SlotMultiplier(0..4) 5개에 streak1..streak5+ 배수 텍스트 채움.
        /// Firestore config 도착 전엔 디자이너가 prefab 에 박아둔 placeholder 텍스트가 유지됨.</summary>
        // prefab 의 배수 라벨 이름(좌→우 = x1..x100). 자식 sibling 순서에 의존하면 첫칸이 x10 으로 나오는
        // 버그가 있어, 이름으로 직접 매핑해 순서 비의존으로 값을 채운다.
        private static readonly string[] MultiplierSlotNames =
            { "TextMultiplierx1", "TextMultiplierx5", "TextMultiplierx10", "TextMultiplierx25", "TextMultiplierx100" };

        private void RefreshMultiplierSlots(WinningStreakConfigDoc cfg)
        {
            if (cfg == null || cfg.streakMultipliers == null) return;

            GameObject root = _multiplierSlotsRoot != null ? _multiplierSlotsRoot.gameObject : gameObject;
            var m = cfg.streakMultipliers;
            int[] values = { m.streak1, m.streak2, m.streak3, m.streak4, m.streak5Plus };

            for (int i = 0; i < MultiplierSlotNames.Length && i < values.Length; i++)
            {
                var tmp = FindChildByName<TMP_Text>(root, MultiplierSlotNames[i]);
                if (tmp != null) tmp.text = $"x{values[i]}";
            }
        }

        /// <summary>_multiplierSlotsRoot 아래의 자식 SlotMultiplier 들에서 TextMultiplier(TMP_Text) 를 한 번만 수집.
        /// 자식 순서를 그대로 사용 (SlotMultiplier, SlotMultiplier (1), ... 형태).</summary>
        private void ResolveMultiplierTexts()
        {
            if (_multiplierTextsResolved) return;
            _multiplierTextsResolved = true;
            _multiplierTexts.Clear();
            if (_multiplierSlotsRoot == null) return;

            for (int i = 0; i < _multiplierSlotsRoot.childCount; i++)
            {
                var slot = _multiplierSlotsRoot.GetChild(i);
                if (slot == null) continue;
                var tmp = FindChildByName<TMP_Text>(slot.gameObject, "TextMultiplier");
                if (tmp == null)
                {
                    // 자식 어디든 TMP_Text 가 하나 있으면 그것 사용 (이름 변경 내성).
                    tmp = slot.GetComponentInChildren<TMP_Text>(true);
                }
                _multiplierTexts.Add(tmp);
            }
        }

        /// <summary>TxtDescription (inner) + TxtDescriptionOutline (outer) 를 CSV(TextData) 영문으로 세팅.
        /// ROLLBACK_WINNING_STREAK_DESC_USE_CSV_20260607: 이전엔 한국어 동적("{n}연승 x{m} / 다음까지 {need}") +
        /// eventFinished "All rewards completed!" 로 덮어써 CSV 영문(박지수 spec 7-4 "more rewards!")이 안 보였음.
        /// 방침("모든 text 는 TextData CSV 영문") 적용 → CSV 정적 영문 사용. 되돌리려면 동적 분기 로직으로 복원.</summary>
        private void RefreshDescriptionText(WinningStreakState state, WinningStreakConfigDoc cfg)
        {
            if (_txtDescription != null)
                _txtDescription.text = LocalizationService.Get("popupwinningstreak.txtdescription");
            if (_txtDescriptionOutline != null)
                _txtDescriptionOutline.text = LocalizationService.Get("popupwinningstreak.txtdescriptionoutline");
        }

        // ── Slot pool / virtual scroll ───────────────────────────

        private void BuildVirtualSlots()
        {
            ResolveReferences();
            Canvas.ForceUpdateCanvases();

            if (_keyBlazeContents == null)
            {
                Debug.LogWarning("[PopupWinningStreak] Content is not assigned.");
                return;
            }

            GameObject slotPrefab = ResolveSlotPrefab();
            if (slotPrefab == null)
            {
                Debug.LogWarning("[PopupWinningStreak] SlotWinningStreak prefab was not found.");
                return;
            }

            CacheSlotMetrics(slotPrefab);
            DisableContentLayoutControllers();
            SetVirtualContentHeight();

            if (_slotsBuilt)
            {
                EnsurePoolSize(slotPrefab);
                ApplyPoolSlotLayout();
                RefreshVisibleSlots();
                return;
            }

            ClearSlotContent(slotPrefab);
            _pooledSlots.Clear();

            int poolCount = CalculatePoolCount();
            for (int i = 0; i < poolCount; i++)
                CreatePooledSlot(slotPrefab, i);

            _slotsBuilt = true;
            ApplyPoolSlotLayout();
            RefreshVisibleSlots();
        }

        private void ResolveReferences()
        {
            if (_frame == null)
                _frame = GetComponentInChildren<PopupCommonFrame>(true);

            if (_scrollRect == null)
                _scrollRect = GetComponentInChildren<ScrollRect>(true);

            if (_keyBlazeContents == null && _scrollRect != null)
                _keyBlazeContents = _scrollRect.content;

            if (_scrollRect == null && _keyBlazeContents != null)
                _scrollRect = _keyBlazeContents.GetComponentInParent<ScrollRect>(true);

            ConfigureScrollRect();
            ConfigureContentTransform();
        }

        private void ConfigureScrollRect()
        {
            if (_scrollRect == null) return;

            _scrollRect.vertical = true;
            _scrollRect.horizontal = false;
            _scrollRect.movementType = ScrollRect.MovementType.Elastic;
            _scrollRect.elasticity = ScrollElasticity;
            _scrollRect.inertia = true;
            _scrollRect.decelerationRate = ScrollDecelerationRate;

            if (_scrollRect.viewport == null && _keyBlazeContents != null)
                _scrollRect.viewport = _keyBlazeContents.parent as RectTransform;

            RectTransform viewport = _scrollRect.viewport;
            if (viewport == null) return;

            if (viewport.GetComponent<RectMask2D>() == null)
                viewport.gameObject.AddComponent<RectMask2D>();
        }

        private void ConfigureContentTransform()
        {
            if (_keyBlazeContents == null) return;

            _keyBlazeContents.anchorMin = new Vector2(0f, 1f);
            _keyBlazeContents.anchorMax = new Vector2(1f, 1f);
            _keyBlazeContents.pivot = new Vector2(0.5f, 1f);

            if (_scrollRect != null) _scrollRect.content = _keyBlazeContents;
        }

        private GameObject ResolveSlotPrefab()
        {
            if (_slotKeyBlazePrefab != null) return _slotKeyBlazePrefab;
            _slotKeyBlazePrefab = Resources.Load<GameObject>(SlotPrefabResource);
            if (_slotKeyBlazePrefab != null) return _slotKeyBlazePrefab;

            if (_keyBlazeContents != null && _keyBlazeContents.childCount > 0)
            {
                _slotKeyBlazePrefab = _keyBlazeContents.GetChild(0).gameObject;
                _slotKeyBlazePrefab.SetActive(false);
                return _slotKeyBlazePrefab;
            }
            return null;
        }

        private void CacheSlotMetrics(GameObject slotPrefab)
        {
            VerticalLayoutGroup layoutGroup = _keyBlazeContents != null ? _keyBlazeContents.GetComponent<VerticalLayoutGroup>() : null;
            _slotSpacingY = layoutGroup != null ? layoutGroup.spacing : 0f;
            _contentTopPadding = layoutGroup != null ? layoutGroup.padding.top : 0f;
            _contentBottomPadding = layoutGroup != null ? layoutGroup.padding.bottom : 0f;

            RectTransform slotRt = slotPrefab != null ? slotPrefab.GetComponent<RectTransform>() : null;
            _slotHeightY = CalculateSlotHeightForFiveVisible(slotRt);
            _slotStrideY = Mathf.Max(1f, _slotHeightY + _slotSpacingY);
        }

        private float CalculateSlotHeightForFiveVisible(RectTransform slotRt)
        {
            return SlotFixedHeight;
        }

        private void DisableContentLayoutControllers()
        {
            if (_keyBlazeContents == null) return;

            // 안전망 — prefab 에 VerticalLayoutGroup 누락 시 런타임에 부착.
            if (_keyBlazeContents.GetComponent<VerticalLayoutGroup>() == null)
                _keyBlazeContents.gameObject.AddComponent<VerticalLayoutGroup>();

            // VerticalLayoutGroup 은 런타임에 활성 상태로 유지 (slot 가시 정렬을 위해 필요).
            // 나머지 LayoutGroup 종류는 가상 스크롤 좌표 계산과 충돌하므로 비활성화.
            LayoutGroup[] layoutGroups = _keyBlazeContents.GetComponents<LayoutGroup>();
            for (int i = 0; i < layoutGroups.Length; i++)
            {
                if (layoutGroups[i] is VerticalLayoutGroup)
                    layoutGroups[i].enabled = true;
                else
                    layoutGroups[i].enabled = false;
            }

            ContentSizeFitter fitter = _keyBlazeContents.GetComponent<ContentSizeFitter>();
            if (fitter != null) fitter.enabled = false;
        }

        private void SetVirtualContentHeight()
        {
            if (_keyBlazeContents == null || _slotStrideY <= 1f) return;

            int n = DataCount;
            float contentHeight = _contentTopPadding + _contentBottomPadding;
            contentHeight += _slotHeightY * n;
            contentHeight += _slotSpacingY * Mathf.Max(0, n - 1);

            if (_scrollRect != null && _scrollRect.viewport != null)
                contentHeight = Mathf.Max(contentHeight, _scrollRect.viewport.rect.height + 1f);

            _keyBlazeContents.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, contentHeight);
        }

        private int CalculatePoolCount()
        {
            if (_scrollRect == null || _scrollRect.viewport == null || _slotStrideY <= 1f)
                return Mathf.Min(DataCount, FallbackPoolSlots);

            int poolCount = Mathf.Max(1, VisibleSlotCount + ExtraPoolSlots);
            return Mathf.Min(DataCount, poolCount);
        }

        private void EnsurePoolSize(GameObject slotPrefab)
        {
            int targetPoolCount = CalculatePoolCount();
            for (int i = _pooledSlots.Count; i < targetPoolCount; i++)
                CreatePooledSlot(slotPrefab, i);
        }

        private void CreatePooledSlot(GameObject slotPrefab, int poolIndex)
        {
            GameObject slot = Instantiate(slotPrefab, _keyBlazeContents);
            slot.name = $"SlotWinningStreak_Pool_{poolIndex:D2}";
            slot.SetActive(true);

            RectTransform slotRt = slot.GetComponent<RectTransform>();
            if (slotRt == null) return;
            ApplySlotLayout(slotRt);

            var pooled = new PooledSlot { root = slotRt };
            BindSlotChildren(pooled, slot);
            _pooledSlots.Add(pooled);

            if (pooled.button != null)
            {
                int captureIndex = _pooledSlots.Count - 1;
                pooled.button.onClick.RemoveAllListeners();
                pooled.button.onClick.AddListener(() => HandleSlotClick(captureIndex));
            }
        }

        private static T FindChildByName<T>(GameObject root, string name, bool includeInactive = true) where T : Component
        {
            if (root == null || string.IsNullOrEmpty(name)) return null;
            var arr = root.GetComponentsInChildren<T>(includeInactive);
            for (int i = 0; i < arr.Length; i++)
                if (arr[i] != null && arr[i].name == name) return arr[i];
            return null;
        }

        private static GameObject FindChildGOByName(GameObject root, string name, bool includeInactive = true)
        {
            if (root == null || string.IsNullOrEmpty(name)) return null;
            var arr = root.GetComponentsInChildren<Transform>(includeInactive);
            for (int i = 0; i < arr.Length; i++)
                if (arr[i] != null && arr[i].name == name) return arr[i].gameObject;
            return null;
        }

        private void BindSlotChildren(PooledSlot pooled, GameObject slot)
        {
            // Number frame
            pooled.imageDefault = FindChildGOByName(slot, "ImageDefault");
            pooled.imageGet = FindChildGOByName(slot, "ImageGet");
            pooled.textNumber = FindChildByName<TMP_Text>(slot, "TextNumber");
            pooled.textNumberOutline = FindChildByName<TMP_Text>(slot, "TextNumberOutline");

            // BtnReward + inner state icons
            var btnRewardGo = FindChildGOByName(slot, "BtnReward");
            pooled.button = btnRewardGo != null ? btnRewardGo.GetComponent<Button>() : null;
            if (pooled.button == null)
            {
                // 안전망 — 첫 Button 사용.
                var anyBtn = slot.GetComponentInChildren<Button>(true);
                pooled.button = anyBtn;
            }
            pooled.btnRewardImage = btnRewardGo != null ? btnRewardGo.GetComponent<Image>() : null;

            // 구분선 — 첫 슬롯은 lineBottom, 마지막 슬롯은 lineTop 만 비활성화.
            pooled.lineTop = FindChildGOByName(slot, "LineTop");
            pooled.lineBottom = FindChildGOByName(slot, "LineBottom");

            pooled.frameInner = FindChildGOByName(slot, "FrameInner")?.transform as RectTransform;
            pooled.iconCheck = FindChildGOByName(slot, "IconCheck");
            pooled.iconLock = FindChildGOByName(slot, "IconLock");
            pooled.grandPrize = FindChildGOByName(slot, "GrandPrize");

            // 상태 스프라이트 타겟 — 사용자 스펙대로 RotateLight / ImageInnerFrame / ImageArrow.
            pooled.rotateLight = FindChildGOByName(slot, "RotateLight");
            pooled.imageInnerFrame = FindChildByName<Image>(slot, "ImageInnerFrame");
            pooled.imageArrow = FindChildByName<Image>(slot, "ImageArrow");
            // RewardItem 템플릿 — 구조 변경 내성: 슬롯 전체에서 "RewardItem" 을 직접 탐색.
            //   실제 프리팹 구조는 BtnReward > RewardImg > RewardItem 이라 이전 'FrameInner' 가정이 깨져
            //   frameInner=null → 보상 바인딩 전체가 no-op(이미지 미적용) 였다. 부모(RewardImg)를 컨테이너로 사용.
            GameObject rewardImgGo = FindChildGOByName(slot, "RewardImg");
            GameObject rewardItemGo = null;
            {
                var trs = slot.GetComponentsInChildren<Transform>(true);
                for (int i = 0; i < trs.Length; i++)
                    if (trs[i] != null && trs[i].name.StartsWith("RewardItem")) { rewardItemGo = trs[i].gameObject; break; }
            }
            // ROLLBACK_WINNING_STREAK_REWARDIMG_VARIANT_RULE:
            // Template used to be the inner RewardItem child and multiple reward items were cloned
            // for composite rewards. New spec treats RewardImg as one variant container:
            // RewardGold=gold only, RewardItem=single item/heart, ImageGift=composite rewards.
            pooled.rewardItemTemplate = rewardImgGo != null ? rewardImgGo : rewardItemGo;
            RectTransform rewardParent = pooled.rewardItemTemplate != null ? pooled.rewardItemTemplate.transform.parent as RectTransform : pooled.frameInner;
            pooled.rewardItemRoot = rewardParent;
            pooled.frameInner = rewardParent; // BindRewardItems 의 frameInner null-check 호환

            if (pooled.rewardItemTemplate != null)
            {
                pooled.rewardItems = new List<RewardItemRefs>();
                pooled.rewardItems.Add(CaptureRewardItemRefs(pooled.rewardItemTemplate));
            }
        }

        // 보상 아이콘(sprite swap 대상) 후보. 실제 프리팹은 "ImageItem". (Heart/Gift 는 alt 로 별도 처리)
        private static readonly string[] RewardIconNames = { "ImageItem", "ImageRewardItem", "ImageReward" };

        private static RewardItemRefs CaptureRewardItemRefs(GameObject item)
        {
            GameObject rewardGold = FindChildGOByName(item, "RewardGold");
            GameObject rewardItemVariant = FindChildGOByName(item, "RewardItem");
            GameObject imageGift = FindChildGOByName(item, "ImageGift");
            GameObject iconSearchRoot = rewardItemVariant != null ? rewardItemVariant : item;
            // 1) 알려진 후보 이름으로 sprite-swap 아이콘 탐색.
            // [2026-06-11 fix] '이름이 일치하는 Image 컴포넌트' 방식은 ImageItem 이 홀더(Image 없는 GO)이고
            // 실제 Image 가 자식에 있는 구조에서 무음 실패(텍스트만 갱신, 아이콘 안 바뀜) →
            // GO 기준으로 찾고 Image 는 자신 → 자식 순으로 해석.
            Image icon = null;
            for (int i = 0; i < RewardIconNames.Length && icon == null; i++)
            {
                GameObject iconGo = FindChildGOByName(iconSearchRoot, RewardIconNames[i]);
                if (iconGo == null) continue;
                icon = iconGo.GetComponent<Image>();
                if (icon == null) icon = iconGo.GetComponentInChildren<Image>(true);
            }

            // 2) fallback — 후보 미일치 시, 배경/틀/깃발/하트/기프트류를 제외한 첫 Image 를 아이콘으로 사용.
            if (icon == null)
            {
                var imgs = iconSearchRoot.GetComponentsInChildren<Image>(true);
                for (int i = 0; i < imgs.Length; i++)
                {
                    if (imgs[i] == null || imgs[i].gameObject == iconSearchRoot) continue;
                    string n = imgs[i].gameObject.name;
                    if (n.IndexOf("Flag",  System.StringComparison.OrdinalIgnoreCase) >= 0) continue;
                    if (n.IndexOf("Frame", System.StringComparison.OrdinalIgnoreCase) >= 0) continue;
                    if (n.IndexOf("Back",  System.StringComparison.OrdinalIgnoreCase) >= 0) continue;
                    if (n.IndexOf("Bg",    System.StringComparison.OrdinalIgnoreCase) >= 0) continue;
                    if (n.IndexOf("Heart", System.StringComparison.OrdinalIgnoreCase) >= 0) continue;
                    if (n.IndexOf("Gift",  System.StringComparison.OrdinalIgnoreCase) >= 0) continue;
                    icon = imgs[i];
                    break;
                }
#if UNITY_EDITOR
                if (icon == null)
                {
                    var names = new System.Text.StringBuilder();
                    for (int i = 0; i < imgs.Length; i++) { if (i > 0) names.Append(", "); names.Append(imgs[i] != null ? imgs[i].gameObject.name : "null"); }
                    Debug.LogWarning($"[PopupWinningStreak] RewardItem 아이콘 child 못 찾음 — 보상 이미지 미표시. " +
                                     $"후보=[{string.Join("/", RewardIconNames)}], 실제 Image children=[{names}]", item);
                }
#endif
            }

            return new RewardItemRefs
            {
                root = item,
                rewardGold = rewardGold,
                rewardItemVariant = rewardItemVariant,
                icon = icon,
                altHeart = FindChildGOByName(item, "ImageHeart"),
                altGift = imageGift,
                // [{n} fix 2026-06-10] 변형별 쌍 전부 수집 — 단일 FindChildByName 은 첫 쌍만 잡혀 "{n}" 노출.
                texts = CollectTextsByName(item, "TextReward"),
                textOutlines = CollectTextsByName(item, "TextRewardOutline")
            };
        }

        /// <summary>root 아래에서 이름이 정확히 일치하는 TMP_Text 전부 수집 (비활성 포함).</summary>
        private static List<TMP_Text> CollectTextsByName(GameObject root, string name)
        {
            var list = new List<TMP_Text>(2);
            if (root == null) return list;
            var arr = root.GetComponentsInChildren<TMP_Text>(true);
            for (int i = 0; i < arr.Length; i++)
                if (arr[i] != null && arr[i].name == name) list.Add(arr[i]);
            return list;
        }

        /// <summary>RewardItemRefs 의 모든 TextReward/TextRewardOutline 에 같은 수치 텍스트 기록.
        /// 활성 변형이 무엇이든 올바른 값이 들어가고, 비활성 변형은 안 보이므로 무해.</summary>
        private static void SetRewardCountText(RewardItemRefs item, string value)
        {
            if (item == null) return;
            if (item.texts != null)
                for (int i = 0; i < item.texts.Count; i++)
                    if (item.texts[i] != null) item.texts[i].text = value;
            if (item.textOutlines != null)
                for (int i = 0; i < item.textOutlines.Count; i++)
                    if (item.textOutlines[i] != null) item.textOutlines[i].text = value;
        }

        private void ApplyPoolSlotLayout()
        {
            for (int i = 0; i < _pooledSlots.Count; i++)
                ApplySlotLayout(_pooledSlots[i].root);
        }

        // 사이즈/VLG 영향 완전 차단: 900x300 고정 + ignoreLayout
        private void ApplySlotLayout(RectTransform slotRt)
        {
            if (slotRt == null) return;

            slotRt.localScale = Vector3.one;
            slotRt.anchorMin = new Vector2(0f, 1f);
            slotRt.anchorMax = new Vector2(1f, 1f);
            slotRt.pivot = new Vector2(0.5f, 1f);
            slotRt.sizeDelta = new Vector2(SlotFixedWidth, SlotFixedHeight);

            LayoutElement layoutElement = slotRt.GetComponent<LayoutElement>();
            if (layoutElement == null)
                layoutElement = slotRt.gameObject.AddComponent<LayoutElement>();

            layoutElement.minWidth = SlotFixedWidth;
            layoutElement.preferredWidth = SlotFixedWidth;
            layoutElement.minHeight = SlotFixedHeight;
            layoutElement.preferredHeight = SlotFixedHeight;
            layoutElement.flexibleWidth = 0f;
            layoutElement.flexibleHeight = 0f;
            layoutElement.ignoreLayout = true;
        }

        private void ClearSlotContent(GameObject template)
        {
            if (_keyBlazeContents == null) return;

            Transform templateTransform = template != null ? template.transform : null;
            for (int i = _keyBlazeContents.childCount - 1; i >= 0; i--)
            {
                Transform child = _keyBlazeContents.GetChild(i);
                if (child == templateTransform)
                {
                    child.gameObject.SetActive(false);
                    continue;
                }
                child.gameObject.SetActive(false);
                Destroy(child.gameObject);
            }
        }

        private void BindScrollListener()
        {
            if (_scrollRect == null || _scrollListenerBound) return;
            _scrollRect.onValueChanged.AddListener(HandleScrollValueChanged);
            _scrollListenerBound = true;
        }

        private void HandleScrollValueChanged(Vector2 _)
        {
            if (_suppressScrollCallback) return;
            HideTooltip();
            RefreshVisibleSlots();
        }

        private void ResetScrollPosition()
        {
            if (_scrollRect == null || _keyBlazeContents == null || !_slotsBuilt) return;

            SetVirtualContentHeight();
            Canvas.ForceUpdateCanvases();

            _suppressScrollCallback = true;
            _scrollRect.StopMovement();
            _scrollRect.velocity = Vector2.zero;
            // 팝업 오픈 시 현재 시도 중인 스테이지(currentStage)가 뷰포트에 보이도록 스크롤.
            //   매핑: stage = dataCount - dataIndex (높은 stage 위, stage1 아래). vnp=1 top, 0 bottom.
            _scrollRect.verticalNormalizedPosition = ComputeScrollToCurrentStage();
            Canvas.ForceUpdateCanvases();
            _scrollRect.velocity = Vector2.zero;
            _suppressScrollCallback = false;

            RefreshVisibleSlots();
        }

        /// <summary>현재 스테이지를 뷰포트 중앙쯤에 두는 verticalNormalizedPosition(0~1) 산출.</summary>
        private float ComputeScrollToCurrentStage()
        {
            int dataCount = DataCount;
            int visible = Mathf.Max(1, _pooledSlots.Count);
            int maxFirstDataIndex = Mathf.Max(0, dataCount - visible);
            if (maxFirstDataIndex <= 0) return 1f;

            int currentStage = 1;
            var mgr = WinningStreakManager.HasInstance ? WinningStreakManager.Instance : null;
            if (mgr?.State != null) currentStage = Mathf.Clamp(mgr.State.currentStage, 1, dataCount);

            int currentDataIndex = dataCount - currentStage;          // 0=최상단 stage
            int firstDataIndex = Mathf.Clamp(currentDataIndex - visible / 2, 0, maxFirstDataIndex);
            return 1f - (float)firstDataIndex / maxFirstDataIndex;    // firstDataIndex=0 → vnp 1(top)
        }

        // ── 슬롯 데이터 바인딩 ────────────────────────────────────

        private void RefreshVisibleSlots()
        {
            if (_keyBlazeContents == null || _slotStrideY <= 1f || _pooledSlots.Count == 0) return;

            int dataCount = DataCount;
            int maxFirstDataIndex = Mathf.Max(0, dataCount - _pooledSlots.Count);
            // [2026-06-12 fix] 정규화 스크롤값(vnp) 라운딩 → 콘텐츠 픽셀 기반 인덱스.
            //   vnp 비율 스케일과 슬롯 stride 픽셀 매핑이 정확히 비례하지 않아(패딩 포함 높이 vs 인덱스 범위)
            //   리스트 상단부(고단계, 22→23+ 스크롤)에서 슬롯 윈도우가 늦게 이동 — 빈 틈(끊김)이 보이던 원인.
            //   content 는 pivot(0.5,1)/anchor top 이라 위로 스크롤할수록 anchoredPosition.y 가 + 로 증가.
            float scrolledY = Mathf.Max(0f, _keyBlazeContents.anchoredPosition.y - _contentTopPadding);
            int firstDataIndex = Mathf.Clamp(Mathf.FloorToInt(scrolledY / _slotStrideY), 0, maxFirstDataIndex);

            for (int poolIndex = 0; poolIndex < _pooledSlots.Count; poolIndex++)
            {
                var pooled = _pooledSlots[poolIndex];
                if (pooled?.root == null) continue;

                int dataIndex = firstDataIndex + poolIndex;
                if (dataIndex >= dataCount)
                {
                    pooled.root.gameObject.SetActive(false);
                    pooled.boundStage = -1;
                    continue;
                }

                int stage = dataCount - dataIndex;       // 위로 갈수록 높은 stage
                pooled.root.gameObject.SetActive(true);
                pooled.root.name = $"SlotWinningStreak_{stage:D2}";
                pooled.root.anchoredPosition = new Vector2(
                    pooled.root.anchoredPosition.x,
                    -(_contentTopPadding + dataIndex * _slotStrideY));

                BindSlotData(pooled, stage);
            }
        }

        private void BindSlotData(PooledSlot pooled, int stage1Based)
        {
            pooled.boundStage = stage1Based;
            EnsureStreakSprites();

            SetSlotNumber(pooled, stage1Based);

            var mgr = WinningStreakManager.HasInstance ? WinningStreakManager.Instance : null;
            var stageDoc = WinningStreakConfigService.HasInstance
                ? WinningStreakConfigService.Instance.GetStage(stage1Based)
                : null;

            SlotState slotState = ResolveSlotState(mgr, stage1Based);
            pooled.lastState = slotState;
            ApplySlotState(pooled, slotState);
            BindRewardItems(pooled, stageDoc);

            int totalStages = DataCount;
            SetActiveSafe(pooled.lineBottom, stage1Based != 1);
            SetActiveSafe(pooled.lineTop, stage1Based != totalStages);
            SetActiveSafe(pooled.grandPrize, stage1Based == totalStages);
        }

        private SlotState ResolveSlotState(WinningStreakManager mgr, int stage1Based)
        {
            if (mgr == null || mgr.State == null) return SlotState.Locked;
            if (mgr.IsStageClaimed(stage1Based)) return SlotState.Claimed;
            if (mgr.IsStageAchieved(stage1Based)) return SlotState.AchievedUnclaimed;
            if (stage1Based == mgr.State.currentStage) return SlotState.InProgress;
            return SlotState.Locked;
        }

        private static void SetActiveSafe(GameObject go, bool active)
        {
            if (go != null && go.activeSelf != active) go.SetActive(active);
        }

        /// <summary>3가지 상태(Lock / 현재 레벨 / 완료)별 SlotWinningStreak 시각 세팅 (사용자 명세 정정).
        /// Claimed: ImageInnerFrame=Complete, BtnReward=SlotComplete, ImageArrow=ArrowComplete, font=Green, IconCheck on.
        /// InProgress/Locked: ImageInnerFrame=Default, BtnReward=Slot, ImageArrow=Slot, font=Purple, IconLock on.
        /// RotateLight 는 InProgress 만 on.</summary>
        private void ApplySlotState(PooledSlot pooled, SlotState state)
        {
            EnsureStreakSprites();

            switch (state)
            {
                case SlotState.Locked:
                    SetActiveSafe(pooled.rotateLight, false);
                    SetSpriteSafe(pooled.imageInnerFrame, _sprFrameNumberDefault);
                    SetSpriteSafe(pooled.imageArrow, _sprArrow);
                    SetSpriteSafe(pooled.btnRewardImage, _sprSlot);
                    SetFontMaterialSafe(pooled.textNumberOutline, _fontMatPurpleOutline);
                    SetActiveSafe(pooled.iconLock, true);
                    SetActiveSafe(pooled.iconCheck, false);
                    if (pooled.button != null) pooled.button.interactable = true;
                    break;

                case SlotState.InProgress:
                case SlotState.AchievedUnclaimed:
                    SetActiveSafe(pooled.rotateLight, true);
                    SetSpriteSafe(pooled.imageInnerFrame, _sprFrameNumberDefault);
                    SetSpriteSafe(pooled.imageArrow, _sprArrow);
                    SetSpriteSafe(pooled.btnRewardImage, _sprSlot);
                    SetFontMaterialSafe(pooled.textNumberOutline, _fontMatPurpleOutline);
                    SetActiveSafe(pooled.iconCheck, false);
                    SetActiveSafe(pooled.iconLock, true);
                    if (pooled.button != null) pooled.button.interactable = true;
                    break;

                case SlotState.Claimed:
                    SetActiveSafe(pooled.rotateLight, false);
                    SetSpriteSafe(pooled.imageInnerFrame, _sprFrameNumberComplete);
                    SetSpriteSafe(pooled.imageArrow, _sprArrowComplete);
                    SetSpriteSafe(pooled.btnRewardImage, _sprSlotComplete);
                    SetFontMaterialSafe(pooled.textNumberOutline, _fontMatGreenOutline);
                    SetActiveSafe(pooled.iconLock, false);
                    SetActiveSafe(pooled.iconCheck, true);
                    if (pooled.button != null) pooled.button.interactable = true;
                    break;
            }
            // imageDefault/imageGet 는 ImageInnerFrame sprite 와 충돌할 수 있어 여기서 토글하지 않음.
        }

        private static void SetFontMaterialSafe(TMP_Text text, Material mat)
        {
            if (text == null || mat == null) return;
            text.fontSharedMaterial = mat;
        }

        private static void SetSpriteSafe(Image image, Sprite sprite)
        {
            if (image == null || sprite == null) return;
            image.sprite = sprite;
        }

        private void EnsureStreakSprites()
        {
            if (!ResourceManager.HasInstance) return;
            var rm = ResourceManager.Instance;
            if (_sprFrameNumberDefault == null)  _sprFrameNumberDefault  = rm.GetUISprite(Const.SPR_FRAMEWINNERSTREAKNUMBERDEFAULT);
            if (_sprFrameNumberComplete == null) _sprFrameNumberComplete = rm.GetUISprite(Const.SPR_FRAMEWINNERSTREAKNUMBERCOMPLETE);
            if (_sprArrow == null)               _sprArrow               = rm.GetUISprite(Const.SPR_FRAMEWINNINGSTREAKSLOTARROW);
            if (_sprArrowComplete == null)       _sprArrowComplete       = rm.GetUISprite(Const.SPR_FRAMEWINNINGSTREAKSLOTARROWCOMPLETE);
            if (_sprSlot == null)                _sprSlot                = rm.GetUISprite(Const.SPR_FRAMEWINNINGSTREAKSLOT);
            if (_sprSlotComplete == null)        _sprSlotComplete        = rm.GetUISprite(Const.SPR_FRAMEWINNINGSTREAKSLOTCOMPLETE);
            if (_fontMatGreenOutline == null)    _fontMatGreenOutline    = Resources.Load<Material>(Const.FONT_MAT_POPPINS_BOLD_GREEN_OUTLINE);
            if (_fontMatPurpleOutline == null)   _fontMatPurpleOutline   = Resources.Load<Material>(Const.FONT_MAT_POPPINS_BOLD_PURPLE_OUTLINE);
        }

        private void SetSlotNumber(PooledSlot pooled, int number)
        {
            // [2026-06-11 스펙 변경] 단계 번호(TextNumber/Outline) 다시 노출 — 이전 '노출 제거' 스펙 폐기.
            // Outline 이 부모, TextNumber 가 자식 구조라 둘 다 활성화해야 보인다.
            if (pooled.textNumber != null) {
                pooled.textNumber.text = number.ToString();
                if (!pooled.textNumber.gameObject.activeSelf) pooled.textNumber.gameObject.SetActive(true);
            }
            if (pooled.textNumberOutline != null) {
                pooled.textNumberOutline.text = number.ToString();
                if (!pooled.textNumberOutline.gameObject.activeSelf) pooled.textNumberOutline.gameObject.SetActive(true);
            }
        }

        // ── 보상 아이템 ──────────────────────────────────────────

        private void BindRewardItems(PooledSlot pooled, WinningStreakStage stageDoc)
        {
            if (pooled.frameInner == null || pooled.rewardItemTemplate == null) return;

            // 완료 상태에서는 RewardItem 모두 비활성화 (이미 수령했으므로 표시 X).
            if (pooled.lastState == SlotState.Claimed)
            {
                if (pooled.rewardItems != null)
                {
                    for (int i = 0; i < pooled.rewardItems.Count; i++)
                    {
                        var item = pooled.rewardItems[i];
                        if (item?.root != null) item.root.SetActive(false);
                    }
                }
                return;
            }

            List<RewardEntry> rewards = BuildRewardEntries(stageDoc);
            // ROLLBACK_WINNING_STREAK_REWARDIMG_VARIANT_RULE:
            // New spec uses one RewardImg variant container. Old behavior used rewards.Count clones.
            int requiredCount = 1;

            EnsureRewardItemCount(pooled, requiredCount);

            for (int i = 0; i < pooled.rewardItems.Count; i++)
            {
                var item = pooled.rewardItems[i];
                if (item?.root == null) continue;

                if (i > 0)
                {
                    item.root.SetActive(false);
                    continue;
                }

                if (i < rewards.Count)
                {
                    item.root.SetActive(true);
                    BindRewardImgVariant(item, rewards);
                    if (UseRewardImgVariantRule()) continue;

                    // Heart 보상은 sprite swap 대신 전용 ImageHeart 표시
                    bool isHeart = rewards[i].type == RewardType.InfiniteHearts;
                    if (item.altHeart != null) item.altHeart.SetActive(isHeart);
                    if (item.altGift != null) item.altGift.SetActive(false);
                    if (item.icon != null)
                    {
                        if (isHeart)
                        {
                            if (item.icon.gameObject.activeSelf) item.icon.gameObject.SetActive(false);
                        }
                        else
                        {
                            var sprite = ResolveRewardSprite(rewards[i].type);
                            if (sprite != null) item.icon.sprite = sprite;
                            item.icon.enabled = true;
                            if (!item.icon.gameObject.activeSelf) item.icon.gameObject.SetActive(true); // ImageItem 기본 비활성 → 활성화
                        }
                    }

                    string countText = rewards[i].count > 0 ? $"x{rewards[i].count}" : "";
                    SetRewardCountText(item, countText);
                }
                else
                {
                    item.root.SetActive(false);
                }
            }
        }

        private void EnsureRewardItemCount(PooledSlot pooled, int requiredCount)
        {
            while (pooled.rewardItems.Count < requiredCount)
            {
                var clone = Instantiate(pooled.rewardItemTemplate, pooled.frameInner);
                clone.name = $"{pooled.rewardItemTemplate.name}_{pooled.rewardItems.Count}";
                pooled.rewardItems.Add(CaptureRewardItemRefs(clone));
            }
        }

        private static bool UseRewardImgVariantRule()
        {
            return true;
        }

        // ROLLBACK_WINNING_STREAK_REWARDIMG_VARIANT_RULE:
        // New RewardImg rule:
        // - RewardGold: gold-only reward
        // - RewardItem: exactly one item reward, including infinite hearts
        // - ImageGift: two or more reward entries, or mixed/composite rewards
        private void BindRewardImgVariant(RewardItemRefs item, List<RewardEntry> rewards)
        {
            bool hasSingle = rewards != null && rewards.Count == 1;
            bool useGold = hasSingle && rewards[0].type == RewardType.Coin;
            bool useItem = hasSingle && rewards[0].type != RewardType.Coin;
            bool useGift = rewards != null && rewards.Count >= 2;

            // [2026-06-11 fix] ImageGift 는 RewardItem '내부'에 중첩된 프리팹 구조 — gift 만 켜고
            // 부모(RewardItem)를 끄면 상자가 영영 안 보였다. gift 모드에선 부모를 켜되 내부
            // 아이템 비주얼(아이콘/하트/깃발)을 숨겨 '상자 이미지만' 노출 (사용자 스펙).
            bool giftInsideItem = item.altGift != null && item.rewardItemVariant != null
                && item.altGift.transform.IsChildOf(item.rewardItemVariant.transform);

            RewardEntry primary = hasSingle ? rewards[0] : default;
            // Heart 보상은 sprite swap 대신 전용 ImageHeart 표시
            bool isHeart = useItem && primary.type == RewardType.InfiniteHearts;

            if (item.rewardGold != null) item.rewardGold.SetActive(useGold);
            if (item.rewardItemVariant != null) item.rewardItemVariant.SetActive(useItem || (useGift && giftInsideItem));
            if (item.altGift != null) item.altGift.SetActive(useGift);
            if (item.altHeart != null) item.altHeart.SetActive(useItem && isHeart);

            if (useItem && isHeart)
            {
                if (item.icon != null && item.icon.gameObject.activeSelf) item.icon.gameObject.SetActive(false);
            }
            else if (useItem && item.icon != null)
            {
                var sprite = ResolveRewardSprite(primary.type);
                if (sprite != null) item.icon.sprite = sprite;
                else Debug.LogWarning($"[PopupWinningStreak] 보상 아이콘 sprite 미해석 — type={primary.type} (atlas_ui 미로드/키 누락 의심)");
                item.icon.enabled = true;
                // [2026-06-11 fix] Image 가 홀더(ImageItem)의 자식일 수 있어 변형 루트까지 조상 활성화.
                SetActiveUpTo(item.icon.transform,
                    item.rewardItemVariant != null ? item.rewardItemVariant.transform : item.root.transform);
            }
            else if (useItem)
            {
                Debug.LogWarning("[PopupWinningStreak] RewardItem 아이콘 Image 미발견 — RewardImg 프리팹 구조 확인 필요.");
            }
            else if (useGift && item.icon != null && item.icon.gameObject.activeSelf)
            {
                item.icon.gameObject.SetActive(false); // gift 모드 — 아이템 아이콘 숨김 (상자만)
            }
            SetRewardItemFlagsActive(item, useItem); // 깃발/카운트 배경은 단일 아이템에서만

            // 다중 보상 = 상자 이미지만 (내용은 슬롯 클릭 시 WinningStreakClickInfo 툴팁이 표시).
            string countText = hasSingle ? GetRewardCountText(primary) : "";
            SetRewardCountText(item, countText);
        }

        /// <summary>leaf 부터 stopExclusive 직전까지 조상 GameObject 활성화 — 홀더 구조의 아이콘 노출 보장.</summary>
        private static void SetActiveUpTo(Transform leaf, Transform stopExclusive)
        {
            for (Transform t = leaf; t != null && t != stopExclusive; t = t.parent)
                if (!t.gameObject.activeSelf) t.gameObject.SetActive(true);
        }

        /// <summary>RewardItem 변형 내부의 깃발/카운트 배경(FlagBack*, RewardFlag) 토글 — gift 모드 '상자만' 표시용.</summary>
        private static void SetRewardItemFlagsActive(RewardItemRefs item, bool active)
        {
            if (item?.rewardItemVariant == null) return;
            var all = item.rewardItemVariant.GetComponentsInChildren<Transform>(true);
            for (int i = 0; i < all.Length; i++)
            {
                if (all[i] == null) continue;
                string n = all[i].gameObject.name;
                if (n.StartsWith("FlagBack") || n == "RewardFlag")
                    all[i].gameObject.SetActive(active);
            }
        }

        private static List<RewardEntry> BuildRewardEntries(WinningStreakStage stageDoc)
        {
            var list = new List<RewardEntry>(3);
            if (stageDoc == null || stageDoc.rewards == null) return list;

            var r = stageDoc.rewards;
            if (r.coins > 0) list.Add(new RewardEntry { type = RewardType.Coin, count = r.coins });
            if (r.boosters != null)
            {
                if (r.boosters.hand > 0) list.Add(new RewardEntry { type = RewardType.Hand, count = r.boosters.hand });
                if (r.boosters.shuffle > 0) list.Add(new RewardEntry { type = RewardType.Shuffle, count = r.boosters.shuffle });
                if (r.boosters.zap > 0) list.Add(new RewardEntry { type = RewardType.Zap, count = r.boosters.zap });
            }
            if (r.infiniteHeartsSeconds > 0)
                list.Add(new RewardEntry { type = RewardType.InfiniteHearts, count = r.infiniteHeartsSeconds });
            return list;
        }

        private static string GetRewardCountText(RewardEntry reward)
        {
            if (reward.count <= 0) return "";
            switch (reward.type)
            {
                case RewardType.Coin: return reward.count.ToString();           // "5000", no x
                case RewardType.InfiniteHearts: return FormatInfiniteHearts(reward.count);
                default: return $"x{reward.count}";                              // booster: "x1", "x3"
            }
        }

        private static string FormatInfiniteHearts(int seconds)
        {
            if (seconds <= 0) return "";
            // ROLLBACK_WS_TIME_LOCALIZE_20260714: KO 보상 시간 단위(시간/분/초). 한글은 폰트 fallback 으로 렌더.
            bool ko = LocalizationService.CurrentLanguageCode == "KO";
            int h = seconds / 3600;
            if (h >= 1) return ko ? $"{h}시간" : $"{h}h";
            int m = seconds / 60;
            return m > 0 ? (ko ? $"{m}분" : $"{m}m") : (ko ? $"{seconds}초" : $"{seconds}s");
        }

        private static string BuildBoxRewardText(List<RewardEntry> rewards)
        {
            var sb = new System.Text.StringBuilder();
            for (int i = 0; i < rewards.Count; i++)
            {
                if (i > 0) sb.Append(" + ");
                var r = rewards[i];
                switch (r.type)
                {
                    case RewardType.Hand:           sb.Append($"Hand x{r.count}"); break;
                    case RewardType.Shuffle:        sb.Append($"Shuffle x{r.count}"); break;
                    case RewardType.Zap:            sb.Append($"Zap x{r.count}"); break;
                    case RewardType.InfiniteHearts: sb.Append(FormatInfiniteHearts(r.count)); break;
                    case RewardType.Coin:           sb.Append(r.count.ToString()); break;
                }
            }
            return sb.ToString();
        }

        // 보상 아이콘은 항상 atlas_ui (Addressable) 에서 동적 로드 — Shop / PurchaseRewardEffect 와 동일 패턴.
        // Inspector 직접 sprite 링크는 사용하지 않음.
        private static Sprite ResolveRewardSprite(RewardType type)
        {
            if (!ResourceManager.HasInstance) return null;
            string spriteName = type switch
            {
                RewardType.Coin => Const.SPR_ICONGOLD,
                RewardType.Hand => Const.SPR_ICONHAND,
                RewardType.Shuffle => Const.SPR_ICONSUFFLE,
                RewardType.Zap => Const.SPR_ICONZAP,
                RewardType.InfiniteHearts => Const.SPR_ICONHEARINFINITE,
                _ => null
            };
            return ResourceManager.Instance.GetUISprite(spriteName);
        }

        // ── Claim 처리 ───────────────────────────────────────────

        private void HandleSlotClick(int poolIndex)
        {
            if (poolIndex < 0 || poolIndex >= _pooledSlots.Count) return;
            var pooled = _pooledSlots[poolIndex];
            if (pooled == null || pooled.boundStage <= 0) return;
            if (!WinningStreakManager.HasInstance) return;

            if (WinningStreakManager.Instance.IsStageClaimed(pooled.boundStage))
            {
                HideTooltip();
                return;
            }

            bool ok = WinningStreakManager.Instance.ClaimStage(pooled.boundStage);
            if (ok)
            {
                // Manager 가 OnStateChanged 발화 → HandleStateChanged 가 슬롯 재그리기.
                // 즉시 시각 갱신.
                BindSlotData(pooled, pooled.boundStage);
                RefreshHeader();
                HideTooltip();
                return;
            }

            ToggleTooltipForSlot(pooled);
        }

        // ── 툴팁 (WinningStreakClickInfo) ────────────────────────

        private void ToggleTooltipForSlot(PooledSlot pooled)
        {
            if (pooled == null || pooled.button == null || pooled.boundStage <= 0) return;

            if (_activeTooltipStage == pooled.boundStage && _tooltipInstance != null && _tooltipInstance.activeSelf)
            {
                HideTooltip();
                return;
            }

            EnsureTooltipInstance();
            if (_tooltipInstance == null || _tooltipRect == null) return;

            RectTransform anchor = pooled.button.transform as RectTransform;
            if (anchor == null) return;

            // 클릭된 stage 의 보상으로 tooltip 내용 갱신.
            var stageDoc = WinningStreakConfigService.HasInstance
                ? WinningStreakConfigService.Instance.GetStage(pooled.boundStage)
                : null;
            if (WinningStreakClickInfoBinder.CountRewards(stageDoc) <= 1)
            {
                HideTooltip();
                return;
            }
            WinningStreakClickInfoBinder.Bind(_tooltipInstance, stageDoc);

            _tooltipInstance.SetActive(true);
            PositionTooltip(anchor);
            _tooltipInstance.transform.SetAsLastSibling();
            _activeTooltipStage = pooled.boundStage;

            _tooltipPopTween?.Kill();
            _tooltipRect.localScale = Vector3.zero;
            _tooltipPopTween = _tooltipRect.DOScale(Vector3.one, TooltipPopDuration)
                .SetEase(Ease.OutBack)
                .SetUpdate(true);
        }

        private void EnsureTooltipInstance()
        {
            if (_tooltipInstance != null) return;

            GameObject prefab = Resources.Load<GameObject>(TooltipPrefabResource);
            if (prefab == null)
            {
                Debug.LogWarning("[PopupWinningStreak] WinningStreakClickInfo prefab not found at Resources/" + TooltipPrefabResource);
                return;
            }

            // popup root (this.transform) 자식으로 생성 — ScrollRect Mask 영향 밖.
            _tooltipInstance = Instantiate(prefab, transform);
            _tooltipInstance.name = "WinningStreakClickInfo";
            _tooltipRect = _tooltipInstance.GetComponent<RectTransform>();
            _tooltipArrowTop = FindChildGOByName(_tooltipInstance, "ArrowTop");
            _tooltipArrowBottom = FindChildGOByName(_tooltipInstance, "ArrowBottom");
            _tooltipInstance.SetActive(false);
        }

        private void PositionTooltip(RectTransform anchorButtonRect)
        {
            if (_tooltipRect == null || anchorButtonRect == null) return;

            RectTransform popupRect = transform as RectTransform;
            if (popupRect == null) return;

            RectTransform viewport = _scrollRect != null ? _scrollRect.viewport : null;
            Canvas canvas = GetComponentInParent<Canvas>();
            Camera cam = (canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay) ? canvas.worldCamera : null;

            // 1) 버튼 중심을 popup local 좌표로 변환 (툴팁 SetParent(popup) 가정).
            Vector3 anchorWorldCenter = anchorButtonRect.TransformPoint(anchorButtonRect.rect.center);
            Vector2 screenPoint = RectTransformUtility.WorldToScreenPoint(cam, anchorWorldCenter);
            Vector2 anchorLocalInPopup;
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(popupRect, screenPoint, cam, out anchorLocalInPopup))
                return;

            // 2) 버튼이 viewport 상반부에 있는지 판정 → 상/하 플립.
            bool placeBelow = true;
            if (viewport != null)
            {
                Vector2 anchorLocalInViewport;
                if (RectTransformUtility.ScreenPointToLocalPointInRectangle(viewport, screenPoint, cam, out anchorLocalInViewport))
                {
                    Rect vRect = viewport.rect;
                    float normalizedY = vRect.height > 0f
                        ? (anchorLocalInViewport.y - vRect.yMin) / vRect.height
                        : 1f;
                    placeBelow = normalizedY > TooltipViewportFlipThreshold;
                }
            }

            _tooltipRect.pivot = placeBelow ? new Vector2(0.5f, 1f) : new Vector2(0.5f, 0f);
            SetActiveSafe(_tooltipArrowTop, placeBelow);
            SetActiveSafe(_tooltipArrowBottom, !placeBelow);

            // 3) 툴팁 위치 — 버튼 위/아래로 오프셋 (pivot 이 변 쪽에 붙어 tipHalfH 보정 불필요).
            float btnHalfH = anchorButtonRect.rect.height * 0.5f * anchorButtonRect.lossyScale.y
                             / Mathf.Max(0.0001f, popupRect.lossyScale.y);
            float targetX = anchorLocalInPopup.x;
            float targetY = placeBelow
                ? anchorLocalInPopup.y - btnHalfH
                : anchorLocalInPopup.y + btnHalfH;

            _tooltipRect.anchoredPosition = new Vector2(targetX, targetY);

            // 4) Clamp — 4 코너를 popup rect 안으로.
            Canvas.ForceUpdateCanvases();
            Vector3[] tipCorners = new Vector3[4];
            _tooltipRect.GetWorldCorners(tipCorners);
            Vector3[] popupCorners = new Vector3[4];
            popupRect.GetWorldCorners(popupCorners);

            float minX = popupCorners[0].x;
            float maxX = popupCorners[2].x;
            float minY = popupCorners[0].y;
            float maxY = popupCorners[2].y;

            float dx = 0f, dy = 0f;
            if (tipCorners[0].x < minX) dx = minX - tipCorners[0].x;
            else if (tipCorners[2].x > maxX) dx = maxX - tipCorners[2].x;
            if (tipCorners[0].y < minY) dy = minY - tipCorners[0].y;
            else if (tipCorners[2].y > maxY) dy = maxY - tipCorners[2].y;

            if (dx != 0f || dy != 0f)
            {
                float scaleX = Mathf.Max(0.0001f, popupRect.lossyScale.x);
                float scaleY = Mathf.Max(0.0001f, popupRect.lossyScale.y);
                _tooltipRect.anchoredPosition += new Vector2(dx / scaleX, dy / scaleY);
            }
        }

        private void HideTooltip()
        {
            _tooltipPopTween?.Kill();
            _tooltipPopTween = null;
            if (_tooltipInstance != null && _tooltipInstance.activeSelf)
                _tooltipInstance.SetActive(false);
            _activeTooltipStage = -1;
        }

        // ── 내부 데이터 구조 ──────────────────────────────────────

        private enum SlotState { Locked, InProgress, AchievedUnclaimed, Claimed }

        private enum RewardType { None, Coin, Hand, Shuffle, Zap, InfiniteHearts }

        private struct RewardEntry { public RewardType type; public int count; }

        private class RewardItemRefs
        {
            public GameObject root;
            public GameObject rewardGold;
            public GameObject rewardItemVariant;
            public Image icon;          // ImageItem — 코인/부스터/하트 sprite 를 swap 해서 표시(단일 아이콘).
            public GameObject altHeart; // ImageHeart — 기본 활성이라 숨겨야 ImageItem 과 안 겹침.
            public GameObject altGift;  // ImageGift — WS 보상 타입에 매핑 없음, 숨김.
            // [{n} fix 2026-06-10] RewardImg 는 변형(RewardGold/RewardItem)마다 TextReward/Outline 쌍을 따로 가짐.
            //   단일 캡처는 첫 쌍만 잡아 다른 변형 활성 시 prefab placeholder "{n}" 가 그대로 노출됐다 → 전부 수집.
            public List<TMP_Text> texts;
            public List<TMP_Text> textOutlines;
        }

        private class PooledSlot
        {
            public RectTransform root;
            public TMP_Text textNumber;
            public TMP_Text textNumberOutline;
            public GameObject imageDefault;
            public GameObject imageGet;
            public GameObject iconCheck;
            public GameObject iconLock;
            public Button button;
            public RectTransform frameInner;
            public GameObject rewardItemTemplate;
            public List<RewardItemRefs> rewardItems;
            public int boundStage = -1;
            public GameObject rotateLight;
            public Image imageInnerFrame;
            public Image imageArrow;
            public Image btnRewardImage;
            public Transform rewardItemRoot;
            public SlotState lastState = SlotState.Locked;
            public GameObject lineTop;
            public GameObject lineBottom;
            public GameObject grandPrize;
        }
    }
}
