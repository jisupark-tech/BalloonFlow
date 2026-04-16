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
        [SerializeField] private float _arcHeight = 0f; // 0 = 직사, >0 = 곡사

        /// <summary>동적 비행 시간 (GameManager에서 실시간 참조).</summary>
        private float FlightTime => GameManager.HasInstance ? GameManager.Instance.Board.dartFlightTime : _projectileFlightTime;

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
            public Vector3 startScale;
            public Vector3 targetScale;
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
        private float _scanTimer;

        private void Update()
        {
            if (_boardFinished) return;

            UpdateSlotDartPositions();
            UpdatePerDartPositions();

            // 공격 스캔: 벨트 속도 × 공격 배율
            _scanTimer += Time.deltaTime;
            float railSpeed = RailManager.HasInstance ? RailManager.Instance.RotationSpeed : 10f;
            float atkMult = GameManager.HasInstance ? GameManager.Instance.Board.attackSpeedMultiplier : 1f;
            float interval = railSpeed > 0f ? 0.5f / (railSpeed * atkMult) : 0.05f;
            interval = Mathf.Clamp(interval, 0.005f, 0.1f);
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
                float maxScale = spacing * 1.3f; // 슬롯 간격의 90%
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
        /// 면수별 기본값 (동일하게 시작, Inspector에서 개별 조정 가능)
        private const float DEFAULT_CAVE_FADE_START = 0.0315f;
        private const float DEFAULT_CAVE_FADE_END   = 0.03f;

        // 프레임당 1회 캐시 (다트 수백 개에서 매번 프로퍼티 접근 방지)
        private float _cachedFadeStart = DEFAULT_CAVE_FADE_START;
        private float _cachedFadeEnd = DEFAULT_CAVE_FADE_END;
        private int _fadeCacheFrame = -1;

        private float CaveFadeStart { get { RefreshFadeCache(); return _cachedFadeStart; } }
        private float CaveFadeEnd { get { RefreshFadeCache(); return _cachedFadeEnd; } }

        private void RefreshFadeCache()
        {
            int frame = Time.frameCount;
            if (frame == _fadeCacheFrame) return;
            _fadeCacheFrame = frame;

            if (!GameManager.HasInstance) { _cachedFadeStart = DEFAULT_CAVE_FADE_START; _cachedFadeEnd = DEFAULT_CAVE_FADE_END; return; }
            int sides = BoardTileManager.HasInstance ? BoardTileManager.Instance.RailSideCount : 4;
            var b = GameManager.Instance.Board;
            _cachedFadeStart = sides switch { 1 => b.caveFadeStart1Side, 2 => b.caveFadeStart2Side, 3 => b.caveFadeStart3Side, _ => b.caveFadeStart4Side };
            _cachedFadeEnd = sides switch { 1 => b.caveFadeEnd1Side, 2 => b.caveFadeEnd2Side, 3 => b.caveFadeEnd3Side, _ => b.caveFadeEnd4Side };
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

                // Cave 스케일: 비순환 레일의 끝점/시작점 근처에서 축소
                if (isOpen && pathLen > 0f)
                {
                    float dist = slotIdx * rail.SlotSpacing + rail.RotationOffset;
                    float t = ((dist % pathLen) + pathLen) % pathLen / pathLen; // 0~1 정규화

                    float scale = 1f;
                    float fs = CaveFadeStart, fe = CaveFadeEnd;
                    float fadeRange = fs - fe;
                    if (t < fs)
                    {
                        // 시작점에서 나옴: FADE_END(0) → FADE_START(1)
                        scale = t <= fe ? 0f : (t - fe) / fadeRange;
                    }
                    else if (t > 1f - fs)
                    {
                        // 끝점으로 들어감: (1-FADE_START)(1) → (1-FADE_END)(0)
                        float distFromEnd = 1f - t;
                        scale = distFromEnd <= fe ? 0f : (distFromEnd - fe) / fadeRange;
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

                // 다트 경로 오프셋 — 벨트 중심 방향으로 이동
                float normT = pathLen > 0f ? dart.progress / pathLen : 0f;
                normT = ((normT % 1f) + 1f) % 1f;
                Vector3 tangent = rail.GetDirectionAtNormalized(normT);
                Vector3 inward = Vector3.Cross(tangent, Vector3.up).normalized;

                float pathOffset = GameManager.HasInstance ? GameManager.Instance.Board.dartPathOffset : 0f;
                pos += inward * pathOffset;

                visual.gameObject.transform.position = pos;

                // 다트 스케일 동적 적용
                float ds = GameManager.HasInstance ? GameManager.Instance.Board.dartScale : 1f;
                visual.gameObject.transform.localScale = Vector3.one * ds;

                // Orient — 접선의 안쪽 직각 방향 = 공격 방향
                if (tangent.sqrMagnitude > 0.001f)
                {
                    if (inward.sqrMagnitude > 0.001f)
                        visual.gameObject.transform.rotation = Quaternion.LookRotation(inward);
                }

                // Cave scale for open rails (dartScale 적용 유지)
                if (isOpen && pathLen > 0f)
                {
                    float t = dart.progress / pathLen;
                    float scale = 1f;
                    float fadeStart = CaveFadeStart;
                    float fadeEnd = CaveFadeEnd;
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
                    visual.gameObject.transform.localScale = Vector3.one * ds * scale;
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

            var proj = new DartProjectile
            {
                gameObject = dartObj,
                targetPosition = to,
                targetBalloonId = targetBalloonId,
                color = color,
                elapsed = 0f,
                duration = FlightTime,
                startScale = dartObj.transform.localScale,
                targetScale = BalloonController.HasInstance
                    ? BalloonController.Instance.GetBalloonWorldScale(targetBalloonId)
                    : dartObj.transform.localScale
            };

            _activeProjectiles.Add(proj);

            // Flight: parabolic arc (곡사) or linear depending on _arcHeight
            if (_arcHeight > 0.01f)
            {
                Vector3 midPoint = (from + to) * 0.5f;
                midPoint.y += _arcHeight;
                Vector3[] path = { from, midPoint, to };
                dartObj.transform.DOPath(path, FlightTime, PathType.CatmullRom)
                    .SetEase(Ease.Linear)
                    .SetLookAt(0.01f); // face movement direction
            }
            else
            {
                dartObj.transform.DOMove(to, FlightTime).SetEase(Ease.Linear);
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
                bool freeMode = GameManager.HasInstance && GameManager.Instance.Board.dartFreeFireMode;
                if (!freeMode)
                {
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
                }

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
                    // 레일에서 보이던 사이즈(dartScale) 그대로 발사 — cave fade는 무시하고 정상 크기로 복원
                    float ds = GameManager.HasInstance ? GameManager.Instance.Board.dartScale : 1f;
                    dartObj.transform.localScale = Vector3.one * ds;

                    EventBus.Publish(new OnDartFired { dartId = dartId, holderId = -1, color = color });

                    // 직사: 풍선 실제 월드 위치로 직접 발사
                    Vector3 targetPos = BalloonController.Instance.GetBalloonWorldPosition(targetId);
                    Vector3 dir = targetPos - dartPos;
                    dir.y = 0f;
                    if (dir.sqrMagnitude > 0.001f)
                        dartObj.transform.rotation = Quaternion.LookRotation(dir.normalized);

                    var proj = new DartProjectile
                    {
                        gameObject = dartObj,
                        targetPosition = targetPos,
                        targetBalloonId = targetId,
                        color = color,
                        elapsed = 0f,
                        duration = FlightTime,
                        startScale = dartObj.transform.localScale,
                        targetScale = BalloonController.Instance.GetBalloonWorldScale(targetId)
                    };
                    _activeProjectiles.Add(proj);

                    dartObj.transform.DOMove(targetPos, FlightTime).SetEase(Ease.Linear);
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

                if (proj.gameObject != null && proj.duration > 0f
                    && GameManager.HasInstance && GameManager.Instance.Board.dartScaleLerpToBalloon)
                {
                    float t = Mathf.Clamp01(proj.elapsed / proj.duration);
                    Vector3 desired = Vector3.Lerp(proj.startScale, proj.targetScale, t);
                    float strength = Mathf.Clamp01(GameManager.Instance.Board.dartScaleLerpStrength);
                    proj.gameObject.transform.localScale = Vector3.Lerp(proj.startScale, desired, strength);
                }

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
