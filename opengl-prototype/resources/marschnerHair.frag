#version 330 core

uniform vec3 eye_world;
uniform vec4 lightPosition;
uniform vec3 u_LightColor;
uniform sampler2D lookUp1;
uniform sampler2D lookUp2;
uniform float alpha;

in vec4 interpSurfPosition;
in vec3 interpSurfTangent;
in vec3 color;
in float strandParam;
in float strandRandom;
in float strandLength;
in vec4 fragPosLightSpace;

uniform float u_Damage;
uniform int u_InterpMode;
uniform sampler2DShadow shadowMap;

out vec4 fragColor;

// 라이트 시점 깊이맵과 비교해 그림자 여부를 구함. sampler2DShadow +
// GL_COMPARE_REF_TO_TEXTURE 덕분에 texture() 호출 한 번으로 하드웨어 2x2 PCF가
// 적용되어(0=완전 그림자, 1=완전 노출) 가는 머리카락 특유의 뚝뚝 끊기는 하드 섀도우를
// 어느 정도 완화해준다.
// 픽셀마다 다른 회전각을 주는 노이즈. 해상도/PCF만으로는 못 지운 블록 패턴을
// "규칙적인 얼룩"에서 "불규칙한 미세 노이즈"로 바꿔서 훨씬 덜 눈에 띄게 만든다
// (그림자 자체 강도/범위는 그대로 유지한 채 샘플링 패턴만 흐트러뜨림).
float InterleavedGradientNoise(vec2 uv) {
    return fract(52.9829189 * fract(dot(uv, vec2(0.06711056, 0.00583715))));
}

float ShadowCalc(vec4 fragPosLightSpace, vec3 N, vec3 L) {
    vec3 proj = fragPosLightSpace.xyz / fragPosLightSpace.w;
    proj = proj * 0.5 + 0.5;
    if (proj.z > 1.0 || proj.x < 0.0 || proj.x > 1.0 || proj.y < 0.0 || proj.y > 1.0) {
        return 1.0; // 섀도우맵 바깥은 그림자 없음으로 취급
    }
    // 가는 선(hair)이라 뎁스 정밀도 오차로 생기는 셀프 셰도잉 얼룩(shadow acne)이
    // 특히 잘 보여서, 표면 각도에 따라 바이어스를 늘려 완화한다(슬로프 스케일 바이어스).
    float bias = max(0.02 * (1.0 - dot(N, L)), 0.01);

    // 머리카락 한 가닥은 섀도우맵 텍셀 하나보다 훨씬 가늘어서, 여러 가닥이 텍셀
    // 하나에 겹치면 그림자가 부드럽게 이어지지 못하고 블록처럼 뭉개져 보인다
    // (특히 앞머리처럼 가닥이 촘촘한 곳). 하드웨어 2x2 PCF만으로는 부족해서
    // 텍셀 여러 개를 넓게 훑어 평균 내는 수동 PCF를 추가로 더하고, 샘플링 패턴을
    // 픽셀마다 회전시켜 남은 블록 무늬를 미세한 노이즈로 흩뜨린다.
    vec2 texelSize = 1.0 / vec2(textureSize(shadowMap, 0));
    float angle = InterleavedGradientNoise(gl_FragCoord.xy) * 6.28318;
    float s = sin(angle), c = cos(angle);
    mat2 rot = mat2(c, -s, s, c);

    float shadow = 0.0;
    const int R = 3; // (2R+1)^2 = 7x7 샘플
    for (int dx = -R; dx <= R; ++dx) {
        for (int dy = -R; dy <= R; ++dy) {
            vec2 offset = rot * (vec2(float(dx), float(dy)) * texelSize);
            shadow += texture(shadowMap, vec3(proj.xy + offset, proj.z - bias));
        }
    }
    return shadow / 49.0;
}

float random(vec2 p) {
    return fract(sin(dot(p, vec2(12.9898, 78.233))) * 43758.5453);
}



