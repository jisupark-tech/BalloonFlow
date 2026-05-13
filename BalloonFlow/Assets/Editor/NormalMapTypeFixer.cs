#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

namespace BalloonFlow.EditorTools
{
    /// <summary>
    /// [2026-05-13] Normal Map 텍스처가 Sprite 타입으로 import 되어 atlas 에 묶이면
    /// shader 의 _BumpMap UV 가 깨져 모델링 표면 망가짐.
    /// 이 유틸이 textureType=Sprite → NormalMap 으로 재설정 + reimport.
    /// Atlas 는 Sprite 타입만 packing 하므로 자동으로 제외됨.
    /// </summary>
    public static class NormalMapTypeFixer
    {
        private static readonly string[] NORMAL_MAP_PATHS =
        {
            "Assets/Resources/Texture/BoxLidNormal.png",
            "Assets/Resources/Texture/IronBoxNormal.png",
        };

        [MenuItem("BalloonFlow/Atlas/Fix Normal Map Texture Type", false, 220)]
        public static void FixNormalMaps()
        {
            int fixed_ = 0, skipped = 0, missing = 0;
            foreach (var p in NORMAL_MAP_PATHS)
            {
                var importer = AssetImporter.GetAtPath(p) as TextureImporter;
                if (importer == null) { missing++; Debug.LogWarning($"[NormalMapFixer] not found: {p}"); continue; }

                if (importer.textureType == TextureImporterType.NormalMap) { skipped++; continue; }

                importer.textureType = TextureImporterType.NormalMap;
                importer.SaveAndReimport();
                Debug.Log($"[NormalMapFixer] {p}: textureType → NormalMap");
                fixed_++;
            }

            // Atlas 재팩 트리거 — 변경된 텍스처가 Sprite 가 아니므로 다음 build 시 자동 제외.
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            EditorUtility.DisplayDialog("Normal Map 타입 수정 완료",
                $"수정 {fixed_}개 / skip {skipped} / missing {missing}\n\n" +
                "Atlas Build 또는 Play 시 Sprite 가 아닌 텍스처는 자동 제외됨.\n" +
                "Normal map 은 Material 의 _BumpMap 슬롯에서 직접 참조해야 함.",
                "OK");
        }
    }
}
#endif
