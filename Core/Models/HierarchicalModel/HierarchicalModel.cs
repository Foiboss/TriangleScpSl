using AdminToys;
using Exiled.API.Features.Toys;
using TriangleScpSl.Core.Decomposition.NGonDecomposition;
using TriangleScpSl.Core.Models.ApproximateModel;
using TriangleScpSl.Core.Primitives.Parallelogram;
using TriangleScpSl.Core.Primitives.Triangle;
using UnityEngine;

namespace TriangleScpSl.Core.Models.HierarchicalModel;

/// <summary>
///     V3 model renderer. Two-phase approach:
///     Phase 1 (during build):
///     For each new parallelogram, try to parent it under an already-built visible quad.
///     If none works, fall back to V2 stretch clustering.
///     Phase 2 (post-build sweeps):
///     Iteratively scan stretch-children and try to move them under visible quads.
///     Each sweep only tests quads that became newly available in the previous sweep. Controlled by optimizationPasses parameter.
///     No primitives are ever destroyed+recreated - only created once in final position.
/// </summary>
public partial class HierarchicalModel
    : ModelBase
{
    readonly float _absoluteToleranceUnits;
    readonly int _optimizationPasses;
    readonly List<ModelTriangle> _localTriangles = [];
    readonly List<ModelParallelogram> _localParallelograms = [];

    // Built geometry
    readonly List<Primitive> _parallelograms = [];
    readonly List<ParallelogramPrimitive> _fallbackParallelograms = [];
    readonly List<ParallelogramSnapshot> _parallelogramSnapshots = [];

    // Per-parallelogram build info (1:1 with _parallelograms)
    readonly List<QuadBuildInfo> _quadBuildInfos = [];

    // Key: child index, Value: parent index
    readonly Dictionary<int, int> _hierarchicalParents = new();
    readonly Dictionary<int, int> _hierarchyDepths = new();

    readonly HashSet<Primitive> _usedStretches = [];

    StretchSpatialIndex _stretches;

    HierarchicalModel
    (
        Vector3 worldPosition,
        PrimitiveFlags flags,
        float absoluteToleranceUnits,
        float scale,
        bool invertWinding,
        int optimizationPasses)
        : base(worldPosition, flags, scale, invertWinding)
    {
        _absoluteToleranceUnits = absoluteToleranceUnits;
        _optimizationPasses = optimizationPasses;
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
        bool buildImmediately = false,
        int optimizationPasses = 3)
        : this(worldPosition, flags, absoluteToleranceUnits, scale, invertWinding, optimizationPasses)
    {
        if (triangles.Count == 0) return;

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
        bool buildImmediately = false,
        int optimizationPasses = 3)
        : this(worldPosition, flags, absoluteToleranceUnits, scale, invertWinding, optimizationPasses)
    {
        if (parallelograms.Count == 0) return;

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
        bool buildImmediately = false,
        int optimizationPasses = 3)
        : this(worldPosition, flags, absoluteToleranceUnits, scale, invertWinding, optimizationPasses)
    {
        foreach (ModelParallelogram p in parallelograms)
            _localParallelograms.Add(p);

        foreach (ModelPrimitive primitive in primitives)
            ModelPrimitives.Add(primitive);

        if (parallelograms.Count == 0 && primitives.Count == 0) return;

        InitStretchIndex();

        if (buildImmediately)
            BuildTriangles(flags);
    }

    public override int ParallelogramCount => _localTriangles.Count + _localParallelograms.Count;

    public override int PrimitiveCount => IsDestroyedValue
        ? 0
        : _usedStretches.Count + _parallelograms.Count + _fallbackParallelograms.Count * 2
        + NativePrimitives.Count + NativePrimitiveBaseCount + 1;

    public int ReparentedCount { get; private set; }

    public int StretchesSaved => ReparentedCount + (_stretches.Count - _usedStretches.Count);

    public override Color Color
    {
        set
        {
            if (IsDestroyedValue) return;

            foreach (Primitive p in _parallelograms) p.Color = value;
            foreach (ParallelogramPrimitive p in _fallbackParallelograms) p.Color = value;
            foreach (ModelParallelogram p in _localParallelograms) p.Color = value;
            foreach (Primitive n in NativePrimitives) n.Color = value;

            for (var i = 0; i < _parallelogramSnapshots.Count; i++)
            {
                ParallelogramSnapshot s = _parallelogramSnapshots[i];

                _parallelogramSnapshots[i] = new ParallelogramSnapshot(
                    s.VUp, s.VLeft, s.Center, value, s.Flags, s.IsFallback);
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

            foreach (Primitive p in _parallelograms) p.Flags = value;
            foreach (ParallelogramPrimitive p in _fallbackParallelograms) p.Flags = value;
            foreach (Primitive n in NativePrimitives) n.Flags = value;

            for (var i = 0; i < _parallelogramSnapshots.Count; i++)
            {
                ParallelogramSnapshot s = _parallelogramSnapshots[i];

                _parallelogramSnapshots[i] = new ParallelogramSnapshot(
                    s.VUp, s.VLeft, s.Center, s.Color, value, s.IsFallback);
            }
        }
    }

    public override string ProjectMerDefaultName => "HierarchicalSpace";

    void InitStretchIndex()
    {
        float maxSize = ComputeMaxParallelogramSize();
        _stretches = new StretchSpatialIndex(0.05f, _absoluteToleranceUnits / maxSize * 2f);
    }

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
        _quadBuildInfos.Clear();
        _localTriangles.Clear();
        _localParallelograms.Clear();
        _hierarchicalParents.Clear();
        _hierarchyDepths.Clear();
        _usedStretches.Clear();
        ModelPrimitives.Clear();
        BaseQuad.Destroy();
    }

    void ClearAllPrimitives()
    {
        foreach (StretchSpatialIndex.Entry entry in _stretches.All())
            SafeDestroy(entry.Stretch);

        foreach (Primitive parallelogram in _parallelograms)
            SafeDestroy(parallelogram);

        foreach (ParallelogramPrimitive parallelogram in _fallbackParallelograms)
            parallelogram.Destroy();
    }

    static void SafeDestroy(Primitive? primitive)
    {
        if (primitive == null) return;

        try
        {
            Transform t = primitive.Transform;
            if (t == null) return;
        }
        catch (NullReferenceException)
        {
            return;
        }

        primitive.Destroy();
    }

    readonly struct QuadBuildInfo(Vector3 vLeft, Vector3 vUp, Vector3 center, Primitive? stretch)
    {
        public readonly Vector3 VLeft = vLeft;
        public readonly Vector3 VUp = vUp;
        public readonly Vector3 Center = center;
        public readonly Primitive? Stretch = stretch;
    }

    public sealed class ParallelogramSnapshot
    (
        Vector3 vUp,
        Vector3 vLeft,
        Vector3 center,
        Color color,
        PrimitiveFlags flags,
        bool isFallback)
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