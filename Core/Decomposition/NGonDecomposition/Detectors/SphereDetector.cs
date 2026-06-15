using TriangleScpSl.Core.Decomposition.NGonDecomposition.Geometry;
using TriangleScpSl.Core.Decomposition.NGonDecomposition.Parsing;
using UnityEngine;

namespace TriangleScpSl.Core.Decomposition.NGonDecomposition.Detectors;

public static partial class SphereDetector
{
    public static bool TryDetect
    (
        List<NGonRaw> faces,
        List<Vector3> uniqueVertices,
        out ModelPrimitive result,
        NGonModelConfig config,
        ModelSolidVolume? solid = null)
    {
        result = null!;

        if (faces.Count < config.SphereMinFaces || uniqueVertices.Count < 4)
            return false;

        // Adaptive smoothness: low-poly spheres have larger inter-face angles
        float adaptiveAngle = AdaptiveSmoothAngle(faces.Count, config.SmoothMaxAngle);

        if (!SmoothnessCheck.IsSurfaceSmooth(faces, adaptiveAngle, config.SmoothMinFraction))
            return false;

        // Try multiple center estimation strategies (partial spheres bias simple methods)
        Vector3 algebraicCenter = AlgebraicSphereFit(uniqueVertices, out _);

        if (TryFitSphere(faces, uniqueVertices, algebraicCenter, out result, config, solid))
            return true;

        // Normal-line intersection: best for partial spheres where vertex centroid is biased
        if (NormalLineCenter(faces, out Vector3 nlCenter))
        {
            if (TryFitSphere(faces, uniqueVertices, nlCenter, out result, config, solid))
                return true;
        }

        Vector3 centroid = Vector3.zero;
        foreach (Vector3 v in uniqueVertices) centroid += v;
        centroid /= uniqueVertices.Count;

        if ((centroid - algebraicCenter).magnitude > 1e-4f)
        {
            if (TryFitSphere(faces, uniqueVertices, centroid, out result, config, solid))
                return true;
        }

        if (TryFitEllipsoid(faces, uniqueVertices, centroid, out result, config, solid))
            return true;

        return false;
    }

    // Relaxed sphere fitting for surface approximation. Accepts higher
    // radial deviation and lower coverage.
    public static bool TryDetectApproximate
    (
        List<NGonRaw> faces,
        List<Vector3> uniqueVertices,
        ModelSolidVolume solid,
        out ModelPrimitive result,
        NGonModelConfig config)
    {
        result = null!;

        if (faces.Count < config.SphereMinFaces || uniqueVertices.Count < 4)
            return false;

        float adaptiveAngle = AdaptiveSmoothAngle(faces.Count, config.SmoothMaxAngle);

        if (!SmoothnessCheck.IsSurfaceSmooth(faces, adaptiveAngle, config.SmoothMinFraction))
            return false;

        // Try multiple center strategies for partial spheres
        Vector3 algebraicCenter = AlgebraicSphereFit(uniqueVertices, out _);

        if (TryFitApproxSphere(faces, uniqueVertices, algebraicCenter, solid, config, out result))
            return true;

        if (NormalLineCenter(faces, out Vector3 nlCenter))
        {
            if (TryFitApproxSphere(faces, uniqueVertices, nlCenter, solid, config, out result))
                return true;
        }

        return false;
    }

    /// <summary>
    ///     Computes an adaptive smoothness angle threshold based on face count.
    ///     Low-poly spheres/ellipsoids have large interface angles that
    ///     would fail a strict threshold. UV spheres have non-uniform face
    ///     distribution with larger equatorial angles than icospheres, so
    ///     the multiplier is generous.
    /// </summary>
    static float AdaptiveSmoothAngle(int faceCount, float baseAngle)
    {
        // For a sphere with f faces, expected angle ≈ 2 * sqrt(π / f).
        // UV spheres have equatorial angles ~1.5x larger than icosphere average,
        // so we use a 1.8x multiplier to accommodate both topologies.
        float expectedAngle = 2f * Mathf.Sqrt(Mathf.PI / Mathf.Max(4f, faceCount));

        // Cap at 1.2 radians (~68°) to reject platonic solids
        // (cube 90°, octahedron 109°, dodecahedron 117°).
        return Mathf.Min(Mathf.Max(baseAngle, expectedAngle * 1.8f), 1.2f);
    }

