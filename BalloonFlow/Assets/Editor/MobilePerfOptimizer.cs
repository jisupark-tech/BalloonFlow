using UnityEditor;
using UnityEditor.Build;
using UnityEngine;
using UnityEngine.Rendering;

namespace BalloonFlow.EditorTools
{
    /// <summary>
    /// 저사양 Android (Galaxy S8 / A23 기준) 60FPS 타겟 일괄 설정.
    /// 메뉴 BalloonFlow > Optimize URP for Low-end Mobile 1회 실행.
    ///
    /// 자동 변경:
    ///  URP Asset (Mobile_RPAsset)
    ///    - HDR / Render Scale / Additional Lights / LOD CrossFade
    ///    - Reflection Probe Blending / Box Projection / Atlas Blending
    ///    - Cookie Atlas Resolution / Lens Flare 2종
    ///  Player Settings (Android)
    ///    - Auto Graphics API off + Vulkan 제거 (GLES3 only)
    ///    - Color Space = Gamma
    ///    - Multithreaded Rendering / Static / Dynamic Batching ON
    ///    - Optimized Frame Pacing ON
    ///    - Scripting Backend = IL2CPP, Architecture = ARM64
    ///    - Strip Engine Code ON, Optimize Mesh Data ON
    ///  Quality Settings (Android default level)
    ///    - Pixel Light Count 0, VSync 0, AA 0, Shadow Distance 0
    ///    - Anisotropic Disabled, Soft Particles off, Realtime Reflection Probes off
    /// </summary>
    public static class MobilePerfOptimizer
    {
        private const string URP_ASSET_PATH = "Assets/Settings/Mobile_RPAsset.asset";
        private const string RENDERER_PATH  = "Assets/Settings/Mobile_Renderer.asset";

        [MenuItem("BalloonFlow/DON'T USE/Optimize URP for Low-end Mobile (S8/A23)")]
        public static void Optimize()
        {
            int n = 0;
            n += OptimizeUrpAsset();
            n += OptimizeRenderer();
            n += OptimizePlayerSettings();
            n += OptimizeQualitySettings();

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            EditorUtility.DisplayDialog("Mobile Perf Optimize",
                $"{n} 항목 변경됨.\nConsole 의 [MobilePerfOptimizer] 로그 확인.\n\n" +
                "Application code (targetFrameRate=60) 는 SdkBootstrap 에 자동 추가됨 (별도 commit).",
                "OK");
        }

        // ─────────────────────────────────────────────────────────────
        // URP Asset
        // ─────────────────────────────────────────────────────────────

        // ─────────────────────────────────────────────────────────────
        // Mobile_Renderer (UniversalRendererData) — Post-processing off
        // ─────────────────────────────────────────────────────────────

        private static int OptimizeRenderer()
        {
            var renderer = AssetDatabase.LoadMainAssetAtPath(RENDERER_PATH);
            if (renderer == null)
            {
                Debug.LogWarning($"[MobilePerfOptimizer] Renderer asset not found: {RENDERER_PATH}");
                return 0;
            }

            int n = 0;
            var so = new SerializedObject(renderer);

            // Post-processing 비활성 — postProcessData = null 로 setting (사용자 결정: Bloom 등 안 씀)
            var postProcessProp = so.FindProperty("postProcessData");
            if (postProcessProp != null && postProcessProp.objectReferenceValue != null)
            {
                postProcessProp.objectReferenceValue = null;
                Debug.Log("[MobilePerfOptimizer] ✓ Renderer: postProcessData = null (Post-processing off — GPU bound 절감)");
                n++;
            }

            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(renderer);
            return n;
        }

