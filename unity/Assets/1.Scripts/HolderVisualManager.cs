using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using DG.Tweening;

namespace BalloonFlow
{
    /// <summary>
    /// Manages the visual representation of holders: spawning in the waiting area,
    /// moving along the rail, firing darts at matching-color outermost balloons,
    /// and handling collision avoidance between on-rail holders.
    /// </summary>
    /// <remarks>
    /// Layer: Game | Genre: Puzzle | Role: Manager | Phase: 1
    /// DB Reference: No DB match found — generated from L3 YAML logicFlow
    /// </remarks>
    public class HolderVisualManager : Singleton<HolderVisualManager>
    {
        #region Constants

        private const string HOLDER_POOL_KEY = "Holder";
        private const string DART_POOL_KEY = "Dart";
        private const float DEFAULT_RAIL_SPEED = 7f;
        private const float DEFAULT_DART_FLIGHT_TIME = 0.03f; // 4x original (very fast)
        private const float FRONT_ROW_Z = -5.0f;    // Front row (closest to board/rail)
        private const float ROW_Z_SPACING = 1.5f;    // Rows go southward (lower Z)
        private const float COLUMN_SPACING = 1.4f;
        private const int HOLDERS_PER_ROW = 5;        // 5x5 grid layout
        private const float MIN_NORMALIZED_SPACING = 0.15f;
        private const int MAGAZINE_FONT_SIZE = 24;
        private const int DEFAULT_MAX_ON_RAIL = 9;

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

        #region Serialized Fields

        [SerializeField] private float _railSpeed = DEFAULT_RAIL_SPEED;
        [SerializeField] private float _dartFlightTime = DEFAULT_DART_FLIGHT_TIME;

        #endregion

        #region Nested Types

        /// <summary>
        /// Tracks the visual state of a single holder.
        /// </summary>
        private class HolderVisual
        {
            public int holderId;
            public int color;
            public int magazineRemaining;
            public GameObject gameObject;
            public Renderer meshRenderer;
            public TextMesh magazineText;
            public Vector3 waitingPosition;
            public bool isOnRail;
            public bool isMovingToRail;
            public float normalizedT;
        }

        /// <summary>
        /// Tracks an in-flight dart projectile.
        /// </summary>
        private class DartProjectile
        {
            public GameObject gameObject;
            public Vector3 startPosition;
            public Vector3 targetPosition;
            public int targetBalloonId;
            public float elapsed;
            public float duration;
        }

        #endregion

        #region Fields

        private readonly Dictionary<int, HolderVisual> _holderVisuals = new Dictionary<int, HolderVisual>();
        private readonly List<HolderVisual> _onRailHolders = new List<HolderVisual>();
        private readonly List<DartProjectile> _activeDartProjectiles = new List<DartProjectile>();
        private readonly List<float> _occupiedNormalizedPositions = new List<float>();

        // Deployment queue: prevents overlapping when clicking multiple holders quickly
        private readonly Queue<OnHolderSelected> _deploymentQueue = new Queue<OnHolderSelected>();
        private bool _isDeployingHolder;
        private int _maxOnRail = DEFAULT_MAX_ON_RAIL;

        #endregion

        #region Properties

        /// <summary>
        /// Rail movement speed in units per second.
        /// </summary>
        public float RailSpeed => _railSpeed;

        #endregion

        #region Lifecycle

        protected override void OnSingletonAwake()
        {
            // Initialization handled by event subscriptions
        }

        private void OnEnable()
        {
            EventBus.Subscribe<OnLevelLoaded>(HandleLevelLoaded);
            EventBus.Subscribe<OnHolderSelected>(HandleHolderSelected);
            EventBus.Subscribe<OnHolderReturned>(HandleHolderReturned);
            EventBus.Subscribe<OnMagazineEmpty>(HandleMagazineEmpty);
        }

