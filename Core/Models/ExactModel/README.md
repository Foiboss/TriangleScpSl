# ExactModel

Renders each parallelogram using its own 2-primitive ParallelogramPrimitive pair. Pixel-perfect but uses more primitives than ApproximateModel.

## Files

| File                     | Content                                                                     |
|--------------------------|-----------------------------------------------------------------------------|
| `ExactModel.cs`          | Constructors, properties, factory methods, Destroy                          |
| `ExactModel.Building.cs` | BuildTriangles, BuildTrianglesCoroutine, CreateRectangle, GetParallelograms |
| `ExactModel.Export.cs`   | GetProjectMerBlocks for ProjectMER export                                   |

Shared fields/methods (Position, Rotation, Scale, TransformPoint, BuildNativePrimitives, etc.) live in `ModelBase`.

## Rectangle Optimization

When `IsRectangle=true` on a ModelParallelogram, ExactModel creates a single Quad with proper rotation and scale instead of a 2-primitive ParallelogramPrimitive. This saves 1 primitive per rectangle.
