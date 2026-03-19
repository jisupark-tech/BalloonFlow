using System.Collections.Generic;
using UnityEngine;

namespace BalloonFlow
{
    /// <summary>
    /// Centralized gimmick behavior processor.
    /// Design ref: BalloonFlow_기믹명세 (2026-03-17) — 13종 기믹
    ///
    /// Gimmick domains:
    ///   FIELD gimmicks  (on balloons): Piñata, Pin, Lock_Key, Surprise(Lv.101), Wall, Piñata_Box, Ice, Color_Curtain
    ///   QUEUE gimmicks  (on holders):  Hidden(Lv.11), Chain(Lv.21), Spawner_T(Lv.41), Spawner_O(Lv.141), Frozen_Dart(Lv.241)
    /// </summary>
    /// <remarks>
    /// Layer: Domain | Genre: Puzzle | Role: Processor | Phase: 1
    /// </remarks>
    public class GimmickProcessor : SceneSingleton<GimmickProcessor>
    {
        #region Constants

        // Piñata default HP (overridden by level data)
        private const int DEFAULT_PINATA_HP = 2;

        // Ice HP — reduced by ANY balloon pop (indirect)
        private const int DEFAULT_ICE_HP = 3;

        // Pin progressive removal — same-color dart direct hit removes 1 segment
        private const int DEFAULT_PIN_LENGTH = 3;

        #endregion

        #region Fields

        // Lock-Key tracking: keyColor → list of locked balloonIds
        private readonly Dictionary<int, List<int>> _lockTargets = new Dictionary<int, List<int>>();
        private readonly HashSet<int> _unlockedBalloons = new HashSet<int>();

        // Ice HP tracking: balloonId → remaining HP
        private readonly Dictionary<int, int> _iceHP = new Dictionary<int, int>();

        // Pin tracking: balloonId → remaining segments
        private readonly Dictionary<int, int> _pinSegments = new Dictionary<int, int>();

        // Surprise tracking: balloonIds with hidden color (field balloon)
        private readonly HashSet<int> _surpriseBalloons = new HashSet<int>();

        // Color Curtain tracking: balloonId → required color
        private readonly Dictionary<int, int> _curtainColors = new Dictionary<int, int>();

        #endregion

        #region Lifecycle

        protected override void OnSingletonAwake()
        {
            ResetAll();
        }

        private void OnEnable()
        {
            EventBus.Subscribe<OnBalloonPopped>(HandleAnyBalloonPopped);
        }

        private void OnDisable()
        {
            EventBus.Unsubscribe<OnBalloonPopped>(HandleAnyBalloonPopped);
        }

        #endregion

        #region Public Methods — Initialization

        public void ResetAll()
        {
            _lockTargets.Clear();
            _unlockedBalloons.Clear();
            _iceHP.Clear();
            _pinSegments.Clear();
            _surpriseBalloons.Clear();
            _curtainColors.Clear();
        }

        /// <summary>
        /// Registers a balloon's gimmick state during level setup.
        /// Call for each balloon with a gimmick type after BalloonController.SetupBalloons().
        /// </summary>
        public void RegisterBalloonGimmick(int balloonId, string gimmickType, int color, int hp = 0)
        {
            switch (gimmickType)
            {
                case BalloonController.GimmickIce:
                    _iceHP[balloonId] = hp > 0 ? hp : DEFAULT_ICE_HP;
                    break;

                case BalloonController.GimmickPin:
                    _pinSegments[balloonId] = hp > 0 ? hp : DEFAULT_PIN_LENGTH;
                    break;

                case BalloonController.GimmickSurprise:
                    _surpriseBalloons.Add(balloonId);
                    break;

                case BalloonController.GimmickColorCurtain:
                    _curtainColors[balloonId] = color;
                    break;

                case BalloonController.GimmickLockKey:
                    // 'color' here is the key color that unlocks this lock
                    if (!_lockTargets.ContainsKey(color))
                        _lockTargets[color] = new List<int>();
                    _lockTargets[color].Add(balloonId);
                    break;
            }
        }

        #endregion

