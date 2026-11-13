// 4/6 NPC 차·사람(유니티 쪽): PNUTrafficCore 계산을 자차(PNUDriveEgo)와 같은 시각·같은 작은 걸음으로 돌리고,
//   미리 만든 차·사람(풀)을 꺼내 보여 줌 — 순간이동 없음(멀리서 서서히 나타나고 멀어지면 서서히 사라짐), 건너뛰면 새 자리 둘레에 바로 다시
// 자차는 이 계산의 앞차(따라가기)·비보호 좌회전(맞은편 차)·빨강 우회전(오는 차)·교차로 건너편 막힘을 봄(EgoDriver)
using System;
using System.Collections.Generic;
using UnityEngine;

namespace PNU.Drive
{
    public class PNUDriveTraffic : MonoBehaviour
    {
        public static PNUDriveTraffic Instance;
        public TextAsset lanesJson;                      // Data/lanes.json(차로 토막·교차로 이음·사람 길)
        public PNUDriveEgo ego;
        [Header("차")]
        public bool carsOn = true;
        [Range(0, 60)] public int carCount = 30;
        public PNUDriveVehicle[] carPrefabs;
        public Material[] paintMats, paintFadeMats;      // 차 색(팔레트)마다 불투명·투명
        public float[] paintWeights;
        public Material brakeMat, blinkMat;
        [Header("사람")]
        public bool peopleOn = true;
        [Range(0, 60)] public int peopleCount = 26;
        public PNUDrivePerson[] personPrefabs;
        public int seed = 12345;

        public TrafficCore Core { get; private set; }
        public PedCore Peds { get; private set; }
        public readonly EgoState Es = new EgoState();
        [NonSerialized] public float leadGap = 1e9f, leadSpeed;

        class CarView { public PNUDriveVehicle v; public Vector3 p; public float yaw; }
        readonly Dictionary<TCar, CarView> carViews = new Dictionary<TCar, CarView>();
        readonly Dictionary<TPed, PNUDrivePerson> pedViews = new Dictionary<TPed, PNUDrivePerson>();
        Stack<PNUDriveVehicle>[] carPool; Stack<PNUDrivePerson>[] pedPool; Transform poolRoot;
        bool loaded, initial = true; float blinkT;
        Func<float, V3> posAtFn; PNUDriveEgo posAtOwner;

        void OnEnable() { Instance = this; }
        void OnDisable() { if (Instance == this) Instance = null; }
        public bool Ready { get { return Core != null; } }
        /// <summary>5/6 연출 사람(버스 앞 보행자): 걷는 동작이 있는 모델(이름에 nathan·walk) 번호</summary>
        public int ScriptedPedModel
        {
            get
            {
                if (personPrefabs == null) return 0;
                for (int i = 0; i < personPrefabs.Length; i++) if (personPrefabs[i] != null) { var nm = personPrefabs[i].name.ToLowerInvariant(); if (nm.Contains("nathan") || nm.Contains("walk")) return i; }
                return 0;
            }
        }
        public int ActiveCars { get { return carViews.Count; } }
        public int ActivePeople { get { return pedViews.Count; } }

        public void EnsureLoaded() { if (!loaded) Load(); }

