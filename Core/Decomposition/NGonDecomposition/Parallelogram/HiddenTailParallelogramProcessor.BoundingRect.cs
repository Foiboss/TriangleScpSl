using TriangleScpSl.Core.Decomposition.NGonDecomposition.Geometry;
using TriangleScpSl.Core.Primitives.Parallelogram;
using UnityEngine;

namespace TriangleScpSl.Core.Decomposition.NGonDecomposition.Parallelogram;

// Bounding-rectangle covering: find the minimum-area oriented bounding rectangle
// for a convex nGon, verify excess regions are inside solid material, and emit
// the rectangle as a single parallelogram (1-2 primitives instead of O(n)).
public static partial class HiddenTailParallelogramProcessor
{
    static bool TryEmitBoundingRect
    (
        List<Vector3> poly,
        Vector3 normal,
        Color color,
        List<ModelParallelogram> parallelograms,
        ModelSolidVolume solid,
        bool useEdgeWalkSampling,
        float hiddenTailPullIn
    )
    {
        int n = poly.Count;
        if (n < 4) return false;

        BuildPlaneBasis(poly, normal, out Vector3 origin, out Vector3 e1, out Vector3 e2);
        List<Vector2> poly2D = ProjectPolygon(poly, origin, e1, e2);

        FindMinBoundingRect(poly2D, n,
            out Vector2 bestDir, out Vector2 bestPerp,
            out float bestMinU, out float bestMaxU,
            out float bestMinV, out float bestMaxV,
            out float bestArea);

        if (bestArea < 1e-10f) return false;

        Vector2[] rectCorners2D =
        [
            bestMinU * bestDir + bestMinV * bestPerp,
            bestMaxU * bestDir + bestMinV * bestPerp,
            bestMaxU * bestDir + bestMaxV * bestPerp,
            bestMinU * bestDir + bestMaxV * bestPerp,
        ];

        if (!VerifyExcessInsideSolid(rectCorners2D, poly2D, origin, e1, e2, normal, solid, useEdgeWalkSampling, hiddenTailPullIn))
            return false;

        // Map rectangle back to 3D as a parallelogram
        Vector2 rectCenter2D = (rectCorners2D[0] + rectCorners2D[2]) * 0.5f;
        Vector3 center = origin + rectCenter2D.x * e1 + rectCenter2D.y * e2;

        Vector3 vLeft = (rectCorners2D[2].x - rectCorners2D[0].x) * 0.5f * e1
            + (rectCorners2D[2].y - rectCorners2D[0].y) * 0.5f * e2;

        Vector3 vUp = (rectCorners2D[3].x - rectCorners2D[1].x) * 0.5f * e1
            + (rectCorners2D[3].y - rectCorners2D[1].y) * 0.5f * e2;

        parallelograms.Add(MakeParallelogram(center, vLeft, vUp, normal, color));
        return true;
    }

    /// <summary>
    ///     Rotating-calipers minimum-area bounding rectangle for a convex polygon in 2D.
    /// </summary>
    static void FindMinBoundingRect
    (
        List<Vector2> poly2D, int n,
        out Vector2 bestDir, out Vector2 bestPerp,
        out float bestMinU, out float bestMaxU,
        out float bestMinV, out float bestMaxV,
        out float bestArea
    )
    {
        bestArea = float.MaxValue;
        bestDir = Vector2.right;
        bestPerp = Vector2.up;
        bestMinU = bestMaxU = bestMinV = bestMaxV = 0;

        for (var i = 0; i < n; i++)
        {
            Vector2 edgeDir = poly2D[(i + 1) % n] - poly2D[i];
            float edgeLen = edgeDir.magnitude;
            if (edgeLen < 1e-7f) continue;
            edgeDir /= edgeLen;
            Vector2 perpDir = new(-edgeDir.y, edgeDir.x);

            float minU = float.MaxValue, maxU = float.MinValue;
            float minV = float.MaxValue, maxV = float.MinValue;

            for (var j = 0; j < n; j++)
            {
                float u = Vector2.Dot(poly2D[j], edgeDir);
                float v = Vector2.Dot(poly2D[j], perpDir);
                if (u < minU) minU = u;
                if (u > maxU) maxU = u;
                if (v < minV) minV = v;
                if (v > maxV) maxV = v;
            }

            float area = (maxU - minU) * (maxV - minV);

            if (area < bestArea)
            {
                bestArea = area;
                bestDir = edgeDir;
                bestPerp = perpDir;
                bestMinU = minU;
                bestMaxU = maxU;
                bestMinV = minV;
                bestMaxV = maxV;
            }
        }
    }

