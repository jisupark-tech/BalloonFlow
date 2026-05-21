#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace BalloonFlow.Editor
{
    /// <summary>
    /// LevelDatabase.asset 의 300레벨을 15개 에피소드 단위로 분할 → JSON export → (옵션) Node 업로더 호출.
    /// Episode 1 은 StreamingAssets/episode_01.json 로 함께 export — 앱 번들 fallback.
    ///
    /// 결과물:
    ///   - BalloonFlow/Assets/StreamingAssets/episode_01.json   (Ep1, 빌드에 포함)
    ///   - BallonFlow_Git/firebase/seed/episodes/episode_01.json ~ episode_15.json (업로드용)
    ///
    /// Firestore 업로드는 upload-episodes.js (Admin SDK) 호출.
    /// </summary>
    public static class LevelEpisodeUploader
    {
        private const string DB_PATH           = "Assets/EditorData/LevelDatabase.asset";
        private const string STREAMING_FILE    = "Assets/StreamingAssets/episode_01.json";
        private const int    LEVELS_PER_EP     = 20;
        private const int    EPISODE_VERSION   = 1;

        private const string MENU_ROOT = "BalloonFlow/Level Episodes/";

        [MenuItem(MENU_ROOT + "Export Ep1 → StreamingAssets")]
        public static void ExportEp1ToStreamingAssets()
        {
            var db = LoadDatabase();
            if (db == null) return;

            var ep1 = BuildEpisode(db, packageId: 1);
            if (ep1 == null) return;

            EnsureDirectoryFor(STREAMING_FILE);
            string json = JsonUtility.ToJson(ep1, prettyPrint: false);
            File.WriteAllText(STREAMING_FILE, json);
            AssetDatabase.Refresh();
            Debug.Log($"[LevelEpisodeUploader] Ep1 → {STREAMING_FILE} ({ep1.levels.Length} levels, {json.Length} bytes)");
            EditorUtility.DisplayDialog("Export 완료", $"Episode 1 → StreamingAssets\n{ep1.levels.Length} 레벨", "OK");
        }

        [MenuItem(MENU_ROOT + "Export All Episodes → firebase/seed/episodes")]
        public static void ExportAllEpisodes()
        {
            var db = LoadDatabase();
            if (db == null) return;

            string outDir = GetSeedEpisodesDir();
            Directory.CreateDirectory(outDir);

            int total = 0;
            int exportedEpisodes = 0;
            int skipped = 0;
            for (int pkg = 1; pkg <= 15; pkg++)
            {
                var ep = BuildEpisode(db, pkg);
                if (ep == null || ep.levels == null || ep.levels.Length == 0)
                {
                    skipped++;
                    continue;
                }

                string json = JsonUtility.ToJson(ep, prettyPrint: false);
                string fileName = $"episode_{pkg:D2}.json";
                string fullPath = Path.Combine(outDir, fileName);
                File.WriteAllText(fullPath, json);
                Debug.Log($"  - {fileName}  levels={ep.levels.Length}  bytes={json.Length}");
                total += ep.levels.Length;
                exportedEpisodes++;
            }
            if (skipped > 0)
                Debug.Log($"[LevelEpisodeUploader] {skipped} 에피소드는 데이터 없음 — skip (LevelDatabase 에 {db.levels.Length}레벨 보유)");

            // Ep1 도 StreamingAssets 에 동기화
            ExportEp1ToStreamingAssets_NoDialog(db);

            Debug.Log($"[LevelEpisodeUploader] Export 완료. {exportedEpisodes} 에피소드 / {total} 레벨 → {outDir}");
            EditorUtility.DisplayDialog("Export 완료",
                $"{exportedEpisodes} 에피소드 / {total} 레벨 → {outDir}\n\n다음 단계:\n1) firebase/seed/service-account.json 준비\n2) cd firebase/seed && npm install\n3) node upload-episodes.js",
                "OK");
        }

        [MenuItem(MENU_ROOT + "Export & Upload to Firestore")]
        public static void ExportAndUpload()
        {
            ExportAllEpisodes();

            if (!EditorUtility.DisplayDialog(
                    "Firestore 업로드 확인",
                    "JSON export 완료. 지금 Node 업로더 (upload-episodes.js) 를 실행할까요?\n" +
                    "사전 요구사항:\n" +
                    "- Node.js 설치\n" +
                    "- firebase/seed/service-account.json\n" +
                    "- firebase/seed/node_modules 설치 완료 (npm install)",
                    "업로드 실행", "취소"))
                return;

            RunNodeUploader();
        }

        [MenuItem(MENU_ROOT + "Run Node Uploader Only")]
        public static void RunNodeUploaderMenu()
        {
            RunNodeUploader();
        }

        // ─── helpers ──────────────────────────────────────────────────────

        private static LevelDatabase LoadDatabase()
        {
            var db = AssetDatabase.LoadAssetAtPath<LevelDatabase>(DB_PATH);
            if (db == null || db.levels == null || db.levels.Length == 0)
            {
                Debug.LogError($"[LevelEpisodeUploader] {DB_PATH} 로드 실패 또는 빈 DB");
                EditorUtility.DisplayDialog("실패", $"{DB_PATH} 가 없거나 비어있음", "OK");
                return null;
            }
            return db;
        }

        private static LevelEpisode BuildEpisode(LevelDatabase db, int packageId)
        {
            int startIndex = (packageId - 1) * LEVELS_PER_EP;
            int endIndex   = Mathf.Min(startIndex + LEVELS_PER_EP, db.levels.Length);
            if (startIndex >= db.levels.Length)
                return null;

            var slice = new List<LevelConfig>(LEVELS_PER_EP);
            for (int i = startIndex; i < endIndex; i++)
            {
                if (db.levels[i] != null)
                    slice.Add(db.levels[i]);
            }

            return new LevelEpisode
            {
                packageId  = packageId,
                levelCount = slice.Count,
                version    = EPISODE_VERSION,
                levels     = slice.ToArray()
            };
        }

        private static void ExportEp1ToStreamingAssets_NoDialog(LevelDatabase db)
        {
            var ep1 = BuildEpisode(db, packageId: 1);
            if (ep1 == null) return;

            EnsureDirectoryFor(STREAMING_FILE);
            string json = JsonUtility.ToJson(ep1, prettyPrint: false);
            File.WriteAllText(STREAMING_FILE, json);
            AssetDatabase.Refresh();
            Debug.Log($"[LevelEpisodeUploader] Ep1 → {STREAMING_FILE} ({ep1.levels.Length} levels)");
        }

        private static void EnsureDirectoryFor(string assetPath)
        {
            string dir = Path.GetDirectoryName(assetPath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);
        }

        /// <summary>
        /// firebase/seed/episodes 절대 경로. Unity project root 의 두 단계 위 (BalloonFlow/Assets → repo root).
        /// </summary>
        private static string GetSeedEpisodesDir()
        {
            // Application.dataPath = .../BalloonFlow/Assets
            // repo root            = .../BalloonFlow/ 의 parent
            string assets = Application.dataPath.Replace('\\', '/');
            string unityProj = Path.GetDirectoryName(assets);              // .../BalloonFlow
            string repoRoot  = Path.GetDirectoryName(unityProj);           // .../BallonFlow_Git
            return Path.Combine(repoRoot, "firebase", "seed", "episodes");
        }

        private static string GetSeedDir()
        {
            string assets = Application.dataPath.Replace('\\', '/');
            string unityProj = Path.GetDirectoryName(assets);
            string repoRoot  = Path.GetDirectoryName(unityProj);
            return Path.Combine(repoRoot, "firebase", "seed");
        }

        private static void RunNodeUploader()
        {
            string seedDir = GetSeedDir();
            string script  = Path.Combine(seedDir, "upload-episodes.js");
            if (!File.Exists(script))
            {
                Debug.LogError($"[LevelEpisodeUploader] {script} 없음");
                EditorUtility.DisplayDialog("실패", $"{script} 없음", "OK");
                return;
            }

            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName               = "node",
                    Arguments              = "upload-episodes.js",
                    WorkingDirectory       = seedDir,
                    UseShellExecute        = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError  = true,
                    CreateNoWindow         = true,
                };

                using var proc = Process.Start(psi);
                string stdout = proc.StandardOutput.ReadToEnd();
                string stderr = proc.StandardError.ReadToEnd();
                proc.WaitForExit();

                if (!string.IsNullOrEmpty(stdout)) Debug.Log($"[upload-episodes]\n{stdout}");
                if (!string.IsNullOrEmpty(stderr)) Debug.LogError($"[upload-episodes:stderr]\n{stderr}");

                if (proc.ExitCode == 0)
                    EditorUtility.DisplayDialog("업로드 완료", "Firestore /episodes 업로드 성공.\nFirebase Console 에서 확인하세요.", "OK");
                else
                    EditorUtility.DisplayDialog("업로드 실패", $"exit code {proc.ExitCode}\n자세한 내용은 Console 참고", "OK");
            }
            catch (Exception e)
            {
                Debug.LogError($"[LevelEpisodeUploader] node 실행 실패: {e.Message}");
                EditorUtility.DisplayDialog("실패", $"node 실행 실패: {e.Message}\n\nNode.js 설치 + PATH 확인", "OK");
            }
        }
    }
}
#endif
