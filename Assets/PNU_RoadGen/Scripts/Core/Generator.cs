// PNU 도로 생성기 — 형상(도로 면·교차로 면·인도·연석·높이)
//   도로 면   : 교차로 입구 ~ 입구. 끝 부분은 팔 직선에 맞춰 곧게 → 교차로 면과 딱 맞물림. 차로 수가 바뀌는 곳은 테이퍼
//   교차로 면 : 팔 입구 + 모서리 연석 곡선(아무 각도) → 다각형 하나(꼬이면 중심 부채꼴)
//   인도·연석 : 도로 양옆 + 교차로 모서리(곡선 따라)
//   높이      : 지형(DEM) 따라 다듬기, 교량·터널은 양 끝 직선, 고가·지하차도는 경사로, 교차로는 기울어진 평면 하나
using System;
using System.Collections.Generic;
using System.Linq;

namespace PNU.RoadGen
{
    /// <summary>도로 하나의 형상용 중심선과 단면 값(점마다)</summary>
    public class RoadGeom
    {
        public Road road; public bool ok = true; public string why = "";
        public List<V2> c; public double[] cum; public V2[] t; public double[] sOrig;   // sOrig: 원래 도로 s(차로 조각 조회용)
        public double[] wL, wR, swL, swR, h, sh; public string[] type;                   // 차도 반폭(왼/오른, 길어깨 포함), 인도 폭, 중심 높이, 길어깨 폭, 도로유형
        public int edgeType;                                                              // 끝선 종류(인도 있는 구간): GenParams.EdgeLine 번호
        public byte[] tmode;                                                              // 지형: 0 맞춤, 1 상판(밑 지형은 낮추기만), 2 덮개(터널·박스: 지형 그대로, 길은 땅속), 3 개착(U형 옹벽: 지형에 구멍)
        public bool[] under;
        public int[] layer;                                                               // OSM 층 참고값(점마다)
        public bool leveled;                                                              // 층 나누기로 높이를 바꾼 도로                                                              // 땅 밑으로 갈 구간(지하차도·터널 유형, 층 나누기로 내린 곳, 직접 내린 곳)
        public double Length { get { return cum[cum.Length - 1]; } }
    }

    /// <summary>교차로 모서리(팔 a 와 반시계로 다음 팔 b 사이)</summary>
    public class Corner
    {
        public Arm a, b; public int kind;            // 0 연석 곡선, 1 완만(직선·곡선 이음), 2 나란히 붙음(인도 없음)
        public double gap, R, tA, tB, reqA, reqB; public V2 F;        // reqA/B: 이 모서리 때문에 필요한 입구 거리(연석 곡선 + 인도 바깥선)
        public List<V2> path = new List<V2>();       // 연석선: a 입구 왼쪽 → b 입구 오른쪽 (교차로 면이 진행 방향 왼쪽)
        public double swA, swB;
        public bool hasCurb;                         // 연석이 있는 모서리(측구·가장자리선 띠를 둘레에)
        public bool whiteEdge = true;                // 모서리 둘레에 흰 가장자리선(두 길 다 흰 실선 끝선일 때)
        public List<List<V2>> rings = new List<List<V2>>();   // 연석에서 안쪽으로 [0, 측구, 가장자리선 바깥, 가장자리선 안쪽] 거리의 선들
    }

    public class JunctionGeom
    {
        public Junction j; public bool ok;
        public List<V2> fitP; public List<double> fitH, fitW; public int mainI = -1, mainJ = -1;     // 면 맞춤에 쓴 팔 끝(확인용)
        public List<Corner> corners = new List<Corner>();
        public List<V2> poly = new List<V2>(); public bool simple;
        public double h0, gx, gz;                    // 평면: h = h0 + gx*(x-cx) + gz*(z-cz)
        public double H(V2 p) { return h0 + gx * (p.x - j.center.x) + gz * (p.z - j.center.z); }
        // 차로 단위 연결(LaneJunction.cs)
        public Dictionary<Arm, LaneEnd[]> ins = new Dictionary<Arm, LaneEnd[]>(), outs = new Dictionary<Arm, LaneEnd[]>();
        public List<LaneLink> links = new List<LaneLink>();
        public List<List<V2>> sheet = new List<List<V2>>(), sheetHoles = new List<List<V2>>();   // 면 한 장: 바깥 둘레(반시계)·구멍
        public List<V2> sheetV; public List<int> sheetT;                                          // 삼각형(반시계 a,b,c)
        public double[] sheetBox;
        public bool manual;                                                                       // 손으로 이은 연결(편집기) 쓰는 교차로
    }

    public partial class RoadGenerator
    {
        public List<string> log = new List<string>();
        public readonly RoadNetwork net; public readonly GenParams P; public readonly HeightGrid dem;
        public readonly Dictionary<Road, RoadGeom> roadGeom = new Dictionary<Road, RoadGeom>();
        public readonly Dictionary<Junction, JunctionGeom> junctionGeom = new Dictionary<Junction, JunctionGeom>();
        public Overrides ov = new Overrides();

        public RoadGenerator(RoadNetwork net, GenParams P, HeightGrid dem) { this.net = net; this.P = P; this.dem = dem; }
        /// <summary>점 p 에 가장 가까운 도로 점의 노면 높이 / 거리</summary>
        public double RoadHAt(RoadGeom g, V2 p) { int kb = 0; double bd = 1e18; for (int k = 0; k < g.c.Count; k++) { double d = (g.c[k] - p).Len2; if (d < bd) { bd = d; kb = k; } } return g.h[kb]; }
        public double RoadDistAt(RoadGeom g, V2 p) { double bd = 1e18; for (int k = 0; k < g.c.Count; k++) bd = Math.Min(bd, (g.c[k] - p).Len2); return Math.Sqrt(bd); }

        /// <summary>차로 수 덮어쓰기(다른 것보다 먼저: 폭·입구 계산에 들어감)</summary>
        void ApplyLaneOverrides()
        {
            foreach (var R in net.roads)
            {
                var o = ov.Road(R); if (o == null) continue;
                if (o.lanesFwd > 0) foreach (var p in R.fwd) p.lanes = o.lanesFwd;
                if (o.lanesBwd > 0) foreach (var p in R.bwd) p.lanes = o.lanesBwd;
            }
        }
        public double Dem(V2 p) { return dem != null ? dem.At(p.x, p.z) : 0; }

        // =====================================================================
        // 준비: 입구 거리 ↔ 도로 폭을 몇 번 주고받으며 맞춤 → 높이 → 교차로 면
        // =====================================================================
        public void Prepare()
        {
            junctionGeom.Clear(); roadGeom.Clear();
            if (!net.gwStacked) { Gwangan.Stack(net, P); net.gwStacked = true; }       // 광안대교 아래층을 위층 밑으로(도로 모양 바꿈 — 한 번만)
            ApplyLaneOverrides();
            if (!edNet) ApplyRoadEdits();                                                  // 도로 편집기(정점·구간 차로 수) — 최종 도로망을 가져왔으면 이미 들어 있음
            foreach (var J in net.junctions) { junctionGeom[J] = new JunctionGeom { j = J }; foreach (var A in J.arms) A.cut = 0; }
            int it = 0;
            for (; it < 5; it++)
            {
                double moved = 0;
                foreach (var J in net.junctions) moved = Math.Max(moved, ComputeCuts(J));
                foreach (var R in net.roads) roadGeom[R] = BuildGeom(R, it == 4);
                foreach (var R in net.roads) SyncArms(R);
                if (it > 0 && moved < 0.05) break;
            }
            foreach (var R in net.roads) { var g = roadGeom[R]; if (!g.ok) log.Add("road " + R.id + " " + R.name + ": " + g.why); else Heights(g); }
            SeparateLevels();
            LimitEndGrades();
            FillDeckValleys();
            ClassifyUnderground();
            TrimFacingSidewalks();
            TrimMergingRoads();
            int fixedPolys = 0;
            foreach (var J in net.junctions)
            {
                var G = junctionGeom[J]; BuildCorners(G, 0); BuildPoly(G);
                // 꼬인 교차로 면(모서리 곡선이 서로 넘어감): 연석 곡선 대신 입구끼리 완만한 곡선으로 → 그래도 꼬이면 곧은 선으로 → 그래도 꼬이면 부채꼴
                for (int lv = 1; lv <= 2 && G.ok && !G.simple; lv++) { BuildCorners(G, lv); BuildPoly(G); if (G.simple) fixedPolys++; }
                if (G.ok && !G.simple && J.arms.Count >= 3)
                {   // 팔 방향 순서와 입구 자리 순서가 다름(도로가 교차로 중심을 비껴 들어옴): 입구 가운데 점의 둘레 순서로 다시 놓고 곧은 모서리로
                    V2 cm = new V2(J.arms.Average(a => a.Point(a.cut).x), J.arms.Average(a => a.Point(a.cut).z));
                    var order = J.arms.OrderBy(a => (a.Point(a.cut) - cm).Angle).ToList();
                    if (!order.SequenceEqual(J.arms))
                    {
                        var keep = new List<Arm>(J.arms); J.arms.Clear(); J.arms.AddRange(order);
                        BuildCorners(G, 2); BuildPoly(G);
                        if (G.simple) fixedPolys++;
                        else { J.arms.Clear(); J.arms.AddRange(keep); BuildCorners(G, 2); BuildPoly(G); }
                    }
                }
                FitPlane(G);
            }
            if (fixedPolys > 0) log.Add("junction polys untangled with gentle corners " + fixedPolys);
            foreach (var J in net.junctions) DecideCrosswalks(J);
            int bad = roadGeom.Values.Count(g => !g.ok), fan = junctionGeom.Values.Count(g => g.ok && !g.simple);
            log.Add("geom iterations " + Math.Min(it + 1, 5) + ", roads collapsed " + bad + ", junction polys not simple " + fan);
            if (P.laneJunctions)
            {   // 차로 단위 연결선 + 교차로 면 한 장(LaneJunction.cs)
                roadBoxes = null; sheetDone.Clear(); sheetBuilt = sheetFailed = sheetLinks = manualJunctions = laneLinkMiss = 0;
                foreach (var J in net.junctions) { var G = junctionGeom[J]; if (!G.ok) continue; BuildLaneLinks(G); BuildSheet(G); }
                log.Add("lane junctions: sheets " + sheetBuilt + " (failed " + sheetFailed + "), lane links " + sheetLinks + ", manual junctions " + manualJunctions);
            }
        }

        // ---------- 입구 거리 ----------
        double ComputeCuts(Junction J)
        {
            var arms = J.arms; int n = arms.Count;
            if (n > 0 && arms.All(a => !double.IsNaN(a.fixedCut)))
            {   // 도로 편집기 입구 그대로
                double mv = 0; foreach (var A in arms) { mv = Math.Max(mv, Math.Abs(A.fixedCut - A.cut)); A.cut = A.fixedCut; } return mv;
            }
            if (n <= 1) { foreach (var A in arms) A.cut = 0; return 0; }
            var req = new double[n];
            for (int i = 0; i < n; i++) req[i] = P.mouthMin;
            for (int i = 0; i < n; i++)
            {
                var C = MakeCorner(arms[i], arms[(i + 1) % n], n);
                if (C.kind != 0) continue;
                req[i] = Math.Max(req[i], C.reqA + P.cornerToMouth);
                req[(i + 1) % n] = Math.Max(req[(i + 1) % n], C.reqB + P.cornerToMouth);
            }
            // 나란히 붙은 팔(모서리 kind 2, 20° 안)끼리는 입구 거리를 같게(가장 먼 것) — 본선 옆에 붙어 나가는 램프 입구가 멀면
            // 본선 입구도 거기까지(교차로 면이 본선 도로 면 위로 겹쳐 깔리지 않게)
            if (P.equalizeSideBySideCuts)
                for (int pass = 0; pass < n; pass++)
                {
                    bool ch = false;
                    for (int i = 0; i < n; i++)
                    {
                        var C = MakeCorner(arms[i], arms[(i + 1) % n], n);
                        if (C.kind != 2 || C.gap * 180 / Math.PI > P.equalizeCutDeg) continue;     // 거의 겹친 팔(노드링크에서 같은 자리로 나가는 본선·램프)만
                        if (arms[i].road.oneWay == arms[(i + 1) % n].road.oneWay) continue;          // 양방향 본선 + 일방 램프 짝만(분리 도로 상·하행 짝은 그대로)
                        int j2 = (i + 1) % n; double mx = Math.Max(req[i], req[j2]);
                        // 짧은 도로는 안 늘림(입구가 멀어지면 도로가 없어짐): 길이가 새 입구 거리 + 30m 보다 길 때만
                        if (req[i] < mx - 1e-6 && arms[i].road.Length > mx + 30) { req[i] = mx; ch = true; }
                        if (req[j2] < mx - 1e-6 && arms[j2].road.Length > mx + 30) { req[j2] = mx; ch = true; }
                    }
                    if (!ch) break;
                }
            double moved = 0;
            for (int i = 0; i < n; i++)
            {
                double c = Math.Min(req[i], P.maxCut);
                // 짧은 도로: 양 끝 입구가 서로 넘어가 도로가 없어지지 않게 — 도로 길이(최소 shortRoadKeep 남김)를 양 끝이 나눠 가짐
                //   양 끝 입구 합이 (길이 − 남길 것)을 넘을 때만, 두 입구 비율대로 줄임
                var R = arms[i].road; var other = arms[i] == R.armStart ? R.armEnd : R.armStart;
                double oc = other != null && other != arms[i] ? other.cut : 0, room = R.Length - P.shortRoadKeep;
                if (c + oc > room) c = Math.Max(1.0, room * c / Math.Max(1e-6, c + oc));
                moved = Math.Max(moved, Math.Abs(c - arms[i].cut)); arms[i].cut = c;
            }
            return moved;
        }

        /// <summary>모서리 종류와 연석 곡선(반경 R, 중심 F, 접점 거리 tA·tB) — 두 팔의 차도 가장자리 직선에 접하는 원</summary>
        Corner MakeCorner(Arm A, Arm B, int n)
        {
            var C = new Corner { a = A, b = B };
            double gap = B.Angle - A.Angle;
            while (gap <= 0) gap += 2 * Math.PI;
            if (n == 1 || gap >= 2 * Math.PI - 1e-6) gap = 0;
            else if (n > 2 && gap > 2 * Math.PI - P.sideBySideDeg * Math.PI / 180) gap = 2 * Math.PI - gap;   // 옆 순서로 놓은 나란한 팔(방향각은 살짝 거꾸로)
            C.gap = gap; double deg = gap * 180 / Math.PI;
            if (deg < P.sideBySideDeg) { C.kind = 2; return C; }
            if (deg >= P.filletMaxDeg) { C.kind = 1; return C; }
            double R = P.CornerRadius(A.road.rank, B.road.rank);
            var jo = ov.Junction(A.j); if (jo != null && jo.cornerRadius > 0) R = jo.cornerRadius;
            if (deg < 70) R *= Math.Max(0.2, (deg - 10) / 60.0);          // 예각은 반경을 줄여 입구가 너무 멀어지지 않게
            V2 la = A.dir.Left, lb = B.dir.Left;
            for (int k = 0; k < 6 && R >= 0.75; k++, R *= 0.6)
            {
                V2 p = A.Point(0) + la * (A.wIn + R), q = B.Point(0) - lb * (B.wOut + R);
                double t, s;
                if (!Geom.LineLine(p, A.dir, q, B.dir, out t, out s)) break;
                if (t + P.cornerToMouth > P.maxCut || s + P.cornerToMouth > P.maxCut) continue;
                if (!double.IsNaN(A.fixedCut) && t > A.fixedCut - 0.2 || !double.IsNaN(B.fixedCut) && s > B.fixedCut - 0.2) continue;   // 편집기 입구 안에 들어오게 반경을 줄임
                C.kind = 0; C.R = R; C.F = p + A.dir * t; C.tA = t; C.tB = s; C.reqA = t; C.reqB = s;
                // 예각(갈라지는 길)은 사이 섬이 좁아 양쪽 인도가 겹침 → 인도 바깥선끼리 만나는 곳까지 입구를 밀어냄
                if (A.swIn > 0 || B.swOut > 0)
                {
                    V2 p2 = A.Point(0) + la * (A.wIn + A.swIn + 0.3), q2 = B.Point(0) - lb * (B.wOut + B.swOut + 0.3);
                    double t2, s2;
                    if (Geom.LineLine(p2, A.dir, q2, B.dir, out t2, out s2) && t2 > 0 && s2 > 0) { C.reqA = Math.Max(C.reqA, t2); C.reqB = Math.Max(C.reqB, s2); }
                }
                return C;
            }
            C.kind = 1; return C;
        }

        // ---------- 도로 형상 중심선 ----------
        RoadGeom BuildGeom(Road R, bool last)
        {
            var g = new RoadGeom { road = R };
            var pts = new List<V2>(R.center); var so = new List<double>(R.cum);
            double L = R.Length;
            bool hs = R.armStart != null && R.armStart.cut > 0, he = R.armEnd != null && R.armEnd.cut > 0;
            if (R.ed != null)
            {   // 도로 편집기 도로: 입구(편집기 자리) 사이를 그대로 — 곧게 펴기·입구 다시 잡기 없음
                hs = R.armStart != null; he = R.armEnd != null;
                double m0 = Math.Max(0, R.ed.m0), m1 = Math.Min(L, R.ed.m1);
                pts = new List<V2> { Geom.At(R.center, R.cum, m0) }; so = new List<double> { m0 };
                for (int i = 0; i < R.center.Count; i++) if (R.cum[i] > m0 + 0.05 && R.cum[i] < m1 - 0.05) { pts.Add(R.center[i]); so.Add(R.cum[i]); }
                pts.Add(Geom.At(R.center, R.cum, m1)); so.Add(m1);
                if (m1 - m0 < 1.0) { g.ok = false; g.why = "editor mouths too close (" + (m1 - m0).ToString("0.0") + "m)"; }
            }
            else
            {
                if (hs) Straighten(pts, so, L, R.armStart, true);
                if (he) Straighten(pts, so, L, R.armEnd, false);
                if (hs && !TrimToMouth(pts, so, R.armStart)) { g.ok = false; g.why = "start mouth beyond road (L=" + L.ToString("0") + ")"; }
                if (g.ok && he)
                {
                    pts.Reverse(); so.Reverse();
                    if (!TrimToMouth(pts, so, R.armEnd)) { g.ok = false; g.why = "end mouth beyond road (L=" + L.ToString("0") + ")"; }
                    pts.Reverse(); so.Reverse();
                }
            }
            if (g.ok && Geom.Length(pts) < 1.0) { g.ok = false; g.why = "too short between mouths"; }
            if (!g.ok) return g;

            ResampleWith(pts, so, P.step, out g.c, out g.sOrig);
            g.cum = Geom.CumLen(g.c); int n = g.c.Count;

            // 단면 폭: 차로 수 × 차로 폭 + 중앙(황색 복선 or 분리대) + 갓길, 인도는 구조물(고가·지하차도·터널)·연결로·고속국도 없음
            g.wL = new double[n]; g.wR = new double[n]; g.swL = new double[n]; g.swR = new double[n]; g.type = new string[n]; g.sh = new double[n];
            var ratio = new double[n]; var lwA = new double[n];
            for (int i = 0; i < n; i++)
            {   // 길어깨(제12조: 설계속도별)는 속도가 바뀌는 곳에서 튀지 않게 먼저 모아 부드럽게
                var pc = PieceAt(R, g.sOrig[i]);
                string rank = pc != null && !string.IsNullOrEmpty(pc.rank) ? pc.rank : R.rank;
                int spd = pc != null && pc.spd > 0 ? pc.spd : 50;
                lwA[i] = P.LaneWidth(rank, spd); g.sh[i] = P.Shoulder(rank, spd);
            }
            g.sh = Geom.SmoothVals(g.sh, 4.0 / P.step);
            for (int i = 0; i < n; i++)
            {
                double s = g.sOrig[i];
                int lf = R.LanesAt(R.fwd, s), lb = R.LanesAt(R.bwd, s);
                var pc = PieceAt(R, s);
                string rank = pc != null && !string.IsNullOrEmpty(pc.rank) ? pc.rank : R.rank;
                int spd = pc != null && pc.spd > 0 ? pc.spd : 50;
                double lw = lwA[i], sh = g.sh[i];
                if (R.oneWay) { int m = Math.Max(1, Math.Max(lf, lb)); g.wL[i] = g.wR[i] = m * lw * 0.5 + sh; }
                else
                {
                    double half = R.divided ? P.medianHalf : P.centerHalfGap;
                    g.wR[i] = half + Math.Max(1, lf) * lw + sh;
                    g.wL[i] = half + Math.Max(1, lb) * lw + sh;
                }
                g.type[i] = TypeAt(R, s);
                bool noWalk = g.type[i] == "001" || g.type[i] == "002" || g.type[i] == "004";
                double sw = noWalk ? 0 : P.SidewalkWidth(rank, pc != null ? pc.connect : "0");
                g.swL[i] = g.swR[i] = sw;
                ratio[i] = spd >= 70 || P.IsExpress(rank) ? P.taperExpress : P.taperUrban;
            }
            Dilate(g.wL, g.cum, ratio); Dilate(g.wR, g.cum, ratio);
            if (R.ed != null && R.ed.s != null && R.ed.s.Length > 0)
                for (int i = 0; i < n; i++) { g.wL[i] = Math.Max(0.5, Geom.AtVal(R.ed.wl, R.ed.s, g.sOrig[i])); g.wR[i] = Math.Max(0.5, Geom.AtVal(R.ed.wr, R.ed.s, g.sOrig[i])); }   // 편집기 가장자리(테이퍼·한쪽 넓힘 그대로)
            var ro = ov.Road(R);
            g.edgeType = ro != null && ro.edgeLine >= 0 ? ro.edgeLine : P.EdgeLine(R.rank);
            if (ro != null)
                for (int i = 0; i < n; i++)
                {
                    if (ro.sidewalkLeft == 0) g.swL[i] = 0; else if (ro.sidewalkLeft == 1 && g.swL[i] <= 0) g.swL[i] = P.sidewalkLocal;
                    if (ro.sidewalkRight == 0) g.swR[i] = 0; else if (ro.sidewalkRight == 1 && g.swR[i] <= 0) g.swR[i] = P.sidewalkLocal;
                }

            // 급곡선 완화: 안쪽 단면(차도+인도)이 접히지 않게 최소 반경 확보. 교차로 쪽 곧은 구간은 고정
            var rMin = new double[n];
            for (int i = 0; i < n; i++) rMin[i] = Math.Max(g.wL[i] + g.swL[i], g.wR[i] + g.swR[i]) + 1.0;
            int fixA = hs ? (int)Math.Ceiling(10 / P.step) : 1, fixB = hs || he ? (he ? (int)Math.Ceiling(10 / P.step) : 1) : 1;
            if (RelaxCurvature(g.c, rMin, fixA, fixB, 300))
            {   // 점 간격이 흐트러졌으니 다시 고르게(단면 값도 같이)
                var c2 = g.c; var s2 = g.sOrig.ToList(); var lists = new[] { g.wL, g.wR, g.swL, g.swR, g.sh };
                var cumOld = Geom.CumLen(c2);
                ResampleWith(c2, s2, P.step, out g.c, out g.sOrig);
                g.cum = Geom.CumLen(g.c); int n2 = g.c.Count;
                var outs = new double[5][];
                for (int k = 0; k < 5; k++) { outs[k] = new double[n2]; for (int i = 0; i < n2; i++) outs[k][i] = Geom.AtVal(lists[k], cumOld, g.cum[i] * cumOld[cumOld.Length - 1] / g.cum[n2 - 1]); }
                var ty2 = new string[n2];
                for (int i = 0; i < n2; i++) { int j = Math.Min(n - 1, (int)Math.Round(g.cum[i] / g.cum[n2 - 1] * (n - 1))); ty2[i] = g.type[j]; }
                g.wL = outs[0]; g.wR = outs[1]; g.swL = outs[2]; g.swR = outs[3]; g.sh = outs[4]; g.type = ty2; n = n2;
            }
            g.t = Geom.Tangents(g.c);
            if (hs) g.t[0] = R.armStart.dir;
            if (he) g.t[n - 1] = -R.armEnd.dir;
            return g;
        }

