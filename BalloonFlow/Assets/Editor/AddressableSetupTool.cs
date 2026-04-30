#if UNITY_EDITOR
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Settings;
using UnityEditor.AddressableAssets.Settings.GroupSchemas;
using UnityEngine;

namespace BalloonFlow.EditorTools
{
    /// <summary>
    /// Addressables 그룹 + 라벨 자동 셋업.
    /// 메뉴: BalloonFlow > Addressables > Setup Groups & Labels
    ///
    /// 생성/보장 항목:
    ///   그룹: Local_Always (label core), Local_OnDemand (label ui), Remote_CDM (label cdm)
    ///   라벨: core, ui, cdm, bgm, sfx
    ///
    /// idempotent — 여러 번 실행해도 안전.
    /// </summary>
    public static class AddressableSetupTool
    {
        private const string LOG_TAG = "[AddrSetup]";

        // 그룹명 (코드 const 와 일치)
        private const string GROUP_LOCAL_ALWAYS = "Local_Always";
        private const string GROUP_LOCAL_DEMAND = "Local_OnDemand";
        private const string GROUP_REMOTE_CDM   = "Remote_CDM";

        private static readonly string[] REQUIRED_LABELS = { "core", "ui", "cdm", "bgm", "sfx" };

        [MenuItem("BalloonFlow/Addressables/Setup Groups & Labels")]
        public static void SetupGroupsAndLabels()
        {
            var settings = AddressableAssetSettingsDefaultObject.Settings;
            if (settings == null)
            {
                Debug.LogError($"{LOG_TAG} AddressableAssetSettings 가 없음. Window > Asset Management > Addressables > Groups 한 번 열어 자동 생성 후 재실행.");
                return;
            }

            // 1) 라벨 보장
            foreach (var label in REQUIRED_LABELS)
            {
                if (!settings.GetLabels().Contains(label))
                {
                    settings.AddLabel(label);
                    Debug.Log($"{LOG_TAG} Label '{label}' 추가");
                }
            }

            // 2) 그룹 보장
            EnsureGroup(settings, GROUP_LOCAL_ALWAYS, isRemote: false);
            EnsureGroup(settings, GROUP_LOCAL_DEMAND, isRemote: false);
            EnsureGroup(settings, GROUP_REMOTE_CDM,   isRemote: true);

            EditorUtility.SetDirty(settings);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Debug.Log($"{LOG_TAG} ✔ 그룹/라벨 셋업 완료. 다음 단계: Window > Asset Management > Addressables > Groups 에서 asset 들 드래그 후 라벨 부여.");
        }

        private static void EnsureGroup(AddressableAssetSettings settings, string groupName, bool isRemote)
        {
            var existing = settings.FindGroup(groupName);
            if (existing != null)
            {
                Debug.Log($"{LOG_TAG} Group '{groupName}' 이미 존재 — skip");
                return;
            }

            // 템플릿 (Packed Assets) 기반으로 새 그룹 생성
            var template = settings.GroupTemplateObjects.OfType<AddressableAssetGroupTemplate>().FirstOrDefault();
            var newGroup = settings.CreateGroup(
                groupName,
                setAsDefaultGroup: false,
                readOnly: false,
                postEvent: true,
                schemasToCopy: template != null ? template.SchemaObjects : null);

            // BundledAssetGroupSchema 의 BuildPath / LoadPath 설정
            var bundledSchema = newGroup.GetSchema<BundledAssetGroupSchema>();
            if (bundledSchema != null)
            {
                if (isRemote)
                {
                    bundledSchema.BuildPath.SetVariableByName(settings,
                        AddressableAssetSettings.kRemoteBuildPath);
                    bundledSchema.LoadPath.SetVariableByName(settings,
                        AddressableAssetSettings.kRemoteLoadPath);
                    bundledSchema.UseAssetBundleCache = true;
                    bundledSchema.UseAssetBundleCrc = true;
                }
                else
                {
                    bundledSchema.BuildPath.SetVariableByName(settings,
                        AddressableAssetSettings.kLocalBuildPath);
                    bundledSchema.LoadPath.SetVariableByName(settings,
                        AddressableAssetSettings.kLocalLoadPath);
                }
            }

            Debug.Log($"{LOG_TAG} Group '{groupName}' 생성 (Remote={isRemote})");
        }

        /// <summary>
        /// 어떤 asset 을 우클릭 → Addressables > Mark Local_Always 식의 메뉴.
        /// 선택한 asset 들을 그룹/라벨에 일괄 추가.
        /// </summary>
        [MenuItem("Assets/Addressables/Mark as Local_Always (core)", true)]
        private static bool ValidateMarkLocalAlways() => Selection.objects.Length > 0;

        [MenuItem("Assets/Addressables/Mark as Local_Always (core)")]
        private static void MarkLocalAlways() => MarkSelected(GROUP_LOCAL_ALWAYS, "core");

        [MenuItem("Assets/Addressables/Mark as Local_OnDemand (ui)", true)]
        private static bool ValidateMarkLocalDemand() => Selection.objects.Length > 0;

        [MenuItem("Assets/Addressables/Mark as Local_OnDemand (ui)")]
        private static void MarkLocalDemand() => MarkSelected(GROUP_LOCAL_DEMAND, "ui");

        [MenuItem("Assets/Addressables/Mark as Remote_CDM (cdm)", true)]
        private static bool ValidateMarkRemote() => Selection.objects.Length > 0;

        [MenuItem("Assets/Addressables/Mark as Remote_CDM (cdm)")]
        private static void MarkRemote() => MarkSelected(GROUP_REMOTE_CDM, "cdm");

        private static void MarkSelected(string groupName, string label)
        {
            var settings = AddressableAssetSettingsDefaultObject.Settings;
            if (settings == null)
            {
                Debug.LogError($"{LOG_TAG} AddressableAssetSettings 없음. 먼저 BalloonFlow > Addressables > Setup Groups & Labels 실행.");
                return;
            }
            var group = settings.FindGroup(groupName);
            if (group == null)
            {
                Debug.LogError($"{LOG_TAG} Group '{groupName}' 없음. Setup 먼저 실행.");
                return;
            }
            if (!settings.GetLabels().Contains(label)) settings.AddLabel(label);

            int added = 0;
            foreach (var obj in Selection.objects)
            {
                string path = AssetDatabase.GetAssetPath(obj);
                if (string.IsNullOrEmpty(path)) continue;
                string guid = AssetDatabase.AssetPathToGUID(path);
                if (string.IsNullOrEmpty(guid)) continue;

                var entry = settings.CreateOrMoveEntry(guid, group, readOnly: false, postEvent: false);
                if (entry == null) continue;

                if (!entry.labels.Contains(label)) entry.SetLabel(label, true, true);
                added++;
            }

            EditorUtility.SetDirty(settings);
            AssetDatabase.SaveAssets();
            Debug.Log($"{LOG_TAG} '{groupName}' / label '{label}' 에 {added} asset 추가.");
        }
    }
}
#endif
