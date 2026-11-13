// PNU 도로 생성기 — 기하 도구 (유니티 없이 도는 순수 C#)
// 좌표: x = 동, z = 북 (유니티와 같음), y = 높이
using System;
using System.Collections.Generic;

namespace PNU.RoadGen
{
    [Serializable]
    public struct V2
    {
        public double x, z;
        public V2(double x, double z) { this.x = x; this.z = z; }
        public static V2 operator +(V2 a, V2 b) { return new V2(a.x + b.x, a.z + b.z); }
        public static V2 operator -(V2 a, V2 b) { return new V2(a.x - b.x, a.z - b.z); }
        public static V2 operator -(V2 a) { return new V2(-a.x, -a.z); }
        public static V2 operator *(V2 a, double s) { return new V2(a.x * s, a.z * s); }
        public static V2 operator *(double s, V2 a) { return new V2(a.x * s, a.z * s); }
        public static V2 operator /(V2 a, double s) { return new V2(a.x / s, a.z / s); }
        public double Len { get { return Math.Sqrt(x * x + z * z); } }
        public double Len2 { get { return x * x + z * z; } }
        public V2 Norm { get { double l = Len; return l > 1e-12 ? new V2(x / l, z / l) : new V2(0, 0); } }
        /// <summary>진행 방향 기준 왼쪽 법선 (위에서 볼 때 반시계 90도)</summary>
        public V2 Left { get { return new V2(-z, x); } }
        public static double Dot(V2 a, V2 b) { return a.x * b.x + a.z * b.z; }
        /// <summary>a×b (z 성분): 양수면 b 가 a 의 왼쪽</summary>
        public static double Cross(V2 a, V2 b) { return a.x * b.z - a.z * b.x; }
        public static V2 Lerp(V2 a, V2 b, double t) { return new V2(a.x + (b.x - a.x) * t, a.z + (b.z - a.z) * t); }
        public double Angle { get { return Math.Atan2(z, x); } }
        public override string ToString() { return "(" + x.ToString("0.0") + "," + z.ToString("0.0") + ")"; }
    }

    public static class Geom
    {
        public static double Clamp(double v, double a, double b) { return v < a ? a : (v > b ? b : v); }
        public static double Smooth(double t) { t = Clamp(t, 0, 1); return t * t * (3 - 2 * t); }

        public static double[] CumLen(List<V2> p)
        {
            var c = new double[p.Count];
            for (int i = 1; i < p.Count; i++) c[i] = c[i - 1] + (p[i] - p[i - 1]).Len;
            return c;
        }
        public static double Length(List<V2> p) { double s = 0; for (int i = 1; i < p.Count; i++) s += (p[i] - p[i - 1]).Len; return s; }

