using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using DG.Tweening;

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
        private bool _awaitingBalloonClick;

        /// <summary>Tracks which booster type is pending user interaction (for deferred consumption).</summary>
        private string _pendingBoosterType;

        /// <summary>부스터 취소 버튼 (런타임 생성).</summary>
        private GameObject _cancelButtonGO;

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

            ConfirmPendingBooster();
            HideCancelButton();
            CloseUseItemPopup();
            ExecuteSelectTool(holderId);

            if (CameraManager.HasInstance)
                CameraManager.Instance.MoveBack();

            ResumeRail();
        }

        /// <summary>
        /// Whether the executor is waiting for player color selection (Color Remove).
        /// </summary>
        public bool IsAwaitingColorSelection => _awaitingColorSelection;

        /// <summary>
        /// Whether the executor is waiting for player holder selection (Select Tool).
        /// </summary>
        public bool IsAwaitingHolderSelection => _awaitingHolderSelection;

        /// <summary>
        /// Whether the executor is waiting for player balloon click (Color Remove).
        /// </summary>
        public bool IsAwaitingBalloonClick => _awaitingBalloonClick;

        /// <summary>Called when player clicks a balloon during Color Remove mode.</summary>
        public void OnBalloonClicked(int balloonId)
        {
            if (!_awaitingBalloonClick) return;
            _awaitingBalloonClick = false;

            ConfirmPendingBooster();

            // Get clicked balloon's color
            if (!BalloonController.HasInstance) return;
            var data = BalloonController.Instance.GetBalloon(balloonId);
            if (data == null) return;
            int selectedColor = data.color;

            // Highlight selected color with white outline, others stay black
            BalloonController.Instance.SetOutlineByColor(selectedColor, true, Color.white);

            // Execute color remove after brief delay (so player sees the highlight)
            HideCancelButton();
            CloseUseItemPopup();
            StartCoroutine(DelayedColorRemove(selectedColor));
        }

        /// <summary>UseItem 팝업 닫기.</summary>
        private void CloseUseItemPopup()
        {
            if (UIManager.HasInstance)
            {
                var popup = UIManager.Instance.GetOpenUI<PopupUseItem>();
                if (popup != null) popup.CloseUI();
            }
        }

        #endregion

        #region Private Methods — Event Handler

        private void HandleBoosterUsed(OnBoosterUsed evt)
        {
            // Pause rail rotation while booster is active
            if (RailManager.HasInstance)
                RailManager.Instance.IsPausedByBooster = true;

            switch (evt.boosterType)
            {
                case BoosterManager.SELECT_TOOL:
                    _pendingBoosterType = BoosterManager.SELECT_TOOL;
                    _awaitingHolderSelection = true;
                    ShowCancelButton();

                    if (CameraManager.HasInstance && HolderVisualManager.HasInstance)
                    {
                        Vector3 queuePosition = HolderVisualManager.Instance.CalculateQueueCenterPosition();
                        CameraManager.Instance.MoveToTarget(queuePosition);
                    }

                    Debug.Log("[BoosterExecutor] Select Tool activated. Waiting for holder selection.");
                    break;

                case BoosterManager.SHUFFLE:
                    ExecuteShuffle();
                    ResumeRail();
                    break;

                case BoosterManager.COLOR_REMOVE:
                    _pendingBoosterType = BoosterManager.COLOR_REMOVE;
                    ShowCancelButton();
                    _awaitingColorSelection = true;
                    _awaitingBalloonClick = true;

                    // Move camera to field center
                    if (CameraManager.HasInstance && GameManager.HasInstance)
                    {
                        Vector3 fieldPosition = new Vector3(
                            GameManager.Instance.Board.boardCenterX,
                            0f,
                            GameManager.Instance.Board.boardCenterZ
                        );
                        CameraManager.Instance.MoveToTarget(fieldPosition);
                    }

                    // Turn ON black outline on ALL balloons
                    if (BalloonController.HasInstance)
                        BalloonController.Instance.SetAllOutlines(true, Color.black);

                    Debug.Log("[BoosterExecutor] Color Remove activated. Waiting for color selection.");
                    break;

                // HAND = SELECT_TOOL (명세 통합) → 위 SELECT_TOOL case에서 처리
            }
        }

        /// <summary>Resume rail rotation after booster completes.</summary>
        private void ResumeRail()
        {
            if (RailManager.HasInstance)
                RailManager.Instance.IsPausedByBooster = false;
        }

        /// <summary>Confirm deferred booster consumption after user completes interaction.</summary>
        private void ConfirmPendingBooster()
        {
            if (!string.IsNullOrEmpty(_pendingBoosterType))
            {
                // Inventory was already decremented in UseBooster — nothing extra needed
                _pendingBoosterType = null;
            }
        }

        /// <summary>
        /// Cancel a pending interactive booster — refunds inventory and resumes rail.
        /// </summary>
        public void CancelPendingBooster()
        {
            if (string.IsNullOrEmpty(_pendingBoosterType)) return;

            // Refund inventory
            if (BoosterManager.HasInstance)
                BoosterManager.Instance.AddBooster(_pendingBoosterType, 1);

            Debug.Log($"[BoosterExecutor] Cancelled {_pendingBoosterType} — inventory refunded.");

            // Reset awaiting flags
            _awaitingHolderSelection = false;
            _awaitingColorSelection = false;
            _awaitingBalloonClick = false;

            // Clear outlines if Color Remove was active
            if (BalloonController.HasInstance)
                BalloonController.Instance.ClearAllOutlines();

            // Move camera back
            if (CameraManager.HasInstance)
                CameraManager.Instance.MoveBack();

            _pendingBoosterType = null;
            HideCancelButton();
            CloseUseItemPopup();
            ResumeRail();
        }

        /// <summary>부스터 취소 [X] 버튼 표시.</summary>
        private void ShowCancelButton()
        {
            if (_cancelButtonGO != null) { _cancelButtonGO.SetActive(true); return; }

            // Canvas 찾기
            var canvas = FindAnyObjectByType<Canvas>();
            if (canvas == null)
            {
                Debug.LogError("[BoosterExecutor] No Canvas found — cancel button can't be created. Cancelling pending booster (inventory refunded).");
                CancelPendingBooster();
                return;
            }

            _cancelButtonGO = new GameObject("BoosterCancelBtn");
            _cancelButtonGO.transform.SetParent(canvas.transform, false);

            var rt = _cancelButtonGO.AddComponent<RectTransform>();
            rt.anchorMin = new Vector2(1f, 1f);
            rt.anchorMax = new Vector2(1f, 1f);
            rt.pivot = new Vector2(1f, 1f);
            rt.anchoredPosition = new Vector2(-20f, -20f);
            rt.sizeDelta = new Vector2(80f, 80f);

            var img = _cancelButtonGO.AddComponent<UnityEngine.UI.Image>();
            img.color = new Color(0.8f, 0.2f, 0.2f, 0.9f);

            var btn = _cancelButtonGO.AddComponent<UnityEngine.UI.Button>();
            btn.onClick.AddListener(CancelPendingBooster);

            // X 텍스트
            var txtGO = new GameObject("X");
            txtGO.transform.SetParent(_cancelButtonGO.transform, false);
            var txtRT = txtGO.AddComponent<RectTransform>();
            txtRT.anchorMin = Vector2.zero; txtRT.anchorMax = Vector2.one;
            txtRT.offsetMin = Vector2.zero; txtRT.offsetMax = Vector2.zero;
            var txt = txtGO.AddComponent<TMPro.TextMeshProUGUI>();
            txt.text = "X";
            txt.fontSize = 40;
            txt.alignment = TMPro.TextAlignmentOptions.Center;
            txt.color = Color.white;
        }

        /// <summary>부스터 취소 버튼 숨기기.</summary>
        private void HideCancelButton()
        {
            if (_cancelButtonGO != null)
                _cancelButtonGO.SetActive(false);
        }

        /// <summary>Whether an interactive booster is pending (can be cancelled).</summary>
        public bool HasPendingBooster => !string.IsNullOrEmpty(_pendingBoosterType);

        #endregion

        #region Private Methods — Execution

        private IEnumerator DelayedColorRemove(int color)
        {
            yield return new WaitForSeconds(0.3f);

            // Clear all outlines
            if (BalloonController.HasInstance)
                BalloonController.Instance.ClearAllOutlines();

            // Execute removal
            _awaitingColorSelection = false;
            HideCancelButton();
            ConfirmPendingBooster();
            ExecuteColorRemove(color);

            // Camera back
            if (CameraManager.HasInstance)
                CameraManager.Instance.MoveBack();

            ResumeRail();
        }

        /// <summary>
        /// Select Tool: force-deploy the chosen holder regardless of queue order.
        /// </summary>
        private void ExecuteSelectTool(int holderId)
        {
            if (!HolderManager.HasInstance) return;

            // Hand/SelectTool: 줄 순서 무시 — ForceSelectHolder 사용
            bool result = HolderManager.Instance.ForceSelectHolder(holderId);
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
        /// Chain groups are treated as single units — members stay together with relative column order preserved.
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

            // Group by chainGroupId. Holders with chainGroupId < 0 are standalone (each is its own unit).
            var chainGroups = new Dictionary<int, List<HolderData>>();
            int soloKey = -1; // unique negative keys for standalone holders
            for (int i = 0; i < shuffleable.Count; i++)
            {
                int gid = shuffleable[i].chainGroupId;
                if (gid < 0)
                {
                    // Standalone — assign unique group key
                    chainGroups[soloKey] = new List<HolderData> { shuffleable[i] };
                    soloKey--;
                }
                else
                {
                    if (!chainGroups.ContainsKey(gid))
                        chainGroups[gid] = new List<HolderData>();
                    chainGroups[gid].Add(shuffleable[i]);
                }
            }

            // Sort members within each chain group by column (preserve relative ordering)
            foreach (var kvp in chainGroups)
            {
                if (kvp.Value.Count > 1)
                    kvp.Value.Sort((a, b) => a.column.CompareTo(b.column));
            }

            // Build list of shuffle units (each unit = list of holders)
            var units = new List<List<HolderData>>();
            foreach (var kvp in chainGroups)
                units.Add(kvp.Value);

            if (units.Count <= 1) return;

            // Collect original column slots for each unit (in order)
            var unitColumns = new List<List<int>>();
            for (int i = 0; i < units.Count; i++)
            {
                var cols = new List<int>();
                for (int j = 0; j < units[i].Count; j++)
                    cols.Add(units[i][j].column);
                unitColumns.Add(cols);
            }

            // Fisher-Yates shuffle of units
            for (int i = units.Count - 1; i > 0; i--)
            {
                int j = Random.Range(0, i + 1);
                // Swap column assignments between unit i and unit j
                var tmpCols = unitColumns[i];
                unitColumns[i] = unitColumns[j];
                unitColumns[j] = tmpCols;
            }

            // Apply new column assignments
            for (int i = 0; i < units.Count; i++)
            {
                var cols = unitColumns[i];
                var members = units[i];
                for (int m = 0; m < members.Count; m++)
                {
                    // If group has more members than available column slots, wrap
                    int colIdx = m < cols.Count ? m : m % cols.Count;
                    members[m].column = cols[colIdx];
                }
            }

            Debug.Log($"[BoosterExecutor] Shuffle: randomized {units.Count} units ({shuffleable.Count} holders).");

            // Shuffle 연출: 카메라 쉐이크
            if (CameraManager.HasInstance && CameraManager.Instance.MainCamera != null)
            {
                CameraManager.Instance.MainCamera.transform.DOShakePosition(0.2f, 0.1f, 8, 90f, false, true);
            }

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
                BalloonData[] balloons = BalloonController.Instance.GetAllBalloonsByColor(color);
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

            // Re-distribute remaining holders across columns and refresh visuals
            if (HolderManager.HasInstance)
                HolderManager.Instance.CompactColumns();
            if (HolderVisualManager.HasInstance)
                HolderVisualManager.Instance.RefreshAllPositions();

            // Color Remove 연출: 카메라 쉐이크
            if (CameraManager.HasInstance && CameraManager.Instance.MainCamera != null)
            {
                CameraManager.Instance.MainCamera.transform.DOShakePosition(0.3f, 0.15f, 10, 90f, false, true);
            }

            Debug.Log($"[BoosterExecutor] Color Remove: removed {totalRemoved} objects of color {color}.");

            EventBus.Publish(new OnBoosterEffectApplied
            {
                boosterType = BoosterManager.COLOR_REMOVE,
                affectedCount = totalRemoved
            });
        }

        // Hand booster now uses SELECT_TOOL behavior (holder selection mode).
        // The HAND case in HandleBoosterUsed sets _awaitingHolderSelection = true
        // and moves camera to queue, identical to SELECT_TOOL.
        // OnHolderSelected handles the actual deployment for both boosters.

        #endregion
    }
}
