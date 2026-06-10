using Exiled.API.Features.Toys;
using TriangleScpSl.Core.Decomposition.NGonDecomposition;
using TriangleScpSl.Core.Primitives.Parallelogram;
using TriangleScpSl.Core.Primitives.Triangle;
using UnityEngine;

namespace TriangleScpSl.Core.Models.ExactModel;

public partial class ExactModel
    : ModelBase
{
    readonly List<ModelParallelogram> _modelParallelograms = [];
    readonly List<ParallelogramPrimitive> _parallelograms = [];
    readonly List<Primitive> _rectangles = [];

    ExactModel
    (
        Vector3 worldPosition,
        AdminToys.PrimitiveFlags flags,
        float scale,
        bool invertWinding)
        : base(worldPosition, flags, scale, invertWinding) { }

    public ExactModel
    (
        IReadOnlyList<ModelTriangle> triangles,
        Vector3 worldPosition,
        AdminToys.PrimitiveFlags flags = AdminToys.PrimitiveFlags.Visible,
        float scale = 1f,
        bool invertWinding = false,
        bool buildImmediately = false)
        : this(worldPosition, flags, scale, invertWinding)
    {
        if (triangles.Count == 0)
            return;

        foreach (ModelTriangle tri in triangles)
        {
            (ModelParallelogram para1, ModelParallelogram para2, ModelParallelogram para3) = GetParallelograms(tri, tri.Color);
            _modelParallelograms.Add(para1);
            _modelParallelograms.Add(para2);
            _modelParallelograms.Add(para3);
        }

        if (buildImmediately)
            BuildTriangles(flags);
    }

    public ExactModel
    (
        IReadOnlyList<ModelParallelogram> parallelograms,
        Vector3 worldPosition,
        AdminToys.PrimitiveFlags flags = AdminToys.PrimitiveFlags.Visible,
        float scale = 1f,
        bool invertWinding = false,
        bool buildImmediately = false)
        : this(worldPosition, flags, scale, invertWinding)
    {
        if (parallelograms.Count == 0)
            return;

        foreach (ModelParallelogram parallelogram in parallelograms)
            _modelParallelograms.Add(parallelogram);

        if (buildImmediately)
            BuildTriangles(flags);
    }

    public ExactModel
    (
        IReadOnlyList<ModelParallelogram> parallelograms,
        IReadOnlyList<ModelPrimitive> primitives,
        Vector3 worldPosition,
        AdminToys.PrimitiveFlags flags = AdminToys.PrimitiveFlags.Visible,
        float scale = 1f,
        bool invertWinding = false,
        bool buildImmediately = false)
        : this(worldPosition, flags, scale, invertWinding)
    {
        foreach (ModelParallelogram parallelogram in parallelograms)
            _modelParallelograms.Add(parallelogram);

        foreach (ModelPrimitive primitive in primitives)
            ModelPrimitives.Add(primitive);

        if (buildImmediately)
            BuildTriangles(flags);
    }

    public override int ParallelogramCount => _modelParallelograms.Count;

    public override int PrimitiveCount => IsDestroyedValue
        ? 0
        : _parallelograms.Count * 2 + _rectangles.Count + NativePrimitives.Count + NativePrimitiveBases.Count + 1;

    public override Color Color
    {
        set
        {
            if (IsDestroyedValue)
                return;

            foreach (ModelParallelogram modelParallelogram in _modelParallelograms)
                modelParallelogram.Color = value;

            foreach (ParallelogramPrimitive parallelogram in _parallelograms)
                parallelogram.Color = value;

            foreach (Primitive rect in _rectangles)
                rect.Color = value;

            foreach (Primitive native in NativePrimitives)
                native.Color = value;
        }
    }

    public override AdminToys.PrimitiveFlags Flags
    {
        get => FlagsValue;
        set
        {
            if (IsDestroyedValue)
                return;

            FlagsValue = value;

            foreach (ParallelogramPrimitive parallelogram in _parallelograms)
                parallelogram.Flags = value;

            foreach (Primitive rect in _rectangles)
                rect.Flags = value;

            foreach (Primitive native in NativePrimitives)
                native.Flags = value;
        }
    }

    public override string ProjectMerDefaultName => "TriangulatedModel";

    public override void Destroy()
    {
        if (IsDestroyedValue)
            return;

        IsDestroyedValue = true;

        foreach (ParallelogramPrimitive parallelogram in _parallelograms)
            parallelogram.Destroy();

        foreach (Primitive rect in _rectangles)
            rect.Destroy();

        _parallelograms.Clear();
        _rectangles.Clear();
        _modelParallelograms.Clear();

        DestroyNativePrimitives();
        ModelPrimitives.Clear();

        BaseQuad.Destroy();
    }
}