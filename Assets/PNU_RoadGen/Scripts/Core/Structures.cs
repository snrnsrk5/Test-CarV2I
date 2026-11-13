// PNU 도로 생성기 — 땅 밑 구간 구조물
//   개착(tmode 3) : 양옆 U형 옹벽(도로 가장자리 → 지형 위 난간벽까지). 지형은 TerrainBuilder 가 옹벽 안쪽을 뚫음
//   덮개(tmode 2) : 터널(004)은 말굽 아치, 그 밖(지하차도 박스)은 평 지붕. 안쪽 아래 3m 는 타일, 위는 콘크리트
//                   갱구(덮개 시작·끝) 가까이는 바깥 껍데기도(땅 위로 드러난 부분)
//   갱구 면벽     : 덮개 시작·끝에 터널 단면 모양 구멍이 난 벽(양옆·위로 지형까지)
using System;
using System.Collections.Generic;
using System.Linq;

namespace PNU.RoadGen
{
    public static class StructureBuilder
    {
        // 교각이 다른 길(밑을 지나는 길·터널 입구) 위에 서지 않게: 2m 칸마다 그 칸을 덮는 도로들
        static Dictionary<long, List<KeyValuePair<Road, double>>> occ;      // 칸 → (도로, 그 칸 노면 높이). 교차로는 도로 null
        static long Key(V2 p) { return ((long)Math.Floor(p.x / 2.0) << 32) ^ ((long)Math.Floor(p.z / 2.0) & 0xffffffffL); }
        /// <summary>기둥(반지름 r, 꼭대기 top)이 다른 길·교차로를 뚫는가 — 기둥보다 높은 곳을 지나는 길(복층 다리 위층 등)은 괜찮음</summary>
        static bool OnOtherRoad(V2 c, double r, Road self, double top)
        {
            for (double dx = -r - 1; dx <= r + 1; dx += 1.0)
                for (double dz = -r - 1; dz <= r + 1; dz += 1.0)
                {
                    List<KeyValuePair<Road, double>> l; if (!occ.TryGetValue(Key(c + new V2(dx, dz)), out l)) continue;
                    foreach (var e in l) if (e.Key != self && e.Value < top + 0.5) return true;
                }
            return false;
        }
        static void Occ(V2 p, Road r, double h)
        {
            long k = Key(p); List<KeyValuePair<Road, double>> l; if (!occ.TryGetValue(k, out l)) { l = new List<KeyValuePair<Road, double>>(); occ[k] = l; }
            foreach (var e in l) if (e.Key == r && Math.Abs(e.Value - h) < 1.0) return;
            l.Add(new KeyValuePair<Road, double>(r, h));
        }
        // 나란한 쌍둥이 지하차도·개착(상·하행이 붙어 박스가 겹침): 그 쪽 벽 대신 가운데(두 중심선 사이)까지 지붕을 잇고 가운데 기둥
        class Share { public double[] uL, uR, dL, dR, oL, oR; public int[] idL, idR; }     // u: 지붕 끝, d: 두 중심 거리, o: 이웃 반폭
        static Dictionary<RoadGeom, Share> share;
        static Share ShareOf(RoadGeom g) { Share sh; return share != null && share.TryGetValue(g, out sh) ? sh : null; }
        static void ComputeShare(RoadGenerator gen)
        {
            var P = gen.P; share = new Dictionary<RoadGeom, Share>();
            if (!P.twinTubes) return;
            var grid = new Dictionary<long, List<KeyValuePair<RoadGeom, int>>>();
            Func<V2, long> key = p => ((long)Math.Floor(p.x / 10.0) << 32) ^ ((long)Math.Floor(p.z / 10.0) & 0xffffffffL);
            foreach (var g in gen.roadGeom.Values)
            {
                if (!g.ok || g.h == null || g.tmode == null) continue;
                for (int i = 0; i < g.c.Count; i++)
                {
                    if (g.tmode[i] != 2 && g.tmode[i] != 3) continue;
                    long k = key(g.c[i]); List<KeyValuePair<RoadGeom, int>> l; if (!grid.TryGetValue(k, out l)) { l = new List<KeyValuePair<RoadGeom, int>>(); grid[k] = l; }
                    l.Add(new KeyValuePair<RoadGeom, int>(g, i));
                }
            }
            foreach (var g in gen.roadGeom.Values)
            {
                if (!g.ok || g.h == null || g.tmode == null || !g.road.oneWay) continue;     // 상·하행 분리(일방) 쌍만
                int n = g.c.Count; Share sh = null;
                for (int i = 0; i < n; i++)
                {
                    if (g.tmode[i] != 2 && g.tmode[i] != 3) continue;
                    if (g.tmode[i] == 2 && g.type[i] == "004") continue;              // 터널(아치)은 따로
                    V2 c = g.c[i], t = g.t[i], lf = t.Left;
                    double bestL = 1e9, bestR = 1e9, dL = 0, dR = 0, oLw = 0, oRw = 0; int idL = -1, idR = -1;
                    long kx = (long)Math.Floor(c.x / 10.0), kz = (long)Math.Floor(c.z / 10.0);
                    for (long ax = kx - 3; ax <= kx + 3; ax++)
                        for (long az = kz - 3; az <= kz + 3; az++)
                        {
                            List<KeyValuePair<RoadGeom, int>> l; if (!grid.TryGetValue((ax << 32) ^ (az & 0xffffffffL), out l)) continue;
                            foreach (var e in l)
                            {
                                var g2 = e.Key; int j = e.Value;
                                if (g2 == g || g2.road == g.road || !g2.road.oneWay) continue;  // 덮개·개착 섞여도 짝(한쪽만 덮이면 반쯤 트인 박스)
                                if (Math.Abs(g2.h[j] - g.h[i]) > 2.5 || Math.Abs(V2.Dot(t, g2.t[j])) < 0.94) continue;
                                V2 d = g2.c[j] - c; double lon = Math.Abs(V2.Dot(d, t)), lat = V2.Dot(d, lf);
                                if (lon > 2.5 || Math.Abs(lat) > 30) continue;
                                double mine = (lat > 0 ? g.wL[i] : g.wR[i]) + P.tubeMargin + P.tubeShell;
                                double other = Math.Max(g2.wL[j], g2.wR[j]) + P.tubeMargin + P.tubeShell;
                                if (Math.Abs(lat) > mine + other + 0.5) continue;               // 떨어져 있으면 각자 벽
                                if (lat > 0 && lon < bestL) { bestL = lon; dL = lat; idL = g2.road.id; oLw = Math.Max(g2.wL[j], g2.wR[j]); }
                                if (lat < 0 && lon < bestR) { bestR = lon; dR = -lat; idR = g2.road.id; oRw = Math.Max(g2.wL[j], g2.wR[j]); }
                            }
                        }
                    if (idL < 0 && idR < 0) continue;
                    if (sh == null) sh = new Share { uL = new double[n], uR = new double[n], dL = new double[n], dR = new double[n], oL = new double[n], oR = new double[n], idL = new int[n], idR = new int[n] };
                    if (idL >= 0) { sh.uL[i] = Math.Max(dL * 0.5, g.wL[i] + 0.3); sh.idL[i] = idL; sh.dL[i] = dL; sh.oL[i] = oLw; }
                    if (idR >= 0) { sh.uR[i] = Math.Max(dR * 0.5, g.wR[i] + 0.3); sh.idR[i] = idR; sh.dR[i] = dR; sh.oR[i] = oRw; }
                }
                if (sh == null) continue;
                // 짧게 끊긴 곳 메우기·짧은 공유 구간 지우기(6점 ≈ 12m)
                foreach (var arr in new[] { sh.uL, sh.uR })
                {
                    for (int i = 0; i < n; )
                    {
                        if (arr[i] > 0) { i++; continue; }
                        int j = i; while (j < n && arr[j] <= 0) j++;
                        if (i > 0 && j < n && j - i <= 3 && (g.tmode[i] == 2 || g.tmode[i] == 3)) for (int k = i; k < j; k++) arr[k] = (arr[i - 1] + arr[j]) * 0.5;
                        i = j;
                    }
                    for (int i = 0; i < n; )
                    {
                        if (arr[i] <= 0) { i++; continue; }
                        int j = i; while (j < n && arr[j] > 0) j++;
                        if (j - i < 6) for (int k = i; k < j; k++) arr[k] = 0;
                        i = j;
                    }
                }
                for (int side = 0; side < 2; side++)
                {
                    var u = side == 0 ? sh.uL : sh.uR; var d = side == 0 ? sh.dL : sh.dR; var ow = side == 0 ? sh.oL : sh.oR; var id = side == 0 ? sh.idL : sh.idR;
                    for (int i = 0; i < n; i++)
                    {
                        if (u[i] <= 0) { d[i] = 0; continue; }
                        if (d[i] > 0) continue;
                        int k = i - 1; while (k >= 0 && d[k] <= 0) k--;
                        if (k < 0) { k = i + 1; while (k < n && d[k] <= 0) k++; }
                        if (k >= 0 && k < n) { d[i] = d[k]; ow[i] = ow[k]; id[i] = id[k]; } else u[i] = 0;
                    }
                }
                share[g] = sh;
                if (Environment.GetEnvironmentVariable("RG_SHAREDBG") == g.road.id.ToString())
                    for (int i = 0; i < n; i += 10) Console.WriteLine("share " + g.road.id + " i" + i + " t" + g.tmode[i] + " uL " + sh.uL[i].ToString("0.00") + " uR " + sh.uR[i].ToString("0.00") + " wL " + g.wL[i].ToString("0.00") + " wR " + g.wR[i].ToString("0.00"));
            }
        }

