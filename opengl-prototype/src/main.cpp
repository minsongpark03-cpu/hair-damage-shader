// src/main.cpp
#define GLFW_INCLUDE_NONE
#include <glad/glad.h>
#include <GLFW/glfw3.h>

#include <glm/glm.hpp>
#include <glm/gtc/matrix_transform.hpp>

#include <fstream>
#include <sstream>
#include <iostream>
#include <vector>
#include <stdexcept>
#include <string>
#include <cfloat>
#include <algorithm>
#include <cmath>
#include <algorithm>

#define STB_IMAGE_IMPLEMENTATION
#include "stb_image.h"

#ifdef _WIN32
#define WIN32_LEAN_AND_MEAN
#define NOMINMAX
#include <windows.h>
#endif

// -------------------- cyHairFile (헤어 파일 로더) --------------------
#include "cyHairFile.h"

// ------------------------ 전역 설정 ------------------------
int g_showMode = 1; // 1~6 선택
float g_damage = 0.0f; // <-- 초기 손상도는 0.0 (완전 건강한 생머리)

// [추가됨] 노이즈 비교군을 위한 전역 변수 (1: 수학적 노이즈, 0: 단순 난수)
int g_noiseMode = 1;
int g_interpMode = 1;

// --- 두상 위치/크기 조절용 전역 변수 ---
float g_headOffsetX = 0.0f;
float g_headOffsetY = 0.34f;
float g_headOffsetZ = 0.04f;
float g_headScale = 0.8f;

// --- 머리색 프리셋: 9번 키로 순환 전환. 탈색 계산은 marschnerHair.frag에서
// 이 색의 luma/warmth를 보고 유멜라닌·페오멜라닌 비율을 역추정하므로,
// 여기서 색을 바꾸면 손상 시 탈색되는 색감도 그에 맞춰 자동으로 달라진다. ---
struct HairColorPreset { const char* name; glm::vec3 color; };
// 실제 사람이 자연적으로 가질 수 있는 모발색 4종. R-B 색차(warmth)가 클수록
// marschnerHair.frag의 탈색 계산에서 페오멜라닌 비중이 높게 잡히므로,
// 붉은 계열일수록 R을 B보다 훨씬 높게 잡아 실제 모발 색소 특성을 반영했다.
const HairColorPreset g_hairColorPresets[] = {
    { "고동색 (다크 체스트넛)", glm::vec3(0.14f, 0.07f, 0.045f) },
    { "밝은 갈색",              glm::vec3(0.50f, 0.32f, 0.18f) },
    { "붉은색 (오렌지 머리)",    glm::vec3(0.62f, 0.24f, 0.12f) },
    { "금발",                  glm::vec3(0.75f, 0.58f, 0.32f) },
};
const int g_hairColorPresetCount = sizeof(g_hairColorPresets) / sizeof(g_hairColorPresets[0]);
int g_hairColorIndex = 0;

// ------------------------ 마우스/키보드 컨트롤 ------------------------
struct CameraControl {
    float yaw = 0.0f;
    float pitch = 0.0f;
    float dist = 2.5f;

    bool rotating = false; // LMB drag
    bool zooming = false;  // RMB drag
    bool panning = false;  // [추가됨] 휠 버튼(MMB) drag 상태

    // [추가됨] 카메라가 바라보는 중심점 (기본값 0,0,0)
    glm::vec3 target = glm::vec3(0.0f, 0.0f, 0.0f);

    double lastX = 0.0;
    double lastY = 0.0;
};



static void keyCallback(GLFWwindow* window, int key, int scancode, int action, int mods) {
    (void)scancode; (void)mods;
    if (action == GLFW_PRESS || action == GLFW_REPEAT) { // 꾹 누르고 있어도 작동하도록 REPEAT 추가
        if (key == GLFW_KEY_ESCAPE) glfwSetWindowShouldClose(window, 1);

        // 키보드 1~6번 입력 처리
        if (key >= GLFW_KEY_1 && key <= GLFW_KEY_6) {
            g_showMode = key - GLFW_KEY_0; // '1' -> 1, '6' -> 6
            std::cout << "[Info] Switched to Mode: " << g_showMode << "\n";
        }

        if (key == GLFW_KEY_7) {
            g_noiseMode = (g_noiseMode == 1) ? 0 : 1;
            if (g_noiseMode == 1) {
                std::cout << "[Info] Noise Mode: Procedural (Smooth Sine Wave)\n";
            }
            else {
                std::cout << "[Info] Noise Mode: Pure Random (White Noise/Jagged)\n";
            }
        }

        if (key == GLFW_KEY_8) {
            g_interpMode = (g_interpMode == 1) ? 0 : 1;
            if (g_interpMode == 1) {
                std::cout << "[Info] Interpolation: Hermite (Smoothstep) - Seamless Boundaries\n";
            }
            else {
                std::cout << "[Info] Interpolation: Linear - Visible Grid Boundaries\n";
            }
        }

        if (key == GLFW_KEY_9) {
            g_hairColorIndex = (g_hairColorIndex + 1) % g_hairColorPresetCount;
            std::cout << "[Info] Hair Color: " << g_hairColorPresets[g_hairColorIndex].name << "\n";
        }

        if (key == GLFW_KEY_UP) {
            g_damage += 0.05f;
            if (g_damage > 1.0f) g_damage = 1.0f; // 최대 1.0
            std::cout << "[Info] Damage Level: " << g_damage << "\n";
        }
        if (key == GLFW_KEY_DOWN) {
            g_damage -= 0.05f;
            if (g_damage < 0.0f) g_damage = 0.0f; // 최소 0.0
            std::cout << "[Info] Damage Level: " << g_damage << "\n";
        }

        float moveSpeed = 0.02f;
        if (key == GLFW_KEY_A) g_headOffsetX -= moveSpeed; // 좌
        if (key == GLFW_KEY_D) g_headOffsetX += moveSpeed; // 우
        if (key == GLFW_KEY_W) g_headOffsetY += moveSpeed; // 상
        if (key == GLFW_KEY_S) g_headOffsetY -= moveSpeed; // 하
        if (key == GLFW_KEY_Q) g_headOffsetZ -= moveSpeed; // 뒤
        if (key == GLFW_KEY_E) g_headOffsetZ += moveSpeed; // 앞

        if (key == GLFW_KEY_Z) g_headScale -= moveSpeed; // 축소
        if (key == GLFW_KEY_X) g_headScale += moveSpeed; // 확대

        // 조절할 때마다 콘솔에 현재 수치 출력 
        if (key >= GLFW_KEY_A && key <= GLFW_KEY_Z) {
            std::cout << "Head Transform -> Pos: (" << g_headOffsetX << ", " << g_headOffsetY << ", " << g_headOffsetZ << ") | Scale: " << g_headScale << "\n";
        }
    }
}

static void mouseButtonCallback(GLFWwindow* window, int button, int action, int mods) {
    (void)mods;
    auto* cam = reinterpret_cast<CameraControl*>(glfwGetWindowUserPointer(window));
    if (!cam) return;

    if (button == GLFW_MOUSE_BUTTON_LEFT) {
        if (action == GLFW_PRESS) {
            cam->rotating = true;
            glfwGetCursorPos(window, &cam->lastX, &cam->lastY);
        }
        else if (action == GLFW_RELEASE) {
            cam->rotating = false;
        }
    }

    if (button == GLFW_MOUSE_BUTTON_RIGHT) {
        if (action == GLFW_PRESS) {
            cam->zooming = true;
            glfwGetCursorPos(window, &cam->lastX, &cam->lastY);
        }
        else if (action == GLFW_RELEASE) {
            cam->zooming = false;
        }
    }

    if (button == GLFW_MOUSE_BUTTON_MIDDLE) {
        if (action == GLFW_PRESS) {
            cam->panning = true;
            glfwGetCursorPos(window, &cam->lastX, &cam->lastY);
        }
        else if (action == GLFW_RELEASE) {
            cam->panning = false;
        }
    }
}

