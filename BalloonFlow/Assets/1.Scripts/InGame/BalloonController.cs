using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using DG.Tweening;

namespace BalloonFlow
{
    /// <summary>
    /// Manages all balloons on the game board.
    /// Balloons are stationary after initial placement (Spawner gimmick excepted).
    /// Provides lookup, pop, and gimmick-base behavior for each balloon.
    /// </summary>
    /// <remarks>
    /// Layer: Domain | Genre: Puzzle | Role: Manager | Phase: 1
    /// DB Reference: CatController (Expert Puzzle score 0.6) — pooled entity + state enum pattern;
    ///               ObstacleManager (Expert Puzzle score 0.6) — Init/Get/Clear pattern;
    ///               logicFlow from ingame_balloon_board.yaml contracts.
    /// </remarks>
    public class BalloonController : SceneSingleton<BalloonController>
    {
        #region Constants

        private const string PoolKey = "Balloon";
        // Gimmick prefab pool keys — must match Resources/Prefabs/<key>.prefab exactly.
        private const string BarricadePoolKey  = "Baricade";     // Barricade gimmick visual
        private const string IronBoxPoolKey    = "IronBox";      // Wall/IronWall visual
        private const string TargetBoxPoolKey  = "paint";        // Pinata_Box / Target Box visual
        private const string WoodenBoardPoolKey = "WoodenBoard"; // Pin gimmick visual (Lv.61)
        private const string FrozenLayerPoolKey = "FrozenLayer"; // Ice / Frozen_Dart overlay child
        private const int PinataRequiredHits = 2;
        private const float DEFAULT_BALLOON_SCALE = 0.5f;
        // ROLLBACK_BARRICADE_BODY_1TO1_SCALE:
        // Updated Barricade/BarricadeBody art is authored at a 1:1 local scale per board cell.
        private const float BARRICADE_BODY_CELL_LOCAL_SCALE_X = 1f;

        /// <summary>
        /// Color palette for balloon visualization. Index matches BalloonData.color.
        /// </summary>
        /// <summary>PixelArtConverter 28색 팔레트와 동기화된 색상표.</summary>
        public static readonly Color[] BalloonColors = new Color[]
        {
            new Color(252/255f, 106/255f, 175/255f),  //  0: HotPink
            new Color( 80/255f, 232/255f, 246/255f),  //  1: Cyan
            new Color(137/255f,  80/255f, 248/255f),  //  2: Purple
            new Color(254/255f, 213/255f,  85/255f),  //  3: Yellow
            new Color(115/255f, 254/255f, 102/255f),  //  4: Green
            new Color(253/255f, 161/255f,  76/255f),  //  5: Orange
            new Color(255/255f, 255/255f, 255/255f),  //  6: White
            new Color( 65/255f,  65/255f,  65/255f),  //  7: DarkGray
            new Color(110/255f, 168/255f, 250/255f),  //  8: SkyBlue
            new Color( 57/255f, 174/255f,  46/255f),  //  9: Forest
            new Color(252/255f,  94/255f,  94/255f),  // 10: Red
            new Color( 50/255f, 107/255f, 248/255f),  // 11: Blue
            new Color( 58/255f, 165/255f, 139/255f),  // 12: Teal
            new Color(231/255f, 167/255f, 250/255f),  // 13: Lavender
            new Color(183/255f, 199/255f, 251/255f),  // 14: Periwinkle
            new Color(106/255f,  74/255f,  48/255f),  // 15: Brown
            new Color(254/255f, 227/255f, 169/255f),  // 16: Cream
            new Color(253/255f, 183/255f, 193/255f),  // 17: Pink
            new Color(158/255f,  61/255f,  94/255f),  // 18: Wine
            new Color(167/255f, 221/255f, 148/255f),  // 19: Mint
            new Color( 89/255f,  46/255f, 126/255f),  // 20: Indigo
            new Color(220/255f, 120/255f, 129/255f),  // 21: Rose
            new Color(217/255f, 217/255f, 231/255f),  // 22: Silver
            new Color(111/255f, 114/255f, 127/255f),  // 23: Gray
            new Color(252/255f,  56/255f, 165/255f),  // 24: Magenta
            new Color(253/255f, 180/255f,  88/255f),  // 25: Amber
            new Color(137/255f,  10/255f,   8/255f),  // 26: Crimson
            new Color(111/255f, 175/255f, 177/255f),  // 27: Sage
        };

        // Gimmick type string constants — 정본: BalloonFlow_기믹명세 (2026-03-17)
        public const string GimmickNone         = "none";
        public const string GimmickHidden       = "Hidden";          // Lv.11  보관함 색상 숨김
        public const string GimmickChain        = "Chain";           // Lv.21  2~4 보관함 연결 순차 배치
        public const string GimmickPinata       = "Pinata";          // Lv.31  1×1~6×6 HP 오브젝트
        public const string GimmickSpawnerT     = "Spawner_T";       // Lv.41  투명 스포너 (큐에 보관함 생성)
        public const string GimmickPin          = "Pin";             // Lv.61  1×N 점진 제거 장애물
        public const string GimmickLockKey      = "Lock_Key";        // Lv.81  Key→Lock 해제
        public const string GimmickSurprise     = "Surprise";        // Lv.101 필드 풍선 색상 숨김 (인접 팝 공개)
        public const string GimmickWall         = "Wall";            // Lv.121 파괴 불가 벽
        public const string GimmickSpawnerO     = "Spawner_O";       // Lv.141 불투명 스포너
        public const string GimmickPinataBox    = "Pinata_Box";      // Lv.161 다중 셀 피냐타
        public const string GimmickIce          = "Ice";             // Lv.201 간접 제거 (모든 팝으로 HP 감소)
        public const string GimmickFrozenDart   = "Frozen_Dart";     // Lv.241 동결 풍선 (2히트 필요: 1히트=해동, 2히트=팝)
        public const string GimmickColorCurtain = "Color_Curtain";   // Lv.281 지정 색상 간접 제거
        public const string GimmickBarricade    = "Barricade";       // destructible wall (HP-based)
        public const string GimmickFlexTube     = "FlexTube";        // 다중 셀 ㄴ/ㄷ/ㄹ 형 튜브 — 같은 색 다트로 EndCap 쪽부터 1셀씩 제거

        #endregion

        #region Fields

        [SerializeField] private GameObject _balloonPrefab;

        [Tooltip("Hidden 기믹 풍선 전용 머테리얼 (BalloonHidden.mat). Inspector 할당 또는 Resources/BalloonHidden 에서 자동 로드.")]
        [SerializeField] private Material _balloonHiddenMaterial;

        [Header("[Perf — 외곽 풍선만 Outline 활성]")]
        [Tooltip("외곽 풍선만 Outline pass 활성 (_OutlineEnabled MaterialPropertyBlock). " +
                 "[2026-05-10] 항상 ON 으로 강제 — Lobby/MapMaker 등 다양한 진입 경로에서도 inspector toggle 의존 없이 자동 적용. " +
                 "RefreshOutermostRendererState 의 가드 라인도 주석 처리됨. 토글 disable 원하면 가드 라인 + default false 복원.")]
        [SerializeField] private bool _outlineOnOuterOnly = true;

        // ROLLBACK_BARRICADE_VISUAL_SETTINGS:
        // Barricade art has its own pivot/height/body length, so keep visual placement separate
        // from regular balloons and from logical cell occupancy.
        [Header("[Barricade Visual]")]
        [SerializeField] private float _barricadeVisualY = 0.5f;
        [SerializeField] private Vector3 _barricadeVisualOffset = Vector3.zero;
        [SerializeField] private float _barricadeLengthMultiplier = 1f;
        [SerializeField] private float _barricadeLengthPadding = 0f;
        [SerializeField] private Vector3 _barricadeBodyVisualOffset = Vector3.zero;
        [SerializeField] private Vector3 _barricadeEdgeOffset = Vector3.zero;

        // Primary data store keyed by balloonId
        private readonly Dictionary<int, BalloonData> _balloons = new Dictionary<int, BalloonData>();

        // Visual GameObject handles keyed by balloonId
        private readonly Dictionary<int, GameObject> _balloonObjects = new Dictionary<int, GameObject>();
        private readonly List<GameObject> _flexTubeRoots = new List<GameObject>(); // FlexTube 부모 GameObject 캐시 — ClearAllBalloons 시 일괄 destroy.

        // Renderer cache per balloonId — _outermostCulling 시 매 호출 GetComponentsInChildren 비용 회피
        private readonly Dictionary<int, Renderer[]> _balloonRenderers = new Dictionary<int, Renderer[]>();

        // Hidden balloons that are currently color-concealed
        private readonly HashSet<int> _hiddenBalloons = new HashSet<int>();

        // Pinata multi-tile occupancy: key = balloonId, value = list of all occupied ids
        private readonly Dictionary<int, List<int>> _pinataGroup = new Dictionary<int, List<int>>();

        // Spatial index: position key -> balloonId  (for adjacency lookups)
        private readonly Dictionary<Vector3Int, int> _positionIndex = new Dictionary<Vector3Int, int>();

        // ROLLBACK_BARRICADE_MULTI_CELL_OCCUPANCY:
        // Barricade is one object, but it can occupy multiple logical board cells.
        private readonly Dictionary<int, List<Vector3Int>> _multiCellOccupancy = new Dictionary<int, List<Vector3Int>>();
        private readonly Dictionary<Transform, Vector3> _barricadeBodyBaseScales = new Dictionary<Transform, Vector3>();
        private readonly Dictionary<Transform, Quaternion> _barricadeBodyBaseRotations = new Dictionary<Transform, Quaternion>();
        private readonly Dictionary<Transform, Vector3> _barricadeBodyBasePositions = new Dictionary<Transform, Vector3>();
        private readonly Dictionary<Transform, Quaternion> _barricadeEdgeBaseRotations = new Dictionary<Transform, Quaternion>();
        private readonly List<Vector3Int> _reusableOccupiedCells = new List<Vector3Int>(16);

        // Key tracking: balloonId -> lockPairId (for path-based Key release)
        private readonly Dictionary<int, int> _activeKeyPairIds = new Dictionary<int, int>();

        // Scale multiplier for balloon visuals (set from LevelConfig)
        private float _balloonScale = DEFAULT_BALLOON_SCALE;

        // Grid spacing for adjacency calculations (read from GameManager.Board)
        private float _cellSpacing = 0.55f;

        private int _nextBalloonId;
        private int _currentLevelId;

        #endregion

        #region Properties

        /// <summary>Total number of non-popped balloons currently on the board.</summary>
        public int RemainingCount { get; private set; }

        /// <summary>누적 팝된 풍선 수 (Spawner로 추가된 풍선 포함). 진행률 슬라이더용.</summary>
        public int PoppedCount { get; private set; }

        /// <summary>
        /// Sets the visual scale for all balloons. Call before InitBoard.
        /// </summary>
        public void SetBalloonScale(float scale)
        {
            _balloonScale = Mathf.Clamp(scale, 0.2f, 1.0f);
        }

        /// <summary>
        /// Sets the grid cell spacing used for adjacency calculations.
        /// Must be called before SetupBalloons. GameManager.Board.cellSpacing 기준.
        /// </summary>
        public void SetCellSpacing(float spacing)
        {
            _cellSpacing = Mathf.Max(0.1f, spacing);
        }

        #endregion

        #region Lifecycle

        protected override void OnSingletonAwake()
        {
            _nextBalloonId = 1;
            _currentLevelId = -1;

            // GameManager.Board에서 설정값 읽기
            if (GameManager.HasInstance)
            {
                _cellSpacing = GameManager.Instance.Board.cellSpacing;
                _balloonScale = GameManager.Instance.Board.balloonScale;
            }
        }


        /// <summary>레벨별 안전 배율. 넘지 않으면 설정값 그대로, 넘으면 축소.</summary>
        private float _levelSafeWm = 1f, _levelSafeHm = 1f;
        /// <summary>위/아래 침범 차이 보정용 Z 이동량.</summary>
        private float _levelSafeZShift = 0f;
        private bool _levelSafeCalculated = false;

        /// <summary>SetupBalloons 완료 후 호출 — 레벨별 안전 배율 1회 계산.</summary>
        private void CalculateLevelSafeMult()
        {
            _levelSafeCalculated = false;
            _levelSafeZShift = 0f;
            if (!GameManager.HasInstance) { _levelSafeWm = 1f; _levelSafeHm = 1f; return; }

            float wm = GameManager.Instance.Board.balloonFieldWidthMult;
            float hm = GameManager.Instance.Board.balloonFieldHeightMult;
            float cz = GameManager.Instance.Board.balloonCenterZ;
            float beltCZ = GameManager.Instance.Board.boardCenterZ;

            // 벨트 내부 경계 (절대 좌표)
            float halfConveyor = BoardTileManager.CONVEYOR_HEIGHT * 0.5f;
            float halfRail = BoardTileManager.RAIL_THICKNESS * 0.5f;
            float beltTop = beltCZ + halfConveyor - halfRail;
            float beltBottom = beltCZ - halfConveyor + halfRail;
            float beltInnerSpan = beltTop - beltBottom;
            float beltInnerCenter = (beltTop + beltBottom) * 0.5f;

            // 풍선 최소/최대 Z (배율 적용 후)
            float minZ = float.MaxValue, maxZ = float.MinValue;
            foreach (var kvp in _balloons)
            {
                float z = cz + (kvp.Value.position.z - cz) * hm;
                if (z < minZ) minZ = z;
                if (z > maxZ) maxZ = z;
            }

            if (minZ >= maxZ) { _levelSafeWm = wm; _levelSafeHm = hm; _levelSafeCalculated = true; return; }

            float gridSpan = maxZ - minZ;
            float gridCenter = (maxZ + minZ) * 0.5f;

            if (gridSpan > beltInnerSpan)
            {
                // 벨트 초과 → 가로세로 동일 비율 축소
                float ratio = beltInnerSpan / gridSpan;
                wm *= ratio;
                hm *= ratio;
                gridCenter = cz; // 축소 후 중심은 balloonCenterZ

                // 축소 후 벨트 내부 정중앙에 배치 → 위아래 마진 동일
                _levelSafeZShift = beltInnerCenter - gridCenter;
            }

            _levelSafeWm = wm;
            _levelSafeHm = hm;
            _levelSafeCalculated = true;
        }

        private void SyncBoardMetricsForLevelSetup()
        {
            // ROLLBACK_GIMMICK_CELLSPACING_SYNC:
            // Runtime levels can change GameManager.Board.cellSpacing before SetupBalloons.
            // Keep BalloonController's grid key/adjacency spacing in sync for field gimmicks.
            if (GameManager.HasInstance)
            {
                _cellSpacing = Mathf.Max(0.1f, GameManager.Instance.Board.cellSpacing);
            }

            _levelSafeWm = 1f;
            _levelSafeHm = 1f;
            _levelSafeZShift = 0f;
            _levelSafeCalculated = false;
        }

        private void ReapplyAllBalloonVisualTransforms()
        {
            float scaleMult = _levelSafeCalculated
                ? Mathf.Max(_levelSafeWm, _levelSafeHm)
                : (GameManager.HasInstance ? Mathf.Max(GameManager.Instance.Board.balloonFieldWidthMult, GameManager.Instance.Board.balloonFieldHeightMult) : 1f);

            foreach (var kvp in _balloons)
            {
                BalloonData data = kvp.Value;
                if (data.isPopped) continue;
                if (!_balloonObjects.TryGetValue(data.balloonId, out GameObject obj) || obj == null) continue;

                if (data.gimmickType == GimmickBarricade)
                {
                    ApplyBarricadeVisualTransform(obj, data);
                    continue;
                }

                // Target Box 알 모델: PinataBoxView + eggColors 있으면 알 격자 방식으로 재배치.
                if (data.gimmickType == GimmickPinataBox)
                {
                    var pbView = obj.GetComponentInChildren<PinataBoxView>(true);
                    if (pbView != null && data.eggColors != null && data.eggColors.Length > 0)
                    {
                        ApplyPinataBoxVisual(obj, data, pbView);
                        continue;
                    }
                }

                if (data.gimmickType == GimmickPinata || data.gimmickType == GimmickPinataBox
                    || (data.gimmickType == GimmickWall && (data.sizeW > 1 || data.sizeH > 1)))
                {
                    // multi-cell Wall(2×2/3×3) 도 footprint 스케일 유지 — 미포함 시 rest 스케일(1×)로 리셋되어 사이즈가 사라짐.
                    ApplySizedFieldVisualTransform(obj, data);
                    continue;
                }

                obj.transform.position = GetAdjustedBoardPosition(data.position);
                obj.transform.localScale = GetBalloonRestScale(scaleMult);
            }

            _frameCachedPositions.Clear();
            _frameCachedPositionsFrame = -1;
            _lastCacheRefreshTime = 0f;
        }

        private void OnEnable()
        {
            EventBus.Subscribe<OnLevelLoaded>(HandleLevelLoaded);
            EventBus.Subscribe<OnBalloonPopped>(CheckKeysOnPop);
        }

        private void OnDisable()
        {
            EventBus.Unsubscribe<OnLevelLoaded>(HandleLevelLoaded);
            EventBus.Unsubscribe<OnBalloonPopped>(CheckKeysOnPop);
        }

#if UNITY_EDITOR
        /// <summary>에디터 전용: GameManager 배율 변경 시 풍선 위치/스케일 실시간 갱신.</summary>
        private float _prevWidthMult = 1f, _prevHeightMult = 1f, _prevZOffset = 0f;
        private void Update()
        {
            if (!GameManager.HasInstance) return;
            float wm = GameManager.Instance.Board.balloonFieldWidthMult;
            float hm = GameManager.Instance.Board.balloonFieldHeightMult;
            float zo = GameManager.Instance.Board.balloonGridZOffset;
            if (Mathf.Approximately(wm, _prevWidthMult) && Mathf.Approximately(hm, _prevHeightMult) && Mathf.Approximately(zo, _prevZOffset)) return;
            _prevWidthMult = wm;
            _prevHeightMult = hm;
            _prevZOffset = zo;
            RefreshAllBalloonTransforms();
        }

        private void RefreshAllBalloonTransforms()
        {
            CalculateLevelSafeMult();
            // ROLLBACK_EDITOR_SIZED_GIMMICK_REFRESH:
            // Reuse the runtime path so Pinata/Pinata_Box/Barricade keep their sized transforms
            // when editor board multipliers change.
            ReapplyAllBalloonVisualTransforms();
        }
#endif

        #endregion

        #region Public Methods

        /// <summary>
        /// Sets up the balloon board from a layout data list.
        /// Clears any existing balloons first.
        /// </summary>
        /// <param name="layout">List of balloon setup entries defining initial board state.</param>
        /// <param name="levelId">Level identifier for event publishing.</param>
        public void SetupBalloons(List<BalloonSetupData> layout, int levelId)
        {
            ClearAllBalloons();
            _currentLevelId = levelId;
            SyncBoardMetricsForLevelSetup();

            if (layout == null || layout.Count == 0)
            {
                Debug.LogWarning("[BalloonController] SetupBalloons called with empty or null layout.");
                return;
            }

            foreach (BalloonSetupData entry in layout)
            {
                SpawnBalloonFromSetup(entry);
            }

            // FlexTube 그룹 단위 prefab 인스턴스화 — 모든 BalloonData 등록 직후, 자식 부품 GameObject 를 _balloonObjects 에 매핑.
            BuildFlexTubes(layout);

            // ROLLBACK_GIMMICK_LEVEL_METRICS_REAPPLY:
            // Level-specific safe multipliers require the full balloon data set. Spawn first,
            // calculate once, then re-apply visuals so sized gimmicks do not keep stale level
            // offsets/scales from the previous board.
            CalculateLevelSafeMult();
            ReapplyAllBalloonVisualTransforms();

            // Wall / FlexTube cells don't count toward clear condition
            int excludeCount = 0;
            foreach (BalloonData d in _balloons.Values)
            {
                if (d.gimmickType == GimmickWall) excludeCount++;
                else if (d.gimmickType == GimmickFlexTube) excludeCount++;
            }
            RemainingCount = _balloons.Count - excludeCount;
            PoppedCount = 0;
            BuildPositionIndex();

            // Apply gimmick visual states after all balloons are placed
            ApplyInitialHiddenState();
            ApplyInitialIceState();
            ApplyInitialFrozenDartState();
            ApplyInitialColorCurtainState();

            // [#13/§11] Ice 영역(인접 연결 성분) 그룹핑 + 영역별 공유 HP 확정. ice 등록·위치 인덱스 완료 후.
            if (GimmickProcessor.HasInstance)
                GimmickProcessor.Instance.InitIceRegions();

            // 레벨별 안전 배율 계산 (벨트 초과 레벨만 축소)
            CalculateLevelSafeMult();

            // [Outline 2026-05-10] 맵 세팅 직후 외곽 풍선만 outline 적용 자동 트리거.
            // throttle reset 으로 첫 호출 보장. _outlineOnOuterOnly = false 면 RefreshOutermostRendererState 가 즉시 return.
            // 롤백: 아래 두 라인 제거.
            _lastOutermostRefreshTime = -1f;
            RefreshOutermostRendererState();
        }

