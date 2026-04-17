#if UNITY_EDITOR
using UnityEngine;
using UnityEngine.UI;
using UnityEditor;

namespace BalloonFlow.Editor
{
    /// <summary>
    /// UIButton 프리팹 생성기. 아트팀이 드래그해서 사용할 수 있는 표준 버튼 프리팹을 만든다.
    /// 메뉴: BalloonFlow > Build UIButton Prefab
    /// 출력: Assets/Resources/UI/UIAssets/UIButton.prefab
    /// </summary>
    public static class UIButtonPrefabBuilder
    {
        private const string OUTPUT_PATH = "Assets/Resources/UI/UIAssets/UIButton.prefab";
        private const string OUTPUT_FOLDER = "Assets/Resources/UI/UIAssets";

        [MenuItem("BalloonFlow/Build UIButton Prefab")]
        private static void Build()
        {
            if (!AssetDatabase.IsValidFolder(OUTPUT_FOLDER))
            {
                Debug.LogError($"[UIButtonPrefabBuilder] 폴더 없음: {OUTPUT_FOLDER}");
                return;
            }

            var root = new GameObject("UIButton");
            int uiLayer = LayerMask.NameToLayer("UI");
            if (uiLayer >= 0) root.layer = uiLayer;

            var rt = root.AddComponent<RectTransform>();
            rt.sizeDelta = new Vector2(280, 88);

            var img = root.AddComponent<Image>();
            img.color = new Color(0.2f, 0.6f, 0.95f, 1f);
            img.raycastTarget = true;

            // UIButton은 Button을 상속 + RequireComponent(ButtonScaleEffect)
            // AddComponent<UIButton>() 하면 ButtonScaleEffect가 자동 부착됨.
            root.AddComponent<UIButton>();

            // Label (raycast 차단 방지)
            var labelGO = new GameObject("Label");
            if (uiLayer >= 0) labelGO.layer = uiLayer;
            labelGO.transform.SetParent(root.transform, false);
            var lrt = labelGO.AddComponent<RectTransform>();
            lrt.anchorMin = Vector2.zero;
            lrt.anchorMax = Vector2.one;
            lrt.offsetMin = Vector2.zero;
            lrt.offsetMax = Vector2.zero;

            var font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            if (font == null) font = Resources.GetBuiltinResource<Font>("Arial.ttf");

            var label = labelGO.AddComponent<Text>();
            label.text = "Button";
            label.font = font;
            label.fontSize = 32;
            label.fontStyle = FontStyle.Bold;
            label.alignment = TextAnchor.MiddleCenter;
            label.color = Color.white;
            label.raycastTarget = false;

            PrefabUtility.SaveAsPrefabAsset(root, OUTPUT_PATH);
            Object.DestroyImmediate(root);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            var asset = AssetDatabase.LoadAssetAtPath<GameObject>(OUTPUT_PATH);
            Selection.activeObject = asset;
            EditorGUIUtility.PingObject(asset);

            Debug.Log($"[UIButtonPrefabBuilder] 생성 완료: {OUTPUT_PATH}");
        }
    }
}
#endif
