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
        // ROLLBACK_OUTLINE_GROUP_SILHOUETTE_20260610: Queue Geometry+10 — 모든 바디(2000)를 먼저 그린 뒤 hull.
        //   바디가 깔아둔 stencil=1 마스크가 전체 유니온에 대해 성립해야 그룹 실루엣이 나옴.
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "Queue" = "Geometry+10" }

        Pass
        {
            Name "OutlineHull"
            Tags { "LightMode" = "UniversalForward" }

            // ROLLBACK_OUTLINE_GROUP_SILHOUETTE_20260610: 그룹(유니온) 실루엣 아웃라인.
            //   바디(ItemShared/ItemSharedOutline)가 stencil=1 을 깔고, hull 은 stencil!=1(=바디가 안 보이는 픽셀)에만 그림.
            //   → 인접 풍선 사이 내부 외곽선 제거, 덩어리 바깥 실루엣(+구멍 둘레)만 남음.
            //   stencil 은 depth/stencil 하드웨어 상태라 추가 비용 ~0. 패스 수/배칭/RT 변화 없음.
            //   롤백: 이 Stencil 블록 + Queue+10 + 바디 셰이더의 Stencil 블록 + GetOutlineHullMaterial 의 renderQueue 라인 제거.
            Stencil
            {
                Ref 1
                Comp NotEqual
            }

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
