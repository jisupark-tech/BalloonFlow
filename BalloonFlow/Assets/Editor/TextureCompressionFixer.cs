using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace BalloonFlow.EditorTools
{
    /// <summary>
    /// Android 모바일 일괄 텍스처 압축. ASTC 6x6 (3.56 bpp, RGBA8 11% 사이즈, 시각 손상 미미).
    /// ETC2 fallback (build settings) — 구형 디바이스 호환.
    ///
    /// 적용 범위:
    ///   Assets/2.Sprite/        (UI + tile + 기타 sprite)
    ///   Assets/Resources/Texture/
    ///   Assets/Resources/Sprites/
    /// (Editor Default Resources, MaxSdk, TextMesh Pro 등 SDK 폴더 제외 — 빌드/외부 의존)
    ///
    /// 변경 항목 per texture:
    ///   - Android override ON
    ///   - format = ASTC_6x6
    ///   - compressionQuality = 50 (균형)
    ///   - androidETC2FallbackOverride = UseBuildSettings
    ///   - Read/Write Enabled = OFF
    ///   - maxTextureSize 그대로 유지 (시각 깨짐 방지 — 사용자가 개별 조정)
    ///   - sRGB / type 그대로 유지
    /// </summary>
    public static class TextureCompressionFixer
    {
        private static readonly string[] TARGET_FOLDERS = {
            "Assets/2.Sprite",
            "Assets/Resources/Texture",
            "Assets/Resources/Sprites",
        };

        [MenuItem("BalloonFlow/Compress Textures for Android (ASTC 6x6)")]
        public static void Compress()
        {
            var paths = new List<string>();
            foreach (var folder in TARGET_FOLDERS)
            {
                var guids = AssetDatabase.FindAssets("t:Texture", new[] { folder });
                for (int i = 0; i < guids.Length; i++)
                    paths.Add(AssetDatabase.GUIDToAssetPath(guids[i]));
            }

            if (paths.Count == 0)
            {
                EditorUtility.DisplayDialog("Texture Compression",
                    "대상 폴더에서 텍스처를 찾지 못했습니다. TARGET_FOLDERS 확인.",
                    "OK");
                return;
            }

            int changed = 0;
            int unchanged = 0;
            int skipped = 0;

            try
            {
                AssetDatabase.StartAssetEditing();
                for (int i = 0; i < paths.Count; i++)
                {
                    string p = paths[i];
                    EditorUtility.DisplayProgressBar("Compressing Textures", p, (float)i / paths.Count);

                    var importer = AssetImporter.GetAtPath(p) as TextureImporter;
                    if (importer == null) { skipped++; continue; }

                    bool dirty = false;

                    // Read/Write off — 메모리 2배 사용 방지.
                    if (importer.isReadable) { importer.isReadable = false; dirty = true; }

                    // Android platform override + ASTC 6x6.
                    var ps = importer.GetPlatformTextureSettings("Android");
                    if (!ps.overridden) { ps.overridden = true; dirty = true; }
                    if (ps.format != TextureImporterFormat.ASTC_6x6)
                    {
                        ps.format = TextureImporterFormat.ASTC_6x6;
                        dirty = true;
                    }
                    if (ps.compressionQuality != 50) { ps.compressionQuality = 50; dirty = true; }
                    if (ps.androidETC2FallbackOverride != AndroidETC2FallbackOverride.UseBuildSettings)
                    {
                        ps.androidETC2FallbackOverride = AndroidETC2FallbackOverride.UseBuildSettings;
                        dirty = true;
                    }
                    // maxTextureSize 1024 cap — 디바이스 메모리 / GPU bandwidth 추가 절감.
                    // default 2048 / 4096 인 sprite 도 모바일에선 1024 충분 (1080p 디바이스).
                    if (ps.maxTextureSize > 1024) { ps.maxTextureSize = 1024; dirty = true; }

                    if (dirty)
                    {
                        importer.SetPlatformTextureSettings(ps);
                        importer.SaveAndReimport();
                        changed++;
                    }
                    else unchanged++;
                }
            }
            finally
            {
                AssetDatabase.StopAssetEditing();
                EditorUtility.ClearProgressBar();
            }

            EditorUtility.DisplayDialog("Texture Compression",
                $"총 {paths.Count}개 텍스처 검사:\n" +
                $"  변경: {changed}\n" +
                $"  이미 압축됨: {unchanged}\n" +
                $"  skip (importer 없음): {skipped}\n\n" +
                "다음 단계:\n" +
                "  1) Project Settings > Player > Android > Texture compression: ASTC (이미 default)\n" +
                "  2) Build & Run 으로 디바이스에서 시각 검증\n" +
                "  3) 깨짐 보이는 텍스처는 개별 ASTC 4x4 (고품질) 또는 RGBA32 으로 조정",
                "OK");
        }

        [MenuItem("BalloonFlow/DON'T USE/Restore Textures (Android override OFF)")]
        public static void Restore()
        {
            if (!EditorUtility.DisplayDialog("Restore Texture Settings",
                "모든 대상 텍스처의 Android override 를 OFF 로 되돌립니다 (default 압축 사용).\n계속하시겠습니까?",
                "Yes", "Cancel")) return;

            var paths = new List<string>();
            foreach (var folder in TARGET_FOLDERS)
            {
                var guids = AssetDatabase.FindAssets("t:Texture", new[] { folder });
                for (int i = 0; i < guids.Length; i++)
                    paths.Add(AssetDatabase.GUIDToAssetPath(guids[i]));
            }

            int changed = 0;
            try
            {
                AssetDatabase.StartAssetEditing();
                for (int i = 0; i < paths.Count; i++)
                {
                    string p = paths[i];
                    EditorUtility.DisplayProgressBar("Restoring Textures", p, (float)i / paths.Count);

                    var importer = AssetImporter.GetAtPath(p) as TextureImporter;
                    if (importer == null) continue;

                    var ps = importer.GetPlatformTextureSettings("Android");
                    if (ps.overridden)
                    {
                        ps.overridden = false;
                        importer.SetPlatformTextureSettings(ps);
                        importer.SaveAndReimport();
                        changed++;
                    }
                }
            }
            finally
            {
                AssetDatabase.StopAssetEditing();
                EditorUtility.ClearProgressBar();
            }

            EditorUtility.DisplayDialog("Restore Textures",
                $"{changed}개 텍스처 Android override 해제.",
                "OK");
        }
    }
}
