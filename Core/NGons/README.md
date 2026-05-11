# NGons - Mesh-to-Primitive Pipeline

Converts OBJ mesh faces into `ModelParallelogram` and `ModelPrimitive` instances through a multi-stage pipeline. This is the core geometry processing that makes the plugin work.

## Pipeline Overview

```
OBJ file
  - ObjNGonParser               parse vertices, faces, MTL colors
  - NGonDeduplicator            remove duplicate/overlapping faces
  - ModelSolidVolume            build winding-number solid for inside/outside tests
  - PrimitiveShapeDetector      detect spheres, cylinders, cubes -> ModelPrimitive
  - PlanarNGonSplitter          merge coplanar same-color faces
  - ConvexNGonDecomposer        split concave n-gons into convex pieces
  - HiddenTailProcessor         decompose convex n-gons into parallelograms
  - List<ModelParallelogram> + List<ModelPrimitive>
```

`NGonModelBuilder.TryLoad()` is the single entry point that runs this entire pipeline.

## Key Data Types

| Type                 | Description                                                                                             |
|----------------------|---------------------------------------------------------------------------------------------------------|
| `NGonRaw`            | A raw polygon: list of vertices + color. Produced by the OBJ parser.                                    |
| `ConvexNGon`         | A convex polygon with vertices, color, and plane normal.                                                |
| `ModelParallelogram` | Center + two half-diagonal vectors (VLeft, VUp) + color. `IsRectangle=true` when `\|VLeft\| = \|VUp\|`. |
| `ModelPrimitive`     | A detected native primitive (Sphere/Cylinder/Cube): type, center, rotation, scale, color.               |

## Pipeline Stages

### 1. ObjNGonParser

Parses `.obj` files into `NGonRaw` faces. Supports `v`, `f`, `vn`, `usemtl`, `mtllib` directives. Unlike the legacy triangle-based parser, this preserves original n-gon faces (quads, pentagons, etc.) which produce fewer parallelograms.

### 2. NGonDeduplicator

Removes duplicate faces that appear from boolean operations, overlapping geometry.

**Matching criteria:** same vertex count, similar colors, same-direction normals, close centroids, and 1-to-1 vertex match (regardless of starting vertex). Opposite-winding faces (back-to-back geometry like plant leaves) are intentionally preserved so double-sided surfaces remain visible from both sides.

### 3. ModelSolidVolume

Computes **generalized winding numbers** (van Oosterom-Strang solid angle method) to answer "is point P inside the mesh?"

For a closed oriented surface S and point P, the winding number `w(P,S) = (1/4pi) * integral of solid angle` counts how many times the surface wraps around P. For watertight meshes: w=1 inside, w=0 outside. Threshold: w >= 0.5 means "inside". Works on non-watertight meshes with graceful degradation.

Used by primitive shape detection and hidden tail optimization.

### 4. PrimitiveShapeDetector

Groups faces into clusters by color and edge adjacency using **Union-Find** (with path compression and union by size), then tries fitting each cluster to primitive shapes.

**Three detection passes:**

1. **Exact fit:** Sphere -> Cylinder -> Cube (tight tolerance)
2. **Approximate sphere** (requires solid): relaxed fit, hidden surface verified inside solid
3. **Partial boxes** (requires solid): 3-5 visible faces forming a box protrusion

A **smoothness gate** prevents replacing low-poly (faceted) geometry with smooth primitives. See `Detectors/README.md` for algorithm details.

### 5. PlanarNGonSplitter

Merges adjacent same-color coplanar faces into larger polygons. Fewer faces = fewer parallelograms.

**Two merge modes:**

- **Exact merge** (always runs): merges faces already on the exact same plane. Uses Union-Find on edge-adjacent faces with matching normals. Extracts merged boundary via directed-edge winding.
- **Approximate merge** (when `planarThreshold > 0`): fits best-fit planes to face clusters using **least-squares plane fitting**, snaps vertices within tolerance, then merges.

Also runs **collinear vertex simplification** to remove redundant vertices on straight edges.

