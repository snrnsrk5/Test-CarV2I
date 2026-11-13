// 4/6 NPC 차·사람·자차 판단 계산(유니티와 상관없는 순수 C#: 유니티 밖 시험 프로그램에서도 똑같이 돌림)
// 차: 차로 토막(Data/lanes.json) 위를 IDM(앞차 따라가기)으로 달림
//     교차로: 신호(PNUDriveSignals 와 같은 계산) + 교차로 차지(길이 겹치는 이음끼리 한 대씩) + 양보(비보호 좌회전·곁길·먼저 온 차 먼저)
//             + 건너편이 막히면 안 들어감 + 횡단보도 사람 양보
//     자차: 자차가 몇 초 안에 지날 길과 겹치면 양보(엇갈림 = 먼저 오는 쪽, 같은 쪽으로 합쳐짐 = 자차 먼저)
//     순간이동 없음: 멀어지면 서서히 투명 → 풀로. 막다른 길 끝은 자차가 볼 수 있으면 서서 기다렸다가 안 보일 때 사라짐
// 자차(EgoDriver): 경로 속도·신호·앞차 + 비보호 좌회전(맞은편 차 기다림)·빨강 우회전(일시정지 뒤 오는 차 없을 때)·교차로 건너편 막힘
// 사람: 인도·모퉁이·횡단보도 그래프, 보행 신호(남은 시간이 건너는 시간보다 길 때만 출발), 자차가 곧 지나는 횡단보도는 기다림
using System;
using System.Collections.Generic;

namespace PNU.Drive
{
    // ---------------------------------------------------------------- 자료(JSON)
    [Serializable] public class LTrack { public int id; public float[] p; public float lim; public int j, g; public float stop; public int[] nx; public bool c, lo, e; public int mv, y, w; public int[] cf, ct, mg; }
    [Serializable] public class LEdge { public int a, b, t, j, g, ec; public float es; public float[] p; }
    [Serializable] public class LJunc { public int id; public float x, y, z, r, es; public bool sig; }
    [Serializable] public class LanesData { public LTrack[] tracks; public float[] nodes; public LEdge[] edges; public LJunc[] junctions; }

    public struct V3
    {
        public float x, y, z;
        public V3(float x, float y, float z) { this.x = x; this.y = y; this.z = z; }
        public static V3 operator +(V3 a, V3 b) { return new V3(a.x + b.x, a.y + b.y, a.z + b.z); }
        public static V3 operator -(V3 a, V3 b) { return new V3(a.x - b.x, a.y - b.y, a.z - b.z); }
        public static V3 operator *(V3 a, float k) { return new V3(a.x * k, a.y * k, a.z * k); }
        public float Len2D { get { return (float)Math.Sqrt(x * x + z * z); } }
        public static float Dist2D(V3 a, V3 b) { float dx = a.x - b.x, dz = a.z - b.z; return (float)Math.Sqrt(dx * dx + dz * dz); }
        public static float Dist2DSq(V3 a, V3 b) { float dx = a.x - b.x, dz = a.z - b.z; return dx * dx + dz * dz; }
        /// <summary>두 방향(평면)의 코사인</summary>
        public static float Cos2D(V3 a, V3 b) { float la = a.Len2D, lb = b.Len2D; return la > 1e-4f && lb > 1e-4f ? (a.x * b.x + a.z * b.z) / (la * lb) : 0f; }
    }

    /// <summary>신호 계산(PNUDriveSignals·cw/greenwave.py 와 같음)</summary>
    public static class SigMath
    {
        public const float G0 = 26f, L0 = 20f, G1 = 18f, Y = 3f, AR = 2f;                  // v15: 보호 좌회전 화살표 20초
        public static float Cycle(bool left0) { return G0 + Y + (left0 ? L0 + Y : 0f) + AR + G1 + Y + AR; }
        public static float U(bool left0, float off, float t) { float c = Cycle(left0); float u = (t + off) % c; if (u < 0) u += c; return u; }
        /// <summary>0 빨강, 1 노랑, 2 초록, 3 빨강+좌회전 화살표</summary>
        public static int Aspect(bool left0, float off, int group, float t)
        {
            float u = U(left0, off, t);
            float a0 = G0, a1 = a0 + Y, a2 = left0 ? a1 + L0 : a1, a3 = left0 ? a2 + Y : a1, a4 = a3 + AR, a5 = a4 + G1, a6 = a5 + Y;
            if (group == 0) { if (u < a0) return 2; if (u < a1) return 1; if (u < a2) return 3; if (u < a3) return 1; return 0; }
            if (u < a4) return 0; if (u < a5) return 2; if (u < a6) return 1; return 0;
        }
        /// <summary>자차 판단(PNUDriveSignals.EgoState 와 같음): 0 서기, 1 노랑(설 수 있으면 서기), 2 가기, 4 빨강 우회전(일시정지 뒤 가기)</summary>
        public static int EgoState(bool left0, float off, int group, string mv, float t)
        {
            int a = Aspect(left0, off, group, t);
            if (mv == "L" && left0 && group == 0) return a == 3 ? 2 : ((a == 1 && U(left0, off, t) >= G0 + Y) ? 1 : 0);
            if (mv == "R") return a == 2 ? 2 : (a == 1 ? 1 : 4);
            return a == 2 ? 2 : (a == 1 ? 1 : 0);
        }
        /// <summary>지금 노랑이면 빨강까지 남은 초(아니면 0)</summary>
        public static float YellowRemain(bool left0, float off, int group, float t)
        {
            float u = U(left0, off, t);
            float a0 = G0, a1 = a0 + Y, a2 = left0 ? a1 + L0 : a1, a3 = left0 ? a2 + Y : a1, a4 = a3 + AR, a5 = a4 + G1, a6 = a5 + Y;
            if (group == 0) { if (u >= a0 && u < a1) return a1 - u; if (left0 && u >= a2 && u < a3) return a3 - u; return 0f; }
            if (u >= a5 && u < a6) return a6 - u;
            return 0f;
        }
        public static bool Walk(bool left0, float off, int g, float t, out float remain, out float frac)
        {
            remain = 0; frac = 0;
            float u = U(left0, off, t);
            float a3 = G0 + Y + (left0 ? L0 + Y : 0f), a4 = a3 + AR, a5 = a4 + G1;
            float w0, w1;
            if (g == 1) { w0 = 0f; w1 = G0 - 2f; } else { w0 = a4; w1 = a5 - 2f; }
            if (u >= w0 && u < w1) { remain = w1 - u; frac = remain / Math.Max(0.1f, w1 - w0); return true; }
            return false;
        }
    }

    /// <summary>꺾은선(1~4m 점) — 길이 s 로 자리·방향</summary>
    public class Poly
    {
        public float[] x, y, z, cum; public float L;
        public Poly(float[] p)
        {
            int n = p.Length / 3; x = new float[n]; y = new float[n]; z = new float[n]; cum = new float[n];
            for (int i = 0; i < n; i++) { x[i] = p[3 * i]; y[i] = p[3 * i + 1]; z[i] = p[3 * i + 2]; }
            for (int i = 1; i < n; i++) { float dx = x[i] - x[i - 1], dz = z[i] - z[i - 1]; cum[i] = cum[i - 1] + (float)Math.Sqrt(dx * dx + dz * dz); }
            L = n > 0 ? cum[n - 1] : 0f;
        }
        public int Count { get { return x.Length; } }
        int Seg(float s)
        {
            int lo = 0, hi = cum.Length - 2;
            if (hi < 0) return 0;
            if (s <= 0) return 0; if (s >= L) return hi;
            while (lo < hi) { int m = (lo + hi + 1) >> 1; if (cum[m] <= s) lo = m; else hi = m - 1; }
            return lo;
        }
        public V3 At(float s)
        {
            if (cum.Length == 1) return new V3(x[0], y[0], z[0]);
            int i = Seg(s); float d = cum[i + 1] - cum[i]; float f = d > 1e-5f ? (s - cum[i]) / d : 0f; if (f < 0) f = 0; if (f > 1) f = 1;
            return new V3(x[i] + (x[i + 1] - x[i]) * f, y[i] + (y[i + 1] - y[i]) * f, z[i] + (z[i + 1] - z[i]) * f);
        }
        public V3 Dir(float s)
        {
            if (cum.Length < 2) return new V3(0, 0, 1);
            int i = Seg(s); float dx = x[i + 1] - x[i], dy = y[i + 1] - y[i], dz = z[i + 1] - z[i];
            float l = (float)Math.Sqrt(dx * dx + dz * dz); if (l < 1e-5f) return new V3(0, 0, 1);
            return new V3(dx / l, dy / l, dz / l);
        }
        public V3 P(int i) { return new V3(x[i], y[i], z[i]); }
    }

    public class TTrack
    {
        public int id; public Poly poly; public float L, lim, stop; public int j, g, mv, y, w; public bool conn, lower, ego; public int[] nx, cf, ct, mg;
        public float vTurn;                                  // 이음 곡률로 정한 속도(m/s)
        public bool merge; public float mergeAt;             // 합류 이음: 옆 차로 mergeAt(s)로 들어감
        public readonly List<TTrack> feeders = new List<TTrack>();     // 이 차로로 합류하는 이음
        public readonly List<TCar> cars = new List<TCar>();
        public bool deadEnd;                                 // 나갈 곳 없음
        public TTrack srcT;                                  // 이음: 들어오는 토막
        public TJunc jn;                                     // 교차로 이음: 그 교차로
        public readonly List<TTrack> xc = new List<TTrack>();    // 교차로 이음: 같은 교차로에서 길이 겹치는(엇갈리는·합쳐지는) 이음
        public readonly List<TTrack> sib = new List<TTrack>();   // 이음: 같은 토막에서 갈라지는 다른 이음
    }

    public class TJunc
    {
        public int id; public V3 c; public float r, es; public bool sig, left0; public float off; public bool hasPlan;
        public readonly List<TTrack> inTracks = new List<TTrack>(); public readonly List<TTrack> conns = new List<TTrack>();
        public readonly List<TCar> inside = new List<TCar>();          // 들어가기로 한 차(차지)
        public readonly List<TTrack> insideConn = new List<TTrack>();  // 그 차의 이음
    }

    /// <summary>자차 정보(유니티: PNUDriveEgo 에서, 시험: 흉내)</summary>
    public class EgoState
    {
        public bool valid; public V3 pos, fwd; public float speed, s, len = 4.7f; public int waitJ = -1, yieldJ = -1;
        public float holdDist = -1f;                         // 자차가 서기로 한 곳까지 거리(신호·양보·막힘, 없으면 -1): 그 너머 자차 길은 비어 있음
        public Func<float, V3> posAt;                        // 자차 경로 s → 자리
        public float pathLen;
    }

    public class TCar
    {
        public int id, model, paint; public float len = 4.5f, wid = 1.9f, wb = 2.7f;
        public TTrack tr, prev; public float s, v, v0, acc, persona = 1f;
        public readonly List<TTrack> plan = new List<TTrack>();        // plan[0] = 다음 토막(이 토막 뒤)
        public readonly List<float> planIn = new List<float>();        // 그 토막에 들어갈 때 s(합류는 중간)
        public bool active; public float alpha; public int fade;        // fade: 1 나타남, -1 사라짐
        public float stopT, age, blink; public bool brake; public int blinkSide;   // -1 왼쪽, 1 오른쪽
        public V3 pos, fwd; public bool hasPose;
        public bool done;                                    // 풀로 돌려보낼 차(사라짐 끝)
        public string why = "";                              // 사라진 까닭
        public TTrack holdTr;                                // 신호 때문에 서기로 한 토막(노랑에 멈추기 시작하면 끝까지 섬)
        public TTrack goTr;                                  // 노랑에 지나가기로 한 토막(못 서면 끝까지 감)
        public TJunc inside; public TTrack insideConn;       // 들어가기로 한 교차로·이음
        public bool gateHeld; public float gateDist;          // 정지선·교차로 앞에서 서는 중(그 거리)
        public string wait = "";                              // 시험용: 마지막으로 선 까닭
        public string dbg = "";                               // 시험용: 마지막 신호 판단
        public bool scripted, parked, hazard, keep;          // 5/6 연출 차: 서 있음(주차·정차) · 비상등 · 멀어져도 안 사라짐
        public float latOff, latS0, latL = 32f;               // 옆 차로로 비켜 가는 중: 새 차로 가운데에서 옆으로 남은 거리(+왼쪽, latS0~+latL 동안 0 으로)
    }

