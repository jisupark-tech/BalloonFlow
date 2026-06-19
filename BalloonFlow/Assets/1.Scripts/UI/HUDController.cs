using UnityEngine;
using UnityEngine.UI;

namespace BalloonFlow
{
    /// <summary>
    /// 인게임 HUD 컨트롤러. SceneSingleton.
    /// UIHud 뷰를 바인딩하고 이벤트 기반으로 업데이트.
    ///
    /// Row 1: [Settings] | [Level X] | [Gold + 버튼]
    /// Row 2: [Balloons: N] | [Score] | [On Rail: N/M]
    /// </summary>
    public class HUDController : SceneSingleton<HUDController>
    {
        #region Constants — Difficulty Tint Colors

        // Design ref: BeatChart_Direction — Hard/SuperHard HUD 색상 차별화
        private static readonly Color TINT_NORMAL    = Color.white;
        private static readonly Color TINT_HARD      = new Color(1f, 0.85f, 0.65f);      // warm amber
        private static readonly Color TINT_SUPERHARD  = new Color(1f, 0.55f, 0.55f);      // red-ish

        // Gauge stage HUD overlay colors
        private static readonly Color GAUGE_SAFE     = new Color(0f, 0f, 0f, 0f);         // transparent
        private static readonly Color GAUGE_CAUTION  = new Color(1f, 1f, 0f, 0.05f);      // faint yellow
        private static readonly Color GAUGE_WARNING  = new Color(1f, 0.3f, 0f, 0.12f);    // orange tint
        private static readonly Color GAUGE_CRITICAL = new Color(1f, 0f, 0f, 0.2f);       // red tint

        #endregion

        #region Fields

        private UIHud _view;
        private PopupSettings _popupSettings;
        private PopupGoldShop _popupGoldShop;
        private PopupQuit _popupQuit;
        private Image _gaugeOverlay; // screen-edge warning overlay

        // 진행률 (LvPanel 슬라이더): 탄창 소진(magazine=0)된 보관함 수 / 전체 보관함 수
        // 분모는 OnLevelLoaded 시점의 _holders.Count (Spawner 자체는 1로 카운트, spawn될 보관함은 미반영).

        #endregion

        #region Lifecycle

        private void OnEnable()
        {
            EventBus.Subscribe<OnHolderSelected>(HandleHolderSelected);
            EventBus.Subscribe<OnMagazineEmpty>(HandleMagazineEmpty);
            EventBus.Subscribe<OnHolderReturned>(HandleHolderReturned);
            EventBus.Subscribe<OnLevelLoaded>(HandleLevelLoaded);
            EventBus.Subscribe<OnCoinChanged>(HandleCoinChanged);
            EventBus.Subscribe<OnGaugeStageChanged>(HandleGaugeStage);
            EventBus.Subscribe<OnHolderDeploymentDone>(HandleHolderDeploymentDone);
            EventBus.Subscribe<OnBalloonPopped>(HandleBalloonPopped);
        }

        private void OnDisable()
        {
            EventBus.Unsubscribe<OnHolderSelected>(HandleHolderSelected);
            EventBus.Unsubscribe<OnMagazineEmpty>(HandleMagazineEmpty);
            EventBus.Unsubscribe<OnHolderReturned>(HandleHolderReturned);
            EventBus.Unsubscribe<OnLevelLoaded>(HandleLevelLoaded);
            EventBus.Unsubscribe<OnCoinChanged>(HandleCoinChanged);
            EventBus.Unsubscribe<OnGaugeStageChanged>(HandleGaugeStage);
            EventBus.Unsubscribe<OnHolderDeploymentDone>(HandleHolderDeploymentDone);
            EventBus.Unsubscribe<OnBalloonPopped>(HandleBalloonPopped);

            // 버튼 이벤트 해제
            if (_view != null)
            {
                if (_view.SettingsButton != null) _view.SettingsButton.onClick.RemoveListener(OnSettingsClicked);
                if (_view.GoldPlusButton != null) _view.GoldPlusButton.onClick.RemoveListener(OnGoldPlusClicked);
            }
            if (_popupSettings != null)
            {
                if (_popupSettings.CloseButton != null)
                    _popupSettings.CloseButton.onClick.RemoveListener(OnSettingsCloseClicked);
                if (_popupSettings.HomeButton != null)
                    _popupSettings.HomeButton.onClick.RemoveListener(OnSettingsHomeClicked);
                if (_popupSettings.ContinueButton != null)
                    _popupSettings.ContinueButton.onClick.RemoveListener(OnSettingsCloseClicked);
            }
            if (_popupGoldShop != null && _popupGoldShop.CloseButton != null)
                _popupGoldShop.CloseButton.onClick.RemoveListener(OnGoldShopCloseClicked);
            if (_popupQuit != null)
            {
                if (_popupQuit.HomeButton != null)
                    _popupQuit.HomeButton.onClick.RemoveListener(OnQuitHomeClicked);
                if (_popupQuit.NextButton != null)
                    _popupQuit.NextButton.onClick.RemoveListener(OnQuitNextClicked);
                if (_popupQuit.ExitDuplicateButton != null)
                    _popupQuit.ExitDuplicateButton.onClick.RemoveListener(OnQuitNextClicked);
            }
        }

