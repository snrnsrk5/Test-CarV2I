// PNU 도로 생성기 — 도로끼리 겹침 빼기(같은 높이)
//   나란히 붙어 갈라지거나 합쳐지는 길(본선 옆 램프), 같은 자리를 두 번 지나는 링크 등은 도로 면이 두 장 겹쳐 깜빡임(z-fighting).
//   → 더 큰 도로(면적 = 길이 × 폭)는 그대로 두고, 작은 도로 메쉬에서 큰 도로 단면(차도 + 인도) 안에 드는 부분을 잘라냄.
//     잘린 경계는 큰 도로 가장자리 그대로라 틈이 없고, 재질(차선 등)은 남은 조각에 그대로.
using System;
using System.Collections.Generic;
using System.Linq;

namespace PNU.RoadGen
{
    public partial class RoadGenerator
    {
        sealed class FootTri { public V2 a, b, c; public double ha, hb, hc, x0, z0, x1, z1; }
        sealed class CV { public V2 p; public double y; public V2 uv; }
        public int rrClipped, rrDropped, rrRoads;

        double RoadImportance(RoadGeom g)
        {
            double A = 0; for (int i = 0; i + 1 < g.c.Count; i++) A += (g.cum[i + 1] - g.cum[i]) * (g.wL[i] + g.wR[i]);
            return A;
        }
        static double[] GeomBox(RoadGeom g)
        {
            double x0 = 1e18, z0 = 1e18, x1 = -1e18, z1 = -1e18;
            for (int i = 0; i < g.c.Count; i++)
            {
                double w = Math.Max(g.wL[i] + g.swL[i], g.wR[i] + g.swR[i]) + 0.5; V2 p = g.c[i];
                x0 = Math.Min(x0, p.x - w); x1 = Math.Max(x1, p.x + w); z0 = Math.Min(z0, p.z - w); z1 = Math.Max(z1, p.z + w);
            }
            return new[] { x0, z0, x1, z1 };
        }

        /// <summary>도로 단면(차도 + 인도) 삼각형 — 상자 bb 와 닿는 구간만. 높이는 도로 면 기준</summary>
        List<FootTri> Footprint(RoadGeom g, double[] bb)
        {
            var o = new List<FootTri>(); int n = g.c.Count;
            for (int i = 0; i + 1 < n; i++)
            {
                V2 c0 = g.c[i], c1 = g.c[i + 1];
                double w = Math.Max(Math.Max(g.wL[i] + g.swL[i], g.wR[i] + g.swR[i]), Math.Max(g.wL[i + 1] + g.swL[i + 1], g.wR[i + 1] + g.swR[i + 1]));
                if (Math.Max(c0.x, c1.x) + w < bb[0] || Math.Min(c0.x, c1.x) - w > bb[2] || Math.Max(c0.z, c1.z) + w < bb[1] || Math.Min(c0.z, c1.z) - w > bb[3]) continue;
                V2 l0 = g.t[i].Left, l1 = g.t[i + 1].Left;
                bool sL = g.swL[i] > 0 && g.swL[i + 1] > 0, sR = g.swR[i] > 0 && g.swR[i + 1] > 0;
                V2 a = c0 + l0 * (g.wL[i] + (sL ? g.swL[i] : 0)), b = c1 + l1 * (g.wL[i + 1] + (sL ? g.swL[i + 1] : 0));
                V2 c = c1 - l1 * (g.wR[i + 1] + (sR ? g.swR[i + 1] : 0)), d = c0 - l0 * (g.wR[i] + (sR ? g.swR[i] : 0));
                double ha = RoadH(g, i, a), hb = RoadH(g, i + 1, b), hc = RoadH(g, i + 1, c), hd = RoadH(g, i, d);
                AddFoot(o, a, b, c, ha, hb, hc); AddFoot(o, a, c, d, ha, hc, hd);
            }
            return o;
        }
        static void AddFoot(List<FootTri> o, V2 a, V2 b, V2 c, double ha, double hb, double hc)
        {
            double cr = V2.Cross(b - a, c - a); if (Math.Abs(cr) < 1e-8) return;
            if (cr < 0) { var t = b; b = c; c = t; var th = hb; hb = hc; hc = th; }     // 반시계로
            o.Add(new FootTri { a = a, b = b, c = c, ha = ha, hb = hb, hc = hc, x0 = Math.Min(a.x, Math.Min(b.x, c.x)), x1 = Math.Max(a.x, Math.Max(b.x, c.x)), z0 = Math.Min(a.z, Math.Min(b.z, c.z)), z1 = Math.Max(a.z, Math.Max(b.z, c.z)) });
        }
        /// <summary>삼각형 평면 높이(밖이면 연장)</summary>
        static double FootH(FootTri q, V2 p)
        {
            double den = V2.Cross(q.b - q.a, q.c - q.a); if (Math.Abs(den) < 1e-12) return q.ha;
            double w1 = V2.Cross(q.c - q.b, p - q.b) / den, w2 = V2.Cross(q.a - q.c, p - q.c) / den, w3 = 1 - w1 - w2;
            return w1 * q.ha + w2 * q.hb + w3 * q.hc;
        }
        static bool InFoot(FootTri q, V2 p)
        {
            return V2.Cross(q.b - q.a, p - q.a) >= -1e-9 && V2.Cross(q.c - q.b, p - q.b) >= -1e-9 && V2.Cross(q.a - q.c, p - q.c) >= -1e-9;
        }

