using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public class SimpleLevelGenerator_Foundation : MonoBehaviour
{
    [Header("Seed")]
    [SerializeField] bool randomizeSeed = true;
    [SerializeField] int seed = 12345;

    [Header("Limits")]
    [SerializeField] int maxModules = 60;
    [SerializeField] int maxAttemptsPerAnchor = 10;

    [Header("Scene Hooks")]
    [SerializeField] Transform levelRoot;

    [Header("Prefabs")]
    [SerializeField] GameObject startPrefab;          // e.g., your 3-way hub
    [SerializeField] GameObject[] modulePrefabs;      // halls, rooms, connectors
    [SerializeField] GameObject doorBlockerPrefab;    // simple cap for leftovers

    [Header("Spacing / Overlap")]
    [Tooltip("Quick-and-dirty spacing safeguard (meters)")]
    [SerializeField] float minCenterDistance = 3.0f; // Increased from 1.0f to prevent overlaps
    
    [Header("Door Cap Offsets (local space of the anchor)")]
    [SerializeField] Vector3 doorCapLocalPositionOffset = Vector3.zero;
    [SerializeField] Vector3 doorCapLocalEulerOffset = Vector3.zero; // e.g., (0,180,0) if your cap faces backward

    [Header("Debug")]
    [SerializeField] bool disableBoundsOverlapCheck = false;

    System.Random rng;
    readonly List<GameObject> placed = new();
    readonly HashSet<Transform> usedAnchors = new();
    readonly Queue<Transform> openAnchors = new();

    void Start()
    {
        if (randomizeSeed) seed = Random.Range(0, int.MaxValue);
        rng = new System.Random(seed);
        Generate();
    }

    [ContextMenu("Regenerate")]
    public void Generate()
    {
        ClearAll();

        if (levelRoot == null)
        {
            levelRoot = new GameObject("LevelRoot").transform;
            levelRoot.SetParent(transform, false);
        }

        // 1) Place start at origin
        var start = PlaceModule(startPrefab, Vector3.zero, Quaternion.identity);
        if (start == null) { Debug.LogError("[Gen] No start prefab assigned."); return; }

        // collect its anchors
        EnqueueFreeAnchors(start);
        Debug.Log($"[Gen] Collected {openAnchors.Count} anchors from starting module");

        // 2) Main growth loop
        Debug.Log($"[Gen] Starting main growth loop with {openAnchors.Count} open anchors");
        
        while (openAnchors.Count > 0 && placed.Count < maxModules)
        {
            var targetAnchor = DequeueValidAnchor();
            if (targetAnchor == null) 
            {
                Debug.Log("[Gen] No valid anchor found, stopping generation");
                break;
            }

            Debug.Log($"[Gen] Working with anchor: {targetAnchor.name} at {targetAnchor.position}");
            bool placedSomething = false;

            for (int attempt = 0; attempt < maxAttemptsPerAnchor; attempt++)
            {
                var prefab = PickCompatiblePrefabFor(targetAnchor);
                if (prefab == null) 
                {
                    Debug.Log($"[Gen] No compatible prefab found for {targetAnchor.name}");
                    break; // nothing compatible -> bail
                }

                // Try align one of prefab's anchors *facing back* into targetAnchor
                if (TryPlaceAligned(prefab, targetAnchor, out GameObject newModule, out Transform matchedOnNew))
                {
                    placedSomething = true;
                    Debug.Log($"[Gen] Successfully placed {newModule.name} on {targetAnchor.name}");

                    // Mark the connected anchors as used
                    usedAnchors.Add(targetAnchor);
                    if (matchedOnNew != null) usedAnchors.Add(matchedOnNew);

                    // Add new module's remaining free anchors
                    EnqueueFreeAnchors(newModule, skip: matchedOnNew);
                    Debug.Log($"[Gen] Added {newModule.name}'s free anchors. Total open anchors: {openAnchors.Count}");
                    break;
                }
                else
                {
                    Debug.Log($"[Gen] Attempt {attempt + 1} failed to place {prefab.name} on {targetAnchor.name}");
                }
            }

            if (!placedSomething)
            {
                Debug.Log($"[Gen] Failed to place anything on {targetAnchor.name}, sealing it");
                // 3) If we failed to expand here, seal it cleanly
                Seal(targetAnchor);
                usedAnchors.Add(targetAnchor);
            }
        }

        Debug.Log($"[Gen] Done. Placed {placed.Count} modules. Unused anchors sealed where needed.");
    }

    // ---------------- core helpers ----------------

    Transform DequeueValidAnchor()
    {
        while (openAnchors.Count > 0)
        {
            var a = openAnchors.Dequeue();
            if (a == null) continue;
            if (usedAnchors.Contains(a)) continue;
            if (a.GetComponent<DoorAnchor>() == null) continue;
            return a;
        }
        return null;
    }

    GameObject PickCompatiblePrefabFor(Transform targetAnchor)
    {
        if (modulePrefabs == null || modulePrefabs.Length == 0) return null;

        // SIMPLIFIED: Just pick any prefab that has DoorAnchors
        var validPrefabs = modulePrefabs
            .Where(p => p && p.GetComponentInChildren<DoorAnchor>(true) != null)
            .ToList();

        if (validPrefabs.Count == 0) 
        {
            Debug.LogWarning("[Gen] No valid prefabs found (no DoorAnchors)");
            return null;
        }

        var chosen = validPrefabs[rng.Next(validPrefabs.Count)];
        Debug.Log($"[Gen] Chose prefab: {chosen.name} for anchor {targetAnchor.name}");
        return chosen;
    }


    bool TryPlaceAligned(GameObject prefab, Transform targetAnchor, out GameObject placedModule, out Transform matchedOnNew)
    {
        placedModule = null;
        matchedOnNew = null;

        // Find the entry anchor on the prefab (the one that will connect to targetAnchor)
        Transform entryAnchor = FindEntryAnchorOnPrefab(prefab);
        if (entryAnchor == null)
        {
            Debug.LogWarning($"[Gen] No entry anchor found on {prefab.name}");
            return false;
        }

        // Instantiate the prefab
        var temp = Instantiate(prefab);
        temp.name = prefab.name + "_TEMP";
        temp.SetActive(false); // avoid flashes/side effects

        // Find the corresponding entry anchor on the instance
        Transform entryAnchorOnInstance = FindCorrespondingTransform(temp.transform, entryAnchor);
        if (entryAnchorOnInstance == null)
        {
            // Fallback: search by name
            entryAnchorOnInstance = FindEntryAnchorOnPrefab(temp);
        }

        if (entryAnchorOnInstance == null)
        {
            Debug.LogWarning($"[Gen] Could not find entry anchor on instance of {prefab.name}");
            DestroyImmediate(temp);
            return false;
        }

        // PROPER ALIGNMENT: Align the entry anchor to face the target anchor
        AlignModuleToAnchor(temp.transform, entryAnchorOnInstance, targetAnchor);

        // Check for overlaps
        if (WouldOverlap(temp, targetAnchor))
        {
            DestroyImmediate(temp);
            return false;
        }

        // Success! Keep the module
        temp.name = prefab.name;
        temp.SetActive(true);
        placed.Add(temp);
        placedModule = temp;
        matchedOnNew = entryAnchorOnInstance;
        
        Debug.Log($"[Gen] Successfully placed {prefab.name} at {temp.transform.position}");
        return true;
    }

    /// <summary>
    /// Find the entry anchor on a prefab (first transform with "Anchor" in the name)
    /// </summary>
    Transform FindEntryAnchorOnPrefab(GameObject prefab)
    {
        if (!prefab) return null;
        
        var anchors = prefab.GetComponentsInChildren<Transform>(true);
        foreach (var anchor in anchors)
        {
            if (anchor.name.ToLower().IndexOf("anchor") >= 0)
            {
                return anchor;
            }
        }
        
        // Fallback: first DoorAnchor component
        var doorAnchors = prefab.GetComponentsInChildren<DoorAnchor>(true);
        if (doorAnchors.Length > 0)
        {
            return doorAnchors[0].transform;
        }
        
        return null;
    }

    /// <summary>
    /// Find the corresponding transform on an instance by rebuilding the path
    /// </summary>
    Transform FindCorrespondingTransform(Transform instanceRoot, Transform prefabTransform)
    {
        if (!instanceRoot || !prefabTransform) return null;

        // Build path from prefabTransform up to its root
        var path = new List<string>();
        var t = prefabTransform;
        while (t.parent != null)
        {
            path.Add(t.name);
            t = t.parent;
        }

        // Navigate down the path on the instance
        t = instanceRoot;
        for (int i = path.Count - 1; i >= 0; i--)
        {
            t = t.Find(path[i]);
            if (t == null) return null;
        }

        return t;
    }

    /// <summary>
    /// Align a module so its entry anchor connects properly to the target anchor
    /// </summary>
    void AlignModuleToAnchor(Transform moduleRoot, Transform entryAnchor, Transform targetAnchor)
    {
        if (!entryAnchor) return;

        // 1. Rotate the module so entry anchor faces the target anchor
        Vector3 entryForward = entryAnchor.forward;
        Vector3 targetForward = -targetAnchor.forward; // Opposite direction
        
        // Calculate rotation to align entry forward with target forward
        Quaternion rotation = Quaternion.FromToRotation(entryForward, targetForward);
        moduleRoot.rotation = rotation * moduleRoot.rotation;

        // 2. Position the module so the entry anchor touches the target anchor
        Vector3 offset = targetAnchor.position - entryAnchor.position;
        moduleRoot.position += offset;
        
        // 3. Add a small offset to prevent exact overlap (simple but effective)
        Vector3 spacingOffset = targetAnchor.forward * 0.5f;
        moduleRoot.position += spacingOffset;
        
        Debug.Log($"[Gen] Aligned {moduleRoot.name}: entry at {entryAnchor.position}, target at {targetAnchor.position}");
    }

    bool WouldOverlap(GameObject candidate, Transform targetAnchor)
    {
        // SIMPLIFIED: Just check center distance, ignore complex bounds
        foreach (var p in placed)
        {
            if (Vector3.Distance(candidate.transform.position, p.transform.position) < minCenterDistance)
            {
                Debug.Log($"[Gen] {candidate.name} too close to {p.name} (distance: {Vector3.Distance(candidate.transform.position, p.transform.position):F2}, need: {minCenterDistance:F2})");
                return true;
            }
        }

        // Skip complex bounds checking for now
        return false;
    }

    static Bounds GetWorldBounds(GameObject go)
    {
        var rends = go.GetComponentsInChildren<Renderer>();
        if (rends.Length == 0) return new Bounds(go.transform.position, Vector3.zero);
        var b = rends[0].bounds;
        for (int i = 1; i < rends.Length; i++) b.Encapsulate(rends[i].bounds);
        return b;
    }

    void EnqueueFreeAnchors(GameObject module, Transform skip = null)
    {
        var anchors = module.GetComponentsInChildren<DoorAnchor>(true);
        foreach (var a in anchors)
        {
            if (a == null) continue;
            var tr = a.transform;
            if (tr == skip) continue;
            if (usedAnchors.Contains(tr)) continue;
            openAnchors.Enqueue(tr);
        }
    }

    void Seal(Transform targetAnchor)
    {
        if (!doorBlockerPrefab || !targetAnchor) return;

        var cap = Instantiate(doorBlockerPrefab, levelRoot);

        // Start with anchor�s transform
        var rot = targetAnchor.rotation * Quaternion.Euler(doorCapLocalEulerOffset);
        var pos = targetAnchor.TransformPoint(doorCapLocalPositionOffset);

        cap.transform.SetPositionAndRotation(pos, rot);
    }


    GameObject PlaceModule(GameObject prefab, Vector3 pos, Quaternion rot)
    {
        if (prefab == null) return null;
        var inst = Instantiate(prefab, pos, rot, levelRoot);
        inst.name = prefab.name;
        placed.Add(inst);
        return inst;
    }

    void ClearAll()
    {
        usedAnchors.Clear();
        openAnchors.Clear();

        // nuke children of levelRoot
        if (levelRoot != null)
        {
            var toDestroy = new List<Transform>();
            foreach (Transform c in levelRoot) toDestroy.Add(c);
            foreach (var t in toDestroy) DestroyImmediate(t.gameObject);
        }
        placed.Clear();
    }
}
