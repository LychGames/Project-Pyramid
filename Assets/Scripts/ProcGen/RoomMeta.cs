// Assets/Scripts/RoomMeta.cs
// Lightweight metadata for weighting & categorization (optional but useful).

using UnityEngine;

public enum RoomCategory { Start, Hallway, Junction, Room }

public class RoomMeta : MonoBehaviour
{
    public RoomCategory category = RoomCategory.Hallway;
    [Tooltip("Relative chance to pick this prefab when multiple are eligible.")]
    public float weight = 1f;
}
