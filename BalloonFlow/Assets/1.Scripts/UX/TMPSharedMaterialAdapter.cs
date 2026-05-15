using TMPro;
using UnityEngine;

namespace BalloonFlow.UX
{
    /// <summary>
    /// Swaps TMP_Text to a shared preset material without touching fontMaterial.
    /// Accessing TMP_Text.fontMaterial creates a per-text material instance and breaks batching.
    /// If a text needs a different outline/face/underlay style, assign a separate preset material.
    /// </summary>
    [DisallowMultipleComponent]
    public class TMPSharedMaterialAdapter : MonoBehaviour
    {
        [SerializeField] private Material _sharedBaseMaterial;

        private bool _applied;

        private void Awake()
        {
            Apply();
        }

        private void OnEnable()
        {
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

            if (orig.shader != _sharedBaseMaterial.shader)
            {
                Debug.LogWarning($"[TMPSharedMaterialAdapter] {name}: shader mismatch (orig: {orig.shader?.name}, base: {_sharedBaseMaterial.shader?.name}). Skipping swap.");
                return;
            }

            // ROLLBACK_TMP_SHARED_MATERIAL_ONLY:
            // Reintroduce fontMaterial-based property copy only if per-text material instances are acceptable.
            tmp.fontSharedMaterial = _sharedBaseMaterial;
            tmp.SetMaterialDirty();
            _applied = true;
        }
    }
}
