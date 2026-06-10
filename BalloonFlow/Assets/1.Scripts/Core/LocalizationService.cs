using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace BalloonFlow
{
    /// <summary>
    /// Key 기반 로컬라이징 사전. Resources/TextData/TextData.csv 를 1회 로드해 메모리에 보관.
    /// CSV 컬럼: Key, Text_EN (+ Text_KO, Text_JA ... 나라 추가 시 컬럼만 늘리면 됨).
    ///
    /// 사용:
    ///   - UI: 텍스트 GameObject 에 <see cref="UIText"/> 컴포넌트를 붙이고 Key 지정 → OnEnable 에 자동 적용.
    ///   - 코드/튜토리얼: LocalizationService.Get("some.key") 로 직접 조회.
    ///
    /// 언어 분기(1.1+): SetLanguageByCode("KO") 로 컬럼 전환 → OnLanguageChanged 로 모든 UIText 갱신.
    /// 1.0 = EN 전용(_lang=0). 키 미존재 시 key 문자열을 그대로 반환(누락이 화면에 바로 보이게).
    /// </summary>
    public static class LocalizationService
    {
        private const string CSV_RESOURCE = "TextData/TextData";

        // key → 언어별 텍스트 배열([0]=EN, [1]=KO, ...). 컬럼 순서는 헤더의 Text_* 등장 순서.
        private static Dictionary<string, string[]> _byKey;
        private static string[] _langCodes;   // ["EN","KO",...] — Text_ 접두 제거한 코드
        private static int _lang = 0;          // 현재 언어 슬롯 index

        /// <summary>언어가 바뀌면 발생 — UIText 들이 구독해 즉시 재적용.</summary>
        public static event System.Action OnLanguageChanged;

        // ─── 조회 ──────────────────────────────────────────────────

        /// <summary>현재 언어 텍스트. 없으면 EN fallback, 그것도 없으면 key 그대로.</summary>
        public static string Get(string key)
        {
            if (string.IsNullOrEmpty(key)) return string.Empty;
            EnsureLoaded();
            if (_byKey != null && _byKey.TryGetValue(key, out string[] vals) && vals != null && vals.Length > 0)
            {
                if (_lang >= 0 && _lang < vals.Length && !string.IsNullOrEmpty(vals[_lang]))
                    return vals[_lang];
                return vals[0] ?? key;   // 해당 언어 비었으면 EN(슬롯0)
            }
            return key;                  // 미존재 → key 노출(진단 용이)
        }

        public static string GetWith(string key, string token, object value)
        {
            string text = Get(key);
            if (string.IsNullOrEmpty(token)) return text;
            string replacement = value != null ? value.ToString() : string.Empty;
            return text.Replace("{" + token + "}", replacement);
        }

        public static bool Has(string key)
        {
            if (string.IsNullOrEmpty(key)) return false;
            EnsureLoaded();
            return _byKey != null && _byKey.ContainsKey(key);
        }

        /// <summary>에디터 드롭다운용 — 모든 Key. (런타임에서도 안전)</summary>
        public static IReadOnlyCollection<string> AllKeys
        {
            get { EnsureLoaded(); return _byKey != null ? (IReadOnlyCollection<string>)_byKey.Keys : System.Array.Empty<string>(); }
        }

        // ─── 언어 전환 ─────────────────────────────────────────────

        /// <summary>"EN","KO" 등 헤더 Text_&lt;CODE&gt; 의 CODE 로 언어 선택. 없으면 EN 유지.</summary>
        public static void SetLanguageByCode(string code)
        {
            EnsureLoaded();
            if (_langCodes == null || string.IsNullOrEmpty(code)) return;
            for (int i = 0; i < _langCodes.Length; i++)
            {
                if (string.Equals(_langCodes[i], code, System.StringComparison.OrdinalIgnoreCase))
                {
                    if (_lang != i) { _lang = i; OnLanguageChanged?.Invoke(); }
                    return;
                }
            }
            Debug.LogWarning($"[Localization] 언어 코드 '{code}' 없음 — EN 유지. (가용: {string.Join(",", _langCodes)})");
        }

        public static string CurrentLanguageCode
            => (_langCodes != null && _lang >= 0 && _lang < _langCodes.Length) ? _langCodes[_lang] : "EN";

        // ─── CSV 로드/파싱 ─────────────────────────────────────────

        /// <summary>강제 재로드(에디터 마이그레이션 직후 등).</summary>
        public static void Reload() { _byKey = null; _langCodes = null; EnsureLoaded(); OnLanguageChanged?.Invoke(); }

        private static void EnsureLoaded()
        {
            if (_byKey != null) return;
            _byKey = new Dictionary<string, string[]>(512);

            var ta = Resources.Load<TextAsset>(CSV_RESOURCE);
            if (ta == null)
            {
                Debug.LogWarning($"[Localization] Resources/{CSV_RESOURCE}.csv not found — UI 텍스트 자동세팅 skip.");
                _langCodes = new[] { "EN" };
                return;
            }

            List<string[]> rows = ParseCsvRows(ta.text);
            if (rows.Count == 0) { _langCodes = new[] { "EN" }; return; }

            // 헤더 파싱: Key 컬럼 + Text_* 언어 컬럼들.
            string[] header = rows[0];
            int keyCol = -1;
            var langCols = new List<int>();
            var langCodes = new List<string>();
            for (int c = 0; c < header.Length; c++)
            {
                string h = TrimBom(header[c]).Trim();
                if (keyCol < 0 && h.Equals("Key", System.StringComparison.OrdinalIgnoreCase)) { keyCol = c; continue; }
                if (h.StartsWith("Text_", System.StringComparison.OrdinalIgnoreCase))
                {
                    langCols.Add(c);
                    langCodes.Add(h.Substring("Text_".Length));
                }
            }
            if (keyCol < 0) keyCol = 0;                       // 헤더에 Key 없으면 0번 컬럼으로 가정
            if (langCols.Count == 0) { langCols.Add(1); langCodes.Add("EN"); }  // 최소 1개 언어 보장
            _langCodes = langCodes.ToArray();

            for (int r = 1; r < rows.Count; r++)
            {
                string[] f = rows[r];
                if (keyCol >= f.Length) continue;
                string key = TrimBom(f[keyCol]).Trim();
                if (string.IsNullOrEmpty(key)) continue;
                if (_byKey.ContainsKey(key)) continue;        // 중복 Key → 첫 항목 유지(드롭)

                var vals = new string[langCols.Count];
                for (int l = 0; l < langCols.Count; l++)
                    vals[l] = langCols[l] < f.Length ? f[langCols[l]] : "";
                _byKey[key] = vals;
            }
        }

        private static string TrimBom(string value)
        {
            return string.IsNullOrEmpty(value) ? string.Empty : value.TrimStart('\uFEFF');
        }

        /// <summary>RFC-4180 최소 파서 — 따옴표 필드 내 콤마/줄바꿈/이스케이프("") 지원.</summary>
        private static List<string[]> ParseCsvRows(string text)
        {
            var rows = new List<string[]>(1024);
            if (string.IsNullOrEmpty(text)) return rows;

            var fields = new List<string>(8);
            var sb = new StringBuilder(64);
            bool inQuotes = false;
            int i = 0;
            if (text[0] == '\uFEFF') i = 1; // BOM 제거

            for (; i < text.Length; i++)
            {
                char c = text[i];
                if (inQuotes)
                {
                    if (c == '"')
                    {
                        if (i + 1 < text.Length && text[i + 1] == '"') { sb.Append('"'); i++; }
                        else inQuotes = false;
                    }
                    else sb.Append(c);
                }
                else
                {
                    if (c == '"') inQuotes = true;
                    else if (c == ',') { fields.Add(sb.ToString()); sb.Clear(); }
                    else if (c == '\r') { /* skip — \n 에서 행 종료 */ }
                    else if (c == '\n')
                    {
                        fields.Add(sb.ToString()); sb.Clear();
                        rows.Add(fields.ToArray()); fields.Clear();
                    }
                    else sb.Append(c);
                }
            }
            if (sb.Length > 0 || fields.Count > 0)
            {
                fields.Add(sb.ToString());
                rows.Add(fields.ToArray());
            }
            return rows;
        }
    }
}