        /// <summary>
        /// Returns a snapshot copy of BalloonData for the given balloonId.
        /// Returns null if not found or already popped.
        /// </summary>
        public BalloonData GetBalloon(int balloonId)
        {
            if (_balloons.TryGetValue(balloonId, out BalloonData data))
            {
                return data;
            }
            return null;
        }

        /// <summary>Frame 단위 position cache — transform.position 호출 N×M → 1×N 으로 절감.</summary>
        private readonly Dictionary<int, Vector3> _frameCachedPositions = new Dictionary<int, Vector3>(512);
        private int _frameCachedPositionsFrame = -1;

        /// <summary>풍선 위치 cache. 풍선 정적 가정 (spawn 후 안 움직임) — 0.1s 마다 갱신.
        /// 이전: 매 frame 1620 iterate × transform.position 호출 = 20%+ 부하 (Profiler 측정).
        /// 변경: 0.1s throttle — 평균 부하 14배 절감. 풍선 정적이라 0.1s stale OK.</summary>
        private float _lastCacheRefreshTime;
        private const float CACHE_REFRESH_INTERVAL = 0.1f;

        private void RefreshFramePositionCacheIfNeeded()
        {
            float now = Time.unscaledTime;
            // 첫 호출 또는 0.1s 경과 시만 rebuild — 풍선이 정적이라 stale 영향 미미.
            if (_frameCachedPositions.Count > 0 && now - _lastCacheRefreshTime < CACHE_REFRESH_INTERVAL) return;
            _lastCacheRefreshTime = now;

            _frameCachedPositions.Clear();
            var en = _balloonObjects.GetEnumerator();
            try
            {
                while (en.MoveNext())
                {
                    var obj = en.Current.Value;
                    if (obj != null) _frameCachedPositions[en.Current.Key] = obj.transform.position;
                }
            }
            finally { en.Dispose(); }
        }

        /// <summary>풍선의 실제 월드 위치 (배율/오프셋 적용 후). 오브젝트 없으면 데이터 위치 반환.
        /// 정확한 위치 — 매 호출 transform.position 직접. 다트 발사 등 정밀 위치 필요한 곳 사용.</summary>
        public Vector3 GetBalloonWorldPosition(int balloonId)
        {
            if (_balloonObjects.TryGetValue(balloonId, out GameObject obj) && obj != null)
                return obj.transform.position;
            if (_balloons.TryGetValue(balloonId, out BalloonData data))
                return data.position;
            return Vector3.zero;
        }

        public Vector3 GetAdjustedBoardPosition(Vector3 position)
        {
            Vector3 adjustedPos = position;
            if (GameManager.HasInstance)
            {
                float cx = GameManager.Instance.Board.boardCenterX;
                float cz = GameManager.Instance.Board.balloonCenterZ;
                float wm = _levelSafeCalculated ? _levelSafeWm : GameManager.Instance.Board.balloonFieldWidthMult;
                float hm = _levelSafeCalculated ? _levelSafeHm : GameManager.Instance.Board.balloonFieldHeightMult;
                float zOffset = GameManager.Instance.Board.balloonGridZOffset;
                adjustedPos.x = cx + (position.x - cx) * wm;
                adjustedPos.z = cz + (position.z - cz) * hm + zOffset + _levelSafeZShift;
            }
            return adjustedPos;
        }

        public void GetAdjustedCellSize(out float cellSizeX, out float cellSizeZ)
        {
            GetFieldVisualMetrics(out float widthMult, out float heightMult, out _, out cellSizeX, out cellSizeZ, out _);
        }

        /// <summary>풍선 월드 위치 — frame 단위 cache. 같은 frame 안에서 N번 호출돼도 transform.position 은 frame 당 1회만.
        /// 1 frame stale 허용되는 hot path 전용 (FindTarget candidates loop 등 — perp tolerance 가 cellSpacing 100% 라 stale 영향 없음).
        /// 정확한 위치 필요하면 GetBalloonWorldPosition() 사용.</summary>
        public Vector3 GetBalloonWorldPositionCached(int balloonId)
        {
            RefreshFramePositionCacheIfNeeded();
            if (_frameCachedPositions.TryGetValue(balloonId, out Vector3 cached))
                return cached;
            // Cache 미스 — fallback to direct.
            return GetBalloonWorldPosition(balloonId);
        }

        /// <summary>
        /// Active(non-popped) 풍선의 world position 을 caller-provided list 에 채워줌.
        /// Frame cache 활용 — 같은 frame 안에서 BuildOccupancyMap 등 다른 호출이 있었으면 0 transform 호출.
        /// </summary>
        public void GetActivePositions(List<Vector3> output)
        {
            if (output == null) return;
            output.Clear();
            RefreshFramePositionCacheIfNeeded();
            foreach (var kvp in _frameCachedPositions)
                output.Add(kvp.Value);
        }

        /// <summary>풍선 오브젝트의 현재 localScale. 오브젝트 없으면 추정 스케일 반환.</summary>
        public Vector3 GetBalloonWorldScale(int balloonId)
        {
            if (_balloonObjects.TryGetValue(balloonId, out GameObject obj) && obj != null)
                return obj.transform.localScale;
            float scaleMult = _levelSafeCalculated
                ? Mathf.Max(_levelSafeWm, _levelSafeHm)
                : (GameManager.HasInstance ? Mathf.Max(GameManager.Instance.Board.balloonFieldWidthMult, GameManager.Instance.Board.balloonFieldHeightMult) : 1f);
            return GetBalloonRestScale(scaleMult);
        }

        // 밀집 레벨 안전 배율(scaleMult)로 풍선이 축소돼도 화면 높이(Y, 월드 업)는 유지.
        // footprint(X/Z)만 축소해 제거 시 pop 피드백이 납작해지지 않게 한다.
        private Vector3 GetBalloonRestScale(float scaleMult)
            => new Vector3(_balloonScale * scaleMult, _balloonScale, _balloonScale * scaleMult);

        /// <summary>
        /// Returns all non-popped balloons matching the specified color.
        /// Hidden balloons whose color is concealed are excluded until revealed.
        /// </summary>
        // 재사용 리스트 (GC 할당 방지)
        private readonly List<BalloonData> _reusableColorResult = new List<BalloonData>(64);

        public BalloonData[] GetBalloonsByColor(int color)
        {
            _reusableColorResult.Clear();
            foreach (KeyValuePair<int, BalloonData> pair in _balloons)
            {
                BalloonData data = pair.Value;
                if (data.isPopped) continue;
                if (_hiddenBalloons.Contains(data.balloonId)) continue;
                if (data.gimmickType == GimmickWall) continue;
                if (data.gimmickType == GimmickIce) continue;
                if (data.gimmickType == GimmickColorCurtain) continue;
                if (data.gimmickType == GimmickLockKey) continue;
                if (data.color == color)
                    _reusableColorResult.Add(data);
            }
            return _reusableColorResult.ToArray();
        }

        /// <summary>
        /// Returns all non-popped balloons matching the specified color, INCLUDING gimmick
        /// balloons (Wall, Pin, Ice, Hidden, etc.). Used by the Color Remove booster so that
        /// every balloon of the chosen color is cleared regardless of gimmick state.
        /// </summary>
        /// <summary>외곽 풍선만 Outline pass 활성. 매 pop 시 호출되니까 throttle + 외곽 diff 적용.
        /// 첫 호출: 모든 풍선 0 + 외곽 set 1. 이후: 이전 외곽 vs 새 외곽 diff 만 update.</summary>
        private static readonly int _propBalloonOutlineEnabled = Shader.PropertyToID("_OutlineEnabled");
        private static MaterialPropertyBlock _balloonMpb;
        private readonly HashSet<int> _prevOutermostSet = new HashSet<int>();
        private readonly List<int> _outerDiffBuffer = new List<int>(64);
        private bool _hasAppliedOutermostOutline;
        private float _lastOutermostRefreshTime;

        public void RefreshOutermostRendererState()
        {
            // [Outline 2026-05-10] 가드 제거 — Lobby/MapMaker/직접 InGame 진입 등 다양한 경로에서 inspector toggle 의존 없이 항상 작동.
            // 롤백: 아래 주석 라인 해제.
            // if (!_outlineOnOuterOnly) return;

            // 사용자 보고: 매 pop 시 1620 iterate spike → throttle.
            // 0.1s 안 중복 호출은 skip — 마지막 pop 후 한 번만 정리.
            float now = Time.unscaledTime;
            if (now - _lastOutermostRefreshTime < 0.1f) return;
            _lastOutermostRefreshTime = now;

            if (_balloonMpb == null) _balloonMpb = new MaterialPropertyBlock();

            // [Outline 2026-05-10] 사용자 의도 정정 — outline = "공격 가능한 풍선 (DirectionalTargeting contour)" 이지
            // BoardStateManager.IsOutermost (Wall/Ice 포함 단순 외곽) 가 아님.
            // DirectionalTargeting.GetAttackableContourIds 가 외곽 + targetable + targetable 풍선 ID 집합 반환.
            // 롤백: 아래 contour 사용 분기 제거 + 주석 처리된 원본 BoardStateManager 분기 복원.
            // ── 원본 BoardStateManager.IsOutermost 분기 ──
            // if (!BoardStateManager.HasInstance) return;
            // var bsm = BoardStateManager.Instance;
            // bsm.IsOutermost(0);  // dummy 로 cache 갱신
            // _outerDiffBuffer.Clear();
            // var en = _balloonObjects.GetEnumerator();
            // try {
            //     while (en.MoveNext()) {
            //         int id = en.Current.Key;
            //         if (bsm.IsOutermost(id)) _outerDiffBuffer.Add(id);
            //     }
            // } finally { en.Dispose(); }

            // [Outline 2026-05-10 fix] diff 방식 → 전수 처리 변경.
            // 이전 diff 방식은 _prevOutermostSet 비어있는 첫 호출 시 비외곽 풍선들에 _OutlineEnabled=0 적용 못함 →
            // mat default (=1) 그대로 → 모든 풍선 outline 보임. 사용자 보고 "Outline On Outer Only ON 인데 모두 outline".
            // 전수 sweep 으로 매 호출 시 모든 풍선에 정확한 값 적용 보장.
            // 비용: _balloonObjects.Count × O(1) HashSet lookup × 0.1s throttle → 부담 작음.
            // 롤백: 아래 sweep 코드 제거 + 위 주석 처리된 원본 diff 코드 복원.
            float __contourStamp = InGamePerfLogger.StartStampMs();
            HashSet<int> contourSet = null;
            var contourCol = DirectionalTargeting.GetAttackableContourIds();
            if (contourCol is HashSet<int> hs) contourSet = hs;
            InGamePerfLogger.EndSection(__contourStamp, "Balloon.RefreshOutline.BuildContour");

            float __applyStamp = InGamePerfLogger.StartStampMs();
            _outerDiffBuffer.Clear();
            if (!_hasAppliedOutermostOutline)
            {
                // ROLLBACK_OUTLINE_DIFF_APPLY:
                // First pass must sweep every balloon because material defaults may have outline on.
                var en = _balloonObjects.GetEnumerator();
                try
                {
                    while (en.MoveNext())
                    {
                        int id = en.Current.Key;
                        bool isContour = contourSet != null
                            ? contourSet.Contains(id)
                            : ContainsContour(contourCol, id);
                        ApplyOutlineToBalloon(id, isContour);
                        if (isContour) _outerDiffBuffer.Add(id);
                    }
                }
                finally { en.Dispose(); }
                _hasAppliedOutermostOutline = true;
            }
            else
            {
                foreach (int id in contourCol)
                {
                    if (!_prevOutermostSet.Contains(id))
                        ApplyOutlineToBalloon(id, true);
                    _outerDiffBuffer.Add(id);
                }

                foreach (int id in _prevOutermostSet)
                {
                    bool stillContour = contourSet != null
                        ? contourSet.Contains(id)
                        : ContainsContour(contourCol, id);
                    if (!stillContour)
                        ApplyOutlineToBalloon(id, false);
                }
            }

            // _prevOutermostSet 갱신 (호환 유지 — 외부에서 읽는 코드 있을 수 있음)
            _prevOutermostSet.Clear();
            for (int i = 0; i < _outerDiffBuffer.Count; i++) _prevOutermostSet.Add(_outerDiffBuffer[i]);
            InGamePerfLogger.EndSection(__applyStamp, "Balloon.RefreshOutline.ApplyRenderers");
        }

        private static bool ContainsContour(IReadOnlyCollection<int> col, int id)
        {
            foreach (int c in col) if (c == id) return true;
            return false;
        }

        private void ApplyOutlineToBalloon(int balloonId, bool enableOutline)
        {
            if (!_balloonObjects.TryGetValue(balloonId, out GameObject obj) || obj == null) return;
            if (!_balloonRenderers.TryGetValue(balloonId, out Renderer[] cachedRenderers))
            {
                cachedRenderers = obj.GetComponentsInChildren<Renderer>(includeInactive: true);
                _balloonRenderers[balloonId] = cachedRenderers;
            }
            float v = enableOutline ? 1f : 0f;
            for (int r = 0; r < cachedRenderers.Length; r++)
            {
                var rend = cachedRenderers[r];
                if (rend == null) continue;
                rend.GetPropertyBlock(_balloonMpb);
                _balloonMpb.SetFloat(_propBalloonOutlineEnabled, v);
                rend.SetPropertyBlock(_balloonMpb);
            }
        }

        /// <summary>재사용 리스트 — GetAllBalloonsByColor GC 방지</summary>
        private readonly List<BalloonData> _reusableColorList = new List<BalloonData>(256);

        public BalloonData[] GetAllBalloonsByColor(int color)
        {
            _reusableColorList.Clear();
            foreach (KeyValuePair<int, BalloonData> pair in _balloons)
            {
                BalloonData data = pair.Value;
                if (data.isPopped) continue;
                if (data.gimmickType == GimmickLockKey) continue;
                if (data.color == color)
                    _reusableColorList.Add(data);
            }
            return _reusableColorList.ToArray();
        }

        // 이어하기 랜덤 풍선 제거용 후보 id 버퍼 (재사용). balloonId 는 안정적이라
        // pop 중 발생하는 이벤트가 _reusableColorList 를 건드려도 영향 없음.
        private List<int> _continueColorBalloonIds;

        /// <summary>
        /// 이어하기 전용: 지정 색 풍선을 '랜덤'하게 최대 count 개 pop. 실제 pop 개수 반환.
        /// LockKey / FlexTube 기믹은 force-pop 대상에서 제외 (있는 만큼만).
        /// </summary>
        public int PopRandomBalloonsByColor(int color, int count)
        {
            if (count <= 0) return 0;

            if (_continueColorBalloonIds == null) _continueColorBalloonIds = new List<int>(64);
            _continueColorBalloonIds.Clear();
            foreach (KeyValuePair<int, BalloonData> pair in _balloons)
            {
                BalloonData data = pair.Value;
                if (data.isPopped) continue;
                if (data.gimmickType == GimmickLockKey) continue;
                if (data.gimmickType == GimmickFlexTube) continue;
                if (data.color == color) _continueColorBalloonIds.Add(data.balloonId);
            }

            int available = _continueColorBalloonIds.Count;
            int toPop = Mathf.Min(count, available);
            if (toPop <= 0) return 0;

            // Fisher-Yates 부분 셔플 — 앞 toPop 개를 무작위로 선정.
            for (int i = 0; i < toPop; i++)
            {
                int j = UnityEngine.Random.Range(i, available);
                int tmp = _continueColorBalloonIds[i];
                _continueColorBalloonIds[i] = _continueColorBalloonIds[j];
                _continueColorBalloonIds[j] = tmp;
            }

            int popped = 0;
            for (int i = 0; i < toPop; i++)
            {
                int id = _continueColorBalloonIds[i];
                if (!_balloons.TryGetValue(id, out BalloonData bd) || bd.isPopped) continue;
                ForcePopBalloon(id);
                popped++;
            }
            return popped;
        }

        /// <summary>재사용 배열 — GetAllBalloons GC 방지</summary>
        private BalloonData[] _reusableAllBalloons;

        /// <summary>
        /// Returns all balloon data entries (including popped), for board state inspection.
        /// </summary>
        public BalloonData[] GetAllBalloons()
        {
            if (_reusableAllBalloons == null || _reusableAllBalloons.Length != _balloons.Count)
                _reusableAllBalloons = new BalloonData[_balloons.Count];

            int i = 0;
            foreach (BalloonData d in _balloons.Values)
            {
                _reusableAllBalloons[i++] = d;
            }
            return _reusableAllBalloons;
        }

        /// <summary>
        /// Returns the current count of non-popped balloons.
        /// </summary>
        public int GetRemainingCount()
        {
            return RemainingCount;
        }

        /// <summary>
        /// Attempts to pop a balloon by id. Applies gimmick behavior before/after pop.
        /// </summary>
        /// <returns>PopResult describing outcome and any side effects.</returns>
        public PopResult PopBalloon(int balloonId)
        {
            if (!_balloons.TryGetValue(balloonId, out BalloonData data))
            {
                return new PopResult { success = false, reason = "NotFound" };
            }

            if (data.isPopped)
            {
                return new PopResult { success = false, reason = "AlreadyPopped" };
            }

            // Delegate pre-pop guard check to GimmickProcessor
            if (GimmickProcessor.HasInstance)
            {
                string blockReason = GimmickProcessor.Instance.CheckDartBlocker(data.balloonId, data.gimmickType, -1);
                if (blockReason != null)
                {
                    return new PopResult { success = false, reason = blockReason, balloonId = data.balloonId, gimmickType = data.gimmickType };
                }
            }
            else
            {
                // Fallback: Wall/Ice always blocked
                if (data.gimmickType == GimmickWall)
                    return new PopResult { success = false, reason = "Wall: indestructible", balloonId = data.balloonId, gimmickType = GimmickWall };
                if (data.gimmickType == GimmickIce)
                    return new PopResult { success = false, reason = "Ice: indirect only", balloonId = data.balloonId, gimmickType = GimmickIce };
                if (data.gimmickType == GimmickColorCurtain)
                    return new PopResult { success = false, reason = "ColorCurtain: indirect only", balloonId = data.balloonId, gimmickType = GimmickColorCurtain };
            }

            // Pin: same-color dart progressive removal
            if (data.gimmickType == GimmickPin && GimmickProcessor.HasInstance)
            {
                // dartColor is unknown here — caller should use PopBalloonWithDart for Pin
                return new PopResult { success = false, reason = "Pin: use PopBalloonWithDart", balloonId = data.balloonId, gimmickType = GimmickPin };
            }

            // Frozen Dart: 2-hit field gimmick (1st hit = thaw, 2nd hit = pop)
            if (data.gimmickType == GimmickFrozenDart)
            {
                return ProcessFrozenDartHit(data);
            }

            // Barricade: destructible wall with HP
            if (data.gimmickType == GimmickBarricade)
            {
                return ProcessBarricadeHit(data);
            }

            // FlexTube: dartColor 없는 경로(force-pop 시도, indirect)는 무조건 reject — 색 검증 필요.
            if (data.gimmickType == GimmickFlexTube)
            {
                return new PopResult { success = false, reason = "FlexTube: requires dart color", balloonId = data.balloonId, gimmickType = GimmickFlexTube };
            }

            // Pinata and Pinata Box require multiple hits
            if (data.gimmickType == GimmickPinata || data.gimmickType == GimmickPinataBox)
            {
                return ProcessPinataHit(data);
            }

            // Standard pop
            return ExecutePop(data);
        }

        /// <summary>
        /// Returns the remaining non-popped balloon count.
        /// </summary>
        public int GetRemainingBalloonCount()
        {
            return RemainingCount;
        }