    public class TrafficCore
    {
        public TTrack[] T; public TJunc[] J;
        public readonly List<TCar> cars = new List<TCar>();
        public readonly List<TCar> removed = new List<TCar>();          // 풀로 돌려보낼 차(유니티가 비움)
        public Random rng = new Random(12345);
        public void Seed(int k) { rng = new Random(k); }
        public Func<int, bool> crosswalkBusy;                // 사람 길 줄 번호 → 사람이 있음
        public int target = 30;
        public float spawnMin = 150f, spawnMax = 380f, despawn = 430f, seeDist = 240f;
        public float fadeIn = 1.6f, fadeOut = 1.3f;
        public int[] modelPick; public float[] modelLen, modelWid, modelWb; public bool[] modelBig;
        public int paints = 8; public float[] paintW;
        public string[] modelKind;                           // car·suv·van·truck·bus·taxi(연출 차 고르기)
        public float speedScale = 1f;                        // 비: 0.8(젖은 노면 감속)
        public Func<TTrack, float, bool> spawnBlock;         // 연출 구간: 이 토막·자리에 새 차 안 넣음
        public Func<TCar, float> extraGate;                  // 연출 사람 등: 그 앞에서 섬(틈 + S0-0.5, 없으면 큰 값)
        float spawnClock, candClock, lastNow; readonly List<TTrack> cand = new List<TTrack>(); readonly List<float> candS0 = new List<float>(), candS1 = new List<float>(), candW = new List<float>();
        int nextId;
        public int statSpawn, statDespawn, statDead;
        const float A = 1.5f, B = 2.2f, S0 = 2.4f, HW = 1.25f;
        const float XC_D = 2.4f;                              // 이음끼리 이만큼 가까우면 겹침
        readonly V3[] EQ = new V3[64]; readonly float[] EX = new float[64]; readonly V3[] TQ = new V3[64];

        public void Load(LanesData d)
        {
            T = new TTrack[d.tracks.Length];
            for (int k = 0; k < d.tracks.Length; k++)
            {
                var s = d.tracks[k]; var t = new TTrack();
                t.id = s.id; t.poly = new Poly(s.p); t.L = t.poly.L; t.lim = s.lim; t.stop = s.stop; t.j = s.j; t.g = s.g; t.mv = s.mv; t.y = s.y; t.w = s.w;
                t.conn = s.c; t.lower = s.lo; t.ego = s.e; t.nx = s.nx ?? new int[0]; t.cf = s.cf ?? new int[0]; t.ct = s.ct ?? new int[0]; t.mg = s.mg ?? new int[0];
                T[k] = t;
            }
            int nj = 0; foreach (var q in d.junctions) nj = Math.Max(nj, q.id + 1);
            J = new TJunc[nj];
            foreach (var q in d.junctions) J[q.id] = new TJunc { id = q.id, c = new V3(q.x, q.y, q.z), r = q.r, sig = q.sig, es = q.es };
            foreach (var t in T)
            {
                if (t.conn && t.j < 0 && t.nx.Length >= 3 && t.nx[1] == 4)
                {
                    var dst = T[t.nx[0]]; t.merge = true; t.mergeAt = dst.poly.cum[Math.Min(dst.poly.Count - 1, Math.Max(0, t.nx[2]))]; dst.feeders.Add(t);
                }
                for (int k = 0; k + 2 < t.nx.Length; k += 3) { var n = Track(t.nx[k]); if (n != null && n.conn) n.srcT = t; }
            }
            foreach (var t in T)
            {
                t.deadEnd = t.nx.Length == 0;
                if (t.conn)
                {
                    if (!t.merge && t.j >= 0 && t.j < J.Length && J[t.j] != null) { t.jn = J[t.j]; t.jn.conns.Add(t); }
                    t.vTurn = TurnSpeed(t);
                }
                else if (t.j >= 0 && t.j < J.Length && J[t.j] != null) J[t.j].inTracks.Add(t);
                // 갈라지는 이음끼리
                var outs = new List<TTrack>();
                for (int k = 0; k + 2 < t.nx.Length; k += 3) { var n = Track(t.nx[k]); if (n != null && n.conn) outs.Add(n); }
                foreach (var a in outs) foreach (var b in outs) if (a != b) a.sib.Add(b);
            }
            // 교차로마다 길이 겹치는 이음(들어오는 토막이 같으면 앞뒤 차례라 뺌)
            foreach (var jn in J)
            {
                if (jn == null) continue;
                var L = jn.conns;
                for (int a = 0; a < L.Count; a++)
                    for (int b = a + 1; b < L.Count; b++)
                    {
                        if (L[a].srcT != null && L[a].srcT == L[b].srcT) continue;
                        if (PolyNear(L[a].poly, L[b].poly, XC_D)) { L[a].xc.Add(L[b]); L[b].xc.Add(L[a]); }
                    }
            }
        }

        static bool PolyNear(Poly a, Poly b, float d)
        {
            float d2 = d * d;
            for (int i = 0; i < a.Count; i++)
                for (int k = 0; k < b.Count; k++)
                {
                    float dx = a.x[i] - b.x[k], dz = a.z[i] - b.z[k];
                    if (dx * dx + dz * dz < d2 && Math.Abs(a.y[i] - b.y[k]) < 2.5f) return true;
                }
            return false;
        }

        public void SetPlan(int j, bool left0, float off) { if (j >= 0 && j < J.Length && J[j] != null) { J[j].left0 = left0; J[j].off = off; J[j].hasPlan = true; } }

        static float TurnSpeed(TTrack t)
        {
            // 방향이 바뀐 만큼과 길이로 반지름 → 옆 가속도 2.9m/s² 안(v15: 덜 보수적으로)
            var p = t.poly; int n = p.Count; if (n < 3) return t.lim;
            V3 d0 = p.Dir(0.5f), d1 = p.Dir(p.L - 0.5f);
            double a0 = Math.Atan2(d0.x, d0.z), a1 = Math.Atan2(d1.x, d1.z); double da = Math.Abs(((a1 - a0 + Math.PI * 3) % (Math.PI * 2)) - Math.PI);
            if (da < 0.2) return t.lim;
            float R = (float)(p.L / da);
            return Math.Min(t.lim, (float)Math.Sqrt(2.9f * R));
        }

        /// <summary>지금 속도 v 에서 1.5m/s² 로 더해 가며 d 만큼 가는 시간(늦게 잡지 않게 조금 이르게)</summary>
        static float Kin(float d, float v) { if (d <= 0f) return 0f; const float a = 1.5f; return (-v + (float)Math.Sqrt(v * v + 2f * a * d)) / a; }

        // ------------------------------------------------------------ 토막 고르기
        TTrack Track(int id) { return id >= 0 && id < T.Length ? T[id] : null; }

        /// <summary>다음 토막 하나 고르기(직진 위주, 막다른 길은 자차 가까우면 피함, 자차 앞 자차 차로는 덜)</summary>
        bool PickNext(TCar c, TTrack from, EgoState ego, out TTrack nxt, out float inS)
        {
            nxt = null; inS = 0f;
            int n = from.nx.Length / 3; if (n == 0) return false;
            float tot = 0f; float[] w = new float[n];
            for (int k = 0; k < n; k++)
            {
                var t = Track(from.nx[3 * k]); if (t == null) continue;
                int ty = from.nx[3 * k + 1];
                float ww = ty == 0 ? 3.0f : (ty == 2 ? 1.0f : (ty == 1 || ty == 5 ? 0.8f : 1.0f));
                if (c.len > 8f && (ty == 1 || ty == 5)) ww *= 0.3f;          // 버스는 덜 돎
                var dst = t.conn ? Track(t.nx.Length > 0 ? t.nx[0] : -1) : t;
                if (dst != null && ego != null && ego.valid)
                {
                    if (dst.deadEnd && V3.Dist2D(dst.poly.P(dst.poly.Count - 1), ego.pos) < 320f) ww *= 0.05f;
                    if (dst.ego && V3.Dist2D(dst.poly.P(0), ego.pos) < 700f && Ahead(ego, dst.poly.P(0))) ww *= 0.1f;     // 자차 앞 자차 차로(회전 차로 포함)는 덜 들어감
                }
                w[k] = ww; tot += ww;
            }
            if (tot <= 0) return false;
            double r = rng.NextDouble() * tot;
            for (int k = 0; k < n; k++)
            {
                r -= w[k];
                if (r <= 0 || k == n - 1)
                {
                    nxt = Track(from.nx[3 * k]); int ty = from.nx[3 * k + 1];
                    if (nxt == null) return false;
                    inS = ty == 4 ? nxt.poly.cum[Math.Min(nxt.poly.Count - 1, Math.Max(0, from.nx[3 * k + 2]))] : 0f;
                    return true;
                }
            }
            return false;
        }

        static bool Ahead(EgoState ego, V3 p) { V3 d = p - ego.pos; return d.x * ego.fwd.x + d.z * ego.fwd.z > 0f; }

        void FillPlan(TCar c, EgoState ego)
        {
            TTrack last = c.plan.Count > 0 ? c.plan[c.plan.Count - 1] : c.tr;
            float total = c.tr.L - c.s; for (int k = 0; k < c.plan.Count; k++) total += c.plan[k].L - c.planIn[k];
            int guard = 0;
            while ((c.plan.Count < 2 || total < 120f) && guard++ < 6)
            {
                TTrack n; float inS;
                if (!PickNext(c, last, ego, out n, out inS)) break;
                c.plan.Add(n); c.planIn.Add(inS); total += n.L - inS; last = n;
            }
        }

        // ------------------------------------------------------------ 자리·방향
        /// <summary>차의 s 에서 d 만큼 앞(-이면 뒤) 자리 — 다음/이전 토막으로 넘어감</summary>
        public V3 PointAlong(TCar c, float d)
        {
            float s = c.s + d;
            if (s < 0f)
            {
                if (c.prev != null) return c.prev.poly.At(Math.Max(0f, c.prev.L + s));
                V3 p0 = c.tr.poly.At(0), dd = c.tr.poly.Dir(0); return p0 + dd * s;
            }
            if (s <= c.tr.L) return c.tr.poly.At(s);
            s -= c.tr.L;
            for (int k = 0; k < c.plan.Count; k++)
            {
                var t = c.plan[k]; float s2 = c.planIn[k] + s;
                if (s2 <= t.L) return t.poly.At(s2);
                s = s2 - t.L;
            }
            var lt = c.plan.Count > 0 ? c.plan[c.plan.Count - 1] : c.tr; V3 pe = lt.poly.At(lt.L), de = lt.poly.Dir(lt.L); return pe + de * s;
        }

