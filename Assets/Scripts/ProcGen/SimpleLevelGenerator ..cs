using System.Collections.Generic;
using UnityEngine;

public class SimpleLevelGenerator : MonoBehaviour
{
    [Header("Prefabs")]
    public GameObject startRoomPrefab;
    public List<GameObject> roomPrefabs = new List<GameObject>();

    [Header("Hierarchy")]
    public Transform levelRoot;

    [Header("Generation")]
    [Min(1)] public int maxRooms = 60;
    public bool autoGenerateOnPlay = true;
    public bool randomizeSeed = true;
    public int seedInput = 0;

    [Header("60° Lattice")]
    [Min(0.1f)] public float cellSize = 2.5f;  // lattice step C
    public bool snapYawTo30 = true;
    public bool snapPosToLattice = true;

    [Header("Overlap")]
    public LayerMask placementCollisionMask; // LevelGeo only
    public bool allowPortalOverlap = true;
    public Vector2 portalSize = new Vector2(2.0f, 3.5f);   // (doorW, doorH)
    public float portalSlabThickness = 0.02f;
    public bool disableOverlapForDebug = false;

    [Header("Flow / Variety")]
    [Min(0)] public int corridorFirstSteps = 14;
    [Min(0)] public int minHallsBetweenRooms = 7;
    [Range(0, 1f)] public float preferTurnBias = 0.55f;
    [Min(0)] public int maxConsecutiveSamePrefab = 2;
    [Min(0)] public int maxConsecutiveSameCategory = 4;
    public float hallwayClearance = 1.0f;

    [Header("Room Limits")]
    public int maxRoomsPlaced = 12;      // non-hall count
    [Range(0f, 1f)] public float roomSpawnChance = 0.35f;

    [Header("Door Capping (visual)")]
    public GameObject doorCapPrefab;
    [Min(0f)] public float doorCapDepth = 0.10f;
    public bool sealOpenAnchorsAtEnd = true;

    [Header("Debug")]
    public bool verboseSelection = false;
    public bool logSteps = true;

    // internals
    private System.Random _rng;
    private int _usedSeed;
    private readonly List<GameObject> _spawned = new List<GameObject>();
    private readonly List<DoorAnchor> _open = new List<DoorAnchor>();
    private readonly Dictionary<GameObject, int> _spawnCounts = new Dictionary<GameObject, int>();
    private readonly List<Bounds> _placedAabbs = new List<Bounds>();
    private readonly Dictionary<GameObject, Bounds> _prefabAabb = new Dictionary<GameObject, Bounds>();

    private int _hallsSinceRoom = 0, _corridorStepsLeft = 0, _nonHallCount = 0;
    private int _samePrefab = 0, _sameCat = 0; private GameObject _lastPrefab; private RoomCategory _lastCat = RoomCategory.None;

    private static readonly List<Collider> _tmpWorldCols = new List<Collider>(256);

    private void Awake() { if (!levelRoot) levelRoot = transform; }
    private void Start() { if (autoGenerateOnPlay) Generate(); }

    public void Generate()
    {
        ClearLevel();
        InitRng();

        // start
        GameObject start = Instantiate(startRoomPrefab, levelRoot);
        start.name = "StartRoom(Clone)";
        AlignYawTo30(start.transform);
        SnapToLatticeAtFirstAnchor(start);
        Register(start);
        PushAnchorsFrom(start);

        _corridorStepsLeft = Mathf.Max(0, corridorFirstSteps);
        _hallsSinceRoom = 0;

        while (_spawned.Count < maxRooms && _open.Count > 0)
        {
            var target = PopOpenAnchor();
            if (!target) continue;

            if (!TryAttachAt(target))
                PlaceDoorCapAt(target);
        }

        if (sealOpenAnchorsAtEnd)
        {
            for (int i = _open.Count - 1; i >= 0; i--) PlaceDoorCapAt(_open[i]);
            _open.Clear();
        }

        if (logSteps) Debug.Log($"[Gen] Done. Spawned={_spawned.Count}, Seed={_usedSeed}");
        TryBakeNavMesh();
    }

