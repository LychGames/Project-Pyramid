using System;
using System.Collections;
using UnityEngine;
using Unity.AI.Navigation;

[DisallowMultipleComponent]
[RequireComponent(typeof(NavMeshSurface))]
public class NavMeshRuntimeBaker : MonoBehaviour
{
    [Header("When to bake")]
    public bool bakeOnStart = false;
    public bool useEndOfFrame = true;

    [Header("Debug")]
    public bool logDuration = false;

    public bool IsBaking { get; private set; }
    public event Action<bool> OnBakingChanged; // true=start, false=end

    NavMeshSurface surface;
    Coroutine pending;

    void Awake()
    {
        surface = GetComponent<NavMeshSurface>();
        if (surface.collectObjects != CollectObjects.Children)
            surface.collectObjects = CollectObjects.Children;
    }

    void Start()
    {
        if (bakeOnStart) RequestBake();
    }

    public void RequestBake()
    {
        if (pending == null) pending = StartCoroutine(BakeCR());
    }

    public void BakeNow()
    {
        if (pending != null) { StopCoroutine(pending); pending = null; }
        if (useEndOfFrame) StartCoroutine(BakeCR()); else DoBake();
    }

    IEnumerator BakeCR()
    {
        if (useEndOfFrame) yield return new WaitForEndOfFrame();
        DoBake();
        pending = null;
    }

    void DoBake()
    {
        if (surface == null)
        {
            Debug.LogError($"[NavMeshRuntimeBaker] NavMeshSurface is null on '{name}'. Cannot bake.");
            return;
        }

        IsBaking = true;
        OnBakingChanged?.Invoke(true);

        try
        {
            var t0 = Time.realtimeSinceStartup;
            surface.BuildNavMesh(); // synchronous
            if (logDuration)
            {
                var ms = (Time.realtimeSinceStartup - t0) * 1000f;
                Debug.Log($"[NavMeshRuntimeBaker] Bake completed in {ms:0.0} ms on '{name}'.");
            }
        }
        catch (System.Exception e)
        {
            Debug.LogError($"[NavMeshRuntimeBaker] Failed to bake NavMesh on '{name}': {e.Message}");
        }
        finally
        {
            IsBaking = false;
            OnBakingChanged?.Invoke(false);
        }
    }
}
