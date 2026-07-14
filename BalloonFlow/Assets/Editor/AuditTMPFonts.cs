// ROLLBACK_TMP_FONT_AUDIT_20260714: TMP 텍스트 폰트/머티리얼 전수 감사 도구.
//   모든 프리팹(+열린 씬)의 TMP_Text 를 훑어 폰트/머티리얼/실제 색상(FaceColor·OutlineColor)/제어 컴포넌트/불일치를 로그.
//   목적: 언어별 폰트-머티리얼 불일치(한글↔영어 아틀라스), 색상 소실(블루→블랙), 폰트 기본머티리얼 오할당 색출.
//   결과는 <projectRoot>/TMP_Font_Audit.txt 파일 + 콘솔 요약. 롤백: 이 파일 삭제.
using System.Collections.Generic;
using System.IO;
using System.Text;
using BalloonFlow;
using BalloonFlow.UX;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace BalloonFlow.EditorTools
{
    public static class AuditTMPFonts
    {
        private static readonly string[] Families = { "ChironGoRoundTC-Black", "Poppins-Bold", "ChironGoRoundTC", "Poppins", "LiberationSans" };
        private static readonly int IdFace = Shader.PropertyToID("_FaceColor");
        private static readonly int IdOutline = Shader.PropertyToID("_OutlineColor");
        private static readonly int IdOutlineWidth = Shader.PropertyToID("_OutlineWidth");

        [MenuItem("Tools/BalloonFlow/Audit TMP Fonts (All Prefabs)")]
        public static void AuditPrefabs()
        {
            var sb = new StringBuilder();
            sb.AppendLine("=== TMP FONT AUDIT (Prefabs) ===");
            int total = 0, mismatch = 0, plain = 0, prefabs = 0;

            string[] guids = AssetDatabase.FindAssets("t:Prefab");
            foreach (string guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                // 서드파티/샘플 노이즈 제외(게임 프리팹만).
                if (path.Contains("/TextMesh Pro/") || path.Contains("/Plugins/") || path.StartsWith("Packages/")) continue;
                var go = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (go == null) continue;
                var texts = go.GetComponentsInChildren<TMP_Text>(true);
                if (texts == null || texts.Length == 0) continue;

                prefabs++;
                sb.AppendLine();
                sb.AppendLine($"### {path}");
                foreach (var t in texts)
                    AppendLine(sb, t, go.transform, ref total, ref mismatch, ref plain);
            }

            WriteAndReport(sb, prefabs, total, mismatch, plain, "prefabs");
        }

        [MenuItem("Tools/BalloonFlow/Audit TMP Fonts (Open Scene)")]
        public static void AuditOpenScene()
        {
            var sb = new StringBuilder();
            Scene scene = SceneManager.GetActiveScene();
            sb.AppendLine($"=== TMP FONT AUDIT (Scene: {scene.name}) ===");
            int total = 0, mismatch = 0, plain = 0, roots = 0;

            foreach (var root in scene.GetRootGameObjects())
            {
                var texts = root.GetComponentsInChildren<TMP_Text>(true);
                if (texts == null || texts.Length == 0) continue;
                roots++;
                sb.AppendLine();
                sb.AppendLine($"### [root] {root.name}");
                foreach (var t in texts)
                    AppendLine(sb, t, null, ref total, ref mismatch, ref plain);
            }

            WriteAndReport(sb, roots, total, mismatch, plain, "scene:" + scene.name);
        }

        private static void AppendLine(StringBuilder sb, TMP_Text t, Transform root, ref int total, ref int mismatch, ref int plain)
        {
            total++;
            string goPath = HierarchyPath(t.transform, root);
            var font = t.font;
            var mat = t.fontSharedMaterial;
            string fontName = font != null ? font.name : "NULL";
            string matName = mat != null ? mat.name : "NULL";
            string fontFam = FamilyOf(fontName);
            string matFam = FamilyOf(matName);

            // 실제 색상
            string face = "-", outline = "-", ow = "-";
            if (mat != null)
            {
                if (mat.HasProperty(IdFace)) face = "#" + ColorUtility.ToHtmlStringRGBA(mat.GetColor(IdFace));
                if (mat.HasProperty(IdOutline)) outline = "#" + ColorUtility.ToHtmlStringRGBA(mat.GetColor(IdOutline));
                if (mat.HasProperty(IdOutlineWidth)) ow = mat.GetFloat(IdOutlineWidth).ToString("0.###");
            }

            var flags = new List<string>();
            if (font == null) flags.Add("NO-FONT");
            if (mat == null) flags.Add("NO-MAT");
            if (fontFam != null && matFam != null && fontFam != matFam) { flags.Add("FONT≠MAT"); mismatch++; }
            // 아웃라인 토큰 없는 머티리얼(폰트 기본 등) — 색상 프리셋 미지정 의심
            bool hasOutlineToken = matName.IndexOf("Outline", System.StringComparison.OrdinalIgnoreCase) >= 0;
            if (mat != null && !hasOutlineToken) { flags.Add("PLAIN/DEFAULT?"); plain++; }
            // 아웃라인이 켜졌는데 색이 사실상 검정이면(블랙 오할당 의심)
            if (mat != null && mat.HasProperty(IdOutline))
            {
                Color oc = mat.GetColor(IdOutline);
                if ((oc.r + oc.g + oc.b) < 0.15f && oc.a > 0.5f) flags.Add("OUTLINE~BLACK");
            }

            // KO 예측(Play 없이): Poppins 폰트는 KO 에서 Chiron 으로 스왑됨 → 대응 KO 네임드 프리셋 존재 여부.
            //   없으면 런타임에 색보존 파생(근사)으로 떨어짐 → 정확한 KO 프리셋을 만들어야 하는 대상.
            if (fontFam == "Poppins-Bold" && matFam == "Poppins-Bold" && hasOutlineToken)
            {
                string koName = "ChironGoRoundTC-Black" + matName.Substring("Poppins-Bold".Length);
                if (Resources.Load<Material>("Fonts & Materials/" + koName) == null)
                    flags.Add("KO-NO-PRESET(" + koName + ")");
            }

            var ctrl = new List<string>();
            if (t.GetComponent<UIText>() != null) ctrl.Add("UIText");
            if (t.GetComponent<TMPSharedMaterialAdapter>() != null) ctrl.Add("Adapter");

            sb.AppendLine(
                $"  {goPath}\n" +
                $"      font={fontName} ({fontFam ?? "?"}) | mat={matName} ({matFam ?? "?"})\n" +
                $"      face={face} outline={outline} width={ow} | ctrl=[{string.Join(",", ctrl)}] | text=\"{Trim(t.text)}\"" +
                (flags.Count > 0 ? $"\n      >>> {string.Join(" ", flags)}" : ""));
        }

        private static string FamilyOf(string name)
        {
            if (string.IsNullOrEmpty(name) || name == "NULL") return null;
            for (int i = 0; i < Families.Length; i++)
                if (name.StartsWith(Families[i], System.StringComparison.Ordinal)) return Families[i];
            return null;
        }

        private static string HierarchyPath(Transform t, Transform stopAt)
        {
            var stack = new List<string>();
            var cur = t;
            while (cur != null)
            {
                stack.Insert(0, cur.name);
                if (stopAt != null && cur == stopAt) break;
                cur = cur.parent;
            }
            return string.Join("/", stack);
        }

        private static string Trim(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            s = s.Replace("\n", " ").Replace("\r", " ");
            return s.Length > 24 ? s.Substring(0, 24) + "…" : s;
        }

        private static void WriteAndReport(StringBuilder sb, int containers, int total, int mismatch, int plain, string scope)
        {
            sb.Insert(0, $"[summary] scope={scope} containers={containers} texts={total} FONT≠MAT={mismatch} PLAIN/DEFAULT?={plain}\n\n");
            string outPath = Path.Combine(Directory.GetParent(Application.dataPath).FullName, "TMP_Font_Audit.txt");
            File.WriteAllText(outPath, sb.ToString());
            Debug.Log($"[AuditTMPFonts] 완료 — texts={total}, FONT≠MAT={mismatch}, PLAIN/DEFAULT?={plain}\n파일: {outPath}");
        }
    }
}
