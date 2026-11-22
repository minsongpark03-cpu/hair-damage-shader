#version 330 core

layout (lines) in; 
layout (line_strip, max_vertices = 6) out; 

in vec4 v_interpSurfPosition[2];
in vec3 v_interpSurfTangent[2];
in vec3 v_color[2];
in float v_strandParam[2];
in float v_strandRandom[2];
in float v_strandLength[2];
in vec4 v_fragPosLightSpace[2];

out vec4 interpSurfPosition;
out vec3 interpSurfTangent;
out vec3 color;
out float strandParam;
out float strandRandom;
out float strandLength;
out vec4 fragPosLightSpace;

uniform float u_Damage;

void main() {
    float paramStart = v_strandParam[0];
    float paramEnd = v_strandParam[1];
    float randVal = v_strandRandom[0];
    float strandLen = v_strandLength[0]; 

    // --- [1. ���� ���� �׸��� (����)] ---
    gl_Position = gl_in[0].gl_Position;
    interpSurfPosition = v_interpSurfPosition[0];
    interpSurfTangent = v_interpSurfTangent[0];
    color = v_color[0];
    strandParam = v_strandParam[0];
    strandRandom = v_strandRandom[0];
    strandLength = v_strandLength[0];
    fragPosLightSpace = v_fragPosLightSpace[0];
    EmitVertex();

    gl_Position = gl_in[1].gl_Position;
    interpSurfPosition = v_interpSurfPosition[1];
    interpSurfTangent = v_interpSurfTangent[1];
    color = v_color[1];
    strandParam = v_strandParam[1];
    strandRandom = v_strandRandom[1];
    strandLength = v_strandLength[1];
    fragPosLightSpace = v_fragPosLightSpace[1];
    EmitVertex();
    EndPrimitive();

    // --- [2. ���� ���� �ٱ����� �������� �ؼջ� ���� ��ȭ] ---
    // �� �Ӹ� ���ַ�, ���� 10% ����(0.9 �̻�)������ �ߵ�
    float splitThreshold = 1.0 - (0.1 * u_Damage); 

    if (strandLen > 0.45 && paramEnd > splitThreshold && u_Damage > 0.0) {
        
        // �ջ󵵿� ����� Ȯ�������� ������ �߻�
        if (randVal < (u_Damage * 1.5)) {
            
            // ù ��° ���� (���� ������ ��� �κ� ������ �ٱ����� �̼��ϰ� ��Ż)
            gl_Position = gl_in[0].gl_Position;
            interpSurfPosition = v_interpSurfPosition[0];
            interpSurfTangent = v_interpSurfTangent[0];
            color = v_color[0];
            strandParam = v_strandParam[0];
            strandRandom = v_strandRandom[0];
            strandLength = v_strandLength[0];
            fragPosLightSpace = v_fragPosLightSpace[0];
            EmitVertex();

            // �� �ٽ�: ���� �Ӹ� �帧(Tangent) ���͸� ���̽��� ���, 
            // �ٱ���(���� ����) �������� ��¦ �о�� '�ڿ������� ��ȭ' ����
            vec3 tangent = normalize(v_interpSurfTangent[1]);
            vec3 upVector = vec3(0.0, 1.0, 0.0);
            // [����] tangent�� upVector�� ���� �����ϸ�(��� �þ��� ������ ���κ�)
            // cross(tangent, upVector)�� 0���Ϳ� ������� normalize()�� NaN�� ���Ƿ�
            // �� ��쿣 ���� ���� �ٲ㼭 Ư����(degenerate case)�� ���Ѵ�
            if (abs(dot(tangent, upVector)) > 0.98) {
                upVector = vec3(1.0, 0.0, 0.0);
            }
            vec3 outwardDir = normalize(cross(tangent, upVector)); 

            // ���ڸ��� �������� ������ ���� �ٸ��� ȸ���ϵ��� ���� ����
            float angleOffset = randVal * 6.28318 + paramEnd * 17.0;
            vec3 spreadDir = normalize(tangent + outwardDir * (sin(angleOffset) * 1.5));

            // ���� ������ �־� ���� �ʰ� ��� ������ �ܰ��� ���� ����
            // [����] ���� ������ ���׸�Ʈ�� ���� �� ���� �־ �������� ����
            // ���� ������ �ѹ��� Ƣ�� �ʵ���, splitThreshold~tip(1.0) ������
            // �����(splitProgress)�� ����� ���� ���������� ��
            float splitProgress = clamp((paramEnd - splitThreshold) / max(0.001, 1.0 - splitThreshold), 0.0, 1.0);
            float splitWidth = u_Damage * 0.035 * mix(0.55, 1.0, splitProgress);
            vec4 splitOffset = vec4(spreadDir * splitWidth, 0.0);
            
            gl_Position = gl_in[1].gl_Position + splitOffset;
            interpSurfPosition = v_interpSurfPosition[1];
            interpSurfTangent = v_interpSurfTangent[1];
            // [����] ������ ������ �������� �� ���ð� ���� ���̵��� ���� ��¦ ��� Ʒ����
            color = v_color[1] * 1.15 + vec3(0.05);
            strandParam = v_strandParam[1];
            strandRandom = v_strandRandom[1];
            strandLength = v_strandLength[1];
            fragPosLightSpace = v_fragPosLightSpace[1];
            EmitVertex();
            EndPrimitive();
        }
    }
}