static void cursorPosCallback(GLFWwindow* window, double x, double y) {
    auto* cam = reinterpret_cast<CameraControl*>(glfwGetWindowUserPointer(window));
    if (!cam) return;

    double dx = x - cam->lastX;
    double dy = y - cam->lastY;
    cam->lastX = x;
    cam->lastY = y;

    const float rotSpeed = 0.005f;
    const float zoomSpeed = 0.01f;

    if (cam->rotating) {
        cam->yaw += (float)dx * rotSpeed;
        cam->pitch -= (float)dy * rotSpeed;
        cam->pitch = glm::clamp(cam->pitch, -1.2f, 1.2f);
    }

    if (cam->zooming) {
        cam->dist *= std::exp((float)dy * zoomSpeed);
        cam->dist = glm::clamp(cam->dist, 0.3f, 20.0f);
    }

    if (cam->panning) {
        float panSpeed = 0.0015f * cam->dist;

        glm::vec3 dir(
            cosf(cam->pitch) * sinf(cam->yaw),
            sinf(cam->pitch),
            cosf(cam->pitch) * cosf(cam->yaw)
        );
        glm::vec3 right = glm::normalize(glm::cross(glm::vec3(0.0f, 1.0f, 0.0f), dir));
        glm::vec3 up = glm::normalize(glm::cross(dir, right));

        cam->target -= right * (float)(dx * panSpeed);
        cam->target += up * (float)(dy * panSpeed);
    }
}

static void scrollCallback(GLFWwindow* window, double xoffset, double yoffset) {
    (void)xoffset;
    auto* cam = reinterpret_cast<CameraControl*>(glfwGetWindowUserPointer(window));
    if (!cam) return;

    const float wheelZoom = 0.10f;
    cam->dist *= std::exp((float)(-yoffset) * wheelZoom);
    cam->dist = glm::clamp(cam->dist, 0.3f, 20.0f);
}



// ------------------------ 유틸 ------------------------
static std::string loadText(const std::string& path) {
    std::ifstream ifs(path, std::ios::binary);
    if (!ifs) throw std::runtime_error("Failed to open: " + path);
    std::stringstream ss; ss << ifs.rdbuf();
    std::string s = ss.str();
    if (s.size() >= 3 && (unsigned char)s[0] == 0xEF && (unsigned char)s[1] == 0xBB && (unsigned char)s[2] == 0xBF)
        s.erase(0, 3);
    return s;
}

static GLuint compile(GLenum type, const std::string& src) {
    GLuint s = glCreateShader(type);
    const char* c = src.c_str();
    glShaderSource(s, 1, &c, nullptr);
    glCompileShader(s);

    GLint ok = 0;
    glGetShaderiv(s, GL_COMPILE_STATUS, &ok);
    if (!ok) {
        GLint len = 0;
        glGetShaderiv(s, GL_INFO_LOG_LENGTH, &len);
        std::string log(len, '\0');
        glGetShaderInfoLog(s, len, nullptr, log.data());
        throw std::runtime_error("Shader compile failed:\n" + log);
    }
    return s;
}

static GLuint link(GLuint vs, GLuint fs) {
    GLuint p = glCreateProgram();
    glAttachShader(p, vs);
    glAttachShader(p, fs);
    glLinkProgram(p);

    GLint ok = 0;
    glGetProgramiv(p, GL_LINK_STATUS, &ok);
    if (!ok) {
        GLint len = 0;
        glGetProgramiv(p, GL_INFO_LOG_LENGTH, &len);
        std::string log(len, '\0');
        glGetProgramInfoLog(p, len, nullptr, log.data());
        throw std::runtime_error("Program link failed:\n" + log);
    }
    return p;
}

// [추가할 부분] 3개의 셰이더(VS, GS, FS)를 연결하는 link 함수 오버로딩
static GLuint link(GLuint vs, GLuint gs, GLuint fs) {
    GLuint p = glCreateProgram();
    glAttachShader(p, vs);
    glAttachShader(p, gs); // 지오메트리 셰이더 부착
    glAttachShader(p, fs);
    glLinkProgram(p);

    GLint ok = 0;
    glGetProgramiv(p, GL_LINK_STATUS, &ok);
    if (!ok) {
        GLint len = 0;
        glGetProgramiv(p, GL_INFO_LOG_LENGTH, &len);
        std::string log(len, '\0');
        glGetProgramInfoLog(p, len, nullptr, log.data());
        throw std::runtime_error("Program link failed:\n" + log);
    }
    return p;
}

static GLuint loadTexture2D(const char* path, bool srgb = false) {
    int w = 0, h = 0, n = 0;
    stbi_set_flip_vertically_on_load(true);
    stbi_uc* data = stbi_load(path, &w, &h, &n, 4);
    if (!data) throw std::runtime_error(std::string("Failed to load PNG: ") + path);

    GLuint tex = 0;
    glGenTextures(1, &tex);
    glBindTexture(GL_TEXTURE_2D, tex);

    glPixelStorei(GL_UNPACK_ALIGNMENT, 1);
    GLenum internalFmt = srgb ? GL_SRGB8_ALPHA8 : GL_RGBA8;
    glTexImage2D(GL_TEXTURE_2D, 0, internalFmt, w, h, 0, GL_RGBA, GL_UNSIGNED_BYTE, data);

    glTexParameteri(GL_TEXTURE_2D, GL_TEXTURE_MIN_FILTER, GL_LINEAR);
    glTexParameteri(GL_TEXTURE_2D, GL_TEXTURE_MAG_FILTER, GL_LINEAR);

    glTexParameteri(GL_TEXTURE_2D, GL_TEXTURE_WRAP_S, GL_CLAMP_TO_EDGE);
    glTexParameteri(GL_TEXTURE_2D, GL_TEXTURE_WRAP_T, GL_CLAMP_TO_EDGE);

    stbi_image_free(data);
    glBindTexture(GL_TEXTURE_2D, 0);
    return tex;
}

// -------------------- GPU 버퍼 관리 --------------------
struct HairGPU {
    GLuint vao = 0, vboPos = 0, vboTan = 0, vboCol = 0, vboParam = 0, vboRand = 0, vboLen = 0;
    std::vector<GLint> starts, counts;
};

// 메쉬 데이터 컨테이너
struct HairMesh {
    bool loaded = false; // 로드 성공 여부
    std::string name;    // 식별용 이름
    std::vector<glm::vec3> P; // 위치
    std::vector<glm::vec3> T; // 접선
    std::vector<glm::vec3> C; // 색상 (옵션)
    std::vector<float> param; // <-- 뿌리(0.0)~끝(1.0) 길이를 담을 배열 추가
    std::vector<float> strandRandom;
    std::vector<float> strandLength;
    HairGPU gpu;
    glm::mat4 modelMat;
    glm::mat3 tangentMat;
};

struct HeadMesh {
    bool loaded = false;
    GLuint vao = 0, vboPos = 0, vboNormal = 0; // vboNormal 추가
    int vertexCount = 0;
    std::vector<glm::vec3> P; // 위치
    std::vector<glm::vec3> N; // 법선(Normal) 배열
    glm::mat4 modelMat = glm::mat4(1.0f);
};

