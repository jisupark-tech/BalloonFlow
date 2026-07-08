using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using DG.Tweening;

namespace BalloonFlow
{
    /// <summary>
    /// Runtime owner for one FlexTube group.
    /// FlexTubePart children forward dart hits here. HP, cell ownership, visual shrink,
    /// and final destruction are handled at group level.
    /// </summary>
    public class FlexTube : MonoBehaviour
    {
        public const string ANIM_TRIGGER_ATTACK = "ZapAttack";
        public const string ANIM_TRIGGER_FINISH = "ZapFinish";

        private const float FINISH_DESTROY_SECONDS = 0.12f;

        [Header("[Animation]")]
        [SerializeField] private Animator _animator;

        [Header("[Runtime Parts]")]
        [Tooltip("Runtime parts in path order. Usually StartCap, Segment..., EndCap.")]
        [SerializeField] private List<FlexTubePart> _parts = new List<FlexTubePart>();

        [Header("[Rotation Correction]")]
        [Tooltip("Extra Y rotation when the authored prefab forward axis is not +Z.")]
        [SerializeField] private float _extraYRotation = 0f;
        public float ExtraYRotation => _extraYRotation;

        [Header("[EndCap Slide]")]
        [Tooltip("Duration for EndCap to move to the newly exposed segment in rib mode.")]
        [SerializeField] private float _endCapMoveDuration = 0.25f;
        [Tooltip("Ease used for EndCap movement.")]
        [SerializeField] private Ease _endCapMoveEase = Ease.OutQuad;

        [Header("[Visual Segments]")]
        [Tooltip("Visible hose pieces per logical cell. Gameplay still uses logical cell data.")]
        [SerializeField] private int _visualSegmentsPerCell = 2;
        [Tooltip("Duration for visible segment shrink after each hit.")]
        [SerializeField] private float _segmentShrinkDuration = 0.12f;
        [Tooltip("Visual XY scale for rib-mode segments. Caps are not affected.")]
        [SerializeField] private float _segmentScaleXY = 0.8f;
        public int VisualSegmentsPerCell => Mathf.Max(1, _visualSegmentsPerCell);
        public float SegmentScaleXY => _segmentScaleXY;

        private int _hp;
        private int _maxHp;
        private int _color = -1;
        private int _groupId = -1;
        private bool _destroying;

        // Removal order is from EndCap side toward StartCap side.
        private readonly List<FlexTubePart> _segmentsRemovalOrder = new List<FlexTubePart>();
        private int _totalSegments;
        private int _removedSegmentCount;

        // ROLLBACK_FLEXTUBE_RENDER_MESH_20260628:
        // Mesh mode combines many hose pieces into one MeshFilter for better runtime cost.
        // _meshSegWorld index 0 is the end-side piece, and the last index is start-side.
        private bool _meshMode;
        private MeshFilter _meshFilter;
        private Mesh _hoseMesh;
        private readonly List<Matrix4x4> _meshSegWorld = new List<Matrix4x4>();
        private readonly List<int> _meshSegCellId = new List<int>();

        // ROLLBACK_FLEXTUBE_MESHFILTER_SPACE_20260629:
        // Render matrices can include prefab MeshFilter child offsets. Keep sampled tube centers
        // separately so hit shrink and EndCap movement do not read a shifted pivot.
        private readonly List<Vector3> _meshSegCenters = new List<Vector3>();

        // ROLLBACK_FLEXTUBE_ENDCAP_FOLLOW_ROT_20260707: 세그먼트의 해석적 회전(segRot=LookRotation(tangent)*extraRot).
        //   EndCap 슬라이드가 이산 센터차분 LookRotation(직선구간 흔들림) 대신 이 값을 그대로 채택 → 세그먼트를 정확히
        //   따라가고 직선구간에서 회전하지 않는다(스폰 시 EndCap 회전 규약과 100% 일치).
        private readonly List<Quaternion> _meshSegRotations = new List<Quaternion>();

        // ROLLBACK_FLEXTUBE_MESH_SMOOTH_SHRINK_20260629:
        // Logical removal commits immediately. This cursor only controls how much mesh is visible.
        private int _meshVisibleRemovedSegmentCount;
        private Coroutine _meshShrinkRoutine;