        public static List<MeshData> Build(RoadGenerator gen, HeightGrid G, Func<V2, bool> inBox)
        {
            var o = new List<MeshData>();
            ComputeShare(gen);
            if (share.Count > 0) gen.net.log.Add("twin boxes: " + string.Join(", ", share.Select(kv => kv.Key.road.id + ":" + kv.Value.uL.Count(u => u > 0) + "/" + kv.Value.uR.Count(u => u > 0))));
            occ = new Dictionary<long, List<KeyValuePair<Road, double>>>();
            foreach (var g in gen.roadGeom.Values)
            {
                if (!g.ok || g.h == null) continue;
                for (int i = 0; i < g.c.Count; i++)
                {
                    V2 lf = g.t[i].Left;
                    for (double u = -(g.wR[i] + g.swR[i]); u <= g.wL[i] + g.swL[i] + 1e-6; u += 1.0) Occ(g.c[i] + lf * u, g.road, g.h[i]);
                }
            }
            foreach (var JG in gen.junctionGeom.Values)
            {
                if (!JG.ok || JG.poly.Count < 3) continue;
                double x0 = JG.poly.Min(p => p.x), x1 = JG.poly.Max(p => p.x), z0 = JG.poly.Min(p => p.z), z1 = JG.poly.Max(p => p.z);
                for (double x = x0; x <= x1; x += 1.0) for (double z = z0; z <= z1; z += 1.0) { var q = new V2(x, z); Occ(q, null, JG.H(q)); }
            }
            var gw = Gwangan.MakePlan(gen, G);
            foreach (var g in gen.roadGeom.Values)
            {
                if (!g.ok || g.h == null || g.tmode == null || !g.tmode.Any(t => t != 0)) continue;
                if (inBox != null && !g.c.Any(inBox)) continue;
                var m = new MeshData { name = "Struct_" + g.road.id + "_" + (string.IsNullOrEmpty(g.road.name) ? "-" : g.road.name), kind = "struct", refId = g.road.id };
                Func<V2, double> ter = p => G != null ? G.At(p.x, p.z) : gen.dem != null ? gen.dem.At(p.x, p.z) : 0;
                OpenCut(m, gen, g, ter);
                Tubes(m, gen, g, ter);
                TwinMedian(m, gen, g);
                if (gw != null && g == gw.gu) Decks(m, gen, g, ter, gw.UpSupported, true);
                else if (gw != null && g == gw.gl) Decks(m, gen, g, ter, gw.LoStacked, true);
                else Decks(m, gen, g, ter, null, false);
                if (m.v.Count > 0) o.Add(m);
            }
            Gwangan.Build(gen, gw, o);
            return o;
        }