        private void OnDisable()
        {
            EventBus.Unsubscribe<OnLevelLoaded>(HandleLevelLoaded);
            EventBus.Unsubscribe<OnHolderSelected>(HandleHolderSelected);
            EventBus.Unsubscribe<OnHolderReturned>(HandleHolderReturned);
            EventBus.Unsubscribe<OnMagazineEmpty>(HandleMagazineEmpty);
        }

        private void Update()
        {
            UpdateDartProjectiles();
        }

        #endregion

        #region Public Methods

        /// <summary>
        /// Spawns visual holder GameObjects in the waiting area based on HolderManager data.
        /// </summary>
        public void SpawnWaitingHolders()
        {
            ClearAllVisuals();

            if (!HolderManager.HasInstance)
            {
                Debug.LogWarning("[HolderVisualManager] HolderManager not available.");
                return;
            }

            HolderData[] holders = HolderManager.Instance.GetHolders();
            if (holders == null || holders.Length == 0)
            {
                return;
            }

            int waitingIndex = 0;
            for (int i = 0; i < holders.Length; i++)
            {
                HolderData data = holders[i];
                if (data.isDeployed || data.isOnRail)
                {
                    continue;
                }

                Vector3 position = CalculateWaitingPosition(waitingIndex);
                HolderVisual visual = CreateHolderVisual(data, position);
                if (visual != null)
                {
                    _holderVisuals[data.holderId] = visual;
                }

                waitingIndex++;
            }
        }

        /// <summary>
        /// Returns the color from the palette for the given index.
        /// </summary>
        public static Color GetColor(int colorIndex)
        {
            if (colorIndex >= 0 && colorIndex < COLORS.Length)
            {
                return COLORS[colorIndex];
            }
            return Color.white;
        }

        /// <summary>
        /// Returns true if the rail is at capacity (no more holders can be deployed).
        /// </summary>
        public bool IsRailFull()
        {
            int onRailCount = _onRailHolders.Count;
            foreach (var kvp in _holderVisuals)
            {
                if (kvp.Value.isMovingToRail) onRailCount++;
            }
            return onRailCount >= _maxOnRail;
        }

        /// <summary>
        /// Sets the maximum number of holders allowed on the rail simultaneously.
        /// </summary>
        public void SetMaxOnRail(int max)
        {
            _maxOnRail = Mathf.Max(1, max);
        }

        /// <summary>
        /// Returns the current number of holders on the rail (including those moving to rail).
        /// </summary>
        public int GetOnRailCount()
        {
            int count = _onRailHolders.Count;
            foreach (var kvp in _holderVisuals)
            {
                if (kvp.Value.isMovingToRail) count++;
            }
            return count;
        }

        /// <summary>
        /// Returns the maximum allowed holders on rail.
        /// </summary>
        public int GetMaxOnRail()
        {
            return _maxOnRail;
        }

        /// <summary>
        /// Returns true if the holder is in the front row (row 0) of the waiting area.
        /// Only front-row holders are clickable.
        /// </summary>
        public bool IsInFrontRow(int holderId)
        {
            if (!_holderVisuals.TryGetValue(holderId, out HolderVisual visual))
            {
                return false;
            }

            if (visual.isOnRail || visual.isMovingToRail || visual.gameObject == null)
            {
                return false;
            }

            // Front row = highest Z among waiting holders. Check if this holder's Z
            // is within tolerance of FRONT_ROW_Z (the front row position).
            float holderZ = visual.gameObject.transform.position.z;
            return holderZ >= FRONT_ROW_Z - ROW_Z_SPACING * 0.5f;
        }

        /// <summary>
        /// Clears all holder visuals and returns objects to pool.
        /// </summary>
        public void ClearAllVisuals()
        {
            StopAllCoroutines();

            _deploymentQueue.Clear();
            _isDeployingHolder = false;

            foreach (var kvp in _holderVisuals)
            {
                ReturnHolderToPool(kvp.Value);
            }
            _holderVisuals.Clear();
            _onRailHolders.Clear();
            _occupiedNormalizedPositions.Clear();

            // Clean up dart projectiles
            for (int i = _activeDartProjectiles.Count - 1; i >= 0; i--)
            {
                ReturnDartProjectileToPool(_activeDartProjectiles[i]);
            }
            _activeDartProjectiles.Clear();
        }