        /// <summary>
        /// Pops a balloon with dart color context. Required for Pin (same-color progressive)
        /// and ColorCurtain (specific color required).
        /// </summary>
        public PopResult PopBalloonWithDart(int balloonId, int dartColor)
        {
            if (!_balloons.TryGetValue(balloonId, out BalloonData data))
                return new PopResult { success = false, reason = "NotFound" };

            // FlexTube: isPopped 가드 이전에 처리 — stale target(이미 비활성 cell 향한 다트) 도 부모 FlexTube 에 위임.
            // FlexTube 가 _destroying / 활성 segment 유무 / 색 매칭 자체 판단. 다트는 hit 인정 후 소진.
            if (data.gimmickType == GimmickFlexTube)
            {
                // _balloonObjects 에서 자식 GameObject 가 제거됐어도(MarkFlexTubeCellInactive),
                // _flexTubeRoots 안 FlexTube 컴포넌트로 직접 위임 (groupId 매칭).
                IDartHittable hittable = null;
                if (_balloonObjects.TryGetValue(balloonId, out GameObject ftObj) && ftObj != null)
                    hittable = ftObj.GetComponent<IDartHittable>();
                if (hittable == null)
                {
                    // Fallback — _flexTubeRoots 안에서 같은 groupId 의 FlexTube 찾기.
                    for (int i = 0; i < _flexTubeRoots.Count; i++)
                    {
                        var root = _flexTubeRoots[i];
                        if (root == null) continue;
                        var ft = root.GetComponent<FlexTube>();
                        if (ft != null && ft.GroupId == data.flexTubeGroupId)
                        {
                            hittable = ft as IDartHittable;
                            // FlexTube 자체는 IDartHittable 안 구현. FlexTubePart 부품 중 활성된 것 하나 찾아서 위임.
                            foreach (var p in ft.Parts)
                            {
                                if (p != null && p.gameObject.activeSelf)
                                {
                                    hittable = p;
                                    break;
                                }
                            }
                            break;
                        }
                    }
                }
                if (hittable != null) hittable.OnDartHit(dartColor);
                return new PopResult { success = false, hitAccepted = true, reason = "FlexTube: delegated to owner", balloonId = data.balloonId, gimmickType = GimmickFlexTube };
            }

            if (data.isPopped)
                return new PopResult { success = false, reason = "AlreadyPopped" };

            // GimmickProcessor pre-pop guard with dart color
            if (GimmickProcessor.HasInstance)
            {
                string blockReason = GimmickProcessor.Instance.CheckDartBlocker(data.balloonId, data.gimmickType, dartColor);
                if (blockReason != null)
                    return new PopResult { success = false, reason = blockReason, balloonId = data.balloonId, gimmickType = data.gimmickType };
            }

            // Pin: same-color dart progressive removal
            if (data.gimmickType == GimmickPin && GimmickProcessor.HasInstance)
            {
                bool destroyed = GimmickProcessor.Instance.ProcessPinHit(data.balloonId, dartColor, data.color);
                UpdatePinVisual(data.balloonId, destroyed);
                if (!destroyed)
                    return new PopResult { success = false, hitAccepted = true, reason = "Pin: segment removed, not fully destroyed", balloonId = data.balloonId, gimmickType = GimmickPin };
                return ExecutePop(data);
            }

            // Frozen Dart: 2-hit field gimmick
            if (data.gimmickType == GimmickFrozenDart)
                return ProcessFrozenDartHit(data);

            // [ROLLBACK_PIN_BARRICADE_MERGE]
            // Barricade 가 Pin mechanic (색 매칭 + segment 점진 제거) 사용 + Barricade visual transform.
            // 롤백 시 이 블록 제거 + 아래 ProcessBarricadeHit 분기 복원.
            if (data.gimmickType == GimmickBarricade && GimmickProcessor.HasInstance)
            {
                bool destroyed = GimmickProcessor.Instance.ProcessPinHit(data.balloonId, dartColor, data.color);
                UpdateBarricadeVisualAfterHit(data, destroyed);
                if (!destroyed)
                    return new PopResult { success = false, hitAccepted = true, reason = "Barricade: segment removed, not fully destroyed", balloonId = data.balloonId, gimmickType = GimmickBarricade };
                return ExecutePop(data);
            }

            // Target Box 알 모델: 다트 색과 같은 색 알의 HP 차감, 해당 알 0이면 제거, 전부 제거 시 박스 파괴.
            if (data.gimmickType == GimmickPinataBox
                && data.eggColors != null && data.eggHps != null && data.eggColors.Length > 0)
                return ProcessPinataBoxEggHit(data, dartColor);

            // Pinata/PinataBox (legacy 단일 박스)
            if (data.gimmickType == GimmickPinata || data.gimmickType == GimmickPinataBox)
                return ProcessPinataHit(data);

            return ExecutePop(data);
        }

        // Target Box 알 모델 타격: 같은 색 살아있는 알 1개의 HP -1 → 0 이면 시각 제거(HideEgg) → 전부 제거 시 박스 pop.
        private PopResult ProcessPinataBoxEggHit(BalloonData data, int dartColor)
        {
            int[] colors = data.eggColors;
            int[] hps = data.eggHps;

            int target = -1;
            for (int i = 0; i < colors.Length; i++)
                if (colors[i] == dartColor && i < hps.Length && hps[i] > 0) { target = i; break; }

            // 박스에 없는 색 다트 — 타격 무효 (타겟팅이 정상이면 거의 발생 안 함).
            if (target < 0)
                return new PopResult { success = false, reason = "TargetBox: no live egg of color", balloonId = data.balloonId, gimmickType = GimmickPinataBox };

            hps[target] = Mathf.Max(0, hps[target] - 1);

            // 비주얼: egg 항목별 1:1. HP 반영 — 절반 이하면 균열(texture) 활성, 0 이면 알 제거.
            bool eggDied = hps[target] == 0;
            if (_balloonObjects.TryGetValue(data.balloonId, out GameObject obj) && obj != null)
            {
                var view = obj.GetComponentInChildren<PinataBoxView>(true);
                if (view != null) view.UpdateEggHp(target, hps[target]);
            }

            bool anyAlive = false;
            for (int i = 0; i < hps.Length; i++) if (hps[i] > 0) { anyAlive = true; break; }

            if (!anyAlive)
                return ExecutePop(data); // 모든 알 제거 → 박스 파괴

            // 알 1개가 죽었으면 그 색 셀이 조준 대상에서 빠질 수 있음 → 타겟팅 재빌드.
            if (eggDied)
                DirectionalTargeting.InvalidateCache();

            // 알 1개 제거됐지만 박스는 유지 — 다트는 hit 인정(소진).
            return new PopResult { success = false, hitAccepted = true, reason = "TargetBox: egg removed", balloonId = data.balloonId, color = dartColor, gimmickType = GimmickPinataBox };
        }

        private void UpdatePinVisual(int balloonId, bool destroyed)
        {
            if (!_balloonObjects.TryGetValue(balloonId, out GameObject hitObj) || hitObj == null)
                return;

            var gi = hitObj.GetComponent<GimmickIdentifier>();
            if (gi == null) return;

            int remaining = 0;
            if (!destroyed && GimmickProcessor.HasInstance)
                remaining = Mathf.Max(0, GimmickProcessor.Instance.GetPinRemainingSegments(balloonId));

            gi.UpdateHP(remaining);
            gi.PlayHitEffect();
            if (destroyed)
                gi.PlayEndEffect();
        }

        // [ROLLBACK_PIN_BARRICADE_MERGE]
        // Pin mechanic + Barricade visual 통합. HP 텍스트만 차감 (Pin 처럼) + Barricade visual transform 은 spawn 시점 그대로 유지.
        // 롤백 시 이 메서드 + PopBalloonWithDart Barricade 분기의 호출 제거.
        private void UpdateBarricadeVisualAfterHit(BalloonData data, bool destroyed)
        {
            if (!_balloonObjects.TryGetValue(data.balloonId, out GameObject hitObj) || hitObj == null)
                return;

            var gi = hitObj.GetComponent<GimmickIdentifier>();
            if (gi == null) return;

            int remaining = 0;
            if (!destroyed && GimmickProcessor.HasInstance)
                remaining = Mathf.Max(0, GimmickProcessor.Instance.GetPinRemainingSegments(data.balloonId));

            gi.UpdateHP(remaining);
            gi.PlayHitEffect();
            if (destroyed)
                gi.PlayEndEffect();
        }

        /// <summary>
        /// Force-pops a balloon by ID (used by GimmickProcessor for indirect removal like Ice).
        /// Bypasses gimmick guards.
        /// </summary>
        public void ForcePopBalloon(int balloonId)
        {
            if (!_balloons.TryGetValue(balloonId, out BalloonData data)) return;
            if (data.isPopped) return;
            if (data.gimmickType == GimmickLockKey) return;
            if (data.gimmickType == GimmickFlexTube) return; // FlexTube 는 같은 색 다트로만 제거 가능 — indirect/force-pop 차단.
            ExecutePop(data);
        }

        /// <summary>
        /// FlexTube 의 비활성된 Segment cell 을 다트 target 후보에서 제외 — _balloons.isPopped=true 마킹 + _balloonObjects 에서 분리.
        /// Pop 점수/이벤트 발화 없이 silent 제거 (FlexTube 는 RemainingCount 영향 제외).
        /// </summary>
        public void MarkFlexTubeCellInactive(int balloonId)
        {
            if (!_balloons.TryGetValue(balloonId, out BalloonData data)) return;
            if (data.gimmickType != GimmickFlexTube) return;
            if (data.isPopped) return;
            data.isPopped = true;
            _balloons[balloonId] = data;
            // _balloonObjects 는 유지 — PopBalloonWithDart FlexTube 분기가 stale target 일 때도 IDartHittable 위임 가능하게.
            // (단 FlexTube.OnDartHit 가 _destroying / 활성 segment 유무 자체 판단.)
            _frameCachedPositions.Remove(balloonId);
            // _positionIndex 는 Vector3Int → balloonId 매핑이라 reverse lookup 필요 — 해당 entry 제거.
            Vector3Int? keyToRemove = null;
            foreach (var kv in _positionIndex)
            {
                if (kv.Value == balloonId) { keyToRemove = kv.Key; break; }
            }
            if (keyToRemove.HasValue) _positionIndex.Remove(keyToRemove.Value);
        }

        /// <summary>
        /// Reveals a specific hidden balloon by removing its concealed state.
        /// Used by Hand booster. Returns true if a balloon was revealed.
        /// </summary>
        public bool RevealHiddenBalloon(int balloonId)
        {
            if (!_hiddenBalloons.Contains(balloonId)) return false;
            if (!_balloons.TryGetValue(balloonId, out BalloonData data)) return false;
            if (data.isPopped) return false;

            _hiddenBalloons.Remove(balloonId);

            if (_balloonObjects.TryGetValue(balloonId, out GameObject obj) && obj != null)
            {
                int colorIdx = Mathf.Clamp(data.color, 0, BalloonColors.Length - 1);
                ApplyTintToObject(obj, BalloonColors[colorIdx]);
                // [Leak fix 2026-05-11] SetLink(KillOnDisable) — pool 반환 시 SetActive(false) 에서 tween 자동 kill.
                // 원본: obj.transform.DOPunchScale(Vector3.one * 0.15f, 0.2f, 8, 0.5f);
                PlayRevealEffect(obj, colorIdx, balloonId);
            }

            // ROLLBACK_REVEAL_TARGET_CACHE_INVALIDATION:
            // A concealed Surprise/Hidden balloon is excluded from targeting. Revealing it must
            // rebuild the contour cache immediately, otherwise it can stay invisible to darts.
            DirectionalTargeting.InvalidateCache();
            RefreshOutermostRendererState();

            EventBus.Publish(new OnGimmickTriggered
            {
                gimmickType = GimmickSurprise, // 필드 풍선 색상 공개 = Surprise 기믹
                targetId    = balloonId
            });

            return true;
        }

        public bool IsBalloonConcealed(int balloonId)
        {
            return _hiddenBalloons.Contains(balloonId);
        }

        /// <summary>
        /// Returns one random hidden (Surprise) balloon ID, or -1 if none.
        /// Used by Hand booster to pick a target.
        /// </summary>
        public int GetRandomHiddenBalloonId()
        {
            if (_hiddenBalloons.Count == 0) return -1;

            // Pick a random one from the set
            int idx = Random.Range(0, _hiddenBalloons.Count);
            int i = 0;
            foreach (int id in _hiddenBalloons)
            {
                if (i == idx) return id;
                i++;
            }
            return -1;
        }

        /// <summary>
        /// Public accessor for adjacent balloon IDs (used by GimmickProcessor).
        /// </summary>
        public List<int> GetAdjacentBalloonIdsPublic(Vector3 position)
        {
            return GetAdjacentBalloonIds(position);
        }

        public List<int> GetAdjacentBalloonIdsForBalloonPublic(int balloonId, Vector3 fallbackPosition)
        {
            return GetAdjacentBalloonIdsForBalloon(balloonId, fallbackPosition);
        }

        /// <summary>
        /// Returns all non-popped balloons matching the given color index.
        /// Unlike GetBalloonsByColor, this returns a List and includes hidden balloons.
        /// Used by DirectionalTargeting and other gameplay systems.
        /// </summary>
        public List<BalloonData> GetActiveBalloonsByColor(int color)
        {
            List<BalloonData> result = new List<BalloonData>();
            foreach (KeyValuePair<int, BalloonData> pair in _balloons)
            {
                BalloonData data = pair.Value;
                if (data.isPopped) continue;
                if (data.gimmickType == GimmickLockKey) continue;
                if (data.color == color)
                {
                    result.Add(data);
                }
            }
            return result;
        }

        /// <summary>
        /// Returns all non-popped balloons regardless of color.
        /// Used by DirectionalTargeting to build the spatial grid for path calculations.
        /// </summary>
        public List<BalloonData> GetAllActiveBalloons()
        {
            List<BalloonData> result = new List<BalloonData>();
            foreach (KeyValuePair<int, BalloonData> pair in _balloons)
            {
                BalloonData data = pair.Value;
                if (!data.isPopped)
                {
                    result.Add(data);
                }
            }
            return result;
        }

        /// <summary>
        /// Clears all balloons and returns them to the pool.
        /// Called at level end or restart.
        /// </summary>
        public void ClearAllBalloons()
        {
            // [Leak fix 2026-05-11] 진행 중 coroutine 정리 — FlyKeyToLock / PopEffectPool.ReturnAfterDelay (runner=this) 등 stale 방지.
            // 누락 시 SetActive(false) 풍선의 transform.position 을 코루틴이 계속 갱신하거나, pool 반환 timing 어긋남.
            StopAllCoroutines();

            foreach (KeyValuePair<int, GameObject> pair in _balloonObjects)
            {
                if (pair.Value != null && ObjectPoolManager.HasInstance)
                {
                    // [Leak fix 2026-05-11] pool 반환 전 DOTween tween 정리.
                    // 진행 중인 Sequence (DOScale/DOPunchScale 등) 가 SetActive(false) 된 GameObject 의 transform 을
                    // 계속 update 하면서 DOTween internal active list 누적 → 매 frame DOTween.Update 비용 증가.
                    pair.Value.transform.DOKill();
                    // FlexTube 부품은 pool 외 (Instantiate 직접 생성) — 부모 FlexTube 와 함께 아래 _flexTubeRoots 루프에서 destroy.
                    if (_balloons.TryGetValue(pair.Key, out BalloonData bdFt) && bdFt.gimmickType == GimmickFlexTube)
                        continue;
                    // 기믹별 전용 풀로 반환 (없으면 기본 Balloon 풀)
                    string returnKey = PoolKey;
                    if (_balloons.TryGetValue(pair.Key, out BalloonData bd))
                    {
                        returnKey = ResolveGimmickPoolKey(bd.gimmickType);
                        if (bd.gimmickType == GimmickLockKey) returnKey = "Key";
                    }
                    ObjectPoolManager.Instance.Return(returnKey, pair.Value);
                }
            }

            // FlexTube 부모 일괄 destroy — 자식 FlexTubePart 들도 같이 정리됨.
            for (int i = 0; i < _flexTubeRoots.Count; i++)
            {
                if (_flexTubeRoots[i] != null) Destroy(_flexTubeRoots[i]);
            }
            _flexTubeRoots.Clear();

            // FrozenLayer 오버레이 자식들도 모두 풀로 반환
            ReturnAllFrozenOverlays();

            _balloons.Clear();
            _balloonObjects.Clear();
            _hiddenBalloons.Clear();
            _pinataGroup.Clear();
            _positionIndex.Clear();
            _multiCellOccupancy.Clear();
            _activeKeyPairIds.Clear();
            // [Leak fix 2026-05-11] _balloonRenderers / _prevOutermostSet / _frameCachedPositions 정리 추가.
            // 이전: ClearAllBalloons 가 _nextBalloonId=1 로 reset 하는데 이 dict 들이 stale entry 보유 →
            //       다음 레벨의 balloonId=1 풍선이 stale Renderer[] / outline state / cached position 과 collision.
            _balloonRenderers.Clear();
            _prevOutermostSet.Clear();
            _hasAppliedOutermostOutline = false;
            _frameCachedPositions.Clear();
            _frameCachedPositionsFrame = -1;
            RemainingCount = 0;
            PoppedCount = 0;
            _nextBalloonId = 1;

            // ROLLBACK_GIMMICK_LEVEL_SAFE_RESET:
            // Do not let the previous level's safe scale/shift affect the next level's first spawn.
            _levelSafeWm = 1f;
            _levelSafeHm = 1f;
            _levelSafeZShift = 0f;
            _levelSafeCalculated = false;
        }

        #endregion

        #region Private Methods — Setup

