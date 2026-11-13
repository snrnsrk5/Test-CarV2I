// PNU 도로 생성기 — 표준노드링크(방향별 링크) → 도로망
//   교차로 = 짧은 링크로 이어진 교차로 노드 묶음
//   도로   = 교차로 사이를 잇는 방향별 링크 사슬, 상행·하행 사슬을 짝지어 양방향 도로 하나로(짝 없으면 일방통행)
//   팔(Arm) = 교차로에 붙은 도로 한쪽 끝
using System;
using System.Collections.Generic;
using System.Linq;

namespace PNU.RoadGen
{
    // ---------- 입력 ----------
    public class InLink { public string id, f, t, name, rank, type, connect; public int lanes, spd; public List<V2> pts; public double len; public int layer; }   // layer: OSM 층(다리 +, 지하 −) 참고값
    public class InNode { public string id, type, name; public V2 p; }
    public class InTurn { public string node, from, to, type; }

    // ---------- 도로망 ----------
    public class LanePiece { public double s0, s1; public int lanes; public int spd; public string linkId, rank, type, connect; public int layer; }

    public class Junction
    {
        public int id; public string name; public V2 center; public double height; public bool keep;
        public bool merge;                                          // 합류·분기점(노드링크 교차로 노드 아님: 연결로 시종점·구조물 시종점에서 길이 갈라지거나 합쳐짐) — 횡단보도·신호 없음
        public string keyOv;                                        // 도로 편집기에서 온 교차로 키(새 교차로 J+기록id) — 있으면 Keys.Junction 이 이것
        public List<string> nodes = new List<string>();
        public List<Arm> arms = new List<Arm>();
        public List<InLink> inner = new List<InLink>();            // 교차로 안 링크(회전 이동)
        public bool IsRealJunction { get { return arms.Count >= 3 || (arms.Count == 2 && Math.Abs(V2.Dot(arms[0].dir, arms[1].dir)) < 0.85); } }
    }

    public class Arm
    {
        public Junction j; public Road road; public bool atStart;   // 도로 시작점(s=0)이 이 교차로에 붙었는지
        public V2 dir;                                              // 교차로에서 바깥으로
        public V2 basePt;                                           // 팔 직선 위 한 점(도로 중심선, 교차로에서 ~20m)
        public double wOut, wIn;                                    // 바깥으로 나가는 쪽(=오른쪽) / 들어오는 쪽(=왼쪽) 차도 폭(중앙에서 가장자리선 바깥 갓길까지)
        public double swOut, swIn;                                  // 그쪽 인도 폭(0 = 없음)
        public int lanesOut, lanesIn;
        public double cut;                                          // 교차로 중심에서 입구까지 팔 방향 거리(계산 결과)
        public bool crosswalk;                                      // 입구에 횡단보도(+들어오는 쪽 정지선)
        public int crosswalkKind = 1; public bool crosswalkYellow;  // 종류(CrosswalkKind), 노란색 여부
        public double crosswalkWidth;                               // 이 팔 횡단보도 폭(m)
        public double stopDist;                                     // 입구에서 정지선 가운데까지 거리(0 = 정지선 없음)
        public double sh;                                           // 이 팔 끝의 길어깨 폭(차로 끝 → 연석)
        public int edgeType;                                        // 이 팔 도로의 끝선 종류(EdgeLine 번호)
        public double fixedCut = double.NaN;                        // 도로 편집기 입구(교차로 중심에서 팔 방향 거리) — 있으면 계산 대신 이것
        public double Angle { get { return dir.Angle; } }
        public V2 Point(double t) { double t0 = V2.Dot(basePt - j.center, dir); return basePt + dir * (t - t0); }   // 팔 직선 위, 중심 기준 거리 t
    }

    public class Road
    {
        public int id; public string name, rank; public bool oneWay, divided;
        public List<V2> center;                                     // 시작→끝 = 정방향(fwd)
        public double[] cum; public double Length { get { return cum[cum.Length - 1]; } }
        public double[] h;                                          // 중심선 점별 높이
        public List<LanePiece> fwd = new List<LanePiece>(), bwd = new List<LanePiece>();
        public Junction jStart, jEnd; public Arm armStart, armEnd;
        public List<string> fwdLinks = new List<string>(), bwdLinks = new List<string>();
        public double s0, s1;                                       // 교차로 입구로 자른 실제 도로 구간
        // 도로 편집기에서 온 도로(EditorNet.cs): 저장 이름표, 차로 끝 ID 앞부분·끝 표시, 입구·가장자리 폭, 사람이 정한 높이
        public string keyOv, ekS, ekE, tagS = "s", tagE = "e";
        public EdShape ed;
        public double[] hFixS, hFixY;                               // 높이(중심선 s 마다) — 있으면 층 나누기·골 메우기가 안 건드림
        public bool hBase; public double[] tmS; public byte[] tmV;        // hBase: 편집기 기본 높이(사람이 정한 것 아님 — 면 맞춤 무게 1) · tmS/tmV: 편집기 구조 판정(0 땅·1 다리·2 덮개·3 개착)
        public int LanesAt(List<LanePiece> ps, double s)
        {
            if (ps.Count == 0) return 0;
            foreach (var p in ps) if (s >= p.s0 && s <= p.s1) return p.lanes;
            return s < ps[0].s0 ? ps[0].lanes : ps[ps.Count - 1].lanes;
        }
        public int SpeedAt(double s)
        {
            var ps = fwd.Count > 0 ? fwd : bwd;
            foreach (var p in ps) if (s >= p.s0 && s <= p.s1) return p.spd;
            return ps.Count > 0 ? ps[0].spd : 50;
        }
    }