void NormalizeHairData(std::vector<glm::vec3>& points);

// -------------------- 로더 & 계산 함수 --------------------

void normalizeHairMesh(HairMesh& mesh) {
    if (mesh.P.empty()) return;

    float fixedScale = 5.0f;

    float baseHeight = 1.65f;
    glm::vec3 globalOffset(0.0f, -baseHeight * fixedScale, 0.0f);

    for (auto& p : mesh.P) {
        p *= fixedScale;      // 크기 뻥튀기!
        p += globalOffset;    // 화면 중앙으로 강제 소환!
    }
}

bool loadHairFile(const char* path, HairMesh& mesh) {
    cyHairFile hair;
    if (hair.LoadFromFile(path) == 0) return false;

    const unsigned int Np = hair.GetHeader().point_count;
    const unsigned int H = hair.GetHeader().hair_count;
    const float* Praw = hair.GetPointsArray();

    mesh.P.resize(Np);
    for (unsigned int i = 0; i < Np; ++i) {
        mesh.P[i] = { Praw[3 * i + 0], Praw[3 * i + 1], Praw[3 * i + 2] };
    }

    mesh.gpu.starts.clear();
    mesh.gpu.counts.clear();
    if (const unsigned short* Seg = hair.GetSegmentsArray()) {
        GLint off = 0;
        mesh.gpu.starts.reserve(H); mesh.gpu.counts.reserve(H);
        for (unsigned int h = 0; h < H; ++h) {
            GLint cnt = (GLint)Seg[h] + 1;
            mesh.gpu.starts.push_back(off);
            mesh.gpu.counts.push_back(cnt);
            off += cnt;
        }
    }
    else {
        if (H == 0 || Np % H != 0) return false;
        GLint cnt = (GLint)(Np / H), off = 0;
        mesh.gpu.starts.reserve(H); mesh.gpu.counts.reserve(H);
        for (unsigned int h = 0; h < H; ++h) {
            mesh.gpu.starts.push_back(off);
            mesh.gpu.counts.push_back(cnt);
            off += cnt;
        }
    }
    return true;
}

bool loadHairTXT(const char* path, HairMesh& mesh) {
    std::ifstream ifs(path);
    if (!ifs.is_open()) return false;

    int numStrands = 0;
    if (!(ifs >> numStrands)) return false;

    mesh.P.clear();
    mesh.gpu.starts.clear();
    mesh.gpu.counts.clear();

    mesh.gpu.starts.reserve(numStrands);
    mesh.gpu.counts.reserve(numStrands);

    int currentOffset = 0;
    // 첫 줄의 가닥 수만큼 반복
    for (int i = 0; i < numStrands; ++i) {
        int nPoints = 0;
        if (!(ifs >> nPoints)) break;

        mesh.gpu.starts.push_back(currentOffset);
        mesh.gpu.counts.push_back(nPoints);
        currentOffset += nPoints;

        for (int k = 0; k < nPoints; ++k) {
            float x, y, z;
            ifs >> x >> y >> z;
            mesh.P.push_back(glm::vec3(x, y, z));
        }
    }
    return !mesh.P.empty();
}

bool loadHairDATA(const char* path, HairMesh& mesh) {
    std::ifstream ifs(path, std::ios::binary);
    if (!ifs.is_open()) {
        std::cerr << "[Error] Failed to open DATA file: " << path << "\n";
        return false;
    }

    // 1. 맨 처음 4바이트: 전체 가닥 수(int) 읽기
    unsigned int numStrands = 0;
    ifs.read(reinterpret_cast<char*>(&numStrands), sizeof(unsigned int));

    // 안전장치 (파일이 비정상이면 중단)
    if (numStrands == 0 || numStrands > 1000000) return false;

    mesh.P.clear();
    mesh.gpu.starts.clear();
    mesh.gpu.counts.clear();

    int currentOffset = 0;
    for (unsigned int i = 0; i < numStrands; ++i) {

        // 2. 가닥이 시작될 때마다 들어있는 4바이트: 점의 개수(int) 읽기
        unsigned int numPoints = 0;
        ifs.read(reinterpret_cast<char*>(&numPoints), sizeof(unsigned int));

        // 안전장치
        if (numPoints == 0 || numPoints > 10000) break;

        mesh.gpu.starts.push_back(currentOffset);
        mesh.gpu.counts.push_back(numPoints);

        // 3. 점의 개수만큼 정직하게 X, Y, Z 좌표(float) 읽기
        for (unsigned int k = 0; k < numPoints; ++k) {
            float v[3];
            ifs.read(reinterpret_cast<char*>(v), sizeof(float) * 3);

            // 데이터 오염 방지
            if (std::isnan(v[0]) || std::isinf(v[0])) v[0] = 0.0f;
            if (std::isnan(v[1]) || std::isinf(v[1])) v[1] = 0.0f;
            if (std::isnan(v[2]) || std::isinf(v[2])) v[2] = 0.0f;

            mesh.P.push_back(glm::vec3(v[0], v[1], v[2]));
        }
        currentOffset += numPoints;
    }


    std::cout << "[Info] Perfect Binary Loaded! Strands: " << numStrands << "\n";
    return !mesh.P.empty();
}

bool loadHeadOBJ(const char* path, HeadMesh& mesh) {
    std::ifstream ifs(path);
    if (!ifs.is_open()) {
        std::cerr << "[Error] Failed to open OBJ: " << path << "\n";
        return false;
    }

    std::vector<glm::vec3> temp_v;
    std::string line;

    while (std::getline(ifs, line)) {
        std::stringstream ss(line);
        std::string type;
        ss >> type;

        if (type == "v") {
            float x, y, z;
            ss >> x >> y >> z;
            temp_v.push_back(glm::vec3(x, y, z));
        }
        else if (type == "f") {
            std::string v1, v2, v3, v4;
            ss >> v1 >> v2 >> v3;
            auto getIndex = [](const std::string& token) {
                return std::stoi(token.substr(0, token.find('/'))) - 1;
                };

            mesh.P.push_back(temp_v[getIndex(v1)]);
            mesh.P.push_back(temp_v[getIndex(v2)]);
            mesh.P.push_back(temp_v[getIndex(v3)]);

            if (ss >> v4) {
                mesh.P.push_back(temp_v[getIndex(v1)]);
                mesh.P.push_back(temp_v[getIndex(v3)]);
                mesh.P.push_back(temp_v[getIndex(v4)]);
            }
        }
    }

    mesh.vertexCount = mesh.P.size();
    if (mesh.vertexCount == 0) return false;

    NormalizeHairData(mesh.P);

    mesh.N.assign(mesh.P.size(), glm::vec3(0.0f));

    for (size_t i = 0; i < mesh.P.size(); i += 3) {
        glm::vec3 v0 = mesh.P[i];
        glm::vec3 v1 = mesh.P[i + 1];
        glm::vec3 v2 = mesh.P[i + 2];
        glm::vec3 faceNormal = glm::normalize(glm::cross(v1 - v0, v2 - v0));
        mesh.N[i] += faceNormal;
        mesh.N[i + 1] += faceNormal;
        mesh.N[i + 2] += faceNormal;
    }

    std::vector<glm::vec3> smoothNormals = mesh.N;
    for (size_t i = 0; i < mesh.P.size(); ++i) {
        glm::vec3 sumNormal = mesh.N[i];
        for (size_t j = 0; j < mesh.P.size(); ++j) {
            if (i != j && glm::distance(mesh.P[i], mesh.P[j]) < 1e-4f) {
                sumNormal += mesh.N[j];
            }
        }
        smoothNormals[i] = glm::normalize(sumNormal);
    }
    mesh.N = smoothNormals;

    // GPU 버퍼 세팅 (위치 + 법선 데이터 전송)
    glGenVertexArrays(1, &mesh.vao);
    glBindVertexArray(mesh.vao);

    glGenBuffers(1, &mesh.vboPos);
    glBindBuffer(GL_ARRAY_BUFFER, mesh.vboPos);
    glBufferData(GL_ARRAY_BUFFER, mesh.P.size() * sizeof(glm::vec3), mesh.P.data(), GL_STATIC_DRAW);
    glEnableVertexAttribArray(0);
    glVertexAttribPointer(0, 3, GL_FLOAT, GL_FALSE, sizeof(glm::vec3), (void*)0);

    // [추가됨] GPU에 부드러운 법선 데이터 보내기
    glGenBuffers(1, &mesh.vboNormal);
    glBindBuffer(GL_ARRAY_BUFFER, mesh.vboNormal);
    glBufferData(GL_ARRAY_BUFFER, mesh.N.size() * sizeof(glm::vec3), mesh.N.data(), GL_STATIC_DRAW);
    glEnableVertexAttribArray(1);
    glVertexAttribPointer(1, 3, GL_FLOAT, GL_FALSE, sizeof(glm::vec3), (void*)0);

    glBindVertexArray(0);

    mesh.loaded = true;
    std::cout << "[Info] Loaded Head OBJ (Smooth Shading): " << mesh.vertexCount / 3 << " triangles.\n";
    return true;
}


