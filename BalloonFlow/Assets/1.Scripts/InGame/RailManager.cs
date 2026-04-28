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
        /// Per-dart individual movement data. Each dart tracks its own progress along the path.
        /// </summary>
        public class DartOnRail
        {
            public int dartId;
            public int dartColor;
            public int holderId;
            public float progress;    // distance along path [0, totalPathLength)
            public bool isFrozen;
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

        private const int CORNER_SUBDIVISIONS = 16; // arc segments per corner (부드러운 곡선)
        private const float MIN_CORNER_RADIUS = 0.2f;
        private const float MAX_CORNER_RADIUS = 5f;

        // 가변 수용량 구간 — 총 다트 수 기준으로 레일 수용량 자동 결정
        // ≤300→50, ≤500→100, ≤700→150, 701+→200
        // 다트 간 간격 제거로 실제 패킹 밀도 증가 → 허용량 상향 (기존 40/80/120/160)
        private static readonly int[] CAPACITY_TIERS = { 50, 100, 150, 200 };
        private static readonly int[] CAPACITY_DART_THRESHOLDS = { 300, 500, 700, int.MaxValue };

        // 이어하기 제거량 — 허용량의 10% (명세: 4/8/12/16)
        private static readonly int[] CONTINUE_REMOVE_COUNTS = { 4, 8, 12, 16 };

        /// <summary>허용량에 따른 이어하기 다트 제거량 반환.</summary>
        public static int GetContinueRemoveCount(int capacity)
        {
            for (int i = 0; i < CAPACITY_TIERS.Length; i++)
            {
                if (capacity <= CAPACITY_TIERS[i])
                    return CONTINUE_REMOVE_COUNTS[i];
            }
            return CONTINUE_REMOVE_COUNTS[CONTINUE_REMOVE_COUNTS.Length - 1];
        }

        /// <summary>
        /// 허용량에 따른 레일 면 수 반환.
        /// 50→1면(하단), 100→2면(하단+우측), 150→3면(하단+우측+상단), 200→4면(전체)
        /// </summary>
        public static int GetRailSideCount(int capacity)
        {
            if (capacity <= 50) return 1;
            if (capacity <= 100) return 2;
            if (capacity <= 150) return 3;
            return 4;
        }
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

        /// <summary>
        /// When true, rail rotation is paused (e.g. during booster execution).
        /// </summary>
        public bool IsPausedByBooster { get; set; }

        // Per-dart individual movement system
        private readonly List<DartOnRail> _darts = new List<DartOnRail>();

        // Off-belt frozen dart system: darts removed from slots and held at fixed world positions
        public struct FrozenDartInfo
        {
            public int dartId;
            public int color;
            public int holderId;
            public Vector3 worldPosition;
            public int originalSlotIndex;
        }
        private readonly List<FrozenDartInfo> _frozenDartInfos = new List<FrozenDartInfo>();

        #endregion

        #region Properties

        public float TotalPathLength => _totalPathLength;
        public int WaypointCount => _waypoints.Count;
        public bool IsClosedLoop => _isClosedLoop;
        public int SlotCount => _slotCount;
        public int OccupiedCount => _occupiedCount;

        /// <summary>Active + frozen 다트 합계. event publish 및 capacity-1 boundary 검출용.</summary>
        public int EffectiveOccupiedCount => _occupiedCount + _frozenDartInfos.Count;

        /// <summary>물리적 packing 최대 다트 수 = min(_slotCount, floor(pathLen / physGap)).
        /// physGap > slotSpacing 인 visual mismatch 케이스에서 _slotCount 그대로 채울 수 없음.
        /// railFull / 배포 슬로우 해제 등 "가득" 임계 검출에 사용.
        /// </summary>
        public int PhysicalCapacity
        {
            get
            {
                if (_totalPathLength <= 0f) return _slotCount;
                float gap = DartPhysicalGap;
                if (gap <= 0f) return _slotCount;
                int phys = Mathf.FloorToInt(_totalPathLength / gap);
                return Mathf.Min(_slotCount, Mathf.Max(1, phys));
            }
        }

        /// <summary>Occupancy ratio 0.0 ~ 1.0.</summary>
        public float Occupancy => _slotCount > 0 ? (float)_occupiedCount / _slotCount : 0f;

        /// <summary>
        /// 유저 속도 조절 배율 (홀드 가속 + x2 토글).
        /// GameSpeedController가 매 프레임 설정. 기본 1.0.
        /// </summary>
        public float UserSpeedMultiplier { get; set; } = 1f;

        /// <summary>
        /// Conveyor belt rotation speed in slots per second.
        /// UserSpeedMultiplier가 이미 반영된 값 — DartManager 등 하위 소비자도 자동으로 가속 적용.
        /// </summary>
        public float RotationSpeed => _rotationSpeed * UserSpeedMultiplier;

        /// <summary>유저 가속 미반영 원본 레벨 속도.</summary>
        public float BaseRotationSpeed => _rotationSpeed;

        /// <summary>Distance between adjacent slots on the path.</summary>
        public float SlotSpacing => _slotSpacing;

        /// <summary>
        /// 다트가 서로 막혀 정지할 때의 물리적 최소 간격 = 다트 비주얼 크기.
        /// Deploy point 대기/주행 중 간격이 벌어지지 않고 밀집 정렬되도록 함.
        /// </summary>
        public float DartPhysicalGap => GameManager.HasInstance ? GameManager.Instance.Board.dartScale * GameManager.Instance.Board.dartSpacingMultiplier : 0.275f;

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
            if (IsPausedByBooster) return;

            // 회전 속도: 남은 다트 수 기반 + 배치 중 감속 + 유저 가속(홀드/x2토글)
            float baseSpeedMult = GetSpeedMultiplier();
            float beltDelta = _rotationSpeed * UserSpeedMultiplier * _slotSpacing * Time.deltaTime * baseSpeedMult;
            _rotationOffset += beltDelta;

            float pathLen = _totalPathLength;
            bool hasWrap = pathLen > 0f;
            if (hasWrap && _rotationOffset >= pathLen) _rotationOffset -= pathLen;

            // Chain freeze: moving darts that reach frozen darts also freeze
            PropagateFreezeChain();

            int dartCount = _darts.Count;
            if (dartCount == 0) return;

            float physicalGap = DartPhysicalGap;
            float dartDelta = beltDelta;

            // 가득 시 uniform advance — 모든 다트가 belt와 함께 동일 속도 회전.
            // (deploy point 장애물 제거됐으므로 일반적인 packing 으로도 회전 가능하지만,
            //  빡빡하게 가득찬 상태에서는 packing이 잠길 수 있어 안전망으로 유지.)
            bool railFullUniform = (_occupiedCount + _frozenDartInfos.Count) >= PhysicalCapacity - 1;
            if (railFullUniform)
            {
                for (int i = 0; i < dartCount; i++)
                {
                    DartOnRail dart = _darts[i];
                    float newProg = dart.progress + beltDelta;
                    if (hasWrap && newProg >= pathLen) newProg -= pathLen;
                    dart.progress = newProg;
                }
                return;
            }

            // ─── 일반 상태: packing/cluster physics (deploy point freeze 시각 유지) ───

            // progress 내림차순 정렬
            _sortedDartIndices.Clear();
            for (int i = 0; i < dartCount; i++) _sortedDartIndices.Add(i);
            if (_dartProgressDescending == null)
                _dartProgressDescending = (a, b) => _darts[b].progress.CompareTo(_darts[a].progress);
            _sortedDartIndices.Sort(_dartProgressDescending);

            // wrap 구간 걸침 감지
            int headPosInSorted = 0;
            if (hasWrap && dartCount > 1)
            {
                float highest = _darts[_sortedDartIndices[0]].progress;
                float lowest = _darts[_sortedDartIndices[dartCount - 1]].progress;
                if ((highest - lowest) > pathLen * 0.5f)
                {
                    float maxGapAhead = -1f;
                    for (int p = 0; p < dartCount; p++)
                    {
                        int idx = _sortedDartIndices[p];
                        float myProg = _darts[idx].progress;
                        float minAhead = float.MaxValue;
                        for (int j = 0; j < dartCount; j++)
                        {
                            if (j == idx) continue;
                            float d = _darts[j].progress - myProg;
                            if (d < 0f) d += pathLen;
                            if (d > 0.001f && d < minAhead) minAhead = d;
                        }
                        if (minAhead > maxGapAhead)
                        {
                            maxGapAhead = minAhead;
                            headPosInSorted = p;
                        }
                    }
                }
            }

            // Deploy point 장애물 — 다트가 deploy point 직전에 settle. 한 칸 안 잡아먹도록 offset=0.
            // (이전: deployOffset = 0.5*physGap → 1.5*physGap 낭비. 지금: 0 → physGap 만 낭비 = 정확히 1 슬롯.)
            // 다트 간격 일정 유지(packing 안정화) + deploy point 기능(freeze 등) 모두 유지.
            bool hasDeployPoints = _activeDeployPoints.Count > 0;

            for (int offset = 0; offset < dartCount; offset++)
            {
                int p = headPosInSorted + offset;
                if (p >= dartCount) p -= dartCount;
                int idx = _sortedDartIndices[p];
                DartOnRail dart = _darts[idx];
                float myProg = dart.progress;

                float closest = float.MaxValue;
                for (int i = 0; i < dartCount; i++)
                {
                    if (i == idx) continue;
                    float d = _darts[i].progress - myProg;
                    if (hasWrap && d < 0f) d += pathLen;
                    if (d > 0.001f && d < closest) closest = d;
                }

                if (hasDeployPoints)
                {
                    foreach (var kvp in _deployPoints)
                    {
                        if (!_activeDeployPoints.Contains(kvp.Key)) continue;
                        float d = kvp.Value - myProg; // offset=0: 장애물 = deployProgress 그 자체
                        if (hasWrap && d < 0f) d += pathLen;
                        if (d > 0.001f && d < closest) closest = d;
                    }
                }

                float maxAdvance = dartDelta;
                if (closest < float.MaxValue)
                {
                    float stopDist = closest - physicalGap;
                    if (stopDist < 0f) stopDist = 0f;
                    if (stopDist < maxAdvance) maxAdvance = stopDist;
                }

                if (maxAdvance > 0.001f)
                {
                    float newProg = myProg + maxAdvance;
                    if (hasWrap && newProg >= pathLen) newProg -= pathLen;
                    dart.progress = newProg;
                }
            }
        }

        // Sort helpers for front-to-back dart iteration (packing physics 시 선두부터 처리)
        private readonly List<int> _sortedDartIndices = new List<int>(256);
        private System.Comparison<int> _dartProgressDescending;

        /// <summary>
        /// 점유율 기반 속도 배율 (배치 감속 제외).
        /// 남은 다트 >= capacity: 1배 / 미만: 2배.
        /// 공격 속도 동기화(dartBalloonSyncedFireMode)에도 사용.
        /// </summary>
        public float GetOccupancySpeedMultiplier()
        {
            // 남은 다트 수 = 레일 위 + 보관함 전체
            int totalRemaining = _darts.Count;
            if (HolderManager.HasInstance)
            {
                var holders = HolderManager.Instance.GetHolders();
                for (int i = 0; i < holders.Length; i++)
                {
                    if (!holders[i].isConsumed)
                        totalRemaining += holders[i].magazineCount;
                }
            }

            return totalRemaining < _slotCount ? 2f : 1f;
        }

        /// <summary>
        /// 남은 다트 수 기반 회전 속도 배율.
        /// 남은 다트 >= capacity: 1배 / 미만: 2배
        /// 배치 중(deploy point 활성화): ×0.5 (중복 감속 없음)
        /// </summary>
        private float GetSpeedMultiplier()
        {
            float baseMult = GetOccupancySpeedMultiplier();

            // 실제 다트 배치 중(첫 다트 투입 후)일 때만 ×0.5
            // synced 모드에선 배치 감속 자체를 비활성화
            // PhysicalCapacity - 1 도달 시 belt 정상 속도로 회전 (배포 슬로우 해제).
            // 사용자 spec: "delay 없이 컨베이어벨트 돌아감".
            // Deploy point 장애물 제거 후 배포 중 감속 의미 없음 (벨트는 항상 정상 속도 회전).
            // baseMult = 점유율 기반 (후반 ×2) 만 유지.

            return baseMult;
        }

        /// <summary>활성 deploy point. holderId → progress on path.</summary>
        private readonly Dictionary<int, float> _deployPoints = new Dictionary<int, float>();
        /// <summary>배치 시작된 deploy point (첫 다트 투입 후 → 장애물 활성화).</summary>
        private readonly HashSet<int> _activeDeployPoints = new HashSet<int>();

        /// <summary>deploy point 등록 (대기 상태 — 아직 장애물 아님).</summary>
        public void RegisterDeployPoint(int holderId, float progress)
        {
            _deployPoints[holderId] = progress;
            // 아직 _activeDeployPoints에 추가하지 않음 → 빈틈 기다림
        }

        /// <summary>deploy point 활성화 (첫 다트 배치 후 → 장애물로 전환).</summary>
        public void ActivateDeployPoint(int holderId)
        {
            _activeDeployPoints.Add(holderId);
        }

        /// <summary>deploy point 해제. 다트는 다음 프레임부터 자연스럽게 이동 재개.</summary>
        public void UnregisterDeployPoint(int holderId)
        {
            _deployPoints.Remove(holderId);
            _activeDeployPoints.Remove(holderId);
        }

        /// <summary>
        /// 다트 앞에 있는 가장 가까운 장애물까지의 경로상 거리.
        /// 장애물 = 다른 다트 or 다른 holder의 deploy point.
        /// -1 = 앞에 장애물 없음.
        /// </summary>
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

            // closedLoop: 물리적 순환 (4면 사각형)
            // 비closedLoop: 끝 도달 시 시작점으로 순간이동 (1~3면)
            // 어느 쪽이든 distance를 래핑하여 순환
            if (_totalPathLength > 0f)
            {
                distance = ((distance % _totalPathLength) + _totalPathLength) % _totalPathLength;
            }

            // 이진 탐색으로 세그먼트 찾기 (O(log n))
            int lo = 0, hi = _cumulativeLengths.Count - 1;
            while (lo < hi)
            {
                int mid = (lo + hi) >> 1;
                if (_cumulativeLengths[mid] < distance)
                    lo = mid + 1;
                else
                    hi = mid;
            }

            int i = lo;
            if (i < _segmentLengths.Count)
            {
                float segStart = (i > 0) ? _cumulativeLengths[i - 1] : 0f;
                float segLength = _segmentLengths[i];
                if (segLength <= 0f) return path[i];
                float localT = (distance - segStart) / segLength;
                int nextIndex = (i + 1) % path.Count;
                return Vector3.Lerp(path[i], path[nextIndex], localT);
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
            _darts.Clear();

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

            // frozen 다트가 복귀할 공간 확보 — 예약분 초과 시 배치 거부
            if (_occupiedCount + _frozenDartInfos.Count >= _slotCount)
            {
                return -1;
            }

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
        /// <param name="ignoreFrozenReserve">true면 frozen 예약분 무시 (UnfreezeAndReinsertAll 전용)</param>
        public int FindNextEmptySlot(int startIndex, bool ignoreFrozenReserve = false)
        {
            if (_slots == null || _occupiedCount >= _slotCount)
                return -1;

            // 배치 시: frozen 다트가 복귀할 공간 확보
            if (!ignoreFrozenReserve && _occupiedCount + _frozenDartInfos.Count >= _slotCount)
                return -1;

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

        #region Public Methods — Per-Dart System

        /// <summary>Returns all darts on the belt (per-dart system).</summary>
        public IReadOnlyList<DartOnRail> GetAllDarts() => _darts;

        /// <summary>월드 좌표를 경로상 progress로 변환 (가장 가까운 지점).</summary>
        public float GetProgressAtWorldPos(Vector3 worldPos)
        {
            float bestDist = float.MaxValue;
            float bestProg = 0f;
            int samples = Mathf.Max(100, _slotCount * 2);
            for (int i = 0; i < samples; i++)
            {
                float prog = (i / (float)samples) * _totalPathLength;
                float d = Vector3.Distance(worldPos, GetPositionAtDistance(prog));
                if (d < bestDist) { bestDist = d; bestProg = prog; }
            }
            return bestProg;
        }

        /// <summary>해당 progress에 배치 가능한지 체크.
        /// 모든 다트와의 간격 확인 — 빈틈이 있을 때만 배치 가능.
        /// 밀집 배치를 위해 물리 간격(DartPhysicalGap) 기준으로 체크 → 다트 단위로 빠르게 연쇄 배치됨.
        /// 마지막 슬롯(capacity-1) 도달 시: deploy point obstacle 영향으로 다트가 0.5*gap 까지
        /// 가까이 settle 가능 → minGap을 0.4로 완화해야 200/200 도달 → railFull → belt 회전.
        /// </summary>
        public bool IsProgressClear(float progress, int holderId)
        {
            if (_darts.Count >= _slotCount) return false;
            float minGap = DartPhysicalGap * 0.9f;
            for (int i = 0; i < _darts.Count; i++)
            {
                float diff = Mathf.Abs(_darts[i].progress - progress);
                if (_totalPathLength > 0f)
                    diff = Mathf.Min(diff, _totalPathLength - diff);
                if (diff < minGap) return false;
            }
            return true;
        }

        private readonly List<float> _gapSortBuffer = new List<float>(256);

        /// <summary>
        /// targetProgress 근처의 빈틈을 찾아 그 중심 progress 반환.
        /// 빈틈 중심이 targetProgress 로부터 maxOffset 이내, 폭이 minGapWidth 이상이면 true.
        /// 여러 후보 중 targetProgress 에 가장 가까운 중심을 반환.
        /// uniform 모드에서 fire로 생긴 2*physGap gap이 deploy point에 맞물렸을 때만
        /// 배치하기 위한 헬퍼. half-integer offset(packing 결과)이 있어도 ±physGap 범위로
        /// 검출 가능.
        /// </summary>
        public bool TryFindGapNearProgress(float targetProgress, float maxOffset, float minGapWidth, out float gapCenter)
        {
            gapCenter = -1f;
            int n = _darts.Count;
            float pathLen = _totalPathLength;
            if (pathLen <= 0f) return false;

            if (n == 0)
            {
                gapCenter = targetProgress;
                return true;
            }

            _gapSortBuffer.Clear();
            for (int i = 0; i < n; i++) _gapSortBuffer.Add(_darts[i].progress);
            _gapSortBuffer.Sort();

            float bestOffset = float.MaxValue;
            float bestCenter = -1f;
            for (int i = 0; i < n; i++)
            {
                float curr = _gapSortBuffer[i];
                float next = (i + 1 < n) ? _gapSortBuffer[i + 1] : _gapSortBuffer[0] + pathLen;
                float gap = next - curr;
                if (gap < minGapWidth) continue;

                float center = (curr + next) * 0.5f;
                if (center >= pathLen) center -= pathLen;

                float diff = Mathf.Abs(center - targetProgress);
                if (diff > pathLen * 0.5f) diff = pathLen - diff;

                if (diff <= maxOffset && diff < bestOffset)
                {
                    bestOffset = diff;
                    bestCenter = center;
                }
            }

            if (bestCenter >= 0f)
            {
                gapCenter = bestCenter;
                return true;
            }
            return false;
        }

        /// <summary>Place a dart at a specific progress on the path.</summary>
        public int PlaceDartAtProgress(float progress, int color, int holderId)
        {
            if (_darts.Count >= _slotCount) return -1; // capacity check
            int id = _nextDartId++;
            _darts.Add(new DartOnRail { dartId = id, dartColor = color, holderId = holderId, progress = progress, isFrozen = false });
            _occupiedCount = _darts.Count;
            PublishOccupancyChanged();
            return id;
        }

        /// <summary>
        /// targetProgress 근처의 빈 progress 를 forward/backward 양방향 스캔으로 탐색.
        /// 0.5*physGap step 으로 점진적 확장. IsProgressClear 만족하는 첫 progress 반환,
        /// 못 찾으면 -1. cb78e1b "capacity-1 deploy point 배치 락 해소" 의 핵심 헬퍼.
        /// </summary>
        public float FindClearProgressNear(float targetProgress, int holderId)
        {
            if (_darts.Count >= _slotCount) return -1f;
            if (IsProgressClear(targetProgress, holderId)) return targetProgress;

            float pathLen = _totalPathLength;
            if (pathLen <= 0f) return -1f;

            float step = DartPhysicalGap * 0.5f;
            int maxIterations = Mathf.CeilToInt(pathLen / step / 2f) + 1;
            for (int i = 1; i <= maxIterations; i++)
            {
                float forward = ((targetProgress + i * step) % pathLen + pathLen) % pathLen;
                if (IsProgressClear(forward, holderId)) return forward;
                float backward = ((targetProgress - i * step) % pathLen + pathLen) % pathLen;
                if (IsProgressClear(backward, holderId)) return backward;
            }
            return -1f;
        }

        /// <summary>Remove a dart by ID.</summary>
        public bool RemoveDartById(int dartId)
        {
            for (int i = 0; i < _darts.Count; i++)
            {
                if (_darts[i].dartId == dartId)
                {
                    _darts.RemoveAt(i);
                    _occupiedCount = _darts.Count;
                    return true;
                }
            }
            return false;
        }

        /// <summary>Find a dart by ID.</summary>
        public DartOnRail FindDart(int dartId)
        {
            for (int i = 0; i < _darts.Count; i++)
                if (_darts[i].dartId == dartId) return _darts[i];
            return null;
        }

        /// <summary>Get world position for a dart.</summary>
        public Vector3 GetDartWorldPosition(int dartId)
        {
            var dart = FindDart(dartId);
            return dart != null ? GetPositionAtDistance(dart.progress) : Vector3.zero;
        }

        /// <summary>Get firing direction for a dart based on its progress along the path.</summary>
        public Vector3 GetDartFiringDirection(int dartId)
        {
            var dart = FindDart(dartId);
            if (dart == null) return Vector3.forward;
            float t = _totalPathLength > 0f ? dart.progress / _totalPathLength : 0f;
            t = ((t % 1f) + 1f) % 1f;
            Vector3 moveDir = GetDirectionAtNormalized(t);
            // Reuse same cardinal logic as GetSlotFiringDirection
            return GetFiringDirectionFromMoveDir(moveDir);
        }

        /// <summary>Check if there's space to place a dart near a world position.</summary>
        public float FindInsertionProgress(Vector3 worldPos)
        {
            // Convert world pos to nearest path progress
            float bestDist = float.MaxValue;
            float bestProgress = 0f;
            int segments = Mathf.Max(50, _slotCount);
            for (int i = 0; i < segments; i++)
            {
                float prog = (i / (float)segments) * _totalPathLength;
                Vector3 pos = GetPositionAtDistance(prog);
                float d = Vector3.Distance(worldPos, pos);
                if (d < bestDist) { bestDist = d; bestProgress = prog; }
            }

            // Check if there's enough gap from existing darts (frozen 다트는 무시 — 곧 이동할 것)
            float minGap = _slotSpacing * 0.8f;
            for (int i = 0; i < _darts.Count; i++)
            {
                if (_darts[i].isFrozen) continue; // frozen 다트는 간격 체크 제외
                float diff = Mathf.Abs(_darts[i].progress - bestProgress);
                if (_totalPathLength > 0f)
                    diff = Mathf.Min(diff, _totalPathLength - diff); // wrap-around
                if (diff < minGap) return -1f; // too close
            }

            if (_darts.Count >= _slotCount) return -1f; // full
            return bestProgress;
        }

        /// <summary>Freeze a dart by ID.</summary>
        public void FreezeDartById(int dartId)
        {
            var dart = FindDart(dartId);
            if (dart != null) dart.isFrozen = true;
        }

        /// <summary>Unfreeze all darts.</summary>
        public void UnfreezeAllDarts()
        {
            for (int i = 0; i < _darts.Count; i++)
                _darts[i].isFrozen = false;
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
        /// Returns the set of dart colors currently on the rail (progress-based system).
        /// HasOutermostMatch fail 판정에 사용 — 색상 교집합 체크 (doc spec line 322-329).
        /// 이전 _slots[] 기반 구현은 PlaceDartAtProgress가 _slots를 채우지 않아 항상 empty
        /// 반환하던 버그. _darts 리스트로 전환.
        /// </summary>
        public HashSet<int> GetRailDartColors()
        {
            var colors = new HashSet<int>();
            for (int i = 0; i < _darts.Count; i++)
            {
                int c = _darts[i].dartColor;
                if (c >= 0) colors.Add(c);
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
        /// 이어하기: 가장 많은 색상의 다트를 count개 제거.
        /// 제거된 색상을 out으로 반환 (풍선도 같은 색상으로 제거해야 함).
        /// </summary>
        public int RemoveDartsByMostCommonColor(int count, out int removedColor)
        {
            removedColor = -1;
            if (_slots == null || count <= 0) return 0;

            // 색상별 카운트
            var colorCounts = new Dictionary<int, int>();
            for (int i = 0; i < _slotCount; i++)
            {
                int c = _slots[i].dartColor;
                if (c < 0) continue;
                if (colorCounts.ContainsKey(c)) colorCounts[c]++;
                else colorCounts[c] = 1;
            }

            if (colorCounts.Count == 0) return 0;

            // 가장 많은 색상 찾기
            int maxCount = 0;
            foreach (var kvp in colorCounts)
            {
                if (kvp.Value > maxCount) { maxCount = kvp.Value; removedColor = kvp.Key; }
            }

            // 해당 색상 다트만 제거 (최근 배치 순)
            var targets = new List<int>();
            for (int i = 0; i < _slotCount; i++)
            {
                if (_slots[i].dartColor == removedColor) targets.Add(i);
            }
            targets.Sort((a, b) => _slots[b].dartId.CompareTo(_slots[a].dartId));

            int removed = 0;
            for (int i = 0; i < targets.Count && removed < count; i++)
            {
                ClearSlot(targets[i]);
                removed++;
            }
            return removed;
        }

        /// <summary>이어하기: 레일 위 다트 색상 중 가장 많은 색을 반환. 다트 없으면 -1.</summary>
        public int FindMostNumerousDartColor()
        {
            if (_darts.Count == 0) return -1;
            // 작은 수의 색상만 있으므로 Dictionary 대신 List 스캔으로 충분 (alloc 회피)
            int bestColor = -1, bestCount = 0;
            for (int i = 0; i < _darts.Count; i++)
            {
                int c = _darts[i].dartColor;
                if (c < 0) continue;
                int cnt = 0;
                for (int j = 0; j < _darts.Count; j++)
                    if (_darts[j].dartColor == c) cnt++;
                if (cnt > bestCount)
                {
                    bestCount = cnt;
                    bestColor = c;
                }
            }
            return bestColor;
        }

        /// <summary>이어하기: 지정한 색상의 다트를 모두 제거. 제거된 개수 반환.</summary>
        public int RemoveDartsByColor(int color)
        {
            return RemoveDartsByColor(color, int.MaxValue);
        }

        /// <summary>이어하기: 지정한 색상의 다트를 최대 maxCount 개까지 제거.
        /// 최근 배치 다트(_darts 끝쪽)부터 우선 제거. 제거된 개수 반환.</summary>
        public int RemoveDartsByColor(int color, int maxCount)
        {
            if (color < 0 || maxCount <= 0) return 0;
            int removed = 0;
            for (int i = _darts.Count - 1; i >= 0 && removed < maxCount; i--)
            {
                if (_darts[i].dartColor == color)
                {
                    _darts.RemoveAt(i);
                    removed++;
                }
            }
            if (removed > 0)
            {
                _occupiedCount = _darts.Count;
                PublishOccupancyChanged();
            }
            return removed;
        }

        /// <summary>지정한 색상의 다트가 rail에 몇 개 있는지 반환.</summary>
        public int CountDartsByColor(int color)
        {
            if (color < 0) return 0;
            int count = 0;
            for (int i = 0; i < _darts.Count; i++)
                if (_darts[i].dartColor == color) count++;
            return count;
        }

        /// <summary>이어하기: 최근 배치 다트부터 count개 제거. 제거된 색상 목록 반환.</summary>
        public int RemoveRecentDarts(int count, out int removedColor)
        {
            removedColor = -1;
            if (count <= 0) return 0;

            // per-dart 시스템: dartId 내림차순 = 최근 배치 순
            var sorted = new List<DartOnRail>(_darts);
            sorted.Sort((a, b) => b.dartId.CompareTo(a.dartId));

            int removed = 0;
            for (int i = 0; i < sorted.Count && removed < count; i++)
            {
                if (removed == 0) removedColor = sorted[i].dartColor;
                RemoveDartById(sorted[i].dartId);
                removed++;
            }

            // 슬롯 시스템도 처리
            if (_slots != null)
            {
                var slotTargets = new List<int>();
                for (int i = 0; i < _slotCount; i++)
                    if (_slots[i].dartColor >= 0) slotTargets.Add(i);
                slotTargets.Sort((a, b) => _slots[b].dartId.CompareTo(_slots[a].dartId));
                for (int i = 0; i < slotTargets.Count && removed < count; i++)
                {
                    if (removed == 0 && removedColor < 0) removedColor = _slots[slotTargets[i]].dartColor;
                    ClearSlot(slotTargets[i]);
                    removed++;
                }
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
            if (_slots == null) { _frozenDartInfos.Clear(); return; }
            int lostCount = 0;
            for (int i = 0; i < _frozenDartInfos.Count; i++)
            {
                var info = _frozenDartInfos[i];
                int nearestSlot = GetNearestSlotIndex(info.worldPosition);
                int emptySlot = FindNextEmptySlot(nearestSlot, ignoreFrozenReserve: true);

                // 빈 슬롯이 없으면 원래 슬롯에 강제 복귀 시도
                if (emptySlot < 0)
                {
                    emptySlot = info.originalSlotIndex;
                    if (emptySlot >= 0 && emptySlot < _slotCount && _slots[emptySlot].dartColor >= 0)
                    {
                        // 원래 슬롯도 점유됨 — 전체 순회로 빈 칸 재탐색
                        for (int j = 0; j < _slotCount; j++)
                        {
                            if (_slots[j].dartColor < 0) { emptySlot = j; break; }
                        }
                        // 정말 없으면 -1
                        if (emptySlot >= 0 && emptySlot < _slotCount && _slots[emptySlot].dartColor >= 0)
                            emptySlot = -1;
                    }
                }

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
                else
                {
                    lostCount++;
                }
            }

            if (lostCount > 0)
                Debug.LogWarning($"[RailManager] UnfreezeAndReinsertAll: {lostCount} darts lost — rail full ({_occupiedCount}/{_slotCount})");

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
            _darts.Clear();
            _deployPoints.Clear();
            _activeDeployPoints.Clear();
            _frozenDartInfos.Clear();
            _activeDeploySlot = -1;
            _activeDeployHolderId = -1;
        }

        #endregion

        #region Private Methods — Event Handlers

        private void HandleBoardCleared(OnBoardCleared evt)
        {
            _boardFinished = true;
            _deployPoints.Clear();
            _activeDeployPoints.Clear();
            _darts.Clear();
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
            _deployPoints.Clear();
            _activeDeployPoints.Clear();
            _darts.Clear();
        }

        private void HandleContinueApplied(OnContinueApplied evt)
        {
            // Resume conveyor after continue — board is back in play
            _boardFinished = false;
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

                // 원호 중심 = 코너에서 안쪽 방향
                Vector3 cross = Vector3.Cross(dirIn, dirOut);
                Vector3 bisector = ((-dirIn) + dirOut).normalized;
                // cross.y로 회전 방향 판별 → 안쪽으로 향하도록 보정
                if (cross.y > 0f) bisector = -bisector;

                float halfAngle = Mathf.Acos(Mathf.Clamp(dot, -1f, 1f)) * 0.5f;
                float sinHalf = Mathf.Sin(halfAngle);
                float centerDist = sinHalf > 0.01f ? radius / sinHalf : radius;
                Vector3 arcCenter = _waypoints[i] + bisector * centerDist;

                // 원호 보간 (Slerp)
                Vector3 startDir = (tangentIn - arcCenter).normalized;
                Vector3 endDir = (tangentOut - arcCenter).normalized;
                float arcRadius = Vector3.Distance(tangentIn, arcCenter);

                for (int s = 0; s <= CORNER_SUBDIVISIONS; s++)
                {
                    float t = (float)s / CORNER_SUBDIVISIONS;
                    Vector3 dir = Vector3.Slerp(startDir, endDir, t);
                    Vector3 arcPos = arcCenter + dir * arcRadius;
                    arcPos.y = Mathf.Lerp(tangentIn.y, tangentOut.y, t);
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

        /// <summary>
        /// Derives the inward firing direction from a movement direction along the belt.
        /// Uses the same cardinal logic as GetSlotFiringDirection.
        /// </summary>
        private Vector3 GetFiringDirectionFromMoveDir(Vector3 moveDir)
        {
            float absX = Mathf.Abs(moveDir.x);
            float absZ = Mathf.Abs(moveDir.z);

            if (absX >= absZ)
            {
                // Moving +X = along bottom edge → fire north
                // Moving -X = along top edge → fire south
                return moveDir.x >= 0f ? Vector3.forward : Vector3.back;
            }
            else
            {
                // Moving +Z = right wall → fire left (west)
                // Moving -Z = left wall → fire right (east)
                return moveDir.z >= 0f ? Vector3.left : Vector3.right;
            }
        }

        private void PublishOccupancyChanged()
        {
            // frozen 다트도 실질적 점유로 포함
            int effectiveCount = _occupiedCount + _frozenDartInfos.Count;
            float effectiveOccupancy = _slotCount > 0 ? (float)effectiveCount / _slotCount : 0f;
            EventBus.Publish(new OnRailOccupancyChanged
            {
                activeDarts = effectiveCount,
                totalSlots = _slotCount,
                occupancy = effectiveOccupancy
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