        #endregion

        #region Private Methods -- Waiting Area

        private Vector3 CalculateWaitingPosition(int index)
        {
            int row = index / HOLDERS_PER_ROW;
            int col = index % HOLDERS_PER_ROW;

            // Center the row horizontally
            float rowWidth = (HOLDERS_PER_ROW - 1) * COLUMN_SPACING;
            float startX = -rowWidth * 0.5f;

            float x = startX + col * COLUMN_SPACING;
            // Front row = closest to board (highest Z), subsequent rows go south
            float z = FRONT_ROW_Z - row * ROW_Z_SPACING;

            return new Vector3(x, 0.25f, z);
        }

        private HolderVisual CreateHolderVisual(HolderData data, Vector3 position)
        {
            if (!ObjectPoolManager.HasInstance)
            {
                Debug.LogWarning("[HolderVisualManager] ObjectPoolManager not available.");
                return null;
            }

            GameObject obj = ObjectPoolManager.Instance.Get(HOLDER_POOL_KEY, position, Quaternion.identity);
            if (obj == null)
            {
                Debug.LogWarning($"[HolderVisualManager] Failed to get holder from pool '{HOLDER_POOL_KEY}'.");
                return null;
            }

            obj.SetActive(true);

            // Set up identifier for tap detection
            HolderIdentifier identifier = obj.GetComponent<HolderIdentifier>();
            if (identifier != null)
            {
                identifier.SetHolderId(data.holderId);
            }

            // Set mesh color
            Renderer sr = obj.GetComponent<Renderer>();
            if (sr == null)
            {
                sr = obj.GetComponentInChildren<Renderer>();
            }

            Color holderColor = GetColor(data.color);
            if (sr != null)
            {
                sr.material.color = holderColor;
            }

            // Set up or find magazine count text
            TextMesh textMesh = FindOrCreateMagazineText(obj);
            if (textMesh != null)
            {
                textMesh.text = data.magazineCount.ToString();
                textMesh.color = Color.white;
                textMesh.fontSize = MAGAZINE_FONT_SIZE;
                textMesh.anchor = TextAnchor.MiddleCenter;
                textMesh.alignment = TextAlignment.Center;
            }

            HolderVisual visual = new HolderVisual
            {
                holderId = data.holderId,
                color = data.color,
                magazineRemaining = data.magazineCount,
                gameObject = obj,
                meshRenderer = sr,
                magazineText = textMesh,
                waitingPosition = position,
                isOnRail = false,
                normalizedT = 0f
            };

            return visual;
        }

        private TextMesh FindOrCreateMagazineText(GameObject holder)
        {
            // Look for existing TextMesh child named "MagazineText"
            TextMesh[] children = holder.GetComponentsInChildren<TextMesh>(true);
            if (children != null && children.Length > 0)
            {
                return children[0];
            }

            // Look for a child transform named "MagazineText"
            Transform textChild = holder.transform.Find("MagazineText");
            if (textChild != null)
            {
                TextMesh existing = textChild.GetComponent<TextMesh>();
                if (existing != null)
                {
                    return existing;
                }
            }

            // No TextMesh found on prefab; cannot create at runtime per project rules.
            // The prefab must include a child with TextMesh component.
            Debug.LogWarning("[HolderVisualManager] Holder prefab has no TextMesh child for magazine count. " +
                             "Add a child named 'MagazineText' with a TextMesh component to the Holder prefab.");
            return null;
        }

        private void RepositionWaitingHolders()
        {
            RepositionWaitingHoldersSmooth();
        }