        public int Color => _color;
        public int GroupId => _groupId;
        public bool IsDestroying => _destroying;
        // ROLLBACK_ZAP_GIMMICK_DAMAGE_20260705: 튜브의 살아있는 실제 HP(_hp). Zap preserve 다트 계산이
        //   authored/셀수 heuristic 대신 이 값을 읽어 Zap 이 깎은 HP 를 정확히 반영한다.
        public int RemainingHp => Mathf.Max(0, _hp);
        public IReadOnlyList<FlexTubePart> Parts => _parts;

        public void Initialize(int hp, int color, int groupId, List<FlexTubePart> parts)
        {
            _hp = Mathf.Max(1, hp);
            _maxHp = _hp;
            _color = color;
            _groupId = groupId;
            _meshMode = false;
            if (parts != null && parts.Count > 0)
                _parts = parts;

            for (int i = 0; i < _parts.Count; i++)
                if (_parts[i] != null)
                    _parts[i].SetOwner(this);

            _segmentsRemovalOrder.Clear();
            for (int i = _parts.Count - 1; i >= 0; i--)
            {
                var p = _parts[i];
                if (p != null && p.PartType == GimmickIdentifier.FlexTubePart.Segment)
                    _segmentsRemovalOrder.Add(p);
            }

            _totalSegments = _segmentsRemovalOrder.Count;
            _removedSegmentCount = 0;

            if (_animator == null)
                _animator = GetComponentInChildren<Animator>();
            // ROLLBACK_FLEXTUBE_ZAP_INSTANT_20260706: 애니메이터는 기본(scaled) 유지 — Zap pause(timeScale=0) 중엔 '얼려서'
            //   ZapAttack recoil 이 재생돼 Body/End 를 Z 로 밀어 틀어지는 것을 막는다. (pause 중 히트는 recoil 스킵 + 즉시 스냅.)
        }

        public void OnDartHit(int dartColor)
        {
            TryApplyDartHit(dartColor, -1);
        }

        // ROLLBACK_FLEXTUBE_CELL_TARGET_HIT_20260628:
        // Targeting is cell based. Consume the exact logical cell selected by DirectionalTargeting.
        public bool TryApplyDartHit(int dartColor, int targetBalloonId)
        {
            if (_destroying) return false;
            if (_color >= 0 && dartColor != _color) return false;

            int logicalCellId = ResolveLiveTargetCell(targetBalloonId);
            if (logicalCellId < 0) return false;

            // ROLLBACK_FLEXTUBE_ZAP_INSTANT_20260706: 부스터(Zap)는 timeScale=0. 이때 히트 반응(ZapAttack: Body/End 를 Z 로
            //   움직이는 recoil 애니메이션)이 그 포즈에서 멈춰 '틀어짐'. pause 중엔 recoil 을 아예 트리거하지 않고, 아래 축소/EndCap 을
            //   즉시 최종 상태로 스냅한다(_boosterPaused 분기). 일반 플레이(timeScale>0)는 기존대로 애니메이션.
            bool _boosterPaused = Time.timeScale == 0f;
            if (_animator != null && !_boosterPaused)
                _animator.SetTrigger(ANIM_TRIGGER_ATTACK);

            if (_hp <= 0)
            {
                BeginFinish();
                return true;
            }

            _hp--;
            // ROLLBACK_GIMMICK_SFX_TABLE_20260703:
            // FlexTube hit uses Stage_Match_Normal_2.mp3 only after a valid color/cell hit consumes HP.
            if (AudioManager.HasInstance) AudioManager.Instance.PlayFlexTubeHit();

            int targetActive = (_maxHp > 0)
                ? Mathf.CeilToInt((float)_hp / _maxHp * _totalSegments)
                : 0;
            targetActive = Mathf.Clamp(targetActive, 0, _totalSegments);
            int targetRemoved = _totalSegments - targetActive;

            Vector3 lastRemovedPos = Vector3.zero;
            bool anyRemoved = false;
            if (_meshMode)
            {
                while (_removedSegmentCount < targetRemoved && _removedSegmentCount < _totalSegments)
                {
                    int cellId = _meshSegCellId[_removedSegmentCount];
                    lastRemovedPos = SegmentCenter(_removedSegmentCount);
                    _removedSegmentCount++;
                    anyRemoved = true;
                    MarkCellInactiveIfDepletedMesh(cellId);
                }

                if (anyRemoved)
                {
                    AnimateMeshShrinkToLogicalCursor();
                    SlideEndCapToMesh(lastRemovedPos);
                }
            }
            else
            {
                while (_removedSegmentCount < targetRemoved && _removedSegmentCount < _segmentsRemovalOrder.Count)
                {
                    var part = _segmentsRemovalOrder[_removedSegmentCount];
                    _removedSegmentCount++;
                    if (part == null || !part.gameObject.activeSelf) continue;

                    lastRemovedPos = part.transform.position;
                    anyRemoved = true;
                    ShrinkAndDeactivateSegment(part);
                    MarkCellInactiveIfDepleted(part);
                }

                if (anyRemoved)
                    SlideEndCapTo(lastRemovedPos);
            }

            bool hasLiveLogicalCells = BalloonController.HasInstance
                && BalloonController.Instance.HasLiveFlexTubeGroupCells(_groupId);
            if (_hp <= 0 || !hasLiveLogicalCells || _removedSegmentCount >= _totalSegments)
                BeginFinish();

            return true;
        }

