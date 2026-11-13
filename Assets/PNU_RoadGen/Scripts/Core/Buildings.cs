// PNU 도로 생성기 — 예전 지도(PNU_Map3)에서 만든 건물을 새 지형 위에 다시 세움 (Data/pnu_buildings.txt)
//   건물 바닥 = 새 지형(지형 맞춤 격자)에서 둘레 가장 낮은 곳, 벽은 그보다 3m 아래부터(비탈에서 뜨지 않게), 지붕 = 바닥 + 높이
//   새 도로(차도·인도·교차로 면)와 겹치는 건물은 뺌(편집기에서 새로 놓은 길·램프 자리) — 땅속(덮인 터널·지하차도) 구간은 안 봄
//   재질: Building_Wall_0~9(용도별 색 · 창문 무늬 3.0m × 3.3m 한 칸), Building_Roof — 예전 지도와 같음
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace PNU.RoadGen
{
    public class BuildingIn { public int ty; public double h; public bool fill; public List<V2> ring; }


    /// <summary>새 도로가 덮는 자리(1.5m 칸): 1 = 차도·교차로 면, 2 = 인도, 0 = 없음. 덮인 땅속 구간(터널·박스)은 빼고</summary>
    public class RoadCover
    {
        public const double Cell = 1.5;
        readonly HashSet<long> occ = new HashSet<long>(), side = new HashSet<long>();
        static long Key(double x, double z) { return ((long)Math.Floor(x / Cell) << 32) ^ ((long)Math.Floor(z / Cell) & 0xffffffffL); }
        public int At(double x, double z) { long k = Key(x, z); return occ.Contains(k) ? 1 : side.Contains(k) ? 2 : 0; }
        public RoadCover(RoadGenerator gen)
        {
            foreach (var g in gen.roadGeom.Values)
            {
                if (!g.ok) continue;
                for (int i = 0; i + 1 < g.c.Count; i++)
                {
                    if (g.tmode != null && g.tmode[i] == 2 && g.tmode[i + 1] == 2) continue;
                    double segL = (g.c[i + 1] - g.c[i]).Len; int m = Math.Max(1, (int)Math.Ceiling(segL / 0.7));
                    for (int k = 0; k < m; k++)
                    {
                        double f = (double)k / m; V2 c = g.c[i] + (g.c[i + 1] - g.c[i]) * f; V2 lf = (g.t[i] * (1 - f) + g.t[i + 1] * f).Norm.Left;
                        double wr = g.wR[i] * (1 - f) + g.wR[i + 1] * f, wl = g.wL[i] * (1 - f) + g.wL[i + 1] * f;
                        double a = -(wr + g.swR[i] * (1 - f) + g.swR[i + 1] * f), b = wl + g.swL[i] * (1 - f) + g.swL[i + 1] * f;
                        for (double u = a; u <= b + 1e-6; u += 0.7) { double uu = Math.Min(u, b); V2 q = c + lf * uu; if (uu >= -wr && uu <= wl) occ.Add(Key(q.x, q.z)); else side.Add(Key(q.x, q.z)); }
                    }
                }
            }
            foreach (var JG in gen.junctionGeom.Values)
            {
                if (!JG.ok) continue;
                var polys = JG.sheet != null && JG.sheet.Count > 0 ? JG.sheet : (JG.poly != null && JG.poly.Count >= 3 ? new List<List<V2>> { JG.poly } : new List<List<V2>>());
                foreach (var pl in polys)
                {
                    if (pl.Count < 3) continue;
                    double x0 = pl.Min(p => p.x), x1 = pl.Max(p => p.x), z0 = pl.Min(p => p.z), z1 = pl.Max(p => p.z);
                    for (double x = Math.Floor(x0 / Cell) * Cell + Cell * 0.5; x <= x1; x += Cell)
                        for (double z = Math.Floor(z0 / Cell) * Cell + Cell * 0.5; z <= z1; z += Cell)
                            if (BuildingBuilder.Inside(pl, new V2(x, z))) occ.Add(Key(x, z));
                }
            }
        }
    }

    public static class BuildingBuilder
    {
        /// <summary>줄: "B 용도 높이 채움(0·1) x z x z …"</summary>
        public static List<BuildingIn> Read(IEnumerable<string> lines)
        {
            var ci = CultureInfo.InvariantCulture; var o = new List<BuildingIn>();
            foreach (var raw in lines)
            {
                if (string.IsNullOrEmpty(raw) || raw[0] != 'B') continue;
                var a = raw.Trim().Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries); if (a.Length < 10) continue;
                var b = new BuildingIn { ty = int.Parse(a[1]), h = double.Parse(a[2], ci), fill = a[3] == "1", ring = new List<V2>() };
                for (int i = 4; i + 1 < a.Length; i += 2) b.ring.Add(new V2(double.Parse(a[i], ci), double.Parse(a[i + 1], ci)));
                if (b.ring.Count >= 3) o.Add(b);
            }
            return o;
        }

        public static bool Inside(List<V2> poly, V2 p)
        {
            bool c = false;
            for (int i = 0, j = poly.Count - 1; i < poly.Count; j = i++)
                if ((poly[i].z > p.z) != (poly[j].z > p.z) && p.x < (poly[j].x - poly[i].x) * (p.z - poly[i].z) / (poly[j].z - poly[i].z) + poly[i].x) c = !c;
            return c;
        }

        /// <summary>건물 메쉬(조각 tile m 마다 하나, 채움 건물은 따로). dropped = 새 도로와 겹쳐 뺀 수</summary>
        public static List<MeshData> Build(RoadGenerator gen, HeightGrid TG, Func<V2, bool> inBox, List<BuildingIn> bl, double tile, out int dropped, out int outside)
        {
            dropped = 0; outside = 0;
            var cov = new RoadCover(gen);
            var o = new Dictionary<string, MeshData>();
            foreach (var b in bl)
            {
                var r = b.ring; V2 cen = new V2(r.Average(p => p.x), r.Average(p => p.z));
                if (!inBox(cen)) { outside++; continue; }
                if (Math.Abs(Geom.SignedArea(r)) < 4.0) continue;
                // 새 도로와 겹침: 둘레 점 + 안쪽 2m 격자 점 중 도로 칸에 든 수
                int hit = 0, hs = 0, tot = 0;
                foreach (var p in r) { tot++; int c = cov.At(p.x, p.z); if (c == 1) hit++; else if (c == 2) hs++; }
                double bx0 = r.Min(p => p.x), bx1 = r.Max(p => p.x), bz0 = r.Min(p => p.z), bz1 = r.Max(p => p.z);
                for (double x = bx0 + 0.75; x < bx1; x += 1.5) for (double z = bz0 + 0.75; z < bz1; z += 1.5) { var p = new V2(x, z); if (!Inside(r, p)) continue; tot++; int c = cov.At(x, z); if (c == 1) hit++; else if (c == 2) hs++; }
                if (hit >= 2 && hit >= 0.05 * tot || hit + hs >= 0.35 * tot) { dropped++; continue; }      // 차도에 걸침 · 인도를 많이 덮음
                // 바닥 = 둘레·가운데 지형 가장 낮은 곳
                double baseH = TG != null ? TG.At(cen.x, cen.z) : (gen.dem != null ? gen.dem.At(cen.x, cen.z) : 0);
                foreach (var p in r) baseH = Math.Min(baseH, TG != null ? TG.At(p.x, p.z) : (gen.dem != null ? gen.dem.At(p.x, p.z) : 0));
                baseH -= 0.3;
                double y0 = baseH - 3, y1 = baseH + Math.Max(3, b.h);
                int kcol = b.h > 55 && b.ty != 4 ? 9 : Math.Max(0, Math.Min(8, b.ty));
                string tk = (b.fill ? "F" : "B") + "_" + (int)Math.Floor(cen.x / tile) + "_" + (int)Math.Floor(cen.z / tile);
                MeshData md; if (!o.TryGetValue(tk, out md)) { md = new MeshData { name = (b.fill ? "채움 건물 " : "건물 ") + tk.Substring(2), kind = "building" }; o[tk] = md; }
                // 반시계로 맞춤(위에서) → 벽 앞면이 바깥
                var ring = Geom.SignedArea(r) > 0 ? r : Enumerable.Reverse(r).ToList();
                string wm = "bld_wall_" + kcol; double acc = 0, v0 = (y0 - baseH) / 3.3, v1 = (y1 - baseH) / 3.3;
                for (int k = 0; k < ring.Count; k++)
                {
                    V2 p0 = ring[k], p1 = ring[(k + 1) % ring.Count]; double L = (p1 - p0).Len; if (L < 1e-3) continue;
                    double u0 = acc / 3.0, u1 = (acc + L) / 3.0; acc += L;
                    int a0 = md.VU(p0, y0, u0, v0), a1 = md.VU(p1, y0, u1, v0), b0 = md.VU(p0, y1, u0, v1), b1 = md.VU(p1, y1, u1, v1);
                    md.Wall(wm, a0, a1, b0, b1, false);
                }
                var tri = Geom.Triangulate(ring);
                if (tri.Count >= 3)
                {
                    int bse = md.v.Count; foreach (var p in ring) md.VU(p, y1, 0.03, 0.03);
                    var l = md.tris.ContainsKey("bld_roof") ? md.tris["bld_roof"] : (md.tris["bld_roof"] = new List<int>());
                    for (int k = 0; k + 2 < tri.Count; k += 3) { l.Add(bse + tri[k]); l.Add(bse + tri[k + 1]); l.Add(bse + tri[k + 2]); }
                }
            }
            return o.Values.ToList();
        }
    }
}
