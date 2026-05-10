# Detectors — Primitive Shape Detection

Detects when a cluster of mesh faces forms a recognizable primitive shape (sphere, cylinder, cube) that can be replaced with a single Unity primitive.

## How Detection Works

`PrimitiveShapeDetector` groups faces into clusters by color and edge adjacency, then tries each detector in order. The first match wins.

## Detectors

### SphereDetector

Fits a sphere or ellipsoid to a face cluster.

**Algorithm:**

1. Compute centroid of unique vertices
2. Sphere fit: check if all vertices are equidistant from centroid (tolerance: 2%)
3. Ellipsoid fallback: covariance eigendecomposition → semi-axes, check unit-sphere fit in eigen-frame
4. Validate normals point outward from center
5. Check solid angle coverage (at least half-sphere)
6. For partial coverage: verify uncovered surface is inside solid (Fibonacci sphere sampling)

**Approximate mode:** 10% tolerance, lower coverage requirement. Used in Pass 2 when solid volume is available.

Unity sphere has diameter 1 at scale 1. Output Scale = (2×semi-axis₁, 2×semi-axis₂, 2×semi-axis₃).

### CylinderDetector

Fits a cylinder to a face cluster.

**Algorithm:**

1. Build area-weighted normal covariance matrix
2. Smallest eigenvector = cylinder axis (lateral normals are perpendicular to axis)
3. Project vertices onto plane perpendicular to axis
4. Kasa circle fit in 2D projected space
5. Check radial deviation (tolerance: 2%)
6. Height = extent along axis

Unity cylinder has radius 0.5, height 2. Output Scale = (2r, h/2, 2r).

### CubeDetector

Detects axis-aligned or arbitrarily rotated boxes.

**Exact mode (TryDetect):** Requires exactly 6 faces. Clusters normals into 3 anti-parallel pairs, checks orthogonality, verifies vertices lie on box surface.

**Partial mode (TryDetectPartial):** For 3-5 visible faces forming a box protrusion. Two approaches:

1. Normal-based: cluster visible face normals into orthogonal groups, infer missing axes
2. OBB fallback: covariance eigendecomposition of vertex positions for arbitrarily rotated boxes

Both verify that missing faces are inside solid material.

### SmoothnessCheck

Shared gate for sphere/cylinder detection. Prevents replacing low-poly (faceted) geometry with smooth primitives.

Measures the angle between normals of adjacent faces. Requires at least 70% of shared edges to have angle < 18° (~0.32 radians). Both thresholds are configurable via the `smoothness` command parameter.

## Eigendecomposition

`SphereDetector.Eigen3x3` implements Jacobi iteration for 3×3 symmetric matrices. Used by both sphere (covariance of vertex positions) and cylinder (covariance of face normals) detectors, and by the OBB fitting in CubeDetector.
