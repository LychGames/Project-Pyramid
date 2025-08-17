// Assets/Scripts/DoorAnchor.cs
// Simple marker component + gizmo. Blue Z must point OUTWARD from the doorway.

using UnityEngine;

[ExecuteAlways]
public class DoorAnchor : MonoBehaviour
{
    public Color gizmoColor = new Color(0.2f, 0.8f, 1f, 0.75f);
    public float gizmoSize = 0.25f;
    
    [Header("Collision Detection")]
    [SerializeField] Vector3 colliderSize = new Vector3(0.5f, 2f, 0.1f);
    [SerializeField] Vector3 colliderCenter = Vector3.zero;
    
    private BoxCollider doorCollider;

    void Awake()
    {
        SetupDoorCollider();
    }

    void OnValidate()
    {
        if (Application.isPlaying) return;
        SetupDoorCollider();
    }

    void SetupDoorCollider()
    {
        // Get or create BoxCollider
        if (doorCollider == null)
            doorCollider = GetComponent<BoxCollider>();
        
        if (doorCollider == null)
            doorCollider = gameObject.AddComponent<BoxCollider>();
        
        // Configure the collider for door detection
        doorCollider.isTrigger = true;
        doorCollider.size = colliderSize;
        doorCollider.center = colliderCenter;
        
        // Set to DoorTrigger layer if it exists
        if (LayerMask.NameToLayer("DoorTrigger") != -1)
        {
            gameObject.layer = LayerMask.NameToLayer("DoorTrigger");
        }
        else
        {
            Debug.LogWarning("DoorTrigger layer not found! Please create it in Project Settings > Tags and Layers");
        }
    }

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
        
        // Draw collider bounds
        if (doorCollider != null)
        {
            Gizmos.color = new Color(1f, 0f, 0f, 0.3f);
            Gizmos.matrix = transform.localToWorldMatrix;
            Gizmos.DrawWireCube(doorCollider.center, doorCollider.size);
        }
    }
}