        private void SpawnBalloonFromSetup(BalloonSetupData entry)
        {
            int id = _nextBalloonId++;
            entry.gimmickType = GimmickDisplayName.Normalize(entry.gimmickType);

            // [ROLLBACK_LOCKKEY_DEPRECATE]
            // Lock_Key 기믹 dead 처리 — 기존 LevelData 호환을 위해 정규화: Lock_Key → none (일반 풍선).
            // 새 레벨에서 사용 안 함. 롤백 시 이 if 블록 제거.
            if (entry.gimmickType == GimmickLockKey)
            {
                entry.gimmickType = GimmickNone;
                entry.lockPairId = -1;
            }

            // [ROLLBACK_PIN_BARRICADE_MERGE]
            // Pin → Barricade 통합 (옵션 A): Pin mechanic (같은 색 다트만 점진 제거) + Barricade 시각.
            // SpawnBalloonFromSetup 진입 시 Pin gimmickType 을 Barricade 로 마이그. 기존 LevelData 호환.
            // 통합 후: Barricade visual prefab + 색 매칭 + segment 점진 제거 동작.
            // 롤백 시: 이 if 블록 제거 + GimmickProcessor.RegisterBalloonGimmick Barricade 케이스 원복 + PopBalloonWithDart Barricade 분기 원복.
            if (entry.gimmickType == GimmickPin)
            {
                entry.gimmickType = GimmickBarricade;
            }

            int resolvedHP = entry.hp > 0 ? entry.hp : PinataRequiredHits;
            BalloonData data = new BalloonData
            {
                balloonId   = id,
                color       = entry.color,
                position    = entry.position,
                isPopped    = false,
                gimmickType = string.IsNullOrEmpty(entry.gimmickType) ? GimmickNone : entry.gimmickType,
                hitCount    = 0,
                maxHP       = resolvedHP,
                sizeW       = entry.sizeW > 0 ? entry.sizeW : 1,
                sizeH       = entry.sizeH > 0 ? entry.sizeH : 1,
                iceBlockSize = entry.iceBlockSize > 0 ? entry.iceBlockSize : 1,
                // 알 배열: eggHps 는 런타임 차감되므로 clone (레벨 에셋 원본 보호). eggColors 는 read-only 라 공유.
                eggColors   = entry.eggColors,
                eggHps      = entry.eggHps != null ? (int[])entry.eggHps.Clone() : null,
                lockPairId  = entry.lockPairId,
                flexTubeGroupId       = entry.flexTubeGroupId,
                flexTubeSequenceIndex = entry.flexTubeSequenceIndex,
                flexTubePartType      = entry.flexTubePartType
            };

            _balloons[id] = data;

            // FlexTube cell — visual 은 BuildFlexTubes 가 spawn. 일반 풍선 풀에서 visual 가져오지 않음.
            // GimmickProcessor 등록도 skip (CheckDartBlocker 가 BalloonData 만으로 색 매칭).
            if (data.gimmickType == GimmickFlexTube)
            {
                return;
            }

            // Lock_Key: 풍선 대신 Key 프리팹을 셀에 독립 배치 (풀링)
            if (data.gimmickType == GimmickLockKey)
            {
                if (ObjectPoolManager.HasInstance)
                {
                    GameObject keyObj = ObjectPoolManager.Instance.Get("Key", entry.position, Quaternion.Euler(90f, 0f, 0f));
                    if (keyObj != null)
                    {
                        keyObj.SetActive(true);
                        keyObj.transform.localScale = Vector3.one * _balloonScale;
                        _balloonObjects[id] = keyObj;
                    }
                }
                _activeKeyPairIds[id] = data.lockPairId;
                Debug.Log($"[Key SETUP] Key {id} registered: pairId={data.lockPairId}, gimmick={data.gimmickType}");
            }
            else
            {
                // 일반 풍선/기믹 — 풀에서 오브젝트 생성
                GameObject obj = GetOrCreateBalloonObject(id, entry.position, entry.color);
                if (obj != null)
                {
                    _balloonObjects[id] = obj;

                    // Override visuals for special gimmick types
                    if (data.gimmickType == GimmickWall)
                    {
                        // ROLLBACK_WALL_IRONBOX_PREFAB:
                        // Wall/IronWall uses the IronBox visual; it is still indestructible.
                        ApplyTintToObject(obj, WALL_COLOR);
                        var gi = obj.GetComponent<GimmickIdentifier>();
                        if (gi != null)
                        {
                            gi.Initialize();
                            if (gi.HasColorRenderers)
                                gi.ApplyColor(WALL_COLOR);
                        }

                        // multi-cell Wall(2×2/3×3) 만 footprint 에 맞춰 시각 스케일/중앙정렬.
                        // 1×1 은 기존 기본 배치를 유지해 기존 레벨 Wall 외형 회귀 방지.
                        if (data.sizeW > 1 || data.sizeH > 1)
                            ApplySizedFieldVisualTransform(obj, data);
                    }
                    else if (data.gimmickType == GimmickPin)
                    {
                        // WoodenBoard 프리팹 사용 — Pin은 같은 색 다트로 점진 제거
                        ApplyTintToObject(obj, PIN_COLOR);
                        var gi = obj.GetComponent<GimmickIdentifier>();
                        if (gi != null)
                        {
                            gi.Initialize();
                            int ci = Mathf.Clamp(data.color, 0, BalloonColors.Length - 1);
                            gi.UpdateHP(data.maxHP > 0 ? data.maxHP : 3);
                            if (gi.HasColorRenderers)
                                gi.ApplyColor(BalloonColors[ci]);
                        }
                    }
                    else if (data.gimmickType == GimmickBarricade)
                    {
                        // [ROLLBACK_PIN_BARRICADE_MERGE]
                        // Pin → Barricade 통합 (옵션 A): Pin mechanic + Barricade visual.
                        // 색 적용을 WALL_COLOR(grey) 대신 BalloonColors[data.color] 로 변경 — Pin 처럼 색 매칭.
                        // 롤백 시 ApplyColor(WALL_COLOR) 로 원복.
                        var gi = obj.GetComponent<GimmickIdentifier>();
                        if (gi != null)
                        {
                            gi.Initialize();
                            int hp = data.maxHP - data.hitCount;
                            gi.UpdateHP(Mathf.Max(1, hp));
                            int ci = Mathf.Clamp(data.color, 0, BalloonColors.Length - 1);
                            if (gi.HasColorRenderers)
                                gi.ApplyColor(BalloonColors[ci]);
                        }

                        ApplyBarricadeVisualTransform(obj, data);
                    }
                    else if (data.gimmickType == GimmickPinataBox)
                    {
                        // Target Box 알 모델: eggColors + PinataBoxView 있으면 W×H 알 격자 생성(셀별 색).
                        var view = obj.GetComponentInChildren<PinataBoxView>(true);
                        if (view != null && data.eggColors != null && data.eggColors.Length > 0)
                        {
                            ApplyPinataBoxVisual(obj, data, view);
                        }
                        else
                        {
                            // 폴백(레거시/뷰 미부착): 단일 박스 — 기존 색+스케일.
                            var gi = obj.GetComponent<GimmickIdentifier>();
                            if (gi != null)
                            {
                                gi.Initialize();
                                int hp = data.maxHP - data.hitCount;
                                gi.UpdateHP(Mathf.Max(1, hp));
                                int ci = Mathf.Clamp(data.color, 0, BalloonColors.Length - 1);
                                if (gi.HasColorRenderers) gi.ApplyColor(BalloonColors[ci]);
                            }
                            ApplySizedFieldVisualTransform(obj, data);
                        }
                    }
                    else if (data.gimmickType == GimmickPinata)
                    {
                        var gi = obj.GetComponent<GimmickIdentifier>();
                        if (gi != null)
                        {
                            gi.Initialize();
                            int hp = data.maxHP - data.hitCount;
                            gi.UpdateHP(Mathf.Max(1, hp));
                            int ci = Mathf.Clamp(data.color, 0, BalloonColors.Length - 1);
                            if (gi.HasColorRenderers)
                                gi.ApplyColor(BalloonColors[ci]);
                        }

                        ApplySizedFieldVisualTransform(obj, data);
                    }
                }
            }

            // Register Pinata group membership
            if (data.gimmickType == GimmickPinata && entry.groupId >= 0)
            {
                if (!_pinataGroup.TryGetValue(entry.groupId, out List<int> group))
                {
                    group = new List<int>();
                    _pinataGroup[entry.groupId] = group;
                }
                group.Add(id);
            }

            // Surprise(Lv.101) + Hidden(필드 풍선 기믹) 모두 색상 은닉 → BalloonHidden.mat 적용
            // Hidden을 큐 기믹으로 쓰는 보관함은 별개로 HolderManager에서 처리
            if (data.gimmickType == GimmickSurprise || data.gimmickType == GimmickHidden)
            {
                _hiddenBalloons.Add(id);
            }

            if (GimmickProcessor.HasInstance)
            {
                GimmickProcessor.Instance.RegisterBalloonGimmick(
                    data.balloonId,
                    data.gimmickType,
                    data.color,
                    data.maxHP);
            }
        }

        /// <summary>
        /// FlexTube 그룹별 prefab 인스턴스화 — SetupBalloons 의 SpawnBalloonFromSetup 일괄 완료 직후 호출.
        /// 같은 flexTubeGroupId 의 BalloonData 들을 sequenceIndex 순으로 정렬 → FlexTube 부모 + StartCap/Segment/EndCap 자식 배치.
        /// 각 자식 GameObject 는 _balloonObjects[balloonId] 에 등록되어 다트 hit 시 IDartHittable(FlexTubePart) 진입점이 됨.
        /// HP = Segment 수 (Cap 제외), color = 첫 cell 색상.
        /// </summary>
        private void BuildFlexTubes(List<BalloonSetupData> layout)
        {
            // 그룹별 balloonId 수집 + sequenceIndex 정렬
            var groups = new Dictionary<int, List<int>>();
            foreach (var kv in _balloons)
            {
                var d = kv.Value;
                if (d.gimmickType != GimmickFlexTube) continue;
                if (d.flexTubeGroupId < 0) continue;
                if (!groups.TryGetValue(d.flexTubeGroupId, out var ids))
                {
                    ids = new List<int>();
                    groups[d.flexTubeGroupId] = ids;
                }
                ids.Add(d.balloonId);
            }

            if (groups.Count == 0) return;

            // prefab 일괄 로드 (Resources 캐시)
            GameObject flexTubePrefab = Resources.Load<GameObject>("Prefabs/FlexTube");
            GameObject startCapPrefab = Resources.Load<GameObject>("Prefabs/FlexTube_StartCap");
            GameObject segmentPrefab  = Resources.Load<GameObject>("Prefabs/FlexTube_Segment");
            GameObject endCapPrefab   = Resources.Load<GameObject>("Prefabs/FlexTube_EndCap");
            if (flexTubePrefab == null || startCapPrefab == null || segmentPrefab == null || endCapPrefab == null)
            {
                Debug.LogWarning("[BalloonController] FlexTube prefab(s) missing in Resources/Prefabs/. FlexTube spawn skipped.");
                return;
            }

            foreach (var kv in groups)
            {
                int groupId = kv.Key;
                var ids = kv.Value;
                ids.Sort((a, b) => _balloons[a].flexTubeSequenceIndex.CompareTo(_balloons[b].flexTubeSequenceIndex));

                // 디버그: 그룹별 cells 정보. spawn 누락 추적용.
                var sb = new System.Text.StringBuilder();
                sb.Append($"[FlexTube] Group {groupId}: {ids.Count} cells —");
                for (int k = 0; k < ids.Count; k++)
                {
                    var d = _balloons[ids[k]];
                    sb.Append($" [{k}: id={ids[k]} seq={d.flexTubeSequenceIndex} pos=({d.position.x:F2},{d.position.z:F2})]");
                }
                Debug.Log(sb.ToString());

                if (ids.Count < 2)
                {
                    Debug.LogWarning($"[FlexTube] Group {groupId}: cells={ids.Count} (need at least 2: StartCap + EndCap). Skipping.");
                    continue;
                }

                // FlexTube 부모 생성 — Animator + FlexTube 컴포넌트는 prefab 에 부착돼 있다고 가정.
                // world origin 으로 강제 — 자식 부품의 world position 이 prefab transform offset 영향 안 받게.
                var tubeObj = Instantiate(flexTubePrefab);
                tubeObj.name = $"FlexTube_Group{groupId}";
                tubeObj.transform.position = Vector3.zero;
                tubeObj.transform.rotation = Quaternion.identity;
                tubeObj.transform.localScale = Vector3.one;
                var tube = tubeObj.GetComponent<FlexTube>() ?? tubeObj.AddComponent<FlexTube>();

                // [FlexTube fix] flexTubePrefab 에 디자인 시점 템플릿 자식(예시 StartCap/Segment/EndCap)이 남아 있으면
                // 런타임에 스폰하는 실제 캡 클론과 중복 노출됨(스크린샷의 'FlexTube > 캡들' + 'FlexTube_StartCap(Clone)').
                // tubeObj 는 컨테이너로만 쓰고(자식 참조 X, parts 리스트만 사용) 실제 파츠는 아래에서 root 에 붙이므로,
                // 인스턴스화 직후 기존 자식을 모두 제거해 템플릿 잔재를 정리한다.
                for (int ci = tubeObj.transform.childCount - 1; ci >= 0; ci--)
                    Destroy(tubeObj.transform.GetChild(ci).gameObject);

                _flexTubeRoots.Add(tubeObj);
                Quaternion extraRot = Quaternion.Euler(0f, tube.ExtraYRotation, 0f);

                // 다른 모든 필드 요소와 동일 좌표계로 정렬 — raw position 을 보드 보정(GetAdjustedBoardPosition)에 통과.
                // 누락 시 보드가 스케일/오프셋된 레벨에서 튜브가 셀 그리드를 벗어남(캡 미정렬 포함).
                var cellPositions = new List<Vector3>(ids.Count);
                foreach (var id in ids) cellPositions.Add(GetAdjustedBoardPosition(_balloons[id].position));

                int groupColor = _balloons[ids[0]].color;
                int colorIdx = Mathf.Clamp(groupColor, 0, BalloonColors.Length - 1);

                // visual segment count per cell — prefab mesh 가 cell 폭 1/N 이라는 전제. cell 폭이 visual 적으로 끊김 없이 채워짐.
                int visualSegmentsPerCell = Mathf.Max(1, tube.VisualSegmentsPerCell);
                // visualStep(1/N 폭 분산 간격)은 보정된 인접 셀 실제 거리 기반으로 cell 별 계산(아래 루프). raw cellSpacing 미사용.

                // parts list 용량 = 2 Cap + (cells - 2) × N visual segment
                int segmentCellCount = Mathf.Max(0, ids.Count - 2);
                int partsCapacity = 2 + segmentCellCount * visualSegmentsPerCell;
                var parts = new List<FlexTubePart>(partsCapacity);

                for (int i = 0; i < ids.Count; i++)
                {
                    int id = ids[i];
                    var data = _balloons[id];

                    bool isStart = (i == 0);
                    bool isEnd   = (i == ids.Count - 1);
                    GimmickIdentifier.FlexTubePart partType =
                        isStart ? GimmickIdentifier.FlexTubePart.StartCap :
                        isEnd   ? GimmickIdentifier.FlexTubePart.EndCap   :
                                  GimmickIdentifier.FlexTubePart.Segment;

                    GameObject prefab = partType == GimmickIdentifier.FlexTubePart.StartCap ? startCapPrefab
                                       : partType == GimmickIdentifier.FlexTubePart.EndCap   ? endCapPrefab
                                                                                              : segmentPrefab;

                    Quaternion rotation = CalculateFlexTubePartRotation(cellPositions, i) * extraRot;

                    // visual segment 가 여러 개일 때 cell 안에서 tangent 방향으로 1/N step 으로 분산.
                    // Cap (Start/End) 은 분해 안 함 — 항상 cell center 1개.
                    int visualCount = (partType == GimmickIdentifier.FlexTubePart.Segment) ? visualSegmentsPerCell : 1;
                    Vector3 tangent = ComputeFlexTubeTangent(cellPositions, i); // cellPositions 기반 forward
                    bool useTangent = visualCount > 1 && tangent.sqrMagnitude > 0.0001f;
                    // 1/N 폭 visual 이 보정된 cell 을 정확히 채우도록 — 보정된 인접 셀 거리 / N.
                    float visualStep = AdjacentCellDistance(cellPositions, i) / visualSegmentsPerCell;

                    // cell center 에 있는 visual 을 _balloonObjects[id] 로 등록 — 다트 target 위치가 cell center 와 일치하도록.
                    // 끝쪽부터 사라지는 정책상 center visual 은 cell 죽기 직전까지 active 유지 → target lookup 안정.
                    int centerVisualIdx = visualCount / 2;

                    for (int v = 0; v < visualCount; v++)
                    {
                        // visual segment center offset — 보정된 cell center 기준 -(N-1)/2 .. +(N-1)/2 × visualStep.
                        Vector3 spawnPos = cellPositions[i];
                        if (useTangent)
                            spawnPos += tangent * ((v - (visualCount - 1) * 0.5f) * visualStep);

                        var partObj = Instantiate(prefab);
                        if (partObj == null)
                        {
                            Debug.LogWarning($"[FlexTube] Instantiate failed for group {groupId} seq {i} visual {v}.");
                            continue;
                        }
                        partObj.transform.position = spawnPos;
                        partObj.transform.rotation = rotation;
                        partObj.transform.SetParent(tubeObj.transform, worldPositionStays: true);

                        // [FlexTube] Segment visual 만 x,y 스케일 보정 (z=길이축 유지). 캡(Start/End)은 프리팹 기본 유지.
                        if (partType == GimmickIdentifier.FlexTubePart.Segment)
                        {
                            Vector3 ls = partObj.transform.localScale;
                            float ss = tube.SegmentScaleXY;
                            partObj.transform.localScale = new Vector3(ss, ss, ls.z);
                        }

                        var part = partObj.GetComponent<FlexTubePart>();
                        if (part == null) part = partObj.AddComponent<FlexTubePart>();
                        if (part == null)
                        {
                            Debug.LogWarning($"[FlexTube] FlexTubePart AddComponent failed on prefab {prefab.name}.");
                            continue;
                        }
                        part.SetPartType(partType);
                        part.SetBalloonId(id);
                        parts.Add(part);

                        // _balloonObjects 는 cell 당 1 object 만 — center visual 등록 (위치=cell center, 죽기 직전까지 active).
                        if (v == centerVisualIdx)
                            _balloonObjects[id] = partObj;

                        var partGi = partObj.GetComponent<GimmickIdentifier>();
                        if (partGi != null)
                        {
                            partGi.Initialize();
                            if (partGi.HasColorRenderers)
                                partGi.ApplyColor(BalloonColors[colorIdx]);
                        }
                        else
                        {
                            ApplyTintToObject(partObj, BalloonColors[colorIdx]);
                        }
                    }

                    Debug.Log($"[FlexTube]   spawned {partType} id={id} at ({data.position.x:F2},{data.position.z:F2}) rot.y={rotation.eulerAngles.y:F0} color={colorIdx} visualCount={visualCount}");
                }

                // HP = segment cell 수(튜브 길이). cell 당 1히트로 파괴되고, visual segment(cell×N)는
                // FlexTube 가 parts 에서 세어 HP 비율로 비례 감소시킨다(한 hit 당 N개씩).
                // segmentCellCount = 0 (cap 만) 이면 안전 fallback 1.
                int flexTubeHp = Mathf.Max(1, segmentCellCount);
                int color = _balloons[ids[0]].color;
                tube.Initialize(flexTubeHp, color, groupId, parts);
            }
        }

        /// <summary>cell index i 의 forward tangent (visual segment 분산 시 사용). 직선/대각 모두 정상 동작. y=0 평면 기준.</summary>
        private static Vector3 ComputeFlexTubeTangent(List<Vector3> cellPositions, int i)
        {
            int n = cellPositions.Count;
            Vector3 dir;
            if (i == 0)                dir = cellPositions[1] - cellPositions[0];
            else if (i == n - 1)       dir = cellPositions[n - 1] - cellPositions[n - 2];
            else                        dir = cellPositions[i + 1] - cellPositions[i - 1];
            dir.y = 0f;
            return dir.sqrMagnitude > 0.0001f ? dir.normalized : Vector3.zero;
        }

        /// <summary>보정된 cell index i 의 인접 셀 간 실제 거리. 양끝은 한쪽, 중간은 양쪽 평균.
        /// visual segment 분산 간격(visualStep = 거리/N) 계산용 — 보드 스케일 반영된 셀 폭과 일치.</summary>
        private static float AdjacentCellDistance(List<Vector3> cellPositions, int i)
        {
            int n = cellPositions.Count;
            if (n < 2) return 0f;
            if (i <= 0)       return Vector3.Distance(cellPositions[0], cellPositions[1]);
            if (i >= n - 1)   return Vector3.Distance(cellPositions[n - 1], cellPositions[n - 2]);
            return 0.5f * (Vector3.Distance(cellPositions[i], cellPositions[i - 1])
                         + Vector3.Distance(cellPositions[i], cellPositions[i + 1]));
        }

        /// <summary>FlexTube 부품의 회전 계산 — prefab forward = +z (Unity 기본) 가정. cell index i 의 이전/다음 위치로 방향 추론.</summary>
        private static Quaternion CalculateFlexTubePartRotation(List<Vector3> cellPositions, int i)
        {
            int n = cellPositions.Count;
            Vector3 self = cellPositions[i];
            Vector3 dir;
            if (i == 0)
                dir = cellPositions[1] - self;                       // StartCap — 다음 Segment 방향으로 향함
            else if (i == n - 1)
                dir = self - cellPositions[n - 2];                   // EndCap — 이전 Segment 에서 자신 쪽 방향
            else
                dir = cellPositions[i + 1] - cellPositions[i - 1];   // Segment — 이전→다음 (직선/대각)

            dir.y = 0f;
            if (dir.sqrMagnitude < 0.0001f) return Quaternion.identity;
            return Quaternion.LookRotation(dir.normalized, Vector3.up);
        }

        private GameObject GetOrCreateBalloonObject(int balloonId, Vector3 position, int color)
        {
            if (!ObjectPoolManager.HasInstance)
            {
                Debug.LogWarning("[BalloonController] ObjectPoolManager not available.");
                return null;
            }

            // 기믹별 전용 풀 라우팅 — 기믹 비주얼 프리팹이 있는 경우 해당 풀 사용
            string poolKey = PoolKey;
            if (_balloons.TryGetValue(balloonId, out BalloonData bData))
            {
                poolKey = ResolveGimmickPoolKey(bData.gimmickType);
            }
            GameObject obj = ObjectPoolManager.Instance.Get(poolKey);
            if (obj == null)
            {
                Debug.LogWarning($"[BalloonController] Pool returned null for key '{poolKey}'.");
                return null;
            }

            // 풍선 타일 영역 배율 적용 (레벨별 안전 배율 사용)
            Vector3 adjustedPos = GetAdjustedBoardPosition(position);
            obj.transform.position = adjustedPos;
            float scaleMult = _levelSafeCalculated
                ? Mathf.Max(_levelSafeWm, _levelSafeHm)
                : (GameManager.HasInstance ? Mathf.Max(GameManager.Instance.Board.balloonFieldWidthMult, GameManager.Instance.Board.balloonFieldHeightMult) : 1f);
            obj.transform.localScale = GetBalloonRestScale(scaleMult);
            obj.SetActive(true);

            // [Optimization 2026-05-12] Balloon Shadow 의 SpriteRenderer → MeshRenderer 전환.
            // SpriteRenderer 의 PerRendererData MPB 가 SRP Batcher / GPU Instancing 차단 → MeshRenderer + Quad + 공용 mat 으로 swap.
            // 같은 sprite (모든 balloon 의 shadow 가 같은 sprite) → 1 mat → SRP Batcher batch.
            // 이미 변환된 경우 (pool 재사용) SpriteRenderer 없어 noop.
            SpriteSRPBatcherUtil.ConvertShadowToMeshSprite(obj);

            // per-object 색상 변주 (같은 색이라도 톤이 약간씩 다름)
            int colorIdx = Mathf.Clamp(color, 0, BalloonColors.Length - 1);
            Color variedColor = GetVariedColor(colorIdx);
            ApplyTintToObject(obj, variedColor);

            // Initialize BalloonIdentifier for dart hit detection
            BalloonIdentifier identifier = obj.GetComponent<BalloonIdentifier>();
            if (identifier != null)
            {
                identifier.Initialize(balloonId, color);
            }

            return obj;
        }

