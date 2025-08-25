using System.Collections.Generic;
using UnityEngine;
using System.Linq;

/// <summary>
/// Simple Level Generator - Clean, working foundation
/// </summary>
public class SimpleLevelGenerator_New : MonoBehaviour
{
    [Header("Generation Settings")]
    [SerializeField] int maxModules = 50;
    [SerializeField] Transform levelRoot;
    
    [Header("Prefab Lists")]
    [SerializeField] GameObject[] hallwayPrefabs;
    [SerializeField] GameObject[] connectorPrefabs;
    [SerializeField] GameObject[] roomPrefabs;
    
    // Private state
    private List<GameObject> placedModules = new List<GameObject>();
    private List<Transform> availableAnchors = new List<Transform>();
    private List<Transform> usedAnchors = new List<Transform>();
    
    void Start()
    {
        GenerateLevel();
    }
    
    void Update()
    {
        // Auto-regenerate on spacebar for testing
        if (Input.GetKeyDown(KeyCode.Space))
        {
            ClearLevel();
            GenerateLevel();
        }
    }
    
    /// <summary>
    /// Main generation entry point
    /// </summary>
    public void GenerateLevel()
    {
        ClearLevel();
        
        Debug.Log("[Gen] ===== STARTING NEW GENERATION =====");
        
        // Find starting room (any connector)
        GameObject startRoom = FindStartingRoom();
        if (startRoom == null)
        {
            Debug.LogError("[Gen] No starting room found!");
            return;
        }
        
        Debug.Log($"[Gen] Starting room: {startRoom.name}");
        
        // Place starting room at origin
        PlaceModule(startRoom, Vector3.zero, Quaternion.identity);
        Debug.Log("[Gen] Starting room placed");
        
        // Begin generation from anchors
        GenerateFromAnchors();
        
        // Final statistics
        Debug.Log($"[Gen] ===== GENERATION COMPLETE =====");
        Debug.Log($"[Gen] Placed {placedModules.Count} modules");
        Debug.Log($"[Gen] Available anchors remaining: {availableAnchors.Count}");
    }
    
    /// <summary>
    /// Find the starting room
    /// </summary>
    GameObject FindStartingRoom()
    {
        // Try to find any connector prefab
        if (connectorPrefabs.Length > 0)
        {
            Debug.Log($"[Gen] Using connector prefab as starting room");
            return connectorPrefabs[0];
        }
        
        // Fallback to any room prefab
        if (roomPrefabs.Length > 0)
        {
            Debug.Log($"[Gen] Using room prefab as starting room (fallback)");
            return roomPrefabs[0];
        }
        
        Debug.LogError("[Gen] No suitable starting room found!");
        return null;
    }
    
    /// <summary>
    /// Main generation loop - places modules from available anchors
    /// </summary>
    void GenerateFromAnchors()
    {
        int iterations = 0;
        const int maxIterations = 2000; // Higher limit for better expansion
        
        // Initial anchor scan
        UpdateAvailableAnchors();
        Debug.Log($"[Gen] Found {availableAnchors.Count} initial anchors");
        
        // Main generation loop - process multiple anchors per iteration for better branching
        while (availableAnchors.Count > 0 && placedModules.Count < maxModules && iterations < maxIterations)
        {
            iterations++;
            
            // Process multiple anchors per iteration for better branching
            int anchorsToProcess = Mathf.Min(3, availableAnchors.Count); // Process up to 3 anchors per iteration
            List<Transform> anchorsThisIteration = new List<Transform>();
            
            // Select anchors for this iteration
            for (int i = 0; i < anchorsToProcess; i++)
            {
                Transform anchor = SelectBestAnchor();
                if (anchor != null)
                {
                    anchorsThisIteration.Add(anchor);
                    availableAnchors.Remove(anchor);
                }
            }
            
            Debug.Log($"[Gen] Processing {anchorsThisIteration.Count} anchors in iteration {iterations}");
            
            // Process each selected anchor
            foreach (Transform anchor in anchorsThisIteration)
            {
                ProcessAnchor(anchor);
            }
            
            // Update available anchors for next iteration
            UpdateAvailableAnchors();
            
            // Check if we're making progress
            if (iterations % 10 == 0)
            {
                Debug.Log($"[Gen] Progress: {placedModules.Count} modules, {availableAnchors.Count} anchors available");
            }
        }
        
        if (iterations >= maxIterations)
        {
            Debug.LogWarning($"[Gen] Generation stopped after {maxIterations} iterations (safety limit)");
        }
        
        Debug.Log($"[Gen] Generation loop complete. Placed {placedModules.Count} modules, {availableAnchors.Count} anchors remaining");
    }
    
