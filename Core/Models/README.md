# Models

Manages collections of Unity primitives that represent a 3D model in the game world.

## Architecture

All models inherit from `ModelBase`, which provides:

- **Transform state:** Position, Rotation, Scale (backed by an invisible anchor Quad)
- **Coordinate conversion:** `TransformPoint` / `InverseTransformPoint`
- **Native primitive management:** `BuildNativePrimitives` / `DestroyNativePrimitives` for detected shapes (spheres, cylinders, cubes)
- **Shared fields:** destruction state, winding inversion, primitive flags

Three concrete implementations exist:

### ExactModel (V1)

Each parallelogram is rendered with its own 2-Quad `ParallelogramPrimitive` (or 1 Quad if it's a rectangle). Pixel-perfect, but uses more primitives.

See `ExactModel/README.md`.

### ApproximateModel (V2)

Groups similarly-oriented parallelograms under shared "stretch" primitives via angular clustering. Fewer total primitives at the cost of configurable vertex error.

See `ApproximateModel/README.md`.

### HierarchicalModel (V3)

Extends V2 with hierarchical parenting: visible parallelograms can serve as parents for other visible parallelograms, eliminating invisible stretch primitives. Lowest primitive count of all three models.

See `HierarchicalModel/README.md`.

## The SetParent Deformation Trick

Both models use Unity's `Transform.SetParent` to create non-uniform deformations:

1. Create an invisible parent primitive with the desired non-uniform scale
2. Create a visible child primitive at identity transform
3. The child inherits the parent's scale, producing the deformed shape

This trick is used for:

- **Parallelogram shearing** (V1): base Quad with shear scale + visible child Quad
- **Stretch clustering** (V2): shared stretch Quad + multiple visible child Quads
- **Hierarchical parenting** (V3): visible Quads serving as parents for other visible Quads

## Files

| File           | Content                                                                              |
|----------------|--------------------------------------------------------------------------------------|
| `ModelBase.cs` | Abstract base class with shared transform, native primitive logic, and common fields |
