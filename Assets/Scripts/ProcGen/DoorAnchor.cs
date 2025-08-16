using UnityEngine;

[System.Flags]
public enum RoomCategory
{
    None = 0,
    Hallway = 1 << 0,
    Small = 1 << 1,
    Medium = 1 << 2,
    Special = 1 << 3,
    Summon = 1 << 4,
    // add more categories as needed
    All = ~0
}

/// <summary>
/// Place one of these at the CENTER of every doorway.
/// Blue Z axis (forward) MUST point OUT through the opening.
/// Parent chain scale must be (1,1,1).
/// </summary>
public class DoorAnchor : MonoBehaviour
{
    [Tooltip("What categories are allowed to attach HERE (bitmask).")]
    public RoomCategory allowedTargets = RoomCategory.Hallway | RoomCategory.Small | RoomCategory.Medium | RoomCategory.Special | RoomCategory.Summon;

#if UNITY_EDITOR
    void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.cyan;
        Gizmos.DrawRay(transform.position, transform.forward * 0.8f);
        Gizmos.DrawSphere(transform.position, 0.05f);
    }
#endif
}
