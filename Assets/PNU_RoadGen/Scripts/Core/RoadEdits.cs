// PNU 도로 생성기 — 도로 편집기에서 고친 값(정점·구간 차로 수)을 도로망에 넣기 (Prepare 처음)
//  모양: 새 중심선으로 바꾸고(양 끝 = 교차로에 붙은 원래 끝점), 차로 조각 s 를 새 길이에 비례해 옮김 → 팔(방향·입구 폭) 다시 맞춤
//  방향(D): 양방향 ↔ 일방, 일방 방향 뒤집기 — 차로 수보다 먼저
//  차로: 구간 [t0, t1](길이 비율)의 정방향·역방향 차로 수를 바꿈(조각을 그 자리에서 잘라서). 0 = 그대로
using System;
using System.Collections.Generic;
using System.Linq;

namespace PNU.RoadGen
{
    public partial class RoadGenerator
    {
        public int roadEditsApplied, roadEditsMissed;

        Road FindRoad(string key, string js, string je)
        {
            Road best = null; int bs = -1;
            foreach (var r in net.roads)
            {
                if (Keys.Road(r) != key) continue;
                string a = r.jStart != null ? Keys.Junction(r.jStart) : "-", b = r.jEnd != null ? Keys.Junction(r.jEnd) : "-";
                int sc = (a == js ? 2 : 0) + (b == je ? 2 : 0);
                if (sc > bs) { bs = sc; best = r; }
            }
            return best;
        }

        void ApplyRoadEdits()
        {
            if (ov.roadEdits.Count == 0) return;
            roadEditsApplied = roadEditsMissed = 0;
            foreach (var e in ov.roadEdits)
            {
                var R = FindRoad(e.key, e.jStart, e.jEnd);
                if (R == null) { roadEditsMissed++; log.Add("road edit: 못 찾음 " + e.key + " " + e.jStart + " " + e.jEnd); continue; }
                bool changed = false;
                if ((e.center != null && e.center.Count >= 2 || e.ctrl != null && e.ctrl.Count >= 2) && !e.geomDone)
                {
                    double oldL = R.Length;
                    List<V2> c;
                    if (e.ctrl != null && e.ctrl.Count >= 2)
                    {   // 정점을 지나는 곡선(양 끝 정점은 교차로에 붙은 원래 끝점으로)
                        var q = new List<V2>(e.ctrl); q[0] = R.center[0]; q[q.Count - 1] = R.center[R.center.Count - 1];
                        c = Geom.CatmullRom(q, 2.0);
                    }
                    else c = new List<V2>(e.center);
                    c[0] = R.center[0]; c[c.Count - 1] = R.center[R.center.Count - 1];      // 교차로에 붙은 끝은 그대로
                    for (int i = c.Count - 1; i > 0; i--) if ((c[i] - c[i - 1]).Len < 0.05) c.RemoveAt(i);   // 겹친 점
                    if (c.Count >= 2)
                    {
                        R.center = c; R.cum = Geom.CumLen(c);
                        double k = R.Length / Math.Max(1e-6, oldL);
                        foreach (var p in R.fwd.Concat(R.bwd)) { p.s0 *= k; p.s1 *= k; }
                        changed = true;
                    }
                    e.geomDone = true;
                }
                if (e.dir >= -1 && e.dir <= 1 && SetDirection(R, e.dir)) changed = true;
                foreach (var ls in e.lanes)
                {
                    double L = R.Length, a = Math.Max(0, Math.Min(ls.t0, ls.t1)) * L, b = Math.Min(1, Math.Max(ls.t0, ls.t1)) * L;
                    if (b - a < 0.5) continue;
                    if (ls.f > 0 && R.fwd.Count > 0) { SetLanes(R.fwd, a, b, ls.f); changed = true; }
                    if (ls.b > 0 && R.bwd.Count > 0) { SetLanes(R.bwd, a, b, ls.b); changed = true; }
                }
                if (changed) NetworkBuilder.RefreshArms(R, P);
                roadEditsApplied++;
            }
            log.Add("road edits: " + roadEditsApplied + " (못 찾음 " + roadEditsMissed + ")");
        }

        /// <summary>방향 바꾸기. 일방→양방향: 빈 쪽에 같은 모양 조각(차로 1개, 구간 차로 수가 뒤에 덮음), 분리대 없음(편집기와 같은 중앙 반폭).
        /// 일방 방향 뒤집기: 조각을 반대쪽으로 옮기고 노드링크 목록도 바꿈. 바뀌었으면 true</summary>
        static bool SetDirection(Road R, int dir)
        {
            bool ow = dir != 0, fw = dir != -1;
            if (!ow)
            {
                if (!R.oneWay) return false;
                if (R.fwd.Count == 0) R.fwd = R.bwd.Select(p => CopyPiece(p, 1)).ToList();
                if (R.bwd.Count == 0) R.bwd = R.fwd.Select(p => CopyPiece(p, 1)).ToList();
                R.oneWay = false; R.divided = false; return true;
            }
            if (R.oneWay && (R.fwd.Count > 0) == fw) return false;
            if (fw && R.fwd.Count == 0) { R.fwd = R.bwd.Select(p => CopyPiece(p, p.lanes)).ToList(); var t = R.fwdLinks; R.fwdLinks = R.bwdLinks; R.bwdLinks = t; }
            else if (!fw && R.bwd.Count == 0) { R.bwd = R.fwd.Select(p => CopyPiece(p, p.lanes)).ToList(); var t = R.fwdLinks; R.fwdLinks = R.bwdLinks; R.bwdLinks = t; }
            if (fw) R.bwd.Clear(); else R.fwd.Clear();
            R.oneWay = true; R.divided = false; return true;
        }
        static LanePiece CopyPiece(LanePiece p, int lanes)
        {
            return new LanePiece { s0 = p.s0, s1 = p.s1, lanes = lanes, spd = p.spd, linkId = p.linkId, rank = p.rank, type = p.type, connect = p.connect, layer = p.layer };
        }

        static void SetLanes(List<LanePiece> ps, double s0, double s1, int n)
        {
            SplitPiece(ps, s0); SplitPiece(ps, s1);
            foreach (var p in ps) if (p.s0 >= s0 - 1e-3 && p.s1 <= s1 + 1e-3) p.lanes = n;
        }
        static void SplitPiece(List<LanePiece> ps, double s)
        {
            for (int i = 0; i < ps.Count; i++)
            {
                var p = ps[i];
                if (s > p.s0 + 1e-3 && s < p.s1 - 1e-3)
                {
                    var q = new LanePiece { s0 = s, s1 = p.s1, lanes = p.lanes, spd = p.spd, linkId = p.linkId, rank = p.rank, type = p.type, connect = p.connect, layer = p.layer };
                    p.s1 = s; ps.Insert(i + 1, q); return;
                }
            }
        }
    }
}
