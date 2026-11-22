using UnityEngine;

namespace HairDemo
{
    /// <summary>
    /// 루트 틴트용 두피 마스크. "머리카락이 나는 두피 전체"를 3D 격자로 만든다.
    ///
    /// 레퍼런스(두피 아래 메쉬를 머리색으로 칠하는 방식)처럼, 머리카락이 덮는 두개골 영역 전체를 균일하게 칠하고
    /// 헤어라인에서 깔끔하게 끝나야 한다. 그래서 뿌리가 놓인 영역에 "닫기"(팽창 후 침식)를 적용한다.
    ///   - 팽창: 뿌리 사이의 빈틈(가르마, 듬성한 곳)을 메운다.
    ///   - 침식: 같은 크기만큼 되돌려서 바깥쪽 경계(헤어라인)가 이마 쪽으로 번지지 않게 한다.
    /// 마지막에 아주 약하게 흐려서 경계가 계단지지 않게 한다.
    /// 두상 셰이더가 이 3D 텍스처를 월드 좌표로 픽셀마다 읽는다(정점 해상도와 무관).
    /// 유니티 오브젝트를 건드리지 않아서 백그라운드 스레드에서 불러도 된다.
    /// </summary>
    public static class ScalpMask
    {
        public class Field
        {
            public float[] data;      // 0~1 (1 = 머리카락이 나는 두피). x가 가장 빠르게 변하는 순서
            public int nx, ny, nz;
            public Vector3 origin;    // 격자 최소 모서리의 월드 좌표
            public float cell;
        }

        public static Field Compute(Vector3[] roots, float cell)
        {
            if (roots == null || roots.Length == 0) return null;

            const int CloseRadius = 3;    // 닫기 반경(칸). 0.02 x 3 = 0.06 -> 폭 약 0.12까지의 빈틈(가르마)을 메운다
            const int ExtraRadius = 2;    // 닫은 뒤 한 번 더 부풀리는 반경(칸, 0.04). 두상 표면이 뿌리에서 조금 어긋나 있어도 두피 전체가 칠해지게 한다
            const float RampSigma = 3f;   // 헤어라인 그라데이션 폭(칸). 0.02 x 3 = 0.06
            const int RampRadius = 8;
            int pad = CloseRadius + ExtraRadius + RampRadius + 2;

            Vector3 mn = roots[0], mx = roots[0];
            for (int i = 1; i < roots.Length; i++) { mn = Vector3.Min(mn, roots[i]); mx = Vector3.Max(mx, roots[i]); }
            int nx = Mathf.CeilToInt((mx.x - mn.x) / cell) + 2 * pad + 1;
            int ny = Mathf.CeilToInt((mx.y - mn.y) / cell) + 2 * pad + 1;
            int nz = Mathf.CeilToInt((mx.z - mn.z) / cell) + 2 * pad + 1;
            var a = new float[nx * ny * nz];
            var b = new float[a.Length];

            // 뿌리가 있는 칸 = 1
            for (int i = 0; i < roots.Length; i++)
            {
                int cx = Mathf.FloorToInt((roots[i].x - mn.x) / cell) + pad;
                int cy = Mathf.FloorToInt((roots[i].y - mn.y) / cell) + pad;
                int cz = Mathf.FloorToInt((roots[i].z - mn.z) / cell) + pad;
                a[cx + nx * (cy + ny * cz)] = 1f;
            }

            // 닫기 = 팽창(max) 후 침식(min). 박스 모양 필터를 x -> y -> z 순서로 적용한다.
            Extremum(a, b, nx, ny, nz, CloseRadius, true);   // 팽창 (결과 b)
            Extremum(b, a, nx, ny, nz, CloseRadius, false);  // 침식 (결과 a)
            Extremum(a, b, nx, ny, nz, ExtraRadius, true);   // 두껍게 (결과 b)

            // 헤어라인에서 안쪽으로 서서히 진해지는 그라데이션.
            // 두껍게 만든 영역을 넓게(σ = RampSigma칸) 흐리면, 영역 안쪽에서는 일정한 값이고 가장자리에서는 절반으로 줄어든다.
            // (표면을 따라 퍼지는 폭 때문에 가장자리 쪽은 서서히 옅어진다.) 3D 침식으로 "경계로부터의 깊이"를 재는 방식은
            // 영역이 두개골 표면을 따라 얇은 껍질이라 두께 방향에서 먼저 다 깎여서 쓸 수 없었다.
            // 실제 헤어라인은 경계로 갈수록 머리카락이 성겨서 두피가 서서히 드러나니, 칠한 색도 서서히 옅어지는 게 맞다.
            var kr = new float[2 * RampRadius + 1];
            float krSum = 0f;
            for (int i = -RampRadius; i <= RampRadius; i++)
            {
                kr[i + RampRadius] = Mathf.Exp(-(i * i) / (2f * RampSigma * RampSigma));
                krSum += kr[i + RampRadius];
            }
            for (int i = 0; i < kr.Length; i++) kr[i] /= krSum;
            Blur(b, a, 1, nx, kr, RampRadius);
            Blur(a, b, nx, ny, kr, RampRadius);
            Blur(b, a, nx * ny, nz, kr, RampRadius);

            // 안쪽 값(상위 10% 지점)을 1로 정규화한다. 머리 모델마다 뿌리 개수가 달라도 같은 모양이 나온다.
            var positive = new System.Collections.Generic.List<float>(a.Length / 8);
            for (int i = 0; i < a.Length; i++) if (a[i] > 1e-4f) positive.Add(a[i]);
            float reference = 1f;
            if (positive.Count > 0)
            {
                positive.Sort();
                reference = Mathf.Max(positive[(int)(positive.Count * 0.90f)], 1e-4f);
            }
            for (int i = 0; i < a.Length; i++) a[i] = a[i] / reference > 1f ? 1f : a[i] / reference;

            return new Field
            {
                data = a, nx = nx, ny = ny, nz = nz,
                origin = mn - Vector3.one * (pad * cell),
                cell = cell,
            };
        }

