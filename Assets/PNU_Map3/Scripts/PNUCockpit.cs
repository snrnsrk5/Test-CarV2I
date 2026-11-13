// 자차 테슬라 모델 3 (런타임)
// - 운전대: 경로 곡률을 보고 저절로 돌아감(자율주행). 왼쪽으로 돌면 운전자가 볼 때 반시계
// - 바퀴: 달린 거리만큼 굴러감
// - 가운데 화면: 고정 그림(Tesla_Screen.png) 위에 속도·제한속도·남은 거리 숫자와 지도 위 내 위치(▲)를 그림
// - 유리: 운전석 보기에선 거의 투명, 밖에서 볼 땐 짙게
// PNUSceneBuilder 가 Ego_Car 에 붙이고 연결함. 좌표·화면 자리는 Data/tesla_ego.json (자차 로컬: x 오른쪽, y 위, z 앞)
using System.Collections.Generic;
using UnityEngine;

namespace PNU
{
    [System.Serializable] public class TeslaScreen { public float[] origin, u, v, normal; }
    [System.Serializable] public class TeslaWheel { public float[] center; public float radius; }
    [System.Serializable] public class TeslaWheels { public TeslaWheel Tesla_WheelsFront, Tesla_WheelsRear; }
    [System.Serializable] public class TeslaMap { public float x0, n0, scale, px0, py0; }
    [System.Serializable] public class TeslaDigits { public string chars; public int cells; }
    [System.Serializable] public class TeslaTexts { public float[] speed, limit, remain; }
    [System.Serializable] public class TeslaMat { public string name, tex, kind; public float[] color; public float alpha, smooth, metal; public bool emission; }
    [System.Serializable]
    public class TeslaData
    {
        public float[] eye, wheel_pivot, wheel_axis; public TeslaScreen screen; public TeslaWheels wheels;
        public int[] screen_px; public TeslaMap map; public TeslaDigits digits; public TeslaTexts texts; public TeslaMat[] materials;
        public static Vector3 V(float[] a) { return a != null && a.Length >= 3 ? new Vector3(a[0], a[1], a[2]) : Vector3.zero; }
    }

    public class PNUCockpit : MonoBehaviour
    {
        public PNUDrivePlayer player;
        public TextAsset teslaJson;
        public Transform steeringWheel, wheelsFront, wheelsRear;
        public Renderer glassRenderer;
        public Material digitMaterial;
        [Range(0, 1)] public float glassInside = 0.1f, glassOutside = 0.6f;
        public Color textColor = new Color(0.09f, 0.1f, 0.11f), markerColor = new Color(0.24f, 0.42f, 0.88f);

        TeslaData T; Vector3 axis; Quaternion wheelRest = Quaternion.identity, wfRest = Quaternion.identity, wrRest = Quaternion.identity;
        float wheelAng; Material[] glassMats; int glassMode = -1; float nextUI;
        DigitRow speedRow, limitRow, remainRow, markRow;

        void Start()
        {
            if (teslaJson == null) { enabled = false; return; }
            T = JsonUtility.FromJson<TeslaData>(teslaJson.text);
            axis = TeslaData.V(T.wheel_axis).normalized;
            if (steeringWheel != null) wheelRest = steeringWheel.localRotation;
            if (wheelsFront != null) wfRest = wheelsFront.localRotation;
            if (wheelsRear != null) wrRest = wheelsRear.localRotation;
            if (glassRenderer != null) glassMats = glassRenderer.materials;                // 인스턴스(에셋 재질은 그대로)
            if (digitMaterial != null && T.screen != null && T.texts != null)
            {
                speedRow = new DigitRow(this, "Screen_Speed", T, T.texts.speed, 3, textColor);
                limitRow = new DigitRow(this, "Screen_Limit", T, T.texts.limit, 3, textColor);
                remainRow = new DigitRow(this, "Screen_Remain", T, T.texts.remain, 5, new Color(0.36f, 0.38f, 0.42f));
                markRow = new DigitRow(this, "Screen_MapMarker", T, new float[] { 0, 0, 30, 0 }, 1, markerColor);
            }
        }

