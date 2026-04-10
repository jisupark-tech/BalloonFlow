using UnityEngine;

namespace BalloonFlow
{
    /// <summary>
    /// Controls the full level lifecycle: load, play, complete, fail, and retry.
    /// Orchestrates LevelDataProvider, RailManager, and ScoreManager to set up
    /// each level, then listens for board-state events to trigger win/lose flows.
    /// Progress (highest completed level, best star counts) is persisted via PlayerPrefs.
    /// </summary>
    /// <remarks>
    /// Layer: Game | Genre: Puzzle | Role: Manager | Phase: 1
    /// DB Reference: No DB match — generated from L3 YAML logicFlow
    /// </remarks>
    public class LevelManager : Singleton<LevelManager>
    {
        #region Constants

        private const string PREFS_KEY_HIGHEST_LEVEL    = "BF_HighestLevel";
        private const string PREFS_KEY_STARS_PREFIX      = "BF_Stars_";
        private const int    FIRST_LEVEL_ID              = 1;
        private const int    LEVELS_PER_PACKAGE          = 20;

        #endregion

        #region Fields

        [SerializeField]
        private LevelDataProvider _levelDataProvider;

        private LevelConfig _currentLevelConfig;
        private int         _currentLevelId;
        private bool        _levelActive;
        private int         _retryCount;

        #endregion

        #region Properties

        /// <summary>
        /// The LevelConfig currently loaded. Null if no level has been loaded.
        /// </summary>
        public LevelConfig CurrentLevel => _currentLevelConfig;

        /// <summary>
        /// The integer ID of the currently loaded level (1-based).
        /// Returns 0 if no level is loaded.
        /// </summary>
        public int CurrentLevelId => _currentLevelId;

        /// <summary>
        /// True while a level is active (loaded and not yet completed or failed).
        /// </summary>
        public bool IsLevelActive => _levelActive;

        #endregion

        #region Lifecycle

        protected override void OnSingletonAwake()
        {
            _levelActive = false;
            _currentLevelId = 0;
        }

        private void OnEnable()
        {
            EventBus.Subscribe<OnBoardCleared>(HandleBoardCleared);
            EventBus.Subscribe<OnBoardFailed>(HandleBoardFailed);
        }

        private void OnDisable()
        {
            EventBus.Unsubscribe<OnBoardCleared>(HandleBoardCleared);
            EventBus.Unsubscribe<OnBoardFailed>(HandleBoardFailed);
        }

        #endregion

        #region Public Methods

        /// <summary>
        /// Loads a level by ID, sets up all subsystems, and publishes OnLevelLoaded.
        /// Does nothing if the level ID is invalid or LevelDataProvider is unassigned.
        /// </summary>
        public void LoadLevel(int levelId)
        {
            LevelConfig config = null;

            // Check for Level Editor test level (editor play test)
            #if UNITY_EDITOR
            if (UnityEditor.EditorPrefs.GetBool("BalloonFlow_UseTestLevel", false))
            {
                string json = UnityEditor.EditorPrefs.GetString("BalloonFlow_TestLevel", "");
                if (!string.IsNullOrEmpty(json))
                {
                    config = JsonUtility.FromJson<LevelConfig>(json);
                    UnityEditor.EditorPrefs.SetBool("BalloonFlow_UseTestLevel", false);
                    Debug.Log($"[LevelManager] Loaded test level from Level Editor. Balloons={config.balloonCount}, Holders={config.holders.Length}");
                }
            }
            #endif

            // Try LevelDataProvider first (pre-authored levels)
            if (config == null && ValidateProvider())
            {
                config = _levelDataProvider.GetLevelData(levelId);
            }

            // Fallback to LevelGenerator for procedurally generated levels
            if (config == null && LevelGenerator.HasInstance)
            {
                Debug.Log($"[LevelManager] No pre-authored config for level {levelId}. Falling back to LevelGenerator.");
                config = LevelGenerator.Instance.GenerateLevel(levelId);
            }

            if (config == null)
            {
                Debug.LogWarning($"[LevelManager] Cannot load level {levelId}: no config found from provider or generator.");
                return;
            }

            // 이전 레벨 오브젝트 정리 (같은 씬 내 레벨 전환 시)
            CleanupPreviousLevel();

            _currentLevelId     = levelId;
            _currentLevelConfig = config;
            _levelActive        = true;
            _retryCount         = 0;

            SetupLevel(config);
        }

