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

        /// <summary>외부 호출 entry — 사용자 요구로 Animator 제거. 현재 비어있음.</summary>
        public void Init()
        {
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