        // 세 축 모두에 같은 반경의 최대/최소 필터(박스)를 적용한다. 결과는 dst.
        static void Extremum(float[] src, float[] dst, int nx, int ny, int nz, int radius, bool max)
        {
            var tmp = new float[src.Length];
            Axis(src, tmp, 1, nx, radius, max);
            Axis(tmp, dst, nx, ny, radius, max);
            Axis(dst, tmp, nx * ny, nz, radius, max);
            System.Array.Copy(tmp, dst, dst.Length);
        }

        static void Axis(float[] src, float[] dst, int stride, int len, int radius, bool max)
        {
            for (int i = 0; i < src.Length; i++)
            {
                int pos = (i / stride) % len;
                float v = max ? 0f : 1f;
                int lo = Mathf.Max(-radius, -pos), hi = Mathf.Min(radius, len - 1 - pos);
                if (max)
                {
                    for (int t = lo; t <= hi; t++) { float s = src[i + t * stride]; if (s > v) v = s; }
                }
                else
                {
                    // 침식: 격자 경계를 벗어나는 쪽은 바깥(0)으로 본다
                    if (pos - radius < 0 || pos + radius >= len) v = 0f;
                    else for (int t = lo; t <= hi; t++) { float s = src[i + t * stride]; if (s < v) v = s; }
                }
                dst[i] = v;
            }
        }

        // 한 방향 1D 가우시안 블러. stride = 그 방향으로 한 칸 이동할 때의 배열 인덱스 증가량, len = 그 방향 길이.
        static void Blur(float[] src, float[] dst, int stride, int len, float[] k, int radius)
        {
            for (int i = 0; i < src.Length; i++)
            {
                int pos = (i / stride) % len;
                float sum = 0f;
                for (int t = -radius; t <= radius; t++)
                {
                    int q = pos + t;
                    if (q < 0 || q >= len) continue;
                    sum += src[i + t * stride] * k[t + radius];
                }
                dst[i] = sum;
            }
        }
    }
}
