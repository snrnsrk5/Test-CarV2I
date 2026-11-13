// 메뉴: PNU Map > Build Drive Scene (v3)
// Assets/PNU_Map3/ 의 FBX·데이터로 주행 씬을 만들고 재질·신호등·재생 컴포넌트를 연결함
// - 자차: Vehicles/Tesla_Ego.fbx + Data/tesla_ego.json 이 있으면 테슬라 모델 3(외관·운전석·운전대·화면), 없으면 상자
// - Assets/PNU_Map3/Editor/run_build.txt 가 있으면 컴파일 직후 한 번 자동 실행하고 지움
// 결과: Assets/PNU_Map3/Scenes/Drive.unity, 프로젝트 폴더/Captures/v3_*.png
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using PNU;

[InitializeOnLoad]
public static class PNUSceneBuilder
{
    const string ROOT = "Assets/PNU_Map3/";
    const string Trigger = ROOT + "Editor/run_build.txt";

    static PNUSceneBuilder()
    {
        EditorApplication.delayCall += RunTriggerIfPresent;
    }
    /// <summary>run_build.txt 가 있으면 지우고 씬을 다시 만듦(스크립트가 안 바뀌어도: 아래 PNUBuildTrigger 가 파일 가져오기 때 부름)</summary>
    public static void RunTriggerIfPresent()
    {
        if (File.Exists(Trigger)) { AssetDatabase.DeleteAsset(Trigger); Build(); }
    }

