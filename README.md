# Damaged Hair Shader — 손상된 모발 셰이더

모발이 손상(탈색, 푸석함, 끊어짐, 갈라짐)됐을 때의 모습을 실시간으로 보여주는 **자체 제작 헤어 셰이더**입니다.
Marschner 반사 모델과 멜라닌 흡수 기반 탈색 모델을 직접 구현했고, OpenGL 프로토타입에서 시작해 Unity(URP)로 옮겨
다양한 두상과 헤어 모델에 적용했습니다. 슬라이더 하나(손상도 0~100%)로 손상이 진행되는 과정을 볼 수 있습니다.

> A real-time hair shader that visualises hair damage (bleaching, frizz, fraying, split ends, trichorrhexis nodosa).
> Marschner lighting + a pigment-based (eumelanin / pheomelanin) bleaching model, prototyped in OpenGL and ported to Unity URP.

> ⚠️ 이 저장소에는 **코드와 셰이더만** 들어 있습니다. 헤어 데이터와 두상 3D 모델은 용량과 라이선스 때문에 포함하지 않았습니다.
> 실행에 필요한 파일은 아래 [데이터와 모델 준비](#데이터와-모델-준비)를 참고하세요.

---

## 주요 기능

### 모발 손상 표현
| 손상 | 구현 |
|---|---|
| **탈색** | d'Eon et al. (2011)의 흡수 계수를 쓴 유멜라닌/페오멜라닌 이중 색소 Beer-Lambert 모델. 손상이 커질수록 유멜라닌이 페오멜라닌보다 빨리 빠져 색이 자연스럽게 밝고 따뜻해집니다. |
| **푸석함 / 부피 팽창** | 손상도에 따라 가닥이 바깥으로 퍼지고 가닥마다 다르게 흩날립니다(정점 셰이더). |
| **끊어짐 / 올 풀림** | 끝부분이 가닥마다 다른 위치에서 끊기고 풀리는 마스크(프래그먼트 셰이더). |
| **갈라진 끝(split ends)** | 가닥 끝이 두 갈래로 나뉘고, 갈라진 올은 더 얇고 밝으며 빛을 더 투과/난반사합니다. 긴 머리, 바깥쪽 머리, 손상도가 클수록 더 잘 갈라집니다. |
| **결절성 열모증** | 줄기 중간에 짧고 하얀 마디가 생기고, 그 부분만 국소적으로 산란이 강합니다. 켜고 끌 수 있습니다. |

### 렌더링
- **Marschner 헤어 라이팅**: R / TT 로브를 룩업 텍스처(`lookup1.png`, `lookup2.png`)로 계산
- **그림자**: 섀도우맵(FBO) + PCF, 머리카락 자기 그림자
- **자연스러운 머리색 프리셋**: 고동색, 밝은 갈색, 적갈색, 금발

### Unity 포팅에서 새로 해결한 것
- **카메라를 향하는 리본 + 가닥 단위 깊이 정렬**: 1픽셀보다 가는 가닥을 알파 블렌딩으로 그리되, 가닥 약 1만 개를 매 프레임
  카메라 깊이순으로 정렬해서 한 번의 인스턴스 드로우로 그림(`StructuredBuffer` 기반). 뒷머리가 앞머리를 덮어쓰는 문제와
  카메라를 돌릴 때 밝기가 덩어리째 튀는 문제를 해결했습니다.
- **여러 두상 자동 맞춤**: 머리카락 뿌리 좌표에 두상 표면이 닿도록 크기/위치를 ICP로 계산(`HeadFitter`),
  뿌리를 두피 표면에 붙이고(`RootSnapper`), 두상 안으로 파고든 가닥을 바깥으로 밀어냅니다.
- **루트 틴트**: 뿌리 쪽 가닥을 어둡게 하고, 머리카락이 덮는 두피를 3D 밀도 필드 텍스처(`ScalpMask`)로 피부 그림자색 쪽으로 칠합니다.
- **전시용 UI**: 손상도 슬라이더, 머리색, 헤어 모델, 두상, 배경 선택. 계산 중에는 로딩 화면이 뜹니다.

---

## 저장소 구성

```
opengl-prototype/      OpenGL 3.3 프로토타입 (C++, GLFW, GLAD, GLM)
  src/main.cpp           렌더링 루프, 손상 로직, 섀도우맵, 키보드 입력
  src/cyHairFile.h       Cem Yuksel의 .hair 파일 로더 (MIT, 원저작자 표기 유지)
  resources/hair.vert    부피 팽창/흩날림 (정점 셰이더)
  resources/hair.geom    갈라진 끝 (지오메트리 셰이더)
  resources/marschnerHair.frag   Marschner 라이팅 + 탈색 + 끊어짐 (프래그먼트 셰이더)
unity-demo/            Unity 6000.3.25f1, URP 17.3 프로젝트
  Assets/HairDemo/Scripts/       컨트롤러, UI, 헤어 로더, 두상 맞춤, 두피 마스크
  Assets/Resources/HairDemo/     HairMarschner.shader, HeadSkin.shader, HairCommon.hlsl
THIRD_PARTY_NOTICES.md  출처와 라이선스
```

---

## 데이터와 모델 준비

헤어 데이터와 두상 모델은 저장소에 없습니다. 직접 구해서 아래 위치에 넣어야 합니다.

| 파일 | 위치 | 설명 |
|---|---|---|
| `strands*.data` | `unity-demo/Assets/StreamingAssets/HairData/` | 가닥 데이터. 바이너리: `uint 가닥 수` → 가닥마다 [`uint 점 개수`, `float x,y,z` × 점 개수]. 폴더에 넣은 `.data`는 자동으로 버튼이 생깁니다. |
| `head_model.obj` | `unity-demo/Assets/Resources/HairDemo/` | 기준(기본) 두상 |
| `Heads/head_female.obj` 등 | `unity-demo/Assets/Resources/HairDemo/Heads/` | 추가 두상. `HairDemoController.cs`의 `HeadSpecs`에 한 줄 추가하면 버튼이 생깁니다. |

OpenGL 프로토타입은 실행 폴더 기준 `resources/head_model.obj`와 `resources/strands00005.data`, `00006`, `00004`, `00501`, `00510`, `00509`를 읽습니다.

- 헤어 데이터는 위 구조의 단순 형식입니다. 공개 헤어 데이터셋 중 같은 형식을 쓰는 것이 있는지, 그리고 **사용 조건(연구용 등)을 직접 확인**하세요.
  `.hair`(cyHair) 형식은 [Cem Yuksel의 헤어 모델](http://www.cemyuksel.com/research/hairmodels/)에서 받을 수 있고, `cyHairFile.h`로 읽습니다.
- 두상 OBJ는 UV가 없어도 됩니다(곡률/법선은 코드에서 계산). 정점이 수십만 개로 많으면 무거우니 줄여서 쓰길 권합니다.
  어깨까지 포함된 흉상은 긴 머리가 어깨를 뚫고 지나가 보이니 턱 아래에서 잘라서 쓰세요.

---

## 실행

### Unity (권장)
1. Unity **6000.3.25f1** 으로 `unity-demo` 폴더를 엽니다 (URP 17.3, Input System 1.20).
2. 위 [데이터와 모델 준비](#데이터와-모델-준비)에 맞춰 파일을 넣습니다.
3. `Assets/Scenes/SampleScene`을 열고 재생합니다. 컨트롤러가 시작할 때 모든 오브젝트와 UI를 스스로 만들기 때문에 씬은 비어 있어도 됩니다.
4. 전시용 실행 파일은 Build Profiles에서 Windows로 빌드합니다. `StreamingAssets` 폴더가 `*_Data/` 안으로 같이 복사됩니다.

**조작**: 마우스 왼쪽 드래그 = 회전, 오른쪽 드래그/휠 = 확대 축소, 휠 클릭 드래그 = 이동. 화면 UI로 손상도, 머리색, 모델, 두상을 고릅니다.
키보드: `↑/↓` 손상도, `1~6` 헤어 모델, `9` 머리색, `R` 카메라 리셋, `Esc` 종료.

### OpenGL 프로토타입
필요: CMake 3.20+, C++17 컴파일러, vcpkg의 `glfw3`, `glad`, `glm`.
```
cd opengl-prototype
cmake -S . -B build -DCMAKE_TOOLCHAIN_FILE=<vcpkg>/scripts/buildsystems/vcpkg.cmake
cmake --build build --config Release
```
**저장소의 `opengl-prototype` 폴더를 작업 디렉터리로 실행**해야 `resources/...` 경로를 찾습니다.
키보드: `↑/↓` 손상도, `1~6` 헤어 모델, `7` 노이즈 방식, `8` 보간 방식, `9` 머리색, `A/D/W/S/Q/E` 두상 위치, `Z/X` 두상 크기, `Esc` 종료.

---

## 알려진 한계
- 가는 가닥은 알파 블렌딩이라 매우 가까이 확대하면 겹침에 따라 약간의 노이즈가 보일 수 있습니다.
- 두상 자동 맞춤은 뿌리 위치만 쓰기 때문에, 머리카락 데이터가 원래 맞춰진 두상과 크게 다른 얼굴에서는 이마 쪽이 어긋날 수 있습니다.
- 에디터(디버그 모드)에서는 두상 맞춤 계산이 몇 초 걸립니다. 빌드(Release)에서는 더 빠릅니다.
- 얼굴 모델은 마네킹처럼 단색입니다(눈, 입술 등은 칠하지 않음).

---

## 출처와 크레딧
출처와 라이선스는 [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md)에 정리했습니다. 간단히:
- 프로토타입은 COMP 465 수업의 기말 프로젝트 *Let Hair be Light* (원 저장소 이름 `hair-simulation-project-oh-pujol`)에서 시작했고, 현재의 `main.cpp`와 셰이더는 대부분 새로 작성/수정한 것입니다.
- Marschner 구현 참고: [voicualexandruteodor의 블로그 글](https://hairrendering.wordpress.com/2010/06/23/marschner-in-nalu-demo/)
- 멜라닌 흡수 계수: d'Eon, Francois, Hill, Letteri, Aubel, *An Energy-Conserving Hair Reflectance Model* (2011)
- `cyHairFile.h`: Cem Yuksel (MIT)

## 라이선스
아직 정하지 않았습니다(별도 라이선스를 붙이기 전까지 모든 권리 보유). 수업 프로젝트 기반 코드와 외부 데이터의 사용 조건을 정리한 뒤 정할 예정입니다.
