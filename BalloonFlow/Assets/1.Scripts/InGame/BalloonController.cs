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
        private const int PinataRequiredHits = 2;
        private const float DEFAULT_BALLOON_SCALE = 0.5f;

        /// <summary>
        /// Color palette for balloon visualization. Index matches BalloonData.color.
        /// </summary>
        public static readonly Color[] BalloonColors = new Color[]
        {
            new Color(0.95f, 0.25f, 0.25f),  //  0: Red
            new Color(0.25f, 0.55f, 0.95f),  //  1: Blue
            new Color(0.25f, 0.85f, 0.35f),  //  2: Green
            new Color(0.95f, 0.85f, 0.15f),  //  3: Yellow
            new Color(0.80f, 0.30f, 0.90f),  //  4: Purple
            new Color(0.95f, 0.55f, 0.15f),  //  5: Orange
            new Color(0.40f, 0.90f, 0.90f),  //  6: Cyan
            new Color(0.95f, 0.50f, 0.70f),  //  7: Pink
            new Color(0.75f, 0.15f, 0.15f),  //  8: Crimson
            new Color(0.15f, 0.20f, 0.65f),  //  9: Navy
            new Color(0.55f, 0.95f, 0.25f),  // 10: Lime
            new Color(0.95f, 0.75f, 0.05f),  // 11: Gold
            new Color(0.55f, 0.20f, 0.80f),  // 12: Violet
            new Color(0.95f, 0.65f, 0.00f),  // 13: Amber
            new Color(0.15f, 0.70f, 0.65f),  // 14: Teal
            new Color(0.95f, 0.35f, 0.50f),  // 15: Rose
            new Color(0.95f, 0.45f, 0.35f),  // 16: Coral
            new Color(0.30f, 0.15f, 0.70f),  // 17: Indigo
            new Color(0.40f, 0.95f, 0.65f),  // 18: Mint
            new Color(0.95f, 0.75f, 0.60f),  // 19: Peach
            new Color(0.90f, 0.15f, 0.65f),  // 20: Magenta
            new Color(0.50f, 0.55f, 0.15f),  // 21: Olive
            new Color(0.45f, 0.75f, 0.95f),  // 22: Sky
            new Color(0.95f, 0.55f, 0.45f),  // 23: Salmon
            new Color(0.50f, 0.10f, 0.15f),  // 24: Maroon
            new Color(0.10f, 0.45f, 0.20f),  // 25: Forest
            new Color(0.70f, 0.55f, 0.90f),  // 26: Lavender
            new Color(0.82f, 0.70f, 0.50f),  // 27: Tan
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

        #endregion

        #region Fields

        [SerializeField] private GameObject _balloonPrefab;

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

        private void OnEnable()
        {
            EventBus.Subscribe<OnLevelLoaded>(HandleLevelLoaded);
        }

        private void OnDisable()
        {
            EventBus.Unsubscribe<OnLevelLoaded>(HandleLevelLoaded);
        }

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
            BuildPositionIndex();

            // Apply gimmick visual states after all balloons are placed
            ApplyInitialHiddenState();
            ApplyInitialIceState();
            ApplyInitialFrozenDartState();

            Debug.Log($"[BalloonController] Board setup complete. {RemainingCount} balloons placed.");
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

        /// <summary>
        /// Returns all non-popped balloons matching the specified color.
        /// Hidden balloons whose color is concealed are excluded until revealed.
        /// </summary>
        public BalloonData[] GetBalloonsByColor(int color)
        {
            List<BalloonData> result = new List<BalloonData>();
            foreach (KeyValuePair<int, BalloonData> pair in _balloons)
            {
                BalloonData data = pair.Value;
                if (data.isPopped) continue;
                if (_hiddenBalloons.Contains(data.balloonId)) continue;
                // Non-targetable gimmicks: darts cannot hit these
                if (data.gimmickType == GimmickWall) continue;
                if (data.gimmickType == GimmickPin) continue;
                if (data.gimmickType == GimmickIce) continue;
                if (data.color == color)
                {
                    result.Add(data);
                }
            }
            return result.ToArray();
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
                    ObjectPoolManager.Instance.Return(PoolKey, pair.Value);
                }
            }

            _balloons.Clear();
            _balloonObjects.Clear();
            _hiddenBalloons.Clear();
            _pinataGroup.Clear();
            _positionIndex.Clear();
            RemainingCount = 0;
            _nextBalloonId = 1;
        }

        #endregion

        #region Private Methods — Setup

        private void SpawnBalloonFromSetup(BalloonSetupData entry)
        {
            int id = _nextBalloonId++;

            BalloonData data = new BalloonData
            {
                balloonId   = id,
                color       = entry.color,
                position    = entry.position,
                isPopped    = false,
                gimmickType = string.IsNullOrEmpty(entry.gimmickType) ? GimmickNone : entry.gimmickType,
                hitCount    = 0
            };

            _balloons[id] = data;

            // Get visual object from pool (with color visualization)
            GameObject obj = GetOrCreateBalloonObject(id, entry.position, entry.color);
            if (obj != null)
            {
                _balloonObjects[id] = obj;

                // Override visuals for special gimmick types
                if (data.gimmickType == GimmickWall)
                    ApplyTintToObject(obj, WALL_COLOR);
                else if (data.gimmickType == GimmickPin)
                    ApplyTintToObject(obj, PIN_COLOR);
                else if (data.gimmickType == GimmickPinata || data.gimmickType == GimmickPinataBox)
                    ApplyTintToObject(obj, PINATA_COLOR);
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

            // Surprise balloons start concealed (Lv.101+, 필드 기믹)
            // Hidden(Lv.11)은 큐 기믹 → HolderManager에서 처리
            if (data.gimmickType == GimmickSurprise)
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

            GameObject obj = ObjectPoolManager.Instance.Get(PoolKey);
            if (obj == null)
            {
                Debug.LogWarning($"[BalloonController] Pool returned null for key '{PoolKey}'. Pool may not be pre-configured.");
                return null;
            }

            obj.transform.position = position;
            obj.transform.localScale = Vector3.one * _balloonScale;
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
        private static readonly Color PINATA_COLOR = new Color(0.95f, 0.70f, 0.20f);   // Gold pinata

        private void ApplyInitialHiddenState()
        {
            foreach (int id in _hiddenBalloons)
            {
                if (_balloonObjects.TryGetValue(id, out GameObject obj) && obj != null)
                {
                    // Show as grey balloon (color concealed, but visible on board)
                    ApplyTintToObject(obj, HIDDEN_COLOR);
                }
            }
        }

        /// <summary>
        /// Applies the Ice visual tint (frozen blue) to Ice gimmick balloons.
        /// Called during setup for any balloon with GimmickIce type.
        /// </summary>
        private void ApplyInitialIceState()
        {
            foreach (BalloonData d in _balloons.Values)
            {
                if (d.isPopped || d.gimmickType != GimmickIce) continue;
                if (_balloonObjects.TryGetValue(d.balloonId, out GameObject obj) && obj != null)
                {
                    ApplyTintToObject(obj, ICE_COLOR);
                }
            }
        }

        /// <summary>
        /// Applies Frozen Dart visual tint. Darker blue than Ice to distinguish.
        /// Called during setup for balloons with GimmickFrozenDart type.
        /// </summary>
        private void ApplyInitialFrozenDartState()
        {
            foreach (BalloonData d in _balloons.Values)
            {
                if (d.isPopped || d.gimmickType != GimmickFrozenDart) continue;
                if (_balloonObjects.TryGetValue(d.balloonId, out GameObject obj) && obj != null)
                {
                    ApplyTintToObject(obj, FROZEN_DART_COLOR);
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
                case 1: // 진한 톤 (채도+, 명도-)
                    Color.RGBToHSV(baseColor, out float h1, out float s1, out float v1);
                    return Color.HSVToRGB(h1, Mathf.Min(s1 + 0.1f, 1f), Mathf.Max(v1 - 0.08f, 0.2f));
                case 2: // 연한 톤 (채도-, 명도+)
                    Color.RGBToHSV(baseColor, out float h2, out float s2, out float v2);
                    return Color.HSVToRGB(h2, Mathf.Max(s2 - 0.12f, 0.1f), Mathf.Min(v2 + 0.1f, 1f));
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

        /// <summary>프리팹 고유 컴포넌트(Shadow, Particle 등) 건드리지 않고 색상만 적용.
        /// tag "BalloonMesh"가 있는 Renderer만 변경. 없으면 루트 Renderer만.</summary>
        private static void ApplyTintToObject(GameObject obj, Color color)
        {
            Material shared = GetOrCreateSharedMaterial(color);

            // 루트 오브젝트의 Renderer만 적용 (자식의 Shadow/Particle/TMP 보호)
            Renderer r = obj.GetComponent<Renderer>();
            if (r != null)
            {
                r.enabled = true;
                r.sharedMaterial = shared;
                return;
            }

            // 루트에 Renderer 없으면 첫 번째 자식 MeshRenderer 찾기 (FBX 구조 대응)
            MeshRenderer mr = obj.GetComponentInChildren<MeshRenderer>();
            if (mr != null)
            {
                mr.enabled = true;
                mr.sharedMaterial = shared;
            }
        }

        #endregion

        #region Private Methods — Pop Logic

        private PopResult ExecutePop(BalloonData data)
        {
            // Mark popped in data
            data.isPopped = true;
            _balloons[data.balloonId] = data;
            RemainingCount = Mathf.Max(0, RemainingCount - 1);

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

            if (data.hitCount < PinataRequiredHits)
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

                case GimmickNone:
                default:
                    break;
            }

            // All pops remove adjacent Pins (Pins can't be targeted directly)
            RemoveAdjacentPins(data.position);

            // All pops thaw adjacent Frozen Dart balloons (like Ice adjacency)
            ThawAdjacentFrozenDarts(data.position);
        }

        private void RevealAdjacentHiddenBalloons(Vector3 position)
        {
            List<int> adjacentIds = GetAdjacentBalloonIds(position);
            foreach (int id in adjacentIds)
            {
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

        private void ChainPopAdjacentSameColor(BalloonData source)
        {
            _chainPopQueue.Clear();

            // 시작 풍선의 인접 같은 색 추가
            foreach (int id in GetAdjacentBalloonIds(source.position))
                _chainPopQueue.Enqueue(id);

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

                // 팝된 풍선의 인접 같은 색도 큐에 추가
                foreach (int adjId in GetAdjacentBalloonIds(neighbor.position))
                    if (!_chainPopQueue.Contains(adjId))
                        _chainPopQueue.Enqueue(adjId);
            }
        }

        /// <summary>
        /// Destroys all adjacent Pin balloons. Pins cannot be targeted by darts —
        /// they are only removed when a neighboring balloon is popped.
        /// </summary>
        private void RemoveAdjacentPins(Vector3 position)
        {
            List<int> adjacentIds = GetAdjacentBalloonIds(position);
            foreach (int id in adjacentIds)
            {
                if (!_balloons.TryGetValue(id, out BalloonData neighbor)) continue;
                if (neighbor.isPopped) continue;
                if (neighbor.gimmickType != GimmickPin) continue;

                // Force-pop the Pin (bypasses PopBalloon's Pin guard)
                ExecutePop(neighbor);
            }
        }

        /// <summary>
        /// Thaws adjacent Ice balloons. Ice balloons are frozen and cannot be targeted.
        /// When an adjacent balloon pops, Ice converts to a normal balloon (targetable).
        /// </summary>
        private void ThawAdjacentIce(Vector3 position)
        {
            List<int> adjacentIds = GetAdjacentBalloonIds(position);
            foreach (int id in adjacentIds)
            {
                if (!_balloons.TryGetValue(id, out BalloonData neighbor)) continue;
                if (neighbor.isPopped) continue;
                if (neighbor.gimmickType != GimmickIce) continue;

                // Thaw: convert Ice to normal balloon (now targetable by darts)
                neighbor.gimmickType = GimmickNone;
                _balloons[id] = neighbor;

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
            List<int> adjacentIds = GetAdjacentBalloonIds(position);
            foreach (int id in adjacentIds)
            {
                if (!_balloons.TryGetValue(id, out BalloonData neighbor)) continue;
                if (neighbor.isPopped) continue;
                if (neighbor.gimmickType != GimmickFrozenDart) continue;

                // Thaw: convert to normal balloon (now poppable in 1 hit)
                neighbor.gimmickType = GimmickNone;
                neighbor.hitCount = 1; // Mark as already thawed so next hit pops
                _balloons[id] = neighbor;

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

            // Animate: bounce up slightly, then shrink to zero
            float savedScale = _balloonScale;
            Sequence seq = DOTween.Sequence();
            seq.Append(obj.transform.DOMove(obj.transform.position + Vector3.up * 0.4f, 0.12f).SetEase(Ease.OutQuad));
            seq.Join(obj.transform.DOScale(Vector3.one * savedScale * 1.2f, 0.12f).SetEase(Ease.OutQuad));
            seq.Append(obj.transform.DOScale(Vector3.zero, 0.15f).SetEase(Ease.InBack));
            seq.OnComplete(() =>
            {
                if (obj != null && ObjectPoolManager.HasInstance)
                {
                    obj.transform.localScale = Vector3.one * savedScale;
                    ObjectPoolManager.Instance.Return(PoolKey, obj);
                }
            });
        }

        #endregion

        #region Private Methods — Spatial Helpers

        private List<int> GetAdjacentBalloonIds(Vector3 position)
        {
            List<int> neighbors = new List<int>();
            Vector3Int center = ToGridKey(position);

            // 4-directional adjacency (grid-based layout, XZ plane)
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
                if (_positionIndex.TryGetValue(neighbor, out int neighborId))
                {
                    neighbors.Add(neighborId);
                }
            }

            return neighbors;
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

        /// <summary>Hit counter for Pinata gimmick (requires 2 hits).</summary>
        public int hitCount;
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
