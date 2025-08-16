using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Procedural layout generator with Corridor-First growth,
/// robust overlap prevention, and seed control.
/// </summary>
public class SimpleLevelGenerator : MonoBehaviour
{
    // ───────────────────────────────── Prefabs / Root ───────────────────────────
    [Header("Prefabs")]
    [SerializeField] private GameObject startRoomPrefab;
    [SerializeField] private List<GameObject> roomPrefabs = new List<GameObject>();

    [Header("Hierarchy (optional)")]
    [SerializeField] private Transform levelRoot; // If null, created at runtime

    // ───────────────────────────────── Generation ───────────────────────────────
    [Header("Generation")]
    [Tooltip("Maximum number of rooms INCLUDING the start room.")]
    [SerializeField, Min(1)] private int maxRooms = 12;

    [SerializeField] private bool autoGenerateOnPlay = true;

    [Header("Seed (0 = random each run)")]
    [Tooltip("Enter a seed for reproducible layouts. Set 0 to randomize each run.")]
    [SerializeField] private int seedInput = 0;

    [SerializeField, Min(1)] private int maxPlacementAttemptsPerRoom = 30;
    [Header("Grid Snap (optional)")]
    [SerializeField] bool enableGridSnap = true;
    [SerializeField, Min(0.1f)] float gridSize = 2f;   // try 2, 2.5, or whatever matches your door pitch

    // ───────────────────────────────── Flow / Variety ───────────────────────────
    [Header("Flow / Variety")]
    [SerializeField, Range(0f, 1f)] private float preferTurnBias = 0.35f;
    [Tooltip("If > 0, the first few placements will try to include at least one turn.")]
    [SerializeField, Min(0)] private int forceFirstTurnWithin = 3;
    [SerializeField, Min(1)] private int frontierShuffleWindow = 4;

    [Header("Anti-Repeat Limits")]
    [SerializeField, Min(0)] private int maxConsecutiveSamePrefab = 2;
    [SerializeField, Min(0)] private int maxConsecutiveSameCategory = 0;
    [SerializeField, Min(0)] private int maxConsecutiveStraight = 3;

    // ───────────────────────────────── Corridor-First ───────────────────────────
    [Header("Corridor-First")]
    [Tooltip("First N placements (after the Start) must be Hallway pieces.")]
    [SerializeField, Min(0)] private int corridorFirstSteps = 10;

    [Tooltip("After placing a Room, require this many Hallways before another Room.")]
    [SerializeField, Min(0)] private int minHallsBetweenRooms = 2;

    [Tooltip("Extra score to prefer snaking/turning halls during corridor/spacing.")]
    [SerializeField, Range(0f, 1.5f)] private float hallwayContinueBias = 0.45f;

    [Tooltip("Extra pruning distance after placing a Hallway to avoid door spam nearby.")]
    [SerializeField, Min(0f)] private float hallwayClearance = 0.8f;

    // ───────────────────────────────── Overlap & Bounds ─────────────────────────
    [Header("Overlap Check")]
    [Tooltip("Set this to your 'LevelGeo' (RoomBounds) layer ONLY.")]
    [SerializeField] private LayerMask placementCollisionMask;

    [Header("Portal Overlap Allowance")]
    [Tooltip("Allow a thin slab of overlap right at the connecting doorway.")]
    [SerializeField] private bool allowPortalOverlap = true;

    [Tooltip("Doorway seam allowance width x height (meters).")]
    [SerializeField] private Vector2 portalSize = new Vector2(2.4f, 2.4f);

    [Tooltip("Seam depth allowed for overlap (meters).")]
    [SerializeField] private float portalSlabThickness = 0.10f;

    [Header("Spacing / Clearance")]
    [Tooltip("Global additional padding when pruning nearby open anchors after placement.")]
    [SerializeField, Min(0f)] private float globalClearancePadding = 0f;

