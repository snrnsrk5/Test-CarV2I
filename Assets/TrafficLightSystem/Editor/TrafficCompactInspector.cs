using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace ModularTrafficLights.Editor
{
    internal static class TrafficCompactInspector
    {
        public static bool Signs(TrafficSignModule module)
        {
            if (!module) return false;
            TrafficAdditionalSockets.Bind(module.GetComponent<TrafficLightController>());
            module.EnsureConfigurations();
            var settings = new SerializedObject(module);
            settings.Update();
            var owner = module.GetComponent<TrafficLightController>();
            int count = owner ? owner.SignalCount : TrafficLightRules.ClampSignalCount(module.signCount);
            EditorGUILayout.LabelField("가로 부착물", EditorStyles.boldLabel);
            var zones = settings.FindProperty(count == 1 ? "oneSign" : count == 2 ? "twoSigns" : "threeSigns");
            for (int i = 0; i < zones.arraySize; i++)
            {
                var zone = zones.GetArrayElementAtIndex(i);
                if (i == 0)
                {
                    SignSlot(zone, "prefab", "disabled", "1구역 1종류");
                    SignSlot(zone, "secondPrefab", "secondDisabled", "1구역 2종류");
                }
                else
                {
                    if (SignSlot(zone, "prefab", "disabled", (i + 1) + "구역 카메라", true))
                        ClearSlot(zone, "additionalPrefab", "additionalDisabled");
                    SignSlot(zone, "secondPrefab", "secondDisabled", (i + 1) + "구역 1종류");
                    if (SignSlot(zone, "additionalPrefab", "additionalDisabled", (i + 1) + "구역 2종류", false, true))
                        ClearSlot(zone, "prefab", "disabled");
                }
            }
            return settings.ApplyModifiedProperties();
        }

        private static void ClearSlot(SerializedProperty zone, string prefabField, string disabledField)
        {
            zone.FindPropertyRelative(prefabField).objectReferenceValue = null;
            zone.FindPropertyRelative(disabledField).boolValue = true;
        }

        private static bool SignSlot(SerializedProperty zone, string prefabField, string disabledField, string label, bool cameraOnly = false, bool excludeCameras = false)
        {
            bool changed;
            using (new EditorGUILayout.HorizontalScope())
            {
                var disabled = zone.FindPropertyRelative(disabledField);
                var prefab = zone.FindPropertyRelative(prefabField);
                var previous = prefab.objectReferenceValue;
                EditorGUI.BeginChangeCheck();
                bool enabled = EditorGUILayout.Toggle(!disabled.boolValue, GUILayout.Width(18));
                changed = EditorGUI.EndChangeCheck();
                if (changed) disabled.boolValue = !enabled;
                TrafficPropCatalogEditor.DrawChoice(EditorGUILayout.GetControlRect(), prefab, true, label, cameraOnly, excludeCameras);
                // Selecting a type also activates that slot. Clearing the other
                // slot should not require an extra checkbox click to switch back.
                if (previous != prefab.objectReferenceValue)
                {
                    changed = true;
                    if (prefab.objectReferenceValue) disabled.boolValue = false;
                }
                return changed && !disabled.boolValue && prefab.objectReferenceValue;
            }
        }

        public static bool Accessories(AccessoryModule module)
        {
            if (!module) return false;
            TrafficAdditionalSockets.Bind(module.GetComponent<TrafficLightController>());
            module.EnsureAdditionalEntries();
            var settings = new SerializedObject(module);
            settings.Update();
            var list = settings.FindProperty("additional");
            if (list.arraySize > 0) EditorGUILayout.LabelField("세로 부착물", EditorStyles.boldLabel);
            for (int i = 0; i < list.arraySize; i++)
            {
                var entry = list.GetArrayElementAtIndex(i);
                EditorGUI.BeginChangeCheck();
                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUILayout.PropertyField(entry.FindPropertyRelative("install"), GUIContent.none, GUILayout.Width(18));
                    AccessoryChoice(EditorGUILayout.GetControlRect(), entry, (i + 1) + " 종류");
                }
                bool entryChanged = EditorGUI.EndChangeCheck();
                int kind = entry.FindPropertyRelative("kind").enumValueIndex;
                if (entryChanged && entry.FindPropertyRelative("install").boolValue && (kind == 1 || kind == 2))
                {
                    for (int j = 0; j < list.arraySize; j++)
                    {
                        if (j == i) continue;
                        var other = list.GetArrayElementAtIndex(j);
                        int otherKind = other.FindPropertyRelative("kind").enumValueIndex;
                        if (otherKind == 1 || otherKind == 2)
                            other.FindPropertyRelative("install").boolValue = false;
                    }
                }
            }
            return settings.ApplyModifiedProperties();
        }

        public static void AccessoryChoice(Rect rect, SerializedProperty entry, string label)
        {
            var kind = entry.FindPropertyRelative("kind");
            var prefab = entry.FindPropertyRelative("mount").FindPropertyRelative("prefab");
            if (entry.FindPropertyRelative("useNonCameraHorizontalTypes").boolValue)
            {
                var previous = prefab.objectReferenceValue;
                TrafficPropCatalogEditor.DrawChoice(rect, prefab, true, "추가 위치 종류", false, true);
                if (previous != prefab.objectReferenceValue)
                {
                    kind.enumValueIndex = 0;
                    entry.FindPropertyRelative("typeKey").stringValue = prefab.objectReferenceValue ? prefab.objectReferenceValue.name : "";
                }
                return;
            }
            var candidates = new List<GameObject>(TrafficPropCatalogEditor.Candidates(false));
            var current = prefab.objectReferenceValue as GameObject;
            if (current && !candidates.Contains(current)) candidates.Add(current);
            var names = new List<string> { "없음", "Number 신호등", "Bar 신호등" };
            foreach (var item in candidates) names.Add(item.name);
            int selected = kind.enumValueIndex == 1 ? 1 : kind.enumValueIndex == 2 ? 2 :
                current ? candidates.IndexOf(current) + 3 : 0;
            EditorGUI.BeginChangeCheck();
            int choice = EditorGUI.Popup(rect, label, selected, names.ToArray());
            if (!EditorGUI.EndChangeCheck()) return;
            kind.enumValueIndex = choice == 1 ? 1 : choice == 2 ? 2 : 0;
            if (choice != 0) entry.FindPropertyRelative("install").boolValue = true;
            // Preserve the assigned socket and the previous custom prefab when switching signal kinds.
            if (choice == 0) prefab.objectReferenceValue = null;
            if (choice >= 3)
            {
                prefab.objectReferenceValue = candidates[choice - 3];
                entry.FindPropertyRelative("typeKey").stringValue = prefab.objectReferenceValue.name;
            }
        }
    }
}
