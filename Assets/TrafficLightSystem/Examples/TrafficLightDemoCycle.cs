using System.Collections;
using UnityEngine;

namespace ModularTrafficLights.Examples
{
    /// <summary>Optional game example. Not attached by the setup tool.</summary>
    public sealed class TrafficLightDemoCycle : MonoBehaviour
    {
        public TrafficLightController controller;
        public bool autoStart;
        [Min(1)] public int pedestrianSeconds = 15;
        [Min(1)] public int vehicleGreenSeconds = 20;
        private Coroutine running;

        private void OnEnable() { if (autoStart && Application.isPlaying) StartCycle(); }
        private void OnDisable() => StopCycle();
        public void StartCycle()
        {
            if (!Application.isPlaying || !controller || running != null) return;
            running = StartCoroutine(Cycle());
        }
        public void StopCycle()
        {
            if (running == null) return;
            StopCoroutine(running); running = null;
        }
        private IEnumerator Cycle()
        {
            var second = new WaitForSeconds(1);
            while (true)
            {
                int duration = Mathf.Max(1, pedestrianSeconds);
                controller.SetVehicleAspect(VehicleAspect.Red);
                for (int remaining = duration; remaining > 0; remaining--)
                {
                    controller.SetPedestrian(true, remaining, duration);
                    yield return second;
                }
                controller.SetPedestrian(false);
                controller.SetVehicleAspect(VehicleAspect.Green);
                yield return new WaitForSeconds(Mathf.Max(1, vehicleGreenSeconds));
                controller.SetVehicleAspect(VehicleAspect.Yellow);
                yield return new WaitForSeconds(3);
            }
        }
    }
}
