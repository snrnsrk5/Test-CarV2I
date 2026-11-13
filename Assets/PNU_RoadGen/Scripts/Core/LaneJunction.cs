// PNU 도로 생성기 — 차로 단위 교차로(연결 구간)
//   ① 팔 입구 단면 위에 차로마다 끝점: 들어오는 차로(a1…) / 나가는 차로(a1′…), 1차로 = 중앙선 쪽(도로교통법 시행규칙 제16조)
//   ② 짝 잇기: 순서 유지(왼쪽 차로는 왼쪽 길로) · 남는 차로는 바깥 = 우회전 전용(우회전이 없으면 안쪽 = 좌회전 전용, 둘 다 없으면 바깥 직진 차로로 합류)
//              · 끊긴 차로 없음(아무도 안 들어오는 나가는 차로는 가장 가까운 차로에서 갈라져 들어감) · 노드링크 회전 금지(003·101·102·103) 반영
//   ③ 교차로 면 한 장: 거리장(SDF) 격자에
//        합집합 = 단순 교차로: 기존 다각형(입구 + 모서리 연석 곡선) / 꼬인 교차로: 입구 띠 + 모서리 + 연결선 띠(차로 폭)
//        빼기   = 같은 높이 도로 면(입구 밖은 도로가 깖) + 모서리 연석·인도 쪽
//      → 0 등고선(선형 보간) → 단순 다각형(구멍 포함) → 입구 모서리 점 맞춤 → 귀 자르기 삼각형. 부채꼴·겹침(z-fighting) 없음
using System;
using System.Collections.Generic;
using System.Linq;

namespace PNU.RoadGen
{
    /// <summary>차로 끝점(팔 입구 단면 위). y = 팔 왼쪽(+dir.Left) 기준 옆 위치, k = 1 이 중앙선 쪽</summary>
    public class LaneEnd { public Arm arm; public bool inbound; public int k; public double y, lw; public V2 p; }
    /// <summary>차로 연결선: a(들어오는 차로) → b(나가는 차로). turn 0 직진, 1 좌회전, 2 우회전</summary>
    public class LaneLink { public LaneEnd a, b; public int turn; public List<V2> path; public double hw; }

    public partial class RoadGenerator
    {
        public int sheetBuilt, sheetFailed, sheetLinks, manualJunctions, laneLinkMiss;

        double ArmLaneW(Arm A)
        {
            var R = A.road; var pc = PieceAt(R, A.atStart ? 0 : R.Length);
            return P.LaneWidth(pc != null && !string.IsNullOrEmpty(pc.rank) ? pc.rank : R.rank, pc != null && pc.spd > 0 ? pc.spd : 50);
        }

        /// <summary>팔 입구의 차로 끝점(1차로부터). 양방향: 중앙선(분리대) 바깥으로 차로, 일방: 차로를 가운데 기준 양쪽으로</summary>
        LaneEnd[] LaneEnds(Arm A, bool inbound)
        {
            int n = inbound ? A.lanesIn : A.lanesOut; if (n <= 0) return new LaneEnd[0];
            double lw = ArmLaneW(A); V2 M = A.Point(A.cut), l = A.dir.Left;
            var o = new LaneEnd[n];
            for (int k = 1; k <= n; k++)
            {
                double y;
                if (A.road.oneWay) y = inbound ? -n * lw * 0.5 + (k - 0.5) * lw : n * lw * 0.5 - (k - 0.5) * lw;   // 들어오는 차는 −l 쪽이 왼쪽
                else { double h = A.road.divided ? P.medianHalf : P.centerHalfGap; y = inbound ? h + (k - 0.5) * lw : -(h + (k - 0.5) * lw); }
                o[k - 1] = new LaneEnd { arm = A, inbound = inbound, k = k, y = y, lw = lw, p = M + l * y };
            }
            return o;
        }

        /// <summary>팔 A 로 들어온 차가 못 가는 길(노드링크 회전정보 003 회전금지·101 좌회전금지·102 직진금지·103 우회전금지)</summary>
        HashSet<Road> BannedTargets(Arm A)
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
            return banned;
        }

        sealed class Tgt { public Arm X; public double th; }

        /// <summary>교차로 차로 연결선(규칙 3개 + 회전 금지)</summary>
        /// <summary>차로 끝 고정 ID(편집기·덮어쓰기 파일): 도로키|s(시작)·e(끝)|i(들어옴)·o(나감)|차로번호</summary>
        public static string LaneEndId(LaneEnd e)
        {
            var R = e.arm.road; string ek = e.arm.atStart ? R.ekS : R.ekE, tg = e.arm.atStart ? R.tagS : R.tagE;
            return (ek ?? Keys.Road(R)) + "|" + (tg ?? (e.arm.atStart ? "s" : "e")) + "|" + (e.inbound ? "i" : "o") + "|" + e.k;
        }

        /// <summary>회전 종류(각도로): 0 직진, 1 좌회전, 2 우회전, 3 유턴</summary>
        public int TurnOf(LaneEnd a, LaneEnd b)
        {
            V2 d = -a.arm.dir; double th = Math.Atan2(V2.Cross(d, b.arm.dir), V2.Dot(d, b.arm.dir)) * 180 / Math.PI;
            return Math.Abs(th) > P.laneUturnDeg ? 3 : Math.Abs(th) <= P.laneStraightDeg ? 0 : th > 0 ? 1 : 2;
        }

