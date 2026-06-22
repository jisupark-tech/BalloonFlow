using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using DG.Tweening;

namespace BalloonFlow
{
    /// <summary>
    /// Manages darts that reside on rail slots and auto-fire at matching balloons.
    /// Rail Overflow mode: darts are fixed to conveyor belt slots, rotate with the belt,
    /// and fire straight inward when passing a matching-color outermost balloon.
    /// Slot is freed immediately on fire (before projectile reaches target).
    /// </summary>
    /// <remarks>
    /// Layer: Domain | Genre: Puzzle | Role: Manager | Phase: 1
    /// DB Reference: Generated from Rail Overflow spec — slot-based dart system
    /// </remarks>
    public class DartManager : SceneSingleton<DartManager>
    {
        #region Constants

        private const string DART_POOL_KEY = "Dart";
        // [ROLLBACK_DART_FLIGHT_TIME_TO_SPEED_MULT]
        // 비행 속도 결정을 시간(초)→배수로 변경. 롤백 시 아래 상수 + 기존 CalculateProjectileFlightTime 복원.
        // private const float DEFAULT_PROJECTILE_FLIGHT_TIME = 0.1f;
        // private const float PROJECTILE_FLIGHT_SPEED_MULTIPLIER = 12f; // 3.3 × 1.8 × 1.8 (80% 추가 증가)
        // private const float PROJECTILE_MIN_FLIGHT_TIME_SCALE = 0.3f;
        // private const float PROJECTILE_MAX_FLIGHT_TIME_SCALE = 20f;
        private const float PROJECTILE_MIN_FLIGHT_TIME = 0.015f;
        private const float PROJECTILE_MAX_FLIGHT_TIME = 5f;
        private const float DEFAULT_DART_FLIGHT_SPEED_MULTIPLIER = 66f;
        private const int ADJACENT_EMPTY_LINE_RESCUE_RADIUS = 1;
        #endregion

        #region Serialized Fields

        // [ROLLBACK_DART_FLIGHT_TIME_TO_SPEED_MULT]
        // 비행 시간 → 배수 전환. dartFlightSpeedMultiplier (GameManager) 가 단일 source.
        // [SerializeField] private float _projectileFlightTime = DEFAULT_PROJECTILE_FLIGHT_TIME;

        [Tooltip("다트 포물선 곡사 높이. 0=직선, >0=곡사. Design ref: 피드백디렉션 §다트궤적")]
        [SerializeField] private float _arcHeight = 0f; // 0 = 직사, >0 = 곡사

        // [ROLLBACK_DART_FLIGHT_TIME_TO_SPEED_MULT]
        // FlightTime / EffectiveFlightTime property — dartFlightTime 기반 dead code.
        // private float FlightTime => GameManager.HasInstance ? GameManager.Instance.Board.dartFlightTime : _projectileFlightTime;
        // private float EffectiveFlightTime
        // {
        //     get
        //     {
        //         float t = FlightTime;
        //         float mult = RailManager.HasInstance ? RailManager.Instance.UserSpeedMultiplier : 1f;
        //         if (mult > 0.001f) t /= mult;
        //         return t;
        //     }
        // }

        // [ROLLBACK_DART_FLIGHT_TIME_TO_SPEED_MULT]
        // 새 식: duration = distance / (cellSpacing × multiplier × userSpeedMult).
        // multiplier 단위는 "셀/초" — 1=한 cell 통과 1초, 10=0.1초. 직관적.
        // 기존 식(시간 기반 + min/max scale clamp)은 위 ROLLBACK 주석 + 상수 해제로 복원.
        private float CalculateProjectileFlightTime(Vector3 from, Vector3 to)
        {
            // [이미지 속도 프로파일 / ROLLBACK_DART_FLIGHT_VELOCITY_RAMP_20260608]
            // 이전: 등속 — duration = distance / (cellSpacing × mult × userSpeed × almostThere).
            // 변경: 속도(mult)가 start(dartFlightSpeedMultiplier=66) → DART_FLIGHT_MAX_MULT(120) 로 0.4초간 선형 가속 후 일정.
            //   distance 를 이 프로파일로 덮는 시간을 역함수(DartRampTimeForUnits)로 산출. userSpeed/almostThere 는 필요 이동량 q 를 줄여 가속.
            float startMult = GameManager.HasInstance ? GameManager.Instance.Board.dartFlightSpeedMultiplier : DEFAULT_DART_FLIGHT_SPEED_MULTIPLIER;
            // ROLLBACK_DART_DEADLOCK_FLIGHT_FALLBACK_20260616:
            // 0/NaN flight multipliers create very long or invalid projectiles. Those keep
            // _activeProjectiles > 0, so the stall watchdog never clears stale scan/line locks.
            // Rollback: change fallback to 1f if slow debug projectile travel is required.
            if (!IsFinite(startMult) || startMult <= 0.001f) startMult = DEFAULT_DART_FLIGHT_SPEED_MULTIPLIER;

            float userSpeed = RailManager.HasInstance ? RailManager.Instance.UserSpeedMultiplier : 1f;
            if (!IsFinite(userSpeed) || userSpeed <= 0.001f) userSpeed = 1f;
            float almostThere = RailManager.HasInstance ? RailManager.Instance.GetOccupancySpeedMultiplier() : 1f;
            if (!IsFinite(almostThere) || almostThere <= 0.001f) almostThere = 1f;
            float scalars = userSpeed * almostThere;

            float cellSpacing = GameManager.HasInstance ? GameManager.Instance.Board.cellSpacing : 0.55f;
            if (!IsFinite(cellSpacing) || cellSpacing <= 0.01f) cellSpacing = 0.55f;

            Vector3 flatFrom = new Vector3(from.x, 0f, from.z);
            Vector3 flatTo = new Vector3(to.x, 0f, to.z);
            float distance = Vector3.Distance(flatFrom, flatTo);
            if (!IsFinite(distance)) distance = cellSpacing;

            float q = distance / (cellSpacing * scalars);          // 덮어야 할 누적 mult-units
            float duration = DartRampTimeForUnits(q, startMult);
            if (!IsFinite(duration) || duration <= 0f) duration = PROJECTILE_MIN_FLIGHT_TIME;
            return Mathf.Clamp(duration, PROJECTILE_MIN_FLIGHT_TIME, PROJECTILE_MAX_FLIGHT_TIME);
        }

        // [이미지 속도 프로파일] 속도(mult): start → MAX 로 RAMP_TIME 선형 가속 후 일정.
        private const float DART_FLIGHT_RAMP_TIME = 0.285f;   // 가속 구간(초)
        private const float DART_FLIGHT_MAX_MULT  = 150f;   // 천장(이전 ~100 → 120). 시작 = dartFlightSpeedMultiplier(66)

        /// <summary>속도가 startMult→MAX 로 RAMP_TIME 선형가속 후 일정일 때 0..t 누적 이동량(∫mult dt). cellSpacing/스칼라 제외(비율에서 상쇄).</summary>
        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }

        private static float DartRampDistanceUnits(float t, float startMult)
        {
            if (t <= 0f) return 0f;
            float maxMult = DART_FLIGHT_MAX_MULT;
            if (startMult >= maxMult) return startMult * t;                       // 이미 천장이면 등속
            float slope = (maxMult - startMult) / DART_FLIGHT_RAMP_TIME;
            if (t <= DART_FLIGHT_RAMP_TIME) return startMult * t + 0.5f * slope * t * t;
            float atRamp = startMult * DART_FLIGHT_RAMP_TIME + 0.5f * slope * DART_FLIGHT_RAMP_TIME * DART_FLIGHT_RAMP_TIME;
            return atRamp + maxMult * (t - DART_FLIGHT_RAMP_TIME);
        }

        /// <summary>DartRampDistanceUnits 역함수 — 누적 이동량 q 도달 시간(duration 산출용).</summary>
        private static float DartRampTimeForUnits(float q, float startMult)
        {
            if (q <= 0f) return 0f;
            float maxMult = DART_FLIGHT_MAX_MULT;
            if (startMult >= maxMult) return q / Mathf.Max(0.001f, startMult);
            float slope = (maxMult - startMult) / DART_FLIGHT_RAMP_TIME;
            float atRamp = startMult * DART_FLIGHT_RAMP_TIME + 0.5f * slope * DART_FLIGHT_RAMP_TIME * DART_FLIGHT_RAMP_TIME;
            if (q <= atRamp)
            {
                // 0.5*slope*t² + startMult*t - q = 0
                float a = 0.5f * slope, b = startMult;
                float disc = b * b + 4f * a * q;
                return (-b + Mathf.Sqrt(Mathf.Max(0f, disc))) / (2f * a);
            }
            return DART_FLIGHT_RAMP_TIME + (q - atRamp) / maxMult;
        }

        // [ROLLBACK_DART_FX_TRAIL]
        // 다트 prefab 안 자식 FXDartTrail 활성/비활성 토글.
        // 레일 위(spawn) = false, 발사 시 = true. 롤백 시 이 메서드 + 모든 호출처 제거.
        private static void SetDartTrailActive(GameObject dartObj, bool active)
        {
            if (dartObj == null) return;
            var trail = dartObj.transform.Find("FXDartTrail");
            if (trail != null && trail.gameObject.activeSelf != active)
                trail.gameObject.SetActive(active);
        }

        #endregion

        #region Nested Types

        /// <summary>
        /// Visual representation of a dart sitting on a rail slot.
        /// </summary>
        private class SlotDartVisual
        {
            public int slotIndex;
            public int color;
            public GameObject gameObject;
            public Vector3 baseScale;  // Cave 스케일 복원용
        }

        /// <summary>
        /// In-flight projectile after a slot dart fires at a balloon.
        /// </summary>
        private class DartProjectile
        {
            public GameObject gameObject;
            // ROLLBACK_DART_PROJECTILE_MANUAL_MOVE:
            // Remove startPosition and restore DOMove in FireDartCandidate to return projectile
            // visuals to DOTween-driven movement.
            public Vector3 startPosition;
            public Vector3 targetPosition;
            public int targetBalloonId;
            public int color;
            public DirectionalTargeting.ScanDirection scanDir;
            public int scanLine;
            public float elapsed;
            public float duration;
            // ROLLBACK_DART_NEEDLE_TIP_IMPACT:
            // Remove impactTime/impactResolved and resolve only at duration to restore center-hit timing.
            public float impactTime;
            public bool impactResolved;
            public Vector3 startScale;
            public Vector3 targetScale;

            // Launch punch (발사 직후 짧은 스케일 펀치): startScale → punchPeakScale → startScale, 이후 lerp.
            public float launchPunchT;       // 펀치 피크 시각(초). 0이면 punch 비활성.
            public Vector3 punchPeakScale;
            public float punchDuration;
            public float lerpStrength;       // Punch 종료 후 비행 보간 강도(0~1) — 풍선 사이즈 도달 제한용.
        }

        private struct DartFireCandidate
        {
            public bool isValid;
            public RailManager.DartOnRail dart;
            public Vector3 dartPos;
            public Vector3 scanDartPos;
            public Vector3 fireDir;
            public int holderId;
            public int dartId;
            public int color;
            public int targetId;
            public int scanLine;
            public DirectionalTargeting.ScanDirection scanDir;
            public Vector3 selectedTargetPos;
            public string findTargetDiag;
            // ROLLBACK_DART_COMMIT_INDEPENDENT_LINES_20260617:
            // Normal scan candidates must still be the current cluster head at commit time.
            // Dead-head relief intentionally fires a later dart when the visible head color cannot
            // ever hit the current contour, so that path opts out of the head-only commit check.
            public bool allowNonHeadCommit;
        }

        #endregion

        #region Fields

        private readonly Dictionary<int, SlotDartVisual> _slotVisuals = new Dictionary<int, SlotDartVisual>();
        private readonly Dictionary<int, SlotDartVisual> _dartVisuals = new Dictionary<int, SlotDartVisual>();
        private readonly List<DartProjectile> _activeProjectiles = new List<DartProjectile>();
        private readonly Stack<DartProjectile> _projectilePool = new Stack<DartProjectile>(32);
        // ROLLBACK_DART_RENDERER_LIST_CACHE:
        // Restore GetComponentsInChildren<Renderer>() array fallback in ApplyColor if this causes
        // prefab-specific renderer assignment issues.
        private readonly List<Renderer> _applyColorRendererCache = new List<Renderer>(8);
        // ROLLBACK_DART_PROJECTILE_MANUAL_MOVE:
        // Shared scratch list avoids Renderer[] allocation when resolving needle-tip lead from
        // prefab renderers. Remove this and restore GetComponentsInChildren<Renderer>() fallback
        // if the manual projectile optimization is rolled back.
        private static readonly List<Renderer> _needleLeadRendererCache = new List<Renderer>(8);
        // ROLLBACK_DART_IDENTIFIER_CACHE:
        // Remove this cache and call dartObj.GetComponent<DartIdentifier>() directly in
        // GetNeedleTipLead if prefab components are added/removed dynamically at runtime. The cache
        // only avoids repeated component lookups during fire bursts; it does not change impact math.
        private static readonly Dictionary<int, DartIdentifier> _dartIdentifierCache =
            new Dictionary<int, DartIdentifier>(64);
        private readonly List<RailManager.DartOnRail> _scanHeadDarts = new List<RailManager.DartOnRail>(16);
        private readonly List<DartFireCandidate> _fireCandidates = new List<DartFireCandidate>(16);
        // DEAD_HEAD_RELIEF: 릴리프 평가 시점의 레일면 외곽 색 스냅샷 + holder 별 타이머 (GC 방지 재사용).
        private readonly HashSet<int> _deadHeadReachableColors = new HashSet<int>(16);
        private readonly Dictionary<int, float> _deadHeadSince = new Dictionary<int, float>(8);
        private readonly Dictionary<int, float> _deadHeadLastReliefFireAt = new Dictionary<int, float>(8);
        private static readonly System.Comparison<RailManager.DartOnRail> CompareDartPlacedSeq =
            (a, b) => a.placedSeq.CompareTo(b.placedSeq);
        private readonly Dictionary<int, int> _lastScannedLineByHolder = new Dictionary<int, int>(16);
        private readonly Dictionary<int, DirectionalTargeting.ScanDirection> _lastScanDirectionByHolder =
            new Dictionary<int, DirectionalTargeting.ScanDirection>(16);
        private readonly Dictionary<int, int> _lastScannedHeadIdByHolder = new Dictionary<int, int>(16);
        // ROLLBACK_DART_CONSUMED_LINE_LOCK:
        // _lastScanned* is head-specific so a promoted head can still catch up. That means it cannot
        // stop the next promoted head from peeling the same row/column after the previous projectile
        // pops. Keep the last fired line per holder independent of head id and skip only that exact
        // holder+direction+line.
        private readonly Dictionary<int, int> _lastFiredLineByHolder = new Dictionary<int, int>(16);
        private readonly Dictionary<int, DirectionalTargeting.ScanDirection> _lastFiredDirectionByHolder =
            new Dictionary<int, DirectionalTargeting.ScanDirection>(16);
        // ROLLBACK_DART_HOLDER_PASS_LINE_SET:
        // The previous holder-local lock remembered only one last-fired line. When the same holder
        // fired line -13, then -12, the -13 lock was overwritten and the holder could peel -13 again
        // a few frames later. Keep every fired line for the current side pass and clear it only when
        // the holder changes scan direction at a corner/tunnel turn.
        private readonly Dictionary<int, HashSet<int>> _holderPassLinesByHolder = new Dictionary<int, HashSet<int>>(16);
        private readonly Dictionary<int, DirectionalTargeting.ScanDirection> _holderPassDirectionByHolder =
            new Dictionary<int, DirectionalTargeting.ScanDirection>(16);
        // ROLLBACK_DART_STUCK_HOLDER_LINE_RELIEF:
        // holder 가 '자기 pass lock 에 막혀' 발사 못 한 채로 머문 시작 시각(unscaled). 임계 초과 시
        // 그 라인 lock 해제 (RelieveStuckHolderLineLocks). 발사 성공/비-막힘 시 제거.
        private readonly Dictionary<int, float> _holderLineStuckSince = new Dictionary<int, float>(16);
        // ROLLBACK_DART_STUCK_LINE_KEYED_RELIEF:
        // Remove these two dictionaries and restore holder-only _holderLineStuckSince usage if this
        // rescue should again ignore scanDir/line changes. The current fix keeps the stuck timer tied
        // to the exact holder+direction+line so a moving head cannot inherit stale stuck time from a
        // previous rail line and repeatedly clear the wrong lock.
        private readonly Dictionary<int, DirectionalTargeting.ScanDirection> _holderLineStuckDirection =
            new Dictionary<int, DirectionalTargeting.ScanDirection>(16);
        private readonly Dictionary<int, int> _holderLineStuckLine = new Dictionary<int, int>(16);
        // 막힌 채 이 시간(초) 이상 지나면 그 라인만 잠금 해제 — 연속공격 방지 간격 + 놓침 방지.
        private const float HOLDER_LINE_STUCK_RESET_SECONDS = 0.4f;
        // ROLLBACK_DART_STRAIGHT_RAIL_PROGRESS_WRAP:
        // ㅡ자(개방 단면) 레일은 끝→시작으로 순간이동(wrap)할 때 holder pass lock 을 풀어야 다음 pass 에서
        // 같은 컬럼을 다시 공격할 수 있다. 기존 line-delta(6) 임계는 보드 컬럼 폭이 6 이하인 좁은 ㅡ 레벨에서
        // 영원히 충족되지 않아 lock 이 영구 지속 → 발사 정지 → rail 가득 → 전체 정지(데드락) 발생.
        // head 의 rail progress 변화로 wrap 을 판정해 보드 폭과 무관하게 lock 을 해제한다.
        private readonly Dictionary<int, float> _lastStraightHeadProgressByHolder = new Dictionary<int, float>(16);
        // ROLLBACK_DART_GLOBAL_CONSUMED_LINE_LOCK:
        // A rail line is a gameplay surface, not a holder-local resource. If holder A pops the
        // outer cell on (direction,line), holder B must not immediately peel the newly exposed
        // inner cell on that same surface. Keep it only while a current head is still on that
        // side/line; once all heads leave, the refreshed outer contour may be attacked next pass.
        private readonly HashSet<int> _consumedTargetLines = new HashSet<int>();
        // ROLLBACK_DART_OUTER_PASS_LINE_LOCK:
        // Global line locks must survive until the fired projectile resolves. Releasing when the
        // promoted owner head leaves the line is too early: the balloon has not popped yet, so the
        // same line is open again when the hit finally refreshes the live contour. After resolution,
        // release only once no current head is still on that side/line.
        private readonly HashSet<int> _unresolvedConsumedTargetLines = new HashSet<int>();
        private readonly HashSet<int> _currentHeadLineKeys = new HashSet<int>();

        // ROLLBACK_PROMOTION_SEED_CATCH_UP:
        // After RemoveDartById, RailManager promotes the next dart in that cluster as the new head
        // synchronously. The next ScanAndFirePerDart tick sees the new head, but the existing
        // _lastScannedHeadIdByHolder still references the removed head's dartId, so the catch-up
        // clause is not entered (`lastHeadId == dart.dartId` fails). At 2x speed + corner/tunnel
        // bursts, the new head can cross multiple exact lines in one frame and they are all skipped
        // because catchUpCount stays at 1. Maintain a per-holder promotion seed so the first scan
        // after head replacement can replay lines crossed between promotion time and now without
        // touching _lastScanned* (which preserves ROLLBACK_DART_CACHE_ONLY_ON_CANDIDATE).
        //
        // Lifecycle:
        //   - Set: in FireDartCandidate after a successful RemoveDartById (per holder).
        //   - Consumed: in ScanAndFirePerDart when catch-up uses it (single-use baseline).
        //   - Cleared: in InvalidateDartScanLines / InvalidateDartScanLineForHolder / ClearAllDarts.
        // ROLLBACK_DART_POP_SCAN_STATE_STABILITY:
        //   Pop/projectile resolution must not call InvalidateDartScanLines. That helper clears this
        //   promotion seed, so a just-promoted head loses the crossed-line replay it needs at x2 speed.
        private readonly Dictionary<int, int> _promoLineByHolder = new Dictionary<int, int>(16);
        private readonly Dictionary<int, DirectionalTargeting.ScanDirection> _promoDirByHolder =
            new Dictionary<int, DirectionalTargeting.ScanDirection>(16);
        private readonly Dictionary<int, int> _promoHeadByHolder = new Dictionary<int, int>(16);

        /// <summary>
        /// Balloon IDs currently targeted by in-flight projectiles.
        /// Prevents multiple darts from firing at the same balloon.
        /// Cleared when projectile hits or balloon is popped externally.
        /// </summary>
        private readonly HashSet<int> _reservedTargets = new HashSet<int>();
        // scan tick 안 이미 발사한 holder ID set. 같은 holder 의 다음 head (cache 자동 갱신 후) 가 같은 tick 발사하는 shotgun 차단.
        private readonly HashSet<int> _firedHoldersThisTick = new HashSet<int>();
        // ROLLBACK_DART_FRONT_ORDERED_FIRE_QUEUE:
        // Remove this set and restore FireNewlyPromotedHeadIfReady immediate firing if post-fire
        // promoted heads must again bypass rail front-to-back ordering. Keeping it as a one-shot
        // guard preserves the old max-one promoted-head rescan per holder per scan tick.
        private readonly HashSet<int> _postFireQueuedHoldersThisTick = new HashSet<int>();
        // ROLLBACK_DART_DEPLOY_FRAME_PROMOTED_FIRE_GUARD:
        // Remove this frame stamp and the TryQueuePromotedHeadFireCandidate guard if deployed darts
        // must be allowed to fire a promoted head again in the exact same frame. Keeping the guard
        // narrow avoids the visible deploy-time double shot without disabling normal x2 post-fire
        // catch-up on later frames.
        private readonly Dictionary<int, int> _lastDeployPlacedFrameByHolder = new Dictionary<int, int>(16);
        // ROLLBACK_DART_STABLE_OUTER_HIT:
        // Heavy targeting diagnostics were useful while isolating penetration/miss cases, but
        // they allocate and format large strings for every fire/pop. Keep them opt-in.
#if BALLOONFLOW_DART_TARGETING_DEBUG
        private static readonly bool DART_TARGETING_DEBUG = true;
#else
        private static readonly bool DART_TARGETING_DEBUG = false;
#endif
        // ROLLBACK_DART_MISS_SUSPECT_DIAG:
        // Debug.Log itself causes visible frame drops during dense firing. Keep this off for play,
        // and enable only while capturing a short miss sample.
#if BALLOONFLOW_DART_MISS_SUSPECT_DEBUG
        private static readonly bool DART_MISS_SUSPECT_DEBUG = true;
#else
        // [2026-06-12] wrap pass-lock 진단 캡처 종료 — false 복귀 (Debug.Log 부하로 플레이 빌드 방치 금지).
        private static readonly bool DART_MISS_SUSPECT_DEBUG = false;
#endif
        // ROLLBACK_DART_ATTACK_ISSUE_DEBUG:
        // Temporary, throttled diagnostics for continuous-fire and miss paths. Disable this after
        // capturing a repro sample; every branch below is intentionally log-only except the matching
        // holder-line guard inside FireDartCandidate.
#if BALLOONFLOW_DART_ATTACK_ISSUE_DEBUG
        private static readonly bool DART_ATTACK_ISSUE_DEBUG = true;
#else
        // [2026-06-10 perf] DBG-TopLeft 캡쳐 종료 — false 복귀. true 방치로 매 프레임 Debug.Log(스택트레이스+IO)가
        // 상시 부하를 만들고 있었음 (Zap 미사용 시에도 인게임 프레임 드랍 원인).
        private static readonly bool DART_ATTACK_ISSUE_DEBUG = false;
#endif

        // [2026-06-10 perf] DBG-TopLeft 캡쳐 종료 — false 복귀.
        private static readonly bool DBG_TOPLEFT_DUMP = false;

        // ROLLBACK_DART_CROSSED_LINE_CACHE_FIX:
        // At x2 speed or after a long frame, a head can cross several grid lines before this scan
        // runs. Keep this exact-line budget wide enough to replay skipped lines without falling
        // back to adjacent-line targeting.
        private const int MAX_LINE_CATCH_UP_PER_HEAD = 6;
        // ROLLBACK_DART_OPEN_RAIL_WRAP_LOCK_RESET:
        // Remove this constant and restore MaxLineCatchUpPerHead in the wrap checks if pass locks
        // must again survive large backward line jumps. Catch-up budget scales with x2 speed, but
        // wrap detection must stay fixed; otherwise a straight/L rail can wrap from +5 to -5 while
        // the old holderLineConsumed locks remain active and every visible target line is skipped.
        private const int OPEN_RAIL_WRAP_RESET_LINE_DELTA = MAX_LINE_CATCH_UP_PER_HEAD;
        // ROLLBACK_DART_SPEED_SCALED_CATCH_UP:
        // At x2 and late-game acceleration, a head can cross more exact grid lines per frame than
        // the base catch-up budget. Scale the replay budget by the user speed multiplier while
        // keeping a hard cap so targeting work remains bounded.
        private int MaxLineCatchUpPerHead
        {
            get
            {
                float mult = RailManager.HasInstance ? RailManager.Instance.UserSpeedMultiplier : 1f;
                int scaled = Mathf.CeilToInt(MAX_LINE_CATCH_UP_PER_HEAD * Mathf.Max(1f, mult));
                return Mathf.Clamp(scaled, MAX_LINE_CATCH_UP_PER_HEAD, 24);
            }
        }
        // ROLLBACK_DART_POST_FIRE_HEAD_RESCAN:
        // When a head fires, RailManager immediately promotes the next dart in that holder. At x2
        // speed waiting until the next frame lets that new head pass one exact line. Re-scan only
        // that holder's newly promoted head, with a tiny cap, so this does not become free-fire.
        private const int MAX_POST_FIRE_HEAD_RESCANS_PER_HOLDER = 1;
        // [ROLLBACK_POST_FIRE_RESCAN_X1] 1.01 → 0.0 — X1 모드(default) 에서도 promoted head 동일 tick rescan 활성.
        // 이전: X2 (UserSpeedMultiplier >= 1.01) 에서만 rescan. X1 에서는 한 holder 한 tick 1발 제한 → 외곽 풍선 놓침 발생.
        // 변경 후: 항상 rescan — 같은 holder 의 promoted head 가 같은 tick 안 발사 후보로 큐잉됨.
        // 롤백 시 1.01f 로 원복.
        private const float POST_FIRE_HEAD_RESCAN_MIN_SPEED = 0.0f;
        private const int MAX_MISS_SUSPECT_LOGS_PER_FRAME = 1;
        private const int MAX_ATTACK_ISSUE_LOGS_PER_FRAME = 8;
        private int _lastMissSuspectLogFrame = -1;
        private int _missSuspectLogsThisFrame;
        private int _lastAttackIssueLogFrame = -1;
        private int _attackIssueLogsThisFrame;
        private int _lastFiredHolderId = -1;

