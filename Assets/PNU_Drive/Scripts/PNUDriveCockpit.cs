// PNU_Drive 자차 테슬라 모델 3 운전석(런타임) — PNU_Map3 의 PNUCockpit(v14)을 PNUDriveEgo 에 맞춰 옮김
// - 운전대: 경로 방향이 바뀌는 만큼(앞뒤 12m) → 앞바퀴 각(축거) → 운전대(조향비 15, 최대 ±430도). 왼쪽으로 돌면 운전자가 볼 때 반시계
// - 바퀴: 달린 거리만큼 굴러감
// - 가운데 화면: 고정 그림(PNU_Drive/Textures/Tesla_Screen_Drive.png) 위에 속도·제한속도·남은 거리 숫자와 지도 위 내 위치(▲)
// - 유리: 운전석 시점에선 거의 투명(10%), 밖에서 볼 땐 짙게(60%). 지붕 유리는 60% / 80%
// - 길안내(PNUDriveNav): 화면 위 카드(돌 곳 화살표·길 이름 = Tesla_Nav.png 칸) + 남은 거리 글자, 깜빡이 초록 화살표, 차 깜빡이 등(주황)
// PNUDriveBuilder 가 자차 뿌리에 붙이고 연결함. 차 자리 자료: PNU_Map3/Data/tesla_ego.json (자차 로컬: x 오른쪽, y 위, z 앞)
// 이 경로 자료: PNU_Drive/Data/tesla_drive.json (화면 지도 투영, 10m 마다 제한속도)
using System;
using UnityEngine;

namespace PNU.Drive
{
    [Serializable] public class TeslaSpecScreen { public float[] origin, u, v, normal; }
    [Serializable] public class TeslaSpecWheel { public float[] center; public float radius; }
    [Serializable] public class TeslaSpecWheels { public TeslaSpecWheel Tesla_WheelsFront, Tesla_WheelsRear; }
    [Serializable] public class TeslaSpecMap { public float x0, n0, scale, px0, py0; }
    [Serializable] public class TeslaSpecDigits { public string chars; public int cells; }
    [Serializable] public class TeslaSpecTexts { public float[] speed, limit, remain; }
    [Serializable] public class TeslaSpecMat { public string name, tex, kind; public float[] color; public float alpha, smooth, metal; public bool emission; }
    [Serializable]
    public class TeslaSpec
    {
        public float[] eye, wheel_pivot, wheel_axis; public TeslaSpecScreen screen; public TeslaSpecWheels wheels;
        public int[] screen_px; public TeslaSpecMap map; public TeslaSpecDigits digits; public TeslaSpecTexts texts; public TeslaSpecMat[] materials;
        public static Vector3 V(float[] a) { return a != null && a.Length >= 3 ? new Vector3(a[0], a[1], a[2]) : Vector3.zero; }
    }
    [Serializable] public class TeslaDrive { public TeslaSpecMap map; public float lim_step = 10f; public int[] lim; public float length; public TeslaAtlas atlas; }

    public class PNUDriveCockpit : MonoBehaviour
    {
        public PNUDriveEgo ego;
        public TextAsset teslaJson;                 // PNU_Map3/Data/tesla_ego.json
        public TextAsset driveJson;                 // PNU_Drive/Data/tesla_drive.json (없으면 지도·제한속도 빼고 그림)
        public Transform steeringWheel, wheelsFront, wheelsRear;
        public Renderer glassRenderer;
        public Material digitMaterial;
        public PNUDriveNav nav;                     // 길안내(카드·깜빡이)
        public Material navMaterial;                // Tesla_Nav.png(투명) 바른 안 비치는 재질
        public Color indicatorOn = new Color(1f, 0.55f, 0.06f);
        [UnityEngine.Range(0, 1)] public float glassInside = 0.1f, glassOutside = 0.6f;
        public float wheelBase = 2.86f, steerRatio = 15f;
        public Color textColor = new Color(0.09f, 0.1f, 0.11f), markerColor = new Color(0.24f, 0.42f, 0.88f);

        TeslaSpec T; TeslaDrive Dr; TeslaSpecMap map; Vector3 axis;
        Quaternion wheelRest = Quaternion.identity, wfRest = Quaternion.identity, wrRest = Quaternion.identity;
        float wheelAng; Material[] glassMats; int glassMode = -1; float nextUI;
        DigitRow speedRow, limitRow, remainRow, markRow;
        Quads navQ; int navCardShown = -2; string navDistShown = null; int sigShown = -9;
        Material[] indL, indR; Color[] indLc, indRc; int lampShown = -9;

