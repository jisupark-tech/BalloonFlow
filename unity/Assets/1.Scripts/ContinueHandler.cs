using UnityEngine;

namespace BalloonFlow
{
    /// <summary>
    /// Handles "continue after fail" with free first + coin-based escalating costs.
    /// Design ref: 아웃게임디렉션 §이어하기
    ///   1st continue: FREE
    ///   2nd: 900 coins
    ///   3rd: 1900 coins
    ///   4th: 2900 coins (max)
    /// Restart resets cost back to free.
    /// </summary>
    /// <remarks>
    /// Layer: Domain | Genre: Puzzle | Role: Handler | Phase: 3
    /// </remarks>
    public class ContinueHandler : Singleton<ContinueHandler>
    {
        #region Constants

        private const int MaxContinues = 4;  // 1 free + 3 paid
        private const int ContinueMagazineBonus = 5;

        // Escalating coin costs (index 0 = free, then 900 → 1900 → 2900)
        private static readonly int[] ContinueCosts = { 0, 900, 1900, 2900 };

        #endregion

        #region Fields

        private int _continueCount;
        private int _currentLevelId;

        #endregion

        #region Properties

        public int ContinueCount => _continueCount;

        #endregion

        #region Lifecycle

        protected override void OnSingletonAwake()
        {
            _continueCount = 0;
            _currentLevelId = -1;
        }

        private void OnEnable()
        {
            EventBus.Subscribe<OnLevelLoaded>(HandleLevelLoaded);
            EventBus.Subscribe<OnBoardFailed>(HandleBoardFailed);
        }

        private void OnDisable()
        {
            EventBus.Unsubscribe<OnLevelLoaded>(HandleLevelLoaded);
            EventBus.Unsubscribe<OnBoardFailed>(HandleBoardFailed);
        }

        #endregion

        #region Public Methods

        /// <summary>
        /// Returns true if the player is eligible to continue (under max limit).
        /// </summary>
        public bool CanContinue()
        {
            return _continueCount < MaxContinues;
        }

        /// <summary>
        /// Returns true if the next continue is free (first continue).
        /// </summary>
        public bool IsNextContinueFree()
        {
            return _continueCount < ContinueCosts.Length && ContinueCosts[_continueCount] == 0;
        }

        /// <summary>
        /// Returns the coin cost of the next continue.
        /// Returns 0 for the first (free) continue.
        /// </summary>
        public int GetContinueCost()
        {
            if (_continueCount >= ContinueCosts.Length)
                return ContinueCosts[ContinueCosts.Length - 1];
            return ContinueCosts[_continueCount];
        }

        /// <summary>
        /// Attempts to execute a continue. Free for the first, coin cost for subsequent.
        /// Returns true if continue succeeded.
        /// </summary>
        public bool Continue()
        {
            if (!CanContinue())
            {
                Debug.LogWarning("[ContinueHandler] Max continues reached.");
                return false;
            }

            int cost = GetContinueCost();

            if (cost > 0)
            {
                if (!CurrencyManager.HasInstance)
                {
                    Debug.LogWarning("[ContinueHandler] CurrencyManager not available.");
                    return false;
                }

                if (!CurrencyManager.Instance.SpendCoins(cost, CurrencyManager.CoinSink.Continue))
                {
                    Debug.LogWarning($"[ContinueHandler] Not enough coins to continue (needs {cost}).");
                    return false;
                }
            }

            _continueCount++;
            ApplyContinueRestore();

            string costLabel = cost > 0 ? $"{cost} coins" : "FREE";
            Debug.Log($"[ContinueHandler] Continue #{_continueCount} applied. Cost={costLabel}.");
            return true;
        }

        /// <summary>
        /// Returns the number of continues used in the current level attempt.
        /// </summary>
        public int GetContinueCount()
        {
            return _continueCount;
        }

        /// <summary>
        /// Resets the continue count (called on new level or explicit retry).
        /// Design: restart resets cost back to free.
        /// </summary>
        public void ResetContinueCount()
        {
            _continueCount = 0;
        }

        #endregion

        #region Private Methods

        private void ApplyContinueRestore()
        {
            // Signal HolderManager to add bonus magazines (holderId -1 = all holders)
            EventBus.Publish(new OnHolderReturned
            {
                holderId = -1,
                remainingMagazine = ContinueMagazineBonus
            });

            // Reset board state so gameplay resumes
            if (BoardStateManager.HasInstance)
            {
                int remaining = BoardStateManager.Instance.GetRemainingBalloons();
                BoardStateManager.Instance.InitializeBoard(_currentLevelId, remaining);
            }
        }

        private void HandleLevelLoaded(OnLevelLoaded evt)
        {
            _currentLevelId = evt.levelId;
            ResetContinueCount();
        }

        private void HandleBoardFailed(OnBoardFailed evt)
        {
            if (CanContinue() && PopupManager.HasInstance)
            {
                PopupManager.Instance.ShowPopup("popup_continue", priority: 10);
            }
        }

        #endregion
    }
}