    /// <summary>
    /// Select the best anchor to process next (prioritizes branching potential)
    /// </summary>
    Transform SelectBestAnchor()
    {
        if (availableAnchors.Count == 0) return null;
        
        // Prioritize anchors that can create better branching
        var priorityAnchors = availableAnchors.Where(a => 
        {
            // Check if this anchor is on a connector (better for branching)
            var parentModule = a.parent;
            if (parentModule == null) return false;
            
            // Connectors get priority for better branching
            return parentModule.name.ToLower().Contains("connector");
        }).ToList();
        
        if (priorityAnchors.Count > 0)
        {
            return priorityAnchors[Random.Range(0, priorityAnchors.Count)];
        }
        
        // Fallback to random selection
        return availableAnchors[Random.Range(0, availableAnchors.Count)];
    }
    
    /// <summary>
    /// Process a single anchor - try to place the best module type
    /// </summary>
    void ProcessAnchor(Transform anchor)
    {
        Debug.Log($"[Gen] Processing anchor: {anchor.name} at {anchor.position}");
        
        // Try to place a module
        GameObject module = ChooseModuleType(anchor);
        if (module == null) 
        {
            Debug.Log($"[Gen] No module chosen for anchor {anchor.name}");
            usedAnchors.Add(anchor);
            return;
        }
        
        // Try to place the module
        if (TryPlaceModule(anchor, module))
        {
            Debug.Log($"[Gen] Successfully placed {module.name} on anchor {anchor.name}");
            usedAnchors.Add(anchor);
        }
        else
        {
            Debug.Log($"[Gen] Failed to place {module.name} on anchor {anchor.name}");
            // Don't mark as used if placement failed - let it try again later
            availableAnchors.Add(anchor);
        }
    }
    
    /// <summary>
    /// Choose what type of module to place on this anchor
    /// </summary>
    GameObject ChooseModuleType(Transform anchor)
    {
        // Strategic decision making for better branching
        float decision = Random.Range(0f, 1f);
        
        // 40% chance to prioritize connectors for better branching
        if (decision < 0.4f && connectorPrefabs.Length > 0)
        {
            Debug.Log($"[Gen] Strategically choosing connector for {anchor.name} (branching priority)");
            return connectorPrefabs[Random.Range(0, connectorPrefabs.Length)];
        }
        
        // 30% chance to place a room for variety
        if (decision < 0.7f && roomPrefabs.Length > 0)
        {
            Debug.Log($"[Gen] Choosing room for {anchor.name} (variety)");
            return roomPrefabs[Random.Range(0, roomPrefabs.Length)];
        }
        
        // Default to hallway for better flow
        if (hallwayPrefabs.Length > 0)
        {
            Debug.Log($"[Gen] Choosing hallway for {anchor.name} (flow)");
            return hallwayPrefabs[Random.Range(0, hallwayPrefabs.Length)];
        }
        
        // Fallbacks
        if (connectorPrefabs.Length > 0)
            return connectorPrefabs[Random.Range(0, connectorPrefabs.Length)];
        if (roomPrefabs.Length > 0)
            return roomPrefabs[Random.Range(0, roomPrefabs.Length)];
        
        Debug.LogWarning($"[Gen] No prefabs available for {anchor.name}");
        return null;
    }
    
