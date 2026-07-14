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

        private const string KO_FONT_RES = "Fonts & Materials/ChironGoRoundTC-Black SDF";
        private static TMP_FontAsset _koFont;
        private static bool _koFontTried;
        private static readonly Dictionary<string, Material> _koMatCache = new Dictionary<string, Material>();

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
            if (!_fontCaptured)
            {
                _origFont = tmp.font;
                _origMat  = tmp.fontSharedMaterial;
                _fontCaptured = true;
            }
            if (_origFont == null || _origFont.name.IndexOf("Poppins", System.StringComparison.OrdinalIgnoreCase) < 0)
                return; // Poppins 아니면 스왑 안 함(숫자/기타 폰트 보호)

            if (LocalizationService.CurrentLanguageCode == "KO")
            {
                var koFont = LoadKoFont();
                if (koFont == null) return;                 // Chiron 없으면 원본 유지(크래시 방지)
                tmp.font = koFont;                          // 폰트 먼저(머티리얼 리셋) → 머티리얼 지정
                tmp.fontSharedMaterial = MapKoMaterial(_origMat, koFont);
            }
            else
            {
                tmp.font = _origFont;
                if (_origMat != null) tmp.fontSharedMaterial = _origMat;
            }
        }

        private static TMP_FontAsset LoadKoFont()
        {
            if (!_koFontTried)
            {
                _koFont = Resources.Load<TMP_FontAsset>(KO_FONT_RES);
                _koFontTried = true;
                if (_koFont == null) Debug.LogWarning($"[UIText] KO 폰트 없음: Resources/{KO_FONT_RES}");
            }
            return _koFont;
        }

        /// <summary>Poppins-Bold-{Outline} → ChironGoRoundTC-Black-{Outline} 매핑. 없으면 Chiron 기본 머티리얼.</summary>
        private static Material MapKoMaterial(Material origMat, TMP_FontAsset koFont)
        {
            if (origMat != null && origMat.name.IndexOf("Poppins-Bold", System.StringComparison.Ordinal) >= 0)
            {
                string koName = origMat.name.Replace("Poppins-Bold", "ChironGoRoundTC-Black");
                if (!_koMatCache.TryGetValue(koName, out var m))
                {
                    m = Resources.Load<Material>("Fonts & Materials/" + koName);
                    _koMatCache[koName] = m;                 // null 도 캐시(반복 로드 방지)
                }
                if (m != null) return m;
            }
            return koFont.material;                          // 매핑 실패 → Chiron 기본
        }

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
