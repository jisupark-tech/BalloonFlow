using UnityEngine;

namespace BalloonFlow
{
    /// <summary>
    /// 지각 균일 색 거리(CIEDE2000) 유틸. MapMaker Import Image 의 팔레트 스냅 품질용.
    /// sRGB(0~1) → CIELab(D65) 변환 + CIEDE2000 ΔE. 단순 RGB/Redmean 대비 "사람 눈 기준" 최근접 정확.
    /// 레퍼런스(bl_palette_snap_base28.py rgb2lab/deltaE2000)와 1:1 대응.
    /// ROLLBACK_MAPMAKER_CIEDE2000_20260623: 색매핑 거리 메트릭 도입.
    /// </summary>
    public static class PerceptualColor
    {
        public struct Lab { public float L, a, b; public Lab(float l, float aa, float bb) { L = l; a = aa; b = bb; } }

        /// <summary>sRGB(Unity Color, 0~1) → CIELab(D65).</summary>
        public static Lab RgbToLab(Color c)
        {
            float r = InvGamma(c.r), g = InvGamma(c.g), b = InvGamma(c.b);
            // 선형 RGB → XYZ (D65), 백색점 정규화
            float x = (r * 0.4124f + g * 0.3576f + b * 0.1805f) / 0.95047f;
            float y = (r * 0.2126f + g * 0.7152f + b * 0.0722f);
            float z = (r * 0.0193f + g * 0.1192f + b * 0.9505f) / 1.08883f;
            float fx = F(x), fy = F(y), fz = F(z);
            return new Lab(116f * fy - 16f, 500f * (fx - fy), 200f * (fy - fz));
        }

        private static float InvGamma(float u)
        {
            return u > 0.04045f ? Mathf.Pow((u + 0.055f) / 1.055f, 2.4f) : u / 12.92f;
        }

        private static float F(float t)
        {
            return t > 0.008856f ? Mathf.Pow(t, 1f / 3f) : 7.787f * t + 16f / 116f;
        }

        /// <summary>CIEDE2000 ΔE (클수록 더 다른 색). 표준 공식.</summary>
        public static float DeltaE2000(Lab l1, Lab l2)
        {
            const float deg = Mathf.Rad2Deg, rad = Mathf.Deg2Rad;
            float L1 = l1.L, a1 = l1.a, b1 = l1.b;
            float L2 = l2.L, a2 = l2.a, b2 = l2.b;

            float C1 = Mathf.Sqrt(a1 * a1 + b1 * b1);
            float C2 = Mathf.Sqrt(a2 * a2 + b2 * b2);
            float Cbar = (C1 + C2) * 0.5f;
            float Cbar7 = Mathf.Pow(Cbar, 7f);
            float G = 0.5f * (1f - Mathf.Sqrt(Cbar7 / (Cbar7 + 6103515625f))); // 25^7 = 6103515625
            float a1p = (1f + G) * a1;
            float a2p = (1f + G) * a2;
            float C1p = Mathf.Sqrt(a1p * a1p + b1 * b1);
            float C2p = Mathf.Sqrt(a2p * a2p + b2 * b2);

            float h1p = HueDeg(b1, a1p);
            float h2p = HueDeg(b2, a2p);

            float dLp = L2 - L1;
            float dCp = C2p - C1p;

            float dhp;
            if (C1p * C2p == 0f) dhp = 0f;
            else
            {
                float diff = h2p - h1p;
                if (diff > 180f) diff -= 360f;
                else if (diff < -180f) diff += 360f;
                dhp = diff;
            }
            float dHp = 2f * Mathf.Sqrt(C1p * C2p) * Mathf.Sin((dhp * 0.5f) * rad);

            float Lbarp = (L1 + L2) * 0.5f;
            float Cbarp = (C1p + C2p) * 0.5f;

            float hbarp;
            if (C1p * C2p == 0f) hbarp = h1p + h2p;
            else
            {
                // 레퍼런스(bl_palette_snap_base28.deltaE2000) 정확 대응:
                // hdiff>180 일 때 hsum<360 → (hsum+360)/2, 아니면 (hsum-360)/2.
                float diff = Mathf.Abs(h1p - h2p);
                float sum = h1p + h2p;
                if (diff <= 180f) hbarp = sum * 0.5f;
                else if (sum < 360f) hbarp = (sum + 360f) * 0.5f;
                else hbarp = (sum - 360f) * 0.5f;
            }

            float T = 1f
                - 0.17f * Mathf.Cos((hbarp - 30f) * rad)
                + 0.24f * Mathf.Cos((2f * hbarp) * rad)
                + 0.32f * Mathf.Cos((3f * hbarp + 6f) * rad)
                - 0.20f * Mathf.Cos((4f * hbarp - 63f) * rad);

            float dTheta = 30f * Mathf.Exp(-((hbarp - 275f) / 25f) * ((hbarp - 275f) / 25f));
            float Cbarp7 = Mathf.Pow(Cbarp, 7f);
            float Rc = 2f * Mathf.Sqrt(Cbarp7 / (Cbarp7 + 6103515625f));
            float Lm = (Lbarp - 50f) * (Lbarp - 50f);
            float Sl = 1f + (0.015f * Lm) / Mathf.Sqrt(20f + Lm);
            float Sc = 1f + 0.045f * Cbarp;
            float Sh = 1f + 0.015f * Cbarp * T;
            float Rt = -Mathf.Sin((2f * dTheta) * rad) * Rc;

            float termL = dLp / Sl;
            float termC = dCp / Sc;
            float termH = dHp / Sh;
            return Mathf.Sqrt(termL * termL + termC * termC + termH * termH + Rt * termC * termH);
        }

        private static float HueDeg(float b, float ap)
        {
            if (ap == 0f && b == 0f) return 0f;
            float h = Mathf.Atan2(b, ap) * Mathf.Rad2Deg;
            return h < 0f ? h + 360f : h;
        }
    }
}
