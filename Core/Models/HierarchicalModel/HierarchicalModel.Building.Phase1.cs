using AdminToys;
using Exiled.API.Features.Toys;
using TriangleScpSl.Core.Models.ApproximateModel;
using TriangleScpSl.Core.Primitives.Parallelogram;
using TriangleScpSl.Core.Primitives.Triangle;
using UnityEngine;

namespace TriangleScpSl.Core.Models.HierarchicalModel;

public partial class HierarchicalModel
{
    void CreateTriangle(ModelTriangle localTriangle, PrimitiveFlags flags)
    {
        Vector3 p1 = TransformPoint(localTriangle.P1);
        Vector3 p2 = TransformPoint(localTriangle.P2);
        Vector3 p3 = TransformPoint(localTriangle.P3);
        if (InvertWinding) (p2, p3) = (p3, p2);

        Vector3[][] data = TriangleParallelogramBuilder.GetParallelogramsInfo(p1, p2, p3);
        CreateParallelogram(data[0][0], data[0][1], data[0][2], flags, localTriangle.Color);
        CreateParallelogram(data[1][0], data[1][1], data[1][2], flags, localTriangle.Color);
        CreateParallelogram(data[2][0], data[2][1], data[2][2], flags, localTriangle.Color);
    }

    void CreateRectangle(Vector3 vLeft, Vector3 vUp, Vector3 center, PrimitiveFlags flags, Color color)
    {
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

        int idx = _parallelograms.Count;
        _parallelograms.Add(quad);
        _parallelogramSnapshots.Add(new ParallelogramSnapshot(vUp, vLeft, center, color, flags, false));
        _quadBuildInfos.Add(new QuadBuildInfo(vLeft, vUp, center, null));
        _hierarchyDepths[idx] = 0;
    }

    void CreateParallelogram(Vector3 vLeft, Vector3 vUp, Vector3 center, PrimitiveFlags flags, Color color)
    {
        // Near-zero-area parallelograms (degenerate triangles produce them with sizable diagonals)
        // have no reliable orientation and render as stray rectangles, therefroe skip
        float sizeSqr = Mathf.Max((vLeft + vUp).sqrMagnitude, (vLeft - vUp).sqrMagnitude);

        if (Vector3.Cross(vLeft, vUp).sqrMagnitude <= sizeSqr * 1e-8f)
            return;

        if (!VectorPhiSolver.TrySolve(vLeft, vUp, out float theta, out float phi))
        {
            CreateFallbackParallelogram(vLeft, vUp, center, flags, color);
            return;
        }

        // Try to parent under an existing visible quad.
        if (TryCreateUnderParent(vLeft, vUp, center, flags, color))
            return;

        // V2 stretch clustering fallback.
        StretchSpatialIndex.Entry? best = ApproximateModelUtils.FindBestStretch(
            _stretches, vLeft, vUp, theta, phi, _absoluteToleranceUnits);

        Primitive stretch;
        float sT, sP;

        if (best is { } match)
        {
            stretch = match.Stretch;
            sT = match.Theta;
            sP = match.Phi;
        }
        else
        {
            sT = theta;
            sP = phi;
            stretch = ApproximateModelUtils.CreateStretch(sT, sP);
            _stretches.Add(sT, sP, stretch);

            if (stretch.Transform.parent != BaseQuad.Transform)
                stretch.Transform.SetParent(BaseQuad.Transform);
        }

        Vector3 v1 = ApproximateModelUtils.ForwardTransform(vLeft, sT, sP);
        Vector3 v2 = ApproximateModelUtils.ForwardTransform(vUp, sT, sP);

        int idx = _parallelograms.Count;
        _parallelograms.Add(ApproximateModelUtils.CreateParallelogram(center, v1, v2, stretch, flags, color));
        _parallelogramSnapshots.Add(new ParallelogramSnapshot(vUp, vLeft, center, color, flags, false));
        _quadBuildInfos.Add(new QuadBuildInfo(vLeft, vUp, center, stretch));
        _hierarchyDepths[idx] = 0;
    }

    void CreateFallbackParallelogram(Vector3 vLeft, Vector3 vUp, Vector3 center, PrimitiveFlags flags, Color color)
    {
        var parallelogram = ParallelogramPrimitive.Create(vUp, vLeft, center, color, flags);
        _fallbackParallelograms.Add(parallelogram);

        if (parallelogram.Transform.parent != BaseQuad.Transform)
            parallelogram.Transform.SetParent(BaseQuad.Transform);
        _parallelogramSnapshots.Add(new ParallelogramSnapshot(vUp, vLeft, center, color, flags, true));
    }

    bool TryCreateUnderParent(Vector3 vLeft, Vector3 vUp, Vector3 center, PrimitiveFlags flags, Color color)
    {
        int count = _parallelograms.Count;
        if (count == 0) return false;

        Vector3 wc0 = center + vUp, wc1 = center + vLeft;
        Vector3 wc2 = center - vUp, wc3 = center - vLeft;

        int bestIdx = -1;
        var bestErr = float.MaxValue;
        Vector3 bestLp = default;
        Quaternion bestLr = default;
        Vector3 bestLs = default;

        for (var pi = 0; pi < count; pi++)
        {
            if (!IsStretchFreeInHierarchy(pi)) continue;

            if (TryFitUnderQuad(_parallelograms[pi].Transform, wc0, wc1, wc2, wc3,
                    out Vector3 lp, out Quaternion lr, out Vector3 ls, out float err) && err < bestErr)
            {
                bestErr = err;
                bestIdx = pi;
                bestLp = lp;
                bestLr = lr;
                bestLs = ls;
            }
        }

        if (bestIdx < 0) return false;

        var quad = Primitive.Create(PrimitiveType.Quad, flags, Vector3.zero, null, Vector3.one, true, color);
        quad.Transform.SetParent(_parallelograms[bestIdx].Transform, false);
        quad.Transform.localPosition = bestLp;
        quad.Transform.localRotation = bestLr;
        quad.Transform.localScale = bestLs;

        int childIdx = _parallelograms.Count;
        _parallelograms.Add(quad);
        _parallelogramSnapshots.Add(new ParallelogramSnapshot(vUp, vLeft, center, color, flags, false));
        _quadBuildInfos.Add(new QuadBuildInfo(vLeft, vUp, center, null));
        _hierarchicalParents[childIdx] = bestIdx;
        ReparentedCount++;
        _hierarchyDepths[childIdx] = (_hierarchyDepths.TryGetValue(bestIdx, out int pd) ? pd : 0) + 1;
        return true;
    }
}