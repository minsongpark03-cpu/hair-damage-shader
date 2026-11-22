cmake_minimum_required(VERSION 3.20)
project(HairShaderOnly LANGUAGES CXX)

set(CMAKE_CXX_STANDARD 17)
set(CMAKE_CXX_STANDARD_REQUIRED ON)

# vcpkg의 DLL 자동복사 스크립트 끔(권한 문제 회피)
set(VCPKG_APPLOCAL_DEPS OFF CACHE BOOL "" FORCE)

find_package(glfw3 CONFIG REQUIRED)
find_package(glad  CONFIG REQUIRED)
find_package(glm   CONFIG REQUIRED)
find_package(OpenGL REQUIRED)

add_executable(HairShaderOnly
  src/main.cpp
)

# cyHairFile.h가 src에 있으므로, vcpkg보다 '먼저' 잡히게 함
target_include_directories(HairShaderOnly BEFORE PRIVATE
  ${CMAKE_SOURCE_DIR}/src
)

# GLFW가 gl.h를 끼워넣지 않도록(우린 GLAD만 사용)
target_compile_definitions(HairShaderOnly PRIVATE GLFW_INCLUDE_NONE)

target_link_libraries(HairShaderOnly PRIVATE
  OpenGL::GL
  glfw
  glad::glad
  glm::glm
)

# VS(F5) 실행 시 작업 디렉터리를 레포 루트로 → resources/* 상대경로 사용
set_property(TARGET HairShaderOnly PROPERTY
  VS_DEBUGGER_WORKING_DIRECTORY "${CMAKE_SOURCE_DIR}"
)
