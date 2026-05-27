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

        // Ice HP tracking: balloonId → remaining HP
        private readonly Dictionary<int, int> _iceHP = new Dictionary<int, int>();

        // Pin tracking: balloonId → remaining segments
        private readonly Dictionary<int, int> _pinSegments = new Dictionary<int, int>();
        private readonly Dictionary<int, int> _pinColors = new Dictionary<int, int>();

        // Surprise tracking: balloonIds with hidden color (field balloon)
        private readonly HashSet<int> _surpriseBalloons = new HashSet<int>();

        // Color Curtain tracking: balloonId → required color, balloonId → counter
        private readonly Dictionary<int, int> _curtainColors = new Dictionary<int, int>();
        private readonly Dictionary<int, int> _curtainCounters = new Dictionary<int, int>();
        private const int DEFAULT_CURTAIN_COUNTER = 3;

        private readonly List<int> _iceKeysBuffer = new List<int>();
        private readonly List<int> _iceRemoveBuffer = new List<int>();
        private readonly List<int> _curtainKeysBuffer = new List<int>();
        private readonly List<int> _curtainRemoveBuffer = new List<int>();
        // ROLLBACK_ICE_GLOBAL_POP_COUNTER:
        // Ice now thaws from adjacent pops in BalloonController. Keep the old global HP counter
        // path behind this flag in case the design returns to "any pop damages all Ice".
        private static readonly bool UseGlobalIcePopCounter = false;

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
            _iceHP.Clear();
            _pinSegments.Clear();
            _pinColors.Clear();
            _surpriseBalloons.Clear();
            _curtainColors.Clear();
            _curtainCounters.Clear();
            _iceKeysBuffer.Clear();
            _iceRemoveBuffer.Clear();
            _curtainKeysBuffer.Clear();
            _curtainRemoveBuffer.Clear();
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
                    _pinColors[balloonId] = color;
                    break;

                // [ROLLBACK_PIN_BARRICADE_MERGE]
                // Barricade 가 Pin mechanic (색 매칭 + 점진 제거) 사용. _pinSegments / _pinColors 동일 등록.
                // 롤백 시 이 case 제거.
                case BalloonController.GimmickBarricade:
                    _pinSegments[balloonId] = hp > 0 ? hp : DEFAULT_PIN_LENGTH;
                    _pinColors[balloonId] = color;
                    break;

                case BalloonController.GimmickSurprise:
                    _surpriseBalloons.Add(balloonId);
                    break;

                case BalloonController.GimmickColorCurtain:
                    _curtainColors[balloonId] = color;
                    _curtainCounters[balloonId] = hp > 0 ? hp : DEFAULT_CURTAIN_COUNTER;
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
                    if (_pinColors.TryGetValue(balloonId, out int pinColor) && dartColor != pinColor)
                        return $"Pin: requires color {pinColor}";
                    if (_pinSegments.TryGetValue(balloonId, out int segments) && segments > 0)
                    {
                        // Check if dart color matches — handled by ProcessPinHit
                        return null; // Allow the hit, ProcessPinHit will handle logic
                    }
                    return null;

                // [ROLLBACK_PIN_BARRICADE_MERGE]
                // Barricade 가 Pin mechanic (색 매칭) 사용. 다른 색은 blocker.
                case BalloonController.GimmickBarricade:
                    if (_pinColors.TryGetValue(balloonId, out int bColor) && dartColor != bColor)
                        return $"Barricade: requires color {bColor}";
                    return null;

                case BalloonController.GimmickColorCurtain:
                    return "ColorCurtain: indirect removal only";

                case BalloonController.GimmickFlexTube:
                    // FlexTube: same-color dart direct hit only. 다른 색은 blocker — 다트가 hit 하지 않음.
                    // 같은 색일 때 PopBalloonWithDart 가 FlexTube.OnDartHit 로 위임 (ZapAttack 트리거 + Segment 비활성).
                    if (BalloonController.HasInstance)
                    {
                        var data = BalloonController.Instance.GetBalloon(balloonId);
                        if (data != null && dartColor >= 0 && dartColor != data.color)
                            return $"FlexTube: requires color {data.color}";
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

        public int GetPinRemainingSegments(int balloonId)
        {
            return _pinSegments.TryGetValue(balloonId, out int seg) ? seg : 0;
        }

        /// <summary>
        /// Reveals a Surprise balloon's color when an adjacent balloon pops.
        /// Returns true if the surprise was revealed.
        /// </summary>
        public bool RevealSurprise(int balloonId)
        {
            if (!_surpriseBalloons.Contains(balloonId)) return false;
            _surpriseBalloons.Remove(balloonId);

            bool revealed = BalloonController.HasInstance
                && BalloonController.Instance.RevealHiddenBalloon(balloonId);
            if (revealed)
                Debug.Log($"[GimmickProcessor] Surprise {balloonId} revealed.");
            return revealed;
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
            if (UseGlobalIcePopCounter)
            {
                // === Ice: every pop reduces all Ice balloon HP by 1 ===
                _iceKeysBuffer.Clear();
                _iceRemoveBuffer.Clear();
                foreach (var kvp in _iceHP)
                    _iceKeysBuffer.Add(kvp.Key);

                for (int i = 0; i < _iceKeysBuffer.Count; i++)
                {
                    int id = _iceKeysBuffer[i];
                    if (!_iceHP.TryGetValue(id, out int hp)) continue;

                    int newHP = hp - 1;
                    _iceHP[id] = newHP;

                    if (newHP <= 0)
                    {
                        _iceRemoveBuffer.Add(id);

                        EventBus.Publish(new OnGimmickTriggered
                        {
                            gimmickType = BalloonController.GimmickIce,
                            targetId = id
                        });
                    }
                }

                // Remove destroyed Ice balloons from tracking before ForcePop can publish recursively.
                for (int i = 0; i < _iceRemoveBuffer.Count; i++)
                {
                    int id = _iceRemoveBuffer[i];
                    _iceHP.Remove(id);

                    // Signal BalloonController to pop this Ice balloon
                    if (BalloonController.HasInstance)
                    {
                        BalloonController.Instance.ForcePopBalloon(id);
                    }
                }
            }

            // === Color Curtain: 해당 색 풍선 팝 시 카운터 -1 ===
            _curtainKeysBuffer.Clear();
            _curtainRemoveBuffer.Clear();
            foreach (var kvp in _curtainCounters)
                _curtainKeysBuffer.Add(kvp.Key);

            for (int i = 0; i < _curtainKeysBuffer.Count; i++)
            {
                // 팝된 풍선의 색상이 커튼의 요구 색상과 일치해야 카운터 감소
                int id = _curtainKeysBuffer[i];
                if (!_curtainCounters.TryGetValue(id, out int counter)) continue;

                if (_curtainColors.TryGetValue(id, out int requiredColor) && evt.color == requiredColor)
                {
                    int newCounter = counter - 1;
                    _curtainCounters[id] = newCounter;

                    if (newCounter <= 0)
                    {
                        _curtainRemoveBuffer.Add(id);
                        EventBus.Publish(new OnGimmickTriggered
                        {
                            gimmickType = BalloonController.GimmickColorCurtain,
                            targetId = id
                        });
                    }
                }
            }

            for (int i = 0; i < _curtainRemoveBuffer.Count; i++)
            {
                int id = _curtainRemoveBuffer[i];
                _curtainCounters.Remove(id);
                _curtainColors.Remove(id);
                if (BalloonController.HasInstance)
                    BalloonController.Instance.ForcePopBalloon(id);
            }

            // === Surprise / Hidden: reveal adjacent concealed balloons ===
            if (BalloonController.HasInstance)
            {
                var adjacentIds = BalloonController.Instance.GetAdjacentBalloonIdsForBalloonPublic(evt.balloonId, evt.position);
                // [디버그] reveal 흐름 추적 — adjacent 개수 + 각 id 의 concealed 여부.
                int revealedCount = 0;
                int concealedCount = 0;
                for (int i = 0; i < adjacentIds.Count; i++)
                {
                    int adjId = adjacentIds[i];
                    bool wasConcealed = BalloonController.Instance.IsBalloonConcealed(adjId);
                    if (wasConcealed) concealedCount++;
                    if (_surpriseBalloons.Contains(adjId))
                    {
                        RevealSurprise(adjId);
                    }
                    _surpriseBalloons.Remove(adjId);
                    if (BalloonController.Instance.RevealHiddenBalloon(adjId))
                        revealedCount++;
                }
                Debug.Log($"[Reveal] popped={evt.balloonId} adjacent={adjacentIds.Count} concealed={concealedCount} revealed={revealedCount}");
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
