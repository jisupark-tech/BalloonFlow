using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using DG.Tweening;
using DigitalRuby.LightningBolt;

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

        /// <summary>[Optimization 2026-05-10] Canvas 캐시. ShowCancelButton 의 FindAnyObjectByType 가 매 booster 활성화 시 발생하는 O(scene) lookup 제거.
        /// 씬 재로드로 destroyed 되면 lazy 재fetch 됨. 롤백: 이 필드 제거 + ShowCancelButton 의 FindAnyObjectByType 직접 호출 라인 복원.</summary>
        private Canvas _cachedCanvas;

        private const float ZapSelectionHighlightDelay = 0.15f;
        private const float ZapAppearDuration = 0.45f;
        private const float ZapMoveDuration = 0.25f;
        private const float ZapMaxTotalEffectDuration = 2f;
        private const float ZapLineLifetime = 0.2f;
        private const float ZapMinLeadInterval = 0.03f;
        private const float ZapLineLeadBeforePop = 0.015f;
        private const float ZapFinishLifetime = 0.35f;
        private const float ZapEffectYOffset = 0.12f;

        // Hand 부스터 사용 시 카메라가 보관함 큐의 앞쪽 몇 개 행을 보여줄지 (5줄 요구).
        private const int HAND_VISIBLE_ROWS = 5;
        private static readonly Vector3 ZapSpawnPosition = new Vector3(-0.1911252f, 1.95f, -7.79f);
        private const float ZapLineWorldLift = 0.35f;
        private const float ZapLineMinWidth = 0.08f;
        private const int ZapLineSortingOrder = 80;
        private const float ZapFieldBottomPaddingCells = 1.5f;
        private const int ZapLineConcurrentCount = 4;
        private const float ZapLineForkOffset = 0.45f; // 4갈래(±0.5Δ, ±1.5Δ)로 벌어지는 perpendicular 오프셋(월드 단위)
        // 번개 라인이 활성화된 동안 매 tick 마다 끝점에 미세 jitter 를 더해 LightningBoltScript.Trigger() 를 재호출해 자글거리게 만든다.
        private const float ZapLineJiggleMinInterval = 0.03f;
        private const float ZapLineJiggleMaxInterval = 0.06f;
        private const float ZapLineJiggleEndpointJitter = 0.07f;

        private readonly List<ZapTarget> _zapTargets = new List<ZapTarget>(128);
        private GameObject _itemZapPrefab;
        private GameObject _fxZapLinePrefab;
        private GameObject _fxZapLine2Prefab;
        private bool _isColorRemoveSequenceRunning;
        private bool _isZapAnimationPlaying = false;
        private Animator _zapAnimator = null;

        // FxZapLine 자글거림(번개 재트리거) 코루틴. ConfigureZapLine 이 마지막에 설정한 baseline 을 읽고
        // jitter 를 더해 매 tick LightningBoltScript.Trigger() 를 호출한다. 정리(라인 SetActive(false)/Destroy)
        // 직전과 OnDisable 에서 반드시 중지하여 누수/이중 호출을 막는다.
        private Coroutine _zapLineJiggleCo;
        private readonly Dictionary<LightningBoltScript, ZapLineBaseline> _zapLineBaselines = new Dictionary<LightningBoltScript, ZapLineBaseline>(8);

        private struct ZapLineBaseline
        {
            public Vector3 start;
            public Vector3 end;
        }

        private struct ZapTarget
        {
            public int balloonId;
            public Vector3 position;
        }

        #endregion

        #region Lifecycle

        private void OnEnable()
        {
            EventBus.Subscribe<OnBoosterUsed>(HandleBoosterUsed);
        }

        private void OnDisable()
        {
            EventBus.Unsubscribe<OnBoosterUsed>(HandleBoosterUsed);
            StopZapLineJiggle();
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
            _awaitingBalloonClick = false;

            ConfirmPendingBooster();
            CloseUseItemPopup(false);
            StartCoroutine(PlayColorRemoveSequence(color));
        }

        /// <summary>
        /// Called by UI when player selects a holder for Select Tool booster.
        /// </summary>
        public void OnHolderSelected(int holderId)
        {
            if (!_awaitingHolderSelection) return;
            _awaitingHolderSelection = false;

            if (HolderVisualManager.HasInstance)
                HolderVisualManager.Instance.SetHandSelectionHighlightActive(false);

            ConfirmPendingBooster();
            //HideCancelButton();
            CloseUseItemPopup(true);
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
            _awaitingColorSelection = false;

            ConfirmPendingBooster();

            // Get clicked balloon's color
            if (!BalloonController.HasInstance) return;
            var data = BalloonController.Instance.GetBalloon(balloonId);
            if (data == null) return;
            int selectedColor = data.color;

            // Highlight selected color with white outline, others stay black
            BalloonController.Instance.SetOutlineByColor(selectedColor, true, Color.white);

            // Execute color remove after brief delay (so player sees the highlight)
            //HideCancelButton();
            CloseUseItemPopup(false);
            StartCoroutine(PlayColorRemoveSequence(selectedColor));
        }

        /// <summary>UseItem 팝업 닫기.</summary>
        private void CloseUseItemPopup(bool restoreBottomPanel = true)
        {
            if (UIManager.HasInstance)
            {
                var popup = UIManager.Instance.GetOpenUI<PopupUseItem>();
                if (popup != null) popup.CloseUI(restoreBottomPanel);
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
                    //ShowCancelButton();

                    if (CameraManager.HasInstance && HolderVisualManager.HasInstance)
                    {
                        // Hand: 보관함 큐의 앞쪽 HAND_VISIBLE_ROWS(5)개 행이 화면에 들어오도록 그 행들의 중심으로 포커스.
                        // (이전: 전체 holder bbox 중심 + 고정 -2Z → 행이 많으면 너무 깊게 잡혀 5줄이 안 보였음.)
                        Vector3 focusPosition = HolderVisualManager.Instance.CalculateRowFocusPosition(HAND_VISIBLE_ROWS);
                        if (CameraManager.Instance.MainCamera != null)
                            focusPosition.y = CameraManager.Instance.MainCamera.transform.position.y; // 카메라 높이 유지(XZ 이동)
                        CameraManager.Instance.MoveToTarget(focusPosition);
                    }

                    if (HolderVisualManager.HasInstance)
                        HolderVisualManager.Instance.SetHandSelectionHighlightActive(true);

                    Debug.Log("[BoosterExecutor] Select Tool activated. Waiting for holder selection.");
                    break;

                case BoosterManager.SHUFFLE:
                    ExecuteShuffle();
                    ResumeRail();
                    break;

                case BoosterManager.COLOR_REMOVE:
                    _pendingBoosterType = BoosterManager.COLOR_REMOVE;
                    //ShowCancelButton();
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

            if (HolderVisualManager.HasInstance)
                HolderVisualManager.Instance.SetHandSelectionHighlightActive(false);

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
            //HideCancelButton();
            CloseUseItemPopup();
            ResumeRail();
        }

        /// <summary>부스터 취소 [X] 버튼 표시.</summary>
        private void ShowCancelButton()
        {
            if (_cancelButtonGO != null) { _cancelButtonGO.SetActive(true); return; }

            // Canvas 찾기
            // [Optimization 2026-05-10] Canvas 를 _cachedCanvas 로 캐시. 씬 재로드 등으로 destroyed 시 lazy 재fetch.
            // 롤백: 아래 캐시 분기 제거 + 주석 처리된 원본 라인 복원.
            // 원본: var canvas = FindAnyObjectByType<Canvas>();
            if (_cachedCanvas == null) _cachedCanvas = FindAnyObjectByType<Canvas>();
            var canvas = _cachedCanvas;
            if (canvas == null)
            {
                Debug.LogError("[BoosterExecutor] No Canvas found — cancel button can't be created. Cancelling pending booster (inventory refunded).");
                CancelPendingBooster();
                return;
            }

            // _cancelButtonGO = new GameObject("BoosterCancelBtn");
            // _cancelButtonGO.transform.SetParent(canvas.transform, false);

            // var rt = _cancelButtonGO.AddComponent<RectTransform>();
            // rt.anchorMin = new Vector2(1f, 1f);
            // rt.anchorMax = new Vector2(1f, 1f);
            // rt.pivot = new Vector2(1f, 1f);
            // rt.anchoredPosition = new Vector2(-20f, -20f);
            // rt.sizeDelta = new Vector2(80f, 80f);

            // var img = _cancelButtonGO.AddComponent<UnityEngine.UI.Image>();
            // img.color = new Color(0.8f, 0.2f, 0.2f, 0.9f);

            // var btn = _cancelButtonGO.AddComponent<UnityEngine.UI.Button>();
            // btn.onClick.AddListener(CancelPendingBooster);

            // // X 텍스트
            // var txtGO = new GameObject("X");
            // txtGO.transform.SetParent(_cancelButtonGO.transform, false);
            // var txtRT = txtGO.AddComponent<RectTransform>();
            // txtRT.anchorMin = Vector2.zero; txtRT.anchorMax = Vector2.one;
            // txtRT.offsetMin = Vector2.zero; txtRT.offsetMax = Vector2.zero;
            // var txt = txtGO.AddComponent<TMPro.TextMeshProUGUI>();
            // txt.text = "X";
            // txt.fontSize = 40;
            // txt.alignment = TMPro.TextAlignmentOptions.Center;
            // txt.color = Color.white;
        }

        /// <summary>부스터 취소 버튼 숨기기.</summary>
        private void HideCancelButton()
        {
            // if (_cancelButtonGO != null)
            //     _cancelButtonGO.SetActive(false);
        }

        /// <summary>Whether an interactive booster is pending (can be cancelled).</summary>
        public bool HasPendingBooster => !string.IsNullOrEmpty(_pendingBoosterType);

        #endregion

        #region Private Methods — Execution

        private IEnumerator DelayedColorRemove(int color)
        {
            yield return PlayColorRemoveSequence(color);
        }

        private IEnumerator PlayColorRemoveSequence(int color)
        {
            if (_isColorRemoveSequenceRunning)
                yield break;

            _isColorRemoveSequenceRunning = true;
            SetHudBottomPanelHiddenForZap(true);

            yield return new WaitForSeconds(ZapSelectionHighlightDelay);

            if (BalloonController.HasInstance)
                BalloonController.Instance.ClearAllOutlines();

            _awaitingColorSelection = false;
            _awaitingBalloonClick = false;
            ConfirmPendingBooster();

            CollectZapTargets(color);

            Vector3 attackPosition = GetZapAttackPosition();
            // ItemZap stays at ZapSpawnPosition for the entire effect — do not tween or reposition.
            GameObject zapObject = CreateItemZap(attackPosition);
            Vector3 zapFixedPosition = zapObject != null ? zapObject.transform.position : ZapSpawnPosition;
            List<GameObject> zapLineObjects = null;
            List<bool> zapLineFromItemZapFlags = null;

            yield return new WaitForSeconds(ZapAppearDuration);
            yield return new WaitForSeconds(ZapMoveDuration);

            // 사용자 보고 버그(ZapStart→ZapIdle 반복) 방어: 컨트롤러 전환에 의존하지 않고 ZapAttackIdle을 코드로 1회 강제 진입.
            if (!_isZapAnimationPlaying && _zapAnimator != null)
            {
                _isZapAnimationPlaying = true;
                _zapAnimator.Play("ZapAttackIdle", 0, 0f);
            }

            int fieldRemoved = 0;
            if (_zapTargets.Count > 0)
            {
                zapLineObjects = CreateZapLineObjects(zapObject, ZapLineConcurrentCount, out zapLineFromItemZapFlags);
                if (zapLineObjects != null && zapLineObjects.Count > 0)
                {
                    _zapLineBaselines.Clear();
                    _zapLineJiggleCo = StartCoroutine(JiggleZapLinesRoutine(zapLineObjects));
                    yield return null;
                }

                // ItemZap.prefab Animator는 생성과 동시에 ZapStart → ZapAttackIdle을 1회 자동 진행한다.
                // FxZapLine 출력 구간 동안에는 zapObject에 Animator.Play/CrossFade/Rebind/SetActive 등 어떤 형태로도
                // 재진입을 트리거하지 마라 — ZapAttackIdle이 처음부터 다시 재생되어 연출이 깨진다.
                // ZAP을 새로 사용할 때마다 ItemZap 인스턴스가 새로 생성되므로 ZapStart부터의 자연스러운 재생은 그쪽에서 보장된다.

                // ROLLBACK_ZAP_FIXED_TOTAL_POP_TIME:
                // Do not multiply a minimum interval by target count. The full balloon-pop
                // pass must stay inside the remaining 2s item-effect budget, so dense boards
                // can pop multiple targets in the same frame instead of stretching the item
                // effect for seconds.
                float popDurationBudget = Mathf.Max(
                    0.1f,
                    ZapMaxTotalEffectDuration - ZapSelectionHighlightDelay - ZapAppearDuration - ZapMoveDuration - ZapLineLifetime);
                float popStartTime = Time.time;
                float stepDelay = _zapTargets.Count > 1
                    ? popDurationBudget / (_zapTargets.Count - 1)
                    : 0f;
                float lineLeadBeforePop = stepDelay >= ZapMinLeadInterval
                    ? Mathf.Min(ZapLineLeadBeforePop, stepDelay * 0.5f)
                    : 0f;

                for (int i = 0; i < _zapTargets.Count; i++)
                {
                    if (stepDelay > 0f)
                    {
                        float targetTime = popStartTime + stepDelay * i;
                        while (Time.time < targetTime)
                            yield return null;
                    }

                    ZapTarget target = _zapTargets[i];
                    Vector3 targetPosition = GetZapEffectPosition(target.position);
                    float lineVisibleDuration = Mathf.Max(ZapLineLifetime, stepDelay + lineLeadBeforePop);
                    Vector3 lineStartPosition = zapFixedPosition;
                    ConfigureZapLineFan(zapLineObjects, lineStartPosition, targetPosition, lineVisibleDuration);

                    // ROLLBACK_ZAP_LINE_PREPOP_LEAD:
                    // Give FxZapLine a rendered moment only when the total-time budget can afford
                    // it. For dense boards, forcing this wait once per target breaks the 2s cap.
                    if (lineLeadBeforePop > 0f)
                        yield return new WaitForSeconds(lineLeadBeforePop);

                    if (TryPopZapTarget(target.balloonId))
                        fieldRemoved++;
                }

                if (zapLineObjects != null && zapLineObjects.Count > 0)
                    yield return new WaitForSeconds(ZapLineLifetime);
            }

            int totalRemoved = fieldRemoved + RemoveRailAndQueueColor(color);
            FinalizeColorRemove(color, totalRemoved);

            PlayZapFinish(zapObject);

            // 라인을 SetActive(false)/Destroy 하기 전에 자글거림 코루틴을 중지해야
            // 비활성/파괴된 LightningBoltScript 에 Trigger() 가 호출되는 것을 막을 수 있다.
            StopZapLineJiggle();

            if (zapLineObjects != null)
            {
                for (int i = 0; i < zapLineObjects.Count; i++)
                {
                    GameObject line = zapLineObjects[i];
                    if (line == null) continue;
                    bool fromItemZap = zapLineFromItemZapFlags != null && i < zapLineFromItemZapFlags.Count
                        ? zapLineFromItemZapFlags[i]
                        : false;
                    if (fromItemZap)
                        line.SetActive(false);
                    else
                        Destroy(line, ZapLineLifetime);
                }
            }
            if (zapObject != null)
                Destroy(zapObject, ZapFinishLifetime);

            if (CameraManager.HasInstance)
                CameraManager.Instance.MoveBack();

            SetHudBottomPanelHiddenForZap(false);
            ResumeRail();
            _zapTargets.Clear();
            _isColorRemoveSequenceRunning = false;
            _isZapAnimationPlaying = false;
            _zapAnimator = null;
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
            int totalRemoved = PopFieldBalloonsImmediately(color);
            totalRemoved += RemoveRailAndQueueColor(color);
            FinalizeColorRemove(color, totalRemoved);
        }

        private int PopFieldBalloonsImmediately(int color)
        {
            int removed = 0;
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
                            removed++;
                        }
                    }
                }
            }

            return removed;
        }

        private int RemoveRailAndQueueColor(int color)
        {
            int removed = 0;

            if (RailManager.HasInstance)
            {
                int slotCount = RailManager.Instance.SlotCount;
                for (int i = 0; i < slotCount; i++)
                {
                    var slot = RailManager.Instance.GetSlot(i);
                    if (slot.dartColor == color)
                    {
                        RailManager.Instance.ClearSlot(i);
                        removed++;
                    }
                }
            }

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

                            if (HolderVisualManager.HasInstance)
                                HolderVisualManager.Instance.RemoveHolderVisual(hid);

                            HolderManager.Instance.UndoDeploy(hid);
                            holders[i].magazineCount = 0;
                            holders[i].isConsumed = true;
                            removed++;
                        }
                    }
                }
            }

            if (HolderManager.HasInstance)
                HolderManager.Instance.CompactColumns();
            if (HolderVisualManager.HasInstance)
                HolderVisualManager.Instance.RefreshAllPositions();

            return removed;
        }

        private void FinalizeColorRemove(int color, int totalRemoved)
        {
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

        private void SetHudBottomPanelHiddenForZap(bool hidden)
        {
            // ROLLBACK_ZAP_HUD_BOTTOM_PANEL_SHIFT:
            // Keep the item panel below the screen while the authored Zap attack/finish
            // sequence is playing, then restore it after the effect completes.
            UIHud hud = FindAnyObjectByType<UIHud>();
            if (hud == null)
                return;

            if (hidden)
                hud.HideBottomPanel();
            else
                hud.ShowBottomPanel();
        }

        private void CollectZapTargets(int color)
        {
            _zapTargets.Clear();

            if (!BalloonController.HasInstance)
                return;

            BalloonData[] balloons = BalloonController.Instance.GetAllBalloonsByColor(color);
            if (balloons == null)
                return;

            for (int i = 0; i < balloons.Length; i++)
            {
                BalloonData data = balloons[i];
                if (data == null || data.isPopped)
                    continue;

                _zapTargets.Add(new ZapTarget
                {
                    balloonId = data.balloonId,
                    position = BalloonController.Instance.GetBalloonWorldPosition(data.balloonId)
                });
            }

            _zapTargets.Sort((a, b) =>
            {
                int z = a.position.z.CompareTo(b.position.z);
                if (z != 0) return z;
                return a.position.x.CompareTo(b.position.x);
            });
        }

        private bool TryPopZapTarget(int balloonId)
        {
            if (!BalloonController.HasInstance)
                return false;

            BalloonData data = BalloonController.Instance.GetBalloon(balloonId);
            if (data == null || data.isPopped)
                return false;

            BalloonController.Instance.ForcePopBalloon(balloonId);
            return true;
        }

        private Vector3 GetZapSpawnPosition(Vector3 attackPosition)
        {
            // ROLLBACK_ZAP_SPAWN_Y_OFFSET: ItemZap 등장 위치를 고정 좌표로 사용 (기존 동적 계산 제거).
            // fixed spawn — attackPosition unused, kept for caller compatibility.
            return ZapSpawnPosition;
        }

        private Vector3 GetZapAttackPosition()
        {
            if (_zapTargets.Count <= 0)
                return GetZapFallbackPosition();

            float minX = _zapTargets[0].position.x;
            float maxX = _zapTargets[0].position.x;
            for (int i = 1; i < _zapTargets.Count; i++)
            {
                Vector3 position = _zapTargets[i].position;
                if (position.x < minX) minX = position.x;
                if (position.x > maxX) maxX = position.x;
            }

            float cellSpacing = GameManager.HasInstance ? GameManager.Instance.Board.cellSpacing : 0.55f;
            float fieldMinZ = GetActiveFieldMinZ();
            // ROLLBACK_ZAP_FIELD_BOTTOM_ANCHOR:
            // The Zap item must enter from the bottom of the whole field, not from the first
            // selected-color row. Selected colors that only exist in middle rows otherwise spawn
            // the Zap inside the balloon grid.
            Vector3 attackPosition = new Vector3(
                (minX + maxX) * 0.5f,
                0f,
                fieldMinZ - cellSpacing * ZapFieldBottomPaddingCells);
            return GetZapEffectPosition(attackPosition);
        }

        private float GetActiveFieldMinZ()
        {
            if (!BalloonController.HasInstance)
                return _zapTargets.Count > 0 ? _zapTargets[0].position.z : 0f;

            BalloonData[] balloons = BalloonController.Instance.GetAllBalloons();
            bool found = false;
            float minZ = 0f;
            if (balloons != null)
            {
                for (int i = 0; i < balloons.Length; i++)
                {
                    BalloonData data = balloons[i];
                    if (data == null || data.isPopped)
                        continue;

                    Vector3 position = BalloonController.Instance.GetBalloonWorldPosition(data.balloonId);
                    if (!found || position.z < minZ)
                    {
                        minZ = position.z;
                        found = true;
                    }
                }
            }

            return found
                ? minZ
                : (_zapTargets.Count > 0 ? _zapTargets[0].position.z : 0f);
        }

        private Vector3 GetZapFinishPosition()
        {
            if (_zapTargets.Count <= 0)
                return GetZapFallbackPosition();

            Vector3 lastPosition = _zapTargets[_zapTargets.Count - 1].position;
            return GetZapEffectPosition(lastPosition);
        }

        private Vector3 GetZapFallbackPosition()
        {
            if (GameManager.HasInstance)
            {
                return GetZapEffectPosition(new Vector3(
                    GameManager.Instance.Board.boardCenterX,
                    0f,
                    GameManager.Instance.Board.boardCenterZ));
            }

            return GetZapEffectPosition(Vector3.zero);
        }

        private Vector3 GetZapEffectPosition(Vector3 position)
        {
            position.y += ZapEffectYOffset;
            return position;
        }

        private GameObject CreateItemZap(Vector3 attackPosition)
        {
            if (_itemZapPrefab == null)
                _itemZapPrefab = Resources.Load<GameObject>(Const.PREFAB_ITEM_ZAP);

            if (_itemZapPrefab == null)
            {
                Debug.LogWarning($"[BoosterExecutor] Missing ItemZap prefab at Resources/{Const.PREFAB_ITEM_ZAP}.");
                return null;
            }

            Vector3 spawnPosition = GetZapSpawnPosition(attackPosition);
            GameObject zapObject = Instantiate(_itemZapPrefab, spawnPosition, Quaternion.identity);
            if (zapObject != null)
            {
                _zapAnimator = zapObject.GetComponentInChildren<Animator>(true);
                if (_zapAnimator != null)
                {
                    _zapAnimator.keepAnimatorStateOnDisable = true;
                }
            }
            return zapObject;
        }

        private GameObject CreateZapLineObject(GameObject zapObject, out bool fromItemZap)
        {
            fromItemZap = false;
            if (zapObject != null)
            {
                // ROLLBACK_ITEMZAP_CHILD_LINE:
                // ItemZap.prefab owns FxZapLine. Prefer that child so the authored ZapStart /
                // ZapAttack / ZapFinish animation setup drives the same visual object.
                Transform childLine = FindChildRecursive(zapObject.transform, "FxZapLine");
                if (childLine != null)
                {
                    // ROLLBACK_ZAP_LINE_RUNTIME_CLONE:
                    // Do not drive the inactive child directly. The ItemZap animator can keep
                    // authored children disabled while ZapAttack/ZapFinish plays, so clone the
                    // line as an independent runtime effect and destroy it after the sequence.
                    GameObject clonedRuntimeLine = Instantiate(childLine.gameObject);
                    // FxZapLine_Runtime stays at fixed ZapSpawnPosition for its full lifetime per design.
                    clonedRuntimeLine.transform.position = ZapSpawnPosition;
                    clonedRuntimeLine.name = "FxZapLine_Runtime";
                    clonedRuntimeLine.SetActive(true);
                    fromItemZap = true;
                    return clonedRuntimeLine;
                }
            }

            if (_fxZapLinePrefab == null)
                _fxZapLinePrefab = Resources.Load<GameObject>(Const.PREFAB_FX_ZAP_LINE);

            if (_fxZapLinePrefab == null)
            {
                Debug.LogWarning($"[BoosterExecutor] Missing FxZapLine prefab at Resources/{Const.PREFAB_FX_ZAP_LINE}.");
                return null;
            }

            GameObject runtimeLine = Instantiate(_fxZapLinePrefab);
            // FxZapLine_Runtime stays at fixed ZapSpawnPosition for its full lifetime per design.
            runtimeLine.transform.position = ZapSpawnPosition;
            return runtimeLine;
        }

        // 4갈래(Fan) Zap 라인용 다중 복제 생성.
        // 앞쪽 절반은 FxZapLine 소스(자식 우선, 없으면 PREFAB_FX_ZAP_LINE), 뒤쪽 절반은 PREFAB_FX_ZAP_LINE2.
        // 라인별 origin(fromItemZap)을 fromItemZapFlags로 반환해 cleanup 분기에 사용한다.
        private List<GameObject> CreateZapLineObjects(GameObject zapObject, int count, out List<bool> fromItemZapFlags)
        {
            var lines = new List<GameObject>(Mathf.Max(1, count));
            fromItemZapFlags = new List<bool>(Mathf.Max(1, count));
            if (count <= 0)
                return lines;

            // FxZapLine source (앞쪽 절반): ItemZap 자식 우선, 없으면 PREFAB_FX_ZAP_LINE.
            GameObject zapLineSource = null;
            bool zapLineSourceFromItemZap = false;

            if (zapObject != null)
            {
                Transform childLine = FindChildRecursive(zapObject.transform, "FxZapLine");
                if (childLine != null)
                {
                    zapLineSource = childLine.gameObject;
                    zapLineSourceFromItemZap = true;
                }
            }

            if (zapLineSource == null)
            {
                if (_fxZapLinePrefab == null)
                    _fxZapLinePrefab = Resources.Load<GameObject>(Const.PREFAB_FX_ZAP_LINE);
                zapLineSource = _fxZapLinePrefab;
                zapLineSourceFromItemZap = false;
            }

            int halfCount = count / 2;

            for (int i = 0; i < count; i++)
            {
                bool useFxZapLine = i < halfCount;
                GameObject source;
                bool sourceFromItemZap;
                string runtimeName;

                if (useFxZapLine)
                {
                    if (zapLineSource == null)
                    {
                        Debug.LogWarning($"[BoosterExecutor] Missing FxZapLine prefab at Resources/{Const.PREFAB_FX_ZAP_LINE}.");
                        continue;
                    }
                    source = zapLineSource;
                    sourceFromItemZap = zapLineSourceFromItemZap;
                    runtimeName = $"FxZapLine_Runtime_{i}";
                }
                else
                {
                    if (_fxZapLine2Prefab == null)
                        _fxZapLine2Prefab = Resources.Load<GameObject>(Const.PREFAB_FX_ZAP_LINE2);

                    if (_fxZapLine2Prefab == null)
                    {
                        Debug.LogWarning($"[BoosterExecutor] Missing FxZapLine2 prefab at Resources/{Const.PREFAB_FX_ZAP_LINE2}.");
                        continue;
                    }
                    source = _fxZapLine2Prefab;
                    sourceFromItemZap = false;
                    runtimeName = $"FxZapLine2_Runtime_{i - halfCount}";
                }

                GameObject runtimeLine = Instantiate(source);
                runtimeLine.transform.position = ZapSpawnPosition;
                runtimeLine.name = runtimeName;
                runtimeLine.SetActive(true);
                lines.Add(runtimeLine);
                fromItemZapFlags.Add(sourceFromItemZap);
            }

            return lines;
        }

        private void ConfigureZapLine(GameObject zapLineObject, Vector3 startPosition, Vector3 endPosition, float visibleDuration)
        {
            if (zapLineObject == null)
                return;

            zapLineObject.SetActive(true);

            Transform startTransform = FindChildRecursive(zapLineObject.transform, "LightningStart");
            Transform endTransform = FindChildRecursive(zapLineObject.transform, "LightningEnd");
            Vector3 lineStartPosition = GetZapLineRenderPosition(startPosition);
            Vector3 lineEndPosition = GetZapLineRenderPosition(endPosition);
            if (startTransform != null) startTransform.position = lineStartPosition;
            if (endTransform != null) endTransform.position = lineEndPosition;

            LightningBoltScript bolt = zapLineObject.GetComponentInChildren<LightningBoltScript>(true);
            if (bolt != null)
            {
                LineRenderer lineRenderer = bolt.GetComponent<LineRenderer>();
                PrepareZapLineRenderer(lineRenderer);
                // ROLLBACK_ZAP_LINE_FORCE_WORLD_SPACE:
                // ItemZap owns FxZapLine as an animated child. Force this particular lightning
                // renderer to world-space so the line always connects the Zap object and target
                // balloon instead of inheriting animated local offsets from the prefab.
                bolt.StartObject = null;
                bolt.EndObject = null;
                bolt.StartPosition = lineStartPosition;
                bolt.EndPosition = lineEndPosition;
                // ROLLBACK_ZAP_LINE_KEEP_WORLD_DEPTH:
                // In this game the board uses world Z as field depth. The generic lightning
                // script flattens Z for orthographic cameras, which makes Zap lines move only
                // left/right instead of connecting the Zap model to each balloon.
                bolt.PreserveDepthInOrthographic = true;
                // ROLLBACK_ZAP_LINE_CONTINUOUS_VISIBILITY:
                // ManualMode clears the LineRenderer after Duration. Keep each bolt visible until
                // the next Zap target so the effect does not blink out between balloon pops.
                bolt.ManualMode = true;
                bolt.Duration = Mathf.Max(0.01f, visibleDuration);
                bolt.Trigger();

                // 자글거림 코루틴이 매 tick 절대 좌표로 재설정할 수 있도록 baseline 기록.
                // (jitter 누적 드리프트 방지 — 코루틴은 baseline + jitter 로 항상 새 절대값을 쓴다.)
                _zapLineBaselines[bolt] = new ZapLineBaseline { start = lineStartPosition, end = lineEndPosition };
            }
        }

        // 4갈래 Zap 라인을 동일 타이밍에 ConfigureZapLine으로 활성화.
        // 공통 시작점=startPosition, 끝점=endPosition + perp * {-1.5Δ, -0.5Δ, +0.5Δ, +1.5Δ}.
        private void ConfigureZapLineFan(List<GameObject> zapLineObjects, Vector3 startPosition, Vector3 endPosition, float visibleDuration)
        {
            if (zapLineObjects == null || zapLineObjects.Count == 0)
                return;

            int count = zapLineObjects.Count;

            // 라인 수가 ZapLineConcurrentCount(=4)가 아니면 가드: 모두 동일 endPosition 사용.
            if (count != ZapLineConcurrentCount)
            {
                for (int i = 0; i < count; i++)
                    ConfigureZapLine(zapLineObjects[i], startPosition, endPosition, visibleDuration);
                return;
            }

            Vector3 dir = endPosition - startPosition;
            dir.y = 0f;
            Vector3 perp;
            if (dir.sqrMagnitude < 0.0001f)
            {
                perp = Vector3.right;
            }
            else
            {
                perp = Vector3.Cross(dir.normalized, Vector3.up).normalized;
                if (perp.sqrMagnitude < 0.0001f)
                    perp = Vector3.right;
            }

            float[] offsets = {
                -1.5f * ZapLineForkOffset,
                -0.5f * ZapLineForkOffset,
                 0.5f * ZapLineForkOffset,
                 1.5f * ZapLineForkOffset
            };
            for (int i = 0; i < count; i++)
            {
                Vector3 forkedEnd = endPosition + perp * offsets[i];
                ConfigureZapLine(zapLineObjects[i], startPosition, forkedEnd, visibleDuration);
            }
        }

        // FxZapLine 라인이 활성화된 동안 매 tick LightningBoltScript.Trigger() 를 재호출하여
        // 번개 모양이 매번 새로 그려지도록(자글거리도록) 한다. 끝점에는 미세 jitter 만 더해
        // 라인이 잡고 있는 시작/타겟 방향성은 유지한다.
        private IEnumerator JiggleZapLinesRoutine(List<GameObject> zapLineObjects)
        {
            if (zapLineObjects == null || zapLineObjects.Count == 0)
                yield break;

            // 시작 시 한 번만 LightningBoltScript 캐시. ConfigureZapLine 이 baseline 을
            // 매번 갱신하므로 코루틴은 jitter 적용만 담당한다.
            var bolts = new List<LightningBoltScript>(zapLineObjects.Count);
            for (int i = 0; i < zapLineObjects.Count; i++)
            {
                GameObject line = zapLineObjects[i];
                if (line == null)
                    continue;
                LightningBoltScript bolt = line.GetComponentInChildren<LightningBoltScript>(true);
                if (bolt != null)
                    bolts.Add(bolt);
            }

            if (bolts.Count == 0)
                yield break;

            while (true)
            {
                bool anyActive = false;
                for (int i = 0; i < zapLineObjects.Count; i++)
                {
                    GameObject line = zapLineObjects[i];
                    if (line == null || !line.activeInHierarchy)
                        continue;

                    LightningBoltScript bolt = i < bolts.Count ? bolts[i] : null;
                    if (bolt == null)
                        continue;

                    if (!_zapLineBaselines.TryGetValue(bolt, out ZapLineBaseline baseline))
                        continue;

                    anyActive = true;

                    Vector3 jitterStart = new Vector3(
                        Random.Range(-ZapLineJiggleEndpointJitter, ZapLineJiggleEndpointJitter),
                        Random.Range(-ZapLineJiggleEndpointJitter, ZapLineJiggleEndpointJitter),
                        Random.Range(-ZapLineJiggleEndpointJitter, ZapLineJiggleEndpointJitter));
                    Vector3 jitterEnd = new Vector3(
                        Random.Range(-ZapLineJiggleEndpointJitter, ZapLineJiggleEndpointJitter),
                        Random.Range(-ZapLineJiggleEndpointJitter, ZapLineJiggleEndpointJitter),
                        Random.Range(-ZapLineJiggleEndpointJitter, ZapLineJiggleEndpointJitter));

                    bolt.StartPosition = baseline.start + jitterStart;
                    bolt.EndPosition = baseline.end + jitterEnd;
                    // 다음 tick 까지 라인이 사라지지 않도록 Duration 보강(ConfigureZapLine 의 값이 너무 짧을 때 대비).
                    bolt.Duration = Mathf.Max(bolt.Duration, ZapLineJiggleMaxInterval * 1.5f);
                    bolt.Trigger();
                }

                if (!anyActive)
                    yield break;

                yield return new WaitForSeconds(Random.Range(ZapLineJiggleMinInterval, ZapLineJiggleMaxInterval));
            }
        }

        private void StopZapLineJiggle()
        {
            if (_zapLineJiggleCo != null)
            {
                StopCoroutine(_zapLineJiggleCo);
                _zapLineJiggleCo = null;
            }
            _zapLineBaselines.Clear();
        }

        private Vector3 GetZapLineRenderPosition(Vector3 position)
        {
            // ROLLBACK_ZAP_LINE_RENDER_LIFT:
            // Keep the lightning slightly above holder/balloon meshes. This affects only the
            // visual line, not Zap pop timing or target selection.
            position.y += ZapLineWorldLift;
            return position;
        }

        private void PrepareZapLineRenderer(LineRenderer lineRenderer)
        {
            if (lineRenderer == null)
                return;

            lineRenderer.enabled = true;
            lineRenderer.useWorldSpace = true;
            lineRenderer.shadowCastingMode = ShadowCastingMode.Off;
            lineRenderer.receiveShadows = false;
            lineRenderer.sortingOrder = Mathf.Max(lineRenderer.sortingOrder, ZapLineSortingOrder);
            lineRenderer.widthMultiplier = Mathf.Max(lineRenderer.widthMultiplier, ZapLineMinWidth);
            lineRenderer.numCapVertices = Mathf.Max(lineRenderer.numCapVertices, 2);
        }

        private Transform FindChildRecursive(Transform root, string childName)
        {
            if (root == null)
                return null;

            Transform[] children = root.GetComponentsInChildren<Transform>(true);
            for (int i = 0; i < children.Length; i++)
            {
                if (children[i].name == childName)
                    return children[i];
            }

            return null;
        }

        private void PlayZapFinish(GameObject zapObject)
        {
            if (zapObject == null)
                return;
            _isZapAnimationPlaying = false;

            // ROLLBACK_ZAP_FINISH_STAY_ON_ATTACK_ORIGIN:
            // ZapFinish is the authored finish animation for the Zap model. Do not snap the
            // model to the last popped balloon; FxZapLine already points at each target.

            bool triggered = TrySetZapFinishTrigger(zapObject);
            ParticleSystem[] particles = zapObject.GetComponentsInChildren<ParticleSystem>(true);
            for (int i = 0; i < particles.Length; i++)
            {
                particles[i].Clear(true);
                particles[i].Play(true);
            }

            if (!triggered)
                zapObject.transform.DOPunchScale(Vector3.one * 0.15f, ZapFinishLifetime, 6, 0.4f);
        }

        private bool TrySetZapFinishTrigger(GameObject zapObject)
        {
            return TrySetZapTrigger(zapObject, "ZapFinish", "Finish", "Zap Finish Trigger");
        }

        private bool TrySetZapTrigger(GameObject zapObject, params string[] triggerNames)
        {
            Animator animator = zapObject.GetComponentInChildren<Animator>(true);
            if (animator == null)
                return false;

            for (int i = 0; i < animator.parameters.Length; i++)
            {
                AnimatorControllerParameter parameter = animator.parameters[i];
                if (parameter.type != AnimatorControllerParameterType.Trigger)
                    continue;

                for (int j = 0; j < triggerNames.Length; j++)
                {
                    if (parameter.name == triggerNames[j])
                    {
                        animator.SetTrigger(parameter.name);
                        return true;
                    }
                }
            }

            return false;
        }

        // Hand booster now uses SELECT_TOOL behavior (holder selection mode).
        // The HAND case in HandleBoosterUsed sets _awaitingHolderSelection = true
        // and moves camera to queue, identical to SELECT_TOOL.
        // OnHolderSelected handles the actual deployment for both boosters.

        #endregion
    }
}
