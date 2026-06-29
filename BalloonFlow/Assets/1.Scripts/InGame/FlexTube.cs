using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using DG.Tweening;

namespace BalloonFlow
{
    /// <summary>
    /// FlexTube 부모 — Animator + HP/색상/Segment 리스트 관리.
    /// 자식 FlexTubePart 들이 다트 hit 을 owner 로 전달 → OnDartHit 처리.
    /// 같은 색 다트마다 ZapAttack 트리거 (이미 재생 중이어도 overwrite). EndCap 쪽부터 Segment 1개 비활성 + HP-1.
    /// HP=0 도달 시 ZapFinish 트리거 → Animator state event 또는 fallback 대기 시간 후 Destroy.
    /// </summary>
    public class FlexTube : MonoBehaviour
    {
        public const string ANIM_TRIGGER_ATTACK = "ZapAttack";
        public const string ANIM_TRIGGER_FINISH = "ZapFinish";

        /// <summary>ZapFinish state 이벤트가 미설정/누락 됐을 때 destroy 까지 fallback 대기 시간(초).</summary>
        private const float FINISH_FALLBACK_SECONDS = 1.5f;

        [Header("[연출]")]
        [SerializeField] private Animator _animator;

        [Header("[부품 — Init 시 spawner 가 채움 / 또는 prefab 에 직접 wire]")]
        [Tooltip("순서대로: StartCap(0), Segment(1..N), EndCap(N+1)")]
        [SerializeField] private List<FlexTubePart> _parts = new List<FlexTubePart>();

        [Header("[회전 보정 — prefab forward 축이 +z 가 아닐 때]")]
        [Tooltip("부품 mesh 의 길이 방향이 +z 가 아니면 90/180/270 으로 보정. 0 = 보정 없음(+z).")]
        [SerializeField] private float _extraYRotation = 0f;
        public float ExtraYRotation => _extraYRotation;

        [Header("[EndCap 이동 — Segment 비활성 시 새 끝점으로 슬라이드]")]
        [Tooltip("EndCap 이 사라진 Segment 위치로 이동하는 데 걸리는 시간(초).")]
        [SerializeField] private float _endCapMoveDuration = 0.25f;
        [Tooltip("EndCap 이동 ease 곡선.")]
        [SerializeField] private Ease _endCapMoveEase = Ease.OutQuad;

        [Header("[Visual 분해 — 한 cell 을 여러 visual segment 로 채움]")]
        [Tooltip("Segment cell 1개당 spawn 되는 visual segment 개수. mesh 가 cell 폭의 1/N 인 prefab 가정 (예: 3D Box 기준 1/3 폭).")]
        // ROLLBACK_FLEXTUBE_THREE_SEGMENTS_PER_CELL_20260623:
        // Original authored look expects 3 visual segment meshes per logical grid cell.
        // HP/targeting still use logical cells; this value only controls the visible tiling.
        [SerializeField] private int _visualSegmentsPerCell = 3;
        [Tooltip("다트 hit 마다 마지막 활성 visual segment 가 scale 0 으로 줄어드는 시간(초). 0 = 즉시 비활성.")]
        [SerializeField] private float _segmentShrinkDuration = 0.12f;
        [Tooltip("Segment visual 의 x,y 로컬 스케일 (z=길이축은 유지). 캡(Start/End)에는 미적용.")]
        [SerializeField] private float _segmentScaleXY = 0.8f;
        public int VisualSegmentsPerCell => Mathf.Max(1, _visualSegmentsPerCell);
        public float SegmentScaleXY => _segmentScaleXY;

        private int _hp;          // 남은 HP — segment cell 수(튜브 길이) 기준. visual segment 총수와 분리.
        private int _maxHp;       // 초기 HP — 활성 segment 수를 HP 비율로 환산할 때 분모.
        private int _color = -1;  // -1 = 임의 색 적중 (디버그/테스트 용)
        private int _groupId = -1;
        private bool _destroying;

