// 5/6 주변 환경 위치 매핑(107): 실제 길 위에 겹쳐 그림
//   - 자차 차로 띠(앞 70m, 셰브런이 앞으로 흐름) — 상황으로 서야 하면 그 자리에서 끝나고 주황
//   - 차·사람 상자(카메라를 향한 네모 + 모서리 꺾쇠) + 이름·거리 글자: 보통 파랑, 경고 주황, 위험 빨강
//   - 비: 카메라 인식 상자는 주황(신뢰도 낮음), 앞차는 레이더(파랑, 거리·상대 속도) + 노면 레이더 고리
//   - 교차로 상황(V2I): 신호 상태·남은 시간 글자
// 모두 한 메쉬(Sprites/Default)로 그림. 글자는 월드 캔버스 하나(나눔바른고딕)
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UI;

namespace PNU.Drive
{
    public class PNUDriveOverlay : MonoBehaviour
    {
        public PNUDriveEgo ego; public PNUDriveTraffic traffic; public Camera cam;
        public Font font, fontBold;
        public Texture2D bandTex, ringTex;
        public bool on;
        [Header("범위")]
        public float bandAhead = 70f, carRange = 75f, pedRange = 48f;
        public float bandWidth = 2.6f;
        [NonSerialized] public int mode;                       // 0 보통, 1 비(카메라 신뢰도 낮음 + 레이더)
        [NonSerialized] public float bandStop = -1f;             // 경로 s: 여기서 띠가 끝남(정지 예정, 주황)
        [NonSerialized] public TCar hiCar; [NonSerialized] public string hiCarText; [NonSerialized] public Color hiCarColor;
        [NonSerialized] public TCar hiCar2; [NonSerialized] public string hiCar2Text; [NonSerialized] public Color hiCar2Color;
        [NonSerialized] public TPed hiPed; [NonSerialized] public string hiPedText; [NonSerialized] public Color hiPedColor;
        [NonSerialized] public TCar radarCar; [NonSerialized] public string radarText;
        [NonSerialized] public bool v2iOn; [NonSerialized] public Vector3 v2iPos; [NonSerialized] public string v2iText; [NonSerialized] public Color v2iColor;
        /// <summary>오른쪽 위 목록용: 가까운 물체(종류, 거리)</summary>
        public readonly List<KeyValuePair<string, float>> nearest = new List<KeyValuePair<string, float>>();

        Mesh bandMesh, boxMesh, ringMesh; Material bandMat, boxMat, ringMat;
        readonly List<Vector3> bv = new List<Vector3>(); readonly List<Vector2> bu = new List<Vector2>(); readonly List<Color> bc = new List<Color>(); readonly List<int> bi = new List<int>();
        readonly List<Vector3> xv = new List<Vector3>(); readonly List<Color> xc = new List<Color>(); readonly List<int> xi = new List<int>(); readonly List<Vector2> xu = new List<Vector2>();
        readonly List<Vector3> rv = new List<Vector3>(); readonly List<Color> rc = new List<Color>(); readonly List<int> ri = new List<int>(); readonly List<Vector2> ru = new List<Vector2>();
        float[] surfY; readonly RaycastHit[] hits = new RaycastHit[6];
        // 글자
        Canvas labelCanvas; readonly List<Text> labels = new List<Text>(); readonly List<Image> labelBg = new List<Image>(); int labelN; Material labelMat;
        float alpha;

        public static readonly Color CBlue = new Color(0.3f, 0.62f, 1f), CCyan = new Color(0.35f, 0.9f, 1f), COrange = new Color(1f, 0.62f, 0.15f), CRed = new Color(1f, 0.25f, 0.2f), CBand = new Color(0.25f, 0.55f, 1f);

