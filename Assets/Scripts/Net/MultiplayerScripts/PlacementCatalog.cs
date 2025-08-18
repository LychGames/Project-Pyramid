using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(menuName = "Net/PlacementCatalog")]
public class PlacementCatalog : ScriptableObject
{
    public enum HallSubtype
    {
        Straight,
        Turn15_L, Turn15_R,
        Turn30_L, Turn30_R,
        Junction3_V,        // #1
        Junction3_Teeth30,  // #7
        Junction3_Y120,     // #8
        AdapterOverlap,      // #9–#11
        // Rooms
        StartRoom,
        SmallRoom,
        MediumRoom,
        LargeRoom,
        SpecialRoom
    }

    [System.Serializable]
    public class Entry
    {
        public int id;
        public GameObject prefab;

        // --- NEW META ---
        public HallSubtype subtype;
        public int anchorCount = 2;      // 2 or 3
        public float weight = 1f;        // selection weight within subtype pool
        public int depthGate = 0;        // min growth depth
        public int maxPerMap = 999;      // soft cap per map
        public bool isLandmark = false;  // reserved
    }

    [SerializeField] public List<Entry> entries = new();

    private Dictionary<int, GameObject> _map;
    private Dictionary<HallSubtype, List<Entry>> _bySubtype;
    private Dictionary<int, int> _spawnCounts;
    private static readonly List<Entry> _empty = new();

    private void OnEnable()
    {
        // Build lookup on enable so it's ready in play mode & domain reloads
        Rebuild();
    }

    public void Rebuild()
    {
        _map = new Dictionary<int, GameObject>(entries.Count);
        foreach (var e in entries)
        {
            if (e == null || e.prefab == null) continue;
            _map[e.id] = e.prefab;
        }
        BuildIndex();
        _spawnCounts = new();
    }

    public GameObject GetPrefab(int id)
    {
        if (_map == null || _map.Count == 0) Rebuild();
        return _map.TryGetValue(id, out var p) ? p : null;
    }

    private void BuildIndex()
    {
        _bySubtype = new();
        if (entries == null) return;
        for (int i = 0; i < entries.Count; i++)
        {
            var e = entries[i];
            if (e == null) continue;
            // If prefab carries RoomMeta, mirror its fields so designers can edit on the prefab
            var meta = e.prefab != null ? e.prefab.GetComponent<RoomMeta>() : null;
            if (meta != null)
            {
                e.subtype = meta.subtype;
                e.anchorCount = meta.anchorCount;
                e.weight = meta.catalogWeight;
                e.depthGate = meta.depthGate;
                e.maxPerMap = meta.maxPerMap;
                e.isLandmark = meta.isLandmark;
            }
            if (!_bySubtype.TryGetValue(e.subtype, out var list))
            {
                list = new List<Entry>();
                _bySubtype[e.subtype] = list;
            }
            list.Add(e);
        }
    }

    public IReadOnlyList<Entry> GetPool(HallSubtype s)
        => _bySubtype != null && _bySubtype.TryGetValue(s, out var list) ? list : _empty;

    public void NoteSpawn(int id)
    {
        if (_spawnCounts == null) _spawnCounts = new();
        _spawnCounts.TryGetValue(id, out var n);
        _spawnCounts[id] = n + 1;
    }

    public bool CanSpawn(Entry e, int depth)
    {
        if (e == null) return false;
        if (depth < e.depthGate) return false;
        if (_spawnCounts != null && _spawnCounts.TryGetValue(e.id, out var n) && n >= e.maxPerMap) return false;
        return true;
    }

    public Entry PickWeighted(HallSubtype s, int depth, System.Random rng)
    {
        if (_bySubtype == null || !_bySubtype.TryGetValue(s, out var pool) || pool == null || pool.Count == 0) return null;

        float total = 0f;
        for (int i = 0; i < pool.Count; i++)
        {
            var e = pool[i];
            if (!CanSpawn(e, depth)) continue;
            total += Mathf.Max(0.0001f, e.weight);
        }
        if (total <= 0f) return null;

        double r = rng.NextDouble() * total;
        for (int i = 0; i < pool.Count; i++)
        {
            var e = pool[i];
            if (!CanSpawn(e, depth)) continue;
            r -= Mathf.Max(0.0001f, e.weight);
            if (r <= 0.0) return e;
        }
        return null;
    }
}
