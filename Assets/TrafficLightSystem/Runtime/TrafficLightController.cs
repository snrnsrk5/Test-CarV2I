using UnityEngine;

namespace ModularTrafficLights
{
    [ExecuteAlways, DisallowMultipleComponent]
    public sealed class TrafficLightController : MonoBehaviour
    {
        [SerializeField, Range(1, 3)] private int signalCount = 3;
        public PoleModule pole;
        public TrafficSignalModule signals;
        public TrafficSignModule signs;
        public AccessoryModule accessories;
        public int SignalCount => signalCount;

        public void SetSignalCount(int count)
        {
            signalCount = TrafficLightRules.ClampSignalCount(count);
            ApplyConfiguration();
        }

        public void ApplyConfiguration()
        {
            if (!gameObject.scene.IsValid()) return;
            signalCount = TrafficLightRules.ClampSignalCount(signalCount);
            pole?.Apply(signalCount);
            signals?.Apply(signalCount);
            signs?.Apply(signalCount);
            accessories?.Apply();
        }

        public SignalLightController GetSignal(int zeroBasedIndex) => signals ? signals.GetSignal(zeroBasedIndex) : null;
        public void SetVehicleAspect(VehicleAspect aspect) => signals?.SetAll(aspect, signalCount);
        public void SetPedestrian(bool walk, int remainingSeconds = 0, int totalSeconds = 30) =>
            accessories?.SetState(walk, remainingSeconds, totalSeconds);

        public void SetPedestrianVariant(PedestrianVariant variant)
        {
            if (!accessories) return;
            accessories.SetPedestrianVariant(variant);
        }

        private void OnEnable() => ApplyConfiguration();
        private void OnValidate() => signalCount = TrafficLightRules.ClampSignalCount(signalCount);
    }
}
