# Third-party notices / 출처

이 저장소가 참고하거나 포함한 외부 자료입니다.

| 자료 | 출처 | 저장소에 포함? | 조건 |
|---|---|---|---|
| `opengl-prototype/src/cyHairFile.h` | Cem Yuksel, cyCodeBase | **포함** | MIT License. 파일 맨 위의 저작권/허가 문구를 그대로 유지해야 합니다. |
| 수업 프로젝트 *Let Hair be Light* (COMP 465) | 원 저장소 이름 `hair-simulation-project-oh-pujol`. 프로토타입이 여기서 시작함 (저자 정보는 확인 필요) | 일부 포함 | `marschnerHair.frag`, `lookup1.png`, `lookup2.png`는 이 프로젝트에서 출발해 수정한 파일이고, Unity의 `HairMarschner.shader`는 이를 이식한 것입니다. **라이선스 파일이 없습니다.** 공개 전에 원저작자 허락이나 조건을 확인해야 합니다. |
| Marschner 구현 참고 | [voicualexandruteodor, Marschner in Nalu demo](https://hairrendering.wordpress.com/2010/06/23/marschner-in-nalu-demo/) | 코드 미포함 (참고) | 블로그 글 |
| 멜라닌 흡수 계수 | d'Eon, Francois, Hill, Letteri, Aubel, *An Energy-Conserving Hair Reflectance Model*, EGSR 2011 | 수치만 사용 | 논문 |
| 헤어 데이터 `strands*.data/.txt` | 출처 확인 필요 | **미포함** | 사용 조건 확인 필요 (연구용 데이터셋일 수 있음) |
| 기본 두상 `head_model.obj` | 출처 확인 필요 | **미포함** | |
| 여성 두상 `head_female.obj` | 스컬프트 모델 (출처/라이선스 확인 필요) | **미포함** | 대부분의 공개 3D 모델은 저작자 표기를 요구합니다. |
| 오픈소스 라이브러리 | GLFW, GLAD, GLM (vcpkg로 설치) | 미포함 | 각 라이브러리의 라이선스 |
| Unity, URP, Input System | Unity Technologies | 패키지 매니페스트만 | Unity 라이선스 |

## 처리 방침
- 수업 프로젝트에서 출발한 파일은 **출처를 밝히고 포함**했습니다. 포함한 파일은 `marschnerHair.frag`, `lookup1.png`, `lookup2.png`입니다.
  현재의 `main.cpp`, `hair.vert`, `hair.geom`은 직접 작성했습니다. Unity의 `HairMarschner.shader`는 프로토타입의 `marschnerHair.frag`를 이식한 것이라(Marschner 라이팅 부분) 같은 출처를 가집니다. 그 외 Unity 코드(`HairDemoController`, `HairStrandLoader`, `HeadFitter`, `RootSnapper`, `ScalpMask`, UI)는 직접 작성했습니다.
- 원 저작자나 권리자께서 문제를 제기하시면 해당 파일을 즉시 제거합니다. 이 저장소의 Issues로 알려주세요.
- 헤어 데이터와 두상 모델은 저장소에 **포함하지 않았습니다**. 다만 `docs/images`의 결과 이미지에는 이 자료들로 렌더링한 화면이 보입니다.

## 확인할 것 (TODO)
- [ ] 헤어 데이터, 기본 두상, 여성 두상의 정확한 출처와 라이선스, 표기 방법을 확인해서 위 표를 채우기
