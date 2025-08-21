// Assets/Scripts/DoorAnchor.cs
// Simple marker component + gizmo. Blue Z must point OUTWARD from the doorway.

using UnityEngine;

// Room restriction types for door anchors
public enum RoomRestriction { Any, Specific }

// Module restriction types for door anchors
public enum ModuleRestriction { Any, RoomsOnly, HallwaysOnly, ConnectorsOnly, SpecificRoomType }

[ExecuteAlways]
public class DoorAnchor : MonoBehaviour
{
    public Color gizmoColor = new Color(0.2f, 0.8f, 1f, 0.75f);
    public float gizmoSize = 0.25f;
    
    [Header("Collision Detection")]
    [SerializeField] Vector3 colliderSize = new Vector3(0.5f, 2f, 0.1f);
    [SerializeField] Vector3 colliderCenter = Vector3.zero;
    [SerializeField] bool autoManageCollider = true; // If off, your manual BoxCollider edits are preserved
    
    public enum ConnectionFilterMode { All, Specific }

    [Header("Connection Rules")]
    [Tooltip("All = accepts any kind. Specific = only the selected kind.")]
    public ConnectionFilterMode filterMode = ConnectionFilterMode.All;
    [Tooltip("If filterMode = Specific, only this kind will be accepted.")]
    public ConnectionKind specificKind = ConnectionKind.StraightHall;
    [Tooltip("If filterMode = Specific, use this list to allow multiple kinds. If empty, 'specificKind' is used.")]
    public ConnectionKind[] specificKinds = new ConnectionKind[0];
    
    [Header("Special Room Restrictions")]
    [Tooltip("What type of rooms this anchor can connect to")]
    public RoomRestriction roomRestriction = RoomRestriction.Any;
    [Tooltip("If RoomRestriction is Specific, only this room type can connect")]
    public RoomType allowedRoomType = RoomType.Extract;
    
    [Header("Module Type Restrictions")]
    [Tooltip("What types of modules can connect to this anchor")]
    public ModuleRestriction moduleRestriction = ModuleRestriction.Any;
    
    // Legacy field retained for backward compatibility but hidden from Inspector
    [HideInInspector]
    public ConnectionKind[] canConnectTo = new ConnectionKind[0];
    
    private BoxCollider doorCollider;

    void Awake()
    {
        if (!autoManageCollider)
        {
            // Do not add a collider; just cache if one exists
            doorCollider = GetComponent<BoxCollider>();
            return;
        }

        EnsureCollider();
        if (doorCollider != null)
        {
            ApplyFieldsToCollider();
        }
    }

    void OnValidate()
    {
        if (Application.isPlaying) return;

#if UNITY_EDITOR
        // Avoid editing prefab asset contents directly
        if (UnityEditor.PrefabUtility.IsPartOfPrefabAsset(this)) return;
#endif

        if (!autoManageCollider)
        {
            // Do not add a collider; just cache if one exists
            doorCollider = GetComponent<BoxCollider>();
            return;
        }

        EnsureCollider();
        if (doorCollider == null) return;

        ApplyFieldsToCollider();
    }

    void EnsureCollider()
    {
        if (doorCollider == null)
            doorCollider = GetComponent<BoxCollider>();
        if (doorCollider == null) return;              // ← early out, no auto-add
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

    public bool Allows(ConnectionKind kind)
    {
        if (filterMode == ConnectionFilterMode.All) return true;
        if (specificKinds != null && specificKinds.Length > 0)
        {
            for (int i = 0; i < specificKinds.Length; i++)
            {
                if (specificKinds[i] == kind) return true;
            }
            return false;
        }
        return kind == specificKind;
    }
    
    // Check if a specific room type is allowed on this anchor
    public bool AllowsRoomType(RoomType roomType)
    {
        if (roomRestriction == RoomRestriction.Any) return true;
        return roomType == allowedRoomType;
    }
    
    // Check if a specific module type can connect to this anchor
    public bool AllowsModuleType(string moduleType)
    {
        if (moduleRestriction == ModuleRestriction.Any) return true;
        
        switch (moduleRestriction)
        {
            case ModuleRestriction.RoomsOnly:
                return moduleType == "Room";
            case ModuleRestriction.HallwaysOnly:
                return moduleType == "Hallway";
            case ModuleRestriction.ConnectorsOnly:
                return moduleType == "Connector";
            case ModuleRestriction.SpecificRoomType:
                // For specific room types, only allow rooms (no hallways/connectors)
                return moduleType == "Room";
            default:
                return true;
        }
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