        /// <summary>
        /// Reloads the current level from scratch, incrementing the retry counter.
        /// </summary>
        public void RetryLevel()
        {
            if (_currentLevelId <= 0)
            {
                Debug.LogWarning("[LevelManager] RetryLevel called with no level loaded.");
                return;
            }

            _retryCount++;
            _levelActive = true;
            SetupLevel(_currentLevelConfig);
        }

        /// <summary>
        /// Returns the LevelConfig of the currently loaded level.
        /// Returns null if no level is loaded.
        /// </summary>
        public LevelConfig GetCurrentLevel()
        {
            return _currentLevelConfig;
        }

        /// <summary>
        /// Returns the integer ID of the currently loaded level.
        /// Returns 0 if no level is loaded.
        /// </summary>
        public int GetCurrentLevelId()
        {
            return _currentLevelId;
        }

        /// <summary>
        /// Whether a level is currently active (not yet completed or failed).
        /// </summary>
        public bool IsLevelActiveState()
        {
            return _levelActive;
        }

        /// <summary>
        /// Explicitly marks the level as complete with the given score and star count.
        /// Normally called by the board-cleared event handler.
        /// </summary>
        public void CompleteLevel(int score, int stars)
        {
            // Clear always takes priority over fail.
            // Even if FailLevel was called (e.g. NoMovesLeft triggered while last dart
            // was in flight), a board clear is the definitive win condition.
            _levelActive = false;
            SaveLevelProgress(_currentLevelId, stars);

            Debug.Log($"[LevelManager] Publishing OnLevelCompleted: level={_currentLevelId}, score={score}, stars={stars}");

            EventBus.Publish(new OnLevelCompleted
            {
                levelId   = _currentLevelId,
                score     = score,
                starCount = stars
            });
        }

        /// <summary>
        /// Explicitly marks the level as failed.
        /// Normally called by the board-failed event handler.
        /// </summary>
        public void FailLevel()
        {
            if (!_levelActive)
            {
                Debug.LogWarning($"[LevelManager] FailLevel called but _levelActive=false. Level={_currentLevelId}");
                return;
            }

            Debug.Log($"[LevelManager] Publishing OnLevelFailed: level={_currentLevelId}");
            _levelActive = false;

            EventBus.Publish(new OnLevelFailed
            {
                levelId      = _currentLevelId,
                attemptCount = _retryCount + 1
            });
        }

        /// <summary>
        /// Returns the ID of the next level, clamped to the total level count.
        /// Returns the current level ID if no data provider is available.
        /// </summary>
        public int GetNextLevelId()
        {
            if (!ValidateProvider())
            {
                return _currentLevelId;
            }

            int maxLevel = _levelDataProvider.GetLevelCount();
            return Mathf.Clamp(_currentLevelId + 1, FIRST_LEVEL_ID, maxLevel);
        }

        /// <summary>
        /// Returns the difficulty of a specific level from the database.
        /// Returns Normal if level data is not available.
        /// </summary>
        public DifficultyPurpose GetLevelDifficulty(int levelId)
        {
            if (_levelDataProvider == null) return DifficultyPurpose.Normal;
            var config = _levelDataProvider.GetLevelData(levelId);
            return config != null ? config.difficultyPurpose : DifficultyPurpose.Normal;
        }

        /// <summary>
        /// Returns the best (highest) star count ever achieved on a level, from PlayerPrefs.
        /// Returns 0 if the level has never been completed.
        /// </summary>
        public int GetBestStars(int levelId)
        {
            return PlayerPrefs.GetInt(PREFS_KEY_STARS_PREFIX + levelId, 0);
        }

