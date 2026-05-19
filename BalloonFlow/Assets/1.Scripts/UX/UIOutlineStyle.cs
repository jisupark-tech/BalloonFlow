using TMPro;
using UnityEngine;

namespace BalloonFlow
{
    public static class UIOutlineStyle
    {
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