    /// <summary>
    ///     Algebraic least-squares sphere fit.
    ///     Solves: minimize Σ (x² + y² + z² + ax + by + cz + d)²
    ///     Center = (-a/2, -b/2, -c/2), r² = center² - d
    ///     Falls back to centroid if the system is degenerate.
    /// </summary>
    static Vector3 AlgebraicSphereFit(List<Vector3> verts, out float radius)
    {
        int n = verts.Count;
        radius = 0f;

        if (n < 4)
        {
            Vector3 c = Vector3.zero;
            foreach (Vector3 v in verts) c += v;
            return c / n;
        }

        // Build normal equations for: [x y z 1] * [a b c d]^T = -(x² + y² + z²)
        // Using double precision for numerical stability
        double sX = 0, sY = 0, sZ = 0;
        double sXX = 0, sYY = 0, sZZ = 0;
        double sXY = 0, sXZ = 0, sYZ = 0;
        double sR2 = 0; // Σ (x² + y² + z²)
        double sXR2 = 0, sYR2 = 0, sZR2 = 0;

        foreach (Vector3 v in verts)
        {
            double x = v.x, y = v.y, z = v.z;
            double r2 = x * x + y * y + z * z;
            sX += x;
            sY += y;
            sZ += z;
            sXX += x * x;
            sYY += y * y;
            sZZ += z * z;
            sXY += x * y;
            sXZ += x * z;
            sYZ += y * z;
            sR2 += r2;
            sXR2 += x * r2;
            sYR2 += y * r2;
            sZR2 += z * r2;
        }

        // 4x4 system: A * [a,b,c,d]^T = -rhs
        // Row 0: [Σxx  Σxy  Σxz  Σx ] * [a,b,c,d] = -Σ(x·r²)
        // Row 1: [Σxy  Σyy  Σyz  Σy ] * [a,b,c,d] = -Σ(y·r²)
        // Row 2: [Σxz  Σyz  Σzz  Σz ] * [a,b,c,d] = -Σ(z·r²)
        // Row 3: [Σx   Σy   Σz   n  ] * [a,b,c,d] = -Σ(r²)
        var A = new double[4, 4];
        var rhs = new double[4];

        A[0, 0] = sXX;
        A[0, 1] = sXY;
        A[0, 2] = sXZ;
        A[0, 3] = sX;
        A[1, 0] = sXY;
        A[1, 1] = sYY;
        A[1, 2] = sYZ;
        A[1, 3] = sY;
        A[2, 0] = sXZ;
        A[2, 1] = sYZ;
        A[2, 2] = sZZ;
        A[2, 3] = sZ;
        A[3, 0] = sX;
        A[3, 1] = sY;
        A[3, 2] = sZ;
        A[3, 3] = n;

        rhs[0] = -sXR2;
        rhs[1] = -sYR2;
        rhs[2] = -sZR2;
        rhs[3] = -sR2;

        // Solve via Gaussian elimination with partial pivoting
        if (!SolveLinear4X4(A, rhs, out double[] sol))
        {
            // Degenerate - fall back to centroid
            Vector3 c = Vector3.zero;
            foreach (Vector3 v in verts) c += v;
            c /= n;
            foreach (Vector3 v in verts) radius += (v - c).magnitude;
            radius /= n;
            return c;
        }

        var center = new Vector3((float)(-sol[0] * 0.5), (float)(-sol[1] * 0.5), (float)(-sol[2] * 0.5));
        double r2Val = center.x * (double)center.x + center.y * (double)center.y + center.z * (double)center.z - sol[3];
        radius = r2Val > 0 ? (float)Math.Sqrt(r2Val) : 0f;

        return center;
    }

    static bool SolveLinear4X4(double[,] A, double[] b, out double[] x)
    {
        x = new double[4];
        var piv = new int[] { 0, 1, 2, 3 };

        for (var col = 0; col < 4; col++)
        {
            // Partial pivoting
            double maxVal = Math.Abs(A[piv[col], col]);
            int maxRow = col;

            for (int row = col + 1; row < 4; row++)
            {
                double val = Math.Abs(A[piv[row], col]);

                if (val > maxVal)
                {
                    maxVal = val;
                    maxRow = row;
                }
            }

            if (maxVal < 1e-14) return false;

            (piv[col], piv[maxRow]) = (piv[maxRow], piv[col]);

            int pivRow = piv[col];

            for (int row = col + 1; row < 4; row++)
            {
                int r = piv[row];
                double factor = A[r, col] / A[pivRow, col];
                A[r, col] = 0;

                for (int c = col + 1; c < 4; c++)
                {
                    A[r, c] -= factor * A[pivRow, c];
                }

                b[r] -= factor * b[pivRow];
            }
        }

        // Back substitution
        for (var row = 3; row >= 0; row--)
        {
            int r = piv[row];
            double sum = b[r];

            for (int c = row + 1; c < 4; c++)
            {
                sum -= A[r, c] * x[c];
            }

            if (Math.Abs(A[r, row]) < 1e-14) return false;
            x[row] = sum / A[r, row];
        }

        return true;
    }

