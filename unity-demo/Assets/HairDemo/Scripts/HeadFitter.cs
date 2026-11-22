using System.Collections.Generic;
using UnityEngine;

namespace HairDemo
{
    /// <summary>
    /// 두상 자동 맞춤. 머리카락 가닥의 뿌리(첫 점)는 두피 표면 위에 있으므로,
    /// "두상 표면이 뿌리들에 최대한 가깝게 닿도록" 두상의 크기(축별)와 위치를 반복 계산(ICP)해서 구한다.
    ///
    ///   1) 현재 변환으로 뿌리마다 가장 가까운 두상 정점을 찾는다 (격자로 가속).
    ///   2) 뿌리에서 너무 먼 점(귀 뒤 목 등 엉뚱하게 붙은 것)은 빼고,
    ///   3) 정점 -> 뿌리 위치로 가는 최소제곱 변환(균일 배율 + 축별 ±12% 보정 + 이동)을 푼다.
    ///   4) 수렴할 때까지 반복.
    /// 균일 배율을 기본으로 하고 축별 보정을 ±12%로 제한하는 이유는, 뿌리는 두개골 윗부분만 알려주기 때문에
    /// 축별로 자유롭게 풀면 얼굴 비율이 찌그러질 수 있어서다.
    /// </summary>
    public static class HeadFitter
    {
        public struct Result
        {
            public Vector3 scale, pos;   // 월드 = scale ∘ 로컬 + pos
            public float errBefore, errAfter; // 뿌리 -> 두피(가장 가까운 정점) 평균 거리
            public int rootsUsed;
        }

        public static Result Fit(Vector3[] verts, Vector3[] roots, Vector3 scale0, Vector3 pos0,
                                 int maxRoots = 800, int iterations = 10)
        {
            var res = new Result { scale = scale0, pos = pos0 };
            if (verts == null || verts.Length == 0 || roots == null || roots.Length == 0) return res;

            // --- 정점 격자 ---
            Vector3 mn = verts[0], mx = verts[0];
            for (int i = 1; i < verts.Length; i++) { mn = Vector3.Min(mn, verts[i]); mx = Vector3.Max(mx, verts[i]); }
            Vector3 size = mx - mn;
            float cell = Mathf.Max(size.x, Mathf.Max(size.y, size.z)) / 32f;
            var grid = new Dictionary<long, List<int>>();
            for (int i = 0; i < verts.Length; i++)
            {
                long key = Key(Cell(verts[i].x - mn.x, cell), Cell(verts[i].y - mn.y, cell), Cell(verts[i].z - mn.z, cell));
                if (!grid.TryGetValue(key, out var list)) { list = new List<int>(4); grid[key] = list; }
                list.Add(i);
            }

            // --- 뿌리 솎아내기 ---
            int step = Mathf.Max(1, roots.Length / maxRoots);
            var r = new List<Vector3>(roots.Length / step + 1);
            for (int i = 0; i < roots.Length; i += step) r.Add(roots[i]);
            int n = r.Count;

            Vector3 s = scale0, t = pos0;
            float su0 = (scale0.x + scale0.y + scale0.z) / 3f;

            var P = new Vector3[n];
            var dist = new float[n];
            var sorted = new float[n];

            for (int iter = 0; iter <= iterations; iter++)
            {
                // 1) 대응점
                for (int i = 0; i < n; i++)
                {
                    Vector3 q = new Vector3((r[i].x - t.x) / s.x, (r[i].y - t.y) / s.y, (r[i].z - t.z) / s.z);
                    int idx = Nearest(verts, grid, mn, cell, q);
                    P[i] = verts[idx];
                    Vector3 w = new Vector3(P[i].x * s.x + t.x, P[i].y * s.y + t.y, P[i].z * s.z + t.z);
                    dist[i] = (w - r[i]).magnitude;
                }

                // 2) 이상치 제거 기준(중앙값의 2.5배)
                System.Array.Copy(dist, sorted, n);
                System.Array.Sort(sorted);
                float median = sorted[n / 2];
                float cutoff = Mathf.Max(median * 2.5f, 1e-4f);

                double sum = 0; int used = 0;
                Vector3 pbar = Vector3.zero, rbar = Vector3.zero;
                for (int i = 0; i < n; i++)
                {
                    if (dist[i] > cutoff) continue;
                    pbar += P[i]; rbar += r[i]; used++; sum += dist[i];
                }
                if (used < 10) break;
                pbar /= used; rbar /= used;
                float err = (float)(sum / used);
                if (iter == 0) res.errBefore = err;
                res.errAfter = err;
                res.rootsUsed = used;
                if (iter == iterations) break;

                // 3) 최소제곱 변환
                float num = 0f, den = 0f;
                Vector3 numA = Vector3.zero, denA = Vector3.zero;
                for (int i = 0; i < n; i++)
                {
                    if (dist[i] > cutoff) continue;
                    Vector3 dp = P[i] - pbar, dr = r[i] - rbar;
                    num += Vector3.Dot(dp, dr);
                    den += dp.sqrMagnitude;
                    numA += new Vector3(dp.x * dr.x, dp.y * dr.y, dp.z * dr.z);
                    denA += new Vector3(dp.x * dp.x, dp.y * dp.y, dp.z * dp.z);
                }
                if (den < 1e-12f) break;

                float su = Mathf.Clamp(num / den, su0 * 0.5f, su0 * 2f);
                Vector3 ns = new Vector3(
                    Mathf.Clamp(denA.x > 1e-12f ? numA.x / denA.x : su, su * 0.88f, su * 1.12f),
                    Mathf.Clamp(denA.y > 1e-12f ? numA.y / denA.y : su, su * 0.88f, su * 1.12f),
                    Mathf.Clamp(denA.z > 1e-12f ? numA.z / denA.z : su, su * 0.88f, su * 1.12f));
                Vector3 nt = new Vector3(
                    rbar.x - ns.x * pbar.x,
                    rbar.y - ns.y * pbar.y,
                    rbar.z - ns.z * pbar.z);

                bool converged = (ns - s).sqrMagnitude < 1e-10f && (nt - t).sqrMagnitude < 1e-10f;
                s = ns; t = nt;
                if (converged) { iter = iterations - 1; }
            }

            res.scale = s;
            res.pos = t;
            return res;
        }

