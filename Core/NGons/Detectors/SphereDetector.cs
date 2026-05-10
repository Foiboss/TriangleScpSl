using UnityEngine;

namespace TriangleScpSl.Core.NGons.Detectors;

public static partial class SphereDetector
{
    const float Tolerance = 0.02f;
    const float ApproxTolerance = 0.10f;
    const int MinFaces = 12;
    const int MinApproxFaces = 8;
    const float MinCoverageAngle = 2f * Mathf.PI;
    const float MinApproxCoverageAngle = 1.5f * Mathf.PI;
    const float FullCoverageAngle = 3.8f * Mathf.PI; // ~95% of 4pi
    const int HiddenSurfaceSamples = 64;

    public static bool TryDetect
    (
        List<NGonRaw> faces,
        List<Vector3> uniqueVertices,
        out ModelPrimitive result,
        ModelSolidVolume? solid = null,
        float smoothMaxAngle = SmoothnessCheck.DefaultMaxAngle,
        float smoothMinFraction = SmoothnessCheck.DefaultMinFraction)
    {
        result = null!;

        if (faces.Count < MinFaces || uniqueVertices.Count < 6)
            return false;

        if (!SmoothnessCheck.IsSurfaceSmooth(faces, smoothMaxAngle, smoothMinFraction))
            return false;

        Vector3 centroid = Vector3.zero;

        foreach (Vector3 v in uniqueVertices)
            centroid += v;
        centroid /= uniqueVertices.Count;

        if (TryFitSphere(faces, uniqueVertices, centroid, out result, solid))
            return true;

        if (TryFitEllipsoid(faces, uniqueVertices, centroid, out result, solid))
            return true;

        return false;
    }

    // Relaxed sphere fitting for surface approximation. Accepts higher
    // radial deviation (10%) and lower coverage.
    public static bool TryDetectApproximate
    (
        List<NGonRaw> faces,
        List<Vector3> uniqueVertices,
        ModelSolidVolume solid,
        out ModelPrimitive result,
        float smoothMaxAngle = SmoothnessCheck.DefaultMaxAngle,
        float smoothMinFraction = SmoothnessCheck.DefaultMinFraction)
    {
        result = null!;

        if (faces.Count < MinApproxFaces || uniqueVertices.Count < 4)
            return false;

        if (!SmoothnessCheck.IsSurfaceSmooth(faces, smoothMaxAngle, smoothMinFraction))
            return false;

        Vector3 centroid = Vector3.zero;

        foreach (Vector3 v in uniqueVertices)
            centroid += v;
        centroid /= uniqueVertices.Count;

        var rSum = 0f;

        foreach (Vector3 v in uniqueVertices)
            rSum += (v - centroid).magnitude;
        float rMean = rSum / uniqueVertices.Count;

        if (rMean < 1e-6f) return false;

        var maxDev = 0f;

        foreach (Vector3 v in uniqueVertices)
        {
            float dev = Mathf.Abs((v - centroid).magnitude - rMean) / rMean;
            if (dev > maxDev) maxDev = dev;
        }

        if (maxDev > ApproxTolerance) return false;

        if (!ValidateNormalsOutward(faces, centroid)) return false;

        float coverage = ComputeCoverage(faces, centroid);
        if (coverage < MinApproxCoverageAngle) return false;

        if (!VerifyHiddenSurfaceInsideSolid(centroid, rMean, faces, solid))
            return false;

        var totalVerts = 0;

        foreach (NGonRaw face in faces)
            totalVerts += face.Vertices.Count;
        if (totalVerts < 12) return false;

        float diameter = rMean * 2f;

        result = new ModelPrimitive
        {
            Type = PrimitiveType.Sphere,
            Center = centroid,
            Rotation = Quaternion.identity,
            Scale = new Vector3(diameter, diameter, diameter),
            Color = faces[0].Color,
        };
        return true;
    }

    static bool TryFitSphere
    (
        List<NGonRaw> faces,
        List<Vector3> verts,
        Vector3 centroid,
        out ModelPrimitive result,
        ModelSolidVolume? solid)
    {
        result = null!;

        var rSum = 0f;

        foreach (Vector3 v in verts)
            rSum += (v - centroid).magnitude;
        float rMean = rSum / verts.Count;

        if (rMean < 1e-6f) return false;

        var maxDev = 0f;

        foreach (Vector3 v in verts)
        {
            float dev = Mathf.Abs((v - centroid).magnitude - rMean) / rMean;
            if (dev > maxDev) maxDev = dev;
        }

        if (maxDev > Tolerance) return false;

        if (!ValidateNormalsOutward(faces, centroid)) return false;

        float coverage = ComputeCoverage(faces, centroid);
        bool isPartial = coverage < FullCoverageAngle;

        if (isPartial)
        {
            if (coverage < MinCoverageAngle) return false;
            if (solid == null) return false;

            if (!VerifyHiddenSurfaceInsideSolid(centroid, rMean, faces, solid))
                return false;
        }

        float diameter = rMean * 2f;

        result = new ModelPrimitive
        {
            Type = PrimitiveType.Sphere,
            Center = centroid,
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

        if (maxDev > Tolerance) return false;

        if (!ValidateNormalsOutward(faces, centroid)) return false;

        float coverage = ComputeCoverage(faces, centroid);
        bool isPartial = coverage < FullCoverageAngle;

        if (isPartial)
        {
            if (coverage < MinCoverageAngle) return false;
            if (solid == null) return false;
            float rMean = (a0 + a1 + a2) / 3f;

            if (!VerifyHiddenSurfaceInsideSolid(centroid, rMean, faces, solid))
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

    internal static void Eigen3X3Internal
    (
        float m00, float m01, float m02,
        float m11, float m12,
        float m22,
        out Vector3 eigenvalues, out Vector3 ev0, out Vector3 ev1, out Vector3 ev2)
        => Eigen3X3(m00, m01, m02, m11, m12, m22, out eigenvalues, out ev0, out ev1, out ev2);
}