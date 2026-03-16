using UnityEngine;
using UnityEngine.UI;

namespace BalloonFlow
{
    /// <summary>
    /// In-game HUD controller. Displays score, star progress, remaining balloons,
    /// on-rail holder info, and level info. Uses legacy UI.Text for SceneBuilder compatibility.
    /// </summary>
    /// <remarks>
    /// Layer: Game | Genre: Puzzle | Role: Controller | Phase: 3
    /// </remarks>
    public class HUDController : Singleton<HUDController>
    {
        #region Serialized Fields

        [Header("Score")]
        [SerializeField] private Text _scoreText;

        [Header("Stars")]
        [SerializeField] private Image[] _starImages;
        [SerializeField] private Sprite _starFilledSprite;
        [SerializeField] private Sprite _starEmptySprite;

        [Header("Balloons")]
        [SerializeField] private Text _remainingText;

        [Header("On-Rail Holders")]
        [SerializeField] private Text _holderCountText;
        [SerializeField] private Text _magazineCountText;

        [Header("Level Info")]
        [SerializeField] private Text _levelText;

        [Header("Move Count")]
        [SerializeField] private Text _moveCountText;

        #endregion

        #region Fields

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
        }

        private void OnDisable()
        {
            EventBus.Unsubscribe<OnScoreChanged>(HandleScoreChanged);
            EventBus.Unsubscribe<OnBoardStateChanged>(HandleBoardStateChanged);
            EventBus.Unsubscribe<OnHolderSelected>(HandleHolderSelected);
            EventBus.Unsubscribe<OnMagazineEmpty>(HandleMagazineEmpty);
            EventBus.Unsubscribe<OnHolderReturned>(HandleHolderReturned);
            EventBus.Unsubscribe<OnLevelLoaded>(HandleLevelLoaded);
        }

        #endregion

        #region Public Methods

        public void UpdateScore(int score)
        {
            if (_scoreText != null) _scoreText.text = score.ToString("N0");
        }

        public void UpdateStars(int count)
        {
            _currentStars = Mathf.Clamp(count, 0, 3);
            if (_starImages == null) return;

            for (int i = 0; i < _starImages.Length; i++)
            {
                if (_starImages[i] == null) continue;
                _starImages[i].sprite = (i < _currentStars) ? _starFilledSprite : _starEmptySprite;
            }
        }

        public void UpdateRemainingBalloons(int count)
        {
            if (_remainingText != null) _remainingText.text = $"Balloons: {count}";
        }

        public void UpdateHolderInfo(int holderCount, int maxHolders)
        {
            if (_holderCountText != null) _holderCountText.text = $"On Rail: {holderCount}/{maxHolders}";
        }

        public void UpdateMagazineDisplay(int holderId, int remaining)
        {
            if (_magazineCountText != null) _magazineCountText.text = remaining > 0 ? remaining.ToString() : "-";
        }

        public void ShowMoveCount(int total, int used)
        {
            if (_moveCountText != null) _moveCountText.text = $"{used}/{total}";
        }

        public void SetLevelInfo(int levelId, int packageId)
        {
            if (_levelText != null) _levelText.text = $"Level {levelId}";
        }

        #endregion

        #region Private Methods

        private void HandleScoreChanged(OnScoreChanged evt)
        {
            UpdateScore(evt.currentScore);
            if (ScoreManager.HasInstance) UpdateStars(ScoreManager.Instance.GetStarCount());
        }

        private void HandleBoardStateChanged(OnBoardStateChanged evt)
        {
            UpdateRemainingBalloons(evt.remainingBalloons);
        }

        private void HandleHolderSelected(OnHolderSelected evt)
        {
            UpdateMagazineDisplay(evt.holderId, evt.magazineCount);
            RefreshOnRailCount();
        }

        private void HandleMagazineEmpty(OnMagazineEmpty evt)
        {
            UpdateMagazineDisplay(evt.holderId, 0);
            RefreshOnRailCount();
        }

        private void HandleHolderReturned(OnHolderReturned evt)
        {
            UpdateMagazineDisplay(evt.holderId, evt.remainingMagazine);
            RefreshOnRailCount();
        }

        private void HandleLevelLoaded(OnLevelLoaded evt)
        {
            SetLevelInfo(evt.levelId, evt.packageId);
            UpdateScore(0);
            UpdateStars(0);
            RefreshOnRailCount();
        }

        private void RefreshOnRailCount()
        {
            if (!HolderVisualManager.HasInstance) return;
            int onRail = HolderVisualManager.Instance.GetOnRailCount();
            int max = HolderVisualManager.Instance.GetMaxOnRail();
            UpdateHolderInfo(onRail, max);
        }

        #endregion
    }
}