    /// <summary>
    ///     Checks that all points of the bounding rectangle outside the polygon
    ///     are inside solid material. Uses a dense grid with edge-walk sampling
    ///     between adjacent nodes to catch pits/holes between sample points.
    /// </summary>
    static bool VerifyExcessInsideSolid
    (
        Vector2[] rectCorners2D,
        List<Vector2> poly2D,
        Vector3 origin, Vector3 e1, Vector3 e2,
        Vector3 normal,
        ModelSolidVolume solid,
        bool useEdgeWalkSampling,
        float hiddenTailPullIn
    )
    {
        const int gridU = 6;
        const int gridV = 6;
        Vector3 pullDir = normal * hiddenTailPullIn;

        var grid = new Vector2[(gridU + 1) * (gridV + 1)];

        for (var iu = 0; iu <= gridU; iu++)
        {
            for (var iv = 0; iv <= gridV; iv++)
            {
                Vector2 sample = Vector2.Lerp(
                    Vector2.Lerp(rectCorners2D[0], rectCorners2D[1], iu / (float)gridU),
                    Vector2.Lerp(rectCorners2D[3], rectCorners2D[2], iu / (float)gridU),
                    iv / (float)gridV);
                grid[iu * (gridV + 1) + iv] = sample;

                if (!CheckSampleInsideSolid(sample, poly2D, origin, e1, e2, pullDir, solid))
                    return false;
            }
        }

        if (!useEdgeWalkSampling)
            return true;

        // Walk edges between adjacent grid nodes to catch gaps
        for (var iu = 0; iu <= gridU; iu++)
        {
            for (var iv = 0; iv <= gridV; iv++)
            {
                Vector2 from = grid[iu * (gridV + 1) + iv];

                if (iu + 1 <= gridU)
                {
                    Vector2 to = grid[(iu + 1) * (gridV + 1) + iv];

                    if (!WalkEdgeInsideSolid(from, to, poly2D, origin, e1, e2, pullDir, solid))
                        return false;
                }

                if (iv + 1 <= gridV)
                {
                    Vector2 to = grid[iu * (gridV + 1) + iv + 1];

                    if (!WalkEdgeInsideSolid(from, to, poly2D, origin, e1, e2, pullDir, solid))
                        return false;
                }
            }
        }

        return true;
    }

    static bool WalkEdgeInsideSolid
    (
        Vector2 from2D, Vector2 to2D,
        List<Vector2> poly2D,
        Vector3 origin, Vector3 e1, Vector3 e2,
        Vector3 pullDir,
        ModelSolidVolume solid
    )
    {
        Vector3 from3D = origin + from2D.x * e1 + from2D.y * e2;
        Vector3 to3D = origin + to2D.x * e1 + to2D.y * e2;
        float dist = (to3D - from3D).magnitude;
        int steps = Mathf.Max(2, Mathf.CeilToInt(dist / ModelSolidVolume.MaxEdgeSampleSpacing));

        for (var s = 1; s < steps; s++)
        {
            float t = s / (float)steps;
            Vector2 sample2D = Vector2.Lerp(from2D, to2D, t);

            if (!CheckSampleInsideSolid(sample2D, poly2D, origin, e1, e2, pullDir, solid))
                return false;
        }

        return true;
    }

    /// <summary>
    ///     If a 2D point is outside the polygon, verifies the corresponding 3D point
    ///     (offset both ways along the normal) is inside solid material.
    /// </summary>
    static bool CheckSampleInsideSolid
    (
        Vector2 sample2D,
        List<Vector2> poly2D,
        Vector3 origin, Vector3 e1, Vector3 e2,
        Vector3 pullDir,
        ModelSolidVolume solid
    )
    {
        if (IsInsideConvex2D(sample2D, poly2D))
            return true;

        Vector3 pt = origin + sample2D.x * e1 + sample2D.y * e2;
        if (!solid.IsSolid(pt + pullDir)) return false;
        if (!solid.IsSolid(pt - pullDir)) return false;
        return true;
    }

    static bool IsInsideConvex2D(Vector2 p, List<Vector2> poly)
    {
        int n = poly.Count;

        for (var i = 0; i < n; i++)
        {
            Vector2 a = poly[i];
            Vector2 b = poly[(i + 1) % n];
            if (Cross2D(b - a, p - a) < -1e-6f) return false;
        }

        return true;
    }
}