    public void ClearLevel()
    {
        for (int i = levelRoot.childCount - 1; i >= 0; i--) DestroyImmediate(levelRoot.GetChild(i).gameObject);
        _spawned.Clear(); _open.Clear(); _spawnCounts.Clear(); _placedAabbs.Clear();
        _hallsSinceRoom = 0; _corridorStepsLeft = 0; _nonHallCount = 0;
        _lastPrefab = null; _lastCat = RoomCategory.None; _samePrefab = _sameCat = 0;
    }

    private void InitRng()
    {
        _usedSeed = (randomizeSeed || seedInput == 0) ? Random.Range(int.MinValue, int.MaxValue) : seedInput;
        _rng = new System.Random(_usedSeed);
        if (logSteps) Debug.Log($"[Gen] Seed = {_usedSeed}");
    }

    // ---------- placement ----------
    private bool TryAttachAt(DoorAnchor target)
    {
        var prefab = PickNextPrefabFor(target);
        if (!prefab) return false;

        GameObject ghost = Instantiate(prefab);
        ghost.hideFlags = HideFlags.HideAndDontSave;

        var doors = ghost.GetComponentsInChildren<DoorAnchor>(true);
        if (doors.Length == 0) { DestroyImmediate(ghost); return false; }
        DoorAnchor door = doors[_rng.Next(doors.Length)];

        // align + snap
        HardAlignAt(ghost, door, target);
        AlignYawTo30(ghost.transform);
        if (snapPosToLattice) SnapByAnchor(ghost, door, target);

        // fast AABB precheck
        var localAabb = GetPrefabAabb(prefab);
        var worldAabb = ToWorldAabb(localAabb, ghost.transform, 0.05f);
        for (int k = 0; k < _placedAabbs.Count; k++)
            if (worldAabb.Intersects(_placedAabbs[k])) { DestroyImmediate(ghost); return false; }

        // precise overlap with door seam allowance
        if (!disableOverlapForDebug && OverlapsExistingAllowPortal(ghost, target, door))
        { DestroyImmediate(ghost); return false; }

        // commit
        ghost.hideFlags = HideFlags.None; ghost.transform.SetParent(levelRoot, true);
        Register(ghost);
        _placedAabbs.Add(ToWorldAabb(localAabb, ghost.transform, 0.02f));

        var meta = prefab.GetComponent<RoomMeta>();
        bool placedHall = (meta && meta.category == RoomCategory.Hallway);
        if (placedHall) { _hallsSinceRoom++; if (_corridorStepsLeft > 0) _corridorStepsLeft--; }
        else { _hallsSinceRoom = 0; _nonHallCount++; }

        PushAnchorsFrom(ghost, door);
        _open.Remove(target);

        float pad = (meta ? meta.clearancePadding : 0f);
        if (pad > 0f) PruneAnchorsNear(CompositeBounds(ghost), pad, ghost.transform);
        if (placedHall && hallwayClearance > 0f) PruneAnchorsNear(CompositeBounds(ghost), hallwayClearance, ghost.transform);

        if (logSteps) Debug.Log($"[Gen] ACCEPT '{prefab.name}'. Rooms:{_spawned.Count}");
        return true;
    }

