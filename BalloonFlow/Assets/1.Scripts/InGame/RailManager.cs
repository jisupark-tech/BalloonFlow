using System.Collections.Generic;
using UnityEngine;

namespace BalloonFlow
{
    /// <summary>
    /// Manages the circular rail as a conveyor belt with discrete slots.
    /// Rail Overflow mode: darts occupy slots, belt rotates counter-clockwise at constant speed.
    /// Provides slot positions, firing directions, and occupancy tracking.
    /// </summary>
    /// <remarks>
    /// Layer: Core | Genre: Puzzle | Role: Manager | Phase: 0
    /// DB Reference: No DB match found — generated from Rail Overflow spec
    /// </remarks>
    public class RailManager : SceneSingleton<RailManager>
    {
        #region Nested Types

        /// <summary>
        /// Data for a single slot on the rail conveyor belt.
        /// </summary>
        public struct SlotData
        {
            /// <summary>Color of the dart occupying this slot (-1 = empty).</summary>
            public int dartColor;

            /// <summary>ID of the holder that placed this dart (-1 = empty).</summary>
            public int holderId;

            /// <summary>Unique dart ID for event tracking.</summary>
            public int dartId;
        }

        /// <summary>
        /// Cardinal direction a dart fires from its rail side.
        /// </summary>
        public enum RailSide
        {
            Bottom, // fires Up (+Z)
            Right,  // fires Left (-X)
            Top,    // fires Down (-Z)
            Left    // fires Right (+X)
        }

        #endregion

        #region Serialized Fields

        [SerializeField] private Transform[] _waypointTransforms;
        [SerializeField] private bool _isClosedLoop = true;

        #endregion

        #region Constants

        private const int CORNER_SUBDIVISIONS = 8; // arc segments per corner
        private const float MIN_CORNER_RADIUS = 0.2f;
        private const float MAX_CORNER_RADIUS = 5f;

        // 가변 수용량 구간 — 정본: 레일초과_코어메카닉_명세 (2026-03-17)
        // 총 다트 수 기준으로 레일 수용량 자동 결정
        private static readonly int[] CAPACITY_TIERS = { 50, 100, 150, 200 };
        private static readonly int[] CAPACITY_DART_THRESHOLDS = { 30, 60, 100, int.MaxValue };
        // darts <= 30 → 50, darts <= 60 → 100, darts <= 100 → 150, else → 200

        #endregion

        #region Fields

        private readonly List<Vector3> _waypoints = new List<Vector3>();
        private readonly List<Vector3> _smoothedPath = new List<Vector3>(); // smoothed version (or copy of waypoints)
        private readonly List<float> _segmentLengths = new List<float>();
        private readonly List<float> _cumulativeLengths = new List<float>();
        private float _totalPathLength;

        // Smooth corners
        private bool _smoothCorners;
        private float _cornerRadius = 1f;

        // Slot system
        private int _slotCount = 200;
        private SlotData[] _slots;
        private float _rotationOffset; // current conveyor belt offset in distance units
        private float _rotationSpeed;  // slots per second
        private float _slotSpacing;    // distance between slots on the path
        private int _occupiedCount;
        private int _nextDartId;
        private bool _boardFinished;

        // Off-belt frozen dart system: darts removed from slots and held at fixed world positions
        public struct FrozenDartInfo
        {
            public int dartId;
            public int color;
            public int holderId;
            public Vector3 worldPosition;
            public int originalSlotIndex; // freeze 시점의 슬롯 인덱스 (체인 전파용)
        }
        private readonly List<FrozenDartInfo> _frozenDartInfos = new List<FrozenDartInfo>();

        #endregion

        #region Properties

        public float TotalPathLength => _totalPathLength;
        public int WaypointCount => _waypoints.Count;
        public bool IsClosedLoop => _isClosedLoop;
        public int SlotCount => _slotCount;
        public int OccupiedCount => _occupiedCount;

        /// <summary>Occupancy ratio 0.0 ~ 1.0.</summary>
        public float Occupancy => _slotCount > 0 ? (float)_occupiedCount / _slotCount : 0f;

        /// <summary>Conveyor belt rotation speed in slots per second.</summary>
        public float RotationSpeed => _rotationSpeed;

        /// <summary>Distance between adjacent slots on the path.</summary>
        public float SlotSpacing => _slotSpacing;

        /// <summary>Current belt rotation offset in distance units.</summary>
        public float RotationOffset => _rotationOffset;

        /// <summary>Whether smooth corner interpolation is active.</summary>
        public bool SmoothCorners => _smoothCorners;

        /// <summary>Corner rounding radius in world units.</summary>
        public float CornerRadius => _cornerRadius;

        #endregion

        #region Lifecycle

