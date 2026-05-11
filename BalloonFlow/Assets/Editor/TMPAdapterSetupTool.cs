#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using TMPro;
using UnityEditor;
using UnityEngine;
using BalloonFlow.UX;

namespace BalloonFlow.Editor
{
    public static class TMPAdapterSetupTool
    {
        private const string SOURCE_BASE_MAT_PATH = "Assets/TextMesh Pro/Resources/Fonts & Materials/Poppins-Bold-BlackOutline.mat";
        private const string SHARED_BASE_MAT_PATH = "Assets/TextMesh Pro/Resources/Fonts & Materials/Poppins-Bold-OutlineShared.mat";
        private const string PREFAB_SEARCH_ROOT = "Assets/Resources";

        // 통합 대상 mat 이름. Holder 는 offsetY/width 차이로 제외. Base SDF Material 도 keyword 차이로 제외.
        private static readonly HashSet<string> TargetMaterialNames = new HashSet<string>
        {
            "Poppins-Bold-BlackOutline",
            "Poppins-Bold-BlueOutline",
            "Poppins-Bold-BrownOutline",
            "Poppins-Bold-GreenOutline",
            "Poppins-Bold-PurpleOutline",
            "Poppins-Bold-RedOutline",
        };

        [MenuItem("BalloonFlow/Performance/TMP Adapter/1. Create SharedBase Mat", false, 510)]
        private static void CreateSharedBaseMat()
        {
            if (File.Exists(SHARED_BASE_MAT_PATH))
            {
                if (!EditorUtility.DisplayDialog("SharedBase Mat",
                    $"이미 존재합니다:\n{SHARED_BASE_MAT_PATH}\n\n덮어쓰겠습니까?",
                    "덮어쓰기", "취소"))
                {
                    return;
                }
                AssetDatabase.DeleteAsset(SHARED_BASE_MAT_PATH);
            }

            bool ok = AssetDatabase.CopyAsset(SOURCE_BASE_MAT_PATH, SHARED_BASE_MAT_PATH);
            if (!ok)
            {
                Debug.LogError($"[TMPAdapterSetupTool] Failed to copy {SOURCE_BASE_MAT_PATH} -> {SHARED_BASE_MAT_PATH}");
                return;
            }

            AssetDatabase.Refresh();
            var mat = AssetDatabase.LoadAssetAtPath<Material>(SHARED_BASE_MAT_PATH);
            Debug.Log($"[TMPAdapterSetupTool] SharedBase mat created: {SHARED_BASE_MAT_PATH}");
            EditorGUIUtility.PingObject(mat);
            Selection.activeObject = mat;
        }

        [MenuItem("BalloonFlow/Performance/TMP Adapter/2. Dry-Run Attach", false, 511)]
        private static void DryRun()
        {
            RunAttach(applyChanges: false);
        }

        [MenuItem("BalloonFlow/Performance/TMP Adapter/3. Apply Attach", false, 512)]
        private static void Apply()
        {
            if (!File.Exists(SHARED_BASE_MAT_PATH))
            {
                EditorUtility.DisplayDialog("Apply",
                    $"SharedBase mat 가 없습니다:\n{SHARED_BASE_MAT_PATH}\n\n먼저 메뉴 1번을 실행하세요.",
                    "확인");
                return;
            }

            if (!EditorUtility.DisplayDialog("Apply Attach",
                "모든 대상 prefab 에 TMPSharedMaterialAdapter 를 attach 합니다.\n\n" +
                "변경 전에 git commit 또는 backup 이 강력히 권장됩니다.\n\n계속할까요?",
                "Apply", "취소"))
            {
                return;
            }

            RunAttach(applyChanges: true);
        }

        [MenuItem("BalloonFlow/Performance/TMP Adapter/4. Remove All Adapters (Rollback)", false, 513)]
        private static void RemoveAllAdapters()
        {
            if (!EditorUtility.DisplayDialog("Remove Adapters",
                "모든 prefab 에서 TMPSharedMaterialAdapter 를 제거합니다.\n\n" +
                "단, 원본 fontSharedMaterial 은 복구되지 않습니다 (Apply 시 변경 안 했으므로 prefab override 만 제거).\n\n계속할까요?",
                "Remove", "취소"))
            {
                return;
            }
            RunRemoveAdapters();
        }

