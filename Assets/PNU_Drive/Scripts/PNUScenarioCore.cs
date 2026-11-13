// 5/6 스토리보드 상황 계산(유니티 PNUDriveScenario·시험 프로그램이 같이 씀) — 자리·순서는 Data/scenario.json(cw/scenario.py)
//   108 교차로 통과   : 신호(V2I)·주변 차량·보행자 확인 → 통과
//   109 차선 변경 보류: 비상등 켠 정차 트럭(바깥 차로) + 안쪽 차로 뒤에서 오는 차 → 틈 모자라면 보류·감속, 지나가면 차로 바꿈
//   110~112 사각지대 보행자: 정차 버스 앞에서 사람이 자차 차로로 → 경고 → 단계 감속 → 정지 → 사람이 물러나면 출발
//   113~115 악천후   : 비(젖은 노면 20% 감속), 카메라 신뢰도 낮음 → 레이더 거리·속도로 보완(앞차 하나)
//   116~117 도착     : 서면 결과(주행 시간·위험 대응·안전 정지 기록)
// 화면·소리는 여기서 만든 값(ScEvent 단계·cues)을 유니티가 읽어서 그림
using System;
using System.Collections.Generic;

namespace PNU.Drive
{
    [Serializable] public class ScPose { public float[] p, f; public float len; }
    [Serializable] public class ScDest { public string name, sub, origin, via; public float km; }
    [Serializable] public class ScJunction { public int j; public float stop, s0, s1; public string mv; }
    [Serializable] public class ScLane { public float sTrig, sHold, sLc0, sLc1, sDone, uA, uB; public int side; public ScPose truck, laneB; }
    [Serializable] public class ScPedPath { public float[] pts; }
    [Serializable] public class ScBlind { public float sBus, sCross, sJ14, uLaneEdge, uBus; public ScPose bus; public ScPedPath ped; }
    [Serializable] public class ScRain { public float s0, full, s1, clear, sVoice, sLead; }
    [Serializable] public class ScArrive { public float s; }
    [Serializable] public class ScenarioData { public int version; public ScDest dest; public ScJunction junction; public ScLane lanechange; public ScBlind blindped; public ScRain rain; public ScArrive arrive; }

    /// <summary>체크리스트 한 줄: st 0 대기, 1 진행 중, 2 완료, 3 경고</summary>
    public class ScStep { public string name, detail = ""; public int st; public ScStep(string n) { name = n; } }

    public class ScEvent
    {
        public string id, title, panel, phase = "", result = "";
        public int state;                                   // 0 대기, 1 진행, 2 끝, 3 건너뜀
        public float s0, s1, t0 = -1f, t1 = -1f, waitT;
        public ScStep[] steps;
        public ScEvent(string id, string title, string panel, params string[] names)
        {
            this.id = id; this.title = title; this.panel = panel;
            steps = new ScStep[names.Length]; for (int i = 0; i < names.Length; i++) steps[i] = new ScStep(names[i]);
        }
        public void ResetSteps() { foreach (var q in steps) { q.st = 0; q.detail = ""; } phase = ""; result = ""; waitT = 0f; t0 = t1 = -1f; }
    }

    /// <summary>자차가 선 기록(결과판)</summary>
    public class ScStopRec { public float t, s, dur; public string why; public int code; }

    public class ScenarioCore
    {
        public ScenarioData D;
        public TrafficCore tr; public PedCore pd; public EgoDriver drv; public EgoState es;
        public Func<float, V3> posAt; public float pathLen;
        public Func<int, int, string, float, int> egoState;
        public int pedModel = 0;
        public ScEvent J, L, B, R, A; public ScEvent[] All;
        public float now, rainW; public int blink;                  // blink: 깜빡이 덮어쓰기(-1 왼쪽, 1 오른쪽, 0 그대로)
        public readonly List<string> cues = new List<string>();      // "J:start" 등 — 화면·소리 신호(유니티가 읽고 비움)
        public readonly List<ScStopRec> stops = new List<ScStopRec>();
        public float driveTime; public bool arrived;
        // 109
        public TCar truck, rear; public TTrack trA, trB; public bool lcHold; public float lcRearGap = 1e9f, lcRearTtc = 1e9f, lcFrontGap = 1e9f, lcMinGap = 1e9f, lcHoldTime, truckDist = -1f, lcMinV = 1e9f;
        float lcSafeT;
        // 110~112
        public TCar bus; public TTrack trBus; public TPed ped; public int pedStage; public float pedDist = -1f, pedLat, pedMinDist = 1e9f, pedStopT, pedDetectDist = -1f;
        float pedTvis = 3f, pedLine = -1f, pedClearT, pedDetT;
        V3[] pedPts; float[] pedV, pedW;
        // 113~115
        public TCar lead; public float radarDist = -1f, radarRel; bool rainVoiced;
        // 정지 기록
        bool stoppedNow; ScStopRec curStop;

