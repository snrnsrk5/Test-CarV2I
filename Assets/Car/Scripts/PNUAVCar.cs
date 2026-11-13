// PNU01 자율주행 시험차 — 센서 그룹 켜고 끄기 · 휠 스타일 · 바퀴/조향/운전대/라이다 회전
// Unity 2022.3 (URP) · 새 Input System / 옛 Input Manager 둘 다 동작
using System.Collections.Generic;
using UnityEngine;

[DisallowMultipleComponent]
public class PNUAVCar : MonoBehaviour
{
    public enum WheelStyle { Turbine = 0, MultiSpoke = 1, PixelAero = 2, Split5 = 3 }

    [Header("센서 그룹 (체크 해제 = 끔)")]
    public bool roofTower = true;     // 지붕 타워: 회전 라이다·카메라 링·GNSS·랙
    public bool cornerLidar = true;   // 범퍼 네 모서리 라이다
    public bool fenderPods = true;    // 앞 펜더 포드(라이다+카메라)
    public bool cameras = true;       // 앞유리·B필러·후방 카메라
    public bool radar = true;         // 레이더·초음파
    public bool wrap = true;          // 래핑(글자·청록 줄)

    [Header("휠")]
    public WheelStyle wheelStyle = WheelStyle.Split5;

    [Header("움직임")]
    [Tooltip("켜면 이 오브젝트가 움직인 만큼 바퀴 회전·조향을 자동 계산")]
    public bool autoFromMotion = true;
    [Tooltip("m/s (autoFromMotion 끄면 직접 넣기)")] public float speed;
    [Tooltip("앞바퀴 각(도), +면 오른쪽")] public float steerAngle;
    public float wheelRadius = 0.357f;
    public float wheelBase = 3.005f;
    public float maxSteer = 35f;
    public float steeringRatio = 15f;
    public float maxSteeringWheel = 450f;
    [Tooltip("운전대 축(유니티 좌표, 운전대→운전자). Setup이 데이터에서 채움")]
    public Vector3 steeringAxis = new Vector3(0f, 0.375f, -0.927f);

    [Header("라이다")]
    [Tooltip("보이는 회전 속도(rpm). 실제(600rpm)는 너무 빨라 보기 어려움")]
    public float lidarRpm = 120f;
    public bool lidarSpin = true;

    [Header("유리")]
    [Tooltip("운전석 시점이면 켜기 — 유리를 거의 투명하게")]
    public bool driverView = false;
    [Range(0f, 1f)] public float glassAlphaOutside = 0.55f;
    [Range(0f, 1f)] public float glassAlphaDriver = 0.10f;

    [Header("단축키 (1~6 그룹, 0 전체, 7 휠, 8 스캔 연출, 9 운전석 유리)")]
    public bool hotkeys = true;

    public static readonly string[] GroupNames =
        { "Sensors_RoofTower", "Sensors_CornerLidar", "Sensors_FenderPods", "Sensors_Cameras", "Sensors_Radar", "Sensors_Wrap" };

    readonly Dictionary<string, Transform> _groups = new Dictionary<string, Transform>();
    readonly List<Transform> _spins = new List<Transform>();
    readonly Transform[] _steer = new Transform[4];
    readonly Transform[] _wheel = new Transform[4];
    readonly List<GameObject>[] _rims = { new List<GameObject>(), new List<GameObject>(), new List<GameObject>(), new List<GameObject>() };
    Transform _steeringWheel;
    Renderer _glass; MaterialPropertyBlock _mpb;
    Quaternion _swBase = Quaternion.identity;
    float _spinDeg, _lidarDeg, _smoothSteer;
    Vector3 _lastPos; Quaternion _lastRot; bool _hasLast;
    bool _cached;

    public Transform DriverEye { get; private set; }
    public PNUAVSensorFX SensorFX => GetComponent<PNUAVSensorFX>();

    void Awake() { Cache(); Apply(); }

    void OnValidate()
    {
        _cached = false;
        Cache(); Apply();
    }