        private int ResolveLiveTargetCell(int targetBalloonId)
        {
            if (IsLiveLogicalCell(targetBalloonId))
                return targetBalloonId;

            if (_meshMode)
            {
                for (int k = _removedSegmentCount; k < _meshSegCellId.Count; k++)
                    if (IsLiveLogicalCell(_meshSegCellId[k]))
                        return _meshSegCellId[k];

                for (int i = 0; i < _parts.Count; i++)
                {
                    var cap = _parts[i];
                    if (cap != null && IsLiveLogicalCell(cap.BalloonId))
                        return cap.BalloonId;
                }
                return -1;
            }

            for (int i = 0; i < _parts.Count; i++)
            {
                var p = _parts[i];
                if (p == null || !p.gameObject.activeSelf) continue;

                int[] ids = p.BalloonIds;
                if (ids == null || ids.Length == 0)
                {
                    if (IsLiveLogicalCell(p.BalloonId))
                        return p.BalloonId;
                    continue;
                }

                for (int k = 0; k < ids.Length; k++)
                    if (IsLiveLogicalCell(ids[k]))
                        return ids[k];
            }

            return -1;
        }

        private bool IsLiveLogicalCell(int balloonId)
        {
            if (!BalloonController.HasInstance || balloonId < 0) return false;
            BalloonData data = BalloonController.Instance.GetBalloon(balloonId);
            return data != null
                && !data.isPopped
                && data.gimmickType == BalloonController.GimmickFlexTube
                && data.flexTubeGroupId == _groupId;
        }

        private void ShrinkAndDeactivateSegment(FlexTubePart part)
        {
            var t = part.transform;
            t.DOKill();
            FlexTubePart captured = part;
            // ROLLBACK_FLEXTUBE_ZAP_INSTANT_20260706: 부스터 pause(timeScale=0) 중엔 tween 없이 즉시 비활성(틀어짐/잔상 방지).
            if (Time.timeScale == 0f || _segmentShrinkDuration <= 0.001f)
            {
                part.gameObject.SetActive(false);
                return;
            }
            t.DOScale(Vector3.zero, _segmentShrinkDuration)
                .SetEase(Ease.InQuad)
                .OnComplete(() =>
                {
                    if (captured != null && captured.gameObject != null)
                        captured.gameObject.SetActive(false);
                });
        }