        public void Init()
        {
            J = new ScEvent("junction", "교차로 통과 상황", "교차로 통과 상황", "신호 확인", "주변 차량 확인", "보행자 확인", "교차로 통과");
            L = new ScEvent("lanechange", "차선 변경 상황", "감속 및 차선 유지 상황", "전방 차량 정차 감지", "후측방 차량 확인", "안전 거리 판단", "차선 유지");
            B = new ScEvent("blindped", "사각지대 보행자 감지", "사각지대 보행자 감지", "사각지대 보행자 감지", "경고 알림", "차량 감속", "차량 정지 준비");
            R = new ScEvent("rain", "악천후 주행", "악천후 센서 보완", "카메라 인식 저하", "레이더 거리 확인", "레이더 속도 비교", "보완 판단 / 주행 유지");
            A = new ScEvent("arrive", "목적지 도착", "주행 결과");
            All = new[] { J, L, B, R, A };
            if (D != null)
            {
                if (D.junction != null) { J.s0 = D.junction.s0; J.s1 = D.junction.s1; }
                if (D.lanechange != null) { L.s0 = D.lanechange.sTrig; L.s1 = D.lanechange.sDone; }
                if (D.blindped != null) { B.s0 = D.blindped.sBus - 120f; B.s1 = D.blindped.sCross + 12f; }
                if (D.rain != null) { R.s0 = D.rain.full; R.s1 = D.rain.s1; }
                if (D.arrive != null) { A.s0 = D.arrive.s - 1f; A.s1 = D.arrive.s + 1f; }
                if (D.blindped != null && D.blindped.ped != null && D.blindped.ped.pts != null)
                {
                    var a = D.blindped.ped.pts; int n = a.Length / 5;
                    pedPts = new V3[n]; pedV = new float[n]; pedW = new float[n];
                    for (int k = 0; k < n; k++) { pedPts[k] = new V3(a[5 * k], a[5 * k + 1], a[5 * k + 2]); pedV[k] = a[5 * k + 3]; pedW[k] = a[5 * k + 4]; }
                }
            }
        }

        /// <summary>사각지대 사람이 버스 옆으로 보이기 시작할 때까지 걸리는 시간(사람 길 따라)</summary>
        void PedVisibleTime()
        {
            pedTvis = 3f;
            if (pedPts == null || posAt == null || D.blindped == null) return;
            float t = 0f;
            for (int k = 0; k + 1 < pedPts.Length; k++)
            {
                V3 a = pedPts[k], b = pedPts[k + 1]; float L = V3.Dist2D(a, b), v = Math.Max(0.3f, pedV[k + 1]);
                for (float u = 0f; u <= 1f; u += 0.02f)
                {
                    float lat; ProjPath(a + (b - a) * u, D.blindped.sCross, 14f, out lat);
                    if (lat > VisLat) { pedTvis = t + L * u / v; return; }
                }
                t += L / v + pedW[k + 1];
            }
        }
        float VisLat { get { return D.blindped != null ? D.blindped.uBus + 1.25f + 0.0f : -2.0f; } }     // 버스 왼쪽 끝 = 이만큼 넘으면 보임
        const float BusV = 11.1f;                                       // 정차 버스 옆 서행 40km/h

        // ------------------------------------------------------------ 자차 경로 도움
        V3 Fwd(float s) { V3 a = posAt(s - 2.5f), b = posAt(s + 2.5f), d = b - a; float l = d.Len2D; return l > 1e-4f ? new V3(d.x / l, 0f, d.z / l) : new V3(0, 0, 1); }
        /// <summary>점 q 를 자차 경로에 내린 s(sGuess ± range 안)와 옆 거리(+왼쪽)</summary>
        public float ProjPath(V3 q, float sGuess, float range, out float lat)
        {
            float bs = sGuess, bd = 1e18f;
            for (float s = sGuess - range; s <= sGuess + range; s += 0.5f) { float d = V3.Dist2DSq(posAt(s), q); if (d < bd) { bd = d; bs = s; } }
            for (float s = bs - 0.5f; s <= bs + 0.5f; s += 0.05f) { float d = V3.Dist2DSq(posAt(s), q); if (d < bd) { bd = d; bs = s; } }
            V3 p = posAt(bs), f = Fwd(bs); V3 left = new V3(-f.z, 0f, f.x);
            lat = (q.x - p.x) * left.x + (q.z - p.z) * left.z;
            return bs;
        }
        static V3 P3(float[] a) { return a != null && a.Length >= 3 ? new V3(a[0], a[1], a[2]) : new V3(0, 0, 0); }
        static V3 F2(float[] a) { return a != null && a.Length >= 2 ? new V3(a[0], 0f, a[1]) : new V3(0, 0, 1); }

