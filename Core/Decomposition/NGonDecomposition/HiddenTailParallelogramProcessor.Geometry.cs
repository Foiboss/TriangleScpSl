using TriangleScpSl.Core.Primitives.Parallelogram;
using UnityEngine;

namespace TriangleScpSl.Core.Decomposition.NGonDecomposition;

public static partial class HiddenTailParallelogramProcessor
{
    static ModelParallelogram MakeParallelogram
    (
        Vector3 center, Vector3 vLeft, Vector3 vUp, Vector3 normal, Color color)
    {
        if (Vector3.Dot(Vector3.Cross(vUp, vLeft), normal) < 0f)
            vUp = -vUp;

        // Rectangle = equal-length half diagonals (|VLeft| = |VUp|)
        float lengthLeft = vLeft.magnitude;
        float lengthUp = vUp.magnitude;

        bool isRect = lengthLeft > 1e-7f && lengthUp > 1e-7f
            && Mathf.Abs(lengthLeft - lengthUp) / Mathf.Max(lengthLeft, lengthUp) < LengthEps;

        return new ModelParallelogram
        {
            Center = center,
            VLeft = vLeft,
            VUp = vUp,
            Color = color,
            IsRectangle = isRect,
        };
    }

    static bool AreParallel(Vector3 a, Vector3 b)
    {
        float la = a.magnitude;
        float lb = b.magnitude;
        if (la < 1e-7f || lb < 1e-7f) return false;
        float sinAngle = Vector3.Cross(a, b).magnitude / (la * lb);
        return sinAngle < SinEps;
    }

    static bool AreParallelAndEqual(Vector3 a, Vector3 b)
    {
        if (!AreParallel(a, b)) return false;
        if (Vector3.Dot(a, b) <= 0f) return false;
        float la = a.magnitude, lb = b.magnitude;
        return Mathf.Abs(la - lb) / Mathf.Max(la, lb) < LengthEps;
    }

    static int FindParallelogramVertex(List<Vector3> poly, Vector3 normal, float eps = 1e-5f)
    {
        int n = poly.Count;
        BuildPlaneBasis(poly, normal, out Vector3 origin, out Vector3 e1, out Vector3 e2);
        List<Vector2> poly2D = ProjectPolygon(poly, origin, e1, e2);

        int idx = FindBestParallelogramVertex(poly, poly2D, n, origin, e1, e2, eps);
        if (idx >= 0) return idx;

        idx = FindBestParallelogramVertex(poly, poly2D, n, origin, e1, e2, 1e-3f);
        if (idx >= 0) return idx;

        return FindLeastViolatingParallelogramVertex(poly, poly2D, n, origin, e1, e2);
    }

    static int FindBestParallelogramVertex
    (
        List<Vector3> poly, List<Vector2> poly2D, int n,
        Vector3 origin, Vector3 e1, Vector3 e2, float eps)
    {
        for (var i = 0; i < n; i++)
        {
            Vector3 v = poly[i];
            Vector3 a = poly[(i - 1 + n) % n];
            Vector3 b = poly[(i + 1) % n];
            Vector3 p = a + b - v;
            Vector2 p2D = ProjectPoint(p, origin, e1, e2);
            if (IsInsideOrOnConvexCcw2D(p2D, poly2D, eps)) return i;
        }

        return -1;
    }

    static int FindLeastViolatingParallelogramVertex
    (
        List<Vector3> poly, List<Vector2> poly2D, int n,
        Vector3 origin, Vector3 e1, Vector3 e2)
    {
        var bestIdx = 0;
        float bestWorst = float.NegativeInfinity;

        for (var i = 0; i < n; i++)
        {
            Vector3 p = poly[(i - 1 + n) % n] + poly[(i + 1) % n] - poly[i];
            Vector2 p2D = ProjectPoint(p, origin, e1, e2);

            float worst = float.PositiveInfinity;

            for (var j = 0; j < n; j++)
            {
                float cross = Cross2D(poly2D[(j + 1) % n] - poly2D[j], p2D - poly2D[j]);
                if (cross < worst) worst = cross;
            }

            if (worst > bestWorst)
            {
                bestWorst = worst;
                bestIdx = i;
            }
        }

        return bestIdx;
    }

    static bool IsInsideOrOnConvexCcw2D(Vector2 p, List<Vector2> poly, float eps)
    {
        int n = poly.Count;

        for (var i = 0; i < n; i++)
        {
            if (Cross2D(poly[(i + 1) % n] - poly[i], p - poly[i]) < -eps) return false;
        }

        return true;
    }

    static void BuildPlaneBasis
    (
        List<Vector3> poly, Vector3 normal,
        out Vector3 origin, out Vector3 e1, out Vector3 e2)
    {
        origin = poly[0];
        e1 = poly[1] - origin;
        e1 -= Vector3.Dot(e1, normal) * normal;

        if (e1.sqrMagnitude < 1e-12f)
            e1 = Vector3.Cross(normal, Mathf.Abs(normal.y) < 0.9f ? Vector3.up : Vector3.right);
        e1 = e1.normalized;
        e2 = Vector3.Cross(normal, e1).normalized;
    }

    static List<Vector2> ProjectPolygon
    (
        List<Vector3> poly, Vector3 origin, Vector3 e1, Vector3 e2)
    {
        var projected = new List<Vector2>(poly.Count);

        foreach (Vector3 p in poly)
            projected.Add(ProjectPoint(p, origin, e1, e2));
        return projected;
    }

    static Vector2 ProjectPoint(Vector3 p, Vector3 origin, Vector3 e1, Vector3 e2)
    {
        Vector3 d = p - origin;
        return new Vector2(Vector3.Dot(d, e1), Vector3.Dot(d, e2));
    }

    static float Cross2D(Vector2 a, Vector2 b) => a.x * b.y - a.y * b.x;
}