        void Load()
        {
            loaded = true;
            if (lanesJson == null) { Debug.LogWarning("[PNU Drive] 차·사람: lanes.json 없음"); return; }
            LanesData d;
            try { d = JsonUtility.FromJson<LanesData>(lanesJson.text); }
            catch (Exception e) { Debug.LogWarning("[PNU Drive] 차·사람: lanes.json 읽기 실패 " + e.Message); return; }
            if (d == null || d.tracks == null || d.junctions == null) return;
            Core = new TrafficCore(); Core.Load(d); Core.Seed(seed);
            int n = carPrefabs != null ? carPrefabs.Length : 0;
            Core.modelPick = new int[Math.Max(1, n)]; Core.modelLen = new float[Math.Max(1, n)]; Core.modelWid = new float[Math.Max(1, n)]; Core.modelWb = new float[Math.Max(1, n)]; Core.modelBig = new bool[Math.Max(1, n)];
            Core.modelKind = new string[Math.Max(1, n)];
            for (int k = 0; k < n; k++)
            {
                var v = carPrefabs[k]; if (v == null) continue;
                Core.modelPick[k] = Mathf.RoundToInt(v.weight * 100f); Core.modelLen[k] = v.length; Core.modelWid[k] = v.width; Core.modelWb[k] = v.wheelbase;
                Core.modelBig[k] = v.kind == "bus" || v.kind == "truck"; Core.modelKind[k] = v.kind;
            }
            if (n == 0) { Core.modelPick[0] = 100; Core.modelLen[0] = 4.5f; Core.modelWid[0] = 1.9f; Core.modelWb[0] = 2.7f; }
            Core.target = carsOn && n > 0 ? carCount : 0;
            Core.paints = paintMats != null && paintMats.Length > 0 ? paintMats.Length : 1;
            Core.paintW = paintWeights != null && paintWeights.Length == Core.paints ? paintWeights : null;
            var sig = PNUDriveSignals.Instance != null ? PNUDriveSignals.Instance : FindObjectOfType<PNUDriveSignals>();
            if (sig != null) foreach (var p in sig.plans) if (p != null) Core.SetPlan(p.j, p.left0, p.off);
            int m = personPrefabs != null ? personPrefabs.Length : 0;
            Peds = new PedCore(d, Core.J);
            Peds.models = Math.Max(1, m); Peds.modelWalks = new bool[Peds.models]; for (int i = 0; i < Peds.models; i++) Peds.modelWalks[i] = true;
            Peds.rng = new System.Random(seed + 1); Peds.target = peopleOn && m > 0 ? peopleCount : 0;
            Core.crosswalkBusy = Peds.CrosswalkBusy;
            poolRoot = new GameObject("NPC 차·사람").transform; poolRoot.SetParent(transform, false);
            carPool = new Stack<PNUDriveVehicle>[Math.Max(1, n)]; for (int i = 0; i < carPool.Length; i++) carPool[i] = new Stack<PNUDriveVehicle>();
            pedPool = new Stack<PNUDrivePerson>[Math.Max(1, m)]; for (int i = 0; i < pedPool.Length; i++) pedPool[i] = new Stack<PNUDrivePerson>();
            Debug.Log("[PNU Drive] 차·사람 준비: 차로 토막 " + Core.T.Length + ", 교차로 " + Core.J.Length + ", 차 모델 " + n + " × 색 " + Core.paints + ", 사람 모델 " + m + ", 사람 길 " + Peds.E.Length);
        }

        static V3 V(Vector3 p) { return new V3(p.x, p.y, p.z); }
        static Vector3 U(V3 p) { return new Vector3(p.x, p.y, p.z); }

        /// <summary>자차가 작은 걸음마다 부름: 자차 상태 → 차·사람 한 걸음 → 자차 앞차까지 틈</summary>
        public void Tick(PNUDriveEgo e, EgoDriver drv, float h, float t)
        {
            EnsureLoaded(); if (Core == null || e == null || drv == null) return;
            SetEgo(e, drv);
            if (initial) { Core.Spawn(0f, Es, true); Peds.Spawn(0f, Es, true); initial = false; }
            Core.Step(h, t, Es); Peds.Step(h, t, Es);
            leadGap = Core.EgoLeadGap(Es, out leadSpeed);
            blinkT += h;
        }

        void SetEgo(PNUDriveEgo e, EgoDriver drv)
        {
            if (posAtOwner != e || posAtFn == null) { posAtOwner = e; posAtFn = s => V(e.PosAt(s)); }
            Es.valid = true; Es.posAt = posAtFn; Es.pathLen = e.Length; Es.len = 4.7f;
            Es.s = drv.s; Es.speed = drv.v; Es.pos = V(e.PosAt(drv.s));
            Vector3 a = e.PosAt(drv.s - 2.5f), b = e.PosAt(drv.s + 2.5f), f = b - a; f.y = 0f;
            Es.fwd = V(f.sqrMagnitude > 1e-6f ? f.normalized : Vector3.forward);
            Es.waitJ = drv.waitJ; Es.yieldJ = drv.yieldJ; Es.holdDist = drv.wait > 0 ? drv.waitDist : -1f;
        }

        /// <summary>자차가 건너뛰면(슬라이더·처음으로·←/→): 다 지우고 새 자리 둘레에 바로 다시</summary>
        public void OnJump()
        {
            if (Core == null) return;
            Core.Clear(); Peds.Clear(); initial = true; leadGap = 1e9f; leadSpeed = 0f;
            ReleaseRemoved();
        }

        PNUDriveVehicle TakeCar(int model)
        {
            if (carPrefabs == null || model < 0 || model >= carPrefabs.Length || carPrefabs[model] == null) return null;
            var st = carPool[model];
            PNUDriveVehicle v = st.Count > 0 ? st.Pop() : Instantiate(carPrefabs[model], poolRoot);
            v.gameObject.SetActive(true); v.Init();
            return v;
        }

