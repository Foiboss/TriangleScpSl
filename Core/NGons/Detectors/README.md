# Detectors - Primitive Shape Detection

Detects when a cluster of mesh faces forms a recognizable Unity primitive shape (Sphere, Cylinder, Cube) that can be replaced with a single native primitive, saving hundreds or thousands of parallelogram primitives.

## Detection Flow

`PrimitiveShapeDetector` groups faces into connected components by color and edge adjacency, then runs detectors in priority order. The first match wins.

**Smoothness gate:** Before trying sphere or cylinder detection, `SmoothnessCheck` verifies that the surface is smooth (not faceted). This prevents replacing intentionally low-poly geometry (like an icosahedron) with a smooth sphere.

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

**Partial mode** (`TryDetectPartial`): For 3-5 visible faces forming a box protrusion. Two approaches:

1. **Normal-based:** cluster visible face normals into orthogonal groups, infer missing axes from cross products
2. **OBB fallback:** covariance eigendecomposition of vertex positions, fit oriented bounding box (**OBB**), verify vertices on surface

Both partial methods verify hidden faces are inside solid material.

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