        public void Pose(TCar c)
        {
            V3 f = PointAlong(c, c.wb * 0.5f), r = PointAlong(c, -c.wb * 0.5f);
            if (c.latOff != 0f)
            {
                // 비켜 가기: 앞·뒤 바퀴 자리를 옆으로(부드럽게 줄어듦) → 방향도 따라 돎
                float uf = Lat(c, c.s + c.wb * 0.5f), ur = Lat(c, c.s - c.wb * 0.5f);
                V3 df = c.tr.poly.Dir(Math.Max(0f, Math.Min(c.tr.L, c.s))); V3 left = new V3(-df.z, 0f, df.x);
                f = f + left * uf; r = r + left * ur;
                if (c.s - c.latS0 >= c.latL) c.latOff = 0f;
            }
            V3 d = f - r; float l = d.Len2D;
            if (l > 1e-3f) c.fwd = new V3(d.x / l, d.y / l, d.z / l);
            c.pos = new V3((f.x + r.x) * 0.5f, (f.y + r.y) * 0.5f, (f.z + r.z) * 0.5f); c.hasPose = true;
        }

        // ------------------------------------------------------------ 앞 막힘
        /// <summary>앞차까지 틈(앞 범퍼 → 앞차 뒤 범퍼)과 그 속도. 없으면 큰 값</summary>
        float LeaderGap(TCar c, float look, out float lv)
        {
            lv = 0f; float best = 1e9f;
            foreach (var o in c.tr.cars)
            {
                if (o == c || o.done) continue;
                float d = o.s - c.s; if (d <= 0f) continue;
                float g = d - (c.len + o.len) * 0.5f - (o.parked && !c.scripted ? 2.6f : 0f); if (g < best) { best = g; lv = o.v; }
            }
            // 이 차로에서 옆 차로로 비켜 가는 중인 차(몸이 아직 이 차로에 걸침)
            TTrack pb; if (passLane.Count > 0 && passLane.TryGetValue(c.tr, out pb))
                foreach (var o in pb.cars)
                {
                    if (o.done || o.prev != c.tr || o.latOff == 0f || Math.Abs(Lat(o, o.s)) < 1.2f) continue;
                    float d = ProjectS(c.tr, o.pos) - c.s; if (d <= 0f) continue;
                    float g = d - (c.len + o.len) * 0.5f; if (g < best) { best = g; lv = o.v; }
                }
            Feeders(c, c.tr, -c.s, ref best, ref lv);
            if (c.tr.conn) Sibs(c, c.tr, -c.s, ref best, ref lv);
            if (best < 1e8f) return best;
            float off = c.tr.L - c.s;
            for (int k = 0; k < c.plan.Count && off < look; k++)
            {
                var t = c.plan[k]; float s0 = c.planIn[k];
                foreach (var o in t.cars)
                {
                    if (o == c || o.done) continue;
                    float d = off + o.s - s0; if (d <= -o.len * 0.5f) continue;     // 합류 지점보다 뒤에 있는 차는 옆 차로(합류는 따로)
                    float g = d - (c.len + o.len) * 0.5f; if (g < best) { best = g; lv = o.v; }
                }
                Feeders(c, t, off - s0, ref best, ref lv);
                if (t.conn) Sibs(c, t, off - s0, ref best, ref lv);
                if (best < 1e8f) return best;
                off += t.L - s0;
            }
            return best;
        }

        /// <summary>토막 t 로 합류 중인 차(이음 뒤쪽) — 내 앞이면 앞차. bas = 내 자리에서 t 의 0 까지</summary>
        void Feeders(TCar c, TTrack t, float bas, ref float best, ref float lv)
        {
            foreach (var f in t.feeders)
                foreach (var o in f.cars)
                {
                    if (o == c || o.done) continue;
                    float vs = f.mergeAt - (f.L - o.s);
                    float d = bas + vs; if (d <= 0f) continue;
                    float g = d - (c.len + o.len) * 0.5f; if (g < best) { best = g; lv = o.v; }
                }
        }

        /// <summary>같은 토막에서 갈라지는 이음의 첫머리(9m 안)에 있는 차 — 아직 내 앞을 막음</summary>
        void Sibs(TCar c, TTrack t, float bas, ref float best, ref float lv)
        {
            foreach (var sb in t.sib)
                foreach (var o in sb.cars)
                {
                    if (o == c || o.done || o.s > 9f) continue;
                    float d = bas + o.s; if (d <= 0f) continue;
                    float g = d - (c.len + o.len) * 0.5f; if (g < best) { best = g; lv = o.v * 0.5f; }
                }
        }

        /// <summary>합류 이음 앞(줄어드는 차로 끝): 옆 차로 들어갈 자리 앞뒤가 비었을 때만</summary>
        float MergeGate(TCar c)
        {
            if (c.tr.conn || c.plan.Count == 0 || !c.plan[0].merge) return 1e9f;
            float rem = c.tr.L - c.s - c.len * 0.5f;
            if (rem > 45f) return 1e9f;
            var f = c.plan[0]; var dst = Track(f.nx[0]); if (dst == null) return 1e9f;
            foreach (var o in dst.cars)
            {
                if (o.done) continue;
                float rel = o.s - f.mergeAt;
                if (rel < 10f && rel > -(o.v * 5f + 15f)) return Math.Max(0f, rem) + (S0 - 0.5f);     // 5초 안에 올 차
            }
            foreach (var g in dst.feeders) if (g != f) foreach (var o in g.cars) if (!o.done) return Math.Max(0f, rem) + (S0 - 0.5f);
            return 1e9f;
        }

        /// <summary>자차가 이 차가 갈 길 위(지금 자리)에 있으면 틈</summary>
        float EgoGap(TCar c, EgoState ego, float look, out float lv)
        {
            lv = 0f;
            if (ego == null || !ego.valid) return 1e9f;
            if (V3.Dist2DSq(c.pos, ego.pos) > (look + 12f) * (look + 12f)) return 1e9f;
            for (float d = 0f; d <= look; d += 2f)
            {
                V3 p = PointAlong(c, d);
                if (Math.Abs(p.y - ego.pos.y) > 3.5f) continue;
                if (V3.Dist2DSq(p, ego.pos) < 1.7f * 1.7f)
                {
                    float al = c.fwd.x * ego.fwd.x + c.fwd.z * ego.fwd.z;
                    lv = Math.Max(0f, ego.speed * al);
                    return d - (c.len + ego.len) * 0.5f;
                }
            }
            return 1e9f;
        }

        /// <summary>자차가 앞으로 몇 초 지날 길과 이 차 앞길이 겹치면: 엇갈림은 자차가 먼저 오거나 비슷하면 그 앞에서 섬,
        /// 같은 쪽으로 합쳐지면(자차 끼어들기) 이 차가 뒤거나 나란할 때 뒤로 처짐</summary>
        float EgoConflictGap(TCar c, EgoState ego, out float lv)
        {
            lv = 0f;
            if (ego == null || !ego.valid || ego.posAt == null || !c.hasPose) return 1e9f;
            if (V3.Dist2DSq(c.pos, ego.pos) > 125f * 125f || Math.Abs(c.pos.y - ego.pos.y) > 8f) return 1e9f;
            float vE = Math.Max(0f, ego.speed);
            bool egoStop = vE < 0.4f;
            float xmax = egoStop ? 5f : Math.Min(75f, 6f + vE * 4.0f);
            if (ego.holdDist >= 0f) xmax = Math.Min(xmax, ego.holdDist + ego.len * 0.5f);   // 자차가 정지선·양보선에서 섬: 그 너머는 안 막음
            if (xmax < -2f) return 1e9f;
            int nq = 0;
            for (float x = -2.5f; x <= xmax && nq < EQ.Length; x += 1.5f)
            {
                float s = ego.s + x; if (s < 0f || s > ego.pathLen) continue;
                EQ[nq] = ego.posAt(s); EX[nq] = x; nq++;
            }
            if (nq < 2) return 1e9f;
            float look = Math.Min(90f, Math.Max(25f, c.v * 5f + 15f));
            float W = (c.wid + 1.9f) * 0.5f + 0.35f, W2 = W * W;
            for (float d = 0f; d <= look; d += 1.5f)
            {
                V3 p = PointAlong(c, d);
                int hit = -1;
                for (int k = 0; k < nq; k++)
                {
                    if (Math.Abs(p.y - EQ[k].y) > 3f) continue;
                    if (V3.Dist2DSq(p, EQ[k]) < W2) { hit = k; break; }
                }
                if (hit < 0) continue;
                if (d < c.len * 0.5f + 2f) return 1e9f;          // 이미 자차 길 위(자차 앞차·옆): 앞차 따라가기 규칙
                float x = EX[hit];
                V3 pd = PointAlong(c, d + 1f) - PointAlong(c, d - 1f);
                V3 qd = EQ[Math.Min(nq - 1, hit + 1)] - EQ[Math.Max(0, hit - 1)];
                float cs = V3.Cos2D(pd, qd);
                float myFront = d - c.len * 0.5f;
                if (cs > 0.6f)
                {
                    if (d > x - 4f) { lv = vE * cs; c.wait = "egoMerge"; return Math.Max(0.5f, myFront - 1f); }
                    return 1e9f;
                }
                float tN = Kin(Math.Max(0f, myFront), c.v);
                float tE = Math.Max(0f, x) / Math.Max(vE, 0.5f);
                if (tE < tN + 2.5f) { c.wait = "egoCross"; return Math.Max(0f, myFront - 1.5f); }
                return 1e9f;
            }
            return 1e9f;
        }

        /// <summary>자차가 따라갈 앞차(자차 경로 위 NPC)까지 틈 — PNUDriveEgo 로</summary>
        public float EgoLeadGap(EgoState ego, out float lv)
        {
            lv = 0f; float best = 1e9f;
            if (ego == null || !ego.valid || ego.posAt == null) return best;
            const float look = 70f;
            foreach (var c in cars)
            {
                if (!c.active || c.done || !c.hasPose) continue;
                if (V3.Dist2DSq(c.pos, ego.pos) > (look + 10f) * (look + 10f)) continue;
                V3 d0 = c.pos - ego.pos; if (d0.x * ego.fwd.x + d0.z * ego.fwd.z < 0f) continue;
                for (float d = 2f; d <= look; d += 2f)
                {
                    if (ego.s + d > ego.pathLen) break;
                    V3 p = ego.posAt(ego.s + d);
                    if (Math.Abs(p.y - c.pos.y) > 3.5f) continue;
                    float wl = Math.Max(1.6f, (c.wid + 1.9f) * 0.5f + 0.15f);       // 차로 바꾸는 중: 옆 차로에 몸이 걸치는 차도 앞차
                    if (V3.Dist2DSq(p, c.pos) < wl * wl)
                    {
                        float g = d - (c.len + ego.len) * 0.5f;
                        if (g < best) { best = g; float al = c.fwd.x * ego.fwd.x + c.fwd.z * ego.fwd.z; lv = Math.Max(0f, c.v * al); }
                        break;
                    }
                }
            }
            return best;
        }

