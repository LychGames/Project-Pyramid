using UnityEngine;
using System.Collections;

public class PlayerSpawner : MonoBehaviour
{
    [Header("Setup")]
    public GameObject playerPrefab;
    [Tooltip("Optional fallback if no object tagged 'PlayerSpawn' is found.")]
    public Transform fallbackSpawn;

    [Header("Grounding")]
    [Tooltip("Layer mask for ground detection. Should include 'levelgeo' layer at minimum")]
    public LayerMask groundMask = ~0;   // Everything by default
    [Tooltip("Spawn this far above the spawn point, then drop to ground.")]
    public float dropHeight = 2f;
    [Tooltip("Tiny lift after grounding so feet never clip.")]
    public float extraClearance = 0.03f;
    [Tooltip("Maximum distance to raycast for ground detection")]
    public float maxGroundDistance = 15f; // Increased to handle multi-floor rooms

    private GameObject playerInstance;

    void Awake()
    {
        // Sync grounding layer mask with main ground mask to avoid conflicts
        grounding.groundMask = groundMask;
        
        // Auto-set to LevelGeo layer if available and ground mask is default
        if (groundMask == (1 << 0)) // Default layer only
        {
            SetGroundMaskToLevelGeo();
        }
        else
        {
            // Always sync FPController ground mask on startup
            UpdateFPControllerGroundMask();
        }
    }

    [ContextMenu("Set Ground Mask to LevelGeo Layer")]
    void SetGroundMaskToLevelGeo()
    {
        // Try different case variations of LevelGeo
        string[] possibleNames = { "LevelGeo", "levelgeo", "levelGeo", "Levelgeo", "LEVELGEO" };
        
        int levelGeoLayer = -1;
        string foundName = "";
        
        foreach (string name in possibleNames)
        {
            levelGeoLayer = LayerMask.NameToLayer(name);
            if (levelGeoLayer >= 0)
            {
                foundName = name;
                break;
            }
        }
        
        if (levelGeoLayer >= 0)
        {
            groundMask = 1 << levelGeoLayer;
            grounding.groundMask = groundMask;
            
            // CRITICAL: Also update the FPController's ground mask for jumping!
            UpdateFPControllerGroundMask();
            
            Debug.Log($"PlayerSpawner: Set ground mask to '{foundName}' layer ({levelGeoLayer}). Mask value: {groundMask}");
        }
        else
        {
            Debug.LogWarning("PlayerSpawner: No LevelGeo layer found! Tried: " + string.Join(", ", possibleNames) + ". Check your layer settings.");
            
            // Show all available layers for debugging
            Debug.Log("PlayerSpawner: Available layers:");
            for (int i = 0; i < 32; i++)
            {
                string layerName = LayerMask.LayerToName(i);
                if (!string.IsNullOrEmpty(layerName))
                {
                    Debug.Log($"  Layer {i}: {layerName}");
                }
            }
        }
    }

    [ContextMenu("Set Ground Mask to Everything")]
    void SetGroundMaskToEverything()
    {
        groundMask = ~0; // Everything
        grounding.groundMask = groundMask;
        
        // Also update the FPController's ground mask
        UpdateFPControllerGroundMask();
        
        Debug.Log($"PlayerSpawner: Set ground mask to EVERYTHING. Mask value: {groundMask}");
    }
    
    void UpdateFPControllerGroundMask()
    {
        // Find and update the FPController's ground mask to match ours
        FPController fpController = GetComponent<FPController>();
        
        // If not found on this GameObject, try to find it in children
        if (fpController == null)
        {
            fpController = GetComponentInChildren<FPController>();
        }
        
        if (fpController != null)
        {
            fpController.groundMask = groundMask;
            Debug.Log($"PlayerSpawner: Updated FPController ground mask to {groundMask} (found on {fpController.gameObject.name})");
        }
        else
        {
            Debug.LogWarning("PlayerSpawner: No FPController found on this GameObject or its children! Jump grounding may not work correctly.");
        }
    }

    [ContextMenu("Sync FPController Ground Mask")]
    void SyncFPControllerGroundMask()
    {
        UpdateFPControllerGroundMask();
    }
    
