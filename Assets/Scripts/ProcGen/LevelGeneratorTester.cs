// Assets/Scripts/LevelGeneratorTester.cs
// Simple test script to debug level generator issues

using UnityEngine;

public class LevelGeneratorTester : MonoBehaviour
{
    [Header("Test Settings")]
    [SerializeField] bool testOnStart = false;
    [SerializeField] bool testDoorTriggerLayer = true;
    [SerializeField] bool testPrefabValidation = true;
    
    [Header("Test Prefabs")]
    [SerializeField] GameObject testPrefab;
    [SerializeField] Transform testAnchor;

    void Start()
    {
        if (testOnStart) RunTests();
    }

    [ContextMenu("Run All Tests")]
    public void RunTests()
    {
        Debug.Log("=== RUNNING LEVEL GENERATOR TESTS ===");
        
        if (testDoorTriggerLayer) TestDoorTriggerLayer();
        if (testPrefabValidation) TestPrefabValidation();
        
        Debug.Log("=== TESTS COMPLETE ===");
    }

    void TestDoorTriggerLayer()
    {
        Debug.Log("--- Testing DoorTrigger Layer ---");
        
        int layerIndex = LayerMask.NameToLayer("DoorTrigger");
        Debug.Log($"DoorTrigger layer index: {layerIndex}");
        
        if (layerIndex == -1)
        {
            Debug.LogError("DoorTrigger layer not found! Please create it in Project Settings > Tags and Layers");
        }
        else
        {
            Debug.Log($"DoorTrigger layer found at index {layerIndex}");
            LayerMask mask = LayerMask.GetMask("DoorTrigger");
            Debug.Log($"DoorTrigger layer mask: {mask.value}");
        }
    }

    void TestPrefabValidation()
    {
        Debug.Log("--- Testing Prefab Validation ---");
        
        if (testPrefab == null)
        {
            Debug.LogWarning("No test prefab assigned");
            return;
        }
        
        Debug.Log($"Testing prefab: {testPrefab.name}");
        
        // Check for DoorAnchor components
        var anchors = testPrefab.GetComponentsInChildren<DoorAnchor>(true);
        Debug.Log($"Found {anchors.Length} DoorAnchor components");
        
        foreach (var anchor in anchors)
        {
            Debug.Log($"  Anchor: {anchor.name}");
            
            // Check if it has a collider
            var collider = anchor.GetComponent<Collider>();
            if (collider != null)
            {
                Debug.Log($"    Has collider: {collider.GetType().Name}, layer: {anchor.gameObject.layer}");
            }
            else
            {
                Debug.LogWarning($"    No collider found on {anchor.name}");
            }
        }
        
        // Check for RoomMeta
        var meta = testPrefab.GetComponent<RoomMeta>();
        if (meta != null)
        {
            Debug.Log($"RoomMeta: category={meta.category}, weight={meta.weight}");
        }
        else
        {
            Debug.Log("No RoomMeta component found");
        }
    }

    [ContextMenu("Test Single Placement")]
    public void TestSinglePlacement()
    {
        if (testPrefab == null || testAnchor == null)
        {
            Debug.LogError("Both testPrefab and testAnchor must be assigned");
            return;
        }
        
        Debug.Log($"Testing placement of {testPrefab.name} on {testAnchor.name}");
        
        // Create a temporary instance (just to ensure prefab is valid in-scene)
        var instance = Instantiate(testPrefab);
        instance.name = "TestInstance_TEMP";
        
        // Get the generator component
        var generator = FindObjectOfType<SimpleLevelGenerator>();
        if (generator == null)
        {
            Debug.LogError("No SimpleLevelGenerator found in scene");
#if UNITY_EDITOR
            DestroyImmediate(instance);
#else
            Destroy(instance);
#endif
            return;
        }
        
        // Try to place it (generator will instantiate its own copy; we destroy ours)
        bool success = generator.TryPlaceOneOf(new GameObject[] { testPrefab }, testAnchor, out Transform placed);
        
        if (success)
        {
            Debug.Log($"SUCCESS: {testPrefab.name} placed successfully");
        }
        else
        {
            Debug.LogError($"FAILED: Could not place {testPrefab.name}");
        }
        
#if UNITY_EDITOR
        DestroyImmediate(instance);
#else
        Destroy(instance);
#endif
    }
}
