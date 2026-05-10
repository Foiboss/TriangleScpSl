using UnityEngine;

namespace TriangleScpSl.Core.NGons;

/// <summary>A convex n-gon with vertices in CCW order, color, and plane normal.</summary>
public struct ConvexNGon
{
    public List<Vector3> Vertices;
    public Color Color;
    public Vector3 Normal;
}

/// <summary>Splits concave n-gons into convex pieces via triangulation and merging.</summary>
public static class ConvexNGonDecomposer
{
    public static List<ConvexNGon> Decompose(IEnumerable<NGonRaw> nGons)
    {
        var result = new List<ConvexNGon>();

        foreach (NGonRaw ngon in nGons)
            DecomposeOne(ngon, result);
        return result;
    }

    public static void DecomposeOne(NGonRaw nGon, List<ConvexNGon> output)
    {
        List<Vector3> verts = nGon.Vertices;
        if (verts.Count < 3) return;

        Vector3 normal = NGonMath.NewellNormal(verts);
        if (normal.sqrMagnitude < 1e-12f) return; // degenerate
        normal = normal.normalized;

        // Build orthonormal basis for 2D projection
        Vector3 origin = verts[0];
        Vector3 e1 = verts[1] - origin;
        e1 -= Vector3.Dot(e1, normal) * normal;

        if (e1.sqrMagnitude < 1e-12f)
            e1 = Vector3.Cross(normal, Mathf.Abs(normal.y) < 0.9f ? Vector3.up : Vector3.right);
        e1 = e1.normalized;
        Vector3 e2 = Vector3.Cross(normal, e1).normalized;

        var p2D = new List<Vector2>(verts.Count);
        var indexMap = new List<int>(verts.Count);

        for (var i = 0; i < verts.Count; i++)
        {
            Vector3 d = verts[i] - origin;
            p2D.Add(new Vector2(Vector3.Dot(d, e1), Vector3.Dot(d, e2)));
            indexMap.Add(i);
        }

        // Ensure CCW winding in 2D
        if (SignedArea2D(p2D) < 0f)
        {
            p2D.Reverse();
            indexMap.Reverse();
        }

        List<List<int>> pieces = ConvexDecompose2D(p2D);

        foreach (List<int> piece in pieces)
        {
            var piece3D = new List<Vector3>(piece.Count);

            foreach (int localIdx in piece)
                piece3D.Add(verts[indexMap[localIdx]]);

            output.Add(new ConvexNGon
            {
                Vertices = piece3D,
                Color = nGon.Color,
                Normal = normal,
            });
        }
    }

    static List<List<int>> ConvexDecompose2D(List<Vector2> polygon)
    {
        if (IsConvex2D(polygon))
        {
            var all = new List<int>(polygon.Count);

            for (var i = 0; i < polygon.Count; i++)
            {
                all.Add(i);
            }

            return [all];
        }

        List<int[]> triangles = EarClipTriangulate(polygon);
        if (triangles.Count == 0) return [];

        var pieces = new List<List<int>>(triangles.Count);

        pieces.AddRange(triangles.Select(t => (List<int>)
        [
            t[0],
            t[1],
            t[2],
        ]));

        // Greedily merge adjacent convex pieces into larger convex polygons
        var changed = true;
        int safety = pieces.Count * pieces.Count + 16;

        while (changed && safety-- > 0)
        {
            changed = false;

            for (var i = 0; i < pieces.Count && !changed; i++)
            {
                for (int j = i + 1; j < pieces.Count; j++)
                {
                    if (TryMergePieces(pieces[i], pieces[j], polygon, out List<int>? merged))
                    {
                        if (merged != null) pieces[i] = merged;
                        pieces.RemoveAt(j);
                        changed = true;
                        break;
                    }
                }
            }
        }

        return pieces;
    }

    // Triangulate CCW polygon using ear clipping
    static List<int[]> EarClipTriangulate(List<Vector2> polygon)
    {
        var triangles = new List<int[]>();
        int n = polygon.Count;
        if (n < 3) return triangles;

        var indices = new List<int>(n);

        for (var i = 0; i < n; i++)
        {
            indices.Add(i);
        }

        int safety = n * n + 16;

        while (indices.Count > 3 && safety-- > 0)
        {
            var found = false;

            for (var i = 0; i < indices.Count; i++)
            {
                int prev = indices[(i - 1 + indices.Count) % indices.Count];
                int cur = indices[i];
                int next = indices[(i + 1) % indices.Count];

                if (IsEar(polygon, indices, prev, cur, next))
                {
                    triangles.Add([prev, cur, next]);
                    indices.RemoveAt(i);
                    found = true;
                    break;
                }
            }

            if (!found) break;
        }

        if (indices.Count == 3)
            triangles.Add([indices[0], indices[1], indices[2]]);
        return triangles;
    }

