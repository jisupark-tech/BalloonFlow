#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace BalloonFlow.EditorTools
{
    /// <summary>
    /// Resources/ 아래 모든 prefab 의 외부 참조 (Sprite / Texture / Material / AudioClip / Font / GameObject)
    /// 를 스캔해서 path/atlas 정보와 함께 콘솔에 dump.
    ///
    /// 메뉴: BalloonFlow > Tools > Scan Resources Prefab External Refs
    ///
    /// 출력 활용:
    ///   1) Const.cs 에 path 상수 정의용 — "Sprites/UI/iconHand" 같은 Resources-relative path 들
    ///   2) refactor 우선순위 정하기 — 가장 많이 참조되는 sprite 부터
    ///   3) atlas 화 candidate 식별 — 같은 폴더 다수 sprite
    ///
    /// CSV 도 함께 저장: Assets/Editor/_PrefabRefs.csv (gitignore 권장)
    /// </summary>
    public static class PrefabReferenceScanner
    {
        private const string LOG_TAG = "[PrefabRefScan]";
        private const string CSV_OUTPUT = "Assets/Editor/_PrefabRefs.csv";

        [MenuItem("BalloonFlow/Tools/Scan Resources Prefab External Refs")]
        public static void Scan()
        {
            var prefabPaths = new List<string>();
            string root = "Assets/Resources";
            CollectPrefabs(root, prefabPaths);

            Debug.Log($"{LOG_TAG} {prefabPaths.Count} prefab 발견 (Resources/ 하위). 스캔 시작...");

            var csv = new StringBuilder();
            csv.AppendLine("PrefabPath,RefType,RefName,RefAssetPath,IsInResources,ResourcesRelative");

            int totalRefs = 0;
            int resourcesRefs = 0;
            int outsideResourcesRefs = 0;
            var refCountByAsset = new Dictionary<string, int>();

            foreach (var prefabPath in prefabPaths)
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
                if (prefab == null) continue;

                var deps = AssetDatabase.GetDependencies(prefabPath, recursive: false);
                foreach (var dep in deps)
                {
                    if (dep == prefabPath) continue; // self
                    if (dep.EndsWith(".cs")) continue; // script — not a content asset
                    if (dep.EndsWith(".shader") || dep.EndsWith(".compute")) continue; // shader

                    var assetType = AssetDatabase.GetMainAssetTypeAtPath(dep);
                    string typeName = assetType != null ? assetType.Name : "?";

                    bool isInResources = dep.Contains("/Resources/");
                    string resourcesRel = isInResources ? ResourcesRelative(dep) : "";

                    string refName = Path.GetFileNameWithoutExtension(dep);
                    csv.AppendLine($"\"{prefabPath}\",\"{typeName}\",\"{refName}\",\"{dep}\",{isInResources},\"{resourcesRel}\"");

                    if (isInResources) resourcesRefs++; else outsideResourcesRefs++;
                    totalRefs++;

                    if (!refCountByAsset.ContainsKey(dep)) refCountByAsset[dep] = 0;
                    refCountByAsset[dep]++;
                }
            }

            Directory.CreateDirectory(Path.GetDirectoryName(CSV_OUTPUT));
            File.WriteAllText(CSV_OUTPUT, csv.ToString());
            AssetDatabase.Refresh();

            Debug.Log($"{LOG_TAG} 완료. 총 ref={totalRefs} (Resources={resourcesRefs}, Outside={outsideResourcesRefs}). " +
                      $"CSV: {CSV_OUTPUT}");

            // Top 20 most-referenced assets
            var sorted = new List<KeyValuePair<string, int>>(refCountByAsset);
            sorted.Sort((a, b) => b.Value.CompareTo(a.Value));
            var topSb = new StringBuilder();
            topSb.AppendLine($"{LOG_TAG} Top 20 most-referenced assets:");
            for (int i = 0; i < Mathf.Min(20, sorted.Count); i++)
                topSb.AppendLine($"  {sorted[i].Value}× {sorted[i].Key}");
            Debug.Log(topSb.ToString());
        }

        private static void CollectPrefabs(string folder, List<string> outList)
        {
            if (!Directory.Exists(folder)) return;
            var guids = AssetDatabase.FindAssets("t:Prefab", new[] { folder });
            foreach (var g in guids)
                outList.Add(AssetDatabase.GUIDToAssetPath(g));
        }

        /// <summary>"Assets/Resources/Sprites/UI/iconHand.png" → "Sprites/UI/iconHand"</summary>
        private static string ResourcesRelative(string assetPath)
        {
            const string token = "/Resources/";
            int idx = assetPath.IndexOf(token);
            if (idx < 0) return "";
            string rel = assetPath.Substring(idx + token.Length);
            // strip extension
            int dot = rel.LastIndexOf('.');
            if (dot >= 0) rel = rel.Substring(0, dot);
            return rel;
        }
    }
}
#endif