        PNUDrivePerson TakePerson(int model)
        {
            if (personPrefabs == null || personPrefabs.Length == 0) return null;
            model = Mathf.Clamp(model, 0, personPrefabs.Length - 1);
            if (personPrefabs[model] == null) return null;
            var st = pedPool[model];
            PNUDrivePerson v = st.Count > 0 ? st.Pop() : Instantiate(personPrefabs[model], poolRoot);
            v.gameObject.SetActive(true);
            return v;
        }

        void ReleaseRemoved()
        {
            if (Core != null)
            {
                foreach (var c in Core.removed)
                {
                    CarView w; if (!carViews.TryGetValue(c, out w)) continue;
                    carViews.Remove(c); w.v.gameObject.SetActive(false);
                    if (c.model >= 0 && c.model < carPool.Length) carPool[c.model].Push(w.v); else Destroy(w.v.gameObject);
                }
                Core.removed.Clear();
            }
            if (Peds != null)
            {
                foreach (var p in Peds.removed)
                {
                    PNUDrivePerson v; if (!pedViews.TryGetValue(p, out v)) continue;
                    pedViews.Remove(p); v.gameObject.SetActive(false);
                    int m = Mathf.Clamp(p.model, 0, pedPool.Length - 1); pedPool[m].Push(v);
                }
                Peds.removed.Clear();
            }
        }

        void LateUpdate()
        {
            if (Core == null) return;
            ReleaseRemoved();
            bool blinkOn = Mathf.Repeat(blinkT, 0.7f) < 0.36f;
            float rate = ego != null && ego.playing ? ego.rate : 0f;
            bool paint = paintMats != null && paintMats.Length > 0;
            foreach (var c in Core.cars)
            {
                if (!c.active || c.done || !c.hasPose) continue;
                CarView w;
                Vector3 p = U(c.pos), f = U(c.fwd);
                if (!carViews.TryGetValue(c, out w))
                {
                    var v = TakeCar(c.model); if (v == null) continue;
                    w = new CarView { v = v, p = p, yaw = Mathf.Atan2(f.x, f.z) }; carViews[c] = w;
                }
                if (f.sqrMagnitude < 1e-6f) f = w.v.transform.forward;
                w.v.transform.SetPositionAndRotation(p, Quaternion.LookRotation(f, Vector3.up));
                if (paint)
                {
                    int pk = w.v.fixedPaint ? 0 : Mathf.Clamp(c.paint, 0, paintMats.Length - 1);
                    w.v.SetLook(paintMats[pk], paintFadeMats != null && pk < paintFadeMats.Length ? paintFadeMats[pk] : null, c.alpha);
                }
                w.v.SetLights(c.brake, c.blinkSide, blinkOn, c.alpha > 0.99f, brakeMat, blinkMat);
                // 바퀴: 지난 프레임부터 간 거리, 앞바퀴는 방향 바뀜/거리(곡률)로 꺾음
                float ds = new Vector2(p.x - w.p.x, p.z - w.p.z).magnitude;
                float yaw = Mathf.Atan2(f.x, f.z), dy = Mathf.DeltaAngle(w.yaw * Mathf.Rad2Deg, yaw * Mathf.Rad2Deg);
                float steer = ds > 0.02f ? Mathf.Clamp(Mathf.Atan(w.v.wheelbase * (dy * Mathf.Deg2Rad) / ds) * Mathf.Rad2Deg, -35f, 35f) : 0f;
                if (ds > 5f) { ds = 0f; steer = 0f; }                   // 처음·건너뛰기
                w.v.Roll(ds, steer); w.p = p; w.yaw = yaw;
            }
            if (Peds == null) return;
            foreach (var q in Peds.peds)
            {
                if (!q.active || q.done) continue;
                PNUDrivePerson v;
                if (!pedViews.TryGetValue(q, out v)) { v = TakePerson(q.model); if (v == null) continue; pedViews[q] = v; }
                var fw = new Vector3(q.fwd.x, 0f, q.fwd.z);
                v.transform.SetPositionAndRotation(U(q.pos), fw.sqrMagnitude > 1e-6f ? Quaternion.LookRotation(fw, Vector3.up) : v.transform.rotation);
                v.SetMotion(q.state == 0 ? q.v : 0f, rate);
                v.SetAlpha(q.alpha);
            }
        }
    }
}
