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
        private readonly List<DartProjectile> _activeProjectiles = new List<DartProjectile>();

        /// <summary>
        /// Balloon IDs currently targeted by in-flight projectiles.
        /// Prevents multiple darts from firing at the same balloon.
        /// Cleared when projectile hits or balloon is popped externally.
        /// </summary>
        private readonly HashSet<int> _reservedTargets = new HashSet<int>();

        /// <summary>Max darts that can fire in a single frame to prevent visual clutter.</summary>
        private const int MAX_FIRES_PER_FRAME = 20;

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
            EventBus.Unsubscribe<OnDartFrozen>(HandleDartFrozen);
            EventBus.Unsubscribe<OnDartsFrozenCleared>(HandleDartsFrozenCleared);
            EventBus.Unsubscribe<OnBalloonPopped>(HandleBalloonPopped);
            EventBus.Unsubscribe<OnBoardCleared>(HandleBoardCleared);
            EventBus.Unsubscribe<OnBoardFailed>(HandleBoardFailed);
            EventBus.Unsubscribe<OnContinueApplied>(HandleContinueApplied);
        }

        private void Update()
        {
            if (_boardFinished) return;

            UpdateSlotDartPositions();
            ScanAndFireDarts();
            UpdateProjectiles();
        }

        #endregion

        #region Public Methods

        /// <summary>
        /// Clears all dart visuals and projectiles.
        /// </summary>
        public void ClearAllDarts()
        {
            foreach (var kvp in _slotVisuals)
            {
                ReturnDartToPool(kvp.Value.gameObject);
            }
            _slotVisuals.Clear();

            for (int i = _activeProjectiles.Count - 1; i >= 0; i--)
            {
                ReturnDartToPool(_activeProjectiles[i].gameObject);
            }
            _activeProjectiles.Clear();
            _reservedTargets.Clear();

            foreach (var kvp in _frozenVisuals)
            {
                ReturnDartToPool(kvp.Value);
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
        public void CreateSlotDartVisual(int slotIndex, int color)
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

            _slotVisuals[slotIndex] = new SlotDartVisual
            {
                slotIndex = slotIndex,
                color = color,
                gameObject = dartObj
            };
        }

        /// <summary>
        /// Returns the number of active dart visuals on slots.
        /// </summary>
        public int GetActiveSlotDartCount()
        {
            return _slotVisuals.Count;
        }

        #endregion

        #region Private Methods — Slot Dart Movement

        /// <summary>
        /// Updates all slot dart positions to follow conveyor belt movement.
        /// </summary>
        /// <summary>Reusable list for safe dictionary iteration (avoids allocation every frame).</summary>
        private readonly List<int> _tempSlotKeys = new List<int>(256);
        private readonly List<int> _tempRemoveKeys = new List<int>(32);

        private void UpdateSlotDartPositions()
        {
            if (!RailManager.HasInstance || _slotVisuals.Count == 0) return;

            RailManager rail = RailManager.Instance;

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

                Vector3 pos = rail.GetSlotWorldPosition(slotIdx);
                visual.gameObject.transform.position = pos;
                OrientDart(visual.gameObject, slotIdx);
            }

            // Deferred removal
            for (int i = 0; i < _tempRemoveKeys.Count; i++)
                _slotVisuals.Remove(_tempRemoveKeys[i]);
        }

        #endregion

        #region Private Methods — Auto-Fire Scan

        /// <summary>
        /// Scans occupied slots per frame and fires darts at matching outermost balloons.
        /// Throttled to MAX_FIRES_PER_FRAME launches per frame.
        /// Outermost filtering is handled by DirectionalTargeting.FindTarget (per-column check).
        /// </summary>
        private void ScanAndFireDarts()
        {
            if (!RailManager.HasInstance || !BalloonController.HasInstance) return;

            RailManager rail = RailManager.Instance;
            int slotCount = rail.SlotCount;
            if (slotCount == 0) return;

            int fired = 0;

            for (int s = 0; s < slotCount && fired < MAX_FIRES_PER_FRAME; s++)
            {
                int slotIdx = s;

                RailManager.SlotData slot = rail.GetSlot(slotIdx);
                if (slot.dartColor < 0) continue; // empty

                Vector3 slotPos = rail.GetSlotWorldPosition(slotIdx);
                Vector3 fireDir = rail.GetSlotFiringDirection(slotIdx);

                // Use DirectionalTargeting to find matching balloon
                int targetId = DirectionalTargeting.FindTarget(slotPos, fireDir, slot.dartColor);
                if (targetId < 0) continue;

                // Skip if another dart is already flying toward this balloon
                if (_reservedTargets.Contains(targetId)) continue;

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
                    ExecuteHit(proj.targetBalloonId, proj.color);

                    // ExecuteHit can trigger OnBoardCleared → ClearAllDarts, which empties the list
                    if (_boardFinished) return;

                    ReturnDartToPool(proj.gameObject);
                    _activeProjectiles.RemoveAt(i);
                }
            }
        }

        private void ExecuteHit(int balloonId, int color)
        {
            EventBus.Publish(new OnDartHitBalloon
            {
                dartId = -1,
                balloonId = balloonId,
                color = color
            });

            if (BalloonController.HasInstance)
            {
                BalloonController.Instance.PopBalloon(balloonId);
            }
        }

        #endregion

        #region Private Methods — Event Handlers

        private void HandleDartPlaced(OnDartPlacedOnSlot evt)
        {
            if (_boardFinished) return;
            CreateSlotDartVisual(evt.slotIndex, evt.color);
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
            Renderer[] renderers = obj.GetComponentsInChildren<Renderer>();
            for (int i = 0; i < renderers.Length; i++)
            {
                foreach (Material mat in renderers[i].materials)
                {
                    if (mat.HasProperty("_BaseColor"))
                        mat.SetColor("_BaseColor", c);
                    if (mat.HasProperty("_Color"))
                        mat.SetColor("_Color", c);
                }
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
