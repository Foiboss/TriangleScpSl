using UnityEngine;

namespace TriangleScpSl.Core.NGons.Detectors;

public static class CylinderDetector
{
    const float Tolerance = 0.02f;
    const int MinFaces = 8;

    public static bool TryDetect
    (
        List<NGonRaw> faces,
        List<Vector3> uniqueVertices,
        out ModelPrimitive result,
        float smoothMaxAngle = SmoothnessCheck.DefaultMaxAngle,
        float smoothMinFraction = SmoothnessCheck.DefaultMinFraction)
    {
        result = null!;

        if (faces.Count < MinFaces || uniqueVertices.Count < 6)
            return false;

        if (!SmoothnessCheck.IsSurfaceSmooth(faces, smoothMaxAngle, smoothMinFraction))
            return false;

        // Step 1: Find cylinder axis via normal-covariance matrix.
        // Lateral face normals are perpendicular to the axis, so the axis
        // direction corresponds to the SMALLEST eigenvalue of the
        // area-weighted normal covariance N = Σ(area_i · n_i · n_iᵀ).
        float nxx = 0, nxy = 0, nxz = 0, nyy = 0, nyz = 0, nzz = 0;

        foreach (NGonRaw face in faces)
        {
            List<Vector3> v = face.Vertices;
            if (v.Count < 3) continue;

            Vector3 normal = NGonMath.NewellNormal(v);
            float area = normal.magnitude;
            if (area < 1e-10f) continue;
            normal /= area;

            nxx += area * normal.x * normal.x;
            nxy += area * normal.x * normal.y;
            nxz += area * normal.x * normal.z;
            nyy += area * normal.y * normal.y;
            nyz += area * normal.y * normal.z;
            nzz += area * normal.z * normal.z;
        }

        SphereDetector.Eigen3X3Internal(
            nxx, nxy, nxz, nyy, nyz, nzz,
            out Vector3 eval, out _, out _, out Vector3 axis);

        // axis is the eigenvector with smallest eigenvalue (ev2, sorted descending)
        if (axis.sqrMagnitude < 1e-10f) return false;
        axis = axis.normalized;

        // Check that the smallest eigenvalue is significantly smaller than the others
        // (otherwise normals don't form a plane perpendicular to any axis)
        if (eval.z > 0.3f * eval.x) return false;

        // Step 2: Project vertices onto plane perpendicular to axis. Fit circle.
        Vector3 centroid = Vector3.zero;
        foreach (Vector3 v in uniqueVertices) centroid += v;
        centroid /= uniqueVertices.Count;

        // Build 2D basis perpendicular to axis
        Vector3 e1 = Vector3.Cross(axis, Mathf.Abs(axis.y) < 0.9f ? Vector3.up : Vector3.right);
        e1 = e1.normalized;
        Vector3 e2 = Vector3.Cross(axis, e1).normalized;

        // Project vertices to 2D and along axis
        var u = new float[uniqueVertices.Count];
        var w = new float[uniqueVertices.Count];
        var h = new float[uniqueVertices.Count];

        for (var i = 0; i < uniqueVertices.Count; i++)
        {
            Vector3 d = uniqueVertices[i] - centroid;
            u[i] = Vector3.Dot(d, e1);
            w[i] = Vector3.Dot(d, e2);
            h[i] = Vector3.Dot(d, axis);
        }

        // Kasa circle fit: minimize Σ((x-cx)² + (y-cy)² - r²)²
        // Reduces to linear system: A·[cx, cy, r²-cx²-cy²]ᵀ = b
        float sU = 0, sW = 0, sU2 = 0, sW2 = 0, sUw = 0;
        float sU3 = 0, sW3 = 0, sU2W = 0, sUw2 = 0;
        int n = uniqueVertices.Count;

        for (var i = 0; i < n; i++)
        {
            float ui = u[i], wi = w[i];
            sU += ui;
            sW += wi;
            sU2 += ui * ui;
            sW2 += wi * wi;
            sUw += ui * wi;
            sU3 += ui * ui * ui;
            sW3 += wi * wi * wi;
            sU2W += ui * ui * wi;
            sUw2 += ui * wi * wi;
        }

        float floatD = n * (sU2 * sW2 - sUw * sUw) - sU * (sU * sW2 - sUw * sW) + sW * (sU * sUw - sU2 * sW);
        if (Mathf.Abs(floatD) < 1e-10f) return false;

        // Need full Cramer's rule for 2-parameter circle fit (cx, cy)
        // Simplified: just solve the 2x2 system from derivatives
        float a11 = sU2 - sU * sU / n;
        float a12 = sUw - sU * sW / n;
        float a22 = sW2 - sW * sW / n;
        float b1 = 0.5f * (sU3 + sUw2 - sU * (sU2 + sW2) / n);
        float b2 = 0.5f * (sU2W + sW3 - sW * (sU2 + sW2) / n);

        float det = a11 * a22 - a12 * a12;
        if (Mathf.Abs(det) < 1e-10f) return false;

        float cx = (b1 * a22 - b2 * a12) / det;
        float cy = (a11 * b2 - a12 * b1) / det;

        // Compute radius
        var rSum = 0f;

        for (var i = 0; i < n; i++)
        {
            float dx = u[i] - cx;
            float dy = w[i] - cy;
            rSum += Mathf.Sqrt(dx * dx + dy * dy);
        }

        float rMean = rSum / n;

        if (rMean < 1e-6f) return false;

        // Check fit quality
        var maxDev = 0f;

        for (var i = 0; i < n; i++)
        {
            float dx = u[i] - cx;
            float dy = w[i] - cy;
            float r = Mathf.Sqrt(dx * dx + dy * dy);
            float dev = Mathf.Abs(r - rMean) / rMean;
            if (dev > maxDev) maxDev = dev;
        }

        if (maxDev > Tolerance) return false;

        // Step 3: Compute height along axis
        float hMin = float.MaxValue, hMax = float.MinValue;

        for (var i = 0; i < n; i++)
        {
            if (h[i] < hMin) hMin = h[i];
            if (h[i] > hMax) hMax = h[i];
        }

        float height = hMax - hMin;
        if (height < 1e-6f) return false;

        // Compute 3D center
        Vector3 center2D = cx * e1 + cy * e2;
        float hMid = (hMin + hMax) * 0.5f;
        Vector3 center = centroid + center2D + hMid * axis;

        // Unity cylinder: radius 0.5, height 2, Y-axis
        // Scale: X,Z = 2*r (diameter), Y = height/2
        Quaternion rotation = Quaternion.FromToRotation(Vector3.up, axis);

        result = new ModelPrimitive
        {
            Type = PrimitiveType.Cylinder,
            Center = center,
            Rotation = rotation,
            Scale = new Vector3(2f * rMean, height / 2f, 2f * rMean),
            Color = faces[0].Color,
        };
        return true;
    }
}