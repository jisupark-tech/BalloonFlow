#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

// ROLLBACK_LOCALIZATION_HARDCODE_SCAN_20260714: 로컬라이징 전수 진단 도구(검수용, 빌드 제외).
//   "한국어로 안 바뀌는 텍스트"의 두 부류를 스캔해 콘솔 + 리포트 파일로 출력.
//     (A) 코드 하드코딩 : Set*Text("영어"), .text = "영어" 등 LocalizationService 를 안 타는 리터럴.
//     (B) 프리팹 baked  : TMP_Text/Text 에 값이 구워져 있고 UIText 컴포넌트가 없어 언어 전환이 안 되는 것.
//   ※ 스프라이트(이미지)로 그린 글자(예: 아트 'NO ADS' 배지)는 텍스트가 아니라 못 잡음 — 로그 말미에 명시.
//   ※ 씬에 직접 박힌 UI 는 씬 강제 오픈이 필요해 스킵(대부분 UI 는 프리팹). 로그에 한계로 표기.
public static class HardcodedTextScanner
{
    private const string ROOT = "Tools/BalloonFlow/Localization/";
    private const string CODE_DIR = "Assets/1.Scripts";           // 런타임 코드만(Editor 스크립트는 빌드 제외라 제외)
    private const string REPORT_REL = "HardcodedTextReport.txt";    // 프로젝트 루트(Assets 상위)에 생성

    // 텍스트 싱크: Set*Text/Label/Title/Description/Message(...) 또는 .text = "..."
    // s? = 복수형 세터(SetVertButtonTexts 등)도 포함. 첫 리터럴 인자만 캡처(위치 식별용 — 라인 전체가 로그에 남음).
    private static readonly Regex RxSetMethod = new Regex(
        @"\bSet\w*(?:Text|Label|Title|Desc|Description|Caption|Message)s?\s*\(\s*@?""((?:[^""\\]|\\.)*)""",
        RegexOptions.Compiled);
    private static readonly Regex RxTextAssign = new Regex(
        @"\.text\s*=\s*@?""((?:[^""\\]|\\.)*)""",
        RegexOptions.Compiled);