        private void RepositionWaitingHoldersSmooth()
        {
            // Column-preserving south→north shift:
            // 1. Group waiting holders by their current column (nearest X column index)
            // 2. Within each column, sort by Z descending (northernmost first)
            // 3. Assign row positions per column from front row backward
            // Result: holders only move north (Z direction), never horizontally

            List<HolderVisual> waitingList = new List<HolderVisual>();
            foreach (var kvp in _holderVisuals)
            {
                HolderVisual visual = kvp.Value;
                if (visual.isOnRail || visual.isMovingToRail || visual.gameObject == null)
                {
                    continue;
                }
                waitingList.Add(visual);
            }

            if (waitingList.Count == 0) return;

            // Calculate column X positions (same as CalculateWaitingPosition)
            float rowWidth = (HOLDERS_PER_ROW - 1) * COLUMN_SPACING;
            float startX = -rowWidth * 0.5f;

            // Group holders by nearest column index
            Dictionary<int, List<HolderVisual>> columns = new Dictionary<int, List<HolderVisual>>();
            for (int i = 0; i < waitingList.Count; i++)
            {
                float holderX = waitingList[i].gameObject.transform.position.x;
                // Find nearest column index
                int nearestCol = 0;
                float minDist = float.MaxValue;
                for (int c = 0; c < HOLDERS_PER_ROW; c++)
                {
                    float colX = startX + c * COLUMN_SPACING;
                    float dist = Mathf.Abs(holderX - colX);
                    if (dist < minDist)
                    {
                        minDist = dist;
                        nearestCol = c;
                    }
                }

                if (!columns.ContainsKey(nearestCol))
                {
                    columns[nearestCol] = new List<HolderVisual>();
                }
                columns[nearestCol].Add(waitingList[i]);
            }

            // Within each column, sort by Z descending (northernmost first) and assign row positions
            foreach (var kvp in columns)
            {
                int col = kvp.Key;
                List<HolderVisual> colHolders = kvp.Value;

                // Sort by Z descending — front holders stay front
                colHolders.Sort((a, b) => b.gameObject.transform.position.z.CompareTo(a.gameObject.transform.position.z));

                float colX = startX + col * COLUMN_SPACING;
                for (int row = 0; row < colHolders.Count; row++)
                {
                    float z = FRONT_ROW_Z - row * ROW_Z_SPACING;
                    Vector3 targetPos = new Vector3(colX, 0.25f, z);

                    if (Vector3.Distance(colHolders[row].gameObject.transform.position, targetPos) > 0.05f)
                    {
                        float dist = Vector3.Distance(colHolders[row].gameObject.transform.position, targetPos);
                        colHolders[row].gameObject.transform.DOMove(targetPos, dist / 3f).SetEase(Ease.OutQuad);
                    }
                    colHolders[row].waitingPosition = targetPos;
                }
            }
        }

        // SmoothMoveCoroutine replaced by DOTween DOMove (see RepositionWaitingHoldersSmooth)

        #endregion

        #region Private Methods -- Rail Movement

        private void MoveHolderToRail(int holderId, int color, int magazineCount)
        {
            if (!_holderVisuals.TryGetValue(holderId, out HolderVisual visual))
            {
                Debug.LogWarning($"[HolderVisualManager] No visual found for holder {holderId}.");
                _isDeployingHolder = false;
                TryProcessDeploymentQueue();
                return;
            }

            if (visual.isOnRail || visual.isMovingToRail)
            {
                _isDeployingHolder = false;
                TryProcessDeploymentQueue();
                return;
            }

            // Rail capacity check — block deployment if full
            if (IsRailFull())
            {
                // Undo the deploy state in HolderManager
                if (HolderManager.HasInstance)
                {
                    HolderManager.Instance.UndoDeploy(holderId);
                }
                _isDeployingHolder = false;
                TryProcessDeploymentQueue();
                return;
            }

            _isDeployingHolder = true;
            visual.isMovingToRail = true;
            visual.magazineRemaining = magazineCount;
            visual.color = color;

            // Reposition remaining waiting holders to fill the gap
            RepositionWaitingHoldersSmooth();

            StartCoroutine(MoveToRailEntryCoroutine(visual));
        }

