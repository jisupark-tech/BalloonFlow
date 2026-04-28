using System.Collections;
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
        Critical,    // 95% ~ capacity-1
        Fail         // capacity (rail full, deploy point 포함) + no outermost match + 2s grace
    }

    /// <summary>
    /// Tracks overall board state and owns clear/fail condition evaluation.
    /// Rail Overflow mode: 6-stage gauge (SAFE→CAUTION→NORMAL_HIGH→WARNING→CRITICAL→FAIL).
    /// Fail = rail full (capacity, deploy point 포함) + no outermost balloon match + 2s grace delay expires.
    /// </summary>
    /// <remarks>
    /// Layer: Domain | Genre: Puzzle | Role: Manager | Phase: 1
    /// DB Reference: Generated from Rail Overflow spec — occupancy-based fail conditions
    /// </remarks>
    public class BoardStateManager : SceneSingleton<BoardStateManager>
    {
        #region Constants — Gauge Thresholds

        // Occupancy ratio thresholds for each gauge stage
        // 명세: SAFE 0~50%, CAUTION 50~80%, NORMAL_HIGH 80~90%, WARNING 90%~허용량-2, CRITICAL 허용량-1+
        private const float THRESHOLD_CAUTION     = 0.50f;
        private const float THRESHOLD_NORMAL_HIGH = 0.80f;
        private const float THRESHOLD_WARNING     = 0.90f;
        private const float THRESHOLD_CRITICAL    = 0.95f; // 실제 fail은 레일 가득(capacity, deploy point 포함) 정수 비교

        #endregion

        #region Fields

        private BoardState _currentState;
        private int _remainingBalloons;
        private int _currentLevelId;
        private float _failGraceDelay = 1.5f;
        // Design ref (doc line 56, 322-329): 실패 조건 = "레일 다트 수 ≥ 허용량-1 + 외곽 매칭 불가 + 1.5초 grace"
        // 매칭 가능하면 critical 진입 안 함. 매칭 가능해지거나 슬롯 비면 critical 즉시 해제.

        // 6-stage gauge
        private GaugeStage _currentGaugeStage = GaugeStage.Safe;

        // Rail overflow fail tracking
        private bool _isCritical;
        private float _criticalTimer;
        private bool _failConfirmed;

        // Danger 시각 경고 임계 (점유율 80%+에서 보드 위험 표시)
        // 단일 실패 경로는 '레일 가득 + 공격 불가 2초 grace' — stall 검출은 제거됨
        private const float STALL_MIN_OCCUPANCY = 0.8f;

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

        private float _periodicLogTimer;
        private const float PERIODIC_LOG_INTERVAL = 1.0f;

        private void Update()
        {
            if (_currentState != BoardState.Playing) return;
            if (_failConfirmed) return;

            // 실패 조건 (사용자 정의):
            //  ① rail 가득 (EFC >= PhysicalCapacity - 1, gap-search 운용 상 rail이
            //     PC-1 ↔ PC 를 오가는 점 고려)
            //  ② 외곽 풍선 중 rail dart 로 공격 가능한 게 없음 (HasOutermostMatch = false)
            //  ③ 풍선 잔존 (clear 아님)
            // 셋 다 충족 → 1.5초 grace 후 fail 트리거. 도중 매칭 가능해지면 즉시 회복.
            int efc = RailManager.HasInstance ? RailManager.Instance.EffectiveOccupiedCount : 0;
            int physCap = RailManager.HasInstance ? RailManager.Instance.PhysicalCapacity : 0;
            bool railFull = RailManager.HasInstance && efc >= physCap - 1;
            bool hasMatch = HasOutermostMatchCached;
            bool stuck = railFull && _remainingBalloons > 0 && !hasMatch;

            // 진단용 주기적 로그 — rail이 많이 차 있는데 stuck 미충족 시 어떤 조건이
            // 막고 있는지 출력 (false negative 케이스 분석용).
            if (_debugLogFail)
            {
                _periodicLogTimer += Time.deltaTime;
                if (_periodicLogTimer >= PERIODIC_LOG_INTERVAL)
                {
                    _periodicLogTimer = 0f;
                    bool nearFull = physCap > 0 && efc >= physCap - 1;
                    if (nearFull)
                    {
                        Debug.Log($"[Fail-DEBUG/Periodic] efc={efc}/{physCap} railFull={railFull} balloons={_remainingBalloons} hasMatch={hasMatch} stuck={stuck} isCritical={_isCritical} timer={_criticalTimer:F2}");
                        if (!stuck && nearFull)
                        {
                            DumpAttackState("[Fail-DEBUG/Periodic] stuck=false 상세");
                        }
                    }
                }
            }

            if (!_isCritical)
            {
                if (stuck)
                {
                    _isCritical = true;
                    _criticalTimer = 0f;
                    if (_debugLogFail) DumpAttackState("[Fail-DEBUG] Critical 진입");
                }
                return;
            }

            // Recovery: 매칭 가능해짐 (다트 발사로 외곽 변경 / 풍선 pop / rail 비어짐)
            if (!stuck)
            {
                if (_debugLogFail) DumpAttackState("[Fail-DEBUG] Critical 회복");
                _isCritical = false;
                _criticalTimer = 0f;
                return;
            }

            _criticalTimer += Time.deltaTime;
            if (_criticalTimer >= _failGraceDelay)
            {
                if (_debugLogFail) DumpAttackState("[Fail-DEBUG] Fail 트리거");
                _failConfirmed = true;
                TriggerFail(FailReason.RailOverflow);
            }
        }

        /// <summary>디버그 로그 활성 토글 — Inspector 에서 ON 가능. 기본 OFF (성능 영향 회피).</summary>
        [SerializeField] private bool _debugLogFail = false;

        /// <summary>
        /// 현재 공격 가능성 상태를 콘솔에 덤프. 외부 디버그 버튼/단축키에서도 호출 가능.
        /// 출력: rail 다트 색상, 외곽 풍선 색상, 매칭 색상, holder 색상, 물리 점유율.
        /// </summary>
        public void DumpAttackState(string tag = "[Fail-DEBUG]")
        {
            string railStr = "n/a", outerStr = "n/a", matchStr = "n/a", holderStr = "n/a", occStr = "n/a";

            if (RailManager.HasInstance)
            {
                var railColors = RailManager.Instance.GetRailDartColors();
                railStr = railColors.Count == 0 ? "(empty)" : string.Join(",", railColors);
                int efc = RailManager.Instance.EffectiveOccupiedCount;
                int pc = RailManager.Instance.PhysicalCapacity;
                occStr = $"{efc}/{pc} (PC-1 임계 {(efc >= pc - 1 ? "도달" : "미달")})";
            }

            if (BalloonController.HasInstance)
            {
                var outer = GetOutermostBalloonColors();
                outerStr = outer.Count == 0 ? "(empty — 외곽 풍선 없음/walls only)" : string.Join(",", outer);

                if (RailManager.HasInstance)
                {
                    // color-set 교집합 — 실제 매칭 로직과 동일
                    var matched = new HashSet<int>();
                    var railColors = RailManager.Instance.GetRailDartColors();
                    foreach (int c in railColors)
                        if (outer.Contains(c)) matched.Add(c);
                    matchStr = matched.Count == 0
                        ? "(매칭 없음 — 공격 불가)"
                        : string.Join(",", matched);
                }
            }

            if (HolderManager.HasInstance)
            {
                var holders = HolderManager.Instance.GetHolders();
                var alive = new List<string>();
                for (int i = 0; i < holders.Length; i++)
                {
                    var h = holders[i];
                    if (h == null || h.isConsumed) continue;
                    alive.Add($"c{h.color}x{h.magazineCount}{(h.isLocked ? "[L]" : "")}{(h.isFrozen ? "[F]" : "")}{(h.isHidden ? "[H]" : "")}{(h.spawnerHP > 0 ? $"[Sp{h.spawnerHP}]" : "")}");
                }
                holderStr = alive.Count == 0 ? "(empty)" : string.Join(" ", alive);
            }

            Debug.Log($"{tag} state={_currentState} balloons={_remainingBalloons} occ={occStr}\n" +
                      $"  rail colors=[{railStr}]\n" +
                      $"  outermost colors=[{outerStr}]\n" +
                      $"  → matched=[{matchStr}]\n" +
                      $"  holders=[{holderStr}]");
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
            // Snapshot 평가 (doc spec line 56): capacity-1 도달 + 매칭 가능 풍선 없음.
            // 실제 실패 트리거는 Update 루프의 1.5s grace timer.
            // PhysicalCapacity 기준 (SlotCount > PhysicalCapacity 케이스 대응).
            if (RailManager.HasInstance)
            {
                int occupied = RailManager.Instance.EffectiveOccupiedCount;
                int capacity = RailManager.Instance.PhysicalCapacity;
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

            // Recovery: pop으로 외곽 풍선 노출 변경 → HasOutermostMatch 재검사
            // (Update 루프의 HasOutermostMatch 체크가 다음 frame에서 critical 해제 처리)
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

            // Danger 알람: stall 감지와 동일한 임계치에서 표시
            if (BoardTileManager.HasInstance)
                BoardTileManager.Instance.SetDangerVisible(evt.occupancy >= STALL_MIN_OCCUPANCY);

            // OnRailCritical 시각 알람용 이벤트 (rail 가득 + 매칭 불가 시).
            // _isCritical / 타이머는 Update 가 일괄 관리 (rail 가득 여부 무관하게 매칭 불가 검출).
            int physCap = RailManager.HasInstance ? RailManager.Instance.PhysicalCapacity : evt.totalSlots;
            bool atFailThreshold = evt.activeDarts >= physCap - 1;

            if (atFailThreshold)
            {
                EventBus.Publish(new OnRailCritical
                {
                    occupancy = evt.occupancy,
                    hasOutermostMatch = HasOutermostMatchCached
                });
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
            // Fail 여부와 관계없이 occupancy 기반 gauge stage 반환
            // (Danger 알람 등 Warning/Critical 연출이 Fail 전에도 보여야 함)
            // Fail 판정은 HandleRailOccupancy에서 별도 처리
            return EvaluateGaugeStage(occupancy);
        }

        private void HandleAllHoldersEmpty(OnAllHoldersEmpty evt)
        {
            if (_currentState != BoardState.Playing) return;

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

        // 프레임 캐싱: HasOutermostMatch는 비용이 있어 매 프레임 다중 호출 회피.
        private int _matchCacheFrame = -1;
        private bool _cachedMatchResult;

        /// <summary>
        /// 레일 위 다트 색상이 외곽 풍선 색상과 교집합 있는지 (현재 프레임 캐싱).
        /// RailManager.Update가 belt 회전 결정에 사용 → 매 프레임 호출됨.
        /// </summary>
        public bool HasOutermostMatchCached
        {
            get
            {
                if (_matchCacheFrame != Time.frameCount)
                {
                    _cachedMatchResult = HasOutermostMatch();
                    _matchCacheFrame = Time.frameCount;
                }
                return _cachedMatchResult;
            }
        }

        /// <summary>
        /// 공격 가능 여부 검사 — rail dart 색상 ∩ outermost ≠ ∅.
        /// 사용자 spec: "최외각에 공격가능한 풍선이 없으면 게임 종료" →
        /// rail dart 가 즉시 발사 가능한 매칭만 검사. holder magazine 색상은 user가 누르기
        /// 전엔 발사 못 하므로 매칭 검사에 포함하지 않음.
        /// </summary>
        private bool HasOutermostMatch()
        {
            if (!RailManager.HasInstance || !BalloonController.HasInstance) return false;

            HashSet<int> outermostColors = GetOutermostBalloonColors();
            if (outermostColors.Count == 0) return false;

            HashSet<int> railColors = RailManager.Instance.GetRailDartColors();
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
                // FindTarget과 동일하게 GetBalloonWorldPosition 사용 — LevelSafeMult 적용된 실제 위치.
                // b.position 은 raw 데이터 좌표라 큰 레벨에서 cell mapping 결과가 FindTarget과 달라짐.
                Vector3 worldPos = BalloonController.Instance.GetBalloonWorldPosition(b.balloonId);
                Vector2Int cell = new Vector2Int(
                    Mathf.RoundToInt(worldPos.x / cs),
                    Mathf.RoundToInt(worldPos.z / cs));

                _reusablePositionMap.Add(cell);

                // 직접 타격 불가 타입 제외 (DirectionalTargeting.FindTarget과 정합):
                //   Wall: 파괴 불가 / Ice: 인접 pop으로 간접 해동 / ColorCurtain: 간접 제거만
                // Pin은 doc line 222 + FindTarget(line 117): 같은 색 다트로 직접 타격 가능 → 포함.
                if (b.gimmickType == BalloonController.GimmickWall) continue;
                if (b.gimmickType == BalloonController.GimmickIce) continue;
                if (b.gimmickType == BalloonController.GimmickColorCurtain) continue;

                _reusableOccupancy[cell] = b.color;
            }

            // 4방향 무조건 검사 (baseline 50c9574 복원).
            // RailSideCount 기반 방향 제한은 BoardTileManager 초기화 타이밍 / RailSideCount
            // 미설정 케이스에서 outermost 빈 set 반환 → false fail 발생. spec 의 "외곽 매칭 가능"
            // 정의는 어느 방향이든 도달 가능한 풍선 → 4방향 모두 검사가 안전.
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
            // 50셀 = 약 28 world units (cellSpacing 0.55 기준). 일반 레벨 풍선 그리드 너비
            // 충분히 커버. 200셀은 매 프레임 비용 높아 제거 (큰 레벨 lag 원인).
            Vector2Int check = cell + direction;
            for (int i = 0; i < 50; i++)
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

            // GameManager.Board.disableFail: 동적 토글로 모든 실패 트리거 차단 (경고 UI는 유지)
            if (GameManager.HasInstance && GameManager.Instance.Board.disableFail)
            {
                _isCritical = false;
                _criticalTimer = 0f;
                _failConfirmed = false;
                return;
            }

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
            // 풍선 제거 연출 → 완료 후 게임 재개
            _isCritical = false;
            _criticalTimer = 0f;
            _failConfirmed = false;

            if (evt.removedColor >= 0 && evt.dartsRemoved > 0)
            {
                // 풍선 제거 연출이 끝난 후 게임 시작
                StartCoroutine(ContinuePopThenResume(evt.removedColor, evt.dartsRemoved));
            }
            else
            {
                // 제거할 풍선 없으면 즉시 재개
                ResumeAfterContinue();
            }
        }

        private IEnumerator ContinuePopThenResume(int color, int count)
        {
            // 1프레임 대기 (팝업 닫히고 게임 화면 전환 완료)
            yield return null;

            // 풍선 제거 연출
            if (BalloonController.HasInstance)
            {
                var balloons = BalloonController.Instance.GetAllBalloonsByColor(color);
                if (balloons != null)
                {
                    int removed = 0;
                    for (int i = 0; i < balloons.Length && removed < count; i++)
                    {
                        if (!balloons[i].isPopped)
                        {
                            BalloonController.Instance.ForcePopBalloon(balloons[i].balloonId);
                            removed++;
                        }
                    }
                    if (removed > 0)
                        Debug.Log($"[BoardStateManager] Continue: removed {removed} balloons of color {color}.");
                }
            }

            // 연출 완료 대기 (ReturnBalloonObject 애니메이션: 0.12 + 0.15 = 0.27초)
            yield return new WaitForSeconds(0.35f);

            // 게임 재개
            ResumeAfterContinue();
        }

        private void ResumeAfterContinue()
        {
            _currentState = BoardState.Playing;

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

            Debug.Log($"[BoardStateManager] Continue — balloons removed, board resumed. Gauge={_currentGaugeStage}");
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
        RailOverflow // 레일 가득 (capacity, deploy point 포함) & 최외곽 매칭 불가 & 2s grace
    }

    public struct FailResult
    {
        public bool isFail;
        public FailReason reason;
    }
}
