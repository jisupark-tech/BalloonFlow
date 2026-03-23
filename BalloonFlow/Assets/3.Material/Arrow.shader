Shader "Custom/Arrow"
{
    Properties
    {
        [MainColor] _BaseColor("Arrow Color", Color) = (1, 1, 1, 0.6)
        _ScrollSpeed("Scroll Speed", Float) = 1.5
        _TileCount("Tile Count (패턴 반복 수)", Float) = 20
        _ArrowSharpness("Arrow Sharpness", Range(0.1, 5)) = 2.0
    }

    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent" "RenderPipeline" = "UniversalPipeline" }

        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite Off
        Cull Off

        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float2 uv : TEXCOORD0;
            };

            CBUFFER_START(UnityPerMaterial)
                half4 _BaseColor;
                float _ScrollSpeed;
                float _TileCount;
                float _ArrowSharpness;
            CBUFFER_END

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                OUT.positionHCS = TransformObjectToHClip(IN.positionOS.xyz);
                OUT.uv = IN.uv;
                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                // UV.x = 경로를 따라 진행 (0~1), UV.y = 두께 방향 (0~1)
                float u = IN.uv.x * _TileCount - _Time.y * _ScrollSpeed;
                float v = IN.uv.y;

                // 셰브론(>) 패턴: V자 모양
                // u를 반복 (frac), v를 중심 기준으로 거리 계산
                float pattern = frac(u);
                float centerDist = abs(v - 0.5) * 2.0; // 0(중심) ~ 1(가장자리)

                // 셰브론: pattern 값이 centerDist와 비슷할 때 밝게
                float chevron = 1.0 - saturate(abs(pattern - centerDist) * _ArrowSharpness);

                // 가장자리 페이드 (두께 방향)
                float edgeFade = 1.0 - smoothstep(0.35, 0.5, centerDist);

                float alpha = chevron * edgeFade * _BaseColor.a;

                return half4(_BaseColor.rgb, alpha);
            }
            ENDHLSL
        }
    }

    FallBack "Hidden/Universal Render Pipeline/FallbackError"
}
