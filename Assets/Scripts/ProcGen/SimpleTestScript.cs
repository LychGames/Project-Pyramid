using UnityEngine;

public class SimpleTestScript : MonoBehaviour
{
    [Header("Test Settings")]
    [SerializeField] bool testOnStart = true;
    
    void Start()
    {
        if (testOnStart) TestSetup();
    }
    
    [ContextMenu("Test Setup")]
    public void TestSetup()
    {
        Debug.Log("=== SIMPLE TEST SCRIPT RUNNING ===");
        
        // Test DoorTrigger layer
        int layerIndex = LayerMask.NameToLayer("DoorTrigger");
        Debug.Log($"DoorTrigger layer index: {layerIndex}");
        
        if (layerIndex == -1)
        {
            Debug.LogError("DoorTrigger layer not found! Please create it in Project Settings > Tags and Layers");
        }
        else
        {
            Debug.Log($"DoorTrigger layer found at index {layerIndex}");
        }
        
        // Test if we can find the generator
        var generator = FindObjectOfType<SimpleLevelGenerator>();
        if (generator == null)
        {
            Debug.LogError("No SimpleLevelGenerator found in scene");
        }
        else
        {
            Debug.Log($"Found SimpleLevelGenerator: {generator.name}");
        }
        
        Debug.Log("=== TEST COMPLETE ===");
    }
}