    private GameObject PickNextPrefabFor(DoorAnchor target)
    {
        float total = 0f; var bag = new List<(GameObject p, float w)>();
        foreach (var p in roomPrefabs)
        {
            if (!p) continue;
            var m = p.GetComponent<RoomMeta>(); if (!m) continue;

            _spawnCounts.TryGetValue(p, out int used);
            if (m.uniqueOnce && used > 0) continue;
            if (m.maxCount > 0 && used >= m.maxCount) continue;

            if (!m.connectsAll && (target.allowedTargets & m.category) == 0) continue;

            bool needHall = (_corridorStepsLeft > 0) || (_hallsSinceRoom < minHallsBetweenRooms);
            if (needHall && m.category != RoomCategory.Hallway) continue;

            if (m.category != RoomCategory.Hallway)
            {
                if (_nonHallCount >= maxRoomsPlaced) continue;
                if (_rng.NextDouble() > roomSpawnChance) continue;
            }

            float w = Mathf.Max(0.0001f, m.weight);

            if (_lastPrefab == p && _samePrefab >= maxConsecutiveSamePrefab) w *= 0.1f;
            if (_lastCat == m.category && _sameCat >= maxConsecutiveSameCategory) w *= 0.25f;

            total += w; bag.Add((p, w));
        }
        if (bag.Count == 0) return null;

        float r = (float)_rng.NextDouble() * total;
        foreach (var (p, w) in bag) { r -= w; if (r <= 0f) return Choose(p); }
        return Choose(bag[bag.Count - 1].p);

        GameObject Choose(GameObject p)
        {
            var m = p.GetComponent<RoomMeta>();
            if (_lastPrefab == p) _samePrefab++; else { _lastPrefab = p; _samePrefab = 1; }
            if (_lastCat == m.category) _sameCat++; else { _lastCat = m.category; _sameCat = 1; }
            return p;
        }
    }

    private void Register(GameObject inst)
    {
        _spawned.Add(inst);
        var meta = inst.GetComponent<RoomMeta>();
        GameObject key = null;
        foreach (var pf in roomPrefabs) if (pf && pf.name == inst.name.Replace("(Clone)", "").Trim()) { key = pf; break; }
        if (key) _spawnCounts[key] = _spawnCounts.TryGetValue(key, out int c) ? c + 1 : 1;
    }

    private void PushAnchorsFrom(GameObject inst, DoorAnchor except = null)
    {
        foreach (var a in inst.GetComponentsInChildren<DoorAnchor>(true))
            if (a && a != except) _open.Add(a);
    }

    private DoorAnchor PopOpenAnchor()
    {
        if (_open.Count == 0) return null;
        return _open[_rng.Next(_open.Count)];
    }

    // ---------- alignment & lattice ----------
    private void HardAlignAt(GameObject ghost, DoorAnchor door, DoorAnchor target)
    {
        Quaternion rot = Quaternion.FromToRotation(door.transform.forward, -target.transform.forward);
        ghost.transform.rotation = rot * ghost.transform.rotation;
        Vector3 delta = target.transform.position - door.transform.position;
        ghost.transform.position += delta;
    }

    private void AlignYawTo30(Transform t)
    {
        if (!snapYawTo30) return;
        var e = t.eulerAngles; float snapped = Mathf.Round(e.y / 30f) * 30f;
        t.rotation = Quaternion.Euler(0f, snapped, 0f);
    }

    private void SnapByAnchor(GameObject ghost, DoorAnchor usedDoor, DoorAnchor target)
    {
        var anchors = ghost.GetComponentsInChildren<DoorAnchor>(true); if (anchors.Length == 0) return;
        Vector3 acc = Vector3.zero; int cnt = 0;
        foreach (var a in anchors)
        {
            if (a == usedDoor) continue;
            Vector3 snapped = NearestLatticePoint(a.transform.position);
            acc += (snapped - a.transform.position); cnt++;
        }
        if (cnt > 0) ghost.transform.position += new Vector3(acc.x / cnt, 0f, acc.z / cnt);
        Vector3 fix = target.transform.position - usedDoor.transform.position;
        ghost.transform.position += new Vector3(fix.x, 0f, fix.z);
    }

    private void SnapToLatticeAtFirstAnchor(GameObject inst)
    {
        if (!snapPosToLattice) return;
        var a = inst.GetComponentInChildren<DoorAnchor>(true); if (!a) return;
        Vector3 p = a.transform.position; Vector3 s = NearestLatticePoint(p);
        Vector3 d = s - p; inst.transform.position += new Vector3(d.x, 0f, d.z);
    }