    // ───────────────────────────────── Optional Door Caps ───────────────────────
    [Header("Unused Door Sealing (optional)")]
    [SerializeField] private GameObject doorBlockerPrefab;

    // ───────────────────────────────── Debug / Info ─────────────────────────────
    [Header("Debug")]
    [SerializeField] private bool logSteps = false;
    [SerializeField] private bool drawGizmos = true;
    [SerializeField] private bool verboseSelection = false;
    [SerializeField] private bool disableOverlapForDebug = false;

    [Header("Runtime (info)")]
    [SerializeField] private int usedSeed = 0;
    public GameObject StartInstance { get; private set; }

    // ───────────────────────────────── Runtime state ────────────────────────────
    private System.Random rng;
    private readonly List<DoorAnchor> openAnchors = new();
    private readonly HashSet<DoorAnchor> usedAnchors = new();
    private readonly List<GameObject> spawnedRooms = new();

    private readonly Dictionary<GameObject, int> _spawnCounts = new();
    private int _placedSummonRooms = 0;
    private int _stepsSinceRoom = 999; // halls since last room; start high

    private GameObject _lastPrefabAsset = null; private int _lastPrefabRun = 0;
    private RoomCategory _lastCategory = RoomCategory.None; private int _lastCategoryRun = 0;
    private int _straightStreak = 0;
    private Vector3 lastGrowthDir = Vector3.zero;

    private const int OverlapCache = 64;
    private static readonly Collider[] overlapHits = new Collider[OverlapCache];

    // ───────────────────────────────── Unity ────────────────────────────────────
    void Awake()
    {
        if (!Validate()) { enabled = false; return; }
        if (!levelRoot)
        {
            var go = new GameObject("LevelRoot");
            levelRoot = go.transform;
        }
    }

    void Start()
    {
        if (autoGenerateOnPlay) { ClearLevel(); Generate(); }
    }

    void Update()
    {
        if (Application.isPlaying && Input.GetKeyDown(KeyCode.R))
        {
            ClearLevel(); Generate();
        }
    }

    // ───────────────────────────────── API ──────────────────────────────────────
    [ContextMenu("Generate Now")]
    public void Generate()
    {
        ClearLevel();
        InitRng();

        // Start room
        StartInstance = Instantiate(startRoomPrefab, levelRoot);
        ForceUnitScale(StartInstance.transform);
        StartInstance.SetActive(true);
        spawnedRooms.Add(StartInstance);
        AddRoomAnchorsToOpenList(StartInstance, null);

        if (logSteps) Debug.Log($"[Gen] Start placed. Open anchors = {openAnchors.Count}, Seed={usedSeed}");

        int roomsPlaced = 1;
        int fails = 0;
        lastGrowthDir = Vector3.zero;
        _straightStreak = 0;
        _lastPrefabAsset = null; _lastPrefabRun = 0;
        _lastCategory = RoomCategory.None; _lastCategoryRun = 0;

        int safety = maxRooms * maxPlacementAttemptsPerRoom * 3;

        while (roomsPlaced < maxRooms && openAnchors.Count > 0 && safety-- > 0)
        {
            int window = Mathf.Min(frontierShuffleWindow, openAnchors.Count);
            int idx = (window <= 1) ? 0 : rng.Next(window);
            var target = openAnchors[idx];
            openAnchors.RemoveAt(idx);
            if (!target || usedAnchors.Contains(target)) continue;

            if (logSteps) Debug.Log($"[Gen] Try anchor '{target.name}'   OA:{openAnchors.Count}   Rooms:{spawnedRooms.Count}");

            if (TryAttachRoomAt(target, ref roomsPlaced))
            {
                fails = 0;
            }
            else
            {
                if (++fails > maxPlacementAttemptsPerRoom * 2) break;
            }
        }

        // Optional: cap unused
        if (doorBlockerPrefab)
        {
            foreach (var a in openAnchors)
            {
                if (!a || usedAnchors.Contains(a)) continue;
                PlaceBlockerAt(a);
            }
        }

        BakeNavMeshIfReady();
        if (logSteps) Debug.Log($"[Gen] Done. Spawned={spawnedRooms.Count}, Open={openAnchors.Count}, Seed={usedSeed}");
    }

