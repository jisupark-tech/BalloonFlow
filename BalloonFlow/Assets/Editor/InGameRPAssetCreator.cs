using UnityEditor;
using UnityEngine;

namespace BalloonFlow.EditorTools
{
    /// <summary>
    /// InGame_RPAsset 자동 생성. Mobile_RPAsset 복제 + 더 minimal 옵션 적용.
    /// 인게임 씬 전용 — Lobby / Title / Shop 은 그대로 Mobile_RPAsset 사용.
    ///
    /// 차이점 (Mobile_RPAsset 대비):
    ///   - Render Scale 0.7 → 0.6 (더 aggressive fillrate 감소)
    ///   - HDR Color Buffer Precision 명시
    ///   - 모든 부하 옵션 OFF (Mobile_RPAsset 와 동일하지만 명시적)
    ///
    /// 사용:
    ///   1) 메뉴 실행 → Assets/Settings/InGame_RPAsset.asset 생성
    ///   2) Title 씬 GameManager Inspector 에서 InGame_RPAsset 슬롯에 드래그 할당
    ///   3) GameManager.LoadSceneCoroutine 에서 InGame 씬 진입 시 switch
    /// </summary>
    public static class InGameRPAssetCreator
    {
        private const string SRC_PATH = "Assets/Settings/Mobile_RPAsset.asset";
        private const string DST_PATH = "Assets/Settings/InGame_RPAsset.asset";

        [MenuItem("BalloonFlow/Create InGame_RPAsset (인게임 전용 minimal)")]
        public static void Create()
        {
            if (AssetDatabase.LoadMainAssetAtPath(SRC_PATH) == null)
            {
                EditorUtility.DisplayDialog("Source Missing",
                    $"{SRC_PATH} 가 없습니다. 먼저 Mobile_RPAsset 을 확인하세요.",
                    "OK");
                return;
            }

            if (AssetDatabase.LoadMainAssetAtPath(DST_PATH) != null)
            {
                if (!EditorUtility.DisplayDialog("Overwrite",
                    $"{DST_PATH} 가 이미 존재. 덮어쓰시겠습니까?",
                    "Yes (덮어쓰기)", "Cancel"))
                    return;
                AssetDatabase.DeleteAsset(DST_PATH);
            }

            if (!AssetDatabase.CopyAsset(SRC_PATH, DST_PATH))
            {
                EditorUtility.DisplayDialog("Copy Failed",
                    $"복사 실패: {SRC_PATH} → {DST_PATH}",
                    "OK");
                return;
            }
            AssetDatabase.Refresh();

            var asset = AssetDatabase.LoadMainAssetAtPath(DST_PATH);
            if (asset == null)
            {
                Debug.LogError("[InGameRPAssetCreator] 복제 후 load 실패");
                return;
            }

            int n = ApplyAggressiveSettings(asset);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            EditorUtility.DisplayDialog("InGame_RPAsset 생성 완료",
                $"{DST_PATH} 생성됨.\n{n} 옵션 minimal 적용.\n\n" +
                "다음 단계:\n" +
                "  1) Title 씬 열기\n" +
                "  2) GameManager 선택 → Inspector\n" +
                "  3) InGame RPAsset 슬롯에 InGame_RPAsset 드래그 할당\n" +
                "  4) Lobby RPAsset 슬롯에 Mobile_RPAsset 드래그 할당\n" +
                "  5) Title 씬 저장 + 빌드",
                "OK");
        }

        private static int ApplyAggressiveSettings(Object asset)
        {
            var so = new SerializedObject(asset);
            int n = 0;

            n += SetBool (so, "m_SupportsHDR",                       false,  "HDR off");
            n += SetFloat(so, "m_RenderScale",                       0.5f,   "Render Scale 0.5 (인게임 전용 aggressive — GPU 78% bound 추가 절감)");
            n += SetInt  (so, "m_AdditionalLightsRenderingMode",     0,      "Additional Lights Disabled");
            n += SetBool (so, "m_EnableLODCrossFade",                false,  "LOD CrossFade off");
            n += SetBool (so, "m_ReflectionProbeBlending",           false,  "Reflection Probe Blending off");
            n += SetBool (so, "m_ReflectionProbeBoxProjection",      false,  "Reflection Probe Box Projection off");
            n += SetInt  (so, "m_AdditionalLightsCookieResolution",  256,    "Cookie Atlas Resolution 256");
            n += SetBool (so, "m_SupportsCameraDepthTexture",        false,  "Depth Texture off");
            n += SetBool (so, "m_SupportsCameraOpaqueTexture",       false,  "Opaque Texture off");
            n += SetBool (so, "m_UseSRPBatcher",                     true,   "SRP Batcher on");
            n += SetBool (so, "m_UseFastSRGBLinearConversion",       true,   "Fast SRGB Linear");
            n += SetBool (so, "m_SupportsTerrainHoles",              false,  "Terrain Holes off");
            n += SetInt  (so, "m_StoreActionsOptimization",          1,      "Store Actions Auto (Mali GPU 핵심)");
            n += SetInt  (so, "m_ColorGradingMode",                  0,      "Color Grading LDR");
            n += SetInt  (so, "m_ColorGradingLutSize",               16,     "Color Grading LUT 16");
            n += SetInt  (so, "m_HDRColorBufferPrecision",           0,      "HDR Color R11G11B10");
            n += SetInt  (so, "m_VolumeFrameworkUpdateMode",         1,      "Volume Update via scripting");

            n += SetBool (so, "m_MainLightShadowsSupported",         false,  "Main Light Shadows off");
            n += SetBool (so, "m_AdditionalLightsShadowsSupported",  false,  "Additional Lights Shadows off");
            n += SetBool (so, "m_SoftShadowsSupported",              false,  "Soft Shadows off");
            n += SetFloat(so, "m_ShadowDistance",                    0f,     "Shadow Distance 0");

            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(asset);
            return n;
        }

        private static int SetBool(SerializedObject so, string n, bool v, string l)
        {
            var p = so.FindProperty(n);
            if (p != null && p.propertyType == SerializedPropertyType.Boolean)
            {
                if (p.boolValue == v) return 0;
                p.boolValue = v;
                Debug.Log($"[InGameRPAssetCreator] ✓ {l}");
                return 1;
            }
            return 0;
        }

        private static int SetFloat(SerializedObject so, string n, float v, string l)
        {
            var p = so.FindProperty(n);
            if (p != null && p.propertyType == SerializedPropertyType.Float)
            {
                if (Mathf.Approximately(p.floatValue, v)) return 0;
                p.floatValue = v;
                Debug.Log($"[InGameRPAssetCreator] ✓ {l}");
                return 1;
            }
            return 0;
        }

        private static int SetInt(SerializedObject so, string n, int v, string l)
        {
            var p = so.FindProperty(n);
            if (p != null && (p.propertyType == SerializedPropertyType.Integer || p.propertyType == SerializedPropertyType.Enum))
            {
                if (p.intValue == v) return 0;
                p.intValue = v;
                Debug.Log($"[InGameRPAssetCreator] ✓ {l}");
                return 1;
            }
            return 0;
        }
    }
}
