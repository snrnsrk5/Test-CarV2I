using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace ModularTrafficLights.Editor
{
    [InitializeOnLoad]
    internal static class PedestrianPoleSetup
    {
        private const string Folder = "Assets/TrafficLightSystem/PedestrianSignal";
        private const string PrefabPath = Folder + "/PedestrianSignal.prefab";
        private static bool queued, building;
        [Serializable] private sealed class Description { public Entry[] materials = Array.Empty<Entry>(); public int meshCount = 0; }
        [Serializable] private sealed class Entry
        {
            public string name = "", baseTexture = "", normalTexture = "";
            public float[] baseColor = new float[4];
            public float metallic = 0, roughness = .5f;
        }

        static PedestrianPoleSetup()
        {
            EditorApplication.projectChanged += Queue;
            Queue();
        }
        private static void Queue()
        {
            if (queued || building) return;
            queued = true;
            EditorApplication.delayCall += () =>
            {
                queued = false;
                if (EditorApplication.isCompiling || EditorApplication.isPlayingOrWillChangePlaymode ||
                    AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath)) return;
                if (!AssetDatabase.LoadAssetAtPath<GameObject>(Folder + "/PedestrianSignal.fbx") ||
                    !AssetDatabase.LoadAssetAtPath<TextAsset>(Folder + "/PedestrianSignal_Manifest.json")) return;
                Build();
            };
        }

        [MenuItem("Tools/Traffic Lights/Pedestrian Signal Prefab")]
        private static void OpenOrBuild()
        {
            var existing = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            if (existing) { Selection.activeObject = existing; EditorGUIUtility.PingObject(existing); }
            else Build();
        }

        private static void Build()
        {
            if (building) return;
            building = true;
            GameObject root = null;
            try
            {
                var scene = SceneManager.GetActiveScene();
                if (!scene.IsValid() || !scene.isLoaded || EditorSceneManager.IsPreviewScene(scene)) return;
                var importer = (ModelImporter)AssetImporter.GetAtPath(Folder + "/PedestrianSignal.fbx");
                if (!importer) throw new InvalidOperationException("Pedestrian model importer missing.");
                if (!importer.bakeAxisConversion || importer.importAnimation)
                {
                    importer.bakeAxisConversion = true;
                    importer.importAnimation = false;
                    importer.SaveAndReimport();
                }
                var description = JsonUtility.FromJson<Description>(AssetDatabase.LoadAssetAtPath<TextAsset>(Folder + "/PedestrianSignal_Manifest.json").text);
                var lit = Shader.Find("Universal Render Pipeline/Lit");
                var signalShader = Shader.Find("TrafficLight/URP/SignalAtlas");
                if (!lit || !signalShader) throw new InvalidOperationException("Required URP shaders missing.");
                if (!AssetDatabase.IsValidFolder(Folder + "/Materials")) AssetDatabase.CreateFolder(Folder, "Materials");
                Texture2D Texture(string name) => AssetDatabase.LoadAssetAtPath<Texture2D>(Folder + "/Textures/" + name);
                foreach (string name in new[] { "Metal_Normal.png", "Signal_EmissionMask.png" })
                {
                    var textureImporter = (TextureImporter)AssetImporter.GetAtPath(Folder + "/Textures/" + name);
                    if (!textureImporter) throw new InvalidOperationException("Missing texture: " + name);
                    textureImporter.textureType = name.Contains("Normal") ? TextureImporterType.NormalMap : TextureImporterType.Default;
                    textureImporter.sRGBTexture = false;
                    textureImporter.SaveAndReimport();
                }
                var signal = new Material(signalShader) { name = "Pedestrian_Signal_URP" };
                signal.SetTexture("_BaseMap", Texture("Signal_BaseColor.png"));
                signal.SetTexture("_MaskMap", Texture("Signal_EmissionMask.png"));
                signal.SetFloat("_EmissionPower", 0);
                AssetDatabase.CreateAsset(signal, Folder + "/Materials/Pedestrian_Signal_URP.mat");
                var materials = new Dictionary<string, Material>();
                for (int i = 0; i < description.materials.Length; i++)
                {
                    var entry = description.materials[i];
                    if (entry.name.StartsWith("Traffic Light_", StringComparison.Ordinal)) { materials[Key(entry.name)] = signal; continue; }
                    var material = new Material(lit) { name = entry.name };
                    material.SetColor("_BaseColor", new Color(entry.baseColor[0], entry.baseColor[1], entry.baseColor[2], entry.baseColor[3]));
                    material.SetFloat("_Metallic", entry.metallic);
                    material.SetFloat("_Smoothness", 1 - entry.roughness);
                    if (!string.IsNullOrEmpty(entry.baseTexture)) material.SetTexture("_BaseMap", Texture(entry.baseTexture));
                    if (!string.IsNullOrEmpty(entry.normalTexture))
                    {
                        material.SetTexture("_BumpMap", Texture(entry.normalTexture)); material.EnableKeyword("_NORMALMAP");
                    }
                    AssetDatabase.CreateAsset(material, Folder + "/Materials/Surface_" + i.ToString("00") + ".mat");
                    materials[Key(entry.name)] = material;
                }
                root = new GameObject("PedestrianSignal");
                var model = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(Folder + "/PedestrianSignal.fbx"), root.transform);
                PrefabUtility.UnpackPrefabInstance(model, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
                var renderers = model.GetComponentsInChildren<MeshRenderer>(true);
                if (renderers.Length != description.meshCount) throw new InvalidOperationException("Imported mesh count mismatch.");
                foreach (var renderer in renderers)
                    renderer.sharedMaterials = renderer.sharedMaterials.Select(m => m && materials.TryGetValue(Key(m.name), out var mapped) ? mapped :
                        throw new InvalidOperationException("Unmapped source material on " + renderer.name + ": " + (m ? m.name : "null"))).ToArray();
                Transform Find(string name) => model.GetComponentsInChildren<Transform>(true).Single(x => x.name == name);
                var numberHead = Find("TrafficLight_Human_Shape.001");
                var barHead = Find("TrafficLight_Human_Shape");
                Transform Pivot(string name, Transform head)
                {
                    var pivot = new GameObject(name).transform;
                    pivot.SetParent(root.transform, false);
                    head.SetParent(pivot, true);
                    return pivot;
                }
                var numberPivot = Pivot("Number_Rotation", numberHead);
                var barPivot = Pivot("Bar_Rotation", barHead);
                foreach (var screw in model.GetComponentsInChildren<Transform>(true).Where(x => x.name.StartsWith("Signal mount | clamp screw", StringComparison.Ordinal)).ToArray())
                {
                    int suffix = screw.name.Contains(".") ? int.Parse(screw.name.Substring(screw.name.LastIndexOf('.') + 1)) : 0;
                    screw.SetParent(suffix % 2 == 0 ? barPivot : numberPivot, true);
                }
                var pole = root.AddComponent<PedestrianPoleController>();
                pole.numberPivot = numberPivot; pole.barPivot = barPivot;
                pole.guardRetainingBand4 = Find("Guard retaining band 4").gameObject;
                pole.numberSignal = Bind(numberHead, true, signal);
                pole.barSignal = Bind(barHead, false, signal);
                Validate(pole, renderers);
                pole.ApplyConfiguration();
                pole.numberSignal.RefreshPreview(); pole.barSignal.RefreshPreview();
                var prefab = PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
                if (!prefab) throw new InvalidOperationException("Could not save pedestrian prefab.");
                Object.DestroyImmediate(root); root = null;
                var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
                var traffic = Object.FindObjectsOfType<TrafficLightController>().FirstOrDefault(x => x.gameObject.scene == scene);
                instance.transform.position = (traffic ? traffic.transform.position : Vector3.zero) + new Vector3(3, 0, 0);
                Undo.RegisterCreatedObjectUndo(instance, "Add prepared pedestrian signal");
                PrefabUtility.RecordPrefabInstancePropertyModifications(instance.transform);
                EditorSceneManager.MarkSceneDirty(scene);
                Selection.activeGameObject = instance;
                AssetDatabase.SaveAssets();
                Debug.Log("PEDESTRIAN_POLE_READY: " + PrefabPath + "; 40 meshes, 14 number segments, 9 bar segments; both 90-degree rotations and Guard retaining band 4 toggle verified. Scene instance added.", instance);
            }
            catch (Exception exception) { Debug.LogException(exception); }
            finally { if (root) Object.DestroyImmediate(root); building = false; }
        }

        private static string Key(string name) => new string(name.Where(char.IsLetterOrDigit).ToArray()).ToLowerInvariant();
        private static PedestrianSignalController Bind(Transform head, bool numeric, Material material)
        {
            var controller = head.gameObject.AddComponent<PedestrianSignalController>();
            controller.signalMaterial = material;
            var renderers = head.GetComponentsInChildren<MeshRenderer>(true);
            MeshRenderer Find(string name) => renderers.Single(x => x.name == name);
            controller.red.renderer = Find(numeric ? "Off_RedHN" : "Off_RedH");
            controller.green.renderer = Find(numeric ? "On_GreenHN" : "On_GreenH");
            controller.remainingSeconds = 30;
            controller.playOriginalNumberLoop = numeric;
            if (numeric)
            {
                controller.numberSegments = renderers.Where(x => x.name.StartsWith("On_GreenHN_", StringComparison.Ordinal)).Select(x =>
                {
                    var code = x.name.Substring("On_GreenHN_".Length);
                    return new PedestrianSignalController.NumberSegment { sourceDigit = code[0] - '0', sourceCode = code.Substring(1), visual = new LampVisual { renderer = x } };
                }).ToArray();
                if (controller.numberSegments.Length != 14) throw new InvalidOperationException("Number segment count mismatch.");
            }
            else controller.barSegments = Enumerable.Range(1, 9).Select(i => new LampVisual { renderer = Find("On_Green_T" + i) }).ToArray();
            return controller;
        }

        private static void Validate(PedestrianPoleController pole, MeshRenderer[] renderers)
        {
            var bounds = renderers[0].bounds;
            foreach (var renderer in renderers) bounds.Encapsulate(renderer.bounds);
            if (bounds.size.y < 4.1f || bounds.size.y > 4.3f) throw new InvalidOperationException("Pedestrian model height/axis mismatch: " + bounds.size);
            foreach (var pair in new[] { (pole.numberPivot, true), (pole.barPivot, false) })
            {
                var other = pair.Item2 ? pole.barPivot : pole.numberPivot;
                var otherRotation = other.localRotation;
                for (int step = 0; step <= 3; step++)
                {
                    if (pair.Item2) pole.numberQuarterTurns = step; else pole.barQuarterTurns = step;
                    pole.ApplyConfiguration();
                    if (Quaternion.Angle(pair.Item1.localRotation, Quaternion.AngleAxis(step * 90, Vector3.up)) > .01f ||
                        Quaternion.Angle(other.localRotation, otherRotation) > .01f) throw new InvalidOperationException("Independent rotation validation failed.");
                }
                if (pair.Item2) pole.numberQuarterTurns = 0; else pole.barQuarterTurns = 0;
                pole.ApplyConfiguration();
            }
            pole.showGuardRetainingBand4 = false; pole.ApplyConfiguration();
            if (pole.guardRetainingBand4.activeSelf) throw new InvalidOperationException("Guard hide validation failed.");
            pole.showGuardRetainingBand4 = true; pole.ApplyConfiguration();
            if (!pole.guardRetainingBand4.activeSelf) throw new InvalidOperationException("Guard show validation failed.");
        }
    }
}
