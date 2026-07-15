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

        // ROLLBACK_TMP_ADAPTER_SET_BASE_20260715: 런타임에 기준 프리셋 머티리얼 교체(즉시 재적용 + 언어전환에도 유지).
        //   외부(예: 1000코인 가격 아웃라인 = Poppins-Bold-BrownOutline)에서 어댑터가 붙은 텍스트의 Material Preset 을
        //   되돌림 없이 바꾸려면 fontSharedMaterial 직접 대입 대신 이 API 를 사용(어댑터가 다음 Apply 에서 안 덮음).
        public void SetBaseMaterial(Material mat)
        {
            _sharedBaseMaterial = mat;
            Capture();
            Apply();
        }

        // ROLLBACK_FONT_SWAP_REMOVED_20260714: Poppins-Bold SDF 에 Chiron Fallback 추가로 언어별 폰트 스왑 불필요.
        //   폰트/자식 스왑 제거 — _sharedBaseMaterial 을 fontSharedMaterial 에 핀만(배칭 유지, 원래 목적). 한글은 fallback 렌더.
        private void Apply()
        {
            if (_tmp == null) return;
            Material mat = _sharedBaseMaterial;
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
    }
}