    public class RoadNetwork
    {
        public bool gwStacked;                                       // 광안대교 아래층 옮김 했음
        public List<Junction> junctions = new List<Junction>();
        public List<Road> roads = new List<Road>();
        public Dictionary<string, InNode> nodes = new Dictionary<string, InNode>();
        public Dictionary<string, InLink> links = new Dictionary<string, InLink>();
        public List<InTurn> turns = new List<InTurn>();
        public List<string> log = new List<string>();
    }

    public static class NetworkBuilder
    {
        class Seg { public InLink link; public double s0, s1; }
        class Chain
        {
            public List<InLink> links = new List<InLink>(); public List<Seg> segs = new List<Seg>(); public Junction from, to; public List<V2> pts; public bool used; public double len;
            /// <summary>구조 종류(길이로 가장 많은 것): 0 땅, 1 고가·교량(001/003), 2 지하차도·터널(002/004)</summary>
            /// <summary>OSM 층 참고값(길이 다수결)</summary>
            public int Layer()
            {
                var d = new Dictionary<int, double>();
                foreach (var g in segs) { double v; d.TryGetValue(g.link.layer, out v); d[g.link.layer] = v + g.s1 - g.s0; }
                return d.Count == 0 ? 0 : d.OrderByDescending(kv => kv.Value).First().Key;
            }
            /// <summary>이름들(빈 이름·'-' 빼고)</summary>
            public HashSet<string> Names()
            {
                var o = new HashSet<string>(); foreach (var g in segs) { var n = g.link.name ?? ""; if (n.Length > 0 && n != "-") o.Add(n); } return o;
            }
            public int Struct()
            {
                var L = new double[3];
                foreach (var g in segs) { string t = g.link.type; L[t == "001" || t == "003" ? 1 : t == "002" || t == "004" ? 2 : 0] += g.s1 - g.s0; }
                return L[1] >= L[0] && L[1] >= L[2] ? 1 : L[2] > L[0] ? 2 : 0;
            }
        }

        public static RoadNetwork Build(List<InNode> nodes, List<InLink> links, List<InTurn> turns, GenParams P)
        {
            // 두 번 돌림: 1차에서 상행·하행 짝이 서로 다른 교차로 묶음에 붙어 있으면(큰 교차로) 그 묶음을 합쳐서 2차
            var extra = new List<KeyValuePair<string, string>>();
            RoadNetwork net = null;
            for (int pass = 0; pass < 2; pass++)
            {
                var found = new List<KeyValuePair<string, string>>();
                net = BuildOnce(nodes, links, turns, P, extra, pass == 0 ? found : null);
                if (pass == 0) { if (found.Count == 0) break; extra.AddRange(found); net.log.Add("merged junction groups " + found.Count); }
            }
            return net;
        }

