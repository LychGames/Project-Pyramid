// Unity 2021+ compatible — simplified triangular lattice generator (compile-safe)
// Notes:
// - Removed hard dependency on a DoorAnchor C# type to avoid CS0246 when that script is missing.
// - Anchors are now discovered by transform name ("Anchor" prefix) across instances & prefabs.
// - If you DO have a DoorAnchor component, it still works automatically via name or reflection.
// - Alignment, lattice snapping, and simple spacing rules preserved.

using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

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
    [Tooltip("If true, do not place a start room; begin from a pre-placed medium room instead.")]
    [SerializeField] bool startFromMediumRoom = false;
    [Tooltip("If true, begin from a pre-placed large room instead of start/medium. RECOMMENDED for party games like Lethal Company style.")]
    [SerializeField] bool startFromLargeRoom = true;

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
    
    [Header("Small Room Settings")]
    [Tooltip("Boost the chance of placing small rooms during generation")]
    [SerializeField] float smallRoomChanceBoost = 0.3f; // Add 30% to small room chance
    [Tooltip("Target number of small rooms to place per level")]
    [SerializeField] int targetSmallRooms = 3;
    
    [Header("Spawn Room Settings")]
    [Tooltip("Dedicated spawn room prefab (should contain PlayerSpawn tag or SpawnPoint component)")]
    [SerializeField] GameObject spawnRoomPrefab;
    [Tooltip("Distance in cells from start (origin) to pre-place the medium room")] 
    [SerializeField] int mediumDistanceCells = 6;
    [Tooltip("Yaw index 0..5 for multiples of 60 degrees (0=+Z, 1=60°, 2=120°,...)")]
    [SerializeField] int mediumYawIndex = 0;
    [Tooltip("How many initial branches to grow from the medium room immediately after placement")] 
    [SerializeField] int initialBranchesFromMedium = 3;
    [Tooltip("How many steps to grow from each chosen medium-room anchor before free growth")] 
    [SerializeField] int initialStepsPerMediumBranch = 6;

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
    private Transform lastMediumRoot = null;
    private Transform lastLargeRoot = null;

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

    [ContextMenu("Configure for Lethal Company Style")]
    public void ConfigureForLethalCompanyStyle()
    {
        startFromLargeRoom = true;
        startFromMediumRoom = false;
        startIsVirtual = false;
        sealOpenAnchorsAtEnd = true;
        spawnPlayerOnComplete = false; // Player spawning handled by PlayerSpawner component
        initialBranchesFromMedium = Mathf.Max(2, initialBranchesFromMedium);
        reserveModulesForCaps = 0; // No need to reserve - door capping ignores module limits now!
        
        // Boost small room frequency for variety
        smallRoomChanceBoost = 0.4f; // 40% boost for small rooms
        targetSmallRooms = 4; // Try to place at least 4 small rooms
        
        // Spawn room will be placed naturally within the generated level
        
        Debug.Log("[Gen] Configured for Lethal Company style: starts from large room, boosts small room spawning, dedicated spawn room, ALL doorways capped");
    }

    [ContextMenu("Force Cap All Open Anchors")]
    public void ForceCapAllOpenAnchorsManual()
    {
        ForceCapAllOpenAnchors();
    }

    [ContextMenu("Final Aggressive Capping")]
    public void FinalAggressiveCappingManual()
    {
        FinalAggressiveCapping();
    }

    [ContextMenu("Ultra Aggressive Capping")]
    public void UltraAggressiveCappingManual()
    {
        UltraAggressiveCapping();
    }

    [ContextMenu("Simple Cap Unconnected Anchors")]
    public void SimpleCapUnconnectedAnchorsManual()
    {
        SimpleCapUnconnectedAnchors();
    }

    [ContextMenu("Nuclear Option - Cap EVERYTHING")]
    public void NuclearCapEverything()
    {
        Debug.Log("[Gen] NUCLEAR OPTION - Capping EVERY anchor with no checks...");
        
        var allTransforms = new List<Transform>();
        GetAllTransformsRecursive(levelRoot, allTransforms);
        
        int cappedCount = 0;
        foreach (Transform t in allTransforms)
        {
            if (t && t.name.IndexOf("Anchor", System.StringComparison.OrdinalIgnoreCase) >= 0)
            {
                // Skip if it's already a door cap
                if (t.name.StartsWith("DoorCap_")) continue;
                
                // Skip if it already has a cap child
                bool hasCapChild = false;
                foreach (Transform child in t)
                {
                    if (child.name.StartsWith("DoorCap_"))
                    {
                        hasCapChild = true;
                        break;
                    }
                }
                if (hasCapChild) continue;
                
                // CAP IT - NO QUESTIONS ASKED
                Transform owner = GetModuleRootForAnchor(t);
                if (owner == null) owner = t.parent;
                if (owner == null) owner = levelRoot;
                
                RoomMeta meta = owner ? owner.GetComponent<RoomMeta>() : null;
                bool isRoom = meta != null && (meta.category == RoomCategory.Room || meta.category == RoomCategory.Start);

                GameObject prefab = isRoom && roomDoorCapPrefab != null ? roomDoorCapPrefab
                                    : (!isRoom && hallwayDoorCapPrefab != null ? hallwayDoorCapPrefab : doorCapPrefab);
                if (prefab == null) prefab = doorCapPrefab;

                if (prefab != null)
                {
                    try
                    {
                        PlaceCapAtAnchor(prefab, t, owner);
                        cappedCount++;
                        Debug.Log($"[Gen] NUCLEAR: Capped {t.name} at {t.position}");
                    }
                    catch (System.Exception e)
                    {
                        Debug.LogError($"[Gen] NUCLEAR: Failed to cap {t.name}: {e.Message}");
                    }
                }
            }
        }
        
        Debug.Log($"[Gen] NUCLEAR OPTION complete. Capped {cappedCount} anchors with ZERO checks.");
    }

    void CapRemainingLargeRoomAnchors(Transform largeRoom)
    {
        if (!largeRoom || !doorCapPrefab) return;
        
        Debug.Log($"[Gen] Managing large room doorways for {largeRoom.name}");
        
        // Get all anchors on the large room
        var largeRoomAnchors = new List<Transform>();
        CollectAnchorsIntoList(largeRoom, largeRoomAnchors);
        
        // Count how many are already connected (have halls)
        int connectedCount = 0;
        var unconnectedAnchors = new List<Transform>();
        
        foreach (Transform anchor in largeRoomAnchors)
        {
            if (!anchor) continue;
            
            if (connectedAnchors.Contains(anchor))
            {
                connectedCount++;
                Debug.Log($"[Gen] Large room anchor {anchor.name} is connected");
            }
            else
            {
                // Check if it has a cap already
                bool hasCapChild = false;
                foreach (Transform child in anchor)
                {
                    if (child.name.StartsWith("DoorCap_"))
                    {
                        hasCapChild = true;
                        break;
                    }
                }
                if (!hasCapChild)
                {
                    unconnectedAnchors.Add(anchor);
                }
            }
        }
        
        Debug.Log($"[Gen] Large room has {connectedCount} connected anchors, {unconnectedAnchors.Count} unconnected");
        
        // Ensure we have 2-3 connections
        if (connectedCount < 2)
        {
            Debug.LogWarning($"[Gen] Large room only has {connectedCount} connections, should have at least 2!");
        }
        else if (connectedCount > 3)
        {
            Debug.LogWarning($"[Gen] Large room has {connectedCount} connections, maximum should be 3!");
        }
        
        // Cap excess unconnected anchors to maintain 2-3 open doorways
        int targetOpenDoorways = Mathf.Clamp(3 - connectedCount, 0, unconnectedAnchors.Count);
        int anchorsToCapCount = unconnectedAnchors.Count - targetOpenDoorways;
        
        if (anchorsToCapCount > 0)
        {
            Debug.Log($"[Gen] Capping {anchorsToCapCount} anchors to maintain 2-3 open doorways");
            
            for (int i = 0; i < anchorsToCapCount && i < unconnectedAnchors.Count; i++)
            {
                Transform anchor = unconnectedAnchors[i];
                try
                {
                    GameObject prefab = roomDoorCapPrefab != null ? roomDoorCapPrefab : doorCapPrefab;
                    PlaceCapAtAnchor(prefab, anchor, largeRoom);
                    Debug.Log($"[Gen] Large room capped: {anchor.name}");
                }
                catch (System.Exception e)
                {
                    Debug.LogError($"[Gen] Failed to cap large room anchor {anchor.name}: {e.Message}");
                }
            }
        }
        else
        {
            Debug.Log($"[Gen] Large room doorway count is appropriate, no additional capping needed");
        }
    }

    [ContextMenu("Force Level to Ground Level")]
    public void ForceLevelToGroundLevel()
    {
        int modulesAdjusted = 0;
        foreach (Transform module in placedModules)
        {
            if (module && module.position.y != 0f)
            {
                Vector3 pos = module.position;
                float oldY = pos.y;
                pos.y = 0f;
                module.position = pos;
                modulesAdjusted++;
                Debug.Log($"[Gen] Moved {module.name} from Y={oldY} to Y=0");
            }
        }
        Debug.Log($"[Gen] Forced {modulesAdjusted} modules to ground level (Y=0)");
    }

    void FinalAggressiveCapping()
    {
        Debug.Log("[Gen] Running final aggressive capping pass...");
        
        // Get ALL transforms in the level that might be anchors
        var allTransforms = new List<Transform>();
        GetAllTransformsRecursive(levelRoot, allTransforms);
        
        var uncappedAnchors = new List<Transform>();
        int totalAnchorsFound = 0;
        
        foreach (Transform t in allTransforms)
        {
            // Check if this looks like an anchor
            if (t.name.IndexOf("Anchor", System.StringComparison.OrdinalIgnoreCase) >= 0)
            {
                totalAnchorsFound++;
                
                // Skip if it's already a door cap
                Transform owner = GetModuleRootForAnchor(t);
                if (owner != null && owner.name.StartsWith("DoorCap_")) continue;
                
                // PROTECTION: Skip large room anchors to prevent blocking doorways
                if (owner == lastLargeRoot)
                {
                    Debug.Log($"[Gen] FINAL pass skipping large room anchor {t.name} - protecting large room exits");
                    continue;
                }
                
                // Skip if it has a door cap child
                bool hasCapChild = false;
                foreach (Transform child in t)
                {
                    if (child.name.StartsWith("DoorCap_"))
                    {
                        hasCapChild = true;
                        break;
                    }
                }
                if (hasCapChild) continue;
                
                // Check if it's really open (very permissive check)
                if (!connectedAnchors.Contains(t))
                {
                    Transform partner = FindPartnerAnchor(t);
                    if (partner == null || Vector3.Distance(t.position, partner.position) > connectionDistance)
                    {
                        uncappedAnchors.Add(t);
                        Debug.Log($"[Gen] Final pass found uncapped anchor: {t.name} at {t.position} (owner: {(owner ? owner.name : "null")})");
                    }
                }
            }
        }
        
        Debug.Log($"[Gen] Final aggressive pass: Found {totalAnchorsFound} total anchors, {uncappedAnchors.Count} still uncapped");
        
        // Cap all remaining uncapped anchors
        foreach (Transform anchor in uncappedAnchors)
        {
            if (!anchor) continue;
            
            Transform owner = GetModuleRootForAnchor(anchor);
            RoomMeta meta = owner ? owner.GetComponent<RoomMeta>() : null;
            bool isRoom = meta != null && (meta.category == RoomCategory.Room || meta.category == RoomCategory.Start);

            GameObject prefab = isRoom && roomDoorCapPrefab != null ? roomDoorCapPrefab
                                : (!isRoom && hallwayDoorCapPrefab != null ? hallwayDoorCapPrefab : doorCapPrefab);
            if (prefab == null) prefab = doorCapPrefab;

            if (prefab != null)
            {
                PlaceCapAtAnchor(prefab, anchor, owner != null ? owner : levelRoot);
                connectedAnchors.Add(anchor);
                Debug.Log($"[Gen] Final pass capped: {anchor.name} at {anchor.position}");
            }
        }
        
        Debug.Log($"[Gen] Final aggressive capping complete. Capped {uncappedAnchors.Count} additional anchors.");
    }

    void GetAllTransformsRecursive(Transform root, List<Transform> result)
    {
        if (!root) return;
        
        result.Add(root);
        foreach (Transform child in root)
        {
            GetAllTransformsRecursive(child, result);
        }
    }

    void UltraAggressiveCapping()
    {
        Debug.Log("[Gen] Running ULTRA-aggressive capping pass - will cap EVERYTHING that looks like an anchor...");
        
        // Get absolutely EVERYTHING in the level
        var allTransforms = new List<Transform>();
        GetAllTransformsRecursive(levelRoot, allTransforms);
        
        var uncappedAnchors = new List<Transform>();
        int totalChecked = 0;
        
        foreach (Transform t in allTransforms)
        {
            // Ultra-permissive anchor detection - anything with "Anchor" in the name
            if (t && t.name.IndexOf("Anchor", System.StringComparison.OrdinalIgnoreCase) >= 0)
            {
                totalChecked++;
                
                // Skip if it's already a door cap or has a door cap child
                if (t.name.StartsWith("DoorCap_")) continue;
                
                bool hasCapChild = false;
                bool hasCapSibling = false;
                
                // Check children for caps
                foreach (Transform child in t)
                {
                    if (child.name.StartsWith("DoorCap_"))
                    {
                        hasCapChild = true;
                        break;
                    }
                }
                
                // Check siblings for caps (in case cap is at same level)
                if (t.parent != null)
                {
                    foreach (Transform sibling in t.parent)
                    {
                        if (sibling != t && sibling.name.StartsWith("DoorCap_") && 
                            Vector3.Distance(sibling.position, t.position) < 2f)
                        {
                            hasCapSibling = true;
                            break;
                        }
                    }
                }
                
                if (!hasCapChild && !hasCapSibling)
                {
                    // Check if there's anything very close that might be connected
                    bool hasVeryClosePartner = false;
                    foreach (Transform other in allTransforms)
                    {
                        if (other != t && other.name.IndexOf("Anchor", System.StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            float dist = Vector3.Distance(t.position, other.position);
                            // Use a more lenient distance for junction detection - junctions can have weird spacing
                            if (dist < 4f) // Increased to 4f for large room connections
                            {
                                // Additional check: make sure they're roughly facing each other (opposite directions)
                                // OR if they're very close (< 1f), assume they're connected regardless of direction
                                float dot = Vector3.Dot(t.forward, other.forward);
                                if (dot < -0.3f || dist < 1f) // More lenient direction check OR very close
                                {
                                    hasVeryClosePartner = true;
                                    Debug.Log($"[Gen] ULTRA pass skipping {t.name} - found partner {other.name} at distance {dist:F2}, dot {dot:F2}");
                                    break;
                                }
                            }
                        }
                    }
                    
                    if (!hasVeryClosePartner)
                    {
                        uncappedAnchors.Add(t);
                        Debug.Log($"[Gen] ULTRA pass found uncapped: {t.name} at {t.position} (parent: {(t.parent ? t.parent.name : "null")})");
                    }
                }
            }
        }
        
        Debug.Log($"[Gen] ULTRA-aggressive pass: Checked {totalChecked} potential anchors, found {uncappedAnchors.Count} to cap");
        
        // Cap everything found
        foreach (Transform anchor in uncappedAnchors)
        {
            if (!anchor) continue;
            
            Transform owner = GetModuleRootForAnchor(anchor);
            if (owner == null) owner = anchor.parent; // Fallback to parent
            if (owner == null) owner = levelRoot; // Ultimate fallback
            
            // PROTECTION: Skip large room anchors to prevent blocking doorways
            if (owner == lastLargeRoot)
            {
                Debug.Log($"[Gen] ULTRA pass skipping large room anchor {anchor.name} - protecting large room exits");
                continue;
            }
            
            RoomMeta meta = owner ? owner.GetComponent<RoomMeta>() : null;
            bool isRoom = meta != null && (meta.category == RoomCategory.Room || meta.category == RoomCategory.Start);

            GameObject prefab = isRoom && roomDoorCapPrefab != null ? roomDoorCapPrefab
                                : (!isRoom && hallwayDoorCapPrefab != null ? hallwayDoorCapPrefab : doorCapPrefab);
            if (prefab == null) prefab = doorCapPrefab;

            if (prefab != null)
            {
                try
                {
                    PlaceCapAtAnchor(prefab, anchor, owner);
                    Debug.Log($"[Gen] ULTRA pass capped: {anchor.name} at {anchor.position}");
                }
                catch (System.Exception e)
                {
                    Debug.LogError($"[Gen] Failed to cap {anchor.name}: {e.Message}");
                }
            }
        }
        
        Debug.Log($"[Gen] ULTRA-aggressive capping complete. Attempted to cap {uncappedAnchors.Count} additional anchors.");
    }

    void SimpleCapUnconnectedAnchors()
    {
        Debug.Log("[Gen] === SIMPLE CAPPING: Only capping truly isolated anchors ===");
        
        // Get all anchors in the level
        var allAnchors = GatherAllAnchorsInLevel();
        var anchorsToCap = new List<Transform>();
        
        foreach (Transform anchor in allAnchors)
        {
            if (!anchor) continue;
            
            // Skip if already has a cap
            bool hasCapChild = false;
            foreach (Transform child in anchor)
            {
                if (child.name.StartsWith("DoorCap_"))
                {
                    hasCapChild = true;
                    break;
                }
            }
            if (hasCapChild) continue;
            
            // Check if this anchor has ANY other anchor within connection distance
            bool hasNearbyPartner = false;
            foreach (Transform otherAnchor in allAnchors)
            {
                if (otherAnchor == anchor || !otherAnchor) continue;
                
                float distance = Vector3.Distance(anchor.position, otherAnchor.position);
                if (distance < connectionDistance)
                {
                    hasNearbyPartner = true;
                    Debug.Log($"[Gen] Anchor {anchor.name} has nearby partner {otherAnchor.name} at distance {distance:F2} - NOT capping");
                    break;
                }
            }
            
            // Only cap if truly isolated
            if (!hasNearbyPartner)
            {
                anchorsToCap.Add(anchor);
                Debug.Log($"[Gen] Anchor {anchor.name} is isolated - WILL cap");
            }
        }
        
        // Cap the isolated anchors
        int cappedCount = 0;
        foreach (Transform anchor in anchorsToCap)
        {
            try
            {
                Transform owner = GetModuleRootForAnchor(anchor);
                RoomMeta meta = owner ? owner.GetComponent<RoomMeta>() : null;
                bool isRoom = meta != null && meta.category == RoomCategory.Room;
                
                GameObject prefab = isRoom && roomDoorCapPrefab != null ? roomDoorCapPrefab : doorCapPrefab;
                if (prefab != null)
                {
                    PlaceCapAtAnchor(prefab, anchor, owner);
                    cappedCount++;
                    Debug.Log($"[Gen] SIMPLE capped isolated anchor: {anchor.name}");
                }
            }
            catch (System.Exception e)
            {
                Debug.LogError($"[Gen] Failed to cap anchor {anchor.name}: {e.Message}");
            }
        }
        
        Debug.Log($"[Gen] === SIMPLE CAPPING COMPLETE: Capped {cappedCount} truly isolated anchors ===");
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

            if (!startFromMediumRoom && !startFromLargeRoom)
            {
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
            }

            // Generate the level
            if (!startFromMediumRoom && !startFromLargeRoom && forceInitialGrowthFromAllStartAnchors)
            {
                var startAnchors = new List<Transform>(availableAnchors);
                GrowFromInitialAnchors(startAnchors, Mathf.Max(1, initialStepsPerStart));
            }

            // Pre-place a medium room on the lattice and fan out from multiple doors
            // Pre-place LARGE first if requested
            Transform mediumRootPlaced = null;
            if (startFromLargeRoom || (!startFromMediumRoom && preplaceMediumRoom))
            {
                var large = TryPreplaceLargeRoom();
                if (large)
                {
                    lastLargeRoot = large;
                    // Ensure at least 3 branches from large room (minimum 2 exits, preferably 3)
                    int minBranches = startFromLargeRoom ? Mathf.Max(2, initialBranchesFromMedium) : Mathf.Max(3, initialBranchesFromMedium);
                    GrowLoopingBranchesFromMedium(large, minBranches, Mathf.Max(4, initialStepsPerMediumBranch));
                    
                    // DISABLED: Don't cap large room early - let it stay open for later connections
                    // CapRemainingLargeRoomAnchors(large);
                }
                else if (startFromLargeRoom)
                {
                    Debug.LogError("[Gen] Failed to place large room as requested start! Cannot proceed without large room.");
                    return;
                }
            }
            // Then medium if desired
            if (preplaceMediumRoom && !startFromLargeRoom)
            {
                mediumRootPlaced = TryPreplaceMediumRoom();
                if (mediumRootPlaced)
                {
                    lastMediumRoot = mediumRootPlaced;
                    GrowLoopingBranchesFromMedium(mediumRootPlaced, Mathf.Max(1, initialBranchesFromMedium), Mathf.Max(1, initialStepsPerMediumBranch));
                }
            }

            GenerateFromAnchors();

            // Optionally ensure medium room placement before sealing
            EnsureMediumRooms();
            
            // Ensure small rooms get placed
            EnsureSmallRooms();

            // Place the dedicated spawn room
            PlaceSpawnRoom();

            // Force-run capping AFTER everything is placed to catch all anchors
            if (sealOpenAnchorsAtEnd && doorCapPrefab != null)
            {
                // Run capping multiple times to ensure ALL anchors get capped
                ForceCapAllOpenAnchors();
                
                // Run one more aggressive pass to catch any missed anchors
                FinalAggressiveCapping();
                
                // Ultra-aggressive final pass - cap EVERYTHING that looks like an anchor
                UltraAggressiveCapping();
            }

            // Player spawning will be handled separately by PlayerSpawner component
            // Do NOT spawn player during level generation

            

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
            // Only adjust Y if it's at an extreme position (preserving multi-floor capability)
            Vector3 pos = startRoot.position;
            if (Mathf.Abs(pos.y) > 20f)
            {
                pos.y = 0f;
                startRoot.position = pos;
                Debug.Log("[Gen] Corrected extreme start room Y position");
            }
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
        start.transform.position = Vector3.zero; // Virtual start is already at ground level

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
            bool isEndcap = (partner == null);
            bool favorSmallMediumRooms = !isEndcap; // prefer rooms inline, not at endcaps
            bool gateMediumRooms = loopConnectorsPlaced < minLoopConnectorsBeforeMediumRooms;
            GameObject room = PickBestRoomPrefab(anchor, favorSmallMediumRooms, gateMediumRooms);

            float roll = (float)rng.NextDouble();
            if (!isEndcap)
            {
                // Enhanced room chance with small room boost
                float roomChance = 0.5f;
                if (room != null && IsSmallRoom(room)) roomChance += smallRoomChanceBoost;
                
                // Bias rooms (especially small ones): Room 50%+boost%, Hall 35%, Conn 15%
                if (roll < roomChance && room != null) return room;
                else if (roll < (roomChance + 0.35f) && hall != null) return hall;
                else if (conn != null) return conn;
            }
            else
            {
                // Endcaps: avoid rooms; prefer hall/connector
                if (roll < 0.6f && hall != null) return hall;
                else if (conn != null) return conn;
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
            instanceTransform.position -= targetAnchor.forward * anchorJoinOffset;
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

    // Ensure at least targetSmallRooms have been placed
    void EnsureSmallRooms()
    {
        // Count current small rooms
        int placedSmallRooms = 0;
        foreach (Transform module in placedModules)
        {
            if (module)
            {
                var meta = module.GetComponent<RoomMeta>();
                if (meta != null && meta.subtype == PlacementCatalog.HallSubtype.SmallRoom)
                {
                    placedSmallRooms++;
                }
            }
        }
        
        if (placedSmallRooms >= targetSmallRooms) return;
        
        Debug.Log($"[Gen] EnsureSmallRooms: Have {placedSmallRooms}, target {targetSmallRooms}. Placing more...");
        
        // Collect small room prefabs
        var smallPrefabs = new List<GameObject>();
        for (int i = 0; i < roomPrefabs.Length; i++)
        {
            var p = roomPrefabs[i]; if (!p) continue;
            var meta = p.GetComponent<RoomMeta>();
            if (meta != null && meta.subtype == PlacementCatalog.HallSubtype.SmallRoom)
                smallPrefabs.Add(p);
        }
        if (smallPrefabs.Count == 0) return;
        
        // Find open anchors and try to place small rooms
        var allAnchors = GatherAllAnchorsInLevel();
        var openAnchors = new List<Transform>();
        foreach (var anchor in allAnchors)
        {
            if (anchor && IsAnchorOpen(anchor, allAnchors))
            {
                openAnchors.Add(anchor);
            }
        }
        
        int attempts = 0;
        int maxAttempts = Mathf.Min(20, openAnchors.Count);
        
        while (placedSmallRooms < targetSmallRooms && attempts < maxAttempts && openAnchors.Count > 0)
        {
            attempts++;
            
            // Pick a random open anchor
            var anchor = openAnchors[UnityEngine.Random.Range(0, openAnchors.Count)];
            openAnchors.Remove(anchor);
            
            // Try to place a small room
            var smallRoom = smallPrefabs[UnityEngine.Random.Range(0, smallPrefabs.Count)];
            if (!IsConnectionCompatible(smallRoom, anchor)) continue;
            
            var entry = FindEntryAnchorOnPrefab(smallRoom);
            if (!entry) continue;
            
            Transform placed = PlaceModule(smallRoom, entry, anchor, out Transform placedEntryAnchor);
            if (!placed) continue;
            
            placedModules.Add(placed);
            CollectAnchorsIntoList(placed, availableAnchors);
            if (placedEntryAnchor) availableAnchors.Remove(placedEntryAnchor);
            availableAnchors.Remove(anchor);
            placedSmallRooms++;
            
            Debug.Log($"[Gen] Placed small room {smallRoom.name} at {placed.position}. Total small rooms: {placedSmallRooms}");
        }
        
        Debug.Log($"[Gen] EnsureSmallRooms complete: {placedSmallRooms}/{targetSmallRooms} small rooms placed");
    }

    void PlaceSpawnRoom()
    {
        if (spawnRoomPrefab == null)
        {
            Debug.LogWarning("[Gen] No spawn room prefab assigned. Player spawning may not work properly.");
            return;
        }

        // Find a random open anchor to place the spawn room naturally
        var allAnchors = GatherAllAnchorsInLevel();
        var openAnchors = new List<Transform>();
        
        foreach (var anchor in allAnchors)
        {
            if (anchor && IsAnchorOpen(anchor, allAnchors))
            {
                Transform owner = GetModuleRootForAnchor(anchor);
                // Skip anchors from door caps and prefer anchors from rooms/halls
                if (owner != null && !owner.name.StartsWith("DoorCap_"))
                {
                    openAnchors.Add(anchor);
                }
            }
        }

        if (openAnchors.Count == 0)
        {
            Debug.LogWarning("[Gen] No open anchors found for spawn room placement. Placing at origin.");
            Transform spawnRoom = Instantiate(spawnRoomPrefab, Vector3.zero, Quaternion.identity, levelRoot).transform;
            spawnRoom.name = "SpawnRoom_Player";
            placedModules.Add(spawnRoom);
            return;
        }

        // Try to place spawn room at a random open anchor
        int attempts = 0;
        int maxAttempts = Mathf.Min(10, openAnchors.Count);
        
        while (attempts < maxAttempts)
        {
            attempts++;
            
            // Pick a random open anchor
            Transform targetAnchor = openAnchors[UnityEngine.Random.Range(0, openAnchors.Count)];
            openAnchors.Remove(targetAnchor);
            
            // Check if spawn room is compatible with this anchor
            if (!IsConnectionCompatible(spawnRoomPrefab, targetAnchor)) continue;
            
            // Find entry anchor on spawn room
            Transform entryAnchor = FindEntryAnchorOnPrefab(spawnRoomPrefab);
            if (!entryAnchor) continue;
            
            // Try to place the spawn room
            Transform placedSpawnRoom = PlaceModule(spawnRoomPrefab, entryAnchor, targetAnchor, out Transform placedEntryAnchor);
            if (!placedSpawnRoom) continue;
            
            // Successfully placed!
            placedSpawnRoom.name = "SpawnRoom_Player";
            placedModules.Add(placedSpawnRoom);
            
            // Remove the used anchor from available anchors
            if (availableAnchors.Contains(targetAnchor)) availableAnchors.Remove(targetAnchor);
            if (placedEntryAnchor && availableAnchors.Contains(placedEntryAnchor)) availableAnchors.Remove(placedEntryAnchor);
            
            // Mark anchors as connected
            connectedAnchors.Add(targetAnchor);
            if (placedEntryAnchor) connectedAnchors.Add(placedEntryAnchor);
            
            Debug.Log($"[Gen] Placed spawn room naturally at {placedSpawnRoom.position} connected to {GetModuleRootForAnchor(targetAnchor)?.name}");
            
            // Validate spawn room
            ValidateSpawnRoom(placedSpawnRoom);
            return;
        }
        
        Debug.LogWarning("[Gen] Failed to place spawn room at any open anchor. Trying fallback placement.");
        
        // Fallback: place spawn room away from everything
        Vector3 fallbackPos = FindEmptySpaceForSpawnRoom();
        Transform fallbackSpawnRoom = Instantiate(spawnRoomPrefab, fallbackPos, Quaternion.identity, levelRoot).transform;
        fallbackSpawnRoom.name = "SpawnRoom_Player_Fallback";
        placedModules.Add(fallbackSpawnRoom);
        
        Debug.Log($"[Gen] Placed spawn room at fallback position {fallbackPos}");
        ValidateSpawnRoom(fallbackSpawnRoom);
    }

    void ValidateSpawnRoom(Transform spawnRoom)
    {
        // Check if spawn room has proper spawn point
        bool hasSpawnPoint = false;
        if (spawnRoom.GetComponentInChildren<SpawnPoint>() != null) hasSpawnPoint = true;
        
        // Check for PlayerSpawn tagged objects
        foreach (Transform child in spawnRoom.GetComponentsInChildren<Transform>())
        {
            if (child.CompareTag("PlayerSpawn"))
            {
                hasSpawnPoint = true;
                break;
            }
        }

        if (!hasSpawnPoint)
        {
            Debug.LogWarning("[Gen] Spawn room doesn't contain a SpawnPoint component or 'PlayerSpawn' tagged GameObject. Player spawning may fail!");
        }
        else
        {
            Debug.Log("[Gen] Spawn room contains proper spawn point. Player spawning should work correctly.");
        }
    }

    Vector3 FindEmptySpaceForSpawnRoom()
    {
        // Find a position that's not too close to existing modules
        for (int attempts = 0; attempts < 20; attempts++)
        {
            Vector3 testPos = new Vector3(
                UnityEngine.Random.Range(-100f, 100f),
                0f,
                UnityEngine.Random.Range(-100f, 100f)
            );
            
            bool tooClose = false;
            foreach (Transform module in placedModules)
            {
                if (module && Vector3.Distance(testPos, module.position) < minModuleDistance * 2f)
                {
                    tooClose = true;
                    break;
                }
            }
            
            if (!tooClose) return testPos;
        }
        
        // Ultimate fallback
        return new Vector3(100f, 0f, 100f);
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

    // Grow from medium room: pick several anchors, grow longer runs prioritizing halls and looping via connectors when partners exist
    void GrowLoopingBranchesFromMedium(Transform root, int branches, int steps)
    {
        if (!root) return;
        var rootAnchors = new List<Transform>();
        CollectAnchorsIntoList(root, rootAnchors);
        // Shuffle anchors to avoid bias
        for (int i = 0; i < rootAnchors.Count; i++)
        {
            int j = UnityEngine.Random.Range(i, rootAnchors.Count);
            (rootAnchors[i], rootAnchors[j]) = (rootAnchors[j], rootAnchors[i]);
        }
        int grown = 0;
        for (int i = 0; i < rootAnchors.Count && grown < branches; i++)
        {
            Transform a = rootAnchors[i];
            if (!a) continue;
            if (!availableAnchors.Contains(a)) continue;
            GrowLoopFromAnchor(a, steps);
            grown++;
        }
    }

    void GrowLoopFromAnchor(Transform startAnchor, int steps)
    {
        Transform current = startAnchor;
        for (int s = 0; s < steps; s++)
        {
            if (current == null) break;
            if (!availableAnchors.Contains(current)) break;
            // If a partner exists, try connector first (triple connector weighted)
            var partner = FindPartnerAnchor(current);
            if (partner != null)
            {
                GameObject conn = PickWeightedFromCatalog(PlacementCatalog.HallSubtype.Junction3_V, Vector3.Distance(current.position, Vector3.zero))
                                  ?? PickBestConnector(current);
                if (conn != null)
                {
                    var entry = FindEntryAnchorOnPrefab(conn);
                    if (entry != null)
                    {
                        var placed = PlaceModule(conn, entry, current, out Transform placedEntry);
                        if (placed != null)
                        {
                            placedModules.Add(placed);
                            nonStartPlacements++;
                            placementsSinceLastConnector++;
                            CollectAnchorsIntoList(placed, availableAnchors);
                            if (placedEntry) availableAnchors.Remove(placedEntry);
                            availableAnchors.Remove(current);
                            // continue forward from the new piece
                            current = FindForwardMostAnchor(placed, current.forward);
                            continue;
                        }
                    }
                }
            }
            // Otherwise, place a hall
            GameObject hall = PickBestHall(current);
            if (hall == null)
            {
                hall = PickBestPrefab(hallwayPrefabs, current, preferHighAnchorCountNearHotspot);
            }
            if (hall != null)
            {
                var entry = FindEntryAnchorOnPrefab(hall);
                if (entry != null)
                {
                    var placed = PlaceModule(hall, entry, current, out Transform placedEntry);
                    if (placed != null)
                    {
                        placedModules.Add(placed);
                        nonStartPlacements++;
                        placementsSinceLastConnector++;
                        CollectAnchorsIntoList(placed, availableAnchors);
                        if (placedEntry) availableAnchors.Remove(placedEntry);
                        availableAnchors.Remove(current);
                        current = FindForwardMostAnchor(placed, current.forward);
                        continue;
                    }
                }
            }
            // If both attempts failed, remove the anchor and stop this branch
            availableAnchors.Remove(current);
            break;
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
        // Keep Y-coordinate reasonable for multi-floor rooms, but prevent extreme heights
        if (Mathf.Abs(pos.y) > 20f) // Only correct if Y is extremely high/low
        {
            pos.y = 0f;
            Debug.Log($"[Gen] Corrected extreme Y position for medium room");
        }

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

    // Place a large room at a lattice-aligned position offset from origin
    Transform TryPreplaceLargeRoom()
    {
        // Find a large room prefab
        GameObject large = null;
        for (int i = 0; i < roomPrefabs.Length; i++)
        {
            var p = roomPrefabs[i]; if (!p) continue;
            var meta = p.GetComponent<RoomMeta>();
            if (meta != null && meta.subtype == PlacementCatalog.HallSubtype.LargeRoom)
            { large = p; break; }
        }
        if (large == null) return null;

        float yaw = Mathf.Repeat(mediumYawIndex, 6) * 60f;
        Vector3 dir = Quaternion.Euler(0f, yaw, 0f) * Vector3.forward;
        Vector3 pos = SnapToLattice(dir.normalized * (cellSize * Mathf.Max(1, mediumDistanceCells)));
        // Force large room to ground level (Y=0) to prevent underground spawning issues
        float originalY = pos.y;
        pos.y = 0f;
        if (originalY != 0f)
        {
            Debug.Log($"[Gen] Large room Y position corrected: {originalY} -> 0 (preventing underground spawn)");
        }
        var temp = new GameObject("Large_TargetAnchor").transform;
        temp.SetParent(levelRoot, false);
        temp.position = pos;
        temp.rotation = Quaternion.Euler(0f, yaw, 0f);
        var entry = FindEntryAnchorOnPrefab(large); if (!entry) { DestroyImmediate(temp.gameObject); return null; }
        Transform placed = PlaceModule(large, entry, temp, out Transform placedEntryAnchor);
        DestroyImmediate(temp.gameObject);
        if (!placed) return null;
        placedModules.Add(placed);
        nonStartPlacements++;
        placementsSinceLastConnector++;
        CollectAnchorsIntoList(placed, availableAnchors);
        if (placedEntryAnchor) availableAnchors.Remove(placedEntryAnchor);
        return placed;
    }

    [Header("Player Spawn (optional)")]
    [SerializeField] bool spawnPlayerOnComplete = false;
    [SerializeField] GameObject playerPrefab;
    [SerializeField] float minPlayerSpawnDistanceFromMedium = 30f;
    [SerializeField] int playerSpawnTryLimit = 50;
    [SerializeField] LayerMask spawnGroundMask = ~0;
    [Tooltip("Extra vertical lift applied to spawn to avoid clipping floors")]
    [SerializeField] float spawnVerticalLift = 0.5f;
    [SerializeField] float spawnGroundClearance = 0.1f;
    [Header("Player Spawn (NavMesh)")]
    [SerializeField] bool useNavMeshForSpawn = true;
    [SerializeField] float navSampleRadius = 4f;
    [SerializeField] int navMeshAreaMask = -1; // NavMesh.AllAreas
    [Header("Player Spawn (Door Anchor)")]
    [SerializeField] bool spawnFromDoorAnchors = true;
    [Tooltip("Meters to move inward from a doorway along -forward to place the player")]
    [SerializeField] float spawnFromAnchorBack = 2.5f;
    [Tooltip("If true, require an overhead ceiling to accept a spawn (prevents roof spawns). Disable if levels are open)")]
    [SerializeField] bool requireOverheadCeiling = true;

    void TrySpawnPlayerFarFromHub()
    {
        if (!spawnPlayerOnComplete || playerPrefab == null) return;
        Vector3 hubPos = (lastLargeRoot != null ? lastLargeRoot.position : (lastMediumRoot != null ? lastMediumRoot.position : Vector3.zero));
        float minSqr = minPlayerSpawnDistanceFromMedium * minPlayerSpawnDistanceFromMedium;

        // Build reachable module set from hub so we never pick isolated islands
        Transform hubRoot = (lastLargeRoot != null ? lastLargeRoot : lastMediumRoot);
        var reachableOwners = BuildReachableOwners(hubRoot);

        // Prefer door-anchor-based spawn inside geometry
        if (spawnFromDoorAnchors)
        {
            var anchorsAll = GatherAllAnchorsInLevel();
            int triesA = 0;
            while (triesA < playerSpawnTryLimit && anchorsAll.Count > 0)
            {
                triesA++;
                var a = anchorsAll[UnityEngine.Random.Range(0, anchorsAll.Count)];
                if (!a) continue;
                var owner = GetModuleRootForAnchor(a);
                if (!owner || owner.name.StartsWith("DoorCap_")) continue;
                if (reachableOwners != null && !reachableOwners.Contains(owner)) continue;
                var metaOwn = owner.GetComponent<RoomMeta>();
                if (metaOwn == null) continue;
                if (!(metaOwn.category == RoomCategory.Room || metaOwn.category == RoomCategory.Hallway)) continue;
                // Avoid hub proximity
                if ((owner.position - hubPos).sqrMagnitude < minSqr) continue;
                // Move inward from the doorway along -forward
                Vector3 baseCandidate = a.position - a.forward * Mathf.Max(0.1f, spawnFromAnchorBack);
                baseCandidate += Vector3.up * (spawnGroundClearance + Mathf.Max(0f, spawnVerticalLift));
                // Do NOT allow generation here – only test ground
                if (TrySpawnAtCandidate(baseCandidate)) return;
            }
        }

        // Build candidate anchor-based positions inside halls/rooms away from hub and reachable
        var moduleCandidates = new List<Transform>();
        for (int i = 0; i < placedModules.Count; i++)
        {
            var t = placedModules[i]; if (!t) continue;
            if (t.name.StartsWith("DoorCap_")) continue;
            if (reachableOwners != null && !reachableOwners.Contains(t)) continue;
            var meta = t.GetComponent<RoomMeta>(); if (meta == null) continue;
            if (meta.category == RoomCategory.Hallway || meta.category == RoomCategory.Room)
            {
                if ((t.position - hubPos).sqrMagnitude >= minSqr) moduleCandidates.Add(t);
            }
        }
        if (moduleCandidates.Count == 0) moduleCandidates.AddRange(placedModules);

        int tries = 0;
        while (tries < playerSpawnTryLimit)
        {
            tries++;
            var m = moduleCandidates[UnityEngine.Random.Range(0, moduleCandidates.Count)];
            if (!m) continue;
            // Use an anchor offset inward to avoid walls
            var anchors = new List<Transform>();
            CollectAnchorsIntoList(m, anchors);
            Vector3 basePos = m.position + Vector3.up * 3f;
            if (anchors.Count > 0)
            {
                var a = anchors[UnityEngine.Random.Range(0, anchors.Count)];
                if (a) basePos = a.position + a.forward * 1.5f + Vector3.up * 3f;
            }
            // Prefer NavMesh sampling if available
            bool spawned = false;
            if (useNavMeshForSpawn)
            {
                NavMeshHit nHit;
                if (NavMesh.SamplePosition(basePos, out nHit, navSampleRadius, navMeshAreaMask))
                {
                    Vector3 candidate = nHit.position + Vector3.up * (spawnGroundClearance + Mathf.Max(0f, spawnVerticalLift));
                    spawned = TrySpawnAtCandidate(candidate);
                    if (spawned) return;
                }
            }
            // Fallback: Raycast down to floor
            RaycastHit hit;
            if (Physics.Raycast(basePos, Vector3.down, out hit, 20f, spawnGroundMask, QueryTriggerInteraction.Ignore))
            {
                Vector3 candidate = hit.point + Vector3.up * (spawnGroundClearance + Mathf.Max(0f, spawnVerticalLift));
                spawned = TrySpawnAtCandidate(candidate);
                if (spawned) return;
            }
        }
    }

    bool TrySpawnAtCandidate(Vector3 candidate)
    {
        // Require reasonably horizontal floor beneath
        RaycastHit ground;
        if (!Physics.Raycast(candidate + Vector3.up * 0.25f, Vector3.down, out ground, 1f, spawnGroundMask, QueryTriggerInteraction.Ignore))
            return false;
        if (Vector3.Dot(ground.normal, Vector3.up) < 0.6f) return false;
        // Require overhead ceiling within ~3.5m to avoid spawning on roofs
        if (requireOverheadCeiling)
        {
            if (!Physics.Raycast(candidate + Vector3.up * 0.1f, Vector3.up, 3.5f, spawnGroundMask, QueryTriggerInteraction.Ignore))
                return false;
        }
        // Small capsule overlap test for a typical controller
        float radius = 0.3f; float height = 1.8f;
        Vector3 p1 = candidate + Vector3.up * 0.1f;
        Vector3 p2 = candidate + Vector3.up * (height - 0.1f);
        if (Physics.CheckCapsule(p1, p2, radius, spawnGroundMask, QueryTriggerInteraction.Ignore))
            return false;
        SpawnPlayerGrounded(candidate);
        return true;
    }

    // Build connectivity graph between modules via anchors and return set of modules reachable from hubRoot
    HashSet<Transform> BuildReachableOwners(Transform hubRoot)
    {
        if (!hubRoot) return null;
        // Map owner -> anchors
        var ownerToAnchors = new Dictionary<Transform, List<Transform>>();
        for (int i = 0; i < placedModules.Count; i++)
        {
            var owner = placedModules[i]; if (!owner) continue;
            if (owner.name.StartsWith("DoorCap_")) continue;
            var list = new List<Transform>();
            CollectAnchorsIntoList(owner, list);
            ownerToAnchors[owner] = list;
        }
        // Build adjacency
        var neighbors = new Dictionary<Transform, List<Transform>>();
        var owners = new List<Transform>(ownerToAnchors.Keys);
        for (int i = 0; i < owners.Count; i++)
        {
            var oi = owners[i]; var ai = ownerToAnchors[oi];
            if (!neighbors.ContainsKey(oi)) neighbors[oi] = new List<Transform>();
            for (int j = 0; j < owners.Count; j++)
            {
                if (i == j) continue; var oj = owners[j]; var aj = ownerToAnchors[oj];
                // If any anchor pair could connect, link owners
                bool linked = false;
                for (int ia = 0; ia < ai.Count && !linked; ia++)
                {
                    var a = ai[ia]; if (!a) continue;
                    for (int ja = 0; ja < aj.Count && !linked; ja++)
                    {
                        var b = aj[ja]; if (!b) continue;
                        if (AnchorsCouldConnectForCapping(a, b)) { linked = true; break; }
                    }
                }
                if (linked) neighbors[oi].Add(oj);
            }
        }
        // BFS from hubRoot owner
        var reachable = new HashSet<Transform>();
        var q = new Queue<Transform>();
        var startOwner = GetModuleRootForAnchor(hubRoot) ?? hubRoot;
        if (!neighbors.ContainsKey(startOwner)) return reachable;
        q.Enqueue(startOwner); reachable.Add(startOwner);
        while (q.Count > 0)
        {
            var cur = q.Dequeue();
            if (!neighbors.TryGetValue(cur, out var neigh)) continue;
            for (int k = 0; k < neigh.Count; k++)
            {
                var n = neigh[k]; if (!n || reachable.Contains(n)) continue;
                reachable.Add(n); q.Enqueue(n);
            }
        }
        return reachable;
    }

    void SpawnPlayerGrounded(Vector3 startPos)
    {
        var player = Instantiate(playerPrefab, startPos, Quaternion.identity);
        // Use CC from children if not on root
        var cc = player.GetComponentInChildren<CharacterController>();
        var rb = player.GetComponent<Rigidbody>();
        if (rb) rb.isKinematic = true;
        if (cc)
        {
            bool had = cc.enabled; cc.enabled = false;
            // Build capsule in world space using the CC's own transform
            float radius = cc.radius;
            float half = Mathf.Max(0f, cc.height * 0.5f - radius);
            // CharacterController capsule is aligned along the CC transform's up axis
            Vector3 centerWorld = cc.transform.TransformPoint(cc.center);
            Vector3 upWorld = cc.transform.up;
            Vector3 worldBottom = centerWorld - upWorld * half;
            Vector3 worldTop = centerWorld + upWorld * half;
            // Cast slightly downward to settle on ground
            RaycastHit hit;
            if (Physics.CapsuleCast(worldTop + Vector3.up * 0.2f, worldBottom + Vector3.up * 0.2f, Mathf.Max(0.01f, radius * 0.98f), Vector3.down, out hit, 6f, spawnGroundMask, QueryTriggerInteraction.Ignore))
            {
                float clearance = Mathf.Max(spawnGroundClearance, 0.05f) + spawnVerticalLift;
                player.transform.position += Vector3.down * hit.distance + Vector3.up * clearance;
            }
            else if (Physics.Raycast(player.transform.position + Vector3.up * 2f, Vector3.down, out hit, 6f, spawnGroundMask, QueryTriggerInteraction.Ignore))
            {
                player.transform.position = hit.point + Vector3.up * (spawnGroundClearance + spawnVerticalLift);
            }
            cc.enabled = had;
        }
        else
        {
            // Fallback: just raycast to ground
            RaycastHit hit;
            if (Physics.Raycast(player.transform.position + Vector3.up * 2f, Vector3.down, out hit, 6f, spawnGroundMask, QueryTriggerInteraction.Ignore))
            {
                player.transform.position = hit.point + Vector3.up * (spawnGroundClearance + spawnVerticalLift);
            }
        }
        if (rb) rb.isKinematic = false;
        Physics.SyncTransforms();
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

        // Large room spacing check - prevent halls from getting too close to large rooms
        Transform ownerA = GetModuleRootForAnchor(a);
        Transform ownerB = GetModuleRootForAnchor(b);
        
        bool aIsLargeRoom = IsLargeRoom(ownerA);
        bool bIsLargeRoom = IsLargeRoom(ownerB);
        bool aIsHall = IsHallway(ownerA);
        bool bIsHall = IsHallway(ownerB);
        
        // If connecting a hall to a large room, ensure minimum spacing
        if ((aIsLargeRoom && bIsHall) || (bIsLargeRoom && aIsHall))
        {
            float minSpacing = connectionDistance * 0.9f; // Require closer connection for large room-hall pairs
            if (dist < minSpacing)
            {
                Debug.Log($"[Gen] Rejecting hall-large room connection: distance {dist:F2} < min spacing {minSpacing:F2}");
                return false;
            }
        }

        // Facing check: forward vectors should be roughly opposite
        float facingDot = Vector3.Dot(a.forward, -b.forward);
        if (facingDot < connectionFacingDotThreshold) return false;
        return true;
    }
    
    bool IsLargeRoom(Transform module)
    {
        if (!module) return false;
        RoomMeta meta = module.GetComponent<RoomMeta>();
        return meta != null && meta.category == RoomCategory.Room && 
               (module.name.ToLower().Contains("large") || module.name.ToLower().Contains("big"));
    }
    
    bool IsHallway(Transform module)
    {
        if (!module) return false;
        RoomMeta meta = module.GetComponent<RoomMeta>();
        return meta != null && meta.category == RoomCategory.Hallway;
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

    bool IsSmallRoom(GameObject prefab)
    {
        if (!prefab) return false;
        var meta = prefab.GetComponent<RoomMeta>();
        return meta != null && meta.subtype == PlacementCatalog.HallSubtype.SmallRoom;
    }

    void ForceCapAllOpenAnchors()
    {
        if (doorCapPrefab == null)
        {
            Debug.LogWarning("[Gen] Cannot cap anchors: doorCapPrefab is null!");
            return;
        }

        int startingModuleCount = placedModules.Count;
        Debug.Log($"[Gen] Starting door capping process. Current modules: {startingModuleCount}, Module limit was: {maxModules}");
        Debug.Log("[Gen] Door capping IGNORES module limits - will place as many caps as needed!");

        int maxPasses = 5;
        int totalCapsPlaced = 0;
        
        for (int pass = 0; pass < maxPasses; pass++)
        {
            var allAnchors = GatherAllAnchorsInLevel();
            var uncappedAnchors = new List<Transform>();
            
            // Find all truly open anchors with enhanced detection
            for (int i = 0; i < allAnchors.Count; i++)
            {
                var a = allAnchors[i];
                if (!a) continue;
                Transform owner = GetModuleRootForAnchor(a);
                if (owner != null && owner.name.StartsWith("DoorCap_")) continue; // skip caps
                
                bool isOpen = IsAnchorOpen(a, allAnchors);
                bool isConnected = connectedAnchors.Contains(a);
                bool hasPartner = FindPartnerAnchor(a) != null;
                
                if (isOpen)
                {
                    uncappedAnchors.Add(a);
                    Debug.Log($"[Gen] Found uncapped anchor: {a.name} at {a.position} (owner: {(owner ? owner.name : "null")}, connected: {isConnected}, hasPartner: {hasPartner})");
                }
                else if (showDebugInfo)
                {
                    Debug.Log($"[Gen] Skipping anchor {a.name}: connected={isConnected}, hasPartner={hasPartner}");
                }
            }
            
            if (uncappedAnchors.Count == 0)
            {
                Debug.Log($"[Gen] All anchors capped after {pass + 1} passes. Total caps placed: {totalCapsPlaced}");
                break;
            }
            
            Debug.Log($"[Gen] Pass {pass + 1}: Found {uncappedAnchors.Count} uncapped anchors, placing caps...");
            
            // Cap all uncapped anchors in this pass
            foreach (var anchor in uncappedAnchors)
            {
                if (!anchor) continue; // Safety check
                
                Transform owner = GetModuleRootForAnchor(anchor);
                RoomMeta meta = owner ? owner.GetComponent<RoomMeta>() : null;
                bool isRoom = meta != null && (meta.category == RoomCategory.Room || meta.category == RoomCategory.Start);

                // Choose best prefab in priority: room-specific -> hallway-specific -> generic
                GameObject prefab = isRoom && roomDoorCapPrefab != null ? roomDoorCapPrefab
                                    : (!isRoom && hallwayDoorCapPrefab != null ? hallwayDoorCapPrefab : doorCapPrefab);
                if (prefab == null) prefab = doorCapPrefab;

                if (prefab == null)
                {
                    Debug.LogError($"[Gen] Cannot cap anchor {anchor.name} - no door cap prefab available!");
                    continue;
                }

                // Place using anchor alignment so the cap's entry DoorAnchor snaps to the target anchor
                PlaceCapAtAnchor(prefab, anchor, owner != null ? owner : levelRoot);
                Debug.Log($"[Gen] Placed cap on anchor {anchor.name} at {anchor.position}");
                totalCapsPlaced++;
                
                // Mark this anchor as connected to prevent future attempts
                connectedAnchors.Add(anchor);
            }
        }
        
        // Final check
        var finalAnchors = GatherAllAnchorsInLevel();
        int remainingOpen = 0;
        foreach (var a in finalAnchors)
        {
            if (!a) continue;
            Transform owner = GetModuleRootForAnchor(a);
            if (owner != null && owner.name.StartsWith("DoorCap_")) continue;
            if (IsAnchorOpen(a, finalAnchors)) remainingOpen++;
        }
        
        int finalModuleCount = placedModules.Count;
        int capsPlacedBeyondLimit = Mathf.Max(0, finalModuleCount - maxModules);
        
        if (remainingOpen > 0)
        {
            Debug.LogWarning($"[Gen] Warning: {remainingOpen} anchors still uncapped after {maxPasses} passes!");
        }
        else
        {
            Debug.Log($"[Gen] Success: All anchors properly capped! Total caps placed: {totalCapsPlaced}");
        }
        
        Debug.Log($"[Gen] Final module count: {finalModuleCount} (original limit: {maxModules})");
        if (capsPlacedBeyondLimit > 0)
        {
            Debug.Log($"[Gen] Placed {capsPlacedBeyondLimit} door caps BEYOND the original module limit to ensure complete level!");
        }
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
        if (!a) return false; // Invalid anchor is not "open"
        
        // First check if anchor is explicitly marked as connected
        if (connectedAnchors.Contains(a))
        {
            Debug.Log($"[Gen] Anchor {a.name} marked as connected in connectedAnchors - not capping");
            return false;
        }
        
        // Get anchor owner once for all checks
        Transform anchorOwner = GetModuleRootForAnchor(a);
        
        // Check if this anchor already has a door cap placed on it
        if (anchorOwner != null && anchorOwner.name.StartsWith("DoorCap_")) return false;
        
        // Check if there's a door cap as a child of this anchor
        foreach (Transform child in a)
        {
            if (child.name.StartsWith("DoorCap_")) return false;
        }
        
        // More aggressive partner checking - only skip if partner is VERY close
        Transform partner = FindPartnerAnchor(a);
        if (partner != null)
        {
            // Check if partner is on the same floor
            float yDifference = Mathf.Abs(a.position.y - partner.position.y);
            if (yDifference > 2f) // Different floors - don't consider as partners
            {
                Debug.Log($"[Gen] Partner {partner.name} is on different floor (Y diff: {yDifference:F2}) - not considering as connected");
            }
            else
            {
                float distance = Vector3.Distance(a.position, partner.position);
                
                // Check if this anchor belongs to a large room - be more lenient for large rooms
                RoomMeta anchorMeta = anchorOwner ? anchorOwner.GetComponent<RoomMeta>() : null;
                bool isLargeRoom = anchorMeta != null && anchorMeta.category == RoomCategory.Room && 
                                  (anchorOwner.name.ToLower().Contains("large") || anchorOwner.name.ToLower().Contains("big"));
                
                float threshold = isLargeRoom ? connectionDistance * 1.5f : connectionDistance * 0.8f; // Even more lenient for large rooms
                
                // Only consider "connected" if partner is close enough
                if (distance < threshold && AnchorsCouldConnectForCapping(a, partner))
                {
                    Debug.Log($"[Gen] Anchor {a.name} on {(anchorOwner ? anchorOwner.name : "unknown")} has partner {partner.name} at distance {distance:F2} (threshold: {threshold:F2}, isLargeRoom: {isLargeRoom}, same floor)");
                    return false; // Treat as "connected"
                }
            }
        }
        
        // CRITICAL: Check if there's an actual hallway module very close to this anchor
        // This catches cases where connectedAnchors tracking failed
        foreach (Transform module in placedModules)
        {
            if (!module || module == anchorOwner) continue;
            
            RoomMeta moduleMeta = module.GetComponent<RoomMeta>();
            if (moduleMeta == null || moduleMeta.category != RoomCategory.Hallway) continue;
            
            // Check if this hallway module has any anchors very close to our anchor
            var moduleAnchors = new List<Transform>();
            CollectAnchorsIntoList(module, moduleAnchors);
            
            foreach (Transform hallAnchor in moduleAnchors)
            {
                if (!hallAnchor) continue;
                
                // Check Y-difference to ensure we're comparing anchors on the same floor
                float yDifference = Mathf.Abs(a.position.y - hallAnchor.position.y);
                if (yDifference > 2f) // Skip if anchors are on different floors
                {
                    Debug.Log($"[Gen] Skipping {hallAnchor.name} - different floor (Y diff: {yDifference:F2})");
                    continue;
                }
                
                float hallDistance = Vector3.Distance(a.position, hallAnchor.position);
                if (hallDistance < connectionDistance * 1.5f) // Be generous for detection
                {
                    Debug.Log($"[Gen] Anchor {a.name} has nearby hallway {module.name} anchor {hallAnchor.name} at distance {hallDistance:F2} (same floor) - treating as connected");
                    return false; // Don't cap - there's a hallway here
                }
            }
        }
        
        return true; // Anchor is open and should be capped
    }

    void PlaceCapAtAnchor(GameObject capPrefab, Transform targetAnchor, Transform parent)
    {
        if (!capPrefab || !targetAnchor) return;
        var inst = Instantiate(capPrefab, Vector3.zero, Quaternion.identity, parent);
        var entry = GetBestCapEntryAnchor(inst.transform, targetAnchor);
        if (!entry) entry = inst.transform;
        AlignModuleYawOnly(inst.transform, entry, targetAnchor, snapYawTo30);
        // small inset to avoid z-fighting
        inst.transform.position -= targetAnchor.forward * doorCapInset;
        inst.name = $"DoorCap_{(parent && parent.GetComponent<RoomMeta>() ? "Room" : "Hall")}_{targetAnchor.GetInstanceID()}";
        var pivot = inst.transform.Find("CapPivot");
        if (pivot != null)
        {
            Vector3 desired = targetAnchor.position - targetAnchor.forward * doorCapInset;
            Vector3 delta = pivot.position - inst.transform.position;
            inst.transform.position = desired - delta;
        }
        connectedAnchors.Add(targetAnchor);
        
        // IMPORTANT: Add the door cap to placedModules list but DON'T count it against module limits
        // Door caps are essential for completing the level and should never be limited
        placedModules.Add(inst.transform);
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
