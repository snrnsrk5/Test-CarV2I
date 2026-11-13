// 부산대 → 광안대교 → 부경대 주행 재생 (런타임)
// - Data/pnu_drive.json, Data/npc_tracks.json 을 읽어 자차·카메라·신호·NPC·가로수/가로등을 재생
// - 메뉴 "PNU Map/Build Drive Scene (v3)" 가 씬을 만들고 이 컴포넌트를 붙여 줌
// - 운전석 보기: 카메라(또는 VR 리그 xrOrigin)를 자차 운전자 눈 자리(driverEye/cameraRig)에 붙임
//   VR: 처음 들어갈 때와 R 키에서 머리 위치를 눈 자리에 맞춤(Recenter) / PC: 오른쪽 마우스 드래그로 둘러보기, R = 정면
//   키: Space 재생·일시정지, C 따라가기↔운전석, R 정면 맞추기
using System;
using System.Collections.Generic;
using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace PNU
{
    /// <summary>주행 재생: 자차·카메라·하이퍼랩스(빠르게 보기)·NPC·신호</summary>
    public class PNUDrivePlayer : MonoBehaviour
    {
        public TextAsset driveJson, npcJson;
        public Transform ego; public Camera cam;
        public GameObject[] npcPrefabs = new GameObject[3];   // 0 승용, 1 버스, 2 트럭(비우면 상자)
        public bool playing = false; public float rate = 1f; public bool autoSkip = true;
        public enum CamMode { Follow, Driver, Free }
        public CamMode camMode = CamMode.Follow;
        [Header("운전석(테슬라)")]
        public Transform driverEye;          // 자차 아래 운전자 눈 자리
        public Transform cameraRig;          // driverEye 아래. VR 머리 위치 맞춤용
        public Transform xrOrigin;           // VR 리그(XR Origin 등)가 따로 있으면 지정: 카메라 대신 이것을 운전석에 붙임
        public bool showEgoInDriver = true;  // 운전석 보기에서도 차(실내) 보이기
        public bool driverOnXR = true;       // VR 기기가 켜져 있으면 운전석 보기로 시작
        float lookYaw, lookPitch, recenterAt = -1f; bool seated; Transform camHome; bool camHomeSet;
        public float t;
        DriveData D; NpcData N;
        float[] ES, EX, EY, EZ, ET, ETn;
        float hyperCur = 1f; string[] hstate;
        PNUSignalLight[] lights; float nextSig;
        float camYaw, camPitch, camH; bool camJump = true;
        class Npc { public NpcD d; public float[] cum; public float t1; public Vector3[] p; }
        List<Npc> npcs = new List<Npc>();
        List<GameObject>[] pool = { new List<GameObject>(), new List<GameObject>(), new List<GameObject>() };
        static readonly Vector3[] SIZE = { new Vector3(1.85f, 1.45f, 4.6f), new Vector3(2.5f, 3.1f, 11f), new Vector3(2.4f, 2.9f, 8f) };
        public float S { get; private set; }
        public float Speed { get; private set; }
        public float RouteLength { get { return ES != null && ES.Length > 0 ? ES[ES.Length - 1] : 0f; } }
        [Header("바퀴 땅 맞춤(바퀴마다 아래로 레이)")]
        public bool wheelRays = true;                  // 바퀴 네 개 자리에서 도로 면으로 레이를 쏴 차 높이·앞뒤·좌우 기울기를 맞춤(빨리 감아도 안 파고듦)
        public float wheelBase = 2.88f, wheelTrack = 1.62f;
        public float smoothDist = 2.0f;                // 운전석 높이·기울기 부드럽게(머리 흔들림): 달린 거리(m) 기준 — 배속·하이퍼랩스에서도 늦지 않음. 밖에서 볼 땐 0.5m
        HashSet<Collider> groundCols; readonly RaycastHit[] hitBuf = new RaycastHit[8];
        float lastS = float.NaN, egoY, egoPitch, egoRoll, drvYaw; bool egoInit;

        void Start()
        {
            if (driveJson == null) { Debug.LogError("PNUDrivePlayer: driveJson 없음"); enabled = false; return; }
            D = JsonUtility.FromJson<DriveData>(driveJson.text);
            SignalPlans.Load(D.plans);
            ES = D.ego.s; EX = D.ego.x; EY = D.ego.y; EZ = D.ego.z; ET = D.ego.t;
            ETn = new float[ES.Length];
            for (int i = 0; i < ES.Length; i++) { float d = 0; foreach (var st in D.stops) if (st.s < ES[i] - 0.01f) d += st.t1 - st.t0; ETn[i] = ET[i] - d; }
            hstate = new string[D.hyper.Length]; for (int i = 0; i < hstate.Length; i++) hstate[i] = "idle";
            lights = FindObjectsOfType<PNUSignalLight>();
            if (npcJson != null)
            {
                N = JsonUtility.FromJson<NpcData>(npcJson.text);
                foreach (var d in N.npcs)
                {
                    int np = d.path.Length / 3; if (np < 2 || d.s.Length < 2) continue;
                    var n = new Npc { d = d, p = new Vector3[np], cum = new float[np] };
                    for (int k = 0; k < np; k++) n.p[k] = new Vector3(d.path[k * 3], d.path[k * 3 + 1], d.path[k * 3 + 2]);
                    for (int k = 1; k < np; k++) { var a = n.p[k - 1]; var b = n.p[k]; n.cum[k] = n.cum[k - 1] + new Vector2(b.x - a.x, b.z - a.z).magnitude; }
                    n.t1 = d.t0 + (d.s.Length - 1) * N.dt;
                    npcs.Add(n);
                }
            }
            if (cam == null) cam = Camera.main;
            // VR 리그가 씬에 있으면(이름으로 찾음) 그 카메라를 쓰고, 빌더가 만든 기본 카메라는 끔
            if (xrOrigin == null && driverEye != null)
                foreach (var nm in new[] { "XR Origin", "XR Origin (XR Rig)", "XR Origin (VR)", "XROrigin", "XR Rig", "MRTK XR Rig", "OVRCameraRig" })
                { var g = GameObject.Find(nm); if (g != null) { xrOrigin = g.transform; break; } }
            if (xrOrigin != null)
            {
                var rc = xrOrigin.GetComponentInChildren<Camera>();
                if (rc != null && rc != cam)
                {
                    if (cam != null && !cam.transform.IsChildOf(xrOrigin)) cam.gameObject.SetActive(false);
                    cam = rc; foreach (var ins in FindObjectsOfType<PNUInstancer>()) ins.cam = cam;
                }
            }
            if (driverOnXR && driverEye != null && XrActive()) camMode = CamMode.Driver;
            SetupGround();
            Place();
        }

        // ---------- 바퀴 땅 맞춤: 경로 둘레 도로·교차로 조각에만 메쉬 콜라이더 ----------
        void SetupGround()
        {
            groundCols = null; if (!wheelRays) return;
            float t0 = Time.realtimeSinceStartup; var cells = new HashSet<long>();
            for (int i = 0; i < ES.Length; i++) cells.Add(CellKey(Mathf.FloorToInt(EX[i] / 50f), Mathf.FloorToInt(EZ[i] / 50f)));
            var set = new HashSet<Collider>(); int added = 0;
            foreach (var mb in FindObjectsOfType<MonoBehaviour>())
            {
                if (mb == null) continue; string tn = mb.GetType().Name;
                if (tn != "PNURoadPiece" && tn != "PNUJunctionPiece") continue;          // 새 도로 생성기의 도로·교차로 조각(예전 지도엔 없음 → 경로 높이 그대로)
                var mf = mb.GetComponent<MeshFilter>(); var rd = mb.GetComponent<Renderer>();
                if (mf == null || mf.sharedMesh == null || rd == null || !NearRoute(rd.bounds, cells)) continue;
                var mc = mb.GetComponent<MeshCollider>();
                if (mc == null) { mc = mb.gameObject.AddComponent<MeshCollider>(); mc.sharedMesh = mf.sharedMesh; added++; }
                set.Add(mc);
            }
            if (set.Count == 0) return;
            groundCols = set; Physics.SyncTransforms();
            Debug.Log(string.Format("PNUDrivePlayer: 바퀴 땅 맞춤 — 경로 둘레 도로·교차로 {0}개(콜라이더 새로 {1}, {2:0.0}초)", set.Count, added, Time.realtimeSinceStartup - t0));
        }
        static float KD(float ds, float L) { return 1f - Mathf.Exp(-ds / Mathf.Max(0.05f, L)); }   // 달린 거리 ds 동안 따라붙는 비율(L m 마다 63%)
        static long CellKey(int x, int z) { return ((long)x << 32) ^ (uint)z; }
        static bool NearRoute(Bounds b, HashSet<long> cells)
        {
            int x0 = Mathf.FloorToInt((b.min.x - 20f) / 50f), x1 = Mathf.FloorToInt((b.max.x + 20f) / 50f), z0 = Mathf.FloorToInt((b.min.z - 20f) / 50f), z1 = Mathf.FloorToInt((b.max.z + 20f) / 50f);
            for (int x = x0; x <= x1; x++) for (int z = z0; z <= z1; z++) if (cells.Contains(CellKey(x, z))) return true;
            return false;
        }
        /// <summary>(x,z) 바로 아래 도로 면 높이 — 경로 높이 yExp 둘레(위 1.6m ~ 아래 2m)만 봄: 위 고가·아래 길은 안 잡힘. 못 찾으면 yExp</summary>
        float GroundY(float x, float z, float yExp)
        {
            if (groundCols == null) return yExp;
            int n = Physics.RaycastNonAlloc(new Vector3(x, yExp + 1.6f, z), Vector3.down, hitBuf, 3.6f, ~0, QueryTriggerInteraction.Ignore);
            float best = float.MaxValue, y = yExp;
            for (int i = 0; i < n; i++)
            {
                var h = hitBuf[i]; if (h.collider == null || !groundCols.Contains(h.collider)) continue;
                float d = Mathf.Abs(h.point.y - yExp); if (d < best) { best = d; y = h.point.y; }
            }
            return y;
        }

        static int Bs(float[] a, float v)
        {
            int lo = 0, hi = a.Length - 1;
            if (v <= a[0]) return 0; if (v >= a[hi]) return hi - 1;
            while (hi - lo > 1) { int m = (lo + hi) >> 1; if (a[m] <= v) lo = m; else hi = m; }
            return lo;
        }
        public Vector3 EgoPos(float s)
        {
            int i = Bs(ES, s); float f = Mathf.Clamp01((s - ES[i]) / Mathf.Max(1e-6f, ES[i + 1] - ES[i]));
            return new Vector3(Mathf.Lerp(EX[i], EX[i + 1], f), Mathf.Lerp(EY[i], EY[i + 1], f), Mathf.Lerp(EZ[i], EZ[i + 1], f));
        }
        float SAtT(float tt, out StopD stop)
        {
            stop = null; float acc = 0;
            foreach (var st in D.stops) { if (tt >= st.t0 && tt <= st.t1) { stop = st; Speed = 0; return st.s; } if (tt > st.t1) acc += st.t1 - st.t0; }
            float tn = tt - acc; int i = Bs(ETn, tn); float dt = Mathf.Max(1e-6f, ETn[i + 1] - ETn[i]);
            float f = Mathf.Clamp01((tn - ETn[i]) / dt); Speed = (ES[i + 1] - ES[i]) / dt;
            return Mathf.Lerp(ES[i], ES[i + 1], f);
        }
        public float TAtS(float s) { int i = Bs(ES, s); float f = (s - ES[i]) / Mathf.Max(1e-6f, ES[i + 1] - ES[i]); return Mathf.Lerp(ET[i], ET[i + 1], f); }
        int HyperIdx(float s) { for (int i = 0; i < D.hyper.Length; i++) if (s >= D.hyper[i].s0 && s < D.hyper[i].s1) return i; return -1; }
        float HyperRate(float s)
        {
            int i = HyperIdx(s); if (i < 0) return 1f;
            bool on = autoSkip ? hstate[i] != "cancel" : hstate[i] == "on";
            if (!on) return 1f;
            float e0 = s - D.hyper[i].s0, e1 = D.hyper[i].s1 - s; float ramp = Mathf.Clamp01(Mathf.Min(e0, e1) / 150f);
            return Mathf.Lerp(1f, D.hyper[i].rate, ramp);
        }
        bool InTunnel(float s, float pad) { foreach (var r in D.tunnels) if (s > r.s0 - pad && s < r.s1 + pad) return true; return false; }
        float TunnelDepth(float s)
        {
            float d = 0;
            foreach (var r in D.tunnels) if (s > r.s0 - 30 && s < r.s1 + 30) d = Mathf.Max(d, Mathf.Min(1f, Mathf.Min(s - (r.s0 - 30), (r.s1 + 30) - s) / 40f));
            return d;
        }
        public void Jump(float newT) { t = Mathf.Clamp(newT, 0, D.total); camJump = true; }
        public string Label(float s) { foreach (var l in D.labels) if (s >= l.s0 && s < l.s1) return l.label; return D.labels.Length > 0 ? D.labels[D.labels.Length - 1].label : ""; }
        public float Limit(float s) { if (s > D.rainS0 && s < D.rainS1) return 64; foreach (var z in D.zones) if (s >= z.s0 && s < z.s1) return z.kmh; return 50; }

        void Update()
        {
            if (D == null) return;
            float dt = Mathf.Min(0.1f, Time.deltaTime);
            StopD stop; float s0 = SAtT(t, out stop);
            float hr = HyperRate(s0); hyperCur += (hr - hyperCur) * Mathf.Min(1f, dt * 3f);
            if (KeyDown(0)) { playing = !playing; if (playing && t >= D.total) Jump(0); }
            if (KeyDown(1)) { camMode = camMode == CamMode.Follow ? CamMode.Driver : CamMode.Follow; camJump = true; }
            if (playing) { t += dt * rate * hyperCur; if (t >= D.total) { t = D.total; playing = false; } }
            for (int i = 0; i < D.hyper.Length; i++) if (HyperIdx(s0) != i && hstate[i] == "on" && s0 > D.hyper[i].s1) hstate[i] = "idle";
            Place();
            if (Time.time > nextSig) { nextSig = Time.time + 0.1f; if (lights != null) foreach (var l in lights) if (l != null) l.Apply(t); }
            UpdateNpc();
        }
        void Place()
        {
            StopD stop; float s = SAtT(t, out stop); S = s;
            Vector3 p = EgoPos(s), a = EgoPos(s - 6), b = EgoPos(s + 6);
            Vector3 fwd = b - a; float yaw = Mathf.Atan2(fwd.x, fwd.z) * Mathf.Rad2Deg;
            float pitchT = Mathf.Atan2(b.y - a.y, 12f) * Mathf.Rad2Deg;
            // 부드럽게 따라가기는 달린 거리 기준(예전: 시간 기준 → 배속·하이퍼랩스·빠른 속도에서 차·카메라가 늦게 따라와 오르막에 파고듦)
            float ds = camJump || !egoInit || float.IsNaN(lastS) ? 1e6f : Mathf.Abs(s - lastS); lastS = s;
            // 바퀴 네 개: 도로 면에 아래로 레이 → 높이 = 네 바퀴 평균, 앞뒤·좌우 기울기 = 바퀴 높이 차
            float hb = wheelBase * 0.5f, ht = wheelTrack * 0.5f;
            Quaternion qy = Quaternion.Euler(0, yaw, 0); Vector3 f = qy * Vector3.forward, r = qy * Vector3.right;
            Vector3 cF = p + f * hb, cB = p - f * hb; float yF = EgoPos(s + hb).y, yB = EgoPos(s - hb).y;
            float fl = GroundY(cF.x - r.x * ht, cF.z - r.z * ht, yF), fr = GroundY(cF.x + r.x * ht, cF.z + r.z * ht, yF);
            float bl = GroundY(cB.x - r.x * ht, cB.z - r.z * ht, yB), br = GroundY(cB.x + r.x * ht, cB.z + r.z * ht, yB);
            float yC = (fl + fr + bl + br) * 0.25f;
            float pitchW = Mathf.Atan2((fl + fr) - (bl + br), 2f * wheelBase) * Mathf.Rad2Deg, rollW = Mathf.Atan2((fr + br) - (fl + bl), 2f * wheelTrack) * Mathf.Rad2Deg;
            if (camJump || !egoInit) { egoY = yC; egoPitch = pitchW; egoRoll = rollW; drvYaw = yaw; camYaw = yaw; camH = yC; camPitch = pitchT; egoInit = true; }
            bool seat = camMode == CamMode.Driver && driverEye != null && cam != null;
            float kE = KD(ds, seat ? smoothDist : 0.5f);
            egoY += (yC - egoY) * kE; egoPitch += (pitchW - egoPitch) * kE; egoRoll += (rollW - egoRoll) * kE;
            if (egoY < yC - 0.02f) egoY = yC - 0.02f;                                 // 땅 밑으로는 안 들어감
            drvYaw += Mathf.DeltaAngle(drvYaw, yaw) * KD(ds, 2.5f);
            camYaw += Mathf.DeltaAngle(camYaw, yaw) * KD(ds, camMode == CamMode.Driver ? 2.5f : 6f);
            camPitch += (pitchT - camPitch) * KD(ds, 8f);
            camH += (egoY - camH) * KD(ds, 5f); if (camH < egoY - 0.3f) camH = egoY - 0.3f;
            camJump = false;
            if (ego != null)
            {
                // 바퀴가 닿은 도로 면 그대로(운전석은 방향만 조금 더 부드럽게)
                ego.position = new Vector3(p.x, egoY, p.z);
                ego.rotation = Quaternion.Euler(0, seat ? drvYaw : yaw, 0) * Quaternion.Euler(-egoPitch, 0, egoRoll);
                bool vis = camMode != CamMode.Driver || (showEgoInDriver && driverEye != null);
                if (ego.gameObject.activeSelf != vis) ego.gameObject.SetActive(vis);
            }
            if (cam == null || camMode == CamMode.Free) { Unseat(); return; }
            if (seat) { Seat(); return; }
            Unseat();
            var rot = Quaternion.Euler(0, camYaw, 0); Vector3 cf = rot * Vector3.forward;
            if (camMode == CamMode.Follow)
            {
                float td = TunnelDepth(s); float back = 17f - 7f * td, up = 6.2f - 3.4f * td;
                Vector3 pos = new Vector3(p.x, camH + up, p.z) - cf * back;
                Vector3 look = new Vector3(p.x, camH + 1.6f, p.z) + cf * 20f + Vector3.up * Mathf.Tan(camPitch * Mathf.Deg2Rad) * 20f;
                cam.transform.position = pos; cam.transform.LookAt(look);
            }
            else
            {
                Vector3 right = rot * Vector3.right;
                Vector3 pos = new Vector3(p.x, camH + 1.25f, p.z) - right * 0.4f;
                cam.transform.position = pos; cam.transform.rotation = Quaternion.Euler(-camPitch, camYaw, 0);
            }
        }

        // ---------- 운전석(카메라를 차에 붙임) ----------
        Transform SeatRoot { get { return xrOrigin != null ? xrOrigin : cam.transform; } }
        void Seat()
        {
            if (cameraRig == null) cameraRig = driverEye;
            var root = SeatRoot;
            if (!seated || root.parent != cameraRig)
            {
                if (!camHomeSet) { camHome = root.parent; camHomeSet = true; }
                root.SetParent(cameraRig, false); root.localPosition = Vector3.zero; root.localRotation = Quaternion.identity;
                cam.nearClipPlane = 0.03f; seated = true; lookYaw = 0; lookPitch = 0;
                recenterAt = Time.unscaledTime + 0.4f;                       // VR 추적이 자리 잡은 뒤 맞춤
            }
            if (XrActive())
            {
                if ((recenterAt > 0 && Time.unscaledTime >= recenterAt) || KeyDown(2)) { Recenter(); recenterAt = -1f; }
                return;
            }
            // PC: 오른쪽 마우스 드래그로 둘러보기(뒤를 볼수록 몸을 가운데·앞으로 기울임), R = 정면
            if (LookHeld()) { var d = LookDelta(); lookYaw = Mathf.Clamp(lookYaw + d.x * 0.15f, -150f, 150f); lookPitch = Mathf.Clamp(lookPitch + d.y * 0.15f, -55f, 45f); }
            if (KeyDown(2)) { lookYaw = 0; lookPitch = 0; }
            float k = Mathf.Clamp01((Mathf.Abs(lookYaw) - 80f) / 70f); k = k * k * (3f - 2f * k);
            cameraRig.localPosition = new Vector3((lookYaw > 0 ? 0.24f : -0.1f) * k, 0.03f * k, 0.1f * k);
            cameraRig.localRotation = Quaternion.identity;
            cam.transform.localRotation = Quaternion.Euler(4f - lookPitch, lookYaw, 0);
        }
        void Unseat()
        {
            if (!seated || cam == null) return;
            seated = false; var root = SeatRoot;
            root.SetParent(camHomeSet ? camHome : null, true); cam.nearClipPlane = 0.2f;
            if (xrOrigin == null) cam.transform.localScale = Vector3.one;
        }
        /// <summary>VR: 지금 머리 위치·방향을 운전자 눈 자리·차 앞쪽에 맞춤</summary>
        public void Recenter()
        {
            if (cameraRig == null || cam == null) return;
            cameraRig.localPosition = Vector3.zero; cameraRig.localRotation = Quaternion.identity;
            Vector3 hp = cameraRig.InverseTransformPoint(cam.transform.position);
            Vector3 hf = cameraRig.InverseTransformDirection(cam.transform.forward); hf.y = 0;
            float yaw = hf.sqrMagnitude > 1e-6f ? Mathf.Atan2(hf.x, hf.z) * Mathf.Rad2Deg : 0f;
            var q = Quaternion.Euler(0, -yaw, 0);
            cameraRig.localRotation = q; cameraRig.localPosition = -(q * hp);
            lookYaw = 0; lookPitch = 0;
        }
        static bool XrActive() { return UnityEngine.XR.XRSettings.isDeviceActive; }

        // ---------- 입력(새 Input System / 옛 Input Manager 둘 다) ----------
        static bool LookHeld()
        {
#if ENABLE_INPUT_SYSTEM
            var m = Mouse.current; return m != null && m.rightButton.isPressed;
#elif ENABLE_LEGACY_INPUT_MANAGER
            return Input.GetMouseButton(1);
#else
            return false;
#endif
        }
        static Vector2 LookDelta()
        {
#if ENABLE_INPUT_SYSTEM
            var m = Mouse.current; return m != null ? m.delta.ReadValue() : Vector2.zero;
#elif ENABLE_LEGACY_INPUT_MANAGER
            return new Vector2(Input.GetAxis("Mouse X"), Input.GetAxis("Mouse Y")) * 12f;
#else
            return Vector2.zero;
#endif
        }
        /// <summary>0 = Space(재생), 1 = C(시점), 2 = R(정면)</summary>
        static bool KeyDown(int which)
        {
#if ENABLE_INPUT_SYSTEM
            var k = Keyboard.current; if (k == null) return false;
            return which == 0 ? k.spaceKey.wasPressedThisFrame : which == 1 ? k.cKey.wasPressedThisFrame : k.rKey.wasPressedThisFrame;
#elif ENABLE_LEGACY_INPUT_MANAGER
            return Input.GetKeyDown(which == 0 ? KeyCode.Space : which == 1 ? KeyCode.C : KeyCode.R);
#else
            return false;
#endif
        }
        GameObject Get(int kind, int idx, string color)
        {
            var list = pool[kind];
            while (list.Count <= idx)
            {
                GameObject g;
                if (npcPrefabs != null && kind < npcPrefabs.Length && npcPrefabs[kind] != null) g = Instantiate(npcPrefabs[kind]);
                else
                {
                    g = GameObject.CreatePrimitive(PrimitiveType.Cube); Destroy(g.GetComponent<Collider>());
                    g.transform.localScale = SIZE[kind];
                    var r = g.GetComponent<Renderer>(); Color c; if (ColorUtility.TryParseHtmlString(color, out c)) r.material.color = c;
                }
                g.name = "NPC_" + kind + "_" + list.Count; g.transform.SetParent(transform, false); list.Add(g);
            }
            return list[idx];
        }
        void UpdateNpc()
        {
            if (N == null) return;
            int[] used = { 0, 0, 0 };
            foreach (var n in npcs)
            {
                if (t < n.d.t0 || t > n.t1) continue;
                float q = (t - n.d.t0) / N.dt; int i0 = Mathf.Min(n.d.s.Length - 2, Mathf.FloorToInt(q)); float f = q - i0;
                float sv = Mathf.Lerp(n.d.s[i0], n.d.s[i0 + 1], f);
                int j = Bs(n.cum, sv); float L = Mathf.Max(1e-6f, n.cum[j + 1] - n.cum[j]); float g = Mathf.Clamp01((sv - n.cum[j]) / L);
                Vector3 pos = Vector3.Lerp(n.p[j], n.p[j + 1], g);
                Vector3 dir = n.p[Mathf.Min(n.p.Length - 1, j + 1)] - n.p[Mathf.Max(0, j - 0)]; dir.y = 0;
                int k = Mathf.Clamp(n.d.kind, 0, 2);
                var go = Get(k, used[k]++, n.d.color);
                if (!go.activeSelf) go.SetActive(true);
                bool box = npcPrefabs == null || k >= npcPrefabs.Length || npcPrefabs[k] == null;
                go.transform.position = pos + (box ? Vector3.up * (SIZE[k].y * 0.5f + 0.3f) : Vector3.zero);
                if (dir.sqrMagnitude > 1e-4f) go.transform.rotation = Quaternion.LookRotation(dir.normalized, Vector3.up);
            }
            for (int k = 0; k < 3; k++) for (int i = used[k]; i < pool[k].Count; i++) if (pool[k][i].activeSelf) pool[k][i].SetActive(false);
        }

        void OnGUI()
        {
            if (D == null) return;
            GUI.Box(new Rect(10, 10, 360, 70), "");
            GUI.Label(new Rect(20, 16, 340, 22), Label(S));
            GUI.Label(new Rect(20, 38, 340, 22), string.Format("{0:0} km/h  (제한 {1:0})   {2:0.0} / {3:0.0} km   시간 {4:0}s", Speed * 3.6f * (playing ? 1 : 1), Limit(S), S / 1000f, ES[ES.Length - 1] / 1000f, t));
            GUI.Label(new Rect(20, 58, 340, 22), hyperCur > 1.05f ? "하이퍼랩스 ×" + hyperCur.ToString("0.0") : "");
            float y = Screen.height - 46;
            if (GUI.Button(new Rect(10, y, 80, 34), playing ? "일시정지" : "재생")) { playing = !playing; if (playing && t >= D.total) Jump(0); }
            if (GUI.Button(new Rect(96, y, 50, 34), "1배")) rate = 1; if (GUI.Button(new Rect(150, y, 50, 34), "2배")) rate = 2; if (GUI.Button(new Rect(204, y, 50, 34), "8배")) rate = 8;
            autoSkip = GUI.Toggle(new Rect(262, y + 8, 150, 24), autoSkip, " 빠른 구간 자동");
            if (GUI.Button(new Rect(412, y, 90, 34), camMode == CamMode.Follow ? "운전석 보기" : "따라가기")) { camMode = camMode == CamMode.Follow ? CamMode.Driver : CamMode.Follow; camJump = true; }
            if (camMode == CamMode.Driver && driverEye != null)
            {
                GUI.Label(new Rect(10, 86, 420, 22), XrActive() ? "VR: R 키 = 머리 위치를 운전석에 다시 맞추기" : "오른쪽 마우스 드래그 = 둘러보기 · R = 정면");
                if (GUI.Button(new Rect(10, 110, 110, 28), XrActive() ? "시점 맞추기" : "정면 보기")) { if (XrActive()) Recenter(); else { lookYaw = 0; lookPitch = 0; } }
            }
            float nt = GUI.HorizontalSlider(new Rect(512, y + 12, Mathf.Max(100, Screen.width - 530), 20), t, 0, D.total);
            if (Mathf.Abs(nt - t) > 0.01f) Jump(nt);
            // 넷플릭스식 빠르게 보기 버튼
            int hi = HyperIdx(S);
            if (hi >= 0)
            {
                float remT = TAtS(D.hyper[hi].s1) - t;
                bool on = autoSkip ? hstate[hi] != "cancel" : hstate[hi] == "on";
                if (remT > 8 || on)
                {
                    string txt = on ? string.Format("하이퍼랩스 ×{0:0} · {1} — 원래 속도로", D.hyper[hi].rate, D.hyper[hi].name)
                                    : string.Format("▶▶ 빠르게 보기 ×{0:0} · {1} ({2:0}초)", D.hyper[hi].rate, D.hyper[hi].name, remT);
                    if (GUI.Button(new Rect(Screen.width - 380, Screen.height - 110, 370, 44), txt))
                    {
                        if (autoSkip) hstate[hi] = on ? "cancel" : "idle"; else hstate[hi] = on ? "idle" : "on";
                    }
                }
            }
        }
    }
}