        private int MAX_FIRES_PER_FRAME => GameManager.HasInstance ? GameManager.Instance.Board.maxFiresPerFrame : 1;

        /// <summary>When true, board is cleared or failed — stop all scanning/firing.</summary>
        private bool _boardFinished;

        #endregion

        #region Lifecycle

        protected override void OnSingletonAwake()
        {
            // [ROLLBACK_DART_FLIGHT_TIME_TO_SPEED_MULT]
            // _projectileFlightTime sync — dartFlightTime 제거로 dead code. 새 흐름: CalculateProjectileFlightTime 가 매 호출 시 multiplier 동적 참조.
            // if (GameManager.HasInstance)
            // {
            //     _projectileFlightTime = GameManager.Instance.Board.dartFlightTime;
            // }
        }

        /// <summary>Frozen dart visuals: pinned at world position, don't move with belt.</summary>
        private readonly Dictionary<int, GameObject> _frozenVisuals = new Dictionary<int, GameObject>();

        private void OnEnable()
        {
            EventBus.Subscribe<OnDartPlacedOnSlot>(HandleDartPlaced);
            EventBus.Subscribe<OnDartPlaced>(HandleDartPlacedPerDart);
            EventBus.Subscribe<OnDartFrozen>(HandleDartFrozen);
            EventBus.Subscribe<OnDartsFrozenCleared>(HandleDartsFrozenCleared);
            EventBus.Subscribe<OnBalloonPopped>(HandleBalloonPopped);
            EventBus.Subscribe<OnBoardCleared>(HandleBoardCleared);
            EventBus.Subscribe<OnBoardFailed>(HandleBoardFailed);
            EventBus.Subscribe<OnContinueApplied>(HandleContinueApplied);
        }

        private void OnDisable()
        {
            EventBus.Unsubscribe<OnDartPlacedOnSlot>(HandleDartPlaced);
            EventBus.Unsubscribe<OnDartPlaced>(HandleDartPlacedPerDart);
            EventBus.Unsubscribe<OnDartFrozen>(HandleDartFrozen);
            EventBus.Unsubscribe<OnDartsFrozenCleared>(HandleDartsFrozenCleared);
            EventBus.Unsubscribe<OnBalloonPopped>(HandleBalloonPopped);
            EventBus.Unsubscribe<OnBoardCleared>(HandleBoardCleared);
            EventBus.Unsubscribe<OnBoardFailed>(HandleBoardFailed);
            EventBus.Unsubscribe<OnContinueApplied>(HandleContinueApplied);
        }

        /// <summary>공격 스캔 주기 타이머 (dartFireInterval 기반)</summary>
        private void Update()
        {
            var __sw = InGamePerfLogger.StartSection();
            try
            {
            if (_boardFinished) return;
            // [FAIL_DERAIL 2026-06-12] 이어하기 복귀 연출 중 — 위치 동기가 복귀 lerp 를 덮어쓰지 않게
            // 동기/스캔/발사 전체를 1프레임 단위로 보류 (복귀 코루틴이 끝나면 자동 재개).
            if (_continueRestoreActive) return;

            var __slotSw = InGamePerfLogger.StartSection();
            UpdateSlotDartPositions();
            InGamePerfLogger.EndSection(__slotSw, "Dart.UpdateSlotDartPositions");

            var __perDartSw = InGamePerfLogger.StartSection();
            UpdatePerDartPositions();
            InGamePerfLogger.EndSection(__perDartSw, "Dart.UpdatePerDartPositions");

            // ROLLBACK_DART_PROJECTILE_RESOLVE_BEFORE_SCAN:
            // Resolve completed projectiles before scanning new heads. Otherwise a target that should
            // pop this frame is still in _reservedTargets and the contour cache is stale during scan.
            var __projectileSw = InGamePerfLogger.StartSection();
            UpdateProjectiles();
            InGamePerfLogger.EndSection(__projectileSw, "Dart.UpdateProjectiles");

            // 셀 한 칸당 1회 스캔 (cellSpacing/dartSpeed 인터벌). MAX 제약은 제거 — 한 틱당 모든 ready 다트 발사.
            // (매 프레임 호출하면 N*M FindTarget 으로 부하 심함. timer로 ~60% 감소, 발사 정확도는 동일.)
            // ROLLBACK_DART_LINE_DRIVEN_SCAN:
            // Scan head darts by row/column changes, not by a frame-sampled timer. This reduces
            // targeting work on frames where heads stay on the same line and avoids missing attack
            // lines when a long frame moves a dart across more than one grid line.
            var __scanSw = InGamePerfLogger.StartSection();
            ScanAndFirePerDart();
            InGamePerfLogger.EndSection(__scanSw, "Dart.ScanAndFirePerDart");
            }
            finally { InGamePerfLogger.EndSection(__sw, "DartManager.Update"); }
        }

        #endregion

        #region Public Methods

        /// <summary>
        /// Clears all dart visuals and projectiles.
        /// </summary>
        public void ClearAllDarts()
        {
            // [FAIL_DERAIL 2026-06-12] 재시작/레벨 이동 정리 — 탈선/복귀 연출 상태 리셋.
            ResetFailScatterState();

            // Dictionary 순회 전 키를 복사 (순회 중 변경 방지)
            _tempRemoveKeys.Clear();
            foreach (var kvp in _slotVisuals)
                _tempRemoveKeys.Add(kvp.Key);
            for (int i = 0; i < _tempRemoveKeys.Count; i++)
            {
                if (_slotVisuals.TryGetValue(_tempRemoveKeys[i], out var visual))
                    ReturnDartToPool(visual.gameObject);
            }
            _slotVisuals.Clear();

            foreach (var kvp in _dartVisuals)
                ReturnDartToPool(kvp.Value.gameObject);
            _dartVisuals.Clear();

            for (int i = _activeProjectiles.Count - 1; i >= 0; i--)
            {
                if (i < _activeProjectiles.Count)
                {
                    ReturnDartToPool(_activeProjectiles[i].gameObject);
                    ReleaseProjectile(_activeProjectiles[i]);
                }
            }
            _activeProjectiles.Clear();
            _reservedTargets.Clear();
            InvalidateDartScanLines();
            ClearConsumedLineLocks();
            _firedHoldersThisTick.Clear();
            _postFireQueuedHoldersThisTick.Clear();
            _scanHeadDarts.Clear();
            _fireCandidates.Clear();
            _lastDeployPlacedFrameByHolder.Clear();
            _lastFiredHolderId = -1;
            // ROLLBACK_DART_STALL_WATCHDOG_PROGRESS_DECOUPLE_20260617:
            // 보드 리셋 시 진행 신호도 '지금' 으로 초기화 — 시작 직후 첫 pop 전에 워치독이 오발동하지 않도록.
            _lastPopUnscaledTime = Time.unscaledTime;
            _stallWatchLastPopSeen = _lastPopUnscaledTime;
            _stallWatchTimer = 0f;

            _tempRemoveKeys.Clear();
            foreach (var kvp in _frozenVisuals)
                _tempRemoveKeys.Add(kvp.Key);
            for (int i = 0; i < _tempRemoveKeys.Count; i++)
            {
                if (_frozenVisuals.TryGetValue(_tempRemoveKeys[i], out var obj))
                    ReturnDartToPool(obj);
            }
            _frozenVisuals.Clear();
        }

        /// <summary>
        /// Resets state for a new level.
        /// </summary>
        public void ResetAll()
        {
            ClearAllDarts();
            DirectionalTargeting.ResetCache();
            _boardFinished = false;
        }

        /// <summary>
        /// Creates a visual dart on a rail slot (called when holder deploys a dart).
        /// </summary>
        public void CreateSlotDartVisual(int slotIndex, int color, int holderId = -1)
        {
            if (_slotVisuals.ContainsKey(slotIndex))
            {
                // Already has visual — replace
                ReturnDartToPool(_slotVisuals[slotIndex].gameObject);
                _slotVisuals.Remove(slotIndex);
            }

            if (!RailManager.HasInstance) return;

            Vector3 pos = RailManager.Instance.GetSlotWorldPosition(slotIndex);
            GameObject dartObj = null;

            if (ObjectPoolManager.HasInstance)
            {
                dartObj = ObjectPoolManager.Instance.Get(DART_POOL_KEY, pos, Quaternion.identity);
            }

            if (dartObj == null) return;

            dartObj.SetActive(true);
            ApplyColor(dartObj, color);
            OrientDart(dartObj, slotIndex);

            // [ROLLBACK_DART_FX_TRAIL] 레일 위 = 비활성.
            SetDartTrailActive(dartObj, false);

            // 슬롯 간격보다 다트가 크면 스케일 축소 (겹침 방지)
            float spacing = RailManager.Instance.SlotSpacing;
            if (spacing > 0.01f)
            {
                float maxScale = spacing * 1.3f; // 슬롯 간격의 90%
                Vector3 s = dartObj.transform.localScale;
                float currentSize = Mathf.Max(s.x, s.z);
                if (currentSize > maxScale)
                {
                    float ratio = maxScale / currentSize;
                    dartObj.transform.localScale = new Vector3(s.x * ratio, s.y * ratio, s.z * ratio);
                }
            }

            Vector3 slotTargetScale = dartObj.transform.localScale;

            _slotVisuals[slotIndex] = new SlotDartVisual
            {
                slotIndex = slotIndex,
                color = color,
                gameObject = dartObj,
                baseScale = slotTargetScale
            };

            // 배치 연출: pop-in 스케일 애니 (매 배치마다 가시적 피드백)
            // ROLLBACK_DART_PLACE_POPIN_TWEEN:
            // Restore zero-scale + DOScale pop-in if the placement animation is needed again.
            dartObj.transform.DOKill();
            dartObj.transform.localScale = slotTargetScale;
        }

        /// <summary>
        /// Returns the number of active dart visuals on slots.
        /// </summary>
        public int GetActiveSlotDartCount()
        {
            return _slotVisuals.Count;
        }

        /// <summary>
        /// Creates a visual dart by dart ID (per-dart system).
        /// </summary>
        public void CreateDartVisualById(int dartId, int color, int holderId)
        {
            if (_dartVisuals.ContainsKey(dartId))
            {
                ReturnDartToPool(_dartVisuals[dartId].gameObject);
                _dartVisuals.Remove(dartId);
            }

            if (!RailManager.HasInstance) return;

            Vector3 pos = RailManager.Instance.GetDartWorldPosition(dartId);
            GameObject dartObj = null;

            if (ObjectPoolManager.HasInstance)
                dartObj = ObjectPoolManager.Instance.Get(DART_POOL_KEY, pos, Quaternion.identity);

            if (dartObj == null) return;

            dartObj.SetActive(true);
            dartObj.transform.localScale = Vector3.one; // 풀 재사용 시 스케일 리셋
            ApplyColor(dartObj, color);

            // [ROLLBACK_DART_FX_TRAIL] 레일 위 = 비활성. fire 시 활성.
            SetDartTrailActive(dartObj, false);

            // 슬롯 간격 기반 스케일 축소 (겹침 방지)
            float spacing = RailManager.Instance.SlotSpacing;
            if (spacing > 0.01f)
            {
                float maxScale = spacing * 1.3f;
                float currentSize = Mathf.Max(1f, 1f); // 리셋 후 기본 1.0 기준
                if (currentSize > maxScale)
                {
                    float ratio = maxScale / currentSize;
                    dartObj.transform.localScale = Vector3.one * ratio;
                }
            }

            Vector3 targetScale = dartObj.transform.localScale;

            _dartVisuals[dartId] = new SlotDartVisual
            {
                slotIndex = dartId,
                color = color,
                gameObject = dartObj,
                baseScale = targetScale
            };

            // 배치 연출: 0 → targetScale 로 OutBack pop-in (매 배치마다 가시적 피드백).
            // ROLLBACK_DART_PLACE_POPIN_TWEEN:
            // Restore zero-scale + DOScale pop-in if the placement animation is needed again.
            dartObj.transform.DOKill();
            dartObj.transform.localScale = targetScale;
        }

        #endregion

        #region Private Methods — Slot Dart Movement

        /// <summary>
        /// Updates all slot dart positions to follow conveyor belt movement.
        /// </summary>
        /// <summary>Reusable list for safe dictionary iteration (avoids allocation every frame).</summary>
        private readonly List<int> _tempSlotKeys = new List<int>(256);
        private readonly List<int> _tempRemoveKeys = new List<int>(32);

        /// <summary>Cave 스케일 구간: FADE_START(스케일1) ~ FADE_END(스케일0) 사이에서 축소.</summary>
        /// 면수별 기본값 (동일하게 시작, Inspector에서 개별 조정 가능)
        private const float DEFAULT_CAVE_FADE_START = 0.0315f;
        private const float DEFAULT_CAVE_FADE_END   = 0.03f;

        // 프레임당 1회 캐시 (다트 수백 개에서 매번 프로퍼티 접근 방지)
        private float _cachedFadeStart = DEFAULT_CAVE_FADE_START;
        private float _cachedFadeEnd = DEFAULT_CAVE_FADE_END;
        private float _cachedDartScale = 1f;
        private float _cachedDartPathOffset = 0f;
        private int _fadeCacheFrame = -1;

        private float CaveFadeStart { get { RefreshFadeCache(); return _cachedFadeStart; } }
        private float CaveFadeEnd { get { RefreshFadeCache(); return _cachedFadeEnd; } }

        private void RefreshFadeCache()
        {
            int frame = Time.frameCount;
            if (frame == _fadeCacheFrame) return;
            _fadeCacheFrame = frame;

            if (!GameManager.HasInstance)
            {
                _cachedFadeStart = DEFAULT_CAVE_FADE_START;
                _cachedFadeEnd = DEFAULT_CAVE_FADE_END;
                _cachedDartScale = 1f;
                _cachedDartPathOffset = 0f;
                return;
            }
            int sides = BoardTileManager.HasInstance ? BoardTileManager.Instance.RailSideCount : 4;
            var b = GameManager.Instance.Board;
            _cachedFadeStart = sides switch { 1 => b.caveFadeStart1Side, 2 => b.caveFadeStart2Side, 3 => b.caveFadeStart3Side, _ => b.caveFadeStart4Side };
            _cachedFadeEnd = sides switch { 1 => b.caveFadeEnd1Side, 2 => b.caveFadeEnd2Side, 3 => b.caveFadeEnd3Side, _ => b.caveFadeEnd4Side };
            _cachedDartScale = b.dartScale;
            _cachedDartPathOffset = b.dartPathOffset;
        }

        private void UpdateSlotDartPositions()
        {
            if (!RailManager.HasInstance || _slotVisuals.Count == 0) return;

            RailManager rail = RailManager.Instance;
            bool isOpen = !rail.IsClosedLoop;
            float pathLen = rail.TotalPathLength;

            // Collect keys without allocation (reuse list)
            _tempSlotKeys.Clear();
            _tempSlotKeys.AddRange(_slotVisuals.Keys);

            _tempRemoveKeys.Clear();

            for (int i = 0; i < _tempSlotKeys.Count; i++)
            {
                int slotIdx = _tempSlotKeys[i];
                if (!_slotVisuals.TryGetValue(slotIdx, out SlotDartVisual visual)) continue;
                if (visual.gameObject == null)
                {
                    _tempRemoveKeys.Add(slotIdx);
                    continue;
                }

                if (rail.IsSlotEmpty(slotIdx))
                {
                    ReturnDartToPool(visual.gameObject);
                    _tempRemoveKeys.Add(slotIdx);
                    continue;
                }

                // null 재확인 (다른 시스템이 mid-frame에 오브젝트 파괴할 수 있음)
                if (visual.gameObject == null) { _tempRemoveKeys.Add(slotIdx); continue; }

                Vector3 pos = rail.GetSlotWorldPosition(slotIdx);
                visual.gameObject.transform.position = pos;

                Vector3 fireDir = rail.GetSlotFiringDirection(slotIdx);
                if (fireDir.sqrMagnitude > 0.001f)
                    visual.gameObject.transform.rotation = Quaternion.LookRotation(fireDir);

                // [DISABLED 2026-05-15] 터널 진입/이탈 시 Scale Up/Down 연출 주석 처리 (요청)
                // Cave 스케일: 비순환 레일의 끝점/시작점 근처에서 축소
                // if (isOpen && pathLen > 0f)
                // {
                //     float dist = slotIdx * rail.SlotSpacing + rail.RotationOffset;
                //     float t = ((dist % pathLen) + pathLen) % pathLen / pathLen; // 0~1 정규화
                //
                //     float scale = 1f;
                //     float fs = CaveFadeStart, fe = CaveFadeEnd;
                //     float fadeRange = fs - fe;
                //     if (t < fs)
                //     {
                //         // 시작점에서 나옴: FADE_END(0) → FADE_START(1)
                //         scale = t <= fe ? 0f : (t - fe) / fadeRange;
                //     }
                //     else if (t > 1f - fs)
                //     {
                //         // 끝점으로 들어감: (1-FADE_START)(1) → (1-FADE_END)(0)
                //         float distFromEnd = 1f - t;
                //         scale = distFromEnd <= fe ? 0f : (distFromEnd - fe) / fadeRange;
                //     }
                //
                //     scale = Mathf.Clamp01(scale);
                //     visual.gameObject.transform.localScale = visual.baseScale * scale;
                // }
                if (visual.gameObject.transform.localScale != visual.baseScale)
                    visual.gameObject.transform.localScale = visual.baseScale;
            }

            // Deferred removal
            for (int i = 0; i < _tempRemoveKeys.Count; i++)
                _slotVisuals.Remove(_tempRemoveKeys[i]);
        }

        #endregion

        #region Private Methods — Per-Dart Movement

        /// <summary>벨트 4개 코너 타일 영역 안에 있는지 판별.</summary>
        private bool IsInCornerZone(Vector3 pos)
        {
            float boardCX = GameManager.HasInstance ? GameManager.Instance.Board.boardCenterX : 0f;
            float boardCZ = GameManager.HasInstance ? GameManager.Instance.Board.boardCenterZ : 2f;
            float halfW = BoardTileManager.CONVEYOR_WIDTH * 0.5f;
            float halfH = BoardTileManager.CONVEYOR_HEIGHT * 0.5f;
            float cs = BoardTileManager.RAIL_THICKNESS; // 코너 타일 크기

            float left = boardCX - halfW;
            float right = boardCX + halfW - cs;
            float bottom = boardCZ - halfH;
            float top = boardCZ + halfH - cs;

            // 4개 코너 박스 체크
            bool inBL = pos.x >= left && pos.x <= left + cs && pos.z >= bottom && pos.z <= bottom + cs;
            bool inBR = pos.x >= right && pos.x <= right + cs && pos.z >= bottom && pos.z <= bottom + cs;
            bool inTL = pos.x >= left && pos.x <= left + cs && pos.z >= top && pos.z <= top + cs;
            bool inTR = pos.x >= right && pos.x <= right + cs && pos.z >= top && pos.z <= top + cs;

            return inBL || inBR || inTL || inTR;
        }

        private void UpdatePerDartPositions()
        {
            if (!RailManager.HasInstance || _dartVisuals.Count == 0) return;

            RailManager rail = RailManager.Instance;
            bool isOpen = !rail.IsClosedLoop;
            float pathLen = rail.TotalPathLength;
            RefreshFadeCache();
            float pathOffset = _cachedDartPathOffset;
            float dartScale = _cachedDartScale;
            Vector3 normalScale = Vector3.one * dartScale;
            // [DISABLED 2026-05-15] 터널 Scale Up/Down 연출 주석 처리에 따라 미사용
            // float fadeStart = _cachedFadeStart;
            // float fadeEnd = _cachedFadeEnd;

            _tempRemoveKeys.Clear();

            foreach (var kvp in _dartVisuals)
            {
                int dartId = kvp.Key;
                var visual = kvp.Value;
                if (visual.gameObject == null) { _tempRemoveKeys.Add(dartId); continue; }

                var dart = rail.FindDart(dartId);
                if (dart == null)
                {
                    // Dart removed from belt
                    ReturnDartToPool(visual.gameObject);
                    _tempRemoveKeys.Add(dartId);
                    continue;
                }

                // 사용자 요구: slot 기반 위치 — 다트 사이 간격 일관성 보장 (progress 가변 영향 제거)
                rail.GetPositionAndDirectionAtDistance(dart.progress, out Vector3 pos, out Vector3 tangent);

                // 다트 경로 오프셋 — 벨트 중심 방향으로 이동
                // [Optimization 2026-05-10] GetDirectionAtNormalized 의 GetPositionAtDistance × 2 + sqrt 패턴을
                // GetDirectionAtDistance (이진 탐색 1회 + 배열 lookup) 으로 대체. dart 200개 시 매 frame 큰 부하 감소.
                // 롤백: 아래 새 라인 제거 + 주석 처리된 원본 3줄 복원.
                // 원본:
                // float normT = pathLen > 0f ? dart.progress / pathLen : 0f;
                // normT = ((normT % 1f) + 1f) % 1f;
                // Vector3 tangent = rail.GetDirectionAtNormalized(normT);
                Vector3 inward = Vector3.Cross(tangent, Vector3.up).normalized;

                pos += inward * pathOffset;

                visual.gameObject.transform.position = pos;

                // 다트 스케일 동적 적용
                // Orient — 접선의 안쪽 직각 방향 = 공격 방향
                if (tangent.sqrMagnitude > 0.001f)
                {
                    if (inward.sqrMagnitude > 0.001f)
                        visual.gameObject.transform.rotation = Quaternion.LookRotation(inward);
                }

                // [DISABLED 2026-05-15] 터널 진입/이탈 시 Scale Up/Down 연출 주석 처리 (요청)
                // Cave scale for open rails (dartScale 적용 유지)
                // if (isOpen && pathLen > 0f)
                // {
                //     float t = dart.progress / pathLen;
                //     float scale = 1f;
                //     float fadeRange = fadeStart - fadeEnd;
                //     if (fadeRange > 0f)
                //     {
                //         if (t < fadeStart)
                //             scale = t <= fadeEnd ? 0f : (t - fadeEnd) / fadeRange;
                //         else if (t > 1f - fadeStart)
                //         {
                //             float distFromEnd = 1f - t;
                //             scale = distFromEnd <= fadeEnd ? 0f : (distFromEnd - fadeEnd) / fadeRange;
                //         }
                //     }
                //     scale = Mathf.Clamp01(scale);
                //     visual.gameObject.transform.localScale = normalScale * scale;
                // }
                if (visual.gameObject.transform.localScale != normalScale)
                {
                    visual.gameObject.transform.localScale = normalScale;
                }
            }

            for (int i = 0; i < _tempRemoveKeys.Count; i++)
                _dartVisuals.Remove(_tempRemoveKeys[i]);
        }

        #endregion

        #region Private Methods — Auto-Fire Scan

        /// <summary>같은 보관함에서 선행 다트(낮은 dartId)가 아직 남아있으면 후행 다트 공격 차단.</summary>
        private readonly Dictionary<int, int> _holderFrontDartId = new Dictionary<int, int>();
        private readonly HashSet<int> _blockedHolders = new HashSet<int>();