    [ContextMenu("Test Ground Detection")]
    void TestGroundDetection()
    {
        // Find spawn point and test raycast from there
        Transform spawn = FindSpawnPoint();
        if (spawn == null)
        {
            Debug.LogError("PlayerSpawner: No spawn point found for testing!");
            return;
        }

        Debug.Log($"PlayerSpawner: Found spawn point '{spawn.name}' at {spawn.position}");
        Debug.Log($"PlayerSpawner: Current ground mask: {groundMask} (layer names: {GetLayerNames(groundMask)})");
        Debug.Log($"PlayerSpawner: Max ground distance: {maxGroundDistance}");

        // Test from multiple heights
        Vector3[] testOrigins = {
            spawn.position + Vector3.up * 10f,
            spawn.position + Vector3.up * 5f,
            spawn.position + Vector3.up * 2f,
            spawn.position
        };

        foreach (Vector3 testOrigin in testOrigins)
        {
            Debug.Log($"PlayerSpawner: Testing from {testOrigin} (height: +{testOrigin.y - spawn.position.y:F1})");

            // Test with current mask
            if (Physics.Raycast(testOrigin, Vector3.down, out RaycastHit hit, maxGroundDistance + 10f, groundMask, QueryTriggerInteraction.Ignore))
            {
                Debug.Log($"PlayerSpawner: ✓ Found ground with current mask at {hit.point}, object: {hit.collider.name}, layer: {LayerMask.LayerToName(hit.collider.gameObject.layer)}, distance: {hit.distance:F2}");
                return; // Success, stop testing
            }
            
            // Test with everything mask
            if (Physics.Raycast(testOrigin, Vector3.down, out RaycastHit backupHit, maxGroundDistance + 10f, ~0, QueryTriggerInteraction.Ignore))
            {
                Debug.LogWarning($"PlayerSpawner: ! Found ground with 'everything' mask at {backupHit.point}, object: {backupHit.collider.name}, layer: {LayerMask.LayerToName(backupHit.collider.gameObject.layer)}, distance: {backupHit.distance:F2}");
            }
        }
        
        Debug.LogError("PlayerSpawner: NO GROUND FOUND from any test height!");
        
        // Additional debug: Show all colliders near spawn point
        Collider[] nearbyColliders = Physics.OverlapSphere(spawn.position, 10f);
        Debug.Log($"PlayerSpawner: Found {nearbyColliders.Length} colliders within 10m of spawn:");
        foreach (Collider col in nearbyColliders)
        {
            Debug.Log($"  - {col.name} (layer: {LayerMask.LayerToName(col.gameObject.layer)}, bounds: {col.bounds})");
        }
    }

    string GetLayerNames(LayerMask mask)
    {
        if (mask == ~0) return "Everything";
        
        var names = new System.Collections.Generic.List<string>();
        for (int i = 0; i < 32; i++)
        {
            if ((mask & (1 << i)) != 0)
            {
                string layerName = LayerMask.LayerToName(i);
                if (!string.IsNullOrEmpty(layerName))
                    names.Add($"{layerName}({i})");
            }
        }
        return names.Count > 0 ? string.Join(", ", names) : "None";
    }

    void Start()
    {
        if (playerPrefab == null)
        {
            Debug.LogError("PlayerSpawner: No playerPrefab assigned.");
            return;
        }

        StartCoroutine(SpawnWhenReady());
    }

    Transform FindSpawnPoint()
    {
        // Look for SpawnPoint component in the scene - simple and reliable
        var sp = FindFirstObjectByType<SpawnPoint>();
        if (sp) 
        {
            Debug.Log($"PlayerSpawner: Found SpawnPoint component on {sp.name}");
            return sp.transform;
        }

        // 3) Use explicit fallback if provided.
        if (fallbackSpawn) return fallbackSpawn;

        Debug.LogError("PlayerSpawner: No SpawnPoint component found! Add a SpawnPoint component to your spawn room GameObject.");
        return null;
    }




