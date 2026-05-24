using Exiled.API.Features;
using UnityEngine;

namespace TriangleScpSl.Core.Decomposition.NGonDecomposition;

/// <summary>Removes near-duplicate n-gons by comparing geometry and vertices.</summary>
public static class NGonDeduplicator
{
    public static List<NGonRaw> Deduplicate
    (
        List<NGonRaw> faces,
        float vertexThreshold = 1e-4f,
        float planeDistThreshold = 1e-4f,
        float planeAngleThreshold = 0.01f,
        float colorEps = 1e-3f)
    {
        if (faces.Count <= 1)
            return faces;

        int n = faces.Count;

        // Precompute face properties for fast comparison
        var centroids = new Vector3[n];
        var normals = new Vector3[n];
        var areas = new float[n];
        var sortedDists = new float[n][];

        for (var i = 0; i < n; i++)
        {
            List<Vector3> verts = faces[i].Vertices;
            centroids[i] = Centroid(verts);
            Vector3 rawNormal = NGonMath.NewellNormal(verts);
            areas[i] = rawNormal.magnitude;
            normals[i] = areas[i] > 1e-10f ? rawNormal / areas[i] : Vector3.up;
            sortedDists[i] = SortedDistancesFromCentroid(verts, centroids[i]);
        }

        // Group faces by vertex count
        var byVertCount = new Dictionary<int, List<int>>();

        for (var i = 0; i < n; i++)
        {
            int vc = faces[i].Vertices.Count;

            if (!byVertCount.TryGetValue(vc, out List<int>? list))
            {
                list = [];
                byVertCount[vc] = list;
            }

            list.Add(i);
        }

        var isDuplicate = new bool[n];
        float vtSq = vertexThreshold * vertexThreshold;
        float pdSq = planeDistThreshold * planeDistThreshold;

        foreach (KeyValuePair<int, List<int>> kv in byVertCount)
        {
            List<int> group = kv.Value;

            for (var gi = 0; gi < group.Count; gi++)
            {
                int a = group[gi];
                if (isDuplicate[a]) continue;

                for (int gj = gi + 1; gj < group.Count; gj++)
                {
                    int b = group[gj];
                    if (isDuplicate[b]) continue;

                    if (!NGonMath.ColorsClose(faces[a].Color, faces[b].Color, colorEps))
                        continue;

                    // Fast early rejection based on centroid distance
                    if ((centroids[a] - centroids[b]).sqrMagnitude > pdSq * 100f + vtSq * 4f)
                        continue;

                    // Check normal alignment (same direction only - keep opposite-winding faces
                    // so double-sided geometry like plants remains visible from both sides)
                    float normalDot = Vector3.Dot(normals[a], normals[b]);

                    if (normalDot < 1f - planeAngleThreshold)
                        continue;

                    // Check plane distance
                    float planeDist = Mathf.Abs(Vector3.Dot(centroids[b] - centroids[a], normals[a]));

                    if (planeDist > planeDistThreshold)
                        continue;

                    // Quick area check
                    float areaA = areas[a], areaB = areas[b];
                    float maxArea = Mathf.Max(areaA, areaB);

                    if (maxArea > 1e-8f && Mathf.Abs(areaA - areaB) / maxArea > 0.1f)
                        continue;

                    // Distance fingerprint check before expensive vertex matching
                    if (!SortedDistsClose(sortedDists[a], sortedDists[b], vertexThreshold))
                        continue;

                    // Full vertex match with tolerance
                    if (VerticesMatch(faces[a].Vertices, faces[b].Vertices, vtSq))
                    {
                        isDuplicate[b] = true;
                    }
                }
            }
        }

        var removed = 0;
        var result = new List<NGonRaw>(n);

        for (var i = 0; i < n; i++)
        {
            if (isDuplicate[i])
                removed++;
            else
                result.Add(faces[i]);
        }

        if (removed > 0)
            Log.Info($"NGonDeduplicator: removed {removed} duplicate faces ({n} → {result.Count})");

        return result;
    }

    static bool VerticesMatch(List<Vector3> a, List<Vector3> b, float thresholdSq)
    {
        int n = a.Count;
        if (n != b.Count) return false;

        // Find 1-to-1 vertex matching with threshold
        var used = new bool[n];

        foreach (Vector3 va in a)
        {
            var found = false;

            for (var j = 0; j < n; j++)
            {
                if (used[j]) continue;

                if ((va - b[j]).sqrMagnitude <= thresholdSq)
                {
                    used[j] = true;
                    found = true;
                    break;
                }
            }

            if (!found) return false;
        }

        return true;
    }

    static float[] SortedDistancesFromCentroid(List<Vector3> verts, Vector3 centroid)
    {
        var dists = new float[verts.Count];

        for (var i = 0; i < verts.Count; i++)
        {
            dists[i] = (verts[i] - centroid).magnitude;
        }

        Array.Sort(dists);
        return dists;
    }

    static bool SortedDistsClose(float[] a, float[] b, float threshold)
    {
        if (a.Length != b.Length) return false;

        for (var i = 0; i < a.Length; i++)
        {
            if (Mathf.Abs(a[i] - b[i]) > threshold)
                return false;
        }

        return true;
    }

    static Vector3 Centroid(List<Vector3> verts)
    {
        Vector3 sum = Vector3.zero;
        foreach (Vector3 v in verts) sum += v;
        return sum / verts.Count;
    }
}