// Assets/Scripts/DoorAnchor.cs
// Simple, reliable door anchor component for procedural generation

using UnityEngine;

// Simple room restriction types
public enum RoomRestriction { Any, Specific }

// Simple module restriction types  
public enum ModuleRestriction { Any, RoomsOnly, HallwaysOnly, ConnectorsOnly }

[ExecuteAlways]
public class DoorAnchor : MonoBehaviour
{
    [Header("Visual Debug")]
    public Color gizmoColor = new Color(0.2f, 0.8f, 1f, 0.75f);
    public float gizmoSize = 0.25f;
    
    [Header("Connection Rules")]
    [Tooltip("What types of modules can connect to this anchor")]
    public ModuleRestriction moduleRestriction = ModuleRestriction.Any;
    
    [Header("Room Restrictions")]
    [Tooltip("What type of rooms this anchor can connect to")]
    public RoomRestriction roomRestriction = RoomRestriction.Any;
    [Tooltip("If RoomRestriction is Specific, only this room type can connect")]
    public RoomType allowedRoomType = RoomType.Regular;
    
    [Header("Connection Filter")]
    [Tooltip("All = accepts any kind. Specific = only the selected kind.")]
    public ConnectionFilterMode filterMode = ConnectionFilterMode.All;
    [Tooltip("If filterMode = Specific, only this kind will be accepted.")]
    public ConnectionKind specificKind = ConnectionKind.StraightHall;
    [Tooltip("If filterMode = Specific, use this list to allow multiple kinds")]
    public ConnectionKind[] specificKinds = new ConnectionKind[0];
    
    // Legacy field retained for backward compatibility but hidden from Inspector
    [HideInInspector]
    public ConnectionKind[] canConnectTo = new ConnectionKind[0];
    
    public enum ConnectionFilterMode { All, Specific }
    
    private BoxCollider doorCollider;

    void Awake()
    {
        EnsureCollider();
    }

    void OnValidate()
    {
        if (Application.isPlaying) return;
        EnsureCollider();
    }

    void EnsureCollider()
    {
        if (doorCollider == null)
            doorCollider = GetComponent<BoxCollider>();
            
        if (doorCollider == null)
        {
            doorCollider = gameObject.AddComponent<BoxCollider>();
        }
        
        doorCollider.isTrigger = true;
        doorCollider.size = new Vector3(0.5f, 2f, 0.1f);
        doorCollider.center = Vector3.zero;
        
        // Set to DoorTrigger layer if it exists
        TrySetDoorTriggerLayer();
    }

    void TrySetDoorTriggerLayer()
    {
        int layer = LayerMask.NameToLayer("DoorTrigger");
        if (layer != -1)
        {
            gameObject.layer = layer;
        }
    }

    /// <summary>
    /// Check if this anchor allows a specific connection kind
    /// </summary>
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
    
    /// <summary>
    /// Check if a specific room type is allowed on this anchor
    /// </summary>
    public bool AllowsRoomType(RoomType roomType)
    {
        if (roomRestriction == RoomRestriction.Any) return true;
        return roomType == allowedRoomType;
    }
    
    /// <summary>
    /// Check if a specific module type can connect to this anchor
    /// </summary>
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
            default:
                return true;
        }
    }

    /// <summary>
    /// Check if this anchor can connect to a specific module based on all restrictions
    /// </summary>
    public bool CanConnectTo(GameObject module)
    {
        if (module == null) return false;
        
        var roomMeta = module.GetComponent<RoomMeta>();
        if (roomMeta == null) return false;
        
        // Check module type restriction
        string moduleType = GetModuleType(roomMeta);
        if (!AllowsModuleType(moduleType)) return false;
        
        // Check room type restriction
        if (!AllowsRoomType(roomMeta.roomType)) return false;
        
        // Check connection kind restriction
        if (!Allows(roomMeta.connectionKind)) return false;
        
        return true;
    }
    
    /// <summary>
    /// Get the module type string from RoomMeta
    /// </summary>
    private string GetModuleType(RoomMeta roomMeta)
    {
        switch (roomMeta.category)
        {
            case RoomCategory.Room:
                return "Room";
            case RoomCategory.Hallway:
                return "Hallway";
            case RoomCategory.Junction:
                return "Connector";
            case RoomCategory.Start:
                return "Room";
            default:
                return "Room";
        }
    }

    void OnDrawGizmos()
    {
        // Draw anchor cube
        Gizmos.color = gizmoColor;
        Gizmos.DrawWireCube(transform.position, new Vector3(gizmoSize, gizmoSize * 0.6f, gizmoSize));
        
        // Draw forward arrow (blue) - this shows the direction the anchor points
        var p = transform.position;
        var f = transform.forward * (gizmoSize * 1.5f);
        Gizmos.color = Color.cyan;
        Gizmos.DrawLine(p, p + f);
        Gizmos.DrawWireSphere(p + f, gizmoSize * 0.12f);
        
        // Draw collider bounds in a different color to avoid confusion
        if (doorCollider != null)
        {
            Gizmos.color = new Color(0f, 1f, 0f, 0.3f); // Green instead of red
            Gizmos.matrix = transform.localToWorldMatrix;
            Gizmos.DrawWireCube(doorCollider.center, doorCollider.size);
        }
    }
}