        /// <summary>자차가 교차로 j 에서 돌아도 되나(비보호 좌회전·빨강 우회전): 자차가 지날 길(정지선 ~ 교차로 나가는 곳)을
        /// 5.5초 안에 지날 차가 없을 때. 자차 뒤를 따라오는 차·자차 앞에서 같은 쪽으로 가는 차는 뺌</summary>
        public bool EgoTurnClear(EgoState ego, int j, float stopS)
        {
            if (ego == null || !ego.valid || ego.posAt == null) return true;
            var jn = (j >= 0 && j < J.Length) ? J[j] : null;
            float s0 = Math.Max(ego.s, stopS - 1f), s1 = ((jn != null && jn.es >= 0f) ? jn.es : stopS + 15f) + (jn != null ? jn.r : 15f) + 8f;
            int n = 0;
            for (float s = s0; s <= s1 && n < TQ.Length; s += 2f) { TQ[n] = ego.posAt(s); n++; }
            if (n < 2) return true;
            V3 mid = TQ[n / 2];
            const float W2 = 2.5f * 2.5f;
            foreach (var o in cars)
            {
                if (!o.active || o.done || !o.hasPose) continue;
                if (V3.Dist2DSq(o.pos, mid) > 150f * 150f) continue;
                if (BehindOnEgoPath(o, ego)) continue;
                int k = NearIdx(o.pos, TQ, n, W2);
                if (k >= 0)
                {
                    V3 qd = TQ[Math.Min(n - 1, k + 1)] - TQ[Math.Max(0, k - 1)];
                    if (V3.Cos2D(o.fwd, qd) > 0.6f) continue;      // 자차 앞에서 같은 쪽으로 감(앞차)
                    return false;
                }
                float look = Math.Min(100f, o.v * 6f + 25f);
                for (float d = 2f; d <= look; d += 2f)
                {
                    V3 p = PointAlong(o, d);
                    if (NearIdx(p, TQ, n, W2) < 0) continue;
                    if (o.gateHeld && o.gateDist < d - o.len * 0.5f - 0.5f) break;     // 그 앞에서 섬
                    if (Kin(d - o.len * 0.5f, o.v) < 5.5f) return false;
                    break;
                }
            }
            return true;
        }

        static int NearIdx(V3 p, V3[] Q, int n, float W2) { for (int k = 0; k < n; k++) if (Math.Abs(p.y - Q[k].y) < 3f && V3.Dist2DSq(p, Q[k]) < W2) return k; return -1; }

        static bool BehindOnEgoPath(TCar o, EgoState ego)
        {
            for (float b = 0f; b <= 60f; b += 3f)
            {
                float s = ego.s - b; if (s < 0f) break;
                V3 q = ego.posAt(s);
                if (Math.Abs(q.y - o.pos.y) < 3f && V3.Dist2DSq(q, o.pos) < 4f) return true;
            }
            return false;
        }

        bool EgoNearJunction(EgoState ego, TJunc j, float before, float after)
        {
            if (ego == null || !ego.valid || j == null) return false;
            if (j.es >= 0f) { float d = j.es - ego.s; return d < before && d > -after; }
            return V3.Dist2D(ego.pos, j.c) < j.r + 6f;
        }

        // ------------------------------------------------------------ 교차로
        /// <summary>앞으로 지날 교차로(지금 토막 + 계획 토막, 90m 안)마다 정지선 판단 → 처음 막히는 곳까지 틈</summary>
        float JunctionGate(TCar c, EgoState ego, float now)
        {
            float front = c.s + c.len * 0.5f;
            float g = GateOn(c, c.tr, c.plan.Count > 0 ? c.plan[0] : null, Line(c.tr) - front, ego, now);
            if (g < 1e8f) return g;
            float E = c.tr.L - front;                       // 앞 범퍼 → 지금 토막 끝
            for (int k = 0; k < c.plan.Count && E < 90f; k++)
            {
                var t = c.plan[k];
                g = GateOn(c, t, k + 1 < c.plan.Count ? c.plan[k + 1] : null, E + Line(t) - c.planIn[k], ego, now);
                if (g < 1e8f) return g;
                E += t.L - c.planIn[k];
            }
            return 1e9f;
        }

        static float Line(TTrack t) { return t.stop >= 0f ? t.stop : t.L - 0.8f; }

        float GateOn(TCar c, TTrack t, TTrack nx, float dist, EgoState ego, float now)
        {
            if (t.conn || t.j < 0 || nx == null || !nx.conn || nx.jn == null) return 1e9f;
            if (dist < -0.6f || dist > 90f) return 1e9f;     // 이미 넘었거나 멂
            return GateAt(c, t, nx, dist, ego, now);
        }

        float GateAt(TCar c, TTrack t, TTrack nx, float dist, EgoState ego, float now)
        {
            var jn = nx.jn; if (jn == null) return 1e9f;
            bool go = true; int mv = nx.mv; string why = "";
            bool mustGo = c.v * c.v / (2f * 5.5f) > dist + 0.3f;            // 세게 밟아도 정지선 앞에 못 섬
            if (jn.sig && jn.hasPlan)
            {
                int asp = SigMath.Aspect(jn.left0, jn.off, t.g, now);
                // 노랑: 편히(3m/s²) 설 수 있으면 섬, 못 서면 빨강 전에 정지선을 넘을 수 있거나 세게(5m/s²)도 못 설 때만 지나감. 한번 서기로 했으면 끝까지 섬
                bool cantStop = false;
                if (asp == 1 && c.holdTr != t)
                {
                    float yRem = SigMath.YellowRemain(jn.left0, jn.off, t.g, now);
                    bool comfort = c.v * c.v / (2f * 3.0f) <= dist - 0.5f, hard = c.v * c.v / (2f * 5.0f) <= dist - 0.5f;
                    bool clear = dist <= c.v * yRem + 0.5f;
                    cantStop = !comfort && (clear || !hard);
                }
                bool sigGo;
                if (mv == 1)                                    // 보호 좌회전: 화살표에만
                    sigGo = asp == 3 || (asp == 1 && cantStop && SigMath.U(jn.left0, jn.off, now) >= SigMath.G0 + SigMath.Y);
                else if (mv == 5) sigGo = asp == 2;             // 비보호 좌회전: 초록에만(노랑에 밀고 들어가지 않음)
                else sigGo = asp == 2 || (asp == 1 && cantStop);
                if (sigGo && asp == 1 && cantStop) c.goTr = t;
                if (!sigGo && c.goTr == t && c.holdTr != t && c.v > 3f && c.v * c.v / (2f * 5.0f) > dist - 0.5f) sigGo = true;       // 노랑에 가기로 했고 이제 못 섬
                if (t == c.tr) c.dbg = "asp" + asp + " d" + dist.ToString("0.0") + " cant" + cantStop + " v" + c.v.ToString("0.0");
                if (!sigGo) { go = false; why = "signal"; c.holdTr = t; if (c.goTr == t) c.goTr = null; }
                else if ((asp == 2 && mv != 1) || asp == 3) { if (c.holdTr == t) c.holdTr = null; }
                if (go && mv == 5 && EgoNearJunction(ego, jn, 80f, 4f) && ego.waitJ != jn.id && ego.yieldJ != jn.id) { go = false; why = "egoU"; }
            }
            else
            {
                // 신호 없는 교차로: 곁길(y=1)은 일단 멈춤 + 자차가 오면 기다림, 큰길 좌회전은 자차가 오면 기다림(나머지 양보는 아래)
                if (t.y == 1)
                {
                    if (EgoNearJunction(ego, jn, Math.Max(60f, (ego != null ? ego.speed : 0f) * 7.5f), 6f)) { go = false; why = "egoSide"; }   // 자차가 7.5초 안에 오면 기다림
                    else if (c.v > 0.5f && dist > 0.4f) { go = false; why = "stopFirst"; }
                }
                else if ((mv == 1 || mv == 5) && EgoNearJunction(ego, jn, 60f, 4f) && ego.waitJ != jn.id && ego.yieldJ != jn.id) { go = false; why = "egoL"; }
            }
            if (go && c.inside != jn)
            {
                if (ConflictInside(c, nx, jn)) { go = false; why = "inside"; }
                else if (!mustGo && YieldToApproaching(c, nx, jn, dist)) { go = false; why = "yield"; }
                else if (!mustGo && !ExitRoom(c, nx)) { go = false; why = "box"; }
            }
            if (go && dist < 10f && c.inside == null) Commit(c, jn, nx);
            if (!go && c.inside == jn && c.insideConn == nx) Uncommit(c);
            if (go) return 1e9f;
            c.wait = why; c.gateDist = Math.Max(0f, dist);
            return Math.Max(0f, dist) + (S0 - 0.5f);           // IDM 최소 틈(S0)만큼 더해 정지선 0.5m 앞에 섬
        }

        /// <summary>교차로를 차지한(들어가기로 한) 차 중 길이 겹치는 이음에 있는 차 / 차지 없이 겹치는 이음 위에 있는 차</summary>
        bool ConflictInside(TCar c, TTrack X, TJunc jn)
        {
            for (int k = 0; k < jn.inside.Count; k++)
            {
                var o = jn.inside[k]; if (o == c || o.done) continue;
                var Y = jn.insideConn[k];
                if (!X.xc.Contains(Y)) continue;
                if (o.tr == Y && o.s - o.len * 0.5f > Y.L - 1.5f) continue;     // 거의 빠져나감
                return true;
            }
            foreach (var Y in X.xc) foreach (var o in Y.cars) if (o != c && !o.done && o.s - o.len * 0.5f < Y.L - 1.5f) return true;
            return false;
        }

        /// <summary>o 가 교차로 jn 에서 탈 이음과 그 첫머리까지 거리(앞 범퍼). 이미 이음 위면 음수</summary>
        static bool NextConnAt(TCar o, TJunc jn, float maxD, out TTrack Y, out float dO)
        {
            Y = null; dO = 0f;
            if (o.tr.conn && o.tr.jn == jn) { Y = o.tr; dO = -o.s - 0.01f; return true; }
            float d = o.tr.L - (o.s + o.len * 0.5f);
            for (int k = 0; k < o.plan.Count && d < maxD; k++)
            {
                var t = o.plan[k];
                if (t.conn && t.jn == jn) { Y = t; dO = d; return true; }
                d += t.L - o.planIn[k];
            }
            return false;
        }

        /// <summary>우선순위: 신호 교차로 = 비보호 좌회전 1, 나머지 2 / 신호 없는 = 곁길 1, 큰길 좌회전 1.5, 큰길 직진·우회전 2</summary>
        static float Prio(TTrack X, TJunc jn)
        {
            if (jn.sig && jn.hasPlan) return X.mv == 5 ? 1f : 2f;
            int y = X.srcT != null ? X.srcT.y : 0;
            if (y == 1) return 1f;
            return (X.mv == 1 || X.mv == 5) ? 1.5f : 2f;
        }

        /// <summary>겹치는 이음으로 곧(7초 안) 들어올 차: 우선순위가 높으면, 같으면 먼저 오는 차에 양보(서기로 한 차는 뺌)</summary>
        bool YieldToApproaching(TCar c, TTrack X, TJunc jn, float distC)
        {
            if (X.xc.Count == 0) return false;
            float pC = Prio(X, jn), etaC = Kin(Math.Max(0f, distC), c.v);
            foreach (var o in cars)
            {
                if (o == c || !o.active || o.done || !o.hasPose) continue;
                if (V3.Dist2DSq(o.pos, jn.c) > 130f * 130f) continue;
                TTrack Y; float dO;
                if (!NextConnAt(o, jn, 120f, out Y, out dO)) continue;
                if (!X.xc.Contains(Y)) continue;
                if (o.inside == jn || dO < 0f) return true;
                if (o.gateHeld && o.gateDist <= dO + 0.5f) continue;          // 그 앞에서 섬(빨강·양보)
                float etaO = Kin(dO, o.v);
                if (etaO > 7f) continue;
                float pO = Prio(Y, jn);
                if (pO > pC + 0.01f) return true;
                if (pO < pC - 0.01f) continue;
                if (etaO + 0.4f < etaC || (Math.Abs(etaO - etaC) <= 0.4f && o.id < c.id)) return true;
            }
            return false;
        }

