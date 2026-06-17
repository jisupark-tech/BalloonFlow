using System.Collections.Generic;
using UnityEngine;

namespace BalloonFlow
{
    /// <summary>
    /// Attach to balloon GameObjects to identify them during dart hit detection.
    /// 색상 적용: Inspector에서 지정한 Renderer + 기반 Material로 복제 방식 적용.
    /// 사용자 요구로 풍선 Animator 제거 — 1620 Animator update 부하 제거. pop 시각은 PopEffectPool 파티클이 처리.
    /// </summary>
    /// <remarks>
    /// MUST be in its own file (BalloonIdentifier.cs) for Unity prefab serialization.
    /// Unity requires MonoBehaviour class name == file name for script GUID resolution.
    /// </remarks>
    public class BalloonIdentifier : MonoBehaviour
    {
        [SerializeField] private int _balloonId;
        [SerializeField] private int _color;

        private bool _isPopped;
        // 사용자 요구: Animator 자체 제거 (풍선 prefab Animator 컴포넌트 삭제 + 코드 주석).
        // [SerializeField] private Animator _animator;
        // private static readonly int _animPop = Animator.StringToHash("Pop");

        [Header("[색상 적용 대상 — Inspector에서 할당]")]
        [Tooltip("색상 적용할 Mesh Renderer만 드래그")]
        [SerializeField] private Renderer[] _colorRenderers;
        [Tooltip("기반 Material (BalloonShared). 복제하여 색상만 변경")]
        [SerializeField] private Material _baseMaterial;

        [Tooltip("Hidden 기믹 상태 전용 머테리얼 (BalloonHidden.mat). 프리팹에 드래그 할당.")]
        [SerializeField] private Material _hiddenMaterial;

        // ROLLBACK_BALLOON_HIGHLIGHT_TINT_20260609: 풍선 상단 하이라이트 이미지(balloonHighlight) 를 풍선 색으로 틴트(알파는 유지).
        //   롤백: 이 필드 + Init() 자동탐색 + ApplyColor() 틴트 블록 제거.
        [Header("[풍선 상단 하이라이트 이미지 — 풍선 색으로 틴트(알파는 유지)]")]
        [Tooltip("balloonHighlight SpriteRenderer. 비워두면 Init 에서 자식에서 자동 탐색.")]
        [SerializeField] private SpriteRenderer _highlightRenderer;

        // ROLLBACK_BALLOON_HIGHLIGHT_SUPPRESS_LOWEND_20260617:
        //   고밀도 보드(그림자 억제와 동일 임계)에서 풍선당 반투명 광택(balloonHighlight) 렌더러를 끈다.
        //   광택은 풍선마다 깔리는 또 하나의 반투명 overdraw 층인데 그림자와 달리 억제가 없었다(저사양 fill 누수).
        //   renderer.enabled 만 토글 — 색 틴트(ApplyColor) 상태는 보존하므로 복원 시 그대로 다시 보인다.
        //   롤백: 이 메서드 + BalloonController.RebuildShadowBatch 의 SetHighlightActive 호출 제거.
        public void SetHighlightActive(bool active)
        {
            if (_highlightRenderer != null && _highlightRenderer.enabled != active)
                _highlightRenderer.enabled = active;
        }

        /// <summary>Hidden 머테리얼 존재 여부.</summary>
        public bool HasHiddenMaterial => _hiddenMaterial != null;

        /// <summary>Unique balloon ID.</summary>
        public int BalloonId => _balloonId;

        /// <summary>Balloon color index.</summary>
        public int Color => _color;

        /// <summary>Whether this balloon has been popped.</summary>
        public bool IsPopped => _isPopped;

        /// <summary>색상 적용 대상이 할당되었는지.</summary>
        public bool HasColorRenderers => _colorRenderers != null && _colorRenderers.Length > 0;

        /// <summary>ROLLBACK_OUTLINE_MATERIAL_SWAP_20260609: body(색상) 렌더러 — 외곽선 hull 을 body 에만 붙이기 위함(그림자/라벨 제외).</summary>
        public Renderer[] ColorRenderers => _colorRenderers;

        /// <summary>외부 호출 entry — 사용자 요구로 Animator 제거. 현재 비어있음.</summary>
        public void Init()
        {
            // ROLLBACK_BALLOON_HIGHLIGHT_TINT_20260609: 하이라이트 미할당 시 1회 자동 탐색(인스턴스에 캐시 — 풀 재사용 시 재탐색 없음).
            if (_highlightRenderer == null) _highlightRenderer = GetComponentInChildren<SpriteRenderer>(true);
            // Animator 검색 + CullCompletely 설정 코드 제거됨.
            // [LEGACY 주석]
            // if (_animator == null) _animator = GetComponent<Animator>();
            // if (_animator == null) _animator = GetComponentInChildren<Animator>();
            // if (_animator != null) _animator.cullingMode = AnimatorCullingMode.CullCompletely;
        }

