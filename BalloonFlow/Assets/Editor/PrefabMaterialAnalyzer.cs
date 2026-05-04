using UnityEditor;
using UnityEngine;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace BalloonFlow.EditorTools
{
    /// <summary>
    /// 임의 prefab 의 Material + Renderer + Shadow 설정 분석.
    /// 같은 shader+texture 면 자동 통일 (canonical 로 합침).
    /// 모바일 SetPass 부하 진단 — URP/Lit 사용 여부 / Cast Shadows / Receive Shadows.
    /// </summary>
    public static class PrefabMaterialAnalyzer
    {
        private static readonly string[] HOT_SHADERS_MOBILE = {
            "Universal Render Pipeline/Lit",
            "Standard",
            "Universal Render Pipeline/Complex Lit",
        };

        private static readonly string[] ALL_INGAME_PREFABS = {
            "Assets/Resources/Prefabs/Balloon.prefab",
            "Assets/Resources/Prefabs/Holder.prefab",
            "Assets/Resources/Prefabs/Rail.prefab",
            "Assets/Resources/Prefabs/Dart.prefab",
            "Assets/Resources/Prefabs/IronBox.prefab",
            "Assets/Resources/Prefabs/FrozenLayer.prefab",
            "Assets/Resources/Prefabs/Baricade.prefab",
            "Assets/Resources/Prefabs/Spawner.prefab",
            "Assets/Resources/Prefabs/Lock.prefab",
            "Assets/Resources/Prefabs/Key.prefab",
            "Assets/Resources/Prefabs/Ground.prefab",
            "Assets/Resources/Prefabs/WoodenBoard.prefab",
            "Assets/Resources/Prefabs/ItemZap.prefab",
            "Assets/Resources/Prefabs/CircleParticle.prefab",
        };

        // ─── Analyze menus ──────────────────────────────
        [MenuItem("BalloonFlow/Analyze Materials/Balloon")]      public static void AnalyzeBalloon()    => Analyze("Assets/Resources/Prefabs/Balloon.prefab");
        [MenuItem("BalloonFlow/Analyze Materials/Holder")]       public static void AnalyzeHolder()     => Analyze("Assets/Resources/Prefabs/Holder.prefab");
        [MenuItem("BalloonFlow/Analyze Materials/Rail")]         public static void AnalyzeRail()       => Analyze("Assets/Resources/Prefabs/Rail.prefab");
        [MenuItem("BalloonFlow/Analyze Materials/Dart")]         public static void AnalyzeDart()       => Analyze("Assets/Resources/Prefabs/Dart.prefab");
        [MenuItem("BalloonFlow/Analyze Materials/IronBox")]      public static void AnalyzeIronBox()    => Analyze("Assets/Resources/Prefabs/IronBox.prefab");
        [MenuItem("BalloonFlow/Analyze Materials/FrozenLayer")]  public static void AnalyzeFrozen()     => Analyze("Assets/Resources/Prefabs/FrozenLayer.prefab");
        [MenuItem("BalloonFlow/Analyze Materials/All In-Game")]
        public static void AnalyzeAll()
        {
            foreach (var p in ALL_INGAME_PREFABS) Analyze(p);
            Debug.Log("[PrefabMaterialAnalyzer] === All In-Game 분석 끝 ===");
        }

        // ─── Unify menus ───────────────────────────────
        [MenuItem("BalloonFlow/DON'T USE/Unify Materials/Balloon (자동)")] public static void UnifyBalloon() => UnifyAutomatic("Assets/Resources/Prefabs/Balloon.prefab");
        [MenuItem("BalloonFlow/DON'T USE/Unify Materials/Holder (자동)")]  public static void UnifyHolder()  => UnifyAutomatic("Assets/Resources/Prefabs/Holder.prefab");
        [MenuItem("BalloonFlow/DON'T USE/Unify Materials/Rail (자동)")]    public static void UnifyRail()    => UnifyAutomatic("Assets/Resources/Prefabs/Rail.prefab");
        [MenuItem("BalloonFlow/DON'T USE/Unify Materials/Dart (자동)")]    public static void UnifyDart()    => UnifyAutomatic("Assets/Resources/Prefabs/Dart.prefab");

        // ─── Shadow disable menus (모바일 즉시 절감) ────
        [MenuItem("BalloonFlow/DON'T USE/Disable Shadows/Balloon")] public static void DisableShadowsBalloon() => DisableShadows("Assets/Resources/Prefabs/Balloon.prefab");
        [MenuItem("BalloonFlow/DON'T USE/Disable Shadows/Holder")]  public static void DisableShadowsHolder()  => DisableShadows("Assets/Resources/Prefabs/Holder.prefab");
        [MenuItem("BalloonFlow/DON'T USE/Disable Shadows/Rail")]    public static void DisableShadowsRail()    => DisableShadows("Assets/Resources/Prefabs/Rail.prefab");
        [MenuItem("BalloonFlow/DON'T USE/Disable Shadows/Dart")]    public static void DisableShadowsDart()    => DisableShadows("Assets/Resources/Prefabs/Dart.prefab");
        [MenuItem("BalloonFlow/DON'T USE/Disable Shadows/All In-Game")]
        public static void DisableShadowsAll()
        {
            int total = 0;
            foreach (var p in ALL_INGAME_PREFABS) total += DisableShadows(p, silent: true);
            Debug.Log($"[PrefabMaterialAnalyzer] === All Shadows OFF 끝. 총 {total}개 Renderer 변경. ===");
        }

        // ─── Core: Analyze ─────────────────────────────
        public static void Analyze(string prefabPath)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            if (prefab == null) { Debug.LogWarning($"[PrefabMaterialAnalyzer] {prefabPath} 미발견 (skip)"); return; }

            var sb = new StringBuilder();
            sb.AppendLine($"[PrefabMaterialAnalyzer] === {prefab.name} ===");

            var entries = new List<MatEntry>();
            CollectRenderers(prefab.transform, entries, "");

            var matGroups = entries
                .Where(e => e.material != null)
                .GroupBy(e => e.material)
                .OrderByDescending(g => g.Count())
                .ToList();

            int castOnCount = entries.Count(e => e.castShadow);
            int recvOnCount = entries.Count(e => e.recvShadow);
            int hotShaderRendererCount = entries.Count(e =>
                e.material != null && e.material.shader != null
                && HOT_SHADERS_MOBILE.Contains(e.material.shader.name));

            sb.AppendLine($"Renderer 총 {entries.Count}개 / Unique Material {matGroups.Count}개");
            sb.AppendLine($"Cast Shadows ON: {castOnCount}/{entries.Count} | Receive Shadows ON: {recvOnCount}/{entries.Count}");
            sb.AppendLine($"모바일 hot shader (URP/Lit, Standard 등) 사용: {hotShaderRendererCount}/{entries.Count}");

            sb.AppendLine("─── Material 별 ───");
            int idx = 0;
            foreach (var grp in matGroups)
            {
                idx++;
                Material mat = grp.Key;
                string shaderName = mat.shader != null ? mat.shader.name : "(null)";
                bool isHot = HOT_SHADERS_MOBILE.Contains(shaderName);
                Texture mainTex = SafeMainTex(mat);
                string texName = mainTex != null ? mainTex.name : "(null)";

                int gCast = grp.Count(e => e.castShadow);
                int gRecv = grp.Count(e => e.recvShadow);

                sb.AppendLine($"[{idx}] {mat.name}  ({grp.Count()} Renderer){(isHot ? "  HOT shader" : "")}");
                sb.AppendLine($"    Shader: {shaderName}");
                sb.AppendLine($"    Texture: {texName}");
                sb.AppendLine($"    Cast={gCast}/{grp.Count()}  Receive={gRecv}/{grp.Count()}");
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
            }

            sb.AppendLine("─── 자동 통일 후보 ───");
            var unifiable = matGroups
                .GroupBy(g => new { Shader = g.Key.shader != null ? g.Key.shader.name : "(null)", Tex = SafeMainTexId(g.Key) })
                .Where(g => g.Count() > 1)
                .ToList();

            if (unifiable.Count == 0) sb.AppendLine("  자동 통일 가능 그룹 없음.");
            else
            {
                foreach (var ug in unifiable)
                {
                    sb.AppendLine($"  Shader '{ug.Key.Shader}' + tex#{ug.Key.Tex}: Material {ug.Count()}개");
                    foreach (var m in ug) sb.AppendLine($"    - {m.Key.name}");
                }
            }

            if (castOnCount > 0 || hotShaderRendererCount > 0)
            {
                sb.AppendLine("─── 모바일 즉시 fix 후보 ───");
                if (castOnCount > 0)
                    sb.AppendLine($"  Cast Shadows OFF (Renderer {castOnCount}개): SetPass 즉시 절감. 메뉴 'Disable Shadows/{prefab.name}'");
                if (hotShaderRendererCount > 0)
                    sb.AppendLine($"  URP/Lit → URP/Unlit 또는 Simple Lit 교체 검토 (Renderer {hotShaderRendererCount}개)");
            }

            Debug.Log(sb.ToString());
        }

        // ─── Core: Unify ───────────────────────────────
        public static void UnifyAutomatic(string prefabPath)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            if (prefab == null) { Debug.LogError($"[PrefabMaterialAnalyzer] {prefabPath} 미발견"); return; }

            var instance = PrefabUtility.LoadPrefabContents(prefabPath);
            int unified = 0;
            try
            {
                var renderers = instance.GetComponentsInChildren<Renderer>(includeInactive: true);
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
                        else canonicalByKey[key] = m;
                    }
                    if (changed) r.sharedMaterials = mats;
                }

                if (unified > 0)
                {
                    PrefabUtility.SaveAsPrefabAsset(instance, prefabPath);
                    Debug.Log($"[PrefabMaterialAnalyzer] {prefab.name}: {unified}개 Renderer Material 통일.");
                }
                else Debug.Log($"[PrefabMaterialAnalyzer] {prefab.name}: 통일 가능 Material 없음.");
            }
            finally { PrefabUtility.UnloadPrefabContents(instance); }
            AssetDatabase.SaveAssets();
        }

        // ─── Core: Disable shadows ─────────────────────
        public static int DisableShadows(string prefabPath, bool silent = false)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            if (prefab == null)
            {
                if (!silent) Debug.LogWarning($"[PrefabMaterialAnalyzer] {prefabPath} 미발견 (skip)");
                return 0;
            }
            var instance = PrefabUtility.LoadPrefabContents(prefabPath);
            int changed = 0;
            try
            {
                var renderers = instance.GetComponentsInChildren<Renderer>(includeInactive: true);
                foreach (var r in renderers)
                {
                    bool dirty = false;
                    if (r.shadowCastingMode != UnityEngine.Rendering.ShadowCastingMode.Off)
                    {
                        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                        dirty = true;
                    }
                    if (r.receiveShadows)
                    {
                        r.receiveShadows = false;
                        dirty = true;
                    }
                    if (dirty) changed++;
                }
                if (changed > 0)
                {
                    PrefabUtility.SaveAsPrefabAsset(instance, prefabPath);
                    Debug.Log($"[PrefabMaterialAnalyzer] {prefab.name}: Shadows OFF 적용 {changed}/{renderers.Length} Renderer.");
                }
                else if (!silent) Debug.Log($"[PrefabMaterialAnalyzer] {prefab.name}: 이미 모든 Renderer Shadows OFF.");
            }
            finally { PrefabUtility.UnloadPrefabContents(instance); }
            AssetDatabase.SaveAssets();
            return changed;
        }

        // ─── Helpers ───────────────────────────────────
        private static void CollectRenderers(Transform t, List<MatEntry> entries, string parentPath)
        {
            string path = string.IsNullOrEmpty(parentPath) ? t.name : parentPath + "/" + t.name;

            var sr = t.GetComponent<SpriteRenderer>();
            if (sr != null) entries.Add(new MatEntry { material = sr.sharedMaterial, path = path, type = "Sprite", castShadow = false, recvShadow = false });

            var mr = t.GetComponent<MeshRenderer>();
            if (mr != null)
            {
                bool cast = mr.shadowCastingMode != UnityEngine.Rendering.ShadowCastingMode.Off;
                bool recv = mr.receiveShadows;
                foreach (var m in mr.sharedMaterials)
                    entries.Add(new MatEntry { material = m, path = path, type = "Mesh", castShadow = cast, recvShadow = recv });
            }

            var smr = t.GetComponent<SkinnedMeshRenderer>();
            if (smr != null)
            {
                bool cast = smr.shadowCastingMode != UnityEngine.Rendering.ShadowCastingMode.Off;
                bool recv = smr.receiveShadows;
                foreach (var m in smr.sharedMaterials)
                    entries.Add(new MatEntry { material = m, path = path, type = "Skinned", castShadow = cast, recvShadow = recv });
            }

            for (int i = 0; i < t.childCount; i++)
                CollectRenderers(t.GetChild(i), entries, path);
        }

        private static Texture SafeMainTex(Material m)
        {
            try { return m.mainTexture; } catch { return null; }
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
            public bool castShadow;
            public bool recvShadow;
        }
    }
}