// 3. 접선(Tangent) 계산
void computeTangents(HairMesh& mesh) {
    const size_t Np = mesh.P.size();
    mesh.T.resize(Np);

    const auto& starts = mesh.gpu.starts;
    const auto& counts = mesh.gpu.counts;

    for (size_t s = 0; s < starts.size(); ++s) {
        GLint st = starts[s], c = counts[s];
        for (GLint i = 0; i < c; ++i) {
            glm::vec3 t;
            if (i == 0)          t = mesh.P[st + 1] - mesh.P[st + 0];
            else if (i == c - 1) t = mesh.P[st + c - 1] - mesh.P[st + c - 2];
            else                 t = mesh.P[st + i + 1] - mesh.P[st + i - 1];

            float L = glm::length(t);
            mesh.T[st + i] = (L > 1e-8f) ? (t / L) : glm::vec3(0, 1, 0);
        }
    }
}

void computeStrandParams(HairMesh& mesh) {
    mesh.param.resize(mesh.P.size());
    mesh.strandRandom.resize(mesh.P.size()); // 배열 크기 할당
    mesh.strandLength.resize(mesh.P.size());

    const auto& starts = mesh.gpu.starts;
    const auto& counts = mesh.gpu.counts;

    // 가닥별 길이를 임시로 저장할 배열과, 최대 길이 변수
    std::vector<float> tempLengths(starts.size());
    float maxLength = 0.0001f;

    for (size_t s = 0; s < starts.size(); ++s) {
        GLint st = starts[s], c = counts[s];
        float totalLen = 0.0f;
        for (GLint i = 0; i < c - 1; ++i) {
            totalLen += glm::length(mesh.P[st + i + 1] - mesh.P[st + i]);
        }
        tempLengths[s] = totalLen;
        if (totalLen > maxLength) maxLength = totalLen; // 가장 긴 머리 길이 갱신
    }

    // 가닥(Strand)마다 하나의 고유한 랜덤 값 생성 (0.0 ~ 1.0)
    float randomVal = (float)rand() / (float)RAND_MAX;

    for (size_t s = 0; s < starts.size(); ++s) {
        GLint st = starts[s], c = counts[s];
        float randomVal = (float)rand() / (float)RAND_MAX;

        // 내 길이 / 가장 긴 길이 = 0.0 ~ 1.0 사이의 비율이 됨
        float normalizedLen = tempLengths[s] / maxLength;

        for (GLint i = 0; i < c; ++i) {
            mesh.param[st + i] = (float)i / (float)(std::max(1, c - 1));
            mesh.strandRandom[st + i] = randomVal;
            mesh.strandLength[st + i] = normalizedLen;
        }
    }
}

// 4. 모델 행렬 계산 (Auto-Fit)
void computeModelMatrix(HairMesh& mesh) {
    mesh.modelMat = glm::mat4(1.0f);
    mesh.tangentMat = glm::mat3(1.0f);
}

// 5. GPU 업로드
static void buildHairGPU(HairMesh& mesh)
{
    const GLuint ATTR_POS = 0, ATTR_TAN = 1, ATTR_COL = 2, ATTR_PARAM = 3, ATTR_RAND = 4, ATTR_LEN = 5;

    glGenVertexArrays(1, &mesh.gpu.vao);
    glBindVertexArray(mesh.gpu.vao);

    glGenBuffers(1, &mesh.gpu.vboPos);
    glBindBuffer(GL_ARRAY_BUFFER, mesh.gpu.vboPos);
    glBufferData(GL_ARRAY_BUFFER, mesh.P.size() * sizeof(glm::vec3), mesh.P.data(), GL_STATIC_DRAW);
    glEnableVertexAttribArray(ATTR_POS);
    glVertexAttribPointer(ATTR_POS, 3, GL_FLOAT, GL_FALSE, sizeof(glm::vec3), (void*)0);

    glGenBuffers(1, &mesh.gpu.vboTan);
    glBindBuffer(GL_ARRAY_BUFFER, mesh.gpu.vboTan);
    glBufferData(GL_ARRAY_BUFFER, mesh.T.size() * sizeof(glm::vec3), mesh.T.data(), GL_STATIC_DRAW);
    glEnableVertexAttribArray(ATTR_TAN);
    glVertexAttribPointer(ATTR_TAN, 3, GL_FLOAT, GL_FALSE, sizeof(glm::vec3), (void*)0);



    if (!mesh.C.empty()) {
        glGenBuffers(1, &mesh.gpu.vboCol);
        glBindBuffer(GL_ARRAY_BUFFER, mesh.gpu.vboCol);
        glBufferData(GL_ARRAY_BUFFER, mesh.C.size() * sizeof(glm::vec3), mesh.C.data(), GL_STATIC_DRAW);
        glEnableVertexAttribArray(ATTR_COL);
        glVertexAttribPointer(ATTR_COL, 3, GL_FLOAT, GL_FALSE, sizeof(glm::vec3), (void*)0);
    }
    else {
        glDisableVertexAttribArray(ATTR_COL);
        glVertexAttrib3f(ATTR_COL, 0.25f, 0.15f, 0.05f);
    }

    if (!mesh.param.empty()) {
        glGenBuffers(1, &mesh.gpu.vboParam);
        glBindBuffer(GL_ARRAY_BUFFER, mesh.gpu.vboParam);
        glBufferData(GL_ARRAY_BUFFER, mesh.param.size() * sizeof(float), mesh.param.data(), GL_STATIC_DRAW);
        glEnableVertexAttribArray(ATTR_PARAM);
        glVertexAttribPointer(ATTR_PARAM, 1, GL_FLOAT, GL_FALSE, sizeof(float), (void*)0);
    }

    if (!mesh.strandRandom.empty()) {
        glGenBuffers(1, &mesh.gpu.vboRand);
        glBindBuffer(GL_ARRAY_BUFFER, mesh.gpu.vboRand);
        glBufferData(GL_ARRAY_BUFFER, mesh.strandRandom.size() * sizeof(float), mesh.strandRandom.data(), GL_STATIC_DRAW);
        glEnableVertexAttribArray(ATTR_RAND);
        glVertexAttribPointer(ATTR_RAND, 1, GL_FLOAT, GL_FALSE, sizeof(float), (void*)0);
    }

    if (!mesh.strandLength.empty()) {
        glGenBuffers(1, &mesh.gpu.vboLen);
        glBindBuffer(GL_ARRAY_BUFFER, mesh.gpu.vboLen);
        glBufferData(GL_ARRAY_BUFFER, mesh.strandLength.size() * sizeof(float), mesh.strandLength.data(), GL_STATIC_DRAW);
        glEnableVertexAttribArray(ATTR_LEN);
        glVertexAttribPointer(ATTR_LEN, 1, GL_FLOAT, GL_FALSE, sizeof(float), (void*)0);
    }


    glBindVertexArray(0);
}

