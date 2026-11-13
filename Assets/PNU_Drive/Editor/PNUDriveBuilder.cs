// PNU_Drive 씬 만들기: 재질(URP Lit) + 맵(PNUDriveWorld) + 자차(테슬라 모델 3, v14 / PNU01 은 메뉴로) + 주행(PNUDriveEgo) + 해·하늘·안개
// Editor/run_build.txt 가 들어오면 한 번 자동 실행하고 지움. 메뉴: PNU Drive/Build Scene
// Editor/run_car.txt(내용 Tesla 또는 PNU01) 가 들어오면 씬은 그대로 두고 자차만 바꿈. 메뉴: PNU Drive/자차: …
// 소리·길안내(v10): PNUDriveSound(코드로 만든 전기차 소리·알림음) + PNUDriveNav(길안내 카드·음성·깜빡이). 음성 안내 wav 는
//   윈도 기본 음성(System.Speech, 한국어 목소리)으로 Assets/PNU_Drive/Audio/Voice 에 만듦(한 번 만들면 다시 안 만듦). 메뉴: PNU Drive/음성 안내 다시 만들기
using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using PNU.Drive;

public class PNUDriveTrigger : AssetPostprocessor
{
    static void OnPostprocessAllAssets(string[] imported, string[] deleted, string[] moved, string[] movedFrom)
    {
        foreach (var p in imported)
        {
            var q = p.Replace('\\', '/');
            if (q == PNUDriveBuilder.Trigger || q == PNUDriveBuilder.CarTrigger || Array.IndexOf(PNUDriveBuilder.Trigger2, q) >= 0) { EditorApplication.delayCall += PNUDriveBuilder.RunTrigger; return; }
        }
    }
}

[InitializeOnLoad]
public static class PNUDriveBuilder
{
    public const string ROOT = "Assets/PNU_Drive/";
    public const string Trigger = ROOT + "Editor/run_build.txt";
    public const string CarTrigger = ROOT + "Editor/run_car.txt";
    public static readonly string[] Trigger2 = { ROOT + "Editor/run_build_v9.txt", ROOT + "Editor/run_build_v10.txt", ROOT + "Editor/run_build_v11.txt", ROOT + "Editor/run_build_v12.txt", ROOT + "Editor/run_build_v13.txt", ROOT + "Editor/run_build_v14.txt", ROOT + "Editor/run_build_v15.txt", ROOT + "Editor/run_build_v16.txt", ROOT + "Editor/run_build_v17.txt" };   // 새 코드만 아는 빌드 트리거(예전 코드가 먼저 빌드하지 않게)
    const string ScenePath = ROOT + "Scenes/PNU_Drive.unity";
    const string MatDir = ROOT + "Materials/";
    const string CarFbx = "Assets/Car/Models/PNU01.fbx";
    // 자차 테슬라 모델 3(v14 PNU_Map3 자료 그대로 씀: FBX·텍스처·자리 자료). 재질은 여기 Materials/Tesla 에 따로 만듦(v14 씬은 안 건드림)
    const string TeslaFbx = "Assets/PNU_Map3/Vehicles/Tesla_Ego.fbx";
    const string TeslaJson = "Assets/PNU_Map3/Data/tesla_ego.json";
    const string TeslaTex = "Assets/PNU_Map3/Vehicles/Textures/";
    const string TeslaMatDir = ROOT + "Materials/Tesla/";

    static PNUDriveBuilder()
    {
        Application.logMessageReceived -= OnLog; Application.logMessageReceived += OnLog;
        UnityEditor.Compilation.CompilationPipeline.assemblyCompilationFinished -= OnCompiled;
        UnityEditor.Compilation.CompilationPipeline.assemblyCompilationFinished += OnCompiled;
        EditorApplication.playModeStateChanged -= OnPlayMode; EditorApplication.playModeStateChanged += OnPlayMode;
        EditorApplication.delayCall += RunTrigger;
    }

    // v16: 재생 버튼과 빌드 신호가 겹치면 건너뛰던 문제 — 재생이 끝나 편집 상태로 돌아오면 다시 확인
    static void OnPlayMode(PlayModeStateChange s) { if (s == PlayModeStateChange.EnteredEditMode) EditorApplication.delayCall += RunTrigger; }
    static bool AnyTrigger()
    {
        if (File.Exists(Trigger) || File.Exists(CarTrigger)) return true;
        foreach (var t in Trigger2) if (File.Exists(t)) return true;
        return false;
    }

    // ------------------------------------------------------------ 빌드 기록: 프로젝트/Logs/pnu_build.txt ([PNU Drive] 줄 + 오류·예외 + 컴파일 오류)
    const string LogPath = "Logs/pnu_build.txt";
    static void Append(string line)
    {
        try
        {
            Directory.CreateDirectory("Logs");
            if (File.Exists(LogPath) && new FileInfo(LogPath).Length > 4000000) return;
            File.AppendAllText(LogPath, DateTime.Now.ToString("MM-dd HH:mm:ss") + " " + line + "\n");
        }
        catch { }
    }
    static void OnLog(string msg, string stack, LogType type)
    {
        bool ours = msg != null && msg.StartsWith("[PNU Drive]");
        if (!ours && type != LogType.Error && type != LogType.Exception && type != LogType.Assert) return;
        string line = type + " " + msg;
        if (!string.IsNullOrEmpty(stack) && (type == LogType.Exception || !ours)) line += "\n    " + stack.Trim().Replace("\n", "\n    ");
        Append(line);
    }
    static void OnCompiled(string asm, UnityEditor.Compilation.CompilerMessage[] ms)
    {
        if (ms == null) return;
        foreach (var m in ms)
            if (m.type == UnityEditor.Compilation.CompilerMessageType.Error) Append("CompileError " + Path.GetFileName(asm) + " " + m.file + ":" + m.line + " " + m.message);
    }