        public void SubtractRoadOverlaps(List<MeshData> pieces)
        {
            rrClipped = rrDropped = rrRoads = 0;
            if (!P.roadOverlapCut) return;
            var imp = new Dictionary<Road, double>(); var box = new Dictionary<Road, double[]>(); var byId = new Dictionary<int, Road>();
            foreach (var kv in roadGeom) { if (!kv.Value.ok) continue; imp[kv.Key] = RoadImportance(kv.Value); box[kv.Key] = GeomBox(kv.Value); byId[kv.Key.id] = kv.Key; }
            foreach (var m in pieces)
            {
                Road B; if (m.kind != "road" || !byId.TryGetValue(m.refId, out B)) continue;
                var bb = box[B]; var foot = new List<FootTri>();
                foreach (var kv in box)
                {
                    var A = kv.Key; if (A == B) continue;
                    if (imp[A] < imp[B] || (imp[A] == imp[B] && A.id > B.id)) continue;       // 더 큰 도로만 남김
                    var ab = kv.Value; if (ab[2] < bb[0] || ab[0] > bb[2] || ab[3] < bb[1] || ab[1] > bb[3]) continue;
                    foot.AddRange(Footprint(roadGeom[A], bb));
                }
                if (foot.Count == 0) continue;
                if (ClipMesh(m, foot)) rrRoads++;
            }
            log.Add("road overlaps cut: roads " + rrRoads + ", triangles clipped " + rrClipped + ", dropped " + rrDropped);
        }

