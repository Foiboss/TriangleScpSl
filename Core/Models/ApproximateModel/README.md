# ApproximateModel

Renders parallelograms by sharing "stretch" primitives across angular-similar shapes.

## How It Works

A parallelogram is defined by two half-diagonal vectors (VLeft, VUp). These vectors can be decomposed into an angle pair (theta, phi) that describes the parallelogram's shape. Parallelograms with similar angles can share a single stretch primitive, reducing the total primitive count at the cost of
small vertex error.

## Files

| File                            | Content                                                                                       |
|---------------------------------|-----------------------------------------------------------------------------------------------|
| `ApproximateModel.cs`           | Constructors, properties, factory methods, Destroy                                            |
| `ApproximateModel.Building.cs`  | BuildTriangles, BuildTrianglesCoroutine, CreateRectangle, CreateParallelogram, CreateTriangle |
| `ApproximateModel.Snapshots.cs` | GetTriangleSnapshot, GetParallelogramSnapshot, GetPrimitiveSnapshot                           |
| `ApproximateModel.Export.cs`    | GetProjectMerBlocks for ProjectMER export                                                     |
| `VectorPhiSolver.cs`            | Converts VLeft/VUp vectors to (theta, phi) angle pairs                                        |
| `StretchSpatialIndex.cs`        | Spatial hash for fast stretch lookup by angle                                                 |
| `ApproximateModelUtils.cs`      | Creates stretch primitives, forward/inverse transforms                                        |

Shared fields/methods (Position, Rotation, Scale, TransformPoint, BuildNativePrimitives, etc.) live in `ModelBase`.

## Key Concepts

- **Stretch primitive**: An invisible Quad with non-uniform scale encoding a specific (theta, phi). Child quads inherit this deformation via SetParent.
- **Accuracy parameter**: Maximum vertex error in world units. Smaller = more stretches but more accurate.
- **Rectangle optimization**: When `IsRectangle=true`, a single Quad primitive is used directly (no stretch needed).
- **Fallback parallelogram**: When VectorPhiSolver can't find a valid (theta, phi), falls back to 2-primitive ParallelogramPrimitive.
