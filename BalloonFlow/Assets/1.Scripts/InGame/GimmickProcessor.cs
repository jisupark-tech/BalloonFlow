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

        // Pin progressive removal — same-color dart direct hit removes 1 segment
        private const int DEFAULT_PIN_LENGTH = 3;

        #endregion

        #region Fields

        // Pin tracking: balloonId → remaining segments
        private readonly Dictionary<int, int> _pinSegments = new Dictionary<int, int>();
        private readonly Dictionary<int, int> _pinColors = new Dictionary<int, int>();

        // Surprise tracking: balloonIds with hidden color (field balloon)
        private readonly HashSet<int> _surpriseBalloons = new HashSet<int>();
        // ROLLBACK_SURPRISE_ORPHAN_REVEAL_20260616: orphan(팝가능 이웃 0) 공개 처리용 임시 버퍼.
        private readonly List<int> _surpriseOrphanBuffer = new List<int>();

        // Color Curtain tracking: balloonId → required color, balloonId → counter
        private readonly Dictionary<int, int> _curtainColors = new Dictionary<int, int>();
        private readonly Dictionary<int, int> _curtainCounters = new Dictionary<int, int>();
        private const int DEFAULT_CURTAIN_COUNTER = 3;

        private readonly List<int> _curtainKeysBuffer = new List<int>();
        private readonly List<int> _curtainRemoveBuffer = new List<int>();

        // Ice (Lv.201, 기믹명세 §11): 영역(region) 공유 HP 모델.
        //  - 인접 연결된 ice 셀들이 하나의 영역을 이룸(런타임 flood-fill, InitIceRegions). 레벨당 여러 영역 가능.
        //  - 필드에서 "어떤 풍선이든" 제거될 때마다 활성 영역들의 HP 각각 -1.
        //  - 영역 HP 0 → 그 영역 얼음만 동시 해제(BalloonController.BreakIceRegion) → 아래 가려진 풍선 노출/타격 가능.
        //  - 영역 HP 는 레벨 데이터(ice 풍선 maxHP 중 최댓값) 에서 소싱. 미지정(0) 이면 영역 셀 수로 fallback.
        private sealed class IceRegion
        {
            public readonly HashSet<int> ids = new HashSet<int>();
            public int hp;
            public int maxHp;
            public int manualGroupId;
            // ROLLBACK_ICE_OVERRIDE_NO_GROUP_20260706: 중앙 HP 라벨 표시 여부 — 수동 그룹 또는 override(그룹 id 무관) 일 때 표시.
            public bool showHpLabel;
        }
        private readonly HashSet<int> _iceBalloons = new HashSet<int>();   // 아직 얼어있는(미해제) ice 풍선 전체
        private readonly Dictionary<int, int> _iceBalloonHp = new Dictionary<int, int>(); // 등록 시 캡처한 셀별 maxHP
        // ROLLBACK_ICE_MANUAL_GROUP_20260608:
        // Optional MapMaker-authored Ice group metadata. Missing/0 group ids keep legacy adjacency grouping.
        private readonly Dictionary<int, int> _iceBalloonGroupId = new Dictionary<int, int>();
        private readonly Dictionary<int, int> _iceBalloonGroupHp = new Dictionary<int, int>();
        private readonly Dictionary<int, int> _iceBalloonGroupHpMode = new Dictionary<int, int>();
        private readonly List<IceRegion> _iceRegions = new List<IceRegion>();

        #endregion

        #region Lifecycle

        protected override void OnSingletonAwake()
        {
            ResetAll();
        }

        private void OnEnable()
        {
            EventBus.Subscribe<OnBalloonPopped>(HandleAnyBalloonPopped);
            Debug.Log("[GimmickProcessor] OnEnable — subscribed to OnBalloonPopped");
        }

        private void OnDisable()
        {
            EventBus.Unsubscribe<OnBalloonPopped>(HandleAnyBalloonPopped);
        }

        #endregion

        #region Public Methods — Initialization

        public void ResetAll()
        {
            _pinSegments.Clear();
            _pinColors.Clear();
            _surpriseBalloons.Clear();
            _curtainColors.Clear();
            _curtainCounters.Clear();
            _curtainKeysBuffer.Clear();
            _curtainRemoveBuffer.Clear();
            _iceBalloons.Clear();
            _iceBalloonHp.Clear();
            _iceBalloonGroupId.Clear();
            _iceBalloonGroupHp.Clear();
            _iceBalloonGroupHpMode.Clear();
            ClearIceHpLabels();
            _iceRegions.Clear();
        }

        /// <summary>
        /// Registers a balloon's gimmick state during level setup.
        /// Call for each balloon with a gimmick type after BalloonController.SetupBalloons().
        /// </summary>
        public void RegisterBalloonGimmick(int balloonId, string gimmickType, int color, int hp = 0,
            int iceGroupId = 0, int iceGroupHp = 0, int iceGroupHpMode = 0, int iceOverlay = 0)
        {
            gimmickType = GimmickDisplayName.Normalize(gimmickType);
            // ROLLBACK_ICE_OVERLAY_LAYER_20260702: 베이스 기믹 위에 얼음 오버레이가 있으면 ice 추적도 등록(베이스 등록과 공존).
            //   per-cell ice HP=1 (영역 HP = 셀 수 fallback 또는 iceGroupHp 오버라이드 — 기존 Ice 와 동일 방식).
            //   베이스 기믹 case 는 아래 switch 에서 그대로 등록되어, 얼음 깨진 뒤 정상 동작.
            if (iceOverlay > 0 && gimmickType != BalloonController.GimmickIce)
            {
                _iceBalloons.Add(balloonId);
                _iceBalloonHp[balloonId] = 1;
                _iceBalloonGroupId[balloonId] = Mathf.Max(0, iceGroupId);
                _iceBalloonGroupHp[balloonId] = Mathf.Max(0, iceGroupHp);
                _iceBalloonGroupHpMode[balloonId] = iceGroupHpMode;
            }
            switch (gimmickType)
            {
                // Ice (기믹명세 §11): 영역 공유 HP. 각 ice 풍선과 셀별 maxHP 를 캡처만 해둔다.
                // 인접 연결 영역으로의 그룹핑 + 영역 HP 확정은 셋업 완료 후 InitIceRegions 에서 수행.
                case BalloonController.GimmickIce:
                    _iceBalloons.Add(balloonId);
                    _iceBalloonHp[balloonId] = hp;
                    _iceBalloonGroupId[balloonId] = Mathf.Max(0, iceGroupId);
                    _iceBalloonGroupHp[balloonId] = Mathf.Max(0, iceGroupHp);
                    _iceBalloonGroupHpMode[balloonId] = iceGroupHpMode;
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

        /// <summary>
        /// [#13/§11] 셋업 완료 후 호출 — 등록된 ice 풍선들을 인접 연결 영역(region)으로 묶고 각 영역의 공유 HP 를 확정.
        /// 영역 HP = 그 영역 셀들의 maxHP 중 최댓값(같은 영역이면 동일값 가정). 모두 0 이면 영역 셀 수로 fallback.
        /// BalloonController.SetupBalloons 의 ApplyInitialIceState 직후 1회 호출.
        /// </summary>
        public void InitIceRegions()
        {
            ClearIceHpLabels();
            _iceRegions.Clear();
            if (_iceBalloons.Count == 0 || !BalloonController.HasInstance) return;

            var components = BalloonController.Instance.GetIceRegions();
            for (int c = 0; c < components.Count; c++)
            {
                var comp = components[c];
                if (comp == null || comp.Count == 0) continue;

                var region = new IceRegion();
                int maxHp = 0;
                int sumHp = 0;
                int manualGroupId = 0;
                // ROLLBACK_ICE_OVERRIDE_NO_GROUP_20260706: override(HP 모드=2) 를 '수동 그룹 id' 유무와 무관하게 적용.
                //   기존엔 group id>0 셀만 override 를 읽어(197), 자동 그룹(id=0)으로 배치한 ice 는 override 가 무시되고
                //   adjacency 기본(maxHp/셀수)으로 떨어졌다 → "Override 안 됨". 모드/override HP 를 그룹 id 무관하게 캡처한다.
                //   (Sum/legacy 는 기존 그대로 — 무회귀. 한 region 셀들은 같이 칠해져 동일 모드/값 가정.)
                //   롤백: 아래 캡처 2줄 + region.hp 분기를, group id>0 안에서만 override/sum 하던 이전 방식으로 복원.
                int regionHpMode = 0;
                int regionOverrideHp = 0;
                for (int i = 0; i < comp.Count; i++)
                {
                    int id = comp[i];
                    region.ids.Add(id);
                    int h = _iceBalloonHp.TryGetValue(id, out int hp) ? Mathf.Max(0, hp) : 0;
                    if (h > maxHp) maxHp = h;
                    sumHp += h;
                    if (regionHpMode == 0 && _iceBalloonGroupHpMode.TryGetValue(id, out int m2) && m2 > 0)
                    {
                        regionHpMode = m2;
                        regionOverrideHp = _iceBalloonGroupHp.TryGetValue(id, out int oh) ? Mathf.Max(0, oh) : 0;
                    }
                    if (manualGroupId <= 0 && _iceBalloonGroupId.TryGetValue(id, out int gid) && gid > 0)
                        manualGroupId = gid;
                }
                region.manualGroupId = manualGroupId;
                bool useOverride = regionHpMode == 2 && regionOverrideHp > 0;
                region.showHpLabel = manualGroupId > 0 || useOverride; // ROLLBACK_ICE_OVERRIDE_NO_GROUP_20260706
                if (useOverride)
                    region.hp = regionOverrideHp;                                  // override — group id 무관
                else if (manualGroupId > 0)
                    region.hp = sumHp > 0 ? sumHp : region.ids.Count;              // sum (수동 그룹, 기존)
                else
                    region.hp = maxHp > 0 ? maxHp : region.ids.Count;              // legacy adjacency (기존)
                region.maxHp = region.hp;

                // ROLLBACK_ICE_OVERRIDE_AUDIT_20260708: Override "HP 안 먹힘/1로 폭발" 원인 확정용 1회성 덤프.
                //   판정법: useOverride=False → 데이터가 override(mode=2,hp>0)로 안 들어옴(저작/커밋 문제, 예: HP 필드
                //   onEndEdit 미커밋으로 hp=0 저장). useOverride=True 인데 finalHp<preClampHp → 언위너블 클램프(보드에
                //   얼음막 외 팝가능 풍선이 preClampHp 보다 적음). 원인 확정 후 이 블록 제거.
                int _auditPreClampHp = region.hp;
                int _auditRemaining = BalloonController.HasInstance ? BalloonController.Instance.RemainingCount : -1;
                Debug.Log($"[Ice-AUDIT] cells={region.ids.Count} groupId={manualGroupId} mode={regionHpMode}(2=override) " +
                          $"overrideHp={regionOverrideHp} useOverride={useOverride} sumHp={sumHp} maxHp={maxHp} " +
                          $"preClampHp={_auditPreClampHp} Remaining={_auditRemaining} maxDecrements={_auditRemaining - region.ids.Count}");

                // ROLLBACK_ICE_HP_WINNABILITY_CLAMP_20260616: region HP 가 받을 수 있는 최대 감소량을 초과하면
                //   영영 thaw 안 돼 ice 셀이 RemainingCount 에 영구 잔존 → 보드 언위너블. HP 를 그 상한으로 클램프.
                //   [2026-06-16 cascade-fix] 이 region 의 최대 감소 = '자기 셀 제외 전체 팝가능' = RemainingCount - region 셀수.
                //   (다른 region 의 ice 셀도 thaw 후 팝되어 이 region 을 감소시키므로 단순 non-ice 수가 아님 — 멀티 region
                //    cascade 레벨 over-clamp 방지.) 정상 데이터(HP ≤ 상한)는 불변. 롤백: 아래 클램프 블록 제거.
                if (BalloonController.HasInstance)
                {
                    int maxDecrements = BalloonController.Instance.RemainingCount - region.ids.Count;
                    if (maxDecrements >= 0 && region.hp > maxDecrements)
                    {
                        Debug.LogWarning($"[GimmickProcessor] Ice region HP({region.hp}) > 최대 감소가능({maxDecrements}) — 언위너블 방지 클램프. 레벨 데이터 검토 권장.");
                        region.hp = maxDecrements;
                        region.maxHp = region.hp;
                    }
                }

                CreateOrUpdateIceHpLabel(region);
                _iceRegions.Add(region);
            }
            Debug.Log($"[GimmickProcessor] Ice 영역 {_iceRegions.Count}개 초기화 (총 {_iceBalloons.Count} 셀)");
        }

        // ROLLBACK_CURTAIN_WINNABILITY_CLAMP_20260616: 셋업 직후 1회 호출(InitIceRegions 와 동일 시점).
        //   커튼 counter 가 '요구색 팝 가능 풍선 수'를 초과하면 그 색을 다 팝해도 counter 가 0 에 못 닿아
        //   커튼이 RemainingCount 에 영구 잔존 → 언위너블. counter 를 요구색 팝가능 수로 클램프(정상 데이터는 불변).
        //   롤백: 이 메서드 + BalloonController 의 호출부 제거.
        public void ClampCurtainCounters()
        {
            if (_curtainCounters.Count == 0 || !BalloonController.HasInstance) return;
            _curtainKeysBuffer.Clear();
            foreach (var kvp in _curtainCounters) _curtainKeysBuffer.Add(kvp.Key);
            for (int i = 0; i < _curtainKeysBuffer.Count; i++)
            {
                int id = _curtainKeysBuffer[i];
                if (!_curtainColors.TryGetValue(id, out int reqColor)) continue;
                // [2026-06-16 fix] GetBalloonsByColor 는 hidden/concealed 를 제외 → curtain+같은색 Surprise/Hidden 레벨에서
                //   under-count 로 winnable 레벨을 변경. concealed 풍선도 공개+팝되면 커튼을 감소시키므로 hidden 포함 카운트 사용.
                var sameColor = BalloonController.Instance.GetActiveBalloonsByColor(reqColor);
                int avail = sameColor != null ? sameColor.Count : 0;
                if (_curtainCounters.TryGetValue(id, out int c) && c > avail)
                {
                    Debug.LogWarning($"[GimmickProcessor] Curtain counter({c}) > 요구색({reqColor}) 팝가능(hidden포함 {avail}) — 언위너블 방지 클램프. 레벨 데이터 검토 권장.");
                    _curtainCounters[id] = avail;
                }
            }
        }

        private void ClearIceHpLabels()
        {
            if (!BalloonController.HasInstance) return;
            for (int i = 0; i < _iceRegions.Count; i++)
            {
                if (_iceRegions[i] != null)
                    BalloonController.Instance.ClearIceRegionHpText(_iceRegions[i].ids);
            }
        }

        private void CreateOrUpdateIceHpLabel(IceRegion region)
        {
            if (region == null || region.ids.Count == 0 || !BalloonController.HasInstance) return;
            // ROLLBACK_ICE_MANUAL_GROUP_20260608:
            // Only explicit MapMaker Ice groups get the shared center HP label. Legacy auto-adjacent
            // Ice regions keep their previous visual behavior.
            // ROLLBACK_ICE_OVERRIDE_NO_GROUP_20260706: override(그룹 id 무관) 도 HP 라벨 표시 — showHpLabel 사용.
            //   롤백: 아래 조건을  if (region.manualGroupId <= 0) return;  으로 복원.
            if (!region.showHpLabel) return;

            // ROLLBACK_ICE_MAGAZINE_TEXT_20260608:
            // Do not create a standalone TextMesh. Activate one MagazineText from FrozenLayer.prefab
            // and place it at the grouped Ice center instead.
            BalloonController.Instance.SetIceRegionHpText(region.ids, Mathf.Max(0, region.hp));
        }

        #endregion

        #region Public Methods — Field Gimmick Pre-Pop Guards

        /// <summary>
        /// Checks if a dart can hit this balloon. Returns null if allowed,
        /// or a reason string if blocked.
        /// </summary>
        public string CheckDartBlocker(int balloonId, string gimmickType, int dartColor)
        {
            gimmickType = GimmickDisplayName.Normalize(gimmickType);
            // ROLLBACK_ICE_OVERLAY_LAYER_20260702: 베이스 기믹 위에 얼음 오버레이가 씌워진 셀도 직접 타격 차단(깨질 때까지).
            //   (gimmickType 은 베이스 타입이라 switch 로 안 잡히므로 여기서 balloon.iceOverlay 확인.)
            if (BalloonController.HasInstance)
            {
                var bd = BalloonController.Instance.GetBalloon(balloonId);
                if (bd != null && bd.iceOverlay > 0)
                    return "Ice overlay: indirect removal only (region HP)";
            }
            switch (gimmickType)
            {
                case BalloonController.GimmickWall:
                    return "Wall: indestructible";

                case BalloonController.GimmickIce:
                    // Ice is indirect-only — darts cannot target directly. 영역 공유 HP 0 도달 시 일괄 해제(§11).
                    return "Ice: indirect removal only (region HP)";

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

            // 마지막 segment 제거 시 isDestroyed=true → woodbreak SFX, 그 이전엔 풍선 pop SFX.
            EventBus.Publish(new OnGimmickTriggered
            {
                gimmickType  = BalloonController.GimmickPin,
                targetId     = balloonId,
                isDestroyed  = remaining <= 0
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

        #endregion

        #region Private Methods — Global Pop Handler (Color Curtain, Surprise)

        /// <summary>
        /// Handles ANY balloon pop — used for indirect gimmick effects:
        /// - Ice (§11): 어떤 풍선이든 제거 시 영역 공유 HP -1, HP 0 → 영역 전체 동시 해제
        /// - Color Curtain: matching-color pop decrements counter; at 0 the curtain is removed
        /// - Surprise: adjacent pop reveals hidden color
        /// </summary>
        // 풍선 팝마다 2개씩 찍히는 진단 로그 게이트 — LogStringToConsole 비용이 커서 평소 false 유지.
        private static readonly bool POP_DEBUG = false;

        private void HandleAnyBalloonPopped(OnBalloonPopped evt)
        {
            if (POP_DEBUG)
                Debug.Log($"[HandleAnyPop] 진입 balloon={evt.balloonId} color={evt.color} surpriseSet={_surpriseBalloons.Count}");

            // === Ice (§11): 영역별 공유 HP — 어떤 풍선이든 제거되면 활성 영역 HP 각각 -1 ===
            // 아직 얼어있는 ice 풍선의 팝은 제외(해제 전엔 팝 불가, 해제 후엔 _iceBalloons 에서 빠짐).
            // 영역 HP 0 → 그 영역 얼음만 동시 해제 → 아래 가려진 풍선 노출.
            if (_iceRegions.Count > 0 && !_iceBalloons.Contains(evt.balloonId))
            {
                for (int i = _iceRegions.Count - 1; i >= 0; i--)
                {
                    var region = _iceRegions[i];
                    region.hp--;
                    if (region.hp <= 0)
                    {
                        if (BalloonController.HasInstance)
                        {
                            BalloonController.Instance.ClearIceRegionHpText(region.ids);
                            BalloonController.Instance.BreakIceRegion(region.ids);
                        }
                        foreach (int id in region.ids) _iceBalloons.Remove(id);
                        _iceRegions.RemoveAt(i);
                    }
                    else
                    {
                        // ROLLBACK_ICE_GROUP_HIT_SCALE_20260629:
                        // Shared Ice HP lost but region is still alive: punch one visible
                        // FrozenLayer overlay at random. Break behavior at HP 0 stays unchanged.
                        if (BalloonController.HasInstance)
                            BalloonController.Instance.PlayRandomIceRegionHitScale(region.ids);
                        CreateOrUpdateIceHpLabel(region);
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
                if (POP_DEBUG)
                    Debug.Log($"[Reveal] popped={evt.balloonId} adjacent={adjacentIds.Count} concealed={concealedCount} revealed={revealedCount}");

                // ROLLBACK_SURPRISE_ORPHAN_REVEAL_20260616: 팝 가능한 이웃이 하나도 없는 concealed Surprise 는
                //   더는 '인접 팝 공개' 트리거가 올 수 없어 비타겟 채로 잔존 → 보드 언클리어. 즉시 공개(안전망).
                //   poppable 이웃 = 미팝 && Wall 아님(Wall 은 영영 안 팝). 조기 공개는 무해(어차피 공개될 풍선).
                //   롤백: 이 블록 제거. (주: 상호-concealed 고립 클러스터는 미커버 — 희귀 author 케이스.)
                if (_surpriseBalloons.Count > 0)
                {
                    _surpriseOrphanBuffer.Clear();
                    foreach (int sid in _surpriseBalloons)
                    {
                        var sdata = BalloonController.Instance.GetBalloon(sid);
                        if (sdata == null) continue;
                        var nbrs = BalloonController.Instance.GetAdjacentBalloonIdsForBalloonPublic(sid, sdata.position);
                        bool hasPoppableNeighbor = false;
                        if (nbrs != null)
                        {
                            for (int n = 0; n < nbrs.Count; n++)
                            {
                                var ndata = BalloonController.Instance.GetBalloon(nbrs[n]);
                                if (ndata == null || ndata.isPopped) continue;
                                if (ndata.gimmickType != BalloonController.GimmickWall) { hasPoppableNeighbor = true; break; }
                            }
                        }
                        if (!hasPoppableNeighbor) _surpriseOrphanBuffer.Add(sid);
                    }
                    for (int i = 0; i < _surpriseOrphanBuffer.Count; i++)
                    {
                        int sid = _surpriseOrphanBuffer[i];
                        Debug.LogWarning($"[GimmickProcessor] Surprise {sid} orphan(팝가능 이웃 0) — 언클리어 방지 자동공개.");
                        RevealSurprise(sid);
                        _surpriseBalloons.Remove(sid);
                        BalloonController.Instance.RevealHiddenBalloon(sid);
                    }
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
