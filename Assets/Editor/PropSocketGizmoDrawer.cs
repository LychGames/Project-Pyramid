#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

public static class PropSocketGizmoDrawer
{
    // Draw gizmos for PropSocket while editing (selected or always visible).
    [DrawGizmo(GizmoType.Selected | GizmoType.Active | GizmoType.NonSelected)]
    private static void DrawPropSocketGizmos(PropSocket socket, GizmoType gizmoType)
    {
        var t = socket.transform;

        // Colors per socket type (optional)
        Color c = socket.type switch
        {
            PropSocketType.HallSideLeft => new Color(1f, 0.85f, 0.2f),  // amber
            PropSocketType.HallSideRight => new Color(1f, 0.85f, 0.2f),
            PropSocketType.RoomWall => new Color(0.2f, 0.9f, 1f),   // cyan
            PropSocketType.Endcap => new Color(1f, 0.4f, 0.4f),   // red
            _ => Color.yellow
        };

        Handles.zTest = UnityEngine.Rendering.CompareFunction.Always;
        Handles.color = c;

        // Arrow showing the forward (placement facing)
        float arrowSize = HandleUtility.GetHandleSize(t.position) * 0.6f;
        Handles.ArrowHandleCap(0, t.position, Quaternion.LookRotation(t.forward, Vector3.up), arrowSize, EventType.Repaint);

        // Small wire cube at the socket
        var up = t.up * (arrowSize * 0.15f);
        var right = t.right * (arrowSize * 0.15f);
        var fwd = t.forward * (arrowSize * 0.15f);
        Handles.DrawWireCube(t.position, new Vector3(right.magnitude, up.magnitude, fwd.magnitude) * 2f);

        // Label: e.g., "BOOKSHELF HERE" / "ENDCAP PROP HERE"
        string label = socket.type switch
        {
            PropSocketType.HallSideLeft => "BOOKSHELF HERE",
            PropSocketType.HallSideRight => "BOOKSHELF HERE",
            PropSocketType.RoomWall => "ROOM PROP HERE",
            PropSocketType.Endcap => "ENDCAP PROP HERE",
            _ => "PROP HERE"
        };

        // Offset label a bit upward for readability
        Handles.Label(t.position + Vector3.up * (arrowSize * 0.25f), label, EditorStyles.boldLabel);
    }
}
#endif
