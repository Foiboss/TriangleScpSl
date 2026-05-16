# HierarchicalModel (V3)

Extends `ApproximateModel` (V2) with **two-phase post-build optimization** that reduces the number of invisible stretch primitives:

1. **Stretch consolidation** — merges small stretches into nearby larger ones with relaxed tolerance
2. **Hierarchical parenting** — parents visible quads onto other visible quads, eliminating their stretch

## How It Works

### Phase 1: V2 Stretch Clustering (same as ApproximateModel)

All parallelograms are initially built using the same `VectorPhiSolver` + `StretchSpatialIndex` pipeline as V2. Each non-rectangular parallelogram is parented to an invisible stretch primitive.

### Phase 2a: Stretch Consolidation (main optimization)

After building, many stretches have only 1-2 children. Each such stretch costs 1 invisible primitive to support a small number of visible quads. Consolidation tries to move these children to nearby larger stretches:

1. Build a map of stretch → children
2. For each "small" stretch (up to 5 children), sorted by child count ascending:
    - For each child, find the best alternative stretch using `MaxVertexError` with **3x relaxed tolerance**
    - If ALL children can be reassigned, execute the reassignments and destroy the empty stretch
3. Each consolidation saves exactly 1 primitive (the eliminated stretch)

This trades a small amount of visual accuracy for measurable primitive reduction. The relaxed tolerance (3x `Accuracy`) keeps error bounded while enabling merges that the V2 strict tolerance wouldn't allow.

### Phase 2b: Hierarchical Parenting (secondary optimization)

After consolidation, try to reparent visible parallelograms onto other visible parallelograms:

1. For each parallelogram as potential child:
    - Transform child's 4 world corners into each candidate parent's local space
    - Check if the local-space shape is nearly rectangular (perpendicular edges)
    - If so, compute child's local TRS and verify world-space corner error
2. If a match is found (error within tolerance), reparent the child
3. If reparenting empties a stretch, destroy it

This optimization has limited applicability (requires compatible transform geometry) but provides additional savings on models with many similarly-oriented faces.

### Phase 2c: Cleanup

After optimization, all stretches with no remaining children are destroyed to free network bandwidth.

## Files

| File                             | Description                                                                  |
|----------------------------------|------------------------------------------------------------------------------|
| `HierarchicalModel.cs`           | Main class, state, factory methods, Destroy, QuadBuildInfo                   |
| `HierarchicalModel.Building.cs`  | Phase 1 (V2 clustering) + Phase 2 (consolidation + hierarchical reparenting) |
| `HierarchicalModel.Snapshots.cs` | Immutable primitive snapshots for serialization                              |
| `HierarchicalModel.Export.cs`    | ProjectMER schematic JSON export                                             |

## API

```csharp
// Create (immediate build)
var model = HierarchicalModel.Create(parallelograms, primitives, position, accuracy: 0.001f);

// Create deferred (coroutine build)
var model = HierarchicalModel.CreateDeferred(parallelograms, primitives, position, accuracy: 0.001f);
yield return model.BuildTrianglesCoroutine(PrimitiveFlags.Visible, batchSize: 64);

// Stats
model.ReparentedCount   // how many quads were reparented onto other quads
model.StretchesSaved    // how many stretch primitives were eliminated (consolidation + reparenting)

// Same interface as ApproximateModel
model.Position = pos;
model.Rotation = rot;
model.Scale = scale;
model.Color = color;
model.Destroy();
```
