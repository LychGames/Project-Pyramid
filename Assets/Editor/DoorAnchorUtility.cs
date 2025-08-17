// Bulk tools for DoorAnchor setup in the editor
#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class DoorAnchorUtility
{
    [MenuItem("Tools/Door Anchors/Disable Auto-Manage On All Prefabs")] 
    public static void DisableAutoManageOnAllPrefabs()
    {
        string[] guids = AssetDatabase.FindAssets("t:Prefab");
        int modifiedCount = 0;

        for (int i = 0; i < guids.Length; i++)
        {
            string path = AssetDatabase.GUIDToAssetPath(guids[i]);
            var root = PrefabUtility.LoadPrefabContents(path);
            if (root == null) continue;
            bool changed = false;

            var anchors = root.GetComponentsInChildren<DoorAnchor>(true);
            foreach (var a in anchors)
            {
                var so = new SerializedObject(a);
                var prop = so.FindProperty("autoManageCollider");
                if (prop != null && prop.boolValue != false)
                {
                    prop.boolValue = false;
                    so.ApplyModifiedPropertiesWithoutUndo();
                    changed = true;
                }
            }

            if (changed)
            {
                modifiedCount++;
                PrefabUtility.SaveAsPrefabAsset(root, path);
            }
            PrefabUtility.UnloadPrefabContents(root);
        }

        Debug.Log($"DoorAnchorUtility: Disabled auto-manage on {modifiedCount} prefab(s).");
    }

    [MenuItem("Tools/Door Anchors/Enable Auto-Manage On All Prefabs")] 
    public static void EnableAutoManageOnAllPrefabs()
    {
        string[] guids = AssetDatabase.FindAssets("t:Prefab");
        int modifiedCount = 0;

        for (int i = 0; i < guids.Length; i++)
        {
            string path = AssetDatabase.GUIDToAssetPath(guids[i]);
            var root = PrefabUtility.LoadPrefabContents(path);
            if (root == null) continue;
            bool changed = false;

            var anchors = root.GetComponentsInChildren<DoorAnchor>(true);
            foreach (var a in anchors)
            {
                var so = new SerializedObject(a);
                var prop = so.FindProperty("autoManageCollider");
                if (prop != null && prop.boolValue != true)
                {
                    prop.boolValue = true;
                    so.ApplyModifiedPropertiesWithoutUndo();
                    changed = true;
                }
            }

            if (changed)
            {
                modifiedCount++;
                PrefabUtility.SaveAsPrefabAsset(root, path);
            }
            PrefabUtility.UnloadPrefabContents(root);
        }

        Debug.Log($"DoorAnchorUtility: Enabled auto-manage on {modifiedCount} prefab(s).");
    }

    [MenuItem("Tools/Door Anchors/Disable Auto-Manage In Open Scenes")] 
    public static void DisableAutoManageInOpenScenes()
    {
        int modified = 0;
        for (int s = 0; s < SceneManager.sceneCount; s++)
        {
            var scene = SceneManager.GetSceneAt(s);
            if (!scene.isLoaded) continue;
            bool changed = false;
            foreach (var root in scene.GetRootGameObjects())
            {
                var anchors = root.GetComponentsInChildren<DoorAnchor>(true);
                foreach (var a in anchors)
                {
                    var so = new SerializedObject(a);
                    var prop = so.FindProperty("autoManageCollider");
                    if (prop != null && prop.boolValue != false)
                    {
                        prop.boolValue = false;
                        so.ApplyModifiedPropertiesWithoutUndo();
                        changed = true;
                    }
                }
            }
            if (changed)
            {
                modified++;
                EditorSceneManager.MarkSceneDirty(scene);
            }
        }
        Debug.Log($"DoorAnchorUtility: Disabled auto-manage in {modified} open scene(s). Don’t forget to save.");
    }
}
#endif