        /// <summary>
        /// Returns the highest level ID that has been completed at least once.
        /// Returns 0 if no level has been completed.
        /// </summary>
        public int GetHighestCompletedLevel()
        {
            return PlayerPrefs.GetInt(PREFS_KEY_HIGHEST_LEVEL, 0);
        }

        #endregion

        #region Private Methods

        /// <summary>
        /// 이전 레벨의 오브젝트를 정리. 같은 씬 내 레벨 전환 시 호출.
        /// </summary>
        private void CleanupPreviousLevel()
        {
            if (BalloonController.HasInstance)
                BalloonController.Instance.ClearAllBalloons();

            if (DartManager.HasInstance)
                DartManager.Instance.ClearAllDarts();

            if (HolderVisualManager.HasInstance)
                HolderVisualManager.Instance.ClearAllVisuals();

            if (RailManager.HasInstance)
                RailManager.Instance.ResetAll();

            if (PopupManager.HasInstance)
                PopupManager.Instance.CloseAllPopups();
        }

        /// <summary>
        /// Coordinates subsystem setup for the given level config.
        /// Publishes setup events for balloons, holders, and rail, then resets the score.
        /// </summary>
        private void SetupLevel(LevelConfig config)
        {
            // 풍선 필드 사이즈 자동 계산
            // boardWorldSize를 컨베이어 내부 최대 영역에서 역산
            if (GameManager.HasInstance && config.gridCols > 0)
            {
                float innerW = BoardTileManager.CONVEYOR_WIDTH - BoardTileManager.RAIL_THICKNESS - BoardTileManager.RAIL_GAP * 2f;
                float innerH = BoardTileManager.CONVEYOR_HEIGHT - BoardTileManager.RAIL_THICKNESS - BoardTileManager.RAIL_GAP * 2f;
                int cols = config.gridCols;
                int rows = config.gridRows > 0 ? config.gridRows : cols;
                int maxDim = Mathf.Max(cols, rows);
                // maxDim 기준으로 boardWorldSize 역산 (가로/세로 중 제한되는 쪽)
                float bwsFromW = innerW / cols * maxDim;
                float bwsFromH = innerH / rows * maxDim;
                float boardWorldSize = Mathf.Min(bwsFromW, bwsFromH);
                GameManager.Instance.Board.cellSpacing = boardWorldSize / maxDim;
            }

            // Reset score first so subsystems receive the correct thresholds
            if (ScoreManager.HasInstance)
            {
                ScoreManager.Instance.InitializeLevel(config.balloonCount);
                ScoreManager.Instance.ResetScore();
            }

            // Reset dart state from previous level
            if (DartManager.HasInstance)
            {
                DartManager.Instance.ResetAll();
            }

            // Reset pop/combo counters from previous level
            if (PopProcessor.HasInstance)
            {
                PopProcessor.Instance.ResetAll();
            }

            // Calculate total darts and rail capacity (needed by both RailManager and HolderManager)
            int totalDarts = 0;
            if (config.holders != null)
            {
                for (int i = 0; i < config.holders.Length; i++)
                    totalDarts += config.holders[i].magazineCount;
            }

            int explicitCapacity = config.railCapacity > 0 ? config.railCapacity
                : (config.rail != null && config.rail.slotCount > 0) ? config.rail.slotCount : 0;
            int slotCount = RailManager.CalculateCapacity(totalDarts, explicitCapacity);

            // Initialize 2D floor tilemap and conveyor belt tiles BEFORE rail setup
            // so that BoardTileManager's fixed rail proportions are available for waypoint generation.
            float cellSpacing = GameManager.HasInstance
                ? GameManager.Instance.Board.cellSpacing
                : 0.55f;
            float boardCX = GameManager.HasInstance ? GameManager.Instance.Board.boardCenterX : 0f;
            float boardCZ = GameManager.HasInstance ? GameManager.Instance.Board.boardCenterZ : 2f;

            int tileCols = config.gridCols > 0 ? config.gridCols : 5;
            int tileRows = config.gridRows > 0 ? config.gridRows : 5;

            if (BoardTileManager.HasInstance)
            {
                BoardTileManager.Instance.InitializeBoard(
                    tileCols, tileRows,
                    new Vector2(boardCX, boardCZ),
                    cellSpacing
                );

                // RailSideCount를 먼저 설정 → BuildConveyorBelt에서 면 수 반영
                int railSides = RailManager.GetRailSideCount(slotCount);
                BoardTileManager.Instance.RailSideCount = railSides;

                // 면 수에 맞는 컨베이어벨트 타일 빌드
                BoardTileManager.Instance.BuildConveyorBelt();
                BoardTileManager.Instance.BuildDangerOverlay();

            }

            // Rail setup via RailManager (slot-based conveyor belt) with variable capacity.
            if (RailManager.HasInstance && config.rail != null)
            {
                Vector3[] waypoints = null;

                // 허용량에 따라 1~4면 웨이포인트 생성
                if (BoardTileManager.HasInstance)
                {
                    var btm = BoardTileManager.Instance;
                    float h = 0.1f;

                    // 타일 배치와 동일한 좌표 사용 (외곽 = 타일 중심)
                    float halfCW = btm.TotalAreaWidth * 0.5f;
                    float halfCH = btm.TotalAreaHeight * 0.5f;

                    float l = boardCX - halfCW;
                    float r = boardCX + halfCW;
                    float b = boardCZ - halfCH;
                    float t = boardCZ + halfCH;

                    int sides = btm.RailSideCount;

                    switch (sides)
                    {
                        case 1: // 하단만 (→)
                            waypoints = new Vector3[]
                            {
                                new Vector3(l, h, b),
                                new Vector3(r, h, b)
                            };
                            break;
                        case 2: // 하단(→) + 우측(↑)
                            waypoints = new Vector3[]
                            {
                                new Vector3(l, h, b),
                                new Vector3(r, h, b),
                                new Vector3(r, h, t)
                            };
                            break;
                        case 3: // 하단(→) + 우측(↑) + 상단(←)
                            waypoints = new Vector3[]
                            {
                                new Vector3(l, h, b),
                                new Vector3(r, h, b),
                                new Vector3(r, h, t),
                                new Vector3(l, h, t)
                            };
                            break;
                        default: // 4면 전체 (사각형 순환)
                            waypoints = new Vector3[]
                            {
                                new Vector3(l, h, b),
                                new Vector3(r, h, b),
                                new Vector3(r, h, t),
                                new Vector3(l, h, t)
                            };
                            break;
                    }
                }

                bool smooth = config.rail.smoothCorners;
                float radius = config.rail.cornerRadius > 0f ? config.rail.cornerRadius : 2.5f;
                // 4면만 closedLoop (물리적 순환). 1~3면은 개방 경로 + 슬롯 래핑으로 순간이동
                int sideCount = RailManager.GetRailSideCount(slotCount);
                bool isLoop = (sideCount >= 4);
                RailManager.Instance.SetRailLayout(waypoints, slotCount, isLoop, smooth, radius);

                // RailManager 초기화 완료 → Arrow 생성
                if (BoardTileManager.HasInstance)
                    BoardTileManager.Instance.SpawnArrows();
            }

            // Apply rail visual type to RailRenderer
            var railRenderer = FindAnyObjectByType<RailRenderer>();
            if (railRenderer != null && config.rail != null)
            {
                // visualType 0(Cylinder)은 레거시 기본값 — SpriteTile(3)로 강제
                int vt = config.rail.visualType;
                if (vt == 0) vt = RailRenderer.VISUAL_SPRITE_TILE;
                railRenderer.VisualType = vt;
            }

            // Initialize holders from level config (column-based queue)
            // Pass slotCount for per-tier magazine cap enforcement
            if (HolderManager.HasInstance && config.holders != null)
            {
                HolderManager.Instance.InitializeHoldersFromConfig(config.holders, config.queueColumns, slotCount);
            }

            // Apply balloon scale
            if (BalloonController.HasInstance && config.balloonScale > 0f)
            {
                BalloonController.Instance.SetBalloonScale(config.balloonScale);
            }

            // Initialize balloons from level config
            if (BalloonController.HasInstance && config.balloons != null)
            {
                var balloonLayout = new System.Collections.Generic.List<BalloonSetupData>(config.balloons.Length);
                for (int i = 0; i < config.balloons.Length; i++)
                {
                    BalloonLayout bl = config.balloons[i];
                    balloonLayout.Add(new BalloonSetupData
                    {
                        color       = bl.color,
                        position    = new Vector3(bl.gridPosition.x, 0.1f, bl.gridPosition.y),
                        gimmickType = bl.gimmickType,
                        groupId     = -1,
                        sizeW       = bl.sizeW > 0 ? bl.sizeW : 1,
                        sizeH       = bl.sizeH > 0 ? bl.sizeH : 1,
                        hp          = bl.hp,
                        lockPairId  = bl.lockPairId
                    });
                }
                BalloonController.Instance.SetupBalloons(balloonLayout, config.levelId);
            }

            // Initialize board state tracking with actual balloon count
            if (BoardStateManager.HasInstance)
            {
                BoardStateManager.Instance.InitializeBoard(config.levelId, config.balloonCount);
            }

            // Initialize gimmick state for this level
            if (GimmickManager.HasInstance)
            {
                GimmickManager.Instance.InitializeGimmicks(config);
            }

            // 카메라 orthoSize 고정 (해상도/비율 픽스)
            if (CameraManager.HasInstance && CameraManager.Instance.MainCamera != null)
            {
                CameraManager.Instance.MainCamera.orthographicSize = 15f;
            }

            // Publish level-loaded for any remaining listeners (HUDController, etc.)
            EventBus.Publish(new OnLevelLoaded
            {
                levelId   = config.levelId,
                packageId = config.packageId
            });
        }

