// PNU01 센서 연출 — 설명 장면용. 켜진 센서 그룹만 표시
// 라이다: 회전하는 레이저 부채(실제 레이캐스트로 닿은 곳까지) · 카메라: 시야 사각뿔 · 레이더: 부채꼴
using System.Collections.Generic;
using UnityEngine;

[RequireComponent(typeof(PNUAVCar))]
public class PNUAVSensorFX : MonoBehaviour
{
    [System.Serializable] public class Anchor { public string name; public string group; public string type; public float[] pos; public float[] dir; }
    [System.Serializable] class Data { public Anchor[] anchors; }

    [Tooltip("pnu01_data.json (Setup이 자동으로 넣음)")] public TextAsset data;
    public Color lidarColor = new Color(0.10f, 0.83f, 0.77f, 0.55f);
    public Color cameraColor = new Color(0.35f, 0.65f, 1f, 0.45f);
    public Color radarColor = new Color(1f, 0.72f, 0.25f, 0.45f);
    [Header("라이다")] public int raysPerLidar = 24; public float topLidarRange = 40f; public float smallLidarRange = 15f;
    public float verticalFovUp = 10f, verticalFovDown = 22f; public float sweepRpm = 60f;
    [Tooltip("레이캐스트 대상 레이어(자기 차는 자동 제외)")] public LayerMask hitMask = ~0;
    [Header("카메라·레이더")] public float cameraLength = 3f; public float cameraHFov = 60f, cameraVFov = 38f;
    public float radarLength = 8f, radarFov = 40f;
    public float lineWidth = 0.012f;

    class Item { public Anchor a; public Transform t; public List<LineRenderer> lines = new List<LineRenderer>(); }
    readonly List<Item> _items = new List<Item>();
    PNUAVCar _car; Material _mat; Transform _root; float _sweep;
    Collider[] _own;

    void OnEnable()
    {
        _car = GetComponent<PNUAVCar>();
        _own = GetComponentsInChildren<Collider>(true);
        Build();
    }

    void OnDisable()
    {
        if (_root) Destroy(_root.gameObject);
        _items.Clear();
    }

    void Build()
    {
        if (data == null) return;
        var d = JsonUtility.FromJson<Data>(data.text);
        if (d == null || d.anchors == null) return;
        _mat = new Material(Shader.Find("Sprites/Default"));
        _root = new GameObject("SensorFX_Lines").transform;
        _root.SetParent(transform, false);
        var fx = transform.Find("FX");
        foreach (var a in d.anchors)
        {
            var it = new Item { a = a };
            it.t = fx ? fx.Find(a.name) : null;
            int n = a.type == "lidar" ? raysPerLidar : (a.type == "camera" ? 8 : 4);
            Color c = a.type == "lidar" ? lidarColor : (a.type == "camera" ? cameraColor : radarColor);
            for (int i = 0; i < n; i++) it.lines.Add(MakeLine(a.name + "_" + i, c));
            _items.Add(it);
        }
    }

    LineRenderer MakeLine(string n, Color c)
    {
        var g = new GameObject(n); g.transform.SetParent(_root, false);
        var l = g.AddComponent<LineRenderer>();
        l.sharedMaterial = _mat; l.positionCount = 2; l.useWorldSpace = true;
        l.widthMultiplier = lineWidth; l.startColor = c; l.endColor = new Color(c.r, c.g, c.b, c.a * 0.15f);
        l.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; l.receiveShadows = false;
        return l;
    }

    Vector3 P(Anchor a, Transform t) { return t ? t.position : transform.TransformPoint(new Vector3(a.pos[0], a.pos[1], a.pos[2])); }
    Vector3 D(Anchor a) { return transform.TransformDirection(new Vector3(a.dir[0], a.dir[1], a.dir[2]).normalized); }

    void LateUpdate()
    {
        if (_items.Count == 0) return;
        _sweep = Mathf.Repeat(_sweep + sweepRpm * 6f * Time.deltaTime, 360f);
        foreach (var it in _items)
        {
            bool on = _car && _car.GetGroup(it.a.group);
            foreach (var l in it.lines) if (l.enabled != on) l.enabled = on;
            if (!on) continue;
            Vector3 o = P(it.a, it.t);
            if (it.a.type == "lidar") Lidar(it, o);
            else if (it.a.type == "camera") Frustum(it, o, D(it.a), cameraHFov, cameraVFov, cameraLength);
            else Frustum(it, o, D(it.a), radarFov, 6f, radarLength);
        }
    }

    void Lidar(Item it, Vector3 o)
    {
        bool top = it.a.name.Contains("Top");
        float range = top ? topLidarRange : smallLidarRange;
        Vector3 up = transform.up;
        Vector3 baseDir = top ? transform.forward : D(it.a);
        float az = _sweep * (top ? 1f : 2f);
        int n = it.lines.Count;
        for (int i = 0; i < n; i++)
        {
            float el = Mathf.Lerp(-verticalFovDown, verticalFovUp, n > 1 ? i / (float)(n - 1) : 0.5f);
            float a = top ? az : Mathf.Sin(az * Mathf.Deg2Rad) * 60f;   // 작은 라이다는 앞뒤로 훑음
            Vector3 dir = Quaternion.AngleAxis(a, up) * baseDir;
            Vector3 side = Vector3.Cross(up, dir).normalized;
            dir = Quaternion.AngleAxis(-el, side) * dir;
            Vector3 end = o + dir * range;
            if (Physics.Raycast(o, dir, out var hit, range, hitMask, QueryTriggerInteraction.Ignore) && !IsOwn(hit.collider))
                end = hit.point;
            it.lines[i].SetPosition(0, o); it.lines[i].SetPosition(1, end);
        }
    }

    void Frustum(Item it, Vector3 o, Vector3 dir, float h, float v, float len)
    {
        Vector3 up = transform.up; Vector3 side = Vector3.Cross(up, dir).normalized;
        if (side.sqrMagnitude < 0.01f) side = transform.right;
        Vector3 vup = Vector3.Cross(dir, side).normalized;
        Vector3[] c = new Vector3[4];
        int k = 0;
        foreach (var sx in new[] { -1, 1 })
            foreach (var sy in new[] { -1, 1 })
                c[k++] = o + (Quaternion.AngleAxis(sx * h * 0.5f, vup) * (Quaternion.AngleAxis(sy * v * 0.5f, side) * dir)) * len;
        // 꼭짓점→네 모서리 + 먼 면 테두리
        int n = it.lines.Count;
        for (int i = 0; i < n; i++)
        {
            Vector3 a, b;
            if (i < 4) { a = o; b = c[i]; }
            else { int[] ord = { 0, 1, 3, 2 }; a = c[ord[i - 4]]; b = c[ord[(i - 3) % 4]]; }
            it.lines[i].SetPosition(0, a); it.lines[i].SetPosition(1, b);
        }
    }

    bool IsOwn(Collider c)
    {
        if (_own == null) return false;
        foreach (var x in _own) if (x == c) return true;
        return false;
    }
}