        #region Public Methods — Field Gimmick Pre-Pop Guards

        /// <summary>
        /// Checks if a dart can hit this balloon. Returns null if allowed,
        /// or a reason string if blocked.
        /// </summary>
        public string CheckDartBlocker(int balloonId, string gimmickType, int dartColor)
        {
            switch (gimmickType)
            {
                case BalloonController.GimmickWall:
                    return "Wall: indestructible";

                case BalloonController.GimmickIce:
                    // Ice is indirect-only — darts cannot target directly
                    return "Ice: indirect removal only (any pop reduces HP)";

                case BalloonController.GimmickPin:
                    // Pin requires same-color dart direct hit for progressive removal
                    if (_pinSegments.TryGetValue(balloonId, out int segments) && segments > 0)
                    {
                        // Check if dart color matches — handled by ProcessPinHit
                        return null; // Allow the hit, ProcessPinHit will handle logic
                    }
                    return null;

                case BalloonController.GimmickLockKey:
                    // Lock is blocked until its key color has been popped
                    if (!_unlockedBalloons.Contains(balloonId))
                        return "Lock: key not yet destroyed";
                    return null;

                case BalloonController.GimmickColorCurtain:
                    // Only removable by specific color dart
                    if (_curtainColors.TryGetValue(balloonId, out int reqColor))
                    {
                        if (dartColor != reqColor)
                            return $"ColorCurtain: requires color {reqColor}";
                    }
                    return null;

                default:
                    return null; // No block
            }
        }

        #endregion

        #region Public Methods — Field Gimmick Hit Processing

        /// <summary>
        /// Processes a Pin hit. Returns true if the pin segment was removed.
        /// When all segments are removed, the Pin is destroyed (caller should ExecutePop).
        /// </summary>
        public bool ProcessPinHit(int balloonId, int dartColor, int balloonColor)
        {
            if (dartColor != balloonColor)
            {
                Debug.Log($"[GimmickProcessor] Pin {balloonId}: dart color {dartColor} != pin color {balloonColor}. No effect.");
                return false;
            }

            if (!_pinSegments.TryGetValue(balloonId, out int remaining))
                return false;

            remaining--;
            _pinSegments[balloonId] = remaining;

            EventBus.Publish(new OnGimmickTriggered
            {
                gimmickType = BalloonController.GimmickPin,
                targetId = balloonId
            });

            Debug.Log($"[GimmickProcessor] Pin {balloonId}: segment removed. Remaining={remaining}");
            return remaining <= 0; // true = fully destroyed
        }

        /// <summary>
        /// Checks if a pin is fully destroyed (all segments removed).
        /// </summary>
        public bool IsPinDestroyed(int balloonId)
        {
            return _pinSegments.TryGetValue(balloonId, out int seg) && seg <= 0;
        }

        /// <summary>
        /// Reveals a Surprise balloon's color when an adjacent balloon pops.
        /// Returns true if the surprise was revealed.
        /// </summary>
        public bool RevealSurprise(int balloonId)
        {
            if (!_surpriseBalloons.Contains(balloonId)) return false;
            _surpriseBalloons.Remove(balloonId);

            EventBus.Publish(new OnGimmickTriggered
            {
                gimmickType = BalloonController.GimmickSurprise,
                targetId = balloonId
            });

            Debug.Log($"[GimmickProcessor] Surprise {balloonId} revealed.");
            return true;
        }

        /// <summary>
        /// Returns true if the balloon is a still-hidden Surprise balloon.
        /// </summary>
        public bool IsSurpriseHidden(int balloonId)
        {
            return _surpriseBalloons.Contains(balloonId);
        }

        /// <summary>
        /// Returns the remaining Ice HP for a balloon, or 0 if not tracked/already removed.
        /// </summary>
        public int GetIceHP(int balloonId)
        {
            return _iceHP.TryGetValue(balloonId, out int hp) ? hp : 0;
        }

        #endregion

        #region Private Methods — Global Pop Handler (Ice indirect, Lock-Key)

