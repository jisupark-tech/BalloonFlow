using UnityEditor;
using UnityEngine;
using System.Collections.Generic;

namespace BalloonFlow.EditorTools
{
    /// <summary>
    /// 풍선 / Dart / Holder / Spawner prefab 의 perf 관련 컴포넌트 일괄 fix.
    ///
    /// 자동 변경:
    ///  1. Animator.cullingMode = CullCompletely (prefab default 갱신 — runtime Init() 외에도 보장)
    ///  2. ParticleSystem.Renderer.cullingMode = AutomaticCulling (Pause-and-Catch-up 효과 — 화면 밖 갱신 차단)
    ///
    /// 변경 안 함 (사용자 / 아트 결정 영역):
    ///  - Holder 의 자식 GO 91개 / Material 8개 / MeshRenderer 57개
    ///    → 게임 visual 직접 영향. 별도로 아트팀 협업 후 atlas 통일 / 자식 정리 필요.
    /// </summary>
    public static class PrefabPerfFixer
    {
        private static readonly string[] PREFAB_PATHS = new[]
        {
            "Assets/Resources/Prefabs/Balloon.prefab",
            "Assets/Resources/Prefabs/Dart.prefab",
            "Assets/Resources/Prefabs/Holder.prefab",
            "Assets/Resources/Prefabs/Spawner.prefab",
        };

        [MenuItem("BalloonFlow/DON'T USE/Fix Prefab Perf Settings (Animator + Particle)")]
        public static void Fix()
        {
            int totalChanges = 0;
            foreach (var path in PREFAB_PATHS)
            {
                int n = FixPrefab(path);
                totalChanges += n;
            }

            AssetDatabase.SaveAssets();
            EditorUtility.DisplayDialog("Prefab Perf Fix",
                $"{totalChanges} 항목 변경.\nConsole 로그 확인.\n\n" +
                "Holder 의 자식 GO/Material 정리는 아트팀 협업 별도.",
                "OK");
        }

        private static int FixPrefab(string path)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (prefab == null)
            {
                Debug.LogWarning($"[PrefabPerfFixer] 미발견: {path}");
                return 0;
            }

            // Prefab contents 로 열어서 수정 → SaveAsPrefabAsset
            var instance = PrefabUtility.LoadPrefabContents(path);
            int n = 0;

            // Animator culling
            var animators = instance.GetComponentsInChildren<Animator>(includeInactive: true);
            for (int i = 0; i < animators.Length; i++)
            {
                if (animators[i].cullingMode != AnimatorCullingMode.CullCompletely)
                {
                    animators[i].cullingMode = AnimatorCullingMode.CullCompletely;
                    Debug.Log($"[PrefabPerfFixer] {prefab.name}: Animator[{i}] cullingMode → CullCompletely");
                    n++;
                }
            }

            // ParticleSystem Renderer culling
            var particles = instance.GetComponentsInChildren<ParticleSystem>(includeInactive: true);
            for (int i = 0; i < particles.Length; i++)
            {
                var renderer = particles[i].GetComponent<ParticleSystemRenderer>();
                if (renderer == null) continue;

                // ParticleSystemRenderer.allowRoll, sortingLayerID 등은 두고 culling 만
                // ParticleSystem.cullingMode 는 사실 ParticleSystem.main 에 있음 (Unity API 위치 주의)
                var main = particles[i].main;
                if (main.cullingMode != ParticleSystemCullingMode.Automatic)
                {
                    main.cullingMode = ParticleSystemCullingMode.Automatic;
                    Debug.Log($"[PrefabPerfFixer] {prefab.name}: ParticleSystem[{i}] cullingMode → Automatic");
                    n++;
                }
            }

            if (n > 0)
            {
                PrefabUtility.SaveAsPrefabAsset(instance, path);
                Debug.Log($"[PrefabPerfFixer] {prefab.name} saved ({n} 변경)");
            }
            PrefabUtility.UnloadPrefabContents(instance);
            return n;
        }
    }
}
