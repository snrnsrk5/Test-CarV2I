// 메뉴: PNU Map > 도로 생성기 > 새 씬 만들기
// - Data/pnu_roadgen_input.txt(노드링크 + 높이) → 도로·교차로·인도·노면표시·지형·신호등 자리를 절차적으로 생성
// - 루트(PNURoadGenRoot) 인스펙터: 규칙·범위 바꾸고 [다시 만들기]. 도로·교차로 조각에서 고친 값(높이, 인도, 차로 수,
//   횡단보도 종류·색, 대각선, 모서리 반경)은 노드링크 ID 키로 기억해서 다시 만들어도 유지
// - Editor/run_build.txt 가 있으면 컴파일 직후 한 번 자동 실행하고 지움
// - Editor/run_rebuild.txt 가 있으면 RoadGen 씬을 (추가로) 열어 있는 루트를 그대로 다시 만들고 저장(설정·고친 값 유지), 원래 씬으로 돌아감
// - 메쉬는 Generated/RoadGenMeshes_00.asset … 조각 에셋에 나눠 저장(조각마다 약 45MB 이하 — 깃허브 파일 100MB 제한)
// - Data/pnu_editor_net.txt(도로 편집기 최종 도로망)가 있으면 노드링크 도로망 대신 편집기에서 고친 도로망으로(자르기·잇기·램프·새 길·차로·높이·연결)
// - Data/pnu_buildings.txt: 예전 지도(PNU_Map3) 건물을 새 지형 위에 다시 세움(새 도로와 겹치는 건물은 뺌)
// - 예전 지도 구조물(정문·NC백화점·부경대 정문·1호선 고가): Assets/PNU_Map3/Map/Section_*.fbx 에서 그것만 가져옴(새 도로 자리는 잘라 냄)
// - Data/pnu_drive_rg.json: 운전 테스트(자차 테슬라 + 운전석 + 주행 재생, Space 재생 · C 운전석/따라가기 · R 정면)
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using System.Reflection;
using PNU.RoadGen;

[InitializeOnLoad]
public static class PNURoadGenBuilder
{
    public const string ROOT = "Assets/PNU_RoadGen/";
    const string Trigger = ROOT + "Editor/run_build.txt";
    public const string InputPath = ROOT + "Data/pnu_roadgen_input.txt";
    public const string LaneLinksPath = ROOT + "Data/pnu_lane_links.txt";     // 차로 연결 편집기에서 손으로 이은 연결(있으면)
    public const string RoadEditsPath = ROOT + "Data/pnu_road_edits.txt";    // 도로 편집기에서 고친 도로(정점·구간 차로 수)
    public const string RouteSpecPath = ROOT + "Data/pnu_route_spec.txt";     // 주행 경로 명세(도로 키·방향 순서). 있으면 차로 따라가는 경로를 만들어 씬에 선으로
    public const string RouteLanesPath = ROOT + "Data/pnu_route_lanes.json";  // 만든 경로(차로 중심선, 교차로 연결선, 높이)
    public const string ScenePath = ROOT + "Scenes/RoadGen.unity";
    public const string EditorNetPath = ROOT + "Data/pnu_editor_net.txt";    // 도로 편집기 최종 도로망(있으면 이것으로)
    public const string BuildingsPath = ROOT + "Data/pnu_buildings.txt";     // 예전 지도 건물(새 지형 위에 다시 세움)
    public const string DriveJsonPath = ROOT + "Data/pnu_drive_rg.json";     // 운전 테스트: 자차 경로·시간표·장면(S0~S5)
    const string OldMap = "Assets/PNU_Map3/";                                 // 예전 지도(건물 재질·구조물·테슬라)
    const string MeshAssetPath = ROOT + "Generated/RoadGenMeshes.asset";       // 예전: 한 덩어리(텍스트 직렬화로 500MB 넘음) — 다시 만들 때 지움
    const string MeshChunkPrefix = ROOT + "Generated/RoadGenMeshes_";          // 지금: 조각 에셋 RoadGenMeshes_00.asset …
    const long MeshChunkBudget = 45L * 1024 * 1024;                            // 조각마다 텍스트 크기 약 45MB 이하(깃허브 100MB 제한·50MB 경고 아래)
    const string RebuildTrigger = ROOT + "Editor/run_rebuild.txt";
    const string GenName = "생성됨";

    static double waitUntil;
    static PNURoadGenBuilder() { EditorApplication.delayCall += RunTriggerIfPresent; }
    static void RunTriggerIfPresent()
    {
        if (File.Exists(RebuildTrigger)) { RebuildSceneFromTrigger(); return; }
        if (!File.Exists(Trigger)) return;
        if (AssetDatabase.LoadAssetAtPath<TextAsset>(InputPath) == null)
        {   // 데이터 가져오기가 아직이면 최대 30초 기다림
            if (waitUntil == 0) waitUntil = EditorApplication.timeSinceStartup + 30.0;
            if (EditorApplication.timeSinceStartup < waitUntil) { EditorApplication.delayCall += RunTriggerIfPresent; return; }
        }
        AssetDatabase.DeleteAsset(Trigger);
        BuildScene();
    }

