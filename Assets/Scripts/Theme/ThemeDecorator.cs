using System.Collections.Generic;
using UnityEngine;

public class ThemeDecorator : MonoBehaviour
{
    [Header("Theme")]
    [SerializeField] private ThemeProfile theme;

    [Header("Collision/clearance for props")]
    [SerializeField] private LayerMask solidMask;   // only solid level geometry layers

    /// <summary>Call this after level generation (host side in multiplayer).</summary>
    public void ApplyThemeAndDecor(GameObject levelRoot)
    {
        if (!theme)
        {
            Debug.LogWarning("ThemeDecorator: missing ThemeProfile");
            return;
        }

        // 1) Material swap by placeholder name tags (case-insensitive)
        var renderers = levelRoot.GetComponentsInChildren<Renderer>(true);
        string wallTag = theme.wallTag.ToLowerInvariant();
        string floorTag = theme.floorTag.ToLowerInvariant();
        string ceilingTag = theme.ceilingTag.ToLowerInvariant();

        for (int i = 0; i < renderers.Length; i++)
        {
            var r = renderers[i];
            // Supports multi-submesh
            var mats = r.sharedMaterials;
            bool changedAny = false;

            for (int m = 0; m < mats.Length; m++)
            {
                var mat = mats[m];
                if (mat == null) continue;

                string nameL = mat.name.ToLowerInvariant();

                if (nameL.Contains(wallTag) && theme.wallMat)
                {
                    mats[m] = theme.wallMat;
                    changedAny = true;
                }
                else if (nameL.Contains(floorTag) && theme.floorMat)
                {
                    mats[m] = theme.floorMat;
                    changedAny = true;
                }
                else if (nameL.Contains(ceilingTag) && theme.ceilingMat)
                {
                    mats[m] = theme.ceilingMat;
                    changedAny = true;
                }
            }

            if (changedAny)
                r.sharedMaterials = mats; // assign once to avoid instancing spam
        }

        // 2) Prop spawning via sockets (lightweight, deterministic if you seed RNG)
        var sockets = levelRoot.GetComponentsInChildren<PropSocket>(true);
        var lastSpawnAtParent = new Dictionary<Transform, Vector3>(64);

        for (int i = 0; i < sockets.Length; i++)
        {
            var s = sockets[i];
            switch (s.type)
            {
                case PropSocketType.HallSideLeft:
                case PropSocketType.HallSideRight:
                    {
                        if (!CanSpawn(theme.hallSideBookshelves)) break;
                        if (Random.value > theme.shelfSpawnChance) break;
                        if (!HasClearance(s.transform.position, s.transform.forward)) break;

                        Transform parent = s.transform.parent ? s.transform.parent : s.transform;
                        if (TooCloseToLast(parent, s.transform.position, theme.minShelfSpacing, lastSpawnAtParent)) break;

                        var prefab = Pick(theme.hallSideBookshelves);
                        Instantiate(prefab, s.transform.position,
                                    Quaternion.LookRotation(s.transform.forward, Vector3.up),
                                    parent);
                        lastSpawnAtParent[parent] = s.transform.position;
                        break;
                    }

                case PropSocketType.RoomWall:
                    {
                        if (!CanSpawn(theme.roomBookshelves)) break;
                        if (Random.value > theme.shelfSpawnChance) break;
                        if (!HasClearance(s.transform.position, s.transform.forward)) break;

                        var prefab = Pick(theme.roomBookshelves);
                        Instantiate(prefab, s.transform.position,
                                    Quaternion.LookRotation(s.transform.forward, Vector3.up),
                                    s.transform.parent);
                        break;
                    }

                case PropSocketType.Endcap:
                    {
                        if (!CanSpawn(theme.smallDressings)) break;
                        if (Random.value > 0.5f)
                        {
                            var prefab = Pick(theme.smallDressings);
                            Instantiate(prefab, s.transform.position,
                                        Quaternion.LookRotation(s.transform.forward, Vector3.up),
                                        s.transform.parent);
                        }
                        break;
                    }
            }
        }
    }

    private static bool CanSpawn(List<GameObject> list) => list != null && list.Count > 0;

    private static GameObject Pick(List<GameObject> list) =>
        list[Random.Range(0, list.Count)];

    private bool TooCloseToLast(Transform parent, Vector3 pos, float minSpacing, Dictionary<Transform, Vector3> map)
    {
        if (!map.TryGetValue(parent, out var last)) return false;
        return (pos - last).sqrMagnitude < (minSpacing * minSpacing);
    }

    private bool HasClearance(Vector3 pos, Vector3 forward)
    {
        // Push outward a bit and ensure no solid geometry immediately in front
        return !Physics.CheckSphere(pos + forward * 0.25f,
                                    theme.navClearance * 0.5f,
                                    solidMask,
                                    QueryTriggerInteraction.Ignore);
    }
}
