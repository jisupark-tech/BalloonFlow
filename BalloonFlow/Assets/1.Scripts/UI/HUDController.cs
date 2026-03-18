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
        private Image _gaugeOverlay; // screen-edge warning overlay

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
        }

        private void OnDisable()
        {
            EventBus.Unsubscribe<OnHolderSelected>(HandleHolderSelected);
            EventBus.Unsubscribe<OnMagazineEmpty>(HandleMagazineEmpty);
            EventBus.Unsubscribe<OnHolderReturned>(HandleHolderReturned);
            EventBus.Unsubscribe<OnLevelLoaded>(HandleLevelLoaded);
            EventBus.Unsubscribe<OnCoinChanged>(HandleCoinChanged);
            EventBus.Unsubscribe<OnGaugeStageChanged>(HandleGaugeStage);

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
            }
            if (_popupGoldShop != null && _popupGoldShop.CloseButton != null)
                _popupGoldShop.CloseButton.onClick.RemoveListener(OnGoldShopCloseClicked);

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
            }
        }

        /// <summary>골드 상점 팝업 연결 + Close 버튼 와이어링</summary>
        public void SetGoldShopPopup(PopupGoldShop _popup)
        {
            _popupGoldShop = _popup;
            if (_popupGoldShop != null && _popupGoldShop.CloseButton != null)
                _popupGoldShop.CloseButton.onClick.AddListener(OnGoldShopCloseClicked);
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
            if (_popupSettings != null) _popupSettings.OpenUI();
            if (GameManager.HasInstance) GameManager.Instance.PauseGame();
        }

        private void OnSettingsCloseClicked()
        {
            if (_popupSettings != null) _popupSettings.CloseUI();
            if (GameManager.HasInstance) GameManager.Instance.ResumeGame();
        }

        private void OnSettingsHomeClicked()
        {
            if (_popupSettings != null) _popupSettings.CloseUI();
            if (GameManager.HasInstance)
            {
                GameManager.Instance.ResumeGame();
                if (GameManager.IsTestPlayMode)
                    GameManager.Instance.GoToMapMaker();
                else
                    GameManager.Instance.GoToLobby();
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
        }

        private void HandleCoinChanged(OnCoinChanged _evt)
        {
            UpdateGoldDisplay(_evt.currentCoins);
        }

        private void HandleGaugeStage(OnGaugeStageChanged _evt)
        {
            // Update HUD overlay color based on gauge stage
            if (_gaugeOverlay == null) return;

            GaugeStage stage = (GaugeStage)_evt.currentStage;
            Color overlay = stage switch
            {
                GaugeStage.Warning  => GAUGE_WARNING,
                GaugeStage.Critical => GAUGE_CRITICAL,
                GaugeStage.Caution  => GAUGE_CAUTION,
                _                   => GAUGE_SAFE
            };
            _gaugeOverlay.color = overlay;
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