        protected override void OnSingletonAwake()
        {
            BuildPathFromTransforms();

            if (GameManager.HasInstance)
            {
                _rotationSpeed = GameManager.Instance.Board.railRotationSpeed;
            }
            else
            {
                _rotationSpeed = 30f;
            }
        }

        private void OnEnable()
        {
            EventBus.Subscribe<OnBoardCleared>(HandleBoardCleared);
            EventBus.Subscribe<OnBoardFailed>(HandleBoardFailed);
            EventBus.Subscribe<OnContinueApplied>(HandleContinueApplied);
        }

        private void OnDisable()
        {
            EventBus.Unsubscribe<OnBoardCleared>(HandleBoardCleared);
            EventBus.Unsubscribe<OnBoardFailed>(HandleBoardFailed);
            EventBus.Unsubscribe<OnContinueApplied>(HandleContinueApplied);
        }

        private void Update()
        {
            if (_slots == null || _slotCount == 0) return;
            if (_boardFinished) return;

            // Advance conveyor belt (counter-clockwise = positive direction along path)
            _rotationOffset += _rotationSpeed * _slotSpacing * Time.deltaTime;

            // Wrap around
            if (_totalPathLength > 0f)
            {
                _rotationOffset %= _totalPathLength;
            }

            // Chain freeze: moving darts that reach frozen darts also freeze
            PropagateFreezeChain();
        }

        #endregion

        #region Public Methods — Path

        /// <summary>
        /// Returns the rail path as an array of world positions (smoothed if enabled).
        /// </summary>
        public Vector3[] GetRailPath()
        {
            if (_smoothedPath.Count == 0)
            {
                return System.Array.Empty<Vector3>();
            }

            return _smoothedPath.ToArray();
        }

        /// <summary>
        /// Returns the original (non-smoothed) waypoints.
        /// </summary>
        public Vector3[] GetRawWaypoints()
        {
            return _waypoints.ToArray();
        }

        /// <summary>
        /// Sets the rail layout from level data. Call when loading a new level.
        /// </summary>
        public void SetRailLayout(Vector3[] positions, int slotCount, bool closedLoop = true)
        {
            SetRailLayout(positions, slotCount, closedLoop, false, 1f);
        }

        /// <summary>
        /// Sets the rail layout with optional smooth corners.
        /// </summary>
        public void SetRailLayout(Vector3[] positions, int slotCount, bool closedLoop, bool smoothCorners, float cornerRadius)
        {
            _waypoints.Clear();
            _isClosedLoop = closedLoop;
            _smoothCorners = smoothCorners;
            _cornerRadius = Mathf.Clamp(cornerRadius, MIN_CORNER_RADIUS, MAX_CORNER_RADIUS);

            if (positions != null)
            {
                for (int i = 0; i < positions.Length; i++)
                {
                    _waypoints.Add(positions[i]);
                }
            }

            BuildSmoothedPath();
            RecalculatePathLengths();
            InitializeSlots(slotCount);
        }

        /// <summary>
        /// Gets the position on the rail at a specific distance from the start.
        /// Uses smoothed path when smooth corners are enabled.
        /// </summary>
        public Vector3 GetPositionAtDistance(float distance)
        {
            var path = _smoothedPath;
            if (path.Count == 0) return Vector3.zero;
            if (path.Count == 1) return path[0];
            if (_totalPathLength <= 0f) return path[0];

            if (_isClosedLoop && _totalPathLength > 0f)
            {
                distance = ((distance % _totalPathLength) + _totalPathLength) % _totalPathLength;
            }
            else
            {
                distance = Mathf.Clamp(distance, 0f, _totalPathLength);
            }

            for (int i = 0; i < _segmentLengths.Count; i++)
            {
                if (distance <= _cumulativeLengths[i])
                {
                    float segStart = (i > 0) ? _cumulativeLengths[i - 1] : 0f;
                    float segLength = _segmentLengths[i];

                    if (segLength <= 0f) return path[i];

                    float localT = (distance - segStart) / segLength;
                    int nextIndex = (i + 1) % path.Count;
                    return Vector3.Lerp(path[i], path[nextIndex], localT);
                }
            }

            return path[path.Count - 1];
        }

        /// <summary>
        /// Gets the position on the rail at a normalized distance (0..1).
        /// </summary>
        public Vector3 GetPositionAtNormalized(float t)
        {
            if (_smoothedPath.Count == 0) return Vector3.zero;
            if (_smoothedPath.Count == 1) return _smoothedPath[0];

            t = Mathf.Clamp01(t);
            return GetPositionAtDistance(t * _totalPathLength);
        }

