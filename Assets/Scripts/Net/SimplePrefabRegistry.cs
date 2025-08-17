using System;
using System.Collections.Generic;
using UnityEngine;

namespace Net
{
    public class SimplePrefabRegistry : MonoBehaviour, IPrefabRegistry
    {
        [Serializable]
        public class Entry
        {
            public string key;
            public GameObject prefab;
        }

        [SerializeField] List<Entry> entries = new List<Entry>();
        readonly Dictionary<string, GameObject> keyToPrefab = new();
        readonly Dictionary<GameObject, string> prefabToKey = new();

        void Awake()
        {
            keyToPrefab.Clear(); prefabToKey.Clear();
            foreach (var e in entries)
            {
                if (e == null || string.IsNullOrEmpty(e.key) || e.prefab == null) continue;
                keyToPrefab[e.key] = e.prefab;
                prefabToKey[e.prefab] = e.key;
            }
        }

        public bool TryGetPrefab(string key, out GameObject prefab) => keyToPrefab.TryGetValue(key, out prefab);
        public bool TryGetKey(GameObject prefab, out string key) => prefabToKey.TryGetValue(prefab, out key);
    }
}


