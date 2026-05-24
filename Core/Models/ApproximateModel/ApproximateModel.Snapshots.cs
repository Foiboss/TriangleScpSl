using AdminToys;
using Exiled.API.Features.Toys;
using TriangleScpSl.Core.Decomposition.NGonDecomposition;
using TriangleScpSl.Core.Primitives.Parallelogram;
using TriangleScpSl.Core.Primitives.Triangle;
using UnityEngine;

namespace TriangleScpSl.Core.Models.ApproximateModel;

public partial class ApproximateModel
{
    public IReadOnlyList<(ModelTriangle Triangle, PrimitiveFlags Flags)> GetTriangleSnapshot()
    {
        if (IsDestroyedValue) return [];

        List<(ModelTriangle Triangle, PrimitiveFlags Flags)> snapshot = new(_localTriangles.Count);

        foreach (ModelTriangle localTriangle in _localTriangles)
        {
            Vector3 p1 = TransformPoint(localTriangle.P1);
            Vector3 p2 = TransformPoint(localTriangle.P2);
            Vector3 p3 = TransformPoint(localTriangle.P3);

            if (InvertWinding)
                (p2, p3) = (p3, p2);

            snapshot.Add((new ModelTriangle(p1, p2, p3, localTriangle.Color), FlagsValue));
        }

        return snapshot;
    }

    public IReadOnlyList<ParallelogramSnapshot> GetParallelogramSnapshot()
    {
        if (IsDestroyedValue) return [];
        return _parallelogramSnapshots.ToArray();
    }

    public IReadOnlyList<PrimitiveSnapshot> GetPrimitiveSnapshot()
    {
        if (IsDestroyedValue) return [];

        List<PrimitiveSnapshot> snapshot = new(PrimitiveCount);
        Dictionary<Transform, int> indexByTransform = new(PrimitiveCount);

        int modelBaseIndex = snapshot.Count;

        snapshot.Add(new PrimitiveSnapshot(
            BaseQuad.Position,
            BaseQuad.Rotation,
            BaseQuad.Scale,
            Vector3.zero,
            Quaternion.identity,
            Vector3.one,
            BaseQuad.Color,
            BaseQuad.Flags,
            "ModelBase",
            -1));
        indexByTransform[BaseQuad.Transform] = modelBaseIndex;

        foreach (StretchSpatialIndex.Entry entry in _stretches.All())
        {
            int stretchIndex = snapshot.Count;
            Transform stretchTransform = entry.Stretch.Transform;
            int parentIndex = indexByTransform.TryGetValue(stretchTransform.parent, out int foundParent) ? foundParent : modelBaseIndex;

            snapshot.Add(new PrimitiveSnapshot(
                entry.Stretch.Position,
                entry.Stretch.Rotation,
                entry.Stretch.Scale,
                stretchTransform.localPosition,
                stretchTransform.localRotation,
                stretchTransform.localScale,
                entry.Stretch.Color,
                entry.Stretch.Flags,
                "Stretch",
                parentIndex));

            indexByTransform[stretchTransform] = stretchIndex;
        }

        foreach (Primitive parallelogram in _parallelograms)
        {
            Transform parallelogramTransform = parallelogram.Transform;
            int parentIndex = indexByTransform.TryGetValue(parallelogramTransform.parent, out int foundParent) ? foundParent : modelBaseIndex;

            snapshot.Add(new PrimitiveSnapshot(
                parallelogram.Position,
                parallelogram.Rotation,
                parallelogram.Scale,
                parallelogramTransform.localPosition,
                parallelogramTransform.localRotation,
                parallelogramTransform.localScale,
                parallelogram.Color,
                parallelogram.Flags,
                "Parallelogram",
                parentIndex));
        }

        foreach (ParallelogramPrimitive fallback in _fallbackParallelograms)
        {
            Primitive fallbackBase = fallback.BasePrimitive;
            Transform fallbackBaseTransform = fallbackBase.Transform;
            int fallbackBaseParent = indexByTransform.TryGetValue(fallbackBaseTransform.parent, out int foundBaseParent) ? foundBaseParent : modelBaseIndex;

            int fallbackBaseIndex = snapshot.Count;

            snapshot.Add(new PrimitiveSnapshot(
                fallbackBase.Position,
                fallbackBase.Rotation,
                fallbackBase.Scale,
                fallbackBaseTransform.localPosition,
                fallbackBaseTransform.localRotation,
                fallbackBaseTransform.localScale,
                fallbackBase.Color,
                fallbackBase.Flags,
                "FallbackBase",
                fallbackBaseParent));

            indexByTransform[fallbackBaseTransform] = fallbackBaseIndex;

            Primitive fallbackQuad = fallback.QuadPrimitive;
            Transform fallbackQuadTransform = fallbackQuad.Transform;
            int fallbackQuadParent = indexByTransform.TryGetValue(fallbackQuadTransform.parent, out int foundQuadParent) ? foundQuadParent : fallbackBaseIndex;

            snapshot.Add(new PrimitiveSnapshot(
                fallbackQuad.Position,
                fallbackQuad.Rotation,
                fallbackQuad.Scale,
                fallbackQuadTransform.localPosition,
                fallbackQuadTransform.localRotation,
                fallbackQuadTransform.localScale,
                fallbackQuad.Color,
                fallbackQuad.Flags,
                "FallbackQuad",
                fallbackQuadParent));
        }

        AppendNativePrimitiveSnapshots(snapshot, indexByTransform, modelBaseIndex);

        return snapshot;
    }