        // 자차 명령 모으기(가장 가까운 줄, 가장 낮은 속도)
        void Haz(float line, float vcap, float dec, string why, int code)
        {
            if (line >= 0f && (drv.hazStop < 0f || line < drv.hazStop)) { drv.hazStop = line; drv.hazWait = code; drv.hazStatus = why; drv.hazDec = Math.Max(drv.hazDec, dec); }
            if (vcap >= 0f && (drv.hazV < 0f || vcap < drv.hazV)) { drv.hazV = vcap; if (drv.hazStop < 0f) { drv.hazStatus = why; drv.hazDec = dec; } }
        }

        void Begin(ScEvent e) { e.state = 1; e.t0 = now; e.ResetSteps(); e.t0 = now; }
        void End(ScEvent e, string result) { e.state = 2; e.t1 = now; e.result = result; }

        // ------------------------------------------------------------ 한 걸음(차·사람 걸음 뒤, 자차 걸음 앞)
        public void Step(float dt, float t)
        {
            now = t;
            if (D == null || es == null || !es.valid || drv == null || posAt == null) return;
            drv.hazStop = -1f; drv.hazV = -1f; drv.hazDec = 1.2f; drv.hazStatus = ""; drv.hazWait = 6; blink = 0;
            StepJunction(dt); StepLane(dt); StepBlind(dt); StepRain(dt); StepArrive();
            if (!arrived) driveTime += dt;
            LogStops(dt);
        }

        /// <summary>건너뛰기(슬라이더·학습 변경): 연출 차·사람 지우고 자리에 맞게 상황을 다시</summary>
        public void Reset(float s)
        {
            truck = rear = bus = lead = null; ped = null; trA = trB = trBus = null;
            if (tr != null) tr.passLane.Clear();
            lcHold = false; lcRearGap = lcRearTtc = lcFrontGap = 1e9f; lcMinGap = 1e9f; lcHoldTime = 0f; truckDist = -1f; lcSafeT = 0f; lcMinV = 1e9f;
            pedStage = 0; pedDist = -1f; pedMinDist = 1e9f; pedStopT = 0f; pedLine = -1f; pedClearT = 0f; pedDetectDist = -1f;
            radarDist = -1f; rainVoiced = false; blink = 0;
            if (tr != null) { tr.spawnBlock = null; tr.extraGate = null; tr.speedScale = 1f; }
            if (drv != null) { drv.hazStop = -1f; drv.hazV = -1f; drv.speedScale = 1f; }
            if (All != null)
                foreach (var e in All)
                {
                    e.ResetSteps();
                    e.state = s < e.s0 ? 0 : 3;
                }
            if (R != null && D != null && D.rain != null && s < D.rain.s1 - 30f) R.state = 0;       // 비는 자리로 정함(구간 안이면 다시 시작)
            if (A != null && D != null && D.arrive != null && s < D.arrive.s - 5f) { A.state = 0; arrived = false; }
            if (s < 5f) { driveTime = 0f; stops.Clear(); if (drv != null) drv.MaxDecel = 0f; }
            stoppedNow = false; curStop = null;
            cues.Add("X:reset");
        }

        // ------------------------------------------------------------ 108 교차로 통과
        int StopIndex(int j) { if (drv.stopJ != null) for (int k = 0; k < drv.stopJ.Length; k++) if (drv.stopJ[k] == j) return k; return -1; }

        /// <summary>신호 남은 초(지금 빛이 바뀔 때까지)</summary>
        public float SigRemain(int j, int g, float t)
        {
            if (tr == null || j < 0 || j >= tr.J.Length || tr.J[j] == null || !tr.J[j].hasPlan) return -1f;
            var jn = tr.J[j]; int a0 = SigMath.Aspect(jn.left0, jn.off, g, t);
            for (float d = 0.25f; d < 150f; d += 0.25f) if (SigMath.Aspect(jn.left0, jn.off, g, t + d) != a0) return d;
            return -1f;
        }

