using System;
using UnityEngine;

namespace ModularTrafficLights
{
    [DisallowMultipleComponent]
    public sealed class TrafficSignalModule : MonoBehaviour
    {
        [Serializable]
        public sealed class SignalMount
        {
            public Transform socket;
            public SignalLightController signal;
        }

        public SignalMount[] signals = Array.Empty<SignalMount>();

        public void Apply(int count)
        {
            count = TrafficLightRules.ClampSignalCount(count);
            if (signals == null) return;
            for (int i = 0; i < signals.Length; i++)
            {
                var entry = signals[i];
                if (entry == null || !entry.signal) continue;
                if (entry.socket) TrafficObjectUtility.Attach(entry.signal.transform, entry.socket);
                TrafficObjectUtility.SetActive(entry.signal.gameObject, i < count);
                if (i < count) entry.signal.RefreshPreview();
            }
        }

        public SignalLightController GetSignal(int zeroBasedIndex)
        {
            if (signals == null || zeroBasedIndex < 0 || zeroBasedIndex >= signals.Length)
                throw new ArgumentOutOfRangeException(nameof(zeroBasedIndex));
            return signals[zeroBasedIndex]?.signal;
        }

        public void SetAll(VehicleAspect aspect, int activeCount)
        {
            if (signals == null) return;
            for (int i = 0; i < Math.Min(activeCount, signals.Length); i++) signals[i]?.signal?.SetAspect(aspect);
        }

        public void RefreshPreview() => GetComponent<TrafficLightController>()?.ApplyConfiguration();
    }
}
