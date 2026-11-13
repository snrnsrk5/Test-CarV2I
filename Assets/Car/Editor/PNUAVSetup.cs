// PNU01 설치: 메뉴 PNU Map → PNU01 → Build Prefab
// 재질(URP Lit) 만들기 → FBX 재질 연결 → 프리팹(PNU01.prefab, PNUAVCar + PNUAVSensorFX) 만들기
// 로고·번호판은 Textures/PNU01_Logo.png, PNU01_Plate.png 를 바꾸고 메뉴를 다시 실행하면 앞·뒤 모두 바뀜
using System.IO;
using UnityEditor;
using UnityEngine;

public static class PNUAVSetup
{
    const string Root = "Assets/PNU_AV01";
    const string Fbx = Root + "/Models/PNU01.fbx";
    const string MatJson = Root + "/Data/pnu01_materials.json";
    const string DataJson = Root + "/Data/pnu01_data.json";
    const string TexDir = Root + "/Textures/";
    const string MatDir = Root + "/Materials";
    const string PrefabPath = Root + "/PNU01.prefab";

    [System.Serializable] class MatDef { public string name; public float[] color; public float metallic; public float smoothness; public float alpha; public float[] emission; public float emissionIntensity; public string texture; public bool cutout; public bool doubleSided; }
    [System.Serializable] class MatList { public MatDef[] materials; }
    [System.Serializable] class CarData { public float wheelRadius; public float wheelBase; public float[] steeringAxis; public int defaultWheelStyle; }

    [MenuItem("PNU Map/PNU01/Build Prefab")]
    public static void Build()
    {
        if (!File.Exists(Fbx)) { Debug.LogError("[PNU01] FBX 없음: " + Fbx); return; }
        if (!AssetDatabase.IsValidFolder(MatDir)) AssetDatabase.CreateFolder(Root, "Materials");
        var mats = JsonUtility.FromJson<MatList>(File.ReadAllText(MatJson));
        Shader lit = Shader.Find("Universal Render Pipeline/Lit");
        bool urp = lit != null;
        if (!urp) lit = Shader.Find("Standard");

        var imp = (ModelImporter)AssetImporter.GetAtPath(Fbx);
        imp.globalScale = 1f; imp.useFileScale = true;
        imp.bakeAxisConversion = true;   // 블렌더 축 → 유니티 축(루트 회전 없이)
        imp.importCameras = false; imp.importLights = false; imp.importAnimation = false;
        imp.materialImportMode = ModelImporterMaterialImportMode.ImportStandard;
        imp.isReadable = false;
        int n = 0;
        foreach (var d in mats.materials)
        {
            string path = MatDir + "/" + d.name + ".mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null) { m = new Material(lit); AssetDatabase.CreateAsset(m, path); }
            else m.shader = lit;
            Texture2D tex = string.IsNullOrEmpty(d.texture) ? null : AssetDatabase.LoadAssetAtPath<Texture2D>(TexDir + d.texture);
            if (!string.IsNullOrEmpty(d.texture) && tex == null) Debug.LogWarning("[PNU01] 텍스처 없음: " + TexDir + d.texture);
            Setup(m, d, urp, tex);
            EditorUtility.SetDirty(m);
            imp.AddRemap(new AssetImporter.SourceAssetIdentifier(typeof(Material), d.name), m);
            n++;
        }
        imp.SaveAndReimport();

        var model = AssetDatabase.LoadAssetAtPath<GameObject>(Fbx);
        var go = (GameObject)PrefabUtility.InstantiatePrefab(model);
        go.name = "PNU01";
        var car = go.AddComponent<PNUAVCar>();
        var data = AssetDatabase.LoadAssetAtPath<TextAsset>(DataJson);
        if (data)
        {
            var cd = JsonUtility.FromJson<CarData>(data.text);
            if (cd.wheelRadius > 0) car.wheelRadius = cd.wheelRadius;
            if (cd.wheelBase > 0) car.wheelBase = cd.wheelBase;
            if (cd.steeringAxis != null && cd.steeringAxis.Length == 3)
                car.steeringAxis = new Vector3(cd.steeringAxis[0], cd.steeringAxis[1], cd.steeringAxis[2]);
            car.wheelStyle = (PNUAVCar.WheelStyle)Mathf.Clamp(cd.defaultWheelStyle, 0, 3);
        }
        var fx = go.AddComponent<PNUAVSensorFX>();
        fx.data = data;
        fx.enabled = false;   // 설명 장면에서 켜기(단축키 8)
        car.Apply();
        PrefabUtility.SaveAsPrefabAsset(go, PrefabPath);
        Object.DestroyImmediate(go);
        AssetDatabase.SaveAssets();
        Debug.Log($"[PNU01] 재질 {n}개, 프리팹 {PrefabPath} 완료 (URP={urp})");
        Selection.activeObject = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
    }

    static Color C(float[] a, float alpha = 1f) { return a != null && a.Length >= 3 ? new Color(a[0], a[1], a[2], alpha) : new Color(1, 1, 1, alpha); }

    static void Setup(Material m, MatDef d, bool urp, Texture2D tex)
    {
        Color baseC = C(d.color, d.alpha);
        bool transparent = d.alpha < 0.99f;
        if (urp)
        {
            m.SetColor("_BaseColor", baseC);
            m.SetFloat("_Metallic", d.metallic);
            m.SetFloat("_Smoothness", d.smoothness);
            m.SetTexture("_BaseMap", tex);
            if (tex) m.SetColor("_BaseColor", new Color(1, 1, 1, d.alpha));
            if (transparent)
            {
                m.SetFloat("_Surface", 1f); m.SetFloat("_Blend", 0f);
                m.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
                m.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                m.SetFloat("_ZWrite", 0f);
                m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                m.SetOverrideTag("RenderType", "Transparent");
                m.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
            }
            else
            {
                m.SetFloat("_Surface", 0f); m.SetFloat("_ZWrite", 1f);
                m.DisableKeyword("_SURFACE_TYPE_TRANSPARENT");
                m.SetOverrideTag("RenderType", "Opaque");
                m.renderQueue = -1;
            }
            // 로고처럼 투명 배경 텍스처: 알파 자르기
            m.SetFloat("_AlphaClip", d.cutout ? 1f : 0f);
            m.SetFloat("_Cutoff", 0.5f);
            if (d.cutout) { m.EnableKeyword("_ALPHATEST_ON"); m.renderQueue = (int)UnityEngine.Rendering.RenderQueue.AlphaTest; }
            else m.DisableKeyword("_ALPHATEST_ON");
            m.SetFloat("_Cull", d.doubleSided ? 0f : 2f);
        }
        else
        {
            m.SetColor("_Color", baseC);
            m.SetFloat("_Metallic", d.metallic);
            m.SetFloat("_Glossiness", d.smoothness);
            m.SetTexture("_MainTex", tex);
            if (tex) m.SetColor("_Color", new Color(1, 1, 1, d.alpha));
        }
        if (d.emissionIntensity > 0f)
        {
            Color e = C(d.emission) * Mathf.Max(1f, d.emissionIntensity * 0.5f);
            m.EnableKeyword("_EMISSION");
            m.SetColor("_EmissionColor", e);
            if (tex) m.SetTexture("_EmissionMap", tex);
            m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
        }
        else
        {
            m.DisableKeyword("_EMISSION");
            m.SetColor("_EmissionColor", Color.black);
        }
    }
}