    /// <summary>run_rebuild.txt: RoadGen 씬의 루트를 그대로 다시 만들기(새 씬을 만들지 않으므로 루트 설정·조각에서 고친 값이 그대로).
    /// 지금 열린 씬은 그대로 두고 RoadGen 씬을 추가로 열었다가, 저장하고 닫음</summary>
    static void RebuildSceneFromTrigger()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {   // 재생 중이면 편집 모드로 돌아온 뒤
            EditorApplication.playModeStateChanged -= RetryAfterPlay; EditorApplication.playModeStateChanged += RetryAfterPlay; return;
        }
        if (EditorApplication.isCompiling || EditorApplication.isUpdating) { EditorApplication.delayCall += RunTriggerIfPresent; return; }
        AssetDatabase.DeleteAsset(RebuildTrigger);
        if (!File.Exists(ScenePath)) { Debug.LogWarning("[PNU Drive] 도로 생성기 씬 없음: " + ScenePath); return; }
        var prev = EditorSceneManager.GetActiveScene();
        var sc = EditorSceneManager.GetSceneByPath(ScenePath); bool opened = false;
        if (!sc.IsValid() || !sc.isLoaded) { sc = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive); opened = true; }
        EditorSceneManager.SetActiveScene(sc);
        PNURoadGenRoot root = null;
        foreach (var g in sc.GetRootGameObjects()) { root = g.GetComponentInChildren<PNURoadGenRoot>(true); if (root != null) break; }
        bool ok = false;
        try
        {
            if (root == null) Debug.LogWarning("[PNU Drive] 도로 생성기: " + ScenePath + " 에 루트(PNURoadGenRoot) 없음");
            else { lastChunkReport = ""; Rebuild(root); ok = true; Debug.Log("[PNU Drive] 도로 생성기 다시 만듦: " + lastChunkReport); }
        }
        catch (System.Exception e) { Debug.LogError("[PNU Drive] 도로 생성기 다시 만들기 실패: " + e); }
        finally
        {
            if (prev.IsValid() && prev.isLoaded && prev != sc) EditorSceneManager.SetActiveScene(prev);
            if (ok && opened && prev.IsValid() && prev != sc) EditorSceneManager.CloseScene(sc, true);
        }
    }
    static void RetryAfterPlay(PlayModeStateChange s)
    {
        if (s != PlayModeStateChange.EnteredEditMode) return;
        EditorApplication.playModeStateChanged -= RetryAfterPlay; EditorApplication.delayCall += RunTriggerIfPresent;
    }
    static string lastChunkReport = "";

    static List<string> MeshChunkFiles()
    {
        var l = new List<string>(); string dir = ROOT + "Generated";
        if (!Directory.Exists(dir)) return l;
        foreach (var f in Directory.GetFiles(dir, "RoadGenMeshes_*.asset")) l.Add(f.Replace('\\', '/'));
        return l;
    }

    /// <summary>메쉬를 조각 에셋에 나눠 저장: 텍스트 크기 어림(꼭짓점·인덱스 바이트 × 2 + 머리)으로 조각마다 MeshChunkBudget 이하</summary>
    static string SaveMeshChunks(List<Mesh> meshes)
    {
        EnsureFolder(ROOT + "Generated");
        int k = 0, n = 0; long acc = 0; Mesh head = null; var sizes = new List<long>();
        foreach (var m in meshes)
        {
            if (m == null) continue;
            long est = EstimateTextBytes(m);
            if (head == null || acc + est > MeshChunkBudget)
            {
                if (head != null) sizes.Add(acc);
                head = m; acc = 0;
                AssetDatabase.CreateAsset(m, MeshChunkPrefix + k.ToString("00") + ".asset"); k++;
            }
            else AssetDatabase.AddObjectToAsset(m, head);
            acc += est; n++;
        }
        if (head != null) sizes.Add(acc);
        AssetDatabase.SaveAssets();
        long mx = sizes.Count > 0 ? sizes.Max() : 0, sum = sizes.Sum();
        return "메쉬 " + n + "개 → 조각 에셋 " + k + "개 (어림 최대 " + (mx / 1048576.0).ToString("0") + "MB, 합 " + (sum / 1048576.0).ToString("0") + "MB)";
    }
    static long EstimateTextBytes(Mesh m)
    {
        long vb = 0;
        for (int s = 0; s < m.vertexBufferCount; s++) vb += (long)m.GetVertexBufferStride(s) * m.vertexCount;
        long ib = 0; int isz = m.indexFormat == IndexFormat.UInt32 ? 4 : 2;
        for (int s = 0; s < m.subMeshCount; s++) ib += (long)m.GetIndexCount(s) * isz;
        return 2 * (vb + ib) + 4096;
    }

    [MenuItem("PNU Map/도로 생성기/새 씬 만들기")]
    public static void BuildScene()
    {
        var ta = AssetDatabase.LoadAssetAtPath<TextAsset>(InputPath);
        if (ta == null) { Debug.LogError("PNU 도로 생성기: " + InputPath + " 없음"); return; }
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        var lg = new GameObject("Directional Light");
        var lt = lg.AddComponent<Light>(); lt.type = LightType.Directional; lt.intensity = 1.2f;
        lg.transform.rotation = Quaternion.Euler(50f, -35f, 0f);
        var go = new GameObject("PNU 도로 생성기");
        var root = go.AddComponent<PNURoadGenRoot>();
        root.input = ta;
        Rebuild(root);
        EnsureFolder(ROOT + "Scenes");
        EditorSceneManager.SaveScene(scene, ScenePath);
        Selection.activeGameObject = go;
        LookAt(root, root.x0 + 600, root.z0 + 650, 160);
    }

    [MenuItem("PNU Map/도로 생성기/선택한 루트 다시 만들기")]
    static void RebuildSelected()
    {
        var root = Selection.activeGameObject != null ? Selection.activeGameObject.GetComponentInParent<PNURoadGenRoot>() : null;
        if (root == null) root = Object.FindObjectsByType<PNURoadGenRoot>(FindObjectsSortMode.None).FirstOrDefault();
        if (root == null) { Debug.LogWarning("PNU 도로 생성기: 씬에 루트가 없음 → [새 씬 만들기]"); return; }
        Rebuild(root);
    }

    // =====================================================================
    // 다시 만들기
    // =====================================================================
    public static void Rebuild(PNURoadGenRoot root)
    {
        if (root.input == null) { Debug.LogError("PNU 도로 생성기: 입력(TextAsset) 없음"); return; }
        var sw = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            EditorUtility.DisplayProgressBar("PNU 도로 생성기", "입력 읽기", 0.05f);
            var input = InputReader.ParseText(root.input.text);
            if (root.settings == null) root.settings = new GenParams();
            var P = root.settings;
            if (root.settingsVersion < 2)
            {   // v2: 노면표시를 면 따라 나눠 깔면서 띄움 3cm, 업무편람 값(횡단보도 간격 0.75, 가각부 3m, 정지선 3m 앞, 전용도로 점선 10/10)
                P.markLift = System.Math.Max(P.markLift, 0.03); P.crosswalkGap = 0.75; P.cornerToMouth = System.Math.Max(P.cornerToMouth, 3.0);
                P.stopLineGap = System.Math.Max(P.stopLineGap, 3.0); P.dashExpressOn = 10; P.dashExpressOff = 10;
                root.settingsVersion = 2; EditorUtility.SetDirty(root);
            }
            if (root.settingsVersion < 3)
            {   // v3: 신호기 업무편람(제1주·제2주·보조신호등) — 예전 배치 번호는 뜻이 바뀌어 자동으로 되돌림, 화살표 모양(머리가 김)
                P.auxSignals = true; P.signalMode = 0; P.crosswalkArrowLen = 0.9; P.crosswalkArrowW = 0.7;
                foreach (var jp in root.GetComponentsInChildren<PNUJunctionPiece>(true)) { jp.signalPlacement = PNUSignalPlacement.Auto; EditorUtility.SetDirty(jp); }
                root.settingsVersion = 3; EditorUtility.SetDirty(root);
            }
            if (root.settingsVersion < 4)
            {   // v4: 경로 전체를 한 번에(사용자 11/11) — 지형은 도로 둘레만, 넓으면 성긴 격자
                root.x0 = -4100; root.z0 = -6600; root.x1 = 2000; root.z1 = 6500;
                root.settingsVersion = 4; EditorUtility.SetDirty(root);
            }

            // 사람이 고친 값 모으기(지금 씬의 조각에서)
            var ov = new Overrides();
            foreach (var rp in root.GetComponentsInChildren<PNURoadPiece>(true)) if (rp.HasOverride && !string.IsNullOrEmpty(rp.key)) ov.roads[rp.key] = rp.ToOverride();
            foreach (var jp in root.GetComponentsInChildren<PNUJunctionPiece>(true)) if (jp.HasOverride && !string.IsNullOrEmpty(jp.key)) ov.junctions[jp.key] = jp.ToOverride();
            // 손으로 이은 차로 연결(차로 연결 편집기 → Data/pnu_lane_links.txt): 그 교차로는 자동 연결 대신 이것으로 면·차선
            if (File.Exists(LaneLinksPath))
            {
                int nLL = ov.ReadLaneLinks(File.ReadAllLines(LaneLinksPath, System.Text.Encoding.UTF8));
                Debug.Log("PNU 도로 생성기: 손으로 이은 차로 연결 " + nLL + "개 (교차로 " + ov.laneLinks.Count + "곳)");
            }
            // 도로 편집기에서 고친 도로(Data/pnu_road_edits.txt): 정점을 지나는 곡선 = 새 중심선, 구간 차로 수
            if (File.Exists(RoadEditsPath))
            {
                int nRE = ov.ReadRoadEdits(File.ReadAllLines(RoadEditsPath, System.Text.Encoding.UTF8));
                Debug.Log("PNU 도로 생성기: 고친 도로 " + nRE + "개");
            }

            EditorUtility.DisplayProgressBar("PNU 도로 생성기", "도로망", 0.15f);
            var net = NetworkBuilder.Build(input.nodes, input.links, input.turns, P);
            var gen = new RoadGenerator(net, P, input.dem) { ov = ov };
            // 도로 편집기 최종 도로망(Data/pnu_editor_net.txt): 노드링크 도로망을 편집기에서 고친 그대로로 바꿈
            string edMsg = "";
            if (root.useEditorNet && File.Exists(EditorNetPath))
            {
                EditorUtility.DisplayProgressBar("PNU 도로 생성기", "도로 편집기 도로망", 0.2f);
                if (gen.ImportEditorNet(File.ReadAllLines(EditorNetPath, System.Text.Encoding.UTF8))) edMsg = gen.log[gen.log.Count - 1];
            }
            EditorUtility.DisplayProgressBar("PNU 도로 생성기", "형상", 0.3f);
            gen.Prepare();
            double bx0 = root.x0, bz0 = root.z0, bx1 = root.x1, bz1 = root.z1;
            System.Func<V2, bool> inBox = p => p.x >= bx0 && p.x <= bx1 && p.z >= bz0 && p.z <= bz1;
            var pieces = gen.BuildAll(inBox);
            List<MeshData> terrainPieces = new List<MeshData>(), structPieces; HeightGrid TG = null;
            if (root.buildTerrain)
            {
                EditorUtility.DisplayProgressBar("PNU 도로 생성기", "지형 맞춤", 0.5f);
                TG = TerrainBuilder.Conform(gen, pieces, bx0, bz0, bx1, bz1);
                terrainPieces = TerrainBuilder.Meshes(TG, P.terrainTile, gen, P.terrainCorridor);
                structPieces = StructureBuilder.Build(gen, TG, inBox);
            }
            else structPieces = StructureBuilder.Build(gen, null, inBox);
            // 예전 지도 건물: 새 지형 높이 위에, 새 도로와 겹치는 건물은 뺌
            var bldPieces = new List<MeshData>(); string bldMsg = "";
            if (root.buildBuildings && File.Exists(BuildingsPath))
            {
                EditorUtility.DisplayProgressBar("PNU 도로 생성기", "건물", 0.6f);
                var bl = BuildingBuilder.Read(File.ReadAllLines(BuildingsPath, System.Text.Encoding.UTF8)); int bd, bo;
                bldPieces = BuildingBuilder.Build(gen, TG, inBox, bl, 400, out bd, out bo);
                bldMsg = "건물 " + (bl.Count - bd - bo) + "채(새 도로와 겹쳐 뺀 것 " + bd + " · 범위 밖 " + bo + ")";
            }
            var spots = root.buildSignals ? SignalPlanner.Plan(gen) : new List<SignalSpot>();   // 같이 쓰는 기둥 번호(poleOf)가 이 목록 순서라 거르지 않고 둠

            // 예전 생성물 지우기
            EditorUtility.DisplayProgressBar("PNU 도로 생성기", "씬에 놓기", 0.65f);
            var old = root.transform.Find(GenName);
            if (old != null) Object.DestroyImmediate(old.gameObject);
            if (AssetDatabase.LoadMainAssetAtPath(MeshAssetPath) != null) AssetDatabase.DeleteAsset(MeshAssetPath);
            foreach (var f in MeshChunkFiles()) AssetDatabase.DeleteAsset(f);
            EnsureFolder(ROOT + "Generated");

            var genRoot = Child(root.transform, GenName);
            var tRoads = Child(genRoot, "도로"); var tJunc = Child(genRoot, "교차로"); var tTer = Child(genRoot, "지형"); var tSig = Child(genRoot, "신호등 자리"); var tStr = Child(genRoot, "구조물(터널·옹벽·교각)");
            var tBld = Child(genRoot, "건물(예전 지도)");
            var mats = Materials(root);
            var meshes = new List<Mesh>();
            int tris = 0;
            var roadByRef = net.roads.ToDictionary(r => r.id);
            var juncByRef = net.junctions.ToDictionary(j => j.id);
            foreach (var md in pieces.Concat(terrainPieces).Concat(structPieces).Concat(bldPieces))
            {
                var parent = md.kind == "road" ? tRoads : md.kind == "junction" ? tJunc : md.kind == "struct" || md.kind == "struct_night" ? tStr : md.kind == "building" ? tBld : tTer;
                var go = new GameObject(md.name); go.transform.SetParent(parent, false);
                go.isStatic = true;
                var mesh = ToMesh(md, mats, out Material[] used); mesh.name = md.name;
                meshes.Add(mesh); tris += md.TriCount;
                go.AddComponent<MeshFilter>().sharedMesh = mesh;
                var mr = go.AddComponent<MeshRenderer>(); mr.sharedMaterials = used;
                mr.shadowCastingMode = md.kind == "terrain" ? ShadowCastingMode.Off : ShadowCastingMode.On;
                if (md.kind == "struct_night") { mr.shadowCastingMode = ShadowCastingMode.Off; go.SetActive(false); }   // 밤 경관 조명: 켜서 씀
                if (md.kind == "road") { var rp = go.AddComponent<PNURoadPiece>(); FillRoad(rp, roadByRef[md.refId], gen, ov); }
                else if (md.kind == "junction") { var jp = go.AddComponent<PNUJunctionPiece>(); FillJunction(jp, juncByRef[md.refId], gen, ov); }
            }
            // 예전 지도 구조물(정문·NC백화점·부경대 정문·1호선 고가): 새 도로 자리는 잘라 냄
            string oldMsg = "";
            if (root.buildOldStructures) { EditorUtility.DisplayProgressBar("PNU 도로 생성기", "예전 구조물", 0.7f); oldMsg = PlaceOldStructures(genRoot, gen, meshes); }
            // 메쉬는 조각 에셋 몇 개에 나눠 저장(씬 파일이 너무 커지지 않게 + 깃허브 파일 100MB 제한)
            lastChunkReport = SaveMeshChunks(meshes);

            // 신호등 자리: 기둥마다 하나, 같은 기둥에 붙는 등(보조·보행)은 그 기둥 자리의 자식으로
            int nSig = 0, nPole = 0; var made = new Dictionary<int, PNUSignalAnchor>();
            for (int i = 0; i < spots.Count; i++)
            {
                var sp = spots[i]; if (!inBox(sp.pos)) continue;
                string kindName = sp.kind == SignalKind.Pedestrian ? "보행 " : sp.kind == SignalKind.VehicleAux ? "보조 " : sp.kind == SignalKind.VehicleSecond ? "제2주 " : "제1주 ";
                var go = new GameObject(kindName + sp.junctionName + " ← " + sp.roadName);
                PNUSignalAnchor host = null;
                if (sp.poleOf >= 0) made.TryGetValue(sp.poleOf, out host);
                go.transform.SetParent(host != null ? host.transform : tSig, true);
                go.transform.position = new Vector3((float)sp.pos.x, (float)sp.y, (float)sp.pos.z);
                go.transform.rotation = Quaternion.LookRotation(new Vector3((float)sp.facing.x, 0f, (float)sp.facing.z), Vector3.up);
                var a = go.AddComponent<PNUSignalAnchor>();
                a.kind = (PNUSignalKind)(int)sp.kind; a.junctionName = sp.junctionName; a.approachRoad = sp.roadName; a.lanesIn = sp.lanesIn;
                a.mount = (PNUSignalMount)sp.mount; a.faces = sp.faces; a.armLength = (float)sp.armLength;
                a.headOffsets = sp.headOffsets.Select(v => (float)v).ToArray(); a.headHeight = (float)sp.headHeight; a.lensMM = sp.lens; a.sharedPole = host;
                a.placement = sp.placement > 0 && sp.placement < SignalPlanner.PlacementName.Length ? SignalPlanner.PlacementName[sp.placement] : "";
                a.distanceFromStopLine = (float)sp.distance; a.note = sp.note;
                a.hints = sp.hints.ToArray(); a.junctionKey = sp.junctionKey; a.approachLink = sp.approachLink;
                made[i] = a; if (host == null) nPole++;
                var prefab = sp.kind == SignalKind.Pedestrian ? root.pedestrianSignalPrefab : root.vehicleSignalPrefab;
                if (prefab != null)
                {
                    var inst = PrefabUtility.InstantiatePrefab(prefab) as GameObject;
                    if (inst != null)
                    {
                        inst.transform.SetParent(go.transform, false);
                        foreach (var t in inst.GetComponentsInChildren<MonoBehaviour>(true)) { var target = t as IPNUSignalTarget; if (target != null) target.ApplyAnchor(a); }
                    }
                }
                nSig++;
            }

            // 주행 경로(차로 따라가기): 명세가 있으면 지금 도로·차로 연결(손 연결 포함)로 다시 계획
            string routeMsg = "";
            if (File.Exists(RouteSpecPath))
            {
                EditorUtility.DisplayProgressBar("PNU 도로 생성기", "주행 경로", 0.9f);
                var rr = gen.PlanRoute(File.ReadAllLines(RouteSpecPath, System.Text.Encoding.UTF8));
                if (rr.ok)
                {
                    File.WriteAllText(RouteLanesPath, rr.ToJson(), new System.Text.UTF8Encoding(false));
                    MakeRouteLine(genRoot, rr);
                    if (root.buildDrive) routeMsg += "\n" + MakeDriveRig(genRoot, rr);
                    routeMsg = "주행 경로 " + rr.Length.ToString("0") + "m · 교차로 " + rr.junctions.Count + "곳(손 연결 " + rr.junctions.Count(j => j.manual) + ") · 차로 바꿈 " + rr.changes.Count + "번";
                }
                else routeMsg = "주행 경로 못 만듦: " + string.Join(" / ", rr.log.ToArray());
                foreach (var l in rr.log) if (l.StartsWith("연결 없음") || l.StartsWith("차로 끝") || l.StartsWith("도로 키")) routeMsg += "\n  " + l;
            }

            root.lastLog = lastChunkReport + "\n도로 " + tRoads.childCount + " · 교차로 " + tJunc.childCount + " · 지형 조각 " + tTer.childCount + " · 신호등 " + nSig + "(기둥 " + nPole + ")"
                + " · 삼각형 " + tris.ToString("N0") + " · 고친 값 " + (ov.roads.Count + ov.junctions.Count) + " · " + (sw.ElapsedMilliseconds / 1000.0).ToString("0.0") + "초";
            if (routeMsg.Length > 0) root.lastLog += "\n" + routeMsg;
            if (edMsg.Length > 0) root.lastLog += "\n" + edMsg;
            if (bldMsg.Length > 0) root.lastLog += "\n" + bldMsg;
            if (oldMsg.Length > 0) root.lastLog += "\n" + oldMsg;
            foreach (var l in gen.log) if (l.StartsWith("road ") || l.StartsWith("geom") || l.StartsWith("lane links:")) root.lastLog += "\n" + l;
            Debug.Log("PNU 도로 생성기: " + root.lastLog);
            EditorSceneManager.MarkSceneDirty(root.gameObject.scene);
            // 메쉬 에셋을 새로 만들었으니 씬도 바로 저장(안 하면 다음에 열 때 예전 메쉬를 찾다가 비어 보임) — 생성기 전용 씬일 때만
            var sc = root.gameObject.scene;
            if (sc.path == ScenePath) EditorSceneManager.SaveScene(sc, ScenePath);
            else root.lastLog += "\n※ 씬을 저장해 주세요(Ctrl+S): 메쉬 에셋을 새로 만들었어요";
        }
        finally { EditorUtility.ClearProgressBar(); }
    }

    /// <summary>주행 경로를 도로 위 주황 띠로(위에서 보이게 눕힘, 노면 30cm 위). 생성물 아래라 다시 만들면 새로 그림 — 안 보이게 하려면 이 오브젝트를 끄면 됨</summary>
    static void MakeRouteLine(Transform genRoot, RouteResult rr)
    {
        var go = new GameObject("주행 경로(차로 따라가기)");
        go.transform.SetParent(genRoot, false);
        go.transform.rotation = Quaternion.Euler(90f, 0f, 0f);                 // TransformZ 정렬: 띠가 땅에 눕게
        var lr = go.AddComponent<LineRenderer>();
        lr.useWorldSpace = true; lr.alignment = LineAlignment.TransformZ;
        lr.widthMultiplier = 0.6f; lr.numCornerVertices = 2; lr.numCapVertices = 2;
        lr.shadowCastingMode = ShadowCastingMode.Off; lr.receiveShadows = false;
        lr.sharedMaterial = MakeUnlit("RG_Route", new Color(1f, 0.45f, 0.1f));
        int n = rr.x.Count; var pts = new Vector3[n];
        for (int i = 0; i < n; i++) pts[i] = new Vector3((float)rr.x[i], (float)rr.y[i] + 0.3f, (float)rr.z[i]);
        lr.positionCount = n; lr.SetPositions(pts);
    }
    static Material MakeUnlit(string name, Color c)
    {
        EnsureFolder(ROOT + "Materials");
        string path = ROOT + "Materials/" + name + ".mat";
        var m = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (m != null) return m;
        var sh = Shader.Find("Universal Render Pipeline/Unlit"); if (sh == null) sh = Shader.Find("Unlit/Color"); if (sh == null) sh = Shader.Find("Sprites/Default");
        m = new Material(sh);
        if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c);
        if (m.HasProperty("_Color")) m.SetColor("_Color", c);
        AssetDatabase.CreateAsset(m, path);
        return m;
    }

    static void FillRoad(PNURoadPiece rp, Road R, RoadGenerator gen, Overrides ov)
    {
        rp.key = Keys.Road(R); rp.roadId = R.id; rp.roadName = R.name;
        RoadGeom g; gen.roadGeom.TryGetValue(R, out g);
        string lanes = "정방향 " + string.Join("/", R.fwd.Select(p => p.lanes.ToString()).Distinct().ToArray()) + "차로" + (R.oneWay ? " (일방)" : ", 역방향 " + string.Join("/", R.bwd.Select(p => p.lanes.ToString()).Distinct().ToArray()) + "차로");
        string types = string.Join(",", R.fwd.Concat(R.bwd).Select(p => TypeName(p.type)).Distinct().ToArray());
        rp.info = (string.IsNullOrEmpty(R.name) ? "(이름 없음)" : R.name) + " · " + RankName(R.rank) + "\n" + lanes + " · 제한 " + R.SpeedAt(0) + "km/h\n유형 " + types
            + (g != null && g.ok ? " · 길이 " + g.Length.ToString("0") + "m" : "") + "\n키 " + rp.key;
        rp.From(ov.roads.ContainsKey(rp.key) ? ov.roads[rp.key] : null);
    }

    static void FillJunction(PNUJunctionPiece jp, Junction J, RoadGenerator gen, Overrides ov)
    {
        jp.key = Keys.Junction(J); jp.junctionId = J.id; jp.junctionName = J.name;
        jp.arms.Clear();
        foreach (var A in J.arms)
            jp.arms.Add(new PNUArmSetting { roadKey = Keys.Road(A.road), atStart = A.atStart, label = (string.IsNullOrEmpty(A.road.name) ? "(이름 없음)" : A.road.name) + " (" + Compass(A.dir) + "쪽, 들어오는 " + A.lanesIn + "차로)" });
        jp.info = (string.IsNullOrEmpty(J.name) ? "(이름 없음)" : J.name) + " · 갈래 " + J.arms.Count + " · 노드 " + J.nodes.Count + "\n횡단보도 " + J.arms.Count(a => a.crosswalk) + "곳 · 키 " + jp.key;
        jp.From(ov.junctions.ContainsKey(jp.key) ? ov.junctions[jp.key] : null);
    }

    static string Compass(V2 d)
    {
        double a = System.Math.Atan2(d.x, d.z) * 180 / System.Math.PI; if (a < 0) a += 360;
        string[] n = { "북", "북동", "동", "남동", "남", "남서", "서", "북서" };
        return n[(int)System.Math.Round(a / 45.0) % 8];
    }
    static string RankName(string r)
    {
        switch (r) { case "101": return "고속국도"; case "102": return "도시고속국도"; case "103": return "일반국도"; case "104": return "특별·광역시도"; case "105": return "국가지원지방도"; case "106": return "지방도"; case "107": return "시·군도"; default: return "기타(" + r + ")"; }
    }
    static string TypeName(string t)
    {
        switch (t) { case "001": return "고가차도"; case "002": return "지하차도"; case "003": return "교량"; case "004": return "터널"; default: return "일반"; }
    }

    // ---------- 메쉬 ----------
    static readonly string[] MatOrder = { "asphalt", "gutter", "curb", "sidewalk", "curb_side", "mark_white", "mark_yellow", "terrain" };
    static Mesh ToMesh(MeshData md, Dictionary<string, Material> mats, out Material[] used)
    {
        var mesh = new Mesh();
        if (md.v.Count > 65000) mesh.indexFormat = IndexFormat.UInt32;
        var vs = new List<Vector3>(md.v.Count); var uv = new List<Vector2>(md.v.Count);
        for (int i = 0; i < md.v.Count; i++) { var p = md.v[i]; vs.Add(new Vector3((float)p.x, (float)p.y, (float)p.z)); var u = md.uv[i]; uv.Add(new Vector2((float)u.x, (float)u.z)); }
        mesh.SetVertices(vs); mesh.SetUVs(0, uv);
        var keys = MatOrder.Where(k => md.tris.ContainsKey(k)).Concat(md.tris.Keys.Where(k => !MatOrder.Contains(k))).ToList();
        mesh.subMeshCount = keys.Count;
        var um = new List<Material>();
        for (int s = 0; s < keys.Count; s++) { mesh.SetTriangles(md.tris[keys[s]], s); Material m; um.Add(mats.TryGetValue(keys[s], out m) ? m : mats["asphalt"]); }
        mesh.RecalculateNormals(); mesh.RecalculateBounds();
        used = um.ToArray();
        return mesh;
    }

    // ---------- 재질(비어 있으면 기본 색으로 에셋 만들고 루트에 넣어 둠) ----------
    static Dictionary<string, Material> Materials(PNURoadGenRoot r)
    {
        // 노면표시는 도로 면에 짜 넣으므로 같은 셰이더·같은 거칠기, 색은 실제 도료처럼 덜 쨍하게(v2)
        if (r.asphalt == null) r.asphalt = MakeMat("RG_Asphalt", new Color(0.24f, 0.24f, 0.245f), 0.15f);
        if (r.curb == null) r.curb = MakeMat("RG_Curb", new Color(0.66f, 0.66f, 0.63f), 0.1f);
        if (r.gutter == null) r.gutter = MakeMat("RG_Gutter", new Color(0.56f, 0.56f, 0.54f), 0.08f);
        if (r.sidewalk == null) r.sidewalk = MakeMat("RG_Sidewalk", new Color(0.74f, 0.64f, 0.52f), 0.1f);
        if (r.curbSide == null) r.curbSide = MakeMat("RG_CurbSide", new Color(0.58f, 0.57f, 0.54f), 0.05f);
        if (r.markWhite == null) r.markWhite = MakeMat("RG_MarkWhite", new Color(0.80f, 0.80f, 0.77f), 0.15f);
        if (r.markYellow == null) r.markYellow = MakeMat("RG_MarkYellow", new Color(0.84f, 0.64f, 0.14f), 0.15f);
        if (r.terrain == null) r.terrain = MakeMat("RG_Terrain", new Color(0.44f, 0.53f, 0.36f), 0.05f);
        // v1 기본색 그대로인 재질은 v2 색으로(사람이 바꾼 색은 그대로 둠)
        Retint(r.asphalt, new Color(0.26f, 0.265f, 0.28f), new Color(0.24f, 0.24f, 0.245f), 0.15f);
        Retint(r.curb, new Color(0.82f, 0.82f, 0.79f), new Color(0.66f, 0.66f, 0.63f), 0.1f);
        Retint(r.markWhite, new Color(0.94f, 0.94f, 0.92f), new Color(0.80f, 0.80f, 0.77f), 0.15f);
        Retint(r.markYellow, new Color(0.95f, 0.74f, 0.08f), new Color(0.84f, 0.64f, 0.14f), 0.15f);
        var d = new Dictionary<string, Material>
        {
            { "asphalt", r.asphalt }, { "gutter", r.gutter }, { "curb", r.curb }, { "sidewalk", r.sidewalk }, { "curb_side", r.curbSide },
            { "mark_white", r.markWhite }, { "mark_yellow", r.markYellow }, { "terrain", r.terrain },
            // 구조물(터널·옹벽·상판·교각): 에셋이 없으면 만들고, 있으면 그대로(사람이 바꾼 재질 유지)
            { "wall", MakeMat("RG_Concrete", new Color(0.62f, 0.62f, 0.60f), 0.08f) },
            { "tunnel_tile", MakeMat("RG_TunnelTile", new Color(0.86f, 0.85f, 0.80f), 0.35f) },
            { "deck_under", MakeMat("RG_DeckUnder", new Color(0.48f, 0.48f, 0.46f), 0.05f) },
            { "water", MakeMat("RG_Water", new Color(0.13f, 0.30f, 0.40f), 0.85f) },
            // 광안대교: 흰 도장 강재·주탑, 케이블, 가로등·항공 장애등(빛남)
            { "gw_steel", MakeMat("RG_GwSteel", new Color(0.86f, 0.87f, 0.89f), 0.35f) },
            { "gw_steel2", MakeMat("RG_GwSteel2", new Color(0.74f, 0.76f, 0.79f), 0.3f) },
            { "gw_tower", MakeMat("RG_GwTower", new Color(0.93f, 0.93f, 0.92f), 0.25f) },
            { "gw_tower2", MakeMat("RG_GwTower2", new Color(0.82f, 0.83f, 0.85f), 0.25f) },
            { "gw_cable", MakeMat("RG_GwCable", new Color(0.78f, 0.80f, 0.83f), 0.45f) },
            { "lamp", MakeEmissive("RG_Lamp", new Color(1f, 0.93f, 0.75f), 2.0f) },
            { "gw_red", MakeEmissive("RG_AviationRed", new Color(1f, 0.12f, 0.06f), 3.0f) },
            { "gw_led", MakeEmissive("RG_GwLed", new Color(0.55f, 0.75f, 1f), 4.0f) },
        };
        // 건물: 예전 지도 재질(용도별 벽 색 + 창문 무늬) 그대로, 없으면 비슷한 색
        string[] pal = { "DCD7CD", "EBE6DB", "D8CEBF", "CBD3DA", "E2CDB1", "E5DCDC", "C8C3B9", "D6C5AB", "BAB5AB", "B7C5D2" };
        for (int k = 0; k < 10; k++)
        {
            var m = AssetDatabase.LoadAssetAtPath<Material>(OldMap + "Materials/Building_Wall_" + k + ".mat");
            if (m == null) { Color c; ColorUtility.TryParseHtmlString("#" + pal[k], out c); m = MakeMat("RG_BuildingWall_" + k, c, 0.2f); }
            d["bld_wall_" + k] = m;
        }
        var roof = AssetDatabase.LoadAssetAtPath<Material>(OldMap + "Materials/Building_Roof.mat");
        d["bld_roof"] = roof != null ? roof : MakeMat("RG_BuildingRoof", new Color(0.70f, 0.69f, 0.66f), 0.1f);
        return d;
    }
    static void Retint(Material m, Color oldC, Color newC, float smooth)
    {
        if (m == null || !m.HasProperty("_BaseColor")) return;
        var c = m.GetColor("_BaseColor");
        if (Mathf.Abs(c.r - oldC.r) + Mathf.Abs(c.g - oldC.g) + Mathf.Abs(c.b - oldC.b) > 0.01f) return;
        m.SetColor("_BaseColor", newC); if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", smooth);
        EditorUtility.SetDirty(m);
    }
    static Material MakeEmissive(string name, Color c, float intensity)
    {
        var m = MakeMat(name, c, 0.5f);
        if (m.HasProperty("_EmissionColor") && !m.IsKeywordEnabled("_EMISSION"))
        {
            m.EnableKeyword("_EMISSION"); m.SetColor("_EmissionColor", c * intensity);
            m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive; EditorUtility.SetDirty(m);
        }
        return m;
    }
    static Material MakeMat(string name, Color c, float smooth)
    {
        EnsureFolder(ROOT + "Materials");
        string path = ROOT + "Materials/" + name + ".mat";
        var m = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (m != null) return m;
        var sh = Shader.Find("Universal Render Pipeline/Lit"); if (sh == null) sh = Shader.Find("Standard");
        m = new Material(sh);
        if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c);
        if (m.HasProperty("_Color")) m.SetColor("_Color", c);
        if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", smooth);
        if (m.HasProperty("_Glossiness")) m.SetFloat("_Glossiness", smooth);
        AssetDatabase.CreateAsset(m, path);
        return m;
    }

    // ---------- 예전 지도 구조물 ----------
    static readonly string[] OldSections = { "A_Campus", "B_Jungangdaero", "C_YunsanTunnel", "D_Beonyeongro", "E_Suyeonggang", "F_Centum", "G_GwanganBridge", "H_Namcheon_PKNU" };
    /// <summary>예전 지도 구간 FBX 에서 정문·NC백화점·부경대 정문(Landmark*)과 1호선 고가(RailViaduct)만 가져옴. 바닥 면(아스팔트·포장·화단·연석·노면표시)은 새 도로·인도 자리를 잘라 냄</summary>
    static string PlaceOldStructures(Transform genRoot, RoadGenerator gen, List<Mesh> meshes)
    {
        var place = typeof(PNUSceneBuilder).GetMethod("Place", BindingFlags.NonPublic | BindingFlags.Static);
        if (place == null) return "예전 구조물: PNUSceneBuilder.Place 를 못 찾음";
        var t = Child(genRoot, "예전 구조물(정문·NC백화점·부경대 정문·1호선 고가)");
        RoadCover cov = null; int nObj = 0, cut = 0, tri0 = 0, tri1 = 0;
        foreach (var s in OldSections)
        {
            string path = OldMap + "Map/Section_" + s + ".fbx";
            if (AssetDatabase.LoadAssetAtPath<GameObject>(path) == null) continue;
            var go = place.Invoke(null, new object[] { path, t }) as GameObject; if (go == null) continue;
            if (PrefabUtility.IsPartOfPrefabInstance(go)) PrefabUtility.UnpackPrefabInstance(go, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
            go.name = "예전 " + s;
            foreach (var mf in go.GetComponentsInChildren<MeshFilter>(true).ToList())
            {
                if (mf == null) continue;
                string n = mf.gameObject.name; bool lm = n.Contains("Landmark"), rail = n.Contains("RailViaduct");
                if (!lm && !rail) { if (mf.gameObject != go) Object.DestroyImmediate(mf.gameObject); continue; }
                nObj++;
                if (lm) mf.transform.position += Vector3.down * 0.25f;                    // 새 지형이 예전보다 평균 0.25m 낮음
                bool ground = n.Contains("LandmarkAsphalt") || n.Contains("LandmarkMarking") || n.Contains("LandmarkCurb") || n.Contains("LandmarkPavers") || n.Contains("LandmarkGreen");
                if (!ground || mf.sharedMesh == null) continue;
                if (cov == null) cov = new RoadCover(gen);
                var src = mf.sharedMesh; var M = mf.transform.localToWorldMatrix; var vs = src.vertices;
                var dst = Object.Instantiate(src); dst.name = src.name + " (새 도로 자리 뺌)";
                for (int sm = 0; sm < src.subMeshCount; sm++)
                {
                    var tr = src.GetTriangles(sm); var keep = new List<int>(tr.Length);
                    for (int k = 0; k + 2 < tr.Length; k += 3)
                    {
                        Vector3 c = M.MultiplyPoint3x4((vs[tr[k]] + vs[tr[k + 1]] + vs[tr[k + 2]]) / 3f); tri0++;
                        if (cov.At(c.x, c.z) != 0) { cut++; continue; }
                        keep.Add(tr[k]); keep.Add(tr[k + 1]); keep.Add(tr[k + 2]); tri1++;
                    }
                    dst.SetTriangles(keep, sm);
                }
                mf.sharedMesh = dst; meshes.Add(dst);
            }
        }
        return "예전 구조물: " + nObj + "개(바닥 면 삼각형 " + tri0 + " 중 새 도로 자리 " + cut + " 뺌)";
    }

    // ---------- 운전 테스트(자차 + 운전석 + 주행 재생) ----------
    static string MakeDriveRig(Transform genRoot, RouteResult rr)
    {
        AssetDatabase.ImportAsset(DriveJsonPath);
        var driveTA = AssetDatabase.LoadAssetAtPath<TextAsset>(DriveJsonPath);
        if (driveTA == null) return "운전 테스트: " + DriveJsonPath + " 없음";
        var t = Child(genRoot, "운전 테스트(자차 · 운전석 · 재생)");
        var ego = new GameObject("Ego_Car"); ego.transform.SetParent(t, false);
        Transform eye = null, rig = null; PNU.PNUCockpit cockpit = null; var log = new System.Text.StringBuilder();
        var teslaFbx = AssetDatabase.LoadAssetAtPath<GameObject>(OldMap + "Vehicles/Tesla_Ego.fbx");
        var teslaTA = AssetDatabase.LoadAssetAtPath<TextAsset>(OldMap + "Data/tesla_ego.json");
        var bt = typeof(PNUSceneBuilder).GetMethod("BuildTesla", BindingFlags.NonPublic | BindingFlags.Static);
        if (teslaFbx != null && teslaTA != null && bt != null)
        {
            var args = new object[] { ego.transform, teslaFbx, teslaTA, log, null, null };
            cockpit = bt.Invoke(null, args) as PNU.PNUCockpit; eye = args[4] as Transform; rig = args[5] as Transform;
        }
        else
        {   // 테슬라가 없으면 상자
            var body = GameObject.CreatePrimitive(PrimitiveType.Cube); body.name = "Body"; body.transform.SetParent(ego.transform, false);
            body.transform.localScale = new Vector3(1.9f, 1.4f, 4.7f); body.transform.localPosition = new Vector3(0, 0.75f, 0);
            Object.DestroyImmediate(body.GetComponent<Collider>()); log.Append("테슬라 없음 — 상자 자차");
        }
        var camGo = new GameObject("Main Camera"); camGo.tag = "MainCamera"; camGo.transform.SetParent(t, false);
        var cam = camGo.AddComponent<Camera>(); cam.nearClipPlane = 0.2f; cam.farClipPlane = 20000f; cam.fieldOfView = 55f; camGo.AddComponent<AudioListener>();
        var player = new GameObject("PNU_Drive").AddComponent<PNU.PNUDrivePlayer>(); player.transform.SetParent(t, false);
        player.driveJson = driveTA; player.ego = ego.transform; player.cam = cam; player.driverEye = eye; player.cameraRig = rig; player.showEgoInDriver = cockpit != null;
        if (cockpit != null) cockpit.player = player;
        // 편집 화면에서도 출발 자리에(재생하면 경로를 따라감)
        if (rr != null && rr.x.Count > 1)
        {
            var p0 = new Vector3((float)rr.x[0], (float)rr.y[0], (float)rr.z[0]); var p1 = new Vector3((float)rr.x[Mathf.Min(10, rr.x.Count - 1)], (float)rr.y[Mathf.Min(10, rr.x.Count - 1)], (float)rr.z[Mathf.Min(10, rr.x.Count - 1)]);
            ego.transform.position = p0; var f = p1 - p0; f.y = 0; if (f.sqrMagnitude > 1e-4f) ego.transform.rotation = Quaternion.LookRotation(f.normalized, Vector3.up);
            camGo.transform.position = p0 - (f.sqrMagnitude > 1e-4f ? f.normalized * 12f : Vector3.forward * 12f) + Vector3.up * 5f; camGo.transform.LookAt(p0 + Vector3.up * 1.2f);
        }
        return "운전 테스트: 자차 " + (cockpit != null ? "테슬라(운전석)" : "상자") + " · 재생 Play → Space 출발/멈춤 · C 운전석↔따라가기 · R 정면" + (log.Length > 0 ? " · " + log.ToString().Trim() : "");
    }

    // ---------- 도우미 ----------
    public static void EnsureFolder(string path)
    {
        path = path.TrimEnd('/');
        if (AssetDatabase.IsValidFolder(path)) return;
        string parent = Path.GetDirectoryName(path).Replace('\\', '/');
        EnsureFolder(parent);
        AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
    }
    static Transform Child(Transform parent, string name)
    {
        var g = new GameObject(name);
        g.transform.SetParent(parent, false);
        return g.transform;
    }
    public static void LookAt(PNURoadGenRoot root, double x, double z, float size)
    {
        var sv = SceneView.lastActiveSceneView; if (sv == null) return;
        float y = 30f;
        var mf = root.GetComponentsInChildren<MeshFilter>(true).FirstOrDefault();
        if (mf != null && mf.sharedMesh != null) y = mf.sharedMesh.bounds.center.y;
        sv.LookAt(new Vector3((float)x, y, (float)z), Quaternion.Euler(50f, 20f, 0f), size, false, true);
    }
    public static void Check(PNURoadGenRoot root)
    {
        // 겹침 검사: 지금 씬의 도로·교차로 메쉬로(위를 보는 면끼리 비슷한 높이로 겹치는 곳)
        var list = new List<MeshData>();
        foreach (var mf in root.GetComponentsInChildren<MeshFilter>(true))
        {
            if (mf.sharedMesh == null || mf.GetComponent<PNURoadPiece>() == null && mf.GetComponent<PNUJunctionPiece>() == null) continue;
            var me = mf.sharedMesh; var md = new MeshData { name = mf.name };
            foreach (var v in me.vertices) md.v.Add(new V3(v.x, v.y, v.z));
            var mrs = mf.GetComponent<MeshRenderer>().sharedMaterials;
            for (int s = 0; s < me.subMeshCount; s++)
            {
                string k = mrs[s] != null && (mrs[s] == root.markWhite || mrs[s] == root.markYellow) ? "mark" + s : "m" + s;
                md.tris[k] = new List<int>(me.GetTriangles(s));
            }
            list.Add(md);
        }
        Dictionary<string, int> pairs;
        var hits = MeshCheck.Overlaps(list, 0.5, 0.3, out pairs);
        string msg = "겹침 검사: " + hits.Count + "칸(0.5m), 조각 쌍 " + pairs.Count;
        foreach (var kv in pairs.OrderByDescending(k => k.Value).Take(10)) { var ab = kv.Key.Split('-'); msg += "\n  " + kv.Value + "  " + list[int.Parse(ab[0])].name + " × " + list[int.Parse(ab[1])].name; }
        root.lastLog = msg;
        Debug.Log("PNU 도로 생성기 " + msg);
    }
}