        /// <summary>
        /// Gets the forward direction on the rail at a normalized distance.
        /// </summary>
        public Vector3 GetDirectionAtNormalized(float t)
        {
            if (_smoothedPath.Count < 2) return Vector3.forward;

            const float epsilon = 0.001f;
            float tA = Mathf.Clamp01(t - epsilon);
            float tB = Mathf.Clamp01(t + epsilon);

            Vector3 posA = GetPositionAtNormalized(tA);
            Vector3 posB = GetPositionAtNormalized(tB);
            Vector3 dir = (posB - posA).normalized;

            return dir.sqrMagnitude > 0.001f ? dir : Vector3.forward;
        }

        #endregion

        #region Public Methods — Slot System

        /// <summary>
        /// Initializes slot array for a new level.
        /// </summary>
        public void InitializeSlots(int slotCount)
        {
            _slotCount = Mathf.Max(1, slotCount);
            _slots = new SlotData[_slotCount];
            _rotationOffset = 0f;
            _occupiedCount = 0;
            _nextDartId = 0;
            _boardFinished = false;

            for (int i = 0; i < _slotCount; i++)
            {
                _slots[i].dartColor = -1;
                _slots[i].holderId = -1;
                _slots[i].dartId = -1;
            }

            _slotSpacing = _totalPathLength > 0f ? _totalPathLength / _slotCount : 1f;
        }

        /// <summary>
        /// Returns the world position of a slot at the current conveyor belt offset.
        /// </summary>
        public Vector3 GetSlotWorldPosition(int slotIndex)
        {
            if (_slots == null || slotIndex < 0 || slotIndex >= _slotCount)
                return Vector3.zero;

            float distance = slotIndex * _slotSpacing + _rotationOffset;
            return GetPositionAtDistance(distance);
        }

        /// <summary>
        /// Returns the rail side and inward firing direction for a given slot.
        /// </summary>
        public RailSide GetSlotRailSide(int slotIndex)
        {
            Vector3 pos = GetSlotWorldPosition(slotIndex);
            Vector3 dir = GetSlotDirection(slotIndex);

            // Determine which side of the rectangular rail based on movement direction
            float absX = Mathf.Abs(dir.x);
            float absZ = Mathf.Abs(dir.z);

            if (absX >= absZ)
            {
                // Moving +X = along bottom edge, Moving -X = along top edge
                return dir.x >= 0f ? RailSide.Bottom : RailSide.Top;
            }
            else
            {
                // Moving +Z (upward) = right wall → fire left (inward)
                // Moving -Z (downward) = left wall → fire right (inward)
                return dir.z >= 0f ? RailSide.Right : RailSide.Left;
            }
        }

        /// <summary>
        /// Returns the inward firing direction for a slot (toward the balloon field center).
        /// </summary>
        public Vector3 GetSlotFiringDirection(int slotIndex)
        {
            RailSide side = GetSlotRailSide(slotIndex);
            switch (side)
            {
                case RailSide.Bottom: return Vector3.forward;  // fire north (+Z)
                case RailSide.Right:  return Vector3.left;     // fire west (-X)
                case RailSide.Top:    return Vector3.back;     // fire south (-Z)
                case RailSide.Left:   return Vector3.right;    // fire east (+X)
                default:              return Vector3.forward;
            }
        }

        /// <summary>
        /// Returns the movement direction of a slot along the belt.
        /// </summary>
        public Vector3 GetSlotDirection(int slotIndex)
        {
            float distance = slotIndex * _slotSpacing + _rotationOffset;
            float normalizedT = _totalPathLength > 0f ? distance / _totalPathLength : 0f;

            // Wrap
            normalizedT = ((normalizedT % 1f) + 1f) % 1f;
            return GetDirectionAtNormalized(normalizedT);
        }

        /// <summary>
        /// Gets the slot data at the given index.
        /// </summary>
        public SlotData GetSlot(int slotIndex)
        {
            if (_slots == null || slotIndex < 0 || slotIndex >= _slotCount)
                return new SlotData { dartColor = -1, holderId = -1, dartId = -1 };
            return _slots[slotIndex];
        }

        /// <summary>
        /// Returns true if the slot is empty (no dart).
        /// </summary>
        public bool IsSlotEmpty(int slotIndex)
        {
            if (_slots == null || slotIndex < 0 || slotIndex >= _slotCount) return false;
            return _slots[slotIndex].dartColor < 0;
        }

        /// <summary>
        /// Places a dart on an empty slot. Returns the assigned dart ID, or -1 if slot is occupied.
        /// </summary>
        public int PlaceDart(int slotIndex, int color, int holderId)
        {
            if (_boardFinished) return -1; // Reject after board clear/fail
            if (_slots == null || slotIndex < 0 || slotIndex >= _slotCount) return -1;
            if (_slots[slotIndex].dartColor >= 0) return -1; // occupied

            int dartId = _nextDartId++;
            _slots[slotIndex].dartColor = color;
            _slots[slotIndex].holderId = holderId;
            _slots[slotIndex].dartId = dartId;
            _occupiedCount++;

            PublishOccupancyChanged();
            return dartId;
        }