        public void BuildLaneLinks(JunctionGeom G)
        {
            var J = G.j; G.links.Clear(); G.ins.Clear(); G.outs.Clear(); G.manual = false;
            foreach (var A in J.arms) { G.ins[A] = LaneEnds(A, true); G.outs[A] = LaneEnds(A, false); }
            var man = ov.LaneLinks(J);
            if (man != null)
            {   // 손으로 이은 연결(차로 연결 편집기) — 자동 대신 그대로
                var byId = new Dictionary<string, LaneEnd>();
                foreach (var e in G.ins.Values.SelectMany(x => x).Concat(G.outs.Values.SelectMany(x => x))) byId[LaneEndId(e)] = e;
                int miss = 0;
                foreach (var o in man)
                {
                    LaneEnd a, b; if (!byId.TryGetValue(o.from, out a) || !byId.TryGetValue(o.to, out b) || !a.inbound || b.inbound) { miss++; continue; }
                    G.links.Add(new LaneLink { a = a, b = b, turn = TurnOf(a, b), hw = 0.5 * (a.lw + b.lw) * 0.5, path = LinkCurveVia(a.p, -a.arm.dir, b.p, b.arm.dir, o.via) });
                }
                G.manual = !ov.edAuto.Contains(Keys.Junction(J)); if (G.manual) manualJunctions++;
                if (miss > 0) { laneLinkMiss += miss; log.Add("lane links: " + J.name + " (" + Keys.Junction(J) + ") 못 찾은 연결 " + miss); }
                sheetLinks += G.links.Count;
                return;
            }
            double sd = P.laneStraightDeg;
            foreach (var A in J.arms)
            {
                var I = G.ins[A]; int n = I.Length; if (n == 0) continue;
                var ban = BannedTargets(A); V2 d = -A.dir;
                var T = new List<Tgt>();
                foreach (var X in J.arms)
                {
                    if (X == A || G.outs[X].Length == 0 || ban.Contains(X.road)) continue;
                    double th = Math.Atan2(V2.Cross(d, X.dir), V2.Dot(d, X.dir)) * 180 / Math.PI;
                    if (Math.Abs(th) > P.laneUturnDeg) continue;                       // 유턴은 안 그림
                    T.Add(new Tgt { X = X, th = th });
                }
                if (T.Count == 0) continue;
                T.Sort((a, b) => b.th.CompareTo(a.th));                                  // 왼쪽(반시계로 큰 각)부터
                var S = T.Where(t => Math.Abs(t.th) <= sd).ToList();
                var Lt = T.Where(t => t.th > sd).ToList();
                var Rt = T.Where(t => t.th < -sd).ToList();
                int Ctot = S.Sum(t => G.outs[t.X].Length);
                if (S.Count >= 2 && n < Ctot)
                {   // 갈라지는 곳(직진 쪽 길이 둘 이상, 들어오는 차로가 모자람): 길마다 들어오는 차로를 나눠 줌 — 왼쪽 길 = 왼쪽 차로, 길마다 1차로 이상
                    if (n >= S.Count)
                    {
                        var al = new int[S.Count];
                        for (int j = 0; j < S.Count; j++) al[j] = Math.Max(1, (int)Math.Round(n * (double)G.outs[S[j].X].Length / Ctot));
                        while (al.Sum() > n) { int jm = -1; for (int j = 0; j < S.Count; j++) if (al[j] > 1 && (jm < 0 || al[j] > al[jm])) jm = j; if (jm < 0) break; al[jm]--; }
                        while (al.Sum() < n) { int jm = 0; for (int j = 1; j < S.Count; j++) if (G.outs[S[j].X].Length - al[j] > G.outs[S[jm].X].Length - al[jm]) jm = j; al[jm]++; }
                        int off = 0;
                        for (int j = 0; j < S.Count; j++) { var O = G.outs[S[j].X]; for (int q = 0; q < al[j] && off + q < n; q++) AddLink(G, I[off + q], O[Math.Min(q, O.Length - 1)], 0); off += al[j]; }
                    }
                    else
                        for (int j = 0; j < S.Count; j++) AddLink(G, I[(int)Math.Round(j * (n - 1.0) / (S.Count - 1))], G.outs[S[j].X][0], 0);
                    foreach (var t in Lt) { var O = G.outs[t.X]; AddLink(G, I[0], O[0], 1); }
                    foreach (var t in Rt) { var O = G.outs[t.X]; AddLink(G, I[n - 1], O[O.Length - 1], 2); }
                }
                else if (S.Count > 0)
                {
                    var so = new List<LaneEnd>(); foreach (var t in S) so.AddRange(G.outs[t.X]);   // 직진 쪽 나가는 차로를 왼쪽 길·1차로부터 한 줄로
                    int m = Math.Min(n, so.Count), sur = n - m;
                    bool surR = sur > 0 && Rt.Count > 0, surL = sur > 0 && !surR && Lt.Count > 0;
                    int f0 = surL ? sur : 0;
                    for (int i = 0; i < m; i++) AddLink(G, I[f0 + i], so[i], 0);
                    if (sur > 0 && !surR && !surL) for (int i = m; i < n; i++) AddLink(G, I[i], so[m - 1], 0);   // 줄어듦: 바깥 직진 차로로 합류
                    int nl = surL ? sur : 1, nr = surR ? sur : 1;
                    foreach (var t in Lt) { var O = G.outs[t.X]; for (int i = 0; i < nl && i < n; i++) AddLink(G, I[i], O[Math.Min(i, O.Length - 1)], 1); }
                    foreach (var t in Rt) { var O = G.outs[t.X]; for (int j = 0; j < nr && j < n; j++) AddLink(G, I[n - 1 - j], O[Math.Max(0, O.Length - 1 - j)], 2); }
                }
                else
                {   // 직진 없음(T자 위쪽 등): 왼쪽 반은 좌회전, 오른쪽 반은 우회전
                    int nl = Lt.Count == 0 ? 0 : Rt.Count == 0 ? n : (n + 1) / 2, nr = n - nl;
                    foreach (var t in Lt) { var O = G.outs[t.X]; for (int i = 0; i < nl; i++) AddLink(G, I[i], O[Math.Min(i, O.Length - 1)], 1); }
                    foreach (var t in Rt) { var O = G.outs[t.X]; for (int j = 0; j < nr; j++) AddLink(G, I[n - 1 - j], O[Math.Max(0, O.Length - 1 - j)], 2); }
                }
            }
            // 끊긴 차로 없음: 아무도 안 들어오는 나가는 차로는 같은 길로 들어오는 연결선 중 차로 번호가 가장 가까운 것(직진 먼저)에서 갈라져 들어감
            foreach (var X in J.arms)
                foreach (var o in G.outs[X])
                {
                    if (G.links.Any(l => l.b == o)) continue;
                    LaneLink best = null; int bd = 99;
                    foreach (var l in G.links)
                    {
                        if (l.b.arm != X) continue; int dk = Math.Abs(l.b.k - o.k);
                        if (dk < bd || (dk == bd && best != null && l.turn == 0 && best.turn != 0)) { bd = dk; best = l; }
                    }
                    if (best != null) AddLink(G, best.a, o, best.turn);
                }
            sheetLinks += G.links.Count;
        }