        /// <summary>반경이 rMin 보다 작은 점을 이웃 가운데 쪽으로 조금씩 당김(라플라스 다듬기, 해당 점만). 고정: 앞 fixA, 뒤 fixB 점</summary>
        static bool RelaxCurvature(List<V2> p, double[] rMin, int fixA, int fixB, int maxIt)
        {
            int n = p.Count; bool changed = false;
            for (int it = 0; it < maxIt; it++)
            {
                var q = new List<V2>(p); bool any = false;
                for (int i = Math.Max(1, fixA); i < n - Math.Max(1, fixB); i++)
                {
                    V2 a = p[i - 1], b = p[i], c = p[i + 1];
                    double l1 = (b - a).Len, l2 = (c - b).Len; if (l1 < 1e-6 || l2 < 1e-6) continue;
                    double ang = Math.Acos(Geom.Clamp(V2.Dot((b - a) / l1, (c - b) / l2), -1, 1));
                    if (ang < 1e-6 || 0.5 * (l1 + l2) / ang >= rMin[i]) continue;
                    any = true; q[i] = b + ((a + c) * 0.5 - b) * 0.5;
                }
                for (int i = 0; i < n; i++) p[i] = q[i];
                if (!any) break; changed = true;
            }
            return changed;
        }

        /// <summary>교차로 쪽 끝을 팔 직선 위로 옮김(입구 근처는 완전히, 그 뒤로 부드럽게 원래 선으로)</summary>
        static void Straighten(List<V2> pts, List<double> so, double L, Arm A, bool atStart)
        {
            double sz = Math.Min(Math.Max(30, A.cut + 12), L * 0.45), sb = Math.Min(sz + 15, L * 0.5);
            V2 lf = A.dir.Left;
            for (int i = 0; i < pts.Count; i++)
            {
                double d = atStart ? so[i] : L - so[i];
                if (d >= sb) continue;
                double w = d <= sz ? 1 : 1 - Geom.Smooth((d - sz) / Math.Max(1e-6, sb - sz));
                double off = V2.Dot(pts[i] - A.basePt, lf);
                pts[i] = pts[i] - lf * (off * w);
            }
        }

        /// <summary>목록 앞쪽을 입구(교차로 중심에서 팔 방향 cut)에서 시작하게 자르거나 팔 직선으로 늘림</summary>
        static bool TrimToMouth(List<V2> pts, List<double> so, Arm A)
        {
            V2 c = A.j.center; double cut = A.cut;
            int k = -1;
            for (int i = 0; i < pts.Count; i++) if (V2.Dot(pts[i] - c, A.dir) >= cut) { k = i; break; }
            if (k < 0) return false;
            V2 M = A.Point(cut);
            if (k == 0)
            {
                if (V2.Dot(pts[0] - c, A.dir) - cut > 0.05) { pts.Insert(0, M); so.Insert(0, so[0]); }
                else pts[0] = M;
                return true;
            }
            double d0 = V2.Dot(pts[k - 1] - c, A.dir), d1 = V2.Dot(pts[k] - c, A.dir), f = (cut - d0) / Math.Max(1e-9, d1 - d0);
            double sM = so[k - 1] + (so[k] - so[k - 1]) * f;
            pts.RemoveRange(0, k); so.RemoveRange(0, k);
            if (V2.Dot(pts[0] - c, A.dir) - cut > 0.05) { pts.Insert(0, M); so.Insert(0, sM); } else { pts[0] = M; so[0] = sM; }
            return true;
        }

        static void ResampleWith(List<V2> p, List<double> val, double step, out List<V2> op, out double[] ov)
        {
            var cum = Geom.CumLen(p); double L = cum[cum.Length - 1];
            int n = Math.Max(1, (int)Math.Ceiling(L / step));
            op = new List<V2>(n + 1); ov = new double[n + 1];
            var va = val.ToArray();
            for (int i = 0; i <= n; i++) { double s = L * i / n; op.Add(Geom.At(p, cum, s)); ov[i] = Geom.AtVal(va, cum, s); }
        }

        /// <summary>넓은 쪽이 좁은 구간 안으로 기울기 1/ratio 로 줄어들게(차로가 있는 곳은 항상 그 폭 이상)</summary>
        static void Dilate(double[] w, double[] cum, double[] ratio)
        {
            int n = w.Length;
            for (int i = 1; i < n; i++) w[i] = Math.Max(w[i], w[i - 1] - (cum[i] - cum[i - 1]) / ratio[i]);
            for (int i = n - 2; i >= 0; i--) w[i] = Math.Max(w[i], w[i + 1] - (cum[i + 1] - cum[i]) / ratio[i]);
        }

        static LanePiece PieceAt(Road R, double s)
        {
            foreach (var p in R.fwd) if (s >= p.s0 - 1e-6 && s <= p.s1 + 1e-6) return p;
            foreach (var p in R.bwd) if (s >= p.s0 - 1e-6 && s <= p.s1 + 1e-6) return p;
            if (R.fwd.Count > 0) return s < R.fwd[0].s0 ? R.fwd[0] : R.fwd[R.fwd.Count - 1];
            if (R.bwd.Count > 0) return s < R.bwd[0].s0 ? R.bwd[0] : R.bwd[R.bwd.Count - 1];
            return null;
        }
        static int LayerAt(Road R, double s)
        {
            int lv = 0;
            foreach (var p in R.fwd) if (s >= p.s0 - 1e-6 && s <= p.s1 + 1e-6) { lv = p.layer; break; }
            if (lv == 0) foreach (var p in R.bwd) if (s >= p.s0 - 1e-6 && s <= p.s1 + 1e-6) { lv = p.layer; break; }
            return lv;
        }
        static string TypeAt(Road R, double s)
        {
            string ty = null;
            foreach (var p in R.fwd) if (s >= p.s0 - 1e-6 && s <= p.s1 + 1e-6) { ty = p.type; break; }
            if (ty == null || ty == "000")
                foreach (var p in R.bwd) if (s >= p.s0 - 1e-6 && s <= p.s1 + 1e-6) { if (!string.IsNullOrEmpty(p.type) && p.type != "000") ty = p.type; break; }
            return string.IsNullOrEmpty(ty) ? "000" : ty;
        }

        // ---------- 층 나누기 ----------
        //   같은 자리를 지나는데 구조가 다른 도로(다리·고가 ↔ 땅, 땅 ↔ 지하차도·터널)가 비슷한 높이면 서로 뚫고 나옴 →
        //   위에 갈 길(다리·고가)은 겹친 곳에서 밑 길 + levelClear 까지 올리고, 지하로 갈 길은 밑으로 내림. 앞뒤는 rampGrade 경사로,
        //   양 끝(교차로 입구)은 원래 높이 그대로(교차로 면과 이어짐 — 끝 가까이는 어쩔 수 없이 비슷한 높이)
        static int LevelClass(string t) { return t == "001" || t == "003" ? 1 : t == "002" || t == "004" ? -1 : 0; }
        /// <summary>점의 층 종류: 노드링크 유형이 일반이면 OSM 층(다리 +1, 지하 −1)으로</summary>
        static int ClassAt(RoadGeom g, int i)
        {
            int c = LevelClass(g.type[i]);
            if (c == 0 && g.layer != null && g.layer[i] != 0) c = g.layer[i] > 0 ? 1 : -1;
            return c;
        }
        /// <summary>점 i 에서 높이를 바꿀 수 있는 최대(교차로에 닿은 끝에서 rampGrade 경사로 — 막다른 끝은 제한 없음)</summary>
        double EndCap(RoadGeom g, int i)
        {
            double L = g.Length, a = g.road.armStart != null ? g.cum[i] : 1e9, b = g.road.armEnd != null ? L - g.cum[i] : 1e9;
            return P.rampGrade * Math.Min(a, b);
        }
        void SeparateLevels()
        {
            if (!P.separateLevels) return;
            var gs = roadGeom.Values.Where(g => g.ok && g.h != null).ToList();
            // 도시고속(101·102) 점: 같은 구조(둘 다 땅)인데 일반 길과 겹치면 고가로 봄(노드링크에 고가 표시가 빠진 곳이 많음)
            var ex = gs.Select(g => g.sOrig.Select(so => { var pc = PieceAt(g.road, so); string rk = pc != null && !string.IsNullOrEmpty(pc.rank) ? pc.rank : g.road.rank; return rk == "101" || rk == "102"; }).ToArray()).ToList();
            const double cell = 2.0;
            int lifted = 0, lowered = 0, nStacked = 0;
            for (int pass = 0; pass < 3; pass++)
            {
                var grid = new Dictionary<long, List<long>>();
                for (int gi = 0; gi < gs.Count; gi++)
                {
                    var g = gs[gi]; int n = g.c.Count;
                    for (int i = 0; i < n; i++)
                    {
                        V2 lf = g.t[i].Left; double a = -(g.wR[i] + g.swR[i]), b = g.wL[i] + g.swL[i];
                        var seen = new HashSet<long>();
                        for (double u = a; u <= b + 1e-6; u += 1.0)
                        {
                            V2 q = g.c[i] + lf * Math.Min(u, b);
                            long key = ((long)Math.Floor(q.x / cell) << 32) ^ ((long)Math.Floor(q.z / cell) & 0xffffffffL);
                            if (!seen.Add(key)) continue;
                            List<long> l; if (!grid.TryGetValue(key, out l)) { l = new List<long>(); grid[key] = l; }
                            l.Add(((long)gi << 24) | (long)i);
                        }
                    }
                }
                var need = new Dictionary<int, double[]>();      // 도로 → 점마다 필요한 높이 바꿈(+ 올림, − 내림)
                foreach (var l in grid.Values)
                {
                    if (l.Count < 2) continue;
                    for (int x = 0; x < l.Count; x++)
                        for (int y = x + 1; y < l.Count; y++)
                        {
                            int ga = (int)(l[x] >> 24), gb = (int)(l[y] >> 24); if (ga == gb) continue;
                            int ia = (int)(l[x] & 0xffffff), ib = (int)(l[y] & 0xffffff);
                            RoadGeom A = gs[ga], B = gs[gb];
                            {   // 나란히 옆에 붙은 길(가장자리만 살짝 겹침)은 층을 나누지 않음 — 위아래로 겹친 길(복층 다리)·가로지르는 길만
                                double dt = Math.Abs(V2.Dot(A.t[ia], B.t[ib]));
                                if (dt > 0.9)
                                {
                                    double lat = Math.Abs(V2.Dot(B.c[ib] - A.c[ia], A.t[ia].Left));
                                    double wa = Math.Max(A.wL[ia] + A.swL[ia], A.wR[ia] + A.swR[ia]), wb = Math.Max(B.wL[ib] + B.swL[ib], B.wR[ib] + B.swR[ib]);
                                    if (lat > 0.55 * (wa + wb)) continue;
                                    // 거의 나란한(10° 안) 지면 길 둘(노드링크 일반·OSM 층 같음)이 많이 겹침: 노드링크 자리가 가까운 것뿐(광안대로·수영강변대로 묶음 등) —
                                    // 층을 나누면 7m 혹·지하 굴이 생김. 같은 높이로 두고 겹친 면은 차도 자르기(trimMergingRoads)가 맡음
                                    if (P.parallelAtGradeNoSplit && dt > 0.985 && lat > 0.2 * (wa + wb) && ClassAt(A, ia) == 0 && ClassAt(B, ib) == 0
                                        && (A.layer != null ? A.layer[ia] : 0) == (B.layer != null ? B.layer[ib] : 0)) continue;
                                }
                            }
                            int ca = ClassAt(A, ia), cb = ClassAt(B, ib);
                            bool stacked = false;
                            if (ca == cb)
                            {
                                int la = A.layer != null ? A.layer[ia] : 0, lb = B.layer != null ? B.layer[ib] : 0;
                                if (la != lb && ca != 0)
                                {   // 같은 구조끼리 OSM 층이 다름(복층 다리·겹친 고가): 층 높은 쪽이 위
                                    stacked = ca == 1; nStacked++;
                                    if (ca == 1) { if (la > lb) { ca = 1; cb = 0; } else { ca = 0; cb = 1; } }
                                    else { if (la > lb) { ca = 0; cb = -1; } else { ca = -1; cb = 0; } }
                                }
                                else
                                {
                                    if (!P.liftExpressways || ca != 0 || ex[ga][ia] == ex[gb][ib]) continue;
                                    if (ex[ga][ia]) { ca = 1; cb = 0; } else { ca = 0; cb = 1; }
                                }
                            }
                            if (ca < cb) { var t = A; A = B; B = t; int ti = ia; ia = ib; ib = ti; int tg = ga; ga = gb; gb = tg; int tc = ca; ca = cb; cb = tc; }   // A 가 위
                            double clear = stacked ? P.deckGap : P.levelClear;
                            double dh = A.h[ia] - B.h[ib]; if (dh >= clear) continue;
                            double want = clear - dh;
                            // 옮길 길이 교차로 가까이라(양 끝은 원래 높이) 다 못 옮기면 나머지는 다른 길이 맡음(터널 입구 바로 위를 지나는 길은 살짝 올림 등)
                            if (ca == 1)
                            {   // 다리·고가를 올림
                                double[] nd; if (!need.TryGetValue(ga, out nd)) { nd = new double[A.c.Count]; need[ga] = nd; }
                                nd[ia] = Math.Max(nd[ia], want);
                                double rest = want - EndCap(A, ia);
                                if (rest > 0.3 && cb == 0 && !stacked && B.tmode[ib] != 1) { double[] nb; if (!need.TryGetValue(gb, out nb)) { nb = new double[B.c.Count]; need[gb] = nb; } nb[ib] = Math.Min(nb[ib], -rest); }
                            }
                            else
                            {   // 땅 위(A) ↔ 지하(B): 지하를 내림
                                double[] nd; if (!need.TryGetValue(gb, out nd)) { nd = new double[B.c.Count]; need[gb] = nd; }
                                nd[ib] = Math.Min(nd[ib], -want);
                                double rest = want - EndCap(B, ib);
                                if (rest > 0.3 && ca == 0 && !stacked) { double[] na; if (!need.TryGetValue(ga, out na)) { na = new double[A.c.Count]; need[ga] = na; } na[ia] = Math.Max(na[ia], rest); }
                            }
                        }
                }
                // 교차로(땅): 중심에서 가장 먼 입구까지 원판, 높이 = 팔 끝 도로 높이 평균 — 구조 도로(위·아래)가 그 위·밑을 지나면 떼어 놓음
                foreach (var J in net.junctions)
                {
                    if (J.arms.Count < 2) continue;
                    double rad = 0, hs = 0; int hn = 0;
                    foreach (var a in J.arms)
                    {
                        rad = Math.Max(rad, a.cut + Math.Max(a.wIn, a.wOut));
                        RoadGeom ag; if (roadGeom.TryGetValue(a.road, out ag) && ag.ok && ag.h != null) { hs += a.atStart ? ag.h[0] : ag.h[ag.h.Length - 1]; hn++; }
                    }
                    if (hn == 0) continue; double hj = hs / hn;
                    var near = new HashSet<long>();
                    for (double dx = -rad; dx <= rad; dx += cell)
                        for (double dz = -rad; dz <= rad; dz += cell)
                        {
                            if (dx * dx + dz * dz > rad * rad) continue;
                            V2 q = J.center + new V2(dx, dz);
                            long key = ((long)Math.Floor(q.x / cell) << 32) ^ ((long)Math.Floor(q.z / cell) & 0xffffffffL);
                            if (!near.Add(key)) continue;
                            List<long> l; if (!grid.TryGetValue(key, out l)) continue;
                            foreach (var e in l)
                            {
                                int gi = (int)(e >> 24), i = (int)(e & 0xffffff); var g = gs[gi];
                                if (g.road.jStart == J || g.road.jEnd == J) continue;          // 이 교차로에 닿는 길은 빼고
                                int c = ClassAt(g, i); if (c == 0) continue;
                                double dh = g.h[i] - hj;
                                double[] nd; if (!need.TryGetValue(gi, out nd)) { nd = new double[g.c.Count]; need[gi] = nd; }
                                if (c > 0 && dh < P.levelClear) nd[i] = Math.Max(nd[i], P.levelClear - dh);
                                if (c < 0 && dh > -P.levelClear) nd[i] = Math.Min(nd[i], -(P.levelClear + dh));
                            }
                        }
                }
                if (need.Count == 0) break;
                foreach (var kv in need)
                {
                    var g = gs[kv.Key]; var nd = kv.Value; int n = g.c.Count; double L = g.Length;
                    if (g.road.hFixS != null) continue;                                                            // 사람이 정한 높이
                    bool up = nd.Any(v => v > 0); var m = nd.Select(v => up ? Math.Max(0, v) : Math.Max(0, -v)).ToArray();
                    for (int i = 1; i < n; i++) m[i] = Math.Max(m[i], m[i - 1] - P.rampGrade * (g.cum[i] - g.cum[i - 1]));       // 앞뒤 경사로
                    for (int i = n - 2; i >= 0; i--) m[i] = Math.Max(m[i], m[i + 1] - P.rampGrade * (g.cum[i + 1] - g.cum[i]));
                    for (int i = 0; i < n; i++)
                    {
                        double d = Math.Min(m[i], EndCap(g, i));                                                   // 교차로에 닿은 끝은 원래 높이
                        if (d <= 1e-3) continue;
                        g.h[i] += up ? d : -d; g.leveled = true;
                        if (up && d > P.elevThreshold) g.tmode[i] = 1;
                        if (!up && d > 1.0) g.under[i] = true;
                    }
                    if (up) lifted++; else lowered++;
                }
            }
            if (lifted + lowered > 0) log.Add("levels separated: lifted " + lifted + ", lowered " + lowered + ", stacked cell pairs " + nStacked);
        }

        // ---------- 교차로 쪽 끝 경사 제한 ----------
        //   층 나누기로 올리고 내린 뒤에도 교차로에 닿는 끝에서 maxEndGrade 보다 가파르게 오르내리지 않게(끝 높이에서 경사 상·하한)
        void LimitEndGrades()
        {
            int n0 = 0;
            foreach (var g in roadGeom.Values)
            {
                if (!g.ok || g.h == null || !g.leveled) continue; int n = g.c.Count; double L = g.Length;   // 층 나누기로 바뀐 길만(원래 가파른 언덕길은 그대로)
                double hs = g.h[0], he = g.h[n - 1]; bool a = g.road.armStart != null, b = g.road.armEnd != null;
                for (int i = 0; i < n; i++)
                {
                    double v = g.h[i];
                    if (a) v = Math.Max(hs - P.maxEndGrade * g.cum[i], Math.Min(hs + P.maxEndGrade * g.cum[i], v));
                    if (b) v = Math.Max(he - P.maxEndGrade * (L - g.cum[i]), Math.Min(he + P.maxEndGrade * (L - g.cum[i]), v));
                    if (Math.Abs(v - g.h[i]) > 0.01) { g.h[i] = v; n0++; if (g.tmode[i] == 1 && v - Dem(g.c[i]) < P.elevThreshold) g.tmode[i] = 0; }
                }
            }
            if (n0 > 0) log.Add("end grades limited at " + n0 + " stations");
        }

        // ---------- 상판 골 메우기 ----------
        //   고가·다리 위에서 건너는 길 때문에 군데군데 올린 곳 사이가 다시 내려앉아 출렁이지 않게: 상판 구간마다 양쪽 높은 곳에서
        //   deckValleyGrade 경사로 내려오는 선보다 낮은 골은 그 선까지 메움(위로만, 끝 높이는 그대로)
        void FillDeckValleys()
        {
            int nf = 0;
            foreach (var g in roadGeom.Values)
            {
                if (!g.ok || g.h == null || g.road.hFixS != null) continue; int n = g.c.Count;
                for (int i = 0; i < n; )
                {
                    if (g.tmode[i] != 1) { i++; continue; }
                    int j = i; while (j < n && g.tmode[j] == 1) j++;
                    int m = j - i; if (m >= 3)
                    {
                        var L = new double[m]; var R = new double[m];
                        for (int k = 0; k < m; k++) L[k] = k == 0 ? g.h[i] : Math.Max(g.h[i + k], L[k - 1] - P.deckValleyGrade * (g.cum[i + k] - g.cum[i + k - 1]));
                        for (int k = m - 1; k >= 0; k--) R[k] = k == m - 1 ? g.h[j - 1] : Math.Max(g.h[i + k], R[k + 1] - P.deckValleyGrade * (g.cum[i + k + 1] - g.cum[i + k]));
                        for (int k = 1; k < m - 1; k++) { double v = Math.Min(L[k], R[k]); if (v > g.h[i + k] + 0.05) { g.h[i + k] = v; nf++; } }
                    }
                    i = j;
                }
            }
            if (nf > 0) log.Add("deck valleys filled at " + nf + " stations");
        }