    [MenuItem(ROOT + "Scan Hardcoded / Non-localized Text", false, 40)]
    public static void Scan()
    {
        var sb = new StringBuilder(1 << 16);
        sb.AppendLine("=== Hardcoded / Non-localized Text Scan (BalloonFlow) ===");
        sb.AppendLine("Generated: " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
        sb.AppendLine("Legend: [A] 코드 영어 리터럴(로컬라이징 미적용) / [B] 프리팹 baked 텍스트(UIText 없음)");
        sb.AppendLine();

        int aCount = ScanCode(sb);
        int bCount = ScanPrefabs(sb);

        sb.AppendLine();
        sb.AppendLine("─────────────────────────────────────────────");
        sb.AppendLine($"SUMMARY : [A] 코드 하드코딩 {aCount}건  /  [B] 프리팹 baked {bCount}건");
        sb.AppendLine("한계    : (1) Image/Sprite 로 그린 글자(아트 배지)는 텍스트가 아니라 미검출.");
        sb.AppendLine("          (2) 씬에 직접 박힌 UI 는 스킵(대부분 UI 는 프리팹).");
        sb.AppendLine("          (3) [A] 는 동적으로 값이 바뀌는 리터럴도 포함될 수 있어 육안 트리아지 필요.");

        string path = Path.GetFullPath(Path.Combine(Application.dataPath, "..", REPORT_REL));
        try { File.WriteAllText(path, sb.ToString(), new UTF8Encoding(true)); }
        catch (Exception e) { Debug.LogWarning($"[HardcodeScan] 리포트 파일 쓰기 실패: {e.Message}"); }

        Debug.Log($"[HardcodeScan] 완료 — [A]{aCount} [B]{bCount}건. 리포트: {path}\n" +
                  "─── 아래 콘솔에도 전체 출력 ───\n" + sb.ToString());
    }

    // ── [A] 코드 스캔 ────────────────────────────────────────────
    private static int ScanCode(StringBuilder sb)
    {
        sb.AppendLine("### [A] 코드 하드코딩 영어 리터럴 (LocalizationService 미사용) ###");
        int count = 0;
        if (!Directory.Exists(CODE_DIR)) { sb.AppendLine("  (코드 디렉터리 없음: " + CODE_DIR + ")"); return 0; }

        string[] files = Directory.GetFiles(CODE_DIR, "*.cs", SearchOption.AllDirectories);
        Array.Sort(files, StringComparer.OrdinalIgnoreCase);
        foreach (string file in files)
        {
            string[] lines;
            try { lines = File.ReadAllLines(file); } catch { continue; }
            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i];
                string trimmed = line.TrimStart();
                if (trimmed.StartsWith("//") || trimmed.StartsWith("*")) continue;   // 주석 스킵
                if (line.Contains("LocalizationService")) continue;                   // 이미 로컬라이즈
                if (line.Contains("Debug.Log") || line.Contains("LOG_TAG")) continue; // 로그 문자열 스킵

                foreach (Match m in RxSetMethod.Matches(line))
                    if (IsUserFacing(m.Groups[1].Value)) { Emit(sb, file, i + 1, "Set…Text()", m.Groups[1].Value); count++; }
                foreach (Match m in RxTextAssign.Matches(line))
                    if (IsUserFacing(m.Groups[1].Value)) { Emit(sb, file, i + 1, ".text =", m.Groups[1].Value); count++; }
            }
        }
        if (count == 0) sb.AppendLine("  (없음)");
        return count;
    }

    private static void Emit(StringBuilder sb, string file, int line, string sink, string text)
    {
        string rel = file.Replace('\\', '/');
        int idx = rel.IndexOf("Assets/", StringComparison.OrdinalIgnoreCase);
        if (idx >= 0) rel = rel.Substring(idx);
        sb.AppendLine($"  {rel}:{line}  [{sink}]  \"{text}\"");
    }

    // ── [B] 프리팹 스캔 ─────────────────────────────────────────
    private static int ScanPrefabs(StringBuilder sb)
    {
        sb.AppendLine();
        sb.AppendLine("### [B] 프리팹 baked 텍스트 — UIText 없어 언어 전환 안 됨 ###");
        int count = 0;
        string[] guids = AssetDatabase.FindAssets("t:Prefab");
        var seen = new List<string>();
        foreach (string guid in guids)
        {
            string p = AssetDatabase.GUIDToAssetPath(guid);
            if (string.IsNullOrEmpty(p) || !p.StartsWith("Assets/")) continue;
            var go = AssetDatabase.LoadAssetAtPath<GameObject>(p);
            if (go == null) continue;

            foreach (var tmp in go.GetComponentsInChildren<TMP_Text>(true))
                if (tmp != null && FlagBaked(tmp.gameObject, tmp.text))
                { sb.AppendLine($"  {p}  ▸ {HierPath(tmp.transform)}  (TMP)  \"{One(tmp.text)}\""); count++; }

            foreach (var t in go.GetComponentsInChildren<Text>(true))
                if (t != null && FlagBaked(t.gameObject, t.text))
                { sb.AppendLine($"  {p}  ▸ {HierPath(t.transform)}  (UI.Text)  \"{One(t.text)}\""); count++; }
        }
        if (count == 0) sb.AppendLine("  (없음)");
        return count;
    }

    // baked 로 볼지: 사용자향 텍스트 + UIText(로컬라이저) 미부착.
    private static bool FlagBaked(GameObject go, string text)
    {
        if (!IsUserFacing(text)) return false;
        // 같은 GO 에 UIText(BalloonFlow.UIText) 가 있으면 로컬라이징 대상 → 제외.
        var comps = go.GetComponents<MonoBehaviour>();
        foreach (var c in comps)
            if (c != null && c.GetType().Name == "UIText") return false;
        return true;
    }

    // ── 공통 판정/유틸 ──────────────────────────────────────────
    // 사용자향 문자열: 영문 2글자 이상 포함 + 키/경로/태그/포맷토큰 아님.
    private static bool IsUserFacing(string s)
    {
        if (string.IsNullOrWhiteSpace(s)) return false;
        s = s.Trim();
        if (s.Length < 2) return false;
        if (s[0] == '<' || s[0] == '#' || s[0] == '{') return false;         // 컬러태그/hex/포맷
        if (s.Contains("/")) return false;                                    // 리소스 경로
        if (Regex.IsMatch(s, @"^[a-z0-9_]+(\.[a-z0-9_]+)+$")) return false;    // 소문자 dotted key (ui.common.stay)
        if (!Regex.IsMatch(s, @"[A-Za-z]{2,}")) return false;                  // 최소 영단어
        if (Regex.IsMatch(s, @"^(true|false|null|On|Off|OK|https?:)", RegexOptions.IgnoreCase) && s.Length <= 5) return false;
        return true;
    }

    private static string One(string s) => (s ?? "").Replace("\n", "\\n").Replace("\r", "");

    private static string HierPath(Transform t)
    {
        var stack = new List<string>();
        while (t != null) { stack.Add(t.name); t = t.parent; }
        stack.Reverse();
        return string.Join("/", stack);
    }
}
#endif