        /// <summary>
        /// Scans occupied slots per frame and fires darts at matching outermost balloons.
        /// 같은 보관함 다트는 선행 인덱스(낮은 dartId)부터 순차 공격.
        /// </summary>
        private void ScanAndFireDarts()
        {
            if (!RailManager.HasInstance || !BalloonController.HasInstance) return;

            RailManager rail = RailManager.Instance;
            int slotCount = rail.SlotCount;
            if (slotCount == 0) return;

            bool freeFireMode = GameManager.HasInstance && GameManager.Instance.Board.dartFreeFireMode;

            // Step 1: 보관함별 가장 선행(낮은 dartId) 다트 찾기
            _holderFrontDartId.Clear();
            _blockedHolders.Clear();

            if (!freeFireMode)
            {
                for (int s = 0; s < slotCount; s++)
                {
                    RailManager.SlotData sd = rail.GetSlot(s);
                    if (sd.dartColor < 0) continue;
                    if (!_holderFrontDartId.TryGetValue(sd.holderId, out int existingId) ||
                        sd.dartId < existingId)
                    {
                        _holderFrontDartId[sd.holderId] = sd.dartId;
                    }
                }
            }

            // Step 2: 스캔 + 공격
            int fired = 0;

            for (int s = 0; s < slotCount && fired < MAX_FIRES_PER_FRAME; s++)
            {
                int slotIdx = s;

                RailManager.SlotData slot = rail.GetSlot(slotIdx);
                if (slot.dartColor < 0) continue;

                if (!freeFireMode)
                {
                    // 같은 보관함의 선행 다트가 막혔으면 후행도 차단
                    if (_blockedHolders.Contains(slot.holderId)) continue;

                    // 선행 다트(가장 낮은 dartId)만 공격 가능
                    if (_holderFrontDartId.TryGetValue(slot.holderId, out int frontId) &&
                        slot.dartId != frontId)
                    {
                        continue;
                    }
                }

                Vector3 slotPos = rail.GetSlotWorldPosition(slotIdx);
                Vector3 fireDir = rail.GetSlotFiringDirection(slotIdx);

                int targetId = DirectionalTargeting.FindTarget(slotPos, fireDir, slot.dartColor);
                if (targetId < 0)
                {
                    if (!freeFireMode)
                        _blockedHolders.Add(slot.holderId);
                    continue;
                }

                // Skip if another dart is already flying toward this balloon
                if (_reservedTargets.Contains(targetId)) continue;

                if (!BalloonController.HasInstance) return;
                // ROLLBACK_CONTOUR_TARGET_DIAG:
                if (DART_TARGETING_DEBUG)
                {
                    Debug.Log($"[FindTargetPick] mode=slot holder={slot.holderId} dartId={slot.dartId} slot={slotIdx} " +
                              $"{DirectionalTargeting.LastFindTargetDiag}");
                }
                BalloonData targetData = BalloonController.Instance.GetBalloon(targetId);
                if (targetData == null || targetData.isPopped)
                {
                    _reservedTargets.Remove(targetId); // stale reservation cleanup
                    continue;
                }

                // Fire! Free slot immediately (spec: slot returns as soon as dart fires)
                int color = slot.dartColor;
                int dartId = slot.dartId;
                rail.ClearSlot(slotIdx);

                // Publish fire event
                EventBus.Publish(new OnSlotDartFired
                {
                    slotIndex = slotIdx,
                    color = color,
                    targetBalloonId = targetId,
                    from = slotPos,
                    to = BalloonController.Instance.GetBalloonWorldPosition(targetId)
                });

                EventBus.Publish(new OnDartFired
                {
                    dartId = dartId,
                    holderId = -1,
                    color = color
                });

                // Reserve target so no other dart targets this balloon
                _reservedTargets.Add(targetId);

                // Launch projectile visual (실제 월드 위치)
                LaunchProjectile(slotIdx, slotPos, BalloonController.Instance.GetBalloonWorldPosition(targetId), targetId, color);
                fired++;
            }

        }

        /// <summary>
        /// Converts a slot dart into a flying projectile aimed at a balloon.
        /// </summary>
        private void LaunchProjectile(int slotIndex, Vector3 from, Vector3 to, int targetBalloonId, int color)
        {
            GameObject dartObj = null;

            // Try to reuse the slot visual
            if (_slotVisuals.TryGetValue(slotIndex, out SlotDartVisual visual))
            {
                dartObj = visual.gameObject;
                _slotVisuals.Remove(slotIndex);
            }

            if (dartObj == null)
            {
                // No visual to reuse — create new
                if (ObjectPoolManager.HasInstance)
                {
                    dartObj = ObjectPoolManager.Instance.Get(DART_POOL_KEY, from, Quaternion.identity);
                }
            }

            if (dartObj == null)
            {
                // Pool exhausted — instant hit
                ExecuteHit(targetBalloonId, color);
                return;
            }

            dartObj.SetActive(true);
            ApplyColor(dartObj, color);
            dartObj.transform.position = from;

            // 직사: 풍선 위치로 직접 발사
            Vector3 dir = to - from;
            dir.y = 0f;
            if (dir.sqrMagnitude > 0.001f)
                dartObj.transform.rotation = Quaternion.LookRotation(dir.normalized);

            float ft = CalculateProjectileFlightTime(from, to);

            // startScale = 레일 위 다트 현재 사이즈(절대 풍선 사이즈로 바꾸지 말 것 — 사용자 피드백 핵심).
            Vector3 balloonScale = BalloonController.HasInstance
                ? BalloonController.Instance.GetBalloonWorldScale(targetBalloonId)
                : dartObj.transform.localScale;
            Vector3 launchStartScale = dartObj.transform.localScale;

            var proj = GetProjectile();
            proj.gameObject = dartObj;
            proj.startPosition = from;
            proj.targetPosition = to;
            proj.targetBalloonId = targetBalloonId;
            proj.color = color;
            proj.scanDir = DirectionalTargeting.DetermineScanDirection(to - from);
            proj.scanLine = GetScanLine(from, proj.scanDir);
            // [ROLLBACK_DART_LAUNCH_INITIAL_PROGRESS]
            // 초기 elapsed offset — 다트 발사 직후 빠른 가속 효과 + 비행 다트끼리 spacing 줄임.
            // 기존: proj.elapsed = 0f;
            float initialProgress = GameManager.HasInstance ? GameManager.Instance.Board.dartLaunchInitialProgress : 0f;
            proj.elapsed = Mathf.Clamp(initialProgress, 0f, Mathf.Max(0f, ft - 0.01f));
            proj.duration = ft;
            proj.impactTime = ft;
            ConfigureLaunchScale(proj, launchStartScale, balloonScale);
            ConfigureNeedleTipImpactTiming(proj, dartObj, from, to, balloonScale);

            _activeProjectiles.Add(proj);

            // [ROLLBACK_DART_FX_TRAIL] 발사 시에도 FXDartTrail 비활성 유지 (요청: 도중 활성화 금지).
            SetDartTrailActive(dartObj, false);

            // [ROLLBACK_DART_DOMOVE_DEAD_CALL]
            // UpdateProjectiles 가 매 frame transform.position 을 manual lerp 로 덮어쓰므로 DOMove/DOPath 는 dead call.
            // Ease 는 UpdateProjectiles 의 manual lerp 에서 DOVirtual.EasedValue 로 적용. 롤백 시 아래 주석 해제.
            // Ease ease = GameManager.HasInstance ? GameManager.Instance.Board.dartFlightEase : Ease.InQuad;
            // if (_arcHeight > 0.01f)
            // {
            //     Vector3 midPoint = (from + to) * 0.5f;
            //     midPoint.y += _arcHeight;
            //     Vector3[] path = { from, midPoint, to };
            //     dartObj.transform.DOPath(path, ft, PathType.CatmullRom)
            //         .SetEase(ease)
            //         .SetLookAt(0.01f);
            // }
            // else
            // {
            //     dartObj.transform.DOMove(to, ft).SetEase(ease);
            // }
        }

        /// <summary>
        /// 다트별 동기 스캔 — scan tick (= belt cellSpacing 이동) 마다 호출.
        /// 사용자 요구: cluster head / sequential lowest-dartId 제한 제거.
        /// 한 scan tick 안 모든 eligible dart 발사 — 풍선 1개 = 다트 1발 (_reservedTargets 로 보장).
        /// 외곽 풍선 균등 hit (선형 + 비선형 outline 모두 대응).
        /// </summary>
        private void ScanAndFirePerDart()
        {
            if (!RailManager.HasInstance || !BalloonController.HasInstance) return;

            RailManager rail = RailManager.Instance;


            // 사용자 요구: scan tick 안 holder 별 1발 제한. 매 tick 시작 시 fired-set 초기화.
            _firedHoldersThisTick.Clear();
            // 추가 safety: 전역 1발/scan tick — "헤더에서 동시에 하나씩 더" 발사 (다른 holder 또는 cache 갱신된 head)
            // 차단. belt cellSpacing 이동마다 정확히 1 dart 만 발사.
            // head 가 먼저 발사되어야 함. RailManager._clusterHeadByHolder 의 holder 별
            // lowest placedSeq dart 만 발사 후보. event-maintained — wrap-around 버그 없음, sort 부담 0.
            // belt 회전 따라 head 위치 자동 변경 → outline 자동 분산 hit (한 column 만 hammering 안 됨).
            // ROLLBACK_DART_HEAD_ONLY_SCAN:
            // Only cluster heads can fire in normal play. Build a small head list from RailManager's
            // maintained cache instead of scanning every dart and rejecting non-heads one by one.
            rail.GetClusterHeadDarts(_scanHeadDarts);
            if (_scanHeadDarts.Count == 0)
            {
                // ROLLBACK_DART_KEEP_LINE_LOCK_WHILE_PROJECTILE:
                // During the first deployment frames, a holder can fire its only placed dart before
                // the next dart is spawned, leaving zero rail heads while the projectile is still in
                // flight. Clearing consumed line locks here reopens the same side/line exactly when
                // that projectile resolves, so the newly exposed inner cell can be peeled at deploy
                // start. Only clear when there is no unresolved projectile-owned line left.
                if (_activeProjectiles.Count == 0 && _unresolvedConsumedTargetLines.Count == 0)
                {
                    ClearConsumedLineLocks();
                }
                else
                {
                    LogAttackIssue(
                        "DartScanNoHead",
                        $"activeProjectiles={_activeProjectiles.Count} unresolvedLines={_unresolvedConsumedTargetLines.Count} " +
                        $"consumedLines={_consumedTargetLines.Count} reservedTargets={_reservedTargets.Count}");
                }
                return;
            }
            _scanHeadDarts.Sort(CompareDartPlacedSeq);
            _fireCandidates.Clear();

            for (int i = 0; i < _scanHeadDarts.Count; i++)
            {
                var dart = _scanHeadDarts[i];
                if (dart.dartColor < 0)
                {
                    continue;
                }

                // This list already contains holder heads only.
                if (_firedHoldersThisTick.Contains(dart.holderId))
                {
                    LogAttackIssue(
                        "DartFireBlocked",
                        $"reason=holderAlreadyFiredThisTick holder={dart.holderId} dartId={dart.dartId} progress={dart.progress:F2}");
                    continue;
                }
                    // 이 holder 가 이미 이 tick 에 발사 → skip (cache 갱신된 새 head 도 차단).

                rail.GetDartCurrentPose(dart, out Vector3 dartPos, out Vector3 scanTangent, out Vector3 fireDir);
                DirectionalTargeting.ScanDirection currentScanDir = DirectionalTargeting.DetermineScanDirection(fireDir);
                int currentLine = GetScanLine(dartPos, currentScanDir);
                int color = dart.dartColor;
                int dartId = dart.dartId;
                int holderId = dart.holderId;
                // ROLLBACK_DART_LINE_CACHE_INVALIDATION:
                // The line-driven scan cache must belong to the current head dart, not only to the
                // holder. Otherwise a new head on the same row/column, or an outer contour exposed
                // by another projectile, can be skipped until the belt reaches the next line.
                int lastLine = 0;
                int lastHeadId = -1;
                DirectionalTargeting.ScanDirection lastDir = currentScanDir;
                bool hasLastScan = _lastScannedLineByHolder.TryGetValue(dart.holderId, out lastLine)
                    && _lastScanDirectionByHolder.TryGetValue(dart.holderId, out lastDir)
                    && _lastScannedHeadIdByHolder.TryGetValue(dart.holderId, out lastHeadId);
                bool catchUpFromLastScan = hasLastScan
                    && lastHeadId == dart.dartId
                    && lastDir == currentScanDir;

                // lastScan 캐시가 stale (이전 head 의 dartId) 인 head-교체 직후 케이스에서 promotion
                // seed 를 catch-up 기준선으로 사용한다. 같은 head/같은 dir 일 때만 적용 (다른 holder
                // 이거나 path 회전으로 scanDir 가 바뀐 경우엔 적용 안 됨).
                // out 변수는 C# definite-assignment 규칙으로 단락 평가 후 보수적으로 unassigned 로
                // 간주되어 컴파일 오류 발생 → default 로 미리 선언.
                int pHead = -1;
                int pLine = 0;
                DirectionalTargeting.ScanDirection pDir = currentScanDir;
                bool hasPromoSeedForHead = !catchUpFromLastScan
                    && _promoHeadByHolder.TryGetValue(dart.holderId, out pHead)
                    && pHead == dart.dartId
                    && _promoDirByHolder.TryGetValue(dart.holderId, out pDir)
                    && _promoLineByHolder.TryGetValue(dart.holderId, out pLine);
                bool catchUpFromPromo = hasPromoSeedForHead && pDir == currentScanDir;
                bool catchUpFromCornerPromo = hasPromoSeedForHead && pDir != currentScanDir;

                ResetStraightRailPassIfWrapped(holderId, currentScanDir, currentLine, dart.progress);
                ResetOpenRailPassIfWrapped(
                    holderId,
                    currentScanDir,
                    currentLine,
                    hasLastScan,
                    lastDir,
                    lastLine,
                    hasPromoSeedForHead,
                    pDir);

                if (IsHolderLineConsumed(holderId, currentScanDir, currentLine))
                {
                    LogAttackIssue(
                        "DartFireBlocked",
                        $"reason=holderLineConsumed stage=current holder={holderId} dartId={dartId} " +
                        $"progress={dart.progress:F2} scan={currentScanDir} line={currentLine}");
                    continue;
                }
                // ROLLBACK_DART_HOLDER_PASS_LINE_SET:
                // Do not clear a holder's consumed line just because the head moved to a new line.
                // That made the same holder re-open an earlier line in the same side pass.
                bool releaseConsumedLineAfterScan = false;

                if (hasLastScan
                    && lastHeadId == dart.dartId
                    && lastDir == currentScanDir
                    && lastLine == currentLine)
                {
                    LogAttackIssue(
                        "DartScanSkip",
                        $"reason=sameHeadSameLine holder={holderId} dartId={dartId} progress={dart.progress:F2} " +
                        $"scan={currentScanDir} line={currentLine}");
                    continue;
                }

                int firstCatchUpLine = currentLine;
                int catchUpStep = 0;
                int catchUpCount = 1;
                // ROLLBACK_DART_CATCHUP_BASELINE_ADVANCE_20260622: 이번 틱에 catch-up budget 초과로 클램프됐는지.
                //   true 면 [firstCatchUpLine .. lastProbed] 만 probe되고 그 너머 ~ currentLine 은 미probe → 미발사 시 baseline 전진 필요.
                bool catchUpClamped = false;

                if (catchUpFromLastScan)
                {
                    int delta = currentLine - lastLine;
                    int absDelta = Mathf.Abs(delta);
                    if (absDelta > 1)
                    {
                        int catchUpBudget = MaxLineCatchUpPerHead;
                        if (absDelta > catchUpBudget)
                        {
                            catchUpClamped = true; // ROLLBACK_DART_CATCHUP_BASELINE_ADVANCE_20260622
                            LogAttackIssue(
                                "DartCatchUpClamped",
                                $"source=lastScan holder={holderId} dartId={dartId} scan={currentScanDir} " +
                                $"lastLine={lastLine} currentLine={currentLine} delta={absDelta} budget={catchUpBudget}");
                        }
                        catchUpStep = delta >= 0 ? 1 : -1;
                        catchUpCount = Mathf.Min(absDelta, catchUpBudget);
                        firstCatchUpLine = lastLine + catchUpStep;
                    }
                }
                else if (catchUpFromPromo)
                {
                    // promotion-time line 부터 currentLine 까지 (currentLine 포함) 모두 replay.
                    // lastScan 경로와 달리 catch-up 의 마지막 probe 가 currentLine 이 되도록 +1 한다.
                    int delta = currentLine - pLine;
                    int absDelta = Mathf.Abs(delta);
                    if (absDelta >= 1)
                    {
                        int catchUpBudget = MaxLineCatchUpPerHead;
                        if (absDelta + 1 > catchUpBudget)
                        {
                            LogAttackIssue(
                                "DartCatchUpClamped",
                                $"source=promo holder={holderId} dartId={dartId} scan={currentScanDir} " +
                                $"promoLine={pLine} currentLine={currentLine} delta={absDelta + 1} budget={catchUpBudget}");
                        }
                        catchUpStep = delta >= 0 ? 1 : -1;
                        catchUpCount = Mathf.Min(absDelta + 1, catchUpBudget);
                        firstCatchUpLine = pLine;
                    }
                }


                // 이미 reserve 된 풍선은 후보에서 제외 → 다음 closest 풍선을 발사 대상으로 받음.
                // [2026-05-13] FindTarget = contour-only. inner-stuck color fallback 은 관통 이슈로 비활성.
                //   inner 만 남은 색 풍선은 hit 불가 → 레벨 디자인으로 회피 (외곽이 같은 색으로 깎이며 노출).
                int targetId = -1;
                DirectionalTargeting.ScanDirection scanDir = currentScanDir;
                int targetLine = currentLine;
                Vector3 selectedTargetPos = Vector3.zero;
                Vector3 scanDartPos = dartPos;
                bool foundTarget = false;

                // ROLLBACK_DART_CORNER_PROMO_REPLAY:
                // A promoted/deployed head can carry a seed from the previous side of a rounded
                // corner. If the next scan already reports the new side, the normal promo catch-up
                // ignores that seed and the last line before the turn can be skipped. Probe the
                // seed direction once before switching fully to the current direction.
                if (catchUpFromCornerPromo)
                {
                    int cornerCurrentLine = GetScanLine(dartPos, pDir);
                    int cornerDelta = cornerCurrentLine - pLine;
                    int cornerStep = cornerDelta >= 0 ? 1 : -1;
                    int cornerCount = Mathf.Clamp(Mathf.Abs(cornerDelta) + 1, 1, MaxLineCatchUpPerHead);
                    Vector3 cornerFireDir = GetFireDirectionForScanDirection(pDir);

                    LogAttackIssue(
                        "DartCornerPromoReplay",
                        $"holder={holderId} dartId={dartId} progress={dart.progress:F2} " +
                        $"seedScan={pDir} currentScan={currentScanDir} seedLine={pLine} " +
                        $"cornerCurrentLine={cornerCurrentLine} probes={cornerCount}");

                    for (int lineProbe = 0; !foundTarget && lineProbe < cornerCount; lineProbe++)
                    {
                        int probeLine = pLine + lineProbe * cornerStep;
                        if (IsTargetLineConsumed(pDir, probeLine))
                        {
                            LogAttackIssue(
                                "DartFireBlocked",
                                $"reason=targetLineConsumed stage=cornerPromo holder={holderId} dartId={dartId} " +
                                $"progress={dart.progress:F2} scan={pDir} line={probeLine} " +
                                $"currentScan={currentScanDir} probe={lineProbe + 1}/{cornerCount}");
                            continue;
                        }
                        if (IsHolderLineConsumed(holderId, pDir, probeLine))
                        {
                            LogAttackIssue(
                                "DartFireBlocked",
                                $"reason=holderLineConsumed stage=cornerPromo holder={holderId} dartId={dartId} " +
                                $"progress={dart.progress:F2} scan={pDir} line={probeLine} " +
                                $"currentScan={currentScanDir} probe={lineProbe + 1}/{cornerCount}");
                            continue;
                        }

                        Vector3 probePos = MakeScanPositionForLine(dartPos, pDir, probeLine);
                        foundTarget = DirectionalTargeting.TryFindTarget(
                            probePos,
                            cornerFireDir,
                            color,
                            _reservedTargets,
                            out targetId,
                            out scanDir,
                            out targetLine,
                            out selectedTargetPos);
                        scanDartPos = probePos;
                        if (!foundTarget)
                        {
                            foundTarget = TryFindAdjacentEmptyLineRescue(
                                holderId,
                                dartId,
                                color,
                                dart.progress,
                                probePos,
                                cornerFireDir,
                                pDir,
                                probeLine,
                                "cornerPromo",
                                out targetId,
                                out scanDir,
                                out targetLine,
                                out selectedTargetPos);
                        }
                        if (foundTarget)
                        {
                            int foundLine = GetScanLine(selectedTargetPos, scanDir);
                            if (IsTargetLineConsumed(scanDir, foundLine) || IsHolderLineConsumed(holderId, scanDir, foundLine))
                            {
                                LogAttackIssue(
                                    "DartFireBlocked",
                                    $"reason=consumedCandidate stage=cornerPromo holder={holderId} dartId={dartId} " +
                                    $"progress={dart.progress:F2} scan={scanDir} line={foundLine} " +
                                    $"seedScan={pDir} seedLine={probeLine}");
                                foundTarget = false;
                                targetId = -1;
                                continue;
                            }
                        }
                    }

                    if (!foundTarget)
                        ClearPromoSeedForHolder(holderId);
                }

                // ROLLBACK_DART_CROSSED_LINE_SCAN:
                // A long frame or speed burst can move a head across multiple grid lines. The old
                // catch-up checked only one representative skipped line, then current position, so
                // the remaining line could be lost. Probe the recently crossed lines in order while
                // keeping the gameplay limit at one fired dart per holder per scan.
                for (int lineProbe = 0; !foundTarget && lineProbe < catchUpCount; lineProbe++)
                {
                    int probeLine = catchUpStep == 0
                        ? currentLine
                        : firstCatchUpLine + lineProbe * catchUpStep;
                    if (IsTargetLineConsumed(currentScanDir, probeLine))
                    {
                        LogAttackIssue(
                            "DartFireBlocked",
                            $"reason=targetLineConsumed stage=probe holder={holderId} dartId={dartId} " +
                            $"progress={dart.progress:F2} scan={currentScanDir} line={probeLine} " +
                            $"currentLine={currentLine} probe={lineProbe + 1}/{catchUpCount}");
                        continue;
                    }
                    if (IsHolderLineConsumed(holderId, currentScanDir, probeLine))
                    {
                        LogAttackIssue(
                            "DartFireBlocked",
                            $"reason=holderLineConsumed stage=probe holder={holderId} dartId={dartId} " +
                            $"progress={dart.progress:F2} scan={currentScanDir} line={probeLine} " +
                            $"currentLine={currentLine} probe={lineProbe + 1}/{catchUpCount}");
                        continue;
                    }
                    Vector3 probePos = catchUpStep == 0
                        ? dartPos
                        : MakeScanPositionForLine(dartPos, currentScanDir, probeLine);

                    foundTarget = DirectionalTargeting.TryFindTarget(
                        probePos,
                        fireDir,
                        color,
                        _reservedTargets,
                        out targetId,
                        out scanDir,
                        out targetLine,
                        out selectedTargetPos);
                    scanDartPos = probePos;
                    if (!foundTarget)
                    {
                        foundTarget = TryFindAdjacentEmptyLineRescue(
                            holderId,
                            dartId,
                            color,
                            dart.progress,
                            probePos,
                            fireDir,
                            currentScanDir,
                            probeLine,
                            "probe",
                            out targetId,
                            out scanDir,
                            out targetLine,
                            out selectedTargetPos);
                    }
                    if (foundTarget)
                    {
                        // ROLLBACK_DART_CATCHUP_CONSUMED_CANDIDATE_CONTINUE:
                        // TryFindTarget can accept a nearby stable line while probing a crossed
                        // line. At x2 this often resolves back to the just-fired/consumed previous
                        // line. If we wait until candidate-build validation, the outer head loop
                        // `continue`s and the remaining catch-up lines are never probed. Reject the
                        // consumed nearby candidate here and keep scanning the later crossed lines.
                        int foundLine = GetScanLine(selectedTargetPos, scanDir);
                        if (IsTargetLineConsumed(scanDir, foundLine))
                        {
                            LogAttackIssue(
                                "DartFireBlocked",
                                $"reason=targetLineConsumed stage=probeCandidate holder={holderId} dartId={dartId} " +
                                $"progress={dart.progress:F2} scan={scanDir} line={foundLine} " +
                                $"probeScan={currentScanDir} probeLine={probeLine} currentLine={currentLine} " +
                                $"probe={lineProbe + 1}/{catchUpCount}");
                            foundTarget = false;
                            targetId = -1;
                            continue;
                        }
                        if (IsHolderLineConsumed(holderId, scanDir, foundLine))
                        {
                            LogAttackIssue(
                                "DartFireBlocked",
                                $"reason=holderLineConsumed stage=probeCandidate holder={holderId} dartId={dartId} " +
                                $"progress={dart.progress:F2} scan={scanDir} line={foundLine} " +
                                $"probeScan={currentScanDir} probeLine={probeLine} currentLine={currentLine} " +
                                $"probe={lineProbe + 1}/{catchUpCount}");
                            foundTarget = false;
                            targetId = -1;
                            continue;
                        }
                        break;
                    }
                }

                if (!foundTarget && (scanDartPos - dartPos).sqrMagnitude > 0.0001f)
                {
                    foundTarget = DirectionalTargeting.TryFindTarget(
                        dartPos,
                        fireDir,
                        color,
                        _reservedTargets,
                        out targetId,
                        out scanDir,
                        out targetLine,
                        out selectedTargetPos);
                    scanDartPos = dartPos;
                    if (!foundTarget)
                    {
                        foundTarget = TryFindAdjacentEmptyLineRescue(
                            holderId,
                            dartId,
                            color,
                            dart.progress,
                            dartPos,
                            fireDir,
                            currentScanDir,
                            currentLine,
                            "currentFallback",
                            out targetId,
                            out scanDir,
                            out targetLine,
                            out selectedTargetPos);
                    }
                }
                if (releaseConsumedLineAfterScan)
                {
                    ClearConsumedLineLockForHolder(holderId);
                }
                if (!foundTarget)
                {
                    // ROLLBACK_PROMOTION_SEED_CATCH_UP:
                    // Do not mark a failed promo catch-up as scanned. At x2, a transient reserved/noEdge
                    // miss can become valid shortly after; caching currentLine here recreates the one-by-one
                    // miss path. The seed is single-use, so the next frame falls back to the current exact
                    // line without replaying stale behind-the-head lines.
                    if (catchUpFromPromo)
                    {
                        ClearPromoSeedForHolder(holderId);
                    }
                    // ROLLBACK_DART_CATCHUP_BASELINE_ADVANCE_20260622: lastScan catch-up 이 budget 초과로 클램프되어
                    //   [firstCatchUpLine .. lastProbed] 만 probe하고 타겟을 못 찾았으면, baseline 을 '마지막 probe 라인'으로
                    //   전진시킨다. 미전진(기존)이면 다음 틱도 currentLine 이 더 멀어진 채 같은 앞쪽 밴드만 재probe →
                    //   [lastProbed+1 .. currentLine] 영구 스킵(놓침). 전진하면 다음 틱이 그 다음 밴드를 이어 probe → 수렴.
                    //   클램프가 없을 땐(밴드 전체 probe 완료) 동작 불변. promo 경로는 seed 단발성이라 제외(위 주석 정책 유지).
                    if (catchUpClamped && catchUpFromLastScan && catchUpStep != 0)
                    {
                        int lastProbedLine = firstCatchUpLine + (catchUpCount - 1) * catchUpStep;
                        _lastScannedLineByHolder[holderId] = lastProbedLine;
                        _lastScanDirectionByHolder[holderId] = currentScanDir;
                        _lastScannedHeadIdByHolder[holderId] = dart.dartId;
                    }
                    LogMissSuspectIfNeeded(holderId, dartId, color, dart.progress, scanDartPos, fireDir, currentScanDir, currentLine);
                    continue;
                }
                // ROLLBACK_PROMOTION_SEED_CATCH_UP:
                // foundTarget 인 경우 line 996-998 의 기존 lastScan 캐시 갱신이 baseline 역할을
                // 하므로, promo seed 만 폐기한다.
                // ROLLBACK_DART_PROMO_SEED_COMMIT_ONLY:
                // Do not clear the promotion seed here. The candidate can still fail at commit time
                // because another candidate consumed the same line/target in this scan tick.
                // ROLLBACK_CONTOUR_TARGET_DIAG:
                if (DART_TARGETING_DEBUG)
                {
                    Debug.Log($"[FindTargetPick] mode=dart holder={holderId} dartId={dartId} " +
                              $"progress={dart.progress:F2} {DirectionalTargeting.LastFindTargetDiag}");
                }
                // [2026-05-13 Diag] 진단 로그 ([DartSkip]) 제거 — DirectionalTargeting.FormatLastDiag()도 비활성.
                //   재활성: DirectionalTargeting.cs 의 Last* 필드 + FormatLastDiag 주석 해제 후
                //   이 분기에서 hasMatching 검사 + Debug.Log 복원.

                BalloonData targetData = BalloonController.Instance.GetBalloon(targetId);
                if (targetData == null || targetData.isPopped)
                {
                    LogAttackIssue(
                        "DartMissBlocked",
                        $"reason=targetGoneBeforeCandidate holder={holderId} dartId={dartId} color={color} " +
                        $"target={targetId} popped={(targetData != null && targetData.isPopped)} " +
                        $"scan={scanDir} line={targetLine} progress={dart.progress:F2}");
                    continue;
                }

                int candidateScanLine = targetLine;
                if (IsTargetLineConsumed(scanDir, candidateScanLine))
                {
                    LogAttackIssue(
                        "DartFireBlocked",
                        $"reason=targetLineConsumed stage=candidateBuild holder={holderId} dartId={dartId} " +
                        $"target={targetId} scan={scanDir} line={candidateScanLine} progress={dart.progress:F2}");
                    continue;
                }
                if (IsHolderLineConsumed(holderId, scanDir, candidateScanLine))
                {
                    LogAttackIssue(
                        "DartFireBlocked",
                        $"reason=holderLineConsumed stage=candidateBuild holder={holderId} dartId={dartId} " +
                        $"target={targetId} scan={scanDir} line={candidateScanLine} progress={dart.progress:F2}");
                    continue;
                }

                // ROLLBACK_DART_CLUSTER_FAIR_FIRE:
                // Collect every holder head that can fire this scan tick, then choose one holder
                // after the loop. The previous "first valid candidate wins" path let a corner/tunnel
                // cluster keep consuming the only fire slot while another cluster skipped its line.
                _fireCandidates.Add(new DartFireCandidate
                {
                    isValid = true,
                    dart = dart,
                    dartPos = dartPos,
                    scanDartPos = scanDartPos,
                    fireDir = fireDir,
                    holderId = holderId,
                    dartId = dartId,
                    color = color,
                    targetId = targetId,
                    scanLine = candidateScanLine,
                    scanDir = scanDir,
                    selectedTargetPos = selectedTargetPos,
                    findTargetDiag = DirectionalTargeting.LastFindTargetDiag
                });
                // ROLLBACK_DART_SCAN_ALL_HEADS_PICK_ONE: old immediate-fire block disabled below.
#if false
                // Fire!
                var fireHead = rail.GetClusterHeadDart(holderId);
                int fireSlot = rail.GetSlotAtPathDistance(dart.progress);
                var fireSlotData = rail.GetSlot(fireSlot);
                if (DART_TARGETING_DEBUG)
                {
                    Debug.Log($"[DartFire] holder={holderId} dartId={dartId} color={color} " +
                              $"progress={dart.progress:F2} slot={fireSlot} slotState=holder{fireSlotData.holderId}/dart{fireSlotData.dartId}/color{fireSlotData.dartColor} " +
                              $"head={(fireHead != null ? fireHead.dartId.ToString() : "null")} target={targetId} " +
                              $"gapAfterRemove={rail.GetPlacementGapDebugInfo(dart.progress, color, holderId, dartId)} " +
                              $"advance={rail.GetAdvanceModeDebugInfo()}");
                }
                bool removedFromRail = rail.RemoveDartById(dartId);
                if (DART_TARGETING_DEBUG)
                {
                    Debug.Log($"[DartFireAfterRemove] holder={holderId} dartId={dartId} removed={removedFromRail} " +
                              $"advance={rail.GetAdvanceModeDebugInfo()}");
                }
                // RemoveDartById 가 RemoveFromClusterHeadCache 호출 → _clusterHeadByHolder 자동 갱신.
                // 새 head 가 같은 tick 에 발사되지 않도록 _firedHoldersThisTick 으로 차단.

                _firedHoldersThisTick.Add(holderId);
                firesThisTick++;
                _reservedTargets.Add(targetId);

                // Transfer visual from belt to projectile
                GameObject dartObj = null;
                if (_dartVisuals.TryGetValue(dartId, out var visual))
                {
                    dartObj = visual.gameObject;
                    _dartVisuals.Remove(dartId);
                }

                if (dartObj == null && ObjectPoolManager.HasInstance)
                    dartObj = ObjectPoolManager.Instance.Get(DART_POOL_KEY, dartPos, Quaternion.identity);

                if (dartObj != null)
                {
                    // 레일에서 보이던 사이즈(dartScale) 그대로 발사 — cave fade는 무시하고 정상 크기로 복원
                    float ds = GameManager.HasInstance ? GameManager.Instance.Board.dartScale : 1f;
                    dartObj.transform.localScale = Vector3.one * ds;

                    EventBus.Publish(new OnDartFired { dartId = dartId, holderId = -1, color = color });

                    // 직사: 풍선 실제 월드 위치로 직접 발사
                    // ROLLBACK_DART_LINE_SNAP:
                    // TryFindTarget may accept a nearby stable line to absorb rail/grid jitter.
                    // Snap the visual to that selected line before cardinal flight so the rendered
                    // dart path and gameplay target cannot diverge.
                    Vector3 targetPos = selectedTargetPos;
                    Vector3 launchPos = CalculateCardinalLaunchPosition(scanDartPos, targetPos, scanDir);
                    dartObj.transform.position = launchPos;
                    Vector3 travelTarget = CalculateCardinalTarget(launchPos, targetPos, scanDir);
                    Vector3 dir = travelTarget - launchPos;
                    dir.y = 0f;
                    if (dir.sqrMagnitude > 0.001f)
                        dartObj.transform.rotation = Quaternion.LookRotation(dir.normalized);

                    float ft = CalculateProjectileFlightTime(launchPos, travelTarget);

                    // startScale = 레일 위 다트 현재 사이즈(절대 풍선 사이즈로 바꾸지 말 것 — 사용자 피드백 핵심).
                    Vector3 balloonScale = BalloonController.Instance.GetBalloonWorldScale(targetId);
                    Vector3 launchStartScale = dartObj.transform.localScale;

                    var proj = new DartProjectile
                    {
                        gameObject = dartObj,
                        targetPosition = travelTarget,
                        targetBalloonId = targetId,
                        color = color,
                        elapsed = 0f,
                        duration = ft
                    };
                    ConfigureLaunchScale(proj, launchStartScale, balloonScale);
                    _activeProjectiles.Add(proj);

                    // [ROLLBACK_DART_DOMOVE_DEAD_CALL]
                    // UpdateProjectiles 의 manual lerp 가 transform.position 을 덮어쓰므로 dead call. Ease 는 거기서 적용.
                    // Ease ease2 = GameManager.HasInstance ? GameManager.Instance.Board.dartFlightEase : Ease.Linear;
                    // dartObj.transform.DOMove(travelTarget, ft).SetEase(ease2);
                }
                else
                {
                    // ROLLBACK_DART_STABLE_OUTER_HIT:
                    // The old path had already removed the dart from the rail and reserved its target,
                    // but if no projectile visual was available it never resolved the hit. Resolve the
                    // original fire-time target immediately so this cannot become a silent miss.
                    _reservedTargets.Remove(targetId);
                    // ROLLBACK_DART_POP_SCAN_STATE_STABILITY:
                    // Visual fallback resolves the same fire-time target immediately. Do not clear the
                    // holder scan/promotion state here; ExecuteHit invalidates DirectionalTargeting's
                    // contour cache through BalloonController.ExecutePop.
                    ExecuteHit(targetId, color);
                }

                break; // Head-only scan fires at most one dart per scan tick.
#endif
            }

            // ROLLBACK_DART_FIRE_ALL_READY_HEADS:
            // Firing only one candidate per scan still lets another holder's valid line pass by while
            // it waits for the next frame. Fire each currently ready holder head once; exact-line
            // targeting plus target reservations still prevent same-target and same-holder repeats.
            // ROLLBACK_DART_FRONT_ORDERED_FIRE_QUEUE:
            // Fire candidates in current conveyor front-to-back order. The old fair-rotation start
            // index could process candidates as 5,4,2,1,3 even when the visible rail order was
            // 5,4,3,2,1. This only changes commit order; target reservation and consumed-line guards
            // still decide whether a candidate is allowed to hit.
            SortFireCandidatesByRailOrder(rail);
            int firedThisScan = 0;
            int initialCandidateCount = _fireCandidates.Count;
            int maxFireAttemptsThisScan = Mathf.Max(1, initialCandidateCount * (1 + MAX_POST_FIRE_HEAD_RESCANS_PER_HOLDER));
            // ROLLBACK_DART_COMMIT_INDEPENDENT_LINES_20260617:
            // A global one-fire cap makes every other valid holder wait until the next frame. On
            // dense/large maps the waiting heads can move past their exact target line and become
            // `behind`, which is the observed miss/deadlock path. Commit every independently valid
            // holder head found in this scan, while FireDartCandidate still blocks same holder,
            // same target, and same side-line. Rollback: restore MAX_FIRES_PER_FRAME here.
            int maxCommittedFiresThisScan = Mathf.Max(1, initialCandidateCount);
            for (int attempts = 0; _fireCandidates.Count > 0 && attempts < maxFireAttemptsThisScan && firedThisScan < maxCommittedFiresThisScan; attempts++)
            {
                SortFireCandidatesByRailOrder(rail);
                DartFireCandidate candidate = _fireCandidates[0];
                _fireCandidates.RemoveAt(0);

                if (_reservedTargets.Contains(candidate.targetId))
                {
                    LogAttackIssue(
                        "DartFireBlocked",
                        $"reason=targetReservedAtCommit holder={candidate.holderId} dartId={candidate.dartId} " +
                        $"target={candidate.targetId} scan={candidate.scanDir} line={candidate.scanLine} " +
                        $"candidates={_fireCandidates.Count}");
                    InvalidateDartScanLineForHolder(candidate.holderId);
                    continue;
                }

                if (FireDartCandidate(rail, candidate))
                {
                    firedThisScan++;
                    // ROLLBACK_DART_POST_FIRE_HEAD_RESCAN:
                    // Re-enabled for x2 cases. Straight rails can move ~0.5+ balloon line per normal
                    // 60fps frame, so gating this only to long frames still misses newly promoted
                    // heads on stage 1. Same-line peeling is blocked by holder/target line guards.
                    // ROLLBACK_DART_FRONT_ORDERED_FIRE_QUEUE:
                    // Queue the promoted head instead of firing it immediately so post-fire rescue
                    // still obeys the same visible front-to-back order.
                    if (ShouldRunPostFireHeadRescan())
                        TryQueuePromotedHeadFireCandidate(rail, candidate.holderId);
                }
            }

            _fireCandidates.Clear();
            RelieveStuckHolderLineLocks(rail);
            PruneConsumedTargetLinesForCurrentHeads(rail);
            RelieveDeadHeadStall(rail, firedThisScan);
            UpdateStallWatchdog(rail, firedThisScan);
            RelievePerHolderStallTimeout(rail); // ROLLBACK_DART_PERHOLDER_STALL_RELIEF_20260622
        }

