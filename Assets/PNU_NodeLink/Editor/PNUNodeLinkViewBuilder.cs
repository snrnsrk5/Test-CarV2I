// 메뉴: PNU Map > 노드링크 보기 씬 만들기
// - Assets/PNU_NodeLink/Data/nodelink_view.json(표준노드링크, 방향별 링크·노드·회전정보) → 새 씬 Assets/PNU_NodeLink/Scenes/NodeLinkView.unity
// - Editor/run_view.txt 가 있으면 컴파일 직후 한 번 자동 실행하고 지움
// - 루트(노드링크 보기) 인스펙터: 색 기준·높이·폭 바꾸고 [다시 그리기], 범례, [기존 맵 같이 보기](Drive 씬을 겹쳐 열기)
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using PNU.NodeLink;

[InitializeOnLoad]
public static class PNUNodeLinkViewBuilder
{
    public const string ROOT = "Assets/PNU_NodeLink/";
    const string Trigger = ROOT + "Editor/run_view.txt";
    public const string ScenePath = ROOT + "Scenes/NodeLinkView.unity";
    public const string DrivePath = "Assets/PNU_Map3/Scenes/Drive.unity";

    static double waitUntil;
    static PNUNodeLinkViewBuilder() { EditorApplication.delayCall += RunTriggerIfPresent; }
    static void RunTriggerIfPresent()
    {
        if (!File.Exists(Trigger)) return;
        // 데이터·셰이더 가져오기가 아직이면 최대 20초 기다림
        if (AssetDatabase.LoadAssetAtPath<TextAsset>(ROOT + "Data/nodelink_view.json") == null || Shader.Find("PNU/NodeLinkOverlay") == null)
        {
            if (waitUntil == 0) waitUntil = EditorApplication.timeSinceStartup + 20.0;
            if (EditorApplication.timeSinceStartup < waitUntil) { EditorApplication.delayCall += RunTriggerIfPresent; return; }
        }
        AssetDatabase.DeleteAsset(Trigger);
        BuildScene();
    }

    [MenuItem("PNU Map/노드링크 보기 씬 만들기")]
    public static void BuildScene()
    {
        var ta = AssetDatabase.LoadAssetAtPath<TextAsset>(ROOT + "Data/nodelink_view.json");
        if (ta == null) { Debug.LogError("PNU 노드링크: " + ROOT + "Data/nodelink_view.json 없음"); return; }
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        var lg = new GameObject("Directional Light");
        var lt = lg.AddComponent<Light>(); lt.type = LightType.Directional; lt.intensity = 1.1f;
        lg.transform.rotation = Quaternion.Euler(55f, -30f, 0f);
        var go = new GameObject("노드링크 보기");
        var v = go.AddComponent<PNUNodeLinkView>();
        v.data = ta;
        Rebuild(v);
        EnsureFolder(ROOT + "Scenes");
        EditorSceneManager.SaveScene(scene, ScenePath);
        Selection.activeGameObject = go;
        LookAtStart(v);
        Debug.Log("PNU 노드링크 보기: 링크 " + v.linkCount + " · 노드 " + v.nodeCount + " · 회전정보 " + v.turnCount + " → " + ScenePath);
    }

    public static void EnsureFolder(string path)
    {
        path = path.TrimEnd('/');
        if (AssetDatabase.IsValidFolder(path)) return;
        string parent = Path.GetDirectoryName(path).Replace('\\', '/');
        EnsureFolder(parent);
        AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
    }

