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

        // ROLLBACK_NO_DRAINAGE_FAIL_WATCHDOG_20260622: 무진전 fail 워치독.
        //   레벨 색 불균형(어떤 색 다트>풍선)으로 surplus dead-dart 가 레일을 영구 점유하면, 레일은 만석인데
        //   HasOutermostMatch 는 '살아있는 다른 색'이 있어 계속 true → stuck 미충족 → fail 영영 미확정 → 영구 freeze.
        //   이 워치독은 hasMatch 와 무관하게 "레일 만석 + 일정 시간 pop(=배수) 0" 이면 RailOverflow fail 확정.
        //   pop 이 한 번이라도 나면(=배수 진행) 리셋되므로 정상/느린-진행 플레이는 오발동 X.
        private float _lastDrainUnscaledTime;
        // ROLLBACK_WATCHDOG_FAST_20260707: 10f/12f → 4f/5f (사용자 지정 — 실패 체감 9초→4초 목표).
        //   근거: 필패 확정은 이제 SUPPLY_MATCH 상태 규칙이 grace 1.5s 로 즉시 처리하므로, 워치독은
        //   '모델상 매칭이 있다는데 N초간 pop/발사가 전혀 없는' 잔여 케이스(오탐/frozen/지오메트리) 전용.
        //   주의: 매칭 다트가 벨트를 길게 돌아 발사까지 4s+ 걸리는 대형 레일에서 오발동이 관찰되면
        //   6f/7f 로 상향 (원값 10f/12f = 벨트 1회전+grace 기준). 롤백: 10f/12f 복원.
        private const float NO_DRAINAGE_FAIL_SECONDS = 4f; // 만석 무배수 지속 한계
        // ROLLBACK_NO_FIRE_FAIL_WATCHDOG_20260622: 만석/강제회전인데 '레일 발사(배수)'가 이 시간 동안 0 이면 fail.
        //   no-drainage(pop 기준)가 다른 색 pop 에 starve 되는 H1 을 fire 기준으로 차단. 느린 belt 의 정상 발사간격보다 충분히 큼.
        private const float NO_FIRE_FAIL_SECONDS = 5f; // ROLLBACK_WATCHDOG_FAST_20260707: 12f→5f (위 주석 참조)
        // ROLLBACK_WATCHDOG_GATE_DWELL_20260707: 두 워치독(no-drainage/no-fire)의 '만석 게이트 체류 시간' 요구.
        //   기존엔 '마지막 pop/발사 이후 경과'만 봐서, 만석이 되기 '전'에 쌓인 무-pop 시간이 그대로 인정 →
        //   매칭 색 다트가 배포로 올라가는 중 레일이 (railFull||forceFullBeltAdvance) 에 닿는 순간 유예 0초로
        //   즉시 fail 하던 버그. '게이트 열린 상태로 N초 지속' 을 AND 조건으로 추가해 원 의도만 남긴다.
        // rev2 (누적식, 2026-07-07): rev1 은 게이트 false→true 전이 시각(edge stamp) 기준이라, 데드락 무진행
        //   exit→재진입 사이클(ROLLBACK_DEADLOCK_NO_PROGRESS_EXIT_20260706)로 forceFullBeltAdvance 가 깜빡이면
        //   체류시간이 매번 0 리셋 → below-full 잼(railFull=false, 데드락 밴드)에서 워치독이 영영 발동 못 해
        //   '영영 실패 안 함 + 벨트 무한 회전'(Level 38 영상 재현). → 열림 동안 evalDelta 를 누적하고,
        //   닫힘이 WATCHDOG_GATE_CLOSE_RESET_SECONDS 이상 '지속'될 때만 리셋해 깜빡임에 면역.
        //   롤백: 이 3개 필드 + Update 의 gate 추적 블록 + 두 워치독의 watchdogGateDwell 조건 제거.
        private float _watchdogGateOpenAccum;
        private float _watchdogGateClosedSince = float.NaN;
        private const float WATCHDOG_GATE_CLOSE_RESET_SECONDS = 2f;

        /// <summary>ROLLBACK_FAIL_NEARFULL_TUNE3_20260708: 실패용 near-full 임계 — 빈 슬롯 N개 이하 = 실패권.
        /// 데드락 강제회전 임계(RailManager.NearFullBandEmptySlots, 5~8)와 별도의 튜닝 상수.
        /// 160캡 기준 157부터 실패권. 하한 주의: 1로 내리면 Level 167 반례(158/160 무한대기) 재발.</summary>
        private const int FAIL_NEARFULL_EMPTY_SLOTS = 3;

        // ROLLBACK_SUPPLY_MATCH_FAIL_20260707: 상태 기반 실패용 필드 —
        //   _forceAdvanceInactiveSince: force 상승에지 criticalTimer 리셋의 히스테리시스 기준
        //     (연속 비활성 ≥ WATCHDOG_GATE_CLOSE_RESET_SECONDS 후의 상승만 '진짜 회복 윈도우'로 인정).
        //   _reusableSupplyColors: HolderManager.CollectSupplyColors 결과 재사용 버퍼 (GC 방지).
        private float _forceAdvanceInactiveSince = float.NaN;
        private readonly HashSet<int> _reusableSupplyColors = new HashSet<int>();

        // ROLLBACK_RAIL_FREEZE_DIAG_20260622: hard-freeze(전면정지) 진단 워치독 — fail 이 아니라 '디버그 덤프'.
        //   재현 불가한 완전정지(다트·레일·배포 모두 멈춤)의 원인을 로그로 포착하기 위함. 동작은 바꾸지 않음(no recovery).
        //   판정: 벨트 회전오프셋(RailManager.RotationOffset)과 점유수(efc)가 FREEZE_DEBUG_SECONDS 동안 전혀 변하지 않고,
        //   in-flight 다트의 pop(=_lastDrainUnscaledTime) 도 없으면 → '움직임 0' 으로 보고 1회 전체 상태 덤프.
        //   (벨트가 돌지만 발사만 안 되는 부류는 belt offset 이 계속 변하므로 여기 안 걸림 — 그건 기존 no-drainage/relief 담당.)
        //   움직임 재개 시 재무장. 1순위 용의자는 IsPausedByBooster 가 부스터 await 중 영구 true 인 케이스.
        //   롤백: 이 5개 필드 + Update 의 freeze 블록 + DumpFreezeState + RailManager.GetFreezeDiagnostics +
        //         BoosterExecutor.GetDebugState 삭제.
        [SerializeField] private bool _debugLogFreeze = true;
        private const float FREEZE_DEBUG_SECONDS = 3f;
        private float _freezeLastActivityTime;
        private float _freezeLastBeltOffset = float.NaN;
        private int _freezeLastEfc = -1;
        private bool _freezeDumpedThisStall;

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
        private const float FORCE_ADVANCE_RECHECK_MIN_DURATION = 1.0f; // ROLLBACK_SUPPLY_FAIL_FAST_GRACE_20260707: 미사용(롤백용 유지)
        private const float FORCE_ADVANCE_RECHECK_MAX_DURATION = 2.0f; // ROLLBACK_SUPPLY_FAIL_FAST_GRACE_20260707: 미사용(롤백용 유지)
        // ROLLBACK_SUPPLY_FAIL_FAST_GRACE_20260707: 확정 신호(supply 규칙) 기반 fail 의 grace 상한 = 명세 1.5s.
        private const float FAIL_GRACE_CAP_SECONDS = 1.5f;
        // ROLLBACK_SUPPLY_ACTIONABLE_20260707: '배포 진행 중' 판정 창 — 최근 이 시간 내 레일 placement 가
        //   있으면 stuck 을 억제(빈 슬롯 존재 시). 앞줄 공급으로 조인 대신, 배포가 진행되면 앞줄이 곧
        //   갱신되므로(완료→큐 전진→새 앞줄) 진행 중 오판 fail 을 막는 균형추. 자세한 근거는 stuck 주석.
        private const float PLACEMENT_PROGRESS_QUIET_SECONDS = 1.5f;

        // ROLLBACK_SUPPLY_FAIL_ENGAGE_GATE_20260707: '보드 활동 게이트' — 상태기반 supply-match fail 은 보드가 실제로
        //   시작(첫 다트가 레일에 배치되거나 첫 풍선 pop)한 뒤에만 허용한다. 배경: supply-match stuck 규칙은 레일 점유
        //   게이트가 없어, 전판 클리어 후 넘어온 '신선한(빈 레일) 스테이지'를 가만히 둘 때, auto-deploy 램프업이 첫 배치를
        //   내기 전/초반 gap 에서 supplyMatch=false && deployProgress=false 로 오탐 실패했다(2026-07-07 회귀). 활동 게이트로
        //   '아직 시작도 안 한 보드'는 절대 supply-fail 안 나게 못박는다(auto-deploy 타이밍과 무관). 첫 배치/첫 pop 후엔
        //   기존 판정 그대로 — 진짜 잼/엔드게임 실패는 유지. 레벨마다 InitializeBoard 에서 false 리셋.
        //   롤백: 이 필드 + stuck 의 '&& _boardEngagedThisLevel' + Update/HandleBalloonPopped/InitializeBoard 세팅 제거.
        private bool _boardEngagedThisLevel;

        private float EffectiveFailGraceDelay => Mathf.Max(_failGraceDelay, FAIL_RECHECK_MIN_DURATION);
        private bool _wasForceFullBeltAdvanceActive;

        // [2026-05-13] Pause 중 fail eval 정지 + 재개 시 critical 상태 reset 위한 추적.
        private bool _wasPausedLastFrame;

        private void Update()
        {
            var __sw = InGamePerfLogger.StartSection();
            try
            {
#if UNITY_EDITOR
            // [CHEAT 2026-06-12] 에디터 0 키 — 강제 실패 (다트 탈선→실패 팝업 연출 확인용)
            var __cheatKb = UnityEngine.InputSystem.Keyboard.current;
            if (__cheatKb != null && __cheatKb.digit0Key.wasPressedThisFrame)
                ForceFailStage();
            // ROLLBACK_DIAG_HOTKEY_20260706: 에디터 9 키 — 데드락 현장 무조건 전체 상태 덤프.
            //   자동 [Freeze-DEBUG] 는 아래 early-return(상태!=Playing / PauseManager) 뒤라 일시정지 누수·상태
            //   이탈형 데드락에선 영영 안 찍힘 → 게이트 앞에서 강제 덤프. 롤백: 이 블록 제거.
            if (__cheatKb != null && __cheatKb.digit9Key.wasPressedThisFrame)
            {
                int __efc = RailManager.HasInstance ? RailManager.Instance.EffectiveOccupiedCount : -1;
                int __cap = RailManager.HasInstance ? RailManager.Instance.PhysicalCapacity : -1;
                Debug.LogWarning(
                    $"[Diag-9] state={_currentState} failConfirmed={_failConfirmed} timeScale={Time.timeScale} " +
                    $"pauseMgr={PauseManager.IsPaused} balloons={_remainingBalloons} " +
                    $"hasMatch={HasOutermostMatchCached} lastDrainAgo={(Time.unscaledTime - _lastDrainUnscaledTime):F1}s " +
                    $"lastFireAgo={(DartManager.HasInstance && DartManager.Instance.LastFireUnscaledTime > 0f ? (Time.unscaledTime - DartManager.Instance.LastFireUnscaledTime) : -1f):F1}s");
                DumpFreezeState(__efc, __cap);
            }
#endif
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
                // ROLLBACK_WATCHDOG_GATE_DWELL_20260707 rev2: 재개 시 dwell 누적 리셋
                _watchdogGateOpenAccum = 0f;
                _watchdogGateClosedSince = float.NaN;
                _forceAdvanceInactiveSince = float.NaN; // ROLLBACK_SUPPLY_MATCH_FAIL_20260707
            }

            // Throttle — fail evaluation 매 frame 안 함.
            _stuckEvalTimer += Time.deltaTime;
            if (_stuckEvalTimer < STUCK_EVAL_INTERVAL) return;
            float evalDelta = _stuckEvalTimer;
            _stuckEvalTimer = 0f;

#if BF_RAIL_HOLDER
            // PROTO_RAIL_HOLDER_20260716: 레일 홀더 모드에서는 레일 점유가 항상 N(홀더 수) 고정 →
            //   만석/near-full/데드락 밴드가 전부 무의미하고, 아래 평가는 efc≈0 이라 영영 발동하지 않는다.
            //   압력계를 '총 탄약'으로 갈아끼운다: 레일 홀더 탄창 + 큐 탄창 = 0 인데 풍선이 남으면 실패.
            if (RailHolderController.ModeActiveForCurrentLevel)
            {
                EvaluateRailHolderAmmoFail(evalDelta);
                return;
            }
#endif

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
                // ROLLBACK_CONTINUE_SPACE_GATE_20260630:
                // Continue should protect the board while the rail has any real free slot.
                // The previous capacity-1 buffer could re-fail even though the player could see
                // rail space after continue.
                bool railStillHasSpace = !RailManager.HasInstance || continuePhysCap <= 0 || continueEfc < continuePhysCap;
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
            // ROLLBACK_FAIL_NEARFULL_GATE_20260708: '사실상 만석' 밴드 = 데드락 강제회전 개입 임계(cap-N, N=5~8).
            //   supply 큐 컷오프(ComputeReachableSupplyMatch)와 동일 소스(NearFullBandEmptySlots).
            //   실패 스펙 확정(2026-07-08): "실패 = 레일이 임계치(이 밴드)까지 참 + 공격할 것 없음".
            //   below-full 에서 배치된 다트가 공격 불가(hasMatch=false)여도 유저가 탭할 홀더가 남아 있으면
            //   실패 금지 — 기존엔 supply 오탐(Hidden 색 제외 등) 시 방치만으로 n초 뒤 실패했다(사용자 보고).
            //   엔드게임(모든 홀더 소진)은 밴드 밖에서도 noMovesLeft 로 실패 유지(영영 무실패 소프트락 방지).
            // ROLLBACK_FAIL_NEARFULL_TUNE3_20260708: 실패용 임계를 데드락 임계(NearFullBandEmptySlots,
            //   빈슬롯 5~8·배포점 수 비례)에서 분리 — 그 밴드는 회복 무브먼트용이라 실패 판정엔 과하게 일렀다
            //   (160캡에서 152부터 실패권, 사용자 "규제 너무 쎔" 2026-07-08). 고정 3 = 만석 직전 체감 +
            //   Level 167 반례(158/160 무한대기) 커버 유지. supply 큐 컷오프도 동일 상수(아래
            //   ComputeReachableSupplyMatch) — 실패 밴드와 함께 움직여야 정합. 데드락 강제회전은 기존 폭 유지.
            //   롤백: 두 곳을 RailManager.Instance.NearFullBandEmptySlots 로 환원.
            bool nearFullBand = RailManager.HasInstance && physCap > 0
                && efc >= physCap - FAIL_NEARFULL_EMPTY_SLOTS;
            if (forceFullBeltAdvance && !_wasForceFullBeltAdvanceActive)
            {
                // ROLLBACK_FAIL_FORCE_ADVANCE_RECHECK_TIMER:
                // Force-full-belt advance is a recovery window. Reset any previously accumulated
                // critical timer so the belt gets time to rotate and expose/fire possible matches.
                // ROLLBACK_SUPPLY_MATCH_FAIL_20260707 (히스테리시스): 데드락 무진행 exit→재진입 사이클
                //   (ROLLBACK_DEADLOCK_NO_PROGRESS_EXIT_20260706)로 상승 에지가 반복되면 이 리셋이
                //   criticalTimer 를 영영 0 으로 묶어 below-full stuck grace 가 안 참 — 워치독 rev2 와
                //   동일하게 '연속 비활성 ≥ RESET_SECONDS 후의 상승'만 진짜 회복 윈도우로 인정.
                if (float.IsNaN(_forceAdvanceInactiveSince)
                    || Time.unscaledTime - _forceAdvanceInactiveSince >= WATCHDOG_GATE_CLOSE_RESET_SECONDS)
                {
                    _criticalTimer = 0f;
                }
            }
            else if (!forceFullBeltAdvance && _wasForceFullBeltAdvanceActive)
            {
                _forceAdvanceInactiveSince = Time.unscaledTime; // 비활성 시작 시각 (히스테리시스 기준)
            }
            _wasForceFullBeltAdvanceActive = forceFullBeltAdvance;

            // ROLLBACK_WATCHDOG_GATE_DWELL_20260707 rev2: 게이트 체류시간 '누적' (필드 주석 참조).
            //   열림 → evalDelta 누적. 닫힘 → 즉시 리셋하지 않고, 닫힘이 RESET_SECONDS 이상 지속될 때만 리셋
            //   (데드락 exit→재진입 깜빡임 면역. 진짜 회복은 게이트가 길게 닫히므로 정상 리셋됨).
            // ROLLBACK_FAIL_NEARFULL_GATE_20260708: 게이트 조건을 워치독 발동 조건과 동일하게 밴드로 한정 —
            //   레벨 초반 low-occupancy 배포 스톨(forceFullBeltAdvance 만 true)의 dwell 이 누적 이월되어
            //   밴드 도달 순간 유예 없이 발동하는 rev2 이월 문제 재발 방지. 롤백: `railFull || forceFullBeltAdvance`.
            bool watchdogGateOpen = railFull || (forceFullBeltAdvance && nearFullBand);
            if (watchdogGateOpen)
            {
                _watchdogGateOpenAccum += evalDelta;
                _watchdogGateClosedSince = float.NaN;
            }
            else if (float.IsNaN(_watchdogGateClosedSince))
            {
                _watchdogGateClosedSince = Time.unscaledTime;
            }
            else if (Time.unscaledTime - _watchdogGateClosedSince >= WATCHDOG_GATE_CLOSE_RESET_SECONDS)
            {
                _watchdogGateOpenAccum = 0f;
            }
            float watchdogGateDwell = _watchdogGateOpenAccum;

            // ROLLBACK_RAIL_FREEZE_DIAG_20260622: 전면정지 진단 — 벨트오프셋·점유수 무변화 + pop 0 지속 시 1회 덤프.
            //   fail 평가(아래)보다 먼저 평가해 강제 fail 직전 상태를 포착. 동작 변경 없음.
            if (_debugLogFreeze && RailManager.HasInstance && _remainingBalloons > 0)
            {
                float beltOffset = RailManager.Instance.RotationOffset;
                bool moved = float.IsNaN(_freezeLastBeltOffset)
                             || Mathf.Abs(beltOffset - _freezeLastBeltOffset) > 0.0001f
                             || efc != _freezeLastEfc;
                _freezeLastBeltOffset = beltOffset;
                _freezeLastEfc = efc;
                if (moved) _freezeLastActivityTime = Time.unscaledTime;
                // in-flight 다트의 pop(배수)도 진전 — 정지로 오인하지 않게 _lastDrainUnscaledTime 포함.
                float lastActivity = Mathf.Max(_freezeLastActivityTime, _lastDrainUnscaledTime);
                if (Time.unscaledTime - lastActivity < FREEZE_DEBUG_SECONDS)
                {
                    _freezeDumpedThisStall = false; // 움직임 재개 → 재무장
                }
                else if (!_freezeDumpedThisStall)
                {
                    _freezeDumpedThisStall = true;
                    DumpFreezeState(efc, physCap);
                }
            }

            bool hasMatch = HasOutermostMatchCached;
            bool allHoldersEmpty = HolderManager.HasInstance && HolderManager.Instance.AreAllHoldersEmpty();

            // ROLLBACK_SUPPLY_MATCH_FAIL_20260707: 상태 기반 실패 — '도달 가능 공급' 단일 규칙.
            //   배경: 기존 1차 판정(ROLLBACK_FAIL_REQUIRE_RAILFULL_20260622)은 railFull(cap-1) 이 게이트라,
            //   데드락 회피가 레일을 cap-8~cap-1 밴드에 묶으면 상태 판정이 영영 미달 → below-full 잼은
            //   전부 '시간 기반 워치독(10~12s) 대기'로 넘어가 실패 시점이 애매했다 (2026-07-07 Level 38 영상).
            //   새 규칙 — 공급(supply) = 지금 또는 앞으로 레일에서 발사될 수 있는 다트의 색 집합:
            //     · 빈 슬롯 > 0 : 레일 다트 ∪ 큐 공급(HolderManager.CollectSupplyColors — 배포중/대기 커밋 +
            //                     탭 가능 잔여 홀더 + 스포너 미래분. frozen/hidden/locked 는 해금 전
            //                     공격 불가 원칙으로 제외 — 사용자 결정 2026-07-07).
            //     · 빈 슬롯 = 0 : 레일 다트만 (아무것도 못 올라오므로 큐 매칭은 무의미).
            //   실패 = 풍선 잔존 && 공급 ∩ 공격가능 외곽색 = ∅  (grace 1.5~2s 는 기존 유지).
            //   효과: ① 필패 보드는 below-full 밴드에서도 결정적으로 즉시(grace 후) 실패 — 애매한 회전 대기 제거.
            //         ② 구원 가능(매칭 색이 올라오는 중/탭 가능) 보드는 만석 직전에도 실패하지 않음.
            //         ③ 데드락 강제회전의 역할은 '실패 유예'가 아니라 '회복 시도'로 한정.
            //   워치독(10~12s)은 상태모델이 오판하는 케이스(움직임-frozen 레일 다트가 매칭으로 잡히는 경우,
            //   TargetBox 다중 live egg 의 첫색 근사 등)의 최후 안전망으로 유지.
            //   롤백: supplyMatch 3줄을 삭제하고
            //         `bool noMovesLeft = allHoldersEmpty && _remainingBalloons > 0 && !hasMatch;`
            //         `bool stuck = _remainingBalloons > 0 && !hasMatch && (railFull || noMovesLeft);` 복원.
            // ROLLBACK_DEADLOCK_ENTRY_SIGNALS_20260707: 데드락 진입 게이트와 동일 신호를 공유(프레임 캐시) —
            //   판정 로직이 두 곳에서 갈라지지 않게 단일 소스(HasReachableSupplyMatchCached)로 일원화.
            bool supplyMatch = HasReachableSupplyMatchCached;

            // ROLLBACK_SUPPLY_ACTIONABLE_20260707: 배포 진행 가드 — 공급을 '앞줄 탭 가능'으로 조인 것의 균형추.
            //   깊은 줄에만 매칭이 있어도 배포가 진행 중(최근 placement + 빈 슬롯 존재)이면 큐가 전진해 그
            //   홀더가 앞줄로 올 수 있으므로 stuck 억제. 잼이면 placement 가 멎어 QUIET(1.5s) 후 가드 해제.
            //   만석(efc>=physCap)은 placement 가 정의상 불가 — 가드 없이 즉시 평가(고전 만석 fail 속도 유지).
            //   롤백: deployProgress 항 제거.
            bool deployProgress = RailManager.HasInstance && physCap > 0 && efc < physCap
                && RailManager.Instance.LastPlacementUnscaledTime > 0f
                && Time.unscaledTime - RailManager.Instance.LastPlacementUnscaledTime < PLACEMENT_PROGRESS_QUIET_SECONDS;

            // ROLLBACK_SUPPLY_FAIL_ENGAGE_GATE_20260707: 첫 다트가 레일에 배치되면 '보드 시작'으로 래치(첫 pop 은 HandleBalloonPopped).
            if (!_boardEngagedThisLevel && RailManager.HasInstance && RailManager.Instance.LastPlacementUnscaledTime > 0f)
                _boardEngagedThisLevel = true;

            // noMovesLeft 는 FailReason 구분용(NoMovesLeft vs RailOverflow) + 밴드 밖 엔드게임 실패 arm.
            bool noMovesLeft = allHoldersEmpty && _remainingBalloons > 0 && !supplyMatch;
            // ROLLBACK_SUPPLY_FAIL_ENGAGE_GATE_20260707: '&& _boardEngagedThisLevel' — 보드가 실제로 시작하기 전(신선한 빈 레일)
            //   엔 supply-fail 금지. 시작 후엔 기존 로직 그대로(진짜 잼/엔드게임 실패 유지).
            // ROLLBACK_FAIL_NEARFULL_GATE_20260708: 빠른 실패(1.5s grace)는 ① near-full 밴드(사실상 만석 +
            //   supply=레일만) 또는 ② 엔드게임(모든 홀더 소진 + 공급 없음)에서만. below-full + 홀더 잔존은
            //   유저가 아직 행동할 수 있으므로 방치만으로 실패하지 않는다(진짜 잼이면 탭이 이어져 결국
            //   밴드 도달 or 홀더 소진으로 수렴). 롤백: `&& (nearFullBand || noMovesLeft)` 항 제거.
            bool stuck = _remainingBalloons > 0 && !supplyMatch && !deployProgress && _boardEngagedThisLevel
                && (nearFullBand || noMovesLeft);

            // ROLLBACK_WATCHDOG_PLACEMENT_QUIET_20260707: 워치독 활동 신호에 '레일 배치(placement)' 추가.
            //   배경: 두 워치독(no-drainage/no-fire)은 hasMatch 무관 설계 + rev2 누적 dwell 이라, 만석 '전'에
            //   쌓인 dwell·무배수·무발사 시간이 그대로 이월된다 — 매칭 색 홀더를 탭해 다트가 올라가는 중
            //   레일이 (railFull||forceFullBeltAdvance) 에 닿는 순간 세 조건이 이미 충족돼 유예 0초 즉시 fail
            //   (GATE_DWELL 도입 사유(위 :78-80)였던 그 증상이 rev2 누적식 + WATCHDOG_FAST 단축(4s/5s)으로
            //   재발 — 2026-07-07 "배포 다트 올라가는 중 만석 → 공격 가능한데 즉시 실패" 리포트).
            //   배치는 '보드가 아직 진행 중' 신호 — 마지막 배치 후 각 워치독의 임계시간까지는 발동을 억제해,
            //   방금 올라온 다트가 벨트를 돌아 발사될 기회(주석 :71-72, 대형 레일 4s+)를 준다.
            //   진짜 잼은 만석이라 배치가 정의상 멈추므로 억제는 임계시간 뒤 자동 해제 — 최후 안전망 역할은
            //   최대 N초 지연될 뿐 보존. 롤백: 이 블록 + 두 워치독의 placementQuiet* 조건 제거.
            float watchdogLastPlacement = RailManager.HasInstance ? RailManager.Instance.LastPlacementUnscaledTime : 0f;
            bool placementQuietDrain = watchdogLastPlacement <= 0f
                || Time.unscaledTime - watchdogLastPlacement >= NO_DRAINAGE_FAIL_SECONDS;
            bool placementQuietFire = watchdogLastPlacement <= 0f
                || Time.unscaledTime - watchdogLastPlacement >= NO_FIRE_FAIL_SECONDS;

            // ROLLBACK_NO_DRAINAGE_FAIL_WATCHDOG_20260622: hasMatch 무관 무진전 워치독.
            //   레일 만석(railFull) + 풍선 잔존 + NO_DRAINAGE_FAIL_SECONDS 간 pop(배수) 0 이면 → RailOverflow fail 확정.
            //   색 불균형으로 dead-dart 가 영구 점유하는데 다른 색이 hasMatch=true 를 유지해 stuck 이 영영 false 인 hang 을 차단.
            //   pop 이 한 번이라도 나면 _lastDrainUnscaledTime 갱신 → 정상/느린-진행 플레이는 오발동 X.
            // ROLLBACK_NO_DRAINAGE_BELOWFULL_RESTORE_20260622: no-drainage 워치독 BELOWFULL 복구.
            //   배경: REQUIRE_RAILFULL 에서 '빠른 오발동'(stuck 의 forceFullBeltAdvance, ~1.5s grace) 과 함께
            //   '느린 안전망'(이 no-drainage, 10s) 의 belowfull 까지 같이 떼냈더니, deadlock 모드가 레일을 cap-1 미만
            //   밴드에 묶어둔 상태(forceFullBeltAdvance=true, railFull=false)에서 공격 불가로 정지해도 fail 경로가
            //   아예 없어 '영영 실패 안 함' 이 됐다. → 느린 안전망(10s 무배수)만 belowfull 로 복구.
            //   stuck(빠른 grace) 은 railFull-only 유지하므로 조기 오발동은 재발 안 함(10s 는 진짜 정지의 최후 fail).
            //   pop 한 번이면 _lastDrainUnscaledTime 리셋이라 정상/회복-진행 플레이는 오발동 X.
            //   롤백: `(railFull || forceFullBeltAdvance)` 를 `railFull` 로 환원.
            // ROLLBACK_WATCHDOG_GATE_DWELL_20260707: watchdogGateDwell 조건 추가 — '만석 상태로 N초' 를 함께 요구.
            // ROLLBACK_WATCHDOG_BELOWFULL_SUPPLY_GATE_20260707: below-full 워치독은 supplyMatch=false 일 때만.
            //   배경: 워치독 게이트는 railFull 외에 forceFullBeltAdvance(데드락 모드)로도 열리는데, 데드락은
            //   레벨 초반 40/160 같은 below-full 에서도 배포 스톨로 진입한다(DEPLOY_STALL_DEADLOCK_TRIGGER).
            //   이때 유저가 희귀색 홀더를 눌러 발사가 잠시 0 이면, WATCHDOG_FAST(4s/5s) + dwell 누적(rev2)으로
            //   '큐에 매칭 색이 탭 가능하게 대기 중'(supplyMatch=true)인데도 시작 수 초 만에 fail
            //   (2026-07-07 Level 167 재현: 840풍선, 홀더 2탭 직후 실패).
            //   워치독의 전제("레일이 못 빠지고 아무것도 못 올라옴")는 만석에서만 성립 — below-full 에선 큐가
            //   살아 있으므로, 필패 판정은 supply 상태 규칙(stuck 1.5s)이 담당하고 워치독은
            //   ① 만석(railFull)이거나 ② 상태 모델도 필패 동의(!supplyMatch)일 때만 발동한다.
            //   below-full + supplyMatch 오탐(모델이 매칭 과대평가) 잔여 리스크는 DartManager 정지 안전망
            //   6종이 발사 재개로 해소 + 배포가 이어지면 결국 railFull 도달로 워치독 재무장.
            //   롤백: 두 워치독의 `(railFull || !supplyMatch)` 항 제거.
            // ROLLBACK_FAIL_NEARFULL_GATE_20260708: below-full arm 을 데드락 밴드(nearFullBand)로 한정 —
            //   레벨 초반 low-occupancy 배포 스톨 + supply 오탐 + 방치가 워치독으로 실패하던 경로 차단
            //   (Level 38 belowfull 잼은 데드락 밴드 안이므로 안전망 유지). 롤백: `&& nearFullBand` 제거.
            if ((railFull || (forceFullBeltAdvance && nearFullBand)) && (railFull || !supplyMatch) && _remainingBalloons > 0
                && watchdogGateDwell >= NO_DRAINAGE_FAIL_SECONDS
                && placementQuietDrain // ROLLBACK_WATCHDOG_PLACEMENT_QUIET_20260707
                && Time.unscaledTime - _lastDrainUnscaledTime >= NO_DRAINAGE_FAIL_SECONDS)
            {
                if (_debugLogFail) DumpAttackState($"[Fail-DEBUG] no-drainage watchdog — 만석 {NO_DRAINAGE_FAIL_SECONDS}s 무배수 → 강제 RailOverflow fail");
                _failConfirmed = true;
                TriggerFail(FailReason.RailOverflow);
                return;
            }

            // ROLLBACK_NO_FIRE_FAIL_WATCHDOG_20260622: H1 차단 — '레일 배수(발사)' 기준 무진전 워치독.
            //   문제(H1): no-drainage 는 pop(풍선 배수) 기준이라, 색 불균형 잼(죽은 색 다트가 레일 점유, 발사 불가)에서
            //   '다른 색'의 in-flight pop 이 _lastDrainUnscaledTime 을 계속 리셋 → no-drainage starve → 영영 실패 안 함.
            //   해결: pop 이 아니라 '레일에서 다트가 마지막으로 발사된 시각(DartManager.LastFireUnscaledTime)' 기준.
            //   레일 만석/강제회전 + 풍선 잔존 + NO_FIRE_FAIL_SECONDS 간 발사 0 = 레일이 못 빠지는 진짜 잼 → RailOverflow fail.
            //   발사가 한 번이라도 있으면(=레일 배수=진행) 리셋이라 정상/느린 진행 플레이엔 오발동 X. 다른 색 pop 엔 안 흔들림.
            //   롤백: 이 블록 삭제.
            // ROLLBACK_WATCHDOG_GATE_DWELL_20260707: watchdogGateDwell 조건 추가 — '만석 상태로 N초' 를 함께 요구.
            // ROLLBACK_FAIL_NEARFULL_GATE_20260708: below-full arm 을 데드락 밴드로 한정 (위 no-drainage 와 동일).
            if ((railFull || (forceFullBeltAdvance && nearFullBand)) && (railFull || !supplyMatch) // ROLLBACK_WATCHDOG_BELOWFULL_SUPPLY_GATE_20260707
                && _remainingBalloons > 0
                && watchdogGateDwell >= NO_FIRE_FAIL_SECONDS
                && placementQuietFire // ROLLBACK_WATCHDOG_PLACEMENT_QUIET_20260707
                && DartManager.HasInstance && DartManager.Instance.LastFireUnscaledTime > 0f
                && Time.unscaledTime - DartManager.Instance.LastFireUnscaledTime >= NO_FIRE_FAIL_SECONDS)
            {
                if (_debugLogFail) DumpAttackState($"[Fail-DEBUG] no-fire watchdog — 만석 {NO_FIRE_FAIL_SECONDS}s 무발사(레일 배수 0) → 강제 RailOverflow fail");
                _failConfirmed = true;
                TriggerFail(FailReason.RailOverflow);
                return;
            }

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
                        Debug.Log($"[Fail-DEBUG/Periodic] efc={efc}/{physCap} railFull={railFull} forceFullBeltAdvance={forceFullBeltAdvance} allHoldersEmpty={allHoldersEmpty} noMovesLeft={noMovesLeft} balloons={_remainingBalloons} hasMatch={hasMatch} supplyMatch={supplyMatch} deployProgress={deployProgress} stuck={stuck} isCritical={_isCritical} timer={_criticalTimer:F2} gateDwell={watchdogGateDwell:F1}");
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
        [SerializeField] private bool _debugLogFail = true;

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

            // ROLLBACK_SUPPLY_DUMP_20260707: '실제 판정 입력' 표기 — 위의 outermost/matched 는 4방향 전체
            //   집합과 레일-단독 교집합(참고용)이라, supplyMatch 가 왜 true/false 인지 이 덤프로 판별이
            //   불가능했다(2026-07-07 Level 62 로그: matched=[8] 인데 10s 무발사 → 어느 항이 생존시켰는지
            //   레일-side 필터/앞줄 공급/deployProgress 구분 불가). 판정과 동일한 소스에서 직접 출력.
            string sideStr = "n/a", supplyStr = "n/a", verdictStr = "n/a";
            if (BalloonController.HasInstance && RailManager.HasInstance)
            {
                var side = GetRailSideOutermostBalloonColors();
                sideStr = side.Count == 0 ? "(empty)" : string.Join(",", side);
            }
            if (HolderManager.HasInstance)
            {
                var supply = new HashSet<int>(); // 디버그 전용 — 판정 버퍼(_reusableSupplyColors)와 분리
                HolderManager.Instance.CollectSupplyColors(supply);
                supplyStr = supply.Count == 0 ? "(empty)" : string.Join(",", supply);
            }
            {
                bool sm = HasReachableSupplyMatchCached;
                float lastPlace = RailManager.HasInstance ? RailManager.Instance.LastPlacementUnscaledTime : 0f;
                float placeAgo = lastPlace > 0f ? Time.unscaledTime - lastPlace : -1f;
                float lastFire = DartManager.HasInstance ? DartManager.Instance.LastFireUnscaledTime : 0f;
                float fireAgo = lastFire > 0f ? Time.unscaledTime - lastFire : -1f;
                verdictStr = $"supplyMatch={sm} lastPlaceAgo={placeAgo:F1}s lastFireAgo={fireAgo:F1}s " +
                             $"gateDwell={_watchdogGateOpenAccum:F1}s dlHolder={(RailManager.HasInstance ? RailManager.Instance.DeadlockHolderId : -1)}";
            }

            Debug.Log($"{tag} state={_currentState} balloons={_remainingBalloons} occ={occStr}\n" +
                      $"  rail colors=[{railStr}]\n" +
                      $"  outermost colors(4방향 전체·참고)=[{outerStr}]\n" +
                      $"  → rail-only matched(참고)=[{matchStr}]\n" +
                      $"  [판정] railSide outermost=[{sideStr}]\n" +
                      $"  [판정] queue supply(커밋+앞줄)=[{supplyStr}]\n" +
                      $"  [판정] {verdictStr}\n" +
                      $"  holders=[{holderStr}]");
        }

        /// <summary>ROLLBACK_RAIL_FREEZE_DIAG_20260622: hard-freeze(전면정지) 진단 1회 덤프 (fail 아님).
        ///   early-return 류 정지 원인 후보(부스터 pause/await, deadlock mode, deploy point, 보드 종료, PauseManager)와
        ///   공격 가능성 상태(DumpAttackState)를 함께 출력. 동작 변경 없음.</summary>
        private void DumpFreezeState(int efc, int physCap)
        {
            string rail = RailManager.HasInstance ? RailManager.Instance.GetFreezeDiagnostics() : "(no RailManager)";
            string booster = BoosterExecutor.HasInstance ? BoosterExecutor.Instance.GetDebugState() : "(no BoosterExecutor)";
            Debug.LogWarning(
                $"[Freeze-DEBUG] HARD FREEZE 감지 — {FREEZE_DEBUG_SECONDS}s 무진전(벨트오프셋·점유 불변·pop 0). " +
                $"balloons={_remainingBalloons} efc={efc}/{physCap} pauseMgr={PauseManager.IsPaused}\n" +
                $"  rail   : {rail}\n" +
                $"  booster: {booster}");
            DumpAttackState("[Freeze-DEBUG] attack-state");
        }

        #endregion

        #region Public Methods

        /// <summary>
        /// Initializes the board for a new level.
        /// </summary>
        public void InitializeBoard(int levelId, int initialBalloonCount)
        {
            // ROLLBACK_BSM_INIT_STOPCOROUTINES_20260622: 재도전/리로드 시 이전 보드의 코루틴(예: ContinuePopThenResume)이
            //   살아남아 새 보드에 stale 색으로 force-pop 하던 레이스 차단. 보드 초기화 시점에 전부 정지.
            StopAllCoroutines();

            _currentLevelId = levelId;
            _remainingBalloons = initialBalloonCount;
            _currentState = BoardState.Playing;
            _currentGaugeStage = GaugeStage.Safe;
            _isCritical = false;
            _criticalTimer = 0f;
            _failConfirmed = false;
            _postContinueGraceUntil = 0f;
            _awaitingPostContinuePlayerAction = false;
            _boardEngagedThisLevel = false; // ROLLBACK_SUPPLY_FAIL_ENGAGE_GATE_20260707: 신선 스테이지 활동 게이트 리셋

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
            if (_remainingBalloons > 0) return false;
            // ROLLBACK_FLEXTUBE_CLEAR_CONDITION_20260708: FlexTube 는 RemainingCount 에서 제외되고
            //   (Wall 과 함께 setup 시 excludeCount — 셀 제거가 silent 경로라 카운트 불변) 있어,
            //   일반 풍선이 전부 팝되면 살아있는 튜브(잔여 HP)가 있어도 클리어가 떴다(레벨 215:
            //   Sage 튜브 HP 20 + 미배포 홀더 상태 오클리어). FlexTube 는 파괴 대상 기믹이므로
            //   살아있는 튜브 셀이 하나라도 있으면 클리어 아님. 튜브 파괴 완료(BeginFinish) 시점의
            //   재평가는 ReevaluateClearAfterGimmickResolved 가 담당.
            //   롤백: 아래 if 제거 (+ FlexTube.BeginFinish 의 재평가 호출, BalloonController.HasLiveFlexTubeCells 제거).
            if (BalloonController.HasInstance && BalloonController.Instance.HasLiveFlexTubeCells()) return false;
            return true;
        }

        /// <summary>ROLLBACK_FLEXTUBE_CLEAR_CONDITION_20260708: 팝 이벤트 없이 해소되는 기믹(FlexTube 파괴)
        /// 완료 시점의 클리어 재평가 훅. 마지막 남은 것이 튜브였다면 OnBalloonPopped 가 더 안 오므로
        /// 이 호출이 없으면 클리어가 영영 평가되지 않는다.</summary>
        public void ReevaluateClearAfterGimmickResolved()
        {
            // ROLLBACK_FAIL_CLEAR_NO_FLIP_20260708: Failed 후 기믹 해소로도 clear 로 뒤집지 않음(위 HandleBalloonPopped 와 동일 정책).
            //   롤백: 조건을  != Playing && != Failed  로 환원.
            if (_currentState != BoardState.Playing) return;
            if (BalloonController.HasInstance)
                _remainingBalloons = BalloonController.Instance.GetRemainingCount();
            EvaluateClearCondition();
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
                    // ROLLBACK_CONTINUE_SPACE_GATE_20260630: same rule as Update().
                    if (postContinueCapacity <= 0 || postContinueOccupied < postContinueCapacity)
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
            _railSideColorsValid = false; // ROLLBACK_RAILSIDE_CACHE_EXPLICIT_INVALIDATE_20260707: 레벨 경계 이중 방어 — 이전 레벨 rail-side 색 잔존 금지
            _awaitingPostContinuePlayerAction = false;
            _postContinueGraceUntil = 0f;
            _lastDrainUnscaledTime = Time.unscaledTime; // ROLLBACK_NO_DRAINAGE_FAIL_WATCHDOG_20260622: 레벨 시작 시 리셋
            _watchdogGateOpenAccum = 0f;                // ROLLBACK_WATCHDOG_GATE_DWELL_20260707 rev2: 레벨 로드 시 리셋
            _watchdogGateClosedSince = float.NaN;
            _forceAdvanceInactiveSince = float.NaN;     // ROLLBACK_SUPPLY_MATCH_FAIL_20260707
            // ROLLBACK_GAUGE_RESET_ON_LEVEL_LOAD_20260706: Retry(in-place) 시 위급(빨간) 게이지·타일 danger 가
            //   잔상으로 남는 문제 — InitializeBoard(477) 는 _currentGaugeStage 만 Safe 로 되돌리고 시각 리셋
            //   이벤트를 발행하지 않아(OnGaugeStageChanged 는 '변화 시'에만 발행) HUD/타일이 이전 판 표시를
            //   유지했다. 레벨 로드 시 Safe 를 명시 발행 + danger 표시 OFF. 롤백: 아래 블록 제거.
            EventBus.Publish(new OnGaugeStageChanged
            {
                previousStage = (int)_currentGaugeStage,
                currentStage  = (int)GaugeStage.Safe,
                occupancy     = 0f
            });
            _currentGaugeStage = GaugeStage.Safe;
            if (BoardTileManager.HasInstance)
                BoardTileManager.Instance.SetDangerVisible(false);
        }

        // ROLLBACK_APP_RESUME_WATCHDOG_REBASE_20260706: no-drainage/no-fire fail 워치독은 Time.unscaledTime
        //   기반인데, 앱 서스펜드/에디터 포커스아웃 동안 unscaled 시계가 점프해 복귀 즉시 'N초 무배수'로
        //   오판 → 레일 만석 근처에서 창 전환 후 복귀하면 매칭 가능한 다트가 있어도 즉시 실패했다.
        //   복귀 시점에 활동 타임스탬프를 리베이스한다. 롤백: 아래 3개 메서드 제거.
        private void OnApplicationPause(bool paused) { if (!paused) RebaseActivityTimersAfterSuspend(); }
        private void OnApplicationFocus(bool focused) { if (focused) RebaseActivityTimersAfterSuspend(); }
        private void RebaseActivityTimersAfterSuspend()
        {
            _lastDrainUnscaledTime  = Time.unscaledTime;
            _freezeLastActivityTime = Time.unscaledTime;
            // ROLLBACK_WATCHDOG_GATE_DWELL_20260707 rev2: 서스펜드 복귀 시 누적 리셋(시계 점프 방어).
            //   (누적식은 evalDelta 기반이라 suspend 중 자체 증가는 없지만, closedSince 의 unscaled 점프 오판 방지.)
            _watchdogGateOpenAccum  = 0f;
            _watchdogGateClosedSince = float.NaN;
        }

        private void HandleHolderTapped(OnHolderTapped evt)
        {
            _awaitingPostContinuePlayerAction = false;
            _postContinueGraceUntil = 0f;
        }

        private void HandleBalloonPopped(OnBalloonPopped evt)
        {
            _outermostDirty = true;
            _boardEngagedThisLevel = true; // ROLLBACK_SUPPLY_FAIL_ENGAGE_GATE_20260707: pop = 보드 시작됨(활동 게이트 래치)
            // ROLLBACK_NO_DRAINAGE_FAIL_WATCHDOG_20260622: pop = 배수 진행 → 무진전 타이머 리셋.
            _lastDrainUnscaledTime = Time.unscaledTime;
            if (BalloonController.HasInstance)
            {
                _remainingBalloons = BalloonController.Instance.GetRemainingCount();
            }
            else
            {
                _remainingBalloons = Mathf.Max(0, _remainingBalloons - 1);
            }

            PublishBoardStateChanged();

            // ROLLBACK_FAIL_CLEAR_NO_FLIP_20260708: 실패 확정(Failed) 후엔 뒤늦은 팝으로 clear 로 뒤집지 않는다.
            //   배경 = WS 진행 중 OUT OF SPACE 실패 → fail 팝업(fail01/continue/fail02)이 뜬 동안 보드는 pause 되지
            //   않아(오직 fail02 OnEnable 이후만 pause) 비행 다트가 남은 풍선을 계속 팝 → Failed 상태에서 clear 평가가
            //   통과(기존 "Clear always wins")하면 OnBoardCleared → WS 활성이라 GameBootstrap 이 자동 GoToLobby →
            //   fail02 가 확인 전에 로딩으로 튕김. 이어하기 후에는 ResumeAfterContinue 가 상태를 Playing 으로 되돌리므로
            //   그 경로의 clear 는 정상(무영향). 정상 클리어(Playing)도 무영향. 잃는 것 = '실패와 거의 동시 막판 팝'을
            //   승리로 인정하던 grace 뿐(사용자 결정: OUT OF SPACE 는 실패 유지, 클릭 시에만 이동).
            //   롤백: 조건을  == Playing || == Failed  로 환원(737 의 ReevaluateClearAfterGimmickResolved 게이트도 함께).
            if (_currentState == BoardState.Playing)
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

            // ROLLBACK_SUPPLY_MATCH_FAIL_20260707 (히스테리시스): 데드락 무진행 exit→재진입 사이클마다
            //   recheck 윈도우가 _criticalTimer 를 0 으로 되돌리면 below-full stuck grace 가 영영 안 참.
            //   '연속 비활성 ≥ RESET_SECONDS 후의 첫 진입'만 진짜 회복 윈도우로 인정해 리셋. 사이클 중
            //   재진입은 무시 — critical 진입/유지는 Update 의 stuck 평가가 담당. 롤백: if 가드 제거.
            bool quietBeforeEntry = float.IsNaN(_forceAdvanceInactiveSince)
                || Time.unscaledTime - _forceAdvanceInactiveSince >= WATCHDOG_GATE_CLOSE_RESET_SECONDS;
            if (quietBeforeEntry)
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
            // ROLLBACK_SUPPLY_FAIL_FAST_GRACE_20260707: force 회전 연장 폐지 + grace 를 명세(1.5s)로 캡.
            //   ① 연장 폐지 근거: stuck 이 SUPPLY_MATCH 규칙(색 '집합' 교집합)이 된 뒤로는 벨트 회전이
            //      판정을 바꿀 수 없다 — 회전은 다트 '위치'만 바꾸고 색 집합은 불변. 회전 중 발사/pop 이
            //      나면 supplyMatch 가 true 로 뒤집혀 recovery 가 즉시 해제하므로 연장은 순수 대기 손실.
            //      (기존 travelDelay 1.0~2.0s 는 위치 기반 옛 모델의 "회전으로 매칭 노출" 가설용이었음.)
            //   ② 캡 근거: Inspector failGraceDelay 의 과거 확장 튜닝(1.5→5→3s)은 옛 신호가 부정확해
            //      회복 시간을 벌던 것 — supply 신호는 확정적이라 명세 1.5s 초과분은 대기 손실.
            //      Inspector 로 '줄이는' 튜닝은 그대로 허용(Min).
            //   롤백: 아래 1줄을 기존 본문(EffectiveFailGraceDelay + force 시 travelDelay clamp[1,2] Max)으로 환원.
            _ = forceFullBeltAdvance; // 시그니처 유지용 (롤백 편의)
            return Mathf.Min(EffectiveFailGraceDelay, FAIL_GRACE_CAP_SECONDS);
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

        /// <summary>[CHEAT] 에디터 0 키 전용 — 실패 트리거를 강제 발화해 실패 연출(다트 탈선→팝업)을 확인.
        /// Playing 상태일 때만 동작. disableFail 토글도 우회(연출 확인 목적).</summary>
        public void ForceFailStage()
        {
            if (_currentState != BoardState.Playing)
            {
                Debug.LogWarning($"[BoardStateManager] [CHEAT] ForceFailStage 무시 — 현재 상태={_currentState}");
                return;
            }
            Debug.Log("[BoardStateManager] [CHEAT] ForceFailStage 호출 — 강제 실패 (탈선 연출 확인용)");
            // disableFail 토글이 켜져 있어도 연출 확인이 목적이므로 일시 우회.
            bool savedDisableFail = false;
            if (GameManager.HasInstance)
            {
                savedDisableFail = GameManager.Instance.Board.disableFail;
                GameManager.Instance.Board.disableFail = false;
            }
            TriggerFail(FailReason.RailOverflow);
            if (GameManager.HasInstance)
                GameManager.Instance.Board.disableFail = savedDisableFail;
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

        // ROLLBACK_DEADLOCK_ENTRY_SIGNALS_20260707: '도달 가능 공급 매칭' 신호의 공용 노출 (프레임 캐시).
        //   소비처 ① Update 의 상태 실패 판정(supplyMatch) ② HolderVisualManager.TryEnterDeadlockIfNeeded 의
        //   진입 게이트 — 필패(매칭 없음) 보드는 데드락 회전이 무의미하므로 진입을 건너뛰고 실패가 결론.
        //   placement 실패마다 조회되므로 HasOutermostMatchCached 와 동일한 frameCount 캐시로 프레임당 1회 계산.
        private int _supplyMatchCacheFrame = -1;
        private bool _cachedSupplyMatch;

        /// <summary>공급(레일 다트 ∪ 빈슬롯>0 이면 큐 공급) ∩ 공격가능 외곽색 ≠ ∅ 인지 — 프레임 캐시.
        /// false = 지금 상태에서 어떤 발사도 영영 불가능(필패 후보). SUPPLY_MATCH_FAIL 규칙과 동일 정의.</summary>
        public bool HasReachableSupplyMatchCached
        {
            get
            {
                if (_supplyMatchCacheFrame != Time.frameCount)
                {
                    _cachedSupplyMatch = ComputeReachableSupplyMatch();
                    _supplyMatchCacheFrame = Time.frameCount;
                }
                return _cachedSupplyMatch;
            }
        }

        // ROLLBACK_DEADLOCK_SUPPLY_DIAG_20260709: 데드락 시나리오 판별용 진단 wrapper (테스트 전용).
        //   원래 로직은 ComputeReachableSupplyMatchInner 로 그대로 두고, 결과에 진단 덤프만 얹는다.
        //   판독: supplyMatch=false = 실패 무장. outer[] 에 눈에 보이는 외곽색이 빠지고 sideCount 가 실제 변수와
        //         불일치 → 시나리오3(기하 오판). outer[] 정상인데 supply[] 에 매칭 없음 → 시나리오1(색 진짜 소진).
        //         supplyMatch=true 인데 화면이 churn → 시나리오B(배포점 잼, 미관 — HVM [데드락-진단] 로그로 확정).
        //   롤백: 이 wrapper + DumpSupplyMatchDiag + 필드 3개 제거하고 Inner 를 원래 이름으로 환원.
        private bool _diagLastSupplyMatch = true;
        private float _diagLastSupplyLogTime = -999f;
        private readonly System.Collections.Generic.HashSet<int> _diagSupplyBuf = new System.Collections.Generic.HashSet<int>();
        private bool ComputeReachableSupplyMatch()
        {
            bool result = ComputeReachableSupplyMatchInner();
            DumpSupplyMatchDiag(result);
            return result;
        }

        private void DumpSupplyMatchDiag(bool result)
        {
            bool transition = result != _diagLastSupplyMatch;
            _diagLastSupplyMatch = result;
            if (!transition)
            {
                if (result) return;                                                  // 정상(true) 지속은 로그 안 함(스팸 방지)
                if (Time.unscaledTime - _diagLastSupplyLogTime < 0.5f) return;        // false(실패 무장) 지속은 0.5s 스로틀
            }
            _diagLastSupplyLogTime = Time.unscaledTime;

            int physCap  = RailManager.HasInstance ? RailManager.Instance.PhysicalCapacity : -1;
            int efc      = RailManager.HasInstance ? RailManager.Instance.EffectiveOccupiedCount : -1;
            int sideCnt  = RailManager.HasInstance ? RailManager.GetRailSideCount(physCap) : -1;
            var outer    = GetRailSideOutermostBalloonColors();
            _diagSupplyBuf.Clear();
            if (HolderManager.HasInstance) HolderManager.Instance.CollectSupplyColors(_diagSupplyBuf);
            Debug.Log($"[판정] supplyMatch={result} efc={efc}/{physCap} sideCount={sideCnt} " +
                      $"outer=[{string.Join(",", outer)}] supply=[{string.Join(",", _diagSupplyBuf)}] remain={_remainingBalloons}");

            // ROLLBACK_DEADLOCK_SUPPLY_DIAG_20260709: 레일 색 클로그 진단 — 레일을 막은 색이 '진짜 죽었나(잔여 0)'
            //   vs '깊은 줄에 생존(잔여>0)'. supplyMatch=false 일 때만. 판독: rail>0 인데 balloon=0 인 색이 많으면
            //   dead-color 클로그(퍼지가 정답). 반대로 balloon>0 이면 깊은줄 생존(배포 순서/잼 문제, 퍼지 위험).
            //   (GetActiveBalloonsByColor 는 hidden 포함 잔여수.) 롤백: 이 블록 제거.
            if (!result && RailManager.HasInstance && BalloonController.HasInstance)
            {
                var hist = new System.Collections.Generic.Dictionary<int, int>();
                int frozenOnRail = 0;
                var darts = RailManager.Instance.GetAllDarts();
                for (int i = 0; i < darts.Count; i++)
                {
                    var d = darts[i];
                    if (d == null) continue;
                    if (d.isFrozen) frozenOnRail++;
                    hist.TryGetValue(d.dartColor, out int n);
                    hist[d.dartColor] = n + 1;
                }
                var sb = new System.Text.StringBuilder();
                foreach (var kv in hist)
                {
                    var bals = BalloonController.Instance.GetActiveBalloonsByColor(kv.Key);
                    int remOfColor = bals != null ? bals.Count : 0;
                    sb.Append($"c{kv.Key}:rail{kv.Value}/bal{remOfColor}  ");
                }
                Debug.Log($"[클로그] frozenOnRail={frozenOnRail} [색:레일다트수/잔여풍선수]= {sb}");
            }
        }

        private bool ComputeReachableSupplyMatchInner()
        {
            if (HasOutermostMatchCached) return true;
            if (!RailManager.HasInstance || !HolderManager.HasInstance) return false;
            int physCap = RailManager.Instance.PhysicalCapacity;
            int efc = RailManager.Instance.EffectiveOccupiedCount;
            // ROLLBACK_SUPPLY_QUEUE_CUTOFF_NEARFULL_20260707 (사용자 결정 2026-07-07):
            //   임계(near-full) 밴드부터는 홀더(큐) 공급을 인정하지 않는다 — 판정은 '레일 위에 올라온
            //   다트'(배포 커밋 포함)만. 근거: "레일이 이 임계치까지 차서 공격할 게 없으면 죽게 처리.
            //   배포중인 홀더에 공격 가능한 색이 있어도 그건 아직 홀더에 있는 것 — 신경 쓸 대상은
            //   홀더가 이미 레일에 올려놓은 다트들." (Level 167 검증 로그: 158/160 + 레일 매칭 없음
            //   상태가 앞줄 매칭 색 때문에 supplyMatch=true 로 30s+ 무한 대기하던 반례.)
            //   컷오프 = NearFullBandEmptySlots(= 데드락 강제회전 개입 임계 capacity-N, N=5~8) —
            //   게임 무브먼트 모델이 이미 '사실상 만석'으로 취급하는 지점과 단일 소스 정합.
            //   이 밴드 밑에서는 기존 rev3 도달가능 깊이 규칙 그대로 (167/155 오탐 해소 유지).
            //   기존 조건은 `efc >= physCap`(빈 슬롯 0일 때만 차단)이었음. 롤백: 아래를
            //   `if (physCap <= 0 || efc >= physCap) return false;` 로 환원 + RailManager 노출 제거.
            // ROLLBACK_FAIL_NEARFULL_TUNE3_20260708: 컷오프를 실패 밴드 상수(고정 3)로 — 데드락 임계(5~8)와 분리.
            if (physCap <= 0 || efc >= physCap - FAIL_NEARFULL_EMPTY_SLOTS) return false;
            return QueueSupplyMatchesOutermost();
        }

        /// <summary>ROLLBACK_SUPPLY_MATCH_FAIL_20260707: 큐 공급(배포중/대기 커밋 + 탭 가능 잔여 홀더 +
        /// 스포너 미래분, frozen/hidden/locked 제외) 색이 공격 가능 외곽색과 교집합 있는지.
        /// 빈 슬롯 > 0 이고 레일 매칭(hasMatch)이 없을 때만 호출됨 (ComputeReachableSupplyMatch 참조).</summary>
        private bool QueueSupplyMatchesOutermost()
        {
            HashSet<int> outermost = GetRailSideOutermostBalloonColors();
            if (outermost.Count == 0) return false;

            _reusableSupplyColors.Clear();
            HolderManager.Instance.CollectSupplyColors(_reusableSupplyColors);
            foreach (int c in _reusableSupplyColors)
            {
                if (outermost.Contains(c))
                    return true;
            }
            return false;
        }

        /// <summary>
        /// 공격 가능 여부 검사 — rail dart 색상 ∩ outermost ≠ ∅.
        /// 사용자 spec: "최외각에 공격가능한 풍선이 없으면 게임 종료" →
        /// rail dart 가 즉시 발사 가능한 매칭만 검사. holder magazine 색상은 user가 누르기
        /// 전엔 발사 못 하므로 매칭 검사에 포함하지 않음.
        /// (2026-07-07 supply 규칙 도입 후: 이 함수는 '레일 위' 매칭 전용으로 유지 — 벨트 회전 판단
        ///  (RailManager)과 fail 의 1차 검사에 공용. 큐 공급 매칭은 QueueSupplyMatchesOutermost 가 담당.)
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

        // ROLLBACK_TARGETBOX_LIVE_COLOR_MASK_20260623:
        // BoardState does not keep a color mask per cell, but it should at least ignore TargetBox
        // cells when every authored egg has already been destroyed.
        private static bool HasAnyLiveEgg(BalloonData balloon)
        {
            if (balloon == null || balloon.eggHps == null) return false;
            for (int i = 0; i < balloon.eggHps.Length; i++)
            {
                if (balloon.eggHps[i] > 0)
                    return true;
            }
            return false;
        }

        // ROLLBACK_FAIL_CONTEXT_20260715: 실패 시 외곽 노출 색상 CSV("c3,c5") — analytics fail_outermost_colors 용(읽기 전용).
        public string GetOutermostColorsCsv()
        {
            var outer = GetOutermostBalloonColors();
            if (outer == null || outer.Count == 0) return "";
            var sb = new System.Text.StringBuilder();
            foreach (int c in outer) { if (sb.Length > 0) sb.Append(','); sb.Append('c').Append(c); }
            return sb.ToString();
        }

        private HashSet<int> GetOutermostBalloonColors()
        {
            // dirty 아니면 직전 계산 결과 그대로 반환 (매 프레임 비용 0)
            if (!_outermostDirty) return _reusableOutermostColors;
            _outermostDirty = false;
            // ROLLBACK_RAILSIDE_CACHE_EXPLICIT_INVALIDATE_20260707: 하부 boundary/occupancy 가 재빌드되면
            //   rail-side 색 캐시도 반드시 함께 무효화. 기존엔 GetRailSideOutermostBalloonColors 가 '진입 시점의
            //   _outermostDirty 관찰'로만 무효화를 판단했는데, dirty 를 이 함수의 다른 직접 호출자(IsOutermost/
            //   DumpAttackState 등)가 먼저 소비하면 rail-side 셋이 이전 상태(레벨 전환 직후엔 '이전 레벨'의 색)로
            //   조용히 잔존 — 그리고 stale 셋이 "매칭 없음"이면 발사 0 → pop 0 → 새 dirty 이벤트가 없어 영영
            //   자가회복 불가 → supplyMatch=false 로 stuck 1.5s 오탐 fail (클리어 후 다음 스테이지에서 한두 탭 뒤
            //   실패 재현, 2026-07-07 리포트). 에디터 전용 검증기(ValidateRailSideColorsCache)가 이 불일치를
            //   30프레임마다 self-heal 해와서 에디터에선 드물고 디바이스에서 지속되는 패턴과도 부합.
            //   재빌드 시점에 무효화를 걸면 dirty 소비 순서와 무관하게 정합 보장. 롤백: 아래 1줄 제거 +
            //   GetRailSideOutermostBalloonColors 의 outermostDirtyAtEntry 조건 복원.
            _railSideColorsValid = false;

            _reusableOutermostColors.Clear();
            if (!BalloonController.HasInstance) return _reusableOutermostColors;

            // ROLLBACK_ALIVE_BALLOON_ITERATION_20260616: GetAllBalloons()(729 고정) → GetAliveBalloons(살아있는 수만).
            //   팝마다 invalidate 되는 outermost 전수 빌드의 死엔트리 700+ skip 비용 제거. 롤백: 아래 2줄을
            //   `BalloonData[] allBalloons = ...GetAllBalloons(); ... allBalloons.Length` 로 환원.
            int aliveCount;
            BalloonData[] allBalloons = BalloonController.Instance.GetAliveBalloons(out aliveCount);
            if (allBalloons == null) return _reusableOutermostColors;

            if (GameManager.HasInstance)
                _cachedCellSpacing = GameManager.Instance.Board.cellSpacing;

            _reusableOccupancy.Clear();
            _reusablePositionMap.Clear();
            _reusableCellToBalloonId.Clear();
            _reusableOutermostBalloonIds.Clear();

            float cs = _cachedCellSpacing;
            // foreach → for index — IL2CPP 명시적 inline + array indexer 직접 사용
            for (int i = 0; i < aliveCount; i++)
            {
                var b = allBalloons[i];
                if (b == null || b.isPopped) continue; // [alive-only] now no-op guard, 동작 불변용 유지
                bool targetable = true;
                if (BalloonController.Instance.IsBalloonConcealed(b.balloonId)) targetable = false;
                if (b.gimmickType == BalloonController.GimmickWall) targetable = false;
                if (b.gimmickType == BalloonController.GimmickIce) targetable = false;
                if (b.iceOverlay > 0) targetable = false; // ROLLBACK_ICE_OVERLAY_LAYER_20260702: 얼음 덮인 기믹/셀 비타겟.
                if (b.gimmickType == BalloonController.GimmickColorCurtain) targetable = false;

                // ROLLBACK_BARRICADE_LENGTH_SEGMENTS_20260623:
                // Keep outermost-color/failure checks aligned with DirectionalTargeting. A
                // Barricade length value is the remaining attackable footprint along its direction.
                if (b.gimmickType == BalloonController.GimmickBarricade && b.barricadeLength > 1)
                {
                    int alongCount = BalloonController.GetBarricadeActiveLength(b);
                    if (alongCount <= 0) continue;
                    int bdir = ((b.barricadeDir % 4) + 4) % 4;
                    bool axisZ = bdir == 0 || bdir == 2;
                    int sign = (bdir == 0 || bdir == 1) ? 1 : -1;
                    BalloonController.Instance.GetRawLatticePhase(out float phX, out float phZ);
                    Vector2Int anchorCell = new Vector2Int(
                        Mathf.RoundToInt((b.position.x - phX) / cs),
                        Mathf.RoundToInt((b.position.z - phZ) / cs));

                    for (int a = 0; a < alongCount; a++)
                    {
                        for (int p = 0; p < 2; p++)
                        {
                            Vector2Int occupiedCell = axisZ
                                ? new Vector2Int(anchorCell.x + p, anchorCell.y + a * sign)
                                : new Vector2Int(anchorCell.x + a * sign, anchorCell.y + p);
                            _reusablePositionMap.Add(occupiedCell);
                            if (targetable)
                            {
                                _reusableOccupancy[occupiedCell] = b.color;
                                _reusableCellToBalloonId[occupiedCell] = b.balloonId;
                            }
                        }
                    }
                    continue;
                }

                // ROLLBACK_BARRICADE_MULTI_CELL_OCCUPANCY:
                // Keep outermost/match checks aligned with DirectionalTargeting's multi-cell sized gimmicks.
                if (BalloonController.IsSizedFieldGimmick(b.gimmickType) && (b.sizeW > 1 || b.sizeH > 1))
                {
                    int width = Mathf.Max(1, b.sizeW);
                    int height = Mathf.Max(1, b.sizeH);
                    // Target Box 알 모델: footprint 셀에 egg 색 분배(modulo N) — N(명시 egg 수)은 W*H 와 무관.
                    bool isEggBox = b.gimmickType == BalloonController.GimmickPinataBox
                        && b.eggColors != null && b.eggColors.Length > 0;
                    int firstLiveEggColor = b.color;
                    if (isEggBox && b.eggHps != null)
                    {
                        for (int egg = 0; egg < Mathf.Min(b.eggColors.Length, b.eggHps.Length); egg++)
                        {
                            if (b.eggHps[egg] <= 0) continue;
                            firstLiveEggColor = b.eggColors[egg];
                            break;
                        }
                    }
                    // [RAW_GRID_SPACE 2026-06-12] 멀티셀 footprint 는 원시 데이터 좌표(b.position) 기준 —
                    // 스케일 보드에서 보정 월드를 원시 spacing 으로 나누면 행/열이 합쳐지거나 어긋난다.
                    // [LATTICE_PHASE] 위상 기준 상대 라운딩 — DirectionalTargeting/DartManager 와 동일 키 공간.
                    BalloonController.Instance.GetRawLatticePhase(out float phX, out float phZ);
                    for (int dx = 0; dx < width; dx++)
                    {
                        for (int dz = 0; dz < height; dz++)
                        {
                            if (BalloonController.IsPinataPerCell(b))
                            {
                                int idx = dz * width + dx;
                                // ROLLBACK_WOODENBOARD_DEPLETED_CELL_SKIP_20260623:
                                // Match/fail checks must use the same live footprint as
                                // DirectionalTargeting, so consumed Wooden Board cells do not
                                // keep contributing blockers or colors.
                                if (idx < b.hitCount)
                                    continue;
                            }
                            Vector2Int occupiedCell = new Vector2Int(
                                Mathf.RoundToInt((b.position.x - phX + dx * cs) / cs),
                                Mathf.RoundToInt((b.position.z - phZ + dz * cs) / cs));
                            _reusablePositionMap.Add(occupiedCell);

                            int cellColor = b.color;
                            bool cellTargetable = targetable;
                            if (isEggBox)
                            {
                                // ROLLBACK_TARGETBOX_LIVE_COLOR_MASK_20260623:
                                // BoardState stores one color per cell; use the first live egg
                                // color so failure/outer-color checks do not keep dead egg colors.
                                cellColor = firstLiveEggColor;
                                cellTargetable = targetable && b.eggHps != null && HasAnyLiveEgg(b);
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
                // [RAW_GRID_SPACE 2026-06-12] FindTarget 과 동일하게 실제 위치를 쓰되, 역변환으로
                // 원시 보드 공간에 정규화 후 위상 기준 상대 라운딩 — 스케일/짝수 그리드 모두 정합.
                // (정적 풍선은 b.position 과 일치, 스포너/이동체는 실시간 위치 반영)
                // ROLLBACK_FLEXTUBE_FAILCHECK_LOGICAL_POS_20260707: FlexTube 는 논리 position 사용.
                //   배경: FlexTube 셀의 _balloonObjects 는 '곡선 위 재배치된 비주얼 파트'를 가리키고 여러 논리
                //   셀이 같은 파트를 공유(AttachPartToSeq) — 비주얼 위치로 cell 을 잡으면 fail 그리드에서 셀들이
                //   한 키로 뭉개지고 격자에서 어긋나, 공격 가능한 FlexTube 색이 rail-side 외곽 집합에서 빠져
                //   hasMatch=false 오탐 fail(또는 안쪽 풍선 오노출)이 난다. 타겟팅은 2026-06-28 에 동일 원인을
                //   고쳤으나(DirectionalTargeting ROLLBACK_FLEXTUBE_TARGETING_LOGICAL_POS_20260628) fail 판정에
                //   미포팅이었다. GetAdjustedBoardPosition↔WorldToRawBoardPosition 은 역변환 짝이라 결과적으로
                //   원시 논리 격자 셀에 정착 — 타겟팅과 동일 키 공간. 롤백: 아래 분기를 GetBalloonWorldPosition 단일 호출로 환원.
                Vector3 worldPos = b.gimmickType == BalloonController.GimmickFlexTube
                    ? BalloonController.Instance.GetAdjustedBoardPosition(b.position)
                    : BalloonController.Instance.GetBalloonWorldPosition(b.balloonId);
                Vector3 rawPos = BalloonController.Instance.WorldToRawBoardPosition(worldPos);
                BalloonController.Instance.GetRawLatticePhase(out float phaseX, out float phaseZ);
                Vector2Int cell = new Vector2Int(
                    Mathf.RoundToInt((rawPos.x - phaseX) / cs),
                    Mathf.RoundToInt((rawPos.z - phaseZ) / cs));

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
                        // ROLLBACK_TARGETBOX_ALL_LIVE_EGG_COLORS_20260707: 단일 색 대신 알 색 전체 수집 (헬퍼 주석 참조)
                        AddCellMatchColors(cell, occEn.Current.Value, _reusableOutermostColors);
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
            // ROLLBACK_RAILSIDE_CACHE_EXPLICIT_INVALIDATE_20260707: 'dirtyAtEntry 관찰' 폐기 — 재빌드가
            //   _railSideColorsValid 를 직접 무효화하므로(GetOutermostBalloonColors 참조) 여기선 valid 플래그와
            //   sideCount 키만 보면 된다. 다른 소비자가 dirty 를 먼저 소비해도 정합 유지.
            GetOutermostBalloonColors(); // boundary/occupancy 최신화 (재빌드 시 _railSideColorsValid=false 동반)

            int sideCount = RailManager.HasInstance
                ? RailManager.GetRailSideCount(RailManager.Instance.PhysicalCapacity)
                : -1; // RailManager 부재도 캐시 키에 포함 (부재 시 결과 = 빈 집합)

            if (_railSideColorsValid && sideCount == _railSideCachedSideCount)
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

        // ROLLBACK_TARGETBOX_ALL_LIVE_EGG_COLORS_20260707: 외곽 셀 색 수집 공용 헬퍼 — TargetBox(알 박스)는
        //   저장된 단일 색(첫 live egg 근사) 대신 '살아있는 알 색 전부'를 매칭 집합에 넣는다.
        //   배경: 타겟팅(DirectionalTargeting.BuildLiveEggColorMask, ROLLBACK_TARGETBOX_LIVE_COLOR_MASK_20260623)은
        //   각 점유 셀에 모든 live egg 색을 노출해 어느 색 다트든 조준 가능한데, fail 판정은 셀당 int 1개
        //   (_reusableOccupancy)라 첫 live egg 색만 남아 두 번째 색부터 매칭에서 누락 — 그 색만 레일에 있으면
        //   공격 가능한데 hasMatch=false → stuck 1.5s 오탐 fail. 점유 그리드 구조는 유지하고(지오메트리/차폐용
        //   색은 뭐든 무방) '외곽 색 수집' 시점에만 알 색 전체로 치환한다. 경계 셀 수만큼의 GetBalloon 조회는
        //   dirty 재빌드 시에만 발생. 롤백: 아래 3개 호출부를 output.Add(storedColor) 로 환원 + 이 헬퍼 제거.
        private void AddCellMatchColors(Vector2Int cell, int storedColor, HashSet<int> output)
        {
            if (BalloonController.HasInstance
                && _reusableCellToBalloonId.TryGetValue(cell, out int bid))
            {
                var b = BalloonController.Instance.GetBalloon(bid);
                if (b != null && b.gimmickType == BalloonController.GimmickPinataBox
                    && b.eggColors != null && b.eggColors.Length > 0 && b.eggHps != null)
                {
                    bool addedAny = false;
                    int count = Mathf.Min(b.eggColors.Length, b.eggHps.Length);
                    for (int i = 0; i < count; i++)
                    {
                        if (b.eggHps[i] <= 0) continue;
                        output.Add(b.eggColors[i]);
                        addedAny = true;
                    }
                    if (addedAny) return; // live egg 없으면 storedColor 폴백 (cellTargetable 가드가 이미 거르지만 방어)
                }
            }
            output.Add(storedColor);
        }

        private void AddColumnBoundaryColors(Dictionary<int, int> colToY, HashSet<int> output)
        {
            var en = colToY.GetEnumerator();
            try
            {
                while (en.MoveNext())
                {
                    Vector2Int cell = new Vector2Int(en.Current.Key, en.Current.Value);
                    if (_reusableOccupancy.TryGetValue(cell, out int color))
                        AddCellMatchColors(cell, color, output); // ROLLBACK_TARGETBOX_ALL_LIVE_EGG_COLORS_20260707
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
                        AddCellMatchColors(cell, color, output); // ROLLBACK_TARGETBOX_ALL_LIVE_EGG_COLORS_20260707
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

#if BF_RAIL_HOLDER
        /// <summary>
        /// PROTO_RAIL_HOLDER_20260716: 레일 홀더 모드 실패 판정 — '총 탄약 소진'.
        ///
        /// 기존 RailOverflow(레일이 다트로 차서 실패)를 대체한다. 레일 위 홀더는 소모되지 않으므로
        /// 점유는 항상 N 고정 → 압력계가 될 수 없다. 대신 탄약이 유한 자원이고, 다 쓰면 끝난다.
        ///
        /// 실패 = ① 레일 위 상자 탄창 + 큐 탄창 = 0  ② 비행 중 투사체 없음  ③ 풍선 잔존.
        ///   ②가 없으면 마지막 발이 날아가는 중에 실패가 먼저 떠서 클리어를 뺏는다.
        /// grace(failGraceDelay)는 기존과 동일하게 적용 — 마지막 팝 연출/체인 반응이 끝날 시간을 준다.
        /// </summary>
        private void EvaluateRailHolderAmmoFail(float evalDelta)
        {
            if (_currentState != BoardState.Playing) return;

            if (!RailHolderController.HasInstance || !BalloonController.HasInstance)
            {
                _isCritical = false;
                _criticalTimer = 0f;
                return;
            }

            BalloonController.Instance.GetAliveBalloons(out int aliveCount);
            bool balloonsRemain = aliveCount > 0;
            bool ammoGone = RailHolderController.Instance.TotalRemainingAmmo <= 0;
            bool projectilesInFlight = DartManager.HasInstance && DartManager.Instance.HasActiveProjectiles;

            // Step1 착지열: 레일 홀더가 한 바퀴 완주 복귀하려는데 착지열이 만석 → 즉시 실패 사유(공간 초과, RailOverflow 부활).
            //   탄약 소진(NoMovesLeft)과 별개 축. 풍선이 남아 있을 때만 실패(다 터졌으면 클리어가 우선).
            bool landingOverflow = RailHolderController.Instance.LandingOverflowFailPending;

            bool doomed = balloonsRemain
                && (landingOverflow || (ammoGone && !projectilesInFlight));
            if (!doomed)
            {
                _isCritical = false;
                _criticalTimer = 0f;
                return;
            }

            _isCritical = true;
            _criticalTimer += evalDelta;
            if (_criticalTimer >= _failGraceDelay)
                TriggerFail(landingOverflow ? FailReason.RailOverflow : FailReason.NoMovesLeft);
        }
#endif

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
            _lastDrainUnscaledTime = Time.unscaledTime; // ROLLBACK_NO_DRAINAGE_FAIL_WATCHDOG_20260622: 이어하기 직후 즉시 발동 방지
            _watchdogGateOpenAccum = 0f;                // ROLLBACK_WATCHDOG_GATE_DWELL_20260707 rev2: 이어하기 후 누적 리셋
            _watchdogGateClosedSince = float.NaN;

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
