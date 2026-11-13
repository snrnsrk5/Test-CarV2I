// PNU_Drive 자차 주행: Data/path.json(1m 점: 위치·속도·시간 + 10m 제한속도·구간 이름)을 따라 차를 움직이고, 바퀴 네 자리 레이로 노면 높이·기울기 맞춤
// 화면(v14 PNUDrivePlayer 와 같게): 왼쪽 위 정보 상자(구간·속도·제한·거리), 아래 버튼줄(재생/일시정지 · 1·2·4·8배 · 운전석 보기/따라가기 · 처음으로 · 구간 슬라이더)
// 키: Space 재생/멈춤, [ ] 배속, C 운전석↔따라가기, R 정면 보기(둘러보기 되돌림), Home 처음으로, ←/→ 150m 뒤/앞, H 화면 표시 숨기기
// 마우스: 오른쪽 드래그 = 운전석에선 둘러보기(뒤를 볼수록 몸을 기울임), 따라가기에선 차 둘레로 돌리기 / 휠 = 따라가기 거리
using System;
using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace PNU.Drive
{
    public class PNUDriveEgo : MonoBehaviour
    {
        [Serializable] public class Stop { public float s; public int j, group; public string mv = "S"; }
        [Serializable] public class Seg { public float s0, s1; public string name; }
        [Serializable]
        public class PathData
        {
            public int n; public float length, duration; public float[] x, y, z, v, t; public Stop[] stops;
            public float lim_step; public int[] lim; public Seg[] labels;
        }

        public TextAsset pathJson;
        public Transform car;                 // 차(앞 = +Z, 원점 = 휠베이스 가운데 노면)
        public Transform driverEye;           // 운전자 눈(없으면 차 기준 자리)
        public Camera cam;
        public bool playing = true;
        public float rate = 1f;
        public float t;
        public enum CamMode { Driver, Follow }
        public CamMode camMode = CamMode.Driver;
        public Vector3 eyeOffset = Vector3.zero;          // 운전석 눈 위치 미세 조정(차 기준 m)
        public float eyePitch = 3f;                       // 운전석 시선 아래로(도)
        public float driverNear = 0.05f;                  // 운전석 시점 카메라 가까운 면(실내가 잘리지 않게)
        public bool hideGlassInDriver = true;             // 운전석 시점에서 "Glass" 렌더러 숨김(PNU01: 반사 없이 또렷하게. 테슬라는 PNUDriveCockpit 이 유리 투명도 조절)
        public bool showGui = true;                       // 화면 정보·버튼(H 로 숨기기/보이기)
        Renderer[] glass;
        public float wheelBase = 3.0f, wheelTrack = 1.62f;
        public LayerMask groundMask = ~0;
        public float smoothDist = 2.0f;
        [Header("시점 돌리기(오른쪽 마우스 드래그)")]
        public float lookSpeed = 0.15f;                   // 드래그 1픽셀당 도
        public float followDist0 = 9.3f, followPitch0 = 14f;   // 따라가기 기본: 차 뒤 9m·위 3.5m 에서 차 앞 4m 를 봄
        float lookYaw, lookPitch, orbitYaw, orbitPitch, followDist = -1f;

        PathData D; float[] S;
        float hSm, pitchSm, rollSm; bool hasSm;
        readonly RaycastHit[] hits = new RaycastHit[8];
        public float Speed { get; private set; }
        public float Dist { get; private set; }
        public bool Ready { get { return D != null && D.n >= 2 && S != null; } }
        public float Length { get { return D != null ? D.length : 0f; } }
        public bool obeySignals = true;                  // 신호 교차로 정지선에서 빨강·노랑이면 섬
        float simT; string status = "";
        /// <summary>자차 판단(신호·앞차·비보호 좌회전·빨강 우회전·교차로 건너편 막힘) — PNUTrafficCore 의 EgoDriver(시험 프로그램과 같은 계산)</summary>
        public EgoDriver Driver { get; private set; }
        PNUDriveSignals sigOwner;
        /// <summary>서는 까닭: 0 아님, 1 빨강·노랑, 2 좌회전 화살표 기다림, 3 빨강 우회전 일시정지, 4 비보호 좌회전 맞은편 차 기다림, 5 교차로 건너편 막힘 (길안내 알림음용)</summary>
        public int SignalWait { get; private set; }
        /// <summary>그 정지선까지 남은 거리(m)</summary>
        public float SignalDist { get; private set; }
        CamMode glassMode = (CamMode)(-1);

        void Awake() { Load(); }

        public void Load()
        {
            if (pathJson == null) return;
            D = JsonUtility.FromJson<PathData>(pathJson.text);
            S = new float[D.n]; for (int i = 1; i < D.n; i++) S[i] = S[i - 1] + Mathf.Sqrt(Sq(D.x[i] - D.x[i - 1]) + Sq(D.z[i] - D.z[i - 1]));
            var dr = new EgoDriver { vAt = VAt, length = D.length };
            int ns = D.stops != null ? D.stops.Length : 0;
            dr.stopS = new float[ns]; dr.stopJ = new int[ns]; dr.stopG = new int[ns]; dr.stopMv = new string[ns];
            for (int k = 0; k < ns; k++) { dr.stopS[k] = D.stops[k].s; dr.stopJ[k] = D.stops[k].j; dr.stopG[k] = D.stops[k].group; dr.stopMv[k] = string.IsNullOrEmpty(D.stops[k].mv) ? "S" : D.stops[k].mv; }
            if (Driver != null) { dr.s = Driver.s; dr.v = Driver.v; }
            Driver = dr;
        }

        static float Sq(float a) { return a * a; }

        int Bs(float[] a, float v)
        {
            int lo = 0, hi = a.Length - 2;
            if (v <= a[0]) return 0; if (v >= a[a.Length - 1]) return a.Length - 2;
            while (lo < hi) { int m = (lo + hi + 1) >> 1; if (a[m] <= v) lo = m; else hi = m - 1; }
            return lo;
        }

        /// <summary>경로 점(1m 간격) 수와 그 자리(매핑 띠 노면 높이용)</summary>
        public int PathCount { get { return D != null ? D.n : 0; } }
        public Vector3 PathPoint(int i) { i = Mathf.Clamp(i, 0, D.n - 1); return new Vector3(D.x[i], D.y[i], D.z[i]); }

        public float SAtT(float tt) { int i = Bs(D.t, tt); float f = Mathf.InverseLerp(D.t[i], D.t[i + 1], tt); return Mathf.Lerp(S[i], S[i + 1], f); }
        public float TAtS(float s) { int i = Bs(S, s); float f = Mathf.InverseLerp(S[i], S[i + 1], s); return Mathf.Lerp(D.t[i], D.t[i + 1], f); }
        public Vector3 PosAt(float s) { int i = Bs(S, s); float f = Mathf.InverseLerp(S[i], S[i + 1], s); return new Vector3(Mathf.Lerp(D.x[i], D.x[i + 1], f), Mathf.Lerp(D.y[i], D.y[i + 1], f), Mathf.Lerp(D.z[i], D.z[i + 1], f)); }
        float VAt(float s) { int i = Bs(S, s); float f = Mathf.InverseLerp(S[i], S[i + 1], s); return Mathf.Lerp(D.v[i], D.v[i + 1], f); }

        /// <summary>경로 s(m)의 제한속도(km/h), 모르면 0</summary>
        public int Limit(float s)
        {
            if (D == null || D.lim == null || D.lim.Length == 0) return 0;
            return D.lim[Mathf.Clamp(Mathf.RoundToInt(s / Mathf.Max(1f, D.lim_step)), 0, D.lim.Length - 1)];
        }
        /// <summary>경로 s(m)의 구간 이름(도로·터널·다리)</summary>
        public string Label(float s)
        {
            if (D == null || D.labels == null || D.labels.Length == 0) return "부산대학교 → 광안대교 → 부경대학교";
            foreach (var g in D.labels) if (s >= g.s0 && s < g.s1) return g.name;
            return D.labels[D.labels.Length - 1].name;
        }

        /// <summary>경로의 s(m) 자리로 바로 옮김(슬라이더·처음으로·←/→)</summary>
        public void JumpTo(float s)
        {
            if (!Ready) return;
            s = Mathf.Clamp(s, 0f, D.length);
            if (Driver != null) Driver.Reset(s);
            t = TAtS(s); hasSm = false;
            var tr = PNUDriveTraffic.Instance; if (tr != null) tr.OnJump();      // 차·사람도 새 자리 둘레에 다시
            var sc = PNUDriveScenario.Instance; if (sc != null) sc.OnJump(s);    // 5/6 상황도 그 자리에 맞게
        }
        public void TogglePlay() { playing = !playing; if (playing && Dist >= D.length - 0.5f) JumpTo(0f); }
        public void ToggleCam() { camMode = camMode == CamMode.Driver ? CamMode.Follow : CamMode.Driver; }
        public void ResetView() { lookYaw = lookPitch = orbitYaw = orbitPitch = 0f; followDist = followDist0; }
        float FollowDist { get { return followDist > 0f ? followDist : followDist0; } }

        void Update()
        {
            if (D == null || D.n < 2 || car == null) return;
            var scn = PNUDriveScenario.Instance;
            if (KeyDown(KeyCode.Space) && (scn == null || !scn.HandlesInput)) TogglePlay();   // 주행 전(목적지 설정 등)에는 시나리오가 Space 를 씀
            if (KeyDown(KeyCode.RightBracket)) rate = Mathf.Min(16f, rate * 2f);
            if (KeyDown(KeyCode.LeftBracket)) rate = Mathf.Max(0.25f, rate * 0.5f);
            if (KeyDown(KeyCode.C)) ToggleCam();
            if (KeyDown(KeyCode.R)) ResetView();
            if (KeyDown(KeyCode.H)) showGui = !showGui;
            if (KeyDown(KeyCode.Home)) JumpTo(0f);
            if (KeyDown(KeyCode.RightArrow)) JumpTo(Dist + 150f);
            if (KeyDown(KeyCode.LeftArrow)) JumpTo(Dist - 150f);
            // 시점 돌리기: 오른쪽 드래그(운전석 = 둘러보기, 따라가기 = 차 둘레로), 휠 = 따라가기 거리
            if (LookHeld())
            {
                Vector2 d = LookDelta();
                if (camMode == CamMode.Driver) { lookYaw = Mathf.Clamp(lookYaw + d.x * lookSpeed, -150f, 150f); lookPitch = Mathf.Clamp(lookPitch + d.y * lookSpeed, -55f, 45f); }
                else { orbitYaw = Mathf.Repeat(orbitYaw + d.x * lookSpeed * 1.5f + 180f, 360f) - 180f; orbitPitch = Mathf.Clamp(orbitPitch - d.y * lookSpeed, -18f, 65f); }
            }
            float wh = Wheel();
            if (camMode == CamMode.Follow && Mathf.Abs(wh) > 0.01f) followDist = Mathf.Clamp(FollowDist * (1f - 0.12f * Mathf.Clamp(wh, -3f, 3f)), 3.5f, 60f);
            if (camMode != glassMode)
            {
                glassMode = camMode; bool drv = camMode == CamMode.Driver;
                car.BroadcastMessage("SetDriverView", drv, SendMessageOptions.DontRequireReceiver);   // 운전석 시점이면 유리 거의 투명(PNUAVCar)
                if (glass == null) { var gl = new System.Collections.Generic.List<Renderer>(); foreach (var r in car.GetComponentsInChildren<Renderer>(true)) if (r.name == "Glass") gl.Add(r); glass = gl.ToArray(); }
                foreach (var r in glass) if (r != null) r.enabled = !(drv && hideGlassInDriver);                       // 유리 반사로 뿌옇게 보이지 않게
            }
            var sig = PNUDriveSignals.Instance;
            bool dyn = obeySignals && sig != null && D.stops != null && D.stops.Length > 0;
            float prevS = Dist, s;
            if (dyn)
            {
                // 작은 걸음(1/30초 이하)으로 나눠 신호·차·사람·자차를 같은 시각으로 함께 움직임(배속을 올려도 판단이 같게)
                float dt = playing ? Time.deltaTime * rate : 0f;
                var tr = PNUDriveTraffic.Instance; bool traffic = tr != null && tr.isActiveAndEnabled;
                if (traffic) tr.EnsureLoaded();
                if (Driver.egoState == null || sigOwner != sig) { Driver.egoState = sig.EgoState; Driver.leftProt = sig.LeftProtected; sigOwner = sig; }
                Driver.traffic = traffic ? tr.Core : null; Driver.es = traffic ? tr.Es : null;
                int n = dt > 0f ? Mathf.Clamp(Mathf.CeilToInt(dt * 30f), 1, 16) : 0; float h = n > 0 ? dt / n : 0f;
                for (int i = 0; i < n; i++)
                {
                    simT += h; sig.simTime = simT;
                    if (traffic) { tr.Tick(this, Driver, h, simT); Driver.leadGap = tr.leadGap; Driver.leadSpeed = tr.leadSpeed; }
                    else { Driver.leadGap = 1e9f; Driver.leadSpeed = 0f; }
                    if (scn != null) scn.Tick(h, simT);                                // 5/6 상황(연출 차·사람, 위험 정지·감속, 비 감속)
                    Driver.Step(h, simT);
                }
                if (n == 0) sig.simTime = simT;
                status = Driver.status; SignalWait = Driver.wait; SignalDist = Driver.waitDist;
                s = Driver.s; Speed = Driver.v;
            }
            else
            {
                SignalWait = 0;
                if (playing) t += Time.deltaTime * rate;
                if (t > D.duration) t = D.duration;
                s = SAtT(t); Speed = VAt(s);
            }
            if (playing && s >= D.length - 0.01f && Speed < 0.05f) playing = false;   // 도착하면 멈춤(재생 누르면 처음부터)
            Dist = s;
            Place(s, Mathf.Abs(s - prevS));
            if (cam != null) PlaceCamera();
        }

        void Place(float s, float ds)
        {
            Vector3 p = PosAt(s), a = PosAt(s - 2.5f), b = PosAt(s + 2.5f);
            Vector3 fwd = b - a; fwd.y = 0; if (fwd.sqrMagnitude < 1e-6f) fwd = car.forward; fwd.Normalize();
            Vector3 right = new Vector3(fwd.z, 0, -fwd.x);
            // 바퀴 네 자리 높이(레이), 없으면 경로 높이
            float hb = wheelBase * 0.5f, ht = wheelTrack * 0.5f;
            float fl = Ground(p + fwd * hb - right * ht, p.y), fr = Ground(p + fwd * hb + right * ht, p.y);
            float rl = Ground(p - fwd * hb - right * ht, p.y), rr = Ground(p - fwd * hb + right * ht, p.y);
            float h = (fl + fr + rl + rr) * 0.25f;
            float pitch = Mathf.Atan2(((fl + fr) - (rl + rr)) * 0.5f, wheelBase) * Mathf.Rad2Deg;
            float roll = Mathf.Atan2(((fl + rl) - (fr + rr)) * 0.5f, wheelTrack) * Mathf.Rad2Deg;
            if (!hasSm) { hSm = h; pitchSm = pitch; rollSm = roll; hasSm = true; }
            else
            {
                float k = 1f - Mathf.Exp(-Mathf.Max(ds, 0.0f) / Mathf.Max(0.05f, smoothDist));
                hSm = Mathf.Lerp(hSm, h, k); pitchSm = Mathf.Lerp(pitchSm, pitch, k); rollSm = Mathf.Lerp(rollSm, roll, k);
                hSm = Mathf.Max(hSm, h - 0.02f);
            }
            car.position = new Vector3(p.x, hSm, p.z);
            car.rotation = Quaternion.LookRotation(fwd, Vector3.up) * Quaternion.Euler(-pitchSm, 0, rollSm);
        }

        float Ground(Vector3 q, float yExp)
        {
            int n = Physics.RaycastNonAlloc(new Vector3(q.x, yExp + 1.6f, q.z), Vector3.down, hits, 3.6f, groundMask & ~(1 << PNUDriveRainBlock.Layer), QueryTriggerInteraction.Ignore);   // v17: 비 막는 상자는 땅이 아님
            float best = float.NegativeInfinity;
            for (int i = 0; i < n; i++)
            {
                if (car != null && hits[i].collider != null && hits[i].collider.transform.IsChildOf(car)) continue;
                if (hits[i].point.y > best) best = hits[i].point.y;
            }
            return float.IsNegativeInfinity(best) ? yExp : best;
        }

        void PlaceCamera()
        {
            if (camMode == CamMode.Driver)
            {
                // 운전자 눈: 차 기준(앞 +Z, 원점 = 휠베이스 가운데 노면). FBX 빈 노드가 축 변환 없이(높이가 z) 들어오면 바로잡음
                Vector3 lp = new Vector3(-0.37f, 1.12f, -0.2f);
                if (driverEye != null)
                {
                    lp = car.InverseTransformPoint(driverEye.position);
                    if (lp.y < 0.6f && lp.z > 0.8f) lp = new Vector3(lp.x, lp.z, -lp.y);
                }
                // 뒤를 볼수록 몸을 가운데(오른쪽 볼 때)·문 쪽(왼쪽 볼 때)과 앞으로 조금 기울임(v14 와 같게)
                float k = Mathf.Clamp01((Mathf.Abs(lookYaw) - 80f) / 70f); k = k * k * (3f - 2f * k);
                lp += new Vector3((lookYaw > 0f ? 0.24f : -0.1f) * k, 0.03f * k, 0.1f * k);
                cam.transform.position = car.TransformPoint(lp + eyeOffset);
                cam.transform.rotation = car.rotation * Quaternion.Euler(eyePitch - lookPitch, lookYaw, 0f);
                cam.nearClipPlane = driverNear;
            }
            else
            {
                cam.nearClipPlane = 0.12f;
                Vector3 f = car.forward; f.y = 0f; if (f.sqrMagnitude < 1e-6f) f = Vector3.forward; f.Normalize();
                float yaw = Mathf.Atan2(f.x, f.z) * Mathf.Rad2Deg + orbitYaw;
                float pitch = Mathf.Clamp(followPitch0 + orbitPitch, -4f, 80f);
                Vector3 dir = Quaternion.Euler(pitch, yaw, 0f) * Vector3.forward;
                Vector3 pivot = car.position + Vector3.up * 1.2f;
                Vector3 want = pivot - dir * FollowDist;
                Vector3 look = pivot + f * (4f * Mathf.Max(0f, Mathf.Cos(orbitYaw * Mathf.Deg2Rad)));
                float kk = 1f - Mathf.Exp(-Time.deltaTime * (LookHeld() ? 14f : 4f));
                cam.transform.position = Vector3.Lerp(cam.transform.position, want, kk);
                cam.transform.LookAt(look);
            }
        }

        // ---------- 입력(새 Input System / 옛 Input Manager 둘 다, v14 와 같게) ----------
        static bool KeyDown(KeyCode k)
        {
#if ENABLE_INPUT_SYSTEM
            var kb = Keyboard.current; if (kb == null) return false;
            switch (k)
            {
                case KeyCode.Space: return kb.spaceKey.wasPressedThisFrame;
                case KeyCode.C: return kb.cKey.wasPressedThisFrame;
                case KeyCode.R: return kb.rKey.wasPressedThisFrame;
                case KeyCode.H: return kb.hKey.wasPressedThisFrame;
                case KeyCode.Home: return kb.homeKey.wasPressedThisFrame;
                case KeyCode.LeftBracket: return kb.leftBracketKey.wasPressedThisFrame;
                case KeyCode.RightBracket: return kb.rightBracketKey.wasPressedThisFrame;
                case KeyCode.LeftArrow: return kb.leftArrowKey.wasPressedThisFrame;
                case KeyCode.RightArrow: return kb.rightArrowKey.wasPressedThisFrame;
            }
            return false;
#elif ENABLE_LEGACY_INPUT_MANAGER
            return Input.GetKeyDown(k);
#else
            return false;
#endif
        }
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
        static float Wheel()
        {
#if ENABLE_INPUT_SYSTEM
            var m = Mouse.current; return m != null ? m.scroll.ReadValue().y / 120f : 0f;
#elif ENABLE_LEGACY_INPUT_MANAGER
            return Input.mouseScrollDelta.y;
#else
            return 0f;
#endif
        }

        // ---------- 화면(v14 와 같은 자리) ----------
        void OnGUI()
        {
            if (D == null) return;
            float k = Mathf.Clamp(Screen.height / 900f, 0.8f, 2.5f);          // 큰 화면에서도 글자·버튼이 작지 않게
            var keep = GUI.matrix; GUI.matrix = Matrix4x4.Scale(new Vector3(k, k, 1f));
            float W = Screen.width / k, H = Screen.height / k;
            if (!showGui)
            {
                if (GUI.Button(new Rect(10, H - 40, 90, 30), "화면 표시")) showGui = true;
                GUI.matrix = keep; return;
            }
            GUI.Box(new Rect(10, 10, 390, 72), "");
            GUI.Label(new Rect(20, 15, 370, 22), Label(Dist));
            int lim = Limit(Dist);
            GUI.Label(new Rect(20, 36, 370, 22), string.Format("{0:0} km/h{1}    {2:0.0} / {3:0.0} km", Speed * 3.6f, lim > 0 ? "  (제한 " + lim + ")" : "", Dist / 1000f, D.length / 1000f));
            GUI.Label(new Rect(20, 57, 370, 22), (Mathf.Approximately(rate, 1f) ? "" : "×" + rate.ToString("0.##") + "   ") + status);
            GUI.Label(new Rect(10, 88, 520, 22), camMode == CamMode.Driver ? "오른쪽 마우스 드래그 = 둘러보기 · R = 정면" : "오른쪽 마우스 드래그 = 돌려보기 · 휠 = 거리 · R = 처음 시점");
            if (GUI.Button(new Rect(10, 112, 110, 28), camMode == CamMode.Driver ? "정면 보기" : "시점 되돌리기")) ResetView();
            float y = H - 46, x = 10;
            if (GUI.Button(new Rect(x, y, 80, 34), playing ? "일시정지" : "재생")) TogglePlay();
            x += 86;
            foreach (var r in new[] { 1f, 2f, 4f, 8f })
            {
                if (GUI.Button(new Rect(x, y, 52, 34), (Mathf.Approximately(rate, r) ? "▶" : "") + r.ToString("0") + "배")) rate = r;
                x += 56;
            }
            x += 6;
            if (GUI.Button(new Rect(x, y, 96, 34), camMode == CamMode.Driver ? "따라가기" : "운전석 보기")) ToggleCam();
            x += 102;
            if (GUI.Button(new Rect(x, y, 80, 34), "처음으로")) JumpTo(0f);
            x += 88;
            float ns = GUI.HorizontalSlider(new Rect(x, y + 12, Mathf.Max(100f, W - x - 18), 20), Dist, 0f, D.length);
            if (Mathf.Abs(ns - Dist) > 1f) JumpTo(ns);
            GUI.matrix = keep;
        }
    }
}
