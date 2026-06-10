// ROLLBACK_ITEMSHAREDOUTLINE_20260609 (Option A):
//   ItemShared 의 multi-pass(Outline 포함) 버전 — "외곽선 대상 소수"(외곽 풍선 / 보관함 front-row) 전용.
//   내부 1500 풍선은 single-pass Custom/ItemShared 유지 → SRP Batcher 로 묶임.
//   이 셰이더는 multi-pass 라 SRP Batcher 가 노드마다 끊지만, 쓰는 오브젝트가 소수(~수십)라 영향 작음.
//   프로퍼티 레이아웃은 ItemShared 와 동일(+_OutlineEnabled default 1) → 머티리얼 복제(new Material(src){shader=this}) 호환.
//   롤백: 이 파일 삭제 + BalloonController/HolderIdentifier 의 material swap 코드 원복.
Shader "Custom/ItemSharedOutline"
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
        [Toggle] _OutlineEnabled("Outline Enabled", Float) = 1   // outlined 전용이라 기본 ON
        _OutlineColor("Outline Color", Color) = (0, 0, 0, 1)
        _OutlineWidth("Outline Width", Range(0.0001, 0.01)) = 0.002

        [Header(Shadow Tint)]
        // [Shadow Tint 2026-06-10] ItemShared 와 동일 — 음영면 진하기 (baseColor² 계수).
        _ShadowTintStrength("Shadow Tint Strength", Range(0, 1.5)) = 0.6
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "Queue" = "Geometry" }

        // ──────────────── Pass 0: Main Color (ItemShared 와 동일) ────────────────
        Pass
        {
            Name "MainColor"
            Tags { "LightMode" = "UniversalForward" }

            Cull Back
            ZWrite On

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
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

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                half3 normalWS : TEXCOORD1;
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

                half3 normalWS = normalize(IN.normalWS);

            #ifdef _NORMALMAP
                half3 normalTS = UnpackNormalScale(
                    SAMPLE_TEXTURE2D(_BumpMap, sampler_BumpMap, IN.uv), _BumpScale);
                half3 T = normalize(IN.tangentWS);
                half3 B = normalize(IN.bitangentWS);
                normalWS = normalize(T * normalTS.x + B * normalTS.y + normalWS * normalTS.z);
            #endif

                Light mainLight = GetMainLight();
                half NdotL = saturate(dot(normalWS, (half3)mainLight.direction));
                // [Shadow Tint 2026-06-10] ItemShared 와 동일 — 음영면을 자기 색 multiply(baseColor²) 그림자로. 두 셰이더 항상 동기 유지.
                // 원본: half3 diffuse = baseColor.rgb * (half3)mainLight.color * NdotL;
                //       half3 ambient = baseColor.rgb * 0.3;
                //       half3 finalColor = lerp(diffuse + ambient, baseColor.rgb + ambient * 0.5, _Metallic);
                half3 litColor    = baseColor.rgb * ((half3)mainLight.color + 0.3);
                half3 shadowColor = baseColor.rgb * baseColor.rgb * _ShadowTintStrength;
                half3 finalColor = lerp(lerp(shadowColor, litColor, NdotL), baseColor.rgb * 1.15, _Metallic);

            #ifdef _EMISSION
                finalColor += _EmissionColor.rgb;
            #endif

                return half4(finalColor, baseColor.a);
            }
            ENDHLSL
        }

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
                    OUT.positionHCS = float4(2, 2, 2, 1); // degenerate clip
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

        // ──────────────── Pass 2: Depth Only (ItemShared 와 동일) ────────────────
        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }

            Cull Back
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
