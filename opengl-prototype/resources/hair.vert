#version 330 core

// [입력]
uniform mat4 projection_mat, view_mat, model_mat;
uniform mat4 lightSpaceMatrix;
uniform float u_Damage;
uniform int u_NoiseMode;
// 지금 로드되는 .data 헤어 파일들은 정점별 색 데이터가 없어서(vertex_color는
// 항상 고정 상수) 실제 색 제어는 이 유니폼으로 한다. 키보드로 실시간 전환 가능.
uniform vec3 u_BaseHairColor;

layout (location = 0) in vec3 vertex_position;
layout (location = 1) in vec3 vertex_tangent;
layout (location = 2) in vec3 vertex_color;
layout (location = 3) in float inParam;
layout (location = 4) in float inRand;
layout (location = 5) in float inLength;

// [출력]
out vec4 v_interpSurfPosition;
out vec3 v_interpSurfTangent;
out vec3 v_color;
out float v_strandParam;
out float v_strandRandom;
out float v_strandLength;
out vec4 v_fragPosLightSpace;

// [주의] 버텍스 셰이더용 random 함수를 반드시 추가해야 에러가 안 납니다!
float random(vec2 p) {
    return fract(sin(dot(p, vec2(12.9898, 78.233))) * 43758.5453);
}

void main(void)
{
    // --- [손상된 머리 가벼워짐 & 전체적인 부피 팽창 효과] ---
    vec3 modifiedPos = vertex_position;

    float lengthWeight = smoothstep(0.2, 0.5, inLength);
    float finalDamage = u_Damage * lengthWeight;
    
    if (finalDamage > 0.0) {
        // 1. 전체적인 형태 팽창 (수평 방향으로 강하게)
        // Y축은 0으로 고정되어 있으므로 완벽하게 옆으로만 밀어내는 힘입니다.
        vec3 outwardDir = normalize(vec3(modifiedPos.x, 0.0, modifiedPos.z) + 0.0001);
        
        // 2. 가닥별 무작위 뻗침 방향 (정전기 효과)
        float randomAngle = inRand * 6.28318; 
        vec3 randomPushDir = vec3(cos(randomAngle), 0.0, sin(randomAngle));

        // 3. [핵심 수정] 방향 섞기: 위로 뜨는 힘(Y)을 확 줄이고, 옆으로 퍼지는 힘(outward)을 더 많이.
        vec3 volumeDir = normalize(outwardDir * 2.0 + randomPushDir * 1.0 + vec3(0.0, 0.1, 0.0));
        
        // -------------------------------------------------------------
        // 💡 [핵심 검증 로직] 비교군 분기 처리
        // -------------------------------------------------------------
        vec3 frizzDir;
        
        if (u_NoiseMode == 1) {
            // [실험군] 수학적 절차적 노이즈 (Procedural Noise)
            // inParam(뿌리~끝)을 삼각함수 곡선에 태워 정점간의 물리적 연속성을 보장함.
            frizzDir = vec3(
                sin(inRand * 123.4 + inParam * 20.0),
                cos(inRand * 567.8 - inParam * 15.0),
                sin(inRand * 910.1 + inParam * 25.0)
            );
        } else {
            // [대조군] 단순 난수 (White Noise)
            // 정점의 위치(inParam)마다 무작위 값을 줘서 연속성이 완전히 박살 남.
            frizzDir = vec3(
                random(vec2(inRand, inParam * 100.1)) * 2.0 - 1.0,
                random(vec2(inRand, inParam * 200.2)) * 2.0 - 1.0,
                random(vec2(inRand, inParam * 300.3)) * 2.0 - 1.0
            );
        }
        
        // 5. 윗부분부터 서서히 시작
        float effectRange = smoothstep(0.1, 1.0, inParam); 
        
        // 6. [수정됨] 방사형 팽창 파워 조절
        // 수평으로 밀어내는 방향을 잘 잡았기 때문에 liftWeight 수치는 그대로 두거나 살짝만 줄여도 충분합니다.
        float liftWeight = effectRange * 0.3 * finalDamage;  
        float frizzWeight = effectRange * 0.1 * finalDamage;
        
        modifiedPos += (volumeDir * liftWeight) + (frizzDir * frizzWeight);
    }
    // --- [효과 끝] ---

    // 1. 위치 변환 (원본 대신 변형된 modifiedPos 사용)
    v_interpSurfPosition = model_mat * vec4(modifiedPos, 1.0);

    // 1-1. 그림자맵 샘플링용 라이트 공간 좌표 (손상으로 변형된 위치 기준)
    v_fragPosLightSpace = lightSpaceMatrix * v_interpSurfPosition;

    // 2. 색상 및 파라미터 전달
    v_color = u_BaseHairColor;
    v_strandParam = inParam;
    v_strandRandom = inRand;
    v_strandLength = inLength;


    // ------------------------------------------------------------------
    // [수정 핵심] 접선(Tangent) 변환 로직 변경
    // ------------------------------------------------------------------
    // mat3(model_mat)을 곱해 모델의 회전을 적용하고, normalize로 길이를 1로 맞춤
    v_interpSurfTangent = normalize(mat3(model_mat) * vertex_tangent);

    // 3. 화면 좌표 변환
    gl_Position = projection_mat * view_mat * v_interpSurfPosition;
}