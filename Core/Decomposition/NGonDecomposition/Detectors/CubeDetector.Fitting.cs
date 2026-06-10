using TriangleScpSl.Core.Decomposition.NGonDecomposition.Geometry;
using TriangleScpSl.Core.Decomposition.NGonDecomposition.Parsing;
using UnityEngine;

namespace TriangleScpSl.Core.Decomposition.NGonDecomposition.Detectors;

public static partial class CubeDetector
{
    static bool TryDetectPartialFromNormals
    (
        List<NGonRaw> faces,
        List<Vector3> uniqueVertices,
        ModelSolidVolume solid,
        NGonModelConfig config,
        out ModelPrimitive result)
    {
        result = null!;

        var normals = new List<Vector3>();

        foreach (NGonRaw face in faces)
        {
            if (face.Vertices.Count < 3) continue;
            Vector3 n = NGonMath.NewellNormal(face.Vertices);
            float area = n.magnitude;
            if (area < 1e-10f) continue;
            normals.Add(n / area);
        }

        // 2 orthogonal faces are enough — the third axis comes from the cross product.
        if (normals.Count < 2) return false;

        var directions = new List<Vector3>();
        var directionCounts = new List<int>();

        foreach (Vector3 n in normals)
        {
            var found = false;

            for (var d = 0; d < directions.Count; d++)
            {
                float dot = Mathf.Abs(Vector3.Dot(n, directions[d]));

                if (dot > 1f - config.CubeNormalTolerance)
                {
                    if (Vector3.Dot(n, directions[d]) > 0)
                        directions[d] = (directions[d] * directionCounts[d] + n) / (directionCounts[d] + 1);
                    else
                        directions[d] = (directions[d] * directionCounts[d] - n) / (directionCounts[d] + 1);
                    directions[d] = directions[d].normalized;
                    directionCounts[d]++;
                    found = true;
                    break;
                }
            }

            if (!found)
            {
                directions.Add(n);
                directionCounts.Add(1);
            }
        }

        if (directions.Count < 2 || directions.Count > 3)
            return false;

        for (var i = 0; i < directions.Count; i++)
        {
            for (int j = i + 1; j < directions.Count; j++)
            {
                if (Mathf.Abs(Vector3.Dot(directions[i], directions[j])) > config.CubeOrthogonalityTolerance)
                    return false;
            }
        }

        if (directions.Count == 2)
            directions.Add(Vector3.Cross(directions[0], directions[1]).normalized);

        if (Vector3.Dot(Vector3.Cross(directions[0], directions[1]), directions[2]) < 0)
            directions[2] = -directions[2];

        return TryFitAndVerifyBox(faces, uniqueVertices, normals,
            directions[0], directions[1], directions[2], solid, config, out result);
    }

    static bool TryDetectPartialFromObb
    (
        List<NGonRaw> faces,
        List<Vector3> uniqueVertices,
        ModelSolidVolume solid,
        NGonModelConfig config,
        out ModelPrimitive result)
    {
        result = null!;

        if (uniqueVertices.Count < 8)
            return false;

        Vector3 centroid = Vector3.zero;
        foreach (Vector3 v in uniqueVertices) centroid += v;
        centroid /= uniqueVertices.Count;

        float cxx = 0, cxy = 0, cxz = 0, cyy = 0, cyz = 0, czz = 0;

        foreach (Vector3 v in uniqueVertices)
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

        float inv = 1f / uniqueVertices.Count;
        cxx *= inv;
        cxy *= inv;
        cxz *= inv;
        cyy *= inv;
        cyz *= inv;
        czz *= inv;

        SphereDetector.Eigen3X3Internal(
            cxx, cxy, cxz, cyy, cyz, czz,
            out _, out Vector3 ev0, out Vector3 ev1, out Vector3 ev2);

        if (Vector3.Dot(Vector3.Cross(ev0, ev1), ev2) < 0)
            ev2 = -ev2;

        var normals = new List<Vector3>();

        foreach (NGonRaw face in faces)
        {
            if (face.Vertices.Count < 3) continue;
            Vector3 n = NGonMath.NewellNormal(face.Vertices);
            float area = n.magnitude;
            if (area < 1e-10f) continue;
            normals.Add(n / area);
        }

        if (normals.Count < 3) return false;

        foreach (Vector3 n in normals)
        {
            var bestAlignment = 0f;
            bestAlignment = Mathf.Max(bestAlignment, Mathf.Abs(Vector3.Dot(n, ev0)));
            bestAlignment = Mathf.Max(bestAlignment, Mathf.Abs(Vector3.Dot(n, ev1)));
            bestAlignment = Mathf.Max(bestAlignment, Mathf.Abs(Vector3.Dot(n, ev2)));

            if (bestAlignment < 1f - config.CubeNormalTolerance * 2f)
                return false;
        }

        return TryFitAndVerifyBox(faces, uniqueVertices, normals,
            ev0, ev1, ev2, solid, config, out result);
    }