        private IEnumerator MoveToRailEntryCoroutine(HolderVisual visual)
        {
            if (!RailManager.HasInstance) yield break;

            Vector3 targetPos = RailManager.Instance.GetPositionAtNormalized(0f);
            const float moveSpeed = 10f;

            // Move toward rail entry with per-frame avoidance to prevent overlapping
            while (visual.gameObject != null)
            {
                Vector3 currentPos = visual.gameObject.transform.position;
                float dist = Vector3.Distance(currentPos, targetPos);
                if (dist < 0.1f) break;

                Vector3 dir = (targetPos - currentPos).normalized;
                Vector3 avoidOffset = CalculateAvoidanceOffset(visual, currentPos, dir);

                visual.gameObject.transform.position = currentPos + (dir + avoidOffset).normalized * moveSpeed * Time.deltaTime;
                yield return null;
            }

            // Arrived at rail entry — snap to entry and start rail traversal
            visual.isMovingToRail = false;
            visual.isOnRail = true;
            visual.normalizedT = 0f;

            if (!IsRailEntryFree())
            {
                // Wait for entry to clear
                float waited = 0f;
                while (waited < 2f && !IsRailEntryFree())
                {
                    waited += Time.deltaTime;
                    yield return null;
                }
            }

            _onRailHolders.Add(visual);
            _occupiedNormalizedPositions.Add(0f);
            StartCoroutine(RailTraversalCoroutine(visual));

            // Release deployment lock — allow next queued holder to start moving
            _isDeployingHolder = false;
            TryProcessDeploymentQueue();
        }

        private Vector3 CalculateAvoidanceOffset(HolderVisual self, Vector3 currentPos, Vector3 moveDir)
        {
            Vector3 offset = Vector3.zero;
            const float avoidRadius = 1.2f;

            foreach (var kvp in _holderVisuals)
            {
                HolderVisual other = kvp.Value;
                if (other == self || other.gameObject == null || other.isOnRail) continue;

                Vector3 otherPos = other.gameObject.transform.position;
                Vector3 diff = currentPos - otherPos;
                float dist = diff.magnitude;

                if (dist < avoidRadius && dist > 0.01f)
                {
                    // Push away from the obstacle
                    offset += diff.normalized * (avoidRadius - dist) * 0.5f;
                }
            }

            // Keep Y height constant
            offset.y = 0f;
            return offset;
        }

        private bool IsRailEntryFree()
        {
            for (int i = 0; i < _occupiedNormalizedPositions.Count; i++)
            {
                if (_occupiedNormalizedPositions[i] < MIN_NORMALIZED_SPACING)
                {
                    return false;
                }
            }
            return true;
        }