        private static void RunAttach(bool applyChanges)
        {
            var sharedBase = AssetDatabase.LoadAssetAtPath<Material>(SHARED_BASE_MAT_PATH);
            if (sharedBase == null && applyChanges)
            {
                Debug.LogError($"[TMPAdapterSetupTool] SharedBase mat not found: {SHARED_BASE_MAT_PATH}");
                return;
            }

            string[] prefabGuids = AssetDatabase.FindAssets("t:Prefab", new[] { PREFAB_SEARCH_ROOT });
            var report = new StringBuilder();
            report.AppendLine($"=== TMP Adapter {(applyChanges ? "Apply" : "Dry-Run")} ===");
            report.AppendLine($"Target mats: {string.Join(", ", TargetMaterialNames.OrderBy(x => x))}");
            report.AppendLine($"SharedBase: {SHARED_BASE_MAT_PATH}");
            report.AppendLine();

            int prefabsModified = 0;
            int adaptersAdded = 0;
            int adaptersExisting = 0;

            try
            {
                AssetDatabase.StartAssetEditing();

                for (int i = 0; i < prefabGuids.Length; i++)
                {
                    string ppath = AssetDatabase.GUIDToAssetPath(prefabGuids[i]);
                    EditorUtility.DisplayProgressBar(applyChanges ? "Apply TMP Adapter" : "Dry-Run TMP Adapter",
                        $"{i + 1}/{prefabGuids.Length}: {Path.GetFileName(ppath)}",
                        (float)i / Mathf.Max(1, prefabGuids.Length));

                    var contents = applyChanges
                        ? PrefabUtility.LoadPrefabContents(ppath)
                        : AssetDatabase.LoadAssetAtPath<GameObject>(ppath);
                    if (contents == null) continue;

                    var tmps = contents.GetComponentsInChildren<TMP_Text>(true);
                    bool prefabHasChange = false;
                    int prefabAdded = 0;
                    int prefabExisting = 0;
                    var prefabLines = new StringBuilder();

                    foreach (var tmp in tmps)
                    {
                        if (tmp == null) continue;
                        var mat = tmp.fontSharedMaterial;
                        if (mat == null) continue;
                        if (!TargetMaterialNames.Contains(mat.name)) continue;

                        var existing = tmp.GetComponent<TMPSharedMaterialAdapter>();
                        if (existing != null)
                        {
                            prefabExisting++;
                            continue;
                        }

                        prefabLines.AppendLine($"    + Adapter on '{GetPath(tmp.transform)}' (mat: {mat.name})");
                        prefabAdded++;

                        if (applyChanges)
                        {
                            var adapter = tmp.gameObject.AddComponent<TMPSharedMaterialAdapter>();
                            var so = new SerializedObject(adapter);
                            so.FindProperty("_sharedBaseMaterial").objectReferenceValue = sharedBase;
                            so.ApplyModifiedPropertiesWithoutUndo();
                            prefabHasChange = true;
                        }
                    }

                    if (prefabAdded > 0 || prefabExisting > 0)
                    {
                        report.AppendLine($"[{ppath}]  +{prefabAdded} new, ={prefabExisting} existing");
                        if (prefabLines.Length > 0) report.Append(prefabLines.ToString());
                        adaptersAdded += prefabAdded;
                        adaptersExisting += prefabExisting;
                    }

                    if (applyChanges)
                    {
                        if (prefabHasChange)
                        {
                            PrefabUtility.SaveAsPrefabAsset(contents, ppath);
                            prefabsModified++;
                        }
                        PrefabUtility.UnloadPrefabContents(contents);
                    }
                }
            }
            finally
            {
                EditorUtility.ClearProgressBar();
                AssetDatabase.StopAssetEditing();
                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();
            }

            report.AppendLine();
            report.AppendLine("=== Summary ===");
            report.AppendLine($"Prefabs scanned:    {prefabGuids.Length}");
            report.AppendLine($"Adapters to add:    {adaptersAdded}");
            report.AppendLine($"Adapters existing:  {adaptersExisting}");
            if (applyChanges) report.AppendLine($"Prefabs modified:   {prefabsModified}");

            string finalReport = report.ToString();
            Debug.Log(finalReport);

            string logPath = Path.Combine(Application.dataPath, "../Logs/",
                applyChanges ? "tmp_adapter_apply.log" : "tmp_adapter_dryrun.log");
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(logPath));
                File.WriteAllText(logPath, finalReport);
                Debug.Log($"[TMPAdapterSetupTool] Report saved: {Path.GetFullPath(logPath)}");
            }
            catch (System.Exception e)
            {
                Debug.LogWarning($"Log save failed: {e.Message}");
            }
        }

        private static void RunRemoveAdapters()
        {
            string[] prefabGuids = AssetDatabase.FindAssets("t:Prefab", new[] { PREFAB_SEARCH_ROOT });
            int removed = 0;
            int prefabsModified = 0;

            try
            {
                AssetDatabase.StartAssetEditing();

                for (int i = 0; i < prefabGuids.Length; i++)
                {
                    string ppath = AssetDatabase.GUIDToAssetPath(prefabGuids[i]);
                    EditorUtility.DisplayProgressBar("Remove TMP Adapters",
                        $"{i + 1}/{prefabGuids.Length}: {Path.GetFileName(ppath)}",
                        (float)i / Mathf.Max(1, prefabGuids.Length));

                    var contents = PrefabUtility.LoadPrefabContents(ppath);
                    if (contents == null) continue;

                    var adapters = contents.GetComponentsInChildren<TMPSharedMaterialAdapter>(true);
                    bool changed = false;
                    foreach (var ad in adapters)
                    {
                        if (ad == null) continue;
                        Object.DestroyImmediate(ad, true);
                        removed++;
                        changed = true;
                    }

                    if (changed)
                    {
                        PrefabUtility.SaveAsPrefabAsset(contents, ppath);
                        prefabsModified++;
                    }
                    PrefabUtility.UnloadPrefabContents(contents);
                }
            }
            finally
            {
                EditorUtility.ClearProgressBar();
                AssetDatabase.StopAssetEditing();
                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();
            }

            Debug.Log($"[TMPAdapterSetupTool] Removed {removed} adapters from {prefabsModified} prefabs.");
        }

        private static string GetPath(Transform t)
        {
            if (t == null) return "<null>";
            var sb = new StringBuilder(t.name);
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
