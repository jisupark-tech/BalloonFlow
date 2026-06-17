// ROLLBACK_BOARDTILE_OPAQUE_20260617:
//   SpriteSRPBatcher(반투명, Blend/ZWrite Off)의 '불투명 AlphaTest' 변형. 보드 바닥/컨베이어/danger 타일처럼
//   '솔리드(꽉 찬) 스프라이트가 화면을 크게 덮고 서로 안 겹치는' 경우 전용. Transparent blend 를 제거해
//   ① 픽셀 블렌드(프레임버퍼 read-modify-write) 비용 제거 ② ZWrite On 으로 early-Z → 타일 뒤 배경/하위
//   타일이 아예 셰이딩 안 됨 → fill(overdraw) 대폭 절감. (Scene Fill Audit: 보드 타일 2D/Sprite-Unlit-Default
//   가 fillAreaSum 549.6 = 전체 fill 7할 = 1위였음.)
//   edge 는 clip(_Cutoff) 로 처리(부드러운 AA 대신 hard edge — 바닥 타일엔 무영향). 겹치는 반투명 타일엔 부적합.
//   롤백: 이 셰이더 + BoardTileManager 의 GetOpaqueTileMat/UseOpaqueBoardTiles 분기 제거.
Shader "Custom/SpriteOpaqueCutout"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite Texture", 2D) = "white" {}
        _Color ("Tint", Color) = (1, 1, 1, 1)
        _Cutoff ("Alpha Cutoff", Range(0, 1)) = 0.5
    }

    SubShader
    {
        Tags
        {
            "Queue"            = "AlphaTest"
            "IgnoreProjector"  = "True"
            "RenderType"       = "TransparentCutout"
            "PreviewType"      = "Plane"
            "CanUseSpriteAtlas"= "True"
            "RenderPipeline"   = "UniversalPipeline"
        }

        Cull Off
        Lighting Off
        ZWrite On          // early-Z 핵심
        // Blend 없음 — 불투명 덮어쓰기

        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float3 positionOS : POSITION;
                float4 color      : COLOR;
                float2 uv         : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float4 color      : COLOR;
                float2 uv         : TEXCOORD0;
            };

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);

            CBUFFER_START(UnityPerMaterial)
                half4 _Color;
                half _Cutoff;
            CBUFFER_END

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                OUT.positionCS = TransformObjectToHClip(IN.positionOS);
                OUT.color      = IN.color * _Color;
                OUT.uv         = IN.uv;
                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                half4 c = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, IN.uv) * IN.color;
                clip(c.a - _Cutoff);   // alpha edge 컷 → 솔리드 본체만 불투명 렌더
                return half4(c.rgb, 1);
            }
            ENDHLSL
        }
    }

    FallBack "Sprites/Default"
}