        /// <summary>
        /// Removes the dart from a slot (dart was fired or cleared). Returns true if removed.
        /// </summary>
        public bool ClearSlot(int slotIndex)
        {
            if (_slots == null || slotIndex < 0 || slotIndex >= _slotCount) return false;
            if (_slots[slotIndex].dartColor < 0) return false; // already empty

            _slots[slotIndex].dartColor = -1;
            _slots[slotIndex].holderId = -1;
            _slots[slotIndex].dartId = -1;
            _occupiedCount = Mathf.Max(0, _occupiedCount - 1);

            PublishOccupancyChanged();
            return true;
        }

        /// <summary>
        /// Finds the next empty slot starting from startIndex, scanning forward (belt direction).
        /// Returns -1 if no empty slot found (full rail).
        /// </summary>
        public int FindNextEmptySlot(int startIndex)
        {
            if (_slots == null || _occupiedCount >= _slotCount) return -1;

            for (int i = 0; i < _slotCount; i++)
            {
                int idx = (startIndex + i) % _slotCount;
                if (_slots[idx].dartColor < 0)
                {
                    return idx;
                }
            }
            return -1;
        }

        /// <summary>
        /// Returns the slot index closest to a world position on the rail.
        /// Used to determine where a deploying holder should start placing darts.
        /// </summary>
        public int GetNearestSlotIndex(Vector3 worldPosition)
        {
            if (_slots == null || _slotCount == 0) return 0;

            float minDist = float.MaxValue;
            int nearestSlot = 0;

            for (int i = 0; i < _slotCount; i++)
            {
                Vector3 slotPos = GetSlotWorldPosition(i);
                float dist = Vector3.Distance(worldPosition, slotPos);
                if (dist < minDist)
                {
                    minDist = dist;
                    nearestSlot = i;
                }
            }

            return nearestSlot;
        }

        /// <summary>
        /// Returns all occupied slot indices. 재사용 리스트로 GC 방지.
        /// </summary>
        private readonly List<int> _reusableOccupiedSlots = new List<int>(200);

        public List<int> GetOccupiedSlots()
        {
            _reusableOccupiedSlots.Clear();
            if (_slots == null) return _reusableOccupiedSlots;

            for (int i = 0; i < _slotCount; i++)
            {
                if (_slots[i].dartColor >= 0)
                    _reusableOccupiedSlots.Add(i);
            }
            return _reusableOccupiedSlots;
        }

        #endregion

        #region Public Methods — Cluster / Gap Detection

        /// <summary>
        /// 지정 슬롯부터 벨트 진행 방향(+)으로 연속된 빈 슬롯 수를 반환.
        /// 0 = 해당 슬롯이 occupied (틈 아님).
        /// 군집 사이의 틈 크기를 측정할 때 사용.
        /// </summary>
        public int GetGapLengthForward(int slotIndex)
        {
            if (_slots == null || slotIndex < 0 || slotIndex >= _slotCount) return 0;
            if (_slots[slotIndex].dartColor >= 0) return 0;

            int length = 0;
            for (int i = 0; i < _slotCount; i++)
            {
                int idx = (slotIndex + i) % _slotCount;
                if (_slots[idx].dartColor >= 0) break;
                length++;
            }
            return length;
        }

        /// <summary>
        /// 지정 슬롯부터 벨트 역방향(-)으로 연속된 빈 슬롯 수를 반환.
        /// deploy point 뒤쪽 틈 크기 측정에 사용.
        /// </summary>
        public int GetGapLengthBackward(int slotIndex)
        {
            if (_slots == null || slotIndex < 0 || slotIndex >= _slotCount) return 0;
            if (_slots[slotIndex].dartColor >= 0) return 0;

            int length = 0;
            for (int i = 0; i < _slotCount; i++)
            {
                int idx = (slotIndex - i + _slotCount) % _slotCount;
                if (_slots[idx].dartColor >= 0) break;
                length++;
            }
            return length;
        }

        /// <summary>
        /// deploy point 기준, 뒤쪽(벨트 역방향)에서 가장 가까운 군집까지의 틈 크기.
        /// 군집이 바로 인접하면 0, 빈칸 3개 있으면 3.
        /// 틈이 있어야 배치 가능한 로직에 사용.
        /// </summary>
        public int GetGapBehindDeployPoint(int deploySlot)
        {
            if (_slots == null) return 0;

            // deploy slot 자체가 비어있는지 확인
            // deploy slot부터 뒤로 스캔하여 첫 occupied 슬롯까지의 거리
            int gap = 0;
            for (int i = 0; i < _slotCount; i++)
            {
                int idx = (deploySlot - i + _slotCount) % _slotCount;
                if (_slots[idx].dartColor >= 0) break;
                gap++;
            }
            return gap;
        }

