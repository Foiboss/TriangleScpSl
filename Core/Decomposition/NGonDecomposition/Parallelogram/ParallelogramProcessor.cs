using Exiled.API.Features;
using TriangleScpSl.Core.Decomposition.NGonDecomposition.Geometry;
using TriangleScpSl.Core.Decomposition.NGonDecomposition.Merging;
using TriangleScpSl.Core.Primitives.Parallelogram;
using TriangleScpSl.Core.Primitives.Triangle;
using UnityEngine;

namespace TriangleScpSl.Core.Decomposition.NGonDecomposition.Parallelogram;

/// <summary>Decomposes convex n-gons into parallelograms and triangles.</summary>
public static class ParallelogramProcessor
{
    public static List<ModelParallelogram> Process
    (
        IEnumerable<ConvexNGon> nGons)
    {
        List<ModelParallelogram> parallelograms = [];

        foreach (ConvexNGon ngon in nGons)
            ProcessOne(ngon, parallelograms);
        return parallelograms;
    }

    static void ProcessOne(ConvexNGon nGon, List<ModelParallelogram> parallelograms)
    {
        List<Vector3> verts = nGon.Vertices;
        if (verts.Count < 3) return;

        Color color = nGon.Color;

        Vector3 normal = nGon.Normal.sqrMagnitude > 1e-12f
            ? nGon.Normal.normalized
            : NGonMath.NewellNormal(verts).normalized;

        var poly = new List<Vector3>(verts);

        if (Vector3.Dot(NGonMath.NewellNormal(poly), normal) < 0f)
            poly.Reverse();

        if (poly.Count == 3)
        {
            AddTriangle(poly[0], poly[1], poly[2], color, parallelograms);
            return;
        }

        if (!NGonMath.IsPlanar(poly, normal))
        {
            // Non-planar polygon: fall back to simple triangulation
            for (var i = 1; i < poly.Count - 1; i++)
            {
                AddTriangle(poly[0], poly[i], poly[i + 1], color, parallelograms);
            }

            return;
        }

        while (poly.Count > 3)
        {
            int n = poly.Count;
            int idx = FindParallelogramVertex(poly, normal);

            if (idx < 0)
            {
                Log.Error("ParallelogramProcessor: no suitable vertex found.");
                return;
            }

            // Extract parallelogram from polygon
            Vector3 v = poly[idx];
            Vector3 a = poly[(idx - 1 + n) % n];
            Vector3 b = poly[(idx + 1) % n];

            Vector3 center = (a + b) * 0.5f;
            Vector3 vUp = v - center;
            Vector3 toA = a - center;

            Vector3 vLeft = Vector3.Dot(Vector3.Cross(vUp, toA), normal) > 0f
                ? toA
                : b - center;

            parallelograms.Add(new ModelParallelogram
            {
                Center = center,
                VLeft = vLeft,
                VUp = vUp,
                Color = color,
            });

            poly.RemoveAt(idx);
        }

        AddTriangle(poly[0], poly[1], poly[2], color, parallelograms);
    }

    static void AddTriangle(Vector3 p1, Vector3 p2, Vector3 p3, Color color, List<ModelParallelogram> parallelograms)
    {
        Vector3[][] triangleParallelograms = TriangleParallelogramBuilder.GetParallelogramsInfo(p1, p2, p3);

        for (var i = 0; i < 3; i++)
        {
            parallelograms.Add(
                new ModelParallelogram
                {
                    VLeft = triangleParallelograms[i][0],
                    VUp = triangleParallelograms[i][1],
                    Center = triangleParallelograms[i][2],
                    Color = color,
                });
        }
    }

    static int FindParallelogramVertex(List<Vector3> poly, Vector3 normal, float eps = 1e-5f)
    {
        int n = poly.Count;
        BuildPlaneBasis(poly, normal, out Vector3 origin, out Vector3 e1, out Vector3 e2);
        List<Vector2> poly2D = ProjectPolygon(poly, origin, e1, e2);

        // Try each vertex: check if P = A + B - V is inside the polygon
        for (var i = 0; i < n; i++)
        {
            Vector3 v = poly[i];
            Vector3 a = poly[(i - 1 + n) % n];
            Vector3 b = poly[(i + 1) % n];
            Vector3 p = a + b - v;

            Vector2 p2D = ProjectPoint(p, origin, e1, e2);

            if (IsInsideOrOnConvexCcw2D(p2D, poly2D, eps))
                return i;
        }

        return -1;
    }

    static bool IsInsideOrOnConvexCcw2D(Vector2 p, List<Vector2> poly, float eps)
    {
        int n = poly.Count;

        for (var i = 0; i < n; i++)
        {
            Vector2 a = poly[i];
            Vector2 b = poly[(i + 1) % n];

            if (Cross2D(b - a, p - a) < -eps)
                return false;
        }

        return true;
    }

    static void BuildPlaneBasis(List<Vector3> poly, Vector3 normal, out Vector3 origin, out Vector3 e1, out Vector3 e2)
    {
        origin = poly[0];
        e1 = poly[1] - origin;
        e1 -= Vector3.Dot(e1, normal) * normal;

        if (e1.sqrMagnitude < 1e-12f)
            e1 = Vector3.Cross(normal, Mathf.Abs(normal.y) < 0.9f ? Vector3.up : Vector3.right);

        e1 = e1.normalized;
        e2 = Vector3.Cross(normal, e1).normalized;
    }

    static List<Vector2> ProjectPolygon(List<Vector3> poly, Vector3 origin, Vector3 e1, Vector3 e2)
    {
        var projected = new List<Vector2>(poly.Count);

        foreach (Vector3 polygon in poly)
            projected.Add(ProjectPoint(polygon, origin, e1, e2));

        return projected;
    }

    static Vector2 ProjectPoint(Vector3 p, Vector3 origin, Vector3 e1, Vector3 e2)
    {
        Vector3 d = p - origin;
        return new Vector2(Vector3.Dot(d, e1), Vector3.Dot(d, e2));
    }

    static float Cross2D(Vector2 a, Vector2 b) => a.x * b.y - a.y * b.x;
}