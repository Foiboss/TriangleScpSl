using UnityEngine;

namespace TriangleScpSl.Core.NGons;

// Parallelogram from an n-gon: center and two half-diagonals.
// vLeft is to the left of vUp when viewed from the visible face (cross(vUp, vLeft) · normal > 0).
// Four vertices = center ± vUp, center ± vLeft.
// Color is inherited from the source FBX face.
public struct ParallelogramInfo
{
    public Vector3 Center;
    public Vector3 VLeft;
    public Vector3 VUp;
    public Color Color;
}

// Remaining triangle after splitting an n-gon into parallelograms.
// (V0, V1, V2) are CCW when viewed from the visible side (right-hand rule relative to normal).
// Color is inherited from the source FBX face.
public struct TriangleInfo
{
    public Vector3 V0;
    public Vector3 V1;
    public Vector3 V2;
    public Color Color;
}

// Processes an array of convex n-gons and returns:
//   - List<ParallelogramInfo>: all parallelograms (n-3 per n-gon).
//   - List<TriangleInfo>: one final triangle per n-gon.
// Algorithm per n-gon: while vertex count > 3, find vertex V with neighbors A and B such that
// the fourth parallelogram point P = A + B - V lies inside the current polygon (guaranteed to
// exist for n >= 4 in a convex polygon). Record ParallelogramInfo, remove V, repeat.
// The remaining 3 vertices become TriangleInfo; CCW order is preserved throughout.
public static class ParallelogramProcessor
{
    public static (List<ParallelogramInfo> parallelograms, List<TriangleInfo> triangles) Process
    (
        IEnumerable<ConvexNgon> ngons)
    {
        var paras = new List<ParallelogramInfo>();
        var tris = new List<TriangleInfo>();

        foreach (ConvexNgon ngon in ngons)
            ProcessOne(ngon, paras, tris);
        return (paras, tris);
    }

    static void ProcessOne(ConvexNgon ngon, List<ParallelogramInfo> paras, List<TriangleInfo> tris)
    {
        List<Vector3>? verts = ngon.Vertices;
        if (verts.Count < 3) return;

        Color color = ngon.Color;

        Vector3 normal = ngon.Normal.sqrMagnitude > 1e-12f
            ? ngon.Normal.normalized
            : NewellNormal(verts).normalized;

        var poly = new List<Vector3>(verts);

        // Ensure CCW winding relative to normal.
        if (Vector3.Dot(NewellNormal(poly), normal) < 0f)
            poly.Reverse();

        // Triangle fast-path.
        if (poly.Count == 3)
        {
            tris.Add(new TriangleInfo
            {
                V0 = poly[0], V1 = poly[1], V2 = poly[2], Color = color,
            });
            return;
        }

        // Greedily peel off one parallelogram per iteration.
        while (poly.Count > 3)
        {
            int n = poly.Count;
            int idx = FindParallelogramVertex(poly, normal);

            if (idx < 0)
            {
                Debug.LogError("ParallelogramProcessor: no suitable vertex found. " +
                    "Polygon is not convex or normal is pointing the wrong way.");
                return;
            }

            Vector3 v = poly[idx];
            Vector3 a = poly[(idx - 1 + n) % n];
            Vector3 b = poly[(idx + 1) % n];

            Vector3 center = (a + b) * 0.5f;
            Vector3 vUp = v - center; // half-diagonal toward the construction vertex
            Vector3 toA = a - center;

            // Pick vLeft from {toA, b-center} so that cross(vUp, vLeft) · normal > 0.
            Vector3 vLeft = Vector3.Dot(Vector3.Cross(vUp, toA), normal) > 0f
                ? toA
                : b - center;

            paras.Add(new ParallelogramInfo
            {
                Center = center,
                VLeft = vLeft,
                VUp = vUp,
                Color = color,
            });

            poly.RemoveAt(idx);
        }

        // Final triangle — the three remaining vertices in CCW order (preserved by removals).
        tris.Add(new TriangleInfo
        {
            V0 = poly[0], V1 = poly[1], V2 = poly[2], Color = color,
        });
    }

    // ============================================================ helpers

    static int FindParallelogramVertex(List<Vector3> poly, Vector3 normal, float eps = 1e-5f)
    {
        int n = poly.Count;

        for (var i = 0; i < n; i++)
        {
            Vector3 v = poly[i];
            Vector3 a = poly[(i - 1 + n) % n];
            Vector3 b = poly[(i + 1) % n];
            Vector3 p = a + b - v;

            if (IsInsideOrOnConvexCcw(p, poly, normal, eps))
                return i;
        }

        return -1;
    }

    static bool IsInsideOrOnConvexCcw(Vector3 p, List<Vector3> poly, Vector3 normal, float eps)
    {
        int n = poly.Count;

        for (var i = 0; i < n; i++)
        {
            Vector3 a = poly[i];
            Vector3 b = poly[(i + 1) % n];

            if (Vector3.Dot(Vector3.Cross(b - a, p - a), normal) < -eps)
                return false;
        }

        return true;
    }

    static Vector3 NewellNormal(List<Vector3> poly)
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
}
