using System.Collections;
using AdminToys;
using Exiled.API.Features.Toys;
using TriangleScpSl.Core.Primitives.Parallelogram;
using TriangleScpSl.Core.Primitives.Triangle;
using UnityEngine;

namespace TriangleScpSl.Core.Models.ApproximateModel;

public partial class ApproximateModel
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
        _quadInfos.Clear();

        foreach (ModelTriangle localTriangle in _localTriangles)
            CreateTriangle(localTriangle, flags);

        foreach (ModelParallelogram p in _localParallelograms)
        {
            Vector3 vUp = InvertWinding ? -p.VUp : p.VUp;
            Vector3 vLeft = p.VLeft;

            if (p.IsRectangle)
                CreateRectangle(vLeft, vUp, TransformPoint(p.Center), flags, p.Color);
            else
                CreateParallelogram(vLeft, vUp, TransformPoint(p.Center), flags, p.Color);
        }

        ConsolidateStretches();
        BuildNativePrimitives(flags);
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
        _quadInfos.Clear();

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

            Vector3 vUp = InvertWinding ? -p.VUp : p.VUp;
            Vector3 vLeft = p.VLeft;

            if (p.IsRectangle)
                CreateRectangle(vLeft, vUp, TransformPoint(p.Center), flags, p.Color);
            else
                CreateParallelogram(vLeft, vUp, TransformPoint(p.Center), flags, p.Color);

            processed++;

            if (processed >= trianglesPerFrame)
            {
                processed = 0;
                yield return null;
            }
        }

        if (IsDestroyedValue) yield break;
        ConsolidateStretches();
        yield return null;

        BuildNativePrimitives(flags);
    }

    void CreateTriangle(ModelTriangle localTriangle, PrimitiveFlags flags)
    {
        Vector3 p1 = TransformPoint(localTriangle.P1);
        Vector3 p2 = TransformPoint(localTriangle.P2);
        Vector3 p3 = TransformPoint(localTriangle.P3);

        if (InvertWinding)
            (p2, p3) = (p3, p2);

        Vector3[][] data = TriangleParallelogramBuilder.GetParallelogramsInfo(p1, p2, p3);
        CreateParallelogram(data[0][0], data[0][1], data[0][2], flags, localTriangle.Color);
        CreateParallelogram(data[1][0], data[1][1], data[1][2], flags, localTriangle.Color);
        CreateParallelogram(data[2][0], data[2][1], data[2][2], flags, localTriangle.Color);
    }

    void CreateRectangle(Vector3 vLeft, Vector3 vUp, Vector3 center, PrimitiveFlags flags, Color color)
    {
        // VLeft/VUp are half-diagonals. Edges are (VLeft+VUp) and (VLeft-VUp).
        Vector3 edgeA = vLeft + vUp;
        Vector3 edgeB = vLeft - vUp;
        float width = edgeB.magnitude;
        float height = edgeA.magnitude;
        Vector3 forward = Vector3.Cross(edgeB, edgeA).normalized;

        if (forward.sqrMagnitude < 1e-6f || width < 1e-7f || height < 1e-7f)
        {
            CreateParallelogram(vLeft, vUp, center, flags, color);
            return;
        }

        Quaternion rotation = Quaternion.LookRotation(forward, edgeA.normalized);

        var quad = Primitive.Create(PrimitiveType.Quad, flags, center, rotation.eulerAngles,
            new Vector3(width, height, 1f), true, color);
        quad.Transform.SetParent(BaseQuad.Transform);
        _parallelograms.Add(quad);
        _quadInfos.Add((vLeft, vUp, null));
        _parallelogramSnapshots.Add(new ParallelogramSnapshot(vUp, vLeft, center, color, flags, false));
    }

    void CreateParallelogram(Vector3 vLeft, Vector3 vUp, Vector3 center, PrimitiveFlags flags, Color color)
    {
        if (!VectorPhiSolver.TrySolve(vLeft, vUp, out float theta, out float phi))
        {
            CreateFallbackParallelogram(vLeft, vUp, center, flags, color);
            return;
        }

        StretchSpatialIndex.Entry? best = ApproximateModelUtils.FindBestStretch(
            _stretches, vLeft, vUp, theta, phi, _absoluteToleranceUnits);

        Primitive stretch;
        float stretchTheta, stretchPhi;

        if (best is { } match)
        {
            stretch = match.Stretch;
            stretchTheta = match.Theta;
            stretchPhi = match.Phi;
        }
        else
        {
            stretchTheta = theta;
            stretchPhi = phi;
            stretch = ApproximateModelUtils.CreateStretch(stretchTheta, stretchPhi);
            _stretches.Add(stretchTheta, stretchPhi, stretch);

            if (stretch.Transform.parent != BaseQuad.Transform)
                stretch.Transform.SetParent(BaseQuad.Transform);
        }

        Vector3 v1ForStretch = ApproximateModelUtils.ForwardTransform(vLeft, stretchTheta, stretchPhi);
        Vector3 v2ForStretch = ApproximateModelUtils.ForwardTransform(vUp, stretchTheta, stretchPhi);

        _parallelograms.Add(
            ApproximateModelUtils.CreateParallelogram(center, v1ForStretch, v2ForStretch, stretch, flags, color));
        _quadInfos.Add((vLeft, vUp, stretch));
        _parallelogramSnapshots.Add(new ParallelogramSnapshot(vUp, vLeft, center, color, flags, false));
    }

    /// <summary>
    ///     Drains sparsely-used stretches by rehoming their quads onto other stretches
    ///     within tolerance, then destroys the emptied stretches. Each one saves a primitive.
    /// </summary>
    void ConsolidateStretches()
    {
        HashSet<Primitive> emptied = ApproximateModelUtils.ConsolidateStretches(
            _stretches,
            _parallelograms.Count,
            i => _quadInfos[i].Stretch,
            i => (_quadInfos[i].VLeft, _quadInfos[i].VUp),
            RehomeQuad,
            _absoluteToleranceUnits);

        foreach (Primitive stretch in emptied)
        {
            _stretches.Remove(stretch);
            stretch.Destroy();
        }
    }

    void RehomeQuad(int index, StretchSpatialIndex.Entry target)
    {
        (Vector3 vLeft, Vector3 vUp, _) = _quadInfos[index];
        Vector3 v1 = ApproximateModelUtils.ForwardTransform(vLeft, target.Theta, target.Phi);
        Vector3 v2 = ApproximateModelUtils.ForwardTransform(vUp, target.Theta, target.Phi);
        ApproximateModelUtils.ReparentToStretch(_parallelograms[index], target.Stretch, v1, v2);
        _quadInfos[index] = (vLeft, vUp, target.Stretch);
    }

    void CreateFallbackParallelogram(Vector3 vLeft, Vector3 vUp, Vector3 center, PrimitiveFlags flags, Color color)
    {
        var parallelogram = ParallelogramPrimitive.Create(vUp, vLeft, center, color, flags);
        _fallbackParallelograms.Add(parallelogram);

        if (parallelogram.Transform.parent != BaseQuad.Transform)
            parallelogram.Transform.SetParent(BaseQuad.Transform);

        _parallelogramSnapshots.Add(new ParallelogramSnapshot(vUp, vLeft, center, color, flags, true));
    }

    /// <summary>
    ///     Max diagonal across all parallelograms, used for angular tolerance in stretch clustering.
    /// </summary>
    float ComputeMaxParallelogramSize()
    {
        var maxSize = 0.01f;

        foreach (ModelTriangle localTriangle in _localTriangles)
        {
            Vector3 p1 = TransformPoint(localTriangle.P1);
            Vector3 p2 = TransformPoint(localTriangle.P2);
            Vector3 p3 = TransformPoint(localTriangle.P3);

            if (InvertWinding) (p2, p3) = (p3, p2);

            Vector3[][] data = TriangleParallelogramBuilder.GetParallelogramsInfo(p1, p2, p3);

            for (var i = 0; i < 3; i++)
            {
                Vector3 v1 = data[i][0];
                Vector3 v2 = data[i][1];
                float size = Mathf.Max((v1 + v2).magnitude, (v1 - v2).magnitude);
                if (size > maxSize) maxSize = size;
            }
        }

        foreach (ModelParallelogram p in _localParallelograms)
        {
            float size = Mathf.Max((p.VLeft + p.VUp).magnitude, (p.VLeft - p.VUp).magnitude);
            if (size > maxSize) maxSize = size;
        }

        return maxSize;
    }
}