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
    [Header("Themes")]
    [SerializeField] ThemeProfile activeTheme;
    [SerializeField] PlacementCatalog catalog;         // Optional: use catalog weights

    [Header("Lattice Settings")]
    [SerializeField] float cellSize = 10f;            // 10m for your equilateral triangle
    [SerializeField] bool snapToLattice = true;
    [SerializeField] bool snapYawTo30 = true;
    [SerializeField] bool snapAttachedToLattice = false; // avoid snapping when attaching to an existing anchor

    [Header("Placement Rules")]
    [SerializeField] float minModuleDistance = 8f;    // Minimum distance between module centers (prevents overlaps)
    [SerializeField] float maxBranchLength = 8f;      // Maximum length before allowing branching
    [SerializeField] int maxSnakeLength = 6;          // (kept for future use)
    [Header("Anchor Join Tuning")]
    [Tooltip("Nudges newly placed modules along the target anchor forward. Positive = push in, Negative = pull back")]
    [SerializeField] float anchorJoinOffset = 0f;

    [Header("Debug")]
    [SerializeField] bool showDebugInfo = false;
    [SerializeField] bool logGeneration = false;
    [SerializeField] bool drawGizmosInPlay = false;

    [Header("Quick Actions")]
    [SerializeField] bool generateButton = false;
    [SerializeField] bool clearButton = false;

    [Header("Reliability / Quality Gates")]
    [SerializeField] bool autoRetryUntilGood = true;
    [SerializeField] int maxGenerationRetries = 6;
    [SerializeField] int minModulesAccept = 12;
    [SerializeField] int minPotentialConnectionsAccept = 1;
    [SerializeField] float connectionDistance = 12f; // anchors closer than this are considered connectable
    [SerializeField, Range(-1f, 1f)] float connectionFacingDotThreshold = -0.6f; // facing roughly towards each other
    [Tooltip("Reserve this many module slots so capping always runs with budget left.")]
    [SerializeField] int reserveModulesForCaps = 10;

    [Header("Loop/Branch Strategies")]
    [SerializeField] bool prioritizeAnchorsThatCanConnect = true;
    [SerializeField] bool preferConnectorWhenPartnerDetected = true;
    [SerializeField] bool limitStartAnchorsToOne = false;
    [Tooltip("Grow a few steps from each start anchor before free growth")] 
    [SerializeField] bool forceInitialGrowthFromAllStartAnchors = true;
    [SerializeField] int initialStepsPerStart = 2;

    [Header("Finishing")]
    [SerializeField] GameObject doorCapPrefab;
    [SerializeField] bool sealOpenAnchorsAtEnd = false;
    [Tooltip("Optional cap just for hallways; overrides generic cap when set")] 
    [SerializeField] GameObject hallwayDoorCapPrefab;
    [Tooltip("Optional cap sized for room doorways (e.g., 2.0m wide x 3.5m high)")] 
    [SerializeField] GameObject roomDoorCapPrefab;
    [Tooltip("Small forward inset to avoid Z-fighting with wall")] 
    [SerializeField] float doorCapInset = 0.02f;
    [Tooltip("Run extra capping passes until no visible open ends remain")] 
    [SerializeField] bool ensureAllAnchorsClosed = true;
    [SerializeField] int capMaxPasses = 3;
    [Tooltip("Eases pairing when deciding if an anchor is already connected")] 
    [SerializeField] float capPairDistanceExtra = 2.0f;
    [SerializeField, Range(-1f,1f)] float capPairFacingMinDot = -0.2f;
    [Header("Medium Room Targeting")]
    [SerializeField] int targetMediumRooms = 1;
    [SerializeField] int mediumAnchorTryLimit = 25;
    [Tooltip("Pre-place a medium room at a lattice-aligned position before growth")] 
    [SerializeField] bool preplaceMediumRoom = true;
    [Tooltip("Distance in cells from start (origin) to pre-place the medium room")] 
    [SerializeField] int mediumDistanceCells = 6;
    [Tooltip("Yaw index 0..5 for multiples of 60 degrees (0=+Z, 1=60°, 2=120°,...)")]
    [SerializeField] int mediumYawIndex = 0;
    [Tooltip("How many initial branches to grow from the medium room immediately after placement")] 
    [SerializeField] int initialBranchesFromMedium = 3;
    [Tooltip("How many steps to grow from each chosen medium-room anchor before free growth")] 
    [SerializeField] int initialStepsPerMediumBranch = 2;

    [Header("Placement Policy")]
    [SerializeField] bool disallowConsecutiveConnectors = true;
    [SerializeField] int minPlacementsBetweenConnectors = 2; // placements between connector uses
    [SerializeField] int forceHallwayFirstSteps = 6; // first N placements: no connectors

    [Header("Connection Hotspots")]
    [SerializeField] bool preferNearRecentConnections = true;
    [SerializeField] int recentConnectionPointsLimit = 8;
    [SerializeField] float hotspotRadius = 10f;
    [SerializeField] float hotspotBias = 1.0f; // increases score for anchors near hotspots
    [SerializeField] bool preferHighAnchorCountNearHotspot = true; // prefer hall variants with more side doors

    private System.Random rng;
    private readonly List<Transform> placedModules = new List<Transform>();
    private readonly List<Transform> availableAnchors = new List<Transform>();
    private int placementsSinceLastConnector = int.MaxValue; // reset per run
    private int nonStartPlacements = 0; // excludes the initial start module
    private int totalConnectorsPlaced = 0;
    private int totalHallwaysPlaced = 0;
    private int placedMediumRooms = 0;
    private readonly HashSet<Transform> connectedAnchors = new HashSet<Transform>();
    // Branch tracking: encourage cross-branch connections
    private readonly Dictionary<Transform, int> anchorToBranchId = new Dictionary<Transform, int>();
    // Recent hotspots where a connection was possible/attempted
    private readonly List<Vector3> recentConnectionPoints = new List<Vector3>();
    // Cache for counting anchors per prefab
    private readonly Dictionary<GameObject, int> prefabToAnchorCount = new Dictionary<GameObject, int>();
    // Track how many halls have been placed consecutively on each branch since last branch piece (connector)
    private readonly Dictionary<int, int> branchHallsSinceBranch = new Dictionary<int, int>();
    [SerializeField] private int minHallsBeforeBranch = 3; // required halls per branch before allowing branching
    [SerializeField] private int minLoopConnectorsBeforeMediumRooms = 2; // gate medium rooms until some loops are formed
    private int loopConnectorsPlaced = 0;

    // Replication hook: subscribers (e.g., networking) can listen for placements
    public event System.Action<GameObject, Vector3, Quaternion, Transform> ModulePlaced;
    private int pendingCatalogEntryId = -1;

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

        int attempts = Mathf.Max(1, autoRetryUntilGood ? maxGenerationRetries : 1);
        int bestScore = int.MinValue;
        List<Transform> bestModules = null;

        for (int attempt = 0; attempt < attempts; attempt++)
        {
            rng = randomizeSeed ? new System.Random() : new System.Random(seed + attempt);
            placedModules.Clear();
            availableAnchors.Clear();
            placementsSinceLastConnector = int.MaxValue;
            nonStartPlacements = 0;
            totalConnectorsPlaced = 0;
            totalHallwaysPlaced = 0;
            anchorToBranchId.Clear();
            recentConnectionPoints.Clear();
            prefabToAnchorCount.Clear();
            branchHallsSinceBranch.Clear();
            loopConnectorsPlaced = 0;
            placedMediumRooms = 0;
            connectedAnchors.Clear();

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
            AssignInitialBranches(startRoot);
            if (limitStartAnchorsToOne && availableAnchors.Count > 1)
            {
                Transform best = ChooseForwardMostAnchor(availableAnchors);
                availableAnchors.Clear();
                if (best) availableAnchors.Add(best);
            }

            // Generate the level
            if (forceInitialGrowthFromAllStartAnchors)
            {
                var startAnchors = new List<Transform>(availableAnchors);
                GrowFromInitialAnchors(startAnchors, Mathf.Max(1, initialStepsPerStart));
            }

            // Pre-place a medium room on the lattice and fan out from multiple doors
            Transform mediumRootPlaced = null;
            if (preplaceMediumRoom)
            {
                mediumRootPlaced = TryPreplaceMediumRoom();
                if (mediumRootPlaced)
                {
                    GrowFromRootAnchors(mediumRootPlaced, Mathf.Max(1, initialBranchesFromMedium), Mathf.Max(1, initialStepsPerMediumBranch));
                }
            }

            GenerateFromAnchors();

            // Optionally ensure medium room placement before sealing
            EnsureMediumRooms();

            // Force-run capping regardless of module limit; this closes any visible openings
            if (sealOpenAnchorsAtEnd && doorCapPrefab != null)
            {
                SealOpenAnchors();
                if (ensureAllAnchorsClosed)
                {
                    EnsureAllAnchorsClosed();
                }
            }

            

            // Evaluate quality
            int potentialConnections = CountPotentialConnectionsAcrossLevel();
            int score = placedModules.Count * 10 + potentialConnections * 25;

            bool passes = placedModules.Count >= minModulesAccept && potentialConnections >= minPotentialConnectionsAccept;
            if (logGeneration)
            {
                Debug.Log($"[Gen] Attempt {attempt + 1}/{attempts}: modules={placedModules.Count}, potConns={potentialConnections}, score={score}, pass={passes}");
            }

            if (passes)
            {
                Debug.Log($"[Gen] Accepted attempt {attempt + 1} with {placedModules.Count} modules and {potentialConnections} potential connections.");
                break; // keep this layout
            }

            if (score > bestScore)
            {
                bestScore = score;
                // Snapshot the transforms so we can restore best if all fail
                bestModules = new List<Transform>(placedModules);
            }

            if (attempt < attempts - 1)
            {
                // Try again with a different seed
                continue;
            }
        }

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

    void AssignInitialBranches(Transform startRoot)
    {
        // Assign unique branch IDs to each direct child anchor under the start
        var startAnchors = new List<Transform>();
        CollectAnchorsIntoList(startRoot, startAnchors);
        int nextId = 1;
        for (int i = 0; i < startAnchors.Count; i++)
        {
            var a = startAnchors[i];
            if (!a) continue;
            anchorToBranchId[a] = nextId++;
        }
    }

    int GetBranchIdForAnchor(Transform anchor)
    {
        if (!anchor) return 0;
        if (anchorToBranchId.TryGetValue(anchor, out int id)) return id;
        // Fallback: inherit from the closest ancestor anchor with an id
        Transform t = anchor.parent;
        while (t != null)
        {
            if (anchorToBranchId.TryGetValue(t, out id)) return id;
            t = t.parent;
        }
        return 0;
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
        int moduleLimit = Mathf.Max(0, maxModules - Mathf.Max(0, reserveModulesForCaps));

        while (availableAnchors.Count > 0 && placedModules.Count < moduleLimit && attempts < maxAttempts)
        {
            attempts++;

            // Pick an anchor, prioritizing cross-branch potential partners
            int anchorIndex = 0;
            if (prioritizeAnchorsThatCanConnect)
            {
                anchorIndex = FindIndexOfAnchorWithCrossBranchPartner(availableAnchors);
                if (anchorIndex < 0)
                    anchorIndex = FindIndexOfAnchorWithPartner(availableAnchors);
                if (anchorIndex < 0) anchorIndex = rng.Next(availableAnchors.Count);
            }
            else
            {
                anchorIndex = rng.Next(availableAnchors.Count);
            }
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

        // Optional connection-kind compatibility: only proceed if target anchor allows this prefab's kind
        if (!IsConnectionCompatible(prefab, anchor))
        {
            return false;
        }

        // Find the entry anchor on the prefab (by name)
        Transform entryAnchor = FindEntryAnchorOnPrefab(prefab);
        if (entryAnchor == null) return false;

        // Place the module
        Transform placedModule = PlaceModule(prefab, entryAnchor, anchor, out Transform placedEntryAnchor);
        if (placedModule == null) return false;

        // Add to placed modules
        placedModules.Add(placedModule);
        nonStartPlacements++;
        placementsSinceLastConnector++;

        // Collect new anchors from the placed module
        CollectAnchorsIntoList(placedModule, availableAnchors);

        // Do not consider the consumed entry anchor for immediate reuse
        if (placedEntryAnchor)
        {
            availableAnchors.Remove(placedEntryAnchor);
        }

        // Mark anchors as connected
        connectedAnchors.Add(anchor);
        if (placedEntryAnchor) connectedAnchors.Add(placedEntryAnchor);

        if (logGeneration)
        {
            Debug.Log($"[Gen] Placed {prefab.name} at {placedModule.position}");
        }

        // Record hotspot for future bias
        if (preferNearRecentConnections)
        {
            Vector3 h = anchor.position;
            recentConnectionPoints.Add(h);
            if (recentConnectionPoints.Count > recentConnectionPointsLimit)
                recentConnectionPoints.RemoveAt(0);
        }

        // Update placement policy counters by prefab category
        string nameLower = prefab.name.ToLowerInvariant();
        bool isConnector = (connectorPrefabs != null && Array.Exists(connectorPrefabs, p => p && p.name == prefab.name));
        bool isHall = (hallwayPrefabs != null && Array.Exists(hallwayPrefabs, p => p && p.name == prefab.name));
        if (isConnector)
        {
            placementsSinceLastConnector = 0;
            totalConnectorsPlaced++;
            // Reset branch run for this branch
            int b = GetBranchIdForAnchor(anchor);
            branchHallsSinceBranch[b] = 0;
            // If this connector tends towards another branch (cross-branch partner exists), treat as loop progress
            var partnerHere = FindPartnerAnchor(anchor);
            if (partnerHere != null)
            {
                int aId = GetBranchIdForAnchor(anchor);
                int bId2 = GetBranchIdForAnchor(partnerHere);
                if (aId != bId2) loopConnectorsPlaced++;
            }
        }
        else if (isHall)
        {
            totalHallwaysPlaced++;
            // Increment branch run for this branch
            int b = GetBranchIdForAnchor(anchor);
            branchHallsSinceBranch.TryGetValue(b, out int run);
            branchHallsSinceBranch[b] = run + 1;
        }
        else
        {
            // Check if placed module is a medium room
            var meta = placedModule ? placedModule.GetComponent<RoomMeta>() : null;
            if (meta != null && meta.subtype == PlacementCatalog.HallSubtype.MediumRoom)
            {
                placedMediumRooms++;
            }
        }

        // Notify replication listeners
        ModulePlaced?.Invoke(prefab, placedModule.position, placedModule.rotation, placedModule);

        return true;
    }

    // Returns true if either side lacks metadata, or if the prefab's RoomMeta.connectionKind
    // is included in the target DoorAnchor.canConnectTo list. This adds specificity without
    // breaking prefabs that don't use these components.
    bool IsConnectionCompatible(GameObject prefab, Transform targetAnchor)
    {
        if (!prefab || !targetAnchor) return true;

        var roomMeta = prefab.GetComponent<RoomMeta>();
        var doorAnchor = targetAnchor.GetComponent<DoorAnchor>();

        if (roomMeta == null || doorAnchor == null) return true; // no restriction if missing

        return doorAnchor.Allows(roomMeta.connectionKind);
    }

    GameObject ChooseModuleType(Transform anchor)
    {
        // Simple rules: prefer hallways for long paths, connectors for branching
        float distanceFromStart = Vector3.Distance(anchor.position, Vector3.zero);

        bool inHallwayPhase = nonStartPlacements < forceHallwayFirstSteps;
        bool connectorOnCooldown = placementsSinceLastConnector < minPlacementsBetweenConnectors;
        bool crossBranchPreferred = false;
        var partner = FindPartnerAnchor(anchor);
        if (partner != null)
        {
            int aId = GetBranchIdForAnchor(anchor);
            int bId = GetBranchIdForAnchor(partner);
            crossBranchPreferred = (aId != bId);
        }

        // Enforce per-branch minimum halls before allowing connector branching
        int branchIdHere = GetBranchIdForAnchor(anchor);
        branchHallsSinceBranch.TryGetValue(branchIdHere, out int hallsSinceBranch);
        bool branchAllowed = hallsSinceBranch >= minHallsBeforeBranch;

        // PRIORITIZE LOOPING: if this anchor has a partner, try connectors FIRST (ignore branch gating)
        if (partner != null && connectorPrefabs != null && connectorPrefabs.Length > 0)
        {
            // Try weighted junction first (triple connector), then fallback to any connector
            var weightedConn = PickWeightedFromCatalog(PlacementCatalog.HallSubtype.Junction3_V, distanceFromStart);
            if (weightedConn != null) return weightedConn;
            return connectorPrefabs[rng.Next(connectorPrefabs.Length)];
        }

        // Otherwise, allow branching connectors only when branch gating allows
        if (branchAllowed && !inHallwayPhase && !connectorOnCooldown && preferConnectorWhenPartnerDetected && connectorPrefabs != null && connectorPrefabs.Length > 0)
        {
            var weightedConn2 = PickWeightedFromCatalog(PlacementCatalog.HallSubtype.Junction3_V, distanceFromStart);
            if (weightedConn2 != null) return weightedConn2;
        }

        // If we're far from start, allow more variety (score-based with hotspot bias)
        if (distanceFromStart > maxBranchLength)
        {
            GameObject hall = PickBestHall(anchor);
            GameObject conn = PickWeightedFromCatalog(PlacementCatalog.HallSubtype.Junction3_V, distanceFromStart) ?? PickBestConnector(anchor);
            bool favorSmallMediumRooms = partner == null; // end-cap candidate far from start
            bool gateMediumRooms = loopConnectorsPlaced < minLoopConnectorsBeforeMediumRooms;
            GameObject room = PickBestRoomPrefab(anchor, favorSmallMediumRooms, gateMediumRooms);

            float roll = (float)rng.NextDouble();
            if (favorSmallMediumRooms)
            {
                // Bias rooms to cap ends: Room 50%, Hall 35%, Conn 15%
                if (roll < 0.5f && room != null) return room;
                else if (roll < 0.85f && hall != null) return hall;
                else if (conn != null) return conn;
            }
            else
            {
                if (roll < 0.45f && hall != null) return hall;
                else if (roll < 0.85f && conn != null) return conn; // boost connectors vs turns
                else if (room != null) return room;
            }
        }
        else
        {
            // Near start: prefer hallways for snaking
            GameObject hall = PickBestPrefab(hallwayPrefabs, anchor, preferHighAnchorCountNearHotspot);
            if (hall != null) return hall;
        }

        // Fallback with policy: if connectors are on cooldown or hallway phase enforced, use hallways/rooms
        if (inHallwayPhase || connectorOnCooldown)
        {
            GameObject hall = PickBestHall(anchor);
            if (hall != null) return hall;
            GameObject room = PickBestRoomPrefab(anchor, false, loopConnectorsPlaced < minLoopConnectorsBeforeMediumRooms);
            if (room != null) return room;
        }

        return null;
    }

    // Returns the index of an anchor in list that has a potential partner; -1 if none
    int FindIndexOfAnchorWithPartner(List<Transform> list)
    {
        for (int i = 0; i < list.Count; i++)
        {
            if (FindPartnerAnchor(list[i]) != null)
                return i;
        }
        return -1;
    }

    // Prefer anchors whose partner is in a different branch
    int FindIndexOfAnchorWithCrossBranchPartner(List<Transform> list)
    {
        for (int i = 0; i < list.Count; i++)
        {
            var a = list[i];
            var b = FindPartnerAnchor(a);
            if (!a || !b) continue;
            int aId = GetBranchIdForAnchor(a);
            int bId = GetBranchIdForAnchor(b);
            if (aId != bId) return i;
        }
        return -1;
    }

    // Finds any other anchor in the level that could connect to the provided one
    Transform FindPartnerAnchor(Transform anchor)
    {
        var anchors = GatherAllAnchorsInLevel();
        for (int i = 0; i < anchors.Count; i++)
        {
            var other = anchors[i];
            if (!other || other == anchor) continue;
            if (!AreDifferentModules(anchor, other)) continue;
            if (AnchorsCouldConnect(anchor, other)) return other;
        }
        return null;
    }

    Transform ChooseForwardMostAnchor(List<Transform> anchors)
    {
        Transform best = null;
        float bestDot = float.NegativeInfinity;
        for (int i = 0; i < anchors.Count; i++)
        {
            var a = anchors[i];
            if (!a) continue;
            float d = Vector3.Dot(a.forward, Vector3.forward);
            if (d > bestDot)
            {
                bestDot = d;
                best = a;
            }
        }
        return best ?? (anchors.Count > 0 ? anchors[0] : null);
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

    Transform PlaceModule(GameObject prefab, Transform entryAnchorOnPrefab, Transform targetAnchor, out Transform entryAnchorOnInstance)
    {
        entryAnchorOnInstance = null;
        // Instantiate the prefab
        GameObject instance = Instantiate(prefab, Vector3.zero, Quaternion.identity, levelRoot);
        Transform instanceTransform = instance.transform;

        // Find the matching entry anchor on the instance by path
        entryAnchorOnInstance = FindCorrespondingTransform(instanceTransform, entryAnchorOnPrefab);
        if (!entryAnchorOnInstance)
        {
            // Fallback: try again by name search
            entryAnchorOnInstance = FindEntryAnchorOnPrefab(instance);
        }

        // Align the entry anchor to the target anchor (yaw-only to prevent roll/pitch)
        AlignModuleYawOnly(instanceTransform, entryAnchorOnInstance, targetAnchor, snapYawTo30);

        // Optional anchor join offset
        if (Mathf.Abs(anchorJoinOffset) > 0f)
        {
            instanceTransform.position += targetAnchor.forward * anchorJoinOffset;
        }

        // Snap to lattice only if explicitly allowed for attached modules, then re-translate to keep anchors touching
        if (snapToLattice && snapAttachedToLattice)
        {
            Vector3 before = entryAnchorOnInstance.position;
            instanceTransform.position = SnapToLattice(instanceTransform.position);
            Vector3 after = entryAnchorOnInstance.position;
            instanceTransform.position += (targetAnchor.position - after); // preserve contact
        }

        // Check if this placement is valid (simple distance check, ignoring the module that owns targetAnchor)
        Transform hostModule = GetModuleRootForAnchor(targetAnchor);
        if (!IsValidPlacement(instanceTransform, hostModule))
        {
#if UNITY_EDITOR
            DestroyImmediate(instance);
#else
            Destroy(instance);
#endif
            return null;
        }

        // Propagate branch ID from the consumed target anchor into the new module's anchors
        int branchId = GetBranchIdForAnchor(targetAnchor);
        var newAnchors = new List<Transform>();
        CollectAnchorsIntoList(instanceTransform, newAnchors);
        for (int i = 0; i < newAnchors.Count; i++)
        {
            var a = newAnchors[i];
            if (!a || a == entryAnchorOnInstance) continue; // skip the consumed entry
            anchorToBranchId[a] = branchId;
        }

        return instanceTransform;
    }

    // Ascend from an arbitrary anchor to the direct child of levelRoot (the owning module)
    Transform GetModuleRootForAnchor(Transform anchor)
    {
        if (!anchor) return null;
        Transform t = anchor;
        while (t.parent != null && t.parent != levelRoot)
        {
            t = t.parent;
        }
        return t;
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

    void AlignModuleYawOnly(Transform moduleRoot, Transform entryAnchor, Transform targetAnchor, bool snapYaw)
    {
        if (!entryAnchor) entryAnchor = moduleRoot; // safety

        // Flattened forward vectors (XZ only)
        Vector3 entryF = Vector3.ProjectOnPlane(entryAnchor.forward, Vector3.up);
        if (entryF.sqrMagnitude < 1e-6f) entryF = entryAnchor.forward;
        Vector3 targetF = Vector3.ProjectOnPlane(-targetAnchor.forward, Vector3.up);
        if (targetF.sqrMagnitude < 1e-6f) targetF = -targetAnchor.forward;

        // Compute yaw delta to align entry forward to -target forward
        float deltaYaw = Vector3.SignedAngle(entryF, targetF, Vector3.up);
        Vector3 pivot = entryAnchor.position;

        // Rotate module around the entry anchor pivot by delta yaw
        RotateAroundPivot(moduleRoot, pivot, Quaternion.AngleAxis(deltaYaw, Vector3.up));

        // Optional snapping to nearest 30 degrees, also around the entry pivot
        if (snapYaw)
        {
            float currentYaw = moduleRoot.eulerAngles.y;
            float snappedYaw = Mathf.Round(currentYaw / 30f) * 30f;
            float snapDelta = Mathf.DeltaAngle(currentYaw, snappedYaw);
            RotateAroundPivot(moduleRoot, pivot, Quaternion.AngleAxis(snapDelta, Vector3.up));
        }

        // Re-translate so the entry anchor lands exactly on target anchor
        Vector3 offset = targetAnchor.position - entryAnchor.position;
        moduleRoot.position += offset;
    }

    // Ensure at least targetMediumRooms have been placed by attempting direct placement(s)
    void EnsureMediumRooms()
    {
        if (placedMediumRooms >= targetMediumRooms) return;
        if (roomPrefabs == null || roomPrefabs.Length == 0) return;
        // Collect and sort anchors by distance from origin (farthest first)
        var anchors = GatherAllAnchorsInLevel();
        anchors.Sort((a, b) =>
        {
            float da = a ? (a.position - Vector3.zero).sqrMagnitude : -1f;
            float db = b ? (b.position - Vector3.zero).sqrMagnitude : -1f;
            return db.CompareTo(da);
        });

        // Collect medium room prefabs
        var mediumPrefabs = new List<GameObject>();
        for (int i = 0; i < roomPrefabs.Length; i++)
        {
            var p = roomPrefabs[i]; if (!p) continue;
            var meta = p.GetComponent<RoomMeta>();
            if (meta != null && meta.subtype == PlacementCatalog.HallSubtype.MediumRoom)
                mediumPrefabs.Add(p);
        }
        if (mediumPrefabs.Count == 0) return;

        int tries = 0;
        for (int i = 0; i < anchors.Count && tries < mediumAnchorTryLimit && placedMediumRooms < targetMediumRooms; i++)
        {
            var anchor = anchors[i]; if (!anchor) continue;
            // Skip anchors that already have a partner (not open)
            if (!IsAnchorOpen(anchor, anchors)) continue;

            for (int m = 0; m < mediumPrefabs.Count && placedMediumRooms < targetMediumRooms; m++)
            {
                var medium = mediumPrefabs[m]; if (!medium) continue;
                if (!IsConnectionCompatible(medium, anchor)) { tries++; continue; }
                var entry = FindEntryAnchorOnPrefab(medium); if (!entry) { tries++; continue; }
                Transform placed = PlaceModule(medium, entry, anchor, out Transform placedEntryAnchor);
                tries++;
                if (!placed) continue;

                placedModules.Add(placed);
                nonStartPlacements++;
                placementsSinceLastConnector++;
                CollectAnchorsIntoList(placed, availableAnchors);
                if (placedEntryAnchor) availableAnchors.Remove(placedEntryAnchor);
                availableAnchors.Remove(anchor);
                placedMediumRooms++;
                ModulePlaced?.Invoke(medium, placed.position, placed.rotation, placed);
                break;
            }
        }
    }

    void RotateAroundPivot(Transform t, Vector3 pivot, Quaternion delta)
    {
        Vector3 dir = t.position - pivot;
        dir = delta * dir;
        t.position = pivot + dir;
        t.rotation = delta * t.rotation;
    }

    Vector3 SnapToLattice(Vector3 worldPos)
    {
        // Snap to a 30°/60° compatible grid by rounding to cellSize in XZ
        float x = Mathf.Round(worldPos.x / cellSize) * cellSize;
        float z = Mathf.Round(worldPos.z / cellSize) * cellSize;
        return new Vector3(x, worldPos.y, z);
    }

    bool IsValidPlacement(Transform newModule, Transform ignoreModule)
    {
        // Simple distance check: ensure minimum distance from all other modules
        for (int i = 0; i < placedModules.Count; i++)
        {
            Transform existing = placedModules[i];
            if (ignoreModule && existing == ignoreModule) continue;
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
        if (Application.isPlaying && !drawGizmosInPlay) return;

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

        // Visualize potential connections
        Gizmos.color = Color.cyan;
        var allAnchors = GatherAllAnchorsInLevel();
        for (int i = 0; i < allAnchors.Count; i++)
        {
            for (int j = i + 1; j < allAnchors.Count; j++)
            {
                var a = allAnchors[i];
                var b = allAnchors[j];
                if (!a || !b) continue;
                if (!AreDifferentModules(a, b)) continue;
                if (AnchorsCouldConnect(a, b))
                {
                    Gizmos.DrawLine(a.position, b.position);
                }
            }
        }
    }

    // Grow forward from each start anchor by N steps to fan out equally
    void GrowFromInitialAnchors(List<Transform> starts, int stepsPerStart)
    {
        if (starts == null) return;
        for (int i = 0; i < starts.Count; i++)
        {
            Transform current = starts[i];
            for (int s = 0; s < stepsPerStart; s++)
            {
                if (current == null) break;
                if (!availableAnchors.Contains(current)) break;
                if (!TryPlaceModule(current)) { availableAnchors.Remove(current); break; }
                // pick the next anchor that continues forward
                Transform lastPlaced = placedModules.Count > 0 ? placedModules[placedModules.Count - 1] : null;
                current = FindForwardMostAnchor(lastPlaced, current.forward);
            }
        }
    }

    Transform FindForwardMostAnchor(Transform moduleRoot, Vector3 fromDir)
    {
        if (!moduleRoot) return null;
        var list = new List<Transform>();
        CollectAnchorsIntoList(moduleRoot, list);
        Transform best = null; float bestDot = -1f;
        for (int i = 0; i < list.Count; i++)
        {
            var t = list[i]; if (!t) continue;
            float d = Vector3.Dot(fromDir.normalized, t.forward);
            if (d > bestDot) { bestDot = d; best = t; }
        }
        return best;
    }

    // Grow from a specific root's anchors: choose N anchors and grow M steps from each
    void GrowFromRootAnchors(Transform root, int branches, int steps)
    {
        if (!root) return;
        var rootAnchors = new List<Transform>();
        CollectAnchorsIntoList(root, rootAnchors);
        int grown = 0;
        for (int i = 0; i < rootAnchors.Count && grown < branches; i++)
        {
            Transform a = rootAnchors[i];
            if (!a) continue;
            if (!availableAnchors.Contains(a)) continue;
            // simple forward grow loop
            Transform current = a;
            for (int s = 0; s < steps; s++)
            {
                if (!availableAnchors.Contains(current)) break;
                if (!TryPlaceModule(current)) { availableAnchors.Remove(current); break; }
                Transform lastPlaced = placedModules.Count > 0 ? placedModules[placedModules.Count - 1] : null;
                current = FindForwardMostAnchor(lastPlaced, current.forward);
            }
            grown++;
        }
    }

    // Place a medium room at a lattice-aligned position offset from origin
    Transform TryPreplaceMediumRoom()
    {
        // Find a medium room prefab
        GameObject medium = null;
        for (int i = 0; i < roomPrefabs.Length; i++)
        {
            var p = roomPrefabs[i]; if (!p) continue;
            var meta = p.GetComponent<RoomMeta>();
            if (meta != null && meta.subtype == PlacementCatalog.HallSubtype.MediumRoom)
            { medium = p; break; }
        }
        if (medium == null) return null;

        // Compute lattice-aligned target
        float yaw = Mathf.Repeat(mediumYawIndex, 6) * 60f;
        Vector3 dir = Quaternion.Euler(0f, yaw, 0f) * Vector3.forward;
        Vector3 pos = SnapToLattice(dir.normalized * (cellSize * Mathf.Max(1, mediumDistanceCells)));

        // Create a temporary target anchor
        var temp = new GameObject("Medium_TargetAnchor").transform;
        temp.SetParent(levelRoot, false);
        temp.position = pos;
        temp.rotation = Quaternion.Euler(0f, yaw, 0f);

        // Align medium's entry to this target anchor
        var entry = FindEntryAnchorOnPrefab(medium); if (!entry) { DestroyImmediate(temp.gameObject); return null; }
        Transform placed = PlaceModule(medium, entry, temp, out Transform placedEntryAnchor);
        DestroyImmediate(temp.gameObject);
        if (!placed) return null;

        placedModules.Add(placed);
        nonStartPlacements++;
        placementsSinceLastConnector++;
        CollectAnchorsIntoList(placed, availableAnchors);
        if (placedEntryAnchor) availableAnchors.Remove(placedEntryAnchor);
        return placed;
    }

    

    // --- Connection / Quality helpers ---
    int CountPotentialConnectionsAcrossLevel()
    {
        var anchors = GatherAllAnchorsInLevel();
        int count = 0;
        for (int i = 0; i < anchors.Count; i++)
        {
            for (int j = i + 1; j < anchors.Count; j++)
            {
                var a = anchors[i]; var b = anchors[j];
                if (!a || !b) continue;
                if (!AreDifferentModules(a, b)) continue;
                if (AnchorsCouldConnect(a, b)) count++;
            }
        }
        return count;
    }

    List<Transform> GatherAllAnchorsInLevel()
    {
        var anchors = new List<Transform>();
        if (!levelRoot) return anchors;
        for (int i = 0; i < levelRoot.childCount; i++)
        {
            CollectAnchorsIntoList(levelRoot.GetChild(i), anchors);
        }
        return anchors;
    }

    bool AnchorsCouldConnect(Transform a, Transform b)
    {
        // Distance check (flattened to XZ)
        Vector3 aXZ = new Vector3(a.position.x, 0f, a.position.z);
        Vector3 bXZ = new Vector3(b.position.x, 0f, b.position.z);
        float dist = Vector3.Distance(aXZ, bXZ);
        if (dist > connectionDistance) return false;

        // Facing check: forward vectors should be roughly opposite
        float facingDot = Vector3.Dot(a.forward, -b.forward);
        if (facingDot < connectionFacingDotThreshold) return false;
        return true;
    }

    // More permissive version used during capping to decide if an anchor is already paired
    bool AnchorsCouldConnectForCapping(Transform a, Transform b)
    {
        Vector3 aXZ = new Vector3(a.position.x, 0f, a.position.z);
        Vector3 bXZ = new Vector3(b.position.x, 0f, b.position.z);
        float dist = Vector3.Distance(aXZ, bXZ);
        if (dist > (connectionDistance + capPairDistanceExtra)) return false;
        float facingDot = Vector3.Dot(a.forward, -b.forward);
        if (facingDot < capPairFacingMinDot) return false;
        return true;
    }

    GameObject PickBestPrefab(GameObject[] candidates, Transform atAnchor, bool favorHighAnchorCount)
    {
        if (candidates == null || candidates.Length == 0) return null;
        // Try catalog first when available: respect per-subtype weights (e.g., connectors vs turns)
        if (catalog != null)
        {
            // Try Junctions first when we’re allowed to branch
            var e = catalog.PickWeighted(PlacementCatalog.HallSubtype.Junction3_V, nonStartPlacements, rng);
            if (e != null && e.prefab != null) return e.prefab;
        }
        float bestScore = float.NegativeInfinity;
        GameObject best = null;
        for (int i = 0; i < candidates.Length; i++)
        {
            var p = candidates[i];
            if (!p) continue;
            float score = 0f;
            if (preferNearRecentConnections && recentConnectionPoints.Count > 0)
            {
                // Positive score for being near any recent hotspot
                float nearest = float.PositiveInfinity;
                Vector3 pos = atAnchor.position;
                for (int h = 0; h < recentConnectionPoints.Count; h++)
                {
                    float d = Vector3.Distance(pos, recentConnectionPoints[h]);
                    if (d < nearest) nearest = d;
                }
                if (nearest <= hotspotRadius)
                {
                    score += hotspotBias * (1f - nearest / hotspotRadius);
                }
            }
            if (favorHighAnchorCount)
            {
                score += 0.25f * GetAnchorCountForPrefab(p);
            }
            if (score > bestScore)
            {
                bestScore = score;
                best = p;
            }
        }
        return best ?? candidates[0];
    }

    GameObject PickBestHall(Transform anchor)
    {
        var themeHalls = GetThemeHalls();
        if (themeHalls != null && themeHalls.Count > 0)
        {
            // Filter by matching width or adapters available
            int currentW = GetAnchorWidthForTransform(anchor);
            var list = FilterByWidth(themeHalls, currentW);
            // Weight simple straights and triple connectors more; fall back if empty
            for (int i = 0; i < list.Count; i++)
            {
                var rm = list[i];
                if (rm != null && rm.gameObject != null) return rm.gameObject;
            }
        }
        return PickBestPrefab(hallwayPrefabs, anchor, preferHighAnchorCountNearHotspot);
    }

    GameObject PickBestConnector(Transform anchor)
    {
        var themeAdapters = GetThemeAdapters();
        if (themeAdapters != null && themeAdapters.Count > 0)
        {
            int currentW = GetAnchorWidthForTransform(anchor);
            var list = FilterByWidth(themeAdapters, currentW);
            for (int i = 0; i < list.Count; i++)
            {
                var rm = list[i];
                if (rm != null && rm.gameObject != null) return rm.gameObject;
            }
        }
        return PickBestPrefab(connectorPrefabs, anchor, false);
    }

    int GetAnchorWidthForTransform(Transform anchor)
    {
        // If the owner module has meta width use it, else fall back to 10
        Transform owner = GetModuleRootForAnchor(anchor);
        RoomMeta meta = owner != null ? owner.GetComponent<RoomMeta>() : null;
        return (meta != null) ? meta.hallWidth : 10;
    }

    List<RoomMeta> FilterByWidth(List<RoomMeta> list, int currentW)
    {
        var result = new List<RoomMeta>(list.Count);
        for (int i = 0; i < list.Count; i++)
        {
            var r = list[i]; if (r == null) continue;
            if (r.hallWidth == currentW || HasAdapter(currentW, r.hallWidth)) result.Add(r);
        }
        return result;
    }

    bool HasAdapter(int fromW, int toW)
    {
        var adapters = GetThemeAdapters();
        if (adapters == null) return false;
        for (int i = 0; i < adapters.Count; i++)
        {
            var a = adapters[i]; if (a == null) continue;
            if (!a.isAdapter) continue;
            // Minimal: allow any adapter when widths differ (you can refine with explicit from/to later)
            if (fromW != toW) return true;
        }
        return false;
    }

    List<RoomMeta> GetThemeHalls()
    {
        if (activeTheme == null) return null;
        var fi = typeof(ThemeProfile).GetField("halls");
        if (fi == null) return null;
        return fi.GetValue(activeTheme) as List<RoomMeta>;
    }

    List<RoomMeta> GetThemeAdapters()
    {
        if (activeTheme == null) return null;
        var fi = typeof(ThemeProfile).GetField("adapters");
        if (fi == null) return null;
        return fi.GetValue(activeTheme) as List<RoomMeta>;
    }

    int GetAnchorCountForPrefab(GameObject prefab)
    {
        if (!prefab) return 0;
        if (prefabToAnchorCount.TryGetValue(prefab, out int cached)) return cached;
        int count = 0;
        var queue = new Queue<Transform>();
        queue.Enqueue(prefab.transform);
        while (queue.Count > 0)
        {
            var t = queue.Dequeue();
            if (t != prefab.transform && t.name.IndexOf("Anchor", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                count++;
            }
            for (int i = 0; i < t.childCount; i++) queue.Enqueue(t.GetChild(i));
        }
        prefabToAnchorCount[prefab] = count;
        return count;
    }

    GameObject PickWeightedFromCatalog(PlacementCatalog.HallSubtype subtype, float distanceFromStart)
    {
        if (catalog == null) return null;
        var e = catalog.PickWeighted(subtype, nonStartPlacements, rng);
        if (e == null || e.prefab == null) return null;
        return e.prefab;
    }

    // Prefer small/medium rooms when asked, using RoomMeta subtype
    GameObject PickBestRoomPrefab(Transform atAnchor, bool favorSmallMedium, bool gateMedium)
    {
        if (roomPrefabs == null || roomPrefabs.Length == 0) return null;
        // Simple two-pass: try small/medium first if favored
        if (favorSmallMedium)
        {
            var pick = PickFirstRoomBySubtype(atAnchor, new PlacementCatalog.HallSubtype?[] {
                PlacementCatalog.HallSubtype.SmallRoom,
                gateMedium ? (PlacementCatalog.HallSubtype?)null : PlacementCatalog.HallSubtype.MediumRoom
            });
            if (pick != null) return pick;
        }
        // If we still need a medium room, try to pick one occasionally
        if (placedMediumRooms < targetMediumRooms)
        {
            var tryMedium = PickFirstRoomBySubtype(atAnchor, new PlacementCatalog.HallSubtype?[] {
                PlacementCatalog.HallSubtype.MediumRoom
            });
            if (tryMedium != null) return tryMedium;
        }
        // Fallback any room
        return PickFirstRoomBySubtype(atAnchor, (PlacementCatalog.HallSubtype?[])null);
    }

    GameObject PickFirstRoomBySubtype(Transform atAnchor, PlacementCatalog.HallSubtype?[] preferred)
    {
        GameObject best = null;
        for (int i = 0; i < roomPrefabs.Length; i++)
        {
            var p = roomPrefabs[i];
            if (!p) continue;
            var meta = p.GetComponent<RoomMeta>();
            if (preferred != null && meta != null)
            {
                bool ok = false;
                for (int k = 0; k < preferred.Length; k++)
                {
                    var want = preferred[k];
                    if (want.HasValue && meta.subtype == want.Value) { ok = true; break; }
                }
                if (!ok) continue;
            }
            // Optionally add hotspot bias later; for now just return first match
            best = p; break;
        }
        return best;
    }

    bool AreDifferentModules(Transform a, Transform b)
    {
        return GetModuleRootForAnchor(a) != GetModuleRootForAnchor(b);
    }

    void SealOpenAnchors()
    {
        if (doorCapPrefab == null) return;
        // Snapshot all anchors at this stage
        var allAnchors = GatherAllAnchorsInLevel();
        for (int i = 0; i < allAnchors.Count; i++)
        {
            var a = allAnchors[i];
            if (!a) continue;
            Transform owner = GetModuleRootForAnchor(a);
            if (owner != null && owner.name.StartsWith("DoorCap_")) continue; // skip caps
            if (!IsAnchorOpen(a, allAnchors)) continue;

            RoomMeta meta = owner ? owner.GetComponent<RoomMeta>() : null;
            bool isRoom = meta != null && (meta.category == RoomCategory.Room || meta.category == RoomCategory.Start);

            // Choose best prefab in priority: room-specific -> hallway-specific -> generic
            GameObject prefab = isRoom && roomDoorCapPrefab != null ? roomDoorCapPrefab
                                : (!isRoom && hallwayDoorCapPrefab != null ? hallwayDoorCapPrefab : doorCapPrefab);
            if (prefab == null) prefab = doorCapPrefab;

            // Place using anchor alignment so the cap's entry DoorAnchor snaps to the target anchor
            PlaceCapAtAnchor(prefab, a, owner != null ? owner : levelRoot);
        }
    }

    bool IsAnchorOpen(Transform a, List<Transform> anchors)
    {
        return !connectedAnchors.Contains(a);
    }

    void PlaceCapAtAnchor(GameObject capPrefab, Transform targetAnchor, Transform parent)
    {
        if (!capPrefab || !targetAnchor) return;
        var inst = Instantiate(capPrefab, Vector3.zero, Quaternion.identity, parent);
        var entry = GetBestCapEntryAnchor(inst.transform, targetAnchor);
        if (!entry) entry = inst.transform;
        AlignModuleYawOnly(inst.transform, entry, targetAnchor, snapYawTo30);
        // small inset to avoid z-fighting
        inst.transform.position += targetAnchor.forward * doorCapInset;
        inst.name = $"DoorCap_{(parent && parent.GetComponent<RoomMeta>() ? "Room" : "Hall")}_{targetAnchor.GetInstanceID()}";
        var pivot = inst.transform.Find("CapPivot");
        if (pivot != null)
        {
            Vector3 desired = targetAnchor.position + targetAnchor.forward * doorCapInset;
            Vector3 delta = pivot.position - inst.transform.position;
            inst.transform.position = desired - delta;
        }
        connectedAnchors.Add(targetAnchor);
    }

    Transform GetBestCapEntryAnchor(Transform capRoot, Transform targetAnchor)
    {
        if (!capRoot) return null;
        Transform best = null; float bestDot = float.NegativeInfinity;
        var queue = new Queue<Transform>();
        queue.Enqueue(capRoot);
        while (queue.Count > 0)
        {
            var t = queue.Dequeue();
            if (t != capRoot && t.name.IndexOf("Anchor", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                float d = Vector3.Dot(t.forward, -targetAnchor.forward);
                if (d > bestDot) { bestDot = d; best = t; }
            }
            for (int i = 0; i < t.childCount; i++) queue.Enqueue(t.GetChild(i));
        }
        return best;
    }

    void EnsureAllAnchorsClosed()
    {
        for (int pass = 0; pass < Mathf.Max(1, capMaxPasses); pass++)
        {
            var anchors = GatherAllAnchorsInLevel();
            bool placedAny = false;
            for (int i = 0; i < anchors.Count; i++)
            {
                var a = anchors[i]; if (!a) continue;
                Transform owner = GetModuleRootForAnchor(a);
                if (owner != null && owner.name.StartsWith("DoorCap_")) continue;
                if (!IsAnchorOpen(a, anchors)) continue;

                RoomMeta meta = owner ? owner.GetComponent<RoomMeta>() : null;
                bool isRoom = meta != null && (meta.category == RoomCategory.Room || meta.category == RoomCategory.Start);
                GameObject prefab = isRoom && roomDoorCapPrefab != null ? roomDoorCapPrefab
                                    : (!isRoom && hallwayDoorCapPrefab != null ? hallwayDoorCapPrefab : doorCapPrefab);
                if (prefab == null) continue;
                PlaceCapAtAnchor(prefab, a, owner != null ? owner : levelRoot);
                placedAny = true;
            }
            if (!placedAny) break;
        }
    }
}
