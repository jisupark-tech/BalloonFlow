// [ROLLBACK_LEVELDB_VALIDATOR]
// Editor 전용 — LevelDatabase 의 모든 레벨을 순회하면서 다트 발사 / 풍선 hit 시스템 가정을 어기는 데이터 찾기.
// 메뉴: BalloonFlow → Validate Levels.
// 롤백: 이 파일 통째로 제거.
using UnityEditor;
using UnityEngine;
using System.Text;
using System.Collections.Generic;

namespace BalloonFlow.EditorTools
{
    public class LevelDatabaseValidator : EditorWindow
    {
        private LevelDatabase _db;
        private Vector2 _scroll;
        private string _result = "";
        private bool _verbose = false; // 정상 레벨도 표시할지

        private const string DEFAULT_TRANSFORM_PATH = "Assets/EditorData/LevelDatabase_Transform.asset";

        [MenuItem("BalloonFlow/Validate Levels")]
        static void Open()
        {
            var w = GetWindow<LevelDatabaseValidator>("Level Validator");
            w.minSize = new Vector2(640, 700);
        }

        private void OnEnable()
        {
            if (_db == null)
                _db = AssetDatabase.LoadAssetAtPath<LevelDatabase>(DEFAULT_TRANSFORM_PATH);
        }

        private void OnGUI()
        {
            GUILayout.Label("LevelDatabase 전체 무결성 검수", EditorStyles.boldLabel);
            _db = (LevelDatabase)EditorGUILayout.ObjectField("DB", _db, typeof(LevelDatabase), false);
            _verbose = EditorGUILayout.Toggle("정상 레벨도 표시", _verbose);

            if (GUILayout.Button("Validate", GUILayout.Height(32)))
            {
                _result = ValidateAll(_db, _verbose);
                Debug.Log(_result);
            }

            EditorGUILayout.Space();
            GUILayout.Label("결과 (⚠ = 다트 hit 시스템 가정 위배)", EditorStyles.boldLabel);
            _scroll = EditorGUILayout.BeginScrollView(_scroll);
            EditorGUILayout.TextArea(_result, GUILayout.ExpandHeight(true));
            EditorGUILayout.EndScrollView();
        }

        private static string ValidateAll(LevelDatabase db, bool verbose)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"=== Validate {(db != null ? db.name : "<null>")} ===");
            if (db == null || db.levels == null) { sb.AppendLine("DB 미할당."); return sb.ToString(); }

