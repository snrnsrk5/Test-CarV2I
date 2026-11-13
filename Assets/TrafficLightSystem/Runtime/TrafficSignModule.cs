using System;
using UnityEngine;

namespace ModularTrafficLights
{
    [DisallowMultipleComponent]
    public sealed class TrafficSignModule : MonoBehaviour
    {
        [Serializable]
        public sealed class Zone
        {
            public Transform socket;
            public GameObject prefab;
            public Transform secondSocket;
            public GameObject secondPrefab;
            public bool disabled;
            public bool secondDisabled;
            public Transform additionalSocket;
            public GameObject additionalPrefab;
            public bool additionalDisabled;
        }

        [Range(0, 3)] public int signCount;
        // Keep old serialized mounts as the three reusable instance holders.
        [HideInInspector]
        public PropMount[] mounts = Array.Empty<PropMount>();
        [SerializeField, HideInInspector] private PropMount[] secondMounts = new PropMount[3];
        [SerializeField, HideInInspector] private PropMount[] additionalMounts = new PropMount[3];
        [SerializeField, HideInInspector] private bool configurationsMigrated;
        [SerializeField, HideInInspector] private Zone[] oneSign;
        [SerializeField, HideInInspector] private Zone[] twoSigns;
        [SerializeField, HideInInspector] private Zone[] threeSigns;

        public bool NeedsConfigurationMigration => !configurationsMigrated;

        public void EnsureConfigurations()
        {
            if (mounts == null) mounts = Array.Empty<PropMount>();
            if (mounts.Length < 3) Array.Resize(ref mounts, 3);
            for (int i = 0; i < mounts.Length; i++) if (mounts[i] == null) mounts[i] = new PropMount();
            if (secondMounts == null || secondMounts.Length != 3) Array.Resize(ref secondMounts, 3);
            for (int i = 0; i < secondMounts.Length; i++) if (secondMounts[i] == null) secondMounts[i] = new PropMount();
            if (additionalMounts == null || additionalMounts.Length != 3) Array.Resize(ref additionalMounts, 3);
            for (int i = 0; i < additionalMounts.Length; i++) if (additionalMounts[i] == null) additionalMounts[i] = new PropMount();
            if (configurationsMigrated) return;
            oneSign = CopyLegacy(1);
            twoSigns = CopyLegacy(2);
            threeSigns = CopyLegacy(3);
            configurationsMigrated = true;
        }

        private Zone[] CopyLegacy(int count)
        {
            var zones = new Zone[count];
            for (int i = 0; i < count; i++) zones[i] = new Zone { socket = mounts[i].socket, prefab = mounts[i].prefab };
            return zones;
        }

        public Zone[] GetZones(int count)
        {
            EnsureConfigurations();
            switch (count)
            {
                case 1: return oneSign;
                case 2: return twoSigns;
                case 3: return threeSigns;
                default: return Array.Empty<Zone>();
            }
        }

        public void Apply(int signalCount)
        {
            // The compact inspector has one count control: vehicle heads also
            // determine which saved zone layout is active. Keep signCount only
            // for serialized compatibility with older scenes and prefabs.
            int activeCount = TrafficLightRules.ClampSignalCount(signalCount);
            var zones = GetZones(activeCount);
            // Runtime safety for conflicting legacy data or direct API edits.
            // Inspector edits clear the opposite choice immediately, so the
            // most recently activated slot normally arrives here on its own.
            for (int i = 1; i < zones.Length; i++)
            {
                var zone = zones[i];
                if (zone == null || zone.disabled || !zone.prefab || zone.additionalDisabled || !zone.additionalPrefab) continue;
                if (!TrafficPropCatalog.Current || !TrafficPropCatalog.Current.IsCamera(zone.prefab)) continue;
                zone.additionalPrefab = null;
                zone.additionalDisabled = true;
            }
            for (int i = 0; i < mounts.Length; i++)
            {
                bool active = i < zones.Length && zones[i] != null;
                if (active)
                {
                    mounts[i].socket = zones[i].socket;
                    mounts[i].prefab = zones[i].prefab;
                }
                bool allowed = i == 0 || (TrafficPropCatalog.Current && TrafficPropCatalog.Current.IsCamera(mounts[i].prefab));
                mounts[i].Apply(active && !zones[i].disabled && allowed, true);
            }
            for (int i = 0; i < secondMounts.Length; i++)
            {
                bool active = i < zones.Length && zones[i] != null;
                if (active)
                {
                    secondMounts[i].socket = zones[i].secondSocket;
                    secondMounts[i].prefab = zones[i].secondPrefab;
                }
                secondMounts[i].Apply(active && !zones[i].secondDisabled, true);
            }
            for (int i = 0; i < additionalMounts.Length; i++)
            {
                bool active = i >= 1 && i < zones.Length && zones[i] != null;
                if (active)
                {
                    additionalMounts[i].socket = zones[i].additionalSocket;
                    additionalMounts[i].prefab = zones[i].additionalPrefab;
                }
                bool camera = TrafficPropCatalog.Current && TrafficPropCatalog.Current.IsCamera(additionalMounts[i].prefab);
                additionalMounts[i].Apply(active && !zones[i].additionalDisabled && !camera, true);
            }
        }

        public void RefreshPreview() => GetComponent<TrafficLightController>()?.ApplyConfiguration();
        private void OnValidate() => signCount = Math.Max(0, Math.Min(3, signCount));
    }
}