    void SpawnAndGround(Vector3 basePos, Quaternion rot)
    {
        // Instantiate the player at the requested transform (we�ll ground it next).
        Transform spawn = FindSpawnPoint();
        if (spawn == null)
        {
            Debug.LogError("PlayerSpawner: No spawn point found (tag 'PlayerSpawn', SpawnPoint component, or fallback).");
            return;
        }

        playerInstance = Instantiate(playerPrefab, spawn.position, spawn.rotation);

        // NEW grounding path (toggle via grounding.enabled)
        if (grounding != null && grounding.enabled)
        {
            GroundSpawnedPlayerSafe(playerInstance);
            return; // we�re done � use the new, robust grounding only
        }

        // LEGACY grounding path (kept for compatibility; only runs if new grounding is disabled)
        Vector3 tempPos = spawn.position + Vector3.up * dropHeight;
        playerInstance.transform.position = tempPos;

        var cc = playerInstance.GetComponent<CharacterController>();
        var rb = playerInstance.GetComponent<Rigidbody>();
        if (cc) cc.enabled = false;
        if (rb) rb.isKinematic = true;

        if (TryGetCapsule(playerInstance.transform, out Vector3 localBottom, out Vector3 localTop, out float radius))
        {
            Vector3 startBottom = playerInstance.transform.TransformPoint(localBottom);
            Vector3 startTop = playerInstance.transform.TransformPoint(localTop);

            float castDistance = dropHeight + 5f;
            if (Physics.CapsuleCast(startBottom, startTop, radius, Vector3.down,
                                    out RaycastHit hit, castDistance, groundMask, QueryTriggerInteraction.Ignore))
            {
                Vector3 finalPos = tempPos + Vector3.down * hit.distance + Vector3.up * extraClearance;
                playerInstance.transform.position = finalPos;
            }
            else
            {
                Debug.LogWarning("PlayerSpawner: No ground found under spawn (legacy grounding).");
            }
        }
        else
        {
            float halfHeightGuess = 0.9f;
            if (Physics.Raycast(tempPos, Vector3.down, out RaycastHit hit, dropHeight + 5f, groundMask, QueryTriggerInteraction.Ignore))
            {
                playerInstance.transform.position = hit.point + Vector3.up * (halfHeightGuess + extraClearance);
            }
            else
            {
                Debug.LogWarning("PlayerSpawner: No collider info and no ground hit (legacy grounding).");
            }
        }

        if (cc) cc.enabled = true;
        if (rb) rb.isKinematic = false;
    }

    bool TryGetCapsule(Transform root, out Vector3 localBottom, out Vector3 localTop, out float radius)
    {
        // Prefer CharacterController
        var cc = root.GetComponent<CharacterController>();
        if (cc)
        {
            radius = cc.radius;
            float half = cc.height * 0.5f;
            float vertical = half - cc.radius;
            Vector3 center = cc.center;

            localBottom = center + new Vector3(0f, -vertical, 0f);
            localTop = center + new Vector3(0f, vertical, 0f);
            return true;
        }

        // Or a CapsuleCollider somewhere on the player
        var cap = root.GetComponentInChildren<CapsuleCollider>();
        if (cap)
        {
            Transform t = cap.transform;
            radius = Mathf.Max(0.0001f, cap.radius);
            float half = Mathf.Max(cap.height * 0.5f - radius, 0f);

            Vector3 axis = Vector3.up;
            if (cap.direction == 0) axis = Vector3.right;
            else if (cap.direction == 1) axis = Vector3.up;
            else if (cap.direction == 2) axis = Vector3.forward;

            Vector3 cLocal = cap.center;
            Vector3 bottomLocal = cLocal - axis * half;
            Vector3 topLocal = cLocal + axis * half;

            // Convert collider-space endpoints to the player root's local space
            localBottom = root.InverseTransformPoint(t.TransformPoint(bottomLocal));
            localTop = root.InverseTransformPoint(t.TransformPoint(topLocal));
            return true;
        }

        localBottom = localTop = Vector3.zero;
        radius = 0f;
        return false;
    }
    [System.Serializable]
    public class GroundingSettings
    {
        public bool enabled = true;
        public LayerMask groundMask = ~0;   // Everything
        public float dropHeight = 2f;       // raise first, then drop
        public float extraClearance = 0.03f; // tiny lift after grounding
    }