            int problemCount = 0;
            for (int i = 0; i < db.levels.Length; i++)
            {
                var lv = db.levels[i];
                if (lv == null) continue;
                var issues = ValidateLevel(lv);
                if (issues.Count > 0)
                {
                    problemCount++;
                    sb.AppendLine();
                    sb.AppendLine($"────── Lv #{lv.levelId} ({lv.difficultyPurpose}) ⚠ ATTACK 불가 가능 ──────");
                    foreach (var s in issues) sb.AppendLine($"  {s}");
                }
                else if (verbose)
                {
                    sb.AppendLine($"Lv #{lv.levelId} OK");
                }
            }
            sb.AppendLine();
            sb.AppendLine($"=== 문제 레벨: {problemCount}/{db.levels.Length} ===");
            return sb.ToString();
        }

        private static List<string> ValidateLevel(LevelConfig lv)
        {
            var issues = new List<string>();

            // 1) 풍선 색 ↔ 홀더 색 매칭
            var balloonColors = new Dictionary<int, int>();
            var holderColors = new Dictionary<int, int>();
            if (lv.balloons != null) foreach (var bl in lv.balloons)
            {
                // Wall / FlexTube / Lock_Key 같은 hit 불가/특수 기믹은 매칭 검증에서 제외
                if (IsExemptFromColorMatch(bl.gimmickType)) continue;
                balloonColors.TryGetValue(bl.color, out int cnt); balloonColors[bl.color] = cnt + 1;
            }
            if (lv.holders != null) foreach (var h in lv.holders)
            {
                holderColors.TryGetValue(h.color, out int cnt); holderColors[h.color] = cnt + 1;
            }

            foreach (var kv in balloonColors)
            {
                if (kv.Key < 0)
                {
                    issues.Add($"⚠ color[{kv.Key}] (음수) 풍선 {kv.Value}개 — invalid color index");
                    continue;
                }
                if (kv.Key >= 28)
                {
                    issues.Add($"⚠ color[{kv.Key}] (≥28) 풍선 {kv.Value}개 — BalloonColors 배열 범위 밖");
                    continue;
                }
                if (!holderColors.ContainsKey(kv.Key))
                {
                    issues.Add($"⚠ color[{kv.Key}] 풍선 {kv.Value}개 — 매칭 홀더 0개 (영원히 hit 불가)");
                }
            }

            // 2) 홀더 색에 매칭 풍선 없는 dead holder
            foreach (var kv in holderColors)
            {
                if (kv.Key < 0 || kv.Key >= 28)
                    issues.Add($"⚠ holder color[{kv.Key}] invalid index");
                else if (!balloonColors.ContainsKey(kv.Key))
                    issues.Add($"⚠ holder color[{kv.Key}] {kv.Value}개 — 매칭 풍선 0개 (다트 소진 안 됨)");
            }

            // 3) magazine 합 vs 풍선 수 — 너무 부족하면 클리어 불가
            int magSum = 0;
            if (lv.holders != null) foreach (var h in lv.holders) magSum += h.magazineCount;
            int balloonCountNonExempt = 0;
            foreach (var kv in balloonColors) balloonCountNonExempt += kv.Value;
            if (magSum < balloonCountNonExempt)
                issues.Add($"⚠ magazine 합({magSum}) < 풍선 수({balloonCountNonExempt}) — 다트 부족");

            // 4) 색별 magazine 합 vs 풍선 수 — 색별로 충분?
            var magByColor = new Dictionary<int, int>();
            if (lv.holders != null) foreach (var h in lv.holders)
            {
                magByColor.TryGetValue(h.color, out int s); magByColor[h.color] = s + h.magazineCount;
            }
            foreach (var kv in balloonColors)
            {
                int ballonsOfColor = kv.Value;
                int magOfColor = magByColor.TryGetValue(kv.Key, out var m) ? m : 0;
                if (magOfColor < ballonsOfColor)
                    issues.Add($"⚠ color[{kv.Key}] magazine({magOfColor}) < 풍선({ballonsOfColor}) — 색별 다트 부족");
            }

            // 5) gridCols / gridRows 유효성
            if (lv.gridCols <= 0 || lv.gridRows <= 0)
                issues.Add($"⚠ grid 크기 비정상: cols={lv.gridCols} rows={lv.gridRows}");

            // 6) rail.deployPoints / queueColumns 일치
            int dpLen = lv.rail != null && lv.rail.deployPoints != null ? lv.rail.deployPoints.Length : 0;
            if (dpLen != lv.queueColumns)
                issues.Add($"⚠ deployPoints.Length({dpLen}) ≠ queueColumns({lv.queueColumns})");

            // 7) numColors vs 실제 사용 색
            var validBalloonColors = new HashSet<int>();
            foreach (var c in balloonColors.Keys) if (c >= 0 && c < 28) validBalloonColors.Add(c);
            if (validBalloonColors.Count > lv.numColors)
                issues.Add($"⚠ 실제 사용 색 수({validBalloonColors.Count}) > numColors({lv.numColors}) — meta 불일치");

            // 8) 실제 사용된 색 인덱스 list (시각상 비슷한 색 발견용 — 사용자가 보고 색조 분리 결정)
            if (validBalloonColors.Count > 0)
            {
                var colorListStr = new System.Text.StringBuilder("사용 풍선 색 인덱스: [");
                var sortedColors = new List<int>(validBalloonColors);
                sortedColors.Sort();
                for (int i = 0; i < sortedColors.Count; i++)
                {
                    if (i > 0) colorListStr.Append(",");
                    colorListStr.Append(sortedColors[i]);
                }
                colorListStr.Append("]");
                // 시각상 흰색 비슷 (6/14/16/22), 노랑 비슷 (3/16/25), 회색 비슷 (7/22/23) 등 충돌 가능 인덱스 경고
                CheckVisuallySimilarColors(sortedColors, issues);
            }

            return issues;
        }

        // 시각상 비슷한 색 인덱스 그룹 — 같이 사용 시 사용자가 시각 구분 어려움.
        private static readonly Dictionary<string, int[]> _similarColorGroups = new Dictionary<string, int[]>
        {
            { "흰색계열",   new[] { 6, 14, 16, 22 } },        // White, Periwinkle, Cream, Silver
            { "노랑계열",   new[] { 3, 16, 25 } },            // Yellow, Cream, Amber
            { "회색계열",   new[] { 7, 22, 23 } },            // DarkGray, Silver, Gray
            { "분홍계열",   new[] { 0, 17, 21 } },            // HotPink, Pink, Rose
            { "주황빨강계열", new[] { 5, 10, 21, 24, 26 } },  // Orange, Red, Rose, Magenta, Crimson
            { "파랑계열",   new[] { 1, 8, 11 } },             // Cyan, SkyBlue, Blue
            { "초록계열",   new[] { 4, 9, 12, 19, 27 } },     // Green, Forest, Teal, Mint, Sage
            { "보라계열",   new[] { 2, 13, 18, 20 } },        // Purple, Lavender, Wine, Indigo
        };

        private static void CheckVisuallySimilarColors(List<int> usedColors, List<string> issues)
        {
            var used = new HashSet<int>(usedColors);
            foreach (var group in _similarColorGroups)
            {
                var conflicting = new List<int>();
                foreach (var c in group.Value) if (used.Contains(c)) conflicting.Add(c);
                if (conflicting.Count >= 2)
                {
                    issues.Add($"⚠ {group.Key} 충돌 — 인덱스 [{string.Join(",", conflicting)}] 같이 사용. 시각 구분 어려움 (Zap 같은 단일색 booster 사용 시 사용자 혼란)");
                }
            }
        }

        private static bool IsExemptFromColorMatch(string gimmickType)
        {
            // 색 매칭 검증에서 제외할 기믹 (Wall = indestructible, FlexTube = 자체 시스템, Lock_Key = dead 등)
            return gimmickType == "Wall"
                || gimmickType == "FlexTube"
                || gimmickType == "Lock_Key";
        }
    }
}
