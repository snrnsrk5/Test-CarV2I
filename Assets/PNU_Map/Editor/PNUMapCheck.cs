// 맵 방향·면 확인용 1회 실행 도구
// - 에디터가 스크립트를 컴파일하면 Assets/PNU_Map/Editor/run_check.txt 가 있을 때 한 번 실행되고 그 파일을 지움
// - 메뉴: PNU Map > Check Section A (직접 다시 실행)
// 결과: 프로젝트 폴더/Captures/check_*.png, Captures/check_report.txt
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

[InitializeOnLoad]
public static class PNUMapCheck
{
    const string Trigger = "Assets/PNU_Map/Editor/run_check.txt";
    static readonly Vector3 EgoStart = new Vector3(-3476.58f, 28.17f, 4992.88f);   // map_data.json ego.pos[0]
    static readonly Vector3 EgoAt200 = new Vector3(-3432.36f, 29.5f, 4856.47f);

    static PNUMapCheck()
    {
        EditorApplication.delayCall += () =>
        {
            if (File.Exists(Trigger))
            {
                AssetDatabase.DeleteAsset(Trigger);
                Run();
            }
        };
    }

    static Material MakeMat(string name, Color c, string tex = null, bool clip = false)
    {
        if (!AssetDatabase.IsValidFolder("Assets/PNU_Map/Materials")) AssetDatabase.CreateFolder("Assets/PNU_Map", "Materials");
        string path = "Assets/PNU_Map/Materials/" + name + ".mat";
        var m = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (m == null)
        {
            var sh = Shader.Find("Universal Render Pipeline/Lit");
            if (sh == null) sh = Shader.Find("Standard");
            m = new Material(sh);
            AssetDatabase.CreateAsset(m, path);
        }
        m.SetColor("_BaseColor", c);
        m.color = c;
        if (tex != null)
        {
            var t = AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/PNU_Map/Textures/" + tex);
            if (t != null) { m.SetTexture("_BaseMap", t); m.mainTexture = t; }
        }
        if (clip)
        {
            m.SetFloat("_AlphaClip", 1f); m.SetFloat("_Cutoff", 0.4f);
            m.EnableKeyword("_ALPHATEST_ON");
        }
        m.SetFloat("_Smoothness", 0.15f);
        EditorUtility.SetDirty(m);
        return m;
    }

    static Material MatFor(string fbxMatName)
    {
        string n = fbxMatName.Replace(" (Instance)", "");
        if (n.StartsWith("Asphalt")) return MakeMat("Asphalt", new Color(0.75f, 0.75f, 0.75f), "Asphalt_Base.png");
        if (n.StartsWith("Pavers")) return MakeMat("Pavers", new Color(0.92f, 0.89f, 0.84f), "Pavers_1.jpg");
        if (n.StartsWith("Curb")) return MakeMat("Curb", new Color(0.81f, 0.80f, 0.77f));
        if (n.StartsWith("Concrete_Cap")) return MakeMat("Concrete_Cap", new Color(0.72f, 0.71f, 0.69f));
        if (n.StartsWith("Concrete")) return MakeMat("Concrete", new Color(0.76f, 0.76f, 0.74f));
        if (n.StartsWith("Marking_Yellow")) return MakeMat("Marking_Yellow", new Color(0.96f, 0.74f, 0.14f));
        if (n.StartsWith("Marking_Blue")) return MakeMat("Marking_Blue", new Color(0.25f, 0.45f, 0.9f));
        if (n.StartsWith("Marking")) return MakeMat("Marking_White", new Color(0.95f, 0.95f, 0.93f));
        if (n.StartsWith("RoadMarking_Atlas")) return MakeMat("RoadMarking_Atlas", Color.white, "Road_Marking_I.png", true);
        if (n.StartsWith("TunnelLight")) return MakeMat("TunnelLight", new Color(1f, 0.9f, 0.7f));
        if (n.StartsWith("Terrain")) return MakeMat("Terrain_Landcover", Color.white, "Terrain_Landcover.jpg");
        if (n.StartsWith("Building_Roof")) return MakeMat("Building_Roof", new Color(0.70f, 0.69f, 0.66f));
        if (n.StartsWith("Building")) return MakeMat("Building_Wall", new Color(0.86f, 0.84f, 0.80f));
        if (n.StartsWith("Water")) return MakeMat("Water", new Color(0.37f, 0.58f, 0.73f));
        return null;
    }

