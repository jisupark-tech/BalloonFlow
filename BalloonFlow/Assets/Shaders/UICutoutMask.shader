// UI Cutout(역마스크) 셰이더.
// 이 셰이더가 적용된 Image 영역은 스텐실 버퍼에 기록만 하고 화면에 안 그림.
// Dim 오버레이는 UICutoutDim 셰이더로 이 영역을 제외하고 그림.
//
// [2026-05-15] sprite alpha 기반 cutout — Image 에 할당된 sprite 의 alpha 가 있는 영역만 stencil 기록.
// stencil 자체가 binary 라 매칭 위해 alpha 0.5 임계로 binary 판정.
// _MainTex 가 흰색(기본 4x4) sprite 면 alpha=1 → 전체 RectTransform 사각형 stencil (기존 사각형 동작 유지).
Shader "UI/CutoutMask"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite Texture", 2D) = "white" {}
        _Color ("Tint", Color) = (1,1,1,1)
    }
    SubShader
    {
        Tags
        {
            "Queue" = "Transparent-1"
            "RenderType" = "Transparent"
        }

        // 화면에 안 그림 (스텐실만 기록)
        ColorMask 0
        ZWrite Off
        ZTest Always

        Stencil
        {
            Ref 1
            Comp Always
            Pass Replace
        }

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            sampler2D _MainTex;

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
            };
            struct v2f
            {
                float4 pos : SV_POSITION;
                float2 uv : TEXCOORD0;
            };

            v2f vert(appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                return o;
            }

            half4 frag(v2f i) : SV_Target
            {
                // sprite alpha 0.5 미만 픽셀은 stencil 기록 안 함 → sprite 불투명 영역만 hole.
                clip(tex2D(_MainTex, i.uv).a - 0.5);
                return 0;
            }
            ENDCG
        }
    }
}