        float Heading(float s) { Vector3 a = player.EgoPos(s - 6f), b = player.EgoPos(s + 6f); return Mathf.Atan2(b.z - a.z, b.x - a.x); }

        void LateUpdate()
        {
            if (player == null || T == null) return;
            float s = player.S, dt = Mathf.Min(0.1f, Time.deltaTime);
            // 운전대: 앞뒤 12m 방향 차 → 앞바퀴 각(축거 2.8m) → 운전대(조향비 15), 최대 ±430도
            float dy = Mathf.DeltaAngle(Heading(s - 6f) * Mathf.Rad2Deg, Heading(s + 6f) * Mathf.Rad2Deg) * Mathf.Deg2Rad;
            float tgt = Mathf.Clamp(15f * Mathf.Atan(2.8f * dy / 12f), -7.5f, 7.5f);
            wheelAng += (tgt - wheelAng) * Mathf.Min(1f, dt * 3.5f);
            if (steeringWheel != null) steeringWheel.localRotation = Quaternion.AngleAxis(-wheelAng * Mathf.Rad2Deg, axis) * wheelRest;
            // 바퀴(앞으로 굴러감 = 자차 x축 + 방향)
            if (T.wheels != null)
            {
                if (wheelsFront != null && T.wheels.Tesla_WheelsFront != null)
                    wheelsFront.localRotation = Quaternion.AngleAxis(Mathf.Repeat(s / Mathf.Max(0.1f, T.wheels.Tesla_WheelsFront.radius) * Mathf.Rad2Deg, 360f), Vector3.right) * wfRest;
                if (wheelsRear != null && T.wheels.Tesla_WheelsRear != null)
                    wheelsRear.localRotation = Quaternion.AngleAxis(Mathf.Repeat(s / Mathf.Max(0.1f, T.wheels.Tesla_WheelsRear.radius) * Mathf.Rad2Deg, 360f), Vector3.right) * wrRest;
            }
            // 유리
            int gm = player.camMode == PNUDrivePlayer.CamMode.Driver ? 1 : 0;
            if (gm != glassMode && glassMats != null)
            {
                glassMode = gm;
                foreach (var m in glassMats)
                {
                    if (m == null) continue;
                    float a = m.name.Contains("Roof") ? (gm == 1 ? 0.6f : 0.8f) : (gm == 1 ? glassInside : glassOutside);
                    var c = m.HasProperty("_BaseColor") ? m.GetColor("_BaseColor") : m.color; c.a = a;
                    if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c); m.color = c;
                }
            }
            // 화면 숫자(0.25초마다)
            if (speedRow != null && Time.time >= nextUI)
            {
                nextUI = Time.time + 0.25f;
                speedRow.Set(Mathf.RoundToInt(player.Speed * 3.6f).ToString());
                limitRow.Set(Mathf.RoundToInt(player.Limit(s)).ToString());
                remainRow.Set((Mathf.Max(0f, player.RouteLength - s) / 1000f).ToString("0.0"));
                if (T.map != null)
                {
                    Vector3 p = player.EgoPos(s);
                    float px = T.map.px0 + (p.x - T.map.x0) * T.map.scale, py = T.map.py0 - (p.z - T.map.n0) * T.map.scale;
                    Vector3 a = player.EgoPos(s - 8f), b = player.EgoPos(s + 8f);
                    markRow.Place(px, py, Mathf.Atan2(b.x - a.x, b.z - a.z));                   // 북쪽 위 지도: 진행 방향으로 돌림
                    markRow.Set("▲");
                }
            }
        }

