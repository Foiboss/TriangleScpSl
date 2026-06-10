# ApproximateModel (V2 Renderer)

Renders parallelograms by sharing "stretch" primitives across angular-similar shapes, reducing total primitive count at the cost of small configurable vertex error.

## How It Works

### The Stretch Clustering Algorithm

A parallelogram's shape can be described by two angles `(theta, phi)` derived from its half-diagonal vectors VLeft and VUp. `VectorPhiSolver` computes these angles.

A **stretch primitive** is an invisible Quad with:

- Rotation `R(theta)` around Z
- Scale `(cos(phi) * F, sin(phi) * F, 1)` where `F = 2`

When a visible child Quad is placed under a stretch via `SetParent`, it inherits the stretch's deformation. Multiple parallelograms with similar `(theta, phi)` can share the same stretch, so instead of 2 Quads per parallelogram (V1), the cost becomes:

```
1 shared stretch + 1 visible Quad per parallelogram in the group
```

### Accuracy Control

`StretchSpatialIndex` maintains a 2D spatial hash over `(theta, phi)` space. Before creating a new stretch, the model queries nearby cells and measures the worst-case vertex error from reusing each candidate. If the error is within `absoluteToleranceUnits`, the existing stretch is reused.

Lower accuracy values = more stretches but higher fidelity. Higher values = fewer primitives but visible vertex displacement.

### Rectangle Optimization

When `IsRectangle=true` (equal-length half-diagonals), a single Quad is created directly with proper rotation and scale. No stretch needed.

### Fallback

When `VectorPhiSolver` cannot find a valid `(theta, phi)` decomposition, the parallelogram falls back to a 2-Quad `ParallelogramPrimitive` (same as V1).

## Primitive Count

```
PrimitiveCount = Stretches + Parallelograms + Fallbacks * 2 + NativePrimitives + 1
```

## Key Algorithms

- **VectorPhiSolver**: Analytically decomposes VLeft/VUp into `(theta, phi)` angles. Uses the relationship between the half-diagonal vectors and the stretch transform to find angles where both vectors map to equal-length vectors in the stretch frame.
- **StretchSpatialIndex**: 2D spatial hash with configurable cell sizes for fast nearest-neighbor lookup in angle space. Cell widths are derived from the accuracy tolerance and maximum parallelogram size.

## Files

| File                            | Content                                                                                                                                |
|---------------------------------|----------------------------------------------------------------------------------------------------------------------------------------|
| `ApproximateModel.cs`           | Constructors, properties, factory methods (`Create`/`CreateDeferred`), `Destroy`, inner snapshot classes                               |
| `ApproximateModel.Building.cs`  | `BuildTriangles`, `BuildTrianglesCoroutine`, `CreateRectangle`, `CreateParallelogram`, `CreateTriangle`, `ComputeMaxParallelogramSize` |
| `ApproximateModel.Snapshots.cs` | `GetTriangleSnapshot`, `GetParallelogramSnapshot`, `GetPrimitiveSnapshot` for inspection/export                                        |
| `ApproximateModel.Export.cs`    | `GetProjectMerBlocks` for ProjectMER schematic export                                                                                  |
| `VectorPhiSolver.cs`            | Converts VLeft/VUp vectors to `(theta, phi)` angle pairs                                                                               |
| `StretchSpatialIndex.cs`        | 2D spatial hash for fast stretch lookup by angle                                                                                       |
| `ApproximateModelUtils.cs`      | Creates stretch primitives, forward/inverse transforms between stretch and world space                                                 |

Shared fields and methods (Position, Rotation, Scale, TransformPoint, BuildNativePrimitives, etc.) live in `ModelBase`.
