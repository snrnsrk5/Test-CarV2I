// PNU 도로 생성기 — 신호등 자리(신호등 모양은 따로 만드는 신호등 생성기가 맡음)
// 근거: 교통신호기 설치·관리 업무편람(2026) 제1편 3장
//   제1주신호등: 교차로 건너기 전, 진행 방향 오른쪽 모서리. 그림 3-2: 정지선 3m + 횡단보도 6~8m + 1m → 정지선에서 10m 이상
//               (등면 높이 5.0m일 때 앙각 15°). 가로형(내민식) 팔로 차로 위에(좌우 마지막 차로에서 4.8m 안)
//   제2주신호등: 교차로 건너편(필요할 때 하나). 접근로 편도 2차로 이하는 원칙적으로 제1주만
//               제1주 자리가 조건을 못 맞추면 제1주는 정지선 부근 + 제2주로 보완
//   등면 수(표 3-2, 제2주 포함): 편도 1·2차로 1~2, 3차로 2, 4·5차로 3, 6차로 이상 3+. 한 팔의 등면 간격 2.4m 이상
//   보조신호등: 정지한 차 운전자용(200mm), 주신호등 기둥이나 보행 신호등 기둥에
//   높이: 가로형 아래 끝 4.5m 이상(도로 면), 세로형 2.5~3.5m, 보행 2~3m — 보행 신호등은 가급적 차량 신호기에 부착
//   부착물 힌트: 노드링크 회전정보(진입 링크 기준) — 비보호·U턴·좌회전금지 등
using System;
using System.Collections.Generic;
using System.Linq;

namespace PNU.RoadGen
{
    public enum SignalKind { VehicleMain = 0, VehicleAux = 1, Pedestrian = 2, VehicleSecond = 3 }

    public class SignalSpot
    {
        public SignalKind kind;
        public string junctionKey, junctionName, roadName, approachLink;
        public V2 pos; public double y;          // 기둥 밑(인도 윗면 높이)
        public V2 facing;                        // 등면이 보는 방향(위에서 본 단위벡터)
        public int lanesIn;
        public int mount;                        // 0 세로형(기둥에 바로), 1 가로형(내민식: 팔을 차로 위로)
        public double armLength;                 // 가로형 팔 길이(m, 기둥 → 운전자 기준 왼쪽)
        public int faces = 1;                    // 등면 수
        public double[] headOffsets = new double[] { 0 };   // 기둥에서 팔을 따라 각 등면 가운데까지(m)
        public double headHeight;                // 등면 아래 끝 높이(기둥 밑 기준, m)
        public int lens = 300;                   // 렌즈 지름(mm): 주신호 300, 보조 200
        public int poleOf = -1;                  // 다른 자리의 기둥에 붙음(그 자리 번호), −1 = 제 기둥
        public List<string> hints = new List<string>();
        public int junctionId, armIndex;
        public int placement;                    // 1 제1주만 2 제1주+제2주 3 정지선 부근+제2주 4 건너편만
        public double distance;                  // 정지선 가운데에서 기둥까지(m)
        public string note = "";
    }

    public static class SignalPlanner
    {
        static readonly Dictionary<string, string> TurnLabel = new Dictionary<string, string>
        { { "001", "비보호" }, { "002", "버스만" }, { "003", "회전금지" }, { "011", "U턴" }, { "012", "P턴" }, { "101", "좌회전금지" }, { "102", "직진금지" }, { "103", "우회전금지" } };

        public static readonly string[] PlacementName = { "자동", "제1주만", "제1주+제2주", "정지선 부근+제2주", "건너편만" };

