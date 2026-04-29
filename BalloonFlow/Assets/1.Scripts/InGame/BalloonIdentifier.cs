using System.Collections.Generic;
using UnityEngine;

namespace BalloonFlow
{
    /// <summary>
    /// Attach to balloon GameObjects to identify them during dart hit detection.
    /// Animator 연동: Pop(bool) 파라미터로 팡 연출.
    /// 색상 적용: Inspector에서 지정한 Renderer + 기반 Material로 복제 방식 적용.
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
        [SerializeField]
        private Animator _animator;
        private static readonly int _animPop = Animator.StringToHash("Pop");

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

        /// <summary>Animator 초기화. 외부에서 명시적으로 호출.</summary>
        public void Init()
        {
            if (_animator == null)
                _animator = GetComponent<Animator>();

            if (_animator == null)
                _animator = GetComponentInChildren<Animator>();
        }

        /// <summary>Sets balloon properties (used by BalloonController during spawn).</summary>
        public void Initialize(int balloonId, int color)
        {
            _balloonId = balloonId;
            _color = color;
            _isPopped = false;

            Init();

            if (_animator != null)
                _animator.SetBool(_animPop, false);
        }

        /// <summary>Marks this balloon as popped + 팡 애니메이션 트리거.
        /// 파티클 이펙트는 외부 PopEffectPool 이 처리.</summary>
        public void MarkPopped()
        {
            _isPopped = true;

            if (_animator != null)
                _animator.SetBool(_animPop, true);
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
