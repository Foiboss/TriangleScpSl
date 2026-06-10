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
        _parallelogramSnapshots.Add(new ParallelogramSnapshot(vUp, vLeft, center, color, flags, false));
    }

    void CreateParallelogram(Vector3 vLeft, Vector3 vUp, Vector3 center, PrimitiveFlags flags, Color color)
    {
        if (!VectorPhiSolver.TrySolve(vLeft, vUp, out float theta, out float phi))
        {
            CreateFallbackParallelogram(vLeft, vUp, center, flags, color);
            return;
        }

        Primitive? bestStretch = null;
        float bestTheta = 0f, bestPhi = 0f;
        var bestErr = float.MaxValue;

        foreach (StretchSpatialIndex.Entry entry in _stretches.QueryNearby(theta, phi))
        {
            float err = ApproximateModelUtils.MaxVertexError(
                vLeft, vUp, entry.Theta, entry.Phi);

            if (err <= _absoluteToleranceUnits && err < bestErr)
            {
                bestErr = err;
                bestStretch = entry.Stretch;
                bestTheta = entry.Theta;
                bestPhi = entry.Phi;
            }
        }

        float stretchTheta, stretchPhi;

        if (bestStretch != null)
        {
            stretchTheta = bestTheta;
            stretchPhi = bestPhi;
        }
        else
        {
            stretchTheta = theta;
            stretchPhi = phi;
        }

        Vector3 v1ForStretch = ApproximateModelUtils.ForwardTransform(vLeft, stretchTheta, stretchPhi);
        Vector3 v2ForStretch = ApproximateModelUtils.ForwardTransform(vUp, stretchTheta, stretchPhi);

        Primitive stretch;

        if (bestStretch != null)
        {
            stretch = bestStretch;
        }
        else
        {
            stretch = ApproximateModelUtils.CreateStretch(stretchTheta, stretchPhi);
            _stretches.Add(stretchTheta, stretchPhi, stretch);

            if (stretch.Transform.parent != BaseQuad.Transform)
                stretch.Transform.SetParent(BaseQuad.Transform);
        }

        _parallelograms.Add(
            ApproximateModelUtils.CreateParallelogram(center, v1ForStretch, v2ForStretch, stretch, flags, color));
        _parallelogramSnapshots.Add(new ParallelogramSnapshot(vUp, vLeft, center, color, flags, false));
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