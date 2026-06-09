// ROLLBACK_OUTLINEHULL_20260609 (Option A 개선):
//   단일 패스 inverted-hull 외곽선 전용 셰이더. body 렌더러의 material[1] 로 추가해 사용(공유 1개 머티리얼).
//   single-pass 라 SRP Batcher 가 안 끊고, 모든 외곽선이 같은 머티리얼+같은 mesh → 한 배치로 묶임.
//   (multi-pass 통짜 셰이더/MPB 와 달리 body 배칭 무손상, shadow 등 다른 렌더러는 안 건드림.)
//   롤백: 이 파일 + GetOutlineHullMaterial / ApplyOutlineToBalloon(hull) / HolderIdentifier hull 코드 제거.
Shader "Custom/OutlineHull"
{
    Properties
    {
        _OutlineColor("Outline Color", Color) = (0, 0, 0, 1)
        _OutlineWidth("Outline Width", Range(0.0, 0.05)) = 0.0005
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "Queue" = "Geometry" }

        Pass
        {
            Name "OutlineHull"
            Tags { "LightMode" = "UniversalForward" }

            Cull Front      // 뒷면만 — 확장된 헐의 바깥 테두리만 보임
            ZWrite On

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
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
                half4 _OutlineColor;
                half _OutlineWidth;
            CBUFFER_END

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                UNITY_SETUP_INSTANCE_ID(IN);
                UNITY_TRANSFER_INSTANCE_ID(IN, OUT);
                float3 expanded = IN.positionOS.xyz + normalize(IN.normalOS) * _OutlineWidth;
                OUT.positionHCS = TransformObjectToHClip(expanded);
                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(IN);
                return _OutlineColor;
            }
            ENDHLSL
        }
    }

    FallBack Off
}
