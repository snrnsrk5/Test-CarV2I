// PNU 도로 생성기 — 광안대교(고객 지정 랜드마크: 실제 제원으로)
//   제원(구조물 정보·위키백과·OSM): 총연장 7,420m, 현수교 900m(측경간 200 + 주경간 500 + 측경간 200), 트러스교 720m, 접속교 5,800m,
//   2층(상층 = 남천동 방향, 하층 = 해운대 방향, 층마다 4차로), 와렌 트러스 보강형, 주케이블 2가닥, 형하공간 35m.
//   주탑·앵커리지 자리: OSM 기초 위치(주탑 (382.4,-4724.9)·(693.7,-4331.6), 앵커리지 각 탑에서 200m)
//   모양(사진 참고, 예전 b3_gwangan 과 같은 값): 흰색 두 기둥(가운데 가로보 없음), 꼭대기 반원 아치 판(윗면 오목),
//   하층 밑 아치(트러스 받침), 상층 높이 띠, 주케이블 r 0.31m, 격간(약 12m)마다 행어 2가닥, 상층 가로등 36m
//   노면 높이: 상층 주경간 가운데 46m(종단 곡선, 경사 1.8%), 하층 = 상층 − 7.8m
using System;
using System.Collections.Generic;
using System.Linq;

namespace PNU.RoadGen
{
    public static class Gwangan
    {
        public static readonly V2 TowerA = new V2(382.4, -4724.9), TowerB = new V2(693.7, -4331.6);
        public static V2 Mid { get { return (TowerA + TowerB) * 0.5; } }

        /// <summary>두 주탑 옆을 지나는 교량(003) 도로들(위층·아래층)</summary>
        public static List<Road> DeckRoads(RoadNetwork net)
        {
            var o = new List<Road>();
            foreach (var R in net.roads)
            {
                if (R.center == null || R.center.Count < 2) continue;
                double da = R.center.Min(p => (p - TowerA).Len), db = R.center.Min(p => (p - TowerB).Len);
                if (da < 30 && db < 30) o.Add(R);
            }
            return o;
        }
        static string TypeOf(Road R, double s)
        {
            foreach (var p in R.fwd.Concat(R.bwd)) if (s >= p.s0 - 1e-6 && s <= p.s1 + 1e-6) return p.type ?? "000";
            return "000";
        }
        /// <summary>주경간 가운데에서 이 도로의 OSM 층</summary>
        static int LayerOf(Road R)
        {
            V2 M = Mid; int kb = 0; double bd = 1e18;
            for (int k = 0; k < R.center.Count; k++) { double d = (R.center[k] - M).Len2; if (d < bd) { bd = d; kb = k; } }
            double s = R.cum[kb];
            foreach (var p in R.fwd.Concat(R.bwd)) if (s >= p.s0 - 1e-6 && s <= p.s1 + 1e-6) return p.layer;
            return 0;
        }
        /// <summary>(위층, 아래층). 층 정보(OSM)가 있으면 그것으로, 없으면 null</summary>
        public static bool Pair(RoadNetwork net, out Road up, out Road lo)
        {
            up = lo = null; var rs = DeckRoads(net);
            if (rs.Count < 2) return false;
            rs = rs.OrderByDescending(LayerOf).ToList();
            if (LayerOf(rs[0]) == LayerOf(rs[1])) return false;
            up = rs[0]; lo = rs[1]; return true;
        }

        /// <summary>아래층을 위층 바로 밑으로(트러스·문형 교각 구간): 두 길이 가까운(18m 안) 곳만, 끝 80m 는 그대로, 150m 에 걸쳐 부드럽게</summary>
        public static void Stack(RoadNetwork net, GenParams P)
        {
            Road up, lo; if (!P.gwEnable || !Pair(net, out up, out lo)) return;
            var U = up.center; var pts = lo.center; int n = pts.Count;
            var cum = Geom.CumLen(pts); double L = cum[n - 1]; var ucum = Geom.CumLen(U);
            var q = new V2[n]; var w = new double[n];
            double suMid = 0; { double bm = 1e18; for (int k = 0; k < U.Count; k++) { double dm = (U[k] - Mid).Len2; if (dm < bm) { bm = dm; suMid = ucum[k]; } } }
            double suA = 0; { double bm = 1e18; for (int k = 0; k < U.Count; k++) { double dm = (U[k] - TowerA).Len2; if (dm < bm) { bm = dm; suA = ucum[k]; } } }
            // 남천 쪽(남쪽 주탑 A 쪽)은 gwStackHalfS: 양 층이 같은 오거리로 내려가므로 위층이 7.8m 더 내려갈 거리를 남김
            Func<double, double> halfAt = su => (su - suMid) * (suA - suMid) > 0 ? P.gwStackHalfS : P.gwStackHalf;
            for (int i = 0; i < n; i++)
            {
                V2 best = pts[i]; double bd = 1e9, su = 0;
                for (int k = 0; k + 1 < U.Count; k++)
                {
                    V2 a = U[k], b = U[k + 1], d = b - a; double ll = V2.Dot(d, d); if (ll < 1e-9) continue;
                    double f = Math.Max(0, Math.Min(1, V2.Dot(pts[i] - a, d) / ll)); V2 c = a + d * f; double dd = (c - pts[i]).Len;
                    if (dd < bd) { bd = dd; best = c; su = ucum[k] + (ucum[k + 1] - ucum[k]) * f; }
                }
                // 둘 다 교량(003)인 곳만(양 끝 지하차도 구간은 그대로)
                bool br = TypeOf(lo, cum[i] / L * lo.Length) == "003" && TypeOf(up, su / ucum[ucum.Length - 1] * up.Length) == "003";
                q[i] = best; w[i] = br && bd < 18 && Math.Abs(su - suMid) < halfAt(su) && cum[i] > 80 && L - cum[i] > 80 ? 1 : 0;
            }
            // 150m 이동 평균(거리 기준)
            var ws = new double[n];
            for (int i = 0; i < n; i++)
            {
                double s = 0, c = 0;
                for (int k = i; k >= 0 && cum[i] - cum[k] <= 75; k--) { s += w[k]; c++; }
                for (int k = i + 1; k < n && cum[k] - cum[i] <= 75; k++) { s += w[k]; c++; }
                ws[i] = Geom.Smooth(s / c);
            }
            for (int i = 0; i < n; i++) pts[i] = pts[i] + (q[i] - pts[i]) * ws[i];
            double L2 = Geom.Length(pts), k2 = L2 / Math.Max(1e-6, lo.Length);
            foreach (var p in lo.fwd.Concat(lo.bwd)) { p.s0 *= k2; p.s1 *= k2; }
            lo.cum = Geom.CumLen(pts);
            net.log.Add("Gwangan: lower deck stacked under upper (road " + lo.id + " under " + up.id + ")");
        }

