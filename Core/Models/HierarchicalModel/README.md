# HierarchicalModel (V3)

Extends `ApproximateModel` (V2) with hierarchical parenting and post-build stretch optimization that reduce the number of invisible stretch primitives:

1. **Hierarchical parenting** - parents visible quads onto other visible quads, eliminating their stretch
2. **Stretch consolidation** - drains sparsely-used stretches onto other stretches and destroys them

## How It Works

### Phase 1: Inline Parenting + V2 Stretch Clustering

For each new parallelogram, the builder first tries to parent it directly under an
already-built, stretch-free visible quad (`TryCreateUnderParent`) — that costs 1 quad and
no stretch at all. If no visible quad fits, it falls back to the same `VectorPhiSolver` +
`StretchSpatialIndex` pipeline as V2.

**Stretch matching is two-tier:** stretches near the parallelogram's own `(theta, phi)`
solution are checked first; on a miss, ALL existing stretches are scanned. A parallelogram
has a whole curve of valid `(theta, phi)` decompositions, so a stretch far from this
particular solution point can still render it within tolerance.

### Phase 2: Optimization Sweeps

Iteratively moves stretch-children onto visible quads:

1. Sweep 0: full scan - every stretch-child checks every stretch-free quad as a parent
2. Sweep 1+: only checks quads that were newly reparented or became parents in the previous sweep
3. Reparenting preserves world position; only parent pointers and local TRS change

Controlled by the `HierarchicalOptimizationPasses` config value (default 3).

### Phase 3: Stretch Consolidation

After the sweeps, sparsely-used stretches are drained, smallest first: each remaining
child quad is rehomed onto another stretch using `MaxVertexError` with the same strict
`Accuracy` tolerance (no extra visual error). If ALL children of a stretch can be
rehomed, the stretch ends up childless and is destroyed — one primitive saved each.

This cleans up the orphans that build order creates: an early parallelogram spawns its
own stretch before the popular stretch that would also have fit it exists.

### Cleanup

All stretches with no remaining children are destroyed to free network bandwidth.

## Files

| File                                      | Description                                                       |
|-------------------------------------------|-------------------------------------------------------------------|
| `HierarchicalModel.cs`                     | Main class, state, constructors, Destroy, QuadBuildInfo           |
| `HierarchicalModel.Building.cs`            | Build orchestration (immediate + coroutine)                       |
| `HierarchicalModel.Building.Phase1.cs`     | Inline parenting + V2 stretch clustering                          |
| `HierarchicalModel.Building.Phase2.cs`     | Optimization sweeps + stretch consolidation                       |
| `HierarchicalModel.Building.Helpers.cs`    | Fit-under-quad math, used-stretch tracking, size computation      |
| `HierarchicalModel.Snapshots.cs`           | Immutable primitive snapshots for serialization                   |
| `HierarchicalModel.Export.cs`              | ProjectMER schematic JSON export                                  |

## API

```csharp
// Create (deferred coroutine build)
var model = new HierarchicalModel(parallelograms, primitives, position, optimizationPasses: 3);
yield return model.BuildTrianglesCoroutine(PrimitiveFlags.Visible, batchSize: 64);

// Stats
model.ReparentedCount   // how many quads were reparented onto other quads
model.StretchesSaved    // how many stretch primitives were eliminated (reparenting + consolidation)

// Same interface as ApproximateModel
model.Position = pos;
model.Rotation = rot;
model.Scale = scale;
model.Color = color;
model.Destroy();
```