        /// <summary>건너편(나가는 토막 첫머리)에 차 한 대 들어갈 자리가 있나 — 없으면 교차로 안에 갇히니 정지선에서 기다림</summary>
        bool ExitRoom(TCar c, TTrack X)
        {
            var dst = X.nx.Length > 0 ? Track(X.nx[0]) : null; if (dst == null) return true;
            float need = c.len + 2.5f, off = 0f; var t = dst; int guard = 0;
            while (t != null && guard++ < 3)
            {
                foreach (var o in t.cars) if (!o.done && o != c && off + o.s - o.len * 0.5f < need && o.v < 2.0f) return false;
                if (off + t.L >= need) break;
                off += t.L; t = t.nx.Length > 0 ? Track(t.nx[0]) : null;
            }
            foreach (var o in X.cars) if (!o.done && o != c && o.v < 1.0f) return false;
            return true;
        }

        void Commit(TCar c, TJunc jn, TTrack X) { if (c.inside != null) return; c.inside = jn; c.insideConn = X; jn.inside.Add(c); jn.insideConn.Add(X); }
        void Uncommit(TCar c)
        {
            var jn = c.inside; if (jn == null) return;
            int k = jn.inside.IndexOf(c); if (k >= 0) { jn.inside.RemoveAt(k); jn.insideConn.RemoveAt(k); }
            c.inside = null; c.insideConn = null;
        }
        void LeaveJunction(TCar c) { Uncommit(c); }

        /// <summary>회전 이음 위: 나가는 팔 횡단보도에 사람 → 이음 끝에서 기다림</summary>
        float CrosswalkGate(TCar c)
        {
            if (crosswalkBusy == null) return 1e9f;
            TTrack conn = null; float off = 0f;
            if (c.tr.conn) { conn = c.tr; off = conn.L - c.s; }
            else if (c.plan.Count > 0 && c.plan[0].conn) { conn = c.plan[0]; off = c.tr.L - c.s + conn.L; }
            if (conn == null || conn.w < 0) return 1e9f;
            if (off > 40f) return 1e9f;
            if (!crosswalkBusy(conn.w)) return 1e9f;
            float g = off - c.len * 0.5f - 0.6f;
            return g < -1.5f ? 1e9f : Math.Max(0f, g) + (S0 - 0.5f);   // 이미 들어섰으면 그대로
        }

        // ------------------------------------------------------------ 한 걸음
        public void Step(float dt, float now, EgoState ego)
        {
            if (dt <= 0f) return;
            lastNow = now;
            foreach (var c in cars) if (c.active && !c.done) Drive(c, dt, now, ego);
            // 토막 넘기기
            foreach (var c in cars)
            {
                if (!c.active || c.done) continue;
                int guard = 0;
                while (c.s > c.tr.L && guard++ < 4)
                {
                    if (c.plan.Count == 0) { c.s = c.tr.L; c.v = 0f; break; }
                    float over = c.s - c.tr.L;
                    var n = c.plan[0]; float inS = c.planIn[0];
                    c.plan.RemoveAt(0); c.planIn.RemoveAt(0);
                    c.tr.cars.Remove(c); c.prev = c.tr; c.tr = n; c.s = inS + over; n.cars.Add(c);
                    if (c.latOff != 0f) c.latS0 = inS - (c.prev.L - c.latS0);      // 비켜 가는 중이면 지나온 거리 이어서
                    if (c.holdTr == c.prev) c.holdTr = null;       // 지나온 정지선 판단만 지움(앞 토막 판단은 그대로)
                    if (c.goTr == c.prev) c.goTr = null;
                    if (!n.conn) LeaveJunction(c);
                    FillPlan(c, ego);
                }
                Pose(c);
            }
            // 사라짐·나타남
            foreach (var c in cars)
            {
                if (!c.active || c.done) continue;
                c.age += dt;
                if (c.fade == 1) { c.alpha += dt / fadeIn; if (c.alpha >= 1f) { c.alpha = 1f; c.fade = 0; } }
                else if (c.fade == -1) { c.alpha -= dt / fadeOut; if (c.alpha <= 0f) { c.alpha = 0f; Remove(c); continue; } }
                if (c.fade != -1 && !c.keep)
                {
                    bool ev = ego != null && ego.valid;
                    float de = ev ? V3.Dist2D(c.pos, ego.pos) : 0f;
                    bool seen = ev && de < seeDist && Seen(ego, c.pos, de);
                    bool endNear = c.plan.Count == 0 && c.tr.L - c.s < 30f;
                    if (de > despawn) { c.fade = -1; c.why = "far"; }
                    else if (endNear && !seen) { c.fade = -1; c.why = "deadend"; }          // 보이면 끝에서 서서 기다림
                    else if (c.stopT > 60f && !seen) { c.fade = -1; c.why = "stuck"; statDead++; }
                }
            }
            // 새 차
            Spawn(dt, ego, false);
        }

        /// <summary>자차가 볼 수 있는 쪽(앞·옆, 바로 뒤 150도 빼고)</summary>
        static bool Seen(EgoState ego, V3 p, float de)
        {
            V3 d = p - ego.pos; return d.x * ego.fwd.x + d.z * ego.fwd.z > -0.26f * Math.Max(1f, de);
        }

        void Drive(TCar c, float dt, float now, EgoState ego)
        {
            if (c.parked) { c.v = 0f; c.acc = 0f; c.brake = false; c.stopT = 0f; c.blinkSide = c.hazard ? 2 : 0; c.wait = "parked"; return; }
            if (c.plan.Count < 2) FillPlan(c, ego);
            var t = c.tr;
            // 바랄 속도: 토막 제한(성격), 다음 이음 회전 속도까지 미리 줄임
            float v0 = (t.conn ? t.vTurn : t.lim) * c.persona * speedScale;
            float off = t.L - c.s;
            for (int k = 0; k < c.plan.Count && off < 120f; k++)
            {
                var n = c.plan[k];
                float vn = (n.conn ? n.vTurn : n.lim) * c.persona * speedScale;
                if (vn < v0) v0 = Math.Min(v0, (float)Math.Sqrt(vn * vn + 2f * 2.0f * Math.Max(0f, off - c.len * 0.5f)));
                off += n.L - c.planIn[k];
            }
            if (c.plan.Count == 0) v0 = Math.Min(v0, (float)Math.Sqrt(2f * 1.4f * Math.Max(0f, t.L - c.s - 2f)));   // 막다른 끝
            v0 = Math.Max(v0, 0.5f);
            float look = Math.Max(40f, c.v * 4.5f + 20f);
            c.wait = "";
            if (passLane.Count > 0) TryPass(c, ego);
            float lv; float gap = LeaderGap(c, look, out lv);
            string w = gap < 1e8f ? "leader" : "";
            float ev; float eg = EgoGap(c, ego, Math.Min(look, 70f), out ev); if (eg < gap) { gap = eg; lv = ev; w = "ego"; }
            float xv; float xg = EgoConflictGap(c, ego, out xv); if (xg < gap) { gap = xg; lv = xv; w = c.wait; }
            float hard = gap;                                                  // 넘으면 안 되는 거리(앞차·자차 = 틈, 정지선 = 선까지)
            float sg = JunctionGate(c, ego, now); c.gateHeld = sg < 1e8f; if (sg < gap) { gap = sg; lv = 0f; w = c.wait; }
            float cg = CrosswalkGate(c); if (cg < gap) { gap = cg; lv = 0f; w = "crosswalk"; }
            float mg2 = MergeGate(c); if (mg2 < gap) { gap = mg2; lv = 0f; w = "merge"; }
            float og = extraGate != null ? extraGate(c) : 1e9f; if (og < gap) { gap = og; lv = 0f; w = "scen"; }
            const float SL = S0 - 0.5f;
            if (sg < 1e8f) hard = Math.Min(hard, sg - SL); if (cg < 1e8f) hard = Math.Min(hard, cg - SL); if (mg2 < 1e8f) hard = Math.Min(hard, mg2 - SL); if (og < 1e8f) hard = Math.Min(hard, og - SL);
            c.wait = w;
            // IDM
            float a = A * (1f - (float)Math.Pow(c.v / v0, 4));
            if (gap < 1e8f)
            {
                float sStar = S0 + c.v * HW + c.v * (c.v - lv) / (2f * (float)Math.Sqrt(A * B));
                float gg = Math.Max(0.1f, gap);
                a -= A * (sStar / gg) * (sStar / gg);
                if (gap < 0.3f) a = Math.Min(a, -6f);
            }
            if (a < -7f) a = -7f;
            c.acc = a;
            c.v = Math.Max(0f, c.v + a * dt);
            if (gap < 0.05f && c.v < 0.5f) c.v = 0f;
            float ds = c.v * dt;
            if (hard < 1e8f && ds > Math.Max(0f, hard)) { ds = Math.Max(0f, hard); c.v = Math.Min(c.v, ds / dt); }   // 겹치지 않게·정지선 안 넘게
            c.s += ds;
            c.brake = a < -0.9f || (c.v < 0.3f);
            c.stopT = c.v < 0.2f ? c.stopT + dt : 0f;
            // 방향지시등: 다음 이음이 좌·우회전이면 40m 전부터, 이음 위에서도
            int side = 0;
            TTrack turn = t.conn ? t : null; float dturn = 0f;
            if (turn == null && c.plan.Count > 0 && c.plan[0].conn) { turn = c.plan[0]; dturn = t.L - c.s; }
            if (turn != null && dturn < 40f)
            {
                if (turn.merge)
                {
                    V3 m0 = turn.poly.P(0), m1 = turn.poly.P(turn.poly.Count - 1), d0 = turn.poly.Dir(0);
                    float cr = d0.x * (m1.z - m0.z) - d0.z * (m1.x - m0.x);    // +: 왼쪽(유니티 x 오른쪽·z 앞)
                    side = cr > 0f ? -1 : 1;
                }
                else side = (turn.mv == 1 || turn.mv == 5) ? -1 : (turn.mv == 2 ? 1 : 0);
            }
            if (c.latOff != 0f && c.s - c.latS0 < c.latL * 0.8f) side = c.latOff < 0f ? -1 : 1;     // 비켜 가는 쪽(새 차로가 왼쪽이면 오른쪽에서 옴 → 왼쪽 깜빡이)
            c.blinkSide = c.hazard ? 2 : side;
        }

        // ------------------------------------------------------------ 나타나기·사라지기
        void Remove(TCar c)
        {
            LeaveJunction(c);
            if (c.tr != null) c.tr.cars.Remove(c);
            c.done = true; c.active = false; c.plan.Clear(); c.planIn.Clear(); statDespawn++; removed.Add(c);
        }

        static float Lat(TCar c, float s) { float u = Math.Max(0f, Math.Min(1f, (s - c.latS0) / Math.Max(1f, c.latL))); return c.latOff * (1f - u * u * (3f - 2f * u)); }

