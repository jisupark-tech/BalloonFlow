#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace BalloonFlow.Editor
{
    /// <summary>
    /// BalloonFlow > Create Tutorial Prefab
    /// Resources/Popup/Tutorial.prefab을 자동 생성합니다.
    /// 이미 존재하면 덮어쓸지 확인합니다.
    /// </summary>
    public static class TutorialPrefabCreator
    {
        private const string PREFAB_PATH = "Assets/Resources/Popup/Tutorial.prefab";
        private static readonly Color DIM_COLOR = new Color(0f, 0f, 0f, 0.75f);

        [MenuItem("BalloonFlow/Don't Use/Create Tutorial Prefab", false, 70)]
        public static void CreatePrefab()
        {
            // 이미 존재하면 확인
            if (AssetDatabase.LoadAssetAtPath<GameObject>(PREFAB_PATH) != null)
            {
                if (!EditorUtility.DisplayDialog("Tutorial Prefab",
                    "Tutorial.prefab already exists.\nOverwrite?", "Overwrite", "Cancel"))
                    return;
            }

            // 폴더 확인
            if (!AssetDatabase.IsValidFolder("Assets/Resources"))
                AssetDatabase.CreateFolder("Assets", "Resources");
            if (!AssetDatabase.IsValidFolder("Assets/Resources/Popup"))
                AssetDatabase.CreateFolder("Assets/Resources", "Popup");

            // Root Canvas
            var root = new GameObject("Tutorial");
            var canvas = root.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 200;
            var scaler = root.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1080, 1920);
            scaler.matchWidthOrHeight = 0.5f;
            root.AddComponent<GraphicRaycaster>();

            // ── Dim Panels (4개) ──
            CreateDimPanel("DimTop", root.transform);
            CreateDimPanel("DimBottom", root.transform);
            CreateDimPanel("DimLeft", root.transform);
            CreateDimPanel("DimRight", root.transform);

            // ── Cutout Frame (구멍 테두리) ──
            var frame = CreateUI<Image>("CutoutFrame", root.transform);
            frame.color = new Color(1f, 1f, 1f, 0f); // 투명 배경
            var outline = frame.gameObject.AddComponent<Outline>();
            outline.effectColor = Color.white;
            outline.effectDistance = new Vector2(3, 3);
            var frameRT = frame.GetComponent<RectTransform>();
            frameRT.sizeDelta = new Vector2(200, 200);

            // ── Arrow Indicator (화살표) ──
            var arrow = CreateUI<Image>("ArrowIndicator", root.transform);
            arrow.color = Color.white;
            var arrowRT = arrow.GetComponent<RectTransform>();
            arrowRT.sizeDelta = new Vector2(50, 50);
            // 화살표 모양 자식
            var arrowHead = new GameObject("ArrowHead");
            arrowHead.transform.SetParent(arrow.transform, false);
            var ahImage = arrowHead.AddComponent<Image>();
            ahImage.color = Color.yellow;
            var ahRT = arrowHead.GetComponent<RectTransform>();
            ahRT.sizeDelta = new Vector2(30, 30);
            ahRT.localEulerAngles = new Vector3(0, 0, 45);

            // ── TapAnywhereOverlay (투명 전체 화면 버튼) ──
            var tapOverlay = CreateUI<Image>("TapAnywhereOverlay", root.transform);
            tapOverlay.color = new Color(0, 0, 0, 0); // 완전 투명
            tapOverlay.raycastTarget = true;
            var tapRT = tapOverlay.GetComponent<RectTransform>();
            SetFillRect(tapRT);
            tapOverlay.gameObject.AddComponent<Button>();

            // ── Instruction Panel (하단 설명 패널) ──
            var instrPanel = new GameObject("InstructionPanel");
            instrPanel.transform.SetParent(root.transform, false);
            var instrImage = instrPanel.AddComponent<Image>();
            instrImage.color = new Color(0.1f, 0.1f, 0.15f, 0.92f);
            var instrRT = instrPanel.GetComponent<RectTransform>();
            instrRT.anchorMin = new Vector2(0.05f, 0.02f);
            instrRT.anchorMax = new Vector2(0.95f, 0.15f);
            instrRT.offsetMin = Vector2.zero;
            instrRT.offsetMax = Vector2.zero;

            // Instruction Text (TMPro)
            var textGO = new GameObject("InstructionText");
            textGO.transform.SetParent(instrPanel.transform, false);
            var tmp = textGO.AddComponent<TextMeshProUGUI>();
            tmp.text = "Tutorial instruction text";
            tmp.fontSize = 28;
            tmp.color = Color.white;
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.enableWordWrapping = true;
            var textRT = textGO.GetComponent<RectTransform>();
            textRT.anchorMin = new Vector2(0.05f, 0.2f);
            textRT.anchorMax = new Vector2(0.75f, 0.9f);
            textRT.offsetMin = Vector2.zero;
            textRT.offsetMax = Vector2.zero;

            // Skip Button
            var skipGO = new GameObject("SkipButton");
            skipGO.transform.SetParent(instrPanel.transform, false);
            var skipImage = skipGO.AddComponent<Image>();
            skipImage.color = new Color(0.3f, 0.3f, 0.35f, 0.8f);
            var skipRT = skipGO.GetComponent<RectTransform>();
            skipRT.anchorMin = new Vector2(0.78f, 0.25f);
            skipRT.anchorMax = new Vector2(0.98f, 0.75f);
            skipRT.offsetMin = Vector2.zero;
            skipRT.offsetMax = Vector2.zero;
            skipGO.AddComponent<Button>();

            var skipTextGO = new GameObject("Text");
            skipTextGO.transform.SetParent(skipGO.transform, false);
            var skipTmp = skipTextGO.AddComponent<TextMeshProUGUI>();
            skipTmp.text = "SKIP";
            skipTmp.fontSize = 22;
            skipTmp.color = Color.white;
            skipTmp.alignment = TextAlignmentOptions.Center;
            var skipTextRT = skipTextGO.GetComponent<RectTransform>();
            SetFillRect(skipTextRT);

            // 프리팹으로 저장
            PrefabUtility.SaveAsPrefabAsset(root, PREFAB_PATH);
            Object.DestroyImmediate(root);

            AssetDatabase.Refresh();
            Debug.Log($"[TutorialPrefabCreator] Created {PREFAB_PATH}");
            EditorUtility.DisplayDialog("Tutorial Prefab", $"Created:\n{PREFAB_PATH}", "OK");
        }

        private static void CreateDimPanel(string name, Transform parent)
        {
            var img = CreateUI<Image>(name, parent);
            img.color = DIM_COLOR;
            img.raycastTarget = true;
            var rt = img.GetComponent<RectTransform>();
            // 기본 크기 — TutorialManager가 런타임에 위치/크기 조정
            SetFillRect(rt);
        }

        private static T CreateUI<T>(string name, Transform parent) where T : Component
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.AddComponent<RectTransform>();
            return go.AddComponent<T>();
        }

        private static void SetFillRect(RectTransform rt)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
        }
    }
}
#endif
