using UnityEditor;
using UnityEngine;

namespace ModularTrafficLights.Editor
{
    [CustomPropertyDrawer(typeof(AccessoryModule.Accessory))]
    internal sealed class VerticalAccessoryDrawer : PropertyDrawer
    {
        public override float GetPropertyHeight(SerializedProperty property, GUIContent label) => EditorGUIUtility.singleLineHeight;

        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            EditorGUI.BeginProperty(position, label, property);
            var toggle = new Rect(position.x, position.y, 18, position.height);
            EditorGUI.PropertyField(toggle, property.FindPropertyRelative("install"), GUIContent.none);
            var choice = new Rect(position.x + 22, position.y, position.width - 22, position.height);
            TrafficCompactInspector.AccessoryChoice(choice, property, "종류");
            EditorGUI.EndProperty();
        }
    }
}