        void AddLink(JunctionGeom G, LaneEnd a, LaneEnd b, int turn)
        {
            foreach (var l in G.links) if (l.a == a && l.b == b) return;
            G.links.Add(new LaneLink { a = a, b = b, turn = turn, hw = 0.5 * (a.lw + b.lw) * 0.5, path = LinkCurve(a.p, -a.arm.dir, b.p, b.arm.dir) });
        }

        /// <summary>지나갈 점(via)이 있는 연결선: 점마다 접선 = 앞뒤 점 방향, 구간마다 3차 베지어(손잡이 = 구간 길이 0.4). 편집기(JS)와 같은 식</summary>
        public static List<V2> LinkCurveVia(V2 p0, V2 t0, V2 p3, V2 t3, List<V2> via)
        {
            if (via == null || via.Count == 0) return LinkCurve(p0, t0, p3, t3);
            var P = new List<V2> { p0 }; P.AddRange(via); P.Add(p3);
            int m = P.Count; var T = new V2[m]; T[0] = t0.Norm; T[m - 1] = t3.Norm;
            for (int i = 1; i < m - 1; i++) T[i] = (P[i + 1] - P[i - 1]).Norm;
            var o = new List<V2> { p0 };
            for (int i = 0; i + 1 < m; i++)
            {
                double D = (P[i + 1] - P[i]).Len, h = D * 0.4;
                V2 c1 = P[i] + T[i] * h, c2 = P[i + 1] - T[i + 1] * h;
                int n = Math.Max(4, (int)Math.Ceiling(D / 1.0));
                for (int k = 1; k <= n; k++) { double u = (double)k / n, w = 1 - u; o.Add(P[i] * (w * w * w) + c1 * (3 * w * w * u) + c2 * (3 * w * u * u) + P[i + 1] * (u * u * u)); }
            }
            return o;
        }

        /// <summary>연결선 곡선(3차 베지어): 접선이 앞에서 만나면 그 거리의 0.55, 아니면 두 점 거리의 0.4</summary>
        static List<V2> LinkCurve(V2 p0, V2 t0, V2 p3, V2 t3)
        {
            double D = (p3 - p0).Len, k0 = D * 0.4, k3 = k0, a, b;
            if (Geom.LineLine(p0, t0, p3, t3, out a, out b) && a > 0 && b < 0 && a < 2 * D && -b < 2 * D) { k0 = a * 0.55; k3 = -b * 0.55; }
            V2 c1 = p0 + t0 * k0, c2 = p3 - t3 * k3;
            int n = Math.Max(8, (int)Math.Ceiling(D / 1.0));
            var o = new List<V2>(n + 1);
            for (int i = 0; i <= n; i++) { double u = (double)i / n, w = 1 - u; o.Add(p0 * (w * w * w) + c1 * (3 * w * w * u) + c2 * (3 * w * u * u) + p3 * (u * u * u)); }
            return o;
        }

