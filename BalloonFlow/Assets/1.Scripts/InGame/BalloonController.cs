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
        private const int BigObjectRequiredHits = 2;
        private const float DEFAULT_BALLOON_SCALE = 0.5f;

        /// <summary>
        /// Color palette for balloon visualization. Index matches BalloonData.color.
        /// </summary>
        public static readonly Color[] BalloonColors = new Color[]
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

        // Gimmick type string constants — avoid magic strings throughout
        public const string GimmickNone      = "none";
        public const string GimmickHidden    = "Hidden";
        public const string GimmickSpawnerT  = "Spawner_T";
        public const string GimmickSpawnerO  = "Spawner_O";
        public const string GimmickBigObject = "BigObject";
        public const string GimmickChain     = "Chain";

        #endregion

        #region Fields

        [SerializeField] private GameObject _balloonPrefab;

        // Primary data store keyed by balloonId
        private readonly Dictionary<int, BalloonData> _balloons = new Dictionary<int, BalloonData>();

        // Visual GameObject handles keyed by balloonId
        private readonly Dictionary<int, GameObject> _balloonObjects = new Dictionary<int, GameObject>();

        // Hidden balloons that are currently color-concealed
        private readonly HashSet<int> _hiddenBalloons = new HashSet<int>();

        // BigObject multi-tile occupancy: key = balloonId, value = list of all occupied ids
        private readonly Dictionary<int, List<int>> _bigObjectGroup = new Dictionary<int, List<int>>();

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

            RemainingCount = _balloons.Count;
            BuildPositionIndex();

            // Apply Hidden gimmick concealment after all balloons are placed
            ApplyInitialHiddenState();

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
                if (data.color == color)
                {
                    result.Add(data);
                }
            }
            return result.ToArray();
        }

        /// <summary>
        /// Returns all balloon data entries (including popped), for board state inspection.
        /// </summary>
        public BalloonData[] GetAllBalloons()
        {
            BalloonData[] all = new BalloonData[_balloons.Count];
            int i = 0;
            foreach (BalloonData d in _balloons.Values)
            {
                all[i++] = d;
            }
            return all;
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

            // BigObject requires multiple hits
            if (data.gimmickType == GimmickBigObject)
            {
                return ProcessBigObjectHit(data);
            }

            // Standard pop (covers none, Hidden, Chain, Spawner_T, Spawner_O)
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
            _bigObjectGroup.Clear();
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
            }

            // Register BigObject group membership
            if (data.gimmickType == GimmickBigObject && entry.groupId >= 0)
            {
                if (!_bigObjectGroup.TryGetValue(entry.groupId, out List<int> group))
                {
                    group = new List<int>();
                    _bigObjectGroup[entry.groupId] = group;
                }
                group.Add(id);
            }

            // Hidden balloons start concealed
            if (data.gimmickType == GimmickHidden)
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

            // Apply balloon color to Renderer
            Renderer rend = obj.GetComponentInChildren<Renderer>();
            if (rend != null)
            {
                int colorIdx = Mathf.Clamp(color, 0, BalloonColors.Length - 1);
                rend.material.color = BalloonColors[colorIdx];
            }

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

        private void ApplyInitialHiddenState()
        {
            foreach (int id in _hiddenBalloons)
            {
                if (_balloonObjects.TryGetValue(id, out GameObject obj) && obj != null)
                {
                    // Visual concealment — GimmickManager will handle reveal VFX in Phase 2
                    // Base behavior: disable renderer to hide color
                    Renderer rend = obj.GetComponentInChildren<Renderer>();
                    if (rend != null)
                    {
                        rend.enabled = false;
                    }
                }
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

        private PopResult ProcessBigObjectHit(BalloonData data)
        {
            data.hitCount++;
            _balloons[data.balloonId] = data;

            if (data.hitCount < BigObjectRequiredHits)
            {
                // Partial hit — not yet destroyed
                EventBus.Publish(new OnGimmickTriggered
                {
                    gimmickType = GimmickBigObject,
                    targetId    = data.balloonId
                });

                return new PopResult
                {
                    success     = false,
                    reason      = "BigObjectPartialHit",
                    balloonId   = data.balloonId,
                    gimmickType = GimmickBigObject
                };
            }

            // Final hit — execute full pop
            return ExecutePop(data);
        }

        private void ProcessGimmickAfterPop(BalloonData data, PopResult result)
        {
            switch (data.gimmickType)
            {
                case GimmickHidden:
                    // Reveal adjacent Hidden balloons
                    RevealAdjacentHiddenBalloons(data.position);
                    break;

                case GimmickSpawnerT:
                    // Base: signal spawner to produce 1 balloon — GimmickManager handles actual spawn
                    result.spawnCount = 1;
                    EventBus.Publish(new OnGimmickTriggered
                    {
                        gimmickType = GimmickSpawnerT,
                        targetId    = data.balloonId
                    });
                    break;

                case GimmickSpawnerO:
                    // Base: signal spawner to produce 2 balloons
                    result.spawnCount = 2;
                    EventBus.Publish(new OnGimmickTriggered
                    {
                        gimmickType = GimmickSpawnerO,
                        targetId    = data.balloonId
                    });
                    break;

                case GimmickChain:
                    // Auto-pop adjacent same-color balloons
                    ChainPopAdjacentSameColor(data);
                    EventBus.Publish(new OnGimmickTriggered
                    {
                        gimmickType = GimmickChain,
                        targetId    = data.balloonId
                    });
                    break;

                case GimmickNone:
                case GimmickBigObject:
                default:
                    break;
            }
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

                // Re-enable renderer to reveal color
                if (_balloonObjects.TryGetValue(id, out GameObject obj) && obj != null)
                {
                    Renderer rend = obj.GetComponentInChildren<Renderer>();
                    if (rend != null) rend.enabled = true;
                }

                EventBus.Publish(new OnGimmickTriggered
                {
                    gimmickType = GimmickHidden,
                    targetId    = id
                });
            }
        }

        private void ChainPopAdjacentSameColor(BalloonData source)
        {
            List<int> adjacentIds = GetAdjacentBalloonIds(source.position);
            foreach (int id in adjacentIds)
            {
                if (!_balloons.TryGetValue(id, out BalloonData neighbor)) continue;
                if (neighbor.isPopped) continue;
                if (neighbor.color != source.color) continue;
                if (_hiddenBalloons.Contains(id)) continue; // Hidden not yet revealed

                // Recursive chain — ExecutePop will trigger further chains if that balloon is also Chain type
                ExecutePop(neighbor);
            }
        }

        private void ReturnBalloonObject(int balloonId)
        {
            if (!_balloonObjects.TryGetValue(balloonId, out GameObject obj)) return;
            if (obj == null) return;

            _balloonObjects.Remove(balloonId);

            // Animate scale-down before returning to pool
            obj.transform.DOScale(Vector3.zero, 0.15f)
                .SetEase(Ease.InBack)
                .OnComplete(() =>
                {
                    if (obj != null && ObjectPoolManager.HasInstance)
                    {
                        obj.transform.localScale = Vector3.one * _balloonScale; // Reset scale for reuse
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
        /// "none", "Hidden", "Spawner_T", "Spawner_O", "BigObject", "Chain".
        /// </summary>
        public string gimmickType;

        /// <summary>Hit counter for BigObject gimmick (requires 2 hits).</summary>
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
        /// Group id for BigObject multi-tile balloons.
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