        /// <summary>
        /// 빈 슬롯에 다트를 배치하면 기존 군집이 분리되는지 체크.
        /// 즉시 이웃(slot ±1)만 확인 — 양쪽 모두 같은 색이면 군집 내부 구멍.
        /// true = 배치 가능, false = 군집 분리 위험 → 배치 금지.
        /// </summary>
        public bool CanPlaceWithoutSplittingCluster(int slotIndex)
        {
            if (_slots == null || slotIndex < 0 || slotIndex >= _slotCount) return false;
            if (_slots[slotIndex].dartColor >= 0) return false; // occupied

            int prev = (slotIndex - 1 + _slotCount) % _slotCount;
            int next = (slotIndex + 1) % _slotCount;

            int colorPrev = _slots[prev].dartColor; // -1 if empty
            int colorNext = _slots[next].dartColor; // -1 if empty

            // 양쪽 즉시 이웃이 같은 색 → 군집 내부 구멍 → 배치 금지
            if (colorPrev >= 0 && colorNext >= 0 && colorPrev == colorNext)
                return false;

            return true;
        }

        /// <summary>
        /// 벨트 위의 군집 정보를 반환. 각 군집 = (시작 슬롯, 길이, 색상).
        /// 같은 색이 연속되면 하나의 군집. 색이 바뀌면 새 군집.
        /// </summary>
        public List<(int startSlot, int length, int color)> GetClusters()
        {
            var clusters = new List<(int, int, int)>();
            if (_slots == null || _slotCount == 0) return clusters;

            int i = 0;
            // 첫 번째 occupied 슬롯 찾기
            while (i < _slotCount && _slots[i].dartColor < 0) i++;
            if (i >= _slotCount) return clusters;

            int start = i;
            int color = _slots[i].dartColor;
            int len = 1;
            i++;

            while (i < _slotCount)
            {
                if (_slots[i].dartColor == color && color >= 0)
                {
                    len++;
                }
                else
                {
                    if (color >= 0)
                        clusters.Add((start, len, color));
                    if (_slots[i].dartColor >= 0)
                    {
                        start = i;
                        color = _slots[i].dartColor;
                        len = 1;
                    }
                    else
                    {
                        color = -1;
                        len = 0;
                    }
                }
                i++;
            }

            if (color >= 0)
                clusters.Add((start, len, color));

            return clusters;
        }

        #endregion

        #region Public Methods — Dart Colors

        /// <summary>
        /// Returns the set of dart colors currently on the rail.
        /// </summary>
        public HashSet<int> GetRailDartColors()
        {
            var colors = new HashSet<int>();
            if (_slots == null) return colors;

            for (int i = 0; i < _slotCount; i++)
            {
                if (_slots[i].dartColor >= 0)
                {
                    colors.Add(_slots[i].dartColor);
                }
            }
            return colors;
        }

        /// <summary>
        /// Determines the appropriate rail capacity based on total dart count.
        /// Design: darts≤30→50, ≤60→100, ≤100→150, else→200.
        /// If explicitCapacity > 0, uses that instead (LevelConfig override).
        /// </summary>
        public static int CalculateCapacity(int totalDarts, int explicitCapacity = 0)
        {
            if (explicitCapacity > 0) return explicitCapacity;

            for (int i = 0; i < CAPACITY_TIERS.Length; i++)
            {
                if (totalDarts <= CAPACITY_DART_THRESHOLDS[i])
                    return CAPACITY_TIERS[i];
            }
            return CAPACITY_TIERS[CAPACITY_TIERS.Length - 1];
        }

        /// <summary>
        /// Initializes the rail for a level with auto-capacity calculation.
        /// Call this instead of InitializeSlots when loading a level.
        /// </summary>
        public void InitializeForLevel(int totalDarts, int explicitCapacity = 0)
        {
            int capacity = CalculateCapacity(totalDarts, explicitCapacity);
            InitializeSlots(capacity);
            Debug.Log($"[RailManager] InitializeForLevel: totalDarts={totalDarts}, capacity={capacity}" +
                      (explicitCapacity > 0 ? " (explicit)" : " (auto)"));
        }

