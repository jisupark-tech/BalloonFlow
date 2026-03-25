using System.Collections.Generic;
using UnityEngine;

namespace BalloonFlow
{
    /// <summary>
    /// 6-stage gauge system for rail overflow fail detection.
    /// Design ref: 레일초과_코어메카닉_명세 (2026-03-17)
    /// </summary>
    public enum GaugeStage
    {
        Safe,        // 0~49%
        Caution,     // 50~69%
        NormalHigh,  // 70~84%
        Warning,     // 85~94%
        Critical,    // 95% ~ capacity-2
        Fail         // capacity-1 + no outermost match + 1.5s grace
    }

    /// <summary>
    /// Tracks overall board state and owns clear/fail condition evaluation.
    /// Rail Overflow mode: 6-stage gauge (SAFE→CAUTION→NORMAL_HIGH→WARNING→CRITICAL→FAIL).
    /// Fail = capacity-1 slot occupied + no outermost balloon match + 1.5s grace delay expires.
    /// </summary>
    /// <remarks>
    /// Layer: Domain | Genre: Puzzle | Role: Manager | Phase: 1
    /// DB Reference: Generated from Rail Overflow spec — occupancy-based fail conditions
    /// </remarks>
    public class BoardStateManager : SceneSingleton<BoardStateManager>
    {
        #region Constants — Gauge Thresholds

        // Occupancy ratio thresholds for each gauge stage
        private const float THRESHOLD_CAUTION     = 0.50f;
        private const float THRESHOLD_NORMAL_HIGH = 0.70f;
        private const float THRESHOLD_WARNING     = 0.85f;
        private const float THRESHOLD_CRITICAL    = 0.95f;

        #endregion

        #region Fields

        private BoardState _currentState;
        private int _remainingBalloons;
        private int _currentLevelId;
        private float _failGraceDelay = 1.5f;
        // Design ref: 실패 조건 = "허용량-1개" (capacity - 1, 정수 기준)
        // 이전: float 0.995f → capacity 50에서 1개 차이 발생. 정수 비교로 변경.

        // 6-stage gauge
        private GaugeStage _currentGaugeStage = GaugeStage.Safe;

        // Rail overflow fail tracking
        private bool _isCritical;
        private float _criticalTimer;
        private bool _failConfirmed;

        #endregion

        #region Properties

        public BoardState CurrentState => _currentState;
        public int RemainingBalloons => _remainingBalloons;
        public GaugeStage CurrentGaugeStage => _currentGaugeStage;

        #endregion

        #region Lifecycle

        protected override void OnSingletonAwake()
        {
            _currentState = BoardState.Playing;
            _remainingBalloons = 0;
            _currentLevelId = -1;
            _isCritical = false;
            _criticalTimer = 0f;
            _failConfirmed = false;

            if (GameManager.HasInstance)
            {
                _failGraceDelay = GameManager.Instance.Board.failGraceDelay;
            }
        }

        private void OnEnable()
        {
            EventBus.Subscribe<OnLevelLoaded>(HandleLevelLoaded);
            EventBus.Subscribe<OnBalloonPopped>(HandleBalloonPopped);
            EventBus.Subscribe<OnBalloonSpawned>(HandleBalloonSpawned);
            EventBus.Subscribe<OnRailOccupancyChanged>(HandleRailOccupancy);
            EventBus.Subscribe<OnAllHoldersEmpty>(HandleAllHoldersEmpty);
            EventBus.Subscribe<OnContinueApplied>(HandleContinueApplied);
        }

        private void OnDisable()
        {
            EventBus.Unsubscribe<OnLevelLoaded>(HandleLevelLoaded);
            EventBus.Unsubscribe<OnBalloonPopped>(HandleBalloonPopped);
            EventBus.Unsubscribe<OnBalloonSpawned>(HandleBalloonSpawned);
            EventBus.Unsubscribe<OnRailOccupancyChanged>(HandleRailOccupancy);
            EventBus.Unsubscribe<OnAllHoldersEmpty>(HandleAllHoldersEmpty);
            EventBus.Unsubscribe<OnContinueApplied>(HandleContinueApplied);
        }

