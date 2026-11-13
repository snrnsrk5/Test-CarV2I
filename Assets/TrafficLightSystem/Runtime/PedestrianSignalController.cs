using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace ModularTrafficLights
{
    [ExecuteAlways, DisallowMultipleComponent]
    public sealed class PedestrianSignalController : MonoBehaviour
    {
        [Serializable]
        public sealed class NumberSegment
        {
            [Range(1, 2)] public int sourceDigit = 1;
            public string sourceCode;
            public LampVisual visual = new LampVisual();
        }

        public Material signalMaterial;
        public bool walking = true;
        [Range(0, 99), Tooltip("수동 표시 값입니다. 원본 자동 반복 중에는 원본 타임라인이 표시됩니다.")] public int remainingSeconds = 12;
        [Min(1)] public int totalSeconds = 30;
        public bool blankLeadingZero = true;
        [ColorUsage(false, true)] public Color walkColor = new Color(0, 1, 0.35f);
        [ColorUsage(false, true)] public Color stopColor = Color.red;
        [Min(0)] public float intensity = 3;
        public LampVisual red = new LampVisual();
        public LampVisual green = new LampVisual();
        public NumberSegment[] numberSegments = Array.Empty<NumberSegment>();
        [Tooltip("Order: T1 through T9 from the inspected source model.")]
        public LampVisual[] barSegments = Array.Empty<LampVisual>();

        [Header("Number · 원본 애니메이션 반복 (Play Mode)")]
        public bool playOriginalNumberLoop = true;
        [Min(0.05f), Tooltip("원본의 빨강 전환 후 다음 카운트다운까지 대기 시간")]
        public float redHoldSeconds = 3;
        [SerializeField, HideInInspector] private bool displayBackgroundFixApplied;
        [NonSerialized] private Coroutine loop;
        [NonSerialized] private bool manualOverride;
        [NonSerialized] private bool hasLoopFrame;
        [NonSerialized] private OriginalPedestrianNumberLoop.Frame loopFrame;
        [NonSerialized] private Dictionary<Renderer, bool> originalVisibility;
        private static readonly WaitForSeconds HalfSecond = new WaitForSeconds(0.5f);
        private static readonly WaitForSeconds OneSecond = new WaitForSeconds(1);

        public bool IsOriginalLoopRunning => loop != null;
        public int DisplayedRemainingSeconds => hasLoopFrame ? Mathf.Max(0, 30 - Mathf.FloorToInt(loopFrame.time)) : remainingSeconds;

        public void RestartOriginalLoop()
        {
            StopOriginalLoop();
            manualOverride = false;
            RefreshPreview();
        }

        public void StopOriginalLoop()
        {
            if (loop != null) StopCoroutine(loop);
            loop = null;
            hasLoopFrame = false;
        }

        private IEnumerator OriginalLoop()
        {
            while (true)
            {
                var frames = OriginalPedestrianNumberLoop.Frames;
                for (int i = 0; i < frames.Length; i++)
                {
                    loopFrame = frames[i]; hasLoopFrame = true;
                    // Keep the serialized manual preview values unchanged.
                    ApplyVisuals();
                    float delay = i + 1 < frames.Length ? frames[i + 1].time - frames[i].time : Mathf.Max(0.05f, redHoldSeconds);
                    yield return delay == 0.5f ? HalfSecond : delay == 1 ? OneSecond : new WaitForSeconds(delay);
                }
            }
        }

        public void SetState(bool walk, int remaining = 0, int total = 30)
        {
            // Explicit external signal logic takes priority until re-enabled or restarted.
            StopOriginalLoop(); manualOverride = true;
            walking = walk;
            remainingSeconds = Mathf.Clamp(remaining, 0, 99);
            totalSeconds = Mathf.Max(1, total);
            RefreshPreview();
        }

        public void RefreshPreview()
        {
            if (!isActiveAndEnabled || !gameObject.scene.IsValid()) return;
            bool shouldLoop = Application.isPlaying && playOriginalNumberLoop && !manualOverride && numberSegments != null && numberSegments.Length > 0;
            if (!shouldLoop && loop != null) StopOriginalLoop();
            if (shouldLoop && loop == null) loop = StartCoroutine(OriginalLoop());
            ApplyVisuals();
        }

        private void ApplyVisuals()
        {
            bool redOn = hasLoopFrame ? loopFrame.red : !walking;
            bool greenOn = hasLoopFrame ? loopFrame.green : walking;
            red?.Apply(signalMaterial, redOn, stopColor, intensity);
            green?.Apply(signalMaterial, greenOn, walkColor, intensity);
            if (numberSegments != null)
                foreach (var item in numberSegments)
                {
                    if (item == null) continue;
                    int bit = TrafficLightRules.SegmentBit(item.sourceCode);
                    int mask = item.sourceDigit == 1 ? loopFrame.unitsMask : loopFrame.tensMask;
                    bool on = hasLoopFrame ? bit >= 0 && (mask & (1 << bit)) != 0 :
                        walking && TrafficLightRules.NumberSegmentOn(remainingSeconds, item.sourceDigit, item.sourceCode, blankLeadingZero);
                    // These polygons contain both LED dots and the black lens
                    // background. Keep the surface; switch emission only.
                    SetSegmentVisible(item.visual?.renderer, true);
                    item.visual?.Apply(signalMaterial, on, walkColor, intensity);
                }
            int lit = TrafficLightRules.BarSegments(remainingSeconds, totalSeconds, barSegments?.Length ?? 0);
            if (barSegments != null)
                for (int i = 0; i < barSegments.Length; i++)
                    barSegments[i]?.Apply(signalMaterial, walking && i < lit, walkColor, intensity);
        }

        public void ReleasePreview()
        {
            red?.Release(); green?.Release();
            if (numberSegments != null) foreach (var item in numberSegments) item?.visual?.Release();
            if (barSegments != null) foreach (var item in barSegments) item?.Release();
            if (originalVisibility != null)
            {
                foreach (var pair in originalVisibility) if (pair.Key) pair.Key.enabled = pair.Value;
                originalVisibility.Clear();
            }
        }

        private void SetSegmentVisible(Renderer target, bool visible)
        {
            if (!target) return;
            if (originalVisibility == null) originalVisibility = new Dictionary<Renderer, bool>();
            if (!originalVisibility.ContainsKey(target)) originalVisibility.Add(target, target.enabled);
            if (target.enabled != visible) target.enabled = visible;
        }

        private void OnEnable()
        {
            if (!displayBackgroundFixApplied)
            {
                // Migrate already loaded instances whose serialized value is 1s.
                redHoldSeconds = Mathf.Max(3, redHoldSeconds);
                displayBackgroundFixApplied = true;
            }
            manualOverride = false;
            RefreshPreview();
        }
        private void OnDisable() { StopOriginalLoop(); ReleasePreview(); }
        private void OnValidate()
        {
            remainingSeconds = Mathf.Clamp(remainingSeconds, 0, 99);
            totalSeconds = Mathf.Max(1, totalSeconds);
            intensity = Mathf.Max(0, intensity);
            redHoldSeconds = Mathf.Max(0.05f, redHoldSeconds);
        }
    }
}
