// ─────────────────────────────────────────────────────────────────────────────
// [WATER_SHADER 2026-07-22] BalloonFlow 스타일라이즈드 워터 (URP, 모바일 최적화 우선)
//   레퍼런스: "FREE Stylized Water Shader URP (Unity 6 Tutorial)" (youtu.be/qvbTDK-u7IY) 의
//   구성요소(깊이 그라데이션·엣지 폼·표면 스파클·버텍스 웨이브)를 수제 HLSL 로 재구성.
//
//   ── 최적화 결정 사항 (타겟: Exynos 9820/SD855, min 50fps) ──
//   · 단일 패스 Unlit — 라이팅/섀도 계산 0, 멀티패스 없음
//   · half 정밀도 전면 사용(모바일 GPU 레지스터/대역폭 절감)
//   · 텍스처 페치 2회(같은 노이즈 텍스처 2방향 스크롤) — GrabPass/반사/굴절/씬컬러 없음
//   · SRP Batcher 호환(UnityPerMaterial CBUFFER) — 머티리얼 다수여도 세트패스 1회
//   · 깊이 효과(그라데이션+쇼어폼)는 shader_feature 로 완전 스트립 가능 —
//     URP Asset 의 Depth Texture 미사용 프로젝트에선 토글 OFF 시 의존성/비용 0
//   · 버텍스 웨이브 = sin 2개 합성(게르스트너 아님). _WaveAmp=0 이면 사실상 무비용
//   · 포그/노멀맵/스페큘러 없음 — 스타일라이즈드 톤은 색 2단 + 스파클 + 폼으로 충분
//
//   ── 사용법 ──
//   Tools > BalloonFlow > Setup Stylized Water 로 노이즈 텍스처+머티리얼 자동 생성 후
//   평면 메시에 적용. 버텍스 웨이브를 쓰려면 서브디비전 있는 평면 필요(기본 Unity Plane 10x10 OK).
//   UV 는 월드 XZ 기반 — 메시 UV 무관하게 균일 타일링(플레인 스케일 자유).
// ─────────────────────────────────────────────────────────────────────────────
Shader "BalloonFlow/StylizedWater"
{
    Properties
    {
        [Header(Colors)]
        _ShallowColor ("Shallow Color", Color) = (0.31, 0.76, 0.91, 0.80)
        _DeepColor    ("Deep Color",    Color) = (0.09, 0.40, 0.65, 0.95)
        _FoamColor    ("Foam Color",    Color) = (1, 1, 1, 1)

        [Header(Depth Effects)]
        [Toggle(_DEPTH_EFFECTS)] _DepthEffects ("Depth Effects (needs Depth Texture)", Float) = 1
        _DepthRange   ("Shallow to Deep Range", Range(0.05, 10)) = 1.5
        _FoamDistance ("Shore Foam Distance",   Range(0.01, 3))  = 0.45

        [Header(Surface)]
        _NoiseTex        ("Noise (tileable gray)", 2D) = "gray" {}
        _NoiseScale      ("Noise World Scale",  Range(0.02, 4)) = 0.35
        _NoiseSpeed      ("Scroll Speed (dir1 xy, dir2 zw)", Vector) = (0.03, 0.02, -0.02, 0.035)
        _SparkleThreshold("Sparkle Threshold", Range(0.1, 0.95)) = 0.62
        _SparkleStrength ("Sparkle Strength",  Range(0, 2)) = 0.6

        [Header(Vertex Waves)]
        _WaveAmp   ("Wave Amplitude", Range(0, 0.5)) = 0.06
        _WaveFreq  ("Wave Frequency", Range(0.1, 8)) = 1.6
        _WaveSpeed ("Wave Speed",     Range(0, 6))   = 1.2
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Transparent"
            "Queue" = "Transparent"
            "RenderPipeline" = "UniversalPipeline"
        }

        Pass
        {
            Name "StylizedWaterUnlit"
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Back

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            // 깊이 효과 토글 — OFF 빌드에선 depth 샘플/스크린UV 계산이 통째로 스트립됨.
            #pragma shader_feature_local _DEPTH_EFFECTS
            // 모바일 전용 타겟 최소화 — 불필요 배리언트 없음.
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
#if defined(_DEPTH_EFFECTS)
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
#endif

            TEXTURE2D(_NoiseTex);
            SAMPLER(sampler_NoiseTex);

            // SRP Batcher 호환 — 머티리얼 프로퍼티 전부 UnityPerMaterial 에.
            CBUFFER_START(UnityPerMaterial)
                half4  _ShallowColor;
                half4  _DeepColor;
                half4  _FoamColor;
                float  _DepthRange;
                float  _FoamDistance;
                float4 _NoiseTex_ST;      // 관례상 유지(월드 UV 라 미사용)
                float  _NoiseScale;
                float4 _NoiseSpeed;
                half   _SparkleThreshold;
                half   _SparkleStrength;
                float  _WaveAmp;
                float  _WaveFreq;
                float  _WaveSpeed;
                float  _DepthEffects;     // Toggle 백킹(분기엔 키워드 사용)
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 worldUV    : TEXCOORD0;   // 월드 XZ — 메시 UV 무관 균일 타일링
#if defined(_DEPTH_EFFECTS)
                float4 screenPos  : TEXCOORD1;   // xy/w = 스크린 UV, w = 수면 eye depth
#endif
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            Varyings Vert(Attributes v)
            {
                Varyings o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_TRANSFER_INSTANCE_ID(v, o);

                float3 posWS = TransformObjectToWorld(v.positionOS.xyz);

                // ── 버텍스 웨이브: sin 2개 합성(주파수/위상 비정수배 → 반복 패턴 은닉) ──
                //   _WaveAmp=0 이면 결과 0 — 분기 없이도 사실상 무비용(버텍스당 sin 2회).
                float t = _Time.y * _WaveSpeed;
                float wave = sin(posWS.x * _WaveFreq + t)
                           + sin(posWS.z * _WaveFreq * 0.87 + t * 1.13);
                posWS.y += wave * _WaveAmp * 0.5;

                o.positionCS = TransformWorldToHClip(posWS);
                o.worldUV = posWS.xz * _NoiseScale;
#if defined(_DEPTH_EFFECTS)
                // NDC 수동 계산 — ComputeScreenPos 는 URP 버전에 따라 제거되어 직접 전개(버전 호환).
                float4 ndc = o.positionCS * 0.5;
                o.screenPos.xy = float2(ndc.x, ndc.y * _ProjectionParams.x) + ndc.w;
                o.screenPos.zw = o.positionCS.zw;   // w = 수면 eye depth(퍼스펙티브)
#endif
                return o;
            }

            half4 Frag(Varyings i) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(i);

                // ── 표면 노이즈: 같은 텍스처를 두 방향으로 스크롤해 2회 페치 ──
                float2 uv1 = i.worldUV + _NoiseSpeed.xy * _Time.y;
                float2 uv2 = i.worldUV * 1.7 + _NoiseSpeed.zw * _Time.y;
                half n1 = SAMPLE_TEXTURE2D(_NoiseTex, sampler_NoiseTex, uv1).r;
                half n2 = SAMPLE_TEXTURE2D(_NoiseTex, sampler_NoiseTex, uv2).r;
                half surface = n1 * n2;   // 곱 = 밝은 점이 드물게 → 스파클 소재

                half4 col;
#if defined(_DEPTH_EFFECTS)
                // ── 깊이 그라데이션 + 쇼어 폼 ──
                float2 screenUV = i.screenPos.xy / i.screenPos.w;
                float sceneEye  = LinearEyeDepth(SampleSceneDepth(screenUV), _ZBufferParams);
                float waterEye  = i.screenPos.w;
                float depthDiff = max(sceneEye - waterEye, 0.0);

                half depthFactor = saturate(depthDiff / _DepthRange);
                col = lerp(_ShallowColor, _DeepColor, depthFactor);

                // 쇼어 폼 — 교차 깊이가 폼 거리 이내. 노이즈로 경계를 흔들어 유기적 라인 +
                //   시간 스크롤(n1)이 자연스러운 명멸을 만듦. smoothstep 1회.
                half foamEdge = _FoamDistance * (0.6 + 0.8 * n1);
                half foam = 1.0 - smoothstep(foamEdge * 0.5, foamEdge, (half)depthDiff);
                col.rgb = lerp(col.rgb, _FoamColor.rgb, foam * _FoamColor.a);
                col.a   = max(col.a, foam * _FoamColor.a);
#else
                // 깊이 미사용 폴백 — 노이즈 저주파(n1)로 얕/깊 색을 섞어 단조로움 회피. 의존성 0.
                col = lerp(_ShallowColor, _DeepColor, (half)(0.35 + 0.5 * n1));
#endif

                // ── 스파클: 노이즈 곱의 상위 구간만 하이라이트(smoothstep 창) ──
                half sparkle = smoothstep(_SparkleThreshold, _SparkleThreshold + (half)0.12, surface);
                col.rgb += sparkle * _SparkleStrength;

                return col;
            }
            ENDHLSL
        }
    }

    // URP 미로드 등 비상 폴백 — 마젠타 대신 단색 반투명.
    FallBack "Universal Render Pipeline/Unlit"
}