    // Add this single field (unique name = no conflicts)
    public GroundingSettings grounding = new GroundingSettings();
    void GroundSpawnedPlayerSafe(GameObject player)
    {
        if (player == null || grounding == null || !grounding.enabled) return;

        // Temporarily disable motion while we place
        var cc = player.GetComponent<CharacterController>();
        var rb = player.GetComponent<Rigidbody>();
        bool hadCC = cc && cc.enabled;
        if (cc) cc.enabled = false;
        bool hadRB = rb && !rb.isKinematic;
        if (rb) rb.isKinematic = true;

        // Get capsule geometry from CC or CapsuleCollider
        if (!TryGetCapsuleForGrounding(player.transform, out Vector3 localBottom, out Vector3 localTop, out float radius))
        {
            // Fallback: raycast and place at guessed half-height if no capsule info
            Vector3 rayStart = player.transform.position + Vector3.up * 3f;
            if (Physics.Raycast(rayStart, Vector3.down, out RaycastHit rh, 10f, grounding.groundMask, QueryTriggerInteraction.Ignore))
            {
                float halfHeightGuess = 0.9f;
                player.transform.position = rh.point + Vector3.up * (halfHeightGuess + grounding.extraClearance);
            }
            if (cc) cc.enabled = hadCC;
            if (rb) rb.isKinematic = !hadRB;
            return;
        }

        // World-space capsule endpoints at current position
        Vector3 worldBottom = player.transform.TransformPoint(localBottom);
        Vector3 worldTop = player.transform.TransformPoint(localTop);

        // Cast a simple ray from above to find the intended floor under the spawn area
        Vector3 rayOrigin = worldTop + Vector3.up * 2f; // start a bit above the head
        float rayDistance = (worldTop - worldBottom).magnitude + 6f; // capsule height + some slack

        // Try multiple approaches to find ground
        bool foundGround = false;
        Vector3 finalPosition = player.transform.position;
        
        // Approach 1: Try with grounding mask
        if (Physics.Raycast(rayOrigin, Vector3.down, out RaycastHit hit, rayDistance, grounding.groundMask, QueryTriggerInteraction.Ignore))
        {
            Vector3 desiredBottom = hit.point + Vector3.up * (radius + grounding.extraClearance);
            Vector3 offset = desiredBottom - worldBottom;
            finalPosition = player.transform.position + offset;
            foundGround = true;
            Debug.Log($"PlayerSpawner: Found ground with grounding mask at {hit.point}, object: {hit.collider.name}, layer: {LayerMask.LayerToName(hit.collider.gameObject.layer)}");
        }
        // Approach 2: Try with everything mask as backup
        else if (Physics.Raycast(rayOrigin, Vector3.down, out RaycastHit backupHit, rayDistance, ~0, QueryTriggerInteraction.Ignore))
        {
            Vector3 desiredBottom = backupHit.point + Vector3.up * (radius + grounding.extraClearance);
            Vector3 offset = desiredBottom - worldBottom;
            finalPosition = player.transform.position + offset;
            foundGround = true;
            Debug.LogWarning($"PlayerSpawner: Found ground with 'everything' mask at {backupHit.point}, object: {backupHit.collider.name}, layer: {LayerMask.LayerToName(backupHit.collider.gameObject.layer)}. Your ground mask ({grounding.groundMask}) might need updating!");
        }
        // Approach 3: Try shorter distance raycast
        else if (Physics.Raycast(rayOrigin, Vector3.down, out RaycastHit shortHit, 3f, ~0, QueryTriggerInteraction.Ignore))
        {
            Vector3 desiredBottom = shortHit.point + Vector3.up * (radius + grounding.extraClearance);
            Vector3 offset = desiredBottom - worldBottom;
            finalPosition = player.transform.position + offset;
            foundGround = true;
            Debug.LogWarning($"PlayerSpawner: Found ground with short raycast at {shortHit.point}, object: {shortHit.collider.name}");
        }
        // Approach 4: Try upward raycast in case player is underground
        else if (Physics.Raycast(rayOrigin, Vector3.up, out RaycastHit upHit, rayDistance, ~0, QueryTriggerInteraction.Ignore))
        {
            Vector3 desiredBottom = upHit.point + Vector3.up * (radius + grounding.extraClearance + 0.5f);
            Vector3 offset = desiredBottom - worldBottom;
            finalPosition = player.transform.position + offset;
            foundGround = true;
            Debug.LogWarning($"PlayerSpawner: Found ground ABOVE with upward raycast at {upHit.point}, object: {upHit.collider.name} - level might be underground!");
        }
        
        if (foundGround)
        {
            // Place player well above ground to prevent falling through
            player.transform.position = finalPosition + Vector3.up * 2.0f; // 2m above ground should be safe
            Debug.Log($"PlayerSpawner: Placed player 2m above detected ground at {finalPosition + Vector3.up * 2.0f}");
        }
        else
        {
            // Ultimate fallback: place high above spawn point
            player.transform.position = player.transform.position + Vector3.up * 5.0f; // Very high fallback
            Debug.LogWarning($"PlayerSpawner: NO GROUND FOUND! Placed player 5m above spawn point. Player will fall down.");
        }

        if (cc) cc.enabled = hadCC;
        if (rb) rb.isKinematic = !hadRB;
    }


