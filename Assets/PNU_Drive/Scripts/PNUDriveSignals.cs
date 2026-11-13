// 신호 교차로(v11): 교차로마다 계획(시차 off, 주 축 보호 좌회전 left0)
//   0 무리(주 축) 초록(직진·우회전) → 노랑 → [보호 좌회전이면] 빨강+좌회전 화살표 → 노랑 → 모두 빨강 → 1 무리 초록 → 노랑 → 모두 빨강
//   보호 좌회전이 없으면(1차로) 좌회전은 비보호: 초록에 좌회전
// 보행: 횡단보도가 가로지르는 차 무리가 빨강이고 다른 무리가 직진 초록일 때 걷기, 끝 6초 깜빡임·남은 초
// 상수·규칙은 파이썬 cw/greenwave.py 와 같음(녹색 물결을 거기서 계산)
using System;
using System.Collections.Generic;
using UnityEngine;

namespace PNU.Drive
{
    public class PNUDriveSignals : MonoBehaviour
    {
        [Serializable] public class Plan { public int j; public float off; public bool left0; }
        public List<Plan> plans = new List<Plan>();
        public List<PNUDriveLamp> lamps = new List<PNUDriveLamp>();
        public const float G0 = 26f, L0 = 20f, G1 = 18f, Y = 3f, AR = 2f;
        readonly Dictionary<int, Plan> map = new Dictionary<int, Plan>();
        public static PNUDriveSignals Instance;
        [NonSerialized] public float simTime = -1f;      // 자차가 넣어 줌(배속 맞춤). 없으면 Time.time
        public float Now { get { return simTime >= 0 ? simTime : Time.time; } }

        void OnEnable() { Instance = this; Index(); }
        void Index() { map.Clear(); foreach (var p in plans) if (p != null) map[p.j] = p; }
        Plan Get(int j) { Plan p; if (map.Count != plans.Count) Index(); return map.TryGetValue(j, out p) ? p : null; }

        public static float CycleOf(bool left0) { return G0 + Y + (left0 ? L0 + Y : 0f) + AR + G1 + Y + AR; }
        public bool LeftProtected(int j) { var p = Get(j); return p != null && p.left0; }

        float U(Plan p, float t) { float c = CycleOf(p.left0); return ((t + p.off) % c + c) % c; }

        /// <summary>차 신호: 0 빨강, 1 노랑, 2 초록, 3 빨강+좌회전 화살표</summary>
        public int Aspect(int j, int group, float t)
        {
            var p = Get(j); if (p == null) return 2;
            float u = U(p, t);
            float a0 = G0, a1 = a0 + Y, a2 = p.left0 ? a1 + L0 : a1, a3 = p.left0 ? a2 + Y : a1, a4 = a3 + AR, a5 = a4 + G1, a6 = a5 + Y;
            if (group == 0) { if (u < a0) return 2; if (u < a1) return 1; if (u < a2) return 3; if (u < a3) return 1; return 0; }
            if (u < a4) return 0; if (u < a5) return 2; if (u < a6) return 1; return 0;
        }

        /// <summary>예전 이름(0 빨강 1 노랑 2 초록) — 화살표는 빨강으로</summary>
        public int State(int junction, int group, float t) { int a = Aspect(junction, group, t); return a == 3 ? 0 : a; }

        /// <summary>자차 판단: mv = S 직진, L 좌회전, R 우회전 → 0 서기, 1 노랑(설 수 있으면 서기), 2 가기, 4 빨강 우회전(일시정지 뒤 가기)</summary>
        public int EgoState(int j, int group, string mv, float t)
        {
            var p = Get(j); int a = Aspect(j, group, t);
            if (mv == "L" && p != null && p.left0 && group == 0)
                return a == 3 ? 2 : ((a == 1 && U(p, t) >= G0 + Y) ? 1 : 0);   // 화살표에만, 화살표 뒤 노랑은 설 수 있으면 섬
            if (mv == "R") return a == 2 ? 2 : (a == 1 ? 1 : 4);
            return a == 2 ? 2 : (a == 1 ? 1 : 0);                             // 직진·비보호 좌회전
        }

        /// <summary>보행: 횡단보도가 가로지르는 차 무리 g → (걷기, 남은 초, 남은 비율)</summary>
        public bool Walk(int j, int g, float t, out float remain, out float frac)
        {
            remain = 0; frac = 0;
            var p = Get(j); if (p == null) return false;
            float u = U(p, t);
            float a3 = G0 + Y + (p.left0 ? L0 + Y : 0f), a4 = a3 + AR, a5 = a4 + G1;
            float w0, w1;
            if (g == 1) { w0 = 0f; w1 = G0 - 2f; }           // 0 무리 직진 초록 동안 1 무리 팔 횡단보도
            else { w0 = a4; w1 = a5 - 2f; }
            if (u >= w0 && u < w1) { remain = w1 - u; frac = remain / Mathf.Max(0.1f, w1 - w0); return true; }
            return false;
        }

        void Update()
        {
            float t = Now;
            foreach (var L in lamps)
            {
                if (L == null) continue;
                if (!L.pedestrian) L.SetCar(Aspect(L.junction, L.group, t));
                if (L.pedestrian || L.hasPed) { float rem, frac; bool w = Walk(L.junction, L.pedGroup, t, out rem, out frac); L.SetPed(w, rem, frac, t); }
            }
        }
    }
}
