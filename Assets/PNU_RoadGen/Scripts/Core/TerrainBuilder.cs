// PNU 도로 생성기 — 지형 맞춤
//   도로·교차로·인도 밑은 그 면보다 조금(terrainClear) 낮게 → 지형이 도로를 뚫고 올라오지 않음
//   바깥으로 갈수록(terrainBlend m 안에서) 원래 지형(DEM)으로 부드럽게 넘어감
//   교량·고가(직접 많이 올린 곳 포함)는 밑 지형을 낮추기만, 터널은 손대지 않음. 지하차도·직접 내린 곳은 주변도 같이 깎임
using System;
using System.Collections.Generic;

namespace PNU.RoadGen
{
    public static class TerrainBuilder
    {
        /// <summary>상자(x0,z0)-(x1,z1) 범위의 맞춘 높이 격자(행 0 = 북쪽). pieces: 이미 만든 교차로 메쉬(위를 보는 면만 씀)</summary>
        public static HeightGrid Conform(RoadGenerator gen, List<MeshData> pieces, double x0, double z0, double x1, double z1)
        {
            var P = gen.P; double st = (x1 - x0) * (z1 - z0) > P.terrainLargeArea * 1e6 ? Math.Max(P.terrainStep, P.terrainStepLarge) : P.terrainStep;
            int nx = (int)Math.Ceiling((x1 - x0) / st) + 1, nz = (int)Math.Ceiling((z1 - z0) / st) + 1;
            double zTop = z0 + (nz - 1) * st;
            var G = new HeightGrid { x0 = x0, z1 = zTop, step = st, nx = nx, nz = nz, h = new float[nx * nz] };
            var tgt = new float[nx * nz];
            for (int i = 0; i < tgt.Length; i++) tgt[i] = float.NaN;
            Action<int, double> put = (k, h) => { float v = (float)(h - P.terrainClear); if (float.IsNaN(tgt[k]) || v < tgt[k]) tgt[k] = v; };

            // 1) 도로: 단면 전체(인도 바깥까지, 인도 없으면 가장자리 +0.5m) 네모를 찍음 — 구조물 구간 제외
            foreach (var g in gen.roadGeom.Values)
            {
                if (!g.ok || g.h == null) continue;
                int n = g.c.Count;
                var L = new V2[n]; var R = new V2[n]; var hL = new double[n]; var hR = new double[n];
                double ext = Math.Max(0, st - 2.0) * 0.75;      // 격자가 성기면(넓은 범위) 도로 둘레를 조금 더 눌러 가장자리에서 지형이 안 솟게
                for (int i = 0; i < n; i++)
                {
                    V2 nl = g.t[i].Left;
                    L[i] = g.c[i] + nl * (g.wL[i] + Math.Max(g.swL[i], 0.5) + ext); R[i] = g.c[i] - nl * (g.wR[i] + Math.Max(g.swR[i], 0.5) + ext);
                    hL[i] = gen.RoadH(g, i, L[i]) + (g.swL[i] > 0 ? P.curbHeight : 0); hR[i] = gen.RoadH(g, i, R[i]) + (g.swR[i] > 0 ? P.curbHeight : 0);
                }
                for (int i = 0; i + 1 < n; i++)
                {
                    if (g.tmode[i] != 0 || g.tmode[i + 1] != 0) continue;            // 상판·터널은 지형을 끌어오지 않음
                    Tri(G, R[i], hR[i], R[i + 1], hR[i + 1], L[i + 1], hL[i + 1], put);
                    Tri(G, R[i], hR[i], L[i + 1], hL[i + 1], L[i], hL[i], put);
                }
            }
            // 1b) 교량·고가 밑: 지형이 상판을 뚫지 않게 '낮추기만' 하는 제한(하천 위 짧은 다리 등). 상판 - bridgeClear
            var low = new float[nx * nz];
            for (int i = 0; i < low.Length; i++) low[i] = float.NaN;
            Action<int, double> putLow = (k, h) => { float v = (float)(h - P.bridgeClear); if (float.IsNaN(low[k]) || v < low[k]) low[k] = v; };
            foreach (var g in gen.roadGeom.Values)
            {
                if (!g.ok || g.h == null) continue;
                int n = g.c.Count;
                for (int i = 0; i + 1 < n; i++)
                {
                    bool br = g.tmode[i] == 1 || g.tmode[i + 1] == 1;
                    if (!br) continue;
                    V2 n0 = g.t[i].Left, n1 = g.t[i + 1].Left;
                    V2 L0 = g.c[i] + n0 * (g.wL[i] + g.swL[i] + 1), R0 = g.c[i] - n0 * (g.wR[i] + g.swR[i] + 1);
                    V2 L1 = g.c[i + 1] + n1 * (g.wL[i + 1] + g.swL[i + 1] + 1), R1 = g.c[i + 1] - n1 * (g.wR[i + 1] + g.swR[i + 1] + 1);
                    Tri(G, R0, g.h[i], R1, g.h[i + 1], L1, g.h[i + 1], putLow);
                    Tri(G, R0, g.h[i], L1, g.h[i + 1], L0, g.h[i], putLow);
                }
            }

            // 2) 교차로 메쉬의 위를 보는 면
            foreach (var m in pieces)
            {
                if (m.kind != "junction") continue;
                foreach (var kv in m.tris)
                {
                    var t = kv.Value;
                    for (int i = 0; i + 2 < t.Count; i += 3)
                    {
                        V3 a = m.v[t[i]], b = m.v[t[i + 1]], c = m.v[t[i + 2]];
                        double ny = (b.z - a.z) * (c.x - a.x) - (b.x - a.x) * (c.z - a.z);   // 앞면 법선 y (유니티 규칙)
                        if (ny <= 1e-9) continue;
                        Tri(G, new V2(a.x, a.z), a.y, new V2(b.x, b.z), b.y, new V2(c.x, c.z), c.y, put);
                    }
                }
            }

            // 3) 거리 변환(3-4 챔퍼): 가장 가까운 도로 칸까지 거리와 그 칸 높이
            var dist = new float[nx * nz]; var src = new float[nx * nz];
            for (int k = 0; k < dist.Length; k++) { bool cov = !float.IsNaN(tgt[k]); dist[k] = cov ? 0 : float.MaxValue; src[k] = cov ? tgt[k] : 0; }
            float a1 = (float)st, a2 = (float)(st * Math.Sqrt(2));
            Action<int, int, int, int, float> relax = (r, c, rr, cc, w) =>
            {
                if (rr < 0 || rr >= nz || cc < 0 || cc >= nx) return;
                int k = r * nx + c, q = rr * nx + cc;
                if (dist[q] == float.MaxValue) return;
                float d = dist[q] + w; if (d < dist[k]) { dist[k] = d; src[k] = src[q]; }
            };
            for (int r = 0; r < nz; r++)
                for (int c = 0; c < nx; c++) { relax(r, c, r, c - 1, a1); relax(r, c, r - 1, c - 1, a2); relax(r, c, r - 1, c, a1); relax(r, c, r - 1, c + 1, a2); }
            for (int r = nz - 1; r >= 0; r--)
                for (int c = nx - 1; c >= 0; c--) { relax(r, c, r, c + 1, a1); relax(r, c, r + 1, c + 1, a2); relax(r, c, r + 1, c, a1); relax(r, c, r + 1, c - 1, a2); }

            // 3b) 땅으로 볼 곳: 지면 도로·교차로 둘레 landAroundRoads m 안(DEM 이 0 이어도 매립지·해안 도로 — 물 재질·물 높이로 안 누름)
            if (P.landAroundRoads > 0) { G.land = new bool[nx * nz]; for (int k = 0; k < dist.Length; k++) G.land[k] = dist[k] <= P.landAroundRoads; }

            // 4) 높이
            for (int r = 0; r < nz; r++)
                for (int c = 0; c < nx; c++)
                {
                    int k = r * nx + c; double x = x0 + c * st, z = zTop - r * st;
                    double d0 = gen.dem != null ? gen.dem.At(x, z) : 0;
                    if (!float.IsNaN(tgt[k])) { G.h[k] = tgt[k]; continue; }
                    if (dist[k] >= P.terrainBlend || dist[k] == float.MaxValue) { G.h[k] = (float)d0; continue; }
                    double w = Geom.Smooth(dist[k] / P.terrainBlend);
                    G.h[k] = (float)(src[k] + (d0 - src[k]) * w);
                }
            // 5) 교량·고가 밑 낮추기(도로가 덮은 칸은 그대로) — 둘레는 bridgeBlend m 비탈
            for (int k = 0; k < dist.Length; k++) { bool cov = !float.IsNaN(low[k]); dist[k] = cov ? 0 : float.MaxValue; src[k] = cov ? low[k] : 0; }
            for (int r = 0; r < nz; r++)
                for (int c = 0; c < nx; c++) { relax(r, c, r, c - 1, a1); relax(r, c, r - 1, c - 1, a2); relax(r, c, r - 1, c, a1); relax(r, c, r - 1, c + 1, a2); }
            for (int r = nz - 1; r >= 0; r--)
                for (int c = nx - 1; c >= 0; c--) { relax(r, c, r, c + 1, a1); relax(r, c, r + 1, c + 1, a2); relax(r, c, r + 1, c, a1); relax(r, c, r + 1, c - 1, a2); }
            for (int k = 0; k < dist.Length; k++)
            {
                if (!float.IsNaN(tgt[k]) || dist[k] >= P.bridgeBlend) continue;
                double lim = src[k] + (G.h[k] - src[k]) * Geom.Smooth(dist[k] / P.bridgeBlend);
                if (lim < G.h[k]) G.h[k] = (float)lim;
            }
            // 6) 땅 밑 구간: 덮개(터널·박스) 위는 흙이 구조물을 덮게(주변 맞춤으로 낮아졌으면 다시 올림), 개착(옹벽)은 구멍
            Action<int, double> putMax = (k, h) => { if (h > G.h[k]) G.h[k] = (float)h; };
            foreach (var g in gen.roadGeom.Values)
            {
                if (!g.ok || g.h == null) continue;
                int n = g.c.Count;
                for (int i = 0; i + 1 < n; i++)
                {
                    bool cov = g.tmode[i] == 2 && g.tmode[i + 1] == 2, cut = g.tmode[i] == 3 || g.tmode[i + 1] == 3 || (g.tmode[i] == 2) != (g.tmode[i + 1] == 2) && (g.tmode[i] == 3 || g.tmode[i + 1] == 3);
                    if (!cov && !cut) continue;
                    V2 n0 = g.t[i].Left, n1 = g.t[i + 1].Left;
                    if (cov)
                    {
                        double e = P.tubeMargin + P.tubeShell + 0.8;
                        V2 L0 = g.c[i] + n0 * (g.wL[i] + e), R0 = g.c[i] - n0 * (g.wR[i] + e), L1 = g.c[i + 1] + n1 * (g.wL[i + 1] + e), R1 = g.c[i + 1] - n1 * (g.wR[i + 1] + e);
                        double t0 = g.h[i] + gen.TubeTop(g, i) + P.coverMin, t1 = g.h[i + 1] + gen.TubeTop(g, i + 1) + P.coverMin;
                        Tri(G, R0, t0, R1, t1, L1, t1, putMax); Tri(G, R0, t0, L1, t1, L0, t0, putMax);
                    }
                    else
                    {   // 옹벽 바깥면보다 살짝 안쪽까지(지형 가장자리가 옹벽 몸체 안에 숨음)
                        double e = P.wallThick - 0.15;
                        V2 L0 = g.c[i] + n0 * (g.wL[i] + e), R0 = g.c[i] - n0 * (g.wR[i] + e), L1 = g.c[i + 1] + n1 * (g.wL[i + 1] + e), R1 = g.c[i + 1] - n1 * (g.wR[i + 1] + e);
                        G.holes.Add(new[] { R0, R1, L1 }); G.holes.Add(new[] { R0, L1, L0 });
                    }
                }
            }
            return G;
        }

