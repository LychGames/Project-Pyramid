using UnityEngine;

[System.Serializable]
public struct PlacementMessage
{
    public int prefabId;              // catalog key
    public Vector3 position;          // world
    public Vector3 rotationEuler;     // world yaw/pitch/roll (we use 60� yaw)
    public uint netId;                // stable id assigned by host

    public const int MaxBytes = 64;   // NGO writer hint

    public void Write(
#if NGO
        Unity.Netcode.FastBufferWriter w
#else
        System.Object w
#endif
    )
    {
#if NGO
        w.WriteValueSafe(prefabId);
        w.WriteValueSafe(position);
        w.WriteValueSafe(rotationEuler);
        w.WriteValueSafe(netId);
#endif
    }
    public static PlacementMessage Read(
#if NGO
        Unity.Netcode.FastBufferReader r
#else
        System.Object r
#endif
    )
    {
        PlacementMessage m = default;
#if NGO
        r.ReadValueSafe(out m.prefabId);
        r.ReadValueSafe(out m.position);
        r.ReadValueSafe(out m.rotationEuler);
        r.ReadValueSafe(out m.netId);
#endif
        return m;
    }
}
