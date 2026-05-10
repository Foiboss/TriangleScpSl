# NGons — Mesh-to-Primitive Pipeline

This folder contains the core pipeline that converts OBJ mesh faces into Unity primitives (Quad, Sphere, Cylinder, Cube).

## Overall Flow

```
OBJ file
  → ObjNGonParser         (parse vertices, faces, colors)
  → NGonDeduplicator       (remove duplicate faces)
  → ModelSolidVolume       (build winding-number solid for inside/outside tests)
  → PrimitiveShapeDetector (detect spheres, cylinders, cubes → ModelPrimitive)
  → PlanarNGonSplitter     (merge coplanar same-color faces, simplify collinear vertices)
  → ConvexNGonDecomposer   (split concave n-gons into convex pieces)
  → HiddenTailParallelogramProcessor (decompose convex n-gons into parallelograms)
  → List<ModelParallelogram> + List<ModelPrimitive>
```

`NGonModelBuilder.TryLoad()` is the entry point that runs this entire pipeline.

## Key Data Types

- **NGonRaw** — A raw polygon: list of vertices + color. Produced by the OBJ parser.
- **ConvexNGon** — A convex polygon with vertices, color, and plane normal.
- **ModelParallelogram** — Center + two half-diagonal vectors (VLeft, VUp) + color. `IsRectangle=true` when VLeft ⊥ VUp (costs 1 primitive instead of 2).
- **ModelPrimitive** — A detected native primitive (Sphere/Cylinder/Cube): type, center, rotation, scale, color.

## Pipeline Stages

### 1. ObjNGonParser

Parses `.obj` files into `NGonRaw` faces. Supports `v`, `f`, `usemtl`, `mtllib` directives. Handles n-gon faces (not just triangles).

### 2. NGonDeduplicator

Removes duplicate faces by comparing vertex count, centroid, normal, area, and sorted vertex distances.

Duplicate faces appear in OBJ files from:

- Boolean operations on geometry
- Mirrored objects
- Overlapping objects
- Double-sided faces

Each duplicate wastes all the primitives needed to render it.

**Similarity Criteria** (all must match):

- Same vertex count
- Colors close (per-channel epsilon tolerance)
- Coplanar normals (within planeAngleThreshold)
- Centroids close (within planeDistThreshold)
- 1-to-1 vertex match (regardless of winding or starting vertex)

### 3. ModelSolidVolume

Computes generalized winding numbers to answer **"is point P inside the mesh?"**

Used for:

- Verifying hidden surfaces inside solid material (primitive detection)
- Hiding parallelogram tails inside the model

**The Algorithm**: Generalized Winding Number via van Oosterom-Strang solid angle accumulation.

For any closed oriented surface S and point P:

```
w(P, S) = (1/4π) × ∮_S (solid angle seen from P)
        = how many times S "wraps around" P
```

**For watertight outward-oriented meshes:**

- w = 1 inside solid material
- w = 0 outside
- Threshold: w ≥ 0.5 means "inside"

**Robustness**: Works on non-watertight meshes with small cracks (graceful degradation).

**Tail Hiding (Parallelogram Optimization)**: When decomposing convex n-gons into parallelograms, triangular "tails" can be hidden inside the solid by:

- Sampling multiple points across the tail (edge midpoints, vertex-near points, interior grid)
- Pulling samples inward from the boundary (TailPullIn = 0.03 = strict)
- If all samples are inside solid, the tail is invisible and can be rendered in 1 primitive instead of 2

Parameters:

- `TailPullIn` (default 0.03): How far samples are pulled from boundary toward centroid. Lower = stricter.
- `TailGridDepth` (default 2): Barycentric grid density. Increase for thinner walls / larger triangles.

### 4. PrimitiveShapeDetector

Clusters faces by color + edge adjacency (Union-Find), then tries fitting each cluster to primitive shapes.

**Three Detection Passes:**

1. **Pass 1 (Exact)**: Sphere → Cylinder → Cube
    - Tries to fit cluster vertices to primitive shapes with tight tolerance
    - Detected primitives are removed from the face list

2. **Pass 2 (Approximate, requires solid)**: Relaxed sphere fit
    - For clusters that failed exact detection
    - Uses `ModelSolidVolume` to verify hidden surface is inside solid
    - Allows approximate fit if mismatch is hidden

3. **Pass 3 (Partial boxes, requires solid)**: 3-5 visible faces forming a box protrusion
    - Detects partially visible cubes where hidden faces are inside solid
    - Useful for models with surface details poking out of walls

### 5. PlanarNGonSplitter

Two merge modes:

- **Exact merge** (always runs): Merges adjacent same-color faces already on the same plane. No vertex snapping.
- **Approximate merge** (when planarThreshold > 0): Snaps nearby vertices to shared planes, then merges.

Also runs `SimplifyCollinear` to remove vertices that lie on straight edges.

### 6. ConvexNGonDecomposer

Splits concave n-gons into convex pieces via:

1. **Triangulation**: Ear-clipping algorithm on 2D projection
2. **Merging**: Hertel-Mehlhorn greedy merge of adjacent convex triangles

**Process:**

- Project to 2D via orthonormal basis (preserves CCW winding)
- Ear-clip to triangles
- Greedily merge adjacent triangles if union remains convex
- Return to 3D with original color and normal

### 7. HiddenTailParallelogramProcessor

Decomposes each convex n-gon into `ModelParallelogram` instances.

**Algorithm:**

1. Check if bounding rectangle covers the whole n-gon (excess inside solid)
2. Peel parallel-side pairs into parallelograms
3. Handle remaining triangular tails (optionally hidden inside solid)

## Shared Utilities

`NGonMath.cs` contains shared geometric helpers used throughout the pipeline:

- `NewellNormal` / `NewellNormalIndexed` — Newell method polygon normals (robust for near-planar faces)
- `IsPlanar` — planarity check against edge-relative tolerance
- `Find` / `Union` — Union-Find with path compression and union by size
- `EdgeKey` — order-independent edge hash for adjacency maps
- `ColorsClose` — per-channel color comparison

## Detectors Subfolder

See `Detectors/README.md` for details on sphere, cylinder, cube detection and the smoothness check.