        /// <summary>삼각형(위에서 본 x,z + 높이) 안의 격자점마다 put(인덱스, 높이)</summary>
        static void Tri(HeightGrid G, V2 a, double ha, V2 b, double hb, V2 c, double hc, Action<int, double> put)
        {
            double st = G.step;
            double den = (b.z - c.z) * (a.x - c.x) + (c.x - b.x) * (a.z - c.z);
            if (Math.Abs(den) < 1e-12) return;
            int c0 = Math.Max(0, (int)Math.Floor((Math.Min(a.x, Math.Min(b.x, c.x)) - G.x0) / st));
            int c1 = Math.Min(G.nx - 1, (int)Math.Ceiling((Math.Max(a.x, Math.Max(b.x, c.x)) - G.x0) / st));
            int r0 = Math.Max(0, (int)Math.Floor((G.z1 - Math.Max(a.z, Math.Max(b.z, c.z))) / st));
            int r1 = Math.Min(G.nz - 1, (int)Math.Ceiling((G.z1 - Math.Min(a.z, Math.Min(b.z, c.z))) / st));
            for (int r = r0; r <= r1; r++)
                for (int cc = c0; cc <= c1; cc++)
                {
                    double px = G.x0 + cc * st, pz = G.z1 - r * st;
                    double w1 = ((b.z - c.z) * (px - c.x) + (c.x - b.x) * (pz - c.z)) / den;
                    double w2 = ((c.z - a.z) * (px - c.x) + (a.x - c.x) * (pz - c.z)) / den;
                    double w3 = 1 - w1 - w2;
                    if (w1 < -1e-9 || w2 < -1e-9 || w3 < -1e-9) continue;
                    put(r * G.nx + cc, w1 * ha + w2 * hb + w3 * hc);
                }
        }