        private void Update()
        {
            if (_currentState != BoardState.Playing) return;

            // Grace delay timer for rail overflow fail
            if (_isCritical && !_failConfirmed)
            {
                // Re-check: has occupancy dropped below capacity-1?
                if (RailManager.HasInstance &&
                    RailManager.Instance.OccupiedCount < RailManager.Instance.SlotCount - 1)
                {
                    // Recovered!
                    _isCritical = false;
                    _criticalTimer = 0f;
                    return;
                }

                // Re-check: has outermost match appeared?
                if (HasOutermostMatch())
                {
                    // Recovery possible — matching will happen naturally
                    _isCritical = false;
                    _criticalTimer = 0f;
                    return;
                }

                _criticalTimer += Time.deltaTime;

                if (_criticalTimer >= _failGraceDelay)
                {
                    _failConfirmed = true;
                    TriggerFail(FailReason.RailOverflow);
                }
            }
        }

        #endregion

        #region Public Methods

        /// <summary>
        /// Initializes the board for a new level.
        /// </summary>
        public void InitializeBoard(int levelId, int initialBalloonCount)
        {
            _currentLevelId = levelId;
            _remainingBalloons = initialBalloonCount;
            _currentState = BoardState.Playing;
            _currentGaugeStage = GaugeStage.Safe;
            _isCritical = false;
            _criticalTimer = 0f;
            _failConfirmed = false;

            PublishBoardStateChanged();
            Debug.Log($"[BoardStateManager] Board initialized. Level={levelId}, Balloons={initialBalloonCount}");
        }

        /// <summary>
        /// Returns the current gauge stage based on rail occupancy.
        /// </summary>
        public GaugeStage GetGaugeStage()
        {
            return _currentGaugeStage;
        }

        public BoardState GetBoardState()
        {
            return _currentState;
        }

        public int GetRemainingBalloons()
        {
            return _remainingBalloons;
        }

        public bool IsBoardClear()
        {
            return _remainingBalloons <= 0;
        }

        /// <summary>
        /// Evaluates fail conditions and returns a FailResult.
        /// Does not change board state — only evaluates.
        /// </summary>
        public FailResult CheckFailCondition()
        {
            // Condition: Rail overflow — capacity-1 (정수 비교) + no outermost match
            if (RailManager.HasInstance)
            {
                int occupied = RailManager.Instance.OccupiedCount;
                int capacity = RailManager.Instance.SlotCount;
                if (occupied >= capacity - 1 && !HasOutermostMatch())
                {
                    return new FailResult
                    {
                        isFail = true,
                        reason = FailReason.RailOverflow
                    };
                }
            }

            // Condition: No moves left — all holders consumed + all rail darts unable to match + balloons remain
            // (This is a subset of RailOverflow when rail is not full but no more darts can fire)
            if (HolderManager.HasInstance && HolderManager.Instance.AreAllHoldersEmpty())
            {
                if (RailManager.HasInstance && RailManager.Instance.OccupiedCount == 0 && _remainingBalloons > 0)
                {
                    return new FailResult
                    {
                        isFail = true,
                        reason = FailReason.NoMovesLeft
                    };
                }
            }

            return new FailResult { isFail = false, reason = FailReason.None };
        }

        #endregion

        #region Private Methods — Event Handlers

        private void HandleLevelLoaded(OnLevelLoaded evt)
        {
            _currentLevelId = evt.levelId;
        }

