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
    /// <summary>
    ///     Maximum hierarchy depth to prevent excessively deep chains
    ///     that would amplify floating-point error.
    /// </summary>
    const int MaxHierarchyDepth = 4;

    /// <summary>
    ///     Minimum parallelogram size (diagonal) for a quad to be eligible as a parent.
    ///     Very small quads produce large local-scale values for children, amplifying error.
    /// </summary>
    const float MinParentDiagonal = 0.05f;

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
        _hierarchicalParents.Clear();
        _usedStretches.Clear();

        // Phase 1: Build all parallelograms using V2 stretch-clustering (same as ApproximateModel)
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

        // Phase 2: Hierarchical reparenting pass
        ReparentParallelograms();

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
        _hierarchicalParents.Clear();
        _usedStretches.Clear();

        // Phase 1: Build all parallelograms using V2 stretch-clustering
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

        // Phase 2: Hierarchical reparenting pass (one frame, typically fast)
        ReparentParallelograms();
        yield return null;

        BuildNativePrimitives(flags);
    }

    /// <summary>
    ///     Phase 2: Try to reparent visible parallelograms onto other visible parallelograms.
    ///     For each parallelogram, we check if any other parallelogram's world transform
    ///     can serve as a parent such that the child's local transform produces
    ///     the target world-space shape within tolerance.
    /// </summary>
    void ReparentParallelograms()
    {
        if (_parallelograms.Count < 2) return;

        int count = _parallelograms.Count;

        // Build world matrices for all parallelograms
        var worldMatrices = new Matrix4x4[count];
        var diagonals = new float[count];
        var depths = new int[count]; // hierarchy depth: 0 = parented to stretch/base

        for (var i = 0; i < count; i++)
        {
            Transform t = _parallelograms[i].Transform;
            worldMatrices[i] = t.localToWorldMatrix;

            Vector3 scale = t.lossyScale;
            diagonals[i] = Mathf.Sqrt(scale.x * scale.x + scale.y * scale.y);
            depths[i] = 0;
        }

        // Track which stretch each parallelogram currently uses
        var stretchByParallelogram = new Primitive?[count];

        for (var i = 0; i < count; i++)
        {
            Transform parent = _parallelograms[i].Transform.parent;

            if (parent != null && parent != BaseQuad.Transform)
                stretchByParallelogram[i] = FindStretchPrimitive(parent);
        }

        // Track how many children each stretch has
        var stretchChildCount = new Dictionary<Primitive, int>();

        foreach (StretchSpatialIndex.Entry entry in _stretches.All())
            stretchChildCount[entry.Stretch] = 0;

        for (var i = 0; i < count; i++)
        {
            Primitive? stretch = stretchByParallelogram[i];

            if (stretch != null && stretchChildCount.ContainsKey(stretch))
                stretchChildCount[stretch]++;
        }

        // Sort by diagonal (largest first) — larger quads make better parents
        var sortedIndices = new int[count];

        for (var i = 0; i < count; i++)
        {
            sortedIndices[i] = i;
        }

        Array.Sort(sortedIndices, (a, b) => diagonals[b].CompareTo(diagonals[a]));

        // Try to reparent each parallelogram onto a larger one
        for (int si = count - 1; si >= 0; si--) // smallest first as children
        {
            int childIdx = sortedIndices[si];
            Primitive child = _parallelograms[childIdx];
            Matrix4x4 childWorld = worldMatrices[childIdx];

            Primitive? bestParent = null;
            int bestParentIdx = -1;
            var bestError = float.MaxValue;
            Vector3 bestLocalPos = Vector3.zero;
            Quaternion bestLocalRot = Quaternion.identity;
            Vector3 bestLocalScale = Vector3.one;

            for (var pi = 0; pi < si; pi++) // larger quads only
            {
                int parentIdx = sortedIndices[pi];

                // Skip if this would exceed max depth
                if (depths[parentIdx] + 1 >= MaxHierarchyDepth)
                    continue;

                // Skip tiny parents
                if (diagonals[parentIdx] < MinParentDiagonal)
                    continue;

                // Skip if child is already parented to this
                if (_hierarchicalParents.TryGetValue(childIdx, out int existingParent) && existingParent == parentIdx)
                    continue;

                // Compute local transform: child_local = parent_world^-1 * child_world
                Matrix4x4 parentWorldInv = worldMatrices[parentIdx].inverse;
                Matrix4x4 localMatrix = parentWorldInv * childWorld;

                // Decompose local matrix to TRS
                if (!TryDecomposeMatrix(localMatrix, out Vector3 localPos, out Quaternion localRot, out Vector3 localScale))
                    continue;

                // Validate: reconstruct world corners and measure error
                float error = MeasureReparentError(
                    childWorld,
                    worldMatrices[parentIdx],
                    localPos, localRot, localScale);

                if (error <= _absoluteToleranceUnits && error < bestError)
                {
                    bestError = error;
                    bestParent = _parallelograms[parentIdx];
                    bestParentIdx = parentIdx;
                    bestLocalPos = localPos;
                    bestLocalRot = localRot;
                    bestLocalScale = localScale;
                }
            }

            if (bestParent == null)
            {
                // No hierarchical parent found, mark stretch as used
                Primitive? stretch = stretchByParallelogram[childIdx];

                if (stretch != null)
                    _usedStretches.Add(stretch);

                continue;
            }

            // Reparent: detach from old stretch, attach to visible parent
            Primitive? oldStretch = stretchByParallelogram[childIdx];

            child.Transform.SetParent(bestParent.Transform, true);
            child.Transform.localPosition = bestLocalPos;
            child.Transform.localRotation = bestLocalRot;
            child.Transform.localScale = bestLocalScale;

            _hierarchicalParents[childIdx] = bestParentIdx;
            depths[childIdx] = depths[bestParentIdx] + 1;

            // Update world matrix after reparenting (for cascading)
            worldMatrices[childIdx] = child.Transform.localToWorldMatrix;

            // Decrement old stretch's child count
            if (oldStretch != null && stretchChildCount.ContainsKey(oldStretch))
            {
                stretchChildCount[oldStretch]--;

                // If stretch has no more children, it can be destroyed
                if (stretchChildCount[oldStretch] <= 0)
                    stretchChildCount.Remove(oldStretch);
            }
        }

        // Mark all stretches that still have children as used
        foreach (KeyValuePair<Primitive, int> kv in stretchChildCount)
        {
            if (kv.Value > 0)
                _usedStretches.Add(kv.Key);
        }

        // Destroy unused stretches
        foreach (StretchSpatialIndex.Entry entry in _stretches.All())
        {
            if (!_usedStretches.Contains(entry.Stretch))
                entry.Stretch.Destroy();
        }

        Log.Debug($"[HierarchicalModel] Reparented {_hierarchicalParents.Count}/{count} parallelograms. " +
            $"Stretches: {_usedStretches.Count} used / {_stretches.Count} total (saved {_stretches.Count - _usedStretches.Count}).");
    }

    Primitive? FindStretchPrimitive(Transform stretchTransform)
    {
        foreach (StretchSpatialIndex.Entry entry in _stretches.All())
        {
            if (entry.Stretch.Transform == stretchTransform)
                return entry.Stretch;
        }

        return null;
    }

    /// <summary>
    ///     Decompose a 4x4 matrix into Translation, Rotation, Scale.
    ///     Returns false if the matrix contains shear that would produce unacceptable deformation.
    /// </summary>
    static bool TryDecomposeMatrix(Matrix4x4 m, out Vector3 position, out Quaternion rotation, out Vector3 scale)
    {
        position = m.GetColumn(3);

        Vector3 col0 = m.GetColumn(0);
        Vector3 col1 = m.GetColumn(1);
        Vector3 col2 = m.GetColumn(2);

        scale = new Vector3(col0.magnitude, col1.magnitude, col2.magnitude);

        // Check for degenerate scale
        if (scale.x < 1e-6f || scale.y < 1e-6f || scale.z < 1e-6f)
        {
            rotation = Quaternion.identity;
            return false;
        }

        // Normalize columns to extract rotation
        Vector3 c0 = col0 / scale.x;
        Vector3 c1 = col1 / scale.y;
        Vector3 c2 = col2 / scale.z;

        // Check for shear: columns should be roughly orthogonal
        float dot01 = Mathf.Abs(Vector3.Dot(c0, c1));
        float dot02 = Mathf.Abs(Vector3.Dot(c0, c2));
        float dot12 = Mathf.Abs(Vector3.Dot(c1, c2));

        const float shearThreshold = 0.15f;

        if (dot01 > shearThreshold || dot02 > shearThreshold || dot12 > shearThreshold)
        {
            rotation = Quaternion.identity;
            return false;
        }

        // Handle reflection (negative determinant)
        float det = Vector3.Dot(c0, Vector3.Cross(c1, c2));

        if (det < 0f)
        {
            scale.x = -scale.x;
            c0 = -c0;
        }

        // Build rotation matrix
        var rotMatrix = new Matrix4x4();
        rotMatrix.SetColumn(0, new Vector4(c0.x, c0.y, c0.z, 0));
        rotMatrix.SetColumn(1, new Vector4(c1.x, c1.y, c1.z, 0));
        rotMatrix.SetColumn(2, new Vector4(c2.x, c2.y, c2.z, 0));
        rotMatrix.SetColumn(3, new Vector4(0, 0, 0, 1));

        rotation = rotMatrix.rotation;
        return true;
    }

    /// <summary>
    ///     Measures world-space error of reparenting by reconstructing the 4 corners
    ///     of the child quad under the proposed parent and comparing to original positions.
    /// </summary>
    static float MeasureReparentError
    (
        Matrix4x4 childWorld,
        Matrix4x4 parentWorld,
        Vector3 localPos, Quaternion localRot, Vector3 localScale)
    {
        // Reconstruct child world matrix via parent
        Matrix4x4 localMatrix = Matrix4x4.TRS(localPos, localRot, localScale);
        Matrix4x4 reconstructed = parentWorld * localMatrix;

        // Compare the 4 corners of a unit quad: (+-0.5, +-0.5, 0)
        var maxError = 0f;

        for (int cx = -1; cx <= 1; cx += 2)
        for (int cy = -1; cy <= 1; cy += 2)
        {
            var corner = new Vector4(cx * 0.5f, cy * 0.5f, 0f, 1f);

            Vector4 original = childWorld * corner;
            Vector4 rebuilt = reconstructed * corner;

            float dx = original.x - rebuilt.x;
            float dy = original.y - rebuilt.y;
            float dz = original.z - rebuilt.z;
            float err = Mathf.Sqrt(dx * dx + dy * dy + dz * dz);

            if (err > maxError)
                maxError = err;
        }

        return maxError;
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
        _parallelograms.Add(quad);
        _parallelogramSnapshots.Add(new ParallelogramSnapshot(vUp, vLeft, center, color, flags, false));
    }

    void CreateParallelogram(Vector3 vLeft, Vector3 vUp, Vector3 center, PrimitiveFlags flags, Color color)
    {
        if (!VectorPhiSolver.TrySolve(vLeft, vUp, out float theta, out float phi))
        {
            var parallelogram = ParallelogramPrimitive.Create(vUp, vLeft, center, color, flags);
            _fallbackParallelograms.Add(parallelogram);

            if (parallelogram.Transform.parent != BaseQuad.Transform)
                parallelogram.Transform.SetParent(BaseQuad.Transform);

            _parallelogramSnapshots.Add(new ParallelogramSnapshot(vUp, vLeft, center, color, flags, true));
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

        _parallelograms.Add(
            ApproximateModelUtils.CreateParallelogram(center, v1ForStretch, v2ForStretch, stretch, flags, color));
        _parallelogramSnapshots.Add(new ParallelogramSnapshot(vUp, vLeft, center, color, flags, false));
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