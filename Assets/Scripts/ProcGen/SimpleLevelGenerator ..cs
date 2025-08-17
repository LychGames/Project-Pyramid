// Assets/Scripts/SimpleLevelGenerator.cs
// Unity 2021+ compatible

using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public class SimpleLevelGenerator : MonoBehaviour
{
    [Header("Hierarchy")]
    [SerializeField] Transform levelRoot;                 // Parent for spawned pieces

    [Header("Start")]
    [SerializeField] GameObject startRoomPrefab;          // Your triangular room (optional if using virtual)
    [SerializeField] bool startIsVirtual = false;         // Debug: build without spawning a start prefab
    [SerializeField, Range(0, 3)] int requiredExitsFromStart = 1;

    [Header("Forced Snakes")]
    [SerializeField, Range(0, 3)] int forcedSnakeCount = 2;
    [SerializeField, Range(1, 50)] int forcedSnakeLength = 6;
    [SerializeField] GameObject[] connectorWhitelist;     // e.g. TripleConnector
    [SerializeField] GameObject[] hallwayWhitelist;       // NewHall, HallLeft15, HallRight15, HallLeft30, HallRight30

    [Header("Lattice / Snapping")]
    [SerializeField] float cellSize = 2.5f;               // 10×3 kit => 2.5m hex basis
    [SerializeField] bool snapYawTo30 = true;
    [SerializeField] bool snapPosToLattice = true;

    [Header("Placement / Overlap")]
    [SerializeField] LayerMask placementCollisionMask;    // LevelGeo
    [SerializeField] float overlapPadding = 0.02f;        // Shrink AABB a hair to avoid false-positives near seams
    [SerializeField] bool allowPortalOverlap = false;     // Enable while debugging if needed

    [Header("Run Control")]
    [SerializeField] bool autoGenerateOnPlay = true;
    [SerializeField] bool randomizeSeed = true;
    [SerializeField] int seed = 0;
    [SerializeField, Range(1, 1000)] int maxPlacements = 120;

    [Header("Debug")]
    [SerializeField] bool verboseSelection = false;
    [SerializeField] bool logSteps = false;
    
    // Prefabs the generator can place in the map
    [Header("Prefabs")]

    public List<GameObject> roomPrefabs = new List<GameObject>();

    System.Random rng;
    
    [SerializeField] LayerMask doorTriggerMask; // set this to your DoorTrigger layer in Inspector
    [SerializeField] private LayerMask doorTriggerLayer;

    // ---------- ENTRY POINT ----------
    void Start()
    {
        if (autoGenerateOnPlay) GenerateNow();
    }

    [ContextMenu("Generate Now")]
    public void GenerateNow()
    {
        rng = randomizeSeed ? new System.Random() : new System.Random(seed);
        if (!levelRoot) levelRoot = transform;

        ClearLevelRoot();

        // Spawn start (or virtual)
        Transform startRoot = startIsVirtual
            ? MakeVirtualStart()
            : (startRoomPrefab ? Instantiate(startRoomPrefab, Vector3.zero, Quaternion.identity, levelRoot).transform
                               : MakeVirtualStart());

        if (!startIsVirtual && startRoot.parent != levelRoot) startRoot.SetParent(levelRoot);

        // Force exits + snakes so something ALWAYS grows from the start
        ForceStartGrowth(startRoot);

        // Optional stochastic expansion: keep placing until budget used
        ExpandFromOpenAnchors(maxPlacements);

        if (logSteps) Debug.Log($"[Gen] Done. Seed={seed}, MaxPlacements={maxPlacements}");
    }

    void ClearLevelRoot()
    {
        for (int i = levelRoot.childCount - 1; i >= 0; i--)
        {
            var c = levelRoot.GetChild(i);
            if (Application.isEditor) DestroyImmediate(c.gameObject);
            else Destroy(c.gameObject);
        }
    }

    // ---------- CORE GROWTH ----------

    void ForceStartGrowth(Transform startRoot)
    {
        var startAnchors = CollectAnchors(startRoot);

        Shuffle(startAnchors);  // vary which walls we use

        // 1) Guarantee at least N exits (try connectors first, fall back to a straight hall)
        int exits = 0;
        foreach (var a in startAnchors)
        {
            if (TryPlaceOneOf(connectorWhitelist, a, out var placed) || TryPlaceOneOf(hallwayWhitelist, a, out placed))
            {
                exits++;
                if (exits >= requiredExitsFromStart) break;
            }
        }

        // 2) Forced snakes from remaining distinct start anchors
        int snakes = 0;
        foreach (var a in startAnchors)
        {
            if (snakes >= forcedSnakeCount) break;

            Transform lastAnchor = a;
            for (int step = 0; step < forcedSnakeLength; step++)
            {
                if (!TryPlaceOneOf(hallwayWhitelist, lastAnchor, out var piece)) break;

                // choose a "forward" anchor on the newly placed piece to continue the snake
                var nextAnchor = ChooseNextAnchor(piece, lastAnchor.position);
                if (nextAnchor == null) break;

                lastAnchor = nextAnchor;
            }

            snakes++;
        }
    }

    void ExpandFromOpenAnchors(int budget)
    {
        // Grow from any anchor we can find under levelRoot until placement budget is used
        for (int placed = 0; placed < budget; placed++)
        {
            var anchors = CollectAnchors(levelRoot);
            if (anchors.Count == 0) break;

            var target = anchors[rng.Next(anchors.Count)];
            // Try some variety: 60% hall, 40% connector
            bool tryHallFirst = rng.NextDouble() < 0.6;

            if (tryHallFirst)
            {
                if (TryPlaceOneOf(hallwayWhitelist, target, out var _)) continue;
                TryPlaceOneOf(connectorWhitelist, target, out var __);
            }
            else
            {
                if (TryPlaceOneOf(connectorWhitelist, target, out var _)) continue;
                TryPlaceOneOf(hallwayWhitelist, target, out var __);
            }
        }
    }

    // ---------- PLACEMENT ----------

    bool TryPlaceOneOf(GameObject[] prefabs, Transform targetAnchor, out Transform placedRoot)
    {
        placedRoot = null;
        if (prefabs == null || prefabs.Length == 0 || targetAnchor == null) return false;

        // Weighted by RoomMeta.weight if present
        var weighted = new List<(GameObject prefab, float w)>();
        foreach (var p in prefabs)
        {
            if (!p) continue;
            float w = 1f;
            var meta = p.GetComponent<RoomMeta>();
            if (meta && meta.weight <= 0f) continue;
            if (meta) w = Mathf.Max(0.0001f, meta.weight);
            weighted.Add((p, w));
        }
        if (weighted.Count == 0) return false;

        // Try up to N random picks (avoid infinite loops)
        for (int tries = 0; tries < 12; tries++)
        {
            var prefab = WeightedPick(weighted);

            var inst = Instantiate(prefab).transform;
            inst.name = prefab.name;
            inst.SetParent(levelRoot, false);

            var entryAnchors = inst.GetComponentsInChildren<DoorAnchor>(true).Select(a => a.transform).ToList();
            if (entryAnchors.Count == 0) { Destroy(inst.gameObject); continue; }

            bool snapped = false;
            foreach (var entry in entryAnchors)
            {
                SnapModuleToAnchor(inst, entry, targetAnchor);

                if (snapYawTo30) SnapYawTo30(inst);
                if (snapPosToLattice) inst.position = SnapXZToHexLattice(inst.position, cellSize);

                // Extra safety: check if the target anchor is overlapping any DoorTrigger colliders
                var bounds = GetCombinedBounds(inst, overlapPadding);
                Collider[] doorHits = Physics.OverlapBox(bounds.center, bounds.extents, inst.rotation, LayerMask.GetMask("DoorTrigger"));
                if (doorHits.Length > 0)
                {
                    if (verboseSelection) Debug.Log($"[Reject-Door] {prefab.name} hit {doorHits.Length} DoorTriggers");
                    continue; // skip this entry and try another
                }

                if (allowPortalOverlap || VolumeFree(GetCombinedBounds(inst, overlapPadding)))
                {
                    placedRoot = inst;
                    snapped = true;
                    if (logSteps) Debug.Log($"[Place] {inst.name} on {targetAnchor.name}");
                    break;
                }
            }

            if (snapped) return true;

            Destroy(inst.gameObject);
            if (verboseSelection) Debug.Log($"[Reject] {prefab.name} @ {targetAnchor.name} (overlap or bad anchors)");
        }

        return false;
    }

    Transform ChooseNextAnchor(Transform pieceRoot, Vector3 previousAnchorPos)
    {
        var anchors = pieceRoot.GetComponentsInChildren<DoorAnchor>(true).Select(a => a.transform).ToList();
        if (anchors.Count == 0) return null;

        // pick the one farthest from the previous anchor position (avoid backtracking)
        Transform best = null;
        float bestDist = -1f;
        foreach (var a in anchors)
        {
            float d = (a.position - previousAnchorPos).sqrMagnitude;
            if (d > bestDist)
            {
                bestDist = d;
                best = a;
            }
        }
        return best;
    }

    // ---------- MATH / UTILS ----------

    static void SnapModuleToAnchor(Transform moduleRoot, Transform moduleEntryAnchor, Transform targetAnchor)
    {
        // rotate so entry faces exactly opposite the target
        Quaternion rotDelta = Quaternion.FromToRotation(moduleEntryAnchor.forward, -targetAnchor.forward);
        Quaternion newRot = rotDelta * moduleRoot.rotation;

        // where entry would be after that rotation
        Vector3 entryAfterRot = rotDelta * (moduleEntryAnchor.position - moduleRoot.position) + moduleRoot.position;

        // translate to target
        Vector3 newPos = moduleRoot.position + (targetAnchor.position - entryAfterRot);

        moduleRoot.SetPositionAndRotation(newPos, newRot);
    }

    static void SnapYawTo30(Transform t)
    {
        var e = t.eulerAngles;
        e.x = 0f; e.z = 0f; // keep modules flat
        e.y = Mathf.Round(e.y / 30f) * 30f;
        t.rotation = Quaternion.Euler(e);
    }

    static Vector3 SnapXZToHexLattice(Vector3 worldPos, float s)
    {
        // hex basis vectors in XZ plane
        Vector2 b0 = new Vector2(1f, 0f) * s;            // 0°
        Vector2 b1 = new Vector2(0.5f, 0.8660254f) * s;  // +60°
        Vector2 p = new Vector2(worldPos.x, worldPos.z);

        float det = b0.x * b1.y - b0.y * b1.x;           // = s^2 * 0.8660
        float q = (p.x * b1.y - p.y * b1.x) / det;
        float r = (-p.x * b0.y + p.y * b0.x) / det;

        int qR = Mathf.RoundToInt(q);
        int rR = Mathf.RoundToInt(r);

        Vector2 snapped = qR * b0 + rR * b1;
        return new Vector3(snapped.x, worldPos.y, snapped.y);
    }

    static Bounds GetCombinedBounds(Transform root, float shrink = 0f)
    {
        var renderers = root.GetComponentsInChildren<Renderer>();
        Bounds b = new Bounds(root.position, Vector3.zero);
        bool init = false;
        foreach (var r in renderers)
        {
            if (!init) { b = r.bounds; init = true; }
            else b.Encapsulate(r.bounds);
        }
        if (!init) b = new Bounds(root.position, Vector3.one * 0.5f);

        if (shrink > 0f)
        {
            b.Expand(-2f * shrink);
            if (b.size.x < 0) b.size = new Vector3(0.001f, b.size.y, b.size.z);
            if (b.size.y < 0) b.size = new Vector3(b.size.x, 0.001f, b.size.z);
            if (b.size.z < 0) b.size = new Vector3(b.size.x, b.size.y, 0.001f);
        }
        return b;
    }

    bool VolumeFree(Bounds b)
    {
        var hits = Physics.OverlapBox(b.center, b.extents, Quaternion.identity, placementCollisionMask, QueryTriggerInteraction.Ignore);
        return hits.Length == 0;
    }

    static List<Transform> CollectAnchors(Transform root)
    {
        var list = new List<Transform>();
        foreach (var a in root.GetComponentsInChildren<DoorAnchor>(true))
            list.Add(a.transform);
        return list;
    }

    void Shuffle<T>(IList<T> list)
    {
        for (int i = list.Count - 1; i > 0; i--)
        {
            int j = rng.Next(i + 1);
            (list[i], list[j]) = (list[j], list[i]);
        }
    }

    GameObject WeightedPick(List<(GameObject prefab, float w)> items)
    {
        float sum = items.Sum(t => t.w);
        float r = (float)(rng.NextDouble() * sum);
        foreach (var it in items)
        {
            if (r < it.w) return it.prefab;
            r -= it.w;
        }
        return items[items.Count - 1].prefab;
    }
    // --- add inside SimpleLevelGenerator ---

    // Creates a dummy start root with three DoorAnchors (0°, ±60°) at the origin.
    // Useful for testing without your triangle prefab.
    Transform MakeVirtualStart()
    {
        var root = new GameObject("VirtualStart").transform;
        root.SetParent(levelRoot, false);
        root.position = Vector3.zero;

        // 3 exits: forward, left60, right60
        CreateAnchor(root, "A_Forward", 0f);
        CreateAnchor(root, "B_Left60", -60f);
        CreateAnchor(root, "C_Right60", 60f);

        return root;
    }

    // small helper to make an anchor child
    static Transform CreateAnchor(Transform parent, string name, float yawDeg)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.transform.localPosition = new Vector3(0f, 0f, 0.01f); // tiny nudge so overlap checks don't hit the start itself
        go.transform.localRotation = Quaternion.Euler(0f, yawDeg, 0f);

        go.AddComponent<DoorAnchor>(); // marker + gizmo

        return go.transform;
    }
    Transform FindFacingDoor(Transform targetAnchor, float maxDist = 0.2f)
    {
        // Cast a short ray straight out of the target doorway to find another door trigger
        Vector3 p = targetAnchor.position + targetAnchor.forward * 0.01f; // tiny nudge out of the wall
        Vector3 dir = targetAnchor.forward;
        if (Physics.Raycast(p, dir, out RaycastHit hit, maxDist, doorTriggerMask, QueryTriggerInteraction.Collide))
        {
            // We hit another door trigger. Return its anchor transform (assumes collider is on a child beside the DoorAnchor)
            var other = hit.collider.GetComponentInParent<DoorAnchor>();
            if (other != null) return other.transform;
        }
        return null;
    }
    private bool OverlapsDoorTriggers(GameObject prefab, Transform targetAnchor)
    {
        // Find the DoorAnchor on the prefab that matches our connection
        DoorAnchor prefabAnchor = prefab.GetComponentInChildren<DoorAnchor>();
        if (prefabAnchor == null) return true; // fail safe

        // Calculate placement: position + rotation to align with targetAnchor
        Quaternion rotation = targetAnchor.rotation * Quaternion.Inverse(prefabAnchor.transform.localRotation);
        Vector3 position = targetAnchor.position - (rotation * prefabAnchor.transform.localPosition);

        // Get prefab bounds (using renderers or colliders)
        Bounds bounds = new Bounds(position, Vector3.zero);
        foreach (var col in prefab.GetComponentsInChildren<Collider>())
        {
            bounds.Encapsulate(new Bounds(position + (rotation * (col.transform.localPosition)), col.bounds.size));
        }

        // Check overlap against doorTriggerLayer
        Collider[] hits = Physics.OverlapBox(bounds.center, bounds.extents, rotation, doorTriggerLayer);
        return hits.Length > 0; // true = overlap detected
    }
}