        /// <summary>
        /// Removes a batch of darts from the rail, starting from the most recently placed.
        /// Design ref: 이어하기 — 가장 최근 배치된 다트부터 제거.
        /// Removes up to count darts, returns actual number removed.
        /// </summary>
        public int RemoveDarts(int count)
        {
            if (_slots == null || count <= 0) return 0;

            // Build a list of occupied slots sorted by dartId descending (most recent first)
            var occupied = new List<int>(_occupiedCount);
            for (int i = 0; i < _slotCount; i++)
            {
                if (_slots[i].dartColor >= 0) occupied.Add(i);
            }

            // Sort by dartId descending — highest dartId = most recently placed
            occupied.Sort((a, b) => _slots[b].dartId.CompareTo(_slots[a].dartId));

            int removed = 0;
            for (int i = 0; i < occupied.Count && removed < count; i++)
            {
                ClearSlot(occupied[i]);
                removed++;
            }
            return removed;
        }

        /// <summary>
        /// Removes a dart from the belt and stores it as frozen at its current world position.
        /// The dart is completely off the slot system until unfrozen.
        /// Design ref: "뒤에 오는걸 그자리에 멈추고, 배치가 끝나면 다시 움직이라고"
        /// </summary>
        /// <returns>True if frozen successfully.</returns>
        public bool FreezeDart(int slotIndex)
        {
            if (_slots == null || slotIndex < 0 || slotIndex >= _slotCount) return false;
            if (_slots[slotIndex].dartColor < 0) return false;

            // Don't freeze darts from the currently deploying holder
            if (_activeDeployHolderId >= 0 && _slots[slotIndex].holderId == _activeDeployHolderId)
                return false;

            // Check not already frozen
            int dartId = _slots[slotIndex].dartId;
            for (int i = 0; i < _frozenDartInfos.Count; i++)
            {
                if (_frozenDartInfos[i].dartId == dartId) return false;
            }

            Vector3 worldPos = GetSlotWorldPosition(slotIndex);

            _frozenDartInfos.Add(new FrozenDartInfo
            {
                dartId = dartId,
                color = _slots[slotIndex].dartColor,
                holderId = _slots[slotIndex].holderId,
                worldPosition = worldPos,
                originalSlotIndex = slotIndex
            });

            // Remove from belt
            ClearSlot(slotIndex);

            // Notify DartManager to pin visual at frozen position
            EventBus.Publish(new OnDartFrozen
            {
                dartId = dartId,
                slotIndex = slotIndex
            });

            return true;
        }

        /// <summary>
        /// Reinserts all frozen darts back into the nearest available belt slots
        /// and resumes normal movement. Called when holder deployment finishes.
        /// </summary>
        public void UnfreezeAndReinsertAll()
        {
            // First clear frozen visuals
            EventBus.Publish(new OnDartsFrozenCleared());

            // Then reinsert each dart and create new slot visuals
            for (int i = 0; i < _frozenDartInfos.Count; i++)
            {
                var info = _frozenDartInfos[i];
                int nearestSlot = GetNearestSlotIndex(info.worldPosition);
                int emptySlot = FindNextEmptySlot(nearestSlot);

                if (emptySlot >= 0)
                {
                    _slots[emptySlot].dartColor = info.color;
                    _slots[emptySlot].holderId = info.holderId;
                    _slots[emptySlot].dartId = info.dartId;
                    _occupiedCount++;

                    // Create visual for reinserted dart
                    EventBus.Publish(new OnDartPlacedOnSlot
                    {
                        slotIndex = emptySlot,
                        color = info.color,
                        holderId = info.holderId
                    });
                }
            }

            _frozenDartInfos.Clear();
            PublishOccupancyChanged();
        }

        /// <summary>
        /// Returns which slot index is currently at a given fixed path distance.
        /// Deterministic: computed from belt offset, no 3D distance search.
        /// </summary>
        public int GetSlotAtPathDistance(float pathDistance)
        {
            if (_slotSpacing <= 0f || _slotCount == 0) return 0;
            float rawIndex = (pathDistance - _rotationOffset) / _slotSpacing;
            // FloorToInt: 슬롯 전환이 균일 (RoundToInt는 경계에서 같은 값 반복)
            int slot = Mathf.FloorToInt(rawIndex) % _slotCount;
            return (slot % _slotCount + _slotCount) % _slotCount;
        }

        /// <summary>
        /// Calculates the path distance for a slot at the current belt offset.
        /// Use once at deployment start, then pass to GetSlotAtPathDistance each frame.
        /// </summary>
        public float GetPathDistanceForSlot(int slotIndex)
        {
            return slotIndex * _slotSpacing + _rotationOffset;
        }

        /// <summary>
        /// 현재 배치 중인 deploy point 슬롯. 체인 전파가 이 슬롯과 앞쪽을 건드리지 않게 함.
        /// -1 = 배치 중 아님.
        /// </summary>
        private int _activeDeploySlot = -1;