        // ---------------------------------------------------------------------
        // 거리장 격자
        // ---------------------------------------------------------------------
        sealed class Sdf
        {
            public readonly double x0, z0, cs; public readonly int nx, nz; public readonly float[] u, s;
            public Sdf(double x0, double z0, double x1, double z1, double cs)
            {
                this.x0 = x0; this.z0 = z0; this.cs = cs;
                nx = (int)Math.Ceiling((x1 - x0) / cs) + 1; nz = (int)Math.Ceiling((z1 - z0) / cs) + 1;
                u = new float[nx * nz]; s = new float[nx * nz];
                for (int i = 0; i < u.Length; i++) { u[i] = 1e9f; s[i] = 1e9f; }
            }
            public V2 Node(int i, int j) { return new V2(x0 + i * cs, z0 + j * cs); }
            bool Box(double ax, double az, double bx, double bz, double pad, out int i0, out int i1, out int j0, out int j1)
            {
                i0 = Math.Max(0, (int)Math.Floor((ax - pad - x0) / cs)); i1 = Math.Min(nx - 1, (int)Math.Ceiling((bx + pad - x0) / cs));
                j0 = Math.Max(0, (int)Math.Floor((az - pad - z0) / cs)); j1 = Math.Min(nz - 1, (int)Math.Ceiling((bz + pad - z0) / cs));
                return i0 <= i1 && j0 <= j1;
            }
            /// <summary>선분 a–b 둘레 반경 r(알약 모양)</summary>
            public void Capsule(float[] f, V2 a, V2 b, double r)
            {
                int i0, i1, j0, j1; double pad = r + 3 * cs;
                if (!Box(Math.Min(a.x, b.x), Math.Min(a.z, b.z), Math.Max(a.x, b.x), Math.Max(a.z, b.z), pad, out i0, out i1, out j0, out j1)) return;
                V2 ab = b - a; double L2 = ab.Len2;
                for (int j = j0; j <= j1; j++)
                    for (int i = i0; i <= i1; i++)
                    {
                        V2 p = Node(i, j); double t = L2 > 1e-12 ? Geom.Clamp(V2.Dot(p - a, ab) / L2, 0, 1) : 0;
                        double d = (a + ab * t - p).Len - r; int k = j * nx + i; if (d < f[k]) f[k] = (float)d;
                    }
            }
            /// <summary>삼각형(볼록): 세 변 바깥 거리의 최댓값</summary>
            public void Tri(float[] f, V2 a, V2 b, V2 c)
            {
                double cr = V2.Cross(b - a, c - a); if (Math.Abs(cr) < 1e-9) return;
                if (cr < 0) { var t = b; b = c; c = t; }
                int i0, i1, j0, j1;
                if (!Box(Math.Min(a.x, Math.Min(b.x, c.x)), Math.Min(a.z, Math.Min(b.z, c.z)), Math.Max(a.x, Math.Max(b.x, c.x)), Math.Max(a.z, Math.Max(b.z, c.z)), 3 * cs, out i0, out i1, out j0, out j1)) return;
                V2 nab = -(b - a).Norm.Left, nbc = -(c - b).Norm.Left, nca = -(a - c).Norm.Left;     // 바깥 법선
                for (int j = j0; j <= j1; j++)
                    for (int i = i0; i <= i1; i++)
                    {
                        V2 p = Node(i, j);
                        double d = Math.Max(V2.Dot(p - a, nab), Math.Max(V2.Dot(p - b, nbc), V2.Dot(p - c, nca)));
                        int k = j * nx + i; if (d < f[k]) f[k] = (float)d;
                    }
            }
            /// <summary>다각형(짝홀 안팎 + 변까지 거리)</summary>
            public void Poly(float[] f, List<V2> poly)
            {
                int n = poly.Count; if (n < 3) return;
                int i0, i1, j0, j1;
                if (!Box(poly.Min(p => p.x), poly.Min(p => p.z), poly.Max(p => p.x), poly.Max(p => p.z), 3 * cs, out i0, out i1, out j0, out j1)) return;
                for (int j = j0; j <= j1; j++)
                    for (int i = i0; i <= i1; i++)
                    {
                        V2 p = Node(i, j); double dm = double.MaxValue;
                        for (int e = 0; e < n; e++)
                        {
                            V2 a = poly[e], ab = poly[(e + 1) % n] - a; double L2 = ab.Len2;
                            double t = L2 > 1e-12 ? Geom.Clamp(V2.Dot(p - a, ab) / L2, 0, 1) : 0;
                            double d2 = (a + ab * t - p).Len2; if (d2 < dm) dm = d2;
                        }
                        double d = Math.Sqrt(dm); if (Geom.InPoly(poly, p)) d = -d;
                        int k = j * nx + i; if (d < f[k]) f[k] = (float)d;
                    }
            }
            /// <summary>합집합 구멍 메우기: 테두리에서 바깥(≥0)으로 이어지지 않는 바깥 칸은 안으로</summary>
            public void FillHoles(float[] f)
            {
                var seen = new bool[f.Length]; var q = new Queue<int>();
                for (int i = 0; i < nx; i++) { Seed(f, seen, q, i, 0); Seed(f, seen, q, i, nz - 1); }
                for (int j = 0; j < nz; j++) { Seed(f, seen, q, 0, j); Seed(f, seen, q, nx - 1, j); }
                while (q.Count > 0)
                {
                    int k = q.Dequeue(), i = k % nx, j = k / nx;
                    if (i > 0) Seed(f, seen, q, i - 1, j); if (i + 1 < nx) Seed(f, seen, q, i + 1, j);
                    if (j > 0) Seed(f, seen, q, i, j - 1); if (j + 1 < nz) Seed(f, seen, q, i, j + 1);
                }
                for (int k = 0; k < f.Length; k++) if (!seen[k] && f[k] >= 0) f[k] = (float)(-0.5 * cs);
            }
            void Seed(float[] f, bool[] seen, Queue<int> q, int i, int j) { int k = j * nx + i; if (seen[k] || f[k] < 0) return; seen[k] = true; q.Enqueue(k); }