    bool TryGetCapsuleForGrounding(Transform root, out Vector3 localBottom, out Vector3 localTop, out float radius)
    {
        var cc = root.GetComponent<CharacterController>();
        if (cc)
        {
            radius = cc.radius;
            float half = cc.height * 0.5f;
            float vertical = half - cc.radius;
            Vector3 center = cc.center;

            localBottom = center + new Vector3(0f, -vertical, 0f);
            localTop = center + new Vector3(0f, vertical, 0f);
            return true;
        }

        var cap = root.GetComponentInChildren<CapsuleCollider>();
        if (cap)
        {
            Transform t = cap.transform;
            radius = Mathf.Max(0.0001f, cap.radius);
            float half = Mathf.Max(cap.height * 0.5f - radius, 0f);

            Vector3 axis = Vector3.up;
            if (cap.direction == 0) axis = Vector3.right;
            else if (cap.direction == 2) axis = Vector3.forward;

            Vector3 cLocal = cap.center;
            Vector3 bottomLocal = cLocal - axis * half;
            Vector3 topLocal = cLocal + axis * half;

            localBottom = root.InverseTransformPoint(t.TransformPoint(bottomLocal));
            localTop = root.InverseTransformPoint(t.TransformPoint(topLocal));
            return true;
        }

        localBottom = localTop = Vector3.zero;
        radius = 0f;
        return false;
    }
    IEnumerator SpawnWhenReady()
    {
        // Find spawn using your existing helper
        Transform spawn = FindSpawnPoint();
        if (spawn == null)
        {
            Debug.LogError("PlayerSpawner: No spawn point found (tag, SpawnPoint, or fallback).");
            yield break;
        }

        // Give the generator one frame to finish placing rooms/colliders
        yield return null;

        // Instantiate at the marker; no manual +Y offset here
        GameObject spawnedPlayer = Instantiate(playerPrefab, spawn.position, spawn.rotation);

        // If your new grounding is enabled, try a few frames until we actually see ground
        if (grounding != null && grounding.enabled)
        {
            const int maxTries = 30; // ~0.5s at 60 FPS
            for (int i = 0; i < maxTries; i++)
            {
                GroundSpawnedPlayerSafe(spawnedPlayer);

                // Did we hit ground beneath the player yet?
                if (Physics.Raycast(
                        spawnedPlayer.transform.position + Vector3.up * 0.1f,
                        Vector3.down,
                        out _,
                        5f,
                        grounding.groundMask,
                        QueryTriggerInteraction.Ignore))
                {
                    break; // grounded successfully
                }

                // Wait a frame for level/colliders to finish spawning
                yield return null;
            }
        }
    }
}