        private static int OptimizeUrpAsset()
        {
            var asset = AssetDatabase.LoadMainAssetAtPath(URP_ASSET_PATH);
            if (asset == null)
            {
                Debug.LogError($"[MobilePerfOptimizer] URP asset not found: {URP_ASSET_PATH}");
                return 0;
            }

            int n = 0;
            var so = new SerializedObject(asset);

            n += SetBool (so, "m_SupportsHDR",                       false,  "HDR off");
            n += SetFloat(so, "m_RenderScale",                       0.7f,   "Render Scale 0.7 (GPU 97% bound — fillrate 추가 30% 절감)");
            n += SetInt  (so, "m_AdditionalLightsRenderingMode",     0,      "Additional Lights Disabled");
            n += SetBool (so, "m_EnableLODCrossFade",                false,  "LOD CrossFade off");
            n += SetBool (so, "m_ReflectionProbeBlending",           false,  "Reflection Probe Blending off");
            n += SetBool (so, "m_ReflectionProbeBoxProjection",      false,  "Reflection Probe Box Projection off");
            n += SetInt  (so, "m_AdditionalLightsCookieResolution",  256,    "Cookie Atlas Resolution 256");

            // ── 추가 부하 옵션 OFF (Profiler 분석 결과 RenderCameraStack/UICamera 47% 부하 잡기) ──
            n += SetBool (so, "m_SupportsCameraDepthTexture",        false,  "Depth Texture off (모바일 비싼 prepass)");
            n += SetBool (so, "m_SupportsCameraOpaqueTexture",       false,  "Opaque Texture off (UI overlay 에선 거의 안 씀)");
            n += SetBool (so, "m_UseSRPBatcher",                     true,   "SRP Batcher on (drawcall 절감)");
            n += SetBool (so, "m_UseFastSRGBLinearConversion",       true,   "Fast SRGB Linear (Color Space Gamma 면 무관)");
            n += SetBool (so, "m_SupportsTerrainHoles",              false,  "Terrain Holes off (terrain 미사용)");
            n += SetInt  (so, "m_StoreActionsOptimization",          1,      "Store Actions Optimization Auto (모바일 tile-based GPU 핵심)");
            n += SetInt  (so, "m_ColorGradingMode",                  0,      "Color Grading Mode = LowDynamicRange (HDR 모드 비싼)");
            n += SetInt  (so, "m_ColorGradingLutSize",               16,     "Color Grading LUT 16 (default 32 → 16, 절반)");
            n += SetInt  (so, "m_HDRColorBufferPrecision",           0,      "HDR Color Buffer R11G11B10 (32bit float 비싼)");
            n += SetInt  (so, "m_VolumeFrameworkUpdateMode",         1,      "Volume Update = ViaScripting (자동 매 frame update 끔)");

            // URP 17 (Unity 6) RenderGraph — 일부 케이스에서 legacy 보다 무거움
            // 모바일에선 RenderGraph 가 default 지만 보수적으로 유지 (호환성). Profiler 결과 28% 차지하지만
            // legacy 로 강제 변경은 위험 (deprecated 경로). 주석으로 남김.

            // Depth Priming — 모바일에선 보통 비싼 (extra prepass)
            n += SetIntFallback(so, new[] {
                    "m_DepthPrimingMode",
                    "depthPrimingMode"
                }, 0, "Depth Priming Disabled (모바일 prepass 비싼)");

            // Main Light Shadows — Disable Shadows 메뉴 + 여기서도 명시적 OFF
            n += SetBool (so, "m_MainLightShadowsSupported",         false,  "Main Light Shadows off");
            n += SetBool (so, "m_AdditionalLightsShadowsSupported",  false,  "Additional Lights Shadows off");
            n += SetBool (so, "m_SoftShadowsSupported",              false,  "Soft Shadows off");
            n += SetFloat(so, "m_ShadowDistance",                    0f,     "Shadow Distance 0");

            // URP 17 (Unity 6) 에서 이름 다를 가능성 — 후보 fallback
            n += SetBoolFallback(so, new[] {
                    "m_ProbeAtlasBlending",
                    "m_ReflectionProbeAtlasBlending",
                    "m_AtlasBlending"
                }, false, "Probe Atlas Blending off");

            n += SetBoolFallback(so, new[] {
                    "m_DataDrivenLensFlareEnabled",
                    "m_DataDrivenLensFlare",
                    "m_LensFlareEnabled",
                    "supportDataDrivenLensFlare"
                }, false, "Data-Driven Lens Flare off");

            n += SetBoolFallback(so, new[] {
                    "m_ScreenSpaceLensFlareEnabled",
                    "m_ScreenSpaceLensFlare",
                    "supportScreenSpaceLensFlare"
                }, false, "Screen-Space Lens Flare off");

            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(asset);
            return n;
        }

        // ─────────────────────────────────────────────────────────────
        // Player Settings (Android)
        // ─────────────────────────────────────────────────────────────