    // ---------- 재질(색마다 하나, 에셋으로 저장) ----------
    static readonly Dictionary<string, Material> matCache = new Dictionary<string, Material>();
    static Shader overlay; static bool overlayChecked;
    static Shader OverlayShader()
    {
        if (!overlayChecked)
        {
            overlayChecked = true;
            overlay = Shader.Find("PNU/NodeLinkOverlay");
            if (overlay != null && ShaderUtil.ShaderHasError(overlay)) overlay = null;
        }
        return overlay;
    }
    /// <summary>layer: 위에 그리는 순서(경로 -10, 링크 0, 화살표 1, 노드 2)</summary>
    public static Material Mat(Color c, bool onTop, int layer)
    {
        var sh = onTop ? OverlayShader() : null;
        bool ov = sh != null;
        string key = ColorUtility.ToHtmlStringRGB(c) + (ov ? "_top" + (layer + 10) : "_flat");
        Material m;
        if (matCache.TryGetValue(key, out m) && m != null) return m;
        EnsureFolder(ROOT + "Materials");
        string path = ROOT + "Materials/NL_" + key + ".mat";
        m = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (m == null)
        {
            if (!ov) { sh = Shader.Find("Universal Render Pipeline/Unlit"); if (sh == null) sh = Shader.Find("Unlit/Color"); }
            m = new Material(sh);
            AssetDatabase.CreateAsset(m, path);
        }
        if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c);
        if (m.HasProperty("_Color")) m.SetColor("_Color", c);
        if (m.HasProperty("_Cull")) m.SetFloat("_Cull", 0f);
        if (ov) m.renderQueue = 4000 + layer;
        EditorUtility.SetDirty(m);
        matCache[key] = m;
        return m;
    }

    static Transform Child(Transform parent, string name)
    {
        var g = new GameObject(name);
        g.transform.SetParent(parent, false);
        return g.transform;
    }
    static void Add(Dictionary<string, List<string>> d, string k, string v)
    {
        List<string> l;
        if (!d.TryGetValue(k, out l)) { l = new List<string>(); d[k] = l; }
        l.Add(v);
    }
    static float FlatLen(Vector3 a) { return Mathf.Sqrt(a.x * a.x + a.z * a.z); }

    /// <summary>선(리본) + 진행 방향 화살표: 서브메시 0 = 선, 1 = 화살표</summary>
    static Mesh LinkMesh(Vector3[] p, float w, string name, bool arrowsOn)
    {
        var verts = new List<Vector3>(); var ribbon = new List<int>(); var arrows = new List<int>();
        int n = p.Length; float hw = w * 0.5f;
        for (int i = 0; i < n; i++)
        {
            Vector3 t = p[Mathf.Min(n - 1, i + 1)] - p[Mathf.Max(0, i - 1)]; t.y = 0f;
            if (t.sqrMagnitude < 1e-6f) t = Vector3.forward;
            t.Normalize();
            var nl = new Vector3(-t.z, 0f, t.x);                       // 진행 방향 왼쪽
            verts.Add(p[i] + nl * hw); verts.Add(p[i] - nl * hw);
        }
        for (int i = 0; i < n - 1; i++)
        {
            int L0 = 2 * i, R0 = 2 * i + 1, L1 = 2 * i + 2, R1 = 2 * i + 3;
            ribbon.Add(L0); ribbon.Add(L1); ribbon.Add(R0);
            ribbon.Add(R0); ribbon.Add(L1); ribbon.Add(R1);
        }
        if (arrowsOn)
        {
            var cum = new float[n]; float total = 0f;
            for (int i = 1; i < n; i++) { total += FlatLen(p[i] - p[i - 1]); cum[i] = total; }
            int na = Mathf.Max(1, Mathf.FloorToInt(total / 25f));
            float al = Mathf.Clamp(w * 1.6f, 2.5f, 6f), aw = w * 0.85f;
            for (int j = 0; j < na; j++)
            {
                float s = total * (j + 0.5f) / na; int i = 1;
                while (i < n - 1 && cum[i] < s) i++;
                float f = (s - cum[i - 1]) / Mathf.Max(1e-4f, cum[i] - cum[i - 1]);
                Vector3 c = Vector3.Lerp(p[i - 1], p[i], f);
                Vector3 t = p[i] - p[i - 1]; t.y = 0f;
                if (t.sqrMagnitude < 1e-6f) continue;
                t.Normalize();
                var nl = new Vector3(-t.z, 0f, t.x); var up = Vector3.up * 0.05f;
                int b0 = verts.Count;
                verts.Add(c - t * (al * 0.4f) + nl * aw + up); verts.Add(c + t * (al * 0.6f) + up); verts.Add(c - t * (al * 0.4f) - nl * aw + up);
                arrows.Add(b0); arrows.Add(b0 + 1); arrows.Add(b0 + 2);
            }
        }
        var m = new Mesh();
        m.name = name;
        if (verts.Count > 60000) m.indexFormat = IndexFormat.UInt32;
        m.SetVertices(verts);
        m.subMeshCount = 2;
        m.SetTriangles(ribbon, 0);
        m.SetTriangles(arrows, 1);
        m.RecalculateNormals(); m.RecalculateBounds();
        return m;
    }
    static Mesh discMesh;
    static Mesh Disc()
    {
        if (discMesh != null) return discMesh;
        var v = new List<Vector3> { Vector3.zero }; var t = new List<int>();
        for (int i = 0; i < 12; i++) { float a = i * Mathf.PI * 2f / 12f; v.Add(new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a))); }
        for (int i = 0; i < 12; i++) { int a = 1 + i, b = 1 + (i + 1) % 12; t.Add(0); t.Add(b); t.Add(a); }
        discMesh = new Mesh(); discMesh.name = "node_disc"; discMesh.SetVertices(v); discMesh.SetTriangles(t, 0); discMesh.RecalculateNormals(); discMesh.RecalculateBounds();
        return discMesh;
    }
    static Vector3 Mid(Vector3[] p)
    {
        float total = 0f; for (int i = 1; i < p.Length; i++) total += FlatLen(p[i] - p[i - 1]);
        float acc = 0f;
        for (int i = 1; i < p.Length; i++)
        {
            float d = FlatLen(p[i] - p[i - 1]);
            if (acc + d >= total * 0.5f) return Vector3.Lerp(p[i - 1], p[i], (total * 0.5f - acc) / Mathf.Max(1e-4f, d));
            acc += d;
        }
        return p[0];
    }

    /// <summary>설정대로 전부 다시 그림</summary>
    public static void Rebuild(PNUNodeLinkView v)
    {
        if (v.data == null) { Debug.LogError("PNU 노드링크: data(json) 비어 있음"); return; }
        var D = JsonUtility.FromJson<NLData>(v.data.text);
        matCache.Clear(); overlayChecked = false; discMesh = null;
        for (int i = v.transform.childCount - 1; i >= 0; i--) Object.DestroyImmediate(v.transform.GetChild(i).gameObject);
        bool flat = !v.useTerrainHeight; float off = v.heightOffset;
        var nodePos = new Dictionary<string, Vector3>();
        foreach (var nd in D.nodes) nodePos[nd.id] = new Vector3(nd.x, flat ? 0f : nd.y + off, nd.z);
        var linksRoot = Child(v.transform, "링크 (도로명별)");
        var arrowMat = Mat(NLPalette.Arrow, v.drawOnTop, 1);
        var groups = new Dictionary<string, Transform>();
        var lp = new List<Vector3>(); var lt = new List<string>();
        var inL = new Dictionary<string, List<string>>(); var outL = new Dictionary<string, List<string>>();
        try
        {
            for (int k = 0; k < D.links.Length; k++)
            {
                var l = D.links[k];
                if ((k & 63) == 0) EditorUtility.DisplayProgressBar("노드링크 보기", "링크 " + k + " / " + D.links.Length, k / (float)D.links.Length);
                if (l.x == null || l.x.Length < 2) continue;
                string nm = string.IsNullOrEmpty(l.name) ? "(이름 없음)" : l.name;
                Transform g;
                if (!groups.TryGetValue(nm, out g)) { g = Child(linksRoot, nm); groups[nm] = g; }
                var go = new GameObject(nm + " · " + l.lanes + "차로 · " + l.spd + "km/h [" + l.id + "]");
                go.transform.SetParent(g, false);
                var info = go.AddComponent<PNULinkInfo>();
                info.linkId = l.id; info.fromNode = l.f; info.toNode = l.t; info.roadName = l.name; info.rank = l.rank; info.roadType = l.type;
                info.connect = l.connect; info.roadNo = l.roadNo; info.multiLink = l.multi; info.roadUse = l.roadUse; info.restVeh = l.restVeh;
                info.lanes = l.lanes; info.speed = l.spd; info.length = l.len;
                var pts = new Vector3[l.x.Length];
                for (int i = 0; i < pts.Length; i++) pts[i] = new Vector3(l.x[i], flat ? 0f : l.y[i] + off, l.z[i]);
                info.fromPos = nodePos.ContainsKey(l.f) ? nodePos[l.f] : pts[0];
                info.toPos = nodePos.ContainsKey(l.t) ? nodePos[l.t] : pts[pts.Length - 1];
                float w = v.baseWidth + v.widthPerLane * Mathf.Max(1, l.lanes);
                go.AddComponent<MeshFilter>().sharedMesh = LinkMesh(pts, w, "link_" + l.id, true);
                var mr = go.AddComponent<MeshRenderer>();
                mr.shadowCastingMode = ShadowCastingMode.Off; mr.receiveShadows = false;
                mr.sharedMaterials = new Material[] { Mat(NLPalette.LinkColor(v.colorBy, info), v.drawOnTop, 0), arrowMat };
                lp.Add(Mid(pts)); lt.Add(nm + " " + l.lanes + "차로 " + l.spd);
                Add(outL, l.f, l.id + "  " + nm + " " + l.lanes + "차로");
                Add(inL, l.t, l.id + "  " + nm + " " + l.lanes + "차로");
            }
            // 노드
            var nlp = new List<Vector3>(); var nlt = new List<string>(); var cnt = new List<int>();
            if (v.showNodes)
            {
                var nodesRoot = Child(v.transform, "노드 (종류별)");
                var ngroups = new Dictionary<string, Transform>();
                var turnsAt = new Dictionary<string, List<string>>();
                if (D.turns != null)
                    foreach (var t in D.turns) Add(turnsAt, t.node, t.frm + " → " + t.to + " : " + NLCodes.Turn(t.type) + " (" + t.type + ")" + (t.oper == "0" || string.IsNullOrEmpty(t.oper) ? "" : " 운영 " + t.oper));
                int k = 0;
                foreach (var nd in D.nodes)
                {
                    if ((k++ & 127) == 0) EditorUtility.DisplayProgressBar("노드링크 보기", "노드 " + k + " / " + D.nodes.Length, k / (float)D.nodes.Length);
                    string tl = NLCodes.NodeType(nd.type);
                    Transform g;
                    if (!ngroups.TryGetValue(tl, out g)) { g = Child(nodesRoot, tl); ngroups[tl] = g; }
                    var go = new GameObject(tl + " · " + (string.IsNullOrEmpty(nd.name) ? "-" : nd.name) + " [" + nd.id + "]");
                    go.transform.SetParent(g, false);
                    go.transform.position = nodePos[nd.id] + Vector3.up * 0.2f;
                    float r = nd.type == "101" ? 2.2f : 1.6f;
                    go.transform.localScale = new Vector3(r, 1f, r);
                    go.AddComponent<MeshFilter>().sharedMesh = Disc();
                    var mr = go.AddComponent<MeshRenderer>();
                    mr.shadowCastingMode = ShadowCastingMode.Off; mr.receiveShadows = false;
                    mr.sharedMaterial = Mat(NLPalette.NodeColor(nd.type), v.drawOnTop, 2);
                    var ni = go.AddComponent<PNUNodeInfo>();
                    ni.nodeId = nd.id; ni.nodeType = nd.type; ni.nodeName = nd.name; ni.turnP = nd.turnP;
                    List<string> li;
                    if (inL.TryGetValue(nd.id, out li)) ni.inLinks = li;
                    if (outL.TryGetValue(nd.id, out li)) ni.outLinks = li;
                    if (turnsAt.TryGetValue(nd.id, out li)) ni.turnRules = li;
                    // 교차로·IC·시설물 이름 라벨: 같은 이름 80m 안은 하나로
                    if ((nd.type == "101" || nd.type == "106" || nd.type == "104") && !string.IsNullOrEmpty(nd.name))
                    {
                        Vector3 p = nodePos[nd.id]; int hit = -1;
                        for (int j = 0; j < nlp.Count; j++)
                            if (nlt[j] == nd.name && FlatLen(nlp[j] / cnt[j] - p) < 80f) { hit = j; break; }
                        if (hit < 0) { nlp.Add(p); nlt.Add(nd.name); cnt.Add(1); }
                        else { nlp[hit] += p; cnt[hit]++; }
                    }
                }
                for (int j = 0; j < nlp.Count; j++) nlp[j] /= cnt[j];
            }
            v.nodeLabelPos = nlp.ToArray(); v.nodeLabelText = nlt.ToArray();
            // 지금 자차 경로(참고)
            if (D.routeX != null && D.routeX.Length > 1)
            {
                var rp = new Vector3[D.routeX.Length];
                for (int i = 0; i < rp.Length; i++) rp[i] = new Vector3(D.routeX[i], flat ? -0.2f : D.routeY[i] + off - 0.2f, D.routeZ[i]);
                v.routeStart = rp[0];
                if (v.showRoute)
                {
                    var rgo = new GameObject("지금 자차 경로 (참고)");
                    rgo.transform.SetParent(v.transform, false);
                    rgo.AddComponent<MeshFilter>().sharedMesh = LinkMesh(rp, 7f, "route_now", false);
                    var mr = rgo.AddComponent<MeshRenderer>();
                    mr.shadowCastingMode = ShadowCastingMode.Off; mr.receiveShadows = false;
                    var rm = Mat(NLPalette.Route, v.drawOnTop, -10);
                    mr.sharedMaterials = new Material[] { rm, rm };
                }
            }
        }
        finally { EditorUtility.ClearProgressBar(); }
        v.linkLabelPos = lp.ToArray(); v.linkLabelText = lt.ToArray();
        v.linkCount = D.links.Length; v.nodeCount = D.nodes.Length; v.turnCount = D.turns != null ? D.turns.Length : 0; v.source = D.source;
        EditorUtility.SetDirty(v);
        EditorSceneManager.MarkSceneDirty(v.gameObject.scene);
        AssetDatabase.SaveAssets();
    }

    /// <summary>선 색만 다시(색 기준 바꿨을 때)</summary>
    public static void Recolor(PNUNodeLinkView v)
    {
        matCache.Clear(); overlayChecked = false;
        var arrowMat = Mat(NLPalette.Arrow, v.drawOnTop, 1);
        foreach (var info in v.GetComponentsInChildren<PNULinkInfo>(true))
        {
            var mr = info.GetComponent<MeshRenderer>();
            if (mr == null) continue;
            mr.sharedMaterials = new Material[] { Mat(NLPalette.LinkColor(v.colorBy, info), v.drawOnTop, 0), arrowMat };
        }
        EditorSceneManager.MarkSceneDirty(v.gameObject.scene);
        AssetDatabase.SaveAssets();
    }

    public static void LookAtStart(PNUNodeLinkView v)
    {
        var sv = SceneView.lastActiveSceneView;
        if (sv == null) return;
        sv.LookAt(v.routeStart, Quaternion.Euler(90f, 0f, 0f), 220f, false, true);
    }

    public static bool DriveOpen()
    {
        var s = SceneManager.GetSceneByPath(DrivePath);
        return s.IsValid() && s.isLoaded;
    }
    /// <summary>기존 맵(Drive 씬)을 겹쳐 열기/닫기 — 겹쳐 볼 땐 지형 높이로 다시 그림</summary>
    public static void ToggleDrive(PNUNodeLinkView v)
    {
        var s = SceneManager.GetSceneByPath(DrivePath);
        if (s.IsValid() && s.isLoaded) { EditorSceneManager.CloseScene(s, true); return; }
        if (AssetDatabase.LoadAssetAtPath<SceneAsset>(DrivePath) == null) { Debug.LogWarning("PNU 노드링크: " + DrivePath + " 없음"); return; }
        if (!v.useTerrainHeight) { v.useTerrainHeight = true; Rebuild(v); }
        EditorSceneManager.OpenScene(DrivePath, OpenSceneMode.Additive);
        SceneManager.SetActiveScene(v.gameObject.scene);
    }

    public static void SelectLink(string id)
    {
        foreach (var l in Object.FindObjectsByType<PNULinkInfo>(FindObjectsSortMode.None))
            if (l.linkId == id) { Selection.activeGameObject = l.gameObject; SceneView.FrameLastActiveSceneView(); return; }
    }
    public static void SelectNode(string id)
    {
        foreach (var n in Object.FindObjectsByType<PNUNodeInfo>(FindObjectsSortMode.None))
            if (n.nodeId == id) { Selection.activeGameObject = n.gameObject; SceneView.FrameLastActiveSceneView(); return; }
    }
}

