using UnityEngine;

namespace HairDemo
{
    /// <summary>
    /// 두상 표면 질의: 월드 공간의 점에서 가장 가까운 표면 점과 바깥쪽 법선을 빠르게 구한다.
    /// 정점을 조밀한 3D 격자에 넣고(CSR), 표면에서 먼 칸은 O(1)로 걸러낸다(머리카락 점 100만 개를 매번 검사하므로).
    /// </summary>
    public class HeadSurface
    {
        readonly Vector3[] wv;      // 월드 공간 정점
        readonly int[] tris;
        readonly int[] vtStart, vtList; // 정점 -> 삼각형 (CSR)
        readonly float sign;        // +1: 삼각형 감김 방향이 바깥쪽, -1: 뒤집어야 바깥쪽
        readonly Vector3 mn;
        readonly float cell;
        readonly int nx, ny, nz;
        readonly int[] cellStart, cellList; // 격자 칸 -> 정점 (CSR)
        readonly bool[] near;       // 표면 정점이 ringMax칸 안에 있는 칸
        readonly int ringMax;

        public HeadSurface(Vector3[] localVerts, int[] triangles, Vector3 scale, Vector3 pos, float maxQueryDist)
        {
            tris = triangles;
            int nv = localVerts.Length, nt = tris.Length / 3;
            wv = new Vector3[nv];
            for (int i = 0; i < nv; i++)
                wv[i] = new Vector3(localVerts[i].x * scale.x + pos.x,
                                    localVerts[i].y * scale.y + pos.y,
                                    localVerts[i].z * scale.z + pos.z);

            // 정점 -> 삼각형
            vtStart = new int[nv + 1];
            for (int i = 0; i < tris.Length; i++) vtStart[tris[i] + 1]++;
            for (int i = 0; i < nv; i++) vtStart[i + 1] += vtStart[i];
            var fill = new int[nv];
            vtList = new int[tris.Length];
            double volume = 0;
            for (int t = 0; t < nt; t++)
            {
                for (int k = 0; k < 3; k++)
                {
                    int v = tris[3 * t + k];
                    vtList[vtStart[v] + fill[v]++] = t;
                }
                // 부호 있는 부피(발산 정리): 양수면 감김 방향이 바깥쪽
                Vector3 a = wv[tris[3 * t]], b = wv[tris[3 * t + 1]], c = wv[tris[3 * t + 2]];
                volume += Vector3.Dot(a, Vector3.Cross(b, c));
            }
            sign = volume >= 0 ? 1f : -1f;

            // 격자
            Vector3 lo = wv[0], hi = wv[0];
            for (int i = 1; i < nv; i++) { lo = Vector3.Min(lo, wv[i]); hi = Vector3.Max(hi, wv[i]); }
            Vector3 size = hi - lo;
            cell = Mathf.Max(size.x, Mathf.Max(size.y, size.z)) / 40f;
            ringMax = Mathf.Max(1, Mathf.CeilToInt(maxQueryDist / cell));
            int pad = ringMax + 1;
            mn = lo - Vector3.one * (pad * cell);
            nx = Mathf.CeilToInt(size.x / cell) + 2 * pad + 1;
            ny = Mathf.CeilToInt(size.y / cell) + 2 * pad + 1;
            nz = Mathf.CeilToInt(size.z / cell) + 2 * pad + 1;

            int cells = nx * ny * nz;
            cellStart = new int[cells + 1];
            var cellOf = new int[nv];
            for (int i = 0; i < nv; i++) { cellOf[i] = CellIndex(wv[i]); cellStart[cellOf[i] + 1]++; }
            for (int i = 0; i < cells; i++) cellStart[i + 1] += cellStart[i];
            var cfill = new int[cells];
            cellList = new int[nv];
            for (int i = 0; i < nv; i++) cellList[cellStart[cellOf[i]] + cfill[cellOf[i]]++] = i;

            // 표면 정점 칸을 ringMax칸만큼 부풀린 마스크
            near = new bool[cells];
            for (int i = 0; i < nv; i++)
            {
                int c = cellOf[i];
                int cx = c % nx, cy = (c / nx) % ny, cz = c / (nx * ny);
                for (int dz = -ringMax; dz <= ringMax; dz++)
                for (int dy = -ringMax; dy <= ringMax; dy++)
                for (int dx = -ringMax; dx <= ringMax; dx++)
                    near[(cx + dx) + nx * ((cy + dy) + ny * (cz + dz))] = true;
            }
        }

