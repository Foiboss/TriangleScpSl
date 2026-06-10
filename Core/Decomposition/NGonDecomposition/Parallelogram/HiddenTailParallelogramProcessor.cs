using System.Collections;
using System.Diagnostics;
using Exiled.API.Features;
using TriangleScpSl.Core.Decomposition.NGonDecomposition.Geometry;
using TriangleScpSl.Core.Decomposition.NGonDecomposition.Merging;
using TriangleScpSl.Core.Primitives.Parallelogram;
using TriangleScpSl.Core.Primitives.Triangle;
using UnityEngine;

namespace TriangleScpSl.Core.Decomposition.NGonDecomposition.Parallelogram;

/// <summary>
///     Converts convex n-gons into ModelParallelogram instances using parallel-sides peeling,
///     bounding-rectangle covering, and hidden-tail optimization.
/// </summary>
public static partial class HiddenTailParallelogramProcessor
{
    const float SinEps = 0.005f;
    const float LengthEps = 1e-3f;

    public static List<ModelParallelogram> Process
    (
        IEnumerable<ConvexNGon> nGons,
        ModelSolidVolume? solid = null,
        bool useEdgeWalkSampling = true,
        float hiddenTailPullIn = 0.1f,
        bool allowNonPlanar = false
    )
    {
        List<ModelParallelogram> parallelograms = [];

        foreach (ConvexNGon ngon in nGons)
            ProcessOne(ngon, parallelograms, solid, useEdgeWalkSampling, hiddenTailPullIn, allowNonPlanar);
        return parallelograms;
    }

    /// <summary>
    ///     Coroutine version of Process that yields periodically to avoid freezing.
    /// </summary>
    public static IEnumerator ProcessCoroutine
    (
        List<ConvexNGon> nGons,
        ModelSolidVolume? solid,
        bool useEdgeWalkSampling,
        float hiddenTailPullIn,
        bool allowNonPlanar,
        float maxMsPerFrame,
        Action<List<ModelParallelogram>> onComplete
    )
    {
        List<ModelParallelogram> parallelograms = [];
        var sw = Stopwatch.StartNew();

        foreach (ConvexNGon nGon in nGons)
        {
            ProcessOne(nGon, parallelograms, solid, useEdgeWalkSampling, hiddenTailPullIn, allowNonPlanar);

            if (sw.Elapsed.TotalMilliseconds >= maxMsPerFrame)
            {
                yield return null;
                sw.Restart();
            }
        }

        onComplete(parallelograms);
    }

