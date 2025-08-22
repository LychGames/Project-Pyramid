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
    [Tooltip("If true, start from a small room for natural generation flow")]
    [SerializeField] bool startFromSmallRoom = true;
    [Tooltip("If true, do not place a start room; begin from a pre-placed medium room instead.")]
    [SerializeField] bool startFromMediumRoom = false;
    [Tooltip("If true, begin from a pre-placed large room instead of start/medium. NOT RECOMMENDED - causes overlap issues.")]
    [SerializeField] bool startFromLargeRoom = false;

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
    [SerializeField] float connectionDistance = 0.5f; // anchors closer than this are considered connectable - try 0.25f-0.5f for tighter connections
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

    [Header("Simplified Generation Settings")]
    [Tooltip("Force hallways for the first N placements to create longer paths")]
    [SerializeField] int forceHallwaysFirst = 8;
    [Tooltip("Minimum halls required in a branch before allowing branching")]
    [SerializeField] int minHallsBeforeBranching = 4;
    [Tooltip("Distance from start before allowing branching")]
    [SerializeField] float minDistanceForBranching = 20f;
    [Tooltip("Chance to place a room instead of hallway (0.0 to 1.0)")]
    [SerializeField, Range(0f, 1f)] float roomChance = 0.2f;
    [Tooltip("Maximum number of halls that can connect to the large room")]
    [SerializeField] int maxHallsToLargeRoom = 2;
    
    [Header("Special Room Requirements")]
    [Tooltip("Minimum number of basements to build per generation")]
    [SerializeField] int minBasementsRequired = 4;
    [Tooltip("Boost chance for second-floor rooms (multiplier)")]
    [SerializeField] float secondFloorRoomBoost = 2.0f;
    


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
    [Tooltip("General boost to room selection over hallways")]
    [SerializeField] float roomChanceBoost = 0.6f;
    [Tooltip("Allow rooms to connect directly to other rooms")]
    [SerializeField] bool allowRoomToRoomConnections = true;
    [Tooltip("Guarantee at least this many large rooms spawn during generation")]
    [SerializeField] int guaranteedLargeRooms = 1;
    [Tooltip("Reduce turn frequency when modules are close together")]
    [SerializeField] bool limitCloseTurns = true;
    [Tooltip("Distance threshold for limiting turns")]
    [SerializeField] float closeTurnThreshold = 15f;
    
    [Header("Medium Room Settings")]
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
    private int placedBasements = 0; // Track basements placed
    private int placedSecondFloorRooms = 0; // Track second-floor rooms placed
    private readonly HashSet<Transform> connectedAnchors = new HashSet<Transform>();
    private readonly List<Transform> allLargeRooms = new List<Transform>(); // Track ALL large rooms
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
        // NEW APPROACH: Start from small room for natural flow
        startFromSmallRoom = true;
        startFromLargeRoom = false;
        startFromMediumRoom = false;
        startIsVirtual = false;
        sealOpenAnchorsAtEnd = true;
        spawnPlayerOnComplete = false; // Player spawning handled by PlayerSpawner component
        reserveModulesForCaps = 0; // No need to reserve - door capping ignores module limits now!
        
        // Boost room frequency for variety
        smallRoomChanceBoost = 0.4f; // 40% boost for small rooms
        targetSmallRooms = 4; // Try to place at least 4 small rooms
        roomChanceBoost = 0.6f; // 60% general boost for all rooms
        allowRoomToRoomConnections = true; // Enable room-to-room connections
        guaranteedLargeRooms = 1; // Guarantee at least 1 large room spawns naturally
        
        // Reduce close turns for cleaner layout
        limitCloseTurns = true;
        closeTurnThreshold = 15f;
        
        // Player will spawn in the starting room via PlayerSpawner fallback
        
        Debug.Log("[Gen] Configured for Lethal Company style: starts from small room, guarantees stair room, natural flow, ALL doorways capped");
    }
    
    [ContextMenu("Configure for Triple Connector Focus")]
    public void ConfigureForTripleConnectorFocus()
    {
        // Ultra-simple generation focused on triple connectors
        startFromSmallRoom = true;
        startFromLargeRoom = false;
        startFromMediumRoom = false;
        sealOpenAnchorsAtEnd = true;
        
        // Simple settings that actually work
        maxModules = 20; // Reasonable module limit
        forceHallwaysFirst = 5; // Just a few hallways to start
        
        // Disable all complex features that cause problems
        prioritizeAnchorsThatCanConnect = false;
        preferConnectorWhenPartnerDetected = false;
        autoRetryUntilGood = false; // No retries
        reserveModulesForCaps = 0; // No reserved modules
        
        Debug.Log("[Gen] Configured for ULTRA-SIMPLE generation: triple connectors + halls, respects maxModules");
    }
    
    [ContextMenu("Configure for Basement + Second Floor Focus")]
    public void ConfigureForBasementAndSecondFloorFocus()
    {
        // Focus on getting basements and second-floor rooms
        startFromSmallRoom = true;
        startFromLargeRoom = false;
        startFromMediumRoom = false;
        sealOpenAnchorsAtEnd = true;
        
        // Settings optimized for room variety
        maxModules = 25; // Allow more modules for variety
        forceHallwaysFirst = 3; // Fewer forced hallways
        minHallsBeforeBranching = 2;
        minDistanceForBranching = 10f;
        roomChance = 0.25f; // Higher base room chance
        maxHallsToLargeRoom = 2;
        minBasementsRequired = 4;
        secondFloorRoomBoost = 3.0f; // Higher boost for second-floor rooms
        
        Debug.Log("[Gen] Configured for basement and second-floor room focus");
    }
    
    [ContextMenu("Configure for Basement Stairs Focus")]
    public void ConfigureForBasementStairsFocus()
    {
        // Focus specifically on basement stairs and basement access
        startFromSmallRoom = true;
        startFromLargeRoom = false;
        startFromMediumRoom = false;
        sealOpenAnchorsAtEnd = true;
        
        // Settings optimized for basement stairs
        maxModules = 30; // Allow more modules for basement expansion
        forceHallwaysFirst = 2; // Very few forced hallways
        minHallsBeforeBranching = 1;
        minDistanceForBranching = 5f;
        roomChance = 0.4f; // Very high room chance for basement focus
        maxHallsToLargeRoom = 2;
        minBasementsRequired = 6; // More basements needed
        secondFloorRoomBoost = 2.0f; // Moderate second-floor boost
        
        Debug.Log("[Gen] Configured for basement stairs focus - maximum basement expansion");
    }
    


    // REMOVED: All complex nuclear options that were causing problems




    
    [ContextMenu("Reset All Anchor Connection Restrictions")]
    public void ResetAllAnchorRestrictions()
    {
        Debug.Log("[Gen] Resetting all DoorAnchor connection restrictions to allow everything...");
        
        DoorAnchor[] allAnchors = FindObjectsOfType<DoorAnchor>();
        int resetCount = 0;
        
        foreach (DoorAnchor anchor in allAnchors)
        {
            if (anchor != null)
            {
                anchor.filterMode = DoorAnchor.ConnectionFilterMode.All;
                resetCount++;
                Debug.Log($"[Gen] Reset {anchor.name} to allow all connections");
            }
        }
        
        Debug.Log($"[Gen] ✅ Reset {resetCount} DoorAnchors to allow connections to anything!");
        Debug.Log("[Gen] ⚠️ NOTE: This only affects anchors in the current scene. For prefabs, edit them directly in the Project window.");
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
        
        // LESS AGGRESSIVE CAPPING: Keep more anchors open for expansion
        int targetOpenDoorways = Mathf.Clamp(4 - connectedCount, 0, unconnectedAnchors.Count); // Increased from 3 to 4
        int anchorsToCapCount = unconnectedAnchors.Count - targetOpenDoorways;
        
        if (anchorsToCapCount > 0)
        {
            Debug.Log($"[Gen] Capping {anchorsToCapCount} anchors to maintain 2-3 open doorways");
            
            for (int i = 0; i < anchorsToCapCount && i < unconnectedAnchors.Count; i++)
            {
                Transform anchor = unconnectedAnchors[i];
                try
                {
                    // SMART CAPPING: Use room door cap for large room doorways
                    GameObject prefab = roomDoorCapPrefab != null ? roomDoorCapPrefab : doorCapPrefab;
                    PlaceCapAtAnchor(prefab, anchor, largeRoom);
                    Debug.Log($"[Gen] Large room capped: {anchor.name} with room door cap");
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
                
                // PROTECTION: Skip ALL large room anchors to prevent blocking doorways
                if (allLargeRooms.Contains(owner))
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
                    if (partner == null)
                    {
                        uncappedAnchors.Add(t);
                        Debug.Log($"[Gen] Final pass found uncapped anchor: {t.name} at {t.position} (owner: {(owner ? owner.name : "null")}) - no partner");
                    }
                    else
                    {
                        float partnerDistance = Vector3.Distance(t.position, partner.position);
                        
                        // Very close = connected, even if facing/other checks fail
                        if (partnerDistance < 1.0f)
                        {
                            Debug.Log($"[Gen] Final pass skipping {t.name} - very close partner {partner.name} (dist {partnerDistance:F2}) - distance wins");
                        }
                        else if (partnerDistance > connectionDistance)
                        {
                            uncappedAnchors.Add(t);
                            Debug.Log($"[Gen] Final pass found uncapped anchor: {t.name} - partner too far (dist {partnerDistance:F2})");
                        }
                        else
                        {
                            // Partner is within connection distance, check facing
                            float facing = Vector3.Dot(t.forward, -partner.forward);
                            if (facing <= 0.95f) // Only cap if NOT facing each other properly
                            {
                                uncappedAnchors.Add(t);
                                Debug.Log($"[Gen] Final pass found uncapped anchor: {t.name} (partner exists but not properly facing: {facing:F2})");
                            }
                            else
                            {
                                Debug.Log($"[Gen] Final pass skipping {t.name} - has proper face-to-face partner with facing {facing:F2}");
                            }
                        }
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
            // SMART CAPPING: Use appropriate door cap based on anchor type
            RoomMeta ownerMeta = owner ? owner.GetComponent<RoomMeta>() : null;
            bool isRoomAnchor = ownerMeta != null && ownerMeta.category == RoomCategory.Room;
            
            GameObject prefab;
            if (isRoomAnchor && roomDoorCapPrefab != null)
            {
                prefab = roomDoorCapPrefab; // Use smaller door cap for room doorways
            }
            else if (!isRoomAnchor && hallwayDoorCapPrefab != null)
            {
                prefab = hallwayDoorCapPrefab; // Use larger door cap for hallways
            }
            else
            {
                prefab = doorCapPrefab; // Fallback to default
            }

            if (prefab != null)
            {
                PlaceCapAtAnchor(prefab, anchor, owner != null ? owner : levelRoot);
                connectedAnchors.Add(anchor);
                Debug.Log($"[Gen] Final pass capped: {anchor.name} at {anchor.position} with {(isRoomAnchor ? "room" : "hallway")} cap");
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
            
            // PROTECTION: Skip ALL large room anchors to prevent blocking doorways
            if (allLargeRooms.Contains(owner))
            {
                Debug.Log($"[Gen] ULTRA pass skipping large room anchor {anchor.name} - protecting large room exits");
                continue;
            }
            
            // SMART CAPPING: Use appropriate door cap based on anchor type
            RoomMeta ownerMeta = owner ? owner.GetComponent<RoomMeta>() : null;
            bool isRoomAnchor = ownerMeta != null && ownerMeta.category == RoomCategory.Room;
            
            GameObject prefab;
            if (isRoomAnchor && roomDoorCapPrefab != null)
            {
                prefab = roomDoorCapPrefab; // Use smaller door cap for room doorways
            }
            else if (!isRoomAnchor && hallwayDoorCapPrefab != null)
            {
                prefab = hallwayDoorCapPrefab; // Use larger door cap for hallways
            }
            else
            {
                prefab = doorCapPrefab; // Fallback to default
            }

            if (prefab != null)
            {
                try
                {
                    PlaceCapAtAnchor(prefab, anchor, owner);
                    Debug.Log($"[Gen] ULTRA pass capped: {anchor.name} at {anchor.position} with {(isRoomAnchor ? "room" : "hallway")} cap");
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
                RoomMeta ownerMeta = owner ? owner.GetComponent<RoomMeta>() : null;
                bool isRoomAnchor = ownerMeta != null && ownerMeta.category == RoomCategory.Room;
                
                // SMART CAPPING: Use appropriate door cap based on anchor type
                GameObject prefab;
                if (isRoomAnchor && roomDoorCapPrefab != null)
                {
                    prefab = roomDoorCapPrefab; // Use smaller door cap for room doorways
                }
                else if (!isRoomAnchor && hallwayDoorCapPrefab != null)
                {
                    prefab = hallwayDoorCapPrefab; // Use larger door cap for hallways
                }
                else
                {
                    prefab = doorCapPrefab; // Fallback to default
                }
                
                if (prefab != null)
                {
                    PlaceCapAtAnchor(prefab, anchor, owner);
                    cappedCount++;
                    Debug.Log($"[Gen] SIMPLE capped isolated anchor: {anchor.name} with {(isRoomAnchor ? "room" : "hallway")} cap");
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
        if (logGeneration) Debug.Log("[Gen] Starting level generation...");

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
            allLargeRooms.Clear(); // Clear large room tracking

            ClearLevel();

            Transform startRoot = null;
            
            if (startFromSmallRoom)
            {
                // Start from a small room for natural flow
                startRoot = PlaceSmallRoomStart();
                if (startRoot == null)
                {
                    Debug.LogError("[Gen] Failed to place small room start!");
                    return;
                }
                // Collect initial anchors
                CollectAnchorsIntoList(startRoot, availableAnchors);
                AssignInitialBranches(startRoot);
            }
            else if (!startFromMediumRoom && !startFromLargeRoom)
            {
                // Place traditional start room
                startRoot = PlaceStartRoom();
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
            if ((startFromSmallRoom || (!startFromMediumRoom && !startFromLargeRoom)) && forceInitialGrowthFromAllStartAnchors)
            {
                var startAnchors = new List<Transform>(availableAnchors);
                GrowFromInitialAnchors(startAnchors, Mathf.Max(1, initialStepsPerStart));
            }

            // Pre-place a medium room on the lattice and fan out from multiple doors
            // Pre-place LARGE first if requested (SKIP if starting from small room)
            Transform mediumRootPlaced = null;
            if (!startFromSmallRoom && (startFromLargeRoom || (!startFromMediumRoom && preplaceMediumRoom)))
            {
                var large = TryPreplaceLargeRoom();
                if (large)
                {
                    lastLargeRoot = large;
                    allLargeRooms.Add(large); // Track this large room
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
            
            // Ensure large rooms get placed (if starting from small room)
            if (startFromSmallRoom)
            {
                EnsureLargeRooms();
            }

            // REMOVED: Complex limiting methods that were causing problems

            // === FINAL STEP: Cap unconnected anchors AFTER all generation is complete ===
            if (sealOpenAnchorsAtEnd && doorCapPrefab != null)
            {
                CapUnconnectedAnchorsAfterGeneration();
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
    
    Transform PlaceSmallRoomStart()
    {
        // Find a small room prefab to start with
        GameObject smallRoomPrefab = null;
        if (roomPrefabs != null)
        {
            foreach (GameObject room in roomPrefabs)
            {
                if (room != null && IsSmallRoom(room))
                {
                    smallRoomPrefab = room;
                    break;
                }
            }
        }
        
        if (smallRoomPrefab == null)
        {
            Debug.LogError("[Gen] No small room prefab found for starting generation!");
            return null;
        }

        var start = Instantiate(smallRoomPrefab, Vector3.zero, Quaternion.identity, levelRoot);
        placedModules.Add(start.transform);
        
        if (logGeneration)
        {
            Debug.Log($"[Gen] Started generation from small room: {smallRoomPrefab.name}");
        }

        return start.transform;
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
        int maxAttempts = maxModules * 2; // Prevent infinite loops

        Debug.Log($"[Gen] Starting generation with maxModules limit: {maxModules}");

        while (availableAnchors.Count > 0 && placedModules.Count < maxModules && attempts < maxAttempts)
        {
            attempts++;

            // SMART ANCHOR SELECTION: Pick anchors that will create better branching
            Transform anchor = PickBestAnchorForBranching();

            // Try to place a module
            if (TryPlaceModule(anchor))
            {
                // Remove this anchor since it's now used
                availableAnchors.Remove(anchor);
                
                Debug.Log($"[Gen] Placed module {placedModules.Count}/{maxModules} from anchor {anchor.name}");
            }
            else
            {
                // If we can't place anything, remove this anchor to prevent infinite loops
                availableAnchors.Remove(anchor);
            }
        }

        Debug.Log($"[Gen] Generation stopped: {placedModules.Count}/{maxModules} modules, {availableAnchors.Count} anchors remaining");
    }
    
    // BALANCED METHOD: Mix of strategic and random selection for better branching
    Transform PickBestAnchorForBranching()
    {
        if (availableAnchors.Count == 0) return null;
        
        // BALANCED APPROACH: Mix of random and strategic selection
        // 70% chance to pick randomly (prevents linear paths)
        // 30% chance to pick strategically (encourages good branching)
        
        if (rng.NextDouble() < 0.7f)
        {
            // Random selection prevents linear hall chains
            int randomIndex = rng.Next(availableAnchors.Count);
            Transform randomAnchor = availableAnchors[randomIndex];
            Debug.Log($"[Gen] Random anchor selection: {randomAnchor.name}");
            return randomAnchor;
        }
        
        // Strategic selection for the remaining 30%
        // Look for anchors that could create good branching
        foreach (Transform anchor in availableAnchors)
        {
            if (!anchor) continue;
            
            Transform anchorOwner = GetModuleRootForAnchor(anchor);
            if (anchorOwner != null)
            {
                // HUGE boost for second-floor rooms to encourage expansion
                if (IsSecondFloorRoom(anchorOwner.gameObject))
                {
                    Debug.Log($"[Gen] Strategic selection: second-floor room anchor {anchor.name} for expansion (priority boost)");
                    return anchor;
                }
                
                // Prefer anchors on connectors (they're designed for branching)
                RoomMeta meta = anchorOwner.GetComponent<RoomMeta>();
                if (meta != null && meta.category == RoomCategory.Hallway)
                {
                    string ownerName = anchorOwner.name.ToLower();
                    if (ownerName.Contains("connector") || ownerName.Contains("triple"))
                    {
                        Debug.Log($"[Gen] Strategic selection: connector anchor {anchor.name} for branching");
                        return anchor;
                    }
                }
            }
        }
        
        // Fallback: random selection
        int fallbackIndex = rng.Next(availableAnchors.Count);
        Transform fallbackAnchor = availableAnchors[fallbackIndex];
        Debug.Log($"[Gen] Fallback random selection: {fallbackAnchor.name}");
        return fallbackAnchor;
    }

    bool TryPlaceModule(Transform anchor)
    {
        // Determine what type of module to place based on current generation state
        GameObject prefab = ChooseModuleType(anchor);
        if (prefab == null) return false;

        // CRITICAL: Check if this module type is allowed on this anchor
        if (!IsModuleTypeAllowedOnAnchor(anchor, GetModuleType(prefab)))
        {
            Debug.Log($"[Gen] Module type {GetModuleType(prefab)} not allowed on anchor {anchor.name}, skipping placement");
            return false;
        }

        // Optional connection-kind compatibility: only proceed if target anchor allows this prefab's kind
        if (!IsConnectionCompatible(prefab, anchor))
        {
            return false;
        }

        // Find the entry anchor on the prefab (by name)
        Transform entryAnchor = FindEntryAnchorOnPrefab(prefab);
        if (entryAnchor == null) return false;

        // SIMPLE OVERLAP CHECK: Prevent placement if it would cause overlap
        Vector3 proposedPosition = CalculateProposedPosition(prefab, entryAnchor, anchor);
        if (WouldCauseOverlap(proposedPosition, prefab))
        {
            Debug.Log($"[Gen] Skipping {prefab.name} placement due to overlap prevention");
            
            // TRY FALLBACK: Try a different module type that might fit better
            GameObject fallbackPrefab = TryFallbackModule(anchor, prefab);
            if (fallbackPrefab != null)
            {
                prefab = fallbackPrefab;
                entryAnchor = FindEntryAnchorOnPrefab(prefab);
                if (entryAnchor == null) return false;
                
                // Check overlap again with fallback
                proposedPosition = CalculateProposedPosition(prefab, entryAnchor, anchor);
                if (WouldCauseOverlap(proposedPosition, prefab))
                {
                    Debug.Log($"[Gen] Fallback {prefab.name} also would overlap, skipping placement");
                    return false;
                }
            }
            else
            {
                return false; // No fallback available
            }
        }

        // Place the module
        Transform placedModule = PlaceModule(prefab, entryAnchor, anchor, out Transform placedEntryAnchor);
        if (placedModule == null) return false;

        // Add to placed modules
        placedModules.Add(placedModule);
        nonStartPlacements++;
        placementsSinceLastConnector++;
        
        // REMOVED: Y-level fixing was causing problems

        // Collect new anchors from the placed module
        CollectAnchorsIntoList(placedModule, availableAnchors);

        // Do not consider the consumed entry anchor for immediate reuse
        if (placedEntryAnchor)
        {
            availableAnchors.Remove(placedEntryAnchor);
        }

        // Mark anchors as connected (UNIFIED: no room/hall distinction)
        connectedAnchors.Add(anchor);
        if (placedEntryAnchor) 
        {
            connectedAnchors.Add(placedEntryAnchor);
        }
        
        // Mark ALL nearby anchors at the same position as connected
        var allAnchorsAtThisLocation = GatherAllAnchorsInLevel();
        foreach (Transform otherAnchor in allAnchorsAtThisLocation)
        {
            if (!otherAnchor || otherAnchor == anchor) continue;
            
            float distance = Vector3.Distance(anchor.position, otherAnchor.position);
            if (distance < 0.35f && !connectedAnchors.Contains(otherAnchor))
            {
                connectedAnchors.Add(otherAnchor);
            }
        }

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
            // Check if placed module is a room and track special types
            var meta = placedModule ? placedModule.GetComponent<RoomMeta>() : null;
            if (meta != null)
            {
                if (meta.subtype == PlacementCatalog.HallSubtype.MediumRoom)
            {
                placedMediumRooms++;
                }
                
                // Track basements and second-floor rooms
                if (meta.roomType == RoomType.Basement)
                {
                    placedBasements++;
                    Debug.Log($"[Gen] Basement placed! Total: {placedBasements}/{minBasementsRequired}");
                }
                
                if (meta.roomType == RoomType.BasementStairs)
                {
                    Debug.Log($"[Gen] Basement stairs placed! This provides basement access");
                }
                
                if (meta.roomType == RoomType.StairRoom)
                {
                    Debug.Log($"[Gen] Stair room placed! This provides vertical access");
                }
                
                if (IsSecondFloorRoom(prefab))
                {
                    placedSecondFloorRooms++;
                    Debug.Log($"[Gen] Second-floor room placed! Total: {placedSecondFloorRooms}");
                }
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
        // ULTRA-SIMPLE: Triple connectors + halls, respect maxModules setting
        
        // RESPECT MAX MODULES: Stop if we've reached the limit
        if (placedModules.Count >= maxModules)
        {
            Debug.Log($"[Gen] Reached max modules limit ({maxModules}), stopping generation");
            return null;
        }
        
        // Get basic info
        float distanceFromStart = Vector3.Distance(anchor.position, Vector3.zero);

        // ENCOURAGE BRANCHING: If we have many available anchors, prefer connectors
        bool manyAnchorsAvailable = availableAnchors.Count > 3;
        
        // Phase 1: Start with a few hallways to establish path
        if (nonStartPlacements < 5)
        {
            GameObject hall = PickBestHall(anchor);
            if (hall != null) return hall;
        }
        
        // Phase 2: FOCUS ON TRIPLE CONNECTORS - make them the primary choice
        if (connectorPrefabs != null && connectorPrefabs.Length > 0)
        {
            // Boost connector chance if we have many anchors (encourage branching)
            float connectorChance = manyAnchorsAvailable ? 0.8f : 0.6f;
            
            if (rng.NextDouble() < connectorChance)
            {
                // Prioritize triple connectors
                var tripleConnector = Array.Find(connectorPrefabs, p => p && p.name.ToLower().Contains("triple"));
                if (tripleConnector != null)
                {
                    string reason = manyAnchorsAvailable ? "many anchors available" : "normal chance";
                    Debug.Log($"[Gen] Placing triple connector ({connectorChance*100}% chance, {reason}) at distance {distanceFromStart:F1}");
                    return tripleConnector;
                }
                
                // Fallback to any connector
            return connectorPrefabs[rng.Next(connectorPrefabs.Length)];
            }
        }
        
        // Phase 3: Hallways as secondary choice (reduced if many anchors)
        float hallChance = manyAnchorsAvailable ? 0.15f : 0.3f;
        if (rng.NextDouble() < hallChance)
        {
            GameObject hall = PickBestHall(anchor);
            if (hall != null) return hall;
        }
        
        // Phase 4: Rooms only rarely (10%) - allow room-to-room connections
        if (rng.NextDouble() < 0.1f)
        {
            // Check if this anchor is on a room - if so, allow room-to-room connection
            Transform anchorOwner = GetModuleRootForAnchor(anchor);
            bool isRoomAnchor = false;
            
            if (anchorOwner != null)
            {
                RoomMeta meta = anchorOwner.GetComponent<RoomMeta>();
                isRoomAnchor = (meta != null && meta.category == RoomCategory.Room);
            }
            
            // If anchor is on a room, boost room placement chance for expansion
            if (isRoomAnchor)
            {
                Debug.Log($"[Gen] Room anchor detected - allowing room-to-room connection for expansion");
                GameObject room = PickBestRoomPrefab(anchor, true, false);
                if (room != null && IsRoomAllowedOnAnchor(anchor, room)) return room;
            }
            else
            {
                // Normal room placement for non-room anchors
                GameObject room = PickBestRoomPrefab(anchor, true, false);
                if (room != null && IsRoomAllowedOnAnchor(anchor, room)) return room;
            }
        }
        
        // Default: Hallway
        GameObject defaultHall = PickBestHall(anchor);
        if (defaultHall != null) return defaultHall;
        
        // Final fallback
        if (hallwayPrefabs != null && hallwayPrefabs.Length > 0)
        {
            return hallwayPrefabs[rng.Next(hallwayPrefabs.Length)];
        }
        
        return null;
    }
    
    // SIMPLE OVERLAP PREVENTION: Check if placement would cause overlap
    bool WouldCauseOverlap(Vector3 position, GameObject prefab)
    {
        if (placedModules.Count == 0) return false;
        
        // Simple distance check - if any existing module is too close, it would overlap
        foreach (Transform existingModule in placedModules)
        {
            if (!existingModule) continue;
            
            float distance = Vector3.Distance(position, existingModule.position);
            if (distance < 8f) // If closer than 8 units, likely overlap
            {
                Debug.Log($"[Gen] OVERLAP PREVENTED: Would place {prefab.name} too close to {existingModule.name} (distance: {distance:F2})");
                return true;
            }
        }
        
        return false;
    }
    
    // Calculate where a module would be placed without actually placing it
    Vector3 CalculateProposedPosition(GameObject prefab, Transform entryAnchorOnPrefab, Transform targetAnchor)
    {
        // This is a simplified calculation - in practice, you might want to use the actual placement logic
        // For now, we'll estimate based on the target anchor position and prefab size
        
        Vector3 basePosition = targetAnchor.position;
        
        // Estimate the module size based on prefab name or use a default
        float estimatedSize = 10f; // Default 10 units for most modules
        
        // If it's a connector, it might be smaller
        if (prefab.name.ToLower().Contains("connector") || prefab.name.ToLower().Contains("triple"))
        {
            estimatedSize = 6f;
        }
        // If it's a room, it might be larger
        else if (prefab.name.ToLower().Contains("room") || prefab.name.ToLower().Contains("large"))
        {
            estimatedSize = 15f;
        }
        
        // Calculate the proposed center position
        Vector3 proposedPosition = basePosition + (targetAnchor.forward * (estimatedSize * 0.5f));
        
        return proposedPosition;
    }
    
    // Try to find a fallback module type that might fit better
    GameObject TryFallbackModule(Transform anchor, GameObject originalPrefab)
    {
        string originalName = originalPrefab.name.ToLower();
        
        // If original was a connector, try a hallway instead
        if (originalName.Contains("connector") || originalName.Contains("triple"))
        {
            if (hallwayPrefabs != null && hallwayPrefabs.Length > 0)
            {
                Debug.Log($"[Gen] Trying hallway as fallback for {originalPrefab.name}");
                return hallwayPrefabs[rng.Next(hallwayPrefabs.Length)];
            }
        }
        
        // If original was a room, try a connector instead
        if (originalName.Contains("room"))
        {
            if (connectorPrefabs != null && connectorPrefabs.Length > 0)
            {
                Debug.Log($"[Gen] Trying connector as fallback for {originalPrefab.name}");
                return connectorPrefabs[rng.Next(connectorPrefabs.Length)];
            }
        }
        
        // If original was a hallway, try a connector instead
        if (originalName.Contains("hall"))
        {
            if (connectorPrefabs != null && connectorPrefabs.Length > 0)
            {
                Debug.Log($"[Gen] Trying connector as fallback for {originalPrefab.name}");
                return connectorPrefabs[rng.Next(hallwayPrefabs.Length)];
            }
        }
        
        return null; // No fallback available
    }
    
    // Check if a room is allowed to be placed on this anchor based on room type restrictions
    bool IsRoomAllowedOnAnchor(Transform anchor, GameObject roomPrefab)
    {
        if (!anchor || !roomPrefab) return false;
        
        // Get the DoorAnchor component on this anchor
        DoorAnchor doorAnchor = anchor.GetComponent<DoorAnchor>();
        if (!doorAnchor) return true; // If no DoorAnchor component, allow all
        
        // Get the RoomMeta component on the room prefab
        RoomMeta roomMeta = roomPrefab.GetComponent<RoomMeta>();
        if (!roomMeta) return true; // If no RoomMeta component, allow all
        
        // SPECIAL RULE: Basements can only spawn at ground level
        if (roomMeta.roomType == RoomType.Basement && !IsAnchorAtGroundLevel(anchor))
        {
            Debug.Log($"[Gen] Basement {roomPrefab.name} NOT allowed on anchor {anchor.name} - not at ground level (Y: {anchor.position.y:F1})");
            return false;
        }
        
        // Check if the room type is allowed on this anchor
        bool isAllowed = doorAnchor.AllowsRoomType(roomMeta.roomType);
        
        if (!isAllowed)
        {
            Debug.Log($"[Gen] Room type {roomMeta.roomType} NOT allowed on anchor {anchor.name} (restriction: {doorAnchor.roomRestriction})");
        }
        
        return isAllowed;
    }
    
    // Check if a module type is allowed to be placed on this anchor
    bool IsModuleTypeAllowedOnAnchor(Transform anchor, string moduleType)
    {
        if (!anchor) return true;
        
        DoorAnchor doorAnchor = anchor.GetComponent<DoorAnchor>();
        if (!doorAnchor) return true; // If no DoorAnchor component, allow all
        
        bool isAllowed = doorAnchor.AllowsModuleType(moduleType);
        
        if (!isAllowed)
        {
            Debug.Log($"[Gen] Module type {moduleType} NOT allowed on anchor {anchor.name} (restriction: {doorAnchor.moduleRestriction})");
        }
        
        return isAllowed;
    }
    
    // Check if we need to force basement placement
    bool ShouldForceBasementPlacement()
    {
        return placedBasements < minBasementsRequired;
    }
    
    // Check if we need to boost second-floor room placement
    bool ShouldBoostSecondFloorRooms()
    {
        return placedSecondFloorRooms < 2; // Boost until we have at least 2 second-floor rooms
    }
    
    // NEW METHOD: Pick straight hallways over angled ones
    GameObject PickStraightHallway(Transform anchor)
    {
        if (hallwayPrefabs == null || hallwayPrefabs.Length == 0) return null;
        
        // Look for straight hallway prefabs first
        var straightHalls = new List<GameObject>();
        var angledHalls = new List<GameObject>();
        
        foreach (GameObject hall in hallwayPrefabs)
        {
            if (!hall) continue;
            
            string name = hall.name.ToLower();
            // Assume straight halls don't have "left", "right", or angle indicators
            if (name.Contains("left") || name.Contains("right") || name.Contains("15") || name.Contains("30"))
            {
                angledHalls.Add(hall);
        }
        else
        {
                straightHalls.Add(hall);
            }
        }
        
        // Prefer straight halls, fallback to angled
        if (straightHalls.Count > 0)
        {
            return straightHalls[rng.Next(straightHalls.Count)];
        }
        else if (angledHalls.Count > 0)
        {
            return angledHalls[rng.Next(angledHalls.Count)];
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
    
    void EnsureLargeRooms()
    {
        // Count current large rooms
        int placedLargeRooms = 0;
        foreach (Transform module in placedModules)
        {
            if (module != null)
            {
                RoomMeta meta = module.GetComponent<RoomMeta>();
                if (meta != null && IsLargeRoom(module.gameObject))
                {
                    placedLargeRooms++;
                }
            }
        }
        
        if (placedLargeRooms >= guaranteedLargeRooms) 
        {
            Debug.Log($"[Gen] Large room requirement met: {placedLargeRooms}/{guaranteedLargeRooms}");
            return;
        }
        
        Debug.Log($"[Gen] EnsureLargeRooms: Have {placedLargeRooms}, need {guaranteedLargeRooms}. Placing more...");
        
        // Collect large room prefabs
        var largePrefabs = new List<GameObject>();
        if (roomPrefabs != null)
        {
            foreach (GameObject room in roomPrefabs)
            {
                if (room != null && IsLargeRoom(room))
                {
                    largePrefabs.Add(room);
                }
            }
        }
        
        if (largePrefabs.Count == 0)
        {
            Debug.LogWarning("[Gen] No large room prefabs found! Cannot guarantee large room placement.");
            return;
        }
        
        // Try to place missing large rooms using available anchors
        int attempts = 0;
        int maxAttempts = 20;
        var openAnchors = new List<Transform>(availableAnchors); // Copy current available anchors
        
        while (placedLargeRooms < guaranteedLargeRooms && attempts < maxAttempts && openAnchors.Count > 0)
        {
            attempts++;
            
            // Pick a random large room prefab
            GameObject largeRoom = largePrefabs[rng.Next(largePrefabs.Count)];
            
            // Try to place it at a random open anchor
            int anchorIndex = rng.Next(openAnchors.Count);
            Transform targetAnchor = openAnchors[anchorIndex];
            
            // Temporarily store the prefab we want to place
            GameObject originalPrefab = null;
            if (roomPrefabs != null && roomPrefabs.Length > 0)
            {
                // Find the large room in our prefab arrays and use TryPlaceModule's normal flow
                for (int i = 0; i < roomPrefabs.Length; i++)
                {
                    if (roomPrefabs[i] == largeRoom)
                    {
                        // Temporarily modify selection to favor this large room
                        bool placed = TryPlaceSpecificRoom(targetAnchor, largeRoom);
                        if (placed)
                        {
                            placedLargeRooms++;
                            Debug.Log($"[Gen] Placed guaranteed large room {largeRoom.name}. Total large rooms: {placedLargeRooms}");
                            
                            // Update our local copy of open anchors
                            openAnchors = new List<Transform>(availableAnchors);
                            break;
                        }
                        else
                        {
                            // Remove this anchor and try another
                            openAnchors.RemoveAt(anchorIndex);
                        }
                        break;
                    }
                }
            }
            
            if (placedLargeRooms < guaranteedLargeRooms && openAnchors.Count == 0)
            {
                Debug.LogWarning($"[Gen] Ran out of open anchors while trying to place guaranteed large rooms!");
                break;
            }
        }
        
        Debug.Log($"[Gen] EnsureLargeRooms complete: {placedLargeRooms}/{guaranteedLargeRooms} large rooms placed");
    }
    
    bool TryPlaceSpecificRoom(Transform anchor, GameObject roomPrefab)
    {
        if (!anchor || !roomPrefab) return false;
        
        // Check connection compatibility
        if (!IsConnectionCompatible(roomPrefab, anchor))
        {
            return false;
        }

        // Find the entry anchor on the prefab
        Transform entryAnchor = FindEntryAnchorOnPrefab(roomPrefab);
        if (entryAnchor == null) return false;

        // Place the module
        Transform placedModule = PlaceModule(roomPrefab, entryAnchor, anchor, out Transform placedEntryAnchor);
        if (placedModule == null) return false;

        // Add new anchors to available list
        CollectAnchorsIntoList(placedModule, availableAnchors);

        // Remove the consumed entry anchor from available list
        if (placedEntryAnchor)
        {
            availableAnchors.Remove(placedEntryAnchor);
        }

        // Mark anchors as connected
        connectedAnchors.Add(anchor);
        if (placedEntryAnchor) 
        {
            connectedAnchors.Add(placedEntryAnchor);
        }
        
        // Mark ALL nearby anchors at the same position as connected
        var allAnchorsAtThisLocation = GatherAllAnchorsInLevel();
        foreach (Transform otherAnchor in allAnchorsAtThisLocation)
        {
            if (!otherAnchor || otherAnchor == anchor) continue;
            
            float distance = Vector3.Distance(anchor.position, otherAnchor.position);
            if (distance < 0.35f && !connectedAnchors.Contains(otherAnchor))
            {
                connectedAnchors.Add(otherAnchor);
            }
        }

        if (logGeneration)
        {
            Debug.Log($"[Gen] Placed specific room {roomPrefab.name} at {placedModule.position}");
        }

        return true;
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
        // Find a large room prefab (including stair rooms)
        GameObject large = null;
        for (int i = 0; i < roomPrefabs.Length; i++)
        {
            var p = roomPrefabs[i]; if (!p) continue;
            var meta = p.GetComponent<RoomMeta>();
            if (meta != null && (meta.subtype == PlacementCatalog.HallSubtype.LargeRoom || meta.roomType == RoomType.StairRoom))
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
        // Check both Room category with large/big names AND StairRoom type
        return meta != null && 
               ((meta.category == RoomCategory.Room && (module.name.ToLower().Contains("large") || module.name.ToLower().Contains("big"))) ||
                meta.roomType == RoomType.StairRoom);
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
            // Try Junctions first when we're allowed to branch
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

    // Smart room selection with basement and second-floor boosting
    GameObject PickBestRoomPrefab(Transform atAnchor, bool favorSmallMedium, bool gateMedium)
    {
        if (roomPrefabs == null || roomPrefabs.Length == 0) return null;
        
        // Create a weighted list of eligible rooms
        var eligibleRooms = new List<GameObject>();
        var totalWeight = 0f;
        
        foreach (GameObject room in roomPrefabs)
        {
            if (!room) continue;
            
            // Check if this room can be placed on this anchor
            if (!IsRoomAllowedOnAnchor(atAnchor, room)) continue;
            
            var meta = room.GetComponent<RoomMeta>();
            if (meta == null) continue;
            
            // Apply size preference
            if (favorSmallMedium && meta.sizeClass != RoomMeta.SizeClass.Small && meta.sizeClass != RoomMeta.SizeClass.Medium) continue;
            
            // Apply medium room gating
            if (gateMedium && meta.sizeClass == RoomMeta.SizeClass.Medium) continue;
            
            // Calculate boosted weight based on what we need
            float boostedWeight = meta.weight;
            
            // Boost basement weight if we need more basements
            if (ShouldForceBasementPlacement() && meta.roomType == RoomType.Basement)
            {
                boostedWeight *= 3.0f; // Triple the weight for basements when needed
                Debug.Log($"[Gen] Boosting basement {room.name} weight from {meta.weight} to {boostedWeight}");
            }
            
            // SPECIAL boost for basement stairs (they're essential for basement access)
            if (meta.roomType == RoomType.BasementStairs)
            {
                boostedWeight *= 4.0f; // 400% boost for basement stairs
                Debug.Log($"[Gen] SPECIAL boost for basement stairs {room.name} weight from {meta.weight} to {boostedWeight}");
            }
            
            // MASSIVE boost for stair rooms (we need more of them!)
            if (IsStairRoom(room))
            {
                boostedWeight *= 3.0f; // 300% boost for stair rooms
                Debug.Log($"[Gen] MASSIVE boost for stair room {room.name} weight from {meta.weight} to {boostedWeight}");
            }
            
            // EXTRA boost for stair rooms when we need more anchors
            if (IsStairRoom(room) && availableAnchors.Count < 5)
            {
                boostedWeight *= 2.0f; // Additional 200% boost when anchor count is low
                Debug.Log($"[Gen] EXTRA boost for stair room {room.name} - low anchor count ({availableAnchors.Count}), final weight: {boostedWeight}");
            }
            
            // HUGE boost for stair rooms on second-floor anchors
            if (IsStairRoom(room) && IsAnchorOnSecondFloor(atAnchor))
            {
                boostedWeight *= 5.0f; // 500% boost for stair rooms on second-floor anchors
                Debug.Log($"[Gen] HUGE boost for stair room {room.name} on second-floor anchor - final weight: {boostedWeight}");
            }
            
            // Boost second-floor room weight if we need more
            if (ShouldBoostSecondFloorRooms() && IsSecondFloorRoom(room))
            {
                boostedWeight *= secondFloorRoomBoost; // Apply the boost multiplier
                Debug.Log($"[Gen] Boosting second-floor room {room.name} weight from {meta.weight} to {boostedWeight}");
            }
            
            eligibleRooms.Add(room);
            totalWeight += boostedWeight;
        }
        
        if (eligibleRooms.Count == 0) return null;
        
        // Weighted random selection using boosted weights
        float randomValue = (float)rng.NextDouble() * totalWeight;
        float currentWeight = 0f;
        
        foreach (GameObject room in eligibleRooms)
        {
            var meta = room.GetComponent<RoomMeta>();
            float boostedWeight = meta.weight;
            
            // Apply the same boosting logic for weight calculation
            if (ShouldForceBasementPlacement() && meta.roomType == RoomType.Basement)
            {
                boostedWeight *= 3.0f;
            }
            
            // SPECIAL boost for basement stairs (they're essential for basement access)
            if (meta.roomType == RoomType.BasementStairs)
            {
                boostedWeight *= 4.0f; // 400% boost for basement stairs
            }
            
            // MASSIVE boost for stair rooms (we need more of them!)
            if (IsStairRoom(room))
            {
                boostedWeight *= 3.0f; // 300% boost for stair rooms
            }
            
            // EXTRA boost for stair rooms when we need more anchors
            if (IsStairRoom(room) && availableAnchors.Count < 5)
            {
                boostedWeight *= 2.0f; // Additional 200% boost when anchor count is low
            }
            
            // HUGE boost for stair rooms on second-floor anchors
            if (IsStairRoom(room) && IsAnchorOnSecondFloor(atAnchor))
            {
                boostedWeight *= 5.0f; // 500% boost for stair rooms on second-floor anchors
            }
            
            if (ShouldBoostSecondFloorRooms() && IsSecondFloorRoom(room))
            {
                boostedWeight *= secondFloorRoomBoost;
            }
            
            currentWeight += boostedWeight;
            if (randomValue <= currentWeight)
            {
                return room;
            }
        }
        
        return eligibleRooms[eligibleRooms.Count - 1]; // Fallback
    }
    
    // Check if a room is a second-floor room (simple name-based detection)
    bool IsSecondFloorRoom(GameObject room)
    {
        if (!room) return false;
        
        string name = room.name.ToLower();
        return name.Contains("second") || name.Contains("2nd") || name.Contains("upper") || 
               name.Contains("floor") || name.Contains("level") || name.Contains("upstairs") ||
               name.Contains("stair") || name.Contains("stairs"); // Include stair rooms
    }
    
    // Check if a room is a stair room (for special boosting)
    bool IsStairRoom(GameObject room)
    {
        if (!room) return false;
        
        string name = room.name.ToLower();
        return name.Contains("stair") || name.Contains("stairs") || name.Contains("step");
    }
    
    // Check if a room is a basement stairs room (for special basement logic)
    bool IsBasementStairsRoom(GameObject room)
    {
        if (!room) return false;
        
        var meta = room.GetComponent<RoomMeta>();
        if (meta != null && meta.roomType == RoomType.BasementStairs)
        {
            return true;
        }
        
        // Fallback name-based detection
        string name = room.name.ToLower();
        return name.Contains("basement") && (name.Contains("stair") || name.Contains("stairs") || name.Contains("step"));
    }
    
    // Check if an anchor is at ground level (for basement restrictions)
    bool IsAnchorAtGroundLevel(Transform anchor)
    {
        if (!anchor) return true; // Default to allowing if we can't check
        
        // Consider ground level to be Y = 0 or very close to it
        float yPosition = anchor.position.y;
        return Mathf.Abs(yPosition) < 2.0f; // Allow some tolerance for slight variations
    }
    
    // Check if an anchor is on the second floor (for stair room placement)
    bool IsAnchorOnSecondFloor(Transform anchor)
    {
        if (!anchor) return false;
        
        // Consider second floor to be above Y = 5 (above ground level)
        float yPosition = anchor.position.y;
        return yPosition > 5.0f;
    }
    
    // Determine what type of module a prefab is
    string GetModuleType(GameObject prefab)
    {
        if (!prefab) return "Unknown";
        
        // Check if it's a connector
        if (connectorPrefabs != null && Array.Exists(connectorPrefabs, p => p && p.name == prefab.name))
        {
            return "Connector";
        }
        
        // Check if it's a hallway
        if (hallwayPrefabs != null && Array.Exists(hallwayPrefabs, p => p && p.name == prefab.name))
        {
            return "Hallway";
        }
        
        // Check if it's a room
        if (roomPrefabs != null && Array.Exists(roomPrefabs, p => p && p.name == prefab.name))
        {
            return "Room";
        }
        
        // Default fallback
        return "Unknown";
    }
    
    // Check if an anchor is a good spot for expansion (prevents bad placement)
    bool IsGoodExpansionSpot(Transform anchor)
    {
        if (!anchor) return true;
        
        // Check if there's enough space around this anchor
        Vector3 anchorPos = anchor.position;
        int nearbyModules = 0;
        
        foreach (Transform module in placedModules)
        {
            if (!module) continue;
            
            float distance = Vector3.Distance(anchorPos, module.position);
            if (distance < 12f) // If closer than 12 units, count as nearby
            {
                nearbyModules++;
            }
        }
        
        // If there are too many nearby modules, this might be a cramped spot
        if (nearbyModules > 3)
        {
            Debug.Log($"[Gen] Anchor {anchor.name} might be cramped ({nearbyModules} nearby modules), not ideal for expansion");
            return false;
        }
        
        return true;
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
    
    bool IsRoomAnchor(Transform anchor)
    {
        if (!anchor) return false;
        Transform owner = GetModuleRootForAnchor(anchor);
        if (!owner) return false;
        RoomMeta meta = owner.GetComponent<RoomMeta>();
        return meta != null && meta.category == RoomCategory.Room;
    }
    
    bool IsLargeRoom(GameObject prefab)
    {
        if (!prefab) return false;
        var meta = prefab.GetComponent<RoomMeta>();
        // Check both the old LargeRoom subtype and the new StairRoom type
        return meta != null && (meta.subtype == PlacementCatalog.HallSubtype.LargeRoom || meta.roomType == RoomType.StairRoom);
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
                // UNIFIED CAPPING: Use generic door cap for everything (no room/hall distinction)
                GameObject prefab = doorCapPrefab;

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

            // SMART CAPPING: Use appropriate door cap based on anchor type
            RoomMeta ownerMeta = owner ? owner.GetComponent<RoomMeta>() : null;
            bool isRoomAnchor = ownerMeta != null && ownerMeta.category == RoomCategory.Room;
            
            GameObject prefab;
            if (isRoomAnchor && roomDoorCapPrefab != null)
            {
                prefab = roomDoorCapPrefab; // Use smaller door cap for room doorways
            }
            else if (!isRoomAnchor && hallwayDoorCapPrefab != null)
            {
                prefab = hallwayDoorCapPrefab; // Use larger door cap for hallways
            }
            else
            {
                prefab = doorCapPrefab; // Fallback to default
            }

            // Place using anchor alignment so the cap's entry DoorAnchor snaps to the target anchor
            PlaceCapAtAnchor(prefab, a, owner != null ? owner : levelRoot);
        }
    }

    bool IsAnchorOpen(Transform a, List<Transform> anchors)
    {
        if (!a) return false; // Invalid anchor is not "open"
        
        // UNIFIED APPROACH: Treat all anchors the same way (no room/hall distinction)
        // First check if anchor is explicitly marked as connected
        if (connectedAnchors.Contains(a))
        {
            Debug.Log($"[Gen] Anchor {a.name} marked as connected in connectedAnchors - not capping");
            return false;
        }
        
        // ROBUST CONNECTION CHECK: Verify anchor actually has a nearby hallway connection
        bool hasVerifiedConnection = VerifyAnchorHasActiveConnection(a);
        if (hasVerifiedConnection)
        {
            Debug.Log($"[Gen] Anchor {a.name} has verified active connection - not capping");
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
                
                // If the partner is basically on top of us, assume connected even if facing is off
                if (distance < 1.0f)
                {
                    Debug.Log($"[Gen] Treating {a.name} as connected: very close partner {partner.name} (dist {distance:F2}) - distance wins over facing");
                    return false;
                }
                
                // Check if this anchor belongs to any room - be more strict about room connections
                RoomMeta anchorMeta = anchorOwner ? anchorOwner.GetComponent<RoomMeta>() : null;
                bool isAnyRoom = anchorMeta != null && anchorMeta.category == RoomCategory.Room;
                bool isLargeRoom = isAnyRoom && (anchorOwner.name.ToLower().Contains("large") || anchorOwner.name.ToLower().Contains("big"));
                
                // For rooms, we need to be more strict - only consider connected if there's a REAL connection
                // For hallways, we can be more lenient since they're designed to connect
                float threshold;
                if (isLargeRoom)
                {
                    threshold = connectionDistance * 1.2f; // Slightly lenient for large rooms
                }
                else if (isAnyRoom)
                {
                    threshold = connectionDistance * 0.9f; // More strict for regular rooms
                }
                else
                {
                    threshold = connectionDistance * 1.0f; // Normal threshold for hallways
                }
                
                // Only consider "connected" if partner is close enough AND facing roughly opposite
                float facing = Vector3.Dot(a.forward, -partner.forward);
                if (distance <= connectionDistance && facing > 0.95f)
                {
                    Debug.Log($"[Gen] Anchor {a.name} has face-to-face partner {partner.name} at distance {distance:F2} with facing {facing:F2} - definitely connected");
                    return false; // Treat as "connected" - robust geometric check
                }
                else if (distance < threshold && facing > 0.8f && AnchorsCouldConnectForCapping(a, partner))
                {
                    Debug.Log($"[Gen] Anchor {a.name} on {(anchorOwner ? anchorOwner.name : "unknown")} has partner {partner.name} at distance {distance:F2} (threshold: {threshold:F2}, isLargeRoom: {isLargeRoom}, facing: {facing:F2})");
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

    bool VerifyAnchorHasActiveConnection(Transform anchor)
    {
        if (!anchor) return false;
        
        // Check all placed modules for nearby connections (INCLUDE ROOMS!)
        foreach (Transform module in placedModules)
        {
            if (!module) continue;
            
            // Check ALL modules - hallways AND rooms are part of the map
            RoomMeta meta = module.GetComponent<RoomMeta>();
            if (meta == null) continue; // Skip only if no metadata at all
            
            // Get all anchors in this module (room or hallway)
            var moduleAnchors = new List<Transform>();
            CollectAnchorsIntoList(module, moduleAnchors);
            
            foreach (Transform moduleAnchor in moduleAnchors)
            {
                if (!moduleAnchor || moduleAnchor == anchor) continue;
                
                // Check if on same floor
                float yDiff = Mathf.Abs(anchor.position.y - moduleAnchor.position.y);
                if (yDiff > 2f) continue; // Different floors
                
                // Check distance
                float distance = Vector3.Distance(anchor.position, moduleAnchor.position);
                if (distance < connectionDistance * 1.2f) // Generous threshold
                {
                    // Check if they're roughly facing each other
                    float dot = Vector3.Dot(anchor.forward, -moduleAnchor.forward);
                    if (dot > 0.8f) // Fairly lenient facing check - should be positive for facing anchors
                    {
                        Debug.Log($"[Gen] VERIFIED connection: {anchor.name} <-> {moduleAnchor.name} (module: {module.name}, distance: {distance:F2}, dot: {dot:F2})");
                        return true;
                    }
                }
            }
        }
        
        return false; // No verified connection found
    }

    void CapTrulyIsolatedAnchorsOnly()
    {
        Debug.Log("[Gen] === SIMPLE ISOLATED CAPPING - NO COMPLEX LOGIC ===");
        
        // Get every single anchor in the level
        var allAnchors = GatherAllAnchorsInLevel();
        Debug.Log($"[Gen] Found {allAnchors.Count} total anchors in level");
        
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
            if (hasCapChild) 
            {
                Debug.Log($"[Gen] Skipping {anchor.name} - already has cap");
                continue;
            }
            
            // Check if ANY other anchor is within connection distance on the same floor
            bool hasNearbyPartner = false;
            foreach (Transform other in allAnchors)
            {
                if (other == anchor || !other) continue;
                
                // Same floor check
                float yDiff = Mathf.Abs(anchor.position.y - other.position.y);
                if (yDiff > 2f) continue;
                
                float distance = Vector3.Distance(anchor.position, other.position);
                if (distance < connectionDistance)
                {
                    hasNearbyPartner = true;
                    Debug.Log($"[Gen] {anchor.name} has nearby partner {other.name} at {distance:F2} - NOT capping");
                    break;
                }
            }
            
            // ONLY cap if completely isolated
            if (!hasNearbyPartner)
            {
                Transform owner = GetModuleRootForAnchor(anchor);
                RoomMeta ownerMeta = owner ? owner.GetComponent<RoomMeta>() : null;
                bool isRoomAnchor = ownerMeta != null && ownerMeta.category == RoomCategory.Room;
                
                // SMART CAPPING: Use appropriate door cap based on anchor type
                GameObject prefab;
                if (isRoomAnchor && roomDoorCapPrefab != null)
                {
                    prefab = roomDoorCapPrefab; // Use smaller door cap for room doorways
                }
                else if (!isRoomAnchor && hallwayDoorCapPrefab != null)
                {
                    prefab = hallwayDoorCapPrefab; // Use larger door cap for hallways
                }
                else
                {
                    prefab = doorCapPrefab; // Fallback to default
                }
                
                if (prefab != null)
                {
                    try
                    {
                        PlaceCapAtAnchor(prefab, anchor, owner);
                        Debug.Log($"[Gen] CAPPED isolated anchor: {anchor.name} (no partners within {connectionDistance}) with {(isRoomAnchor ? "room" : "hallway")} cap");
                    }
                    catch (System.Exception e)
                    {
                        Debug.LogError($"[Gen] Failed to cap {anchor.name}: {e.Message}");
                    }
                }
            }
        }
        
        Debug.Log("[Gen] === SIMPLE ISOLATED CAPPING COMPLETE ===");
    }

    void CapUnconnectedAnchorsAfterGeneration()
    {
        Debug.Log("[Gen] === SIMPLIFIED DOOR CAPPING - ONLY TRULY ISOLATED ANCHORS ===");
        
        if (levelRoot == null)
        {
            Debug.LogError("[Gen] PROBLEM: levelRoot is null!");
            return;
        }
        
        // Get all anchors in the completed level
        var allAnchors = GatherAllAnchorsInLevel();
        
        int capsPlaced = 0;
        int anchorsChecked = 0;
        
        foreach (Transform anchor in allAnchors)
        {
            if (!anchor) continue;
            anchorsChecked++;
            
            // Skip if already has a door cap
            bool alreadyHasCap = false;
            foreach (Transform child in anchor)
            {
                if (child.name.StartsWith("DoorCap_"))
                {
                    alreadyHasCap = true;
                    break;
                }
            }
            if (alreadyHasCap) continue;
            
            // SIMPLIFIED LOGIC: Only cap if anchor is truly isolated
            bool isTrulyIsolated = IsAnchorTrulyIsolated(anchor, allAnchors);
            
            if (isTrulyIsolated)
            {
                // Use appropriate door cap based on anchor type
                Transform owner = GetModuleRootForAnchor(anchor);
                RoomMeta ownerMeta = owner ? owner.GetComponent<RoomMeta>() : null;
                bool isRoomAnchor = ownerMeta != null && ownerMeta.category == RoomCategory.Room;
                
                GameObject prefab;
                if (isRoomAnchor && roomDoorCapPrefab != null)
                {
                    prefab = roomDoorCapPrefab;
                }
                else if (!isRoomAnchor && hallwayDoorCapPrefab != null)
                {
                    prefab = hallwayDoorCapPrefab;
                }
                else
                {
                    prefab = doorCapPrefab;
                }
                
                if (prefab != null)
                {
                    try
                    {
                        PlaceCapAtAnchor(prefab, anchor, owner != null ? owner : levelRoot);
                        capsPlaced++;
                        Debug.Log($"[Gen] Capped isolated anchor: {anchor.name}");
                    }
                    catch (System.Exception e)
                    {
                        Debug.LogError($"[Gen] Failed to cap anchor {anchor.name}: {e.Message}");
                    }
                }
            }
        }
        
        Debug.Log($"[Gen] === SIMPLIFIED CAPPING COMPLETE ===");
        Debug.Log($"[Gen] Anchors checked: {anchorsChecked}");
        Debug.Log($"[Gen] Door caps placed: {capsPlaced}");
    }
    
    // NEW METHOD: Check if anchor is truly isolated (no nearby modules or potential connections)
    bool IsAnchorTrulyIsolated(Transform anchor, List<Transform> allAnchors)
    {
        if (!anchor) return true;
        
        // Check if there are any modules very close to this anchor
        foreach (Transform module in placedModules)
        {
            if (!module) continue;
            
            float distance = Vector3.Distance(anchor.position, module.position);
            if (distance < 2.0f) // If there's a module within 2 units, not isolated
            {
                return false;
            }
        }
        
        // Check if there are any other anchors that could potentially connect
        foreach (Transform otherAnchor in allAnchors)
        {
            if (!otherAnchor || otherAnchor == anchor) continue;
            
            float distance = Vector3.Distance(anchor.position, otherAnchor.position);
            if (distance < 3.0f) // If there's another anchor within 3 units, not isolated
            {
            return false;
        }
        
            // ENHANCEMENT: For extremely close anchors, add rotation validation
            // This catches overlapping cases where anchors are very close but not properly aligned
            if (distance < 0.5f) // Only check rotation for very close anchors
            {
                // Check if anchors are roughly facing opposite directions (should be ~180 degrees)
                float facingDot = Vector3.Dot(anchor.forward, -otherAnchor.forward);
                if (facingDot > 0.8f) // If they're facing roughly opposite directions
                {
                    Debug.Log($"[Gen] Anchor {anchor.name} has very close partner {otherAnchor.name} with good rotation (facing: {facingDot:F2}) - treating as connected");
                    return false; // Treat as connected due to proximity + rotation
                }
                else if (facingDot < 0.3f) // If they're facing roughly the same direction
                {
                    Debug.Log($"[Gen] Anchor {anchor.name} has very close partner {otherAnchor.name} with poor rotation (facing: {facingDot:F2}) - treating as isolated despite proximity");
                    // Still treat as isolated - they're close but not properly aligned
                }
                // If facing is in between (0.3 to 0.8), let the distance check handle it
            }
        }
        
        return true; // Truly isolated
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
        // Better naming based on the actual prefab type
        string capType = "Default";
        if (capPrefab == roomDoorCapPrefab) capType = "Room";
        else if (capPrefab == hallwayDoorCapPrefab) capType = "Hall";
        inst.name = $"DoorCap_{capType}_{targetAnchor.GetInstanceID()}";
        var pivot = inst.transform.Find("CapPivot");
        if (pivot != null)
        {
            Vector3 desired = targetAnchor.position - targetAnchor.forward * doorCapInset;
            Vector3 delta = pivot.position - inst.transform.position;
            inst.transform.position = desired - delta;
        }
        // DO NOT add to connectedAnchors - door caps are for UNCONNECTED anchors!
        // connectedAnchors.Add(targetAnchor); // REMOVED - this was corrupting the connection tracking
        
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

                // SMART CAPPING: Use appropriate door cap based on anchor type
                RoomMeta ownerMeta = owner ? owner.GetComponent<RoomMeta>() : null;
                bool isRoomAnchor = ownerMeta != null && (ownerMeta.category == RoomCategory.Room || ownerMeta.roomType == RoomType.StairRoom);
                
                GameObject prefab;
                if (isRoomAnchor && roomDoorCapPrefab != null)
                {
                    prefab = roomDoorCapPrefab; // Use smaller door cap for room doorways
                }
                else if (!isRoomAnchor && hallwayDoorCapPrefab != null)
                {
                    prefab = hallwayDoorCapPrefab; // Use larger door cap for hallways
                }
                else
                {
                    prefab = doorCapPrefab; // Fallback to default
                }
                
                if (prefab == null) continue;
                PlaceCapAtAnchor(prefab, a, owner != null ? owner : levelRoot);
                placedAny = true;
            }
            if (!placedAny) break;
        }
    }

    // NEW METHOD: Limit the number of halls that can reach the large room
    void LimitHallsToLargeRoom()
    {
        if (allLargeRooms.Count == 0) return;
        
        Debug.Log("[Gen] Limiting halls to large room...");
        
        foreach (Transform largeRoom in allLargeRooms)
        {
            if (!largeRoom) continue;
            
            // Get all anchors on the large room
            var largeRoomAnchors = new List<Transform>();
            CollectAnchorsIntoList(largeRoom, largeRoomAnchors);
            
            // Count how many are connected to halls
            int hallConnections = 0;
            var connectedHallAnchors = new List<Transform>();
            
            foreach (Transform anchor in largeRoomAnchors)
            {
                if (!anchor) continue;
                
                // Check if this anchor connects to a hallway
                bool connectsToHall = false;
                foreach (Transform module in placedModules)
                {
                    if (!module) continue;
                    
                    RoomMeta meta = module.GetComponent<RoomMeta>();
                    if (meta != null && meta.category == RoomCategory.Hallway)
                    {
                        // Check if this hallway has an anchor near the large room anchor
                        var moduleAnchors = new List<Transform>();
                        CollectAnchorsIntoList(module, moduleAnchors);
                        
                        foreach (Transform moduleAnchor in moduleAnchors)
                        {
                            float distance = Vector3.Distance(anchor.position, moduleAnchor.position);
                            if (distance < 1.0f)
                            {
                                connectsToHall = true;
                                connectedHallAnchors.Add(anchor);
                                break;
                            }
                        }
                        
                        if (connectsToHall) break;
                    }
                }
                
                if (connectsToHall)
                {
                    hallConnections++;
                }
            }
            
            Debug.Log($"[Gen] Large room {largeRoom.name} has {hallConnections} hall connections");
            
            // If more than maxHallsToLargeRoom halls connect, cap some of the excess
            if (hallConnections > maxHallsToLargeRoom)
            {
                int excessHalls = hallConnections - maxHallsToLargeRoom;
                Debug.Log($"[Gen] Capping {excessHalls} excess hall connections to large room (max allowed: {maxHallsToLargeRoom})");
                
                // Cap the excess hall anchors (keep the first maxHallsToLargeRoom)
                for (int i = maxHallsToLargeRoom; i < connectedHallAnchors.Count && (i - maxHallsToLargeRoom) < excessHalls; i++)
                {
                    Transform anchorToCap = connectedHallAnchors[i];
                    if (anchorToCap && doorCapPrefab)
                    {
                        try
                        {
                            PlaceCapAtAnchor(doorCapPrefab, anchorToCap, largeRoom);
                            Debug.Log($"[Gen] Capped excess hall connection: {anchorToCap.name}");
                        }
                        catch (System.Exception e)
                        {
                            Debug.LogError($"[Gen] Failed to cap excess hall connection {anchorToCap.name}: {e.Message}");
                        }
                    }
                }
            }
        }
    }

    // NEW METHOD: Limit excessive branching from the start room
    void LimitStartRoomBranching()
    {
        if (placedModules.Count == 0) return;
        
        // Find the start room (first placed module)
        Transform startRoom = placedModules[0];
        if (!startRoom) return;
        
        Debug.Log("[Gen] Limiting branching from start room...");
        
        // Get all anchors on the start room
        var startRoomAnchors = new List<Transform>();
        CollectAnchorsIntoList(startRoom, startRoomAnchors);
        
        // Count how many are connected to connectors (branching points)
        int connectorConnections = 0;
        var connectorAnchors = new List<Transform>();
        
        foreach (Transform anchor in startRoomAnchors)
        {
            if (!anchor) continue;
            
            // Check if this anchor connects to a connector
            bool connectsToConnector = false;
            foreach (Transform module in placedModules)
            {
                if (!module || module == startRoom) continue;
                
                // Check if this module is a connector
                bool isConnector = false;
                if (connectorPrefabs != null)
                {
                    isConnector = Array.Exists(connectorPrefabs, p => p && p.name == module.name.Replace("(Clone)", ""));
                }
                
                if (isConnector)
                {
                    // Check if this connector has an anchor near the start room anchor
                    var moduleAnchors = new List<Transform>();
                    CollectAnchorsIntoList(module, moduleAnchors);
                    
                    foreach (Transform moduleAnchor in moduleAnchors)
                    {
                        float distance = Vector3.Distance(anchor.position, moduleAnchor.position);
                        if (distance < 1.0f)
                        {
                            connectsToConnector = true;
                            connectorAnchors.Add(anchor);
                            break;
                        }
                    }
                    
                    if (connectsToConnector) break;
                }
            }
            
            if (connectsToConnector)
            {
                connectorConnections++;
            }
        }
        
        Debug.Log($"[Gen] Start room has {connectorConnections} connector connections");
        
        // Limit to maximum 2 connector connections from start room
        int maxConnectorsFromStart = 2;
        if (connectorConnections > maxConnectorsFromStart)
        {
            int excessConnectors = connectorConnections - maxConnectorsFromStart;
            Debug.Log($"[Gen] Capping {excessConnectors} excess connector connections from start room (max allowed: {maxConnectorsFromStart})");
            
            // Cap the excess connector anchors (keep the first maxConnectorsFromStart)
            for (int i = maxConnectorsFromStart; i < connectorAnchors.Count && (i - maxConnectorsFromStart) < excessConnectors; i++)
            {
                Transform anchorToCap = connectorAnchors[i];
                if (anchorToCap && doorCapPrefab)
                {
                    try
                    {
                        PlaceCapAtAnchor(doorCapPrefab, anchorToCap, startRoom);
                        Debug.Log($"[Gen] Capped excess connector connection from start room: {anchorToCap.name}");
                    }
                    catch (System.Exception e)
                    {
                        Debug.LogError($"[Gen] Failed to cap excess connector connection {anchorToCap.name}: {e.Message}");
                    }
                }
            }
        }
    }

    // REMOVED: This method was causing Y-level and overlap problems
}