        static RoadNetwork BuildOnce(List<InNode> nodes, List<InLink> links, List<InTurn> turns, GenParams P, List<KeyValuePair<string, string>> extra, List<KeyValuePair<string, string>> mergeOut)
        {
            var net = new RoadNetwork();
            foreach (var n in nodes) net.nodes[n.id] = n;
            foreach (var l in links) { if (l.pts == null || l.pts.Count < 2) continue; l.len = Geom.Length(l.pts); net.links[l.id] = l; }
            if (turns != null) net.turns = turns;
            var outL = new Dictionary<string, List<InLink>>(); var inL = new Dictionary<string, List<InLink>>();
            foreach (var l in net.links.Values) { Add(outL, l.f, l); Add(inL, l.t, l); }

            // 1) 교차로 묶음: 짧은 링크로 이어진 교차로 노드 / 같은 이름 가까운 노드 / 아주 가까운 노드 / 1차에서 찾은 합치기
            var parent = new Dictionary<string, string>();
            Func<string, string> find = null;
            find = a => { string r = a; while (parent[r] != r) r = parent[r]; while (parent[a] != r) { string nx = parent[a]; parent[a] = r; a = nx; } return r; };
            var members = new Dictionary<string, List<string>>();
            // 두 교차로 노드를 잇는 긴 링크(=그 사이에 진짜 도로 구간이 있음)가 있으면 이름이 같고 가까워도 한 교차로로 묶지 않음
            var longLink = new HashSet<string>();
            foreach (var l in net.links.Values)
                if (l.len > P.junctionLinkMaxSameName) longLink.Add(string.CompareOrdinal(l.f, l.t) < 0 ? l.f + "|" + l.t : l.t + "|" + l.f);
            Func<string, string, bool> roadBetween = (x, y) => longLink.Contains(string.CompareOrdinal(x, y) < 0 ? x + "|" + y : y + "|" + x);
            // limitR > 0: 합친 묶음의 반경(중심에서 가장 먼 노드)이 이보다 크면 합치지 않음(여러 교차로가 줄줄이 붙는 것 방지)
            Func<string, string, double, bool> union = (x, y, limitR) =>
            {
                if (!parent.ContainsKey(x) || !parent.ContainsKey(y)) return false;
                string ra = find(x), rb = find(y); if (ra == rb) return true;
                var all = members[ra].Concat(members[rb]).ToList();
                if (limitR > 0)
                {
                    V2 c = new V2(0, 0); foreach (var id in all) c = c + net.nodes[id].p; c = c / all.Count;
                    foreach (var id in all) if ((net.nodes[id].p - c).Len > limitR) return false;
                    foreach (var u in members[ra]) foreach (var w in members[rb]) if (roadBetween(u, w)) return false;
                }
                parent[ra] = rb; members[rb] = all; members.Remove(ra); return true;
            };
            // 교차로 노드(101) + 길이 갈라지거나 합쳐지는 노드(이웃 노드 3개 이상: 연결로·지하차도·터널 시종점 등). 양방향 같은 노드를 쓰는 길의 중간 점은 이웃 2개라 빠짐
            var branch = new HashSet<string>();
            if (P.mergeJunctions)
                foreach (var n in net.nodes.Values)
                {
                    if (n.type == "101") continue;
                    var nb = new HashSet<string>(); List<InLink> li, lo;
                    if (inL.TryGetValue(n.id, out li)) foreach (var x in li) nb.Add(x.f);
                    if (outL.TryGetValue(n.id, out lo)) foreach (var x in lo) nb.Add(x.t);
                    if (nb.Count >= 3) branch.Add(n.id);
                }
            Func<InNode, bool> isJ = n => n != null && (n.type == "101" || branch.Contains(n.id));
            var jn = net.nodes.Values.Where(isJ).ToList();
            foreach (var n in jn) { parent[n.id] = n.id; members[n.id] = new List<string> { n.id }; }
            var addrName = new System.Text.RegularExpressions.Regex(@"\d+(-\d+)?$");
            foreach (var l in net.links.Values)
            {
                InNode a, b;
                if (!net.nodes.TryGetValue(l.f, out a) || !net.nodes.TryGetValue(l.t, out b) || !isJ(a) || !isJ(b)) continue;
                bool same = !string.IsNullOrEmpty(a.name) && a.name == b.name && !addrName.IsMatch(a.name);
                if (l.len < P.junctionLinkMax) union(a.id, b.id, 0);
                else if (same && l.len < P.junctionLinkMaxSameName) union(a.id, b.id, P.junctionMaxRadius);
            }
            for (int i = 0; i < jn.Count; i++)
                for (int k = i + 1; k < jn.Count; k++)
                {
                    double d = (jn[i].p - jn[k].p).Len;
                    if (d < P.junctionNodeNear) union(jn[i].id, jn[k].id, P.junctionMaxRadius);
                    else if (d < P.junctionSameNameDist && !string.IsNullOrEmpty(jn[i].name) && jn[i].name == jn[k].name && !addrName.IsMatch(jn[i].name)) union(jn[i].id, jn[k].id, P.junctionMaxRadius);
                }
            foreach (var kv in extra) union(kv.Key, kv.Value, P.junctionMaxRadius);
            var jOf = new Dictionary<string, Junction>();
            foreach (var id in parent.Keys.ToList())
            {
                string r = find(id); Junction J;
                if (!jOf.TryGetValue(r, out J)) { J = new Junction { id = net.junctions.Count }; net.junctions.Add(J); jOf[r] = J; }
                J.nodes.Add(id); jOf[id] = J;
            }
            foreach (var J in net.junctions)
            {
                V2 c = new V2(0, 0); var names = new Dictionary<string, int>();
                foreach (var id in J.nodes) { c = c + net.nodes[id].p; var nm = net.nodes[id].name; if (!string.IsNullOrEmpty(nm)) names[nm] = names.ContainsKey(nm) ? names[nm] + 1 : 1; }
                J.center = c / J.nodes.Count;
                J.merge = J.nodes.All(id => net.nodes[id].type != "101");
                J.name = names.Count > 0 ? names.OrderByDescending(kv => kv.Value).First().Key : "";
            }
            Func<string, Junction> JAt = id => { Junction J; return jOf.TryGetValue(id, out J) ? J : null; };

            // 2) 방향별 사슬: 교차로(또는 시작점)에서 나가 다음 교차로(또는 끝)까지
            var usedL = new HashSet<string>();
            var chains = new List<Chain>();
            foreach (var l in net.links.Values)
            {
                var a = JAt(l.f); var b = JAt(l.t);
                if (a != null && a == b) { a.inner.Add(l); usedL.Add(l.id); }
            }
            Func<InLink, InLink> next = cur =>
            {
                if (JAt(cur.t) != null) return null;
                List<InLink> outs; if (!outL.TryGetValue(cur.t, out outs)) return null;
                V2 dIn = (cur.pts[cur.pts.Count - 1] - cur.pts[Math.Max(0, cur.pts.Count - 2)]).Norm;
                InLink best = null; double bestDot = 0.2;
                foreach (var o in outs)
                {
                    if (usedL.Contains(o.id) || o.t == cur.f) continue;
                    V2 dOut = (o.pts[1] - o.pts[0]).Norm;
                    double d = V2.Dot(dIn, dOut);
                    if (d > bestDot) { bestDot = d; best = o; }
                }
                return best;
            };
            Action<InLink> grow = start =>
            {
                var ch = new Chain(); var cur = start;
                while (cur != null && !usedL.Contains(cur.id)) { ch.links.Add(cur); usedL.Add(cur.id); cur = next(cur); }
                ch.from = JAt(ch.links[0].f); ch.to = JAt(ch.links[ch.links.Count - 1].t);
                ch.pts = new List<V2>(); double acc = 0;
                foreach (var cl in ch.links)
                {
                    foreach (var p in cl.pts) if (ch.pts.Count == 0 || (p - ch.pts[ch.pts.Count - 1]).Len > 0.05) ch.pts.Add(p);
                    double L = Geom.Length(ch.pts); ch.segs.Add(new Seg { link = cl, s0 = acc, s1 = L }); acc = L;
                }
                ch.len = Geom.Length(ch.pts);
                if (ch.pts.Count >= 2) chains.Add(ch);
            };
            foreach (var l in net.links.Values) if (!usedL.Contains(l.id) && JAt(l.f) != null) grow(l);
            foreach (var l in net.links.Values)
                if (!usedL.Contains(l.id))
                {
                    List<InLink> ins; bool hasPrev = inL.TryGetValue(l.f, out ins) && ins.Any(x => !usedL.Contains(x.id) && x.f != l.t);
                    if (!hasPrev) grow(l);
                }
            foreach (var l in net.links.Values) if (!usedL.Contains(l.id)) grow(l);

            // 2b) 반대 방향 사슬이 끝나는 교차로를 이 사슬이 그냥 지나가면(그쪽 노드 없음) 거기서 자름 → 양쪽 끝이 같아져 짝지을 수 있음
            chains = SplitAtJunctions(chains, net.junctions, P);

            // 3) 상행·하행 짝짓기 (1차: 끝 교차로가 달라도 가까우면 짝 + 합칠 묶음 기록)
            Func<Junction, Junction, bool> sameOrNear = (x, y) => x == null || y == null || x == y || (mergeOut != null && (x.center - y.center).Len < P.junctionMergeDist);
            foreach (var A in chains.OrderByDescending(c => c.len))
            {
                if (A.used) continue;
                Chain best = null; double bestD = double.MaxValue;
                foreach (var B in chains)
                {
                    if (B == A || B.used) continue;
                    if (!sameOrNear(A.from, B.to) || !sameOrNear(A.to, B.from)) continue;
                    if (A.from == null && A.to == null) continue;
                    if (P.pairByStructure && A.Struct() != B.Struct()) continue;
                    if (P.pairByLayer && A.Layer() != B.Layer()) continue;          // OSM 층이 다르면(광안대교 위층·아래층 등) 짝 아님      // 다리 위 길과 다리 밑(옆) 길, 광안대교 위층·아래층 접속로처럼 같은 자리라도 구조가 다르면 짝이 아님
                    if (P.pairByName) { var na = A.Names(); var nb = B.Names(); if (na.Count > 0 && nb.Count > 0 && !na.Overlaps(nb)) continue; }   // 이름이 서로 다른 길(광안대로 + 수영강변대로 등)은 짝 아님 — 나란히 가는 다른 길을 왕복 한 도로로 묶어 차로가 2배로 보이던 것
                    double mean, max; if (!Parallel(A.pts, B.pts, out mean, out max)) continue;
                    if (mean < P.pairMeanMax && max < P.pairMaxMax && mean < bestD) { bestD = mean; best = B; }
                }
                A.used = true;
                if (best != null && mergeOut != null)
                {
                    if (A.from != null && best.to != null && A.from != best.to) mergeOut.Add(new KeyValuePair<string, string>(A.from.nodes[0], best.to.nodes[0]));
                    if (A.to != null && best.from != null && A.to != best.from) mergeOut.Add(new KeyValuePair<string, string>(A.to.nodes[0], best.from.nodes[0]));
                }
                var R = new Road { id = net.roads.Count, oneWay = best == null };
                if (best != null) best.used = true;
                MakeRoad(R, A, best, P);
                net.roads.Add(R);
            }

            // 4) 팔
            BuildArms(net, P);
            // 5) 팔이 둘뿐이고 거의 일직선인 '교차로'(옆길이 노드링크에 없는 곳)는 없애고 두 도로를 하나로 이음
            int merged = 0;
            for (int guard = 0; guard < 10000; guard++)
            {
                var J = net.junctions.FirstOrDefault(x => !x.keep && x.arms.Count == 2 && V2.Dot(x.arms[0].dir, x.arms[1].dir) < -0.85 && x.arms[0].road != x.arms[1].road);
                if (J == null) break;
                MergeThrough(net, J, P); merged++;
            }
            int thru = P.mergeThroughStructures ? MergeThroughStructures(net, P) : 0;
            if (thru > 0) net.log.Add("structures passing through junctions joined " + thru);
            net.junctions.RemoveAll(x => x.arms.Count == 0);
            for (int i = 0; i < net.junctions.Count; i++) net.junctions[i].id = i;
            for (int i = 0; i < net.roads.Count; i++) net.roads[i].id = i;
            net.log.Add("pass-through junctions merged " + merged);
            net.log.Add("junctions " + net.junctions.Count + " roads " + net.roads.Count + " (oneway " + net.roads.Count(r => r.oneWay) + ") chains " + chains.Count);
            return net;
        }