        private void HandleBalloonPopped(OnBalloonPopped evt)
        {
            if (BalloonController.HasInstance)
            {
                _remainingBalloons = BalloonController.Instance.GetRemainingCount();
            }
            else
            {
                _remainingBalloons = Mathf.Max(0, _remainingBalloons - 1);
            }

            PublishBoardStateChanged();

            // Clear always wins, even from Failed state
            if (_currentState == BoardState.Playing || _currentState == BoardState.Failed)
            {
                EvaluateClearCondition();
            }

            // Recovery: balloon popped may open outermost match or free rail slots
            if (_isCritical && HasOutermostMatch())
            {
                _isCritical = false;
                _criticalTimer = 0f;
            }
        }

        private void HandleBalloonSpawned(OnBalloonSpawned evt)
        {
            if (_currentState != BoardState.Playing) return;
            _remainingBalloons++;
            PublishBoardStateChanged();
        }

        private void HandleRailOccupancy(OnRailOccupancyChanged evt)
        {
            if (_currentState != BoardState.Playing) return;

            // Evaluate 6-stage gauge (with integer-based Fail check)
            GaugeStage newStage = EvaluateGaugeStageWithFail(evt.occupancy, evt.activeDarts, evt.totalSlots);
            if (newStage != _currentGaugeStage)
            {
                GaugeStage prevStage = _currentGaugeStage;
                _currentGaugeStage = newStage;

                EventBus.Publish(new OnGaugeStageChanged
                {
                    previousStage = (int)prevStage,
                    currentStage = (int)newStage,
                    occupancy = evt.occupancy
                });
            }

            // Fail trigger: integer-based "capacity - 1" check (Design ref: 레일초과_코어메카닉_명세)
            bool atFailThreshold = evt.activeDarts >= evt.totalSlots - 1;

            if (atFailThreshold)
            {
                // Enter critical check
                bool hasMatch = HasOutermostMatch();

                EventBus.Publish(new OnRailCritical
                {
                    occupancy = evt.occupancy,
                    hasOutermostMatch = hasMatch
                });

                if (!hasMatch && !_isCritical)
                {
                    // Start grace delay
                    _isCritical = true;
                    _criticalTimer = 0f;
                }
            }
            else
            {
                // Below threshold — cancel any critical state
                if (_isCritical)
                {
                    _isCritical = false;
                    _criticalTimer = 0f;
                }
            }
        }

        private GaugeStage EvaluateGaugeStage(float occupancy)
        {
            // Fail stage uses integer check (capacity-1) — see HandleRailOccupancy
            // Here we only check ratio-based visual stages (Safe~Critical)
            if (occupancy >= THRESHOLD_CRITICAL)      return GaugeStage.Critical;
            if (occupancy >= THRESHOLD_WARNING)        return GaugeStage.Warning;
            if (occupancy >= THRESHOLD_NORMAL_HIGH)    return GaugeStage.NormalHigh;
            if (occupancy >= THRESHOLD_CAUTION)        return GaugeStage.Caution;
            return GaugeStage.Safe;
        }

        /// <summary>
        /// Integer-based gauge stage evaluation including Fail.
        /// Used when activeDarts and totalSlots are available.
        /// </summary>
        private GaugeStage EvaluateGaugeStageWithFail(float occupancy, int activeDarts, int totalSlots)
        {
            // Design: Fail = capacity-1 (정수 비교)
            if (totalSlots > 0 && activeDarts >= totalSlots - 1) return GaugeStage.Fail;
            return EvaluateGaugeStage(occupancy);
        }

