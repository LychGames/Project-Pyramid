using System.Collections.Generic;
using UnityEngine;

public class SimpleLevelGenerator : MonoBehaviour
{
    // ─────────────────────────────────────────────────────────────────────────────
    //  Inspector
    // ─────────────────────────────────────────────────────────────────────────────

    [Header("Prefabs")]
    [SerializeField] private GameObject startRoomPrefab;
    [SerializeField] private List<GameObject> roomPrefabs = new List<GameObject>();

    [Header("Hierarchy (optional)")]
    [SerializeField] private Transform levelRoot; // if null, we create one

    [Header("Generation")]
    [SerializeField, Min(1)] private int maxRooms = 6;
    [SerializeField] private bool autoGenerateOnPlay = true;

    [Tooltip("If ON, a fresh random seed is used each Generate(). If OFF, 'seed' is used.")]
    [SerializeField] private bool randomizeSeed = true;

    [SerializeField] private int seed = 42; // used when randomizeSeed == false
    [SerializeField, Min(1)] private int maxPlacementAttemptsPerRoom = 30;
    [SerializeField] private bool avoidImmediateStraight = true;

    [Header("Overlap Check")]
    [Tooltip("Only colliders on these layers are used to detect overlap. Put each room's RoomBounds collider on one of these layers (e.g., GenBounds).")]
    [SerializeField] private LayerMask placementCollisionMask = ~0;

    [Header("Unused Door Sealing (optional)")]
    [Tooltip("Optional. If assigned, unused doorways get sealed with this simple blocker (e.g., a cube). DO NOT assign your decorative DoorCap here.")]
    [SerializeField] private GameObject doorBlockerPrefab;

    [Header("Debug")]
    [SerializeField] private bool logSteps = false;
    [SerializeField] private bool drawGizmos = true;

    [Header("Runtime (info)")]
    [SerializeField, Tooltip("The seed actually used for the last Generate() call.")]
    private int usedSeed = 0;
    [Header("Flow Bias")]
    [SerializeField, Range(0f, 1f)] private float preferTurnBias = 0.35f; // 0 = no bias, 0.35 feels good
    [SerializeField, Min(0)] private int forceFirstTurnWithin = 3;         // guarantee at least one turn early (0 = off)

    // ─────────────────────────────────────────────────────────────────────────────
    //  Runtime state (no allocation in inner loops)
    // ─────────────────────────────────────────────────────────────────────────────

    private System.Random rng;
    private readonly List<DoorAnchor> openAnchors = new();
    private readonly HashSet<DoorAnchor> usedAnchors = new();
    private readonly List<GameObject> spawnedRooms = new();
    private Vector3 lastGrowthDir = Vector3.zero;
    // runtime counters per prefab
    private readonly Dictionary<GameObject, int> _spawnCounts = new();
    private int _placedSummonRooms = 0;

    // Overlap non-alloc cache (reduces GC during placement)
    private const int OverlapCache = 64;
    private static readonly Collider[] overlapHits = new Collider[OverlapCache];

    // ─────────────────────────────────────────────────────────────────────────────
    //  Unity
    // ─────────────────────────────────────────────────────────────────────────────

    private void Awake()
    {
        if (!ValidateSerializedFields()) { enabled = false; return; }

        if (levelRoot == null)
        {
            var go = new GameObject("LevelRoot");
            levelRoot = go.transform;
            levelRoot.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
        }
    }

    private void Start()
    {
        if (autoGenerateOnPlay)
        {
            ClearLevel();
            Generate();
        }
    }

    private void Update()
    {
        // Handy editor-time test: press R to regenerate while in Play Mode
        if (Application.isPlaying && Input.GetKeyDown(KeyCode.R))
        {
            ClearLevel();
            Generate();
        }
    }

    // ─────────────────────────────────────────────────────────────────────────────
    //  Public / Context
    // ─────────────────────────────────────────────────────────────────────────────