    static bool TryFitSphere
    (
        List<NGonRaw> faces,
        List<Vector3> verts,
        Vector3 center,
        out ModelPrimitive result,
        NGonModelConfig config,
        ModelSolidVolume? solid)
    {
        result = null!;

        var rSum = 0f;

        foreach (Vector3 v in verts)
            rSum += (v - center).magnitude;
        float rMean = rSum / verts.Count;

        if (rMean < 1e-6f) return false;

        var maxDev = 0f;

        foreach (Vector3 v in verts)
        {
            float dev = Mathf.Abs((v - center).magnitude - rMean) / rMean;
            if (dev > maxDev) maxDev = dev;
        }

        if (maxDev > config.SphereTolerance) return false;

        // Reject boxes masquerading as spheres: cube face centroids are only ~58%
        // of vertex radius, real sphere face centroids are ~95%+.
        if (!ValidateCentroidRadius(faces, center, rMean, 0.80f)) return false;

        if (!ValidateNormalsOutward(faces, center)) return false;

        float coverage = ComputeCoverage(faces, center);
        bool isPartial = coverage < config.SphereFullCoverage;

        if (isPartial)
        {
            if (coverage < config.SphereMinCoverage) return false;
            if (solid == null) return false;

            if (!VerifyHiddenSurfaceInsideSolid(center, rMean, faces, solid, config.SphereHiddenSurfaceSamples))
                return false;
        }

        float diameter = rMean * 2f;

        result = new ModelPrimitive
        {
            Type = PrimitiveType.Sphere,
            Center = center,
            Rotation = Quaternion.identity,
            Scale = new Vector3(diameter, diameter, diameter),
            Color = faces[0].Color,
        };
        return true;
    }

    static bool TryFitEllipsoid
    (
        List<NGonRaw> faces,
        List<Vector3> verts,
        Vector3 centroid,
        out ModelPrimitive result,
        NGonModelConfig config,
        ModelSolidVolume? solid)
    {
        result = null!;

        // 3x3 covariance matrix (symmetric)
        float cxx = 0, cxy = 0, cxz = 0, cyy = 0, cyz = 0, czz = 0;

        foreach (Vector3 v in verts)
        {
            float dx = v.x - centroid.x;
            float dy = v.y - centroid.y;
            float dz = v.z - centroid.z;
            cxx += dx * dx;
            cxy += dx * dy;
            cxz += dx * dz;
            cyy += dy * dy;
            cyz += dy * dz;
            czz += dz * dz;
        }

        float inv = 1f / verts.Count;
        cxx *= inv;
        cxy *= inv;
        cxz *= inv;
        cyy *= inv;
        cyz *= inv;
        czz *= inv;

        Eigen3X3(
            cxx, cxy, cxz,
            cyy, cyz,
            czz,
            out Vector3 eval, out Vector3 ev0, out Vector3 ev1, out Vector3 ev2);

        if (eval.x < 1e-10f || eval.y < 1e-10f || eval.z < 1e-10f)
            return false;

        // For uniform sphere sampling, covariance eigenvalues = r^2/3
        float a0 = Mathf.Sqrt(3f * eval.x);
        float a1 = Mathf.Sqrt(3f * eval.y);
        float a2 = Mathf.Sqrt(3f * eval.z);

        // Transform vertices to eigen-frame and check unit-sphere fit
        var maxDev = 0f;

        foreach (Vector3 v in verts)
        {
            Vector3 d = v - centroid;
            float x = Vector3.Dot(d, ev0) / a0;
            float y = Vector3.Dot(d, ev1) / a1;
            float z = Vector3.Dot(d, ev2) / a2;
            float r = Mathf.Sqrt(x * x + y * y + z * z);
            float dev = Mathf.Abs(r - 1f);
            if (dev > maxDev) maxDev = dev;
        }

        if (maxDev > config.SphereTolerance) return false;

        // Reject boxes: use average ellipsoid radius for centroid check
        float rAvg = (a0 + a1 + a2) / 3f;
        if (!ValidateCentroidRadius(faces, centroid, rAvg, 0.75f)) return false;

        if (!ValidateNormalsOutward(faces, centroid)) return false;

        float coverage = ComputeCoverage(faces, centroid);
        bool isPartial = coverage < config.SphereFullCoverage;

        if (isPartial)
        {
            if (coverage < config.SphereMinCoverage) return false;
            if (solid == null) return false;
            float rMean = (a0 + a1 + a2) / 3f;

            if (!VerifyHiddenSurfaceInsideSolid(centroid, rMean, faces, solid, config.SphereHiddenSurfaceSamples))
                return false;
        }

        // Build rotation from eigenvectors, ensure right-handed frame
        Vector3 cross = Vector3.Cross(ev0, ev1);

        if (Vector3.Dot(cross, ev2) < 0f)
            ev2 = -ev2;

        Quaternion rotation = Quaternion.LookRotation(ev2, ev1);

        result = new ModelPrimitive
        {
            Type = PrimitiveType.Sphere,
            Center = centroid,
            Rotation = rotation,
            Scale = new Vector3(2f * a0, 2f * a1, 2f * a2),
            Color = faces[0].Color,
        };
        return true;
    }

