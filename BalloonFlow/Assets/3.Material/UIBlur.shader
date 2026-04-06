Shader "BalloonFlow/UIBlur"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite Texture", 2D) = "white" {}
        _Color ("Tint", Color) = (1,1,1,1)
        _BlurSize ("Blur Size", Range(0, 10)) = 3.0

        _StencilComp ("Stencil Comparison", Float) = 8
        _Stencil ("Stencil ID", Float) = 0
        _StencilOp ("Stencil Operation", Float) = 0
        _StencilWriteMask ("Stencil Write Mask", Float) = 255
        _StencilReadMask ("Stencil Read Mask", Float) = 255
        _ColorMask ("Color Mask", Float) = 15
    }

    SubShader
    {
        Tags
        {
            "Queue"="Transparent"
            "IgnoreProjector"="True"
            "RenderType"="Transparent"
            "PreviewType"="Plane"
            "CanUseSpriteAtlas"="True"
        }

        Stencil
        {
            Ref [_Stencil]
            Comp [_StencilComp]
            Pass [_StencilOp]
            ReadMask [_StencilReadMask]
            WriteMask [_StencilWriteMask]
        }

        Cull Off
        Lighting Off
        ZWrite Off
        ZTest [unity_GUIZTestMode]
        Blend SrcAlpha OneMinusSrcAlpha
        ColorMask [_ColorMask]

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            #include "UnityUI.cginc"

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
                float4 color : COLOR;
            };

            struct v2f
            {
                float4 vertex : SV_POSITION;
                float2 uv : TEXCOORD0;
                float4 color : COLOR;
                float4 worldPosition : TEXCOORD1;
            };

            sampler2D _MainTex;
            float4 _MainTex_TexelSize;
            fixed4 _Color;
            float _BlurSize;
            float4 _ClipRect;

            v2f vert(appdata v)
            {
                v2f o;
                o.worldPosition = v.vertex;
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                o.color = v.color * _Color;
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                float2 texelSize = _MainTex_TexelSize.xy * _BlurSize;

                // 9-tap Gaussian blur
                fixed4 col = fixed4(0, 0, 0, 0);
                col += tex2D(_MainTex, i.uv + float2(-texelSize.x, -texelSize.y)) * 0.0625;
                col += tex2D(_MainTex, i.uv + float2(0, -texelSize.y))            * 0.125;
                col += tex2D(_MainTex, i.uv + float2(texelSize.x, -texelSize.y))  * 0.0625;
                col += tex2D(_MainTex, i.uv + float2(-texelSize.x, 0))            * 0.125;
                col += tex2D(_MainTex, i.uv)                                       * 0.25;
                col += tex2D(_MainTex, i.uv + float2(texelSize.x, 0))             * 0.125;
                col += tex2D(_MainTex, i.uv + float2(-texelSize.x, texelSize.y))  * 0.0625;
                col += tex2D(_MainTex, i.uv + float2(0, texelSize.y))             * 0.125;
                col += tex2D(_MainTex, i.uv + float2(texelSize.x, texelSize.y))   * 0.0625;

                col *= i.color;
                col.a *= UnityGet2DClipping(i.worldPosition.xy, _ClipRect);
                return col;
            }
            ENDCG
        }
    }
}
