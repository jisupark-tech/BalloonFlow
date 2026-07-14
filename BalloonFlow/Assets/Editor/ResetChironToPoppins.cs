// ROLLBACK_FONT_SWAP_REMOVED_20260714: 프리팹에 고착된 ChironGoRoundTC-Black 폰트를 Poppins-Bold 로 되돌림.
//   배경: 언어별 폰트 스왑 코드(UIText.OnValidate/Adapter 등)가 에디터 편집 시점에 프리팹 TMP 폰트를 Chiron 으로
//   바꿔 저장해버린 케이스가 있음. Fallback(Poppins→Chiron) 채택 후엔 모든 TMP 가 Poppins-Bold SDF 여야 하므로
//   Chiron 폰트를 쓰는 TMP 를 Poppins 로 되돌리고, 머티리얼도 이름규약으로 Poppins 대응 프리셋에 매핑한다.
//   읽기 전용 아님 — 프리팹을 수정/저장한다. 자산은 git 추적이라 결과 이상 시 revert 가능. 롤백: 이 파일 삭제.
using System.Collections.Generic;
using System.Text;
using TMPro;
using UnityEditor;
using UnityEngine;

namespace BalloonFlow.EditorTools
{
    public static class ResetChironToPoppins
    {
        private const string ResFolder = "Fonts & Materials/";
        private const string ChironFamily = "ChironGoRoundTC-Black";
        private const string PoppinsFamily = "Poppins-Bold";
        private const string PoppinsFontPath = "Fonts & Materials/Poppins-Bold SDF";

        [MenuItem("Tools/BalloonFlow/Reset Chiron Fonts to Poppins (prefabs)")]
        public static void Run()
        {
            var poppins = Resources.Load<TMP_FontAsset>(PoppinsFontPath);
            if (poppins == null)
            {
                Debug.LogError($"[ResetChironToPoppins] Poppins 폰트 못 찾음: Resources/{PoppinsFontPath}");
                return;
            }

            var sb = new StringBuilder();
            int changedTexts = 0, changedPrefabs = 0;
            var matCache = new Dictionary<string, Material>();

            string[] guids = AssetDatabase.FindAssets("t:Prefab");
            foreach (string guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (path.Contains("/TextMesh Pro/") || path.Contains("/Plugins/") || path.StartsWith("Packages/")) continue;
                var go = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (go == null) continue;

                var texts = go.GetComponentsInChildren<TMP_Text>(true);
                if (texts == null || texts.Length == 0) continue;

                bool prefabChanged = false;
                foreach (var t in texts)
                {
                    if (t.font == null || t.font.name.IndexOf(ChironFamily, System.StringComparison.Ordinal) < 0) continue;

                    string oldFont = t.font.name;
                    Material oldMat = t.fontSharedMaterial;
                    string oldMatName = oldMat != null ? oldMat.name : "(null)";

                    // 폰트를 Poppins 로
                    t.font = poppins;

                    // 머티리얼: Chiron 접두 → Poppins 접두 매핑(프리셋). 실패/기본머티리얼이면 Poppins 기본.
                    Material newMat = poppins.material;
                    if (oldMat != null)
                    {
                        string mapped = oldMat.name.Replace(" (Instance)", "").Replace(ChironFamily, PoppinsFamily);
                        if (!matCache.TryGetValue(mapped, out var m))
                        {
                            m = Resources.Load<Material>(ResFolder + mapped);
                            matCache[mapped] = m;
                        }
                        if (m != null) newMat = m;
                    }
                    t.fontSharedMaterial = newMat;

                    EditorUtility.SetDirty(t);
                    changedTexts++;
                    prefabChanged = true;
                    sb.AppendLine($"  {path} : {HierarchyPath(t.transform, go.transform)}");
                    sb.AppendLine($"      font {oldFont} -> {poppins.name} | mat {oldMatName} -> {newMat?.name}");
                }
                if (prefabChanged) changedPrefabs++;
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log($"[ResetChironToPoppins] 완료 — {changedTexts}개 텍스트 / {changedPrefabs}개 프리팹 되돌림.\n" + sb.ToString());
        }

        private static string HierarchyPath(Transform t, Transform stopAt)
        {
            var stack = new List<string>();
            var cur = t;
            while (cur != null)
            {
                stack.Insert(0, cur.name);
                if (cur == stopAt) break;
                cur = cur.parent;
            }
            return string.Join("/", stack);
        }
    }
}