        private static int OptimizePlayerSettings()
        {
            int n = 0;
            const BuildTarget tgt = BuildTarget.Android;
            var ngt = NamedBuildTarget.Android;

            // Auto Graphics API off + GLES3 only (Vulkan 제거)
            if (PlayerSettings.GetUseDefaultGraphicsAPIs(tgt))
            {
                PlayerSettings.SetUseDefaultGraphicsAPIs(tgt, false);
                Debug.Log("[MobilePerfOptimizer] ✓ Player: Auto Graphics API off");
                n++;
            }
            var current = PlayerSettings.GetGraphicsAPIs(tgt);
            bool needSet = current.Length != 1 || current[0] != GraphicsDeviceType.OpenGLES3;
            if (needSet)
            {
                PlayerSettings.SetGraphicsAPIs(tgt, new[] { GraphicsDeviceType.OpenGLES3 });
                Debug.Log("[MobilePerfOptimizer] ✓ Player: Graphics APIs = OpenGLES3 only (Vulkan 제거)");
                n++;
            }

            // Color Space = Gamma
            if (PlayerSettings.colorSpace != ColorSpace.Gamma)
            {
                PlayerSettings.colorSpace = ColorSpace.Gamma;
                Debug.Log("[MobilePerfOptimizer] ✓ Player: Color Space = Gamma");
                n++;
            }

            // Multithreaded Rendering
            if (!PlayerSettings.GetMobileMTRendering(ngt))
            {
                PlayerSettings.SetMobileMTRendering(ngt, true);
                Debug.Log("[MobilePerfOptimizer] ✓ Player: Multithreaded Rendering on");
                n++;
            }

            // Static / Dynamic Batching — Unity 6 에서 PlayerSettings API 제거됨.
            // Edit > Project Settings > Player 의 Static/Dynamic Batching 토글에서 직접 확인 권장
            // (보통 default 로 둘 다 활성).

            // Optimized Frame Pacing
            if (!PlayerSettings.Android.optimizedFramePacing)
            {
                PlayerSettings.Android.optimizedFramePacing = true;
                Debug.Log("[MobilePerfOptimizer] ✓ Player: Optimized Frame Pacing on");
                n++;
            }

            // Scripting Backend = IL2CPP
            if (PlayerSettings.GetScriptingBackend(ngt) != ScriptingImplementation.IL2CPP)
            {
                PlayerSettings.SetScriptingBackend(ngt, ScriptingImplementation.IL2CPP);
                Debug.Log("[MobilePerfOptimizer] ✓ Player: Scripting Backend = IL2CPP");
                n++;
            }

            // Target Architectures = ARM64
            var wantArch = AndroidArchitecture.ARM64;
            if (PlayerSettings.Android.targetArchitectures != wantArch)
            {
                PlayerSettings.Android.targetArchitectures = wantArch;
                Debug.Log("[MobilePerfOptimizer] ✓ Player: Target Architecture = ARM64");
                n++;
            }

            // Strip Engine Code
            if (PlayerSettings.stripEngineCode != true)
            {
                PlayerSettings.stripEngineCode = true;
                Debug.Log("[MobilePerfOptimizer] ✓ Player: Strip Engine Code on");
                n++;
            }

            // Optimize Mesh Data
            if (!PlayerSettings.stripUnusedMeshComponents)
            {
                PlayerSettings.stripUnusedMeshComponents = true;
                Debug.Log("[MobilePerfOptimizer] ✓ Player: Optimize Mesh Data on");
                n++;
            }

            // Managed Stripping Level — High 면 Firebase reflection 기반 코드가 잘려 디바이스에서 init fail.
            // Minimal = Engine code 만 strip, managed code 보존. link.xml 와 함께 안전.
            if (PlayerSettings.GetManagedStrippingLevel(ngt) != ManagedStrippingLevel.Minimal)
            {
                PlayerSettings.SetManagedStrippingLevel(ngt, ManagedStrippingLevel.Minimal);
                Debug.Log("[MobilePerfOptimizer] ✓ Player: Managed Stripping Level = Minimal (Firebase reflection 보호)");
                n++;
            }

            // Min SDK Version 24 (Android 7.0 Nougat = Galaxy S8 base) — 1.0 minimum spec 명시
            if (PlayerSettings.Android.minSdkVersion != AndroidSdkVersions.AndroidApiLevel24)
            {
                PlayerSettings.Android.minSdkVersion = AndroidSdkVersions.AndroidApiLevel24;
                Debug.Log("[MobilePerfOptimizer] ✓ Player: Min SDK = Android 7.0 (API 24, Galaxy S8 base)");
                n++;
            }

            return n;
        }

        // ─────────────────────────────────────────────────────────────
        // Quality Settings (Android default level)
        // ─────────────────────────────────────────────────────────────