        /// <summary>쌍둥이 지하차도·개착 사이 콘크리트 중앙분리대(높이 0.9m): 두 길 가운데까지, 두 차도가 겹치는 띠(노드링크 폭 탓)는 덮음 — 빈 땅·높이차 틈 가림</summary>
        static void TwinMedian(MeshData m, RoadGenerator gen, RoadGeom g)
        {
            var sh = ShareOf(g); if (sh == null) return;
            int n = g.c.Count; double bh = gen.P.twinBarrierHeight;
            for (int side = 0; side < 2; side++)
            {
                var d = side == 0 ? sh.dL : sh.dR; var ow = side == 0 ? sh.oL : sh.oR; double sg = side == 0 ? 1 : -1;
                Func<int, double> ua = i => { double w = side == 0 ? g.wL[i] : g.wR[i]; return Math.Max(d[i] * 0.5 - 1.2, Math.Min(w, d[i] - ow[i])) - 0.02; };
                Func<int, double> ub = i => d[i] * 0.5 + 0.02;
                for (int i = 0; i + 1 < n; i++)
                {
                    if (d[i] <= 0 || d[i + 1] <= 0) continue;
                    if ((g.tmode[i] != 2 && g.tmode[i] != 3) || (g.tmode[i + 1] != 2 && g.tmode[i + 1] != 3)) continue;
                    V2 l0 = g.t[i].Left, l1 = g.t[i + 1].Left;
                    V2 a0p = g.c[i] + l0 * (sg * ua(i)), a1p = g.c[i + 1] + l1 * (sg * ua(i + 1));
                    V2 b0p = g.c[i] + l0 * (sg * ub(i)), b1p = g.c[i + 1] + l1 * (sg * ub(i + 1));
                    double y0 = gen.RoadH(g, i, a0p), y1 = gen.RoadH(g, i + 1, a1p);
                    int a0 = m.V(a0p, y0 - 0.1), a1 = m.V(a1p, y1 - 0.1), at0 = m.V(a0p, y0 + bh), at1 = m.V(a1p, y1 + bh);
                    int bt0 = m.V(b0p, y0 + bh), bt1 = m.V(b1p, y1 + bh), bb0 = m.V(b0p, y0 - 1.5), bb1 = m.V(b1p, y1 - 1.5);
                    double w0 = side == 0 ? g.wL[i] : g.wR[i], w1 = side == 0 ? g.wL[i + 1] : g.wR[i + 1];
                    if (ua(i) > w0 + 0.05 || ua(i + 1) > w1 + 0.05)
                    {   // 차도 끝 ~ 분리대 사이 바닥(콘크리트)
                        V2 e0 = g.c[i] + l0 * (sg * (w0 - 0.02)), e1 = g.c[i + 1] + l1 * (sg * (w1 - 0.02));
                        int ea = m.V(e0, gen.RoadH(g, i, e0) - 0.02), eb = m.V(e1, gen.RoadH(g, i + 1, e1) - 0.02), fa = m.V(a0p, y0 - 0.02), fb = m.V(a1p, y1 - 0.02);
                        Quad(m, "curb", ea, eb, fb, fa, new V3(0, 1, 0));
                    }
                    Quad(m, "wall", at0, at1, bt1, bt0, new V3(0, 1, 0));                                  // 윗면
                    Quad(m, "wall", a0, a1, at1, at0, new V3(-sg * l0.x, 0, -sg * l0.z));                // 내 차도 쪽 면
                    Quad(m, "wall", bb0, bb1, bt1, bt0, new V3(sg * l0.x, 0, sg * l0.z));                // 가운데(이웃 쪽) 면 — 높이차 가림
                    if (g.tmode[i] == 2 && g.tmode[i + 1] == 2)
                    {   // 가운데 보(기둥 위): 지붕에서 1.4m 내려옴 — 두 박스 지붕 높이차 틈 가림
                        double H0 = g.h[i] + gen.P.boxHeight + 0.05, H1 = g.h[i + 1] + gen.P.boxHeight + 0.05, bd = 1.4;
                        V2 f0 = g.c[i] + l0 * (sg * (d[i] * 0.5 - 0.3)), f1 = g.c[i + 1] + l1 * (sg * (d[i + 1] * 0.5 - 0.3));
                        V2 k0 = g.c[i] + l0 * (sg * (d[i] * 0.5 + 0.02)), k1 = g.c[i + 1] + l1 * (sg * (d[i + 1] * 0.5 + 0.02));
                        int f0t = m.V(f0, H0), f1t = m.V(f1, H1), f0b = m.V(f0, H0 - bd), f1b = m.V(f1, H1 - bd), k0b = m.V(k0, H0 - bd), k1b = m.V(k1, H1 - bd);
                        Quad(m, "wall", f0b, f1b, f1t, f0t, new V3(-sg * l0.x, 0, -sg * l0.z));
                        Quad(m, "wall", f0b, k0b, k1b, f1b, new V3(0, -1, 0));
                    }
                }
            }
        }

        // ---------------- 개착 옹벽 ----------------
        static void OpenCut(MeshData m, RoadGenerator gen, RoadGeom g, Func<V2, double> ter)
        {
            var P = gen.P; int n = g.c.Count;
            for (int i = 0; i < n; )
            {
                if (g.tmode[i] != 3) { i++; continue; }
                int j = i; while (j < n && g.tmode[j] == 3) j++;
                int a = Math.Max(0, i - 1), b = Math.Min(n - 1, j);                // 앞뒤 한 점까지(구멍과 같은 범위)
                var sh = ShareOf(g);
                for (int side = 0; side < 2; side++)
                {
                    if (sh == null) { WallRun(m, gen, g, ter, a, b, side == 0); continue; }
                    var arr = side == 0 ? sh.uL : sh.uR;                          // 쌍둥이 개착: 붙은 쪽은 옹벽 없음(두 길 사이 트임)
                    for (int q = a; q <= b; )
                    {
                        if (arr[q] > 0) { q++; continue; }
                        int r = q; while (r <= b && arr[r] <= 0) r++;
                        WallRun(m, gen, g, ter, q, r - 1, side == 0);
                        q = r;
                    }
                }
                i = j;
            }
        }

