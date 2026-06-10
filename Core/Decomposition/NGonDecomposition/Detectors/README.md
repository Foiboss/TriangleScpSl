# Detectors - Primitive Shape Detection

Detects when a cluster of mesh faces forms a recognizable Unity primitive shape (Sphere, Cylinder, Cube) that can be replaced with a single native primitive, saving hundreds or thousands of parallelogram primitives.

## Detection Flow

`PrimitiveShapeDetector` groups faces into connected components by color and edge adjacency, then runs detection passes in priority order. The first match wins.

**Passes (per iteration):**

1. **Exact detection** on whole clusters (sphere → cylinder → cube).
2. **Smooth sub-cluster splitting** — clusters are split on normal similarity so e.g. a sphere welded to a same-color wall is separated and detected.
3. **Convex-piece splitting** — clusters are split at *concave* edges into convex pieces (e.g. two stacked boxes meet at a concave seam). Each piece is retried with exact and partial-box detection.
4. **Approximate sphere/cylinder fit** with relaxed tolerance (requires solid volume).
5. **Partial box detection** — 2+ visible box faces with the hidden sides verified embedded in solid material.

**Iteration:** after a round of consumption the remaining faces are re-clustered and the passes run again (up to 3 rounds). This resolves composite clusters incrementally — extracting one shape often leaves a clean remainder.

**Embedded-face culling:** after detection, any remaining face that lies entirely inside a detected primitive (which is an opaque convex solid) is dropped — it can never be seen, so rendering it would only waste primitives. Faces lying exactly on a primitive's surface (decals) are kept via a depth threshold.

**Smoothness gate:** Before trying sphere or cylinder detection, `SmoothnessCheck` verifies that the surface is smooth (not faceted). This prevents replacing intentionally low-poly geometry (like an icosahedron) with a smooth sphere.

**Foreign-vertex guard:** a candidate primitive is rejected when an unrelated face has a vertex just below the candidate's surface — such vertices were visible before the replacement and the primitive would cover them, changing the model's look.

## Detectors

### SphereDetector

Fits a sphere or ellipsoid to a face cluster.

**Algorithm:**

1. Compute centroid C of all unique vertices
2. **Sphere test:** check `max(|r_i - r_mean|) / r_mean < 0.02` (all vertices equidistant from center)
3. **Ellipsoid fallback:** compute 3x3 covariance matrix of `(v_i - C)`, perform **Jacobi eigendecomposition** to get principal axes and semi-axis lengths. Transform vertices into the eigen-frame and check unit-sphere fit.
4. **Normal validation:** verify face normals point outward from center
5. **Coverage check:** compute total solid angle subtended by faces from center (must be >= 2pi, at least half-sphere)
6. For partial coverage: verify uncovered surface is inside solid material using **Fibonacci sphere sampling**

**Approximate mode** (Pass 2): 10% tolerance, lower coverage. Requires solid volume for hidden surface verification.

Unity sphere has diameter 1 at scale 1. Output: `Scale = (2*a1, 2*a2, 2*a3)`.

### CylinderDetector

Fits a cylinder to a face cluster.

**Algorithm:**

1. Build **area-weighted normal covariance matrix** `N = sum(area_i * n_i * n_i^T)`
2. Smallest eigenvector of N = cylinder axis (lateral face normals are perpendicular to axis)
3. Project vertices onto plane perpendicular to axis
4. **Kasa circle fitting** in 2D projected space (least-squares circle fit)
5. Check radial deviation: `max(|dist - r|) / r < 0.02`
6. Height = extent of vertices along axis

Unity cylinder has radius 0.5, height 2. Output: `Scale = (2r, h/2, 2r)`.

### CubeDetector

Detects axis-aligned or arbitrarily rotated boxes.

**Exact mode** (`TryDetect`): Requires exactly 6 faces.

1. Cluster face normals into 3 anti-parallel pairs (tolerance: ~5 degrees)
2. Check mutual orthogonality: `|dot(d_i, d_j)| < 0.05`
3. Project vertices onto each axis for extents
4. Verify all vertices lie on box surface

**Partial mode** (`TryDetectPartial`): For 2-48 visible faces forming a box protrusion (2 orthogonal faces are enough — the third axis comes from the cross product). Two approaches:

1. **Normal-based:** cluster visible face normals into orthogonal groups, infer missing axes from cross products
2. **OBB fallback:** covariance eigendecomposition of vertex positions, fit oriented bounding box (**OBB**), verify vertices on surface

Both partial methods verify hidden faces are inside solid material, that ≥75% of face normals point outward (rejects concave room corners), and that at least one visible face has empty space outside it.

### SmoothnessCheck

Shared gate that prevents replacing faceted geometry with smooth primitives.

**Algorithm:** builds edge adjacency map, measures dihedral angle between normals of adjacent faces. Requires at least 70% of shared edges to have angle below the threshold (default 0.32 radians ~ 18 degrees).

Both thresholds (max angle, min fraction) are configurable via the `smoothness` command parameter.

## Mathematical Methods used

| Method                                | Used by                      | Description                                                                                          |
|---------------------------------------|------------------------------|------------------------------------------------------------------------------------------------------|
| **Jacobi eigendecomposition**         | Sphere, Cylinder, Cube (OBB) | Iterative eigenvalue solver for 3x3 symmetric matrices. Finds principal axes of covariance matrices. |
| **Kasa circle fitting**               | Cylinder                     | Least-squares 2D circle fit. Minimizes algebraic distance.                                           |
| **Solid angle (van Oosterom-Strang)** | Sphere coverage              | Computes solid angle subtended by a triangle from a point using the atan2 formula.                   |
| **Fibonacci sphere sampling**         | Sphere (partial)             | Quasi-uniform point distribution on sphere surface for hidden-surface verification.                  |

## Files

| File                           | Content                                                              |
|--------------------------------|----------------------------------------------------------------------|
| `SphereDetector.cs`            | `TryDetect` and `TryDetectApproximate` for sphere/ellipsoid fitting  |
| `SphereDetector.Validation.cs` | Normal validation, solid angle coverage, hidden surface verification |
| `SphereDetector.Eigen.cs`      | Jacobi eigendecomposition for 3x3 symmetric matrices                 |
| `CylinderDetector.cs`          | `TryDetect` for cylinder axis + Kasa circle fitting                  |
| `CubeDetector.cs`              | `TryDetect` (exact 6-face) and `TryDetectPartial` (3-5 face)         |
| `CubeDetector.Fitting.cs`      | Normal-based fitting, OBB fitting, shared box verification           |
| `SmoothnessCheck.cs`           | `IsSurfaceSmooth` dihedral angle gate                                |
