using UnityEngine;

namespace BalloonFlow
{
    /// <summary>
    /// Shared identifier and visual helper for field gimmick prefabs.
    /// Handles HP text, hit/end particles, color tint, and Barricade part references.
    /// </summary>
    public class GimmickIdentifier : MonoBehaviour
    {
        public enum GimmickType
        {
            None,
            Pinata,
            PinataBox,
            Pin,
            Wall,
            Ice,
            ColorCurtain,
            Surprise,
            LockKey,
            Barricade,
            SpawnerT,
            SpawnerO,
            FlexTube
        }

        public enum FlexTubePart
        {
            None,
            StartCap,
            Segment,
            EndCap
        }

        [Header("[Gimmick Type]")]
        [SerializeField] private GimmickType _gimmickType = GimmickType.None;

        [Header("[HP Text]")]
        [Tooltip("TMP texts used for HP display. All assigned entries are updated together.")]
        [SerializeField] private TMPro.TMP_Text[] _hpTexts;

        [Header("[Effects]")]
        [Tooltip("Hit particle object.")]
        [SerializeField] private GameObject _hitEffect;
        [Tooltip("Destroy particle object.")]
        [SerializeField] private GameObject _endEffect;

        [Header("[Color Renderers]")]
        [Tooltip("Renderers that receive the gimmick color.")]
        [SerializeField] private Renderer[] _colorRenderers;
        [Tooltip("Base material cloned per color.")]
        [SerializeField] private Material _baseMaterial;

        [Header("[Barricade Parts]")]
        [Tooltip("Barricade head transform.")]
        [SerializeField] private Transform _barricadeHead;
        [Tooltip("Barricade body transform.")]
        [SerializeField] private Transform _barricadeBody;
        [Tooltip("Barricade edge transform.")]
        [SerializeField] private Transform _barricadeEdge;

        [Header("[Barricade Materials — 앞/뒤 면 색 날아감 방지]")]
        [Tooltip("BarricadeBody 에 적용할 머티리얼.")]
        [SerializeField] private Material _barricadeBodyMaterial;
        [Tooltip("Edge 와 Head 에 적용할 머티리얼 (BaricadeEdge).")]
        [SerializeField] private Material _barricadeEdgeHeadMaterial;

        public GimmickType Type => _gimmickType;
        public Transform BarricadeHead => _barricadeHead;
        public Transform BarricadeBody => _barricadeBody;
        public Transform BarricadeEdge => _barricadeEdge;
        public bool HasColorRenderers => _colorRenderers != null && _colorRenderers.Length > 0;

        // ROLLBACK_HOLDER_MATCACHE_KEY_20260609:
        // Cache by base material instance id and color to avoid collisions between prefabs.
        private static readonly System.Collections.Generic.Dictionary<(int, Color), Material> _matCache
            = new System.Collections.Generic.Dictionary<(int, Color), Material>();

        public void Initialize()
        {
            if (_hitEffect != null) _hitEffect.SetActive(false);
            if (_endEffect != null) _endEffect.SetActive(false);
            if (_hpTexts != null)
            {
                for (int i = 0; i < _hpTexts.Length; i++)
                {
                    if (_hpTexts[i] != null)
                        _hpTexts[i].gameObject.SetActive(false);
                }
            }
        }

        public void UpdateHP(int hp)
        {
            EnsureHpTextSetup();
            if (_hpTexts == null) return;

            for (int i = 0; i < _hpTexts.Length; i++)
            {
                if (_hpTexts[i] == null) continue;

                _hpTexts[i].gameObject.SetActive(true);
                _hpTexts[i].SetText("{0}", hp);
            }
        }

        private Vector3[] _hpTextBaseScales;
        private bool _hpTextSetup;