    // ───────────────────────────────── Core ─────────────────────────────────────
    private bool TryAttachRoomAt(DoorAnchor targetAnchor, ref int roomsPlaced)
    {
        bool mustTurn = false;
        if (forceFirstTurnWithin > 0 && spawnedRooms.Count <= forceFirstTurnWithin)
            mustTurn = (lastGrowthDir != Vector3.zero);

        bool corridorPhase = (spawnedRooms.Count - 1) < corridorFirstSteps;
        bool enforceRoomSpacing = (_stepsSinceRoom < minHallsBetweenRooms);

        const int samples = 8;
        GameObject bestAsset = null; GameObject bestGhost = null; DoorAnchor bestDoor = null;
        float bestScore = float.NegativeInfinity; float bestTurn = 0f;

        for (int s = 0; s < samples; s++)
        {
            var prefabAsset = PickNextPrefabFor(targetAnchor);
            if (!prefabAsset) break;

            // hard anti-repeat
            if (maxConsecutiveSamePrefab > 0 && _lastPrefabAsset == prefabAsset && _lastPrefabRun >= maxConsecutiveSamePrefab) continue;
            var metaAsset = prefabAsset.GetComponent<RoomMeta>();
            if (metaAsset && maxConsecutiveSameCategory > 0 && _lastCategory == metaAsset.category && _lastCategoryRun >= maxConsecutiveSameCategory) continue;

            // spawn ghost (inactive)
            var ghost = Instantiate(prefabAsset, levelRoot);
            ghost.SetActive(false);
            ForceUnitScale(ghost.transform);

            if (!HasBoundsOnMask(ghost))
            {
                if (verboseSelection) Debug.LogWarning($"[Gen] REJECT {ghost.name}: no LevelGeo bounds. Add TRIGGER BoxCollider(s) on LevelGeo.");
                DestroyImmediate(ghost); continue;
            }

            var door = ChooseBestAnchor(ghost, targetAnchor);
            if (!door)
            {
                if (verboseSelection) Debug.LogWarning($"[Gen] REJECT {ghost.name}: no door anchors found.");
                DestroyImmediate(ghost); continue;
            }



            // Non-unit scale on anchor parents can break alignment (tolerant ~1)
            Vector3 ls1 = door.transform.lossyScale;
            Vector3 ls2 = targetAnchor.transform.lossyScale;
            bool approxOne(Vector3 v) => Mathf.Abs(v.x - 1f) < 0.001f && Mathf.Abs(v.y - 1f) < 0.001f && Mathf.Abs(v.z - 1f) < 0.001f;
            if (!approxOne(ls1) || !approxOne(ls2))
            {
                if (verboseSelection) Debug.LogWarning($"[Gen] REJECT {ghost.name}: anchor parent scale not ~1 (door {ls1}, target {ls2}). Ensure all parents are (1,1,1).");
                DestroyImmediate(ghost); continue;
            }

            // NEW:
            HardAlignRoomAtAnchor(ghost, door, targetAnchor);

            // (optional) if you had grid snap, apply it AFTER alignment, or disable it while testing.


            // Must be snapped & facing
            Vector3 aPos = door.transform.position;
            Vector3 aFwd = door.transform.forward.normalized;
            Vector3 tPos = targetAnchor.transform.position;
            Vector3 tFwd = targetAnchor.transform.forward.normalized;
            float posErr = Vector3.Distance(aPos, tPos);
            float facingDot = Vector3.Dot(aFwd, -tFwd);
            const float maxSnapError = 0.02f;   // 2 cm
            const float minFacingDot = 0.995f;  // ~cos(5°)
            if (posErr > maxSnapError || facingDot < minFacingDot)
            {
                if (verboseSelection)
                    Debug.LogWarning($"[Gen] REJECT {ghost.name}: bad anchor snap (posErr={posErr:F3}, facingDot={facingDot:F3}).");
                DestroyImmediate(ghost);
                continue;
            }

            // Overlap check (robust, with door seam allowance)
            bool blocked = !disableOverlapForDebug
              && OverlapsExisting_PenetrationAllowPortal(ghost, targetAnchor, door);
            if (blocked)
            {
                if (verboseSelection) Debug.LogWarning($"[Gen] REJECT {ghost.name}: overlap with existing LevelGeo (outside portal seam).");
                DestroyImmediate(ghost); continue;
            }


            // scoring
            float turn = ComputeTurnScore(targetAnchor, door);
            if (mustTurn && turn < 0.2f) { DestroyImmediate(ghost); continue; }
            if (maxConsecutiveStraight > 0 && _straightStreak >= maxConsecutiveStraight && turn < 0.2f)
            { DestroyImmediate(ghost); continue; }

            float score = 0f;
            if (preferTurnBias > 0f) score += turn * preferTurnBias;

            // Corridor-first: encourage snaking halls during corridor phase / spacing window
            bool candidateIsHall = metaAsset && metaAsset.category == RoomCategory.Hallway;
            if ((corridorPhase || enforceRoomSpacing) && candidateIsHall)
            {
                score += hallwayContinueBias * Mathf.Lerp(0.25f, 1f, turn); // prefer slight turns to make “S” shapes
            }

            // Avoid immediate 180° backtrack
            Vector3 inDir = targetAnchor.transform.forward.normalized;
            Vector3 outDir = (-door.transform.forward).normalized;
            float backDot = Vector3.Dot(inDir, outDir); // +1 straight, -1 back
            if (backDot < -0.95f) score -= 0.5f;

            // Favor more-degree nodes a tiny bit (branch potential)
            int doorsCount = ghost.GetComponentsInChildren<DoorAnchor>(true).Length;
            if (doorsCount > 2) score += 0.05f * (doorsCount - 2);

            // mild anti-repeat
            if (_lastPrefabAsset == prefabAsset) score -= 0.05f;
            if (metaAsset && _lastCategory == metaAsset.category) score -= 0.03f;

            if (score > bestScore)
            {
                if (bestGhost) DestroyImmediate(bestGhost);
                bestAsset = prefabAsset; bestGhost = ghost; bestDoor = door; bestScore = score; bestTurn = turn;
            }
            else DestroyImmediate(ghost);
        }

        if (!bestGhost) return false;

        // accept
        bestGhost.SetActive(true);
        spawnedRooms.Add(bestGhost);
        usedAnchors.Add(targetAnchor);
        usedAnchors.Add(bestDoor);
        roomsPlaced++;
        lastGrowthDir = targetAnchor.transform.forward.normalized;

        AddRoomAnchorsToOpenList(bestGhost, bestDoor);

        if (!_spawnCounts.ContainsKey(bestAsset)) _spawnCounts[bestAsset] = 0;
        _spawnCounts[bestAsset]++;

        var metaPlaced = bestGhost.GetComponent<RoomMeta>();
        if (metaPlaced && metaPlaced.category == RoomCategory.Summon) _placedSummonRooms++;

        _lastPrefabRun = (_lastPrefabAsset == bestAsset) ? _lastPrefabRun + 1 : 1;
        _lastPrefabAsset = bestAsset;
        if (metaPlaced)
        {
            _lastCategoryRun = (_lastCategory == metaPlaced.category) ? _lastCategoryRun + 1 : 1;
            _lastCategory = metaPlaced.category;
        }
        _straightStreak = (bestTurn < 0.2f) ? _straightStreak + 1 : 0;

        // spacing counter: Halls increment, Rooms reset
        bool placedIsHall = metaPlaced && metaPlaced.category == RoomCategory.Hallway;
        _stepsSinceRoom = placedIsHall ? (_stepsSinceRoom + 1) : 0;

        // clearance pruning — do NOT prune anchors that belong to the room we just placed
        float pad = globalClearancePadding + (metaPlaced ? metaPlaced.clearancePadding : 0f);
        if (pad > 0f)
        {
            Bounds b = GetCompositeBounds(bestGhost);
            if (b.size != Vector3.zero) PruneAnchorsNear(b, pad, bestGhost.transform);
        }

        // extra prune around halls to discourage door spam near corridors
        if (placedIsHall && hallwayClearance > 0f)
        {
            Bounds hb = GetCompositeBounds(bestGhost);
            if (hb.size != Vector3.zero) PruneAnchorsNear(hb, hallwayClearance, bestGhost.transform);
        }

        if (logSteps) Debug.Log($"[Gen] ACCEPT '{bestGhost.name}'. OA now:{openAnchors.Count}");
        return true;

    }

