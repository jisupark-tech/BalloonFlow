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
        // 명세(§5-5) fail grace = 1.5s. GameManager.Board.failGraceDelay (Inspector/data) 가 OnSingletonAwake 에서 override(권위).
        // (과거 1.5→5→3s 확장 튜닝 이력 있었으나 명세 정합으로 1.5 복귀 — recovery 시간 필요 시 GameManager.Board 값으로 조정)
        private float _failGraceDelay = 1.5f;
        // Design ref (doc line 56, 322-329): 실패 조건 = "rail dart 1+ + 외곽 매칭 불가 + 3초 grace (railFull 조건 제거)"
        // 매칭 가능하면 critical 진입 안 함. 매칭 가능해지거나 슬롯 비면 critical 즉시 해제.

        // 6-stage gauge
        private GaugeStage _currentGaugeStage = GaugeStage.Safe;

        // Rail overflow fail tracking
        private bool _isCritical;
        private float _criticalTimer;
        private bool _failConfirmed;

        /// <summary>이어하기 직후 fail 평가 일시 정지 기간 (초). player 가 행동할 시간 확보.</summary>
        private const float POST_CONTINUE_GRACE_DURATION = 3f;
        /// <summary>이어하기 grace 종료 시각 (Time.unscaledTime 기준). 0 이면 비활성.</summary>
        private float _postContinueGraceUntil;
        /// <summary>Continue restored rail space. Do not re-fail while the player has not made the next move.</summary>
        private bool _awaitingPostContinuePlayerAction;

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
            _wasForceFullBeltAdvanceActive = false;

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
            EventBus.Subscribe<OnDeadlockEntered>(HandleDeadlockEntered);
            EventBus.Subscribe<OnHolderTapped>(HandleHolderTapped);
        }

        private void OnDisable()
        {
            EventBus.Unsubscribe<OnLevelLoaded>(HandleLevelLoaded);
            EventBus.Unsubscribe<OnBalloonPopped>(HandleBalloonPopped);
            EventBus.Unsubscribe<OnBalloonSpawned>(HandleBalloonSpawned);
            EventBus.Unsubscribe<OnRailOccupancyChanged>(HandleRailOccupancy);
            EventBus.Unsubscribe<OnAllHoldersEmpty>(HandleAllHoldersEmpty);
            EventBus.Unsubscribe<OnContinueApplied>(HandleContinueApplied);
            EventBus.Unsubscribe<OnDeadlockEntered>(HandleDeadlockEntered);
            EventBus.Unsubscribe<OnHolderTapped>(HandleHolderTapped);
        }

        private float _periodicLogTimer;
        private const float PERIODIC_LOG_INTERVAL = 1.0f;

        // Stuck 평가 throttle — HasOutermostMatchCached 가 dirty 시 1620 풍선 iterate × 4방향 × 50 cells = 324K ops.
        // 매 frame 호출 spike. 0.1s 마다 1번이면 fail 감지 0.1s 지연 — 게임 디자인 영향 미미.
        private float _stuckEvalTimer;
        private const float STUCK_EVAL_INTERVAL = 0.1f;
        private const float FAIL_RECHECK_MIN_DURATION = 0.5f;
        private const float FORCE_ADVANCE_RECHECK_MIN_DURATION = 1.0f;
        private const float FORCE_ADVANCE_RECHECK_MAX_DURATION = 2.0f;

        private float EffectiveFailGraceDelay => Mathf.Max(_failGraceDelay, FAIL_RECHECK_MIN_DURATION);
        private bool _wasForceFullBeltAdvanceActive;

        // [2026-05-13] Pause 중 fail eval 정지 + 재개 시 critical 상태 reset 위한 추적.
        private bool _wasPausedLastFrame;

        private void Update()
        {
            var __sw = InGamePerfLogger.StartSection();
            try
            {
            if (_currentState != BoardState.Playing) return;
            if (_failConfirmed) return;

            // [2026-05-13] UseItem/Setting popup 등 PauseManager.IsPaused 중 fail 평가 정지.
            //   재개 시 _criticalTimer 누적되어 popup 닫자마자 fail 트리거되던 이슈 방지.
            if (PauseManager.IsPaused)
            {
                _wasPausedLastFrame = true;
                return;
            }
            if (_wasPausedLastFrame)
            {
                _wasPausedLastFrame = false;
                _isCritical = false;
                _criticalTimer = 0f;
                _stuckEvalTimer = 0f;
                _wasForceFullBeltAdvanceActive = false;
            }

            // Throttle — fail evaluation 매 frame 안 함.
            _stuckEvalTimer += Time.deltaTime;
            if (_stuckEvalTimer < STUCK_EVAL_INTERVAL) return;
            float evalDelta = _stuckEvalTimer;
            _stuckEvalTimer = 0f;

            // 이어하기 직후 grace 기간 — fail 평가 일시 정지. 이어하기 후 rail 이 여전히 stuck 일 수 있는데
            // 즉시 fail 재트리거 방지. critical 도 강제로 풀어 시각 알람도 잠시 OFF.
            if (_postContinueGraceUntil > 0f && Time.unscaledTime < _postContinueGraceUntil)
            {
                _isCritical = false;
                _criticalTimer = 0f;
                _wasForceFullBeltAdvanceActive = false;
                return;
            }

            // [2026-05-13] 실패 조건 — 임계치 (벨트 거의 가득) + 공격 불가 + 풍선 잔존.
            //  ① 레일 거의 가득 (efc >= physCap - FAIL_BUFFER) — 1발만 남은 상태에서 매칭 없다고 끝나는 이슈 차단
            //  ② 외곽 풍선 중 rail dart 로 공격 가능한 게 없음 (HasOutermostMatch = false)
            //  ③ 풍선 잔존 (clear 아님)
            // 셋 다 충족 → grace 후 fail. grace 동안 dart 발사 + 매칭 가능 시 recovery.
            // 이전 (2026-05-12): railFull 제거하고 efc > 0 으로 완화 → 1발만 있어도 trigger 되는 부작용.
            //   사용자 피드백 후 임계치 복원. FAIL_BUFFER 로 1슬롯 정도 여유 (deploy point 차단 고려).
            if (_awaitingPostContinuePlayerAction)
            {
                int continueEfc = RailManager.HasInstance ? RailManager.Instance.EffectiveOccupiedCount : 0;
                int continuePhysCap = RailManager.HasInstance ? RailManager.Instance.PhysicalCapacity : 0;
                bool railStillHasSpace = !RailManager.HasInstance || continuePhysCap <= 0 || continueEfc < continuePhysCap - 1;
                if (railStillHasSpace)
                {
                    _isCritical = false;
                    _criticalTimer = 0f;
                    _wasForceFullBeltAdvanceActive = false;
                    return;
                }

                // If restore failed to make space for any reason, fall back to the normal fail evaluator.
                _awaitingPostContinuePlayerAction = false;
            }

            int efc = RailManager.HasInstance ? RailManager.Instance.EffectiveOccupiedCount : 0;
            int physCap = RailManager.HasInstance ? RailManager.Instance.PhysicalCapacity : 0;
            const int FAIL_BUFFER = 1; // efc 가 physCap-1 도달 시 fail 평가 진입
            bool railFull = RailManager.HasInstance && physCap > 0 && efc >= physCap - FAIL_BUFFER;
            bool forceFullBeltAdvance = RailManager.HasInstance && RailManager.Instance.IsForceFullBeltAdvanceActive();
            if (forceFullBeltAdvance && !_wasForceFullBeltAdvanceActive)
            {
                // ROLLBACK_FAIL_FORCE_ADVANCE_RECHECK_TIMER:
                // Force-full-belt advance is a recovery window. Reset any previously accumulated
                // critical timer so the belt gets time to rotate and expose/fire possible matches.
                _criticalTimer = 0f;
            }
            _wasForceFullBeltAdvanceActive = forceFullBeltAdvance;
            bool hasMatch = HasOutermostMatchCached;
            // ROLLBACK_FAIL_NO_MOVES_LEFT_WITH_RAIL_DARTS:
            // Previously no-move fail only triggered when all holders were empty AND rail was empty.
            // That missed the real dead state where rail still has darts, but none of their colors
            // can attack any exposed balloon. Keep railFull for overflow, and add the no-supply path.
            bool allHoldersEmpty = HolderManager.HasInstance && HolderManager.Instance.AreAllHoldersEmpty();
            bool noMovesLeft = allHoldersEmpty && _remainingBalloons > 0 && !hasMatch;
            // [2026-05-13] 이전: bool stuck = (efc > 0) && _remainingBalloons > 0 && !hasMatch;
            // ROLLBACK_FAIL_ON_FORCE_ADVANCE_NO_MATCH:
            // Forced full-belt advance means the rail is already in recovery/full movement mode.
            // If nothing can attack while this is active, enter the same grace-based fail flow.
            bool stuck = _remainingBalloons > 0 && !hasMatch && (railFull || noMovesLeft || forceFullBeltAdvance);

            // 진단용 주기적 로그 — rail이 많이 차 있는데 stuck 미충족 시 어떤 조건이
            // 막고 있는지 출력 (false negative 케이스 분석용).
            if (_debugLogFail)
            {
                _periodicLogTimer += evalDelta;
                if (_periodicLogTimer >= PERIODIC_LOG_INTERVAL)
                {
                    _periodicLogTimer = 0f;
                    bool nearFull = physCap > 0 && efc >= physCap - 1;
                    if (nearFull)
                    {
                        Debug.Log($"[Fail-DEBUG/Periodic] efc={efc}/{physCap} railFull={railFull} forceFullBeltAdvance={forceFullBeltAdvance} allHoldersEmpty={allHoldersEmpty} noMovesLeft={noMovesLeft} balloons={_remainingBalloons} hasMatch={hasMatch} stuck={stuck} isCritical={_isCritical} timer={_criticalTimer:F2}");
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
                _wasForceFullBeltAdvanceActive = false;
                return;
            }

            // Fail evaluation is throttled, so accumulate the elapsed evaluation window
            // instead of a single frame delta. Otherwise a 2s grace can stretch far longer.
            _criticalTimer += evalDelta;
            if (_criticalTimer >= GetRequiredFailDelay(forceFullBeltAdvance))
            {
                if (_debugLogFail) DumpAttackState("[Fail-DEBUG] Fail 트리거");
                _failConfirmed = true;
                TriggerFail(noMovesLeft ? FailReason.NoMovesLeft : FailReason.RailOverflow);
            }
            }
            finally { InGamePerfLogger.EndSection(__sw, "BoardStateManager.Update"); }
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
            _postContinueGraceUntil = 0f;
            _awaitingPostContinuePlayerAction = false;

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
                if (_awaitingPostContinuePlayerAction)
                {
                    int postContinueOccupied = RailManager.Instance.EffectiveOccupiedCount;
                    int postContinueCapacity = RailManager.Instance.PhysicalCapacity;
                    if (postContinueCapacity <= 0 || postContinueOccupied < postContinueCapacity - 1)
                        return new FailResult { isFail = false, reason = FailReason.None };
                }

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

                // ROLLBACK_FAIL_ON_FORCE_ADVANCE_NO_MATCH:
                // Match Update(): forced full-belt advance with no attackable exposed color is a
                // fail candidate, even before the rail reaches capacity - 1.
                if (RailManager.Instance.IsForceFullBeltAdvanceActive() && !HasOutermostMatch())
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
                // ROLLBACK_FAIL_NO_MOVES_LEFT_WITH_RAIL_DARTS:
                // Old check required rail empty. If rail still has darts but no exposed color can be
                // hit, the board is equally unwinnable because there are no holders left to add colors.
                if (_remainingBalloons > 0 && !HasOutermostMatch())
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
            _outermostDirty = true;
            _awaitingPostContinuePlayerAction = false;
            _postContinueGraceUntil = 0f;
        }

        private void HandleHolderTapped(OnHolderTapped evt)
        {
            _awaitingPostContinuePlayerAction = false;
            _postContinueGraceUntil = 0f;
        }

        private void HandleBalloonPopped(OnBalloonPopped evt)
        {
            _outermostDirty = true;
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
            _outermostDirty = true;
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

            // Danger 알람: stall 감지와 동일한 임계치에서 ON.
            // 80% 미만 진입 시 즉시 OFF 안 함 — BoardTileManager.UpdateDangerBlink가 사이클 종료 시점에 자체 종료.
            if (BoardTileManager.HasInstance && evt.occupancy >= STALL_MIN_OCCUPANCY)
                BoardTileManager.Instance.SetDangerVisible(true);

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

        private void HandleDeadlockEntered(OnDeadlockEntered evt)
        {
            if (_currentState != BoardState.Playing) return;
            if (_failConfirmed) return;
            if (_remainingBalloons <= 0) return;
            if (!RailManager.HasInstance) return;

            // ROLLBACK_FAIL_ON_DEADLOCK_ENTER_NO_MATCH:
            // Previous behavior called HasOutermostMatch + TriggerFail immediately here. That could
            // fail on the same frame the belt entered recovery, before rail darts had an interval to
            // advance/fire. Keep the optimized 0.1s Update throttle as the single fail evaluator.
            if (!RailManager.Instance.IsForceFullBeltAdvanceActive()) return;
            EnterFailRecheckWindow("[Fail-DEBUG] Deadlock entered -> recheck window");
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
            // ROLLBACK_FAIL_NO_MOVES_LEFT_WITH_RAIL_DARTS:
            // All holders consumed. Fail when no remaining rail dart color can hit an exposed balloon,
            // even if darts are still sitting on the rail.
            if (_remainingBalloons > 0)
            {
                EnterFailRecheckWindow("[Fail-DEBUG] All holders empty -> recheck window");
            }
        }

        #endregion

        #region Private Methods — Condition Evaluation

        private void EnterFailRecheckWindow(string debugTag)
        {
            _outermostDirty = true;
            _matchCacheFrame = -1;
            _stuckEvalTimer = 0f;

            _isCritical = true;
            _criticalTimer = 0f;

            if (_debugLogFail)
                DumpAttackState(debugTag);
        }

        private float GetRequiredFailDelay(bool forceFullBeltAdvance)
        {
            float requiredDelay = EffectiveFailGraceDelay;
            if (!forceFullBeltAdvance || !RailManager.HasInstance)
                return requiredDelay;

            float beltSpeed = RailManager.Instance.GetBeltDistancePerSecond();
            if (beltSpeed <= 0.001f)
                return Mathf.Max(requiredDelay, FORCE_ADVANCE_RECHECK_MIN_DURATION);

            float requiredTravel = Mathf.Max(
                GameManager.HasInstance ? GameManager.Instance.Board.cellSpacing : 0.55f,
                RailManager.Instance.DartClusterAttackGap);
            float travelDelay = requiredTravel / beltSpeed;
            travelDelay = Mathf.Clamp(travelDelay, FORCE_ADVANCE_RECHECK_MIN_DURATION, FORCE_ADVANCE_RECHECK_MAX_DURATION);
            return Mathf.Max(requiredDelay, travelDelay);
        }

        private void EvaluateClearCondition()
        {
            if (!IsBoardClear()) return;
            PublishBoardCleared();
        }

        /// <summary>클리어 발행 공통 경로 — 정상 클리어(EvaluateClearCondition)와 치트 강제 클리어(ForceClearStage)가 공유.</summary>
        private void PublishBoardCleared()
        {
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

        /// <summary>[CHEAT] btnClear(UIHud) 전용 — IsBoardClear 게이트를 무시하고 현재 스테이지를 즉시 클리어 처리.
        /// Playing 상태일 때만 동작(이미 Cleared/Failed 면 중복 발행 방지).</summary>
        public void ForceClearStage()
        {
            if (_currentState != BoardState.Playing)
            {
                Debug.LogWarning($"[BoardStateManager] [CHEAT] ForceClearStage 무시 — 현재 상태={_currentState}");
                return;
            }
            Debug.Log("[BoardStateManager] [CHEAT] ForceClearStage 호출 — 강제 클리어");
            PublishBoardCleared();
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

            HashSet<int> outermostColors = GetRailSideOutermostBalloonColors();
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
        /// 현재 레일 면에서 '공격 가능한' 외곽 풍선 색 집합 (read-only, dirty-cached 재사용 set).
        /// DartManager 의 dead-head 릴리프(공격 불가 head 를 건너뛰고 매칭 다트 발사)에서 사용.
        /// </summary>
        public HashSet<int> GetReachableOutermostColors() => GetRailSideOutermostBalloonColors();

        /// <summary>
        /// Returns the set of balloon colors exposed on the outermost edges
        /// (directly targetable from any rail side).
        /// Uses per-column nearest-to-rail check (IsOutermostInDirection) for all 4 directions.
        /// </summary>
        // 재사용 컬렉션 (GC 방지)
        private readonly HashSet<int> _reusableOutermostColors = new HashSet<int>();
        private readonly Dictionary<Vector2Int, int> _reusableOccupancy = new Dictionary<Vector2Int, int>();
        private readonly HashSet<Vector2Int> _reusablePositionMap = new HashSet<Vector2Int>();
        private readonly Dictionary<Vector2Int, int> _reusableCellToBalloonId = new Dictionary<Vector2Int, int>(512);
        // 외곽 풍선 ID set — DirectionalTargeting candidates pre-filter 용. dirty 갱신 때 같이 채움.
        private readonly HashSet<int> _reusableOutermostBalloonIds = new HashSet<int>();
        // 사용자 요구: Sweep + boundary check 알고리즘 — row/col 별 min/max cell 계산 (O(N) sweep).
        // 이전 ray cast 50 cells 한계 / 200 cells 부하 모두 해결. 부하 100배 절감 + 큰 보드 정확.
        private readonly Dictionary<int, int> _reusableRowMinX = new Dictionary<int, int>(64);
        private readonly Dictionary<int, int> _reusableRowMaxX = new Dictionary<int, int>(64);
        private readonly Dictionary<int, int> _reusableColMinY = new Dictionary<int, int>(64);
        private readonly Dictionary<int, int> _reusableColMaxY = new Dictionary<int, int>(64);
        private readonly HashSet<int> _reusableRailSideOutermostColors = new HashSet<int>();
        private float _cachedCellSpacing = 0.55f;

        /// <summary>
        /// 외곽 색상 dirty 플래그 — 풍선 spawn/pop/level load 시 true 마킹.
        /// false 면 _reusableOutermostColors 의 직전 결과 그대로 반환 → 매 프레임 O(n×200) 재계산 회피.
        /// 풍선 N×N 격자 perf 최적화 (BoardStateManager.HasOutermostMatchCached 핫스팟 제거).
        /// </summary>
        private bool _outermostDirty = true;

        /// <summary>
        /// 외곽 색상 cache 무효화. 풍선 visibility 변경 (Hidden reveal / Ice melt / ColorCurtain off 등)
        /// 외부에서 발생 시 호출. EventBus 으로 잡지 못하는 변경에 대한 방어 hook.
        /// </summary>
        public void InvalidateOutermostCache() => _outermostDirty = true;

        private HashSet<int> GetOutermostBalloonColors()
        {
            // dirty 아니면 직전 계산 결과 그대로 반환 (매 프레임 비용 0)
            if (!_outermostDirty) return _reusableOutermostColors;
            _outermostDirty = false;

            _reusableOutermostColors.Clear();
            if (!BalloonController.HasInstance) return _reusableOutermostColors;

            BalloonData[] allBalloons = BalloonController.Instance.GetAllBalloons();
            if (allBalloons == null) return _reusableOutermostColors;

            if (GameManager.HasInstance)
                _cachedCellSpacing = GameManager.Instance.Board.cellSpacing;

            _reusableOccupancy.Clear();
            _reusablePositionMap.Clear();
            _reusableCellToBalloonId.Clear();
            _reusableOutermostBalloonIds.Clear();

            float cs = _cachedCellSpacing;
            // foreach → for index — IL2CPP 명시적 inline + array indexer 직접 사용
            for (int i = 0; i < allBalloons.Length; i++)
            {
                var b = allBalloons[i];
                if (b == null || b.isPopped) continue;
                bool targetable = true;
                if (BalloonController.Instance.IsBalloonConcealed(b.balloonId)) targetable = false;
                if (b.gimmickType == BalloonController.GimmickWall) targetable = false;
                if (b.gimmickType == BalloonController.GimmickIce) targetable = false;
                if (b.gimmickType == BalloonController.GimmickColorCurtain) targetable = false;

                // ROLLBACK_BARRICADE_MULTI_CELL_OCCUPANCY:
                // Keep outermost/match checks aligned with DirectionalTargeting's multi-cell sized gimmicks.
                if (BalloonController.IsSizedFieldGimmick(b.gimmickType) && (b.sizeW > 1 || b.sizeH > 1))
                {
                    int width = Mathf.Max(1, b.sizeW);
                    int height = Mathf.Max(1, b.sizeH);
                    // Target Box 알 모델: footprint 셀에 egg 색 분배(modulo N) — N(명시 egg 수)은 W*H 와 무관.
                    bool isEggBox = b.gimmickType == BalloonController.GimmickPinataBox
                        && b.eggColors != null && b.eggColors.Length > 0;
                    int eggN = isEggBox ? b.eggColors.Length : 0;
                    Vector3 anchor = BalloonController.Instance.GetAdjustedBoardPosition(b.position);
                    BalloonController.Instance.GetAdjustedCellSize(out float cellSizeX, out float cellSizeZ);
                    for (int dx = 0; dx < width; dx++)
                    {
                        for (int dz = 0; dz < height; dz++)
                        {
                            Vector2Int occupiedCell = new Vector2Int(
                                Mathf.RoundToInt((anchor.x + dx * cellSizeX) / cs),
                                Mathf.RoundToInt((anchor.z + dz * cellSizeZ) / cs));
                            _reusablePositionMap.Add(occupiedCell);

                            int cellColor = b.color;
                            bool cellTargetable = targetable;
                            if (isEggBox)
                            {
                                int eggIdx = (dz * width + dx) % eggN;
                                cellColor = b.eggColors[eggIdx];
                                bool eggAlive = b.eggHps != null && eggIdx < b.eggHps.Length && b.eggHps[eggIdx] > 0;
                                cellTargetable = targetable && eggAlive;
                            }
                            if (cellTargetable)
                            {
                                _reusableOccupancy[occupiedCell] = cellColor;
                                _reusableCellToBalloonId[occupiedCell] = b.balloonId;
                            }
                        }
                    }
                    continue;
                }
                // FindTarget과 동일하게 GetBalloonWorldPosition 사용 — LevelSafeMult 적용된 실제 위치.
                Vector3 worldPos = BalloonController.Instance.GetBalloonWorldPosition(b.balloonId);
                Vector2Int cell = new Vector2Int(
                    Mathf.RoundToInt(worldPos.x / cs),
                    Mathf.RoundToInt(worldPos.z / cs));

                _reusablePositionMap.Add(cell);

                // 직접 타격 불가 타입 제외 (DirectionalTargeting.FindTarget과 정합):
                //   Wall: 파괴 불가 / Ice: 인접 pop으로 간접 해동 / ColorCurtain: 간접 제거만
                // Pin은 doc line 222 + FindTarget(line 117): 같은 색 다트로 직접 타격 가능 → 포함.
                if (!targetable) continue;

                _reusableOccupancy[cell] = b.color;
                _reusableCellToBalloonId[cell] = b.balloonId;
            }

            // 사용자 요구: Sweep + boundary check — ray cast 50 cells 한계 + 200 cells 부하 둘 다 해결.
            // O(N) sweep 으로 row/col 별 min/max 계산 → per-cell O(1) boundary check.
            _reusableRowMinX.Clear();
            _reusableRowMaxX.Clear();
            _reusableColMinY.Clear();
            _reusableColMaxY.Clear();
            var posEn = _reusablePositionMap.GetEnumerator();
            try
            {
                while (posEn.MoveNext())
                {
                    Vector2Int c = posEn.Current;
                    if (!_reusableRowMinX.TryGetValue(c.y, out int rxMin) || c.x < rxMin) _reusableRowMinX[c.y] = c.x;
                    if (!_reusableRowMaxX.TryGetValue(c.y, out int rxMax) || c.x > rxMax) _reusableRowMaxX[c.y] = c.x;
                    if (!_reusableColMinY.TryGetValue(c.x, out int cyMin) || c.y < cyMin) _reusableColMinY[c.x] = c.y;
                    if (!_reusableColMaxY.TryGetValue(c.x, out int cyMax) || c.y > cyMax) _reusableColMaxY[c.x] = c.y;
                }
            }
            finally { posEn.Dispose(); }

            // 외곽 cell 식별 — boundary 4가지 중 하나라도 만족하면 외곽.
            var occEn = _reusableOccupancy.GetEnumerator();
            try
            {
                while (occEn.MoveNext())
                {
                    Vector2Int cell = occEn.Current.Key;
                    bool isOuter =
                        (_reusableRowMinX.TryGetValue(cell.y, out int rxMin) && cell.x == rxMin)
                        || (_reusableRowMaxX.TryGetValue(cell.y, out int rxMax) && cell.x == rxMax)
                        || (_reusableColMinY.TryGetValue(cell.x, out int cyMin) && cell.y == cyMin)
                        || (_reusableColMaxY.TryGetValue(cell.x, out int cyMax) && cell.y == cyMax);

                    if (isOuter)
                    {
                        _reusableOutermostColors.Add(occEn.Current.Value);
                        if (_reusableCellToBalloonId.TryGetValue(cell, out int bid))
                            _reusableOutermostBalloonIds.Add(bid);
                    }
                }
            }
            finally { occEn.Dispose(); }

            return _reusableOutermostColors;
        }

        // RAIL_SIDE_COLOR_CACHE (등급2 perf 2026-06-11):
        // 이전엔 매 호출(HasOutermostMatchCached 경유 매 프레임)마다 side 필터를 재계산했다.
        // 입력은 ① 외곽 boundary/occupancy(_outermostDirty 로만 변함) ② sideCount(레벨 중 사실상 고정) 뿐이므로
        // 둘 다 변하지 않은 호출은 직전 결과를 그대로 반환한다 (입력 동일 → 출력 동일).
        // 무효화는 별도 플래그 없이 '진입 시점의 _outermostDirty 관찰'로 기존 dirty 마킹 지점과 자동 동기화 —
        // 새 invalidate 지점을 빠뜨릴 수 없는 구조.
        private bool _railSideColorsValid;
        private int _railSideCachedSideCount = int.MinValue;

        private HashSet<int> GetRailSideOutermostBalloonColors()
        {
            bool outermostDirtyAtEntry = _outermostDirty;
            GetOutermostBalloonColors(); // boundary/occupancy 최신화 (+_outermostDirty 해제)

            int sideCount = RailManager.HasInstance
                ? RailManager.GetRailSideCount(RailManager.Instance.PhysicalCapacity)
                : -1; // RailManager 부재도 캐시 키에 포함 (부재 시 결과 = 빈 집합)

            if (!outermostDirtyAtEntry && _railSideColorsValid && sideCount == _railSideCachedSideCount)
            {
#if UNITY_EDITOR
                ValidateRailSideColorsCache(sideCount);
#endif
                return _reusableRailSideOutermostColors;
            }

            RebuildRailSideColors(sideCount, _reusableRailSideOutermostColors);
            _railSideColorsValid = true;
            _railSideCachedSideCount = sideCount;
            return _reusableRailSideOutermostColors;
        }

        /// <summary>side 필터 본체 — 기존 로직 그대로, 출력 set 만 파라미터화 (검증 어서트와 공유).</summary>
        private void RebuildRailSideColors(int sideCount, HashSet<int> output)
        {
            output.Clear();

            if (_reusableOutermostColors.Count == 0)
                return;

            if (sideCount < 0) // RailManager 부재
                return;

            // ROLLBACK_FAIL_RAIL_SIDE_MATCH:
            // Fail matching must use the same rail sides that can actually fire.
            // 1: bottom, 2: bottom+right, 3: bottom+right+top, 4: all sides.
            if (sideCount >= 1) AddColumnBoundaryColors(_reusableColMinY, output);
            if (sideCount >= 2) AddRowBoundaryColors(_reusableRowMaxX, output);
            if (sideCount >= 3) AddColumnBoundaryColors(_reusableColMaxY, output);
            if (sideCount >= 4) AddRowBoundaryColors(_reusableRowMinX, output);
        }

#if UNITY_EDITOR
        // RAIL_SIDE_COLOR_CACHE 검증 (에디터 전용·한시 운용): 캐시 적중 시 주기적으로 fresh 재계산과 비교.
        // 불일치 = invalidate 누락 → 놓침/데드락으로 직결되므로 즉시 에러 로그. 검증 기간 후 블록 제거 가능.
        private static readonly HashSet<int> _railSideValidationSet = new HashSet<int>();
        private void ValidateRailSideColorsCache(int sideCount)
        {
            if (Time.frameCount % 30 != 0) return;
            RebuildRailSideColors(sideCount, _railSideValidationSet);
            if (!_railSideValidationSet.SetEquals(_reusableRailSideOutermostColors))
            {
                Debug.LogError(
                    $"[BoardStateManager] RailSide color cache 불일치 — invalidate 누락 의심! " +
                    $"cached=[{string.Join(",", _reusableRailSideOutermostColors)}] " +
                    $"fresh=[{string.Join(",", _railSideValidationSet)}] sideCount={sideCount}");
                // 안전 복구: fresh 결과로 교체.
                _reusableRailSideOutermostColors.Clear();
                foreach (int c in _railSideValidationSet) _reusableRailSideOutermostColors.Add(c);
            }
        }
#endif

        private void AddColumnBoundaryColors(Dictionary<int, int> colToY, HashSet<int> output)
        {
            var en = colToY.GetEnumerator();
            try
            {
                while (en.MoveNext())
                {
                    Vector2Int cell = new Vector2Int(en.Current.Key, en.Current.Value);
                    if (_reusableOccupancy.TryGetValue(cell, out int color))
                        output.Add(color);
                }
            }
            finally { en.Dispose(); }
        }

        private void AddRowBoundaryColors(Dictionary<int, int> rowToX, HashSet<int> output)
        {
            var en = rowToX.GetEnumerator();
            try
            {
                while (en.MoveNext())
                {
                    Vector2Int cell = new Vector2Int(en.Current.Value, en.Current.Key);
                    if (_reusableOccupancy.TryGetValue(cell, out int color))
                        output.Add(color);
                }
            }
            finally { en.Dispose(); }
        }

        /// <summary>풍선이 외곽인지 (4방향 중 하나라도 rail 까지 비어있는지). dirty 자동 갱신.
        /// DirectionalTargeting.FindTarget candidates pre-filter 용 — 외곽 아닌 풍선은 어차피 hit 불가능.
        /// dirty=false 면 캐시 즉시 반환 (HashSet.Contains O(1)).</summary>
        public bool IsOutermost(int balloonId)
        {
            // dirty 면 GetOutermostBalloonColors 호출이 _reusableOutermostBalloonIds 도 함께 갱신.
            GetOutermostBalloonColors();
            return _reusableOutermostBalloonIds.Contains(balloonId);
        }

        private bool IsOutermostInDirection(Vector2Int cell, Vector2Int direction, HashSet<Vector2Int> occupied)
        {
            // Scan from cell toward the edge. 200 → 50 원복 (프레임 드랍 회피).
            // 큰 보드 외곽 인식 누락 issue 는 별도 알고리즘 (sweep 기반 boundary) 으로 fix 권장.
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

            // 이어하기 후 grace 시작 — rail 이 여전히 stuck 이어도 일정 시간 fail 평가 멈춤
            _awaitingPostContinuePlayerAction = true;
            _postContinueGraceUntil = Time.unscaledTime + POST_CONTINUE_GRACE_DURATION;

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
