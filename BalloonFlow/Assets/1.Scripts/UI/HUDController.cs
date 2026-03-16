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
        #region Fields

        private UIHud _view;
        private PopupSettings _popupSettings;
        private PopupGoldShop _popupGoldShop;
        private int _currentStars;

        #endregion

        #region Lifecycle

        private void OnEnable()
        {
            EventBus.Subscribe<OnScoreChanged>(HandleScoreChanged);
            EventBus.Subscribe<OnBoardStateChanged>(HandleBoardStateChanged);
            EventBus.Subscribe<OnHolderSelected>(HandleHolderSelected);
            EventBus.Subscribe<OnMagazineEmpty>(HandleMagazineEmpty);
            EventBus.Subscribe<OnHolderReturned>(HandleHolderReturned);
            EventBus.Subscribe<OnLevelLoaded>(HandleLevelLoaded);
            EventBus.Subscribe<OnCoinChanged>(HandleCoinChanged);
        }

        private void OnDisable()
        {
            EventBus.Unsubscribe<OnScoreChanged>(HandleScoreChanged);
            EventBus.Unsubscribe<OnBoardStateChanged>(HandleBoardStateChanged);
            EventBus.Unsubscribe<OnHolderSelected>(HandleHolderSelected);
            EventBus.Unsubscribe<OnMagazineEmpty>(HandleMagazineEmpty);
            EventBus.Unsubscribe<OnHolderReturned>(HandleHolderReturned);
            EventBus.Unsubscribe<OnLevelLoaded>(HandleLevelLoaded);
            EventBus.Unsubscribe<OnCoinChanged>(HandleCoinChanged);

            // 버튼 이벤트 해제
            if (_view != null)
            {
                if (_view.SettingsButton != null) _view.SettingsButton.onClick.RemoveListener(OnSettingsClicked);
                if (_view.GoldPlusButton != null) _view.GoldPlusButton.onClick.RemoveListener(OnGoldPlusClicked);
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
        }

        /// <summary>설정 팝업 연결</summary>
        public void SetSettingsPopup(PopupSettings _popup)
        {
            _popupSettings = _popup;
        }

        /// <summary>골드 상점 팝업 연결</summary>
        public void SetGoldShopPopup(PopupGoldShop _popup)
        {
            _popupGoldShop = _popup;
        }

        #endregion

        #region Public — HUD 업데이트

        public void UpdateScore(int _score)
        {
            if (_view != null) _view.SetScore(_score);
        }

        public void UpdateStars(int _count)
        {
            _currentStars = Mathf.Clamp(_count, 0, 3);
        }

        public void UpdateRemainingBalloons(int _count)
        {
            if (_view != null) _view.SetRemainingBalloons(_count);
        }

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

        private void HandleScoreChanged(OnScoreChanged _evt)
        {
            UpdateScore(_evt.currentScore);
            if (ScoreManager.HasInstance) UpdateStars(ScoreManager.Instance.GetStarCount());
        }

        private void HandleBoardStateChanged(OnBoardStateChanged _evt)
        {
            UpdateRemainingBalloons(_evt.remainingBalloons);
        }

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
            UpdateScore(0);
            UpdateStars(0);
            RefreshOnRailCount();
            if (CurrencyManager.HasInstance) UpdateGoldDisplay(CurrencyManager.Instance.Coins);
        }

        private void HandleCoinChanged(OnCoinChanged _evt)
        {
            UpdateGoldDisplay(_evt.currentCoins);
        }

        private void RefreshOnRailCount()
        {
            if (!HolderVisualManager.HasInstance) return;
            int _onRail = HolderVisualManager.Instance.GetOnRailCount();
            int _max = HolderVisualManager.Instance.GetMaxOnRail();
            UpdateHolderInfo(_onRail, _max);
        }

        #endregion
    }
}