    private GameObject PickNextPrefabFor(DoorAnchor target)
    {
        // Local gates for this pick
        bool corridorPhase = (spawnedRooms.Count - 1) < corridorFirstSteps; // Start not counted
        bool enforceRoomSpacing = (_stepsSinceRoom < minHallsBetweenRooms);

        GameObject PickWithGate(bool hallsOnly, out string dump)
        {
            float total = 0f;
            var bag = new List<(GameObject p, float w)>();
            System.Text.StringBuilder sb = verboseSelection ? new System.Text.StringBuilder() : null;

            for (int i = 0; i < roomPrefabs.Count; i++)
            {
                var p = roomPrefabs[i]; if (!p) continue;
                var meta = p.GetComponent<RoomMeta>(); if (!meta) continue;

                // corridor/spacing gate → only Hallways allowed
                if (hallsOnly && meta.category != RoomCategory.Hallway)
                { if (sb != null) sb.AppendLine($"REJECT {p.name}: not Hallway (phase/spacing)"); continue; }

                _spawnCounts.TryGetValue(p, out int used);
                if (meta.uniqueOnce && used > 0) { if (sb != null) sb.AppendLine($"REJECT {p.name}: uniqueOnce"); continue; }
                if (meta.maxCount > 0 && used >= meta.maxCount) { if (sb != null) sb.AppendLine($"REJECT {p.name}: maxCount"); continue; }
                if (!meta.connectsAll && (target.allowedTargets & meta.category) == 0)
                { if (sb != null) sb.AppendLine($"REJECT {p.name}: disallowed by target"); continue; }
                if (meta.category == RoomCategory.Summon && _placedSummonRooms >= 1)
                { if (sb != null) sb.AppendLine($"REJECT {p.name}: summon limit"); continue; }

                float w = Mathf.Max(0.0001f, meta.weight);
                total += w; bag.Add((p, w));
            }

            if (bag.Count == 0) { dump = sb?.ToString() ?? ""; return null; }

            float r = (float)rng.NextDouble() * total;
            foreach (var (p, w) in bag) { if ((r -= w) <= 0f) { dump = sb?.ToString() ?? ""; return p; } }
            dump = sb?.ToString() ?? "";
            return bag[bag.Count - 1].p;
        }

        bool hallsOnly = (corridorPhase || enforceRoomSpacing);
        string debugDump;
        var pick = PickWithGate(hallsOnly, out debugDump);

        // fail-safe: if no Halls fit, allow Rooms this pick so we don’t stall
        if (!pick && hallsOnly)
        {
            if (verboseSelection)
                Debug.LogWarning($"[Gen] Corridor/spacing gate yielded 0 candidates at '{target.name}'. Allowing rooms this time.\n{debugDump}");
            pick = PickWithGate(false, out debugDump);
        }

        if (!pick && verboseSelection)
            Debug.LogWarning($"[Gen] No eligible prefabs for '{target.name}'.\n{debugDump}");

        return pick;
    }

