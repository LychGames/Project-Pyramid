using UnityEngine;

public enum PropSocketType { HallSideLeft, HallSideRight, RoomWall, Endcap }

public class PropSocket : MonoBehaviour
{
    public PropSocketType type;

#if UNITY_EDITOR
    void OnDrawGizmos()
    {
        Gizmos.color = Color.yellow;
        Gizmos.DrawWireCube(transform.position, Vector3.one * 0.2f);
        Gizmos.DrawRay(transform.position, transform.forward * 0.4f);
    }
#endif
}