        // ---------- 땅 밑 구간 나누기 ----------
        //   지하차도·터널(과 내린 길)은 원래 땅(DEM)보다 얼마나 깊은지로: 구조물 위로 흙이 덮일 만큼 깊으면 덮개(2: 터널·박스, 지형 그대로),
        //   그보다 얕으면 개착(3: U형 옹벽, 지형에 구멍), 아주 얕으면 보통 길(0: 지형 맞춤). 짧게 끊기는 구간은 앞뒤에 붙임. 땅 밑 구간은 인도 없음
        /// <summary>덮개 구조물 윗면 높이(도로 중심 기준): 터널(004)은 말굽 아치 꼭대기, 그 밖(박스)은 평 지붕 + 두께</summary>
        public double TubeTop(RoadGeom g, int i)
        {
            if (g.type[i] == "004") { double W = g.wL[i] + g.wR[i] + 2 * P.tubeMargin; return P.archSpring + W * 0.5 + P.tubeShell; }
            return P.boxHeight + P.tubeShell;
        }
        void ClassifyUnderground()
        {
            int nCov = 0, nCut = 0;
            foreach (var g in roadGeom.Values)
            {
                if (!g.ok || g.h == null || g.under == null) continue;
                int n = g.c.Count; if (!g.under.Any(u => u)) continue;
                for (int i = 0; i < n; i++)
                {
                    if (g.road.tmS != null) continue;                                       // 편집기가 판정한 구조는 그대로
                    if (!g.under[i] || (g.tmode[i] == 1 && !(g.type[i] == "002" || g.type[i] == "004"))) continue;
                    V2 lf = g.t[i].Left; double hc = Dem(g.c[i]), hl = Dem(g.c[i] + lf * g.wL[i]), hr = Dem(g.c[i] - lf * g.wR[i]);
                    double dmin = Math.Min(hc, Math.Min(hl, hr)) - g.h[i], dmax = Math.Max(hc, Math.Max(hl, hr)) - g.h[i];
                    // 땅 밑이어야 할 길이 오히려 땅보다 한참 높음(양 끝 직선이 골짜기를 건넘 — 입구 높이를 DEM 산비탈에서 읽은 탓): 흙 둑 대신 고가로
                    g.tmode[i] = (byte)(-dmax > P.elevThreshold ? 1 : dmin >= TubeTop(g, i) + P.coverMin ? 2 : dmax >= P.cutMinDepth ? 3 : 0);
                }
            }
            if (P.twinTubes) SyncTwinCover();
            foreach (var g in roadGeom.Values)
            {
                if (!g.ok || g.h == null || g.under == null) continue;
                int n = g.c.Count; if (!g.under.Any(u => u)) continue;
                // 짧은 개착(덮개 사이) → 덮개, 짧은 덮개 → 개착
                Runs(g.tmode, 3, (int)Math.Ceiling(P.cutMinLen / P.step), 2, true);
                Runs(g.tmode, 2, (int)Math.Ceiling(P.coverMinLen / P.step), 3, false);
                for (int i = 0; i < n; i++)
                {
                    if (g.tmode[i] == 2 || g.tmode[i] == 3) { g.swL[i] = 0; g.swR[i] = 0; }
                    if (g.tmode[i] == 2) nCov++; else if (g.tmode[i] == 3) nCut++;
                }
            }
            foreach (var R in net.roads) SyncArms(R);
            if (nCov + nCut > 0) log.Add("underground stations: covered " + nCov + ", open cut " + nCut);
        }
        /// <summary>쌍둥이 지하차도(002): 나란히 붙은 상·하행 중 한쪽만 덮개(2)고 다른 쪽이 개착(3)이면 그쪽도 덮개 — 한 박스로(반쯤 트인 모양 방지)</summary>
        void SyncTwinCover()
        {
            var grid = new Dictionary<long, List<KeyValuePair<RoadGeom, int>>>();
            Func<double, long> cell = v => (long)Math.Floor(v / 10.0);
            foreach (var g in roadGeom.Values)
            {
                if (!g.ok || g.h == null || g.tmode == null) continue;
                for (int i = 0; i < g.c.Count; i++)
                {
                    if (g.tmode[i] != 2 || g.type[i] != "002") continue;
                    long k = (cell(g.c[i].x) << 32) ^ (cell(g.c[i].z) & 0xffffffffL); List<KeyValuePair<RoadGeom, int>> l;
                    if (!grid.TryGetValue(k, out l)) { l = new List<KeyValuePair<RoadGeom, int>>(); grid[k] = l; }
                    l.Add(new KeyValuePair<RoadGeom, int>(g, i));
                }
            }
            var up = new List<KeyValuePair<RoadGeom, int>>();
            foreach (var g in roadGeom.Values)
            {
                if (!g.ok || g.h == null || g.tmode == null) continue;
                for (int i = 0; i < g.c.Count; i++)
                {
                    if (g.tmode[i] != 3 || g.type[i] != "002" || !g.road.oneWay) continue;
                    V2 c = g.c[i], t = g.t[i], lf = t.Left; long kx = cell(c.x), kz = cell(c.z); bool hit = false;
                    for (long ax = kx - 2; ax <= kx + 2 && !hit; ax++)
                        for (long az = kz - 2; az <= kz + 2 && !hit; az++)
                        {
                            List<KeyValuePair<RoadGeom, int>> l; if (!grid.TryGetValue((ax << 32) ^ (az & 0xffffffffL), out l)) continue;
                            foreach (var e in l)
                            {
                                var g2 = e.Key; int j = e.Value; if (g2.road == g.road || !g2.road.oneWay) continue;
                                if (Math.Abs(g2.h[j] - g.h[i]) > 2.5 || Math.Abs(V2.Dot(t, g2.t[j])) < 0.94) continue;
                                V2 d = g2.c[j] - c; double lon = Math.Abs(V2.Dot(d, t)), lat = Math.Abs(V2.Dot(d, lf));
                                double reach = (V2.Dot(d, lf) > 0 ? g.wL[i] : g.wR[i]) + Math.Max(g2.wL[j], g2.wR[j]) + 2 * (P.tubeMargin + P.tubeShell) + 0.5;
                                if (lon <= 2.5 && lat <= reach) { hit = true; break; }
                            }
                        }
                    if (hit) up.Add(new KeyValuePair<RoadGeom, int>(g, i));
                }
            }
            foreach (var e in up) e.Key.tmode[e.Value] = 2;
            if (up.Count > 0) log.Add("twin underpass: open cut → covered " + up.Count);
        }
        /// <summary>값이 v 인 연속 구간 중 minLen 칸보다 짧은 것을 to 로. between: 양옆이 to 일 때만</summary>
        static void Runs(byte[] a, byte v, int minLen, byte to, bool between)
        {
            int n = a.Length;
            for (int i = 0; i < n; )
            {
                if (a[i] != v) { i++; continue; }
                int j = i; while (j < n && a[j] == v) j++;
                bool l = i > 0 && a[i - 1] == to, r = j < n && a[j] == to;
                if (j - i < minLen && (!between || (l && r)) && (l || r)) for (int k = i; k < j; k++) a[k] = to;
                i = j;
            }
        }

        // ---------- 마주 보는 인도 빼기 ----------
        //   나란한 길(분리 도로 상·하행, 램프·본선, 고가 밑 길) 사이 분리대 쪽 인도는 다른 길 차도를 덮음 → 그쪽 인도를 없앰
        //   (인도 띠 안에 같은 높이(±2.5m) 다른 도로 차도가 있는 점). 짧게 끊긴 곳은 이어서 없앰
        void TrimFacingSidewalks()
        {
            if (!P.trimFacingSidewalks) return;
            var gs = roadGeom.Values.Where(g => g.ok && g.h != null).ToList();
            const double cell = 1.0;
            Func<V2, long> K = q => ((long)Math.Floor(q.x / cell) << 32) ^ ((long)Math.Floor(q.z / cell) & 0xffffffffL);
            var grid = new Dictionary<long, List<long>>();
            for (int gi = 0; gi < gs.Count; gi++)
            {
                var g = gs[gi]; int n = g.c.Count;
                for (int i = 0; i < n; i++)
                    for (double f = 0; f < 1.0; f += 0.5)
                    {   // 점 사이(반 칸)도 채움
                        if (f > 0 && i + 1 >= n) break;
                        V2 c = f == 0 ? g.c[i] : (g.c[i] + g.c[i + 1]) * 0.5, lf = g.t[i].Left;
                        for (double u = -g.wR[i]; u <= g.wL[i] + 1e-6; u += 0.5)
                        {
                            long key = K(c + lf * u);
                            List<long> l; if (!grid.TryGetValue(key, out l)) { l = new List<long>(); grid[key] = l; }
                            if (l.Count == 0 || l[l.Count - 1] >> 24 != gi) l.Add(((long)gi << 24) | (long)i);
                        }
                    }
            }
            // 인도끼리 겹침(나란한 일방 두 길 사이 가운데에 양쪽 인도가 겹침 — 광안대로 남천 지면 도로 등): 인도 자리 격자도 따로
            var gridSw = new Dictionary<long, List<long>>();
            for (int gi = 0; gi < gs.Count; gi++)
            {
                var g = gs[gi]; int n = g.c.Count;
                for (int i = 0; i < n; i++)
                {
                    V2 lf = g.t[i].Left;
                    for (int side = 0; side < 2; side++)
                    {
                        double w0 = side == 0 ? g.wL[i] : g.wR[i], s0 = side == 0 ? g.swL[i] : g.swR[i], sg0 = side == 0 ? 1 : -1;
                        if (s0 <= 0) continue;
                        for (double u = w0 + 0.25; u <= w0 + s0; u += 0.5)
                        {
                            long key = K(g.c[i] + lf * (sg0 * u));
                            List<long> l; if (!gridSw.TryGetValue(key, out l)) { l = new List<long>(); gridSw[key] = l; }
                            if (l.Count == 0 || l[l.Count - 1] >> 24 != gi) l.Add(((long)gi << 24) | (long)i);
                        }
                    }
                }
            }
            int trimmed = 0;
            for (int gi = 0; gi < gs.Count; gi++)
            {
                var g = gs[gi]; int n = g.c.Count;
                for (int side = 0; side < 2; side++)
                {
                    var sw = side == 0 ? g.swL : g.swR; var w = side == 0 ? g.wL : g.wR; double sg = side == 0 ? 1 : -1;
                    var hit = new bool[n]; bool any = false;
                    if (P.trimOverlappingSidewalks)
                        for (int i = 0; i < n; i++)
                        {   // 다른 나란한 길(|cos| > 0.9)의 인도와 같은 높이(1m 안)로 겹치면 양쪽 다 없앰(가운데는 지형)
                            if (sw[i] <= 0) continue;
                            V2 lf = g.t[i].Left;
                            for (double u = w[i] + 0.25; u <= w[i] + sw[i] && !hit[i]; u += 0.5)
                            {
                                List<long> l; if (!gridSw.TryGetValue(K(g.c[i] + lf * (sg * u)), out l)) continue;
                                foreach (var e in l)
                                {
                                    int oj = (int)(e >> 24), oi = (int)(e & 0xffffff); if (oj == gi || gs[oj].road == g.road) continue;
                                    if (Math.Abs(gs[oj].h[oi] - g.h[i]) < 1.0 && Math.Abs(V2.Dot(gs[oj].t[oi], g.t[i])) > 0.9) { hit[i] = true; any = true; break; }
                                }
                            }
                        }
                    for (int i = 0; i < n; i++)
                    {
                        if (sw[i] <= 0) continue;
                        V2 lf = g.t[i].Left;
                        for (double u = w[i] + 0.25; u <= w[i] + sw[i] + 0.3 && !hit[i]; u += 0.5)
                        {
                            List<long> l; if (!grid.TryGetValue(K(g.c[i] + lf * (sg * u)), out l)) continue;
                            foreach (var e in l)
                            {
                                int oj = (int)(e >> 24), oi = (int)(e & 0xffffff); if (oj == gi) continue;
                                if (Math.Abs(gs[oj].h[oi] - g.h[i]) < 2.5) { hit[i] = true; any = true; break; }
                            }
                        }
                    }
                    if (!any) continue;
                    // 사이 6m 안 끊김은 메우고, 앞뒤 2m 더
                    int gapMax = (int)Math.Ceiling(6 / P.step), pad = (int)Math.Ceiling(2 / P.step);
                    var cut = new bool[n];
                    for (int i = 0; i < n; i++) if (hit[i]) for (int k = Math.Max(0, i - pad); k <= Math.Min(n - 1, i + pad); k++) cut[k] = true;
                    for (int i = 0; i < n; )
                    {
                        if (cut[i]) { i++; continue; }
                        int j = i; while (j < n && !cut[j]) j++;
                        if (i > 0 && j < n && j - i <= gapMax) for (int k = i; k < j; k++) cut[k] = true;
                        i = j;
                    }
                    for (int i = 0; i < n; i++) if (cut[i] && sw[i] > 0) { sw[i] = 0; trimmed++; }
                }
            }
            foreach (var R in net.roads) SyncArms(R);
            if (trimmed > 0) log.Add("facing sidewalks removed at " + trimmed + " stations");
        }

        // ---------- 합류·나란한 길 차도 자르기 ----------
        //   같은 높이로 겹치는 두 길(램프가 본선 옆에 붙어 들어오는 곳 등): 작은 길(차로 적은 쪽) 단면을 큰 길 차도 가장자리에서 자름 → 겹친 면(깜빡임) 없음
        //   작은 길 중심선까지 큰 길 안이면 그 점 단면을 아주 얇게(큰 길 면이 덮음)
        void TrimMergingRoads()
        {
            if (!P.trimMergingRoads) return;
            var gs = roadGeom.Values.Where(g => g.ok && g.h != null).ToList();
            const double cell = 2.0;
            Func<V2, long> K = q => ((long)Math.Floor(q.x / cell) << 32) ^ ((long)Math.Floor(q.z / cell) & 0xffffffffL);
            var grid = new Dictionary<long, List<long>>();
            for (int gi = 0; gi < gs.Count; gi++)
            {
                var g = gs[gi]; int n = g.c.Count;
                for (int i = 0; i < n; i++)
                {
                    V2 lf = g.t[i].Left;
                    for (double u = -g.wR[i]; u <= g.wL[i] + 1e-6; u += 1.0)
                    {
                        long key = K(g.c[i] + lf * u);
                        List<long> l; if (!grid.TryGetValue(key, out l)) { l = new List<long>(); grid[key] = l; }
                        long e = ((long)gi << 24) | (long)i; if (l.Count == 0 || l[l.Count - 1] != e) l.Add(e);
                    }
                }
            }
            // 큰 길: 그 점 차로 합이 많은 쪽, 같으면 등급(숫자 작을수록), 같으면 긴 쪽
            Func<RoadGeom, int, double> weight = (g, i) =>
            {
                var R = g.road; double so = g.sOrig[i]; int lanes = R.LanesAt(R.fwd, so) + R.LanesAt(R.bwd, so);
                var pc = PieceAt(R, so); string rk = pc != null && !string.IsNullOrEmpty(pc.rank) ? pc.rank : R.rank; int rv; int.TryParse(rk, out rv);
                return lanes * 1000 + (200 - rv) + Math.Min(0.99, g.Length / 1e5);
            };
            // 점 q 가 다른 길(oj) 차도 안인가(그 길 점 oi 근처 단면으로)
            Func<V2, int, int, double, bool> inside = (q, oj, oi, hq) =>
            {
                var o = gs[oj]; int on = o.c.Count;
                for (int k = Math.Max(0, oi - 1); k <= Math.Min(on - 1, oi + 1); k++)
                {
                    if (Math.Abs(o.h[k] - hq) > 1.0) continue;
                    V2 d = q - o.c[k]; double along = V2.Dot(d, o.t[k]), lat = V2.Dot(d, o.t[k].Left);
                    if (Math.Abs(along) <= P.step * 0.55 && lat >= -o.wR[k] && lat <= o.wL[k]) return true;
                }
                return false;
            };
            int trimmed = 0, hidden = 0;
            for (int gi = 0; gi < gs.Count; gi++)
            {
                var g = gs[gi]; int n = g.c.Count; bool changed = false;
                var nL = (double[])g.wL.Clone(); var nR = (double[])g.wR.Clone();
                for (int i = 0; i < n; i++)
                {
                    double myW = weight(g, i); V2 lf = g.t[i].Left;
                    // 이 점 근처에 있는 더 큰 길 후보
                    var cands = new List<long>();
                    for (double u = -g.wR[i]; u <= g.wL[i] + 1e-6; u += 1.0)
                    {
                        List<long> l; if (!grid.TryGetValue(K(g.c[i] + lf * u), out l)) continue;
                        foreach (var e in l) { int oj = (int)(e >> 24); if (oj != gi && !cands.Contains(e)) cands.Add(e); }
                    }
                    if (cands.Count == 0) continue;
                    var bigger = cands.Where(e => { int oj = (int)(e >> 24), oi = (int)(e & 0xffffff); return weight(gs[oj], oi) > myW && Math.Abs(gs[oj].h[oi] - g.h[i]) < 1.0; }).ToList();
                    if (bigger.Count == 0) continue;
                    Func<double, bool> inBig = u => { V2 q = g.c[i] + lf * u; foreach (var e in bigger) if (inside(q, (int)(e >> 24), (int)(e & 0xffffff), g.h[i])) return true; return false; };
                    if (inBig(0))
                    {   // 중심선까지 큰 길 안: 바깥쪽으로 큰 길을 벗어나는 데까지만 남김
                        double outL = -1, outR = -1;
                        for (double u = 0.25; u <= g.wL[i]; u += 0.25) if (!inBig(u)) { outL = u; break; }
                        for (double u = 0.25; u <= g.wR[i]; u += 0.25) if (!inBig(-u)) { outR = u; break; }
                        if (outL < 0 && outR < 0) { nL[i] = Math.Min(nL[i], 0.02); nR[i] = Math.Min(nR[i], 0.02); hidden++; changed = true; }
                        else if (P.trimInnerHalf && (outL < 0) != (outR < 0))
                        {   // 한쪽만 바깥으로 나감: 큰 길 안에 다 들어간 쪽 반은 숨김(겹친 면 절반 줄임), 나가는 쪽은 그대로(길이 끊기지 않게)
                            if (outL < 0) nL[i] = Math.Min(nL[i], 0.02); else nR[i] = Math.Min(nR[i], 0.02);
                            changed = true; trimmed++;
                        }
                        continue;   // 한쪽이 바깥으로 나가면 그쪽은 그대로(겹침은 남지만 길이 끊기지 않게)
                    }
                    for (double u = 0.25; u <= g.wL[i] + 1e-6; u += 0.25) if (inBig(u)) { nL[i] = Math.Min(nL[i], Math.Max(0.02, u - 0.25)); changed = true; trimmed++; break; }
                    for (double u = 0.25; u <= g.wR[i] + 1e-6; u += 0.25) if (inBig(-u)) { nR[i] = Math.Min(nR[i], Math.Max(0.02, u - 0.25)); changed = true; trimmed++; break; }
                }
                if (!changed) continue;
                // 들쭉날쭉하지 않게: 이웃 최소 → 평균. 상판(방호벽 있는 곳)은 trimTaper m 에 걸쳐 차로 줄이듯 부드럽게(방호벽 꺾임 방지),
                // 지면은 짧게(길게 하면 큰 길이 안 덮는 곳까지 잘려 틈(땅)이 보임)
                int tw = Math.Max(1, (int)Math.Round(P.trimTaper / P.step));
                foreach (var arr in new[] { nL, nR })
                {
                    var src = (double[])arr.Clone();
                    for (int pass = 0; pass < 2; pass++)
                    {
                        int w = pass == 0 ? 1 : tw;
                        var m = new double[n];
                        for (int i = 0; i < n; i++) { double v = src[i]; for (int d = -w; d <= w; d++) { int q = i + d; if (q >= 0 && q < n) v = Math.Min(v, src[q]); } m[i] = v; }
                        for (int i = 0; i < n; i++)
                        {
                            if ((pass == 1) != (g.tmode[i] == 1)) continue;
                            double sm = 0; int c = 0; for (int d = -w; d <= w; d++) { int q = Math.Max(0, Math.Min(n - 1, i + d)); sm += m[q]; c++; }
                            arr[i] = Math.Min(src[i], sm / c);
                        }
                    }
                }
                for (int i = 0; i < n; i++)
                {
                    if (nL[i] < g.wL[i] - 1e-3) { g.wL[i] = nL[i]; g.swL[i] = 0; }
                    if (nR[i] < g.wR[i] - 1e-3) { g.wR[i] = nR[i]; g.swR[i] = 0; }
                }
            }
            foreach (var R in net.roads) SyncArms(R);
            if (trimmed + hidden > 0) log.Add("merging roads trimmed at " + trimmed + " sides, hidden " + hidden + " stations");
        }

        /// <summary>팔 폭을 실제 도로 끝 단면 값으로(교차로 면 입구와 도로 면 끝이 정확히 같은 점)</summary>
        void SyncArms(Road R)
        {
            var g = roadGeom[R]; if (!g.ok) return; int n = g.c.Count;
            if (R.armStart != null) { var A = R.armStart; A.wOut = g.wR[0]; A.wIn = g.wL[0]; A.swOut = g.swR[0]; A.swIn = g.swL[0]; A.lanesOut = R.LanesAt(R.fwd, g.sOrig[0]); A.lanesIn = R.LanesAt(R.bwd, g.sOrig[0]); A.sh = g.sh[0]; A.edgeType = g.edgeType; }
            if (R.armEnd != null) { var A = R.armEnd; A.wOut = g.wL[n - 1]; A.wIn = g.wR[n - 1]; A.swOut = g.swL[n - 1]; A.swIn = g.swR[n - 1]; A.lanesOut = R.LanesAt(R.bwd, g.sOrig[n - 1]); A.lanesIn = R.LanesAt(R.fwd, g.sOrig[n - 1]); A.sh = g.sh[n - 1]; A.edgeType = g.edgeType; }
        }

