// Assets/Scripts/DoorBlocker.cs
// Component for door blocker prefabs that seal unused door anchors

using UnityEngine;

public class DoorBlocker : MonoBehaviour
{
    [Header("Blocker Settings")]
    [SerializeField] bool snapToLattice = true;
    [SerializeField] float latticeCellSize = 2.5f;
    
    [Header("Visual Debug")]
    [SerializeField] Color gizmoColor = new Color(1f, 0.5f, 0f, 0.7f);
    [SerializeField] float gizmoSize = 0.3f;

    void Start()
    {
        // Ensure the blocker is properly positioned
        if (snapToLattice)
        {
            SnapToLattice();
        }
    }

    void SnapToLattice()
    {
        // Snap position to hexagonal lattice
        Vector3 snappedPos = SnapXZToHexLattice(transform.position, latticeCellSize);
        transform.position = snappedPos;
        
        // Snap rotation to 30° increments
        Vector3 euler = transform.eulerAngles;
        euler.y = Mathf.Round(euler.y / 30f) * 30f;
        transform.rotation = Quaternion.Euler(euler);
    }

    static Vector3 SnapXZToHexLattice(Vector3 worldPos, float s)
    {
        // hex basis vectors in XZ plane
        Vector2 b0 = new Vector2(1f, 0f) * s;            // 0°
        Vector2 b1 = new Vector2(0.5f, 0.8660254f) * s;  // +60°
        Vector2 p = new Vector2(worldPos.x, worldPos.z);

        float det = b0.x * b1.y - b0.y * b1.x;           // = s^2 * 0.8660
        float q = (p.x * b1.y - p.y * b1.x) / det;
        float r = (-p.x * b0.y + p.y * b0.x) / det;

        int qR = Mathf.RoundToInt(q);
        int rR = Mathf.RoundToInt(r);

        Vector2 snapped = qR * b0 + rR * b1;
        return new Vector3(snapped.x, worldPos.y, snapped.y);
    }

    void OnDrawGizmos()
    {
        Gizmos.color = gizmoColor;
        Gizmos.DrawWireCube(transform.position, Vector3.one * gizmoSize);
        
        // Draw forward direction
        Gizmos.color = Color.red;
        Vector3 pos = transform.position;
        Vector3 forward = transform.forward * gizmoSize * 1.5f;
        Gizmos.DrawLine(pos, pos + forward);
        Gizmos.DrawWireSphere(pos + forward, gizmoSize * 0.2f);
    }
}
