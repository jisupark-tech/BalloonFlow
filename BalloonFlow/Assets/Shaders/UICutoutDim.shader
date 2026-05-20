// UI Dim overlay shader with a direct rectangular cutout.
// A single full-screen Image uses this material; code updates the cutout in overlay-local space.
Shader "UI/CutoutDim"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite Texture", 2D) = "white" {}
        _Color ("Tint", Color) = (0,0,0,0.7)
        _OverlayRect ("Overlay Rect", Vector) = (0,0,1,1)
        _CutoutCenter ("Cutout Center (Normalized Local)", Vector) = (0.5,0.5,0,0)
        _CutoutSize ("Cutout Size (Normalized Local)", Vector) = (0,0,0,0)
        _CutoutSoftness ("Cutout Softness (Normalized Local)", Float) = 0.001
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
            "Queue" = "Transparent"
            "RenderType" = "Transparent"
            "IgnoreProjector" = "True"
        }

        ZWrite Off
        ZTest Always
        Blend SrcAlpha OneMinusSrcAlpha

        Stencil
        {
            Ref [_Stencil]
            Comp [_StencilComp]
            Pass [_StencilOp]
            ReadMask [_StencilReadMask]
            WriteMask [_StencilWriteMask]
        }
        ColorMask [_ColorMask]

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            sampler2D _MainTex;
            fixed4 _Color;
            float4 _OverlayRect;
            float4 _CutoutCenter;
            float4 _CutoutSize;
            float _CutoutSoftness;

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
                fixed4 color : COLOR;
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float2 uv : TEXCOORD0;
                float2 localPos : TEXCOORD1;
                fixed4 color : COLOR;
            };

            v2f vert(appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                o.localPos = v.vertex.xy;
                o.color = v.color;
                return o;
            }

            half4 frag(v2f i) : SV_Target
            {
                // ROLLBACK_USEITEM_LOCAL_CUTOUT_SHADER:
                // UI Image UVs are unreliable for a cutout when the Overlay Image is Sliced.
                // Use RectTransform-local vertex coordinates so the hole follows CutoutMask
                // exactly in canvas space while CutoutMask itself stays only a rect marker.
                float2 overlaySize = max(_OverlayRect.zw, float2(1.0, 1.0));
                float2 local01 = (i.localPos - _OverlayRect.xy) / overlaySize;
                float2 halfSize = max(_CutoutSize.xy * 0.5, 0.0);
                float2 rectDelta = abs(local01 - _CutoutCenter.xy) - halfSize;
                float outside = max(rectDelta.x, rectDelta.y);
                float softness = max(_CutoutSoftness, 0.0001);
                float dimAlpha = smoothstep(-softness, softness, outside);

                fixed4 tex = tex2D(_MainTex, i.uv);
                return fixed4(_Color.rgb, _Color.a * dimAlpha * tex.a * i.color.a);
            }
            ENDCG
        }
    }
}
