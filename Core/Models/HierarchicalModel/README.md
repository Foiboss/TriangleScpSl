# HierarchicalModel (V3)

Extends `ApproximateModel` (V2) with **hierarchical parenting**: visible parallelograms can serve as parents for other visible parallelograms, creating deeper transform trees that eliminate invisible stretch primitives.

## How It Works

### Phase 1: V2 Stretch Clustering (same as ApproximateModel)

All parallelograms are initially built using the same `VectorPhiSolver` + `StretchSpatialIndex` pipeline as V2. Each parallelogram is parented to an invisible stretch primitive.

### Phase 2: Hierarchical Reparenting

After all parallelograms are built, a post-processing pass tries to reparent each parallelogram onto another visible parallelogram:

1. **Compute world matrices** for all parallelograms
2. **Sort by diagonal** (largest first) — larger quads make better parents
3. For each parallelogram (smallest first), try every larger parallelogram as a potential parent:
    - Compute `localMatrix = parentWorld^(-1) * childWorld`
    - Decompose into TRS (position, rotation, scale)
    - Reject if matrix contains excessive shear (non-orthogonal columns)
    - Measure world-space corner error of the reconstructed transform
    - Accept if error ≤ tolerance
4. **Remove unused stretches** — stretch primitives with no remaining children are destroyed

### Why This Saves Primitives

In V2, the hierarchy is:

```
BaseQuad → Stretch_1 → Quad_A, Quad_B, Quad_C
         → Stretch_2 → Quad_D, Quad_E
```

Total: 1 base + 2 stretches + 5 quads = 8 primitives

In V3, if Quad_B and Quad_C can be parented onto Quad_A:

```
BaseQuad → Stretch_1 → Quad_A → Quad_B
                               → Quad_C
         → Stretch_2 → Quad_D → Quad_E
```

If Stretch_2 loses all direct children (Quad_E reparented onto Quad_D):

```
BaseQuad → Stretch_1 → Quad_A → Quad_B
                               → Quad_C
                      → Quad_D → Quad_E
```

Wait — Quad_D is still under Stretch_2 initially. But if Quad_D can be parented onto Quad_A:

```
BaseQuad → Stretch_1 → Quad_A → Quad_B
                               → Quad_C
                               → Quad_D → Quad_E
```

Total: 1 base + 1 stretch + 5 quads = 7 primitives (saved 1 stretch)

On real models with hundreds of stretches, the savings compound significantly.

### Constraints

- **Max hierarchy depth: 4** — prevents floating-point error amplification in deep chains
- **Min parent diagonal: 0.05** — very small quads produce huge local scales for children
- **Shear threshold: 0.15** — rejects decompositions where the local matrix has non-orthogonal axes (would produce visible distortion)

## Files

| File                             | Description                                                  |
|----------------------------------|--------------------------------------------------------------|
| `HierarchicalModel.cs`           | Main class, state, factory methods, Destroy                  |
| `HierarchicalModel.Building.cs`  | Phase 1 (V2 clustering) + Phase 2 (hierarchical reparenting) |
| `HierarchicalModel.Snapshots.cs` | Immutable primitive snapshots for serialization              |
| `HierarchicalModel.Export.cs`    | ProjectMER schematic JSON export                             |

## API

```csharp
// Create (immediate build)
var model = HierarchicalModel.Create(parallelograms, primitives, position, accuracy: 0.001f);

// Create deferred (coroutine build)
var model = HierarchicalModel.CreateDeferred(parallelograms, primitives, position, accuracy: 0.001f);
yield return model.BuildTrianglesCoroutine(PrimitiveFlags.Visible, batchSize: 64);

// Stats
model.ReparentedCount   // how many quads were reparented onto other quads
model.StretchesSaved    // how many stretch primitives were eliminated

// Same interface as ApproximateModel
model.Position = pos;
model.Rotation = rot;
model.Scale = scale;
model.Color = color;
model.Destroy();
```
