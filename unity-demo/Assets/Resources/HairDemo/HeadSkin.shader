Shader "HairDemo/HeadSkin"
{
    Properties
    {
        _BaseColor ("Skin Color", Color) = (0.95, 0.80, 0.72, 1)
        _HairLightColor ("Light Color", Color) = (1, 0.96, 0.88, 1)
        _Ambient ("Ambient", Float) = 0.58
        _DiffuseWeight ("Diffuse Weight", Float) = 0.50
        _ShadowFloor ("Shadow Floor", Float) = 0.6
        _ScalpColor ("Scalp tint color (hair color)", Color) = (0.14, 0.07, 0.045, 1)
        _ScalpTint ("Scalp tint amount (0 = off)", Range(0, 1)) = 1
        [NoScaleOffset] _ScalpTex ("Scalp density field (3D)", 3D) = "" {}
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "Queue" = "Geometry" }

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseColor;
                float4 _HairLightColor;
                float _Ambient;
                float _DiffuseWeight;
                float _ShadowFloor;
                float4 _ScalpColor;
                float _ScalpTint;
                float4 _ScalpOrigin;
                float4 _ScalpDims;
            CBUFFER_END

            // 두피 밀도 필드: 월드 좌표를 그대로 넣어 읽는 3D 텍스처. 값 1 = 머리카락이 빽빽한 두피.
            // 정점마다 계산해서 보간하던 이전 방식은 정점이 듬성한 두상에서 삼각형 모양으로 얼룩졌다.
            TEXTURE3D(_ScalpTex);
            SAMPLER(sampler_ScalpTex);

            struct Attributes
            {
                float3 positionOS : POSITION;
                float3 normalOS   : NORMAL;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS   : TEXCOORD1;
            };

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                OUT.positionWS = TransformObjectToWorld(IN.positionOS);
                OUT.positionCS = TransformWorldToHClip(OUT.positionWS);
                OUT.normalWS = TransformObjectToWorldNormal(IN.normalOS);
                return OUT;
            }

            float4 frag(Varyings IN) : SV_Target
            {
                float3 N = normalize(IN.normalWS);
                float4 shadowCoord = TransformWorldToShadowCoord(IN.positionWS);
                Light mainLight = GetMainLight(shadowCoord);

                float diff = max(dot(N, mainLight.direction), 0.0);
                // 머리카락이 두피/이마/얼굴에 드리우는 그림자
                float shadow = lerp(_ShadowFloor, 1.0, mainLight.shadowAttenuation);

                // 빛 색은 40%만 섞는다. 노란 빛을 그대로 곱하면 피부가 주황빛으로 짙어진다.
                float3 lightTint = lerp(float3(1.0, 1.0, 1.0), _HairLightColor.rgb, 0.4);
                // 루트 틴트: 머리카락이 난 두피는 머리색 쪽으로 어둡게 칠한다. 머리카락 사이로 비치는 두피가
                // 얼굴색 그대로 환하게 보이면 가발처럼 보이기 때문이다. 뿌리 가까이일수록(scalpMask) 진하다.
                float3 fieldUVW = (IN.positionWS - _ScalpOrigin.xyz) / (_ScalpDims.xyz * _ScalpOrigin.w);
                float density = _ScalpDims.w > 0.5 ? SAMPLE_TEXTURE3D(_ScalpTex, sampler_ScalpTex, fieldUVW).r : 0.0;
                float mask = smoothstep(0.20, 0.60, density);
                // 이마는 앞(+z)을 향한 면이다. 정수리/뒤통수/옆머리 밑은 머리카락이 두피를 덮지만 이마는 앞머리가
                // 있든 없든 머리카락 안쪽이 아니라서, 칠한 색을 앞쪽을 향한 면에서 서서히 뺀다. 그러면 앞머리가 없는
                // 스타일에서 이마가 갈색 띠로 칠해지지 않고 정수리에서 이마로 자연스럽게 옅어진다. (정수리는 위를 향해서 그대로 칠한다.)
                float3 Nn = normalize(IN.normalWS);
                float frontFacing = smoothstep(0.25, 0.70, Nn.z);
                float crownFacing = smoothstep(0.55, 0.90, Nn.y);
                float tint = mask * _ScalpTint * (1.0 - frontFacing * (1.0 - crownFacing));
                // 두피를 칠하는 색: 피부색에 배율을 곱하는 방식은 적갈색/금발처럼 빨강이 많은 머리색에서는 빨간 성분이
                // 거의 줄지 않아 살구색이 그대로 남았다. 레퍼런스처럼 "머리색 자체"로 칠한다.
                // 머리카락 밑 두피는 머리카락에 가려 빛을 덜 받으므로 밝기도 함께 낮춘다(occlusion).
                // 머리색으로 직접 칠하면 고동색 말고는 두피가 부자연스러웠다. 머리카락 밑 두피는 실제로 "피부의 그림자색"이
                // 보이는 곳이므로, 어둡고 약간 붉은 피부 그림자색을 바탕으로 하고 머리색은 약 20%만 섞는다.
                float3 shadowSkin = _BaseColor.rgb * float3(0.62, 0.48, 0.46);
                float3 scalpAlbedo = lerp(shadowSkin, saturate(_ScalpColor.rgb * 1.2 + 0.02), 0.22);
                float3 skin = lerp(_BaseColor.rgb, scalpAlbedo, tint * 0.92);
                float occlusion = lerp(1.0, 0.55, tint);
                float3 c = (_Ambient + diff * _DiffuseWeight * shadow) * occlusion * skin * lightTint;

                // 계산한 색을 그대로 출력한다. 예전에는 Linear 색공간이라는 이유로 pow(c, 2.2)를 한 번 더 했는데,
                // 이 프로젝트 화면에서는 그 값이 감마 인코딩 없이 그대로 표시돼서 피부가 이중으로 어둡고 주황빛이
                // 됐다(스크린샷 픽셀 R212 G139 B101: G/R, B/R 비가 pow(2.2)를 적용한 값과 일치). 프로토타입도
                // 같은 계산 결과를 그대로 화면에 냈으므로 pow 없이 출력해야 원래 의도한 피부색이 나온다.
                return float4(c, 1.0);
            }
            ENDHLSL
        }

        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }
            ZWrite On
            ZTest LEqual
            ColorMask 0

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex ShadowVert
            #pragma fragment ShadowFrag
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseColor;
                float4 _HairLightColor;
                float _Ambient;
                float _DiffuseWeight;
                float _ShadowFloor;
                float4 _ScalpColor;
                float _ScalpTint;
                float4 _ScalpOrigin;
                float4 _ScalpDims;
            CBUFFER_END

            float3 _LightDirection;
            float3 _LightPosition;

            struct Attributes
            {
                float3 positionOS : POSITION;
                float3 normalOS   : NORMAL;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
            };

            Varyings ShadowVert(Attributes IN)
            {
                Varyings OUT;
                float3 positionWS = TransformObjectToWorld(IN.positionOS);
                float3 normalWS = TransformObjectToWorldNormal(IN.normalOS);

                #if _CASTING_PUNCTUAL_LIGHT_SHADOW
                    float3 lightDirectionWS = normalize(_LightPosition - positionWS);
                #else
                    float3 lightDirectionWS = _LightDirection;
                #endif

                OUT.positionCS = ApplyShadowClamping(TransformWorldToHClip(ApplyShadowBias(positionWS, normalWS, lightDirectionWS)));
                return OUT;
            }

            float4 ShadowFrag(Varyings IN) : SV_Target
            {
                return 0;
            }
            ENDHLSL
        }

        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }
            ZWrite On
            ColorMask R

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex DepthVert
            #pragma fragment DepthFrag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseColor;
                float4 _HairLightColor;
                float _Ambient;
                float _DiffuseWeight;
                float _ShadowFloor;
                float4 _ScalpColor;
                float _ScalpTint;
                float4 _ScalpOrigin;
                float4 _ScalpDims;
            CBUFFER_END

            struct Attributes { float3 positionOS : POSITION; };
            struct Varyings   { float4 positionCS : SV_POSITION; };

            Varyings DepthVert(Attributes IN)
            {
                Varyings OUT;
                OUT.positionCS = TransformObjectToHClip(IN.positionOS);
                return OUT;
            }

            float4 DepthFrag(Varyings IN) : SV_Target { return 0; }
            ENDHLSL
        }
    }
}