            /// <summary>0 등고선(안 = 음수, 안이 진행 방향 왼쪽 → 바깥 둘레 반시계, 구멍 시계)</summary>
            public List<List<V2>> Contours(float[] f)
            {
                var next = new Dictionary<long, long>();
                Action<long, long> seg = (a, b) => { next[a] = b; };
                for (int j = 0; j + 1 < nz; j++)
                    for (int i = 0; i + 1 < nx; i++)
                    {
                        float v0 = f[j * nx + i], v1 = f[j * nx + i + 1], v2 = f[(j + 1) * nx + i + 1], v3 = f[(j + 1) * nx + i];
                        bool b0 = v0 < 0, b1 = v1 < 0, b2 = v2 < 0, b3 = v3 < 0;
                        int c = (b0 ? 1 : 0) | (b1 ? 2 : 0) | (b2 ? 4 : 0) | (b3 ? 8 : 0);
                        if (c == 0 || c == 15) continue;
                        // 변 0 아래((i,j)→(i+1,j)), 1 오른쪽((i+1,j)→(i+1,j+1)), 2 위((i+1,j+1)→(i,j+1)), 3 왼쪽((i,j+1)→(i,j))
                        long e0 = Hk(i, j), e1 = Vk(i + 1, j), e2 = Hk(i, j + 1), e3 = Vk(i, j);
                        long[] E = { e0, e1, e2, e3 }; bool[] B = { b0, b1, b2, b3 };
                        int ni = 0; int io0 = -1, io1 = -1, oi0 = -1;
                        for (int k = 0; k < 4; k++)
                        {
                            bool a = B[k], b = B[(k + 1) & 3];
                            if (a && !b) { if (ni++ == 0) io0 = k; else io1 = k; }
                            else if (!a && b) oi0 = k;
                        }
                        if (ni == 1) seg(E[io0], E[oi0]);
                        else
                        {
                            float vc = (v0 + v1 + v2 + v3) * 0.25f;
                            if (vc < 0) { seg(E[io0], E[(io0 + 1) & 3]); seg(E[io1], E[(io1 + 1) & 3]); }
                            else { seg(E[io0], E[(io0 + 3) & 3]); seg(E[io1], E[(io1 + 3) & 3]); }
                        }
                    }
                var loops = new List<List<V2>>(); var done = new HashSet<long>();
                foreach (var k0 in next.Keys)
                {
                    if (done.Contains(k0)) continue;
                    var loop = new List<V2>(); long k = k0; int guard = 0;
                    while (!done.Contains(k) && guard++ < 4000000)
                    {
                        done.Add(k); loop.Add(EdgePt(f, k));
                        long nk; if (!next.TryGetValue(k, out nk)) break; k = nk;
                    }
                    if (loop.Count >= 3) loops.Add(loop);
                }
                return loops;
            }
            long Hk(int i, int j) { return ((long)j * nx + i) * 2; }
            long Vk(int i, int j) { return ((long)j * nx + i) * 2 + 1; }
            V2 EdgePt(float[] f, long key)
            {
                long b = key >> 1; int i = (int)(b % nx), j = (int)(b / nx); bool vert = (key & 1) != 0;
                int ka = j * nx + i, kb = vert ? (j + 1) * nx + i : j * nx + i + 1;
                double va = f[ka], vb = f[kb], t = Math.Abs(va - vb) > 1e-12 ? va / (va - vb) : 0.5;
                t = Geom.Clamp(t, 0, 1);
                V2 A = Node(i, j), Bn = vert ? Node(i, j + 1) : Node(i + 1, j);
                return A + (Bn - A) * t;
            }
        }

        // ---------------------------------------------------------------------
        // 교차로 면 한 장
        // ---------------------------------------------------------------------
        sealed class RoadBox { public RoadGeom g; public double x0, z0, x1, z1; }
        List<RoadBox> roadBoxes; readonly List<JunctionGeom> sheetDone = new List<JunctionGeom>();