        /// <summary>호 길이 s 에서의 점(선형 보간)</summary>
        public static V2 At(List<V2> p, double[] cum, double s)
        {
            if (p.Count == 1) return p[0];
            if (s <= 0) return p[0];
            if (s >= cum[cum.Length - 1]) return p[p.Count - 1];
            int lo = 0, hi = cum.Length - 1;
            while (hi - lo > 1) { int m = (lo + hi) / 2; if (cum[m] <= s) lo = m; else hi = m; }
            double d = cum[hi] - cum[lo];
            return V2.Lerp(p[lo], p[hi], d > 1e-12 ? (s - cum[lo]) / d : 0);
        }
        /// <summary>단조 3차 보간(PCHIP) 기울기 — 편집기 makePchip 과 같은 식(넘침 없음)</summary>
        public static double[] PchipM(double[] T, double[] Y)
        {
            int n = T.Length; var m = new double[n]; if (n < 2) return m;
            var h = new double[n - 1]; var d = new double[n - 1];
            for (int k = 0; k < n - 1; k++) { h[k] = Math.Max(1e-9, T[k + 1] - T[k]); d[k] = (Y[k + 1] - Y[k]) / h[k]; }
            if (n == 2) { m[0] = m[1] = d[0]; return m; }
            m[0] = PchipEnd(h[0], h[1], d[0], d[1]); m[n - 1] = PchipEnd(h[n - 2], h[n - 3], d[n - 2], d[n - 3]);
            for (int k = 1; k < n - 1; k++)
            {
                if (d[k - 1] * d[k] <= 0) m[k] = 0;
                else { double w1 = 2 * h[k] + h[k - 1], w2 = h[k] + 2 * h[k - 1]; m[k] = (w1 + w2) / (w1 / d[k - 1] + w2 / d[k]); }
            }
            return m;
        }
        static double PchipEnd(double h0, double h1, double d0, double d1)
        {
            double m = ((2 * h0 + h1) * d0 - h0 * d1) / (h0 + h1);
            if (Math.Sign(m) != Math.Sign(d0)) m = 0;
            else if (Math.Sign(d0) != Math.Sign(d1) && Math.Abs(m) > Math.Abs(3 * d0)) m = 3 * d0;
            return m;
        }
        /// <summary>PCHIP 값(범위 밖은 끝 값 그대로)</summary>
        public static double Pchip(double[] T, double[] Y, double[] m, double t)
        {
            int n = T.Length; if (n == 1 || t <= T[0]) return Y[0]; if (t >= T[n - 1]) return Y[n - 1];
            int lo = 0, hi = n - 1; while (hi - lo > 1) { int q = (lo + hi) >> 1; if (T[q] <= t) lo = q; else hi = q; }
            double hh = Math.Max(1e-9, T[hi] - T[lo]), u = (t - T[lo]) / hh, u2 = u * u, u3 = u2 * u;
            return (2 * u3 - 3 * u2 + 1) * Y[lo] + (u3 - 2 * u2 + u) * hh * m[lo] + (-2 * u3 + 3 * u2) * Y[hi] + (u3 - u2) * hh * m[hi];
        }
        public static double AtVal(double[] vals, double[] cum, double s)
        {
            if (vals.Length == 1 || s <= 0) return vals[0];
            if (s >= cum[cum.Length - 1]) return vals[vals.Length - 1];
            int lo = 0, hi = cum.Length - 1;
            while (hi - lo > 1) { int m = (lo + hi) / 2; if (cum[m] <= s) lo = m; else hi = m; }
            double d = cum[hi] - cum[lo];
            double t = d > 1e-12 ? (s - cum[lo]) / d : 0;
            return vals[lo] + (vals[hi] - vals[lo]) * t;
        }

        /// <summary>일정 간격(step)으로 다시 찍기 — 양 끝 점 유지</summary>
        public static List<V2> Resample(List<V2> p, double step)
        {
            var cum = CumLen(p); double L = cum[cum.Length - 1];
            int n = Math.Max(1, (int)Math.Ceiling(L / step));
            var o = new List<V2>(n + 1);
            for (int i = 0; i <= n; i++) o.Add(At(p, cum, L * i / n));
            return o;
        }
        public static List<V2> ResampleN(List<V2> p, int n)
        {
            var cum = CumLen(p); double L = cum[cum.Length - 1];
            var o = new List<V2>(n);
            for (int i = 0; i < n; i++) o.Add(At(p, cum, n == 1 ? 0 : L * i / (n - 1)));
            return o;
        }

        /// <summary>가우스 다듬기(양 끝 고정, 간격 균일 가정) — sigma 는 점 개수 단위</summary>
        public static List<V2> SmoothPts(List<V2> p, double sigmaPts)
        {
            int n = p.Count; if (n < 3 || sigmaPts < 0.5) return new List<V2>(p);
            int r = (int)Math.Ceiling(sigmaPts * 2.5);
            var w = new double[2 * r + 1];
            for (int k = -r; k <= r; k++) w[k + r] = Math.Exp(-0.5 * k * k / (sigmaPts * sigmaPts));
            var o = new List<V2>(n);
            for (int i = 0; i < n; i++)
            {
                if (i == 0 || i == n - 1) { o.Add(p[i]); continue; }
                int rr = Math.Min(r, Math.Min(i, n - 1 - i));          // 끝으로 갈수록 창 줄임(끝점 고정 효과)
                double sx = 0, sz = 0, sw = 0;
                for (int k = -rr; k <= rr; k++) { double ww = w[k + r]; sx += p[i + k].x * ww; sz += p[i + k].z * ww; sw += ww; }
                o.Add(new V2(sx / sw, sz / sw));
            }
            return o;
        }
        public static double[] SmoothVals(double[] v, double sigmaPts)
        {
            int n = v.Length; if (n < 3 || sigmaPts < 0.5) return (double[])v.Clone();
            int r = (int)Math.Ceiling(sigmaPts * 2.5);
            var o = new double[n];
            for (int i = 0; i < n; i++)
            {
                double s = 0, sw = 0;
                for (int k = -r; k <= r; k++)
                {
                    int j = Math.Max(0, Math.Min(n - 1, i + k));
                    double ww = Math.Exp(-0.5 * k * k / (sigmaPts * sigmaPts)); s += v[j] * ww; sw += ww;
                }
                o[i] = s / sw;
            }
            return o;
        }