    public static void RunTrigger()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) return;                       // 재생 중: 끝나면 OnPlayMode 가 다시 부름
        if (EditorApplication.isCompiling || EditorApplication.isUpdating)
        {
            if (AnyTrigger()) EditorApplication.delayCall += RunTrigger;
            return;
        }
        foreach (var t2 in Trigger2) if (File.Exists(t2)) { if (!File.Exists(Trigger)) File.WriteAllText(Trigger, Path.GetFileNameWithoutExtension(t2)); AssetDatabase.DeleteAsset(t2); }
        if (File.Exists(CarTrigger) && !File.Exists(Trigger))
        {
            string k = ""; try { k = File.ReadAllText(CarTrigger).Trim(); } catch { }
            AssetDatabase.DeleteAsset(CarTrigger);
            if (k == "Tesla" || k == "PNU01") CarKind = k;
            Append("---- 자차 트리거 " + CarKind + " (Unity " + Application.unityVersion + ")");
            try { SwapCar(); } catch (Exception e) { Debug.LogError("[PNU Drive] 자차 바꾸기 실패: " + e); }
            return;
        }
        if (!File.Exists(Trigger)) return;
        if (File.Exists(CarTrigger)) { string k = ""; try { k = File.ReadAllText(CarTrigger).Trim(); } catch { } AssetDatabase.DeleteAsset(CarTrigger); if (k == "Tesla" || k == "PNU01") CarKind = k; }
        if (AssetDatabase.LoadAssetAtPath<TextAsset>(ROOT + "Data/roads_00.bytes") == null) { EditorApplication.delayCall += RunTrigger; return; }
        Append("---- 빌드 트리거 (Unity " + Application.unityVersion + ")");
        // 1단계: 후배 신호등·표지판 꾸러미가 아직 없으면 가져오고(컴파일 뒤 다시 실행되게 트리거 유지)
        if (ImportPackagesIfMissing()) return;
        // 2단계: 도로변 건물 에셋(일본 도시)이 없으면 옆 프로젝트에서 복사·가져오기
        try { CopyJapaneseCityIfMissing(); } catch (Exception e) { Debug.LogWarning("[PNU Drive] 일본 도시 에셋 복사 실패: " + e.Message); }
        AssetDatabase.DeleteAsset(Trigger);
        try { BuildScene(); } catch (Exception e) { Debug.LogError("[PNU Drive] 씬 만들기 실패: " + e); }
    }

    // 원본: 같은 UnityProject 폴더의 City 프로젝트. 건물에 쓰는 네 폴더만(.meta 그대로 → GUID·참조 유지). URP_HDRP 꾸러미·데모 씬은 안 가져옴
    static readonly string[] JCSources = { "../City/Assets/JapaneseCity", "D:/UnityProject/City/Assets/JapaneseCity" };
    static readonly string[] JCParts = { "Materials", "Models", "Prefabs", "Textures" };

    static bool CopyJapaneseCityIfMissing()
    {
        if (AssetDatabase.IsValidFolder(JC + "/Prefabs/Buildings")) return false;
        string src = null;
        foreach (var c in JCSources)
        {
            string full; try { full = Path.GetFullPath(c); } catch { continue; }
            if (Directory.Exists(Path.Combine(full, "Prefabs/Buildings"))) { src = full.TrimEnd('/', '\\'); break; }
        }
        if (src == null) { Debug.LogWarning("[PNU Drive] 일본 도시 에셋 원본 없음(" + Path.GetFullPath(JCSources[0]) + ") → 도로변 건물은 상자"); return false; }
        string dst = Path.GetFullPath(JC).TrimEnd('/', '\\');
        var sw = System.Diagnostics.Stopwatch.StartNew(); int n = 0; long bytes = 0;
        Directory.CreateDirectory(dst);
        foreach (var part in JCParts)
        {
            var s = Path.Combine(src, part); if (!Directory.Exists(s)) continue;
            foreach (var f in Directory.GetFiles(s, "*", SearchOption.AllDirectories))
            {
                var d = Path.Combine(dst, f.Substring(src.Length + 1));
                if (File.Exists(d)) continue;
                Directory.CreateDirectory(Path.GetDirectoryName(d));
                File.Copy(f, d); n++; bytes += new FileInfo(f).Length;
            }
            if (File.Exists(s + ".meta") && !File.Exists(Path.Combine(dst, part + ".meta"))) File.Copy(s + ".meta", Path.Combine(dst, part + ".meta"));
        }
        foreach (var x in new[] { "README.txt", "README.txt.meta" })
            if (File.Exists(Path.Combine(src, x)) && !File.Exists(Path.Combine(dst, x))) File.Copy(Path.Combine(src, x), Path.Combine(dst, x));
        if (File.Exists(src + ".meta") && !File.Exists(dst + ".meta")) File.Copy(src + ".meta", dst + ".meta");
        Debug.Log("[PNU Drive] 일본 도시 에셋 복사 " + n + "개 " + (bytes / 1e6).ToString("F0") + "MB (" + sw.Elapsed.TotalSeconds.ToString("F0") + "초) ← " + src);
        AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
        Debug.Log("[PNU Drive] 일본 도시 에셋 가져오기 끝 (" + sw.Elapsed.TotalSeconds.ToString("F0") + "초), 건물 프리팹 폴더 " + (AssetDatabase.IsValidFolder(JC + "/Prefabs/Buildings") ? "있음" : "없음"));
        return true;
    }

    static readonly string[][] Packages = {
        new[] { "Assets/TrafficLightSystem", "Tools/pnu_packages/TrafficLightSystem.unitypackage" },
        new[] { "Assets/OptimizedRoadSigns", "Tools/pnu_packages/OptimizedRoadSigns.unitypackage" } };

    static bool ImportPackagesIfMissing()
    {
        bool any = false;
        foreach (var pk in Packages)
            if (!AssetDatabase.IsValidFolder(pk[0]) && File.Exists(pk[1]))
            {
                Debug.Log("[PNU Drive] 꾸러미 가져오기: " + pk[1]);
                AssetDatabase.ImportPackage(pk[1], false); any = true;
            }
        return any;   // 가져왔으면 스크립트 컴파일 뒤(도메인 다시 읽기) 트리거가 남아 있어 다시 실행됨
    }

    // 재질 번호(파이썬 MAT 과 같게)
    static readonly string[] MatNames = { "asphalt", "white", "yellow", "curb", "sidewalk", "concrete", "grass", "tactile", "terrain", "blue", "tunnel", "lamp", "median", "water",
        "bld_apt", "bld_office", "bld_comm", "bld_house", "bld_apt2", "bld_comm2", "roof", "urban", "trunk", "leaves", "pole", "bridge_white", "cable", "hedge", "forest", "gw_fence" };

    static Material MakeMat(string name, string tex, Color c, float smooth, float metal = 0f, float tile = 1f, bool both = false, Color? emit = null)
    {
        EnsureFolder(MatDir);
        string path = MatDir + "PD_" + name + ".mat";
        var m = AssetDatabase.LoadAssetAtPath<Material>(path);
        var sh = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
        if (m == null) { m = new Material(sh); AssetDatabase.CreateAsset(m, path); }
        m.shader = sh;
        if (tex != null)
        {
            var t = AssetDatabase.LoadAssetAtPath<Texture2D>(ROOT + "Textures/" + tex + ".png");
            if (t != null) { m.SetTexture("_BaseMap", t); m.SetTexture("_MainTex", t); m.SetTextureScale("_BaseMap", new Vector2(tile, tile)); }
        }
        m.SetColor("_BaseColor", c); m.SetColor("_Color", c);
        m.SetFloat("_Smoothness", smooth); m.SetFloat("_Glossiness", smooth); m.SetFloat("_Metallic", metal);
        if (both) m.SetFloat("_Cull", 0f);
        if (emit.HasValue) { m.EnableKeyword("_EMISSION"); m.SetColor("_EmissionColor", emit.Value); m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive; }
        EditorUtility.SetDirty(m);
        return m;
    }

    // 물: PNU/Water 셰이더(물결·하늘 반사·해 반짝임·물가 거품, v11). 셰이더가 없으면 예전 Lit
    static Material MakeWater()
    {
        var sh = Shader.Find("PNU/Water");
        if (sh == null) { Debug.LogWarning("[PNU Drive] 물 셰이더(PNU/Water)를 못 찾아 예전 재질로"); return MakeMat("water", null, new Color(0.13f, 0.27f, 0.36f), 0.88f, 0.05f); }
        EnsureFolder(MatDir);
        string path = MatDir + "PD_water.mat";
        var m = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (m == null) { m = new Material(sh); AssetDatabase.CreateAsset(m, path); }
        if (m.shader != sh) m.shader = sh;
        EditorUtility.SetDirty(m);
        return m;
    }

    static Material[] Materials()
    {
        var M = new Material[MatNames.Length];
        M[0] = MakeMat("asphalt", "asphalt", Color.white, 0.18f, 0f, 2f);
        M[1] = MakeMat("white", null, new Color(0.93f, 0.93f, 0.91f), 0.35f);
        M[2] = MakeMat("yellow", null, new Color(0.96f, 0.68f, 0.08f), 0.35f);
        M[3] = MakeMat("curb", "concrete", new Color(0.86f, 0.86f, 0.84f), 0.15f, 0f, 0.25f);
        M[4] = MakeMat("sidewalk", "pavers", Color.white, 0.12f);
        M[5] = MakeMat("concrete", "concrete", new Color(0.92f, 0.91f, 0.88f), 0.12f, 0f, 0.25f);
        M[6] = MakeMat("grass", "terrain", new Color(0.85f, 0.95f, 0.8f), 0.05f);
        M[7] = MakeMat("tactile", null, new Color(0.95f, 0.78f, 0.1f), 0.2f);
        M[8] = MakeMat("terrain", "terrain", Color.white, 0.05f);
        M[9] = MakeMat("blue", null, new Color(0.15f, 0.35f, 0.75f), 0.3f);
        M[10] = MakeMat("tunnel", "tunnel", Color.white, 0.4f, 0f, 1f, true);
        M[11] = MakeMat("lamp", null, new Color(1f, 0.85f, 0.55f), 0.5f, 0f, 1f, false, new Color(2.2f, 1.6f, 0.8f));
        M[12] = MakeMat("median", "concrete", new Color(0.7f, 0.7f, 0.68f), 0.1f, 0f, 0.25f);
        M[13] = MakeWater();
        M[14] = MakeMat("bld_apt", "bld_apt", Color.white, 0.25f);
        M[15] = MakeMat("bld_office", "bld_office", Color.white, 0.6f, 0.2f);
        M[16] = MakeMat("bld_comm", "bld_comm", Color.white, 0.2f);
        M[17] = MakeMat("bld_house", "bld_house", Color.white, 0.15f);
        M[18] = MakeMat("bld_apt2", "bld_apt2", Color.white, 0.25f);
        M[19] = MakeMat("bld_comm2", "bld_comm2", Color.white, 0.2f);
        M[20] = MakeMat("roof", "concrete", new Color(0.62f, 0.62f, 0.6f), 0.1f, 0f, 0.25f);
        M[21] = MakeMat("urban", "concrete", new Color(0.72f, 0.7f, 0.66f), 0.08f, 0f, 2.5f);
        M[22] = MakeMat("trunk", null, new Color(0.33f, 0.25f, 0.19f), 0.1f);
        M[23] = MakeMat("leaves", null, new Color(0.31f, 0.47f, 0.21f), 0.15f);
        M[24] = MakeMat("pole", null, new Color(0.56f, 0.58f, 0.6f), 0.55f, 0.6f);
        M[25] = MakeMat("bridge_white", null, new Color(0.9f, 0.91f, 0.92f), 0.45f);
        M[26] = MakeMat("cable", null, new Color(0.85f, 0.86f, 0.87f), 0.5f, 0.3f);
        M[27] = MakeMat("hedge", "hedge", Color.white, 0.06f);
        M[28] = MakeMat("forest", "forest", Color.white, 0.04f, 0f, 0.5f);
        M[29] = MakeMat("gw_fence", null, new Color(0.62f, 0.68f, 0.75f), 0.55f, 0.45f, 1f, true);   // 광안대교 난간 은청색 물결(후배 GW_Fence_Silver_Blue_Wave)
        AssetDatabase.SaveAssets();
        return M;
    }

    [MenuItem("PNU Drive/Build Scene")]
    public static void BuildScene()
    {
        // 지금 열린 씬이 이 주행 씬이면 묻지 않음(어차피 새로 만듦). 다른 씬이 고쳐져 있으면 저장할지 물음
        if (EditorSceneManager.GetActiveScene().path != ScenePath && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
        var mats = Materials();
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        // 해·하늘·안개
        var sun = new GameObject("Sun"); var lt = sun.AddComponent<Light>(); lt.type = LightType.Directional; lt.intensity = 1.25f; lt.shadows = LightShadows.Soft;
        lt.color = new Color(1f, 0.96f, 0.9f); sun.transform.rotation = Quaternion.Euler(48f, -32f, 0f);
        var sky = AssetDatabase.LoadAssetAtPath<Material>(MatDir + "PD_sky.mat");
        if (sky == null) { var ss = Shader.Find("Skybox/Procedural"); if (ss != null) { sky = new Material(ss); AssetDatabase.CreateAsset(sky, MatDir + "PD_sky.mat"); } }
        if (sky != null) { sky.SetFloat("_AtmosphereThickness", 0.85f); sky.SetFloat("_Exposure", 1.15f); sky.SetFloat("_SunSize", 0.04f); sky.SetColor("_SkyTint", new Color(0.5f, 0.5f, 0.5f, 1f)); sky.SetColor("_GroundColor", new Color(0.369f, 0.349f, 0.341f, 1f)); RenderSettings.skybox = sky; }   // v17: 비 하늘 바꾼 값이 남아 있어도 빌드 때 기본값으로
        RenderSettings.sun = lt; RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Skybox; RenderSettings.ambientIntensity = 1.0f;
        RenderSettings.fog = true; RenderSettings.fogMode = FogMode.Linear; RenderSettings.fogStartDistance = 400f; RenderSettings.fogEndDistance = 4500f; RenderSettings.fogColor = new Color(0.74f, 0.8f, 0.86f);
        // 맵
        var wgo = new GameObject("PNU Drive World"); var world = wgo.AddComponent<PNUDriveWorld>();
        world.materials = mats;
        var files = Directory.GetFiles(ROOT + "Data", "*.bytes").Select(p => p.Replace('\\', '/')).OrderBy(p => p).ToArray();
        world.packs = files.Select(p => AssetDatabase.LoadAssetAtPath<TextAsset>(p)).Where(a => a != null && !a.name.StartsWith("trees")).ToArray();
        world.packColliders = world.packs.Select(a => a.name.StartsWith("roads")).ToArray();
        world.Build();
        // 나무(GPU 인스턴싱)
        try { BuildTrees(); } catch (Exception e) { Debug.LogWarning("[PNU Drive] 나무 건너뜀: " + e.Message); }
        // 도로변 건물(일본 도시 에셋)
        try { BuildCity(mats); } catch (Exception e) { Debug.LogWarning("[PNU Drive] 도로변 건물 건너뜀: " + e); }
        // 부산대·부경대 정문 자리(후배 모델 넣는 곳, v11)
        try { BuildLandmarks(); } catch (Exception e) { Debug.LogWarning("[PNU Drive] 정문 자리 건너뜀: " + e.Message); }
        // 자차(기본 테슬라 모델 3 — PNU01 은 만드는 중이라 메뉴 "PNU Drive/자차: PNU01" 로 고를 때만)
        var rig = MakeCar(CarKind); var car = rig.go;
        var camGo = new GameObject("Main Camera"); camGo.tag = "MainCamera"; var cam = camGo.AddComponent<Camera>(); cam.nearClipPlane = 0.12f; cam.farClipPlane = 6000f; cam.fieldOfView = 62f;
        camGo.AddComponent<AudioListener>();
        var dgo = new GameObject("PNU Drive (주행)"); var ego = dgo.AddComponent<PNUDriveEgo>();
        ego.pathJson = AssetDatabase.LoadAssetAtPath<TextAsset>(ROOT + "Data/path.json"); ego.cam = cam;
        // 소리·길안내
        var snd = dgo.AddComponent<PNUDriveSound>(); snd.ego = ego;
        var nav = dgo.AddComponent<PNUDriveNav>(); nav.ego = ego; nav.sound = snd;
        nav.driveJson = AssetDatabase.LoadAssetAtPath<TextAsset>(ROOT + "Data/tesla_drive.json"); nav.navTexture = NavTexture();
        try { MakeVoices(ego.pathJson, snd, false); } catch (Exception e) { Debug.LogWarning("[PNU Drive] 음성 안내 건너뜀: " + e.Message); }
        Wire(ego, rig);
        // 시작 자리에 차·카메라 놓기(편집 화면에서도 보이게)
        ego.Load(); PlaceAtStart(ego);
        cam.transform.position = car.transform.position - car.transform.forward * 9f + Vector3.up * 3.5f; cam.transform.LookAt(car.transform.position + Vector3.up);
        try { PlaceSignals(); } catch (Exception e) { Debug.LogWarning("[PNU Drive] 신호등 놓기 건너뜀: " + e.Message); }
        try { PlaceSigns(); } catch (Exception e) { Debug.LogWarning("[PNU Drive] 표지판 놓기 건너뜀: " + e.Message); }
        try { BuildTraffic(ego); } catch (Exception e) { Debug.LogWarning("[PNU Drive] 차·사람 건너뜀: " + e); }
        try { BuildScenario(ego, cam, mats); } catch (Exception e) { Debug.LogWarning("[PNU Drive] 스토리보드(5/6) 건너뜀: " + e); }
        EnsureFolder(ROOT + "Scenes");
        EditorSceneManager.SaveScene(scene, ScenePath);
        Debug.Log("[PNU Drive] 씬 완료: " + ScenePath + " (타일 " + world.tileCount + ", 꼭짓점 " + world.vertexCount + ")");
        if (SceneView.lastActiveSceneView != null) { SceneView.lastActiveSceneView.LookAt(car.transform.position, Quaternion.Euler(35f, car.transform.eulerAngles.y, 0f), 60f); }
    }

    // ------------------------------------------------------------ 자차 고르기·만들기·바꾸기
    const string CarPref = "PNUDrive.Car";
    const string MenuTesla = "PNU Drive/자차: 테슬라 모델 3";
    const string MenuPNU01 = "PNU Drive/자차: PNU01 (만드는 중)";
    static string CarKind { get { return EditorPrefs.GetString(CarPref, "Tesla"); } set { EditorPrefs.SetString(CarPref, value); } }

    class CarRig { public GameObject go; public Transform eye; public float wheelBase = 3.0f, wheelTrack = 1.62f, eyePitch = 3f, near = 0.05f; public string name; }

    [MenuItem(MenuTesla, false, 20)] static void UseTesla() { CarKind = "Tesla"; SwapCar(); }
    [MenuItem(MenuTesla, true)] static bool UseTeslaCheck() { Menu.SetChecked(MenuTesla, CarKind != "PNU01"); return true; }
    [MenuItem(MenuPNU01, false, 21)] static void UsePNU01() { CarKind = "PNU01"; SwapCar(); }
    [MenuItem(MenuPNU01, true)] static bool UsePNU01Check() { Menu.SetChecked(MenuPNU01, CarKind == "PNU01"); return true; }

    /// <summary>고른 차(없으면 다른 차, 그것도 없으면 상자)</summary>
    static CarRig MakeCar(string kind)
    {
        Func<CarRig>[] order = kind == "PNU01" ? new Func<CarRig>[] { MakePNU01, MakeTesla } : new Func<CarRig>[] { MakeTesla, MakePNU01 };
        foreach (var f in order)
        {
            CarRig r = null;
            try { r = f(); } catch (Exception e) { Debug.LogWarning("[PNU Drive] 자차 만들기 실패: " + e); }
            if (r != null) return r;
        }
        var box = GameObject.CreatePrimitive(PrimitiveType.Cube); box.name = "자차(상자)"; box.transform.localScale = new Vector3(1.9f, 1.5f, 4.6f);
        UnityEngine.Object.DestroyImmediate(box.GetComponent<Collider>());
        return new CarRig { go = box, name = "상자" };
    }

    static void Wire(PNUDriveEgo ego, CarRig rig)
    {
        ego.car = rig.go.transform; ego.driverEye = rig.eye;
        ego.wheelBase = rig.wheelBase; ego.wheelTrack = rig.wheelTrack; ego.eyePitch = rig.eyePitch; ego.driverNear = rig.near;
        var ck = rig.go.GetComponent<PNUDriveCockpit>(); if (ck != null) { ck.ego = ego; ck.wheelBase = rig.wheelBase; ck.nav = ego.GetComponent<PNUDriveNav>(); }
        EditorUtility.SetDirty(ego);
    }

    static void PlaceAtStart(PNUDriveEgo ego)
    {
        if (ego.car == null || !ego.Ready) return;
        var p0 = ego.PosAt(0f); var p1 = ego.PosAt(8f);
        ego.car.position = p0; var f = p1 - p0; f.y = 0; if (f.sqrMagnitude > 1e-4f) ego.car.rotation = Quaternion.LookRotation(f.normalized, Vector3.up);
    }

    /// <summary>씬은 그대로 두고 자차만 바꿈: PNU_Drive 씬을 열어 기존 차를 지우고 새로 만들어 주행에 연결 → 저장</summary>
    public static void SwapCar()
    {
        var sc = EditorSceneManager.GetActiveScene();
        if (sc.path != ScenePath)
        {
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath) == null) { BuildScene(); return; }
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            sc = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        }
        var ego = UnityEngine.Object.FindObjectOfType<PNUDriveEgo>();
        if (ego == null) { BuildScene(); return; }
        bool had = ego.car != null; Vector3 pos = had ? ego.car.position : Vector3.zero; Quaternion rot = had ? ego.car.rotation : Quaternion.identity;
        if (had && Selection.activeTransform != null && Selection.activeTransform.IsChildOf(ego.car)) Selection.activeObject = null;   // 인스펙터가 지운 차를 보고 있지 않게
        if (had) UnityEngine.Object.DestroyImmediate(ego.car.gameObject);
        var rig = MakeCar(CarKind);
        Wire(ego, rig);
        if (had) { rig.go.transform.position = pos; rig.go.transform.rotation = rot; } else { ego.Load(); PlaceAtStart(ego); }
        EditorSceneManager.MarkSceneDirty(sc); EditorSceneManager.SaveScene(sc);
        Debug.Log("[PNU Drive] 자차 바꿈 → " + rig.name + " (씬 저장: " + sc.path + ")");
        if (SceneView.lastActiveSceneView != null) SceneView.lastActiveSceneView.LookAt(rig.go.transform.position, Quaternion.Euler(25f, rig.go.transform.eulerAngles.y + 150f, 0f), 9f);
    }

    static CarRig MakePNU01()
    {
        var carModel = AssetDatabase.LoadAssetAtPath<GameObject>(CarFbx);
        if (carModel == null) return null;
        try { SetupCarMaterials(); } catch (Exception e) { Debug.LogWarning("[PNU Drive] 자차 재질 건너뜀: " + e.Message); }
        // 빈 부모(자리·방향은 주행이 움직임) 아래에 FBX 를 그대로: FBX 뿌리의 축 회전(Z 위 → Y 위)을 덮어쓰지 않게
        var car = new GameObject("PNU01 (자차)");
        var model = (GameObject)PrefabUtility.InstantiatePrefab(carModel); model.name = "PNU01";
        model.transform.SetParent(car.transform, false);
        model.transform.localPosition = carModel.transform.localPosition; model.transform.localRotation = carModel.transform.localRotation;
        var t = Type.GetType("PNUAVCar, Assembly-CSharp"); if (t != null && model.GetComponent(t) == null) model.AddComponent(t);
        foreach (var c in car.GetComponentsInChildren<Collider>(true)) UnityEngine.Object.DestroyImmediate(c);
        Debug.Log("[PNU Drive] 자차 PNU01: FBX 뿌리 회전 " + carModel.transform.localRotation.eulerAngles + " 유지");
        return new CarRig { go = car, eye = car.GetComponentsInChildren<Transform>(true).FirstOrDefault(x => x.name == "DriverEye"), name = "PNU01" };
    }

    /// <summary>v14 테슬라(PNUSceneBuilder.BuildTesla 와 같게): FBX 를 빈 부모 아래 그대로, 재질은 tesla_ego.json 계획대로, 운전석 눈·운전대·바퀴·화면·유리 연결</summary>
    static CarRig MakeTesla()
    {
        var fbx = AssetDatabase.LoadAssetAtPath<GameObject>(TeslaFbx); var ta = AssetDatabase.LoadAssetAtPath<TextAsset>(TeslaJson);
        if (fbx == null || ta == null) { Debug.LogWarning("[PNU Drive] 테슬라 자료 없음: " + TeslaFbx + (fbx == null ? " ✗" : " ✓") + ", " + TeslaJson + (ta == null ? " ✗" : " ✓")); return null; }
        var T = JsonUtility.FromJson<TeslaSpec>(ta.text);
        var root = new GameObject("Tesla_Model3 (자차)");
        var car = (GameObject)PrefabUtility.InstantiatePrefab(fbx); car.name = "Tesla_Model3";
        car.transform.SetParent(root.transform, false); car.transform.localPosition = Vector3.zero; car.transform.localRotation = Quaternion.identity;
        var plan = new Dictionary<string, TeslaSpecMat>(); if (T.materials != null) foreach (var m in T.materials) plan[m.name] = m;
        var made = new Dictionary<string, Material>(); var missing = new HashSet<string>();
        Transform wheel = null, wf = null, wr = null; Renderer glass = null;
        foreach (var r in car.GetComponentsInChildren<MeshRenderer>(true))
        {
            var mats = r.sharedMaterials;
            for (int i = 0; i < mats.Length; i++)
            {
                if (mats[i] == null) continue;
                string n = mats[i].name.Replace(" (Instance)", ""); TeslaSpecMat tm;
                if (!plan.TryGetValue(n, out tm)) { missing.Add(n); continue; }
                Material mm; if (!made.TryGetValue(n, out mm)) { mm = TeslaMaterial(tm); made[n] = mm; }
                mats[i] = mm;
            }
            r.sharedMaterials = mats;
            bool isGlass = r.name.StartsWith("Tesla_Glass");
            r.shadowCastingMode = isGlass ? UnityEngine.Rendering.ShadowCastingMode.Off : UnityEngine.Rendering.ShadowCastingMode.On;   // 유리 지붕으로 햇빛이 들어오게
            if (isGlass) glass = r;
            if (r.name.StartsWith("Tesla_SteeringWheel")) wheel = r.transform;
            if (r.name.StartsWith("Tesla_WheelsFront")) wf = r.transform;
            if (r.name.StartsWith("Tesla_WheelsRear")) wr = r.transform;
        }
        foreach (var c in root.GetComponentsInChildren<Collider>(true)) UnityEngine.Object.DestroyImmediate(c);
        AssetDatabase.SaveAssets();
        var eye = new GameObject("DriverEye").transform; eye.SetParent(root.transform, false); eye.localPosition = TeslaSpec.V(T.eye);
        var ck = root.AddComponent<PNUDriveCockpit>();
        ck.teslaJson = ta; ck.driveJson = AssetDatabase.LoadAssetAtPath<TextAsset>(ROOT + "Data/tesla_drive.json");
        ck.steeringWheel = wheel; ck.wheelsFront = wf; ck.wheelsRear = wr; ck.glassRenderer = glass; ck.digitMaterial = TeslaDigitsMaterial();
        try { ck.navMaterial = TeslaNavMaterial(); } catch (Exception e) { Debug.LogWarning("[PNU Drive] 길안내 화면 재질 건너뜀: " + e.Message); }
        float wb = 2.86f;
        if (T.wheels != null && T.wheels.Tesla_WheelsFront != null && T.wheels.Tesla_WheelsRear != null)
            wb = Mathf.Abs(TeslaSpec.V(T.wheels.Tesla_WheelsFront.center).z - TeslaSpec.V(T.wheels.Tesla_WheelsRear.center).z);
        if (wb < 2f || wb > 4f) wb = 2.86f;
        Debug.Log("[PNU Drive] 자차 테슬라 모델 3: 재질 " + made.Count + (missing.Count > 0 ? " (계획에 없는 재질: " + string.Join(", ", missing) + ")" : "") +
                  ", 운전대 " + (wheel != null) + ", 바퀴 " + (wf != null) + "/" + (wr != null) + ", 유리 " + (glass != null) +
                  ", 화면 지도 " + (ck.driveJson != null) + ", 축거 " + wb.ToString("F2") + "m, 눈 " + eye.localPosition);
        return new CarRig { go = root, eye = eye, wheelBase = wb, wheelTrack = 1.58f, eyePitch = 4f, near = 0.03f, name = "테슬라 모델 3" };
    }

    static Material TeslaMaterial(TeslaSpecMat t)
    {
        EnsureFolder(TeslaMatDir);
        string path = TeslaMatDir + t.name + ".mat";
        bool unlit = t.name == "TM_Screen";
        var sh = unlit ? (Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Unlit/Texture")) : (Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard"));
        var m = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (m == null) { m = new Material(sh); AssetDatabase.CreateAsset(m, path); } else m.shader = sh;
        Color c = t.color != null && t.color.Length >= 3 ? new Color(t.color[0], t.color[1], t.color[2]) : Color.white;
        if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c);
        if (m.HasProperty("_Color")) m.SetColor("_Color", c);
        Texture2D tx = unlit ? AssetDatabase.LoadAssetAtPath<Texture2D>(ROOT + "Textures/Tesla_Screen_Drive.png") : null;   // 가운데 화면: 이 경로 지도 그림
        if (tx == null)
        {
            string texName = unlit ? "Tesla_Screen.png" : t.tex;
            if (!string.IsNullOrEmpty(texName)) tx = AssetDatabase.LoadAssetAtPath<Texture2D>(TeslaTex + texName);
        }
        if (tx != null) { if (m.HasProperty("_BaseMap")) m.SetTexture("_BaseMap", tx); if (m.HasProperty("_MainTex")) m.SetTexture("_MainTex", tx); }
        if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", t.smooth);
        if (m.HasProperty("_Metallic")) m.SetFloat("_Metallic", t.metal);
        if (t.kind == "glass" || t.kind == "blend") TeslaTransparent(m, t.alpha);
        else if (t.kind == "light") TeslaTransparent(m, 1f);                  // 등 그림의 알파로 비침
        else TeslaDoubleSided(m);                                               // 실내 얇은 판(한 면짜리)도 보이게
        EditorUtility.SetDirty(m);
        return m;
    }

    static void TeslaTransparent(Material m, float alpha)
    {
        Color c = m.HasProperty("_BaseColor") ? m.GetColor("_BaseColor") : Color.white; c.a = alpha;
        if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c);
        if (m.HasProperty("_Color")) m.SetColor("_Color", c);
        if (m.HasProperty("_Surface")) m.SetFloat("_Surface", 1f);
        if (m.HasProperty("_Blend")) m.SetFloat("_Blend", 0f);
        if (m.HasProperty("_BlendModePreserveSpecular")) m.SetFloat("_BlendModePreserveSpecular", 0f);   // 반사도 알파만큼만(투명 유리가 뿌옇지 않게)
        m.SetOverrideTag("RenderType", "Transparent");
        m.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha); m.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha); m.SetFloat("_ZWrite", 0f);
        m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT"); m.DisableKeyword("_ALPHAPREMULTIPLY_ON");
        m.renderQueue = 3000;
        TeslaDoubleSided(m);
    }

    static void TeslaDoubleSided(Material m) { if (m.HasProperty("_Cull")) m.SetFloat("_Cull", 0f); m.doubleSidedGI = true; }

    static Material TeslaDigitsMaterial()
    {
        EnsureFolder(TeslaMatDir);
        string path = TeslaMatDir + "Tesla_Digits.mat";
        var sh = Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Unlit/Transparent Cutout");
        var dm = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (dm == null) { dm = new Material(sh); AssetDatabase.CreateAsset(dm, path); }
        var dt = AssetDatabase.LoadAssetAtPath<Texture2D>(TeslaTex + "Tesla_Digits.png");
        if (dt != null) { if (dm.HasProperty("_BaseMap")) dm.SetTexture("_BaseMap", dt); if (dm.HasProperty("_MainTex")) dm.SetTexture("_MainTex", dt); }
        if (dm.HasProperty("_AlphaClip")) { dm.SetFloat("_AlphaClip", 1f); dm.SetFloat("_Cutoff", 0.5f); dm.EnableKeyword("_ALPHATEST_ON"); }
        dm.renderQueue = 2450; TeslaDoubleSided(dm); EditorUtility.SetDirty(dm);
        AssetDatabase.SaveAssets();
        return dm;
    }

    /// <summary>길안내 글자판(Tesla_Nav.png): 늘이지 않게(Clamp), 알파 = 투명</summary>
    static Texture2D NavTexture()
    {
        string path = ROOT + "Textures/Tesla_Nav.png";
        var imp = AssetImporter.GetAtPath(path) as TextureImporter;
        if (imp != null && (imp.wrapMode != TextureWrapMode.Clamp || !imp.alphaIsTransparency || imp.textureCompression != TextureImporterCompression.Uncompressed))
        {
            imp.wrapMode = TextureWrapMode.Clamp; imp.alphaIsTransparency = true; imp.mipmapEnabled = true; imp.textureCompression = TextureImporterCompression.Uncompressed;
            imp.SaveAndReimport();
        }
        return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
    }

    /// <summary>테슬라 화면 위 길안내 카드·글자·깜빡이 화살표: URP Unlit 투명(알파 섞기)</summary>
    static Material TeslaNavMaterial()
    {
        EnsureFolder(TeslaMatDir);
        string path = TeslaMatDir + "Tesla_Nav.mat";
        var sh = Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Unlit/Transparent");
        var m = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (m == null) { m = new Material(sh); AssetDatabase.CreateAsset(m, path); } else m.shader = sh;
        var tx = NavTexture();
        if (tx != null) { if (m.HasProperty("_BaseMap")) m.SetTexture("_BaseMap", tx); if (m.HasProperty("_MainTex")) m.SetTexture("_MainTex", tx); }
        if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", Color.white);
        if (m.HasProperty("_Surface")) m.SetFloat("_Surface", 1f);
        if (m.HasProperty("_Blend")) m.SetFloat("_Blend", 0f);
        m.SetOverrideTag("RenderType", "Transparent");
        m.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha); m.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha); m.SetFloat("_ZWrite", 0f);
        m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        m.renderQueue = 3050; TeslaDoubleSided(m); EditorUtility.SetDirty(m);
        AssetDatabase.SaveAssets();
        return m;
    }

    // ------------------------------------------------------------ 음성 안내 wav: 윈도 기본 음성(System.Speech) 중 한국어 목소리로
    [Serializable] class VoiceLine { public float s; public string key, text, kind; }
    [Serializable] class VoiceList { public VoiceLine[] voice; }
    const string VoiceDir = ROOT + "Audio/Voice/";

    [MenuItem("PNU Drive/음성 안내 다시 만들기")]
    static void RemakeVoices()
    {
        var ego = UnityEngine.Object.FindObjectOfType<PNUDriveEgo>(); var snd = ego != null ? ego.GetComponent<PNUDriveSound>() : null;
        if (ego == null || snd == null) { Debug.LogWarning("[PNU Drive] 주행 씬(PNU Drive (주행))을 열고 다시 해 주세요"); return; }
        MakeVoices(ego.pathJson, snd, true);
        EditorUtility.SetDirty(snd); EditorSceneManager.MarkSceneDirty(snd.gameObject.scene); EditorSceneManager.SaveScene(snd.gameObject.scene);
    }

    static string Hash8(string s)
    {
        using (var md5 = System.Security.Cryptography.MD5.Create())
        {
            var b = md5.ComputeHash(System.Text.Encoding.UTF8.GetBytes(s)); return BitConverter.ToString(b, 0, 4).Replace("-", "").ToLowerInvariant();
        }
    }

    static void MakeVoices(TextAsset pathTa, PNUDriveSound snd, bool force)
    {
        if (pathTa == null || snd == null) return;
        var vl = JsonUtility.FromJson<VoiceList>(pathTa.text);
        if (vl == null || vl.voice == null || vl.voice.Length == 0) { Debug.Log("[PNU Drive] 음성 안내 없음(path.json voice)"); return; }
        EnsureFolder(VoiceDir.TrimEnd('/'));
        var texts = new Dictionary<string, string>();
        foreach (var v in vl.voice) if (!string.IsNullOrEmpty(v.key) && !texts.ContainsKey(v.key)) texts[v.key] = v.text;
        var files = new Dictionary<string, string>(); var todo = new List<KeyValuePair<string, string>>();
        foreach (var kv in texts)
        {
            string f = VoiceDir + kv.Key + "_" + Hash8(kv.Value) + ".wav"; files[kv.Key] = f;
            if (force || !File.Exists(f)) todo.Add(new KeyValuePair<string, string>(Path.GetFullPath(f), kv.Value));
        }
        if (todo.Count > 0)
        {
            Speak(todo, "음성 안내");
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
        }
        var keys = new List<string>(); var clips = new List<AudioClip>();
        foreach (var kv in files)
        {
            var c = File.Exists(kv.Value) && new FileInfo(kv.Value).Length > 1000 ? AssetDatabase.LoadAssetAtPath<AudioClip>(kv.Value) : null;
            if (c == null) continue;
            keys.Add(kv.Key); clips.Add(c);
        }
        snd.voiceKeys = keys.ToArray(); snd.voiceClips = clips.ToArray(); EditorUtility.SetDirty(snd);
        Debug.Log("[PNU Drive] 음성 안내 wav " + clips.Count + "/" + texts.Count + "개 연결");
    }


    /// <summary>윈도 기본 음성(한국어)으로 (파일, 문장) 목록을 wav 로</summary>
    static void Speak(List<KeyValuePair<string, string>> todo, string what)
    {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            string list = Path.GetFullPath("Temp/pnu_voice_list.txt");
            Directory.CreateDirectory(Path.GetDirectoryName(list));
            File.WriteAllLines(list, todo.Select(t => t.Key + "\t" + t.Value.Replace("\t", " ")).ToArray(), new System.Text.UTF8Encoding(false));
            string ps =
                "$ErrorActionPreference='Stop'; $ProgressPreference='SilentlyContinue'\n" +
                "Add-Type -AssemblyName System.Speech\n" +
                "$s = New-Object System.Speech.Synthesis.SpeechSynthesizer\n" +
                "$vs = @($s.GetInstalledVoices() | Where-Object { $_.Enabled })\n" +
                "foreach ($x in $vs) { Write-Output ('VOICE ' + $x.VoiceInfo.Name + ' | ' + $x.VoiceInfo.Culture.Name) }\n" +
                "$ko = $vs | Where-Object { $_.VoiceInfo.Culture.Name -like 'ko*' } | Select-Object -First 1\n" +
                "if ($ko -eq $null) { Write-Output 'NOKOREAN'; exit 3 }\n" +
                "$s.SelectVoice($ko.VoiceInfo.Name); $s.Rate = 1; $s.Volume = 100\n" +
                "$fmt = New-Object System.Speech.AudioFormat.SpeechAudioFormatInfo(22050, [System.Speech.AudioFormat.AudioBitsPerSample]::Sixteen, [System.Speech.AudioFormat.AudioChannel]::Mono)\n" +
                "foreach ($ln in [System.IO.File]::ReadAllLines('" + list.Replace("'", "''") + "', [System.Text.Encoding]::UTF8)) {\n" +
                "  if ($ln.Trim().Length -eq 0) { continue }\n" +
                "  $p = $ln.Split([char]9)\n" +
                "  $s.SetOutputToWaveFile($p[0], $fmt); $s.Speak($p[1]); $s.SetOutputToNull()\n" +
                "  Write-Output ('WROTE ' + $p[0])\n" +
                "}\n" +
                "$s.Dispose(); Write-Output ('OK ' + $ko.VoiceInfo.Name)\n";
            string enc = Convert.ToBase64String(System.Text.Encoding.Unicode.GetBytes(ps));
            string outp = "", err = ""; int code = -1;
            try
            {
                var psi = new System.Diagnostics.ProcessStartInfo("powershell.exe", "-NoProfile -NonInteractive -EncodedCommand " + enc)
                { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true, StandardOutputEncoding = System.Text.Encoding.UTF8 };
                using (var pr = System.Diagnostics.Process.Start(psi))
                {
                    var to = pr.StandardOutput.ReadToEndAsync(); var te = pr.StandardError.ReadToEndAsync();
                    if (!pr.WaitForExit(120000)) { try { pr.Kill(); } catch { } }
                    try { outp = to.Result; err = te.Result; } catch { }
                    code = pr.HasExited ? pr.ExitCode : -2;
                }
            }
            catch (Exception e) { err = e.Message; }
            var voices = string.Join(", ", outp.Split('\n').Where(l => l.StartsWith("VOICE ")).Select(l => l.Substring(6).Trim()).ToArray());
            int wrote = outp.Split('\n').Count(l => l.StartsWith("WROTE "));
            Debug.Log("[PNU Drive] " + what + " 만들기: " + wrote + "/" + todo.Count + "개, 종료 " + code + " (" + sw.Elapsed.TotalSeconds.ToString("F1") + "초), 설치된 목소리: " + (voices.Length > 0 ? voices : "없음") +
                      (outp.Contains("NOKOREAN") ? " → 한국어 목소리가 없어 음성 없이 띵동·자막만" : "") + (err.Trim().Length > 0 ? " / 오류: " + err.Trim().Split('\n')[0] : ""));
    }

    // ------------------------------------------------------------ 자차(PNU01) 재질: Car/Data/pnu01_materials.json → URP Lit 52개, FBX 재질 이름에 연결(유리 = 반투명)
    // (Car/Editor/PNUAVSetup 메뉴는 Assets/PNU_AV01 기준이라 여기 폴더(Assets/Car)에 맞춰 같은 일을 함. 축 변환 설정은 건드리지 않음)
    [Serializable] class CarMatDef { public string name; public float[] color; public float metallic, smoothness, alpha = 1f; public float[] emission; public float emissionIntensity; public string texture; public bool cutout, doubleSided; }
    [Serializable] class CarMatList { public CarMatDef[] materials; }

    static void SetupCarMaterials()
    {
        string root = Path.GetDirectoryName(Path.GetDirectoryName(CarFbx)).Replace('\\', '/');   // Assets/Car
        string json = root + "/Data/pnu01_materials.json";
        var imp = AssetImporter.GetAtPath(CarFbx) as ModelImporter;
        if (imp == null || !File.Exists(json)) return;
        var list = JsonUtility.FromJson<CarMatList>(File.ReadAllText(json));
        if (list == null || list.materials == null || list.materials.Length == 0) return;
        var lit = Shader.Find("Universal Render Pipeline/Lit"); if (lit == null) return;
        EnsureFolder(root + "/Materials");
        var have = imp.GetExternalObjectMap();
        bool changed = false; int n = 0;
        // 축 변환은 FBX 뿌리 회전으로 두기(굽기 끔): 이 FBX 는 굽기를 켜면 빈 노드(DriverEye)·메쉬 축이 어긋남. 대신 차를 빈 부모 아래에 둠
        if (imp.bakeAxisConversion) { imp.bakeAxisConversion = false; changed = true; }
        foreach (var d in list.materials)
        {
            if (string.IsNullOrEmpty(d.name)) continue;
            string path = root + "/Materials/" + d.name + ".mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null) { m = new Material(lit); AssetDatabase.CreateAsset(m, path); }
            m.shader = lit;
            var tex = string.IsNullOrEmpty(d.texture) ? null : AssetDatabase.LoadAssetAtPath<Texture2D>(root + "/Textures/" + d.texture);
            Color baseC = d.color != null && d.color.Length >= 3 ? new Color(d.color[0], d.color[1], d.color[2], d.alpha) : new Color(1, 1, 1, d.alpha);
            m.SetColor("_BaseColor", tex != null ? new Color(1, 1, 1, d.alpha) : baseC);
            m.SetTexture("_BaseMap", tex);
            m.SetFloat("_Metallic", d.metallic); m.SetFloat("_Smoothness", d.smoothness);
            if (d.alpha < 0.99f)
            {
                m.SetFloat("_Surface", 1f); m.SetFloat("_Blend", 0f);
                m.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha); m.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                m.SetFloat("_ZWrite", 0f); m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                m.SetOverrideTag("RenderType", "Transparent"); m.renderQueue = 3000;
            }
            else
            {
                m.SetFloat("_Surface", 0f); m.SetFloat("_ZWrite", 1f); m.DisableKeyword("_SURFACE_TYPE_TRANSPARENT");
                m.SetOverrideTag("RenderType", "Opaque"); m.renderQueue = -1;
            }
            m.SetFloat("_AlphaClip", d.cutout ? 1f : 0f); m.SetFloat("_Cutoff", 0.5f);
            if (d.cutout) { m.EnableKeyword("_ALPHATEST_ON"); m.renderQueue = 2450; } else m.DisableKeyword("_ALPHATEST_ON");
            m.SetFloat("_Cull", d.doubleSided ? 0f : 2f);
            if (d.emissionIntensity > 0f && d.emission != null && d.emission.Length >= 3)
            {
                m.EnableKeyword("_EMISSION");
                m.SetColor("_EmissionColor", new Color(d.emission[0], d.emission[1], d.emission[2]) * Mathf.Max(1f, d.emissionIntensity * 0.5f));
                if (tex != null) m.SetTexture("_EmissionMap", tex);
                m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
            }
            else { m.DisableKeyword("_EMISSION"); m.SetColor("_EmissionColor", Color.black); }
            EditorUtility.SetDirty(m);
            var id = new AssetImporter.SourceAssetIdentifier(typeof(Material), d.name);
            UnityEngine.Object cur;
            if (!have.TryGetValue(id, out cur) || cur != m) { imp.AddRemap(id, m); changed = true; }
            n++;
        }
        AssetDatabase.SaveAssets();
        if (changed) imp.SaveAndReimport();
        Debug.Log("[PNU Drive] 자차 재질 " + n + "개" + (changed ? " 연결(FBX 다시 가져옴)" : " 그대로"));
    }

    // ------------------------------------------------------------ 나무: 재질(잎 = 알파 자르기·양면·인스턴싱) + 그리는 컴포넌트
    [Serializable] class TreeMat { public string name, tex, normal; public float cutoff, smooth; public bool clip, both; public float[] color; }
    [Serializable] class TreeMats { public TreeMat[] mats; }

    static Material[] TreeMaterials()
    {
        var ta = AssetDatabase.LoadAssetAtPath<TextAsset>(ROOT + "Data/trees_mats.json");
        if (ta == null) return null;
        var L = JsonUtility.FromJson<TreeMats>(ta.text); var M = new Material[L.mats.Length];
        var sh = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
        for (int i = 0; i < L.mats.Length; i++)
        {
            var d = L.mats[i]; string path = MatDir + "PD_tree_" + d.name + ".mat";
            EnsureFolder(MatDir);
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null) { m = new Material(sh); AssetDatabase.CreateAsset(m, path); }
            m.shader = sh;
            var tp = ROOT + "Textures/Trees/" + d.tex + ".png";
            var imp = AssetImporter.GetAtPath(tp) as TextureImporter;
            if (imp != null)
            {
                bool ch = false;
                if (d.clip && (!imp.alphaIsTransparency || !imp.mipMapsPreserveCoverage)) { imp.alphaIsTransparency = true; imp.mipMapsPreserveCoverage = true; imp.alphaTestReferenceValue = d.cutoff; ch = true; }
                if (imp.maxTextureSize != 2048) { imp.maxTextureSize = 2048; ch = true; }
                if (ch) imp.SaveAndReimport();
            }
            var t = AssetDatabase.LoadAssetAtPath<Texture2D>(tp);
            if (t != null) { m.SetTexture("_BaseMap", t); m.SetTexture("_MainTex", t); }
            if (!string.IsNullOrEmpty(d.normal))
            {
                var np = ROOT + "Textures/Trees/" + d.normal + ".png";
                var ni = AssetImporter.GetAtPath(np) as TextureImporter;
                if (ni != null && ni.textureType != TextureImporterType.NormalMap) { ni.textureType = TextureImporterType.NormalMap; ni.SaveAndReimport(); }
                var nt = AssetDatabase.LoadAssetAtPath<Texture2D>(np);
                if (nt != null) { m.SetTexture("_BumpMap", nt); m.EnableKeyword("_NORMALMAP"); }
            }
            var c = d.color != null && d.color.Length >= 3 ? new Color(d.color[0], d.color[1], d.color[2]) : Color.white;
            m.SetColor("_BaseColor", c); m.SetColor("_Color", c);
            m.SetFloat("_Smoothness", d.smooth); m.SetFloat("_Glossiness", d.smooth); m.SetFloat("_Metallic", 0f);
            if (d.clip)
            {
                m.SetFloat("_AlphaClip", 1f); m.SetFloat("_Cutoff", d.cutoff); m.EnableKeyword("_ALPHATEST_ON");
                m.SetOverrideTag("RenderType", "TransparentCutout"); m.renderQueue = 2450;
            }
            m.SetFloat("_Cull", d.both ? 0f : 2f);
            m.enableInstancing = true;
            EditorUtility.SetDirty(m); M[i] = m;
        }
        AssetDatabase.SaveAssets();
        return M;
    }

    static void BuildTrees()
    {
        var tm = AssetDatabase.LoadAssetAtPath<TextAsset>(ROOT + "Data/trees_models.bytes");
        var ti = AssetDatabase.LoadAssetAtPath<TextAsset>(ROOT + "Data/trees_inst.bytes");
        if (tm == null || ti == null) { Debug.LogWarning("[PNU Drive] 나무 자료 없음"); return; }
        var mats = TreeMaterials(); if (mats == null) { Debug.LogWarning("[PNU Drive] 나무 재질 목록 없음"); return; }
        var go = new GameObject("PNU Drive Trees"); var tr = go.AddComponent<PNUDriveTrees>();
        tr.models = tm; tr.instances = ti; tr.materials = mats; tr.Load();
    }

    // ------------------------------------------------------------ 정문 자리: Data/landmarks.json → 빈 오브젝트(PNUDriveLandmark)
    [Serializable] class LmItem { public string key, name; public float[] p; public float yaw; public float[] ring; }
    [Serializable] class LmFile { public LmItem[] landmarks; }
    static void BuildLandmarks()
    {
        var ta = AssetDatabase.LoadAssetAtPath<TextAsset>(ROOT + "Data/landmarks.json");
        if (ta == null) return;
        var lf = JsonUtility.FromJson<LmFile>(ta.text);
        if (lf == null || lf.landmarks == null) return;
        var root = new GameObject("PNU Drive Landmarks (정문 자리)");
        foreach (var L in lf.landmarks)
        {
            if (L.p == null || L.p.Length < 3) continue;
            var go = new GameObject(L.name + " 자리 (모델을 여기 밑에)");
            go.transform.SetParent(root.transform, false);
            go.transform.position = new Vector3(L.p[0], L.p[1], L.p[2]);
            go.transform.rotation = Quaternion.Euler(0f, L.yaw, 0f);
            var lm = go.AddComponent<PNUDriveLandmark>(); lm.key = L.key;
            if (L.ring != null && L.ring.Length >= 6)
            {
                var pts = new List<Vector3>();
                for (int i = 0; i + 1 < L.ring.Length; i += 2) pts.Add(new Vector3(L.ring[i], L.p[1] + 0.3f, L.ring[i + 1]));
                lm.reserve = pts.ToArray();
            }
        }
        Debug.Log("[PNU Drive] 정문 자리 " + lf.landmarks.Length + "곳");
    }

    // ------------------------------------------------------------ 도로변 건물: Assets/JapaneseCity 건물 프리팹 재고 → 크기·정면 재서 PNUDriveCity 에 넘김
    const string JC = "Assets/JapaneseCity";
    static readonly System.Text.RegularExpressions.Regex BldName = new System.Text.RegularExpressions.Regex(
        @"^jctBld(12_6?[A-E]b?|24_12[ABC]b?|24_12[DE]_(short|tall)|24_[A-E]_(short|tall)|24_C_short_b|50_[A-E]_(short|tall))$");

    static GameObject FindLow(Dictionary<string, GameObject> all, string n)
    {
        foreach (var cand in new[] { n + "_low", n.Replace("_short", "_low_short").Replace("_tall", "_low_tall"), n.Replace("_short_b", "_low_short_b") })
        { GameObject g; if (cand != n && all.TryGetValue(cand, out g)) return g; }
        return null;
    }

    /// <summary>URP 가 아닌 재질(Standard 등)을 URP Lit 으로(텍스처·색·투명 옮김)</summary>
    static int ToURP(IEnumerable<Material> mats)
    {
        var lit = Shader.Find("Universal Render Pipeline/Lit"); if (lit == null) return 0;
        int n = 0;
        foreach (var m in mats)
        {
            if (m == null || m.shader == null) continue;
            if (m.shader.name.StartsWith("Universal Render Pipeline")) continue;
            Texture main = m.HasProperty("_MainTex") ? m.GetTexture("_MainTex") : null;
            Color col = m.HasProperty("_Color") ? m.GetColor("_Color") : Color.white;
            Texture bump = m.HasProperty("_BumpMap") ? m.GetTexture("_BumpMap") : null;
            Texture emi = m.HasProperty("_EmissionMap") ? m.GetTexture("_EmissionMap") : null;
            Color emc = m.HasProperty("_EmissionColor") ? m.GetColor("_EmissionColor") : Color.black;
            float gl = m.HasProperty("_Glossiness") ? m.GetFloat("_Glossiness") : 0.3f;
            float met = m.HasProperty("_Metallic") ? m.GetFloat("_Metallic") : 0f;
            float mode = m.HasProperty("_Mode") ? m.GetFloat("_Mode") : 0f;
            float cut = m.HasProperty("_Cutoff") ? m.GetFloat("_Cutoff") : 0.5f;
            Vector2 sc = m.HasProperty("_MainTex") ? m.GetTextureScale("_MainTex") : Vector2.one;
            Vector2 of = m.HasProperty("_MainTex") ? m.GetTextureOffset("_MainTex") : Vector2.zero;
            m.shader = lit;
            if (main != null) { m.SetTexture("_BaseMap", main); m.SetTextureScale("_BaseMap", sc); m.SetTextureOffset("_BaseMap", of); }
            m.SetColor("_BaseColor", col);
            if (bump != null) { m.SetTexture("_BumpMap", bump); m.EnableKeyword("_NORMALMAP"); }
            if (emi != null || emc.maxColorComponent > 0.01f) { if (emi != null) m.SetTexture("_EmissionMap", emi); m.SetColor("_EmissionColor", emc); m.EnableKeyword("_EMISSION"); }
            m.SetFloat("_Smoothness", gl); m.SetFloat("_Metallic", met);
            if (mode == 1f) { m.SetFloat("_AlphaClip", 1f); m.SetFloat("_Cutoff", cut); m.EnableKeyword("_ALPHATEST_ON"); m.renderQueue = 2450; }
            else if (mode >= 2f)
            {
                m.SetFloat("_Surface", 1f); m.SetFloat("_Blend", 0f);
                m.SetOverrideTag("RenderType", "Transparent"); m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                m.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha); m.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                m.SetInt("_ZWrite", 0); m.renderQueue = 3000;
            }
            m.enableInstancing = true;
            EditorUtility.SetDirty(m); n++;
        }
        return n;
    }

    static void BuildCity(Material[] mats)
    {
        var lotsTa = AssetDatabase.LoadAssetAtPath<TextAsset>(ROOT + "Data/lots.json");
        if (lotsTa == null) { Debug.LogWarning("[PNU Drive] lots.json 없음"); return; }
        var go = new GameObject("PNU Drive City (도로변 건물)"); var city = go.AddComponent<PNUDriveCity>();
        city.lots = lotsTa; city.fallback = mats != null && mats.Length > 16 ? mats[16] : null;
        city.plinthMat = mats != null && mats.Length > 21 ? mats[21] : null;          // 건물 밑 받침(비탈에서 뜨지 않게, v11)
        if (!AssetDatabase.IsValidFolder(JC + "/Prefabs/Buildings"))
        {
            Debug.LogWarning("[PNU Drive] Assets/JapaneseCity 가 없어 도로변 건물은 상자로 둡니다(폴더를 복사하면 다시 Build Scene)");
            city.prefabs = new GameObject[0]; city.size = new Vector3[0]; city.Build(); return;
        }
        var all = new Dictionary<string, GameObject>();
        foreach (var g in AssetDatabase.FindAssets("t:Prefab", new[] { JC + "/Prefabs/Buildings" }))
        {
            var p = AssetDatabase.GUIDToAssetPath(g); var o = AssetDatabase.LoadAssetAtPath<GameObject>(p);
            if (o != null) all[o.name] = o;
        }
        var P = new List<GameObject>(); var PL = new List<GameObject>(); var S = new List<Vector3>(); var C = new List<Vector3>(); var F = new List<float>();
        var used = new HashSet<Material>();
        foreach (var kv in all.OrderBy(k => k.Key))
        {
            if (!BldName.IsMatch(kv.Key)) continue;
            var inst = (GameObject)PrefabUtility.InstantiatePrefab(kv.Value); inst.transform.position = Vector3.zero; inst.transform.rotation = Quaternion.identity;
            var rs = inst.GetComponentsInChildren<Renderer>(true);
            if (rs.Length == 0) { UnityEngine.Object.DestroyImmediate(inst); continue; }
            foreach (var r in rs) foreach (var m in r.sharedMaterials) if (m != null) used.Add(m);
            // 정면: 문·출입구 재질이 붙은 면들의 법선 평균(수평). 없으면 -Z
            Vector3 acc = Vector3.zero;
            foreach (var r in rs)
            {
                var mf = r.GetComponent<MeshFilter>(); if (mf == null || mf.sharedMesh == null) continue;
                var mesh = mf.sharedMesh; var sm = r.sharedMaterials;
                Vector3[] nrm; try { nrm = mesh.normals; } catch { continue; }
                if (nrm == null || nrm.Length == 0) continue;
                for (int k = 0; k < mesh.subMeshCount && k < sm.Length; k++)
                {
                    if (sm[k] == null) continue; var nm = sm[k].name.ToLowerInvariant();
                    if (!(nm.Contains("door") || nm.Contains("shop") || nm.Contains("entrance"))) continue;
                    int[] tri; try { tri = mesh.GetTriangles(k); } catch { continue; }
                    for (int q = 0; q < tri.Length; q += 3) acc += r.transform.TransformDirection(nrm[tri[q]]);
                }
            }
            acc.y = 0; float yawF = acc.sqrMagnitude > 1e-4f ? Mathf.Atan2(acc.x, acc.z) * Mathf.Rad2Deg : 180f;
            // 정면을 +Z 로 돌려 크기 재기
            inst.transform.rotation = Quaternion.Euler(0f, -yawF, 0f);
            var b = rs[0].bounds; foreach (var r in rs) b.Encapsulate(r.bounds);
            UnityEngine.Object.DestroyImmediate(inst);
            if (b.size.x < 2f || b.size.z < 2f || b.size.y < 2f) continue;
            P.Add(kv.Value); PL.Add(FindLow(all, kv.Key)); S.Add(b.size); C.Add(new Vector3(b.center.x, b.min.y, b.center.z)); F.Add(yawF);
            Debug.Log("[PNU Drive] 건물 " + kv.Key + " 크기 " + b.size + " 정면 " + yawF.ToString("F0") + "°");
        }
        foreach (var pl in PL) if (pl != null) foreach (var r in pl.GetComponentsInChildren<Renderer>(true)) foreach (var m in r.sharedMaterials) if (m != null) used.Add(m);
        int nconv = ToURP(used); AssetDatabase.SaveAssets();
        Debug.Log("[PNU Drive] 건물 프리팹 " + P.Count + "종, URP 로 바꾼 재질 " + nconv);
        city.prefabs = P.ToArray(); city.prefabsLow = PL.ToArray(); city.size = S.ToArray(); city.center = C.ToArray(); city.frontYaw = F.ToArray();
        city.Build();
    }

    // ------------------------------------------------------------ 신호등·표지판(후배 프리팹)
    [Serializable] class Sig { public int j; public float[] pr, pl, dir, face, pedFace, aux; public float auxH; public int lanes, heads; public int group = -1; public bool unprot; public int lim; public string cross; public int name = -1, guide = -1; public int[] regs; public string kind = "main", ped = ""; public int pedGroup; }
    [Serializable] class Sgn { public string prefab; public float[] p, face; }
    [Serializable] class PedP { public int j, group; public float[] p, face; public string style; }
    [Serializable] class PlanJ { public int j; public float off; public bool left0; }
    [Serializable] class Placements { public Sig[] signals; public Sgn[] signs; public PedP[] peds; public PlanJ[] plans; public PNU.SignCell[] cells; }

    static Placements LoadPlacements()
    {
        var ta = AssetDatabase.LoadAssetAtPath<TextAsset>(ROOT + "Data/placements.json");
        return ta == null ? null : JsonUtility.FromJson<Placements>(ta.text);
    }

    static GameObject FindPrefab(string name)
    {
        foreach (var g in AssetDatabase.FindAssets(name + " t:Prefab"))
        {
            var p = AssetDatabase.GUIDToAssetPath(g);
            if (Path.GetFileNameWithoutExtension(p) == name) return AssetDatabase.LoadAssetAtPath<GameObject>(p);
        }
        return null;
    }

    /// <summary>프리팹 안에서 가장 큰 렌더러의 앞(법선 평균, uv2.x 가 앞면 표시면 그쪽만) — 루트 기준 수평 방향</summary>
    static Vector3 FrontLocal(GameObject inst, Renderer only = null)
    {
        Renderer best = only; float area = -1;
        if (best == null)
            foreach (var r in inst.GetComponentsInChildren<Renderer>(true)) { var e = r.bounds.size; float a = e.x * e.y + e.y * e.z + e.x * e.z; if (a > area) { area = a; best = r; } }
        var mf = best != null ? best.GetComponent<MeshFilter>() : null;
        if (mf == null || mf.sharedMesh == null) return Vector3.forward;
        var m = mf.sharedMesh; Vector3[] n; List<Vector2> uv2 = new List<Vector2>();
        try { n = m.normals; m.GetUVs(1, uv2); } catch { return Vector3.forward; }
        Vector3 acc = Vector3.zero;
        for (int i = 0; i < n.Length; i++) if (uv2.Count != n.Length || uv2[i].x > 0.5f) acc += n[i];
        var w = best.transform.TransformDirection(acc); w = inst.transform.InverseTransformDirection(w); w.y = 0;
        return w.sqrMagnitude < 1e-6f ? Vector3.forward : w.normalized;
    }

    // ------------------------------------------------------------ 신호등(v13): 후배 신호등 꾸러미(TrafficLightSystem) 조립 신호등
    //   차량: Generated/TrafficLight/TrafficLight_Modular.prefab — 기둥 + 내민 팔 + 4구 등 1~3개(SetSignalCount), 등 앞 = +Z, 팔 = +X(등 오른쪽 = 운전자 왼쪽)
    //   보행: 같은 기둥 보행 등(SetPedestrianVariant) 또는 PedestrianSignal.prefab(숫자 등 앞 = +Z, 막대 등은 3번 돌려 +Z 로)
    //   표지판(도로명·방면·제한속도·비보호): 이 맵 그림판, 팔·기둥에 붙임(PNUSignalAssembly, 180도 돌린 틀 = 예전 기준 앞 -Z·팔 -X)
    const string MastPrefab = "Assets/TrafficLightSystem/Generated/TrafficLight/TrafficLight_Modular.prefab";
    const string PedPrefab = "Assets/TrafficLightSystem/PedestrianSignal/PedestrianSignal.prefab";

    static GameObject FindPrefabByName(string path, string name)
    {
        var g = AssetDatabase.LoadAssetAtPath<GameObject>(path);
        if (g != null) return g;
        foreach (var gid in AssetDatabase.FindAssets(name + " t:Prefab"))
        {
            var p = AssetDatabase.GUIDToAssetPath(gid);
            if (Path.GetFileNameWithoutExtension(p) == name) { g = AssetDatabase.LoadAssetAtPath<GameObject>(p); if (g != null) return g; }
        }
        return null;
    }

    static void PlaceSignals()
    {
        var P = LoadPlacements(); if (P == null || P.signals == null) return;
        var mast = FindPrefabByName(MastPrefab, "TrafficLight_Modular");
        var pedPf = FindPrefabByName(PedPrefab, "PedestrianSignal");
        if (mast == null) { Debug.LogWarning("[PNU Drive] 후배 신호등(TrafficLight_Modular.prefab) 없음 — Tools > Traffic Lights > Setup Prepared Model 로 먼저 만드세요"); return; }
        var root = new GameObject("신호등 (v13 후배 조립 신호등)"); var sig = root.AddComponent<PNUDriveSignals>();
        if (P.plans != null) foreach (var q in P.plans) sig.plans.Add(new PNUDriveSignals.Plan { j = q.j, off = q.off, left0 = q.left0 });
        // 표지판 재질(그림판: 이 맵의 길 이름·방면)
        Material atlas = null, back = null, brk = null;
        if (P.cells != null && P.cells.Length > 0)
        {
            atlas = MakeMat("sign_atlas", "SignAtlas_PNUDrive", Color.white, 0.25f);
            if (atlas.HasProperty("_AlphaClip")) { atlas.SetFloat("_AlphaClip", 1f); atlas.SetFloat("_Cutoff", 0.5f); atlas.EnableKeyword("_ALPHATEST_ON"); }
            back = MakeMat("sign_back", null, new Color(0.62f, 0.64f, 0.66f), 0.3f);
            brk = MakeMat("sign_bracket", null, new Color(0.55f, 0.57f, 0.6f), 0.4f);
        }
        int nCar = 0, n2 = 0, nPedOn = 0, nPed = 0, nSign = 0, nAux = 0;
        var pedFix = new List<float>(); int pedGuess = 0;     // v16: 보행 등을 돌린 각(렌즈 기준), 렌즈를 못 읽어 모양으로 짐작한 수
        string meshCheck = null;
        foreach (var s in P.signals)
        {
            var g = (GameObject)PrefabUtility.InstantiatePrefab(mast); g.transform.SetParent(root.transform, false);
            g.name = (s.kind == "second" ? "차량신호_제2주_" : "차량신호_제1주_") + s.j + "_" + nCar;
            if (meshCheck == null) meshCheck = SignalMeshCheck(g);
            g.transform.position = new Vector3(s.pr[0], s.pr[1], s.pr[2]);
            var face = s.face != null && s.face.Length == 2 ? new Vector3(s.face[0], 0f, s.face[1]) : new Vector3(-s.dir[0], 0f, -s.dir[1]);
            g.transform.rotation = Quaternion.LookRotation(face.normalized, Vector3.up);
            var ctl = g.GetComponent<ModularTrafficLights.TrafficLightController>();
            if (ctl == null) { Debug.LogWarning("[PNU Drive] TrafficLight_Modular 에 TrafficLightController 없음"); UnityEngine.Object.DestroyImmediate(g); break; }
            ctl.SetSignalCount(Mathf.Clamp(s.heads, 1, 3));
            bool hasPed = !string.IsNullOrEmpty(s.ped);
            ctl.SetPedestrianVariant(!hasPed ? ModularTrafficLights.PedestrianVariant.None : (s.ped == "bar" ? ModularTrafficLights.PedestrianVariant.Bar : ModularTrafficLights.PedestrianVariant.Number));
            ctl.SetVehicleAspect(ModularTrafficLights.VehicleAspect.Green);
            var L = g.AddComponent<PNUDriveLamp>(); L.junction = s.j; L.group = Mathf.Max(0, s.group); L.pedGroup = s.pedGroup; L.hasPed = hasPed; L.vehicle = ctl;
            if (hasPed && s.pedFace != null && s.pedFace.Length == 2) AimPedSocket(g, new Vector3(s.pedFace[0], 0f, s.pedFace[1]), s.ped == "bar" ? "Bar" : "Number", pedFix, ref pedGuess);   // 보행 등 렌즈를 건너편 쪽으로
            if (s.aux != null && s.aux.Length == 2) { L.aux = AddAuxSignal(g, new Vector3(s.aux[0], 0f, s.aux[1]), hasPed ? 3.85f : 3.1f); if (L.aux != null) nAux++; }
            sig.lamps.Add(L); nCar++; if (s.kind == "second") n2++; if (hasPed) nPedOn++;
            if (atlas != null && s.kind != "second")
            {
                var frame = new GameObject("SignFrame"); frame.transform.SetParent(g.transform, false); frame.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);
                var asm = frame.AddComponent<PNU.PNUSignalAssembly>(); asm.atlasMaterial = atlas; asm.backMaterial = back; asm.bracketMaterial = brk; asm.armHeight = 6.0f;
                if (s.name >= 0 && s.name < P.cells.Length) { var c = P.cells[s.name]; asm.nameSign = new PNU.SignPlacement { cell = c, localPos = new Vector3(-(0.8f + c.w * 0.5f), asm.armHeight - 0.2f - c.h * 0.5f, -0.06f) }; }
                if (s.guide >= 0 && s.guide < P.cells.Length) { var c = P.cells[s.guide]; asm.guideSign = new PNU.SignPlacement { cell = c, localPos = new Vector3(-(0.8f + c.w * 0.5f), asm.armHeight + 0.35f + c.h * 0.5f, -0.06f) }; }
                if (s.regs != null)
                    for (int k = 0; k < s.regs.Length; k++)
                    {
                        if (s.regs[k] < 0 || s.regs[k] >= P.cells.Length) continue;
                        var c = P.cells[s.regs[k]]; asm.poleSigns.Add(new PNU.SignPlacement { cell = c, localPos = new Vector3(0f, 4.55f + k * 0.8f, -0.36f) });   // 보행 등(2.3~3.4m)·보조 등 위
                    }
                asm.Rebuild(); nSign++;
            }
        }
        if (P.peds != null && pedPf != null)
            foreach (var q in P.peds)
            {
                var g = (GameObject)PrefabUtility.InstantiatePrefab(pedPf); g.transform.SetParent(root.transform, false);
                g.name = "보행신호_" + q.j + "_" + nPed;
                g.transform.position = new Vector3(q.p[0], q.p[1], q.p[2]);
                var pfaceW = new Vector3(q.face[0], 0f, q.face[1]).normalized;
                g.transform.rotation = Quaternion.LookRotation(pfaceW, Vector3.up);
                var pc = g.GetComponent<ModularTrafficLights.PedestrianPoleController>();
                ModularTrafficLights.PedestrianSignalController ps = null;
                if (pc != null)
                {
                    bool bar = q.style == "bar";
                    pc.selectedVariant = bar ? ModularTrafficLights.PedestrianPoleController.SignalVariant.Bar : ModularTrafficLights.PedestrianPoleController.SignalVariant.Number;
                    pc.numberQuarterTurns = 0; pc.barQuarterTurns = 0;                       // v16: 둘 다 원래 자리(예전 막대 3번 돌림 = 90° 어긋남)
                    pc.ApplyConfiguration();
                    ps = bar ? pc.barSignal : pc.numberSignal;
                    pc.enabled = false;                                                     // 꾸러미 자체 막대 반복(시연용) 끔 → 신호 계획이 켬
                    // v16: 등 렌즈의 실제 앞 방향을 재서 건너편(횡단보도 반대쪽 끝)을 보게 기둥째 돌림
                    var head = ps != null ? ps.transform : g.transform;
                    bool byLens; var cur = PedFacing(head, g.transform.position, null, out byLens);
                    if (cur.sqrMagnitude > 1e-6f)
                    {
                        float yaw = Vector3.SignedAngle(cur, pfaceW, Vector3.up);
                        g.transform.rotation = Quaternion.AngleAxis(yaw, Vector3.up) * g.transform.rotation;
                        pedFix.Add(Mathf.Abs(yaw)); if (!byLens) pedGuess++;
                    }
                }
                var L = g.AddComponent<PNUDriveLamp>(); L.junction = q.j; L.group = q.group; L.pedGroup = q.group; L.pedestrian = true; L.ped = ps;
                sig.lamps.Add(L); nPed++;
            }
        else if (pedPf == null) Debug.LogWarning("[PNU Drive] 보행 신호 기둥(PedestrianSignal.prefab) 없음");
        Debug.Log("[PNU Drive] 신호등(v13 후배 조립) 차량 " + nCar + "(제2주 " + n2 + ", 보조 " + nAux + ") · 차량 기둥 보행 " + nPedOn + " · 보행 기둥 " + nPed + " · 표지 붙은 신호 " + nSign + " · 계획 " + sig.plans.Count + " (보호 좌회전 " + sig.plans.Count(p => p.left0) + ")");
        Debug.Log("[PNU Drive] 신호등(v16): 면 방향 " + (meshCheck ?? "확인 못함") + " · 보행 등 " + pedFix.Count + "개를 렌즈 앞이 건너편을 보게 돌림(평균 " + (pedFix.Count > 0 ? pedFix.Average() : 0f).ToString("F0") + "°, 최대 " + (pedFix.Count > 0 ? pedFix.Max() : 0f).ToString("F0") + "°" + (pedGuess > 0 ? ", 렌즈 못 읽어 모양으로 짐작 " + pedGuess : "") + ")");
    }

    /// <summary>차량 기둥의 보행 등을 건너편 쪽으로(v16): 보행 소켓을 기둥 둘레(위 축)로 돌려 렌즈 앞이 target 을 보게.
    /// v13~v15 는 '등 몸이 기둥에서 벗어난 쪽'을 맞췄는데, 후배 모델은 렌즈가 그 쪽에서 90° 옆을 봐서 등이 도로(운전자) 쪽을 봤음</summary>
    static void AimPedSocket(GameObject g, Vector3 target, string prefer, List<float> fix, ref int guess)
    {
        Transform sock = null;
        foreach (var t in g.GetComponentsInChildren<Transform>(true)) if (t.name.Contains("Socket_Pedestrian")) { sock = t; break; }
        if (sock == null || target.sqrMagnitude < 1e-6f) return;
        target.y = 0f;
        bool byLens; var cur = PedFacing(sock, sock.position, prefer, out byLens);
        if (cur.sqrMagnitude < 1e-6f) return;
        float yaw = Vector3.SignedAngle(cur, target.normalized, Vector3.up);
        sock.rotation = Quaternion.AngleAxis(yaw, Vector3.up) * sock.rotation;
        fix.Add(Mathf.Abs(yaw)); if (!byLens) guess++;
    }

    /// <summary>보행 등(켜진 것) 렌즈의 앞 방향(월드, 수평). 렌즈 메쉬 감김으로 잼(byLens).
    /// 못 읽으면: 등 몸이 기둥(axis)에서 벗어난 쪽을 -90° 돌린 쪽(후배 숫자·막대 등 모델 공통 모양)</summary>
    static Vector3 PedFacing(Transform under, Vector3 axis, string prefer, out bool byLens)
    {
        byLens = false;
        var all = under.GetComponentsInChildren<ModularTrafficLights.PedestrianSignalController>(false);
        if (!string.IsNullOrEmpty(prefer) && all.Length > 1)        // 숫자·막대 둘 다 켜져 있으면 고른 쪽 먼저
            all = all.OrderBy(x => x.name.IndexOf(prefer, StringComparison.OrdinalIgnoreCase) >= 0 ? 0 : 1).ToArray();
        foreach (var ps in all)
        {
            Vector3 f = Vector3.zero;
            if (ps.red != null) f += LensFacing(ps.red.renderer);
            if (ps.green != null) f += LensFacing(ps.green.renderer);
            f.y = 0f;
            if (f.sqrMagnitude > 1e-6f) { byLens = true; return f.normalized; }
        }
        var rs = under.GetComponentsInChildren<Renderer>(false);
        if (rs.Length == 0) return Vector3.zero;
        Bounds b = rs[0].bounds; for (int i = 1; i < rs.Length; i++) b.Encapsulate(rs[i].bounds);
        Vector3 d = b.center - axis; d.y = 0f;
        if (d.sqrMagnitude < 1e-4f) return Vector3.zero;
        return Quaternion.AngleAxis(-90f, Vector3.up) * d.normalized;
    }

    /// <summary>렌즈(얇은 판)의 앞: 유니티 앞면 = Cross(b-a, c-a). 넓이 합 → 월드 방향(수평). 못 읽으면 0</summary>
    static Vector3 LensFacing(Renderer r)
    {
        var mf = r != null ? r.GetComponent<MeshFilter>() : null;
        if (mf == null || mf.sharedMesh == null) return Vector3.zero;
        Vector3[] v; int[] t;
        try { v = mf.sharedMesh.vertices; t = mf.sharedMesh.triangles; } catch { return Vector3.zero; }
        if (v == null || t == null || v.Length == 0 || t.Length < 3) return Vector3.zero;
        Vector3 acc = Vector3.zero;
        for (int i = 0; i + 2 < t.Length; i += 3) acc += Vector3.Cross(v[t[i + 1]] - v[t[i]], v[t[i + 2]] - v[t[i]]);
        var M = r.transform.localToWorldMatrix; var w = M.MultiplyVector(acc); if (M.determinant < 0f) w = -w;
        w.y = 0f;
        return w.sqrMagnitude < 1e-12f ? Vector3.zero : w.normalized;
    }

    /// <summary>후배 신호등 기둥·팔 메쉬가 바깥을 보는지(감김 부호 부피). 신호등 FBX 가져오기 보정(v2)이 됐는지 빌드 기록에 남김</summary>
    // v17: 기둥처럼 여러 조각(관 + 바닥판 + 날개)인 메쉬는 "전체 부호 부피"가 판 위치 때문에 음수가 될 수 있어(올바른 판 윗면도 음수)
    //   긴 축에서 바깥을 보는 면의 넓이 비율로 판정(옆을 보는 면만, 80% 이상이면 바깥)
    static string SignalMeshCheck(GameObject g)
    {
        var parts = new List<string>(); int inward = 0;
        foreach (var mf in g.GetComponentsInChildren<MeshFilter>(true))
        {
            var m = mf.sharedMesh; if (m == null) continue;
            string key = mf.name.Contains("Pole_Static") ? "기둥" : mf.name.Contains("Arm_Base") ? "팔 뿌리" : mf.name.Contains("ExtensionB") ? "팔 끝" : mf.name.Contains("ExtensionA") ? "팔 가운데" : null;
            if (key == null) continue;
            double outA = 0, allA = 0;
            try
            {
                var v = m.vertices; var t = m.triangles; var b = m.bounds; var c = b.center; var sz = b.size;
                int ax = sz.x >= sz.y && sz.x >= sz.z ? 0 : (sz.y >= sz.z ? 1 : 2);
                for (int i = 0; i + 2 < t.Length; i += 3)
                {
                    Vector3 p0 = v[t[i]], p1 = v[t[i + 1]], p2 = v[t[i + 2]];
                    Vector3 n = Vector3.Cross(p1 - p0, p2 - p0); float area = n.magnitude * 0.5f; if (area < 1e-10f) continue;
                    Vector3 r = (p0 + p1 + p2) / 3f - c;
                    if (ax == 0) r.x = 0f; else if (ax == 1) r.y = 0f; else r.z = 0f;
                    if (r.sqrMagnitude < 1e-10f) continue;
                    float d = Vector3.Dot(n.normalized, r.normalized); if (Mathf.Abs(d) < 0.3f) continue;
                    allA += area; if (d > 0f) outA += area;
                }
            }
            catch { parts.Add(key + " ?"); continue; }
            double f = allA > 0 ? outA / allA : 1.0;
            if (f < 0.8) inward++;
            parts.Add(key + " 바깥 " + Mathf.RoundToInt((float)(f * 100.0)) + "%" + (f < 0.8 ? "✗" : ""));
        }
        return parts.Count == 0 ? "확인 못함" : string.Join(", ", parts) + (inward == 0 ? " ✓" : " → 신호등 FBX 다시 가져오기 필요");
    }

    /// <summary>보조신호등(표 3-2 주1): 첫째 등(4구)을 0.6배로 복사해 기둥에, 정지선 가운데를 보게. 신호 계획은 같은 무리</summary>
    static ModularTrafficLights.SignalLightController AddAuxSignal(GameObject g, Vector3 face, float h)
    {
        ModularTrafficLights.SignalLightController src = null;
        foreach (var c in g.GetComponentsInChildren<ModularTrafficLights.SignalLightController>(true)) { if (src == null || c.name.Contains("S1")) src = c; if (c.name.Contains("S1")) break; }
        if (src == null || face.sqrMagnitude < 1e-6f) return null;
        Quaternion rel = Quaternion.Inverse(g.transform.rotation) * src.transform.rotation;   // 원래 등의 몸 기준 방향(앞 = +Z)
        var copy = UnityEngine.Object.Instantiate(src.gameObject, g.transform);
        copy.name = "보조신호등"; copy.SetActive(true);
        Vector3 ld = Quaternion.Inverse(g.transform.rotation) * face.normalized; ld.y = 0f; ld.Normalize();
        const float S = 0.6f;
        copy.transform.localScale = Vector3.one * S;
        copy.transform.localRotation = Quaternion.FromToRotation(Vector3.forward, ld) * rel;
        copy.transform.localPosition = ld * 0.42f + Vector3.up * (h + 0.23f * S);
        foreach (var c in copy.GetComponentsInChildren<Collider>(true)) UnityEngine.Object.DestroyImmediate(c);
        var ctl = copy.GetComponent<ModularTrafficLights.SignalLightController>();
        if (ctl != null) ctl.SetAspect(ModularTrafficLights.VehicleAspect.Green);
        return ctl;
    }

    static void PlaceSigns()
    {
        var P = LoadPlacements(); if (P == null || P.signs == null) return;
        EditorApplication.ExecuteMenuItem("Tools/Optimized Road Signs/Create 10 Prefabs");
        var root = new GameObject("표지판"); var cache = new System.Collections.Generic.Dictionary<string, KeyValuePair<GameObject, Vector3>>();
        int n = 0;
        foreach (var s in P.signs)
        {
            KeyValuePair<GameObject, Vector3> e;
            if (!cache.TryGetValue(s.prefab, out e))
            {
                var pf = FindPrefab(s.prefab); var f = Vector3.forward;
                if (pf != null) { var probe = (GameObject)PrefabUtility.InstantiatePrefab(pf); f = FrontLocal(probe); UnityEngine.Object.DestroyImmediate(probe); }
                e = new KeyValuePair<GameObject, Vector3>(pf, f); cache[s.prefab] = e;
            }
            if (e.Key == null) continue;
            var go = (GameObject)PrefabUtility.InstantiatePrefab(e.Key); go.transform.SetParent(root.transform, false);
            go.transform.position = new Vector3(s.p[0], s.p[1], s.p[2]);
            go.transform.rotation = Quaternion.FromToRotation(e.Value, new Vector3(s.face[0], 0, s.face[1]).normalized);
            n++;
        }
        Debug.Log("[PNU Drive] 표지판 " + n + "개");
    }

    // ------------------------------------------------------------ 4/6 NPC 차·사람(v14)
    //   차: Vehicles/PNU_Vehicles.fbx(MaG80 Traffic Car Pack CC-BY + RgsDev CC0 을 한 파일로, 앞 = +Z·원점 = 축 가운데 노면·실제 크기) + 팔레트 그림 8장(차 색)
    //       → 차마다 프리팹(PNUDriveVehicle: 바퀴·등·LOD), 팔레트 재질(불투명·투명), 브레이크·깜빡이 재질
    //   사람: People/<이름>/ 사람 FBX(Renderpeople, CC-BY·NoAI) → Humanoid, 걷기(nathan)·서기(sophia) 동작을 모두에게, 재질(불투명·투명)
    //   씬: PNUDriveTraffic(차·사람 계산 + 풀) 하나
    const string VehDir = ROOT + "Vehicles/";
    const string VehFbx = VehDir + "PNU_Vehicles.fbx";
    const string VehMatDir = ROOT + "Materials/Vehicles/";
    const string PrefabDir = ROOT + "Prefabs/";
    const string PeopleDir = ROOT + "People/";

    [Serializable] class VehInfo { public string name = "", kind = "car"; public float weight = 1f, length = 4.5f, width = 1.9f, height = 1.5f, wheelR = 0.33f, wheelbase = 2.7f; public bool @fixed; public string[] wheels, lights; }
    [Serializable] class VehJson { public VehInfo[] vehicles; }
    [Serializable] class PaintInfo { public string name = ""; public float weight = 1f; }
    [Serializable] class PaintJson { public PaintInfo[] paints; }

    static void BuildTraffic(PNUDriveEgo ego)
    {
        var lanes = AssetDatabase.LoadAssetAtPath<TextAsset>(ROOT + "Data/lanes.json");
        if (lanes == null) { Debug.LogWarning("[PNU Drive] 차·사람 건너뜀: Data/lanes.json 없음"); return; }
        var go = new GameObject("PNU Traffic (차·사람)"); var tr = go.AddComponent<PNUDriveTraffic>();
        tr.lanesJson = lanes; tr.ego = ego;
        Material[] fade; float[] pw; Material brake, blink;
        tr.paintMats = VehicleMaterials(out fade, out pw, out brake, out blink); tr.paintFadeMats = fade; tr.paintWeights = pw; tr.brakeMat = brake; tr.blinkMat = blink;
        try { tr.carPrefabs = VehiclePrefabs(); } catch (Exception e) { Debug.LogWarning("[PNU Drive] 차 프리팹 실패: " + e); tr.carPrefabs = new PNUDriveVehicle[0]; }
        try { tr.personPrefabs = PersonPrefabs(); } catch (Exception e) { Debug.LogWarning("[PNU Drive] 사람 프리팹 실패: " + e); tr.personPrefabs = new PNUDrivePerson[0]; }
        WriteCredits();
        EditorUtility.SetDirty(tr);
        Debug.Log("[PNU Drive] 차·사람(v14): 차 모델 " + tr.carPrefabs.Length + " × 색 " + (tr.paintMats != null ? tr.paintMats.Length : 0) + " (" + tr.carCount + "대), 사람 모델 " + tr.personPrefabs.Length + " (" + tr.peopleCount + "명)");
    }

    // ---------- 차 재질: 팔레트(8×8 칸 그림, 칸 = 재질 한 가지) → 불투명·투명, 브레이크(빨강 빛)·깜빡이(주황 빛)
    static Material[] VehicleMaterials(out Material[] fade, out float[] weights, out Material brake, out Material blink)
    {
        EnsureFolder(VehMatDir);
        var pj = AssetDatabase.LoadAssetAtPath<TextAsset>(ROOT + "Data/paints.json");
        var P = pj != null ? JsonUtility.FromJson<PaintJson>(pj.text) : null;
        var names = new List<string>(); var w = new List<float>();
        if (P != null && P.paints != null) foreach (var p in P.paints) { names.Add(p.name); w.Add(p.weight); }
        if (names.Count == 0) { names.Add("white"); w.Add(1f); }
        var sh = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
        var mats = new List<Material>(); var fades = new List<Material>();
        for (int k = 0; k < names.Count; k++)
        {
            string tp = VehDir + "Tex/VehiclePalette_" + k + "_" + names[k] + ".png";
            var ti = AssetImporter.GetAtPath(tp) as TextureImporter;
            if (ti != null && (ti.filterMode != FilterMode.Point || ti.mipmapEnabled || ti.textureCompression != TextureImporterCompression.Uncompressed))
            { ti.filterMode = FilterMode.Point; ti.mipmapEnabled = false; ti.wrapMode = TextureWrapMode.Clamp; ti.textureCompression = TextureImporterCompression.Uncompressed; ti.SaveAndReimport(); }
            var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(tp);
            if (tex == null) Debug.LogWarning("[PNU Drive] 차 팔레트 그림 없음: " + tp);
            for (int f = 0; f < 2; f++)
            {
                string path = VehMatDir + "PD_veh_" + k + "_" + names[k] + (f == 1 ? "_fade" : "") + ".mat";
                var m = AssetDatabase.LoadAssetAtPath<Material>(path);
                if (m == null) { m = new Material(sh); AssetDatabase.CreateAsset(m, path); }
                m.shader = sh;
                if (tex != null) { m.SetTexture("_BaseMap", tex); m.SetTexture("_MainTex", tex); }
                m.SetColor("_BaseColor", Color.white); m.SetFloat("_Smoothness", 0.62f); m.SetFloat("_Metallic", 0.15f);
                m.enableInstancing = true;
                if (f == 1) TeslaTransparent(m, 1f);
                EditorUtility.SetDirty(m);
                (f == 0 ? mats : fades).Add(m);
            }
        }
        brake = MakeMat("veh_brake", null, new Color(0.9f, 0.05f, 0.04f), 0.6f, 0f, 1f, false, new Color(3.2f, 0.12f, 0.08f));
        blink = MakeMat("veh_blink", null, new Color(1f, 0.55f, 0.05f), 0.6f, 0f, 1f, false, new Color(3.4f, 1.5f, 0.1f));
        AssetDatabase.SaveAssets();
        fade = fades.ToArray(); weights = w.ToArray();
        return mats.ToArray();
    }

    // ---------- 차 프리팹: FBX 안 VEH_* 마다 빈 뿌리 아래로, 앞 = +Z·바퀴 바닥 = 0·축 가운데 = 원점·길이 = vehicles.json 이 되게 맞춤
    static PNUDriveVehicle[] VehiclePrefabs()
    {
        var fbx = AssetDatabase.LoadAssetAtPath<GameObject>(VehFbx);
        var vj = AssetDatabase.LoadAssetAtPath<TextAsset>(ROOT + "Data/vehicles.json");
        if (fbx == null || vj == null) { Debug.LogWarning("[PNU Drive] 차 자료 없음: " + VehFbx + (fbx == null ? " ✗" : " ✓") + ", Data/vehicles.json" + (vj == null ? " ✗" : " ✓")); return new PNUDriveVehicle[0]; }
        var mi = AssetImporter.GetAtPath(VehFbx) as ModelImporter;
        if (mi != null && (mi.materialImportMode != ModelImporterMaterialImportMode.None || mi.importAnimation || mi.animationType != ModelImporterAnimationType.None))
        {
            mi.materialImportMode = ModelImporterMaterialImportMode.None; mi.importAnimation = false; mi.animationType = ModelImporterAnimationType.None;
            mi.importCameras = false; mi.importLights = false; mi.SaveAndReimport();
            fbx = AssetDatabase.LoadAssetAtPath<GameObject>(VehFbx);
        }
        var info = JsonUtility.FromJson<VehJson>(vj.text);
        EnsureFolder(PrefabDir + "Vehicles");
        var outp = new List<PNUDriveVehicle>(); var rep = new List<string>();
        foreach (var vi in info.vehicles)
        {
            // FBX 를 통째로 꺼내(부모 축 변환 유지) 이 차만 남김
            var all = (GameObject)PrefabUtility.InstantiatePrefab(fbx);
            PrefabUtility.UnpackPrefabInstance(all, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
            Transform src = all.GetComponentsInChildren<Transform>(true).FirstOrDefault(x => x.name == vi.name);
            if (src == null) { UnityEngine.Object.DestroyImmediate(all); rep.Add(vi.name + " 없음"); continue; }
            var root = new GameObject(vi.name);
            src.SetParent(root.transform, true); src.name = "model";
            UnityEngine.Object.DestroyImmediate(all);
            foreach (var c in root.GetComponentsInChildren<Collider>(true)) UnityEngine.Object.DestroyImmediate(c);
            Func<string, Transform> part = key => root.GetComponentsInChildren<Transform>(true).FirstOrDefault(x => x.name == vi.name + "_" + key || x.name.EndsWith("_" + key));
            var wheels = (vi.wheels ?? new string[0]).Select(k => part(k)).Where(x => x != null).ToArray();
            var fw = wheels.Where(x => x.name.Contains("Wheel_F")).ToArray(); var rw = wheels.Where(x => !x.name.Contains("Wheel_F")).ToArray();
            // 1) 앞 = +Z (앞바퀴 가운데 - 뒷바퀴 가운데)
            float yawFix = 0f;
            if (fw.Length > 0 && rw.Length > 0)
            {
                Vector3 d = Avg(fw) - Avg(rw); d.y = 0f;
                if (d.sqrMagnitude > 0.01f) { yawFix = Vector3.SignedAngle(d, Vector3.forward, Vector3.up); if (Mathf.Abs(yawFix) > 1f) src.RotateAround(Vector3.zero, Vector3.up, yawFix); }
            }
            // 2) 크기: 몸체 길이 → vehicles.json 길이
            var bodyR = part("Body") != null ? part("Body").GetComponent<Renderer>() : null;
            float scale = 1f;
            if (bodyR != null && bodyR.bounds.size.z > 0.01f) { scale = vi.length / bodyR.bounds.size.z; if (Mathf.Abs(scale - 1f) > 0.03f) src.localScale *= scale; else scale = 1f; }
            // 3) 원점: 축 가운데(XZ) + 바퀴 바닥 = 0
            Vector3 mid = (fw.Length > 0 && rw.Length > 0) ? (Avg(fw) + Avg(rw)) * 0.5f : Vector3.zero;
            float bottom = float.MaxValue;
            foreach (var wt in wheels) { var r = wt.GetComponent<Renderer>(); if (r != null) bottom = Mathf.Min(bottom, r.bounds.min.y); }
            if (bottom == float.MaxValue) bottom = bodyR != null ? bodyR.bounds.min.y : 0f;
            src.position -= new Vector3(mid.x, bottom, mid.z);
            // 4) 부품·등·LOD
            var v = root.AddComponent<PNUDriveVehicle>();
            v.kind = vi.kind; v.length = vi.length; v.width = vi.width; v.height = vi.height; v.wheelbase = vi.wheelbase; v.wheelR = vi.wheelR; v.weight = vi.weight; v.fixedPaint = vi.@fixed;
            v.wheels = wheels; v.wheelFront = wheels.Select(x => x.name.Contains("Wheel_F")).ToArray();
            Func<string, Renderer> rr = key => { var t = part(key); return t != null ? t.GetComponent<Renderer>() : null; };
            v.lightL = rr("Light_L"); v.lightR = rr("Light_R"); v.blinkL = rr("Blink_L"); v.blinkR = rr("Blink_R");
            foreach (var lr in new[] { v.lightL, v.lightR, v.blinkL, v.blinkR }) if (lr != null) { lr.enabled = false; lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; }
            var lod1 = rr("LOD1");
            var paint = new List<Renderer>(); if (bodyR != null) paint.Add(bodyR);
            foreach (var wt in wheels) { var r = wt.GetComponent<Renderer>(); if (r != null) paint.Add(r); }
            if (lod1 != null) paint.Add(lod1);
            v.paintRenderers = paint.ToArray();
            var lod0 = new List<Renderer>(paint.Where(r => r != lod1)); foreach (var lr in new[] { v.lightL, v.lightR, v.blinkL, v.blinkR }) if (lr != null) lod0.Add(lr);
            var lg = root.AddComponent<LODGroup>(); v.lod = lg;
            if (lod1 != null) lg.SetLODs(new[] { new LOD(0.04f, lod0.ToArray()), new LOD(0.004f, new[] { lod1 }) });      // 화면 높이 4% 아래면 줄인 모양, 0.4% 아래면 안 그림
            else lg.SetLODs(new[] { new LOD(0.004f, lod0.ToArray()) });
            lg.RecalculateBounds();
            // 확인 기록: 고친 뒤 몸체 크기·앞바퀴 z
            var b = bodyR != null ? bodyR.bounds : new Bounds();
            float fz = fw.Length > 0 ? Avg(fw).z : 0f;
            // v16: 앞바퀴가 꺾인 채면 옆 폭이 뒷바퀴의 1.5~2배 → 곧은 바퀴면 1.0 근처
            Func<Transform[], float> wx = ws => { float sum = 0f; int n = 0; foreach (var x in ws) { var r = x.GetComponent<Renderer>(); if (r != null) { sum += r.bounds.size.x; n++; } } return n > 0 ? sum / n : 0f; };
            float fwX = wx(fw), rwX = wx(rw);
            rep.Add(vi.name + (Mathf.Abs(yawFix) > 1f ? " 돌림" + yawFix.ToString("F0") + "°" : "") + (scale != 1f ? " ×" + scale.ToString("F2") : "") + " 길이" + b.size.z.ToString("F2") + "/폭" + b.size.x.ToString("F2") + "/높이" + b.size.y.ToString("F2") + " 앞바퀴z" + fz.ToString("F2") + (rwX > 0.01f ? " 앞뒤바퀴폭" + (fwX / rwX).ToString("F2") : "") + " 등" + new[] { v.lightL, v.lightR, v.blinkL, v.blinkR }.Count(x => x != null) + (lod1 != null ? " LOD" : ""));
            var pf = PrefabUtility.SaveAsPrefabAsset(root, PrefabDir + "Vehicles/" + vi.name + ".prefab");
            UnityEngine.Object.DestroyImmediate(root);
            if (pf != null) outp.Add(pf.GetComponent<PNUDriveVehicle>());
        }
        Debug.Log("[PNU Drive] 차 프리팹 " + outp.Count + "/" + info.vehicles.Length + ": " + string.Join(" · ", rep));
        return outp.ToArray();
    }

    static Vector3 Avg(Transform[] t) { var s = Vector3.zero; foreach (var x in t) s += x.position; return t.Length > 0 ? s / t.Length : s; }

    // ---------- 사람: Humanoid 로 가져오기 → 걷기·서기 동작 컨트롤러 → 프리팹(재질은 그 사람 그림으로)
    class PersonSrc { public string dir, fbx, dif, norm; }
    static PersonSrc[] FindPeople()
    {
        var list = new List<PersonSrc>();
        if (!AssetDatabase.IsValidFolder(PeopleDir.TrimEnd('/'))) return list.ToArray();
        foreach (var d in Directory.GetDirectories(PeopleDir).Select(x => x.Replace('\\', '/')).OrderBy(x => x))
        {
            var f = Directory.GetFiles(d, "*.fbx").Select(x => x.Replace('\\', '/')).FirstOrDefault(); if (f == null) continue;
            var imgs = Directory.GetFiles(d).Select(x => x.Replace('\\', '/')).Where(x => x.EndsWith(".jpg") || x.EndsWith(".jpeg") || x.EndsWith(".png")).ToArray();
            list.Add(new PersonSrc { dir = d, fbx = f, dif = imgs.FirstOrDefault(x => x.Contains("_dif")), norm = imgs.FirstOrDefault(x => x.Contains("_norm")) });
        }
        return list.ToArray();
    }

    static AnimationClip FirstClip(string fbx)
    {
        return AssetDatabase.LoadAllAssetsAtPath(fbx).OfType<AnimationClip>().FirstOrDefault(c => !c.name.StartsWith("__preview__"));
    }

    static PNUDrivePerson[] PersonPrefabs()
    {
        var src = FindPeople();
        if (src.Length == 0) { Debug.LogWarning("[PNU Drive] 사람 모델 없음(" + PeopleDir + ")"); return new PNUDrivePerson[0]; }
        // 1) Humanoid·동작 반복으로 가져오기, 그림 크기 2048 까지
        foreach (var p in src)
        {
            var mi = AssetImporter.GetAtPath(p.fbx) as ModelImporter; if (mi == null) continue;
            bool ch = false;
            if (mi.animationType != ModelImporterAnimationType.Human) { mi.animationType = ModelImporterAnimationType.Human; mi.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel; ch = true; }
            if (mi.materialImportMode != ModelImporterMaterialImportMode.None) { mi.materialImportMode = ModelImporterMaterialImportMode.None; ch = true; }
            if (!mi.importAnimation) { mi.importAnimation = true; ch = true; }
            if (ch) { mi.SaveAndReimport(); mi = AssetImporter.GetAtPath(p.fbx) as ModelImporter; }
            var clips = mi.clipAnimations != null && mi.clipAnimations.Length > 0 ? mi.clipAnimations : mi.defaultClipAnimations;
            if (clips != null && clips.Length > 0 && clips.Any(c => !c.loopTime || !c.lockRootRotation))
            {
                foreach (var c in clips) { c.loopTime = true; c.lockRootRotation = true; c.lockRootHeightY = true; c.keepOriginalOrientation = true; c.keepOriginalPositionY = true; }
                mi.clipAnimations = clips; mi.SaveAndReimport();
            }
            foreach (var img in new[] { p.dif, p.norm })
            {
                if (img == null) continue;
                var ti = AssetImporter.GetAtPath(img) as TextureImporter; if (ti == null) continue;
                bool isN = img == p.norm;
                if (ti.maxTextureSize != 2048 || (isN && ti.textureType != TextureImporterType.NormalMap))
                { ti.maxTextureSize = 2048; if (isN) ti.textureType = TextureImporterType.NormalMap; ti.SaveAndReimport(); }
            }
        }
        // 2) 동작: 걷기(이름에 walk), 서기(idl) — 없으면 다른 사람 동작
        AnimationClip walk = null, idle = null;
        foreach (var p in src) { var c = FirstClip(p.fbx); if (c == null) continue; string n = Path.GetFileName(p.fbx).ToLower(); if (n.Contains("walk") && walk == null) walk = c; else if (n.Contains("idl") && idle == null) idle = c; }
        if (walk == null) foreach (var p in src) { var c = FirstClip(p.fbx); if (c != null) { walk = c; break; } }
        if (idle == null) idle = walk;
        if (walk == null) { Debug.LogWarning("[PNU Drive] 사람 동작 없음(걷기 FBX 필요)"); }
        string ctlPath = PeopleDir + "PNU_Ped.controller";
        UnityEditor.Animations.AnimatorController ctl = null;
        if (walk != null)
        {
            AssetDatabase.DeleteAsset(ctlPath);
            ctl = UnityEditor.Animations.AnimatorController.CreateAnimatorControllerAtPath(ctlPath);
            ctl.AddParameter("Speed", AnimatorControllerParameterType.Float);
            var sm = ctl.layers[0].stateMachine;
            var sIdle = sm.AddState("Idle"); sIdle.motion = idle;
            var sWalk = sm.AddState("Walk"); sWalk.motion = walk;
            sm.defaultState = sIdle;
            var t1 = sIdle.AddTransition(sWalk); t1.hasExitTime = false; t1.duration = 0.2f; t1.AddCondition(UnityEditor.Animations.AnimatorConditionMode.Greater, 0.25f, "Speed");
            var t2 = sWalk.AddTransition(sIdle); t2.hasExitTime = false; t2.duration = 0.25f; t2.AddCondition(UnityEditor.Animations.AnimatorConditionMode.Less, 0.15f, "Speed");
            EditorUtility.SetDirty(ctl);
        }
        float walkRef = walk != null && walk.averageSpeed.magnitude > 0.5f ? walk.averageSpeed.magnitude : 1.35f;
        // 3) 재질·프리팹
        EnsureFolder(PrefabDir + "People"); EnsureFolder(MatDir + "People");
        var sh = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
        var outp = new List<PNUDrivePerson>(); var rep = new List<string>();
        foreach (var p in src)
        {
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(p.fbx); if (model == null) continue;
            string nm = Path.GetFileName(p.dir);
            var mats = new Material[2];
            for (int f = 0; f < 2; f++)
            {
                string path = MatDir + "People/PD_ped_" + nm + (f == 1 ? "_fade" : "") + ".mat";
                var m = AssetDatabase.LoadAssetAtPath<Material>(path);
                if (m == null) { m = new Material(sh); AssetDatabase.CreateAsset(m, path); }
                m.shader = sh;
                var dt = p.dif != null ? AssetDatabase.LoadAssetAtPath<Texture2D>(p.dif) : null;
                if (dt != null) { m.SetTexture("_BaseMap", dt); m.SetTexture("_MainTex", dt); }
                var nt = p.norm != null ? AssetDatabase.LoadAssetAtPath<Texture2D>(p.norm) : null;
                if (nt != null) { m.SetTexture("_BumpMap", nt); m.EnableKeyword("_NORMALMAP"); }
                m.SetColor("_BaseColor", Color.white); m.SetFloat("_Smoothness", 0.2f); m.SetFloat("_Metallic", 0f);
                if (f == 1) TeslaTransparent(m, 1f);
                EditorUtility.SetDirty(m); mats[f] = m;
            }
            var root = new GameObject("PED_" + nm);
            var inst = (GameObject)PrefabUtility.InstantiatePrefab(model);
            PrefabUtility.UnpackPrefabInstance(inst, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
            inst.transform.SetParent(root.transform, false); inst.name = "model";
            foreach (var c in root.GetComponentsInChildren<Collider>(true)) UnityEngine.Object.DestroyImmediate(c);
            var anim = inst.GetComponentInChildren<Animator>(true); if (anim == null) anim = inst.AddComponent<Animator>();
            if (anim.avatar == null) { var av = AssetDatabase.LoadAllAssetsAtPath(p.fbx).OfType<Avatar>().FirstOrDefault(); if (av != null) anim.avatar = av; }
            anim.runtimeAnimatorController = ctl; anim.applyRootMotion = false; anim.cullingMode = AnimatorCullingMode.CullUpdateTransforms;
            var rends = root.GetComponentsInChildren<Renderer>(true);
            foreach (var r in rends) { var sm2 = r.sharedMaterials; for (int i = 0; i < sm2.Length; i++) sm2[i] = mats[0]; r.sharedMaterials = sm2; r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On; }
            // 크기(키 1.4~2.1m 가 아니면 맞춤)·앞(왼발이 -X 쪽이면 +Z 를 봄)
            var bb = new Bounds(); bool hasB = false; foreach (var r in rends) { if (!hasB) { bb = r.bounds; hasB = true; } else bb.Encapsulate(r.bounds); }
            float hgt = hasB ? bb.size.y : 1.7f, k = 1f;
            if (hgt > 0.1f && (hgt < 1.4f || hgt > 2.1f)) { k = 1.72f / hgt; inst.transform.localScale *= k; }
            float turn = 0f;
            if (anim.avatar != null && anim.avatar.isHuman)
            {
                var lf = anim.GetBoneTransform(HumanBodyBones.LeftFoot); var rf = anim.GetBoneTransform(HumanBodyBones.RightFoot);
                if (lf != null && rf != null && lf.position.x > rf.position.x + 0.02f) { turn = 180f; inst.transform.Rotate(0f, 180f, 0f, Space.World); }
            }
            // 멀면 안 그림(사람 모델은 줄인 모양이 없음): 화면 높이 1.5% 아래(VR 에서 60~80m 밖)
            var plg = root.AddComponent<LODGroup>(); plg.SetLODs(new[] { new LOD(0.015f, rends) }); plg.RecalculateBounds();
            var per = root.AddComponent<PNUDrivePerson>();
            per.anim = anim; per.rends = rends; per.opaqueMats = rends.Select(r => mats[0]).ToArray(); per.fadeMats = rends.Select(r => mats[1]).ToArray(); per.walkRef = walkRef;
            rep.Add(nm + " 키" + (hgt * k).ToString("F2") + "m" + (k != 1f ? "(×" + k.ToString("F3") + ")" : "") + (turn != 0f ? " 돌림180°" : "") + " 사람뼈대 " + (anim.avatar != null && anim.avatar.isHuman ? "O" : "X") + " 렌더러 " + rends.Length);
            var pf = PrefabUtility.SaveAsPrefabAsset(root, PrefabDir + "People/PED_" + nm + ".prefab");
            UnityEngine.Object.DestroyImmediate(root);
            if (pf != null) outp.Add(pf.GetComponent<PNUDrivePerson>());
        }
        AssetDatabase.SaveAssets();
        Debug.Log("[PNU Drive] 사람 프리팹 " + outp.Count + "/" + src.Length + " (걷기 " + (walk != null ? walk.name : "없음") + ", 서기 " + (idle != null ? idle.name : "없음") + ", 걷기 기준 " + walkRef.ToString("F2") + "m/s): " + string.Join(" · ", rep));
        return outp.ToArray();
    }

    // ---------- 저작자 표시(CC-BY 는 꼭)
    static void WriteCredits()
    {
        string txt =
            "PNU Drive — NPC 차·사람 에셋 출처 (v14)\n\n" +
            "차\n" +
            "- \"Traffic Car Pack\" — MaG80 (Sketchfab), CC BY 4.0. 재질을 팔레트 한 장으로 바꾸고 크기·원점·등 면을 나눔(PNU_Vehicles.fbx 의 VEH_M00~M10)\n" +
            "- \"Free Low Poly Vehicles Pack\" — Raphael Gonçalves (Rgsdev), CC0. 같은 방식으로 바꿈(VEH_R_*)\n\n" +
            "사람 (Renderpeople, Sketchfab, CC BY 4.0, NoAI: 생성형 AI 학습·입력에 쓰면 안 됨)\n" +
            "- \"Nathan Animated 003 - Walking 3D Man\" — Renderpeople — https://sketchfab.com/3d-models/nathan-animated-003-walking-3d-man-143a2b1ea5eb4385ae90a73657aca3bc\n" +
            "- \"Sophia Animated 003 - Animated 3D Woman\" — Renderpeople — https://sketchfab.com/3d-models/sophia-animated-003-animated-3d-woman-dc448c3be0e74f96a55fb475a13433cf\n" +
            "- \"Claudia Rigged 002 - 3D Rigged Business Women\" — Renderpeople (Sketchfab)\n" +
            "  유니티에서 Humanoid 로 가져와 걷기(Nathan)·서기(Sophia) 동작을 함께 씀. 모델 파일은 바꾸지 않음\n\n" +
            "CC BY 4.0: https://creativecommons.org/licenses/by/4.0/  ·  CC0: https://creativecommons.org/publicdomain/zero/1.0/\n\n" +
            "글꼴(5/6 화면)\n" +
            "- 나눔바른고딕 NanumBarunGothic · NanumBarunGothicBold — NAVER Corporation, SIL Open Font License 1.1 (Fonts/OFL_NanumBarunGothic.txt). 원본 파일 그대로\n" +
            "아이콘·UI 그림(UI/)은 코드로 직접 그림(cw: ui_assets.py)\n";
        try { File.WriteAllText(ROOT + "CREDITS_NPC.txt", txt); AssetDatabase.ImportAsset(ROOT + "CREDITS_NPC.txt"); } catch (Exception e) { Debug.LogWarning("[PNU Drive] 출처 파일 실패: " + e.Message); }
    }

    // ------------------------------------------------------------ 5/6 스토리보드: 흐름(PNUDriveScenario)·월드 UI(PNUDriveHud)·매핑(PNUDriveOverlay)·비(PNUDriveWeather)·sim_1~8 음성
    const string UiDir = ROOT + "UI/", FontDir = ROOT + "Fonts/", StoryDir = ROOT + "Audio/Storyboard/";
    static readonly string[] StoryText = {
        "운전석 1인칭 시점으로 전환됩니다. 멀미를 줄이기 위해 카메라 이동은 최소화하고 차량 진행 방향을 안정적으로 유지합니다.",
        "지도에서 목적지를 선택하면 권장 경로와 예상 주행 정보가 표시됩니다.",
        "차선, 주변 차량, 보행자와 장애물의 위치가 실제 환경 위에 중첩되어 표시됩니다.",
        "교차로에 진입합니다. 차량은 신호와 주변 객체를 확인한 뒤 통과 시퀀스를 진행합니다.",
        "전방 차량이 정차하면 후측방을 확인합니다. 안전 거리가 부족하면 차선 변경을 보류하고 현재 차선에서 감속합니다.",
        "사각지대에서 보행자가 감지되었습니다. 차량은 경고 후 감속하며 위험이 지속되면 자동으로 정지합니다.",
        "악천후에서는 카메라 인식 성능이 낮아질 수 있습니다. 레이더의 거리와 속도 정보를 비교해 보완 과정을 확인하세요.",
        "목적지에 도착했습니다. 주행 시간과 위험 상황 대응 결과를 확인하고 시뮬레이션을 마무리합니다." };

    static Texture2D UiTex(string name) { return AssetDatabase.LoadAssetAtPath<Texture2D>(UiDir + name + ".png"); }

    /// <summary>UI 그림 가져오기 설정: 둥근 판·아이콘 = 밉맵(멀리서 안 깨짐)·압축 없음, 차로 띠 = 되풀이</summary>
    static void UiImport()
    {
        if (!AssetDatabase.IsValidFolder(UiDir.TrimEnd('/'))) return;
        foreach (var f in Directory.GetFiles(UiDir, "*.png"))
        {
            var p = f.Replace('\\', '/'); var ti = AssetImporter.GetAtPath(p) as TextureImporter; if (ti == null) continue;
            bool band = p.Contains("tx_lane_band") || p.Contains("tx_glass_drops");
            bool ch = ti.textureType != TextureImporterType.Default || !ti.alphaIsTransparency || ti.textureCompression != TextureImporterCompression.Uncompressed || !ti.mipmapEnabled
                      || ti.wrapMode != (band ? TextureWrapMode.Repeat : TextureWrapMode.Clamp) || ti.npotScale != TextureImporterNPOTScale.None;
            if (!ch) continue;
            ti.textureType = TextureImporterType.Default; ti.alphaIsTransparency = true; ti.alphaSource = TextureImporterAlphaSource.FromInput;
            ti.textureCompression = TextureImporterCompression.Uncompressed; ti.mipmapEnabled = true; ti.mipmapFilter = TextureImporterMipFilter.KaiserFilter;
            ti.wrapMode = band ? TextureWrapMode.Repeat : TextureWrapMode.Clamp; ti.filterMode = FilterMode.Trilinear; ti.anisoLevel = band ? 8 : 2; ti.npotScale = TextureImporterNPOTScale.None;
            ti.SaveAndReimport();
        }
    }

    // ------------------------------------------------------------ v17 비 막는 구역(터널·지하차도): 경로 구간마다 상자(입자 충돌·안 판정)
    [Serializable] class RbZone { public float s0, s1; public string kind; }
    [Serializable] class RbPath { public float[] x, y, z; public RbZone[] zones; }
    static PNUDriveRainBlock BuildRainBlockers(PNUDriveEgo ego)
    {
        if (ego == null || ego.pathJson == null) return null;
        var P = JsonUtility.FromJson<RbPath>(ego.pathJson.text);
        if (P == null || P.x == null || P.zones == null || P.x.Length < 2) return null;
        int n = P.x.Length; var S = new float[n];
        for (int i = 1; i < n; i++) S[i] = S[i - 1] + Mathf.Sqrt((P.x[i] - P.x[i - 1]) * (P.x[i] - P.x[i - 1]) + (P.z[i] - P.z[i - 1]) * (P.z[i] - P.z[i - 1]));
        Func<float, Vector3> at = s =>
        {
            int k = Array.BinarySearch(S, s); if (k < 0) k = ~k; k = Mathf.Clamp(k, 1, n - 1);
            float t = Mathf.InverseLerp(S[k - 1], S[k], s);
            return new Vector3(Mathf.Lerp(P.x[k - 1], P.x[k], t), Mathf.Lerp(P.y[k - 1], P.y[k], t), Mathf.Lerp(P.z[k - 1], P.z[k], t));
        };
        var root = new GameObject("비 막는 구역 (터널·지하차도)"); root.layer = PNUDriveRainBlock.Layer;
        var rb = root.AddComponent<PNUDriveRainBlock>();
        var boxes = new List<BoxCollider>(); var zoneOf = new List<int>(); var pa = new List<Vector3>(); var pb = new List<Vector3>(); var names = new List<string>();
        const float Width = 26f, Below = 1.5f, Above = 7.5f, Step = 20f;
        foreach (var z in P.zones)
        {
            if (z == null || z.kind == "bridge" || z.s1 - z.s0 < 2f) continue;
            int zi = pa.Count; string nm = (z.kind == "tunnel" ? "터널 " : "지하차도·덮인 길 ") + Mathf.RoundToInt(z.s0) + "~" + Mathf.RoundToInt(z.s1) + "m";
            pa.Add(at(z.s0)); pb.Add(at(z.s1)); names.Add(nm);
            int segs = Mathf.Max(1, Mathf.CeilToInt((z.s1 - z.s0) / Step));
            for (int k = 0; k < segs; k++)
            {
                float a = Mathf.Lerp(z.s0, z.s1, (float)k / segs), b = Mathf.Lerp(z.s0, z.s1, (float)(k + 1) / segs);
                Vector3 A = at(a), B = at(b), d = B - A; if (d.sqrMagnitude < 1e-4f) continue;
                var g = new GameObject(nm + " #" + (k + 1)); g.layer = PNUDriveRainBlock.Layer; g.transform.SetParent(root.transform, false);
                g.transform.SetPositionAndRotation((A + B) * 0.5f, Quaternion.LookRotation(d.normalized, Vector3.up));
                var bc = g.AddComponent<BoxCollider>(); bc.isTrigger = false;
                // 끝 상자는 입구·출구에 딱 맞게, 가운데 상자끼리는 0.5m 씩 겹침
                float ext = (k > 0 ? 0.5f : 0f) + (k < segs - 1 ? 0.5f : 0f), shift = ((k < segs - 1 ? 0.5f : 0f) - (k > 0 ? 0.5f : 0f)) * 0.5f;
                bc.size = new Vector3(Width, Below + Above, d.magnitude + ext); bc.center = new Vector3(0f, (Above - Below) * 0.5f, shift);
                boxes.Add(bc); zoneOf.Add(zi);
            }
        }
        rb.boxes = boxes.ToArray(); rb.zoneOf = zoneOf.ToArray(); rb.portalA = pa.ToArray(); rb.portalB = pb.ToArray(); rb.zoneName = names.ToArray();
        Debug.Log("[PNU Drive] 비 막는 구역(v17): " + names.Count + "곳, 상자 " + boxes.Count + "개 (" + string.Join(", ", names) + ") — 폭 " + Width + "m, 노면 위 " + Above + "m");
        if (boxes.Count == 0) { UnityEngine.Object.DestroyImmediate(root); return null; }
        return rb;
    }

    static void BuildScenario(PNUDriveEgo ego, Camera cam, Material[] mats)
    {
        var ta = AssetDatabase.LoadAssetAtPath<TextAsset>(ROOT + "Data/scenario.json");
        if (ta == null) { Debug.LogWarning("[PNU Drive] 스토리보드 건너뜀: Data/scenario.json 없음"); return; }
        UiImport();
        var go = new GameObject("PNU 스토리보드 (5/6)");
        var sc = go.AddComponent<PNUDriveScenario>(); sc.scenarioJson = ta; sc.ego = ego;
        sc.nav = ego.GetComponent<PNUDriveNav>(); sc.sound = ego.GetComponent<PNUDriveSound>(); sc.traffic = UnityEngine.Object.FindObjectOfType<PNUDriveTraffic>();
        var fr = AssetDatabase.LoadAssetAtPath<Font>(FontDir + "NanumBarunGothic.ttf"); var fb = AssetDatabase.LoadAssetAtPath<Font>(FontDir + "NanumBarunGothicBold.ttf");
        var hud = go.AddComponent<PNUDriveHud>(); hud.cam = cam; hud.ego = ego; hud.fontRegular = fr; hud.fontBold = fb != null ? fb : fr;
        hud.texRound = UiTex("ui_round"); hud.texRoundS = UiTex("ui_round_s"); hud.texRing = UiTex("ui_ring"); hud.texPill = UiTex("ui_pill"); hud.texShadow = UiTex("ui_shadow");
        var ic = Directory.Exists(UiDir) ? Directory.GetFiles(UiDir, "ic_*.png").Select(f => f.Replace('\\', '/')).OrderBy(f => f).ToArray() : new string[0];
        hud.iconTex = ic.Select(f => AssetDatabase.LoadAssetAtPath<Texture2D>(f)).ToArray(); hud.iconNames = ic.Select(f => Path.GetFileNameWithoutExtension(f).Substring(3)).ToArray();
        var ov = go.AddComponent<PNUDriveOverlay>(); ov.cam = cam; ov.ego = ego; ov.traffic = sc.traffic; ov.font = fr; ov.fontBold = hud.fontBold; ov.bandTex = UiTex("tx_lane_band"); ov.ringTex = UiTex("tx_ring");
        var we = go.AddComponent<PNUDriveWeather>(); we.cam = cam; we.ego = ego; we.streakTex = UiTex("tx_rain_streak"); we.dropsTex = UiTex("tx_glass_drops"); we.sun = RenderSettings.sun;
        if (mats != null && mats.Length > 21) we.wetMats = new[] { mats[0], mats[1], mats[2], mats[3], mats[4], mats[5], mats[12], mats[21] };
        try { we.blockers = BuildRainBlockers(ego); } catch (Exception e) { Debug.LogWarning("[PNU Drive] 비 막는 구역 건너뜀: " + e.Message); }
        sc.hud = hud; sc.overlay = ov; sc.weather = we;
        // 마우스로 누르기(월드 캔버스): EventSystem
        if (UnityEngine.Object.FindObjectOfType<UnityEngine.EventSystems.EventSystem>() == null)
        {
            var es = new GameObject("EventSystem", typeof(UnityEngine.EventSystems.EventSystem));
#if !ENABLE_INPUT_SYSTEM || ENABLE_LEGACY_INPUT_MANAGER
            es.AddComponent<UnityEngine.EventSystems.StandaloneInputModule>();
#else
            Debug.LogWarning("[PNU Drive] 새 Input System 만 켜져 있어 UI 입력 모듈을 직접 넣어 주세요(InputSystemUIInputModule)");
#endif
        }
        MakeStoryVoices(sc, false);
        Debug.Log("[PNU Drive] 스토리보드(v15): 글꼴 " + (fr != null ? fr.name : "없음") + "/" + (fb != null ? fb.name : "없음") + ", 아이콘 " + hud.iconTex.Count(t => t != null) + "/" + ic.Length +
                  ", 띠 " + (ov.bandTex != null) + ", 비 " + (we.streakTex != null) + "/" + (we.dropsTex != null) + ", 젖는 재질 " + (we.wetMats != null ? we.wetMats.Count(m => m != null) : 0) + ", 해 " + (we.sun != null) +
                  ", 음성 " + sc.storyClips.Count(c => c != null) + "/8, 차·사람 " + (sc.traffic != null));
    }

    /// <summary>sim_1~8: Audio/Storyboard/sim_N.(mp3|wav|ogg) 가 있으면 그것(유디포엠 녹음), 없으면 윈도 음성으로 만듦</summary>
    static void MakeStoryVoices(PNUDriveScenario sc, bool force)
    {
        EnsureFolder(StoryDir.TrimEnd('/'));
        var todo = new List<KeyValuePair<string, string>>(); var files = new string[8];
        for (int i = 0; i < 8; i++)
        {
            string key = "sim_" + (i + 1), given = null;
            foreach (var ext in new[] { ".mp3", ".wav", ".ogg" }) if (File.Exists(StoryDir + key + ext)) { given = StoryDir + key + ext; break; }
            if (given != null) { files[i] = given; continue; }
            string f = StoryDir + "tts_" + key + "_" + Hash8(StoryText[i]) + ".wav"; files[i] = f;
            if (force || !File.Exists(f)) todo.Add(new KeyValuePair<string, string>(Path.GetFullPath(f), StoryText[i]));
        }
        if (todo.Count > 0) { Speak(todo, "스토리보드 음성(sim)"); AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport); }
        var keys = new List<string>(); var clips = new List<AudioClip>(); int given2 = 0;
        for (int i = 0; i < 8; i++)
        {
            var c = File.Exists(files[i]) && new FileInfo(files[i]).Length > 1000 ? AssetDatabase.LoadAssetAtPath<AudioClip>(files[i]) : null;
            if (c == null) continue;
            keys.Add("sim_" + (i + 1)); clips.Add(c); if (!Path.GetFileName(files[i]).StartsWith("tts_")) given2++;
        }
        sc.storyKeys = keys.ToArray(); sc.storyClips = clips.ToArray(); EditorUtility.SetDirty(sc);
        Debug.Log("[PNU Drive] 스토리보드 음성 " + clips.Count + "/8 연결(녹음 파일 " + given2 + ", 윈도 음성 " + (clips.Count - given2) + ") — 녹음본은 " + StoryDir + "sim_1.mp3 … 로 넣으면 다음 빌드부터 그것을 씀");
    }

    [MenuItem("PNU Drive/스토리보드 음성 다시 만들기")]
    static void RemakeStoryVoices()
    {
        var sc = UnityEngine.Object.FindObjectOfType<PNUDriveScenario>();
        if (sc == null) { Debug.LogWarning("[PNU Drive] 주행 씬을 열고 다시 해 주세요"); return; }
        MakeStoryVoices(sc, true); EditorSceneManager.MarkSceneDirty(sc.gameObject.scene); EditorSceneManager.SaveScene(sc.gameObject.scene);
    }

    public static void EnsureFolder(string path)
    {
        path = path.TrimEnd('/'); if (AssetDatabase.IsValidFolder(path)) return;
        var parent = Path.GetDirectoryName(path).Replace('\\', '/'); EnsureFolder(parent);
        AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
    }
}
