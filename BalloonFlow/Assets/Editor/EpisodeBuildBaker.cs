// ROLLBACK_AB_EDITORDATA_20260714: 빌드 시 EditorData/{Master,Test} → StreamingAssets bake.
//   EditorData 는 빌드에 포함되지 않으므로(스트립), 빌드 직전 유효 에피소드를 StreamingAssets(번들 포함)로 복사한다.
//   - Master/episode_XX.json      → StreamingAssets/episode_XX.json      (base = A variant)
//   - Test/episode_XX_B.json      → StreamingAssets/episode_XX_B.json    (B variant, 전역 A/B 로 B 유저가 로드)
//   런타임(빌드)은 StreamingAssets 를 읽고, 에디터 Play 는 EditorData 를 직접 읽는다(LevelEpisodeService).
//   롤백: 이 파일 삭제(에디터 Play 는 계속 동작, 빌드만 StreamingAssets 수동 관리로 회귀).
using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace BalloonFlow.EditorTools
{
    public class EpisodeBuildBaker : IPreprocessBuildWithReport
    {
        private const string MasterDir    = "Assets/EditorData/Master";
        private const string TestDir      = "Assets/EditorData/B";
        private const string StreamingDir = "Assets/StreamingAssets";

        public int callbackOrder => 0;

        public void OnPreprocessBuild(BuildReport report) => Bake();

        [MenuItem("BalloonFlow/Level Episodes/Bake EditorData → StreamingAssets", false, 12)]
        public static void BakeMenu()
        {
            Bake();
            AssetDatabase.Refresh();
            EditorUtility.DisplayDialog("Episode Bake", "EditorData/{Master,Test} → StreamingAssets bake 완료.", "OK");
        }

        private static void Bake()
        {
            Directory.CreateDirectory(StreamingDir);

            // 기존 bake 산출물 정리(제거된 에피소드가 남지 않게). .meta 는 Refresh 가 정리.
            foreach (var f in Directory.GetFiles(StreamingDir, "episode_*.json"))
                File.Delete(f);

            int baseN = 0, bN = 0;

            // Master → base (episode_XX.json). 방어적으로 _B 는 제외(Master 는 base 전용).
            if (Directory.Exists(MasterDir))
            {
                foreach (var f in Directory.GetFiles(MasterDir, "episode_*.json"))
                {
                    string name = Path.GetFileName(f);
                    if (name.EndsWith("_B.json", System.StringComparison.OrdinalIgnoreCase)) continue;
                    File.Copy(f, Path.Combine(StreamingDir, name), true);
                    baseN++;
                }
            }

            // Test → B variant (episode_XX_B.json) 만.
            if (Directory.Exists(TestDir))
            {
                foreach (var f in Directory.GetFiles(TestDir, "episode_*_B.json"))
                {
                    File.Copy(f, Path.Combine(StreamingDir, Path.GetFileName(f)), true);
                    bN++;
                }
            }

            // ROLLBACK_AB_EDITORDATA_20260714: 실측 최대 에피소드 번호를 Resources 매니페스트로 기록(빌드 런타임의
            //   전량-클리어 게이트 GetLevelCount 소스 — 빌드에선 StreamingAssets 디렉터리 열거가 불가하므로).
            int maxMaster = MaxEpisodeNum(MasterDir);                          // A(base) 상한
            int maxBoth   = System.Math.Max(maxMaster, MaxEpisodeNum(TestDir)); // B 상한(Test 전용 신규 포함)
            Directory.CreateDirectory("Assets/Resources");
            File.WriteAllText("Assets/Resources/episodes_max.txt", maxMaster.ToString());
            File.WriteAllText("Assets/Resources/episodes_max_b.txt", maxBoth.ToString());

            AssetDatabase.Refresh();
            Debug.Log($"[EpisodeBuildBaker] bake 완료 — base(Master) {baseN}개 + B(Test) {bN}개 → {StreamingDir} | maxMaster={maxMaster} maxBoth={maxBoth} (Resources/episodes_max[_b])");
        }

        // episode_XX.json / episode_XX_B.json 파일명에서 최대 XX.
        private static int MaxEpisodeNum(string dir)
        {
            if (!Directory.Exists(dir)) return 0;
            int max = 0;
            foreach (var f in Directory.GetFiles(dir, "episode_*.json"))
            {
                string name = Path.GetFileNameWithoutExtension(f);
                string[] parts = name.Split('_');
                if (parts.Length >= 2 && int.TryParse(parts[1], out int n)) max = System.Math.Max(max, n);
            }
            return max;
        }
    }
}