    /// <summary>
    ///     Returns a primitive snapshot without native primitives.
    ///     Used by export code which computes native primitive blocks
    ///     directly from <see cref="ModelPrimitive" /> data.
    /// </summary>
    IReadOnlyList<PrimitiveSnapshot> GetPrimitiveSnapshotWithoutNatives()
    {
        if (IsDestroyedValue) return [];

        int parallelogramPrimitiveCount = _stretches.Count + _parallelograms.Count + _fallbackParallelograms.Count * 2 + 1;
        List<PrimitiveSnapshot> snapshot = new(parallelogramPrimitiveCount);
        Dictionary<Transform, int> indexByTransform = new(parallelogramPrimitiveCount);

        int modelBaseIndex = snapshot.Count;

        snapshot.Add(new PrimitiveSnapshot(
            BaseQuad.Position,
            BaseQuad.Rotation,
            BaseQuad.Scale,
            Vector3.zero,
            Quaternion.identity,
            Vector3.one,
            BaseQuad.Color,
            BaseQuad.Flags,
            "ModelBase",
            -1));
        indexByTransform[BaseQuad.Transform] = modelBaseIndex;

        foreach (StretchSpatialIndex.Entry entry in _stretches.All())
        {
            int stretchIndex = snapshot.Count;
            Transform stretchTransform = entry.Stretch.Transform;
            int parentIndex = indexByTransform.TryGetValue(stretchTransform.parent, out int foundParent) ? foundParent : modelBaseIndex;

            snapshot.Add(new PrimitiveSnapshot(
                entry.Stretch.Position,
                entry.Stretch.Rotation,
                entry.Stretch.Scale,
                stretchTransform.localPosition,
                stretchTransform.localRotation,
                stretchTransform.localScale,
                entry.Stretch.Color,
                entry.Stretch.Flags,
                "Stretch",
                parentIndex));

            indexByTransform[stretchTransform] = stretchIndex;
        }

        foreach (Primitive parallelogram in _parallelograms)
        {
            Transform parallelogramTransform = parallelogram.Transform;
            int parentIndex = indexByTransform.TryGetValue(parallelogramTransform.parent, out int foundParent) ? foundParent : modelBaseIndex;

            snapshot.Add(new PrimitiveSnapshot(
                parallelogram.Position,
                parallelogram.Rotation,
                parallelogram.Scale,
                parallelogramTransform.localPosition,
                parallelogramTransform.localRotation,
                parallelogramTransform.localScale,
                parallelogram.Color,
                parallelogram.Flags,
                "Parallelogram",
                parentIndex));
        }

        foreach (ParallelogramPrimitive fallback in _fallbackParallelograms)
        {
            Primitive fallbackBase = fallback.BasePrimitive;
            Transform fallbackBaseTransform = fallbackBase.Transform;
            int fallbackBaseParent = indexByTransform.TryGetValue(fallbackBaseTransform.parent, out int foundBaseParent) ? foundBaseParent : modelBaseIndex;

            int fallbackBaseIndex = snapshot.Count;

            snapshot.Add(new PrimitiveSnapshot(
                fallbackBase.Position,
                fallbackBase.Rotation,
                fallbackBase.Scale,
                fallbackBaseTransform.localPosition,
                fallbackBaseTransform.localRotation,
                fallbackBaseTransform.localScale,
                fallbackBase.Color,
                fallbackBase.Flags,
                "FallbackBase",
                fallbackBaseParent));

            indexByTransform[fallbackBaseTransform] = fallbackBaseIndex;

            Primitive fallbackQuad = fallback.QuadPrimitive;
            Transform fallbackQuadTransform = fallbackQuad.Transform;
            int fallbackQuadParent = indexByTransform.TryGetValue(fallbackQuadTransform.parent, out int foundQuadParent) ? foundQuadParent : fallbackBaseIndex;

            snapshot.Add(new PrimitiveSnapshot(
                fallbackQuad.Position,
                fallbackQuad.Rotation,
                fallbackQuad.Scale,
                fallbackQuadTransform.localPosition,
                fallbackQuadTransform.localRotation,
                fallbackQuadTransform.localScale,
                fallbackQuad.Color,
                fallbackQuad.Flags,
                "FallbackQuad",
                fallbackQuadParent));
        }

        return snapshot;
    }