        // ---------- 높이 ----------
        void Heights(RoadGeom g)
        {
            int n = g.c.Count; var h = new double[n];
            for (int i = 0; i < n; i++) h[i] = Dem(g.c[i]);
            h = Geom.SmoothVals(h, P.heightSmoothM / P.step);
            int i0 = 0;
            while (i0 < n)
            {
                string ty = g.type[i0]; int i1 = i0;
                while (i1 + 1 < n && g.type[i1 + 1] == ty) i1++;
                if (ty == "003" || ty == "004")
                {   // 교량·터널: 양 끝을 곧게 이음(골짜기로 처지거나 산을 타고 넘지 않게)
                    int a = Math.Max(0, i0 - 2), b = Math.Min(n - 1, i1 + 2);
                    double ha = h[a], hb = h[b], sa = g.cum[a], sb = g.cum[b];
                    for (int k = a; k <= b; k++) h[k] = ha + (hb - ha) * (g.cum[k] - sa) / Math.Max(1e-6, sb - sa);
                    // 광안대교: 실제 종단(주경간 가운데 상층 46m)
                    if (ty == "003" && Gwangan.Profile(this, g, h, a, b, ha, hb)) { }
                    // 바다·큰 강 위 긴 다리(밑이 거의 물높이): 양 끝에서 seaBridgeGrade 로 올라 seaBridgeTop 까지(뱃길 높이)
                    else if (ty == "003" && sb - sa > P.seaBridgeMinLen)
                    {
                        var under = new List<double>(); for (int k = a; k <= b; k += 5) under.Add(Dem(g.c[k]));
                        under.Sort();
                        if (under[under.Count / 2] < P.seaLevelMax)
                        {
                            var hc = new double[b - a + 1];
                            for (int k = a; k <= b; k++)
                            {
                                double up = Math.Min(P.seaBridgeTop, Math.Min(ha + P.seaBridgeGrade * (g.cum[k] - sa), hb + P.seaBridgeGrade * (sb - g.cum[k])));
                                hc[k - a] = Math.Max(h[k], up);
                            }
                            hc = Geom.SmoothVals(hc, P.seaBridgeCurve / P.step);          // 종단 곡선(꺾인 곳을 둥글게)
                            for (int k = a; k <= b; k++) h[k] = Math.Max(h[k], hc[k - a]);
                        }
                    }
                }
                else if (ty == "001" || ty == "002")
                {   // 고가차도 +, 지하차도 − (양 끝 경사로)
                    double H = ty == "001" ? P.overpassClear : -P.underpassDepth, ramp = Math.Abs(H) / P.rampGrade;
                    double sa = g.cum[i0], sb = g.cum[i1];
                    for (int k = i0; k <= i1; k++) h[k] += H * Math.Min(1, Math.Min(g.cum[k] - sa, sb - g.cum[k]) / ramp);
                }
                i0 = i1 + 1;
            }
            h = Geom.SmoothVals(h, 3);
            g.tmode = new byte[n]; g.under = new bool[n]; g.layer = new int[n];
            for (int i = 0; i < n; i++)
            {
                g.layer[i] = LayerAt(g.road, g.sOrig[i]);
                g.tmode[i] = (byte)(g.type[i] == "001" || g.type[i] == "003" ? 1 : 0);
                g.under[i] = g.type[i] == "002" || g.type[i] == "004" || g.layer[i] < 0;
            }
            if (P.osmBridges)
                for (int i = 0; i < n; )
                {   // OSM 다리(층 +)인 짧은 구간: 상판으로(하천 위 다리 등 — 밑 지형을 낮추고 교각)
                    if (g.layer[i] <= 0 || g.tmode[i] != 0) { i++; continue; }
                    int j = i; while (j < n && g.layer[j] > 0 && g.tmode[j] == 0) j++;
                    if (g.cum[j - 1] - g.cum[i] <= P.osmBridgeMaxLen) for (int k = i; k < j; k++) g.tmode[k] = 1;
                    i = j;
                }
            var ro = ov.Road(g.road);
            if (ro != null && ro.heightMode == 1 && Math.Abs(ro.heightOffset) > 1e-6)
            {   // 직접: 양 끝(rampLength)에서 서서히 올리고 내림. 많이 올린 곳은 고가(땅 그대로), 내린 곳은 주변도 깎임(지형 맞춤)
                double L = g.Length, ramp = Math.Max(1, Math.Min(ro.rampLength, L * 0.5));
                for (int i = 0; i < n; i++)
                {
                    double d = Math.Min(g.cum[i], L - g.cum[i]);
                    double add = ro.heightOffset * Geom.Smooth(d / ramp);
                    h[i] += add;
                    if (add > P.elevThreshold && g.tmode[i] == 0) g.tmode[i] = 1;
                    if (add < -1.0 && g.tmode[i] == 0) g.under[i] = true;
                }
            }
            if (g.road.hFixS != null && g.road.hFixS.Length >= 2)
            {   // 도로 편집기에서 사람이 정한 높이(노면 중심) — 층 나누기·골 메우기가 다시 건드리지 않음. 땅보다 많이 높으면 상판, 낮으면 땅 밑
                var mP = Geom.PchipM(g.road.hFixS, g.road.hFixY);                   // 편집기와 같은 단조 3차 곡선(점 사이 꺾임 없음)
                var TS = g.road.tmS; var TV = g.road.tmV;
                for (int i = 0; i < n; i++)
                {   // 편집기 tmOf 와 같은 규칙: 원래 다리였고 땅보다 0.5m 넘게 높거나 4.5m 넘게 높으면 상판, 1m 넘게 낮으면 땅 밑(덮개·개착은 땅 밑 판정에서)
                    h[i] = Geom.Pchip(g.road.hFixS, g.road.hFixY, mP, g.sOrig[i]);
                    if (TS != null)
                    {   // 편집기가 판정한 구조 그대로(편집기 화면 = 유니티)
                        int lo = 0, hi = TS.Length - 1; double s = g.sOrig[i];
                        while (hi - lo > 1) { int q = (lo + hi) >> 1; if (TS[q] <= s) lo = q; else hi = q; }
                        byte code = Math.Abs(TS[lo] - s) <= Math.Abs(TS[hi] - s) ? TV[lo] : TV[hi];
                        g.tmode[i] = code; g.under[i] = code == 2 || code == 3; continue;
                    }
                    double d = h[i] - Dem(g.c[i]); bool deckType = g.tmode[i] == 1;
                    g.tmode[i] = (byte)((deckType && d > 0.5) || d > P.elevThreshold ? 1 : 0);
                    g.under[i] = g.tmode[i] == 0 && d < -1.0;
                }
            }
            g.h = h;
        }

        // ---------- 교차로 모서리·면 ----------
        void BuildCorners(JunctionGeom G, int gentleLevel)
        {
            var arms = G.j.arms; int n = arms.Count;
            G.corners.Clear(); G.ok = n >= 2;
            if (!G.ok) return;
            for (int i = 0; i < n; i++)
            {
                Arm A = arms[i], B = arms[(i + 1) % n];
                var C = MakeCorner(A, B, n);
                if (gentleLevel > 0 && C.kind == 0) C.kind = 1;
                V2 la = A.dir.Left, lb = B.dir.Left;
                V2 MLa = A.Point(A.cut) + la * A.wIn, MRb = B.Point(B.cut) - lb * B.wOut;
                C.path.Add(MLa);
                if (C.kind == 0)
                {
                    V2 TA = C.F - la * C.R, TB = C.F + lb * C.R;
                    if (A.cut - C.tA > 0.05) C.path.Add(TA);
                    var arc = Geom.Arc(C.F, C.R, (TA - C.F).Angle, (TB - C.F).Angle, false, 6 * Math.PI / 180);
                    for (int k = 1; k < arc.Count - 1; k++) C.path.Add(arc[k]);
                    if (B.cut - C.tB > 0.05) C.path.Add(TB);
                }
                else if (C.kind == 1)
                {   // 완만한 쪽: 두 가장자리 직선이 교차로 안쪽에서 만나면 그 점을 조절점으로 곡선, 아니면 직선
                    double t, s;
                    if (gentleLevel < 2 && Geom.LineLine(MLa, A.dir, MRb, B.dir, out t, out s) && t < -0.5 && s < -0.5 && t > -60 && s > -60)
                    {
                        V2 X = MLa + A.dir * t;
                        for (int k = 1; k < 12; k++) { double u = k / 12.0; C.path.Add(MLa * ((1 - u) * (1 - u)) + X * (2 * u * (1 - u)) + MRb * (u * u)); }
                    }
                }
                C.path.Add(MRb);
                C.swA = C.kind == 2 ? 0 : A.swIn; C.swB = C.kind == 2 ? 0 : B.swOut;
                C.hasCurb = C.kind != 2 && (C.swA > 0 || C.swB > 0) && P.markings;
                if (C.hasCurb)
                {   // 안쪽(교차로 쪽) 방향: 끝은 팔 방향에 맞춰 도로 단면과 같은 점
                    var p = C.path; int pn = p.Count; var inD = new V2[pn];
                    for (int k = 0; k < pn; k++) inD[k] = ((p[Math.Min(pn - 1, k + 1)] - p[Math.Max(0, k - 1)]).Norm).Left;
                    inD[0] = -A.dir.Left; inD[pn - 1] = B.dir.Left;
                    // 길어깨는 두 팔 값 사이를 곡선 따라 이어 줌(속도가 다른 길이 만나는 모서리)
                    var pcum = Geom.CumLen(p); double pU = Math.Max(1e-6, pcum[pn - 1]); double ew = P.lineEdgeW;
                    for (int q = 0; q < 4; q++)
                    {
                        var r = new List<V2>(pn);
                        for (int k = 0; k < pn; k++)
                        {
                            double sh = A.sh + (B.sh - A.sh) * pcum[k] / pU;
                            double d = q == 0 ? 0 : q == 1 ? P.gutterWidth : q == 2 ? sh - ew * 0.5 : sh + ew * 0.5;
                            r.Add(p[k] + inD[k] * d);
                        }
                        C.rings.Add(r);
                    }
                    C.whiteEdge = (C.swA <= 0 || A.edgeType == 0) && (C.swB <= 0 || B.edgeType == 0);
                    if (!IsSimple(C.rings[3]) && C.rings[3].Count > 3) { C.hasCurb = false; C.rings.Clear(); }
                }
                G.corners.Add(C);
            }
        }

        void BuildPoly(JunctionGeom G)
        {
            G.poly.Clear(); if (!G.ok) return;
            var arms = G.j.arms;
            int na = arms.Count;
            for (int i = 0; i < na; i++)
            {
                var A = arms[i]; V2 M = A.Point(A.cut), lf = A.dir.Left; double eo = A.sh + P.lineEdgeW * 0.5;
                var Cprev = G.corners[(i + na - 1) % na]; var C = G.corners[i];
                G.poly.Add(M - lf * (A.wOut - (Cprev.hasCurb ? eo : 0))); G.poly.Add(M + lf * (A.wIn - (C.hasCurb ? eo : 0)));
                var path = C.hasCurb ? C.rings[3] : C.path;
                for (int k = 1; k < path.Count - 1; k++) G.poly.Add(path[k]);
            }
            G.simple = Geom.SignedArea(G.poly) > 0 && IsSimple(G.poly);
        }

        static bool IsSimple(List<V2> p)
        {
            int n = p.Count;
            for (int i = 0; i < n; i++)
                for (int k = i + 2; k < n; k++)
                {
                    if (i == 0 && k == n - 1) continue;
                    if (SegX(p[i], p[(i + 1) % n], p[k], p[(k + 1) % n])) return false;
                }
            return true;
        }
        static bool SegX(V2 a, V2 b, V2 c, V2 d)
        {
            double d1 = V2.Cross(b - a, c - a), d2 = V2.Cross(b - a, d - a), d3 = V2.Cross(d - c, a - c), d4 = V2.Cross(d - c, b - c);
            return ((d1 > 1e-9 && d2 < -1e-9) || (d1 < -1e-9 && d2 > 1e-9)) && ((d3 > 1e-9 && d4 < -1e-9) || (d3 < -1e-9 && d4 > 1e-9));
        }

        /// <summary>교차로 면 = 평면 하나: 중심(지형) + 각 입구(도로 높이)에 최소제곱, 기울기 상한</summary>
        /// <summary>교차로 면(편집기 fitJPlane 과 같은 식): 팔 1개 = 끝 높이, 팔 2개(이음매) = 두 끝을 잇는 면,
        /// 3개 이상 = 팔 끝(사람이 정한 높이 3배, 가운데서 3m 넘게 튀는 끝 0.2배) + 가운데(팔 끝 가중 중앙값 — 땅과 1.5m 안이면 땅, 2배) 최소제곱, 기울기 상한</summary>
        void FitPlane(JunctionGeom G)
        {
            var J = G.j; V2 c = J.center;
            var pts = new List<V2>(); var hs = new List<double>(); var ws = new List<double>(); var dirs = new List<V2?>(); var names = new List<string>();
            foreach (var A in J.arms)
            {
                RoadGeom g; V2 M = A.Point(A.cut); double h = Dem(M), w = 1; V2? dd = null;
                if (roadGeom.TryGetValue(A.road, out g) && g.ok && g.h != null)
                {
                    h = A.atStart ? g.h[0] : g.h[g.h.Length - 1]; if (A.road.hFixS != null && !A.road.hBase) w = 3;
                    int nc = g.c.Count; double Lg = g.Length, s10 = Math.Min(10, Lg / 2);          // 나가는 방향(끝에서 10m) — 편집기 armDir
                    V2 E = A.atStart ? g.c[0] : g.c[nc - 1], P1 = Geom.At(g.c, g.cum, A.atStart ? s10 : Lg - s10), dv = P1 - E; double dl = dv.Len;
                    if (dl > 1e-6) dd = dv / dl;
                }
                pts.Add(M); hs.Add(h); ws.Add(w); dirs.Add(dd); names.Add(A.road.name);
            }
            G.fitP = new List<V2>(pts); G.fitH = new List<double>(hs); G.fitW = new List<double>(ws); G.mainI = G.mainJ = -1;
            if (pts.Count == 0) { G.h0 = Dem(c); G.gx = G.gz = 0; return; }
            if (FitMain(G, pts, hs, ws, dirs, names)) return;
            if (pts.Count == 1) { G.h0 = hs[0]; G.gx = G.gz = 0; return; }
            if (pts.Count == 2)
            {
                V2 d = pts[1] - pts[0]; double L = d.Len;
                if (L < 0.3) { G.h0 = (hs[0] * ws[0] + hs[1] * ws[1]) / (ws[0] + ws[1]); G.gx = G.gz = 0; return; }
                V2 u = d / L; double gr = (hs[1] - hs[0]) / L;
                G.h0 = hs[0] + gr * V2.Dot(c - pts[0], u); G.gx = gr * u.x; G.gz = gr * u.z; return;
            }
            var ord = Enumerable.Range(0, pts.Count).OrderBy(i => hs[i]).ToList(); double W = ws.Sum(), acc = 0, hm = hs[ord[0]];
            foreach (int i in ord) { acc += ws[i]; if (acc >= W / 2) { hm = hs[i]; break; } }
            double g0 = Dem(c); bool atGrade = Math.Abs(hm - g0) <= 1.5;
            for (int i = 0; i < pts.Count; i++) if (Math.Abs(hs[i] - hm) > 3) ws[i] *= 0.2;
            pts.Add(c); hs.Add(atGrade ? g0 : hm); ws.Add(2);
            double S = 0, Sx = 0, Sz = 0, Sxx = 0, Sxz = 0, Szz = 0, Sh = 0, Sxh = 0, Szh = 0;
            for (int i = 0; i < pts.Count; i++)
            {
                double x = pts[i].x - c.x, z = pts[i].z - c.z, w = ws[i], h = hs[i];
                S += w; Sx += w * x; Sz += w * z; Sxx += w * x * x; Sxz += w * x * z; Szz += w * z * z; Sh += w * h; Sxh += w * x * h; Szh += w * z * h;
            }
            double[] sol;
            if (Solve3(new[] { S, Sx, Sz, Sx, Sxx, Sxz, Sz, Sxz, Szz }, new[] { Sh, Sxh, Szh }, out sol)) { G.h0 = sol[0]; G.gx = sol[1]; G.gz = sol[2]; }
            else { G.h0 = Sh / S; G.gx = G.gz = 0; }
            double gm = Math.Sqrt(G.gx * G.gx + G.gz * G.gz);
            if (gm > P.maxJunctionGrade)
            {
                G.gx *= P.maxJunctionGrade / gm; G.gz *= P.maxJunctionGrade / gm;
                double r = 0; for (int i = 0; i < pts.Count; i++) r += ws[i] * (hs[i] - G.gx * (pts[i].x - c.x) - G.gz * (pts[i].z - c.z));
                G.h0 = r / S;
            }
        }
        /// <summary>큰길 우선 면(편집기 fitMain 과 같은 식): 곧게(150° 넘게) 마주 보는 팔 한 쌍 — 높이를 정한 길 먼저, 이름 같은 쌍, 같은 차로 쌍, 덜 가파른 쪽.
        /// 면은 두 끝을 잇는 선을 그대로 지나고(큰길이 꺾이지 않게), 옆 기울기는 그 선에서 1m 안인 나머지 팔에 맞춤(6% 까지). 큰길 기울기 15% 까지</summary>
        bool FitMain(JunctionGeom G, List<V2> pts, List<double> hs, List<double> ws, List<V2?> dirs, List<string> names)
        {
            V2 c = G.j.center; int n = pts.Count, bi = -1, bj = -1; double bs = -1e9;
            if (n < 3) return false;                                       // 팔 2개(이음매)는 두 끝을 잇는 면 그대로
            Func<string, bool> named = s => !string.IsNullOrEmpty(s) && s != "-";
            for (int i = 0; i < n; i++) for (int j = i + 1; j < n; j++)
            {
                if (dirs[i] == null || dirs[j] == null) continue;
                V2 di = dirs[i].Value, dj = dirs[j].Value; double cc = -(di.x * dj.x + di.z * dj.z), L = (pts[j] - pts[i]).Len;
                if (cc < 0.866 || L < 1) continue;
                double al = Math.Abs(((pts[j].x - pts[i].x) * di.x + (pts[j].z - pts[i].z) * di.z) / L);
                double sc = (ws[i] >= 3 && ws[j] >= 3 ? 3 : 0) + (named(names[i]) && names[i] == names[j] ? 2 : 0) + cc + 2 * al - 0.5 * Math.Abs(hs[j] - hs[i]) / L;
                if (sc > bs) { bs = sc; bi = i; bj = j; }
            }
            if (bi < 0) return false;
            G.mainI = bi; G.mainJ = bj;
            V2 A = pts[bi], B = pts[bj]; double LL = (B - A).Len, ux = (B.x - A.x) / LL, uz = (B.z - A.z) / LL, vx = -uz, vz = ux;
            double ga = (hs[bj] - hs[bi]) / LL, ha = hs[bi];
            if (Math.Abs(ga) > 0.15) { ga = Math.Sign(ga) * 0.15; ha = (hs[bi] + hs[bj]) / 2 - ga * LL / 2; }
            double Sw = 0, Sr = 0;
            for (int k = 0; k < n; k++)
            {
                if (k == bi || k == bj) continue;
                double tt = (pts[k].x - A.x) * ux + (pts[k].z - A.z) * uz, o = (pts[k].x - A.x) * vx + (pts[k].z - A.z) * vz, r = hs[k] - (ha + ga * tt);
                if (Math.Abs(r) > 1 || Math.Abs(o) < 0.5) continue;
                Sw += ws[k] * o * o; Sr += ws[k] * r * o;
            }
            double gcv = Math.Max(-0.06, Math.Min(0.06, Sw > 0 ? Sr / Sw : 0));
            G.h0 = ha + ga * ((c.x - A.x) * ux + (c.z - A.z) * uz) + gcv * ((c.x - A.x) * vx + (c.z - A.z) * vz);
            G.gx = ga * ux + gcv * vx; G.gz = ga * uz + gcv * vz;
            return true;
        }
        static bool Solve3(double[] m, double[] b, out double[] x)
        {
            x = null;
            double det = m[0] * (m[4] * m[8] - m[5] * m[7]) - m[1] * (m[3] * m[8] - m[5] * m[6]) + m[2] * (m[3] * m[7] - m[4] * m[6]);
            if (Math.Abs(det) < 1e-9) return false;
            x = new double[3];
            for (int c = 0; c < 3; c++)
            {
                var q = (double[])m.Clone(); q[c] = b[0]; q[3 + c] = b[1]; q[6 + c] = b[2];
                x[c] = (q[0] * (q[4] * q[8] - q[5] * q[7]) - q[1] * (q[3] * q[8] - q[5] * q[6]) + q[2] * (q[3] * q[7] - q[4] * q[6])) / det;
            }
            return true;
        }

        // =====================================================================
        // 메쉬
        // =====================================================================
        public List<MeshData> BuildAll(Func<V2, bool> inBox)
        {
            var o = new List<MeshData>();
            foreach (var R in net.roads) { if (inBox != null && !R.center.Any(inBox)) continue; var m = BuildRoad(R); if (m != null) o.Add(m); }
            SubtractRoadOverlaps(o);                       // 같은 높이로 겹친 도로: 작은 도로 쪽을 잘라냄(RoadOverlap.cs)
            foreach (var J in net.junctions) { if (inBox != null && !inBox(J.center)) continue; var m = BuildJunction(J); if (m != null) o.Add(m); }
            return o;
        }

        /// <summary>도로 점 i 근처 위치 p 의 높이: 도로 높이, 교차로 가까이는 교차로 평면으로 넘어감</summary>
        public double RoadH(RoadGeom g, int i, V2 p) { return RoadHs(g, g.cum[i], g.h[i], p); }
        /// <summary>도로 s 위치 근처 점 p 의 높이(중심 높이 hc). 교차로 가까이는 교차로 평면으로 넘어감</summary>
        public double RoadHs(RoadGeom g, double s, double hc, V2 p)
        {
            double h = hc; var R = g.road; double L = g.Length, Lb0 = Math.Min(P.junctionBlend, L * 0.5);
            JunctionGeom G;
            // 면과 끝 높이 차가 크면 경사 6% 로 넘어가게 길게(길 절반까지) — 편집기 blendLen 과 같음
            if (R.armStart != null && R.armStart.cut > 0 && junctionGeom.TryGetValue(R.armStart.j, out G) && G.ok)
            { double Lb = Math.Min(L * 0.5, Math.Max(Lb0, Math.Abs(G.H(g.c[0]) - g.h[0]) / 0.06)); if (s < Lb) { double w = Geom.Smooth(s / Lb); h = G.H(p) * (1 - w) + h * w; } }
            if (R.armEnd != null && R.armEnd.cut > 0 && junctionGeom.TryGetValue(R.armEnd.j, out G) && G.ok)
            { int n = g.c.Count; double Lb = Math.Min(L * 0.5, Math.Max(Lb0, Math.Abs(G.H(g.c[n - 1]) - g.h[n - 1]) / 0.06)); if (L - s < Lb) { double w = Geom.Smooth((L - s) / Lb); h = G.H(p) * (1 - w) + h * w; } }
            return h;
        }

