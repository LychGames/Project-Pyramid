using System;
using System.Collections.Generic;
using UnityEngine;

// Minimal host-authoritative placement replication scaffold.
// Works with any networking layer that can deliver messages/RPCs.

namespace Net
{
    [Serializable]
    public struct PlacementMessage
    {
        public string prefabKey;     // prefab registry key
        public Vector3 position;
        public Quaternion rotation;
        public uint netId;           // unique id assigned by host
    }

    public interface IPrefabRegistry
    {
        bool TryGetPrefab(string key, out GameObject prefab);
        bool TryGetKey(GameObject prefab, out string key);
    }

    public class PlacementReplicator : MonoBehaviour
    {
        [Header("Roles")]
        [SerializeField] bool isHost = true; // toggle for local testing
        [SerializeField] SimplePrefabRegistry registry;

        [Header("Debug Net Settings")]
        [Range(0, 500)] public int simulateLatencyMs = 0;
        [Range(0, 50)] public int packetDropPercent = 0;

        uint nextNetId = 1;

        // Hook generator events at runtime
        void OnEnable()
        {
            var gen = FindObjectOfType<SimpleLevelGenerator>();
            if (gen != null)
            {
                gen.ModulePlaced += OnHostModulePlaced;
            }
        }

        void OnDisable()
        {
            var gen = FindObjectOfType<SimpleLevelGenerator>();
            if (gen != null)
            {
                gen.ModulePlaced -= OnHostModulePlaced;
            }
        }

        void OnHostModulePlaced(GameObject prefab, Vector3 pos, Quaternion rot, Transform instance)
        {
            if (!isHost) return;
            if (registry == null || !registry.TryGetKey(prefab, out string key)) return;

            var msg = new PlacementMessage
            {
                prefabKey = key,
                position = QuantizePosition(pos),
                rotation = QuantizeRotation(rot),
                netId = nextNetId++
            };

            // Local host applies immediately; in a real stack you would send over the network
            BroadcastPlacement(msg);
        }

        // Entry point for networking layer to deliver placement to clients
        public void BroadcastPlacement(PlacementMessage msg)
        {
            // Simulate loss
            if (UnityEngine.Random.Range(0, 100) < packetDropPercent) return;
            if (simulateLatencyMs > 0)
            {
                // Only clients should apply locally. Host already spawned via generator.
                if (!isHost)
                {
                    _pending = msg; // single slot for demo; replace with queue if needed
                    Invoke(nameof(_DelayedApply), simulateLatencyMs / 1000f);
                }
            }
            else
            {
                if (!isHost)
                {
                    ApplyPlacement(msg);
                }
            }
        }

        PlacementMessage _pending;
        void _DelayedApply() => ApplyPlacement(_pending);

        void ApplyPlacement(PlacementMessage msg)
        {
            if (registry == null) return;
            if (!registry.TryGetPrefab(msg.prefabKey, out var prefab) || prefab == null) return;
            var go = Instantiate(prefab, msg.position, msg.rotation, transform);
            go.name = $"{msg.prefabKey}_{msg.netId}";
            // Tag with NetId for later state replication
            var nid = go.GetComponent<ReplicatedId>();
            if (!nid) nid = go.AddComponent<ReplicatedId>();
            nid.netId = msg.netId;
        }

        // Quantize to 60° lattice
        Vector3 QuantizePosition(Vector3 p)
        {
            // Only XZ; Y untouched
            var gen = FindObjectOfType<SimpleLevelGenerator>();
            float cell = 10f;
            if (gen != null)
            {
                var fi = typeof(SimpleLevelGenerator).GetField("cellSize", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                if (fi != null)
                {
                    object val = fi.GetValue(gen);
                    if (val is float f) cell = f;
                }
            }
            float x = Mathf.Round(p.x / cell) * cell;
            float z = Mathf.Round(p.z / cell) * cell;
            return new Vector3(x, p.y, z);
        }

        Quaternion QuantizeRotation(Quaternion r)
        {
            Vector3 e = r.eulerAngles;
            e.x = 0f; e.z = 0f;
            e.y = Mathf.Round(e.y / 60f) * 60f; // 0/60/120/180/240/300
            return Quaternion.Euler(e);
        }
    }

    public class ReplicatedId : MonoBehaviour
    {
        public uint netId;
    }
}


