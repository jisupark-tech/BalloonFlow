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

        // ROLLBACK_DART_FLIGHT_TRAIL:
        // Remove these fields and DartManager Enable/DisableDartFlightTrail hooks if the extra
        // visual effect causes readability or device-specific renderer cost issues.
        [Header("[Flight Trail]")]
        [Tooltip("Optional TrailRenderer used only while this dart is flying. Assign a prefab child renderer.")]
        [SerializeField] private TrailRenderer _flightTrail;

        [Tooltip("Shared material for the flight trail. Leave empty to reuse the dart color material.")]
        [SerializeField] private Material _flightTrailMaterial;

        [SerializeField] private float _flightTrailTime = 0.08f;
        [SerializeField] private float _flightTrailStartWidth = 0.055f;
        [SerializeField] private float _flightTrailEndWidth = 0f;
        [SerializeField] private float _flightTrailMinVertexDistance = 0.025f;

        // [2026-05-19 DISABLED] Flight Trail (TrailRenderer) — 주석 처리. 재활성 시 아래 + ApplyColor 내 trail 라인 + DartManager hook 함께 주석 해제.
        // [Header("[Flight Trail — 발사 시 활성, 풀 반환 시 비활성]")]
        // [Tooltip("Dart 비행 잔상용 TrailRenderer. Inspector 에서 자식 TrailRenderer 드래그. 미할당 시 trail 없음.")]
        // [SerializeField] private TrailRenderer _flightTrail;

        /// <summary>색상 적용 대상이 할당되었는지.</summary>
        public bool HasColorRenderers => _colorRenderers != null && _colorRenderers.Length > 0;

        private void Awake()
        {
            ConfigureTrail();
        }

        private void OnDisable()
        {
            DisableTrail();
        }

        public void EnableTrail()
        {
            if (!TryResolveTrail(out TrailRenderer trail)) return;

            ConfigureTrail();
            trail.Clear();
            trail.emitting = true;
        }

        public void DisableTrail()
        {
            if (!TryResolveTrail(out TrailRenderer trail)) return;

            trail.emitting = false;
            trail.Clear();
        }

        // [2026-05-19 DISABLED] Flight Trail API — TrailRenderer wire 안 쓰는 동안 주석.
        // /// <summary>
        // /// 비행 잔상 활성화 — Fire 시 호출. 풀에서 재사용되는 다트 잔상 잔여를 Clear 후 emit 시작.
        // /// 색상은 ApplyColor 가 sharedMaterial 로 set (다트 mesh 와 동일 per-color material).
        // /// _flightTrail 미할당 시 no-op (Inspector wire 안 됐으면 trail 없이 동작).
        // /// </summary>
        // public void EnableTrail()
        // {
        //     if (_flightTrail == null) return;
        //     _flightTrail.Clear();   // 풀 재사용 시 직전 잔여 점 제거 — 매 fire 시 초기화 보장
        //     _flightTrail.emitting = true;
        // }
        //
        // /// <summary>비행 잔상 비활성화 — 풀 반환 시 호출. emit 끄고 잔여 점 Clear.</summary>
        // public void DisableTrail()
        // {
        //     if (_flightTrail == null) return;
        //     _flightTrail.emitting = false;
        //     _flightTrail.Clear();
        // }

        /// <summary>기반 Material 복제 캐시 (색상별)</summary>
        private static readonly Dictionary<int, Material> _dartMatCache = new Dictionary<int, Material>();

        private static Material _needleOutlineMat;
        private static readonly int _propOutlineEnabled = Shader.PropertyToID("_OutlineEnabled");
        private static readonly int _propOutlineColor = Shader.PropertyToID("_OutlineColor");
        private MaterialPropertyBlock _mpb;

        private bool TryResolveTrail(out TrailRenderer trail)
        {
            if (_flightTrail == null)
                _flightTrail = GetComponentInChildren<TrailRenderer>(true);

            trail = _flightTrail;
            return trail != null;
        }

        private void ConfigureTrail()
        {
            if (!TryResolveTrail(out TrailRenderer trail)) return;

            trail.emitting = false;
            trail.time = Mathf.Max(0.01f, _flightTrailTime);
            trail.startWidth = Mathf.Max(0f, _flightTrailStartWidth);
            trail.endWidth = Mathf.Max(0f, _flightTrailEndWidth);
            trail.minVertexDistance = Mathf.Max(0.001f, _flightTrailMinVertexDistance);
            trail.autodestruct = false;
            // ROLLBACK_DART_FLIGHT_TRAIL_COLOR:
            // TrailRenderer multiplies the material by vertex color and can also look gray when
            // generated lighting data is enabled on Lit materials. Keep vertex color pure white
            // and lighting data off so the assigned shared material is visible as authored.
            trail.startColor = Color.white;
            trail.endColor = new Color(1f, 1f, 1f, 0f);
            trail.generateLightingData = false;
            trail.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            trail.receiveShadows = false;
            trail.Clear();

            if (_flightTrailMaterial != null)
                trail.sharedMaterial = _flightTrailMaterial;
        }

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

            for (int i = 0; i < _colorRenderers.Length; i++)
            {
                if (_colorRenderers[i] != null)
                    _colorRenderers[i].sharedMaterial = mat;
            }

            if (TryResolveTrail(out TrailRenderer trail))
                trail.sharedMaterial = _flightTrailMaterial != null ? _flightTrailMaterial : mat;

            // [2026-05-19 DISABLED] Flight trail material 적용 — 주석. 재활성 시 _flightTrail 필드 함께 주석 해제.
            // if (_flightTrail != null)
            //     _flightTrail.sharedMaterial = mat;

            // Niddle: 은색 유지 + 아웃라인 활성화 (MPB)
            if (_outlineOnlyRenderers != null)
            {
                // Niddle 기반 Material 적용 (처음 1회)
                if (_needleBaseMaterial != null)
                {
                    if (_needleOutlineMat == null)
                    {
                        _needleOutlineMat = new Material(_needleBaseMaterial);
                        // [Optimization 2026-05-10 revert] GPU Instancing 채택.
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