        void Start()
        {
            if (cam == null) cam = Camera.main;
            var sh = Shader.Find("Sprites/Default");
            bandMat = new Material(sh) { name = "PNU 차로 띠", mainTexture = bandTex, renderQueue = 3010 };
            if (bandTex != null) { bandTex.wrapMode = TextureWrapMode.Repeat; }
            boxMat = new Material(sh) { name = "PNU 물체 상자", renderQueue = 3020 };
            ringMat = new Material(sh) { name = "PNU 레이더 고리", mainTexture = ringTex, renderQueue = 3015 };
            bandMesh = NewMesh("차로 띠", bandMat); boxMesh = NewMesh("물체 상자", boxMat); ringMesh = NewMesh("레이더 고리", ringMat);
            var go = new GameObject("매핑 글자", typeof(RectTransform), typeof(Canvas));
            go.transform.SetParent(transform, false);
            labelCanvas = go.GetComponent<Canvas>(); labelCanvas.renderMode = RenderMode.WorldSpace; labelCanvas.worldCamera = cam; labelCanvas.sortingOrder = 40;
            ((RectTransform)go.transform).sizeDelta = new Vector2(10, 10);
            labelMat = new Material(Shader.Find("UI/Default")) { name = "PNU 매핑 글자" }; labelMat.SetInt("unity_GUIZTestMode", (int)CompareFunction.Always);
        }

        Mesh NewMesh(string name, Material m)
        {
            var go = new GameObject(name); go.transform.SetParent(transform, false);
            var mf = go.AddComponent<MeshFilter>(); var mr = go.AddComponent<MeshRenderer>(); mr.sharedMaterial = m;
            mr.shadowCastingMode = ShadowCastingMode.Off; mr.receiveShadows = false;
            var mesh = new Mesh { name = name }; mesh.MarkDynamic(); mf.sharedMesh = mesh; return mesh;
        }

        float SurfaceY(int i)
        {
            if (surfY == null || surfY.Length != ego.PathCount) { surfY = new float[ego.PathCount]; for (int k = 0; k < surfY.Length; k++) surfY[k] = float.NaN; }
            if (i < 0) i = 0; if (i >= surfY.Length) i = surfY.Length - 1;
            if (!float.IsNaN(surfY[i])) return surfY[i];
            Vector3 p = ego.PathPoint(i);
            int n = Physics.RaycastNonAlloc(p + Vector3.up * 2.5f, Vector3.down, hits, 5f, ~(1 << PNUDriveRainBlock.Layer), QueryTriggerInteraction.Ignore);   // v17: 비 막는 상자 빼고
            float best = float.NegativeInfinity;
            for (int k = 0; k < n; k++)
            {
                var col = hits[k].collider; if (col == null) continue;
                if (ego.car != null && col.transform.IsChildOf(ego.car)) continue;
                if (hits[k].point.y > best && hits[k].point.y < p.y + 1.2f) best = hits[k].point.y;
            }
            float y = float.IsNegativeInfinity(best) ? p.y : best;
            if (n > 0) surfY[i] = y;                     // 아직 땅이 안 불러졌으면 다음에 다시
            return y;
        }

        void LateUpdate()
        {
            if (ego == null || !ego.Ready || cam == null) return;
            alpha = Mathf.MoveTowards(alpha, on ? 1f : 0f, Time.unscaledDeltaTime * 2.5f);
            bv.Clear(); bu.Clear(); bc.Clear(); bi.Clear(); xv.Clear(); xc.Clear(); xi.Clear(); xu.Clear(); rv.Clear(); rc.Clear(); ri.Clear(); ru.Clear();
            labelN = 0; nearest.Clear();
            if (alpha > 0.01f)
            {
                Band(); Boxes(); if (mode == 1) Rings(); if (v2iOn) Label(v2iPos, v2iText, v2iColor, 1.0f);
            }
            Apply(bandMesh, bv, bu, bc, bi); Apply(boxMesh, xv, xu, xc, xi); Apply(ringMesh, rv, ru, rc, ri);
            for (int k = labelN; k < labels.Count; k++) if (labels[k].transform.parent.gameObject.activeSelf) labels[k].transform.parent.gameObject.SetActive(false);
        }