        int CellIndex(Vector3 p)
        {
            int cx = Mathf.Clamp(Mathf.FloorToInt((p.x - mn.x) / cell), 0, nx - 1);
            int cy = Mathf.Clamp(Mathf.FloorToInt((p.y - mn.y) / cell), 0, ny - 1);
            int cz = Mathf.Clamp(Mathf.FloorToInt((p.z - mn.z) / cell), 0, nz - 1);
            return cx + nx * (cy + ny * cz);
        }

        /// <summary>q에서 maxDist 안의 가장 가까운 표면 점 p와 바깥쪽 법선 n. 없으면 false.</summary>
        public bool Closest(Vector3 q, float maxDist, out Vector3 p, out Vector3 n)
        {
            p = q; n = Vector3.up;
            int cx = Mathf.FloorToInt((q.x - mn.x) / cell);
            int cy = Mathf.FloorToInt((q.y - mn.y) / cell);
            int cz = Mathf.FloorToInt((q.z - mn.z) / cell);
            if (cx < 0 || cy < 0 || cz < 0 || cx >= nx || cy >= ny || cz >= nz) return false;
            if (!near[cx + nx * (cy + ny * cz)]) return false;

            int best = -1; float bestD = float.MaxValue;
            for (int dz = -ringMax; dz <= ringMax; dz++)
            {
                int z = cz + dz; if (z < 0 || z >= nz) continue;
                for (int dy = -ringMax; dy <= ringMax; dy++)
                {
                    int y = cy + dy; if (y < 0 || y >= ny) continue;
                    for (int dx = -ringMax; dx <= ringMax; dx++)
                    {
                        int x = cx + dx; if (x < 0 || x >= nx) continue;
                        int c = x + nx * (y + ny * z);
                        for (int a = cellStart[c]; a < cellStart[c + 1]; a++)
                        {
                            int i = cellList[a];
                            float d = (wv[i] - q).sqrMagnitude;
                            if (d < bestD) { bestD = d; best = i; }
                        }
                    }
                }
            }
            if (best < 0) return false;

            // 가장 가까운 정점에 붙은 삼각형 중 실제로 가장 가까운 점
            Vector3 bestP = wv[best];
            Vector3 bestN = Vector3.up;
            float bestTri = float.MaxValue;
            for (int a = vtStart[best]; a < vtStart[best + 1]; a++)
            {
                int t = vtList[a];
                Vector3 va = wv[tris[3 * t]], vb = wv[tris[3 * t + 1]], vc = wv[tris[3 * t + 2]];
                Vector3 cp = ClosestOnTriangle(q, va, vb, vc);
                float d = (cp - q).sqrMagnitude;
                if (d < bestTri)
                {
                    bestTri = d; bestP = cp;
                    Vector3 fn = Vector3.Cross(vb - va, vc - va);
                    bestN = fn.sqrMagnitude > 1e-20f ? fn.normalized * sign : Vector3.up;
                }
            }
            if (bestTri > maxDist * maxDist) return false;
            p = bestP; n = bestN;
            return true;
        }

