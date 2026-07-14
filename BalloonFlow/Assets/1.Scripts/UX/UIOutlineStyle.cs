using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;

namespace BalloonFlow
{
    public static class UIOutlineStyle
    {
        // ROLLBACK_OUTLINE_LANG_FONT_20260714: 아웃라인 프리셋을 현재 언어의 폰트 패밀리 버전으로 치환.
        //   프리셋 네이밍 규약: "{FontFamily}-{Color}Outline" (예 Poppins-Bold-BlueOutline ↔ ChironGoRoundTC-Black-BlueOutline).
        //   접미(-BlueOutline)=난이도 색 유지, 접두=폰트 패밀리만 언어에 맞게 교체. 프리셋/폰트는 Resources 하위라 이름 로드 가능.
        private const string TmpResFolder = "Fonts & Materials/";
        private static readonly string[] KnownFontFamilies = { "ChironGoRoundTC-Black", "Poppins-Bold" };

        // 특정 폰트를 '강제'해야 하는 언어만 그 패밀리를 반환, 아니면 null(=원본 프리셋 패밀리 유지).
        //   KO 는 한글이 필요해 ChironGoRoundTC-Black 강제. EN 등은 디자이너가 지정한 원본 폰트를 그대로 씀(강제 안 함).
        //   ※ EN 을 Poppins 로 강제하지 않는 이유: 기존 프리셋이 ChironGoRoundTC 계열이면 EN 이 깨지므로. 매핑은 여기 한 곳.
        public static string RequiredFontFamily(string code)
            => string.Equals(code, "KO", StringComparison.OrdinalIgnoreCase) ? "ChironGoRoundTC-Black" : null;

        // baseMat(프리셋)을 현재 언어가 강제하는 폰트 패밀리의 동일 접미 프리셋으로 치환하고, 그 패밀리 SDF 폰트를 out 으로 준다.
        //   강제 없음(비-KO)/미인식 프리셋/로드 실패 → 원본 유지(깨짐 방지).
        public static Material ResolveLanguageOutline(Material baseMat, out TMP_FontAsset font)
        {
            font = null;
            if (baseMat == null) return null;

            string name = baseMat.name;
            string cur = null;
            for (int i = 0; i < KnownFontFamilies.Length; i++)
                if (name.StartsWith(KnownFontFamilies[i], StringComparison.Ordinal)) { cur = KnownFontFamilies[i]; break; }
            if (cur == null) return baseMat; // 규약 밖 머티리얼 → 폰트/머티리얼 그대로

            string want = RequiredFontFamily(LocalizationService.CurrentLanguageCode) ?? cur; // 강제 없으면 원본 패밀리 유지
            font = Resources.Load<TMP_FontAsset>(TmpResFolder + want + " SDF");
            if (cur == want) return baseMat; // 이미 맞는 패밀리 → 폰트만 확정, 머티리얼 유지

            string target = want + name.Substring(cur.Length); // want + "-BlueOutline"
            Material m = Resources.Load<Material>(TmpResFolder + target);
            return m != null ? m : baseMat;
        }

        // 언어인지 아웃라인 적용: 폰트를 먼저 교체(setter 가 fontSharedMaterial 을 리셋)한 뒤 프리셋 머티리얼/색 적용.
        public static void ApplyLanguageAwareOutline(TMP_Text text, Material baseMat, Color color)
        {
            if (text == null) return;
            Material mat = ResolveLanguageOutline(baseMat, out TMP_FontAsset font);
            if (font != null && text.font != font) text.font = font;
            ApplyMaterialOrColor(text, mat, color);
        }

