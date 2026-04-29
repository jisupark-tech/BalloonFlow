using UnityEngine;

namespace BalloonFlow
{
    /// <summary>
    /// Handles "continue after fail" with free first + coin-based escalating costs.
    /// Design ref: 아웃게임디렉션 §이어하기
    ///   1st continue: FREE
    ///   2nd: 900 coins
    ///   3rd: 1900 coins
    ///   4th+: 2900 coins (cap — 횟수 제한 자체는 없음)
    /// Restart resets cost back to free.
    /// </summary>
    /// <remarks>
    /// Layer: Domain | Genre: Puzzle | Role: Handler | Phase: 3
    /// </remarks>
    public class ContinueHandler : Singleton<ContinueHandler>
    {
        #region Constants

        // 이어하기 제거량은 RailManager.GetContinueRemoveCount()로 결정 (허용량 기반)

        // Escalating coin costs (index 0 = free, then 900 → 1900 → 2900). idx 가 배열 길이를 넘으면 마지막(2900)으로 캡.
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
        /// 이어하기 횟수 제한 없음 — 항상 true.
        /// 보드 종료 직후 OnBoardFailed 이벤트 1회만 PopupFail01 을 띄우는 가드는 호출 흐름에서 처리.
        /// </summary>
        public bool CanContinue()
        {
            return true;
        }

        /// <summary>
        /// Returns true if the next continue is free (first continue).
        /// </summary>
        public bool IsNextContinueFree()
        {
            return _continueCount < ContinueCosts.Length && ContinueCosts[_continueCount] == 0;
        }

        /// <summary>
        /// Returns the coin cost of the next continue (현재 _continueCount 기준).
        /// </summary>
        public int GetContinueCost()
        {
            return GetContinueCost(_continueCount);
        }

        /// <summary>
        /// Returns the coin cost for a specific continue index.
        /// idx >= ContinueCosts.Length 이면 마지막 값(2900)으로 캡.
        /// </summary>
        public int GetContinueCost(int idx)
        {
            if (idx < 0) idx = 0;
            if (idx >= ContinueCosts.Length)
                return ContinueCosts[ContinueCosts.Length - 1];
            return ContinueCosts[idx];
        }

        /// <summary>
        /// Attempts to execute a continue. Free for the first, coin cost for subsequent.
        /// Returns true if continue succeeded.
        /// </summary>
        public bool Continue()
        {
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
            // 1) 최근 배치 다트 N개 + 같은 색 풍선 1:1 제거. 제거량은 레일 허용량 기준(4/8/12/16).
            int dartsRemoved = 0;
            int balloonsRemoved = 0;
            if (RailManager.HasInstance)
            {
                int capacity = RailManager.Instance.SlotCount;
                int targetRemove = RailManager.GetContinueRemoveCount(capacity);
                var res = RailManager.Instance.RemoveRecentDartsAndMatchingBalloons(targetRemove);
                dartsRemoved = res.removedDarts;
                balloonsRemoved = res.removedBalloons;
                Debug.Log($"[ContinueHandler] Continue removed {dartsRemoved} recent darts ({res.distinctColors} colors) + {balloonsRemoved} matching balloons (target={targetRemove}, capacity={capacity}).");
            }

            // 2) 배포중인 holder는 그대로 유지 (계속 배포).
            //    대기 중/이동 중인 holder만 큐로 복귀.
            if (HolderManager.HasInstance)
            {
                ReturnWaitingHoldersToQueue();
            }

            // 3) 풍선 제거는 새 API에서 직접 처리됨. 이벤트는 보드 상태 재개용.
            //    removedColor = -1 → BoardStateManager 가 추가 풍선 pop 을 시도하지 않음.
            EventBus.Publish(new OnContinueApplied
            {
                dartsRemoved = dartsRemoved,
                removedColor = -1,
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
            // 횟수 제한 제거 — 매 보드 실패마다 표시.
            if (PopupManager.HasInstance)
                PopupManager.Instance.ShowPopup("popup_fail01", priority: 50);

            Debug.Log($"[ContinueHandler] Board failed — showing PopupFail01. ContinueCount={_continueCount}");
        }

        #endregion
    }
}