        static V2[] Offset(RoadGeom g, int side, double[] w, double[] extra, double extraConst)
        {
            int n = g.c.Count; var o = new V2[n];
            for (int i = 0; i < n; i++) o[i] = g.c[i] + g.t[i].Left * (side * (w[i] + (extra != null ? extra[i] : 0) + extraConst));
            for (int i = 1; i < n - 1; i++)                                  // 안쪽 급곡선에서 접히는 점은 앞 점에 붙임
                if (V2.Dot(o[i] - o[i - 1], g.t[i]) < 0.01) o[i] = o[i - 1] + g.t[i] * 0.01;
            return o;
        }

        public MeshData BuildRoad(Road R)
        {
            RoadGeom g; if (!roadGeom.TryGetValue(R, out g) || !g.ok) return null;
            var m = new MeshData { name = "Road_" + R.id + (string.IsNullOrEmpty(R.name) ? "" : "_" + R.name), kind = "road", refId = R.id };
            int n = g.c.Count;
            V2[] EL = Offset(g, 1, g.wL, null, 0), ER = Offset(g, -1, g.wR, null, 0);
            V2[] CL = Offset(g, 1, g.wL, null, P.curbWidth), CR = Offset(g, -1, g.wR, null, P.curbWidth);
            V2[] OL = Offset(g, 1, g.wL, g.swL, 0), OR = Offset(g, -1, g.wR, g.swR, 0);
            paint = new List<PaintBand>();
            if (P.markings) Markings(m, g);
            PaintSurface(m, g, paint);                     // 차도 면 + 노면표시(한 면)
            Side(m, n, i => RoadH(g, i, EL[i]), EL, CL, OL, g.swL, true, (i, p) => RoadH(g, i, p));
            Side(m, n, i => RoadH(g, i, ER[i]), ER, CR, OR, g.swR, false, (i, p) => RoadH(g, i, p));
            return m;
        }

        /// <summary>한쪽 연석+인도: E 차도 가장자리, C 연석 뒤, O 인도 바깥. left = 진행 방향 왼쪽 편</summary>
        void Side(MeshData m, int n, Func<int, double> hEdge, V2[] E, V2[] C, V2[] O, double[] sw, bool left, Func<int, V2, double> H)
        {
            double ch = P.curbHeight, sk = P.skirtDepth;
            var bE = new int[n]; var tE = new int[n]; var uE = new int[n]; var uC = new int[n]; var uO = new int[n]; var kO = new int[n]; var dO = new int[n];
            for (int i = 0; i < n; i++)
            {
                bool need = sw[i] > 0 || (i > 0 && sw[i - 1] > 0) || (i + 1 < n && sw[i + 1] > 0);
                if (!need) continue;
                double he = hEdge(i), hc = H(i, C[i]) + ch, ho = H(i, O[i]) + ch;
                bE[i] = m.V(E[i], he); tE[i] = m.V(E[i], he + ch);              // 연석 앞면(세움)
                uE[i] = m.V(E[i], he + ch); uC[i] = m.V(C[i], hc); uO[i] = m.V(O[i], ho);   // 윗면
                kO[i] = m.V(O[i], ho); dO[i] = m.V(O[i], ho - ch - sk);          // 바깥 치마(세움)
            }
            for (int i = 0; i + 1 < n; i++)
            {
                if (sw[i] <= 0 || sw[i + 1] <= 0) continue;
                m.Wall("curb", bE[i], bE[i + 1], tE[i], tE[i + 1], !left);          // 차도 쪽을 봄
                if (left) { m.Strip("curb", uC[i], uC[i + 1], uE[i], uE[i + 1]); m.Strip("sidewalk", uO[i], uO[i + 1], uC[i], uC[i + 1]); }
                else { m.Strip("curb", uE[i], uE[i + 1], uC[i], uC[i + 1]); m.Strip("sidewalk", uC[i], uC[i + 1], uO[i], uO[i + 1]); }
                m.Wall("curb_side", dO[i], dO[i + 1], kO[i], kO[i + 1], left);      // 바깥을 봄
            }
        }

        public MeshData BuildJunction(Junction J)
        {
            JunctionGeom G; if (!junctionGeom.TryGetValue(J, out G) || !G.ok || G.poly.Count < 3) return null;
            var m = new MeshData { name = "Junction_" + J.id + (string.IsNullOrEmpty(J.name) ? "" : "_" + J.name), kind = "junction", refId = J.id };
            if (G.sheetV != null && G.sheetT != null && G.sheetT.Count >= 3)
            {   // 면 한 장(차로 연결선 합집합 − 도로 면): 겹침 없음
                var vi = G.sheetV.Select(p => m.V(p, G.H(p))).ToList();
                for (int t = 0; t + 2 < G.sheetT.Count; t += 3) m.TriCCW("asphalt", vi[G.sheetT[t]], vi[G.sheetT[t + 1]], vi[G.sheetT[t + 2]]);
            }
            else if (G.simple) m.Poly("asphalt", G.poly, G.H);
            else
            {   // 꼬인 다각형: 중심에서 부채꼴(겹치는 곳은 같은 높이·같은 재질이라 안 보임)
                int c = m.V(J.center, G.H(J.center)); var idx = G.poly.Select(p => m.V(p, G.H(p))).ToList();
                for (int k = 0; k < idx.Count; k++) m.TriUp("asphalt", c, idx[k], idx[(k + 1) % idx.Count]);
            }
            var diag = P.markings ? DiagonalLines(G) : null;
            foreach (var C in G.corners)
            {
                CornerSidewalk(m, G, C);
                if (!C.hasCurb) continue;
                // 연석 → 안쪽: 측구 / 아스팔트 / 가장자리선(흰 실선) — 도로 쪽 같은 띠와 입구에서 이어짐(편람 505: 교차되는 도로의 가장자리선과 연결)
                string[] mats = { "gutter", "asphalt", C.whiteEdge ? "mark_white" : "asphalt" };
                for (int q = 0; q < 3; q++)
                {
                    var outer = C.rings[q]; var inner = C.rings[q + 1]; int pn = outer.Count;
                    var io = new int[pn]; var ii = new int[pn];
                    for (int k = 0; k < pn; k++) { io[k] = m.V(outer[k], G.H(outer[k])); ii[k] = m.V(inner[k], G.H(inner[k])); }
                    for (int k = 0; k + 1 < pn; k++)
                    {   // 대각선 횡단보도가 닿는 곳은 가장자리선을 끊음(횡단보도 앞 끝선처럼)
                        string mt = mats[q];
                        if (q == 2 && diag != null && mt == "mark_white" && UnderDiagonal(diag, (outer[k] + outer[k + 1]) * 0.5)) mt = "asphalt";
                        m.Strip(mt, ii[k], ii[k + 1], io[k], io[k + 1]);
                    }
                }
            }
            if (P.markings) DiagonalCrosswalks(m, G);
            if (P.markings && P.guideLines && DiagonalLines(G) == null) { GuideLines(m, G); if (P.throughGuides) ThroughGuideLines(m, G); }
            if (P.markings && P.mergeLaneLines && (G.j.merge || !G.j.arms.Any(a => a.crosswalk || a.stopDist > 0))) MergeLaneLines(m, G);   // 신호·횡단보도 없는 곳(합류점·나들목)
            return m;
        }

        /// <summary>모서리 연석선을 따라 연석(앞면+윗면) + 인도(다각형). 인도 바깥선은 접힌 고리를 잘라냄(예각 섬은 한 면으로).
        /// 양 끝 단면은 팔 방향에 맞춰 도로 인도와 딱 맞물림</summary>
        void CornerSidewalk(MeshData m, JunctionGeom G, Corner C)
        {
            var p = C.path; int n = p.Count;
            if (C.kind == 2 || n < 2 || (C.swA <= 0 && C.swB <= 0)) return;
            double ch = P.curbHeight, sk = P.skirtDepth;
            var cum = Geom.CumLen(p); double U = Math.Max(1e-6, cum[n - 1]);
            var outD = new V2[n];
            for (int k = 0; k < n; k++) outD[k] = -((p[Math.Min(n - 1, k + 1)] - p[Math.Max(0, k - 1)]).Norm).Left;
            outD[0] = C.a.dir.Left; outD[n - 1] = -C.b.dir.Left;
            var back = new List<V2>(n); var outer = new List<V2>(n);
            for (int k = 0; k < n; k++)
            {
                double sw = Math.Max(P.curbWidth + 0.1, C.swA + (C.swB - C.swA) * cum[k] / U);
                back.Add(p[k] + outD[k] * P.curbWidth); outer.Add(p[k] + outD[k] * sw);
            }
            outer = RemoveLoops(outer);
            // 연석 앞면 + 윗면
            for (int k = 0; k + 1 < n; k++)
            {
                double h0 = G.H(p[k]), h1 = G.H(p[k + 1]);
                int b0 = m.V(p[k], h0), b1 = m.V(p[k + 1], h1), t0 = m.V(p[k], h0 + ch), t1 = m.V(p[k + 1], h1 + ch);
                m.Wall("curb", b0, b1, t0, t1, true);
                int u0 = m.V(p[k], h0 + ch), u1 = m.V(p[k + 1], h1 + ch), c0 = m.V(back[k], G.H(back[k]) + ch), c1 = m.V(back[k + 1], G.H(back[k + 1]) + ch);
                m.Strip("curb", u0, u1, c0, c1);
            }
            // 인도 면
            var poly = new List<V2>(back); for (int k = outer.Count - 1; k >= 0; k--) poly.Add(outer[k]);
            bool skirt = true;
            if (!IsSimple(poly)) { poly = new List<V2>(back); skirt = false; if (!IsSimple(poly)) return; }
            m.Poly("sidewalk", poly, q => G.H(q) + ch);
            if (!skirt) return;
            for (int k = 0; k + 1 < outer.Count; k++)
            {
                double h0 = G.H(outer[k]) + ch, h1 = G.H(outer[k + 1]) + ch;
                int k0 = m.V(outer[k], h0), k1 = m.V(outer[k + 1], h1), d0 = m.V(outer[k], h0 - ch - sk), d1 = m.V(outer[k + 1], h1 - ch - sk);
                m.Wall("curb_side", d0, d1, k0, k1, false);
            }
        }

        // =====================================================================
        // 노면표시(교통노면표시 설치·관리 매뉴얼 기준 값은 GenParams 에서 바꿈)
        //   중앙선: 편도 2차로 이상 황색 복선, 편도 1차로 황색 실선, 분리 도로는 분리대 양옆 황색
        //   차선: 백색 점선(도시 3m/5m, 고속 8m/12m), 정지선 앞 solidBeforeStop m 는 백색 실선(진로변경 제한)
        //   가장자리선: 백색 실선(차로와 갓길 사이)
        //   횡단보도·정지선: 큰 교차로 입구(인도가 양쪽에 있는 팔)
        // =====================================================================
        void DecideCrosswalks(Junction J)
        {
            bool major = !J.merge && J.arms.Count >= 3 && J.arms.Any(a => a.lanesOut >= 2 || a.lanesIn >= 2 || a.road.rank == "103" || a.road.rank == "104");
            var jo = ov.Junction(J); bool force = jo != null && jo.crosswalks == 1;
            if (jo != null && jo.crosswalks >= 0) major = jo.crosswalks == 1;
            foreach (var A in J.arms)
            {
                RoadGeom g; bool ok = roadGeom.TryGetValue(A.road, out g) && g.ok;
                var ao = jo != null ? jo.Arm(A) : null;
                int kind = ao != null ? ao.crosswalk : CrosswalkKind.Auto;
                bool want = kind == CrosswalkKind.Auto ? major && (force || (A.swOut > 0 && A.swIn > 0)) : kind != CrosswalkKind.None;
                // 자동: 넓은 길(편도 splitCrosswalkMinLanes 차로 이상)은 폭 6m → 업무편람 532 '6m 이상은 2등분' 에 따라 방향 분리 2단
                bool wide = Math.Max(A.lanesIn, A.lanesOut) >= P.splitCrosswalkMinLanes;
                A.crosswalkKind = kind == CrosswalkKind.Auto ? (wide ? CrosswalkKind.Split : CrosswalkKind.Standard) : kind;
                A.crosswalkYellow = ao != null && ao.yellow == 1;
                A.crosswalkWidth = A.crosswalkKind == CrosswalkKind.Split ? P.crosswalkWidthSplit : P.crosswalkWidth;
                A.crosswalk = P.crosswalks && want && ok && !P.IsExpress(A.road.rank) && g.Length > 2 * (A.crosswalkWidth + P.stopLineGap + 3);
                A.stopDist = A.crosswalk && A.lanesIn > 0 ? A.crosswalkWidth + P.stopLineGap + P.stopLineWidth * 0.5 : 0;
            }
        }

        V2 TanAt(RoadGeom g, double s)
        {
            double L = g.Length;
            if (s <= 0.5) return g.t[0];
            if (s >= L - 0.5) return g.t[g.t.Length - 1];
            return (Geom.At(g.c, g.cum, s + 0.5) - Geom.At(g.c, g.cum, s - 0.5)).Norm;
        }
        double MarkH(RoadGeom g, double s, V2 p) { return RoadHs(g, s, Geom.AtVal(g.h, g.cum, s), p) + P.markLift; }
        /// <summary>노면표시 높이: 도로 면을 이루는 점(2m 간격)끼리 직선으로 이은 것과 똑같이(옆 위치 y) + 띄움 → 도로 면이 표시를 뚫지 않음</summary>
        double MarkHy(RoadGeom g, double s, double y)
        {
            int n = g.cum.Length; double L = g.Length;
            s = Geom.Clamp(s, 0, L);
            int lo = 0, hi = n - 1;
            while (hi - lo > 1) { int mid = (lo + hi) / 2; if (g.cum[mid] <= s) lo = mid; else hi = mid; }
            double d = g.cum[hi] - g.cum[lo], f = d > 1e-9 ? (s - g.cum[lo]) / d : 0;
            double h0 = RoadH(g, lo, g.c[lo] + g.t[lo].Left * y), h1 = RoadH(g, hi, g.c[hi] + g.t[hi].Left * y);
            return h0 + (h1 - h0) * f + P.markLift;
        }

        // ---------- 노면표시는 도로 면에 '짜 넣음' ----------
        // 표시를 아스팔트 위에 따로 띄워 깔면 멀리서 깜빡이거나(z-fighting) 볼록한 곳에서 뚫림 → 도로 면 자체를 표시 경계로 나눠
        // 같은 면의 칸마다 재질만 다르게(아스팔트 / 흰색 / 노란색). 겹침·뚫림이 생길 수 없음
        class PaintBand { public double sA, sB; public Func<double, double> lo, hi; public string mat; public int prio; }
        List<PaintBand> paint;

        /// <summary>도로를 따라가는 줄(s0~s1, 옆 위치 off(s) — + 왼쪽, 폭 width)</summary>
        void LineStrip(MeshData m, RoadGeom g, string mat, double s0, double s1, Func<double, double> off, double width)
        {
            if (s1 - s0 < 0.05) return;
            double hw = width / 2;
            paint.Add(new PaintBand { sA = s0, sB = s1, lo = q => off(q) - hw, hi = q => off(q) + hw, mat = mat, prio = 1 });
        }
        /// <summary>도로를 가로지르는 네모(s 구간 sA~sB, 옆 y0~y1)</summary>
        void QuadAcross(MeshData m, RoadGeom g, string mat, double sA, double sB, double y0, double y1)
        {
            double a = Math.Min(y0, y1), b = Math.Max(y0, y1);
            paint.Add(new PaintBand { sA = sA, sB = sB, lo = q => a, hi = q => b, mat = mat, prio = 2 });
        }

        /// <summary>도로 면 위치(s, 옆 y): 도로 점(2m 간격) 단면 사이를 직선으로 — 연석·인도와 같은 점을 씀</summary>
        int StationAt(RoadGeom g, double s, out double f)
        {
            int n = g.cum.Length; s = Geom.Clamp(s, 0, g.Length);
            int lo = 0, hi = n - 1;
            while (hi - lo > 1) { int mid = (lo + hi) / 2; if (g.cum[mid] <= s) lo = mid; else hi = mid; }
            if (lo >= n - 1) lo = n - 2;
            double d = g.cum[lo + 1] - g.cum[lo]; f = d > 1e-9 ? Geom.Clamp((s - g.cum[lo]) / d, 0, 1) : 0;
            return lo;
        }
        V2 SurfP(RoadGeom g, double s, double y)
        {
            double f; int i = StationAt(g, s, out f);
            return V2.Lerp(g.c[i] + g.t[i].Left * y, g.c[i + 1] + g.t[i + 1].Left * y, f);
        }
        double SurfH(RoadGeom g, double s, double y)
        {
            double f; int i = StationAt(g, s, out f);
            double h0 = RoadH(g, i, g.c[i] + g.t[i].Left * y), h1 = RoadH(g, i + 1, g.c[i + 1] + g.t[i + 1].Left * y);
            return h0 + (h1 - h0) * f;
        }

        /// <summary>차도 면(가장자리 −wR ~ +wL)을 표시 띠 경계로 나눠 칸마다 재질을 줌</summary>
        void PaintSurface(MeshData m, RoadGeom g, List<PaintBand> bands)
        {
            double L = g.Length;
            var S = new List<double>(g.cum);
            var live = new List<PaintBand>();
            foreach (var b in bands)
            {
                b.sA = Geom.Clamp(b.sA, 0, L); b.sB = Geom.Clamp(b.sB, 0, L);
                if (b.sB - b.sA > 1e-3) { live.Add(b); S.Add(b.sA); S.Add(b.sB); }
            }
            S.Sort();
            var SS = new List<double>();
            foreach (var x in S) if (SS.Count == 0 || x - SS[SS.Count - 1] > 1e-3) SS.Add(x);
            if (L - SS[SS.Count - 1] > 1e-6) SS.Add(L); else SS[SS.Count - 1] = L;
            int K = SS.Count; if (K < 2) return;
            var SM = new double[K - 1]; for (int k = 0; k + 1 < K; k++) SM[k] = (SS[k] + SS[k + 1]) * 0.5;
            Func<double, int> idx = x => { int lo = 0, hi = K - 1; while (hi - lo > 1) { int mid = (lo + hi) / 2; if (SS[mid] <= x + 1e-3) lo = mid; else hi = mid; } return Math.Abs(SS[hi] - x) < Math.Abs(SS[lo] - x) ? hi : lo; };
            // 경계 = 값 표(끊는 위치·가운데에서 미리 계산): 0 오른쪽 가장자리, 1 왼쪽 가장자리, 그다음 띠마다 lo, hi
            var ka = new List<int>(); var kb = new List<int>(); var vAt = new List<double[]>(); var vMid = new List<double[]>(); var owner = new List<int>();
            Action<int, int, Func<double, double>, int> addB = (a, b, f, own) =>
            {
                var va = new double[b - a + 1]; var vm = new double[Math.Max(0, b - a)];
                for (int k = a; k <= b; k++) va[k - a] = f(SS[k]);
                for (int k = a; k < b; k++) vm[k - a] = f(SM[k]);
                ka.Add(a); kb.Add(b); vAt.Add(va); vMid.Add(vm); owner.Add(own);
            };
            addB(0, K - 1, q => -Geom.AtVal(g.wR, g.cum, q), -1);
            addB(0, K - 1, q => Geom.AtVal(g.wL, g.cum, q), -1);
            var bandIdx = new int[live.Count, 2];
            for (int bi = 0; bi < live.Count; bi++)
            {
                int a = idx(live[bi].sA), b = idx(live[bi].sB); if (b <= a) { bandIdx[bi, 0] = -1; continue; }
                bandIdx[bi, 0] = ka.Count; addB(a, b, live[bi].lo, bi);
                bandIdx[bi, 1] = ka.Count; addB(a, b, live[bi].hi, bi);
            }
            int NB = ka.Count;
            Func<int, int, double> At = (bj, k) => vAt[bj][k - ka[bj]];
            // 구간별 경계 목록(구간 k 를 처음부터 끝까지 덮는 것)
            var inK = new List<int>[K - 1];
            for (int k = 0; k + 1 < K; k++) inK[k] = new List<int>();
            for (int bj = 0; bj < NB; bj++) for (int k = ka[bj]; k < kb[bj]; k++) inK[k].Add(bj);
            double[] eR = vAt[0], eLv = vAt[1];
            // 끊는 위치마다 점(양쪽 칸의 경계를 다 모아서 → 칸끼리 점을 같이 씀, 틈 없음)
            var U = new List<double>[K]; var vid = new List<int>[K];
            for (int k = 0; k < K; k++)
            {
                double yr = eR[k], yl = eLv[k];
                var ys = new List<double> { yr, yl };
                if (k > 0) foreach (var bj in inK[k - 1]) ys.Add(At(bj, k));
                if (k + 1 < K) foreach (var bj in inK[k]) ys.Add(At(bj, k));
                for (int i = 0; i < ys.Count; i++) ys[i] = Geom.Clamp(ys[i], yr, yl);
                ys.Sort();
                var u = new List<double>(); foreach (var y in ys) if (u.Count == 0 || y - u[u.Count - 1] > 1e-4) u.Add(y);
                U[k] = u; vid[k] = new List<int>(u.Count);
                foreach (var y in u) vid[k].Add(m.V(SurfP(g, SS[k], y), SurfH(g, SS[k], y)));
            }
            var A = new List<int>(); var tA = new List<double>(); var B = new List<int>(); var tB = new List<double>();
            for (int k = 0; k + 1 < K; k++)
            {
                var bl = inK[k]; int kk = k;
                bl.Sort((x, y) => vMid[x][kk - ka[x]].CompareTo(vMid[y][kk - ka[y]]));
                double r0 = eR[k], l0 = eLv[k], r1 = eR[k + 1], l1 = eLv[k + 1];
                for (int j = 0; j + 1 < bl.Count; j++)
                {
                    int flo = bl[j], fhi = bl[j + 1];
                    double a0 = Geom.Clamp(At(flo, k), r0, l0), b0 = Geom.Clamp(At(fhi, k), r0, l0), a1 = Geom.Clamp(At(flo, k + 1), r1, l1), b1 = Geom.Clamp(At(fhi, k + 1), r1, l1);
                    if (b0 - a0 < 1e-4 && b1 - a1 < 1e-4) continue;
                    A.Clear(); tA.Clear(); B.Clear(); tB.Clear();
                    for (int i = 0; i < U[k].Count; i++) { double y = U[k][i]; if (y >= a0 - 1e-4 && y <= b0 + 1e-4) { A.Add(vid[k][i]); tA.Add(b0 - a0 > 1e-6 ? (y - a0) / (b0 - a0) : 0.5); } }
                    for (int i = 0; i < U[k + 1].Count; i++) { double y = U[k + 1][i]; if (y >= a1 - 1e-4 && y <= b1 + 1e-4) { B.Add(vid[k + 1][i]); tB.Add(b1 - a1 > 1e-6 ? (y - a1) / (b1 - a1) : 0.5); } }
                    if (A.Count == 0 || B.Count == 0) continue;
                    // 칸 재질: 칸 가운데가 들어 있는 띠(우선순위 높은 것)
                    double ym = (vMid[flo][k - ka[flo]] + vMid[fhi][k - ka[fhi]]) * 0.5; string mat = "asphalt"; int pr = 0;
                    foreach (var bj in bl)
                    {
                        int own = owner[bj]; if (own < 0 || bandIdx[own, 0] != bj) continue;        // 띠마다 한 번(lo 쪽에서)
                        double lo = vMid[bj][k - ka[bj]], hi = vMid[bandIdx[own, 1]][k - ka[bandIdx[own, 1]]];
                        if (lo <= ym && ym <= hi && live[own].prio > pr) { pr = live[own].prio; mat = live[own].mat; }
                    }
                    int ia = 0, ib = 0;
                    while (ia < A.Count - 1 || ib < B.Count - 1)
                    {
                        bool advA = ib >= B.Count - 1 || (ia < A.Count - 1 && tA[ia + 1] <= tB[ib + 1]);
                        if (advA) { m.TriUp(mat, A[ia], B[ib], A[ia + 1]); ia++; }
                        else { m.TriUp(mat, A[ia], B[ib], B[ib + 1]); ib++; }
                    }
                }
            }
        }