        /// <summary>
        /// Handles ANY balloon pop — used for indirect gimmick effects:
        /// - Ice: ALL pops reduce Ice HP by 1
        /// - Lock-Key: popping a Key color unlocks corresponding Locks
        /// - Surprise: adjacent pop reveals hidden color
        /// </summary>
        private void HandleAnyBalloonPopped(OnBalloonPopped evt)
        {
            // === Ice: every pop reduces all Ice balloon HP by 1 ===
            var iceToRemove = new List<int>();
            foreach (var kvp in _iceHP)
            {
                int newHP = kvp.Value - 1;
                _iceHP[kvp.Key] = newHP;

                if (newHP <= 0)
                {
                    iceToRemove.Add(kvp.Key);

                    EventBus.Publish(new OnGimmickTriggered
                    {
                        gimmickType = BalloonController.GimmickIce,
                        targetId = kvp.Key
                    });
                }
            }

            // Remove destroyed Ice balloons from tracking
            foreach (int id in iceToRemove)
            {
                _iceHP.Remove(id);

                // Signal BalloonController to pop this Ice balloon
                if (BalloonController.HasInstance)
                {
                    BalloonController.Instance.ForcePopBalloon(id);
                }
            }

            // === Lock-Key: popping any balloon with this color unlocks Locks ===
            if (_lockTargets.TryGetValue(evt.color, out List<int> lockedIds))
            {
                foreach (int lockedId in lockedIds)
                {
                    _unlockedBalloons.Add(lockedId);

                    EventBus.Publish(new OnGimmickTriggered
                    {
                        gimmickType = BalloonController.GimmickLockKey,
                        targetId = lockedId
                    });

                    Debug.Log($"[GimmickProcessor] Lock {lockedId} unlocked by color {evt.color}.");
                }
                _lockTargets.Remove(evt.color);
            }

            // === Surprise: reveal adjacent Surprise balloons ===
            if (BalloonController.HasInstance)
            {
                var adjacentIds = BalloonController.Instance.GetAdjacentBalloonIdsPublic(evt.position);
                foreach (int adjId in adjacentIds)
                {
                    if (_surpriseBalloons.Contains(adjId))
                    {
                        RevealSurprise(adjId);
                    }
                }

                // === Hidden: reveal adjacent Hidden balloons ===
                foreach (int adjId in adjacentIds)
                {
                    BalloonController.Instance.RevealHiddenBalloon(adjId);
                }
            }
        }

        #endregion

        #region Queue Gimmick Methods (delegated from HolderManager)

        /// <summary>
        /// Processes Hidden holder reveal. Called when a holder becomes touchable (deploying position).
        /// Returns the actual color of the holder.
        /// </summary>
        public int RevealHiddenHolder(int holderId, int actualColor)
        {
            EventBus.Publish(new OnGimmickTriggered
            {
                gimmickType = BalloonController.GimmickHidden,
                targetId = holderId
            });
            Debug.Log($"[GimmickProcessor] Hidden holder {holderId} revealed: color={actualColor}");
            return actualColor;
        }

        /// <summary>
        /// Gets chain-linked holder IDs. Chain gimmick links 2-4 holders for sequential deployment.
        /// </summary>
        public void ProcessChainDeploy(int leadHolderId, int[] linkedHolderIds)
        {
            EventBus.Publish(new OnGimmickTriggered
            {
                gimmickType = BalloonController.GimmickChain,
                targetId = leadHolderId
            });
            Debug.Log($"[GimmickProcessor] Chain deploy from holder {leadHolderId}, linked: {linkedHolderIds.Length}");
        }

        /// <summary>
        /// Processes Spawner trigger. When a Spawner holder is fully consumed,
        /// it creates a new holder in the queue.
        /// </summary>
        public void ProcessSpawnerConsumed(int holderId, string spawnerType)
        {
            // Signal HolderManager to create new holder in queue
            EventBus.Publish(new OnGimmickTriggered
            {
                gimmickType = spawnerType,
                targetId = holderId
            });
            Debug.Log($"[GimmickProcessor] {spawnerType} holder {holderId} consumed — new holder spawned in queue.");
        }

        #endregion
    }
}
