using UnityEngine;

namespace BalloonFlow
{
    /// <summary>
    /// Handles "continue after fail" with coin-based escalating costs.
    /// Design ref: 아웃게임디렉션 §3 / UX플로우 §7-1 (v1.2.37, 2026-05-28: "첫 실패 무료 1회" 명세 폐기 — 첫 실패부터 유료)
    ///   1st continue: 900 coins
    ///   2nd: 1900 coins
    ///   3rd+: 2900 coins (cap — 횟수 제한 자체는 없음)
    /// Restart resets cost back to 1회차(900).
    /// </summary>
    /// <remarks>
    /// Layer: Domain | Genre: Puzzle | Role: Handler | Phase: 3
    /// </remarks>
    public class ContinueHandler : Singleton<ContinueHandler>
    {
        #region Constants

        // 이어하기 제거량은 RailManager.GetContinueRemoveCount()로 결정 (허용량 기반)

        // Escalating coin costs (1회차 900 → 2회차 1900 → 3회차+ 2900). idx 가 배열 길이를 넘으면 마지막(2900)으로 캡.
        // v1.2.37: "첫 실패 무료 1회" 폐기 — index 0 부터 유료(900).
        private static readonly int[] ContinueCosts = { 900, 1900, 2900 };

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
            EventBus.Subscribe<OnSceneTransitionStarted>(HandleSceneTransitionStarted);
        }

        private void OnDisable()
        {
            EventBus.Unsubscribe<OnLevelLoaded>(HandleLevelLoaded);
            EventBus.Unsubscribe<OnBoardFailed>(HandleBoardFailed);
            EventBus.Unsubscribe<OnSceneTransitionStarted>(HandleSceneTransitionStarted);
            CancelFailPopupDelay();
        }

        #endregion

        #region Public Methods

        /// <summary>
        /// 이어하기 횟수 제한 없음 — 항상 true.
        /// 보드 종료 직후 OnBoardFailed 이벤트 1회만 PopupFail01 을 띄우는 가드는 호출 흐름에서 처리.
        /// </summary>
        public bool CanContinue()
        {
#if BF_RAIL_HOLDER
            // PROTO_RAIL_HOLDER_20260716: 레일 홀더 모드는 이어하기 미지원.
            //   이어하기 복원은 '레일에서 다트 제거'인데 이 모드엔 레일 다트가 없다 → 탄약 복원 0.
            //   허용하면 코인만 차감하고 즉시 재실패(+컨트롤러 정지)하므로 아예 막는다. false → 곧장 최종 실패 흐름.
            if (RailHolderController.ModeActiveForCurrentLevel) return false;
#endif
            return true;
        }