        private void HandleAllHoldersEmpty(OnAllHoldersEmpty evt)
        {
            if (_currentState != BoardState.Playing) return;

            // 남은 풍선 색상별 로그
            if (BalloonController.HasInstance)
            {
                var remaining = new Dictionary<int, int>();
                BalloonData[] all = BalloonController.Instance.GetAllBalloons();
                if (all != null)
                {
                    for (int i = 0; i < all.Length; i++)
                    {
                        if (all[i].isPopped) continue;
                        int c = all[i].color;
                        if (remaining.ContainsKey(c)) remaining[c]++;
                        else remaining[c] = 1;
                    }
                }
                int totalRemain = 0;
                foreach (var kvp in remaining) totalRemain += kvp.Value;

                // 벨트 위 다트 색상별 로그
                var railDarts = new Dictionary<int, int>();
                if (RailManager.HasInstance)
                {
                    var rail = RailManager.Instance;
                    var occupied = rail.GetOccupiedSlots();
                    for (int i = 0; i < occupied.Count; i++)
                    {
                        int c = rail.GetSlot(occupied[i]).dartColor;
                        if (c < 0) continue;
                        if (railDarts.ContainsKey(c)) railDarts[c]++;
                        else railDarts[c] = 1;
                    }
                }

                var sb = new System.Text.StringBuilder();
                sb.Append($"[BoardState] AllHoldersEmpty — 남은 풍선 {totalRemain}개: ");
                foreach (var kvp in remaining)
                    sb.Append($"color{kvp.Key}={kvp.Value} ");
                sb.Append("| 벨트 다트: ");
                foreach (var kvp in railDarts)
                    sb.Append($"color{kvp.Key}={kvp.Value} ");
                if (railDarts.Count == 0) sb.Append("없음");
                Debug.Log(sb.ToString());
            }

            // All holders consumed. If rail also empty and balloons remain → fail
            if (RailManager.HasInstance && RailManager.Instance.OccupiedCount == 0 && _remainingBalloons > 0)
            {
                TriggerFail(FailReason.NoMovesLeft);
            }
        }

        #endregion

        #region Private Methods — Condition Evaluation

        private void EvaluateClearCondition()
        {
            if (!IsBoardClear()) return;

            int score = ScoreManager.HasInstance ? ScoreManager.Instance.CurrentScore : 0;
            int starCount = ScoreManager.HasInstance ? ScoreManager.Instance.GetStarCountForScore(score) : 0;

            _currentState = BoardState.Cleared;

            EventBus.Publish(new OnBoardCleared
            {
                levelId = _currentLevelId,
                score = score,
                starCount = starCount
            });

            Debug.Log($"[BoardStateManager] Board cleared! Level={_currentLevelId}, Score={score}, Stars={starCount}");
        }

        /// <summary>
        /// Checks if any dart color on the rail can match an outermost balloon.
        /// "Outermost" = directly visible from a rail side (no other balloon blocking).
        /// </summary>
        private bool HasOutermostMatch()
        {
            if (!RailManager.HasInstance || !BalloonController.HasInstance) return false;

            HashSet<int> railColors = RailManager.Instance.GetRailDartColors();
            if (railColors.Count == 0) return false;

            HashSet<int> outermostColors = GetOutermostBalloonColors();
            if (outermostColors.Count == 0) return false;

            // Check intersection
            foreach (int color in railColors)
            {
                if (outermostColors.Contains(color))
                    return true;
            }

            return false;
        }

        /// <summary>
        /// Returns the set of balloon colors exposed on the outermost edges
        /// (directly targetable from any rail side).
        /// Uses per-column nearest-to-rail check (IsOutermostInDirection) for all 4 directions.
        /// </summary>
        // 재사용 컬렉션 (GC 방지)
        private readonly HashSet<int> _reusableOutermostColors = new HashSet<int>();
        private readonly Dictionary<Vector2Int, int> _reusableOccupancy = new Dictionary<Vector2Int, int>();
        private readonly HashSet<Vector2Int> _reusablePositionMap = new HashSet<Vector2Int>();
        private float _cachedCellSpacing = 0.55f;