        /// <summary>각 점의 진행 방향(앞뒤 차, 단위벡터)</summary>
        public static V2[] Tangents(List<V2> p)
        {
            int n = p.Count; var t = new V2[n];
            for (int i = 0; i < n; i++)
            {
                V2 d = p[Math.Min(n - 1, i + 1)] - p[Math.Max(0, i - 1)];
                t[i] = d.Len > 1e-9 ? d.Norm : (i > 0 ? t[i - 1] : new V2(1, 0));
            }
            return t;
        }

        /// <summary>두 직선(점+방향) 교점. 평행이면 false</summary>
        public static bool LineLine(V2 p, V2 d, V2 q, V2 e, out double t, out double s)
        {
            double den = V2.Cross(d, e);
            t = s = 0;
            if (Math.Abs(den) < 1e-9) return false;
            V2 w = q - p;
            t = V2.Cross(w, e) / den;
            s = V2.Cross(w, d) / den;
            return true;
        }

        /// <summary>정점들을 지나는 곡선(구심 캐트멀-롬, 끝은 바깥으로 늘린 가상점) — 편집기 JS 와 같은 식. step m 간격 남짓</summary>
        public static List<V2> CatmullRom(List<V2> P, double step)
        {
            int n = P.Count; var o = new List<V2>(); if (n == 0) return o;
            if (n == 1) { o.Add(P[0]); return o; }
            for (int i = 0; i < n - 1; i++)
            {
                V2 p0 = i > 0 ? P[i - 1] : P[0] + (P[0] - P[1]), p1 = P[i], p2 = P[i + 1], p3 = i + 2 < n ? P[i + 2] : P[n - 1] + (P[n - 1] - P[n - 2]);
                double t0 = 0, t1 = t0 + Math.Sqrt(Math.Max(1e-6, (p1 - p0).Len)), t2 = t1 + Math.Sqrt(Math.Max(1e-6, (p2 - p1).Len)), t3 = t2 + Math.Sqrt(Math.Max(1e-6, (p3 - p2).Len));
                int m = Math.Max(1, (int)Math.Ceiling((p2 - p1).Len / step));
                for (int k = i == 0 ? 0 : 1; k <= m; k++)
                {
                    double t = t1 + (t2 - t1) * k / m;
                    V2 A1 = p0 * ((t1 - t) / (t1 - t0)) + p1 * ((t - t0) / (t1 - t0));
                    V2 A2 = p1 * ((t2 - t) / (t2 - t1)) + p2 * ((t - t1) / (t2 - t1));
                    V2 A3 = p2 * ((t3 - t) / (t3 - t2)) + p3 * ((t - t2) / (t3 - t2));
                    V2 B1 = A1 * ((t2 - t) / (t2 - t0)) + A2 * ((t - t0) / (t2 - t0));
                    V2 B2 = A2 * ((t3 - t) / (t3 - t1)) + A3 * ((t - t1) / (t3 - t1));
                    o.Add(B1 * ((t2 - t) / (t2 - t1)) + B2 * ((t - t1) / (t2 - t1)));
                }
            }
            return o;
        }

        /// <summary>열린 선 단순화(더글러스-포이커): 남길 점 번호(처음·끝 포함)</summary>
        public static List<int> SimplifyOpen(List<V2> p, double tol)
        {
            int n = p.Count; var keep = new bool[n]; if (n == 0) return new List<int>(); keep[0] = keep[n - 1] = true;
            var st = new Stack<int[]>(); st.Push(new[] { 0, n - 1 });
            while (st.Count > 0)
            {
                var ab = st.Pop(); int a = ab[0], b = ab[1]; if (b - a < 2) continue;
                V2 d = p[b] - p[a]; double L = d.Len; int bi = -1; double bd = tol;
                for (int i = a + 1; i < b; i++)
                {
                    double dist = L > 1e-9 ? Math.Abs(V2.Cross(d, p[i] - p[a])) / L : (p[i] - p[a]).Len;
                    if (dist > bd) { bd = dist; bi = i; }
                }
                if (bi >= 0) { keep[bi] = true; st.Push(new[] { a, bi }); st.Push(new[] { bi, b }); }
            }
            var o = new List<int>(); for (int i = 0; i < n; i++) if (keep[i]) o.Add(i); return o;
        }