    void Cache()
    {
        if (_cached) return;
        _groups.Clear(); _spins.Clear();
        foreach (var l in _rims) l.Clear();
        foreach (Transform t in GetComponentsInChildren<Transform>(true))
        {
            string n = t.name;
            if (System.Array.IndexOf(GroupNames, n) >= 0) _groups[n] = t;
            else if (n.EndsWith("_Spin")) _spins.Add(t);
            else if (n == "SteeringWheel") { _steeringWheel = t; _swBase = t.localRotation; }
            else if (n == "DriverEye") DriverEye = t;
            else if (n == "Glass") _glass = t.GetComponent<Renderer>();
            else if (n.StartsWith("Steer_")) _steer[Idx(n.Substring(6))] = t;
            else if (n.StartsWith("Wheel_")) _wheel[Idx(n.Substring(6))] = t;
            else if (n.StartsWith("RimA_")) _rims[0].Add(t.gameObject);
            else if (n.StartsWith("RimB_")) _rims[1].Add(t.gameObject);
            else if (n.StartsWith("RimC_")) _rims[2].Add(t.gameObject);
            else if (n.StartsWith("RimD_")) _rims[3].Add(t.gameObject);
        }
        _cached = true;
    }

    static int Idx(string s)
    {
        switch (s) { case "FL": return 0; case "FR": return 1; case "RL": return 2; default: return 3; }
    }

    /// <summary>인스펙터 값대로 그룹·휠 적용</summary>
    public void Apply()
    {
        Cache();   // 에디터(프리팹 만들 때)에서도 휠·그룹 목록을 먼저 채움
        bool[] on = { roofTower, cornerLidar, fenderPods, cameras, radar, wrap };
        for (int i = 0; i < GroupNames.Length; i++)
            if (_groups.TryGetValue(GroupNames[i], out var t) && t && t.gameObject.activeSelf != on[i])
                t.gameObject.SetActive(on[i]);
        for (int s = 0; s < _rims.Length; s++)
            foreach (var g in _rims[s])
                if (g && g.activeSelf != (s == (int)wheelStyle)) g.SetActive(s == (int)wheelStyle);
        ApplyGlass();
    }

    void ApplyGlass()
    {
        if (!_glass || !_glass.sharedMaterial) return;
        if (_mpb == null) _mpb = new MaterialPropertyBlock();
        var m = _glass.sharedMaterial;
        string prop = m.HasProperty("_BaseColor") ? "_BaseColor" : "_Color";
        if (!m.HasProperty(prop)) return;
        Color c = m.GetColor(prop);
        c.a = driverView ? glassAlphaDriver : glassAlphaOutside;
        _glass.GetPropertyBlock(_mpb);
        _mpb.SetColor(prop, c);
        _glass.SetPropertyBlock(_mpb);
    }

    public void SetDriverView(bool on) { driverView = on; ApplyGlass(); }

    public bool GetGroup(string group)
    {
        switch (Norm(group))
        {
            case "Sensors_RoofTower": return roofTower;
            case "Sensors_CornerLidar": return cornerLidar;
            case "Sensors_FenderPods": return fenderPods;
            case "Sensors_Cameras": return cameras;
            case "Sensors_Radar": return radar;
            case "Sensors_Wrap": return wrap;
        }
        return false;
    }

    /// <summary>그룹 켜기/끄기. 이름은 "RoofTower" 또는 "Sensors_RoofTower" 둘 다 됨</summary>
    public void SetGroup(string group, bool on)
    {
        switch (Norm(group))
        {
            case "Sensors_RoofTower": roofTower = on; break;
            case "Sensors_CornerLidar": cornerLidar = on; break;
            case "Sensors_FenderPods": fenderPods = on; break;
            case "Sensors_Cameras": cameras = on; break;
            case "Sensors_Radar": radar = on; break;
            case "Sensors_Wrap": wrap = on; break;
        }
        Apply();
    }

    public void ToggleGroup(string group) { SetGroup(group, !GetGroup(group)); }

    /// <summary>센서 전부 켜기/끄기(래핑 포함)</summary>
    public void SetAllSensors(bool on)
    {
        roofTower = cornerLidar = fenderPods = cameras = radar = wrap = on;
        Apply();
    }

    public void SetWheelStyle(int style) { wheelStyle = (WheelStyle)Mathf.Clamp(style, 0, _rims.Length - 1); Apply(); }
    public void NextWheelStyle() { SetWheelStyle(((int)wheelStyle + 1) % _rims.Length); }

    static string Norm(string g) { return g.StartsWith("Sensors_") ? g : "Sensors_" + g; }

    [ContextMenu("센서 전부 켜기")] void CtxAllOn() { SetAllSensors(true); }
    [ContextMenu("센서 전부 끄기")] void CtxAllOff() { SetAllSensors(false); }
    [ContextMenu("다음 휠 스타일")] void CtxNextWheel() { NextWheelStyle(); }

