using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace HairDemo
{
    /// <summary>
    /// Play를 누르면 씬에 아무것도 없어도 카메라/조명/두상/헤어/UI를 코드로 구성한다.
    /// OpenGL 프로토타입(main.cpp)의 조작을 그대로 옮겼다:
    ///   마우스 좌 드래그=회전, 우 드래그/휠=줌, 휠 클릭 드래그=이동
    ///   1~6 헤어 모델, ↑/↓ 손상도, 7 노이즈 모드, 8 보간 모드, 9 머리색, R 카메라 리셋
    /// </summary>
    public partial class HairDemoController : MonoBehaviour
    {
        // 프로토타입의 슬롯 순서(1~6번 키)와 같은 파일. 그 뒤로는 StreamingAssets/HairData 폴더의
        // 나머지 .data 파일이 번호순으로 자동으로 붙는다(파일만 넣으면 버튼이 생긴다).
        static readonly string[] PriorityHairFiles =
        {
            "strands00005.data", "strands00006.data", "strands00004.data",
            "strands00501.data", "strands00510.data", "strands00509.data",
        };
        string[] HairFiles = PriorityHairFiles;

        void ScanHairFiles()
        {
            string dir = Path.Combine(Application.streamingAssetsPath, "HairData");
            if (!Directory.Exists(dir)) return;

            var found = new List<string>();
            foreach (var f in Directory.GetFiles(dir, "*.data")) found.Add(Path.GetFileName(f));
            found.Sort(System.StringComparer.OrdinalIgnoreCase);

            var list = new List<string>();
            foreach (var p in PriorityHairFiles) if (found.Remove(p)) list.Add(p);
            list.AddRange(found);
            if (list.Count > 0) HairFiles = list.ToArray();
        }

        struct ColorPreset
        {
            public string name;
            public Color color;
            public ColorPreset(string n, float r, float g, float b) { name = n; color = new Color(r, g, b, 1f); }
        }

        static readonly ColorPreset[] Presets =
        {
            new ColorPreset("고동색 (다크 체스트넛)", 0.14f, 0.07f, 0.045f),
            new ColorPreset("밝은 갈색",             0.50f, 0.32f, 0.18f),
            new ColorPreset("붉은색 (오렌지 머리)",   0.62f, 0.24f, 0.12f),
            new ColorPreset("금발",                  0.75f, 0.58f, 0.32f),
        };

        // 배경색: 탈색된 밝은 끝이 흰 배경 위에서는 대비가 없어 비쳐 보이므로, 평가/전시용으로 바꿔볼 수 있게 한다.
        struct BackgroundPreset
        {
            public string name;
            public Color color;
            public BackgroundPreset(string n, float v) { name = n; color = new Color(v, v, v, 1f); }
        }

        static readonly BackgroundPreset[] Backgrounds =
        {
            new BackgroundPreset("흰색", 1.00f),
            new BackgroundPreset("밝은 회색", 0.62f),
            new BackgroundPreset("어두운 회색", 0.22f),
            new BackgroundPreset("검정", 0.00f),
        };

        // OpenGL -> Unity 좌표계 변환 시 X축을 뒤집는다. Unity가 OBJ를 불러올 때도 X를 뒤집으므로
        // 두상과 헤어가 같은 쪽으로 맞는다. (두상과 헤어가 좌우로 어긋나 보이면 이 값만 바꾸면 된다)
        const bool HairFlipX = true;

        // 키라이트 방향(광원 쪽). 프로토타입의 (0.5, 1.0, 0.8)에서 X를 뒤집었다.
        static readonly Vector3 ToLight = new Vector3(-0.5f, 1.0f, 0.8f);

        Camera cam;
        Light keyLight;
        Material hairMat, headMat;
        MeshFilter hairShadowFilter;  // 그림자만 드리우는 라인 메쉬

        // 가닥 단위 깊이 정렬: 매 프레임 가닥을 카메라 깊이순으로 정렬해서 인스턴스 목록(orderBuffer)을 만든다.
        GraphicsBuffer orderBuffer;
        uint[] orderData;
        float[] sortKeys;
        int[] sortIds;

        // 자식 가닥: 같은 리본 메쉬를 번호(0=원본, 1~=옆으로 벗어난 복제)별로 여러 번 그려서 머리숱을 늘린다.
        const int MaxDensity = 5;
        int hairDensity = 4;

        // 갈라진 모발: 일반 패스(0)에 더해 잔가닥 패스(1~3)를 추가로 그린다. [자식 가닥 번호, 잔가닥 번호]
        const int MaxFibers = 2;
        float splitSpread = 0.03f;
        bool nodosaOn = true;      // 결절성 열모증(줄기 중간의 하얀 마디) 켜기/끄기
        UniversalRenderPipelineAsset urp;

        // 모델 하나가 정점 수백만 개라 메모리를 아끼려고 지금 보는 것만 들고 있는다(전환 시 새로 읽음).
        HairMeshSet currentHair;
        int hairSlot = -1;
        float hairWidth = 0.0020f; // 월드 단위 가닥 굵기 (화면 표시값 2.0)
        int colorIndex;
        float damage;
        int noiseMode = 1;
        int interpMode = 1;
        int backgroundIndex;

        // 진단용: 자글거림/비침의 원인을 가려내기 위한 스위치들
        float cuticleNoise = 1f;   // 큐티클 노이즈 세기 (0 = 끔)
        int debugView;             // 0 = 일반, 1 = 조명 없이 색만, 2 = 알파만

        // 헤어 셀프 섀도우. 선(머리카락)은 두께가 없어서 바이어스가 작으면 가닥끼리 서로에게
        // 무작위로 그림자를 드리워 점선 같은 얼룩이 생긴다. (프로토타입은 바이어스를 아주 크게 줬다)
        bool hairReceivesShadow = true;
        float shadowBias = 0.04f;

        // 카메라 (CameraControl)
        float yaw, pitch, dist = 2.5f;
        Vector3 target = Vector3.zero;

        float damageRepeat;
        bool pointerOverHud;
        float hudWidthPx, hudHeightPx;

        // --------------------------------------------------------------------
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            if (FindFirstObjectByType<HairDemoController>() != null) return;
            new GameObject("HairDemo").AddComponent<HairDemoController>();
        }

        void Awake()
        {
            QualitySettings.vSyncCount = 1;
            SetupRenderPipeline();
            SetupCamera();
            SetupLight();
            BuildMaterials();
            LoadHeadSlot(0);
            BuildHair();
            ScanHairFiles();
            LoadHairSlot(0);
            ApplyMaterialState();
        }

        // --------------------------------------------------------------------
        // 설정
        // --------------------------------------------------------------------
        void SetupRenderPipeline()
        {
            urp = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
            if (urp == null)
            {
                Debug.LogWarning("[HairDemo] URP 에셋을 찾지 못했습니다. Universal 3D 템플릿 프로젝트에서 실행해주세요.");
                return;
            }
            // 가는 선(머리카락) 그림자를 위해 섀도우맵을 한 장으로 크게 쓰고, 선 가장자리는 MSAA로 부드럽게 한다.
            // (메인 라이트 그림자/소프트 섀도우 켜기는 읽기 전용이라 코드에서 못 바꾼다. PC 에셋에서 이미 켜져 있다.)
            urp.mainLightShadowmapResolution = 4096;
            urp.shadowCascadeCount = 1;
            urp.msaaSampleCount = 4;
        }

        void SetupCamera()
        {
            cam = Camera.main;
            if (cam == null)
            {
                var go = new GameObject("Main Camera") { tag = "MainCamera" };
                cam = go.AddComponent<Camera>();
            }
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = Color.white;
            cam.fieldOfView = 45f;
            cam.nearClipPlane = 0.01f;
            cam.farClipPlane = 100f;

            // 셰이더가 직접 톤매핑하므로 템플릿의 포스트프로세싱(Tonemapping 등)은 끈다.
            var data = cam.GetComponent<UniversalAdditionalCameraData>();
            if (data != null) data.renderPostProcessing = false;
        }

        void SetupLight()
        {
            keyLight = null;
            foreach (var l in FindObjectsByType<Light>(FindObjectsSortMode.None))
            {
                if (l.type == LightType.Directional) { keyLight = l; break; }
            }
            if (keyLight == null)
            {
                var go = new GameObject("Key Light");
                keyLight = go.AddComponent<Light>();
                keyLight.type = LightType.Directional;
            }

            keyLight.transform.rotation = Quaternion.LookRotation(-ToLight.normalized);
            keyLight.color = Color.white;
            keyLight.intensity = 1f;
            keyLight.shadows = LightShadows.Soft;
            keyLight.shadowStrength = 1f;

            // 장면이 아주 작아서(머리 ≈ 1.6유닛) 기본 바이어스(0.05/0.4)를 쓰면 그림자가 뜬다.
            var ald = keyLight.GetComponent<UniversalAdditionalLightData>();
            if (ald != null) ald.usePipelineSettings = false;
            keyLight.shadowBias = shadowBias;
            keyLight.shadowNormalBias = 0.0f;
        }

        Shader LoadShader(string resourceName, string findName)
        {
            var sh = Resources.Load<Shader>("HairDemo/" + resourceName);
            if (sh == null) sh = Shader.Find(findName);
            if (sh == null) Debug.LogError("[HairDemo] 셰이더를 찾을 수 없습니다: " + findName);
            return sh;
        }

        void BuildMaterials()
        {
            hairMat = new Material(LoadShader("HairMarschner", "HairDemo/Marschner")) { name = "HairMarschner (runtime)" };
            hairMat.SetTexture("_LookUp1", Resources.Load<Texture2D>("HairDemo/lookup1"));
            hairMat.SetTexture("_LookUp2", Resources.Load<Texture2D>("HairDemo/lookup2"));
            hairMat.SetColor("_HairLightColor", new Color(1f, 0.96f, 0.88f, 1f));
            hairMat.SetFloat("_AmbientCol", 0.10f);
            hairMat.SetFloat("_DiffuseCol", 0.22f);
            hairMat.SetFloat("_Rcol", 0.40f);
            hairMat.SetFloat("_TTcol", 1.3f);
            hairMat.SetFloat("_Exposure", 0.5f);

            // 색 패스: 알파 블렌딩, 깊이는 쓰지 않고 프리패스가 쓴 깊이와만 비교한다(가장 앞 가닥만 통과).
            hairMat.SetFloat("_DepthPrepass", 0f);
            hairMat.SetFloat("_ZWrite", 0f);
            hairMat.SetFloat("_ColorMask", 15f);
            hairMat.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
            hairMat.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
            hairMat.renderQueue = (int)RenderQueue.Transparent;

            headMat = new Material(LoadShader("HeadSkin", "HairDemo/HeadSkin")) { name = "HeadSkin (runtime)" };
            headMat.SetColor("_HairLightColor", new Color(1f, 0.96f, 0.88f, 1f));
        }

        // --------------------------------------------------------------------
        // 두상 / 헤어 오브젝트
        // --------------------------------------------------------------------
        struct HeadSpec
        {
            public string label, resource;
            public Vector3 scale; // 폭 기준 크기에 곱하는 축별 보정 (x=가로, y=세로, z=앞뒤)
            public HeadSpec(string l, string r, float sx = 1f, float sy = 1f, float sz = 1f)
            {
                label = l; resource = r; scale = new Vector3(sx, sy, sz);
            }
        }

        // 첫 번째가 프로토타입의 두상(기준). 나머지는 기준 두상의 폭/정수리 높이/앞뒤 중심에 맞춰 크기와 위치를 정한다.
        static readonly HeadSpec[] HeadSpecs =
        {
            new HeadSpec("기본", "HairDemo/head_model"),
            // 귀를 뺀 두개골 폭이 기준 두상보다 약 20% 좁아서 가로를 넓히고, 세로/앞뒤는 약간만 키운다(1.20, 1.06, 1.05).
            // 어깨를 잘라낸 뒤로는 머리 폭 기준이 어깨(2.77)에서 귀(약 2.21)로 바뀌어 기본 배율이 1.25배가 되었으니,
            // 같은 크기가 되도록 그만큼 나눈다. 자동 맞춤의 출발점과 자동 맞춤을 끌 때의 수동 배율에 쓰인다.
            new HeadSpec("여성", "HairDemo/Heads/head_female", 0.96f, 0.85f, 0.84f),
        };

        GameObject headGO;
        int headSlot = -1;
        float refHeadWidth, refHeadTop; // 기준 두상이 월드에서 차지하는 폭과 정수리 높이

        // 화면 슬라이더로 두상 크기를 미세 조정한다(두상을 바꾸면 1로 되돌린다).
        float headWidthAdj = 1f, headSizeAdj = 1f;
        Bounds headBounds;   // 현재 두상 메쉬의 로컬 바운즈
        float headBaseK;     // 폭 기준 기본 배율
        Vector3 headSpecScale = Vector3.one;

        // 자동 맞춤 결과(머리카락 뿌리 좌표 기준). 끄면 HeadSpecs의 수동 배율을 쓴다.
        bool headAutoFit = true;

        // 뿌리 두피 보정: 뿌리를 두상 표면의 가장 가까운 점으로 옮긴다.
        bool snapRoots = true;
        bool snapCollide = true; // 가닥이 두상 안으로 파고들지 않게 밀어내기
        bool rootTint = true;    // 루트 틴트: 뿌리 쪽 머리카락과 머리카락 밑 두피를 머리색 쪽으로 어둡게
        bool snapDirty;
        float snapTime;
        Vector3[] headLocalVerts;
        int[] headTris;
        bool hasHeadFit;
        float actionStart; // 가장 마지막으로 두상/머리카락 버튼을 누른 시각(시간 측정용)
        System.Threading.Tasks.Task<HeadFitter.Result> fitTask;
        int fitGen, fitGenAtStart;
        Vector3 headFitScale, headFitPos;

        // 두상의 배율/위치를 다시 계산해서 적용한다. 정수리 높이와 좌우/앞뒤 중심은 항상 기준 두상과 같게 유지한다.
        void ApplyHeadTransform()
        {
            ApplyHeadTransformCore();
            MarkSnapDirty();
        }

        void ApplyHeadTransformCore()
        {
            if (headGO == null) return;

            // 자동 맞춤 결과가 있으면 그것을 기준으로 하고, 슬라이더는 그 위에서 정수리 중심을 축으로 미세 조정한다.
            if (headAutoFit && hasHeadFit)
            {
                Vector3 sf = headFitScale * headSizeAdj;
                sf.x *= headWidthAdj;
                Vector3 pivot = new Vector3(headBounds.center.x, headBounds.max.y, headBounds.center.z);
                Vector3 w0 = Vector3.Scale(headFitScale, pivot) + headFitPos;
                headGO.transform.localScale = sf;
                headGO.transform.position = w0 - Vector3.Scale(sf, pivot);
                return;
            }

            Vector3 s = headSpecScale * (headBaseK * headSizeAdj);
            s.x *= headWidthAdj;
            Vector3 pos;
            if (headSlot == 0)
                pos = new Vector3(0f, 0.34f, 0.04f) - Vector3.Scale(headBounds.center, s);
            else
                pos = new Vector3(
                    -headBounds.center.x * s.x,
                    refHeadTop - headBounds.max.y * s.y,
                    0.04f - headBounds.center.z * s.z);
            headGO.transform.localScale = s;
            headGO.transform.position = pos;
        }

        void LoadHeadSlot(int slot)
        {
            slot = Mathf.Clamp(slot, 0, HeadSpecs.Length - 1);
            if (slot == headSlot && headGO != null) return;
            actionStart = Time.realtimeSinceStartup;
            var swHead = System.Diagnostics.Stopwatch.StartNew();

            var prefab = Resources.Load<GameObject>(HeadSpecs[slot].resource);
            var srcFilter = prefab != null ? prefab.GetComponentInChildren<MeshFilter>() : null;
            if (srcFilter == null || srcFilter.sharedMesh == null)
            {
                Debug.LogError("[HairDemo] 두상을 불러오지 못했습니다: " + HeadSpecs[slot].resource);
                return;
            }

            Mesh head = BuildSmoothedMesh(srcFilter.sharedMesh);
            Bounds b = head.bounds;

            // 기준 두상(프로토타입): 최대 변이 ±1이 되게 정규화 후 scale 0.8, 위치 (0, 0.34, 0.04)
            float maxDim = Mathf.Max(b.size.x, Mathf.Max(b.size.y, b.size.z));
            float k;
            if (slot == 0)
            {
                k = 0.8f * 2f / maxDim;
                refHeadWidth = b.size.x * k;
                refHeadTop = 0.34f + b.extents.y * k;
            }
            else
            {
                // 다른 두상: 머리 폭을 기준 두상과 같게(목 길이/어깨 포함 여부와 무관하게) 맞춘 뒤 축별 보정을 곱한다.
                // 정수리 높이와 좌우/앞뒤 중심은 ApplyHeadTransform에서 기준 두상과 같게 맞춘다.
                if (refHeadWidth <= 0f) LoadHeadSlotReference();
                k = refHeadWidth / b.size.x;
            }
            headBounds = b;
            headLocalVerts = head.vertices;
            headTris = head.triangles;
            headBaseK = k;
            headSpecScale = HeadSpecs[slot].scale;
            headWidthAdj = 1f;
            headSizeAdj = 1f;

            // 자동 맞춤: 머리카락 뿌리 좌표에 두상 표면이 닿도록 크기/위치를 계산한다(기본 두상도 프로토타입에서 손으로 맞춘 위치가 정확하지 않아서 똑같이 맞춘다).
            hasHeadFit = false;
            if (currentHair != null && currentHair.roots != null)
            {
                Vector3 s0 = headSpecScale * k;
                Vector3 t0 = slot == 0 ? new Vector3(0f, 0.34f, 0.04f) - Vector3.Scale(b.center, s0) : new Vector3(
                    -b.center.x * s0.x,
                    refHeadTop - b.max.y * s0.y,
                    0.04f - b.center.z * s0.z);
                // 계산은 백그라운드에서 한다. 끝나기 전에는 수동 배율로 보여주다가, 끝나면 자동 맞춤 결과로 바꾼다.
                var fitVerts = head.vertices;
                var fitRoots = currentHair.roots;
                fitGenAtStart = ++fitGen;
                fitTask = System.Threading.Tasks.Task.Run(() => HeadFitter.Fit(fitVerts, fitRoots, s0, t0));
            }
            else
            {
                fitGen++; // 이전 두상의 맞춤 결과가 뒤늦게 도착해도 버리게 한다
            }

            if (headGO != null)
            {
                var oldFilter = headGO.GetComponent<MeshFilter>();
                if (oldFilter != null && oldFilter.sharedMesh != null) Destroy(oldFilter.sharedMesh);
                Destroy(headGO);
            }

            headGO = new GameObject("Head");
            headGO.transform.SetParent(transform, false);

            headGO.AddComponent<MeshFilter>().sharedMesh = head;
            var mr = headGO.AddComponent<MeshRenderer>();
            mr.sharedMaterial = headMat;
            mr.shadowCastingMode = ShadowCastingMode.On;
            mr.receiveShadows = true;
            headSlot = slot;
            ApplyHeadTransform();
            Debug.Log($"[HairDemo][시간] 두상 메쉬 구성+배치 {swHead.ElapsedMilliseconds}ms");
        }

        // 기준 두상의 폭/정수리 높이만 필요할 때(처음부터 다른 두상을 고른 경우)
        void LoadHeadSlotReference()
        {
            var prefab = Resources.Load<GameObject>(HeadSpecs[0].resource);
            var f = prefab != null ? prefab.GetComponentInChildren<MeshFilter>() : null;
            if (f == null || f.sharedMesh == null) return;
            Bounds b = f.sharedMesh.bounds;
            float k0 = 0.8f * 2f / Mathf.Max(b.size.x, Mathf.Max(b.size.y, b.size.z));
            refHeadWidth = b.size.x * k0;
            refHeadTop = 0.34f + b.extents.y * k0;
        }


        void BuildHair()
        {
            // 화면에 보이는 머리카락(카메라를 보는 얇은 리본)은 DrawHair()에서 프로시저럴 드로우로 그린다.

            // 그림자 전용(화면에는 안 그림): 같은 위치의 라인 메쉬를 섀도우맵에만 그린다.
            var sgo = new GameObject("HairShadowCaster");
            sgo.transform.SetParent(transform, false);
            hairShadowFilter = sgo.AddComponent<MeshFilter>();
            var smr = sgo.AddComponent<MeshRenderer>();
            smr.sharedMaterial = hairMat;
            smr.shadowCastingMode = ShadowCastingMode.ShadowsOnly;
            smr.receiveShadows = false;
            smr.lightProbeUsage = LightProbeUsage.Off;
            smr.reflectionProbeUsage = ReflectionProbeUsage.Off;
        }

        void LoadHairSlot(int slot)
        {
            slot = Mathf.Clamp(slot, 0, HairFiles.Length - 1);
            if (slot == hairSlot && currentHair != null) return;

            actionStart = Time.realtimeSinceStartup;
            var swLoad = System.Diagnostics.Stopwatch.StartNew();
            string path = Path.Combine(Application.streamingAssetsPath, "HairData", HairFiles[slot]);
            HairMeshSet loaded = HairStrandLoader.Load(path, HairFlipX);
            if (loaded == null) return;
            Debug.Log($"[HairDemo][시간] 머리카락 파일 읽기+GPU 업로드 {swLoad.ElapsedMilliseconds}ms");

            hairShadowFilter.sharedMesh = null;
            if (currentHair != null) currentHair.Destroy();
            orderBuffer?.Release();

            currentHair = loaded;
            hairSlot = slot;
            hairShadowFilter.sharedMesh = loaded.lines;
            MarkSnapDirty();

            // 셰이더가 읽는 머리카락 버퍼들을 머티리얼에 연결한다.
            hairMat.SetBuffer("_HairA", loaded.hairA);
            hairMat.SetBuffer("_HairB", loaded.hairB);
            hairMat.SetBuffer("_StrandU", loaded.strandU);
            hairMat.SetBuffer("_StrandF", loaded.strandF);

            hairMat.SetFloat("_MaxStrandLen", loaded.maxStrandLength);

            // 앱을 처음 켤 때는 두상이 머리카락보다 먼저 만들어져서 자동 맞춤을 아직 시작하지 못했다. 이때 시작한다.
            if (!hasHeadFit && fitTask == null && headGO != null && headLocalVerts != null && loaded.roots != null)
            {
                Vector3 s0 = headSpecScale * headBaseK;
                Vector3 t0 = headSlot == 0
                    ? new Vector3(0f, 0.34f, 0.04f) - Vector3.Scale(headBounds.center, s0)
                    : new Vector3(-headBounds.center.x * s0.x, refHeadTop - headBounds.max.y * s0.y, 0.04f - headBounds.center.z * s0.z);
                var fitVerts = headLocalVerts;
                var fitRoots = loaded.roots;
                fitGenAtStart = ++fitGen;
                fitTask = System.Threading.Tasks.Task.Run(() => HeadFitter.Fit(fitVerts, fitRoots, s0, t0));
            }
            int capacity = loaded.strandCount * MaxDensity * (MaxFibers + 1);
            orderBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, capacity, sizeof(uint));
            orderData = new uint[capacity];
            sortKeys = new float[loaded.strandCount];
            sortIds = new int[loaded.strandCount];
            hairMat.SetBuffer("_HairOrder", orderBuffer);
        }

        // 가닥을 카메라 깊이순(먼 것 → 가까운 것)으로 정렬한 인스턴스 목록을 만들어 한 번에 그린다.
        // 알파 블렌딩은 그리는 순서가 곧 합성 순서라서, 가닥 단위로 순서를 맞춰야 카메라를 돌려도
        // 밝은 가닥/어두운 가닥이 덩어리째 번갈아 튀지 않는다.
        void DrawHair()
        {
            if (currentHair == null || hairMat == null || orderBuffer == null) return;

            int n = Mathf.Clamp(hairDensity, 1, MaxDensity);

            // 손상이 거의 없으면 갈라지는 가닥이 없으므로 잔가닥 인스턴스는 만들지 않는다.
            int passes = damage > 0.01f ? MaxFibers + 1 : 1;

            int count = currentHair.strandCount;
            Vector3 camPos = cam != null ? cam.transform.position : Vector3.zero;
            Vector3 fwd = cam != null ? cam.transform.forward : Vector3.forward;
            Vector3[] centers = currentHair.strandCenters;
            for (int s = 0; s < count; s++)
            {
                sortKeys[s] = Vector3.Dot(centers[s] - camPos, fwd); // 카메라 앞쪽으로의 깊이
                sortIds[s] = s;
            }
            System.Array.Sort(sortKeys, sortIds, 0, count);

            int w = 0;
            for (int s = count - 1; s >= 0; s--) // 깊이가 큰(먼) 가닥부터
            {
                uint strand = (uint)sortIds[s];
                for (int i = 0; i < n; i++)
                {
                    for (int k = 0; k < passes; k++)
                    {
                        orderData[w++] = strand | ((uint)i << 14) | ((uint)k << 17);
                    }
                }
            }
            orderBuffer.SetData(orderData, 0, 0, w);

            var rp = new RenderParams(hairMat)
            {
                shadowCastingMode = ShadowCastingMode.Off,
                receiveShadows = true,
                worldBounds = currentHair.bounds,
                layer = 0,
                lightProbeUsage = LightProbeUsage.Off,
                reflectionProbeUsage = ReflectionProbeUsage.Off,
            };
            Graphics.RenderPrimitives(rp, MeshTopology.Triangles, currentHair.maxSegments * 6, w);
        }

        void OnDestroy()
        {
            if (currentHair != null) currentHair.Destroy();
            orderBuffer?.Release();
        }

        // 프로토타입의 loadHeadOBJ: 같은 위치의 정점끼리 면 노멀을 평균내서 부드러운 노멀을 만든다.
        static Mesh BuildSmoothedMesh(Mesh src)
        {
            Vector3[] v = src.vertices;
            int[] t = src.triangles;

            var weld = new Dictionary<Vector3Int, int>();
            var group = new int[v.Length];
            for (int i = 0; i < v.Length; i++)
            {
                var key = Vector3Int.RoundToInt(v[i] * 10000f);
                if (!weld.TryGetValue(key, out int g)) { g = weld.Count; weld[key] = g; }
                group[i] = g;
            }

            var sums = new Vector3[weld.Count];
            for (int i = 0; i + 2 < t.Length; i += 3)
            {
                int a = t[i], b = t[i + 1], c = t[i + 2];
                Vector3 fn = Vector3.Cross(v[b] - v[a], v[c] - v[a]).normalized;
                sums[group[a]] += fn;
                sums[group[b]] += fn;
                sums[group[c]] += fn;
            }

            var n = new Vector3[v.Length];
            for (int i = 0; i < v.Length; i++) n[i] = sums[group[i]].normalized;

            var m = new Mesh { name = "Head (smoothed)", indexFormat = IndexFormat.UInt32 };
            m.vertices = v;
            m.triangles = t;
            m.normals = n;
            m.RecalculateBounds();
            return m;
        }

        // --------------------------------------------------------------------
        // 매 프레임
        // --------------------------------------------------------------------
        void Update()
        {
            HandleKeyboard();
            HandleMouse();
            UpdateCamera();
            ApplyMaterialState();
            RunPendingLoad();
            UpdateRootSnap();
            DrawHair();
        }

        // 두상이나 머리카락이 바뀐 뒤(슬라이더를 움직이는 중에는 기다렸다가) 뿌리를 두피에 다시 붙인다.
        // 계산(두상 표면 질의, 충돌 처리)은 백그라운드 스레드에서 하고, 끝나면 메인 스레드에서 GPU에 올린다.
        class SnapResult { public Vector3[] positions; public string log; }
        System.Threading.Tasks.Task<SnapResult> snapTask;
        HairMeshSet snapHair;
        int snapGen, snapGenAtStart;

        void UpdateHeadFit()
        {
            if (fitTask == null || !fitTask.IsCompleted) return;
            var done = fitTask;
            fitTask = null;
            if (done.IsFaulted) { Debug.LogError("[HairDemo] 두상 자동 맞춤 실패: " + done.Exception); return; }
            if (fitGenAtStart != fitGen || headGO == null) return; // 그 사이 두상이 바뀜

            var fit = done.Result;
            headFitScale = fit.scale;
            headFitPos = fit.pos;
            hasHeadFit = true;
            Debug.Log($"[HairDemo] 두상 자동 맞춤: 뿌리-두피 평균 거리 {fit.errBefore:0.0000} -> {fit.errAfter:0.0000} " +
                      $"(뿌리 {fit.rootsUsed}개 사용, 배율 {fit.scale.x:0.000}/{fit.scale.y:0.000}/{fit.scale.z:0.000})");
            Debug.Log($"[HairDemo][시간] 두상 자동 맞춤은 버튼을 누른 뒤 {Time.realtimeSinceStartup - actionStart:0.0}초 만에 끝남");
            ApplyHeadTransform();
        }

        // 루트 틴트(두피 마스크): "머리카락이 빽빽한 두피" 밀도를 3D 텍스처로 만들어 두상 셰이더가 픽셀마다 읽는다.
        // (Unreal의 follicle 마스크처럼 마스크를 텍스처로 두는 방식. 정점마다 계산하면 정점이 듬성한 두상에서 얼룩진다.)
        // 두상이 바뀌거나 머리카락이 바뀔 때마다 다시 계산하며, 백그라운드에서 한다.
        System.Threading.Tasks.Task<ScalpMask.Field> maskTask;
        Texture3D scalpTex;
        int maskGen, maskGenAtStart;

        void StartScalpMask()
        {
            if (currentHair == null) return;
            var hair = currentHair;
            maskGenAtStart = ++maskGen;
            maskTask = System.Threading.Tasks.Task.Run(() =>
            {
                // 머리카락이 실제로 있는 가닥의 뿌리만 쓴다. 짧은 머리처럼 옆/뒤를 민 스타일은 뿌리는 있지만 길이가 0인
                // 가닥이 많은데, 그 뿌리까지 두피로 칠하면 머리카락 없는 맨살이 어두운 갈색 덩어리로 칠해진다.
                const float MinStrandLength = 0.2f; // 월드 단위(원본 0.04)
                var visible = new System.Collections.Generic.List<Vector3>(hair.roots.Length);
                for (int s = 0; s < hair.strandCount; s++)
                {
                    int st = hair.strandStartIdx[s], c = hair.strandPointCount[s];
                    float len = 0f;
                    for (int i = 1; i < c; i++) len += (hair.baseVerts[st + i] - hair.baseVerts[st + i - 1]).magnitude;
                    if (len >= MinStrandLength) visible.Add(hair.roots[s]);
                }
                return ScalpMask.Compute(visible.ToArray(), 0.02f);
            });
        }

        void UpdateScalpMask()
        {
            if (maskTask == null || !maskTask.IsCompleted) return;
            var done = maskTask;
            maskTask = null;
            if (done.IsFaulted) { Debug.LogError("[HairDemo] 두피 마스크 계산 실패: " + done.Exception); return; }
            if (maskGenAtStart != maskGen || headMat == null) return;
            var f = done.Result;
            if (f == null) return;

            if (scalpTex != null) Destroy(scalpTex);
            scalpTex = new Texture3D(f.nx, f.ny, f.nz, TextureFormat.RFloat, false)
            {
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
            };
            scalpTex.SetPixelData(f.data, 0);
            scalpTex.Apply(false, true);

            headMat.SetTexture("_ScalpTex", scalpTex);
            headMat.SetVector("_ScalpOrigin", new Vector4(f.origin.x, f.origin.y, f.origin.z, f.cell));
            headMat.SetVector("_ScalpDims", new Vector4(f.nx, f.ny, f.nz, 1f));
        }

        void UpdateRootSnap()
        {
            UpdateHeadFit();
            UpdateScalpMask();

            // 1) 끝난 계산 반영. 그 사이 두상/머리카락/설정이 또 바뀌었으면 결과를 버린다(곧 새로 계산한다).
            if (snapTask != null && snapTask.IsCompleted)
            {
                var done = snapTask;
                snapTask = null;
                if (done.IsFaulted)
                    Debug.LogError("[HairDemo] 뿌리 두피 보정 실패: " + done.Exception);
                else if (snapHair == currentHair && currentHair != null && snapGenAtStart == snapGen)
                {
                    var swUp = System.Diagnostics.Stopwatch.StartNew();
                    currentHair.SetPositions(done.Result.positions);
                    Debug.Log(done.Result.log);
                    Debug.Log($"[HairDemo][시간] GPU 반영 {swUp.ElapsedMilliseconds}ms / 마지막 버튼을 누른 뒤 머리카락이 완전히 맞춰지기까지 " +
                              $"{Time.realtimeSinceStartup - actionStart:0.0}초");
                }
            }

            // 2) 새 계산 시작
            if (!snapDirty || Time.unscaledTime < snapTime) return;
            if (snapTask != null) { snapTime = Time.unscaledTime + 0.2f; return; } // 이전 계산이 끝나길 기다린다
            snapDirty = false;
            if (currentHair == null || headGO == null || headLocalVerts == null) return;
            StartScalpMask();

            const float RootMaxDist = 0.08f;   // 이보다 먼 뿌리는 두피가 아니라고 보고 건드리지 않는다
            const float HairMargin = 0.01f;    // 가닥이 두상 표면에서 떨어져 있어야 하는 최소 거리
            var hair = currentHair;
            var verts = headLocalVerts;
            var tris = headTris;
            Vector3 hs = headGO.transform.localScale, hp = headGO.transform.position;
            bool doRoots = snapRoots, doCollide = snapCollide;
            snapHair = hair;
            snapGenAtStart = snapGen;

            snapTask = System.Threading.Tasks.Task.Run(() =>
            {
                var sw = System.Diagnostics.Stopwatch.StartNew();
                var surf = new HeadSurface(verts, tris, hs, hp, RootMaxDist);
                long tSurf = sw.ElapsedMilliseconds;

                var stats = new RootSnapper.Stats();
                var offsets = doRoots ? RootSnapper.ComputeOffsets(surf, hair.roots, RootMaxDist, 0.05f, ref stats) : null;
                long tRoots = sw.ElapsedMilliseconds;

                var positions = hair.ComputePositions(offsets);
                int pushed = 0;
                if (doCollide)
                    pushed = RootSnapper.PushOut(surf, positions, hair.strandStartIdx, hair.strandPointCount, HairMargin, RootMaxDist);
                long tAll = sw.ElapsedMilliseconds;

                return new SnapResult
                {
                    positions = positions,
                    log = $"[HairDemo] 뿌리 두피 보정: 뿌리-표면 평균 거리 {stats.meanBefore:0.0000}, 평균 이동량 {stats.meanMoved:0.0000} " +
                          $"(보정 {stats.moved}개, 제외 {stats.skipped}개) / 파고든 점 {pushed}개를 밀어냄 / " +
                          $"소요 {tAll}ms (표면 구축 {tSurf}ms, 뿌리 {tRoots - tSurf}ms, 충돌 {tAll - tRoots}ms)",
                };
            });
        }

        void MarkSnapDirty()
        {
            snapDirty = true;
            snapGen++;
            snapTime = Time.unscaledTime + 0.4f;
        }

        void HandleKeyboard()
        {
            var kb = Keyboard.current;
            if (kb == null) return;

            if (kb.digit1Key.wasPressedThisFrame) RequestHairSlot(0);
            if (kb.digit2Key.wasPressedThisFrame) RequestHairSlot(1);
            if (kb.digit3Key.wasPressedThisFrame) RequestHairSlot(2);
            if (kb.digit4Key.wasPressedThisFrame) RequestHairSlot(3);
            if (kb.digit5Key.wasPressedThisFrame) RequestHairSlot(4);
            if (kb.digit6Key.wasPressedThisFrame) RequestHairSlot(5);
            if (kb.digit7Key.wasPressedThisFrame) noiseMode = 1 - noiseMode;
            if (kb.digit8Key.wasPressedThisFrame) interpMode = 1 - interpMode;
            if (kb.digit9Key.wasPressedThisFrame) colorIndex = (colorIndex + 1) % Presets.Length;
            if (kb.rKey.wasPressedThisFrame) ResetCamera();

            // 손상도: 꾹 누르고 있어도 0.05씩 반복 (프로토타입과 동일)
            int dir = 0;
            if (kb.upArrowKey.isPressed) dir = 1;
            else if (kb.downArrowKey.isPressed) dir = -1;
            if (dir != 0)
            {
                damageRepeat -= Time.unscaledDeltaTime;
                if (damageRepeat <= 0f)
                {
                    damage = Mathf.Clamp01(damage + dir * 0.05f);
                    damageRepeat = (kb.upArrowKey.wasPressedThisFrame || kb.downArrowKey.wasPressedThisFrame) ? 0.35f : 0.06f;
                }
            }
            else damageRepeat = 0f;

            if (kb.escapeKey.wasPressedThisFrame)
            {
#if UNITY_EDITOR
                UnityEditor.EditorApplication.isPlaying = false;
#else
                Application.Quit();
#endif
            }
        }

        void HandleMouse()
        {
            var mouse = Mouse.current;
            if (mouse == null) return;

            Vector2 pos = mouse.position.ReadValue();
            // OnGUI가 기록한 UI 영역(위쪽이 y=0인 화면 픽셀) 위에 마우스가 있으면 카메라를 움직이지 않는다.
            Vector2 guiPos = new Vector2(pos.x, Screen.height - pos.y);
            pointerOverHud = false;
            for (int i = 0; i < uiRectsPx.Count; i++)
                if (uiRectsPx[i].Contains(guiPos)) { pointerOverHud = true; break; }

            Vector2 delta = mouse.delta.ReadValue();
            bool rotating = mouse.leftButton.isPressed;
            bool zooming = mouse.rightButton.isPressed;
            bool panning = mouse.middleButton.isPressed;

            if (!pointerOverHud)
            {
                if (rotating)
                {
                    yaw += delta.x * 0.005f;
                    pitch = Mathf.Clamp(pitch + delta.y * 0.005f, -1.2f, 1.2f);
                }
                if (zooming)
                {
                    dist = Mathf.Clamp(dist * Mathf.Exp(-delta.y * 0.01f), 0.3f, 20f);
                }
                if (panning)
                {
                    float panSpeed = 0.0015f * dist;
                    target -= cam.transform.right * (delta.x * panSpeed);
                    target -= cam.transform.up * (delta.y * panSpeed);
                }

                float scroll = mouse.scroll.ReadValue().y;
                if (Mathf.Abs(scroll) > 0.01f)
                {
                    dist = Mathf.Clamp(dist * Mathf.Exp(-Mathf.Sign(scroll) * 0.10f), 0.3f, 20f);
                }
            }
        }

        void ResetCamera()
        {
            yaw = 0f; pitch = 0f; dist = 2.5f; target = Vector3.zero;
        }

        void UpdateCamera()
        {
            // 프로토타입(오른손 좌표)의 eye 공식에서 X축만 뒤집어 같은 화면이 나오게 한다.
            Vector3 eyeOffset = new Vector3(
                -dist * Mathf.Cos(pitch) * Mathf.Sin(yaw),
                dist * Mathf.Sin(pitch),
                dist * Mathf.Cos(pitch) * Mathf.Cos(yaw));
            cam.transform.position = target + eyeOffset;
            cam.transform.LookAt(target, Vector3.up);

            // 섀도우맵이 장면 크기에 맞게 촘촘하도록, 카메라가 보는 거리까지만 그림자를 계산한다.
            if (urp != null) urp.shadowDistance = dist + 3f;
        }

        void ApplyMaterialState()
        {
            if (hairMat == null) return;
            hairMat.SetFloat("_Damage", damage);
            hairMat.SetColor("_BaseHairColor", Presets[colorIndex].color);
            hairMat.SetFloat("_NoiseMode", noiseMode);
            hairMat.SetFloat("_InterpMode", interpMode);
            hairMat.SetFloat("_ShadowAmount", hairReceivesShadow ? 1f : 0f);
            hairMat.SetFloat("_HairWidth", hairWidth);
            hairMat.SetFloat("_SplitSpread", splitSpread);
            hairMat.SetFloat("_CuticleNoise", cuticleNoise);
            hairMat.SetFloat("_NodeOn", nodosaOn ? 1f : 0f);
            hairMat.SetFloat("_RootTint", rootTint ? 1f : 0f);
            if (headMat != null)
            {
                headMat.SetFloat("_ScalpTint", rootTint ? 1f : 0f);
                headMat.SetColor("_ScalpColor", Presets[colorIndex].color);
            }
            hairMat.SetFloat("_DebugMode", debugView);

            if (cam != null) cam.backgroundColor = Backgrounds[backgroundIndex].color;
            if (keyLight != null) keyLight.shadowBias = shadowBias;
        }

    }
}