        private void SlideEndCapTo(Vector3 targetPos)
        {
            int last = _parts.Count - 1;
            FlexTubePart endCap = last >= 0 ? _parts[last] : null;
            if (endCap == null || !endCap.gameObject.activeSelf) return;

            Quaternion targetRot = endCap.transform.rotation;
            FlexTubePart newEndSeg = (_removedSegmentCount < _segmentsRemovalOrder.Count)
                ? _segmentsRemovalOrder[_removedSegmentCount]
                : null;
            if (newEndSeg != null)
            {
                Vector3 dir = targetPos - newEndSeg.transform.position;
                dir.y = 0f;
                if (dir.sqrMagnitude > 0.0001f)
                    targetRot = Quaternion.LookRotation(dir.normalized, Vector3.up)
                                * Quaternion.Euler(0f, _extraYRotation, 0f);
            }

            endCap.transform.DOKill();
            // ROLLBACK_FLEXTUBE_ZAP_INSTANT_20260706: 부스터 pause(timeScale=0) 중엔 즉시 최종 위치/회전 스냅(틀어짐 방지).
            if (Time.timeScale == 0f)
            {
                endCap.transform.position = targetPos;
                endCap.transform.rotation = targetRot;
                return;
            }
            endCap.transform.DOMove(targetPos, _endCapMoveDuration).SetEase(_endCapMoveEase);
            endCap.transform.DORotateQuaternion(targetRot, _endCapMoveDuration).SetEase(_endCapMoveEase);
        }

        private void MarkCellInactiveIfDepleted(FlexTubePart part)
        {
            int cellId = part.BalloonId;
            if (cellId < 0) return;

            for (int k = _removedSegmentCount; k < _segmentsRemovalOrder.Count; k++)
            {
                var p = _segmentsRemovalOrder[k];
                if (p != null && p.BalloonId == cellId)
                    return;
            }

            ReleaseEndCapCellOnce();
            if (BalloonController.HasInstance)
                BalloonController.Instance.MarkFlexTubeCellInactive(cellId);
        }

        private bool _endCapCellReleased;

        private void ReleaseEndCapCellOnce()
        {
            if (_endCapCellReleased) return;
            _endCapCellReleased = true;

            int last = _parts.Count - 1;
            FlexTubePart endCap = last >= 0 ? _parts[last] : null;
            if (endCap != null && endCap.PartType == GimmickIdentifier.FlexTubePart.EndCap
                && endCap.BalloonId >= 0 && BalloonController.HasInstance)
            {
                BalloonController.Instance.MarkFlexTubeCellInactive(endCap.BalloonId);
            }
        }

        private void BeginFinish()
        {
            if (_destroying) return;
            _destroying = true;
            // ROLLBACK_GIMMICK_SFX_TABLE_20260703:
            // FlexTube disappear uses woodbreak.mp3 once when finish starts.
            if (AudioManager.HasInstance) AudioManager.Instance.PlayFlexTubeDisappear();

            PlayDetachedEndParticleOnce();
            HideVisualsForFinish();

            if (_animator != null)
                _animator.SetTrigger(ANIM_TRIGGER_FINISH);

            if (BalloonController.HasInstance)
            {
                for (int i = 0; i < _parts.Count; i++)
                {
                    var p = _parts[i];
                    if (p != null && p.BalloonId >= 0)
                        BalloonController.Instance.MarkFlexTubeCellInactive(p.BalloonId);
                }

                if (_meshMode)
                {
                    for (int i = 0; i < _meshSegCellId.Count; i++)
                        if (_meshSegCellId[i] >= 0)
                            BalloonController.Instance.MarkFlexTubeCellInactive(_meshSegCellId[i]);
                }
            }

            // ROLLBACK_FLEXTUBE_CLEAR_CONDITION_20260708: FlexTube 파괴는 OnBalloonPopped 를 발행하지
            //   않으므로(silent 셀 제거), 마지막 남은 오브젝트가 튜브면 클리어 평가가 영영 안 돈다.
            //   전 셀 inactive 마킹 '후' 재평가 — 이 튜브는 이미 dead 로 보이고, 다른 튜브가 살아 있으면
            //   IsBoardClear 가 막는다. 롤백: 이 블록 제거.
            if (BoardStateManager.HasInstance)
                BoardStateManager.Instance.ReevaluateClearAfterGimmickResolved();

            StartCoroutine(DestroyAfterFinish());
        }

