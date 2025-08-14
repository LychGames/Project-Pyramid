using UnityEngine;

public class AnchorCapTester : MonoBehaviour
{
    public GameObject doorBlockerPrefab;

    [ContextMenu("Spawn Blocker Here")]
    void SpawnHere()
    {
        if (!doorBlockerPrefab)
        {
            Debug.LogWarning("Assign doorBlockerPrefab in the Inspector first.");
            return;
        }

        var go = Instantiate(doorBlockerPrefab, transform);
        go.transform.localPosition = Vector3.zero;
        go.transform.localRotation = Quaternion.identity;
        go.transform.localScale = Vector3.one;

        Debug.Log("Spawned blocker at " + name);
    }
}
