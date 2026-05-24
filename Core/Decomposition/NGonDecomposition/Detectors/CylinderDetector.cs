using UnityEngine;

namespace TriangleScpSl.Core.Decomposition.NGonDecomposition.Detectors;

public static class CylinderDetector
{
    public static bool TryDetect
    (
        List<NGonRaw> faces,
        List<Vector3> uniqueVertices,
        out ModelPrimitive result,
        NGonModelConfig config)
    {
        result = null!;

        if (faces.Count < config.CylinderMinFaces || uniqueVertices.Count < 4)
            return false;

        if (!TryFitCylinder(faces, uniqueVertices, config.CylinderTolerance, config, out result))
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
        NGonModelConfig config)
    {
        result = null!;

        if (faces.Count < config.CylinderMinFaces || uniqueVertices.Count < 4)
            return false;

        if (!TryFitCylinder(faces, uniqueVertices, config.CylinderApproxTolerance, config, out result))
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
        NGonModelConfig config,
        out ModelPrimitive result)
    {
        result = null!;

        // Find cylinder axis via normal-covariance matrix.
        // Lateral face normals are perpendicular to the axis, so the axis
        // corresponds to the SMALLEST eigenvalue.
        float nxx = 0, nxy = 0, nxz = 0, nyy = 0, nyz = 0, nzz = 0;
        var faceNormals = new List<Vector3>(faces.Count);
        var faceNormalIndices = new List<int>(faces.Count);

        for (var fi = 0; fi < faces.Count; fi++)
        {
            List<Vector3> v = faces[fi].Vertices;
            if (v.Count < 3) continue;

            Vector3 normal = NGonMath.NewellNormal(v);
            float area = normal.magnitude;
            if (area < 1e-10f) continue;
            normal /= area;

            faceNormals.Add(normal);
            faceNormalIndices.Add(fi);

            nxx += area * normal.x * normal.x;
            nxy += area * normal.x * normal.y;
            nxz += area * normal.x * normal.z;
            nyy += area * normal.y * normal.y;
            nyz += area * normal.y * normal.z;
            nzz += area * normal.z * normal.z;
        }

        if (faceNormals.Count < config.CylinderMinFaces)
            return false;

        SphereDetector.Eigen3X3Internal(
            nxx, nxy, nxz, nyy, nyz, nzz,
            out Vector3 eval, out _, out _, out Vector3 axis);

        if (axis.sqrMagnitude < 1e-10f) return false;
        axis = axis.normalized;

        // Check that the smallest eigenvalue is significantly smaller than the others.
        if (eval.z > config.CylinderMinEigenRatio * eval.x) return false;

        // Reject near-coplanar faces: if both middle and smallest eigenvalues are small,
        // normals only span one direction (flat surface, not a cylinder).
        if (eval.y < 0.3f * eval.x) return false;

        // Classify faces as lateral (perpendicular to axis) or cap (parallel to axis).
        var lateralFaceIndices = new List<int>();
        var capFaceIndices = new List<int>();

        for (var ni = 0; ni < faceNormals.Count; ni++)
        {
            float axisDot = Mathf.Abs(Vector3.Dot(faceNormals[ni], axis));

            if (axisDot < 0.5f)
                lateralFaceIndices.Add(ni);
            else
                capFaceIndices.Add(ni);
        }

        // At least 50% of faces should be lateral
        if (lateralFaceIndices.Count < faceNormals.Count * 0.5f) return false;

        // Smoothness check on lateral faces only (cap-lateral edges have ~90° angles by design).
        // Adaptive angle: a cylinder with N lateral faces has dihedral angle 2π/N between
        // adjacent laterals. A 6-sided cylinder has 60° angles, 8-sided has 45°, etc.
        // Use 1.3x multiplier to allow slight irregularity.
        if (lateralFaceIndices.Count >= 3)
        {
            var lateralFaces = new List<NGonRaw>(lateralFaceIndices.Count);

            foreach (int ni in lateralFaceIndices)
                lateralFaces.Add(faces[faceNormalIndices[ni]]);

            float expectedAngle = 2f * Mathf.PI / Mathf.Max(3f, lateralFaceIndices.Count);
            float adaptiveAngle = Mathf.Min(Mathf.Max(config.SmoothMaxAngle, expectedAngle * 1.3f), 1.2f);

            if (!SmoothnessCheck.IsSurfaceSmooth(lateralFaces, adaptiveAngle, config.SmoothMinFraction))
                return false;
        }

        // Collect lateral vertices for circle fitting (excluding cap-only vertices)
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

        // Kasa circle fit on all vertices
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

        // Compute radius from lateral vertices only for better accuracy
        var rSum = 0f;
        var rCount = 0;

        // Collect which vertices belong to lateral faces
        var isLateralVertex = new HashSet<int>();

        foreach (int ni in lateralFaceIndices)
        {
            int fi = faceNormalIndices[ni];

            foreach (Vector3 v in faces[fi].Vertices)
            {
                // Find closest vertex in uniqueVertices
                for (var vi = 0; vi < uniqueVertices.Count; vi++)
                {
                    if ((uniqueVertices[vi] - v).sqrMagnitude < 1e-8f)
                    {
                        isLateralVertex.Add(vi);
                        break;
                    }
                }
            }
        }

        // If we have enough lateral vertices, use them for radius; otherwise use all
        if (isLateralVertex.Count >= 4)
        {
            foreach (int vi in isLateralVertex)
            {
                float dx = u[vi] - cx;
                float dy = w[vi] - cy;
                rSum += Mathf.Sqrt(dx * dx + dy * dy);
                rCount++;
            }
        }
        else
        {
            for (var i = 0; i < n; i++)
            {
                float dx = u[i] - cx;
                float dy = w[i] - cy;
                rSum += Mathf.Sqrt(dx * dx + dy * dy);
            }

            rCount = n;
        }

        float rMean = rSum / rCount;

        if (rMean < 1e-6f) return false;

        // Verify lateral face centroids are approximately at the cylinder radius.
        // Boxes have face centroids at ~71% of vertex radius (cos(45°) for square
        // cross-section). Real cylinders with ≥6 sides have centroids at ≥87%.
        {
            Vector3 circleCtr = centroid + cx * e1 + cy * e2;
            float latCentroidSum = 0;
            var latCentroidCount = 0;

            foreach (int ni in lateralFaceIndices)
            {
                int fi = faceNormalIndices[ni];
                List<Vector3> fverts = faces[fi].Vertices;
                Vector3 fc = Vector3.zero;
                foreach (Vector3 fv in fverts) fc += fv;
                fc /= fverts.Count;
                Vector3 toFc = fc - circleCtr;
                toFc -= Vector3.Dot(toFc, axis) * axis;
                latCentroidSum += toFc.magnitude;
                latCentroidCount++;
            }

            if (latCentroidCount >= 3 && latCentroidSum / latCentroidCount < rMean * 0.80f)
                return false;
        }

        // Check fit quality on lateral vertices (cap vertices can be at center)
        var maxDev = 0f;
        var sumDev = 0f;
        var devCount = 0;

        for (var i = 0; i < n; i++)
        {
            float dx = u[i] - cx;
            float dy = w[i] - cy;
            float r = Mathf.Sqrt(dx * dx + dy * dy);

            // Skip vertices clearly on caps (near center, small radial distance)
            if (r < rMean * 0.3f && !isLateralVertex.Contains(i))
                continue;

            float dev = Mathf.Abs(r - rMean) / rMean;
            sumDev += dev;
            devCount++;
            if (dev > maxDev) maxDev = dev;
        }

        if (devCount == 0) return false;
        float meanDev = sumDev / devCount;

        // Reject if mean deviation too high or max deviation way too high
        if (meanDev > tolerance) return false;
        if (maxDev > tolerance * 3f) return false;

        // Compute height along axis
        float hMin = float.MaxValue, hMax = float.MinValue;

        for (var i = 0; i < n; i++)
        {
            if (h[i] < hMin) hMin = h[i];
            if (h[i] > hMax) hMax = h[i];
        }

        float height = hMax - hMin;
        if (height < 1e-6f) return false;

        // Validate normals point outward from axis (using correct face index mapping)
        var outwardCount = 0;
        Vector3 axisPoint = centroid + cx * e1 + cy * e2;

        for (var ni = 0; ni < faceNormals.Count; ni++)
        {
            Vector3 fn = faceNormals[ni];
            int fi = faceNormalIndices[ni];

            // Skip cap normals (parallel to axis)
            if (Mathf.Abs(Vector3.Dot(fn, axis)) > 0.5f)
            {
                outwardCount++;
                continue;
            }

            // For lateral faces, the normal projected onto the perpendicular plane
            // should point away from the axis
            List<Vector3> verts = faces[fi].Vertices;
            Vector3 faceCentroid = Vector3.zero;
            foreach (Vector3 v in verts) faceCentroid += v;
            faceCentroid /= verts.Count;

            Vector3 toFace = faceCentroid - axisPoint;
            toFace -= Vector3.Dot(toFace, axis) * axis; // project to perp plane

            if (Vector3.Dot(toFace, fn) > 0)
                outwardCount++;
        }

        if (outwardCount < faceNormals.Count * config.CylinderMinNormalsOutwardFraction) return false;

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

        float coverDistSq = radius * radius * 0.25f;

        for (var ai = 0; ai < angleSteps; ai++)
        {
            float angle = ai * 2f * Mathf.PI / angleSteps;
            Vector3 radial = Mathf.Cos(angle) * e1 + Mathf.Sin(angle) * e2;

            for (var hi = 0; hi <= heightSteps; hi++)
            {
                float t = hi / (float)heightSteps;
                float hOffset = Mathf.Lerp(-halfHeight, halfHeight, t);
                Vector3 surfacePoint = center + hOffset * up + radius * radial;

                if (IsPointCoveredByFace(surfacePoint, faces, coverDistSq))
                    continue;

                if (!solid.IsSolid(surfacePoint))
                    return false;
            }
        }

        // Check cap disk interiors: uncovered cap points must be inside solid.
        // Prevents cylinders whose caps span empty space (e.g. torus hole).
        const int capRadialSteps = 3;
        const int capAngleSteps = 6;

        for (var cap = 0; cap < 2; cap++)
        {
            float hOffset = cap == 0 ? -halfHeight : halfHeight;
            Vector3 capCenter = center + hOffset * up;

            for (var ri = 0; ri <= capRadialSteps; ri++)
            {
                float r = radius * ri / (capRadialSteps + 1); // 0, r/4, r/2, 3r/4
                int aSteps = ri == 0 ? 1 : capAngleSteps;

                for (var ai = 0; ai < aSteps; ai++)
                {
                    float angle = ai * 2f * Mathf.PI / aSteps;
                    Vector3 pt = capCenter;

                    if (ri > 0)
                        pt += r * (Mathf.Cos(angle) * e1 + Mathf.Sin(angle) * e2);

                    if (IsPointCoveredByFace(pt, faces, coverDistSq))
                        continue;

                    if (!solid.IsSolid(pt))
                        return false;
                }
            }
        }

        return true;
    }

    static bool IsPointCoveredByFace(Vector3 point, List<NGonRaw> faces, float coverDistSq)
    {
        foreach (NGonRaw face in faces)
        {
            List<Vector3> verts = face.Vertices;
            if (verts.Count < 3) continue;

            Vector3 fc = Vector3.zero;
            foreach (Vector3 v in verts) fc += v;
            fc /= verts.Count;

            if ((fc - point).sqrMagnitude < coverDistSq)
                return true;
        }

        return false;
    }
}