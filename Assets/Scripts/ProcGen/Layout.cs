// Assets/Scripts/ProcGen/LayoutTheme.cs
using UnityEngine;

[CreateAssetMenu(menuName = "ProcGen/Layout Theme", fileName = "NewLayoutTheme")]
public class LayoutTheme : ScriptableObject
{
    public RoomMeta[] halls;
    public RoomMeta[] smallRooms;
    public RoomMeta[] mediumRooms;
    public RoomMeta[] largeRooms;
    public RoomMeta[] adapters;

    [Header("Knobs")]
    public int spineLength = 20;
    public int maxBranch = 3;
    [Range(0f, 1f)] public float roomDensity = 0.35f;
    [Range(0f, 1f)] public float loopBias = 0.25f;
    public int[] preferredWidths = new int[] { 10 };
    public bool allowVertical = false;
}