        private void EnsureHpTextSetup()
        {
            if (_hpTextSetup) return;
            _hpTextSetup = true;
            if (_hpTexts == null) return;

            _hpTextBaseScales = new Vector3[_hpTexts.Length];
            for (int i = 0; i < _hpTexts.Length; i++)
            {
                var t = _hpTexts[i];
                if (t == null)
                {
                    _hpTextBaseScales[i] = Vector3.one;
                    continue;
                }

                _hpTextBaseScales[i] = t.transform.localScale;

                // ROLLBACK_GIMMICK_HP_TEXT_ON_TOP_20260629:
                // Keep HP text above geometry by forcing the TMP material ZTest/render queue per instance.
                var mat = t.fontMaterial;
                if (mat != null)
                {
                    if (mat.HasProperty("_ZTestMode"))
                        mat.SetFloat("_ZTestMode", (float)UnityEngine.Rendering.CompareFunction.Always);
                    if (mat.HasProperty("_ZTest"))
                        mat.SetInt("_ZTest", (int)UnityEngine.Rendering.CompareFunction.Always);
                    if (mat.HasProperty("unity_GUIZTestMode"))
                        mat.SetFloat("unity_GUIZTestMode", (float)UnityEngine.Rendering.CompareFunction.Always);
                    mat.renderQueue = 4000;
                }

                var rend = t.GetComponent<Renderer>();
                if (rend != null)
                    rend.sortingOrder = Mathf.Max(rend.sortingOrder, 100);
            }
        }

        public void NormalizeHpTextForFootprint(int w, int h)
        {
            // ROLLBACK_GIMMICK_HP_TEXT_ASPECT_20260629:
            // Sized gimmicks can stretch their parent transform. Counter-scale TMP on local X/Y
            // so HP digits keep a stable aspect ratio.
            if (_hpTexts == null || _hpTexts.Length == 0) return;

            EnsureHpTextSetup();
            int min = Mathf.Min(Mathf.Max(1, w), Mathf.Max(1, h));
            int sizeBase = Mathf.Min(min, 5); // 최대 텍스트 사이즈 5 (작은 축 기준, 그 이상은 5 로 클램프)
            float kx = (float)sizeBase / Mathf.Max(1, w);
            float ky = (float)sizeBase / Mathf.Max(1, h);

            for (int i = 0; i < _hpTexts.Length; i++)
            {
                if (_hpTexts[i] == null) continue;
                if (IsDescendantOfAnotherHpText(i)) continue;

                Vector3 b = _hpTextBaseScales[i];
                _hpTexts[i].transform.localScale = new Vector3(b.x * kx, b.y * ky, b.z);
            }
        }

        private bool IsDescendantOfAnotherHpText(int index)
        {
            Transform self = _hpTexts[index].transform;
            for (int j = 0; j < _hpTexts.Length; j++)
            {
                if (j == index || _hpTexts[j] == null) continue;
                if (self.IsChildOf(_hpTexts[j].transform)) return true;
            }
            return false;
        }

        public void PlayHitEffect()
        {
            if (_hitEffect == null) return;

            _hitEffect.SetActive(false);
            _hitEffect.SetActive(true);
        }

        public void PlayEndEffect()
        {
            GameObject fx = ResolveEndEffect();
            if (fx != null)
                fx.SetActive(true);
        }

        public bool PlayEndEffectDetached(out float maxLifetime)
        {
            // ROLLBACK_FLEXTUBE_END_PARTICLE_DETACH_20260629:
            // FlexTube destroys its root immediately on HP 0. Detach EndParticle so it can finish.
            maxLifetime = 0f;
            GameObject fx = ResolveEndEffect();
            if (fx == null) return false;

            Transform fxTransform = fx.transform;
            fxTransform.SetParent(null, true);
            fx.SetActive(true);

            ParticleSystem[] systems = fx.GetComponentsInChildren<ParticleSystem>(true);
            if (systems == null || systems.Length == 0)
            {
                maxLifetime = 0.6f;
            }
            else
            {
                for (int i = 0; i < systems.Length; i++)
                {
                    ParticleSystem ps = systems[i];
                    if (ps == null) continue;

                    ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                    var main = ps.main;
                    float duration = main.duration;
                    duration += main.startLifetime.mode == ParticleSystemCurveMode.TwoConstants
                        ? main.startLifetime.constantMax
                        : main.startLifetime.constant;
                    duration += main.startDelay.mode == ParticleSystemCurveMode.TwoConstants
                        ? main.startDelay.constantMax
                        : main.startDelay.constant;
                    if (main.loop) duration = Mathf.Min(duration, 2f);
                    maxLifetime = Mathf.Max(maxLifetime, duration);
                }

                for (int i = 0; i < systems.Length; i++)
                    if (systems[i] != null)
                        systems[i].Play(true);
            }

            maxLifetime = Mathf.Clamp(maxLifetime + 0.2f, 0.3f, 3f);
            Destroy(fx, maxLifetime);
            return true;
        }

