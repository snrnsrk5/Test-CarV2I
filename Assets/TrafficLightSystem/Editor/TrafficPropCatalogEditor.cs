using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace ModularTrafficLights.Editor
{
    [InitializeOnLoad]
    internal static class TrafficPropCatalogEditor
    {
        private const string Folder = "Assets/TrafficLightSystem/Resources";
        private const string Path = Folder + "/TrafficPropCatalog.asset";
        private static bool queued;

        static TrafficPropCatalogEditor()
        {
            EditorApplication.hierarchyChanged += QueueRefresh;
            EditorApplication.projectChanged += QueueRefresh;
            QueueRefresh();
        }

        private static void QueueRefresh()
        {
            if (queued) return;
            queued = true;
            EditorApplication.delayCall += Refresh;
        }

        public static void Refresh()
        {
            queued = false;
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling) return;
            var horizontal = new HashSet<GameObject>();
            var vertical = new HashSet<GameObject>();
            bool foundHorizontal = false, foundVertical = false;
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                var scene = SceneManager.GetSceneAt(i);
                if (!scene.isLoaded || EditorSceneManager.IsPreviewScene(scene)) continue;
                foreach (var root in scene.GetRootGameObjects())
                foreach (var group in root.GetComponentsInChildren<Transform>(true))
                {
                    string name = group.name.Trim();
                    bool width = name.Equals("width", StringComparison.OrdinalIgnoreCase);
                    bool length = name.Equals("lengh", StringComparison.OrdinalIgnoreCase) ||
                        name.Equals("length", StringComparison.OrdinalIgnoreCase);
                    if (!width && !length) continue;
                    if (width) foundHorizontal = true; else foundVertical = true;
                    foreach (Transform child in group) Collect(child, width ? horizontal : vertical);
                }
            }
            if (!foundHorizontal && !foundVertical) return;
            var catalog = AssetDatabase.LoadAssetAtPath<TrafficPropCatalog>(Path);
            if (!catalog)
            {
                if (!AssetDatabase.IsValidFolder(Folder)) AssetDatabase.CreateFolder("Assets/TrafficLightSystem", "Resources");
                catalog = ScriptableObject.CreateInstance<TrafficPropCatalog>();
                AssetDatabase.CreateAsset(catalog, Path);
            }
            var nextHorizontal = foundHorizontal ? Sorted(horizontal) : catalog.horizontal;
            var nextVertical = foundVertical ? Sorted(vertical) : catalog.vertical;
            if (catalog.horizontal.SequenceEqual(nextHorizontal) && catalog.vertical.SequenceEqual(nextVertical)) return;
            catalog.horizontal = nextHorizontal;
            catalog.vertical = nextVertical;
            EditorUtility.SetDirty(catalog);
            AssetDatabase.SaveAssetIfDirty(catalog);
        }

        private static GameObject[] Sorted(HashSet<GameObject> found) => found.OrderBy(x => x.name, StringComparer.OrdinalIgnoreCase).ToArray();

        private static void Collect(Transform item, HashSet<GameObject> found)
        {
            var source = PrefabUtility.GetCorrespondingObjectFromSource(item.gameObject);
            if (source)
            {
                // A direct child is one complete selectable module, including model prefabs.
                found.Add(source);
                return;
            }
            foreach (Transform child in item) Collect(child, found);
        }

        public static GameObject[] Candidates(bool horizontal)
        {
            var catalog = AssetDatabase.LoadAssetAtPath<TrafficPropCatalog>(Path);
            if (catalog) return (horizontal ? catalog.horizontal : catalog.vertical).Where(x => x).ToArray();
            return Array.Empty<GameObject>();
        }

        public static void DrawChoice(Rect rect, SerializedProperty property, bool horizontal, string label, bool cameraOnly = false, bool excludeCameras = false)
        {
            var candidates = Candidates(horizontal).ToList();
            var catalog = AssetDatabase.LoadAssetAtPath<TrafficPropCatalog>(Path);
            if (cameraOnly) candidates.RemoveAll(x => !catalog || !catalog.IsCamera(x));
            if (excludeCameras) candidates.RemoveAll(x => catalog && catalog.IsCamera(x));
            var current = property.objectReferenceValue as GameObject;
            // Preserve and display existing selections even if their source group was removed.
            if (current && !candidates.Contains(current) && !cameraOnly && !excludeCameras) candidates.Add(current);
            var labels = new[] { "없음" }.Concat(candidates.Select(x => cameraOnly ? "카메라 (" + x.name + ")" : x.name)).ToArray();
            int selected = current ? candidates.IndexOf(current) + 1 : 0;
            EditorGUI.BeginChangeCheck();
            int choice = EditorGUI.Popup(rect, label, selected, labels);
            if (EditorGUI.EndChangeCheck()) property.objectReferenceValue = choice == 0 ? null : candidates[choice - 1];
        }
    }
}