        /// <summary>격자로 q 근처의 가장 가까운 정점 번호를 찾는다(가닥의 첫 점처럼 출발점이 없을 때). 없으면 -1.</summary>
        public int GridNearest(Vector3 q)
        {
            int cx = Mathf.FloorToInt((q.x - mn.x) / cell);
            int cy = Mathf.FloorToInt((q.y - mn.y) / cell);
            int cz = Mathf.FloorToInt((q.z - mn.z) / cell);
            if (cx < 0 || cy < 0 || cz < 0 || cx >= nx || cy >= ny || cz >= nz) return -1;
            if (!near[cx + nx * (cy + ny * cz)]) return -1;

            int best = -1; float bestD = float.MaxValue;
            for (int dz = -ringMax; dz <= ringMax; dz++)
            {
                int z = cz + dz; if (z < 0 || z >= nz) continue;
                for (int dy = -ringMax; dy <= ringMax; dy++)
                {
                    int y = cy + dy; if (y < 0 || y >= ny) continue;
                    for (int dx = -ringMax; dx <= ringMax; dx++)
                    {
                        int x = cx + dx; if (x < 0 || x >= nx) continue;
                        int c = x + nx * (y + ny * z);
                        for (int a = cellStart[c]; a < cellStart[c + 1]; a++)
                        {
                            int i = cellList[a];
                            float d = (wv[i] - q).sqrMagnitude;
                            if (d < bestD) { bestD = d; best = i; }
                        }
                    }
                }
            }
            return best;
        }

        /// <summary>
        /// 빠른 질의: 이전 점의 결과(seed 정점)에서 출발해 메쉬 이웃 중 더 가까운 정점으로 계속 내려간다.
        /// 머리카락 가닥은 인접한 점끼리 가까워서 몇 걸음이면 끝난다(격자 전체를 훑는 것보다 훨씬 싸다).
        /// dist는 가장 가까운 표면 점까지의 거리(maxDist보다 멀어도 채워 준다). maxDist 안이면 true.
        /// </summary>
        public bool ClosestFrom(ref int seed, Vector3 q, float maxDist, out Vector3 p, out Vector3 n, out float dist)
        {
            p = q; n = Vector3.up; dist = float.MaxValue;
            if (seed < 0) seed = GridNearest(q);
            if (seed < 0) return false;

            int cur = seed;
            float cd = (wv[cur] - q).sqrMagnitude;
            for (int step = 0; step < 64; step++)
            {
                int best = cur; float bd = cd;
                for (int a = vtStart[cur]; a < vtStart[cur + 1]; a++)
                {
                    int t = vtList[a] * 3;
                    for (int k = 0; k < 3; k++)
                    {
                        int v = tris[t + k];
                        float d = (wv[v] - q).sqrMagnitude;
                        if (d < bd) { bd = d; best = v; }
                    }
                }
                if (best == cur) break;
                cur = best; cd = bd;
            }
            seed = cur;

            Vector3 bestP = wv[cur];
            Vector3 bestN = Vector3.up;
            float bestTri = float.MaxValue;
            for (int a = vtStart[cur]; a < vtStart[cur + 1]; a++)
            {
                int t = vtList[a];
                Vector3 va = wv[tris[3 * t]], vb = wv[tris[3 * t + 1]], vc = wv[tris[3 * t + 2]];
                Vector3 cp = ClosestOnTriangle(q, va, vb, vc);
                float d = (cp - q).sqrMagnitude;
                if (d < bestTri)
                {
                    bestTri = d; bestP = cp;
                    Vector3 fn = Vector3.Cross(vb - va, vc - va);
                    bestN = fn.sqrMagnitude > 1e-20f ? fn.normalized * sign : Vector3.up;
                }
            }
            dist = Mathf.Sqrt(bestTri);
            if (dist > maxDist) return false;
            p = bestP; n = bestN;
            return true;
        }