        static void WallRun(MeshData m, RoadGenerator gen, RoadGeom g, Func<V2, double> ter, int a, int b, bool left)
        {
            var P = gen.P; int cnt = b - a + 1; if (cnt < 2) return;
            double sg = left ? 1 : -1;
            var pin = new V2[cnt]; var pout = new V2[cnt]; var yb = new double[cnt]; var yt = new double[cnt]; var yo = new double[cnt];
            for (int k = 0; k < cnt; k++)
            {
                int i = a + k; V2 lf = g.t[i].Left;
                double w = left ? g.wL[i] : g.wR[i];
                pin[k] = g.c[i] + lf * (sg * w); pout[k] = pin[k] + lf * (sg * P.wallThick);
                yb[k] = gen.RoadH(g, i, pin[k]) - 0.05;
                double tz = Math.Max(ter(pout[k]), ter(pout[k] + lf * (sg * 1.0)));
                yt[k] = Math.Max(tz, yb[k] + 0.6) + P.wallParapet;
                yo[k] = Math.Min(tz, yb[k]) - 1.0;
            }
            // 윗선 매끈하게(이웃 최대 → 평균)
            var mx = new double[cnt];
            for (int k = 0; k < cnt; k++) { mx[k] = yt[k]; for (int d = -2; d <= 2; d++) { int q = k + d; if (q >= 0 && q < cnt) mx[k] = Math.Max(mx[k], yt[q]); } }
            for (int k = 0; k < cnt; k++) { double s = 0; int c = 0; for (int d = -1; d <= 1; d++) { int q = k + d; if (q >= 0 && q < cnt) { s += mx[q]; c++; } } yt[k] = Math.Max(yt[k], s / c); }
            int[] ib = new int[cnt], it = new int[cnt], ot = new int[cnt], ob = new int[cnt];
            for (int k = 0; k < cnt; k++)
            {
                ib[k] = m.V(pin[k], yb[k]); it[k] = m.V(pin[k], yt[k]); ot[k] = m.V(pout[k], yt[k]); ob[k] = m.V(pout[k], yo[k]);
            }
            for (int k = 0; k + 1 < cnt; k++)
            {
                V2 lf = g.t[a + k].Left; V3 inward = new V3(-sg * lf.x, 0, -sg * lf.z), outward = new V3(sg * lf.x, 0, sg * lf.z);
                Quad(m, "wall", ib[k], ib[k + 1], it[k + 1], it[k], inward);
                Quad(m, "wall", it[k], it[k + 1], ot[k + 1], ot[k], new V3(0, 1, 0));
                Quad(m, "wall", ob[k], ob[k + 1], ot[k + 1], ot[k], outward);
            }
            // 끝 막음
            V2 t0 = g.t[a], t1 = g.t[b];
            Quad(m, "wall", ib[0], it[0], ot[0], ob[0], new V3(-t0.x, 0, -t0.z));
            Quad(m, "wall", ib[cnt - 1], it[cnt - 1], ot[cnt - 1], ob[cnt - 1], new V3(t1.x, 0, t1.z));
        }

        // ---------------- 덮개(터널·박스) + 갱구 면벽 ----------------
        /// <summary>안쪽 단면(옆 위치 u: 왼쪽 +, 높이 v: 도로 중심 높이 기준). 왼쪽 바닥 → 위 → 오른쪽 바닥</summary>
        static List<V2> Profile(RoadGenerator gen, RoadGeom g, int i, double grow)
        {
            var P = gen.P; var o = new List<V2>();
            double uL = g.wL[i] + P.tubeMargin + grow, uR = -(g.wR[i] + P.tubeMargin + grow);
            double vL = gen.RoadH(g, i, g.c[i] + g.t[i].Left * (g.wL[i])) - g.h[i] - 0.05 - (grow > 0 ? 0.6 : 0);
            double vR = gen.RoadH(g, i, g.c[i] - g.t[i].Left * (g.wR[i])) - g.h[i] - 0.05 - (grow > 0 ? 0.6 : 0);
            const double tileTop = 3.0, kerb = 0.25;
            bool inner = grow <= 0;
            if (inner) { o.Add(new V2(g.wL[i], vL)); o.Add(new V2(g.wL[i], vL + kerb)); vL += kerb; }      // 차도 가장자리 → 보도턱(점검로) → 벽
            if (g.type[i] == "004")
            {
                double r = (uL - uR) * 0.5, uc = (uL + uR) * 0.5, sp = P.archSpring;
                o.Add(new V2(uL, vL)); if (sp > tileTop + 0.1) o.Add(new V2(uL, tileTop)); o.Add(new V2(uL, sp));
                int seg = 14;
                for (int k = 1; k < seg; k++) { double th = Math.PI * k / seg; o.Add(new V2(uc + r * Math.Cos(th), sp + r * Math.Sin(th))); }
                o.Add(new V2(uR, sp)); if (sp > tileTop + 0.1) o.Add(new V2(uR, tileTop)); o.Add(new V2(uR, vR + (inner ? kerb : 0)));
            }
            else
            {
                double H = P.boxHeight + grow;
                o.Add(new V2(uL, vL)); o.Add(new V2(uL, tileTop)); o.Add(new V2(uL, H));
                o.Add(new V2((uL + uR) * 0.5, H));
                o.Add(new V2(uR, H)); o.Add(new V2(uR, tileTop)); o.Add(new V2(uR, vR + (inner ? kerb : 0)));
            }
            if (inner) { o.Add(new V2(-g.wR[i], vR + kerb)); o.Add(new V2(-g.wR[i], vR)); }
            var sh = ShareOf(g);
            if (sh != null && g.type[i] != "004" && (sh.uL[i] > 0 || sh.uR[i] > 0))
            {   // 쌍둥이 박스: 붙은 쪽 벽·점검로 점들을 지붕 끝(가운데)으로 모음 — 점 개수는 그대로(이웃 단면과 이어지게)
                double H = P.boxHeight + grow; int nL = inner ? 5 : 3;      // 왼쪽 벽 쪽 점 개수(지붕 가운데 점 앞까지)
                if (sh.uL[i] > 0) for (int k = 0; k < nL; k++) o[k] = new V2(sh.uL[i], H);
                if (sh.uR[i] > 0) for (int k = o.Count - nL; k < o.Count; k++) o[k] = new V2(-sh.uR[i], H);
                o[nL] = new V2((o[nL - 1].x + o[nL + 1].x) * 0.5, H);
            }
            return o;
        }

