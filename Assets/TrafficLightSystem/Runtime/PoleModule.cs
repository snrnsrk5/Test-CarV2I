using System;
using UnityEngine;

namespace ModularTrafficLights
{
    [DisallowMultipleComponent]
    public sealed class PoleModule : MonoBehaviour
    {
        [Serializable]
        public sealed class CountedPart
        {
            public GameObject part;
            [Range(1, 3)] public int minimumSignals = 1;
        }

        [Tooltip("Original arm sections and support cables. Geometry is never stretched.")]
        public CountedPart[] parts = Array.Empty<CountedPart>();

        public void Apply(int count)
        {
            count = TrafficLightRules.ClampSignalCount(count);
            if (parts == null) return;
            foreach (var entry in parts)
                if (entry != null) TrafficObjectUtility.SetActive(entry.part, count >= entry.minimumSignals);
        }

        public void RefreshPreview() => GetComponent<TrafficLightController>()?.ApplyConfiguration();
    }
}