    static void ProcessOne
    (
        ConvexNGon nGon,
        List<ModelParallelogram> parallelograms,
        ModelSolidVolume? solid,
        bool useEdgeWalkSampling,
        float hiddenTailPullIn,
        bool allowNonPlanar
    )
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
            EmitTriangle(poly[0], poly[1], poly[2], normal, color, parallelograms, solid, useEdgeWalkSampling, hiddenTailPullIn);
            return;
        }

        if (!allowNonPlanar && !NGonMath.IsPlanar(poly, normal))
        {
            for (var i = 1; i < poly.Count - 1; i++)
            {
                EmitTriangle(poly[0], poly[i], poly[i + 1], normal, color, parallelograms, solid, useEdgeWalkSampling, hiddenTailPullIn);
            }

            return;
        }

        // Whole polygon is a rectangle / parallelogram?
        if (poly.Count == 4 && TryEmitWholeQuad(poly, normal, color, parallelograms))
            return;

        // Bounding rectangle covering (requires solid volume)
        if (solid != null && poly.Count >= 4
            && TryEmitBoundingRect(poly, normal, color, parallelograms, solid, useEdgeWalkSampling, hiddenTailPullIn))
            return;

        // Parallel-sides peel + standard peel loop
        while (poly.Count > 3)
        {
            if (TryParallelSidesPeel(poly, normal, color, parallelograms))
                continue;

            if (StandardConstructionPeel(poly, normal, color, parallelograms))
                continue;

            Log.Warn($"HiddenTailParallelogramProcessor: peel stalled at " +
                $"{poly.Count}-gon; fan-triangulating remainder.");

            for (var i = 1; i < poly.Count - 1; i++)
            {
                EmitTriangle(poly[0], poly[i], poly[i + 1], normal, color, parallelograms, solid, useEdgeWalkSampling, hiddenTailPullIn);
            }

            return;
        }

        EmitTriangle(poly[0], poly[1], poly[2], normal, color, parallelograms, solid, useEdgeWalkSampling, hiddenTailPullIn);
    }

    static bool TryEmitWholeQuad
    (
        List<Vector3> poly,
        Vector3 normal,
        Color color,
        List<ModelParallelogram> parallelograms
    )
    {
        Vector3 a = poly[0], b = poly[1], c = poly[2], d = poly[3];

        if (!AreParallelAndEqual(b - a, c - d)) return false;
        if (!AreParallelAndEqual(d - a, c - b)) return false;

        Vector3 center = (a + c) * 0.5f;
        Vector3 vLeft = (c - a) * 0.5f;
        Vector3 vUp = (d - b) * 0.5f;

        parallelograms.Add(MakeParallelogram(center, vLeft, vUp, normal, color));
        return true;
    }

    static bool StandardConstructionPeel
    (
        List<Vector3> poly,
        Vector3 normal,
        Color color,
        List<ModelParallelogram> parallelograms
    )
    {
        int n = poly.Count;
        int idx = FindParallelogramVertex(poly, normal);
        if (idx < 0) return false;

        Vector3 v = poly[idx];
        Vector3 a = poly[(idx - 1 + n) % n];
        Vector3 b = poly[(idx + 1) % n];

        Vector3 center = (a + b) * 0.5f;
        Vector3 vUp = v - center;
        Vector3 vLeft = a - center;

        parallelograms.Add(MakeParallelogram(center, vLeft, vUp, normal, color));

        poly.RemoveAt(idx);
        return true;
    }

    static void EmitTriangle
    (
        Vector3 p1, Vector3 p2, Vector3 p3,
        Vector3 normal, Color color,
        List<ModelParallelogram> parallelograms,
        ModelSolidVolume? solid,
        bool useEdgeWalkSampling,
        float hiddenTailPullIn
    )
    {
        if (solid != null
            && TryEmitHiddenTail(p1, p2, p3, normal, color, parallelograms, solid, useEdgeWalkSampling, hiddenTailPullIn))
            return;

        Vector3[][] triangleParallelograms =
            TriangleParallelogramBuilder.GetParallelogramsInfo(p1, p2, p3);

        for (var i = 0; i < 3; i++)
        {
            Vector3 vLeft = triangleParallelograms[i][0];
            Vector3 vUp = triangleParallelograms[i][1];
            Vector3 center = triangleParallelograms[i][2];
            parallelograms.Add(MakeParallelogram(center, vLeft, vUp, normal, color));
        }
    }

    static bool TryEmitHiddenTail
    (
        Vector3 p1, Vector3 p2, Vector3 p3,
        Vector3 normal, Color color,
        List<ModelParallelogram> parallelograms,
        ModelSolidVolume solid,
        bool useEdgeWalkSampling,
        float hiddenTailPullIn
    )
    {
        if (TryHideOne(p1, p2, p3, normal, color, parallelograms, solid, useEdgeWalkSampling, hiddenTailPullIn)) return true;
        if (TryHideOne(p2, p1, p3, normal, color, parallelograms, solid, useEdgeWalkSampling, hiddenTailPullIn)) return true;
        if (TryHideOne(p3, p1, p2, normal, color, parallelograms, solid, useEdgeWalkSampling, hiddenTailPullIn)) return true;
        return false;
    }

    static bool TryHideOne
    (
        Vector3 v, Vector3 a, Vector3 b,
        Vector3 normal, Color color,
        List<ModelParallelogram> parallelograms,
        ModelSolidVolume solid,
        bool useEdgeWalkSampling,
        float hiddenTailPullIn
    )
    {
        Vector3 p = a + b - v;
        if (!solid.IsTriangleFullyInsideSolid(a, b, p, normal, hiddenTailPullIn, useEdgeWalkSampling)) return false;

        Vector3 center = (a + b) * 0.5f;
        Vector3 vUp = v - center;
        Vector3 vLeft = a - center;

        parallelograms.Add(MakeParallelogram(center, vLeft, vUp, normal, color));
        return true;
    }
}