        // DART_STALL_WATCHDOG (2026-06-11):
        // '매칭이 존재(HasOutermostMatch=true → fail 도 안 뜸)하는데 발사도 비행체도 일정 시간 0' 인
        // 정지 상태의 원인 불문 최후 안전망. 개별 안전망(데드헤드 릴리프 = head 색이 외곽에 없을 때만,
        // 라인락 릴리프 = holder pass 락만, 팝 기반 재개방 = 팝이 있어야 동작)이 못 덮는 조합 —
        // 예: 패킹 정지로 head 들이 라인을 못 건너고 + 현재 라인은 이미 소비됨 + 팝이 없어 재개방 없음 —
        // 에서 수동 홀더 선택이 하던 전체 스캔라인 무효화를 자동 수행해 현재 라인 재스캔을 강제한다.
        // 연속공격 안전: 비행체 0 + 발사 0 이 STALL_WATCHDOG_SECONDS 지속된 뒤에만 발동하므로
        // '직전 발사 직후 같은 라인 재발사' 위험 창과 겹치지 않는다 (발사/비행 발생 즉시 타이머 리셋).
        private float _stallWatchTimer;
        // ROLLBACK_DART_STALL_WATCHDOG_PROGRESS_DECOUPLE_20260617:
        // 워치독 타이머를 '발사/비행체 유무'가 아니라 '실제 보드 진행(풍선 pop)' 기준으로 리셋하기
        // 위한 신호. _lastPopUnscaledTime = 마지막 pop 시각(HandleBalloonPopped 에서 갱신),
        // _stallWatchLastPopSeen = 워치독이 직전 tick 에 관측한 pop 시각. 두 값이 다르면 'pop 발생 = 진행'.
        private float _lastPopUnscaledTime;
        private float _stallWatchLastPopSeen = -1f;
        private const float STALL_WATCHDOG_SECONDS = 1.5f;
        // ROLLBACK_DART_STALL_WATCHDOG_WIDEN_20260615: 매치가 없을 때(또는 sweep scope 불일치)
        // 발동하는 더 긴 타임아웃. belt 회전 지연/짧은 inter-fire 갭이 오발동하지 않도록 1.5s 보다 길게.
        private const float STALL_WATCHDOG_SECONDS_NO_MATCH = 3.0f;

        private void UpdateStallWatchdog(RailManager rail, int committedFiresThisScan)
        {
            if (PauseManager.IsPaused || rail == null || rail.EffectiveOccupiedCount == 0)
            {
                _stallWatchTimer = 0f;
                _stallWatchLastPopSeen = _lastPopUnscaledTime;
                return;
            }

            // ROLLBACK_DART_STALL_WATCHDOG_PROGRESS_DECOUPLE_20260617: START
            // 기존: 'committedFiresThisScan>0 || _activeProjectiles.Count>0' 이면 타이머 리셋.
            // 다중배포/다중클러스터에서 '다른' 클러스터가 발사·비행 중이면, 국소 정지(한 head 가
            // 패킹블록/라인락에 막힘)가 그 활동에 영구히 가려져 워치독이 영영 안 떠 데드락이 잔존했다.
            // → 리셋 기준을 '실제 보드 진행(풍선 pop)' 으로 변경. pop 이 있으면 진행 중 → 리셋,
            //   없으면 다른 클러스터의 발사/비행과 무관하게 누적. 단, 최후의 lock clear 는 아래에서
            //   여전히 '_activeProjectiles==0' 일 때만 수행 → 전체 ClearConsumedLineLocks 안전성(연속공격
            //   방지: in-flight 라인 재개방 없음)은 기존과 동일하게 보존. 비행체가 남아 있으면 타이머를
            //   유지한 채 대기하다 비행체가 빠지는 순간(웨이브 사이) 즉시 발동한다.
            // 롤백: 아래 START~END 를 다음으로 환원:
            //   if (committedFiresThisScan > 0 || _activeProjectiles.Count > 0 || PauseManager.IsPaused
            //       || rail == null || rail.EffectiveOccupiedCount == 0) { _stallWatchTimer = 0f; return; }
            //   _stallWatchTimer += Time.deltaTime;
            bool progressedSinceLastTick = _lastPopUnscaledTime > _stallWatchLastPopSeen;
            _stallWatchLastPopSeen = _lastPopUnscaledTime;
            if (progressedSinceLastTick)
            {
                _stallWatchTimer = 0f;
                return;
            }
            _stallWatchTimer += Time.deltaTime;
            // ROLLBACK_DART_STALL_WATCHDOG_PROGRESS_DECOUPLE_20260617: END

            // ROLLBACK_DART_STALL_WATCHDOG_WIDEN_20260615: START
            // 기존엔 HasOutermostMatchCached==true 일 때만 1.5s 후 발동했다. 그러나 그 sweep 은
            // DirectionalTargeting 실제 타겟 선정과 다른 계산이라, 'sweep=매치없음 인데 실제론 타겟불가-잔존'
            // scope 불일치 시 안전망이 영영 안 떠 silent 영구정지가 가능했다. → 매치 유무와 무관하게 발동하되,
            // 매치 없을 땐 더 긴 타임아웃 사용. 최후 동작에 consumed-line lock clear 추가 — 위 guard 로
            // 비행체 0 이 보장되므로(불변식: projectiles==0 ⇒ unresolvedConsumedLines==0, 1158-1160 동일 패턴)
            // in-flight 라인 재개방으로 인한 더블어택 위험 없음.
            // 롤백: 아래 START~END 를 다음 종전 코드로 교체:
            //   if (!BoardStateManager.HasInstance || !BoardStateManager.Instance.HasOutermostMatchCached) { _stallWatchTimer = 0f; return; }
            //   if (_stallWatchTimer < STALL_WATCHDOG_SECONDS) return;
            //   _stallWatchTimer = 0f; InvalidateDartScanLines();
            //   LogAttackIssue("DartStallWatchdog", $"no fire/projectile for {STALL_WATCHDOG_SECONDS:F1}s with match present — full scan-line invalidate");
            bool matchPresent = BoardStateManager.HasInstance && BoardStateManager.Instance.HasOutermostMatchCached;
            float threshold = matchPresent ? STALL_WATCHDOG_SECONDS : STALL_WATCHDOG_SECONDS_NO_MATCH;
            if (_stallWatchTimer < threshold) return;

            // ROLLBACK_DART_STALL_WATCHDOG_PROGRESS_DECOUPLE_20260617:
            // 타이머는 진행(pop) 기준으로 누적하지만, 전체 ClearConsumedLineLocks 는 in-flight 라인까지
            // 비우므로 '비행체 0' 일 때만 수행해야 연속공격(비행 중 라인 재개방)이 없다. 비행체가 남아
            // 있으면 타이머를 유지(리셋 X)한 채 대기 → 비행체가 빠지는 즉시 같은 frame 경로로 발동.
            // (불변식: _activeProjectiles==0 ⇒ _unresolvedConsumedTargetLines==0, 1158-1160 동일 패턴)
            if (_activeProjectiles.Count > 0)
                return;
            _stallWatchTimer = 0f;

            InvalidateDartScanLines();
            ClearConsumedLineLocks();   // 비행체 0 보장 하에서만 도달 → 라인 재개방 안전
            LogAttackIssue("DartStallWatchdog",
                $"no board-progress(pop) for {threshold:F1}s (match={matchPresent}) — scan-line + consumed-lock clear");
            // ROLLBACK_DART_STALL_WATCHDOG_WIDEN_20260615: END
        }

        // ROLLBACK_DART_PERHOLDER_STALL_RELIEF_20260622:
        // 정적 감사 MED #2/#3 보완. 두 구멍을 메운다:
        //   #3 starvation: 전역 Stall 워치독(_stallWatchTimer)은 '아무 클러스터의 pop' 에 리셋되므로, 한 클러스터가
        //      계속 pop 하면 국소 정지된 다른 holder 가 영구히 lock 해제를 못 받아 그 색 풍선이 놓침이 된다.
        //   #2 dead-head scope 불일치: RelieveDeadHeadStall 은 head 색이 'GetReachableOutermostColors(side-sweep 윤곽)'
        //      에 있으면 "belt 가 곧 도달" 으로 보고 스킵하지만, 실제 DirectionalTargeting.TryFindTarget 이
        //      은닉/Ice/Wall/예약 으로 거부하면 head 는 못 쏘는 채 남아 전역 워치독(=starvation 가능)에만 의존한다.
        // 해결(per-holder, 최후 안전망): head 색이 board 도달가능 + 이 holder 가 PER_HOLDER_STALL_RELIEF_SECONDS 동안
        //   미발사 + 비행체 0 + 외곽 매칭 존재 이면, 그 holder 의 라인락만 해제 + scan 라인 무효화.
        // 연속공격 안전: (1) ClearConsumedLineLockForHolder 는 holder-local pass-lock 만 건드림 → 전역 in-flight 락
        //   (_unresolvedConsumedTargetLines) 불변이라 비행 중 라인 재개방 없음. (2) _activeProjectiles==0 추가 가드.
        //   (3) 임계 2.0s 는 stuck-line(0.4s)/dead-head(0.4s)/전역워치독(1.5s) 보다 길어 최후 발동 + 정상 발사 시 리셋.
        // 트리거가 좁아(도달가능+2s 무발사+매칭존재) 정상 플레이엔 작동 안 함. 롤백: 이 메서드 + 아래 2 필드 + 1814 호출 제거.
        private readonly Dictionary<int, float> _holderStallSince = new Dictionary<int, float>(16);
        private const float PER_HOLDER_STALL_RELIEF_SECONDS = 2.0f;

        private void RelievePerHolderStallTimeout(RailManager rail)
        {
            if (rail == null || _activeProjectiles.Count > 0) return;
            if (!BoardStateManager.HasInstance || !BoardStateManager.Instance.HasOutermostMatchCached)
            {
                _holderStallSince.Clear(); // fail 영역/매칭 없음 — 타이머 리셋(오발동 방지).
                return;
            }

            // 재사용 set 스냅샷 (pop 이벤트 중 내용 변동 방지 — RelieveDeadHeadStall 과 동일 패턴).
            _deadHeadReachableColors.Clear();
            foreach (int c in BoardStateManager.Instance.GetReachableOutermostColors())
                _deadHeadReachableColors.Add(c);

            rail.GetClusterHeadDarts(_scanHeadDarts);
            float now = Time.unscaledTime;
            for (int i = 0; i < _scanHeadDarts.Count; i++)
            {
                var head = _scanHeadDarts[i];
                if (head == null || head.dartColor < 0) continue;
                int holderId = head.holderId;

                // 이 tick 발사 = 진행 중 → 타이머 리셋.
                if (_firedHoldersThisTick.Contains(holderId)) { _holderStallSince.Remove(holderId); continue; }
                // head 색이 외곽 도달불가 = RelieveDeadHeadStall 담당 영역 → 여기선 제외.
                if (!_deadHeadReachableColors.Contains(head.dartColor)) { _holderStallSince.Remove(holderId); continue; }

                if (!_holderStallSince.TryGetValue(holderId, out float since)) { _holderStallSince[holderId] = now; continue; }
                if (now - since < PER_HOLDER_STALL_RELIEF_SECONDS) continue;

                // 도달가능으로 보고된 색인데 2s 동안 못 쏨 → holder-local 락 해제 + scan 라인 무효화로 재스캔 강제.
                ClearConsumedLineLockForHolder(holderId);
                InvalidateDartScanLineForHolder(holderId);
                _holderStallSince.Remove(holderId);
                LogAttackIssue("DartPerHolderStallRelief",
                    $"holder={holderId} headColor={head.dartColor} — reachable-but-no-fire {PER_HOLDER_STALL_RELIEF_SECONDS:F1}s → holder lock clear + scanline invalidate");
            }
        }

        // DEAD_HEAD_RELIEF (2026-06-10):
        // fail 판정(HasOutermostMatch)은 '레일 전체 다트 색 ∩ 레일면 외곽 색'으로 매칭을 보는데,
        // 발사는 holder 별 cluster head 만 후보다. head 색이 현재 레일면 외곽 어디에도 없으면
        // head 는 보드가 변하기 전까지 발사 불가 → 뒤의 매칭 다트도 영원히 차단 →
        // '실패도 안 뜨고 공격도 안 하는' 영구 순환이 생긴다.
        // 복구 원칙 (놓침/연속공격/부하 0):
        //   - 정상 head-only 스캔·catch-up·wrap 로직은 일절 변경하지 않음 (놓침 회귀 차단).
        //   - 이번 tick 정상 발사 0 + head 가 DEAD_HEAD_OBSERVE_SECONDS 동안 계속 dead 일 때만,
        //     그 클러스터의 front-most 매칭 다트 1발을 기존 commit 경로(FireDartCandidate)로 발사.
        //   - holder 당 릴리프 발사 간격 ≥ DEAD_HEAD_RELIEF_FIRE_INTERVAL + 전역 tick 당 1발
        //     + 기존 target/holder line lock·target 예약 그대로 적용 → 연속공격(스택 즉시 벗기기) 불가.
        //   - 발사가 전혀 없는 tick 에만 동작 + 재사용 컬렉션만 사용(alloc 0) → 프레임 부하 무시 가능.
        private const float DEAD_HEAD_OBSERVE_SECONDS = 0.4f;
        private const float DEAD_HEAD_RELIEF_FIRE_INTERVAL = 0.4f;