        /// <summary>서 있는 연출 차(정차 트럭) 뒤 차: 옆 차로(passLane)가 비면 그리로 비켜 감</summary>
        public readonly Dictionary<TTrack, TTrack> passLane = new Dictionary<TTrack, TTrack>();
        void TryPass(TCar c, EgoState ego)
        {
            if (c.scripted || c.latOff != 0f || c.tr.conn || passLane.Count == 0) return;
            TTrack tB; if (!passLane.TryGetValue(c.tr, out tB)) return;
            TCar block = null; float bd = 1e9f;
            foreach (var o in c.tr.cars) if (o.parked && !o.done && o.s > c.s) { float d = o.s - c.s; if (d < bd) { bd = d; block = o; } }
            if (block == null) return;
            float gb = bd - (c.len + block.len) * 0.5f;
            if (gb > 45f || gb < 4.5f) return;
            float sB = ProjectS(tB, c.pos); if (sB < 2f || sB > tB.L - 20f) return;
            foreach (var o in tB.cars)
            {
                if (o.done) continue;
                float dd = o.s - sB;
                if (dd > -(18f + o.v * 2.2f) && dd < 14f) return;             // 옆 차로 앞뒤가 비었을 때만
            }
            if (ego != null && ego.valid)
            {
                float sE = ProjectS(tB, ego.pos); V3 pe = tB.poly.At(Math.Max(0f, Math.Min(tB.L, sE)));
                bool egoSlowBehind = sE - sB < -6.5f && ego.speed < 3f;                          // 자차가 뒤에서 거의 섬: 먼저 비켜 감(자차는 이 차를 앞차로 따라감)
                if (!egoSlowBehind && V3.Dist2D(pe, ego.pos) < 2.6f && sE - sB > -(20f + ego.speed * 2f) && sE - sB < 16f) return;   // 자차가 옆 차로에(또는 그리로 들어가는 중)
            }
            V3 p0 = c.pos; V3 dB = tB.poly.Dir(sB), pB = tB.poly.At(sB); V3 left = new V3(-dB.z, 0f, dB.x);
            c.tr.cars.Remove(c); LeaveJunction(c); c.prev = c.tr; c.tr = tB; c.s = sB; tB.cars.Add(c);
            c.plan.Clear(); c.planIn.Clear(); c.holdTr = null; c.goTr = null; FillPlan(c, ego);
            c.latOff = (p0.x - pB.x) * left.x + (p0.z - pB.z) * left.z; c.latS0 = sB; c.latL = Math.Max(4f, Math.Min(32f, Math.Min(18f + c.v * 2f, gb - 2.5f)));
            Pose(c);
        }

        // ------------------------------------------------------------ 5/6 연출 차
        /// <summary>점 p 에서 가장 가까운(방향 같은) 차로 토막과 그 s. 없으면 null</summary>
        public TTrack FindTrack(V3 p, V3 dir, float maxD, out float sOut)
        {
            sOut = 0f; TTrack best = null; float bd = maxD * maxD;
            float dl = (float)Math.Sqrt(dir.x * dir.x + dir.z * dir.z); if (dl < 1e-6f) return null;
            foreach (var t in T)
            {
                if (t.conn) continue;
                var q = t.poly;
                for (int i = 0; i + 1 < q.Count; i++)
                {
                    float ax = q.x[i], az = q.z[i], dx = q.x[i + 1] - ax, dz = q.z[i + 1] - az, L2 = dx * dx + dz * dz;
                    if (L2 < 1e-6f) continue;
                    float u = ((p.x - ax) * dx + (p.z - az) * dz) / L2; u = Math.Max(0f, Math.Min(1f, u));
                    float px = ax + dx * u - p.x, pz = az + dz * u - p.z, d2 = px * px + pz * pz;
                    if (d2 >= bd) continue;
                    float py = q.y[i] + (q.y[i + 1] - q.y[i]) * u; if (Math.Abs(py - p.y) > 3.5f) continue;
                    float L = (float)Math.Sqrt(L2); if ((dx * dir.x + dz * dir.z) / (L * dl) < 0.8f) continue;
                    bd = d2; best = t; sOut = q.cum[i] + L * u;
                }
            }
            return best;
        }

        /// <summary>토막 t 위에서 점 p 에 가장 가까운 s(토막 밖이면 끝으로 늘여 음수·L 넘는 값)</summary>
        public static float ProjectS(TTrack t, V3 p)
        {
            var q = t.poly; float bd = 1e18f, bs = 0f;
            for (int i = 0; i + 1 < q.Count; i++)
            {
                float ax = q.x[i], az = q.z[i], dx = q.x[i + 1] - ax, dz = q.z[i + 1] - az, L2 = dx * dx + dz * dz;
                if (L2 < 1e-6f) continue;
                float u = ((p.x - ax) * dx + (p.z - az) * dz) / L2;
                float uc = (i == 0) ? Math.Min(1f, u) : (i + 2 == q.Count ? Math.Max(0f, u) : Math.Max(0f, Math.Min(1f, u)));
                float px = ax + dx * uc - p.x, pz = az + dz * uc - p.z, d2 = px * px + pz * pz;
                if (d2 < bd) { bd = d2; bs = q.cum[i] + (float)Math.Sqrt(L2) * uc; }
            }
            return bs;
        }

        /// <summary>연출 차 넣기(그 자리 둘레 보통 차는 바로 뺌 — 자차가 멀 때만 부름)</summary>
        public TCar SpawnScripted(TTrack t, float s, float v, int model, bool parked, bool hazard, bool fadeIn, int paint = -1)
        {
            if (t == null || modelLen == null || model < 0 || model >= modelLen.Length) return null;
            foreach (var o in t.cars.ToArray()) if (!o.done && !o.scripted && Math.Abs(o.s - s) < (o.len + modelLen[model]) * 0.5f + 6f) Remove(o);
            var c = new TCar { id = nextId++, model = model, paint = paint >= 0 ? paint : PickPaint(), len = modelLen[model], wid = modelWid[model], wb = modelWb[model] };
            c.tr = t; c.s = Math.Max(0f, Math.Min(t.L, s)); c.v = parked ? 0f : v; c.persona = 1f;
            c.active = true; c.alpha = fadeIn ? 0f : 1f; c.fade = fadeIn ? 1 : 0;
            c.scripted = true; c.parked = parked; c.hazard = hazard; c.keep = true;
            t.cars.Add(c); cars.Add(c);
            FillPlan(c, null); Pose(c); statSpawn++;
            return c;
        }

        /// <summary>연출 차를 보통 차로(서 있던 차는 떠남)</summary>
        public void Release(TCar c) { if (c == null || c.done) return; c.parked = false; c.hazard = false; c.keep = false; c.scripted = false; if (c.plan.Count < 2) FillPlan(c, null); }

        /// <summary>바로 뺌(안 보일 때만)</summary>
        public void RemoveNow(TCar c) { if (c != null && !c.done) Remove(c); }

        /// <summary>천천히 사라지게</summary>
        public void FadeOut(TCar c) { if (c == null || c.done) return; c.keep = false; c.parked = false; if (c.fade != -1) { c.fade = -1; c.why = "scen"; } }

        /// <summary>종류(truck·bus …)로 모델 번호, 없으면 큰 차 중 가장 긴 것(bus) / 짧은 것(truck), 그것도 없으면 0</summary>
        public int ModelOfKind(string kind)
        {
            if (modelLen == null || modelLen.Length == 0) return -1;
            if (modelKind != null) for (int i = 0; i < modelKind.Length && i < modelLen.Length; i++) if (modelKind[i] == kind) return i;
            int best = -1;
            if (modelBig != null)
                for (int i = 0; i < modelLen.Length && i < modelBig.Length; i++)
                {
                    if (!modelBig[i]) continue;
                    if (best < 0 || (kind == "bus" ? modelLen[i] > modelLen[best] : modelLen[i] < modelLen[best])) best = i;
                }
            return best >= 0 ? best : 0;
        }

        public bool SeenByEgo(EgoState ego, V3 p) { if (ego == null || !ego.valid) return false; float de = V3.Dist2D(p, ego.pos); return de < seeDist && Seen(ego, p, de); }

        public void Clear()
        {
            foreach (var c in cars) { LeaveJunction(c); if (c.tr != null) c.tr.cars.Remove(c); c.done = true; c.active = false; c.plan.Clear(); c.planIn.Clear(); removed.Add(c); }
            cars.Clear(); cand.Clear(); candClock = 0f;
            if (J != null) foreach (var jn in J) if (jn != null) { jn.inside.Clear(); jn.insideConn.Clear(); }
        }

        int ActiveCount() { int n = 0; foreach (var c in cars) if (c.active && !c.done) n++; return n; }

        void BuildCandidates(EgoState ego, float r0, float r1)
        {
            cand.Clear(); candS0.Clear(); candS1.Clear(); candW.Clear();
            if (ego == null || !ego.valid) return;
            foreach (var t in T)
            {
                if (t.conn || t.L < 20f) continue;
                var p = t.poly; float a = -1f, b = -1f;
                for (int i = 0; i < p.Count; i++)
                {
                    float d = V3.Dist2D(p.P(i), ego.pos);
                    if (d >= r0 && d <= r1 && Math.Abs(p.y[i] - ego.pos.y) < 60f) { if (a < 0f) a = p.cum[i]; b = p.cum[i]; }
                }
                if (a < 0f) continue;
                a = Math.Max(a, 6f); b = Math.Min(b, t.L - 8f);
                if (t.stop >= 0f) b = Math.Min(b, t.stop - 12f);
                if (b - a < 4f) continue;
                cand.Add(t); candS0.Add(a); candS1.Add(b); candW.Add(b - a);
            }
        }

        bool InEgoCorridor(EgoState ego, V3 p, float ahead)
        {
            if (ego == null || !ego.valid || ego.posAt == null) return false;
            for (float d = -40f; d <= ahead; d += 4f)
            {
                float s = ego.s + d; if (s < 0f || s > ego.pathLen) continue;
                V3 q = ego.posAt(s);
                if (Math.Abs(q.y - p.y) < 4f && V3.Dist2DSq(q, p) < 2.6f * 2.6f) return true;
            }
            return false;
        }

        /// <summary>새 차 넣기. initial = 처음(또는 건너뛰기 뒤): 가까운 곳부터 바로 보이게</summary>
        public void Spawn(float dt, EgoState ego, bool initial)
        {
            if (ego == null || !ego.valid) return;
            candClock -= dt;
            float r0 = initial ? 35f : spawnMin, r1 = spawnMax;
            if (initial || candClock <= 0f || cand.Count == 0) { BuildCandidates(ego, r0, r1); candClock = 1.0f; }
            if (cand.Count == 0) return;
            spawnClock -= dt;
            int budget = initial ? target : 1;
            if (!initial && spawnClock > 0f) return;
            spawnClock = 0.12f;
            int tries = 0;
            while (budget > 0 && ActiveCount() < target && tries++ < budget * 8 + 8)
            {
                float tot = 0f; foreach (var wt in candW) tot += wt;
                double r = rng.NextDouble() * tot; int k = 0;
                for (; k < cand.Count - 1; k++) { r -= candW[k]; if (r <= 0) break; }
                var t = cand[k]; float s = candS0[k] + (float)rng.NextDouble() * (candS1[k] - candS0[k]);
                if (spawnBlock != null && spawnBlock(t, s)) continue;
                bool ok = true;
                foreach (var o in t.cars) if (Math.Abs(o.s - s) < 28f) { ok = false; break; }
                if (!ok) continue;
                V3 p = t.poly.At(s);
                if (InEgoCorridor(ego, p, 420f)) continue;
                if (V3.Dist2D(p, ego.pos) < (initial ? 30f : r0 - 15f)) continue;
                int m = PickModel(t);
                var c = new TCar { id = nextId++, model = m, paint = PickPaint(), len = modelLen[m], wid = modelWid[m], wb = modelWb[m] };
                c.tr = t; c.s = s; c.persona = 0.88f + 0.18f * (float)rng.NextDouble();
                c.active = true; c.alpha = initial ? 1f : 0f; c.fade = initial ? 0 : 1;
                t.cars.Add(c); cars.Add(c);
                FillPlan(c, ego);
                Pose(c);
                float lv; float gap = LeaderGap(c, 60f, out lv);
                if (gap < 8f) { t.cars.Remove(c); cars.Remove(c); continue; }
                // 처음 속도: 제한 × 성격 × 0.85, 앞차·앞 정지선에 편히(2.5m/s²) 설 수 있을 만큼
                float v = t.lim * c.persona * 0.85f;
                if (gap < 1e8f) v = Math.Min(v, (float)Math.Sqrt(2f * 2.5f * Math.Max(0f, gap - S0)) + lv);
                float sg = JunctionGate(c, ego, lastNow);
                if (sg < 1e8f) v = Math.Min(v, (float)Math.Sqrt(2f * 2.5f * Math.Max(0f, sg - S0)));
                c.v = Math.Max(0f, v);
                budget--; statSpawn++;
            }
            cars.RemoveAll(q => q.done);
            if (initial) { candClock = 0f; cand.Clear(); }      // 처음 넣기(가까운 곳 포함) 뒤엔 보통 고리(150m~)로 다시
        }

