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
            SpawnerO
        }

        [Header("[기믹 타입 선택]")]
        [SerializeField] private GimmickType _gimmickType = GimmickType.None;

        [Header("[HP 표시 — Pinata/PinataBox/Pin/Ice용]")]
        [Tooltip("HP 텍스트 (MagazineText)")]
        [SerializeField] private TMPro.TMP_Text _hpText;

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

        /// <summary>현재 기믹 타입.</summary>
        public GimmickType Type => _gimmickType;

        /// <summary>색상 적용 대상이 할당되었는지.</summary>
        public bool HasColorRenderers => _colorRenderers != null && _colorRenderers.Length > 0;

        private static readonly System.Collections.Generic.Dictionary<int, Material> _matCache
            = new System.Collections.Generic.Dictionary<int, Material>();

        /// <summary>초기화 — 이펙트 비활성, HP 숨김.</summary>
        public void Initialize()
        {
            if (_hitEffect != null) _hitEffect.SetActive(false);
            if (_endEffect != null) _endEffect.SetActive(false);
            if (_hpText != null) _hpText.gameObject.SetActive(false);
        }

        /// <summary>HP 텍스트 표시 + 갱신.</summary>
        public void UpdateHP(int hp)
        {
            if (_hpText != null)
            {
                _hpText.gameObject.SetActive(true);
                _hpText.text = hp.ToString();
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
                default:                       return "";
            }
        }
    }
}