        // Ericson, Real-Time Collision Detection: 점 p에서 삼각형 abc 위의 가장 가까운 점
        static Vector3 ClosestOnTriangle(Vector3 p, Vector3 a, Vector3 b, Vector3 c)
        {
            Vector3 ab = b - a, ac = c - a, ap = p - a;
            float d1 = Vector3.Dot(ab, ap), d2 = Vector3.Dot(ac, ap);
            if (d1 <= 0f && d2 <= 0f) return a;

            Vector3 bp = p - b;
            float d3 = Vector3.Dot(ab, bp), d4 = Vector3.Dot(ac, bp);
            if (d3 >= 0f && d4 <= d3) return b;

            float vc = d1 * d4 - d3 * d2;
            if (vc <= 0f && d1 >= 0f && d3 <= 0f)
                return a + ab * (d1 / (d1 - d3));

            Vector3 cp = p - c;
            float d5 = Vector3.Dot(ab, cp), d6 = Vector3.Dot(ac, cp);
            if (d6 >= 0f && d5 <= d6) return c;

            float vb = d5 * d2 - d1 * d6;
            if (vb <= 0f && d2 >= 0f && d6 <= 0f)
                return a + ac * (d2 / (d2 - d6));

            float va = d3 * d6 - d5 * d4;
            if (va <= 0f && (d4 - d3) >= 0f && (d5 - d6) >= 0f)
                return b + (c - b) * ((d4 - d3) / ((d4 - d3) + (d5 - d6)));

            float denom = 1f / (va + vb + vc);
            return a + ab * (vb * denom) + ac * (vc * denom);
        }
    }

    /// <summary>
    /// 머리카락을 두상에 맞추는 보정 두 가지.
    ///  1) ComputeOffsets: 뿌리를 가장 가까운 두피 표면 점으로 옮기는 이동량(이웃 뿌리끼리 평균해서 부드럽게).
    ///  2) PushOut: 가닥의 모든 점이 두상 안쪽으로 파고들었으면 표면 바깥으로 밀어낸다(충돌 처리).
    /// </summary>
    public static class RootSnapper
    {
        public struct Stats { public float meanBefore, meanMoved; public int moved, skipped; public int pushed; }

        public static Vector3[] ComputeOffsets(HeadSurface surf, Vector3[] roots, float maxDist, float smoothRadius, ref Stats stats)
        {
            int n = roots.Length;
            var off = new Vector3[n];
            var valid = new bool[n];
            double sumBefore = 0;
            for (int r = 0; r < n; r++)
            {
                if (surf.Closest(roots[r], maxDist, out var p, out _))
                {
                    off[r] = p - roots[r];
                    valid[r] = true;
                    sumBefore += off[r].magnitude;
                }
                else stats.skipped++;
            }
            int validCount = 0;
            for (int r = 0; r < n; r++) if (valid[r]) validCount++;
            stats.meanBefore = (float)(sumBefore / Mathf.Max(1, validCount));

            // 가까운 뿌리끼리 평균(가우시안)
            float rc = Mathf.Max(smoothRadius, 1e-4f);
            var rgrid = new System.Collections.Generic.Dictionary<long, System.Collections.Generic.List<int>>();
            for (int r = 0; r < n; r++)
            {
                if (!valid[r]) continue;
                long key = Key(Cell(roots[r].x, rc), Cell(roots[r].y, rc), Cell(roots[r].z, rc));
                if (!rgrid.TryGetValue(key, out var list)) { list = new System.Collections.Generic.List<int>(8); rgrid[key] = list; }
                list.Add(r);
            }

            var result = new Vector3[n];
            float sigma2 = 2f * (smoothRadius * 0.5f) * (smoothRadius * 0.5f);
            double sumMoved = 0;
            for (int r = 0; r < n; r++)
            {
                if (!valid[r]) continue;
                int cx = Cell(roots[r].x, rc), cy = Cell(roots[r].y, rc), cz = Cell(roots[r].z, rc);
                Vector3 acc = Vector3.zero; float wsum = 0f;
                for (int dx = -1; dx <= 1; dx++)
                for (int dy = -1; dy <= 1; dy++)
                for (int dz = -1; dz <= 1; dz++)
                {
                    if (!rgrid.TryGetValue(Key(cx + dx, cy + dy, cz + dz), out var list)) continue;
                    foreach (int o in list)
                    {
                        float d2 = (roots[o] - roots[r]).sqrMagnitude;
                        if (d2 > smoothRadius * smoothRadius) continue;
                        float w = Mathf.Exp(-d2 / sigma2);
                        acc += off[o] * w; wsum += w;
                    }
                }
                result[r] = wsum > 0f ? acc / wsum : off[r];
                sumMoved += result[r].magnitude;
                stats.moved++;
            }
            stats.meanMoved = (float)(sumMoved / Mathf.Max(1, stats.moved));
            return result;
        }