//정규화 함수 _ 좌표를 스케일링
void NormalizeHairData(std::vector<glm::vec3>& points) {
    if (points.empty()) return;

    glm::vec3 minBox(glm::vec3(FLT_MAX));
    glm::vec3 maxBox(glm::vec3(-FLT_MAX));

    for (const auto& p : points) {
        minBox = glm::min(minBox, p);
        maxBox = glm::max(maxBox, p);
    }


    glm::vec3 center = (minBox + maxBox) * 0.5f;
    glm::vec3 size = maxBox - minBox;
    float maxDim = std::max(std::max(size.x, size.y), size.z);

    if (maxDim <= 1e-6f) maxDim = 1.0f;
    float scale = 1.0f / (maxDim * 0.5f);


    for (auto& p : points) {
        p = (p - center) * scale;
    }

    std::cout << "[Normalize] Complete! Scale: " << scale << std::endl;
}

//최종적으로 GPU에 계산된 값들을 보냄.
void processAndUploadMesh(HairMesh& mesh, const std::string& name) {
    if (mesh.P.empty()) {
        std::cerr << "[Warning] Empty mesh: " << name << "\n";
        mesh.loaded = false;
        return;
    }
    mesh.name = name;


    // NormalizeHairData(mesh.P); 
    normalizeHairMesh(mesh);

    computeTangents(mesh);
    computeStrandParams(mesh);
    computeModelMatrix(mesh);
    buildHairGPU(mesh);
    mesh.loaded = true;
    std::cout << "[Info] Loaded " << name << ": " << mesh.P.size() << " points.\n";
}



// 탐색기에서 exe를 더블클릭하거나 VS Debug 설정(작업 디렉토리가 build/ 자체라
// resources/가 없음)으로 F5 실행하면 "resources/..." 상대경로를 못 찾고 죽는다.
// exe 위치에서부터 상위로 올라가며 resources 폴더를 찾아 그곳으로 작업
// 디렉토리를 옮겨준다.
static void establishWorkingDirectory() {
#ifdef _WIN32
    wchar_t exePath[MAX_PATH];
    if (GetModuleFileNameW(nullptr, exePath, MAX_PATH) == 0) return;

    std::wstring dir(exePath);
    for (int depth = 0; depth < 6; ++depth) {
        size_t slash = dir.find_last_of(L"\\/");
        if (slash == std::wstring::npos) return;
        dir = dir.substr(0, slash);

        std::wstring candidate = dir + L"\\resources";
        DWORD attrs = GetFileAttributesW(candidate.c_str());
        if (attrs != INVALID_FILE_ATTRIBUTES && (attrs & FILE_ATTRIBUTE_DIRECTORY)) {
            SetCurrentDirectoryW(dir.c_str());
            return;
        }
    }
#endif
}