        public void BuildSheet(JunctionGeom G)
        {
            G.sheetV = null; G.sheetT = null; G.sheet.Clear(); G.sheetHoles.Clear();
            var J = G.j; if (!G.ok || J.arms.Count < 2 || G.poly.Count < 3) return;
            if (J.arms.All(a => a.cut < 0.5)) return;
            if (roadBoxes == null)
            {
                roadBoxes = new List<RoadBox>();
                foreach (var g in roadGeom.Values)
                {
                    if (!g.ok) continue; double x0 = 1e18, z0 = 1e18, x1 = -1e18, z1 = -1e18;
                    for (int i = 0; i < g.c.Count; i++) { double w = Math.Max(g.wL[i] + g.swL[i], g.wR[i] + g.swR[i]); V2 p = g.c[i]; x0 = Math.Min(x0, p.x - w); x1 = Math.Max(x1, p.x + w); z0 = Math.Min(z0, p.z - w); z1 = Math.Max(z1, p.z + w); }
                    roadBoxes.Add(new RoadBox { g = g, x0 = x0, z0 = z0, x1 = x1, z1 = z1 });
                }
            }
            bool fresh = !G.simple || G.manual;                      // 꼬인 교차로·손으로 이은 교차로: 차로 연결선으로 새로 만듦
            // ---- 합집합 모양 ----
            var polys = new List<List<V2>>(); var caps = new List<V2[]>(); var capR = new List<double>(); var tris = new List<V2[]>();
            var snaps = new List<V2>();
            double eoW = P.lineEdgeW * 0.5; int na = J.arms.Count;
            for (int i = 0; i < na; i++)
            {
                var A = J.arms[i]; V2 M = A.Point(A.cut), l = A.dir.Left;
                var Cprev = G.corners[(i + na - 1) % na]; var C = G.corners[i];
                double yR = -(A.wOut - (Cprev.hasCurb ? A.sh + eoW : 0)), yL = A.wIn - (C.hasCurb ? A.sh + eoW : 0);
                snaps.Add(M + l * yR); snaps.Add(M + l * yL);
                if (fresh && A.cut > 0.3)
                {   // 입구 띠(입구 단면 폭 그대로, 안쪽으로 몇 m)
                    double dep = Math.Min(P.sheetMouthDepth, A.cut * 0.8);
                    V2 a = M + l * yR, b = M + l * yL, c = b - A.dir * dep, d = a - A.dir * dep;
                    tris.Add(new[] { a, b, c }); tris.Add(new[] { a, c, d });
                }
            }
            if (fresh)
            {
                for (int i = 0; i < na; i++)
                {   // 모서리(연석 곡선 ~ 두 입구 가운데)
                    var C = G.corners[i]; if (C.kind == 2) continue;
                    var line = C.hasCurb ? C.rings[3] : C.path; if (line.Count < 2) continue;
                    var sec = new List<V2>(line); sec.Add(C.b.Point(C.b.cut)); sec.Add(C.a.Point(C.a.cut));
                    if (IsSimple(sec) && Math.Abs(Geom.SignedArea(sec)) > 0.5) polys.Add(sec);
                }
                foreach (var L in G.links)
                    for (int k = 0; k + 1 < L.path.Count; k++) { caps.Add(new[] { L.path[k], L.path[k + 1] }); capR.Add(L.hw + 0.15); }
            }
            else polys.Add(G.poly);
            // ---- 격자 범위 ----
            double bx0 = 1e18, bz0 = 1e18, bx1 = -1e18, bz1 = -1e18;
            Action<V2> grow = p => { bx0 = Math.Min(bx0, p.x); bx1 = Math.Max(bx1, p.x); bz0 = Math.Min(bz0, p.z); bz1 = Math.Max(bz1, p.z); };
            foreach (var pl in polys) foreach (var p in pl) grow(p);
            foreach (var t in tris) foreach (var p in t) grow(p);
            for (int k = 0; k < caps.Count; k++) { grow(caps[k][0] + new V2(capR[k], capR[k])); grow(caps[k][0] - new V2(capR[k], capR[k])); grow(caps[k][1] + new V2(capR[k], capR[k])); grow(caps[k][1] - new V2(capR[k], capR[k])); }
            if (bx1 <= bx0 || bz1 <= bz0) return;
            double pad = 2.0, cs = P.sheetCell;
            double area = (bx1 - bx0 + 2 * pad) * (bz1 - bz0 + 2 * pad);
            if (area / (cs * cs) > 3e6) cs = Math.Sqrt(area / 3e6);
            var S = new Sdf(bx0 - pad, bz0 - pad, bx1 + pad, bz1 + pad, cs);
            foreach (var pl in polys) S.Poly(S.u, pl);
            foreach (var t in tris) S.Tri(S.u, t[0], t[1], t[2]);
            for (int k = 0; k < caps.Count; k++) S.Capsule(S.u, caps[k][0], caps[k][1], capR[k]);
            S.FillHoles(S.u);
            // ---- 빼기: 같은 높이 도로 면 ----
            if (P.sheetSubtractRoads)
                foreach (var rb in roadBoxes)
                {
                    if (rb.x1 < S.x0 || rb.x0 > bx1 + pad || rb.z1 < S.z0 || rb.z0 > bz1 + pad) continue;
                    var g = rb.g; int n = g.c.Count;
                    for (int i = 0; i + 1 < n; i++)
                    {
                        V2 c0 = g.c[i], c1 = g.c[i + 1];
                        double wmax = Math.Max(Math.Max(g.wL[i] + g.swL[i], g.wR[i] + g.swR[i]), Math.Max(g.wL[i + 1] + g.swL[i + 1], g.wR[i + 1] + g.swR[i + 1]));
                        if (Math.Max(c0.x, c1.x) + wmax < S.x0 || Math.Min(c0.x, c1.x) - wmax > bx1 + pad || Math.Max(c0.z, c1.z) + wmax < S.z0 || Math.Min(c0.z, c1.z) - wmax > bz1 + pad) continue;
                        V2 l0 = g.t[i].Left, l1 = g.t[i + 1].Left;
                        // 깜빡일 만큼 가까운 높이(네 모서리 모두 tol 안)만 뺌 — 도로가 뚜렷이 낮으면 교차로 면이 덮고, 높으면 도로가 덮음
                        V2 q0 = c0 + l0 * g.wL[i], q1 = c1 + l1 * g.wL[i + 1], q2 = c1 - l1 * g.wR[i + 1], q3 = c0 - l0 * g.wR[i];
                        if (Math.Abs(RoadH(g, i, q0) - G.H(q0)) > P.sheetHeightTol || Math.Abs(RoadH(g, i + 1, q1) - G.H(q1)) > P.sheetHeightTol
                            || Math.Abs(RoadH(g, i + 1, q2) - G.H(q2)) > P.sheetHeightTol || Math.Abs(RoadH(g, i, q3) - G.H(q3)) > P.sheetHeightTol) continue;
                        // 차도 + 인도(연석 포함) 단면 전체 — 인도는 양 끝 점 모두 있을 때
                        double eL0 = g.wL[i] + (g.swL[i] > 0 && g.swL[i + 1] > 0 ? g.swL[i] : 0), eL1 = g.wL[i + 1] + (g.swL[i] > 0 && g.swL[i + 1] > 0 ? g.swL[i + 1] : 0);
                        double eR0 = g.wR[i] + (g.swR[i] > 0 && g.swR[i + 1] > 0 ? g.swR[i] : 0), eR1 = g.wR[i + 1] + (g.swR[i] > 0 && g.swR[i + 1] > 0 ? g.swR[i + 1] : 0);
                        V2 a = c0 + l0 * eL0, b = c1 + l1 * eL1, c = c1 - l1 * eR1, d = c0 - l0 * eR0;
                        S.Tri(S.s, a, b, c); S.Tri(S.s, a, c, d);
                    }
                }
            // ---- 빼기: 먼저 만든 이웃 교차로 면(같은 높이) ----
            foreach (var G2 in sheetDone)
            {
                if (G2.sheetV == null) continue;
                var bb = G2.sheetBox; if (bb[2] < S.x0 || bb[0] > bx1 + pad || bb[3] < S.z0 || bb[1] > bz1 + pad) continue;
                for (int t = 0; t + 2 < G2.sheetT.Count; t += 3)
                {
                    V2 a = G2.sheetV[G2.sheetT[t]], b = G2.sheetV[G2.sheetT[t + 1]], c = G2.sheetV[G2.sheetT[t + 2]], mid = (a + b + c) / 3;
                    if (Math.Abs(G2.H(mid) - G.H(mid)) > P.sheetHeightTol) continue;
                    S.Tri(S.s, a, b, c);
                }
            }
            // ---- 빼기: 모서리 연석 띠·인도 쪽(꼬인 교차로만 — 단순은 다각형이 이미 연석선) ----
            if (fresh)
                foreach (var C in G.corners)
                {
                    if (C.kind == 2) continue;
                    var p = C.path; int pn = p.Count; if (pn < 2) continue;
                    var inner = C.hasCurb ? C.rings[3] : p;
                    double dOut = Math.Max(1.0, Math.Max(C.swA, C.swB) + 0.5); if (C.kind == 0) dOut = Math.Min(dOut, Math.Max(0.5, C.R * 0.9));
                    var outer = new List<V2>(pn);
                    for (int k = 0; k < pn; k++) { V2 od = -((p[Math.Min(pn - 1, k + 1)] - p[Math.Max(0, k - 1)]).Norm).Left; outer.Add(p[k] + od * dOut); }
                    outer = RemoveLoops(outer);
                    var reg = new List<V2>(inner); for (int k = outer.Count - 1; k >= 0; k--) reg.Add(outer[k]);
                    if (IsSimple(reg)) S.Poly(S.s, reg);
                }
            var f = new float[S.u.Length];
            for (int k = 0; k < f.Length; k++) f[k] = Math.Max(S.u[k], -S.s[k]);
            // ---- 등고선 → 다각형 ----
            var loops = S.Contours(f);
            var outers = new List<List<V2>>(); var holes = new List<List<V2>>();
            foreach (var lp in loops)
            {
                var q = SimplifyClosed(lp, P.sheetSimplify);
                if (q.Count < 3) continue;
                double ar = Geom.SignedArea(q);
                if (Math.Abs(ar) < P.sheetMinArea) continue;
                if (ar > 0) outers.Add(q); else holes.Add(q);
            }
            if (outers.Count == 0) { sheetFailed++; return; }
            foreach (var q in outers) SnapTo(q, snaps, cs * 0.75 + 0.05);
            foreach (var q in holes) SnapTo(q, snaps, cs * 0.75 + 0.05);
            // ---- 삼각형 ----
            var V = new List<V2>(); var Tr = new List<int>();
            foreach (var o in outers)
            {
                var hs = holes.Where(h => Geom.InPoly(o, h[0])).ToList();
                List<V2> pv; var idx = EarClipHoles(o, hs, out pv);
                int b0 = V.Count; V.AddRange(pv); foreach (var t in idx) Tr.Add(b0 + t);
                G.sheet.Add(o); G.sheetHoles.AddRange(hs);
            }
            if (Tr.Count < 3) { sheetFailed++; G.sheet.Clear(); G.sheetHoles.Clear(); return; }
            G.sheetV = V; G.sheetT = Tr; sheetBuilt++;
            G.sheetBox = new[] { V.Min(q => q.x), V.Min(q => q.z), V.Max(q => q.x), V.Max(q => q.z) }; sheetDone.Add(G);
            if (fresh) { var big = outers.OrderByDescending(o => Geom.SignedArea(o)).First(); G.poly.Clear(); G.poly.AddRange(big); }
        }