        void Start()
        {
            if (teslaJson == null) { enabled = false; return; }
            T = JsonUtility.FromJson<TeslaSpec>(teslaJson.text);
            if (driveJson != null) Dr = JsonUtility.FromJson<TeslaDrive>(driveJson.text);
            map = Dr != null && Dr.map != null && Dr.map.scale > 0f ? Dr.map : null;   // v14 지도 투영은 그 그림 전용이라 안 씀
            if (ego == null) ego = FindObjectOfType<PNUDriveEgo>();
            axis = TeslaSpec.V(T.wheel_axis).normalized; if (axis.sqrMagnitude < 0.5f) axis = Vector3.back;
            if (steeringWheel != null) wheelRest = steeringWheel.localRotation;
            if (wheelsFront != null) wfRest = wheelsFront.localRotation;
            if (wheelsRear != null) wrRest = wheelsRear.localRotation;
            if (glassRenderer != null) glassMats = glassRenderer.materials;                // 인스턴스(에셋 재질은 그대로)
            if (digitMaterial != null && T.screen != null && T.texts != null)
            {
                speedRow = new DigitRow(this, "Screen_Speed", T, T.texts.speed, 3, textColor);
                limitRow = new DigitRow(this, "Screen_Limit", T, T.texts.limit, 3, textColor);
                remainRow = new DigitRow(this, "Screen_Remain", T, T.texts.remain, 5, new Color(0.36f, 0.38f, 0.42f));
                if (map != null) markRow = new DigitRow(this, "Screen_MapMarker", T, new float[] { 0, 0, 30, 0 }, 1, markerColor);
            }
            if (nav == null) nav = FindObjectOfType<PNUDriveNav>();
            if (navMaterial != null && T.screen != null && Dr != null && Dr.atlas != null && Dr.atlas.screen != null && Dr.atlas.screen.card != null) navQ = new Quads(this, "Screen_Nav", T, navMaterial, 11);
            FindIndicators();
        }

        /// <summary>차 깜빡이 등 재질(TM_Light_indicator_lf/lr = 왼쪽, rf/rr = 오른쪽) — 인스턴스로 바꿔 켜고 끔</summary>
        void FindIndicators()
        {
            var L = new System.Collections.Generic.List<Material>(); var R = new System.Collections.Generic.List<Material>();
            foreach (var r in GetComponentsInChildren<Renderer>(true))
            {
                var sm = r.sharedMaterials; bool has = false;
                foreach (var m in sm) if (m != null && m.name.Contains("indicator")) has = true;
                if (!has) continue;
                var mats = r.materials;
                for (int i = 0; i < mats.Length; i++)
                {
                    if (mats[i] == null || !mats[i].name.Contains("indicator")) continue;
                    mats[i].EnableKeyword("_EMISSION"); mats[i].SetColor("_EmissionColor", Color.black);
                    (mats[i].name.Contains("indicator_l") ? L : R).Add(mats[i]);
                }
            }
            indL = L.ToArray(); indR = R.ToArray();
            indLc = new Color[indL.Length]; indRc = new Color[indR.Length];
            for (int i = 0; i < indL.Length; i++) indLc[i] = indL[i].HasProperty("_BaseColor") ? indL[i].GetColor("_BaseColor") : Color.white;
            for (int i = 0; i < indR.Length; i++) indRc[i] = indR[i].HasProperty("_BaseColor") ? indR[i].GetColor("_BaseColor") : Color.white;
        }

        void SetLamps(Material[] ms, Color[] c0, bool on)
        {
            if (ms == null) return;
            for (int i = 0; i < ms.Length; i++)
            {
                var m = ms[i]; if (m == null) continue;
                Color c = on ? new Color(indicatorOn.r, indicatorOn.g, indicatorOn.b, c0[i].a) : c0[i];
                if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c);
                m.SetColor("_EmissionColor", on ? indicatorOn * 4f : Color.black);
            }
        }

        float Heading(float s) { Vector3 a = ego.PosAt(s - 6f), b = ego.PosAt(s + 6f); return Mathf.Atan2(b.z - a.z, b.x - a.x); }

        /// <summary>경로 s(m)의 제한속도(km/h), 모르면 0</summary>
        public int Limit(float s)
        {
            if (Dr == null || Dr.lim == null || Dr.lim.Length == 0) return 0;
            int k = Mathf.Clamp(Mathf.RoundToInt(s / Mathf.Max(1f, Dr.lim_step)), 0, Dr.lim.Length - 1);
            return Dr.lim[k];
        }

