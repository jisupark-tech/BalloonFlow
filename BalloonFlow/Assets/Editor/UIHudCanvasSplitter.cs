#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using BalloonFlow;

namespace BalloonFlow.Editor
{
    public static class UIHudCanvasSplitter
    {
        private const string PREFAB_PATH = "Assets/Resources/UI/UIHud.prefab";

        [MenuItem("BalloonFlow/Performance/UIHud/Split LvPanel Canvas", false, 520)]
        private static void SplitLvPanel()
        {
            ModifyLvPanel(addCanvas: true);
        }

        [MenuItem("BalloonFlow/Performance/UIHud/Remove LvPanel Canvas (Rollback)", false, 521)]
        private static void RemoveLvPanelCanvas()
        {
            ModifyLvPanel(addCanvas: false);
        }

        private static void ModifyLvPanel(bool addCanvas)
        {
            var contents = PrefabUtility.LoadPrefabContents(PREFAB_PATH);
            if (contents == null)
            {
                Debug.LogError($"[UIHudCanvasSplitter] Failed to load: {PREFAB_PATH}");
                return;
            }

            try
            {
                var hud = contents.GetComponentInChildren<UIHud>(true);
                if (hud == null)
                {
                    Debug.LogError("[UIHudCanvasSplitter] UIHud component not found in prefab.");
                    return;
                }

                var so = new SerializedObject(hud);
                var fillProp = so.FindProperty("_fillGaugeImage");
                if (fillProp == null || fillProp.objectReferenceValue == null)
                {
                    Debug.LogError("[UIHudCanvasSplitter] _fillGaugeImage SerializeField not assigned.");
                    return;
                }

                var fillImage = fillProp.objectReferenceValue as Image;
                if (fillImage == null || fillImage.transform.parent == null)
                {
                    Debug.LogError("[UIHudCanvasSplitter] _fillGaugeImage has no parent.");
                    return;
                }

                var lvPanel = fillImage.transform.parent.gameObject;
                Debug.Log($"[UIHudCanvasSplitter] LvPanel target: {GetPath(lvPanel.transform)}");

                if (addCanvas)
                {
                    var existing = lvPanel.GetComponent<Canvas>();
                    if (existing != null)
                    {
                        Debug.LogWarning($"[UIHudCanvasSplitter] Canvas already exists on {lvPanel.name}. Skipping.");
                        return;
                    }

                    // 부모 Canvas 의 sortingLayer / order 추출 (시각 동등 유지)
                    var parentCanvas = lvPanel.GetComponentInParent<Canvas>();
                    int parentSortingOrder = parentCanvas != null ? parentCanvas.sortingOrder : 0;
                    int parentSortingLayerID = parentCanvas != null ? parentCanvas.sortingLayerID : 0;

                    var canvas = lvPanel.AddComponent<Canvas>();
                    canvas.overrideSorting = true;
                    canvas.sortingLayerID = parentSortingLayerID;
                    canvas.sortingOrder = parentSortingOrder;

                    // GraphicRaycaster 는 추가 안 함 (게이지/% 텍스트는 클릭 입력 받지 않음)
                    Debug.Log($"[UIHudCanvasSplitter] Added Canvas to {lvPanel.name} (sortingOrder={parentSortingOrder}, sortingLayer={parentSortingLayerID}).");
                }
                else
                {
                    var existing = lvPanel.GetComponent<Canvas>();
                    if (existing == null)
                    {
                        Debug.LogWarning($"[UIHudCanvasSplitter] No Canvas on {lvPanel.name}. Nothing to remove.");
                        return;
                    }
                    Object.DestroyImmediate(existing, true);

                    var raycaster = lvPanel.GetComponent<GraphicRaycaster>();
                    if (raycaster != null) Object.DestroyImmediate(raycaster, true);

                    Debug.Log($"[UIHudCanvasSplitter] Removed Canvas from {lvPanel.name}.");
                }

                PrefabUtility.SaveAsPrefabAsset(contents, PREFAB_PATH);
                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(contents);
            }
        }

        private static string GetPath(Transform t)
        {
            if (t == null) return "<null>";
            var sb = new System.Text.StringBuilder(t.name);
            var p = t.parent;
            while (p != null)
            {
                sb.Insert(0, p.name + "/");
                p = p.parent;
            }
            return sb.ToString();
        }
    }
}
#endif