        // 아웃라인 + fill 쌍에 언어인지 아웃라인 적용(폰트/머티리얼 일관 + fill 폰트 동기화 = 이격 방지).
        //   코드가 난이도별 프리셋을 세팅하는 텍스트(레일박스·플레이버튼 등)용. font 해석 실패 시 폴백으로 최소한 폰트는 맞춤.
        public static void ApplyOutlineWithFill(TMP_Text outline, TMP_Text fill, Material baseMat, Color color)
        {
            if (outline == null) return;
            Material mat = ResolveLanguageOutline(baseMat, out TMP_FontAsset font);
            if (font == null) font = FontAssetForMaterial(baseMat); // 로드 실패 시라도 패밀리 폰트 확보
            if (font != null && outline.font != font) outline.font = font;
            ApplyMaterialOrColor(outline, mat, color);
            if (fill != null)
            {
                TMP_FontAsset target = font != null ? font : outline.font;
                if (target != null && fill.font != target) fill.font = target;
            }
        }

        // mat 이름의 폰트 패밀리 접두로 SDF 폰트 로드(mat 과 아틀라스가 맞는 폰트). 규약 밖이면 null.
        public static TMP_FontAsset FontAssetForMaterial(Material mat)
        {
            if (mat == null) return null;
            string name = mat.name;
            for (int i = 0; i < KnownFontFamilies.Length; i++)
                if (name.StartsWith(KnownFontFamilies[i], StringComparison.Ordinal))
                    return Resources.Load<TMP_FontAsset>(TmpResFolder + KnownFontFamilies[i] + " SDF");
            return null;
        }

        // 언어 무관 '고정' 적용: baseMat 을 그대로 쓰되 폰트를 baseMat 패밀리에 맞춰 교체(폰트↔머티리얼 불일치 깨짐 방지).
        //   예: 배속(x1/x2) 처럼 항상 영어 폰트여야 하는 텍스트 — baseMat 이 Poppins 프리셋이면 폰트도 Poppins 로 강제.
        public static void ApplyFixedOutline(TMP_Text text, Material baseMat, Color color)
        {
            if (text == null) return;
            TMP_FontAsset font = FontAssetForMaterial(baseMat);
            if (font != null && text.font != font) text.font = font;
            ApplyMaterialOrColor(text, baseMat, color);
        }

        // ── 폰트 전환 시 '색상/아웃라인 프리셋'을 보존하는 매핑(UIText 등에서 사용) ──
        // ROLLBACK_OUTLINE_LANG_COLORKEEP_20260714: origMat(예 Poppins-Bold-BlueOutline)을 targetFont(예 KO Chiron) 용으로.
        //   1순위: 이름 규약 프리셋(targetFamily-{Color}...) Resources 로드(디자이너가 만든 KO 프리셋).
        //   2순위(없으면): origMat 의 색/아웃라인은 유지하고 아틀라스만 targetFont 로 재타겟한 런타임 파생 →
        //     '블랙 기본 머티리얼로 떨어지는' 문제 방지(색상 항상 보존).
        private const string DerivedSuffix = " (KO-derived)";
        private static readonly int _idMainTex = Shader.PropertyToID("_MainTex");
        private static readonly int _idTexWidth = Shader.PropertyToID("_TextureWidth");
        private static readonly int _idTexHeight = Shader.PropertyToID("_TextureHeight");
        private static readonly int _idGradient = Shader.PropertyToID("_GradientScale");
        private static readonly int _idRatioA = Shader.PropertyToID("_ScaleRatioA");
        private static readonly int _idRatioB = Shader.PropertyToID("_ScaleRatioB");
        private static readonly int _idRatioC = Shader.PropertyToID("_ScaleRatioC");
        private static readonly Dictionary<string, Material> _derivedCache = new Dictionary<string, Material>();

        public static Material MaterialForFont(Material origMat, TMP_FontAsset targetFont, string targetFamily)
        {
            if (origMat == null) return targetFont != null ? targetFont.material : null;

            string baseName = origMat.name.Replace(" (Instance)", "").Replace(DerivedSuffix, "");

            // 1순위: 이름 규약 프리셋(현재 패밀리 접두 → targetFamily 치환) Resources 로드.
            string cur = null;
            for (int i = 0; i < KnownFontFamilies.Length; i++)
                if (baseName.StartsWith(KnownFontFamilies[i], StringComparison.Ordinal)) { cur = KnownFontFamilies[i]; break; }
            if (cur != null && cur != targetFamily)
            {
                string presetName = targetFamily + baseName.Substring(cur.Length);
                Material preset = Resources.Load<Material>(TmpResFolder + presetName);
                if (preset != null) return preset;
            }

            // 2순위: 색 보존 런타임 파생(아틀라스만 targetFont 로).
            return DeriveOnFont(origMat, targetFont, baseName);
        }

