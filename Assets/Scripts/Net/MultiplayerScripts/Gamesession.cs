using System.Collections.Generic;
using UnityEngine;
#if NGO
using Unity.Netcode;
using Unity.Collections;
#endif

public class GameSession : MonoBehaviour
{
    public static GameSession Instance { get; private set; }

    [Header("Session")]
    public int Seed;
    public bool IsHost;
    public bool IsClient;

    [Header("Placement")]
    public PlacementCatalog catalog;

    // NetId registry
    private readonly Dictionary<uint, NetworkEntity> _entities = new();

    private void Awake()
    {
        if (Instance != null) { Destroy(gameObject); return; }
        Instance = this;
        DontDestroyOnLoad(gameObject);
    }

    private void OnEnable()
    {
        // Register NGO custom message handlers when NetworkManager exists
#if NGO
        var nm = NetworkManager.Singleton;
        if (nm != null)
        {
            nm.OnServerStarted += OnServerStarted;
            nm.OnClientConnectedCallback += OnClientConnected;
            RegisterMessageHandlers();
        }
#endif
    }

    private void OnDisable()
    {
        
#if NGO
        var nm = NetworkManager.Singleton;
        if (nm != null)
        {
            nm.OnServerStarted -= OnServerStarted;
            nm.OnClientConnectedCallback -= OnClientConnected;
        }
#endif
    }

    // --- Public entry points from your bootstrap (Host/Client buttons, etc.) ---

    public void OnHostStarted()
    {
        IsHost = true; IsClient = true;
        if (Seed == 0) Seed = Random.Range(int.MinValue, int.MaxValue);
        BroadcastSessionSeed(Seed);

        // Host runs generation and broadcasts placements
        LevelGeneratorNetworkDriver.RunHostGenerationAndBroadcast(Seed, this);
    }

    public void OnClientStarted()
    {
        IsHost = false; IsClient = true;
        // Wait for seed + placement messages
    }

    // --- NetworkManager events ---

    private void OnServerStarted()
    {
        // If you used NetworkManager.StartHost() elsewhere, you can call OnHostStarted() there instead.
    }

    private void OnClientConnected(ulong _)
    {
        // Clients will receive seed/placements as they arrive
    }

    // --- Entity registry ---

    public void Register(NetworkEntity e)
    {
        if (!_entities.ContainsKey(e.NetId))
            _entities.Add(e.NetId, e);
    }

    public void Unregister(NetworkEntity e)
    {
        if (_entities.ContainsKey(e.NetId))
            _entities.Remove(e.NetId);
    }

    public bool TryGet(uint id, out NetworkEntity ent) => _entities.TryGetValue(id, out ent);

    // ---------------- Messaging ----------------

    private const string MSG_SEED = "GS_SEED";
    private const string MSG_PLACE = "GS_PLACE";

    private void RegisterMessageHandlers()
    {
#if NGO
        var cm = NetworkManager.Singleton.CustomMessagingManager;
        cm.RegisterNamedMessageHandler(MSG_SEED, (sender, reader) =>
        {
            var msg = SessionSeedMessage.Read(reader);
            OnSeedMessage(msg);
        });
        cm.RegisterNamedMessageHandler(MSG_PLACE, (sender, reader) =>
        {
            var msg = PlacementMessage.Read(reader);
            OnPlacementMessage(msg);
        });
#endif
    }

    public void BroadcastPlacement(in PlacementMessage msg)
    {
#if NGO
        var cm = NetworkManager.Singleton.CustomMessagingManager;
        using var w = new FastBufferWriter(PlacementMessage.MaxBytes, Allocator.Temp);
        msg.Write(w);
        cm.SendNamedMessageToAll(MSG_PLACE, w);
#else
        Debug.LogWarning("BroadcastPlacement called without NGO enabled.");
#endif
    }

    public void BroadcastSessionSeed(int seed)
    {
#if NGO
        var cm = NetworkManager.Singleton.CustomMessagingManager;
        using var w = new FastBufferWriter(SessionSeedMessage.MaxBytes, Allocator.Temp);
        new SessionSeedMessage { seed = seed }.Write(w);
        cm.SendNamedMessageToAll(MSG_SEED, w);
#else
        Debug.LogWarning("BroadcastSessionSeed called without NGO enabled.");
#endif
    }

    private void OnSeedMessage(SessionSeedMessage msg)
    {
        if (IsHost) return;
        Seed = msg.seed;
    }

    private void OnPlacementMessage(PlacementMessage msg)
    {
        if (catalog == null)
        {
            Debug.LogError("GameSession: PlacementCatalog not assigned.");
            return;
        }

        var prefab = catalog.GetPrefab(msg.prefabId);
        if (prefab == null)
        {
            Debug.LogError($"GameSession: No prefab for id {msg.prefabId}");
            return;
        }

        // Spawn a local instance (clients mirror host placements)
        var inst = Instantiate(prefab, msg.position, Quaternion.Euler(msg.rotationEuler));
        var ne = inst.GetComponent<NetworkEntity>();
        if (ne == null) ne = inst.AddComponent<NetworkEntity>();
        ne.SetNetId(msg.netId);
        Register(ne);
    }
}

// --- Simple messages for NGO ---

public struct SessionSeedMessage
{
    public int seed;
    public const int MaxBytes = 8;

    public void Write(
#if NGO
        FastBufferWriter w
#else
        System.Object w
#endif
    )
    {
#if NGO
        w.WriteValueSafe(seed);
#endif
    }
    public static SessionSeedMessage Read(
#if NGO
        FastBufferReader r
#else
        System.Object r
#endif
    )
    {
        int s = 0;
#if NGO
        r.ReadValueSafe(out s);
#endif
        return new SessionSeedMessage { seed = s };
    }
}