        private void BuildPositionIndex()
        {
            _positionIndex.Clear();
            _multiCellOccupancy.Clear();
            foreach (BalloonData d in _balloons.Values)
            {
                RegisterPositionIndexForBalloon(d);
            }
        }

        private bool UsesMultiCellOccupancy(BalloonData data)
        {
            return data != null
                && IsSizedFieldGimmick(data.gimmickType)
                && (data.sizeW > 1 || data.sizeH > 1);
        }

        // 멀티셀 footprint(occupancy/blocking/targeting)을 갖는 sized field 기믹의 단일 소스.
        // 모든 소비처(UsesMultiCellOccupancy / DirectionalTargeting / BoardStateManager / DartManager)가 이 함수를 참조.
        // Wall 도 정사각 2×2/3×3 footprint 전체가 blocker 로 깔려야 하므로 포함 (미포함 시 anchor 1칸만 막혀 다트가 관통).
        public static bool IsSizedFieldGimmick(string gimmickType)
        {
            string normalized = GimmickDisplayName.Normalize(gimmickType);
            return normalized == GimmickPinata
                || normalized == GimmickPinataBox
                || normalized == GimmickBarricade
                || normalized == GimmickWall;
        }

        private void BuildOccupiedCells(BalloonData data, List<Vector3Int> output)
        {
            output.Clear();
            if (data == null) return;

            Vector3Int anchor = ToGridKey(data.position);
            int width = Mathf.Max(1, data.sizeW);
            int height = Mathf.Max(1, data.sizeH);
            for (int dx = 0; dx < width; dx++)
            {
                for (int dz = 0; dz < height; dz++)
                    output.Add(new Vector3Int(anchor.x + dx, 0, anchor.z + dz));
            }
        }

        private void RegisterPositionIndexForBalloon(BalloonData data)
        {
            if (data == null || data.isPopped) return;

            if (!UsesMultiCellOccupancy(data))
            {
                _positionIndex[ToGridKey(data.position)] = data.balloonId;
                return;
            }

            BuildOccupiedCells(data, _reusableOccupiedCells);
            var cells = new List<Vector3Int>(_reusableOccupiedCells.Count);
            for (int i = 0; i < _reusableOccupiedCells.Count; i++)
            {
                Vector3Int cell = _reusableOccupiedCells[i];
                cells.Add(cell);
                _positionIndex[cell] = data.balloonId;
            }
            _multiCellOccupancy[data.balloonId] = cells;
        }

        private void RemovePositionIndexForBalloon(BalloonData data)
        {
            if (data == null) return;

            if (_multiCellOccupancy.TryGetValue(data.balloonId, out List<Vector3Int> cells))
            {
                for (int i = 0; i < cells.Count; i++)
                    _positionIndex.Remove(cells[i]);
                _multiCellOccupancy.Remove(data.balloonId);
                return;
            }

            _positionIndex.Remove(ToGridKey(data.position));
        }

        private void GetFieldVisualMetrics(
            out float widthMult,
            out float heightMult,
            out float scaleMult,
            out float cellSizeX,
            out float cellSizeZ,
            out float scaleBase)
        {
            float cs = _cellSpacing > 0 ? _cellSpacing : 0.3f;
            widthMult = 1f;
            heightMult = 1f;
            scaleMult = _levelSafeCalculated
                ? Mathf.Max(_levelSafeWm, _levelSafeHm)
                : (GameManager.HasInstance ? Mathf.Max(GameManager.Instance.Board.balloonFieldWidthMult, GameManager.Instance.Board.balloonFieldHeightMult) : 1f);
            if (_levelSafeCalculated)
            {
                widthMult = _levelSafeWm;
                heightMult = _levelSafeHm;
            }
            else if (GameManager.HasInstance)
            {
                widthMult = GameManager.Instance.Board.balloonFieldWidthMult;
                heightMult = GameManager.Instance.Board.balloonFieldHeightMult;
            }

            cellSizeX = cs * widthMult;
            cellSizeZ = cs * heightMult;
            scaleBase = _balloonScale * scaleMult;
        }

        // Target Box(Pinata_Box) 알 모델 비주얼: 박스 루트를 footprint 중앙·identity scale 로 놓고
        // PinataBoxView 가 W×H 알을 셀 간격으로 배치·색칠. 단일 mesh 확대가 아니므로 이웃 셀 침범 없음.
        private void ApplyPinataBoxVisual(GameObject obj, BalloonData data, PinataBoxView view)
        {
            if (obj == null || data == null || view == null) return;

            GetFieldVisualMetrics(
                out _, out _, out _,
                out float cellSizeX, out float cellSizeZ, out _);

            int width = Mathf.Max(1, data.sizeW);
            int height = Mathf.Max(1, data.sizeH);

            Vector3 anchor = GetAdjustedBoardPosition(data.position);
            Vector3 center = new Vector3(
                anchor.x + (width - 1) * cellSizeX * 0.5f,
                obj.transform.position.y,
                anchor.z + (height - 1) * cellSizeZ * 0.5f);
            obj.transform.position = center;
            obj.transform.localScale = Vector3.one; // 알/틀 크기는 view 가 제어 (루트 scale=1 가정)

            // 알은 footprint 안쪽 격자 셀에 자동 맞춤(cellSize 기준). eggHps 로 초기 균열 상태 판단.
            // scaleMult 는 전달하지 않는다 — cellSizeX/Z 가 이미 field mult 를 포함하므로 알엔 재적용 불필요.
            view.Build(width, height, data.eggColors, data.eggHps, cellSizeX, cellSizeZ);
        }

        private void ApplySizedFieldVisualTransform(GameObject obj, BalloonData data)
        {
            if (obj == null || data == null) return;

            GetFieldVisualMetrics(
                out float widthMult,
                out float heightMult,
                out float scaleMult,
                out float cellSizeX,
                out float cellSizeZ,
                out _);

            int width = Mathf.Max(1, data.sizeW);
            int height = Mathf.Max(1, data.sizeH);

            // ROLLBACK_SIZED_GIMMICK_ANCHOR_VISUAL_CENTER:
            // MapMaker stores the authored cell as the bottom-left anchor of the sized gimmick.
            // Most field prefabs scale from their center pivot, so move only the visual root to
            // the center of that anchored rectangle. Logical occupancy/targeting still uses
            // data.position as the bottom-left anchor.
            Vector3 adjustedAnchor = GetAdjustedBoardPosition(data.position);
            Vector3 visualCenter = new Vector3(
                adjustedAnchor.x + (width - 1) * cellSizeX * 0.5f,
                obj.transform.position.y,
                adjustedAnchor.z + (height - 1) * cellSizeZ * 0.5f);
            obj.transform.localScale = new Vector3(
                _balloonScale * widthMult * width,
                _balloonScale * scaleMult,
                _balloonScale * heightMult * height);
            obj.transform.position = visualCenter;
        }

        private void ApplyBarricadeVisualTransform(GameObject obj, BalloonData data)
        {
            if (obj == null || data == null) return;

            GetFieldVisualMetrics(
                out float widthMult,
                out float heightMult,
                out float scaleMult,
                out float cellSizeX,
                out float cellSizeZ,
                out _);

            int width = Mathf.Max(1, data.sizeW);
            int height = Mathf.Max(1, data.sizeH);
            bool vertical = height > width;

            // ROLLBACK_BARRICADE_VISUAL_SETTINGS:
            // The root/head stays on the authored anchor cell. Only BarricadeBody covers the extra cells.
            Vector3 adjustedAnchor = GetAdjustedBoardPosition(data.position);
            obj.transform.localScale = new Vector3(
                _balloonScale * widthMult,
                _balloonScale * scaleMult,
                _balloonScale * heightMult);
            obj.transform.position = new Vector3(
                adjustedAnchor.x + _barricadeVisualOffset.x,
                _barricadeVisualY + _barricadeVisualOffset.y,
                adjustedAnchor.z + _barricadeVisualOffset.z);

            // Inspector 에서 GimmickIdentifier 에 명시 할당한 참조를 우선 사용. 미할당 시 이름 자동 탐색(기존 동작).
            GimmickIdentifier gid = obj.GetComponent<GimmickIdentifier>();
            Transform edge = gid != null ? gid.BarricadeEdge : null;

            Transform body = (gid != null && gid.BarricadeBody != null)
                ? gid.BarricadeBody
                : FindChildRecursive(obj.transform, "BarricadeBody")
                    ?? FindChildRecursive(obj.transform, "BaricadeBody")
                    ?? FindChildRecursive(obj.transform, "Barricade")
                    ?? FindChildRecursive(obj.transform, "Baricade")
                    ?? FindFirstRenderableChild(obj.transform);

            if (body == null)
            {
                Vector3 visualCenter = new Vector3(
                    adjustedAnchor.x + (width - 1) * cellSizeX * 0.5f,
                    obj.transform.position.y,
                    adjustedAnchor.z + (height - 1) * cellSizeZ * 0.5f);
                obj.transform.localScale = new Vector3(
                    _balloonScale * widthMult * width,
                    _balloonScale * scaleMult,
                    _balloonScale * heightMult * height);
                obj.transform.position = visualCenter;
                return;
            }

            if (!_barricadeBodyBaseScales.TryGetValue(body, out Vector3 baseScale))
            {
                baseScale = body.localScale;
                _barricadeBodyBaseScales[body] = baseScale;
            }
            if (!_barricadeBodyBaseRotations.TryGetValue(body, out Quaternion baseRotation))
            {
                baseRotation = body.localRotation;
                _barricadeBodyBaseRotations[body] = baseRotation;
            }
            if (!_barricadeBodyBasePositions.TryGetValue(body, out Vector3 basePosition))
            {
                basePosition = body.localPosition;
                _barricadeBodyBasePositions[body] = basePosition;
            }

            float lengthCells = Mathf.Max(1, vertical ? height : width);
            int requiredHits = data.maxHP > 0 ? data.maxHP : 2;
            int remainingHits = Mathf.Clamp(requiredHits - data.hitCount, 0, requiredHits);
            float hpRatio = requiredHits > 0 ? remainingHits / (float)requiredHits : 1f;

            // ROLLBACK_BARRICADE_BODY_HP_SHRINK:
            // BarricadeBody covers the cells after the anchor/head. Each hit shortens that body
            // by the remaining HP ratio while the root/head stays on the authored anchor cell.
            float bodyCells = Mathf.Max(0f, lengthCells - 1f) * hpRatio;
            body.gameObject.SetActive(bodyCells > 0.001f);
            if (bodyCells <= 0.001f)
            {
                // 몸통이 사라지면 끝 마감(Edge)도 함께 숨김.
                if (edge != null) edge.gameObject.SetActive(false);
                return;
            }

            Quaternion targetRotation = vertical ? baseRotation * Quaternion.Euler(0f, 90f, 0f) : baseRotation;

            body.localScale = baseScale;
            body.localRotation = targetRotation;
            body.localPosition = basePosition;

            // ROLLBACK_BARRICADE_BODY_CELL_SCALE_X:
            // BarricadeBody is now authored 1:1 with a board cell. Use that authored ratio
            // directly; renderer bounds from the imported FBX are not reliable enough for sizing.
            float bodyScaleX = Mathf.Max(
                0.001f,
                BARRICADE_BODY_CELL_LOCAL_SCALE_X * bodyCells * _barricadeLengthMultiplier + _barricadeLengthPadding);
            body.localScale = new Vector3(bodyScaleX, baseScale.y, baseScale.z);
            body.localRotation = targetRotation;

            if (TryMeasureRendererBounds(body, out Bounds bodyBounds))
            {
                float centerDistance = (bodyCells + 1f) * 0.5f * (vertical ? cellSizeZ : cellSizeX);
                Vector3 desiredCenter = obj.transform.position + (vertical
                    ? new Vector3(0f, 0f, centerDistance)
                    : new Vector3(centerDistance, 0f, 0f));
                desiredCenter += _barricadeBodyVisualOffset;
                Vector3 delta = desiredCenter - bodyBounds.center;
                body.position += delta;

                // Edge(끝 마감)를 늘어난 body 의 먼 쪽 끝으로 이동 — HP 감소로 body 가 짧아지면 Edge 도 따라옴.
                if (edge != null)
                {
                    edge.gameObject.SetActive(true);
                    if (!_barricadeEdgeBaseRotations.TryGetValue(edge, out Quaternion edgeBaseRotation))
                    {
                        edgeBaseRotation = edge.localRotation;
                        _barricadeEdgeBaseRotations[edge] = edgeBaseRotation;
                    }
                    edge.localRotation = vertical ? edgeBaseRotation * Quaternion.Euler(0f, 90f, 0f) : edgeBaseRotation;

                    float halfLen = 0.5f * (vertical ? bodyBounds.size.z : bodyBounds.size.x);
                    Vector3 dir = vertical ? Vector3.forward : Vector3.right;
                    edge.position = desiredCenter + dir * halfLen + _barricadeEdgeOffset;
                }
            }
        }

        private Transform FindChildRecursive(Transform root, string childName)
        {
            if (root == null || string.IsNullOrEmpty(childName)) return null;
            for (int i = 0; i < root.childCount; i++)
            {
                Transform child = root.GetChild(i);
                if (child.name == childName) return child;

                Transform nested = FindChildRecursive(child, childName);
                if (nested != null) return nested;
            }
            return null;
        }

        private Transform FindFirstRenderableChild(Transform root)
        {
            if (root == null) return null;
            for (int i = 0; i < root.childCount; i++)
            {
                Transform child = root.GetChild(i);
                if (child.GetComponent<Renderer>() != null) return child;

                Transform nested = FindFirstRenderableChild(child);
                if (nested != null) return nested;
            }
            return null;
        }

        private float MeasureRendererLength(Transform root, bool vertical)
        {
            if (!TryMeasureRendererBounds(root, out Bounds bounds)) return 0f;
            return vertical ? bounds.size.z : bounds.size.x;
        }

        private bool TryMeasureRendererBounds(Transform root, out Bounds bounds)
        {
            bounds = default;
            if (root == null) return false;
            Renderer[] renderers = root.GetComponentsInChildren<Renderer>(true);
            if (renderers == null || renderers.Length == 0) return false;

            bool hasBounds = false;
            for (int i = 0; i < renderers.Length; i++)
            {
                Renderer r = renderers[i];
                if (r == null) continue;
                if (!hasBounds)
                {
                    bounds = r.bounds;
                    hasBounds = true;
                }
                else
                {
                    bounds.Encapsulate(r.bounds);
                }
            }

            return hasBounds;
        }

        private static readonly Color HIDDEN_COLOR = new Color(0.45f, 0.45f, 0.50f);   // Grey mystery balloon
        private static readonly Color ICE_COLOR = new Color(0.65f, 0.85f, 0.95f);      // Frozen blue tint
        private static readonly Color FROZEN_DART_COLOR = new Color(0.50f, 0.70f, 0.90f); // Darker frozen tint (distinct from Ice)
        private static readonly Color WALL_COLOR = new Color(0.35f, 0.35f, 0.38f);     // Dark grey stone wall
        private static readonly Color PIN_COLOR = new Color(0.70f, 0.50f, 0.20f);      // Brown wooden pin
        private static readonly Color CURTAIN_COLOR = new Color(0.85f, 0.55f, 0.85f);  // Purple curtain tint
        private static readonly Color PINATA_COLOR = new Color(0.95f, 0.70f, 0.20f);   // Gold pinata

        private void ApplyInitialHiddenState()
        {
            // 머테리얼 우선순위: (1) BalloonIdentifier 프리팹 할당 → (2) BalloonController 할당/Resources
            Material fallbackMat = GetHiddenMaterialFallback();

            foreach (int id in _hiddenBalloons)
            {
                if (!_balloonObjects.TryGetValue(id, out GameObject obj) || obj == null) continue;

                BalloonIdentifier bi = obj.GetComponent<BalloonIdentifier>();
                if (bi != null && bi.HasColorRenderers && (bi.HasHiddenMaterial || fallbackMat != null))
                {
                    // 프리팹 자체 할당된 머테리얼 우선, 없으면 fallback 전달
                    bi.ApplyHiddenMaterial(bi.HasHiddenMaterial ? null : fallbackMat);
                }
                else
                {
                    // 최종 폴백: 기존 grey tint
                    ApplyTintToObject(obj, HIDDEN_COLOR);
                }
            }
        }

        private Material GetHiddenMaterialFallback()
        {
            if (_balloonHiddenMaterial != null) return _balloonHiddenMaterial;
            // Resources 폴백 로드 (Assets/Resources/BalloonHidden.mat 에 복사 시 자동 사용)
            _balloonHiddenMaterial = Resources.Load<Material>("BalloonHidden");
            return _balloonHiddenMaterial;
        }

        /// <summary>
        /// Applies the Ice visual tint (frozen blue) to Ice gimmick balloons.
        /// Called during setup for any balloon with GimmickIce type.
        /// Also overlays FrozenLayer prefab as a child for visual ice shell.
        /// </summary>
        private void ApplyInitialIceState()
        {
            // [#13/§11] Ice 영역을 인접 연결 성분으로 묶고, 영역의 blockSize(2=2×2 등) 로 타일링 렌더.
            // blockSize<=1 이면 셀당 오버레이(기본·하위호환). >1 이면 블록당 1개 오버레이로 병합 렌더.
            var regions = GetIceRegions();
            if (regions.Count == 0) return;

            GetAdjustedCellSize(out float cellSizeX, out float cellSizeZ);

            for (int r = 0; r < regions.Count; r++)
            {
                var region = regions[r];
                int blockSize = 1;
                if (region.Count > 0 && _balloons.TryGetValue(region[0], out BalloonData first))
                    blockSize = Mathf.Max(1, first.iceBlockSize);

                if (blockSize <= 1)
                {
                    // 셀당 오버레이 (기본)
                    for (int i = 0; i < region.Count; i++)
                    {
                        int id = region[i];
                        if (_balloonObjects.TryGetValue(id, out GameObject obj) && obj != null)
                        {
                            ApplyTintToObject(obj, ICE_COLOR);
                            AttachFrozenOverlay(id, obj);
                        }
                    }
                    continue;
                }

                RenderIceRegionBlocks(region, blockSize, cellSizeX, cellSizeZ);
            }
        }

