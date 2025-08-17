using UnityEngine;

public static class LevelGeneratorNetworkDriver
{
    /// <summary>
    /// Host-only entry. Generates and broadcasts placements to all clients.
    /// </summary>
    public static void RunHostGenerationAndBroadcast(int seed, GameSession session)
    {
        Random.InitState(seed);

        // TODO: replace with your real lattice growth.
        // For now, send two example placements to prove the pipeline.
        SendPlacement(session, prefabId: 1001, pos: Vector3.zero, yaw60Index: 0);
        SendPlacement(session, prefabId: 1002, pos: new Vector3(6, 0, 0), yaw60Index: 3);
    }

    private static void SendPlacement(GameSession session, int prefabId, Vector3 pos, int yaw60Index)
    {
        // Constrain yaw to one of 6 lattice directions (0..5) * 60°
        yaw60Index = ((yaw60Index % 6) + 6) % 6;
        var rotY = yaw60Index * 60f;

        var msg = new PlacementMessage
        {
            prefabId = prefabId,
            position = pos,
            rotationEuler = new Vector3(0f, rotY, 0f),
            netId = NetIDs.New()
        };

        session.BroadcastPlacement(msg);
    }
}
