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
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "Queue" = "Geometry" }

        // ──────────────── Pass 0: Main Color ────────────────
        Pass
        {
            Name "MainColor"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS
            #pragma multi_compile _ _ADDITIONAL_LIGHTS
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
                float3 normalWS : TEXCOORD1;
                float3 positionWS : TEXCOORD2;
            #ifdef _NORMALMAP
                float3 tangentWS : TEXCOORD3;
                float3 bitangentWS : TEXCOORD4;
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
            CBUFFER_END

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                UNITY_SETUP_INSTANCE_ID(IN);
                UNITY_TRANSFER_INSTANCE_ID(IN, OUT);
                OUT.positionHCS = TransformObjectToHClip(IN.positionOS.xyz);
                OUT.positionWS = TransformObjectToWorld(IN.positionOS.xyz);
                OUT.normalWS = TransformObjectToWorldNormal(IN.normalOS);
                OUT.uv = TRANSFORM_TEX(IN.uv, _BaseMap);

            #ifdef _NORMALMAP
                OUT.tangentWS = TransformObjectToWorldDir(IN.tangentOS.xyz);
                OUT.bitangentWS = cross(OUT.normalWS, OUT.tangentWS) * IN.tangentOS.w;
            #endif

                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(IN);
                half4 texColor = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, IN.uv);
                half4 baseColor = texColor * _BaseColor;

                // Normal
                float3 normalWS = normalize(IN.normalWS);

            #ifdef _NORMALMAP
                half3 normalTS = UnpackNormalScale(
                    SAMPLE_TEXTURE2D(_BumpMap, sampler_BumpMap, IN.uv), _BumpScale);
                float3 T = normalize(IN.tangentWS);
                float3 B = normalize(IN.bitangentWS);
                normalWS = normalize(T * normalTS.x + B * normalTS.y + normalWS * normalTS.z);
            #endif

                Light mainLight = GetMainLight();

                // Diffuse
                half NdotL = saturate(dot(normalWS, mainLight.direction));
                half3 diffuse = baseColor.rgb * mainLight.color * NdotL;

                // Ambient
                half3 ambient = baseColor.rgb * 0.3;

                // Specular (Blinn-Phong)
                float3 viewDir = normalize(GetWorldSpaceViewDir(IN.positionWS));
                float3 halfDir = normalize(mainLight.direction + viewDir);
                half NdotH = saturate(dot(normalWS, halfDir));
                half specPower = exp2(10.0 * _Smoothness + 1.0);
                half3 specular = mainLight.color * pow(NdotH, specPower) * _Smoothness;

                // Metallic blend
                half3 finalColor = lerp(diffuse + ambient, baseColor.rgb * specular + ambient * 0.5, _Metallic);
                finalColor += specular * (1.0 - _Metallic) * 0.3;

                // Emission
            #ifdef _EMISSION
                finalColor += _EmissionColor.rgb;
            #endif

                // Blur overlay: _BlurAmount=0 원래 색, =1 완전히 BlurColor로 덮음
                finalColor = lerp(finalColor, _BlurColor.rgb, _BlurAmount);

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
            CBUFFER_END

            Varyings vertOutline(Attributes IN)
            {
                Varyings OUT;
                UNITY_SETUP_INSTANCE_ID(IN);
                UNITY_TRANSFER_INSTANCE_ID(IN, OUT);

                float width = _OutlineEnabled > 0.5 ? _OutlineWidth : 0.0;
                float3 expanded = IN.positionOS.xyz + IN.normalOS * width;
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

        // ──────────────── Pass 2: Depth Only ────────────────
        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }

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