        /// <summary>pred 가 참인 구간들(1m 간격으로 보고 경계는 반씩 나눠 2cm 까지)</summary>
        static List<double[]> Runs(double a, double b, Func<double, bool> pred)
        {
            var o = new List<double[]>(); if (b - a < 1e-6) return o;
            int n = Math.Max(1, (int)Math.Ceiling(b - a));
            double st = (b - a) / n, ra = double.NaN, prevX = a; bool prev = false;
            for (int i = 0; i <= n; i++)
            {
                double x = i == n ? b : a + i * st; bool e = pred(x);
                if (i == 0) { if (e) ra = a; }
                else if (e != prev)
                {
                    double lo = prevX, hi = x;
                    for (int k = 0; k < 6; k++) { double mid = (lo + hi) * 0.5; if (pred(mid) == prev) lo = mid; else hi = mid; }
                    double xb = (lo + hi) * 0.5;
                    if (e) ra = xb; else { if (!double.IsNaN(ra)) o.Add(new[] { ra, xb }); ra = double.NaN; }
                }
                prev = e; prevX = x;
            }
            if (!double.IsNaN(ra) && b - ra > 1e-6) o.Add(new[] { ra, b });
            return o;
        }

        /// <summary>터널(덮개 구간)·지하차도 안 진로변경 제한 백색 실선 구간(업무편람 4.차선(503) 해설: 교량·곡선구간·오르막 정상부·터널 내에서는
        /// 진로변경을 제한하는 차선을 설치). 덮개(2)가 낀 땅밑 구간 전체 + 입구 앞 tunnelSolidExtend m. 덮개 없는 개착(3)은 지하도로(002)일 때만.
        /// 교량은 실선 일괄 적용 안 함(광안대교도 분기부 480m 정도만 실선 — 실제 노면 따름)</summary>
        Road gwUp; bool gwUpDone;
        /// <summary>광안대교 위층(남천 방향, OSM layer 2) 도로 — 한 번만 찾음</summary>
        Road GwUpper()
        {
            if (!gwUpDone) { gwUpDone = true; Road u, l; if (Gwangan.Pair(net, out u, out l)) gwUp = u; }
            return gwUp;
        }
        List<double[]> NoChangeRuns(RoadGeom g)
        {
            var o = new List<double[]>();
            if (!P.tunnelSolidLines || g.tmode == null) return o;
            int n = g.tmode.Length, i = 0;
            while (i < n)
            {
                if (g.tmode[i] != 2 && g.tmode[i] != 3) { i++; continue; }
                int j = i; bool cov = false, sub = false;
                while (j < n && (g.tmode[j] == 2 || g.tmode[j] == 3)) { if (g.tmode[j] == 2) cov = true; if (g.type[j] == "002") sub = true; j++; }
                if (cov || sub)
                {
                    double a = g.cum[i] - P.tunnelSolidExtend, b = g.cum[j - 1] + P.tunnelSolidExtend;
                    if (o.Count > 0 && a <= o[o.Count - 1][1]) o[o.Count - 1][1] = b; else o.Add(new[] { a, b });
                }
                i = j;
            }
            return o;
        }

        /// <summary>점선: 끊김 없는 구간마다 처음·끝이 온전한 점선이 되게 점선·빈 길이를 같은 비율로 살짝 늘이고 줄임(끝에 조각이 안 남음).
        /// solidA~solidB 는 진로변경제한선(실선), 실선 앞뒤 점선 구간은 실선 쪽 끝을 빈 길이로 띄움</summary>
        void Dashed(MeshData m, RoadGeom g, string mat, double s0, double s1, Func<double, double> off, Func<double, bool> exists, double on, double offLen, double solidA, double solidB, List<double[]> extra = null)
        {
            if (s1 - s0 < 0.3) return;
            // 실선 구간: 정지선 앞(solidA~solidB) + extra(터널 안 등). 잘라 붙이고, 점선 한 칸도 안 되는 틈은 메움
            var sol = new List<double[]>();
            if (solidB > solidA) sol.Add(new[] { solidA, solidB });
            if (extra != null) foreach (var r in extra) sol.Add(new[] { r[0], r[1] });
            var cl = new List<double[]>();
            foreach (var r in sol) { double a = Math.Max(s0, r[0]), b = Math.Min(s1, r[1]); if (b - a > 0.2) cl.Add(new[] { a, b }); }
            cl.Sort((x, y) => x[0].CompareTo(y[0]));
            var mg = new List<double[]>();
            foreach (var r in cl)
            {
                if (mg.Count > 0 && r[0] <= mg[mg.Count - 1][1] + on + offLen * 2) mg[mg.Count - 1][1] = Math.Max(mg[mg.Count - 1][1], r[1]);
                else mg.Add(r);
            }
            if (mg.Count > 0 && mg[0][0] - s0 < on) mg[0][0] = s0;                                  // 끝에 점선 한 칸 못 남으면 실선으로
            if (mg.Count > 0 && s1 - mg[mg.Count - 1][1] < on) mg[mg.Count - 1][1] = s1;
            var parts = new List<double[]>();                                    // a, b, 앞 띄움, 뒤 띄움
            double cur = s0; bool after = false;
            foreach (var r in mg)
            {
                if (r[0] > cur + 1e-6) parts.Add(new[] { cur, r[0], after ? 1.0 : 0.0, 1.0 });
                SolidRuns(m, g, mat, r[0], r[1], off, exists, P.lineNoChangeW);
                cur = r[1]; after = true;
            }
            if (s1 > cur + 1e-6) parts.Add(new[] { cur, s1, after ? 1.0 : 0.0, 0.0 });
            foreach (var pt in parts)
                foreach (var run in Runs(pt[0], pt[1], exists))
                {
                    bool gs = pt[2] > 0 && Math.Abs(run[0] - pt[0]) < 1e-3, ge = pt[3] > 0 && Math.Abs(run[1] - pt[1]) < 1e-3;
                    DashRun(m, g, mat, run[0] + (gs ? offLen * 0.6 : 0), run[1] - (ge ? offLen * 0.6 : 0), off, on, offLen, P.lineLaneW);
                }
        }
        /// <summary>a~b 를 점선 n 개로 꽉 맞춤(점선·빈 길이 비율 유지, 0.7~1.4배 안에서)</summary>
        void DashRun(MeshData m, RoadGeom g, string mat, double a, double b, Func<double, double> off, double on, double offLen, double width)
        {
            double len = b - a; if (len < on * 0.5) return;
            int n = Math.Max(1, (int)Math.Round((len + offLen) / (on + offLen)));
            double f = len / (n * on + (n - 1) * offLen);
            while (n > 1 && f < 0.7) { n--; f = len / (n * on + (n - 1) * offLen); }
            if (n == 1)
            {
                double d = Math.Min(len, on * 1.3), c = (a + b) * 0.5;
                LineStrip(m, g, mat, c - d * 0.5, c + d * 0.5, off, width); return;
            }
            double dl = on * f, gl = offLen * f;
            for (int k = 0; k < n; k++) { double x = a + k * (dl + gl); LineStrip(m, g, mat, x, x + dl, off, width); }
        }
        /// <summary>실선(있는 구간만 이어서)</summary>
        void SolidRuns(MeshData m, RoadGeom g, string mat, double s0, double s1, Func<double, double> off, Func<double, bool> exists, double width)
        {
            foreach (var r in Runs(s0, s1, exists)) if (r[1] - r[0] > 0.2) LineStrip(m, g, mat, r[0], r[1], off, width);
        }

        /// <summary>끝선(한쪽, sign +1 왼쪽 wL / −1 오른쪽 wR): 인도(연석) 있는 구간은 도로의 끝선 종류, 없는 구간은 흰 실선(505)</summary>
        void EdgeLines(MeshData m, RoadGeom g, double a, double b, int sign, Func<double, double> w, Func<double, double> sh)
        {
            if (b - a < 0.2) return;
            var swA = sign > 0 ? g.swL : g.swR;
            Func<double, bool> curb = s => Geom.AtVal(swA, g.cum, s) > 1e-3;
            double ew = P.lineEdgeW;
            foreach (var r in Runs(a, b, s => !curb(s))) if (r[1] - r[0] > 0.2) LineStrip(m, g, "mark_white", r[0], r[1], s => sign * (w(s) - sh(s)), ew);
            int t = g.edgeType;
            if (t == EdgeLine.None) return;
            foreach (var r in Runs(a, b, curb))
            {
                if (r[1] - r[0] < 0.2) continue;
                if (t == EdgeLine.White) { LineStrip(m, g, "mark_white", r[0], r[1], s => sign * (w(s) - sh(s)), ew); continue; }
                // 황색 정차·주차금지선: 측구 바로 옆(5cm), 흰 가장자리선은 안 그림
                double gy = P.gutterWidth + 0.05;
                if (t == EdgeLine.YellowDouble)
                {
                    double w1 = P.lineParkDoubleW, o1 = gy + w1 * 0.5, o2 = gy + w1 * 1.5 + P.parkDoubleGap;
                    LineStrip(m, g, "mark_yellow", r[0], r[1], s => sign * (w(s) - o1), w1);
                    LineStrip(m, g, "mark_yellow", r[0], r[1], s => sign * (w(s) - o2), w1);
                }
                else
                {
                    double w1 = P.lineParkW, o = gy + w1 * 0.5;
                    if (t == EdgeLine.Yellow) LineStrip(m, g, "mark_yellow", r[0], r[1], s => sign * (w(s) - o), w1);
                    else DashRun(m, g, "mark_yellow", r[0], r[1], s => sign * (w(s) - o), P.parkDashOn, P.parkDashOff, w1);
                }
            }
        }

        void Markings(MeshData m, RoadGeom g)
        {
            var R = g.road; double L = g.Length;
            Func<double, double> sO = s => Geom.AtVal(g.sOrig, g.cum, s);
            Func<double, int> lf = s => R.LanesAt(R.fwd, sO(s)), lb = s => R.LanesAt(R.bwd, sO(s));
            Func<double, double> lw = s => { var pc = PieceAt(R, sO(s)); return P.LaneWidth(pc != null && !string.IsNullOrEmpty(pc.rank) ? pc.rank : R.rank, pc != null && pc.spd > 0 ? pc.spd : 50); };
            Func<double, double> sh = s => Geom.AtVal(g.sh, g.cum, s);
            Func<double, double> wL = s => Geom.AtVal(g.wL, g.cum, s), wR = s => Geom.AtVal(g.wR, g.cum, s);
            Func<double, double> gutL = s => Geom.AtVal(g.swL, g.cum, s) > 1e-3 ? P.gutterWidth : 0, gutR = s => Geom.AtVal(g.swR, g.cum, s) > 1e-3 ? P.gutterWidth : 0;
            bool express = P.IsExpress(R.rank) || R.divided;
            double on = express ? P.dashExpressOn : P.dashUrbanOn, offL = express ? P.dashExpressOff : P.dashUrbanOff;
            // 양 끝: 횡단보도·정지선·줄 시작 위치
            Arm aS = R.armStart != null && R.armStart.cut > 0 ? R.armStart : null, aE = R.armEnd != null && R.armEnd.cut > 0 ? R.armEnd : null;
            double slw = P.stopLineWidth;
            // 차선: 들어오는 쪽은 정지선까지, 나가는 쪽은 횡단보도 지나서. 횡단보도 없는 입구는 입구(교차로 경계)까지
            Func<Arm, double> inClear = A => A == null ? 0.0 : A.crosswalk ? (A.stopDist > 0 ? A.stopDist + slw * 0.5 : A.crosswalkWidth + 0.5) : 0.0;
            Func<Arm, double> outClear = A => A == null ? 0.0 : A.crosswalk ? A.crosswalkWidth + 0.5 : 0.0;
            double sIn0 = inClear(aS), sOut0 = outClear(aS), sIn1 = L - inClear(aE), sOut1 = L - outClear(aE);
            // 끝선: 들어오는 쪽은 정지선에서 끝, 나가는 쪽은 횡단보도 바로 뒤부터 — 횡단보도 없으면 입구까지(교차로 모서리 선과 이어짐)
            Func<Arm, double> edgeIn = A => A == null ? 0.0 : A.crosswalk ? (A.stopDist > 0 ? A.stopDist : A.crosswalkWidth) : 0.0;
            Func<Arm, double> edgeOut = A => A == null ? 0.0 : A.crosswalk ? A.crosswalkWidth : 0.0;
            double eIn0 = edgeIn(aS), eOut0 = edgeOut(aS), eIn1 = L - edgeIn(aE), eOut1 = L - edgeOut(aE);
            // 중앙선: 횡단보도 앞까지(편람 그림 3-46), 횡단보도 없으면 입구까지. centerLineThroughCrosswalk 면 횡단보도 가운데 틈을 지나 입구까지
            Func<Arm, double> cEnd = A => A == null ? 0.0 : A.crosswalk && !P.centerLineThroughCrosswalk ? A.crosswalkWidth : 0.0;
            double solid = P.solidBeforeStop;
            var nc = NoChangeRuns(g);                                            // 터널·지하차도 안 진로변경 제한(실선)
            // 광안대교 위층(남천 방향) 분기 앞: 1·2차로(남구 방면)와 3·4차로(수영구 방면)가 갈라지는 곳 앞 약 480m 는 2·3차로 사이 백색 실선
            int gwK = -1; double gwA = 0, gwB = 0;
            if (P.gwEnable && P.gwSplitSolid > 0 && R.oneWay && R.fwd.Count > 0 && R == GwUpper())
                for (int i = g.c.Count - 1; i > 0; i--)
                {
                    int n0 = lf(g.cum[i - 1]), n1 = lf(g.cum[i]);
                    if (n1 < n0 && n0 >= 3 && n1 >= 1) { gwK = n0 - n1; gwB = g.cum[i - 1]; gwA = gwB - P.gwSplitSolid; break; }
                }
            Func<int, List<double[]>> ncK = k => { if (k != gwK) return nc; var l = new List<double[]>(nc); l.Add(new[] { gwA, gwB }); return l; };
            // 측구(물 빠지는 곳): 연석 앞 차도 끝 gutterWidth — 인도(연석)가 있는 구간만
            for (int side = 0; side < 2; side++)
            {
                var sw = side == 0 ? g.swL : g.swR; int a = -1;
                for (int i = 0; i <= sw.Length; i++)
                {
                    bool has = i < sw.Length && sw[i] > 0;
                    if (has && a < 0) a = i;
                    if (!has && a >= 0)
                    {
                        double s0 = g.cum[a], s1 = g.cum[Math.Min(i, sw.Length - 1)];
                        if (side == 0) paint.Add(new PaintBand { sA = s0, sB = s1, lo = q => wL(q) - P.gutterWidth, hi = q => wL(q), mat = "gutter", prio = 1 });
                        else paint.Add(new PaintBand { sA = s0, sB = s1, lo = q => -wR(q), hi = q => -wR(q) + P.gutterWidth, mat = "gutter", prio = 1 });
                        a = -1;
                    }
                }
            }

            double half = R.oneWay ? 0 : R.divided ? P.medianHalf : P.centerHalfGap;
            Func<double, bool> dbl = s => Math.Max(lf(s), lb(s)) >= 2;
            // 정지선 안쪽 끝 = 중앙선 바깥 가장자리(업무편람: 중앙선 ~ 길 끝)
            Func<double, double> centerEdge = s => R.divided ? half - 0.2 + P.lineCenterW * 0.5 : dbl(s) ? P.doubleGap * 0.5 + P.lineCenterDoubleW : P.lineCenterW * 0.5;
            if (R.oneWay)
            {   // 일방: 차로를 가운데 기준 양쪽으로, 끝선은 양쪽
                bool fw = R.fwd.Count > 0; Func<double, int> nl = s => Math.Max(1, fw ? lf(s) : lb(s));
                double a = fw ? sOut0 : sIn0, b = fw ? sIn1 : sOut1;
                for (int k = 1; k < 8; k++)
                {
                    int kk = k;
                    Dashed(m, g, "mark_white", a, b, s => -nl(s) * lw(s) * 0.5 + kk * lw(s), s => kk < nl(s), on, offL,
                        fw ? (aE != null && aE.stopDist > 0 ? sIn1 - solid : 0) : sIn0, fw ? (aE != null && aE.stopDist > 0 ? sIn1 : 0) : (aS != null && aS.stopDist > 0 ? sIn0 + solid : 0), ncK(kk));
                }
                double ea = fw ? eOut0 : eIn0, eb = fw ? eIn1 : eOut1;
                EdgeLines(m, g, ea, eb, +1, wL, sh);
                EdgeLines(m, g, ea, eb, -1, wR, sh);
            }
            else
            {
                // 중앙선
                double c0 = cEnd(aS), c1 = L - cEnd(aE);
                if (R.divided)
                {   // 중앙분리대가 있는 편도 2차로 이상 고속도로·자동차전용도로: 분리대 옆은 흰색 실선(업무편람 중앙선 501)
                    LineStrip(m, g, "mark_white", c0, c1, s => half - 0.2, P.lineCenterW);
                    LineStrip(m, g, "mark_white", c0, c1, s => -(half - 0.2), P.lineCenterW);
                }
                else
                {   // 편도 2차로 이상 황색 복선(10~15cm, 간격 10~15cm), 아니면 황색 실선(15~20cm)
                    double o = P.doubleGap * 0.5 + P.lineCenterDoubleW * 0.5;
                    SolidRuns(m, g, "mark_yellow", c0, c1, s => o, dbl, P.lineCenterDoubleW);
                    SolidRuns(m, g, "mark_yellow", c0, c1, s => -o, dbl, P.lineCenterDoubleW);
                    SolidRuns(m, g, "mark_yellow", c0, c1, s => 0, s => !dbl(s), P.lineCenterW);
                }
                // 오른쪽(정방향): 시작 끝에선 나감, 끝 쪽에선 들어옴
                double rSolidA = aE != null && aE.stopDist > 0 ? sIn1 - solid : 0, rSolidB = aE != null && aE.stopDist > 0 ? sIn1 : 0;
                double lSolidA = aS != null && aS.stopDist > 0 ? sIn0 : 0, lSolidB = aS != null && aS.stopDist > 0 ? sIn0 + solid : 0;
                for (int k = 1; k < 8; k++)
                {
                    int kk = k;
                    Dashed(m, g, "mark_white", sOut0, sIn1, s => -(half + kk * lw(s)), s => kk < lf(s), on, offL, rSolidA, rSolidB, nc);
                    Dashed(m, g, "mark_white", sIn0, sOut1, s => half + kk * lw(s), s => kk < lb(s), on, offL, lSolidA, lSolidB, nc);
                }
                EdgeLines(m, g, eOut0, eIn1, -1, wR, sh);
                EdgeLines(m, g, eIn0, eOut1, +1, wL, sh);
            }
            // 횡단보도(입구 바로 바깥) + 들어오는 쪽 정지선
            for (int e = 0; e < 2; e++)
            {
                Arm A = e == 0 ? aS : aE; if (A == null || !A.crosswalk) continue;
                double cw = A.crosswalkWidth;
                double sEnd = e == 0 ? 0.0 : L;
                // 줄무늬는 끝선(가장자리선 바깥 가장자리)까지만 — 그 안을 줄 폭·간격을 조금씩 맞춰 채움
                double yL = wL(sEnd) - sh(sEnd) + P.lineEdgeW * 0.5, yR = -(wR(sEnd) - sh(sEnd) + P.lineEdgeW * 0.5);
                string mat = A.crosswalkYellow ? "mark_yellow" : "mark_white";
                double near = e == 0 ? 0.0 : L - cw;                     // 교차로 쪽 끝(띠 시작 s)
                if (A.crosswalkKind == CrosswalkKind.Split)
                {   // 2등분(붙이거나 crosswalkSplitGap 띄움), 두 줄 엇갈림(1010/0101). 보행자 우측통행: 왼쪽(+) 연석에서 출발하는 사람은 s 가 작은 쪽 줄
                    // 줄무늬 자리는 차도 전체로 한 번 정하고, 줄마다 출발 쪽 끝(화살표 자리)만 비움
                    double gp = P.crosswalkSplitGap, hw = (cw - gp) * 0.5;
                    double b0a = near, b0b = near + hw, b1a = near + hw + gp, b1b = near + cw;
                    double ar = P.crosswalkArrows ? P.crosswalkArrowLen + 0.4 : 0;
                    Zebra(m, g, mat, b0a, b0b, yR, yL, !R.oneWay, 0, e == 0, yL - ar, yL);   // 작은 s 줄: 왼쪽(+)에서 출발 → 오른쪽(−)
                    Zebra(m, g, mat, b1a, b1b, yR, yL, !R.oneWay, 1, e == 0, yR, yR + ar);   // 큰 s 줄: 오른쪽(−)에서 출발 → 왼쪽(+), 반 칸 밀어 엇갈림
                    if (P.crosswalkArrows)
                        foreach (double f in new[] { 0.28, 0.72 })
                        {   // 화살표: 줄마다 출발 쪽에만 2개(나란히), 건너가는 방향 — 도착 쪽엔 없음
                            Arrow(m, g, mat, b0a + (b0b - b0a) * f, yL - 0.2, -1);
                            Arrow(m, g, mat, b1a + (b1b - b1a) * f, yR + 0.2, +1);
                        }
                }
                else Zebra(m, g, mat, near, near + cw, yR, yL, !R.oneWay, 0, e == 0);   // 들어오는 쪽(정지선 쪽) 반에 맞춤
                if (A.stopDist <= 0) continue;
                double sc = e == 0 ? A.stopDist : L - A.stopDist;
                // 정지선: 중앙선 가장자리 ~ 측구 앞(아스팔트 끝)까지. 일방은 차도 전체
                double inner = R.oneWay ? 0 : centerEdge(sc);
                if (e == 0) QuadAcross(m, g, "mark_white", sc - slw / 2, sc + slw / 2, R.oneWay ? -(wR(sc) - gutR(sc)) : inner, wL(sc) - gutL(sc));
                else QuadAcross(m, g, "mark_white", sc - slw / 2, sc + slw / 2, -(wR(sc) - gutR(sc)), R.oneWay ? wL(sc) - gutL(sc) : -inner);
                // 진행방향 화살표(537~539): 정지선에서 2~3m 앞(올라오는 쪽), 차로 가운데. 차로별 방향은 교차로 팔 모양 + 노드링크 회전 금지
                if (P.laneArrows)
                {
                    int sig = e == 0 ? -1 : +1;                                     // 들어오는 차의 진행 방향(s 증가 = +1)
                    int mv = Movements(A); if (mv == 0) continue;
                    Func<double, int> nIn = q => R.oneWay ? Math.Max(1, R.fwd.Count > 0 ? lf(q) : lb(q)) : (e == 0 ? lb(q) : lf(q));
                    int n = nIn(sc); if (n <= 0) continue;
                    var masks = LaneMasks(n, mv);
                    for (int rep = 0; rep < 2; rep++)
                    {
                        double sTip = sc - sig * (slw * 0.5 + P.laneArrowGap + rep * P.laneArrowRepeat);
                        double sTail = sTip - sig * P.laneArrowLen;
                        if (rep > 0 && (P.laneArrowRepeat <= 0 || nIn(sTail) != n || nIn(sTip) != n)) break;
                        double lo = Math.Min(sTip, sTail), hi = Math.Max(sTip, sTail);
                        if (lo < 2 || hi > L - 2) break;
                        // 반대쪽 끝 횡단보도·정지선 구간은 피함
                        Arm other = e == 0 ? aE : aS; double oc = other != null && other.crosswalk ? (other.stopDist > 0 ? other.stopDist : other.crosswalkWidth) + 5 : 5;
                        if (e == 0 ? hi > L - oc : lo < oc) break;
                        double lwv = lw(sc);
                        for (int k = 1; k <= n; k++)
                        {
                            double yc = R.oneWay ? sig * (n * lwv * 0.5 - (k - 0.5) * lwv) : (e == 0 ? 1 : -1) * (half + (k - 0.5) * lwv);
                            LaneArrow(sTail, sig, yc, masks[k - 1]);
                        }
                    }
                }
            }
            // 노면 글자: 제한속도 숫자(517, 자동) + 직접 넣은 글자(덮어쓰기 texts) — 들어온 쪽 횡단보도 지나서 at m, 차로마다
            if (P.roadText)
            {
                var ro = ov.Road(R);
                for (int dsig = -1; dsig <= 1; dsig += 2)
                {
                    if (R.oneWay ? (dsig > 0) != (R.fwd.Count > 0) : (dsig > 0 ? R.fwd.Count : R.bwd.Count) == 0) continue;
                    var ps = dsig > 0 ? R.fwd : R.bwd;
                    Arm aIn = dsig > 0 ? aS : aE, aOut = dsig > 0 ? aE : aS;        // 들어온 쪽(뒤) / 가는 쪽(앞)
                    double entry = outClear(aIn), stopBack = inClear(aOut);          // 그 방향으로 잰 거리
                    double arrowsBack = P.laneArrows && aOut != null && aOut.stopDist > 0 ? stopBack + P.laneArrowGap + P.laneArrowLen + Math.Max(0, P.laneArrowRepeat) + 3 : stopBack + 3;
                    Func<double, double> S = d => dsig > 0 ? d : L - d;              // 그 방향 거리 → s
                    var marks = new List<RoadTextMark>(); RoadTextMark auto = null;
                    bool autoSpeed = ro != null && ro.speedMark >= 0 ? ro.speedMark == 1 : P.speedMarks;
                    if (autoSpeed && L >= P.speedMarkMinLen)
                    {
                        double so = sO(S(entry + P.speedMarkAt)); int spd = 0;
                        foreach (var pc in ps) if (so >= pc.s0 && so <= pc.s1) { spd = pc.spd; break; }
                        if (spd <= 0 && ps.Count > 0) spd = ps[0].spd;
                        if (spd >= 10 && spd <= 110) marks.Add(auto = new RoadTextMark { text = spd.ToString(), dir = dsig, at = P.speedMarkAt });
                    }
                    if (ro != null) foreach (var t in ro.texts) if (t != null && t.dir == dsig && !string.IsNullOrEmpty(t.text)) marks.Add(t);
                    foreach (var t in marks)
                    {
                        double probe = t.at >= 0 ? entry + t.at : L - stopBack + t.at;
                        double sProbe = S(Geom.Clamp(probe, 0, L)), lwv = lw(sProbe);
                        int n = Math.Max(1, dsig > 0 ? lf(sProbe) : lb(sProbe));
                        var lay = TextLayout(t.text, lwv); if (lay == null) { log.Add("노면 글자 못 씀(모르는 글자): " + t.text); continue; }
                        double dNear = t.at >= 0 ? entry + t.at : L - stopBack + t.at - lay.len, dFar = dNear + lay.len;
                        if (dNear < entry + 0.5 || dFar > L - (t == auto ? arrowsBack : stopBack + 1)) continue;   // 자리 없음
                        if ((dsig > 0 ? lf(S(dFar)) : lb(S(dFar))) != (dsig > 0 ? lf(S(dNear)) : lb(S(dNear)))) continue;   // 차로 수 바뀌는 곳은 피함
                        for (int k = 1; k <= n; k++)
                        {
                            if (t.lane > 0 && t.lane != k) continue;
                            double yc = R.oneWay ? dsig * (n * lwv * 0.5 - (k - 0.5) * lwv) : -dsig * (half + (k - 0.5) * lwv);
                            var polys = new List<List<V2>>();
                            foreach (var pl in lay.polys) { var q = new List<V2>(pl.Count); foreach (var v in pl) q.Add(new V2(S(dNear + v.z), yc + dsig * v.x)); polys.Add(q); }
                            PaintPolys(polys, "mark_white", 3); textCount++;
                            textSpots.Add(t.text + "@" + SurfP(g, S(dNear + lay.len * 0.5), yc).ToString());
                        }
                    }
                }
            }
        }

