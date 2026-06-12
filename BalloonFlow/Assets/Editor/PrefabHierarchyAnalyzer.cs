using UnityEditor;
using UnityEngine;
using System.Text;
using System.Collections.Generic;

namespace BalloonFlow.EditorTools
{
    /// <summary>
    /// 풍선/Dart/Holder prefab 의 hierarchy + 컴포넌트 통계를 dump.
    /// 자식 GO 수, Renderer 종류, Material 다양성, Animator 정보를 한 번에 확인.
    /// 부하 진단 — Scene Objects 폭증 / batching 깨짐 / Animator culling 상태 검증.
    /// </summary>
    public static class PrefabHierarchyAnalyzer
    {
        private static readonly string[] CANDIDATE_PATHS = new[]
        {
            "Assets/Resources/Prefabs/Balloon.prefab",
            "Assets/Resources/Prefabs/Dart.prefab",
            "Assets/Resources/Prefabs/Holder.prefab",
            "Assets/Resources/Prefabs/Spawner.prefab",
        };

        // [2026-06-12 메뉴 정리] [MenuItem("BalloonFlow/Analyze Prefab Hierarchies")]
        public static void Analyze()
        {
            var sb = new StringBuilder();
            sb.AppendLine("[PrefabHierarchyAnalyzer] === 결과 ===\n");

            foreach (var path in CANDIDATE_PATHS)
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (prefab == null)
                {
                    sb.AppendLine($"  ⚠️  미발견: {path}");
                    continue;
                }
                sb.AppendLine(AnalyzePrefab(prefab, path));
            }

            Debug.Log(sb.ToString());
        }

        private static string AnalyzePrefab(GameObject prefab, string path)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"━━━ {prefab.name} ({path}) ━━━");

            int totalGO = 0;
            int activeGO = 0;
            int spriteRenderers = 0;
            int meshRenderers = 0;
            int particleSystems = 0;
            int colliders = 0;
            var materials = new HashSet<Material>();
            var sprites = new HashSet<Sprite>();
            int animatorCount = 0;
            var animatorCullingModes = new List<string>();

            CountRecursive(prefab.transform, ref totalGO, ref activeGO,
                ref spriteRenderers, ref meshRenderers, ref particleSystems,
                ref colliders, materials, sprites, ref animatorCount, animatorCullingModes);

            sb.AppendLine($"  Total GameObjects:    {totalGO}  (active {activeGO})");
            sb.AppendLine($"  SpriteRenderer:       {spriteRenderers}");
            sb.AppendLine($"  MeshRenderer:         {meshRenderers}");
            sb.AppendLine($"  ParticleSystem:       {particleSystems}");
            sb.AppendLine($"  Collider:             {colliders}");
            sb.AppendLine($"  Unique Material:      {materials.Count}  ← 동일 material 여야 batching");
            sb.AppendLine($"  Unique Sprite:        {sprites.Count}");
            sb.AppendLine($"  Animator:             {animatorCount}");
            for (int i = 0; i < animatorCullingModes.Count; i++)
                sb.AppendLine($"    [{i}] cullingMode = {animatorCullingModes[i]}");

            // 진단 hint
            if (totalGO > 30)
                sb.AppendLine($"  ⚠️  자식 GO 수 많음 ({totalGO}) — n×n 격자 시 Scene Objects 폭증 가능. 사용 안 하는 변형 비활성/제거 검토.");
            if (materials.Count > 1)
                sb.AppendLine($"  ⚠️  Material 다양 ({materials.Count}개) — dynamic batching 깨질 수 있음. 같은 atlas + Material Property Block 사용 권장.");
            if (particleSystems > 0)
                sb.AppendLine($"  ℹ️  ParticleSystem {particleSystems}개 — 풍선 N개 시 N×{particleSystems} 갱신. Inspector 의 Renderer > Culling Mode = Pause 권장.");

            sb.AppendLine();
            return sb.ToString();
        }

        private static void CountRecursive(Transform t,
            ref int total, ref int active,
            ref int spriteR, ref int meshR, ref int particle, ref int coll,
            HashSet<Material> mats, HashSet<Sprite> sprites,
            ref int animator, List<string> cullingModes)
        {
            total++;
            if (t.gameObject.activeSelf) active++;

            var sr = t.GetComponent<SpriteRenderer>();
            if (sr != null)
            {
                spriteR++;
                if (sr.sharedMaterial != null) mats.Add(sr.sharedMaterial);
                if (sr.sprite != null) sprites.Add(sr.sprite);
            }
            var mr = t.GetComponent<MeshRenderer>();
            if (mr != null)
            {
                meshR++;
                if (mr.sharedMaterial != null) mats.Add(mr.sharedMaterial);
            }
            var ps = t.GetComponent<ParticleSystem>();
            if (ps != null) particle++;
            var col = t.GetComponent<Collider>();
            if (col != null) coll++;
            var col2d = t.GetComponent<Collider2D>();
            if (col2d != null) coll++;

            var anim = t.GetComponent<Animator>();
            if (anim != null)
            {
                animator++;
                cullingModes.Add(anim.cullingMode.ToString());
            }

            for (int i = 0; i < t.childCount; i++)
                CountRecursive(t.GetChild(i), ref total, ref active,
                    ref spriteR, ref meshR, ref particle, ref coll,
                    mats, sprites, ref animator, cullingModes);
        }
    }
}