    [ContextMenu("Generate Now")]
    public void Generate()
    {
        void InitRng()
        {
            usedSeed = randomizeSeed
                ? System.Environment.TickCount ^ System.Guid.NewGuid().GetHashCode()  // avoids repeats on fast regen
                : seed;

            rng = new System.Random(usedSeed);
            Random.InitState(usedSeed);
            if (logSteps) Debug.Log($"[Gen] Seed = {usedSeed}");
        }

        ClearLevel();
        InitRng();

        // 1) Spawn start room
        var startRoom = Instantiate(startRoomPrefab, levelRoot);
        ForceUnitScale(startRoom.transform);
        spawnedRooms.Add(startRoom);

        // collect its anchors
        AddRoomAnchorsToOpenList(startRoom, exclude: null);

        int roomsPlaced = 1; // start already placed
        int failsInARow = 0;
        lastGrowthDir = Vector3.zero;

        // 2) Growth loop
        int safety = maxRooms * maxPlacementAttemptsPerRoom * 3;
        while (roomsPlaced < maxRooms && openAnchors.Count > 0 && safety-- > 0)
        {
            // pull a target anchor (FIFO for consistent breadth)
            var target = openAnchors[0];
            openAnchors.RemoveAt(0);
            if (!target || usedAnchors.Contains(target)) continue;

            bool placed = TryAttachRoomAt(target, ref roomsPlaced);

            if (!placed)
            {
                failsInARow++;
                if (failsInARow > maxPlacementAttemptsPerRoom * 2)
                {
                    if (logSteps) Debug.LogWarning("[Generator] Too many failed placements; stopping early.");
                    break;
                }
            }
            else
            {
                failsInARow = 0;
            }
        }

        // 3) Seal remaining unused anchors (optional)
        if (doorBlockerPrefab != null)
        {
            for (int i = 0; i < openAnchors.Count; i++)
            {
                var a = openAnchors[i];
                if (!a || usedAnchors.Contains(a)) continue;
                PlaceBlockerAt(a);
            }
        }

        BakeNavMeshIfReady();

        if (logSteps) Debug.Log($"[Generator] Done. Spawned {spawnedRooms.Count} room(s). Open anchors left: {openAnchors.Count}. Seed={usedSeed}");
    }

    // ─────────────────────────────────────────────────────────────────────────────
    //  Core placement
    // ─────────────────────────────────────────────────────────────────────────────

    private bool TryAttachRoomAt(DoorAnchor targetAnchor, ref int roomsPlaced)
    {
        float ComputeTurnScore(DoorAnchor target, DoorAnchor chosenOnCandidate)
        {
            // Incoming direction is where the target room was growing (its forward)
            Vector3 inDir = target.transform.forward.normalized;
            // Candidate will continue in the opposite of its chosen door's forward
            Vector3 outDir = (-chosenOnCandidate.transform.forward).normalized;

            float dot = Mathf.Clamp01(Vector3.Dot(inDir, outDir)); // 1 = straight, 0 = 90-degree turn
            float turn = 1f - dot; // 0=straight, 1=hard turn
            return turn;
        }

        // choose a few random prefabs to try
        int prefabCount = roomPrefabs.Count;
        for (int attempt = 0; attempt < maxPlacementAttemptsPerRoom; attempt++)
        {
            var prefab = PickNextPrefabFor(targetAnchor);
            if (prefab == null) return false;

            // ---- SAMPLE A FEW CANDIDATES AND PICK THE BEST TURN ----
            const int samples = 6;

            GameObject chosenPrefabAsset = null;   // asset from roomPrefabs list
            GameObject chosenGhost = null;         // inactive instance we will accept
            DoorAnchor chosenDoor = null;          // the door on the chosen ghost
            float chosenScore = float.NegativeInfinity;

            for (int s = 0; s < samples; s++)
            {
                var prefabAsset = PickNextPrefabFor(targetAnchor);
                if (prefabAsset == null) break;

                // spawn a test instance (inactive)
                var ghost = Instantiate(prefabAsset, levelRoot);
                ghost.SetActive(false);

                var door = ChooseAnchorFacingOpposite(ghost, targetAnchor);
                if (!door) { DestroyImmediate(ghost); continue; }

                // align and check overlap
                AlignRoom(ghost, door, targetAnchor);
                if (OverlapsExisting(ghost)) { DestroyImmediate(ghost); continue; }

                // score: prefer turns over straight
                float turn = ComputeTurnScore(targetAnchor, door); // 0=straight, 1=90°+
                float score = (preferTurnBias > 0f) ? turn * preferTurnBias : 0f;

                // keep the best
                if (score > chosenScore)
                {
                    if (chosenGhost) DestroyImmediate(chosenGhost); // discard previous best
                    chosenGhost = ghost;
                    chosenDoor = door;
                    chosenPrefabAsset = prefabAsset;
                    chosenScore = score;
                }
                else
                {
                    DestroyImmediate(ghost); // discard this trial
                }
            }

            // nothing viable
            if (!chosenGhost) return false;

            // ---- ACCEPT THE WINNER ----
            chosenGhost.SetActive(true);
            spawnedRooms.Add(chosenGhost);
            usedAnchors.Add(targetAnchor);
            usedAnchors.Add(chosenDoor);
            roomsPlaced++;
            lastGrowthDir = (targetAnchor.transform.forward).normalized;

            // bring in the new room's other doors
            AddRoomAnchorsToOpenList(chosenGhost, exclude: chosenDoor);

            // track counts by PREFAB ASSET (so limits/uniques work)
            if (!_spawnCounts.ContainsKey(chosenPrefabAsset)) _spawnCounts[chosenPrefabAsset] = 0;
            _spawnCounts[chosenPrefabAsset]++;

            var metaPlaced = chosenGhost.GetComponent<RoomMeta>();
            if (metaPlaced && metaPlaced.category == RoomCategory.Summon) _placedSummonRooms++;

            if (logSteps) Debug.Log($"[Generator] Placed '{chosenGhost.name}' turnScore={chosenScore:F2}.");
            return true;

        }
        return false;
    }
   
