Shader "HairDemo/Marschner"
{
    Properties
    {
        _LookUp1 ("LookUp1 (R lobe LUT)", 2D) = "white" {}
        _LookUp2 ("LookUp2 (TT lobe LUT)", 2D) = "white" {}
        _BaseHairColor ("Base Hair Color", Color) = (0.14, 0.07, 0.045, 1)
        _HairLightColor ("Light Color", Color) = (1, 0.96, 0.88, 1)
        _Damage ("Damage", Range(0, 1)) = 0
        _AmbientCol ("Ambient", Float) = 0.10
        _DiffuseCol ("Diffuse", Float) = 0.22
        _Rcol ("R lobe (surface reflection)", Float) = 0.40
        _TTcol ("TT lobe (transmission)", Float) = 1.3
        _Exposure ("Exposure", Float) = 0.5
        _NoiseMode ("Noise Mode (1=procedural, 0=white)", Float) = 1
        _InterpMode ("Interp Mode (1=hermite, 0=linear)", Float) = 1
        _ShadowAmount ("Hair receives shadow (0=off, 1=on)", Range(0, 1)) = 1
        _HairWidth ("Hair width (world units)", Float) = 0.0022
        _ChildIndex ("Child strand index (0 = guide strand)", Float) = 0
        _ChildSpread ("Child strand spread (world units)", Float) = 0.02
        _SplitIndex ("Split fiber index (0 = normal strand, 1~3 = split fiber)", Float) = 0
        _SplitSpread ("Split fiber spread (world units)", Float) = 0.045

        _CuticleNoise ("Cuticle noise strength (0 = off)", Range(0, 1)) = 1
        _DebugMode ("Debug view (0 = normal, 1 = unlit color, 2 = alpha)", Float) = 0
        _NodeOn ("Trichorrhexis nodosa (1 = on)", Float) = 1
        _RootTint ("Root tint (1 = on)", Float) = 1
        _MaxStrandLen ("Longest strand length (world units)", Float) = 4

        // 렌더 상태 (같은 셰이더를 "깊이 프리패스"용과 "색 블렌딩"용 두 머티리얼로 나눠 쓴다)
        _DepthPrepass ("Depth prepass mode (1 = depth only)", Float) = 0
        _ZWrite ("ZWrite", Float) = 0
        _ColorMask ("ColorMask (15 = RGBA, 0 = none)", Float) = 15
        _SrcBlend ("Src Blend", Float) = 5
        _DstBlend ("Dst Blend", Float) = 10
    }

    SubShader
    {
        Tags { "RenderType" = "Transparent" "RenderPipeline" = "UniversalPipeline" "Queue" = "Transparent" }
        Cull Off

        // ---------------------------------------------------------------
        // 1) 메인 패스: Marschner LUT 라이팅 + 유멜라닌/페오멜라닌 Beer-Lambert 탈색
        // ---------------------------------------------------------------
        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }
            // 가는 가닥은 부분 투명도(알파 블렌딩)로 그린다. 수십만 개를 정렬할 수는 없지만, 같은 색의
            // 가늘고 촘촘한 가닥은 겹친 개수만큼 자연스럽게 진해져서 정렬 없이도 잘 보인다.
            // 두상(불투명)은 먼저 그려지고 깊이 테스트로 가려지며, 헤어끼리는 깊이를 쓰지 않는다.
            // 프리패스 머티리얼: 깊이만 쓰고(ZWrite On, ColorMask 0) 불투명 큐에서 먼저 그려진다.
            // 색 머티리얼: 프리패스가 쓴 깊이와 비교(ZTest LEqual)해서 가장 앞 가닥만 통과시킨다.
            Blend [_SrcBlend] [_DstBlend]
            ZWrite [_ZWrite]
            ColorMask [_ColorMask]
            ZTest LEqual

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex HairVert
            #pragma fragment HairFrag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH

            #include "HairCommon.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            struct HairVaryings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 tangentWS  : TEXCOORD1;
                float3 hairParams : TEXCOORD2;
                float3 ribbon     : TEXCOORD3; // x = 좌우(-1~1), y = 가닥 굵기(px), z = 띠 반폭(px)
                float2 splitInfo  : TEXCOORD4; // x = 보임 정도(잔가닥 페이드), y = 갈라져서 더 밝아지는 정도
            };

            // 머리카락 데이터는 메쉬가 아니라 버퍼에서 직접 읽는다(프로시저럴 드로우).
            // 컴퓨터가 가닥을 카메라 깊이순(먼 것 → 가까운 것)으로 정렬한 목록(_HairOrder)을 만들어 주고,
            // 인스턴스 하나 = (가닥, 자식 번호, 잔가닥 번호) 하나. 이렇게 하면 알파 블렌딩 순서가 가닥 단위로
            // 정확해져서, 카메라를 돌려도 덩어리 단위로 밝기가 튀지 않는다.
            StructuredBuffer<float4> _HairA;      // (위치.xyz, 미사용)
            StructuredBuffer<float4> _HairB;      // (접선.xyz, 미사용)
            StructuredBuffer<uint2>  _StrandU;    // (시작 점 번호, 점 개수)
            StructuredBuffer<float4> _StrandF;    // (랜덤, 정규화 길이, 바깥 정도, 미사용)
            StructuredBuffer<uint>   _HairOrder;  // 정렬된 인스턴스 목록: strand | child << 14 | fiber << 17

            struct HairInput
            {
                uint vertexID   : SV_VertexID;
                uint instanceID : SV_InstanceID;
            };

            HairVaryings HairVert(HairInput INP)
            {
                HairVaryings OUT = (HairVaryings)0;
                OUT.positionCS = float4(2.0, 2.0, 2.0, 1.0); // 기본값: 화면 밖(버림)

                uint entry = _HairOrder[INP.instanceID];
                uint strand = entry & 0x3FFFu;
                float childIdx = (float)((entry >> 14) & 0x7u);
                float fiberIdx = (float)((entry >> 17) & 0x3u);

                uint2 range = _StrandU[strand];
                uint segment = INP.vertexID / 6u;
                uint corner = INP.vertexID - segment * 6u;
                if (segment + 1u >= range.y) return OUT; // 이 가닥에는 없는 선분(길이 차이로 남는 부분)

                // 선분당 사각형 2개: (i,-) (i+1,-) (i,+) / (i,+) (i+1,-) (i+1,+)
                uint pointOffset = (corner == 1u || corner == 4u || corner == 5u) ? 1u : 0u;
                float sideSign = (corner == 2u || corner == 3u || corner == 5u) ? 1.0 : -1.0;
                uint pointIdx = range.x + segment + pointOffset;

                float4 sf = _StrandF[strand];
                float param = (float)(segment + pointOffset) / max((float)(range.y - 1u), 1.0);
                float strandRnd = sf.x;
                float strandLen = sf.y;
                float outer = sf.z;
                float3 posOS = _HairA[pointIdx].xyz;
                float3 tangentOS = _HairB[pointIdx].xyz;
                float side = sideSign;

                HairApplyChild(childIdx, posOS, tangentOS, param, strandRnd);

                // --- 갈라진 모발 ---
                float widthScale = 1.0;   // 가닥 굵기 배율
                float fiberMask = 1.0;    // 이 정점이 보이는 정도 (잔가닥은 갈라지는 지점에서 서서히 나타남)
                float brighten = 0.0;     // 갈라진 부분이 더 밝아지는 정도 (멜라닌이 더 빠진 것으로 계산)

                float propensity = HairSplitPropensity(strandRnd, strandLen, outer);
                bool isSplit = HairIsSplit(propensity);
                float s0 = HairSplitStart(strandRnd);

                if (fiberIdx > 0.5)
                {
                    // 잔가닥 패스: 갈라지는 가닥이 아니거나 이 번호의 잔가닥이 없으면 통째로 화면 밖으로 보낸다.
                    if (!isSplit || fiberIdx > HairFiberCount(strandRnd) + 0.5)
                    {
                        OUT.positionCS = float4(2.0, 2.0, 2.0, 1.0);
                        return OUT;
                    }

                    // 갈라지는 지점(s0)에서 부모와 만나 시작해서, 끝으로 갈수록 가닥 둘레 방향으로 벌어진다.
                    // 잔가닥마다 끝나는 위치가 달라서 들쭉날쭉한 끝이 된다.
                    float tipEnd = lerp(0.97, 1.0, HairRandom(float2(strandRnd * 13.9 + fiberIdx * 9.7, 7.7)));
                    fiberMask = smoothstep(s0 - 0.01, s0 + 0.01, param) * (1.0 - smoothstep(tipEnd - 0.02, tipEnd, param));

                    float grow = smoothstep(s0, 1.0, param);
                    // 두 잔가닥은 서로 반대 방향(Y자)으로 벌어진다.
                    float ang = HairRandom(float2(strandRnd * 29.3 + 4.1, 3.3)) * 6.28318 + (fiberIdx - 1.0) * 3.14159;
                    float spreadAmt = _SplitSpread
                        * (0.5 + 0.9 * HairRandom(float2(strandRnd * 13.9 + fiberIdx * 3.3, 1.9)))
                        * pow(grow, 1.3);

                    float3 tdir = normalize(tangentOS);
                    float3 sideDir = cross(tdir, float3(0.0, 1.0, 0.0));
                    sideDir = (dot(sideDir, sideDir) > 1e-6) ? normalize(sideDir) : float3(1.0, 0.0, 0.0);
                    float3 binorm = cross(tdir, sideDir);
                    posOS += (sideDir * cos(ang) + binorm * sin(ang)) * spreadAmt;

                    widthScale = 0.8;   // 부모보다 약간 가는 잔가닥
                    brighten = 1.0;     // 피질이 드러나 멜라닌이 더 빠진 밝은 잔가닥
                }
                else if (isSplit)
                {
                    // 부모 가닥은 갈라지는 지점에서 끝나고, 그 자리를 두 잔가닥이 이어받는다.
                    fiberMask = 1.0 - smoothstep(s0 - 0.01, s0 + 0.01, param);
                    brighten = 0.3 * smoothstep(s0 - 0.1, s0, param);
                }

                posOS = HairDisplace(posOS, param, strandRnd, strandLen);
                float3 posWS = TransformObjectToWorld(posOS);

                float3 tanWS = TransformObjectToWorldDir(tangentOS);

                float4 clip0 = TransformWorldToHClip(posWS);
                float4 clip1 = TransformWorldToHClip(posWS + tanWS * 0.01);

                // 화면(픽셀) 공간에서 가닥이 향하는 방향과 그 수직 방향
                float2 screen = _ScreenParams.xy;
                float2 dir = (clip1.xy / clip1.w - clip0.xy / clip0.w) * screen;
                dir = (dot(dir, dir) > 1e-10) ? normalize(dir) : float2(1.0, 0.0);
                float2 nrm = float2(-dir.y, dir.x);

                // 가닥 굵기: 월드 단위로 정해서 멀수록 가늘어지고 가까울수록 굵어진다(실제 머리카락처럼).
                float pxPerUnit = 0.5 * screen.y * abs(UNITY_MATRIX_P[1][1]) / max(clip0.w, 1e-4);
                float widthPx = _HairWidth * pxPerUnit * widthScale;

                // 1픽셀보다 가는 가닥은 최소 폭의 띠로 그리되 알파로 굵기를 표현한다.
                float core = max(widthPx, 1.0) * 0.5;
                float halfExtent = core + 0.5;
                // 아직 나타나지 않은 잔가닥 구간은 폭을 0으로 만들어 래스터라이즈 비용을 없앤다.
                if (fiberMask < 0.001) halfExtent = 0.0;

                clip0.xy += nrm * (side * halfExtent) * (2.0 / screen) * clip0.w;

                OUT.positionWS = posWS;
                OUT.positionCS = clip0;
                OUT.tangentWS = tanWS;
                OUT.hairParams = float3(param, strandRnd, strandLen);
                OUT.ribbon = float3(side, widthPx, halfExtent);
                OUT.splitInfo = float2(fiberMask, brighten);
                return OUT;
            }

            float4 HairFrag(HairVaryings IN) : SV_Target
            {
                // 아직 나타나지 않은(또는 이미 끝난) 잔가닥 구간은 비싼 계산 전에 버린다.
                clip(IN.splitInfo.x - 0.003);

                float strandParam  = IN.hairParams.x;
                float strandRandom = IN.hairParams.y;
                float strandLength = IN.hairParams.z;

                float finalDamage = HairFinalDamage(strandLength);

                // --- 절단(끊어짐/올 풀림) ---
                float finalAlpha = HairCutAlpha(strandParam, strandRandom, strandLength);
                clip(finalAlpha - 0.05);

                // --- 띠 가장자리 커버리지 ---
                // 굵기 w(px)인 가닥이 1픽셀 안에 차지하는 면적과 같아지도록 삼각형 모양의 알파
                // 프로파일을 쓴다(w<1이면 높이를 w로 낮춰서 가늘게 보이게 한다).
                float ribbonWidthPx = IN.ribbon.y;
                float ribbonDist = abs(IN.ribbon.x) * IN.ribbon.z;
                float ribbonCore = max(ribbonWidthPx, 1.0) * 0.5;
                // 갈라진 잔가닥(brighten=1). 너무 가늘면 픽셀의 일부만 덮어 거의 안 보이므로,
                // 잔가닥은 최소 커버리지를 보장해서 가늘어도 또렷한 한 올로 보이게 한다.
                float fiber = saturate((IN.splitInfo.y - 0.3) / 0.7);
                // 결절(하얀 마디)도 가는 가닥에서 사라지지 않게 최소 커버리지를 보장한다.
                // 결절(비듬처럼 작은 하얀 점)은 정점이 아니라 픽셀마다 계산해야 1~3픽셀 크기로 작게 찍힌다.
                float node = (fiber > 0.5) ? 0.0 : HairNode(IN.hairParams.x, IN.hairParams.y, IN.hairParams.z);
                float coverage = saturate(ribbonCore + 0.5 - ribbonDist) * max(min(ribbonWidthPx, 1.0), max(0.7 * fiber, 0.6 * node));

                // 끊어지고 풀린 끝(fray)의 부분 투명도와 잔가닥 페이드도 그대로 반영
                float alpha = coverage * finalAlpha * IN.splitInfo.x;

                // 깊이 프리패스: 가닥의 "단단한 중심부"만 깊이에 기록한다. 이 깊이 덕분에 색 패스에서
                // 앞 가닥 뒤에 가려진 가닥(안쪽/뒤쪽 머리)이 비쳐 보이지 않는다. 너무 가는 가닥은
                // 기록하지 않아서 뒤가 비치는 게 자연스럽다.
                if (_DepthPrepass > 0.5)
                {
                    clip(alpha - 0.45);
                    return float4(0.0, 0.0, 0.0, 0.0);
                }
                clip(alpha - 0.003);

                // [디버그 2] 알파(커버리지)만 흑백으로 본다. 비침/성김이 알파 때문인지 확인용.
                if (_DebugMode > 1.5) return float4(alpha, alpha, alpha, 1.0);

                // 가닥 안에서 뿌리 쪽은 보호
                float localDamage = finalDamage * smoothstep(0.0, 1.0, strandParam);

                // --- 손상에 따른 반사 계수 ---
                float currentRcol = _Rcol * lerp(1.0, 0.3, localDamage * 0.7);
                float currentTTcol = _TTcol * (1.0 - localDamage * 0.95);
                float currentDiffuseCol = _DiffuseCol * (1.0 + localDamage * 6.0);

                float3 E = GetWorldSpaceNormalizeViewDir(IN.positionWS);

                // --- 큐티클 노이즈로 결 방향 비틀기 ---
                float2 cuticleUV = float2(strandRandom * 100.0, strandParam * 200.0);
                // 가닥을 따라 아주 촘촘하게(한 가닥에 200칸) 결 방향을 흔드는 노이즈라 반사광이 픽셀마다
                // 튀어서 확대하면 자글거릴 수 있다. _CuticleNoise로 세기를 줄이거나 끌 수 있다.
                float cuticleNoise = (HairNoise(cuticleUV) - 0.5) * 2.0 * _CuticleNoise;
                float cuticleShift = 0.05 + (cuticleNoise * localDamage * 1.0);
                // 갈라진 잔가닥은 가닥마다 결 방향이 제각각이라, 카메라/조명 각도에 따라 어떤 올은 번쩍이고
                // 어떤 올은 어둡다 -> 돌릴 때 끝이 반짝이는 느낌이 난다.
                cuticleShift += fiber * (HairRandom(float2(strandRandom * 7.1 + 3.0, 1.3)) - 0.5) * 0.9;
                float3 shiftedTangent = IN.tangentWS + (E * cuticleShift);
                float3 T = normalize(shiftedTangent + (cuticleNoise * localDamage * 0.3));

                // --- 탈색: 유멜라닌/페오멜라닌 이중 색소 Beer-Lambert (d'Eon et al. 2011 흡수계수) ---
                float tipWeight = smoothstep(0.15, 1.0, strandParam);
                float strandVariance = lerp(0.7, 1.3, strandRandom);
                float finalBleach = saturate(finalDamage * tipWeight * strandVariance);
                // 갈라진 모발은 큐티클이 벗겨져 피질이 드러나 있어서 멜라닌이 더 많이 빠진다 -> 더 밝다.
                finalBleach = saturate(finalBleach + 0.55 * IN.splitInfo.y);

                const float3 sigmaEumelanin   = float3(0.419, 0.697, 1.37);
                const float3 sigmaPheomelanin = float3(0.187, 0.4, 1.05);

                float3 color = _BaseHairColor.rgb;
                float luma   = dot(color, float3(0.299, 0.587, 0.114));
                float warmth = saturate((color.r - color.b) * 2.0);
                float totalMelaninBase = lerp(1.6, 0.3, luma);
                float eumelanin0   = totalMelaninBase * lerp(0.9, 0.55, warmth);
                float pheomelanin0 = totalMelaninBase * lerp(0.1, 0.45, warmth);

                float eumelaninConc   = eumelanin0 * exp(-3.0 * finalBleach);
                float pheomelaninConc = pheomelanin0 * exp(-0.6 * finalBleach) + 0.02;

                float3 sigmaA = eumelaninConc * sigmaEumelanin + pheomelaninConc * sigmaPheomelanin;
                float3 bleachedColor = exp(-sigmaA);
                float3 currentBaseColor = lerp(color, bleachedColor, finalBleach);
                // 결절: 피질이 드러나 멜라닌 색이 거의 안 보이는 희뿌연 상아색
                currentBaseColor = lerp(currentBaseColor, float3(0.88, 0.84, 0.74), 0.7 * node);

                // [디버그 1] 조명 없이 탈색된 색만 본다. 노이즈가 조명(반사광) 때문인지 확인용.
                if (_DebugMode > 0.5) return float4(currentBaseColor, alpha);

                // --- 라이팅 ---
                float4 shadowCoord = TransformWorldToShadowCoord(IN.positionWS);
                Light mainLight = GetMainLight(shadowCoord);
                float3 L = mainLight.direction;

                float distFromCenter = length(IN.positionWS.xz);
                float depthShadow = smoothstep(0.2, 0.9, distFromCenter);
                float shadowIntensity = lerp(1.0, depthShadow, 0.6 + (finalDamage * 0.4));

                // URP 섀도우맵(소프트 PCF) 기반 실제 그림자. 완전한 암부도 순흑이 되지 않게 바닥값을 둔다.
                float shadowAtt = lerp(1.0, mainLight.shadowAttenuation, _ShadowAmount);
                float shadowFactor = lerp(0.72, 1.0, shadowAtt);
                // 실제로 빛을 받는 정도(0 = 그림자 속, 1 = 직사광). 갈라진 끝/결절의 반짝임은 이 값에 비례해서
                // 빛을 받지 않으면 그냥 바랜 올로 보이고, 빛을 받을 때만 번쩍인다.
                float lit = shadowAtt;

                float2 fuv1;
                fuv1.x = dot(L, T);
                fuv1.y = dot(E, T);
                float3 angles;
                angles.xy = 0.5 + 0.5 * fuv1;

                float3 lightPerp = L - fuv1.x * T;
                float3 eyePerp   = E - fuv1.y * T;
                float denom = dot(eyePerp, eyePerp) * dot(lightPerp, lightPerp);
                float cosPhi = dot(E, L) * rsqrt(max(denom, 1e-5));
                angles.z = 0.5 * cosPhi + 0.5;

                float diffuse = sqrt(max(0.0, 1.0 - fuv1.x * fuv1.x));
                float3 diffuseColor = diffuse * currentBaseColor * currentDiffuseCol * shadowFactor;
                float3 ambientColor = currentBaseColor * _AmbientCol * (shadowIntensity * 0.5 + 0.5) * lerp(0.6, 1.0, shadowFactor);

                float4 m = SAMPLE_TEXTURE2D_LOD(_LookUp1, sampler_LookUp1, angles.xy, 0);

                float2 uv2;
                uv2.x = cos((asin(clamp(2.0 * angles.x - 1.0, -1.0, 1.0)) - asin(clamp(2.0 * angles.y - 1.0, -1.0, 1.0))) / 2.0) * 0.5 + 0.5;
                uv2.y = angles.z;
                float4 ntt = SAMPLE_TEXTURE2D_LOD(_LookUp2, sampler_LookUp2, uv2, 0);

                float3 lighting = float3(0.0, 0.0, 0.0);

                // R lobe는 큐티클 표면 프레넬 반사라 무채색이 맞다 — 하이라이트 띠 바깥에서는 거의 꺼지게 한다.
                // 잔가닥은 큐티클이 벗겨져 거칠고 얇아서 표면 반사(R lobe)가 훨씬 강하게 번쩍인다.
                float x = m.r * ntt.a * currentRcol * lerp(0.04, 1.0, shadowIntensity) * shadowFactor * (1.0 + 2.5 * fiber * lit);
                lighting += float3(x, x, x);

                // TT lobe(투과)와 diffuse는 현재(탈색된) 색소 색을 실어 나른다.
                // 잔가닥은 투과(TT)가 크게 늘어나고, 역광이 아니어도 은은히 빛이 비쳐 보이는 값을 더한다.
                float ttBoost = 1.0 + 0.8 * fiber * lit;
                lighting += (m.b * ntt.rgb * currentBaseColor) * currentTTcol * ttBoost * shadowIntensity * shadowFactor;
                lighting += currentBaseColor * 0.10 * fiber * lit;

                // --- 갈라진 끝의 난반사(글리터) ---
                // 큐티클이 벗겨진 거친 면은 한 방향으로 반사하지 않고 사방으로 흩뿌린다. 가닥을 따라
                // 마디(작은 거친 조각)가 드문드문 있고, 마디마다 면의 방향이 제각각이라 카메라나 빛이
                // 움직이면 어떤 마디는 번쩍이고 어떤 마디는 꺼진다. 색은 피질이 드러나 희끄무레한 색.
                float rough = saturate(IN.splitInfo.y);
                float nodule = floor(strandRandom * 977.0 + strandParam * 70.0);
                float nodActive = step(0.5, HairRandom(float2(nodule, 4.7)));
                float3 nodNormal = normalize(float3(HairRandom(float2(nodule, 1.1)),
                                                    HairRandom(float2(nodule, 2.2)),
                                                    HairRandom(float2(nodule, 3.3))) * 2.0 - 1.0);
                float3 halfV = normalize(E + L);
                float glint = pow(saturate(dot(nodNormal, halfV)), 3.0);
                // 참고 사진처럼 바랜 회백색(옅은 크림)으로, 빛을 받을 때(lit)만 번쩍인다.
                float3 scatterCol = lerp(currentBaseColor, float3(0.92, 0.86, 0.74), 0.7);
                // 빛 없을 때도 보이는 은은한 값(0.03) + 빛을 받는 마디별 번쩍임(최대 약 5)
                lighting += scatterCol * rough * (0.03 + 5.0 * nodActive * glint * lit);
                lighting += diffuseColor;

                // 결절: 평소엔 바랜 점(색 변화만), 빛을 받으면 산란이 늘어난다.
                lighting += float3(0.92, 0.86, 0.74) * node * (0.04 + 0.5 * lit * saturate(dot(E, L) * 0.5 + 0.5));
                lighting += float3(x, x, x) * 1.0 * node;

                float3 finalColor = (lighting + ambientColor) * _HairLightColor.rgb;

                // 루트 틴트: 두피에 가까운 뿌리 쪽(가닥 앞 약 20%)은 빛이 덜 닿고 색소가 진해 보여서 어둡다.
                // 어두워진 두피 색과 자연스럽게 이어지고, 뿌리가 두피 위에 둥둥 떠 보이는 느낌이 줄어든다.
                // 뿌리 끝에서 가장 어둡고(약 45%) 가닥을 따라 부드럽게 원래 밝기로 돌아온다.
                // 뿌리 구간은 가닥 길이의 비율이 아니라 "실제 길이"로 정한다. 비율(앞 20%)로 하면 긴 머리는 어두운 구간이
                // 두상 높이의 절반까지 내려와서, 앞머리가 없는 스타일은 머리 윗부분이 검은 모자를 쓴 것처럼 보였다.
                // 정규화 길이(strandLength)에 가장 긴 가닥의 실제 길이를 곱하면 이 점이 뿌리에서 가닥을 따라 얼마나 떨어졌는지 나온다.
                float rootDist = strandParam * strandLength * _MaxStrandLen;
                float rootFade = (1.0 - smoothstep(0.0, 0.10, rootDist)) * _RootTint;
                finalColor *= lerp(1.0, 0.72, rootFade);

                // --- 노출 + ACES 톤매핑 ---
                float3 hdr = max(finalColor, 0.0) * _Exposure;
                float3 mapped = saturate((hdr * (2.51 * hdr + 0.03)) / (hdr * (2.43 * hdr + 0.59) + 0.14));

                // 프로토타입은 마지막에 pow(1/2.2)로 감마 인코딩했다. Linear 색공간에서는
                // 프레임버퍼가 sRGB 인코딩을 대신해주므로 감마 색공간일 때만 직접 해준다.
                #if defined(UNITY_COLORSPACE_GAMMA)
                    mapped = pow(mapped, 1.0 / 2.2);
                #endif

                return float4(mapped, alpha);
            }
            ENDHLSL
        }

        // ---------------------------------------------------------------
        // 2) 섀도우캐스터: 메인 패스와 똑같은 손상 변위 + 절단을 적용해서
        //    그림자맵 위치가 실제 렌더링 위치와 어긋나지 않게 한다.
        // ---------------------------------------------------------------
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

            #include "HairCommon.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"

            float3 _LightDirection;
            float3 _LightPosition;

            struct ShadowVaryings
            {
                float4 positionCS : SV_POSITION;
                float3 hairParams : TEXCOORD0;
            };

            ShadowVaryings ShadowVert(HairAttributes IN)
            {
                ShadowVaryings OUT;
                float3 posOS = HairDisplace(IN.positionOS, IN.hairParams.x, IN.hairParams.y, IN.hairParams.z);
                float3 positionWS = TransformObjectToWorld(posOS);

                #if _CASTING_PUNCTUAL_LIGHT_SHADOW
                    float3 lightDirectionWS = normalize(_LightPosition - positionWS);
                #else
                    float3 lightDirectionWS = _LightDirection;
                #endif

                // 선(머리카락)에는 진짜 표면 노멀이 없다. 노멀 바이어스가 엉뚱한 방향으로 밀지 않도록
                // 노멀을 빛 방향과 같게 줘서(=노멀 바이어스 0) 깊이 바이어스만 적용한다.
                float4 positionCS = TransformWorldToHClip(ApplyShadowBias(positionWS, lightDirectionWS, lightDirectionWS));
                OUT.positionCS = ApplyShadowClamping(positionCS);
                OUT.hairParams = IN.hairParams.xyz;
                return OUT;
            }

            float4 ShadowFrag(ShadowVaryings IN) : SV_Target
            {
                clip(HairCutAlpha(IN.hairParams.x, IN.hairParams.y, IN.hairParams.z) - 0.05);
                return 0;
            }
            ENDHLSL
        }
    }
}