        /// <summary>현재 배치 중인 holder ID. 이 holder의 다트는 freeze 대상에서 제외.</summary>
        private int _activeDeployHolderId = -1;

        public void SetActiveDeploySlot(int slot) { _activeDeploySlot = slot; }
        public void ClearActiveDeploySlot() { _activeDeploySlot = -1; _activeDeployHolderId = -1; }
        public void SetActiveDeployHolderId(int holderId) { _activeDeployHolderId = holderId; }

        /// <summary>Whether any darts are currently frozen off-belt.</summary>
        public bool HasFrozenDarts => _frozenDartInfos.Count > 0;

        /// <summary>Returns the list of currently frozen darts (read-only).</summary>
        public List<FrozenDartInfo> GetFrozenDarts() => _frozenDartInfos;

        /// <summary>
        /// Resets all slots and conveyor state for a new level.
        /// </summary>
        public void ResetAll()
        {
            if (_slots != null)
            {
                for (int i = 0; i < _slotCount; i++)
                {
                    _slots[i].dartColor = -1;
                    _slots[i].holderId = -1;
                    _slots[i].dartId = -1;
                }
            }
            _occupiedCount = 0;
            _rotationOffset = 0f;
            _nextDartId = 0;
            _boardFinished = false;
            _frozenDartInfos.Clear();
            _activeDeploySlot = -1;
            _activeDeployHolderId = -1;
        }

        #endregion

        #region Private Methods — Event Handlers

        private void HandleBoardCleared(OnBoardCleared evt)
        {
            _boardFinished = true;
            // Force-clear all slots immediately
            if (_slots != null)
            {
                for (int i = 0; i < _slotCount; i++)
                {
                    _slots[i].dartColor = -1;
                    _slots[i].holderId = -1;
                    _slots[i].dartId = -1;
                }
            }
            _occupiedCount = 0;
        }

        private void HandleBoardFailed(OnBoardFailed evt)
        {
            _boardFinished = true;
        }

        private void HandleContinueApplied(OnContinueApplied evt)
        {
            // Resume conveyor after continue — board is back in play
            _boardFinished = false;
            Debug.Log($"[RailManager] Continue applied — conveyor resumed. Occupied={_occupiedCount}/{_slotCount}");
        }

        #endregion

        #region Private Methods

        private void BuildPathFromTransforms()
        {
            _waypoints.Clear();

            if (_waypointTransforms == null || _waypointTransforms.Length == 0)
            {
                return;
            }

            foreach (var t in _waypointTransforms)
            {
                if (t != null)
                {
                    _waypoints.Add(t.position);
                }
            }

            BuildSmoothedPath();
            RecalculatePathLengths();
        }

        private void RecalculatePathLengths()
        {
            _segmentLengths.Clear();
            _cumulativeLengths.Clear();
            _totalPathLength = 0f;

            var path = _smoothedPath;
            if (path.Count < 2) return;

            int segmentCount = _isClosedLoop ? path.Count : path.Count - 1;

            for (int i = 0; i < segmentCount; i++)
            {
                int nextIndex = (i + 1) % path.Count;
                float segLen = Vector3.Distance(path[i], path[nextIndex]);
                _segmentLengths.Add(segLen);
                _totalPathLength += segLen;
                _cumulativeLengths.Add(_totalPathLength);
            }

            _slotSpacing = _totalPathLength > 0f && _slotCount > 0
                ? _totalPathLength / _slotCount
                : 1f;
        }

