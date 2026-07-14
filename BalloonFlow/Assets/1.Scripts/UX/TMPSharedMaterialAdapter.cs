using TMPro;
using UnityEngine;

namespace BalloonFlow.UX
{
    /// <summary>
    /// TMP_Text 에 배칭 친화적으로 shared 프리셋 머티리얼을 지정하고,
    /// 현재 언어에 맞는 폰트 패밀리 버전으로 동적 전환한다(UIHud 아웃라인과 동일 메커니즘).
    ///
    /// ROLLBACK_TMP_ADAPTER_LANG_MATERIAL_20260714: 언어 분기.
    ///   - fontMaterial(인스턴스) 대신 fontSharedMaterial 만 사용 → 배칭 유지.
    ///   - _sharedBaseMaterial(예 "Poppins-Bold-BlueOutline")을 UIOutlineStyle.ResolveLanguageOutline 로
    ///     현재 언어의 폰트 패밀리(예 KO=ChironGoRoundTC-Black) 동일 접미 프리셋 + 그 폰트로 치환.
    ///     (한글은 폴백이 아니라 폰트 자체를 교체해야 아웃라인이 정상 적용됨.)
    ///   - LocalizationService.OnLanguageChanged 구독 → 런타임 언어 전환 시 자동 재적용.
    ///   롤백: 이벤트 구독/Resolve 제거 후 _sharedBaseMaterial 1회 적용으로 복원.
    /// </summary>
    [DisallowMultipleComponent]
    public class TMPSharedMaterialAdapter : MonoBehaviour
    {
        [Tooltip("기준 프리셋 머티리얼(네이밍 규약 \"{FontFamily}-{Color}Outline\"). 언어에 따라 같은 접미의 다른 폰트 패밀리로 자동 치환됨.")]
        [SerializeField] private Material _sharedBaseMaterial;

        [Tooltip("언어 무관 고정(예: 배속 x1/x2, 아이템 수량/락 — 항상 영어). 켜면 언어 스왑 안 하고 base 머티리얼 패밀리 폰트로 맞춤.")]
        [SerializeField] private bool _ignoreLanguage;

        [Tooltip("자식 TMP(예: 아웃라인 밑의 fill 텍스트) 폰트도 같은 폰트로 동기화. 아웃라인/fill 이격 방지(기본 ON).")]
        [SerializeField] private bool _syncChildrenFont = true;

        private TMP_Text _tmp;
        private Material _originalMaterial;
        private bool _captured;

        private void Awake() => Capture();

        private void OnEnable()
        {
            Capture();
            LocalizationService.OnLanguageChanged += Apply;
            Apply();
        }

        private void OnDisable()
        {
            LocalizationService.OnLanguageChanged -= Apply;
        }

        private void Capture()
        {
            if (_captured) return;
            _tmp = GetComponent<TMP_Text>();
            if (_tmp == null) return;
            _originalMaterial = _tmp.fontSharedMaterial; // base 미지정 시 기준
            _captured = true;
        }

        private void Apply()
        {
            if (_tmp == null) return;

            Material baseMat = _sharedBaseMaterial != null ? _sharedBaseMaterial : _originalMaterial;

            Material mat;
            TMP_FontAsset font;
            if (_ignoreLanguage)
            {
                // 언어 무관 고정: base 머티리얼 그대로 + 그 패밀리 폰트로 맞춤(폰트↔머티리얼 불일치 방지).
                mat = baseMat;
                font = UIOutlineStyle.FontAssetForMaterial(baseMat);
            }
            else
            {
                mat = UIOutlineStyle.ResolveLanguageOutline(baseMat, out font);
            }

            if (font != null && _tmp.font != font) _tmp.font = font; // 폰트 먼저(setter 가 material 리셋)

            // 자식 fill 텍스트(아웃라인 밑 Txt)도 같은 폰트로 → 이격 방지. fill 이 UIText/Adapter 없어도 여기서 커버.
            if (_syncChildrenFont && font != null) SyncChildrenFont(font);

            if (mat == null) return;

            Material cur = _tmp.fontSharedMaterial;
            if (cur == mat) return;
            if (cur != null && mat.shader != cur.shader)
            {
                Debug.LogWarning($"[TMPSharedMaterialAdapter] {name}: shader mismatch (cur:{cur.shader?.name} new:{mat.shader?.name}). Skipping.");
                return;
            }

            _tmp.fontSharedMaterial = mat;
            _tmp.SetMaterialDirty();
        }

        // 자식 TMP 들의 폰트를 동일하게(자기 자신 제외). 머티리얼은 폰트 기본값을 따라감(fill 은 보통 plain).
        private void SyncChildrenFont(TMP_FontAsset font)
        {
            var kids = GetComponentsInChildren<TMP_Text>(true);
            for (int i = 0; i < kids.Length; i++)
            {
                var k = kids[i];
                if (k == _tmp || k == null) continue;
                if (k.font != font) k.font = font;
            }
        }
    }
}