        private void RelieveDeadHeadStall(RailManager rail, int committedFiresThisScan)
        {
            // ROLLBACK_DART_DEADHEAD_PERHOLDER_UNGATE_20260617: START
            // 기존: 'if (committedFiresThisScan > 0) return;' — 이 tick 에 '다른' 클러스터가 1발이라도
            // 쏘면 dead-head 릴리프 전체를 스킵. 다중클러스터에서 '색이 외곽에 없는' head 가 영구히
            // 릴리프를 못 받아 그 색 풍선이 간헐 놓침이 됐다. → 전역 게이트를 제거하고 per-holder 로 강등:
            // 아래 루프에서 '이 tick 에 실제 발사한 holder' 만 스킵(+dead 타이머 리셋)한다. 기존 per-holder
            // 안전장치(DEAD_HEAD_OBSERVE 0.4s 연속 dead + RELIEF_FIRE_INTERVAL 0.4s + IsTargetLineConsumed/
            // IsHolderLineConsumed + target reservation + tick 당 전역 1발)가 그대로라, 진행 중인 클러스터의
            // 스택을 즉시 벗기는 연속공격은 발생할 수 없다. (RelieveStuckHolderLineLocks 의 per-holder
            // _firedHoldersThisTick 스킵과 동일한 패턴.)
            // 롤백: 이 블록을 'if (committedFiresThisScan > 0) return;' 한 줄로 환원하고, 루프 상단의
            //       ROLLBACK_DART_DEADHEAD_PERHOLDER_SKIP 블록도 제거.
            // ROLLBACK_DART_DEADHEAD_PERHOLDER_UNGATE_20260617: END
            if (!BoardStateManager.HasInstance) return;

            // 스냅샷 복사 — BoardStateManager 의 재사용 set 은 pop 이벤트 처리 중 내용이 바뀔 수 있음.
            _deadHeadReachableColors.Clear();
            foreach (int c in BoardStateManager.Instance.GetReachableOutermostColors())
                _deadHeadReachableColors.Add(c);
            if (_deadHeadReachableColors.Count == 0)
                return; // 외곽 매칭 자체가 없음 — HasOutermostMatch=false → fail 판정 영역.

            rail.GetClusterHeadDarts(_scanHeadDarts);
            _scanHeadDarts.Sort(CompareDartPlacedSeq);
            float now = Time.unscaledTime;

            for (int i = 0; i < _scanHeadDarts.Count; i++)
            {
                var head = _scanHeadDarts[i];
                if (head == null || head.dartColor < 0) continue;
                int holderId = head.holderId;

                // ROLLBACK_DART_DEADHEAD_PERHOLDER_SKIP_20260617:
                // 이 tick 에 실제 발사한 holder = 진행 중 → 릴리프 대상 아님 (제거된 전역 게이트의
                // per-holder 대체). dead 타이머도 리셋해 정상 발사가 재개되면 자연 소멸.
                if (_firedHoldersThisTick.Contains(holderId))
                {
                    _deadHeadSince.Remove(holderId);
                    continue;
                }

                if (_deadHeadReachableColors.Contains(head.dartColor))
                {
                    // head 색이 외곽에 존재 — belt 회전이 결국 그 라인에 도달하므로 릴리프 대상 아님.
                    _deadHeadSince.Remove(holderId);
                    continue;
                }

                if (!_deadHeadSince.TryGetValue(holderId, out float since))
                {
                    _deadHeadSince[holderId] = now;
                    continue;
                }
                if (now - since < DEAD_HEAD_OBSERVE_SECONDS) continue;
                if (_deadHeadLastReliefFireAt.TryGetValue(holderId, out float lastFire)
                    && now - lastFire < DEAD_HEAD_RELIEF_FIRE_INTERVAL)
                    continue;

                var sub = rail.GetFrontmostFireableDart(holderId, _deadHeadReachableColors);
                if (sub == null || sub.dartId == head.dartId) continue;

                rail.GetDartCurrentPose(sub, out Vector3 subPos, out _, out Vector3 subFireDir);
                if (!DirectionalTargeting.TryFindTarget(
                        subPos, subFireDir, sub.dartColor, _reservedTargets,
                        out int targetId, out var scanDir, out int targetLine, out Vector3 targetPos))
                    continue;

                if (IsTargetLineConsumed(scanDir, targetLine)) continue;
                if (IsHolderLineConsumed(holderId, scanDir, targetLine)) continue;

                var candidate = new DartFireCandidate
                {
                    isValid = true,
                    dart = sub,
                    dartPos = subPos,
                    scanDartPos = subPos,
                    fireDir = subFireDir,
                    holderId = holderId,
                    dartId = sub.dartId,
                    color = sub.dartColor,
                    targetId = targetId,
                    scanLine = targetLine,
                    scanDir = scanDir,
                    selectedTargetPos = targetPos,
                    findTargetDiag = DirectionalTargeting.LastFindTargetDiag,
                    allowNonHeadCommit = true
                };

                if (FireDartCandidate(rail, candidate))
                {
                    _deadHeadLastReliefFireAt[holderId] = now;
                    // 다음 릴리프도 dead 관찰 시간부터 다시 시작 (정상 발사가 재개되면 자연 소멸).
                    _deadHeadSince.Remove(holderId);
                    LogAttackIssue(
                        "DartDeadHeadRelief",
                        $"holder={holderId} headDart={head.dartId} headColor={head.dartColor} " +
                        $"subDart={sub.dartId} subColor={sub.dartColor} target={targetId} " +
                        $"scan={scanDir} line={targetLine}");
                    break; // 릴리프는 tick 당 전역 1발.
                }
            }
        }

        // ROLLBACK_DART_STUCK_HOLDER_LINE_RELIEF:
        // holder pass lock(IsHolderLineConsumed)은 한 pass 동안 같은 라인 재발사를 막아 연속공격
        // (같은 색 스택 즉시 벗기기)을 방지한다. 그런데 그 라인의 바깥 풍선을 깐 뒤 '새 외곽'이
        // 노출되면, 같은 라인이라 lock 에 막혀 그 색 holder 가 유일할 경우 pass 리셋(wrap/코너)까지
        // 영구 대기 → "공격 놓침"(reason=holderLineConsumed, 2026-06-01 로그 확정).
        // 해결: head 가 '자기 pass lock 에 막혀' 일정 시간(unscaled) 발사 못 하면 '그 라인만' 잠금 해제해
        // 재공격을 허용. 정상 플레이(쏠 unconsumed 라인이 남음)에선 매 tick fire → streak 리셋이라
        // 절대 작동 안 하고, 스택은 임계 간격으로만 풀려 연속공격이 재발하지 않는다.
        private void RelieveStuckHolderLineLocks(RailManager rail)
        {
            rail.GetClusterHeadDarts(_scanHeadDarts);
            for (int i = 0; i < _scanHeadDarts.Count; i++)
            {
                var head = _scanHeadDarts[i];
                if (head == null || head.dartColor < 0) continue;
                int holderId = head.holderId;

                // 발사 성공 → 진행 중이므로 stuck 타이머 해제. (타이머는 '발사할 때만' 리셋.)
                if (_firedHoldersThisTick.Contains(holderId))
                {
                    ClearHolderLineStuckState(holderId);
                    continue;
                }

                rail.GetDartCurrentPose(head, out Vector3 pos, out _, out Vector3 fireDir);
                var scanDir = DirectionalTargeting.DetermineScanDirection(fireDir);
                int line = GetScanLine(pos, scanDir);

                bool isCurrentlyConsumed = IsHolderLineConsumed(holderId, scanDir, line);
                if (!isCurrentlyConsumed)
                {
                    ClearHolderLineStuckState(holderId);
                    continue;
                }

                if (!_holderLineStuckSince.TryGetValue(holderId, out float since))
                {
                    // 자기 pass lock 에 막힌 순간부터 타이머 시작.
                    // (이전 버그: head 가 sweep 중 빈 라인을 지날 때 타이머를 리셋해 0.4s 도달 불가.
                    //  → 이제 발사 전까지 타이머를 유지한다.)
                    SetHolderLineStuckState(holderId, scanDir, line, Time.unscaledTime);
                    continue;
                }

                // ROLLBACK_DART_STUCK_LINE_KEYED_RELIEF:
                // A holder head moves while the timer is running. If it reaches another direction
                // or line, restart the timer instead of treating the new line as the same stuck lock.
                // ROLLBACK_DART_STUCK_LINE_JITTER_TOLERANCE_20260616:
                //   정지한 head 의 GetScanLine 이 .5 lattice 경계에서 N↔N±1 로 지터하면(부동소수/belt 미세 nudge)
                //   기존 'stuckLine == line' 정확비교가 매 프레임 타이머를 리셋 → 0.4s 도달 불가 → pass-lock 영구
                //   잔존(그 라인 영영 재스캔 X = 국소 데드락). 인접 ±1 을 같은 정지로 허용해 지터만 흡수한다.
                //   (연속 advance >1 라인은 여전히 리셋되어 정상 이동과 구분 — 타게팅/발사 로직 불변, 놓침/연속공격 무영향.)
                //   롤백: `Mathf.Abs(stuckLine - line) <= 1` 를 `stuckLine == line` 로 환원.
                bool sameStuckLine =
                    _holderLineStuckDirection.TryGetValue(holderId, out var stuckDir)
                    && stuckDir == scanDir
                    && _holderLineStuckLine.TryGetValue(holderId, out int stuckLine)
                    && Mathf.Abs(stuckLine - line) <= 1;
                if (!sameStuckLine)
                {
                    SetHolderLineStuckState(holderId, scanDir, line, Time.unscaledTime);
                    continue;
                }

                // 임계 시간 동안 '한 번도 발사 못 함' → pass lock 전체 해제 (코너 전환과 동일 효과).
                // 한 sweep 동안 각 라인을 한 번씩만 재발사하고 즉시 재-consume 되므로, 같은 라인
                // 연속 재발사(스택 즉시 벗기기)는 발생하지 않는다 → "놓침 X, 연속공격 X" 동시 충족.
                if (Time.unscaledTime - since >= HOLDER_LINE_STUCK_RESET_SECONDS)
                {
                    ClearConsumedLineLockForHolder(holderId);
                    // ROLLBACK_DART_STUCK_LINE_KEYED_RELIEF:
                    // New holder placement fixes the deadlock because it seeds a fresh scan line.
                    // Do the same for the stuck holder after clearing its pass lock so the next scan
                    // does not reuse stale same-head/same-line cache or an obsolete promo seed.
                    InvalidateDartScanLineForHolder(holderId);
                    ClearHolderLineStuckState(holderId);
                    LogAttackIssue("DartStuckHolderLineReset", $"holder={holderId} scan={scanDir} line={line}");
                }
            }
        }

        // ROLLBACK_DART_STUCK_LINE_KEYED_RELIEF:
        // Remove these helpers and write only _holderLineStuckSince if holder-only stuck relief is restored.
        private void SetHolderLineStuckState(
            int holderId,
            DirectionalTargeting.ScanDirection scanDir,
            int line,
            float since)
        {
            _holderLineStuckSince[holderId] = since;
            _holderLineStuckDirection[holderId] = scanDir;
            _holderLineStuckLine[holderId] = line;
        }

        private void ClearHolderLineStuckState(int holderId)
        {
            _holderLineStuckSince.Remove(holderId);
            _holderLineStuckDirection.Remove(holderId);
            _holderLineStuckLine.Remove(holderId);
        }

        private bool ShouldRunPostFireHeadRescan()
        {
            if (!RailManager.HasInstance) return false;
            return RailManager.Instance.UserSpeedMultiplier >= POST_FIRE_HEAD_RESCAN_MIN_SPEED;
        }

        private void SortFireCandidatesByRailOrder(RailManager rail)
        {
            int count = _fireCandidates.Count;
            if (count <= 1) return;

            for (int i = 1; i < count; i++)
            {
                DartFireCandidate candidate = _fireCandidates[i];
                int j = i - 1;
                while (j >= 0 && ComesAfterInRailProgressOrder(_fireCandidates[j], candidate))
                {
                    _fireCandidates[j + 1] = _fireCandidates[j];
                    j--;
                }
                _fireCandidates[j + 1] = candidate;
            }

            float pathLen = rail != null ? rail.TotalPathLength : 0f;
            if (pathLen <= 0.0001f || count <= 1) return;

            float highest = _fireCandidates[0].dart != null ? _fireCandidates[0].dart.progress : 0f;
            float lowest = _fireCandidates[count - 1].dart != null ? _fireCandidates[count - 1].dart.progress : highest;
            if ((highest - lowest) <= pathLen * 0.5f) return;

            int headPos = 0;
            float maxGap = -1f;
            for (int p = 0; p < count; p++)
            {
                int prevPos = p == 0 ? count - 1 : p - 1;
                float curProg = _fireCandidates[p].dart != null ? _fireCandidates[p].dart.progress : 0f;
                float prevProg = _fireCandidates[prevPos].dart != null ? _fireCandidates[prevPos].dart.progress : curProg;
                float gap = prevProg - curProg;
                if (gap < 0f) gap += pathLen;
                if (gap > maxGap)
                {
                    maxGap = gap;
                    headPos = p;
                }
            }

            RotateFireCandidatesLeft(headPos);
        }

        private static bool ComesAfterInRailProgressOrder(DartFireCandidate current, DartFireCandidate candidate)
        {
            float currentProgress = current.dart != null ? current.dart.progress : 0f;
            float candidateProgress = candidate.dart != null ? candidate.dart.progress : 0f;
            float delta = currentProgress - candidateProgress;
            if (Mathf.Abs(delta) > 0.0001f)
                return currentProgress < candidateProgress;

            long currentSeq = current.dart != null ? current.dart.placedSeq : long.MaxValue;
            long candidateSeq = candidate.dart != null ? candidate.dart.placedSeq : long.MaxValue;
            return currentSeq > candidateSeq;
        }

        private void RotateFireCandidatesLeft(int startIndex)
        {
            int count = _fireCandidates.Count;
            if (startIndex <= 0 || startIndex >= count) return;

            for (int i = 0; i < startIndex; i++)
            {
                DartFireCandidate first = _fireCandidates[0];
                _fireCandidates.RemoveAt(0);
                _fireCandidates.Add(first);
            }
        }

        private bool TryQueuePromotedHeadFireCandidate(RailManager rail, int holderId)
        {
            // ROLLBACK_DART_COMMIT_INDEPENDENT_LINES_20260617:
            // Same-scan promoted-head firing is the dangerous path for same-holder continuous
            // peeling. The promotion seed already replays crossed lines on the next scan.
            if (_firedHoldersThisTick.Contains(holderId))
                return false;

            if (_postFireQueuedHoldersThisTick.Contains(holderId))
                return false;

            if (_lastDeployPlacedFrameByHolder.TryGetValue(holderId, out int deployFrame)
                && deployFrame == Time.frameCount)
            {
                LogAttackIssue(
                    "DartFireBlocked",
                    $"reason=deployFramePromotedHeadGuard holder={holderId} frame={Time.frameCount}");
                return false;
            }

            if (!TryBuildCurrentHeadFireCandidate(rail, holderId, out DartFireCandidate candidate))
                return false;

            _postFireQueuedHoldersThisTick.Add(holderId);
            _fireCandidates.Add(candidate);
            return true;
        }

        private int FireNewlyPromotedHeadIfReady(RailManager rail, int holderId)
        {
            int fired = 0;
            for (int i = 0; i < MAX_POST_FIRE_HEAD_RESCANS_PER_HOLDER; i++)
            {
                if (!TryBuildCurrentHeadFireCandidate(rail, holderId, out DartFireCandidate candidate))
                    break;

                if (!FireDartCandidate(rail, candidate))
                    break;

                fired++;
            }

            return fired;
        }

        private bool TryBuildCurrentHeadFireCandidate(RailManager rail, int holderId, out DartFireCandidate candidate)
        {
            candidate = default;
            if (rail == null || !BalloonController.HasInstance) return false;

            RailManager.DartOnRail dart = rail.GetClusterHeadDart(holderId);
            if (dart == null || dart.dartColor < 0) return false;

            rail.GetDartCurrentPose(dart, out Vector3 dartPos, out Vector3 scanTangent, out Vector3 fireDir);
            DirectionalTargeting.ScanDirection scanDir = DirectionalTargeting.DetermineScanDirection(fireDir);
            int currentLine = GetScanLine(dartPos, scanDir);

            // ROLLBACK_DART_POST_FIRE_HEAD_RESCAN:
            // This path is only for the head that appears immediately after a fire. Do not replay a
            // previously scanned same-head/same-line state; that would re-open continuous attacks.
            if (_lastScannedLineByHolder.TryGetValue(holderId, out int lastLine)
                && _lastScanDirectionByHolder.TryGetValue(holderId, out DirectionalTargeting.ScanDirection lastDir)
                && _lastScannedHeadIdByHolder.TryGetValue(holderId, out int lastHeadId)
                && lastHeadId == dart.dartId
                && lastDir == scanDir
                && lastLine == currentLine)
            {
                return false;
            }

            if (!DirectionalTargeting.TryFindTarget(
                    dartPos,
                    fireDir,
                    dart.dartColor,
                    _reservedTargets,
                    out int targetId,
                    out DirectionalTargeting.ScanDirection targetScanDir,
                    out int targetLine,
                    out Vector3 selectedTargetPos))
            {
                return false;
            }

            BalloonData targetData = BalloonController.Instance.GetBalloon(targetId);
            if (targetData == null || targetData.isPopped)
                return false;

            candidate = new DartFireCandidate
            {
                isValid = true,
                dart = dart,
                dartPos = dartPos,
                scanDartPos = dartPos,
                fireDir = fireDir,
                holderId = holderId,
                dartId = dart.dartId,
                color = dart.dartColor,
                targetId = targetId,
                scanLine = targetLine,
                scanDir = targetScanDir,
                selectedTargetPos = selectedTargetPos,
                findTargetDiag = DirectionalTargeting.LastFindTargetDiag,
                allowNonHeadCommit = false
            };

            return true;
        }