float noise(vec2 p) {
    vec2 i = floor(p);  // 정수 격자점 (Grid)
    vec2 f = fract(p);  // 소수점 (격자 내 위치, 0.0 ~ 1.0)
    
    vec2 u;
    if (u_InterpMode == 1) {
        // [실험군] 에르미트 보간 (Cubic Hermite Curve / Smoothstep)
        // 3t^2 - 2t^3 공식을 사용하여 경계선에서의 기울기를 0으로 만들어 격자 자국을 완전히 지웁니다.
        u = f * f * (3.0 - 2.0 * f); 
    } else {
        // [대조군] 선형 보간 (Linear)
        // 값은 변환 없이 그대로 사용. 다이아몬드 형태의 날카로운 타일 경계선이 노출됩니다.
        u = f; 
    }
    
    return mix(mix(random(i + vec2(0.0, 0.0)), random(i + vec2(1.0, 0.0)), u.x),
               mix(random(i + vec2(0.0, 1.0)), random(i + vec2(1.0, 1.0)), u.x), u.y);
}



uniform float AmbientCol;
uniform float DiffuseCol;
uniform float Rcol;
uniform float TTcol;

void main() {

    // 1단계: 전체 데미지를 바탕으로 '현재 가닥'의 데미지 계산 (짧은 머리 보호)
    float lengthWeight = smoothstep(0.2, 0.5, strandLength);
    float finalDamage = u_Damage * lengthWeight; 

    // --- [1. 절단 (머리 끊어짐) 로직] ---
    float finalAlpha = 1.0;

    if (finalDamage > 0.0) {
        float maxBreakage = 0.65; 
        float skewedRandom = pow(strandRandom, 0.3);
        // u_Damage 대신 finalDamage 사용
        float cutThreshold = 1.0 - (skewedRandom * maxBreakage * finalDamage);

        float frayArea = smoothstep(cutThreshold - 0.1, cutThreshold, strandParam);

        // 고주파 노이즈 생성.숫자를 크게 곱할수록 노이즈가 미세해져 얇게 찢어진 것 처럼 보임.
        vec2 frayUV = vec2(strandRandom * 100.0, strandParam * 50.0);

        float frayNoise = noise(frayUV);


        float alphaMask = mix(1.0, frayNoise, frayArea);



        // 기존의 부드러운 싹둑 자르기와 결합
        float cutFade = 1.0 - smoothstep(cutThreshold - 0.02, cutThreshold, strandParam);

        finalAlpha = alphaMask * cutFade;

        // [모드 2] 대조군 A: 가위로 싹둑 자르기 (방금 하신 것, 줌인해야 단면이 보임)
        //finalAlpha = 1.0 - smoothstep(cutThreshold - 0.02, cutThreshold, strandParam);

    }



    if (finalAlpha <= 0.05) {

        discard; 

    }



    // 2단계: 가닥 내에서 '현재 픽셀(위치)'의 데미지 계산 (뿌리 쪽 보호)
    float localDamage = finalDamage * smoothstep(0.0, 1.0, strandParam);



    // --- [2. 빛 반사 계수 로직] ---
    // 손상된 모발은 큐티클이 들떠서 매끄러운 정반사/투과(TT)가 줄고 거친 난반사(diffuse)가
    // 느는 게 실제 현상이라 방향 자체는 맞다. 다만 TT가 거의 0까지 꺼지는 데 비해 diffuse
    // 증가폭이 작아서, 색을 실어나르는 총 가중치가 3~4배 줄어버려 탈색된 밝은 색(현재
    // currentBaseColor)이 화면에 거의 반영되지 않는 문제가 있었다. diffuse 보정폭을 올려
    // TT가 잃은 만큼을 diffuse가 이어받도록 해서, 손상돼도 "색 자체는 보이되 무광"이 되게 함.
    float currentRcol = Rcol * mix(1.0, 0.3, localDamage * 0.7);
    float currentTTcol = TTcol * (1.0 - localDamage * 0.95);
    float currentDiffuseCol = DiffuseCol * (1.0 + localDamage * 6.0);



    // 시선 방향(E)을 먼저 계산합니다.
    vec3 E = normalize(eye_world - interpSurfPosition.xyz);



    // 1. 큐티클 노이즈 생성 (현미경으로 본 비늘 질감)
    vec2 cuticleUV = vec2(strandRandom * 100.0, strandParam * 200.0);
    float cuticleNoise = (noise(cuticleUV) - 0.5) * 2.0;


    // 2. 비늘이 일어나는 각도(Shift) 계산
    float cuticleShift = 0.05 + (cuticleNoise * localDamage * 1.0);


    // 3. 접선(Tangent) 벡터 비틀기
    vec3 shiftedTangent = interpSurfTangent + (E * cuticleShift);



    // 4. [최종 결 방향 T 확정]
    vec3 T = normalize(shiftedTangent + vec3(cuticleNoise * localDamage * 0.3));


    // --- [3. 탈색 (옴브레) 로직 — 이중 색소(유멜라닌/페오멜라닌) Beer-Lambert 모델] ---

    // 실제 모발 손상(자외선 누적, 반복된 열/화학 처리)은 두피에서 갓 자라난 뿌리보다
    // 성장 기간이 길어 노출이 누적된 모발 끝(tip)에 훨씬 강하게 나타난다 (weathering).
    // 두상 위에서 좌우로 얼마나 떨어져 있는지(월드 좌표 기반 outerBleach)는 그 가닥이
    // "얼마나 오래 노출됐는지"와 물리적으로 무관하고, 덧셈으로 합치면 옆쪽 뿌리의
    // 보호까지 뚫려버리는 부작용이 있어 제거했다. 대신 실제로 존재하는 개인차 —
    // 빗질/스타일링 편차로 가닥마다 손상 누적이 다른 현상 — 을 strandRandom으로 반영한다.
    float tipWeight = smoothstep(0.15, 1.0, strandParam);
    float strandVariance = mix(0.7, 1.3, strandRandom);
    float finalBleach = clamp(finalDamage * tipWeight * strandVariance, 0.0, 1.0);

    // d'Eon et al. 2011 기준 상대 흡수 계수 (pbrt에서도 쓰이는 값)
    const vec3 sigmaEumelanin   = vec3(0.419, 0.697, 1.37);
    const vec3 sigmaPheomelanin = vec3(0.187, 0.4,   1.05);

    // 원본 색상에서 초기 색소 농도 역추정: 어두울수록 총 멜라닌 많음,
    // 붉은기가 강할수록(R이 B보다 높을수록) 페오멜라닌 비중이 높다고 가정
    float luma   = dot(color, vec3(0.299, 0.587, 0.114));
    float warmth = clamp((color.r - color.b) * 2.0, 0.0, 1.0);
    float totalMelaninBase   = mix(1.6, 0.3, luma);
    float eumelanin0   = totalMelaninBase * mix(0.9, 0.55, warmth);
    float pheomelanin0 = totalMelaninBase * mix(0.1, 0.45, warmth);

    // 손상도에 따라 서로 다른 속도로 파괴: 유멜라닌이 훨씬 빠르게 소실됨
    float eumelaninConc   = eumelanin0   * exp(-3.0 * finalBleach);
    // 페오멜라닌은 천천히 소실되고, 완전히 0이 되지 않도록 소량 잔존(floor)시켜
    // 극손상 시에도 순백이 아닌 옅은 금발/애쉬 톤에서 멈추게 함
    float pheomelaninConc = pheomelanin0 * exp(-0.6 * finalBleach) + 0.02;

    vec3 sigmaA = eumelaninConc * sigmaEumelanin + pheomelaninConc * sigmaPheomelanin;
    vec3 bleachedColor = exp(-sigmaA); // 색소가 줄수록 1(밝음)에 수렴 — 방향 수정됨

    // 이 한 줄이 지우신 코드를 완벽히 대체합니다!
    vec3 currentBaseColor = mix(color, bleachedColor, finalBleach);

    // --- [4. 최종 라이팅 로직 (조명 방향 L만 추가로 구함)] --
    vec3 L = normalize(lightPosition.xyz - interpSurfPosition.xyz);

    //셰이더 기반 가짜 그림자 생성(안쪽머리는 어두움0.0, 바깥은 밝음1.0)
    float distFromCenter = length(interpSurfPosition.xz);
    float depthShadow = smoothstep(0.2, 0.9, distFromCenter);

    //손상도에 비례해 그림자가 더 강해짐. 부스스해지면서 대비감이 강해지고 그림자 강도도 쎄짐.
    float shadowIntensity = mix(1.0, depthShadow, 0.6 + (finalDamage * 0.4));

    // 섀도우맵 기반 실제 그림자(FBO 깊이 텍스처 비교). T를 "표면 방향" 근사로 사용해
    // 바이어스를 살짝 보정한다(선 형태라 진짜 표면 노멀은 존재하지 않음).
    // 완전히 0으로 떨어뜨리면 그림자 진 머리가 순수 검정이 되어 부자연스러워서,
    // 주변광이 살짝 도는 것처럼 0.35를 바닥값으로 둔다.
    float realShadow = ShadowCalc(fragPosLightSpace, T, L);
    float shadowFactor = mix(0.72, 1.0, realShadow);



    vec2 fuv1;

    vec3 angles;

    fuv1.x = dot(L, T); 

    fuv1.y = dot(E, T); 

    angles.xy = 0.5 + 0.5 * fuv1;

    

    vec3 lightPerp = L - fuv1.x * T;

    vec3 eyePerp   = E - fuv1.y * T;

    float denom = dot(eyePerp, eyePerp) * dot(lightPerp, lightPerp);

    float cosPhi = dot(E, L) * inversesqrt(max(denom, 1e-5));

    angles.z = 0.5 * cosPhi + 0.5;


    float diffuse = sqrt(max(0.0, 1.0 - fuv1.x * fuv1.x));

    vec3 diffuseColor = diffuse * currentBaseColor * currentDiffuseCol * shadowFactor;

    vec3 ambientColor = currentBaseColor * AmbientCol * (shadowIntensity * 0.5 + 0.5) * mix(0.6, 1.0, shadowFactor);

    
    vec4 m = texture(lookUp1, angles.xy);


    vec2 uv2;
    uv2.x = cos((asin(clamp(2.0 * angles.x - 1.0, -1.0, 1.0)) - asin(clamp(2.0 * angles.y - 1.0, -1.0, 1.0))) / 2.0) * 0.5 + 0.5;
    uv2.y = angles.z;
    vec4 ntt = texture(lookUp2, uv2);

   
    vec3 lighting = vec3(0.0);
    // [수정] 정반사(R lobe)는 실제로도 거의 무채색이 맞다(색소 이전에 큐티클 표면에서
    // 튕기는 프레넬 반사라 금발/흑발 상관없이 흰빛 광택선으로 보임) — 그래서 색을
    // 틴트하지 않고, 대신 하이라이트 영역 밖에서도 항상 40%는 켜져 있던 바닥값을
    // 낮춰서 좁은 광택 띠로만 집중되게 했다. 안 그러면 채도 있는 diffuse/TT 색에
    // 무채색 값이 넓게 더해져서 전체적으로 탁하게 뜨는 느낌이 났다.
    float x = m.r * ntt.a * currentRcol * mix(0.04, 1.0, shadowIntensity) * shadowFactor;
    lighting += vec3(x);


    // [버그 수정] color -> currentBaseColor 로 교체

    lighting += (m.b * ntt.rgb * currentBaseColor) * currentTTcol * shadowIntensity * shadowFactor;

    lighting += diffuseColor;

    // 헤드 셰이더와 같은 광원 색(u_LightColor)을 곱해서 두상과 헤어가 같은 조명
    // 아래 있는 것처럼 톤을 맞춘다.
    vec3 finalColor = (lighting + ambientColor) * u_LightColor;

    // [버그 수정] color -> currentBaseColor 로 교체

    if (length(currentBaseColor) < 0.001) {

        finalColor = vec3(0.0);

    }

    // [노출/톤매핑] Rcol/TTcol 등 개별 값의 상대 비율(아티스트 튜닝)은 그대로 두고
    // 최종 합산 밝기에만 노출 스칼라를 곱한 뒤 ACES 필름 커브로 눌러, 기본 프레임버퍼
    // (8비트 LDR)에서 하이라이트가 뚝 끊겨 흰 얼룩으로 뭉개지는 대신 부드럽게 롤오프되게 함.
    // TTcol=2.5로 올라간 만큼(이전 1.5 대비) 노출값도 같이 낮춰 재보정했다.
    const float exposure = 0.5;
    vec3 hdr = max(finalColor, vec3(0.0)) * exposure;
    vec3 tonemapped = clamp((hdr * (2.51 * hdr + 0.03)) / (hdr * (2.43 * hdr + 0.59) + 0.14), 0.0, 1.0);

    // [수정] 감마 보정: 선형(linear) 색상값을 보정 없이 그대로 출력하면
    // 모니터(sRGB)에서 중간톤이 실제보다 어둡게 보임. 2.2 감마로 인코딩해 보정.
    vec3 gammaCorrected = pow(tonemapped, vec3(1.0 / 2.2));
    fragColor = vec4(gammaCorrected, finalAlpha);

}