    // ---------- 재질 ----------
    static Shader LitShader()
    {
        var sh = Shader.Find("Universal Render Pipeline/Lit");
        if (sh == null) sh = Shader.Find("Standard");
        return sh;
    }
    static Material Mat(string name, Color c, string tex = null, float smooth = 0.15f)
    {
        if (!AssetDatabase.IsValidFolder(ROOT + "Materials")) AssetDatabase.CreateFolder(ROOT.TrimEnd('/'), "Materials");
        string path = ROOT + "Materials/" + name + ".mat";
        var m = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (m == null) { m = new Material(LitShader()); AssetDatabase.CreateAsset(m, path); }
        if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c);
        m.color = c;
        if (tex != null)
        {
            var t = LoadTex(tex);
            if (t != null) { if (m.HasProperty("_BaseMap")) m.SetTexture("_BaseMap", t); m.mainTexture = t; }
        }
        if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", smooth);
        m.enableInstancing = true;
        EditorUtility.SetDirty(m);
        return m;
    }
    static Texture2D LoadTex(string file)
    {
        foreach (var dir in new[] { ROOT + "Textures/", ROOT + "Vehicles/Textures/", "Assets/PNU_Map/Textures/" })
        {
            var t = AssetDatabase.LoadAssetAtPath<Texture2D>(dir + file);
            if (t != null) return t;
        }
        return null;
    }
    static void MakeTransparent(Material m, float alpha)
    {
        var c = m.color; c.a = alpha; m.color = c; if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c);
        if (m.HasProperty("_Surface")) m.SetFloat("_Surface", 1f);
        if (m.HasProperty("_Blend")) m.SetFloat("_Blend", 0f);
        m.SetOverrideTag("RenderType", "Transparent");
        m.SetInt("_SrcBlend", (int)BlendMode.SrcAlpha); m.SetInt("_DstBlend", (int)BlendMode.OneMinusSrcAlpha); m.SetInt("_ZWrite", 0);
        m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        m.renderQueue = (int)RenderQueue.Transparent;
        DoubleSided(m);
    }
    static void DoubleSided(Material m) { if (m.HasProperty("_Cull")) m.SetFloat("_Cull", 0f); m.doubleSidedGI = true; }
    static void Emissive(Material m, Color e)
    {
        m.EnableKeyword("_EMISSION"); if (m.HasProperty("_EmissionColor")) m.SetColor("_EmissionColor", e);
        m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
    }
    static Material MatFor(string fbxName)
    {
        string n = fbxName.Replace(" (Instance)", "");
        if (n.StartsWith("Col_") && n.Length >= 10)
        {
            Color c;
            if (ColorUtility.TryParseHtmlString("#" + n.Substring(4, 6), out c)) return Mat(n.Substring(0, 10), c);
        }
        if (n.StartsWith("Asphalt")) return Mat("Asphalt", new Color(0.72f, 0.72f, 0.72f), "Asphalt_Base.png", 0.1f);
        if (n.StartsWith("Pavers")) return Mat("Pavers", new Color(0.92f, 0.89f, 0.84f), "Pavers_1.jpg");
        if (n.StartsWith("Curb")) { var m = Mat("Curb", new Color(0.81f, 0.80f, 0.77f)); DoubleSided(m); return m; }
        if (n.StartsWith("Road_Text"))
        {
            var m = Mat("Road_Text", Color.white, "roadtext.png", 0.2f);
            if (m.HasProperty("_AlphaClip")) { m.SetFloat("_AlphaClip", 1f); m.SetFloat("_Cutoff", 0.4f); m.EnableKeyword("_ALPHATEST_ON"); }
            return m;
        }
        if (n.StartsWith("Concrete_Cap")) return Mat("Concrete_Cap", new Color(0.72f, 0.71f, 0.69f));
        if (n.StartsWith("Concrete")) return Mat("Concrete", new Color(0.76f, 0.76f, 0.74f));
        if (n.StartsWith("Barrier")) return Mat("Barrier", new Color(0.84f, 0.83f, 0.80f));
        if (n.StartsWith("Median_Green")) return Mat("Median_Green", new Color(0.44f, 0.54f, 0.32f));
        if (n.StartsWith("Tunnel_Wall")) { var m = Mat("Tunnel_Wall", Color.white, "tunnel.jpg", 0.3f); DoubleSided(m); return m; }
        if (n.StartsWith("Retaining_Wall")) { var m = Mat("Retaining_Wall", Color.white, "wall.jpg"); DoubleSided(m); return m; }
        if (n.StartsWith("SoundWall_Glass")) { var m = Mat("SoundWall_Glass", new Color(0.56f, 0.81f, 0.70f), null, 0.8f); MakeTransparent(m, 0.42f); return m; }
        if (n.StartsWith("TunnelLight")) { var m = Mat("TunnelLight", new Color(1f, 0.77f, 0.45f)); Emissive(m, new Color(1f, 0.7f, 0.35f) * 2f); return m; }
        if (n.StartsWith("Wire")) { var m = Mat("Wire", new Color(0.16f, 0.17f, 0.18f)); DoubleSided(m); return m; }
        if (n.StartsWith("Terrain"))
        {
            var m = Mat("Terrain_Landcover", Color.white, "Terrain_Landcover.jpg", 0.05f);
            var det = LoadTex("Terrain_Detail.jpg");                                     // 지형 잔결: 안쪽 지형 9520m x 15160m 를 4m 칸으로 반복
            if (det != null && m.HasProperty("_DetailAlbedoMap"))
            {
                m.SetTexture("_DetailAlbedoMap", det); m.SetTextureScale("_DetailAlbedoMap", new Vector2(2380f, 3790f));
                m.SetFloat("_DetailAlbedoMapScale", 1f); m.EnableKeyword("_DETAIL_MULX2");
                if (m.HasProperty("_DetailMask")) m.SetTexture("_DetailMask", Texture2D.whiteTexture);
            }
            return m;
        }
        if (n.StartsWith("Building_Wall_") && n.Length >= 15)
        {
            int k = 0; int.TryParse(n.Substring(14, 1), out k);
            var pal = new[] { "DCD7CD", "EBE6DB", "D8CEBF", "CBD3DA", "E2CDB1", "E5DCDC", "C8C3B9", "D6C5AB", "BAB5AB", "B7C5D2" };
            Color c; ColorUtility.TryParseHtmlString("#" + pal[Mathf.Clamp(k, 0, 9)], out c);
            return Mat("Building_Wall_" + k, c, "Building_Facade.png", k == 9 ? 0.55f : 0.2f);   // 창문 텍스처(UV: 가로 3m·세로 3.3m 한 칸 반복)
        }
        if (n.StartsWith("Building_Roof")) return Mat("Building_Roof", new Color(0.70f, 0.69f, 0.66f));
        if (n.StartsWith("Building")) return Mat("Building_Wall", new Color(0.86f, 0.84f, 0.80f));
        if (n.StartsWith("Water")) return Mat("Water", new Color(0.37f, 0.58f, 0.73f), null, 0.85f);
        if (n.StartsWith("Steel")) { var m = Mat("Steel", new Color(0.87f, 0.89f, 0.91f), null, 0.5f); DoubleSided(m); return m; }
        return null;
    }

    static GameObject Place(string assetPath, Transform parent)
    {
        var src = AssetDatabase.LoadAssetAtPath<GameObject>(assetPath);
        if (src == null) { Debug.LogWarning("PNUSceneBuilder: 못 찾음 " + assetPath); return null; }
        var go = (GameObject)PrefabUtility.InstantiatePrefab(src);
        go.transform.SetParent(parent, false);
        foreach (var r in go.GetComponentsInChildren<MeshRenderer>())
        {
            var mats = r.sharedMaterials;
            for (int i = 0; i < mats.Length; i++)
            {
                if (mats[i] == null) continue;
                var m = MatFor(mats[i].name);
                if (m != null) mats[i] = m;
            }
            r.sharedMaterials = mats;
            r.shadowCastingMode = r.name.Contains("Terrain") ? ShadowCastingMode.Off : ShadowCastingMode.On;
        }
        return go;
    }

    // ---------- 신호등 모델 ----------
    static GameObject FindTrafficLightFbx()
    {
        var go = AssetDatabase.LoadAssetAtPath<GameObject>(ROOT + "Modules/TrafficLight_Variants.fbx");
        if (go != null) return go;
        go = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/PNU_Map/Modules/TrafficLightAnim.fbx");
        if (go != null) return go;
        foreach (var guid in AssetDatabase.FindAssets("TrafficLightAnim t:GameObject"))
        {
            go = AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GUIDToAssetPath(guid));
            if (go != null) return go;
        }
        return null;
    }
    static void PaintSignal(GameObject g)
    {
        var red = Mat("Lamp_Red", new Color(1f, 0.15f, 0.1f)); Emissive(red, new Color(1f, 0.1f, 0.05f) * 3f);
        var yel = Mat("Lamp_Yellow", new Color(1f, 0.7f, 0.1f)); Emissive(yel, new Color(1f, 0.6f, 0.05f) * 3f);
        var grn = Mat("Lamp_Green", new Color(0.15f, 1f, 0.45f)); Emissive(grn, new Color(0.1f, 1f, 0.4f) * 3f);
        var off = Mat("Lamp_Off", new Color(0.09f, 0.09f, 0.09f));
        var blk = Mat("Signal_Black", new Color(0.06f, 0.06f, 0.06f));
        var metal = Mat("Signal_Metal", new Color(0.86f, 0.87f, 0.88f), null, 0.4f);
        foreach (var r in g.GetComponentsInChildren<MeshRenderer>(true))
        {
            string n = r.name; Material m;
            bool pedDigitOff = n.Contains("HN_") && n.EndsWith(".001");
            if (!n.Contains("HN_")) { int dot = n.IndexOf('.'); if (dot > 0) n = n.Substring(0, dot); }
            if (n.StartsWith("Off_") || pedDigitOff) m = off;
            else if (n.StartsWith("On_Red")) m = red;
            else if (n.StartsWith("On_Yellow")) m = yel;
            else if (n.StartsWith("On_GreenHN_")) m = red;           // 남은 시간 숫자
            else if (n.StartsWith("On_")) m = grn;
            else if (n == "Black") m = blk;
            else m = null;
            var mats = r.sharedMaterials;
            for (int i = 0; i < mats.Length; i++)
            {
                if (m != null) mats[i] = m;
                else
                {
                    string mn = mats[i] != null ? mats[i].name : "";
                    mats[i] = mn.Contains("0D0D0D") ? blk : (mn.Contains("Traffic Light_On") ? off : metal);
                }
            }
            r.sharedMaterials = mats;
        }
    }
    static GameObject Template(GameObject fbx, string childName, Transform parent)
    {
        if (fbx == null) return null;
        Transform found = null;
        foreach (var tr in fbx.GetComponentsInChildren<Transform>(true)) if (tr.name == childName) { found = tr; break; }
        if (found == null) return null;
        var g = Object.Instantiate(found.gameObject, parent);
        g.name = childName + "_Template"; g.transform.localPosition = Vector3.zero; g.transform.localRotation = Quaternion.identity;
        foreach (var r in g.GetComponentsInChildren<Transform>(true)) if (r != g.transform && r.localScale == Vector3.zero) r.localScale = Vector3.one;
        PaintSignal(g); g.SetActive(false);
        return g;
    }

    // ---------- 빌드 ----------
    [MenuItem("PNU Map/Build Drive Scene (v3)")]
    public static void Build()
    {
        var log = new StringBuilder();
        var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);
        var root = new GameObject("PNU_Map3").transform;
        var mapRoot = new GameObject("Map").transform; mapRoot.SetParent(root, false);
        string[] secs = { "A_Campus", "B_Jungangdaero", "C_YunsanTunnel", "D_Beonyeongro", "E_Suyeonggang", "F_Centum", "G_GwanganBridge", "H_Namcheon_PKNU" };
        // OSM에 없는 시가지 채움 건물(상자): 따로 묶어 켜고 끌 수 있게
        var fillRoot = new GameObject("BuildingsFill (OSM에 없는 채움 건물)").transform; fillRoot.SetParent(mapRoot, false);
        foreach (var s in secs)
        {
            var g = Place(ROOT + "Map/Section_" + s + ".fbx", mapRoot); if (g != null) log.AppendLine("section " + s);
            Place(ROOT + "Map/Buildings_" + s + ".fbx", mapRoot);
            if (AssetDatabase.LoadAssetAtPath<GameObject>(ROOT + "Map/BuildingsFill_" + s + ".fbx") != null) Place(ROOT + "Map/BuildingsFill_" + s + ".fbx", fillRoot);
        }
        Place(ROOT + "Map/Buildings_Far.fbx", mapRoot);
        Place(ROOT + "Map/Gwangan_Structure.fbx", mapRoot);
        Place(ROOT + "Map/Bridges_Outside.fbx", mapRoot);
        if (AssetDatabase.LoadAssetAtPath<GameObject>("Assets/PNU_Map/Map/Background.fbx") != null) Place("Assets/PNU_Map/Map/Background.fbx", mapRoot);
        else log.AppendLine("Background.fbx 없음(Assets/PNU_Map/Map) — 바깥 지형 생략");

        // 데이터
        var driveTA = AssetDatabase.LoadAssetAtPath<TextAsset>(ROOT + "Data/pnu_drive.json");
        var npcTA = AssetDatabase.LoadAssetAtPath<TextAsset>(ROOT + "Data/npc_tracks.json");
        if (driveTA == null) { Debug.LogError("pnu_drive.json 없음"); return; }
        var D = JsonUtility.FromJson<DriveData>(driveTA.text);
        SignalPlans.Load(D.plans);

        // 신호등
        var sigRoot = new GameObject("Signals").transform; sigRoot.SetParent(root, false);
        var fbx = FindTrafficLightFbx();
        var tCar3 = Template(fbx, "TrafficLight_Car_3H", sigRoot) ?? Template(fbx, "TrafficLight_Car_4H_3C", sigRoot);
        var tCar2 = Template(fbx, "TrafficLight_Car_2H", sigRoot) ?? tCar3;
        var tCar1 = Template(fbx, "TrafficLight_Car_1H", sigRoot) ?? tCar2;
        var tNum = Template(fbx, "TrafficLight_Human_Number", sigRoot);
        var tBar = Template(fbx, "TrafficLight_Human_Bar", sigRoot);
        int nCar = 0, nPed = 0;
        var sigObjs = new List<GameObject>();
        foreach (var s in D.signals)
        {
            GameObject tCar = s.heads <= 1 ? tCar1 : (s.heads == 2 ? tCar2 : tCar3);
            GameObject tpl = s.kind == 0 ? tCar : (s.style == "bar" ? (tBar ?? tNum) : (tNum ?? tBar));
            GameObject g;
            if (tpl != null) { g = Object.Instantiate(tpl, sigRoot); g.SetActive(true); }
            else { g = GameObject.CreatePrimitive(PrimitiveType.Cylinder); g.transform.SetParent(sigRoot, false); Object.DestroyImmediate(g.GetComponent<Collider>()); g.transform.localScale = new Vector3(0.3f, s.kind == 0 ? 4f : 1.6f, 0.3f); }
            g.name = (s.kind == 0 ? "CarSignal_" : "PedSignal_") + s.junction + "_" + s.arm;
            g.transform.position = new Vector3(s.x, s.y, s.z);
            // 차량 모델 앞면 -Z, 보행 모델 앞면 -X → 보행은 +90도
            g.transform.rotation = Quaternion.Euler(0, s.kind == 0 ? s.yawDeg : s.yawDeg + 90f, 0);
            var sl = g.AddComponent<PNUSignalLight>(); sl.junction = s.junction; sl.arm = s.arm; sl.kind = s.kind; sl.style = s.style;
            sl.Bind(); sl.Apply(0f);
            if (s.kind == 0) nCar++; else nPed++;
            sigObjs.Add(g);
        }
        log.AppendLine("signals car " + nCar + " ped " + nPed + (fbx == null ? " (TrafficLightAnim.fbx 못 찾음: 기둥만)" : ""));
        AttachSigns(sigRoot, sigObjs, D, log);

        // 가로수·가로등
        var inst = new GameObject("TreesLamps").AddComponent<PNUInstancer>(); inst.transform.SetParent(root, false);
        var cyl = GameObject.CreatePrimitive(PrimitiveType.Cylinder); var sph = GameObject.CreatePrimitive(PrimitiveType.Sphere); var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
        inst.trunk = cyl.GetComponent<MeshFilter>().sharedMesh; inst.crown = sph.GetComponent<MeshFilter>().sharedMesh;
        inst.pole = cyl.GetComponent<MeshFilter>().sharedMesh; inst.head = cube.GetComponent<MeshFilter>().sharedMesh;
        Object.DestroyImmediate(cyl); Object.DestroyImmediate(sph); Object.DestroyImmediate(cube);
        inst.trunkMat = Mat("Tree_Trunk", new Color(0.42f, 0.35f, 0.27f)); inst.crownMat = Mat("Tree_Crown", new Color(0.37f, 0.54f, 0.29f));
        inst.poleMat = Mat("Lamp_Pole", new Color(0.6f, 0.63f, 0.65f), null, 0.4f); inst.headMat = Mat("Lamp_Head", new Color(0.95f, 0.94f, 0.9f));
        inst.driveJson = driveTA;

        // 자차: 테슬라 모델 3(있으면) / 상자 자리표시
        var ego = new GameObject("Ego_Car");
        Transform eye = null, rig = null; PNUCockpit cockpit = null;
        var teslaFbx = AssetDatabase.LoadAssetAtPath<GameObject>(ROOT + "Vehicles/Tesla_Ego.fbx");
        var teslaTA = AssetDatabase.LoadAssetAtPath<TextAsset>(ROOT + "Data/tesla_ego.json");
        if (teslaFbx != null && teslaTA != null) cockpit = BuildTesla(ego.transform, teslaFbx, teslaTA, log, out eye, out rig);
        else
        {
            log.AppendLine("Tesla_Ego.fbx / tesla_ego.json 없음 — 상자 자차");
            var body = GameObject.CreatePrimitive(PrimitiveType.Cube); body.name = "Body"; body.transform.SetParent(ego.transform, false);
            body.transform.localScale = new Vector3(1.9f, 0.8f, 4.7f); body.transform.localPosition = new Vector3(0, 0.66f, 0);
            body.GetComponent<MeshRenderer>().sharedMaterial = Mat("Ego_Orange", new Color(0.88f, 0.38f, 0.23f));
            var cab = GameObject.CreatePrimitive(PrimitiveType.Cube); cab.name = "Cabin"; cab.transform.SetParent(ego.transform, false);
            cab.transform.localScale = new Vector3(1.75f, 0.6f, 2.5f); cab.transform.localPosition = new Vector3(0, 1.34f, -0.15f);
            cab.GetComponent<MeshRenderer>().sharedMaterial = Mat("Ego_Glass", new Color(0.16f, 0.19f, 0.22f), null, 0.8f);
            Object.DestroyImmediate(body.GetComponent<Collider>()); Object.DestroyImmediate(cab.GetComponent<Collider>());
        }

        // 카메라·조명·재생
        var cam = Camera.main; if (cam == null) cam = new GameObject("Main Camera").AddComponent<Camera>();
        cam.nearClipPlane = 0.2f; cam.farClipPlane = 20000f; cam.fieldOfView = 55f;
        inst.cam = cam;
        var sun = Object.FindObjectOfType<Light>();
        if (sun != null) { sun.transform.rotation = Quaternion.Euler(48f, -35f, 0f); sun.intensity = 1.15f; sun.shadows = LightShadows.Soft; }
        RenderSettings.ambientMode = AmbientMode.Flat; RenderSettings.ambientLight = new Color(0.55f, 0.57f, 0.6f);
        RenderSettings.fog = true; RenderSettings.fogMode = FogMode.Linear; RenderSettings.fogStartDistance = 2500; RenderSettings.fogEndDistance = 26000;
        RenderSettings.fogColor = new Color(0.79f, 0.85f, 0.9f);
        var player = new GameObject("PNU_Drive").AddComponent<PNUDrivePlayer>();
        player.driveJson = driveTA; player.npcJson = npcTA; player.ego = ego.transform; player.cam = cam;
        player.driverEye = eye; player.cameraRig = rig; player.showEgoInDriver = cockpit != null;
        if (cockpit != null) cockpit.player = player;

        // 저장 + 확인 캡처
        if (!AssetDatabase.IsValidFolder(ROOT + "Scenes")) AssetDatabase.CreateFolder(ROOT.TrimEnd('/'), "Scenes");
        EditorSceneManager.SaveScene(scene, ROOT + "Scenes/Drive.unity");
        Capture(cam, D, ego.transform, log);
        if (eye != null) CaptureDriver(cam, D, ego.transform, eye, log);
        Directory.CreateDirectory("Captures");
        File.WriteAllText("Captures/v3_report.txt", log.ToString(), Encoding.UTF8);
        AssetDatabase.SaveAssets();
        Debug.Log("PNUSceneBuilder 완료\n" + log);
    }

    // ---------- 자차 테슬라 ----------
    static Material TeslaMaterial(TeslaMat t)
    {
        if (!AssetDatabase.IsValidFolder(ROOT + "Materials")) AssetDatabase.CreateFolder(ROOT.TrimEnd('/'), "Materials");
        if (!AssetDatabase.IsValidFolder(ROOT + "Materials/Tesla")) AssetDatabase.CreateFolder(ROOT + "Materials", "Tesla");
        string path = ROOT + "Materials/Tesla/" + t.name + ".mat";
        bool unlit = t.name == "TM_Screen";
        var sh = unlit ? (Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Unlit/Texture")) : LitShader();
        var m = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (m == null) { m = new Material(sh); AssetDatabase.CreateAsset(m, path); } else m.shader = sh;
        Color c = t.color != null && t.color.Length >= 3 ? new Color(t.color[0], t.color[1], t.color[2]) : Color.white;
        if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c); m.color = c;
        string texName = unlit ? "Tesla_Screen.png" : t.tex;
        if (!string.IsNullOrEmpty(texName))
        {
            var tx = AssetDatabase.LoadAssetAtPath<Texture2D>(ROOT + "Vehicles/Textures/" + texName);
            if (tx != null) { if (m.HasProperty("_BaseMap")) m.SetTexture("_BaseMap", tx); m.mainTexture = tx; }
        }
        if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", t.smooth);
        if (m.HasProperty("_Metallic")) m.SetFloat("_Metallic", t.metal);
        if (t.kind == "glass" || t.kind == "blend") MakeTransparent(m, t.alpha);
        else if (t.kind == "light") MakeTransparent(m, 1f);                  // 등 그림의 알파로 비침
        else DoubleSided(m);                                                  // 실내 얇은 판(한 면짜리)도 보이게
        EditorUtility.SetDirty(m);
        return m;
    }
    static PNUCockpit BuildTesla(Transform ego, GameObject fbx, TextAsset ta, StringBuilder log, out Transform eye, out Transform rig)
    {
        var T = JsonUtility.FromJson<TeslaData>(ta.text);
        var car = (GameObject)PrefabUtility.InstantiatePrefab(fbx); car.name = "Tesla_Model3";
        car.transform.SetParent(ego, false); car.transform.localPosition = Vector3.zero; car.transform.localRotation = Quaternion.identity;
        var plan = new Dictionary<string, TeslaMat>(); if (T.materials != null) foreach (var m in T.materials) plan[m.name] = m;
        var made = new Dictionary<string, Material>(); var missing = new HashSet<string>();
        Transform wheel = null, wf = null, wr = null; Renderer glass = null;
        foreach (var r in car.GetComponentsInChildren<MeshRenderer>(true))
        {
            var mats = r.sharedMaterials;
            for (int i = 0; i < mats.Length; i++)
            {
                if (mats[i] == null) continue;
                string n = mats[i].name.Replace(" (Instance)", ""); TeslaMat tm;
                if (!plan.TryGetValue(n, out tm)) { missing.Add(n); continue; }
                Material mm; if (!made.TryGetValue(n, out mm)) { mm = TeslaMaterial(tm); made[n] = mm; }
                mats[i] = mm;
            }
            r.sharedMaterials = mats;
            bool isGlass = r.name.StartsWith("Tesla_Glass");
            r.shadowCastingMode = isGlass ? ShadowCastingMode.Off : ShadowCastingMode.On;     // 유리 지붕으로 햇빛이 들어오게
            if (isGlass) glass = r;
            if (r.name.StartsWith("Tesla_SteeringWheel")) wheel = r.transform;
            if (r.name.StartsWith("Tesla_WheelsFront")) wf = r.transform;
            if (r.name.StartsWith("Tesla_WheelsRear")) wr = r.transform;
        }
        eye = new GameObject("DriverEye").transform; eye.SetParent(ego, false); eye.localPosition = TeslaData.V(T.eye);
        rig = new GameObject("CameraRig").transform; rig.SetParent(eye, false);
        var dm = AssetDatabase.LoadAssetAtPath<Material>(ROOT + "Materials/Tesla/Tesla_Digits.mat");
        if (dm == null)
        {
            var sh = Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Unlit/Transparent Cutout");
            dm = new Material(sh); if (!AssetDatabase.IsValidFolder(ROOT + "Materials/Tesla")) AssetDatabase.CreateFolder(ROOT + "Materials", "Tesla");
            AssetDatabase.CreateAsset(dm, ROOT + "Materials/Tesla/Tesla_Digits.mat");
        }
        var dt = AssetDatabase.LoadAssetAtPath<Texture2D>(ROOT + "Vehicles/Textures/Tesla_Digits.png");
        if (dt != null) { if (dm.HasProperty("_BaseMap")) dm.SetTexture("_BaseMap", dt); dm.mainTexture = dt; }
        if (dm.HasProperty("_AlphaClip")) { dm.SetFloat("_AlphaClip", 1f); dm.SetFloat("_Cutoff", 0.5f); dm.EnableKeyword("_ALPHATEST_ON"); }
        dm.renderQueue = 2450; DoubleSided(dm); EditorUtility.SetDirty(dm);
        var ck = ego.gameObject.AddComponent<PNUCockpit>();
        ck.teslaJson = ta; ck.steeringWheel = wheel; ck.wheelsFront = wf; ck.wheelsRear = wr; ck.glassRenderer = glass; ck.digitMaterial = dm;
        log.AppendLine("tesla: materials " + made.Count + (missing.Count > 0 ? " (계획에 없는 재질: " + string.Join(", ", missing) + ")" : "") +
                       ", wheel " + (wheel != null) + ", wheels " + (wf != null) + "/" + (wr != null) + ", glass " + (glass != null));
        return ck;
    }
    static void CaptureDriver(Camera cam, DriveData D, Transform ego, Transform eye, StringBuilder log)
    {
        float[] ss = { 400, 3300, 12300, 17600 };
        var keep = cam.transform.position; var keepR = cam.transform.rotation; float near = cam.nearClipPlane, fov = cam.fieldOfView;
        cam.nearClipPlane = 0.03f; cam.fieldOfView = 70f;
        foreach (var s in ss)
        {
            Vector3 p = EgoAt(D, s), a = EgoAt(D, s - 6), b = EgoAt(D, s + 6);
            ego.position = p; ego.rotation = Quaternion.LookRotation(b - a, Vector3.up);
            cam.transform.position = eye.position; cam.transform.rotation = ego.rotation * Quaternion.Euler(4f, 0, 0);
            Shot(cam, "v3_driver_s" + ((int)s).ToString("00000") + ".png");
        }
        cam.transform.position = keep; cam.transform.rotation = keepR; cam.nearClipPlane = near; cam.fieldOfView = fov;
        log.AppendLine("driver captures " + ss.Length);
    }

    // ---------- 표지판 부착 ----------
    static void AttachSigns(Transform sigRoot, List<GameObject> sigObjs, DriveData D, StringBuilder log)
    {
        var ta = AssetDatabase.LoadAssetAtPath<TextAsset>(ROOT + "Data/signs.json");
        if (ta == null) { log.AppendLine("signs.json 없음 — 표지판 생략"); return; }
        var S = JsonUtility.FromJson<SignsData>(ta.text);
        var atlas = Mat("Sign_Atlas", Color.white, "SignAtlas.png", 0.25f);
        if (atlas.HasProperty("_AlphaClip")) { atlas.SetFloat("_AlphaClip", 1f); atlas.SetFloat("_Cutoff", 0.5f); atlas.EnableKeyword("_ALPHATEST_ON"); }
        var back = Mat("Sign_Back", new Color(0.62f, 0.64f, 0.66f), null, 0.3f);
        var brk = Mat("Sign_Bracket", new Color(0.55f, 0.57f, 0.6f), null, 0.4f);
        int n = 0;
        foreach (var e in S.signals)
        {
            if (e.index < 0 || e.index >= sigObjs.Count) continue;
            var g = sigObjs[e.index];
            var asm = g.AddComponent<PNUSignalAssembly>();
            asm.atlasMaterial = atlas; asm.backMaterial = back; asm.bracketMaterial = brk;
            if (e.name >= 0 && e.name < S.cells.Length)
            {
                var c = S.cells[e.name];
                asm.nameSign = new SignPlacement { cell = c, localPos = new Vector3(-(0.5f + c.w * 0.5f), asm.armHeight - 0.12f - c.h * 0.5f, -0.06f) };
            }
            if (e.guide >= 0 && e.guide < S.cells.Length)
            {
                var c = S.cells[e.guide];
                asm.guideSign = new SignPlacement { cell = c, localPos = new Vector3(-(0.5f + c.w * 0.5f), asm.armHeight + 0.2f + c.h * 0.5f, -0.06f) };
            }
            if (e.regs != null)
                for (int k = 0; k < e.regs.Length; k++)
                {
                    if (e.regs[k] < 0 || e.regs[k] >= S.cells.Length) continue;
                    var c = S.cells[e.regs[k]];
                    asm.poleSigns.Add(new SignPlacement { cell = c, localPos = new Vector3(0, 3.7f - k * 0.8f, -0.3f) });
                }
            asm.Rebuild(); n++;
        }
        // 보행 신호가 차량 신호 지주 2.5m 안이면 같은 지주로 붙임
        int snapped = 0;
        for (int i = 0; i < D.signals.Length && i < sigObjs.Count; i++)
        {
            if (D.signals[i].kind != 1) continue;
            for (int k = 0; k < D.signals.Length && k < sigObjs.Count; k++)
            {
                if (D.signals[k].kind != 0 || D.signals[k].junction != D.signals[i].junction) continue;
                var a = sigObjs[i].transform.position; var b = sigObjs[k].transform.position;
                if (new Vector2(a.x - b.x, a.z - b.z).magnitude < 2.5f) { sigObjs[i].transform.position = new Vector3(b.x, a.y, b.z); snapped++; break; }
            }
        }
        log.AppendLine("signs on " + n + " signals, ped snapped " + snapped);
    }

    static Vector3 EgoAt(DriveData D, float s)
    {
        var e = D.ego; int i = 0; while (i < e.s.Length - 2 && e.s[i + 1] < s) i++;
        float f = Mathf.Clamp01((s - e.s[i]) / Mathf.Max(1e-6f, e.s[i + 1] - e.s[i]));
        return new Vector3(Mathf.Lerp(e.x[i], e.x[i + 1], f), Mathf.Lerp(e.y[i], e.y[i + 1], f), Mathf.Lerp(e.z[i], e.z[i + 1], f));
    }
    static void Shot(Camera cam, string file)
    {
        int w = 1280, h = 720;
        var rt = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32);
        cam.targetTexture = rt; cam.Render();
        var prev = RenderTexture.active; RenderTexture.active = rt;
        var tex = new Texture2D(w, h, TextureFormat.RGB24, false); tex.ReadPixels(new Rect(0, 0, w, h), 0, 0); tex.Apply();
        RenderTexture.active = prev; cam.targetTexture = null;
        Directory.CreateDirectory("Captures"); File.WriteAllBytes("Captures/" + file, tex.EncodeToPNG());
        Object.DestroyImmediate(tex); rt.Release(); Object.DestroyImmediate(rt);
    }
    static void Capture(Camera cam, DriveData D, Transform ego, StringBuilder log)
    {
        float[] ss = { 60, 880, 1950, 3000, 4400, 9580, 12300, 14000, 16750, 17600 };
        var keep = cam.transform.position; var keepR = cam.transform.rotation;
        foreach (var s in ss)
        {
            Vector3 p = EgoAt(D, s), a = EgoAt(D, s - 6), b = EgoAt(D, s + 6);
            Vector3 f = b - a; f.y = 0; f.Normalize();
            ego.position = p; ego.rotation = Quaternion.LookRotation(f, Vector3.up);
            cam.transform.position = p - f * 17f + Vector3.up * 6.2f; cam.transform.LookAt(p + f * 20f + Vector3.up * 1.6f);
            Shot(cam, "v3_s" + ((int)s).ToString("00000") + ".png");
        }
        cam.transform.position = keep; cam.transform.rotation = keepR;
        log.AppendLine("captures " + ss.Length);
    }
}

/// <summary>지도·데이터만 바뀌어 스크립트 다시 컴파일이 없을 때도 run_build.txt 를 가져오면 자동으로 다시 빌드</summary>
public class PNUBuildTrigger : AssetPostprocessor
{
    static void OnPostprocessAllAssets(string[] imported, string[] deleted, string[] moved, string[] movedFrom)
    {
        foreach (var p in imported)
            if (p.Replace('\\', '/').EndsWith("PNU_Map3/Editor/run_build.txt")) { EditorApplication.delayCall += PNUSceneBuilder.RunTriggerIfPresent; break; }
    }
}
