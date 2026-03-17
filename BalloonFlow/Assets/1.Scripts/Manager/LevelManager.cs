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
        /// Coordinates subsystem setup for the given level config.
        /// Publishes setup events for balloons, holders, and rail, then resets the score.
        /// </summary>
        private void SetupLevel(LevelConfig config)
        {
            // Update cellSpacing based on level grid size (critical for targeting + fail detection)
            if (GameManager.HasInstance && config.gridCols > 0)
            {
                float boardWorldSize = 8f; // must match LevelDatabaseGenerator50.BOARD_WORLD_SIZE
                GameManager.Instance.Board.cellSpacing = boardWorldSize / config.gridCols;
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

            // Rail setup via RailManager (slot-based conveyor belt)
            if (RailManager.HasInstance && config.rail != null)
            {
                int slotCount = config.rail.slotCount > 0 ? config.rail.slotCount : 200;
                bool smooth = config.rail.smoothCorners;
                float radius = config.rail.cornerRadius > 0f ? config.rail.cornerRadius : 1f;
                RailManager.Instance.SetRailLayout(config.rail.waypoints, slotCount, true, smooth, radius);
            }

            // Apply rail visual type to RailRenderer
            var railRenderer = FindAnyObjectByType<RailRenderer>();
            if (railRenderer != null && config.rail != null)
            {
                railRenderer.VisualType = config.rail.visualType;
            }

            // Initialize holders from level config (column-based queue)
            if (HolderManager.HasInstance && config.holders != null)
            {
                var holderSetup = new System.Collections.Generic.List<(int color, int magazineCount)>(config.holders.Length);
                for (int i = 0; i < config.holders.Length; i++)
                {
                    holderSetup.Add((config.holders[i].color, config.holders[i].magazineCount));
                }
                HolderManager.Instance.InitializeHolders(holderSetup, config.queueColumns);
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
                        position    = new Vector3(bl.gridPosition.x, 0.5f, bl.gridPosition.y),  // XZ plane: gridPosition.y = world Z
                        gimmickType = bl.gimmickType,
                        groupId     = -1
                    });
                }
                BalloonController.Instance.SetupBalloons(balloonLayout, config.levelId);
            }

            // Initialize 2D floor tilemap and conveyor belt tiles
            if (BoardTileManager.HasInstance)
            {
                float cellSpacing = GameManager.HasInstance
                    ? GameManager.Instance.Board.cellSpacing
                    : 0.55f;
                float boardCX = GameManager.HasInstance ? GameManager.Instance.Board.boardCenterX : 0f;
                float boardCZ = GameManager.HasInstance ? GameManager.Instance.Board.boardCenterZ : 2f;

                int tileCols = config.gridCols > 0 ? config.gridCols : 5;
                int tileRows = config.gridRows > 0 ? config.gridRows : 5;

                BoardTileManager.Instance.InitializeBoard(
                    tileCols, tileRows,
                    new Vector2(boardCX, boardCZ),
                    cellSpacing
                );

                // Apply conveyor tile positions from level config, or auto-build rectangular belt
                if (config.conveyorPositions != null && config.conveyorPositions.Length > 0)
                {
                    BoardTileManager.Instance.SetConveyorFromConfig(config.conveyorPositions);
                }
                else
                {
                    BoardTileManager.Instance.BuildConveyorBelt();
                }
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
            FailLevel();
        }

        #endregion
    }
}