        static void Tubes(MeshData m, RoadGenerator gen, RoadGeom g, Func<V2, double> ter)
        {
            var P = gen.P; int n = g.c.Count;
            for (int i = 0; i < n; )
            {
                if (g.tmode[i] != 2) { i++; continue; }
                int j = i; while (j < n && g.tmode[j] == 2) j++;
                int a = i, b = j - 1;
                if (b > a)
                {
                    Shell(m, gen, g, a, b, false, ter);
                    Shell(m, gen, g, a, b, true, ter);
                    if (P.tunnelFixtures) Fixtures(m, gen, g, a, b);
                }
                bool startPortal = a > 0 || g.road.armStart == null, endPortal = b < n - 1 || g.road.armEnd == null;
                if (a > 0) Portal(m, gen, g, a, ter, false);
                if (b < n - 1) Portal(m, gen, g, b, ter, true);
                i = j;
            }
        }

        /// <summary>터널·지하차도 안 설비: 조명등(양쪽 어깨, tunnelLightSpacing 간격, 발광 재질 lamp) + 긴 터널(004, tunnelFanMinLen 이상)은
        /// 천장 제트팬 2대 한 쌍(tunnelFanSpacing 간격). 도로터널 조명·환기 설치 관행(입구 쪽 조명 촘촘히)</summary>
        static void Fixtures(MeshData m, RoadGenerator gen, RoadGeom g, int a, int b)
        {
            var P = gen.P; bool arch = g.type[a] == "004" || g.type[b] == "004";
            double s0 = g.cum[a], s1 = g.cum[b], len = s1 - s0;
            if (len < 6) return;
            Func<double, int> idx = sq => { int lo = a, hi = b; while (hi - lo > 1) { int mid = (lo + hi) / 2; if (g.cum[mid] <= sq) lo = mid; else hi = mid; } return lo; };
            // 조명: 입구·출구 쪽 tunnelLightEntry m 안은 촘촘히(간격 절반)
            double sp = Math.Max(2.0, P.tunnelLightSpacing);
            for (double sq = s0 + 3.0; sq <= s1 - 3.0; )
            {
                int i = idx(sq); V2 t = g.t[i], lf = t.Left; double hc = g.h[i];
                double uL = g.wL[i] + P.tubeMargin, uR = -(g.wR[i] + P.tubeMargin);
                for (int side = 0; side < 2; side++)
                {
                    double u, v;
                    if (arch)
                    {   // 아치 어깨(스프링선 위 55°)
                        double r = (uL - uR) * 0.5, uc = (uL + uR) * 0.5, th = side == 0 ? Math.PI * 0.30 : Math.PI * 0.70;
                        u = uc + (r - 0.25) * Math.Cos(th); v = P.archSpring + (r - 0.25) * Math.Sin(th);
                    }
                    else
                    {
                        var shf = ShareOf(g); double eL = shf != null && shf.uL[i] > 0 ? shf.uL[i] : uL, eR = shf != null && shf.uR[i] > 0 ? -shf.uR[i] : uR;
                        u = side == 0 ? eL - 0.7 : eR + 0.7; v = P.boxHeight - 0.12;
                    }
                    V2 c = g.c[i] + lf * u;
                    Box3(m, "lamp", c, t, lf, hc + v - 0.16, hc + v, 0.65, 0.16);
                }
                double dEnd = Math.Min(sq - s0, s1 - sq);
                sq += dEnd < P.tunnelLightEntry ? sp * 0.5 : sp;
            }
            // 쌍둥이 박스 가운데 기둥(번호 작은 도로만 세움): 기둥 자리가 차도에서 0.4m 이상 떨어질 때만
            var shc = ShareOf(g);
            if (!arch && shc != null)
                for (double sq = s0 + 4.0; sq <= s1 - 4.0; sq += P.twinPillarSpacing)
                {
                    int i = idx(sq);
                    for (int side = 0; side < 2; side++)
                    {
                        double dd = side == 0 ? shc.dL[i] : shc.dR[i]; int oid = side == 0 ? shc.idL[i] : shc.idR[i];
                        if (dd <= 0 || oid < g.road.id || g.tmode[i] != 2) continue;
                        V2 t = g.t[i], lf = t.Left; V2 c = g.c[i] + lf * (side == 0 ? dd * 0.5 : -dd * 0.5);
                        Box3(m, "wall", c, t, lf, g.h[i] - 0.3, g.h[i] + P.boxHeight + 0.2, 0.3, 0.22);
                    }
                }
            // 제트팬(긴 터널만): 천장 가운데 양쪽으로 한 쌍
            if (!arch || len < P.tunnelFanMinLen) return;
            for (double sq = s0 + P.tunnelFanSpacing * 0.5; sq <= s1 - P.tunnelFanSpacing * 0.4; sq += P.tunnelFanSpacing)
            {
                int i = idx(sq); V2 t = g.t[i], lf = t.Left; double hc = g.h[i];
                double uL = g.wL[i] + P.tubeMargin, uR = -(g.wR[i] + P.tubeMargin);
                double r = (uL - uR) * 0.5, uc = (uL + uR) * 0.5, crown = P.archSpring + r;
                foreach (double du in new[] { -1.55, 1.55 })
                {
                    double u = uc + du, vtop = P.archSpring + Math.Sqrt(Math.Max(0, r * r - du * du)) - 0.15, vc = vtop - 0.95;
                    V2 c = g.c[i] + lf * u;
                    HCyl(m, "gw_steel2", c - t * 2.0, c + t * 2.0, hc + vc, 0.55, 12);
                    Box3(m, "gw_steel2", c, t, lf, hc + vc + 0.5, hc + vtop + 0.2, 0.9, 0.08);      // 매달기 받침
                }
            }
        }
        /// <summary>바닥 중심 c(평면), 방향 t 를 따라 반길이 hl, 옆 반폭 hw, 높이 y0~y1 상자(바깥을 보는 6면)</summary>
        static void Box3(MeshData m, string mat, V2 c, V2 t, V2 n, double y0, double y1, double hl, double hw)
        {
            V2[] q = { c - t * hl - n * hw, c + t * hl - n * hw, c + t * hl + n * hw, c - t * hl + n * hw };
            int[] lo = new int[4], up = new int[4];
            for (int k = 0; k < 4; k++) { lo[k] = m.V(q[k], y0); up[k] = m.V(q[k], y1); }
            Quad(m, mat, up[0], up[1], up[2], up[3], new V3(0, 1, 0));
            Quad(m, mat, lo[0], lo[3], lo[2], lo[1], new V3(0, -1, 0));
            for (int k = 0; k < 4; k++)
            {
                int k1 = (k + 1) % 4; V2 mid = (q[k] + q[k1]) * 0.5 - c;
                Quad(m, mat, lo[k], lo[k1], up[k1], up[k], new V3(mid.x, 0, mid.z));
            }
        }
        /// <summary>수평 원기둥(p0→p1, 높이 y, 반지름 r) + 양 끝 막음</summary>
        static void HCyl(MeshData m, string mat, V2 p0, V2 p1, double y, double r, int sides)
        {
            V2 d = p1 - p0; double L = d.Len; if (L < 1e-6) return; V2 t = d * (1 / L), n = t.Left;
            var r0 = new int[sides]; var r1 = new int[sides];
            for (int k = 0; k < sides; k++)
            {
                double th = 2 * Math.PI * k / sides, cu = Math.Cos(th) * r, cv = Math.Sin(th) * r;
                r0[k] = m.V(p0 + n * cu, y + cv); r1[k] = m.V(p1 + n * cu, y + cv);
            }
            for (int k = 0; k < sides; k++)
            {
                int k1 = (k + 1) % sides; double th = 2 * Math.PI * (k + 0.5) / sides;
                Quad(m, mat, r0[k], r1[k], r1[k1], r0[k1], new V3(n.x * Math.Cos(th), Math.Sin(th), n.z * Math.Cos(th)));
            }
            for (int k = 1; k + 1 < sides; k++)
            {
                Tri(m, mat, r0[0], r0[k], r0[k + 1], new V3(-t.x, 0, -t.z));
                Tri(m, mat, r1[0], r1[k + 1], r1[k], new V3(t.x, 0, t.z));
            }
        }