        private IEnumerator RailTraversalCoroutine(HolderVisual visual)
        {
            if (!RailManager.HasInstance)
            {
                Debug.LogError("[HolderVisualManager] RailManager not available for rail traversal.");
                yield break;
            }

            RailManager rail = RailManager.Instance;
            float totalLength = rail.TotalPathLength;

            if (totalLength <= 0f)
            {
                Debug.LogError("[HolderVisualManager] Rail total path length is zero.");
                yield break;
            }

            // Multi-lap traversal: loop around the rail until magazine is empty.
            // Fire only ONE dart per rail side per lap.
            // Rail sides are detected via movement direction (South/East/North/West).

            float distanceTraveled = 0f;
            float lastFireTime = -999f;
            float fireCooldown = _dartFlightTime + 0.02f; // Very tight cooldown
            const int MAX_LAPS = 50;
            int lapCount = 0;

            // Track fired balloon IDs per side per lap to avoid re-targeting
            // When side changes, the previous side's targets are "spent" for this lap
            int previousSide = -1;
            HashSet<int> firedThisSide = new HashSet<int>();

            while (visual.isOnRail && visual.magazineRemaining > 0 && lapCount < MAX_LAPS)
            {
                distanceTraveled = 0f;
                previousSide = -1;
                lapCount++;

                while (distanceTraveled < totalLength && visual.isOnRail && visual.magazineRemaining > 0)
                {
                    float deltaDistance = _railSpeed * Time.deltaTime;
                    distanceTraveled += deltaDistance;

                    float normalizedT = Mathf.Clamp01(distanceTraveled / totalLength);
                    visual.normalizedT = normalizedT;

                    UpdateOccupiedPosition(visual);

                    Vector3 position = rail.GetPositionAtNormalized(normalizedT);
                    Vector3 direction = rail.GetDirectionAtNormalized(normalizedT);

                    if (visual.gameObject != null)
                    {
                        visual.gameObject.transform.position = position;
                    }

                    if (visual.magazineText != null)
                    {
                        visual.magazineText.text = visual.magazineRemaining.ToString();
                    }

                    int currentSide = GetRailSide(direction);

                    // Reset fired set when entering a new side
                    if (currentSide != previousSide)
                    {
                        firedThisSide.Clear();
                        previousSide = currentSide;
                    }

                    // Fire at all outermost matching balloons on this side, one at a time (sequential cooldown).
                    // Each outermost balloon gets fired at once per side pass.
                    // E.g., south side with (1,1),(2,1),(3,1) all outermost red → fire all 3 sequentially.
                    // But (1,2) behind (1,1) won't fire until next lap when (1,1) is popped.
                    if (visual.magazineRemaining > 0 && Time.time - lastFireTime >= fireCooldown)
                    {
                        bool fired = TryFireDart(visual, position, direction, firedThisSide);
                        if (fired)
                        {
                            lastFireTime = Time.time;
                        }
                    }

                    yield return null;
                }
            }

            // Wait for all in-flight darts to land before completing
            // (prevents fail trigger while last dart is still in flight)
            yield return StartCoroutine(WaitForDartsToLand());

            // Rail traversal complete (magazine empty or max laps)
            CompleteRailLoop(visual);
        }

        /// <summary>
        /// Determines which side of the rectangular rail the holder is on based on movement direction.
        /// Returns: 0=South (moving right, +X), 1=East (moving forward, +Z),
        ///          2=North (moving left, -X), 3=West (moving back, -Z).
        /// </summary>
        private int GetRailSide(Vector3 direction)
        {
            float absX = Mathf.Abs(direction.x);
            float absZ = Mathf.Abs(direction.z);

            if (absX >= absZ)
            {
                // Horizontal movement
                return direction.x >= 0f ? 0 : 2; // 0=South (left→right), 2=North (right→left)
            }
            else
            {
                // Depth movement
                return direction.z >= 0f ? 1 : 3; // 1=East (bottom→top), 3=West (top→bottom)
            }
        }

        private IEnumerator WaitForDartsToLand()
        {
            float timeout = 3f;
            float waited = 0f;
            while (_activeDartProjectiles.Count > 0 && waited < timeout)
            {
                waited += Time.deltaTime;
                yield return null;
            }
        }

        private void UpdateOccupiedPosition(HolderVisual visual)
        {
            int index = _onRailHolders.IndexOf(visual);
            if (index >= 0 && index < _occupiedNormalizedPositions.Count)
            {
                _occupiedNormalizedPositions[index] = visual.normalizedT;
            }
        }

        private void CompleteRailLoop(HolderVisual visual)
        {
            // Remove from on-rail tracking
            int index = _onRailHolders.IndexOf(visual);
            if (index >= 0)
            {
                _onRailHolders.RemoveAt(index);
                if (index < _occupiedNormalizedPositions.Count)
                {
                    _occupiedNormalizedPositions.RemoveAt(index);
                }
            }

            visual.isOnRail = false;

            // Publish rail loop complete -- HolderManager will handle return/remove logic
            EventBus.Publish(new OnRailLoopComplete
            {
                holderId = visual.holderId,
                remainingMagazine = visual.magazineRemaining
            });

            // If magazine empty, remove holder visual immediately (holder is consumed)
            if (visual.magazineRemaining <= 0)
            {
                ReturnHolderToPool(visual);
                _holderVisuals.Remove(visual.holderId);
                RepositionWaitingHolders();
            }
            // If magazine remains, HandleHolderReturned will reposition
        }

        #endregion