        // ---------- 노면 글자(RoadText.cs 글꼴 → 도로 면에 짜 넣기) ----------
        public int textCount; public List<string> textSpots = new List<string>();
        sealed class TextLay { public readonly List<List<V2>> polys = new List<List<V2>>(); public double len, width; }   // 점 = (x 운전자 왼쪽 +, z 앞으로)
        readonly Dictionary<string, TextLay> textCache = new Dictionary<string, TextLay>();
        /// <summary>글자를 차로 폭에 맞춰 배치(한 줄로 너무 좁으면 두 줄). 결과 원점 = 운전자 쪽 끝 가운데</summary>
        TextLay TextLayout(string text, double laneW)
        {
            string key = text + "|" + laneW.ToString("0.00");
            TextLay lay; if (textCache.TryGetValue(key, out lay)) return lay;
            foreach (char ch in text) if (ch != '/' && !RoadFont.CanDraw(ch)) { textCache[key] = null; return null; }
            var st = P.TextStyle();
            double avail = Math.Max(0.5, laneW - 2 * P.textLaneClear), cellW = st.W + 2 * st.M;
            var lines = SplitLines(text, Math.Max(1, P.textMaxPerLine));
            double mw = 1; foreach (var l in lines) { double w, h; RoadFont.Line(l, st, out w, out h); mw = Math.Max(mw, w); }
            double u = Math.Min(P.textCharW / cellW, avail / mw);                      // 같은 크기, 좁은 차로만 줄임
            var order = new List<string>(lines); if (P.textFirstLineFar) order.Reverse();     // 운전자 쪽 줄부터 쌓음
            lay = new TextLay(); double k = Math.Max(0.2, P.textElong), b0 = 0;
            foreach (var l in order)
            {
                double w, h; var polys = RoadFont.Line(l, st, out w, out h);
                foreach (var p in polys) { var q = new List<V2>(p.Count); foreach (var v in p) q.Add(new V2((w * 0.5 - v.x) * u, b0 + v.z * u * k)); lay.polys.Add(q); }
                b0 += h * u * k; lay.width = Math.Max(lay.width, w * u);
            }
            lay.len = b0; textCache[key] = lay; return lay;
        }

        /// <summary>줄 나누기: '/' 는 그대로, 한 줄이 max 자를 넘으면 'OO보호구역' 은 뜻대로, 그 밖은 고르게</summary>
        static List<string> SplitLines(string text, int max)
        {
            var o = new List<string>();
            foreach (var part in text.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries))
            {
                string t = part.Trim(); if (t.Length == 0) continue;
                if (t.Length > max && t.EndsWith("보호구역") && t.Length > 4) { o.AddRange(SplitLines(t.Substring(0, t.Length - 4), max)); o.Add("보호구역"); continue; }
                int n = (t.Length + max - 1) / max, at = 0;
                for (int i = 0; i < n; i++) { int len = (t.Length - at + (n - i) - 1) / (n - i); o.Add(t.Substring(at, len)); at += len; }
            }
            return o;
        }

        sealed class TEdge { public int id, w; public double s0, y0, s1, y1; public double Y(double s) { return s1 - s0 > 1e-12 ? y0 + (y1 - y0) * (s - s0) / (s1 - s0) : y0; } }
        /// <summary>다각형들(점 = (s, y))의 합친 모양을 s 방향 띠(사다리꼴)로 나눠 도로 면 표시로 — 겹친 곳·구멍 없이 정확히</summary>
        void PaintPolys(List<List<V2>> polys, string mat, int prio)
        {
            var E = new List<TEdge>(); var xs = new List<double>();
            foreach (var p0 in polys)
            {
                if (p0.Count < 3) continue;
                var p = p0; if (Geom.SignedArea(p) < 0) { p = new List<V2>(p0); p.Reverse(); }
                for (int i = 0; i < p.Count; i++)
                {
                    V2 a = p[i], b = p[(i + 1) % p.Count]; xs.Add(a.x);
                    if (Math.Abs(b.x - a.x) < 1e-7) continue;
                    E.Add(a.x < b.x ? new TEdge { id = E.Count, s0 = a.x, y0 = a.z, s1 = b.x, y1 = b.z, w = 1 } : new TEdge { id = E.Count, s0 = b.x, y0 = b.z, s1 = a.x, y1 = a.z, w = -1 });
                }
            }
            for (int i = 0; i < E.Count; i++)
                for (int j = i + 1; j < E.Count; j++)
                {
                    TEdge a = E[i], b = E[j]; double lo = Math.Max(a.s0, b.s0), hi = Math.Min(a.s1, b.s1); if (hi - lo < 1e-7) continue;
                    double d0 = a.Y(lo) - b.Y(lo), d1 = a.Y(hi) - b.Y(hi);
                    if (d0 * d1 < 0) xs.Add(lo + (hi - lo) * d0 / (d0 - d1));
                }
            xs.Sort();
            var X = new List<double>(); foreach (var x in xs) if (X.Count == 0 || x - X[X.Count - 1] > 1e-5) X.Add(x);
            var open = new Dictionary<long, PaintBand>();
            var act = new List<TEdge>();
            for (int k = 0; k + 1 < X.Count; k++)
            {
                double sa = X[k], sb = X[k + 1], sm = (sa + sb) * 0.5;
                act.Clear(); foreach (var e in E) if (e.s0 <= sm && e.s1 >= sm) act.Add(e);
                act.Sort((p, q) => p.Y(sm).CompareTo(q.Y(sm)));
                var next = new Dictionary<long, PaintBand>(); int cnt = 0; TEdge lo = null;
                foreach (var e in act)
                {
                    int before = cnt; cnt += e.w;
                    if (before == 0 && cnt != 0) lo = e;
                    else if (before != 0 && cnt == 0 && lo != null)
                    {
                        long key = (long)lo.id * 1000003L + e.id; PaintBand b;
                        if (open.TryGetValue(key, out b)) b.sB = sb;
                        else { TEdge le = lo, he = e; b = new PaintBand { sA = sa, sB = sb, lo = q => le.Y(q), hi = q => he.Y(q), mat = mat, prio = prio }; paint.Add(b); }
                        next[key] = b; lo = null;
                    }
                }
                open = next;
            }
        }

        // ---------- 진행방향 화살표 ----------
        Dictionary<string, Road> linkRoad;
        /// <summary>팔 A 로 들어온 차가 갈 수 있는 방향: 1 좌회전, 2 직진, 4 우회전(팔 각도로, 노드링크 회전정보 003·101·102·103 금지는 뺌)</summary>
        public int Movements(Arm A)
        {
            if (linkRoad == null)
            {
                linkRoad = new Dictionary<string, Road>();
                foreach (var r in net.roads) { foreach (var l in r.fwdLinks) linkRoad[l] = r; foreach (var l in r.bwdLinks) linkRoad[l] = r; }
            }
            var banned = new HashSet<Road>();
            var inL = A.atStart ? A.road.bwdLinks : A.road.fwdLinks; string appr = inL.Count > 0 ? inL[inL.Count - 1] : null;
            if (appr != null)
                foreach (var t in net.turns)
                    if (t.from == appr && (t.type == "003" || t.type == "101" || t.type == "102" || t.type == "103")) { Road rr; if (t.to != null && linkRoad.TryGetValue(t.to, out rr)) banned.Add(rr); }
            V2 d = -A.dir; int mask = 0;
            foreach (var X in A.j.arms)
            {
                if (X == A || X.lanesOut <= 0 || banned.Contains(X.road)) continue;
                double th = Math.Atan2(V2.Cross(d, X.dir), V2.Dot(d, X.dir)) * 180 / Math.PI;
                if (Math.Abs(th) < 30) mask |= 2; else if (th >= 30 && th <= 160) mask |= 1; else if (th <= -30 && th >= -160) mask |= 4;
            }
            return mask;
        }
        /// <summary>차로별 방향(1차로 = 중앙선 쪽): 직진이 있으면 1차로 좌회전(편도 leftOnlyMinLanes 차로 이상은 좌회전 전용, 아니면 직진+좌), 끝 차로 직진+우, 나머지 직진.
        /// 직진이 없으면(T자) 왼쪽 반 좌회전, 오른쪽 반 우회전. 1차로는 갈 수 있는 방향 다</summary>
        int[] LaneMasks(int n, int mv)
        {
            var o = new int[n];
            if (n == 1) { o[0] = mv; return o; }
            bool L = (mv & 1) != 0, S = (mv & 2) != 0, Rr = (mv & 4) != 0;
            if (S)
            {
                for (int k = 0; k < n; k++) o[k] = 2;
                if (L) o[0] = n >= P.leftOnlyMinLanes ? 1 : 3;
                if (Rr) o[n - 1] |= 4;
                if (n == 2 && L && Rr) { o[0] = 3; o[1] = 6; }
                return o;
            }
            if (L && Rr) { int nl = (n + 1) / 2; for (int k = 0; k < n; k++) o[k] = k < nl ? 1 : 4; return o; }
            for (int k = 0; k < n; k++) o[k] = mv;
            return o;
        }
        /// <summary>진행방향 화살표 하나(짜 넣기): 꼬리 sTail 에서 진행 방향(sig)으로 laneArrowLen, 차로 가운데 yc. mask 1 좌 2 직진 4 우</summary>
        void LaneArrow(double sTail, int sig, double yc, int mask)
        {
            double Lr = P.laneArrowLen, sw = P.laneArrowShaftW * 0.5, hw = P.laneArrowHeadW * 0.5, hl = P.laneArrowHeadLen, sl = P.laneArrowSideLen;
            Func<double, double> S = a => sTail + sig * a;                  // 앞으로 a → 도로 s
            Func<double, double> Y = b => yc + sig * b;                     // 운전자 왼쪽 b → 도로 y(진행 +s 면 왼쪽 = +y)
            Action<double, double, double, double> rect = (a0, a1, b0, b1) =>
            {
                double s0 = S(a0), s1 = S(a1), y0 = Y(b0), y1 = Y(b1), ylo = Math.Min(y0, y1), yhi = Math.Max(y0, y1);
                paint.Add(new PaintBand { sA = Math.Min(s0, s1), sB = Math.Max(s0, s1), lo = q => ylo, hi = q => yhi, mat = "mark_white", prio = 3 });
            };
            Action<double, double> headFwd = (a0, a1) =>
            {   // 앞 머리: 밑변 a0(폭 2hw) → 꼭지 a1
                double s0 = S(a0), s1 = S(a1);
                Func<double, double> h = q => hw * Geom.Clamp((a1 - sig * (q - sTail)) / (a1 - a0), 0, 1);
                paint.Add(new PaintBand { sA = Math.Min(s0, s1), sB = Math.Max(s0, s1), lo = q => yc - h(q), hi = q => yc + h(q), mat = "mark_white", prio = 3 });
            };
            Action<double, int> branch = (ab, dir) =>
            {   // 옆 가지: 줄기에서 dir(+1 왼쪽, −1 오른쪽)으로 막대 + 옆을 보는 머리(가운데 ab, 반폭 hw)
                double bb = 0.45 * dir;
                rect(ab - sw, ab + sw, Math.Min(0, bb), Math.Max(0, bb));
                for (int h = 0; h < 2; h++)
                {
                    double aA = h == 0 ? ab - hw : ab, aB = h == 0 ? ab : ab + hw, sA = S(aA), sB = S(aB);
                    Func<double, double> ext = q => sl * Math.Max(0, 1 - Math.Abs(sig * (q - sTail) - ab) / hw);
                    Func<double, double> y0 = q => Y(bb), y1 = q => Y(bb + dir * ext(q));
                    paint.Add(new PaintBand { sA = Math.Min(sA, sB), sB = Math.Max(sA, sB), lo = q => Math.Min(y0(q), y1(q)), hi = q => Math.Max(y0(q), y1(q)), mat = "mark_white", prio = 3 });
                }
            };
            bool L = (mask & 1) != 0, St = (mask & 2) != 0, Rr = (mask & 4) != 0;
            if (St)
            {
                rect(0, Lr - hl, -sw, sw); headFwd(Lr - hl, Lr);
                double ab = (Lr - hl) * 0.55;
                if (L) branch(ab, +1);
                if (Rr) branch(ab, -1);
            }
            else
            {
                double ab = Lr - hw - 0.05;
                rect(0, ab + sw, -sw, sw);
                if (L) branch(ab, +1);
                if (Rr) branch(ab, -1);
            }
        }

        /// <summary>줄 폭(45~50cm)·간격(≈ 1.5배) 고르기: 맞출 쪽 폭 hFit 에 꼭 맞추고(첫 줄·마지막 줄이 양 끝에 붙음),
        /// 같은 줄·간격으로 반대쪽 hOther 를 채울 때 남는 빈자리가 가장 작게. 반대쪽이 없으면 hOther = 0</summary>
        void StripeLayout(double hFit, double hOther, out double st, out double gap, out int nFit, out int nOther)
        {
            double smax = P.crosswalkStripe, smin = Math.Min(P.crosswalkStripeMin, smax), ratio = P.crosswalkGap / Math.Max(1e-6, P.crosswalkStripe);
            st = smax; gap = P.crosswalkGap; nFit = hFit >= smin ? 1 : 0; nOther = 0;
            double best = double.MaxValue;
            for (int si = 0; si <= 10; si++)
            {
                double sw = smin + (smax - smin) * si / 10.0;
                int n0 = (int)Math.Round((hFit + sw * ratio) / (sw * (1 + ratio)));
                for (int n = Math.Max(1, n0 - 1); n <= n0 + 1; n++)
                {
                    if (hFit < sw) continue;
                    double gp = n > 1 ? (hFit - n * sw) / (n - 1) : sw * ratio, r = gp / sw;
                    if (n > 1 && (r < ratio * 0.8 || r > ratio * 1.25)) continue;
                    if (n == 1 && hFit > sw * (1 + ratio * 1.25)) continue;
                    int no = hOther >= sw ? (int)Math.Floor((hOther + gp) / (sw + gp) + 1e-9) : 0;
                    double left = hOther > 0 ? hOther - (no > 0 ? no * sw + (no - 1) * gp : 0) : 0;
                    double score = left + Math.Abs(r - ratio) * 0.5 + (n == 1 ? hFit - sw : 0);
                    if (score < best) { best = score; st = sw; gap = gp; nFit = n; nOther = no; }
                }
            }
        }
        /// <summary>a 에서 바깥(+)으로 줄 n 개(폭 st, 간격 gap). phase 1 = 빈칸 가운데에(2등분 횡단보도 두 번째 줄, 1010/0101)</summary>
        static List<double[]> StripesFrom(double a, double st, double gap, int n, int phase)
        {
            var o = new List<double[]>();
            for (int k = 0; k < n; k++)
            {
                double x = a + k * (st + gap);
                if (phase == 0) o.Add(new[] { x, x + st });
                else if (k + 1 < n) { double c = x + st + gap * 0.5; o.Add(new[] { c - st * 0.5, c + st * 0.5 }); }
            }
            return o;
        }

