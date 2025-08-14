// SimpleLevelGenerator.cs
using UnityEngine;
using System.Collections.Generic;
using Unity.AI.Navigation; // for NavMeshSurface

public class SimpleLevelGenerator : MonoBehaviour
{
    [Header("Prefabs")]
    public Room startRoomPrefab;
    public List<Room> roomPrefabs = new List<Room>();

    [Header("Generation")]
    public int roomCount = 10;

    [Header("Debug")]
    public bool forceSpawnLine = false;
    public float lineStep = 10f;

    [Header("Parent (optional)")]
    public Transform levelRoot; // can be null; used to keep hierarchy tidy

    [Header("Rotation Variety")]
    [SerializeField] private bool randomizeRoomYaw = true;
    [SerializeField] private int yawStepDegrees = 90; // 90 for gridlike rotation

    [Header("Overlap Settings")]
    [SerializeField] private LayerMask overlapLayerMask;
    [SerializeField] private float overlapPadding = 0.05f;

    [Header("Capping / Blockers")]
    [SerializeField] private GameObject doorBlockerPrefab; // a 1x1x1 cube works great
    [SerializeField] private float blockerForwardInset = 0.0f; // small push along +Z if you see z-fighting

    [Header("Randomness")]
    [SerializeField] private bool useSeed = false;
    [SerializeField] private int seed = 12345;

    [Header("Branching")]
    [SerializeField] private bool useBreadthFirst = true;
    [SerializeField] private int breadthSample = 4;   // choose from first N anchors for more branching

    [Header("Layout Control")]
    [SerializeField] private int maxDepth = 100;
    [SerializeField] private bool avoidRepeatPrefab = true;

    [Header("Navigation")]
    [SerializeField] private bool buildNavMeshAfterGen = true;

    // Tracking
    private readonly List<Room> _placed = new List<Room>();

    private void Start()
    {
        if (useSeed) Random.InitState(seed);
        Generate();
    }