        #endregion

        #region Public — View/Popup 바인딩

        /// <summary>
        /// UIHud 뷰 바인딩. GameBootstrap이 UIHud 로드 후 호출.
        /// </summary>
        public void BindView(UIHud _hudView)
        {
            if (_hudView == null) return;
            _view = _hudView;

            // 버튼 이벤트 연결
            if (_view.SettingsButton != null) _view.SettingsButton.onClick.AddListener(OnSettingsClicked);
            if (_view.GoldPlusButton != null) _view.GoldPlusButton.onClick.AddListener(OnGoldPlusClicked);

            // Proactively set level info in case OnLevelLoaded already fired before binding
            if (LevelManager.HasInstance && LevelManager.Instance.CurrentLevelId > 0)
            {
                LevelConfig cfg = LevelManager.Instance.CurrentLevel;
                int pkgId = cfg != null ? cfg.packageId : 1;
                SetLevelInfo(LevelManager.Instance.CurrentLevelId, pkgId);
            }
            if (CurrencyManager.HasInstance) UpdateGoldDisplay(CurrencyManager.Instance.Coins);
            RefreshOnRailCount();

        }

        /// <summary>설정 팝업 연결 + Close/Home 버튼 와이어링</summary>
        public void SetSettingsPopup(PopupSettings _popup)
        {
            _popupSettings = _popup;
            if (_popupSettings != null)
            {
                if (_popupSettings.CloseButton != null)
                    _popupSettings.CloseButton.onClick.AddListener(OnSettingsCloseClicked);
                if (_popupSettings.HomeButton != null)
                    _popupSettings.HomeButton.onClick.AddListener(OnSettingsHomeClicked);
                if (_popupSettings.ContinueButton != null)
                    _popupSettings.ContinueButton.onClick.AddListener(OnSettingsCloseClicked);
            }
        }

        public PopupGoldShop GoldShopPopup => _popupGoldShop;

        /// <summary>골드 상점 팝업 연결 + Close 버튼 와이어링</summary>
        public void SetGoldShopPopup(PopupGoldShop _popup)
        {
            _popupGoldShop = _popup;
            if (_popupGoldShop != null && _popupGoldShop.CloseButton != null)
                _popupGoldShop.CloseButton.onClick.AddListener(OnGoldShopCloseClicked);
        }

        /// <summary>나가기 확인 팝업 연결 + Home/Next 버튼 와이어링</summary>
        public void SetQuitPopup(PopupQuit _popup)
        {
            _popupQuit = _popup;
            if (_popupQuit != null)
            {
                if (_popupQuit.HomeButton != null)
                    _popupQuit.HomeButton.onClick.AddListener(OnQuitHomeClicked);
                if (_popupQuit.NextButton != null)
                    _popupQuit.NextButton.onClick.AddListener(OnQuitNextClicked);
                if (_popupQuit.ExitDuplicateButton != null)
                    _popupQuit.ExitDuplicateButton.onClick.AddListener(OnQuitNextClicked);
            }
        }

        #endregion

        #region Public — HUD 업데이트

        public void UpdateHolderInfo(int _holderCount, int _maxHolders)
        {
            if (_view != null) _view.SetHolderInfo(_holderCount, _maxHolders);
        }

        public void UpdateMagazineDisplay(int _holderId, int _remaining) { }

        public void ShowMoveCount(int _total, int _used)
        {
            if (_view != null) _view.SetMoveCount(_used, _total);
        }

        public void SetLevelInfo(int _levelId, int _packageId)
        {
            if (_view != null) _view.SetLevel(_levelId);
        }