        /// <summary>줄무늬(막대는 도로 방향으로 길게, 옆으로 줄지어): s 구간 sA~sB, 옆 yR(−)~yL(+).
        /// 양방향 도로는 중앙선 위(±crosswalkCenterGap/2)를 비우고(업무편람 그림 3-46) 한쪽(fitLeft: 왼쪽 + / 아니면 오른쪽 −)에 꼭 맞춘 줄·간격을
        /// 반대쪽에도 그대로 씀(양쪽이 같은 무늬, 반대쪽 끝에만 빈자리가 남을 수 있음). 일방은 차도 전체를 양 끝에 맞춤.
        /// phase 1(2등분 두 번째 줄)은 빈칸 가운데에 — 가운데(중앙선 연장) 빈칸에도 하나 넣어 엇갈림이 끊기지 않게. clr0~clr1 에 걸치는 줄은 뺌(화살표 자리)</summary>
        void Zebra(MeshData m, RoadGeom g, string mat, double sA, double sB, double yR, double yL, bool centerGap, int phase, bool fitLeft, double clr0 = 0, double clr1 = 0)
        {
            double st, gap; int nF, nO;
            Action<double, double> put = (a, b) => { if (clr1 > clr0 && b > clr0 && a < clr1) return; QuadAcross(m, g, mat, sA, sB, a, b); };
            if (centerGap)
            {
                double c0 = P.crosswalkCenterGap * 0.5, hl = yL - c0, hr = -yR - c0;
                StripeLayout(fitLeft ? hl : hr, fitLeft ? hr : hl, out st, out gap, out nF, out nO);
                int nl = fitLeft ? nF : nO, nr = fitLeft ? nO : nF;
                foreach (var q in StripesFrom(c0, st, gap, nl, phase)) put(q[0], q[1]);
                foreach (var q in StripesFrom(c0, st, gap, nr, phase)) put(-q[1], -q[0]);
                if (phase == 1 && 2 * c0 >= st + 0.2) put(-st * 0.5, st * 0.5);
                return;
            }
            StripeLayout(yL - yR, 0, out st, out gap, out nF, out nO);
            if (nF == 1) { double c = (yL + yR) * 0.5; if (phase == 0) put(c - st * 0.5, c + st * 0.5); return; }
            foreach (var q in StripesFrom(yR, st, gap, nF, phase)) put(q[0], q[1]);
        }

        /// <summary>보행 방향 화살표(도로를 가로지르는 방향): s 가운데, 꼬리 쪽 옆 위치 yTail, dir −1 = 오른쪽(−)으로, +1 = 왼쪽(+)으로.
        /// 머리(세모)가 몸통(네모)보다 김</summary>
        void Arrow(MeshData m, RoadGeom g, string mat, double sc, double yTail, int dir)
        {
            double Lh = P.crosswalkArrowLen, W = P.crosswalkArrowW, head = Lh * Geom.Clamp(P.crosswalkArrowHead, 0.3, 0.8), shaftW = W * 0.4;
            double yb = yTail + dir * (Lh - head), yt = yTail + dir * Lh, hw = W / 2;
            QuadAcross(m, g, mat, sc - shaftW / 2, sc + shaftW / 2, Math.Min(yTail, yb), Math.Max(yTail, yb));
            Func<double, double> tip = q => yb + (yt - yb) * Math.Max(0, 1 - Math.Abs(q - sc) / hw);   // 머리: 가운데(sc)에서 뾰족
            Func<double, double> bse = q => yb;
            for (int h = 0; h < 2; h++)
                paint.Add(new PaintBand { sA = h == 0 ? sc - hw : sc, sB = h == 0 ? sc : sc + hw, lo = dir > 0 ? bse : tip, hi = dir > 0 ? tip : bse, mat = mat, prio = 3 });
        }

        /// <summary>대각선 횡단보도 시작점(마주 보는 모서리 연석 곡선 가운데)과 방향. 양 끝 다(시작점 2개씩). 없으면 null</summary>
        List<V2[]> DiagonalLines(JunctionGeom G)
        {
            var J = G.j; int n = G.corners.Count; if (n < 4) return null;
            var jo = ov.Junction(J); if (jo == null || jo.diagonal != 1) return null;
            var mids = new List<V2>();
            foreach (var C in G.corners) { var p = C.path; var cum = Geom.CumLen(p); mids.Add(Geom.At(p, cum, cum[cum.Length - 1] * 0.5)); }
            var o = new List<V2[]>();
            for (int i = 0; i < n / 2; i++) { V2 a = mids[i], b = mids[(i + n / 2) % n]; o.Add(new[] { a, b }); }
            return o;
        }

        /// <summary>좌회전 유도선(525): 신호 교차로에서 좌회전 차로마다, 그 차로 왼쪽 끝(1차로는 중앙선)에서 좌회전해 들어갈 길의 같은 차로 왼쪽 끝까지
        /// 흰 점선(50~100cm / 50~100cm, 폭 = 이어지는 선 폭). 차량 진행 방향 왼쪽(편람 25, 60~61쪽 그림 3-29). 교차로 면 위에 3cm 띄워 깖(평면)</summary>
        void GuideLines(MeshData m, JunctionGeom G)
        {
            var J = G.j; if (!J.arms.Any(a => a.stopDist > 0)) return;
            int idx = 0;
            foreach (var A in J.arms)
            {
                if (A.stopDist <= 0 || A.lanesIn <= 0 || A.road.oneWay) continue;
                int mv = Movements(A); if ((mv & 1) == 0) continue;
                var masks = LaneMasks(A.lanesIn, mv);
                // 좌회전으로 들어갈 팔: 왼쪽으로 90°에 가장 가까운 것
                V2 d = -A.dir; Arm X = null; double best = 1e9;
                foreach (var Y in J.arms)
                {
                    if (Y == A || Y.lanesOut <= 0) continue;
                    double th = Math.Atan2(V2.Cross(d, Y.dir), V2.Dot(d, Y.dir)) * 180 / Math.PI;
                    if (th < 30 || th > 160) continue;
                    if (Math.Abs(th - 90) < best) { best = Math.Abs(th - 90); X = Y; }
                }
                if (X == null) continue;
                double lwA = P.LaneWidth(A.road.rank, A.road.SpeedAt(A.atStart ? 0 : A.road.Length)), lwX = P.LaneWidth(X.road.rank, X.road.SpeedAt(X.atStart ? 0 : X.road.Length));
                double hA = A.road.divided ? P.medianHalf : P.centerHalfGap, hX = X.road.oneWay ? 0 : X.road.divided ? P.medianHalf : P.centerHalfGap;
                for (int k = 1; k <= A.lanesIn; k++)
                {
                    if ((masks[k - 1] & 1) == 0) continue;
                    int kx = Math.Min(k, Math.Max(1, X.lanesOut));
                    V2 p0 = A.Point(A.cut) + A.dir.Left * (hA + (k - 1) * lwA);
                    V2 p3 = X.road.oneWay ? X.Point(X.cut) + X.dir.Left * (X.lanesOut * lwX * 0.5 - (kx - 1) * lwX)
                                          : X.Point(X.cut) - X.dir.Left * (hX + (kx - 1) * lwX);
                    V2 t0 = d, t3 = X.dir; double a, b;
                    double k0 = (p3 - p0).Len * 0.4, k3 = k0;
                    if (Geom.LineLine(p0, t0, p3, t3, out a, out b) && a > 0 && b < 0) { k0 = a * 0.55; k3 = -b * 0.55; }
                    V2 c1 = p0 + t0 * k0, c2 = p3 - t3 * k3;
                    var pts = new List<V2>();
                    for (int i = 0; i <= 80; i++)
                    {
                        double u = i / 80.0, w = 1 - u;
                        pts.Add(p0 * (w * w * w) + c1 * (3 * w * w * u) + c2 * (3 * w * u * u) + p3 * (u * u * u));
                    }
                    var cum = Geom.CumLen(pts); double len = cum[cum.Length - 1];
                    if (len < P.guideMinLen) continue;
                    DashedCurve(m, G, pts, cum, P.guideOn, P.guideOff, P.lineLaneW, P.markLift + 0.002 * (idx++ % 4));
                }
            }
        }
        /// <summary>직진 유도선(525 '중앙선 연장'): 신호 교차로에서 직진으로 이어지는 두 양방향 길의 중앙선 끝끼리,
        /// 꺾이는 각이 throughGuideDeg 넘거나 중앙선이 옆으로 throughGuideOffset m 넘게 어긋날 때만(편람: 차로간 선형의 차이가 있는 지점 등 필요한 곳에만)</summary>
        void ThroughGuideLines(MeshData m, JunctionGeom G)
        {
            var J = G.j; if (J.merge || !J.arms.Any(a => a.stopDist > 0)) return;
            var done = new HashSet<string>(); int idx = 0;
            foreach (var A in J.arms)
            {
                if (A.road.oneWay || A.stopDist <= 0) continue;
                V2 d = -A.dir; Arm B = null; double best = 35;
                foreach (var Y in J.arms)
                {
                    if (Y == A || Y.road.oneWay) continue;
                    double th = Math.Abs(Math.Atan2(V2.Cross(d, Y.dir), V2.Dot(d, Y.dir)) * 180 / Math.PI);
                    if (th < best) { best = th; B = Y; }
                }
                if (B == null) continue;
                string key = Math.Min(A.road.id, B.road.id) + ":" + Math.Max(A.road.id, B.road.id);
                V2 p0 = A.Point(A.cut), p3 = B.Point(B.cut);
                double off = Math.Abs(V2.Cross(d, p3 - p0));                    // 들어오는 중앙선 연장선에서 옆으로 어긋남
                if (best < P.throughGuideDeg && off < P.throughGuideOffset) continue;
                if (!done.Add(key)) continue;
                double k = (p3 - p0).Len * 0.38; V2 c1 = p0 + d * k, c2 = p3 - B.dir * k;
                var pts = new List<V2>();
                for (int i = 0; i <= 60; i++) { double u = i / 60.0, w = 1 - u; pts.Add(p0 * (w * w * w) + c1 * (3 * w * w * u) + c2 * (3 * w * u * u) + p3 * (u * u * u)); }
                var cum = Geom.CumLen(pts); if (cum[cum.Length - 1] < P.guideMinLen * 0.5) continue;
                DashedCurve(m, G, pts, cum, P.guideOn, P.guideOff, P.lineLaneW, P.markLift + 0.001 + 0.002 * (idx++ % 4));
            }
        }
        /// <summary>합류·분기점(신호·횡단보도 없는 곳) 안: 마주 보는 양방향 본선 두 팔 사이로 차선(점선)·중앙선(분리 = 흰 실선 둘, 비분리 = 황색)을 이어 그림
        /// — 본선 위 20~30m 노면표시 빈 곳 없앰. 차로 수가 다르면 적은 쪽만큼, 끝선은 램프가 붙는 쪽이라 안 그림</summary>
        void MergeLaneLines(MeshData m, JunctionGeom G)
        {
            var J = G.j; var done = new HashSet<string>(); int idx = 0;
            // 마주 보는 양방향 팔 짝이 둘 이상이면(네거리 등 가로지름) 안 그림 — 선이 X 로 엇갈림
            int pairs = 0; var seen = new HashSet<string>();
            foreach (var A in J.arms)
            {
                if (A.road.oneWay || A.cut <= 0) continue;
                foreach (var Y in J.arms)
                {
                    if (Y == A || Y.road.oneWay || Y.cut <= 0) continue;
                    double th = Math.Abs(Math.Atan2(V2.Cross(-A.dir, Y.dir), V2.Dot(-A.dir, Y.dir)) * 180 / Math.PI);
                    if (th < 25 && seen.Add(Math.Min(A.road.id, Y.road.id) + ":" + Math.Max(A.road.id, Y.road.id) + ":" + (A.road.id == Y.road.id ? 1 : 0))) pairs++;
                }
            }
            if (pairs != 1) return;
            Func<Arm, double> armLw = A =>
            {
                var R = A.road; var pcs = R.fwd.Count > 0 ? R.fwd : R.bwd; if (pcs.Count == 0) return P.LaneWidth(R.rank, 50);
                var pc = A.atStart ? pcs[0] : pcs[pcs.Count - 1];
                return P.LaneWidth(!string.IsNullOrEmpty(pc.rank) ? pc.rank : R.rank, pc.spd > 0 ? pc.spd : 50);
            };
            foreach (var A in J.arms)
            {
                if (A.road.oneWay || A.cut <= 0) continue;
                Arm B = null; double best = 25;
                foreach (var Y in J.arms)
                {
                    if (Y == A || Y.road.oneWay || Y.cut <= 0) continue;
                    double th = Math.Abs(Math.Atan2(V2.Cross(-A.dir, Y.dir), V2.Dot(-A.dir, Y.dir)) * 180 / Math.PI);
                    if (th < best) { best = th; B = Y; }
                }
                if (B == null) continue;
                string key = Math.Min(A.road.id, B.road.id) + ":" + Math.Max(A.road.id, B.road.id);
                if (!done.Add(key)) continue;
                // 나머지 팔이 본선과 비스듬히(35° 안) 붙는 램프일 때만(가로지르는 길이 있는 평면 교차로·오거리는 안 그림). 세 갈래(T)는 그림
                if (J.arms.Count > 3 && J.arms.Any(Y => Y != A && Y != B && Math.Abs(V2.Dot(Y.dir, A.dir)) < Math.Cos(35 * Math.PI / 180))) continue;
                V2 pA = A.Point(A.cut), pB = B.Point(B.cut), nA = A.dir.Left, nB = B.dir.Left;
                double lwA = armLw(A), lwB = armLw(B);
                double hA = A.road.divided ? P.medianHalf : P.centerHalfGap, hB = B.road.divided ? P.medianHalf : P.centerHalfGap;
                Action<V2, V2, double, double, string, double> curve = (pa, pb, dOn, dOff, mat, wd) =>
                {
                    double k = (pb - pa).Len * 0.38; V2 c1 = pa - A.dir * k, c2 = pb - B.dir * k;
                    var pts = new List<V2>();
                    for (int i = 0; i <= 40; i++) { double u = i / 40.0, w = 1 - u; pts.Add(pa * (w * w * w) + c1 * (3 * w * w * u) + c2 * (3 * w * u * u) + pb * (u * u * u)); }
                    var cum = Geom.CumLen(pts); double L = cum[cum.Length - 1]; if (L < 1.0) return;
                    if (dOff <= 0) DashedCurve(m, G, pts, cum, L, 0, wd, P.markLift + 0.001 + 0.002 * (idx++ % 4), mat, true);
                    else DashedCurve(m, G, pts, cum, dOn, dOff, wd, P.markLift + 0.001 + 0.002 * (idx++ % 4), mat, true);
                };
                bool express = P.IsExpress(A.road.rank) || A.road.divided;
                double dOn0 = express ? P.dashExpressOn : P.dashUrbanOn, dOff0 = express ? P.dashExpressOff : P.dashUrbanOff;
                // A → B 흐름: A 의 들어오는 차로(+nA 쪽) → B 의 나가는 차로(−nB 쪽). B → A 는 반대
                int nAB = Math.Min(A.lanesIn, B.lanesOut), nBA = Math.Min(B.lanesIn, A.lanesOut);
                for (int k = 1; k < nAB; k++) curve(pA + nA * (hA + k * lwA), pB - nB * (hB + k * lwB), dOn0, dOff0, "mark_white", P.lineLaneW);
                for (int k = 1; k < nBA; k++) curve(pA - nA * (hA + k * lwA), pB + nB * (hB + k * lwB), dOn0, dOff0, "mark_white", P.lineLaneW);
                // 중앙선
                if (A.road.divided && B.road.divided)
                {
                    curve(pA + nA * (hA - 0.2), pB - nB * (hB - 0.2), 0, 0, "mark_white", P.lineCenterW);
                    curve(pA - nA * (hA - 0.2), pB + nB * (hB - 0.2), 0, 0, "mark_white", P.lineCenterW);
                }
                else if (!A.road.divided && !B.road.divided)
                {
                    bool dbl = Math.Max(A.lanesIn, A.lanesOut) >= 2;
                    if (dbl)
                    {
                        double o = P.doubleGap * 0.5 + P.lineCenterDoubleW * 0.5;
                        curve(pA + nA * o, pB - nB * o, 0, 0, "mark_yellow", P.lineCenterDoubleW);
                        curve(pA - nA * o, pB + nB * o, 0, 0, "mark_yellow", P.lineCenterDoubleW);
                    }
                    else curve(pA, pB, 0, 0, "mark_yellow", P.lineCenterW);
                }
            }
        }
        /// <summary>교차로 면 위 곡선 점선(띄워 깖): 양 끝이 온전한 점선이 되게 점선·빈 길이를 같은 비율로 맞춤</summary>
        void DashedCurve(MeshData m, JunctionGeom G, List<V2> pts, double[] cum, double on, double off, double width, double lift, string mat = "mark_white", bool clip = false)
        {
            double len = cum[cum.Length - 1];
            int n = Math.Max(1, (int)Math.Round((len + off) / (on + off)));
            double f = len / (n * on + (n - 1) * off), dl = on * f, gl = off * f;
            for (int k = 0; k < n; k++)
            {
                double a0 = k * (dl + gl), a1 = a0 + dl; int steps = Math.Max(1, (int)Math.Ceiling(dl / 0.25));
                int pl = -1, pr = -1;
                for (int i = 0; i <= steps; i++)
                {
                    double sx = a0 + (a1 - a0) * i / steps;
                    V2 p = Geom.At(pts, cum, sx), t = (Geom.At(pts, cum, Math.Min(len, sx + 0.1)) - Geom.At(pts, cum, Math.Max(0, sx - 0.1))).Norm, nl = t.Left;
                    V2 l = p + nl * (width * 0.5), r = p - nl * (width * 0.5);
                    if (clip && !InSheet(G, p)) { pl = pr = -1; continue; }     // 교차로 면 밖(램프 쪽 좁은 곳)은 안 그림
                    int il = m.V(l, G.H(l) + lift), ir = m.V(r, G.H(r) + lift);
                    if (pl >= 0) m.Strip(mat, pl, il, pr, ir);
                    pl = il; pr = ir;
                }
            }
        }

        /// <summary>점 q 가 대각선 횡단보도 띠의 시작 부분(연석 쪽)에 드는지</summary>
        bool UnderDiagonal(List<V2[]> lines, V2 q)
        {
            double hw = P.crosswalkWidth * 0.5 + 0.05;
            foreach (var l in lines)
                for (int e = 0; e < 2; e++)
                {
                    V2 a = l[e], d = (l[1 - e] - l[e]).Norm, r = q - a;
                    double along = V2.Dot(r, d), side = Math.Abs(V2.Cross(d, r));
                    if (side < hw && along > -2.0 && along < 3.0) return true;
                }
            return false;
        }

        /// <summary>대각선 횡단보도: 마주 보는 모서리끼리 X 자로, 줄무늬는 걷는 방향에 수직.
        /// 두 대각선 띠가 실제로 겹치는 칸만 비움(업무편람 그림 3-48: 네 갈래 줄무늬, 가운데 빈칸)</summary>
        void DiagonalCrosswalks(MeshData m, JunctionGeom G)
        {
            var lines = DiagonalLines(G); if (lines == null) return;
            string mat = "mark_white"; int nd = lines.Count;
            double cw = P.crosswalkWidth, per = P.crosswalkStripe + P.crosswalkGap;
            for (int i = 0; i < nd; i++)
            {
                V2 a = lines[i][0], d = (lines[i][1] - a).Norm, nl = d.Left; double len = (lines[i][1] - a).Len;
                double lift = P.markLift + 0.004 * i;
                // 다른 대각선 띠와 겹치는 구간(걷는 방향 거리 u): 막대(폭 cw)가 그 띠(반폭 cw/2)에 닿는 곳만
                var skip = new List<double[]>();
                for (int k = 0; k < nd; k++)
                {
                    if (k == i) continue; double t, u;
                    V2 bk = lines[k][0], ek = (lines[k][1] - bk).Norm;
                    if (!Geom.LineLine(a, d, bk, ek, out t, out u)) continue;
                    double sn = Math.Abs(V2.Cross(d, ek)); if (sn < 0.2) continue;
                    double cs = Math.Abs(V2.Dot(d, ek)), half = (cw * 0.5 + cs * cw * 0.5) / sn - 0.05;
                    skip.Add(new[] { t - half, t + half });
                }
                for (double u = 1.0; u + P.crosswalkStripe <= len - 1.0; u += per)
                {
                    double u0 = u, u1 = u + P.crosswalkStripe;
                    if (skip.Any(q => u1 > q[0] && u0 < q[1])) continue;
                    // 막대: 걷는 방향(d)으로 stripe 두께, 옆(nl)으로 cw 길이
                    V2 p0 = a + d * u0, p1 = a + d * u1;
                    V2 l0 = p0 + nl * (cw / 2), r0 = p0 - nl * (cw / 2), l1 = p1 + nl * (cw / 2), r1 = p1 - nl * (cw / 2);
                    m.Strip(mat, m.V(l0, G.H(l0) + lift), m.V(l1, G.H(l1) + lift), m.V(r0, G.H(r0) + lift), m.V(r1, G.H(r1) + lift));
                }
            }
        }

        /// <summary>꺾은선의 자기 교차 고리 제거(첫·끝 점 유지): 교차하는 두 선분 사이를 교점 하나로</summary>
        static List<V2> RemoveLoops(List<V2> q)
        {
            var o = new List<V2>(q);
            for (int guard = 0; guard < 200; guard++)
            {
                bool found = false;
                for (int i = 0; i + 1 < o.Count && !found; i++)
                    for (int j = o.Count - 2; j > i + 1; j--)
                    {
                        double t, s;
                        V2 a = o[i], b = o[i + 1], c = o[j], d = o[j + 1];
                        if (!Geom.LineLine(a, b - a, c, d - c, out t, out s) || t <= 1e-9 || t >= 1 - 1e-9 || s <= 1e-9 || s >= 1 - 1e-9) continue;
                        V2 X = a + (b - a) * t;
                        o.RemoveRange(i + 1, j - i); o.Insert(i + 1, X); found = true; break;
                    }
                if (!found) break;
            }
            return o;
        }
    }
}