        static int Cell(float v, float cell) { return Mathf.FloorToInt(v / cell); }
        static long Key(int x, int y, int z) { return ((long)(x + 1024) * 4096 + (y + 1024)) * 4096 + (z + 1024); }

        // 가장 가까운 정점: 질의 점이 있는 칸부터 바깥 고리로 넓혀가며 찾는다.
        static int Nearest(Vector3[] verts, Dictionary<long, List<int>> grid, Vector3 mn, float cell, Vector3 q)
        {
            int cx = Cell(q.x - mn.x, cell), cy = Cell(q.y - mn.y, cell), cz = Cell(q.z - mn.z, cell);
            int best = -1; float bestD = float.MaxValue;
            for (int ring = 0; ring < 64; ring++)
            {
                for (int dx = -ring; dx <= ring; dx++)
                for (int dy = -ring; dy <= ring; dy++)
                for (int dz = -ring; dz <= ring; dz++)
                {
                    if (Mathf.Max(Mathf.Abs(dx), Mathf.Max(Mathf.Abs(dy), Mathf.Abs(dz))) != ring) continue;
                    if (!grid.TryGetValue(Key(cx + dx, cy + dy, cz + dz), out var list)) continue;
                    foreach (int i in list)
                    {
                        float d = (verts[i] - q).sqrMagnitude;
                        if (d < bestD) { bestD = d; best = i; }
                    }
                }
                // 이 고리까지 보면 반경 ring*cell 안은 전부 확인한 것: 더 가까운 점이 없다.
                if (best >= 0 && Mathf.Sqrt(bestD) <= ring * cell) break;
            }
            return best >= 0 ? best : 0;
        }
    }
}
