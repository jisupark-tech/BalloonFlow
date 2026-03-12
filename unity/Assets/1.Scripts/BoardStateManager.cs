using UnityEngine;

namespace BalloonFlow
{
    /// <summary>
    /// Tracks overall board state and owns clear/fail condition evaluation.
    /// Subscribes to balloon and holder events; publishes OnBoardCleared / OnBoardFailed.
    /// </summary>
    /// <remarks>
    /// Layer: Domain | Genre: Puzzle | Role: Manager | Phase: 1
    /// DB Reference: LevelManager (Expert Puzzle score 0.6) — state tracking + EventBus subscribe/publish pattern;
    ///               GameManager (Expert Puzzle score 0.6) — IsPaused/state enum + fail/clear flow;
    ///               logicFlow from ingame_balloon_board.yaml contracts.
    /// </remarks>
    public class BoardStateManager : Singleton<BoardStateManager>
    {
        #region Constants

        /// <summary>Maximum holders allowed in waiting queue before overflow triggers fail.</summary>
        private const int HolderOverflowThreshold = 5;

        #endregion

        #region Fields

        private BoardState _currentState;
        private int _remainingBalloons;
        private int _currentLevelId;
        private int _holderWaitingCount;
        private bool _allHoldersEmpty;
        private bool _allMagazinesEmpty;

        #endregion

        #region Properties

        /// <summary>Current board state: Playing, Cleared, or Failed.</summary>
        public BoardState CurrentState => _currentState;

        /// <summary>Remaining non-popped balloon count (cached from last event).</summary>
        public int RemainingBalloons => _remainingBalloons;

        #endregion

        #region Lifecycle

        protected override void OnSingletonAwake()
        {
            _currentState       = BoardState.Playing;
            _remainingBalloons  = 0;
            _currentLevelId     = -1;
            _holderWaitingCount = 0;
            _allHoldersEmpty    = false;
            _allMagazinesEmpty  = false;
        }

        private void OnEnable()
        {
            EventBus.Subscribe<OnLevelLoaded>(HandleLevelLoaded);
            EventBus.Subscribe<OnBalloonPopped>(HandleBalloonPopped);
            EventBus.Subscribe<OnBalloonSpawned>(HandleBalloonSpawned);
            EventBus.Subscribe<OnHolderOverflow>(HandleHolderOverflow);
            EventBus.Subscribe<OnMagazineEmpty>(HandleMagazineEmpty);
            EventBus.Subscribe<OnAllHoldersEmpty>(HandleAllHoldersEmpty);
        }

        private void OnDisable()
        {
            EventBus.Unsubscribe<OnLevelLoaded>(HandleLevelLoaded);
            EventBus.Unsubscribe<OnBalloonPopped>(HandleBalloonPopped);
            EventBus.Unsubscribe<OnBalloonSpawned>(HandleBalloonSpawned);
            EventBus.Unsubscribe<OnHolderOverflow>(HandleHolderOverflow);
            EventBus.Unsubscribe<OnMagazineEmpty>(HandleMagazineEmpty);
            EventBus.Unsubscribe<OnAllHoldersEmpty>(HandleAllHoldersEmpty);
        }

        #endregion

        #region Public Methods

        /// <summary>
        /// Initializes the board for a new level. Resets all state tracking.
        /// </summary>
        /// <param name="levelId">Level identifier for events.</param>
        /// <param name="initialBalloonCount">Number of balloons placed at level start.</param>
        public void InitializeBoard(int levelId, int initialBalloonCount)
        {
            _currentLevelId     = levelId;
            _remainingBalloons  = initialBalloonCount;
            _currentState       = BoardState.Playing;
            _holderWaitingCount = 0;
            _allHoldersEmpty    = false;
            _allMagazinesEmpty  = false;

            PublishBoardStateChanged();
            Debug.Log($"[BoardStateManager] Board initialized. Level={levelId}, Balloons={initialBalloonCount}");
        }

        /// <summary>
        /// Returns the current BoardState enum value.
        /// </summary>
        public BoardState GetBoardState()
        {
            return _currentState;
        }

        /// <summary>
        /// Returns the cached remaining balloon count.
        /// </summary>
        public int GetRemainingBalloons()
        {
            return _remainingBalloons;
        }

        /// <summary>
        /// Returns true if no balloons remain on the board.
        /// </summary>
        public bool IsBoardClear()
        {
            return _remainingBalloons <= 0;
        }

        /// <summary>
        /// Evaluates both fail conditions and returns a FailResult.
        /// Does not change board state — only evaluates.
        /// </summary>
        public FailResult CheckFailCondition()
        {
            // Condition 1: Holder overflow — immediate fail
            if (_holderWaitingCount > HolderOverflowThreshold)
            {
                return new FailResult
                {
                    isFail = true,
                    reason = FailReason.HolderOverflow
                };
            }

            // Condition 2: No moves left — all holders deployed AND all magazines empty AND balloons remain
            if (_allHoldersEmpty && _allMagazinesEmpty && _remainingBalloons > 0)
            {
                return new FailResult
                {
                    isFail = true,
                    reason = FailReason.NoMovesLeft
                };
            }

            return new FailResult { isFail = false, reason = FailReason.None };
        }

