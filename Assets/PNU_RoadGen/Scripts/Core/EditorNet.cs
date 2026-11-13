// PNU 도로 생성기 — 도로 편집기 최종 도로망 가져오기(Data/pnu_editor_net.txt, scratchpad unity_net/export_net.js 가 만듦)
//   편집기에서 고친 것(자르기·지우기·끝 붙이기·모양·램프·합치기·새 도로, 정점·구간 차로·한쪽 넓힘·이어지는 길이·방향, 높이, 차로 연결)을
//   모두 덮은 뒤의 도로망을 그대로 받아 생성기 도로망을 바꿈 → 그다음은 보통처럼 형상·높이·교차로 면·노면표시·지형
//   - 그대로인 도로(U): 노드링크로 만든 도로를 그대로(입구만 편집기 자리로 고정)
//   - 새로·고친 도로(I): 기준선(일방 = 차로 묶음 가운데, 양방향 = 중앙선) + 입구 자리 + 가장자리 폭(테이퍼 그대로) + 구간 차로.
//     차로 조각의 도로 유형(교량·터널…)·속도·등급·링크는 뿌리(원래) 도로에서 가까운 자리 것을 씀
//   - 교차로: 편집기 팔 그대로(입구 고정 = fixedCut), 새 교차로 키 J+기록id, 합류·분류는 merge(횡단보도·신호 없음)
//   - 차로 연결: 편집기에 보이던 연결 그대로(손 연결은 손 연결로, 자동은 자동 면으로)
//   - 높이: 사람이 정한 높이(편집기 heightEdits, 물려받은 것 포함)는 그대로
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace PNU.RoadGen
{
    /// <summary>도로 편집기에서 온 도로 모양: 중심선 위 입구 자리(m0·m1), 입구 사이 점(s)마다 기준선에서 왼쪽·오른쪽 가장자리까지(길어깨 포함)</summary>
    public class EdShape { public double m0, m1; public double[] s, wl, wr, tFull; }   // tFull: 중심선 점마다 편집기 곡선 길이 비율

    public partial class RoadGenerator
    {
        public bool edNet;
        public int edKept, edImported, edMissingRoads, edJunctions, edNewJunctions, edArms, edArmMiss, edLinkSets, edHeights, edBaseHeights;

        sealed class EdR
        {
            public string doc, mode, rk, rjs, rje, bk, bjs, bje, js, je, name; public bool ow, fw, hfix; public double half;
            public List<V2> pts; public double[] T, W, S; public int i0, i1;
        }
        sealed class EdJ { public string key, flags, name, mode = "A"; public V2 c; public List<string[]> arms = new List<string[]>(); public List<LaneLinkOverride> links = new List<LaneLinkOverride>(); }

        static double[] EdNums(string s)
        {
            var a = s.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries); var o = new double[a.Length];
            for (int i = 0; i < a.Length; i++) o[i] = double.Parse(a[i], CultureInfo.InvariantCulture);
            return o;
        }

        /// <summary>편집기 최종 도로망으로 바꿈(Prepare 전에). 반환: 바꿨으면 true</summary>
        public bool ImportEditorNet(IEnumerable<string> lines)
        {
            var ci = CultureInfo.InvariantCulture;
            var rs = new List<EdR>(); var js = new List<EdJ>(); var jmap = new Dictionary<string, EdJ>(); var hts = new Dictionary<string, double[]>(); var hbase = new HashSet<string>(); var tms = new Dictionary<string, double[]>();
            EdR cur = null;
            foreach (var raw in lines)
            {
                if (string.IsNullOrEmpty(raw) || raw[0] == '#') continue;
                var a = raw.TrimEnd('\r').Split('\t'); if (a.Length < 2) continue;
                switch (a[0])
                {
                    case "R":
                        if (a.Length < 15) { cur = null; break; }
                        cur = new EdR { doc = a[1], mode = a[2], rk = a[3], rjs = a[4], rje = a[5], bk = a[6], bjs = a[7], bje = a[8], ow = a[9] == "1", fw = a[10] == "1",
                                        half = double.Parse(a[11], ci), js = a[12], je = a[13], hfix = a[14] == "1", name = a.Length > 15 ? a[15] : "" };
                        rs.Add(cur); break;
                    case "P": if (cur != null) { var v = EdNums(a[1]); cur.pts = new List<V2>(v.Length / 2); for (int i = 0; i + 1 < v.Length; i += 2) cur.pts.Add(new V2(v[i], v[i + 1])); } break;
                    case "T": if (cur != null) cur.T = EdNums(a[1]); break;
                    case "M": if (cur != null) { var v = EdNums(a[1]); cur.i0 = (int)v[0]; cur.i1 = (int)v[1]; } break;
                    case "W": if (cur != null) cur.W = EdNums(a[1]); break;
                    case "S": if (cur != null) cur.S = EdNums(a[1]); break;
                    case "H": if (a.Length >= 3) hts[a[1]] = EdNums(a[2]); break;
                    case "B": if (a.Length >= 3) { hts[a[1]] = EdNums(a[2]); hbase.Add(a[1]); } break;       // 편집기 기본 높이(사람 높이 없는 길)
                    case "K": if (a.Length >= 3) tms[a[1]] = EdNums(a[2]); break;                             // 편집기 구조 판정 t 코드 …
                    case "J": if (a.Length >= 5) { var j = new EdJ { key = a[1], c = new V2(double.Parse(a[2], ci), double.Parse(a[3], ci)), flags = a[4], name = a.Length > 5 ? a[5] : "" }; js.Add(j); jmap[j.key] = j; } break;
                    case "A": { EdJ j; if (a.Length >= 10 && jmap.TryGetValue(a[1], out j)) j.arms.Add(a); } break;
                    case "X": { EdJ j; if (a.Length >= 3 && jmap.TryGetValue(a[1], out j)) j.mode = a[2]; } break;
                    case "L":
                        {
                            EdJ j; if (a.Length < 4 || !jmap.TryGetValue(a[1], out j)) break;
                            var o = new LaneLinkOverride { from = a[2], to = a[3] };
                            if (a.Length > 4) { var v = EdNums(a[4]); for (int i = 0; i + 1 < v.Length; i += 2) o.via.Add(new V2(v[i], v[i + 1])); }
                            j.links.Add(o); break;
                        }
                }
            }
            if (rs.Count == 0 || js.Count == 0) { log.Add("editor net: 비어 있음"); return false; }
            if (!net.gwStacked) { Gwangan.Stack(net, P); net.gwStacked = true; }       // 편집기 자료는 광안대교 아래층을 옮긴 뒤 모양

            // ---- 원래 도로·교차로 찾기(도로 키~시작 교차로 키~끝 교차로 키) ----
            Func<Junction, string> jk = J => J != null ? Keys.Junction(J) : "-";
            var orig = new Dictionary<string, Road>();
            foreach (var r in net.roads) { string k = Keys.Road(r) + "~" + jk(r.jStart) + "~" + jk(r.jEnd); if (!orig.ContainsKey(k)) orig[k] = r; }
            var origJ = new Dictionary<string, Junction>(); foreach (var J in net.junctions) { var k = Keys.Junction(J); if (!origJ.ContainsKey(k)) origJ[k] = J; }
            Func<string, string, string, Road> findOrig = (k, a, b) => { Road r; return k != "-" && orig.TryGetValue(k + "~" + a + "~" + b, out r) ? r : null; };

            int nextRoadId = net.roads.Count > 0 ? net.roads.Max(r => r.id) + 1000 : 100000;
            int nextJId = net.junctions.Count > 0 ? net.junctions.Max(j => j.id) + 1000 : 100000;
            var byDoc = new Dictionary<string, Road>(); var outRoads = new List<Road>();
            edKept = edImported = edMissingRoads = edJunctions = edNewJunctions = edArms = edArmMiss = edLinkSets = edHeights = edBaseHeights = 0;
            foreach (var e in rs)
            {
                if (e.mode == "U")
                {
                    var r = findOrig(e.rk, e.rjs, e.rje);
                    if (r == null) { edMissingRoads++; log.Add("editor net: 그대로인 도로를 못 찾음 " + e.doc); continue; }
                    byDoc[e.doc] = r; outRoads.Add(r); edKept++;
                    continue;
                }
                var nr = BuildEdRoad(e, findOrig(e.rk, e.rjs, e.rje), findOrig(e.bk, e.bjs, e.bje), nextRoadId++);
                if (nr == null) { edMissingRoads++; log.Add("editor net: 모양이 없는 도로 " + e.doc); continue; }
                byDoc[e.doc] = nr; outRoads.Add(nr); edImported++;
            }
            // 사람이 정한 높이: t(편집기 곡선 길이 비율) → 이 도로 s
            foreach (var e in rs)
            {
                double[] hv; Road r; if (!hts.TryGetValue(e.doc, out hv) || !byDoc.TryGetValue(e.doc, out r) || hv.Length < 4) continue;
                int m = hv.Length / 2; var S = new double[m]; var Y = new double[m];
                for (int i = 0; i < m; i++)
                {
                    double t = hv[2 * i];
                    S[i] = r.ed != null && r.ed.tFull != null ? Geom.AtVal(r.cum, r.ed.tFull, t) : t * r.Length;
                    Y[i] = hv[2 * i + 1];
                }
                for (int i = 1; i < m; i++) if (S[i] < S[i - 1]) S[i] = S[i - 1];       // 단조
                r.hFixS = S; r.hFixY = Y; r.hBase = hbase.Contains(e.doc); if (r.hBase) edBaseHeights++; else edHeights++;
                double[] kv;
                if (tms.TryGetValue(e.doc, out kv) && kv.Length >= 4)
                {
                    int q = kv.Length / 2; var TS = new double[q]; var TV = new byte[q];
                    for (int i = 0; i < q; i++) { double t = kv[2 * i]; TS[i] = r.ed != null && r.ed.tFull != null ? Geom.AtVal(r.cum, r.ed.tFull, t) : t * r.Length; TV[i] = (byte)Math.Max(0, Math.Min(3, (int)Math.Round(kv[2 * i + 1]))); }
                    for (int i = 1; i < q; i++) if (TS[i] < TS[i - 1]) TS[i] = TS[i - 1];
                    r.tmS = TS; r.tmV = TV;
                }
            }

            // ---- 교차로·팔: 편집기 그대로 ----
            foreach (var r in outRoads) { r.jStart = r.jEnd = null; r.armStart = r.armEnd = null; }
            var outJ = new List<Junction>();
            foreach (var ej in js)
            {
                Junction J; bool isNew = false;
                if (!origJ.TryGetValue(ej.key, out J)) { J = new Junction { id = nextJId++, keyOv = ej.key, name = ej.name == "-" ? "" : ej.name }; isNew = true; }
                J.arms = new List<Arm>(); J.center = ej.c;
                if (ej.flags.Contains("m")) J.merge = true;
                foreach (var a in ej.arms)
                {
                    Road r; if (!byDoc.TryGetValue(a[2], out r)) { edArmMiss++; continue; }
                    bool st = a[3] == "1";
                    var dir = new V2(double.Parse(a[6], ci), double.Parse(a[7], ci)); if (dir.Len < 1e-6) { edArmMiss++; continue; } dir = dir.Norm;
                    V2 M;
                    if (r.ed != null) M = Geom.At(r.center, r.cum, st ? r.ed.m0 : r.ed.m1);
                    else M = new V2(double.Parse(a[8], ci), double.Parse(a[9], ci));
                    var A = new Arm { j = J, road = r, atStart = st, dir = dir, basePt = M };
                    A.fixedCut = V2.Dot(M - J.center, dir);
                    if (st) { if (r.armStart != null) { edArmMiss++; continue; } r.jStart = J; r.armStart = A; r.ekS = a[4]; r.tagS = a[5]; }
                    else { if (r.armEnd != null) { edArmMiss++; continue; } r.jEnd = J; r.armEnd = A; r.ekE = a[4]; r.tagE = a[5]; }
                    J.arms.Add(A); edArms++;
                }
                if (J.arms.Count == 0) continue;
                J.arms.Sort((x, y) => x.Angle.CompareTo(y.Angle)); NetworkBuilder.OrderBundles(J, P);
                outJ.Add(J); edJunctions++; if (isNew) edNewJunctions++;
                // 차로 연결: 편집기에 보이던 그대로
                ov.laneLinks[ej.key] = ej.links; edLinkSets++;
                if (ej.mode != "M") ov.edAuto.Add(ej.key); else ov.edAuto.Remove(ej.key);
            }
            net.roads.Clear(); net.roads.AddRange(outRoads);
            net.junctions.Clear(); net.junctions.AddRange(outJ);
            ov.roadEdits.Clear();                                                                  // 편집기 도로 고친 값은 이미 들어 있음
            linkRoad = null;
            edNet = true;
            log.Add("editor net: 도로 그대로 " + edKept + " · 새로 " + edImported + " · 못 찾음 " + edMissingRoads + " · 교차로 " + edJunctions + "(새 " + edNewJunctions + ") · 팔 " + edArms
                    + (edArmMiss > 0 ? "(못 붙임 " + edArmMiss + ")" : "") + " · 차로 연결 " + edLinkSets + "곳 · 사람 높이 " + edHeights + " · 편집기 기본 높이 " + edBaseHeights);
            return true;
        }

        /// <summary>편집기 도로 하나 → 생성기 도로(기준선·입구·가장자리·구간 차로, 차로 조각 속성은 뿌리 도로에서)</summary>
        Road BuildEdRoad(EdR e, Road rootA, Road rootB, int id)
        {
            if (e.pts == null || e.pts.Count < 2 || e.T == null || e.W == null || e.S == null) return null;
            // 겹친 점 빼기(번호 다시 맞춤)
            var keep = new List<int> { 0 };
            for (int i = 1; i < e.pts.Count; i++) if ((e.pts[i] - e.pts[keep[keep.Count - 1]]).Len > 0.02) keep.Add(i); else if (i == e.i0 || i == e.i1) keep[keep.Count - 1] = i;
            var c = keep.Select(i => e.pts[i]).ToList(); if (c.Count < 2) return null;
            var T = keep.Select(i => e.T[i]).ToArray();
            int i0 = 0, i1 = c.Count - 1;
            for (int k = 0; k < keep.Count; k++) { if (keep[k] <= e.i0) i0 = k; if (keep[k] <= e.i1) i1 = k; }
            var cum = Geom.CumLen(c); double L = cum[cum.Length - 1];
            for (int k = 1; k < T.Length; k++) if (T[k] < T[k - 1]) T[k] = T[k - 1];
            var R = new Road { id = id, name = e.name == "-" ? "" : e.name, oneWay = e.ow, center = c, cum = cum, keyOv = e.doc };
            R.divided = !e.ow && e.half > P.centerHalfGap + 0.01;
            // 가장자리 폭(입구 사이 점마다)
            int nw = e.W.Length / 2, nst = e.i1 - e.i0 + 1;
            var shp = new EdShape { m0 = cum[i0], m1 = cum[i1], tFull = T };
            var kidx = new Dictionary<int, int>(); for (int k = 0; k < keep.Count; k++) kidx[keep[k]] = k;
            if (nw == nst)
            {
                var ss = new List<double>(); var wl = new List<double>(); var wr = new List<double>();
                for (int k = 0; k < nw; k++)
                {
                    int src = e.i0 + k, kk; if (!kidx.TryGetValue(src, out kk)) continue;
                    ss.Add(cum[kk]); wl.Add(e.W[2 * k]); wr.Add(e.W[2 * k + 1]);
                }
                shp.s = ss.ToArray(); shp.wl = wl.ToArray(); shp.wr = wr.ToArray();
            }
            R.ed = shp;
            // 뿌리 도로 속성(가까운 자리): 등급·속도·유형·링크
            Road near = rootA ?? rootB;
            if (near == null)
            {   // 새로 그린 길: 가운데에서 가장 가까운 원래 도로 속성(땅 위 길)
                V2 mid = Geom.At(c, cum, L * 0.5); double bd = 1e18;
                foreach (var r in net.roads) { if (r.ed != null) continue; foreach (var p in r.center) { double d = (p - mid).Len2; if (d < bd) { bd = d; near = r; } } }
            }
            R.rank = near != null ? near.rank : "107";
            if (rootA != null) { R.fwdLinks = new List<string>(rootA.fwdLinks); R.bwdLinks = new List<string>(rootA.bwdLinks); }
            Func<double, LanePiece> attrAt = s =>
            {
                V2 p = Geom.At(c, cum, s); LanePiece best = null; double bd = 1e18;
                foreach (var rt in new[] { rootA, rootB })
                {
                    if (rt == null) continue;
                    double ps, pd; ProjectS(rt.center, rt.cum, p, out ps, out pd);
                    if (pd > 30) ps = Math.Max(0, Math.Min(1, Geom.AtVal(T, cum, s))) * rt.Length;   // 많이 옮긴 길: 비율로
                    if (pd < bd) { bd = pd; best = PieceAt(rt, ps); }
                }
                return best;
            };
            // 구간 차로(t → s) + 속성 조각(5m 마다 같은 속성끼리)
            var cuts = new SortedSet<double> { 0, L };
            int nsec = e.S.Length / 4; var secS0 = new double[nsec]; var secS1 = new double[nsec]; var secF = new int[nsec]; var secB = new int[nsec];
            for (int k = 0; k < nsec; k++)
            {
                secS0[k] = k == 0 ? 0 : Geom.AtVal(cum, T, e.S[4 * k]); secS1[k] = k == nsec - 1 ? L : Geom.AtVal(cum, T, e.S[4 * k + 1]);
                secF[k] = (int)e.S[4 * k + 2]; secB[k] = (int)e.S[4 * k + 3]; cuts.Add(secS0[k]); cuts.Add(secS1[k]);
            }
            Func<LanePiece, string> sig = p => p == null ? "-" : (p.type + "|" + p.spd + "|" + p.rank + "|" + p.linkId + "|" + p.connect + "|" + p.layer);
            string prevSig = null;
            for (double s = 2.5; s < L; s += 5.0) { var pc = attrAt(s); string sg = sig(pc); if (prevSig != null && sg != prevSig) cuts.Add(Math.Max(0, s - 2.5)); prevSig = sg; }
            var cl = cuts.ToList(); var fw = new List<LanePiece>(); var bw = new List<LanePiece>();
            bool newRoad = rootA == null && rootB == null;
            for (int k = 0; k + 1 < cl.Count; k++)
            {
                double a = cl[k], b = cl[k + 1]; if (b - a < 0.05) continue;
                double m = 0.5 * (a + b); int si = 0; while (si < nsec - 1 && m > secS1[si]) si++;
                var pc = attrAt(m);
                int spd = pc != null && pc.spd > 0 ? pc.spd : near != null ? near.SpeedAt(0) : 50;
                string rank = pc != null && !string.IsNullOrEmpty(pc.rank) ? pc.rank : R.rank;
                string type = pc != null && !string.IsNullOrEmpty(pc.type) ? pc.type : "000", connect = pc != null ? pc.connect : (e.ow ? "001" : "000");
                if (newRoad) { type = "000"; connect = e.ow ? "001" : "000"; }
                int layer = pc != null ? pc.layer : 0; string link = pc != null ? pc.linkId : null;
                if (!e.ow || e.fw) fw.Add(new LanePiece { s0 = a, s1 = b, lanes = Math.Max(1, secF[si]), spd = spd, rank = rank, type = type, connect = connect, layer = layer, linkId = link });
                if (!e.ow || !e.fw) bw.Add(new LanePiece { s0 = a, s1 = b, lanes = Math.Max(1, secB[si]), spd = spd, rank = rank, type = type, connect = connect, layer = layer, linkId = link });
            }
            R.fwd = Merge(fw); R.bwd = Merge(bw);
            if (R.fwd.Count == 0 && R.bwd.Count == 0) return null;
            return R;
        }

        static List<LanePiece> Merge(List<LanePiece> ps)
        {
            var o = new List<LanePiece>();
            foreach (var p in ps)
            {
                var q = o.Count > 0 ? o[o.Count - 1] : null;
                if (q != null && q.lanes == p.lanes && q.spd == p.spd && q.rank == p.rank && q.type == p.type && q.connect == p.connect && q.layer == p.layer && q.linkId == p.linkId && Math.Abs(q.s1 - p.s0) < 1e-6) q.s1 = p.s1;
                else o.Add(p);
            }
            return o;
        }

        /// <summary>선 위 가장 가까운 자리(s)와 거리</summary>
        static void ProjectS(List<V2> pts, double[] cum, V2 p, out double s, out double d)
        {
            s = 0; double bd = 1e18;
            for (int i = 1; i < pts.Count; i++)
            {
                V2 a = pts[i - 1], b = pts[i], ab = b - a; double l2 = ab.Len2; double u = l2 > 1e-12 ? Geom.Clamp(V2.Dot(p - a, ab) / l2, 0, 1) : 0;
                double dd = (a + ab * u - p).Len2; if (dd < bd) { bd = dd; s = cum[i - 1] + u * (cum[i] - cum[i - 1]); }
            }
            d = Math.Sqrt(bd);
        }
    }
}
