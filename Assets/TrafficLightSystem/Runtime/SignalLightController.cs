using System;
using UnityEngine;

namespace ModularTrafficLights
{
    [ExecuteAlways, DisallowMultipleComponent]
    public sealed class SignalLightController : MonoBehaviour
    {
        [Serializable]
        public sealed class Lamp
        {
            public LampVisual visual = new LampVisual();
            public bool on;
            [ColorUsage(false, true)] public Color color = Color.white;
            [Min(0)] public float intensity = 3;
        }

        [Tooltip("Generated URP signal material, shared by all lamps.")]
        public Material signalMaterial;
        public Lamp red = NewLamp(Color.red, true);
        public Lamp yellow = NewLamp(new Color(1, 0.65f, 0), false);
        public Lamp left = NewLamp(new Color(0, 1, 0.35f), false);
        public Lamp green = NewLamp(new Color(0, 1, 0.35f), false);

        private static Lamp NewLamp(Color color, bool on) => new Lamp { color = color, on = on };
        public Lamp GetLamp(SignalLamp role)
        {
            switch (role)
            {
                case SignalLamp.Red: return red;
                case SignalLamp.Yellow: return yellow;
                case SignalLamp.Left: return left;
                case SignalLamp.Green: return green;
                default: throw new ArgumentOutOfRangeException(nameof(role));
            }
        }

        public void SetLamp(SignalLamp role, bool on)
        {
            var lamp = GetLamp(role);
            lamp.on = on;
            ApplyOne(lamp);
        }

        public void SetLamp(SignalLamp role, bool on, Color color, float intensity = 3)
        {
            var lamp = GetLamp(role);
            lamp.on = on; lamp.color = color; lamp.intensity = Mathf.Max(0, intensity);
            ApplyOne(lamp);
        }

        public void SetAspect(VehicleAspect aspect)
        {
            for (int i = 0; i < 4; i++) GetLamp((SignalLamp)i).on = TrafficLightRules.LampOn(aspect, (SignalLamp)i);
            RefreshPreview();
        }

        private void ApplyOne(Lamp lamp)
        {
            if (lamp != null && lamp.visual != null && isActiveAndEnabled && gameObject.scene.IsValid())
                lamp.visual.Apply(signalMaterial, lamp.on, lamp.color, lamp.intensity);
        }

        public void RefreshPreview()
        {
            for (int i = 0; i < 4; i++) ApplyOne(GetLamp((SignalLamp)i));
        }

        public void ReleasePreview()
        {
            for (int i = 0; i < 4; i++) GetLamp((SignalLamp)i)?.visual?.Release();
        }

        private void OnEnable() => RefreshPreview();
        private void OnDisable() => ReleasePreview();
        private void OnValidate()
        {
            // No object creation, activation, or material mutations in OnValidate.
            for (int i = 0; i < 4; i++)
            {
                var lamp = GetLamp((SignalLamp)i);
                if (lamp != null) lamp.intensity = Mathf.Max(0, lamp.intensity);
            }
        }
    }
}
