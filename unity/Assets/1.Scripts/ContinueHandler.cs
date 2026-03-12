using UnityEngine;

namespace BalloonFlow
{
    /// <summary>
    /// Handles "continue after fail" with gem-based escalating costs.
    /// Design: cost = 30 + (continue_count - 1) * 10 gems.
    /// 1st continue = 30 gem, 2nd = 40, 3rd = 50, 4th(max) = 60.
    /// </summary>
    /// <remarks>
    /// Layer: Domain | Genre: Puzzle | Role: Handler | Phase: 3
    /// Design ref: outgame_life_booster.yaml §106-111, economy.yaml §gem_sinks
    /// </remarks>
    public class ContinueHandler : Singleton<ContinueHandler>
    {
        #region Constants

        private const int MaxContinues = 4;
        private const int BaseContinueCostGems = 30;
        private const int CostEscalationPerContinue = 10;
        private const int ContinueMagazineBonus = 5;

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
        /// Returns the gem cost of the next continue.
        /// Formula: 30 + continueCount * 10.
        /// </summary>
        public int GetContinueCost()
        {
            return BaseContinueCostGems + (_continueCount * CostEscalationPerContinue);
        }

        /// <summary>
        /// Attempts to execute a continue. Deducts gems, then restores board state.
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

            if (!GemManager.HasInstance)
            {
                Debug.LogWarning("[ContinueHandler] GemManager not available.");
                return false;
            }

            if (!GemManager.Instance.SpendGems(cost, GemManager.GemSink.Continue))
            {
                Debug.LogWarning($"[ContinueHandler] Not enough gems to continue (needs {cost}).");
                return false;
            }

            _continueCount++;
            ApplyContinueRestore();

            Debug.Log($"[ContinueHandler] Continue #{_continueCount} applied. Cost={cost} gems.");
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
