using UnityEngine;

public static class NetIDs
{
    private static uint _next = 1; // 0 = invalid
    public static uint New() => _next++;
}

public class NetworkEntity : MonoBehaviour
{
    [SerializeField] private uint netId;
    public uint NetId => netId;

    public void SetNetId(uint id) => netId = id;

    void OnEnable()
    {
        if (GameSession.Instance != null)
            GameSession.Instance.Register(this);
    }

    void OnDisable()
    {
        if (GameSession.Instance != null)
            GameSession.Instance.Unregister(this);
    }
}