        // 끝(EndCap)→시작(StartCap) 순서의 visual Segment 제거 큐.
        // shrink 지연(activeSelf 갱신이 tween 완료 후) 때문에 activeSelf 스캔으로 "마지막 활성"을 고르면
        // 같은 hit 에서 여러 개를 지울 때 동일 segment 가 재선택될 수 있어, cursor(_removedSegmentCount)로 추적한다.
        private readonly List<FlexTubePart> _segmentsRemovalOrder = new List<FlexTubePart>();
        private int _totalSegments;        // 초기 visual Segment 총수.
        private int _removedSegmentCount;  // 지금까지 제거 착수한 visual Segment 수 (cursor).

        // ROLLBACK_FLEXTUBE_RENDER_MESH_20260628 (기본 OFF; BalloonController.FLEXTUBE_RENDER_MESH 로 켬):
        // 디스크 리브 GameObject 수백 개 대신, Hose 메시를 path 따라 인스턴스→CombineMeshes 한 '단일 메시'로 렌더.
        // shrink 는 보이는 인스턴스 수를 줄여 메시를 재결합(Edge→Start). HP/타게팅/hit 은 논리 셀 기반이라 동일.
        // _meshSegWorld: index0=Edge 끝(먼저 제거), 마지막=Start. 월드 TRS 보관(슬라이드용 월드pos + 재결합용 변환).
        private bool _meshMode;
        private MeshFilter _meshFilter;
        private Mesh _hoseMesh;
        private readonly List<Matrix4x4> _meshSegWorld = new List<Matrix4x4>();
        private readonly List<int> _meshSegCellId = new List<int>();
        // ROLLBACK_FLEXTUBE_MESHFILTER_SPACE_20260629:
        // Render matrices can include prefab MeshFilter child offsets. Keep sampled tube centers separately
        // so hit shrink/end-cap movement does not read a shifted pivot from Matrix4x4 column 3.
        private readonly List<Vector3> _meshSegCenters = new List<Vector3>();

        public int Color => _color;
        public int GroupId => _groupId;
        public bool IsDestroying => _destroying;
        public IReadOnlyList<FlexTubePart> Parts => _parts;