        /// <summary>교차로 면(한 장) 안인가: 바깥 둘레 안 && 구멍 밖</summary>
        public bool InSheet(JunctionGeom G, V2 p)
        {
            if (G.sheet.Count == 0) return G.poly.Count >= 3 && Geom.InPoly(G.poly, p);
            bool inO = false; foreach (var o in G.sheet) if (Geom.InPoly(o, p)) { inO = true; break; }
            if (!inO) return false;
            foreach (var h in G.sheetHoles) if (Geom.InPoly(h, p)) return false;
            return true;
        }

        /// <summary>입구 모서리 점에 가까운(반경 r) 꼭짓점들을 그 점 하나로(등고선이 모서리를 깎은 것 되돌림)</summary>
        static void SnapTo(List<V2> q, List<V2> targets, double r)
        {
            foreach (var T in targets)
            {
                int n = q.Count; if (n < 4) return;
                int best = -1; double bd = r * r;
                for (int i = 0; i < n; i++) { double d = (q[i] - T).Len2; if (d < bd) { bd = d; best = i; } }
                if (best < 0) continue;
                // best 둘레로 반경 안에 이어진 점들
                int a = best, b = best, guard = 0;
                while ((q[(a + n - 1) % n] - T).Len2 < r * r && guard++ < n) a = (a + n - 1) % n;
                guard = 0;
                while ((q[(b + 1) % n] - T).Len2 < r * r && guard++ < n) b = (b + 1) % n;
                if (a == (b + 1) % n) continue;                                   // 전부 반경 안(아주 작은 고리)
                var o = new List<V2>(n);
                int k = (b + 1) % n;
                while (k != a) { o.Add(q[k]); k = (k + 1) % n; }
                o.Add(T);
                if (o.Count >= 3) { q.Clear(); q.AddRange(o); }
            }
        }