        /// <summary>Sets balloon properties (used by BalloonController during spawn).</summary>
        public void Initialize(int balloonId, int color)
        {
            _balloonId = balloonId;
            _color = color;
            _isPopped = false;
            Init();
            // [LEGACY] if (_animator != null) _animator.SetBool(_animPop, false);
        }

        /// <summary>Marks this balloon as popped. 파티클 이펙트는 외부 PopEffectPool 이 처리 — Animator 트리거 제거됨.</summary>
        public void MarkPopped()
        {
            _isPopped = true;
            // [LEGACY] if (_animator != null) _animator.SetBool(_animPop, true);
        }

        // Piñata 관련 기능은 GimmickIdentifier로 이전됨

        #region Color

        /// <summary>기반 Material 복제 캐시 (색상별)</summary>
        private static readonly Dictionary<int, Material> _balloonMatCache = new Dictionary<int, Material>();

        /// <summary>
        /// 기반 Material을 복제 + 색상 변경하여 지정된 Renderer에 적용.
        /// Outline/Metallic/Smoothness 모두 유지.
        /// </summary>
        public void ApplyColor(Color color)
        {
            if (_colorRenderers == null || _colorRenderers.Length == 0) return;

            Material mat;
            if (_baseMaterial != null)
            {
                int key = _baseMaterial.GetInstanceID() ^ color.GetHashCode();
                if (!_balloonMatCache.TryGetValue(key, out mat))
                {
                    mat = new Material(_baseMaterial);
                    mat.SetColor("_BaseColor", color);
                    // [Optimization 2026-05-10 revert] GPU Instancing path 채택 — 풍선 1500+ × 28 색 시 SRP Batcher 보다 draw call 수 압도적 적음.
                    // SRP Batcher 와 mutually exclusive 지만 mesh 수 많은 mobile 환경에선 instancing 효율 우선.
                    mat.enableInstancing = true;
                    _balloonMatCache[key] = mat;
                }
            }
            else
            {
                mat = BalloonController.GetOrCreateSharedMaterial(color);
            }

            if (mat == null) return;

            for (int i = 0; i < _colorRenderers.Length; i++)
            {
                if (_colorRenderers[i] != null)
                {
                    _colorRenderers[i].enabled = true;
                    _colorRenderers[i].sharedMaterial = mat;
                }
            }

            // ROLLBACK_BALLOON_HIGHLIGHT_TINT_20260609: 상단 하이라이트 이미지를 풍선 색(RGB)으로 틴트. 알파는 프리팹 값 그대로 유지.
            //   SpriteRenderer.color 는 vertex stream 으로 들어가 sprite 배칭을 안 깸(머티리얼/MPB 변경 아님).
            if (_highlightRenderer != null)
            {
                Color hc = color;
                hc.a = _highlightRenderer.color.a;   // 알파 수정 X
                _highlightRenderer.color = hc;
            }
        }

        /// <summary>
        /// Hidden 기믹 상태 전용 머테리얼 적용.
        /// 우선순위: 인자로 전달된 머테리얼 > 프리팹에 할당된 _hiddenMaterial > null(무시).
        /// </summary>
        public void ApplyHiddenMaterial(Material hiddenMat = null)
        {
            Material mat = hiddenMat != null ? hiddenMat : _hiddenMaterial;
            if (mat == null || _colorRenderers == null) return;
            for (int i = 0; i < _colorRenderers.Length; i++)
            {
                if (_colorRenderers[i] == null) continue;
                _colorRenderers[i].enabled = true;
                _colorRenderers[i].sharedMaterial = mat;
            }
        }

        /// <summary>
        /// 풍선 비주얼 (color renderers) 의 보이기/숨기기 토글.
        /// FrozenLayer 오버레이가 부착된 동안 풍선 본체를 숨기고, 해동 시 다시 보이게 함.
        /// 자식 오버레이는 별도 GameObject 라 영향받지 않음.
        /// </summary>
        public void SetVisible(bool visible)
        {
            if (_colorRenderers == null) return;
            for (int i = 0; i < _colorRenderers.Length; i++)
            {
                if (_colorRenderers[i] != null)
                    _colorRenderers[i].enabled = visible;
            }
        }

        #endregion
    }
}
