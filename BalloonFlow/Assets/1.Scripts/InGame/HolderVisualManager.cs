using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using DG.Tweening;
using TMPro;

namespace BalloonFlow
{
    /// <summary>
    /// Manages visual representation of holders in Rail Overflow mode.
    /// Holders sit in a column-based queue, move up to the rail when selected,
    /// deploy darts onto empty passing slots, then disappear when magazine=0.
    /// Per column: 1 deploying (at rail) + 1 waiting (just below).
    /// </summary>
    /// <remarks>
    /// Layer: Game | Genre: Puzzle | Role: Manager | Phase: 1
    /// DB Reference: Generated from Rail Overflow spec — column queue visual system
    /// </remarks>
    public class HolderVisualManager : SceneSingleton<HolderVisualManager>
    {
        #region Constants

        private const string HOLDER_POOL_KEY = "Holder";
        private const float QUEUE_BASE_Z = -5.0f;    // Front row Z
        private const float QUEUE_ROW_SPACING = 1.5f;  // Rows go south
        private const float COLUMN_SPACING = 1.4f;
        private const int MAX_COLUMNS = 5;
        private const int MAGAZINE_FONT_SIZE = 5;
        private const float DEPLOY_MOVE_SPEED = 12f;

        #endregion

        #region Color Palette

        private static readonly Color[] COLORS =
        {
            new Color(0.95f, 0.25f, 0.25f), // 0: Red
            new Color(0.25f, 0.55f, 0.95f), // 1: Blue
            new Color(0.25f, 0.85f, 0.35f), // 2: Green
            new Color(0.95f, 0.85f, 0.15f), // 3: Yellow
            new Color(0.80f, 0.30f, 0.90f), // 4: Purple
            new Color(0.95f, 0.55f, 0.15f), // 5: Orange
            new Color(0.40f, 0.90f, 0.90f), // 6: Cyan
            new Color(0.95f, 0.50f, 0.70f), // 7: Pink
        };

        #endregion

        #region Nested Types

        private class HolderVisual
        {
            public int holderId;
            public int color;
            public int column;
            public int magazineRemaining;
            public GameObject gameObject;
            public Renderer meshRenderer;
            public TMP_Text magazineText;
            public Vector3 queuePosition;
            public bool isDeploying;     // at rail, deploying darts
            public bool isWaiting;       // just below deploying holder
            public bool isMovingToRail;
        }

        #endregion

        #region Fields

        private readonly Dictionary<int, HolderVisual> _holderVisuals = new Dictionary<int, HolderVisual>();
        private readonly HashSet<int> _cancelledHolders = new HashSet<int>();
        private int _queueColumns = 5;

        #endregion

        #region Lifecycle

        protected override void OnSingletonAwake()
        {
        }

        private bool _boardFinished;

        private void OnEnable()
        {
            EventBus.Subscribe<OnLevelLoaded>(HandleLevelLoaded);
            EventBus.Subscribe<OnHolderSelected>(HandleHolderSelected);
            EventBus.Subscribe<OnMagazineEmpty>(HandleMagazineEmpty);
            EventBus.Subscribe<OnBoardCleared>(HandleBoardCleared);
            EventBus.Subscribe<OnBoardFailed>(HandleBoardFailed);
            EventBus.Subscribe<OnContinueApplied>(HandleContinueApplied);
        }

        private void OnDisable()
        {
            EventBus.Unsubscribe<OnLevelLoaded>(HandleLevelLoaded);
            EventBus.Unsubscribe<OnHolderSelected>(HandleHolderSelected);
            EventBus.Unsubscribe<OnMagazineEmpty>(HandleMagazineEmpty);
            EventBus.Unsubscribe<OnBoardCleared>(HandleBoardCleared);
            EventBus.Unsubscribe<OnBoardFailed>(HandleBoardFailed);
            EventBus.Unsubscribe<OnContinueApplied>(HandleContinueApplied);
        }

        #endregion

        #region Public Methods

