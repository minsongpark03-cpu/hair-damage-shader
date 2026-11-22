using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.Rendering;

namespace HairDemo
{
    /// <summary>
    /// OpenGL 프로토타입의 loadHairDATA + processAndUploadMesh를 옮긴 로더.
    /// .data(바이너리: 가닥 수, [점 개수, xyz*N]*) 파일을 읽어서 라인 토폴로지 메쉬를 만든다.
    ///   vertices = 가닥 위치,  normals = 가닥 접선,
    ///   uv0 = (뿌리0~끝1, 가닥별 랜덤, 정규화된 가닥 길이, 0)
    /// </summary>
    /// <summary>한 헤어 모델의 두 가지 메쉬. lines = 그림자용 라인, ribbons = 화면에 그리는 카메라 대면 띠.</summary>
    public class HairMeshSet
    {
        public Mesh lines;               // 그림자 캐스터용 라인 메쉬

        // 화면용 머리카락: 메쉬 대신 버퍼에서 셰이더가 직접 읽는다.
        public GraphicsBuffer hairA, hairB, strandU, strandF;
        public Vector3[] strandCenters;  // 가닥별 중심(깊이 정렬 키 계산용)
        public Vector3[] roots;          // 가닥별 뿌리 위치(두상 자동 맞춤용)
        public float maxStrandLength;    // 가장 긴 가닥의 길이(월드 단위). 셰이더가 정규화 길이를 실제 길이로 되돌릴 때 쓴다
        public int strandCount;
        public int maxSegments;          // 가장 긴 가닥의 선분 수 (드로우 정점 수 = maxSegments * 6)
        public Bounds bounds;

        // 뿌리 두피 보정용: 원래 위치와 가닥 구성을 보관해 두고, 보정이 바뀔 때마다 이 원본에서 다시 계산한다.
        public Vector3[] baseVerts;
        public int[] strandStartIdx, strandPointCount;
        Vector4[] scratchA;
        Vector3[] scratchLine;
        public bool Released { get; private set; }

        /// <summary>
        /// 가닥 뿌리를 offsets[가닥]만큼 옮긴다. 이동량은 뿌리에서 100%, 가닥을 따라 25% 지점에서 0%가 되도록
        /// 서서히 줄여서 머리카락 모양은 그대로 두고 뿌리만 두피에 붙는다. offsets가 null이면 원래 위치로 되돌린다.
        /// </summary>
        public void ApplyRootOffsets(Vector3[] offsets)
        {
            SetPositions(ComputePositions(offsets));
        }

