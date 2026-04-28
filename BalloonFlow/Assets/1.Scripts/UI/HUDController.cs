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

        private void OnSettingsClicked()
        {
            // PauseGame 제거 — timeScale=0이 UI 입력을 막을 수 있음
            if (_popupSettings != null) _popupSettings.OpenUI();
        }

        private void OnSettingsCloseClicked()
        {
            if (_popupSettings != null) _popupSettings.CloseUI();
        }

        private void OnSettingsHomeClicked()
        {
            if (_popupSettings != null) _popupSettings.CloseUI();

            // 나가기 확인 팝업이 있으면 표시, 없으면 바로 나가기
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
                        GameManager.Instance.GoToMapMaker();
                    else
                        GameManager.Instance.GoToLobby();
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

        /// <summary>나가기 확인 → Home 버튼: Lobby 또는 MapMaker로 이동</summary>
        private void OnQuitHomeClicked()
        {
            if (_popupQuit != null) _popupQuit.CloseUI();
            if (GameManager.HasInstance)
            {
                GameManager.Instance.ResumeGame();
                if (GameManager.IsTestPlayMode)
                    GameManager.Instance.GoToMapMaker();
                else
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
            // 보관함 하나의 magazine 이 0이 됨 → 진행도 갱신.
            RefreshProgress();
        }

        /// <summary>
        /// 진행도 = 탄창 소진(magazine=0 또는 isConsumed=true)된 보관함 수 / 전체 보관함 수.
        /// EX. 5레벨 20개 보관함 중 3개 소진 = 3/20 = 15%.
        /// </summary>
        private void RefreshProgress()
        {
            if (_view == null) return;
            if (!HolderManager.HasInstance)
            {
                _view.SetProgress(0, 0);
                return;
            }

            HolderData[] holders = HolderManager.Instance.GetHolders();
            if (holders == null || holders.Length == 0)
            {
                _view.SetProgress(0, 0);
                return;
            }

            int consumed = 0;
            for (int i = 0; i < holders.Length; i++)
            {
                if (holders[i] == null) continue;
                // isConsumed = 라이프사이클 종료 (regular: magazine=0 / spawner: spawnerHP=0).
                // magazineCount 직접 검사는 spawner(magazine=0 시작) false-positive 발생 → 사용 안 함.
                if (holders[i].isConsumed) consumed++;
            }
            _view.SetProgress(consumed, holders.Length);
        }

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
