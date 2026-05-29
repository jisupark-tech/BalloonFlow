// [ROLLBACK_LEVELDB_COMPARE_TOOL]
// Editor 전용 — 두 LevelDatabase 의 특정 levelId LevelConfig 차이 비교.
// 메뉴: BalloonFlow → Compare DB Levels.
// 사용 시나리오: Transform DB 의 24렙이 Ori DB 의 40렙에서 이식된 경우 누락/변형 확인.
// 롤백: 이 파일 통째로 제거.
using UnityEditor;
using UnityEngine;
using System.Text;

namespace BalloonFlow.EditorTools
{
    public class LevelDatabaseCompareTool : EditorWindow
    {
        private LevelDatabase _dbA;
        private LevelDatabase _dbB;
        private int _levelIdA = 24;
        private int _levelIdB = 40;
        private Vector2 _scroll;
        private string _result = "";

        private const string DEFAULT_TRANSFORM_PATH = "Assets/EditorData/LevelDatabase_Transform.asset";
        private const string DEFAULT_ORI_PATH = "Assets/EditorData/LevelDatabase.asset";

        [MenuItem("BalloonFlow/Compare DB Levels")]
        static void Open()
        {
            var w = GetWindow<LevelDatabaseCompareTool>("DB Level Compare");
            w.minSize = new Vector2(560, 600);
        }

        private void OnEnable()
        {
            // 기본 DB 자동 로드
            if (_dbA == null)
                _dbA = AssetDatabase.LoadAssetAtPath<LevelDatabase>(DEFAULT_TRANSFORM_PATH);
            if (_dbB == null)
                _dbB = AssetDatabase.LoadAssetAtPath<LevelDatabase>(DEFAULT_ORI_PATH);
        }

        private void OnGUI()
        {
            GUILayout.Label("DB A (= Transform 비교 대상)", EditorStyles.boldLabel);
            _dbA = (LevelDatabase)EditorGUILayout.ObjectField("DB A", _dbA, typeof(LevelDatabase), false);
            _levelIdA = EditorGUILayout.IntField("Level ID (A)", _levelIdA);

            EditorGUILayout.Space();

            GUILayout.Label("DB B (= Ori 원본)", EditorStyles.boldLabel);
            _dbB = (LevelDatabase)EditorGUILayout.ObjectField("DB B", _dbB, typeof(LevelDatabase), false);
            _levelIdB = EditorGUILayout.IntField("Level ID (B)", _levelIdB);

            EditorGUILayout.Space();

            if (GUILayout.Button("Compare", GUILayout.Height(32)))
            {
                _result = Compare(_dbA, _levelIdA, _dbB, _levelIdB);
                Debug.Log(_result);
            }

            EditorGUILayout.Space();
            GUILayout.Label("결과", EditorStyles.boldLabel);
            _scroll = EditorGUILayout.BeginScrollView(_scroll);
            EditorGUILayout.TextArea(_result, GUILayout.ExpandHeight(true));
            EditorGUILayout.EndScrollView();
        }

        private static LevelConfig FindLevel(LevelDatabase db, int levelId)
        {
            if (db == null || db.levels == null) return null;
            for (int i = 0; i < db.levels.Length; i++)
                if (db.levels[i] != null && db.levels[i].levelId == levelId) return db.levels[i];
            return null;
        }

        private static string Compare(LevelDatabase dbA, int levelIdA, LevelDatabase dbB, int levelIdB)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"=== Compare A({(dbA != null ? dbA.name : "<null>")} #{levelIdA}) ↔ B({(dbB != null ? dbB.name : "<null>")} #{levelIdB}) ===");

            if (dbA == null || dbB == null) { sb.AppendLine("DB 미할당."); return sb.ToString(); }

            var lvA = FindLevel(dbA, levelIdA);
            var lvB = FindLevel(dbB, levelIdB);
            if (lvA == null) sb.AppendLine($"[ERROR] A 에 levelId={levelIdA} 없음");
            if (lvB == null) sb.AppendLine($"[ERROR] B 에 levelId={levelIdB} 없음");
            if (lvA == null || lvB == null) return sb.ToString();

            // 메타 비교
            sb.AppendLine();
            sb.AppendLine("[메타]");
            Diff(sb, "numColors", lvA.numColors, lvB.numColors);
            Diff(sb, "balloonCount", lvA.balloonCount, lvB.balloonCount);
            Diff(sb, "queueColumns", lvA.queueColumns, lvB.queueColumns);
            Diff(sb, "balloonScale", lvA.balloonScale, lvB.balloonScale);
            Diff(sb, "targetClearRate", lvA.targetClearRate, lvB.targetClearRate);
            Diff(sb, "railCapacity", lvA.railCapacity, lvB.railCapacity);
            Diff(sb, "gridCols", lvA.gridCols, lvB.gridCols);
            Diff(sb, "gridRows", lvA.gridRows, lvB.gridRows);
            Diff(sb, "difficultyPurpose", lvA.difficultyPurpose, lvB.difficultyPurpose);

