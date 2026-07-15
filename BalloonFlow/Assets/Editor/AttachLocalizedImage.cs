// ROLLBACK_LOCALIZED_IMAGE_20260715: LocalizedImage 컴포넌트를 3개 팝업 프리팹의 지정 자식에 부착하는 '수동' 도구.
//   ※ InitializeOnLoad 아님 — 메뉴 클릭 시에만 동작(아트 프리팹 자동 덮어쓰기 방지 정책 준수).
//   대상: PopupFail01/ImageTxt, PopupWinningStreakinfo/ImageTxt, NewFeature/Title.
//   각 자식(Image 보유)에 LocalizedImage 가 없으면 추가만 하고 저장. 이미 있으면 스킵. 값 할당 불필요(이름 규약 '~KR').
//   롤백: 이 파일 삭제(부착된 컴포넌트는 프리팹에서 수동 Remove).
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace BalloonFlow.EditorTools
{
    public static class AttachLocalizedImage
    {
        // (프리팹 경로, 자식 오브젝트 이름)
        private static readonly (string prefab, string child)[] Targets = new[]
        {
            ("Assets/Resources/Popup/PopupFail01.prefab",           "ImageTxt"),
            ("Assets/Resources/Popup/PopupWinningStreakinfo.prefab", "ImageTxt"),
            ("Assets/Resources/Popup/NewFeature.prefab",            "Title"),
        };

        [MenuItem("Tools/BalloonFlow/Localize/Attach LocalizedImage → 3 Popups", false, 30)]
        public static void Attach()
        {
            int added = 0, skipped = 0, failed = 0;
            var report = new System.Text.StringBuilder();

            foreach (var (prefabPath, childName) in Targets)
            {
                GameObject root = PrefabUtility.LoadPrefabContents(prefabPath);
                if (root == null) { report.AppendLine($"✗ 프리팹 로드 실패: {prefabPath}"); failed++; continue; }
                try
                {
                    GameObject target = FindChildWithImage(root.transform, childName);
                    if (target == null)
                    {
                        report.AppendLine($"✗ '{childName}'(Image 보유) 자식 없음: {prefabPath}");
                        failed++;
                        continue;
                    }
                    if (target.GetComponent<LocalizedImage>() != null)
                    {
                        report.AppendLine($"– 이미 부착됨(스킵): {prefabPath} / {childName}");
                        skipped++;
                        continue;
                    }
                    target.AddComponent<LocalizedImage>();
                    PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
                    report.AppendLine($"✓ 부착: {prefabPath} / {childName}");
                    added++;
                }
                finally
                {
                    PrefabUtility.UnloadPrefabContents(root);
                }
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            string msg = $"LocalizedImage 부착 완료\n\n추가 {added} · 스킵 {skipped} · 실패 {failed}\n\n{report}";
            Debug.Log("[AttachLocalizedImage] " + msg);
            EditorUtility.DisplayDialog("Attach LocalizedImage", msg, "OK");
        }

        // childName 과 이름이 정확히 일치하고 Image 를 가진 자손을 찾음(중복 시 첫 번째 + 경고).
        private static GameObject FindChildWithImage(Transform root, string childName)
        {
            GameObject found = null;
            int matches = 0;
            var all = root.GetComponentsInChildren<Transform>(true);
            foreach (var t in all)
            {
                if (t.name != childName) continue;
                if (t.GetComponent<Image>() == null) continue;
                matches++;
                if (found == null) found = t.gameObject;
            }
            if (matches > 1)
                Debug.LogWarning($"[AttachLocalizedImage] '{childName}' Image 자식이 {matches}개 — 첫 번째에 부착. 확인 요망.");
            return found;
        }
    }
}