        void StepJunction(float dt)
        {
            var e = J; var d = D.junction; if (e == null || d == null || e.state >= 2) return;
            float s = es.s;
            if (e.state == 0)
            {
                if (s < d.s0) return;
                if (s > d.stop - 25f) { e.state = 3; return; }
                Begin(e); cues.Add("J:start");
            }
            float el = now - e.t0;
            int k = StopIndex(d.j);
            int state = egoState != null ? egoState(d.j, 0, string.IsNullOrEmpty(d.mv) ? "S" : d.mv, now) : 2;
            bool unprot = d.mv == "L" && !(drv.leftProt != null && drv.leftProt(d.j));
            float rem = SigRemain(d.j, 0, now);
            string remT = rem > 0f ? " · " + Math.Ceiling(rem).ToString("0") + "초 남음" : "";
            string sig = state == 2 ? "녹색" + (unprot ? " · 비보호 좌회전" : "") : state == 1 ? "황색" : state == 4 ? "적색 · 우회전 가능" : (d.mv == "L" && !unprot ? "적색 · 좌회전 신호 대기" : "적색 · 정지");
            float front = s + es.len * 0.5f;
            bool passed = front > d.stop + 0.3f, committed = passed || (k >= 0 && drv.TurnOk == k);
            if (es.speed < 0.2f && !passed) e.waitT += dt;
            // 1 신호(V2I)
            e.steps[0].st = el < 1.0f ? 1 : 2; e.steps[0].detail = "V2I " + sig + remT;
            // 2 주변 차량
            int nCar = 0;
            if (tr != null && d.j >= 0 && d.j < tr.J.Length && tr.J[d.j] != null)
            {
                V3 jc = tr.J[d.j].c;
                foreach (var c in tr.cars)
                {
                    if (!c.active || c.done || !c.hasPose || c.parked) continue;
                    if (V3.Dist2DSq(c.pos, jc) > 75f * 75f || c.v < 0.5f) continue;
                    V3 tj = jc - c.pos; if (tj.x * c.fwd.x + tj.z * c.fwd.z < 0f) continue;            // 교차로로 오는 차
                    if (c.fwd.x * es.fwd.x + c.fwd.z * es.fwd.z > 0.7f) continue;                     // 자차와 같은 쪽(앞·뒤 차)
                    nCar++;
                }
            }
            bool yieldNow = drv.wait == 4 || drv.wait == 5;
            e.steps[1].st = el < 1.8f ? 1 : (yieldNow ? 3 : (committed ? 2 : 1));
            e.steps[1].detail = drv.wait == 4 ? "맞은편 차량 지나가기 기다림" : drv.wait == 5 ? "건너편이 막혀 기다림" : (nCar > 0 ? "주변 차량 " + nCar + "대 확인" : "다가오는 차량 없음");
            // 3 보행자
            int nPed = 0;
            if (pd != null) foreach (var p in pd.peds) if (p.active && !p.done && p.e != null && p.e.t == 2 && p.e.j == d.j && p.state == 0) nPed++;
            e.steps[2].st = el < 2.6f ? (el < 1.8f ? 0 : 1) : (committed ? 2 : 1);
            e.steps[2].detail = nPed > 0 ? "횡단 보행자 " + nPed + "명 확인" : "횡단보도 보행자 없음";
            // 4 통과
            bool exiting = s > d.s1 - 6f;
            e.steps[3].st = passed ? (exiting ? 2 : 1) : 0;
            e.steps[3].detail = passed ? (exiting ? "통과 완료" : "통과 중") : (state == 2 ? "안전 조건 확인 중" : "신호 기다림");
            e.phase = !passed ? "교차로 진입 판단중" : (exiting ? "교차로 통과 완료" : "교차로 통과 중");
            if (s >= d.s1)
            {
                End(e, "신호(V2I)·주변 차량·보행자 확인 후 통과" + (e.waitT > 0.8f ? " · 대기 " + e.waitT.ToString("0") + "초" : ""));
                cues.Add("J:done");
            }
        }