        private IEnumerator DestroyAfterFinish()
        {
            // ROLLBACK_FLEXTUBE_FAST_FINISH_20260629:
            // HP 0 should behave like WoodenBox: break particle plays immediately and the tube disappears.
            // Do not wait for the legacy ZapFinish state length here; detached particles outlive this root.
            yield return new WaitForSeconds(FINISH_DESTROY_SECONDS);
            Destroy(gameObject);
        }

        public void OnZapFinishComplete()
        {
            if (!_destroying) return;
            Destroy(gameObject);
        }

        private void PlayDetachedEndParticleOnce()
        {
            // ROLLBACK_FLEXTUBE_END_PARTICLE_DETACH_20260629:
            // Prefer the end-side part, then fallback through all parts/root. Play one burst only.
            for (int i = _parts.Count - 1; i >= 0; i--)
            {
                FlexTubePart part = _parts[i];
                if (part == null) continue;

                var gi = part.GetComponent<GimmickIdentifier>();
                if (gi != null && gi.PlayEndEffectDetached(out _))
                    return;
            }

            var rootGi = GetComponent<GimmickIdentifier>();
            if (rootGi != null && rootGi.PlayEndEffectDetached(out _))
                return;

            var anyGi = GetComponentInChildren<GimmickIdentifier>(true);
            if (anyGi != null && anyGi.PlayEndEffectDetached(out _))
                return;

            SpawnWoodenBoardEndParticleFallback();
        }

        private void SpawnWoodenBoardEndParticleFallback()
        {
            // ROLLBACK_FLEXTUBE_WOODEN_ENDPARTICLE_FALLBACK_20260629:
            // FlexTube prefabs may not carry EndParticle. Reuse WoodenBoard's burst if needed.
            GameObject woodenPrefab = Resources.Load<GameObject>("Prefabs/WoodenBoard");
            Transform template = FindDeep(woodenPrefab != null ? woodenPrefab.transform : null, "EndParticle");
            if (template == null) return;

            Vector3 pos = transform.position;
            for (int i = _parts.Count - 1; i >= 0; i--)
            {
                if (_parts[i] == null) continue;
                pos = _parts[i].transform.position;
                break;
            }

            GameObject fx = Instantiate(template.gameObject, pos, Quaternion.identity);
            fx.name = "FlexTube_EndParticle_RT";
            fx.SetActive(true);

            float maxLifetime = 0.6f;
            ParticleSystem[] systems = fx.GetComponentsInChildren<ParticleSystem>(true);
            for (int i = 0; i < systems.Length; i++)
            {
                ParticleSystem ps = systems[i];
                if (ps == null) continue;

                ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                var main = ps.main;
                if (_color >= 0 && _color < BalloonController.BalloonColors.Length)
                    main.startColor = BalloonController.BalloonColors[_color];

                float duration = main.duration + main.startLifetime.constantMax + main.startDelay.constantMax;
                if (main.loop) duration = Mathf.Min(duration, 2f);
                maxLifetime = Mathf.Max(maxLifetime, duration);
            }

            for (int i = 0; i < systems.Length; i++)
                if (systems[i] != null)
                    systems[i].Play(true);

            Destroy(fx, Mathf.Clamp(maxLifetime + 0.2f, 0.3f, 3f));
        }

        private static Transform FindDeep(Transform root, string childName)
        {
            if (root == null || string.IsNullOrEmpty(childName)) return null;

            for (int i = 0; i < root.childCount; i++)
            {
                Transform child = root.GetChild(i);
                if (child != null && child.name == childName) return child;

                Transform nested = FindDeep(child, childName);
                if (nested != null) return nested;
            }

            return null;
        }

