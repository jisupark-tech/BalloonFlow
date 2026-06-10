Shader "Custom/ItemShared"
{
    Properties
    {
        [MainColor] _BaseColor("Base Color", Color) = (1, 1, 1, 1)
        [MainTexture] _BaseMap("Base Map", 2D) = "white" {}

        [Header(Surface)]
        _Metallic("Metallic", Range(0, 1)) = 0
        _Smoothness("Smoothness", Range(0, 1)) = 0.15

        [Header(Normal Map)]
        [Toggle(_NORMALMAP)] _UseNormalMap("Use Normal Map", Float) = 0
        [NoScaleOffset] _BumpMap("Normal Map", 2D) = "bump" {}
        _BumpScale("Normal Strength", Range(0, 2)) = 1

        [Header(Emission)]
        [Toggle(_EMISSION)] _UseEmission("Use Emission", Float) = 0
        [HDR] _EmissionColor("Emission Color", Color) = (0, 0, 0, 1)

        [Header(Blur Overlay)]
        _BlurAmount("Blur Amount", Range(0, 1)) = 0
        _BlurColor("Blur Color", Color) = (1, 1, 1, 1)

        [Header(Outline)]
        [Toggle] _OutlineEnabled("Outline Enabled", Float) = 0
        _OutlineColor("Outline Color", Color) = (0, 0, 0, 1)
        _OutlineWidth("Outline Width", Range(0.0001, 0.01)) = 0.002

        [Header(Shadow Tint)]
        // [Shadow Tint 2026-06-10] 음영면 진하기 — baseColor² 에 곱해지는 계수. 0 = 검정, 클수록 밝고 찐한 자기 색.
        _ShadowTintStrength("Shadow Tint Strength", Range(0, 1.5)) = 0.6
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "Queue" = "Geometry" }

        // ──────────────── Pass 0: Main Color ────────────────
        Pass
        {
            Name "MainColor"
            Tags { "LightMode" = "UniversalForward" }

            // ROLLBACK_OUTLINE_GROUP_SILHOUETTE_20260610: 바디 픽셀에 stencil=1 마킹.
            //   OutlineHull(Queue+10, Comp NotEqual)이 바디 위에는 안 그려져 인접 풍선 사이 내부 외곽선이 제거되고
            //   그룹 실루엣만 남음. 상세/롤백은 OutlineHull.shader 주석 참조.
            Stencil
            {
                Ref 1
                Comp Always
                Pass Replace
            }

            Cull Back   // 명시: backface culling — 카메라 반대면 픽셀 skip (default 와 동일, 명확성 위한 명시)
            ZWrite On

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            // [Optimization 2026-05-11] 사용 안 하는 multi_compile 제거 → variant 수 절감.
            //   _MAIN_LIGHT_SHADOWS: frag 가 GetMainLight() shadow-less overload 만 사용 + URP Cast Shadows Distance=0 → 영향 없음.
            //   _ADDITIONAL_LIGHTS: URP Additional Lights=Disabled + frag 에 additional light 코드 없음.
            // 롤백: 아래 두 라인 주석 해제.
            // #pragma multi_compile _ _MAIN_LIGHT_SHADOWS
            // #pragma multi_compile _ _ADDITIONAL_LIGHTS
            #pragma shader_feature_local _NORMALMAP
            #pragma shader_feature_local _EMISSION

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float4 tangentOS : TANGENT;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            // [Optimization 2026-05-11] positionWS 제거 — Specular 제거 후 frag 에서 사용 안 함. interpolator 1 slot + vertex 계산 절감.
            // half 정밀도 적용 (normalWS / tangentWS / bitangentWS) — mobile FP16 가 FP32 대비 1.5~2× 빠름. 시각 동등.
            // 롤백: positionWS line 추가 + half3 → float3 복원.
            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                half3 normalWS : TEXCOORD1;
                // 원본: float3 positionWS : TEXCOORD2; (Specular 제거 후 미사용)
            #ifdef _NORMALMAP
                half3 tangentWS : TEXCOORD2;
                half3 bitangentWS : TEXCOORD3;
            #endif
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            TEXTURE2D(_BaseMap);
            SAMPLER(sampler_BaseMap);

            #ifdef _NORMALMAP
            TEXTURE2D(_BumpMap);
            SAMPLER(sampler_BumpMap);
            #endif

            CBUFFER_START(UnityPerMaterial)
                half4 _BaseColor;
                float4 _BaseMap_ST;
                half _Metallic;
                half _Smoothness;
                half _BumpScale;
                half4 _EmissionColor;
                half _BlurAmount;
                half4 _BlurColor;
                half _OutlineEnabled;
                half4 _OutlineColor;
                half _OutlineWidth;
                half _ShadowTintStrength;
            CBUFFER_END

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                UNITY_SETUP_INSTANCE_ID(IN);
                UNITY_TRANSFER_INSTANCE_ID(IN, OUT);
                OUT.positionHCS = TransformObjectToHClip(IN.positionOS.xyz);
                // [Optimization 2026-05-11] positionWS 계산 제거 (미사용). 원본: OUT.positionWS = TransformObjectToWorld(IN.positionOS.xyz);
                OUT.normalWS = (half3)TransformObjectToWorldNormal(IN.normalOS);
                OUT.uv = TRANSFORM_TEX(IN.uv, _BaseMap);

            #ifdef _NORMALMAP
                OUT.tangentWS = (half3)TransformObjectToWorldDir(IN.tangentOS.xyz);
                OUT.bitangentWS = (half3)cross((float3)OUT.normalWS, (float3)OUT.tangentWS) * IN.tangentOS.w;
            #endif

                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(IN);
                half4 texColor = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, IN.uv);
                half4 baseColor = texColor * _BaseColor;

                // [Optimization 2026-05-11] half 정밀도 — mobile FP16 더 빠름. 원본: float3 normalWS.
                half3 normalWS = normalize(IN.normalWS);

            #ifdef _NORMALMAP
                half3 normalTS = UnpackNormalScale(
                    SAMPLE_TEXTURE2D(_BumpMap, sampler_BumpMap, IN.uv), _BumpScale);
                // 원본: float3 T / float3 B
                half3 T = normalize(IN.tangentWS);
                half3 B = normalize(IN.bitangentWS);
                normalWS = normalize(T * normalTS.x + B * normalTS.y + normalWS * normalTS.z);
            #endif

                Light mainLight = GetMainLight();

                half NdotL = saturate(dot(normalWS, (half3)mainLight.direction));
                // [Shadow Tint 2026-06-10] 음영면이 회색빛(baseColor*0.3 만 잔존)으로 칙칙 → 자기 색 multiply 그림자로 교체 (아트 요구).
                //   shadowColor = baseColor² × _ShadowTintStrength — multiply 블렌드라 어두워지며 채도가 올라가 자기 색으로 "찐하게" 보임.
                //   litColor = 기존 NdotL=1 결과(diffuse+ambient)와 동일값 → 밝은 면 시각 변화 없음, 어두운 끝점만 교체.
                //   롤백: 아래 2줄을 원본으로 복원 + finalColor 의 원본 lerp 복원.
                // 원본: half3 diffuse = baseColor.rgb * (half3)mainLight.color * NdotL;
                //       half3 ambient = baseColor.rgb * 0.3;
                half3 litColor    = baseColor.rgb * ((half3)mainLight.color + 0.3);
                half3 shadowColor = baseColor.rgb * baseColor.rgb * _ShadowTintStrength;

                // 사용자 요구로 Specular 제거 — 모바일 pow + exp2 가 fragment shader 의 가장 큰 부하.
                // Smoothness 0.15 default 에선 시각 거의 동일.
                // [LEGACY: Blinn-Phong Specular]
                // float3 viewDir = normalize(GetWorldSpaceViewDir(IN.positionWS));
                // float3 halfDir = normalize(mainLight.direction + viewDir);
                // half NdotH = saturate(dot(normalWS, halfDir));
                // half specPower = exp2(10.0 * _Smoothness + 1.0);
                // half3 specular = mainLight.color * pow(NdotH, specPower) * _Smoothness;

                // Metallic blend — specular 없는 단순 형태. _Metallic 0 이면 shadowColor↔litColor 음영 램프.
                // 원본: half3 finalColor = lerp(diffuse + ambient, baseColor.rgb + ambient * 0.5, _Metallic);  (metallic 항 = baseColor*1.15 와 동일)
                half3 finalColor = lerp(lerp(shadowColor, litColor, NdotL), baseColor.rgb * 1.15, _Metallic);
                // [LEGACY] finalColor += specular * (1.0 - _Metallic) * 0.3;

                // Emission
            #ifdef _EMISSION
                finalColor += _EmissionColor.rgb;
            #endif

                // Blur overlay: 비활성 (흰색 아웃라인만 사용)
                // finalColor = lerp(finalColor, _BlurColor.rgb, _BlurAmount);

                return half4(finalColor, baseColor.a);
            }
            ENDHLSL
        }

        // [Optimization 2026-05-10] Outline Pass 통째로 주석 처리 — 풍선 1500개 × Outline Pass = 1500 draws/frame 제거.
        // 시각: 풍선 외곽선 사라짐. Mobile 디바이스에서 큰 stage 시 frame drop 회복 위해 outline 자체 OFF.
        // 롤백: 아래 /* */ 블록 주석 해제. 외곽만 outline 원하면 vertex shader 의 _OutlineEnabled<0.5 분기도 함께 해제.
        /*
        // ──────────────── Pass 1: Outline (Inverted Hull) ────────────────
        Pass
        {
            Name "Outline"
            Tags { "LightMode" = "SRPDefaultUnlit" }

            Cull Front
            ZWrite On

            HLSLPROGRAM
            #pragma vertex vertOutline
            #pragma fragment fragOutline
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            CBUFFER_START(UnityPerMaterial)
                half4 _BaseColor;
                float4 _BaseMap_ST;
                half _Metallic;
                half _Smoothness;
                half _BumpScale;
                half4 _EmissionColor;
                half _BlurAmount;
                half4 _BlurColor;
                half _OutlineEnabled;
                half4 _OutlineColor;
                half _OutlineWidth;
                half _ShadowTintStrength;
            CBUFFER_END

            Varyings vertOutline(Attributes IN)
            {
                Varyings OUT;
                UNITY_SETUP_INSTANCE_ID(IN);
                UNITY_TRANSFER_INSTANCE_ID(IN, OUT);

                if (_OutlineEnabled < 0.5)
                {
                    OUT.positionHCS = float4(2, 2, 2, 1);
                    return OUT;
                }

                float3 expanded = IN.positionOS.xyz + IN.normalOS * _OutlineWidth;
                OUT.positionHCS = TransformObjectToHClip(expanded);
                return OUT;
            }

            half4 fragOutline(Varyings IN) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(IN);
                return _OutlineColor;
            }
            ENDHLSL
        }
        */

        // ──────────────── Pass 2: Depth Only ────────────────
        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }

            Cull Back   // 명시: backface culling
            ZWrite On
            ColorMask 0

            HLSLPROGRAM
            #pragma vertex vertDepth
            #pragma fragment fragDepth
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
            };

            Varyings vertDepth(Attributes IN)
            {
                Varyings OUT;
                UNITY_SETUP_INSTANCE_ID(IN);
                OUT.positionHCS = TransformObjectToHClip(IN.positionOS.xyz);
                return OUT;
            }

            half4 fragDepth(Varyings IN) : SV_Target
            {
                return 0;
            }
            ENDHLSL
        }
    }

    FallBack "Hidden/Universal Render Pipeline/FallbackError"
}
