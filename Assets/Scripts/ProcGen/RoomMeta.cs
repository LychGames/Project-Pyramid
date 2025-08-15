using UnityEngine;

[System.Flags]
public enum RoomCategory
{
    None = 0,
    Start = 1 << 0,
    Hallway = 1 << 1,
    Small = 1 << 2,
    Medium = 1 << 3,
    Special = 1 << 4,
    Summon = 1 << 5
}

public class RoomMeta : MonoBehaviour
{
    public RoomCategory category = RoomCategory.Small;

    [Header("Selection")]
    [Tooltip("Relative spawn weight when eligible.")]
    public float weight = 1f;

    [Header("Limits")]
    [Tooltip("If >0, this room prefab may spawn at most this many times.")]
    public int maxCount = 0;   // 0 = unlimited
    [Tooltip("If true, place at most once in the entire level.")]
    public bool uniqueOnce = false;

    [Header("Connectivity")]
    [Tooltip("If true, this room can connect to any category (Hub).")]
    public bool connectsAll = false;
    [Tooltip("If true, this room may only connect to Hallway doors.")]
    public bool onlyConnectsToHallways = false;
}
