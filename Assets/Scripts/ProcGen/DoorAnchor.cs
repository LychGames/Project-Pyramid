// Assets/Scripts/DoorAnchor.cs
// Simple marker component + gizmo. Blue Z must point OUTWARD from the doorway.

using UnityEngine;

[ExecuteAlways]
public class DoorAnchor : MonoBehaviour
{
    public Color gizmoColor = new Color(0.2f, 0.8f, 1f, 0.75f);
    public float gizmoSize = 0.25f;

    void OnDrawGizmos()
    {
        Gizmos.color = gizmoColor;
        Gizmos.DrawWireCube(transform.position, new Vector3(gizmoSize, gizmoSize * 0.6f, gizmoSize));
        // Draw forward arrow (blue)
        var p = transform.position;
        var f = transform.forward * (gizmoSize * 1.5f);
        Gizmos.color = Color.cyan;
        Gizmos.DrawLine(p, p + f);
        Gizmos.DrawWireSphere(p + f, gizmoSize * 0.12f);
    }
}


