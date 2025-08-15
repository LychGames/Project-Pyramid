using UnityEngine;

[DisallowMultipleComponent]
public class DoorAnchor : MonoBehaviour
{
    // add near the top of the class:
    public RoomCategory allowedTargets = RoomCategory.Hallway | RoomCategory.Small | RoomCategory.Medium | RoomCategory.Special | RoomCategory.Summon;

    // optional: for a pure hallway door, set this to Hallway only in the prefab.
    // for "connects all" hubs, you can leave it as-is or toggle in RoomMeta.

    // Marker component for doorway connection points.
    // Keep it lightweight. We track usage in the generator (no need for flags here).

    // OPTIONAL: Draw an arrow in Scene view so you can see direction quickly.
    private void OnDrawGizmos()
    {
        Gizmos.matrix = transform.localToWorldMatrix;
        Gizmos.color = new Color(0.2f, 0.8f, 1f, 0.9f);
        Gizmos.DrawRay(Vector3.zero, Vector3.forward * 0.5f);
        Gizmos.DrawWireCube(new Vector3(0f, 0f, 0.02f), new Vector3(0.2f, 0.2f, 0.04f));
    }
}