        static void Apply(Mesh m, List<Vector3> v, List<Vector2> u, List<Color> c, List<int> idx)
        {
            m.Clear(); if (v.Count == 0) return;
            m.SetVertices(v); m.SetUVs(0, u); m.SetColors(c); m.SetTriangles(idx, 0); m.RecalculateBounds();
            m.bounds = new Bounds(m.bounds.center, m.bounds.size + Vector3.one * 2000f);   // 잘리지 않게
        }

        // ------------------------------------------------------------ 차로 띠
        void Band()
        {
            float s0 = ego.Dist + 3.2f, s1 = Mathf.Min(ego.Length - 0.5f, ego.Dist + bandAhead);
            bool stop = bandStop >= 0f && bandStop > s0 + 1f && bandStop < s1;
            if (stop) s1 = bandStop;
            if (s1 - s0 < 2f) return;
            Color c0 = stop ? COrange : (mode == 1 ? CCyan : CBand);
            float scroll = Time.time * 0.9f;
            int seg = Mathf.CeilToInt((s1 - s0) / 1.0f);
            for (int k = 0; k <= seg; k++)
            {
                float s = Mathf.Lerp(s0, s1, (float)k / seg);
                Vector3 p = ego.PosAt(s); Vector3 f = ego.PosAt(s + 1f) - ego.PosAt(s - 1f); f.y = 0f; f.Normalize();
                Vector3 left = new Vector3(-f.z, 0f, f.x);
                p.y = Mathf.Lerp(SurfaceY(Mathf.FloorToInt(s)), SurfaceY(Mathf.CeilToInt(s)), s - Mathf.Floor(s)) + 0.07f;
                float a = Mathf.Clamp01((s - s0) / 6f) * Mathf.Clamp01((s1 - s) / (stop ? 2f : 22f)) * 0.85f * alpha;
                int b = bv.Count;
                bv.Add(p - left * bandWidth * 0.5f); bv.Add(p + left * bandWidth * 0.5f);
                float v = scroll - (s - ego.Dist) / 6.5f;          // v17: 그림의 셰브런(V)이 아래(작은 v)를 가리키므로 앞으로 갈수록 v 가 줄게 → 화살표가 진행 쪽, 흐름도 앞으로
                bu.Add(new Vector2(0f, v)); bu.Add(new Vector2(1f, v));
                var c = new Color(c0.r, c0.g, c0.b, a); bc.Add(c); bc.Add(c);
                if (k > 0) { bi.Add(b - 2); bi.Add(b); bi.Add(b - 1); bi.Add(b - 1); bi.Add(b); bi.Add(b + 1); }
            }
            if (stop)
            {
                // 정지 줄(띠 끝 가로선)
                Vector3 p = ego.PosAt(s1); Vector3 f = ego.PosAt(s1 + 1f) - ego.PosAt(s1 - 1f); f.y = 0f; f.Normalize(); Vector3 left = new Vector3(-f.z, 0f, f.x);
                p.y = SurfaceY(Mathf.RoundToInt(s1)) + 0.08f;
                Quad(xv, xu, xc, xi, p - left * (bandWidth * 0.55f) - f * 0.18f, p + left * (bandWidth * 0.55f) - f * 0.18f, p + left * (bandWidth * 0.55f) + f * 0.18f, p - left * (bandWidth * 0.55f) + f * 0.18f, new Color(1f, 0.45f, 0.1f, 0.95f * alpha));
            }
        }

        static void Quad(List<Vector3> v, List<Vector2> u, List<Color> c, List<int> idx, Vector3 a, Vector3 b, Vector3 d, Vector3 e, Color col)
        {
            int k = v.Count; v.Add(a); v.Add(b); v.Add(d); v.Add(e);
            u.Add(new Vector2(0.5f, 0.5f)); u.Add(new Vector2(0.5f, 0.5f)); u.Add(new Vector2(0.5f, 0.5f)); u.Add(new Vector2(0.5f, 0.5f));
            c.Add(col); c.Add(col); c.Add(col); c.Add(col);
            idx.Add(k); idx.Add(k + 1); idx.Add(k + 2); idx.Add(k); idx.Add(k + 2); idx.Add(k + 3);
        }