        /// <summary>안쪽 면(outer=false) 또는 땅 위로 드러난 바깥 껍데기(outer=true)</summary>
        static void Shell(MeshData m, RoadGenerator gen, RoadGeom g, int a, int b, bool outer, Func<V2, double> ter)
        {
            var P = gen.P;
            List<V2> prev = null; int[] prevIdx = null; int prevI = -1;
            for (int i = a; i <= b; i++)
            {
                if (outer)
                {   // 지형 위로 드러날 수 있는 곳만(지형이 꼭대기 + 2m 보다 낮은 점) + 앞뒤 한 점
                    bool vis = false;
                    for (int q = Math.Max(a, i - 1); q <= Math.Min(b, i + 1) && !vis; q++) vis = ter(g.c[q]) < g.h[q] + gen.TubeTop(g, q) + 2.0;
                    if (!vis) { prev = null; prevIdx = null; continue; }
                }
                var pr = Profile(gen, g, i, outer ? P.tubeShell : 0);
                if (prev != null && prev.Count != pr.Count) { prev = null; prevIdx = null; }
                V2 lf = g.t[i].Left; var idx = new int[pr.Count];
                for (int k = 0; k < pr.Count; k++) idx[k] = m.V(g.c[i] + lf * pr[k].x, g.h[i] + pr[k].z);
                if (prev != null)
                {
                    V2 lf0 = g.t[prevI].Left;
                    double ucen = (pr[0].x + pr[pr.Count - 1].x) * 0.5, vmid = 2.5;
                    for (int k = 0; k + 1 < pr.Count; k++)
                    {
                        // 단면 중심 쪽(안쪽 면) 또는 바깥쪽을 보게
                        V2 mid2 = (pr[k] + pr[k + 1]) * 0.5;
                        double du = mid2.x - ucen, dv = mid2.z - vmid;
                        V3 dir = new V3(lf.x * du, dv, lf.z * du);
                        if (!outer) dir = new V3(-dir.x, -dir.y, -dir.z);
                        bool walk = !outer && (k <= 1 || k >= pr.Count - 3);
                        string mat = walk ? "curb" : !outer && Math.Max(pr[k].z, pr[k + 1].z) <= 3.05 ? "tunnel_tile" : "wall";
                        Quad(m, mat, prevIdx[k], idx[k], idx[k + 1], prevIdx[k + 1], dir);
                    }
                }
                prev = pr; prevIdx = idx; prevI = i;
            }
        }

