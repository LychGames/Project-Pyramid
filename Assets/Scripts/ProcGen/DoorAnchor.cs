// DoorAnchor.cs
using UnityEngine;

public class DoorAnchor : MonoBehaviour
{
    [Tooltip("Distance from this anchor to the outward doorway surface, along this anchor's +forward.")]
    public float edgeOffset = 0f;

    [Header("Doorway Size (meters)")]
    public float doorwayWidth = 1.0f;
    public float doorwayHeight = 2.2f;
    public float blockerThickness = 0.10f;

    [Header("Pivot")]
    [Tooltip("If true, this transform's position is at floor level of the doorway; if false, assume pivot is at doorway center.")]
    public bool pivotAtFloor = true;

    public bool IsConnected { get; private set; }

    public void MarkConnected()
    {
        IsConnected = true;
    }

    // simple visual aid
    private void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.cyan;
        Vector3 up = transform.up * (pivotAtFloor ? doorwayHeight : doorwayHeight * 0.5f);
        Gizmos.DrawLine(transform.position, transform.position + up);
        Gizmos.color = Color.magenta;
        Gizmos.DrawLine(transform.position, transform.position + transform.forward * 0.3f);
    }
}