        /// <summary>메쉬 m 에서 발자국 삼각형들(같은 높이) 안 부분을 잘라냄. 바뀌었으면 true</summary>
        bool ClipMesh(MeshData m, List<FootTri> foot)
        {
            const double cell = 4.0;
            var grid = new Dictionary<long, List<int>>();
            Func<double, long> cx = x => (long)Math.Floor(x / cell);
            Func<long, long, long> key = (i, j) => ((i + (1L << 20)) << 21) | ((j + (1L << 20)) & 0x1FFFFF);
            for (int k = 0; k < foot.Count; k++)
            {
                var q = foot[k];
                for (long i = cx(q.x0); i <= cx(q.x1); i++)
                    for (long j = cx(q.z0); j <= cx(q.z1); j++)
                    { List<int> l; long kk = key(i, j); if (!grid.TryGetValue(kk, out l)) grid[kk] = l = new List<int>(); l.Add(k); }
            }
            double tol = P.roadOverlapTol; bool changed = false;
            var cand = new List<int>(); var mark = new HashSet<int>();
            foreach (var mat in m.tris.Keys.ToList())
            {
                var src = m.tris[mat]; var dst = new List<int>(src.Count);
                for (int t = 0; t + 2 < src.Count; t += 3)
                {
                    int ia = src[t], ib = src[t + 1], ic = src[t + 2];
                    V3 A = m.v[ia], B = m.v[ib], C = m.v[ic];
                    double tx0 = Math.Min(A.x, Math.Min(B.x, C.x)), tx1 = Math.Max(A.x, Math.Max(B.x, C.x)), tz0 = Math.Min(A.z, Math.Min(B.z, C.z)), tz1 = Math.Max(A.z, Math.Max(B.z, C.z));
                    cand.Clear(); mark.Clear();
                    for (long i = cx(tx0); i <= cx(tx1); i++)
                        for (long j = cx(tz0); j <= cx(tz1); j++)
                        {
                            List<int> l; if (!grid.TryGetValue(key(i, j), out l)) continue;
                            foreach (var k in l) if (mark.Add(k)) { var q = foot[k]; if (q.x1 >= tx0 && q.x0 <= tx1 && q.z1 >= tz0 && q.z0 <= tz1) cand.Add(k); }
                        }
                    if (cand.Count == 0) { dst.Add(ia); dst.Add(ib); dst.Add(ic); continue; }
                    double ux = B.x - A.x, uy = B.y - A.y, uz = B.z - A.z, vx = C.x - A.x, vy = C.y - A.y, vz = C.z - A.z;
                    double nx = uy * vz - uz * vy, ny = uz * vx - ux * vz, nz = ux * vy - uy * vx, nl = Math.Sqrt(nx * nx + ny * ny + nz * nz);
                    V2 cen = new V2((A.x + B.x + C.x) / 3, (A.z + B.z + C.z) / 3); double hc = (A.y + B.y + C.y) / 3;
                    if (nl < 1e-12 || Math.Abs(ny) / nl < 0.5)
                    {   // 세운 면(연석 앞면 등): 가운데가 큰 도로 안이면 버림
                        double hmin = Math.Min(A.y, Math.Min(B.y, C.y)); bool drop = false;
                        foreach (var k in cand) { var q = foot[k]; if (InFoot(q, cen) && Math.Abs(FootH(q, cen) - hmin) <= tol) { drop = true; break; } }
                        if (drop) { rrDropped++; changed = true; } else { dst.Add(ia); dst.Add(ib); dst.Add(ic); }
                        continue;
                    }
                    var pieces = new List<List<CV>> { new List<CV> { Cv(m, ia), Cv(m, ib), Cv(m, ic) } };
                    bool touched = false;
                    foreach (var k in cand)
                    {
                        var q = foot[k];
                        if (Math.Abs(FootH(q, cen) - hc) > tol) continue;                  // 다른 층
                        var np = new List<List<CV>>();
                        foreach (var pc in pieces)
                        {
                            if (!Overlaps(pc, q)) { np.Add(pc); continue; }
                            touched = true; SubtractTri(pc, q, np);
                        }
                        pieces = np; if (pieces.Count == 0) break;
                    }
                    if (!touched) { dst.Add(ia); dst.Add(ib); dst.Add(ic); continue; }
                    changed = true; rrClipped++;
                    foreach (var pc in pieces)
                    {
                        if (pc.Count < 3) continue;
                        int b0 = m.v.Count;
                        foreach (var c in pc) { m.v.Add(new V3(c.p.x, c.y, c.p.z)); m.uv.Add(c.uv); }
                        for (int k = 1; k + 1 < pc.Count; k++) { dst.Add(b0); dst.Add(b0 + k); dst.Add(b0 + k + 1); }
                    }
                }
                m.tris[mat] = dst;
            }
            return changed;
        }
        static CV Cv(MeshData m, int i) { var v = m.v[i]; return new CV { p = new V2(v.x, v.z), y = v.y, uv = m.uv[i] }; }
        static bool Overlaps(List<CV> pc, FootTri q)
        {
            double x0 = 1e18, x1 = -1e18, z0 = 1e18, z1 = -1e18;
            foreach (var c in pc) { x0 = Math.Min(x0, c.p.x); x1 = Math.Max(x1, c.p.x); z0 = Math.Min(z0, c.p.z); z1 = Math.Max(z1, c.p.z); }
            return q.x1 > x0 && q.x0 < x1 && q.z1 > z0 && q.z0 < z1;
        }
        /// <summary>볼록 조각 pc 에서 삼각형 q 를 뺀 볼록 조각들을 o 에 더함</summary>
        static void SubtractTri(List<CV> pc, FootTri q, List<List<CV>> o)
        {
            var cur = pc; V2[] E = { q.a, q.b, q.c };
            for (int e = 0; e < 3; e++)
            {
                V2 p0 = E[e], p1 = E[(e + 1) % 3];
                var outside = ClipHalf(cur, p0, p1, false);
                if (outside.Count >= 3 && Area(outside) > 1e-5) o.Add(outside);
                cur = ClipHalf(cur, p0, p1, true);
                if (cur.Count < 3) return;
            }
        }
        /// <summary>반평면 자르기: keepLeft 면 p0→p1 왼쪽(삼각형 안쪽)을 남김</summary>
        static List<CV> ClipHalf(List<CV> poly, V2 p0, V2 p1, bool keepLeft)
        {
            var o = new List<CV>(poly.Count + 2); int n = poly.Count; V2 d = p1 - p0;
            Func<V2, double> S = p => { double c = V2.Cross(d, p - p0); return keepLeft ? c : -c; };
            for (int i = 0; i < n; i++)
            {
                var A = poly[i]; var B = poly[(i + 1) % n]; double sa = S(A.p), sb = S(B.p);
                if (sa >= 0) o.Add(A);
                if ((sa >= 0) != (sb >= 0))
                {
                    double t = sa / (sa - sb);
                    o.Add(new CV { p = A.p + (B.p - A.p) * t, y = A.y + (B.y - A.y) * t, uv = A.uv + (B.uv - A.uv) * t });
                }
            }
            return o;
        }
        static double Area(List<CV> p) { double a = 0; for (int i = 0; i < p.Count; i++) { V2 u = p[i].p, v = p[(i + 1) % p.Count].p; a += u.x * v.z - v.x * u.z; } return Math.Abs(a) * 0.5; }
    }
}
