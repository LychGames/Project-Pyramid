using UnityEngine;

[System.Flags]
public enum RoomCategory
{
    None = 0,
    Hallway = 1 << 0,
    Small = 1 << 1,
    Medium = 1 << 2,
    Large = 1 << 3,
    Special = 1 << 4,
    Summon = 1 << 5,
    All = ~0
}

/// Place at the CENTER of each doorway. Blue Z must point OUT through the opening.
public class DoorAnchor : MonoBehaviour
{
    [Tooltip("Which categories may attach here (bitmask).")]
    public RoomCategory allowedTargets = RoomCategory.All;

#if UNITY_EDITOR
    private void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.cyan;
        Gizmos.DrawSphere(transform.position, 0.06f);
        Gizmos.DrawRay(transform.position, transform.forward * 0.6f);
    }
#endif
}

