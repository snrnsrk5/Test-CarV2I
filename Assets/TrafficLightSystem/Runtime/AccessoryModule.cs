using System;
using System.Collections.Generic;
using UnityEngine;

namespace ModularTrafficLights
{
    [DisallowMultipleComponent]
    public sealed class AccessoryModule : MonoBehaviour
    {
        public enum AccessoryKind { Prefab, Number, Bar }

        [Serializable]
        public sealed class Accessory
        {
            public AccessoryKind kind;
            [Tooltip("Unique type key. Only the first enabled entry for a type can be installed.")]
            public string typeKey;
            public bool install = true;
            public PropMount mount = new PropMount();
            [HideInInspector] public bool useNonCameraHorizontalTypes;
        }

        [HideInInspector] public Transform pedestrianSocket;
        [HideInInspector] public PedestrianVariant pedestrian = PedestrianVariant.Number;
        [SerializeField, HideInInspector] private bool verticalSignalsMigrated;
        [SerializeField, HideInInspector] private bool additionalSignalsMigrated;

        // Retain old serialized bindings for existing scenes and the setup tool.
        // Installation and sockets are now controlled exclusively by Additional.
        [HideInInspector] public bool installNumber = true;
        [HideInInspector] public Transform numberSocket;
        [HideInInspector] public PedestrianSignalController number;

        [HideInInspector] public bool installBar;
        [HideInInspector] public Transform barSocket;
        [HideInInspector] public PedestrianSignalController bar;
        [Header("세로 기둥 · 추가 프랍 및 신호등")]
        [Tooltip("Number, Bar와 LENGTH_ 추가 Prefab을 이 목록에서 함께 관리합니다.")]
        public Accessory[] additional = Array.Empty<Accessory>();

        public PedestrianSignalController ActivePedestrian => installNumber ? number : installBar ? bar : null;

        private void MigrateVerticalSignals()
        {
            if (verticalSignalsMigrated) return;
            installNumber = pedestrian == PedestrianVariant.Number;
            installBar = pedestrian == PedestrianVariant.Bar;
            if (!numberSocket) numberSocket = pedestrianSocket;
            if (!barSocket) barSocket = pedestrianSocket;
            verticalSignalsMigrated = true;
        }

        public bool NeedsAdditionalMigration => !additionalSignalsMigrated;

        public void EnsureAdditionalEntries()
        {
            MigrateVerticalSignals();
            if (additionalSignalsMigrated) return;
            var entries = new List<Accessory>(additional ?? Array.Empty<Accessory>());
            if (!entries.Exists(e => e != null && e.kind == AccessoryKind.Number))
                entries.Add(new Accessory { kind = AccessoryKind.Number, install = installNumber,
                    mount = new PropMount { socket = numberSocket ? numberSocket : pedestrianSocket } });
            if (!entries.Exists(e => e != null && e.kind == AccessoryKind.Bar))
                entries.Add(new Accessory { kind = AccessoryKind.Bar, install = installBar,
                    mount = new PropMount { socket = barSocket ? barSocket : pedestrianSocket } });
            additional = entries.ToArray();
            additionalSignalsMigrated = true;
        }

        public void SetPedestrianVariant(PedestrianVariant variant)
        {
            EnsureAdditionalEntries();
            pedestrian = variant;
            bool found = false;
            var wanted = variant == PedestrianVariant.Number ? AccessoryKind.Number : AccessoryKind.Bar;
            foreach (var entry in additional)
            {
                if (entry == null || entry.kind == AccessoryKind.Prefab) continue;
                entry.install = variant != PedestrianVariant.None && entry.kind == wanted && !found;
                if (entry.install) found = true;
            }
            if (!found && variant != PedestrianVariant.None)
            {
                var entries = new List<Accessory>(additional);
                entries.Add(new Accessory { kind = wanted, mount = new PropMount
                    { socket = wanted == AccessoryKind.Number ? numberSocket : barSocket } });
                additional = entries.ToArray();
            }
            Apply();
        }

        public void Apply()
        {
            EnsureAdditionalEntries();
            Accessory numberEntry = null, barEntry = null;
            var installed = new HashSet<string>(StringComparer.Ordinal);
            foreach (var entry in additional)
            {
                if (entry == null) continue;
                if (entry.kind != AccessoryKind.Prefab)
                {
                    // Old custom-prefab instances owned by a repurposed slot are
                    // disabled, never confused with the built-in signal objects.
                    entry.mount?.Apply(false, false);
                    if (!entry.install || entry.mount == null || !entry.mount.socket) continue;
                    if (entry.kind == AccessoryKind.Number && numberEntry == null) numberEntry = entry;
                    if (entry.kind == AccessoryKind.Bar && barEntry == null) barEntry = entry;
                    continue;
                }
                bool eligible = entry.install && !string.IsNullOrWhiteSpace(entry.typeKey) &&
                    entry.mount != null && entry.mount.socket && entry.mount.HasValidPrefix(entry.useNonCameraHorizontalTypes);
                if (entry.useNonCameraHorizontalTypes && entry.mount != null && TrafficPropCatalog.Current && TrafficPropCatalog.Current.IsCamera(entry.mount.prefab))
                    eligible = false;
                bool install = eligible && installed.Add(entry.typeKey);
                entry.mount?.Apply(install, entry.useNonCameraHorizontalTypes);
            }
            // Resolve conflicting older scene data deterministically. Inspector
            // changes turn off the other kind before Apply, so a newly chosen
            // Bar still takes precedence over an already configured Number.
            if (numberEntry != null && barEntry != null)
            {
                foreach (var entry in additional)
                    if (entry != null && entry.kind == AccessoryKind.Bar) entry.install = false;
                barEntry = null;
            }
            installNumber = numberEntry != null;
            installBar = barEntry != null;
            if (numberEntry != null) numberSocket = numberEntry.mount.socket;
            if (barEntry != null) barSocket = barEntry.mount.socket;
            ApplySignal(number, numberEntry);
            ApplySignal(bar, barEntry);
        }

        private static void ApplySignal(PedestrianSignalController signal, Accessory entry)
        {
            if (!signal) return;
            if (entry != null) TrafficObjectUtility.Attach(signal.transform, entry.mount.socket);
            TrafficObjectUtility.SetActive(signal.gameObject, entry != null);
            if (entry != null) signal.RefreshPreview();
        }

        public void SetState(bool walk, int remaining, int total = 30)
        {
            // Keep both variants synchronized so changing the variant preserves state.
            number?.SetState(walk, remaining, total);
            bar?.SetState(walk, remaining, total);
        }

        public void RefreshPreview() => GetComponent<TrafficLightController>()?.ApplyConfiguration();
    }
}