        // ------------------------------------------------------------ 109 차선 변경 보류
        void StepLane(float dt)
        {
            var e = L; var d = D.lanechange; if (e == null || d == null) return;
            float s = es.s;
            if (e.state >= 2)
            {
                // 뒤처리: 트럭은 자차가 멀어지고 안 보이면 사라짐, 뒤차는 보통 차로
                if (truck != null && !truck.done && s > d.sLc1 + 150f && !tr.SeenByEgo(es, truck.pos)) { tr.FadeOut(truck); truck = null; tr.passLane.Clear(); }
                if (rear != null) { if (!rear.done) tr.Release(rear); rear = null; }
                return;
            }
            // 준비: 트럭(자차 340m 뒤부터, 아직 안 보일 때)
            if (truck == null && s > d.sTrig - 340f && s < d.sTrig - 20f && tr != null) SpawnTruck();
            if (e.state == 0)
            {
                if (s < d.sTrig) return;
                if (s > d.sHold - 30f || truck == null || truck.done) { e.state = 3; return; }
                Begin(e); cues.Add("L:start"); SpawnRear(); lcSafeT = 0f; lcHold = false;
                if (tr != null) { var B2 = trB; var A2 = trA; float sT = truck.s; tr.spawnBlock = (t, sp) => t == B2 || (t == A2 && sp < sT + 10f); }
            }
            float el = now - e.t0;
            blink = d.side;
            truckDist = truck != null && !truck.done ? (TrafficCore.ProjectS(trA ?? truck.tr, truck.pos) - truck.len * 0.5f) - (trA != null ? TrafficCore.ProjectS(trA, es.pos) : s) - es.len * 0.5f : -1f;
            float rg, ttc, fg; bool safe = LaneGapSafe(out rg, out ttc, out fg);
            lcRearGap = rg; lcRearTtc = ttc; lcFrontGap = fg; if (rg < lcMinGap) lcMinGap = rg;
            bool before = s < d.sHold + 0.5f;
            if (safe) lcSafeT += dt; else lcSafeT = 0f;
            bool hold = before && (!safe || (lcHold && lcSafeT < 0.6f));
            if (hold)
            {
                if (!lcHold) cues.Add("L:hold");
                lcHold = true; lcHoldTime += dt;
                Haz(d.sHold + es.len * 0.5f + 0.5f, -1f, 2.0f, "차선 변경 보류 — 후측방 차량 접근", 7);
                if (es.speed < lcMinV) lcMinV = es.speed;
            }
            else if (lcHold) { lcHold = false; cues.Add("L:go"); }
            bool changing = s >= d.sLc0 && !hold, done = s >= d.sLc1;
            // 1 앞 정차
            e.steps[0].st = el < 0.8f ? 1 : 2;
            e.steps[0].detail = truckDist >= 0f ? "정차 차량(비상등) · 거리 " + truckDist.ToString("0") + "m" : "정차 차량(비상등)";
            // 2 후측방
            e.steps[1].st = el < 1.6f ? 1 : (rg < 1e8f && !safe ? 3 : 2);
            e.steps[1].detail = rg < 1e8f ? (rg < 0f ? "옆 차로 차량 나란히" : (ttc < 1e8f ? "접근 중 · 거리 " + rg.ToString("0.0") + "m" : "뒤 차량 · 거리 " + rg.ToString("0.0") + "m")) : (fg < 1e8f ? "앞 차량 · 거리 " + fg.ToString("0.0") + "m" : "후측방 차량 없음");
            // 3 안전 거리
            e.steps[2].st = el < 2.2f ? (el < 1.6f ? 0 : 1) : (lcHold ? 3 : 2);
            e.steps[2].detail = el < 2.2f ? "판단 중" : (lcHold ? "부족 → 차선 변경 보류" : "확보 → 차선 변경");
            // 4 차선 유지 → 차선 변경
            if (lcHold || !changing) { e.steps[3].name = "차선 유지"; e.steps[3].st = el < 2.2f ? 0 : 1; e.steps[3].detail = lcHold ? "현재 차선에서 감속 · " + (es.speed * 3.6f).ToString("0") + "km/h" : "현재 차선 주행"; }
            else { e.steps[3].name = "차선 변경"; e.steps[3].st = done ? 2 : 1; e.steps[3].detail = done ? "완료" : "왼쪽 차로로 변경 중"; }
            e.phase = lcHold ? "감속 및 차선 유지 판단중" : (changing ? (done ? "차선 변경 완료" : "차선 변경 중") : "후측방 안전 확인");
            if (s >= d.sDone)
            {
                // 뒤차가 옆 차로로 나란히 지나가면 앞뒤 간격이 0 아래로 내려감 → "최소 0.0m" 대신 통과로 적음
                string why = lcMinGap >= 0.5f && lcMinGap < 1e8f ? "후측방 차량 접근(최소 " + lcMinGap.ToString("0.0") + "m)" : "후측방 차량 접근·옆 차로 통과";
                End(e, lcHoldTime > 0.3f ? why + " → 보류·감속 " + lcHoldTime.ToString("0") + "초 후 차로 변경" : "후측방 안전 확인 후 차로 변경");
                cues.Add("L:done"); if (tr != null) tr.spawnBlock = null;
            }
        }

        void SpawnTruck()
        {
            var d = D.lanechange; if (d.truck == null) return;
            float sA; trA = tr.FindTrack(P3(d.truck.p), F2(d.truck.f), 3.5f, out sA);
            if (trA == null) return;
            int m = tr.ModelOfKind("truck");
            truck = tr.SpawnScripted(trA, sA, 0f, m, true, true, true);
            // 이 차로(트럭 앞까지) 보통 차는 비움: 안 보이면 바로, 보이면 서서히(자차가 차로 바꾸는 곳에 줄 서지 않게)
            if (truck != null) foreach (var o in trA.cars.ToArray()) if (!o.scripted && !o.done && o.s < sA) { if (tr.SeenByEgo(es, o.pos)) tr.FadeOut(o); else tr.RemoveNow(o); }
            // 트럭 뒤 차는 안쪽 차로로 비켜 감, 트럭 앞까지 바깥 차로엔 새 차 안 넣음
            if (truck != null && d.laneB != null)
            {
                float sb; trB = tr.FindTrack(P3(d.laneB.p), F2(d.laneB.f), 3.5f, out sb);
                if (trB != null) tr.passLane[trA] = trB;
                var A2 = trA; float sT = sA; tr.spawnBlock = (t, sp) => t == A2 && sp < sT + 10f;
            }
        }

        void SpawnRear()
        {
            var d = D.lanechange; if (d.laneB == null) return;
            float sb; trB = tr.FindTrack(P3(d.laneB.p), F2(d.laneB.f), 3.5f, out sb);
            if (trB == null) return;
            float sE = TrafficCore.ProjectS(trB, es.pos), sR = sE - 34f;
            if (sR < 3f) return;
            int m = tr.ModelOfKind("suv"); if (m < 0) m = 0;
            float v = Math.Min(es.speed + 4.5f, trB.lim * 1.25f);
            rear = tr.SpawnScripted(trB, sR, v, m, false, false, true);
            if (rear != null) rear.persona = 1.22f;
        }