    static bool TryFitApproxSphere
    (
        List<NGonRaw> faces,
        List<Vector3> uniqueVertices,
        Vector3 center,
        ModelSolidVolume solid,
        NGonModelConfig config,
        out ModelPrimitive result)
    {
        result = null!;

        var rSum = 0f;

        foreach (Vector3 v in uniqueVertices)
            rSum += (v - center).magnitude;
        float rMean = rSum / uniqueVertices.Count;

        if (rMean < 1e-6f) return false;

        var maxDev = 0f;

        foreach (Vector3 v in uniqueVertices)
        {
            float dev = Mathf.Abs((v - center).magnitude - rMean) / rMean;
            if (dev > maxDev) maxDev = dev;
        }

        if (maxDev > config.SphereApproxTolerance) return false;

        if (!ValidateCentroidRadius(faces, center, rMean, 0.75f)) return false;

        if (!ValidateNormalsOutward(faces, center)) return false;

        float coverage = ComputeCoverage(faces, center);
        if (coverage < config.SphereMinApproxCoverage) return false;

        if (!VerifyHiddenSurfaceInsideSolid(center, rMean, faces, solid, config.SphereHiddenSurfaceSamples))
            return false;

        var totalVerts = 0;

        foreach (NGonRaw face in faces)
            totalVerts += face.Vertices.Count;
        if (totalVerts < 8) return false;

        float diameter = rMean * 2f;

        result = new ModelPrimitive
        {
            Type = PrimitiveType.Sphere,
            Center = center,
            Rotation = Quaternion.identity,
            Scale = new Vector3(diameter, diameter, diameter),
            Color = faces[0].Color,
        };
        return true;
    }

    /// <summary>
    ///     Estimates sphere center by finding the point closest to all face normal lines.
    ///     Each face centroid + normal defines a ray toward the center.
    ///     Solves: minimize Σ ||(I - n_i·n_i^T)(center - c_i)||²
    /// </summary>
    static bool NormalLineCenter(List<NGonRaw> faces, out Vector3 center)
    {
        center = Vector3.zero;

        float m00 = 0, m01 = 0, m02 = 0, m11 = 0, m12 = 0, m22 = 0;
        float b0 = 0, b1 = 0, b2 = 0;
        var count = 0;

        foreach (NGonRaw face in faces)
        {
            if (face.Vertices.Count < 3) continue;

            Vector3 n = NGonMath.NewellNormal(face.Vertices);
            float mag = n.magnitude;
            if (mag < 1e-10f) continue;
            n /= mag;

            Vector3 c = FaceCentroid(face.Vertices);

            float p00 = 1 - n.x * n.x, p01 = -n.x * n.y, p02 = -n.x * n.z;
            float p11 = 1 - n.y * n.y, p12 = -n.y * n.z;
            float p22 = 1 - n.z * n.z;

            m00 += p00;
            m01 += p01;
            m02 += p02;
            m11 += p11;
            m12 += p12;
            m22 += p22;

            b0 += p00 * c.x + p01 * c.y + p02 * c.z;
            b1 += p01 * c.x + p11 * c.y + p12 * c.z;
            b2 += p02 * c.x + p12 * c.y + p22 * c.z;

            count++;
        }

        if (count < 3) return false;

        float det = m00 * (m11 * m22 - m12 * m12)
            - m01 * (m01 * m22 - m12 * m02)
            + m02 * (m01 * m12 - m11 * m02);

        if (Mathf.Abs(det) < 1e-10f) return false;

        float invDet = 1f / det;

        center = new Vector3(
            (b0 * (m11 * m22 - m12 * m12) - m01 * (b1 * m22 - m12 * b2) + m02 * (b1 * m12 - m11 * b2)) * invDet,
            (m00 * (b1 * m22 - m12 * b2) - b0 * (m01 * m22 - m12 * m02) + m02 * (m01 * b2 - b1 * m02)) * invDet,
            (m00 * (m11 * b2 - b1 * m12) - m01 * (m01 * b2 - b1 * m02) + b0 * (m01 * m12 - m11 * m02)) * invDet
        );

        return true;
    }

    internal static void Eigen3X3Internal
    (
        float m00, float m01, float m02,
        float m11, float m12,
        float m22,
        out Vector3 eigenvalues, out Vector3 ev0, out Vector3 ev1, out Vector3 ev2)
        => Eigen3X3(m00, m01, m02, m11, m12, m22, out eigenvalues, out ev0, out ev1, out ev2);
}