    private void Generate()
    {
        _placed.Clear();
        Room lastPrefabUsed = null;

        // --- Place start room at origin ---
        var start = Instantiate(startRoomPrefab, Vector3.zero, Quaternion.identity, levelRoot);
        ApplyRandomYaw(start);
        _placed.Add(start);
        Debug.Log("[Gen] Start placed. Anchors on start: " + (start.anchors == null ? 0 : start.anchors.Count));

        // ---------- DEBUG: Force a straight line ----------
        if (forceSpawnLine)
        {
            int spawned = _placed.Count; // start room already placed
            int i = 0;
            int safety = 1000;

            while (spawned < roomCount && safety-- > 0)
            {
                var prefab = PickRoomPrefab(lastPrefabUsed);
                lastPrefabUsed = prefab;

                var pos = new Vector3(0f, 0f, (i + 1) * lineStep);
                var newRoom = Instantiate(prefab, pos, Quaternion.identity, levelRoot);
                _placed.Add(newRoom);
                spawned++;
                i++;

                int anchorCount = (newRoom.anchors == null) ? 0 : newRoom.anchors.Count;
                Debug.Log("[Gen] Forced line spawn #" + spawned + " at Z=" + pos.z + ". Anchors=" + anchorCount);
            }

            if (safety <= 0) Debug.LogWarning("[Gen] Safety tripped in forced-line mode.");
            SealRemainingAnchors(new List<(Room room, DoorAnchor anchor, int depth)>()); // no openAnchors in this mode
            MaybeBuildNav();
            return;
        }

        // ---------- NORMAL ANCHOR MODE ----------
        // Tuple holds (room, anchor, depth)
        List<(Room room, DoorAnchor anchor, int depth)> openAnchors
            = new List<(Room room, DoorAnchor anchor, int depth)>();

        if (start.anchors != null)
        {
            foreach (var a in start.anchors) openAnchors.Add((start, a, 0));
        }

        int safetyNormal = 2000; // prevents infinite loops if overlaps stall progress

        // place until we hit target rooms or run out of anchors
        while (_placed.Count < roomCount && openAnchors.Count > 0 && safetyNormal-- > 0)
        {
            // --- choose a target anchor ---
            int targetIndex;
            if (useBreadthFirst)
            {
                int pool = Mathf.Min(breadthSample, openAnchors.Count);
                targetIndex = Random.Range(0, pool); // earlier entries → more branching
            }
            else
            {
                targetIndex = Random.Range(0, openAnchors.Count);
            }

            // try to avoid immediately continuing straight from the most recent anchor (if we have a choice)
            if (openAnchors.Count > 1)
            {
                var (recentRoom, recentAnchor, _) = openAnchors[0];
                var (candidateRoom, candidateAnchor, _) = openAnchors[targetIndex];

                if (candidateRoom == recentRoom && IsOppositeOf(candidateAnchor, recentAnchor))
                {
                    int pool = Mathf.Min(breadthSample, openAnchors.Count);
                    for (int tryIdx = 0; tryIdx < pool; tryIdx++)
                    {
                        if (tryIdx == targetIndex) continue;
                        var (altRoom, altAnchor, _) = openAnchors[tryIdx];
                        if (!(altRoom == recentRoom && IsOppositeOf(altAnchor, recentAnchor)))
                        {
                            targetIndex = tryIdx;
                            break;
                        }
                    }
                }
            }

            var (targetRoom, targetAnchor, targetDepth) = openAnchors[targetIndex];

            // --- pick and instantiate a candidate room ---
            var prefab = PickRoomPrefab(lastPrefabUsed);
            lastPrefabUsed = prefab;

            var newRoom = Instantiate(prefab, levelRoot);
            ApplyRandomYaw(newRoom);

            // collect anchors from the newly placed candidate room
            var newAnchors = newRoom.anchors;
            if (newAnchors == null || newAnchors.Count == 0)
            {
                Debug.LogWarning("[Gen] New room had 0 anchors. Destroying and retrying.");
                Destroy(newRoom.gameObject);
                openAnchors.RemoveAt(targetIndex);
                continue;
            }

            // choose best-facing anchor on the new room
            var newAnchor = FindBestAnchorFacing(newRoom, targetAnchor.transform.forward);
            if (newAnchor == null)
            {
                Debug.LogWarning("[Gen] Could not find anchor on new room. Destroying and retrying.");
                Destroy(newRoom.gameObject);
                openAnchors.RemoveAt(targetIndex);
                continue;
            }

            // align/snap, then check overlap
            AlignRoomToConnectAnchors(newRoom, newAnchor, targetAnchor);
            if (OverlapsAnything(newRoom, targetRoom))
            {
                Destroy(newRoom.gameObject);
                openAnchors.RemoveAt(targetIndex);
                Debug.Log("[Gen] Rejected overlapping room. Retrying with a new target.");
                continue;
            }

            // mark both ends so sealing skips them
            targetAnchor.MarkConnected();
            newAnchor.MarkConnected();

            // commit placement
            _placed.Add(newRoom);
            openAnchors.RemoveAt(targetIndex);

            // push remaining anchors from the new room
            int nextDepth = targetDepth + 1;
            int pushed = 0;
            for (int ai = 0; ai < newAnchors.Count; ai++)
            {
                var a = newAnchors[ai];
                if (a == newAnchor) continue;
                if (nextDepth <= maxDepth)
                {
                    openAnchors.Add((newRoom, a, nextDepth));
                    pushed++;
                }
            }

            Debug.Log("[Gen] Placed via anchors. Pushed " + pushed + " anchors (open=" + openAnchors.Count + "), depth=" + nextDepth);
        }

        if (safetyNormal <= 0)
        {
            Debug.LogWarning("[Gen] Safety tripped in normal mode (likely repeated overlaps).");
        }

        Debug.Log("[Gen] Finished with placed=" + _placed.Count + " openAnchors=" + openAnchors.Count);

        // Seal any unused doors & maybe build nav
        SealRemainingAnchors(openAnchors);
        MaybeBuildNav();
    }

