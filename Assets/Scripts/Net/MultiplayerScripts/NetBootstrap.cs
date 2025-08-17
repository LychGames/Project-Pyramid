using UnityEngine;

public class NetBootstrap : MonoBehaviour
{
    [Header("Debug Start")]
    public bool autoStart = true;
    public bool startAsHost = true;

    void Awake()
    {
        DontDestroyOnLoad(gameObject);
    }

    void Start()
    {
        if (!autoStart) return;
        if (startAsHost) StartHost();
        else StartClient();
    }

    public void StartHost()
    {
#if MIRROR
        var nm = Mirror.NetworkManager.singleton;
        nm.StartHost();
        GameSession.Instance.OnHostStarted();
#elif NGO
        var nm = Unity.Netcode.NetworkManager.Singleton;
        if (!nm.IsServer && !nm.IsClient)
        {
            nm.StartHost();
            GameSession.Instance.OnHostStarted();
        }
#else
        Debug.LogError("Define MIRROR or NGO in Scripting Define Symbols.");
#endif
    }

    public void StartClient()
    {
#if MIRROR
        var nm = Mirror.NetworkManager.singleton;
        nm.StartClient();
        GameSession.Instance.OnClientStarted();
#elif NGO
        var nm = Unity.Netcode.NetworkManager.Singleton;
        if (!nm.IsServer && !nm.IsClient)
        {
            nm.StartClient();
            GameSession.Instance.OnClientStarted();
        }
#else
        Debug.LogError("Define MIRROR or NGO in Scripting Define Symbols.");
#endif
    }
}
