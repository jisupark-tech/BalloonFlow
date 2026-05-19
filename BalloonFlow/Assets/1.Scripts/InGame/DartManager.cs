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
        private const float DEFAULT_PROJECTILE_FLIGHT_TIME = 0.1f;
        private const float PROJECTILE_MIN_FLIGHT_TIME = 0.015f;
        private const float PROJECTILE_FLIGHT_SPEED_MULTIPLIER = 2.2f;
        private const float PROJECTILE_MIN_FLIGHT_TIME_SCALE = 0.35f;
        private const float PROJECTILE_MAX_FLIGHT_TIME_SCALE = 4f;
        private const int ADJACENT_EMPTY_LINE_RESCUE_RADIUS = 1;
        #endregion

        #region Serialized Fields

        [SerializeField] private float _projectileFlightTime = DEFAULT_PROJECTILE_FLIGHT_TIME;

        [Tooltip("다트 포물선 곡사 높이. 0=직선, >0=곡사. Design ref: 피드백디렉션 §다트궤적")]
        [SerializeField] private float _arcHeight = 0f; // 0 = 직사, >0 = 곡사

        /// <summary>동적 비행 시간 (GameManager에서 실시간 참조).</summary>
        private float FlightTime => GameManager.HasInstance ? GameManager.Instance.Board.dartFlightTime : _projectileFlightTime;

        /// <summary>유저 가속 반영된 비행 시간.
        /// x2 토글 시 투사체도 2x 빠르게 풍선에 도달 → 공격 플로우 일관성.</summary>
        private float EffectiveFlightTime
        {
            get
            {
                float t = FlightTime;
                float mult = RailManager.HasInstance ? RailManager.Instance.UserSpeedMultiplier : 1f;
                if (mult > 0.001f) t /= mult;

                return t;
            }
        }

        // ROLLBACK_DART_DISTANCE_BASED_FLIGHT_TIME:
        // Restore callers to EffectiveFlightTime if fixed-speed projectile travel causes gameplay
        // timing regressions. The existing dartFlightTime is treated as the time to travel one
        // board cell, so close targets resolve faster and far targets resolve later while preserving
        // the old one-cell feel.
        // ROLLBACK_DART_PROJECTILE_SPEED_X3:
        // Set PROJECTILE_FLIGHT_SPEED_MULTIPLIER back to 1f and PROJECTILE_MIN_FLIGHT_TIME to 0.035f
        // if the faster dart travel causes hit timing or visual readability regressions.
        private float CalculateProjectileFlightTime(Vector3 from, Vector3 to)
        {
            float baseTime = Mathf.Max(0.001f, FlightTime);
            float speedMultiplier = RailManager.HasInstance ? RailManager.Instance.UserSpeedMultiplier : 1f;
            if (speedMultiplier <= 0.001f)
                speedMultiplier = 1f;
            speedMultiplier *= PROJECTILE_FLIGHT_SPEED_MULTIPLIER;

            float cellSpacing = GameManager.HasInstance ? GameManager.Instance.Board.cellSpacing : 0.55f;
            if (cellSpacing <= 0.01f)
                cellSpacing = 0.55f;

            Vector3 flatFrom = new Vector3(from.x, 0f, from.z);
            Vector3 flatTo = new Vector3(to.x, 0f, to.z);
            float distance = Vector3.Distance(flatFrom, flatTo);
            float unitsPerSecond = cellSpacing / baseTime;
            float duration = distance / Mathf.Max(0.001f, unitsPerSecond * speedMultiplier);

            float minDuration = Mathf.Max(PROJECTILE_MIN_FLIGHT_TIME, baseTime * PROJECTILE_MIN_FLIGHT_TIME_SCALE / speedMultiplier);
            float maxDuration = Mathf.Max(minDuration, baseTime * PROJECTILE_MAX_FLIGHT_TIME_SCALE / speedMultiplier);
            return Mathf.Clamp(duration, minDuration, maxDuration);
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
        private static readonly bool DART_MISS_SUSPECT_DEBUG = false;
#endif
        // ROLLBACK_DART_ATTACK_ISSUE_DEBUG:
        // Temporary, throttled diagnostics for continuous-fire and miss paths. Disable this after
        // capturing a repro sample; every branch below is intentionally log-only except the matching
        // holder-line guard inside FireDartCandidate.
#if BALLOONFLOW_DART_ATTACK_ISSUE_DEBUG
        private static readonly bool DART_ATTACK_ISSUE_DEBUG = true;
#else
        private static readonly bool DART_ATTACK_ISSUE_DEBUG = false;
#endif

        // ROLLBACK_DART_CROSSED_LINE_CACHE_FIX:
        // At x2 speed or after a long frame, a head can cross several grid lines before this scan
        // runs. Keep this exact-line budget wide enough to replay skipped lines without falling
        // back to adjacent-line targeting.
        private const int MAX_LINE_CATCH_UP_PER_HEAD = 6;
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
        private const float POST_FIRE_HEAD_RESCAN_MIN_SPEED = 1.01f;
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
            if (GameManager.HasInstance)
            {
                _projectileFlightTime = GameManager.Instance.Board.dartFlightTime;
            }
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
            proj.elapsed = 0f;
            proj.duration = ft;
            proj.impactTime = ft;
            ConfigureLaunchScale(proj, launchStartScale, balloonScale);
            ConfigureNeedleTipImpactTiming(proj, dartObj, from, to, balloonScale);

            _activeProjectiles.Add(proj);
            EnableDartFlightTrail(dartObj);

            // [2026-05-19 DISABLED] Flight trail 활성화 — 주석. 재활성 시 DartIdentifier._flightTrail + Enable/DisableTrail 함께 주석 해제.
            // if (dartObj.TryGetComponent<DartIdentifier>(out var dartIdFire))
            //     dartIdFire.EnableTrail();

            // Flight: parabolic arc (곡사) or linear depending on _arcHeight
            if (_arcHeight > 0.01f)
            {
                Vector3 midPoint = (from + to) * 0.5f;
                midPoint.y += _arcHeight;
                Vector3[] path = { from, midPoint, to };
                dartObj.transform.DOPath(path, ft, PathType.CatmullRom)
                    .SetEase(Ease.Linear)
                    .SetLookAt(0.01f); // face movement direction
            }
            else
            {
                dartObj.transform.DOMove(to, ft).SetEase(Ease.Linear);
            }
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

                ResetStraightRailPassIfWrapped(holderId, currentScanDir, currentLine);
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

                if (catchUpFromLastScan)
                {
                    int delta = currentLine - lastLine;
                    int absDelta = Mathf.Abs(delta);
                    if (absDelta > 1)
                    {
                        int catchUpBudget = MaxLineCatchUpPerHead;
                        if (absDelta > catchUpBudget)
                        {
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

                    dartObj.transform.DOMove(travelTarget, ft).SetEase(Ease.Linear);
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
            int maxFireAttemptsThisScan = Mathf.Max(1, _fireCandidates.Count * (1 + MAX_POST_FIRE_HEAD_RESCANS_PER_HOLDER));
            for (int attempts = 0; _fireCandidates.Count > 0 && attempts < maxFireAttemptsThisScan && firedThisScan < maxFireAttemptsThisScan; attempts++)
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
            PruneConsumedTargetLinesForCurrentHeads(rail);
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
                findTargetDiag = DirectionalTargeting.LastFindTargetDiag
            };

            return true;
        }

        private bool FireDartCandidate(RailManager rail, DartFireCandidate candidate)
        {
            if (!BalloonController.HasInstance) return false;

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
                proj.elapsed = 0f;
                proj.duration = ft;
                proj.impactTime = ft;
                ConfigureLaunchScale(proj, launchStartScale, balloonScale);
                ConfigureNeedleTipImpactTiming(proj, dartObj, launchPos, travelTarget, balloonScale);
                _activeProjectiles.Add(proj);
                EnableDartFlightTrail(dartObj);

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
                _unresolvedConsumedTargetLines.Remove(GetConsumedLineKey(candidate.scanDir, candidate.scanLine));
                LogAttackIssue(
                    "DartProjectileFallback",
                    $"reason=noVisualImmediateHit holder={candidate.holderId} dartId={candidate.dartId} " +
                    $"target={candidate.targetId} scan={candidate.scanDir} line={candidate.scanLine}");
                ExecuteHit(candidate.targetId, candidate.color);
            }

            return true;
        }

        private static int GetScanLine(Vector3 pos, DirectionalTargeting.ScanDirection scanDir)
        {
            float cs = GameManager.HasInstance ? GameManager.Instance.Board.cellSpacing : 0.55f;
            if (cs <= 0.01f) cs = 0.55f;

            switch (scanDir)
            {
                case DirectionalTargeting.ScanDirection.Right:
                case DirectionalTargeting.ScanDirection.Left:
                    return Mathf.RoundToInt(pos.z / cs);
                case DirectionalTargeting.ScanDirection.Up:
                case DirectionalTargeting.ScanDirection.Down:
                    return Mathf.RoundToInt(pos.x / cs);
                default:
                    return 0;
            }
        }

        private static Vector3 MakeScanPositionForLine(Vector3 currentPos, DirectionalTargeting.ScanDirection scanDir, int line)
        {
            float cs = GameManager.HasInstance ? GameManager.Instance.Board.cellSpacing : 0.55f;
            if (cs <= 0.01f) cs = 0.55f;

            switch (scanDir)
            {
                case DirectionalTargeting.ScanDirection.Right:
                case DirectionalTargeting.ScanDirection.Left:
                    return new Vector3(currentPos.x, currentPos.y, line * cs);
                case DirectionalTargeting.ScanDirection.Up:
                case DirectionalTargeting.ScanDirection.Down:
                    return new Vector3(line * cs, currentPos.y, currentPos.z);
                default:
                    return currentPos;
            }
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
            int currentLine)
        {
            if (!RailManager.HasInstance) return;
            if (RailManager.GetRailSideCount(RailManager.Instance.PhysicalCapacity) != 1) return;
            if (scanDir != DirectionalTargeting.ScanDirection.Up) return;

            if (!_holderPassDirectionByHolder.TryGetValue(holderId, out DirectionalTargeting.ScanDirection passDir)
                || passDir != scanDir)
            {
                return;
            }

            if (!_lastFiredLineByHolder.TryGetValue(holderId, out int lastFiredLine))
                return;

            if (currentLine < lastFiredLine - MaxLineCatchUpPerHead)
                ClearConsumedLineLockForHolder(holderId);
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
                && currentLine < lastLine - MaxLineCatchUpPerHead;
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
            _consumedTargetLines.Clear();
            _unresolvedConsumedTargetLines.Clear();
            _currentHeadLineKeys.Clear();
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
            foreach (int consumedKey in _consumedTargetLines)
            {
                if (_unresolvedConsumedTargetLines.Contains(consumedKey))
                    continue;

                if (!_currentHeadLineKeys.Contains(consumedKey))
                    _tempRemoveKeys.Add(consumedKey);
            }

            for (int i = 0; i < _tempRemoveKeys.Count; i++)
            {
                _consumedTargetLines.Remove(_tempRemoveKeys[i]);
                _unresolvedConsumedTargetLines.Remove(_tempRemoveKeys[i]);
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
            }

            _tempRemoveKeys.Clear();
        }

        private void ClearResolvedConsumedTargetLine(DirectionalTargeting.ScanDirection scanDir, int line)
        {
            int key = GetConsumedLineKey(scanDir, line);
            if (_unresolvedConsumedTargetLines.Contains(key))
                return;

            _consumedTargetLines.Remove(key);
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

                if (proj.gameObject != null && proj.duration > 0f)
                {
                    float __moveStamp = InGamePerfLogger.StartStampMs();
                    // ROLLBACK_DART_PROJECTILE_MANUAL_MOVE:
                    // This replaces per-shot Transform.DOMove allocation with deterministic
                    // per-frame interpolation. Gameplay still resolves using impactTime below,
                    // so miss/continuous-fire guards are not changed.
                    float moveT = Mathf.Clamp01(proj.elapsed / proj.duration);
                    proj.gameObject.transform.position = Vector3.Lerp(proj.startPosition, proj.targetPosition, moveT);

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
                    _unresolvedConsumedTargetLines.Remove(GetConsumedLineKey(proj.scanDir, proj.scanLine));
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
        }

        private void HandleContinueApplied(OnContinueApplied evt)
        {
            _boardFinished = false;
        }

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
            DisableDartFlightTrail(obj);

            // [2026-05-19 DISABLED] Flight trail 비활성화 — 주석. 재활성 시 LaunchProjectile 의 EnableTrail 과 함께 주석 해제.
            // if (obj.TryGetComponent<DartIdentifier>(out var dartId))
            //     dartId.DisableTrail();

            if (ObjectPoolManager.HasInstance)
                ObjectPoolManager.Instance.Return(DART_POOL_KEY, obj);
        }

        // ROLLBACK_DART_FLIGHT_TRAIL:
        // Visual-only hooks. They run when a dart becomes an in-flight projectile and before it
        // returns to the pool, leaving targeting, reservations, fire order, and hit timing intact.
        // [2026-05-19 DISABLED] Tail/Trail Renderer effect disabled by request. Keep Disable active
        // so any prefab-attached TrailRenderer is reset before pooling.
        private static readonly bool DART_FLIGHT_TRAIL_ENABLED = true;

        private static void EnableDartFlightTrail(GameObject obj)
        {
            if (!DART_FLIGHT_TRAIL_ENABLED) return;

            if (obj != null && obj.TryGetComponent(out DartIdentifier dartId))
                dartId.EnableTrail();
        }

        private static void DisableDartFlightTrail(GameObject obj)
        {
            if (obj != null && obj.TryGetComponent(out DartIdentifier dartId))
                dartId.DisableTrail();
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
