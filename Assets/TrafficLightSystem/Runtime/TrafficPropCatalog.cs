using System;
using UnityEngine;

namespace ModularTrafficLights
{
    // Asset references survive Prefab Mode, scene changes and player builds.
    public sealed class TrafficPropCatalog : ScriptableObject
    {
        public GameObject[] horizontal = Array.Empty<GameObject>();
        public GameObject[] vertical = Array.Empty<GameObject>();
        public GameObject[] cameras = Array.Empty<GameObject>();
        private static TrafficPropCatalog loaded;
        public static TrafficPropCatalog Current => loaded ? loaded :
            (loaded = Resources.Load<TrafficPropCatalog>("TrafficPropCatalog"));

        public bool Contains(GameObject prefab, bool isHorizontal) =>
            prefab && Array.IndexOf(isHorizontal ? horizontal : vertical, prefab) >= 0;

        public bool IsCamera(GameObject prefab) => prefab && Array.IndexOf(cameras, prefab) >= 0;
    }
}