        /// <summary>
        /// [#13/§11] 한 Ice 영역을 blockSize×blockSize 블록으로 분할해 블록당 FrozenLayer 1개를 부착(병합 렌더).
        /// 블록 내 모든 셀 본체는 숨김, 앵커(블록 내 최소 col,row 셀)에 blockSize 배율 오버레이를 블록 중앙으로 오프셋해 부착.
        /// 그리드 좌표는 월드 위치를 셀 크기로 스냅해 산출. (월드 축 정렬 가정 — 시각 오프셋/스케일은 Editor 미세조정 대상)
        /// </summary>
        private void RenderIceRegionBlocks(List<int> region, int blockSize, float cellSizeX, float cellSizeZ)
        {
            float invX = 1f / Mathf.Max(0.0001f, cellSizeX);
            float invZ = 1f / Mathf.Max(0.0001f, cellSizeZ);

            float minX = float.MaxValue, minZ = float.MaxValue;
            for (int i = 0; i < region.Count; i++)
                if (_balloons.TryGetValue(region[i], out BalloonData d))
                {
                    if (d.position.x < minX) minX = d.position.x;
                    if (d.position.z < minZ) minZ = d.position.z;
                }

            var cellCol = new Dictionary<int, int>();
            var cellRow = new Dictionary<int, int>();
            var blocks  = new Dictionary<(int, int), List<int>>();
            for (int i = 0; i < region.Count; i++)
            {
                int id = region[i];
                if (!_balloons.TryGetValue(id, out BalloonData d)) continue;
                int col = Mathf.RoundToInt((d.position.x - minX) * invX);
                int row = Mathf.RoundToInt((d.position.z - minZ) * invZ);
                cellCol[id] = col;
                cellRow[id] = row;
                var key = (col / blockSize, row / blockSize);
                if (!blocks.TryGetValue(key, out var list)) { list = new List<int>(); blocks[key] = list; }
                list.Add(id);
            }

            foreach (var kv in blocks)
            {
                var cells = kv.Value;
                // 앵커 = 블록 내 (row, col) 최소 셀
                int anchorId = -1, best = int.MaxValue;
                for (int i = 0; i < cells.Count; i++)
                {
                    int id = cells[i];
                    int rank = cellRow[id] * 100000 + cellCol[id];
                    if (rank < best) { best = rank; anchorId = id; }
                }
                if (anchorId < 0) continue;

                // 블록 내 모든 셀 본체 숨김 (얼음 블록만 보이게)
                for (int i = 0; i < cells.Count; i++)
                {
                    if (_balloonObjects.TryGetValue(cells[i], out GameObject cobj) && cobj != null)
                    {
                        var cbi = cobj.GetComponent<BalloonIdentifier>();
                        if (cbi != null) cbi.SetVisible(false);
                    }
                }

                // 앵커에 blockSize 배율 오버레이 — 블록 중앙으로 오프셋
                if (_balloonObjects.TryGetValue(anchorId, out GameObject aobj) && aobj != null)
                {
                    int blockColBase = kv.Key.Item1 * blockSize;
                    int blockRowBase = kv.Key.Item2 * blockSize;
                    float offCellsX = (blockSize - 1) * 0.5f - (cellCol[anchorId] - blockColBase);
                    float offCellsZ = (blockSize - 1) * 0.5f - (cellRow[anchorId] - blockRowBase);
                    AttachIceBlockOverlay(anchorId, aobj, blockSize, offCellsX * cellSizeX, offCellsZ * cellSizeZ);
                }
            }
        }

        /// <summary>
        /// [#13/§11] Ice 블록 앵커에 blockSize 배율 FrozenLayer 오버레이 부착 (블록 중앙 오프셋). 본체 숨김은 호출 측이 처리.
        /// </summary>
        private void AttachIceBlockOverlay(int anchorId, GameObject anchor, int blockSize, float offsetX, float offsetZ)
        {
            if (anchor == null || !ObjectPoolManager.HasInstance) return;
            if (_frozenOverlays.ContainsKey(anchorId)) return;
            if (!ObjectPoolManager.Instance.HasPool(FrozenLayerPoolKey)) return;

            GameObject overlay = ObjectPoolManager.Instance.Get(FrozenLayerPoolKey);
            if (overlay == null) return;

            overlay.transform.SetParent(anchor.transform, false);
            overlay.transform.localRotation = Quaternion.identity;

            // 스케일: 1×1 은 FROZEN_OVERLAY_SCALE(여백 포함) 그대로. 블록은 셀 수만큼 확장하되 여백은 고정 →
            // (blockSize-1) + FROZEN_OVERLAY_SCALE. (1.3*B 는 여백이 B 배로 과대해져 footprint 를 벗어남)
            float s = (blockSize - 1) + FROZEN_OVERLAY_SCALE;
            overlay.transform.localScale = Vector3.one * s;

            // 위치 보정(Wall 패턴): 앵커=블록 코너 셀 → footprint 중앙으로 이동. localPosition 은 부모(_balloonScale)
            // 스케일에 곱해져 어긋나므로 월드 위치로 직접 설정 (offsetX/Z 는 이미 월드 단위 = (B-1)*0.5*cellSize).
            Vector3 aw = anchor.transform.position;
            overlay.transform.position = new Vector3(aw.x + offsetX, aw.y, aw.z + offsetZ);
            overlay.SetActive(true);

            _frozenOverlays[anchorId] = overlay;
        }

        /// <summary>
        /// Applies Frozen Dart visual tint. Darker blue than Ice to distinguish.
        /// Called during setup for balloons with GimmickFrozenDart type.
        /// Also overlays FrozenLayer prefab as a child for visual frost shell.
        /// </summary>
        private void ApplyInitialFrozenDartState()
        {
            foreach (BalloonData d in _balloons.Values)
            {
                if (d.isPopped || d.gimmickType != GimmickFrozenDart) continue;
                if (_balloonObjects.TryGetValue(d.balloonId, out GameObject obj) && obj != null)
                {
                    ApplyTintToObject(obj, FROZEN_DART_COLOR);
                    AttachFrozenOverlay(d.balloonId, obj);
                }
            }
        }

        private void ApplyInitialColorCurtainState()
        {
            foreach (BalloonData d in _balloons.Values)
            {
                if (d.isPopped || d.gimmickType != GimmickColorCurtain) continue;
                if (_balloonObjects.TryGetValue(d.balloonId, out GameObject obj) && obj != null)
                {
                    ApplyTintToObject(obj, CURTAIN_COLOR);
                }
            }
        }

        /// <summary>
        /// 색상 변주: 기본색에서 3가지 톤 (기본, 진한, 연한) 중 랜덤 선택.
        /// 머티리얼은 색상별 캐시되므로 동일 톤끼리 배칭 가능.
        /// </summary>
        private const int VARIATION_COUNT = 3; // 기본, 진한, 연한

        public static Color GetVariedColor(int colorIndex)
        {
            Color baseColor = BalloonColors[Mathf.Clamp(colorIndex, 0, BalloonColors.Length - 1)];

            int variant = Random.Range(0, VARIATION_COUNT);
            switch (variant)
            {
                case 1: // 진한 톤 (미세 변주)
                    Color.RGBToHSV(baseColor, out float h1, out float s1, out float v1);
                    // 그레이스케일(s≈0)은 saturation 변주 생략 (h=0 + s>0 = 빨강 끼어들음)
                    float newS1 = s1 < 0.01f ? 0f : Mathf.Min(s1 + 0.03f, 1f);
                    return Color.HSVToRGB(h1, newS1, Mathf.Max(v1 - 0.03f, 0.2f));
                case 2: // 연한 톤 (미세 변주)
                    Color.RGBToHSV(baseColor, out float h2, out float s2, out float v2);
                    float newS2 = s2 < 0.01f ? 0f : Mathf.Max(s2 - 0.04f, 0.1f);
                    return Color.HSVToRGB(h2, newS2, Mathf.Min(v2 + 0.03f, 1f));
                default: // 기본 톤
                    return baseColor;
            }
        }

        /// <summary>색상별 공유 Material 캐시. sharedMaterial 할당 → SRP Batcher 배칭 유지.</summary>
        private static readonly Dictionary<Color, Material> _sharedColorMats = new Dictionary<Color, Material>();
        private static Shader _cachedLitShader;

        public static Material GetOrCreateSharedMaterial(Color color)
        {
            if (_sharedColorMats.TryGetValue(color, out Material mat))
                return mat;

            if (_cachedLitShader == null)
                _cachedLitShader = Shader.Find("Custom/ItemShared")
                    ?? Shader.Find("Universal Render Pipeline/Lit")
                    ?? Shader.Find("Standard");

            if (_cachedLitShader == null)
            {
                Debug.LogError("[BalloonController] No shader found for balloon material!");
                return null;
            }

            mat = new Material(_cachedLitShader);
            mat.SetColor("_BaseColor", color);
            // [Optimization 2026-05-10 revert] GPU Instancing 채택 — 같은 색 풍선들 1 draw 로 묶임.
            mat.enableInstancing = true;
            _sharedColorMats[color] = mat;
            return mat;
        }

        /// <summary>아웃라인 ON/OFF 설정 (검은색=활성, 흰색=비활성)</summary>
        public static void SetOutline(GameObject obj, bool active, Color outlineColor)
        {
            Renderer r = obj.GetComponent<Renderer>();
            if (r == null) return;

            // MaterialPropertyBlock으로 per-object 아웃라인 제어 (SRP Batcher는 Unlit이라 영향 없음)
            var mpb = new MaterialPropertyBlock();
            r.GetPropertyBlock(mpb);
            mpb.SetFloat("_OutlineEnabled", active ? 1f : 0f);
            mpb.SetColor("_OutlineColor", outlineColor);
            r.SetPropertyBlock(mpb);
        }

        /// <summary>Set outline on ALL non-popped balloons.</summary>
        public void SetAllOutlines(bool active, Color outlineColor)
        {
            foreach (var kvp in _balloonObjects)
            {
                if (kvp.Value == null) continue;
                if (_balloons.TryGetValue(kvp.Key, out BalloonData data) && data.isPopped) continue;
                SetOutline(kvp.Value, active, outlineColor);
            }
        }

        /// <summary>Set outline only on balloons of a specific color.</summary>
        public void SetOutlineByColor(int color, bool active, Color outlineColor)
        {
            foreach (var kvp in _balloonObjects)
            {
                if (kvp.Value == null) continue;
                if (!_balloons.TryGetValue(kvp.Key, out BalloonData data)) continue;
                if (data.isPopped) continue;
                if (data.color == color)
                    SetOutline(kvp.Value, active, outlineColor);
            }
        }

        /// <summary>
        /// 화면 클릭 위치에서 가장 가까운 풍선 ID 반환. Collider 없이 동작.
        /// 월드 좌표 XZ 거리 기반. threshold 이내만 반환, 없으면 -1.
        /// </summary>
        public int FindNearestBalloonAtWorldPos(Vector3 worldPos, float threshold = 1f)
        {
            int bestId = -1;
            float bestDist = threshold * threshold; // sqr 비교

            foreach (var kvp in _balloons)
            {
                if (kvp.Value.isPopped) continue;
                float dx = kvp.Value.position.x - worldPos.x;
                float dz = kvp.Value.position.z - worldPos.z;
                float sqrDist = dx * dx + dz * dz;
                if (sqrDist < bestDist)
                {
                    bestDist = sqrDist;
                    bestId = kvp.Key;
                }
            }
            return bestId;
        }

        /// <summary>
        /// Finds the nearest active balloon to a screen point using the rendered transform position.
        /// Used by Zap item picking so camera tilt and runtime field scaling do not shift selection.
        /// </summary>
        public int FindNearestBalloonAtScreenPoint(Camera camera, Vector2 screenPosition, float thresholdPixels = 0f)
        {
            if (camera == null) return -1;

            int bestId = -1;
            float baseThreshold = thresholdPixels > 0f
                ? thresholdPixels
                : EstimateBalloonPickRadiusPixels(camera);
            float bestDist = baseThreshold * baseThreshold;

            foreach (var kvp in _balloons)
            {
                BalloonData data = kvp.Value;
                if (data == null || data.isPopped) continue;

                Vector3 world = GetBalloonWorldPosition(data.balloonId);
                Vector3 screen = camera.WorldToScreenPoint(world);
                if (screen.z < camera.nearClipPlane || screen.z > camera.farClipPlane) continue;

                float candidateRadius = baseThreshold;
                if (IsSizedFieldGimmick(data.gimmickType))
                    candidateRadius *= Mathf.Max(1f, Mathf.Max(data.sizeW, data.sizeH) * 0.75f);

                float dx = screen.x - screenPosition.x;
                float dy = screen.y - screenPosition.y;
                float sqrDist = dx * dx + dy * dy;
                float allowed = candidateRadius * candidateRadius;
                if (sqrDist < bestDist && sqrDist <= allowed)
                {
                    bestDist = sqrDist;
                    bestId = kvp.Key;
                }
            }

            return bestId;
        }

        private float EstimateBalloonPickRadiusPixels(Camera camera)
        {
            const float fallback = 72f;
            if (camera == null || !GameManager.HasInstance)
                return fallback;

            float cellSpacing = Mathf.Max(0.1f, GameManager.Instance.Board.cellSpacing);
            Vector3 origin = new Vector3(
                GameManager.Instance.Board.boardCenterX,
                0.1f,
                GameManager.Instance.Board.balloonCenterZ);

            Vector3 center = camera.WorldToScreenPoint(origin);
            Vector3 right = camera.WorldToScreenPoint(origin + Vector3.right * cellSpacing);
            Vector3 forward = camera.WorldToScreenPoint(origin + Vector3.forward * cellSpacing);

            float pxPerCell = Mathf.Max(
                Vector2.Distance(new Vector2(center.x, center.y), new Vector2(right.x, right.y)),
                Vector2.Distance(new Vector2(center.x, center.y), new Vector2(forward.x, forward.y)));

            if (pxPerCell <= 1f) return fallback;
            return Mathf.Clamp(pxPerCell * 0.65f, 42f, 110f);
        }

        /// <summary>Clear all outlines on all balloons.</summary>
        public void ClearAllOutlines()
        {
            foreach (var kvp in _balloonObjects)
            {
                if (kvp.Value == null) continue;
                SetOutline(kvp.Value, false, Color.black);
            }
        }

        /// <summary>프리팹 고유 컴포넌트(Shadow, Particle 등) 건드리지 않고 색상만 적용.
        /// tag "BalloonMesh"가 있는 Renderer만 변경. 없으면 루트 Renderer만.</summary>
        private static void ApplyTintToObject(GameObject obj, Color color)
        {
            // BalloonIdentifier에 Renderer + 기반 Material이 할당되어 있으면 복제 방식
            BalloonIdentifier bi = obj.GetComponent<BalloonIdentifier>();
            if (bi != null && bi.HasColorRenderers)
            {
                bi.ApplyColor(color);
                return;
            }

            // fallback: 기존 방식
            Material shared = GetOrCreateSharedMaterial(color);
            if (shared == null) return;

            Renderer r = obj.GetComponent<Renderer>();
            if (r != null)
            {
                r.enabled = true;
                r.sharedMaterial = shared;
                return;
            }

            MeshRenderer mr = obj.GetComponentInChildren<MeshRenderer>();
            if (mr != null)
            {
                mr.enabled = true;
                mr.sharedMaterial = shared;
            }
        }

        /// <summary>
        /// 기믹 타입에 따라 사용할 풀 키를 반환. 전용 비주얼 프리팹이 없는 기믹은
        /// 기본 Balloon 풀로 폴백. (Lock_Key는 별도 처리이므로 여기서 다루지 않음)
        /// </summary>
        private static string ResolveGimmickPoolKey(string gimmickType)
        {
            string normalized = GimmickDisplayName.Normalize(gimmickType);
            if (string.IsNullOrEmpty(normalized)) return PoolKey;
            switch (normalized)
            {
                case GimmickBarricade: return BarricadePoolKey;
                // ROLLBACK_PINATA_WOODENBOARD_PREFAB:
                // The current Pinata visual is authored in Resources/Prefabs/WoodenBoard
                // and addressed as prefab_WoodenBoard.
                case GimmickPinata:    return WoodenBoardPoolKey;
                case GimmickPinataBox: return TargetBoxPoolKey;
                case GimmickPin:       return WoodenBoardPoolKey;
                // ROLLBACK_WALL_IRONBOX_PREFAB:
                // NewFeature maps Wall/IronWall to IronBox; use the same runtime prefab.
                case GimmickWall:      return IronBoxPoolKey;
                default:               return PoolKey;
            }
        }

        // FrozenLayer 오버레이 추적: balloonId → 자식으로 부착된 FrozenLayer GameObject
        private readonly Dictionary<int, GameObject> _frozenOverlays = new Dictionary<int, GameObject>();

        /// <summary>풍선보다 커 보이게 하는 오버레이 스케일 배율 (얼음 쉘이 풍선을 감싼 모습).
        /// 명세: "FrozenLayer 가 풍선보다 커보여야함."</summary>
        private const float FROZEN_OVERLAY_SCALE = 1.3f;

        /// <summary>
        /// FrozenLayer 프리팹을 풍선의 자식으로 부착 (얼음 쉘 비주얼).
        /// Ice (Lv.201) / Frozen_Dart (Lv.241) 기믹용. 풍선 자체는 교체하지 않고 오버레이만 추가.
        /// 부착 시 풍선 본체 비주얼 숨김 → 얼음만 보임. 해동 시 ReturnFrozenOverlay 로 제거 →
        /// 원본 풍선 노출.
        /// </summary>
        private void AttachFrozenOverlay(int balloonId, GameObject parentBalloon)
        {
            if (parentBalloon == null) return;
            if (!ObjectPoolManager.HasInstance) return;
            if (_frozenOverlays.ContainsKey(balloonId)) return; // 중복 부착 방지
            if (!ObjectPoolManager.Instance.HasPool(FrozenLayerPoolKey)) return;

            GameObject overlay = ObjectPoolManager.Instance.Get(FrozenLayerPoolKey);
            if (overlay == null) return;

            overlay.transform.SetParent(parentBalloon.transform, false);
            overlay.transform.localPosition = Vector3.zero;
            overlay.transform.localRotation = Quaternion.identity;
            // 풍선보다 크게 — 얼음이 풍선을 감싼 시각.
            overlay.transform.localScale = Vector3.one * FROZEN_OVERLAY_SCALE;
            overlay.SetActive(true);

            // 풍선 본체 숨김 (얼음만 보이게).
            var bi = parentBalloon.GetComponent<BalloonIdentifier>();
            if (bi != null) bi.SetVisible(false);

            _frozenOverlays[balloonId] = overlay;
        }

        /// <summary>
        /// 특정 풍선의 FrozenLayer 오버레이를 풀로 반환 (해동/팝/클리어 시 호출).
        /// 풍선 본체 비주얼 다시 보이게 복원.
        /// </summary>
        private void ReturnFrozenOverlay(int balloonId)
        {
            if (!_frozenOverlays.TryGetValue(balloonId, out GameObject overlay)) return;
            _frozenOverlays.Remove(balloonId);

            // 풍선 본체 다시 보이게 (얼음 깨짐 → 원래 풍선 노출).
            if (_balloonObjects.TryGetValue(balloonId, out GameObject balloonObj) && balloonObj != null)
            {
                var bi = balloonObj.GetComponent<BalloonIdentifier>();
                if (bi != null) bi.SetVisible(true);
            }

            if (overlay == null) return;
            overlay.transform.SetParent(null, false);
            if (ObjectPoolManager.HasInstance)
                ObjectPoolManager.Instance.Return(FrozenLayerPoolKey, overlay);
            else
                overlay.SetActive(false);
        }

        /// <summary>모든 FrozenLayer 오버레이를 풀로 반환 (보드 클리어 시).
        /// 풍선 본체 visibility 도 복원 — 다음 풀 재사용 안전성 확보.</summary>
        private void ReturnAllFrozenOverlays()
        {
            foreach (var kvp in _frozenOverlays)
            {
                int balloonId = kvp.Key;
                GameObject overlay = kvp.Value;

                // 풍선 본체 visibility 복원
                if (_balloonObjects.TryGetValue(balloonId, out GameObject balloonObj) && balloonObj != null)
                {
                    var bi = balloonObj.GetComponent<BalloonIdentifier>();
                    if (bi != null) bi.SetVisible(true);
                }

                if (overlay == null) continue;
                overlay.transform.SetParent(null, false);
                if (ObjectPoolManager.HasInstance)
                    ObjectPoolManager.Instance.Return(FrozenLayerPoolKey, overlay);
                else
                    overlay.SetActive(false);
            }
            _frozenOverlays.Clear();
        }

        #endregion

        #region Private Methods — Pop Logic