        /// <summary>종단: 상층 주경간 가운데 gwTop(46m), 경사 gwGrade, 곡선 길이 gwCurve. 아래층은 − deckGap. 접속부는 끝 높이에서 rampG 로</summary>
        public static bool Profile(RoadGenerator gen, RoadGeom g, double[] h, int a, int b, double ha, double hb)
        {
            var P = gen.P; Road up, lo;
            if (!P.gwEnable || !Pair(gen.net, out up, out lo) || (g.road != up && g.road != lo)) return false;
            int km = 0; double bd = 1e9; V2 M = Mid;
            for (int k = 0; k < g.c.Count; k++) { double d = (g.c[k] - M).Len; if (d < bd) { bd = d; km = k; } }
            if (bd > 40) return false;
            double top = P.gwTop - (g.road == lo ? P.deckGap : 0), sm = g.cum[km], sa = g.cum[a], sb = g.cum[b];
            // 위층은 겹친 구간 끝에서 도로 끝까지 아래층보다 deckGap 더 내려가야 함 → 그 쪽 접속 경사를 deckGap / 거리 만큼 더 가파르게
            int kA = 0; { double bA = 1e18; for (int k = 0; k < g.c.Count; k++) { double d = (g.c[k] - TowerA).Len; if (d < bA) { bA = d; kA = k; } } }
            double sTA = g.cum[kA];
            double hA = sTA < sm ? P.gwStackHalfS : P.gwStackHalf, hB = sTA < sm ? P.gwStackHalf : P.gwStackHalfS;   // a 쪽·b 쪽 겹침 범위
            double gA = P.gwApproachGrade, gB = P.gwApproachGrade;
            if (g.road == up)
            {
                gA += P.deckGap / Math.Max(150.0, (sm - hA) - sa);
                gB += P.deckGap / Math.Max(150.0, sb - (sm + hB));
            }
            for (int k = a; k <= b; k++)
            {
                double d = Math.Abs(g.cum[k] - sm);
                // 주경간 가운데 종단 곡선 → 1.8% → (현수교·트러스 밖 접속교) 1.2% — 예전 경로 높이(s 13050 29m, 12300 15m)와 맞춤
                double crest = d < P.gwCurve * 0.5 ? top - P.gwGrade / P.gwCurve * d * d : top - P.gwGrade * (Math.Min(d, P.gwBreak) - P.gwCurve * 0.25);
                if (d > P.gwBreak) crest -= P.gwGrade2 * (d - P.gwBreak);
                double ramp = Math.Min(ha + gA * (g.cum[k] - sa), hb + gB * (sb - g.cum[k]));
                h[k] = Math.Max(h[k], Math.Min(crest, ramp));
            }
            return true;
        }

        // ================== 구조물 ==================
        class Ref
        {
            public RoadGeom g;
            public void At(double s, out V2 c, out V2 n, out V2 t, out double z)
            {
                var cum = g.cum; int N = cum.Length; s = Math.Max(0, Math.Min(cum[N - 1], s));
                int lo = 0, hi = N - 1; while (hi - lo > 1) { int m = (lo + hi) / 2; if (cum[m] <= s) lo = m; else hi = m; }
                double f = cum[hi] - cum[lo] > 1e-9 ? (s - cum[lo]) / (cum[hi] - cum[lo]) : 0;
                c = g.c[lo] + (g.c[hi] - g.c[lo]) * f; t = (g.t[lo] * (1 - f) + g.t[hi] * f).Norm; n = t.Left; z = g.h[lo] + (g.h[hi] - g.h[lo]) * f;
            }
            public V3 P3(double s, double u, double dz) { V2 c, n, t; double z; At(s, out c, out n, out t, out z); V2 p = c + n * u; return new V3(p.x, z + dz, p.z); }
            public double Z(double s) { V2 c, n, t; double z; At(s, out c, out n, out t, out z); return z; }
        }

