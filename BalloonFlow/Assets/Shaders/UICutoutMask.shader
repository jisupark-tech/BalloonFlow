// UI Cutout(역마스크) 셰이더.
// 이 셰이더가 적용된 Image 영역은 스텐실 버퍼에 기록만 하고 화면에 안 그림.
// Dim 오버레이는 UICutoutDim 셰이더로 이 영역을 제외하고 그림.
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

            struct appdata { float4 vertex : POSITION; };
            struct v2f { float4 pos : SV_POSITION; };

            v2f vert(appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                return o;
            }

            half4 frag(v2f i) : SV_Target
            {
                return 0;
            }
            ENDCG
        }
    }
}
