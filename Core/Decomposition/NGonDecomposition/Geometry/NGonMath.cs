using UnityEngine;

namespace TriangleScpSl.Core.Decomposition.NGonDecomposition.Geometry;

/// <summary>
///     Shared geometric utilities for NGon processing.
/// </summary>
public static class NGonMath
{
    /// <summary>
    ///     Computes the Newell normal of a polygon (robust for near-planar faces).
    ///     The result is NOT normalized; its magnitude is proportional to twice the polygon area.
    /// </summary>
    public static Vector3 NewellNormal(List<Vector3> poly)
    {
        Vector3 n = Vector3.zero;
        int count = poly.Count;

        for (var i = 0; i < count; i++)
        {
            Vector3 c = poly[i];
            Vector3 nx = poly[(i + 1) % count];
            n.x += (c.y - nx.y) * (c.z + nx.z);
            n.y += (c.z - nx.z) * (c.x + nx.x);
            n.z += (c.x - nx.x) * (c.y + nx.y);
        }

        return n;
    }

    /// <summary>
    ///     Computes the Newell normal for a polygon defined by indices into a shared vertex table.
    /// </summary>
    public static Vector3 NewellNormalIndexed(List<Vector3> table, int[] indices)
    {
        Vector3 n = Vector3.zero;
        int count = indices.Length;

        for (var i = 0; i < count; i++)
        {
            Vector3 c = table[indices[i]];
            Vector3 nx = table[indices[(i + 1) % count]];
            n.x += (c.y - nx.y) * (c.z + nx.z);
            n.y += (c.z - nx.z) * (c.x + nx.x);
            n.z += (c.x - nx.x) * (c.y + nx.y);
        }

        return n;
    }

    /// <summary>
    ///     Union-Find: find root with path halving.
    /// </summary>
    public static int Find(int[] parent, int x)
    {
        while (parent[x] != x)
        {
            parent[x] = parent[parent[x]];
            x = parent[x];
        }

        return x;
    }

    /// <summary>
    ///     Union-Find: union by size.
    /// </summary>
    public static void Union(int[] parent, int[] size, int a, int b)
    {
        int ra = Find(parent, a);
        int rb = Find(parent, b);
        if (ra == rb) return;
        if (size[ra] < size[rb]) (ra, rb) = (rb, ra);
        parent[rb] = ra;
        size[ra] += size[rb];
    }

    /// <summary>
    ///     Undirected edge key for adjacency maps. Order-independent.
    /// </summary>
    public static long EdgeKey(int a, int b)
    {
        if (a > b) (a, b) = (b, a);
        return ((long)a << 32) | (uint)b;
    }

    /// <summary>
    ///     Checks whether two colors are approximately equal (per-channel).
    /// </summary>
    public static bool ColorsClose(Color a, Color b, float eps = 1e-3f)
        => Mathf.Abs(a.r - b.r) < eps
            && Mathf.Abs(a.g - b.g) < eps
            && Mathf.Abs(a.b - b.b) < eps
            && Mathf.Abs(a.a - b.a) < eps;

    /// <summary>
    ///     Checks whether all polygon vertices lie within a small tolerance of the plane
    ///     defined by the first vertex and the given normal.
    /// </summary>
    public static bool IsPlanar(List<Vector3> poly, Vector3 normal)
    {
        Vector3 origin = poly[0];
        var maxDeviation = 0f;
        var avgEdgeLength = 0f;

        for (var i = 0; i < poly.Count; i++)
        {
            Vector3 a = poly[i];
            Vector3 b = poly[(i + 1) % poly.Count];
            avgEdgeLength += (b - a).magnitude;
            float deviation = Mathf.Abs(Vector3.Dot(a - origin, normal));
            if (deviation > maxDeviation) maxDeviation = deviation;
        }

        avgEdgeLength = poly.Count > 0 ? avgEdgeLength / poly.Count : 0f;
        float tolerance = avgEdgeLength * 1e-4f + 1e-6f;
        return maxDeviation <= tolerance;
    }
}