        static void BuildArms(RoadNetwork net, GenParams P)
        {
            foreach (var J in net.junctions) J.arms.Clear();
            foreach (var R in net.roads)
            {
                if (R.jStart != null) { R.armStart = MakeArm(R, R.jStart, true, P); R.jStart.arms.Add(R.armStart); }
                if (R.jEnd != null && !(R.jEnd == R.jStart && R.Length < 40)) { R.armEnd = MakeArm(R, R.jEnd, false, P); R.jEnd.arms.Add(R.armEnd); }
            }
            foreach (var J in net.junctions) { J.arms.Sort((a, b) => a.Angle.CompareTo(b.Angle)); OrderBundles(J, P); }
        }

        /// <summary>거의 같은 방향으로 나가는 팔(분리 도로 상·하행, 나란한 길)은 방향각이 아니라 옆으로 놓인 순서로
        /// (반시계 = 바깥을 보고 오른쪽 → 왼쪽). 방향각 순서가 실제 자리와 거꾸로면 교차로 면이 꼬임</summary>
        public static void OrderBundles(Junction J, GenParams P)
        {
            var A = J.arms; int n = A.Count; if (n < 3) return;
            double lim = P.sideBySideDeg * Math.PI / 180;
            int start = 0; double big = -1;
            for (int i = 0; i < n; i++) { double g = A[(i + 1) % n].Angle - A[i].Angle; if (g <= 0) g += 2 * Math.PI; if (g > big) { big = g; start = (i + 1) % n; } }
            var seq = new List<Arm>(); for (int i = 0; i < n; i++) seq.Add(A[(start + i) % n]);
            var o = new List<Arm>(); int k = 0;
            while (k < n)
            {
                int e = k;
                while (e + 1 < n) { double g = seq[e + 1].Angle - seq[e].Angle; if (g < 0) g += 2 * Math.PI; if (g >= lim) break; e++; }
                if (e > k)
                {
                    var grp = seq.GetRange(k, e - k + 1);
                    V2 dm = new V2(0, 0); foreach (var a in grp) dm = dm + a.dir; dm = dm.Norm;
                    o.AddRange(grp.OrderBy(a => V2.Cross(dm, a.basePt - J.center)));
                }
                else o.Add(seq[k]);
                k = e + 1;
            }
            A.Clear(); A.AddRange(o);
        }