        /// <summary>
        /// v1.2.37 이후 무료 이어하기 없음 — 항상 false. (호출부 호환용으로 메서드 유지)
        /// </summary>
        public bool IsNextContinueFree()
        {
            return false;
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
                    Debug.LogWarning($"[ContinueHandler] Not enough coins to continue. have={CurrencyManager.Instance.Coins}, need={cost}");
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

        // ROLLBACK_CONTINUE_DEBUG_20260716: 이어하기 배포정지·다트/풍선 이격 진단 로그(순수 관측, 동작 변경 없음).
        //   원인 확정 후 이 메서드와 호출부(ApplyContinueRestore 3곳)를 통째로 제거.
        //   읽는 법: BEFORE/AFTER 를 '비교'해서 본다. 제거 대상 색만 darts/balloons 가 같은 수만큼 줄고,
        //   나머지 색은 전혀 변하지 않아야 정상. 한 줄의 [+N]/[-N] 절대값 자체는 정상일 수 있다 —
        //   Pinata/Ice 는 풍선 1개가 다트를 여러 개 먹으므로 count 기준으로는 원래 +N 이 뜬다.
        private static void LogContinueSupply(string phase)
        {
            if (!RailManager.HasInstance || !BalloonController.HasInstance) return;

            const int MAX_C = 16;
            int[] railD = new int[MAX_C];
            int[] magD = new int[MAX_C];
            int[] balB = new int[MAX_C];

            var darts = RailManager.Instance.GetAllDarts();
            for (int i = 0; i < darts.Count; i++)
            {
                int c = darts[i] != null ? darts[i].dartColor : -1;
                if (c >= 0 && c < MAX_C) railD[c]++;
            }

            if (HolderManager.HasInstance)
            {
                HolderData[] holders = HolderManager.Instance.GetHolders();
                if (holders != null)
                {
                    for (int i = 0; i < holders.Length; i++)
                    {
                        HolderData h = holders[i];
                        if (h == null || h.isConsumed || h.magazineCount <= 0) continue;
                        if (h.color >= 0 && h.color < MAX_C) magD[h.color] += h.magazineCount;
                    }
                }
            }

            BalloonData[] alive = BalloonController.Instance.GetAliveBalloons(out int aliveCount);
            for (int i = 0; i < aliveCount; i++)
            {
                BalloonData b = alive[i];
                if (b != null && b.color >= 0 && b.color < MAX_C) balB[b.color]++;
            }

            var sb = new System.Text.StringBuilder(256);
            sb.Append($"[Continue-DEBUG] supply {phase} — perColor: rail+mag=darts vs aliveBalloons\n");
            for (int c = 0; c < MAX_C; c++)
            {
                int have = railD[c] + magD[c];
                if (have == 0 && balB[c] == 0) continue;
                int diff = have - balB[c];
                string status = diff == 0 ? "0" : (diff > 0 ? $"+{diff}" : diff.ToString());
                sb.Append($"  c{c}: {railD[c]}rail+{magD[c]}mag={have}D vs {balB[c]}B [{status}]\n");
            }
            Debug.LogWarning(sb.ToString());
        }

        private void ApplyContinueRestore()
        {
            // 1) 최근 배치 다트 N개 + 같은 색 풍선 1:1 제거. 제거량은 레일 허용량 기준(4/8/12/16).
            int dartsRemoved = 0;
            int balloonsRemoved = 0;
            int targetColor = -1;
            if (RailManager.HasInstance)
            {
                LogContinueSupply("BEFORE-remove");

                // 레일에서 가장 많은 색 다트 전부 제거 + 그 수만큼 같은 색 필드 풍선 랜덤 제거.
                var res = RailManager.Instance.RemoveMostCommonColorDartsAndRandomBalloons();
                dartsRemoved = res.removedDarts;
                balloonsRemoved = res.removedBalloons;
                targetColor = res.targetColor;
                Debug.Log($"[ContinueHandler] Continue removed {dartsRemoved} darts of most-common color({targetColor}) + {balloonsRemoved} random matching balloons.");

                // ROLLBACK_CONTINUE_DEBUG_20260716: 다트:풍선 1:1 이 깨지는 순간을 즉시 표면화.
                //   removedDarts 는 스냅샷 경로에서 '보고값'이라 실제 물리 제거와 다를 수 있음 → BEFORE/AFTER 로 교차검증.
                if (dartsRemoved != balloonsRemoved)
                    Debug.LogWarning($"[Continue-DEBUG] 1:1 MISMATCH — removedDarts={dartsRemoved} vs removedBalloons={balloonsRemoved} (color={targetColor})");

                LogContinueSupply("AFTER-remove");
            }

            // 2) 보관함(holder): 제거된 색만 큐 복귀, 다른 색은 재구동(이어 배포). HolderVisualManager 가 처리.

            // 3) 풍선 제거는 새 API에서 직접 처리됨. 이벤트는 보드 상태 재개용.
            //    removedColor = -1 → BoardStateManager 가 추가 풍선 pop 을 시도하지 않음.
            // ROLLBACK_CONTINUE_GRACE_ORDER_20260630:
            // Reset the board before publishing OnContinueApplied. Publishing first let
            // BoardStateManager enable post-continue grace, then InitializeBoard cleared it,
            // causing an immediate re-fail even when the rail had visible space.
            if (BoardStateManager.HasInstance)
            {
                int remaining = BoardStateManager.Instance.GetRemainingBalloons();
                BoardStateManager.Instance.InitializeBoard(_currentLevelId, remaining);
            }

            EventBus.Publish(new OnContinueApplied
            {
                dartsRemoved = dartsRemoved,
                removedColor = -1,
                levelId = _currentLevelId,
                // 같은 색 holder 만 큐 복귀, 다른 색 holder 는 재구동(이어 배포) — 다른 색 보관함 사라짐 방지.
                holderResetColor = targetColor
            });

            // 4) 보드 상태 리셋하여 게임플레이 재개
            // BoardStateManager was already reset before OnContinueApplied so its continue
            // grace is not wiped after the event.
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
            // 실패 흐름: [다트 탈선 흩어짐 연출] → PopupFail01 → PopupContinue → PopupFail02
            // [FAIL_DERAIL 2026-06-12] DartManager 가 같은 이벤트로 레일 다트 탈선 연출을 재생하므로,
            // 연출이 끝날 때까지(FailScatterPopupDelay) 기다렸다 팝업 표시. realtime — pause 무관.
            CancelFailPopupDelay();
            _failPopupDelayCo = StartCoroutine(ShowFailPopupAfterScatter());
        }

        private Coroutine _failPopupDelayCo;

        private void HandleSceneTransitionStarted(OnSceneTransitionStarted evt)
        {
            // ROLLBACK_CONTINUE_FAIL_POPUP_SCENE_CANCEL_20260619:
            // MapMaker test play can leave this delayed fail popup coroutine alive while
            // the registered popup canvases are destroyed during scene transitions.
            CancelFailPopupDelay();
        }

        private void CancelFailPopupDelay()
        {
            if (_failPopupDelayCo == null) return;
            StopCoroutine(_failPopupDelayCo);
            _failPopupDelayCo = null;
        }

        private System.Collections.IEnumerator ShowFailPopupAfterScatter()
        {
            yield return new WaitForSecondsRealtime(DartManager.FailScatterPopupDelay);
            if (PopupManager.HasInstance && PopupManager.Instance.HasPopup("popup_fail01"))
                PopupManager.Instance.ShowPopup("popup_fail01", priority: 50);
            Debug.Log($"[ContinueHandler] Board failed — showing PopupFail01 (after derail fx). ContinueCount={_continueCount}");
            _failPopupDelayCo = null;
        }

        #endregion
    }
}
