using UnityEngine;

public class RoomMeta : MonoBehaviour
{
    [Header("Classification")]
    public RoomCategory category = RoomCategory.Small;

    [Tooltip("If true, this prefab can attach to any DoorAnchor regardless of its mask.")]
    public bool connectsAll = false;

    [Header("Spawn Limits & Weight")]
    [Min(0.0001f)] public float weight = 1f;
    public bool uniqueOnce = false;
    public int maxCount = 0;

    [Header("Clearance")]
    [Tooltip("Extra pruning radius after placing this prefab (good for large rooms).")]
    public float clearancePadding = 0f;

    [Header("Special Roles")]
    public bool isStartPrefab = false;
    public bool isEndPrefab = false;
}
