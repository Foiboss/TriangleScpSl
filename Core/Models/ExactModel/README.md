# ExactModel (V1 Renderer)

Renders each parallelogram using its own primitive pair. Pixel-perfect but uses more primitives than ApproximateModel.

## How It Works

For each `ModelParallelogram`:

- **If rectangle** (`IsRectangle=true`, meaning `|VLeft| = |VUp|`): creates a single Quad with proper rotation and scale. The edges are computed as `VLeft + VUp` and `VLeft - VUp` (since VLeft/VUp are half-diagonals, not edges). Saves 1 primitive per rectangle.

- **If non-rectangle**: creates a 2-Quad `ParallelogramPrimitive` using the SetParent deformation trick (see `Core/Models/README.md`).

## Primitive Count

```
PrimitiveCount = ParallelogramPrimitives * 2 + Rectangles + NativePrimitives + NativeBases + 1
```

The `+1` is the invisible anchor Quad that serves as the model's transform root.

## Half-Diagonal Parametrization

`ModelParallelogram` stores `Center`, `VLeft`, `VUp` as **half-diagonals**, not edges:

- Corners: `Center + VLeft`, `Center + VUp`, `Center - VLeft`, `Center - VUp`
- Edges: `VLeft + VUp` and `VLeft - VUp`
- Rectangle condition: `|VLeft| = |VUp|` (equal-length diagonals make perpendicular edges)

## Files

| File                     | Content                                                                             |
|--------------------------|-------------------------------------------------------------------------------------|
| `ExactModel.cs`          | Constructors, properties, factory methods (`Create`/`CreateDeferred`), `Destroy`    |
| `ExactModel.Building.cs` | `BuildTriangles`, `BuildTrianglesCoroutine`, `CreateRectangle`, `GetParallelograms` |
| `ExactModel.Export.cs`   | `GetProjectMerBlocks` for ProjectMER schematic export                               |

Shared fields and methods (Position, Rotation, Scale, TransformPoint, BuildNativePrimitives, etc.) live in `ModelBase`.
