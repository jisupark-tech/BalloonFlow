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

        #endregion

        #region Serialized Fields

        [SerializeField] private float _projectileFlightTime = DEFAULT_PROJECTILE_FLIGHT_TIME;

        [Tooltip("다트 포물선 곡사 높이. 0=직선, >0=곡사. Design ref: 피드백디렉션 §다트궤적")]
        [SerializeField] private float _arcHeight = 1.5f;

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
            public Vector3 targetPosition;
            public int targetBalloonId;
            public int color;
            public float elapsed;
            public float duration;
        }

        #endregion

        #region Fields

        private readonly Dictionary<int, SlotDartVisual> _slotVisuals = new Dictionary<int, SlotDartVisual>();
        private readonly Dictionary<int, SlotDartVisual> _dartVisuals = new Dictionary<int, SlotDartVisual>();
        private readonly List<DartProjectile> _activeProjectiles = new List<DartProjectile>();

        /// <summary>
        /// Balloon IDs currently targeted by in-flight projectiles.
        /// Prevents multiple darts from firing at the same balloon.
        /// Cleared when projectile hits or balloon is popped externally.
        /// </summary>
        private readonly HashSet<int> _reservedTargets = new HashSet<int>();

        /// <summary>Max darts that can fire in a single frame to prevent visual clutter.</summary>
        private const int MAX_FIRES_PER_FRAME = 40;

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
        private float _scanTimer;

        private void Update()
        {
            if (_boardFinished) return;

            UpdateSlotDartPositions();
            UpdatePerDartPositions();

            // 공격 스캔: 벨트 속도 적응형 간격 (1슬롯 이동당 최소 2회 스캔)
            _scanTimer += Time.deltaTime;
            float baseInterval = GameManager.HasInstance ? GameManager.Instance.Board.dartFireInterval : 0.05f;
            float railSpeed = RailManager.HasInstance ? RailManager.Instance.RotationSpeed : 10f;
            float speedInterval = railSpeed > 0f ? 0.5f / railSpeed : baseInterval;
            float interval = Mathf.Min(baseInterval, speedInterval);
            if (_scanTimer >= interval)
            {
                _scanTimer -= interval;
                ScanAndFireDarts();
                ScanAndFirePerDart();
            }

            UpdateProjectiles();
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
                    ReturnDartToPool(_activeProjectiles[i].gameObject);
            }
            _activeProjectiles.Clear();
            _reservedTargets.Clear();

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
                float maxScale = spacing * 0.9f; // 슬롯 간격의 90%
                Vector3 s = dartObj.transform.localScale;
                float currentSize = Mathf.Max(s.x, s.z);
                if (currentSize > maxScale)
                {
                    float ratio = maxScale / currentSize;
                    dartObj.transform.localScale = new Vector3(s.x * ratio, s.y * ratio, s.z * ratio);
                }
            }

            _slotVisuals[slotIndex] = new SlotDartVisual
            {
                slotIndex = slotIndex,
                color = color,
                gameObject = dartObj,
                baseScale = dartObj.transform.localScale
            };
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
            ApplyColor(dartObj, color);

            // 슬롯 간격 기반 스케일 축소 (겹침 방지)
            float spacing = RailManager.Instance.SlotSpacing;
            if (spacing > 0.01f)
            {
                float maxScale = spacing * 0.9f;
                Vector3 s = dartObj.transform.localScale;
                float currentSize = Mathf.Max(s.x, s.z);
                if (currentSize > maxScale)
                {
                    float ratio = maxScale / currentSize;
                    dartObj.transform.localScale = new Vector3(s.x * ratio, s.y * ratio, s.z * ratio);
                }
            }

            _dartVisuals[dartId] = new SlotDartVisual
            {
                slotIndex = dartId,
                color = color,
                gameObject = dartObj,
                baseScale = dartObj.transform.localScale
            };
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
        private const float CAVE_FADE_START = 0.0315f;  // 이 지점부터 축소 시작
        private const float CAVE_FADE_END   = 0.03f;  // 이 지점에서 스케일 0

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

                // Cave 스케일: 비순환 레일의 끝점/시작점 근처에서 축소
                if (isOpen && pathLen > 0f)
                {
                    float dist = slotIdx * rail.SlotSpacing + rail.RotationOffset;
                    float t = ((dist % pathLen) + pathLen) % pathLen / pathLen; // 0~1 정규화

                    float scale = 1f;
                    float fadeRange = CAVE_FADE_START - CAVE_FADE_END;
                    if (t < CAVE_FADE_START)
                    {
                        // 시작점에서 나옴: FADE_END(0) → FADE_START(1)
                        scale = t <= CAVE_FADE_END ? 0f : (t - CAVE_FADE_END) / fadeRange;
                    }
                    else if (t > 1f - CAVE_FADE_START)
                    {
                        // 끝점으로 들어감: (1-FADE_START)(1) → (1-FADE_END)(0)
                        float distFromEnd = 1f - t;
                        scale = distFromEnd <= CAVE_FADE_END ? 0f : (distFromEnd - CAVE_FADE_END) / fadeRange;
                    }

                    scale = Mathf.Clamp01(scale);
                    visual.gameObject.transform.localScale = visual.baseScale * scale;
                }
            }

            // Deferred removal
            for (int i = 0; i < _tempRemoveKeys.Count; i++)
                _slotVisuals.Remove(_tempRemoveKeys[i]);
        }

        #endregion

        #region Private Methods — Per-Dart Movement

        private void UpdatePerDartPositions()
        {
            if (!RailManager.HasInstance || _dartVisuals.Count == 0) return;

            RailManager rail = RailManager.Instance;
            bool isOpen = !rail.IsClosedLoop;
            float pathLen = rail.TotalPathLength;

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

                Vector3 pos = rail.GetPositionAtDistance(dart.progress);
                visual.gameObject.transform.position = pos;

                // Orient along path
                Vector3 fireDir = rail.GetDartFiringDirection(dartId);
                if (fireDir.sqrMagnitude > 0.001f)
                    visual.gameObject.transform.rotation = Quaternion.LookRotation(fireDir);

                // Cave scale for open rails
                if (isOpen && pathLen > 0f)
                {
                    float t = dart.progress / pathLen;
                    float scale = 1f;
                    float fadeStart = CAVE_FADE_START;
                    float fadeEnd = CAVE_FADE_END;
                    float fadeRange = fadeStart - fadeEnd;
                    if (fadeRange > 0f)
                    {
                        if (t < fadeStart)
                            scale = t <= fadeEnd ? 0f : (t - fadeEnd) / fadeRange;
                        else if (t > 1f - fadeStart)
                        {
                            float distFromEnd = 1f - t;
                            scale = distFromEnd <= fadeEnd ? 0f : (distFromEnd - fadeEnd) / fadeRange;
                        }
                    }
                    scale = Mathf.Clamp01(scale);
                    visual.gameObject.transform.localScale = visual.baseScale * scale;
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

            // Step 1: 보관함별 가장 선행(낮은 dartId) 다트 찾기
            _holderFrontDartId.Clear();
            _blockedHolders.Clear();

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

            // Step 2: 스캔 + 순차 공격
            int fired = 0;

            for (int s = 0; s < slotCount && fired < MAX_FIRES_PER_FRAME; s++)
            {
                int slotIdx = s;

                RailManager.SlotData slot = rail.GetSlot(slotIdx);
                if (slot.dartColor < 0) continue;

                // 같은 보관함의 선행 다트가 막혔으면 후행도 차단
                if (_blockedHolders.Contains(slot.holderId)) continue;

                // 선행 다트(가장 낮은 dartId)만 공격 가능
                if (_holderFrontDartId.TryGetValue(slot.holderId, out int frontId) &&
                    slot.dartId != frontId)
                {
                    continue;
                }

                Vector3 slotPos = rail.GetSlotWorldPosition(slotIdx);
                Vector3 fireDir = rail.GetSlotFiringDirection(slotIdx);

                int targetId = DirectionalTargeting.FindTarget(slotPos, fireDir, slot.dartColor);
                if (targetId < 0)
                {
                    // 선행 다트가 공격 못 함 → 같은 보관함 전체 차단
                    _blockedHolders.Add(slot.holderId);
                    continue;
                }

                // Skip if another dart is already flying toward this balloon
                if (_reservedTargets.Contains(targetId)) continue;

                if (!BalloonController.HasInstance) return;
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
                    to = targetData.position
                });

                EventBus.Publish(new OnDartFired
                {
                    dartId = dartId,
                    holderId = -1,
                    color = color
                });

                // Reserve target so no other dart targets this balloon
                _reservedTargets.Add(targetId);

                // Launch projectile visual
                LaunchProjectile(slotIdx, slotPos, targetData.position, targetId, color);
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

            // Fire along pure cardinal axis (no diagonal), then snap to balloon position at impact
            // This prevents the dart from visually passing through adjacent-column balloons
            Vector3 cardinalTarget = CalculateCardinalTarget(from, to);

            // Orient along cardinal direction
            Vector3 dir = cardinalTarget - from;
            dir.y = 0f;
            if (dir.sqrMagnitude > 0.001f)
            {
                dartObj.transform.rotation = Quaternion.LookRotation(dir.normalized);
            }

            var proj = new DartProjectile
            {
                gameObject = dartObj,
                targetPosition = to,
                targetBalloonId = targetBalloonId,
                color = color,
                elapsed = 0f,
                duration = _projectileFlightTime
            };

            _activeProjectiles.Add(proj);

            // Flight: parabolic arc (곡사) or linear depending on _arcHeight
            if (_arcHeight > 0.01f)
            {
                // 3-point arc: start → apex (midpoint + Y offset) → target
                Vector3 midPoint = (from + cardinalTarget) * 0.5f;
                midPoint.y += _arcHeight;
                Vector3[] path = { from, midPoint, cardinalTarget };
                dartObj.transform.DOPath(path, _projectileFlightTime, PathType.CatmullRom)
                    .SetEase(Ease.Linear)
                    .SetLookAt(0.01f); // face movement direction
            }
            else
            {
                dartObj.transform.DOMove(cardinalTarget, _projectileFlightTime).SetEase(Ease.Linear);
            }
        }

        /// <summary>
        /// Per-dart scanning: fires darts from the per-dart system at matching balloons.
        /// </summary>
        private void ScanAndFirePerDart()
        {
            if (!RailManager.HasInstance || !BalloonController.HasInstance) return;

            RailManager rail = RailManager.Instance;
            var darts = rail.GetAllDarts();
            if (darts.Count == 0) return;

            int fired = 0;

            for (int i = 0; i < darts.Count && fired < MAX_FIRES_PER_FRAME; i++)
            {
                var dart = darts[i];
                if (dart.dartColor < 0) continue;

                // Skip if another dart from same holder with lower ID exists (sequential firing)
                bool blocked = false;
                for (int j = 0; j < darts.Count; j++)
                {
                    if (i == j) continue;
                    if (darts[j].holderId == dart.holderId && darts[j].dartId < dart.dartId && darts[j].dartColor >= 0)
                    {
                        blocked = true;
                        break;
                    }
                }
                if (blocked) continue;

                Vector3 dartPos = rail.GetPositionAtDistance(dart.progress);
                Vector3 fireDir = rail.GetDartFiringDirection(dart.dartId);

                int targetId = DirectionalTargeting.FindTarget(dartPos, fireDir, dart.dartColor);
                if (targetId < 0) continue;

                if (_reservedTargets.Contains(targetId)) continue;

                BalloonData targetData = BalloonController.Instance.GetBalloon(targetId);
                if (targetData == null || targetData.isPopped) continue;

                // Fire!
                int color = dart.dartColor;
                int dartId = dart.dartId;
                rail.RemoveDartById(dartId);

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
                    // 배치 시 축소된 baseScale 유지 (cave scale은 리셋)
                    if (visual != null)
                        dartObj.transform.localScale = visual.baseScale;
                    else
                    {
                        float sp = RailManager.Instance.SlotSpacing;
                        float maxS = sp * 0.9f;
                        Vector3 s = dartObj.transform.localScale;
                        float cur = Mathf.Max(s.x, s.z);
                        if (cur > maxS && maxS > 0.01f)
                        {
                            float r = maxS / cur;
                            dartObj.transform.localScale = new Vector3(s.x * r, s.y * r, s.z * r);
                        }
                    }

                    EventBus.Publish(new OnDartFired { dartId = dartId, holderId = -1, color = color });

                    Vector3 cardinalTarget = CalculateCardinalTarget(dartPos, targetData.position);
                    Vector3 dir = cardinalTarget - dartPos;
                    dir.y = 0f;
                    if (dir.sqrMagnitude > 0.001f)
                        dartObj.transform.rotation = Quaternion.LookRotation(dir.normalized);

                    var proj = new DartProjectile
                    {
                        gameObject = dartObj,
                        targetPosition = targetData.position,
                        targetBalloonId = targetId,
                        color = color,
                        elapsed = 0f,
                        duration = _projectileFlightTime
                    };
                    _activeProjectiles.Add(proj);

                    if (_arcHeight > 0.01f)
                    {
                        Vector3 midPoint = (dartPos + cardinalTarget) * 0.5f;
                        midPoint.y += _arcHeight;
                        Vector3[] path = { dartPos, midPoint, cardinalTarget };
                        dartObj.transform.DOPath(path, _projectileFlightTime, PathType.CatmullRom)
                            .SetEase(Ease.Linear).SetLookAt(0.01f);
                    }
                    else
                    {
                        dartObj.transform.DOMove(cardinalTarget, _projectileFlightTime).SetEase(Ease.Linear);
                    }
                }

                fired++;
                i--; // re-check same index since we removed from list
            }
        }

        #endregion

        #region Private Methods — Projectile Update

        private void UpdateProjectiles()
        {
            for (int i = _activeProjectiles.Count - 1; i >= 0; i--)
            {
                // Guard: ClearAllDarts may have emptied the list via ExecuteHit → OnBoardCleared chain
                if (i >= _activeProjectiles.Count) continue;

                DartProjectile proj = _activeProjectiles[i];
                proj.elapsed += Time.deltaTime;

                if (proj.elapsed >= proj.duration)
                {
                    _reservedTargets.Remove(proj.targetBalloonId);
                    GameObject projObj = proj.gameObject; // 참조 미리 저장
                    ExecuteHit(proj.targetBalloonId, proj.color);

                    // ExecuteHit → OnBoardCleared → ClearAllDarts로 리스트가 비워질 수 있음
                    if (_boardFinished || _activeProjectiles.Count == 0) return;

                    ReturnDartToPool(projObj);
                    if (i < _activeProjectiles.Count)
                        _activeProjectiles.RemoveAt(i);
                }
            }
        }

        private void ExecuteHit(int balloonId, int color)
        {
            // 다트로 풍선 팝 (기믹 처리 포함)
            if (BalloonController.HasInstance)
            {
                BalloonController.Instance.PopBalloonWithDart(balloonId, color);
            }

            // PopProcessor가 점수/콤보 처리 (PopBalloon 중복 호출은 isPopped 체크로 방지)
            EventBus.Publish(new OnDartHitBalloon
            {
                dartId = -1,
                balloonId = balloonId,
                color = color
            });
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
            CreateDartVisualById(evt.dartId, evt.color, evt.holderId);
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
        }

        /// <summary>
        /// Removes all rail darts and slot visuals of a specific color.
        /// Called when all targetable balloons of that color are gone (Chain gimmick surplus cleanup).
        /// </summary>
        private void RemoveDartsByColor(int color)
        {
            if (!RailManager.HasInstance) return;
            RailManager rail = RailManager.Instance;

            for (int i = 0; i < rail.SlotCount; i++)
            {
                RailManager.SlotData slot = rail.GetSlot(i);
                if (slot.dartColor == color)
                {
                    rail.ClearSlot(i);

                    // Remove corresponding visual
                    if (_slotVisuals.TryGetValue(i, out SlotDartVisual visual))
                    {
                        ReturnDartToPool(visual.gameObject);
                        _slotVisuals.Remove(i);
                    }
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
            Debug.Log("[DartManager] Continue applied — dart system resumed.");
        }

        #endregion

        #region Private Methods — Targeting Helpers

        /// <summary>
        /// Calculates the dart's flight destination along a pure cardinal axis.
        /// Instead of flying diagonally from rail to balloon (crossing other columns),
        /// the dart flies straight inward along the rail's firing direction to the balloon's depth.
        /// </summary>
        private static Vector3 CalculateCardinalTarget(Vector3 from, Vector3 balloonPos)
        {
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

        #endregion

        #region Private Methods — Pool & Visual

        private void ReturnDartToPool(GameObject obj)
        {
            if (obj != null && ObjectPoolManager.HasInstance)
            {
                ObjectPoolManager.Instance.Return(DART_POOL_KEY, obj);
            }
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
            Renderer[] renderers = obj.GetComponentsInChildren<Renderer>();
            for (int i = 0; i < renderers.Length; i++)
            {
                if (renderers[i].GetComponent<TMPro.TMP_Text>() != null) continue;
                string name = renderers[i].gameObject.name;
                if (name == "Shadow" || name.Contains("Particle")) continue;
                renderers[i].sharedMaterial = shared;
            }
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
