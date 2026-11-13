using System;
using System.Collections.Generic;
using UnityEngine;

namespace ModularTrafficLights
{
    /// <summary>URP runtime shared variants; no Renderer.material or runtime property blocks.</summary>
    public static class SignalMaterialCache
    {
        private const int MaximumIdleVariants = 32;
        private static readonly int TintId = Shader.PropertyToID("_EmissionTint");
        private static readonly int PowerId = Shader.PropertyToID("_EmissionPower");
        private static readonly Dictionary<Key, Lease> Items = new Dictionary<Key, Lease>();
        private static long clock;

        private struct Key : IEquatable<Key>
        {
            public int source;
            public Color tint;
            public float power;
            public bool Equals(Key other) => source == other.source && tint.Equals(other.tint) && power.Equals(other.power);
            public override bool Equals(object obj) => obj is Key other && Equals(other);
            public override int GetHashCode() => unchecked((source * 397 ^ tint.GetHashCode()) * 397 ^ power.GetHashCode());
        }

        public sealed class Lease
        {
            internal Material material;
            internal int references;
            internal long lastUsed;
            public Material Material => material;
        }

        internal static Lease Acquire(Material source, bool on, Color tint, float power)
        {
            if (!source) return null;
            power = float.IsNaN(power) || float.IsInfinity(power) ? 0 : Mathf.Max(0, power);
            if (!on || power == 0) { tint = Color.black; power = 0; }
            tint.a = 1;
            var key = new Key { source = source.GetInstanceID(), tint = tint, power = power };
            if (!Items.TryGetValue(key, out var lease) || !lease.material)
            {
                var mat = new Material(source)
                {
                    name = "TL_State_" + Items.Count,
                    hideFlags = HideFlags.HideAndDontSave
                };
                mat.SetColor(TintId, tint);
                mat.SetFloat(PowerId, power);
                lease = new Lease { material = mat };
                Items[key] = lease;
            }
            lease.references++;
            lease.lastUsed = ++clock;
            TrimIdle();
            return lease;
        }

        internal static void Release(Lease lease)
        {
            if (lease == null) return;
            lease.references = Math.Max(0, lease.references - 1);
            lease.lastUsed = ++clock;
            TrimIdle();
        }

        private static void TrimIdle()
        {
            int idle = 0;
            foreach (var pair in Items) if (pair.Value.references == 0) idle++;
            while (idle > MaximumIdleVariants)
            {
                Key oldest = default;
                long stamp = long.MaxValue;
                foreach (var pair in Items)
                    if (pair.Value.references == 0 && pair.Value.lastUsed < stamp)
                    { oldest = pair.Key; stamp = pair.Value.lastUsed; }
                DestroyMaterial(Items[oldest].material);
                Items.Remove(oldest);
                idle--;
            }
        }

        private static void DestroyMaterial(Material material)
        {
            if (!material) return;
            if (Application.isPlaying) UnityEngine.Object.Destroy(material);
            else UnityEngine.Object.DestroyImmediate(material);
        }

        public static void Clear()
        {
            foreach (var pair in Items) DestroyMaterial(pair.Value.material);
            Items.Clear();
            clock = 0;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Reset()
        {
            Clear();
            Application.quitting -= Clear;
            Application.quitting += Clear;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void ReapplyWhenSceneReloadIsDisabled()
        {
            // Also covers entering Play Mode with domain/scene reload disabled.
            foreach (var item in UnityEngine.Object.FindObjectsOfType<SignalLightController>(true))
            { item.ReleasePreview(); item.RefreshPreview(); }
            foreach (var item in UnityEngine.Object.FindObjectsOfType<PedestrianSignalController>(true))
            { item.ReleasePreview(); item.RefreshPreview(); }
        }
    }

    [Serializable]
    public sealed class LampVisual
    {
        public Renderer renderer;
        [NonSerialized] private Renderer appliedRenderer;
        [NonSerialized] private Material original;
        [NonSerialized] private Material lastSource;
        [NonSerialized] private Color lastColor;
        [NonSerialized] private float lastPower;
        [NonSerialized] private bool lastOn;
        [NonSerialized] private SignalMaterialCache.Lease lease;
#if UNITY_EDITOR
        [NonSerialized] private MaterialPropertyBlock editorBlock;
        [NonSerialized] private bool editorPreview;
#endif

        public void Apply(Material source, bool on, Color tint, float power)
        {
            if (appliedRenderer != renderer) Release();
            if (!renderer || !source) { Release(); return; }
#if UNITY_EDITOR
            if (!Application.isPlaying)
            {
                // Editor preview keeps a durable asset on the Renderer so scene
                // and prefab saves never serialize transient runtime materials.
                if (!appliedRenderer) { appliedRenderer = renderer; original = renderer.sharedMaterial; }
                if (renderer.sharedMaterial != source) renderer.sharedMaterial = source;
                if (editorBlock == null) editorBlock = new MaterialPropertyBlock();
                renderer.GetPropertyBlock(editorBlock);
                editorBlock.SetColor("_EmissionTint", on ? tint : Color.black);
                editorBlock.SetFloat("_EmissionPower", on ? Mathf.Max(0, power) : 0);
                renderer.SetPropertyBlock(editorBlock);
                editorPreview = true;
                return;
            }
            if (editorPreview)
            {
                appliedRenderer.SetPropertyBlock(null);
                editorPreview = false;
            }
#endif
            if (lease != null && lease.Material && lastSource == source && lastOn == on &&
                lastColor.Equals(tint) && lastPower.Equals(power) && renderer.sharedMaterial == lease.Material) return;
            if (!appliedRenderer) { appliedRenderer = renderer; original = renderer.sharedMaterial; }
            // Acquire first, so an unchanged shared variant remains referenced.
            var next = SignalMaterialCache.Acquire(source, on, tint, power);
            SignalMaterialCache.Release(lease);
            lease = next;
            renderer.sharedMaterial = lease.Material;
            lastSource = source; lastOn = on; lastColor = tint; lastPower = power;
        }

        public void Release()
        {
#if UNITY_EDITOR
            if (appliedRenderer && editorPreview)
            {
                appliedRenderer.SetPropertyBlock(null);
                appliedRenderer.sharedMaterial = original;
            }
            editorPreview = false;
#endif
            if (appliedRenderer && lease != null && appliedRenderer.sharedMaterial == lease.Material)
                appliedRenderer.sharedMaterial = original;
            SignalMaterialCache.Release(lease);
            lease = null; appliedRenderer = null; original = null; lastSource = null;
        }
    }
}