        /// <summary>갱구 면벽: atEnd=false 면 덮개가 이 점에서 시작(밖은 뒤쪽), true 면 끝남</summary>
        static void Portal(MeshData m, RoadGenerator gen, RoadGeom g, int i, Func<V2, double> ter, bool atEnd)
        {
            var P = gen.P; V2 lf = g.t[i].Left, t = g.t[i];
            var inner = Profile(gen, g, i, 0);
            var shp = ShareOf(g); bool shL = shp != null && g.type[i] != "004" && shp.uL[i] > 0, shR = shp != null && g.type[i] != "004" && shp.uR[i] > 0;
            double uLo = shL ? inner[0].x : inner[0].x + P.tubeShell + P.portalWing, uRo = shR ? inner[inner.Count - 1].x : inner[inner.Count - 1].x - P.tubeShell - P.portalWing;
            double vt = gen.TubeTop(g, i) + 0.8;
            for (double u = uRo; u <= uLo + 1e-6; u += 1.0)
            {   // 면벽 위 끝은 지형(면 앞뒤 3m 중 높은 곳)보다 위
                V2 p = g.c[i] + lf * u;
                double z = Math.Max(ter(p), Math.Max(ter(p + t * 3), ter(p - t * 3)));
                vt = Math.Max(vt, z - g.h[i] + 0.4);
            }
            vt = Math.Min(vt, gen.TubeTop(g, i) + 12);                           // 너무 높은 벽은 자름(가파른 산)
            double vb = -0.6;
            var ring = new List<V2>();
            if (!shL) ring.Add(new V2(uLo, vb));
            ring.Add(new V2(uLo, vt)); ring.Add(new V2(uRo, vt));
            if (!shR) { ring.Add(new V2(uRo, vb)); ring.Add(new V2(inner[inner.Count - 1].x, vb)); }
            for (int k = inner.Count - 1; k >= 0; k--) ring.Add(inner[k]);
            if (!shL) ring.Add(new V2(inner[0].x, vb));
            for (int k = ring.Count - 1; k > 0; k--) if ((ring[k] - ring[k - 1]).Len < 1e-4) ring.RemoveAt(k);      // 모인 점(쌍둥이 쪽) 겹침 제거
            if (ring.Count > 2 && (ring[ring.Count - 1] - ring[0]).Len < 1e-4) ring.RemoveAt(ring.Count - 1);
            var tri = Geom.Triangulate(ring);
            // 두께 0.8m(밖으로): 앞면·뒷면
            double th = 0.8; V2 outDir = atEnd ? t : -t;
            for (int face = 0; face < 2; face++)
            {
                V2 off = outDir * (face == 0 ? th : 0);
                int b0 = m.v.Count;
                foreach (var q in ring) m.V(g.c[i] + off + lf * q.x, g.h[i] + q.z);
                V3 want = face == 0 ? new V3(outDir.x, 0, outDir.z) : new V3(-outDir.x, 0, -outDir.z);
                for (int k = 0; k + 2 < tri.Count; k += 3) Tri(m, "wall", b0 + tri[k], b0 + tri[k + 1], b0 + tri[k + 2], want);
            }
            // 위 끝 두께 면
            int a0 = m.V(g.c[i] + lf * uLo, g.h[i] + vt), a1 = m.V(g.c[i] + lf * uRo, g.h[i] + vt);
            int c0 = m.V(g.c[i] + outDir * th + lf * uLo, g.h[i] + vt), c1 = m.V(g.c[i] + outDir * th + lf * uRo, g.h[i] + vt);
            Quad(m, "wall", a0, a1, c1, c0, new V3(0, 1, 0));
        }

