# Triangulation

Primitives and the geometry needed to render triangles and parallelograms using Unity Quads.

## Subfolders

### Triangle/

Data structures and logic for decomposing a triangle into 3 parallelograms.

**The decomposition:** Given triangle vertices P1, P2, P3, `TriangleParallelogramBuilder.GetParallelogramsInfo` computes three parallelogram coverings by constructing parallelograms from each edge. Each parallelogram shares one edge with the triangle and a portion of the triangle's area.

| File                              | Content                                                                                                          |
|-----------------------------------|------------------------------------------------------------------------------------------------------------------|
| `ModelTriangle.cs`                | Immutable data: P1, P2, P3, Color                                                                                |
| `TrianglePrimitive.cs`            | Renderable triangle built from 3 `ParallelogramPrimitive` instances. Supports rebuild, move, color/flag changes. |
| `TriangleParallelogramBuilder.cs` | Computes the 3 parallelogram coverings for a triangle                                                            |

### Parallelogram/

Data structures and the 2-Quad shearing primitive for rendering arbitrary parallelograms.

**The shearing construction:** A parallelogram with half-diagonals VUp and VLeft is rendered by:

1. Decomposing VLeft into components along and perpendicular to VUp
2. Computing inner rectangle dimensions `a, b` and shear factor `x` from the diagonal geometry
3. Placing a base Quad at the center with shear-encoded scale
4. Attaching a visible child Quad with local rotation and scale `(b, a, 1)` to produce the final shape

This is the fundamental building block that makes arbitrary polygon rendering possible with only Unity Quads.

| File                        | Content                                                                                                                                 |
|-----------------------------|-----------------------------------------------------------------------------------------------------------------------------------------|
| `ModelParallelogram.cs`     | Data: VUp, VLeft, Center, Color, IsRectangle. Uses half-diagonal parametrization (corners are `Center +/- VLeft` and `Center +/- VUp`). |
| `ParallelogramPrimitive.cs` | Renderable parallelogram: 2-Quad shearing construction. `Create`, `Rebuild`, `Destroy`.                                                 |
| `ParallelogramHelpUtils.cs` | Utility functions for parallelogram corner calculations                                                                                 |
