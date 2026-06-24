using System.Collections;
using System.Collections.Generic;
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

        public  const string PREFS_KEY_HIGHEST_LEVEL    = "BF_HighestLevel";
        private const string PREFS_KEY_STARS_PREFIX      = "BF_Stars_";
        private const int    FIRST_LEVEL_ID              = 1;
        private const int    LEVELS_PER_PACKAGE          = 20;
        private const float  LOADING_FADE_DURATION       = 0.25f;

        #endregion

        #region Fields

        [SerializeField]
        private LevelDataProvider _levelDataProvider;

        private LevelConfig _currentLevelConfig;
        private int         _currentLevelId;
        private bool        _levelActive;
        private bool        _currentLevelEndedInClear;
        private int         _retryCount;
        private bool        _isLoading;

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

        /// <summary>
        /// True after the current level has ended by clear. Used by life consumption guards.
        /// </summary>
        public bool CurrentLevelEndedInClear => _currentLevelEndedInClear;

        /// <summary>True while LoadLevelCoroutine is in progress (cleanup + setup). GameManager가 fade-in 시점 동기화에 사용.</summary>
        public bool IsLoading => _isLoading;

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
        /// Internally runs as coroutine: fade out → cleanup → setup (yielded across frames) → fade in.
        /// Does nothing if the level ID is invalid or LevelDataProvider is unassigned.
        /// </summary>
        public void LoadLevel(int levelId)
        {
            StartCoroutine(LoadLevelCoroutine(levelId));
        }

        /// <summary>
        /// next-stage / retry 전환에서 강제 최소 로딩 시간 (초). fadeOut + setup + warmup 합산이 미만이면 hold.
        /// GameManager.MIN_INGAME_LOAD_DURATION 과 동일 의미.
        /// </summary>
        private const float MIN_LEVEL_LOAD_DURATION = 2.5f;

        private IEnumerator LoadLevelCoroutine(int levelId)
        {
            _isLoading = true;
            float coStart = Time.realtimeSinceStartup;

            // ── Fade out (loading screen 역할) ──
            // GameManager.LoadScene 직후 호출되는 케이스(씬 전환 → InGame)에선 이미 fade overlay가 활성이라
            // 여기서 또 FadeOut 하면 두 번 페이드되어 보임 → IsFading 시 skip.
            bool ownsFade = false;
            if (UIManager.HasInstance && !UIManager.Instance.IsFading)
            {
                UIManager.Instance.FadeOut(LOADING_FADE_DURATION);
                yield return new WaitForSecondsRealtime(LOADING_FADE_DURATION);
                ownsFade = true;
            }

            // ── 에피소드 prefetch ── 캐시 miss 면 fade overlay 가 가린 동안 fetch.
            if (LevelEpisodeService.HasInstance)
            {
                int needPkg = LevelEpisodeService.PackageIdForLevel(levelId);
                if (LevelEpisodeService.Instance.CurrentPackageId != needPkg)
                {
                    var epTask = LevelEpisodeService.Instance.EnsureEpisodeAsync(needPkg);
                    while (!epTask.IsCompleted) yield return null;
                    if (epTask.IsCompletedSuccessfully && !epTask.Result)
                        Debug.LogWarning($"[LevelManager] Episode {needPkg} prefetch 실패 → 폴백 시도.");
                }
            }

            // ── Config 로드 ──
            LevelConfig config = LoadConfig(levelId);
            if (config == null)
            {
                Debug.LogWarning($"[LevelManager] Cannot load level {levelId}: no config found.");
                if (ownsFade && UIManager.HasInstance) UIManager.Instance.FadeIn(LOADING_FADE_DURATION);
                _isLoading = false;
                yield break;
            }

            // ── Cleanup ──
            CleanupPreviousLevel();
            yield return null; // GPU 한 프레임 양보

            _currentLevelId     = levelId;
            _currentLevelConfig = config;
            _levelActive        = true;
            _currentLevelEndedInClear = false;
            _retryCount         = 0;

            // ── Setup (yields 분산) ──
            yield return SetupLevelCoroutine(config);

            // ── Warmup ── fade overlay 가 opaque 인 상태에서 몇 프레임 렌더 → first-frame shader / material 비용 흡수
            if (ownsFade)
                for (int i = 0; i < 3; i++) yield return new WaitForEndOfFrame();

            // ── 최소 로딩 시간 보장 ── 셋업이 빨라도 사용자에게 일정 시간 로딩 화면 노출.
            // GameManager 가 fade 를 관리 중이면 (외부 LoadScene 경로) 거기서 별도 보장 → skip.
            if (ownsFade)
            {
                float elapsed = Time.realtimeSinceStartup - coStart;
                if (elapsed < MIN_LEVEL_LOAD_DURATION)
                    yield return new WaitForSecondsRealtime(MIN_LEVEL_LOAD_DURATION - elapsed);
            }

            // ── Fade in ── (자체 fade일 때만. GameManager 가 fade 관리 중이면 GM이 IsLoading 감지 후 fade-in)
            if (ownsFade && UIManager.HasInstance)
                UIManager.Instance.FadeIn(LOADING_FADE_DURATION);

            _isLoading = false;
        }

        private LevelConfig LoadConfig(int levelId)
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

            if (config == null && ValidateProvider())
                config = _levelDataProvider.GetLevelData(levelId);

            if (config == null && LevelGenerator.HasInstance)
            {
                Debug.Log($"[LevelManager] No pre-authored config for level {levelId}. Falling back to LevelGenerator.");
                config = LevelGenerator.Instance.GenerateLevel(levelId);
            }

            return config;
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
            _currentLevelEndedInClear = false;
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
            _currentLevelEndedInClear = true;
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
            _currentLevelEndedInClear = false;

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
            // [2026-05-11] CoinFlyEffect 진행 중 코루틴 + 활성 코인 GameObject 강제 정리.
            // 정상 클리어 흐름에선 reward 시퀀스 완료 후 다음 레벨이라 자연 정리되지만,
            // DEV 치트 (JumpLevel) 는 중간 끊김 → stale coroutine + 활성 코인 GameObject 잔존 → 메모리 누수.
            CoinFlyEffect.StopAll();

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
        /// 코루틴 버전 — 무거운 셋업 단계 사이에 yield return null 삽입해 한 프레임 부하 분산.
        /// FadeOut → 이 함수 → FadeIn 순서로 호출됨.
        /// </summary>
        private IEnumerator SetupLevelCoroutine(LevelConfig config)
        {
            SetupLevel(config);
            yield return null;
        }

        /// <summary>
        /// Coordinates subsystem setup for the given level config.
        /// Publishes setup events for balloons, holders, and rail, then resets the score.
        /// </summary>
        private void SetupLevel(LevelConfig config)
        {
            // 풍선 필드 사이즈 자동 계산
            // 컨베이어 inner area에 cols/rows가 들어갈 최대 spacing을 산출.
            // 베이크된 좌표가 다른 spacing을 쓰면 board center 기준으로 rescale —
            // 임포터/MapMaker/Procedural 어디서 만들어진 데이터든 현재 conveyor에 맞춰짐.
            if (GameManager.HasInstance && config.gridCols > 0)
            {
                float innerW = BoardTileManager.CONVEYOR_WIDTH - BoardTileManager.RAIL_THICKNESS - BoardTileManager.RAIL_GAP * 2f;
                float innerH = BoardTileManager.CONVEYOR_HEIGHT - BoardTileManager.RAIL_THICKNESS - BoardTileManager.RAIL_GAP * 2f;
                int cols = config.gridCols;
                int rows = config.gridRows > 0 ? config.gridRows : cols;
                float targetSpacing = Mathf.Min(innerW / cols, innerH / rows);

                // ROLLBACK_DART_SPEED_MAPMAKER_SPACING:
                // detected spacing은 grid-aligned 여부 판별용 — grid 데이터는 targetSpacing으로
                // 재투영하고, 비균등 배치(detect 실패)는 그대로 둠.
                float detectedSpacing = DetectSpacingFromBalloons(config.balloons);
                if (detectedSpacing > 0f && config.balloons != null
                    && !Mathf.Approximately(detectedSpacing, targetSpacing))
                {
                    float scale = targetSpacing / detectedSpacing;
                    float cx = GameManager.Instance.Board.boardCenterX;
                    float cz = GameManager.Instance.Board.boardCenterZ;
                    for (int i = 0; i < config.balloons.Length; i++)
                    {
                        var bl = config.balloons[i];
                        if (bl == null) continue;
                        bl.gridPosition = new Vector2(
                            cx + (bl.gridPosition.x - cx) * scale,
                            cz + (bl.gridPosition.y - cz) * scale);
                    }
                }

                GameManager.Instance.Board.cellSpacing = targetSpacing;
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

            // Reset gimmick state (Ice HP, Pin segments, Surprise, Color Curtain counters).
            // Without this, stale balloonId-keyed entries from previous levels can collide
            // with new-level balloonIds and trigger unintended ForcePopBalloon on balloons
            // that share IDs with orphaned Ice/Curtain entries.
            if (GimmickProcessor.HasInstance)
            {
                GimmickProcessor.Instance.ResetAll();
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
            int railSideCount = RailManager.GetRailSideCount(slotCount);

#if UNITY_EDITOR || DEVELOPMENT_BUILD
            int authoredRailSlotCount = config.rail != null ? config.rail.slotCount : 0;
            int authoredWaypointCount = config.rail != null && config.rail.waypoints != null ? config.rail.waypoints.Length : 0;
            int expectedRuntimeWaypointCount = railSideCount >= 4 ? 4 : railSideCount + 1;
            Debug.Log(
                $"[LevelManager/RailShape] level={_currentLevelId} totalDarts={totalDarts} " +
                $"railCapacity={config.railCapacity} rail.slotCount={authoredRailSlotCount} " +
                $"resolvedCapacity={slotCount} sides={railSideCount} " +
                $"authoredWaypoints={authoredWaypointCount} runtimeWaypoints={expectedRuntimeWaypointCount}");

            if (authoredWaypointCount > 0 && authoredWaypointCount != expectedRuntimeWaypointCount)
            {
                Debug.LogWarning(
                    $"[LevelManager/RailShape] level={_currentLevelId} authored waypoint count " +
                    $"{authoredWaypointCount} does not match capacity-derived runtime waypoint count " +
                    $"{expectedRuntimeWaypointCount}. Authored rail.waypoints are ignored during runtime setup.");
            }
#endif

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
                int railSides = railSideCount;
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
                    // ROLLBACK_RAIL_SHAPE_FROM_CAPACITY:
                    // Runtime rail geometry intentionally follows the resolved capacity/sides,
                    // not serialized rail.waypoints. Episode exports can leave stale authored
                    // waypoints, which makes MapMaker and runtime appear to use different shapes.
                    // Revert this block only if authored waypoints become the single source of truth.
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
                // 레벨 데이터의 cornerRadius는 의도적으로 무시 — 모든 레벨 1.5f 고정
                float radius = 1.5f;
                // 4면만 closedLoop (물리적 순환). 1~3면은 개방 경로 + 슬롯 래핑으로 순간이동
                int sideCount = railSideCount;
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

            NormalizeLevelGimmickTypes(config);

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

            // [#2 개정 2026-06-10] 보드 가로 칸수 전달 — scale.y 분기(≤26: x×1.1 / ≥27: 0.35 고정) 기준.
            // balloonScale 가드와 별개로 매 레벨 무조건 갱신 (이전 레벨 값 잔존 방지. 0=미설정 폴백).
            if (BalloonController.HasInstance)
            {
                BalloonController.Instance.SetBoardGridCols(config.gridCols);
            }

            // Initialize balloons from level config
            if (BalloonController.HasInstance && config.balloons != null)
            {
                var balloonLayout = new System.Collections.Generic.List<BalloonSetupData>(config.balloons.Length);
                for (int i = 0; i < config.balloons.Length; i++)
                {
                    BalloonLayout bl = config.balloons[i];
                    string gimmickType = GimmickDisplayName.Normalize(bl.gimmickType);
                    balloonLayout.Add(new BalloonSetupData
                    {
                        color       = bl.color,
                        position    = new Vector3(bl.gridPosition.x, 0.1f, bl.gridPosition.y),
                        gimmickType = gimmickType,
                        groupId     = -1,
                        sizeW       = bl.sizeW > 0 ? bl.sizeW : 1,
                        sizeH       = bl.sizeH > 0 ? bl.sizeH : 1,
                        hp          = bl.hp,
                        iceBlockSize = bl.iceBlockSize > 0 ? bl.iceBlockSize : 1,
                        // ROLLBACK_ICE_MANUAL_GROUP_20260608:
                        // Pass optional MapMaker-authored Ice group metadata to runtime.
                        iceGroupId = bl.iceGroupId,
                        iceGroupHp = bl.iceGroupHp,
                        iceGroupHpMode = bl.iceGroupHpMode,
                        barricadeDir    = bl.barricadeDir,
                        barricadeLength = bl.barricadeLength > 0 ? bl.barricadeLength : 1,
                        eggColors   = bl.eggColors,
                        eggHps      = bl.eggHps,
                        lockPairId  = bl.lockPairId,
                        flexTubeGroupId       = bl.flexTubeGroupId,
                        flexTubePartType      = bl.flexTubePartType,
                        flexTubeSequenceIndex = bl.flexTubeSequenceIndex,
                        flexTubeHp            = bl.flexTubeHp
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
                // ROLLBACK_WINNING_STREAK_HIGHEST_LEVEL_SYNC_20260601:
                // Previous behavior updated only PlayerPrefs. WinningStreakManager gates on
                // UserData.highestClearedLevel, so keep the Firebase-backed user state in sync when
                // a new highest clear is recorded.
                if (UserDataService.HasInstance)
                    UserDataService.Instance.SetHighestClearedLevel(levelId);
            }

            PlayerPrefs.Save();
        }

        private static float DetectSpacingFromBalloons(BalloonLayout[] balloons)
        {
            if (balloons == null || balloons.Length < 2) return -1f;

            var xs = new List<float>(balloons.Length);
            var zs = new List<float>(balloons.Length);
            for (int i = 0; i < balloons.Length; i++)
            {
                xs.Add(balloons[i].gridPosition.x);
                zs.Add(balloons[i].gridPosition.y);
            }

            xs.Sort();
            zs.Sort();

            float minGap = float.MaxValue;
            DetectMinPositiveGap(xs, ref minGap);
            DetectMinPositiveGap(zs, ref minGap);

            return minGap < float.MaxValue ? minGap : -1f;
        }

        private static void DetectMinPositiveGap(List<float> values, ref float minGap)
        {
            for (int i = 1; i < values.Count; i++)
            {
                float gap = values[i] - values[i - 1];
                if (gap > 0.01f && gap < minGap)
                    minGap = gap;
            }
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

        private static void NormalizeLevelGimmickTypes(LevelConfig config)
        {
            if (config == null) return;

            if (config.gimmickTypes != null)
            {
                for (int i = 0; i < config.gimmickTypes.Length; i++)
                    config.gimmickTypes[i] = GimmickDisplayName.Normalize(config.gimmickTypes[i]);
            }

            if (config.balloons != null)
            {
                for (int i = 0; i < config.balloons.Length; i++)
                {
                    if (config.balloons[i] == null) continue;
                    config.balloons[i].gimmickType = GimmickDisplayName.Normalize(config.balloons[i].gimmickType);
                }
            }

            if (config.holders != null)
            {
                for (int i = 0; i < config.holders.Length; i++)
                {
                    if (config.holders[i] == null) continue;
                    string queueGimmick = GimmickDisplayName.Normalize(config.holders[i].queueGimmick);
                    config.holders[i].queueGimmick = queueGimmick == "none" ? "" : queueGimmick;
                }
            }
        }

        #endregion
    }
}
