using UnityEditor;
using UnityEngine;

namespace ModularTrafficLights.Editor
{
    [CustomEditor(typeof(PedestrianPoleController)), CanEditMultipleObjects]
    internal sealed class PedestrianPoleInspector : TrafficModuleEditor
    {
        protected override void Refresh(Component component) => ((PedestrianPoleController)component).ApplyConfiguration();
        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            var variant = serializedObject.FindProperty("selectedVariant");
            EditorGUILayout.LabelField("신호등 종류", EditorStyles.boldLabel);
            EditorGUI.showMixedValue = variant.hasMultipleDifferentValues;
            EditorGUI.BeginChangeCheck();
            int selected = GUILayout.Toolbar(variant.hasMultipleDifferentValues ? -1 : variant.enumValueIndex, new[] { "숫자형 (Number)", "막대형 (Bar)" });
            if (EditorGUI.EndChangeCheck() && selected >= 0) variant.enumValueIndex = selected;
            EditorGUI.showMixedValue = false;
            if (variant.hasMultipleDifferentValues)
            {
                Rotation("numberQuarterTurns", "숫자형 신호등 회전");
                Rotation("barQuarterTurns", "막대형 신호등 회전");
            }
            else if (variant.enumValueIndex == 0) Rotation("numberQuarterTurns", "숫자형 신호등 회전");
            else Rotation("barQuarterTurns", "막대형 신호등 회전");
            EditorGUILayout.PropertyField(serializedObject.FindProperty("showGuardRetainingBand4"), new GUIContent("Guard retaining band 4 표시"));
            if (serializedObject.ApplyModifiedProperties()) QueueRefresh();
        }

        private void Rotation(string field, string label)
        {
            var property = serializedObject.FindProperty(field);
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.PrefixLabel(label);
                EditorGUI.showMixedValue = property.hasMultipleDifferentValues;
                EditorGUI.BeginChangeCheck();
                int quarterTurns = Mathf.RoundToInt(GUILayout.HorizontalSlider(property.intValue, 0, 3));
                if (EditorGUI.EndChangeCheck()) property.intValue = quarterTurns;
                EditorGUI.showMixedValue = false;
                GUILayout.Label((property.intValue * 90) + "°", GUILayout.Width(40));
            }
        }

        [InitializeOnLoadMethod]
        private static void BindUndo()
        {
            Undo.undoRedoPerformed += () => EditorApplication.delayCall += () =>
            {
                foreach (var pole in Object.FindObjectsOfType<PedestrianPoleController>(true)) pole.ApplyConfiguration();
            };
        }
    }
}
