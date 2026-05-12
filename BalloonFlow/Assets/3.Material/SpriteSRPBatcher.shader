// SRP Batcher 호환 Sprite shader. Sprites/Default 의 기능 + CBUFFER UnityPerMaterial 추가.
// SpriteRenderer 의 sprite.texture 가 [PerRendererData] _MainTex 에 자동 binding.
// 같은 shared mat 인스턴스로 여러 SpriteRenderer 사용 시 SRP Batcher 가 batch.
Shader "Custom/SpriteSRPBatcher"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite Texture", 2D) = "white" {}
        _Color ("Tint", Color) = (1, 1, 1, 1)
    }

    SubShader
    {
        Tags
        {
            "Queue"            = "Transparent"
            "IgnoreProjector"  = "True"
            "RenderType"       = "Transparent"
            "PreviewType"      = "Plane"
            "CanUseSpriteAtlas"= "True"
            "RenderPipeline"   = "UniversalPipeline"
        }

        Cull Off
        Lighting Off
        ZWrite Off
        Blend One OneMinusSrcAlpha

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
                c.rgb *= c.a;
                return c;
            }
            ENDHLSL
        }
    }

    FallBack "Sprites/Default"
}
