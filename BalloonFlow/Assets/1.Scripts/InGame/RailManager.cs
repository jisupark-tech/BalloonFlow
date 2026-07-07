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
            public float progress;    // distance along path [0, totalPathLength). LEGACY — slotIndex 우선 사용.
            public bool isFrozen;
            // 배치 순서 식별용 단조 증가 ID. 이어하기 시 "최근 배치 다트" 선정에 사용.
            // dartId는 발사로 RemoveDartById 후 새 배치에서 재할당될 수 있으므로 별도 시퀀스 유지.
            public long placedSeq;

            // 사용자 요구: slot index 기반 통일 — 배치/이동/Freeze 시 간격 일관성 보장.
            // -1 = 미할당. 다트 점유 slot 의 array index. _slots[slotIndex].dartId == this.dartId 와 동기화.
            // 위치 계산: rail.GetPositionAtSlot(slotIndex) 사용 (progress 거리 비례 대체).
            public int slotIndex = -1;
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
        // 2026-05-08: 1f → 0f. V2 freeze 폐기 후 packing physics 가 deploy block obstacle 검사 시
        //   stopDist=closest-physGap 로 자동 1 slot buffer 적용 → cluster head 가 deploy point 의 1 slot 직전 정지.
        //   clearance=0 시 cluster head ↔ deploy holder placement 사이 spacing physGap 1 slot (visual 인접) → capacity 도달.
        //   clearance=1 (이전) 시 추가 1 slot buffer → visual 2 slot gap → capacity 도달 못함 (사용자 의도 위배).
        // 롤백: 1f 로 변경.
        private const float DEPLOY_POINT_CLEARANCE_GAP = 0f;
        private const float PACKING_ASSIST_GAP_START = 1.75f;
        private const float PACKING_ASSIST_GAP_STOP = 1.1f;
        private const float PACKING_ASSIST_SPEED_SCALE = 0.4f;
        private const float PACKING_ASSIST_CATCH_UP_MULTIPLIER = 0.75f;
        // 이전: 3. Fire가 연속으로 2~3발 빠지면 deadlock 중 196/200 아래로 내려가 full-belt advance가 끊김.
        // deadlock 상태에서는 빈칸이 deploy point까지 순환해야 하므로 5칸까지 전체 벨트 회전을 유지한다.
        private const int DEADLOCK_BELT_ADVANCE_EMPTY_SLOTS = 5;
        private const float DEPLOY_PHYSICAL_GAP_TOLERANCE = 0.01f;
        // ROLLBACK_DART_REMOVE_LOG_THROTTLE:
        // These diagnostics format large strings during active firing/near-full movement. Keep them
        // opt-in so gameplay profiling measures logic, not console logging.
        private static readonly bool LOG_RAIL_ADVANCE_DIAG = false;
        private static readonly bool LOG_DART_REMOVE_DIAG = false;
        private const float RAIL_ADVANCE_DIAG_INTERVAL = 1.0f;

        // 가변 수용량 구간 — 총 다트 수 기준으로 레일 수용량 자동 결정
        // ≤300→40, ≤500→80, ≤700→120, 701+→160
        // Spec capacity tiers: 40/80/120/160.
        private static readonly int[] CAPACITY_TIERS = { 40, 80, 120, 160 };
        private static readonly int[] CAPACITY_DART_THRESHOLDS = { 300, 500, 700, int.MaxValue };

        // 이어하기 제거량 — 허용량의 10% (명세: 4/8/12/16)
        private static readonly int[] CONTINUE_REMOVE_COUNTS = { 4, 8, 12, 16 };
        private static readonly int[] MAGAZINE_MAX_VALUES = { 30, 40, 50, 50 };

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

        public static int GetMagazineMaxForCapacity(int capacity)
        {
            if (capacity <= 0) return MAGAZINE_MAX_VALUES[MAGAZINE_MAX_VALUES.Length - 1];

            for (int i = 0; i < CAPACITY_TIERS.Length; i++)
            {
                if (capacity <= CAPACITY_TIERS[i])
                    return MAGAZINE_MAX_VALUES[i];
            }
            return MAGAZINE_MAX_VALUES[MAGAZINE_MAX_VALUES.Length - 1];
        }

        /// <summary>
        /// 허용량에 따른 레일 면 수 반환.
        /// 40→1면(하단), 80→2면(하단+우측), 120→3면(하단+우측+상단), 160→4면(전체)
        /// </summary>
        public static int GetRailSideCount(int capacity)
        {
            if (capacity <= 40) return 1;
            if (capacity <= 80) return 2;
            if (capacity <= 120) return 3;
            return 4;
        }

        #endregion

        #region Fields

        private readonly List<Vector3> _waypoints = new List<Vector3>();
        private readonly List<Vector3> _smoothedPath = new List<Vector3>(); // smoothed version (or copy of waypoints)
        private readonly List<float> _segmentLengths = new List<float>();
        private readonly List<float> _cumulativeLengths = new List<float>();
        // [Optimization 2026-05-10] segment 별 normalized direction 캐시. RecalculatePathLengths 에서 1회 build.
        // GetDirectionAtDistance 가 이진 탐색 + 배열 lookup 으로 O(log n) — 기존 GetDirectionAtNormalized 의 GetPositionAtDistance × 2 + sqrt 패턴 제거.
        // dart 200개 시 매 frame 이진 탐색 600 → 200, sqrt 400+ → 0 효과.
        // 롤백: 이 필드 + RecalculatePathLengths 의 build 라인 + GetDirectionAtDistance 메서드 제거 + DartManager 의 호출 원복.
        private readonly List<Vector3> _segmentDirections = new List<Vector3>();
        private float _totalPathLength;
        // Smooth corners
        private bool _smoothCorners;
        private float _cornerRadius = 1f;

        // Slot system
        private int _slotCount = 160;
        private SlotData[] _slots;
        // V2 아키텍처: 110% buffer 제거. 100% slot capacity 만 사용. deadlock 은 leftmost-only suspend 로 해결.
        private int _deadlockBufferSize; // 항상 0 — 코드 호환 위해 필드 유지
        // -1 = 데드락 모드 아님. 양수 = 해당 holder 가 buffer slot 사용 + 다른 holder pause.
        private int _deadlockHolderId = -1;
        private float _rotationOffset; // current conveyor belt offset in distance units
        private float _rotationSpeed;  // slots per second
        private float _slotSpacing;    // distance between slots on the path
        private int _occupiedCount;
        private int _nextDartId;
        private long _nextPlacedSeq; // monotonically increasing dart placement order (이어하기 정렬용)
        private bool _boardFinished;
        private bool _packingAssistActive;

        /// <summary>
        /// When true, rail rotation is paused (e.g. during booster execution).
        /// </summary>
        public bool IsPausedByBooster { get; set; }

        // Per-dart individual movement system. capacity 256 미리 할당 → resize 비용 0.
        // RailManager 전반에 _slotCount 까지 다트가 갈 수 있으므로 보수적으로 256 (slot 일반 200, 여유).
        private readonly List<DartOnRail> _darts = new List<DartOnRail>(256);

        // FindDart O(N) → O(1) 캐시. Place/Remove 시 동기화.
        private readonly Dictionary<int, DartOnRail> _dartById = new Dictionary<int, DartOnRail>();
        private readonly Dictionary<int, DartOnRail> _clusterHeadByHolder = new Dictionary<int, DartOnRail>();
        private bool _slotOccupancyDirty;
        private float _lastRailAdvanceDiagLogTime;

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

        private struct ProgressReservation
        {
            public float startProgress;
            public int dartCount;
            public long order;
        }

        private readonly Dictionary<int, ProgressReservation> _holderReservations = new Dictionary<int, ProgressReservation>();
        private long _nextReservationOrder;

        #endregion

        #region Properties

        public float TotalPathLength => _totalPathLength;
        public int WaypointCount => _waypoints.Count;
        public bool IsClosedLoop => _isClosedLoop;
        public int SlotCount => _slotCount;
        public int OccupiedCount => _occupiedCount;

        /// <summary>ROLLBACK_QUIT_MOVE_BASED_LIFE_20260619: 이번 레벨에서 다트가 1개라도 배치됐는지(=유저가 '무브'를
        /// 1회라도 했는지). _nextPlacedSeq 는 매 배치마다 증가하고 레벨 로드 시 0 으로 리셋된다. Quit 시 하트 소모 판정용.</summary>
        public bool HasAnyDartPlacedThisLevel => _nextPlacedSeq > 0;

        /// <summary>현재 데드락 모드의 leftmost holder ID. -1 = 정상 모드.</summary>
        public int DeadlockHolderId => _deadlockHolderId;

        /// <summary>데드락 buffer slot 수 (= 10% of slotCount).</summary>
        public int DeadlockBufferSize => _deadlockBufferSize;

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
                float rawCapacity = _totalPathLength / gap;
                int phys = Mathf.FloorToInt(rawCapacity + 0.001f);
                if (_slotSpacing > 0.0001f && Mathf.Abs(gap - _slotSpacing) <= _slotSpacing * 0.001f)
                    phys = _slotCount;
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
        public float DartPhysicalGap
        {
            get
            {
                if (_slotSpacing > 0.0001f) return _slotSpacing;
                if (_totalPathLength > 0.0001f && _slotCount > 0) return _totalPathLength / _slotCount;
                return GameManager.HasInstance ? GameManager.Instance.Board.dartScale * GameManager.Instance.Board.dartSpacingMultiplier : 0.275f;
            }
        }

        /// <summary>
        /// Minimum same-holder spacing used for attack order. It follows balloon cell spacing when
        /// possible, but is capped so the rail can fill to its physical capacity.
        /// </summary>
        public float DartClusterAttackGap
        {
            get
            {
                float minGap = DartPhysicalGap;
                float gap = minGap;
                if (GameManager.HasInstance)
                {
                    float cellSpacing = GameManager.Instance.Board.cellSpacing;
                    if (cellSpacing > 0.0001f)
                        gap = Mathf.Max(gap, cellSpacing);
                }
                float fitCap = GetClusterGapFitCap();
                if (fitCap > 0.0001f)
                    gap = Mathf.Min(gap, Mathf.Max(minGap, fitCap));
                return gap;
            }
        }

        private float GetClusterGapFitCap()
        {
            if (_totalPathLength <= 0.0001f) return 0f;
            int targetCount = Mathf.Max(1, PhysicalCapacity);
            return _totalPathLength / targetCount;
        }

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

            // [Optimization 2026-05-10] dart progress descending Comparison 을 1회만 할당.
            // 이전: UpdateInternal 매 frame 에서 if(null) lazy-init → first-frame 이후엔 무시 가능 비용이지만
            //       hot path 의 null check 와 람다 closure 가 잠재 alloc 위험. Awake 1회 할당이 가장 안전.
            _dartProgressDescending = (a, b) => _darts[b].progress.CompareTo(_darts[a].progress);
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
            var __sw = InGamePerfLogger.StartSection();
            UpdateInternal();
            InGamePerfLogger.EndSection(__sw, "RailManager.Update");
        }

        private void UpdateInternal()
        {
            if (_slots == null || _slotCount == 0) return;
            if (_boardFinished) return;
            if (IsPausedByBooster) return;

            // 회전 속도: 남은 다트 수 기반 + 배치 중 감속 + 유저 가속(홀드/x2토글)
            UpdatePackingAssistState();
            float baseSpeedMult = GetSpeedMultiplier();
            // ROLLBACK_DART_BELT_DT_CLAMP_20260615: 프레임 드랍(큰 deltaTime) 시 belt 가 한 프레임에 과도하게 진행해
            //   다트가 catch-up 예산(MaxLineCatchUpPerHead) 보다 많은 라인을 건너뛰어 '놓침'(DartCatchUpClamped 로그) 발생.
            //   per-frame belt 진행을 ~33ms(30fps) 상당으로 캡 → 라인 스킵을 예산 내로 유지(발사 스캔 프레임율 독립화).
            //   프레임 드랍 중엔 다트가 real-time 으로 약간 느려질 뿐 놓침은 사라짐(미세 슬로우 << 미스).
            //   롤백: dt 를 Time.deltaTime 으로 환원.
            const float MAX_BELT_DELTA_TIME = 1f / 30f;
            float dt = Mathf.Min(Time.deltaTime, MAX_BELT_DELTA_TIME);
            float beltDelta = _rotationSpeed * UserSpeedMultiplier * _slotSpacing * dt * baseSpeedMult * GetPackingAssistSpeedScale();
            _rotationOffset += beltDelta;

            float pathLen = _totalPathLength;
            bool hasWrap = pathLen > 0f;
            if (hasWrap && _rotationOffset >= pathLen) _rotationOffset -= pathLen;

            int dartCount = _darts.Count;
            if (dartCount == 0) return;

            int physicalCapacity = PhysicalCapacity;
            UpdateAlmostThereState(physicalCapacity);   // [Almost There] 클리어 임박 가속 + 메시지
            if (physicalCapacity > 0)                   // [Analytics] 레일 최대 점유율 갱신
            {
                float occ = (float)EffectiveOccupiedCount / physicalCapacity;
                if (occ > _peakOccupancyRatio) _peakOccupancyRatio = Mathf.Min(1f, occ);
                // ROLLBACK_ANALYTICS_NULLFILL_20260625: avg_resource_usage_ratio 계측 — 샘플 합/카운트(peak 옆, 가산만).
                _occupancySum += occ;
                _occupancySampleCount++;
            }
            bool deadlockHolderActive = _deadlockHolderId >= 0 && _activeDeployPoints.Contains(_deadlockHolderId);
            bool deadlockNearFull = _deadlockHolderId >= 0 && dartCount >= Mathf.Max(0, physicalCapacity - DeadlockBeltAdvanceEmptySlots());
            bool shouldAdvanceAsFullBelt =
                _frozenDartInfos.Count == 0
                && (dartCount >= physicalCapacity
                    || deadlockHolderActive
                    || deadlockNearFull);

            if (LOG_RAIL_ADVANCE_DIAG
                && _activeDeployPoints.Count > 0
                && dartCount >= Mathf.Max(0, physicalCapacity - 5)
                && Time.unscaledTime - _lastRailAdvanceDiagLogTime > RAIL_ADVANCE_DIAG_INTERVAL)
            {
                _lastRailAdvanceDiagLogTime = Time.unscaledTime;
                Debug.Log($"[RailAdvanceDiag] {GetAdvanceModeDebugInfo()} beltDelta={beltDelta:F4} " +
                          $"baseSpeedMult={baseSpeedMult:F2} packingAssist={GetPackingAssistSpeedScale():F2} " +
                          $"rotationOffset={_rotationOffset:F3}");
            }

            if (shouldAdvanceAsFullBelt)
            {
                AdvanceAllDarts(beltDelta, pathLen);
                MarkSlotOccupancyDirty();
                return;
            }

            // Packing physics — frozen dart 가 멈춰있을 때 trailing cluster 가 catch-up.
            // (2) 색상 무관 cluster 끼리 만나면 앞 cluster 뒤로 붙음 (slotSpacing 간격 유지).
            // (3) consecutive deploy 시 cluster 내부 spacing uniform (packing 이 trailing dart catch-up).
            // Fire 시 gap 자동 close 안 함 — fire 로 head 가 사라지면 trailing 은 이전 위치 유지하다 belt 회전으로 도달.
            //     ※ 단 cluster 내부 packing 이 작동해서 trailing 은 head 위치까지 catch-up 가능.
            //       사용자 요구: fire empty 가 deploy point 도달해야 placement.
            //       이는 IsSlotEmpty / GetSlotAtPathDistance 로 보장 (slot 점유 체크).
            // ── 2026-05-08: V2 freeze 폐기. dart.isFrozen=true 가 dart.progress 절대 정지 → fire 검사 시
            //   매칭 풍선 위치 도달 못함 → stuck. 사용자 의도 5 (capacity 도달 시 belt 회전 → fire 가능 dart 회전 따라 외곽 도달)
            //   와 충돌. 대신 packing physics 의 ahead obstacle 검사에 deploy block 추가 (아래 ── A 영역).
            //   packing 자연 정지 → cluster 정지 위치까지 belt 진행 따라 dart.progress 진행 → fire 가능.
            // V2UpdateFreezeOnDeployBlock();

            float physGap = DartPhysicalGap;
            // ROLLBACK_DART_CELL_SPACING_CLUSTER_GAP:
            // Same-holder darts need one balloon-cell spacing so a promoted head maps to the next
            // scan line, while different holders can still pack on the rail's physical slot gap.
            float clusterAttackGap = DartClusterAttackGap;
            _sortedDartIndices.Clear();
            for (int i = 0; i < dartCount; i++) _sortedDartIndices.Add(i);
            // [Optimization 2026-05-10] OnSingletonAwake 에서 1회 init 으로 이동. 매 frame null check 제거.
            // 롤백: 아래 두 줄 주석 해제 + OnSingletonAwake 의 _dartProgressDescending = ... 라인 제거.
            // if (_dartProgressDescending == null)
            //     _dartProgressDescending = (a, b) => _darts[b].progress.CompareTo(_darts[a].progress);
            _sortedDartIndices.Sort(_dartProgressDescending);

            // Wrap 감지 — full rail 또는 cluster 가 path 0/pathLen 경계 wrap 시 sort[0] 이 진짜 head 가 아님.
            // 가장 큰 gap 뒤가 진짜 head. iteration 시작점으로 사용 → head 가 free advance.
            int headPosInSorted = 0;
            if (hasWrap && dartCount > 1)
            {
                float highest = _darts[_sortedDartIndices[0]].progress;
                float lowest = _darts[_sortedDartIndices[dartCount - 1]].progress;
                if ((highest - lowest) > pathLen * 0.5f)
                {
                    float maxGap = -1f;
                    for (int p = 0; p < dartCount; p++)
                    {
                        int prevPos = p == 0 ? dartCount - 1 : p - 1;
                        float curProg = _darts[_sortedDartIndices[p]].progress;
                        float prevProg = _darts[_sortedDartIndices[prevPos]].progress;
                        float gap = prevProg - curProg;
                        if (gap < 0f) gap += pathLen;
                        if (gap > maxGap)
                        {
                            maxGap = gap;
                            headPosInSorted = p;
                        }
                    }
                }
            }

            // ROLLBACK_DEPLOY_BLOCK_HOIST_20260616: 활성 deploy 의 blockProgress 를 다트 루프 전에 1회 채움.
            //   (GetDeployBlockProgress 는 순수함수라 다트마다 동일 → 루프 내 재열거/재계산 제거. 동작 불변.)
            _deployBlockHolders.Clear();
            _deployBlockProgress.Clear();
            if (DEPLOY_POINT_BLOCKS_FLOW && _activeDeployPoints.Count > 0)
            {
                var depPre = _deployPoints.GetEnumerator();
                try
                {
                    while (depPre.MoveNext())
                    {
                        int hId = depPre.Current.Key;
                        if (!_activeDeployPoints.Contains(hId)) continue;
                        _deployBlockHolders.Add(hId);
                        _deployBlockProgress.Add(GetDeployBlockProgress(depPre.Current.Value));
                    }
                }
                finally { depPre.Dispose(); }
            }

            for (int offset = 0; offset < dartCount; offset++)
            {
                int p = headPosInSorted + offset;
                if (p >= dartCount) p -= dartCount;
                int idx = _sortedDartIndices[p];
                DartOnRail dart = _darts[idx];
                if (dart.isFrozen) continue;

                // Ahead dart 까지 거리 + 같은 cluster 여부.
                float closest = float.MaxValue;
                float closestRequiredGap = physGap;
                if (dartCount > 1)
                {
                    int aheadPos = p - 1;
                    if (aheadPos < 0) aheadPos = dartCount - 1;
                    int aheadIdx = _sortedDartIndices[aheadPos];
                    if (aheadIdx != idx)
                    {
                        float d = _darts[aheadIdx].progress - dart.progress;
                        if (hasWrap && d < 0f) d += pathLen;
                        if (d > 0.001f)
                        {
                            closest = d;
                            bool sameCluster = (_darts[aheadIdx].holderId == dart.holderId);
                            closestRequiredGap = sameCluster ? clusterAttackGap : physGap;
                        }
                    }
                }

                // ── A. 신규 (2026-05-08): deploy block 도 ahead obstacle ──
                // V2UpdateFreezeOnDeployBlock 폐기 후 cluster freeze 의도 2 효과를 packing 으로 대체.
                // self-skip: 자기 deploy 는 obstacle X (자기 cluster 가 자기 deploy point 부터 자라남).
                // same-color skip 없음 (사용자 명시: same-color 도 packing 정지, spacing physGap).
                // 결과: cluster head 가 다른 holder 의 deploy block 직전 packing 정지 (stopDist=0).
                // belt 회전 시 dart.progress 진행 (frozen 아님) → fire 검사 시 매칭 풍선 위치 도달 가능.
                // [DEPLOY_BLOCK_HOIST] 위에서 precompute 한 버퍼만 순회 (값/로직 동일, 재열거·재계산 제거).
                for (int dpi = 0; dpi < _deployBlockHolders.Count; dpi++)
                {
                    if (_deployBlockHolders[dpi] == dart.holderId) continue; // self skip
                    float distToBlock = _deployBlockProgress[dpi] - dart.progress;
                    if (hasWrap && distToBlock < 0f) distToBlock += pathLen;
                    if (distToBlock > 0.001f && distToBlock < closest)
                    {
                        closest = distToBlock;
                        closestRequiredGap = physGap;
                    }
                }

                float maxAdvance = beltDelta;
                if (closest < float.MaxValue)
                {
                    float stopDist = closest - closestRequiredGap;
                    if (stopDist < 0f) stopDist = 0f;
                    // 단순화 — 모든 dart 가 belt 속도 normal advance, ahead 와 physGap 미만 충돌 시만 한도.
                    // catch-up 제거 (corner 의 boundary case 에서 maxAdvance=0 trap 방지).
                    if (stopDist < maxAdvance) maxAdvance = stopDist;
                }

                if (maxAdvance > 0.001f)
                {
                    float newProg = dart.progress + maxAdvance;
                    if (hasWrap && newProg >= pathLen) newProg -= pathLen;
                    dart.progress = newProg;
                }
            }

            MarkSlotOccupancyDirty();
        }

        // Sort helpers for front-to-back dart iteration (packing physics 시 선두부터 처리)
        private readonly List<int> _sortedDartIndices = new List<int>(256);
        // ROLLBACK_DEPLOY_BLOCK_HOIST_20260616: deploy block progress 는 다트 독립적(GetDeployBlockProgress 순수함수)
        //   → 프레임당 1회 precompute 해 다트 루프의 O(darts×deploys) 재열거+재계산을 O(deploys)+O(darts×deploys 비교)로.
        private readonly List<int> _deployBlockHolders = new List<int>(8);
        private readonly List<float> _deployBlockProgress = new List<float>(8);
        // ★ 게임 디자인 확정 (2026-07-07, 사용자 결정): 이 동작은 게임의 킬링 포인트 — 절대 끄지 말 것. ★
        //   동작: 활성 배포점 앞(physGap 여유)에서 '다른 홀더'의 다트가 정지 — 배포 중인 홀더가 자기
        //   다트를 연속 덩어리로 밀어 넣을 공간을 보장하고, 배포가 끝나면 멈췄던 행렬이 다시 흐른다.
        //   ([배포1][다트][다트][배포2][다트] 상황에서 배포1 행렬이 배포2 앞에서 잠깐 멈추는 그 연출.)
        //   이력: ROLLBACK_DEPLOY_NONBLOCKING_20260707 로 임계치 뚝뚝 끊김 완화를 위해 false(무정지 흐름,
        //   빈칸 주입)로 바꿨다가, "킬링 포인트가 사라진다" 는 사용자 판단으로 당일 원복(true).
        //   끊김 완화는 데드락 진입 게이트(ROLLBACK_DEADLOCK_ENTRY_SIGNALS/STALL_ONLY_ENTRY_20260707)가 담당.
        //   static readonly (const 면 gated if 에 CS0162 unreachable 경고).
        private static readonly bool DEPLOY_POINT_BLOCKS_FLOW = true;
        private System.Comparison<int> _dartProgressDescending;

        private void AdvanceAllDarts(float distance, float pathLen)
        {
            if (distance <= 0.001f || pathLen <= 0f) return;

            for (int i = 0; i < _darts.Count; i++)
            {
                DartOnRail dart = _darts[i];
                if (dart == null || dart.isFrozen) continue;

                float newProgress = dart.progress + distance;
                if (newProgress >= pathLen) newProgress -= pathLen;
                dart.progress = newProgress;
            }
        }

        // [Almost There] 클리어 임박 가속. 명세상 "레일 ×2 가속"이나, 과거 상시 ×2 가 deploy 동기 깨짐(cluster
        // spacing 벌어짐)을 유발했으므로 실제 배율은 1.8 로 완화(UI/명세 표기는 ×2). 트리거 = 총 잔여 다트 < 레일 capacity.
        private const float ALMOST_THERE_SPEED_MULT = 1.8f;
        private bool _almostThere;            // 현재 클리어 임박 상태(가속 on)
        private bool _almostThereToastShown;  // "Almost There!" 토스트 1회 발사 가드

        // [Analytics] 레벨 동안의 레일 최대 점유율(0~1). AnalyticsLevelTracker 의 peak_resource_usage_ratio wiring 용
        // (해당 파일 TODO: "RailManager 측 peak 점유율 노출 시 wiring"). InitializeSlots 에서 0 으로 리셋.
        private float _peakOccupancyRatio;
        /// <summary>이번 레벨 동안 기록된 레일 최대 점유율(EffectiveOccupiedCount / PhysicalCapacity, 0~1).</summary>
        public float PeakOccupancyRatio => _peakOccupancyRatio;

        // ROLLBACK_ANALYTICS_NULLFILL_20260625: avg_resource_usage_ratio — 점유율 샘플 평균.
        private double _occupancySum;
        private int _occupancySampleCount;
        /// <summary>이번 레벨 동안의 레일 평균 점유율(0~1). 다트 진행 틱마다 샘플.</summary>
        public float AverageOccupancyRatio => _occupancySampleCount > 0 ? (float)(_occupancySum / _occupancySampleCount) : 0f;

        /// <summary>
        /// 점유율 기반 속도 배율. 클리어 임박(총 잔여 다트 &lt; 레일 capacity) 시 1.8배, 그 외 1배.
        /// (2026-05-08 상시 ×2 가속은 deploy 동기 문제로 폐기 — 임박 구간에서만 1.8배 재도입.)
        /// </summary>
        public float GetOccupancySpeedMultiplier()
        {
            return _almostThere ? ALMOST_THERE_SPEED_MULT : 1f;
        }

        // ROLLBACK_CLEAR_IMMINENT_BOOSTER_NOOP_20260622:
        // Public mirror of the Almost There trigger so HUD booster taps can no-op at the
        // exact same moment as rail acceleration/toast.
        public bool IsClearImminentForBoosterLock()
        {
            return IsAlmostThereImminent(PhysicalCapacity);
        }

        /// <summary>홀더 미배포 magazine + 레일 위 다트의 합 = 레벨에 남은 총 다트 수.</summary>
        private int GetTotalRemainingDarts()
        {
            int total = _darts.Count;
            if (HolderManager.HasInstance)
            {
                var holders = HolderManager.Instance.GetHolders();
                if (holders != null)
                    for (int i = 0; i < holders.Length; i++)
                        if (!holders[i].isConsumed && holders[i].magazineCount > 0)
                            total += holders[i].magazineCount;
            }
            return total;
        }

        /// <summary>매 프레임 클리어 임박 상태 갱신 + 진입 순간(rising edge) "Almost There!" 토스트 1회.</summary>
        private bool IsAlmostThereImminent(int capacity)
        {
            return capacity > 0 && _darts.Count > 0 && GetTotalRemainingDarts() < capacity;
        }

        private void UpdateAlmostThereState(int capacity)
        {
            bool imminent = IsAlmostThereImminent(capacity);
            _almostThere = imminent;
            if (imminent)
            {
                if (!_almostThereToastShown)
                {
                    _almostThereToastShown = true;
                    // ROLLBACK_CLEAR_IMMINENT_IRONWALL_REMOVE_20260630:
                    // Use the exact Almost There rising edge for Iron Wall auto-removal.
                    if (BalloonController.HasInstance)
                        BalloonController.Instance.RemoveIronWallsForClearImminent();
                    ShowAlmostThereMessage();
                }
            }
            else
            {
                // 이어하기 등으로 다트가 다시 늘어 임박이 풀리면 재무장(다음 임박 때 재노출).
                _almostThereToastShown = false;
            }
        }

        private void ShowAlmostThereMessage()
        {
            if (!UIManager.HasInstance) return;
            Transform parent = UIManager.Instance.PopupTr ?? UIManager.Instance.UiTr;
            if (parent == null) return;
            string msg = LocalizationService.Get("ingame_almost_there");
            if (msg == "ingame_almost_there") msg = "Almost There!";   // CSV 미등록 시 폴백
            TxtToast.Spawn(parent, msg, Vector2.zero);
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

        public float GetBeltDistancePerSecond()
        {
            return _rotationSpeed * UserSpeedMultiplier * _slotSpacing * GetSpeedMultiplier() * GetPackingAssistSpeedScale();
        }

        /// <summary>활성 deploy point. holderId → progress on path.</summary>
        private float GetPackingAssistSpeedScale()
        {
            // 사용자 요구 (2026-05-08): belt 속도 일정 유지 — packing assist 의 0.4x 감속 제거.
            // 이전: nearly full 시 0.4x 감속 → 단일 cluster (= sparse) 와 다중 cluster (= dense) 속도 mismatch.
            return 1f;
        }

        private void UpdatePackingAssistState()
        {
            if (_slotCount <= 0 || _totalPathLength <= 0f || _darts.Count < 2 || _activeDeployPoints.Count == 0)
            {
                _packingAssistActive = false;
                return;
            }

            bool nearlyFull = (_occupiedCount + _frozenDartInfos.Count) >= PhysicalCapacity - 2;
            if (!nearlyFull)
            {
                _packingAssistActive = false;
                return;
            }

            float largestGap = CalculateLargestProgressGap();
            float gapMultiplier = _packingAssistActive ? PACKING_ASSIST_GAP_STOP : PACKING_ASSIST_GAP_START;
            _packingAssistActive = largestGap > DartPhysicalGap * gapMultiplier;
        }

        private float CalculateLargestProgressGap()
        {
            if (_totalPathLength <= 0f || _darts.Count < 2) return 0f;

            _gapSortBuffer.Clear();
            for (int i = 0; i < _darts.Count; i++)
            {
                DartOnRail dart = _darts[i];
                if (dart == null || dart.isFrozen) continue;
                _gapSortBuffer.Add(dart.progress);
            }

            if (_gapSortBuffer.Count < 2) return 0f;

            _gapSortBuffer.Sort();
            float largestGap = 0f;
            for (int i = 0; i < _gapSortBuffer.Count; i++)
            {
                float current = _gapSortBuffer[i];
                float next = i + 1 < _gapSortBuffer.Count
                    ? _gapSortBuffer[i + 1]
                    : _gapSortBuffer[0] + _totalPathLength;
                float gap = next - current;
                if (gap > largestGap) largestGap = gap;
            }

            return largestGap;
        }

        private readonly Dictionary<int, float> _deployPoints = new Dictionary<int, float>();
        /// <summary>배치 시작된 deploy point (첫 다트 투입 후 → 장애물 활성화).</summary>
        private readonly HashSet<int> _activeDeployPoints = new HashSet<int>();

        /// <summary>deploy point 등록 (대기 상태 — 아직 장애물 아님).</summary>
        public void RegisterDeployPoint(int holderId, float progress)
        {
            NormalizeProgress(ref progress);
            _deployPoints[holderId] = progress;
            // 아직 _activeDeployPoints에 추가하지 않음 → 빈틈 기다림
        }

        /// <summary>deploy point 활성화 (첫 다트 배치 후 → 장애물로 전환).</summary>
        public void ActivateDeployPoint(int holderId)
        {
            // ROLLBACK_DEPLOY_CROSSED_DART_REVERT_20260618: ①(추월 가드) 되돌림 — 회귀(deadlock freeze) 유발로 제거.
            _activeDeployPoints.Add(holderId);
        }

        public void DeactivateDeployPoint(int holderId)
        {
            _activeDeployPoints.Remove(holderId);
        }

        /// <summary>deploy point 해제. 다트는 다음 프레임부터 자연스럽게 이동 재개.</summary>
        public void UnregisterDeployPoint(int holderId)
        {
            _deployPoints.Remove(holderId);
            _activeDeployPoints.Remove(holderId);

            if (_deployPoints.Count == 0)
            {
                UnfreezeAllDarts();
                if (_frozenDartInfos.Count > 0)
                    UnfreezeAndReinsertAll();
            }
        }

        /// <summary>데드락 trigger 조건 — rail 점유량이 capacity-1 이상 (V2 아키텍처: buffer 없음).
        /// 100% 가 아닌 capacity-1 임계 이유: 199/200 같은 in-cluster 빈 slot stuck 케이스에서도 deadlock detect.
        /// (cluster 가 freeze 되거나 다른 cluster 가 빈 slot 막아서 belt 회전으로 채워지지 않는 상황.)</summary>
        public bool IsRailNearFull()
        {
            return (_occupiedCount + _frozenDartInfos.Count) >= _slotCount - 1;
        }

        /// <summary>현재 active deploy point 개수.</summary>
        public int GetActiveDeployPointCount() => _activeDeployPoints.Count;

        /// <summary>
        /// 데드락 강제-벨트회전 빈칸 윈도우 N (= 회전 개입 임계 capacity-N).
        /// ㅡ자(단면, GetRailSideCount=1) 레일은 path 가 짧은데 deploy point 는 열 수만큼(최대 5) 밀집 →
        /// deploy-point 밀도 최고 → 빈칸이 구간 사이에 갇혀 순환 못 함.
        /// active deploy point 가 많을수록(=ㅡ 다열) 윈도우를 키워, 잠기기 전에 강제회전이 개입하도록 함.
        /// floor=DEADLOCK_BELT_ADVANCE_EMPTY_SLOTS(5, 기존), deploys+2 스케일, ceiling=8.
        /// </summary>
        private int DeadlockBeltAdvanceEmptySlots()
            => Mathf.Clamp(_activeDeployPoints.Count + 2, DEADLOCK_BELT_ADVANCE_EMPTY_SLOTS, 8);

        /// <summary>ROLLBACK_SUPPLY_QUEUE_CUTOFF_NEARFULL_20260707: near-full 밴드 빈칸 수의 공개 노출.
        /// BoardStateManager 의 supply 판정이 '큐 공급 인정 컷오프'로 사용 — 데드락 강제회전이 개입하는
        /// 임계(capacity-N)와 실패 판정의 '사실상 만석' 기준을 단일 소스로 정합시킨다.</summary>
        public int NearFullBandEmptySlots => DeadlockBeltAdvanceEmptySlots();

        public bool ShouldUseFixedDeployPlacement(int holderId)
        {
            int physicalCapacity = PhysicalCapacity;
            int emptySlotsToPhysical = Mathf.Max(0, physicalCapacity - _darts.Count);
            bool holderIsDeadlock = _deadlockHolderId == holderId;
            bool holderIsActiveDeployPoint = _activeDeployPoints.Contains(holderId);
            bool fullOrRecovery = _darts.Count >= physicalCapacity || emptySlotsToPhysical <= DeadlockBeltAdvanceEmptySlots();

            return holderIsDeadlock || (holderIsActiveDeployPoint && fullOrRecovery);
        }

        // ROLLBACK_FAIL_ON_FORCE_ADVANCE_NO_MATCH:
        // BoardStateManager uses the same condition as UpdateInternal's forced full-belt advance.
        // Remove this helper and the BoardStateManager branch if forced-rotation should remain only
        // a movement recovery mode, not a fail-condition contributor.
        public bool IsForceFullBeltAdvanceActive()
        {
            int dartCount = _darts.Count;
            if (dartCount == 0 || _frozenDartInfos.Count > 0) return false;

            int physicalCapacity = PhysicalCapacity;
            bool capacityFull = dartCount >= physicalCapacity;
            bool deadlockHolderActive = _deadlockHolderId >= 0 && _activeDeployPoints.Contains(_deadlockHolderId);
            bool deadlockNearFull =
                _deadlockHolderId >= 0
                && dartCount >= Mathf.Max(0, physicalCapacity - DeadlockBeltAdvanceEmptySlots());

            return capacityFull || deadlockHolderActive || deadlockNearFull;
        }

        public string GetAdvanceModeDebugInfo()
        {
            int dartCount = _darts.Count;
            int physicalCapacity = PhysicalCapacity;
            int deadlockAdvanceThreshold = Mathf.Max(0, physicalCapacity - DeadlockBeltAdvanceEmptySlots());
            bool capacityFull = dartCount >= physicalCapacity;
            bool deadlockNearFull = _deadlockHolderId >= 0 && dartCount >= deadlockAdvanceThreshold;
            bool deadlockHolderActive = _deadlockHolderId >= 0 && _activeDeployPoints.Contains(_deadlockHolderId);
            bool shouldAdvanceAsFullBelt = _frozenDartInfos.Count == 0 && (capacityFull || deadlockHolderActive || deadlockNearFull);
            int emptySlotsToPhysical = Mathf.Max(0, physicalCapacity - dartCount);

            return $"rail={dartCount}/{physicalCapacity} slots={_slotCount} emptyToPhys={emptySlotsToPhysical} " +
                   $"shouldFullAdvance={shouldAdvanceAsFullBelt} capacityFull={capacityFull} " +
                   $"deadlockNearFull={deadlockNearFull} deadlockActive={deadlockHolderActive} dlh={_deadlockHolderId} threshold={deadlockAdvanceThreshold} " +
                   $"activeDeploys={_activeDeployPoints.Count} frozen={_frozenDartInfos.Count} dirty={_slotOccupancyDirty}";
        }

        /// <summary>ROLLBACK_RAIL_FREEZE_DIAG_20260622: hard-freeze(전면정지) 진단용 — UpdateInternal early-return 류
        ///   원인 후보를 한 줄로 노출. IsPausedByBooster(부스터 await 중 영구정지 1순위 용의자) / _boardFinished /
        ///   deadlock mode / active·suspended deploy point 목록. 동작 변경 없음(읽기 전용).
        ///   롤백: 이 메서드 + BoardStateManager 의 freeze 워치독 삭제.</summary>
        public string GetFreezeDiagnostics()
        {
            return $"pausedByBooster={IsPausedByBooster} boardFinished={_boardFinished} " +
                   $"rotationOffset={_rotationOffset:F3} occupied={_occupiedCount} " +
                   $"activeDeployIds=[{string.Join(",", _activeDeployPoints)}] " +
                   $"suspendedDeployIds=[{string.Join(",", _deadlockSuspendedDeployPoints)}] " +
                   $"{GetAdvanceModeDebugInfo()}";
        }

        /// <summary>데드락 모드 진입 — leftmost holder 만 buffer slot 사용 가능, 다른 holder pause.
        /// 호출자: PlaceDart 가 capacity 초과로 실패한 시점에서 _activeDeployPoints 의 leftmost 선택해 호출.
        /// 이미 진입 상태면 no-op.
        ///
        /// 옵션 A: leftmost 외 다른 deploy point 를 _activeDeployPoints 에서 제거 → packing physics 의
        /// obstacle 아니게 됨 → 다른 cluster 들 belt 회전 따라 자유롭게 흐름 → fire 매치 기회 발생 →
        /// slot 해소. paused holder 들은 ExitDeadlockMode 후 placement 재개 시 자동으로 re-activate.</summary>
        public bool EnterDeadlockMode(int leftmostHolderId)
        {
            if (_deadlockHolderId >= 0) return false; // 이미 진입
            if (leftmostHolderId < 0) return false;
            _deadlockHolderId = leftmostHolderId;

            // leftmost 외 active deploy point 모두 obstacle 해제 — cluster 자연 흐름.
            // 사본 iterate (Remove 중 collection 변경 방지).
            _deadlockSuspendedDeployPoints.Clear();
            foreach (int hid in _activeDeployPoints)
            {
                if (hid != leftmostHolderId) _deadlockSuspendedDeployPoints.Add(hid);
            }
            for (int i = 0; i < _deadlockSuspendedDeployPoints.Count; i++)
            {
                _activeDeployPoints.Remove(_deadlockSuspendedDeployPoints[i]);
            }

            Debug.Log($"[Deadlock] ENTER — leftmost holder {leftmostHolderId}. " +
                      $"Suspended deploy points: [{string.Join(", ", _deadlockSuspendedDeployPoints)}]. " +
                      $"Occupancy: {_occupiedCount}/{_slotCount} (+frozen {_frozenDartInfos.Count})");

            EventBus.Publish(new OnDeadlockEntered { holderId = leftmostHolderId });
            return true;
        }

        // EnterDeadlockMode 에서 obstacle 해제된 holder ID 들. ExitDeadlockMode 에서 정보로만 보유 (자동 re-activate 는 holder 자신이 다음 placement 성공 시 호출).
        private readonly List<int> _deadlockSuspendedDeployPoints = new List<int>();

        /// <summary>데드락 모드 해제 — 트리거 holder 의 magazine 종료 시 호출.
        /// 다른 holder 들 deploy 재개.</summary>
        public void ExitDeadlockMode()
        {
            if (_deadlockHolderId < 0) return;
            int prevHolder = _deadlockHolderId;
            _deadlockHolderId = -1;

            // ROLLBACK_DEADLOCK_EXIT_REACTIVATE_20260615: START
            // 기존 주석은 suspended holder 가 "다음 placement 성공 시 자동 re-activate" 된다고 했으나,
            // near-full 로 막힌 holder 는 placement 에 도달하지 못해 _activeDeployPoints 에서 빠진 채
            // stale obstacle/비발사 blocker 로 잔존 → survivor 정지 가능. 여기서 명시적으로 복귀시킨다.
            // 안전: _activeDeployPoints 는 HashSet(Add 멱등, 이중 obstacle 불가). 트리거 holder 와
            //       이미 UnregisterDeployPoint 된 holder(_deployPoints 에서 제거됨)는 제외.
            // 롤백: 아래 for 블록 전체 삭제(이 마커 START~END 사이) 하면 종전 동작 복원.
            for (int i = 0; i < _deadlockSuspendedDeployPoints.Count; i++)
            {
                int hid = _deadlockSuspendedDeployPoints[i];
                if (hid == prevHolder) continue;          // 트리거는 이미 처리됨
                if (_deployPoints.ContainsKey(hid))        // 그사이 unregister 된 死 포인트 제외
                    _activeDeployPoints.Add(hid);          // HashSet → 멱등
            }
            _deadlockSuspendedDeployPoints.Clear();
            // ROLLBACK_DEADLOCK_EXIT_REACTIVATE_20260615: END

            Debug.Log($"[Deadlock] EXIT — trigger holder {prevHolder} done. " +
                      $"Occupancy: {_occupiedCount}/{_slotCount}");
            EventBus.Publish(new OnDeadlockExited { holderId = prevHolder });
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
        // ─── 사용자 요구: slot index 기반 위치 통일 ───
        /// <summary>Slot index 의 현재 world position. _rotationOffset 자동 적용.
        /// 모든 다트 위치 계산은 이걸로 통일 — 다트 사이 간격이 항상 slotSpacing 의 정수배 보장.</summary>
        public Vector3 GetPositionAtSlot(int slotIndex)
        {
            if (_slotCount <= 0 || _slotSpacing <= 0.0001f) return Vector3.zero;
            slotIndex = ((slotIndex % _slotCount) + _slotCount) % _slotCount;
            float distance = slotIndex * _slotSpacing + _rotationOffset;
            return GetPositionAtDistance(distance);
        }

        /// <summary>다트의 현재 world position. progress 기반 (LEGACY) — 곡선/packing 자연.
        /// slotIndex 는 메타데이터 (간격 추적용)만 사용. 시각은 packing physics 결과 progress 따름.</summary>
        public Vector3 GetDartCurrentPosition(DartOnRail dart)
        {
            if (dart == null) return Vector3.zero;
            return GetPositionAtDistance(dart.progress);
        }

        public void GetDartCurrentPose(DartOnRail dart, out Vector3 position, out Vector3 tangent, out Vector3 firingDirection)
        {
            if (dart == null)
            {
                position = Vector3.zero;
                tangent = Vector3.forward;
                firingDirection = Vector3.forward;
                return;
            }

            GetPositionAndDirectionAtDistance(dart.progress, out position, out tangent);
            firingDirection = GetFiringDirectionFromMoveDir(tangent);
        }

        // ROLLBACK_DART_POSE_LOOKUP_OPT:
        // DartManager needs both position and direction for every visible dart. Doing separate
        // GetPositionAtDistance + GetDirectionAtDistance calls performs two binary searches per dart.
        // This combined lookup keeps the same interpolation but resolves the segment once.
        public void GetPositionAndDirectionAtDistance(float distance, out Vector3 position, out Vector3 direction)
        {
            var path = _smoothedPath;
            int pathCount = path.Count;
            if (pathCount == 0)
            {
                position = Vector3.zero;
                direction = Vector3.forward;
                return;
            }
            if (pathCount == 1 || _totalPathLength <= 0f)
            {
                position = path[0];
                direction = _segmentDirections.Count > 0 ? _segmentDirections[0] : Vector3.forward;
                return;
            }

            distance = ((distance % _totalPathLength) + _totalPathLength) % _totalPathLength;

            int lo = 0;
            int hi = _cumulativeLengths.Count - 1;
            while (lo < hi)
            {
                int mid = (lo + hi) >> 1;
                if (_cumulativeLengths[mid] < distance)
                    lo = mid + 1;
                else
                    hi = mid;
            }

            int i = lo;
            int segCount = _segmentLengths.Count;
            if (i < segCount)
            {
                float segStart = (i > 0) ? _cumulativeLengths[i - 1] : 0f;
                float segLength = _segmentLengths[i];
                if (segLength <= 0f)
                {
                    position = path[i];
                }
                else
                {
                    float localT = (distance - segStart) / segLength;
                    int nextIndex = (i + 1) % pathCount;
                    position = Vector3.Lerp(path[i], path[nextIndex], localT);
                }
            }
            else
            {
                i = pathCount - 1;
                position = path[i];
            }

            int dirIndex = i;
            int dirCount = _segmentDirections.Count;
            if (dirIndex >= dirCount) dirIndex = dirCount - 1;
            direction = dirIndex >= 0 ? _segmentDirections[dirIndex] : Vector3.forward;
        }

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

        /// <summary>
        /// [Optimization 2026-05-10] distance 기반 forward direction. 사전 계산된 _segmentDirections 사용.
        /// 기존 GetDirectionAtNormalized 의 GetPositionAtDistance × 2 + sqrt 패턴 제거 — 이진 탐색 1회 + 배열 lookup.
        /// path smoothed 라 segment dir 자체가 부드럽게 변함 → 시각 차이 무시.
        /// </summary>
        public Vector3 GetDirectionAtDistance(float distance)
        {
            int segCount = _segmentDirections.Count;
            if (segCount == 0) return Vector3.forward;
            if (_totalPathLength <= 0f) return _segmentDirections[0];

            // wrap (GetPositionAtDistance 와 동일 정책)
            distance = ((distance % _totalPathLength) + _totalPathLength) % _totalPathLength;

            // 이진 탐색 — _cumulativeLengths 와 동일 패턴
            int lo = 0, hi = _cumulativeLengths.Count - 1;
            while (lo < hi)
            {
                int mid = (lo + hi) >> 1;
                if (_cumulativeLengths[mid] < distance)
                    lo = mid + 1;
                else
                    hi = mid;
            }
            int idx = lo;
            if (idx >= segCount) idx = segCount - 1;
            return _segmentDirections[idx];
        }

        #endregion

        #region Public Methods — Slot System

        /// <summary>
        /// Initializes slot array for a new level.
        /// </summary>
        public void InitializeSlots(int slotCount)
        {
            _slotCount = Mathf.Max(1, slotCount);
            // 데드락 buffer = 10% of slotCount (최소 1). _slots 배열은 normal + buffer 크기.
            _deadlockBufferSize = 0; // V2 아키텍처: buffer 없음
            _slots = new SlotData[_slotCount];
            _deadlockHolderId = -1;
            _lastPlacementUnscaledTime = 0f; // ROLLBACK_SUPPLY_ACTIONABLE_20260707: 새 보드 리셋
            // ROLLBACK_BOOSTER_PAUSE_RESET_20260618: 새 보드 시작 시 부스터 일시정지 강제 해제.
            //   IsPausedByBooster 는 ResumeRail(부스터 완료/취소) 에서만 false 가 되는데, 부스터 arm 상태에서
            //   Retry/자동실패/광고중단/레벨점프로 보드가 리셋되면 true 인 채 남아 UpdateInternal(375) 이 매 프레임
            //   return → 새 레일 영구정지(soft-lock). 보드 init 에서 무조건 false 로 끊는다. (QA HIGH)
            //   롤백: 이 줄 제거.
            IsPausedByBooster = false;
            _rotationOffset = 0f;
            _occupiedCount = 0;
            _nextDartId = 0;
            _nextPlacedSeq = 0;
            _boardFinished = false;
            _continueSnapColor = -1;
            _continueSnapCount = 0;
            _almostThere = false;            // [Almost There] 새 레벨 시 가속/토스트 상태 리셋
            _almostThereToastShown = false;
            _peakOccupancyRatio = 0f;        // [Analytics] 피크 점유율 리셋
            _occupancySum = 0; _occupancySampleCount = 0; // ROLLBACK_ANALYTICS_NULLFILL_20260625: 평균 점유율 리셋
            _darts.Clear();
            _dartById.Clear();
            _clusterHeadByHolder.Clear();
            // ROLLBACK_RAIL_INIT_RESIDUAL_RESET_20260706: Retry(SetRailLayout→InitializeSlots) 경로는 ResetAll 과
            //   달리 아래 필드들을 리셋하지 않아 이전 판 상태가 새 판으로 이월됐다:
            //   - _frozenDartInfos: EffectiveOccupiedCount(=_occupiedCount+_frozenDartInfos.Count) 를 유령 점유로
            //     부풀려 새 판이 시작부터 만석/위급 오판(조기 fail·벨트 오동작·데드락).
            //   - _deployPoints/_activeDeployPoints/_holderReservations: fail/clear 핸들러에만 정리가 걸려 있어
            //     이벤트 없이 재시작하는 경로(무브1+ Quit→fail02→Retry)에서 팬텀 배포점/예약이 배치·전진을 차단.
            //   - _activeDeploySlot/_activeDeployHolderId: stale holderId 가 신규 보드의 동결 로직을 오차단.
            //   ResetAll(3034) 과 동일 수준으로 정리한다(이중 방어). 롤백: 이 블록 제거.
            _deployPoints.Clear();
            _activeDeployPoints.Clear();
            _holderReservations.Clear();
            _frozenDartInfos.Clear();
            _nextReservationOrder = 0;
            _activeDeploySlot = -1;
            _activeDeployHolderId = -1;

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
            Vector3 dir = GetSlotDirection(slotIndex);
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
            EnsureSlotOccupancySynced();
            if (_slots == null || slotIndex < 0 || slotIndex >= _slotCount)
                return new SlotData { dartColor = -1, holderId = -1, dartId = -1 };
            return _slots[slotIndex];
        }

        /// <summary>
        /// Returns true if the slot is empty (no dart).
        /// </summary>
        public bool IsSlotEmpty(int slotIndex)
        {
            EnsureSlotOccupancySynced();
            if (_slots == null || slotIndex < 0 || slotIndex >= _slotCount) return false;
            return _slots[slotIndex].dartColor < 0;
        }

        /// <summary>
        /// Places a dart on an empty slot. Returns the assigned dart ID, or -1 if slot is occupied.
        /// 데드락 모드 시 leftmost holder 는 buffer slot (slotIndex >= _slotCount) 도 사용 가능.
        /// </summary>
        public int PlaceDart(int slotIndex, int color, int holderId)
        {
            EnsureSlotOccupancySynced();
            if (_boardFinished) return -1; // Reject after board clear/fail
            if (_slots == null || slotIndex < 0) return -1;

            // V2 아키텍처: deadlock 도 100% capacity. buffer 분기 제거.
            int slotIndexLimit = _slotCount;
            int capacityLimit  = _slotCount;

            if (slotIndex >= slotIndexLimit) return -1;
            if (_slots[slotIndex].dartColor >= 0) return -1; // occupied

            // frozen 다트가 복귀할 공간 확보 — 예약분 초과 시 배치 거부
            if (_occupiedCount + _frozenDartInfos.Count >= capacityLimit)
            {
                return -1;
            }

            int dartId = _nextDartId++;
            _slots[slotIndex].dartColor = color;
            _slots[slotIndex].holderId = holderId;
            _slots[slotIndex].dartId = dartId;
            var dart = new DartOnRail
            {
                dartId = dartId,
                dartColor = color,
                holderId = holderId,
                progress = GetPathDistanceForSlot(slotIndex),
                isFrozen = false,
                placedSeq = _nextPlacedSeq++,
                slotIndex = slotIndex
            };
            _darts.Add(dart);
            _dartById[dartId] = dart;
            UpdateClusterHeadCache(dart);
            _occupiedCount++;
            _lastPlacementUnscaledTime = Time.unscaledTime; // ROLLBACK_SUPPLY_ACTIONABLE_20260707: 배포 진행 신호

            PublishOccupancyChanged();
            return dartId;
        }

        // ROLLBACK_SUPPLY_ACTIONABLE_20260707: 마지막 레일 placement 시각 — BoardStateManager 의
        //   '배포 진행 중' 가드용 (진행 중엔 앞줄 공급 미스매치로 오판 fail 하지 않게). 0 = 이번 보드 무배치.
        private float _lastPlacementUnscaledTime;
        /// <summary>마지막으로 다트가 레일에 배치된 시각(unscaled). BoardStateManager 배포 진행 가드가 참조.</summary>
        public float LastPlacementUnscaledTime => _lastPlacementUnscaledTime;

        /// <summary>
        /// Removes the dart from a slot (dart was fired or cleared). Returns true if removed.
        /// </summary>
        public bool ClearSlot(int slotIndex)
        {
            EnsureSlotOccupancySynced();
            if (_slots == null || slotIndex < 0 || slotIndex >= _slotCount) return false;
            if (_slots[slotIndex].dartColor < 0) return false; // already empty

            int dartId = _slots[slotIndex].dartId;
            if (dartId >= 0)
            {
                _dartById.TryGetValue(dartId, out DartOnRail removedDart);
                _dartById.Remove(dartId);
                for (int i = _darts.Count - 1; i >= 0; i--)
                {
                    if (_darts[i].dartId == dartId)
                    {
                        _darts.RemoveAt(i);
                        break;
                    }
                }
                RemoveFromClusterHeadCache(removedDart);
            }

            _slots[slotIndex].dartColor = -1;
            _slots[slotIndex].holderId = -1;
            _slots[slotIndex].dartId = -1;
            _occupiedCount = _darts.Count;

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
            EnsureSlotOccupancySynced();
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

        /// <summary>
        /// Fills results with one active head dart per holder/cluster.
        /// DartManager uses this to avoid scanning every dart when only cluster heads can fire.
        /// </summary>
        public void GetClusterHeadDarts(List<DartOnRail> results)
        {
            if (results == null) return;

            results.Clear();
            foreach (KeyValuePair<int, DartOnRail> kvp in _clusterHeadByHolder)
            {
                DartOnRail head = kvp.Value;
                if (head == null || head.dartColor < 0) continue;
                if (!_dartById.TryGetValue(head.dartId, out DartOnRail current) || current != head) continue;
                results.Add(head);
            }
        }

        /// <summary>
        /// 데드락 릴리프 전용: 지정 holder 클러스터에서 색이 matchableColors 에 속하는
        /// front-most(최저 placedSeq) 다트 반환. head 색이 공격 불가일 때만 호출됨.
        /// 없으면 null. (head-only 규칙을 dead-head 한정으로만 우회 — 정상 클러스터엔 미사용.)
        /// </summary>
        public DartOnRail GetFrontmostFireableDart(int holderId, HashSet<int> matchableColors)
        {
            if (matchableColors == null || matchableColors.Count == 0) return null;
            DartOnRail best = null;
            for (int i = 0; i < _darts.Count; i++)
            {
                DartOnRail d = _darts[i];
                if (d == null || d.holderId != holderId || d.dartColor < 0) continue;
                if (!matchableColors.Contains(d.dartColor)) continue;
                if (best == null || d.placedSeq < best.placedSeq) best = d;
            }
            return best;
        }

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
            // V2 아키텍처: 모든 holder 동일 — slot-index O(1) check. buffer / deadlock 분기 없음.
            // DartPhysicalGap == _slotSpacing (line 248) 이므로 slot 점유 = 자동 minGap 보장.
            if (!HasRoomForAdditionalDart(DartPhysicalGap)) return false;

            EnsureSlotOccupancySynced();
            int targetSlot = GetSlotAtPathDistance(progress);
            if (targetSlot < 0 || targetSlot >= _slotCount) return false;
            return _slots[targetSlot].dartId < 0;
        }

        public string GetProgressClearFailReason(float progress, int holderId)
        {
            string roomBlockReason = GetAdditionalDartRoomBlockReason(DartPhysicalGap);
            if (!string.IsNullOrEmpty(roomBlockReason))
                return roomBlockReason;

            float normalizedProgress = progress;
            NormalizeProgress(ref normalizedProgress);

            EnsureSlotOccupancySynced();
            int targetSlot = GetSlotAtPathDistance(normalizedProgress);
            if (targetSlot < 0 || targetSlot >= _slotCount)
            {
                return $"invalidSlot progress={normalizedProgress:F2} targetSlot={targetSlot} slotCount={_slotCount} " +
                       $"slotSpacing={_slotSpacing:F3} rotationOffset={_rotationOffset:F3}";
            }

            SlotData slot = _slots[targetSlot];
            if (slot.dartId >= 0)
            {
                DartOnRail dart = FindDart(slot.dartId);
                string dartInfo = string.Empty;
                if (dart != null)
                {
                    float circularDist = Mathf.Abs(dart.progress - normalizedProgress);
                    if (_totalPathLength > 0f && circularDist > _totalPathLength * 0.5f)
                        circularDist = _totalPathLength - circularDist;

                    float slotCenter = GetPathDistanceForSlot(targetSlot);
                    float slotDelta = Mathf.Abs(slotCenter - normalizedProgress);
                    if (_totalPathLength > 0f && slotDelta > _totalPathLength * 0.5f)
                        slotDelta = _totalPathLength - slotDelta;

                    dartInfo = $" occProgress={dart.progress:F2} occFrozen={dart.isFrozen} progressDist={circularDist:F3} " +
                               $"slotCenter={slotCenter:F2} slotDelta={slotDelta:F3}";
                }

                return $"slotOccupied progress={normalizedProgress:F2} targetSlot={targetSlot} occupiedBy=holder{slot.holderId} " +
                       $"dartId={slot.dartId} color={slot.dartColor}{dartInfo}";
            }

            return $"clearNow progress={normalizedProgress:F2} targetSlot={targetSlot}";
        }

        public string GetPlacementGapDebugInfo(float progress, int color, int holderId, int ignoreDartId = -1)
        {
            float normalizedProgress = progress;
            NormalizeProgress(ref normalizedProgress);

            TryGetPlacementNeighbors(normalizedProgress, ignoreDartId, out DartOnRail prev, out float prevDist, out DartOnRail next, out float nextDist);
            string prevInfo = FormatNeighborDebugInfo("prev", prev, prevDist);
            string nextInfo = FormatNeighborDebugInfo("next", next, nextDist);
            string splitRisk = GetPlacementSplitRisk(color, holderId, prev, next);

            return $"gapDiag progress={normalizedProgress:F2} color={color} holder={holderId} " +
                   $"{prevInfo} {nextInfo} physGap={DartPhysicalGap:F3} clusterGap={DartClusterAttackGap:F3} splitRisk={splitRisk}";
        }

        public bool IsDeployProgressPhysicallyClear(float progress, int color, int holderId, out string reason)
        {
            if (!IsProgressClear(progress, holderId))
            {
                reason = $"slotBlocked {GetProgressClearFailReason(progress, holderId)}";
                return false;
            }

            float normalizedProgress = progress;
            NormalizeProgress(ref normalizedProgress);

            TryGetPlacementNeighbors(normalizedProgress, -1, out DartOnRail prev, out float prevDist, out DartOnRail next, out float nextDist);

            // ROLLBACK_DART_CELL_SPACING_CLUSTER_GAP:
            // Only same-holder neighbors need balloon-cell spacing. Other holders retain the rail
            // physical gap so independent clusters do not suppress each other's deployment.
            float requiredPrevGap = GetDeployRequiredGapForNeighbor(holderId, prev);
            float requiredNextGap = GetDeployRequiredGapForNeighbor(holderId, next);
            bool prevOk = prev == null || prevDist + 0.0001f >= requiredPrevGap;
            bool nextOk = next == null || nextDist + 0.0001f >= requiredNextGap;
            string gapInfo = GetPlacementGapDebugInfo(normalizedProgress, color, holderId);

            if (!prevOk || !nextOk)
            {
                reason = $"physicalGapTooSmall requiredPrev={requiredPrevGap:F3} requiredNext={requiredNextGap:F3} " +
                         $"prevOk={prevOk} nextOk={nextOk} {gapInfo}";
                return false;
            }

            // 이전: 물리 간격만 충분하면 배포 허용.
            // 문제: fire로 생긴 실제 gap이 deploy point에 도달하지 않아도, 같은 색 cluster 사이의 넓은 틈에
            // 다른 색이 들어가 AABBAABB 패턴을 만들 수 있음.
            // holder 기준까지 막으면 같은 색을 다른 holder가 이어받는 정상 배포까지 막힐 수 있어 color split만 차단한다.
            reason = $"physicalClear requiredPrev={requiredPrevGap:F3} requiredNext={requiredNextGap:F3} {gapInfo}";
            return true;
        }

        // ROLLBACK_DART_CELL_SPACING_CLUSTER_GAP:
        private float GetDeployRequiredGapForNeighbor(int holderId, DartOnRail neighbor)
        {
            float gap = DartPhysicalGap;
            if (neighbor != null && neighbor.holderId == holderId)
                gap = Mathf.Max(gap, DartClusterAttackGap);
            return Mathf.Max(0f, gap - DEPLOY_PHYSICAL_GAP_TOLERANCE);
        }

        private void TryGetPlacementNeighbors(
            float normalizedProgress,
            int ignoreDartId,
            out DartOnRail prev,
            out float prevDist,
            out DartOnRail next,
            out float nextDist)
        {
            prev = null;
            next = null;
            float localPrevDist = float.MaxValue;
            float localNextDist = float.MaxValue;

            for (int i = 0; i < _darts.Count; i++)
            {
                DartOnRail dart = _darts[i];
                if (dart == null || dart.dartId == ignoreDartId) continue;

                float distFromDart = ForwardDistance(dart.progress, normalizedProgress);
                if (distFromDart > 0.0001f && distFromDart < localPrevDist)
                {
                    localPrevDist = distFromDart;
                    prev = dart;
                }

                float distToDart = ForwardDistance(normalizedProgress, dart.progress);
                if (distToDart > 0.0001f && distToDart < localNextDist)
                {
                    localNextDist = distToDart;
                    next = dart;
                }
            }

            prevDist = localPrevDist;
            nextDist = localNextDist;
        }

        private string FormatNeighborDebugInfo(string label, DartOnRail dart, float distance)
        {
            if (dart == null)
                return $"{label}=none";

            return $"{label}=holder{dart.holderId}/dart{dart.dartId}/color{dart.dartColor}/prog{dart.progress:F2}/dist{distance:F3}/frozen{dart.isFrozen}";
        }

        private string FormatSlotDebugInfo(string label, int slotIndex)
        {
            if (_slots == null || slotIndex < 0 || slotIndex >= _slotCount)
                return $"{label}=slot{slotIndex}/invalid";

            SlotData slot = _slots[slotIndex];
            return $"{label}=slot{slotIndex}/holder{slot.holderId}/dart{slot.dartId}/color{slot.dartColor}";
        }

        private string GetPlacementSplitRisk(int color, int holderId, DartOnRail prev, DartOnRail next)
        {
            if (prev == null || next == null) return "edge";

            bool betweenSameColorCluster = prev.dartColor == next.dartColor;
            bool betweenSameHolderCluster = prev.holderId == next.holderId;
            bool deployMatchesBothColor = prev.dartColor == color && next.dartColor == color;
            bool deployMatchesBothHolder = prev.holderId == holderId && next.holderId == holderId;

            if (betweenSameHolderCluster && !deployMatchesBothHolder)
                return "betweenSameHolderCluster";

            if (betweenSameColorCluster && !deployMatchesBothColor)
                return "betweenSameColorCluster";

            if (!deployMatchesBothColor && prev.dartColor != color && next.dartColor != color)
                return "betweenOtherColors";

            return "none";
        }

        private float ForwardDistance(float fromProgress, float toProgress)
        {
            float d = toProgress - fromProgress;
            if (_totalPathLength > 0f && d < 0f) d += _totalPathLength;
            return d;
        }

        private float GetDeployBlockProgress(float deployProgress)
        {
            float blockProgress = deployProgress - (DartPhysicalGap * DEPLOY_POINT_CLEARANCE_GAP);
            NormalizeProgress(ref blockProgress);
            return blockProgress;
        }

        private void NormalizeProgress(ref float progress)
        {
            if (_totalPathLength <= 0f) return;
            progress = ((progress % _totalPathLength) + _totalPathLength) % _totalPathLength;
        }

        private bool IsReservationClearForHolder(float progress, int holderId)
        {
            float minGap = DartPhysicalGap;
            long myOrder = _holderReservations.TryGetValue(holderId, out ProgressReservation ownReservation)
                ? ownReservation.order
                : long.MaxValue;

            var reservationEn = _holderReservations.GetEnumerator();
            try
            {
                while (reservationEn.MoveNext())
                {
                    if (reservationEn.Current.Key == holderId) continue;

                    ProgressReservation reservation = reservationEn.Current.Value;
                    if (reservation.order > myOrder) continue;

                    int reserveCount = Mathf.Max(1, reservation.dartCount);
                    float reserveLength = minGap * reserveCount;
                    float distFromStart = ForwardDistance(reservation.startProgress, progress);
                    if (distFromStart <= reserveLength + minGap)
                        return false;
                }
            }
            finally { reservationEn.Dispose(); }

            return true;
        }

        private readonly List<float> _gapSortBuffer = new List<float>(256);
        private readonly List<int> _pushInsertIndices = new List<int>(256);
        private readonly List<float> _pushInsertDistances = new List<float>(256);
        private readonly List<float> _pushInsertTargets = new List<float>(256);

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

        public bool TryFindInsertionProgressNear(float targetProgress, float maxOffset, int holderId, out float insertionProgress)
        {
            insertionProgress = -1f;
            float pathLen = _totalPathLength;
            if (pathLen <= 0f) return false;

            NormalizeProgress(ref targetProgress);
            if (IsProgressClear(targetProgress, holderId))
            {
                insertionProgress = targetProgress;
                return true;
            }

            int n = _darts.Count;
            if (n == 0)
            {
                insertionProgress = targetProgress;
                return IsReservationClearForHolder(targetProgress, holderId);
            }

            float minGap = DartPhysicalGap;
            _gapSortBuffer.Clear();
            for (int i = 0; i < n; i++) _gapSortBuffer.Add(_darts[i].progress);
            _gapSortBuffer.Sort();

            float bestOffset = float.MaxValue;
            float bestProgress = -1f;

            for (int i = 0; i < n; i++)
            {
                float curr = _gapSortBuffer[i];
                float next = (i + 1 < n) ? _gapSortBuffer[i + 1] : _gapSortBuffer[0] + pathLen;
                float start = curr + minGap;
                float end = next - minGap;
                if (end < start) continue;

                float target = targetProgress;
                if (target < start - pathLen * 0.5f) target += pathLen;
                if (target > end + pathLen * 0.5f) target -= pathLen;

                float candidate = Mathf.Clamp(target, start, end);
                float normalized = candidate;
                NormalizeProgress(ref normalized);

                if (!IsReservationClearForHolder(normalized, holderId)) continue;
                if (!IsProgressClear(normalized, holderId)) continue;

                float offset = Mathf.Abs(candidate - target);
                if (offset > pathLen * 0.5f) offset = pathLen - offset;
                if (offset <= maxOffset && offset < bestOffset)
                {
                    bestOffset = offset;
                    bestProgress = normalized;
                }
            }

            if (bestProgress >= 0f)
            {
                insertionProgress = bestProgress;
                return true;
            }

            return false;
        }

        public void ReserveProgressForHolder(int holderId, float startProgress, int dartCount)
        {
            if (holderId < 0 || dartCount <= 0) return;

            if (_totalPathLength > 0f)
                startProgress = ((startProgress % _totalPathLength) + _totalPathLength) % _totalPathLength;

            long order = _holderReservations.TryGetValue(holderId, out ProgressReservation existing)
                ? existing.order
                : _nextReservationOrder++;

            _holderReservations[holderId] = new ProgressReservation
            {
                startProgress = startProgress,
                dartCount = dartCount,
                order = order
            };
        }

        public void ReleaseHolderReservation(int holderId)
        {
            _holderReservations.Remove(holderId);
        }

        public bool TryEnterDeployPlacement(int holderId)
        {
            return true;
        }

        public void ExitDeployPlacement(int holderId)
        {
        }

        public int TryPlaceDartWithPush(float progress, int color, int holderId, bool allowPush)
        {
            if (!IsProgressClear(progress, holderId)) return -1;
            return PlaceDartAtProgress(progress, color, holderId);
        }

        private bool HasRoomForAdditionalDart(float minGap)
        {
            return string.IsNullOrEmpty(GetAdditionalDartRoomBlockReason(minGap));
        }

        private string GetAdditionalDartRoomBlockReason(float minGap)
        {
            int effectiveCount = _darts.Count + _frozenDartInfos.Count;
            int physicalCapacity = PhysicalCapacity;
            if (effectiveCount >= physicalCapacity)
            {
                return $"noRoom effective={effectiveCount} physicalCapacity={physicalCapacity} " +
                       $"occupied={_occupiedCount} darts={_darts.Count} frozen={_frozenDartInfos.Count} slotCount={_slotCount}";
            }

            if (_totalPathLength <= 0f)
                return $"invalidPath totalPathLength={_totalPathLength:F3}";

            if (minGap <= 0f)
                return effectiveCount < _slotCount ? null : $"slotCapacityFull effective={effectiveCount} slotCount={_slotCount}";

            float requiredLength = (effectiveCount + 1) * minGap;
            if (requiredLength > _totalPathLength + 0.0001f)
            {
                return $"noPhysicalGap effective={effectiveCount} nextRequiredLen={requiredLength:F3} " +
                       $"pathLen={_totalPathLength:F3} gap={minGap:F3}";
            }

            return null;
        }

        private void ShiftSlotsForward(int fromSlot, int emptySlot)
        {
            if (_slots == null || _slotCount <= 0) return;

            int current = emptySlot;
            while (current != fromSlot)
            {
                int prev = (current - 1 + _slotCount) % _slotCount;
                _slots[current] = _slots[prev];
                UpdateDartSlotIndex(_slots[current].dartId, current);
                current = prev;
            }

            _slots[fromSlot].dartColor = -1;
            _slots[fromSlot].holderId = -1;
            _slots[fromSlot].dartId = -1;
        }

        private void UpdateDartSlotIndex(int dartId, int slotIndex)
        {
            if (dartId < 0) return;
            if (_dartById.TryGetValue(dartId, out DartOnRail dart))
            {
                dart.slotIndex = slotIndex;
                // ROLLBACK_DART_SLOTINDEX_NO_PROGRESS_REWRITE_20260615: START
                // dart.progress 가 시각/스캔/발사슬롯의 single source of truth 인데, 슬롯중심 거리로 덮어쓰면
                // 렌더가 tween 없이 직접 대입하므로 순간이동 + scan line/fire-slot 교란(놓침/중복)이 된다.
                // slotIndex 메타만 갱신하고 progress 는 belt(SyncSlotOccupancyFromDarts)가 단독 관리하게 둔다.
                // (현재 유일 호출자 ShiftSlotsForward 는 死코드이므로 라이브 영향 없음 — 방어적 정정.)
                // 롤백: 아래 한 줄 주석 해제.
                // dart.progress = GetPathDistanceForSlot(slotIndex);
                // ROLLBACK_DART_SLOTINDEX_NO_PROGRESS_REWRITE_20260615: END
            }
        }

        private void SyncSlotOccupancyFromDarts()
        {
            if (_slots == null || _slotCount <= 0) return;

            for (int i = 0; i < _slotCount; i++)
            {
                _slots[i].dartColor = -1;
                _slots[i].holderId = -1;
                _slots[i].dartId = -1;
            }

            for (int i = 0; i < _darts.Count; i++)
            {
                DartOnRail dart = _darts[i];
                if (dart == null) continue;

                int slotIndex = GetSlotAtPathDistance(dart.progress);
                if (_slots[slotIndex].dartColor >= 0)
                    slotIndex = FindNearestEmptySlot(slotIndex);

                if (slotIndex < 0) continue;

                dart.slotIndex = slotIndex;
                _slots[slotIndex].dartColor = dart.dartColor;
                _slots[slotIndex].holderId = dart.holderId;
                _slots[slotIndex].dartId = dart.dartId;
            }

            _occupiedCount = _darts.Count;
            _slotOccupancyDirty = false;
        }

        private void MarkSlotOccupancyDirty()
        {
            _slotOccupancyDirty = true;
        }

        private void EnsureSlotOccupancySynced()
        {
            if (_slotOccupancyDirty)
                SyncSlotOccupancyFromDarts();
        }

        private void UpdateClusterHeadCache(DartOnRail dart)
        {
            if (dart == null) return;
            if (!_clusterHeadByHolder.TryGetValue(dart.holderId, out DartOnRail current)
                || current == null
                || dart.placedSeq < current.placedSeq)
            {
                _clusterHeadByHolder[dart.holderId] = dart;
            }
        }

        private void RebuildClusterHeadCache(int holderId)
        {
            DartOnRail head = null;
            long minSeq = long.MaxValue;
            for (int i = 0; i < _darts.Count; i++)
            {
                DartOnRail dart = _darts[i];
                if (dart == null || dart.holderId != holderId) continue;
                if (dart.placedSeq < minSeq)
                {
                    minSeq = dart.placedSeq;
                    head = dart;
                }
            }

            if (head != null)
                _clusterHeadByHolder[holderId] = head;
            else
                _clusterHeadByHolder.Remove(holderId);
        }

        private void RemoveFromClusterHeadCache(DartOnRail dart)
        {
            if (dart == null) return;
            if (_clusterHeadByHolder.TryGetValue(dart.holderId, out DartOnRail head)
                && head != null
                && head.dartId == dart.dartId)
            {
                RebuildClusterHeadCache(dart.holderId);
            }
        }

        private int FindNearestEmptySlot(int centerSlot)
        {
            if (_slots == null || _slotCount <= 0) return -1;
            centerSlot = ((centerSlot % _slotCount) + _slotCount) % _slotCount;

            for (int step = 0; step < _slotCount; step++)
            {
                int forward = (centerSlot + step) % _slotCount;
                if (_slots[forward].dartColor < 0) return forward;

                if (step == 0) continue;
                int backward = (centerSlot - step + _slotCount) % _slotCount;
                if (_slots[backward].dartColor < 0) return backward;
            }

            return -1;
        }

        /// <summary>Place a dart at a specific progress on the path.</summary>
        public int PlaceDartAtProgress(float progress, int color, int holderId)
        {
            NormalizeProgress(ref progress);
            if (!IsProgressClear(progress, holderId)) return -1;

            // slotIndex 메타데이터 동기화 — progress 그대로 유지 (packing physics 자연 동작 위해 snap 안 함).
            // slotIndex 는 추적/디버그/빈 slot 인덱스 식별용. 시각 위치 계산은 progress 기반 GetPositionAtDistance.
            int slotIndex = -1;
            if (_slotSpacing > 0.0001f && _slotCount > 0)
                slotIndex = GetSlotAtPathDistance(progress);

            int id = _nextDartId++;
            var dart = new DartOnRail
            {
                dartId = id,
                dartColor = color,
                holderId = holderId,
                progress = progress,
                isFrozen = false,
                placedSeq = _nextPlacedSeq++,
                slotIndex = slotIndex
            };
            _darts.Add(dart);
            _dartById[id] = dart; // FindDart 캐시 동기화
            UpdateClusterHeadCache(dart);
            _occupiedCount = _darts.Count;
            // ROLLBACK_SUPPLY_ACTIONABLE_20260707: progress 기반 배치도 배포 진행 신호 스탬프 —
            //   실배포가 이 경로를 타는 레벨에서 스탬프 누락으로 deployProgress 가드가 항상 false 였음
            //   (2026-07-07 Level 62 로그: 배치 진행 중인데 lastPlaceAgo=-1.0).
            _lastPlacementUnscaledTime = Time.unscaledTime;
            SyncSlotOccupancyFromDarts();
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

        /// <summary>Remove a dart by ID. O(1) dict lookup + O(N) list remove.
        /// 사용자 의도 B: 다트 발사 시점에 cluster unfreeze → 빈 공간 advance → 다음 frame PropagateFreezeChain 이 다시 cluster 형성.
        /// 매 frame UpdateInternal 호출 대신 발사 시점만 호출 — 부하 0 + cluster 흐름 자연.</summary>
        public bool RemoveDartById(int dartId)
        {
            if (!_dartById.TryGetValue(dartId, out DartOnRail dart)) return false;

            int cachedSlot = dart.slotIndex;
            int currentSlot = GetSlotAtPathDistance(dart.progress);
            if (LOG_DART_REMOVE_DIAG)
            {
                Debug.Log($"[DartRemoveAttempt] dartId={dartId} holder={dart.holderId} color={dart.dartColor} " +
                          $"progress={dart.progress:F2} cachedSlot={cachedSlot} currentSlot={currentSlot} " +
                          $"{FormatSlotDebugInfo("cached", cachedSlot)} {FormatSlotDebugInfo("current", currentSlot)} " +
                          $"slotDirty={_slotOccupancyDirty} gapAfterRemove={GetPlacementGapDebugInfo(dart.progress, dart.dartColor, dart.holderId, dartId)} " +
                          $"{GetAdvanceModeDebugInfo()}");
            }

            if (dart.slotIndex >= 0 && dart.slotIndex < _slotCount
                && _slots != null && _slots[dart.slotIndex].dartId == dartId)
            {
                bool cleared = ClearSlot(dart.slotIndex);
                if (_frozenDartInfos.Count > 0)
                    UnfreezeAndReinsertAll();
                if (LOG_DART_REMOVE_DIAG)
                {
                    Debug.Log($"[DartRemoveResult] dartId={dartId} holder={dart.holderId} path=slotIndexClear " +
                              $"cleared={cleared} cachedSlot={cachedSlot} currentSlotBefore={currentSlot} {GetAdvanceModeDebugInfo()}");
                }
                return cleared;
            }

            _dartById.Remove(dartId);
            for (int i = 0; i < _darts.Count; i++)
            {
                if (_darts[i].dartId == dartId)
                {
                    _darts.RemoveAt(i);
                    RemoveFromClusterHeadCache(dart);
                    _occupiedCount = _darts.Count;

                    // 빈 공간 생겼으니 frozen cluster 풀어 packing physics 가 advance.
                    if (_frozenDartInfos.Count > 0)
                        UnfreezeAndReinsertAll();
                    if (LOG_DART_REMOVE_DIAG)
                    {
                        Debug.Log($"[DartRemoveResult] dartId={dartId} holder={dart.holderId} path=listRemove " +
                                  $"removed=True cachedSlot={cachedSlot} currentSlotBefore={currentSlot} {GetAdvanceModeDebugInfo()}");
                    }
                    return true;
                }
            }
            if (LOG_DART_REMOVE_DIAG)
            {
                Debug.Log($"[DartRemoveResult] dartId={dartId} holder={dart.holderId} path=notFound " +
                          $"removed=False cachedSlot={cachedSlot} currentSlotBefore={currentSlot} {GetAdvanceModeDebugInfo()}");
            }
            return false;
        }

        /// <summary>Find a dart by ID. O(1) — dict 조회.</summary>
        public DartOnRail FindDart(int dartId)
        {
            return _dartById.TryGetValue(dartId, out DartOnRail d) ? d : null;
        }

        /// <summary>Get world position for a dart. slot index 우선 — 사용자 요구로 다트 위치 통일.</summary>
        public Vector3 GetDartWorldPosition(int dartId)
        {
            var dart = FindDart(dartId);
            return dart != null ? GetDartCurrentPosition(dart) : Vector3.zero;
        }

        /// <summary>Get firing direction for a dart based on its progress along the path.</summary>
        public Vector3 GetDartFiringDirection(int dartId)
        {
            var dart = FindDart(dartId);
            if (dart == null) return Vector3.forward;
            float t = _totalPathLength > 0f ? dart.progress / _totalPathLength : 0f;
            t = ((t % 1f) + 1f) % 1f;
            Vector3 moveDir = GetDirectionAtNormalized(t);
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

        // Phase 2 v1 — sequential turn-based deploy 지원 API.

        /// <summary>특정 holder dart 들 모두 isFrozen=true. cluster 정지 효과 (UpdateInternal 에서 progress update skip).</summary>
        public void FreezeClusterByHolder(int holderId)
        {
            for (int i = 0; i < _darts.Count; i++)
            {
                var d = _darts[i];
                if (d.holderId == holderId) d.isFrozen = true;
            }
        }

        /// <summary>특정 holder dart 들 모두 isFrozen=false. cluster belt 회전 재개.</summary>
        public void UnfreezeClusterByHolder(int holderId)
        {
            for (int i = 0; i < _darts.Count; i++)
            {
                var d = _darts[i];
                if (d.holderId == holderId) d.isFrozen = false;
            }
        }

        /// <summary>자기 holder dart 중 placedSeq 가장 작은 dart (가장 먼저 spawn = belt 진행 방향 head).
        /// 없으면 null.</summary>
        public DartOnRail GetClusterHeadDart(int holderId)
        {
            if (_clusterHeadByHolder.TryGetValue(holderId, out DartOnRail head)
                && head != null
                && _dartById.ContainsKey(head.dartId))
            {
                return head;
            }

            RebuildClusterHeadCache(holderId);
            return _clusterHeadByHolder.TryGetValue(holderId, out head) ? head : null;
        }

        /// <summary>progress 가 다른 holder 의 활성 deploy point 와 인접 (within physGap) 한지 check.
        /// 인접하면 그 holder Id 반환, 아니면 -1. 사용자 의도: A cluster head 가 B deploy point 도달 trigger.</summary>
        public int GetOtherActiveDeployPointHolderNear(float progress, int callingHolderId)
        {
            if (_deployPoints.Count == 0 || _activeDeployPoints.Count == 0) return -1;
            float physGap = DartPhysicalGap;
            var en = _deployPoints.GetEnumerator();
            try
            {
                while (en.MoveNext())
                {
                    if (en.Current.Key == callingHolderId) continue;
                    if (!_activeDeployPoints.Contains(en.Current.Key)) continue;
                    float deployProg = GetDeployBlockProgress(en.Current.Value);
                    float distanceAhead = ForwardDistance(progress, deployProg);
                    if (distanceAhead > 0.001f && distanceAhead <= physGap) return en.Current.Key;
                }
            }
            finally { en.Dispose(); }
            return -1;
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
            EnsureSlotOccupancySynced();
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
            EnsureSlotOccupancySynced();
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
            EnsureSlotOccupancySynced();
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
        // 재사용 HashSet (매 프레임 호출되므로 GC 방지).
        private readonly HashSet<int> _reusableRailColors = new HashSet<int>();

        public HashSet<int> GetRailDartColors()
        {
            _reusableRailColors.Clear();
            for (int i = 0; i < _darts.Count; i++)
            {
                int c = _darts[i].dartColor;
                if (c >= 0) _reusableRailColors.Add(c);
            }
            return _reusableRailColors;
        }

        /// <summary>
        /// Determines the appropriate rail capacity based on total dart count.
        /// Design: darts≤300→40, ≤500→80, ≤700→120, else→160.
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
                    _dartById.Remove(_darts[i].dartId); // FindDart 캐시 동기화
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

        #region Public Methods — Continue (이어하기 정본 API)

        /// <summary>
        /// 이어하기 결과 정보. 제거된 다트 수 / 매칭 풍선 수 / 사용된 색상 종류 수.
        /// </summary>
        public struct ContinueRemoveResult
        {
            public int removedDarts;
            public int removedBalloons;
            public int distinctColors;
            /// <summary>제거된(가장 많은) 다트 색상. -1 = 제거 없음. 이어하기 holder 색상 필터용.</summary>
            public int targetColor;
        }

        // GC alloc 0 — 매 호출 재사용 버퍼.
        private readonly List<DartOnRail> _continueRecentBuffer = new List<DartOnRail>(64);
        private readonly List<int> _continueRemoveColors = new List<int>(16);
        private readonly List<int> _continueRemoveCounts = new List<int>(16);
        private static System.Comparison<DartOnRail> _placedSeqDescending;
        // 보드 fail 시 HandleBoardFailed 가 _darts 를 즉시 비우므로, continue 가 쓸 '최다 색+개수'를
        // fail 직전에 스냅샷해 둔다. (continue 시점엔 레일 다트가 0개라 live 계산이 불가능.)
        private int _continueSnapColor = -1;
        private int _continueSnapCount;

        /// <summary>레일 다트 중 가장 많은 색과 그 개수 반환. 없으면 (-1, count=0). 동률이면 먼저 등장한 색.</summary>
        private int ComputeMostCommonDartColor(out int count)
        {
            count = 0;
            _continueRemoveColors.Clear();
            _continueRemoveCounts.Clear();
            for (int i = 0; i < _darts.Count; i++)
            {
                int c = _darts[i].dartColor;
                if (c < 0) continue;
                bool found = false;
                for (int k = 0; k < _continueRemoveColors.Count; k++)
                {
                    if (_continueRemoveColors[k] == c) { _continueRemoveCounts[k]++; found = true; break; }
                }
                if (!found) { _continueRemoveColors.Add(c); _continueRemoveCounts.Add(1); }
            }
            int targetColor = -1, maxCount = 0;
            for (int k = 0; k < _continueRemoveColors.Count; k++)
            {
                if (_continueRemoveCounts[k] > maxCount) { maxCount = _continueRemoveCounts[k]; targetColor = _continueRemoveColors[k]; }
            }
            count = maxCount;
            return targetColor;
        }

        /// <summary>보드 fail 이 _darts 를 비우기 직전에 호출 — continue 용 최다 색+개수를 스냅샷.</summary>
        public void CaptureContinueSnapshot()
        {
            _continueSnapColor = ComputeMostCommonDartColor(out _continueSnapCount);
        }

        /// <summary>
        /// 이어하기 정본 API: 레일 다트 중 '가장 많은 색'을 제거하고, 그 수만큼 같은 색 필드 풍선을
        /// '랜덤' 제거 (필드에 적게 남았으면 있는 만큼만). 보관함(holder)은 건드리지 않음.
        /// ※ 보드 fail 이 이미 _darts 를 비웠으므로(HandleBoardFailed), 레일이 비어 있으면 fail 직전
        ///   스냅샷(CaptureContinueSnapshot)의 색/개수를 사용한다. 이게 'removed 0 darts → 풍선 0개' 버그의 핵심 수정.
        /// </summary>
        public ContinueRemoveResult RemoveMostCommonColorDartsAndRandomBalloons()
        {
            ContinueRemoveResult result = default;
            result.targetColor = -1; // 제거 없을 시 안전값(color 0 오인 방지)

            int targetColor;
            int targetCount;

            if (_darts.Count > 0)
                // 방어적: 레일에 다트가 남아 있으면 live 계산.
                targetColor = ComputeMostCommonDartColor(out targetCount);
            else
            {
                // 일반 경로: fail 직전 스냅샷 사용 (레일 다트는 이미 fail 로 비워짐).
                targetColor = _continueSnapColor;
                targetCount = _continueSnapCount;
            }

            if (targetColor < 0 || targetCount <= 0) return result;

            // [1:1 보정] 풍선을 먼저 제거해 "실제 제거된 풍선 수(M)" 를 확정한다.
            //   PopRandomBalloonsByColor 는 잠금(LockKey)/플렉스튜브(FlexTube) 풍선 제외 + available 클램프라
            //   targetCount 미만일 수 있다. 다트를 풍선보다 많이 제거하면 "다트 소진·풍선 잔존 → 데드락" 이 되므로,
            //   다트도 정확히 M 개만 제거해 다트:풍선 = 1:1 을 유지한다.
            int balloonsRemoved = 0;
            if (BalloonController.HasInstance)
                balloonsRemoved = BalloonController.Instance.PopRandomBalloonsByColor(targetColor, targetCount);
            result.removedBalloons = balloonsRemoved;

            if (_darts.Count > 0)
            {
                // 레일 다트를 풍선 제거 수(M)만큼만, 최근 배치(높은 dartId) 우선 제거.
                if (balloonsRemoved > 0)
                {
                    _continueRecentBuffer.Clear();
                    for (int i = 0; i < _darts.Count; i++)
                        if (_darts[i].dartColor == targetColor) _continueRecentBuffer.Add(_darts[i]);
                    _continueRecentBuffer.Sort((a, b) => b.dartId.CompareTo(a.dartId));
                    for (int i = 0; i < _continueRecentBuffer.Count && result.removedDarts < balloonsRemoved; i++)
                        if (RemoveDartById(_continueRecentBuffer[i].dartId)) result.removedDarts++;
                    _continueRecentBuffer.Clear();

                    if (result.removedDarts > 0) PublishOccupancyChanged();
                }
            }
            else
            {
                // 스냅샷 경로: 레일은 이미 fail 로 비워져 물리 제거 없음. 보고값만 풍선과 1:1.
                result.removedDarts = balloonsRemoved;
            }

            result.distinctColors = 1;
            result.targetColor = targetColor; // 이어하기 holder 색상 필터용

            // 스냅샷 1회 소비 — 다음 fail 에서 다시 캡쳐.
            _continueSnapColor = -1;
            _continueSnapCount = 0;
            return result;
        }

        #endregion

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
            EnsureSlotOccupancySynced();
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
                    var dart = new DartOnRail
                    {
                        dartId = info.dartId,
                        dartColor = info.color,
                        holderId = info.holderId,
                        progress = GetPathDistanceForSlot(emptySlot),
                        isFrozen = false,
                        placedSeq = _nextPlacedSeq++,
                        slotIndex = emptySlot
                    };
                    _darts.Add(dart);
                    _dartById[info.dartId] = dart;
                    UpdateClusterHeadCache(dart);
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
        /// 데드락 buffer slot (slotIndex >= _slotCount) 인 경우 정상 belt 범위 (0~totalPathLen) 안으로 wrap.
        /// 버퍼 dart 의 visual 위치는 normal slot 위치와 겹쳐 packing physics 가 자연 분리.
        /// </summary>
        public float GetPathDistanceForSlot(int slotIndex)
        {
            float distance = slotIndex * _slotSpacing + _rotationOffset;
            if (_totalPathLength > 0f)
            {
                // 강제 wrap (옵션 a) — buffer slot 이 belt 범위 안으로 들어옴.
                distance = ((distance % _totalPathLength) + _totalPathLength) % _totalPathLength;
            }
            return distance;
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
            _nextPlacedSeq = 0;
            _nextReservationOrder = 0;
            _boardFinished = false;
            // ROLLBACK_BOOSTER_PAUSE_RESET_20260618: ResetAll 에서도 부스터 일시정지 해제 (soft-lock 방지). QA HIGH.
            IsPausedByBooster = false;
            _darts.Clear();
            _dartById.Clear();
            _clusterHeadByHolder.Clear();
            _deployPoints.Clear();
            _activeDeployPoints.Clear();
            _holderReservations.Clear();
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
            _holderReservations.Clear();
            _nextReservationOrder = 0;
            _darts.Clear();
            _dartById.Clear();
            _clusterHeadByHolder.Clear();
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
            // continue 가 쓸 '최다 색+개수' 스냅샷 (레일이 이미 비어있는 fail 경로 대비 폴백).
            CaptureContinueSnapshot();
            _boardFinished = true;
            _deployPoints.Clear();
            _activeDeployPoints.Clear();
            _holderReservations.Clear();
            _nextReservationOrder = 0;
            // [이어하기 1:1 정합 2026-06-11] fail 시 레일 다트를 비우지 않는다.
            //   기존 전체 Clear 는 이어하기 보상(최다색 1:1 제거)에 포함되지 않는 '다른 색' 다트를
            //   보상 없이 소멸시켜 다트:풍선 개수 불일치(그 색 풍선 영구 잔존 → 클리어 불가)를 만들었다.
            //   유지하면 continue 가 live 경로로 최다색만 정확히 M개 제거하고 나머지 색은 레일에 남아
            //   1:1 이 보존된다. _boardFinished=true 가 벨트/스캔/발사를 정지시키므로 잔존 다트는 동결
            //   상태이며, 이어하기 거절/재시작 시엔 레벨 로드 경로(ClearAllDarts)가 정리한다.
            //   롤백: 아래 3줄 복원.
            // _darts.Clear();
            // _dartById.Clear();
            // _clusterHeadByHolder.Clear();
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
            // [Optimization 2026-05-10] segment direction 캐시도 함께 reset. 롤백: 이 라인 + 아래 _segmentDirections.Add 라인 제거.
            _segmentDirections.Clear();
            _totalPathLength = 0f;

            var path = _smoothedPath;
            if (path.Count < 2) return;

            int segmentCount = _isClosedLoop ? path.Count : path.Count - 1;

            for (int i = 0; i < segmentCount; i++)
            {
                int nextIndex = (i + 1) % path.Count;
                Vector3 delta = path[nextIndex] - path[i];
                float segLen = delta.magnitude;
                _segmentLengths.Add(segLen);
                _totalPathLength += segLen;
                _cumulativeLengths.Add(_totalPathLength);
                // [Optimization 2026-05-10] segment direction 사전 계산. segLen==0 이면 fallback forward.
                _segmentDirections.Add(segLen > 0.0001f ? delta / segLen : Vector3.forward);
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


        // V2 아키텍처 freeze 추적 — 회전 중 deploy block 도달한 holder 의 cluster 전체 freeze.
        // 매 frame 재평가, 조건 해제 시 자동 unfreeze.
        // ── 2026-05-08 폐기 — 롤백 가능. dart.isFrozen=true → dart.progress 절대 정지로 fire 검사 시
        //   매칭 풍선 위치 도달 못함 → stuck. packing physics 의 ahead obstacle 검사에 deploy block
        //   추가하는 방식으로 대체 (UpdateInternal 의 ── A 영역). 필드 + 함수 모두 주석.
        // private readonly System.Collections.Generic.HashSet<int> _v2FrozenHolders = new System.Collections.Generic.HashSet<int>();
        // private readonly System.Collections.Generic.HashSet<int> _v2ShouldFreezeBuffer = new System.Collections.Generic.HashSet<int>();

        /*
        private void V2UpdateFreezeOnDeployBlock()
        {
            _v2ShouldFreezeBuffer.Clear();

            if (_deployPoints.Count > 0 && _activeDeployPoints.Count > 0 && _darts.Count > 0)
            {
                // freeze 검사 range = physGap + 한 frame 안 belt 진행 거리.
                float beltStep = _rotationSpeed * UserSpeedMultiplier * _slotSpacing * Time.deltaTime;
                float freezeRange = DartPhysicalGap + beltStep;

                var en = _deployPoints.GetEnumerator();
                try
                {
                    while (en.MoveNext())
                    {
                        int deployHolderId = en.Current.Key;
                        if (!_activeDeployPoints.Contains(deployHolderId)) continue;
                        float blockProgress = GetDeployBlockProgress(en.Current.Value);

                        int deployColor = -1;
                        if (HolderManager.HasInstance)
                        {
                            var hd = HolderManager.Instance.FindHolderPublic(deployHolderId);
                            if (hd != null) deployColor = hd.color;
                        }

                        for (int i = 0; i < _darts.Count; i++)
                        {
                            DartOnRail dart = _darts[i];
                            if (dart.holderId == deployHolderId) continue;
                            if (deployColor >= 0 && dart.dartColor == deployColor) continue;
                            float distToBlock = ForwardDistance(dart.progress, blockProgress);
                            if (distToBlock <= freezeRange)
                            {
                                _v2ShouldFreezeBuffer.Add(dart.holderId);
                            }
                        }
                    }
                }
                finally { en.Dispose(); }
            }

            for (int i = 0; i < _darts.Count; i++)
            {
                DartOnRail dart = _darts[i];
                bool shouldFreeze = _v2ShouldFreezeBuffer.Contains(dart.holderId);
                bool wasV2Frozen = _v2FrozenHolders.Contains(dart.holderId);

                if (shouldFreeze) dart.isFrozen = true;
                else if (wasV2Frozen) dart.isFrozen = false;
            }

            _v2FrozenHolders.Clear();
            var bufEn = _v2ShouldFreezeBuffer.GetEnumerator();
            try { while (bufEn.MoveNext()) _v2FrozenHolders.Add(bufEn.Current); }
            finally { bufEn.Dispose(); }
        }
        */

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