        public static List<SignalSpot> Plan(RoadGenerator gen)
        {
            var P = gen.P; var o = new List<SignalSpot>();
            var turnsFrom = new Dictionary<string, List<InTurn>>();
            foreach (var t in gen.net.turns) { List<InTurn> l; if (!turnsFrom.TryGetValue(t.from, out l)) { l = new List<InTurn>(); turnsFrom[t.from] = l; } l.Add(t); }
            double ch = P.curbHeight, pad = P.signalPolePad;
            foreach (var J in gen.net.junctions)
            {
                JunctionGeom G; if (!gen.junctionGeom.TryGetValue(J, out G) || !G.ok) continue;
                if (!J.arms.Any(a => a.stopDist > 0)) continue;                     // 정지선 있는 교차로 = 신호 교차로로 봄
                string jkey = Keys.Junction(J);
                var jo = gen.ov.Junction(J);
                int jmode = jo != null && jo.signalMode > 0 ? jo.signalMode : P.signalMode;
                int first = o.Count;
                Func<SignalKind, int, string, SignalSpot> New = (k, ai, road) => new SignalSpot { kind = k, junctionKey = jkey, junctionName = J.name, junctionId = J.id, armIndex = ai, roadName = road };

                // ---- 차량 신호(들어오는 차로 + 정지선이 있는 팔) ----
                for (int ai = 0; ai < J.arms.Count; ai++)
                {
                    var A = J.arms[ai];
                    if (A.lanesIn <= 0 || A.stopDist <= 0) continue;
                    V2 d = -A.dir, Rt = A.dir.Left;                            // d: 다가오는 차의 진행 방향, Rt: 운전자 오른쪽
                    double yIn0, yIn1, lw; LaneSpan(P, A, true, out yIn0, out yIn1, out lw);   // 들어오는 차로의 옆 위치(Rt 방향, 중심선 기준)
                    double sStop = A.cut + A.stopDist;
                    V2 stopC = A.Point(sStop) + Rt * ((yIn0 + yIn1) * 0.5);
                    Arm B = null; double best = 0.6;                           // 직진해서 나가는 팔
                    foreach (var X in J.arms) if (X != A) { double c = V2.Dot(X.dir, d); if (c > best) { best = c; B = X; } }
                    int lanes = A.lanesIn, total = Faces(P, lanes);

                    // 제1주 자리: 횡단보도 끝(교차로 쪽)에서 signalBeyondCrosswalk 지나고, 정지선에서 signalNearDist 이상(그림 3-2)
                    V2 pNear; double dNear; bool nearOK = NearPole(P, G, A, sStop, out pNear, out dNear);
                    int mode = jmode;
                    string note = "";
                    if (mode <= 0) mode = !nearOK ? 3 : lanes >= P.secondMainMinLanes ? 2 : 1;
                    if (!nearOK && (mode == 1 || mode == 2)) { note = "제1주 자리 없음(모서리 인도 없음) → 정지선 부근"; mode = 3; }

                    string link = ApproachLink(A); var hints = new List<string>();
                    List<InTurn> tl;
                    if (link != null && turnsFrom.TryGetValue(link, out tl))
                        foreach (var t in tl) { string lab; if (TurnLabel.TryGetValue(t.type, out lab) && !hints.Contains(lab)) hints.Add(lab); }

                    bool hasSecond = mode == 2 || mode == 3 || mode == 4;
                    int mainIdx = -1;
                    if (mode != 4)
                    {
                        V2 pole; double sMain;
                        if (mode == 3)
                        {   // 정지선 부근: 횡단보도와 정지선 사이(업무편람 권장: 해당 위치가 어려우면 횡단보도와 정지선 사이)
                            sMain = A.cut + A.crosswalkWidth + P.stopLineGap * 0.5;
                            pole = A.Point(sMain) + Rt * (A.wIn + pad);
                        }
                        else pole = pNear;
                        int nf = Math.Max(1, total - (hasSecond ? 1 : 0));
                        var sp = New(SignalKind.VehicleMain, ai, A.road.name);
                        sp.pos = pole; sp.y = G.H(pole) + ch; sp.facing = A.dir; sp.lanesIn = lanes;
                        Heads(P, sp, Lateral(A, pole, Rt), yIn0, lw, lanes, nf);
                        sp.placement = mode; sp.distance = (pole - stopC).Len; sp.approachLink = link; sp.hints = hints; sp.note = note;
                        mainIdx = o.Count; o.Add(sp);
                        // 보조신호등: 제1주가 정지선에서 10m 넘게 앞 + 가로형이면, 같은 기둥에 정지한 차를 보는 낮은 신호등(표 3-2 주1)
                        if (P.auxSignals && mode != 3 && sp.mount == 1)
                        {
                            var ax = New(SignalKind.VehicleAux, ai, A.road.name);
                            ax.pos = pole; ax.y = sp.y; ax.facing = (stopC - pole).Norm; ax.lanesIn = lanes; ax.mount = 0; ax.faces = 1;
                            ax.headOffsets = new double[] { 0 }; ax.headHeight = P.auxHeadHeight; ax.lens = 200; ax.poleOf = mainIdx;
                            ax.placement = mode; ax.distance = sp.distance; ax.approachLink = link; ax.hints = new List<string>(hints);
                            o.Add(ax);
                        }
                    }
                    if (hasSecond)
                    {   // 제2주(건너편): 직진해 나가는 팔의 오른쪽 모서리, 그 팔 횡단보도 앞(교차로 쪽)
                        var sp = New(SignalKind.VehicleSecond, ai, A.road.name);
                        V2 pole; double lat, y0 = 0, lwB = lw; int nB = 1;
                        if (B != null)
                        {
                            V2 Rb = -B.dir.Left;                                       // 나가는 쪽(운전자 오른쪽)
                            double yB1; LaneSpan(P, B, false, out y0, out yB1, out lwB); nB = Math.Max(1, B.lanesOut);
                            double tF = B.cut - P.signalBeyondCrosswalk;
                            Corner Cb = G.corners.FirstOrDefault(c => c.b == B);
                            if (Cb != null && Cb.kind == 0) tF = Math.Max(tF, Cb.tB);
                            pole = B.Point(tF) + Rb * (B.wOut + pad);
                            lat = Lateral(B, pole, Rb);
                        }
                        else
                        {   // 직진 갈 길이 없음(T자 곁가지): 교차로 건너편 가장자리 바로 뒤
                            V2 hit = FarHit(G.poly, stopC, d);
                            pole = hit + d * (1.5 + pad); lat = 0;
                        }
                        sp.pos = pole; sp.y = G.H(pole) + ch; sp.facing = -d; sp.lanesIn = lanes;
                        int nf = mode == 4 ? total : 1;
                        if (B != null) Heads(P, sp, lat, y0, lwB, nB, nf);
                        else { sp.mount = 0; sp.faces = 1; sp.headOffsets = new double[] { 0 }; sp.headHeight = P.postHeadHeight; }
                        sp.placement = mode; sp.distance = (pole - stopC).Len; sp.approachLink = link; sp.hints = new List<string>(hints);
                        if (mode == 4) sp.note = "건너편만(예전 방식)";
                        // 같은 자리에 같은 쪽을 보는 등이 이미 있으면(두 진입로가 같은 건너편을 봄) 하나로: 힌트만 합침
                        SignalSpot dup = null;
                        for (int k = first; k < o.Count && dup == null; k++)
                        {
                            var q = o[k]; if (q.kind != SignalKind.VehicleSecond && q.kind != SignalKind.VehicleMain) continue;
                            if ((q.pos - sp.pos).Len < 0.5 && V2.Dot(q.facing, sp.facing) > 0.95) dup = q;
                        }
                        if (dup != null) { foreach (var h in sp.hints) if (!dup.hints.Contains(h)) dup.hints.Add(h); }
                        else o.Add(sp);
                    }
                }

                // ---- 보행 신호(횡단보도 양 끝): 가까운 차량 신호 기둥이 있으면 거기 붙임 ----
                for (int ai = 0; ai < J.arms.Count; ai++)
                {
                    var A = J.arms[ai]; if (!A.crosswalk) continue;
                    double along = P.pedSignalSide == 0 ? A.cut - P.pedSignalGap : A.cut + A.crosswalkWidth + P.pedSignalGap;
                    V2 m = A.Point(along);
                    V2 e1 = m + A.dir.Left * (A.wIn + pad), e2 = m - A.dir.Left * (A.wOut + pad);
                    for (int side = 0; side < 2; side++)
                    {
                        V2 e = side == 0 ? e1 : e2, other = side == 0 ? e2 : e1;
                        var sp = New(SignalKind.Pedestrian, ai, A.road.name);
                        sp.facing = (other - e).Norm; sp.mount = 0; sp.faces = 1; sp.headOffsets = new double[] { 0 }; sp.headHeight = P.pedHeadHeight;
                        int host = -1; double hd = P.signalMergeDist;
                        for (int k = first; k < o.Count; k++)
                        {
                            var q = o[k]; if (q.poleOf >= 0 || (q.kind != SignalKind.VehicleMain && q.kind != SignalKind.VehicleSecond)) continue;
                            double dd = (q.pos - e).Len; if (dd <= hd) { hd = dd; host = k; }
                        }
                        if (host >= 0) { sp.pos = o[host].pos; sp.y = o[host].y; sp.poleOf = host; }
                        else { sp.pos = e; sp.y = G.H(e) + ch; }
                        o.Add(sp);
                    }
                }

                // ---- 기둥 합치기: 제 기둥끼리 signalMergeDist 안이면 앞 기둥 하나에 같이 닮(보행끼리, 차량끼리도) ----
                for (int k = first; k < o.Count; k++)
                {
                    if (o[k].poleOf >= 0) continue;
                    int host = -1; double hd = P.signalMergeDist;
                    for (int h = first; h < k; h++)
                    {
                        if (o[h].poleOf >= 0) continue;
                        double dd = (o[h].pos - o[k].pos).Len; if (dd <= hd) { hd = dd; host = h; }
                    }
                    if (host >= 0) o[k].poleOf = host;
                }
                for (int k = first; k < o.Count; k++)
                {   // 붙은 등은 맨 아래 기둥 자리로(사슬 따라감)
                    int r = k, guard = 0; while (o[r].poleOf >= 0 && guard++ < 20) r = o[r].poleOf;
                    if (r != k) { o[k].poleOf = r; o[k].pos = o[r].pos; o[k].y = o[r].y; }
                }
            }
            return o;
        }