// =====================================================================
// 인스펙터
// =====================================================================
[CustomEditor(typeof(PNURoadGenRoot))]
public class PNURoadGenRootEditor : Editor
{
    public override void OnInspectorGUI()
    {
        var r = (PNURoadGenRoot)target;
        EditorGUILayout.HelpBox("노드링크(방향별 선) → 도로·교차로·인도·노면표시·지형·신호등 자리를 만들어요.\n도로·교차로 조각에서 고친 값은 [다시 만들기] 해도 유지돼요.", MessageType.Info);
        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button("다시 만들기", GUILayout.Height(30))) PNURoadGenBuilder.Rebuild(r);
            if (GUILayout.Button("겹침 검사", GUILayout.Height(30))) PNURoadGenBuilder.Check(r);
        }
        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button("범위: 경로 전체")) { r.x0 = -4100; r.z0 = -6600; r.x1 = 2000; r.z1 = 6500; EditorUtility.SetDirty(r); }
            if (GUILayout.Button("범위: 시작 구간(부산대 앞)")) { r.x0 = -3900; r.z0 = 4300; r.x1 = -2700; r.z1 = 5400; EditorUtility.SetDirty(r); }
        }
        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button("시작 지점 보기")) PNURoadGenBuilder.LookAt(r, -3300, 4950, 160);
            if (GUILayout.Button("전체 보기")) PNURoadGenBuilder.LookAt(r, -1050, -50, 7000);
        }
        if (!string.IsNullOrEmpty(r.lastLog)) EditorGUILayout.HelpBox(r.lastLog, MessageType.None);
        GUILayout.Space(6);
        // 생성 규칙은 한글 이름·묶음으로(PNUGenParamsGUI), 나머지는 기본 모양
        serializedObject.Update();
        var it = serializedObject.GetIterator(); bool enter = true;
        while (it.NextVisible(enter))
        {
            enter = false;
            if (it.name == "m_Script") continue;
            if (it.name == "settings") { PNUGenParamsGUI.Draw(it); continue; }
            EditorGUILayout.PropertyField(it, true);
        }
        serializedObject.ApplyModifiedProperties();
    }
}