    // ---- Helpers ----

    private Room PickRoomPrefab(Room lastUsed)
    {
        if (roomPrefabs == null || roomPrefabs.Count == 0) return null;

        if (!avoidRepeatPrefab || roomPrefabs.Count == 1)
            return roomPrefabs[Random.Range(0, roomPrefabs.Count)];

        // Try up to a few picks to avoid the same prefab twice
        for (int tries = 0; tries < 4; tries++)
        {
            var p = roomPrefabs[Random.Range(0, roomPrefabs.Count)];
            if (p != lastUsed) return p;
        }
        // fallback
        foreach (var p in roomPrefabs) if (p != lastUsed) return p;
        return roomPrefabs[0];
    }

    private DoorAnchor FindBestAnchorFacing(Room newRoom, Vector3 targetForward)
    {
        if (newRoom == null || newRoom.anchors == null || newRoom.anchors.Count == 0)
            return null;

        Vector3 desired = -targetForward.normalized; // face-to-face
        DoorAnchor best = newRoom.anchors[0];
        float bestDot = -1e9f;

        foreach (var a in newRoom.anchors)
        {
            float d = Vector3.Dot(a.transform.forward.normalized, desired);
            if (d > bestDot)
            {
                bestDot = d;
                best = a;
            }
        }
        return best;
    }

    private bool IsOppositeOf(DoorAnchor a, DoorAnchor b, float cosineThreshold = 0.98f)
    {
        // true if a.forward ≈ -b.forward
        return Vector3.Dot(a.transform.forward.normalized, -b.transform.forward.normalized) > cosineThreshold;
    }

    private bool OverlapsAnything(Room room, Room ignoreRoom = null)
    {
        var box = room.GetComponentInChildren<BoxCollider>();
        if (box == null)
        {
            Debug.LogWarning("[Overlap] No BoxCollider 'Bounds' found in " + room.name + ". Add one as a footprint.");
            return false; // allow if none, for now
        }

        Vector3 worldCenter = box.transform.TransformPoint(box.center);
        Vector3 half = Vector3.Scale(box.size * 0.5f, box.transform.lossyScale) + new Vector3(overlapPadding, overlapPadding, overlapPadding);
        Quaternion worldRot = box.transform.rotation;

        var hits = Physics.OverlapBox(
            worldCenter,
            half,
            worldRot,
            overlapLayerMask,
            QueryTriggerInteraction.Collide
        );

        foreach (var h in hits)
        {
            if (!h) continue;
            if (h.transform.IsChildOf(room.transform)) continue;                 // ignore self
            if (ignoreRoom != null && h.transform.IsChildOf(ignoreRoom.transform)) continue; // ignore the room we're joining
            return true; // anything else = overlap
        }
        return false;
    }

    // Rotate so anchors face, snap positions to coincide, then flush based on edgeOffset
    private void AlignRoomToConnectAnchors(Room newRoom, DoorAnchor newAnchor, DoorAnchor targetAnchor)
    {
        if (newRoom == null || newAnchor == null || targetAnchor == null)
        {
            Debug.LogError("AlignRoomToConnectAnchors: missing references.");
            return;
        }

        // 1) rotate so the two anchors face each other
        Quaternion rotDelta = Quaternion.FromToRotation(newAnchor.transform.forward, -targetAnchor.transform.forward);
        newRoom.transform.rotation = rotDelta * newRoom.transform.rotation;

        // 2) move so positions coincide
        Vector3 delta = targetAnchor.transform.position - newAnchor.transform.position;
        newRoom.transform.position += delta;

        // 3) flush faces based on edgeOffset
        Vector3 targetFace = targetAnchor.transform.position + targetAnchor.transform.forward * targetAnchor.edgeOffset;
        Vector3 newFace = newAnchor.transform.position - newAnchor.transform.forward * newAnchor.edgeOffset;
        Vector3 faceDelta = targetFace - newFace;
        newRoom.transform.position += faceDelta;

        Debug.DrawRay(targetAnchor.transform.position, targetAnchor.transform.forward * 0.5f, Color.green, 5f);
        Debug.DrawRay(newAnchor.transform.position, newAnchor.transform.forward * 0.5f, Color.cyan, 5f);
        Debug.Log("[Align] " + newRoom.name + " faceDelta=" + faceDelta.magnitude.ToString("F3"));
    }