[CustomEditor(typeof(PNUNodeLinkView))]
public class PNUNodeLinkViewInspector : Editor
{
    public override void OnInspectorGUI()
    {
        var v = (PNUNodeLinkView)target;
        EditorGUILayout.HelpBox("선 = 노드링크 링크(한 방향). 폭 = 차로 수, 검은 화살표 = 진행 방향.\n선이나 원을 누르면 속성이 보여요. 씬 뷰에서 가까이 가면 도로명·차로·속도 글씨가 떠요.", MessageType.Info);
        var before = v.colorBy;
        DrawDefaultInspector();
        if (v.colorBy != before) PNUNodeLinkViewBuilder.Recolor(v);
        GUILayout.Space(6);
        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button("다시 그리기 (설정 적용)", GUILayout.Height(26))) PNUNodeLinkViewBuilder.Rebuild(v);
            if (GUILayout.Button("출발 지점 보기", GUILayout.Height(26))) PNUNodeLinkViewBuilder.LookAtStart(v);
        }
        if (GUILayout.Button(PNUNodeLinkViewBuilder.DriveOpen() ? "기존 맵(Drive 씬) 닫기" : "기존 맵(Drive 씬) 같이 보기", GUILayout.Height(24))) PNUNodeLinkViewBuilder.ToggleDrive(v);
        GUILayout.Space(8);
        EditorGUILayout.LabelField("범례 — 선 색", EditorStyles.boldLabel);
        foreach (var kv in NLPalette.Legend(v.colorBy)) Swatch(kv.Key, kv.Value);
        GUILayout.Space(4);
        EditorGUILayout.LabelField("범례 — 원(노드) 색", EditorStyles.boldLabel);
        foreach (var kv in NLPalette.NodeLegend()) Swatch(kv.Key, kv.Value);
        Swatch("#00E5FF", "굵은 하늘색 = 지금 자차 경로(참고)");
    }
    static void Swatch(string hex, string label)
    {
        var r = EditorGUILayout.GetControlRect();
        EditorGUI.DrawRect(new Rect(r.x, r.y + 3f, 26f, r.height - 6f), NLPalette.Hex(hex));
        EditorGUI.LabelField(new Rect(r.x + 34f, r.y, r.width - 34f, r.height), label);
    }
}

