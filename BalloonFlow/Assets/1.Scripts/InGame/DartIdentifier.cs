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

        [Header("[Optional impact anchor]")]
        [Tooltip("Optional world anchor for the needle tip. If empty, Niddle renderer bounds are used.")]
        [SerializeField] private Transform _needleTip;

        /// <summary>색상 적용 대상이 할당되었는지.</summary>
        public bool HasColorRenderers => _colorRenderers != null && _colorRenderers.Length > 0;

        /// <summary>기반 Material 복제 캐시 (색상별)</summary>
        private static readonly Dictionary<int, Material> _dartMatCache = new Dictionary<int, Material>();

        private static Material _needleOutlineMat;
        // ROLLBACK_DART_OUTLINE_HULL_20260615: 아웃라인을 OutlineHull material[1] 방식으로 전환하며
        //   _propOutlineEnabled/_propOutlineColor/_mpb(구 MPB 토글용) 제거. 롤백 시 함께 복원.

        /// <summary>
        /// Distance from this prefab root to the needle tip along the current firing direction.
        /// ROLLBACK_DART_NEEDLE_TIP_IMPACT: remove this helper and let DartManager resolve at flight end.
        /// </summary>
        public bool TryGetNeedleTipLead(Vector3 worldDirection, out float lead)
        {
            lead = 0f;
            if (worldDirection.sqrMagnitude <= 0.0001f) return false;
            Vector3 dir = worldDirection.normalized;

            if (_needleTip != null)
            {
                lead = Vector3.Dot(_needleTip.position - transform.position, dir);
                return lead > 0.0001f;
            }

            if (TryGetRendererLead(_outlineOnlyRenderers, dir, out lead))
                return true;

            return TryGetRendererLead(_colorRenderers, dir, out lead);
        }

        private bool TryGetRendererLead(Renderer[] renderers, Vector3 dir, out float lead)
        {
            lead = 0f;
            if (renderers == null || renderers.Length == 0) return false;

            bool found = false;
            Vector3 origin = transform.position;
            for (int i = 0; i < renderers.Length; i++)
            {
                Renderer r = renderers[i];
                if (r == null) continue;
                Bounds b = r.bounds;
                Vector3 c = b.center;
                Vector3 e = b.extents;
                for (int sx = -1; sx <= 1; sx += 2)
                for (int sy = -1; sy <= 1; sy += 2)
                for (int sz = -1; sz <= 1; sz += 2)
                {
                    Vector3 p = c + new Vector3(e.x * sx, e.y * sy, e.z * sz);
                    float d = Vector3.Dot(p - origin, dir);
                    if (!found || d > lead)
                    {
                        lead = d;
                        found = true;
                    }
                }
            }

            return found && lead > 0.0001f;
        }

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
                    // [Optimization 2026-05-10 revert] GPU Instancing 채택 — dart 200+ × 28 색.
                    mat.enableInstancing = true;
                    _dartMatCache[key] = mat;
                }
            }
            else
            {
                mat = BalloonController.GetOrCreateSharedMaterial(color);
            }

            if (mat == null) return;

            // ROLLBACK_DART_OUTLINE_HULL_20260615: START
            // 다트 아웃라인 = 공유 OutlineHull 머티리얼을 material[1] 로 얹는 방식(HolderIdentifier PHASE1 동일).
            //   왜: niddle/body 머티리얼은 Custom/ItemShared(=single-pass, outline 패스 없음)라 _OutlineEnabled
            //       토글로는 외곽선이 안 나온다. 별도 단일 공유 hull 머티리얼(Custom/OutlineHull, inverted-hull)을
            //       두 번째 서브머티리얼로 얹어 외곽선 패스를 그린다.
            //   배칭: 모든 다트가 hull '하나'를 공유 → 외곽선들 한 배치, main[0]은 그대로 인스턴싱 유지.
            //         (MPB 방식이 배칭을 깨던 2026-06-09 회귀를 피함.) EnableDartOutline_Phase2=false 면 hull=null
            //         → 단일 머티리얼(외곽선 OFF).
            //   롤백: hull 분기 제거하고 sharedMaterial 단일 setter 로 복원 + EnableDartOutline_Phase2=false.
            Material hull = BalloonController.EnableDartOutline_Phase2
                ? BalloonController.GetOutlineHullMaterial() : null;

            for (int i = 0; i < _colorRenderers.Length; i++)
            {
                if (_colorRenderers[i] == null) continue;
                if (hull != null)
                    _colorRenderers[i].sharedMaterials = new Material[] { mat, hull };
                else
                    _colorRenderers[i].sharedMaterial = mat;
            }

            // Niddle: 은색 유지 + 아웃라인(hull material[1])
            if (_outlineOnlyRenderers != null && _needleBaseMaterial != null)
            {
                if (_needleOutlineMat == null)
                {
                    _needleOutlineMat = new Material(_needleBaseMaterial);
                    // [Optimization 2026-05-10 revert] GPU Instancing 채택.
                    _needleOutlineMat.enableInstancing = true;
                }
                for (int i = 0; i < _outlineOnlyRenderers.Length; i++)
                {
                    if (_outlineOnlyRenderers[i] == null) continue;
                    if (hull != null)
                        _outlineOnlyRenderers[i].sharedMaterials = new Material[] { _needleOutlineMat, hull };
                    else
                        _outlineOnlyRenderers[i].sharedMaterial = _needleOutlineMat;
                }
            }
            // ROLLBACK_DART_OUTLINE_HULL_20260615: END
        }
    }
}
