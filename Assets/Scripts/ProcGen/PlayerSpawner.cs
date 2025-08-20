using System.Collections;
using UnityEngine;

public class PlayerSpawner : MonoBehaviour
{
    [Header("Player Settings")]
    [Tooltip("Player prefab to spawn")]
    public GameObject playerPrefab;

    [Header("Grounding")]
    [Tooltip("Layer mask for ground detection")]
    public LayerMask groundMask = ~0;
    [Tooltip("Maximum distance to raycast for ground detection")]
    public float maxGroundDistance = 10f;
    [Tooltip("Height to lift player above detected ground")]
    public float groundClearance = 0.1f;

    private GameObject playerInstance;

    void Awake()
    {
        // Auto-set to LevelGeo layer if available
        int levelGeoLayer = LayerMask.NameToLayer("LevelGeo");
        if (levelGeoLayer >= 0)
        {
            groundMask = 1 << levelGeoLayer;
        }
    }

    void Start()
    {
        StartCoroutine(SpawnPlayerAfterGeneration());
    }

    IEnumerator SpawnPlayerAfterGeneration()
    {
        // Wait one frame for generation to complete
        yield return new WaitForEndOfFrame();
        
        if (playerPrefab != null)
        {
            SpawnPlayer();
        }
    }

    void SpawnPlayer()
    {
        Transform spawnPoint = FindSpawnPoint();
        if (spawnPoint == null)
        {
            Debug.LogWarning("PlayerSpawner: No spawn point found, using origin");
            spawnPoint = transform;
        }

        // Spawn player
        playerInstance = Instantiate(playerPrefab, spawnPoint.position, spawnPoint.rotation);
        
        // Ground the player
        GroundPlayer(playerInstance);
        
        // Sync ground masks
        SyncGroundMasks(playerInstance);
    }

    Transform FindSpawnPoint()
    {
        // Find the starting room - should be the small room closest to origin
        Transform[] allTransforms = FindObjectsOfType<Transform>();
        Transform closestSmallRoom = null;
        float closestDistance = float.MaxValue;

        foreach (Transform t in allTransforms)
        {
            RoomMeta meta = t.GetComponent<RoomMeta>();
            if (meta != null && meta.subtype == PlacementCatalog.HallSubtype.SmallRoom)
            {
                float distance = Vector3.Distance(t.position, Vector3.zero);
                if (distance < closestDistance)
                {
                    closestDistance = distance;
                    closestSmallRoom = t;
                }
            }
        }

        return closestSmallRoom;
    }

    void GroundPlayer(GameObject player)
    {
        // Start from the room position, just slightly above
        Vector3 startPos = player.transform.position + Vector3.up * 2f;
        
        if (Physics.Raycast(startPos, Vector3.down, out RaycastHit hit, maxGroundDistance, groundMask))
        {
            Vector3 groundedPos = hit.point + Vector3.up * groundClearance;
            player.transform.position = groundedPos;
        }
        else if (Physics.Raycast(startPos, Vector3.down, out RaycastHit backupHit, maxGroundDistance, ~0))
        {
            Vector3 groundedPos = backupHit.point + Vector3.up * groundClearance;
            player.transform.position = groundedPos;
            Debug.LogWarning("PlayerSpawner: Used backup ground detection - check your ground layers");
        }
        else
        {
            // If no ground found, just place at room level
            Debug.LogWarning("PlayerSpawner: No ground detected, placing at room level");
        }
    }

    void SyncGroundMasks(GameObject player)
    {
        // Update FPController ground mask to match
        FPController fpController = player.GetComponent<FPController>();
        if (fpController == null)
        {
            fpController = player.GetComponentInChildren<FPController>();
        }

        if (fpController != null)
        {
            fpController.groundMask = groundMask;
        }
    }

    [ContextMenu("Respawn Player")]
    void RespawnPlayer()
    {
        if (playerInstance != null)
        {
            DestroyImmediate(playerInstance);
        }
        SpawnPlayer();
    }
}

[System.Serializable]
public class GroundingSettings
{
    public bool enabled = true;
    public LayerMask groundMask = ~0;
    public float dropHeight = 2f;
    public float extraClearance = 0.03f;
}