    void Update()
    {
        Cache();
        float dt = Time.deltaTime;
        if (dt <= 0f) return;

        if (autoFromMotion)
        {
            if (_hasLast)
            {
                Vector3 v = (transform.position - _lastPos) / dt;
                float fwd = Vector3.Dot(v, transform.forward);
                float yaw = Mathf.DeltaAngle(_lastRot.eulerAngles.y, transform.rotation.eulerAngles.y) / dt; // 도/초
                speed = fwd;
                float target = 0f;
                if (Mathf.Abs(fwd) > 0.3f)
                    target = Mathf.Atan(wheelBase * (yaw * Mathf.Deg2Rad) / fwd) * Mathf.Rad2Deg;
                _smoothSteer = Mathf.Lerp(_smoothSteer, Mathf.Clamp(target, -maxSteer, maxSteer), 1f - Mathf.Exp(-6f * dt));
                steerAngle = _smoothSteer;
            }
            _lastPos = transform.position; _lastRot = transform.rotation; _hasLast = true;
        }

        // 바퀴 굴림(+X 축: 위쪽이 앞으로) · 앞바퀴 조향(+Y)
        _spinDeg = Mathf.Repeat(_spinDeg + speed * dt / Mathf.Max(0.05f, wheelRadius) * Mathf.Rad2Deg, 360f);
        for (int i = 0; i < 4; i++)
        {
            if (_wheel[i]) _wheel[i].localRotation = Quaternion.Euler(_spinDeg, 0f, 0f);
            if (_steer[i]) _steer[i].localRotation = i < 2 ? Quaternion.Euler(0f, steerAngle, 0f) : Quaternion.identity;
        }
        // 운전대: 오른쪽으로 꺾으면 운전자 기준 시계 방향
        if (_steeringWheel)
        {
            float sw = Mathf.Clamp(steerAngle * steeringRatio, -maxSteeringWheel, maxSteeringWheel);
            _steeringWheel.localRotation = _swBase * Quaternion.AngleAxis(sw, steeringAxis.normalized);
        }
        // 라이다 회전(켜진 그룹만 보임)
        if (lidarSpin)
        {
            _lidarDeg = Mathf.Repeat(_lidarDeg + lidarRpm * 6f * dt, 360f);
            foreach (var s in _spins) if (s) s.localRotation = Quaternion.Euler(0f, _lidarDeg, 0f);
        }

        if (hotkeys) HandleKeys();
    }

    void HandleKeys()
    {
        for (int d = 1; d <= 6; d++) if (KeyDown(d)) ToggleGroup(GroupNames[d - 1]);
        if (KeyDown(0)) SetAllSensors(!(roofTower || cornerLidar || fenderPods || cameras || radar || wrap));
        if (KeyDown(7)) NextWheelStyle();
        if (KeyDown(8)) { var fx = SensorFX; if (fx) fx.enabled = !fx.enabled; }
        if (KeyDown(9)) SetDriverView(!driverView);
    }

    static bool KeyDown(int digit)
    {
#if ENABLE_INPUT_SYSTEM
        var kb = UnityEngine.InputSystem.Keyboard.current;
        if (kb != null)
        {
            UnityEngine.InputSystem.Key k;
            switch (digit)
            {
                case 1: k = UnityEngine.InputSystem.Key.Digit1; break;
                case 2: k = UnityEngine.InputSystem.Key.Digit2; break;
                case 3: k = UnityEngine.InputSystem.Key.Digit3; break;
                case 4: k = UnityEngine.InputSystem.Key.Digit4; break;
                case 5: k = UnityEngine.InputSystem.Key.Digit5; break;
                case 6: k = UnityEngine.InputSystem.Key.Digit6; break;
                case 7: k = UnityEngine.InputSystem.Key.Digit7; break;
                case 8: k = UnityEngine.InputSystem.Key.Digit8; break;
                case 9: k = UnityEngine.InputSystem.Key.Digit9; break;
                default: k = UnityEngine.InputSystem.Key.Digit0; break;
            }
            if (kb[k].wasPressedThisFrame) return true;
        }
#endif
#if ENABLE_LEGACY_INPUT_MANAGER
        if (Input.GetKeyDown(KeyCode.Alpha0 + digit)) return true;
#endif
        return false;
    }
}