    // Weighted pick that respects RoomMeta limits & door rules
    GameObject PickNextPrefabFor(DoorAnchor targetAnchor)
    {
        float total = 0f;
        var bag = new List<(GameObject prefab, float w)>();

        for (int i = 0; i < roomPrefabs.Count; i++)
        {
            var p = roomPrefabs[i];
            if (!p) continue;

            var meta = p.GetComponent<RoomMeta>();
            if (!meta) continue;

            // --- limits ---
            _spawnCounts.TryGetValue(p, out int used);
            if (meta.uniqueOnce && used > 0) continue;
            if (meta.maxCount > 0 && used >= meta.maxCount) continue;

            // --- door compatibility ---
            bool okDoor = meta.connectsAll;
            if (!okDoor)
            {
                // If target door only allows some categories, enforce it
                if ((targetAnchor.allowedTargets & meta.category) != 0) okDoor = true;
            }
            if (!okDoor) continue;

            // example rarity: allow at most one Summon
            if (meta.category == RoomCategory.Summon && _placedSummonRooms >= 1) continue;

            float w = Mathf.Max(0.0001f, meta.weight);
            total += w;
            bag.Add((p, w));
        }

        if (bag.Count == 0) return null;

        float r = (float)rng.NextDouble() * total;
        foreach (var (prefab, w) in bag)
        {
            if ((r -= w) <= 0f) return prefab;
        }
        return bag[bag.Count - 1].prefab;
    }

    private DoorAnchor ChooseAnchorFacingOpposite(GameObject roomInstance, DoorAnchor target)
    {
        var anchors = roomInstance.GetComponentsInChildren<DoorAnchor>(true);
        if (anchors == null || anchors.Length == 0) return null;

        DoorAnchor best = null;
        float bestScore = -1f;
        Vector3 neededDir = -target.transform.forward; // want approx opposite

        for (int i = 0; i < anchors.Length; i++)
        {
            var a = anchors[i];
            if (!a) continue;
            if (usedAnchors.Contains(a)) continue;

            float score = Vector3.Dot(a.transform.forward.normalized, neededDir);
            if (score > bestScore)
            {
                bestScore = score;
                best = a;
            }
        }
        return best;
    }

    private void AlignRoom(GameObject room, DoorAnchor roomAnchor, DoorAnchor target)
    {
        // 1) Rotate so roomAnchor.forward matches -target.forward
        Quaternion from = Quaternion.FromToRotation(roomAnchor.transform.forward, -target.transform.forward);
        room.transform.rotation = from * room.transform.rotation;

        // 2) Move so anchors coincide
        Vector3 offset = target.transform.position - roomAnchor.transform.position;
        room.transform.position += offset;
    }

    private void AddRoomAnchorsToOpenList(GameObject room, DoorAnchor exclude)
    {
        var anchors = room.GetComponentsInChildren<DoorAnchor>(true);
        for (int i = 0; i < anchors.Length; i++)
        {
            var a = anchors[i];
            if (!a) continue;
            if (a == exclude) continue;
            if (usedAnchors.Contains(a)) continue;
            openAnchors.Add(a);
        }
    }

    private void PlaceBlockerAt(DoorAnchor a)
    {
        if (doorBlockerPrefab == null || !a) return;
        var b = Instantiate(doorBlockerPrefab, a.transform.position, a.transform.rotation, levelRoot);
        // tiny nudge so it doesn’t z-fight with trims
        b.transform.position += a.transform.forward * 0.01f;
    }

    private void ForceUnitScale(Transform t)
    {
        // make sure rooms are at scale (1,1,1) regardless of prefab import state
        t.localScale = Vector3.one;
    }

