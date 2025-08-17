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
    [SerializeField] bool autoManageCollider = true; // If off, your manual BoxCollider edits are preserved
    
    private BoxCollider doorCollider;

    void Awake()
    {
        EnsureCollider();
        if (autoManageCollider)
        {
            ApplyFieldsToCollider();
        }
        else
        {
            // Keep your manual collider size/center; ensure trigger + optional layer
            if (doorCollider != null)
            {
                doorCollider.isTrigger = true;
                TrySetDoorTriggerLayer();
                // Reflect current collider values back into fields so they persist
                colliderSize = doorCollider.size;
                colliderCenter = doorCollider.center;
            }
        }
    }

    void OnValidate()
    {
        if (Application.isPlaying) return;
        EnsureCollider();
        if (doorCollider == null) return;

        if (autoManageCollider)
        {
            ApplyFieldsToCollider();
        }
        else
        {
            // Preserve your manual edits and mirror them into the serialized fields
            colliderSize = doorCollider.size;
            colliderCenter = doorCollider.center;
            doorCollider.isTrigger = true;
            TrySetDoorTriggerLayer();
        }
    }

    void EnsureCollider()
    {
        if (doorCollider == null)
            doorCollider = GetComponent<BoxCollider>();
        if (doorCollider == null)
            doorCollider = gameObject.AddComponent<BoxCollider>();
        doorCollider.isTrigger = true;
        TrySetDoorTriggerLayer();
    }

    void ApplyFieldsToCollider()
    {
        if (doorCollider == null) return;
        doorCollider.isTrigger = true;
        doorCollider.size = colliderSize;
        doorCollider.center = colliderCenter;
        TrySetDoorTriggerLayer();
    }

    void TrySetDoorTriggerLayer()
    {
        int layer = LayerMask.NameToLayer("DoorTrigger");
        if (layer != -1)
        {
            gameObject.layer = layer;
        }
        else
        {
            // Only warn in the editor
            #if UNITY_EDITOR
            Debug.LogWarning("DoorTrigger layer not found! Please create it in Project Settings > Tags and Layers");
            #endif
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


