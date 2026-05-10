using UnityEngine;

namespace TriangleScpSl.Core.NGons.Detectors;

public static partial class CubeDetector
{
    const float NormalTolerance = 0.087f; // ~5 degrees
    const float OrthogonalityTolerance = 0.05f;
    const float VertexTolerance = 0.01f;
    const float RelaxedVertexTolerance = 0.03f;
    const int MinFaces = 6;

    public static bool TryDetect
    (
        List<NGonRaw> faces,
        List<Vector3> uniqueVertices,
        out ModelPrimitive result)
    {
        result = null!;

        if (faces.Count < MinFaces)
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

        if (normals.Count < MinFaces) return false;

        var directions = new List<Vector3>();
        var directionCounts = new List<int>();

        foreach (Vector3 n in normals)
        {
            var found = false;

            for (var d = 0; d < directions.Count; d++)
            {
                float dot = Mathf.Abs(Vector3.Dot(n, directions[d]));

                if (dot > 1f - NormalTolerance)
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
                if (Mathf.Abs(Vector3.Dot(directions[i], directions[j])) > OrthogonalityTolerance)
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

                if (Mathf.Abs(proj[axis]) > halfExtent * (1f + VertexTolerance))
                    return false;

                if (Mathf.Abs(Mathf.Abs(proj[axis]) - halfExtent) < halfExtent * VertexTolerance + maxExtent * 1e-4f)
                    onSurface = true;
            }

            if (!onSurface) return false;
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
        out ModelPrimitive result)
    {
        result = null!;

        if (faces.Count < 3 || faces.Count > 20 || uniqueVertices.Count < 4)
            return false;

        if (TryDetectPartialFromNormals(faces, uniqueVertices, solid, out result))
            return true;

        if (TryDetectPartialFromObb(faces, uniqueVertices, solid, out result))
            return true;

        return false;
    }
}