        private bool FireDartCandidate(RailManager rail, DartFireCandidate candidate)
        {
            if (!BalloonController.HasInstance) return false;
            if (rail == null) return false;

            // ROLLBACK_DART_COMMIT_INDEPENDENT_LINES_20260617:
            // This is the hard 1-dart-per-holder-per-scan gate. It prevents a promoted head from
            // firing in the same scan that removed the previous head, which was the same-holder
            // continuous-attack/penetration risk.
            if (_firedHoldersThisTick.Contains(candidate.holderId))
            {
                LogAttackIssue(
                    "DartFireBlocked",
                    $"reason=holderAlreadyFiredAtCommit holder={candidate.holderId} dartId={candidate.dartId} " +
                    $"target={candidate.targetId} scan={candidate.scanDir} line={candidate.scanLine}");
                return false;
            }

            RailManager.DartOnRail liveDart = rail.FindDart(candidate.dartId);
            if (liveDart == null || liveDart != candidate.dart)
            {
                LogAttackIssue(
                    "DartMissBlocked",
                    $"reason=staleCandidateDart holder={candidate.holderId} dartId={candidate.dartId} " +
                    $"target={candidate.targetId} scan={candidate.scanDir} line={candidate.scanLine}");
                InvalidateDartScanLineForHolder(candidate.holderId);
                return false;
            }

            if (!candidate.allowNonHeadCommit)
            {
                var currentHead = rail.GetClusterHeadDart(candidate.holderId);
                if (currentHead == null || currentHead.dartId != candidate.dartId)
                {
                    LogAttackIssue(
                        "DartMissBlocked",
                        $"reason=staleCandidateNotHead holder={candidate.holderId} dartId={candidate.dartId} " +
                        $"head={(currentHead != null ? currentHead.dartId.ToString() : "null")} " +
                        $"target={candidate.targetId} scan={candidate.scanDir} line={candidate.scanLine}");
                    InvalidateDartScanLineForHolder(candidate.holderId);
                    return false;
                }
            }

            if (_reservedTargets.Contains(candidate.targetId))
            {
                LogAttackIssue(
                    "DartFireBlocked",
                    $"reason=targetReservedAtFire holder={candidate.holderId} dartId={candidate.dartId} " +
                    $"target={candidate.targetId} scan={candidate.scanDir} line={candidate.scanLine}");
                InvalidateDartScanLineForHolder(candidate.holderId);
                return false;
            }
            if (IsTargetLineConsumed(candidate.scanDir, candidate.scanLine))
            {
                LogAttackIssue(
                    "DartFireBlocked",
                    $"reason=targetLineConsumed stage=fire holder={candidate.holderId} dartId={candidate.dartId} " +
                    $"target={candidate.targetId} scan={candidate.scanDir} line={candidate.scanLine}");
                return false;
            }
            if (IsHolderLineConsumed(candidate.holderId, candidate.scanDir, candidate.scanLine))
            {
                LogAttackIssue(
                    "DartFireBlocked",
                    $"reason=holderLineConsumed stage=fire holder={candidate.holderId} dartId={candidate.dartId} " +
                    $"target={candidate.targetId} scan={candidate.scanDir} line={candidate.scanLine}");
                return false;
            }

            BalloonData targetData = BalloonController.Instance.GetBalloon(candidate.targetId);
            if (targetData == null || targetData.isPopped)
            {
                LogAttackIssue(
                    "DartMissBlocked",
                    $"reason=targetGoneAtFire holder={candidate.holderId} dartId={candidate.dartId} " +
                    $"target={candidate.targetId} popped={(targetData != null && targetData.isPopped)} " +
                    $"scan={candidate.scanDir} line={candidate.scanLine}");
                InvalidateDartScanLineForHolder(candidate.holderId);
                return false;
            }

            _lastFiredHolderId = candidate.holderId;

            // ROLLBACK_CONTOUR_TARGET_DIAG:
            if (DART_TARGETING_DEBUG)
            {
                Debug.Log($"[FindTargetPick] mode=dart holder={candidate.holderId} dartId={candidate.dartId} " +
                          $"progress={candidate.dart.progress:F2} {candidate.findTargetDiag}");
            }

            var fireHead = rail.GetClusterHeadDart(candidate.holderId);
            int fireSlot = rail.GetSlotAtPathDistance(candidate.dart.progress);
            var fireSlotData = rail.GetSlot(fireSlot);
            if (DART_TARGETING_DEBUG)
            {
                Debug.Log($"[DartFire] holder={candidate.holderId} dartId={candidate.dartId} color={candidate.color} " +
                          $"progress={candidate.dart.progress:F2} scanLine={candidate.scanLine} slot={fireSlot} slotState=holder{fireSlotData.holderId}/dart{fireSlotData.dartId}/color{fireSlotData.dartColor} " +
                          $"head={(fireHead != null ? fireHead.dartId.ToString() : "null")} target={candidate.targetId} " +
                          $"gapAfterRemove={rail.GetPlacementGapDebugInfo(candidate.dart.progress, candidate.color, candidate.holderId, candidate.dartId)} " +
                          $"advance={rail.GetAdvanceModeDebugInfo()}");
            }

            bool removedFromRail = rail.RemoveDartById(candidate.dartId);
            if (DART_TARGETING_DEBUG)
            {
                Debug.Log($"[DartFireAfterRemove] holder={candidate.holderId} dartId={candidate.dartId} removed={removedFromRail} " +
                          $"advance={rail.GetAdvanceModeDebugInfo()}");
            }
            if (!removedFromRail)
            {
                LogAttackIssue(
                    "DartMissBlocked",
                    $"reason=removeDartFailed holder={candidate.holderId} dartId={candidate.dartId} " +
                    $"target={candidate.targetId} scan={candidate.scanDir} line={candidate.scanLine}");
                InvalidateDartScanLineForHolder(candidate.holderId);
                return false;
            }

            // ROLLBACK_DART_SCAN_CACHE_AFTER_FIRE:
            // A fire candidate can still lose at execution time because an earlier holder in the
            // same scan tick consumed the same side/line. Caching at candidate-build time made that
            // losing holder think the line had been processed, so it skipped the refreshed outer cell
            // later, especially at x2. Record the accepted line only after the dart was actually
            // removed from the rail and the fire is committed.
            _lastScannedLineByHolder[candidate.holderId] = candidate.scanLine;
            _lastScanDirectionByHolder[candidate.holderId] = candidate.scanDir;
            _lastScannedHeadIdByHolder[candidate.holderId] = candidate.dartId;

            // ROLLBACK_PROMOTION_SEED_CATCH_UP:
            // RemoveDartById 가 RemoveFromClusterHeadCache → RebuildClusterHeadCache 를 동기 호출해
            // 새 head 가 즉시 승격되었다. 새 head 의 현재 line 을 seed 로 캡처해 두면, 다음 scan tick
            // 의 catch-up clause 가 promotion-time line ~ currentLine 사이 crossed exact line 들을
            // 정상적으로 replay 할 수 있다. _lastScanned* 는 갱신하지 않는다 (anti-pattern 회피).
            var promotedHead = rail.GetClusterHeadDart(candidate.holderId);
            if (promotedHead != null && promotedHead.dartColor >= 0)
            {
                rail.GetDartCurrentPose(promotedHead, out Vector3 promoPos, out _, out Vector3 promoFireDir);
                var promoScanDir = DirectionalTargeting.DetermineScanDirection(promoFireDir);
                int promoCurrentLine = GetScanLine(promoPos, promoScanDir);
                int promoSeedLine = promoCurrentLine;

                // ROLLBACK_DART_PROMO_SEED_FROM_FIRED_LINE:
                // At x2 a newly promoted head can already be several exact lines past the dart that
                // just fired. Seeding from the promoted head's current line skips the in-between
                // lines (ex: fired line 9, promoted current line 12 => line 10/11 never probed).
                // If the side did not change, replay starts at the first line after the fired line
                // in the promoted head's travel direction, then continues through promoCurrentLine.
                if (promoScanDir == candidate.scanDir)
                {
                    int delta = promoCurrentLine - candidate.scanLine;
                    if (delta != 0)
                        promoSeedLine = candidate.scanLine + (delta > 0 ? 1 : -1);
                }

                _promoHeadByHolder[candidate.holderId] = promotedHead.dartId;
                _promoDirByHolder[candidate.holderId]  = promoScanDir;
                _promoLineByHolder[candidate.holderId] = promoSeedLine;

                LogAttackIssue(
                    "DartPromoHeadSeed",
                    $"holder={candidate.holderId} firedDart={candidate.dartId} nextDart={promotedHead.dartId} " +
                    $"scan={promoScanDir} firedLine={candidate.scanLine} seedLine={promoSeedLine} " +
                    $"currentLine={promoCurrentLine}");
            }
            else
            {
                ClearPromoSeedForHolder(candidate.holderId);
            }

            _firedHoldersThisTick.Add(candidate.holderId);
            MarkHolderLineConsumed(candidate.holderId, candidate.scanDir, candidate.scanLine);
            // ROLLBACK_DART_GLOBAL_CONSUMED_LINE_LOCK:
            // Reserve the whole side/line after a fire, not only the exact balloon id. This prevents
            // another holder from immediately attacking the newly exposed inner cell on the same row.
            // ROLLBACK_DART_HOLDER_LINE_LOCK_ONLY:
            // The global line lock made holder/cluster A suppress holder/cluster B on the same
            // row/column, which recreated the "one-by-one miss" after another cluster passed a
            // corner/tunnel. Keep only the holder-local last-fired line above.
            // _consumedTargetLines.Add(GetConsumedLineKey(candidate.scanDir, candidate.scanLine));
            MarkTargetLineConsumed(candidate.holderId, candidate.scanDir, candidate.scanLine);
            _reservedTargets.Add(candidate.targetId);
            LogAttackIssue(
                "DartFireCommitted",
                $"holder={candidate.holderId} dartId={candidate.dartId} color={candidate.color} " +
                $"target={candidate.targetId} scan={candidate.scanDir} line={candidate.scanLine} " +
                $"progress={candidate.dart.progress:F2} activeProjectiles={_activeProjectiles.Count} " +
                $"reservedTargets={_reservedTargets.Count} unresolvedLines={_unresolvedConsumedTargetLines.Count}");

            GameObject dartObj = null;
            if (_dartVisuals.TryGetValue(candidate.dartId, out var visual))
            {
                dartObj = visual.gameObject;
                _dartVisuals.Remove(candidate.dartId);
            }

            if (dartObj == null && ObjectPoolManager.HasInstance)
                dartObj = ObjectPoolManager.Instance.Get(DART_POOL_KEY, candidate.dartPos, Quaternion.identity);

            if (dartObj != null)
            {
                float ds = GameManager.HasInstance ? GameManager.Instance.Board.dartScale : 1f;
                dartObj.transform.localScale = Vector3.one * ds;
                dartObj.transform.DOKill();
                ApplyColor(dartObj, candidate.color);

                EventBus.Publish(new OnDartFired { dartId = candidate.dartId, holderId = -1, color = candidate.color });

                Vector3 targetPos = candidate.selectedTargetPos;
                Vector3 launchPos = CalculateCardinalLaunchPosition(candidate.scanDartPos, targetPos, candidate.scanDir);
                dartObj.transform.position = launchPos;
                Vector3 travelTarget = CalculateCardinalTarget(launchPos, targetPos, candidate.scanDir);
                Vector3 dir = travelTarget - launchPos;
                dir.y = 0f;
                if (dir.sqrMagnitude > 0.001f)
                    dartObj.transform.rotation = Quaternion.LookRotation(dir.normalized);

                float ft = CalculateProjectileFlightTime(launchPos, travelTarget);
                Vector3 balloonScale = BalloonController.Instance.GetBalloonWorldScale(candidate.targetId);
                Vector3 launchStartScale = dartObj.transform.localScale;

                var proj = GetProjectile();
                proj.gameObject = dartObj;
                proj.startPosition = launchPos;
                proj.targetPosition = travelTarget;
                proj.targetBalloonId = candidate.targetId;
                proj.color = candidate.color;
                proj.scanDir = candidate.scanDir;
                proj.scanLine = candidate.scanLine;
                // [ROLLBACK_DART_LAUNCH_INITIAL_PROGRESS]
                // 기존: proj.elapsed = 0f;
                float initialProgress2 = GameManager.HasInstance ? GameManager.Instance.Board.dartLaunchInitialProgress : 0f;
                proj.elapsed = Mathf.Clamp(initialProgress2, 0f, Mathf.Max(0f, ft - 0.01f));
                proj.duration = ft;
                proj.impactTime = ft;
                ConfigureLaunchScale(proj, launchStartScale, balloonScale);
                ConfigureNeedleTipImpactTiming(proj, dartObj, launchPos, travelTarget, balloonScale);
                _activeProjectiles.Add(proj);

                // [ROLLBACK_DART_FX_TRAIL] 발사 시에도 FXDartTrail 비활성 유지 (요청: 도중 활성화 금지, 외곽 fire path).
                SetDartTrailActive(dartObj, false);

                // ROLLBACK_DART_PROJECTILE_MANUAL_MOVE:
                // Previous behavior created a DOTween per fired dart:
                // dartObj.transform.DOMove(travelTarget, ft).SetEase(Ease.Linear);
                // Movement is now advanced in UpdateProjectiles so firing does not allocate a
                // tween and DOTween.Update does not scale with projectile count. Hit timing and
                // target reservation stay unchanged.
            }
            else
            {
                // ROLLBACK_DART_STABLE_OUTER_HIT:
                // The old path had already removed the dart from the rail and reserved its target,
                // but if no projectile visual was available it never resolved the hit. Resolve the
                // original fire-time target immediately so this cannot become a silent miss.
                _reservedTargets.Remove(candidate.targetId);
                // ROLLBACK_DART_POP_SCAN_STATE_STABILITY:
                // No projectile visual means we resolve the original fire-time target immediately.
                // Clearing all DartManager scan state here reopens same-line continuous fire and wipes
                // promotion catch-up seeds for unrelated holders.
                int fallbackLineKey = GetConsumedLineKey(candidate.scanDir, candidate.scanLine);
                _unresolvedConsumedTargetLines.Remove(fallbackLineKey);
                // RESOLVED_LINE_DWELL_RELEASE: 즉시 resolve 경로도 dwell 기준 시각 기록.
                _resolvedConsumedLineAt[fallbackLineKey] = Time.unscaledTime;
                LogAttackIssue(
                    "DartProjectileFallback",
                    $"reason=noVisualImmediateHit holder={candidate.holderId} dartId={candidate.dartId} " +
                    $"target={candidate.targetId} scan={candidate.scanDir} line={candidate.scanLine}");
                ExecuteHit(candidate.targetId, candidate.color);
            }

            return true;
        }

        // [RAW_GRID_SPACE 2026-06-12] 스캔라인 키는 원시 보드 공간 기준 — DirectionalTargeting.WorldToGrid 와
        // 동일 정규화(역변환 후 원시 spacing 나눔). 스케일 보드에서 라인 키 단위(0.55 전제 튜닝)는 유지하면서
        // 시각적 한 줄 = 한 라인 정합 복원. 라인 락(_consumedTargetLines)/스캔 게이트가 모두 이 키를 공유.
        private static Vector3 ToRawBoardSpace(Vector3 worldPos)
        {
            return BalloonController.HasInstance
                ? BalloonController.Instance.WorldToRawBoardPosition(worldPos)
                : worldPos;
        }

        private static void GetLatticePhase(out float phaseX, out float phaseZ)
        {
            phaseX = 0f;
            phaseZ = 0f;
            if (BalloonController.HasInstance)
                BalloonController.Instance.GetRawLatticePhase(out phaseX, out phaseZ);
        }

        private static int GetScanLine(Vector3 pos, DirectionalTargeting.ScanDirection scanDir)
        {
            float cs = GameManager.HasInstance ? GameManager.Instance.Board.cellSpacing : 0.55f;
            if (cs <= 0.01f) cs = 0.55f;

            // [LATTICE_PHASE 2026-06-12] 위상 기준 상대 라운딩 — DirectionalTargeting.WorldToGrid 와 동일 키 공간.
            Vector3 raw = ToRawBoardSpace(pos);
            GetLatticePhase(out float phaseX, out float phaseZ);
            switch (scanDir)
            {
                case DirectionalTargeting.ScanDirection.Right:
                case DirectionalTargeting.ScanDirection.Left:
                    return Mathf.RoundToInt((raw.z - phaseZ) / cs);
                case DirectionalTargeting.ScanDirection.Up:
                case DirectionalTargeting.ScanDirection.Down:
                    return Mathf.RoundToInt((raw.x - phaseX) / cs);
                default:
                    return 0;
            }
        }

        private static Vector3 MakeScanPositionForLine(Vector3 currentPos, DirectionalTargeting.ScanDirection scanDir, int line)
        {
            float cs = GameManager.HasInstance ? GameManager.Instance.Board.cellSpacing : 0.55f;
            if (cs <= 0.01f) cs = 0.55f;

            // 라인 → 원시 공간 좌표(위상 + line×cs) → 월드로 정변환 (결과는 월드 스캔 위치).
            Vector3 raw = ToRawBoardSpace(currentPos);
            GetLatticePhase(out float phaseX, out float phaseZ);
            switch (scanDir)
            {
                case DirectionalTargeting.ScanDirection.Right:
                case DirectionalTargeting.ScanDirection.Left:
                    raw.z = phaseZ + line * cs;
                    break;
                case DirectionalTargeting.ScanDirection.Up:
                case DirectionalTargeting.ScanDirection.Down:
                    raw.x = phaseX + line * cs;
                    break;
                default:
                    return currentPos;
            }

            Vector3 world = BalloonController.HasInstance
                ? BalloonController.Instance.GetAdjustedBoardPosition(raw)
                : raw;
            world.y = currentPos.y;   // GetAdjustedBoardPosition 이 y 를 스폰 고도로 덮으므로 복원.
            return world;
        }

        private static Vector3 GetFireDirectionForScanDirection(DirectionalTargeting.ScanDirection scanDir)
        {
            switch (scanDir)
            {
                case DirectionalTargeting.ScanDirection.Right:
                    return Vector3.right;
                case DirectionalTargeting.ScanDirection.Left:
                    return Vector3.left;
                case DirectionalTargeting.ScanDirection.Up:
                    return Vector3.forward;
                case DirectionalTargeting.ScanDirection.Down:
                    return Vector3.back;
                default:
                    return Vector3.forward;
            }
        }

        private bool TryFindAdjacentEmptyLineRescue(
            int holderId,
            int dartId,
            int color,
            float progress,
            Vector3 probePos,
            Vector3 fireDir,
            DirectionalTargeting.ScanDirection probeScanDir,
            int probeLine,
            string stage,
            out int targetId,
            out DirectionalTargeting.ScanDirection scanDir,
            out int targetLine,
            out Vector3 selectedTargetPos)
        {
            targetId = -1;
            scanDir = probeScanDir;
            targetLine = probeLine;
            selectedTargetPos = Vector3.zero;

            if (!DirectionalTargeting.TryFindTargetOnAdjacentLineWhenExactLineEmpty(
                    probePos,
                    fireDir,
                    color,
                    _reservedTargets,
                    ADJACENT_EMPTY_LINE_RESCUE_RADIUS,
                    out targetId,
                    out scanDir,
                    out targetLine,
                    out selectedTargetPos))
            {
                return false;
            }

            int foundLine = targetLine;
            if (IsTargetLineConsumed(scanDir, foundLine))
            {
                LogAttackIssue(
                    "DartFireBlocked",
                    $"reason=targetLineConsumed stage={stage}AdjacentRescue holder={holderId} dartId={dartId} " +
                    $"progress={progress:F2} scan={scanDir} line={foundLine} " +
                    $"probeScan={probeScanDir} probeLine={probeLine}");
                targetId = -1;
                return false;
            }
            if (IsHolderLineConsumed(holderId, scanDir, foundLine))
            {
                LogAttackIssue(
                    "DartFireBlocked",
                    $"reason=holderLineConsumed stage={stage}AdjacentRescue holder={holderId} dartId={dartId} " +
                    $"progress={progress:F2} scan={scanDir} line={foundLine} " +
                    $"probeScan={probeScanDir} probeLine={probeLine}");
                targetId = -1;
                return false;
            }

            LogAttackIssue(
                "DartAdjacentLineRescue",
                $"stage={stage} holder={holderId} dartId={dartId} color={color} progress={progress:F2} " +
                $"probeScan={probeScanDir} probeLine={probeLine} target={targetId} scan={scanDir} line={foundLine}");
            return true;
        }

        private void ResetStraightRailPassIfWrapped(
            int holderId,
            DirectionalTargeting.ScanDirection scanDir,
            int currentLine,
            float headProgress)
        {
            if (!RailManager.HasInstance) return;
            RailManager rail = RailManager.Instance;
            // [WRAP_PASS_RESET 2026-06-12] 1면 한정 → 개방 레일 전체(1~3면)로 확대 + 방향/passDir 게이트 제거.
            //   증거(holderLineConsumed 영구 차단 로그): 2~3면 레일에서 holder 가 wrap 경계 부근
            //   라인(좌측 끝 0/1)에서만 발사하면 — ①line-jump 감지는 |현재-마지막발사| < DELTA(6) 라 실패,
            //   ②방향전환 감지는 스캔 캐시가 발사 시에만 갱신돼 항상 같은 방향이라 실패,
            //   ③stuck 릴리프는 벨트 이동으로 같은 라인 0.4s 체류가 안 돼 실패 → pass lock 영구 잔존
            //   → 매칭 풍선이 그 라인에만 있으면 영구 정지(매치 존재로 fail 도 안 뜸).
            //   progress 급감(> pathLen*0.5)은 기하학적 wrap 그 자체 — 면 수/스캔 방향 무관하게 신뢰 가능,
            //   head 교체로 인한 감소는 한 다트 간격 수준이라 임계에 안 걸림. 폐루프(4면)는 wrap 이 없고
            //   코너 방향 전환(EnsureHolderPassDirection)이 pass 를 리셋하므로 제외.
            if (RailManager.GetRailSideCount(rail.PhysicalCapacity) >= 4) return;

            // head progress 변화량으로 wrap 판정 (board 너비 무관). 매 tick baseline 갱신.
            float pathLen = rail.TotalPathLength;
            bool hasLastProg = _lastStraightHeadProgressByHolder.TryGetValue(holderId, out float lastProg);
            _lastStraightHeadProgressByHolder[holderId] = headProgress;

            bool wrapped = false;

            // 1) progress 기반: 개방 레일은 끝→시작 순간이동 시 progress 가 pathLen 만큼 급감.
            if (pathLen > 0f && hasLastProg && headProgress < lastProg - pathLen * 0.5f)
                wrapped = true;

            // 2) 보조: 같은 pass 방향에서 큰 backward line jump (기존 동작 유지 — 방향 일치 시에만 의미).
            if (!wrapped
                && _holderPassDirectionByHolder.TryGetValue(holderId, out DirectionalTargeting.ScanDirection passDir)
                && passDir == scanDir
                && _lastFiredLineByHolder.TryGetValue(holderId, out int lastFiredLine)
                && currentLine < lastFiredLine - OPEN_RAIL_WRAP_RESET_LINE_DELTA)
                wrapped = true;

            if (wrapped)
            {
                // 양쪽 게이트 모두 해제 — holder-local pass lock + global target-line lock
                // (ResetOpenRailPassIfWrapped 와 동일). 둘 중 하나라도 남으면 발사가 다시 막힘.
                ClearConsumedLineLockForHolder(holderId);
                ClearResolvedConsumedTargetLinesForDirection(scanDir);
                LogAttackIssue(
                    "DartWrapPassReset",
                    $"holder={holderId} scan={scanDir} line={currentLine} progress={headProgress:F2} pathLen={pathLen:F2}");
            }
        }

        private void ResetOpenRailPassIfWrapped(
            int holderId,
            DirectionalTargeting.ScanDirection currentScanDir,
            int currentLine,
            bool hasLastScan,
            DirectionalTargeting.ScanDirection lastDir,
            int lastLine,
            bool hasPromoSeedForHead,
            DirectionalTargeting.ScanDirection promoDir)
        {
            if (!RailManager.HasInstance) return;
            int sideCount = RailManager.GetRailSideCount(RailManager.Instance.PhysicalCapacity);
            if (sideCount >= 4) return;
            if (currentScanDir != DirectionalTargeting.ScanDirection.Up) return;

            bool wrappedFromEndSide = hasLastScan && lastDir != currentScanDir;
            bool wrappedOnStraightSide = hasLastScan
                && lastDir == currentScanDir
                && currentLine < lastLine - OPEN_RAIL_WRAP_RESET_LINE_DELTA;
            bool promotedAcrossWrap = hasPromoSeedForHead && promoDir != currentScanDir;

            if (!wrappedFromEndSide && !wrappedOnStraightSide && !promotedAcrossWrap)
                return;

            ClearConsumedLineLockForHolder(holderId);
            ClearResolvedConsumedTargetLinesForDirection(currentScanDir);

            LogAttackIssue(
                "DartOpenRailWrapReset",
                $"holder={holderId} sides={sideCount} scan={currentScanDir} currentLine={currentLine} " +
                $"lastScan={hasLastScan}/{lastDir}/{lastLine} promo={hasPromoSeedForHead}/{promoDir}");
        }

        private void InvalidateDartScanLines()
        {
            _lastScannedLineByHolder.Clear();
            _lastScanDirectionByHolder.Clear();
            _lastScannedHeadIdByHolder.Clear();
            // ROLLBACK_PROMOTION_SEED_CATCH_UP:
            _promoLineByHolder.Clear();
            _promoDirByHolder.Clear();
            _promoHeadByHolder.Clear();
            // DEAD_HEAD_RELIEF: 레벨 전환/전체 리셋 시 holder 타이머 잔존 방지.
            _deadHeadSince.Clear();
            _deadHeadLastReliefFireAt.Clear();
            // DART_STALL_WATCHDOG: 전체 무효화 = 새 스캔 패스 시작 — 정지 감시 타이머도 리셋
            // (레벨 전환/홀더 배치/워치독 자신 어느 경로든 이월 누적 방지).
            _stallWatchTimer = 0f;
        }

        private void InvalidateDartScanLineForHolder(int holderId)
        {
            _lastScannedLineByHolder.Remove(holderId);
            _lastScanDirectionByHolder.Remove(holderId);
            _lastScannedHeadIdByHolder.Remove(holderId);
            // ROLLBACK_PROMOTION_SEED_CATCH_UP:
            _promoLineByHolder.Remove(holderId);
            _promoDirByHolder.Remove(holderId);
            _promoHeadByHolder.Remove(holderId);
        }

        // ROLLBACK_DART_POP_LINE_RESCAN:
        // A pop changes the outer contour on exactly one grid row and one grid column. Clearing all
        // DartManager scan state after every pop reopens same-holder peeling and wipes promotion
        // catch-up, but keeping every line cached makes heads on that line miss the refreshed outer
        // cell. Reopen only holders whose last accepted scan line matches the popped cell's row/column.
        private void InvalidateDartScanLinesForPoppedPosition(Vector3 position)
        {
            int horizontalLine = GetScanLine(position, DirectionalTargeting.ScanDirection.Left);
            int verticalLine = GetScanLine(position, DirectionalTargeting.ScanDirection.Up);

            // ROLLBACK_DART_POP_RELEASE_RESOLVED_TARGET_LINE:
            // The global target-line lock is only needed while the fired projectile has not resolved.
            // Once a real pop happened, keeping that line locked until every head leaves it can make
            // x2 straight rails skip the refreshed outer cell. Holder-local pass locks below still
            // prevent the same holder from peeling the same line repeatedly in one pass.
            ClearResolvedConsumedTargetLine(DirectionalTargeting.ScanDirection.Left, horizontalLine);
            ClearResolvedConsumedTargetLine(DirectionalTargeting.ScanDirection.Right, horizontalLine);
            ClearResolvedConsumedTargetLine(DirectionalTargeting.ScanDirection.Up, verticalLine);
            ClearResolvedConsumedTargetLine(DirectionalTargeting.ScanDirection.Down, verticalLine);

            _tempRemoveKeys.Clear();
            foreach (var kvp in _lastScannedLineByHolder)
            {
                int holderId = kvp.Key;
                int line = kvp.Value;
                if (!_lastScanDirectionByHolder.TryGetValue(holderId, out DirectionalTargeting.ScanDirection dir))
                    continue;

                bool sameLine =
                    ((dir == DirectionalTargeting.ScanDirection.Left || dir == DirectionalTargeting.ScanDirection.Right) && line == horizontalLine)
                    || ((dir == DirectionalTargeting.ScanDirection.Up || dir == DirectionalTargeting.ScanDirection.Down) && line == verticalLine);
                if (sameLine)
                    _tempRemoveKeys.Add(holderId);
            }

            for (int i = 0; i < _tempRemoveKeys.Count; i++)
                InvalidateDartScanLineForHolder(_tempRemoveKeys[i]);

            _tempRemoveKeys.Clear();
        }

        // ROLLBACK_PROMOTION_SEED_CATCH_UP:
        private void ClearPromoSeedForHolder(int holderId)
        {
            _promoLineByHolder.Remove(holderId);
            _promoDirByHolder.Remove(holderId);
            _promoHeadByHolder.Remove(holderId);
        }

        // ROLLBACK_DART_DEPLOY_HEAD_SEED:
        // At x2 speed a freshly deployed first dart can move from its placement line to a later
        // line before DartManager's next Update scan. With no previous scan/head-replacement seed,
        // the first scan only probes the current line and silently skips the crossed lines. Treat a
        // newly placed holder head like a promoted head so the first scan replays placement line
        // through current line.
        private void SeedPlacedHeadCatchUp(OnDartPlaced evt)
        {
            if (!RailManager.HasInstance || evt.dartId < 0 || evt.color < 0)
                return;

            var rail = RailManager.Instance;
            var head = rail.GetClusterHeadDart(evt.holderId);
            if (head == null || head.dartId != evt.dartId || head.dartColor < 0)
                return;

            rail.GetDartCurrentPose(head, out Vector3 seedPos, out _, out Vector3 seedFireDir);
            var seedScanDir = DirectionalTargeting.DetermineScanDirection(seedFireDir);
            int seedLine = GetScanLine(seedPos, seedScanDir);

            _promoHeadByHolder[evt.holderId] = evt.dartId;
            _promoDirByHolder[evt.holderId] = seedScanDir;
            _promoLineByHolder[evt.holderId] = seedLine;

            LogAttackIssue(
                "DartDeployHeadSeed",
                $"holder={evt.holderId} dartId={evt.dartId} color={evt.color} " +
                $"progress={evt.progress:F2} scan={seedScanDir} line={seedLine}");
        }

        // ROLLBACK_DART_CONSUMED_LINE_LOCK:
        private bool IsHolderLineConsumed(int holderId, DirectionalTargeting.ScanDirection scanDir, int line)
        {
            // ROLLBACK_DART_HOLDER_PASS_LINE_SET:
            // This must be read-only. At x2 speed a head can briefly report a corner/tunnel tangent
            // direction before a real fire happens; clearing the pass set during a mere lookup
            // reopens earlier lines and recreates same-side continuous fire.
            if (!_holderPassDirectionByHolder.TryGetValue(holderId, out DirectionalTargeting.ScanDirection passDir)
                || passDir != scanDir)
            {
                return false;
            }

            return _holderPassLinesByHolder.TryGetValue(holderId, out HashSet<int> firedLines)
                && firedLines.Contains(line);
        }

        // ROLLBACK_DART_CONSUMED_LINE_LOCK:
        private bool HasHolderLeftConsumedLine(int holderId, DirectionalTargeting.ScanDirection scanDir, int line)
        {
            // ROLLBACK_DART_HOLDER_PASS_LINE_SET:
            // A line stays consumed for the whole current side pass, not only while the head remains
            // on that exact line.
            return false;
        }

        // ROLLBACK_DART_CONSUMED_LINE_LOCK:
        private void ClearConsumedLineLockForHolder(int holderId)
        {
            _lastFiredLineByHolder.Remove(holderId);
            _lastFiredDirectionByHolder.Remove(holderId);
            _holderPassLinesByHolder.Remove(holderId);
            _holderPassDirectionByHolder.Remove(holderId);
        }

        // ROLLBACK_DART_CONSUMED_LINE_LOCK:
        private void ClearConsumedLineLocks()
        {
            _lastFiredLineByHolder.Clear();
            _lastFiredDirectionByHolder.Clear();
            _holderPassLinesByHolder.Clear();
            _holderPassDirectionByHolder.Clear();
            _lastStraightHeadProgressByHolder.Clear();
            _holderLineStuckSince.Clear();
            _holderLineStuckDirection.Clear();
            _holderLineStuckLine.Clear();
            _consumedTargetLines.Clear();
            _unresolvedConsumedTargetLines.Clear();
            _currentHeadLineKeys.Clear();
            _resolvedConsumedLineAt.Clear();
        }

