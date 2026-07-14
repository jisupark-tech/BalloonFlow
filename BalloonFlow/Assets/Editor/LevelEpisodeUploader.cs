#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace BalloonFlow.Editor
{
    /// <summary>
    /// [2026-06-12 개편] Level Episodes 메뉴 — 레거시 SO(LevelDatabase.asset) 기반 기능 전부 폐기.
    /// 현행 단일 스토어 = Assets/EditorData/Episodes/episode_XX.json (MapMaker/Importer 라운드트립).
    ///
    ///   1) Upload Episodes to Firestore
    ///      : EditorData/Episodes → firebase/seed/episodes 복사 → node upload-episodes.js 실행 (SO 미경유)
    ///   2) Merge Level JSON Folder → Episodes...
    ///      : 사용자가 지정한 폴더의 개별 레벨 JSON(MapMaker 'Export Level JSON' 산출물 또는 LevelConfig)을
    ///        기존 episode 에 levelId 단위로만 교체/추가 — 예: 1,5,6,15,18 만 만들었으면 episode_01 의
    ///        해당 레벨들만 갱신되고 나머지는 보존. 적용 전 episode 자동 백업.
    /// </summary>
    public static class LevelEpisodeUploader
    {
        private const string STREAMING_FILE  = "Assets/StreamingAssets/episode_01.json";
        private const string EDITORDATA_DIR  = "Assets/EditorData/Master";
        private const string BACKUP_DIR      = "Assets/LevelBackups";
        private const int    LEVELS_PER_EP   = 20;
        private const int    TOTAL_EPISODES  = 15;
        private const int    EPISODE_VERSION = 1;

        private const string MENU_ROOT = "BalloonFlow/Level Episodes/";

        // ─── 1) Firestore 업로드 — 현행 JSON 스토어 기준 ─────────────────────

        [MenuItem(MENU_ROOT + "Upload Episodes to Firestore", false, 10)]
        public static void UploadEpisodesToFirestore()
        {
            if (!Directory.Exists(EDITORDATA_DIR)
                || Directory.GetFiles(EDITORDATA_DIR, "episode_*.json").Length == 0)
            {
                EditorUtility.DisplayDialog("실패",
                    $"{EDITORDATA_DIR} 에 episode_*.json 없음.\nMapMaker 에서 저장하거나 git pull 먼저 하세요.", "OK");
                return;
            }

            CopyEditorDataEpisodesToSeed();

            if (!EditorUtility.DisplayDialog("Firestore 업로드",
                    "EditorData/Episodes → firebase/seed/episodes 복사 완료.\n" +
                    "지금 Node 업로더(upload-episodes.js)를 실행할까요?\n\n" +
                    "사전 요구사항:\n- Node.js 설치\n- firebase/seed/service-account.json\n- firebase/seed npm install 완료",
                    "업로드 실행", "취소"))
                return;

            RunNodeUploader();
        }

        // ─── 2) 개별 레벨 JSON 폴더 → Episode 부분 병합 ──────────────────────

        [MenuItem(MENU_ROOT + "Merge Level JSON Folder → Episodes...", false, 11)]
        public static void MergeLevelJsonFolder()
        {
            string defaultDir = Directory.Exists(BACKUP_DIR) ? BACKUP_DIR : "Assets";
            string folder = EditorUtility.OpenFolderPanel("개별 레벨 JSON 폴더 선택", defaultDir, "");
            if (string.IsNullOrEmpty(folder)) return;

            string[] files = Directory.GetFiles(folder, "*.json", SearchOption.TopDirectoryOnly);
            if (files.Length == 0)
            {
                EditorUtility.DisplayDialog("실패", $"{folder} 에 .json 파일 없음.", "OK");
                return;
            }

            // 레벨 수집 — LevelEpisode 컨테이너(levels 배열) / 단일 LevelConfig 모두 지원.
            var byId = new Dictionary<int, LevelConfig>();
            var parseErrors = new List<string>();
            foreach (string f in files.OrderBy(p => p))
            {
                try
                {
                    string text = File.ReadAllText(f);
                    if (text.Contains("\"levels\"", StringComparison.Ordinal))
                    {
                        var ep = JsonUtility.FromJson<LevelEpisode>(text);
                        if (ep?.levels != null)
                            foreach (var lv in ep.levels)
                                if (lv != null && lv.levelId > 0) byId[lv.levelId] = lv;
                    }
                    else if (text.Contains("\"levelId\"", StringComparison.Ordinal))
                    {
                        var lv = JsonUtility.FromJson<LevelConfig>(text);
                        if (lv != null && lv.levelId > 0) byId[lv.levelId] = lv;
                    }
                }
                catch (Exception e)
                {
                    parseErrors.Add($"{Path.GetFileName(f)}: {e.Message}");
                }
            }

            if (byId.Count == 0)
            {
                EditorUtility.DisplayDialog("실패",
                    "유효한 레벨 JSON 없음." + (parseErrors.Count > 0 ? $"\n오류 {parseErrors.Count}건 — Console 참고." : ""), "OK");
                foreach (var err in parseErrors) Debug.LogError($"[LevelEpisodeUploader] {err}");
                return;
            }

            // 패키지별 그룹 → 기존 episode 에 levelId 단위 교체/추가 (나머지 레벨 보존).
            var byPkg = byId.Values.GroupBy(lv => PackageIdForLevel(lv.levelId))
                            .OrderBy(g => g.Key);
            string ts = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            int replaced = 0, added = 0, outOfRange = 0;
            var summary = new StringBuilder();

            foreach (var grp in byPkg)
            {
                int pkg = grp.Key;
                if (pkg < 1 || pkg > TOTAL_EPISODES)
                {
                    outOfRange += grp.Count();
                    Debug.LogWarning($"[LevelEpisodeUploader] pkg {pkg} 범위 밖(1~{TOTAL_EPISODES}) — skip: " +
                                     string.Join(",", grp.Select(l => l.levelId)));
                    continue;
                }

                string path = $"{EDITORDATA_DIR}/episode_{pkg:D2}.json";
                var levels = new List<LevelConfig>();
                if (File.Exists(path))
                {
                    try
                    {
                        var existing = JsonUtility.FromJson<LevelEpisode>(File.ReadAllText(path));
                        if (existing?.levels != null)
                            foreach (var lv in existing.levels) if (lv != null) levels.Add(lv);
                    }
                    catch (Exception e)
                    {
                        Debug.LogError($"[LevelEpisodeUploader] {path} 읽기 실패: {e.Message} — 이 episode skip.");
                        continue;
                    }

                    // 덮어쓰기 전 백업.
                    Directory.CreateDirectory(BACKUP_DIR);
                    File.Copy(path, $"{BACKUP_DIR}/episode_{pkg:D2}_{ts}.json", true);
                }

                var mergedIds = new List<int>();
                foreach (var lv in grp.OrderBy(l => l.levelId))
                {
                    int idx = levels.FindIndex(l => l.levelId == lv.levelId);
                    if (idx >= 0) { levels[idx] = lv; replaced++; }
                    else          { levels.Add(lv);  added++; }
                    mergedIds.Add(lv.levelId);
                }

                // levelId 정렬 + packageId/positionInPackage 정규화 (런타임 position 인덱싱 보장).
                levels.Sort((a, b) => a.levelId.CompareTo(b.levelId));
                foreach (var lv in levels)
                {
                    lv.packageId = pkg;
                    lv.positionInPackage = PositionInPackage(lv.levelId);
                }

                WriteEpisodeFile(pkg, levels);
                summary.AppendLine($"episode_{pkg:D2}: {string.Join(",", mergedIds)}");
            }

            EditorUtility.DisplayDialog("Episode 병합 완료",
                $"교체 {replaced} / 추가 {added}" +
                (outOfRange > 0 ? $" / 범위밖 skip {outOfRange}" : "") +
                (parseErrors.Count > 0 ? $" / 파싱오류 {parseErrors.Count}" : "") +
                $"\n\n{summary}" +
                "\npkg1 은 StreamingAssets 동기화 완료. pkg2~15 라이브 반영은\n'Upload Episodes to Firestore' 를 실행하세요." +
                "\nMapMaker 가 열려있으면 Reload 버튼으로 갱신.",
                "OK");
            Debug.Log($"[LevelEpisodeUploader] 폴더 병합 완료 — 교체 {replaced}, 추가 {added}\n{summary}");
        }

        // ─── helpers ──────────────────────────────────────────────────────

        private static int PackageIdForLevel(int levelId)
            => levelId < 1 ? 0 : ((levelId - 1) / LEVELS_PER_EP) + 1;

        private static int PositionInPackage(int levelId)
            => levelId < 1 ? 1 : ((levelId - 1) % LEVELS_PER_EP) + 1;

        private static void WriteEpisodeFile(int pkg, List<LevelConfig> levels)
        {
            var ep = new LevelEpisode
            {
                packageId  = pkg,
                levelCount = levels.Count,
                version    = EPISODE_VERSION,
                levels     = levels.ToArray()
            };
            string json = JsonUtility.ToJson(ep, false); // MapMaker/Importer/런타임과 동일 포맷

            Directory.CreateDirectory(EDITORDATA_DIR);
            string path = $"{EDITORDATA_DIR}/episode_{pkg:D2}.json";
            File.WriteAllText(path, json);
            AssetDatabase.ImportAsset(path);

            if (pkg == 1)
            {
                string streamDir = Path.GetDirectoryName(STREAMING_FILE);
                if (!string.IsNullOrEmpty(streamDir)) Directory.CreateDirectory(streamDir);
                File.WriteAllText(STREAMING_FILE, json);
                AssetDatabase.ImportAsset(STREAMING_FILE);
            }
            Debug.Log($"[LevelEpisodeUploader] {path} 갱신 ({levels.Count}레벨)");
        }

        /// <summary>EditorData/Episodes/*.json → firebase/seed/episodes (node 업로더가 읽는 위치).</summary>
        private static void CopyEditorDataEpisodesToSeed()
        {
            string seedDir = GetSeedEpisodesDir();
            Directory.CreateDirectory(seedDir);
            int copied = 0;
            foreach (var f in Directory.GetFiles(EDITORDATA_DIR, "episode_*.json"))
            {
                File.Copy(f, Path.Combine(seedDir, Path.GetFileName(f)), overwrite: true);
                copied++;
            }
            Debug.Log($"[LevelEpisodeUploader] {copied}개 episode → {seedDir}");
        }

        /// <summary>firebase/seed/episodes 절대 경로 — Unity project root 의 한 단계 위(repo root) 기준.</summary>
        private static string GetSeedEpisodesDir()
        {
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