        public void UpdateGoldDisplay(int _amount)
        {
            if (_view != null) _view.SetGold(_amount);
        }

        #endregion

        #region 버튼 이벤트

        // [2026-05-13] HUD popup-open/close 연출은 UIBase.OpenUI/CloseUI 에서 NotifyPopupOpened/Closed 로 중앙 트리거.
        // HUDController 의 별도 헬퍼/명시 호출은 제거 — popup OpenUI/CloseUI 만 호출하면 연출 자동 동작.

        private void OnSettingsClicked()
        {
            // PauseGame 제거 — timeScale=0이 UI 입력을 막을 수 있음
            if (_popupSettings != null) _popupSettings.OpenUI();
        }

        /// <summary>
        /// 인게임 플레이 중 안드로이드 백버튼 처리 (UX플로우 §5-3-0). 팝업이 하나도 열려있지 않을 때
        /// BackButtonRouter 가 호출. 우선순위:
        ///   1) 부스터 사용 중간 단계 → 부스터 사용 취소 (자원 소모 X, BoosterExecutor.CancelPendingBooster)
        ///   2) 온보딩 중 (Lv.5 클리어 전) → 인게임 세팅 팝업 (Quit 비노출 — PopupSettings 가 FtueGate 로 처리)
        ///   3) 온보딩 후 → Quit Level 확인 팝업
        /// </summary>
        public void HandleInGameBack()
        {
            // 1) 부스터 사용 중간 취소
            if (BoosterExecutor.HasInstance)
            {
                var be = BoosterExecutor.Instance;
                if (be.HasPendingBooster || be.IsAwaitingColorSelection
                    || be.IsAwaitingHolderSelection || be.IsAwaitingBalloonClick)
                {
                    be.CancelPendingBooster();
                    return;
                }
            }

            // 2) 온보딩 중 → 인게임 세팅 팝업 (Quit 버튼은 PopupSettings 가 온보딩 여부로 숨김)
            if (!FtueGate.IsOnboardingComplete)
            {
                if (_popupSettings != null) _popupSettings.OpenUI();
                return;
            }

            // 3) 온보딩 후 → Quit Level 확인 (Settings → Quit 과 동일 종착)
            // ROLLBACK_QUIT_MOVE_BASED_LIFE_20260619: 무브 0 → 경고팝업 없이 즉시 종료(하트 0, WS 유지).
            if (!HasUsedMoveThisLevel()) { QuitImmediateNoMove(); return; }
            if (_popupQuit != null) _popupQuit.OpenUI();
            else if (_popupSettings != null) _popupSettings.OpenUI();
        }

        private void OnSettingsCloseClicked()
        {
            if (_popupSettings != null) _popupSettings.CloseUI();
        }

        // ROLLBACK_QUIT_MOVE_BASED_LIFE_20260619: 이번 레벨에 '무브'(보관함 탭 → 다트 1개라도 레일 배치)가 있었는지.
        private bool HasUsedMoveThisLevel()
            => RailManager.HasInstance && RailManager.Instance.HasAnyDartPlacedThisLevel;

        // ROLLBACK_QUIT_MOVE_BASED_LIFE_20260619: 무브 0 즉시 종료 — 경고/실패 팝업 없음, 하트 0,
        //   WS 연승 유지(OnLevelAbandoned 미호출). 분석은 씬 재로드 시 quit_by_user 자동.
        private void QuitImmediateNoMove()
        {
            if (_popupSettings != null) _popupSettings.CloseUI();
            if (_popupQuit != null) _popupQuit.CloseUI();
            if (!GameManager.HasInstance) return;
            GameManager.Instance.ResumeGame();
            if (GameManager.IsTestPlayMode) GameManager.Instance.GoToMapMaker();
            else GameManager.Instance.GoToLobby();
        }

