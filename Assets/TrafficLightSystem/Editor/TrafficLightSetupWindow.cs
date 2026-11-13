using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace ModularTrafficLights.Editor
{
    public sealed class TrafficLightSetupWindow : EditorWindow
    {
        private GameObject assemblyModel;
        private GameObject barModel;
        private RenderPipelineAsset pipelineToAssign;
        private const string GeneratedRoot = "Assets/TrafficLightSystem/Generated";

        [MenuItem("Tools/Traffic Lights/Setup Prepared Model")]
        private static void Open() => GetWindow<TrafficLightSetupWindow>("Traffic Light Setup");

        private void OnEnable()
        {
            assemblyModel = FindModel("TrafficLight_Assembly_3Head");
            barModel = FindModel("TrafficLight_Pedestrian_Bar");
        }

        private static GameObject FindModel(string name)
        {
            var paths = AssetDatabase.FindAssets(name).Select(AssetDatabase.GUIDToAssetPath)
                .Where(p => string.Equals(Path.GetFileNameWithoutExtension(p), name, StringComparison.Ordinal)
                    && p.EndsWith(".fbx", StringComparison.OrdinalIgnoreCase)).ToArray();
            return paths.Length == 1 ? AssetDatabase.LoadAssetAtPath<GameObject>(paths[0]) : null;
        }

        private void OnGUI()
        {
            EditorGUILayout.HelpBox("준비한 FBX를 선택하면 부품 연결과 URP 머티리얼을 설정한 Prefab을 만듭니다.", MessageType.Info);
            DrawPipelineStatus();
            assemblyModel = (GameObject)EditorGUILayout.ObjectField("3-head assembly FBX", assemblyModel, typeof(GameObject), false);
            barModel = (GameObject)EditorGUILayout.ObjectField("Bar pedestrian FBX", barModel, typeof(GameObject), false);
            using (new EditorGUI.DisabledScope(!assemblyModel))
                if (GUILayout.Button("관리용 Prefab 만들기"))
                {
                    try
                    {
                        var prefab = Build(assemblyModel, barModel);
                        var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
                        Undo.RegisterCreatedObjectUndo(instance, "Create traffic light");
                        Selection.activeGameObject = instance;
                        EditorGUIUtility.PingObject(prefab);
                    }
                    catch (Exception ex) { Debug.LogException(ex); }
                }
        }

        public static GameObject Build(GameObject assembly, GameObject optionalBar)
        {
            var pipeline = ConfiguredPipeline();
            if (!IsUniversal(pipeline))
                throw new InvalidOperationException("현재 Quality에 적용되는 URP Asset이 없습니다. " +
                    "Quality=" + Describe(QualitySettings.renderPipeline) + ", Graphics=" + Describe(GraphicsSettings.defaultRenderPipeline) +
                    ". 설치 창의 렌더 파이프라인 설정을 확인해주세요. URP 패키지 설치만으로는 URP가 활성화되지 않습니다.");
            string assemblyPath = AssetDatabase.GetAssetPath(assembly);
            string barPath = optionalBar ? AssetDatabase.GetAssetPath(optionalBar) : null;
            ConfigureImporter(assemblyPath);
            if (!string.IsNullOrEmpty(barPath))
            {
                ConfigureImporter(barPath);
                optionalBar = AssetDatabase.LoadAssetAtPath<GameObject>(barPath);
            }
            assembly = AssetDatabase.LoadAssetAtPath<GameObject>(assemblyPath);
            var signalShader = Shader.Find("TrafficLight/URP/SignalAtlas");
            var litShader = Shader.Find("Universal Render Pipeline/Lit");
            if (!signalShader || !litShader) throw new InvalidOperationException("URP 또는 SignalAtlasURP.shader의 임포트/컴파일을 확인해주세요.");
            if (ShaderUtil.ShaderHasError(signalShader)) throw new InvalidOperationException("SignalAtlasURP.shader에 컴파일 오류가 있습니다. Console을 확인해주세요.");

            Directory.CreateDirectory(GeneratedRoot);
            AssetDatabase.Refresh();
            string folder = AssetDatabase.GenerateUniqueAssetPath(GeneratedRoot + "/TrafficLight");
            Directory.CreateDirectory(folder);
            AssetDatabase.Refresh();
            string textureFolder = Path.GetDirectoryName(Path.GetDirectoryName(assemblyPath)).Replace('\\', '/') + "/Textures";
            Texture2D Texture(string name) => LoadTexture(textureFolder + "/" + name + ".png");
            var metalBase = Texture("TL_Metal_BaseColor");
            var metalNormal = Texture("TL_Metal_Normal");
            var roughness = Texture("TL_Metal_Roughness");
            var signalBase = Texture("TL_Signal_BaseColor");
            var mask = Texture("TL_Signal_EmissionMask");
            ConfigureTexture(AssetDatabase.GetAssetPath(metalNormal), true, false);
            ConfigureTexture(AssetDatabase.GetAssetPath(roughness), false, true);
            ConfigureTexture(AssetDatabase.GetAssetPath(mask), false, true);

            var housing = new Material(litShader) { name = "TL_Housing_URP" };
            housing.SetColor("_BaseColor", new Color(0.04f, 0.04f, 0.04f));
            housing.SetFloat("_Metallic", 0.08f); housing.SetFloat("_Smoothness", 0.5f);
            AssetDatabase.CreateAsset(housing, folder + "/TL_Housing_URP.mat");
            var metal = new Material(litShader) { name = "TL_Metal_URP" };
            metal.SetTexture("_BaseMap", metalBase);
            metal.SetTexture("_BumpMap", metalNormal); metal.EnableKeyword("_NORMALMAP");
            metal.SetFloat("_Metallic", 1);
            metal.SetFloat("_Smoothness", 1);
            metal.SetTexture("_MetallicGlossMap", MakeMetalSmoothness(AssetDatabase.GetAssetPath(roughness), folder));
            metal.EnableKeyword("_METALLICSPECGLOSSMAP");
            AssetDatabase.CreateAsset(metal, folder + "/TL_Metal_URP.mat");
            var signal = new Material(signalShader) { name = "TL_Signal_URP_Base" };
            signal.SetTexture("_BaseMap", signalBase); signal.SetTexture("_MaskMap", mask);
            signal.SetFloat("_EmissionPower", 0);
            AssetDatabase.CreateAsset(signal, folder + "/TL_Signal_URP_Base.mat");

            GameObject root = null;
            try
            {
                root = InstantiateUnpacked(assembly);
                root.name = "TrafficLight_Modular";
                root.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
                root.transform.localScale = Vector3.one;
                var transforms = root.GetComponentsInChildren<Transform>(true);
                Transform One(string exactName) => RequireOne(transforms, t => t.name == exactName, exactName);
                var pole = root.AddComponent<PoleModule>();
                pole.parts = new[]
                {
                    Part(One("3H_TL_Module_Arm_ExtensionA").gameObject, 2),
                    Part(One("3H_TL_Module_Arm_ExtensionB").gameObject, 3),
                    Part(One("3H_TL_Module_Cable_2").gameObject, 2),
                    Part(One("3H_TL_Module_Cable_3").gameObject, 3)
                };
                var signals = root.AddComponent<TrafficSignalModule>();
                signals.signals = new TrafficSignalModule.SignalMount[3];
                for (int i = 0; i < 3; i++)
                {
                    string prefix = "3H_S" + (i + 1) + "_";
                    var head = One(prefix + "TL_Module_Signal4");
                    var controller = head.gameObject.AddComponent<SignalLightController>();
                    controller.signalMaterial = signal;
                    foreach (SignalLamp role in Enum.GetValues(typeof(SignalLamp)))
                    {
                        string ending = "Lamp_0" + ((int)role + 1) + "_" + role;
                        var renderer = RequireOne(head.GetComponentsInChildren<MeshRenderer>(true), r => r.name.EndsWith(ending, StringComparison.Ordinal), ending);
                        controller.GetLamp(role).visual.renderer = renderer;
                    }
                    signals.signals[i] = new TrafficSignalModule.SignalMount
                    { signal = controller, socket = One("Socket_Signal_0" + (i + 1) + "_3H") };
                }
                var signs = root.AddComponent<TrafficSignModule>();
                signs.mounts = new PropMount[3];
                for (int i = 0; i < 3; i++) signs.mounts[i] = new PropMount { socket = One("Socket_Sign_0" + (i + 1) + "_3H") };
                var accessories = root.AddComponent<AccessoryModule>();
                accessories.pedestrianSocket = One("3H_Socket_Pedestrian");
                accessories.number = BindPedestrian(One("3H_TL_Module_Pedestrian_Number").gameObject, signal, true);
                if (optionalBar)
                {
                    var barObject = InstantiateUnpacked(optionalBar);
                    var barRoot = RequireOne(barObject.GetComponentsInChildren<Transform>(true), t => t.name == "TL_Module_Pedestrian_Bar", "TL_Module_Pedestrian_Bar");
                    if (barRoot != barObject.transform)
                    {
                        barRoot.SetParent(null, true);
                        Object.DestroyImmediate(barObject);
                        barObject = barRoot.gameObject;
                    }
                    barObject.transform.SetParent(accessories.pedestrianSocket, false);
                    barObject.transform.localPosition = Vector3.zero;
                    barObject.transform.localRotation = Quaternion.identity;
                    barObject.transform.localScale = Vector3.one;
                    accessories.bar = BindPedestrian(barObject, signal, false);
                    barObject.SetActive(false);
                }
                RemapMaterials(root, housing, metal, signal);
                TrafficLightAxisUtility.NormalizeAssembly(root);
                // Configure fields without invoking a material preview before saving:
                // the prefab keeps durable material assets, not transient cache entries.
                var manager = root.AddComponent<TrafficLightController>();
                manager.pole = pole; manager.signals = signals; manager.signs = signs; manager.accessories = accessories;
                var result = PrefabUtility.SaveAsPrefabAsset(root, folder + "/TrafficLight_Modular.prefab", out bool success);
                if (!success || !result) throw new InvalidOperationException("Prefab을 저장하지 못했습니다.");
                AssetDatabase.SaveAssets();
                Debug.Log("Traffic light prefab created: " + AssetDatabase.GetAssetPath(result));
                return result;
            }
            finally { if (root) Object.DestroyImmediate(root); }
        }

        private static RenderPipelineAsset ConfiguredPipeline() =>
            QualitySettings.renderPipeline ? QualitySettings.renderPipeline : GraphicsSettings.defaultRenderPipeline;

        private static bool IsUniversal(RenderPipelineAsset pipeline)
        {
            if (!pipeline) return false;
            for (var type = pipeline.GetType(); type != null; type = type.BaseType)
                if (type.FullName == "UnityEngine.Rendering.Universal.UniversalRenderPipelineAsset") return true;
            return false;
        }

        private static string Describe(RenderPipelineAsset pipeline) =>
            pipeline ? pipeline.name + " (" + pipeline.GetType().Name + ")" : "None";

        private void DrawPipelineStatus()
        {
            var configured = ConfiguredPipeline();
            EditorGUILayout.LabelField("현재 Quality", QualitySettings.names[QualitySettings.GetQualityLevel()]);
            EditorGUILayout.LabelField("Quality Render Pipeline", Describe(QualitySettings.renderPipeline));
            EditorGUILayout.LabelField("Graphics Render Pipeline", Describe(GraphicsSettings.defaultRenderPipeline));
            if (IsUniversal(configured)) return;
            EditorGUILayout.HelpBox("현재 설정에 URP가 활성화되어 있지 않습니다. 기존 URP Pipeline Asset을 아래에 지정하거나 Graphics/Quality에서 설정해주세요. Renderer Data가 아닌 Pipeline Asset이 필요합니다.", MessageType.Warning);
            pipelineToAssign = (RenderPipelineAsset)EditorGUILayout.ObjectField("적용할 URP Pipeline Asset", pipelineToAssign, typeof(RenderPipelineAsset), false);
            using (new EditorGUI.DisabledScope(!IsUniversal(pipelineToAssign)))
                if (GUILayout.Button("선택한 URP를 현재 Quality에 적용"))
                {
                    QualitySettings.renderPipeline = pipelineToAssign;
                    AssetDatabase.SaveAssets();
                    Repaint();
                }
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Graphics 설정 열기")) SettingsService.OpenProjectSettings("Project/Graphics");
                if (GUILayout.Button("Quality 설정 열기")) SettingsService.OpenProjectSettings("Project/Quality");
            }
        }

        private static PoleModule.CountedPart Part(GameObject part, int minimum) =>
            new PoleModule.CountedPart { part = part, minimumSignals = minimum };

        private static T RequireOne<T>(T[] items, Func<T, bool> predicate, string description)
        {
            var matches = items.Where(predicate).ToArray();
            if (matches.Length != 1) throw new InvalidOperationException("준비한 FBX의 항목을 유일하게 찾을 수 없습니다: " + description + " (" + matches.Length + ")");
            return matches[0];
        }

        private static GameObject InstantiateUnpacked(GameObject asset)
        {
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(asset);
            PrefabUtility.UnpackPrefabInstance(instance, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
            return instance;
        }

        private static PedestrianSignalController BindPedestrian(GameObject root, Material material, bool numeric)
        {
            var controller = root.AddComponent<PedestrianSignalController>();
            controller.signalMaterial = material;
            string variant = numeric ? "Number" : "Bar";
            var renderers = root.GetComponentsInChildren<MeshRenderer>(true);
            controller.red.renderer = RequireOne(renderers, r => r.name.EndsWith(variant + "_Ped_Red", StringComparison.Ordinal), "Ped_Red");
            controller.green.renderer = RequireOne(renderers, r => r.name.EndsWith(variant + "_Ped_Green", StringComparison.Ordinal), "Ped_Green");
            if (numeric)
            {
                controller.numberSegments = renderers.Where(r => r.name.Contains("Number_Digit_"))
                    .Select(r =>
                    {
                        var pieces = r.name.Substring(r.name.IndexOf("Number_Digit_", StringComparison.Ordinal) + 13).Split('_');
                        return new PedestrianSignalController.NumberSegment
                        { sourceDigit = int.Parse(pieces[0]), sourceCode = pieces[1], visual = new LampVisual { renderer = r } };
                    }).ToArray();
                if (controller.numberSegments.Length != 14) throw new InvalidOperationException("숫자형 세그먼트 14개를 찾지 못했습니다.");
            }
            else
            {
                controller.barSegments = renderers.Where(r => r.name.Contains("Bar_Timer_Bar_"))
                    .OrderBy(r => r.name, StringComparer.Ordinal).Select(r => new LampVisual { renderer = r }).ToArray();
                if (controller.barSegments.Length != 9) throw new InvalidOperationException("잔여시간 막대 9개를 찾지 못했습니다.");
            }
            return controller;
        }

        private static void RemapMaterials(GameObject root, Material housing, Material metal, Material signal)
        {
            foreach (var renderer in root.GetComponentsInChildren<MeshRenderer>(true))
            {
                var remapped = renderer.sharedMaterials.Select(mat =>
                {
                    if (!mat) throw new InvalidOperationException("FBX 머티리얼이 없습니다: " + renderer.name);
                    if (mat.name.StartsWith("TL_Metal", StringComparison.Ordinal)) return metal;
                    if (mat.name.StartsWith("TL_Housing", StringComparison.Ordinal)) return housing;
                    if (mat.name.StartsWith("TL_Signal", StringComparison.Ordinal)) return signal;
                    throw new InvalidOperationException("알 수 없는 FBX 머티리얼: " + mat.name);
                }).ToArray();
                renderer.sharedMaterials = remapped;
            }
        }

        private static Texture2D LoadTexture(string path)
        {
            var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            if (!texture) throw new FileNotFoundException("필요한 텍스처가 없습니다. Models와 Textures 폴더 구조를 확인해주세요.", path);
            return texture;
        }

        private static void ConfigureTexture(string path, bool normal, bool linear)
        {
            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            var kind = normal ? TextureImporterType.NormalMap : TextureImporterType.Default;
            bool change = importer.textureType != kind || (linear && importer.sRGBTexture);
            if (!change) return;
            importer.textureType = kind;
            if (linear) importer.sRGBTexture = false;
            importer.SaveAndReimport();
        }

        private static Texture2D MakeMetalSmoothness(string roughnessPath, string outputFolder)
        {
            var input = new Texture2D(2, 2, TextureFormat.RGBA32, false, true);
            Texture2D output = null;
            try
            {
                if (!ImageConversion.LoadImage(input, File.ReadAllBytes(roughnessPath), false))
                    throw new InvalidOperationException("Roughness 이미지를 읽지 못했습니다.");
                var colors = input.GetPixels32();
                for (int i = 0; i < colors.Length; i++) colors[i] = new Color32(255, 255, 255, (byte)(255 - colors[i].r));
                output = new Texture2D(input.width, input.height, TextureFormat.RGBA32, false, true);
                output.SetPixels32(colors); output.Apply();
                string path = outputFolder + "/TL_Metal_MetallicSmoothness.png";
                File.WriteAllBytes(path, ImageConversion.EncodeToPNG(output));
                AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
                ConfigureTexture(path, false, true);
                return LoadTexture(path);
            }
            finally { Object.DestroyImmediate(input); if (output) Object.DestroyImmediate(output); }
        }

        private static void ConfigureImporter(string path)
        {
            var importer = AssetImporter.GetAtPath(path) as ModelImporter;
            if (!importer) throw new InvalidOperationException("FBX Model asset을 선택해주세요: " + path);
            importer.globalScale = 1;
            importer.useFileScale = true;
            importer.bakeAxisConversion = true;
            importer.preserveHierarchy = true;
            importer.importCameras = false; importer.importLights = false;
            importer.importAnimation = false; importer.isReadable = false;
            importer.materialImportMode = ModelImporterMaterialImportMode.ImportStandard;
            importer.importNormals = ModelImporterNormals.Import;
            importer.SaveAndReimport();
        }
    }
}
