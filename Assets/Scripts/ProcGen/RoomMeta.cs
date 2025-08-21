// Assets/Scripts/RoomMeta.cs
// Lightweight metadata for weighting & categorization (optional but useful).

using UnityEngine;

public enum RoomCategory { Start, Hallway, Junction, Room }

// Room types for special room restrictions
public enum RoomType { Regular, Extract, Basement, BasementStairs, StairRoom, Summoning }

// More granular connection kinds so anchors can whitelist compatible targets
public enum ConnectionKind
{
    Start,
    StraightHall,
    Angled15Hall,
    Angled30Hall,
    TripConnHall,
    TripConnHub,
    Room,
    StartRoom
}

public class RoomMeta : MonoBehaviour
{
    public RoomCategory category = RoomCategory.Hallway;
    [Tooltip("Relative chance to pick this prefab when multiple are eligible.")]
    public float weight = 1f;

    [Header("Connection Classification")]
    [Tooltip("Granular type used by DoorAnchor.canConnectTo to filter compatible targets.")]
    public ConnectionKind connectionKind = ConnectionKind.StraightHall;

    // Theme system metadata
    public enum SizeClass { Hall, Small, Medium, Large, Mega }
    [Tooltip("Clear doorway width this piece expects (e.g., 10/14/20)")]
    public int hallWidth = 10;
    public SizeClass sizeClass = SizeClass.Hall;
    public string[] themeTags;
    [Tooltip("Mark true for width reducers/expanders/adapters")]
    public bool isAdapter = false;

    [Header("Room Type")]
    [Tooltip("What type of room this is - affects which door anchors can connect to it")]
    public RoomType roomType = RoomType.Regular;
    
    [Header("Placement Catalog Metadata (edit here on the prefab)")]
    [Tooltip("Subtype used by the generator/catalog for selection.")]
    public PlacementCatalog.HallSubtype subtype = PlacementCatalog.HallSubtype.Straight;
    [Tooltip("How many usable anchors this piece exposes once placed (2 = hallway, 3 = Y junction).")]
    public int anchorCount = 2;
    [Tooltip("Selection weight within its subtype pool.")]
    public float catalogWeight = 1f;
    [Tooltip("Minimum growth depth before this can appear.")]
    public int depthGate = 0;
    [Tooltip("Soft cap per map for this prefab id (catalog entry).")]
    public int maxPerMap = 999;
    [Tooltip("Special landmark piece (rare).")]
    public bool isLandmark = false;
}
