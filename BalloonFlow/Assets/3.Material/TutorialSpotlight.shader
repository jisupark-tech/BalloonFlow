Shader "UI/TutorialSpotlight"
{
    Properties
    {
        _Color ("Dim Color", Color) = (0, 0, 0, 0.75)
        _Center ("Spotlight Center (UV)", Vector) = (0.5, 0.5, 0, 0)
        _Radius ("Spotlight Radius (UV)", Vector) = (0.15, 0.15, 0, 0)
        _Softness ("Edge Softness", Range(0.01, 0.5)) = 0.08
        [HideInInspector] _MainTex ("Texture", 2D) = "white" {}
    }

    SubShader
    {
        Tags { "Queue" = "Overlay" "RenderType" = "Transparent" "RenderPipeline" = "UniversalPipeline" }

        Pass
        {
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
                float4 color : COLOR;
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float4 color : COLOR;
            };

            CBUFFER_START(UnityPerMaterial)
                half4 _Color;
                float4 _Center;
                float4 _Radius;
                float _Softness;
            CBUFFER_END

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                OUT.positionHCS = TransformObjectToHClip(IN.positionOS.xyz);
                OUT.uv = IN.uv;
                OUT.color = IN.color;
                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                // UV 기준 타원형 거리 계산
                float2 diff = (IN.uv - _Center.xy) / max(_Radius.xy, 0.001);
                float dist = length(diff);

                // 부드러운 경계: 1.0에서 투명, 1.0+softness에서 불투명
                float alpha = smoothstep(1.0 - _Softness, 1.0 + _Softness, dist);

                return half4(_Color.rgb, _Color.a * alpha * IN.color.a);
            }
            ENDHLSL
        }
    }
}