        private static int OptimizeQualitySettings()
        {
            int n = 0;
            int original = QualitySettings.GetQualityLevel();

            // Android default quality level 변경. 다중 level 일괄 변경 위해 모든 level 순회.
            string[] names = QualitySettings.names;
            for (int i = 0; i < names.Length; i++)
            {
                QualitySettings.SetQualityLevel(i, false);

                if (QualitySettings.pixelLightCount != 0)
                {
                    QualitySettings.pixelLightCount = 0;
                    Debug.Log($"[MobilePerfOptimizer] ✓ Quality[{names[i]}]: Pixel Light Count 0");
                    n++;
                }
                if (QualitySettings.vSyncCount != 0)
                {
                    QualitySettings.vSyncCount = 0;
                    Debug.Log($"[MobilePerfOptimizer] ✓ Quality[{names[i]}]: VSync Count 0");
                    n++;
                }
                if (QualitySettings.antiAliasing != 0)
                {
                    QualitySettings.antiAliasing = 0;
                    Debug.Log($"[MobilePerfOptimizer] ✓ Quality[{names[i]}]: Anti Aliasing 0");
                    n++;
                }
                if (QualitySettings.shadowDistance > 0f)
                {
                    QualitySettings.shadowDistance = 0f;
                    Debug.Log($"[MobilePerfOptimizer] ✓ Quality[{names[i]}]: Shadow Distance 0");
                    n++;
                }
                if (QualitySettings.anisotropicFiltering != AnisotropicFiltering.Disable)
                {
                    QualitySettings.anisotropicFiltering = AnisotropicFiltering.Disable;
                    Debug.Log($"[MobilePerfOptimizer] ✓ Quality[{names[i]}]: Anisotropic Disabled");
                    n++;
                }
                if (QualitySettings.softParticles)
                {
                    QualitySettings.softParticles = false;
                    Debug.Log($"[MobilePerfOptimizer] ✓ Quality[{names[i]}]: Soft Particles off");
                    n++;
                }
                if (QualitySettings.realtimeReflectionProbes)
                {
                    QualitySettings.realtimeReflectionProbes = false;
                    Debug.Log($"[MobilePerfOptimizer] ✓ Quality[{names[i]}]: Realtime Reflection Probes off");
                    n++;
                }
                if (QualitySettings.billboardsFaceCameraPosition)
                {
                    QualitySettings.billboardsFaceCameraPosition = false;
                    Debug.Log($"[MobilePerfOptimizer] ✓ Quality[{names[i]}]: Billboards Face Camera Position off");
                    n++;
                }
            }

            QualitySettings.SetQualityLevel(original, false);
            return n;
        }

        // ─────────────────────────────────────────────────────────────
        // SerializedObject helpers
        // ─────────────────────────────────────────────────────────────

        private static int SetBool(SerializedObject so, string propName, bool value, string label)
        {
            var p = so.FindProperty(propName);
            if (p == null) { Debug.Log($"[MobilePerfOptimizer] skip '{label}' — property '{propName}' 없음"); return 0; }
            if (p.boolValue == value) return 0;
            p.boolValue = value;
            Debug.Log($"[MobilePerfOptimizer] ✓ {label}");
            return 1;
        }

        private static int SetFloat(SerializedObject so, string propName, float value, string label)
        {
            var p = so.FindProperty(propName);
            if (p == null) { Debug.Log($"[MobilePerfOptimizer] skip '{label}' — property '{propName}' 없음"); return 0; }
            if (Mathf.Approximately(p.floatValue, value)) return 0;
            p.floatValue = value;
            Debug.Log($"[MobilePerfOptimizer] ✓ {label}");
            return 1;
        }

        private static int SetInt(SerializedObject so, string propName, int value, string label)
        {
            var p = so.FindProperty(propName);
            if (p == null) { Debug.Log($"[MobilePerfOptimizer] skip '{label}' — property '{propName}' 없음"); return 0; }
            if (p.intValue == value) return 0;
            p.intValue = value;
            Debug.Log($"[MobilePerfOptimizer] ✓ {label}");
            return 1;
        }

        /// <summary>
        /// 후보 이름 list 순서대로 시도. 처음 발견된 bool property 에 적용.
        /// URP 버전 차이로 property 이름이 바뀌는 케이스 방어.
        /// </summary>
        private static int SetBoolFallback(SerializedObject so, string[] candidates, bool value, string label)
        {
            for (int i = 0; i < candidates.Length; i++)
            {
                var p = so.FindProperty(candidates[i]);
                if (p != null && p.propertyType == SerializedPropertyType.Boolean)
                {
                    if (p.boolValue == value) return 0;
                    p.boolValue = value;
                    Debug.Log($"[MobilePerfOptimizer] ✓ {label} (matched: {candidates[i]})");
                    return 1;
                }
            }
            Debug.Log($"[MobilePerfOptimizer] skip '{label}' — 후보 이름 모두 미발견 ({string.Join(", ", candidates)})");
            return 0;
        }

        private static int SetIntFallback(SerializedObject so, string[] candidates, int value, string label)
        {
            for (int i = 0; i < candidates.Length; i++)
            {
                var p = so.FindProperty(candidates[i]);
                if (p != null && (p.propertyType == SerializedPropertyType.Integer || p.propertyType == SerializedPropertyType.Enum))
                {
                    if (p.intValue == value) return 0;
                    p.intValue = value;
                    Debug.Log($"[MobilePerfOptimizer] ✓ {label} (matched: {candidates[i]})");
                    return 1;
                }
            }
            Debug.Log($"[MobilePerfOptimizer] skip '{label}' — 후보 이름 모두 미발견 ({string.Join(", ", candidates)})");
            return 0;
        }
    }
}
