using UnityEngine;

/// <summary>
/// Attach to the ROOT of every placeable prefab (rooms, halls, etc.).
/// Controls category, weights, uniqueness, and local clearance.
/// </summary>
public class RoomMeta : MonoBehaviour
{
    [Header("Classification")]
    public RoomCategory category = RoomCategory.Hallway;

    [Tooltip("If true, this prefab can connect to any DoorAnchor regardless of its allowedTargets.")]
    public bool connectsAll = false;

    [Header("Weights & Limits")]
    [Tooltip("Relative chance to be picked vs. others in same gate.")]
    public float weight = 1f;

    [Tooltip("If true, this prefab can be placed at most once.")]
    public bool uniqueOnce = false;

    [Tooltip("If > 0, caps the total times this prefab can appear.")]
    public int maxCount = 0;

    [Header("Clearance")]
    [Tooltip("Extra pruning radius applied after placing THIS prefab (meters). Useful for big rooms).")]
    public float clearancePadding = 0f;
}