        #region Private Methods -- Dart Firing

        private bool TryFireDart(HolderVisual visual, Vector3 position, Vector3 direction, HashSet<int> firedThisSide = null)
        {
            // Determine firing direction: holder fires INWARD toward the board center.
            // The rail side determines the cardinal direction (south rail → fire north, etc.).
            // Use GetRailSide to get current side, then convert to inward-facing direction.
            int side = GetRailSide(direction);
            Vector3 fireDirection;
            switch (side)
            {
                case 0: fireDirection = Vector3.forward;  break; // South rail → fire north (+Z)
                case 1: fireDirection = Vector3.left;     break; // East rail  → fire west  (-X)
                case 2: fireDirection = Vector3.back;     break; // North rail → fire south (-Z)
                case 3: fireDirection = Vector3.right;    break; // West rail  → fire east  (+X)
                default: fireDirection = Vector3.forward; break;
            }
            int targetId = DirectionalTargeting.FindTarget(position, fireDirection, visual.color, firedThisSide);
            if (targetId < 0)
            {
                return false;
            }

            // Get target balloon position
            if (!BalloonController.HasInstance)
            {
                return false;
            }

            BalloonData targetData = BalloonController.Instance.GetBalloon(targetId);
            if (targetData == null || targetData.isPopped)
            {
                return false;
            }

            // Track this balloon so we don't re-target it on the same side pass
            if (firedThisSide != null)
            {
                firedThisSide.Add(targetId);
            }

            // Consume magazine
            visual.magazineRemaining--;

            if (visual.magazineText != null)
            {
                visual.magazineText.text = visual.magazineRemaining.ToString();
            }

            // Also notify HolderManager
            if (HolderManager.HasInstance)
            {
                HolderManager.Instance.ConsumeMagazine(visual.holderId);
            }

            // Scale punch animation on fire (DOTween)
            if (visual.gameObject != null)
            {
                visual.gameObject.transform.DOPunchScale(Vector3.one * 0.1f, 0.1f, 8, 0.5f);
            }

            // Spawn dart projectile from pool
            SpawnDartProjectile(position, targetData.position, targetId, visual.color);

            // Publish dart fired event
            EventBus.Publish(new OnDartFired
            {
                dartId = -1, // Visual-only dart
                holderId = visual.holderId,
                color = visual.color
            });

            return true;
        }

        private void SpawnDartProjectile(Vector3 from, Vector3 to, int targetBalloonId, int color)
        {
            if (!ObjectPoolManager.HasInstance)
            {
                // No pool available; execute instant hit
                ExecuteDartHit(targetBalloonId);
                return;
            }

            GameObject dartObj = ObjectPoolManager.Instance.Get(DART_POOL_KEY, from, Quaternion.identity);
            if (dartObj == null)
            {
                // Pool exhausted; execute instant hit
                ExecuteDartHit(targetBalloonId);
                return;
            }

            dartObj.SetActive(true);

            // Tint dart to match holder color
            Renderer sr = dartObj.GetComponent<Renderer>();
            if (sr == null)
            {
                sr = dartObj.GetComponentInChildren<Renderer>();
            }
            if (sr != null)
            {
                sr.material.color = GetColor(color);
            }

            // Orient dart horizontally toward target (flat on XZ plane, no pitch)
            Vector3 dir = to - from;
            dir.y = 0f; // Keep dart level/horizontal
            if (dir.sqrMagnitude > 0.001f)
            {
                dartObj.transform.rotation = Quaternion.LookRotation(dir.normalized);
            }

            DartProjectile projectile = new DartProjectile
            {
                gameObject = dartObj,
                startPosition = from,
                targetPosition = to,
                targetBalloonId = targetBalloonId,
                elapsed = 0f,
                duration = _dartFlightTime
            };

            _activeDartProjectiles.Add(projectile);

            // DOTween fast straight-line flight
            dartObj.transform.DOMove(to, _dartFlightTime).SetEase(Ease.Linear);
        }