int main() {
    establishWorkingDirectory();

    if (!glfwInit()) { std::cerr << "glfwInit failed\n"; return -1; }

    glfwWindowHint(GLFW_CONTEXT_VERSION_MAJOR, 3);
    glfwWindowHint(GLFW_CONTEXT_VERSION_MINOR, 3);
    glfwWindowHint(GLFW_OPENGL_PROFILE, GLFW_OPENGL_CORE_PROFILE);
    glfwWindowHint(GLFW_SAMPLES, 4);

    GLFWwindow* win = glfwCreateWindow(1280, 720, "Hair Switcher (1~6 keys)", nullptr, nullptr);
    if (!win) { std::cerr << "create window failed\n"; glfwTerminate(); return -1; }

    glfwMakeContextCurrent(win);
    if (!gladLoadGLLoader((GLADloadproc)glfwGetProcAddress)) {
        std::cerr << "glad load failed\n"; return -1;
    }

    CameraControl cam;
    glfwSetWindowUserPointer(win, &cam);
    glfwSetKeyCallback(win, keyCallback);
    glfwSetMouseButtonCallback(win, mouseButtonCallback);
    glfwSetCursorPosCallback(win, cursorPosCallback);
    glfwSetScrollCallback(win, scrollCallback);

    glEnable(GL_DEPTH_TEST);
    glEnable(GL_MULTISAMPLE);
    glEnable(GL_BLEND);
    glBlendFunc(GL_SRC_ALPHA, GL_ONE_MINUS_SRC_ALPHA);

    glfwSwapInterval(1);

    // 6개의 슬롯으로 벡터 관리, 6개의 머리카락 텍스트파일을 저장
    std::vector<HairMesh> meshes(6);

    try {
        std::string vsSrc = loadText("resources/hair.vert");
        std::string gsSrc = loadText("resources/hair.geom");
        std::string fsSrc = loadText("resources/marschnerHair.frag");
        GLuint vs = compile(GL_VERTEX_SHADER, vsSrc);
        GLuint gs = compile(GL_GEOMETRY_SHADER, gsSrc);
        GLuint fs = compile(GL_FRAGMENT_SHADER, fsSrc);
        GLuint prog = link(vs, gs, fs);
        glDeleteShader(vs);
        glDeleteShader(gs);
        glDeleteShader(fs);
        glUseProgram(prog);

        const char* headVertSrc = R"(
            #version 330 core
            layout (location = 0) in vec3 aPos;
            layout (location = 1) in vec3 aNormal; // C++에서 보낸 부드러운 법선

            uniform mat4 projection_mat, view_mat, model_mat;
            uniform mat4 lightSpaceMatrix;

            out vec3 FragPos;
            out vec3 Normal; // 색칠(Frag) 단계로 전달
            out vec4 FragPosLightSpace;

            void main() {
                FragPos = vec3(model_mat * vec4(aPos, 1.0));

                // 크기나 위치가 변해도 빛이 닿는 각도가 일치하도록 수학적 보정
                Normal = mat3(transpose(inverse(model_mat))) * aNormal;
                FragPosLightSpace = lightSpaceMatrix * vec4(FragPos, 1.0);

                gl_Position = projection_mat * view_mat * vec4(FragPos, 1.0);
            }
        )";

        const char* headFragSrc = R"(
            #version 330 core
            in vec3 FragPos;
            in vec3 Normal;
            in vec4 FragPosLightSpace;
            out vec4 FragColor;

            uniform sampler2DShadow shadowMap;

            float ShadowCalc(vec3 N, vec3 L) {
                vec3 proj = FragPosLightSpace.xyz / FragPosLightSpace.w;
                proj = proj * 0.5 + 0.5;
                if (proj.z > 1.0 || proj.x < 0.0 || proj.x > 1.0 || proj.y < 0.0 || proj.y > 1.0) {
                    return 1.0;
                }
                float bias = max(0.004 * (1.0 - dot(N, L)), 0.0015);
                vec2 texelSize = 1.0 / vec2(textureSize(shadowMap, 0));
                float shadow = 0.0;
                for (int dx = -2; dx <= 2; ++dx) {
                    for (int dy = -2; dy <= 2; ++dy) {
                        vec2 offset = vec2(float(dx), float(dy)) * texelSize;
                        shadow += texture(shadowMap, vec3(proj.xy + offset, proj.z - bias));
                    }
                }
                return shadow / 25.0;
            }

            uniform vec3 u_LightColor;

            void main() {
                vec3 norm = normalize(Normal);

                // [주의] 얼굴을 비추는 이 정면광(키 라이트)은 헤어에 쓰이는 역광
                // (8,10,-15, TTcol 역광 투과 증폭용)과는 의도적으로 다른 별개의 조명이다.
                // 한때 이 방향을 역광과 통일시켜봤더니 얼굴 정면이 빛을 전혀 못 받아
                // 굴곡이 다 사라졌음 — 사진/영상에서도 얼굴용 키라이트와 헤어용
                // 백라이트를 따로 두는 게 흔한 2-라이트 세팅이라 원래대로 되돌림.
                vec3 lightDir = normalize(vec3(0.5, 1.0, 0.8));

                // 명암 계산
                float diff = max(dot(norm, lightDir), 0.0);
                float ambient = 0.45; // 너무 어둡지 않게 기본 밝기 올림

                // 머리카락이 두피/이마/얼굴 위에 드리우는 그림자. 완전히 0으로
                // 떨어뜨리면 부자연스러워서 0.4를 바닥값으로 둔다(간접광 근사).
                float shadow = mix(0.4, 1.0, ShadowCalc(norm, lightDir));

                vec3 objectColor = vec3(0.85, 0.76, 0.70); // 살구색
                vec3 finalColor = (ambient + diff * 0.55 * shadow) * objectColor * u_LightColor;

                FragColor = vec4(finalColor, 1.0);
            }
        )";
        GLuint hvs = compile(GL_VERTEX_SHADER, headVertSrc);
        GLuint hfs = compile(GL_FRAGMENT_SHADER, headFragSrc);
        GLuint headProg = link(hvs, hfs);
        glDeleteShader(hvs); glDeleteShader(hfs);

        // --- [FBO 그림자맵: 라이트 시점 깊이만 렌더하는 전용 셰이더] ---
        // 헤드(location 0 = aPos)와 헤어(location 0 = vertex_position) 둘 다
        // 위치가 항상 attribute 0번이라 이 셰이더 하나로 둘 다 깊이를 찍을 수 있다.
        const char* shadowVertSrc = R"(
            #version 330 core
            layout (location = 0) in vec3 aPos;
            uniform mat4 lightSpaceMatrix;
            uniform mat4 model_mat;
            void main() {
                gl_Position = lightSpaceMatrix * model_mat * vec4(aPos, 1.0);
            }
        )";
        const char* shadowFragSrc = R"(
            #version 330 core
            void main() { }
        )";
        GLuint svs = compile(GL_VERTEX_SHADER, shadowVertSrc);
        GLuint sfs = compile(GL_FRAGMENT_SHADER, shadowFragSrc);
        GLuint shadowProg = link(svs, sfs);
        glDeleteShader(svs); glDeleteShader(sfs);

        // 헤어 전용 그림자 패스: 일반 shadowProg는 원본 정점 위치(aPos)를 그대로
        // 찍기 때문에, hair.vert가 손상도에 따라 흔들고 부풀리는 변위(frizz/volume)를
        // 반영하지 못했다. 그러면 손상이 있을 때 메인 패스가 그리는 실제(변위된) 위치와
        // 그림자맵에 찍힌(변위 안 된) 위치가 어긋나서, 셀프 섀도잉 비교가 무작위로
        // 틀어지고 얼룩덜룩하게 보였다. hair.vert/hair.geom을 그대로 재사용하고
        // projection_mat/view_mat 자리에 카메라 대신 라이트의 행렬을 넣어서, 손상
        // 변위까지 정확히 일치하는 깊이를 찍도록 한다.
        const char* hairShadowFragSrc = R"(
            #version 330 core
            void main() { }
        )";
        GLuint hsvs = compile(GL_VERTEX_SHADER, vsSrc);
        GLuint hsgs = compile(GL_GEOMETRY_SHADER, gsSrc);
        GLuint hsfs = compile(GL_FRAGMENT_SHADER, hairShadowFragSrc);
        GLuint hairShadowProg = link(hsvs, hsgs, hsfs);
        glDeleteShader(hsvs); glDeleteShader(hsgs); glDeleteShader(hsfs);

        const unsigned int SHADOW_W = 4096, SHADOW_H = 4096;
        GLuint shadowFBO = 0, shadowDepthTex = 0;
        glGenFramebuffers(1, &shadowFBO);
        glGenTextures(1, &shadowDepthTex);
        glBindTexture(GL_TEXTURE_2D, shadowDepthTex);
        glTexImage2D(GL_TEXTURE_2D, 0, GL_DEPTH_COMPONENT, SHADOW_W, SHADOW_H, 0, GL_DEPTH_COMPONENT, GL_FLOAT, nullptr);
        glTexParameteri(GL_TEXTURE_2D, GL_TEXTURE_MIN_FILTER, GL_LINEAR);
        glTexParameteri(GL_TEXTURE_2D, GL_TEXTURE_MAG_FILTER, GL_LINEAR);
        glTexParameteri(GL_TEXTURE_2D, GL_TEXTURE_WRAP_S, GL_CLAMP_TO_BORDER);
        glTexParameteri(GL_TEXTURE_2D, GL_TEXTURE_WRAP_T, GL_CLAMP_TO_BORDER);
        float shadowBorder[4] = { 1.0f, 1.0f, 1.0f, 1.0f };
        glTexParameterfv(GL_TEXTURE_2D, GL_TEXTURE_BORDER_COLOR, shadowBorder);
        // sampler2DShadow + COMPARE_REF_TO_TEXTURE: texture()가 깊이 비교 결과를
        // 하드웨어 2x2 선형보간(PCF)까지 적용해 바로 돌려준다.
        glTexParameteri(GL_TEXTURE_2D, GL_TEXTURE_COMPARE_MODE, GL_COMPARE_REF_TO_TEXTURE);
        glTexParameteri(GL_TEXTURE_2D, GL_TEXTURE_COMPARE_FUNC, GL_LEQUAL);

        glBindFramebuffer(GL_FRAMEBUFFER, shadowFBO);
        glFramebufferTexture2D(GL_FRAMEBUFFER, GL_DEPTH_ATTACHMENT, GL_TEXTURE_2D, shadowDepthTex, 0);
        glDrawBuffer(GL_NONE);
        glReadBuffer(GL_NONE);
        if (glCheckFramebufferStatus(GL_FRAMEBUFFER) != GL_FRAMEBUFFER_COMPLETE) {
            std::cerr << "[Error] Shadow FBO incomplete\n";
        }
        glBindFramebuffer(GL_FRAMEBUFFER, 0);
        glBindTexture(GL_TEXTURE_2D, 0);

        auto U = [&](const char* n) { return glGetUniformLocation(prog, n); };

        HeadMesh headModel;
        loadHeadOBJ("resources/head_model.obj", headModel);

        // 1번 
        if (loadHairDATA("resources/strands00005.data", meshes[0])) {
            processAndUploadMesh(meshes[0], "strands00155 (Key 1)");
        }

        // 2번
        if (loadHairDATA("resources/strands00006.data", meshes[1])) {
            processAndUploadMesh(meshes[1], "strands00001 (Key 2)");
        }
        // 3번
        if (loadHairDATA("resources/strands00004.data", meshes[2])) {
            processAndUploadMesh(meshes[2], "strands00164 (Key 3)");
        }
        // 4번
        if (loadHairDATA("resources/strands00501.data", meshes[3])) {
            processAndUploadMesh(meshes[3], "strands00187 (Key 4)");
        }
        // 5번
        if (loadHairDATA("resources/strands00510.data", meshes[4])) {
            processAndUploadMesh(meshes[4], "strands00149 (Key 5)");
        }
        // 6번
        if (loadHairDATA("resources/strands00509.data", meshes[5])) {
            processAndUploadMesh(meshes[5], "strands00155 (Key 6)");
        }

        // LUT 로드
        GLuint lut1 = loadTexture2D("resources/lookup1.png", false);
        GLuint lut2 = loadTexture2D("resources/lookup2.png", false);

        if (GLint x = U("lookUp1"); x >= 0) glUniform1i(x, 0);
        if (GLint x = U("lookUp2"); x >= 0) glUniform1i(x, 1);
        glActiveTexture(GL_TEXTURE0); glBindTexture(GL_TEXTURE_2D, lut1);
        glActiveTexture(GL_TEXTURE1); glBindTexture(GL_TEXTURE_2D, lut2);

        // 톤 파라미터 세팅 (main.cpp 내부)
        // [수정] diffuse(각도 dot(L,T)≈0에서만 살아남음)와 TT(좁은 로브)는 시야각/광원각에
        // 따라 넓은 영역에서 거의 0이 되어, 각도와 무관하게 늘 보여야 할 "고유색"을 실어줄
        // 항이 사실상 ambient뿐이었다. 0.02는 너무 작아 그 구간에서 무채색 정반사 잔광만
        // 남아 색이 안 보이는(회색으로 뜨는) 문제가 있어 올렸다.
        if (GLint x = U("AmbientCol"); x >= 0) glUniform1f(x, 0.10f);
        if (GLint x = U("DiffuseCol"); x >= 0) glUniform1f(x, 0.22f);  // 탁한 지푸라기 느낌 제거 (밝은 프리셋이 실제로 밝아 보이도록 상향)
        if (GLint x = U("Rcol");       x >= 0) glUniform1f(x, 0.40f);  // 정면광으로 바뀌며 과했던 정반사 줄임
        // [수정] 2.5는 피사체 뒤쪽 역광으로 헤어 가장자리를 강제로 빛나게 하려고
        // 증폭했던 값인데, 광원을 자연스러운 정면 위쪽으로 옮기면서 더는 필요 없어짐.
        // 자연스러운 투과 정도로 되돌림.
        if (GLint x = U("TTcol");      x >= 0) glUniform1f(x, 1.3f);
        if (GLint x = U("alpha");      x >= 0) glUniform1f(x, 0.8f);   // 약간의 투명도 부여

        // ---------- 렌더 루프 ----------
        while (!glfwWindowShouldClose(win)) {
            glfwPollEvents();

            int fbw, fbh;
            glfwGetFramebufferSize(win, &fbw, &fbh);
            float aspect = (fbh > 0) ? float(fbw) / float(fbh) : 16.0f / 9.0f;

            glm::vec3 eyeOffset(
                cam.dist * cosf(cam.pitch) * sinf(cam.yaw),
                cam.dist * sinf(cam.pitch),
                cam.dist * cosf(cam.pitch) * cosf(cam.yaw)
            );
            glm::vec3 eye = cam.target + eyeOffset;

            glm::mat4 view = glm::lookAt(eye, cam.target, glm::vec3(0.0f, 1.0f, 0.0f));
            glm::mat4 proj = glm::perspective(glm::radians(45.0f), aspect, 0.01f, 100.0f);

            // 헤드 변환과 현재 선택된 헤어 메쉬는 그림자 패스/메인 패스 둘 다에서 쓰므로 미리 계산
            glm::mat4 headTransform = glm::translate(glm::mat4(1.0f), glm::vec3(g_headOffsetX, g_headOffsetY, g_headOffsetZ));
            headTransform = glm::scale(headTransform, glm::vec3(g_headScale));
            headTransform = headTransform * headModel.modelMat;

            int idx = g_showMode - 1;
            HairMesh* currMesh = nullptr;
            if (idx >= 0 && idx < (int)meshes.size() && meshes[idx].loaded) {
                currMesh = &meshes[idx];
            }

            // 라이트 시점 뷰/투영 행렬 (원거리 점광원을 방향광처럼 취급)
            // [수정] 예전엔 (8,10,-15)로 피사체 뒤쪽(역광)에 있었는데, 얼굴 키라이트
            // 방향(0.5,1.0,0.8)과 일치하는 정면 위쪽으로 옮겨서 자연스러운 단일 광원
            // 세팅으로 통일했다. 이러면 헤어 그림자에 쓰이는 라이트와 얼굴을 비추는
            // 라이트가 같은 방향이라 이제는 안전하게 하나로 합칠 수 있다.
            glm::vec3 lightPos = glm::vec3(0.5f, 1.0f, 0.8f) * 12.0f;
            glm::mat4 lightView = glm::lookAt(lightPos, glm::vec3(0.0f), glm::vec3(0.0f, 1.0f, 0.0f));
            glm::mat4 lightProj = glm::ortho(-3.0f, 3.0f, -3.0f, 3.0f, 1.0f, 30.0f);
            glm::mat4 lightSpaceMatrix = lightProj * lightView;

            // --- [패스 1: 그림자맵] 라이트 시점에서 깊이만 렌더 ---
            glBindFramebuffer(GL_FRAMEBUFFER, shadowFBO);
            glViewport(0, 0, SHADOW_W, SHADOW_H);
            glClear(GL_DEPTH_BUFFER_BIT);
            glUseProgram(shadowProg);
            {
                GLint lsLoc = glGetUniformLocation(shadowProg, "lightSpaceMatrix");
                GLint smLoc = glGetUniformLocation(shadowProg, "model_mat");
                glUniformMatrix4fv(lsLoc, 1, GL_FALSE, &lightSpaceMatrix[0][0]);

                if (headModel.loaded) {
                    glUniformMatrix4fv(smLoc, 1, GL_FALSE, &headTransform[0][0]);
                    glBindVertexArray(headModel.vao);
                    glDrawArrays(GL_TRIANGLES, 0, headModel.vertexCount);
                    glBindVertexArray(0);
                }
            }

            // 헤어는 hair.vert/hair.geom을 그대로 재사용하는 전용 프로그램으로 깊이를
            // 찍는다 — projection_mat/view_mat에 카메라 대신 라이트 행렬을 넣어서,
            // 메인 패스와 똑같은 손상 변위가 적용된 위치가 찍히도록 한다.
            if (currMesh && currMesh->gpu.vao != 0) {
                glUseProgram(hairShadowProg);
                auto HS = [&](const char* n) { return glGetUniformLocation(hairShadowProg, n); };
                if (GLint x = HS("projection_mat"); x >= 0) glUniformMatrix4fv(x, 1, GL_FALSE, &lightProj[0][0]);
                if (GLint x = HS("view_mat");       x >= 0) glUniformMatrix4fv(x, 1, GL_FALSE, &lightView[0][0]);
                if (GLint x = HS("model_mat");      x >= 0) glUniformMatrix4fv(x, 1, GL_FALSE, &currMesh->modelMat[0][0]);
                if (GLint x = HS("lightSpaceMatrix"); x >= 0) glUniformMatrix4fv(x, 1, GL_FALSE, &lightSpaceMatrix[0][0]);
                if (GLint x = HS("u_Damage");       x >= 0) glUniform1f(x, g_damage);
                if (GLint x = HS("u_NoiseMode");     x >= 0) glUniform1i(x, g_noiseMode);

                glBindVertexArray(currMesh->gpu.vao);
                const auto& starts = currMesh->gpu.starts;
                const auto& counts = currMesh->gpu.counts;
                for (size_t i = 0; i < starts.size(); ++i) {
                    glDrawArrays(GL_LINE_STRIP, starts[i], counts[i]);
                }
                glBindVertexArray(0);
            }
            glBindFramebuffer(GL_FRAMEBUFFER, 0);

            // 이후 모든 패스에서 그림자맵은 텍스처 유닛 2번에 고정
            glActiveTexture(GL_TEXTURE2);
            glBindTexture(GL_TEXTURE_2D, shadowDepthTex);

            // --- [패스 2: 메인 렌더] ---
            glViewport(0, 0, fbw, fbh);
            glClearColor(1.0f, 1.0f, 1.0f, 1.0f);
            glClear(GL_COLOR_BUFFER_BIT | GL_DEPTH_BUFFER_BIT);

            if (headModel.loaded) {
                glUseProgram(headProg);

                GLint pLoc = glGetUniformLocation(headProg, "projection_mat");
                GLint vLoc = glGetUniformLocation(headProg, "view_mat");
                GLint mLoc = glGetUniformLocation(headProg, "model_mat");
                GLint lsLoc = glGetUniformLocation(headProg, "lightSpaceMatrix");
                GLint smLoc = glGetUniformLocation(headProg, "shadowMap");
                GLint lcLoc = glGetUniformLocation(headProg, "u_LightColor");
                glUniformMatrix4fv(pLoc, 1, GL_FALSE, &proj[0][0]);
                glUniformMatrix4fv(vLoc, 1, GL_FALSE, &view[0][0]);
                glUniformMatrix4fv(lsLoc, 1, GL_FALSE, &lightSpaceMatrix[0][0]);
                if (smLoc >= 0) glUniform1i(smLoc, 2);
                // 살짝 따뜻한 색온도를 줘서 전체적으로 더 부드럽고 자연스러운 톤이 되도록 함.
                if (lcLoc >= 0) glUniform3f(lcLoc, 1.0f, 0.96f, 0.88f);

                // 셰이더로 최종 계산된 매트릭스 전송
                glUniformMatrix4fv(mLoc, 1, GL_FALSE, &headTransform[0][0]);

                glBindVertexArray(headModel.vao);
                glDrawArrays(GL_TRIANGLES, 0, headModel.vertexCount);
                glBindVertexArray(0);
            }

            glUseProgram(prog);
            if (GLint x = U("view_mat");       x >= 0) glUniformMatrix4fv(x, 1, GL_FALSE, &view[0][0]);
            if (GLint x = U("projection_mat"); x >= 0) glUniformMatrix4fv(x, 1, GL_FALSE, &proj[0][0]);
            if (GLint x = U("eye_world");      x >= 0) glUniform3f(x, eye.x, eye.y, eye.z);
            if (GLint x = U("lightPosition");  x >= 0) glUniform4f(x, lightPos.x, lightPos.y, lightPos.z, 1.0f);
            if (GLint x = U("u_LightColor");   x >= 0) glUniform3f(x, 1.0f, 0.96f, 0.88f);
            if (GLint x = U("lightSpaceMatrix"); x >= 0) glUniformMatrix4fv(x, 1, GL_FALSE, &lightSpaceMatrix[0][0]);
            if (GLint x = U("shadowMap");       x >= 0) glUniform1i(x, 2);
            if (GLint x = U("u_Damage");       x >= 0) glUniform1f(x, g_damage);
            if (GLint x = U("u_NoiseMode");    x >= 0) glUniform1i(x, g_noiseMode);
            if (GLint x = U("u_InterpMode");   x >= 0) glUniform1i(x, g_interpMode);
            if (GLint x = U("u_BaseHairColor"); x >= 0) {
                const glm::vec3& c = g_hairColorPresets[g_hairColorIndex].color;
                glUniform3f(x, c.r, c.g, c.b);
            }

            if (currMesh && currMesh->gpu.vao != 0) {
                if (GLint x = U("model_mat");   x >= 0) glUniformMatrix4fv(x, 1, GL_FALSE, &currMesh->modelMat[0][0]);
                if (GLint x = U("tangent_mat"); x >= 0) glUniformMatrix3fv(x, 1, GL_FALSE, &currMesh->tangentMat[0][0]);

                glLineWidth(0.8f);
                glEnable(GL_DEPTH_TEST);

                // 알파 값이 들어간 선을 그릴 때 버그를 막기 위한 설정
                glDepthMask(GL_TRUE); // Z버퍼 쓰기 켜기
                glEnable(GL_ALPHA_TEST); // 알파 테스트 켜기 (구버전 호환)
                glAlphaFunc(GL_GREATER, 0.05f); // 알파가 0.05보다 클 때만 그리기

                // 정렬
                struct StrandSort { int index; float dist2; };
                std::vector<StrandSort> sortedStrands(currMesh->gpu.starts.size());

                const auto& P = currMesh->P;
                const auto& starts = currMesh->gpu.starts;
                const auto& counts = currMesh->gpu.counts;
                const auto& model = currMesh->modelMat;

                for (size_t i = 0; i < starts.size(); ++i) {
                    int st = starts[i];
                    int c = counts[i];
                    glm::vec3 mid = 0.5f * (P[st] + P[st + c - 1]);
                    glm::vec3 midW = glm::vec3(model * glm::vec4(mid, 1.0f));
                    glm::vec3 d = midW - eye;
                    sortedStrands[i] = { (int)i, glm::dot(d, d) };
                }
                std::sort(sortedStrands.begin(), sortedStrands.end(),
                    [](const StrandSort& a, const StrandSort& b) { return a.dist2 > b.dist2; });

                glBindVertexArray(currMesh->gpu.vao);
                for (size_t k = 0; k < sortedStrands.size(); ++k) {
                    int id = sortedStrands[k].index;
                    glDrawArrays(GL_LINE_STRIP, starts[id], counts[id]);
                }
                glBindVertexArray(0);
                glDepthMask(GL_TRUE);
            }

            glfwSwapBuffers(win);
        }

        glDeleteTextures(1, &lut1);
        glDeleteTextures(1, &lut2);
        glDeleteProgram(prog);

        // 모든 메쉬 정리
        for (auto& m : meshes) {
            if (m.loaded) {
                glDeleteBuffers(1, &m.gpu.vboPos);
                glDeleteBuffers(1, &m.gpu.vboTan);
                if (m.gpu.vboCol) glDeleteBuffers(1, &m.gpu.vboCol);
                glDeleteVertexArrays(1, &m.gpu.vao);
            }
        }
    }
    catch (const std::exception& e) {
        std::cerr << e.what() << std::endl;
        glfwTerminate();
        return -1;
    }

    glfwTerminate();
    return 0;
}