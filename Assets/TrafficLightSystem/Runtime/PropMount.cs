using System;
using UnityEngine;

namespace ModularTrafficLights
{
    /// <summary>Only manages the instance it created. Other socket children are untouched.</summary>
    [Serializable]
    public sealed class PropMount
    {
        public Transform socket;
        public GameObject prefab;
        [SerializeField, HideInInspector] private GameObject generatedInstance;
        [SerializeField, HideInInspector] private GameObject generatedFrom;
        public GameObject Instance => generatedInstance;

        public bool HasValidPrefix(bool horizontal)
        {
            if (!prefab) return false;
            var catalog = TrafficPropCatalog.Current;
            // Explicit hierarchy classification takes priority over legacy names.
            if (catalog && (catalog.Contains(prefab, true) || catalog.Contains(prefab, false)))
                return catalog.Contains(prefab, horizontal);
            return horizontal ? TrafficLightRules.IsHorizontalProp(prefab.name) : TrafficLightRules.IsVerticalProp(prefab.name);
        }

        public void Apply(bool requested, bool horizontal)
        {
            if (!requested || !socket || !HasValidPrefix(horizontal))
            {
                TrafficObjectUtility.SetActive(generatedInstance, false);
                return;
            }
            // Scene objects are not valid prefab inputs for this module.
            if (prefab.scene.IsValid())
            {
                TrafficObjectUtility.SetActive(generatedInstance, false);
                return;
            }
            if (generatedInstance && generatedFrom != prefab)
            {
                var obsolete = generatedInstance;
                generatedInstance = null; generatedFrom = null;
                TrafficObjectUtility.SetActive(obsolete, false);
                TrafficObjectUtility.DestroyOwned(obsolete);
            }
            if (!generatedInstance)
            {
#if UNITY_EDITOR
                if (!Application.isPlaying)
                {
                    generatedInstance = (GameObject)UnityEditor.PrefabUtility.InstantiatePrefab(prefab, socket);
                    UnityEditor.Undo.RegisterCreatedObjectUndo(generatedInstance, "Add traffic light prop");
                }
                else
#endif
                    generatedInstance = UnityEngine.Object.Instantiate(prefab, socket, false);
                generatedFrom = prefab;
            }
            // Imported prefabs may require an authored axis correction and unit
            // scale at their root. Align the pivot without discarding that pose.
            // The asset's scene position is intentionally replaced by the socket.
            var target = generatedInstance.transform;
            var authored = prefab.transform;
            if (target.parent != socket) target.SetParent(socket, false);
            if (target.localPosition != Vector3.zero) target.localPosition = Vector3.zero;
            if (Quaternion.Angle(target.localRotation, authored.localRotation) > 0.001f)
                target.localRotation = authored.localRotation;
            if (target.localScale != authored.localScale) target.localScale = authored.localScale;
            TrafficObjectUtility.SetActive(generatedInstance, true);
        }
    }

    internal static class TrafficObjectUtility
    {
        public static void SetActive(GameObject target, bool active)
        {
            if (target && target.activeSelf != active) target.SetActive(active);
        }

        public static void Attach(Transform target, Transform socket)
        {
            if (!target || !socket || target == socket) return;
            if (target.parent != socket) target.SetParent(socket, false);
            if (target.localPosition != Vector3.zero) target.localPosition = Vector3.zero;
            if (Quaternion.Angle(target.localRotation, Quaternion.identity) > 0.001f) target.localRotation = Quaternion.identity;
            if (target.localScale != Vector3.one) target.localScale = Vector3.one;
        }

        public static void DestroyOwned(GameObject target)
        {
            if (!target) return;
#if UNITY_EDITOR
            if (!Application.isPlaying) { UnityEditor.Undo.DestroyObjectImmediate(target); return; }
#endif
            UnityEngine.Object.Destroy(target);
        }
    }
}