    private DoorAnchor ChooseBestAnchor(GameObject roomInstance, DoorAnchor target)
    {
        var anchors = roomInstance.GetComponentsInChildren<DoorAnchor>(true);
        if (anchors == null || anchors.Length == 0) return null;

        // Pick the anchor whose forward is MOST opposite to the target (best dot with -target.forward).
        DoorAnchor best = null; float bestDot = -2f;
        Vector3 need = -target.transform.forward.normalized;
        for (int i = 0; i < anchors.Length; i++)
        {
            var a = anchors[i]; if (!a || usedAnchors.Contains(a)) continue;
            float d = Vector3.Dot(a.transform.forward.normalized, need);
            if (d > bestDot) { bestDot = d; best = a; }
        }
        return best;
    }


    private void AlignRoom(GameObject room, DoorAnchor roomAnchor, DoorAnchor target)
    {
        Quaternion rot = Quaternion.FromToRotation(roomAnchor.transform.forward, -target.transform.forward);
        room.transform.rotation = rot * room.transform.rotation;
        room.transform.position += target.transform.position - roomAnchor.transform.position;
        room.transform.position += target.transform.position - roomAnchor.transform.position;

        if (enableGridSnap)
        {
            Vector3 p = room.transform.position;
            p.x = Mathf.Round(p.x / gridSize) * gridSize;
            p.z = Mathf.Round(p.z / gridSize) * gridSize;
            room.transform.position = p;
        }
    }