        public static double PolylineDist(List<V2> p, V2 q, out double sAt)
        {
            double best = double.MaxValue; sAt = 0; double acc = 0;
            for (int i = 1; i < p.Count; i++)
            {
                V2 a = p[i - 1], b = p[i], ab = b - a; double L2 = ab.Len2, L = Math.Sqrt(L2);
                double t = L2 > 1e-12 ? Clamp(V2.Dot(q - a, ab) / L2, 0, 1) : 0;
                double d = (a + ab * t - q).Len;
                if (d < best) { best = d; sAt = acc + t * L; }
                acc += L;
            }
            return best;
        }

        /// <summary>호(중심 c, 반경 r, 시작각 a0 → 끝각 a1, 짧은 쪽 방향 ccw 여부) 점들</summary>
        public static List<V2> Arc(V2 c, double r, double a0, double a1, bool ccw, double maxStepRad)
        {
            double da = a1 - a0;
            if (ccw) { while (da < 0) da += 2 * Math.PI; while (da > 2 * Math.PI) da -= 2 * Math.PI; }
            else { while (da > 0) da -= 2 * Math.PI; while (da < -2 * Math.PI) da += 2 * Math.PI; }
            int n = Math.Max(1, (int)Math.Ceiling(Math.Abs(da) / maxStepRad));
            var o = new List<V2>(n + 1);
            for (int i = 0; i <= n; i++) { double a = a0 + da * i / n; o.Add(new V2(c.x + r * Math.Cos(a), c.z + r * Math.Sin(a))); }
            return o;
        }

        /// <summary>다각형(반시계) 넓이 부호</summary>
        public static double SignedArea(List<V2> p)
        {
            double a = 0; for (int i = 0; i < p.Count; i++) { V2 u = p[i], v = p[(i + 1) % p.Count]; a += u.x * v.z - v.x * u.z; }
            return a * 0.5;
        }

        /// <summary>귀 자르기 삼각분할(단순 다각형). 반환: 꼭짓점 인덱스 3개씩, 위(+y)에서 볼 때 시계 방향 = 유니티 앞면</summary>
        public static List<int> Triangulate(List<V2> poly)
        {
            var res = new List<int>();
            int n = poly.Count; if (n < 3) return res;
            var idx = new List<int>(n);
            bool ccw = SignedArea(poly) > 0;
            for (int i = 0; i < n; i++) idx.Add(ccw ? i : n - 1 - i);          // 반시계 순서로
            int guard = 0;
            while (idx.Count > 3 && guard++ < 10000)
            {
                bool cut = false;
                for (int k = 0; k < idx.Count; k++)
                {
                    int ia = idx[(k + idx.Count - 1) % idx.Count], ib = idx[k], ic = idx[(k + 1) % idx.Count];
                    V2 a = poly[ia], b = poly[ib], c = poly[ic];
                    if (V2.Cross(b - a, c - b) <= 1e-12) continue;               // 오목 꼭짓점
                    bool inside = false;
                    for (int m = 0; m < idx.Count; m++)
                    {
                        int im = idx[m]; if (im == ia || im == ib || im == ic) continue;
                        if (PointInTri(poly[im], a, b, c)) { inside = true; break; }
                    }
                    if (inside) continue;
                    res.Add(ia); res.Add(ic); res.Add(ib);                           // 반시계 a,b,c → 유니티 앞면은 a,c,b
                    idx.RemoveAt(k); cut = true; break;
                }
                if (!cut) break;                                                     // 꼬인 다각형: 남은 건 부채꼴로
            }
            if (idx.Count >= 3)
                for (int k = 1; k + 1 < idx.Count; k++) { res.Add(idx[0]); res.Add(idx[k + 1]); res.Add(idx[k]); }
            return res;
        }
        /// <summary>점이 다각형 안인가(짝홀 규칙)</summary>
        public static bool InPoly(List<V2> poly, V2 p)
        {
            bool c = false; int n = poly.Count;
            for (int i = 0, j = n - 1; i < n; j = i++)
            {
                V2 a = poly[i], b = poly[j];
                if (((a.z > p.z) != (b.z > p.z)) && (p.x < (b.x - a.x) * (p.z - a.z) / (b.z - a.z) + a.x)) c = !c;
            }
            return c;
        }
        static bool PointInTri(V2 p, V2 a, V2 b, V2 c)
        {
            double d1 = V2.Cross(b - a, p - a), d2 = V2.Cross(c - b, p - b), d3 = V2.Cross(a - c, p - c);
            return d1 >= -1e-12 && d2 >= -1e-12 && d3 >= -1e-12;
        }
    }
}
