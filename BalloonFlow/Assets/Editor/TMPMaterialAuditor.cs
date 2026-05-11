#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace BalloonFlow.Editor
{
    public static class TMPMaterialAuditor
    {
        private const string TMP_RESOURCE_FOLDER = "Assets/TextMesh Pro/Resources/Fonts & Materials";

        private static readonly string[] DiffColorProperties =
        {
            "_FaceColor",
            "_OutlineColor",
            "_UnderlayColor",
            "_GlowColor",
            "_ReflectFaceColor",
            "_ReflectOutlineColor",
            "_SpecularColor",
        };

        private static readonly string[] DiffFloatProperties =
        {
            "_OutlineWidth",
            "_OutlineSoftness",
            "_FaceDilate",
            "_UnderlayDilate",
            "_UnderlayOffsetX",
            "_UnderlayOffsetY",
            "_UnderlaySoftness",
            "_GlowOffset",
            "_GlowInner",
            "_GlowOuter",
            "_GlowPower",
            "_Bevel",
            "_BevelOffset",
            "_BevelWidth",
            "_BevelRoundness",
            "_BevelClamp",
        };

        [MenuItem("BalloonFlow/Performance/Audit TMP Materials", false, 500)]
        private static void Audit()
        {
            var report = BuildReport();
            Debug.Log(report);

            string path = Path.Combine(Application.dataPath, "../Logs/tmp_material_audit.log");
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                File.WriteAllText(path, report);
                Debug.Log($"[TMPMaterialAuditor] Report saved to {Path.GetFullPath(path)}");
            }
            catch (System.Exception e)
            {
                Debug.LogWarning($"[TMPMaterialAuditor] Failed to save log: {e.Message}");
            }
        }

        private static string BuildReport()
        {
            var sb = new StringBuilder();
            sb.AppendLine("=== TMP Material Audit ===");
            sb.AppendLine($"Folder: {TMP_RESOURCE_FOLDER}");
            sb.AppendLine();

            string[] guids = AssetDatabase.FindAssets("t:Material", new[] { TMP_RESOURCE_FOLDER });
            var mats = new List<Material>();
            foreach (var g in guids)
            {
                string p = AssetDatabase.GUIDToAssetPath(g);
                var m = AssetDatabase.LoadAssetAtPath<Material>(p);
                if (m != null) mats.Add(m);
            }

            sb.AppendLine($"Materials found: {mats.Count}");
            foreach (var m in mats) sb.AppendLine($"  - {m.name} (shader: {(m.shader != null ? m.shader.name : "<null>")})");
            sb.AppendLine();

            // shader 별 그룹화
            var byShader = mats.GroupBy(m => m.shader != null ? m.shader.name : "<null>").ToList();
            sb.AppendLine($"Shader groups: {byShader.Count}");
            foreach (var g in byShader)
                sb.AppendLine($"  [{g.Key}]  -> {g.Count()} mats");
            sb.AppendLine();

            // 각 shader 그룹 내에서 keyword/property 비교
            foreach (var g in byShader)
            {
                sb.AppendLine($"---- Shader: {g.Key} ----");
                var groupMats = g.ToList();

                // keyword 집합
                sb.AppendLine();
                sb.AppendLine("Keywords per material:");
                foreach (var m in groupMats)
                {
                    var kws = m.shaderKeywords;
                    System.Array.Sort(kws);
                    sb.AppendLine($"  {m.name}: [{string.Join(", ", kws)}]");
                }

                // keyword union & 차이
                var keywordSets = groupMats.Select(m => new HashSet<string>(m.shaderKeywords)).ToList();
                var union = new HashSet<string>();
                foreach (var s in keywordSets) union.UnionWith(s);
                var intersection = keywordSets.Count > 0 ? new HashSet<string>(keywordSets[0]) : new HashSet<string>();
                for (int i = 1; i < keywordSets.Count; i++) intersection.IntersectWith(keywordSets[i]);

                sb.AppendLine();
                sb.AppendLine($"Union keywords:        [{string.Join(", ", union.OrderBy(x => x))}]");
                sb.AppendLine($"Intersection keywords: [{string.Join(", ", intersection.OrderBy(x => x))}]");
                var diff = new HashSet<string>(union);
                diff.ExceptWith(intersection);
                sb.AppendLine($"Differing keywords:    [{string.Join(", ", diff.OrderBy(x => x))}]");

                // property 비교
                sb.AppendLine();
                sb.AppendLine("Color properties:");
                foreach (var p in DiffColorProperties)
                {
                    if (groupMats.Count == 0 || !groupMats[0].HasProperty(p)) continue;
                    sb.AppendLine($"  {p}:");
                    foreach (var m in groupMats)
                    {
                        Color c = m.HasProperty(p) ? m.GetColor(p) : new Color(0, 0, 0, 0);
                        sb.AppendLine($"    {m.name,-40} -> ({c.r:F3}, {c.g:F3}, {c.b:F3}, {c.a:F3})");
                    }
                }

                sb.AppendLine();
                sb.AppendLine("Float properties:");
                foreach (var p in DiffFloatProperties)
                {
                    if (groupMats.Count == 0 || !groupMats[0].HasProperty(p)) continue;
                    var values = groupMats.Select(m => m.HasProperty(p) ? m.GetFloat(p) : 0f).Distinct().ToList();
                    if (values.Count <= 1) continue; // 모두 동일하면 skip
                    sb.AppendLine($"  {p} (differs across mats):");
                    foreach (var m in groupMats)
                    {
                        float v = m.HasProperty(p) ? m.GetFloat(p) : 0f;
                        sb.AppendLine($"    {m.name,-40} -> {v:F4}");
                    }
                }

                // 결론: 통합 가능 여부 판정
                sb.AppendLine();
                sb.AppendLine("== Verdict ==");
                if (diff.Count == 0)
                {
                    sb.AppendLine("  Keywords IDENTICAL — same shader variant.");
                    sb.AppendLine("  SRP Batcher SHOULD batch these (verify in Frame Debugger).");
                    sb.AppendLine("  Color-only differences can be moved to MPB / outlineColor for single-mat path.");
                }
                else
                {
                    sb.AppendLine("  Keywords DIFFER — separate shader variants per mat.");
                    sb.AppendLine("  Consolidation requires unifying keyword set (turn all effects ON and use 0-value to disable).");
                    sb.AppendLine($"  Differing keywords to align: {string.Join(", ", diff)}");
                }

                sb.AppendLine();
            }

            // prefab usage scan (참고용 — 어떤 prefab 이 어떤 mat 사용하는지)
            sb.AppendLine();
            sb.AppendLine("=== Material usage scan (TMP_Text in prefabs) ===");
            ScanPrefabUsage(sb, mats);

            return sb.ToString();
        }

        private static void ScanPrefabUsage(StringBuilder sb, List<Material> tmpMats)
        {
            var matGuidSet = new HashSet<string>();
            foreach (var m in tmpMats)
            {
                string p = AssetDatabase.GetAssetPath(m);
                string guid = AssetDatabase.AssetPathToGUID(p);
                if (!string.IsNullOrEmpty(guid)) matGuidSet.Add(guid);
            }

            string[] prefabGuids = AssetDatabase.FindAssets("t:Prefab", new[] { "Assets/Resources" });
            int prefabHit = 0;
            var usage = new Dictionary<string, List<string>>();

            foreach (var pg in prefabGuids)
            {
                string ppath = AssetDatabase.GUIDToAssetPath(pg);
                string[] deps = AssetDatabase.GetDependencies(ppath, false);
                foreach (var d in deps)
                {
                    string dguid = AssetDatabase.AssetPathToGUID(d);
                    if (matGuidSet.Contains(dguid))
                    {
                        var mat = tmpMats.Find(m => AssetDatabase.GetAssetPath(m) == d);
                        if (mat != null)
                        {
                            if (!usage.TryGetValue(mat.name, out var lst))
                            {
                                lst = new List<string>();
                                usage[mat.name] = lst;
                            }
                            lst.Add(ppath);
                        }
                        prefabHit++;
                        break;
                    }
                }
            }

            sb.AppendLine($"Scanned {prefabGuids.Length} prefabs in Assets/Resources");
            sb.AppendLine($"Prefabs referencing TMP mats: {prefabHit}");
            sb.AppendLine();

            foreach (var kv in usage.OrderBy(k => k.Key))
            {
                sb.AppendLine($"[{kv.Key}]  ({kv.Value.Count} prefabs)");
                foreach (var p in kv.Value.Take(20)) sb.AppendLine($"  - {p}");
                if (kv.Value.Count > 20) sb.AppendLine($"  ... +{kv.Value.Count - 20} more");
                sb.AppendLine();
            }
        }
    }
}
#endif