        private HashSet<int> GetOutermostBalloonColors()
        {
            _reusableOutermostColors.Clear();
            if (!BalloonController.HasInstance) return _reusableOutermostColors;

            BalloonData[] allBalloons = BalloonController.Instance.GetAllBalloons();
            if (allBalloons == null) return _reusableOutermostColors;

            if (GameManager.HasInstance)
                _cachedCellSpacing = GameManager.Instance.Board.cellSpacing;

            _reusableOccupancy.Clear();
            _reusablePositionMap.Clear();

            float cs = _cachedCellSpacing;
            foreach (var b in allBalloons)
            {
                if (b.isPopped) continue;
                Vector2Int cell = new Vector2Int(
                    Mathf.RoundToInt(b.position.x / cs),
                    Mathf.RoundToInt(b.position.z / cs));

                _reusablePositionMap.Add(cell);

                if (b.gimmickType == BalloonController.GimmickWall) continue;
                if (b.gimmickType == BalloonController.GimmickPin) continue;
                if (b.gimmickType == BalloonController.GimmickIce) continue;

                _reusableOccupancy[cell] = b.color;
            }

            foreach (var kvp in _reusableOccupancy)
            {
                Vector2Int cell = kvp.Key;

                if (IsOutermostInDirection(cell, Vector2Int.up, _reusablePositionMap) ||
                    IsOutermostInDirection(cell, Vector2Int.down, _reusablePositionMap) ||
                    IsOutermostInDirection(cell, Vector2Int.left, _reusablePositionMap) ||
                    IsOutermostInDirection(cell, Vector2Int.right, _reusablePositionMap))
                {
                    _reusableOutermostColors.Add(kvp.Value);
                }
            }

            return _reusableOutermostColors;
        }

        private bool IsOutermostInDirection(Vector2Int cell, Vector2Int direction, HashSet<Vector2Int> occupied)
        {
            // Scan from cell toward the edge (direction toward rail). If no other balloon
            // is between cell and the edge in that direction, cell is outermost.
            Vector2Int check = cell + direction;
            for (int i = 0; i < 30; i++) // max scan
            {
                if (occupied.Contains(check))
                    return false; // another balloon blocks
                check += direction;
            }
            return true; // no blocker found → outermost
        }

        private void TriggerFail(FailReason reason)
        {
            if (_currentState != BoardState.Playing) return;

            _currentState = BoardState.Failed;

            string reasonText;
            switch (reason)
            {
                case FailReason.RailOverflow:  reasonText = "RailOverflow"; break;
                case FailReason.NoMovesLeft:   reasonText = "NoMovesLeft"; break;
                default:                       reasonText = reason.ToString(); break;
            }

            EventBus.Publish(new OnBoardFailed
            {
                levelId = _currentLevelId,
                reason = reasonText
            });

            Debug.Log($"[BoardStateManager] Board failed! Level={_currentLevelId}, Reason={reasonText}");
        }

        private void HandleContinueApplied(OnContinueApplied evt)
        {
            // Resume board after continue — reset fail state so game can detect next fail/clear
            _currentState = BoardState.Playing;
            _isCritical = false;
            _criticalTimer = 0f;
            _failConfirmed = false;

            // Re-evaluate gauge based on current occupancy
            if (RailManager.HasInstance)
            {
                int occupied = RailManager.Instance.OccupiedCount;
                int total = RailManager.Instance.SlotCount;
                float ratio = total > 0 ? (float)occupied / total : 0f;
                _currentGaugeStage = EvaluateGaugeStage(ratio);
            }
            else
            {
                _currentGaugeStage = GaugeStage.Safe;
            }

            Debug.Log($"[BoardStateManager] Continue applied — board resumed. State=Playing, Gauge={_currentGaugeStage}");
        }

        private void PublishBoardStateChanged()
        {
            EventBus.Publish(new OnBoardStateChanged
            {
                remainingBalloons = _remainingBalloons
            });
        }

        #endregion
    }

    // ─────────────────────────────────────────────
    // Data Types
    // ─────────────────────────────────────────────

    public enum BoardState
    {
        Playing,
        Cleared,
        Failed
    }

    public enum FailReason
    {
        None,
        NoMovesLeft,
        RailOverflow // 레일 capacity-1개 이상 & 최외곽 매칭 불가 & 1.5s grace
    }

    public struct FailResult
    {
        public bool isFail;
        public FailReason reason;
    }
}