            // gimmickTypes
            sb.AppendLine();
            sb.AppendLine("[gimmickTypes]");
            sb.AppendLine($"A: [{(lvA.gimmickTypes != null ? string.Join(",", lvA.gimmickTypes) : "<null>")}]");
            sb.AppendLine($"B: [{(lvB.gimmickTypes != null ? string.Join(",", lvB.gimmickTypes) : "<null>")}]");

            // 풍선 카운트 + 기믹별 카운트
            sb.AppendLine();
            sb.AppendLine("[풍선 통계]");
            CompareBalloonStats(sb, lvA, lvB);

            // 홀더
            sb.AppendLine();
            sb.AppendLine("[홀더]");
            int holdersA = lvA.holders != null ? lvA.holders.Length : 0;
            int holdersB = lvB.holders != null ? lvB.holders.Length : 0;
            Diff(sb, "holders.Length", holdersA, holdersB);
            CompareHolderStats(sb, lvA, lvB);

            // 레일
            sb.AppendLine();
            sb.AppendLine("[레일]");
            int wpA = lvA.rail != null && lvA.rail.waypoints != null ? lvA.rail.waypoints.Length : 0;
            int wpB = lvB.rail != null && lvB.rail.waypoints != null ? lvB.rail.waypoints.Length : 0;
            Diff(sb, "rail.waypoints.Length", wpA, wpB);
            int dpA = lvA.rail != null && lvA.rail.deployPoints != null ? lvA.rail.deployPoints.Length : 0;
            int dpB = lvB.rail != null && lvB.rail.deployPoints != null ? lvB.rail.deployPoints.Length : 0;
            Diff(sb, "rail.deployPoints.Length", dpA, dpB);
            if (lvA.rail != null && lvB.rail != null)
            {
                Diff(sb, "rail.slotCount", lvA.rail.slotCount, lvB.rail.slotCount);
                Diff(sb, "rail.visualType", lvA.rail.visualType, lvB.rail.visualType);
                Diff(sb, "rail.smoothCorners", lvA.rail.smoothCorners, lvB.rail.smoothCorners);
                Diff(sb, "rail.cornerRadius", lvA.rail.cornerRadius, lvB.rail.cornerRadius);
            }

