using System;
using System.Collections.Generic;
using UnityEngine;

namespace ModularTrafficLights.Editor
{
    internal static class TrafficLightAxisUtility
    {
        private sealed class Pose
        {
            internal Transform target;
            internal Vector3 position, localPosition, localScale;
            internal Quaternion rotation, localRotation;
            internal Matrix4x4 worldMatrix;
            internal bool geometry;
        }

        internal static void NormalizeAssembly(GameObject root)
        {
            Transform assembly = null;
            foreach (var item in root.GetComponentsInChildren<Transform>(true))
                if (item.name == "TL_Assembly_3Head")
                {
                    if (assembly) throw new InvalidOperationException("Duplicate TL_Assembly_3Head root.");
                    assembly = item;
                }
            if (!assembly || Quaternion.Angle(assembly.localRotation, Quaternion.identity) < 0.001f) return;
            var poses = new List<Pose>();
            Capture(assembly, poses);
            Quaternion parent = assembly.parent ? assembly.parent.rotation : Quaternion.identity;
            Quaternion correction = parent * Quaternion.Inverse(assembly.localRotation) * Quaternion.Inverse(parent);
            try
            {
                foreach (var pose in poses)
                    pose.target.SetPositionAndRotation(pose.position, pose.geometry ? pose.rotation : correction * pose.rotation);
                foreach (var pose in poses)
                {
                    if (!pose.geometry) continue;
                    var actual = pose.target.localToWorldMatrix;
                    for (int r = 0; r < 4; r++)
                        for (int c = 0; c < 4; c++)
                            if (Mathf.Abs(actual[r, c] - pose.worldMatrix[r, c]) > 0.001f)
                                throw new InvalidOperationException("Axis normalization changed model geometry: " + pose.target.name);
                }
            }
            catch
            {
                foreach (var pose in poses)
                {
                    pose.target.localPosition = pose.localPosition;
                    pose.target.localRotation = pose.localRotation;
                    pose.target.localScale = pose.localScale;
                }
                throw;
            }
        }

        private static void Capture(Transform target, List<Pose> poses)
        {
            // User-authored props retain their original local pose at the corrected socket.
            if (target.name.StartsWith("WIDTH_", StringComparison.Ordinal) || target.name.StartsWith("LENGTH_", StringComparison.Ordinal)) return;
            poses.Add(new Pose
            {
                target = target, position = target.position, rotation = target.rotation,
                localPosition = target.localPosition, localRotation = target.localRotation,
                localScale = target.localScale, worldMatrix = target.localToWorldMatrix,
                geometry = target.GetComponent<Renderer>() != null
            });
            for (int i = 0; i < target.childCount; i++) Capture(target.GetChild(i), poses);
        }
    }
}
