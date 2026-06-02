using UnityEngine;

namespace BalloonFlow
{
    /// <summary>
    /// 기믹 오브젝트(Pinata, PinataBox, Pin, Wall 등) 프리팹에 부착.
    /// Inspector에서 기믹 타입을 선택하면 해당 기능이 활성화됨.
    /// </summary>
    public class GimmickIdentifier : MonoBehaviour
    {
        /// <summary>기믹 종류 Enum — Inspector 드롭다운으로 선택.</summary>
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

        /// <summary>FlexTube 부품 종류 — 단일 "FlexTube" 기믹 안에서 셀 역할 구분.</summary>
        public enum FlexTubePart
        {
            None,
            StartCap,
            Segment,
            EndCap
        }

        [Header("[기믹 타입 선택]")]
        [SerializeField] private GimmickType _gimmickType = GimmickType.None;

        [Header("[HP 표시 — Pinata/PinataBox/Pin/Ice용]")]
        [Tooltip("HP 텍스트들 — 인스펙터에서 2개의 TMP_Text를 할당. 두 항목 모두 동일 HP 값으로 동기 갱신됨")]
        [SerializeField] private TMPro.TMP_Text[] _hpTexts;

        [Header("[이펙트]")]
        [Tooltip("피격 이펙트 (HitParticle)")]
        [SerializeField] private GameObject _hitEffect;
        [Tooltip("파괴 이펙트 (EndParticle)")]
        [SerializeField] private GameObject _endEffect;

        [Header("[색상 적용 대상 — Inspector에서 할당]")]
        [Tooltip("색상 적용할 Renderer만")]
        [SerializeField] private Renderer[] _colorRenderers;
        [Tooltip("기반 Material. 복제하여 색상만 변경")]
        [SerializeField] private Material _baseMaterial;

        [Header("[Barricade 전용 — Gimmick Type이 Barricade일 때만 할당]")]
        [Tooltip("머리/베이스 메시 (Barricade)")]
        [SerializeField] private Transform _barricadeHead;
        [Tooltip("늘어나는 몸통 (BarricadeBody) — 미할당 시 이름으로 자동 탐색")]
        [SerializeField] private Transform _barricadeBody;
        [Tooltip("몸통 끝 마감 (Edge) — 할당 시 코드가 body 끝으로 이동")]
        [SerializeField] private Transform _barricadeEdge;

        /// <summary>현재 기믹 타입.</summary>
        public GimmickType Type => _gimmickType;

        /// <summary>Barricade 머리/베이스 (Inspector 할당, 선택).</summary>
        public Transform BarricadeHead => _barricadeHead;
        /// <summary>Barricade 늘어나는 몸통 (Inspector 할당, 선택).</summary>
        public Transform BarricadeBody => _barricadeBody;
        /// <summary>Barricade 끝 마감 (Inspector 할당, 선택).</summary>
        public Transform BarricadeEdge => _barricadeEdge;

        /// <summary>색상 적용 대상이 할당되었는지.</summary>
        public bool HasColorRenderers => _colorRenderers != null && _colorRenderers.Length > 0;

        private static readonly System.Collections.Generic.Dictionary<int, Material> _matCache
            = new System.Collections.Generic.Dictionary<int, Material>();

        /// <summary>초기화 — 이펙트 비활성, HP 숨김.</summary>
        public void Initialize()
        {
            if (_hitEffect != null) _hitEffect.SetActive(false);
            if (_endEffect != null) _endEffect.SetActive(false);
            if (_hpTexts != null)
            {
                for (int i = 0; i < _hpTexts.Length; i++)
                {
                    if (_hpTexts[i] != null) _hpTexts[i].gameObject.SetActive(false);
                }
            }
        }

        /// <summary>HP 텍스트 표시 + 갱신. 할당된 모든 _hpTexts 항목을 동기 갱신.</summary>
        public void UpdateHP(int hp)
        {
            if (_hpTexts != null)
            {
                for (int i = 0; i < _hpTexts.Length; i++)
                {
                    if (_hpTexts[i] != null)
                    {
                        _hpTexts[i].gameObject.SetActive(true);
                        _hpTexts[i].SetText("{0}", hp);
                    }
                }
            }
        }

        /// <summary>피격 이펙트 재생.</summary>
        public void PlayHitEffect()
        {
            if (_hitEffect != null)
            {
                _hitEffect.SetActive(false);
                _hitEffect.SetActive(true);
            }
        }

        /// <summary>파괴 이펙트 재생.</summary>
        public void PlayEndEffect()
        {
            if (_endEffect != null)
                _endEffect.SetActive(true);
        }

        /// <summary>색상 Material 적용.</summary>
        public void ApplyColor(Color color)
        {
            if (_colorRenderers == null || _colorRenderers.Length == 0) return;

            Material mat;
            if (_baseMaterial != null)
            {
                int key = _baseMaterial.GetInstanceID() ^ color.GetHashCode();
                if (!_matCache.TryGetValue(key, out mat))
                {
                    mat = new Material(_baseMaterial);
                    mat.SetColor("_BaseColor", color);
                    // [Optimization 2026-05-10 revert] GPU Instancing 채택.
                    mat.enableInstancing = true;
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
                if (_colorRenderers[i] != null)
                    _colorRenderers[i].sharedMaterial = mat;
            }
        }

        /// <summary>GimmickType enum → BalloonController 문자열 상수 변환.</summary>
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

        /// <summary>FlexTubePart enum ↔ 직렬화용 문자열 변환.</summary>
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