        /// <summary>높이 격자 → 메쉬 조각(tile m 단위). 재질 "terrain", UV = 월드 (x, z)</summary>
        public static List<MeshData> Meshes(HeightGrid G, double tile) { return Meshes(G, tile, null, 0); }
        /// <summary>지형 조각 메쉬. corridor > 0 이면 도로·교차로에서 corridor m 안에 걸친 조각은 촘촘히, 나머지는 성기게(terrainFarStep) — 경로 전체를 볼 때 가볍게.
        /// 촘촘·성긴 조각 경계의 틈은 치마(아래로 늘어뜨린 띠)로 가림. 개착 구멍(G.holes)은 삼각형을 정확히 잘라 냄</summary>
        public static List<MeshData> Meshes(HeightGrid G, double tile, RoadGenerator gen, double corridor)
        {
            var o = new List<MeshData>();
            int per = Math.Max(1, (int)Math.Round(tile / G.step));
            HashSet<long> keep = null;
            if (gen != null && corridor > 0)
            {   // 도로 단면 바깥 끝 + corridor 를 덮는 조각 번호
                keep = new HashSet<long>(); double tw = per * G.step;
                Action<V2, double> mark = (p, rad) =>
                {
                    int ca = (int)Math.Floor((p.x - rad - G.x0) / tw), cb = (int)Math.Floor((p.x + rad - G.x0) / tw);
                    int ra = (int)Math.Floor((G.z1 - (p.z + rad)) / tw), rb = (int)Math.Floor((G.z1 - (p.z - rad)) / tw);
                    for (int r = ra; r <= rb; r++) for (int c = ca; c <= cb; c++) keep.Add(((long)r << 32) | (uint)c);
                };
                foreach (var g in gen.roadGeom.Values)
                {
                    if (!g.ok) continue;
                    for (int i = 0; i < g.c.Count; i += 2) mark(g.c[i], Math.Max(g.wL[i] + g.swL[i], g.wR[i] + g.swR[i]) + corridor);
                }
                foreach (var JG in gen.junctionGeom.Values) { double rad = 0; foreach (var a in JG.j.arms) rad = Math.Max(rad, a.cut + Math.Max(a.wIn, a.wOut)); mark(JG.j.center, rad + corridor); }
            }
            double farStep = gen != null ? gen.P.terrainFarStep : 0;
            int farK = farStep > G.step ? Math.Max(1, (int)Math.Round(farStep / G.step)) : 0;
            while (farK > 1 && per % farK != 0) farK--;                       // 조각 안에 딱 맞게
            double skirt = gen != null ? gen.P.terrainSkirt : 3.0;
            // 구멍 삼각형 → 격자 칸 색인
            var holeAt = new Dictionary<long, List<int>>();
            for (int hi = 0; hi < G.holes.Count; hi++)
            {
                var t = G.holes[hi];
                double mnx = Math.Min(t[0].x, Math.Min(t[1].x, t[2].x)), mxx = Math.Max(t[0].x, Math.Max(t[1].x, t[2].x));
                double mnz = Math.Min(t[0].z, Math.Min(t[1].z, t[2].z)), mxz = Math.Max(t[0].z, Math.Max(t[1].z, t[2].z));
                int c0 = (int)Math.Floor((mnx - G.x0) / G.step), c1 = (int)Math.Floor((mxx - G.x0) / G.step);
                int r0 = (int)Math.Floor((G.z1 - mxz) / G.step), r1 = (int)Math.Floor((G.z1 - mnz) / G.step);
                for (int r = r0; r <= r1; r++) for (int c = c0; c <= c1; c++) { long key = ((long)r << 32) | (uint)c; List<int> l; if (!holeAt.TryGetValue(key, out l)) { l = new List<int>(); holeAt[key] = l; } l.Add(hi); }
            }
            int ntr = (G.nz - 2) / per + 1, ntc = (G.nx - 2) / per + 1;
            Func<int, int, bool> isFine = (a, b) => keep == null || farK <= 1 || keep.Contains(((long)a << 32) | (uint)b);
            Func<int, int, bool> exists = (a, b) => a >= 0 && b >= 0 && a < ntr && b < ntc && (keep == null || farK > 1 || keep.Contains(((long)a << 32) | (uint)b));
            for (int tr = 0; tr < G.nz - 1; tr += per)
                for (int tc = 0; tc < G.nx - 1; tc += per)
                {
                    int ti = tr / per, tj = tc / per;
                    bool fine = isFine(ti, tj);
                    if (!fine && farK <= 1) continue;                            // 성긴 채움을 안 쓰면 회랑 밖은 비움(예전 방식)
                    int k = fine ? 1 : farK;
                    int r1 = Math.Min(G.nz - 1, tr + per), c1 = Math.Min(G.nx - 1, tc + per);
                    var m = new MeshData { name = "Terrain_" + ti + "_" + tj, kind = "terrain", refId = -1 };
                    var rows = new List<int>(); for (int r = tr; r < r1; r += k) rows.Add(r); rows.Add(r1);
                    // 바다·강(원래 땅 높이가 0 인 곳: DEM 의 물): 꼭짓점 셋이 다 물이면 물 재질
                    var cols = new List<int>(); for (int c = tc; c < c1; c += k) cols.Add(c); cols.Add(c1);
                    int w = cols.Count; int b = m.v.Count;
                    var wet = new bool[rows.Count * w]; int wi = 0;
                    foreach (int r in rows) foreach (int c in cols)
                    {
                        double x = G.x0 + c * G.step, z = G.z1 - r * G.step, y = G.h[r * G.nx + c];
                        bool wv = IsWet(gen, G, r, c, x, z, y);
                        m.V(x, wv ? gen.P.waterLevel : y, z);                           // 물은 수면 높이로 평평하게(DEM 의 바닷속 깊이(−)도 수면으로 — 조각 경계 틈 없앰)
                        wet[wi++] = wv;
                    }
                    for (int a = 0; a + 1 < rows.Count; a++)
                        for (int q = 0; q + 1 < w; q++)
                        {
                            int nw = b + a * w + q, ne = nw + 1, sw = nw + w, se = sw + 1;
                            List<int> hl = null;
                            if (fine && holeAt.Count > 0) holeAt.TryGetValue(((long)rows[a] << 32) | (uint)cols[q], out hl);
                            if (hl == null)
                            {
                                int a0 = a * w + q; bool wNW = wet[a0], wNE = wet[a0 + 1], wSW = wet[a0 + w], wSE = wet[a0 + w + 1];
                                m.TriCCW(wSW && wSE && wNE ? "water" : "terrain", sw, se, ne); m.TriCCW(wSW && wNE && wNW ? "water" : "terrain", sw, ne, nw); continue;
                            }
                            ClipTri(m, G, sw, se, ne, hl); ClipTri(m, G, sw, ne, nw, hl);
                        }
                    // 치마: 성긴 조각은 네 변 모두, 촘촘한 조각은 성긴 조각(또는 빈 곳)과 맞닿는 변만
                    if (farK > 1)
                    {
                        Func<int, int, bool> needs = (a, c) => !fine || !exists(a, c) || !isFine(a, c);
                        if (needs(ti - 1, tj)) Skirt(m, G, rows[0], cols, true, skirt, gen);
                        if (needs(ti + 1, tj)) Skirt(m, G, rows[rows.Count - 1], cols, true, skirt, gen);
                        if (needs(ti, tj - 1)) Skirt(m, G, cols[0], rows, false, skirt, gen);
                        if (needs(ti, tj + 1)) Skirt(m, G, cols[cols.Count - 1], rows, false, skirt, gen);
                    }
                    o.Add(m);
                }
            return o;
        }

