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

    /// <summary>
    ///     Full primitive snapshot, including natives, ordered so that every entry
    ///     appears after its parent (see <see cref="OrderByHierarchy" />).
    /// </summary>
    public IReadOnlyList<PrimitiveSnapshot> GetPrimitiveSnapshot()
    {
        if (IsDestroyedValue) return [];
        return OrderByHierarchy(CollectPrimitiveNodes(true));
    }

    /// <summary>
    ///     Primitive snapshot without natives, used by export code which computes native
    ///     primitive blocks directly from <see cref="ModelPrimitive" /> data. Entries are
    ///     ordered so that every entry appears after its parent (see <see cref="OrderByHierarchy" />),
    ///     since consumers assign sequential object ids while walking the list in order.
    /// </summary>
    internal IReadOnlyList<PrimitiveSnapshot> GetPrimitiveSnapshotWithoutNatives()
    {
        if (IsDestroyedValue) return [];
        return OrderByHierarchy(CollectPrimitiveNodes(false));
    }

    List<PendingPrimitive> CollectPrimitiveNodes(bool includeNatives)
    {
        int cap = _usedStretches.Count + _parallelograms.Count + _fallbackParallelograms.Count * 2 + 1
            + (includeNatives ? NativePrimitives.Count : 0);
        var nodes = new List<PendingPrimitive>(cap) { PendingPrimitive.Root(BaseQuad) };

        foreach (StretchSpatialIndex.Entry e in _stretches.All())
        {
            if (!_usedStretches.Contains(e.Stretch)) continue;
            nodes.Add(new PendingPrimitive(e.Stretch, "Stretch"));
        }

        foreach (Primitive p in _parallelograms)
            nodes.Add(new PendingPrimitive(p, "Parallelogram"));

        foreach (ParallelogramPrimitive fb in _fallbackParallelograms)
        {
            nodes.Add(new PendingPrimitive(fb.BasePrimitive, "FallbackBase"));
            nodes.Add(new PendingPrimitive(fb.QuadPrimitive, "FallbackQuad"));
        }

        if (includeNatives)
            for (var i = 0; i < NativePrimitives.Count; i++)
                nodes.Add(new PendingPrimitive(NativePrimitives[i], "Native", ModelPrimitives[i].Type));

        return nodes;
    }

    /// <summary>
    ///     Reorders collected nodes into a valid build order: every node is emitted only after
    ///     its actual Unity transform parent has already been emitted. This is required because
    ///     source collections (spatial index enumeration, post-hoc reparenting sweeps such as
    ///     <see cref="RunOptimizationSweeps" />) do not themselves guarantee that a primitive's
    ///     real parent was already visited when the primitive is enumerated - resolving
    ///     ParentIndex against a partially-built map (as opposed to the complete map built here)
    ///     would silently drop the real parent link. Consumers (e.g. the ProjectMer exporter) rely
    ///     on parents always appearing before their children in the emitted list.
    /// </summary>
    static List<PrimitiveSnapshot> OrderByHierarchy(List<PendingPrimitive> nodes)
    {
        int n = nodes.Count;
        var indexByTransform = new Dictionary<Transform, int>(n);
        for (var i = 0; i < n; i++) indexByTransform[nodes[i].Transform] = i;

        var graphParent = new int[n];
        var children = new List<int>[n];
        for (var i = 0; i < n; i++) children[i] = [];

        for (var i = 1; i < n; i++)
        {
            Transform parent = nodes[i].Transform.parent;
            int parentIdx = parent != null && indexByTransform.TryGetValue(parent, out int pIdx) ? pIdx : 0;
            graphParent[i] = parentIdx;
            children[parentIdx].Add(i);
        }

        var snapshot = new List<PrimitiveSnapshot>(n);
        var finalIndexOf = new int[n];
        var stack = new Stack<int>();
        stack.Push(0);

        while (stack.Count > 0)
        {
            int i = stack.Pop();
            PendingPrimitive node = nodes[i];
            finalIndexOf[i] = snapshot.Count;

            bool isRoot = i == 0;
            Vector3 localPos = isRoot ? Vector3.zero : node.LocalPosition;
            Quaternion localRot = isRoot ? Quaternion.identity : node.LocalRotation;
            Vector3 localScale = isRoot ? Vector3.one : node.LocalScale;
            int parentFinalIndex = isRoot ? -1 : finalIndexOf[graphParent[i]];

            snapshot.Add(new PrimitiveSnapshot(node.Position, node.Rotation, node.Scale,
                localPos, localRot, localScale, node.Color, node.Flags, node.Kind,
                parentFinalIndex, node.PrimitiveType));

            List<int> kids = children[i];
            for (int k = kids.Count - 1; k >= 0; k--)
                stack.Push(kids[k]);
        }

        return snapshot;
    }

    readonly struct PendingPrimitive
    {
        public readonly Transform Transform;
        public readonly Vector3 Position;
        public readonly Quaternion Rotation;
        public readonly Vector3 Scale;
        public readonly Vector3 LocalPosition;
        public readonly Quaternion LocalRotation;
        public readonly Vector3 LocalScale;
        public readonly Color Color;
        public readonly PrimitiveFlags Flags;
        public readonly string Kind;
        public readonly PrimitiveType PrimitiveType;

        public PendingPrimitive(Primitive primitive, string kind, PrimitiveType primitiveType = PrimitiveType.Quad)
        {
            Transform = primitive.Transform;
            Position = primitive.Position;
            Rotation = primitive.Rotation;
            Scale = primitive.Scale;
            LocalPosition = Transform.localPosition;
            LocalRotation = Transform.localRotation;
            LocalScale = Transform.localScale;
            Color = primitive.Color;
            Flags = primitive.Flags;
            Kind = kind;
            PrimitiveType = primitiveType;
        }

        public static PendingPrimitive Root(Primitive baseQuad) => new(baseQuad, "ModelBase");
    }
}