        // ROLLBACK_DART_HOLDER_PASS_LINE_SET:
        private void EnsureHolderPassDirection(int holderId, DirectionalTargeting.ScanDirection scanDir)
        {
            if (_holderPassDirectionByHolder.TryGetValue(holderId, out DirectionalTargeting.ScanDirection passDir)
                && passDir == scanDir)
            {
                return;
            }

            _holderPassDirectionByHolder[holderId] = scanDir;
            if (_holderPassLinesByHolder.TryGetValue(holderId, out HashSet<int> firedLines))
                firedLines.Clear();
        }

        // ROLLBACK_DART_HOLDER_PASS_LINE_SET:
        private void MarkHolderLineConsumed(int holderId, DirectionalTargeting.ScanDirection scanDir, int line)
        {
            EnsureHolderPassDirection(holderId, scanDir);

            if (!_holderPassLinesByHolder.TryGetValue(holderId, out HashSet<int> firedLines))
            {
                firedLines = new HashSet<int>();
                _holderPassLinesByHolder[holderId] = firedLines;
            }

            firedLines.Add(line);
            _lastFiredLineByHolder[holderId] = line;
            _lastFiredDirectionByHolder[holderId] = scanDir;
        }

        // ROLLBACK_DART_OUTER_PASS_LINE_LOCK:
        private bool IsTargetLineConsumed(DirectionalTargeting.ScanDirection scanDir, int line)
        {
            return _consumedTargetLines.Contains(GetConsumedLineKey(scanDir, line));
        }

        // ROLLBACK_DART_OUTER_PASS_LINE_LOCK:
        private void MarkTargetLineConsumed(int holderId, DirectionalTargeting.ScanDirection scanDir, int line)
        {
            int key = GetConsumedLineKey(scanDir, line);
            _consumedTargetLines.Add(key);
            _unresolvedConsumedTargetLines.Add(key);
        }

        // RESOLVED_LINE_DWELL_RELEASE (2026-06-11):
        // 글로벌 라인 락은 'resolve 후 head 가 그 라인을 떠나야' 해제되는 설계(즉시 peel 방지)였는데,
        // 패킹 정지 등으로 head 가 라인을 영영 못 떠나면 락이 영구 잔존 — 그 라인의 새 외곽 풍선은
        // 영구 놓침이 되고, 다른 곳 발사가 이어지면 전역 워치독도 안 걸린다 (부분 정지).
        // resolve 시각을 기록해 두고, head 가 주차 중이어도 dwell(0.4s — 기존 stuck-relief 와 동일
        // 한도) 경과 시 락을 해제 + 그 라인에 주차한 holder 의 스캔 라인을 무효화해 현재 라인
        // 재스캔을 허용한다. resolve 직후 같은 라인 즉시 재발사(연속공격)는 dwell 이 그대로 차단.
        private readonly Dictionary<int, float> _resolvedConsumedLineAt = new Dictionary<int, float>(16);
        private readonly HashSet<int> _dwellReleasedLineKeys = new HashSet<int>();
        private const float RESOLVED_LINE_HEAD_DWELL_RELEASE_SECONDS = 0.4f;

        // ROLLBACK_DART_OUTER_PASS_LINE_LOCK:
        private void PruneConsumedTargetLinesForCurrentHeads(RailManager rail)
        {
            if (_consumedTargetLines.Count == 0 || rail == null)
                return;

            _currentHeadLineKeys.Clear();
            rail.GetClusterHeadDarts(_scanHeadDarts);
            for (int i = 0; i < _scanHeadDarts.Count; i++)
            {
                var head = _scanHeadDarts[i];
                if (head == null || head.dartColor < 0)
                    continue;

                rail.GetDartCurrentPose(head, out Vector3 pos, out _, out Vector3 fireDir);
                var scanDir = DirectionalTargeting.DetermineScanDirection(fireDir);
                _currentHeadLineKeys.Add(GetConsumedLineKey(scanDir, GetScanLine(pos, scanDir)));
            }

            _tempRemoveKeys.Clear();
            _dwellReleasedLineKeys.Clear();
            float now = Time.unscaledTime;
            foreach (int consumedKey in _consumedTargetLines)
            {
                if (_unresolvedConsumedTargetLines.Contains(consumedKey))
                    continue;

                if (!_currentHeadLineKeys.Contains(consumedKey))
                {
                    _tempRemoveKeys.Add(consumedKey);
                    continue;
                }

                // RESOLVED_LINE_DWELL_RELEASE: head 주차 중이어도 resolve 후 dwell 경과 시 해제.
                if (_resolvedConsumedLineAt.TryGetValue(consumedKey, out float resolvedAt)
                    && now - resolvedAt >= RESOLVED_LINE_HEAD_DWELL_RELEASE_SECONDS)
                {
                    _tempRemoveKeys.Add(consumedKey);
                    _dwellReleasedLineKeys.Add(consumedKey);
                }
            }

            for (int i = 0; i < _tempRemoveKeys.Count; i++)
            {
                _consumedTargetLines.Remove(_tempRemoveKeys[i]);
                _unresolvedConsumedTargetLines.Remove(_tempRemoveKeys[i]);
                _resolvedConsumedLineAt.Remove(_tempRemoveKeys[i]);
            }

            // dwell 해제된 라인에 주차 중인 holder 는 스캔 라인 무효화 — 라인 횡단 없이도 재스캔.
            if (_dwellReleasedLineKeys.Count > 0)
            {
                for (int i = 0; i < _scanHeadDarts.Count; i++)
                {
                    var head = _scanHeadDarts[i];
                    if (head == null || head.dartColor < 0) continue;
                    rail.GetDartCurrentPose(head, out Vector3 pos, out _, out Vector3 fireDir);
                    var scanDir = DirectionalTargeting.DetermineScanDirection(fireDir);
                    if (_dwellReleasedLineKeys.Contains(GetConsumedLineKey(scanDir, GetScanLine(pos, scanDir))))
                    {
                        InvalidateDartScanLineForHolder(head.holderId);
                        LogAttackIssue("DartDwellLineRelease",
                            $"holder={head.holderId} dart={head.dartId} color={head.dartColor} scan={scanDir} line={GetScanLine(pos, scanDir)}");
                    }
                }
                _dwellReleasedLineKeys.Clear();
            }

            _tempRemoveKeys.Clear();
            _currentHeadLineKeys.Clear();
        }

        private void ClearResolvedConsumedTargetLinesForDirection(DirectionalTargeting.ScanDirection scanDir)
        {
            if (_consumedTargetLines.Count == 0)
                return;

            _tempRemoveKeys.Clear();
            foreach (int consumedKey in _consumedTargetLines)
            {
                if (_unresolvedConsumedTargetLines.Contains(consumedKey))
                    continue;

                if (IsConsumedLineKeyForDirection(consumedKey, scanDir))
                    _tempRemoveKeys.Add(consumedKey);
            }

            for (int i = 0; i < _tempRemoveKeys.Count; i++)
            {
                _consumedTargetLines.Remove(_tempRemoveKeys[i]);
                _unresolvedConsumedTargetLines.Remove(_tempRemoveKeys[i]);
                _resolvedConsumedLineAt.Remove(_tempRemoveKeys[i]);
            }

            _tempRemoveKeys.Clear();
        }

        private void ClearResolvedConsumedTargetLine(DirectionalTargeting.ScanDirection scanDir, int line)
        {
            int key = GetConsumedLineKey(scanDir, line);
            if (_unresolvedConsumedTargetLines.Contains(key))
                return;

            _consumedTargetLines.Remove(key);
            _resolvedConsumedLineAt.Remove(key);
        }

        // ROLLBACK_DART_GLOBAL_CONSUMED_LINE_LOCK:
        private const int CONSUMED_LINE_KEY_STRIDE = 1000000;
        private const int CONSUMED_LINE_KEY_OFFSET = 500000;

        private static int GetConsumedLineKey(DirectionalTargeting.ScanDirection scanDir, int line)
        {
            return ((int)scanDir * CONSUMED_LINE_KEY_STRIDE) + (line + CONSUMED_LINE_KEY_OFFSET);
        }

        private static bool IsConsumedLineKeyForDirection(int key, DirectionalTargeting.ScanDirection scanDir)
        {
            int start = (int)scanDir * CONSUMED_LINE_KEY_STRIDE;
            return key >= start && key < start + CONSUMED_LINE_KEY_STRIDE;
        }

        private int GetFireCandidateStartIndex()
        {
            if (_fireCandidates.Count == 0)
                return 0;

            // ROLLBACK_DART_CLUSTER_FAIR_FIRE:
            // Rotate the first holder processed among currently fireable holder heads. This prevents
            // a cluster in a corner/tunnel from repeatedly winning first-candidate order while
            // another cluster's line advances past its target.
            int start = 0;
            if (_lastFiredHolderId >= 0)
            {
                for (int i = 0; i < _fireCandidates.Count; i++)
                {
                    if (_fireCandidates[i].holderId == _lastFiredHolderId)
                    {
                        start = (i + 1) % _fireCandidates.Count;
                        break;
                    }
                }
            }

            return start;
        }

        // ROLLBACK_DART_ATTACK_ISSUE_DEBUG:
        // [2026-06-01 TEMP] attack-miss 데드락 진단을 위해 [Conditional] 임시 해제 — define 없이도
        //   런타임 플래그(DART_ATTACK_ISSUE_DEBUG)+throttle 로 [DartFireBlocked] reason 로그가 찍힌다.
        //   진단/캡쳐 후 아래 한 줄 주석을 해제해 다시 컴파일-제거(hot path 문자열 비용 0)로 되돌릴 것.
        //   (call site 문자열 인자 비용이 생기므로 진단 빌드 전용.)
        // [System.Diagnostics.Conditional("BALLOONFLOW_DART_ATTACK_ISSUE_DEBUG")]
        // [2026-06-10 perf] Conditional 전환 — 심볼 미정의 시 호출부(문자열 보간 인자 포함)가 통째로 컴파일 제거됨.
        //   기존엔 플래그 false 여도 36개 호출부의 $"..." 보간이 매번 평가·할당돼 GC 압박을 만들었음.
        //   진단 필요 시 Scripting Define Symbols 에 BALLOONFLOW_DART_ATTACK_ISSUE_DEBUG 추가.
        [System.Diagnostics.Conditional("BALLOONFLOW_DART_ATTACK_ISSUE_DEBUG")]
        private void LogAttackIssue(string tag, string message)
        {
            if (!DART_ATTACK_ISSUE_DEBUG) return;

            int frame = Time.frameCount;
            if (_lastAttackIssueLogFrame != frame)
            {
                _lastAttackIssueLogFrame = frame;
                _attackIssueLogsThisFrame = 0;
            }
            if (_attackIssueLogsThisFrame >= MAX_ATTACK_ISSUE_LOGS_PER_FRAME) return;

            _attackIssueLogsThisFrame++;
            Debug.Log($"[{tag}] frame={frame} {message}");
        }

        [System.Diagnostics.Conditional("BALLOONFLOW_DART_MISS_SUSPECT_DEBUG")]
        private void LogMissSuspectIfNeeded(
            int holderId,
            int dartId,
            int color,
            float progress,
            Vector3 dartPos,
            Vector3 fireDir,
            DirectionalTargeting.ScanDirection scanDir,
            int scanLine)
        {
            if (!DART_MISS_SUSPECT_DEBUG) return;

            int frame = Time.frameCount;
            if (_lastMissSuspectLogFrame != frame)
            {
                _lastMissSuspectLogFrame = frame;
                _missSuspectLogsThisFrame = 0;
            }
            if (_missSuspectLogsThisFrame >= MAX_MISS_SUSPECT_LOGS_PER_FRAME) return;

            if (!DirectionalTargeting.TryBuildMissSuspectDiag(
                    dartPos,
                    fireDir,
                    color,
                    _reservedTargets,
                    2,
                    out string diag))
            {
                _missSuspectLogsThisFrame++;
                Debug.Log($"[DartMissSuspect] holder={holderId} dartId={dartId} color={color} " +
                          $"progress={progress:F2} scan={scanDir} scanLine={scanLine} mode=miss noDiag");
                return;
            }

            _missSuspectLogsThisFrame++;
            Debug.Log($"[DartMissSuspect] holder={holderId} dartId={dartId} color={color} " +
                      $"progress={progress:F2} scan={scanDir} scanLine={scanLine} {diag}");

            // [2026-05-22 DBG-TopLeft] BOTTOM/RIGHT rail 다트 미발사 시 lock 상태와 contour 좌상단 상태 dump.
            // 캡쳐 끝나면 본 블록 + DBG_TOPLEFT_DUMP 상수 제거.
            if (DBG_TOPLEFT_DUMP &&
                (scanDir == DirectionalTargeting.ScanDirection.Up ||
                 scanDir == DirectionalTargeting.ScanDirection.Left))
            {
                System.Text.StringBuilder sb = new System.Text.StringBuilder(256);
                sb.Append("[DBG-TopLeft] holder=").Append(holderId)
                  .Append(" dartId=").Append(dartId)
                  .Append(" color=").Append(color)
                  .Append(" scan=").Append(scanDir)
                  .Append(" scanLine=").Append(scanLine);

                // 이 holder 의 pass 방향/소비 라인 set
                if (_holderPassDirectionByHolder.TryGetValue(holderId, out DirectionalTargeting.ScanDirection passDir))
                    sb.Append(" passDir=").Append(passDir);
                else sb.Append(" passDir=none");
                if (_holderPassLinesByHolder.TryGetValue(holderId, out HashSet<int> passLines) && passLines != null)
                {
                    sb.Append(" passLines={");
                    bool first = true;
                    foreach (int l in passLines)
                    {
                        if (!first) sb.Append(',');
                        sb.Append(l);
                        first = false;
                    }
                    sb.Append('}');
                }
                else sb.Append(" passLines={}");

                // 글로벌 in-flight consumed line 집합 (현재 scanDir 만)
                sb.Append(" globalConsumed[").Append(scanDir).Append("]={");
                bool firstG = true;
                foreach (int key in _consumedTargetLines)
                {
                    if (!IsConsumedLineKeyForDirection(key, scanDir)) continue;
                    if (!firstG) sb.Append(',');
                    int line = (key % CONSUMED_LINE_KEY_STRIDE) - CONSUMED_LINE_KEY_OFFSET;
                    sb.Append(line);
                    bool unresolved = _unresolvedConsumedTargetLines.Contains(key);
                    if (unresolved) sb.Append('!');
                    firstG = false;
                }
                sb.Append('}');

                // 좌상단 contour 후보: scanLine 자신 (=col 0 또는 row max 가 와야 정상)
                if (DirectionalTargeting.TryGetContourEdgeForDirection(scanDir, scanLine,
                        out int contourBalloonId, out int contourCellX, out int contourCellY,
                        out int contourColor, out bool contourTargetable))
                {
                    sb.Append(" contourEdge=id").Append(contourBalloonId)
                      .Append("/cell(").Append(contourCellX).Append(',').Append(contourCellY).Append(')')
                      .Append("/color=").Append(contourColor)
                      .Append("/targetable=").Append(contourTargetable);
                }
                else
                {
                    sb.Append(" contourEdge=none(forLine=").Append(scanLine).Append(')');
                }

                Debug.Log(sb.ToString());
            }
        }

        #endregion

        #region Private Methods — Projectile Update

        private DartProjectile GetProjectile()
        {
            DartProjectile proj = _projectilePool.Count > 0 ? _projectilePool.Pop() : new DartProjectile();
            ResetProjectile(proj);
            return proj;
        }

        private void ReleaseProjectile(DartProjectile proj)
        {
            if (proj == null) return;
            ResetProjectile(proj);
            _projectilePool.Push(proj);
        }

        private static void ResetProjectile(DartProjectile proj)
        {
            proj.gameObject = null;
            proj.startPosition = Vector3.zero;
            proj.targetPosition = Vector3.zero;
            proj.targetBalloonId = -1;
            proj.color = -1;
            proj.scanDir = default;
            proj.scanLine = 0;
            proj.elapsed = 0f;
            proj.duration = 0f;
            proj.impactTime = 0f;
            proj.impactResolved = false;
            proj.startScale = Vector3.one;
            proj.targetScale = Vector3.one;
            proj.launchPunchT = 0f;
            proj.punchPeakScale = Vector3.one;
            proj.punchDuration = 0f;
            proj.lerpStrength = 0f;
        }

        private void UpdateProjectiles()
        {
            float __moveScaleMs = 0f;
            float __resolvePrepMs = 0f;
            float __executeHitMs = 0f;
            float __returnPoolMs = 0f;

            try
            {
            for (int i = _activeProjectiles.Count - 1; i >= 0; i--)
            {
                // Guard: ClearAllDarts may have emptied the list via ExecuteHit → OnBoardCleared chain
                if (i >= _activeProjectiles.Count) continue;

                DartProjectile proj = _activeProjectiles[i];
                proj.elapsed += Time.deltaTime;
                // ROLLBACK_DART_DEADLOCK_PROJECTILE_GUARD_20260616:
                // Invalid projectile timing can keep _activeProjectiles > 0 forever. That blocks the
                // stall watchdog, leaving line locks/reservations stale and causing apparent deadlock.
                // Rollback: remove this guard if projectile timing is guaranteed by authored data.
                if (!IsFinite(proj.elapsed)) proj.elapsed = 0f;
                if (!IsFinite(proj.duration) || proj.duration <= 0f)
                {
                    LogAttackIssue(
                        "DartProjectileTimingGuard",
                        $"target={proj.targetBalloonId} color={proj.color} duration={proj.duration} impact={proj.impactTime}");
                    proj.duration = PROJECTILE_MIN_FLIGHT_TIME;
                    proj.impactTime = 0f;
                }
                else if (!IsFinite(proj.impactTime))
                {
                    proj.impactTime = proj.duration;
                }

                if (proj.gameObject != null && proj.duration > 0f)
                {
                    float __moveStamp = InGamePerfLogger.StartStampMs();
                    // ROLLBACK_DART_PROJECTILE_MANUAL_MOVE:
                    // This replaces per-shot Transform.DOMove allocation with deterministic
                    // per-frame interpolation. Gameplay still resolves using impactTime below,
                    // so miss/continuous-fire guards are not changed.
                    // [ROLLBACK_DART_MANUAL_LERP_EASE]
                    // 이전: linear lerp (Ease 무효).
                    //   float moveT = Mathf.Clamp01(proj.elapsed / proj.duration);
                    //   proj.gameObject.transform.position = Vector3.Lerp(proj.startPosition, proj.targetPosition, moveT);
                    // 변경: DOVirtual.EasedValue 로 GameManager.dartFlightEase 동적 반영. 롤백 시 위 두 줄 복원 + 아래 블록 주석.
                    // [이미지 속도 프로파일] 위치 보간을 등속이 아니라 가속→등속 누적이동량 비율로.
                    // ROLLBACK_DART_FLIGHT_VELOCITY_RAMP_20260608: 아래 ramp 블록 → moveT = Clamp01(proj.elapsed/proj.duration) 로 복원.
                    float startMultRT = GameManager.HasInstance ? GameManager.Instance.Board.dartFlightSpeedMultiplier : DEFAULT_DART_FLIGHT_SPEED_MULTIPLIER;
                    if (!IsFinite(startMultRT) || startMultRT <= 0.001f) startMultRT = DEFAULT_DART_FLIGHT_SPEED_MULTIPLIER;
                    float rampDenom = DartRampDistanceUnits(proj.duration, startMultRT);
                    float moveT = rampDenom > 0.0001f
                        ? Mathf.Clamp01(DartRampDistanceUnits(proj.elapsed, startMultRT) / rampDenom)
                        : Mathf.Clamp01(proj.elapsed / proj.duration);
                    Ease lerpEase = GameManager.HasInstance ? GameManager.Instance.Board.dartFlightEase : Ease.Linear;
                    float easedT = DOVirtual.EasedValue(0f, 1f, moveT, lerpEase);
                    proj.gameObject.transform.position = Vector3.Lerp(proj.startPosition, proj.targetPosition, easedT);

                    Vector3 scale;
                    if (proj.punchDuration > 0f && proj.elapsed < proj.punchDuration)
                    {
                        // 발사 직후 짧은 punch: startScale → peak (EaseOut) → startScale (EaseIn).
                        if (proj.elapsed < proj.launchPunchT)
                        {
                            float pt = proj.launchPunchT > 0f ? proj.elapsed / proj.launchPunchT : 1f;
                            float eased = 1f - (1f - pt) * (1f - pt); // EaseOutQuad
                            scale = Vector3.Lerp(proj.startScale, proj.punchPeakScale, eased);
                        }
                        else
                        {
                            float remain = proj.punchDuration - proj.launchPunchT;
                            float pt = remain > 0f ? (proj.elapsed - proj.launchPunchT) / remain : 1f;
                            float eased = pt * pt; // EaseInQuad
                            scale = Vector3.Lerp(proj.punchPeakScale, proj.startScale, eased);
                        }
                    }
                    else
                    {
                        // Punch 종료 후 lerpStrength로 풍선 사이즈에 도달하지 않게 제한 — 사용자 피드백 반영.
                        float t = Mathf.Clamp01(proj.elapsed / proj.duration);
                        scale = Vector3.Lerp(proj.startScale, proj.targetScale, Mathf.Clamp01(t * proj.lerpStrength));
                    }
                    proj.gameObject.transform.localScale = scale;
                    __moveScaleMs += InGamePerfLogger.ElapsedMs(__moveStamp);
                }

                // ROLLBACK_DART_NEEDLE_TIP_IMPACT:
                // Previous behavior resolved when the dart root reached targetPosition
                // (`proj.elapsed >= proj.duration`). This resolves earlier when the needle tip
                // reaches the balloon surface, without retargeting or changing scan logic.
                float resolveTime = Mathf.Clamp(proj.impactTime, 0f, proj.duration);
                if (!proj.impactResolved && proj.elapsed >= resolveTime)
                {
                    float __prepStamp = InGamePerfLogger.StartStampMs();
                    proj.impactResolved = true;
                    BalloonData impactData = BalloonController.HasInstance
                        ? BalloonController.Instance.GetBalloon(proj.targetBalloonId)
                        : null;
                    if (impactData == null || impactData.isPopped)
                    {
                        LogAttackIssue(
                            "DartProjectileResolve",
                            $"reason=targetGoneBeforeImpact target={proj.targetBalloonId} color={proj.color} " +
                            $"popped={(impactData != null && impactData.isPopped)} scan={proj.scanDir} line={proj.scanLine} " +
                            $"elapsed={proj.elapsed:F3}/{proj.duration:F3}");
                    }
                    _reservedTargets.Remove(proj.targetBalloonId);
                    int resolvedLineKey = GetConsumedLineKey(proj.scanDir, proj.scanLine);
                    _unresolvedConsumedTargetLines.Remove(resolvedLineKey);
                    // RESOLVED_LINE_DWELL_RELEASE: head 주차 라인 락의 dwell 해제 기준 시각 기록.
                    _resolvedConsumedLineAt[resolvedLineKey] = Time.unscaledTime;
                    // ROLLBACK_DART_POP_SCAN_STATE_STABILITY:
                    // The hit below invalidates DirectionalTargeting's outer-contour cache if it
                    // actually pops. Do not reset DartManager's per-holder scan/promotion state on
                    // projectile completion; doing so lets the same holder peel the newly exposed
                    // inner cell and also erases catch-up state for promoted heads at high speed.
                    GameObject projObj = proj.gameObject; // 참조 미리 저장
                    // ROLLBACK_DART_STABLE_OUTER_HIT:
                    // Do not retarget at impact time. Retargeting to the current first-hit cell lets
                    // a late projectile peel newly exposed inner cells, which is the continuous-fire
                    // bug. The authoritative gameplay hit is the fire-time target id.
                    if (projObj != null)
                        projObj.transform.DOKill();
                    __resolvePrepMs += InGamePerfLogger.ElapsedMs(__prepStamp);

                    float __hitStamp = InGamePerfLogger.StartStampMs();
                    ExecuteHit(proj.targetBalloonId, proj.color);
                    __executeHitMs += InGamePerfLogger.ElapsedMs(__hitStamp);

                    // ExecuteHit → OnBoardCleared → ClearAllDarts로 리스트가 비워질 수 있음
                    if (_boardFinished || _activeProjectiles.Count == 0) return;

                    float __returnStamp = InGamePerfLogger.StartStampMs();
                    ReturnDartToPool(projObj);
                    if (i < _activeProjectiles.Count)
                    {
                        _activeProjectiles.RemoveAt(i);
                        ReleaseProjectile(proj);
                    }
                    __returnPoolMs += InGamePerfLogger.ElapsedMs(__returnStamp);
                }
            }
            }
            finally
            {
                if (__moveScaleMs > 0f) InGamePerfLogger.RecordSectionMs("Dart.Projectiles.MoveScale", __moveScaleMs);
                if (__resolvePrepMs > 0f) InGamePerfLogger.RecordSectionMs("Dart.Projectiles.ResolvePrep", __resolvePrepMs);
                if (__executeHitMs > 0f) InGamePerfLogger.RecordSectionMs("Dart.Projectiles.ExecuteHit", __executeHitMs);
                if (__returnPoolMs > 0f) InGamePerfLogger.RecordSectionMs("Dart.Projectiles.ReturnPool", __returnPoolMs);
            }
        }