    private void AddRoomAnchorsToOpenList(GameObject room, DoorAnchor exclude)
    {
        bool corridorPhase = (spawnedRooms.Count - 1) < corridorFirstSteps;

        var arr = room.GetComponentsInChildren<DoorAnchor>(true);
        for (int i = 0; i < arr.Length; i++)
        {
            var a = arr[i]; if (!a || a == exclude) continue;
            if (usedAnchors.Contains(a)) continue;

            if (corridorPhase) openAnchors.Insert(0, a); // depth-first during corridor phase
            else openAnchors.Add(a);
        }
    }

    private void PlaceBlockerAt(DoorAnchor a)
    {
        if (!doorBlockerPrefab || !a) return;
        var b = Instantiate(doorBlockerPrefab, a.transform.position, a.transform.rotation, levelRoot);
        b.transform.position += a.transform.forward * 0.01f;
    }

    private void ForceUnitScale(Transform t) => t.localScale = Vector3.one;

    // ───────────────────────────────── Overlap helpers ──────────────────────────
    private bool HasBoundsOnMask(GameObject go)
    {
        var cols = go.GetComponentsInChildren<Collider>(true);
        foreach (var c in cols)
        {
            if (!c || !c.enabled) continue;
            if ((placementCollisionMask.value & (1 << c.gameObject.layer)) == 0) continue;
            return true;
        }
        return false;
    }