    private Vector3 NearestLatticePoint(Vector3 world)
    {
        Vector2 p = new Vector2(world.x, world.z);
        Vector2 a = new Vector2(cellSize, 0f);
        Vector2 b = new Vector2(0.5f * cellSize, 0.86602540378f * cellSize); // cos30,sin30

        float det = a.x * b.y - a.y * b.x; // > 0
        Vector2 inv0 = new Vector2(b.y / det, -b.x / det);
        Vector2 inv1 = new Vector2(-a.y / det, a.x / det);

        float i = inv0.x * p.x + inv0.y * p.y;
        float j = inv1.x * p.x + inv1.y * p.y;

        int ri = Mathf.RoundToInt(i), rj = Mathf.RoundToInt(j);
        Vector2 snapped = ri * a + rj * b;
        return new Vector3(snapped.x, world.y, snapped.y);
    }

    // ---------- overlap guards ----------
    private bool OverlapsExistingAllowPortal(GameObject cand, DoorAnchor targetAnchor, DoorAnchor candDoor)
    {
        var candCols = cand.GetComponentsInChildren<Collider>(true);
        if (candCols == null || candCols.Length == 0) return false;

        bool anyOnMask = false;
        foreach (var c in candCols)
            if (c && c.enabled && ((placementCollisionMask.value & (1 << c.gameObject.layer)) != 0)) { anyOnMask = true; break; }
        if (!anyOnMask) return true;

        _tmpWorldCols.Clear();
        foreach (var r in _spawned)
        {
            if (!r) continue;
            foreach (var wc in r.GetComponentsInChildren<Collider>(true))
                if (wc && wc.enabled && ((placementCollisionMask.value & (1 << wc.gameObject.layer)) != 0)) _tmpWorldCols.Add(wc);
        }

        Vector3 n = targetAnchor.transform.forward.normalized;
        Vector3 center = targetAnchor.transform.position;
        Vector3 right = Vector3.Cross(Vector3.up, n).normalized;
        float halfW = portalSize.x * 0.5f, halfH = portalSize.y * 0.5f, halfT = portalSlabThickness * 0.5f;

        bool InPortal(Vector3 p)
        {
            float d = Vector3.Dot(p - center, n);
            if (Mathf.Abs(d) > halfT) return false;
            Vector3 inPlane = p - d * n;
            float rx = Vector3.Dot(inPlane - center, right);
            float uy = Vector3.Dot(inPlane - center, Vector3.up);
            return Mathf.Abs(rx) <= halfW && Mathf.Abs(uy) <= halfH;
        }

        foreach (var a in candCols)
        {
            if (!a || !a.enabled || ((placementCollisionMask.value & (1 << a.gameObject.layer)) == 0)) continue;
            Vector3 pa = a.transform.position; Quaternion qa = a.transform.rotation;

            foreach (var b in _tmpWorldCols)
            {
                if (!b) continue;
                Vector3 pb = b.transform.position; Quaternion qb = b.transform.rotation;
                if (Physics.ComputePenetration(a, pa, qa, b, pb, qb, out _, out _))
                {
                    if (allowPortalOverlap && InPortal(pa)) continue;
                    return true;
                }
            }
        }
        return false;
    }

    private Bounds CompositeBounds(GameObject go)
    {
        Bounds b = new Bounds(Vector3.zero, Vector3.zero); bool inited = false;
        foreach (var c in go.GetComponentsInChildren<Collider>(true))
        {
            if (!c || ((placementCollisionMask.value & (1 << c.gameObject.layer)) == 0)) continue;
            if (!inited) { b = c.bounds; inited = true; } else b.Encapsulate(c.bounds);
        }
        return b;
    }

