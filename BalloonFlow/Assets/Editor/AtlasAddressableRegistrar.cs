#if UNITY_EDITOR
using System.IO;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Settings;
using UnityEngine;

namespace BalloonFlow.EditorTools
{
    /// <summary>
    /// [2026-05-13] UI.spriteatlasv2 / GamePlay.spriteatlasv2 를 Addressables catalog 에 자동 등록.
    /// atlas_ui / atlas_gameplay 주소 부여.
    ///
    /// 원인: 코드에서 `Addressables.LoadAssetAsync<T>("atlas_ui")` 호출하지만 catalog 미등록 →
    ///   InvalidKeyException 동기 throw → loading flow 중단.
    /// 해결: 이 메뉴로 1회 등록 후 Build > New Build > Default Build Script 실행.
    /// </summary>
    public static class AtlasAddressableRegistrar
    {
        private const string GROUP_NAME = "Local_Always";

        // [2026-05-13] Unity 2022+ Sprite Atlas V2 (.spriteatlasv2) 사용. V1 (.spriteatlas) 호환 fallback 포함.
        private static readonly (string[] candidates, string address)[] ENTRIES =
        {
            (new[] { "Assets/4.Atlas/UI.spriteatlasv2", "Assets/4.Atlas/UI.spriteatlas" },             "atlas_ui"),
            (new[] { "Assets/4.Atlas/GamePlay.spriteatlasv2", "Assets/4.Atlas/Gameplay.spriteatlas" }, "atlas_gameplay"),
        };

        [MenuItem("BalloonFlow/Atlas/Register Atlases to Addressables", false, 210)]
        public static void RegisterAll()
        {
            var settings = AddressableAssetSettingsDefaultObject.Settings;
            if (settings == null)
            {
                EditorUtility.DisplayDialog("Addressables 미초기화",
                    "Window > Asset Management > Addressables > Groups 에서 'Create Addressables Settings' 먼저 실행.",
                    "OK");
                return;
            }

            var group = settings.FindGroup(GROUP_NAME);
            if (group == null)
            {
                Debug.LogWarning($"[AtlasAddressableRegistrar] group '{GROUP_NAME}' 못 찾음 → DefaultGroup 사용.");
                group = settings.DefaultGroup;
            }

            int registered = 0, skipped = 0, missing = 0;
            foreach (var (candidates, address) in ENTRIES)
            {
                // V2/V1 후보 중 존재하는 첫 경로 사용.
                string foundPath = null;
                foreach (var p in candidates) { if (File.Exists(p)) { foundPath = p; break; } }
                if (foundPath == null)
                {
                    missing++;
                    Debug.LogWarning($"[AtlasAddressableRegistrar] missing all candidates for '{address}': {string.Join(", ", candidates)}");
                    continue;
                }

                string guid = AssetDatabase.AssetPathToGUID(foundPath);
                if (string.IsNullOrEmpty(guid)) { missing++; continue; }

                var existing = settings.FindAssetEntry(guid);
                if (existing != null)
                {
                    if (existing.address != address)
                    {
                        existing.address = address;
                        Debug.Log($"[AtlasAddressableRegistrar] address 갱신: {foundPath} → '{address}'");
                        registered++;
                    }
                    else { skipped++; continue; }
                }
                else
                {
                    var entry = settings.CreateOrMoveEntry(guid, group);
                    entry.address = address;
                    Debug.Log($"[AtlasAddressableRegistrar] 등록: {foundPath} → '{address}' (group={group.Name})");
                    registered++;
                }
            }

            EditorUtility.SetDirty(settings);
            AssetDatabase.SaveAssets();

            string msg = $"등록 {registered}개 / skip {skipped} / missing {missing}\n\n"
                       + "다음 단계:\n"
                       + "1. Window > Asset Management > Addressables > Groups\n"
                       + "2. Build > New Build > Default Build Script\n"
                       + "3. Play 또는 빌드";
            EditorUtility.DisplayDialog("Atlas Addressables 등록 완료", msg, "OK");
        }
    }
}
#endif