        /// <summary>물인가: DEM 이 물높이 이하, 또는 바다 한가운데 조금(2m 이하) 솟은 칸(다리 기초·앵커리지 자리 등 — 120m 또는 250m 둘레 8곳 중 7곳 이상이 물)</summary>
        static bool Wet(HeightGrid dem, double x, double z, double wl)
        {
            double d0 = dem.At(x, z); if (d0 <= wl) return true; if (d0 > 2.0) return false;
            foreach (double r in new[] { 120.0, 250.0 })                       // 작은 칸(다리 기초)·조금 큰 칸(앵커리지 자리 120m)
            {
                int n = 0;
                for (int k = 0; k < 8; k++) { double th = Math.PI * k / 4; if (dem.At(x + r * Math.Cos(th), z + r * Math.Sin(th)) <= wl) n++; }
                if (n >= 7) return true;
            }
            return false;
        }
        /// <summary>격자 한 줄(행 또는 열)을 따라 아래로 늘어뜨린 띠(양면)</summary>
        /// <summary>물로 그릴 꼭짓점인가(지면 도로 둘레 땅 칸 제외) — 면과 치마가 같은 높이를 쓰게</summary>
        static bool IsWet(RoadGenerator gen, HeightGrid G, int r, int c, double x, double z, double y)
        {
            return gen != null && gen.dem != null && y <= gen.P.waterLevel + 2.0 && !(G.land != null && G.land[r * G.nx + c]) && Wet(gen.dem, x, z, gen.P.waterLevel);
        }
        static void Skirt(MeshData m, HeightGrid G, int fixedIdx, List<int> along, bool isRow, double depth, RoadGenerator gen = null)
        {
            int prevT = -1, prevB = -1;
            foreach (int a in along)
            {
                int r = isRow ? fixedIdx : a, c = isRow ? a : fixedIdx;
                double x = G.x0 + c * G.step, z = G.z1 - r * G.step, y = G.h[r * G.nx + c];
                if (IsWet(gen, G, r, c, x, z, y)) y = gen.P.waterLevel;                   // 물 칸은 면처럼 수면 높이(치마가 물 위로 삐져나오지 않게)
                int t = m.V(x, y, z), bt = m.V(x, y - depth, z);
                if (prevT >= 0) { m.Wall("terrain", prevB, bt, prevT, t, true); m.Wall("terrain", prevB, bt, prevT, t, false); }
                prevT = t; prevB = bt;
            }
        }

