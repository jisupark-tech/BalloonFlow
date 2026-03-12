using UnityEngine;

namespace BalloonFlow
{
    /// <summary>
    /// Tracks score within a level. Calculates star thresholds based on
    /// balloon count: star1=base, star2=ceil(base*1.5), star3=ceil(base*2.2).
    /// </summary>
    /// <remarks>
    /// Layer: Core | Genre: Puzzle | Role: Manager | Phase: 0
    /// DB Reference: No DB match found — generated from L3 YAML logicFlow
    /// </remarks>
    public class ScoreManager : Singleton<ScoreManager>
    {
        #region Constants

        private const int POINTS_PER_BALLOON = 100;
        private const float STAR_2_MULTIPLIER = 1.5f;
        private const float STAR_3_MULTIPLIER = 2.2f;

        #endregion

        #region Fields

        private int _currentScore;
        private int _baseScore;
        private int _star1Threshold;
        private int _star2Threshold;
        private int _star3Threshold;

        #endregion

        #region Properties

        /// <summary>
        /// Current accumulated score for this level.
        /// </summary>
        public int CurrentScore => _currentScore;

        /// <summary>
        /// Base score for 1-star rating.
        /// </summary>
        public int BaseScore => _baseScore;

        /// <summary>
        /// Score threshold for 1 star.
        /// </summary>
        public int Star1Threshold => _star1Threshold;

        /// <summary>
        /// Score threshold for 2 stars.
        /// </summary>
        public int Star2Threshold => _star2Threshold;

        /// <summary>
        /// Score threshold for 3 stars.
        /// </summary>
        public int Star3Threshold => _star3Threshold;

        #endregion

        #region Lifecycle

        protected override void OnSingletonAwake()
        {
            EventBus.Subscribe<OnBalloonPopped>(HandleBalloonPopped);
            EventBus.Subscribe<OnBoardCleared>(HandleBoardCleared);
        }

        protected override void OnDestroy()
        {
            EventBus.Unsubscribe<OnBalloonPopped>(HandleBalloonPopped);
            EventBus.Unsubscribe<OnBoardCleared>(HandleBoardCleared);
            base.OnDestroy();
        }

        #endregion

        #region Public Methods

        /// <summary>
        /// Initializes score thresholds for a level based on balloon count.
        /// Call when a level is loaded.
        /// </summary>
        public void InitializeLevel(int balloonCount)
        {
            _currentScore = 0;
            _baseScore = balloonCount * POINTS_PER_BALLOON;
            _star1Threshold = _baseScore;
            _star2Threshold = Mathf.CeilToInt(_baseScore * STAR_2_MULTIPLIER);
            _star3Threshold = Mathf.CeilToInt(_baseScore * STAR_3_MULTIPLIER);
        }

        /// <summary>
        /// Adds score and publishes OnScoreChanged event.
        /// </summary>
        public void AddScore(int amount)
        {
            if (amount <= 0)
            {
                return;
            }

            _currentScore += amount;

            EventBus.Publish(new OnScoreChanged
            {
                currentScore = _currentScore,
                delta = amount
            });
        }

        /// <summary>
        /// Returns the current score.
        /// </summary>
        public int GetCurrentScore()
        {
            return _currentScore;
        }

        /// <summary>
        /// Returns how many stars the current score earns (0, 1, 2, or 3).
        /// </summary>
        public int GetStarCount()
        {
            return GetStarCountForScore(_currentScore);
        }

        /// <summary>
        /// Returns how many stars a given score earns.
        /// </summary>
        public int GetStarCountForScore(int score)
        {
            if (score >= _star3Threshold)
            {
                return 3;
            }
            if (score >= _star2Threshold)
            {
                return 2;
            }
            if (score >= _star1Threshold)
            {
                return 1;
            }
            return 0;
        }

        /// <summary>
        /// Returns the score needed for the next star (0 if already 3 stars).
        /// </summary>
        public int GetScoreToNextStar()
        {
            int currentStars = GetStarCount();
            switch (currentStars)
            {
                case 0:
                    return Mathf.Max(0, _star1Threshold - _currentScore);
                case 1:
                    return Mathf.Max(0, _star2Threshold - _currentScore);
                case 2:
                    return Mathf.Max(0, _star3Threshold - _currentScore);
                default:
                    return 0;
            }
        }

        /// <summary>
        /// Resets the score to zero.
        /// </summary>
        public void ResetScore()
        {
            _currentScore = 0;

            EventBus.Publish(new OnScoreChanged
            {
                currentScore = 0,
                delta = 0
            });
        }

        #endregion

        #region Private Methods

        private void HandleBalloonPopped(OnBalloonPopped evt)
        {
            AddScore(POINTS_PER_BALLOON);
        }

        private void HandleBoardCleared(OnBoardCleared evt)
        {
            // Board cleared event is informational; score is already accumulated
        }

        #endregion
    }
}