        public bool PlayEndEffectCloneDetached(out float maxLifetime)
        {
            // ROLLBACK_WOODENBOARD_HIT_DESTROY_FX_20260629:
            // Pooled gimmicks must keep their authored EndParticle child. Spawn a detached clone
            // for one-shot destroy FX, then return the original prefab instance to the pool intact.
            maxLifetime = 0f;
            GameObject template = ResolveEndEffect();
            if (template == null) return false;

            Transform source = template.transform;
            GameObject fx = Instantiate(template, source.position, source.rotation);
            fx.name = template.name + "_RT";
            fx.transform.localScale = source.lossyScale;
            fx.SetActive(true);

            ParticleSystem[] systems = fx.GetComponentsInChildren<ParticleSystem>(true);
            if (systems == null || systems.Length == 0)
            {
                maxLifetime = 0.6f;
            }
            else
            {
                for (int i = 0; i < systems.Length; i++)
                {
                    ParticleSystem ps = systems[i];
                    if (ps == null) continue;

                    ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                    var main = ps.main;
                    float duration = main.duration;
                    duration += main.startLifetime.mode == ParticleSystemCurveMode.TwoConstants
                        ? main.startLifetime.constantMax
                        : main.startLifetime.constant;
                    duration += main.startDelay.mode == ParticleSystemCurveMode.TwoConstants
                        ? main.startDelay.constantMax
                        : main.startDelay.constant;
                    if (main.loop) duration = Mathf.Min(duration, 2f);
                    maxLifetime = Mathf.Max(maxLifetime, duration);
                }

                for (int i = 0; i < systems.Length; i++)
                    if (systems[i] != null)
                        systems[i].Play(true);
            }

            maxLifetime = Mathf.Clamp(maxLifetime + 0.2f, 0.3f, 3f);
            Destroy(fx, maxLifetime);
            return true;
        }

        private GameObject ResolveEndEffect()
        {
            if (_endEffect != null) return _endEffect;

            Transform found = FindDeep(transform, "EndParticle");
            if (found != null)
                _endEffect = found.gameObject;
            return _endEffect;
        }

        private static Transform FindDeep(Transform root, string childName)
        {
            if (root == null || string.IsNullOrEmpty(childName)) return null;

            for (int i = 0; i < root.childCount; i++)
            {
                Transform child = root.GetChild(i);
                if (child != null && child.name == childName) return child;

                Transform nested = FindDeep(child, childName);
                if (nested != null) return nested;
            }

            return null;
        }

        public void ApplyColor(Color color)
        {
            if (_colorRenderers == null || _colorRenderers.Length == 0) return;

            Material mat;
            if (_baseMaterial != null)
            {
                var key = (_baseMaterial.GetInstanceID(), color);
                if (!_matCache.TryGetValue(key, out mat))
                {
                    mat = new Material(_baseMaterial);
                    mat.SetColor("_BaseColor", color);
                    // ROLLBACK_HOLDER_VARIANT_STRIP_20260609:
                    // Keep instancing setting as authored to avoid stripped shader variants in builds.
                    // mat.enableInstancing = true;
                    _matCache[key] = mat;
                }
            }
            else
            {
                mat = BalloonController.GetOrCreateSharedMaterial(color);
            }

            if (mat == null) return;

            for (int i = 0; i < _colorRenderers.Length; i++)
            {
                var r = _colorRenderers[i];
                if (r == null) continue;

                // ROLLBACK_GIMMICK_PARTICLE_TINT_KEEP_MATERIAL_20260612:
                // Keep particle materials intact and tint via ParticleSystem startColor only.
                if (r is ParticleSystemRenderer psr)
                {
                    var ps = psr.GetComponent<ParticleSystem>();
                    if (ps == null) continue;

                    var main = ps.main;
                    main.startColor = color;
                    continue;
                }

                r.sharedMaterial = mat;
            }

            // ROLLBACK_BARRICADE_EDGE_COLOR_20260625:
            // Edge can be outside _colorRenderers; tint it via MaterialPropertyBlock.
            if (_barricadeEdge != null)
            {
                var er = _barricadeEdge.GetComponent<Renderer>();
                if (er != null && !(er is ParticleSystemRenderer))
                {
                    var edgeMpb = new MaterialPropertyBlock();
                    er.GetPropertyBlock(edgeMpb);
                    edgeMpb.SetColor("_BaseColor", color);
                    er.SetPropertyBlock(edgeMpb);
                }
            }
        }

