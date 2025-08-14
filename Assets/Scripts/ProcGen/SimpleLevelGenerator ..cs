using System.Collections.Generic;
using UnityEngine;

public class SimpleLevelGenerator : MonoBehaviour
{
    [Header("Prefabs")]
    [SerializeField] private GameObject startRoomPrefab;
    [SerializeField] private List<GameObject> roomPrefabs = new List<GameObject>();

    [Header("Hierarchy (optional)")]
    [SerializeField] private Transform levelRoot; // if null, we create one

    [Header("Generation")]
    [SerializeField, Min(1)] private int maxRooms = 6;
    [SerializeField] private bool autoGenerateOnPlay = true;
    [SerializeField] private int seed = 42; // -1 for random
    [SerializeField, Min(1)] private int maxPlacementAttemptsPerRoom = 30;
    [SerializeField] private bool avoidImmediateStraight = true;

    [Header("Overlap Check")]
    [Tooltip("Only colliders on these layers are used to detect overlap. Put each room's RoomBounds collider on one of these layers (e.g., GenBounds).")]
    [SerializeField] private LayerMask placementCollisionMask;

    [Header("Unused Door Sealing (optional)")]
    [Tooltip("Optional. If assigned, unused doorways get sealed with this simple blocker (e.g., a cube). DO NOT assign your decorative DoorCap here.")]
    [SerializeField] private GameObject doorBlockerPrefab;

    [Header("Debug")]
    [SerializeField] private bool logSteps = false;
    [SerializeField] private bool drawGizmos = true;

    // runtime
    private System.Random rng;
    private readonly List<DoorAnchor> openAnchors = new();
    private readonly HashSet<DoorAnchor> usedAnchors = new();
    private readonly List<GameObject> spawnedRooms = new();
    private Vector3 lastGrowthDir = Vector3.zero;

    private void Awake()
    {
        if (!ValidateSerializedFields()) { enabled = false; return; }
        if (levelRoot == null)
        {
            var go = new GameObject("LevelRoot");
            levelRoot = go.transform;
            levelRoot.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
        }
        rng = (seed < 0) ? new System.Random() : new System.Random(seed);
    }

    private void Start()
    {
        if (autoGenerateOnPlay)
        {
            ClearLevel();
            Generate();
        }
    }

    [ContextMenu("Generate Now")]
    public void Generate()
    {
        ClearLevel();

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
            // pull a target anchor (FIFO)
            var target = openAnchors[0];
            openAnchors.RemoveAt(0);
            if (target == null) continue;
            if (usedAnchors.Contains(target)) continue;

            bool placed = TryAttachRoomAt(target, ref roomsPlaced, ref failsInARow);

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
            foreach (var a in openAnchors)
            {
                if (a == null || usedAnchors.Contains(a)) continue;
                PlaceBlockerAt(a);
            }
        }
        BakeNavMeshIfReady();