        /// <summary>
        /// Builds the smoothed path from raw waypoints.
        /// When _smoothCorners is true, replaces sharp corners with circular arcs.
        /// When false, copies waypoints directly.
        /// </summary>
        private void BuildSmoothedPath()
        {
            _smoothedPath.Clear();

            if (_waypoints.Count < 3 || !_smoothCorners)
            {
                _smoothedPath.AddRange(_waypoints);
                return;
            }

            int wpCount = _waypoints.Count;
            int loopCount = _isClosedLoop ? wpCount : wpCount;

            for (int i = 0; i < loopCount; i++)
            {
                int prev = (i - 1 + wpCount) % wpCount;
                int next = (i + 1) % wpCount;

                // Skip first/last for open paths
                if (!_isClosedLoop && (i == 0 || i == wpCount - 1))
                {
                    _smoothedPath.Add(_waypoints[i]);
                    continue;
                }

                Vector3 dirIn = (_waypoints[i] - _waypoints[prev]).normalized;
                Vector3 dirOut = (_waypoints[next] - _waypoints[i]).normalized;

                float dot = Vector3.Dot(dirIn, dirOut);

                // If directions are nearly the same (not a corner), keep the waypoint
                if (dot > 0.95f)
                {
                    _smoothedPath.Add(_waypoints[i]);
                    continue;
                }

                // It's a corner — calculate tangent points and arc
                float distToPrev = Vector3.Distance(_waypoints[i], _waypoints[prev]);
                float distToNext = Vector3.Distance(_waypoints[i], _waypoints[next]);
                float maxRadius = Mathf.Min(distToPrev * 0.45f, distToNext * 0.45f);
                float radius = Mathf.Min(_cornerRadius, maxRadius);

                if (radius < 0.01f)
                {
                    _smoothedPath.Add(_waypoints[i]);
                    continue;
                }

                // Tangent points: pull back from corner along each segment
                Vector3 tangentIn = _waypoints[i] - dirIn * radius;
                Vector3 tangentOut = _waypoints[i] + dirOut * radius;

                // Generate arc points using quadratic Bezier (corner as control point)
                for (int s = 0; s <= CORNER_SUBDIVISIONS; s++)
                {
                    float t = (float)s / CORNER_SUBDIVISIONS;
                    float u = 1f - t;
                    // Quadratic Bezier: P = (1-t)^2 * P0 + 2(1-t)t * P1 + t^2 * P2
                    Vector3 arcPos = u * u * tangentIn
                                   + 2f * u * t * _waypoints[i]
                                   + t * t * tangentOut;
                    _smoothedPath.Add(arcPos);
                }
            }
        }

        /// <summary>
        /// Chain freeze propagation: if a moving dart's world position becomes
        /// directly adjacent (within ~1 slot spacing) to any frozen dart, freeze it too.
        /// Frozen darts are off-belt, so we compare world positions.
        /// Design ref: "대기하지않은 다트들이 대기하는 다트에 바로 인접하게 되면 대기하게 세팅"
        /// </summary>
        private void PropagateFreezeChain()
        {
            if (_frozenDartInfos.Count == 0) return;

            float adjacencyThreshold = _slotSpacing * 1.3f;

            bool changed = true;
            int maxIterations = _slotCount + 1; // 최대 슬롯 수만큼만 반복 (안전장치)
            while (changed && maxIterations-- > 0)
            {
                changed = false;
                for (int s = 0; s < _slotCount; s++)
                {
                    if (_slots[s].dartColor < 0) continue;

                    // deploy point와 그 앞쪽(+1)은 절대 freeze하지 않음
                    if (_activeDeploySlot >= 0)
                    {
                        if (s == _activeDeploySlot) continue;
                        if (s == (_activeDeploySlot + 1) % _slotCount) continue;
                    }

                    Vector3 dartPos = GetSlotWorldPosition(s);

                    for (int f = 0; f < _frozenDartInfos.Count; f++)
                    {
                        float dist = Vector3.Distance(dartPos, _frozenDartInfos[f].worldPosition);
                        if (dist < adjacencyThreshold)
                        {
                            if (FreezeDart(s)) // 실제로 freeze 성공했을 때만
                            {
                                changed = true;
                            }
                            break;
                        }
                    }
                }
            }
        }

        private void PublishOccupancyChanged()
        {
            EventBus.Publish(new OnRailOccupancyChanged
            {
                activeDarts = _occupiedCount,
                totalSlots = _slotCount,
                occupancy = Occupancy
            });
        }

        #endregion

        #region Gizmos

#if UNITY_EDITOR
        private void OnDrawGizmos()
        {
            if (_waypointTransforms == null || _waypointTransforms.Length < 2)
            {
                return;
            }

            Gizmos.color = Color.cyan;
            for (int i = 0; i < _waypointTransforms.Length; i++)
            {
                if (_waypointTransforms[i] == null) continue;

                int nextIndex = (i + 1) % _waypointTransforms.Length;
                if (!_isClosedLoop && i == _waypointTransforms.Length - 1) break;

                if (_waypointTransforms[nextIndex] != null)
                {
                    Gizmos.DrawLine(_waypointTransforms[i].position, _waypointTransforms[nextIndex].position);
                }

                Gizmos.DrawSphere(_waypointTransforms[i].position, 0.1f);
            }

            // Draw occupied slots
            if (_slots != null && Application.isPlaying)
            {
                for (int i = 0; i < _slotCount; i++)
                {
                    Vector3 pos = GetSlotWorldPosition(i);
                    if (_slots[i].dartColor >= 0)
                    {
                        Gizmos.color = HolderVisualManager.GetColor(_slots[i].dartColor);
                        Gizmos.DrawSphere(pos, 0.08f);
                    }
                    else
                    {
                        Gizmos.color = new Color(0.3f, 0.3f, 0.3f, 0.2f);
                        Gizmos.DrawWireSphere(pos, 0.04f);
                    }
                }
            }
        }
#endif

        #endregion
    }
}
