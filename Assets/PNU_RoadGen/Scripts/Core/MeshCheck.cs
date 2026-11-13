// PNU 도로 생성기 — 겹침 검사(메쉬 씹힘 찾기)
//   위를 보는 면(차도·인도·연석 윗면)을 격자에 찍어서, 서로 다른 조각(또는 같은 조각의 다른 삼각형)이
//   같은 칸을 비슷한 높이(dh 안)로 덮으면 겹침 → 화면에서 깜빡이거나 서로 뚫고 나옴
using System;
using System.Collections.Generic;
using System.Linq;

namespace PNU.RoadGen
{
    public class OverlapHit { public double x, z; public int a, b; public double dh; }

    public static class MeshCheck
    {
        struct Cell { public int p, tri; public float h; }
        // 칸 키: 해시가 잘 퍼지게(예전 (cx<<32)^cz 는 .NET 해시에서 상·하위가 서로 지워져 같은 값이 몰림 → 매우 느림)
        static long Key(long cx, long cz) { return ((cx + (1L << 20)) << 21) | ((cz + (1L << 20)) & 0x1FFFFF); }
        static void Unkey(long k, out long cx, out long cz) { cx = (k >> 21) - (1L << 20); cz = (k & 0x1FFFFF) - (1L << 20); }

        public static List<OverlapHit> Overlaps(List<MeshData> pieces, double cell, double dhMax, out Dictionary<string, int> pairCount)
        {
            var grid = new Dictionary<long, Cell>();
            var hits = new List<OverlapHit>();
            pairCount = new Dictionary<string, int>();
            for (int pi = 0; pi < pieces.Count; pi++)
            {
                var m = pieces[pi]; int triId = 0;
                foreach (var kv in m.tris)
                {
                    var t = kv.Value;
                    if (kv.Key.StartsWith("mark") && m.kind == "junction") { triId += t.Count / 3; continue; }    // 교차로 안 표시(대각선 횡단보도)는 일부러 띄운 것
                    for (int i = 0; i + 2 < t.Count; i += 3, triId++)
                    {
                        V3 A = m.v[t[i]], B = m.v[t[i + 1]], C = m.v[t[i + 2]];
                        // 앞면 법선(유니티 규칙) y 성분 — 위를 보는 면만
                        double ux = B.x - A.x, uy = B.y - A.y, uz = B.z - A.z, vx = C.x - A.x, vy = C.y - A.y, vz = C.z - A.z;
                        double nx = uy * vz - uz * vy, ny = uz * vx - ux * vz, nz = ux * vy - uy * vx;
                        double nl = Math.Sqrt(nx * nx + ny * ny + nz * nz); if (nl < 1e-9 || ny / nl < 0.5) continue;
                        double x0 = Math.Min(A.x, Math.Min(B.x, C.x)), x1 = Math.Max(A.x, Math.Max(B.x, C.x));
                        double z0 = Math.Min(A.z, Math.Min(B.z, C.z)), z1 = Math.Max(A.z, Math.Max(B.z, C.z));
                        long cx0 = (long)Math.Floor(x0 / cell), cx1 = (long)Math.Floor(x1 / cell), cz0 = (long)Math.Floor(z0 / cell), cz1 = (long)Math.Floor(z1 / cell);
                        double den = (B.z - C.z) * (A.x - C.x) + (C.x - B.x) * (A.z - C.z);
                        if (Math.Abs(den) < 1e-12) continue;
                        for (long cx = cx0; cx <= cx1; cx++)
                            for (long cz = cz0; cz <= cz1; cz++)
                            {
                                double px = (cx + 0.5) * cell, pz = (cz + 0.5) * cell;
                                double w1 = ((B.z - C.z) * (px - C.x) + (C.x - B.x) * (pz - C.z)) / den;
                                double w2 = ((C.z - A.z) * (px - C.x) + (A.x - C.x) * (pz - C.z)) / den;
                                double w3 = 1 - w1 - w2;
                                if (w1 < -1e-9 || w2 < -1e-9 || w3 < -1e-9) continue;
                                float h = (float)(w1 * A.y + w2 * B.y + w3 * C.y);
                                long key = Key(cx, cz);
                                Cell e;
                                if (!grid.TryGetValue(key, out e)) { grid[key] = new Cell { p = pi, tri = triId, h = h }; continue; }
                                if (e.p == pi && e.tri == triId) continue;
                                double dh = Math.Abs(e.h - h);
                                if (dh > dhMax) { if (h > e.h) grid[key] = new Cell { p = pi, tri = triId, h = h }; continue; }
                                hits.Add(new OverlapHit { x = px, z = pz, a = e.p, b = pi, dh = dh });
                                string k2 = Math.Min(e.p, pi) + "-" + Math.Max(e.p, pi);
                                int c; pairCount.TryGetValue(k2, out c); pairCount[k2] = c + 1;
                            }
                    }
                }
            }
            return hits;
        }
        /// <summary>지형이 도로·인도 윗면을 뚫고 올라온 칸 수(위를 보는 면끼리 비교). 지형 높이 > 도로면 - tol 이면 뚫림</summary>
        public static List<OverlapHit> TerrainAbove(List<MeshData> roads, List<MeshData> terrain, double cell, double tol)
        {
            var top = new Dictionary<long, float>();
            Dictionary<long, float> only = null;                      // 지형은 도로가 있는 칸만(넓은 지형 전체를 0.5m 로 찍으면 메모리 부족)
            Action<MeshData, Dictionary<long, float>, bool> raster = (m, dst, keepMax) =>
            {
                foreach (var kv in m.tris)
                {
                    var t = kv.Value;
                    if (kv.Key.StartsWith("mark")) continue;
                    for (int i = 0; i + 2 < t.Count; i += 3)
                    {
                        V3 A = m.v[t[i]], B = m.v[t[i + 1]], C = m.v[t[i + 2]];
                        double ny = (B.z - A.z) * (C.x - A.x) - (B.x - A.x) * (C.z - A.z);
                        if (ny <= 1e-9) continue;
                        double den = (B.z - C.z) * (A.x - C.x) + (C.x - B.x) * (A.z - C.z); if (Math.Abs(den) < 1e-12) continue;
                        long cx0 = (long)Math.Floor(Math.Min(A.x, Math.Min(B.x, C.x)) / cell), cx1 = (long)Math.Floor(Math.Max(A.x, Math.Max(B.x, C.x)) / cell);
                        long cz0 = (long)Math.Floor(Math.Min(A.z, Math.Min(B.z, C.z)) / cell), cz1 = (long)Math.Floor(Math.Max(A.z, Math.Max(B.z, C.z)) / cell);
                        for (long cx = cx0; cx <= cx1; cx++)
                            for (long cz = cz0; cz <= cz1; cz++)
                            {
                                double px = (cx + 0.5) * cell, pz = (cz + 0.5) * cell;
                                double w1 = ((B.z - C.z) * (px - C.x) + (C.x - B.x) * (pz - C.z)) / den, w2 = ((C.z - A.z) * (px - C.x) + (A.x - C.x) * (pz - C.z)) / den, w3 = 1 - w1 - w2;
                                if (w1 < -1e-9 || w2 < -1e-9 || w3 < -1e-9) continue;
                                long key = Key(cx, cz); if (only != null && !only.ContainsKey(key)) continue;
                                float h = (float)(w1 * A.y + w2 * B.y + w3 * C.y);
                                float e; if (!dst.TryGetValue(key, out e) || (keepMax ? h > e : h < e)) dst[key] = h;
                            }
                    }
                }
            };
            foreach (var m in roads) raster(m, top, false);           // 도로: 가장 낮은 윗면(구조물 아래층 기준)
            var ter = new Dictionary<long, float>(); only = top;
            foreach (var m in terrain) raster(m, ter, true);
            var o = new List<OverlapHit>();
            foreach (var kv in top)
            {
                float th; if (!ter.TryGetValue(kv.Key, out th)) continue;
                if (th > kv.Value - tol) { long cx, cz; Unkey(kv.Key, out cx, out cz); o.Add(new OverlapHit { x = (cx + 0.5) * cell, z = (cz + 0.5) * cell, dh = th - kv.Value }); }
            }
            return o;
        }
    }
}
