using AdminToys;
using Exiled.API.Features.Toys;
using TriangleScpSl.Core.Models.ApproximateModel;
using TriangleScpSl.Core.NGons;
using TriangleScpSl.Core.Triangulation.Parallelogram;
using TriangleScpSl.Core.Triangulation.Triangle;
using UnityEngine;

namespace TriangleScpSl.Core.Models.HierarchicalModel;

/// <summary>
///     V3 model that extends ApproximateModel's stretch-clustering with hierarchical
///     parenting: visible parallelograms can serve as parents for other visible
///     parallelograms, eliminating the need for separate invisible stretch primitives.
///     This produces deeper transform trees but significantly fewer total primitives.
/// </summary>
public partial class HierarchicalModel
    : ModelBase
{
    readonly float _absoluteToleranceUnits;
    readonly List<ModelTriangle> _localTriangles = [];
    readonly List<ModelParallelogram> _localParallelograms = [];

    // Built geometry
    readonly List<Primitive> _parallelograms = [];
    readonly List<ParallelogramPrimitive> _fallbackParallelograms = [];
    readonly List<ParallelogramSnapshot> _parallelogramSnapshots = [];

    // Hierarchical parenting data: tracks which parallelograms are parented to other parallelograms
    // Key: child parallelogram index in _parallelograms, Value: parent parallelogram index
    readonly Dictionary<int, int> _hierarchicalParents = new();

    // Tracks stretches that became unused after hierarchical reparenting
    readonly HashSet<Primitive> _usedStretches = new();

    // Stretch index (same as V2, used for initial solve)
    StretchSpatialIndex _stretches;

    HierarchicalModel
    (
        Vector3 worldPosition,
        PrimitiveFlags flags,
        float absoluteToleranceUnits,
        float scale,
        bool invertWinding)
        : base(worldPosition, flags, scale, invertWinding)
    {
        _absoluteToleranceUnits = absoluteToleranceUnits;
        _stretches = new StretchSpatialIndex(0.05f, 0.1f);
    }

    public HierarchicalModel
    (
        IReadOnlyList<ModelTriangle> triangles,
        Vector3 worldPosition,
        PrimitiveFlags flags = PrimitiveFlags.Visible,
        float absoluteToleranceUnits = 0.001f,
        float scale = 1f,
        bool invertWinding = false,
        bool buildImmediately = true)
        : this(worldPosition, flags, absoluteToleranceUnits, scale, invertWinding)
    {
        if (triangles.Count == 0)
            return;

        foreach (ModelTriangle tri in triangles)
            _localTriangles.Add(new ModelTriangle(tri.P1, tri.P2, tri.P3, tri.Color));

        InitStretchIndex();

        if (buildImmediately)
            BuildTriangles(flags);
    }

    public HierarchicalModel
    (
        IReadOnlyList<ModelParallelogram> parallelograms,
        Vector3 worldPosition,
        PrimitiveFlags flags = PrimitiveFlags.Visible,
        float absoluteToleranceUnits = 0.001f,
        float scale = 1f,
        bool invertWinding = false,
        bool buildImmediately = true)
        : this(worldPosition, flags, absoluteToleranceUnits, scale, invertWinding)
    {
        if (parallelograms.Count == 0)
            return;

        foreach (ModelParallelogram p in parallelograms)
            _localParallelograms.Add(p);

        InitStretchIndex();

        if (buildImmediately)
            BuildTriangles(flags);
    }

    public HierarchicalModel
    (
        IReadOnlyList<ModelParallelogram> parallelograms,
        IReadOnlyList<ModelPrimitive> primitives,
        Vector3 worldPosition,
        PrimitiveFlags flags = PrimitiveFlags.Visible,
        float absoluteToleranceUnits = 0.001f,
        float scale = 1f,
        bool invertWinding = false,
        bool buildImmediately = true)
        : this(worldPosition, flags, absoluteToleranceUnits, scale, invertWinding)
    {
        foreach (ModelParallelogram p in parallelograms)
            _localParallelograms.Add(p);

        foreach (ModelPrimitive primitive in primitives)
            ModelPrimitives.Add(primitive);

        if (parallelograms.Count == 0 && primitives.Count == 0)
            return;

        InitStretchIndex();

        if (buildImmediately)
            BuildTriangles(flags);
    }

    public override int ParallelogramCount => _localTriangles.Count + _localParallelograms.Count;

    /// <summary>
    ///     Total spawned primitives. In V3, unused stretches are destroyed after reparenting,
    ///     so this count is lower than V2 for the same geometry.
    /// </summary>
    public override int PrimitiveCount => IsDestroyedValue
        ? 0
        : _usedStretches.Count + _parallelograms.Count + _fallbackParallelograms.Count * 2 + NativePrimitives.Count + NativePrimitiveBases.Count + 1;

    public int ReparentedCount => _hierarchicalParents.Count;

    public int StretchesSaved => _stretches.Count - _usedStretches.Count;

    public override Color Color
    {
        set
        {
            if (IsDestroyedValue) return;

            foreach (Primitive parallelogram in _parallelograms) parallelogram.Color = value;
            foreach (ParallelogramPrimitive parallelogram in _fallbackParallelograms) parallelogram.Color = value;
            foreach (ModelParallelogram p in _localParallelograms) p.Color = value;
            foreach (Primitive native in NativePrimitives) native.Color = value;

            for (var i = 0; i < _parallelogramSnapshots.Count; i++)
            {
                ParallelogramSnapshot snapshot = _parallelogramSnapshots[i];

                _parallelogramSnapshots[i]
                    = new ParallelogramSnapshot(snapshot.VUp, snapshot.VLeft, snapshot.Center, value, snapshot.Flags, snapshot.IsFallback);
            }
        }
    }

    public override PrimitiveFlags Flags
    {
        get => FlagsValue;
        set
        {
            if (IsDestroyedValue) return;

            FlagsValue = value;

            foreach (Primitive parallelogram in _parallelograms) parallelogram.Flags = value;
            foreach (ParallelogramPrimitive parallelogram in _fallbackParallelograms) parallelogram.Flags = value;
            foreach (Primitive native in NativePrimitives) native.Flags = value;

            for (var i = 0; i < _parallelogramSnapshots.Count; i++)
            {
                ParallelogramSnapshot snapshot = _parallelogramSnapshots[i];

                _parallelogramSnapshots[i]
                    = new ParallelogramSnapshot(snapshot.VUp, snapshot.VLeft, snapshot.Center, snapshot.Color, value, snapshot.IsFallback);
            }
        }
    }

    public override string ProjectMerDefaultName => "HierarchicalSpace";

    void InitStretchIndex()
    {
        float maxSize = ComputeMaxParallelogramSize();

        _stretches = new StretchSpatialIndex(
            0.05f,
            _absoluteToleranceUnits / maxSize * 2f);
    }

    public static HierarchicalModel Create
    (
        IReadOnlyList<ModelTriangle> triangles,
        Vector3 worldPosition,
        PrimitiveFlags flags = PrimitiveFlags.Visible,
        float absoluteToleranceUnits = 0.001f,
        float scale = 1f,
        bool invertWinding = false)
        => new(triangles, worldPosition, flags, absoluteToleranceUnits, scale, invertWinding);

    public static HierarchicalModel CreateDeferred
    (
        IReadOnlyList<ModelTriangle> triangles,
        Vector3 worldPosition,
        PrimitiveFlags flags = PrimitiveFlags.Visible,
        float absoluteToleranceUnits = 0.001f,
        float scale = 1f,
        bool invertWinding = false)
        => new(triangles, worldPosition, flags, absoluteToleranceUnits, scale, invertWinding, false);

    public static HierarchicalModel Create
    (
        IReadOnlyList<ModelParallelogram> parallelograms,
        Vector3 worldPosition,
        PrimitiveFlags flags = PrimitiveFlags.Visible,
        float absoluteToleranceUnits = 0.001f,
        float scale = 1f,
        bool invertWinding = false)
        => new(parallelograms, worldPosition, flags, absoluteToleranceUnits, scale, invertWinding);

    public static HierarchicalModel CreateDeferred
    (
        IReadOnlyList<ModelParallelogram> parallelograms,
        Vector3 worldPosition,
        PrimitiveFlags flags = PrimitiveFlags.Visible,
        float absoluteToleranceUnits = 0.001f,
        float scale = 1f,
        bool invertWinding = false)
        => new(parallelograms, worldPosition, flags, absoluteToleranceUnits, scale, invertWinding, false);

    public static HierarchicalModel Create
    (
        IReadOnlyList<ModelParallelogram> parallelograms,
        IReadOnlyList<ModelPrimitive> primitives,
        Vector3 worldPosition,
        PrimitiveFlags flags = PrimitiveFlags.Visible,
        float absoluteToleranceUnits = 0.001f,
        float scale = 1f,
        bool invertWinding = false)
        => new(parallelograms, primitives, worldPosition, flags, absoluteToleranceUnits, scale, invertWinding);

    public static HierarchicalModel CreateDeferred
    (
        IReadOnlyList<ModelParallelogram> parallelograms,
        IReadOnlyList<ModelPrimitive> primitives,
        Vector3 worldPosition,
        PrimitiveFlags flags = PrimitiveFlags.Visible,
        float absoluteToleranceUnits = 0.001f,
        float scale = 1f,
        bool invertWinding = false)
        => new(parallelograms, primitives, worldPosition, flags, absoluteToleranceUnits, scale, invertWinding, false);

    public override void Destroy()
    {
        if (IsDestroyedValue) return;

        IsDestroyedValue = true;

        ClearAllPrimitives();
        DestroyNativePrimitives();
        _stretches.Clear();
        _parallelograms.Clear();
        _fallbackParallelograms.Clear();
        _parallelogramSnapshots.Clear();
        _localTriangles.Clear();
        _localParallelograms.Clear();
        _hierarchicalParents.Clear();
        _usedStretches.Clear();
        ModelPrimitives.Clear();
        BaseQuad.Destroy();
    }

    void ClearAllPrimitives()
    {
        foreach (StretchSpatialIndex.Entry entry in _stretches.All())
            entry.Stretch.Destroy();

        foreach (Primitive parallelogram in _parallelograms)
            parallelogram.Destroy();

        foreach (ParallelogramPrimitive parallelogram in _fallbackParallelograms)
            parallelogram.Destroy();
    }

    public sealed class ParallelogramSnapshot(Vector3 vUp, Vector3 vLeft, Vector3 center, Color color, PrimitiveFlags flags, bool isFallback)
    {
        public Vector3 VUp { get; } = vUp;
        public Vector3 VLeft { get; } = vLeft;
        public Vector3 Center { get; } = center;
        public Color Color { get; } = color;
        public PrimitiveFlags Flags { get; } = flags;
        public bool IsFallback { get; } = isFallback;
    }

    public sealed class PrimitiveSnapshot
    (
        Vector3 position,
        Quaternion rotation,
        Vector3 scale,
        Vector3 localPosition,
        Quaternion localRotation,
        Vector3 localScale,
        Color color,
        PrimitiveFlags flags,
        string kind,
        int parentIndex,
        PrimitiveType primitiveType = PrimitiveType.Quad)
    {
        public Vector3 Position { get; } = position;
        public Quaternion Rotation { get; } = rotation;
        public Vector3 Scale { get; } = scale;
        public Vector3 LocalPosition { get; } = localPosition;
        public Quaternion LocalRotation { get; } = localRotation;
        public Vector3 LocalScale { get; } = localScale;
        public Color Color { get; } = color;
        public PrimitiveFlags Flags { get; } = flags;
        public string Kind { get; } = kind;
        public int ParentIndex { get; } = parentIndex;
        public PrimitiveType PrimitiveType { get; } = primitiveType;
    }
}