        /// <summary>
        /// Spawns visual holder GameObjects in the queue based on HolderManager data.
        /// </summary>
        public void SpawnWaitingHolders()
        {
            _boardFinished = false;
            ClearAllVisuals();

            if (!HolderManager.HasInstance) return;

            HolderData[] holders = HolderManager.Instance.GetHolders();
            if (holders == null || holders.Length == 0) return;

            _queueColumns = HolderManager.Instance.QueueColumns;

            // Group by column
            var columnQueues = new Dictionary<int, List<HolderData>>();
            for (int i = 0; i < holders.Length; i++)
            {
                HolderData data = holders[i];
                if (data.isConsumed) continue;

                if (!columnQueues.ContainsKey(data.column))
                    columnQueues[data.column] = new List<HolderData>();
                columnQueues[data.column].Add(data);
            }

            // Spawn per column, row by row
            foreach (var kvp in columnQueues)
            {
                int col = kvp.Key;
                var colHolders = kvp.Value;

                for (int row = 0; row < colHolders.Count; row++)
                {
                    Vector3 pos = CalculateQueuePosition(col, row);
                    HolderVisual visual = CreateHolderVisual(colHolders[row], pos, col);
                    if (visual != null)
                    {
                        _holderVisuals[colHolders[row].holderId] = visual;
                    }
                }
            }
        }

        /// <summary>
        /// Returns the color from the palette for the given index.
        /// </summary>
        public static Color GetColor(int colorIndex)
        {
            if (colorIndex >= 0 && colorIndex < COLORS.Length)
                return COLORS[colorIndex];
            return Color.white;
        }

        /// <summary>
        /// Returns true if the holder is in the front row (row 0) of its column.
        /// Only front-row holders are clickable.
        /// </summary>
        public bool IsInFrontRow(int holderId)
        {
            if (!_holderVisuals.TryGetValue(holderId, out HolderVisual visual))
                return false;
            if (visual.isDeploying || visual.isMovingToRail || visual.gameObject == null)
                return false;

            float holderZ = visual.gameObject.transform.position.z;
            return holderZ >= QUEUE_BASE_Z - QUEUE_ROW_SPACING * 0.5f;
        }

        /// <summary>
        /// Clears all holder visuals and returns objects to pool.
        /// </summary>
        public void ClearAllVisuals()
        {
            StopAllCoroutines();
            _cancelledHolders.Clear();

            foreach (var kvp in _holderVisuals)
            {
                ReturnHolderToPool(kvp.Value);
            }
            _holderVisuals.Clear();
        }

        /// <summary>
        /// Cancels an active deploy coroutine for the given holder and returns it to queue position.
        /// Called by ContinueHandler when reverting active holders.
        /// </summary>
        public void CancelDeployAndReturnToQueue(int holderId)
        {
            _cancelledHolders.Add(holderId);

            if (!_holderVisuals.TryGetValue(holderId, out HolderVisual visual)) return;

            visual.isDeploying = false;
            visual.isWaiting = false;
            visual.isMovingToRail = false;

            // Kill any active DOTween on this object
            if (visual.gameObject != null)
            {
                visual.gameObject.transform.DOKill();
            }

            // Move back to queue
            RepositionColumnHolders(visual.column);
        }

        /// <summary>
        /// Removes a holder visual immediately (e.g. Color Remove booster consumed it).
        /// </summary>
        public void RemoveHolderVisual(int holderId)
        {
            _cancelledHolders.Add(holderId);

            if (!_holderVisuals.TryGetValue(holderId, out HolderVisual visual)) return;

            if (visual.gameObject != null)
                visual.gameObject.transform.DOKill();

            ReturnHolderToPool(visual);
            _holderVisuals.Remove(holderId);
        }

        /// <summary>
        /// Refreshes all visual positions from HolderData columns.
        /// Called after Shuffle booster changes column assignments.
        /// </summary>
        public void RefreshAllPositions()
        {
            if (!HolderManager.HasInstance) return;
            HolderData[] holders = HolderManager.Instance.GetHolders();
            if (holders == null) return;

            // Sync visual column from data column
            for (int i = 0; i < holders.Length; i++)
            {
                if (_holderVisuals.TryGetValue(holders[i].holderId, out HolderVisual visual))
                {
                    visual.column = holders[i].column;
                }
            }

            // Reposition all columns
            for (int col = 0; col < _queueColumns; col++)
            {
                RepositionColumnHolders(col);
            }
        }

        /// <summary>
        /// IsRailFull is no longer relevant in Rail Overflow mode.
        /// Always returns false — dart deployment is gated by slot availability, not holder count.
        /// </summary>
        public bool IsRailFull()
        {
            return false;
        }

        public int GetOnRailCount()
        {
            int count = 0;
            foreach (var kvp in _holderVisuals)
            {
                if (kvp.Value.isDeploying || kvp.Value.isMovingToRail) count++;
            }
            return count;
        }

        #endregion