[CustomEditor(typeof(PNULinkInfo))]
public class PNULinkInfoInspector : Editor
{
    public override void OnInspectorGUI()
    {
        var l = (PNULinkInfo)target;
        EditorGUILayout.LabelField("링크 (한 방향)", EditorStyles.boldLabel);
        Row("도로명", string.IsNullOrEmpty(l.roadName) ? "(없음)" : l.roadName);
        Row("차로 수", l.lanes + "차로");
        Row("제한속도", l.speed + " km/h");
        Row("도로 등급", NLCodes.Rank(l.rank) + "  (" + l.rank + ")");
        Row("도로 종류", NLCodes.RoadType(l.roadType) + "  (" + l.roadType + ")");
        Row("연결로", NLCodes.IsConnect(l.connect) ? "연결로(램프)  (" + l.connect + ")" : "아님");
        Row("길이", l.length.ToString("0.0") + " m");
        Row("도로 번호", l.roadNo);
        Row("중용 구간", l.multiLink);
        Row("통행 제한 차량", l.restVeh);
        Row("ROAD_USE", l.roadUse);
        GUILayout.Space(4);
        Row("링크 ID", l.linkId);
        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button("시작 노드 " + l.fromNode)) PNUNodeLinkViewBuilder.SelectNode(l.fromNode);
            if (GUILayout.Button("끝 노드 " + l.toNode)) PNUNodeLinkViewBuilder.SelectNode(l.toNode);
        }
    }
    static void Row(string k, string v) { EditorGUILayout.LabelField(k, string.IsNullOrEmpty(v) ? "-" : v); }
}