        private PopResult ExecutePop(BalloonData data)
        {
            float __popTotalStamp = InGamePerfLogger.StartStampMs();
            // Mark popped in data
            float __markStamp = InGamePerfLogger.StartStampMs();
            data.isPopped = true;
            _balloons[data.balloonId] = data;
            RemainingCount = Mathf.Max(0, RemainingCount - 1);
            PoppedCount++;
            InGamePerfLogger.EndSection(__markStamp, "Balloon.ExecutePop.MarkData");

            float __scaleStamp = InGamePerfLogger.StartStampMs();
            float effectScaleMultiplier = GetBalloonEffectScaleMultiplier(data.balloonId);
            InGamePerfLogger.EndSection(__scaleStamp, "Balloon.ExecutePop.GetScale");

            // Return visual to pool
            float __returnObjStamp = InGamePerfLogger.StartStampMs();
            ReturnBalloonObject(data.balloonId, effectScaleMultiplier);
            InGamePerfLogger.EndSection(__returnObjStamp, "Balloon.ExecutePop.ReturnObject");

            // Remove from position index
            float __invalidateStamp = InGamePerfLogger.StartStampMs();
            RemovePositionIndexForBalloon(data);

            // ROLLBACK_DART_TARGET_CACHE_DIRTY:
            // DirectionalTargeting is now dirty-driven instead of frame-driven. A pop is the exact
            // moment the outer contour changes, so invalidate once here and rebuild on the next
            // targeting query instead of rebuilding every frame.
            DirectionalTargeting.InvalidateCache();
            InGamePerfLogger.EndSection(__invalidateStamp, "Balloon.ExecutePop.Invalidate");

            // Publish pop event
            float __publishStamp = InGamePerfLogger.StartStampMs();
            EventBus.Publish(new OnBalloonPopped
            {
                balloonId = data.balloonId,
                color     = data.color,
                position  = data.position,
                effectScaleMultiplier = effectScaleMultiplier
            });
            InGamePerfLogger.EndSection(__publishStamp, "Balloon.ExecutePop.PublishPop");

            // Trigger gimmick side-effects (base behavior only)
            PopResult result = new PopResult
            {
                success    = true,
                balloonId  = data.balloonId,
                color      = data.color,
                position   = data.position,
                gimmickType = data.gimmickType
            };

            float __gimmickStamp = InGamePerfLogger.StartStampMs();
            ProcessGimmickAfterPop(data, result);
            InGamePerfLogger.EndSection(__gimmickStamp, "Balloon.ExecutePop.Gimmick");

            // ROLLBACK_CONTOUR_TARGET_DIAG:
            // Diagnose whether DirectionalTargeting's frame cache still points at
            // the popped balloon or shifts contour candidates unexpectedly.
            float __diagStamp = InGamePerfLogger.StartStampMs();
            DirectionalTargeting.LogContourAfterPop(data.balloonId, data.color, data.position, data.gimmickType);
            InGamePerfLogger.EndSection(__diagStamp, "Balloon.ExecutePop.ContourDiag");

            // 외곽 변경 가능 — 외곽 풍선만 렌더링 toggle 시 Renderer state 갱신
            float __outlineStamp = InGamePerfLogger.StartStampMs();
            RefreshOutermostRendererState();
            InGamePerfLogger.EndSection(__outlineStamp, "Balloon.ExecutePop.RefreshOutline");
            InGamePerfLogger.EndSection(__popTotalStamp, "Balloon.ExecutePop.Total");

            return result;
        }

        private PopResult ProcessPinataHit(BalloonData data)
        {
            data.hitCount++;
            _balloons[data.balloonId] = data;

            // HP 텍스트 + 피격/파괴 이펙트
            int requiredHits = data.maxHP > 0 ? data.maxHP : PinataRequiredHits;
            if (_balloonObjects.TryGetValue(data.balloonId, out GameObject hitObj) && hitObj != null)
            {
                int remainHP = Mathf.Max(0, requiredHits - data.hitCount);
                var gi = hitObj.GetComponent<GimmickIdentifier>();
                if (gi != null)
                {
                    gi.UpdateHP(remainHP);
                    gi.PlayHitEffect();
                    if (remainHP <= 0) gi.PlayEndEffect();
                }
            }

            if (data.hitCount < requiredHits)
            {
                // Partial hit — not yet destroyed → 풍선 pop SFX 라우팅용 (isDestroyed=false)
                EventBus.Publish(new OnGimmickTriggered
                {
                    gimmickType  = GimmickPinata,
                    targetId     = data.balloonId,
                    isDestroyed  = false
                });

                return new PopResult
                {
                    success     = false,
                    hitAccepted = true,
                    reason      = "PinataPartialHit",
                    balloonId   = data.balloonId,
                    gimmickType = GimmickPinata
                };
            }

            // Final hit — execute full pop. 파괴 SFX(woodbreak) 라우팅용 publish (isDestroyed=true).
            EventBus.Publish(new OnGimmickTriggered
            {
                gimmickType  = GimmickPinata,
                targetId     = data.balloonId,
                isDestroyed  = true
            });
            return ExecutePop(data);
        }

        /// <summary>
        /// Barricade: destructible wall with HP.
        /// Each hit reduces HP by 1. Destroyed when HP reaches 0.
        /// While alive, blocks dart path (occupancy map). Default HP = 2.
        /// </summary>
        private PopResult ProcessBarricadeHit(BalloonData data)
        {
            data.hitCount++;
            _balloons[data.balloonId] = data;

            int requiredHits = data.maxHP > 0 ? data.maxHP : 2;

            if (_balloonObjects.TryGetValue(data.balloonId, out GameObject hitObj) && hitObj != null)
            {
                var gi = hitObj.GetComponent<GimmickIdentifier>();
                int remainHP = Mathf.Max(0, requiredHits - data.hitCount);
                if (gi != null)
                {
                    gi.UpdateHP(remainHP);
                    gi.PlayHitEffect();
                    if (remainHP <= 0) gi.PlayEndEffect();
                }

                ApplyBarricadeVisualTransform(hitObj, data);
            }

            if (data.hitCount < requiredHits)
            {
                return new PopResult
                {
                    success     = false,
                    hitAccepted = true,
                    reason      = "BarricadePartialHit",
                    balloonId   = data.balloonId,
                    gimmickType = GimmickBarricade
                };
            }

            return ExecutePop(data);
        }

        /// <summary>
        /// Frozen Dart: 2-hit field gimmick.
        /// 1st hit (hitCount 0→1): thaw — removes frozen layer, converts to normal balloon.
        /// 2nd hit: standard pop.
        /// Adjacent pops also thaw (like Ice, but requires direct hit to pop afterward).
        /// </summary>
        private PopResult ProcessFrozenDartHit(BalloonData data)
        {
            data.hitCount++;
            _balloons[data.balloonId] = data;

            if (data.hitCount < 2)
            {
                // Thaw: convert to normal balloon (still alive, now poppable in 1 hit)
                data.gimmickType = GimmickNone;
                _balloons[data.balloonId] = data;

                // 얼음 쉘 오버레이 제거 (해동)
                ReturnFrozenOverlay(data.balloonId);

                // Visual: restore original color from frozen tint
                if (_balloonObjects.TryGetValue(data.balloonId, out GameObject obj) && obj != null)
                {
                    int colorIdx = Mathf.Clamp(data.color, 0, BalloonColors.Length - 1);
                    ApplyTintToObject(obj, BalloonColors[colorIdx]);
                }

                // ROLLBACK_FROZEN_THAW_TARGET_CACHE_INVALIDATION:
                // Thawing changes this cell from a frozen target to a normal target without a pop.
                DirectionalTargeting.InvalidateCache();
                RefreshOutermostRendererState();

                EventBus.Publish(new OnGimmickTriggered
                {
                    gimmickType = GimmickFrozenDart,
                    targetId    = data.balloonId
                });

                return new PopResult
                {
                    success     = false,
                    hitAccepted = true,
                    reason      = "FrozenDartThawed",
                    balloonId   = data.balloonId,
                    gimmickType = GimmickFrozenDart
                };
            }

            // 2nd hit — fully thawed, execute pop
            return ExecutePop(data);
        }

        private void ProcessGimmickAfterPop(BalloonData data, PopResult result)
        {
            // Post-pop gimmick side effects:
            // - Ice HP, Lock-Key, Surprise, Hidden → GimmickProcessor.HandleAnyBalloonPopped (EventBus)
            // - Chain, Pin, PinataBox → here (requires BalloonController internal access)
            switch (data.gimmickType)
            {
                case GimmickChain:
                    // ROLLBACK_DISABLE_FIELD_CHAIN_POP_20260602:
                    // Previous behavior chain-popped same-color adjacent balloons after a normal dart hit.
                    // Linked Dart Box now uses the same "Chain" key on holder/queue data, so field balloon
                    // pops must stay dart:balloon = 1:1 and should not trigger extra balloon pops here.
                    break;

                case GimmickPinataBox:
                    // PinataBox 는 단일 타격으로 파괴 — isDestroyed=true 로 woodbreak SFX 라우팅.
                    EventBus.Publish(new OnGimmickTriggered { gimmickType = GimmickPinataBox, targetId = data.balloonId, isDestroyed = true });
                    break;

                case GimmickLockKey:
                    // OnKeyReleased는 ReturnBalloonObject에서 발행 (KeyVisual 분리 후)
                    break;

                case GimmickNone:
                default:
                    break;
            }

            // Pin은 인접 팝으로 제거 안 됨 — 같은 색 다트 직접 타격으로만 제거
            // RemoveAdjacentPins(data.position);  // 문서 기준 비활성

            // Ice(§11): 영역 공유 HP 모델 — 어떤 풍선이든 제거 시 GimmickProcessor.HandleAnyBalloonPopped
            // (OnBalloonPopped 구독)가 영역 HP 를 깎고, HP 0 시 BalloonController.BreakAllIce 로 일괄 해제.
            // (이전 인접 팝 해동 ThawAdjacentIce 모델은 폐기 — 명세 HP 모델로 리라이트.)

            // All pops thaw adjacent Frozen Dart balloons
            ThawAdjacentFrozenDarts(data);
        }

        private void PlayRevealEffect(GameObject obj, int colorIdx, int balloonId)
        {
            if (obj == null) return;

            // ROLLBACK_FIELD_REVEAL_VISIBLE_EFFECT:
            // The previous reveal was only a tiny punch scale, so Surprise/Hidden and Ice thaw
            // could look like a silent color swap. Reuse the pooled pop particle at a smaller
            // scale and run one short pulse without changing gameplay state.
            obj.transform.DOKill();
            Vector3 baseScale = obj.transform.localScale;
            Sequence seq = DOTween.Sequence();
            seq.Append(obj.transform.DOScale(baseScale * 1.18f, 0.10f).SetEase(Ease.OutQuad));
            seq.Append(obj.transform.DOScale(baseScale, 0.12f).SetEase(Ease.OutBack));
            seq.SetLink(obj, LinkBehaviour.KillOnDisable);

            int ci = Mathf.Clamp(colorIdx, 0, BalloonColors.Length - 1);
            float effectScale = Mathf.Max(0.15f, GetBalloonEffectScaleMultiplier(balloonId) * 0.65f);
            PopEffectPool.Play(obj.transform.position, BalloonColors[ci], this, effectScale);
        }

        private void RevealAdjacentHiddenBalloons(Vector3 position)
        {
            int count = CopyAdjacentIds(GetAdjacentBalloonIds(position));
            for (int i = 0; i < count; i++)
            {
                int id = _adjCopyBuffer[i];
                if (!_hiddenBalloons.Contains(id)) continue;
                if (!_balloons.TryGetValue(id, out BalloonData neighbor)) continue;
                if (neighbor.isPopped) continue;

                _hiddenBalloons.Remove(id);

                // Restore actual balloon color (was showing grey)
                if (_balloonObjects.TryGetValue(id, out GameObject obj) && obj != null)
                {
                    int colorIdx = Mathf.Clamp(neighbor.color, 0, BalloonColors.Length - 1);
                    ApplyTintToObject(obj, BalloonColors[colorIdx]);

                    // Reveal punch animation
                    // [Leak fix 2026-05-11] SetLink(KillOnDisable) — pool 반환 시 자동 kill. 원본: 동일 코드 .SetLink 없이.
                    PlayRevealEffect(obj, colorIdx, id);
                }

                EventBus.Publish(new OnGimmickTriggered
                {
                    gimmickType = GimmickSurprise, // 인접 팝으로 필드 Surprise 공개
                    targetId    = id
                });
            }
        }

        /// <summary>BFS 큐 기반 체인 팝 (재귀 대신 → StackOverflow 방지)</summary>
        private readonly Queue<int> _chainPopQueue = new Queue<int>();
        private readonly HashSet<int> _chainPopVisited = new HashSet<int>();

        private void ChainPopAdjacentSameColor(BalloonData source)
        {
            _chainPopQueue.Clear();
            _chainPopVisited.Clear();
            _chainPopVisited.Add(source.balloonId);

            // 시작 풍선의 인접 같은 색 추가
            List<int> startAdj = GetAdjacentBalloonIds(source.position);
            for (int i = 0; i < startAdj.Count; i++)
            {
                if (_chainPopVisited.Add(startAdj[i]))
                    _chainPopQueue.Enqueue(startAdj[i]);
            }

            int safety = 0;
            const int MAX_CHAIN = 500;

            while (_chainPopQueue.Count > 0 && safety++ < MAX_CHAIN)
            {
                int id = _chainPopQueue.Dequeue();
                if (!_balloons.TryGetValue(id, out BalloonData neighbor)) continue;
                if (neighbor.isPopped) continue;
                if (neighbor.color != source.color) continue;
                if (_hiddenBalloons.Contains(id)) continue;

                ExecutePop(neighbor);

                // 팝된 풍선의 인접도 큐에 추가 (HashSet으로 O(1) 중복 체크)
                List<int> adj = GetAdjacentBalloonIds(neighbor.position);
                for (int i = 0; i < adj.Count; i++)
                {
                    if (_chainPopVisited.Add(adj[i]))
                        _chainPopQueue.Enqueue(adj[i]);
                }
            }
        }

        /// <summary>
        /// Destroys all adjacent Pin balloons. Pins cannot be targeted by darts —
        /// they are only removed when a neighboring balloon is popped.
        /// </summary>
        private void RemoveAdjacentPins(Vector3 position)
        {
            int count = CopyAdjacentIds(GetAdjacentBalloonIds(position));
            for (int i = 0; i < count; i++)
            {
                int id = _adjCopyBuffer[i];
                if (!_balloons.TryGetValue(id, out BalloonData neighbor)) continue;
                if (neighbor.isPopped) continue;
                if (neighbor.gimmickType != GimmickPin) continue;

                ExecutePop(neighbor);
            }
        }

        /// <summary>
        /// [#13 / 기믹명세 §11] 한 Ice 영역의 공유 HP 가 0 에 도달하면 GimmickProcessor 가 호출 —
        /// 해당 영역의 얼음만 동시에 해제한다. 얼음이 깨지며 아래 가려진 풍선(=Ice 풍선 본체)이 노출되어
        /// 다트로 타격 가능해진다. (이전 인접 팝 해동 ThawAdjacentIce / 전체 일괄 BreakAllIce 모델은 폐기.)
        /// </summary>
        public void BreakIceRegion(IEnumerable<int> ids)
        {
            if (ids == null) return;
            bool thawedAny = false;
            foreach (int id in ids)
            {
                if (!_balloons.TryGetValue(id, out BalloonData ice)) continue;
                if (ice.isPopped || ice.gimmickType != GimmickIce) continue;

                // 얼음 해제: 일반 풍선으로 전환 (이제 다트로 타격 가능)
                ice.gimmickType = GimmickNone;
                _balloons[id] = ice;

                // 얼음 쉘 오버레이 제거 → (앵커) 풍선 본체 재표시
                ReturnFrozenOverlay(id);

                // Visual: 본체 강제 표시(블록 비앵커 셀은 오버레이 없이 숨겨져 있었음) → 원래 색 복원 + 노출 연출
                if (_balloonObjects.TryGetValue(id, out GameObject obj) && obj != null)
                {
                    var bi = obj.GetComponent<BalloonIdentifier>();
                    if (bi != null) bi.SetVisible(true);
                    int colorIdx = Mathf.Clamp(ice.color, 0, BalloonColors.Length - 1);
                    ApplyTintToObject(obj, BalloonColors[colorIdx]);
                    PlayRevealEffect(obj, colorIdx, id);
                }

                EventBus.Publish(new OnGimmickTriggered
                {
                    gimmickType = GimmickIce,
                    targetId    = id
                });

                thawedAny = true;
            }

            if (thawedAny)
            {
                // Ice 가 팝 없이 타격 가능해지므로 캐시 윤곽을 재구성.
                DirectionalTargeting.InvalidateCache();
                RefreshOutermostRendererState();
            }
        }

        /// <summary>
        /// [#13 / 기믹명세 §11] 필드의 Ice 풍선들을 인접 연결 성분(영역)으로 묶어 반환.
        /// 각 영역 = 공유 HP 단위. GimmickProcessor.InitIceRegions 가 셋업 직후 1회 호출.
        /// 4방향 인접(상하좌우) flood-fill 기준.
        /// </summary>
        public List<List<int>> GetIceRegions()
        {
            var regions = new List<List<int>>();
            var visited = new HashSet<int>();
            var stack = new Stack<int>();

            foreach (var kvp in _balloons)
            {
                if (kvp.Value.isPopped || kvp.Value.gimmickType != GimmickIce) continue;
                if (visited.Contains(kvp.Key)) continue;

                var region = new List<int>();
                stack.Clear();
                stack.Push(kvp.Key);
                visited.Add(kvp.Key);

                while (stack.Count > 0)
                {
                    int id = stack.Pop();
                    region.Add(id);
                    if (!_balloons.TryGetValue(id, out BalloonData cur)) continue;

                    int cnt = CopyAdjacentIds(GetAdjacentBalloonIdsForBalloon(id, cur.position));
                    for (int i = 0; i < cnt; i++)
                    {
                        int nb = _adjCopyBuffer[i];
                        if (visited.Contains(nb)) continue;
                        if (!_balloons.TryGetValue(nb, out BalloonData nbData)) continue;
                        if (nbData.isPopped || nbData.gimmickType != GimmickIce) continue;
                        visited.Add(nb);
                        stack.Push(nb);
                    }
                }
                regions.Add(region);
            }
            return regions;
        }

        /// <summary>
        /// Thaws adjacent Frozen Dart balloons. Unlike Ice (which becomes targetable),
        /// Frozen Dart thaw converts it to a normal balloon that can be popped in 1 hit.
        /// </summary>
        private void ThawAdjacentFrozenDarts(BalloonData source)
        {
            int count = CopyAdjacentIds(GetAdjacentBalloonIdsForBalloon(source.balloonId, source.position));
            bool thawedAny = false;
            for (int i = 0; i < count; i++)
            {
                int id = _adjCopyBuffer[i];
                if (!_balloons.TryGetValue(id, out BalloonData neighbor)) continue;
                if (neighbor.isPopped) continue;
                if (neighbor.gimmickType != GimmickFrozenDart) continue;

                // Thaw: convert to normal balloon (now poppable in 1 hit)
                neighbor.gimmickType = GimmickNone;
                neighbor.hitCount = 1; // Mark as already thawed so next hit pops
                _balloons[id] = neighbor;

                // 얼음 쉘 오버레이 제거
                ReturnFrozenOverlay(id);

                // Visual: restore original color
                if (_balloonObjects.TryGetValue(id, out GameObject obj) && obj != null)
                {
                    int colorIdx = Mathf.Clamp(neighbor.color, 0, BalloonColors.Length - 1);
                    ApplyTintToObject(obj, BalloonColors[colorIdx]);
                }

                EventBus.Publish(new OnGimmickTriggered
                {
                    gimmickType = GimmickFrozenDart,
                    targetId    = id
                });

                thawedAny = true;
            }

            if (thawedAny)
            {
                // ROLLBACK_FROZEN_THAW_TARGET_CACHE_INVALIDATION:
                // Adjacent thaw changes targetability without removing the object.
                DirectionalTargeting.InvalidateCache();
                RefreshOutermostRendererState();
            }
        }

        /// <summary>
        /// Spawns new balloons at random adjacent empty grid cells.
        /// Used by Spawner_T (1 balloon) and Spawner_O (2 balloons).
        /// Returns the actual number of balloons spawned.
        /// </summary>
        private int SpawnAtAdjacentEmpty(Vector3 position, int count)
        {
            List<Vector3> emptyPositions = GetAdjacentEmptyPositions(position);
            if (emptyPositions.Count == 0) return 0;

            // Shuffle empty positions for randomness
            for (int i = emptyPositions.Count - 1; i > 0; i--)
            {
                int j = Random.Range(0, i + 1);
                Vector3 tmp = emptyPositions[i];
                emptyPositions[i] = emptyPositions[j];
                emptyPositions[j] = tmp;
            }

            int spawned = 0;
            int maxColors = Mathf.Min(BalloonColors.Length, 8); // Use first 8 colors

            for (int i = 0; i < count && i < emptyPositions.Count; i++)
            {
                int color = Random.Range(0, maxColors);
                int id = _nextBalloonId++;

                BalloonData newData = new BalloonData
                {
                    balloonId   = id,
                    color       = color,
                    position    = emptyPositions[i],
                    isPopped    = false,
                    gimmickType = GimmickNone,
                    hitCount    = 0
                };

                _balloons[id] = newData;
                RemainingCount++;

                GameObject obj = GetOrCreateBalloonObject(id, emptyPositions[i], color);
                if (obj != null)
                {
                    _balloonObjects[id] = obj;

                    // Spawn animation: scale up from zero
                    obj.transform.localScale = Vector3.zero;
                    // [Leak fix 2026-05-11] SetLink(KillOnDisable) — pool 재사용 시 SetActive(false) 에서 자동 kill. 원본: .SetLink 없이.
                    obj.transform.DOScale(Vector3.one * _balloonScale, 0.25f)
                        .SetEase(Ease.OutBack)
                        .SetLink(obj, LinkBehaviour.KillOnDisable);
                }

                // Update position index
                _positionIndex[ToGridKey(emptyPositions[i])] = id;

                EventBus.Publish(new OnBalloonSpawned
                {
                    balloonId = id,
                    color     = color,
                    position  = emptyPositions[i]
                });

                spawned++;
            }

            return spawned;
        }

