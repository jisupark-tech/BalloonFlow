#if UNITY_EDITOR
using System;
using System.IO;
using System.Text;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;
using UnityEditor;
using UnityEditor.SceneManagement;
using TMPro;

namespace BalloonFlow.Editor
{
    /// <summary>
    /// 로컬라이징 1차 작업용 — 프로젝트 내 모든 prefab/scene 의 TMP_Text/UI.Text 를
    /// 순회해 CSV 로 export. 읽기 전용: prefab/scene 을 수정하거나 저장하지 않음.
    /// (UI prefab auto-build 금지 원칙 — InitializeOnLoad/SaveAsPrefabAsset 미사용)
    /// 메뉴: BalloonFlow > Localization > Extract All UI Text → CSV
    /// </summary>
    public static class LocalizationTextExtractor
    {
        private struct Row
        {
            public string SuggestedKey;
            public string SourceType; // Prefab | Scene
            public string AssetPath;
            public string ObjectPath;
            public string Component;  // TMP_Text | Text
            public string Text;
        }

        [MenuItem("BalloonFlow/Localization/Extract All UI Text → CSV")]
        public static void ExtractAll()
        {
            // 미저장 scene 변경이 있으면 scene 스캔이 작업을 날릴 수 있으므로 중단.
            for (int i = 0; i < EditorSceneManager.sceneCount; i++)
            {
                if (EditorSceneManager.GetSceneAt(i).isDirty)
                {
                    EditorUtility.DisplayDialog("중단",
                        "저장하지 않은 Scene 변경이 있습니다. 저장 후 다시 실행하세요.", "OK");
                    return;
                }
            }

            var rows = new List<Row>();

            try
            {
                ScanPrefabs(rows);
                ScanScenes(rows);
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }

            if (rows.Count == 0)
            {
                EditorUtility.DisplayDialog("결과 없음", "추출된 텍스트가 없습니다.", "OK");
                return;
            }

            string defaultName = $"UITextAudit_{DateTime.Now:yyyyMMdd_HHmmss}";
            string path = EditorUtility.SaveFilePanel("Export UI Text Audit",
                Path.GetDirectoryName(Application.dataPath), defaultName, "csv");
            if (string.IsNullOrEmpty(path)) return;

            WriteCsv(path, rows);

            Debug.Log($"[Loc] {rows.Count} text rows → {path}");
            EditorUtility.DisplayDialog("Extract 완료",
                $"{rows.Count}개 텍스트 추출됨\n\n{path}", "OK");
        }

        private static void ScanPrefabs(List<Row> rows)
        {
            string[] guids = AssetDatabase.FindAssets("t:Prefab");
            for (int i = 0; i < guids.Length; i++)
            {
                string assetPath = AssetDatabase.GUIDToAssetPath(guids[i]);
                if (EditorUtility.DisplayCancelableProgressBar("Scanning Prefabs",
                        assetPath, (float)i / guids.Length))
                    break;

                var go = AssetDatabase.LoadAssetAtPath<GameObject>(assetPath);
                if (go == null) continue;
                CollectFrom(go.transform, "Prefab", assetPath, rows);
            }
        }

        private static void ScanScenes(List<Row> rows)
        {
            string[] guids = AssetDatabase.FindAssets("t:Scene");
            var setup = EditorSceneManager.GetSceneManagerSetup();
            try
            {
                for (int i = 0; i < guids.Length; i++)
                {
                    string assetPath = AssetDatabase.GUIDToAssetPath(guids[i]);
                    // 패키지/임베디드 scene 제외
                    if (!assetPath.StartsWith("Assets/")) continue;
                    if (EditorUtility.DisplayCancelableProgressBar("Scanning Scenes",
                            assetPath, (float)i / guids.Length))
                        break;

                    var scene = EditorSceneManager.OpenScene(assetPath, OpenSceneMode.Single);
                    foreach (var root in scene.GetRootGameObjects())
                        CollectFrom(root.transform, "Scene", assetPath, rows);
                }
            }
            finally
            {
                if (setup != null && setup.Length > 0)
                    EditorSceneManager.RestoreSceneManagerSetup(setup);
            }
        }

        private static void CollectFrom(Transform root, string sourceType, string assetPath, List<Row> rows)
        {
            foreach (var tmp in root.GetComponentsInChildren<TMP_Text>(true))
                AddRow(rows, sourceType, assetPath, tmp.transform, "TMP_Text", tmp.text);

            foreach (var ui in root.GetComponentsInChildren<Text>(true))
                AddRow(rows, sourceType, assetPath, ui.transform, "Text", ui.text);
        }

        private static void AddRow(List<Row> rows, string sourceType, string assetPath,
            Transform t, string component, string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return;

            string objectPath = GetHierarchyPath(t);
            rows.Add(new Row
            {
                SuggestedKey = SuggestKey(assetPath, objectPath),
                SourceType = sourceType,
                AssetPath = assetPath,
                ObjectPath = objectPath,
                Component = component,
                Text = text,
            });
        }

        private static string GetHierarchyPath(Transform t)
        {
            var sb = new StringBuilder(t.name);
            var p = t.parent;
            while (p != null)
            {
                sb.Insert(0, p.name + "/");
                p = p.parent;
            }
            return sb.ToString();
        }

        private static string SuggestKey(string assetPath, string objectPath)
        {
            string asset = Path.GetFileNameWithoutExtension(assetPath);
            string leaf = objectPath.Contains("/")
                ? objectPath.Substring(objectPath.LastIndexOf('/') + 1)
                : objectPath;
            string key = $"{asset}.{leaf}".ToLowerInvariant();
            var sb = new StringBuilder(key.Length);
            foreach (char c in key)
                sb.Append(char.IsLetterOrDigit(c) || c == '.' ? c : '_');
            return sb.ToString();
        }

        private static void WriteCsv(string path, List<Row> rows)
        {
            var sb = new StringBuilder();
            sb.AppendLine("SuggestedKey,SourceType,AssetPath,ObjectPath,Component,Text_EN");
            foreach (var r in rows)
            {
                sb.Append(Esc(r.SuggestedKey)).Append(',')
                  .Append(Esc(r.SourceType)).Append(',')
                  .Append(Esc(r.AssetPath)).Append(',')
                  .Append(Esc(r.ObjectPath)).Append(',')
                  .Append(Esc(r.Component)).Append(',')
                  .Append(Esc(r.Text)).Append('\n');
            }
            // UTF-8 BOM — Excel 한글/이모지 깨짐 방지
            File.WriteAllText(path, sb.ToString(), new UTF8Encoding(true));
        }

        private static string Esc(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            bool needsQuote = s.IndexOfAny(new[] { ',', '"', '\n', '\r' }) >= 0;
            s = s.Replace("\"", "\"\"");
            return needsQuote ? $"\"{s}\"" : s;
        }
    }
}
#endif