        // ROLLBACK_BARRICADE_FACE_MATERIAL_20260629: Barricade 앞/뒤 면 색 날아감 방지 —
        //   Body / (Edge+Head) 에 전용 머티리얼을 적용하고 색을 틴트한다. 머티리얼 미할당 파트는 no-op
        //   → ApplyColor 결과 유지. ApplyColor 직후(스폰 시) 호출.
        public void ApplyBarricadeMaterials(Color color)
        {
            // Body·Head·Edge 모두 색 틴트 적용 (할당된 머티리얼을 색별로 클론·틴트).
            ApplyBarricadePartMaterial(_barricadeBody, _barricadeBodyMaterial, color, tint: true);
            ApplyBarricadePartMaterial(_barricadeEdge, _barricadeEdgeHeadMaterial, color, tint: true);
            ApplyBarricadePartMaterial(_barricadeHead, _barricadeEdgeHeadMaterial, color, tint: true);
        }

        private static void ApplyBarricadePartMaterial(Transform part, Material baseMat, Color color, bool tint)
        {
            if (part == null || baseMat == null) return;
            var rend = part.GetComponent<Renderer>();
            if (rend == null || rend is ParticleSystemRenderer) return;

            if (!tint)
            {
                // 색상 적용 안 함 — 세팅한 머티리얼 그대로. ApplyColor 가 남긴 MPB(_BaseColor) 틴트도 제거.
                rend.sharedMaterial = baseMat;
                rend.SetPropertyBlock(null);
                return;
            }

            var key = (baseMat.GetInstanceID(), color); // 공유 머티리얼 오염 방지 — (머티리얼,색) 캐시
            if (!_matCache.TryGetValue(key, out Material mat))
            {
                mat = new Material(baseMat);
                if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", color);
                _matCache[key] = mat;
            }
            rend.sharedMaterial = mat;
        }

        public static string ToGimmickString(GimmickType type)
        {
            switch (type)
            {
                case GimmickType.Pinata:       return BalloonController.GimmickPinata;
                case GimmickType.PinataBox:    return BalloonController.GimmickPinataBox;
                case GimmickType.Pin:          return BalloonController.GimmickPin;
                case GimmickType.Wall:         return BalloonController.GimmickWall;
                case GimmickType.Ice:          return BalloonController.GimmickIce;
                case GimmickType.ColorCurtain: return BalloonController.GimmickColorCurtain;
                case GimmickType.Surprise:     return BalloonController.GimmickSurprise;
                case GimmickType.LockKey:      return "Lock_Key";
                case GimmickType.Barricade:    return BalloonController.GimmickBarricade;
                case GimmickType.SpawnerT:     return BalloonController.GimmickSpawnerT;
                case GimmickType.SpawnerO:     return BalloonController.GimmickSpawnerO;
                case GimmickType.FlexTube:     return BalloonController.GimmickFlexTube;
                default:                       return "";
            }
        }

        public static string FlexTubePartToString(FlexTubePart part)
        {
            switch (part)
            {
                case FlexTubePart.StartCap: return "StartCap";
                case FlexTubePart.Segment:  return "Segment";
                case FlexTubePart.EndCap:   return "EndCap";
                default:                    return "";
            }
        }

        public static FlexTubePart FlexTubePartFromString(string s)
        {
            switch (s)
            {
                case "StartCap": return FlexTubePart.StartCap;
                case "Segment":  return FlexTubePart.Segment;
                case "EndCap":   return FlexTubePart.EndCap;
                default:         return FlexTubePart.None;
            }
        }
    }
}