### 6. ConvexNGonDecomposer

Splits concave n-gons into convex pieces using a two-phase approach:

1. **Ear-clipping triangulation**: projects the polygon to 2D, finds "ears" (triangles where the diagonal lies inside the polygon), clips them iteratively. This is a classic O(n^2) algorithm.
2. **Hertel-Mehlhorn merge**: greedily merges adjacent convex triangles back into larger convex polygons by removing shared diagonals when the result remains convex. This produces near-optimal convex decompositions.

### 7. HiddenTailParallelogramProcessor

Decomposes each convex n-gon into `ModelParallelogram` instances. Three strategies:

1. **Bounding rectangle cover**: if a single rectangle covers the entire n-gon and the excess area is inside solid material, emit 1 rectangle instead of multiple parallelograms.
2. **Parallel-sides peeling**: find vertex V where neighbors A, B form parallel sides to other polygon edges. Peel off a parallelogram, reduce the polygon by 1 vertex, repeat.
3. **Triangle tail handling**: the final 3 vertices form a triangle, decomposed into 3 parallelograms. If the tail triangle is inside solid material, the hidden parts can use fewer primitives.

The vertex selection for peeling uses the **fourth parallelogram vertex test**: for vertex V with neighbors A and B, compute P = A + B - V. If P lies inside the polygon (tested via 2D convex point-in-polygon), then V can be peeled.

## Shared Utilities

`NGonMath.cs` contains shared geometric helpers used throughout the pipeline:

| Method                                 | Description                                                  |
|----------------------------------------|--------------------------------------------------------------|
| `NewellNormal` / `NewellNormalIndexed` | Newell method polygon normals - robust for near-planar faces |
| `IsPlanar`                             | Planarity check against edge-length-relative tolerance       |
| `Find` / `Union`                       | Union-Find with path halving and union by size               |
| `EdgeKey`                              | Order-independent edge hash for adjacency maps               |
| `ColorsClose`                          | Per-channel RGBA color comparison                            |

## Files

| File                                               | Content                                                          |
|----------------------------------------------------|------------------------------------------------------------------|
| `NGonModelBuilder.cs`                              | Entry point: `TryLoad()` runs the full pipeline                  |
| `ObjNGonParser.cs`                                 | OBJ/MTL parser producing `NGonRaw` faces                         |
| `NGonRaw.cs`                                       | Raw polygon data structure (vertices + color)                    |
| `NGonDeduplicator.cs`                              | Duplicate face removal                                           |
| `ModelSolidVolume.cs`                              | Generalized winding number solid                                 |
| `PrimitiveShapeDetector.cs`                        | Shape detection orchestrator (3-pass)                            |
| `ModelPrimitive.cs`                                | Detected native primitive data structure                         |
| `PlanarNGonSplitter.cs`                            | Main coplanar merging logic + shared helpers                     |
| `PlanarNGonSplitter.ExactMerge.cs`                 | Exact coplanar face merge                                        |
| `PlanarNGonSplitter.ApproxMerge.cs`                | Approximate merge with vertex snapping                           |
| `ConvexNGonDecomposer.cs`                          | Ear-clipping + Hertel-Mehlhorn convex decomposition              |
| `ParallelogramProcessor.cs`                        | Simple convex n-gon to parallelogram conversion (no hidden tail) |
| `HiddenTailParallelogramProcessor.cs`              | Main parallelogram decomposition with hidden tail optimization   |
| `HiddenTailParallelogramProcessor.Peeling.cs`      | Parallel-sides peeling logic                                     |
| `HiddenTailParallelogramProcessor.BoundingRect.cs` | Bounding rectangle covering logic                                |
| `HiddenTailParallelogramProcessor.Geometry.cs`     | Geometric helpers (point-in-polygon, plane basis, projection)    |
| `NGonMath.cs`                                      | Shared utilities (Newell normal, Union-Find, etc.)               |

## Detectors Subfolder

See `Detectors/README.md` for sphere, cylinder, cube detection algorithms and the smoothness gate.