        /// <summary>표 3-2 주신호등면 수(제2주 포함)</summary>
        static int Faces(GenParams P, int lanes)
        {
            var t = P.signalFaces; if (t == null || t.Length == 0) return lanes >= 4 ? 3 : lanes >= 2 ? 2 : 1;
            return Math.Max(1, t[Math.Max(0, Math.Min(t.Length - 1, lanes - 1))]);
        }

        /// <summary>팔의 한쪽 차로가 차지하는 옆 위치(그쪽 바깥 방향으로, 도로 중심선 기준): 안쪽 끝 y0, 바깥 끝 y1</summary>
        static void LaneSpan(GenParams P, Arm A, bool incoming, out double y0, out double y1, out double lw)
        {
            var R = A.road; double sEnd = A.atStart ? 0 : R.Length;
            int spd = R.SpeedAt(sEnd);
            lw = P.LaneWidth(R.rank, spd);
            int n = Math.Max(1, incoming ? A.lanesIn : A.lanesOut);
            y1 = (incoming ? A.wIn : A.wOut) - A.sh; y0 = y1 - n * lw;
        }

        /// <summary>팔 직선 기준 옆 위치(방향 side)</summary>
        static double Lateral(Arm A, V2 p, V2 side) { return V2.Dot(p - A.basePt, side); }

