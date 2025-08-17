using UnityEngine;
using System.Collections;

public class PlayerSpawner : MonoBehaviour
{
    [Header("Setup")]
    public GameObject playerPrefab;
    [Tooltip("Optional fallback if no object tagged 'PlayerSpawn' is found.")]
    public Transform fallbackSpawn;

    [Header("Grounding")]
    public LayerMask groundMask = ~0;   // Everything by default
    [Tooltip("Spawn this far above the spawn point, then drop to ground.")]
    public float dropHeight = 2f;
    [Tooltip("Tiny lift after grounding so feet never clip.")]
    public float extraClearance = 0.03f;

    private GameObject playerInstance;

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
        // 1) Try the tag, but don�t crash if it doesn�t exist.
        try
        {
            GameObject tagged = GameObject.FindGameObjectWithTag("PlayerSpawn");
            if (tagged) return tagged.transform;
            GameObject taggedAlt = GameObject.FindGameObjectWithTag("Playerspawn");
            if (taggedAlt) return taggedAlt.transform;
        }
        catch (UnityException)
        {
            // Tag not defined � ignore and fall through to other options.
            Debug.LogWarning("PlayerSpawner: Tag 'PlayerSpawn' is not defined. Using fallback(s).");
        }

        // 2) Try a SpawnPoint component in the scene.
        var sp = FindFirstObjectByType<SpawnPoint>();
        if (sp) return sp.transform;

        // 3) Use explicit fallback if provided.
        if (fallbackSpawn) return fallbackSpawn;

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

        if (Physics.Raycast(rayOrigin, Vector3.down, out RaycastHit hit, rayDistance, grounding.groundMask, QueryTriggerInteraction.Ignore))
        {
            // We want the capsule bottom to sit at floor + clearance
            Vector3 desiredBottom = hit.point + Vector3.up * (radius + grounding.extraClearance);

            // Compute how far to move the whole player so bottom goes to desiredBottom
            Vector3 offset = desiredBottom - worldBottom;

            player.transform.position += offset;
        }
        else
        {
            // Fallback: if no floor seen, don't move them; optional small nudge up
            player.transform.position += Vector3.up * grounding.dropHeight;
            Debug.LogWarning("PlayerSpawner: Ground ray found no floor under spawn. Check colliders/mask.");
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