        // origMat 의 모든 프로퍼티(색/아웃라인 포함) 복제 + SDF 아틀라스 프로퍼티만 targetFont 기본 머티리얼 것으로 교체.
        //   프리셋 조합별 1개 캐시(공유) → 배칭 유지. targetFont 미유효 시 origMat 반환.
        public static Material DeriveOnFont(Material origMat, TMP_FontAsset targetFont, string baseName = null)
        {
            if (origMat == null) return null;
            if (targetFont == null || targetFont.material == null) return origMat;

            string key = (baseName ?? origMat.name) + "→" + targetFont.name;
            if (_derivedCache.TryGetValue(key, out Material cached) && cached != null) return cached;

            Material src = targetFont.material;
            Material m = new Material(origMat) { name = key + DerivedSuffix };
            if (src.HasProperty(_idMainTex)) m.SetTexture(_idMainTex, src.GetTexture(_idMainTex));
            CopyFloat(m, src, _idTexWidth); CopyFloat(m, src, _idTexHeight); CopyFloat(m, src, _idGradient);
            CopyFloat(m, src, _idRatioA); CopyFloat(m, src, _idRatioB); CopyFloat(m, src, _idRatioC);

            _derivedCache[key] = m;
            return m;
        }

        private static void CopyFloat(Material dst, Material src, int id)
        {
            if (dst.HasProperty(id) && src.HasProperty(id)) dst.SetFloat(id, src.GetFloat(id));
        }

        private static readonly Color DifficultyNormal = FromHex(0x00, 0x13, 0x4F);
        private static readonly Color DifficultyHard = FromHex(0x4B, 0x15, 0x6E);
        private static readonly Color DifficultySuperHard = FromHex(0x67, 0x18, 0x09);

        private static readonly Color ShopPurple = FromHex(0x3F, 0x1F, 0x66);
        private static readonly Color ShopRed = FromHex(0x6A, 0x12, 0x12);

        public static Color ForDifficulty(DifficultyPurpose difficulty)
        {
            return difficulty switch
            {
                DifficultyPurpose.Hard => DifficultyHard,
                DifficultyPurpose.SuperHard => DifficultySuperHard,
                _ => DifficultyNormal
            };
        }

        public static Color ForShopBundle(bool isSpecial)
        {
            return isSpecial ? ShopRed : ShopPurple;
        }

        public static Material SelectDifficultyMaterial(
            DifficultyPurpose difficulty,
            Material normal,
            Material hard,
            Material superHard)
        {
            return difficulty switch
            {
                DifficultyPurpose.Hard => hard,
                DifficultyPurpose.SuperHard => superHard,
                _ => normal
            };
        }

        public static void ApplyMaterialOrColor(TMP_Text text, Material material, Color color)
        {
            if (text == null) return;
            if (material != null) text.fontSharedMaterial = material;
            text.color = WithAlpha(color, text.color.a);
            text.SetVerticesDirty();
            text.SetMaterialDirty();
        }

        public static void ApplyDifficulty(TMP_Text text, DifficultyPurpose difficulty)
        {
            ApplyColor(text, ForDifficulty(difficulty));
        }

        public static void ApplyColor(TMP_Text text, Color color)
        {
            if (text == null) return;
            text.color = WithAlpha(color, text.color.a);
            text.SetVerticesDirty();
        }

        public static Color WithAlpha(Color color, float alpha)
        {
            color.a = alpha;
            return color;
        }

        private static Color FromHex(byte r, byte g, byte b)
        {
            return new Color32(r, g, b, 0xFF);
        }
    }
}