    /// <summary>
    /// Try to place a module on an anchor
    /// </summary>
    bool TryPlaceModule(Transform anchor, GameObject modulePrefab)
    {
        // Simple positioning - place module at anchor position with offset
        Vector3 position = anchor.position + (anchor.forward * 2.0f);
        Quaternion rotation = anchor.rotation;
        
        // Check for overlaps
        if (WouldCauseOverlap(position, modulePrefab))
        {
            Debug.Log($"[Gen] Module {modulePrefab.name} would cause overlap at {position}");
            return false;
        }
        
        // Place the module
        GameObject placedModule = PlaceModule(modulePrefab, position, rotation);
        if (placedModule == null) return false;
        
        return true;
    }
    
    /// <summary>
    /// Check if placing a module would cause overlap
    /// </summary>
    bool WouldCauseOverlap(Vector3 position, GameObject modulePrefab)
    {
        float minDistance = 1.5f; // Minimum distance between modules
        
        foreach (var placed in placedModules)
        {
            float distance = Vector3.Distance(position, placed.transform.position);
            if (distance < minDistance)
            {
                Debug.Log($"[Gen] {modulePrefab.name} too close to {placed.name} (distance: {distance:F1}, need: {minDistance:F1})");
                return true;
            }
        }
        
        return false;
    }
    
    /// <summary>
    /// Place a module in the world
    /// </summary>
    GameObject PlaceModule(GameObject modulePrefab, Vector3 position, Quaternion rotation)
    {
        GameObject module = Instantiate(modulePrefab, position, rotation, levelRoot);
        placedModules.Add(module);
        
        Debug.Log($"[Gen] Placed {module.name} at {position}");
        return module;
    }
    
    /// <summary>
    /// Update the list of available anchors - SIMPLE VERSION
    /// </summary>
    void UpdateAvailableAnchors()
    {
        availableAnchors.Clear();
        
        // Find all DoorAnchor components from placed modules
        foreach (var module in placedModules)
        {
            var doorAnchors = module.GetComponentsInChildren<DoorAnchor>(true);
            
            Debug.Log($"[Gen] {module.name} has {doorAnchors.Length} DoorAnchor(s)");
            
            foreach (var doorAnchor in doorAnchors)
            {
                // Skip if this anchor is already used
                if (usedAnchors.Contains(doorAnchor.transform))
                {
                    Debug.Log($"[Gen] Skipping used DoorAnchor: {doorAnchor.name} on {module.name}");
                    continue;
                }
                
                // Add ALL unused anchors
                availableAnchors.Add(doorAnchor.transform);
                Debug.Log($"[Gen] Added DoorAnchor: {doorAnchor.name} on {module.name} at {doorAnchor.transform.position}");
            }
        }
        
        Debug.Log($"[Gen] Available anchors: {availableAnchors.Count}");
    }
    
    /// <summary>
    /// Clear the current level
    /// </summary>
    void ClearLevel()
    {
        // Destroy all placed modules
        foreach (var module in placedModules)
        {
            if (module != null)
                DestroyImmediate(module);
        }
        
        placedModules.Clear();
        availableAnchors.Clear();
        usedAnchors.Clear();
        
        Debug.Log("[Gen] Level cleared");
    }
    
    /// <summary>
    /// Debug generation state
    /// </summary>
    [ContextMenu("Debug Generation")]
    void DebugGeneration()
    {
        Debug.Log("[Gen] ===== DEBUG GENERATION =====");
        Debug.Log($"[Gen] Placed modules: {placedModules.Count}");
        Debug.Log($"[Gen] Available anchors: {availableAnchors.Count}");
        Debug.Log($"[Gen] Used anchors: {usedAnchors.Count}");
        
        if (placedModules.Count > 0)
        {
            Debug.Log($"[Gen] First module: {placedModules[0].name} at {placedModules[0].transform.position}");
            
            var doorAnchors = placedModules[0].GetComponentsInChildren<DoorAnchor>();
            Debug.Log($"[Gen] First module has {doorAnchors.Length} DoorAnchor(s)");
        }
    }
}