        static void ReverseRoad(Road R)
        {
            R.center.Reverse(); R.cum = Geom.CumLen(R.center); double L = R.Length;
            var f = R.fwd; R.fwd = R.bwd; R.bwd = f;
            foreach (var p in R.fwd.Concat(R.bwd)) { double a = L - p.s1, b = L - p.s0; p.s0 = a; p.s1 = b; }
            R.fwd.Sort((x, y) => x.s0.CompareTo(y.s0)); R.bwd.Sort((x, y) => x.s0.CompareTo(y.s0));
            var j = R.jStart; R.jStart = R.jEnd; R.jEnd = j;
            var fl = R.fwdLinks; R.fwdLinks = R.bwdLinks; R.bwdLinks = fl;
            if (R.oneWay && R.fwd.Count == 0) { }   // 일방통행은 뒤집으면 진행 방향이 bwd 쪽에 남음
        }

        /// <summary>일직선 2갈래 교차로 J 를 없애고 a(→J) + b(J→) 를 한 도로로</summary>
        static void MergeThrough(RoadNetwork net, Junction J, GenParams P) { MergeThrough(net, J, J.arms[0].road, J.arms[1].road, P); }
        static bool MergeThrough(RoadNetwork net, Junction J, Road a, Road b, GenParams P)
        {
            if (a == b || a.jStart == a.jEnd || b.jStart == b.jEnd) { J.keep = true; return false; }
            if (a.jEnd != J) ReverseRoad(a);          // a 는 J 로 끝나게
            if (b.jStart != J) ReverseRoad(b);        // b 는 J 에서 시작하게
            if (a.oneWay != b.oneWay || (a.oneWay && (a.fwd.Count == 0) != (b.fwd.Count == 0))) { J.keep = true; BuildArms(net, P); return false; }   // 성격이 다르면 그대로 둠
            double La = a.Length, Lb = b.Length;
            var pts = new List<V2>(a.center);
            foreach (var p in b.center) if ((p - pts[pts.Count - 1]).Len > 0.5) pts.Add(p);
            // 이음매 다듬기
            a.center = Geom.SmoothPts(Geom.Resample(pts, P.step), P.smoothSigmaM / P.step); a.cum = Geom.CumLen(a.center);
            double k = a.Length / Math.Max(1e-6, La + Lb);
            foreach (var p in a.fwd.Concat(a.bwd)) { p.s0 *= k; p.s1 *= k; }
            foreach (var p in b.fwd) a.fwd.Add(new LanePiece { s0 = (La + p.s0) * k, s1 = (La + p.s1) * k, lanes = p.lanes, spd = p.spd, linkId = p.linkId, rank = p.rank, type = p.type, connect = p.connect, layer = p.layer });
            foreach (var p in b.bwd) a.bwd.Add(new LanePiece { s0 = (La + p.s0) * k, s1 = (La + p.s1) * k, lanes = p.lanes, spd = p.spd, linkId = p.linkId, rank = p.rank, type = p.type, connect = p.connect, layer = p.layer });
            a.fwdLinks.AddRange(b.fwdLinks); a.bwdLinks.AddRange(b.bwdLinks);
            if (string.IsNullOrEmpty(a.name)) a.name = b.name;
            a.jEnd = b.jEnd; a.s1 = a.Length;
            net.roads.Remove(b);
            J.arms.Clear();
            BuildArms(net, P);
            return true;
        }