        private void UpdateDartProjectiles()
        {
            for (int i = _activeDartProjectiles.Count - 1; i >= 0; i--)
            {
                DartProjectile proj = _activeDartProjectiles[i];
                proj.elapsed += Time.deltaTime;

                float t = Mathf.Clamp01(proj.elapsed / proj.duration);

                // DOTween handles position movement; we only track elapsed for hit timing

                if (t >= 1f)
                {
                    // Dart reached target
                    ExecuteDartHit(proj.targetBalloonId);
                    ReturnDartProjectileToPool(proj);
                    _activeDartProjectiles.RemoveAt(i);
                }
            }
        }

        private void ExecuteDartHit(int balloonId)
        {
            if (!BalloonController.HasInstance)
            {
                return;
            }

            BalloonController.Instance.PopBalloon(balloonId);
        }

        private void ReturnDartProjectileToPool(DartProjectile proj)
        {
            if (proj.gameObject != null && ObjectPoolManager.HasInstance)
            {
                ObjectPoolManager.Instance.Return(DART_POOL_KEY, proj.gameObject);
            }
            proj.gameObject = null;
        }

        // ScalePunchCoroutine replaced by DOTween DOPunchScale (see TryFireDart)

        #endregion

        #region Private Methods -- Pool Cleanup

        private void ReturnHolderToPool(HolderVisual visual)
        {
            if (visual.gameObject != null && ObjectPoolManager.HasInstance)
            {
                ObjectPoolManager.Instance.Return(HOLDER_POOL_KEY, visual.gameObject);
            }
            visual.gameObject = null;
        }

        #endregion

        #region Private Methods -- Event Handlers

        private void HandleLevelLoaded(OnLevelLoaded evt)
        {
            // Delay one frame to let HolderManager initialize first
            StartCoroutine(SpawnAfterDelay());
        }

        private IEnumerator SpawnAfterDelay()
        {
            yield return null;
            SpawnWaitingHolders();
        }

        private void HandleHolderSelected(OnHolderSelected evt)
        {
            _deploymentQueue.Enqueue(evt);
            TryProcessDeploymentQueue();
        }

        /// <summary>
        /// Processes the deployment queue one holder at a time.
        /// Prevents overlapping when clicking multiple holders quickly.
        /// </summary>
        private void TryProcessDeploymentQueue()
        {
            if (_isDeployingHolder || _deploymentQueue.Count == 0) return;

            var evt = _deploymentQueue.Dequeue();

            // Verify the holder is still valid (not already on rail)
            if (_holderVisuals.TryGetValue(evt.holderId, out HolderVisual visual))
            {
                if (visual.isOnRail || visual.isMovingToRail)
                {
                    // Skip this one, try next
                    TryProcessDeploymentQueue();
                    return;
                }
            }

            MoveHolderToRail(evt.holderId, evt.color, evt.magazineCount);
        }

        private void HandleHolderReturned(OnHolderReturned evt)
        {
            // Holder returned with remaining magazine -- reposition in waiting area
            if (_holderVisuals.TryGetValue(evt.holderId, out HolderVisual visual))
            {
                visual.isOnRail = false;
                visual.isMovingToRail = false;
                visual.magazineRemaining = evt.remainingMagazine;

                if (visual.magazineText != null)
                {
                    visual.magazineText.text = evt.remainingMagazine.ToString();
                }
            }

            RepositionWaitingHolders();
        }

        private void HandleMagazineEmpty(OnMagazineEmpty evt)
        {
            // When holder is removed by HolderManager, clean up the visual
            if (_holderVisuals.TryGetValue(evt.holderId, out HolderVisual visual))
            {
                // Don't remove immediately if still on rail -- CompleteRailLoop handles that.
                // This event fires when magazine hits zero; the rail loop coroutine will
                // end naturally and call CompleteRailLoop.
                if (!visual.isOnRail && !visual.isMovingToRail)
                {
                    ReturnHolderToPool(visual);
                    _holderVisuals.Remove(evt.holderId);
                    RepositionWaitingHolders();
                }
            }
        }

        #endregion
    }
}
