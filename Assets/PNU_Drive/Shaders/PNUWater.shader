// PNU Drive 물(바다·강·저수지) — 퀘스트에서도 가볍게(깊이 텍스처 없이)
// - 물결: 방향·길이가 다른 사인 물결 6개를 합한 기울기(멀수록 잔물결을 줄여 반짝임 깨짐 막음)
// - 하늘 반사(반사 프로브가 비어 있으면 하늘색 그라데이션) + 프레넬(비스듬히 볼수록 반사)
// - 해 반짝임, 물가: 메쉬 UV.x = 물가까지 거리(m) → 얕은 물빛 + 일렁이는 거품
// - VR 한 번에 두 눈 그리기(single-pass instanced) 매크로 포함
Shader "PNU/Water"
{
    Properties
    {
        _DeepColor ("깊은 물빛", Color) = (0.035, 0.15, 0.21, 1)
        _ShallowColor ("얕은 물빛", Color) = (0.09, 0.33, 0.36, 1)
        _FoamColor ("거품", Color) = (0.88, 0.93, 0.94, 1)
        _SkyZenith ("하늘 위", Color) = (0.36, 0.55, 0.82, 1)
        _SkyHorizon ("하늘 가", Color) = (0.74, 0.80, 0.86, 1)
        _WaveScale ("물결 크기(작을수록 큰 물결)", Float) = 1.0
        _WaveStrength ("물결 세기", Range(0, 2)) = 1.0
        _WaveSpeed ("물결 빠르기", Float) = 1.0
        _ShoreFade ("얕은 물 폭(m)", Float) = 16
        _FoamWidth ("거품 폭(m)", Float) = 2.2
        _Gloss ("해 반짝임 날카로움", Float) = 700
        _SunBoost ("해 반짝임 세기", Float) = 5
        _ReflStrength ("반사 세기", Range(0, 1)) = 0.92
    }
    SubShader
    {
        Tags { "RenderType" = "Opaque" "Queue" = "Geometry" "RenderPipeline" = "UniversalPipeline" }
        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fog
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _DeepColor, _ShallowColor, _FoamColor, _SkyZenith, _SkyHorizon;
                float _WaveScale, _WaveStrength, _WaveSpeed, _ShoreFade, _FoamWidth, _Gloss, _SunBoost, _ReflStrength;
            CBUFFER_END

            // 물결(방향, 파수 1/m, 기울기 크기)
            static const float2 WD[6] = { float2(0.95, 0.31), float2(-0.62, 0.78), float2(0.21, -0.98), float2(-0.88, -0.47), float2(0.55, 0.83), float2(0.99, -0.12) };
            static const float WK[6] = { 0.09, 0.15, 0.26, 0.43, 0.71, 1.17 };
            static const float WA[6] = { 0.050, 0.042, 0.034, 0.026, 0.019, 0.013 };

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float2 uv : TEXCOORD1;
                half fogFactor : TEXCOORD2;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings vert(Attributes v)
            {
                Varyings o = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.positionWS = TransformObjectToWorld(v.positionOS.xyz);
                o.positionCS = TransformWorldToHClip(o.positionWS);
                o.uv = v.uv;
                o.fogFactor = ComputeFogFactor(o.positionCS.z);
                return o;
            }

            float2 WaveSlope(float2 p, float t, float dist)
            {
                float2 s = float2(0, 0);
                [unroll] for (int i = 0; i < 6; i++)
                {
                    float k = WK[i] * _WaveScale;
                    float w = sqrt(9.8 * k) * _WaveSpeed;                       // 깊은 물 분산(긴 물결이 빠름)
                    float fade = saturate(1.0 - dist * k / 260.0);              // 멀면 잔물결부터 지움
                    s += WD[i] * (WA[i] * cos(dot(WD[i], p) * k + w * t) * fade);
                }
                return s;
            }

            half4 frag(Varyings i) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
                float3 V = GetWorldSpaceNormalizeViewDir(i.positionWS);
                float dist = distance(GetCameraPositionWS(), i.positionWS);
                float t = _Time.y;
                float2 sl = WaveSlope(i.positionWS.xz, t, dist) * _WaveStrength;
                float3 N = normalize(float3(-sl.x, 1.0, -sl.y));
                // 물가
                float shore = max(i.uv.x, 0.0);
                float shallow = saturate(1.0 - shore / max(_ShoreFade, 0.01));
                half3 body = lerp(_DeepColor.rgb, _ShallowColor.rgb, shallow * shallow);
                // 반사: 반사 프로브(하늘) — 비었으면 하늘 그라데이션
                float3 R = reflect(-V, N); R.y = abs(R.y);
                half3 probe = GlossyEnvironmentReflection(R, 0.05h, 1.0h);
                half3 skyc = lerp(_SkyHorizon.rgb, _SkyZenith.rgb, sqrt(saturate(R.y)));
                half3 refl = dot(probe, half3(1, 1, 1)) > 0.06 ? probe : skyc;
                float NdotV = saturate(dot(N, V));
                float fres = 0.02 + 0.98 * pow(1.0 - NdotV, 5.0);
                half3 col = lerp(body, refl * _ReflStrength, saturate(fres));
                // 해 반짝임
                Light L = GetMainLight();
                float3 H = normalize(L.direction + V);
                float spec = pow(saturate(dot(N, H)), _Gloss) * _SunBoost;
                col += L.color * spec;
                // 물가 거품(조금씩 밀려왔다 빠짐)
                float fw = _FoamWidth * (0.7 + 0.3 * sin(t * 1.1 + i.positionWS.x * 0.13 + i.positionWS.z * 0.09));
                float foam = saturate(1.0 - shore / max(fw, 0.01));
                foam *= 0.55 + 0.45 * sin(i.positionWS.x * 0.9 + i.positionWS.z * 0.7 + t * 0.8);
                col = lerp(col, _FoamColor.rgb, saturate(foam) * 0.65);
                col = MixFog(col, i.fogFactor);
                return half4(col, 1.0);
            }
            ENDHLSL
        }
    }
    FallBack "Universal Render Pipeline/Unlit"
}