    void AppendNativePrimitiveSnapshots
    (
        List<PrimitiveSnapshot> snapshot,
        Dictionary<Transform, int> indexByTransform,
        int modelBaseIndex)
    {
        for (var i = 0; i < NativePrimitives.Count; i++)
        {
            Primitive native = NativePrimitives[i];
            ModelPrimitive model = ModelPrimitives[i];
            bool hasBase = i < NativePrimitiveBases.Count && NativePrimitiveBases[i] != null;

            if (hasBase)
            {
                Primitive basePrim = NativePrimitiveBases[i];
                Transform baseTransform = basePrim.Transform;
                int baseParent = indexByTransform.TryGetValue(baseTransform.parent, out int foundBase) ? foundBase : modelBaseIndex;
                int baseIndex = snapshot.Count;

                snapshot.Add(new PrimitiveSnapshot(
                    basePrim.Position, basePrim.Rotation, basePrim.Scale,
                    baseTransform.localPosition, baseTransform.localRotation, baseTransform.localScale,
                    basePrim.Color, basePrim.Flags, "NativeBase", baseParent));

                indexByTransform[baseTransform] = baseIndex;

                Transform nativeTransform = native.Transform;
                int nativeParent = indexByTransform.TryGetValue(nativeTransform.parent, out int foundNative) ? foundNative : baseIndex;

                snapshot.Add(new PrimitiveSnapshot(
                    native.Position, native.Rotation, native.Scale,
                    nativeTransform.localPosition, nativeTransform.localRotation, nativeTransform.localScale,
                    native.Color, native.Flags, "Native", nativeParent, model.Type));
            }
            else
            {
                Transform nativeTransform = native.Transform;
                int nativeParent = indexByTransform.TryGetValue(nativeTransform.parent, out int foundNative) ? foundNative : modelBaseIndex;

                snapshot.Add(new PrimitiveSnapshot(
                    native.Position, native.Rotation, native.Scale,
                    nativeTransform.localPosition, nativeTransform.localRotation, nativeTransform.localScale,
                    native.Color, native.Flags, "Native", nativeParent, model.Type));
            }
        }
    }
}