        /// <summary>삼각형(정점 번호) − 구멍 삼각형들 → 남은 볼록 조각을 같은 평면 높이로</summary>
        static void ClipTri(MeshData m, HeightGrid G, int ia, int ib, int ic, List<int> holeIdx)
        {
            V3 A = m.v[ia], B = m.v[ib], C = m.v[ic];
            var pieces = new List<List<V2>> { new List<V2> { new V2(A.x, A.z), new V2(B.x, B.z), new V2(C.x, C.z) } };
            bool touched = false;
            foreach (int hi in holeIdx)
            {
                var H = G.holes[hi];
                var next = new List<List<V2>>();
                foreach (var pc in pieces)
                {
                    var res = Subtract(pc, H);
                    if (res == null) { next.Add(pc); continue; }                 // 안 겹침
                    touched = true; next.AddRange(res);
                }
                pieces = next; if (pieces.Count == 0) break;
            }
            if (!touched) { m.TriCCW("terrain", ia, ib, ic); return; }
            double den = (B.z - C.z) * (A.x - C.x) + (C.x - B.x) * (A.z - C.z);
            if (Math.Abs(den) < 1e-12) return;
            Func<V2, double> hAt = p =>
            {
                double w1 = ((B.z - C.z) * (p.x - C.x) + (C.x - B.x) * (p.z - C.z)) / den, w2 = ((C.z - A.z) * (p.x - C.x) + (A.x - C.x) * (p.z - C.z)) / den;
                return w1 * A.y + w2 * B.y + (1 - w1 - w2) * C.y;
            };
            foreach (var pc in pieces)
            {
                if (pc.Count < 3) continue;
                int b0 = m.V(pc[0], hAt(pc[0]));
                int prev = m.V(pc[1], hAt(pc[1]));
                for (int k = 2; k < pc.Count; k++) { int cur = m.V(pc[k], hAt(pc[k])); m.TriUp("terrain", b0, prev, cur); prev = cur; }
            }
        }