        /// <summary>등면 배치: 1차로는 세로형(기둥에 바로), 2차로 이상은 가로형 팔에 nf 개를 차로 위로(간격 2.4m 이상)</summary>
        static void Heads(GenParams P, SignalSpot sp, double latPole, double y0, double lw, int lanes, int nf)
        {
            sp.faces = nf;
            if (lanes < 2)
            {
                sp.mount = 0; sp.armLength = 0; sp.headOffsets = new double[nf]; sp.headHeight = P.postHeadHeight;
                for (int k = 0; k < nf; k++) sp.headOffsets[k] = 0;
                return;
            }
            sp.mount = 1; sp.headHeight = P.mastHeadHeight - P.curbHeight;
            var ys = new double[nf];
            double c1 = y0 + lw * 0.5, cn = y0 + lw * (lanes - 0.5);           // 첫 차로(중앙선 쪽) ~ 마지막 차로 가운데
            if (nf == 1) ys[0] = (y0 + y0 + lanes * lw) * 0.5;
            else
            {
                double sp0 = (cn - c1) / (nf - 1);
                if (sp0 < P.signalFaceSpacing) sp0 = P.signalFaceSpacing;
                for (int k = 0; k < nf; k++) ys[k] = c1 + sp0 * k;
            }
            sp.headOffsets = ys.Select(y => Math.Max(0.6, latPole - y)).OrderBy(v => v).ToArray();
            sp.armLength = sp.headOffsets[sp.headOffsets.Length - 1] + 0.3;
        }