        public class Plan
        {
            public Road up, lo; public RoadGeom gu, gl; public double sT1, sT2, span, sMid, PAN, sA1, sA2, s0, s1; public List<double> SP = new List<double>();
            public Func<V2, double> ter;
            public double SOf(V2 p) { int kb = 0; double bd = 1e9; for (int k = 0; k < gu.c.Count; k++) { double d = (gu.c[k] - p).Len; if (d < bd) { bd = d; kb = k; } } return gu.cum[kb]; }
            /// <summary>위층 점 i: 트러스 또는 문형 교각이 받치는 곳(일반 교각·거더 옆면 안 그림)</summary>
            public bool UpSupported(int i) { double s = gu.cum[i]; return (s >= s0 - 1 && s <= s1 + 1) || gu.h[i] - ter(gu.c[i]) >= 9; }
            /// <summary>아래층 점 i 가 위층 바로 밑에 겹쳐 있음(문형 교각·트러스가 받침)</summary>
            public bool LoStacked(int i) { V2 p = gl.c[i]; double s = SOf(p); V2 c, n, t; double z; new Ref { g = gu }.At(s, out c, out n, out t, out z); return (c - p).Len < 3.0; }
        }
        public static Plan MakePlan(RoadGenerator gen, HeightGrid G)
        {
            var P = gen.P; Road up, lo;
            if (!P.gwEnable || !Pair(gen.net, out up, out lo)) return null;
            RoadGeom gu, gl; if (!gen.roadGeom.TryGetValue(up, out gu) || !gu.ok || !gen.roadGeom.TryGetValue(lo, out gl) || !gl.ok) return null;
            var pl = new Plan { up = up, lo = lo, gu = gu, gl = gl };
            pl.ter = p => G != null ? G.At(p.x, p.z) : gen.dem != null ? gen.dem.At(p.x, p.z) : 0;
            double sT1 = pl.SOf(TowerA), sT2 = pl.SOf(TowerB); if (sT1 > sT2) { var t = sT1; sT1 = sT2; sT2 = t; }
            pl.sT1 = sT1; pl.sT2 = sT2; pl.span = sT2 - sT1; pl.sMid = (sT1 + sT2) * 0.5;
            pl.PAN = pl.span / Math.Round(pl.span / P.gwPanel);
            pl.sA1 = sT1 - P.gwSideSpan; pl.sA2 = sT2 + P.gwSideSpan;
            double half = P.gwTrussLen * 0.5;
            int k0 = (int)Math.Ceiling((pl.sMid - half - sT1) / pl.PAN), k1 = (int)Math.Floor((pl.sMid + half - sT1) / pl.PAN);
            for (int k = k0; k <= k1; k++) pl.SP.Add(sT1 + pl.PAN * k);
            if (pl.SP.Count < 4) return null;
            pl.s0 = pl.SP[0]; pl.s1 = pl.SP[pl.SP.Count - 1];
            return pl;
        }

