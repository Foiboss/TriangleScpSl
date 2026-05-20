using System.Collections;
using AdminToys;
using Exiled.API.Features;
using TriangleScpSl.Core.Triangulation.Parallelogram;
using TriangleScpSl.Core.Triangulation.Triangle;
using UnityEngine;

namespace TriangleScpSl.Core.Models.HierarchicalModel;

public partial class HierarchicalModel
{
    void BuildTriangles(PrimitiveFlags flags)
    {
        if (IsDestroyedValue) return;

        FlagsValue = flags;

        ClearAllPrimitives();
        DestroyNativePrimitives();
        _stretches.Clear();
        _parallelograms.Clear();
        _fallbackParallelograms.Clear();
        _parallelogramSnapshots.Clear();
        _quadBuildInfos.Clear();
        _hierarchicalParents.Clear();
        _hierarchyDepths.Clear();
        _usedStretches.Clear();
        _hierarchicallyParentedCount = 0;

        // Phase 1: Build all parallelograms with inline hierarchical parenting.
        foreach (ModelTriangle localTriangle in _localTriangles)
            CreateTriangle(localTriangle, flags);

        foreach (ModelParallelogram p in _localParallelograms)
        {
            Vector3 vLeft = TransformVector(p.VLeft);
            Vector3 vUp = TransformVector(InvertWinding ? -p.VUp : p.VUp);

            if (p.IsRectangle) CreateRectangle(vLeft, vUp, TransformPoint(p.Center), flags, p.Color);
            else CreateParallelogram(vLeft, vUp, TransformPoint(p.Center), flags, p.Color);
        }

        // Phase 2: Iterative optimization sweeps.
        RunOptimizationSweeps();

        MarkUsedStretches();
        DestroyUnusedStretches();
        BuildNativePrimitives(flags);

        Log.Debug($"[HierarchicalModel] Built: {_parallelograms.Count} quads, " +
            $"{_hierarchicallyParentedCount} hierarchically parented, " +
            $"{_usedStretches.Count} stretches used / {_stretches.Count} total " +
            $"(saved {StretchesSaved}).");
    }

    public override IEnumerator BuildTrianglesCoroutine(PrimitiveFlags flags, int trianglesPerFrame)
    {
        if (IsDestroyedValue) yield break;

        trianglesPerFrame = Mathf.Max(1, trianglesPerFrame);
        FlagsValue = flags;

        ClearAllPrimitives();
        DestroyNativePrimitives();
        _stretches.Clear();
        _parallelograms.Clear();
        _fallbackParallelograms.Clear();
        _parallelogramSnapshots.Clear();
        _quadBuildInfos.Clear();
        _hierarchicalParents.Clear();
        _hierarchyDepths.Clear();
        _usedStretches.Clear();
        _hierarchicallyParentedCount = 0;

        var processed = 0;

        foreach (ModelTriangle localTriangle in _localTriangles)
        {
            if (IsDestroyedValue) yield break;
            CreateTriangle(localTriangle, flags);
            processed++;

            if (processed >= trianglesPerFrame)
            {
                processed = 0;
                yield return null;
            }
        }

        foreach (ModelParallelogram p in _localParallelograms)
        {
            if (IsDestroyedValue) yield break;
            Vector3 vLeft = TransformVector(p.VLeft);
            Vector3 vUp = TransformVector(InvertWinding ? -p.VUp : p.VUp);

            if (p.IsRectangle) CreateRectangle(vLeft, vUp, TransformPoint(p.Center), flags, p.Color);
            else CreateParallelogram(vLeft, vUp, TransformPoint(p.Center), flags, p.Color);
            processed++;

            if (processed >= trianglesPerFrame)
            {
                processed = 0;
                yield return null;
            }
        }

        if (IsDestroyedValue) yield break;
        RunOptimizationSweeps();
        yield return null;

        MarkUsedStretches();
        DestroyUnusedStretches();
        yield return null;

        BuildNativePrimitives(flags);
    }
}