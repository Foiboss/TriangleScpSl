using UnityEngine;

namespace TriangleScpSl.Core.Decomposition.NGonDecomposition.Detectors;

public static partial class CubeDetector
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

        if (faces.Count < config.CubeMinFaces)
            return false;

        var normals = new List<Vector3>();

        foreach (NGonRaw face in faces)
        {
            if (face.Vertices.Count < 3) continue;
            Vector3 n = NGonMath.NewellNormal(face.Vertices);
            float area = n.magnitude;
            if (area < 1e-10f) continue;
            normals.Add(n / area);
        }

        if (normals.Count < config.CubeMinFaces) return false;

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

        if (directions.Count != 3) return false;

        for (var i = 0; i < 3; i++)
        {
            for (int j = i + 1; j < 3; j++)
            {
                if (Mathf.Abs(Vector3.Dot(directions[i], directions[j])) > config.CubeOrthogonalityTolerance)
                    return false;
            }
        }

        if (Vector3.Dot(Vector3.Cross(directions[0], directions[1]), directions[2]) < 0)
            directions[2] = -directions[2];

        Vector3 centroid = Vector3.zero;
        foreach (Vector3 v in uniqueVertices) centroid += v;
        centroid /= uniqueVertices.Count;

        float[] minProj = [float.MaxValue, float.MaxValue, float.MaxValue];
        float[] maxProj = [float.MinValue, float.MinValue, float.MinValue];

        foreach (Vector3 v in uniqueVertices)
        {
            Vector3 d = v - centroid;

            for (var axis = 0; axis < 3; axis++)
            {
                float proj = Vector3.Dot(d, directions[axis]);
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
            boxCenter += mid * directions[axis];
        }

        float maxExtent = Mathf.Max(extents[0], Mathf.Max(extents[1], extents[2]));

        foreach (Vector3 v in uniqueVertices)
        {
            Vector3 d = v - boxCenter;
            var proj = new float[3];
            var onSurface = false;

            for (var axis = 0; axis < 3; axis++)
            {
                proj[axis] = Vector3.Dot(d, directions[axis]);
                float halfExtent = extents[axis] * 0.5f;

                if (Mathf.Abs(proj[axis]) > halfExtent * (1f + config.CubeVertexTolerance))
                    return false;

                if (Mathf.Abs(Mathf.Abs(proj[axis]) - halfExtent) < halfExtent * config.CubeVertexTolerance + maxExtent * 1e-4f)
                    onSurface = true;
            }

            if (!onSurface) return false;
        }

        // Validate face normals point outward from box center.
        // Rejects boxes viewed from inside (all normals pointing inward).
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

        // Check which of the 6 face directions are covered by actual faces.
        // Any missing direction represents a hidden face that must be inside solid.
        {
            var hasFace = new bool[6];

            foreach (Vector3 n in normals)
            {
                for (var axis = 0; axis < 3; axis++)
                {
                    float dot = Vector3.Dot(n, directions[axis]);
                    if (dot > 1f - config.CubeNormalTolerance) hasFace[axis * 2] = true;
                    if (dot < -(1f - config.CubeNormalTolerance)) hasFace[axis * 2 + 1] = true;
                }
            }

            for (var i = 0; i < 6; i++)
            {
                if (hasFace[i]) continue;

                // Missing face direction — need solid volume to verify it's embedded
                if (solid == null) return false;

                int axis = i / 2;
                float sign = i % 2 == 0 ? 1f : -1f;
                Vector3 faceCenter = boxCenter + sign * (extents[axis] * 0.5f) * directions[axis];

                int ax1 = (axis + 1) % 3;
                int ax2 = (axis + 2) % 3;

                for (var u = 0; u <= 2; u++)
                for (var v = 0; v <= 2; v++)
                {
                    float tu = (u / 2f - 0.5f) * extents[ax1] * 0.8f;
                    float tv = (v / 2f - 0.5f) * extents[ax2] * 0.8f;
                    Vector3 sample = faceCenter + tu * directions[ax1] + tv * directions[ax2];

                    if (!solid.IsSolid(sample))
                        return false;
                }
            }

            // Verify at least one visible face has empty space on its exterior side.
            // Rejects cubes fully enclosed in solid (e.g. viewed from inside a room).
            // Allows partially embedded cubes where some faces are exposed.
            if (solid != null)
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
                    Vector3 faceCenter = boxCenter + sign * (extents[axis] * 0.5f) * directions[axis];
                    Vector3 exteriorPoint = faceCenter + sign * offset * directions[axis];

                    if (solid.IsSolid(exteriorPoint))
                        solidExteriorCount++;
                }

                if (visibleCount > 0 && solidExteriorCount >= visibleCount)
                    return false;
            }
        }

        Quaternion rotation = Quaternion.LookRotation(directions[2], directions[1]);

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

    public static bool TryDetectPartial
    (
        List<NGonRaw> faces,
        List<Vector3> uniqueVertices,
        ModelSolidVolume solid,
        NGonModelConfig config,
        out ModelPrimitive result)
    {
        result = null!;

        if (faces.Count < 3 || faces.Count > 20 || uniqueVertices.Count < 4)
            return false;

        if (TryDetectPartialFromNormals(faces, uniqueVertices, solid, config, out result))
            return true;

        if (TryDetectPartialFromObb(faces, uniqueVertices, solid, config, out result))
            return true;

        return false;
    }
}