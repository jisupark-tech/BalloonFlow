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
        private const string IronBoxPoolKey    = "IronBox";      // Pinata_Box gimmick visual (Lv.161)
        private const string WoodenBoardPoolKey = "WoodenBoard"; // Pin gimmick visual (Lv.61)
        private const string FrozenLayerPoolKey = "FrozenLayer"; // Ice / Frozen_Dart overlay child
        private const int PinataRequiredHits = 2;
        private const float DEFAULT_BALLOON_SCALE = 0.5f;

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

        #endregion

        #region Fields

        [SerializeField] private GameObject _balloonPrefab;

        [Tooltip("Hidden 기믹 풍선 전용 머테리얼 (BalloonHidden.mat). Inspector 할당 또는 Resources/BalloonHidden 에서 자동 로드.")]
        [SerializeField] private Material _balloonHiddenMaterial;

        // Primary data store keyed by balloonId
        private readonly Dictionary<int, BalloonData> _balloons = new Dictionary<int, BalloonData>();

        // Visual GameObject handles keyed by balloonId
        private readonly Dictionary<int, GameObject> _balloonObjects = new Dictionary<int, GameObject>();

        // Hidden balloons that are currently color-concealed
        private readonly HashSet<int> _hiddenBalloons = new HashSet<int>();

        // Pinata multi-tile occupancy: key = balloonId, value = list of all occupied ids
        private readonly Dictionary<int, List<int>> _pinataGroup = new Dictionary<int, List<int>>();

        // Spatial index: position key -> balloonId  (for adjacency lookups)
        private readonly Dictionary<Vector3Int, int> _positionIndex = new Dictionary<Vector3Int, int>();

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
            float cx = GameManager.Instance.Board.boardCenterX;
            float cz = GameManager.Instance.Board.balloonCenterZ;
            float wm = _levelSafeWm;
            float hm = _levelSafeHm;
            float zo = GameManager.Instance.Board.balloonGridZOffset;
            float scaleMult = Mathf.Max(wm, hm);

            foreach (var kvp in _balloons)
            {
                if (kvp.Value.isPopped) continue;
                if (!_balloonObjects.TryGetValue(kvp.Key, out GameObject obj) || obj == null) continue;

                Vector3 origPos = kvp.Value.position;
                obj.transform.position = new Vector3(
                    cx + (origPos.x - cx) * wm,
                    origPos.y,
                    cz + (origPos.z - cz) * hm + zo + _levelSafeZShift);
                obj.transform.localScale = Vector3.one * _balloonScale * scaleMult;
            }
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

            if (layout == null || layout.Count == 0)
            {
                Debug.LogWarning("[BalloonController] SetupBalloons called with empty or null layout.");
                return;
            }

            foreach (BalloonSetupData entry in layout)
            {
                SpawnBalloonFromSetup(entry);
            }

            // Wall balloons are indestructible — don't count toward clear condition
            int excludeCount = 0;
            foreach (BalloonData d in _balloons.Values)
            {
                if (d.gimmickType == GimmickWall) excludeCount++;
            }
            RemainingCount = _balloons.Count - excludeCount;
            PoppedCount = 0;
            BuildPositionIndex();

            // Apply gimmick visual states after all balloons are placed
            ApplyInitialHiddenState();
            ApplyInitialIceState();
            ApplyInitialFrozenDartState();
            ApplyInitialColorCurtainState();

            // 레벨별 안전 배율 계산 (벨트 초과 레벨만 축소)
            CalculateLevelSafeMult();
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

        /// <summary>풍선의 실제 월드 위치 (배율/오프셋 적용 후). 오브젝트 없으면 데이터 위치 반환.</summary>
        public Vector3 GetBalloonWorldPosition(int balloonId)
        {
            if (_balloonObjects.TryGetValue(balloonId, out GameObject obj) && obj != null)
                return obj.transform.position;
            if (_balloons.TryGetValue(balloonId, out BalloonData data))
                return data.position;
            return Vector3.zero;
        }

        /// <summary>풍선 오브젝트의 현재 localScale. 오브젝트 없으면 추정 스케일 반환.</summary>
        public Vector3 GetBalloonWorldScale(int balloonId)
        {
            if (_balloonObjects.TryGetValue(balloonId, out GameObject obj) && obj != null)
                return obj.transform.localScale;
            float scaleMult = _levelSafeCalculated
                ? Mathf.Max(_levelSafeWm, _levelSafeHm)
                : (GameManager.HasInstance ? Mathf.Max(GameManager.Instance.Board.balloonFieldWidthMult, GameManager.Instance.Board.balloonFieldHeightMult) : 1f);
            return Vector3.one * _balloonScale * scaleMult;
        }

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
                if (!destroyed)
                    return new PopResult { success = false, reason = "Pin: segment removed, not fully destroyed", balloonId = data.balloonId, gimmickType = GimmickPin };
                return ExecutePop(data);
            }

            // Frozen Dart: 2-hit field gimmick
            if (data.gimmickType == GimmickFrozenDart)
                return ProcessFrozenDartHit(data);

            // Barricade: destructible wall with HP
            if (data.gimmickType == GimmickBarricade)
                return ProcessBarricadeHit(data);

            // Pinata/PinataBox
            if (data.gimmickType == GimmickPinata || data.gimmickType == GimmickPinataBox)
                return ProcessPinataHit(data);

            return ExecutePop(data);
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
            ExecutePop(data);
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
                obj.transform.DOPunchScale(Vector3.one * 0.15f, 0.2f, 8, 0.5f);
            }

            EventBus.Publish(new OnGimmickTriggered
            {
                gimmickType = GimmickSurprise, // 필드 풍선 색상 공개 = Surprise 기믹
                targetId    = balloonId
            });

            return true;
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
            foreach (KeyValuePair<int, GameObject> pair in _balloonObjects)
            {
                if (pair.Value != null && ObjectPoolManager.HasInstance)
                {
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

            // FrozenLayer 오버레이 자식들도 모두 풀로 반환
            ReturnAllFrozenOverlays();

            _balloons.Clear();
            _balloonObjects.Clear();
            _hiddenBalloons.Clear();
            _pinataGroup.Clear();
            _positionIndex.Clear();
            _activeKeyPairIds.Clear();
            RemainingCount = 0;
            PoppedCount = 0;
            _nextBalloonId = 1;
        }

        #endregion

        #region Private Methods — Setup

        private void SpawnBalloonFromSetup(BalloonSetupData entry)
        {
            int id = _nextBalloonId++;

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
                lockPairId  = entry.lockPairId
            };

            _balloons[id] = data;

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
                        // WoodenBoard 프리팹 사용 — GimmickIdentifier 있으면 색상 적용 (Wall은 HP 미사용)
                        ApplyTintToObject(obj, WALL_COLOR);
                        var gi = obj.GetComponent<GimmickIdentifier>();
                        if (gi != null)
                        {
                            gi.Initialize();
                            if (gi.HasColorRenderers)
                                gi.ApplyColor(WALL_COLOR);
                        }
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
                            if (gi.HasColorRenderers)
                                gi.ApplyColor(BalloonColors[ci]);
                        }
                    }
                    else if (data.gimmickType == GimmickBarricade)
                    {
                        // Baricade 프리팹 사용 — HP 기반 파괴 가능 벽.
                        // 명세: Pinata 처럼 sizeW/sizeH 만큼 길이 stretch (여러 개 뭉친 게 아닌 한 덩어리).
                        var gi = obj.GetComponent<GimmickIdentifier>();
                        if (gi != null)
                        {
                            gi.Initialize();
                            int hp = data.maxHP - data.hitCount;
                            gi.UpdateHP(Mathf.Max(1, hp));
                            if (gi.HasColorRenderers)
                                gi.ApplyColor(WALL_COLOR);
                        }

                        // 멀티셀 stretching — Pinata 와 동일 방식
                        {
                            float cs = _cellSpacing > 0 ? _cellSpacing : 0.3f;
                            float scaleBase = _balloonScale;

                            if (data.sizeW > 1 || data.sizeH > 1)
                            {
                                obj.transform.localScale = new Vector3(
                                    scaleBase * data.sizeW,
                                    scaleBase,
                                    scaleBase * data.sizeH);
                            }

                            // 앵커(좌하단)에서 멀티셀 중심으로 이동
                            Vector3 centerOffset = new Vector3(
                                (data.sizeW - 1) * cs * 0.5f,
                                0f,
                                (data.sizeH - 1) * cs * 0.5f);
                            obj.transform.position = new Vector3(
                                data.position.x + centerOffset.x,
                                0f,
                                data.position.z + centerOffset.z);
                        }
                    }
                    else if (data.gimmickType == GimmickPinata || data.gimmickType == GimmickPinataBox)
                    {
                        // PinataBox는 IronBox 프리팹, 일반 Pinata는 Balloon 풀 폴백
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

                        // Piñata 위치/스케일 — 프리팹 세팅 기준, 추가 보정 없음
                        {
                            float cs = _cellSpacing > 0 ? _cellSpacing : 0.3f;
                            float scaleBase = _balloonScale;

                            if (data.sizeW > 1 || data.sizeH > 1)
                            {
                                obj.transform.localScale = new Vector3(
                                    scaleBase * data.sizeW,
                                    scaleBase,
                                    scaleBase * data.sizeH);
                            }

                            // 앵커(좌하단)에서 멀티셀 중심으로 이동
                            Vector3 centerOffset = new Vector3(
                                (data.sizeW - 1) * cs * 0.5f,
                                0f,
                                (data.sizeH - 1) * cs * 0.5f);
                            // Y는 프리팹 기준 (0으로 — 프리팹에서 이미 높이 설정됨)
                            obj.transform.position = new Vector3(
                                data.position.x + centerOffset.x,
                                0f,
                                data.position.z + centerOffset.z);
                        }
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
            obj.transform.position = adjustedPos;
            float scaleMult = _levelSafeCalculated
                ? Mathf.Max(_levelSafeWm, _levelSafeHm)
                : (GameManager.HasInstance ? Mathf.Max(GameManager.Instance.Board.balloonFieldWidthMult, GameManager.Instance.Board.balloonFieldHeightMult) : 1f);
            obj.transform.localScale = Vector3.one * _balloonScale * scaleMult;
            obj.SetActive(true);


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
            foreach (BalloonData d in _balloons.Values)
            {
                Vector3Int key = ToGridKey(d.position);
                _positionIndex[key] = d.balloonId;
            }
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
            foreach (BalloonData d in _balloons.Values)
            {
                if (d.isPopped || d.gimmickType != GimmickIce) continue;
                if (_balloonObjects.TryGetValue(d.balloonId, out GameObject obj) && obj != null)
                {
                    ApplyTintToObject(obj, ICE_COLOR);
                    AttachFrozenOverlay(d.balloonId, obj);
                }
            }
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
            if (string.IsNullOrEmpty(gimmickType)) return PoolKey;
            switch (gimmickType)
            {
                case GimmickBarricade: return BarricadePoolKey;
                case GimmickPinataBox: return IronBoxPoolKey;
                case GimmickPin:       return WoodenBoardPoolKey;
                case GimmickWall:      return WoodenBoardPoolKey;
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
            // Mark popped in data
            data.isPopped = true;
            _balloons[data.balloonId] = data;
            RemainingCount = Mathf.Max(0, RemainingCount - 1);
            PoppedCount++;

            // Return visual to pool
            ReturnBalloonObject(data.balloonId);

            // Remove from position index
            _positionIndex.Remove(ToGridKey(data.position));

            // Publish pop event
            EventBus.Publish(new OnBalloonPopped
            {
                balloonId = data.balloonId,
                color     = data.color,
                position  = data.position
            });

            // Trigger gimmick side-effects (base behavior only)
            PopResult result = new PopResult
            {
                success    = true,
                balloonId  = data.balloonId,
                color      = data.color,
                position   = data.position,
                gimmickType = data.gimmickType
            };

            ProcessGimmickAfterPop(data, result);
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
                // Partial hit — not yet destroyed
                EventBus.Publish(new OnGimmickTriggered
                {
                    gimmickType = GimmickPinata,
                    targetId    = data.balloonId
                });

                return new PopResult
                {
                    success     = false,
                    reason      = "PinataPartialHit",
                    balloonId   = data.balloonId,
                    gimmickType = GimmickPinata
                };
            }

            // Final hit — execute full pop
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
                if (gi != null)
                {
                    int remainHP = Mathf.Max(0, requiredHits - data.hitCount);
                    gi.UpdateHP(remainHP);
                    gi.PlayHitEffect();
                    if (remainHP <= 0) gi.PlayEndEffect();
                }
            }

            if (data.hitCount < requiredHits)
            {
                return new PopResult
                {
                    success     = false,
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

                EventBus.Publish(new OnGimmickTriggered
                {
                    gimmickType = GimmickFrozenDart,
                    targetId    = data.balloonId
                });

                return new PopResult
                {
                    success     = false,
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
                    ChainPopAdjacentSameColor(data);
                    break;

                case GimmickPinataBox:
                    EventBus.Publish(new OnGimmickTriggered { gimmickType = GimmickPinataBox, targetId = data.balloonId });
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

            // All pops thaw adjacent Frozen Dart balloons (like Ice adjacency)
            ThawAdjacentFrozenDarts(data.position);
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
                    obj.transform.DOPunchScale(Vector3.one * 0.15f, 0.2f, 8, 0.5f);
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
        /// Thaws adjacent Ice balloons. Ice balloons are frozen and cannot be targeted.
        /// When an adjacent balloon pops, Ice converts to a normal balloon (targetable).
        /// </summary>
        private void ThawAdjacentIce(Vector3 position)
        {
            int count = CopyAdjacentIds(GetAdjacentBalloonIds(position));
            for (int i = 0; i < count; i++)
            {
                int id = _adjCopyBuffer[i];
                if (!_balloons.TryGetValue(id, out BalloonData neighbor)) continue;
                if (neighbor.isPopped) continue;
                if (neighbor.gimmickType != GimmickIce) continue;

                // Thaw: convert Ice to normal balloon (now targetable by darts)
                neighbor.gimmickType = GimmickNone;
                _balloons[id] = neighbor;

                // 얼음 쉘 오버레이 제거
                ReturnFrozenOverlay(id);

                // Visual: restore color (Ice was shown as frozen/blue tint)
                if (_balloonObjects.TryGetValue(id, out GameObject obj) && obj != null)
                {
                    int colorIdx = Mathf.Clamp(neighbor.color, 0, BalloonColors.Length - 1);
                    ApplyTintToObject(obj, BalloonColors[colorIdx]);
                }

                EventBus.Publish(new OnGimmickTriggered
                {
                    gimmickType = GimmickIce,
                    targetId    = id
                });
            }
        }

        /// <summary>
        /// Thaws adjacent Frozen Dart balloons. Unlike Ice (which becomes targetable),
        /// Frozen Dart thaw converts it to a normal balloon that can be popped in 1 hit.
        /// </summary>
        private void ThawAdjacentFrozenDarts(Vector3 position)
        {
            int count = CopyAdjacentIds(GetAdjacentBalloonIds(position));
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
                    obj.transform.DOScale(Vector3.one * _balloonScale, 0.25f).SetEase(Ease.OutBack);
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

        private void ReturnBalloonObject(int balloonId)
        {
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
            ReturnFrozenOverlay(balloonId);

            // 풍선 스케일업 → 완료 후 PopEffectPool 재생 + 풍선 풀 반환.
            // 이전: BalloonIdentifier에 _popEffect 자식 부착 → detach/reattach + 풍선마다 별도 인스턴스 → 부하.
            // 이후: 단일 CircleParticle 풀에서 가져와 색상 적용 + play, 끝나면 풀 반환.
            float scaleUpDuration = GameManager.Instance.Board.popScaleDuration;
            float scaleUpMult = GameManager.Instance.Board.popScaleMultiplier;
            Vector3 popPos = obj.transform.position;
            Sequence seq = DOTween.Sequence();
            seq.Append(obj.transform.DOScale(Vector3.one * savedScale * scaleUpMult, scaleUpDuration).SetEase(Ease.OutQuad));
            seq.AppendCallback(() =>
            {
                // 스케일업 완료 시점에 애니메이터 Pop 트리거 (파티클은 PopEffectPool 가 처리)
                if (identifier != null)
                    identifier.MarkPopped();

                int ci = Mathf.Clamp(popColorIdx, 0, BalloonColors.Length - 1);
                PopEffectPool.Play(popPos, BalloonColors[ci], this);

                if (obj != null && ObjectPoolManager.HasInstance)
                {
                    obj.transform.localScale = Vector3.one * savedScale;
                    ObjectPoolManager.Instance.Return(returnKey, obj);
                }
            });
        }

        /// <summary>Key 프리팹이 포물선으로 Lock 보관함까지 비행 → 도착 시 잠금 해제.</summary>
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

            // Mark as popped so it's removed from position tracking
            keyData.isPopped = true;
            _balloons[keyId] = keyData;
            RemainingCount = Mathf.Max(0, RemainingCount - 1);
            PoppedCount++;
            _positionIndex.Remove(ToGridKey(keyData.position));

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
                position = keyData.position
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
                    _reusableAdjacentIds.Add(neighborId);
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
    }

    /// <summary>
    /// Result returned from BalloonController.PopBalloon().
    /// </summary>
    public class PopResult
    {
        public bool success;
        public string reason;
        public int balloonId;
        public int color;
        public Vector3 position;
        public string gimmickType;

        /// <summary>Number of new balloons spawned by Spawner gimmick. 0 for non-spawner types.</summary>
        public int spawnCount;
    }
}