    private void ApplyRandomYaw(Room room)
    {
        if (!randomizeRoomYaw || room == null) return;
        int steps = Mathf.Max(1, 360 / Mathf.Max(1, yawStepDegrees));
        int k = Random.Range(0, steps); // 0..steps-1
        float yaw = k * yawStepDegrees;
        room.transform.rotation = Quaternion.AngleAxis(yaw, Vector3.up) * room.transform.rotation;
    }

    private void SealRemainingAnchors(List<(Room room, DoorAnchor anchor, int depth)> openAnchors)
    {
        if (doorBlockerPrefab == null)
        {
            Debug.LogWarning("[Cap] doorBlockerPrefab is not assigned.");
            return;
        }

        int sealedCount = 0;

        // 1) seal any unconsumed open anchors
        if (openAnchors != null)
        {
            foreach (var entry in openAnchors)
            {
                var anchor = entry.anchor;
                TryPlaceBlockerAtAnchor(anchor, ref sealedCount);
            }
        }

        // 2) also sweep already placed rooms for any anchors that never got connected
        for (int r = 0; r < _placed.Count; r++)
        {
            var room = _placed[r];
            if (room == null || room.anchors == null) continue;
            for (int a = 0; a < room.anchors.Count; a++)
            {
                TryPlaceBlockerAtAnchor(room.anchors[a], ref sealedCount);
            }
        }

        Debug.Log("[Cap] Sealed " + sealedCount + " unconnected door anchors.");
    }

    private void TryPlaceBlockerAtAnchor(DoorAnchor anchor, ref int sealedCount)
    {
        if (anchor == null) return;
        if (anchor.IsConnected) return;

        var blocker = Instantiate(
            doorBlockerPrefab,
            anchor.transform.position,
            anchor.transform.rotation,
            levelRoot != null ? levelRoot : null
        );

        // scale to fit doorway (assumes blocker prefab is a unit cube aligned to +Z forward)
        var t = blocker.transform;
        float w = Mathf.Max(0.01f, anchor.doorwayWidth);
        float h = Mathf.Max(0.01f, anchor.doorwayHeight);
        float thick = Mathf.Max(0.01f, anchor.blockerThickness);

        t.localScale = new Vector3(w, h, thick);

        // position: by default anchor sits at doorway base at the wall plane, facing outward (+Z).
        // lift half-height if the pivot is at floor; if the pivot is at center, skip this.
        if (anchor.pivotAtFloor)
        {
            t.Translate(0f, h * 0.5f, 0f, Space.Self);
        }

        // push forward so the cube volume sits flush in the opening (half its thickness inside the doorway)
        t.Translate(0f, 0f, (thick * 0.5f) + blockerForwardInset, Space.Self);

        sealedCount++;
    }

    private void MaybeBuildNav()
    {
        if (!buildNavMeshAfterGen) return;

        var surface = Object.FindFirstObjectByType<NavMeshSurface>();
        if (surface != null)
        {
            surface.BuildNavMesh();
            Debug.Log("[Nav] Built runtime NavMesh.");
        }
        else
        {
            Debug.LogWarning("[Nav] No NavMeshSurface found in scene.");
        }
    }
}

