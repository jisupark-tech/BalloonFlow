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

        private int _hp;          // 남은 HP — Segment 활성 수와 1:1
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

        /// <summary>EndCap 쪽(끝)부터 가장 가까운 활성 Segment 1개를 비활성 + EndCap 을 그 위치로 슬라이드 이동. 비활성된 게 있으면 true.</summary>
        private bool DeactivateLastActiveSegment()
        {
            int last = _parts.Count - 1;
            FlexTubePart endCap = last >= 0 ? _parts[last] : null;

            // 양 끝(StartCap=0, EndCap=last) 제외 — Segment 만 대상.
            for (int i = last - 1; i >= 1; i--)
            {
                var part = _parts[i];
                if (part != null && part.gameObject.activeSelf
                    && part.PartType == GimmickIdentifier.FlexTubePart.Segment)
                {
                    Vector3 targetPos = part.transform.position;
                    // 비활성 cell 을 다트 target 후보에서 제외 — 같은 cell 재 hit 으로 인한 "공격 안 됨" 회피.
                    if (BalloonController.HasInstance && part.BalloonId >= 0)
                        BalloonController.Instance.MarkFlexTubeCellInactive(part.BalloonId);
                    part.gameObject.SetActive(false);

                    if (endCap != null && endCap.gameObject.activeSelf)
                    {
                        // EndCap 새 rotation — 사라진 Segment 의 prev cell(= i-1) → 자신 방향.
                        // 직선이면 직선 방향, 코너에서는 새 끝점의 정확한 향함 보장.
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
                    return true;
                }
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
