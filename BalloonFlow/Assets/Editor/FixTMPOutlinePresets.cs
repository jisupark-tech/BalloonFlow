// ROLLBACK_FIX_TMP_OUTLINE_PRESETS_20260713: 폰트 아틀라스 재생성(폰트 스왑) 후 파생 아웃라인 머티리얼 프리셋
//   재동기화 툴. TMP 는 폰트 '기본 머티리얼'만 자동 갱신하고, 파생 프리셋(ChironGoRoundTC-Black-*Outline.mat)의
//   아틀라스 텍스처/스케일 프로퍼티는 자동 갱신하지 않는다 → 프리셋을 쓰는 아웃라인 텍스트(TxtNumberOutline/TxtLock 등)만 깨짐.
//   이 툴은 폰트 기본 머티리얼의 아틀라스 텍스처 + SDF 스케일 프로퍼티만 각 프리셋에 복사한다(아웃라인 색/두께 등 정체성은 보존).
//   TMP Font Asset Creator 가 Save 시 프리셋에 하는 동작과 동일. 자산은 git 추적되므로 결과가 이상하면 되돌릴 수 있음.
//   롤백: 이 파일 삭제.
using System.Collections.Generic;
using TMPro;
using UnityEditor;
using UnityEngine;

namespace BalloonFlow.EditorTools
{
    public static class FixTMPOutlinePresets
    {
        // 재생성된 폰트 에셋(스왑 대상). 필요 시 경로 수정.
        private const string FontAssetPath =
            "Assets/TextMesh Pro/Resources/Fonts & Materials/ChironGoRoundTC-Black SDF.asset";

        // 이 폰트에서 파생된 프리셋 이름 접두(이 접두로 시작하는 Material 만 대상).
        private const string PresetNamePrefix = "ChironGoRoundTC-Black";

        // TMP SDF 셰이더 프로퍼티 ID — ShaderUtilities 정적 필드는 초기화 순서 의존이 있어 직접 PropertyToID 사용.
        private static readonly int ID_MainTex       = Shader.PropertyToID("_MainTex");
        private static readonly int ID_TextureWidth  = Shader.PropertyToID("_TextureWidth");
        private static readonly int ID_TextureHeight = Shader.PropertyToID("_TextureHeight");
        private static readonly int ID_GradientScale = Shader.PropertyToID("_GradientScale");
        private static readonly int ID_ScaleRatioA   = Shader.PropertyToID("_ScaleRatioA");
        private static readonly int ID_ScaleRatioB   = Shader.PropertyToID("_ScaleRatioB");
        private static readonly int ID_ScaleRatioC   = Shader.PropertyToID("_ScaleRatioC");

        [MenuItem("Tools/BalloonFlow/Fix TMP Outline Presets (re-sync atlas)")]
        public static void Run()
        {
            var font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FontAssetPath);
            if (font == null)
            {
                Debug.LogError($"[FixTMPOutlinePresets] 폰트 에셋을 못 찾음: {FontAssetPath}");
                return;
            }

            Material baseMat = font.material; // 폰트 기본 머티리얼(아틀라스 재생성 시 TMP 가 자동 갱신하는 것)
            if (baseMat == null)
            {
                Debug.LogError("[FixTMPOutlinePresets] 폰트 기본 머티리얼이 null");
                return;
            }

            // 기본 머티리얼에서 아틀라스/스케일 관련 값 추출(TMP Save 가 프리셋에 복사하는 프로퍼티 세트).
            Texture atlas = baseMat.HasProperty(ID_MainTex) ? baseMat.GetTexture(ID_MainTex) : null;
            float texW  = GetF(baseMat, ID_TextureWidth);
            float texH  = GetF(baseMat, ID_TextureHeight);
            float grad  = GetF(baseMat, ID_GradientScale);
            float ratA  = GetF(baseMat, ID_ScaleRatioA);
            float ratB  = GetF(baseMat, ID_ScaleRatioB);
            float ratC  = GetF(baseMat, ID_ScaleRatioC);

            if (atlas == null)
            {
                Debug.LogError("[FixTMPOutlinePresets] 기본 머티리얼에 _MainTex(아틀라스) 없음 — 중단");
                return;
            }

            var guids = AssetDatabase.FindAssets($"t:Material {PresetNamePrefix}");
            var report = new List<string>();
            int synced = 0;

            foreach (var guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
                if (mat == null || mat == baseMat) continue;
                if (!mat.name.StartsWith(PresetNamePrefix)) continue;          // 이름 접두 방어
                if (!mat.HasProperty(ID_GradientScale)) continue;               // TMP SDF 계열만

                // 아틀라스/스케일만 덮어쓰기(아웃라인 _OutlineColor/_OutlineWidth/_FaceColor 등은 그대로 둠).
                SetTex(mat, ID_MainTex, atlas);
                SetF(mat, ID_TextureWidth, texW);
                SetF(mat, ID_TextureHeight, texH);
                SetF(mat, ID_GradientScale, grad);
                SetF(mat, ID_ScaleRatioA, ratA);
                SetF(mat, ID_ScaleRatioB, ratB);
                SetF(mat, ID_ScaleRatioC, ratC);

                EditorUtility.SetDirty(mat);
                synced++;
                report.Add($"  · {mat.name}");
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log($"[FixTMPOutlinePresets] 아틀라스 재동기화 완료: {synced}개 프리셋\n" +
                      $"  atlas={atlas.name} texW={texW} texH={texH} grad={grad} ratA={ratA} ratB={ratB} ratC={ratC}\n" +
                      string.Join("\n", report));
        }

        private static float GetF(Material m, int id) => m.HasProperty(id) ? m.GetFloat(id) : 0f;
        private static void SetF(Material m, int id, float v) { if (m.HasProperty(id)) m.SetFloat(id, v); }
        private static void SetTex(Material m, int id, Texture t) { if (m.HasProperty(id)) m.SetTexture(id, t); }
    }
}