        int PickModel(TTrack t)
        {
            // 모델 고르기(무게) — 버스·큰 차는 좁은 길(제한 40 이하)엔 안 넣음
            float tot = 0f; for (int i = 0; i < modelPick.Length; i++) tot += Weight(i, t);
            double r = rng.NextDouble() * tot;
            for (int i = 0; i < modelPick.Length; i++) { r -= Weight(i, t); if (r <= 0) return i; }
            return 0;
        }
        float Weight(int i, TTrack t) { float w = modelPick[i] * 0.01f; if (modelBig[i] && t.lim < 11.5f) w = 0f; return w; }
        int PickPaint()
        {
            if (paintW == null || paintW.Length == 0) return rng.Next(paints);
            float tot = 0f; foreach (var w in paintW) tot += w;
            double r = rng.NextDouble() * tot;
            for (int i = 0; i < paintW.Length; i++) { r -= paintW[i]; if (r <= 0) return i; }
            return 0;
        }
    }

    // ================================================================ 자차 운전(PNUDriveEgo·시험 프로그램이 같이 씀)
    public class EgoDriver
    {
        public float[] stopS; public int[] stopJ, stopG; public string[] stopMv;
        public Func<float, float> vAt; public float length, egoLen = 4.7f;
        public Func<int, int, string, float, int> egoState;        // (교차로, 무리, 움직임, 시각) → 0 서기, 1 노랑, 2 가기, 4 빨강 우회전
        public Func<int, bool> leftProt;                            // 교차로 주 축 보호 좌회전(화살표)
        public TrafficCore traffic; public EgoState es;             // 없으면(차 없음) 신호만 봄
        public float s, v;
        public float leadGap = 1e9f, leadSpeed;
        /// <summary>서는 까닭: 0 아님, 1 빨강·노랑, 2 좌회전 화살표 기다림, 3 빨강 우회전 일시정지, 4 비보호 좌회전 맞은편 차 기다림, 5 건너편 막힘</summary>
        public int wait; public int waitJ = -1, yieldJ = -1; public float waitDist; public string status = "";
        public int nStops; public float stopTime;
        int rightClear = -1, turnOk = -1; float rightWait;
        /// <summary>5/6 연출: 경로 s 의 줄 앞(앞 범퍼 0.5m 앞)에 섬 / 속도 상한 / 그때 쓸 감속(m/s²) / 화면 문장 / 서는 까닭 번호(6 보행자, 7 차선 변경 보류)</summary>
        public float hazStop = -1f, hazV = -1f, hazDec = 3.2f; public string hazStatus = ""; public int hazWait = 6;
        /// <summary>비 오면 0.8(젖은 노면: 제한속도의 20% 줄임)</summary>
        public float speedScale = 1f;
        public int TurnOk { get { return turnOk; } }
        public float MaxDecel { get; set; }

        public void Reset(float s0) { s = s0; v = Math.Min(v, vAt(s0)); rightClear = -1; turnOk = -1; rightWait = 0f; leadGap = 1e9f; leadSpeed = 0f; hazStop = -1f; hazV = -1f; }

        /// <summary>정지선까지 거리(차 가운데 기준) → 앞 범퍼가 선 0.5m 앞에 서게 하는 속도</summary>
        float StopV(float dist) { return (float)Math.Sqrt(Math.Max(0f, 2f * 1.8f * (dist - egoLen * 0.5f - 0.5f))); }
        static float MoveTowards(float a, float b, float d) { if (Math.Abs(b - a) <= d) return b; return a + Math.Sign(b - a) * d; }

        bool BoxClear(int k)
        {
            if (leadGap > 100f || leadSpeed > 2.0f || traffic == null) return true;
            var JJ = traffic.J; int j = stopJ[k]; if (j < 0 || j >= JJ.Length || JJ[j] == null) return true;
            float exitS = (JJ[j].es >= 0f ? JJ[j].es : stopS[k] + 12f) + JJ[j].r;
            float need = exitS - (s + egoLen * 0.5f) + egoLen + 1.5f;
            return leadGap >= need;
        }

        public void Step(float dt, float t)
        {
            if (dt <= 0f) return;
            float vt = Math.Max(vAt(s) * speedScale, s < length - 30f ? 3f : 0f);
            wait = 0; waitJ = -1; yieldJ = -1; waitDist = 0f; status = "";
            for (int k = 0; stopS != null && k < stopS.Length; k++)
            {
                float dist = stopS[k] - s;
                if (dist < -0.5f) continue; if (dist > 140f) break;
                string mv = string.IsNullOrEmpty(stopMv[k]) ? "S" : stopMv[k];
                int state = egoState(stopJ[k], stopG[k], mv, t);
                if (state == 2)
                {
                    // 초록: 비보호 좌회전은 맞은편 차가 없을 때, 건너편이 막혔으면 정지선에서 기다림
                    if (traffic == null || es == null || turnOk == k || dist > 40f) break;
                    bool unprot = mv == "L" && !(leftProt(stopJ[k]) && stopG[k] == 0);
                    string hold = null; int hw = 0;
                    if (unprot && !traffic.EgoTurnClear(es, stopJ[k], stopS[k])) { hold = "비보호 좌회전 — 맞은편 차 기다림"; hw = 4; yieldJ = stopJ[k]; }
                    else if (dist < 30f && !BoxClear(k)) { hold = "앞이 막혀 교차로 앞에서 기다림"; hw = 5; }
                    else if (dist < 8f) turnOk = k;                  // 들어가기로 함(다시 안 따짐)
                    if (hold == null) break;
                    vt = Math.Min(vt, StopV(dist)); status = hold; wait = hw; waitJ = stopJ[k]; waitDist = dist; break;
                }
                if (state == 4)
                {
                    if (rightClear == k) break;                          // 일시정지 끝 → 우회전
                    if (dist - egoLen * 0.5f < 1.3f && v < 0.3f)
                    {
                        rightWait += dt;
                        if (rightWait > 2.0f && (traffic == null || es == null || traffic.EgoTurnClear(es, stopJ[k], stopS[k]))) { rightClear = k; break; }
                    }
                    else rightWait = 0f;
                    vt = Math.Min(vt, StopV(dist));
                    status = rightWait > 2.0f ? "빨강 우회전 — 오는 차 기다림" : "빨강 — 우회전 전 일시정지"; wait = 3; waitJ = stopJ[k]; waitDist = dist; break;
                }
                float brake = v * v / (2f * 3.0f);
                if (state == 1 && brake > dist - egoLen * 0.5f - 0.5f) break;          // 노랑인데 못 서면 지나감
                vt = Math.Min(vt, StopV(dist));
                status = state == 1 ? "노랑 — 정지" : (mv == "L" ? "좌회전 신호 기다림" : "빨강 — 정지");
                wait = mv == "L" && state != 1 ? 2 : 1; waitJ = stopJ[k]; waitDist = dist; break;
            }
            float dec = 3.2f;
            // 5/6 연출: 속도 상한(서행·단계 감속) · 줄 앞 정지(보행자·차선 변경 보류)
            if (hazV >= 0f && hazV < vt) { vt = hazV; dec = hazDec; if (status == "") status = hazStatus; }
            if (hazStop >= 0f && hazStop - s > egoLen * 0.5f)
            {
                float d = hazStop - s, room = d - egoLen * 0.5f - 0.5f;
                float vs = StopV(d);
                if (vs < vt)
                {
                    vt = vs; float need = room > 0.05f ? v * v / (2f * room) : 9f;
                    dec = Math.Max(hazDec, Math.Min(7f, need * 1.12f));
                    status = hazStatus; wait = hazWait; waitJ = -1; waitDist = d;
                }
            }
            if (leadGap < 120f)
            {
                float want = 4.0f + 1.2f * v;
                float vf = Math.Max(0f, leadSpeed + (leadGap - want) * 0.55f);
                vf = Math.Min(vf, (float)Math.Sqrt(Math.Max(0f, 2f * 3.5f * (leadGap - 2.0f))) + leadSpeed);
                if (vf < vt) { vt = vf; if (leadGap < want) dec = leadGap < Math.Max(6f, want * 0.55f) ? 6f : 4.0f; if (status == "") status = "앞차 따라감"; }   // v15: 아주 가까울 때만 세게
            }
            bool stopped = v < 0.2f;
            float v1 = MoveTowards(v, vt, (vt > v ? 1.7f : dec) * dt);
            if (dt > 0f && v1 < v) MaxDecel = Math.Max(MaxDecel, (v - v1) / dt);
            v = v1;
            if (leadGap < 1e8f && v * dt > Math.Max(0f, leadGap - 0.5f)) v = Math.Max(0f, leadGap - 0.5f) / dt;
            if (hazStop >= 0f) { float lim = hazStop - egoLen * 0.5f - 0.3f; if (lim - s > -0.3f && s + v * dt > lim) v = Math.Max(0f, lim - s) / dt; }   // 줄은 넘지 않음
            s = Math.Min(length, s + v * dt);
            if (!stopped && v < 0.2f && s < length - 5f) nStops++;
            if (v < 0.2f) stopTime += dt;
        }
    }

    // ================================================================ 사람
    public class WEdge { public int id, a, b, t, j, g; public bool ec, sig; public float es; public Poly poly; public float L; public readonly List<TPed> peds = new List<TPed>(); }

    public class TPed
    {
        public int id, model; public bool active, done; public float alpha; public int fade;
        public WEdge e; public int dir; public float s, v, speed, lat, waitT, age; public int state;   // 0 걷기, 1 기다림(횡단보도), 2 서 있기
        public WEdge nextE; public int nextDir;
        public V3 pos, fwd; public float idleT;
        // 5/6 연출 사람: 정해진 점들을 따라 걸음(spV[k] = k 로 가는 빠르기, spW[k] = k 에 닿아 멈추는 초)
        public bool scripted, spEnd; public V3[] sp; public float[] spV, spW; public int spI; public float spU, spWait;
    }

    public class PedCore
    {
        public V3[] N; public WEdge[] E; public List<int>[] adj; public TJunc[] J;
        public readonly List<TPed> peds = new List<TPed>();
        public readonly List<TPed> removed = new List<TPed>();
        public Random rng = new Random(777);
        public int target = 28; public int models = 1; public bool[] modelWalks;
        public float spawnMin = 45f, spawnMax = 210f, despawn = 250f, fadeIn = 1.4f, fadeOut = 1.2f;
        float spawnClock; int nextId;
        public int statSpawn, statDespawn;

        public PedCore(LanesData d, TJunc[] junc)
        {
            J = junc;
            int nn = d.nodes.Length / 3; N = new V3[nn];
            for (int i = 0; i < nn; i++) N[i] = new V3(d.nodes[3 * i], d.nodes[3 * i + 1], d.nodes[3 * i + 2]);
            E = new WEdge[d.edges.Length]; adj = new List<int>[nn];
            for (int i = 0; i < nn; i++) adj[i] = new List<int>();
            for (int k = 0; k < E.Length; k++)
            {
                var s = d.edges[k]; var e = new WEdge { id = k, a = s.a, b = s.b, t = s.t, j = s.j, g = s.g, es = s.es, ec = s.ec != 0, poly = new Poly(s.p) };
                e.sig = e.j >= 0 && e.j < junc.Length && junc[e.j] != null && junc[e.j].sig;
                e.L = e.poly.L; E[k] = e; adj[e.a].Add(k); adj[e.b].Add(k);
            }
        }