    static GameObject Place(string fbx, Transform parent)
    {
        var src = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/PNU_Map/Map/" + fbx + ".fbx");
        if (src == null) { Debug.LogWarning("PNUMapCheck: 못 찾음 " + fbx); return null; }
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
        }
        return go;
    }

    static void Capture(Camera cam, string file, int w = 1600, int h = 900)
    {
        var rt = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32);
        cam.targetTexture = rt;
        cam.Render();
        var prev = RenderTexture.active;
        RenderTexture.active = rt;
        var tex = new Texture2D(w, h, TextureFormat.RGB24, false);
        tex.ReadPixels(new Rect(0, 0, w, h), 0, 0);
        tex.Apply();
        RenderTexture.active = prev;
        cam.targetTexture = null;
        Directory.CreateDirectory("Captures");
        File.WriteAllBytes("Captures/" + file, tex.EncodeToPNG());
        Object.DestroyImmediate(tex);
        rt.Release(); Object.DestroyImmediate(rt);
    }

    [MenuItem("PNU Map/Check Section A")]
    public static void Run()
    {
        var sb = new StringBuilder();
        var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);
        var root = new GameObject("PNU_Map").transform;
        var a = Place("Section_A_Campus", root);
        var b = Place("Buildings_A_Campus", root);
        // 자차 출발 표시(빨간 기둥) - 도로 위에 서 있으면 좌표가 맞음
        var marker = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        marker.name = "EgoStart_Marker"; marker.transform.position = EgoStart + Vector3.up * 3f; marker.transform.localScale = new Vector3(1.2f, 3f, 1.2f);
        marker.GetComponent<MeshRenderer>().sharedMaterial = MakeMat("Marker_Red", new Color(0.9f, 0.15f, 0.1f));
        var marker2 = GameObject.CreatePrimitive(PrimitiveType.Cube);
        marker2.name = "Ego_200m_Marker"; marker2.transform.position = EgoAt200 + Vector3.up * 2f; marker2.transform.localScale = new Vector3(2f, 4f, 2f);
        marker2.GetComponent<MeshRenderer>().sharedMaterial = MakeMat("Marker_Blue", new Color(0.1f, 0.3f, 0.95f));

        var bounds = new Bounds(EgoStart, Vector3.zero); bool first = true;
        if (a != null)
            foreach (var r in a.GetComponentsInChildren<Renderer>()) { if (first) { bounds = r.bounds; first = false; } else bounds.Encapsulate(r.bounds); }
        sb.AppendLine("Section_A bounds min " + bounds.min + " max " + bounds.max);
        sb.AppendLine("기대값: X -3810~-2830, Z 4410~5430 (다르면 축이 다름)");
        sb.AppendLine("EgoStart inside XZ: " + (EgoStart.x > bounds.min.x && EgoStart.x < bounds.max.x && EgoStart.z > bounds.min.z && EgoStart.z < bounds.max.z));
        // 출발점 아래 도로 높이 확인
        if (Physics.Raycast(EgoStart + Vector3.up * 200f, Vector3.down, out var hit, 500f))
            sb.AppendLine("Raycast at ego start hit " + hit.collider.name + " y=" + hit.point.y);
        else sb.AppendLine("Raycast: 콜라이더 없음(정상: FBX에 콜라이더 미생성)");

        var cam = Camera.main;
        if (cam == null) cam = new GameObject("Main Camera").AddComponent<Camera>();
        cam.nearClipPlane = 0.3f; cam.farClipPlane = 20000f; cam.fieldOfView = 55f;
        var sun = Object.FindObjectOfType<Light>();
        if (sun != null) { sun.transform.rotation = Quaternion.Euler(50f, -30f, 0f); sun.intensity = 1.2f; }
        RenderSettings.ambientMode = AmbientMode.Flat; RenderSettings.ambientLight = new Color(0.55f, 0.57f, 0.6f);

        // 1) 위에서 넓게
        cam.transform.position = bounds.center + new Vector3(0, 900f, -650f);
        cam.transform.LookAt(bounds.center);
        Capture(cam, "check_A_overview.png");
        // 2) 출발점 뒤쪽 낮게(자차 시점 비슷)
        Vector3 dir = (EgoAt200 - EgoStart); dir.y = 0; dir.Normalize();
        cam.transform.position = EgoStart - dir * 18f + Vector3.up * 7f;
        cam.transform.LookAt(EgoStart + dir * 25f + Vector3.up * 1.5f);
        Capture(cam, "check_A_follow.png");
        // 3) 아래에서 위로(면 뒤집힘 확인용)
        cam.transform.position = EgoStart + dir * 10f + Vector3.up * 1.2f + Vector3.Cross(Vector3.up, dir) * 12f;
        cam.transform.LookAt(EgoStart + Vector3.up * 1f);
        Capture(cam, "check_A_side.png");

        if (!AssetDatabase.IsValidFolder("Assets/PNU_Map/Scenes")) AssetDatabase.CreateFolder("Assets/PNU_Map", "Scenes");
        EditorSceneManager.SaveScene(scene, "Assets/PNU_Map/Scenes/MapCheck.unity");
        File.WriteAllText("Captures/check_report.txt", sb.ToString(), Encoding.UTF8);
        AssetDatabase.SaveAssets();
        Debug.Log("PNUMapCheck 완료: Captures 폴더 확인\n" + sb);
        Selection.activeGameObject = marker;
        if (SceneView.lastActiveSceneView != null) SceneView.lastActiveSceneView.FrameSelected();
    }
}