        // ROLLBACK_DART_NEEDLE_TIP_IMPACT:
        // Remove this timing setup and the impactTime fields to return to center-arrival pop timing.
        private static void ConfigureNeedleTipImpactTiming(
            DartProjectile proj,
            GameObject dartObj,
            Vector3 from,
            Vector3 to,
            Vector3 balloonScale)
        {
            if (proj == null || dartObj == null || proj.duration <= 0f)
                return;

            // ROLLBACK_DART_NEEDLE_TIP_TRANSFORM_IMPACT:
            // Restore impactTime = duration if pop timing must return to visual root arrival.
            // When DartIdentifier has a NeedleTip transform, that authored point wins over bounds,
            // so the balloon pops as the needle tip reaches the balloon surface.
            Vector3 travel = to - from;
            float travelDistance = travel.magnitude;
            if (travelDistance <= 0.0001f)
            {
                proj.impactTime = 0f;
                return;
            }

            Vector3 travelDir = travel / travelDistance;
            float needleLead = GetNeedleTipLead(dartObj, travelDir);
            float balloonSurfaceLead = GetBalloonSurfaceLead(balloonScale);
            float earlyLead = Mathf.Max(0f, needleLead + balloonSurfaceLead);
            if (earlyLead <= 0.0001f)
            {
                proj.impactTime = proj.duration;
                return;
            }

            float impactDistance = Mathf.Clamp(travelDistance - earlyLead, 0f, travelDistance);
            float impactT = impactDistance / travelDistance;
            proj.impactTime = Mathf.Clamp(proj.duration * impactT, 0f, proj.duration);
        }

        private static float GetNeedleTipLead(GameObject dartObj, Vector3 travelDir)
        {
            int dartObjId = dartObj.GetInstanceID();
            if (!_dartIdentifierCache.TryGetValue(dartObjId, out DartIdentifier identifier))
            {
                dartObj.TryGetComponent(out identifier);
                _dartIdentifierCache[dartObjId] = identifier;
            }
            if (identifier != null && identifier.TryGetNeedleTipLead(travelDir, out float identifierLead))
                return Mathf.Max(0f, identifierLead);

            _needleLeadRendererCache.Clear();
            dartObj.GetComponentsInChildren(false, _needleLeadRendererCache);
            bool found = TryGetRendererLead(dartObj.transform.position, _needleLeadRendererCache, travelDir, out float rendererLead);
            _needleLeadRendererCache.Clear();
            return found
                ? Mathf.Max(0f, rendererLead)
                : 0f;
        }

        private static bool TryGetRendererLead(Vector3 origin, List<Renderer> renderers, Vector3 dir, out float lead)
        {
            lead = 0f;
            if (renderers == null || renderers.Count == 0) return false;

            bool found = false;
            for (int i = 0; i < renderers.Count; i++)
            {
                Renderer r = renderers[i];
                if (r == null) continue;
                string n = r.gameObject.name;
                if (n == "Shadow" || n.Contains("Particle")) continue;

                Bounds b = r.bounds;
                Vector3 c = b.center;
                Vector3 e = b.extents;
                for (int sx = -1; sx <= 1; sx += 2)
                for (int sy = -1; sy <= 1; sy += 2)
                for (int sz = -1; sz <= 1; sz += 2)
                {
                    Vector3 p = c + new Vector3(e.x * sx, e.y * sy, e.z * sz);
                    float d = Vector3.Dot(p - origin, dir);
                    if (!found || d > lead)
                    {
                        lead = d;
                        found = true;
                    }
                }
            }

            return found && lead > 0.0001f;
        }

        private static float GetBalloonSurfaceLead(Vector3 balloonScale)
        {
            float diameter = Mathf.Max(Mathf.Abs(balloonScale.x), Mathf.Abs(balloonScale.z));
            return Mathf.Max(0f, diameter * 0.5f);
        }

        // 발사 punch와 비행 lerp 모두 startScale(=레일 사이즈)을 절대 초과하지 않도록 cap — 사용자 피드백: 다트가 레일 위보다 커보이면 안 됨
        private void ConfigureLaunchScale(DartProjectile proj, Vector3 dartScaleVec, Vector3 balloonScale)
        {
            proj.startScale = dartScaleVec;
            proj.targetScale = dartScaleVec;

            bool hasBoard = GameManager.HasInstance;
            bool punchOn = hasBoard && GameManager.Instance.Board.dartLaunchScalePunch;
            float punchDur = hasBoard ? GameManager.Instance.Board.dartLaunchScalePunchDuration : 0f;
            float overshoot = hasBoard ? GameManager.Instance.Board.dartLaunchScaleOvershoot : 1f;
            float lerpStr = hasBoard ? Mathf.Clamp01(GameManager.Instance.Board.dartScaleLerpStrength) : 1f;

            proj.lerpStrength = lerpStr;

            if (punchOn && punchDur > 0.001f && overshoot > 1.0001f)
            {
                proj.punchDuration = punchDur;
                proj.launchPunchT = punchDur * 0.5f;
                proj.punchPeakScale = dartScaleVec * Mathf.Min(overshoot, 1f);
            }
            else
            {
                proj.punchDuration = 0f;
                proj.launchPunchT = 0f;
                proj.punchPeakScale = dartScaleVec;
            }
        }

        private void ExecuteHit(int balloonId, int color)
        {
            // 다트로 풍선 팝 (기믹 처리 포함)
            // ROLLBACK_DART_HIT_RESULT_GUARD:
            // Only publish a successful dart hit after BalloonController actually popped the target.
            // Otherwise an already-popped/blocked target becomes a silent visual miss plus false combo.
            PopResult result = null;
            if (BalloonController.HasInstance)
            {
                float __popStamp = InGamePerfLogger.StartStampMs();
                result = BalloonController.Instance.PopBalloonWithDart(balloonId, color);
                InGamePerfLogger.EndSection(__popStamp, "Dart.ExecuteHit.PopBalloon");
            }

            bool hitAccepted = result != null && (result.success || result.hitAccepted);
            if (!hitAccepted)
            {
                LogAttackIssue(
                    "DartHitFailed",
                    $"balloonId={balloonId} color={color} reason={(result != null ? result.reason : "nullResult")}");
                return;
            }

            // PopProcessor가 점수/콤보 처리 (PopBalloon 중복 호출은 isPopped 체크로 방지)
            // ROLLBACK_DART_PARTIAL_HIT_ACCEPTANCE:
            // Partial gimmick hits (Frozen thaw, Barricade/Pinata HP damage) consume the dart and
            // should not be reported as misses. PopProcessor scores only when the balloon is popped.
            if (!result.success)
            {
                // ROLLBACK_DART_PARTIAL_HIT_CACHE_INVALIDATE_20260616:
                // Pin/Barricade/TargetBox/FlexTube partial hits may change targetability or exposed
                // colors without publishing OnBalloonPopped. Dirty shared caches so fail checks and
                // dart targeting do not keep using stale outer-shell data.
                // Rollback: remove this block if every partial gimmick publishes its own cache event.
                DirectionalTargeting.InvalidateCache();
                if (BoardStateManager.HasInstance)
                    BoardStateManager.Instance.InvalidateOutermostCache();
            }

            float __publishStamp = InGamePerfLogger.StartStampMs();
            EventBus.Publish(new OnDartHitBalloon
            {
                dartId = -1,
                balloonId = balloonId,
                color = color
            });
            InGamePerfLogger.EndSection(__publishStamp, "Dart.ExecuteHit.PublishHit");
        }

        #endregion

        #region Private Methods — Event Handlers

        private void HandleDartPlaced(OnDartPlacedOnSlot evt)
        {
            if (_boardFinished) return;
            CreateSlotDartVisual(evt.slotIndex, evt.color, evt.holderId);
        }

        private void HandleDartPlacedPerDart(OnDartPlaced evt)
        {
            if (_boardFinished) return;
            _lastDeployPlacedFrameByHolder[evt.holderId] = Time.frameCount;
            CreateDartVisualById(evt.dartId, evt.color, evt.holderId);
            SeedPlacedHeadCatchUp(evt);
        }

        /// <summary>
        /// A dart was frozen (removed from belt). Convert its slot visual to a pinned frozen visual.
        /// The visual stays at the dart's world position and does NOT move with the belt.
        /// </summary>
        private void HandleDartFrozen(OnDartFrozen evt)
        {
            if (_boardFinished) return;

            // Transfer visual from slot tracking to frozen tracking
            if (_slotVisuals.TryGetValue(evt.slotIndex, out SlotDartVisual slotVisual))
            {
                _frozenVisuals[evt.dartId] = slotVisual.gameObject;
                _slotVisuals.Remove(evt.slotIndex);
                // Visual stays at its current position — don't update it
            }
        }

        /// <summary>
        /// All frozen darts were reinserted back onto the belt.
        /// Remove all frozen visuals — new slot visuals will be created by OnDartPlacedOnSlot.
        /// </summary>
        private void HandleDartsFrozenCleared(OnDartsFrozenCleared evt)
        {
            foreach (var kvp in _frozenVisuals)
            {
                ReturnDartToPool(kvp.Value);
            }
            _frozenVisuals.Clear();
        }


        /// <summary>
        /// When a balloon is popped externally (chain pop, gimmick, etc.),
        /// clear its reservation so other darts can target new outermost balloons.
        /// NOTE: We do NOT auto-remove surplus darts when no matching balloons remain.
        /// Unmatched darts stay on rail, raising occupancy toward fail condition.
        /// This is core to Rail Overflow gameplay.
        /// </summary>
        private void HandleBalloonPopped(OnBalloonPopped evt)
        {
            // ROLLBACK_DART_STALL_WATCHDOG_PROGRESS_DECOUPLE_20260617:
            // 보드 진행(풍선 pop) 시각 기록 — 워치독이 '발사/비행' 대신 '실제 진행' 으로 정지를 판정.
            _lastPopUnscaledTime = Time.unscaledTime;
            _reservedTargets.Remove(evt.balloonId);
            // ROLLBACK_DART_POP_LINE_RESCAN:
            // BalloonController.ExecutePop already invalidates DirectionalTargeting's contour cache.
            // Also reopen DartManager's accepted-line cache for the popped row/column only; otherwise
            // a head that stays on the same line never asks for the refreshed outer target.
            InvalidateDartScanLinesForPoppedBalloon(evt);
        }

        private void InvalidateDartScanLinesForPoppedBalloon(OnBalloonPopped evt)
        {
            if (!BalloonController.HasInstance)
            {
                InvalidateDartScanLinesForPoppedPosition(evt.position);
                return;
            }

            BalloonData data = BalloonController.Instance.GetBalloon(evt.balloonId);
            if (data == null || !BalloonController.IsSizedFieldGimmick(data.gimmickType) || (data.sizeW <= 1 && data.sizeH <= 1))
            {
                InvalidateDartScanLinesForPoppedPosition(BalloonController.Instance.GetAdjustedBoardPosition(evt.position));
                return;
            }

            // ROLLBACK_MULTI_CELL_POP_LINE_RESCAN:
            // A sized field object can expose refreshed targets on every occupied row/column.
            // Reopening only the anchor cell leaves other lines locked and creates apparent misses
            // around irregular A/B shapes and square rails.
            int width = Mathf.Max(1, data.sizeW);
            int height = Mathf.Max(1, data.sizeH);
            Vector3 anchor = BalloonController.Instance.GetAdjustedBoardPosition(data.position);
            BalloonController.Instance.GetAdjustedCellSize(out float cellSizeX, out float cellSizeZ);

            for (int dx = 0; dx < width; dx++)
            {
                for (int dz = 0; dz < height; dz++)
                {
                    Vector3 cellWorld = new Vector3(
                        anchor.x + dx * cellSizeX,
                        anchor.y,
                        anchor.z + dz * cellSizeZ);
                    InvalidateDartScanLinesForPoppedPosition(cellWorld);
                }
            }
        }

        /// <summary>[2026-06-11] FlexTube 등 'silent cell 제거'(OnBalloonPopped 미발행) 알림 —
        /// 팝과 동일하게 해당 위치 라인의 head 스캔 수락 캐시를 재개방한다.
        /// 미호출 시 같은 라인에 머무는 head 가 새로 노출된 타겟을 재스캔하지 않아
        /// '공격 가능한데 공격 안 함' 상태가 되고, 홀더 선택 같은 전체 무효화 때까지 지속된다.</summary>
        public void NotifySilentCellRemoved(Vector3 adjustedWorldPosition)
        {
            InvalidateDartScanLinesForPoppedPosition(adjustedWorldPosition);
        }

        /// <summary>
        /// Board cleared — stop all dart activity and clear remaining darts from rail.
        /// Surplus darts exist due to Chain gimmick auto-popping adjacent balloons without darts.
        /// </summary>
        private void HandleBoardCleared(OnBoardCleared evt)
        {
            _boardFinished = true;
            ClearAllDarts();

            // Also clear all darts from rail slot data
            if (RailManager.HasInstance)
            {
                RailManager.Instance.ResetAll();
            }

            // Safety: delayed re-clear catches any darts placed by coroutines
            // that resumed after OnBoardCleared but before StopAllCoroutines took effect
            StartCoroutine(DelayedClearCoroutine());
        }

        private IEnumerator DelayedClearCoroutine()
        {
            yield return null; // wait 1 frame
            ClearAllDarts();
            if (RailManager.HasInstance)
            {
                RailManager.Instance.ResetAll();
            }
        }

        private void HandleBoardFailed(OnBoardFailed evt)
        {
            _boardFinished = true;
            // [FAIL_DERAIL 2026-06-12] 실패 인식 → 레일 다트 탈선 흩어짐 연출.
            // 실패 팝업은 ContinueHandler 가 FailScatterPopupDelay 만큼 기다렸다 띄운다.
            if (_failScatterCo != null) StopCoroutine(_failScatterCo);
            _failScatterCo = StartCoroutine(PlayFailDerailScatter());
        }

        private void HandleContinueApplied(OnContinueApplied evt)
        {
            _boardFinished = false;
            // [FAIL_DERAIL 2026-06-12] 흩어진 상태에서 이어하기 → 제거 다트는 즉시 소멸,
            // 생존 다트는 라이브 레일 포즈로 복귀 연출 후 동기/스캔 재개.
            if (_failScatterActive)
            {
                if (_continueRestoreCo != null) StopCoroutine(_continueRestoreCo);
                _continueRestoreCo = StartCoroutine(PlayContinueRestoreFromScatter());
            }
        }

        #region Fail Derail Scatter — [FAIL_DERAIL 2026-06-12]

        // 실패 시 레일 다트가 탈선하듯 주변으로 흩어졌다가(뒹굴며), 이어하기 시 원래 레일 위치로 복귀.
        // 연출은 transform 전용 — DartOnRail 논리 상태(슬롯/순서/색, _boardFinished 동결)는 불변이라
        // 이어하기 1:1 정합(2026-06-11)이 그대로 유지된다. 모든 트윈/대기는 unscaled(팝업 pause 대응).
        public const float FailScatterDuration = 0.55f;    // 다트별 흩어짐 트윈 길이
        public const float FailScatterMaxStagger = 0.18f;  // 다트별 시작 시차(랜덤)
        /// <summary>ContinueHandler 가 실패 팝업을 띄우기 전 대기 시간 (흩어짐 완료 + 여유).</summary>
        public const float FailScatterPopupDelay = 0.85f;
        private const float FailScatterMinDistance = 0.35f; // 흩어짐 거리(월드)
        private const float FailScatterMaxDistance = 0.95f;
        private const float ContinueRestoreDuration = 0.45f;

        private bool _failScatterActive;       // 흩어진 상태(복귀 전)
        private bool _continueRestoreActive;   // 복귀 연출 중 — Update(동기/스캔/발사) 전체 억제
        private Coroutine _failScatterCo;
        private Coroutine _continueRestoreCo;
        private readonly Dictionary<int, Vector3> _restoreStartPos = new Dictionary<int, Vector3>(64);
        private readonly Dictionary<int, Quaternion> _restoreStartRot = new Dictionary<int, Quaternion>(64);

        private IEnumerator PlayFailDerailScatter()
        {
            _failScatterActive = true;
            if (!RailManager.HasInstance) yield break;
            RailManager rail = RailManager.Instance;

            foreach (var kvp in _dartVisuals)
            {
                GameObject go = kvp.Value.gameObject;
                if (go == null) continue;
                if (rail.FindDart(kvp.Key) == null) continue; // 레일에 없는 다트(발사체 등) 제외

                Vector3 start = go.transform.position;
                Vector2 dir2 = Random.insideUnitCircle.normalized;
                if (dir2.sqrMagnitude < 0.001f) dir2 = Vector2.down;
                float dist = Random.Range(FailScatterMinDistance, FailScatterMaxDistance);
                Vector3 end = start + new Vector3(dir2.x, 0f, dir2.y) * dist;
                float delay = Random.Range(0f, FailScatterMaxStagger);

                // 뒹구는 느낌 — 탑다운 화면 기준 Y 스핀(회전) + X/Z 기울임('누운' 실루엣) 랜덤 조합.
                Vector3 tumble = new Vector3(
                    Random.Range(40f, 100f) * (Random.value < 0.5f ? -1f : 1f),
                    Random.Range(140f, 360f) * (Random.value < 0.5f ? -1f : 1f),
                    Random.Range(20f, 70f) * (Random.value < 0.5f ? -1f : 1f));

                go.transform.DOKill();
                var seq = DOTween.Sequence().SetUpdate(true).SetLink(go, LinkBehaviour.KillOnDisable);
                seq.AppendInterval(delay);
                seq.Append(go.transform.DOMove(end, FailScatterDuration).SetEase(Ease.OutCubic));
                seq.Join(go.transform.DORotate(tumble, FailScatterDuration, RotateMode.WorldAxisAdd)
                    .SetEase(Ease.OutCubic));
            }

            yield return new WaitForSecondsRealtime(FailScatterMaxStagger + FailScatterDuration);
            _failScatterCo = null;
        }

        private IEnumerator PlayContinueRestoreFromScatter()
        {
            _continueRestoreActive = true;   // Update 전체 skip — 복귀 중 동기 스톰핑/발사 방지
            RailManager rail = RailManager.HasInstance ? RailManager.Instance : null;
            if (rail == null)
            {
                _failScatterActive = false;
                _continueRestoreActive = false;
                _continueRestoreCo = null;
                yield break;
            }

            RefreshFadeCache(); // _cachedDartPathOffset 갱신 (레일 포즈 계산용)

            // 1) 이어하기로 제거된 다트 — "그냥 사라지게" (스펙): 즉시 풀 반환.
            _tempRemoveKeys.Clear();
            foreach (var kvp in _dartVisuals)
            {
                GameObject go = kvp.Value.gameObject;
                if (go == null || rail.FindDart(kvp.Key) == null)
                {
                    if (go != null) { go.transform.DOKill(); ReturnDartToPool(go); }
                    _tempRemoveKeys.Add(kvp.Key);
                }
            }
            for (int i = 0; i < _tempRemoveKeys.Count; i++)
                _dartVisuals.Remove(_tempRemoveKeys[i]);

            // 2) 생존 다트 — 흩어진 현재 포즈에서 '라이브' 레일 포즈로 lerp.
            //    벨트가 복귀 중에도 전진할 수 있어 목표 포즈를 매 프레임 재계산 → 핸드오프 스냅 없음.
            _restoreStartPos.Clear();
            _restoreStartRot.Clear();
            foreach (var kvp in _dartVisuals)
            {
                GameObject go = kvp.Value.gameObject;
                if (go == null) continue;
                go.transform.DOKill(); // 흩어짐 트윈 잔존 시 충돌 방지
                _restoreStartPos[kvp.Key] = go.transform.position;
                _restoreStartRot[kvp.Key] = go.transform.rotation;
            }

            float t = 0f;
            while (t < ContinueRestoreDuration)
            {
                t += Time.unscaledDeltaTime;
                float k = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t / ContinueRestoreDuration));
                foreach (var kvp in _dartVisuals)
                {
                    GameObject go = kvp.Value.gameObject;
                    if (go == null) continue;
                    var dart = rail.FindDart(kvp.Key);
                    if (dart == null) continue;
                    if (!_restoreStartPos.TryGetValue(kvp.Key, out Vector3 sp)) continue;

                    GetRailVisualPose(rail, dart, out Vector3 railPos, out Quaternion railRot);
                    go.transform.position = Vector3.Lerp(sp, railPos, k);
                    if (_restoreStartRot.TryGetValue(kvp.Key, out Quaternion sr))
                        go.transform.rotation = Quaternion.Slerp(sr, railRot, k);
                }
                yield return null;
            }

            _restoreStartPos.Clear();
            _restoreStartRot.Clear();
            _failScatterActive = false;
            _continueRestoreActive = false;
            _continueRestoreCo = null;
        }

        /// <summary>UpdatePerDartPositions 와 동일한 레일 포즈 계산 (위치+조준 회전).</summary>
        private void GetRailVisualPose(RailManager rail, RailManager.DartOnRail dart, out Vector3 pos, out Quaternion rot)
        {
            rail.GetPositionAndDirectionAtDistance(dart.progress, out pos, out Vector3 tangent);
            Vector3 inward = Vector3.Cross(tangent, Vector3.up).normalized;
            pos += inward * _cachedDartPathOffset;
            rot = (tangent.sqrMagnitude > 0.001f && inward.sqrMagnitude > 0.001f)
                ? Quaternion.LookRotation(inward)
                : Quaternion.identity;
        }

        /// <summary>레벨 정리/재시작 경로 — 탈선 연출 상태 리셋.</summary>
        private void ResetFailScatterState()
        {
            if (_failScatterCo != null) { StopCoroutine(_failScatterCo); _failScatterCo = null; }
            if (_continueRestoreCo != null) { StopCoroutine(_continueRestoreCo); _continueRestoreCo = null; }
            _failScatterActive = false;
            _continueRestoreActive = false;
            _restoreStartPos.Clear();
            _restoreStartRot.Clear();
        }

        #endregion

        #endregion

        #region Private Methods — Targeting Helpers

        /// <summary>
        /// Calculates the dart's flight destination along a pure cardinal axis.
        /// Instead of flying diagonally from rail to balloon (crossing other columns),
        /// the dart flies straight inward along the rail's firing direction to the balloon's depth.
        /// </summary>
        private static Vector3 CalculateCardinalTarget(Vector3 from, Vector3 balloonPos, DirectionalTargeting.ScanDirection scanDir)
        {
            switch (scanDir)
            {
                case DirectionalTargeting.ScanDirection.Right:
                case DirectionalTargeting.ScanDirection.Left:
                    return new Vector3(balloonPos.x, from.y, from.z);
                case DirectionalTargeting.ScanDirection.Up:
                case DirectionalTargeting.ScanDirection.Down:
                    return new Vector3(from.x, from.y, balloonPos.z);
            }

            // Determine dominant axis (which direction is the dart moving more)
            float dx = Mathf.Abs(balloonPos.x - from.x);
            float dz = Mathf.Abs(balloonPos.z - from.z);

            if (dx > dz)
            {
                // Firing along X axis — keep dart's Z, move to balloon's X
                return new Vector3(balloonPos.x, from.y, from.z);
            }
            else
            {
                // Firing along Z axis — keep dart's X, move to balloon's Z
                return new Vector3(from.x, from.y, balloonPos.z);
            }
        }

        private static Vector3 CalculateCardinalLaunchPosition(Vector3 from, Vector3 balloonPos, DirectionalTargeting.ScanDirection scanDir)
        {
            switch (scanDir)
            {
                case DirectionalTargeting.ScanDirection.Right:
                case DirectionalTargeting.ScanDirection.Left:
                    return new Vector3(from.x, from.y, balloonPos.z);
                case DirectionalTargeting.ScanDirection.Up:
                case DirectionalTargeting.ScanDirection.Down:
                    return new Vector3(balloonPos.x, from.y, from.z);
            }

            return from;
        }

        #endregion

        #region Private Methods — Pool & Visual

        private void ReturnDartToPool(GameObject obj)
        {
            if (obj == null) return;

            // [ROLLBACK_DART_FX_TRAIL] pool 반환 전 FXDartTrail 비활성 — 다음 spawn 시 깨끗한 상태.
            SetDartTrailActive(obj, false);

            if (ObjectPoolManager.HasInstance)
                ObjectPoolManager.Instance.Return(DART_POOL_KEY, obj);
        }

        private void ApplyColor(GameObject obj, int color)
        {
            Color c = HolderVisualManager.GetColor(color);

            // DartIdentifier에 기반 Material + Renderer가 할당되어 있으면 복제 방식
            DartIdentifier dartId = obj.GetComponent<DartIdentifier>();
            if (dartId != null && dartId.HasColorRenderers)
            {
                dartId.ApplyColor(c);
                return;
            }

            // fallback: 전체 Renderer (TMP/Shadow/Particle 제외)
            Material shared = BalloonController.GetOrCreateSharedMaterial(c);
            if (shared == null) return;
            _applyColorRendererCache.Clear();
            obj.GetComponentsInChildren(false, _applyColorRendererCache);
            for (int i = 0; i < _applyColorRendererCache.Count; i++)
            {
                Renderer renderer = _applyColorRendererCache[i];
                if (renderer == null) continue;
                if (renderer.GetComponent<TMPro.TMP_Text>() != null) continue;
                string name = renderer.gameObject.name;
                if (name == "Shadow" || name.Contains("Particle")) continue;
                renderer.sharedMaterial = shared;
            }
            _applyColorRendererCache.Clear();
        }

        private void OrientDart(GameObject obj, int slotIndex)
        {
            if (!RailManager.HasInstance) return;

            Vector3 fireDir = RailManager.Instance.GetSlotFiringDirection(slotIndex);
            if (fireDir.sqrMagnitude > 0.001f)
            {
                obj.transform.rotation = Quaternion.LookRotation(fireDir);
            }
        }

        #endregion
    }
}