        /// <summary>교차로 끝 쪽 도로유형(구조): 1 다리·고가, -1 지하차도·터널, 0 땅</summary>
        static int EndClass(Arm A)
        {
            var R = A.road; double s = A.atStart ? 0 : R.Length; string t = null;
            foreach (var p in R.fwd.Concat(R.bwd)) if (s >= p.s0 - 1 && s <= p.s1 + 1 && !string.IsNullOrEmpty(p.type) && p.type != "000") { t = p.type; break; }
            return t == "001" || t == "003" ? 1 : t == "002" || t == "004" ? -1 : 0;
        }

        /// <summary>교차로를 위·아래로 그냥 지나가는 길(지하차도·터널·고가·다리가 교차로 노드 묶음에 걸린 것): 마주 보는 같은 구조 팔 둘을 한 도로로 이어 교차로에서 뺌</summary>
        static int MergeThroughStructures(RoadNetwork net, GenParams P)
        {
            int merged = 0; var failed = new HashSet<string>();
            for (int guard = 0; guard < 2000; guard++)
            {
                Junction bestJ = null; Road ba = null, bb = null; double bestDot = -0.85;
                foreach (var J in net.junctions)
                {
                    if (J.arms.Count < 3 || J.merge) continue;                 // 합류·분기점은 그대로(갈라지는 길이 이어져야 함)
                    var cand = J.arms.Where(a => EndClass(a) != 0).ToList();
                    for (int i = 0; i < cand.Count; i++)
                        for (int k = i + 1; k < cand.Count; k++)
                        {
                            Arm x = cand[i], y = cand[k];
                            if (x.road == y.road || EndClass(x) != EndClass(y) || x.road.oneWay != y.road.oneWay) continue;
                            double d = V2.Dot(x.dir, y.dir); if (d >= bestDot) continue;
                            if (x.road.oneWay)
                            {   // 일방: 하나는 들어오고 하나는 나가야
                                bool xin = x.atStart ? x.road.bwd.Count > 0 : x.road.fwd.Count > 0, yin = y.atStart ? y.road.bwd.Count > 0 : y.road.fwd.Count > 0;
                                if (xin == yin) continue;
                                if (!xin) { var t = x; x = y; y = t; }
                            }
                            if (failed.Contains(J.id + ":" + x.road.id + ":" + y.road.id)) continue;
                            bestDot = d; bestJ = J; ba = x.road; bb = y.road;
                        }
                }
                if (bestJ == null) break;
                bool keep0 = bestJ.keep;
                if (MergeThrough(net, bestJ, ba, bb, P)) merged++;
                else { failed.Add(bestJ.id + ":" + ba.id + ":" + bb.id); failed.Add(bestJ.id + ":" + bb.id + ":" + ba.id); bestJ.keep = keep0; }
            }
            return merged;
        }

        static void Add<T>(Dictionary<string, List<T>> d, string k, T v) { List<T> l; if (!d.TryGetValue(k, out l)) { l = new List<T>(); d[k] = l; } l.Add(v); }

        /// <summary>A 와 B(반대 방향)가 나란한지: A 위 점 중 B 와 겹치는 부분에서만 거리·방향을 봄(길이가 달라도 됨)</summary>
        static bool Parallel(List<V2> A, List<V2> B, out double mean, out double max)
        {
            mean = max = 1e9;
            double la = Geom.Length(A), lb = Geom.Length(B);
            int n = Math.Max(6, (int)(la / 5));
            var a = Geom.ResampleN(A, n); var at = Geom.Tangents(a); var bc = Geom.CumLen(B);
            double sum = 0; int cnt = 0, bad = 0; max = 0;
            for (int i = 0; i < a.Count; i++)
            {
                double sAt; double d = Geom.PolylineDist(B, a[i], out sAt);
                if (sAt < 0.5 || sAt > lb - 0.5) continue;                          // B 밖(끝에 걸림)
                cnt++; sum += d; if (d > max) max = d;
                V2 q0 = Geom.At(B, bc, Math.Max(0, sAt - 2)), q1 = Geom.At(B, bc, Math.Min(lb, sAt + 2));
                if (V2.Dot((q1 - q0).Norm, at[i]) > -0.5) bad++;
            }
            double overlap = cnt * la / Math.Max(1, n - 1);
            if (cnt < 2 || overlap < 0.6 * Math.Min(la, lb) || overlap < 10) return false;
            mean = sum / cnt;
            return bad <= Math.Max(1, cnt / 5);
        }