        /// <summary>Spawn 측에서 호출 — 부품 리스트는 StartCap → Segment_0..N → EndCap 순서로 정렬되어 있어야 함.</summary>
        public void Initialize(int hp, int color, int groupId, List<FlexTubePart> parts)
        {
            _hp = Mathf.Max(1, hp);
            _maxHp = _hp;
            _color = color;
            _groupId = groupId;
            if (parts != null && parts.Count > 0)
                _parts = parts;
            for (int i = 0; i < _parts.Count; i++)
                if (_parts[i] != null) _parts[i].SetOwner(this);

            // ROLLBACK_FLEXTUBE_REMOVAL_ALL_SEGMENTS_20260628:
            // 제거 순서는 Edge(End)→Start. parts 는 [Start, seg@start..seg@edge, End] 스폰 순이라, 인덱스를
            // '뒤에서 앞으로' 돌며 Segment 만 담으면 Edge 쪽부터 들어간다. PartType 으로 캡을 거르므로,
            // 캡이 이음매로 스킵되어 parts[0]/parts[last] 가 Segment 인 경우에도 첫/마지막 세그먼트가 누락되지 않는다.
            // (기존 i=Count-2..1 은 0/last 를 무조건 캡으로 가정해, 캡 스킵 링에서 양 끝 세그먼트가 영영 안 줄어듦.)
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

        /// <summary>다트 hit 진입점 — FlexTubePart 가 위임 호출.</summary>
        public void OnDartHit(int dartColor)
        {
            TryApplyDartHit(dartColor, -1);
        }

        // ROLLBACK_FLEXTUBE_CELL_TARGET_HIT_20260628:
        // Targeting is cell based: each FlexTube footprint cell can be selected by DirectionalTargeting.
        // Consume exactly the targeted logical cell so exposed sibling cells do not become stale misses.
        public bool TryApplyDartHit(int dartColor, int targetBalloonId)
        {
            if (_destroying) return false;
            if (_color >= 0 && dartColor != _color) return false; // 색 불일치 → 무시

            int logicalCellId = ResolveLiveTargetCell(targetBalloonId);
            if (logicalCellId < 0) return false;

            // 매 hit 마다 ZapAttack — Animator trigger 는 자체적으로 reset 되므로 overwrite 안전.
            if (_animator != null) _animator.SetTrigger(ANIM_TRIGGER_ATTACK);

            if (_hp <= 0) { BeginFinish(); return true; }
            _hp--;

            // ROLLBACK_FLEXTUBE_SHRINK_EDGE_TO_START_20260628:
            // 줄어드는 방향은 '항상' Edge(End)→Start. 맞은 위치와 무관하게 EndCap 쪽부터 한 단위씩 비주얼 축소 +
            // 그 셀(seq)이 소진되면 논리 비활성(MarkCellInactiveIfDepleted). 다트는 그대로 소비(accept=return true).
            // (직전: hit cell 을 직접 비활성 + 맞은 곳 근처 비주얼 제거 → 방향이 연결된 쪽으로 뒤섞였던 것 원복.)
            int targetActive = (_maxHp > 0)
                ? Mathf.CeilToInt((float)_hp / _maxHp * _totalSegments)
                : 0;
            targetActive = Mathf.Clamp(targetActive, 0, _totalSegments);
            int targetRemoved = _totalSegments - targetActive;

            Vector3 lastRemovedPos = Vector3.zero;
            bool anyRemoved = false;
            if (_meshMode)
            {
                // ROLLBACK_FLEXTUBE_RENDER_MESH_20260628: 메시 모드 — 인스턴스 cursor 만 전진시키고
                //   남은 구간 [removed..total) 으로 단일 메시를 재결합(Edge→Start). 로직(셀 소진/슬라이드)은 동일.
                while (_removedSegmentCount < targetRemoved && _removedSegmentCount < _totalSegments)
                {
                    int cellId = _meshSegCellId[_removedSegmentCount];
                    lastRemovedPos = SegmentCenter(_removedSegmentCount);
                    _removedSegmentCount++;
                    anyRemoved = true;
                    MarkCellInactiveIfDepletedMesh(cellId);
                }
                if (anyRemoved) { RebuildMesh(); SlideEndCapToMesh(lastRemovedPos); }
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
                if (anyRemoved) SlideEndCapTo(lastRemovedPos);
            }

            // HP 0 또는 모든 segment 소진 → 종료. (_totalSegments 는 rib 모드에서 _segmentsRemovalOrder.Count 와 동일)
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

            // ROLLBACK_FLEXTUBE_RENDER_MESH_20260628: 메시 모드는 세그먼트 파트가 없으므로(_parts=캡만)
            //   남은 메시 셀들(+캡 셀)에서 살아있는 논리 셀을 폴백 조회. (안 하면 stale 다트가 -1 로 거부됨)
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
                {
                    if (IsLiveLogicalCell(ids[k]))
                        return ids[k];
                }
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

        /// <summary>visual segment 1개 shrink → SetActive(false). _segmentShrinkDuration<=0 이면 즉시 비활성.</summary>
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

        /// <summary>EndCap 을 이번 hit 에서 마지막으로 사라진 segment 위치로 슬라이드 — "쑥쑥 줄어드는" 효과.</summary>
        private void SlideEndCapTo(Vector3 targetPos)
        {
            int last = _parts.Count - 1;
            FlexTubePart endCap = last >= 0 ? _parts[last] : null;
            if (endCap == null || !endCap.gameObject.activeSelf) return;

            // 회전 보정 — 새 끝점이 될 (다음 제거 대상 = 남은 마지막 활성) segment 기준 방향.
            Quaternion targetRot = endCap.transform.rotation;
            FlexTubePart newEndSeg = (_removedSegmentCount < _segmentsRemovalOrder.Count)
                ? _segmentsRemovalOrder[_removedSegmentCount] : null;
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

        /// <summary>해당 segment 의 cell 에 아직 제거 안 된 visual segment 가 없으면 cell 단위 비활성(타겟 제외) 마킹.</summary>
        private void MarkCellInactiveIfDepleted(FlexTubePart part)
        {
            int cellId = part.BalloonId;
            if (cellId < 0) return;
            for (int k = _removedSegmentCount; k < _segmentsRemovalOrder.Count; k++)
            {
                var p = _segmentsRemovalOrder[k];
                if (p != null && p.BalloonId == cellId) return; // 같은 cell 의 미제거 segment 남음
            }
            // [2026-06-11] 첫 cell 소진 시 EndCap 의 원래 cell 도 타겟 제외.
            // 캡 visual 은 SlideEndCapTo 로 안쪽 cell 로 옮겨가는데 논리 cell 을 남겨두면
            // 비워진 끝자리에 계속 공격이 가능 — '줄어들면 공격 가능한 부분도 cell 기준으로
            // 줄어든다' 위반 + 허공 타격. (남은 Seg/StartCap cell 은 그대로 공격 가능.)
            ReleaseEndCapCellOnce();
            if (BalloonController.HasInstance)
                BalloonController.Instance.MarkFlexTubeCellInactive(cellId);
        }

        private bool _endCapCellReleased;

        /// <summary>EndCap 의 논리 cell 을 1회 타겟 제외 — 첫 segment cell 소진 시점(캡이 원래 cell 을 비움).</summary>
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
            if (_destroying) return; // 이미 종료 중 — idempotent.
            _destroying = true;
            if (_animator != null) _animator.SetTrigger(ANIM_TRIGGER_FINISH);
            // 남은 모든 cell(StartCap/EndCap/잔존 Segment) 도 target 후보에서 제외 —
            // ZapFinish 연출 중 도착하는 추가 다트가 "놓침"으로 소진되는 것 차단.
            if (BalloonController.HasInstance)
            {
                for (int i = 0; i < _parts.Count; i++)
                {
                    var p = _parts[i];
                    if (p != null && p.BalloonId >= 0)
                        BalloonController.Instance.MarkFlexTubeCellInactive(p.BalloonId);
                }
                // ROLLBACK_FLEXTUBE_RENDER_MESH_20260628: 메시 모드는 세그먼트 셀이 _parts 에 없으니 별도 마킹.
                if (_meshMode)
                    for (int i = 0; i < _meshSegCellId.Count; i++)
                        if (_meshSegCellId[i] >= 0)
                            BalloonController.Instance.MarkFlexTubeCellInactive(_meshSegCellId[i]);
            }
            StartCoroutine(DestroyAfterFinish());
        }

        /// <summary>ZapFinish 연출 길이 후 전체 Destroy. Animator state callback 이 없으면 fallback 시간 사용.</summary>
        private IEnumerator DestroyAfterFinish()
        {
            // Animator current state length 측정 — 정확한 path 검증 없이 simple wait.
            float wait = FINISH_FALLBACK_SECONDS;
            if (_animator != null && _animator.runtimeAnimatorController != null)
            {
                yield return null; // SetTrigger 적용 1프레임 대기
                var info = _animator.GetCurrentAnimatorStateInfo(0);
                if (info.length > 0.05f && info.length < 10f) wait = info.length;
            }
            yield return new WaitForSeconds(wait);
            Destroy(gameObject);
        }

        /// <summary>Animator AnimationEvent 에서 직접 호출용 — ZapFinish 마지막 키에 event 박아두면 fallback 대신 즉시 destroy.</summary>
        public void OnZapFinishComplete()
        {
            if (!_destroying) return;
            Destroy(gameObject);
        }

        // ===== ROLLBACK_FLEXTUBE_RENDER_MESH_20260628: 메시(타일링) 모드 =====

        /// <summary>메시 모드 초기화 — caps(StartCap/EndCap)만 _parts 로, body 는 단일 결합 메시로 렌더.
        /// segWorldMatrices/segCellIds 는 index0=Edge 끝(먼저 제거) 순서여야 한다(BalloonController 가 뒤집어 전달).</summary>
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

            // 세그먼트 파트는 없음 — caps 만 보관(슬라이드/Finish 마킹/타게팅 폴백용).
            _parts.Clear();
            _segmentsRemovalOrder.Clear();
            if (caps != null)
            {
                _parts.AddRange(caps);
                for (int i = 0; i < _parts.Count; i++)
                    if (_parts[i] != null) _parts[i].SetOwner(this);
            }

            if (_animator == null) _animator = GetComponentInChildren<Animator>();
            RebuildMesh();
        }

        /// <summary>남은 인스턴스 [removed..total) 로 단일 메시 재결합. world 행렬을 이 transform 의 local 로 변환해 굽는다.</summary>
        private void RebuildMesh()
        {
            if (_meshFilter == null || _hoseMesh == null) return;

            var prev = _meshFilter.sharedMesh;
            int count = _totalSegments - _removedSegmentCount;
            if (count <= 0)
            {
                _meshFilter.sharedMesh = null;
                if (prev != null && prev != _hoseMesh) Destroy(prev);
                return;
            }

            // 메시 GO 는 tubeObj(FlexTube_Group)의 local-identity 자식 → 월드 행렬을 tubeObj-local 로 변환해 구우면,
            //   렌더 시 tubeObj 변환을 거쳐 필드(월드) 위치로 돌아온다.
            // ROLLBACK_FLEXTUBE_MESHFILTER_SPACE_20260629:
            // Convert the captured world TRS into the actual output MeshFilter's local space. This keeps
            // the mesh object's transform at 0/0/0 and 1/1/1 while avoiding hidden root/child offset drift.
            Matrix4x4 w2l = _meshFilter.transform.worldToLocalMatrix;
            var combines = new CombineInstance[count];
            for (int i = 0; i < count; i++)
            {
                int idx = _removedSegmentCount + i; // 남은 구간 (Edge 쪽이 잘려 줄어듦)
                combines[i].mesh = _hoseMesh;
                combines[i].transform = w2l * _meshSegWorld[idx];
            }

            var m = new Mesh { indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
            m.CombineMeshes(combines, true, true); // mergeSubMeshes, useMatrices
            m.RecalculateBounds();
            _meshFilter.sharedMesh = m;
            if (prev != null && prev != _hoseMesh) Destroy(prev); // 이전 결합 메시 정리(누수 방지)
        }

        /// <summary>해당 cellId 의 남은 메시 인스턴스가 없으면 그 논리 셀을 타겟 제외(소진).</summary>
        private void MarkCellInactiveIfDepletedMesh(int cellId)
        {
            if (cellId < 0) return;
            for (int k = _removedSegmentCount; k < _meshSegCellId.Count; k++)
                if (_meshSegCellId[k] == cellId) return; // 같은 셀의 미제거 인스턴스 남음
            ReleaseEndCapCellOnce();
            if (BalloonController.HasInstance)
                BalloonController.Instance.MarkFlexTubeCellInactive(cellId);
        }

        /// <summary>메시 모드 EndCap 슬라이드 — 새 끝점은 다음 제거 대상 인스턴스 위치 기준.</summary>
        private void SlideEndCapToMesh(Vector3 targetPos)
        {
            int last = _parts.Count - 1;
            FlexTubePart endCap = last >= 0 ? _parts[last] : null;
            if (endCap == null || !endCap.gameObject.activeSelf) return;

            Quaternion targetRot = endCap.transform.rotation;
            if (_removedSegmentCount < _meshSegWorld.Count)
            {
                Vector3 newEndPos = SegmentCenter(_removedSegmentCount);
                Vector3 dir = targetPos - newEndPos; dir.y = 0f;
                if (dir.sqrMagnitude > 0.0001f)
                    targetRot = Quaternion.LookRotation(dir.normalized, Vector3.up)
                                * Quaternion.Euler(0f, _extraYRotation, 0f);
            }

            endCap.transform.DOKill();
            endCap.transform.DOMove(targetPos, _endCapMoveDuration).SetEase(_endCapMoveEase);
            endCap.transform.DORotateQuaternion(targetRot, _endCapMoveDuration).SetEase(_endCapMoveEase);
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