        /// <summary>제1주 기둥: 운전자 오른쪽 모서리 인도. 정지선에서 signalNearDist 이상 + 횡단보도 끝에서 signalBeyondCrosswalk 지나.
        /// 연석 곡선(가각부) 안으로 들어가면 곡선을 따라감. 그쪽 모서리에 인도가 없으면 false</summary>
        static bool NearPole(GenParams P, JunctionGeom G, Arm A, double sStop, out V2 pole, out double dist)
        {
            V2 Rt = A.dir.Left; double pad = P.signalPolePad;
            double along = Math.Min(A.cut - P.signalBeyondCrosswalk, sStop - P.signalNearDist);
            Corner C = G.corners.FirstOrDefault(c => c.a == A);
            pole = A.Point(along) + Rt * (A.wIn + pad); dist = sStop - along;
            if (C == null || C.kind == 2 || C.swA <= 0 || C.path.Count < 2) return false;
            // 연석선(모서리 경로)에서 팔 거리 along 인 곳: 경로는 A 입구 왼쪽에서 시작해 교차로를 왼쪽에 두고 돎
            var p = C.path; V2 ctr = A.j.center;
            for (int k = 0; k + 1 < p.Count; k++)
            {
                double a = V2.Dot(p[k] - ctr, A.dir), b = V2.Dot(p[k + 1] - ctr, A.dir);
                if (a >= along && b <= along && a - b > 1e-6)
                {
                    double f = (a - along) / (a - b); V2 q = V2.Lerp(p[k], p[k + 1], f);
                    V2 outN = -((p[k + 1] - p[k]).Norm.Left);                 // 바깥(인도 쪽)
                    pole = q + outN * pad; return true;
                }
            }
            // 경로가 그 거리까지 안 감(모서리 곡선이 일찍 끝남): 경로에서 가장 먼 곳
            V2 last = p[p.Count - 1]; V2 dl = (p[p.Count - 1] - p[p.Count - 2]).Norm;
            pole = last - dl.Left * pad; dist = sStop - V2.Dot(last - ctr, A.dir);
            return dist >= P.signalNearDist * 0.7;
        }

        /// <summary>교차로로 들어오는 마지막 링크(회전정보의 '진입 링크')</summary>
        static string ApproachLink(Arm A)
        {
            var R = A.road;
            var l = A.atStart ? R.bwdLinks : R.fwdLinks;
            return l.Count > 0 ? l[l.Count - 1] : null;
        }

        /// <summary>점 s 에서 방향 d 로 쏜 반직선이 다각형 경계를 마지막으로 지나는 점(건너편 가장자리)</summary>
        static V2 FarHit(List<V2> poly, V2 s, V2 d)
        {
            double bestT = -1;
            for (int i = 0; i < poly.Count; i++)
            {
                V2 a = poly[i], b = poly[(i + 1) % poly.Count]; double t, u;
                if (!Geom.LineLine(s, d, a, b - a, out t, out u)) continue;
                if (t > 0 && u >= 0 && u <= 1 && t > bestT) bestT = t;
            }
            return bestT > 0 ? s + d * bestT : s + d * 20;
        }
    }
}