        // ------------------------------------------------------------ 물체 상자
        struct Obj { public Vector3 c; public Vector3 f; public float len, wid, h, d; public string label; public Color col; public bool hi; public string kind; }
        readonly List<Obj> objs = new List<Obj>();

        void Boxes()
        {
            objs.Clear();
            var tr = traffic != null ? traffic.Core : null; var pd = traffic != null ? traffic.Peds : null;
            Vector3 ep = ego.car != null ? ego.car.position : ego.PosAt(ego.Dist); Vector3 ef = ego.car != null ? ego.car.forward : Vector3.forward;
            Vector3 cf = cam.transform.forward;
            if (tr != null)
                foreach (var c in tr.cars)
                {
                    if (!c.active || c.done || !c.hasPose || c.alpha < 0.5f) continue;
                    Vector3 p = new Vector3(c.pos.x, c.pos.y, c.pos.z), d = p - ep; float dist = d.magnitude;
                    bool hi = c == hiCar || c == hiCar2 || c == radarCar;
                    if (!hi && (dist > carRange || Vector3.Dot(d, ef) < -4f)) continue;
                    if (!hi && Vector3.Dot((p - cam.transform.position).normalized, cf) < 0.35f) continue;
                    float h = c.len > 8f ? 3.2f : (c.len > 5f ? 2.3f : 1.55f);
                    string lb; Color col;
                    if (c == hiCar) { lb = hiCarText; col = hiCarColor; }
                    else if (c == hiCar2) { lb = hiCar2Text; col = hiCar2Color; }
                    else if (c == radarCar && mode == 1) { lb = radarText; col = CBlue; }
                    else if (mode == 1) { lb = "차량(카메라)\n신뢰도: 낮음"; col = COrange; }
                    else { lb = (c.len > 8f ? "버스" : c.len > 5f ? "트럭" : "차량") + "\n거리: " + Gap(dist, c.len) + "m"; col = CBlue; }
                    objs.Add(new Obj { c = p + Vector3.up * h * 0.5f, f = new Vector3(c.fwd.x, 0f, c.fwd.z), len = c.len, wid = c.wid, h = h, d = dist, label = lb, col = col, hi = hi, kind = c.len > 8f ? "bus" : "car" });
                }
            if (pd != null)
                foreach (var q in pd.peds)
                {
                    if (!q.active || q.done || q.alpha < 0.5f) continue;
                    Vector3 p = new Vector3(q.pos.x, q.pos.y, q.pos.z), d = p - ep; float dist = d.magnitude;
                    bool hi = q == hiPed;
                    if (!hi && (dist > pedRange || Vector3.Dot(d, ef) < 2f)) continue;
                    if (!hi && Vector3.Dot((p - cam.transform.position).normalized, cf) < 0.45f) continue;
                    string lb; Color col;
                    if (hi) { lb = hiPedText; col = hiPedColor; }
                    else if (mode == 1) { lb = "보행자(카메라)\n신뢰도: 낮음"; col = COrange; }
                    else { lb = "보행자\n거리: " + Mathf.Max(0f, dist - 2.3f).ToString("0.0") + "m"; col = CCyan; }
                    objs.Add(new Obj { c = p + Vector3.up * 0.88f, f = new Vector3(q.fwd.x, 0f, q.fwd.z), len = 0.55f, wid = 0.55f, h = 1.76f, d = dist, label = lb, col = col, hi = hi, kind = "ped" });
                }
            objs.Sort((a, b) => a.d.CompareTo(b.d));
            int nLab = 0;
            foreach (var o in objs)
            {
                DrawBox(o);
                if ((o.hi || nLab < 7) && !string.IsNullOrEmpty(o.label)) { LabelForBox(o); if (!o.hi) nLab++; }
                if (nearest.Count < 3 && !o.hi && Vector3.Dot(o.c - ep, ef) > 0f) nearest.Add(new KeyValuePair<string, float>(o.kind, Mathf.Max(0f, o.d - 2.4f)));
            }
        }