        public static void Build(RoadGenerator gen, Plan pl, List<MeshData> o)
        {
            if (pl == null) return;
            var P = gen.P; Road up = pl.up; RoadGeom gu = pl.gu, gl = pl.gl;
            var R = new Ref { g = gu };
            Func<V2, double> ter = pl.ter; Func<V2, double> sOf = pl.SOf;
            double sT1 = pl.sT1, sT2 = pl.sT2, span = pl.span, sMid = pl.sMid, PAN = pl.PAN, sA1 = pl.sA1, sA2 = pl.sA2, s0 = pl.s0, s1 = pl.s1;
            var SP = pl.SP;
            int im = 0; { double bd = 1e9; for (int k = 0; k < gu.c.Count; k++) { double d = Math.Abs(gu.cum[k] - sMid); if (d < bd) { bd = d; im = k; } } }
            double HWD = Math.Max(gu.wL[im], gu.wR[im]) + P.barrierWidth, HWT = HWD + 0.55, HWC = HWT + 2.6, DD = P.deckGap;
            var steel = new MeshData { name = "Gwangan_Truss", kind = "struct", refId = up.id };
            var tower = new MeshData { name = "Gwangan_Towers", kind = "struct", refId = up.id };
            var cable = new MeshData { name = "Gwangan_Cables", kind = "struct", refId = up.id };
            var conc = new MeshData { name = "Gwangan_Anchorage_Piers", kind = "struct", refId = up.id };
            var lamps = new MeshData { name = "Gwangan_Lamps", kind = "struct", refId = up.id };

            // ---- 와렌 트러스(양옆) + 바닥 가로보 + 하부 수평 브레이싱 ----
            for (int sg = -1; sg <= 1; sg += 2)
            {
                double u = sg * HWT;
                for (double s = s0; s < s1 - 1e-6; s += 3.0)
                {
                    double s2 = Math.Min(s1, s + 3.0);
                    Beam(steel, "gw_steel", R.P3(s, u, -0.75), R.P3(s2, u, -0.75), 1.0, 1.1);
                    Beam(steel, "gw_steel", R.P3(s, u, -DD - 0.95), R.P3(s2, u, -DD - 0.95), 1.0, 1.15);
                }
                for (int k = 0; k < SP.Count; k++)
                {
                    bool end = k == 0 || k == SP.Count - 1;
                    Beam(steel, "gw_steel", R.P3(SP[k], u, -DD - 0.4), R.P3(SP[k], u, -1.3), end ? 0.95 : 0.7, end ? 0.75 : 0.6);
                    if (k + 1 < SP.Count)
                    {
                        if (k % 2 == 0) Beam(steel, "gw_steel", R.P3(SP[k], u, -1.15), R.P3(SP[k + 1], u, -DD - 0.55), 0.62, 0.5);
                        else Beam(steel, "gw_steel", R.P3(SP[k], u, -DD - 0.55), R.P3(SP[k + 1], u, -1.15), 0.62, 0.5);
                    }
                }
            }
            for (int k = 0; k < SP.Count; k++)
            {
                Beam(steel, "gw_steel2", R.P3(SP[k], -HWT + 0.5, -1.05), R.P3(SP[k], HWT - 0.5, -1.05), 0.55, 1.1);
                Beam(steel, "gw_steel2", R.P3(SP[k], -HWT + 0.5, -DD - 1.05), R.P3(SP[k], HWT - 0.5, -DD - 1.05), 0.55, 1.2);
                if (k + 1 < SP.Count)
                {
                    Beam(steel, "gw_steel2", R.P3(SP[k], -HWT + 0.5, -DD - 1.45), R.P3(SP[k + 1], HWT - 0.5, -DD - 1.45), 0.32, 0.32);
                    Beam(steel, "gw_steel2", R.P3(SP[k], HWT - 0.5, -DD - 1.45), R.P3(SP[k + 1], -HWT + 0.5, -DD - 1.45), 0.32, 0.32);
                }
            }
            // 상층 바닥판 밑면(트러스 사이) — 하층에서 올려다볼 때
            Strip(steel, "deck_under", R, s0, s1, -HWT + 0.5, HWT - 0.5, -0.45, false);

            // ---- 주탑 ----
            double ZT = P.gwTowerTop;
            foreach (double sT in new[] { sT1, sT2 })
            {
                V2 c, n, t; double zu; R.At(sT, out c, out n, out t, out zu);
                Func<double, double> legC = zq => HWC + 1.3 * (ZT - zq) / (ZT - 4.0), legV = zq => 2.7 - 0.9 * (zq - 4.0) / (ZT - 4.0), legU = zq => 3.6 - 1.2 * (zq - 4.0) / (ZT - 4.0);
                for (int sg = -1; sg <= 1; sg += 2)
                {   // 기둥: 높이 따라 가늘어지는 상자(8 마디)
                    var rings = new List<V3[]>();
                    for (int k = 0; k <= 8; k++)
                    {
                        double zq = 4.0 + (ZT - 4.0) * k / 8.0, cy = sg * legC(zq), hv = legV(zq), hu = legU(zq);
                        V2 cc = c + n * cy;
                        rings.Add(new[] { Pt(cc - t * hu - n * hv, zq), Pt(cc + t * hu - n * hv, zq), Pt(cc + t * hu + n * hv, zq), Pt(cc - t * hu + n * hv, zq) });
                    }
                    Loft(tower, "gw_tower", rings, true);
                    // 상층 높이 받침 띠, 기단
                    double zb = zu - 2.0; V2 cb = c + n * (sg * legC(zb));
                    BoxAt(tower, "gw_tower2", cb, t, n, legU(zb) + 0.4, legV(zb) + 0.4, zb - 1.2, zb + 0.4);
                    V2 c0 = c + n * (sg * legC(4.0));
                    BoxAt(tower, "wall", c0, t, n, legU(4.0) + 1.2, legV(4.0) + 1.2, 1.0, 5.0);
                    // 항공 장애등(빨강)
                    V2 cl = c + n * (sg * legC(ZT));
                    BoxAt(tower, "gw_red", cl, t, n, 0.35, 0.35, ZT + 0.2, ZT + 0.9);
                }
                // 꼭대기 아치 판: 밑면 반원(기점 = 상층 위 52%), 윗면 오목(가운데 88% → 기둥 쪽 꼭대기 − 1)
                {
                    double H = ZT - zu, zs = zu + 0.52 * H, inner = legC(zs) - legV(zs);
                    var bot = new List<V2>(); var topL = new List<V2>();
                    int N = 16;
                    for (int k = 0; k <= N; k++)
                    {
                        double th = Math.PI * k / N, uu = inner * Math.Cos(th);
                        bot.Add(new V2(uu, zs + inner * 0.55 * Math.Sin(th)));
                        double f = Math.Abs(uu) / inner; topL.Add(new V2(uu, zu + 0.88 * H + (ZT - 1 - (zu + 0.88 * H)) * f * f));
                    }
                    ArchPlate(tower, "gw_tower", c, n, t, bot, topL, legU(zs) * 0.9);
                    // 하층 밑 아치(트러스 받침): 윗면 = 하현재 밑, 가운데 4.5m 더 내려감
                    double zt2 = zu - DD - 1.6, inner2 = legC(zt2) - legV(zt2);
                    var b2 = new List<V2>(); var t2 = new List<V2>();
                    for (int k = 0; k <= N; k++)
                    {
                        double th = Math.PI * k / N, uu = inner2 * Math.Cos(th);
                        b2.Add(new V2(uu, zt2 - 4.5 + 3.0 * (1 - Math.Sin(th)))); t2.Add(new V2(uu, zt2));
                    }
                    ArchPlate(tower, "gw_tower", c, n, t, b2, t2, legU(zt2) * 0.9);
                }
                // 케이슨
                Cyl(conc, "wall", c, 15.0, -6, 4.0, 24);
            }

            // ---- 주케이블(포물선) + 행어 ----
            double ZS = ZT + 2.2, zMidDeck = R.Z(sMid), zLow = zMidDeck + 2.6, sag = ZS - zLow;
            Func<double, double> cableZ = s =>
            {
                if (s >= sT1 && s <= sT2) { double x = (s - sMid) / (span * 0.5); return zLow + sag * x * x; }
                bool left = s < sT1; double sa = left ? sA1 : sA2, sb = left ? sT1 : sT2, za = R.Z(sa) + 1.5;
                double uu = (s - sa) / (sb - sa); double lin = za + (ZS - za) * uu; return lin - 0.16 * sag * 4 * uu * (1 - uu) * 0.35;
            };
            for (int sg = -1; sg <= 1; sg += 2)
            {
                var pts = new List<V3>();
                for (double s = sA1; s <= sA2 + 1e-6; s += 4.0) pts.Add(R.P3(s, sg * HWC, 0) .WithY(cableZ(s)));
                Tube(cable, "gw_cable", pts, 0.31, 8);
                for (int k = 0; k < SP.Count; k++)
                {
                    double s = SP[k]; if (s < sA1 + PAN || s > sA2 - PAN || Math.Abs(s - sT1) < PAN * 0.6 || Math.Abs(s - sT2) < PAN * 0.6) continue;
                    double zc = cableZ(s); if (zc - R.Z(s) < 0.8) continue;
                    V2 c, n, t; double zu; R.At(s, out c, out n, out t, out zu);
                    foreach (double ds in new[] { -0.25, 0.25 })
                    {
                        V2 p = c + n * (sg * HWC) + t * ds;
                        Beam(cable, "gw_cable", Pt(p, zc - 0.2), Pt(p, zu + 0.3), 0.07, 0.07);
                    }
                    // 행어 밑 브래킷(트러스 위현재 → 케이블 면) + 케이블 밴드
                    Beam(steel, "gw_steel", R.P3(s, sg * HWT, -0.75), R.P3(s, sg * HWC, 0.2), 0.5, 0.45);
                    Beam(cable, "gw_steel", Pt(c + n * (sg * HWC), zc - 0.15), Pt(c + n * (sg * HWC), zc + 0.45), 0.8, 0.9);
                }
            }

            // ---- 앵커리지: 경간 쪽 경사면(케이블이 들어감) 콘크리트 덩어리 ----
            foreach (var pr in new[] { new[] { sA1, -1.0 }, new[] { sA2, 1.0 } })
            {
                double sa = pr[0], dir = pr[1];        // dir: 경간 바깥쪽(+ 이면 s 증가 쪽)
                double zt = R.Z(sa) + 3.0, zb = -2.0, sFront = sa - dir * 14, sBack = sa + dir * 30;
                double zmid = R.Z(sa) - DD - 1.75;
                for (int side = -1; side <= 1; side += 2)
                    AnchorBlock(conc, R, sFront, sBack, sa, side * (HWT + 0.6), side * (HWC + 5.5), zb, zt);
                AnchorBlock(conc, R, sFront + dir * 6, sBack, sa + dir * 6, -(HWT + 0.6), HWT + 0.6, zb, zmid);
                V2 cc, nn, tt; double zz; R.At(sa + dir * 8, out cc, out nn, out tt, out zz);
                Cyl(conc, "wall", cc, 26.0, -6, -1.5, 28);
            }

            // ---- 접속교 문형 교각(트러스 밖): 두 기둥 + 하층·상층 받침 가로보 ----
            foreach (double dirSign in new[] { -1.0, 1.0 })
            {
                double sStart = dirSign < 0 ? s0 - 36 : s1 + 36, sEnd = dirSign < 0 ? 0 : gu.Length;
                for (double s = sStart; dirSign < 0 ? s > sEnd + 30 : s < sEnd - 30; s += dirSign * P.gwPortalSpacing)
                {
                    V2 c, n, t; double zu; R.At(s, out c, out n, out t, out zu);
                    double ground = Math.Min(ter(c), Math.Min(ter(c + n * HWD), ter(c - n * HWD)));
                    if (zu - ground < 9) continue;                       // 땅에 가까우면 그만(접속부 끝)
                    double zl = zu - DD; bool stacked = Math.Abs(gen.RoadHAt(gl, c) - zl) < 1.5 && gen.RoadDistAt(gl, c) < 3.0;
                    double cw = HWD + 1.0;
                    for (int sg = -1; sg <= 1; sg += 2) BoxAt(conc, "wall", c + n * (sg * cw), t, n, 1.1, 0.9, ground - 1.0, zu - 2.2);
                    BoxAt(conc, "wall", c, t, n, 1.2, cw + 0.9, zu - 3.6, zu - 2.0);          // 상층 받침
                    if (stacked) BoxAt(conc, "wall", c, t, n, 1.2, cw + 0.9, zl - 3.4, zl - 1.8); // 하층 받침
                }
            }
            // 접속교 상판 옆면(상·하층 모두): 박스 거더 느낌(1.8m) — 트러스 구간 밖
            foreach (var g in new[] { gu, gl })
            {
                for (int sg = -1; sg <= 1; sg += 2)
                    for (int i = 0; i + 1 < g.c.Count; i++)
                    {
                        if (g.tmode[i] != 1 || g.tmode[i + 1] != 1) continue;
                        double sc = g == gu ? g.cum[i] : sOf(g.c[i]);
                        if (sc >= s0 - 1 && sc <= s1 + 1) continue;
                        V2 l0 = g.t[i].Left, l1 = g.t[i + 1].Left;
                        double w0 = sg > 0 ? g.wL[i] : g.wR[i], w1 = sg > 0 ? g.wL[i + 1] : g.wR[i + 1];
                        V2 a = g.c[i] + l0 * (sg * (w0 + P.barrierWidth)), b = g.c[i + 1] + l1 * (sg * (w1 + P.barrierWidth));
                        int a0 = conc.V(a, g.h[i] - 0.05), b0 = conc.V(b, g.h[i + 1] - 0.05), a1 = conc.V(a, g.h[i] - P.deckDepth), b1 = conc.V(b, g.h[i + 1] - P.deckDepth);
                        StructureBuilder.QuadPub(conc, "gw_steel2", a1, b1, b0, a0, new V3(sg * l0.x, 0, sg * l0.z));
                    }
                for (int i = 0; i + 1 < g.c.Count; i++)
                {
                    if (g.tmode[i] != 1 || g.tmode[i + 1] != 1) continue;
                    double sc = g == gu ? g.cum[i] : sOf(g.c[i]);
                    if (sc >= s0 - 1 && sc <= s1 + 1) continue;
                    V2 l0 = g.t[i].Left, l1 = g.t[i + 1].Left;
                    int a = conc.V(g.c[i] + l0 * (g.wL[i] + P.barrierWidth), g.h[i] - P.deckDepth), b = conc.V(g.c[i + 1] + l1 * (g.wL[i + 1] + P.barrierWidth), g.h[i + 1] - P.deckDepth);
                    int c2 = conc.V(g.c[i + 1] - l1 * (g.wR[i + 1] + P.barrierWidth), g.h[i + 1] - P.deckDepth), d = conc.V(g.c[i] - l0 * (g.wR[i] + P.barrierWidth), g.h[i] - P.deckDepth);
                    StructureBuilder.QuadPub(conc, "deck_under", a, b, c2, d, new V3(0, -1, 0));
                }
            }

            // ---- 상층 가로등(양쪽, 36m) ----
            for (double s = 18; s < gu.Length - 18; s += P.gwLampSpacing)
            {
                int ii = Math.Max(0, Math.Min(gu.c.Count - 1, (int)Math.Round(s / P.step)));
                if (gu.tmode[ii] != 1) continue;
                V2 c, n, t; double zu; R.At(s, out c, out n, out t, out zu);
                for (int sg = -1; sg <= 1; sg += 2)
                {
                    double ub = sg * (HWD - 0.25);
                    V2 pb = c + n * ub, ph = c + n * (ub - sg * 2.2);
                    Beam(lamps, "gw_steel2", Pt(pb, zu + 1.0), Pt(pb, zu + 10.5), 0.22, 0.22);
                    Beam(lamps, "gw_steel2", Pt(pb, zu + 10.4), Pt(ph, zu + 10.4), 0.15, 0.15);
                    BoxAt(lamps, "lamp", ph, t, n, 0.35, 0.18, zu + 10.15, zu + 10.35);
                }
            }
            // ---- 경관 조명(밤): 주케이블 따라 점등, 트러스 위현재 바깥 띠, 주탑 기둥 위로 비추는 빛 — 처음엔 꺼 둠(유니티에서 켜기) ----
            var night = new MeshData { name = "Gwangan_NightLights", kind = "struct_night", refId = up.id };
            for (int sg = -1; sg <= 1; sg += 2)
            {
                for (double s = sA1 + 3; s <= sA2 - 3; s += 6.0)
                {
                    V3 p = R.P3(s, sg * HWC, 0).WithY(cableZ(s) + 0.45);
                    Beam(night, "gw_led", Add(p, new V3(0, -0.2, 0)), Add(p, new V3(0, 0.2, 0)), 0.35, 0.35);
                }
                for (double s = s0; s < s1 - 1e-6; s += 6.0)
                    Beam(night, "gw_led", R.P3(s, sg * (HWT + 0.56), -0.45), R.P3(Math.Min(s1, s + 5.6), sg * (HWT + 0.56), -0.45), 0.08, 0.18);
            }
            foreach (double sT in new[] { sT1, sT2 })
            {
                V2 c, n, t; double zu; R.At(sT, out c, out n, out t, out zu);
                for (int sg = -1; sg <= 1; sg += 2)
                {
                    double cy = sg * (HWC + 1.3 * (ZT - (zu + 1)) / (ZT - 4.0));
                    V2 pc = c + n * cy;
                    foreach (double ds in new[] { -4.2, 4.2 }) Beam(night, "gw_led", Pt(pc + t * ds, zu + 1.0), Pt(pc + t * ds * 0.7, ZT - 2), 0.12, 0.12);
                }
            }
            foreach (var m in new[] { steel, tower, cable, conc, lamps, night }) if (m.v.Count > 0) o.Add(m);
            gen.log.Add("Gwangan: towers s " + sT1.ToString("0") + "/" + sT2.ToString("0") + " span " + span.ToString("0.0") + " truss " + s0.ToString("0") + "-" + s1.ToString("0") + " panels " + (SP.Count - 1) + " (" + PAN.ToString("0.00") + "m)");
        }