        /// <summary>안쪽 차로(B) 틈: 뒤에서 오는 차는 차로 바꾸기 시작할 때 틈·충돌 시간, 앞차는 틈</summary>
        bool LaneGapSafe(out float rearGap, out float ttc, out float frontGap)
        {
            rearGap = 1e9f; ttc = 1e9f; frontGap = 1e9f;
            if (trB == null) return true;
            float sE = TrafficCore.ProjectS(trB, es.pos);
            float tLc = Math.Min(6f, Math.Max(0f, D.lanechange.sLc0 - es.s) / Math.Max(1.5f, es.speed));
            bool safe = true;
            foreach (var c in trB.cars)
            {
                if (c.done || !c.active) continue;
                float dd = c.s - sE, half = (c.len + es.len) * 0.5f;
                if (dd < 0f)
                {
                    float g = -dd - half, cl = c.v - es.speed, gp = g - Math.Max(0f, cl) * tLc;
                    if (g < rearGap) { rearGap = g; ttc = cl > 0.1f ? g / cl : 1e9f; }
                    if (-dd > 60f) continue;
                    if (g < 10f || gp < 10f || (cl > 0.1f && gp / cl < 3.0f)) safe = false;
                }
                else
                {
                    float g = dd - half; if (g < frontGap) frontGap = g;
                    if (g < 4f + 0.4f * es.speed && c.v < es.speed + 1f) safe = false;
                }
            }
            return safe;
        }