            sb.AppendLine();
            sb.AppendLine("=== Done ===");
            return sb.ToString();
        }

        private static void Diff<T>(StringBuilder sb, string field, T a, T b)
        {
            bool same = (a == null && b == null) || (a != null && a.Equals(b));
            sb.AppendLine($"  {(same ? " " : "≠")} {field}: A={a}  B={b}");
        }

        private static void CompareBalloonStats(StringBuilder sb, LevelConfig a, LevelConfig b)
        {
            // 색 인덱스 모든 값 처리 — invalid (음수 / 12+) 도 별도 카운트.
            var byColorA = new System.Collections.Generic.Dictionary<int, int>();
            var byColorB = new System.Collections.Generic.Dictionary<int, int>();
            var byGimmickA = new System.Collections.Generic.Dictionary<string, int>();
            var byGimmickB = new System.Collections.Generic.Dictionary<string, int>();
            int totalA = a.balloons != null ? a.balloons.Length : 0;
            int totalB = b.balloons != null ? b.balloons.Length : 0;

            if (a.balloons != null) foreach (var bl in a.balloons)
            {
                byColorA.TryGetValue(bl.color, out int cnt); byColorA[bl.color] = cnt + 1;
                string g = string.IsNullOrEmpty(bl.gimmickType) ? "(none)" : bl.gimmickType;
                byGimmickA.TryGetValue(g, out int gcnt); byGimmickA[g] = gcnt + 1;
            }
            if (b.balloons != null) foreach (var bl in b.balloons)
            {
                byColorB.TryGetValue(bl.color, out int cnt); byColorB[bl.color] = cnt + 1;
                string g = string.IsNullOrEmpty(bl.gimmickType) ? "(none)" : bl.gimmickType;
                byGimmickB.TryGetValue(g, out int gcnt); byGimmickB[g] = gcnt + 1;
            }

            Diff(sb, "balloons.Length", totalA, totalB);
            sb.AppendLine("  색별(모든 인덱스 — invalid 포함):");
            var allColors = new System.Collections.Generic.HashSet<int>(byColorA.Keys);
            foreach (var k in byColorB.Keys) allColors.Add(k);
            var sortedColors = new System.Collections.Generic.List<int>(allColors);
            sortedColors.Sort();
            int sumA = 0, sumB = 0;
            foreach (var c in sortedColors)
            {
                int ca = byColorA.TryGetValue(c, out var av) ? av : 0;
                int cb = byColorB.TryGetValue(c, out var bv) ? bv : 0;
                string warn = (c < 0 || c >= 12) ? "  ⚠INVALID" : "";
                Diff(sb, $"    color[{c}]{warn}", ca, cb);
                sumA += ca;
                sumB += cb;
            }
            sb.AppendLine($"  합계: A={sumA}  B={sumB}  (Length: A={totalA} B={totalB})");
            sb.AppendLine("  기믹별:");
            var keys = new System.Collections.Generic.HashSet<string>(byGimmickA.Keys);
            foreach (var k in byGimmickB.Keys) keys.Add(k);
            foreach (var k in keys)
            {
                int ca = byGimmickA.TryGetValue(k, out var av) ? av : 0;
                int cb = byGimmickB.TryGetValue(k, out var bv) ? bv : 0;
                Diff(sb, $"    {k}", ca, cb);
            }
        }

        private static void CompareHolderStats(StringBuilder sb, LevelConfig a, LevelConfig b)
        {
            var byColorA = new System.Collections.Generic.Dictionary<int, int>();
            var byColorB = new System.Collections.Generic.Dictionary<int, int>();
            var byGimmickA = new System.Collections.Generic.Dictionary<string, int>();
            var byGimmickB = new System.Collections.Generic.Dictionary<string, int>();
            int magSumA = 0, magSumB = 0;

            if (a.holders != null) foreach (var h in a.holders)
            {
                byColorA.TryGetValue(h.color, out int cnt); byColorA[h.color] = cnt + 1;
                magSumA += h.magazineCount;
                string g = string.IsNullOrEmpty(h.queueGimmick) ? "(none)" : h.queueGimmick;
                byGimmickA.TryGetValue(g, out int gcnt); byGimmickA[g] = gcnt + 1;
            }
            if (b.holders != null) foreach (var h in b.holders)
            {
                byColorB.TryGetValue(h.color, out int cnt); byColorB[h.color] = cnt + 1;
                magSumB += h.magazineCount;
                string g = string.IsNullOrEmpty(h.queueGimmick) ? "(none)" : h.queueGimmick;
                byGimmickB.TryGetValue(g, out int gcnt); byGimmickB[g] = gcnt + 1;
            }

            Diff(sb, "magazine 총합", magSumA, magSumB);
            sb.AppendLine("  색별(모든 인덱스):");
            var allColors = new System.Collections.Generic.HashSet<int>(byColorA.Keys);
            foreach (var k in byColorB.Keys) allColors.Add(k);
            var sortedColors = new System.Collections.Generic.List<int>(allColors);
            sortedColors.Sort();
            foreach (var c in sortedColors)
            {
                int ca = byColorA.TryGetValue(c, out var av) ? av : 0;
                int cb = byColorB.TryGetValue(c, out var bv) ? bv : 0;
                string warn = (c < 0 || c >= 12) ? "  ⚠INVALID" : "";
                Diff(sb, $"    color[{c}]{warn}", ca, cb);
            }
            sb.AppendLine("  큐 기믹별:");
            var keys = new System.Collections.Generic.HashSet<string>(byGimmickA.Keys);
            foreach (var k in byGimmickB.Keys) keys.Add(k);
            foreach (var k in keys)
            {
                int ca = byGimmickA.TryGetValue(k, out var av) ? av : 0;
                int cb = byGimmickB.TryGetValue(k, out var bv) ? bv : 0;
                Diff(sb, $"    {k}", ca, cb);
            }

            // 풍선 색 vs 홀더 색 매칭 검증.
            sb.AppendLine();
            sb.AppendLine("[색 매칭 검증 — 풍선 색에 매칭 홀더 색 존재?]");
            CompareColorMatching(sb, "A", a, byColorA);
            CompareColorMatching(sb, "B", b, byColorB);
        }

        private static void CompareColorMatching(StringBuilder sb, string label, LevelConfig lv, System.Collections.Generic.Dictionary<int, int> holderColors)
        {
            var balloonColors = new System.Collections.Generic.Dictionary<int, int>();
            if (lv.balloons != null) foreach (var bl in lv.balloons)
            {
                balloonColors.TryGetValue(bl.color, out int cnt); balloonColors[bl.color] = cnt + 1;
            }
            int unmatched = 0;
            int unmatchedBalloons = 0;
            foreach (var kv in balloonColors)
            {
                if (!holderColors.ContainsKey(kv.Key))
                {
                    unmatched++;
                    unmatchedBalloons += kv.Value;
                    string warn = (kv.Key < 0 || kv.Key >= 12) ? "  ⚠INVALID" : "";
                    sb.AppendLine($"  {label}: color[{kv.Key}]{warn} 풍선 {kv.Value}개 — 매칭 홀더 없음 → 영원히 hit 불가!");
                }
            }
            sb.AppendLine($"  {label}: 매칭 안 되는 색 {unmatched}종, 풍선 {unmatchedBalloons}개 ⚠ ATTACK 불가");
        }
    }
}
