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

            if (_animator != null)
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
            if (_segmentShrinkDuration > 0.001f)
            {
                t.DOScale(Vector3.zero, _segmentShrinkDuration)
                    .SetEase(Ease.InQuad)
                    .OnComplete(() =>
                    {
                        if (captured != null && captured.gameObject != null)
                            captured.gameObject.SetActive(false);
                    });
            }
            else
            {
                part.gameObject.SetActive(false);
            }
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
                                   List<Vector3> segWorldCenters)
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
            if (segWorldMatrices != null) _meshSegWorld.AddRange(segWorldMatrices);
            if (segCellIds != null) _meshSegCellId.AddRange(segCellIds);
            if (segWorldCenters != null) _meshSegCenters.AddRange(segWorldCenters);

            while (_meshSegCenters.Count < _meshSegWorld.Count)
                _meshSegCenters.Add(ColumnPos(_meshSegWorld[_meshSegCenters.Count]));
            if (_meshSegCenters.Count > _meshSegWorld.Count)
                _meshSegCenters.RemoveRange(_meshSegWorld.Count, _meshSegCenters.Count - _meshSegWorld.Count);

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

            Matrix4x4 w2l = _meshFilter.transform.worldToLocalMatrix;
            var combines = new CombineInstance[count];
            for (int i = 0; i < count; i++)
            {
                int idx = renderRemoved + i;
                combines[i].mesh = _hoseMesh;
                combines[i].transform = w2l * _meshSegWorld[idx];
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
                elapsed += Time.deltaTime;
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
                Vector3 newEndPos = SegmentCenter(_removedSegmentCount);
                Vector3 dir = targetPos - newEndPos;
                dir.y = 0f;
                if (dir.sqrMagnitude > 0.0001f)
                    targetRot = Quaternion.LookRotation(dir.normalized, Vector3.up)
                                * Quaternion.Euler(0f, _extraYRotation, 0f);
            }

            endCap.transform.DOKill();
            // ROLLBACK_FLEXTUBE_MESH_EDGE_SYNC_20260629:
            // Match EndCap movement to mesh shrink so the cap does not detach from the body.
            float duration = _meshMode ? Mathf.Max(0.03f, _segmentShrinkDuration) : _endCapMoveDuration;
            endCap.transform.DOMove(targetPos, duration).SetEase(_endCapMoveEase);
            endCap.transform.DORotateQuaternion(targetRot, duration).SetEase(_endCapMoveEase);
        }

        private Vector3 SegmentCenter(int index)
        {
            if (index >= 0 && index < _meshSegCenters.Count)
                return _meshSegCenters[index];
            if (index >= 0 && index < _meshSegWorld.Count)
                return ColumnPos(_meshSegWorld[index]);
            return transform.position;
        }

        private static Vector3 ColumnPos(Matrix4x4 m)
        {
            Vector4 c = m.GetColumn(3);
            return new Vector3(c.x, c.y, c.z);
        }
    }
}