        static List<Chain> SplitAtJunctions(List<Chain> chains, List<Junction> junctions, GenParams P)
        {
            // 교차로마다 그 교차로에 끝이 붙은 사슬들의 방향
            var ends = new Dictionary<Junction, List<KeyValuePair<V2, V2>>>();   // (끝점, 방향)
            foreach (var c in chains)
            {
                if (c.from != null) AddEnd(ends, c.from, c.pts[0], (c.pts[Math.Min(c.pts.Count - 1, 1)] - c.pts[0]).Norm);
                if (c.to != null) AddEnd(ends, c.to, c.pts[c.pts.Count - 1], (c.pts[c.pts.Count - 1] - c.pts[Math.Max(0, c.pts.Count - 2)]).Norm);
            }
            var outc = new List<Chain>();
            foreach (var c in chains)
            {
                var cuts = new List<KeyValuePair<double, Junction>>();
                var cum = Geom.CumLen(c.pts);
                foreach (var kv in ends)
                {
                    var J = kv.Key; if (J == c.from || J == c.to) continue;
                    double sAt; double d = Geom.PolylineDist(c.pts, J.center, out sAt);
                    if (d > P.splitNear || sAt < 8 || sAt > c.len - 8) continue;
                    V2 t = (Geom.At(c.pts, cum, Math.Min(c.len, sAt + 3)) - Geom.At(c.pts, cum, Math.Max(0, sAt - 3))).Norm;
                    bool ok = false;
                    foreach (var e in kv.Value)
                    {
                        double s2; if (Math.Abs(V2.Dot(e.Value, t)) > 0.8 && Geom.PolylineDist(c.pts, e.Key, out s2) < P.splitNear + 5) { ok = true; break; }
                    }
                    if (ok) cuts.Add(new KeyValuePair<double, Junction>(sAt, J));
                }
                if (cuts.Count == 0) { outc.Add(c); continue; }
                cuts.Sort((a, b) => a.Key.CompareTo(b.Key));
                double prevS = 0; Junction prevJ = c.from;
                for (int i = 0; i <= cuts.Count; i++)
                {
                    double sE = i < cuts.Count ? cuts[i].Key : c.len; Junction jE = i < cuts.Count ? cuts[i].Value : c.to;
                    if (sE - prevS < 3) { prevS = sE; prevJ = jE; continue; }
                    var n = new Chain { from = prevJ, to = jE };
                    n.pts = SubLine(c.pts, cum, prevS, sE);
                    n.len = Geom.Length(n.pts);
                    foreach (var sg in c.segs)
                    {
                        double a = Math.Max(sg.s0, prevS), b = Math.Min(sg.s1, sE);
                        if (b - a > 0.1) { n.segs.Add(new Seg { link = sg.link, s0 = a - prevS, s1 = b - prevS }); if (!n.links.Contains(sg.link)) n.links.Add(sg.link); }
                    }
                    if (n.pts.Count >= 2 && n.segs.Count > 0) outc.Add(n);
                    prevS = sE; prevJ = jE;
                }
            }
            return outc;
        }
        static void AddEnd(Dictionary<Junction, List<KeyValuePair<V2, V2>>> d, Junction J, V2 p, V2 dir)
        {
            List<KeyValuePair<V2, V2>> l; if (!d.TryGetValue(J, out l)) { l = new List<KeyValuePair<V2, V2>>(); d[J] = l; }
            l.Add(new KeyValuePair<V2, V2>(p, dir));
        }
        static List<V2> SubLine(List<V2> p, double[] cum, double a, double b)
        {
            var o = new List<V2> { Geom.At(p, cum, a) };
            for (int i = 0; i < p.Count; i++) if (cum[i] > a + 0.05 && cum[i] < b - 0.05) o.Add(p[i]);
            o.Add(Geom.At(p, cum, b));
            return o;
        }

