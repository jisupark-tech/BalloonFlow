#if UNITY_EDITOR
using System.IO;
using UnityEditor;
using UnityEngine;

namespace BalloonFlow.EditorTools
{
    /// <summary>
    /// balloon.fbx 용 _BaseMap(albedo) 절차 생성기 — 1024×1024 그레이스케일.
    /// ItemShared 가 _BaseMap × _BaseColor(틴트) 라서, 그레이스케일 텍스처 1장으로 28색 전부 커버.
    /// 베이크 방식 = 런타임 비용 0(셰이더/코드 변경 없음) → 1500 풍선·Android7 에서 FPS 불변.
    /// 카메라가 거의 고정인 보드 뷰에 맞춰 "위쪽 소프트 하이라이트 + 위밝/아래어둠 그라데이션 + 작은 꼭지" 를 굽는다.
    ///
    /// [사용] 상단 메뉴 Tools/Balloon/Generate BaseMap (1024) → Assets/Resources/Modeling/balloon_BaseMap.png 생성.
    ///        balloon 머티리얼의 Base Map 에 할당. 하이라이트 위치(HL_U/HL_V)가 UV 와 안 맞으면 값 바꿔 재생성.
    ///
    /// [참고] _BaseMap×_BaseColor 는 multiply 라 "흰색 sheen" 은 한계(컬러 풍선 위 순백 불가).
    ///        대신 본체를 약간 어둡게(BODY_*) + 하이라이트 코어를 1.0 로 → 대비로 광택감. 순백 sheen 이 꼭 필요하면 셰이더 가법 _GLOSS 경로 별도.
    /// </summary>
    public static class BalloonTextureGenerator
    {
        // ── 튜닝 파라미터 (값 바꾸고 메뉴 재실행하면 재생성) ──
        private const int SIZE = 1024;
        private const string OUT_PATH = "Assets/Resources/Modeling/balloon_BaseMap.png";

        // 본체 밝기 — 곱(_BaseMap×_BaseColor)이라 본체가 밝아야 틴트 색이 선명(어두우면 검게 보임).
        //   전반적으로 밝게(0.8~0.95) + 그라데이션은 약하게.
        private const float BODY_TOP = 0.92f;     // 위 밝기
        private const float BODY_BOTTOM = 0.82f;  // 아래 밝기

        // 메인 하이라이트 (소프트 blob) — "중간 하단"(가로 중앙·세로 아래쪽).
        //   ※ v=0 이 텍스처 하단. 미리보기에서 위/아래가 뒤집혀 보이면 HL_V 를 0.70 으로 바꿔 재실행.
        private const float HL_U = 0.50f, HL_V = 0.30f;   // 중앙·하단
        private const float HL_RU = 0.16f, HL_RV = 0.18f; // 반경
        private const float HL_INT = 0.35f;               // 가산 강도 (코어 ~1.0)

        // 보조 하이라이트 (위쪽 아주 흐린 sheen — 거의 안 보일 정도)
        private const float HL2_U = 0.50f, HL2_V = 0.78f;
        private const float HL2_R = 0.20f, HL2_INT = 0.06f;

        // 하단 가장자리 약간 어둡게(볼륨감) + 꼭지(knot) — 검게 안 보이게 약하게.
        private const float BOTTOM_DARKEN = 0.04f;        // v→0 추가 어둡기(약하게)
        private const float KNOT_U = 0.5f, KNOT_V = 0.03f;
        private const float KNOT_R = 0.030f, KNOT_DARK = 0.72f; // 꼭지 어둡기(곱, 0.72=살짝만)

        [MenuItem("Tools/Balloon/Generate BaseMap (1024)")]
        public static void Generate()
        {
            var tex = new Texture2D(SIZE, SIZE, TextureFormat.RGBA32, true, false);
            var px = new Color32[SIZE * SIZE];

            float inv = 1f / (SIZE - 1);
            for (int y = 0; y < SIZE; y++)
            {
                float v = y * inv;
                for (int x = 0; x < SIZE; x++)
                {
                    float u = x * inv;

                    // 1) 본체 수직 그라데이션 (smoothstep 으로 부드럽게)
                    float t = v * v * (3f - 2f * v); // smoothstep(0,1,v)
                    float lum = Mathf.Lerp(BODY_BOTTOM, BODY_TOP, t);

                    // 2) 하단 가장자리 어둡게(볼륨)
                    lum -= BOTTOM_DARKEN * (1f - t);

                    // 3) 메인 하이라이트 (가산)
                    lum += HL_INT * Gauss(u - HL_U, v - HL_V, HL_RU, HL_RV);
                    // 4) 보조 하이라이트
                    lum += HL2_INT * Gauss(u - HL2_U, v - HL2_V, HL2_R, HL2_R);

                    // 5) 꼭지(knot) — 곱으로 어둡게
                    float k = Gauss(u - KNOT_U, v - KNOT_V, KNOT_R, KNOT_R);
                    lum *= Mathf.Lerp(1f, KNOT_DARK, Mathf.Clamp01(k));

                    lum = Mathf.Clamp01(lum);
                    byte g = (byte)(lum * 255f);
                    px[y * SIZE + x] = new Color32(g, g, g, 255);
                }
            }

            tex.SetPixels32(px);
            tex.Apply(true);

            // PNG 저장
            string dir = Path.GetDirectoryName(OUT_PATH);
            if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
            File.WriteAllBytes(OUT_PATH, tex.EncodeToPNG());
            Object.DestroyImmediate(tex);

            AssetDatabase.ImportAsset(OUT_PATH, ImportAssetOptions.ForceUpdate);

            // 임포트 설정: sRGB albedo, mipmap(1500개 축소 시 앨리어싱/대역폭↓), clamp, 압축은 플랫폼 기본.
            var imp = AssetImporter.GetAtPath(OUT_PATH) as TextureImporter;
            if (imp != null)
            {
                imp.textureType = TextureImporterType.Default;
                imp.sRGBTexture = true;
                imp.mipmapEnabled = true;
                imp.wrapMode = TextureWrapMode.Clamp;
                imp.alphaSource = TextureImporterAlphaSource.None;
                imp.SaveAndReimport();
            }

            AssetDatabase.Refresh();
            Debug.Log($"[BalloonTextureGenerator] 생성 완료: {OUT_PATH} ({SIZE}x{SIZE}). " +
                      $"balloon 머티리얼 Base Map 에 할당하세요. 하이라이트 위치 안 맞으면 HL_U/HL_V 조정 후 재실행.");
            EditorGUIUtility.PingObject(AssetDatabase.LoadAssetAtPath<Texture2D>(OUT_PATH));
        }

        // 정규화 가우시안 blob (du,dv = UV 차, ru/rv = 반경)
        private static float Gauss(float du, float dv, float ru, float rv)
        {
            float a = du / Mathf.Max(1e-4f, ru);
            float b = dv / Mathf.Max(1e-4f, rv);
            return Mathf.Exp(-(a * a + b * b));
        }
    }
}
#endif
