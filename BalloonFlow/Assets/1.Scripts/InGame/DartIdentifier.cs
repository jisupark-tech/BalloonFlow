using System.Collections.Generic;
using UnityEngine;

namespace BalloonFlow
{
    /// <summary>
    /// Dart 프리팹에 부착. 색상 적용 대상 Renderer를 Inspector에서 지정.
    /// 기반 Material(BalloonShared)을 복제하여 색상만 변경 → Outline/Metallic 유지.
    /// </summary>
    public class DartIdentifier : MonoBehaviour
    {
        [Header("[색상 적용 대상 Renderer — Inspector에서 할당]")]
        [Tooltip("Body 등 색상 적용할 Renderer만 드래그")]
        [SerializeField] private Renderer[] _colorRenderers;

        [Tooltip("기반 Material (BalloonShared). 복제하여 색상만 변경")]
        [SerializeField] private Material _baseMaterial;

        [Header("[아웃라인 전용 Renderer — 색상 변경 없이 아웃라인만 적용]")]
        [Tooltip("Niddle 등 은색 유지하면서 아웃라인만 적용할 Renderer")]
        [SerializeField] private Renderer[] _outlineOnlyRenderers;

        [Tooltip("Niddle 기반 Material (은색). 복제하여 아웃라인 활성화")]
        [SerializeField] private Material _needleBaseMaterial;

        /// <summary>색상 적용 대상이 할당되었는지.</summary>
        public bool HasColorRenderers => _colorRenderers != null && _colorRenderers.Length > 0;

        /// <summary>기반 Material 복제 캐시 (색상별)</summary>
        private static readonly Dictionary<int, Material> _dartMatCache = new Dictionary<int, Material>();

        private static Material _needleOutlineMat;
        private static readonly int _propOutlineEnabled = Shader.PropertyToID("_OutlineEnabled");
        private static readonly int _propOutlineColor = Shader.PropertyToID("_OutlineColor");
        private MaterialPropertyBlock _mpb;

        /// <summary>기반 Material을 복제 + 색상 변경하여 적용. Outline/Metallic 유지.</summary>
        public void ApplyColor(Color color)
        {
            if (_colorRenderers == null) return;

            Material mat;
            if (_baseMaterial != null)
            {
                int key = _baseMaterial.GetInstanceID() ^ color.GetHashCode();
                if (!_dartMatCache.TryGetValue(key, out mat))
                {
                    mat = new Material(_baseMaterial);
                    mat.SetColor("_BaseColor", color);
                    mat.enableInstancing = true;
                    _dartMatCache[key] = mat;
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
                    _colorRenderers[i].sharedMaterial = mat;
            }

            // Niddle: 은색 유지 + 아웃라인 활성화 (MPB)
            if (_outlineOnlyRenderers != null)
            {
                // Niddle 기반 Material 적용 (처음 1회)
                if (_needleBaseMaterial != null)
                {
                    if (_needleOutlineMat == null)
                    {
                        _needleOutlineMat = new Material(_needleBaseMaterial);
                        _needleOutlineMat.enableInstancing = true;
                    }
                    for (int i = 0; i < _outlineOnlyRenderers.Length; i++)
                    {
                        if (_outlineOnlyRenderers[i] != null)
                            _outlineOnlyRenderers[i].sharedMaterial = _needleOutlineMat;
                    }
                }

                // 아웃라인 MPB 적용
                if (_mpb == null) _mpb = new MaterialPropertyBlock();
                for (int i = 0; i < _outlineOnlyRenderers.Length; i++)
                {
                    if (_outlineOnlyRenderers[i] == null) continue;
                    _outlineOnlyRenderers[i].GetPropertyBlock(_mpb);
                    _mpb.SetFloat(_propOutlineEnabled, 1f);
                    _mpb.SetColor(_propOutlineColor, Color.black); // Niddle 아웃라인은 모든 다트에서 검정 고정
                    _outlineOnlyRenderers[i].SetPropertyBlock(_mpb);
                }
            }
        }
    }
}
