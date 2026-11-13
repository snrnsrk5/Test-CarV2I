using UnityEngine;
using UnityEngine.Rendering;
using UnityEditor;
using System.IO;
using System.Collections.Generic;
public static class RoadSignsSetup {
 // Resolve relative to this editor tool, including packages extracted under nested Assets folders.
 static string FindPackageRoot() {
  var candidates = new List<string>();
  foreach (string guid in AssetDatabase.FindAssets("RoadSignsSetup t:MonoScript")) {
   string scriptPath = AssetDatabase.GUIDToAssetPath(guid);
   if (Path.GetFileName(scriptPath) != "RoadSignsSetup.cs") continue;
   string editorFolder = Path.GetDirectoryName(scriptPath).Replace('\\','/');
   string root = Path.GetDirectoryName(editorFolder).Replace('\\','/');
   if (AssetDatabase.LoadAssetAtPath<TextAsset>(root+"/variants.json") != null &&
       AssetDatabase.LoadAssetAtPath<Texture2D>(root+"/Textures/Signs_Atlas_2K.png") != null &&
       AssetDatabase.LoadAssetAtPath<GameObject>(root+"/Models/Sign_Templates.fbx") != null)
    candidates.Add(root);
  }
  if (candidates.Count == 1) return candidates[0];
  Debug.LogError(candidates.Count == 0
   ? "Road Signs package is incomplete: keep variants.json, Models, Textures and Editor together in the same package folder. The folder can be anywhere under Assets."
   : "Multiple Road Signs packages found. Keep one complete copy under Assets.");
  return null;
 }
 [System.Serializable] class Variant {public string name; public string shape; public float[] rect;}
 [System.Serializable] class Config {public Variant[] variants;}
 [MenuItem("Tools/Optimized Road Signs/Create 10 Prefabs")]
 public static void CreatePrefabs() {
  string Root = FindPackageRoot();
  if (Root == null) return;
  var json = AssetDatabase.LoadAssetAtPath<TextAsset>(Root+"/variants.json");
  var config=JsonUtility.FromJson<Config>(json.text);
  string texturePath=Root+"/Textures/Signs_Atlas_2K.png";
  var ti=(TextureImporter)AssetImporter.GetAtPath(texturePath);
  ti.textureType=TextureImporterType.Default;ti.sRGBTexture=true;ti.alphaSource=TextureImporterAlphaSource.None;
  ti.maxTextureSize=2048;ti.mipmapEnabled=true;ti.filterMode=FilterMode.Bilinear;ti.wrapMode=TextureWrapMode.Clamp;
  ti.anisoLevel=2;ti.isReadable=false;ti.textureCompression=TextureImporterCompression.Compressed;ti.compressionQuality=50;ti.SaveAndReimport();
  string modelPath=Root+"/Models/Sign_Templates.fbx";
  var mi=(ModelImporter)AssetImporter.GetAtPath(modelPath);mi.isReadable=false;mi.importAnimation=false;
  mi.materialImportMode=ModelImporterMaterialImportMode.None;mi.generateSecondaryUV=false;
  mi.importNormals=ModelImporterNormals.Import;mi.importTangents=ModelImporterTangents.None;
  mi.optimizeMeshPolygons=true;mi.optimizeMeshVertices=true;mi.SaveAndReimport();
  bool srp=GraphicsSettings.currentRenderPipeline!=null;
  if(srp && !GraphicsSettings.currentRenderPipeline.GetType().Name.Contains("Universal")) {
   Debug.LogError("This package supports URP and Built-In. HDRP requires an equivalent instanced shader.");return;
  }
  Shader shader=Shader.Find(srp?"OptimizedRoadSigns/Atlas URP":"OptimizedRoadSigns/Atlas BuiltIn");
  if(shader==null){Debug.LogError("Sign shader not found. Wait for shader import, then retry.");return;}
  if (!AssetDatabase.IsValidFolder(Root+"/Materials")) AssetDatabase.CreateFolder(Root,"Materials");
  if (!AssetDatabase.IsValidFolder(Root+"/Prefabs")) AssetDatabase.CreateFolder(Root,"Prefabs");
  string mp=Root+"/Materials/Signs_Shared.mat";var material=AssetDatabase.LoadAssetAtPath<Material>(mp);
  if(!material){material=new Material(shader);AssetDatabase.CreateAsset(material,mp);}else material.shader=shader;
  material.SetTexture("_MainTex",AssetDatabase.LoadAssetAtPath<Texture2D>(texturePath));material.enableInstancing=true;EditorUtility.SetDirty(material);
  var model=AssetDatabase.LoadAssetAtPath<GameObject>(modelPath);var meshes=new Dictionary<string,Mesh>();
  foreach(var mf in model.GetComponentsInChildren<MeshFilter>(true)){
   foreach(string shape in new[]{"Circle","Triangle","Rectangle","Crosswalk"})
    if(mf.name==shape || mf.sharedMesh.name==shape+"_SharedMesh")meshes[shape]=mf.sharedMesh;
  }
  if(meshes.Count!=4){Debug.LogError("Expected 4 mesh templates; found "+meshes.Count);return;}
  foreach(var v in config.variants){
   // Clone the import hierarchy to preserve FBX axis/unit conversion transforms.
   // Mesh data remains shared; Instantiate does not duplicate Mesh assets.
   var go=Object.Instantiate(model);go.name=v.name;
   foreach(var filter in go.GetComponentsInChildren<MeshFilter>(true))
    if(filter.sharedMesh!=meshes[v.shape])Object.DestroyImmediate(filter.gameObject);
   var renderer=go.GetComponentInChildren<MeshRenderer>(true);renderer.sharedMaterial=material;
   var variant=renderer.gameObject.AddComponent<SignAtlasVariant>();variant.atlasRect=new Vector4(v.rect[0],v.rect[1],v.rect[2],v.rect[3]);variant.Apply();
   // Do not mark Batching Static: per-instance atlas rectangles must stay independent.
   PrefabUtility.SaveAsPrefabAsset(go,Root+"/Prefabs/"+v.name+".prefab");Object.DestroyImmediate(go);
  }
  AssetDatabase.SaveAssets();AssetDatabase.Refresh();Debug.Log("Created 10 prefabs: 4 shared meshes, 1 material, 1 atlas. Use the Prefabs folder.");
 }
}
