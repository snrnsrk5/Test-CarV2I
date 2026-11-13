using System;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

namespace ModularTrafficLights.Editor
{
    internal abstract class TrafficModuleEditor : UnityEditor.Editor
    {
        protected abstract void Refresh(Component component);

        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            EditorGUI.BeginChangeCheck();
            DrawDefaultInspector();
            bool changed = EditorGUI.EndChangeCheck();
            serializedObject.ApplyModifiedProperties();
            if (changed) QueueRefresh();
            if (GUILayout.Button("설정 적용 / 미리보기 갱신")) QueueRefresh();
        }

        protected void QueueRefresh()
        {
            var captured = targets;
            // Inspector edits are applied on the main editor loop, not OnValidate.
            EditorApplication.delayCall += () =>
            {
                foreach (var item in captured)
                {
                    var component = item as Component;
                    if (!component || !component.gameObject.scene.IsValid()) continue;
                    Undo.RegisterFullObjectHierarchyUndo(component.gameObject, "Configure traffic light");
                    Refresh(component);
                    PrefabUtility.RecordPrefabInstancePropertyModifications(component);
                    EditorUtility.SetDirty(component);
                    if (!Application.isPlaying) EditorSceneManager.MarkSceneDirty(component.gameObject.scene);
                }
                SceneView.RepaintAll();
            };
        }
    }

    [CustomEditor(typeof(TrafficLightController)), CanEditMultipleObjects]
    internal sealed class TrafficLightControllerEditor : TrafficModuleEditor
    {
        protected override void Refresh(Component item) => ((TrafficLightController)item).ApplyConfiguration();
        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            EditorGUILayout.PropertyField(serializedObject.FindProperty("signalCount"), new GUIContent("신호등 개수"));
            bool changed = serializedObject.ApplyModifiedProperties();
            if (targets.Length == 1)
            {
                var manager = (TrafficLightController)target;
                changed |= TrafficCompactInspector.Signs(manager.signs);
                changed |= TrafficCompactInspector.Accessories(manager.accessories);
            }
            if (changed) QueueRefresh();
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("차량 신호등 불빛 · 일괄 선택", EditorStyles.boldLabel);
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("빨강")) SetAspect(VehicleAspect.Red);
                if (GUILayout.Button("노랑")) SetAspect(VehicleAspect.Yellow);
                if (GUILayout.Button("초록")) SetAspect(VehicleAspect.Green);
                if (GUILayout.Button("좌회전")) SetAspect(VehicleAspect.Left);
                if (GUILayout.Button("끄기")) SetAspect(VehicleAspect.AllOff);
            }
        }

        private void SetAspect(VehicleAspect aspect)
        {
            foreach (Object item in targets)
            {
                var manager = (TrafficLightController)item;
                Undo.RegisterFullObjectHierarchyUndo(manager.gameObject, "Change traffic signal lights");
                manager.SetVehicleAspect(aspect);
                if (manager.signals && manager.signals.signals != null)
                {
                    for (int i = 0; i < Math.Min(manager.SignalCount, manager.signals.signals.Length); i++)
                    {
                        var signal = manager.signals.signals[i]?.signal;
                        if (!signal) continue;
                        EditorUtility.SetDirty(signal);
                        PrefabUtility.RecordPrefabInstancePropertyModifications(signal);
                    }
                }
                if (!Application.isPlaying && manager.gameObject.scene.IsValid())
                    EditorSceneManager.MarkSceneDirty(manager.gameObject.scene);
            }
            SceneView.RepaintAll();
        }
    }

    [CustomEditor(typeof(SignalLightController)), CanEditMultipleObjects]
    internal sealed class SignalLightEditor : TrafficModuleEditor
    { protected override void Refresh(Component item) => ((SignalLightController)item).RefreshPreview(); }

    [CustomEditor(typeof(PedestrianSignalController)), CanEditMultipleObjects]
    internal sealed class PedestrianEditor : TrafficModuleEditor
    {
        protected override void Refresh(Component item) => ((PedestrianSignalController)item).RefreshPreview();
        public override void OnInspectorGUI()
        {
            base.OnInspectorGUI();
            EditorGUILayout.HelpBox("Number는 Play Mode에서 활성화하면 원본 숫자/점멸 순서를 자동 반복합니다. Red Hold Seconds는 빨강 유지 시간입니다. SetState API를 호출하면 외부 제어가 우선하며, 재활성화 또는 아래 버튼으로 자동 반복을 재개합니다.", MessageType.Info);
            using (new EditorGUI.DisabledScope(!Application.isPlaying))
                if (GUILayout.Button("Number 원본 반복 · 처음부터 재생"))
                    foreach (Object item in targets) ((PedestrianSignalController)item).RestartOriginalLoop();
        }
    }

    [CustomEditor(typeof(PoleModule)), CanEditMultipleObjects]
    internal sealed class PoleEditor : TrafficModuleEditor
    {
        protected override void Refresh(Component item) => ((PoleModule)item).RefreshPreview();
        public override void OnInspectorGUI() { }
    }

    [CustomEditor(typeof(TrafficSignalModule)), CanEditMultipleObjects]
    internal sealed class SignalsEditor : TrafficModuleEditor
    {
        protected override void Refresh(Component item) => ((TrafficSignalModule)item).RefreshPreview();
        public override void OnInspectorGUI() { }
    }

    [CustomEditor(typeof(TrafficSignModule)), CanEditMultipleObjects]
    internal sealed class SignsEditor : TrafficModuleEditor
    {
        protected override void Refresh(Component item) => ((TrafficSignModule)item).RefreshPreview();
        public override void OnInspectorGUI()
        {
            if (targets.Length == 1 && TrafficCompactInspector.Signs((TrafficSignModule)target)) QueueRefresh();
        }
    }

    [CustomEditor(typeof(AccessoryModule)), CanEditMultipleObjects]
    internal sealed class AccessoriesEditor : TrafficModuleEditor
    {
        protected override void Refresh(Component item) => ((AccessoryModule)item).RefreshPreview();
        public override void OnInspectorGUI()
        {
            if (targets.Length == 1 && TrafficCompactInspector.Accessories((AccessoryModule)target)) QueueRefresh();
        }
    }

    [InitializeOnLoad]
    internal static class TrafficEditorLifecycle
    {
        static TrafficEditorLifecycle()
        {
            AssemblyReloadEvents.beforeAssemblyReload += ReleaseAll;
            EditorApplication.quitting += ReleaseAll;
            Undo.undoRedoPerformed += RefreshAfterUndo;
        }

        private static void ReleaseAll()
        {
            foreach (var signal in Object.FindObjectsOfType<SignalLightController>(true)) signal.ReleasePreview();
            foreach (var pedestrian in Object.FindObjectsOfType<PedestrianSignalController>(true)) pedestrian.ReleasePreview();
            SignalMaterialCache.Clear();
        }

        private static void RefreshAfterUndo()
        {
            EditorApplication.delayCall += () =>
            {
                foreach (var root in Object.FindObjectsOfType<TrafficLightController>(true)) root.ApplyConfiguration();
                foreach (var signal in Object.FindObjectsOfType<SignalLightController>(true)) signal.RefreshPreview();
                foreach (var pedestrian in Object.FindObjectsOfType<PedestrianSignalController>(true)) pedestrian.RefreshPreview();
                SceneView.RepaintAll();
            };
        }
    }
}