        #region Private Methods — Queue Positioning

        private Vector3 CalculateQueuePosition(int column, int row)
        {
            float totalWidth = (_queueColumns - 1) * COLUMN_SPACING;
            float startX = -totalWidth * 0.5f;

            float x = startX + column * COLUMN_SPACING;
            float z = QUEUE_BASE_Z - row * QUEUE_ROW_SPACING;

            return new Vector3(x, 0.25f, z);
        }

        /// <summary>
        /// Returns the deploy point — where a holder attaches to the rail bottom edge
        /// to start deploying darts onto passing empty slots.
        /// </summary>
        private Vector3 GetDeployPoint(int column)
        {
            if (!RailManager.HasInstance) return CalculateQueuePosition(column, 0) + Vector3.forward * 2f;

            // Deploy point = bottom rail edge, aligned to column X
            float totalWidth = (_queueColumns - 1) * COLUMN_SPACING;
            float startX = -totalWidth * 0.5f;
            float x = startX + column * COLUMN_SPACING;

            // Get the bottom rail Y from waypoints
            Vector3[] path = RailManager.Instance.GetRailPath();
            float railY = 0.5f;
            float railZ = 0f;
            if (path != null && path.Length > 0)
            {
                // Bottom rail = lowest Z waypoint
                railY = path[0].y;
                railZ = float.MaxValue;
                for (int i = 0; i < path.Length; i++)
                {
                    if (path[i].z < railZ)
                        railZ = path[i].z;
                }
            }

            return new Vector3(x, railY, railZ);
        }

        private void RepositionColumnHolders(int column)
        {
            if (!HolderManager.HasInstance) return;

            var colHolders = new List<HolderVisual>();
            foreach (var kvp in _holderVisuals)
            {
                HolderVisual v = kvp.Value;
                if (v.column == column && !v.isDeploying && !v.isMovingToRail && v.gameObject != null)
                {
                    colHolders.Add(v);
                }
            }

            // Sort by current Z descending (front first)
            colHolders.Sort((a, b) => b.gameObject.transform.position.z.CompareTo(a.gameObject.transform.position.z));

            for (int row = 0; row < colHolders.Count; row++)
            {
                Vector3 targetPos = CalculateQueuePosition(column, row);
                if (Vector3.Distance(colHolders[row].gameObject.transform.position, targetPos) > 0.05f)
                {
                    float dist = Vector3.Distance(colHolders[row].gameObject.transform.position, targetPos);
                    colHolders[row].gameObject.transform.DOMove(targetPos, dist / 4f).SetEase(Ease.OutQuad);
                }
                colHolders[row].queuePosition = targetPos;
            }
        }

        #endregion

        #region Private Methods — Holder Visual Creation

        private HolderVisual CreateHolderVisual(HolderData data, Vector3 position, int column)
        {
            if (!ObjectPoolManager.HasInstance) return null;

            GameObject obj = ObjectPoolManager.Instance.Get(HOLDER_POOL_KEY, position, Quaternion.identity);
            if (obj == null) return null;

            obj.SetActive(true);

            HolderIdentifier identifier = obj.GetComponent<HolderIdentifier>();
            if (identifier != null)
            {
                identifier.SetHolderId(data.holderId);
            }

            Color holderColor = GetColor(data.color);
            ApplyColorToRenderers(obj, holderColor);

            TMP_Text textMesh = obj.GetComponentInChildren<TMP_Text>(true);
            if (textMesh != null)
            {
                textMesh.text = data.magazineCount.ToString();
                textMesh.color = Color.white;
                textMesh.fontSize = MAGAZINE_FONT_SIZE;
                textMesh.alignment = TextAlignmentOptions.Center;
            }

            return new HolderVisual
            {
                holderId = data.holderId,
                color = data.color,
                column = column,
                magazineRemaining = data.magazineCount,
                gameObject = obj,
                meshRenderer = obj.GetComponent<Renderer>(),
                magazineText = textMesh,
                queuePosition = position,
                isDeploying = false,
                isWaiting = false,
                isMovingToRail = false
            };
        }

        private static void ApplyColorToRenderers(GameObject obj, Color color)
        {
            Renderer[] renderers = obj.GetComponentsInChildren<Renderer>();
            for (int i = 0; i < renderers.Length; i++)
            {
                foreach (Material mat in renderers[i].materials)
                {
                    if (mat.HasProperty("_BaseColor"))
                        mat.SetColor("_BaseColor", color);
                    if (mat.HasProperty("_Color"))
                        mat.SetColor("_Color", color);
                }
            }
        }

