using UnityEngine;

namespace TriangleScpSl.Core.NGons.Detectors;

public static class SmoothnessCheck
{
    // Default max angle (~18°) and minimum smooth edge fraction (70%)
    public const float DefaultMaxAngle = 0.32f;
    public const float DefaultMinFraction = 0.7f;

    public static bool IsSurfaceSmooth
    (List<NGonRaw> faces,
        float maxAngle = DefaultMaxAngle,
        float minFraction = DefaultMinFraction)
    {
        if (faces.Count < 3)
            return false;

        var normals = new List<Vector3>(faces.Count);
        var faceVertexIndices = new List<List<int>>(faces.Count);

        // Build a simple vertex table for edge adjacency
        var vertTable = new List<Vector3>();
        const float eps2 = 1e-5f * 1e-5f;

        for (var fi = 0; fi < faces.Count; fi++)
        {
            List<Vector3> verts = faces[fi].Vertices;

            if (verts.Count < 3)
            {
                normals.Add(Vector3.up);
                faceVertexIndices.Add([]);
                continue;
            }

            Vector3 n = NGonMath.NewellNormal(verts);
            float mag = n.magnitude;
            normals.Add(mag > 1e-10f ? n / mag : Vector3.up);

            var indices = new List<int>(verts.Count);

            foreach (Vector3 v in verts)
            {
                int found = -1;

                for (var i = 0; i < vertTable.Count; i++)
                {
                    if ((vertTable[i] - v).sqrMagnitude <= eps2)
                    {
                        found = i;
                        break;
                    }
                }

                if (found < 0)
                {
                    found = vertTable.Count;
                    vertTable.Add(v);
                }

                indices.Add(found);
            }

            faceVertexIndices.Add(indices);
        }

        // Build edge -> face map
        var edgeToFaces = new Dictionary<long, List<int>>();

        for (var fi = 0; fi < faces.Count; fi++)
        {
            List<int> idx = faceVertexIndices[fi];
            int vc = idx.Count;

            for (var i = 0; i < vc; i++)
            {
                int a = idx[i];
                int b = idx[(i + 1) % vc];
                long key = a < b ? ((long)a << 32) | (uint)b : ((long)b << 32) | (uint)a;

                if (!edgeToFaces.TryGetValue(key, out List<int>? list))
                {
                    list = new List<int>(2);
                    edgeToFaces[key] = list;
                }

                list.Add(fi);
            }
        }

        // Check angle between normals of adjacent faces
        var totalSharedEdges = 0;
        var smoothEdges = 0;
        float cosThreshold = Mathf.Cos(maxAngle);

        foreach (KeyValuePair<long, List<int>> kv in edgeToFaces)
        {
            List<int> adj = kv.Value;
            if (adj.Count < 2) continue;

            for (var i = 0; i < adj.Count; i++)
            {
                for (int j = i + 1; j < adj.Count; j++)
                {
                    totalSharedEdges++;
                    float dot = Vector3.Dot(normals[adj[i]], normals[adj[j]]);

                    if (dot >= cosThreshold)
                        smoothEdges++;
                }
            }
        }

        if (totalSharedEdges == 0)
            return false;

        float smoothFraction = (float)smoothEdges / totalSharedEdges;
        return smoothFraction >= minFraction;
    }
}