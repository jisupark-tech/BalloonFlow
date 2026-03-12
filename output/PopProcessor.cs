using System;
using UnityEngine;

namespace BalloonFlow
{
    /// <summary>
    /// Result of a pop processing attempt (scoring and combo data).
    /// Distinct from BalloonController.PopResult which covers balloon-level pop status.
    /// </summary>
    public struct PopProcessorResult
    {
        public bool success;
        public int balloonId;
        public int color;
        public Vector3 position;
        public int scoreAwarded;
        public int comboCount;
    }

    /// <summary>
    /// Processes balloon pops when a dart hits a matching balloon.
    /// Tracks combo count within a deployment sequence and awards score.
    /// Publishes OnBalloonPopped and OnComboIncremented events for
    /// ScoreManager and FeedbackController consumption.
    /// </summary>
    /// <remarks>
    /// Layer: Domain | Genre: Puzzle | Role: Processor | Phase: 1
    /// DB Reference: No DB match found — generated from L3 YAML logicFlow (ingame_holder_dart_pop)
    ///
    /// popIndex (combo) is the sequential hit number within a single deployment.
    /// FeedbackController uses popIndex for pitch-ascending SFX.
    /// No chain-pop mechanic — strictly 1:1 dart-to-balloon hits.
    /// </remarks>
    public class PopProcessor : Singleton<PopProcessor>
    {
        #region Constants

        private const int BASE_POP_SCORE = 100;
        private const int COMBO_BONUS_PER_HIT = 10;

        #endregion

        #region Fields

        private int _popCount;
        private int _comboCount;
        private int _currentDeploymentHolderId = -1;

        #endregion

        #region Properties

        /// <summary>
        /// Total number of balloons popped in the current level.
        /// </summary>
        public int PopCount => _popCount;

        /// <summary>
        /// Current combo count within the active deployment.
        /// Resets when a new deployment starts.
        /// </summary>
        public int ComboCount => _comboCount;

        #endregion

        #region Lifecycle

        protected override void OnSingletonAwake()
        {
            _popCount = 0;
            _comboCount = 0;
        }

        private void OnEnable()
        {
            EventBus.Subscribe<OnDartHitBalloon>(HandleDartHitBalloon);
            EventBus.Subscribe<OnHolderSelected>(HandleHolderSelected);
            EventBus.Subscribe<OnDeploymentComplete>(HandleDeploymentComplete);
        }

        private void OnDisable()
        {
            EventBus.Unsubscribe<OnDartHitBalloon>(HandleDartHitBalloon);
            EventBus.Unsubscribe<OnHolderSelected>(HandleHolderSelected);
            EventBus.Unsubscribe<OnDeploymentComplete>(HandleDeploymentComplete);
        }

        #endregion

        #region Public Methods