        /// <summary>볼록 다각형 P − 삼각형 H. 안 겹치면 null, 아니면 남은 볼록 조각들(빈 목록 = 다 덮임)</summary>
        static List<List<V2>> Subtract(List<V2> Pg, V2[] H)
        {
            // H 를 반시계로
            V2 a = H[0], b = H[1], c = H[2];
            if (V2.Cross(b - a, c - a) < 0) { var t = b; b = c; c = t; }
            var E = new[] { a, b, b, c, c, a };
            // 겹침 빠른 검사: P 가 어느 한 변의 바깥에 통째로 있으면 안 겹침
            for (int e = 0; e < 3; e++)
            {
                V2 p0 = E[2 * e], d = E[2 * e + 1] - p0; bool allOut = true;
                foreach (var q in Pg) if (V2.Cross(d, q - p0) > 1e-9) { allOut = false; break; }
                if (allOut) return null;
            }
            var res = new List<List<V2>>(); var rest = Pg;
            for (int e = 0; e < 3 && rest.Count >= 3; e++)
            {
                V2 p0 = E[2 * e], d = E[2 * e + 1] - p0;
                var outP = ClipHalf(rest, p0, d, false); if (outP.Count >= 3 && Area(outP) > 1e-4) res.Add(outP);
                rest = ClipHalf(rest, p0, d, true);
            }
            return res;
        }
        static double Area(List<V2> p) { double s = 0; for (int i = 0; i < p.Count; i++) { var q = p[(i + 1) % p.Count]; s += p[i].x * q.z - q.x * p[i].z; } return Math.Abs(s) * 0.5; }
        /// <summary>선(p0, 방향 d)의 왼쪽(keepLeft) 또는 오른쪽 반평면으로 자름</summary>
        static List<V2> ClipHalf(List<V2> poly, V2 p0, V2 d, bool keepLeft)
        {
            var o = new List<V2>(); int n = poly.Count;
            Func<V2, double> f = q => (keepLeft ? 1 : -1) * V2.Cross(d, q - p0);
            for (int i = 0; i < n; i++)
            {
                V2 A = poly[i], B = poly[(i + 1) % n]; double fa = f(A), fb = f(B);
                if (fa >= 0) o.Add(A);
                if ((fa >= 0) != (fb >= 0)) { double t = fa / (fa - fb); o.Add(A + (B - A) * t); }
            }
            return o;
        }
    }
}
