using System.Collections;
using AdminToys;
using Exiled.API.Features;
using Exiled.API.Features.Toys;
using TriangleScpSl.Core.Models.ApproximateModel;
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

        MarkUsedStretches();
        DestroyUnusedStretches();
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
        // Fallback path — VectorPhiSolver can't decompose these half-diagonals.
        if (!VectorPhiSolver.TrySolve(vLeft, vUp, out float theta, out float phi))
        {
            var parallelogram = ParallelogramPrimitive.Create(vUp, vLeft, center, color, flags);
            _fallbackParallelograms.Add(parallelogram);

            if (parallelogram.Transform.parent != BaseQuad.Transform)
                parallelogram.Transform.SetParent(BaseQuad.Transform);

            _parallelogramSnapshots.Add(new ParallelogramSnapshot(vUp, vLeft, center, color, flags, true));
            return;
        }

        // ── Try hierarchical parenting first ──────────────────────────────────
        // Check ALL existing visible parallelograms as potential parents.
        // No depth limit, no candidate limit, no perpendicularity pre-filter.
        // MeasureCornerError is the sole gatekeeper.
        if (TryCreateUnderParent(vLeft, vUp, center, flags, color))
            return;

        // ── Fall back to V2 stretch clustering ────────────────────────────────
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

        Primitive stretch;
        float stretchTheta, stretchPhi;

        if (bestStretch != null)
        {
            stretch = bestStretch;
            stretchTheta = bestTheta;
            stretchPhi = bestPhi;
        }
        else
        {
            stretch = ApproximateModelUtils.CreateStretch(theta, phi);
            _stretches.Add(theta, phi, stretch);

            if (stretch.Transform.parent != BaseQuad.Transform)
                stretch.Transform.SetParent(BaseQuad.Transform);

            stretchTheta = theta;
            stretchPhi = phi;
        }

        Vector3 v1ForStretch = ApproximateModelUtils.ForwardTransform(vLeft, stretchTheta, stretchPhi);
        Vector3 v2ForStretch = ApproximateModelUtils.ForwardTransform(vUp, stretchTheta, stretchPhi);

        int idx = _parallelograms.Count;

        _parallelograms.Add(
            ApproximateModelUtils.CreateParallelogram(center, v1ForStretch, v2ForStretch, stretch, flags, color));
        _parallelogramSnapshots.Add(new ParallelogramSnapshot(vUp, vLeft, center, color, flags, false));
        _quadBuildInfos.Add(new QuadBuildInfo(vLeft, vUp, center, stretch));
        _hierarchyDepths[idx] = 0;
    }

    /// <summary>
    ///     Tries to create a new parallelogram as a direct child of an existing visible
    ///     parallelogram. Searches ALL existing parallelograms. The only filter is
    ///     MeasureCornerError — if the world corners match within tolerance, we use it.
    ///     Returns true if a suitable parent was found and the child was created.
    /// </summary>
    bool TryCreateUnderParent(Vector3 vLeft, Vector3 vUp, Vector3 center, PrimitiveFlags flags, Color color)
    {
        int count = _parallelograms.Count;
        if (count == 0) return false;

        // The 4 world-space vertices of the target parallelogram.
        Vector3 wc0 = center + vUp;
        Vector3 wc1 = center + vLeft;
        Vector3 wc2 = center - vUp;
        Vector3 wc3 = center - vLeft;

        int bestParentIdx = -1;
        var bestError = float.MaxValue;
        Vector3 bestLocalPos = Vector3.zero;
        Quaternion bestLocalRot = Quaternion.identity;
        Vector3 bestLocalScale = Vector3.one;

        for (var pi = 0; pi < count; pi++)
        {
            Primitive parent = _parallelograms[pi];
            Transform parentTransform = parent.Transform;

            // Transform target corners to parent's local space.
            Vector3 lc0 = parentTransform.InverseTransformPoint(wc0);
            Vector3 lc1 = parentTransform.InverseTransformPoint(wc1);
            Vector3 lc2 = parentTransform.InverseTransformPoint(wc2);
            Vector3 lc3 = parentTransform.InverseTransformPoint(wc3);

            Vector3 localCenter = (lc0 + lc1 + lc2 + lc3) * 0.25f;

            // Half-diagonals in parent's local space.
            Vector3 hd1 = lc0 - localCenter;
            Vector3 hd2 = lc1 - localCenter;

            // Edges of the parallelogram in parent's local space.
            Vector3 e1 = hd1 + hd2;
            Vector3 e2 = hd1 - hd2;

            float e1Mag = e1.magnitude;
            float e2Mag = e2.magnitude;
            if (e1Mag < 1e-7f || e2Mag < 1e-7f) continue;

            // Compute the child's local rotation and scale.
            Vector3 localNormal = Vector3.Cross(e1, e2);
            if (localNormal.sqrMagnitude < 1e-12f) continue;
            localNormal = localNormal.normalized;

            Quaternion localRot = Quaternion.LookRotation(localNormal, e2.normalized);
            var localScale = new Vector3(e1Mag, e2Mag, 1f);

            // MeasureCornerError is the sole acceptance criterion.
            float error = MeasureCornerError(
                parentTransform, localCenter, localRot, localScale,
                wc0, wc1, wc2, wc3);

            if (error <= _absoluteToleranceUnits && error < bestError)
            {
                bestError = error;
                bestParentIdx = pi;
                bestLocalPos = localCenter;
                bestLocalRot = localRot;
                bestLocalScale = localScale;
            }
        }

        if (bestParentIdx < 0) return false;

        // Create the child directly under the parent.
        var quad = Primitive.Create(
            PrimitiveType.Quad, flags, Vector3.zero, null, Vector3.one, true, color);

        quad.Transform.SetParent(_parallelograms[bestParentIdx].Transform, false);
        quad.Transform.localPosition = bestLocalPos;
        quad.Transform.localRotation = bestLocalRot;
        quad.Transform.localScale = bestLocalScale;

        int childIdx = _parallelograms.Count;
        _parallelograms.Add(quad);
        _parallelogramSnapshots.Add(new ParallelogramSnapshot(vUp, vLeft, center, color, flags, false));
        _quadBuildInfos.Add(new QuadBuildInfo(vLeft, vUp, center, null));
        _hierarchicalParents[childIdx] = bestParentIdx;
        _hierarchicallyParentedCount++;

        int parentDepth = _hierarchyDepths.TryGetValue(bestParentIdx, out int pd) ? pd : 0;
        _hierarchyDepths[childIdx] = parentDepth + 1;

        return true;
    }

    /// <summary>
    ///     Measures the max world-space distance between the reconstructed corners
    ///     (from the proposed child local TRS under parentTransform) and the 4 target
    ///     world corners. Each reconstructed corner is matched to its closest target.
    /// </summary>
    static float MeasureCornerError
    (
        Transform parentTransform,
        Vector3 localPos, Quaternion localRot, Vector3 localScale,
        Vector3 target0, Vector3 target1, Vector3 target2, Vector3 target3)
    {
        var maxError = 0f;

        for (int cx = -1; cx <= 1; cx += 2)
        for (int cy = -1; cy <= 1; cy += 2)
        {
            Vector3 localCorner = localPos + localRot * new Vector3(
                cx * localScale.x * 0.5f,
                cy * localScale.y * 0.5f,
                0f);
            Vector3 worldCorner = parentTransform.TransformPoint(localCorner);

            float d0 = (worldCorner - target0).sqrMagnitude;
            float d1 = (worldCorner - target1).sqrMagnitude;
            float d2 = (worldCorner - target2).sqrMagnitude;
            float d3 = (worldCorner - target3).sqrMagnitude;
            float minSqr = Mathf.Min(Mathf.Min(d0, d1), Mathf.Min(d2, d3));

            if (minSqr > maxError) maxError = minSqr;
        }

        return Mathf.Sqrt(maxError);
    }

    void MarkUsedStretches()
    {
        _usedStretches.Clear();

        for (var i = 0; i < _parallelograms.Count; i++)
        {
            if (_hierarchicalParents.ContainsKey(i)) continue;

            Primitive? stretch = _quadBuildInfos[i].Stretch;

            if (stretch != null)
                _usedStretches.Add(stretch);
        }
    }

    void DestroyUnusedStretches()
    {
        foreach (StretchSpatialIndex.Entry entry in _stretches.All())
        {
            if (!_usedStretches.Contains(entry.Stretch))
                entry.Stretch.Destroy();
        }
    }

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