        static string Gap(float dist, float len) { return Mathf.Max(0f, dist - (len + 4.7f) * 0.5f).ToString("0.0"); }

        Vector3 lastTop;
        void DrawBox(Obj o)
        {
            // 카메라를 향한 네모: 상자 8꼭짓점을 카메라 오른쪽·위로 투영한 범위
            var ct = cam.transform; Vector3 r = ct.right, u = ct.up, toC = (ct.position - o.c).normalized;
            Vector3 f = o.f.sqrMagnitude > 1e-4f ? o.f.normalized : Vector3.forward, side = new Vector3(f.z, 0f, -f.x);
            float minR = 1e9f, maxR = -1e9f, minU = 1e9f, maxU = -1e9f;
            for (int k = 0; k < 8; k++)
            {
                Vector3 q = o.c + f * (((k & 1) == 0 ? -0.5f : 0.5f) * o.len) + side * (((k & 2) == 0 ? -0.5f : 0.5f) * o.wid) + Vector3.up * (((k & 4) == 0 ? -0.5f : 0.5f) * o.h);
                Vector3 dq = q - o.c; float a = Vector3.Dot(dq, r), b = Vector3.Dot(dq, u);
                minR = Mathf.Min(minR, a); maxR = Mathf.Max(maxR, a); minU = Mathf.Min(minU, b); maxU = Mathf.Max(maxU, b);
            }
            float pad = 0.08f; minR -= pad; maxR += pad; minU -= pad; maxU += pad;
            Vector3 c0 = o.c + toC * Mathf.Min(o.len, o.wid) * 0.55f;          // 앞면 쪽으로 조금 당김(차체에 덜 묻힘)
            Vector3 A = c0 + r * minR + u * minU, B = c0 + r * maxR + u * minU, C = c0 + r * maxR + u * maxU, D = c0 + r * minR + u * maxU;
            float dist = Vector3.Distance(ct.position, o.c);
            float t = Mathf.Clamp(dist * 0.0024f, 0.012f, 0.16f) * (o.hi ? 1.6f : 1f);    // 선 굵기(화면에서 비슷하게)
            Color col = new Color(o.col.r, o.col.g, o.col.b, (o.hi ? 1f : 0.9f) * alpha), fill = new Color(o.col.r, o.col.g, o.col.b, (o.hi ? 0.14f : 0.07f) * alpha), thin = new Color(o.col.r, o.col.g, o.col.b, 0.38f * alpha);
            Quad(xv, xu, xc, xi, A, B, C, D, fill);
            Line(A, B, t * 0.45f, thin); Line(B, C, t * 0.45f, thin); Line(C, D, t * 0.45f, thin); Line(D, A, t * 0.45f, thin);
            float cl = Mathf.Min(maxR - minR, maxU - minU) * 0.28f;
            Corner(A, r, u, cl, t, col); Corner(B, -r, u, cl, t, col); Corner(C, -r, -u, cl, t, col); Corner(D, r, -u, cl, t, col);
            lastTop = D;
        }
        void Corner(Vector3 p, Vector3 a, Vector3 b, float l, float t, Color c) { Line(p, p + a * l, t, c); Line(p, p + b * l, t, c); }
        void Line(Vector3 a, Vector3 b, float t, Color c)
        {
            Vector3 d = b - a; if (d.sqrMagnitude < 1e-8f) return;
            Vector3 n = Vector3.Cross(d.normalized, (cam.transform.position - (a + b) * 0.5f).normalized) * (t * 0.5f);
            Quad(xv, xu, xc, xi, a - n, b - n, b + n, a + n, c);
        }