        /// <summary>
        /// 뿌리 이동량을 적용한 점 위치를 새 배열로 계산한다. 유니티 오브젝트를 건드리지 않아서
        /// 백그라운드 스레드에서 불러도 된다(결과는 메인 스레드에서 SetPositions로 올린다).
        /// </summary>
        public Vector3[] ComputePositions(Vector3[] offsets)
        {
            var dst = new Vector3[baseVerts.Length];
            for (int s = 0; s < strandCount; s++)
            {
                int st = strandStartIdx[s], c = strandPointCount[s];
                Vector3 o = offsets != null ? offsets[s] : Vector3.zero;
                for (int i = 0; i < c; i++)
                {
                    float t = c > 1 ? (float)i / (c - 1) : 0f;
                    float w = 1f - Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t / 0.25f));
                    dst[st + i] = baseVerts[st + i] + o * w;
                }
            }
            return dst;
        }

        /// <summary>계산된 점 위치를 GPU 버퍼(화면용)와 그림자용 라인 메쉬에 올린다. 메인 스레드 전용.</summary>
        public void SetPositions(Vector3[] positions)
        {
            if (positions == null || positions.Length != baseVerts.Length) return;
            if (scratchA == null) scratchA = new Vector4[baseVerts.Length];
            scratchLine = positions;
            for (int i = 0; i < positions.Length; i++)
                scratchA[i] = new Vector4(positions[i].x, positions[i].y, positions[i].z, 0f);
            hairA.SetData(scratchA);
            if (lines != null) lines.SetVertices(scratchLine);
        }

        public void Destroy()
        {
            if (lines != null) UnityEngine.Object.Destroy(lines);
            hairA?.Release(); hairB?.Release(); strandU?.Release(); strandF?.Release();
        }
    }

    public static class HairStrandLoader
    {
        // normalizeHairMesh(): 원본 좌표를 5배로 키우고 두피(y=1.65)가 원점에 오도록 내린다.
        public const float FixedScale = 5.0f;
        public const float BaseHeight = 1.65f;

        public static HairMeshSet Load(string path, bool flipX, int seed = 1234)
        {
            if (!File.Exists(path))
            {
                Debug.LogError("[HairDemo] 헤어 데이터를 찾을 수 없습니다: " + path);
                return null;
            }

            byte[] bytes = File.ReadAllBytes(path);
            int off = 0;
            uint numStrands = BitConverter.ToUInt32(bytes, off); off += 4;
            if (numStrands == 0 || numStrands > 1000000)
            {
                Debug.LogError("[HairDemo] 비정상적인 헤어 파일입니다: " + path);
                return null;
            }

            var starts = new List<int>((int)numStrands);
            var counts = new List<int>((int)numStrands);
            var pts = new List<Vector3>(1100000);
            float yOffset = -BaseHeight * FixedScale;

            for (uint s = 0; s < numStrands; s++)
            {
                if (off + 4 > bytes.Length) break;
                uint n = BitConverter.ToUInt32(bytes, off); off += 4;
                if (n == 0 || n > 10000) break;
                if ((long)off + (long)n * 12 > bytes.Length) break;

                starts.Add(pts.Count);
                counts.Add((int)n);
                for (uint k = 0; k < n; k++)
                {
                    float x = BitConverter.ToSingle(bytes, off);
                    float y = BitConverter.ToSingle(bytes, off + 4);
                    float z = BitConverter.ToSingle(bytes, off + 8);
                    off += 12;
                    if (!float.IsFinite(x)) x = 0f;
                    if (!float.IsFinite(y)) y = 0f;
                    if (!float.IsFinite(z)) z = 0f;

                    float px = x * FixedScale;
                    if (flipX) px = -px; // OpenGL(오른손) -> Unity(왼손) 좌표계 변환
                    pts.Add(new Vector3(px, y * FixedScale + yOffset, z * FixedScale));
                }
            }

            int strandTotal = starts.Count;
            if (strandTotal == 0 || pts.Count == 0) return null;

            // 가닥별 길이와 최대 길이 (computeStrandParams)
            var lengths = new float[strandTotal];
            float maxLen = 0.0001f;
            for (int s = 0; s < strandTotal; s++)
            {
                int st = starts[s], c = counts[s];
                float total = 0f;
                for (int i = 0; i < c - 1; i++) total += (pts[st + i + 1] - pts[st + i]).magnitude;
                lengths[s] = total;
                if (total > maxLen) maxLen = total;
            }

            int vertCount = pts.Count;
            var verts = pts.ToArray();
            var tangents = new Vector3[vertCount];
            var uvs = new Vector4[vertCount];

            int segCount = 0;
            for (int s = 0; s < strandTotal; s++) segCount += Math.Max(0, counts[s] - 1);
            var indices = new int[segCount * 2];

            // 바깥 머리일수록 잘 갈라지므로, 가닥이 두피 중심축(y축)에서 얼마나 바깥쪽에 있는지를
            // 0~1로 미리 계산한다. 가닥의 65% 지점의 반경을 기준으로 하고, 상·하위 10%는 잘라서
            // 극단값 하나가 전체 분포를 왜곡하지 않게 한다.
            var radial = new float[strandTotal];
            for (int s = 0; s < strandTotal; s++)
            {
                int st = starts[s], c = counts[s];
                int mid = st + Mathf.Clamp(Mathf.RoundToInt(0.65f * (c - 1)), 0, c - 1);
                radial[s] = Mathf.Sqrt(verts[mid].x * verts[mid].x + verts[mid].z * verts[mid].z);
            }
            var sortedRadial = (float[])radial.Clone();
            Array.Sort(sortedRadial);
            float radialLo = sortedRadial[(int)(strandTotal * 0.10f)];
            float radialHi = sortedRadial[Math.Min(strandTotal - 1, (int)(strandTotal * 0.90f))];
            float radialRange = Mathf.Max(radialHi - radialLo, 1e-4f);
            var outerPerVertex = new float[vertCount];

            var rng = new System.Random(seed);
            int ii = 0;
            Vector3 mn = verts[0], mx = verts[0];

            for (int s = 0; s < strandTotal; s++)
            {
                int st = starts[s], c = counts[s];
                float rnd = (float)rng.NextDouble();
                float normLen = lengths[s] / maxLen;
                float outerS = Mathf.Clamp01((radial[s] - radialLo) / radialRange);

                for (int i = 0; i < c; i++)
                {
                    outerPerVertex[st + i] = outerS;
                    Vector3 t;
                    if (c < 2) t = Vector3.up;
                    else if (i == 0) t = verts[st + 1] - verts[st];
                    else if (i == c - 1) t = verts[st + c - 1] - verts[st + c - 2];
                    else t = verts[st + i + 1] - verts[st + i - 1];

                    float len = t.magnitude;
                    tangents[st + i] = len > 1e-8f ? t / len : Vector3.up;

                    uvs[st + i] = new Vector4((float)i / Math.Max(1, c - 1), rnd, normLen, 0f);

                    Vector3 p = verts[st + i];
                    mn = Vector3.Min(mn, p);
                    mx = Vector3.Max(mx, p);

                    if (i < c - 1)
                    {
                        indices[ii++] = st + i;
                        indices[ii++] = st + i + 1;
                    }
                }
            }

            string baseName = Path.GetFileNameWithoutExtension(path);

            // 손상 변위로 바깥으로 퍼지는 만큼 여유를 둬서 컬링되지 않게 한다.
            var bounds = new Bounds((mn + mx) * 0.5f, (mx - mn) + Vector3.one * 2f);

            // 1) 라인 메쉬 — 그림자 캐스터 전용(1픽셀 라인이어도 섀도우맵에는 충분)
            var lineMesh = new Mesh { name = baseName + " (lines)", indexFormat = IndexFormat.UInt32 };
            lineMesh.SetVertices(verts);
            lineMesh.SetNormals(tangents);
            lineMesh.SetUVs(0, uvs);
            lineMesh.SetIndices(indices, MeshTopology.Lines, 0, false);
            lineMesh.bounds = bounds;
            lineMesh.UploadMeshData(false); // 뿌리 보정으로 정점을 다시 쓰므로 CPU 복사본을 남긴다

            // 2) 화면용 머리카락 데이터 — 메쉬 대신 GraphicsBuffer로 올린다.
            //    셰이더가 SV_VertexID/SV_InstanceID로 직접 읽어서 점마다 리본 사각형을 만든다.
            //    (가닥 단위로 깊이 정렬한 순서대로 그리기 위해서)
            if (strandTotal > 16384)
            {
                Debug.LogError("[HairDemo] 가닥이 16384개를 넘으면 정렬 인덱스 비트가 모자랍니다.");
                return null;
            }

            var hairA = new Vector4[vertCount];
            var hairB = new Vector4[vertCount];
            for (int i = 0; i < vertCount; i++)
            {
                hairA[i] = new Vector4(verts[i].x, verts[i].y, verts[i].z, 0f);
                hairB[i] = new Vector4(tangents[i].x, tangents[i].y, tangents[i].z, 0f);
            }

            var strandU = new uint2[strandTotal];
            var strandF = new Vector4[strandTotal];
            var centers = new Vector3[strandTotal];
            var roots = new Vector3[strandTotal]; // 두상 자동 맞춤용: 가닥의 첫 점 = 두피에 닿는 위치
            int maxSegs = 1;
            for (int s = 0; s < strandTotal; s++)
            {
                int st = starts[s], c = counts[s];
                roots[s] = verts[st];
                strandU[s] = new uint2 { x = (uint)st, y = (uint)c };
                strandF[s] = new Vector4(uvs[st].y, uvs[st].z, outerPerVertex[st], 0f);
                maxSegs = Math.Max(maxSegs, c - 1);

                Vector3 sum = Vector3.zero;
                for (int i = 0; i < c; i++) sum += verts[st + i];
                centers[s] = sum / Math.Max(1, c);
            }

            var set = new HairMeshSet
            {
                lines = lineMesh,
                strandCount = strandTotal,
                maxSegments = maxSegs,
                strandCenters = centers,
                maxStrandLength = maxLen,
                roots = roots,
                baseVerts = verts,
                strandStartIdx = starts.ToArray(),
                strandPointCount = counts.ToArray(),
                bounds = bounds,
                hairA = new GraphicsBuffer(GraphicsBuffer.Target.Structured, vertCount, 16),
                hairB = new GraphicsBuffer(GraphicsBuffer.Target.Structured, vertCount, 16),
                strandU = new GraphicsBuffer(GraphicsBuffer.Target.Structured, strandTotal, 8),
                strandF = new GraphicsBuffer(GraphicsBuffer.Target.Structured, strandTotal, 16),
            };
            set.hairA.SetData(hairA);
            set.hairB.SetData(hairB);
            set.strandU.SetData(strandU);
            set.strandF.SetData(strandF);

            Debug.Log($"[HairDemo] {Path.GetFileName(path)}: 가닥 {strandTotal}개, 점 {vertCount}개 로드");
            return set;
        }

        struct uint2 { public uint x, y; }
    }
}
