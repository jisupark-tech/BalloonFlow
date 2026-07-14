using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BalloonFlow
{
    /// <summary>
    /// 같은 GameObject 의 TMP_Text(우선) 또는 legacy UI.Text 에 CSV Key 의 텍스트를 적용.
    /// OnEnable 마다 적용 → 풀링/재사용·언어 전환에 자동 대응.
    /// Key 선택은 인스펙터 드롭다운(UITextDrawer)으로. 빈 Key 면 아무것도 하지 않음(기존 텍스트 유지).
    /// 자동 부착: BalloonFlow > Localization > Migrate &amp; Attach UIText (수동 메뉴).
    /// </summary>
    [DisallowMultipleComponent]
    public class UIText : MonoBehaviour
    {
        [SerializeField] private string _key;

        // ROLLBACK_LOCALIZATION_KO_FONT_20260713: 언어별 폰트 스왑 — 프리팹 원본(EN/Poppins) 1회 캐시.
        private TMP_FontAsset _origFont;
        private Material _origMat;
        private bool _fontCaptured;

        // KO 폰트 로드는 LocalizationFont 로 일원화(코드-세팅 경로와 규약 공유).

        public string Key
        {
            get => _key;
            set { _key = value; Apply(); }
        }

        private void OnEnable()
        {
            LocalizationService.OnLanguageChanged += Apply;
            Apply();
        }

        private void OnDisable()
        {
            LocalizationService.OnLanguageChanged -= Apply;
        }

        /// <summary>현재 언어 텍스트를 컴포넌트에 적용.</summary>
        public void Apply()
        {
            if (string.IsNullOrEmpty(_key)) return;
            string s = LocalizationService.Get(_key);

            var tmp = GetComponent<TMP_Text>();
            var ui = tmp == null ? GetComponent<Text>() : null;

            // ROLLBACK_LOCALIZATION_KO_FONT_20260713: 텍스트 세팅 전에 언어별 폰트 스왑(KO=Chiron / EN=Poppins). legacy Text 는 스킵.
            if (tmp != null) ApplyFont(tmp);

            // [{n} 가드 2026-06-16] 치환되지 않은 동적 placeholder({n}/{0} 등)를 포함한 키는
            //   런타임 값을 코드(GetWith / string.Format / 직접 .text)가 채운다. UIText 가 OnEnable 마다
            //   raw "{n}" 으로 덮어쓰면 풀 재사용·재활성 시 코드가 채운 가격/금액 값을 placeholder 로 되돌려버린다.
            //   → placeholder 포함 시 적용 스킵(코드 소유). 단 현재 텍스트도 placeholder 면(코드 미주입 상태)
            //     빈 값으로 비워 raw "{n}" 노출 자체를 차단. (CSV 의 '{' 는 전부 포맷 토큰 — 컬러태그는 '<'.)
            if (s.IndexOf('{') >= 0)
            {
                if (tmp != null && tmp.text != null && tmp.text.IndexOf('{') >= 0) tmp.text = string.Empty;
                else if (ui != null && ui.text != null && ui.text.IndexOf('{') >= 0) ui.text = string.Empty;
                return;
            }

            if (tmp != null) { tmp.text = s; return; }
            if (ui != null) ui.text = s;
        }

        // ─── ROLLBACK_LOCALIZATION_KO_FONT_20260713: 언어별 폰트 스왑 ───

        /// <summary>현재 언어에 맞춰 TMP 폰트/머티리얼 스왑. KO = ChironGoRoundTC-Black(같은 Outline 변형),
        ///   그 외 = 프리팹 원본(Poppins). Poppins 기반 텍스트만 스왑(숫자/타 폰트는 원본 유지).</summary>
        private void ApplyFont(TMP_Text tmp)
        {
            // ROLLBACK_FONT_SWAP_REMOVED_20260714: Poppins-Bold SDF 에 ChironGoRoundTC-Black SDF Fallback 추가로
            //   한글은 TMP fallback 이 자동 렌더 → 언어별 폰트 스왑 불필요(아웃라인/fill 이격도 사라짐). no-op.
            //   (텍스트 세팅은 Apply 가 계속 수행. 롤백: 이 메서드 본문을 이전 스왑 로직으로 복원.)
        }

        private static TMP_FontAsset LoadKoFont() => LocalizationFont.LoadKoFont();

        /// <summary>Poppins-Bold-{Outline} → ChironGoRoundTC-Black-{Outline} 매핑.
        ///   ROLLBACK_OUTLINE_LANG_COLORKEEP_20260714: 공유 로직 위임 — 이름 규약 프리셋 우선,
        ///   없으면 EN 색/아웃라인 유지한 채 아틀라스만 KO 폰트로 재타겟(색상 보존, 블랙 기본으로 안 떨어짐).</summary>
        private static Material MapKoMaterial(Material origMat, TMP_FontAsset koFont)
            => UIOutlineStyle.MaterialForFont(origMat, koFont, "ChironGoRoundTC-Black");

#if UNITY_EDITOR
        /// <summary>에디터에서 Key 변경/프리필 시 즉시 미리보기.</summary>
        private void OnValidate()
        {
            if (!Application.isPlaying && !string.IsNullOrEmpty(_key))
                Apply();
        }
#endif
    }
}
