using System.Collections.Generic;
using UnityEngine;

public class Room : MonoBehaviour
{
    public List<DoorAnchor> anchors = new List<DoorAnchor>();

    private void Reset()
    {
        anchors.Clear();
        anchors.AddRange(GetComponentsInChildren<DoorAnchor>());
    }

    public Bounds GetApproxBounds()
    {
        var renderers = GetComponentsInChildren<Renderer>();
        if (renderers.Length == 0) return new Bounds(transform.position, Vector3.one);
        var b = renderers[0].bounds;
        foreach (var r in renderers) b.Encapsulate(r.bounds);
        return b;
    }
}