        static void MakeRoad(Road R, Chain A, Chain B, GenParams P)
        {
            R.jStart = A.from; R.jEnd = A.to;
            R.fwdLinks = A.links.Select(l => l.id).ToList();
            var first = A.segs.OrderByDescending(g => g.s1 - g.s0).First().link;
            R.name = first.name; R.rank = first.rank;
            List<V2> c;
            if (B == null) c = Geom.Resample(A.pts, P.step);
            else
            {
                R.bwdLinks = B.links.Select(l => l.id).ToList();
                // A(긴 쪽) 점마다 B 까지의 절반만큼 옮김. B 와 안 겹치는 끝부분은 가장 가까운 겹친 곳의 옮김을 그대로
                var a = Geom.Resample(A.pts, P.step); double lb = Geom.Length(B.pts);
                var off = new V2[a.Count]; var ok = new bool[a.Count];
                for (int i = 0; i < a.Count; i++)
                {
                    double sAt; Geom.PolylineDist(B.pts, a[i], out sAt);
                    if (sAt < 0.5 || sAt > lb - 0.5) continue;
                    var bc = Geom.CumLen(B.pts); off[i] = (Geom.At(B.pts, bc, sAt) - a[i]) * 0.5; ok[i] = true;
                }
                int firstOk = Array.IndexOf(ok, true), lastOk = Array.LastIndexOf(ok, true);
                for (int i = 0; i < a.Count; i++) if (!ok[i]) off[i] = i < firstOk ? off[firstOk] : i > lastOk ? off[lastOk] : off[Math.Max(firstOk, i - 1)];
                c = new List<V2>(a.Count);
                for (int i = 0; i < a.Count; i++) c.Add(a[i] + off[i]);
            }
            c = Geom.SmoothPts(c, P.smoothSigmaM / P.step);
            R.center = c; R.cum = Geom.CumLen(c);
            double Lc = R.Length;
            R.fwd = Pieces(A, Lc, false);
            if (B != null) R.bwd = Pieces(B, Lc, true);
            R.divided = !R.oneWay && (R.rank == "101" || R.rank == "102");
            R.s0 = 0; R.s1 = Lc;
        }

        static List<LanePiece> Pieces(Chain ch, double Lc, bool reversed)
        {
            double tot = Math.Max(1e-6, ch.len); var o = new List<LanePiece>();
            foreach (var g in ch.segs)
            {
                double a = g.s0 / tot * Lc, b = g.s1 / tot * Lc;
                if (reversed) { double t = Lc - b; b = Lc - a; a = t; }
                var l = g.link;
                o.Add(new LanePiece { s0 = a, s1 = b, lanes = Math.Max(1, l.lanes), spd = l.spd, linkId = l.id, rank = l.rank, type = l.type, connect = l.connect, layer = l.layer });
            }
            o.Sort((x, y) => x.s0.CompareTo(y.s0));
            return o;
        }

        /// <summary>도로 모양·차로 수를 바꾼 뒤 그 도로의 팔(방향·입구 폭·차로 수)을 제자리에서 다시 맞추고 교차로 팔 순서를 다시 정함</summary>
        public static void RefreshArms(Road R, GenParams P)
        {
            foreach (var A in new[] { R.armStart, R.armEnd })
            {
                if (A == null) continue;
                var t = MakeArm(R, A.j, A.atStart, P);
                A.dir = t.dir; A.basePt = t.basePt; A.lanesOut = t.lanesOut; A.lanesIn = t.lanesIn; A.sh = t.sh;
                A.wOut = t.wOut; A.wIn = t.wIn; A.swOut = t.swOut; A.swIn = t.swIn;
                A.j.arms.Sort((a, b) => a.Angle.CompareTo(b.Angle)); OrderBundles(A.j, P);
            }
        }

        static Arm MakeArm(Road R, Junction J, bool atStart, GenParams P)
        {
            var A = new Arm { j = J, road = R, atStart = atStart };
            double L = R.Length;
            // 교차로에서 바깥으로: 끝에서 8~30m 사이 방향 평균
            double sA = atStart ? Math.Min(L, 8) : Math.Max(0, L - 8), sB = atStart ? Math.Min(L, 30) : Math.Max(0, L - 30);
            V2 pA = Geom.At(R.center, R.cum, sA), pB = Geom.At(R.center, R.cum, sB);
            A.dir = (pB - pA).Len > 1 ? (pB - pA).Norm : (Geom.At(R.center, R.cum, atStart ? L : 0) - J.center).Norm;
            A.basePt = pB;
            double sEnd = atStart ? 0 : L;
            // 바깥으로 나가는 쪽 = 도로 정방향이 바깥을 향하면 fwd, 아니면 bwd
            var outP = atStart ? R.fwd : R.bwd; var inP = atStart ? R.bwd : R.fwd;
            A.lanesOut = R.LanesAt(outP, sEnd); A.lanesIn = R.LanesAt(inP, sEnd);
            int spd = R.SpeedAt(sEnd);
            double lw = P.LaneWidth(R.rank, spd);
            double half = R.oneWay ? 0 : (R.divided ? P.medianHalf : P.centerHalfGap);
            A.sh = P.Shoulder(R.rank, spd);
            A.wOut = (A.lanesOut > 0 ? half + A.lanesOut * lw : 0) + A.sh;
            A.wIn = (A.lanesIn > 0 ? half + A.lanesIn * lw : 0) + A.sh;
            if (R.oneWay)
            {   // 일방통행: 차로를 중심선 양쪽으로 반씩
                int n = Math.Max(A.lanesOut, A.lanesIn); double w = n * lw * 0.5 + A.sh;
                A.wOut = A.wIn = w;
            }
            double sw = P.SidewalkWidth(R.rank, R.fwd.Count > 0 ? R.fwd[0].connect : "0");
            A.swOut = A.swIn = sw;
            return A;
        }
    }
}