        // ------------------------------------------------------------ 레이더 고리(비)
        void Rings()
        {
            if (ego.car == null) return;
            Vector3 f = ego.car.forward; f.y = 0f; f.Normalize(); Vector3 left = new Vector3(-f.z, 0f, f.x);
            Vector3 o = ego.car.position + f * 2.6f; o.y = SurfaceY(Mathf.RoundToInt(ego.Dist)) + 0.09f;
            for (int k = 0; k < 3; k++)
            {
                float ph = Mathf.Repeat(Time.time * 0.55f + k / 3f, 1f), R = Mathf.Lerp(2f, 26f, ph), a = Mathf.Sin(ph * Mathf.PI) * 0.55f * alpha;
                // 앞쪽 반원(부채꼴 사각 → 고리 텍스처가 원)
                Vector3 c = o; int b = rv.Count;
                rv.Add(c - left * R - f * R * 0.2f); rv.Add(c + left * R - f * R * 0.2f); rv.Add(c + left * R + f * R); rv.Add(c - left * R + f * R);
                ru.Add(new Vector2(0f, 0.4f)); ru.Add(new Vector2(1f, 0.4f)); ru.Add(new Vector2(1f, 1f)); ru.Add(new Vector2(0f, 1f));
                var col = new Color(0.4f, 0.8f, 1f, a); rc.Add(col); rc.Add(col); rc.Add(col); rc.Add(col);
                ri.Add(b); ri.Add(b + 1); ri.Add(b + 2); ri.Add(b); ri.Add(b + 2); ri.Add(b + 3);
            }
        }

        // ------------------------------------------------------------ 글자
        void LabelForBox(Obj o)
        {
            float dist = Vector3.Distance(cam.transform.position, o.c);
            Label(lastTop + cam.transform.up * Mathf.Clamp(dist * 0.012f, 0.05f, 1.2f), o.label, o.col, o.hi ? 1.25f : 0.9f, true);
        }

        void Label(Vector3 p, string text, Color col, float scale, bool leftAlign = false)
        {
            if (labelCanvas == null) return;
            Text t; Image bg;
            if (labelN < labels.Count) { t = labels[labelN]; bg = labelBg[labelN]; }
            else
            {
                var g = new GameObject("글자", typeof(RectTransform)); g.transform.SetParent(labelCanvas.transform, false);
                var r = (RectTransform)g.transform; r.sizeDelta = new Vector2(320, 90); r.pivot = new Vector2(0f, 0f);
                bg = g.AddComponent<Image>(); bg.material = labelMat; bg.raycastTarget = false;
                var g2 = new GameObject("t", typeof(RectTransform)); g2.transform.SetParent(g.transform, false);
                var r2 = (RectTransform)g2.transform; r2.anchorMin = Vector2.zero; r2.anchorMax = Vector2.one; r2.offsetMin = new Vector2(10, 4); r2.offsetMax = new Vector2(-8, -4);
                t = g2.AddComponent<Text>(); t.font = fontBold != null ? fontBold : font; t.fontSize = 26; t.alignment = TextAnchor.MiddleLeft; t.material = labelMat; t.raycastTarget = false;
                t.horizontalOverflow = HorizontalWrapMode.Overflow; t.verticalOverflow = VerticalWrapMode.Overflow; t.lineSpacing = 1.0f;
                if (t.font == null) t.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
                labels.Add(t); labelBg.Add(bg);
            }
            labelN++;
            var go = t.transform.parent.gameObject; if (!go.activeSelf) go.SetActive(true);
            t.text = text; t.color = Color.white;
            bg.color = new Color(col.r * 0.55f, col.g * 0.55f, col.b * 0.6f, 0.78f * alpha);
            var rt = (RectTransform)go.transform;
            int lines = 1; foreach (char ch in text) if (ch == '\n') lines++;
            float w = 0f; foreach (var ln in text.Split('\n')) w = Mathf.Max(w, ln.Length);
            rt.sizeDelta = new Vector2(Mathf.Max(90f, w * 24f + 24f), lines * 31f + 10f);
            float dist = Vector3.Distance(cam.transform.position, p);
            rt.position = p; rt.rotation = Quaternion.LookRotation(p - cam.transform.position, cam.transform.up);
            rt.localScale = Vector3.one * Mathf.Clamp(dist * 0.00115f, 0.0006f, 0.12f) * scale;
        }
    }
}
