using AdminToys;
using Exiled.API.Features.Toys;
using TriangleScpSl.Core.Decomposition.NGonDecomposition;
using TriangleScpSl.Core.Models.ApproximateModel;
using TriangleScpSl.Core.Primitives.Parallelogram;
using TriangleScpSl.Core.Primitives.Triangle;
using UnityEngine;

namespace TriangleScpSl.Core.Models.HierarchicalModel;

public partial class HierarchicalModel
{
    public IReadOnlyList<(ModelTriangle Triangle, PrimitiveFlags Flags)> GetTriangleSnapshot()
    {
        if (IsDestroyedValue) return [];
        List<(ModelTriangle, PrimitiveFlags)> snapshot = new(_localTriangles.Count);

        foreach (ModelTriangle lt in _localTriangles)
        {
            Vector3 p1 = TransformPoint(lt.P1), p2 = TransformPoint(lt.P2), p3 = TransformPoint(lt.P3);
            if (InvertWinding) (p2, p3) = (p3, p2);
            snapshot.Add((new ModelTriangle(p1, p2, p3, lt.Color), FlagsValue));
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
        var snapshot = new List<PrimitiveSnapshot>(PrimitiveCount);
        var idx = new Dictionary<Transform, int>(PrimitiveCount);

        int baseIdx = snapshot.Count;

        snapshot.Add(new PrimitiveSnapshot(BaseQuad.Position, BaseQuad.Rotation, BaseQuad.Scale,
            Vector3.zero, Quaternion.identity, Vector3.one, BaseQuad.Color, BaseQuad.Flags, "ModelBase", -1));
        idx[BaseQuad.Transform] = baseIdx;

        foreach (StretchSpatialIndex.Entry e in _stretches.All())
        {
            if (!_usedStretches.Contains(e.Stretch)) continue;
            int si = snapshot.Count;
            Transform st = e.Stretch.Transform;
            int pi = idx.TryGetValue(st.parent, out int fp) ? fp : baseIdx;

            snapshot.Add(new PrimitiveSnapshot(e.Stretch.Position, e.Stretch.Rotation, e.Stretch.Scale,
                st.localPosition, st.localRotation, st.localScale, e.Stretch.Color, e.Stretch.Flags, "Stretch", pi));
            idx[st] = si;
        }

        foreach (Primitive p in _parallelograms)
        {
            Transform pt = p.Transform;
            int pi = idx.TryGetValue(pt.parent, out int fp) ? fp : baseIdx;
            int pIdx = snapshot.Count;

            snapshot.Add(new PrimitiveSnapshot(p.Position, p.Rotation, p.Scale,
                pt.localPosition, pt.localRotation, pt.localScale, p.Color, p.Flags, "Parallelogram", pi));
            idx[pt] = pIdx;
        }

        foreach (ParallelogramPrimitive fb in _fallbackParallelograms)
        {
            Primitive b = fb.BasePrimitive;
            Transform bt = b.Transform;
            int bp = idx.TryGetValue(bt.parent, out int fbp) ? fbp : baseIdx;
            int bi = snapshot.Count;

            snapshot.Add(new PrimitiveSnapshot(b.Position, b.Rotation, b.Scale,
                bt.localPosition, bt.localRotation, bt.localScale, b.Color, b.Flags, "FallbackBase", bp));
            idx[bt] = bi;

            Primitive q = fb.QuadPrimitive;
            Transform qt = q.Transform;
            int qp = idx.TryGetValue(qt.parent, out int fqp) ? fqp : bi;

            snapshot.Add(new PrimitiveSnapshot(q.Position, q.Rotation, q.Scale,
                qt.localPosition, qt.localRotation, qt.localScale, q.Color, q.Flags, "FallbackQuad", qp));
        }

        AppendNativePrimitiveSnapshots(snapshot, idx, baseIdx);
        return snapshot;
    }

    internal IReadOnlyList<PrimitiveSnapshot> GetPrimitiveSnapshotWithoutNatives()
    {
        if (IsDestroyedValue) return [];
        int cap = _usedStretches.Count + _parallelograms.Count + _fallbackParallelograms.Count * 2 + 1;
        var snapshot = new List<PrimitiveSnapshot>(cap);
        var idx = new Dictionary<Transform, int>(cap);

        int baseIdx = snapshot.Count;

        snapshot.Add(new PrimitiveSnapshot(BaseQuad.Position, BaseQuad.Rotation, BaseQuad.Scale,
            Vector3.zero, Quaternion.identity, Vector3.one, BaseQuad.Color, BaseQuad.Flags, "ModelBase", -1));
        idx[BaseQuad.Transform] = baseIdx;

        foreach (StretchSpatialIndex.Entry e in _stretches.All())
        {
            if (!_usedStretches.Contains(e.Stretch)) continue;
            int si = snapshot.Count;
            Transform st = e.Stretch.Transform;
            int pi = idx.TryGetValue(st.parent, out int fp) ? fp : baseIdx;

            snapshot.Add(new PrimitiveSnapshot(e.Stretch.Position, e.Stretch.Rotation, e.Stretch.Scale,
                st.localPosition, st.localRotation, st.localScale, e.Stretch.Color, e.Stretch.Flags, "Stretch", pi));
            idx[st] = si;
        }

        foreach (Primitive p in _parallelograms)
        {
            Transform pt = p.Transform;
            int pi = idx.TryGetValue(pt.parent, out int fp) ? fp : baseIdx;
            int pIdx = snapshot.Count;

            snapshot.Add(new PrimitiveSnapshot(p.Position, p.Rotation, p.Scale,
                pt.localPosition, pt.localRotation, pt.localScale, p.Color, p.Flags, "Parallelogram", pi));
            idx[pt] = pIdx;
        }

        foreach (ParallelogramPrimitive fb in _fallbackParallelograms)
        {
            Primitive b = fb.BasePrimitive;
            Transform bt = b.Transform;
            int bp = idx.TryGetValue(bt.parent, out int fbp) ? fbp : baseIdx;
            int bi = snapshot.Count;

            snapshot.Add(new PrimitiveSnapshot(b.Position, b.Rotation, b.Scale,
                bt.localPosition, bt.localRotation, bt.localScale, b.Color, b.Flags, "FallbackBase", bp));
            idx[bt] = bi;

            Primitive q = fb.QuadPrimitive;
            Transform qt = q.Transform;
            int qp = idx.TryGetValue(qt.parent, out int fqp) ? fqp : bi;

            snapshot.Add(new PrimitiveSnapshot(q.Position, q.Rotation, q.Scale,
                qt.localPosition, qt.localRotation, qt.localScale, q.Color, q.Flags, "FallbackQuad", qp));
        }

        return snapshot;
    }

    void AppendNativePrimitiveSnapshots(List<PrimitiveSnapshot> snapshot, Dictionary<Transform, int> idx, int baseIdx)
    {
        for (var i = 0; i < NativePrimitives.Count; i++)
        {
            Primitive nat = NativePrimitives[i];
            ModelPrimitive mod = ModelPrimitives[i];

            Transform nt = nat.Transform;
            int npi = idx.TryGetValue(nt.parent, out int fn) ? fn : baseIdx;

            snapshot.Add(new PrimitiveSnapshot(nat.Position, nat.Rotation, nat.Scale,
                nt.localPosition, nt.localRotation, nt.localScale, nat.Color, nat.Flags, "Native", npi, mod.Type));
        }
    }
}