        /// <summary>화면 평면 위 글자 줄(글자판 Tesla_Digits.png 의 칸을 사각형 여러 개로)</summary>
        class DigitRow
        {
            readonly Mesh mesh; readonly TeslaData T; readonly int n; readonly float hPx; readonly int align;
            readonly Vector3 O, U, Vv, N; float px, py, rot; string cur = null;
            readonly Vector3[] verts; readonly Vector2[] uvs;
            public DigitRow(PNUCockpit owner, string name, TeslaData t, float[] spec, int maxChars, Color col)
            {
                T = t; n = maxChars; px = spec[0]; py = spec[1]; hPx = spec[2]; align = (int)spec[3];
                O = TeslaData.V(t.screen.origin); U = TeslaData.V(t.screen.u); Vv = TeslaData.V(t.screen.v); N = TeslaData.V(t.screen.normal).normalized;
                var go = new GameObject(name); go.transform.SetParent(owner.transform, false);
                mesh = new Mesh(); mesh.name = name; verts = new Vector3[n * 4]; uvs = new Vector2[n * 4];
                var tris = new int[n * 6];
                for (int i = 0; i < n; i++) { int v = i * 4, k = i * 6; tris[k] = v; tris[k + 1] = v + 1; tris[k + 2] = v + 2; tris[k + 3] = v; tris[k + 4] = v + 2; tris[k + 5] = v + 3; }
                mesh.vertices = verts; mesh.uv = uvs; mesh.triangles = tris;
                go.AddComponent<MeshFilter>().sharedMesh = mesh;
                var mr = go.AddComponent<MeshRenderer>(); mr.sharedMaterial = owner.digitMaterial; mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                var mpb = new MaterialPropertyBlock(); mpb.SetColor("_BaseColor", col); mpb.SetColor("_Color", col); mr.SetPropertyBlock(mpb);
            }
            public void Place(float x, float y, float r) { if (Mathf.Abs(x - px) + Mathf.Abs(y - py) + Mathf.Abs(r - rot) > 0.3f) cur = null; px = x; py = y; rot = r; }
            Vector3 P(float x, float y)
            {
                float W = T.screen_px != null && T.screen_px.Length > 1 ? T.screen_px[0] : 1024f, H = T.screen_px != null && T.screen_px.Length > 1 ? T.screen_px[1] : 804f;
                return O + U * (x / W) + Vv * (1f - y / H) + N * 0.003f;
            }
            public void Set(string s)
            {
                if (s == cur) return; cur = s;
                string chars = T.digits != null && !string.IsNullOrEmpty(T.digits.chars) ? T.digits.chars : "0123456789.- ●▲";
                int cells = T.digits != null && T.digits.cells > 0 ? T.digits.cells : 16;
                float h = hPx, w = h * 0.5f, adv = h * 0.56f; int L = Mathf.Min(n, s.Length);
                float total = (L - 1) * adv + w; float x0 = align == 0 ? px - total * 0.5f + w * 0.5f : px + w * 0.5f;
                float cs = Mathf.Cos(rot), sn = Mathf.Sin(rot);
                for (int i = 0; i < n; i++)
                {
                    int v = i * 4;
                    if (i >= L) { verts[v] = verts[v + 1] = verts[v + 2] = verts[v + 3] = O; continue; }
                    int ci = chars.IndexOf(s[i]); if (ci < 0) ci = chars.IndexOf(' ');
                    float cx = x0 + i * adv, cy = py;
                    // 화면 픽셀(아래로 +y) 기준 네 모서리: 왼위, 오른위, 오른아래, 왼아래 (rot: 시계 방향 회전)
                    float[] ox = { -w * 0.5f, w * 0.5f, w * 0.5f, -w * 0.5f }, oy = { -h * 0.5f, -h * 0.5f, h * 0.5f, h * 0.5f };
                    for (int k = 0; k < 4; k++) verts[v + k] = P(cx + ox[k] * cs - oy[k] * sn, cy + ox[k] * sn + oy[k] * cs);
                    float u0 = (float)ci / cells, u1 = (float)(ci + 1) / cells;
                    uvs[v] = new Vector2(u0, 1); uvs[v + 1] = new Vector2(u1, 1); uvs[v + 2] = new Vector2(u1, 0); uvs[v + 3] = new Vector2(u0, 0);
                }
                mesh.vertices = verts; mesh.uv = uvs; mesh.RecalculateBounds();
            }
        }
    }
}
