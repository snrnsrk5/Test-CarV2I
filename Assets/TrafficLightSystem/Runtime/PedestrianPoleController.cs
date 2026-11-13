using System.Collections;
using UnityEngine;

namespace ModularTrafficLights
{
    [ExecuteAlways, DisallowMultipleComponent]
    public sealed class PedestrianPoleController : MonoBehaviour
    {
        public enum SignalVariant { Number, Bar }
        public SignalVariant selectedVariant = SignalVariant.Number;
        [HideInInspector] public Transform numberPivot;
        [HideInInspector] public Transform barPivot;
        [HideInInspector] public GameObject guardRetainingBand4;
        [HideInInspector] public PedestrianSignalController numberSignal;
        [HideInInspector] public PedestrianSignalController barSignal;
        [Range(0, 3)] public int numberQuarterTurns;
        [Range(0, 3)] public int barQuarterTurns;
        public bool showGuardRetainingBand4 = true;
        private Coroutine barLoop;
        private static readonly WaitForSeconds OneSecond = new WaitForSeconds(1);

        public void ApplyConfiguration()
        {
            if (selectedVariant != SignalVariant.Number && selectedVariant != SignalVariant.Bar) selectedVariant = SignalVariant.Number;
            numberQuarterTurns = Mathf.Clamp(numberQuarterTurns, 0, 3);
            barQuarterTurns = Mathf.Clamp(barQuarterTurns, 0, 3);
            if (numberPivot) numberPivot.localRotation = Quaternion.AngleAxis(numberQuarterTurns * 90, Vector3.up);
            if (barPivot) barPivot.localRotation = Quaternion.AngleAxis(barQuarterTurns * 90, Vector3.up);
            SetVisible(numberPivot, selectedVariant == SignalVariant.Number);
            SetVisible(barPivot, selectedVariant == SignalVariant.Bar);
            if (guardRetainingBand4 && guardRetainingBand4.activeSelf != showGuardRetainingBand4)
                guardRetainingBand4.SetActive(showGuardRetainingBand4);
            bool animateBar = Application.isPlaying && isActiveAndEnabled && selectedVariant == SignalVariant.Bar && barSignal && barSignal.isActiveAndEnabled;
            if (animateBar && barLoop == null) barLoop = StartCoroutine(BarCycle());
            else if (!animateBar) StopBarCycle();
        }

        private static void SetVisible(Transform target, bool visible)
        {
            if (target && target.gameObject.activeSelf != visible) target.gameObject.SetActive(visible);
        }

        private void OnEnable() => ApplyConfiguration();
        private void OnDisable() => StopBarCycle();
        private void OnValidate()
        {
            numberQuarterTurns = Mathf.Clamp(numberQuarterTurns, 0, 3);
            barQuarterTurns = Mathf.Clamp(barQuarterTurns, 0, 3);
        }

        private void StopBarCycle()
        {
            if (barLoop != null) StopCoroutine(barLoop);
            barLoop = null;
        }

        private IEnumerator BarCycle()
        {
            // The hidden Number face is inactive, so Bar owns its own timer.
            while (barSignal && selectedVariant == SignalVariant.Bar)
            {
                for (int remaining = 30; remaining > 0; remaining--)
                {
                    barSignal.SetState(true, remaining, 30);
                    yield return OneSecond;
                }
                barSignal.SetState(false, 0, 30);
                yield return new WaitForSeconds(Mathf.Max(.05f, barSignal.redHoldSeconds));
            }
            barLoop = null;
        }
    }
}
