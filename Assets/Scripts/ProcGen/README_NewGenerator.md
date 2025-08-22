# NEW Simple Level Generator

This is a completely rewritten procedural generation script that addresses all the issues with the previous system.

## **What This Fixes:**

### ✅ **Basement Placement Issues:**
- **NO MORE ground level restrictions** - basements can spawn anywhere
- **Proper room type validation** - basements only connect to basement stairs or summoning rooms
- **Smart basement placement** - automatically finds basement stairs nearby

### ✅ **DoorAnchor Connection Issues:**
- **IGNORES restrictive DoorAnchor settings** - no more connection failures
- **Simple anchor detection** - finds any transform with "door" or "anchor" in the name
- **No more filter mode restrictions** - everything can connect to everything

### ✅ **Vertical Expansion Problems:**
- **Guaranteed second-floor expansion** - forces stair room placement when needed
- **Smart anchor prioritization** - second-floor anchors get priority
- **Configurable thresholds** - easy to adjust when "second floor" starts

### ✅ **Generation Stopping Issues:**
- **No artificial limits** - generation continues until no more anchors available
- **Fallback systems** - always finds a way to continue
- **Safety iteration limits** - prevents infinite loops

## **How to Use:**

### 1. **Setup:**
- Add this script to a GameObject in your scene
- Assign your prefab arrays (hallways, connectors, rooms, etc.)
- Set `levelRoot` to a parent Transform for organization

### 2. **Configuration:**
- **Room Type Weights**: Control how often each type spawns
- **Vertical Expansion**: Adjust second-floor thresholds and chances
- **Overlap Prevention**: Set minimum distances between modules
- **Max Modules**: Set the maximum number of modules to generate

### 3. **Context Menu Options:**
- **Configure for Vertical Expansion**: Maximizes upward growth
- **Configure for Basement Focus**: Prioritizes basement generation
- **Configure for Balanced Generation**: Balanced mix of all types

### 4. **Testing:**
- Press **SPACEBAR** to regenerate the level
- Check console for detailed generation logs
- Monitor statistics (success rate, room counts, etc.)

## **Key Features:**

### **Smart Module Selection:**
- Forces vertical expansion when needed
- Forces basement placement when appropriate
- Weighted random selection for variety
- Fallback to hallways if nothing else works

### **Overlap Prevention:**
- Simple distance-based overlap detection
- Configurable minimum distances
- Prevents modules from spawning on top of each other

### **Vertical Expansion:**
- Automatically detects second-floor anchors
- Forces stair room placement for upward growth
- Tracks second-floor room counts
- Configurable expansion chances

### **Basement System:**
- Tracks basement and basement stairs placement
- Forces basement placement when needed
- Only places basements near basement stairs
- No ground level restrictions

## **What This Generator Does:**

1. **Starts with a triangle room** at the origin
2. **Finds all available anchors** from placed modules
3. **Chooses module types** based on weights and needs
4. **Places modules** with proper positioning and rotation
5. **Updates available anchors** for the next iteration
6. **Continues until** no more anchors or max modules reached
7. **Forces expansion** when needed (vertical, basements)

## **No More Problems:**

- ❌ No more DoorAnchor restriction failures
- ❌ No more ground level basement restrictions  
- ❌ No more generation stopping at 4 modules
- ❌ No more basements on hallways
- ❌ No more missing second-floor expansion
- ❌ No more complex, broken logic

## **Simple and Reliable:**

This generator is designed to be:
- **Simple**: Easy to understand and modify
- **Reliable**: Always finds a way to continue generation
- **Flexible**: Easy to configure for different needs
- **Debug-friendly**: Detailed logging and statistics
- **Fast**: No complex validation or restriction checks

## **Migration:**

To use this instead of the old generator:
1. **Disable** the old `SimpleLevelGenerator` script
2. **Enable** this new `SimpleLevelGenerator_New` script
3. **Assign your prefab arrays** in the inspector
4. **Test generation** with spacebar
5. **Adjust settings** as needed

The new generator will work with your existing prefabs and RoomMeta components without any changes needed.