        private void HideVisualsForFinish()
        {
            // ROLLBACK_FLEXTUBE_FAST_FINISH_20260629:
            // Logical cells are inactive already. Hide visuals immediately while detached FX plays.
            if (_meshShrinkRoutine != null)
            {
                StopCoroutine(_meshShrinkRoutine);
                _meshShrinkRoutine = null;
            }

            _meshVisibleRemovedSegmentCount = _totalSegments;
            if (_meshMode)
                RebuildMesh();

            for (int i = 0; i < _parts.Count; i++)
            {
                FlexTubePart part = _parts[i];
                if (part == null) continue;

                var renderers = part.GetComponentsInChildren<Renderer>(true);
                for (int r = 0; r < renderers.Length; r++)
                    if (renderers[r] != null)
                        renderers[r].enabled = false;

                var colliders = part.GetComponentsInChildren<Collider>(true);
                for (int c = 0; c < colliders.Length; c++)
                    if (colliders[c] != null)
                        colliders[c].enabled = false;
            }
        }

        public void InitializeMesh(int hp, int color, int groupId, List<FlexTubePart> caps,
                                   MeshFilter meshFilter, Mesh hoseMesh,
                                   List<Matrix4x4> segWorldMatrices, List<int> segCellIds,
                                   List<Vector3> segWorldCenters,
                                   List<Quaternion> segWorldRotations = null)
        {
            _hp = Mathf.Max(1, hp);
            _maxHp = _hp;
            _color = color;
            _groupId = groupId;
            _meshMode = true;
            _meshFilter = meshFilter;
            _hoseMesh = hoseMesh;

            _meshSegWorld.Clear();
            _meshSegCellId.Clear();
            _meshSegCenters.Clear();
            _meshSegRotations.Clear();
            if (segWorldMatrices != null) _meshSegWorld.AddRange(segWorldMatrices);
            if (segCellIds != null) _meshSegCellId.AddRange(segCellIds);
            if (segWorldCenters != null) _meshSegCenters.AddRange(segWorldCenters);
            if (segWorldRotations != null) _meshSegRotations.AddRange(segWorldRotations);

            while (_meshSegCenters.Count < _meshSegWorld.Count)
                _meshSegCenters.Add(ColumnPos(_meshSegWorld[_meshSegCenters.Count]));
            if (_meshSegCenters.Count > _meshSegWorld.Count)
                _meshSegCenters.RemoveRange(_meshSegWorld.Count, _meshSegCenters.Count - _meshSegWorld.Count);

            // ROLLBACK_FLEXTUBE_ENDCAP_FOLLOW_ROT_20260707: 회전 리스트를 세그먼트 수에 맞춰 정렬(부족분은 행렬 회전으로 폴백).
            while (_meshSegRotations.Count < _meshSegWorld.Count)
                _meshSegRotations.Add(_meshSegWorld[_meshSegRotations.Count].rotation);
            if (_meshSegRotations.Count > _meshSegWorld.Count)
                _meshSegRotations.RemoveRange(_meshSegWorld.Count, _meshSegRotations.Count - _meshSegWorld.Count);

            _totalSegments = _meshSegWorld.Count;
            _removedSegmentCount = 0;
            _meshVisibleRemovedSegmentCount = 0;
            if (_meshShrinkRoutine != null)
            {
                StopCoroutine(_meshShrinkRoutine);
                _meshShrinkRoutine = null;
            }

            _parts.Clear();
            _segmentsRemovalOrder.Clear();
            if (caps != null)
            {
                _parts.AddRange(caps);
                for (int i = 0; i < _parts.Count; i++)
                    if (_parts[i] != null)
                        _parts[i].SetOwner(this);
            }

            if (_animator == null)
                _animator = GetComponentInChildren<Animator>();
            // ROLLBACK_FLEXTUBE_ZAP_INSTANT_20260706: 애니메이터 기본(scaled) 유지 — pause 중 recoil 재생으로 인한 틀어짐 방지.

            RebuildMesh();
        }