        // ------------------------------------------------------------ 110~112 사각지대 보행자
        void StepBlind(float dt)
        {
            var e = B; var d = D.blindped; if (e == null || d == null || pedPts == null) return;
            float s = es.s;
            if (e.state >= 2)
            {
                if (bus != null && !bus.done && s > d.sBus + 18f) { tr.Release(bus); bus = null; }
                if (ped != null && !ped.done && ped.spEnd && !tr.SeenByEgo(es, ped.pos)) { pd.FadeOut(ped); ped = null; }
                if (tr != null && e.state == 2 && tr.extraGate != null && (ped == null || ped.spEnd)) tr.extraGate = null;
                return;
            }
            if (bus == null && s > d.sBus - 340f && s < d.sBus - 70f && tr != null) SpawnBus();
            bool busOk = bus != null && !bus.done;
            if (e.state == 0)
            {
                if (busOk && s > d.sBus - 150f && s < d.sCross) Haz(-1f, BusV, 1.2f, "정차 버스 옆 서행", 0);
                if (s > d.sCross - 10f || (s > d.sBus - 120f && !busOk)) { e.state = 3; return; }
                if (!busOk) return;
                // 사람이 버스 옆으로 보일 때 자차가 편히(2.6m/s²) 설 수 있는 거리 + 0.4초 반응이 되도록 출발(서행 속도에 맞춰서만)
                float dist = d.sCross - s, v = Math.Max(es.speed, 4f);
                float need = v * (pedTvis + 0.4f) + v * v / (2f * 2.6f) + es.len * 0.5f + 4f;
                if (dist <= need && es.speed <= BusV + 0.5f && drv.leadGap > dist + 2f)
                {
                    if (dist < v * 1.0f + v * v / (2f * 4.5f) + es.len * 0.5f + 3f) { e.state = 3; return; }     // 너무 늦음(건너뛰고 들어온 경우)
                    Begin(e); pedStage = 0; pedLine = -1f; pedClearT = 0f; pedStopT = 0f;
                    ped = pd.SpawnScripted(pedPts, pedV, pedW, pedModel, true);
                    if (ped == null) { e.state = 3; return; }
                    if (tr != null) { var P0 = ped; tr.extraGate = c => PedGate(c, P0); }
                    cues.Add("B:walk");
                }
                return;
            }
            if (ped == null || ped.done) { End(e, "사람이 사라짐"); return; }
            float lat, sp = ProjPath(ped.pos, d.sCross, 14f, out lat);
            pedLat = lat; float front = s + es.len * 0.5f; pedDist = sp - front;
            V3 fp = posAt(Math.Min(pathLen, front));
            float dEgo = V3.Dist2D(fp, ped.pos); if (pedStage >= 1 && lat > d.uLaneEdge - 0.6f && dEgo < pedMinDist) pedMinDist = dEgo;
            bool nearLane = lat > d.uLaneEdge - 0.9f;
            bool goingBack = ped.spI >= 2 && ped.spWait <= 0f;
            float edgeIn = d.uLaneEdge;
            switch (pedStage)
            {
                case 0:                                       // 버스에 가려 안 보임
                    Haz(-1f, BusV, 1.2f, "정차 버스 옆 서행", 0);
                    if (lat > VisLat) { pedStage = 1; pedDetT = now; pedDetectDist = pedDist; cues.Add("B:detect"); }
                    break;
                case 1:                                       // 감지·경고: 단계 감속
                    Haz(-1f, Math.Max(0f, Math.Min(es.speed, 6f)), 2.4f, "사각지대 보행자 — 감속", 6);
                    if (lat > edgeIn || now - pedDetT > 0.8f) { pedStage = 2; pedLine = Math.Max(sp - 3.7f, front + 0.6f); cues.Add("B:stop"); }
                    break;
                case 2:                                       // 위험 지속: 정지
                case 3:
                    Haz(pedLine, -1f, 3.0f, "보행자 감지 — 자동 정지", 6);
                    if (es.speed < 0.2f) { if (pedStage == 2) { pedStage = 3; cues.Add("B:halt"); } pedStopT += dt; }
                    if (goingBack && !nearLane) { pedClearT += dt; if (pedClearT > 0.9f) { pedStage = 4; cues.Add("B:go"); } } else pedClearT = 0f;
                    break;
                case 4:                                       // 사람이 물러남: 버스 옆 서행으로 지나감
                    Haz(-1f, 8f, 1.2f, "정차 버스 옆 서행", 0);
                    break;
            }
            // 사람은 자차가 다 설 때까지(1.2초) 차로 안에서 멈칫(최대 감지 7초 뒤까지) — 그다음 물러남
            if (pedStage >= 1 && pedStage <= 3 && ped.spI == 2 && ped.spWait > 0f && pedStopT < 1.2f && now - pedDetT < 7f) ped.spWait = Math.Max(ped.spWait, 0.25f);
            // 체크리스트
            float el = pedStage >= 1 ? now - pedDetT : 0f;
            e.steps[0].st = pedStage >= 1 ? 2 : 1; e.steps[0].detail = pedStage >= 1 ? "버스 앞 · 거리 " + Math.Max(0f, pedDist).ToString("0.0") + "m" : "정차 버스 주변 확인";
            e.steps[1].st = pedStage >= 1 ? (el < 0.5f ? 1 : 2) : 0; e.steps[1].detail = pedStage >= 1 ? "경고 표시 · 경고음" : "";
            e.steps[2].st = pedStage == 0 ? 0 : (pedStage >= 3 || es.speed < 1f ? 2 : 1);
            e.steps[2].detail = pedStage == 0 ? "" : (es.speed * 3.6f).ToString("0") + "km/h";
            e.steps[3].name = pedStage >= 3 ? "차량 정지" : "차량 정지 준비";
            e.steps[3].st = pedStage < 2 ? 0 : (pedStage == 2 ? 1 : 2);
            e.steps[3].detail = pedStage < 2 ? "" : pedStage == 2 ? "위험 지속 → 정지" : pedStage == 3 ? "정지 · 보행자와 " + Math.Max(0f, pedMinDist).ToString("0.0") + "m" : "보행자 물러남 → 출발";
            e.phase = pedStage == 0 ? "" : pedStage == 1 ? "경고 · 단계적 감속" : pedStage == 2 ? "위험 지속 · 자동 정지" : pedStage == 3 ? "정지 · 보행자 확인" : "안전 확인 · 서행 출발";
            if (pedStage == 4 && s > d.sCross + 12f)
            {
                End(e, "버스 앞 보행자 감지 → 경고·감속 → 정지(보행자와 " + Math.Max(0f, pedMinDist).ToString("0.0") + "m, " + pedStopT.ToString("0") + "초)");
                cues.Add("B:done");
            }
        }

        void SpawnBus()
        {
            var d = D.blindped; if (d.bus == null) return;
            float sb; trBus = tr.FindTrack(P3(d.bus.p), F2(d.bus.f), 3.5f, out sb);
            if (trBus == null) return;
            int m = tr.ModelOfKind("bus");
            bus = tr.SpawnScripted(trBus, sb, 0f, m, true, false, true);
        }

        /// <summary>보통 차: 연출 사람이 앞길(차 폭 + 0.8m) 위에 있으면 그 앞에서 섬</summary>
        float PedGate(TCar c, TPed p)
        {
            if (p == null || p.done || !p.active || !c.hasPose) return 1e9f;
            if (V3.Dist2DSq(c.pos, p.pos) > 70f * 70f || Math.Abs(c.pos.y - p.pos.y) > 3f) return 1e9f;
            float W = c.wid * 0.5f + 0.8f, W2 = W * W;
            float look = Math.Min(60f, c.v * 4f + 15f);
            for (float dd = 0f; dd <= look; dd += 1f)
            {
                V3 q = tr.PointAlong(c, dd);
                if (V3.Dist2DSq(q, p.pos) < W2)
                {
                    if (dd < c.len * 0.5f) return 1e9f;          // 이미 옆(지나는 중)
                    return Math.Max(0f, dd - c.len * 0.5f - 1.5f) + 1.9f;
                }
            }
            return 1e9f;
        }

