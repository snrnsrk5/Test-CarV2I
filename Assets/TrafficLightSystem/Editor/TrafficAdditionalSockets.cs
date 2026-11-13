using System;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace ModularTrafficLights.Editor
{
    [InitializeOnLoad]
    internal static class TrafficAdditionalSockets
    {
        private static bool queued;
        static TrafficAdditionalSockets()
        {
            EditorApplication.hierarchyChanged += Queue;
            Queue();
        }

        private static void Queue()
        {
            if (queued) return;
            queued = true;
            EditorApplication.delayCall += () =>
            {
                queued = false;
                if (EditorApplication.isPlayingOrWillChangePlaymode) return;
                foreach (var owner in UnityEngine.Object.FindObjectsOfType<TrafficLightController>(true)) Bind(owner);
            };
        }

        public static void Bind(TrafficLightController owner)
        {
            if (!owner || Application.isPlaying || !owner.gameObject.scene.IsValid()) return;
            var transforms = owner.GetComponentsInChildren<Transform>(true);
            Transform Find(string name)
            {
                var matches = transforms.Where(x => x.name.Equals(name, StringComparison.OrdinalIgnoreCase)).ToArray();
                if (matches.Length == 0 && UnityEngine.Object.FindObjectsOfType<TrafficLightController>(true).Count(x => x.gameObject.scene == owner.gameObject.scene) == 1)
                    matches = owner.gameObject.scene.GetRootGameObjects().SelectMany(x => x.GetComponentsInChildren<Transform>(true))
                        .Where(x => x.name.Equals(name, StringComparison.OrdinalIgnoreCase)).ToArray();
                return matches.Length == 1 ? matches[0] : null;
            }
            void Own(Transform socket)
            {
                if (!socket.IsChildOf(owner.transform))
                    Undo.SetTransformParent(socket, owner.transform, "Attach traffic socket preserving world position");
            }
            bool changed = false;
            if (owner.signs)
            {
                owner.signs.EnsureConfigurations();
                bool recorded = false;
                for (int count = 2; count <= 3; count++)
                {
                    var zones = owner.signs.GetZones(count);
                    for (int zone = 1; zone < zones.Length; zone++)
                    {
                        var socket = Find(zone == 1 ? "Socket_add" : "Socket_add2");
                        if (!socket || zones[zone] == null || zones[zone].additionalSocket) continue;
                        if (!recorded) { Undo.RecordObject(owner.signs, "Connect additional horizontal sockets"); recorded = true; }
                        Own(socket);
                        zones[zone].additionalSocket = socket;
                        changed = true;
                    }
                }
                if (recorded) SaveBinding(owner.signs);
            }
            if (owner.accessories)
            {
                var socket = Find("Socket_lengh");
                owner.accessories.EnsureAdditionalEntries();
                var module = owner.accessories;
                if (socket)
                {
                    var existing = module.additional.FirstOrDefault(x => x != null && x.mount != null && x.mount.socket == socket);
                    if (existing == null || !existing.useNonCameraHorizontalTypes)
                    {
                        Undo.RecordObject(module, "Connect additional vertical socket");
                        Own(socket);
                        if (existing == null)
                        {
                            existing = new AccessoryModule.Accessory { mount = new PropMount { socket = socket } };
                            module.additional = module.additional.Concat(new[] { existing }).ToArray();
                        }
                        existing.useNonCameraHorizontalTypes = true;
                        existing.kind = AccessoryModule.AccessoryKind.Prefab;
                        SaveBinding(module);
                        changed = true;
                    }
                }
            }
            if (changed)
            {
                EditorSceneManager.MarkSceneDirty(owner.gameObject.scene);
                Debug.Log("Additional traffic sockets on " + owner.name + ": Socket_add=" + (Find("Socket_add") != null) +
                    ", Socket_add2=" + (Find("Socket_add2") != null) + ", Socket_lengh=" + (Find("Socket_lengh") != null) +
                    " (existing positions preserved).", owner);
            }
        }

        private static void SaveBinding(Component module)
        {
            EditorUtility.SetDirty(module);
            PrefabUtility.RecordPrefabInstancePropertyModifications(module);
        }
    }
}