    private bool OverlapsExisting_PenetrationAllowPortal(GameObject candidate, DoorAnchor targetAnchor, DoorAnchor candDoor)
    {
#if UNITY_EDITOR
        if (verboseSelection)
        {
            int candOnMask = 0, worldOnMask = 0;
            foreach (var c in candidate.GetComponentsInChildren<Collider>(true))
                if (c && c.enabled && ((placementCollisionMask.value & (1 << c.gameObject.layer)) != 0)) candOnMask++;
            foreach (var r in spawnedRooms)
                if (r) foreach (var c in r.GetComponentsInChildren<Collider>(true))
                        if (c && c.enabled && ((placementCollisionMask.value & (1 << c.gameObject.layer)) != 0)) worldOnMask++;

            Debug.Log($"[Gen][Overlap] candidateCols={candOnMask} worldCols={worldOnMask} portal={allowPortalOverlap}");
        }
#endif

#if UNITY_EDITOR
        if (verboseSelection)
        {
            int candOnMask = 0, worldOnMask = 0;
            foreach (var c in candidate.GetComponentsInChildren<Collider>(true))
                if (c && c.enabled && ((placementCollisionMask.value & (1 << c.gameObject.layer)) != 0)) candOnMask++;
            foreach (var r in spawnedRooms)
                if (r) foreach (var c in r.GetComponentsInChildren<Collider>(true))
                        if (c && c.enabled && ((placementCollisionMask.value & (1 << c.gameObject.layer)) != 0)) worldOnMask++;

            Debug.Log($"[Gen][Overlap] candidateCols={candOnMask} worldCols={worldOnMask} portal={allowPortalOverlap}");
        }
#endif

        // 1) Build an allow-set inside a thin portal slab at the target doorway
        HashSet<Collider> portalSet = null;
        if (allowPortalOverlap)
        {
            var half = new Vector3(Mathf.Max(0.01f, portalSize.x * 0.5f),
                                   Mathf.Max(0.01f, portalSize.y * 0.5f),
                                   Mathf.Max(0.01f, portalSlabThickness * 0.5f));
            int n = Physics.OverlapBoxNonAlloc(targetAnchor.transform.position, half,
                                               overlapHits, targetAnchor.transform.rotation,
                                               placementCollisionMask, QueryTriggerInteraction.Collide);
            if (n > 0)
            {
                portalSet = new HashSet<Collider>();
                for (int i = 0; i < n; i++) if (overlapHits[i]) portalSet.Add(overlapHits[i]);
            }
        }

        // 2) Gather existing LevelGeo colliders once
        var worldCols = GetWorldBoundsColliders();

        // 3) Pairwise penetration check (robust for rotated composite bounds)
        var candCols = candidate.GetComponentsInChildren<Collider>(true);
        foreach (var cC in candCols)
        {
            if (!cC || !cC.enabled) continue;
            if ((placementCollisionMask.value & (1 << cC.gameObject.layer)) == 0) continue;

            for (int i = 0; i < worldCols.Count; i++)
            {
                var cE = worldCols[i];
                if (!cE || !cE.enabled) continue;
                if (cE.transform.root == candidate.transform.root) continue; // skip self
                if (portalSet != null && portalSet.Contains(cE)) continue;   // ignore seam

                Vector3 dir; float dist;
                if (Physics.ComputePenetration(
                    cC, cC.transform.position, cC.transform.rotation,
                    cE, cE.transform.position, cE.transform.rotation,
                    out dir, out dist))
                {
                    if (dist > 0.001f) return true; // real intersection
                }
            }
        }
        return false;
    }

    private List<Collider> GetWorldBoundsColliders()
    {
        var list = new List<Collider>(128);
        for (int r = 0; r < spawnedRooms.Count; r++)
        {
            var room = spawnedRooms[r]; if (!room) continue;
            var cols = room.GetComponentsInChildren<Collider>(true);
            for (int i = 0; i < cols.Length; i++)
            {
                var c = cols[i]; if (!c || !c.enabled) continue;
                if ((placementCollisionMask.value & (1 << c.gameObject.layer)) == 0) continue;
                list.Add(c);
            }
        }
        return list;
    }

    private Bounds GetCompositeBounds(GameObject go)
    {
        var cols = go.GetComponentsInChildren<Collider>(true);
        bool any = false; Bounds b = default;
        foreach (var c in cols)
        {
            if (!c) continue;
            if ((placementCollisionMask.value & (1 << c.gameObject.layer)) == 0) continue;
            if (!any) { b = c.bounds; any = true; } else b.Encapsulate(c.bounds);
        }
        return any ? b : new Bounds(go.transform.position, Vector3.zero);
    }

    private void PruneAnchorsNear(Bounds b, float padding, Transform skipOwner)
    {
        if (padding <= 0f) return;
        b.Expand(padding);

        for (int i = openAnchors.Count - 1; i >= 0; i--)
        {
            var a = openAnchors[i];
            if (!a) { openAnchors.RemoveAt(i); continue; }

            // Don’t prune anchors that belong to the room we just placed
            if (skipOwner && a.transform.root == skipOwner.root) continue;

            if (b.Contains(a.transform.position))
                openAnchors.RemoveAt(i);
        }
    }