        /// <summary>
        /// pts(월드 위치)를 제자리에서 수정한다. 두상 표면 안쪽이거나 표면에서 margin보다 가까운 점을
        /// 표면 바깥 margin 위치로 밀어낸다. 밀어낸 양은 가닥을 따라 조금 평균해서 가닥이 꺾이지 않게 한다.
        /// 뿌리(첫 점)는 건드리지 않는다(이미 두피에 붙어 있다).
        /// 속도: 가닥을 따라 이전 점의 결과에서 출발해 찾고, 표면에서 먼 구간은 점을 여러 개씩 건너뛴다.
        /// </summary>
        public static int PushOut(HeadSurface surf, Vector3[] pts, int[] starts, int[] counts, float margin, float searchDist)
        {
            int pushed = 0;
            var d = new Vector3[512];
            for (int s = 0; s < starts.Length; s++)
            {
                int st = starts[s], c = counts[s];
                if (c < 2) continue;
                if (d.Length < c) d = new Vector3[c];

                // 점 간격의 최댓값: 표면에서 먼 점은 이 간격으로 몇 개를 건너뛰어도 안전하다.
                float maxStep = 1e-4f;
                for (int i = 1; i < c; i++) maxStep = Mathf.Max(maxStep, (pts[st + i] - pts[st + i - 1]).magnitude);

                bool any = false;
                d[0] = Vector3.zero;
                int seed = surf.GridNearest(pts[st]);
                for (int i = 1; i < c; i++)
                {
                    d[i] = Vector3.zero;
                    if (surf.ClosestFrom(ref seed, pts[st + i], searchDist, out var p, out var n, out float dist))
                    {
                        float signed = Vector3.Dot(pts[st + i] - p, n);
                        if (signed < margin) { d[i] = n * (margin - signed); any = true; pushed++; }
                    }
                    else if (dist < float.MaxValue)
                    {
                        // 아직 searchDist보다 (dist - searchDist)만큼 더 멀다 -> 그 거리 안에서는 닿을 수 없으니 건너뛴다.
                        int skip = (int)((dist - searchDist) / maxStep) - 1;
                        if (skip > 0) { i += skip; seed = -1; }
                    }
                }
                if (!any) continue;

                // 밀어낸 양을 가닥을 따라 부드럽게(1-2-1 두 번) 해서 가닥이 꺾이지 않게 한 뒤 적용한다.
                for (int pass = 0; pass < 2; pass++)
                {
                    Vector3 prev = d[0];
                    for (int i = 1; i < c - 1; i++)
                    {
                        Vector3 cur = d[i];
                        d[i] = (prev + 2f * cur + d[i + 1]) * 0.25f;
                        prev = cur;
                    }
                }
                for (int i = 1; i < c; i++) pts[st + i] += d[i];

                // 부드럽게 하면서 줄어든 만큼 아직 파고든 점은 한 번 더 직접 밀어낸다(밀어낸 점 근처만).
                seed = surf.GridNearest(pts[st]);
                for (int i = 1; i < c; i++)
                {
                    if (d[i] == Vector3.zero && (i + 1 >= c || d[i + 1] == Vector3.zero) && d[i - 1] == Vector3.zero) { seed = -1; continue; }
                    if (surf.ClosestFrom(ref seed, pts[st + i], searchDist, out var p, out var n, out float dist))
                    {
                        float signed = Vector3.Dot(pts[st + i] - p, n);
                        if (signed < margin * 0.5f) pts[st + i] += n * (margin * 0.5f - signed);
                    }
                }
            }
            return pushed;
        }

        static int Cell(float v, float cell) { return Mathf.FloorToInt(v / cell); }
        static long Key(int x, int y, int z) { return ((long)(x + 4096) * 16384 + (y + 4096)) * 16384 + (z + 4096); }
    }
}
