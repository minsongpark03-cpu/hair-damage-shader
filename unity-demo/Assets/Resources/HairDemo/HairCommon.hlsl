#ifndef HAIR_DEMO_COMMON_INCLUDED
#define HAIR_DEMO_COMMON_INCLUDED

// OpenGL 프로토타입(hair.vert / marschnerHair.frag)의 손상 로직을 URP HLSL로 옮긴 공용 코드.
// 포워드 / 섀도우캐스터 / 뎁스 패스가 모두 같은 변위·절단 계산을 써야 그림자 위치가 어긋나지 않는다.

#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

TEXTURE2D(_LookUp1); SAMPLER(sampler_LookUp1);
TEXTURE2D(_LookUp2); SAMPLER(sampler_LookUp2);

CBUFFER_START(UnityPerMaterial)
    float4 _BaseHairColor;
    float4 _HairLightColor;
    float _Damage;
    float _AmbientCol;
    float _DiffuseCol;
    float _Rcol;
    float _TTcol;
    float _Exposure;
    float _NoiseMode;
    float _InterpMode;
    float _ShadowAmount;
    float _HairWidth;
    float _ChildIndex;
    float _ChildSpread;
    float _SplitIndex;
    float _SplitSpread;
    float _DepthPrepass;
    float _CuticleNoise;
    float _DebugMode;
    float _NodeOn;
    float _RootTint;
    float _MaxStrandLen;
CBUFFER_END

// 메쉬 입력: positionOS = 가닥 위치, tangentOS(NORMAL 슬롯) = 가닥 접선,
// hairParams(TEXCOORD0) = (뿌리0~끝1, 가닥별 랜덤, 정규화된 가닥 길이, 미사용)
struct HairAttributes
{
    float3 positionOS  : POSITION;
    float3 tangentOS   : NORMAL;
    float4 hairParams  : TEXCOORD0;
    float4 hairParams2 : TEXCOORD1; // x = 바깥 정도(0=안쪽 ~ 1=바깥). 라인 메쉬에는 없다(0).
};

float HairRandom(float2 p)
{
    return frac(sin(dot(p, float2(12.9898, 78.233))) * 43758.5453);
}

float HairNoise(float2 p)
{
    float2 i = floor(p);
    float2 f = frac(p);
    float2 u = (_InterpMode > 0.5) ? (f * f * (3.0 - 2.0 * f)) : f;
    return lerp(lerp(HairRandom(i + float2(0.0, 0.0)), HairRandom(i + float2(1.0, 0.0)), u.x),
                lerp(HairRandom(i + float2(0.0, 1.0)), HairRandom(i + float2(1.0, 1.0)), u.x), u.y);
}

// 자식 가닥: 원본(가이드) 가닥을 같은 메쉬로 여러 번 그리면서, 번호(_ChildIndex)마다 가닥 옆으로
// 살짝 벗어난 위치로 옮겨 밀도를 높인다. 뿌리 쪽은 거의 붙어 있고 끝으로 갈수록 퍼진다.
// 자식마다 랜덤값(rnd)도 바꿔서 손상 시 풀리는 모양과 흩날림이 가닥마다 달라지게 한다.
void HairApplyChild(float childIndex, inout float3 posOS, float3 tangentOS, float param, inout float rnd)
{
    if (childIndex < 0.5) return;

    float id = childIndex;
    float h1 = HairRandom(float2(rnd * 91.7 + id * 13.1, 1.7));
    float h2 = HairRandom(float2(rnd * 47.3 + id * 7.9, 5.3));

    float ang = h1 * 6.28318;
    float spread = _ChildSpread * (0.35 + 0.65 * h2) * lerp(0.15, 1.0, param);

    float3 t = normalize(tangentOS);
    float3 side = cross(t, float3(0.0, 1.0, 0.0));
    side = (dot(side, side) > 1e-6) ? normalize(side) : float3(1.0, 0.0, 0.0);
    float3 bi = cross(t, side);

    posOS += (side * cos(ang) + bi * sin(ang)) * spread;
    rnd = frac(rnd + id * 0.381966 + h2 * 0.1);
}

// ---------------------------------------------------------------------------
// 갈라진 모발 (split ends)
//  - 누가: 긴 머리일수록, 바깥 머리일수록 잘 갈라진다. 손상도가 오르면 기준선이 내려가서
//         점점 더 많은 가닥이 갈라진다.
//  - 어떻게: 갈라진 가닥의 끝 구간이 2~3개의 더 얇은 잔가닥으로 나뉘어 끝으로 갈수록 벌어진다.
// ---------------------------------------------------------------------------

// 갈라지기 쉬운 정도(0~1). 긴 머리 55% + 바깥 머리 30% + 가닥별 우연 15%
float HairSplitPropensity(float rnd, float strandLength, float outer)
{
    float luck = HairRandom(float2(rnd * 3.7 + 0.31, 8.13));
    // 아랫부분까지 길게 내려오는 가닥일수록 잘 갈라진다(길이 비중 0.55 -> 0.65, 바깥 0.30 -> 0.22).
    return 0.65 * smoothstep(0.30, 1.0, strandLength) + 0.22 * outer + 0.13 * luck;
}

// 손상도가 오를수록 기준선이 내려가 더 낮은 점수의 가닥까지 갈라진다.
bool HairIsSplit(float propensity)
{
    // 손상이 최대여도 갈라지기 쉬운 가닥의 약 1/4~1/3만 갈라진다(실제로는 일부만 갈라진다).
    return _Damage > 0.001 && propensity >= (1.0 - _Damage * 0.4);
}