    // ───────────────────────────────── Seed / Cleanup / Gizmos ─────────────────
    private void InitRng()
    {
        usedSeed = (seedInput != 0)
            ? seedInput
            : (System.Environment.TickCount ^ System.Guid.NewGuid().GetHashCode());

        rng = new System.Random(usedSeed);
        Random.InitState(usedSeed);

        if (logSteps || verboseSelection) Debug.Log($"[Gen] Seed = {usedSeed}");
    }

    private void ClearLevel()
    {
        openAnchors.Clear(); usedAnchors.Clear();
        for (int i = 0; i < spawnedRooms.Count; i++) if (spawnedRooms[i]) DestroyImmediate(spawnedRooms[i]);
        spawnedRooms.Clear();

        _spawnCounts.Clear(); _placedSummonRooms = 0;
        _lastPrefabAsset = null; _lastPrefabRun = 0; _lastCategory = RoomCategory.None; _lastCategoryRun = 0; _straightStreak = 0;
        _stepsSinceRoom = 999;
        StartInstance = null;
    }

    private bool Validate()
    {
        bool ok = true;
        if (!startRoomPrefab) { Debug.LogError("[Gen] Start room missing."); ok = false; }
        if (roomPrefabs == null || roomPrefabs.Count == 0) { Debug.LogError("[Gen] Room list empty."); ok = false; }
        if (placementCollisionMask.value == 0)
        {
            Debug.LogWarning("[Gen] placementCollisionMask is 0; set it to your LevelGeo layer.");
        }
        return ok;
    }

#if UNITY_EDITOR
    void OnDrawGizmos()
    {
        if (!drawGizmos) return;
        Gizmos.color = new Color(1f, 0.6f, 0.15f, 0.9f);
        for (int i = 0; i < openAnchors.Count; i++)
        {
            var a = openAnchors[i]; if (!a) continue;
            Gizmos.DrawSphere(a.transform.position, 0.06f);
            Gizmos.DrawRay(a.transform.position, a.transform.forward * 0.4f);
        }
    }
#endif

    private void BakeNavMeshIfReady()
    {
        var rootGO = levelRoot ? levelRoot.gameObject : gameObject;
        var baker = rootGO.GetComponent<NavMeshRuntimeBaker>();
        if (baker) baker.BakeNow();
    }

    /// <summary>0=straight, 1=90°.</summary>
    private float ComputeTurnScore(DoorAnchor target, DoorAnchor chosenOnCandidate)
    {
        Vector3 inDir = target.transform.forward.normalized;
        Vector3 outDir = (-chosenOnCandidate.transform.forward).normalized;
        float dot = Mathf.Clamp01(Vector3.Dot(inDir, outDir));
        return 1f - dot;
    }
    private void HardAlignRoomAtAnchor(GameObject room, DoorAnchor roomAnchor, DoorAnchor target)
    {
        // Build rotations that define “forward” and “up” for each anchor.
        // We use target.up to remove any roll; if your anchors sit on flat floors, this keeps rooms upright.
        Quaternion from = Quaternion.LookRotation(roomAnchor.transform.forward, roomAnchor.transform.up);
        Quaternion to = Quaternion.LookRotation(-target.transform.forward, target.transform.up);

        // Rotate room so its anchor forward/up matches the target’s opposing forward/up.
        Quaternion delta = to * Quaternion.Inverse(from);
        room.transform.rotation = delta * room.transform.rotation;

        // Then translate so anchor centers coincide exactly.
        Vector3 afterAnchorPos = roomAnchor.transform.position; // now in new rotation
        Vector3 offset = target.transform.position - afterAnchorPos;
        room.transform.position += offset;
    }
}
