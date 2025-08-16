using UnityEngine;

public class RoomMeta : MonoBehaviour
{
    [Header("Classification")]
    public bool isStartRoom = false;
    public bool isEndRoom = false;
    public bool isHallway = false;

    [Header("Generation Settings")]
    public bool allowRotation = true;   // whether this room can rotate during placement
    public bool gridSnap = true;        // whether this room should snap to grid (new option)
}