    private bool OverlapsExisting(GameObject candidate)
    {
        // Check all colliders under candidate that are on the placementCollisionMask.
        // Use non-alloc OverlapBox for fewer GC spikes.
        var colliders = candidate.GetComponentsInChildren<Collider>(true);
        bool anyRelevant = false;

        for (int i = 0; i < colliders.Length; i++)
        {
            var c = colliders[i];
            if (!c) continue;

            int layerBit = 1 << c.gameObject.layer;
            if ((placementCollisionMask.value & layerBit) == 0) continue; // skip layers we don't check
            anyRelevant = true;

            // Approximate all collider types using Bounds (world space AABB)
            Bounds b = c.bounds;
            Vector3 half = b.extents;

            // Note: using the collider's rotation is only meaningful for BoxCollider; for AABB we pass Quaternion.identity.
            int hitCount = Physics.OverlapBoxNonAlloc(b.center, half, overlapHits, Quaternion.identity, placementCollisionMask, QueryTriggerInteraction.Ignore);

            for (int h = 0; h < hitCount; h++)
            {
                var hit = overlapHits[h];
                if (!hit) continue;
                if (hit.transform.root == candidate.transform.root) continue; // ignore self
                return true; // overlap with existing
            }
        }

        if (!anyRelevant && logSteps)
        {
            Debug.LogWarning("[Generator] Candidate room has no colliders on placementCollisionMask. Add a 'RoomBounds' BoxCollider on an included layer.");
        }
        return false;
    }

    // ─────────────────────────────────────────────────────────────────────────────
    //  Seed / RNG
    // ─────────────────────────────────────────────────────────────────────────────

    private void InitRng()
    {
        usedSeed = randomizeSeed
    ? System.Environment.TickCount ^ System.Guid.NewGuid().GetHashCode()
    : seed;
        rng = new System.Random(usedSeed);
        Random.InitState(usedSeed);

    }

    // ─────────────────────────────────────────────────────────────────────────────
    //  Cleanup & validation
    // ─────────────────────────────────────────────────────────────────────────────

    private void ClearLevel()
    {
        openAnchors.Clear();
        usedAnchors.Clear();

        for (int i = 0; i < spawnedRooms.Count; i++)
        {
            var r = spawnedRooms[i];
            if (r) DestroyImmediate(r);
        }
        spawnedRooms.Clear();
        _spawnCounts.Clear();
        _placedSummonRooms = 0;
    }

    private bool ValidateSerializedFields()
    {
        bool ok = true;

        if (startRoomPrefab == null) { Debug.LogError("[Generator] Start Room Prefab is not assigned."); ok = false; }
        if (roomPrefabs == null || roomPrefabs.Count == 0) { Debug.LogError("[Generator] Room Prefabs list is empty."); ok = false; }

        if (placementCollisionMask.value == 0)
        {
            placementCollisionMask = ~0; // default to Everything so overlaps still work
            Debug.LogWarning("[Generator] placementCollisionMask was zero; defaulting to Everything. " +
                             "Tip: create a dedicated layer (e.g., 'GenBounds') for each RoomBounds collider " +
                             "and set the mask to that for faster/cleaner checks.");
        }
        return ok;
    }

#if UNITY_EDITOR
    private void OnDrawGizmos()
    {
        if (!drawGizmos) return;

        // Draw open anchors in play mode for quick visual
        Gizmos.color = new Color(1f, 0.6f, 0.15f, 0.9f);
        for (int i = 0; i < openAnchors.Count; i++)
        {
            var a = openAnchors[i];
            if (!a) continue;
            Gizmos.DrawSphere(a.transform.position, 0.06f);
            Gizmos.DrawRay(a.transform.position, a.transform.forward * 0.4f);
        }
    }
#endif

    // Keep this inside the class, not inside Generate()
    void BakeNavMeshIfReady()
    {
        var rootGO = levelRoot ? levelRoot.gameObject : gameObject;
        var baker = rootGO.GetComponent<NavMeshRuntimeBaker>();
        if (baker) baker.BakeNow();
    }
    float ComputeTurnScore(DoorAnchor target, DoorAnchor chosenOnCandidate)
    {
        Vector3 inDir = target.transform.forward.normalized;
        Vector3 outDir = (-chosenOnCandidate.transform.forward).normalized;
        float dot = Mathf.Clamp01(Vector3.Dot(inDir, outDir)); // 1 straight, 0 right-angle
        return 1f - dot;
    }
}
