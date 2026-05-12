using UnityEngine;

namespace TriangleScpSl.Core.NGons.Detectors;

public static class CylinderDetector
{
    const float Tolerance = 0.05f;
    const float ApproxTolerance = 0.12f;
    const int MinFaces = 6;
    const float MinEigenRatio = 0.5f;
    const float MinNormalsOutwardFraction = 0.7f;

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

        if (!TryFitCylinder(faces, uniqueVertices, Tolerance, out result))
            return false;

        return true;
    }

    /// <summary>
    ///     Approximate cylinder detection with relaxed tolerances, for use with solid volume verification.
    /// </summary>
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

        if (faces.Count < MinFaces || uniqueVertices.Count < 6)
            return false;

        if (!SmoothnessCheck.IsSurfaceSmooth(faces, smoothMaxAngle, smoothMinFraction))
            return false;

        if (!TryFitCylinder(faces, uniqueVertices, ApproxTolerance, out result))
            return false;

        // Verify that hidden parts of the cylinder are inside solid material
        if (!VerifyHiddenSurfaceInsideSolid(result, faces, solid))
        {
            result = null!;
            return false;
        }

        return true;
    }

    static bool TryFitCylinder
    (
        List<NGonRaw> faces,
        List<Vector3> uniqueVertices,
        float tolerance,
        out ModelPrimitive result)
    {
        result = null!;

        // Step 1: Find cylinder axis via normal-covariance matrix.
        // Lateral face normals are perpendicular to the axis, so the axis
        // corresponds to the SMALLEST eigenvalue.
        float nxx = 0, nxy = 0, nxz = 0, nyy = 0, nyz = 0, nzz = 0;
        var faceNormals = new List<Vector3>(faces.Count);

        foreach (NGonRaw face in faces)
        {
            List<Vector3> v = face.Vertices;
            if (v.Count < 3) continue;

            Vector3 normal = NGonMath.NewellNormal(v);
            float area = normal.magnitude;
            if (area < 1e-10f) continue;
            normal /= area;

            faceNormals.Add(normal);

            nxx += area * normal.x * normal.x;
            nxy += area * normal.x * normal.y;
            nxz += area * normal.x * normal.z;
            nyy += area * normal.y * normal.y;
            nyz += area * normal.y * normal.z;
            nzz += area * normal.z * normal.z;
        }

        if (faceNormals.Count < MinFaces)
            return false;

        SphereDetector.Eigen3X3Internal(
            nxx, nxy, nxz, nyy, nyz, nzz,
            out Vector3 eval, out _, out _, out Vector3 axis);

        if (axis.sqrMagnitude < 1e-10f) return false;
        axis = axis.normalized;

        // Check that the smallest eigenvalue is significantly smaller than the others.
        // Relaxed: for low-poly cylinders, the ratio can be higher.
        if (eval.z > MinEigenRatio * eval.x) return false;

        // Additional check: normals should be roughly perpendicular to the detected axis.
        // Count how many face normals are ~perpendicular to the axis (dot product near 0).
        var perpCount = 0;

        foreach (Vector3 fn in faceNormals)
        {
            if (Mathf.Abs(Vector3.Dot(fn, axis)) < 0.5f)
                perpCount++;
        }

        // At least 60% of faces should have normals perpendicular to the axis (lateral faces)
        if (perpCount < faceNormals.Count * 0.6f) return false;

        // Step 2: Project vertices onto plane perpendicular to axis. Fit circle.
        Vector3 centroid = Vector3.zero;
        foreach (Vector3 v in uniqueVertices) centroid += v;
        centroid /= uniqueVertices.Count;

        Vector3 e1 = Vector3.Cross(axis, Mathf.Abs(axis.y) < 0.9f ? Vector3.up : Vector3.right);
        e1 = e1.normalized;
        Vector3 e2 = Vector3.Cross(axis, e1).normalized;

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

        // Kasa circle fit
        int n = uniqueVertices.Count;
        float sU = 0, sW = 0, sU2 = 0, sW2 = 0, sUw = 0;
        float sU3 = 0, sW3 = 0, sU2W = 0, sUw2 = 0;

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

        // Check fit quality — use mean deviation instead of max to be more robust
        var maxDev = 0f;
        var sumDev = 0f;

        for (var i = 0; i < n; i++)
        {
            float dx = u[i] - cx;
            float dy = w[i] - cy;
            float r = Mathf.Sqrt(dx * dx + dy * dy);
            float dev = Mathf.Abs(r - rMean) / rMean;
            sumDev += dev;
            if (dev > maxDev) maxDev = dev;
        }

        float meanDev = sumDev / n;

        // Reject if mean deviation too high or max deviation way too high
        if (meanDev > tolerance) return false;
        if (maxDev > tolerance * 3f) return false;

        // Step 3: Compute height along axis
        float hMin = float.MaxValue, hMax = float.MinValue;

        for (var i = 0; i < n; i++)
        {
            if (h[i] < hMin) hMin = h[i];
            if (h[i] > hMax) hMax = h[i];
        }

        float height = hMax - hMin;
        if (height < 1e-6f) return false;

        // Step 4: Validate normals point outward from axis
        var outwardCount = 0;
        Vector3 axisPoint = centroid + cx * e1 + cy * e2;

        for (var i = 0; i < faceNormals.Count; i++)
        {
            Vector3 fn = faceNormals[i];

            // Skip cap normals (parallel to axis)
            if (Mathf.Abs(Vector3.Dot(fn, axis)) > 0.5f)
            {
                outwardCount++;
                continue;
            }

            // For lateral faces, the normal projected onto the perpendicular plane
            // should point away from the axis
            List<Vector3> verts = faces[i].Vertices;
            Vector3 faceCentroid = Vector3.zero;
            foreach (Vector3 v in verts) faceCentroid += v;
            faceCentroid /= verts.Count;

            Vector3 toFace = faceCentroid - axisPoint;
            toFace -= Vector3.Dot(toFace, axis) * axis; // project to perp plane

            if (Vector3.Dot(toFace, fn) > 0)
                outwardCount++;
        }

        if (outwardCount < faceNormals.Count * MinNormalsOutwardFraction) return false;

        // Compute 3D center
        Vector3 center2D = cx * e1 + cy * e2;
        float hMid = (hMin + hMax) * 0.5f;
        Vector3 center = centroid + center2D + hMid * axis;

        // Unity cylinder: radius 0.5, height 2, Y-axis
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

    /// <summary>
    ///     For approximate cylinder detection, verify that parts of the cylinder
    ///     surface not covered by faces are inside solid material.
    /// </summary>
    static bool VerifyHiddenSurfaceInsideSolid
    (
        ModelPrimitive cylinder,
        List<NGonRaw> faces,
        ModelSolidVolume solid)
    {
        Vector3 center = cylinder.Center;
        Vector3 up = cylinder.Rotation * Vector3.up;
        float halfHeight = cylinder.Scale.y; // Scale.y = height/2
        float radius = cylinder.Scale.x * 0.5f;

        // Sample points on the cylinder surface and check if uncovered ones are inside solid
        const int angleSteps = 16;
        const int heightSteps = 4;

        Vector3 e1 = cylinder.Rotation * Vector3.right;
        Vector3 e2 = cylinder.Rotation * Vector3.forward;

        for (var ai = 0; ai < angleSteps; ai++)
        {
            float angle = ai * 2f * Mathf.PI / angleSteps;
            Vector3 radial = Mathf.Cos(angle) * e1 + Mathf.Sin(angle) * e2;

            for (var hi = 0; hi <= heightSteps; hi++)
            {
                float t = hi / (float)heightSteps;
                float hOffset = Mathf.Lerp(-halfHeight, halfHeight, t);
                Vector3 surfacePoint = center + hOffset * up + radius * radial;

                // Check if any face covers this point (approximate: check if point
                // is near any face centroid's angular region)
                var covered = false;

                foreach (NGonRaw face in faces)
                {
                    List<Vector3> verts = face.Vertices;
                    if (verts.Count < 3) continue;

                    Vector3 fc = Vector3.zero;
                    foreach (Vector3 v in verts) fc += v;
                    fc /= verts.Count;

                    if ((fc - surfacePoint).sqrMagnitude < radius * radius * 0.25f)
                    {
                        covered = true;
                        break;
                    }
                }

                if (covered) continue;

                // Uncovered surface point — must be inside solid
                if (!solid.IsSolid(surfacePoint))
                    return false;
            }
        }

        return true;
    }
}