        private void RebuildMesh()
        {
            if (_meshFilter == null || _hoseMesh == null) return;

            var prev = _meshFilter.sharedMesh;
            int renderRemoved = _meshMode
                ? Mathf.Clamp(_meshVisibleRemovedSegmentCount, 0, _totalSegments)
                : Mathf.Clamp(_removedSegmentCount, 0, _totalSegments);
            int count = _totalSegments - renderRemoved;
            if (count <= 0)
            {
                _meshFilter.sharedMesh = null;
                if (prev != null && prev != _hoseMesh) Destroy(prev);
                return;
            }

            // ROLLBACK_FLEXTUBE_HIT_OFFSET_SHIFT_20260707: 결합 변환을 '스폰 시(원점) meshGO 프레임' 기준으로 고정.
            //   기존엔 live _meshFilter.worldToLocalMatrix 를 매 RebuildMesh 마다 곱해, 튜브가 gimmickOffsetFlexTube(루트
            //   delta)로 이동한 뒤의 히트 RebuildMesh 가 그 오프셋을 상쇄 → 메시가 -offset 만큼 순간이동(피격 시 위치 변경).
            //   _meshSegWorld 는 tubeObj@원점(=meshGO@원점, local identity)에서 캡처돼 그 자체가 meshGO-local 이다.
            //   live w2l 곱을 제거하면 RebuildMesh 가 튜브 현재 위치와 무관하게 안정 — 메시는 항상 authored(+루트 오프셋)
            //   로 렌더된다(스폰 시 w2l=identity 라 동작 불변, 오프셋 적용 후 히트에서만 교정). 롤백: w2l 곱 복원.
            var combines = new CombineInstance[count];
            for (int i = 0; i < count; i++)
            {
                int idx = renderRemoved + i;
                combines[i].mesh = _hoseMesh;
                combines[i].transform = _meshSegWorld[idx];
            }

            var m = new Mesh { indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
            m.CombineMeshes(combines, true, true);
            m.RecalculateBounds();
            _meshFilter.sharedMesh = m;

            if (prev != null && prev != _hoseMesh)
                Destroy(prev);
        }

        private void MarkCellInactiveIfDepletedMesh(int cellId)
        {
            if (cellId < 0) return;

            for (int k = _removedSegmentCount; k < _meshSegCellId.Count; k++)
                if (_meshSegCellId[k] == cellId)
                    return;

            ReleaseEndCapCellOnce();
            if (BalloonController.HasInstance)
                BalloonController.Instance.MarkFlexTubeCellInactive(cellId);
        }

        private void AnimateMeshShrinkToLogicalCursor()
        {
            if (!_meshMode)
            {
                RebuildMesh();
                return;
            }

            // ROLLBACK_FLEXTUBE_ZAP_INSTANT_20260706: 부스터 pause(timeScale=0) 중엔 애니메이션 코루틴이 어긋나므로
            //   즉시 최종 상태로 rebuild(스냅). 롤백: 이 if 블록 제거.
            if (Time.timeScale == 0f)
            {
                if (_meshShrinkRoutine != null) { StopCoroutine(_meshShrinkRoutine); _meshShrinkRoutine = null; }
                _meshVisibleRemovedSegmentCount = _removedSegmentCount;
                RebuildMesh();
                return;
            }

            if (_meshShrinkRoutine != null)
                StopCoroutine(_meshShrinkRoutine);
            _meshShrinkRoutine = StartCoroutine(AnimateMeshShrinkRoutine(_removedSegmentCount));
        }

        private IEnumerator AnimateMeshShrinkRoutine(int targetRemoved)
        {
            // ROLLBACK_FLEXTUBE_MESH_SMOOTH_SHRINK_20260629:
            // Gameplay removal is already committed. This coroutine only spreads the visible rebuild.
            targetRemoved = Mathf.Clamp(targetRemoved, 0, _totalSegments);
            int startRemoved = Mathf.Clamp(_meshVisibleRemovedSegmentCount, 0, _totalSegments);
            if (targetRemoved <= startRemoved)
            {
                _meshVisibleRemovedSegmentCount = targetRemoved;
                RebuildMesh();
                _meshShrinkRoutine = null;
                yield break;
            }

            float duration = Mathf.Max(0.001f, _segmentShrinkDuration);
            float elapsed = 0f;
            int lastVisibleRemoved = startRemoved;
            while (elapsed < duration)
            {
                elapsed += Time.deltaTime; // (부스터 pause 는 AnimateMeshShrinkToLogicalCursor 의 instant 분기가 처리 — 이 코루틴은 timeScale>0 에서만 구동)
                float t = Mathf.Clamp01(elapsed / duration);
                int nextRemoved = Mathf.Clamp(
                    Mathf.RoundToInt(Mathf.Lerp(startRemoved, targetRemoved, t)),
                    startRemoved,
                    targetRemoved);

                if (nextRemoved != lastVisibleRemoved)
                {
                    _meshVisibleRemovedSegmentCount = nextRemoved;
                    RebuildMesh();
                    lastVisibleRemoved = nextRemoved;
                }

                yield return null;
            }

            _meshVisibleRemovedSegmentCount = targetRemoved;
            RebuildMesh();
            _meshShrinkRoutine = null;
        }

        private void SlideEndCapToMesh(Vector3 targetPos)
        {
            int last = _parts.Count - 1;
            FlexTubePart endCap = last >= 0 ? _parts[last] : null;
            if (endCap == null || !endCap.gameObject.activeSelf) return;

            Quaternion targetRot = endCap.transform.rotation;
            if (_removedSegmentCount < _meshSegWorld.Count)
            {
                // ROLLBACK_FLEXTUBE_ENDCAP_FOLLOW_ROT_20260707: 새 끝 세그먼트의 해석적 회전(segRot)을 그대로 채택 —
                //   이산 센터차분 LookRotation 의 직선구간 흔들림/불일치 제거. segRot 는 스폰 EndCap 회전과 동일 규약
                //   (LookRotation(tangent)*extraRot). 저장값은 tubeObj-local(스폰 시 루트 identity) → 현재 루트 회전을 실어 월드로.
                //   회전 데이터가 없으면(폴백) 기존 센터차분 방식 유지.
                if (_removedSegmentCount < _meshSegRotations.Count)
                {
                    targetRot = transform.rotation * _meshSegRotations[_removedSegmentCount];
                }
                else
                {
                    Vector3 newEndPos = SegmentCenter(_removedSegmentCount);
                    Vector3 dir = targetPos - newEndPos;
                    dir.y = 0f;
                    if (dir.sqrMagnitude > 0.0001f)
                        targetRot = Quaternion.LookRotation(dir.normalized, Vector3.up)
                                    * Quaternion.Euler(0f, _extraYRotation, 0f);
                }
            }

            endCap.transform.DOKill();
            // ROLLBACK_FLEXTUBE_ZAP_INSTANT_20260706: 부스터 pause(timeScale=0) 중엔 tween 대신 즉시 최종 위치/회전 스냅(틀어짐 방지).
            if (Time.timeScale == 0f)
            {
                endCap.transform.position = targetPos;
                endCap.transform.rotation = targetRot;
                return;
            }
            // ROLLBACK_FLEXTUBE_MESH_EDGE_SYNC_20260629:
            // Match EndCap movement to mesh shrink so the cap does not detach from the body.
            float duration = _meshMode ? Mathf.Max(0.03f, _segmentShrinkDuration) : _endCapMoveDuration;
            endCap.transform.DOMove(targetPos, duration).SetEase(_endCapMoveEase);
            endCap.transform.DORotateQuaternion(targetRot, duration).SetEase(_endCapMoveEase);
        }

        private Vector3 SegmentCenter(int index)
        {
            // ROLLBACK_FLEXTUBE_HIT_OFFSET_SHIFT_20260707: 센터는 tubeObj@원점에서 캡처된 tubeObj-local 값 → 현재 루트
            //   변환(gimmickOffsetFlexTube 반영)을 통과시켜 월드로 환산. EndCap 슬라이드 타겟이 오프셋된 튜브/메시와
            //   일치해 피격 시 캡 위치 변경을 막는다. (스폰 시 tubeObj@원점 이면 TransformPoint 는 항등 → 동작 불변.)
            if (index >= 0 && index < _meshSegCenters.Count)
                return transform.TransformPoint(_meshSegCenters[index]);
            if (index >= 0 && index < _meshSegWorld.Count)
                return transform.TransformPoint(ColumnPos(_meshSegWorld[index]));
            return transform.position;
        }

        private static Vector3 ColumnPos(Matrix4x4 m)
        {
            Vector4 c = m.GetColumn(3);
            return new Vector3(c.x, c.y, c.z);
        }
    }
}
