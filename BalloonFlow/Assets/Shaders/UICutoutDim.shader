// UI Dim overlay shader with a direct rectangular cutout.
// A single full-screen Image uses this material; code updates the cutout in overlay-local space.
// [2026-05-21] _CutoutMaskTex 추가 — texture 의 alpha 가 hole 의 모양을 결정. 기본 white 면 사각형 hole.
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
        _CutoutMaskTex ("Cutout Mask (alpha = hole)", 2D) = "white" {}
        _CutoutMaskUVRect ("Cutout Mask UV Rect in atlas (xMin,yMin,w,h 0..1)", Vector) = (0,0,1,1)
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
            sampler2D _CutoutMaskTex;
            float4 _CutoutMaskUVRect;
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
                // rect 안쪽(inside) 정도 — 1=완전 inside rect, 0=완전 outside.
                float rectInsideness = smoothstep(softness, -softness, outside);

                // [2026-05-21] _CutoutMaskTex 의 alpha 가 hole 모양 결정. white default (alpha=1) 이면
                //   현재의 사각형 hole 동작 그대로. 알파가 0~1 이면 그만큼만 hole.
                //   maskUV = cutout rect 내 local 위치를 (0..1) 로 매핑.
                //   _CutoutMaskUVRect 로 atlas 안의 sprite sub-rect 까지 지원 (atlased sprite 호환).
                float2 cutoutSizeSafe = max(_CutoutSize.xy, float2(0.001, 0.001));
                float2 maskUV = (local01 - (_CutoutCenter.xy - cutoutSizeSafe * 0.5)) / cutoutSizeSafe;
                float maskAlpha = 0.0;
                if (maskUV.x >= 0.0 && maskUV.x <= 1.0 && maskUV.y >= 0.0 && maskUV.y <= 1.0)
                {
                    float2 atlasUV = _CutoutMaskUVRect.xy + maskUV * _CutoutMaskUVRect.zw;
                    maskAlpha = tex2D(_CutoutMaskTex, atlasUV).a;
                }

                float holeStrength = rectInsideness * maskAlpha;
                float dimAlpha = 1.0 - holeStrength;

                fixed4 tex = tex2D(_MainTex, i.uv);
                return fixed4(_Color.rgb, _Color.a * dimAlpha * tex.a * i.color.a);
            }
            ENDCG
        }
    }
}