        // ------------------------------------------------------------ 113~115 악천후
        void StepRain(float dt)
        {
            var d = D.rain; var e = R; if (d == null || e == null) return;
            float s = es.s;
            float w = s <= d.s0 ? 0f : s < d.full ? (s - d.s0) / Math.Max(1f, d.full - d.s0) : s <= d.s1 ? 1f : s < d.clear ? 1f - (s - d.s1) / Math.Max(1f, d.clear - d.s1) : 0f;
            rainW = w * w * (3f - 2f * w);
            drv.speedScale = 1f - 0.2f * rainW; if (tr != null) tr.speedScale = 1f - 0.15f * rainW;
            if (lead == null && e.state == 0 && s >= d.sLead && s < d.full - 150f && tr != null) SpawnLead();
            if (e.state >= 2) { if (lead != null) { if (!lead.done) tr.Release(lead); lead = null; } return; }
            if (e.state == 0)
            {
                if (s >= d.full - 3f && s < d.s1) { Begin(e); cues.Add("R:start"); rainVoiced = false; }
                else return;
            }
            float el = now - e.t0;
            bool have = drv.leadGap < 150f;
            radarDist = have ? drv.leadGap : -1f; radarRel = have ? (drv.leadSpeed - es.speed) * 3.6f : 0f;
            if (!rainVoiced && s >= d.sVoice) { rainVoiced = true; cues.Add("R:voice"); }
            e.steps[0].st = el < 1.5f ? 1 : 2; e.steps[0].detail = "신뢰도 낮음 · 시야 저하";
            e.steps[1].st = el < 1.5f ? 0 : el < 3.5f ? 1 : 2; e.steps[1].detail = el < 1.5f ? "" : have ? "전방 차량 " + radarDist.ToString("0.0") + "m" : "전방 차량 없음";
            e.steps[2].st = el < 3.5f ? 0 : el < 6.5f ? 1 : 2; e.steps[2].detail = el < 3.5f ? "" : have ? "상대 속도 " + (radarRel >= 0f ? "+" : "") + radarRel.ToString("0") + "km/h" : "비교할 차량 없음";
            e.steps[3].st = el < 6.5f ? 0 : el < 9f ? 1 : 2; e.steps[3].detail = el < 6.5f ? "" : "감속 운행 · 제한속도의 " + Math.Round(drv.speedScale * 100f).ToString("0") + "%";
            e.phase = el < 9f ? "센서 보완 판단중" : "레이더 보완 · 주의 주행";
            if (s >= d.s1)
            {
                End(e, "카메라 인식 저하 → 레이더 거리·속도로 보완, 젖은 노면 감속 운행");
                cues.Add("R:end");
            }
        }

        void SpawnLead()
        {
            float s = es.s + 85f; if (s > pathLen - 10f) return;
            V3 p = posAt(s); float st; var t = tr.FindTrack(p, Fwd(s), 2.2f, out st);
            if (t == null) return;
            int m = tr.ModelOfKind("car"); if (m < 0) m = 0;
            lead = tr.SpawnScripted(t, st, Math.Max(4f, es.speed * 0.85f), m, false, false, true);
            if (lead != null) lead.persona = 0.8f;
        }

        // ------------------------------------------------------------ 116~117 도착
        void StepArrive()
        {
            var e = A; if (e == null || D.arrive == null || e.state >= 2) return;
            if (es.s >= D.arrive.s - 1.5f && es.speed < 0.3f)
            {
                arrived = true; e.state = 2; e.t1 = now; e.result = "도착";
                if (curStop != null) { curStop = null; stoppedNow = false; }
                cues.Add("A:arrive");
            }
        }

        // ------------------------------------------------------------ 안전 정지 기록
        void LogStops(float dt)
        {
            if (arrived) return;
            bool st = es.speed < 0.2f;
            if (st && !stoppedNow && es.s > 3f && es.s < pathLen - 3f)
            {
                stoppedNow = true;
                var last = stops.Count > 0 ? stops[stops.Count - 1] : null;
                if (last != null && driveTime - (last.t + last.dur) < 4f && Math.Abs(es.s - last.s) < 8f) curStop = last;     // 줄에서 조금씩 움직인 것은 한 번으로
                else { curStop = new ScStopRec { t = driveTime, s = es.s, why = string.IsNullOrEmpty(drv.status) ? "정지" : drv.status, code = drv.wait }; stops.Add(curStop); }
            }
            else if (!st && stoppedNow) { stoppedNow = false; curStop = null; }
            if (curStop != null) { if (stoppedNow) curStop.dur = driveTime - curStop.t; if (curStop.code == 0 && drv.wait != 0) { curStop.code = drv.wait; curStop.why = drv.status; } }
        }
    }
}