        if (logSteps) Debug.Log($"[Generator] Done. Spawned {spawnedRooms.Count} room(s). Open anchors left: {openAnchors.Count}");
   
    }

    private bool TryAttachRoomAt(DoorAnchor targetAnchor, ref int roomsPlaced, ref int failsInARow)
    {
        // choose a few random prefabs to try
        for (int attempt = 0; attempt < maxPlacementAttemptsPerRoom; attempt++)
        {
            var prefab = roomPrefabs[rng.Next(roomPrefabs.Count)];
            var room = Instantiate(prefab, levelRoot);
            ForceUnitScale(room.transform);

            var chosenAnchor = ChooseAnchorFacingOpposite(room, targetAnchor);
            if (chosenAnchor == null)
            {
                Destroy(room);
                continue;
            }

            AlignRoom(room, chosenAnchor, targetAnchor);

            if (avoidImmediateStraight && lastGrowthDir != Vector3.zero)
            {
                // discourage placing a room that continues exactly straight from the last step
                var dir = (targetAnchor.transform.forward).normalized;
                if (Vector3.Dot(dir, lastGrowthDir) > 0.95f)
                {
                    // too straight; try a different candidate
                    Destroy(room);
                    continue;
                }
            }

            if (OverlapsExisting(room))
            {
                Destroy(room);
                continue;
            }

            // accept placement
            spawnedRooms.Add(room);
            usedAnchors.Add(targetAnchor);
            usedAnchors.Add(chosenAnchor);
            roomsPlaced++;
            lastGrowthDir = (targetAnchor.transform.forward).normalized;

            // bring in the new room's unused anchors
            AddRoomAnchorsToOpenList(room, exclude: chosenAnchor);

            if (logSteps) Debug.Log($"[Generator] Placed '{prefab.name}' at attempt {attempt + 1}. Rooms: {roomsPlaced}");
            return true;
        }
        return false;
    }

    private DoorAnchor ChooseAnchorFacingOpposite(GameObject roomInstance, DoorAnchor target)
    {
        var anchors = roomInstance.GetComponentsInChildren<DoorAnchor>(true);
        if (anchors == null || anchors.Length == 0) return null;

        DoorAnchor best = null;
        float bestScore = -1f;
        Vector3 neededDir = -target.transform.forward; // want approx opposite

        foreach (var a in anchors)
        {
            if (a == null) continue;
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

        // 2) After rotating the whole room, move so anchors coincide
        Vector3 offset = target.transform.position - roomAnchor.transform.position;
        room.transform.position += offset;
    }

    private void AddRoomAnchorsToOpenList(GameObject room, DoorAnchor exclude)
    {
        var anchors = room.GetComponentsInChildren<DoorAnchor>(true);
        foreach (var a in anchors)
        {
            if (a == null) continue;
            if (a == exclude) continue;
            if (usedAnchors.Contains(a)) continue;
            openAnchors.Add(a);
        }
    }

    private void PlaceBlockerAt(DoorAnchor a)
    {
        if (doorBlockerPrefab == null || a == null) return;
        var b = Instantiate(doorBlockerPrefab, a.transform.position, a.transform.rotation, levelRoot);
        // Optional: nudge a tiny bit so it doesn’t z-fight with trims
        b.transform.position += a.transform.forward * 0.01f;
    }

    private void ForceUnitScale(Transform t)
    {
        // make sure rooms are at scale (1,1,1) regardless of prefab import state
        t.localScale = Vector3.one;
    }

    private bool OverlapsExisting(GameObject candidate)
    {
        // We check all colliders under candidate that are on the placementCollisionMask.
        // For each, run Physics.OverlapBox against the world, then reject hits that belong to candidate itself.
        var colliders = candidate.GetComponentsInChildren<Collider>(true);
        bool anyRelevant = false;

        foreach (var c in colliders)
        {
            if (c == null) continue;
            if (((1 << c.gameObject.layer) & placementCollisionMask.value) == 0) continue; // skip layers we don't check
            anyRelevant = true;

            // support BoxCollider and Capsule/Sphere by approximating with bounds box
            Quaternion rot = c.transform.rotation;
            Bounds b = c.bounds; // world space
            Vector3 half = b.extents;
            Collider[] hits = Physics.OverlapBox(b.center, half, rot, placementCollisionMask, QueryTriggerInteraction.Ignore);
            foreach (var h in hits)
            {
                if (h == null) continue;
                if (h.transform.root == candidate.transform.root) continue; // ignore self
                return true; // overlap with existing
            }
        }

        if (!anyRelevant && logSteps)
        {
            Debug.LogWarning("[Generator] Candidate room has no colliders on placementCollisionMask. Add a 'RoomBounds' BoxCollider on an included layer.");
        }
        return false;
    }

    private void ClearLevel()
    {
        openAnchors.Clear();
        usedAnchors.Clear();
        foreach (var r in spawnedRooms)
        {
            if (r != null) DestroyImmediate(r);
        }
        spawnedRooms.Clear();
    }

    private bool ValidateSerializedFields()
    {
        bool ok = true;
        if (startRoomPrefab == null) { Debug.LogError("[Generator] Start Room Prefab is not assigned."); ok = false; }
        if (roomPrefabs == null || roomPrefabs.Count == 0) { Debug.LogError("[Generator] Room Prefabs list is empty."); ok = false; }

        if (placementCollisionMask.value == 0)
        {
            // Default to Everything so overlaps still work, and let you know politely.
            placementCollisionMask = ~0;
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
        // draw open anchors in play mode for quick visual
        Gizmos.color = new Color(1f, 0.6f, 0.15f, 0.9f);
        foreach (var a in openAnchors)
        {
            if (a == null) continue;
            Gizmos.DrawSphere(a.transform.position, 0.06f);
            Gizmos.DrawRay(a.transform.position, a.transform.forward * 0.4f);
        }
    }
#endif
    // Put this inside the class SimpleLevelGenerator, not inside Generate()
    void BakeNavMeshIfReady()
    {
        var rootGO = levelRoot ? levelRoot.gameObject : gameObject;
        var baker = rootGO.GetComponent<NavMeshRuntimeBaker>();
        if (baker) baker.BakeNow();
    }
}