        // ---------------- 교량·고가 상판(옆면·밑면·방호벽) + 교각 ----------------
        static void Decks(MeshData m, RoadGenerator gen, RoadGeom g, Func<V2, double> ter, Func<int, bool> supported, bool barriersOnly)
        {
            var P = gen.P; int n = g.c.Count;
            for (int i = 0; i < n; )
            {
                if (g.tmode[i] != 1) { i++; continue; }
                int j = i; while (j < n && g.tmode[j] == 1) j++;
                int a = Math.Max(0, i - 1), b = Math.Min(n - 1, j);
                if (b - a >= 1) DeckRun(m, gen, g, ter, a, b, supported, barriersOnly);
                i = j;
            }
        }
        static void DeckRun(MeshData m, RoadGenerator gen, RoadGeom g, Func<V2, double> ter, int a, int b, Func<int, bool> supported, bool barriersOnly)
        {
            var P = gen.P; int cnt = b - a + 1;
            double dep = P.deckDepth, bw = P.barrierWidth, bh = P.barrierHeight;
            // 점마다: 왼·오른 바깥 끝(인도 포함) + 방호벽
            var Lo = new V2[cnt]; var Ro = new V2[cnt]; var Li = new V2[cnt]; var Ri = new V2[cnt]; var yl = new double[cnt]; var yr = new double[cnt];
            for (int k = 0; k < cnt; k++)
            {
                int i = a + k; V2 lf = g.t[i].Left;
                double eL = g.wL[i] + g.swL[i] + (g.swL[i] > 0 ? P.curbWidth : 0), eR = g.wR[i] + g.swR[i] + (g.swR[i] > 0 ? P.curbWidth : 0);
                Li[k] = g.c[i] + lf * eL; Lo[k] = g.c[i] + lf * (eL + bw); Ri[k] = g.c[i] - lf * eR; Ro[k] = g.c[i] - lf * (eR + bw);
                yl[k] = gen.RoadH(g, i, Li[k]) + (g.swL[i] > 0 ? P.curbHeight : 0); yr[k] = gen.RoadH(g, i, Ri[k]) + (g.swR[i] > 0 ? P.curbHeight : 0);
            }
            int[] lit = new int[cnt], lot = new int[cnt], lob = new int[cnt], lib = new int[cnt], rit = new int[cnt], rot = new int[cnt], rob = new int[cnt], rib = new int[cnt], lbt = new int[cnt], rbt = new int[cnt];
            for (int k = 0; k < cnt; k++)
            {
                lib[k] = m.V(Li[k], yl[k] - 0.02); lit[k] = m.V(Li[k], yl[k] + bh); lot[k] = m.V(Lo[k], yl[k] + bh); lob[k] = m.V(Lo[k], yl[k] - dep);
                rib[k] = m.V(Ri[k], yr[k] - 0.02); rit[k] = m.V(Ri[k], yr[k] + bh); rot[k] = m.V(Ro[k], yr[k] + bh); rob[k] = m.V(Ro[k], yr[k] - dep);
                lbt[k] = m.V(Lo[k], yl[k] - dep); rbt[k] = m.V(Ro[k], yr[k] - dep);   // 밑면용(재질 나눔)
            }
            for (int k = 0; k + 1 < cnt; k++)
            {
                V2 lf = g.t[a + k].Left; V3 L = new V3(lf.x, 0, lf.z), Rr = new V3(-lf.x, 0, -lf.z), Up = new V3(0, 1, 0), Dn = new V3(0, -1, 0);
                Quad(m, "wall", lib[k], lib[k + 1], lit[k + 1], lit[k], Rr);          // 방호벽 안쪽(도로 쪽)
                Quad(m, "wall", lit[k], lit[k + 1], lot[k + 1], lot[k], Up);          // 방호벽 윗면
                if (!barriersOnly) Quad(m, "wall", lob[k], lob[k + 1], lot[k + 1], lot[k], L);           // 바깥 옆면(상판 두께까지)
                Quad(m, "wall", rib[k], rib[k + 1], rit[k + 1], rit[k], L);
                Quad(m, "wall", rit[k], rit[k + 1], rot[k + 1], rot[k], Up);
                if (!barriersOnly) Quad(m, "wall", rob[k], rob[k + 1], rot[k + 1], rot[k], Rr);
                if (barriersOnly)
                {   // 방호벽 바깥 면은 노면 아래 30cm 까지만(거더 옆면은 광안대교 쪽에서)
                    int lo2 = m.V(Lo[k], yl[k] - 0.3), lo3 = m.V(Lo[k + 1], yl[k + 1] - 0.3), ro2 = m.V(Ro[k], yr[k] - 0.3), ro3 = m.V(Ro[k + 1], yr[k + 1] - 0.3);
                    Quad(m, "wall", lo2, lo3, lot[k + 1], lot[k], L); Quad(m, "wall", ro2, ro3, rot[k + 1], rot[k], Rr);
                }
                else Quad(m, "deck_under", lbt[k], lbt[k + 1], rbt[k + 1], rbt[k], Dn);    // 밑면
            }
            // 교각: pierSpacing 마다, 밑 지형이 상판 밑보다 2.5m 이상 낮을 때. 넓은 상판은 기둥 둘 + 가로보
            double acc = P.pierSpacing * 0.5;
            for (int k = 1; k < cnt; k++)
            {
                int i = a + k; acc += g.cum[i] - g.cum[i - 1];
                if (acc < P.pierSpacing) continue;
                if (supported != null && supported(i)) continue;                   // 광안대교 트러스·문형 교각 구간
                double ground = ter(g.c[i]), bot = g.h[i] - dep;
                if (bot - ground < 2.5) continue;
                acc = 0;
                V2 lf = g.t[i].Left, t = g.t[i];
                double eL = (Lo[k] - g.c[i]).Len, eR = (Ro[k] - g.c[i]).Len, width = eL + eR;
                var us = width > P.pierTwinWidth ? new[] { eL - width * 0.25, eL - width * 0.75 } : new[] { (eL - eR) * 0.5 };
                var cs = us.Select(u => g.c[i] + lf * u).ToList();
                if (cs.Any(c => OnOtherRoad(c, P.pierRadius, g.road, bot)))
                {   // 밑에 다른 길: 상판 폭 안에서 비어 있는 곳 하나를 찾고, 없으면 조금 더 가서 다시(복층 다리 위층은 아래층 바깥쪽에)
                    cs.Clear();
                    for (double u = eL - P.pierRadius - 0.3; u >= -eR + P.pierRadius + 0.3; u -= 1.0)
                    { V2 c = g.c[i] + lf * u; if (!OnOtherRoad(c, P.pierRadius, g.road, bot)) { cs.Add(c); break; } }
                    if (cs.Count == 0) { acc = P.pierSpacing * 0.8; continue; }
                }
                foreach (var c in cs)
                {
                    double gy = Math.Min(ter(c), ground) - 1.0;
                    Column(m, c, t, gy, bot - 1.2, P.pierRadius);
                }
                // 가로보(기둥머리): 상판 밑에 붙은 상자
                V2 c0 = g.c[i] + lf * (eL - 0.6), c1 = g.c[i] - lf * (eR - 0.6);
                Box(m, c0, c1, t, 1.0, bot - 1.2, bot);
            }
        }
        static void Column(MeshData m, V2 c, V2 t, double y0, double y1, double r)
        {
            const int N = 10; var bo = new int[N]; var to = new int[N];
            for (int k = 0; k < N; k++)
            {
                double th = 2 * Math.PI * k / N; V2 p = c + new V2(Math.Cos(th), Math.Sin(th)) * r;
                bo[k] = m.V(p, y0); to[k] = m.V(p, y1);
            }
            for (int k = 0; k < N; k++)
            {
                int q = (k + 1) % N; double th = 2 * Math.PI * (k + 0.5) / N;
                Quad(m, "wall", bo[k], bo[q], to[q], to[k], new V3(Math.Cos(th), 0, Math.Sin(th)));
            }
        }
        /// <summary>두 점(c0→c1) 사이 가로 상자: 진행 방향(t)으로 두께 th, 높이 y0~y1</summary>
        static void Box(MeshData m, V2 c0, V2 c1, V2 t, double th, double y0, double y1)
        {
            V2 h = t * (th * 0.5);
            var bt = new[] { c0 - h, c1 - h, c1 + h, c0 + h };
            int[] lo = bt.Select(p => m.V(p, y0)).ToArray(), hi = bt.Select(p => m.V(p, y1)).ToArray();
            V2 mid = (c0 + c1) * 0.5;
            for (int k = 0; k < 4; k++)
            {
                int q = (k + 1) % 4; V2 e = (bt[k] + bt[q]) * 0.5 - mid;
                Quad(m, "wall", lo[k], lo[q], hi[q], hi[k], new V3(e.x, 0, e.z));
            }
            Quad(m, "wall", lo[0], lo[1], lo[2], lo[3], new V3(0, -1, 0));
        }

        static void Quad(MeshData m, string mat, int a, int b, int c, int d, V3 want) { Tri(m, mat, a, b, c, want); Tri(m, mat, a, c, d, want); }
        public static void QuadPub(MeshData m, string mat, int a, int b, int c, int d, V3 want) { Quad(m, mat, a, b, c, d, want); }
        public static void TriPub(MeshData m, string mat, int a, int b, int c, V3 want) { Tri(m, mat, a, b, c, want); }
        /// <summary>삼각형을 앞면(유니티: 법선 = cross(p1-p0, p2-p0))이 want 쪽을 보게</summary>
        static void Tri(MeshData m, string mat, int a, int b, int c, V3 want)
        {
            V3 A = m.v[a], B = m.v[b], C = m.v[c];
            double ux = B.x - A.x, uy = B.y - A.y, uz = B.z - A.z, vx = C.x - A.x, vy = C.y - A.y, vz = C.z - A.z;
            double nx = uy * vz - uz * vy, ny = uz * vx - ux * vz, nz = ux * vy - uy * vx;
            if (nx * nx + ny * ny + nz * nz < 1e-14) return;
            List<int> l; if (!m.tris.TryGetValue(mat, out l)) { l = new List<int>(); m.tris[mat] = l; }
            if (nx * want.x + ny * want.y + nz * want.z >= 0) { l.Add(a); l.Add(b); l.Add(c); } else { l.Add(a); l.Add(c); l.Add(b); }
        }
    }
}