    private Bounds GetPrefabAabb(GameObject prefab)
    {
        Bounds b;
        if (_prefabAabb.TryGetValue(prefab, out b)) return b;

        var inst = GameObject.Instantiate(prefab);
        inst.hideFlags = HideFlags.HideAndDontSave;

        bool has = false;
        Bounds a = new Bounds(Vector3.zero, Vector3.zero);

        foreach (var c in inst.GetComponentsInChildren<Collider>(true))
        {
            if (!c) continue;
            if ((placementCollisionMask.value & (1 << c.gameObject.layer)) == 0) continue;

            var wb = c.bounds;
            Vector3[] corners = {
                new Vector3(wb.min.x, wb.min.y, wb.min.z), new Vector3(wb.max.x, wb.min.y, wb.min.z),
                new Vector3(wb.min.x, wb.max.y, wb.min.z), new Vector3(wb.max.x, wb.max.y, wb.min.z),
                new Vector3(wb.min.x, wb.min.y, wb.max.z), new Vector3(wb.max.x, wb.min.y, wb.max.z),
                new Vector3(wb.min.x, wb.max.y, wb.max.z), new Vector3(wb.max.x, wb.max.y, wb.max.z),
            };
            for (int i = 0; i < corners.Length; i++) corners[i] = inst.transform.InverseTransformPoint(corners[i]);

            Bounds lb = new Bounds(corners[0], Vector3.zero);
            for (int i = 1; i < corners.Length; i++) lb.Encapsulate(corners[i]);

            if (!has) { a = lb; has = true; } else a.Encapsulate(lb);
        }

        DestroyImmediate(inst);
        _prefabAabb[prefab] = a;
        return a;
    }

    private Bounds ToWorldAabb(Bounds local, Transform t, float pad = 0f)
    {
        Vector3 c = local.center;
        Vector3 e = local.extents + Vector3.one * pad;

        Vector3[] corners = {
            new Vector3(c.x-e.x, c.y-e.y, c.z-e.z), new Vector3(c.x+e.x, c.y-e.y, c.z-e.z),
            new Vector3(c.x-e.x, c.y+e.y, c.z-e.z), new Vector3(c.x+e.x, c.y+e.y, c.z-e.z),
            new Vector3(c.x-e.x, c.y-e.y, c.z+e.z), new Vector3(c.x+e.x, c.y-e.y, c.z+e.z),
            new Vector3(c.x-e.x, c.y+e.y, c.z+e.z), new Vector3(c.x+e.x, c.y+e.y, c.z+e.z),
        };
        Bounds w = new Bounds(t.TransformPoint(corners[0]), Vector3.zero);
        for (int i = 1; i < corners.Length; i++) w.Encapsulate(t.TransformPoint(corners[i]));
        return w;
    }

    private void PruneAnchorsNear(Bounds b, float padding, Transform skipOwner = null)
    {
        for (int i = _open.Count - 1; i >= 0; i--)
        {
            var a = _open[i]; if (!a) { _open.RemoveAt(i); continue; }
            if (skipOwner && a.transform.root == skipOwner.root) continue;
            float d = Vector3.Distance(a.transform.position, b.ClosestPoint(a.transform.position));
            if (d < padding) _open.RemoveAt(i);
        }
    }

    private void PlaceDoorCapAt(DoorAnchor anchor)
    {
        if (!anchor) return;
        if (doorCapPrefab)
        {
            Vector3 pos = anchor.transform.position + anchor.transform.forward * (doorCapDepth * 0.5f);
            Quaternion rot = Quaternion.LookRotation(-anchor.transform.forward, Vector3.up);
            Instantiate(doorCapPrefab, pos, rot, levelRoot);
        }
        _open.Remove(anchor);
    }

    private void TryBakeNavMesh()
    {
        var rootGO = levelRoot ? levelRoot.gameObject : gameObject;
        System.Type t = System.Type.GetType("NavMeshRuntimeBaker") ?? System.Type.GetType("NavMeshRuntimeBakler");
        if (t == null) return;
        var comp = rootGO.GetComponent(t); if (comp == null) return;
        var bake = t.GetMethod("BakeNow", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);
        if (bake != null) bake.Invoke(comp, null);
    }
}
