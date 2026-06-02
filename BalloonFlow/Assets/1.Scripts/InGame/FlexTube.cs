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

            // visual Segment 제거 순서 구성 — EndCap 쪽(높은 index)부터 StartCap 쪽으로.
            // _parts = StartCap(0), Segment(1..last-1), EndCap(last).
            _segmentsRemovalOrder.Clear();
            for (int i = _parts.Count - 2; i >= 1; i--)
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
            if (_destroying) return;
            if (_color >= 0 && dartColor != _color) return; // 색 불일치 → 무시

            // 매 hit 마다 ZapAttack — Animator trigger 는 자체적으로 reset 되므로 overwrite 안전.
            if (_animator != null) _animator.SetTrigger(ANIM_TRIGGER_ATTACK);

            if (_hp <= 0) { BeginFinish(); return; }
            _hp--;

            // 남은 HP 비율에 맞춰 활성 visual Segment 수를 목표치까지 줄인다 (HP 1당 1개가 아니라 비율).
            // 예) HP=cell 수, totalSegments=cell 수×N → 한 hit 마다 N(=visualSegmentsPerCell)개씩 함께 사라짐.
            int targetActive = (_maxHp > 0)
                ? Mathf.CeilToInt((float)_hp / _maxHp * _totalSegments)
                : 0;
            targetActive = Mathf.Clamp(targetActive, 0, _totalSegments);
            int targetRemoved = _totalSegments - targetActive;

            Vector3 lastRemovedPos = Vector3.zero;
            bool anyRemoved = false;
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

            // HP 0 또는 모든 segment 소진 → 종료.
            if (_hp <= 0 || _removedSegmentCount >= _segmentsRemovalOrder.Count)
                BeginFinish();
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
            if (BalloonController.HasInstance)
                BalloonController.Instance.MarkFlexTubeCellInactive(cellId);
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
    }
}
