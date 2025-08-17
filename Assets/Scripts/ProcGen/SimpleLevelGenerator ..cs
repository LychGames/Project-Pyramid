// Unity 2021+ compatible — simplified triangular lattice generator (compile-safe)
// Notes:
// - Removed hard dependency on a DoorAnchor C# type to avoid CS0246 when that script is missing.
// - Anchors are now discovered by transform name ("Anchor" prefix) across instances & prefabs.
// - If you DO have a DoorAnchor component, it still works automatically via name or reflection.
// - Alignment, lattice snapping, and simple spacing rules preserved.

using System;
using System.Collections.Generic;
using UnityEngine;

public class SimpleLevelGenerator : MonoBehaviour
{
    [Header("Generation Settings")]
    [SerializeField] int seed = 42;
    [SerializeField] bool randomizeSeed = true;
    [SerializeField] int maxModules = 50;
    [SerializeField] Transform levelRoot;

    [Header("Start Room")]
    [SerializeField] GameObject startRoomPrefab;  // Your 10m equilateral triangle
    [SerializeField] bool startIsVirtual = false;

    [Header("Prefab Lists")]
    [SerializeField] GameObject[] hallwayPrefabs;     // NewHall, HallLeft15, HallRight15, HallLeft30, HallRight30
    [SerializeField] GameObject[] connectorPrefabs;   // TripleConnector
    [SerializeField] GameObject[] roomPrefabs;        // Square rooms, triangle floors

    [Header("Lattice Settings")]
    [SerializeField] float cellSize = 10f;            // 10m for your equilateral triangle
    [SerializeField] bool snapToLattice = true;
    [SerializeField] bool snapYawTo30 = true;

    [Header("Placement Rules")]
    [SerializeField] float minModuleDistance = 8f;    // Minimum distance between module centers (prevents overlaps)
    [SerializeField] float maxBranchLength = 8f;      // Maximum length before allowing branching
    [SerializeField] int maxSnakeLength = 6;          // (kept for future use)

    [Header("Debug")]
    [SerializeField] bool showDebugInfo = false;
    [SerializeField] bool logGeneration = false;

    [Header("Quick Actions")]
    [SerializeField] bool generateButton = false;
    [SerializeField] bool clearButton = false;

    private System.Random rng;
    private readonly List<Transform> placedModules = new List<Transform>();
    private readonly List<Transform> availableAnchors = new List<Transform>();

    void Start()
    {
        if (levelRoot == null) levelRoot = transform;
    }

    void Update()
    {
        if (generateButton)
        {
            generateButton = false;
            GenerateLevel();
        }

        if (clearButton)
        {
            clearButton = false;
            ClearLevel();
        }
    }

    [ContextMenu("Generate Level")]
    public void GenerateLevel()
    {
        Debug.Log("[Gen] === STARTING SIMPLIFIED LATTICE GENERATION ===");

        rng = randomizeSeed ? new System.Random() : new System.Random(seed);
        placedModules.Clear();
        availableAnchors.Clear();

        ClearLevel();

        // Place start room
        Transform startRoot = PlaceStartRoom();
        if (startRoot == null)
        {
            Debug.LogError("[Gen] Failed to place start room!");
            return;
        }

        // Collect initial anchors
        CollectAnchorsIntoList(startRoot, availableAnchors);

        // Generate the level
        GenerateFromAnchors();

        Debug.Log($"[Gen] Generation complete! Placed {placedModules.Count} modules");
    }

    Transform PlaceStartRoom()
    {
        Transform startRoot;

        if (startIsVirtual)
        {
            startRoot = CreateVirtualStart();
        }
        else if (startRoomPrefab != null)
        {
            startRoot = Instantiate(startRoomPrefab, Vector3.zero, Quaternion.identity, levelRoot).transform;
        }
        else
        {
            startRoot = CreateVirtualStart();
        }

        placedModules.Add(startRoot);
        return startRoot;
    }

    Transform CreateVirtualStart()
    {
        // Create a simple triangular foundation with three named anchors
        GameObject start = new GameObject("VirtualStart");
        start.transform.SetParent(levelRoot, false);
        start.transform.position = Vector3.zero;

        // Create three anchor points at 0°, 120°, 240°
        CreateAnchor(start.transform, "Anchor_0", 0f);
        CreateAnchor(start.transform, "Anchor_120", 120f);
        CreateAnchor(start.transform, "Anchor_240", 240f);

        return start.transform;
    }

    void CreateAnchor(Transform parent, string name, float yaw)
    {
        GameObject anchor = new GameObject(name);
        anchor.transform.SetParent(parent, false);
        anchor.transform.localPosition = Vector3.zero;
        anchor.transform.localRotation = Quaternion.Euler(0f, yaw, 0f);
        // No hard reference to a DoorAnchor type; name-based discovery is used elsewhere
    }