        /// <summary>닫힌 선 다듬기(더글러스–포이커)</summary>
        static List<V2> SimplifyClosed(List<V2> p, double tol)
        {
            int n = p.Count; if (n < 6) return new List<V2>(p);
            int far = 0; double bd = -1;
            for (int i = 1; i < n; i++) { double d = (p[i] - p[0]).Len2; if (d > bd) { bd = d; far = i; } }
            var keep = new bool[n + 1]; keep[0] = keep[far] = keep[n] = true;
            var st = new Stack<int[]>(); st.Push(new[] { 0, far }); st.Push(new[] { far, n });
            Func<int, V2> P0 = i => p[i % n];
            while (st.Count > 0)
            {
                var ab = st.Pop(); int a = ab[0], b = ab[1]; if (b - a < 2) continue;
                V2 A = P0(a), B = P0(b), AB = B - A; double L = AB.Len; int mi = -1; double md = tol;
                for (int i = a + 1; i < b; i++)
                {
                    V2 Q = P0(i); double d = L > 1e-9 ? Math.Abs(V2.Cross(AB, Q - A)) / L : (Q - A).Len;
                    if (d > md) { md = d; mi = i; }
                }
                if (mi >= 0) { keep[mi] = true; st.Push(new[] { a, mi }); st.Push(new[] { mi, b }); }
            }
            var o = new List<V2>(); for (int i = 0; i < n; i++) if (keep[i]) o.Add(p[i]);
            return o;
        }

        /// <summary>구멍 있는 다각형 → 귀 자르기(구멍은 다리로 이어 붙임). 바깥 반시계, 구멍 시계. 반환 삼각형은 반시계(a,b,c)</summary>
        static List<int> EarClipHoles(List<V2> outer, List<List<V2>> holes, out List<V2> pv)
        {
            var poly = new List<V2>(outer);
            foreach (var h0 in holes.OrderByDescending(h => h.Max(p => p.x)))
            {
                var h = new List<V2>(h0); if (Geom.SignedArea(h) > 0) h.Reverse();
                int hi = 0; for (int i = 1; i < h.Count; i++) if (h[i].x > h[hi].x) hi = i;
                V2 M = h[hi]; int bi = -1; double bx = double.MaxValue;
                for (int a = 0; a < poly.Count; a++)
                {
                    V2 p0 = poly[a], p1 = poly[(a + 1) % poly.Count];
                    if ((p0.z > M.z) == (p1.z > M.z)) continue;
                    double x = p0.x + (M.z - p0.z) * (p1.x - p0.x) / (p1.z - p0.z);
                    if (x < M.x - 1e-9 || x >= bx) continue;
                    bx = x; bi = p0.x > p1.x ? a : (a + 1) % poly.Count;
                }
                if (bi < 0) continue;
                // 다리 끝이 다른 변을 가로지르면 가장 가까운 보이는 꼭짓점으로
                V2 Bp = poly[bi]; double bestD = double.MaxValue; int bestI = -1;
                for (int a = 0; a < poly.Count; a++)
                {
                    V2 c = poly[a]; if (c.x < M.x - 1e-9) continue;
                    double d = (c - M).Len2; if (d >= bestD) continue;
                    bool hit = false;
                    for (int e = 0; e < poly.Count && !hit; e++) { int e1 = (e + 1) % poly.Count; if (e == a || e1 == a) continue; if (SegX(M, c, poly[e], poly[e1])) hit = true; }
                    for (int e = 0; e < h.Count && !hit; e++) { int e1 = (e + 1) % h.Count; if (e == hi || e1 == hi) continue; if (SegX(M, c, h[e], h[e1])) hit = true; }
                    if (!hit) { bestD = d; bestI = a; }
                }
                if (bestI >= 0) bi = bestI;
                var np = new List<V2>(poly.Count + h.Count + 2);
                for (int i = 0; i <= bi; i++) np.Add(poly[i]);
                for (int i = 0; i <= h.Count; i++) np.Add(h[(hi + i) % h.Count]);
                np.Add(poly[bi]);
                for (int i = bi + 1; i < poly.Count; i++) np.Add(poly[i]);
                poly = np;
            }
            pv = poly;
            return EarClip(poly);
        }

        static List<int> EarClip(List<V2> poly)
        {
            var res = new List<int>(); int n = poly.Count; if (n < 3) return res;
            var idx = new List<int>(n); for (int i = 0; i < n; i++) idx.Add(i);
            if (Geom.SignedArea(poly) < 0) idx.Reverse();
            double eps = 1e-10; int guard = 0, k = 0;
            while (idx.Count > 3 && guard++ < 200000)
            {
                bool cut = false; int m = idx.Count;
                for (int t = 0; t < m; t++)
                {
                    int kk = (k + t) % m;
                    int ia = idx[(kk + m - 1) % m], ib = idx[kk], ic = idx[(kk + 1) % m];
                    V2 a = poly[ia], b = poly[ib], c = poly[ic];
                    if (V2.Cross(b - a, c - b) <= eps) continue;
                    bool inside = false;
                    for (int q = 0; q < m && !inside; q++)
                    {
                        int iq = idx[q]; if (iq == ia || iq == ib || iq == ic) continue;
                        V2 p = poly[iq];
                        if ((p - a).Len2 < 1e-14 || (p - b).Len2 < 1e-14 || (p - c).Len2 < 1e-14) continue;   // 다리 이음점(같은 자리)
                        double d1 = V2.Cross(b - a, p - a), d2 = V2.Cross(c - b, p - b), d3 = V2.Cross(a - c, p - c);
                        if (d1 >= -1e-12 && d2 >= -1e-12 && d3 >= -1e-12) inside = true;
                    }
                    if (inside) continue;
                    res.Add(ia); res.Add(ib); res.Add(ic);
                    idx.RemoveAt(kk); k = Math.Max(0, kk - 1); cut = true; break;
                }
                if (!cut)
                {   // 숫자 문제: 가장 덜 오목한 점 하나를 버림(부채꼴보다 나음)
                    if (eps > -1e-6) { eps = -1e-6; continue; }
                    break;
                }
            }
            if (idx.Count == 3) { res.Add(idx[0]); res.Add(idx[1]); res.Add(idx[2]); }
            return res;
        }
    }
}
