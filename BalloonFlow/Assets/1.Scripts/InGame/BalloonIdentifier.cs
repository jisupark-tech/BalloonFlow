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

        [Header("[팝 이펙트 — Inspector에서 할당]")]
        [Tooltip("풍선 터질 때 활성화할 이펙트 (Particle System 등)")]
        [SerializeField] private GameObject _popEffect;

        [Header("[색상 적용 대상 — Inspector에서 할당]")]
        [Tooltip("색상 적용할 Mesh Renderer만 드래그 (ParticleSystem은 아래 _colorParticles에)")]
        [SerializeField] private Renderer[] _colorRenderers;
        [Tooltip("색상 적용할 ParticleSystem (main.startColor로 적용, material은 건드리지 않음)")]
        [SerializeField] private ParticleSystem[] _colorParticles;
        [Tooltip("기반 Material (BalloonShared). 복제하여 색상만 변경")]
        [SerializeField] private Material _baseMaterial;

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

            // 이펙트 비활성화 (풀 재사용 대비)
            if (_popEffect != null)
                _popEffect.SetActive(false);
        }

        /// <summary>Marks this balloon as popped + 팡 애니메이션 트리거.</summary>
        public void MarkPopped()
        {
            _isPopped = true;

            if (_animator != null)
                _animator.SetBool(_animPop, true);

            // 팝 이펙트 활성화 + 풍선 원본 색상 적용 (변주 없이 단일 색)
            if (_popEffect != null)
            {
                _popEffect.SetActive(true);
                var ps = _popEffect.GetComponent<ParticleSystem>();
                if (ps != null)
                {
                    int ci = Mathf.Clamp(_color, 0, BalloonController.BalloonColors.Length - 1);
                    Color baseColor = BalloonController.BalloonColors[ci];
                    var main = ps.main;
                    main.startColor = baseColor;
                }
            }
        }

        /// <summary>팝 이펙트를 풍선에서 분리. 풍선 스케일 변경이 파티클에 영향 안 주도록.</summary>
        public Transform DetachPopEffect()
        {
            if (_popEffect == null) return null;
            var t = _popEffect.transform;
            t.SetParent(null, true);
            return t;
        }

        /// <summary>분리했던 팝 이펙트를 다시 풍선 자식으로 복귀 + 비활성화.</summary>
        public void ReattachPopEffect(Transform detached)
        {
            if (detached == null || _popEffect == null) return;
            detached.SetParent(transform, false);
            detached.localPosition = Vector3.zero;
            detached.localScale = Vector3.one;
            _popEffect.SetActive(false);
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

            if (_colorParticles != null)
            {
                for (int i = 0; i < _colorParticles.Length; i++)
                {
                    if (_colorParticles[i] == null) continue;
                    var main = _colorParticles[i].main;
                    main.startColor = color;
                }
            }
        }

        #endregion
    }
}
