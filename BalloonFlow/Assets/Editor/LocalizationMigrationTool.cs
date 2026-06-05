using System.Collections.Generic;
using System.Text;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace BalloonFlow.EditorTools
{
    /// <summary>
    /// 1회성 마이그레이션: 기존 4컬럼 CSV(SuggestedKey,AssetPath,ObjectPath,Text_EN) →
    ///   (1) Key,Text_EN 2컬럼 CSV (중복 Key = 첫 항목 유지·나머지 드롭)
    ///   (2) Resources/UI · Resources/Popup 프리팹의 TMP/Text GameObject 에 UIText 부착 + Key 프리필
    ///
    /// ⚠ 프리팹을 SaveAsPrefabAsset 으로 수정하므로 반드시 수동 메뉴로만 실행하고, 결과를 본인이 커밋.
    ///    (InitializeOnLoad 자동실행 금지 — 아트팀 머신 프리팹 덮어쓰기 사고 방지.)
    /// </summary>
    public static class LocalizationMigrationTool
    {
        private const string CSV_ASSET_PATH = "Assets/Resources/TextData/TextData.csv";
        private const string ASSET_PREFIX = "Assets/Resources/";
        private const string PREFAB_SUFFIX = ".prefab";
        private static readonly string[] PREFAB_ROOTS = { "Assets/Resources/UI", "Assets/Resources/Popup" };

        [MenuItem("BalloonFlow/Localization/Migrate & Attach UIText", priority = 200)]
        public static void Run()
        {
            string raw = ReadCsvRaw();
            if (raw == null) return;

            List<string[]> rows = ParseCsvRows(raw);
            if (rows.Count == 0) { EditorUtility.DisplayDialog("Localization", "CSV 가 비어 있습니다.", "OK"); return; }

            // 포맷 감지 — 이미 마이그레이션됐으면(헤더에 AssetPath 없음) 중단.
            string[] header = rows[0];
            bool isOldFormat = System.Array.Exists(header, h => (h ?? "").Trim().Equals("AssetPath", System.StringComparison.OrdinalIgnoreCase));
            if (!isOldFormat)
            {
                EditorUtility.DisplayDialog("Localization",
                    "CSV 가 이미 Key 기반(2컬럼)으로 보입니다. 마이그레이션은 1회만 실행하세요.\n" +
                    "프리팹 재부착만 필요하면 별도 메뉴를 쓰거나, 기존 4컬럼 CSV 를 git 에서 복구 후 재실행하세요.", "OK");
                return;
            }

            // 컬럼 index (old: SuggestedKey, AssetPath, ObjectPath, Text_EN)
            int cKey = IndexOf(header, "SuggestedKey", 0);
            int cAsset = IndexOf(header, "AssetPath", 1);
            int cObj = IndexOf(header, "ObjectPath", 2);
            int cText = IndexOf(header, "Text_EN", 3);

            // 사전 구성
            var keyToText = new Dictionary<string, string>(rows.Count);          // Key → EN (첫 항목 유지)
            var pathToKey = new Dictionary<string, string>(rows.Count);          // resourceKey|relPath → Key (첫 항목 유지)
            int dupDropped = 0;
            var keyOrder = new List<string>(rows.Count);

            for (int r = 1; r < rows.Count; r++)
            {
                string[] f = rows[r];
                if (cKey >= f.Length) continue;
                string key = (f[cKey] ?? "").Trim();
                if (string.IsNullOrEmpty(key)) continue;
                string text = cText < f.Length ? f[cText] : "";

                if (!keyToText.ContainsKey(key)) { keyToText[key] = text; keyOrder.Add(key); }
                else dupDropped++;

                string resourceKey = AssetPathToResourceKey(cAsset < f.Length ? f[cAsset] : null);
                string relPath = StripRoot(cObj < f.Length ? f[cObj] : null);
                if (!string.IsNullOrEmpty(resourceKey))
                {
                    string pk = resourceKey + "|" + relPath;
                    if (!pathToKey.ContainsKey(pk)) pathToKey[pk] = key;
                }
            }

            bool ok = EditorUtility.DisplayDialog("Localization 마이그레이션",
                $"기존 {rows.Count - 1}행 → Key {keyToText.Count}개 (중복 {dupDropped}개 드롭).\n\n" +
                $"수행:\n" +
                $"  1) {PREFAB_ROOTS.Length}개 폴더의 프리팹 TMP/Text 에 UIText 부착 + Key 프리필\n" +
                $"  2) CSV 를 Key,Text_EN 2컬럼으로 덮어쓰기\n\n" +
                $"프리팹이 수정·저장됩니다. 계속할까요?", "실행", "취소");
            if (!ok) return;

            // (1) 프리팹 부착
            int prefabsTouched = 0, comps = 0, prefilled = 0, unmatched = 0;
            string[] guids = AssetDatabase.FindAssets("t:Prefab", PREFAB_ROOTS);
            try
            {
                for (int i = 0; i < guids.Length; i++)
                {
                    string path = AssetDatabase.GUIDToAssetPath(guids[i]);
                    EditorUtility.DisplayProgressBar("Attach UIText", path, (float)i / Mathf.Max(1, guids.Length));
                    string resourceKey = AssetPathToResourceKey(path);
                    if (string.IsNullOrEmpty(resourceKey)) continue;

                    GameObject root = PrefabUtility.LoadPrefabContents(path);
                    bool changed = false;
                    try
                    {
                        var texts = new List<Component>();
                        texts.AddRange(root.GetComponentsInChildren<TMP_Text>(true));
                        texts.AddRange(root.GetComponentsInChildren<Text>(true));

                        foreach (var comp in texts)
                        {
                            var go = comp.gameObject;
                            if (go.GetComponent<UIText>() != null) continue;   // 이미 있음

                            var ut = go.AddComponent<UIText>();
                            comps++;
                            changed = true;

                            string relPath = RelPath(root.transform, go.transform);
                            if (pathToKey.TryGetValue(resourceKey + "|" + relPath, out string key))
                            {
                                var so = new SerializedObject(ut);
                                so.FindProperty("_key").stringValue = key;
                                so.ApplyModifiedPropertiesWithoutUndo();
                                prefilled++;
                            }
                            else unmatched++;
                        }

                        if (changed)
                        {
                            PrefabUtility.SaveAsPrefabAsset(root, path);
                            prefabsTouched++;
                        }
                    }
                    finally { PrefabUtility.UnloadPrefabContents(root); }
                }
            }
            finally { EditorUtility.ClearProgressBar(); }

            // (2) CSV 덮어쓰기 (Key 정렬 — 안정적 diff)
            keyOrder.Sort(System.StringComparer.Ordinal);
            var sb = new StringBuilder(keyOrder.Count * 32);
            sb.Append("\"Key\",\"Text_EN\"\n");
            foreach (var key in keyOrder)
                sb.Append(Csv(key)).Append(',').Append(Csv(keyToText[key])).Append('\n');
            System.IO.File.WriteAllText(CSV_ASSET_PATH, sb.ToString(), new UTF8Encoding(true)); // BOM
            AssetDatabase.ImportAsset(CSV_ASSET_PATH);
            AssetDatabase.Refresh();
            LocalizationService.Reload();

            Debug.Log($"[Localization] 마이그레이션 완료 — Key {keyToText.Count}개(중복 {dupDropped} 드롭) / " +
                      $"프리팹 {prefabsTouched}개 수정, UIText {comps}개 부착(프리필 {prefilled} / 미매칭 {unmatched}).");
            EditorUtility.DisplayDialog("Localization",
                $"완료.\nKey {keyToText.Count}개 · 프리팹 {prefabsTouched}개 · UIText {comps}개(프리필 {prefilled}/미매칭 {unmatched}).\n" +
                $"미매칭 {unmatched}개는 인스펙터에서 Key 를 직접 골라주세요.\n결과 프리팹/CSV 를 커밋하세요.", "OK");
        }

        // ─── 경로 유틸 (구 LocalizationService 와 동일 규칙) ───

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

        private static string StripRoot(string objectPath)
        {
            if (string.IsNullOrEmpty(objectPath)) return "";
            int slash = objectPath.IndexOf('/');
            return slash < 0 ? "" : objectPath.Substring(slash + 1);
        }

        /// <summary>root 의 자식들 기준 상대 경로(root 이름 제외) — StripRoot 결과와 동일 형식.</summary>
        private static string RelPath(Transform root, Transform t)
        {
            var stack = new List<string>(8);
            var cur = t;
            while (cur != null && cur != root) { stack.Add(cur.name); cur = cur.parent; }
            stack.Reverse();
            return string.Join("/", stack);
        }

        private static int IndexOf(string[] header, string name, int fallback)
        {
            for (int i = 0; i < header.Length; i++)
                if ((header[i] ?? "").Trim().Equals(name, System.StringComparison.OrdinalIgnoreCase)) return i;
            return fallback;
        }

        private static string Csv(string s)
        {
            s ??= "";
            return "\"" + s.Replace("\"", "\"\"") + "\"";
        }

        private static string ReadCsvRaw()
        {
            if (!System.IO.File.Exists(CSV_ASSET_PATH))
            {
                EditorUtility.DisplayDialog("Localization", $"CSV 없음: {CSV_ASSET_PATH}", "OK");
                return null;
            }
            return System.IO.File.ReadAllText(CSV_ASSET_PATH);
        }

        // RFC-4180 최소 파서 (LocalizationService 와 동일).
        private static List<string[]> ParseCsvRows(string text)
        {
            var rows = new List<string[]>(1024);
            if (string.IsNullOrEmpty(text)) return rows;
            var fields = new List<string>(8);
            var sb = new StringBuilder(64);
            bool inQuotes = false;
            int i = 0;
            if (text[0] == '﻿') i = 1;
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
                    else if (c == '\r') { }
                    else if (c == '\n') { fields.Add(sb.ToString()); sb.Clear(); rows.Add(fields.ToArray()); fields.Clear(); }
                    else sb.Append(c);
                }
            }
            if (sb.Length > 0 || fields.Count > 0) { fields.Add(sb.ToString()); rows.Add(fields.ToArray()); }
            return rows;
        }
    }
}
