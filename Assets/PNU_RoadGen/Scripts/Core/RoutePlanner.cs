// PNU 도로 생성기 — 주행 경로(차로 따라가기)
//  명세(도로 키 + 방향 줄들) → 차로 중심선 + 교차로 차로 연결선(손으로 이은 연결 포함)을 잇는 경로, 높이는 도로면·교차로 면에서
//  차로 고르기: 교차로마다 그 회전에 있는 연결만 쓰고(회전 차로 규칙), 차로 변경 수 + 달리는 차로 선호(1차로 피함 등)를 최소로 하는 동적 계획
//  명세 줄:  START x z | END x z | R <도로키> <+1|-1> [메모]   (+1 = 도로 시작→끝, −1 = 끝→시작, 줄 순서 = 달리는 순서)
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace PNU.RoadGen
{
    public class RouteStep
    {
        public string key, note; public int dir; public Road road; public RoadGeom g;
        public double sIn, sOut;                 // g 위 들어가고 나오는 s (dir>0 이면 sIn<sOut)
        public Arm armIn, armOut;                // 들어온 교차로 팔, 나갈 교차로 팔(없으면 null = 경로 처음·끝)
        public int nIn, nOut, nMin, best;        // 차로 수(들어갈 때·나올 때·가운데 최소), 달리기 좋은 차로
        public int a, c, b;                      // 고른 차로: 들어갈 때 · 달릴 때 · 나올 때 (1 = 1차로, 중앙선/왼쪽)
        public double Lch;                       // 차로 하나 바꾸는 길이(m)
        public double D { get { return Math.Abs(sOut - sIn); } }
        public double SG(double u) { return sIn + dir * u; }
    }

    public class RouteJunction { public int id; public string key, name; public double s0, s1, jog, phi; public int turn, kin, kout; public bool manual, synthetic; }

    public class RouteResult
    {
        public bool ok; public List<string> log = new List<string>();
        public List<RouteStep> steps = new List<RouteStep>();
        public List<RouteJunction> junctions = new List<RouteJunction>();
        public List<double[]> changes = new List<double[]>();             // [s0, s1, 차로 from, to]
        public List<double> s = new List<double>(), x = new List<double>(), y = new List<double>(), z = new List<double>();
        public List<int> lane = new List<int>(), kind = new List<int>(), step = new List<int>();   // kind 0 도로, 1 교차로, 2 차로 바꿈
        public double Length { get { return s.Count > 0 ? s[s.Count - 1] : 0; } }

        static string F(double v) { return Math.Round(v, 2).ToString("0.##", CultureInfo.InvariantCulture); }
        static string Q(string v) { return "\"" + (v ?? "").Replace("\\", "\\\\").Replace("\"", "\\\"") + "\""; }
        public string ToJson()
        {
            var sb = new StringBuilder();
            sb.Append("{\"note\":" + Q("PNU 주행 경로(차로 중심선). 유니티 좌표 X=동, Y=높이, Z=북(m). s = 수평 거리. kind 0 도로·1 교차로·2 차로 바꿈. lane 1 = 1차로(중앙선·왼쪽)"));
            sb.Append(",\"ok\":" + (ok ? "true" : "false") + ",\"length\":" + F(Length));
            sb.Append(",\"s\":[" + string.Join(",", s.Select(F)) + "]");
            sb.Append(",\"x\":[" + string.Join(",", x.Select(F)) + "]");
            sb.Append(",\"y\":[" + string.Join(",", y.Select(F)) + "]");
            sb.Append(",\"z\":[" + string.Join(",", z.Select(F)) + "]");
            sb.Append(",\"lane\":[" + string.Join(",", lane) + "]");
            sb.Append(",\"kind\":[" + string.Join(",", kind) + "]");
            sb.Append(",\"steps\":[" + string.Join(",", steps.Select(st => "{\"key\":" + Q(st.key) + ",\"id\":" + st.road.id + ",\"name\":" + Q(st.road.name) + ",\"dir\":" + st.dir
                + ",\"len\":" + F(st.D) + ",\"nIn\":" + st.nIn + ",\"nOut\":" + st.nOut + ",\"nMin\":" + st.nMin + ",\"a\":" + st.a + ",\"c\":" + st.c + ",\"b\":" + st.b + "}")) + "]");
            sb.Append(",\"junctions\":[" + string.Join(",", junctions.Select(j => "{\"id\":" + j.id + ",\"key\":" + Q(j.key) + ",\"name\":" + Q(j.name) + ",\"s0\":" + F(j.s0) + ",\"s1\":" + F(j.s1)
                + ",\"turn\":" + j.turn + ",\"jog\":" + F(j.jog) + ",\"phi\":" + F(j.phi * 180 / Math.PI) + ",\"kin\":" + j.kin + ",\"kout\":" + j.kout + ",\"manual\":" + (j.manual ? "true" : "false") + ",\"synthetic\":" + (j.synthetic ? "true" : "false") + "}")) + "]");
            sb.Append(",\"changes\":[" + string.Join(",", changes.Select(c => "[" + F(c[0]) + "," + F(c[1]) + "," + (int)c[2] + "," + (int)c[3] + "]")) + "]");
            sb.Append(",\"log\":[" + string.Join(",", log.Select(Q)) + "]}");
            return sb.ToString();
        }
    }

    public partial class RoadGenerator
    {
        // 경로 차로 고르기 가중치
        public double routeChangeCost = 1.0;       // 차로 한 번 바꿀 때
        public double routePrefPerKm = 1.0;        // 달리기 좋은 차로에서 한 차로 벗어나 1km 달릴 때
        public double routeEndRightCost = 0.6;     // 도착할 때 맨 오른쪽 차로가 아니면(차로 하나당)
        public double routeStep = 1.0;             // 결과 점 간격(m)

        static double RDbl(string s) { return double.Parse(s, CultureInfo.InvariantCulture); }

        int RouteNL(RouteStep st, double sg)
        {
            var R = st.road; double so = Geom.AtVal(st.g.sOrig, st.g.cum, sg);
            int n = R.oneWay ? Math.Max(R.LanesAt(R.fwd, so), R.LanesAt(R.bwd, so)) : (st.dir > 0 ? R.LanesAt(R.fwd, so) : R.LanesAt(R.bwd, so));
            return Math.Max(1, n);
        }
        double RouteLW(RouteStep st, double sg)
        {
            var R = st.road; var pc = PieceAt(R, Geom.AtVal(st.g.sOrig, st.g.cum, sg));
            return P.LaneWidth(pc != null && !string.IsNullOrEmpty(pc.rank) ? pc.rank : R.rank, pc != null && pc.spd > 0 ? pc.spd : 50);
        }
        int RouteSpd(RouteStep st, double sg)
        {
            var pc = PieceAt(st.road, Geom.AtVal(st.g.sOrig, st.g.cum, sg)); return pc != null && pc.spd > 0 ? pc.spd : 50;
        }
        /// <summary>차로 k(1 = 1차로, 소수 = 바꾸는 중)의 중심 가로 위치(도로 중심선 기준 왼쪽 +). 일방은 차로를 가운데 기준으로 놓음(nEff = 부드럽게 한 차로 수)</summary>
        double RouteLaneY(RouteStep st, double sg, double k, double nEff)
        {
            var R = st.road; double lw = RouteLW(st, sg);
            if (R.oneWay) { bool fw = R.fwd.Count > 0; return fw ? nEff * lw * 0.5 - (k - 0.5) * lw : -nEff * lw * 0.5 + (k - 0.5) * lw; }
            double half = R.divided ? P.medianHalf : P.centerHalfGap;
            return st.dir > 0 ? -(half + (k - 0.5) * lw) : (half + (k - 0.5) * lw);
        }
        static V2 RouteTan(RoadGeom g, double sg)
        {
            double L = g.cum[g.cum.Length - 1], a = Math.Max(0, sg - 1.0), b = Math.Min(L, sg + 1.0);
            var d = Geom.At(g.c, g.cum, b) - Geom.At(g.c, g.cum, a); return d.Len > 1e-9 ? d.Norm : new V2(1, 0);
        }

        sealed class ROpt { public int kin, kout; public LaneLink L; public bool synthetic; public double cost; }

        public RouteResult PlanRoute(IEnumerable<string> specLines)
        {
            var res = new RouteResult();
            // 도로 키는 같은 링크에서 나뉜 조각끼리 겹칠 수 있음 → 들어가고 나오는 교차로 키로 가림
            var byKey = new Dictionary<string, List<Road>>();
            foreach (var r in net.roads) { List<Road> l; var k = Keys.Road(r); if (!byKey.TryGetValue(k, out l)) byKey[k] = l = new List<Road>(); l.Add(r); }
            Func<Arm, string> jk = A => A != null && A.j != null ? Keys.Junction(A.j) : "-";
            bool hasStart = false, hasEnd = false; V2 start = new V2(), end = new V2();
            foreach (var raw in specLines)
            {
                var line = raw.Trim(); if (line.Length == 0 || line[0] == '#') continue;
                var a = line.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                if (a[0] == "START" && a.Length >= 3) { start = new V2(RDbl(a[1]), RDbl(a[2])); hasStart = true; }
                else if (a[0] == "END" && a.Length >= 3) { end = new V2(RDbl(a[1]), RDbl(a[2])); hasEnd = true; }
                else if (a[0] == "R" && a.Length >= 3)
                {   // R <도로키> <방향> [들어가는 교차로 키] [나오는 교차로 키] [메모]
                    List<Road> cands; if (!byKey.TryGetValue(a[1], out cands)) { res.log.Add("없는 도로 키 " + a[1]); return res; }
                    int dir = int.Parse(a[2]) >= 0 ? 1 : -1;
                    string jin = a.Length > 3 ? a[3] : "?", jout = a.Length > 4 ? a[4] : "?";
                    Road r = null; int bestScore = -1;
                    foreach (var c in cands)
                    {
                        string ein = jk(dir > 0 ? c.armStart : c.armEnd), eout = jk(dir > 0 ? c.armEnd : c.armStart);
                        int sc = (ein == jin ? 2 : 0) + (eout == jout ? 2 : 0) + (c.oneWay && ((c.fwd.Count > 0) != (dir > 0)) ? -5 : 0);
                        if (sc > bestScore) { bestScore = sc; r = c; }
                    }
                    if (cands.Count > 1 && bestScore < 2) res.log.Add("도로 키 " + a[1] + " 조각이 여럿인데 교차로 키가 안 맞음 → " + r.id + " " + r.name);
                    RoadGeom g; if (!roadGeom.TryGetValue(r, out g) || !g.ok) { res.log.Add("모양 없는 도로 " + a[1] + " " + r.name); return res; }
                    res.steps.Add(new RouteStep { key = a[1], dir = dir, road = r, g = g, note = a.Length > 5 ? string.Join(" ", a.Skip(5)) : "" });
                }
            }
            var S = res.steps; int m = S.Count;
            if (m == 0) { res.log.Add("명세에 도로 없음"); return res; }
            // 팔·구간
            for (int i = 0; i < m; i++)
            {
                var st = S[i]; var R = st.road; double L = st.g.cum[st.g.cum.Length - 1];
                if (R.oneWay && ((R.fwd.Count > 0) != (st.dir > 0))) { res.log.Add("일방 역주행: " + st.key + " " + R.name); return res; }
                st.armIn = i > 0 ? (st.dir > 0 ? R.armStart : R.armEnd) : null;
                st.armOut = i < m - 1 ? (st.dir > 0 ? R.armEnd : R.armStart) : null;
                st.sIn = st.dir > 0 ? 0 : L; st.sOut = st.dir > 0 ? L : 0;
                double sp;
                if (i == 0 && hasStart) { Geom.PolylineDist(st.g.c, start, out sp); st.sIn = sp; }
                if (i == m - 1 && hasEnd) { Geom.PolylineDist(st.g.c, end, out sp); st.sOut = sp; }
                if ((st.sOut - st.sIn) * st.dir < 0.5) { res.log.Add("구간 길이 0 또는 거꾸로: " + st.key + " " + R.name); return res; }
            }
            for (int i = 0; i + 1 < m; i++)
                if (S[i].armOut == null || S[i + 1].armIn == null || S[i].armOut.j != S[i + 1].armIn.j)
                { res.log.Add("이어지지 않음: " + S[i].key + " " + S[i].road.name + " → " + S[i + 1].key + " " + S[i + 1].road.name); return res; }
            // 차로 수·선호 차로·차로 바꿈 길이
            foreach (var st in S)
            {
                double D = st.D;
                st.nIn = st.armIn != null ? Math.Max(1, st.armIn.lanesOut) : RouteNL(st, st.sIn);
                st.nOut = st.armOut != null ? Math.Max(1, st.armOut.lanesIn) : RouteNL(st, st.sOut);
                double mg = Math.Min(20, D / 3); int nm = int.MaxValue;
                for (double u = mg; u <= D - mg + 1e-6; u += 5) nm = Math.Min(nm, RouteNL(st, st.SG(u)));
                st.nMin = nm == int.MaxValue ? Math.Min(st.nIn, st.nOut) : nm;
                bool express = P.IsExpress(st.road.rank) || st.road.divided;
                int n = st.nMin;
                st.best = n <= 1 ? 1 : n == 2 ? (express || st.road.oneWay ? 2 : 1) : 2;
                int spd = RouteSpd(st, st.SG(D * 0.5));
                st.Lch = Math.Max(30, spd / 3.6 * 3.0);
            }
            // 교차로마다 쓸 수 있는 연결
            var opts = new List<List<ROpt>>();
            for (int i = 0; i + 1 < m; i++)
            {
                var A = S[i].armOut; var X = S[i + 1].armIn; JunctionGeom G; junctionGeom.TryGetValue(A.j, out G);
                var o = new List<ROpt>();
                if (G != null && G.links != null)
                    foreach (var L in G.links) if (L.a.arm == A && L.b.arm == X) o.Add(new ROpt { kin = L.a.k, kout = L.b.k, L = L });
                if (o.Count == 0)
                {   // 연결이 없음(회전 금지·면 실패): 회전 종류로 차로 하나 골라 곡선을 새로 만듦
                    LaneEnd[] ins = null, outs = null;
                    if (G != null) { G.ins.TryGetValue(A, out ins); G.outs.TryGetValue(X, out outs); }
                    if (ins == null || ins.Length == 0) ins = LaneEndsForRoute(A, true);
                    if (outs == null || outs.Length == 0) outs = LaneEndsForRoute(X, false);
                    int turn = TurnOf(ins[0], outs[0]);
                    int ki = turn == 1 ? 1 : turn == 2 ? ins.Length : Math.Min(ins.Length, outs.Length);
                    int ko = turn == 1 ? 1 : turn == 2 ? outs.Length : ki;
                    var la = ins[ki - 1]; var lb = outs[ko - 1];
                    var path = LinkCurveVia(la.p, -A.dir, lb.p, X.dir, new List<V2>());
                    o.Add(new ROpt { kin = ki, kout = ko, synthetic = true, L = new LaneLink { a = la, b = lb, turn = turn, path = path, hw = la.lw * 0.5 } });
                    V2 dd = -A.dir; double th = Math.Atan2(V2.Cross(dd, X.dir), V2.Dot(dd, X.dir)) * 180 / Math.PI;
                    int fromA = G != null && G.links != null ? G.links.Count(L => L.a.arm == A) : -1;
                    var tg = G != null && G.links != null ? string.Join(",", G.links.Where(L => L.a.arm == A).Select(L => L.b.arm.road.id).Distinct()) : "";
                    bool banned = BannedTargets(A).Contains(X.road);
                    res.log.Add("연결 없음 → 새 곡선: " + Keys.Junction(A.j) + " " + A.j.name + " (" + S[i].road.id + " " + S[i].road.name + " → " + S[i + 1].road.id + " " + S[i + 1].road.name + ")"
                        + " 면ok " + (G != null && G.ok) + " 연결 " + (G != null && G.links != null ? G.links.Count : -1) + " 이 팔에서 " + fromA + "→[" + tg + "] 각 " + Math.Round(th) + " 금지 " + banned
                        + " 들어오는차로 " + A.lanesIn + " 나가는차로 " + X.lanesOut);
                }
                opts.Add(o);
            }
            // 동적 계획: 교차로 i 에서 고른 연결 → 다음 구간 들어가는 차로
            int T = m - 1;
            Func<int, int, int, double[]> stepCost = (i, a, b) =>
            {   // 반환: [비용, 고른 달리는 차로]
                var st = S[i]; double bestCost = double.MaxValue; int bestC = Math.Max(1, Math.Min(a, st.nMin));
                if (a < 1 || a > st.nIn || b < 1 || b > st.nOut) return new[] { 1e9, bestC };
                for (int c = 1; c <= Math.Max(1, st.nMin); c++)
                {
                    int nch = Math.Abs(c - a) + Math.Abs(b - c);
                    double cost = routeChangeCost * nch + routePrefPerKm * st.D / 1000.0 * Math.Abs(c - st.best);
                    double need = nch * st.Lch + (nch > 0 ? 20 : 0);
                    if (need > st.D) cost += 2 + (need - st.D) / 20.0;
                    if (cost < bestCost - 1e-9) { bestCost = cost; bestC = c; }
                }
                return new[] { bestCost, bestC };
            };
            var Vc = new List<double[]>(); var back = new List<int[]>();
            var stE = S[m - 1];
            if (T == 0)
            {
                double bc = double.MaxValue; int ba = 1, bb = 1;
                for (int a0 = 1; a0 <= S[0].nIn; a0++) for (int b0 = 1; b0 <= stE.nOut; b0++)
                    { double c = stepCost(0, a0, b0)[0] + routeEndRightCost * (stE.nOut - b0); if (c < bc) { bc = c; ba = a0; bb = b0; } }
                S[0].a = ba; S[0].b = bb; S[0].c = (int)stepCost(0, ba, bb)[1];
            }
            else
            {
                for (int i = 0; i < T; i++)
                {
                    var o = opts[i]; var v = new double[o.Count]; var bk = new int[o.Count];
                    for (int q = 0; q < o.Count; q++)
                    {
                        v[q] = double.MaxValue; bk[q] = -1;
                        if (i == 0)
                        {
                            for (int a0 = 1; a0 <= S[0].nIn; a0++) { double c = stepCost(0, a0, o[q].kin)[0]; if (c < v[q]) { v[q] = c; bk[q] = a0; } }
                        }
                        else
                        {
                            var po = opts[i - 1];
                            for (int p = 0; p < po.Count; p++) { double c = Vc[i - 1][p] + stepCost(i, po[p].kout, o[q].kin)[0]; if (c < v[q]) { v[q] = c; bk[q] = p; } }
                        }
                    }
                    Vc.Add(v); back.Add(bk);
                }
                double fb = double.MaxValue; int fq = 0, fbEnd = 1; var lo2 = opts[T - 1];
                for (int q = 0; q < lo2.Count; q++)
                    for (int b0 = 1; b0 <= stE.nOut; b0++)
                    { double c = Vc[T - 1][q] + stepCost(T, lo2[q].kout, b0)[0] + routeEndRightCost * (stE.nOut - b0); if (c < fb) { fb = c; fq = q; fbEnd = b0; } }
                var pick = new int[T]; pick[T - 1] = fq;
                for (int i = T - 1; i > 0; i--) pick[i - 1] = back[i][pick[i]];
                S[0].a = back[0][pick[0]];
                for (int i = 0; i < T; i++) { var o = opts[i][pick[i]]; S[i].b = o.kin; S[i + 1].a = o.kout; opts[i] = new List<ROpt> { o }; }
                stE.b = fbEnd;
                for (int i = 0; i < m; i++) S[i].c = (int)stepCost(i, S[i].a, S[i].b)[1];
                if (fb > 1e8) res.log.Add("차로 고르기: 맞는 차로 조합 없음(억지로 이음)");
            }
            // 점 만들기
            var X_ = new List<V2>(); var H_ = new List<double>(); var K_ = new List<int>(); var KD_ = new List<int>(); var ST_ = new List<int>(); var U_ = new List<double>();
            var chg = new List<double[]>(); var jrec = new List<RouteJunction>(); var jIdx = new List<int[]>();
            for (int i = 0; i < m; i++)
            {
                var st = S[i]; var g = st.g; double D = st.D;
                // 차로 바꿈 자리
                var ch = PlaceChanges(st);
                // 일방: 차로 수 바뀌는 곳 가로 위치가 튀지 않게 차로 수를 80m 에 걸쳐 부드럽게
                int nu = (int)Math.Ceiling(D) + 1; var nRaw = new double[nu];
                for (int k = 0; k < nu; k++) nRaw[k] = RouteNL(st, st.SG(Math.Min(D, k)));
                var nEff = BoxSmooth(BoxSmooth(nRaw, 40), 40);
                var us = new List<double>(); for (double u = 0; u < D; u += routeStep) us.Add(u); us.Add(D);
                foreach (var c in ch) { us.Add(c[0]); us.Add(c[1]); }
                us = us.Where(u => u >= 0 && u <= D).Distinct().OrderBy(u => u).ToList();
                int startIdx = X_.Count;
                foreach (double u in us)
                {
                    double k = st.a; bool changing = false;
                    foreach (var c in ch) { double f = (u - c[0]) / Math.Max(1e-6, c[1] - c[0]); if (f > 0) k += (c[3] - c[2]) * Geom.Smooth(f); if (f > 0 && f < 1) changing = true; }
                    double sg = st.SG(u); V2 cpt = Geom.At(g.c, g.cum, sg), t = RouteTan(g, sg);
                    double ne = nEff[Math.Min(nu - 1, (int)Math.Round(u))];
                    V2 p = cpt + t.Left * RouteLaneY(st, sg, k, ne);
                    double hc = Geom.AtVal(g.h, g.cum, sg);
                    X_.Add(p); H_.Add(RoadHs(g, sg, hc, p)); K_.Add((int)Math.Round(k)); KD_.Add(changing ? 2 : 0); ST_.Add(i); U_.Add(u);
                }
                // 처음·끝을 교차로 차로 끝점에 맞춤(어긋난 만큼 8m 에 걸쳐 나눠 옮김)
                if (i > 0) { var o = opts[i - 1][0]; FitEnds(X_, startIdx, X_.Count - 1, o.L.b.p, true, res, st); }
                if (i < m - 1) { var o = opts[i][0]; FitEnds(X_, startIdx, X_.Count - 1, o.L.a.p, false, res, st); }
                foreach (var c in ch) chg.Add(new[] { (double)startIdx, c[0], c[1], c[2], c[3], i, (double)(startIdx + us.Count - 1) });
                // 교차로 연결선
                if (i < m - 1)
                {
                    var o = opts[i][0]; var A = st.armOut; JunctionGeom G; junctionGeom.TryGetValue(A.j, out G);
                    int j0 = X_.Count - 1; var path = o.L.path; double hA = H_[H_.Count - 1];
                    // 다음 구간 첫 점 높이(연결선 끝 높이용) — 교차로 면이 없으면 직선 보간
                    var nx = S[i + 1]; double hB = nx.g.h != null ? RoadHs(nx.g, nx.sIn, Geom.AtVal(nx.g.h, nx.g.cum, nx.sIn), o.L.b.p) : hA;
                    double plen = Geom.Length(path), acc = 0;
                    for (int q = 1; q < path.Count - 1; q++)
                    {
                        acc += (path[q] - path[q - 1]).Len;
                        double hh = G != null && G.ok ? G.H(path[q]) : hA + (hB - hA) * (plen > 0 ? acc / plen : 0);
                        X_.Add(path[q]); H_.Add(hh); K_.Add(-1); KD_.Add(1); ST_.Add(i); U_.Add(double.NaN);
                    }
                    jIdx.Add(new[] { j0, X_.Count });   // 연결선 시작 점 번호, 다음 구간 첫 점 번호
                    V2 dIn = (-A.dir).Norm, pa = o.L.a.p, pb = o.L.b.p;
                    double jog = Math.Abs(V2.Cross(dIn, pb - pa)), phi = Math.Abs(Math.Atan2(V2.Cross(dIn, S[i + 1].armIn.dir), V2.Dot(dIn, S[i + 1].armIn.dir)));
                    jrec.Add(new RouteJunction { id = A.j.id, key = Keys.Junction(A.j), name = A.j.name, turn = o.L.turn, kin = o.kin, kout = o.kout, manual = G != null && G.manual, synthetic = o.synthetic, jog = jog, phi = phi });
                }
            }
            // 거리 → 고른 간격으로 다시 찍기
            var cum = new double[X_.Count]; for (int i = 1; i < X_.Count; i++) cum[i] = cum[i - 1] + (X_[i] - X_[i - 1]).Len;
            for (int q = 0; q < jrec.Count; q++) { jrec[q].s0 = cum[jIdx[q][0]]; jrec[q].s1 = cum[Math.Min(cum.Length - 1, jIdx[q][1])]; }
            foreach (var c in chg)
            {   // 구간 점들 중 u 가 가장 가까운 점의 경로 거리
                int a0 = (int)c[0], a1 = (int)c[6]; Func<double, double> sOfU = u => { int bi = a0; double bd = double.MaxValue; for (int q = a0; q <= a1; q++) { double dd = Math.Abs(U_[q] - u); if (dd < bd) { bd = dd; bi = q; } } return cum[bi]; };
                res.changes.Add(new[] { sOfU(c[1]), sOfU(c[2]), c[3], c[4] });
            }
            double total = cum[cum.Length - 1]; int iq = 0;
            for (double sq = 0; ; sq += routeStep)
            {
                if (sq > total) sq = total;
                while (iq + 1 < cum.Length - 1 && cum[iq + 1] < sq) iq++;
                int i1 = Math.Min(iq + 1, cum.Length - 1); double d = cum[i1] - cum[iq], f = d > 1e-9 ? Geom.Clamp((sq - cum[iq]) / d, 0, 1) : 0;
                var p = V2.Lerp(X_[iq], X_[i1], f);
                res.s.Add(sq); res.x.Add(p.x); res.z.Add(p.z); res.y.Add(H_[iq] + (H_[i1] - H_[iq]) * f);
                int nn = f < 0.5 ? iq : i1; res.lane.Add(K_[nn]); res.kind.Add(KD_[iq] == 1 || KD_[i1] == 1 ? 1 : KD_[nn]); res.step.Add(ST_[nn]);
                if (sq >= total) break;
            }
            res.junctions = jrec;
            SmoothRoute(res);
            res.ok = true;
            res.log.Add("경로 " + Math.Round(res.Length) + "m, 도로 " + m + "개, 교차로 " + jrec.Count + "곳(손 연결 " + jrec.Count(j => j.manual) + ", 새 곡선 " + jrec.Count(j => j.synthetic) + "), 차로 바꿈 " + res.changes.Count + "번");
            return res;
        }

        /// <summary>차로 중심선끼리 짧게 엇갈린 곳(일방 차로 가운데 놓기·짧은 교차로 연결)의 지그재그를 펴기: 제한속도에 맞춘 폭으로 가우스 평활(시속 80 → 약 9m, 30 → 약 4m),
        /// 높이는 2m 로 살짝. 처음·끝 점은 그대로. 거리(s)가 조금 줄어드니 교차로·차로 바꿈 s 도 새 거리로 옮김</summary>
        void SmoothRoute(RouteResult res)
        {
            int n = res.x.Count; if (n < 5) return;
            var spd = new double[n];
            for (int i = 0; i < n; i++)
            {
                var st = res.steps[Math.Max(0, Math.Min(res.steps.Count - 1, res.step[i]))];
                spd[i] = RouteSpd(st, st.SG(st.D * 0.5)) / 3.6;
            }
            foreach (var J in res.junctions)
            {   // 교차로 안: 좌·우회전이면 앞뒤 도로 중 느린 쪽의 0.6배, 직진이면 느린 쪽 그대로
                int a = Math.Max(0, (int)(J.s0 / routeStep) - 20), b = Math.Min(n - 1, (int)(J.s1 / routeStep) + 20); double m = double.MaxValue;
                for (int j = a; j <= b; j++) if (res.kind[j] != 1) m = Math.Min(m, spd[j]);
                if (m == double.MaxValue) continue;
                for (int j = (int)(J.s0 / routeStep); j <= Math.Min(n - 1, (int)(J.s1 / routeStep) + 1); j++) spd[j] = J.turn == 0 ? m : m * 0.6;
            }
            var sgA = new double[n]; for (int i = 0; i < n; i++) sgA[i] = Geom.Clamp(spd[i] * 0.6, 2.5, 12.0) / routeStep;
            {   // 평활 폭이 갑자기 바뀌면 점 순서가 뒤집힐 수 있음 → 폭도 앞뒤 30m 로 고르게(작은 쪽 먼저)
                int h = (int)(30 / routeStep); var mn = new double[n];
                for (int i = 0; i < n; i++) { double m = double.MaxValue; for (int j = Math.Max(0, i - h); j <= Math.Min(n - 1, i + h); j++) m = Math.Min(m, sgA[j]); mn[i] = m; }
                sgA = BoxSmooth(mn, h);
            }
            var nx = new double[n]; var nz = new double[n]; var ny = new double[n];
            for (int i = 0; i < n; i++)
            {
                double sg = sgA[i]; int w = (int)Math.Ceiling(3 * sg);
                double sx = 0, sz = 0, sw = 0, hy = 0, hw = 0;
                for (int j = Math.Max(0, i - w); j <= Math.Min(n - 1, i + w); j++)
                {
                    double d = j - i, wt = Math.Exp(-d * d / (2 * sg * sg)); sx += wt * res.x[j]; sz += wt * res.z[j]; sw += wt;
                    if (Math.Abs(d) <= 6 / routeStep) { double wy = Math.Exp(-d * d / (2 * (2 / routeStep) * (2 / routeStep))); hy += wy * res.y[j]; hw += wy; }
                }
                // 끝 가까이는 평활을 줄여 처음·끝 점을 지킴
                double edge = Math.Min(i, n - 1 - i) / Math.Max(1.0, 3 * sg), keep = 1 - Geom.Smooth(edge);
                nx[i] = sx / sw * (1 - keep) + res.x[i] * keep; nz[i] = sz / sw * (1 - keep) + res.z[i] * keep; ny[i] = hy / hw;
            }
            // 직진 교차로에서 옆으로 엇갈린 차로(jog): 그 둘레만 더 넓게 펴서 차로 바꾸듯 천천히 옮겨 감(꺾인 각이 아니라 평행 이동이라 길을 벗어나지 않음)
            foreach (var J in res.junctions)
            {
                if (J.turn != 0 || J.jog < 0.4 || J.jog > 7.0 || J.phi > 0.26) continue;   // 15도 넘게 꺾이거나 7m 넘게 엇갈린 곳(갈라지는 램프)은 빼고
                int c = (int)Math.Round(0.5 * (J.s0 + J.s1) / routeStep); if (c <= 0 || c >= n - 1) continue;
                double sJ = Geom.Clamp(0.6 * spd[c] + 4.0 * J.jog, 6.0, 28.0) / routeStep; int w = (int)Math.Ceiling(3 * sJ);
                var cx = (double[])nx.Clone(); var cz = (double[])nz.Clone();
                for (int i = Math.Max(1, c - w); i <= Math.Min(n - 2, c + w); i++)
                {
                    double sx = 0, sz = 0, sw = 0;
                    for (int j = Math.Max(0, i - w); j <= Math.Min(n - 1, i + w); j++) { double d = j - i, wt = Math.Exp(-d * d / (2 * sJ * sJ)); sx += wt * cx[j]; sz += wt * cz[j]; sw += wt; }
                    double bw = 1 - Geom.Smooth(Math.Abs(i - c) / (double)w);
                    nx[i] = cx[i] + (sx / sw - cx[i]) * bw; nz[i] = cz[i] + (sz / sw - cz[i]) * bw;
                }
            }
            var cum = new double[n]; for (int i = 1; i < n; i++) cum[i] = cum[i - 1] + Math.Sqrt((nx[i] - nx[i - 1]) * (nx[i] - nx[i - 1]) + (nz[i] - nz[i - 1]) * (nz[i] - nz[i - 1]));
            Func<double, double> remap = s0 => { double f = s0 / routeStep; int a = Math.Max(0, Math.Min(n - 1, (int)Math.Floor(f))), b = Math.Min(n - 1, a + 1); return cum[a] + (cum[b] - cum[a]) * (f - a); };
            foreach (var j in res.junctions) { j.s0 = remap(j.s0); j.s1 = remap(j.s1); }
            foreach (var c in res.changes) { c[0] = remap(c[0]); c[1] = remap(c[1]); }
            var lane = res.lane.ToArray(); var kind = res.kind.ToArray(); var step = res.step.ToArray();
            res.s.Clear(); res.x.Clear(); res.y.Clear(); res.z.Clear(); res.lane.Clear(); res.kind.Clear(); res.step.Clear();
            double total = cum[n - 1]; int iq = 0;
            for (double sq = 0; ; sq += routeStep)
            {
                if (sq > total) sq = total;
                while (iq + 1 < n - 1 && cum[iq + 1] < sq) iq++;
                int i1 = Math.Min(iq + 1, n - 1); double d = cum[i1] - cum[iq], f = d > 1e-9 ? Geom.Clamp((sq - cum[iq]) / d, 0, 1) : 0;
                res.s.Add(sq); res.x.Add(nx[iq] + (nx[i1] - nx[iq]) * f); res.z.Add(nz[iq] + (nz[i1] - nz[iq]) * f); res.y.Add(ny[iq] + (ny[i1] - ny[iq]) * f);
                int nn = f < 0.5 ? iq : i1; res.lane.Add(lane[nn]); res.kind.Add(kind[nn]); res.step.Add(step[nn]);
                if (sq >= total) break;
            }
        }

        LaneEnd[] LaneEndsForRoute(Arm A, bool inbound) { return LaneEnds(A, inbound); }

        static double[] BoxSmooth(double[] v, int half)
        {
            int n = v.Length; var o = new double[n]; var pre = new double[n + 1];
            for (int i = 0; i < n; i++) pre[i + 1] = pre[i] + v[i];
            for (int i = 0; i < n; i++) { int a = Math.Max(0, i - half), b = Math.Min(n - 1, i + half); o[i] = (pre[b + 1] - pre[a]) / (b - a + 1); }
            return o;
        }

        /// <summary>구간 처음(atStart) 또는 끝 점을 target 에 맞춤: 어긋남을 8m 에 걸쳐 줄여 나감</summary>
        static void FitEnds(List<V2> X, int i0, int i1, V2 target, bool atStart, RouteResult res, RouteStep st)
        {
            if (i1 <= i0) return;
            int e = atStart ? i0 : i1; V2 d = target - X[e]; if (d.Len < 1e-3) return;
            if (d.Len > 1.0) res.log.Add("차로 끝 어긋남 " + Math.Round(d.Len, 2) + "m: " + st.key + " " + st.road.name + (atStart ? " 처음" : " 끝"));
            double acc = 0; int k = e, step = atStart ? 1 : -1;
            while (k >= i0 && k <= i1)
            {
                double w = 1 - Geom.Smooth(acc / 8.0); if (w <= 0) break;
                X[k] = X[k] + d * w;
                int kn = k + step; if (kn < i0 || kn > i1) break;
                acc += (X[kn] - X[k]).Len; k = kn;
            }
        }

        /// <summary>구간 안 차로 바꿈 자리: 들어와서 일찍(a→c), 나가기 전 늦게(c→b). 터널·지하차도 실선 구간은 피함. 반환 [u0, u1, from, to]</summary>
        List<double[]> PlaceChanges(RouteStep st)
        {
            var o = new List<double[]>(); double D = st.D, Lc = st.Lch;
            double uS = st.armIn != null ? (st.armIn.crosswalk ? st.armIn.crosswalkWidth : 0) + 10 : 10;
            double uE = st.armOut != null ? D - ((st.armOut.stopDist > 0 ? st.armOut.stopDist + P.solidBeforeStop : (st.armOut.crosswalk ? st.armOut.crosswalkWidth : 0) + 10) + 5) : D - 15;
            int n1 = Math.Abs(st.c - st.a), n2 = Math.Abs(st.b - st.c);
            var plan = new List<double[]>();   // [희망 시작 u, from, to]
            if (uS + n1 * (Lc + 10) <= uE - n2 * (Lc + 10))
            {
                int k = st.a; for (int q = 0; q < n1; q++) { int nk = k + Math.Sign(st.c - st.a); plan.Add(new[] { uS + q * (Lc + 10), k, nk }); k = nk; }
                k = st.c; for (int q = 0; q < n2; q++) { int nk = k + Math.Sign(st.b - st.c); plan.Add(new[] { uE - (n2 - q) * (Lc + 10) + 10, k, nk }); k = nk; }
            }
            else
            {   // 짧은 구간: a → b 만 고르게
                int n = Math.Abs(st.b - st.a); if (n == 0) return o;
                double w0 = Math.Min(uS, D * 0.15), w1 = Math.Max(uE, D * 0.85); if (w1 - w0 < 4) { w0 = 0; w1 = D; }
                double len = Math.Min(Lc, (w1 - w0) / n); int k = st.a;
                for (int q = 0; q < n; q++) { int nk = k + Math.Sign(st.b - st.a); plan.Add(new[] { w0 + ((w1 - w0) - n * len) * 0.5 + q * len, k, nk, len }); k = nk; }
            }
            if (plan.Count == 0) return o;
            // 진로변경 제한(실선) 구간 피하기
            var runs = new List<double[]>();
            foreach (var r in NoChangeRuns(st.g)) { double u0 = (r[0] - st.sIn) * st.dir, u1 = (r[1] - st.sIn) * st.dir; runs.Add(new[] { Math.Min(u0, u1), Math.Max(u0, u1) }); }
            Func<double, double, bool> free = (a0, a1) => a0 >= 0 && a1 <= D && !runs.Any(r => a0 < r[1] && a1 > r[0]);
            double prevEnd = 0;
            foreach (var p in plan)
            {
                double len = p.Length > 3 ? p[3] : Lc, want = Math.Max(prevEnd, p[0]), pick = want; bool found = free(want, want + len);
                for (double dd = 1; !found && dd < D; dd += 1)
                {
                    if (want + dd + len <= D && free(want + dd, want + dd + len)) { pick = want + dd; found = true; break; }
                    if (want - dd >= prevEnd && free(want - dd, want - dd + len)) { pick = want - dd; found = true; break; }
                }
                pick = Geom.Clamp(pick, 0, Math.Max(0, D - len));
                o.Add(new[] { pick, pick + len, p[1], p[2] }); prevEnd = pick + len;
            }
            return o;
        }
    }
}
