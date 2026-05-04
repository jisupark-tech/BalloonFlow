using UnityEditor;
using UnityEditor.U2D;
using UnityEngine;
using UnityEngine.U2D;

namespace BalloonFlow.EditorTools
{
    /// <summary>
    /// 모바일 빌드에서 atlas sprite 가 거꾸로 / 깨져서 나오는 이슈 일괄 해결.
    /// 메뉴 BalloonFlow > Fix UI Atlas for Mobile 1회 실행.
    ///
    /// 원인 + 해결:
    ///   - Allow Rotation ON: atlas packer 가 sprite 회전 packing → 일부 모바일 GPU 에서 sampling 시 회전 정보 미적용 → 거꾸로 표시. → OFF
    ///   - Tight Packing ON: mesh 기반 packing → 모바일 mesh 데이터 호환 깨질 시 이미지 변형. → OFF (Rectangle packing)
    ///   - Compression default: 모바일 fallback 시 format 불일치로 색 깨짐. → ASTC 6x6 명시 (Android 모든 GLES3 디바이스 호환)
    ///   - Read/Write Enabled: 메모리 2배 사용 + 모바일 GC 부하. → OFF
    ///   - Generate Mip Maps: UI atlas 미필요 (texture 가 항상 1:1 표시). → OFF
    ///
    /// 실행 후: 다시 Build & Run 해서 디바이스 검증.
    /// </summary>
    public static class AtlasMobileFixer
    {
        private const string ATLAS_PATH = "Assets/4.Atlas/UI.spriteatlas";

        [MenuItem("BalloonFlow/DON'T USE/Fix UI Atlas for Mobile")]
        public static void Fix()
        {
            var atlas = AssetDatabase.LoadAssetAtPath<SpriteAtlas>(ATLAS_PATH);
            if (atlas == null)
            {
                Debug.LogError($"[AtlasMobileFixer] Atlas not found: {ATLAS_PATH}");
                return;
            }

            int n = 0;
            n += FixPackingSettings(atlas);
            n += FixTextureSettings(atlas);
            n += FixAndroidPlatformSettings(atlas);
            n += FixIOSPlatformSettings(atlas);

            EditorUtility.SetDirty(atlas);
            AssetDatabase.SaveAssets();

            // 현재 플랫폼 기준 atlas 재패킹 (변경된 settings 즉시 반영)
            SpriteAtlasUtility.PackAtlases(new[] { atlas }, EditorUserBuildSettings.activeBuildTarget);
            Debug.Log("[AtlasMobileFixer] Atlas 재패킹 완료 (현재 플랫폼).");

            EditorUtility.DisplayDialog("Atlas Mobile Fix",
                $"{n} 항목 변경 + 재패킹 완료.\n\n" +
                "다음 단계:\n" +
                "  1) File > Build Settings > Build & Run\n" +
                "  2) 디바이스에서 sprite 깨짐/거꾸로 사라졌는지 확인",
                "OK");
        }

        // ─────────────────────────────────────────────────────────────
        // Packing — rotation / tight packing OFF
        // ─────────────────────────────────────────────────────────────

        private static int FixPackingSettings(SpriteAtlas atlas)
        {
            int n = 0;
            var p = atlas.GetPackingSettings();

            if (p.enableRotation)
            {
                p.enableRotation = false;
                Debug.Log("[AtlasMobileFixer] ✓ Packing: Enable Rotation off (거꾸로 방지)");
                n++;
            }
            if (p.enableTightPacking)
            {
                p.enableTightPacking = false;
                Debug.Log("[AtlasMobileFixer] ✓ Packing: Tight Packing off (mesh 깨짐 방지)");
                n++;
            }
            // padding 4 (default) 그대로

            if (n > 0) atlas.SetPackingSettings(p);
            return n;
        }

        // ─────────────────────────────────────────────────────────────
        // Texture — Read/Write off, MipMaps off, Bilinear
        // ─────────────────────────────────────────────────────────────

        private static int FixTextureSettings(SpriteAtlas atlas)
        {
            int n = 0;
            var t = atlas.GetTextureSettings();

            if (t.readable)
            {
                t.readable = false;
                Debug.Log("[AtlasMobileFixer] ✓ Texture: Read/Write off (메모리 2배 사용 방지)");
                n++;
            }
            if (t.generateMipMaps)
            {
                t.generateMipMaps = false;
                Debug.Log("[AtlasMobileFixer] ✓ Texture: Generate Mip Maps off (UI atlas 미필요)");
                n++;
            }
            if (t.filterMode != FilterMode.Bilinear)
            {
                t.filterMode = FilterMode.Bilinear;
                Debug.Log("[AtlasMobileFixer] ✓ Texture: Filter Mode = Bilinear");
                n++;
            }
            // sRGB true 유지 (UI sprite 는 표준 sRGB)

            if (n > 0) atlas.SetTextureSettings(t);
            return n;
        }

        // ─────────────────────────────────────────────────────────────
        // Android — ASTC 6x6 (GLES3 모든 디바이스 호환)
        // ─────────────────────────────────────────────────────────────

        private static int FixAndroidPlatformSettings(SpriteAtlas atlas)
        {
            int n = 0;
            var ps = atlas.GetPlatformSettings("Android");
            bool changed = false;

            if (!ps.overridden) { ps.overridden = true; changed = true; }
            if (ps.format != TextureImporterFormat.ASTC_6x6)
            {
                ps.format = TextureImporterFormat.ASTC_6x6;
                changed = true;
            }
            if (ps.maxTextureSize != 2048) { ps.maxTextureSize = 2048; changed = true; }
            if (ps.compressionQuality != 50) { ps.compressionQuality = 50; changed = true; }
            // ETC2 fallback (구형 디바이스 ASTC 미지원 시 — Galaxy S8 이전)
            if (ps.androidETC2FallbackOverride != AndroidETC2FallbackOverride.UseBuildSettings)
            {
                ps.androidETC2FallbackOverride = AndroidETC2FallbackOverride.UseBuildSettings;
                changed = true;
            }

            if (changed)
            {
                atlas.SetPlatformSettings(ps);
                Debug.Log("[AtlasMobileFixer] ✓ Android: ASTC 6x6, max 2048, ETC2 fallback (build settings)");
                n++;
            }
            return n;
        }

        // ─────────────────────────────────────────────────────────────
        // iOS — ASTC 6x6 (iPhone 5s+ 모두 호환)
        // ─────────────────────────────────────────────────────────────

        private static int FixIOSPlatformSettings(SpriteAtlas atlas)
        {
            int n = 0;
            var ps = atlas.GetPlatformSettings("iPhone");
            bool changed = false;

            if (!ps.overridden) { ps.overridden = true; changed = true; }
            if (ps.format != TextureImporterFormat.ASTC_6x6)
            {
                ps.format = TextureImporterFormat.ASTC_6x6;
                changed = true;
            }
            if (ps.maxTextureSize != 2048) { ps.maxTextureSize = 2048; changed = true; }
            if (ps.compressionQuality != 50) { ps.compressionQuality = 50; changed = true; }

            if (changed)
            {
                atlas.SetPlatformSettings(ps);
                Debug.Log("[AtlasMobileFixer] ✓ iOS: ASTC 6x6, max 2048");
                n++;
            }
            return n;
        }
    }
}
