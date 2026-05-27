using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using BalloonFlow;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Settings;
using UnityEngine;

namespace BalloonFlow.EditorTools
{
    /// <summary>
    /// Const.cs 의 ADDR_* 상수와 Addressables 그룹 entries 의 address 를 양방향 비교.
    /// 메뉴 1회 실행으로 C:/tmp/AddressableKeyAudit.log 에 mismatch dump.
    /// 1회성 진단 — 끝나면 이 파일 삭제 가능.
    /// </summary>
    public static class AddressableKeyAudit
    {
        private const string ReportPath = "C:/tmp/AddressableKeyAudit.log";

        [MenuItem("BalloonFlow/Addressables/Audit Keys (Const vs Groups)")]
        public static void Audit()
        {
            var settings = AddressableAssetSettingsDefaultObject.Settings;
            if (settings == null)
            {
                Debug.LogError("[AddressableKeyAudit] AddressableAssetSettings 못 찾음.");
                return;
            }

            var sb = new StringBuilder(16 * 1024);
            sb.AppendLine($"[AddressableKeyAudit] {System.DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            sb.AppendLine();

            // ─── 1. Group 의 모든 entry address 수집 ───
            var groupAddresses = new HashSet<string>();
            sb.AppendLine("=== Groups + Entries ===");
            foreach (var g in settings.groups)
            {
                if (g == null) continue;
                sb.AppendLine($"--- {g.Name} (entries={g.entries.Count}) ---");
                foreach (var e in g.entries)
                {
                    string labels = e.labels != null && e.labels.Count > 0
                        ? string.Join(",", e.labels)
                        : "";
                    sb.AppendLine($"  '{e.address}'  [{labels}]");
                    groupAddresses.Add(e.address);
                }
            }
            sb.AppendLine();

            // ─── 2. Const class 의 ADDR_* 상수 수집 (ADDR_LABEL_ 제외) ───
            var constFields = typeof(Const).GetFields(BindingFlags.Public | BindingFlags.Static)
                .Where(f => f.IsLiteral && f.FieldType == typeof(string)
                            && f.Name.StartsWith("ADDR_") && !f.Name.StartsWith("ADDR_LABEL_"))
                .ToList();

            sb.AppendLine($"=== Const.ADDR_* (label 제외) — total {constFields.Count} ===");
            sb.AppendLine();

            // ─── 3. Const 에 있는데 Group 에 없음 (= Const 정리 필요) ───
            var missingInGroups = new List<string>();
            foreach (var f in constFields)
            {
                string value = (string)f.GetValue(null);
                if (!groupAddresses.Contains(value))
                    missingInGroups.Add($"  {f.Name,-55} = \"{value}\"");
            }

            sb.AppendLine($"=== Missing in Groups (Const 정리/삭제 후보) — {missingInGroups.Count} ===");
            foreach (var m in missingInGroups) sb.AppendLine(m);
            sb.AppendLine();

            // ─── 4. Group 에 있는데 Const 에 없음 (= Const 추가 필요) ───
            var constValues = new HashSet<string>(constFields.Select(f => (string)f.GetValue(null)));
            var missingInConst = groupAddresses.Where(a => !constValues.Contains(a)).OrderBy(a => a).ToList();

            sb.AppendLine($"=== Missing in Const (Const 추가 후보) — {missingInConst.Count} ===");
            foreach (var m in missingInConst) sb.AppendLine($"  \"{m}\"");

            string fullPath = Path.GetFullPath(ReportPath);
            Directory.CreateDirectory(Path.GetDirectoryName(fullPath));
            File.WriteAllText(fullPath, sb.ToString(), Encoding.UTF8);
            Debug.Log($"[AddressableKeyAudit] Wrote {fullPath}\nMissing in Groups: {missingInGroups.Count}\nMissing in Const: {missingInConst.Count}");
        }
    }
}