    // Check if vertex cur forms an ear (convex + no internal vertices)
    static bool IsEar(List<Vector2> polygon, List<int> indices, int prev, int cur, int next)
    {
        Vector2 a = polygon[prev], b = polygon[cur], c = polygon[next];
        if (Cross2D(b - a, c - b) <= 1e-7f) return false;

        foreach (int idx in indices)
        {
            if (idx == prev || idx == cur || idx == next) continue;

            if (PointInTriangle(polygon[idx], a, b, c))
                return false;
        }

        return true;
    }

    // Try merging two convex pieces along their shared edge
    static bool TryMergePieces(List<int> p1, List<int> p2, List<Vector2> polygon, out List<int>? merged)
    {
        merged = null;

        for (var i = 0; i < p1.Count; i++)
        {
            int a = p1[i];
            int b = p1[(i + 1) % p1.Count];

            for (var j = 0; j < p2.Count; j++)
            {
                if (p2[j] == b && p2[(j + 1) % p2.Count] == a)
                {
                    List<int> candidate = MergeAlongSharedEdge(p1, i, p2, j);

                    if (candidate.Count >= 3 && IsConvexIndexed(candidate, polygon))
                    {
                        merged = candidate;
                        return true;
                    }

                    return false; // edge found but merged polygon is not convex
                }
            }
        }

        return false;
    }

    // Merge two pieces along their shared edge
    static List<int> MergeAlongSharedEdge(List<int> p1, int i1, List<int> p2, int j2)
    {
        var merged = new List<int>();
        int n1 = p1.Count, n2 = p2.Count;

        merged.Add(p1[i1]);
        int cur = (j2 + 2) % n2;

        while (true)
        {
            merged.Add(p2[cur]);
            if (cur == j2) break;
            cur = (cur + 1) % n2;
        }

        int p1End = (i1 - 1 + n1) % n1;
        cur = (i1 + 2) % n1;

        if (cur != i1)
        {
            while (true)
            {
                if (cur == (i1 + 1) % n1) break;
                merged.Add(p1[cur]);
                if (cur == p1End) break;
                cur = (cur + 1) % n1;
            }
        }

        return merged;
    }

    static bool IsConvexIndexed(List<int> indexed, List<Vector2> polygon)
    {
        var pts = new List<Vector2>(indexed.Count);
        pts.AddRange(indexed.Select(i => polygon[i]));
        return IsConvex2D(pts);
    }

    static bool IsConvex2D(List<Vector2> poly)
    {
        int n = poly.Count;
        if (n < 3) return false;
        bool gotPos = false, gotNeg = false;

        for (var i = 0; i < n; i++)
        {
            Vector2 a = poly[i];
            Vector2 b = poly[(i + 1) % n];
            Vector2 c = poly[(i + 2) % n];
            float cross = Cross2D(b - a, c - b);

            switch (cross)
            {
                case > 1e-7f:
                    gotPos = true;
                    break;
                case < -1e-7f:
                    gotNeg = true;
                    break;
            }

            if (gotPos && gotNeg) return false;
        }

        return true;
    }

    static bool PointInTriangle(Vector2 p, Vector2 a, Vector2 b, Vector2 c)
    {
        float d1 = Cross2D(b - a, p - a);
        float d2 = Cross2D(c - b, p - b);
        float d3 = Cross2D(a - c, p - c);
        bool hasNeg = d1 < -1e-7f || d2 < -1e-7f || d3 < -1e-7f;
        bool hasPos = d1 > 1e-7f || d2 > 1e-7f || d3 > 1e-7f;
        return !(hasNeg && hasPos);
    }

    static float SignedArea2D(List<Vector2> p)
    {
        var a = 0f;
        int n = p.Count;

        for (var i = 0; i < n; i++)
        {
            Vector2 cur = p[i];
            Vector2 nxt = p[(i + 1) % n];
            a += cur.x * nxt.y - nxt.x * cur.y;
        }

        return a * 0.5f;
    }

    static float Cross2D(Vector2 a, Vector2 b) => a.x * b.y - a.y * b.x;
}