[CustomEditor(typeof(PNUNodeInfo))]
public class PNUNodeInfoInspector : Editor
{
    public override void OnInspectorGUI()
    {
        var n = (PNUNodeInfo)target;
        EditorGUILayout.LabelField("노드", EditorStyles.boldLabel);
        EditorGUILayout.LabelField("종류", NLCodes.NodeType(n.nodeType) + "  (" + n.nodeType + ")");
        EditorGUILayout.LabelField("이름", string.IsNullOrEmpty(n.nodeName) ? "-" : n.nodeName);
        EditorGUILayout.LabelField("회전 제한 있음", n.turnP == "1" ? "예" : "아니오");
        EditorGUILayout.LabelField("노드 ID", n.nodeId);
        GUILayout.Space(4);
        EditorGUILayout.LabelField("들어오는 링크", EditorStyles.boldLabel);
        LinkButtons(n.inLinks);
        EditorGUILayout.LabelField("나가는 링크", EditorStyles.boldLabel);
        LinkButtons(n.outLinks);
        if (n.turnRules != null && n.turnRules.Count > 0)
        {
            EditorGUILayout.LabelField("회전 정보 (들어온 링크 → 나갈 링크)", EditorStyles.boldLabel);
            foreach (var t in n.turnRules) EditorGUILayout.LabelField(t);
        }
    }
    static void LinkButtons(List<string> ls)
    {
        if (ls == null || ls.Count == 0) { EditorGUILayout.LabelField("-"); return; }
        foreach (var s in ls)
            if (GUILayout.Button(s, EditorStyles.miniButton)) PNUNodeLinkViewBuilder.SelectLink(s.Split(' ')[0]);
    }
}