        public bool CrosswalkBusy(int edge)
        {
            if (edge < 0 || edge >= E.Length) return false;
            foreach (var p in E[edge].peds) if (!p.done && p.state == 0) return true;
            return false;
        }

        float WalkRemain(WEdge e, float now)
        {
            if (e.t != 2 || e.j < 0 || e.j >= J.Length || J[e.j] == null || !J[e.j].hasPlan) return 999f;
            float rem, frac;
            return SigMath.Walk(J[e.j].left0, J[e.j].off, e.g, now, out rem, out frac) ? rem : -1f;
        }

        /// <summary>자차가 자기 초록에 지나는 횡단보도(ec: 자차 회전 → 나가는 팔)면, 자차가 오는 동안(그 교차로에 서 있어도) 안 건넘</summary>
        bool EgoSoon(WEdge e, EgoState ego, float need)
        {
            if (ego == null || !ego.valid || e.es < 0f || !e.ec) return false;
            float d = e.es - ego.s;
            if (d < -6f || d > 220f) return false;
            if (ego.waitJ == e.j) return true;
            float eta = d / Math.Max(1.5f, ego.speed);
            return eta < need + 6f;
        }

        void ChooseNext(TPed p, int node, float now, EgoState ego)
        {
            var lst = adj[node];
            int came = p.e != null ? p.e.id : -1;
            // 고르기: 온 줄 말고(막다른 곳이면 되돌아감), 횡단보도는 가끔
            var opts = new List<int>(); var w = new List<float>();
            foreach (int k in lst)
            {
                if (k == came && lst.Count > 1) continue;
                var e = E[k]; float ww = e.t == 2 ? (e.sig ? 0.7f : 0f) : 1f;      // 신호 없는 횡단보도는 안 건넘
                if (ww <= 0f) continue;
                opts.Add(k); w.Add(ww);
            }
            if (opts.Count == 0) { p.nextE = p.e; p.nextDir = -p.dir; return; }
            float tot = 0f; foreach (var x in w) tot += x;
            double r = rng.NextDouble() * tot; int pick = opts[opts.Count - 1];
            for (int i = 0; i < opts.Count; i++) { r -= w[i]; if (r <= 0) { pick = opts[i]; break; } }
            var ne = E[pick];
            p.nextE = ne; p.nextDir = ne.a == node ? 1 : -1;
        }

        public void Step(float dt, float now, EgoState ego)
        {
            if (dt <= 0f) return;
            foreach (var p in peds)
            {
                if (!p.active || p.done) continue;
                p.age += dt;
                if (p.scripted)
                {
                    StepScripted(p, dt);
                    if (p.fade == 1) { p.alpha += dt / fadeIn; if (p.alpha >= 1f) { p.alpha = 1f; p.fade = 0; } }
                    else if (p.fade == -1) { p.alpha -= dt / fadeOut; if (p.alpha <= 0f) Remove(p); }
                    continue;
                }
                if (p.state == 2) { p.v = 0f; p.idleT -= dt; if (p.idleT <= 0f) p.state = 0; }
                else if (p.state == 1)
                {
                    // 횡단보도 앞 기다림: 걷기 신호 + 남은 시간 넉넉 + 자차 안 옴
                    p.v = 0f;
                    var e = p.nextE; float need = e.L / Math.Max(0.8f, p.speed) + 1.5f;
                    float rem = WalkRemain(e, now);
                    p.waitT += dt;
                    if (rem >= need && !EgoSoon(e, ego, need) && p.waitT > 0.6f) { Enter(p, e, p.nextDir); }
                    else if (p.waitT > 75f) { p.state = 0; p.nextE = null; Turn(p); }
                }
                else
                {
                    p.v = p.speed;
                    p.s += p.v * dt;
                    if (p.s >= p.e.L)
                    {
                        float over = p.s - p.e.L;
                        int node = p.dir > 0 ? p.e.b : p.e.a;
                        ChooseNext(p, node, now, ego);
                        var ne = p.nextE;
                        if (ne.t == 2)
                        {
                            float need = ne.L / Math.Max(0.8f, p.speed) + 1.5f;
                            float rem = WalkRemain(ne, now);
                            if (rem >= need && !EgoSoon(ne, ego, need)) Enter(p, ne, p.nextDir, over);
                            else { p.state = 1; p.waitT = 0f; p.s = p.e.L; }
                        }
                        else Enter(p, ne, p.nextDir, over);
                        if (p.state == 0 && rng.NextDouble() < 0.04) { p.state = 2; p.idleT = 3f + (float)rng.NextDouble() * 6f; }
                    }
                }
                Pose(p);
                if (p.fade == 1) { p.alpha += dt / fadeIn; if (p.alpha >= 1f) { p.alpha = 1f; p.fade = 0; } }
                else if (p.fade == -1) { p.alpha -= dt / fadeOut; if (p.alpha <= 0f) { Remove(p); continue; } }
                if (p.fade != -1 && ego != null && ego.valid && V3.Dist2D(p.pos, ego.pos) > despawn && p.e.t != 2) p.fade = -1;
            }
            peds.RemoveAll(q => q.done);
            Spawn(dt, ego, false);
        }

        void Turn(TPed p) { p.dir = -p.dir; p.s = p.e.L - p.s; }

        /// <summary>5/6 연출 사람 넣기: pts 를 차례로 걸음</summary>
        public TPed SpawnScripted(V3[] pts, float[] speeds, float[] waits, int model, bool fadeIn)
        {
            if (pts == null || pts.Length < 2) return null;
            var p = new TPed { id = nextId++, model = Math.Max(0, Math.Min(Math.Max(1, models) - 1, model)), active = true, alpha = fadeIn ? 0f : 1f, fade = fadeIn ? 1 : 0, scripted = true };
            p.sp = pts; p.spV = speeds; p.spW = waits; p.spI = 0; p.spU = 0f; p.spWait = 0f; p.speed = speeds != null && speeds.Length > 1 ? speeds[1] : 1.3f;
            p.pos = pts[0]; V3 d = pts[1] - pts[0]; float l = d.Len2D; p.fwd = l > 1e-3f ? new V3(d.x / l, 0f, d.z / l) : new V3(0, 0, 1); p.state = 2;
            peds.Add(p); statSpawn++;
            return p;
        }

        void StepScripted(TPed p, float dt)
        {
            if (p.spWait > 0f) { p.spWait -= dt; p.v = 0f; p.state = 2; return; }
            if (p.spI >= p.sp.Length - 1) { p.v = 0f; p.state = 2; p.spEnd = true; return; }
            V3 a = p.sp[p.spI], b = p.sp[p.spI + 1];
            float L = Math.Max(0.01f, V3.Dist2D(a, b)), v = p.spV != null && p.spI + 1 < p.spV.Length ? p.spV[p.spI + 1] : 1.3f;
            p.state = 0; p.v = v; p.spU += v * dt / L;
            if (p.spU >= 1f)
            {
                p.spU = 0f; p.spI++;
                float w = p.spW != null && p.spI < p.spW.Length ? p.spW[p.spI] : 0f; if (w > 0f) p.spWait = w;
                a = b; b = p.spI + 1 < p.sp.Length ? p.sp[p.spI + 1] : b;
            }
            p.pos = a + (b - a) * p.spU;
            V3 d = b - a; float l = d.Len2D; if (l > 1e-3f) p.fwd = new V3(d.x / l, 0f, d.z / l);
        }

        public void FadeOut(TPed p) { if (p != null && !p.done && p.fade != -1) p.fade = -1; }

        void Enter(TPed p, WEdge e, int dir, float over = 0f)
        {
            if (p.e != null) p.e.peds.Remove(p);
            p.e = e; p.dir = dir; p.s = Math.Min(over, e.L); p.state = 0; p.waitT = 0f; e.peds.Add(p);
            if (e.t == 2) p.lat = (float)(rng.NextDouble() * 2.0 - 1.0) * 1.1f;
            else if (e.t == 1) p.lat = 0.35f + (float)rng.NextDouble() * 0.4f;
            else p.lat = 0.3f + (float)rng.NextDouble() * 0.7f;
        }

        void Remove(TPed p) { if (p.e != null) p.e.peds.Remove(p); p.done = true; p.active = false; statDespawn++; removed.Add(p); }

        public void Clear() { foreach (var p in peds) { if (p.e != null) p.e.peds.Remove(p); p.done = true; p.active = false; removed.Add(p); } peds.Clear(); }

        public void Pose(TPed p)
        {
            float s = p.dir > 0 ? p.s : p.e.L - p.s;
            V3 q = p.e.poly.At(s), d = p.e.poly.Dir(s);
            if (p.dir < 0) d = d * -1f;
            if (p.state == 1 && p.nextE != null)
            {
                // 기다릴 땐 건널 쪽을 봄, 기다리는 자리는 횡단보도 첫 점 근처로 조금 흩어짐
                float s2 = p.nextDir > 0 ? 1.0f : p.nextE.L - 1.0f;
                V3 q2 = p.nextE.poly.At(s2); V3 d2 = p.nextE.poly.Dir(s2); if (p.nextDir < 0) d2 = d2 * -1f;
                V3 r2 = new V3(d2.z, 0, -d2.x);
                q = q2 - d2 * 1.2f + r2 * p.lat; d = d2;
            }
            V3 r = new V3(d.z, 0, -d.x);
            p.pos = p.state == 1 ? q : q + r * p.lat; p.fwd = d;
        }

        readonly List<WEdge> pc = new List<WEdge>(); float pcClock;
        public void Spawn(float dt, EgoState ego, bool initial)
        {
            if (ego == null || !ego.valid || E.Length == 0) return;
            spawnClock -= dt; if (!initial && spawnClock > 0f) return; spawnClock = 0.15f;
            float r0 = initial ? 25f : spawnMin;
            pcClock -= dt;
            if (initial || pcClock <= 0f)
            {
                pc.Clear(); pcClock = 1.0f;
                foreach (var e in E)
                {
                    if (e.t == 2 || e.L < 6f) continue;
                    for (int i = 0; i < e.poly.Count; i += 2)
                    {
                        float d = V3.Dist2D(e.poly.P(i), ego.pos);
                        if (d >= r0 && d <= spawnMax && Math.Abs(e.poly.y[i] - ego.pos.y) < 40f) { pc.Add(e); break; }
                    }
                }
            }
            if (pc.Count == 0) return;
            int active = 0; foreach (var p in peds) if (p.active && !p.done) active++;
            int budget = initial ? target : 2, tries = 0;
            while (budget > 0 && active < target && tries++ < 60)
            {
                var e = pc[rng.Next(pc.Count)];
                float s = (float)rng.NextDouble() * e.L;
                V3 q = e.poly.At(s); float d = V3.Dist2D(q, ego.pos);
                if (d < r0 || d > spawnMax || Math.Abs(q.y - ego.pos.y) > 40f) continue;
                bool close = false; foreach (var o in e.peds) if (Math.Abs((o.dir > 0 ? o.s : e.L - o.s) - s) < 3f) { close = true; break; }
                if (close) continue;
                var p = new TPed { id = nextId++, model = rng.Next(Math.Max(1, models)), active = true, alpha = initial ? 1f : 0f, fade = initial ? 0 : 1 };
                p.speed = 1.05f + 0.45f * (float)rng.NextDouble();
                Enter(p, e, rng.NextDouble() < 0.5 ? 1 : -1); p.s = p.dir > 0 ? s : e.L - s;
                if (modelWalks != null && !modelWalks[p.model]) { p.state = 2; p.idleT = 1e9f; }
                peds.Add(p); Pose(p); active++; budget--; statSpawn++;
            }
            if (initial) pcClock = 0f;
        }
    }
}
