using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace ModularTrafficLights.Editor
{
    public static class TrafficLightSelfTest
    {
        [MenuItem("Tools/Traffic Lights/Validate Selected Assembly")]
        public static void ValidateSelected()
        {
            var selected = Selection.activeGameObject;
            var manager = selected ? selected.GetComponentInParent<TrafficLightController>() : null;
            if (!manager) throw new InvalidOperationException("생성한 관리용 Prefab의 루트를 선택해주세요.");
            var errors = Validate(manager);
            if (errors.Count == 0) Debug.Log("Traffic light checks PASS: 1/2/3 heads, 4/8/12 lamps, pole parts, shared materials, pedestrian variants and rules.");
            else throw new InvalidOperationException(string.Join("\n", errors));
        }

        public static List<string> Validate(TrafficLightController source)
        {
            var errors = new List<string>();
            var clone = Object.Instantiate(source.gameObject);
            clone.name = "TL_Validation_Temporary";
            clone.hideFlags = HideFlags.HideAndDontSave;
            try
            {
                var manager = clone.GetComponent<TrafficLightController>();
                Require(manager.signals && manager.signals.signals.Length == 3, "Three vehicle signal bindings", errors);
                if (errors.Count > 0) return errors;
                for (int count = 1; count <= 3; count++)
                {
                    manager.SetSignalCount(count);
                    var activeSignals = clone.GetComponentsInChildren<SignalLightController>(false);
                    Require(activeSignals.Length == count, "Active heads for count " + count, errors);
                    Require(activeSignals.Sum(s => Enum.GetValues(typeof(SignalLamp)).Cast<SignalLamp>().Count(role => s.GetLamp(role).visual.renderer)) == count * 4,
                        "Lamp bindings for count " + count, errors);
                    foreach (var part in manager.pole.parts)
                        Require(part.part.activeInHierarchy == (count >= part.minimumSignals), "Pole/cable part " + part.part.name, errors);
                }
                manager.SetVehicleAspect(VehicleAspect.Red);
                var first = manager.GetSignal(0);
                var second = manager.GetSignal(1);
                Require(first.red.visual.renderer.sharedMaterial == second.red.visual.renderer.sharedMaterial, "Shared red material variant", errors);
                first.SetLamp(SignalLamp.Green, true, Color.cyan, 4);
                Require(first.green.on && !second.green.on, "Independent head/lamp state", errors);
                foreach (PedestrianVariant variant in Enum.GetValues(typeof(PedestrianVariant)))
                {
                    if (variant == PedestrianVariant.Bar && !manager.accessories.bar) continue;
                    manager.SetPedestrianVariant(variant);
                    int active = clone.GetComponentsInChildren<PedestrianSignalController>(false).Length;
                    Require(active == (variant == PedestrianVariant.None ? 0 : 1), "Pedestrian variant " + variant, errors);
                    manager.SetPedestrian(true, 9, 30);
                }
                Require(!TrafficLightRules.IsHorizontalProp("Width_Test"), "Case-sensitive WIDTH_ rule", errors);
                Require(!TrafficLightRules.IsVerticalProp("Lengh_Test"), "Exact LENGTH_ rule", errors);
                Require(TrafficLightRules.ClampSignCount(3, 1) == 1, "Sign count limit", errors);
                Require(TrafficLightRules.BarSegments(30, 30) == 9 && TrafficLightRules.BarSegments(0, 30) == 0, "Bar endpoints", errors);
                return errors;
            }
            finally { Object.DestroyImmediate(clone); }
        }

        private static void Require(bool passed, string check, List<string> errors)
        { if (!passed) errors.Add("FAIL: " + check); }
    }
}
