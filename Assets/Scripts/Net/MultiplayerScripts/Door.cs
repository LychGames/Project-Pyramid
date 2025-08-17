using UnityEngine;

public class Door : NetworkEntity
{
    [SerializeField] private bool isOpen;
    [SerializeField] private Animator anim;

    public void ClientRequestToggle()
    {
#if MIRROR
        if (Mirror.NetworkClient.active)
            Mirror.NetworkClient.Send(new DoorToggleRequest { netId = NetId });
#elif NGO
        var cm = Unity.Netcode.NetworkManager.Singleton.CustomMessagingManager;
        using var w = new Unity.Netcode.FastBufferWriter(DoorToggleRequest.MaxBytes, Unity.Collections.Allocator.Temp);
        new DoorToggleRequest { netId = NetId }.Write(w);
        cm.SendNamedMessage("DOOR_REQ", Unity.Netcode.NetworkManager.Singleton.ServerClientId, w);
#else
        Debug.LogWarning("ClientRequestToggle called without MIRROR/NGO defined.");
#endif
    }

    // Called on host after validating request (distance, permissions, etc.)
    public void HostSetOpen(bool open)
    {
        isOpen = open;
        ApplyLocal(open);
        BroadcastState(open);
    }

    private void ApplyLocal(bool open)
    {
        if (anim != null) anim.SetBool("Open", open);
        // enable/disable colliders, play SFX, etc.
    }

    private void BroadcastState(bool open)
    {
#if MIRROR
        Mirror.NetworkServer.SendToAll(new DoorStateMessage { netId = NetId, isOpen = open });
#elif NGO
        var cm = Unity.Netcode.NetworkManager.Singleton.CustomMessagingManager;
        using var w = new Unity.Netcode.FastBufferWriter(DoorStateMessage.MaxBytes, Unity.Collections.Allocator.Temp);
        new DoorStateMessage { netId = NetId, isOpen = open }.Write(w);
        cm.SendNamedMessageToAll("DOOR_STATE", w);
#else
        Debug.Log("Door state changed locally (no net layer). NetId=" + NetId + " open=" + open);
#endif
    }
}

public struct DoorToggleRequest
{
    public uint netId;
    public const int MaxBytes = 8;
    public void Write(
#if NGO
        Unity.Netcode.FastBufferWriter w
#else
        System.Object w
#endif
    )
    {
#if NGO
        w.WriteValueSafe(netId);
#endif
    }
    public static DoorToggleRequest Read(
#if NGO
        Unity.Netcode.FastBufferReader r
#else
        System.Object r
#endif
    )
    {
        uint id = 0;
#if NGO
        r.ReadValueSafe(out id);
#endif
        return new DoorToggleRequest { netId = id };
    }
}

public struct DoorStateMessage
{
    public uint netId;
    public bool isOpen;
    public const int MaxBytes = 12;
    public void Write(
#if NGO
        Unity.Netcode.FastBufferWriter w
#else
        System.Object w
#endif
    )
    {
#if NGO
        w.WriteValueSafe(netId); w.WriteValueSafe(isOpen);
#endif
    }
    public static DoorStateMessage Read(
#if NGO
        Unity.Netcode.FastBufferReader r
#else
        System.Object r
#endif
    )
    {
        uint id = 0; bool o = false;
#if NGO
        r.ReadValueSafe(out id); r.ReadValueSafe(out o);
#endif
        return new DoorStateMessage { netId = id, isOpen = o };
    }
}
