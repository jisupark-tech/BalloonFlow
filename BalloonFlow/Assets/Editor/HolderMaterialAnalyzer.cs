using UnityEditor;
using UnityEngine;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace BalloonFlow.EditorTools
{
    /// <summary>
    /// Holder prefab 의 Material 분석 + 통일 가능 그룹 검출 + 자동 unify 시도.
    ///
    /// 동작:
    ///  1) 모든 Renderer 의 Material 수집
    ///  2) Material 별 shader + mainTexture + 핵심 properties (color/tint) dump
    ///  3) 같은 shader + 같은 texture 면 통일 가능 (group). 사용자 결정 후 unify 메뉴 실행
    ///
    /// Material 통일 = SetPass calls 1/N. GPU bound 의 직접 fix.
    /// </summary>
    public static class HolderMaterialAnalyzer
    {
        private const string PREFAB_PATH = "Assets/Resources/Prefabs/Holder.prefab";

        [MenuItem("BalloonFlow/Analyze Holder Materials (perf 진단)")]
        public static void Analyze()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PREFAB_PATH);
            if (prefab == null) { Debug.LogError($"[HolderMaterialAnalyzer] {PREFAB_PATH} 미발견"); return; }

            var sb = new StringBuilder();
            sb.AppendLine($"[HolderMaterialAnalyzer] === {prefab.name} Material 분석 ===\n");

            // 모든 Renderer + Material 수집 (자식 포함, inactive 포함)
            var entries = new List<MatEntry>();
            CollectRenderers(prefab.transform, entries, "");

            // Unique Material 별 group
            var matGroups = entries
                .Where(e => e.material != null)
                .GroupBy(e => e.material)
                .OrderByDescending(g => g.Count())
                .ToList();

            sb.AppendLine($"총 Renderer: {entries.Count}개");
            sb.AppendLine($"Unique Material: {matGroups.Count}개\n");
            sb.AppendLine("─── Material 별 shader / texture / 사용처 ───");

            int idx = 0;
            foreach (var grp in matGroups)
            {
                idx++;
                Material mat = grp.Key;
                string shaderName = mat.shader != null ? mat.shader.name : "(null)";
                Texture mainTex = null;
                try { mainTex = mat.mainTexture; } catch { }
                string texName = mainTex != null ? mainTex.name : "(null)";
                string color = mat.HasProperty("_Color")    ? "_Color="    + mat.GetColor("_Color").ToString("F2")    : "";
                string tint  = mat.HasProperty("_TintColor") ? " _TintColor=" + mat.GetColor("_TintColor").ToString("F2") : "";

                sb.AppendLine($"[{idx}] {mat.name}  ({grp.Count()} Renderer)");
                sb.AppendLine($"    Shader: {shaderName}");
                sb.AppendLine($"    Texture: {texName}");
                if (!string.IsNullOrEmpty(color) || !string.IsNullOrEmpty(tint))
                    sb.AppendLine($"    Props: {color}{tint}");
                sb.Append("    Used by: ");
                int u = 0;
                foreach (var e in grp.Take(5))
                {
                    sb.Append(e.path);
                    u++;
                    if (u < grp.Count() && u < 5) sb.Append(", ");
                }
                if (grp.Count() > 5) sb.Append($", ... (+{grp.Count() - 5} more)");
                sb.AppendLine();
                sb.AppendLine();
            }

            // 통일 가능 그룹 검출 — 같은 shader + 같은 texture 인 Material 들
            sb.AppendLine("─── 통일 가능 후보 (같은 shader + 같은 texture) ───");
            var unifiable = matGroups
                .GroupBy(g => new {
                    Shader = g.Key.shader != null ? g.Key.shader.name : "(null)",
                    Tex = SafeMainTex(g.Key)
                })
                .Where(g => g.Count() > 1)
                .ToList();

            if (unifiable.Count == 0)
            {
                sb.AppendLine("  자동 통일 가능 그룹 없음 — 모든 Material 이 다른 shader 또는 다른 texture 사용.");
                sb.AppendLine("  → 통일하려면 atlas 통합 + sprite 재packing 필요 (아트팀 협업).");
            }
            else
            {
                foreach (var ug in unifiable)
                {
                    sb.AppendLine($"  ◆ Shader '{ug.Key.Shader}' + Texture '{ug.Key.Tex}' 그룹: Material {ug.Count()}개");
                    foreach (var m in ug)
                        sb.AppendLine($"    - {m.Key.name}");
                    sb.AppendLine($"    → 통일 시 SetPass {ug.Count()}→1 절감");
                }
            }

            sb.AppendLine("\n─── 권장 다음 step ───");
            sb.AppendLine("  1. 위 통일 가능 그룹이 있다면 'BalloonFlow > Unify Holder Materials' 메뉴 실행 (자동)");
            sb.AppendLine("  2. 그룹이 없거나 충분히 줄어들지 않으면 아트팀 협업 — atlas 통합 + sprite 재packing 필요");
            sb.AppendLine("  3. 풍선 / Dart prefab 도 같은 패턴으로 분석 권장");

            Debug.Log(sb.ToString());
        }

        [MenuItem("BalloonFlow/Unify Holder Materials (자동 통일)")]
        public static void UnifyAutomatic()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PREFAB_PATH);
            if (prefab == null) { Debug.LogError($"[HolderMaterialAnalyzer] {PREFAB_PATH} 미발견"); return; }

            // Prefab 안전 편집을 위해 Contents 로 열기
            var instance = PrefabUtility.LoadPrefabContents(PREFAB_PATH);
            int unified = 0;

            try
            {
                var renderers = instance.GetComponentsInChildren<Renderer>(includeInactive: true);

                // 같은 (shader name, texture instance ID) 면 첫 Material 로 통일
                var canonicalByKey = new Dictionary<(string, int), Material>();

                foreach (var r in renderers)
                {
                    var mats = r.sharedMaterials;
                    bool changed = false;
                    for (int i = 0; i < mats.Length; i++)
                    {
                        var m = mats[i];
                        if (m == null) continue;
                        string shader = m.shader != null ? m.shader.name : "(null)";
                        int texId = SafeMainTexId(m);
                        var key = (shader, texId);

                        if (canonicalByKey.TryGetValue(key, out var canonical))
                        {
                            if (canonical != m) { mats[i] = canonical; changed = true; unified++; }
                        }
                        else
                        {
                            canonicalByKey[key] = m;
                        }
                    }
                    if (changed) r.sharedMaterials = mats;
                }

                if (unified > 0)
                {
                    PrefabUtility.SaveAsPrefabAsset(instance, PREFAB_PATH);
                    Debug.Log($"[HolderMaterialAnalyzer] ✓ {unified}개 Renderer 의 Material 을 canonical 로 통일. prefab 저장됨.");
                }
                else
                {
                    Debug.Log("[HolderMaterialAnalyzer] 통일 가능 Material 없음 — 모든 Material 이 unique shader+texture.");
                }
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(instance);
            }
            AssetDatabase.SaveAssets();
        }

        // ─────────────────────────────────────────

        private static void CollectRenderers(Transform t, List<MatEntry> entries, string parentPath)
        {
            string path = string.IsNullOrEmpty(parentPath) ? t.name : parentPath + "/" + t.name;

            var sr = t.GetComponent<SpriteRenderer>();
            if (sr != null) entries.Add(new MatEntry { material = sr.sharedMaterial, path = path, type = "Sprite" });

            var mr = t.GetComponent<MeshRenderer>();
            if (mr != null)
            {
                foreach (var m in mr.sharedMaterials)
                    entries.Add(new MatEntry { material = m, path = path, type = "Mesh" });
            }

            for (int i = 0; i < t.childCount; i++)
                CollectRenderers(t.GetChild(i), entries, path);
        }

        private static string SafeMainTex(Material m)
        {
            try { return m.mainTexture != null ? m.mainTexture.name : "(null)"; }
            catch { return "(no _MainTex prop)"; }
        }

        private static int SafeMainTexId(Material m)
        {
            try { return m.mainTexture != null ? m.mainTexture.GetInstanceID() : 0; }
            catch { return 0; }
        }

        private struct MatEntry
        {
            public Material material;
            public string path;
            public string type;
        }
    }
}
