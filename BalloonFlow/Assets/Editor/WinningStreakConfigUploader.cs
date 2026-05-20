#if UNITY_EDITOR
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace BalloonFlow.Editor
{
    /// <summary>
    /// WinningStreak 이벤트 시드 데이터 → JSON export.
    /// LevelEpisodeUploader 와 동일 패턴: JSON 파일만 생성, Firestore 업로드는 Node Admin SDK 가 담당.
    ///
    /// 결과물: BallonFlow_Git/firebase/seed/winningStreak/config.json
    ///
    /// Firestore 경로: /config/winningStreak (단일 doc)
    /// </summary>
    public static class WinningStreakConfigUploader
    {
        private const string MENU_ROOT = "BalloonFlow/Winning Streak/";
        private const string SEED_DIRNAME = "winningStreak";
        private const string SEED_FILENAME = "config.json";

        [MenuItem(MENU_ROOT + "Export Config → firebase/seed/winningStreak")]
        public static void ExportConfigJson()
        {
            string json = BuildDefaultConfigJson();

            string outDir = GetSeedDir();
            Directory.CreateDirectory(outDir);
            string outPath = Path.Combine(outDir, SEED_FILENAME);
            File.WriteAllText(outPath, json, Encoding.UTF8);

            Debug.Log($"[WinningStreakConfigUploader] Exported → {outPath} ({json.Length} bytes)");
            EditorUtility.DisplayDialog(
                "Export 완료",
                $"WinningStreak config → {outPath}\n\n" +
                "다음 단계:\n" +
                "1) firebase/seed/service-account.json 준비\n" +
                "2) Node 업로더로 /config/winningStreak doc 업로드\n" +
                "   (upload-episodes.js 와 동일 패턴으로 작성)",
                "OK");
        }

        // ── 기획서 표 기반 기본 25 stage + meta ──────────────────────

        private static string BuildDefaultConfigJson()
        {
            var sb = new StringBuilder(8192);
            sb.Append("{\n");
            sb.Append("  \"unlockLevel\": 34,\n");
            sb.Append("  \"streakMultipliers\": { \"streak1\": 1, \"streak2\": 5, \"streak3\": 10, \"streak4\": 25, \"streak5Plus\": 100 },\n");
            sb.Append("  \"difficultyMultipliers\": { \"normal\": 1, \"hard\": 3, \"superHard\": 5 },\n");
            sb.Append("  \"boosterCosts\": { \"undo\": 300, \"shuffle\": 300, \"slotExpand3\": 900, \"magnet\": 600 },\n");
            sb.Append("  \"stages\": [\n");

            // (stage, required, coins, hand, shuffle, zap, infiniteHeartsSeconds)
            int[,] table =
            {
                // stage, requiredPoints, coins, hand, shuffle, zap, infiniteHeartsSeconds
                {  1,    1,    0, 0, 0, 0,  900 }, // 무제한 하트 15분
                {  2,   10,   50, 0, 0, 0,    0 },
                {  3,  200,    0, 0, 1, 0,    0 }, // 셔플1
                {  4,  500,  100, 0, 0, 0,    0 },
                {  5,  250,    0, 0, 0, 0,  900 }, // 무제한 하트 15분
                {  6, 1000,    0, 1, 1, 0,    0 }, // 핸드1, 셔플1
                {  7,  200,  100, 0, 0, 0,    0 },
                {  8,  250,    0, 0, 1, 0,    0 }, // 셔플1
                {  9,  500,    0, 0, 0, 0, 1800 }, // 무제한 하트 30분
                { 10, 1000,    0, 1, 1, 0,    0 }, // 핸드1, 셔플1
                { 11,  200,  200, 0, 0, 0,    0 },
                { 12,  500,    0, 0, 1, 0,    0 }, // 셔플1
                { 13,  750,    0, 1, 0, 0,    0 }, // 핸드1
                { 14, 1500,    0, 0, 0, 0, 1800 }, // 무제한 하트 30분
                { 15,  250,    0, 0, 1, 1,    0 }, // 셔플1, 잽1
                { 16,  500,  400, 0, 0, 0,    0 },
                { 17, 1000,    0, 1, 0, 1,    0 }, // 핸드1, 잽1
                { 18, 2000,    0, 1, 0, 0,    0 }, // 핸드1
                { 19,  500,    0, 0, 0, 0, 3600 }, // 무제한 하트 1시간
                { 20, 1000,    0, 1, 1, 1,    0 }, // 핸드1, 셔플1, 잽1
                { 21, 1500,    0, 0, 1, 0,    0 }, // 셔플1
                { 22, 3000, 1000, 0, 0, 0,    0 },
                { 23,  750,    0, 0, 0, 1,    0 }, // 잽1
                { 24, 2000,    0, 0, 0, 0, 7200 }, // 무제한 하트 2시간
                { 25, 5000, 5000, 0, 0, 0,    0 }
            };

            int rows = table.GetLength(0);
            for (int i = 0; i < rows; i++)
            {
                int stage = table[i, 0];
                int required = table[i, 1];
                int coins = table[i, 2];
                int hand = table[i, 3];
                int shuffle = table[i, 4];
                int zap = table[i, 5];
                int infHearts = table[i, 6];

                sb.Append("    { ");
                sb.Append($"\"stage\": {stage}, \"requiredPoints\": {required}, ");
                sb.Append("\"rewards\": { ");
                sb.Append($"\"coins\": {coins}, ");
                sb.Append($"\"boosters\": {{ \"hand\": {hand}, \"shuffle\": {shuffle}, \"zap\": {zap} }}, ");
                sb.Append($"\"infiniteHeartsSeconds\": {infHearts}, \"removeAds\": false ");
                sb.Append("}, ");
                sb.Append("\"collectionXRewards\": { \"coins\": 0, \"boosters\": { \"hand\": 0, \"shuffle\": 0, \"zap\": 0 }, \"infiniteHeartsSeconds\": 0, \"removeAds\": false } ");
                sb.Append("}");
                if (i < rows - 1) sb.Append(",");
                sb.Append("\n");
            }

            sb.Append("  ]\n");
            sb.Append("}\n");
            return sb.ToString();
        }

        private static string GetSeedDir()
        {
            // Application.dataPath = .../BalloonFlow/Assets
            string assets = Application.dataPath.Replace('\\', '/');
            string unityProj = Path.GetDirectoryName(assets);
            string repoRoot = Path.GetDirectoryName(unityProj);
            return Path.Combine(repoRoot ?? assets, "firebase", "seed", SEED_DIRNAME);
        }
    }
}
#endif