        /// <summary>
        /// Returns world positions of empty adjacent grid cells (4-directional).
        /// </summary>
        private List<Vector3> GetAdjacentEmptyPositions(Vector3 position)
        {
            List<Vector3> empty = new List<Vector3>();
            Vector3Int center = ToGridKey(position);

            Vector3Int[] directions =
            {
                new Vector3Int( 1, 0,  0),
                new Vector3Int(-1, 0,  0),
                new Vector3Int( 0, 0,  1),
                new Vector3Int( 0, 0, -1)
            };

            foreach (Vector3Int dir in directions)
            {
                Vector3Int neighbor = center + dir;
                if (!_positionIndex.ContainsKey(neighbor))
                {
                    // Convert grid key back to world position
                    Vector3 worldPos = new Vector3(
                        neighbor.x * _cellSpacing,
                        position.y,
                        neighbor.z * _cellSpacing
                    );
                    empty.Add(worldPos);
                }
            }

            return empty;
        }

        private void ReturnBalloonObject(int balloonId, float effectScaleMultiplier)
        {
            float __totalStamp = InGamePerfLogger.StartStampMs();
            if (!_balloonObjects.TryGetValue(balloonId, out GameObject obj)) return;
            if (obj == null) return;

            _balloonObjects.Remove(balloonId);

            var identifier = obj.GetComponent<BalloonIdentifier>();

            float savedScale = _balloonScale;
            string returnKey = PoolKey;
            int popColorIdx = 0;
            if (_balloons.TryGetValue(balloonId, out BalloonData retData))
            {
                returnKey = ResolveGimmickPoolKey(retData.gimmickType);
                popColorIdx = retData.color;
            }

            // FrozenLayer 오버레이가 붙어있다면 먼저 풀로 반환
            float __overlayStamp = InGamePerfLogger.StartStampMs();
            ReturnFrozenOverlay(balloonId);
            InGamePerfLogger.EndSection(__overlayStamp, "Balloon.ReturnObject.FrozenOverlay");

            // 풍선 스케일업 → 완료 후 PopEffectPool 재생 + 풍선 풀 반환.
            // 이전: BalloonIdentifier에 _popEffect 자식 부착 → detach/reattach + 풍선마다 별도 인스턴스 → 부하.
            // 이후: 단일 CircleParticle 풀에서 가져와 색상 적용 + play, 끝나면 풀 반환.
            // [Leak fix 2026-05-11] 새 Sequence 시작 전 stale tween 정리 — DOPunchScale 등 진행 중이면 leak + 시각 충돌.
            float __tweenSetupStamp = InGamePerfLogger.StartStampMs();
            obj.transform.DOKill();
            float scaleUpDuration = GameManager.Instance.Board.popScaleDuration;
            float scaleUpMult = GameManager.Instance.Board.popScaleMultiplier;
            Vector3 popPos = obj.transform.position;
            Sequence seq = DOTween.Sequence();
            // SetUpdate(true): 이어하기는 fail 팝업(PauseManager, timeScale=0)이 열린 상태에서 풍선을
            //   pop 하므로, scaled tween 이면 시퀀스(스케일업→풀반환 콜백)가 정지해 풍선이 화면에 남는다.
            //   unscaled 로 돌려 timeScale=0 에서도 시각 제거가 완료되게 함. (timeScale=1 일반 플레이에선 동일 동작.)
            seq.SetUpdate(true);
            seq.Append(obj.transform.DOScale(Vector3.one * savedScale * scaleUpMult, scaleUpDuration).SetEase(Ease.OutQuad));
            seq.AppendCallback(() =>
            {
                // 스케일업 완료 시점에 애니메이터 Pop 트리거 (파티클은 PopEffectPool 가 처리)
                if (identifier != null)
                    identifier.MarkPopped();

                int ci = Mathf.Clamp(popColorIdx, 0, BalloonColors.Length - 1);
                PopEffectPool.Play(popPos, BalloonColors[ci], this, effectScaleMultiplier);

                if (obj != null && ObjectPoolManager.HasInstance)
                {
                    obj.transform.localScale = Vector3.one * savedScale;
                    ObjectPoolManager.Instance.Return(returnKey, obj);
                }
            });
            InGamePerfLogger.EndSection(__tweenSetupStamp, "Balloon.ReturnObject.TweenSetup");
            InGamePerfLogger.EndSection(__totalStamp, "Balloon.ReturnObject.Total");
        }

        /// <summary>Key 프리팹이 포물선으로 Lock 보관함까지 비행 → 도착 시 잠금 해제.</summary>
        private float GetBalloonEffectScaleMultiplier(int balloonId)
        {
            if (_balloonObjects.TryGetValue(balloonId, out GameObject obj) && obj != null)
            {
                Vector3 scale = obj.transform.localScale;
                float visualScale = Mathf.Max(Mathf.Abs(scale.x), Mathf.Abs(scale.y), Mathf.Abs(scale.z));
                if (visualScale > 0.001f)
                    return Mathf.Max(0.01f, visualScale / DEFAULT_BALLOON_SCALE);
            }

            return Mathf.Max(0.01f, _balloonScale / DEFAULT_BALLOON_SCALE);
        }

        private IEnumerator FlyKeyToLock(Vector3 startPos, int pairId)
        {
            // Lock 보관함 찾기
            Vector3 targetPos = startPos + Vector3.up * 2f; // fallback
            if (HolderManager.HasInstance && HolderVisualManager.HasInstance)
            {
                HolderData[] holders = HolderManager.Instance.GetHolders();
                for (int i = 0; i < holders.Length; i++)
                {
                    if (holders[i].lockPairId == pairId && holders[i].isLocked)
                    {
                        GameObject targetObj = HolderVisualManager.Instance.GetHolderGameObject(holders[i].holderId);
                        if (targetObj != null)
                            targetPos = targetObj.transform.position + Vector3.up * 1.1f;
                        break;
                    }
                }
            }

            // Key 오브젝트 풀에서 가져오기
            if (!ObjectPoolManager.HasInstance)
            {
                if (HolderManager.HasInstance) HolderManager.Instance.UnlockHolder(pairId);
                yield break;
            }

            Vector3 spawnPos = startPos + Vector3.up * 0.3f;
            GameObject keyObj = ObjectPoolManager.Instance.Get("Key", spawnPos, Quaternion.Euler(90f, 0f, 0f));
            if (keyObj == null)
            {
                if (HolderManager.HasInstance) HolderManager.Instance.UnlockHolder(pairId);
                yield break;
            }
            keyObj.SetActive(true);

            // Phase 1: 위로 튕김 (0.15초)
            Vector3 bounceTop = spawnPos + Vector3.up * 1.2f;
            float t = 0f;
            while (t < 0.15f)
            {
                t += Time.deltaTime;
                float p = t / 0.15f;
                keyObj.transform.position = Vector3.Lerp(spawnPos, bounceTop, Mathf.Sin(p * Mathf.PI * 0.5f));
                yield return null;
            }

            // Phase 2: 포물선 비행 (0.5초)
            t = 0f;
            while (t < 0.5f)
            {
                t += Time.deltaTime;
                float p = Mathf.Clamp01(t / 0.5f);
                Vector3 linear = Vector3.Lerp(bounceTop, targetPos, p);
                float arc = 2f * 4f * p * (1f - p);
                keyObj.transform.position = linear + Vector3.up * arc;
                keyObj.transform.Rotate(Vector3.forward, 540f * Time.deltaTime);
                yield return null;
            }

            keyObj.transform.position = targetPos;
            keyObj.SetActive(false);
            if (ObjectPoolManager.HasInstance)
                ObjectPoolManager.Instance.Return("Key", keyObj);

            // 잠금 해제
            if (HolderManager.HasInstance)
                HolderManager.Instance.UnlockHolder(pairId);
        }

        #endregion

        #region Private Methods — Key Path Checking

        private void CheckKeysOnPop(OnBalloonPopped evt)
        {
            if (_activeKeyPairIds.Count == 0) return;
            var keysToRelease = new List<int>();
            foreach (var kvp in _activeKeyPairIds)
            {
                if (!_balloons.TryGetValue(kvp.Key, out BalloonData kd)) continue;
                if (kd.isPopped) continue;
                Vector3Int keyGrid = ToGridKey(kd.position);
                bool canReach = CanKeyReachBelt(keyGrid);
#if UNITY_EDITOR
                Debug.Log($"[Key A*]" + "stripped");
#endif
                if (canReach)
                    keysToRelease.Add(kvp.Key);
            }
            foreach (int keyId in keysToRelease)
            {
#if UNITY_EDITOR
                Debug.Log($"[Key A*]" + "stripped");
#endif
                ReleaseKey(keyId);
            }
        }

        private bool CanKeyReachBelt(Vector3Int startGrid)
        {
            // BFS from Key grid position to any edge of the balloon field
            // A cell is walkable if no non-popped balloon exists there
            var visited = new HashSet<Vector3Int>();
            var queue = new Queue<Vector3Int>();
            visited.Add(startGrid);
            queue.Enqueue(startGrid);

            while (queue.Count > 0)
            {
                var current = queue.Dequeue();
                // Check if current is at edge of grid (can escape to belt)
                if (!_positionIndex.ContainsKey(current) || current.Equals(startGrid))
                {
                    // Check if this position is at the boundary or outside the populated area
                    bool atEdge = false;
                    Vector3Int[] dirs = { new Vector3Int(1,0,0), new Vector3Int(-1,0,0), new Vector3Int(0,0,1), new Vector3Int(0,0,-1) };
                    foreach (var d in dirs)
                    {
                        Vector3Int neighbor = current + d;
                        if (!_positionIndex.ContainsKey(neighbor) && !visited.Contains(neighbor))
                        {
                            // Neighbor is empty and not in grid = belt edge reached
                            // A position with no balloon registered means it's open
                            atEdge = true;
                        }
                    }
                    if (atEdge && !current.Equals(startGrid)) return true;
                }

                // Expand to neighbors
                Vector3Int[] directions = { new Vector3Int(1,0,0), new Vector3Int(-1,0,0), new Vector3Int(0,0,1), new Vector3Int(0,0,-1) };
                foreach (var dir in directions)
                {
                    Vector3Int next = current + dir;
                    if (visited.Contains(next)) continue;
                    visited.Add(next);

                    // Check if this cell is passable (no non-popped balloon, or it's a popped balloon)
                    if (_positionIndex.TryGetValue(next, out int balloonId))
                    {
                        if (_balloons.TryGetValue(balloonId, out BalloonData bd) && !bd.isPopped)
                            continue; // Blocked by non-popped balloon
                    }
                    queue.Enqueue(next);
                }
            }
            return false;
        }

        private void ReleaseKey(int keyId)
        {
            if (!_balloons.TryGetValue(keyId, out BalloonData keyData)) return;
            if (!_activeKeyPairIds.TryGetValue(keyId, out int pairId)) return;

            _activeKeyPairIds.Remove(keyId);
            float effectScaleMultiplier = GetBalloonEffectScaleMultiplier(keyId);

            // Mark as popped so it's removed from position tracking
            keyData.isPopped = true;
            _balloons[keyId] = keyData;
            RemainingCount = Mathf.Max(0, RemainingCount - 1);
            PoppedCount++;
            RemovePositionIndexForBalloon(keyData);

            // Return Key visual to pool
            if (_balloonObjects.TryGetValue(keyId, out GameObject keyObj) && keyObj != null)
            {
                Vector3 keyPos = keyObj.transform.position;
                _balloonObjects.Remove(keyId);
                keyObj.SetActive(false);
                if (ObjectPoolManager.HasInstance)
                    ObjectPoolManager.Instance.Return("Key", keyObj);

                // Start flight animation
                if (pairId >= 0)
                    StartCoroutine(FlyKeyToLock(keyPos, pairId));
            }

            EventBus.Publish(new OnBalloonPopped
            {
                balloonId = keyId,
                color = keyData.color,
                position = keyData.position,
                effectScaleMultiplier = effectScaleMultiplier
            });
        }

        #endregion

        #region Private Methods — Spatial Helpers

        /// <summary>재사용 리스트 + 방향 배열 (GC 방지)</summary>
        private readonly List<int> _reusableAdjacentIds = new List<int>(4);
        /// <summary>순회 중 재진입 방지용 로컬 복사 버퍼 (최대 4방향)</summary>
        private readonly int[] _adjCopyBuffer = new int[4];

        /// <summary>_reusableAdjacentIds를 로컬 버퍼로 복사. 순회 중 재진입 안전.</summary>
        private int CopyAdjacentIds(List<int> src)
        {
            int count = Mathf.Min(src.Count, _adjCopyBuffer.Length);
            for (int i = 0; i < count; i++) _adjCopyBuffer[i] = src[i];
            return count;
        }
        private static readonly Vector3Int[] _adjacentDirs =
        {
            new Vector3Int( 1, 0,  0),
            new Vector3Int(-1, 0,  0),
            new Vector3Int( 0, 0,  1),
            new Vector3Int( 0, 0, -1)
        };

        private List<int> GetAdjacentBalloonIds(Vector3 position)
        {
            _reusableAdjacentIds.Clear();
            Vector3Int center = ToGridKey(position);

            for (int i = 0; i < _adjacentDirs.Length; i++)
            {
                Vector3Int neighbor = center + _adjacentDirs[i];
                if (_positionIndex.TryGetValue(neighbor, out int neighborId))
                {
                    if (!_reusableAdjacentIds.Contains(neighborId))
                        _reusableAdjacentIds.Add(neighborId);
                }
            }

            return _reusableAdjacentIds;
        }

        private List<int> GetAdjacentBalloonIdsForBalloon(int balloonId, Vector3 fallbackPosition)
        {
            _reusableAdjacentIds.Clear();

            if (!_balloons.TryGetValue(balloonId, out BalloonData data) || !UsesMultiCellOccupancy(data))
                return GetAdjacentBalloonIds(fallbackPosition);

            // ROLLBACK_MULTI_CELL_GIMMICK_ADJACENCY:
            // Sized field gimmicks occupy a rectangle from the authored bottom-left anchor. Adjacent
            // effects such as Surprise reveal and Frozen thaw must inspect every occupied edge cell,
            // not just the anchor cell.
            BuildOccupiedCells(data, _reusableOccupiedCells);
            for (int c = 0; c < _reusableOccupiedCells.Count; c++)
            {
                Vector3Int cell = _reusableOccupiedCells[c];
                for (int d = 0; d < _adjacentDirs.Length; d++)
                {
                    Vector3Int neighbor = cell + _adjacentDirs[d];
                    if (_positionIndex.TryGetValue(neighbor, out int neighborId)
                        && neighborId != balloonId
                        && !_reusableAdjacentIds.Contains(neighborId))
                    {
                        _reusableAdjacentIds.Add(neighborId);
                    }
                }
            }

            return _reusableAdjacentIds;
        }

        /// <summary>
        /// Converts a world-space Vector3 position to a grid cell key.
        /// cellSpacing 기준으로 나누어 정수 그리드 좌표로 변환.
        /// </summary>
        private Vector3Int ToGridKey(Vector3 worldPos)
        {
            // cellSpacing으로 나누어 인접 셀이 정확히 ±1 차이가 되도록 함
            return new Vector3Int(
                Mathf.RoundToInt(worldPos.x / _cellSpacing),
                0,
                Mathf.RoundToInt(worldPos.z / _cellSpacing)
            );
        }

        #endregion

        #region Private Methods — Event Handlers

        private void HandleLevelLoaded(OnLevelLoaded evt)
        {
            _currentLevelId = evt.levelId;

            // 모든 풍선 spawn 끝난 후 외곽 풍선만 렌더링 1회 적용 (toggle ON 시).
            // BoardStateManager 의 outermost cache 가 첫 호출에 build 되므로 자동.
            RefreshOutermostRendererState();
        }

        #endregion
    }

    // ─────────────────────────────────────────────
    // Data Types
    // ─────────────────────────────────────────────

    /// <summary>
    /// Runtime snapshot of a single balloon's state on the board.
    /// </summary>
    [System.Serializable]
    public class BalloonData
    {
        /// <summary>Unique identifier assigned at board setup.</summary>
        public int balloonId;

        /// <summary>Color index used for dart-matching logic.</summary>
        public int color;

        /// <summary>World-space position on the board.</summary>
        public Vector3 position;

        /// <summary>Whether this balloon has been popped.</summary>
        public bool isPopped;

        /// <summary>
        /// Gimmick type string. One of:
        /// "none", "Hidden", "Chain", "Pinata", "Spawner_T", "Pin", "Lock_Key",
        /// "Surprise", "Wall", "Spawner_O", "Pinata_Box", "Ice", "Frozen_Dart", "Color_Curtain".
        /// </summary>
        public string gimmickType;

        /// <summary>Hit counter for Pinata gimmick.</summary>
        public int hitCount;
        /// <summary>Piñata 최대 HP (설정값).</summary>
        public int maxHP = 2;

        /// <summary>Piñata 가로 크기 (1=기본).</summary>
        public int sizeW = 1;
        /// <summary>Piñata 세로 크기.</summary>
        public int sizeH = 1;
        public int lockPairId = -1;

        /// <summary>[Ice §11] 얼음 블록 변 길이(셀). 2=2×2 타일. 1=셀당. 영역 공유 HP·렌더 블록화에 사용.</summary>
        public int iceBlockSize = 1;

        /// <summary>Pinata_Box 셀별 알 색상 (len=sizeW*sizeH, row-major). null=레거시 단일 박스.</summary>
        public int[] eggColors;
        /// <summary>Pinata_Box 셀별 알 HP (eggColors 와 동일 길이/순서). 런타임에 차감됨.</summary>
        public int[] eggHps;

        /// <summary>FlexTube 그룹 ID. -1 = FlexTube cell 아님.</summary>
        public int flexTubeGroupId = -1;
        /// <summary>FlexTube paint 순서 인덱스. 0=StartCap, max=EndCap.</summary>
        public int flexTubeSequenceIndex = -1;
        /// <summary>FlexTube 부품 종류 — "StartCap" / "Segment" / "EndCap".</summary>
        public string flexTubePartType = "";
    }

    /// <summary>
    /// Input data for placing a single balloon during board setup.
    /// </summary>
    [System.Serializable]
    public class BalloonSetupData
    {
        public int color;
        public Vector3 position;
        public string gimmickType;

        /// <summary>
        /// Group id for Pinata multi-tile balloons.
        /// -1 means not part of a group.
        /// </summary>
        public int groupId = -1;
        public int sizeW = 1;
        public int sizeH = 1;
        public int hp = 0;
        public int lockPairId = -1;

        /// <summary>[Ice §11] 얼음 블록 변 길이(셀). 2=2×2 타일. 1=셀당(기본).</summary>
        public int iceBlockSize = 1;

        /// <summary>Pinata_Box 셀별 알 색상 (len=sizeW*sizeH, row-major). null=레거시 단일 박스.</summary>
        public int[] eggColors;
        /// <summary>Pinata_Box 셀별 알 HP (eggColors 와 동일 길이/순서).</summary>
        public int[] eggHps;

        /// <summary>FlexTube 그룹 ID. -1 = FlexTube 셀 아님.</summary>
        public int flexTubeGroupId = -1;
        /// <summary>FlexTube 부품 종류 (StartCap/Segment/EndCap).</summary>
        public string flexTubePartType = "";
        /// <summary>FlexTube 그룹 안 paint 순서 (0..N).</summary>
        public int flexTubeSequenceIndex = -1;
    }

    /// <summary>
    /// Result returned from BalloonController.PopBalloon().
    /// </summary>
    public class PopResult
    {
        public bool success;
        public bool hitAccepted;
        public string reason;
        public int balloonId;
        public int color;
        public Vector3 position;
        public string gimmickType;

        /// <summary>Number of new balloons spawned by Spawner gimmick. 0 for non-spawner types.</summary>
        public int spawnCount;
    }
}
