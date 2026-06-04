using System.Collections.Generic;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BalloonFlow
{
    /// <summary>
    /// CSV(Resources/TextData/TextData.csv) 기반 UI 텍스트 자동 세팅.
    /// 컬럼: SuggestedKey, AssetPath, ObjectPath, Text_EN (+ 나라 베리에이션 컬럼 예정).
    ///
    /// 매칭(프리팹/컴포넌트 무수정):
    ///   - AssetPath("Assets/Resources/UI/UILobby.prefab") → Resources 키("UI/UILobby") = UIManager.OpenUI 의 path
    ///   - ObjectPath("UILobby/.../TxtX") → 첫 세그먼트(루트명) 제거 후 인스턴스 루트 기준 Transform.Find
    ///
    /// 사용: UI 프리팹 인스턴스화 직후 LocalizationService.Apply(rootGO, resourcePath).
    /// 1.0 = Text_EN(EN 전용). 1.1+ 언어 분기는 SetLanguage 로 컬럼 선택 확장.
    /// </summary>
    public static class LocalizationService
    {
        private const string CSV_RESOURCE  = "TextData/TextData";
        private const string ASSET_PREFIX  = "Assets/Resources/";
        private const string PREFAB_SUFFIX = ".prefab";

        private struct Entry { public string sub; public string text; }

        // resourceKey("UI/UILobby") → [(루트 제거 경로, 텍스트)]
        private static Dictionary<string, List<Entry>> _byResource;

        /// <summary>인스턴스화된 UI 루트에 CSV 텍스트를 자동 적용. resourcePath = OpenUI 에 넘긴 키("UI/..","Popup/..").</summary>
        public static void Apply(GameObject root, string resourcePath)
        {
            if (root == null || string.IsNullOrEmpty(resourcePath)) return;
            EnsureLoaded();
            if (_byResource == null || !_byResource.TryGetValue(resourcePath, out List<Entry> entries))
            {
                Debug.Log($"[Localization] {resourcePath}: CSV 엔트리 없음 — 매핑 skip.");
                return;
            }

            int applied = 0;
            List<string> missed = null; // 경로 못 찾음/Text 컴포넌트 없음 — 진단용
            for (int i = 0; i < entries.Count; i++)
            {
                Transform t = string.IsNullOrEmpty(entries[i].sub)
                    ? root.transform
                    : root.transform.Find(entries[i].sub);
                if (t != null && SetText(t, entries[i].text))
                {
                    applied++;
                }
                else
                {
                    (missed ??= new List<string>()).Add(entries[i].sub);
                }
            }

            // [검증 로그] UI 별 매핑 결과. EN 은 화면상 동일(idempotent)이라 콘솔로 동작 확인.
            if (missed == null)
                Debug.Log($"[Localization] {resourcePath}: {applied}/{entries.Count} text 매핑 완료 ✓");
            else
                Debug.LogWarning($"[Localization] {resourcePath}: {applied}/{entries.Count} 매핑, {missed.Count} 누락(경로/컴포넌트 불일치): {string.Join(" | ", missed)}");
        }

        /// <summary>TMP 우선, 없으면 legacy UI.Text 에 세팅. 적용 성공 시 true.</summary>
        private static bool SetText(Transform t, string text)
        {
            var tmp = t.GetComponent<TMP_Text>();
            if (tmp != null) { tmp.text = text; return true; }
            var ui = t.GetComponent<Text>();
            if (ui != null) { ui.text = text; return true; }
            return false;
        }

        // ─── CSV 로드/파싱 ──────────────────────────────────────────

        private static void EnsureLoaded()
        {
            if (_byResource != null) return;
            _byResource = new Dictionary<string, List<Entry>>(256);

            var ta = Resources.Load<TextAsset>(CSV_RESOURCE);
            if (ta == null)
            {
                Debug.LogWarning($"[Localization] Resources/{CSV_RESOURCE}.csv not found — UI 텍스트 자동세팅 skip.");
                return;
            }

            List<string[]> rows = ParseCsvRows(ta.text);
            // 0행 = 헤더(SuggestedKey,AssetPath,ObjectPath,Text_EN). 데이터는 1행부터.
            for (int r = 1; r < rows.Count; r++)
            {
                string[] f = rows[r];
                if (f.Length < 4) continue;
                string resourceKey = AssetPathToResourceKey(f[1]);
                if (string.IsNullOrEmpty(resourceKey)) continue;

                if (!_byResource.TryGetValue(resourceKey, out List<Entry> list))
                {
                    list = new List<Entry>(8);
                    _byResource[resourceKey] = list;
                }
                list.Add(new Entry { sub = StripRoot(f[2]), text = f[3] });
            }
        }

        private static string AssetPathToResourceKey(string assetPath)
        {
            if (string.IsNullOrEmpty(assetPath)) return null;
            string k = assetPath.Replace('\\', '/');
            int idx = k.IndexOf(ASSET_PREFIX, System.StringComparison.Ordinal);
            if (idx >= 0) k = k.Substring(idx + ASSET_PREFIX.Length);
            if (k.EndsWith(PREFAB_SUFFIX, System.StringComparison.Ordinal))
                k = k.Substring(0, k.Length - PREFAB_SUFFIX.Length);
            return k;
        }

        /// <summary>ObjectPath 의 첫 세그먼트(루트 GO 명)를 제거 → 인스턴스 루트 기준 상대 경로.</summary>
        private static string StripRoot(string objectPath)
        {
            if (string.IsNullOrEmpty(objectPath)) return "";
            int slash = objectPath.IndexOf('/');
            return slash < 0 ? "" : objectPath.Substring(slash + 1);
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
            if (text[0] == '﻿') i = 1; // BOM 제거

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
            // 마지막 줄(개행 없이 끝나는 경우)
            if (sb.Length > 0 || fields.Count > 0)
            {
                fields.Add(sb.ToString());
                rows.Add(fields.ToArray());
            }
            return rows;
        }
    }
}
