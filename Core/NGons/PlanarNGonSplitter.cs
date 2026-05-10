using UnityEngine;

namespace TriangleScpSl.Core.NGons;

public static partial class PlanarNGonSplitter
{
    const float VertexMergeEps = 1e-5f;
    const float NormalEps = 0.13f;

    public static List<NGonRaw> SplitAll(IEnumerable<NGonRaw> sources, float planarThreshold = 0f)
    {
        List<NGonRaw> faces = sources as List<NGonRaw> ?? [..sources];

        faces = MergeExactCoplanar(faces);

        if (planarThreshold > 0f)
            faces = MergeCoplanar(faces, planarThreshold);

        return faces;
    }

    static int Intern(List<Vector3> table, Vector3 v)
    {
        const float eps2 = VertexMergeEps * VertexMergeEps;

        for (var i = 0; i < table.Count; i++)
        {
            if ((table[i] - v).sqrMagnitude <= eps2)
                return i;
        }

        table.Add(v);
        return table.Count - 1;
    }

    static long DirectedKey(int a, int b)
        => ((long)a << 32) | (uint)b;

    static bool TryExtractBoundary
    (
        List<int> facesInCluster,
        int[][] faceIdx,
        Vector3[] snapped,
        out List<int>? loop)
    {
        loop = null;

        var directed = new HashSet<long>();
        var allEdges = new List<(int a, int b)>();

        foreach (int fi in facesInCluster)
        {
            int[] idx = faceIdx[fi];
            int n = idx.Length;

            for (var i = 0; i < n; i++)
            {
                int a = idx[i];
                int b = idx[(i + 1) % n];
                long k = DirectedKey(a, b);

                if (!directed.Add(k))
                    return false;
                allEdges.Add((a, b));
            }
        }

        var nextOnBoundary = new Dictionary<int, int>();
        var boundaryEdgeCount = 0;

        foreach (var (a, b) in allEdges)
        {
            if (directed.Contains(DirectedKey(b, a))) continue;

            if (nextOnBoundary.ContainsKey(a))
                return false;
            nextOnBoundary[a] = b;
            boundaryEdgeCount++;
        }

        if (boundaryEdgeCount < 3) return false;

        int start = -1;

        foreach (int k in nextOnBoundary.Keys)
        {
            start = k;
            break;
        }

        if (start < 0) return false;

        var result = new List<int>(boundaryEdgeCount);
        int cur = start;

        for (var step = 0; step < boundaryEdgeCount; step++)
        {
            result.Add(cur);
            if (!nextOnBoundary.TryGetValue(cur, out int nxt)) return false;
            cur = nxt;

            if (cur == start)
            {
                if (result.Count != boundaryEdgeCount) return false;
                loop = SimplifyCollinear(result, snapped);
                return loop is { Count: >= 3 };
            }
        }

        return false;
    }

    static List<int> SimplifyCollinear(List<int> loop, Vector3[] pos)
    {
        int n = loop.Count;
        if (n < 4) return loop;

        const float sinEps = 0.01f;
        var keep = new bool[n];

        for (var i = 0; i < n; i++)
        {
            keep[i] = true;
        }

        var changed = true;

        while (changed)
        {
            changed = false;

            for (var i = 0; i < n; i++)
            {
                if (!keep[i]) continue;

                int prev = PrevKept(keep, i, n);
                int next = NextKept(keep, i, n);
                if (prev == i || next == i || prev == next) break;

                Vector3 a = pos[loop[prev]];
                Vector3 b = pos[loop[i]];
                Vector3 c = pos[loop[next]];

                Vector3 ab = b - a;
                Vector3 bc = c - b;
                float lenab = ab.magnitude;
                float lenbc = bc.magnitude;

                if (lenab < 1e-7f || lenbc < 1e-7f)
                {
                    keep[i] = false;
                    changed = true;
                    continue;
                }

                float sinAngle = Vector3.Cross(ab, bc).magnitude / (lenab * lenbc);

                if (sinAngle < sinEps)
                {
                    keep[i] = false;
                    changed = true;
                }
            }
        }

        var result = new List<int>(n);

        for (var i = 0; i < n; i++)
        {
            if (keep[i]) result.Add(loop[i]);
        }

        return result;
    }

    static int PrevKept(bool[] keep, int i, int n)
    {
        for (var s = 1; s <= n; s++)
        {
            int k = (i - s + n) % n;
            if (keep[k]) return k;
        }

        return i;
    }

    static int NextKept(bool[] keep, int i, int n)
    {
        for (var s = 1; s <= n; s++)
        {
            int k = (i + s) % n;
            if (keep[k]) return k;
        }

        return i;
    }
}