[CustomEditor(typeof(PNURoadPiece))]
public class PNURoadPieceEditor : Editor
{
    public override void OnInspectorGUI()
    {
        var p = (PNURoadPiece)target;
        EditorGUILayout.HelpBox("고친 값은 노드링크 ID 키로 기억돼요. 고친 뒤 [이 설정으로 다시 만들기].", MessageType.Info);
        DrawDefaultInspector();
        GUILayout.Space(6);
        if (GUILayout.Button("이 설정으로 다시 만들기", GUILayout.Height(28)))
        {
            var root = p.GetComponentInParent<PNURoadGenRoot>();
            if (root != null) { var key = p.key; PNURoadGenBuilder.Rebuild(root); Reselect<PNURoadPiece>(root, key); }
        }
    }
    internal static void Reselect<T>(PNURoadGenRoot root, string key) where T : MonoBehaviour
    {
        foreach (var c in root.GetComponentsInChildren<T>(true))
        {
            string k = c is PNURoadPiece ? ((PNURoadPiece)(object)c).key : c is PNUJunctionPiece ? ((PNUJunctionPiece)(object)c).key : null;
            if (k == key) { Selection.activeGameObject = c.gameObject; return; }
        }
    }
}

[CustomEditor(typeof(PNUJunctionPiece))]
public class PNUJunctionPieceEditor : Editor
{
    public override void OnInspectorGUI()
    {
        var p = (PNUJunctionPiece)target;
        EditorGUILayout.HelpBox("횡단보도 종류·색, 대각선, 모서리 반경을 바꾸고 [이 설정으로 다시 만들기].", MessageType.Info);
        DrawDefaultInspector();
        GUILayout.Space(6);
        if (GUILayout.Button("이 설정으로 다시 만들기", GUILayout.Height(28)))
        {
            var root = p.GetComponentInParent<PNURoadGenRoot>();
            if (root != null) { var key = p.key; PNURoadGenBuilder.Rebuild(root); PNURoadPieceEditor.Reselect<PNUJunctionPiece>(root, key); }
        }
    }
}