    static bool TryFitAndVerifyBox
    (
        List<NGonRaw> faces,
        List<Vector3> uniqueVertices,
        List<Vector3> normals,
        Vector3 dir0, Vector3 dir1, Vector3 dir2,
        ModelSolidVolume solid,
        NGonModelConfig config,
        out ModelPrimitive result)
    {
        result = null!;

        Vector3 centroid = Vector3.zero;
        foreach (Vector3 v in uniqueVertices) centroid += v;
        centroid /= uniqueVertices.Count;

        float[] minProj = [float.MaxValue, float.MaxValue, float.MaxValue];
        float[] maxProj = [float.MinValue, float.MinValue, float.MinValue];
        Vector3[] dirs = [dir0, dir1, dir2];

        foreach (Vector3 v in uniqueVertices)
        {
            Vector3 d = v - centroid;

            for (var axis = 0; axis < 3; axis++)
            {
                float proj = Vector3.Dot(d, dirs[axis]);
                if (proj < minProj[axis]) minProj[axis] = proj;
                if (proj > maxProj[axis]) maxProj[axis] = proj;
            }
        }

        var extents = new float[3];
        Vector3 boxCenter = centroid;

        for (var axis = 0; axis < 3; axis++)
        {
            extents[axis] = maxProj[axis] - minProj[axis];
            if (extents[axis] < 1e-6f) return false;
            float mid = (maxProj[axis] + minProj[axis]) * 0.5f;
            boxCenter += mid * dirs[axis];
        }

        float maxExtent = Mathf.Max(extents[0], Mathf.Max(extents[1], extents[2]));

        foreach (Vector3 v in uniqueVertices)
        {
            Vector3 d = v - boxCenter;
            var onSurface = false;

            for (var axis = 0; axis < 3; axis++)
            {
                float proj = Mathf.Abs(Vector3.Dot(d, dirs[axis]));
                float halfExtent = extents[axis] * 0.5f;

                if (proj > halfExtent * (1f + config.CubeRelaxedVertexTolerance))
                    return false;

                if (Mathf.Abs(proj - halfExtent) < halfExtent * config.CubeRelaxedVertexTolerance + maxExtent * 1e-4f)
                    onSurface = true;
            }

            if (!onSurface) return false;
        }

        // Validate face normals point outward from box center
        {
            int outward = 0, total = 0;

            foreach (NGonRaw face in faces)
            {
                if (face.Vertices.Count < 3) continue;
                Vector3 fn = NGonMath.NewellNormal(face.Vertices);
                float mag = fn.magnitude;
                if (mag < 1e-10f) continue;
                fn /= mag;
                total++;

                Vector3 fc = Vector3.zero;
                foreach (Vector3 v in face.Vertices) fc += v;
                fc /= face.Vertices.Count;

                if (Vector3.Dot(fn, fc - boxCenter) > 0)
                    outward++;
            }

            if (total > 0 && outward * 4 < total * 3)
                return false;
        }

        var hasFace = new bool[6];

        foreach (Vector3 n in normals)
        {
            for (var axis = 0; axis < 3; axis++)
            {
                float dot = Vector3.Dot(n, dirs[axis]);
                if (dot > 1f - config.CubeNormalTolerance) hasFace[axis * 2] = true;
                if (dot < -(1f - config.CubeNormalTolerance)) hasFace[axis * 2 + 1] = true;
            }
        }

        var presentFaces = 0;

        for (var i = 0; i < 6; i++)
        {
            if (hasFace[i]) presentFaces++;
        }

        // 2 visible directions suffice — every hidden direction is verified to be
        // embedded in solid below. 6 visible directions are allowed too: this is
        // the relaxed-tolerance rescue for boxes the exact detector rejected.
        if (presentFaces < 2) return false;

        for (var i = 0; i < 6; i++)
        {
            if (hasFace[i]) continue;

            int axis = i / 2;
            float sign = i % 2 == 0 ? 1f : -1f;
            Vector3 faceCenter = boxCenter + sign * (extents[axis] * 0.5f) * dirs[axis];

            int ax1 = (axis + 1) % 3;
            int ax2 = (axis + 2) % 3;

            for (var u = 0; u <= 2; u++)
            {
                for (var v = 0; v <= 2; v++)
                {
                    float tu = (u / 2f - 0.5f) * extents[ax1] * 0.8f;
                    float tv = (v / 2f - 0.5f) * extents[ax2] * 0.8f;
                    Vector3 sample = faceCenter + tu * dirs[ax1] + tv * dirs[ax2];

                    if (!solid.IsSolid(sample))
                        return false;
                }
            }
        }

        // Verify at least one visible face has empty space on its exterior side.
        // Rejects cubes fully enclosed in solid but allows partially embedded cubes.
        {
            float offset = maxExtent * 0.03f;
            var visibleCount = 0;
            var solidExteriorCount = 0;

            for (var i = 0; i < 6; i++)
            {
                if (!hasFace[i]) continue;
                visibleCount++;

                int axis = i / 2;
                float sign = i % 2 == 0 ? 1f : -1f;
                Vector3 faceCenter = boxCenter + sign * (extents[axis] * 0.5f) * dirs[axis];
                Vector3 exteriorPoint = faceCenter + sign * offset * dirs[axis];

                if (solid.IsSolid(exteriorPoint))
                    solidExteriorCount++;
            }

            if (visibleCount > 0 && solidExteriorCount >= visibleCount)
                return false;
        }

        Quaternion rotation = Quaternion.LookRotation(dirs[2], dirs[1]);

        result = new ModelPrimitive
        {
            Type = PrimitiveType.Cube,
            Center = boxCenter,
            Rotation = rotation,
            Scale = new Vector3(extents[0], extents[1], extents[2]),
            Color = faces[0].Color,
        };
        return true;
    }
}