        private void OnSettingsHomeClicked()
        {
            if (_popupSettings != null) _popupSettings.CloseUI();

            // ROLLBACK_QUIT_MOVE_BASED_LIFE_20260619: 무브 0 → 경고팝업 없이 즉시 종료(하트 0, WS 유지).
            if (!HasUsedMoveThisLevel()) { QuitImmediateNoMove(); return; }

            // 나가기 확인 팝업이 있으면 표시(무브 1+), 없으면 바로 나가기
            if (_popupQuit != null)
            {
                _popupQuit.OpenUI();
            }
            else
            {
                // Fallback: 팝업 없으면 기존 동작
                if (GameManager.HasInstance)
                {
                    GameManager.Instance.ResumeGame();
                    if (GameManager.IsTestPlayMode)
                    {
                        GameManager.Instance.GoToMapMaker();
                    }
                    else
                    {
                        // [WS quit-fail 2026-06-10] 미클리어 중도 이탈 = 실패 — streak 리셋 + 로비 배수 드롭 연출 예약.
                        if (WinningStreakManager.HasInstance) WinningStreakManager.Instance.OnLevelAbandoned();
                        GameManager.Instance.GoToLobby();
                    }
                }
            }
        }

        private void OnGoldPlusClicked()
        {
            if (_popupGoldShop != null) _popupGoldShop.OpenUI();
            if (GameManager.HasInstance) GameManager.Instance.PauseGame();
        }

        private void OnGoldShopCloseClicked()
        {
            if (_popupGoldShop != null) _popupGoldShop.CloseUI();
            if (GameManager.HasInstance) GameManager.Instance.ResumeGame();
        }

        /// <summary>나가기 확인 → Home 버튼: 1차 클릭은 LoseLife→WinningStreak 토글, 2차 클릭에 Lobby/MapMaker로 이동</summary>
        private void OnQuitHomeClicked()
        {
            if (_popupQuit != null && _popupQuit.TryAdvanceHomeButton()) return;

            if (!GameManager.HasInstance) return;

            // 테스트 플레이: 기존대로 MapMaker 복귀(하트 무관).
            if (GameManager.IsTestPlayMode)
            {
                if (_popupQuit != null) _popupQuit.CloseUI();
                GameManager.Instance.ResumeGame();
                GameManager.Instance.GoToMapMaker();
                return;
            }

            // ROLLBACK_QUIT_MOVE_BASED_LIFE_20260619: 여기 도달 = 무브 1+ (무브 0 은 OnSettingsHomeClicked/HandleInGameBack
            //   에서 PopupQuit 없이 즉시 종료). 정책: 무브 1+ Quit = 실패 처리 → WS 연승 끊김 + 하트 1 소모 + Level Failed 팝업.
            //   ★PopupQuit 을 닫지 않는다★ — PauseManager pause 를 유지해 fail02 밑에서 board 가 진행(clear/fail)하는 것을 막는다.
            //   fail02(OnEnable)가 하트 1 소모 + Retry/Exit 처리. Retry/Exit 의 scene 전환 시 PopupQuit 파괴 →
            //   OnDisable→PauseManager.Resume 으로 pause 자동 균형. 분석은 씬 재로드 시 quit_by_user 자동.
            // [WS quit-fail 2026-06-10] 미클리어 중도 이탈 = 실패 — streak 리셋 + 로비 배수 드롭 연출 예약.
            if (WinningStreakManager.HasInstance) WinningStreakManager.Instance.OnLevelAbandoned();
            if (PopupManager.HasInstance && PopupManager.Instance.HasPopup("popup_fail02"))
            {
                PopupManager.Instance.ShowPopup("popup_fail02", priority: 60);
            }
            else
            {
                // 폴백: fail02 미등록 시 기존 동작(하트 미소모, 즉시 로비).
                if (_popupQuit != null) _popupQuit.CloseUI();
                GameManager.Instance.ResumeGame();
                GameManager.Instance.GoToLobby();
            }
        }

        /// <summary>나가기 확인 → Next 버튼: 팝업 닫고 게임 계속</summary>
        private void OnQuitNextClicked()
        {
            if (_popupQuit != null) _popupQuit.CloseUI();
            if (GameManager.HasInstance) GameManager.Instance.ResumeGame();
        }

        #endregion

        #region EventBus 핸들러

        private void HandleHolderSelected(OnHolderSelected _evt)
        {
            UpdateMagazineDisplay(_evt.holderId, _evt.magazineCount);
            RefreshOnRailCount();
        }

        private void HandleMagazineEmpty(OnMagazineEmpty _evt)
        {
            UpdateMagazineDisplay(_evt.holderId, 0);
            RefreshOnRailCount();
        }

        private void HandleHolderReturned(OnHolderReturned _evt)
        {
            UpdateMagazineDisplay(_evt.holderId, _evt.remainingMagazine);
            RefreshOnRailCount();
        }