        private void ReturnHolderToPool(HolderVisual visual)
        {
            if (visual.gameObject != null && ObjectPoolManager.HasInstance)
            {
                ObjectPoolManager.Instance.Return(HOLDER_POOL_KEY, visual.gameObject);
            }
            visual.gameObject = null;
        }

        #endregion

        #region Private Methods — Deploy Flow

        /// <summary>
        /// Moves a holder to a waiting position (just behind the deploy point).
        /// Called when the column already has a deploying holder.
        /// </summary>
        private void MoveToWaitingPosition(int holderId)
        {
            if (!_holderVisuals.TryGetValue(holderId, out HolderVisual visual))
                return;

            visual.isWaiting = true;

            Vector3 deployPoint = GetDeployPoint(visual.column);
            // Waiting position = 1.5 units behind the deploy point (toward queue)
            Vector3 waitPos = deployPoint + Vector3.back * 1.5f;

            if (visual.gameObject != null)
            {
                float dist = Vector3.Distance(visual.gameObject.transform.position, waitPos);
                visual.gameObject.transform.DOMove(waitPos, dist / DEPLOY_MOVE_SPEED).SetEase(Ease.OutQuad);
            }
        }

        /// <summary>
        /// Moves a holder from queue to deploy point, then starts deploying darts onto empty slots.
        /// </summary>
        private void StartDeploy(int holderId)
        {
            if (!_holderVisuals.TryGetValue(holderId, out HolderVisual visual))
                return;

            if (visual.isDeploying || visual.isMovingToRail) return;

            visual.isMovingToRail = true;
            RepositionColumnHolders(visual.column);
            StartCoroutine(DeployCoroutine(visual));
        }

        private IEnumerator DeployCoroutine(HolderVisual visual)
        {
            if (!RailManager.HasInstance || visual.gameObject == null) yield break;

            // Phase 1: Move to deploy point (bottom rail edge, aligned to column)
            Vector3 deployPoint = GetDeployPoint(visual.column);

            while (visual.gameObject != null)
            {
                if (_cancelledHolders.Contains(visual.holderId))
                {
                    _cancelledHolders.Remove(visual.holderId);
                    yield break;
                }
                Vector3 current = visual.gameObject.transform.position;
                float dist = Vector3.Distance(current, deployPoint);
                if (dist < 0.15f) break;

                Vector3 dir = (deployPoint - current).normalized;
                visual.gameObject.transform.position = current + dir * DEPLOY_MOVE_SPEED * Time.deltaTime;
                yield return null;
            }

            if (visual.gameObject != null)
                visual.gameObject.transform.position = deployPoint;

            // Phase 2: At rail — start deploying darts
            visual.isMovingToRail = false;
            visual.isDeploying = true;

            if (HolderManager.HasInstance)
            {
                HolderManager.Instance.ConfirmOnRail(visual.holderId);
            }

            RailManager rail = RailManager.Instance;

            // Find the starting slot nearest to deploy point
            int startSlot = rail.GetNearestSlotIndex(deployPoint);

            // Deploy darts one at a time: wait for empty slot passing nearby
            // NOTE: Darts are ALWAYS placed on rail regardless of matching balloons.
            // Unmatched darts accumulate on rail -> occupancy rises -> fail condition.
            // DartManager handles auto-fire when a match passes; we never skip placement.
            while (visual.magazineRemaining > 0 && visual.gameObject != null && !_boardFinished)
            {
                // Check if this holder was cancelled (continue, color remove, etc.)
                if (_cancelledHolders.Contains(visual.holderId))
                {
                    _cancelledHolders.Remove(visual.holderId);
                    yield break;
                }

                // Find next empty slot near deploy point
                int emptySlot = FindEmptySlotNearPosition(deployPoint);

                if (emptySlot < 0)
                {
                    // No empty slot available — wait
                    yield return null;
                    continue;
                }

                // Re-check board state right before placing (guards against race with OnBoardCleared)
                if (_boardFinished) break;

                // Place dart on slot
                int dartId = rail.PlaceDart(emptySlot, visual.color, visual.holderId);
                if (dartId < 0)
                {
                    yield return null;
                    continue;
                }

                visual.magazineRemaining--;

                // Update magazine text
                if (visual.magazineText != null)
                    visual.magazineText.text = visual.magazineRemaining.ToString();

                // Consume from HolderManager
                if (HolderManager.HasInstance)
                    HolderManager.Instance.ConsumeMagazine(visual.holderId);

                // Publish dart placed event
                EventBus.Publish(new OnDartPlacedOnSlot
                {
                    slotIndex = emptySlot,
                    color = visual.color,
                    holderId = visual.holderId
                });

                // Punch animation (optional — toggled via GameManager.Board.useDeployPunchScale)
                bool usePunch = GameManager.HasInstance && GameManager.Instance.Board.useDeployPunchScale;
                if (usePunch && visual.gameObject != null)
                {
                    visual.gameObject.transform.localScale = Vector3.one;
                    visual.gameObject.transform.DOPunchScale(Vector3.one * 0.08f, 0.08f, 6, 0.5f);
                }

                // Next frame — no artificial delay, deploy as fast as empty slots appear
                yield return null;
            }

            // Phase 3: Magazine empty — holder disappears
            CompleteDeployment(visual);
        }

