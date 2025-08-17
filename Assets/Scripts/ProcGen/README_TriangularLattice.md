# Triangular Lattice Level Generator - Setup Guide

## Overview
This system generates procedural levels on a 60° triangular/hexagonal lattice instead of a square grid. It ensures proper alignment, prevents overlaps, and automatically places door blockers on unused anchors.

## Key Features
- **Triangular Lattice**: Snaps positions to hexagonal grid coordinates
- **30° Rotation Snapping**: Aligns all modules to equilateral triangle orientations
- **Smart Overlap Detection**: Uses DoorTrigger layer colliders to prevent door overlaps
- **Automatic Door Blocking**: Seals unused anchors with blockers
- **Lattice-Aware Placement**: Ensures clean triangular grid alignment

## Setup Instructions

### 1. Layer Setup
Create a new layer called "DoorTrigger" in Unity:
1. Go to **Edit > Project Settings > Tags and Layers**
2. Add "DoorTrigger" to an empty User Layer slot (e.g., Layer 8)
3. This layer will be used for door collision detection

### 2. DoorAnchor Component Setup
Each DoorAnchor automatically gets a BoxCollider:
- **Collider Size**: Default (0.5, 2, 0.1) - adjust for your doorway size
- **Collider Center**: Default (0,0,0) - center on the anchor
- **Layer**: Automatically set to DoorTrigger
- **Is Trigger**: Always true for overlap detection

### 3. Door Blocker Prefab
Create a door blocker prefab:
1. Create a simple cube or wall piece
2. Add the `DoorBlocker` component
3. Adjust size to match your doorway dimensions
4. Assign to `doorBlockerPrefab` in SimpleLevelGenerator

### 4. Generator Configuration
In SimpleLevelGenerator inspector:
- **Cell Size**: Set to match your prefab width (default: 2.5)
- **Snap Yaw To 30**: Enable for proper triangular alignment
- **Snap Pos To Lattice**: Enable for hexagonal grid positioning
- **Door Trigger Mask**: Set to DoorTrigger layer
- **Placement Collision Mask**: Set to LevelGeo layer

## How It Works

### Lattice System
- **Basis Vectors**: Uses 0° and 60° vectors for hexagonal grid
- **Position Snapping**: Converts world coordinates to lattice coordinates
- **Rotation Snapping**: Rounds Y rotation to 30° increments

### Placement Process
1. **Anchor Alignment**: Snaps module entry anchor to target anchor
2. **Lattice Snapping**: Applies position and rotation snapping
3. **Overlap Check**: Tests DoorTrigger layer collisions
4. **Volume Check**: Tests LevelGeo layer collisions
5. **Placement**: If all checks pass, places the module

### Door Blocker Placement
After generation completes:
1. Scans all remaining DoorAnchors
2. Checks if anchor faces empty space
3. Places door blocker with proper lattice alignment
4. Names blockers for easy identification

## Troubleshooting

### Common Issues

**Modules Not Aligning**
- Check that `cellSize` matches your prefab dimensions
- Ensure `snapYawTo30` and `snapPosToLattice` are enabled
- Verify DoorAnchor forward direction points outward

**Overlap Detection Not Working**
- Confirm DoorTrigger layer exists and is assigned
- Check that DoorAnchors have BoxColliders
- Verify `doorTriggerLayer` mask is set correctly

**Door Blockers Misaligned**
- Ensure doorBlockerPrefab has DoorBlocker component
- Check that blocker size matches doorway dimensions
- Verify lattice snapping is enabled

**Performance Issues**
- Reduce `maxPlacements` for smaller levels
- Disable verbose logging in production
- Consider reducing overlap check frequency

### Debug Features
- **Gizmos**: DoorAnchors show blue arrows, DoorBlockers show orange cubes
- **Verbose Selection**: Logs placement attempts and rejections
- **Log Steps**: Tracks generation progress
- **Collider Visualization**: Shows DoorTrigger collider bounds

## Advanced Usage

### Custom Lattice Sizes
Modify `cellSize` to match different prefab dimensions:
- Small rooms: 1.5-2.0
- Standard rooms: 2.5-3.0
- Large rooms: 4.0+

### Cross-Connections
Enable `allowPortalOverlap` to allow intentional door overlaps for cross-connections.

### Weighted Prefab Selection
Use RoomMeta components on prefabs to control placement frequency:
- Higher weight = more likely to be selected
- Weight <= 0 = never selected

### Virtual Start Room
Enable `startIsVirtual` to test without a start room prefab. Creates three anchors at 0°, -60°, and +60°.

## Example Workflow
1. Set up DoorTrigger layer
2. Configure DoorAnchors on all prefabs
3. Create door blocker prefab
4. Assign prefabs to generator arrays
5. Set cell size and enable snapping
6. Run generation
7. Review results and adjust parameters

## Technical Notes
- Uses Physics.OverlapBox for precise collision detection
- Implements weighted random selection for variety
- Supports both virtual and physical start rooms
- Automatically handles anchor collection and management
- Maintains clean triangular grid alignment throughout generation