    // Collects anchors under root whose transform name contains "Anchor" (case-insensitive)
    static void CollectAnchorsIntoList(Transform root, List<Transform> outList)
    {
        if (!root) return;
        var stack = new Stack<Transform>();
        stack.Push(root);
        while (stack.Count > 0)
        {
            var t = stack.Pop();
            if (t != root && t.name.IndexOf("Anchor", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                outList.Add(t);
            }
            for (int i = 0; i < t.childCount; i++) stack.Push(t.GetChild(i));
        }
    }

    void GenerateFromAnchors()
    {
        int attempts = 0;
        int maxAttempts = maxModules * 3; // Prevent infinite loops

        while (availableAnchors.Count > 0 && placedModules.Count < maxModules && attempts < maxAttempts)
        {
            attempts++;

            // Pick a random anchor
            int anchorIndex = rng.Next(availableAnchors.Count);
            Transform anchor = availableAnchors[anchorIndex];

            // Try to place a module
            if (TryPlaceModule(anchor))
            {
                // Remove this anchor since it's now used
                availableAnchors.RemoveAt(anchorIndex);
            }
            else
            {
                // If we can't place anything, remove this anchor to prevent infinite loops
                availableAnchors.RemoveAt(anchorIndex);
            }
        }

        if (logGeneration)
        {
            Debug.Log($"[Gen] Generation stopped: {placedModules.Count} modules, {availableAnchors.Count} anchors remaining");
        }
    }

    bool TryPlaceModule(Transform anchor)
    {
        // Determine what type of module to place based on current generation state
        GameObject prefab = ChooseModuleType(anchor);
        if (prefab == null) return false;

        // Find the entry anchor on the prefab (by name)
        Transform entryAnchor = FindEntryAnchorOnPrefab(prefab);
        if (entryAnchor == null) return false;

        // Place the module
        Transform placedModule = PlaceModule(prefab, entryAnchor, anchor);
        if (placedModule == null) return false;

        // Add to placed modules
        placedModules.Add(placedModule);

        // Collect new anchors from the placed module
        CollectAnchorsIntoList(placedModule, availableAnchors);

        if (logGeneration)
        {
            Debug.Log($"[Gen] Placed {prefab.name} at {placedModule.position}");
        }

        return true;
    }

    GameObject ChooseModuleType(Transform anchor)
    {
        // Simple rules: prefer hallways for long paths, connectors for branching
        float distanceFromStart = Vector3.Distance(anchor.position, Vector3.zero);

        // If we're far from start, allow more variety
        if (distanceFromStart > maxBranchLength)
        {
            // 60% hallway, 30% connector, 10% room
            float roll = (float)rng.NextDouble();
            if (roll < 0.6f && hallwayPrefabs != null && hallwayPrefabs.Length > 0)
                return hallwayPrefabs[rng.Next(hallwayPrefabs.Length)];
            else if (roll < 0.9f && connectorPrefabs != null && connectorPrefabs.Length > 0)
                return connectorPrefabs[rng.Next(connectorPrefabs.Length)];
            else if (roomPrefabs != null && roomPrefabs.Length > 0)
                return roomPrefabs[rng.Next(roomPrefabs.Length)];
        }
        else
        {
            // Near start: prefer hallways for snaking
            if (hallwayPrefabs != null && hallwayPrefabs.Length > 0)
                return hallwayPrefabs[rng.Next(hallwayPrefabs.Length)];
        }

        return null;
    }

    // Finds a child transform on the prefab whose name contains "Anchor"; falls back to first child
    static Transform FindEntryAnchorOnPrefab(GameObject prefab)
    {
        if (!prefab) return null;
        var trans = prefab.transform;
        // Search breadth-first so top-level anchors are preferred
        var queue = new Queue<Transform>();
        queue.Enqueue(trans);
        while (queue.Count > 0)
        {
            var t = queue.Dequeue();
            if (t != trans && t.name.IndexOf("Anchor", StringComparison.OrdinalIgnoreCase) >= 0)
                return t;
            for (int i = 0; i < t.childCount; i++) queue.Enqueue(t.GetChild(i));
        }
        // Fallback: first child if exists
        return trans.childCount > 0 ? trans.GetChild(0) : null;
    }

    Transform PlaceModule(GameObject prefab, Transform entryAnchorOnPrefab, Transform targetAnchor)
    {
        // Instantiate the prefab
        GameObject instance = Instantiate(prefab, Vector3.zero, Quaternion.identity, levelRoot);
        Transform instanceTransform = instance.transform;

        // Find the matching entry anchor on the instance by path
        Transform entryAnchorOnInstance = FindCorrespondingTransform(instanceTransform, entryAnchorOnPrefab);
        if (!entryAnchorOnInstance)
        {
            // Fallback: try again by name search
            entryAnchorOnInstance = FindEntryAnchorOnPrefab(instance);
        }

        // Align the entry anchor to the target anchor
        AlignModule(instanceTransform, entryAnchorOnInstance, targetAnchor);

        // Snap to lattice if enabled
        if (snapToLattice)
        {
            instanceTransform.position = SnapToLattice(instanceTransform.position);
        }

        if (snapYawTo30)
        {
            SnapYawTo30(instanceTransform);
        }

        // Check if this placement is valid (simple distance check)
        if (!IsValidPlacement(instanceTransform))
        {
#if UNITY_EDITOR
            DestroyImmediate(instance);
#else
            Destroy(instance);
#endif
            return null;
        }

        return instanceTransform;
    }

    // Try to find the same transform on the instantiated object by rebuilding a relative path
    static Transform FindCorrespondingTransform(Transform instanceRoot, Transform prefabTransform)
    {
        if (!instanceRoot || !prefabTransform) return null;

        // Build path from prefabTransform up to its root prefab
        var path = new List<string>();
        var t = prefabTransform;
        while (t != null)
        {
            path.Add(t.name);
            t = t.parent;
        }
        path.Reverse();

        // Walk the path starting at instanceRoot
        Transform current = instanceRoot;
        for (int i = 1; i < path.Count; i++) // skip root name
        {
            string childName = path[i];
            current = current.Find(childName);
            if (current == null) return null;
        }
        return current;
    }

    void AlignModule(Transform moduleRoot, Transform entryAnchor, Transform targetAnchor)
    {
        if (!entryAnchor) entryAnchor = moduleRoot; // safety

        // Rotation that makes entryAnchor forward match -targetAnchor.forward and up match world up
        Quaternion rotToMatch = Quaternion.FromToRotation(entryAnchor.forward, -targetAnchor.forward);
        Quaternion newRot = rotToMatch * moduleRoot.rotation;

        // Position so entryAnchor lands exactly on targetAnchor
        Vector3 entryWorldAfterRot = rotToMatch * (entryAnchor.position - moduleRoot.position) + moduleRoot.position;
        Vector3 newPos = moduleRoot.position + (targetAnchor.position - entryWorldAfterRot);

        moduleRoot.SetPositionAndRotation(newPos, newRot);
    }

    Vector3 SnapToLattice(Vector3 worldPos)
    {
        // Snap to a 30°/60° compatible grid by rounding to cellSize in XZ
        float x = Mathf.Round(worldPos.x / cellSize) * cellSize;
        float z = Mathf.Round(worldPos.z / cellSize) * cellSize;
        return new Vector3(x, worldPos.y, z);
    }

    void SnapYawTo30(Transform t)
    {
        Vector3 euler = t.eulerAngles;
        euler.x = 0f; euler.z = 0f;
        euler.y = Mathf.Round(euler.y / 30f) * 30f;
        t.rotation = Quaternion.Euler(euler);
    }

    bool IsValidPlacement(Transform newModule)
    {
        // Simple distance check: ensure minimum distance from all other modules
        for (int i = 0; i < placedModules.Count; i++)
        {
            Transform existing = placedModules[i];
            float distance = Vector3.Distance(newModule.position, existing.position);
            if (distance < minModuleDistance)
            {
                if (showDebugInfo)
                {
                    Debug.Log($"[Debug] {newModule.name} too close to {existing.name}: {distance:F1}m < {minModuleDistance}m");
                }
                return false;
            }
        }

        return true;
    }

    [ContextMenu("Clear Level")]
    public void ClearLevel()
    {
        if (levelRoot == null) return;

        int childCount = levelRoot.childCount;
        for (int i = childCount - 1; i >= 0; i--)
        {
            Transform child = levelRoot.GetChild(i);
#if UNITY_EDITOR
            DestroyImmediate(child.gameObject);
#else
            Destroy(child.gameObject);
#endif
        }

        placedModules.Clear();
        availableAnchors.Clear();

        Debug.Log($"[Gen] Cleared {childCount} modules");
    }

    void OnDrawGizmos()
    {
        if (!showDebugInfo) return;

        // Draw lattice grid
        Gizmos.color = Color.yellow;
        for (int x = -5; x <= 5; x++)
        {
            for (int z = -5; z <= 5; z++)
            {
                Vector3 pos = new Vector3(x * cellSize, 0, z * cellSize);
                Gizmos.DrawWireCube(pos, Vector3.one * cellSize);
            }
        }

        // Draw placed modules
        Gizmos.color = Color.green;
        for (int i = 0; i < placedModules.Count; i++)
        {
            var module = placedModules[i];
            if (module) Gizmos.DrawWireSphere(module.position, 1f);
        }

        // Draw available anchors
        Gizmos.color = Color.blue;
        for (int i = 0; i < availableAnchors.Count; i++)
        {
            var anchor = availableAnchors[i];
            if (anchor) Gizmos.DrawWireCube(anchor.position, Vector3.one * 0.5f);
        }
    }
}