        /// <summary>
        /// Persists level progress to PlayerPrefs.
        /// Updates highest completed level and best star count.
        /// </summary>
        private void SaveLevelProgress(int levelId, int stars)
        {
            // Best stars
            string starsKey  = PREFS_KEY_STARS_PREFIX + levelId;
            int    bestStars = PlayerPrefs.GetInt(starsKey, 0);
            if (stars > bestStars)
            {
                PlayerPrefs.SetInt(starsKey, stars);
            }

            // Highest completed level
            int highest = PlayerPrefs.GetInt(PREFS_KEY_HIGHEST_LEVEL, 0);
            if (levelId > highest)
            {
                PlayerPrefs.SetInt(PREFS_KEY_HIGHEST_LEVEL, levelId);
            }

            PlayerPrefs.Save();
        }

        private bool ValidateProvider()
        {
            if (_levelDataProvider == null)
            {
                Debug.LogWarning("[LevelManager] LevelDataProvider is not assigned in the Inspector.");
                return false;
            }

            return true;
        }

        // ── EventBus handlers ──────────────────────────────────────────────────

        private void HandleBoardCleared(OnBoardCleared evt)
        {
            CompleteLevel(evt.score, evt.starCount);
        }

        private void HandleBoardFailed(OnBoardFailed evt)
        {
            // If continues are available, ContinueHandler will show the popup.
            // Don't mark level as failed yet — wait for continue timeout or decline.
            if (ContinueHandler.HasInstance && ContinueHandler.Instance.CanContinue())
            {
                Debug.Log($"[LevelManager] Board failed but continues available ({ContinueHandler.Instance.ContinueCount}/{4}). Deferring FailLevel.");
                return;
            }

            FailLevel();
        }

        #endregion
    }
}
