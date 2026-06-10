using TriangleScpSl.Core.Primitives.Parallelogram;
using UnityEngine;

namespace TriangleScpSl.Core.Decomposition.NGonDecomposition.Parallelogram;

/// <summary>
///     Parallel-sides peel: scan for 4 consecutive vertices forming a parallelogram,
///     emit it, and remove the 2 interior vertices. Rectangles are preferred (1 primitive vs 2).
/// </summary>
public static partial class HiddenTailParallelogramProcessor
{
    static bool TryParallelSidesPeel
    (
        List<Vector3> poly,
        Vector3 normal,
        Color color,
        List<ModelParallelogram> parallelograms
    )
    {
        int n = poly.Count;
        if (n < 5) return false;

        int bestStart = -1;
        int bestScore = -1;

        for (var i = 0; i < n; i++)
        {
            if (!IsParallelogramQuad(poly, i, n, out _, out _, out _, out bool isRect))
                continue;

            int score = isRect ? 1 : 0;

            if (score > bestScore)
            {
                bestScore = score;
                bestStart = i;
            }
        }

        if (bestStart < 0) return false;

        IsParallelogramQuad(poly, bestStart, n,
            out Vector3 center, out Vector3 vLeft, out Vector3 vUp, out _);

        parallelograms.Add(MakeParallelogram(center, vLeft, vUp, normal, color));

        // Remove interior vertices (descending order to keep indices stable)
        int rem1 = (bestStart + 1) % n;
        int rem2 = (bestStart + 2) % n;
        if (rem1 < rem2) (rem1, rem2) = (rem2, rem1);
        poly.RemoveAt(rem1);
        poly.RemoveAt(rem2);
        return true;
    }

    /// <summary>
    ///     Tests whether poly[start…start+3] (mod n) form a parallelogram.
    ///     Outputs center, half-diagonals, and whether the quad is a rectangle.
    /// </summary>
    static bool IsParallelogramQuad
    (
        List<Vector3> poly,
        int start,
        int n,
        out Vector3 center,
        out Vector3 vLeft,
        out Vector3 vUp,
        out bool isRect
    )
    {
        center = vLeft = vUp = Vector3.zero;
        isRect = false;

        Vector3 a = poly[start];
        Vector3 b = poly[(start + 1) % n];
        Vector3 c = poly[(start + 2) % n];
        Vector3 d = poly[(start + 3) % n];

        if (!AreParallelAndEqual(b - a, c - d)) return false;
        if (!AreParallelAndEqual(d - a, c - b)) return false;

        center = (a + c) * 0.5f;
        vLeft = (c - a) * 0.5f;
        vUp = (d - b) * 0.5f;

        float la = vLeft.magnitude, lu = vUp.magnitude;
        if (la < 1e-7f || lu < 1e-7f) return false;

        isRect = Mathf.Abs(la - lu) / Mathf.Max(la, lu) < LengthEps;
        return true;
    }
}