        void LateUpdate()
        {
            if (T == null || ego == null || !ego.Ready) return;
            float s = ego.Dist, dt = Mathf.Min(0.1f, Time.deltaTime);
            // 운전대: 앞뒤 12m 방향 차 → 앞바퀴 각 → 운전대 각(라디안, 최대 ±7.5 = ±430도)
            float dy = Mathf.DeltaAngle(Heading(s - 6f) * Mathf.Rad2Deg, Heading(s + 6f) * Mathf.Rad2Deg) * Mathf.Deg2Rad;
            float tgt = Mathf.Clamp(steerRatio * Mathf.Atan(wheelBase * dy / 12f), -7.5f, 7.5f);
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
            int gm = ego.camMode == PNUDriveEgo.CamMode.Driver ? 1 : 0;
            if (gm != glassMode && glassMats != null)
            {
                glassMode = gm;
                foreach (var m in glassMats)
                {
                    if (m == null) continue;
                    float a = m.name.Contains("Roof") ? (gm == 1 ? 0.6f : 0.8f) : (gm == 1 ? glassInside : glassOutside);
                    var c = m.HasProperty("_BaseColor") ? m.GetColor("_BaseColor") : m.color; c.a = a;
                    if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c);
                    if (m.HasProperty("_Color")) m.SetColor("_Color", c);
                }
            }
            // 화면 숫자(0.25초마다)
            if (speedRow != null && Time.time >= nextUI)
            {
                nextUI = Time.time + 0.25f;
                speedRow.Set(Mathf.RoundToInt(ego.Speed * 3.6f).ToString());
                int lim = ego.Limit(s); if (lim <= 0) lim = Limit(s);                       // path.json 제한속도(없으면 tesla_drive.json)
                limitRow.Set(lim > 0 ? lim.ToString() : "");
                remainRow.Set((Mathf.Max(0f, ego.Length - s) / 1000f).ToString("0.0"));
                if (markRow != null)
                {
                    Vector3 p = ego.PosAt(s);
                    float px = map.px0 + (p.x - map.x0) * map.scale, py = map.py0 - (p.z - map.n0) * map.scale;
                    Vector3 a = ego.PosAt(s - 8f), b = ego.PosAt(s + 8f);
                    markRow.Place(px, py, Mathf.Atan2(b.x - a.x, b.z - a.z));                   // 북쪽 위 지도: 진행 방향으로 돌림
                    markRow.Set("▲");
                }
            }
            // 길안내 카드·남은 거리·깜빡이 화살표(화면), 차 깜빡이 등
            if (nav != null && nav.Ready)
            {
                int lamp = nav.BlinkLamp ? nav.BlinkSide : 0;
                if (navQ != null)
                {
                    var A = Dr.atlas; var sc = A.screen; bool dirty = false;
                    int ci = nav.Next != null && A.cards != null && nav.NextIndex * 4 + 3 < A.cards.Length ? nav.NextIndex : -1;
                    string dist = ci >= 0 ? PNUDriveNav.DistText(nav.NextDist) : "";
                    if (ci != navCardShown)
                    {
                        navCardShown = ci; dirty = true;
                        if (ci < 0) navQ.Hide(0);
                        else { int c = ci * 4; navQ.Set(0, sc.card[0], sc.card[1], sc.card[2], sc.card[3], A, A.cards[c], A.cards[c + 1], A.cards[c + 2], A.cards[c + 3], 0.0036f); }
                    }
                    if (dist != navDistShown)
                    {
                        navDistShown = dist; dirty = true;
                        var g = A.glyph; float hpx = sc.dist[2], k = hpx / Mathf.Max(1f, g.h), x = sc.dist[0];
                        for (int i = 0; i < 8; i++)
                        {
                            int gi = i < dist.Length ? g.chars.IndexOf(dist[i]) : -1;
                            if (gi < 0) { navQ.Hide(1 + i); continue; }
                            float w = g.w * k;
                            navQ.Set(1 + i, x, sc.dist[1] - hpx * 0.5f, x + w, sc.dist[1] + hpx * 0.5f, A, g.x0 + gi * g.w, g.y0, g.w, g.h, 0.0046f);
                            x += (g.adv != null && gi < g.adv.Length ? g.adv[gi] : g.w) * k;
                        }
                    }
                    if (lamp != sigShown)
                    {
                        sigShown = lamp; dirty = true;
                        if (lamp < 0 && A.sigL != null) { float h = sc.sigL[2] * 0.5f; navQ.Set(9, sc.sigL[0] - h, sc.sigL[1] - h, sc.sigL[0] + h, sc.sigL[1] + h, A, A.sigL[0], A.sigL[1], A.sigL[2], A.sigL[3], 0.0046f); } else navQ.Hide(9);
                        if (lamp > 0 && A.sigR != null) { float h = sc.sigR[2] * 0.5f; navQ.Set(10, sc.sigR[0] - h, sc.sigR[1] - h, sc.sigR[0] + h, sc.sigR[1] + h, A, A.sigR[0], A.sigR[1], A.sigR[2], A.sigR[3], 0.0046f); } else navQ.Hide(10);
                    }
                    if (dirty) navQ.Apply();
                }
                if (lamp != lampShown) { lampShown = lamp; SetLamps(indL, indLc, lamp < 0); SetLamps(indR, indRc, lamp > 0); }
            }
        }

        /// <summary>화면 평면 위 사각형 여러 개(길안내 글자판 칸)</summary>
        class Quads
        {
            readonly Mesh mesh; readonly TeslaSpec T; readonly Vector3 O, U, Vv, N; readonly Vector3[] verts; readonly Vector2[] uvs;
            public Quads(PNUDriveCockpit owner, string name, TeslaSpec t, Material mat, int n)
            {
                T = t; O = TeslaSpec.V(t.screen.origin); U = TeslaSpec.V(t.screen.u); Vv = TeslaSpec.V(t.screen.v); N = TeslaSpec.V(t.screen.normal).normalized;
                var go = new GameObject(name); go.transform.SetParent(owner.transform, false);
                mesh = new Mesh(); mesh.name = name; verts = new Vector3[n * 4]; uvs = new Vector2[n * 4];
                var tris = new int[n * 6];
                for (int i = 0; i < n; i++) { int v = i * 4, k = i * 6; tris[k] = v; tris[k + 1] = v + 1; tris[k + 2] = v + 2; tris[k + 3] = v; tris[k + 4] = v + 2; tris[k + 5] = v + 3; verts[v] = verts[v + 1] = verts[v + 2] = verts[v + 3] = O; }
                mesh.vertices = verts; mesh.uv = uvs; mesh.triangles = tris;
                go.AddComponent<MeshFilter>().sharedMesh = mesh;
                var mr = go.AddComponent<MeshRenderer>(); mr.sharedMaterial = mat; mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            }
            Vector3 P(float x, float y, float off)
            {
                float W = T.screen_px != null && T.screen_px.Length > 1 ? T.screen_px[0] : 1024f, H = T.screen_px != null && T.screen_px.Length > 1 ? T.screen_px[1] : 804f;
                return O + U * (x / W) + Vv * (1f - y / H) + N * off;
            }
            /// <summary>화면 픽셀 사각형(x0,y0 왼위 ~ x1,y1 오른아래)에 글자판 칸(ax,ay 왼위, aw×ah)</summary>
            public void Set(int i, float x0, float y0, float x1, float y1, TeslaAtlas A, float ax, float ay, float aw, float ah, float off)
            {
                int v = i * 4;
                verts[v] = P(x0, y0, off); verts[v + 1] = P(x1, y0, off); verts[v + 2] = P(x1, y1, off); verts[v + 3] = P(x0, y1, off);
                float u0 = ax / A.w, u1 = (ax + aw) / A.w, vt = 1f - ay / A.h, vb = 1f - (ay + ah) / A.h;
                uvs[v] = new Vector2(u0, vt); uvs[v + 1] = new Vector2(u1, vt); uvs[v + 2] = new Vector2(u1, vb); uvs[v + 3] = new Vector2(u0, vb);
            }
            public void Hide(int i) { int v = i * 4; verts[v] = verts[v + 1] = verts[v + 2] = verts[v + 3] = O; }
            public void Apply() { mesh.vertices = verts; mesh.uv = uvs; mesh.RecalculateBounds(); }
        }

        /// <summary>화면 평면 위 글자 줄(글자판 Tesla_Digits.png 의 칸을 사각형 여러 개로)</summary>
        class DigitRow
        {
            readonly Mesh mesh; readonly TeslaSpec T; readonly int n; readonly float hPx; readonly int align;
            readonly Vector3 O, U, Vv, N; float px, py, rot; string cur = null;
            readonly Vector3[] verts; readonly Vector2[] uvs;
            public DigitRow(PNUDriveCockpit owner, string name, TeslaSpec t, float[] spec, int maxChars, Color col)
            {
                T = t; n = maxChars; px = spec[0]; py = spec[1]; hPx = spec[2]; align = (int)spec[3];
                O = TeslaSpec.V(t.screen.origin); U = TeslaSpec.V(t.screen.u); Vv = TeslaSpec.V(t.screen.v); N = TeslaSpec.V(t.screen.normal).normalized;
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
