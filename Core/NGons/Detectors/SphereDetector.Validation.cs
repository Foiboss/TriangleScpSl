using UnityEngine;

namespace TriangleScpSl.Core.NGons.Detectors;

public static partial class SphereDetector
{
    static bool ValidateNormalsOutward(List<NGonRaw> faces, Vector3 center)
    {
        int outward = 0, inward = 0;

        foreach (NGonRaw face in faces)
        {
            if (face.Vertices.Count < 3) continue;
            Vector3 normal = NGonMath.NewellNormal(face.Vertices);
            Vector3 faceCentroid = FaceCentroid(face.Vertices);
            float dot = Vector3.Dot(normal, faceCentroid - center);

            if (dot > 0f) outward++;
            else inward++;
        }

        return outward > inward;
    }

    static float ComputeCoverage(List<NGonRaw> faces, Vector3 center)
    {
        var totalSolidAngle = 0.0;

        foreach (NGonRaw face in faces)
        {
            List<Vector3> v = face.Vertices;
            if (v.Count < 3) continue;

            for (var i = 1; i < v.Count - 1; i++)
            {
                totalSolidAngle += TriangleSolidAngle(center, v[0], v[i], v[i + 1]);
            }
        }

        return Mathf.Abs((float)totalSolidAngle);
    }

    static bool VerifyHiddenSurfaceInsideSolid
    (
        Vector3 center, float radius,
        List<NGonRaw> coveredFaces,
        ModelSolidVolume solid)
    {
        // Fibonacci sphere sampling for uniform distribution
        var samples = new List<Vector3>(HiddenSurfaceSamples);
        float goldenAngle = Mathf.PI * (3f - Mathf.Sqrt(5f));

        for (var i = 0; i < HiddenSurfaceSamples; i++)
        {
            float y = 1f - 2f * i / (HiddenSurfaceSamples - 1);
            float rAtY = Mathf.Sqrt(1f - y * y);
            float theta = goldenAngle * i;
            float x = Mathf.Cos(theta) * rAtY;
            float z = Mathf.Sin(theta) * rAtY;

            Vector3 surfacePoint = center + new Vector3(x, y, z) * radius;
            samples.Add(surfacePoint);
        }

        // Adaptive coverage distance: scale by face density so coarse meshes aren't penalized.
        // For N faces covering a sphere, each face spans roughly sqrt(4π/N) radians,
        // giving a linear extent of radius * 2 * sqrt(π/N).
        float faceExtent = radius * 2f * Mathf.Sqrt(Mathf.PI / Mathf.Max(4f, coveredFaces.Count));
        float coverDist = Mathf.Max(faceExtent, radius * 0.2f);
        float coverDistSq = coverDist * coverDist;

        foreach (Vector3 sample in samples)
        {
            var coveredByFace = false;

            foreach (NGonRaw face in coveredFaces)
            {
                Vector3 fc = FaceCentroid(face.Vertices);

                if ((sample - fc).sqrMagnitude < coverDistSq)
                {
                    coveredByFace = true;
                    break;
                }
            }

            if (coveredByFace) continue;

            // Pull sample slightly inward to avoid boundary ambiguity
            Vector3 inward = Vector3.Lerp(sample, center, 0.05f);

            if (!solid.IsSolid(inward))
                return false;
        }

        return true;
    }

    static double TriangleSolidAngle(Vector3 p, Vector3 a, Vector3 b, Vector3 c)
    {
        Vector3 pa = a - p;
        Vector3 pb = b - p;
        Vector3 pc = c - p;

        float la = pa.magnitude;
        float lb = pb.magnitude;
        float lc = pc.magnitude;

        if (la < 1e-7f || lb < 1e-7f || lc < 1e-7f) return 0.0;

        float num = Vector3.Dot(pa, Vector3.Cross(pb, pc));

        float den = la * lb * lc
            + Vector3.Dot(pa, pb) * lc
            + Vector3.Dot(pb, pc) * la
            + Vector3.Dot(pc, pa) * lb;

        return 2.0 * Math.Atan2(num, den);
    }

    static Vector3 FaceCentroid(List<Vector3> verts)
    {
        Vector3 sum = Vector3.zero;
        foreach (Vector3 v in verts) sum += v;
        return sum / verts.Count;
    }

    /// <summary>
    ///     Checks that face centroids are approximately at the expected radius from center.
    ///     For a real sphere, face centroids ≈ radius. For a cube, face centroids are only
    ///     ~58% of vertex radius. This rejects boxes masquerading as spheres.
    /// </summary>
    static bool ValidateCentroidRadius(List<NGonRaw> faces, Vector3 center, float expectedRadius, float minRatio)
    {
        if (expectedRadius < 1e-6f) return false;

        float centroidDistSum = 0;
        var count = 0;

        foreach (NGonRaw face in faces)
        {
            if (face.Vertices.Count < 3) continue;
            centroidDistSum += (FaceCentroid(face.Vertices) - center).magnitude;
            count++;
        }

        if (count == 0) return false;

        return centroidDistSum / count >= expectedRadius * minRatio;
    }
}