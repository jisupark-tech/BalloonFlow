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
        // 이어하기 제거량은 RailManager.GetContinueRemoveCount()로 결정 (허용량 기반)

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
            // 1) 레일 위 다트 중 가장 많은 색을 찾아 그 색 다트 중 10%만 제거.
            //    동일 색의 풍선도 제거된 다트 수와 동일하게 제거 (BoardStateManager.HandleContinueApplied 처리).
            //    최소 1개는 제거 (다트가 1~9개라도 부분적 도움 보장).
            int dartsRemoved = 0;
            int removedColor = -1;
            if (RailManager.HasInstance)
            {
                removedColor = RailManager.Instance.FindMostNumerousDartColor();
                if (removedColor >= 0)
                {
                    int totalCount = RailManager.Instance.CountDartsByColor(removedColor);
                    int targetRemove = Mathf.Max(1, Mathf.CeilToInt(totalCount * 0.1f));
                    dartsRemoved = RailManager.Instance.RemoveDartsByColor(removedColor, targetRemove);
                    Debug.Log($"[ContinueHandler] Removed {dartsRemoved}/{totalCount} darts of color {removedColor} (10% of most numerous).");
                }
                else
                {
                    Debug.Log("[ContinueHandler] No darts on rail — skipping rail clear.");
                }
            }

            // 2) 배포중인 holder는 그대로 유지 (계속 배포).
            //    대기 중/이동 중인 holder만 큐로 복귀.
            if (HolderManager.HasInstance)
            {
                ReturnWaitingHoldersToQueue();
            }

            // 3) 제거된 다트와 동일 색 풍선 제거 (BoardStateManager.HandleContinueApplied가 처리)
            EventBus.Publish(new OnContinueApplied
            {
                dartsRemoved = dartsRemoved,
                removedColor = removedColor,
                levelId = _currentLevelId
            });

            // 4) 보드 상태 리셋하여 게임플레이 재개
            if (BoardStateManager.HasInstance)
            {
                int remaining = BoardStateManager.Instance.GetRemainingBalloons();
                BoardStateManager.Instance.InitializeBoard(_currentLevelId, remaining);
            }
        }

        /// <summary>
        /// 대기 중/이동 중인 holder만 큐로 복귀. 배포 중인 holder는 그대로 유지하여 남은 magazine 계속 배치.
        /// 사용자 spec: "배포중인 다트는 이어서 배포해야하는데"
        /// </summary>
        private void ReturnWaitingHoldersToQueue()
        {
            HolderData[] holders = HolderManager.Instance.GetHolders();
            if (holders == null) return;

            int returned = 0;
            for (int i = 0; i < holders.Length; i++)
            {
                if (holders[i].isConsumed) continue;
                // 배포 중인 holder는 그대로 유지 — 남은 mag 계속 배치
                if (holders[i].isDeploying) continue;

                if (holders[i].isWaiting || holders[i].isMovingToRail)
                {
                    // Cancel visual coroutine BEFORE resetting data (prevents 1-frame stale dart placement)
                    if (HolderVisualManager.HasInstance)
                        HolderVisualManager.Instance.CancelDeployAndReturnToQueue(holders[i].holderId);

                    HolderManager.Instance.UndoDeploy(holders[i].holderId);
                    returned++;
                }
            }

            if (returned > 0)
            {
                Debug.Log($"[ContinueHandler] Returned {returned} waiting/moving holders to queue (active deploying holders preserved).");
            }
        }

        private void HandleLevelLoaded(OnLevelLoaded evt)
        {
            _currentLevelId = evt.levelId;
            ResetContinueCount();
        }

        private void HandleBoardFailed(OnBoardFailed evt)
        {
            // 실패 흐름: PopupFail01 → PopupContinue → PopupFail02
            // ContinueHandler는 팝업을 직접 띄우지 않음.
            // PopupFail01이 먼저 표시되고, Decline 시 PopupContinue를 띄움.
            if (!CanContinue()) return;

            if (PopupManager.HasInstance)
                PopupManager.Instance.ShowPopup("popup_fail01", priority: 50);

            Debug.Log($"[ContinueHandler] Board failed — showing PopupFail01. ContinueCount={_continueCount}/{MaxContinues}");
        }

        #endregion
    }
}