        /// <summary>
        /// Finds an empty slot near a deploy position.
        /// Checks slots within a small window around the nearest slot to deployPos.
        /// </summary>
        private int FindEmptySlotNearPosition(Vector3 deployPos)
        {
            if (!RailManager.HasInstance) return -1;
            RailManager rail = RailManager.Instance;

            int nearestSlot = rail.GetNearestSlotIndex(deployPos);

            // Check a window of slots around the nearest
            int window = 3;
            for (int offset = 0; offset <= window; offset++)
            {
                int idx = (nearestSlot + offset) % rail.SlotCount;
                if (rail.IsSlotEmpty(idx))
                {
                    // Verify it's actually close to deploy position
                    Vector3 slotPos = rail.GetSlotWorldPosition(idx);
                    if (Vector3.Distance(slotPos, deployPos) < 2f)
                        return idx;
                }

                if (offset > 0)
                {
                    idx = (nearestSlot - offset + rail.SlotCount) % rail.SlotCount;
                    if (rail.IsSlotEmpty(idx))
                    {
                        Vector3 slotPos = rail.GetSlotWorldPosition(idx);
                        if (Vector3.Distance(slotPos, deployPos) < 2f)
                            return idx;
                    }
                }
            }

            return -1;
        }

        private void CompleteDeployment(HolderVisual visual)
        {
            int col = visual.column;
            visual.isDeploying = false;

            // Publish deployment done
            EventBus.Publish(new OnHolderDeploymentDone
            {
                holderId = visual.holderId,
                column = col
            });

            // Remove visual
            ReturnHolderToPool(visual);
            _holderVisuals.Remove(visual.holderId);

            // Reposition remaining holders in this column
            RepositionColumnHolders(col);
        }

        #endregion

        #region Private Methods — Event Handlers

        private void HandleLevelLoaded(OnLevelLoaded evt)
        {
            StartCoroutine(SpawnAfterDelay());
        }

        private IEnumerator SpawnAfterDelay()
        {
            yield return null;
            SpawnWaitingHolders();
        }

        private void HandleHolderSelected(OnHolderSelected evt)
        {
            // Check if this holder is in waiting state (another holder deploying in same column)
            if (HolderManager.HasInstance)
            {
                HolderData holderData = null;
                HolderData[] allHolders = HolderManager.Instance.GetHolders();
                for (int i = 0; i < allHolders.Length; i++)
                {
                    if (allHolders[i].holderId == evt.holderId)
                    {
                        holderData = allHolders[i];
                        break;
                    }
                }

                if (holderData != null && holderData.isWaiting)
                {
                    // Move to waiting position (just behind deploy point), do NOT start deploy
                    MoveToWaitingPosition(evt.holderId);
                    return;
                }
            }

            StartDeploy(evt.holderId);
        }

        private void HandleMagazineEmpty(OnMagazineEmpty evt)
        {
            // Magazine empty notification — deployment coroutine handles cleanup
        }

        private void HandleBoardCleared(OnBoardCleared evt)
        {
            _boardFinished = true;
            StopAllCoroutines();
            ClearAllVisuals();
        }

        private void HandleBoardFailed(OnBoardFailed evt)
        {
            _boardFinished = true;
            StopAllCoroutines();
        }

        private void HandleContinueApplied(OnContinueApplied evt)
        {
            _boardFinished = false;
            Debug.Log("[HolderVisualManager] Continue applied — holder deployment resumed.");
        }

        #endregion
    }
}
