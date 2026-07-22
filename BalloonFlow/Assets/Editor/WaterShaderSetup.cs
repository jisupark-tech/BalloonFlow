using System.IO;
using UnityEditor;
using UnityEngine;

namespace BalloonFlow.EditorTools
{
    /// <summary>
    /// [WATER_SHADER 2026-07-22] 스타일라이즈드 워터 셋업 — 메뉴 1회 실행 도구.
    ///
    /// 하는 일:
    ///   ① Assets/Textures/WaterNoise.png 생성 — 타일링 펄린 노이즈 128×128(2옥타브, 4귀퉁이 블렌드로
    ///      이음매 없음). 임포트 세팅: Repeat/Bilinear/sRGB off(데이터 텍스처)/밉맵 on.
    ///   ② Assets/Resources/Materials/StylizedWater.mat 생성 — BalloonFlow/StylizedWater 셰이더 +
    ///      노이즈 텍스처 배선 + 모바일 기본값. 이미 있으면 텍스처/셰이더만 동기화(튜닝값 보존).
    ///
    /// 사용: 평면 메시(버텍스 웨이브 쓰려면 서브디비전 필요 — 기본 Unity Plane OK)에 머티리얼 적용.
    /// ⚠️ Depth Effects(깊이 그라데이션+쇼어 폼)는 URP Asset 의 Depth Texture 활성화 필요 —
    ///    미사용 프로젝트면 머티리얼의 Depth Effects 토글을 꺼서 의존성/비용을 0 으로.
    /// ※ 수동 메뉴 전용 — InitializeOnLoad 자동 실행 금지 규칙 준수.
    /// </summary>
    public static class WaterShaderSetup
    {
        private const string TEX_DIR   = "Assets/Textures";
        private const string TEX_PATH  = TEX_DIR + "/WaterNoise.png";
        private const string MAT_DIR   = "Assets/Resources/Materials";
        private const string MAT_PATH  = MAT_DIR + "/StylizedWater.mat";
        private const string SHADER    = "BalloonFlow/StylizedWater";
        private const int    TEX_SIZE  = 128;

        [MenuItem("Tools/BalloonFlow/Setup Stylized Water (noise+mat)")]
        public static void Setup()
        {
            Shader shader = Shader.Find(SHADER);
            if (shader == null)
            {
                EditorUtility.DisplayDialog("Stylized Water",
                    $"셰이더를 찾을 수 없습니다: {SHADER}\n컴파일 에러 여부를 확인하세요.", "확인");
                return;
            }

            // ── ① 타일링 노이즈 텍스처 ──
            if (!File.Exists(TEX_PATH))
            {
                if (!AssetDatabase.IsValidFolder(TEX_DIR))
                    AssetDatabase.CreateFolder("Assets", "Textures");
                File.WriteAllBytes(TEX_PATH, GenerateTileableNoisePng());
                AssetDatabase.ImportAsset(TEX_PATH);

                var importer = (TextureImporter)AssetImporter.GetAtPath(TEX_PATH);
                importer.wrapMode = TextureWrapMode.Repeat;
                importer.filterMode = FilterMode.Bilinear;
                importer.sRGBTexture = false;      // 데이터 텍스처 — 감마 보정 불필요
                importer.mipmapEnabled = true;     // 원거리 수면 셰이딩 안정 + 대역폭 절감
                importer.SaveAndReimport();
                Debug.Log($"[WaterShaderSetup] 노이즈 텍스처 생성 — {TEX_PATH}");
            }
            Texture2D noise = AssetDatabase.LoadAssetAtPath<Texture2D>(TEX_PATH);

            // ── ② 머티리얼 ──
            Material mat = AssetDatabase.LoadAssetAtPath<Material>(MAT_PATH);
            if (mat == null)
            {
                if (!AssetDatabase.IsValidFolder(MAT_DIR))
                    AssetDatabase.CreateFolder("Assets/Resources", "Materials");
                mat = new Material(shader);
                AssetDatabase.CreateAsset(mat, MAT_PATH);
            }
            else if (mat.shader != shader)
            {
                mat.shader = shader;   // 기존 재사용 — 튜닝값 보존, 셰이더만 동기화
            }
            mat.SetTexture("_NoiseTex", noise);
            EditorUtility.SetDirty(mat);
            AssetDatabase.SaveAssets();

            Selection.activeObject = mat;
            EditorGUIUtility.PingObject(mat);
            Debug.Log($"[WaterShaderSetup] 완료 — {MAT_PATH} (shader={SHADER}). " +
                      "평면 메시에 적용하세요. Depth Texture 미사용이면 머티리얼의 Depth Effects 를 끄세요.");
        }

        /// <summary>타일링 펄린 노이즈 PNG — 4귀퉁이 크로스 블렌드로 상하좌우 이음매 제거, 2옥타브.</summary>
        private static byte[] GenerateTileableNoisePng()
        {
            const float SCALE1 = 5f, SCALE2 = 11f;   // 옥타브 주파수(정수 비율 회피)
            var tex = new Texture2D(TEX_SIZE, TEX_SIZE, TextureFormat.RGB24, false); // RGB24 — EncodeToPNG 전 포맷 호환
            var pixels = new Color32[TEX_SIZE * TEX_SIZE];

            for (int y = 0; y < TEX_SIZE; y++)
            {
                float fy = (float)y / TEX_SIZE;
                for (int x = 0; x < TEX_SIZE; x++)
                {
                    float fx = (float)x / TEX_SIZE;
                    float v = TileableNoise(fx, fy, SCALE1) * 0.65f
                            + TileableNoise(fx, fy, SCALE2) * 0.35f;
                    byte b = (byte)Mathf.Clamp(Mathf.RoundToInt(v * 255f), 0, 255);
                    pixels[y * TEX_SIZE + x] = new Color32(b, b, b, 255);
                }
            }
            tex.SetPixels32(pixels);
            byte[] png = tex.EncodeToPNG();
            Object.DestroyImmediate(tex);
            return png;
        }

        /// <summary>[0,1)² 를 주기 경계에서 매끄럽게 잇는 펄린 — 4방향 시프트 샘플의 쌍선형 블렌드.</summary>
        private static float TileableNoise(float x, float y, float scale)
        {
            float sx = x * scale, sy = y * scale;
            float n00 = Mathf.PerlinNoise(sx,          sy);
            float n10 = Mathf.PerlinNoise(sx - scale,  sy);
            float n01 = Mathf.PerlinNoise(sx,          sy - scale);
            float n11 = Mathf.PerlinNoise(sx - scale,  sy - scale);
            float nx0 = Mathf.Lerp(n00, n10, x);
            float nx1 = Mathf.Lerp(n01, n11, x);
            return Mathf.Lerp(nx0, nx1, y);
        }
    }
}