        private void HandleLevelLoaded(OnLevelLoaded _evt)
        {
            SetLevelInfo(_evt.levelId, _evt.packageId);
            RefreshOnRailCount();
            if (CurrencyManager.HasInstance) UpdateGoldDisplay(CurrencyManager.Instance.Coins);

            // Apply difficulty tint to HUD
            ApplyDifficultyTint(_evt.levelId);

            // 진행률 초기화 — 0% 표시.
            RefreshProgress();
        }

        private void HandleHolderDeploymentDone(OnHolderDeploymentDone _evt)
        {
            // 보관함 배치 종료 시 진행도 동기화 (보드 상태 변동 시점).
            RefreshProgress();
        }

        private void HandleBalloonPopped(OnBalloonPopped _evt)
        {
            // 풍선이 터질 때마다 진행도 갱신.
            RefreshProgress();
        }

        /// <summary>
        /// 진행도 = 공격한 풍선 수(누적 PoppedCount) / 전체 풍선 수(PoppedCount + RemainingCount).
        /// Spawner로 추가된 풍선도 분모에 합산 → 모두 터트려야 100%.
        /// </summary>
        private void RefreshProgress()
        {
            if (_view == null) return;
            if (!BalloonController.HasInstance)
            {
                _view.SetProgress(0, 0);
                return;
            }

            int popped = BalloonController.Instance.PoppedCount;
            int remaining = BalloonController.Instance.RemainingCount;
            int total = popped + remaining;
            _view.SetProgress(popped, total);

            // achieve — 진행도 100% 도달 순간 1회(상승 엣지). 100% 미만으로 떨어지면(새 레벨) 리셋.
            bool atFull = total > 0 && popped >= total;
            if (atFull && !_progressWasFull && AudioManager.HasInstance)
                AudioManager.Instance.PlayAchieve();
            _progressWasFull = atFull;
        }
        private bool _progressWasFull;

        private void HandleCoinChanged(OnCoinChanged _evt)
        {
            UpdateGoldDisplay(_evt.currentCoins);
        }

        private void HandleGaugeStage(OnGaugeStageChanged _evt)
        {
            GaugeStage stage = (GaugeStage)_evt.currentStage;

            // Update HUD overlay color based on gauge stage
            if (_gaugeOverlay != null)
            {
                Color overlay = stage switch
                {
                    GaugeStage.Warning  => GAUGE_WARNING,
                    GaugeStage.Critical => GAUGE_CRITICAL,
                    GaugeStage.Caution  => GAUGE_CAUTION,
                    _                   => GAUGE_SAFE
                };
                _gaugeOverlay.color = overlay;
            }

            // Danger 알람 타일: Warning 이상에서 표시
            if (BoardTileManager.HasInstance)
            {
                bool showDanger = stage >= GaugeStage.Warning;
                BoardTileManager.Instance.SetDangerVisible(showDanger);
            }
        }

        private void RefreshOnRailCount()
        {
            if (!RailManager.HasInstance) return;
            int _onRail = RailManager.Instance.OccupiedCount;
            int _max = RailManager.Instance.SlotCount;
            UpdateHolderInfo(_onRail, _max);
        }

        /// <summary>
        /// Applies difficulty-based color tint to HUD elements.
        /// Design ref: BeatChart_Direction — Hard=amber, SuperHard=red HUD
        /// </summary>
        private void ApplyDifficultyTint(int levelId)
        {
            if (_view == null) return;

            LevelConfig cfg = null;
            if (LevelManager.HasInstance) cfg = LevelManager.Instance.CurrentLevel;
            if (cfg == null) return;

            Color tint = cfg.difficultyPurpose switch
            {
                DifficultyPurpose.Hard      => TINT_HARD,
                DifficultyPurpose.SuperHard  => TINT_SUPERHARD,
                _                            => TINT_NORMAL
            };

            // Apply tint to HUD background if available
            if (_view.BackgroundImage != null)
                _view.BackgroundImage.color = tint;

            // Lock 색상 반영
            _view.SetDifficulty(cfg.difficultyPurpose);
        }

        /// <summary>
        /// Binds the gauge overlay image for danger tinting.
        /// Called by GameBootstrap after UI creation.
        /// </summary>
        public void SetGaugeOverlay(Image overlay)
        {
            _gaugeOverlay = overlay;
            if (_gaugeOverlay != null) _gaugeOverlay.color = GAUGE_SAFE;
        }

        #endregion

    }
}
