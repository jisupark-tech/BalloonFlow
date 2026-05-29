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

        private int _hp;          // 남은 HP — 활성 visual segment 수와 1:1 (cell 수 × N)
        private int _color = -1;  // -1 = 임의 색 적중 (디버그/테스트 용)
        private int _groupId = -1;
        private bool _destroying;

        public int Color => _color;
        public int GroupId => _groupId;
        public bool IsDestroying => _destroying;
        public IReadOnlyList<FlexTubePart> Parts => _parts;

        /// <summary>Spawn 측에서 호출 — 부품 리스트는 StartCap → Segment_0..N → EndCap 순서로 정렬되어 있어야 함.</summary>
        public void Initialize(int hp, int color, int groupId, List<FlexTubePart> parts)
        {
            _hp = Mathf.Max(1, hp);
            _color = color;
            _groupId = groupId;
            if (parts != null && parts.Count > 0)
                _parts = parts;
            for (int i = 0; i < _parts.Count; i++)
                if (_parts[i] != null) _parts[i].SetOwner(this);

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

            bool removed = DeactivateLastActiveSegment();
            if (removed)
            {
                _hp--;
                if (_hp <= 0) BeginFinish();
            }
            else if (_hp > 0)
            {
                // 활성 Segment 0개인데 HP 양수 — race 또는 segment=0 spawn 케이스. 안전 종료.
                _hp = 0;
                BeginFinish();
            }
        }

        /// <summary>
        /// EndCap 쪽(끝)부터 가장 가까운 활성 visual segment 1개를 shrink → SetActive(false).
        /// + EndCap 슬라이드 (사라질 segment 위치로). + 같은 cell 의 모든 visual segment 가 비활성됐을 때만 cell 단위 MarkFlexTubeCellInactive.
        /// 비활성될 segment 가 있으면 true.
        /// </summary>
        private bool DeactivateLastActiveSegment()
        {
            int last = _parts.Count - 1;
            FlexTubePart endCap = last >= 0 ? _parts[last] : null;

            // 양 끝(StartCap=0, EndCap=last) 제외 — Segment 만 대상.
            for (int i = last - 1; i >= 1; i--)
            {
                var part = _parts[i];
                if (part == null) continue;
                if (!part.gameObject.activeSelf) continue;
                if (part.PartType != GimmickIdentifier.FlexTubePart.Segment) continue;

                Vector3 targetPos = part.transform.position;
                int cellId = part.BalloonId;

                // visual segment shrink. _segmentShrinkDuration <= 0 이면 즉시 비활성 (DOTween 미사용).
                var t = part.transform;
                t.DOKill();
                FlexTubePart capturedPart = part;
                if (_segmentShrinkDuration > 0.001f)
                {
                    t.DOScale(Vector3.zero, _segmentShrinkDuration)
                        .SetEase(Ease.InQuad)
                        .OnComplete(() =>
                        {
                            if (capturedPart != null && capturedPart.gameObject != null)
                                capturedPart.gameObject.SetActive(false);
                        });
                }
                else
                {
                    part.gameObject.SetActive(false);
                }

                // EndCap 슬라이드 — 항상 사라질 segment 자리로 (visual segment 단위 미세 이동 = "쑥쑥 줄어드는" 효과).
                if (endCap != null && endCap.gameObject.activeSelf)
                {
                    Quaternion targetRot = endCap.transform.rotation;
                    if (i - 1 >= 0 && _parts[i - 1] != null)
                    {
                        Vector3 dir = targetPos - _parts[i - 1].transform.position;
                        dir.y = 0f;
                        if (dir.sqrMagnitude > 0.0001f)
                        {
                            targetRot = Quaternion.LookRotation(dir.normalized, Vector3.up)
                                        * Quaternion.Euler(0f, _extraYRotation, 0f);
                        }
                    }

                    endCap.transform.DOKill();
                    endCap.transform.DOMove(targetPos, _endCapMoveDuration).SetEase(_endCapMoveEase);
                    endCap.transform.DORotateQuaternion(targetRot, _endCapMoveDuration).SetEase(_endCapMoveEase);
                }

                // cell 단위 비활성 — 같은 BalloonId 의 다른 active Segment 가 있으면 cell 은 살아있음.
                // shrink tween 완료 전이라도 SetActive 호출 시점 기준이 아니라 "이번에 사라질 segment" 까지 전부 비활성으로 가정해서 검사.
                if (cellId >= 0)
                {
                    bool cellHasOtherActive = false;
                    for (int k = 1; k < last; k++)
                    {
                        if (k == i) continue;
                        var p = _parts[k];
                        if (p == null) continue;
                        if (p.BalloonId != cellId) continue;
                        if (p.PartType != GimmickIdentifier.FlexTubePart.Segment) continue;
                        if (!p.gameObject.activeSelf) continue;
                        cellHasOtherActive = true;
                        break;
                    }
                    if (!cellHasOtherActive && BalloonController.HasInstance)
                        BalloonController.Instance.MarkFlexTubeCellInactive(cellId);
                }
                return true;
            }
            return false;
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