        #endregion

        #region Private Methods — Event Handlers

        private void HandleLevelLoaded(OnLevelLoaded evt)
        {
            // BalloonController calls InitializeBoard after layout is ready;
            // we just cache the level id here as a redundancy guard.
            _currentLevelId = evt.levelId;
        }

        private void HandleBalloonPopped(OnBalloonPopped evt)
        {
            // Always update remaining count, even if state is Failed.
            // Clear takes absolute priority: if all balloons are popped, it's a win
            // regardless of any prior fail condition (e.g. NoMovesLeft triggered while
            // the last dart was still in flight).
            if (BalloonController.HasInstance)
            {
                _remainingBalloons = BalloonController.Instance.GetRemainingCount();
            }
            else
            {
                _remainingBalloons = Mathf.Max(0, _remainingBalloons - 1);
            }

            PublishBoardStateChanged();

            // Allow clear check even from Failed state — clear always wins
            if (_currentState == BoardState.Playing || _currentState == BoardState.Failed)
            {
                EvaluateClearCondition();
            }
        }

        private void HandleBalloonSpawned(OnBalloonSpawned evt)
        {
            if (_currentState != BoardState.Playing) return;

            // A Spawner gimmick added a new balloon — increment count
            _remainingBalloons++;
            PublishBoardStateChanged();
        }

        private void HandleHolderOverflow(OnHolderOverflow evt)
        {
            if (_currentState != BoardState.Playing) return;

            _holderWaitingCount = evt.holderCount;

            if (_holderWaitingCount > HolderOverflowThreshold)
            {
                TriggerFail(FailReason.HolderOverflow);
            }
        }

        private void HandleMagazineEmpty(OnMagazineEmpty evt)
        {
            if (_currentState != BoardState.Playing) return;

            // Track that at least one magazine is now empty.
            // Full no-moves check occurs when all holders are also empty.
            EvaluateNoMovesCondition();
        }

        private void HandleAllHoldersEmpty(OnAllHoldersEmpty evt)
        {
            if (_currentState != BoardState.Playing) return;

            _allHoldersEmpty = true;
            EvaluateNoMovesCondition();
        }

        #endregion

        #region Private Methods — Condition Evaluation

        private void EvaluateClearCondition()
        {
            if (!IsBoardClear()) return;

            int score     = ScoreManager.HasInstance ? ScoreManager.Instance.CurrentScore : 0;
            int starCount = ScoreManager.HasInstance ? ScoreManager.Instance.GetStarCountForScore(score) : 0;

            _currentState = BoardState.Cleared;

            EventBus.Publish(new OnBoardCleared
            {
                levelId   = _currentLevelId,
                score     = score,
                starCount = starCount
            });

            Debug.Log($"[BoardStateManager] Board cleared! Level={_currentLevelId}, Score={score}, Stars={starCount}");
        }

        private void EvaluateNoMovesCondition()
        {
            // No-moves fail: all holders empty + all magazines empty + balloons still on board
            if (!_allHoldersEmpty) return;

            // Check if all magazines are empty via BalloonController-independent logic.
            // The OnMagazineEmpty event fires per holder. We rely on OnAllHoldersEmpty
            // as the definitive "no more shots available" signal.
            _allMagazinesEmpty = true;

            if (_remainingBalloons > 0)
            {
                TriggerFail(FailReason.NoMovesLeft);
            }
        }

        private void TriggerFail(FailReason reason)
        {
            if (_currentState != BoardState.Playing) return;

            _currentState = BoardState.Failed;

            string reasonText = reason == FailReason.HolderOverflow
                ? "HolderOverflow"
                : "NoMovesLeft";

            EventBus.Publish(new OnBoardFailed
            {
                levelId = _currentLevelId,
                reason  = reasonText
            });

            Debug.Log($"[BoardStateManager] Board failed! Level={_currentLevelId}, Reason={reasonText}");
        }

        private void PublishBoardStateChanged()
        {
            EventBus.Publish(new OnBoardStateChanged
            {
                remainingBalloons = _remainingBalloons
            });
        }

        #endregion
    }

    // ─────────────────────────────────────────────
    // Data Types
    // ─────────────────────────────────────────────

    /// <summary>
    /// Describes the current overall state of the game board.
    /// </summary>
    public enum BoardState
    {
        Playing,
        Cleared,
        Failed
    }

    /// <summary>
    /// Fail reason codes for CheckFailCondition / TriggerFail.
    /// </summary>
    public enum FailReason
    {
        None,
        HolderOverflow,
        NoMovesLeft
    }

    /// <summary>
    /// Result returned from BoardStateManager.CheckFailCondition().
    /// </summary>
    public struct FailResult
    {
        public bool isFail;
        public FailReason reason;
    }
}
