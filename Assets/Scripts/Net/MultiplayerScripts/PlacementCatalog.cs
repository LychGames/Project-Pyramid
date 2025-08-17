using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(menuName = "Net/PlacementCatalog")]
public class PlacementCatalog : ScriptableObject
{
    [System.Serializable]
    public class Entry
    {
        public int id;
        public GameObject prefab;
    }

    [SerializeField] public List<Entry> entries = new();

    private Dictionary<int, GameObject> _map;

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
    }

    public GameObject GetPrefab(int id)
    {
        if (_map == null || _map.Count == 0) Rebuild();
        return _map.TryGetValue(id, out var p) ? p : null;
    }
}
