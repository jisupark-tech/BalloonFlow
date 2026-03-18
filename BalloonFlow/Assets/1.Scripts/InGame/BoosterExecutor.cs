using System.Collections.Generic;
using UnityEngine;

namespace BalloonFlow
{
    /// <summary>
    /// Executes actual game effects when a booster is used.
    /// BoosterManager handles inventory; this handles gameplay logic.
    /// Design ref: 아웃게임디렉션 §부스터
    ///   Select Tool — 큐에서 원하는 보관함 선택 배치
    ///   Shuffle — 큐 보관함 순서 랜덤 셔플
    ///   Color Remove — 필드+레일+큐에서 지정 색상 전체 제거
    /// </summary>
    /// <remarks>
    /// Layer: Domain | Genre: Puzzle | Role: Handler | Phase: 3
    /// </remarks>
    public class BoosterExecutor : SceneSingleton<BoosterExecutor>
    {
        #region Fields

        private bool _awaitingColorSelection;
        private bool _awaitingHolderSelection;

        #endregion

        #region Lifecycle

        private void OnEnable()
        {
            EventBus.Subscribe<OnBoosterUsed>(HandleBoosterUsed);
        }

        private void OnDisable()
        {
            EventBus.Unsubscribe<OnBoosterUsed>(HandleBoosterUsed);
        }

        #endregion

        #region Public Methods

        /// <summary>
        /// Called by UI when player selects a color for Color Remove booster.
        /// </summary>
        public void OnColorSelected(int color)
        {
            if (!_awaitingColorSelection) return;
            _awaitingColorSelection = false;

            ExecuteColorRemove(color);
        }

        /// <summary>
        /// Called by UI when player selects a holder for Select Tool booster.
        /// </summary>
        public void OnHolderSelected(int holderId)
        {
            if (!_awaitingHolderSelection) return;
            _awaitingHolderSelection = false;

            ExecuteSelectTool(holderId);
        }

        /// <summary>
        /// Whether the executor is waiting for player color selection (Color Remove).
        /// </summary>
        public bool IsAwaitingColorSelection => _awaitingColorSelection;

        /// <summary>
        /// Whether the executor is waiting for player holder selection (Select Tool).
        /// </summary>
        public bool IsAwaitingHolderSelection => _awaitingHolderSelection;

        #endregion

        #region Private Methods — Event Handler

        private void HandleBoosterUsed(OnBoosterUsed evt)
        {
            switch (evt.boosterType)
            {
                case BoosterManager.SELECT_TOOL:
                    // Enter holder selection mode — UI highlights available holders
                    _awaitingHolderSelection = true;
                    Debug.Log("[BoosterExecutor] Select Tool activated. Waiting for holder selection.");
                    break;

                case BoosterManager.SHUFFLE:
                    ExecuteShuffle();
                    break;

                case BoosterManager.COLOR_REMOVE:
                    // Enter color selection mode — UI shows available colors
                    _awaitingColorSelection = true;
                    Debug.Log("[BoosterExecutor] Color Remove activated. Waiting for color selection.");
                    break;
            }
        }

        #endregion

        #region Private Methods — Execution

        /// <summary>
        /// Select Tool: force-deploy the chosen holder regardless of queue order.
        /// </summary>
        private void ExecuteSelectTool(int holderId)
        {
            if (!HolderManager.HasInstance) return;

            bool result = HolderManager.Instance.SelectHolder(holderId);
            if (result)
            {
                Debug.Log($"[BoosterExecutor] Select Tool: deployed holder {holderId}.");
            }
            else
            {
                Debug.LogWarning($"[BoosterExecutor] Select Tool: failed to deploy holder {holderId}.");
            }
        }

        /// <summary>
        /// Shuffle: randomize the order of waiting holders in the queue.
        /// </summary>
        private void ExecuteShuffle()
        {
            if (!HolderManager.HasInstance) return;

            HolderData[] holders = HolderManager.Instance.GetHolders();
            if (holders == null || holders.Length == 0) return;

            // Collect non-active, non-consumed holders
            var shuffleable = new List<HolderData>();
            for (int i = 0; i < holders.Length; i++)
            {
                if (!holders[i].isDeploying && !holders[i].isWaiting &&
                    !holders[i].isMovingToRail && !holders[i].isConsumed &&
                    holders[i].magazineCount > 0)
                {
                    shuffleable.Add(holders[i]);
                }
            }

            if (shuffleable.Count <= 1) return;

            // Fisher-Yates shuffle of column assignments
            int queueCols = HolderManager.Instance.QueueColumns;
            for (int i = shuffleable.Count - 1; i > 0; i--)
            {
                int j = Random.Range(0, i + 1);
                // Swap columns
                int tmpCol = shuffleable[i].column;
                shuffleable[i].column = shuffleable[j].column;
                shuffleable[j].column = tmpCol;
            }

            Debug.Log($"[BoosterExecutor] Shuffle: randomized {shuffleable.Count} holder positions.");

            // Sync visual positions to new column assignments
            if (HolderVisualManager.HasInstance)
                HolderVisualManager.Instance.RefreshAllPositions();

            EventBus.Publish(new OnBoosterEffectApplied
            {
                boosterType = BoosterManager.SHUFFLE,
                affectedCount = shuffleable.Count
            });
        }

        /// <summary>
        /// Color Remove: remove all balloons of the chosen color from field,
        /// all darts of that color from rail, and all holders of that color from queue.
        /// </summary>
        private void ExecuteColorRemove(int color)
        {
            int totalRemoved = 0;

            // 1) Field — pop all balloons of this color
            if (BalloonController.HasInstance)
            {
                BalloonData[] balloons = BalloonController.Instance.GetBalloonsByColor(color);
                if (balloons != null)
                {
                    for (int i = 0; i < balloons.Length; i++)
                    {
                        if (!balloons[i].isPopped)
                        {
                            BalloonController.Instance.ForcePopBalloon(balloons[i].balloonId);
                            totalRemoved++;
                        }
                    }
                }
            }

            // 2) Rail — clear all slots with this dart color
            if (RailManager.HasInstance)
            {
                int slotCount = RailManager.Instance.SlotCount;
                for (int i = 0; i < slotCount; i++)
                {
                    var slot = RailManager.Instance.GetSlot(i);
                    if (slot.dartColor == color)
                    {
                        RailManager.Instance.ClearSlot(i);
                        totalRemoved++;
                    }
                }
            }

            // 3) Queue — consume all holders of this color and remove visuals
            if (HolderManager.HasInstance)
            {
                HolderData[] holders = HolderManager.Instance.GetHolders();
                if (holders != null)
                {
                    for (int i = 0; i < holders.Length; i++)
                    {
                        if (holders[i].color == color && !holders[i].isConsumed && holders[i].magazineCount > 0)
                        {
                            int hid = holders[i].holderId;

                            // Cancel deploy coroutine if active
                            if (HolderVisualManager.HasInstance)
                                HolderVisualManager.Instance.RemoveHolderVisual(hid);

                            // Reset data state
                            HolderManager.Instance.UndoDeploy(hid);
                            holders[i].magazineCount = 0;
                            holders[i].isConsumed = true;
                            totalRemoved++;
                        }
                    }
                }
            }

            Debug.Log($"[BoosterExecutor] Color Remove: removed {totalRemoved} objects of color {color}.");

            EventBus.Publish(new OnBoosterEffectApplied
            {
                boosterType = BoosterManager.COLOR_REMOVE,
                affectedCount = totalRemoved
            });
        }

        #endregion
    }
}
