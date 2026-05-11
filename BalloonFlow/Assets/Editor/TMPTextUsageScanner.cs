#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using TMPro;
using UnityEditor;
using UnityEngine;

namespace BalloonFlow.Editor
{
    public static class TMPTextUsageScanner
    {
        [MenuItem("BalloonFlow/Performance/Scan TMP_Text Material Usage", false, 501)]
        private static void Scan()
        {
            var sb = new StringBuilder();
            sb.AppendLine("=== TMP_Text Material Usage Scan ===");
            sb.AppendLine();

            string[] searchFolders = { "Assets/Resources" };
            string[] prefabGuids = AssetDatabase.FindAssets("t:Prefab", searchFolders);
            sb.AppendLine($"Scanning {prefabGuids.Length} prefabs in [{string.Join(", ", searchFolders)}]...");
            sb.AppendLine();

            // mat path -> list of (prefab path, component path, gameobject name)
            var usage = new Dictionary<string, List<string>>();
            int totalTmp = 0;
            int prefabsWithTmp = 0;

            foreach (var pg in prefabGuids)
            {
                string ppath = AssetDatabase.GUIDToAssetPath(pg);
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(ppath);
                if (prefab == null) continue;

                var tmps = prefab.GetComponentsInChildren<TMP_Text>(true);
                if (tmps == null || tmps.Length == 0) continue;
                prefabsWithTmp++;

                foreach (var tmp in tmps)
                {
                    if (tmp == null) continue;
                    totalTmp++;

                    var mat = tmp.fontSharedMaterial;
                    string matPath = mat != null ? AssetDatabase.GetAssetPath(mat) : "<null>";
                    string matName = mat != null ? mat.name : "<null>";
                    string key = $"{matName}  ({matPath})";

                    if (!usage.TryGetValue(key, out var lst))
                    {
                        lst = new List<string>();
                        usage[key] = lst;
                    }
                    lst.Add($"{ppath} > {GetGameObjectPath(tmp.gameObject)}");
                }
            }

            sb.AppendLine($"Prefabs containing TMP_Text: {prefabsWithTmp}");
            sb.AppendLine($"Total TMP_Text components:   {totalTmp}");
            sb.AppendLine();

            foreach (var kv in usage.OrderByDescending(k => k.Value.Count))
            {
                sb.AppendLine($"[{kv.Key}]  -> {kv.Value.Count} TMP_Text(s)");
                foreach (var line in kv.Value.Take(50))
                    sb.AppendLine($"  - {line}");
                if (kv.Value.Count > 50) sb.AppendLine($"  ... +{kv.Value.Count - 50} more");
                sb.AppendLine();
            }

            // mat 별 prefab unique 개수 보고
            sb.AppendLine();
            sb.AppendLine("=== Summary (unique prefabs per material) ===");
            foreach (var kv in usage.OrderByDescending(k => k.Value.Select(v => v.Split('>')[0].Trim()).Distinct().Count()))
            {
                int uniquePrefabs = kv.Value.Select(v => v.Split('>')[0].Trim()).Distinct().Count();
                sb.AppendLine($"{kv.Key}: {uniquePrefabs} prefabs, {kv.Value.Count} TMP_Text");
            }

            string report = sb.ToString();
            Debug.Log(report);

            string path = Path.Combine(Application.dataPath, "../Logs/tmp_text_usage.log");
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                File.WriteAllText(path, report);
                Debug.Log($"[TMPTextUsageScanner] Report saved to {Path.GetFullPath(path)}");
            }
            catch (System.Exception e)
            {
                Debug.LogWarning($"[TMPTextUsageScanner] Failed to save log: {e.Message}");
            }
        }

        private static string GetGameObjectPath(GameObject go)
        {
            if (go == null) return "<null>";
            var sb = new StringBuilder(go.name);
            var t = go.transform.parent;
            while (t != null)
            {
                sb.Insert(0, t.name + "/");
                t = t.parent;
            }
            return sb.ToString();
        }
    }
}
#endif