        /// <summary>
        /// Processes a balloon pop. Verifies the balloon exists and matches the
        /// dart color, then awards score with combo bonus.
        /// </summary>
        /// <param name="balloonId">ID of the balloon to pop.</param>
        /// <param name="dartColor">Color of the dart that hit.</param>
        /// <returns>PopProcessorResult with success status, score awarded, and combo count.</returns>
        public PopProcessorResult ProcessPop(int balloonId, int dartColor)
        {
            PopProcessorResult result = new PopProcessorResult
            {
                success = false,
                balloonId = balloonId,
                color = dartColor,
                position = Vector3.zero,
                scoreAwarded = 0,
                comboCount = _comboCount
            };

            // Verify balloon exists and check color via BalloonController
            if (!BalloonController.HasInstance)
            {
                Debug.LogError("[PopProcessor] BalloonController not available.");
                return result;
            }

            BalloonData balloonData = BalloonController.Instance.GetBalloon(balloonId);
            if (balloonData == null)
            {
                Debug.LogWarning($"[PopProcessor] Balloon {balloonId} not found.");
                return result;
            }

            if (balloonData.isPopped)
            {
                Debug.LogWarning($"[PopProcessor] Balloon {balloonId} already popped.");
                return result;
            }

            if (balloonData.color != dartColor)
            {
                Debug.LogWarning($"[PopProcessor] Color mismatch: dart={dartColor}, balloon={balloonData.color}.");
                return result;
            }

            // Execute pop via BalloonController (handles gimmick logic, pool return, etc.)
            PopResult popResult = BalloonController.Instance.PopBalloon(balloonId);
            if (!popResult.success)
            {
                Debug.LogWarning($"[PopProcessor] BalloonController.PopBalloon failed: {popResult.reason}");
                return result;
            }

            Vector3 balloonPosition = popResult.position;

            // Increment combo
            _comboCount++;
            _popCount++;

            // Calculate combo bonus only. Base score (100) is awarded by ScoreManager
            // via its OnBalloonPopped subscription (published by BalloonController.PopBalloon).
            int comboBonus = (_comboCount - 1) * COMBO_BONUS_PER_HIT;
            int totalScoreAwarded = BASE_POP_SCORE + comboBonus;

            // Add combo bonus on top of the base score that ScoreManager auto-awards
            if (comboBonus > 0 && ScoreManager.HasInstance)
            {
                ScoreManager.Instance.AddScore(comboBonus);
            }

            // NOTE: OnBalloonPopped is already published by BalloonController.PopBalloon().
            // We do NOT re-publish it here to avoid double events.

            // Publish combo increment
            EventBus.Publish(new OnComboIncremented
            {
                comboCount = _comboCount
            });

            // Publish pop complete with popIndex for FeedbackController pitch
            EventBus.Publish(new OnPopComplete
            {
                balloonId = balloonId,
                color = dartColor,
                position = balloonPosition,
                popIndex = _comboCount
            });

            result.success = true;
            result.position = balloonPosition;
            result.scoreAwarded = totalScoreAwarded;
            result.comboCount = _comboCount;

            return result;
        }

        /// <summary>
        /// Returns the total number of balloons popped this level.
        /// </summary>
        public int GetPopCount()
        {
            return _popCount;
        }

        /// <summary>
        /// Resets pop and combo counters for a new level.
        /// </summary>
        public void ResetAll()
        {
            _popCount = 0;
            _comboCount = 0;
            _currentDeploymentHolderId = -1;
        }

        #endregion

        #region Private Methods

        /// <summary>
        /// Handles dart-balloon hit events from DartManager.
        /// Looks up the dart color and processes the pop.
        /// </summary>
        private void HandleDartHitBalloon(OnDartHitBalloon evt)
        {
            // Determine dart color from DartManager's active dart data
            int dartColor = -1;

            if (DartManager.HasInstance)
            {
                DartData[] activeDarts = DartManager.Instance.GetActiveDarts();
                if (activeDarts != null)
                {
                    for (int i = 0; i < activeDarts.Length; i++)
                    {
                        if (activeDarts[i].dartId == evt.dartId)
                        {
                            dartColor = activeDarts[i].color;
                            break;
                        }
                    }
                }
            }

            // Fallback: try to get color from the current holder
            if (dartColor < 0 && HolderManager.HasInstance)
            {
                HolderData currentHolder = HolderManager.Instance.GetCurrentHolder();
                if (currentHolder != null)
                {
                    dartColor = currentHolder.color;
                }
            }

            if (dartColor < 0)
            {
                Debug.LogWarning($"[PopProcessor] Could not determine dart color for dartId {evt.dartId}.");
                return;
            }

            ProcessPop(evt.balloonId, dartColor);
        }

        /// <summary>
        /// When a new holder is selected, reset the combo counter for the new deployment.
        /// </summary>
        private void HandleHolderSelected(OnHolderSelected evt)
        {
            _currentDeploymentHolderId = evt.holderId;
            _comboCount = 0;
        }

        /// <summary>
        /// When a deployment completes, finalize the combo sequence.
        /// </summary>
        private void HandleDeploymentComplete(OnDeploymentComplete evt)
        {
            // Combo resets on next deployment; no action needed here
            _currentDeploymentHolderId = -1;
        }

        // Balloon lookup delegated to BalloonController.Instance.GetBalloon()
        // No local search needed — BalloonController owns the balloon registry.

        #endregion
    }
}