        // ---------------- 도형 도우미 ----------------
        static V3 Pt(V2 p, double y) { return new V3(p.x, y, p.z); }
        static V3 WithY(this V3 v, double y) { return new V3(v.x, y, v.z); }
        static V3 Sub(V3 a, V3 b) { return new V3(a.x - b.x, a.y - b.y, a.z - b.z); }
        static V3 Add(V3 a, V3 b) { return new V3(a.x + b.x, a.y + b.y, a.z + b.z); }
        static V3 Mul(V3 a, double k) { return new V3(a.x * k, a.y * k, a.z * k); }
        static V3 Cross(V3 a, V3 b) { return new V3(a.y * b.z - a.z * b.y, a.z * b.x - a.x * b.z, a.x * b.y - a.y * b.x); }
        static double Len(V3 a) { return Math.Sqrt(a.x * a.x + a.y * a.y + a.z * a.z); }
        static V3 Nrm(V3 a) { double l = Len(a); return l > 1e-12 ? Mul(a, 1 / l) : new V3(0, 1, 0); }
        static int Vx(MeshData m, V3 p) { return m.V(p.x, p.y, p.z); }

        /// <summary>A→B 상자 보(가로 w × 세로 hgt)</summary>
        static void Beam(MeshData m, string mat, V3 A, V3 B, double w, double hgt)
        {
            V3 d = Sub(B, A); double L = Len(d); if (L < 1e-6) return; d = Mul(d, 1 / L);
            V3 side = Cross(d, new V3(0, 1, 0)); if (Len(side) < 1e-3) side = new V3(1, 0, 0); side = Nrm(side);
            V3 upv = Nrm(Cross(side, d));
            V3 sx = Mul(side, w * 0.5), sy = Mul(upv, hgt * 0.5);
            var c = new[] { Add(Sub(A, sx), Mul(sy, -1)), Sub(Add(A, sx), sy), Add(Add(A, sx), sy), Add(Sub(A, sx), sy) };
            var e = c.Select(p => Add(p, Mul(d, L))).ToArray();
            int[] ia = c.Select(p => Vx(m, p)).ToArray(), ib = e.Select(p => Vx(m, p)).ToArray();
            V3 mid = Add(A, Mul(d, L * 0.5));
            for (int k = 0; k < 4; k++)
            {
                int q = (k + 1) % 4; V3 fc = Mul(Add(Add(c[k], c[q]), Add(e[k], e[q])), 0.25);
                StructureBuilder.QuadPub(m, mat, ia[k], ia[q], ib[q], ib[k], Sub(fc, mid));
            }
            StructureBuilder.QuadPub(m, mat, ia[0], ia[1], ia[2], ia[3], Mul(d, -1));
            StructureBuilder.QuadPub(m, mat, ib[0], ib[1], ib[2], ib[3], d);
        }
        /// <summary>바닥 중심 c, 진행 t·옆 n 방향 반폭 hu·hv, 높이 y0~y1 상자</summary>
        static void BoxAt(MeshData m, string mat, V2 c, V2 t, V2 n, double hu, double hv, double y0, double y1)
        {
            var b = new[] { c - t * hu - n * hv, c + t * hu - n * hv, c + t * hu + n * hv, c - t * hu + n * hv };
            int[] lo = b.Select(p => m.V(p, y0)).ToArray(), hi = b.Select(p => m.V(p, y1)).ToArray();
            for (int k = 0; k < 4; k++) { int q = (k + 1) % 4; V2 e = (b[k] + b[q]) * 0.5 - c; StructureBuilder.QuadPub(m, mat, lo[k], lo[q], hi[q], hi[k], new V3(e.x, 0, e.z)); }
            StructureBuilder.QuadPub(m, mat, hi[0], hi[1], hi[2], hi[3], new V3(0, 1, 0));
            StructureBuilder.QuadPub(m, mat, lo[0], lo[1], lo[2], lo[3], new V3(0, -1, 0));
        }
        /// <summary>단면 고리들(아래 → 위)을 이은 기둥. cap 이면 윗면도</summary>
        static void Loft(MeshData m, string mat, List<V3[]> rings, bool cap)
        {
            int N = rings[0].Length; var idx = rings.Select(r => r.Select(p => Vx(m, p)).ToArray()).ToList();
            for (int k = 0; k + 1 < rings.Count; k++)
            {
                V3 cen = Mul(rings[k].Aggregate(new V3(0, 0, 0), Add), 1.0 / N);
                for (int a = 0; a < N; a++)
                {
                    int b = (a + 1) % N; V3 fc = Mul(Add(rings[k][a], rings[k][b]), 0.5);
                    StructureBuilder.QuadPub(m, mat, idx[k][a], idx[k][b], idx[k + 1][b], idx[k + 1][a], new V3(fc.x - cen.x, 0, fc.z - cen.z));
                }
            }
            if (cap) { var t = idx[idx.Count - 1]; StructureBuilder.QuadPub(m, mat, t[0], t[1], t[2], t[3], new V3(0, 1, 0)); }
        }
        /// <summary>교축 방향 두께 2·hu 인 판: 단면 (옆 u, 높이 z) 의 밑선 bot · 윗선 top(같은 개수, u 같은 순서)</summary>
        static void ArchPlate(MeshData m, string mat, V2 c, V2 n, V2 t, List<V2> bot, List<V2> top, double hu)
        {
            int N = bot.Count;
            Func<V2, double, int> v = (q, ds) => m.V(c + n * q.x + t * ds, q.z);
            var bf = bot.Select(q => v(q, -hu)).ToArray(); var bb = bot.Select(q => v(q, hu)).ToArray();
            var tf = top.Select(q => v(q, -hu)).ToArray(); var tb = top.Select(q => v(q, hu)).ToArray();
            V3 tm = new V3(t.x, 0, t.z), tmn = new V3(-t.x, 0, -t.z);
            for (int k = 0; k + 1 < N; k++)
            {
                StructureBuilder.QuadPub(m, mat, bf[k], bf[k + 1], tf[k + 1], tf[k], tmn);      // 앞면
                StructureBuilder.QuadPub(m, mat, bb[k], bb[k + 1], tb[k + 1], tb[k], tm);       // 뒷면
                StructureBuilder.QuadPub(m, mat, bf[k], bf[k + 1], bb[k + 1], bb[k], new V3(0, -1, 0));   // 밑면(아치)
                StructureBuilder.QuadPub(m, mat, tf[k], tf[k + 1], tb[k + 1], tb[k], new V3(0, 1, 0));    // 윗면
            }
        }
        static void Tube(MeshData m, string mat, List<V3> pts, double r, int sides)
        {
            List<int> prev = null;
            for (int k = 0; k < pts.Count; k++)
            {
                V3 d = k + 1 < pts.Count ? Sub(pts[k + 1], pts[k]) : Sub(pts[k], pts[k - 1]); d = Nrm(d);
                V3 a = Nrm(Cross(d, new V3(0, 1, 0))); if (Len(a) < 1e-3) a = new V3(1, 0, 0); V3 b = Nrm(Cross(a, d));
                var ring = new List<int>();
                for (int j = 0; j < sides; j++) { double th = 2 * Math.PI * j / sides; ring.Add(Vx(m, Add(pts[k], Add(Mul(a, r * Math.Cos(th)), Mul(b, r * Math.Sin(th)))))); }
                if (prev != null)
                    for (int j = 0; j < sides; j++)
                    {
                        int q = (j + 1) % sides; double th = 2 * Math.PI * (j + 0.5) / sides;
                        StructureBuilder.QuadPub(m, mat, prev[j], prev[q], ring[q], ring[j], Add(Mul(a, Math.Cos(th)), Mul(b, Math.Sin(th))));
                    }
                prev = ring;
            }
        }
        static void Cyl(MeshData m, string mat, V2 c, double r, double y0, double y1, int N)
        {
            var bo = new int[N]; var to = new int[N];
            for (int k = 0; k < N; k++) { double th = 2 * Math.PI * k / N; V2 p = c + new V2(Math.Cos(th), Math.Sin(th)) * r; bo[k] = m.V(p, y0); to[k] = m.V(p, y1); }
            for (int k = 0; k < N; k++) { int q = (k + 1) % N; double th = 2 * Math.PI * (k + 0.5) / N; StructureBuilder.QuadPub(m, mat, bo[k], bo[q], to[q], to[k], new V3(Math.Cos(th), 0, Math.Sin(th))); }
            int cc = m.V(c, y1); for (int k = 0; k < N; k++) StructureBuilder.TriPub(m, mat, cc, to[k], to[(k + 1) % N], new V3(0, 1, 0));
        }
        /// <summary>앵커리지 덩어리: 옆 u0~u1, 앞(경간 쪽) sFront 에서 경사면으로 sMidTop 까지 올라가 뒤 sBack 까지 윗면 zt</summary>
        static void AnchorBlock(MeshData m, Ref R, double sFront, double sBack, double sTop, double u0, double u1, double zb, double zt)
        {
            // 단면(교축 s, 높이 z): 앞 아래 → 앞 위(경사) → 뒤 위 → 뒤 아래
            var prof = new[] { new[] { sFront, zb }, new[] { sTop, zt }, new[] { sBack, zt }, new[] { sBack, zb } };
            Func<double, double, double, int> v = (s, u, z) => { V2 c, n, t; double zz; R.At(s, out c, out n, out t, out zz); V2 p = c + n * u; return m.V(p, z); };
            var a = prof.Select(q => v(q[0], u0, q[1])).ToArray(); var b = prof.Select(q => v(q[0], u1, q[1])).ToArray();
            V2 cm, nm, tm; double zm; R.At((sFront + sBack) * 0.5, out cm, out nm, out tm, out zm);
            double dirS = sBack > sFront ? 1 : -1;
            for (int k = 0; k < 4; k++)
            {
                int q = (k + 1) % 4;
                double ds = (prof[k][0] + prof[q][0]) * 0.5 - (sFront + sBack) * 0.5, dz = (prof[k][1] + prof[q][1]) * 0.5 - (zb + zt) * 0.5;
                StructureBuilder.QuadPub(m, "wall", a[k], a[q], b[q], b[k], new V3(tm.x * ds, dz, tm.z * ds));
            }
            V3 sn = new V3(nm.x * Math.Sign(u0 - u1), 0, nm.z * Math.Sign(u0 - u1));
            StructureBuilder.QuadPub(m, "wall", a[0], a[1], a[2], a[3], sn);
            StructureBuilder.QuadPub(m, "wall", b[0], b[1], b[2], b[3], new V3(-sn.x, 0, -sn.z));
        }
        static void Strip(MeshData m, string mat, Ref R, double s0, double s1, double u0, double u1, double dz, bool upFace)
        {
            int pa = -1, pb = -1;
            for (double s = s0; s <= s1 + 1e-6; s += 3.0)
            {
                int a = Vx(m, R.P3(s, u0, dz)), b = Vx(m, R.P3(s, u1, dz));
                if (pa >= 0) StructureBuilder.QuadPub(m, mat, pa, a, b, pb, new V3(0, upFace ? 1 : -1, 0));
                pa = a; pb = b;
            }
        }
    }
}
