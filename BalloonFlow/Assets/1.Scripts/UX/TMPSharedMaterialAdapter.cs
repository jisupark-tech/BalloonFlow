using TMPro;
using UnityEngine;

namespace BalloonFlow.UX
{
    /// <summary>
    /// TMP_Text 의 fontSharedMaterial 을 공통 base mat 으로 swap 하고
    /// 원본 mat 의 color property (Outline / Face / Underlay) 만 instance 에 재적용.
    /// SRP Batcher batch 통합을 위한 opt-in helper.
    /// _sharedBaseMaterial 가 null 이거나 원본과 동일하면 동작 안 함 (passthrough).
    /// </summary>
    [DisallowMultipleComponent]
    public class TMPSharedMaterialAdapter : MonoBehaviour
    {
        [SerializeField] private Material _sharedBaseMaterial;
        [SerializeField] private bool _preserveOutlineColor = true;
        [SerializeField] private bool _preserveOutlineWidth = true;
        [SerializeField] private bool _preserveFaceColor = true;
        [SerializeField] private bool _preserveUnderlayColor = true;
        [SerializeField] private bool _preserveUnderlayOffset = true;
        [SerializeField] private bool _preserveUnderlaySoftness = true;
        [SerializeField] private bool _preserveUnderlayDilate = true;

        private bool _applied;

        private void Awake()
        {
            Apply();
        }

        private void OnEnable()
        {
            // Awake 이후 prefab pool 재활용 시점도 커버
            if (!_applied) Apply();
        }

        private void Apply()
        {
            if (_sharedBaseMaterial == null) return;

            var tmp = GetComponent<TMP_Text>();
            if (tmp == null) return;

            Material orig = tmp.fontSharedMaterial;
            if (orig == null) return;
            if (orig == _sharedBaseMaterial)
            {
                _applied = true;
                return;
            }

            // 같은 shader 가 아니면 위험 — 시각 회귀 방지 위해 abort
            if (orig.shader != _sharedBaseMaterial.shader)
            {
                Debug.LogWarning($"[TMPSharedMaterialAdapter] {name}: shader mismatch (orig: {orig.shader?.name}, base: {_sharedBaseMaterial.shader?.name}). Skipping swap.");
                return;
            }

            // 원본 property 추출
            Color outlineCol = (_preserveOutlineColor && orig.HasProperty(ShaderProps.OutlineColor)) ? orig.GetColor(ShaderProps.OutlineColor) : default;
            float outlineWidth = (_preserveOutlineWidth && orig.HasProperty(ShaderProps.OutlineWidth)) ? orig.GetFloat(ShaderProps.OutlineWidth) : 0f;
            Color faceCol = (_preserveFaceColor && orig.HasProperty(ShaderProps.FaceColor)) ? orig.GetColor(ShaderProps.FaceColor) : default;
            Color underlayCol = (_preserveUnderlayColor && orig.HasProperty(ShaderProps.UnderlayColor)) ? orig.GetColor(ShaderProps.UnderlayColor) : default;
            float underlayOffsetX = (_preserveUnderlayOffset && orig.HasProperty(ShaderProps.UnderlayOffsetX)) ? orig.GetFloat(ShaderProps.UnderlayOffsetX) : 0f;
            float underlayOffsetY = (_preserveUnderlayOffset && orig.HasProperty(ShaderProps.UnderlayOffsetY)) ? orig.GetFloat(ShaderProps.UnderlayOffsetY) : 0f;
            float underlaySoftness = (_preserveUnderlaySoftness && orig.HasProperty(ShaderProps.UnderlaySoftness)) ? orig.GetFloat(ShaderProps.UnderlaySoftness) : 0f;
            float underlayDilate = (_preserveUnderlayDilate && orig.HasProperty(ShaderProps.UnderlayDilate)) ? orig.GetFloat(ShaderProps.UnderlayDilate) : 0f;

            // sharedBase 로 swap → fontMaterial 접근 시 instance 복제 발생
            tmp.fontSharedMaterial = _sharedBaseMaterial;
            Material inst = tmp.fontMaterial;
            if (inst == null)
            {
                _applied = true;
                return;
            }

            if (_preserveOutlineColor && inst.HasProperty(ShaderProps.OutlineColor)) inst.SetColor(ShaderProps.OutlineColor, outlineCol);
            if (_preserveOutlineWidth && inst.HasProperty(ShaderProps.OutlineWidth)) inst.SetFloat(ShaderProps.OutlineWidth, outlineWidth);
            if (_preserveFaceColor && inst.HasProperty(ShaderProps.FaceColor)) inst.SetColor(ShaderProps.FaceColor, faceCol);
            if (_preserveUnderlayColor && inst.HasProperty(ShaderProps.UnderlayColor)) inst.SetColor(ShaderProps.UnderlayColor, underlayCol);
            if (_preserveUnderlayOffset && inst.HasProperty(ShaderProps.UnderlayOffsetX)) inst.SetFloat(ShaderProps.UnderlayOffsetX, underlayOffsetX);
            if (_preserveUnderlayOffset && inst.HasProperty(ShaderProps.UnderlayOffsetY)) inst.SetFloat(ShaderProps.UnderlayOffsetY, underlayOffsetY);
            if (_preserveUnderlaySoftness && inst.HasProperty(ShaderProps.UnderlaySoftness)) inst.SetFloat(ShaderProps.UnderlaySoftness, underlaySoftness);
            if (_preserveUnderlayDilate && inst.HasProperty(ShaderProps.UnderlayDilate)) inst.SetFloat(ShaderProps.UnderlayDilate, underlayDilate);

            tmp.SetMaterialDirty();
            _applied = true;
        }

        private static class ShaderProps
        {
            public static readonly int OutlineColor = Shader.PropertyToID("_OutlineColor");
            public static readonly int OutlineWidth = Shader.PropertyToID("_OutlineWidth");
            public static readonly int FaceColor = Shader.PropertyToID("_FaceColor");
            public static readonly int UnderlayColor = Shader.PropertyToID("_UnderlayColor");
            public static readonly int UnderlayOffsetX = Shader.PropertyToID("_UnderlayOffsetX");
            public static readonly int UnderlayOffsetY = Shader.PropertyToID("_UnderlayOffsetY");
            public static readonly int UnderlaySoftness = Shader.PropertyToID("_UnderlaySoftness");
            public static readonly int UnderlayDilate = Shader.PropertyToID("_UnderlayDilate");
        }
    }
}