// 갈라지기 시작하는 위치(0=뿌리, 1=끝). 끝에서 아주 조금만 갈라진다: 마지막 약 2.5~7% 구간.
float HairSplitStart(float rnd)
{
    float r3 = HairRandom(float2(rnd * 5.3 + 1.7, 2.41));
    return 1.0 - lerp(0.025, 0.07, saturate(_Damage)) * (0.6 + 0.4 * r3);
}

// 잔가닥 개수: 항상 두 갈래
float HairFiberCount(float rnd)
{
    return 2.0;
}

// 짧은 머리는 보호하고 긴 머리 위주로 손상을 적용
float HairFinalDamage(float strandLength)
{
    return _Damage * smoothstep(0.2, 0.5, strandLength);
}

// ---------------------------------------------------------------------------
// 결절성 열모증 (trichorrhexis nodosa)
//  줄기 중간에 큐티클이 부풀어 터진 짧은 마디(결절)가 생긴다. 그 구간은 약간 굵어지고,
//  피질 섬유가 드러나 희뿌옇게 보이며 빛을 강하게 산란시킨다.
//  손상도가 높을수록 마디가 있는 가닥이 늘어나고, 가닥당 최대 2개의 마디가 줄기 중간에 생긴다.
// 반환: 이 위치가 마디 안쪽인 정도 (0 = 정상, 1 = 마디 한가운데)
// ---------------------------------------------------------------------------
float HairNode(float param, float rnd, float strandLength)
{
    float fd = HairFinalDamage(strandLength);
    if (_NodeOn < 0.5 || fd < 0.001) return 0.0;

    // 아래로 길게 늘어진 머리일수록(= 아랫부분까지 내려오는 가닥) 더 많이 상한다.
    float lowness = smoothstep(0.25, 0.9, strandLength);

    // 마디가 있는 가닥만 (손상 최대 + 가장 긴 머리일 때 약 30%). 손상도와 길이에 비례해서 늘어난다.
    if (HairRandom(float2(rnd * 17.3 + 2.1, 6.6)) > fd * (0.04 + 0.26 * lowness)) return 0.0;

    float node = 0.0;
    for (int i = 0; i < 2; i++)
    {
        // 두 번째 마디는 일부 가닥에만
        if (i == 1 && HairRandom(float2(rnd * 3.1 + 7.7, 2.8)) < 0.7) break;
        // 마디 위치는 줄기 중간~아랫부분 쪽으로 치우친다(윗부분=뿌리 쪽은 거의 안 생김).
        float center = lerp(0.40, 0.92, pow(HairRandom(float2(rnd * 9.1 + i * 3.7, 1.9)), 0.6));
        float halfLen = 0.0012 + 0.0018 * HairRandom(float2(rnd * 4.3 + i * 5.9, 8.2));
        node = max(node, 1.0 - smoothstep(0.35 * halfLen, halfLen, abs(param - center)));
    }
    return node;
}

// hair.vert: 부피 팽창 + 가닥별 흩날림
float3 HairDisplace(float3 posOS, float param, float rnd, float strandLength)
{
    float finalDamage = HairFinalDamage(strandLength);
    if (finalDamage > 0.0)
    {
        float3 outwardDir = normalize(float3(posOS.x, 0.0, posOS.z) + 0.0001);

        float randomAngle = rnd * 6.28318;
        float3 randomPushDir = float3(cos(randomAngle), 0.0, sin(randomAngle));

        float3 volumeDir = normalize(outwardDir * 2.0 + randomPushDir * 1.0 + float3(0.0, 0.1, 0.0));

        float3 frizzDir;
        if (_NoiseMode > 0.5)
        {
            // 연속적인 절차적 노이즈(삼각함수)
            frizzDir = float3(
                sin(rnd * 123.4 + param * 20.0),
                cos(rnd * 567.8 - param * 15.0),
                sin(rnd * 910.1 + param * 25.0));
        }
        else
        {
            // 대조군: 정점마다 독립적인 화이트 노이즈
            frizzDir = float3(
                HairRandom(float2(rnd, param * 100.1)) * 2.0 - 1.0,
                HairRandom(float2(rnd, param * 200.2)) * 2.0 - 1.0,
                HairRandom(float2(rnd, param * 300.3)) * 2.0 - 1.0);
        }

        float effectRange = smoothstep(0.1, 1.0, param);
        float liftWeight = effectRange * 0.3 * finalDamage;
        float frizzWeight = effectRange * 0.1 * finalDamage;

        posOS += volumeDir * liftWeight + frizzDir * frizzWeight;
    }
    return posOS;
}

// marschnerHair.frag: 끝이 끊어지고 올이 풀리는 절단 마스크 (1=남음, 0=사라짐)
float HairCutAlpha(float param, float rnd, float strandLength)
{
    float finalDamage = HairFinalDamage(strandLength);
    float finalAlpha = 1.0;
    if (finalDamage > 0.0)
    {
        float maxBreakage = 0.65;
        float skewedRandom = pow(rnd, 0.3);
        float cutThreshold = 1.0 - (skewedRandom * maxBreakage * finalDamage);

        float frayArea = smoothstep(cutThreshold - 0.1, cutThreshold, param);
        float2 frayUV = float2(rnd * 100.0, param * 50.0);
        float frayNoise = HairNoise(frayUV);

        float alphaMask = lerp(1.0, frayNoise, frayArea);
        float cutFade = 1.0 - smoothstep(cutThreshold - 0.02, cutThreshold, param);

        finalAlpha = alphaMask * cutFade;
    }
    return finalAlpha;
}

#endif
