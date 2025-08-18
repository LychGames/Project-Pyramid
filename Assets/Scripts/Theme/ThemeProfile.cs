using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(menuName = "Theme/ThemeProfile")]
public class ThemeProfile : ScriptableObject
{
    [Header("Surface Materials (final look)")]
    public Material wallMat;
    public Material floorMat;
    public Material ceilingMat;

    [Header("Material name tags (match, case-insensitive)")]
    public string wallTag = "Wall";       // e.g., "Wall_Placeholder"
    public string floorTag = "Floor";     // e.g., "Floor_Placeholder"
    public string ceilingTag = "Ceiling"; // e.g., "Ceiling_Placeholder"

    [Header("Prop Pools")]
    public List<GameObject> hallSideBookshelves;
    public List<GameObject> roomBookshelves;
    public List<GameObject> smallDressings;

    [Header("Spawn Settings")]
    [Range(0f, 1f)] public float shelfSpawnChance = 0.65f;
    [Range(0.5f, 6f)] public float minShelfSpacing = 2.0f;
    public float navClearance = 0.6f; // meters
}
