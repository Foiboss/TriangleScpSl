using System.Collections;
using Exiled.API.Features.Toys;
using TriangleScpSl.Core.ModelFactory;
using TriangleScpSl.Core.ProjectMerExport;
using TriangleScpSl.Core.Triangulation.Parallelogram;
using TriangleScpSl.Core.Triangulation.Triangle;
using UnityEngine;

namespace TriangleScpSl.Core.Models.ExactModel;

// A 3-D mesh loaded from triangulated file data (STL/OBJ).
// The model stores local-space triangles and rebuilds ParallelogramPrimitive instances
// when its transform changes.
public class ExactModel
    : ModelBase
{
    readonly Primitive _baseQuad;
    readonly List<ModelParallelogram> _modelParallelograms = [];
    readonly List<ParallelogramPrimitive> _parallelograms = [];
    readonly bool _invertWinding;
    AdminToys.PrimitiveFlags _flags;

    Vector3 _position;
    Quaternion _rotation;
    Vector3 _scale;
    bool _isDestroyed;

    public ExactModel
    (
        IReadOnlyList<ModelTriangle> triangles,
        Vector3 worldPosition,
        AdminToys.PrimitiveFlags flags = AdminToys.PrimitiveFlags.Visible,
        float scale = 1f,
        bool invertWinding = false,
        bool buildImmediately = true)
    {
        _position = worldPosition;
        _rotation = Quaternion.identity;
        _scale = Vector3.one * scale;
        _invertWinding = invertWinding;
        _flags = flags;

        _baseQuad = Primitive.Create(
            PrimitiveType.Quad,
            AdminToys.PrimitiveFlags.None,
            _position,
            Vector3.zero,
            _scale,
            true,
            Color.clear);

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
        bool buildImmediately = true)
    {
        _position = worldPosition;
        _rotation = Quaternion.identity;
        _scale = Vector3.one * scale;
        _invertWinding = invertWinding;
        _flags = flags;

        _baseQuad = Primitive.Create(
            PrimitiveType.Quad,
            AdminToys.PrimitiveFlags.None,
            _position,
            Vector3.zero,
            _scale,
            true,
            Color.clear);

        if (parallelograms.Count == 0)
            return;

        foreach (ModelParallelogram parallelogram in parallelograms)
            _modelParallelograms.Add(parallelogram);

        if (buildImmediately)
            BuildTriangles(flags);
    }

    public override int ParallelogramCount => _modelParallelograms.Count;
    public override int QuadCount => _isDestroyed ? 0 : ParallelogramCount * 2 + 1; // +1 for model base quad

    public override Vector3 Position
    {
        get => _position;
        set
        {
            if (_isDestroyed)
                return;

            _position = value;
            _baseQuad.Position = value;
        }
    }

    public override Quaternion Rotation
    {
        get => _rotation;
        set
        {
            if (_isDestroyed)
                return;

            _rotation = value;
            _baseQuad.Rotation = value;
        }
    }

    public override Vector3 Scale
    {
        get => _scale;
        set
        {
            if (_isDestroyed)
                return;

            _scale = value;
            _baseQuad.Scale = value;
        }
    }

    public override Transform Transform => _baseQuad.Transform;

    public override Color Color
    {
        set
        {
            if (_isDestroyed)
                return;

            foreach (ModelParallelogram modelParallelogram in _modelParallelograms)
                modelParallelogram.Color = value;

            foreach (ParallelogramPrimitive parallelogram in _parallelograms)
                parallelogram.Color = value;
        }
    }

    public override AdminToys.PrimitiveFlags Flags
    {
        get => _flags;
        set
        {
            if (_isDestroyed)
                return;

            _flags = value;

            foreach (ParallelogramPrimitive parallelogram in _parallelograms)
                parallelogram.Flags = value;
        }
    }

    public override string ProjectMerDefaultName => "TriangulatedModel";

    public override Vector3 TransformPoint(Vector3 localPoint)
        => _position + _rotation * Vector3.Scale(localPoint, _scale);

    public override Vector3 InverseTransformPoint(Vector3 worldPoint)
    {
        Vector3 local = Quaternion.Inverse(_rotation) * (worldPoint - _position);

        return new Vector3(
            _scale.x != 0f ? local.x / _scale.x : 0f,
            _scale.y != 0f ? local.y / _scale.y : 0f,
            _scale.z != 0f ? local.z / _scale.z : 0f);
    }

    public static ExactModel Create
    (
        IReadOnlyList<ModelTriangle> triangles,
        Vector3 worldPosition,
        AdminToys.PrimitiveFlags flags = AdminToys.PrimitiveFlags.Visible,
        float scale = 1f,
        bool invertWinding = false)
        => new(triangles, worldPosition, flags, scale, invertWinding);

    public static ExactModel CreateDeferred
    (
        IReadOnlyList<ModelTriangle> triangles,
        Vector3 worldPosition,
        AdminToys.PrimitiveFlags flags = AdminToys.PrimitiveFlags.Visible,
        float scale = 1f,
        bool invertWinding = false)
        => new(triangles, worldPosition, flags, scale, invertWinding, false);
    
    public static ExactModel Create
    (
        IReadOnlyList<ModelParallelogram> parallelograms,
        Vector3 worldPosition,
        AdminToys.PrimitiveFlags flags = AdminToys.PrimitiveFlags.Visible,
        float scale = 1f,
        bool invertWinding = false)
        => new(parallelograms, worldPosition, flags, scale, invertWinding);

    public static ExactModel CreateDeferred
    (
        IReadOnlyList<ModelParallelogram> parallelograms,
        Vector3 worldPosition,
        AdminToys.PrimitiveFlags flags = AdminToys.PrimitiveFlags.Visible,
        float scale = 1f,
        bool invertWinding = false)
        => new(parallelograms, worldPosition, flags, scale, invertWinding, false);

    public override IEnumerator BuildTrianglesCoroutine(AdminToys.PrimitiveFlags flags, int trianglesPerFrame)
    {
        if (_isDestroyed)
            yield break;

        _flags = flags;
        trianglesPerFrame = Mathf.Max(1, trianglesPerFrame);

        foreach (ParallelogramPrimitive parallelogram in _parallelograms)
            parallelogram.Destroy();

        _parallelograms.Clear();
        var processed = 0;

        foreach (ModelParallelogram modelParallelogram in _modelParallelograms)
        {
            if (_isDestroyed)
                yield break;

            Vector3 vUp = _invertWinding ? -modelParallelogram.VUp : modelParallelogram.VUp;
            Vector3 vLeft = modelParallelogram.VLeft;
            Vector3 center = TransformPoint(modelParallelogram.Center);

            _parallelograms.Add(ParallelogramPrimitive.Create(
                vUp, 
                vLeft, 
                center, 
                modelParallelogram.Color, 
                flags));
            processed++;

            if (processed >= trianglesPerFrame)
            {
                processed = 0;
                yield return null;
            }
        }
    }

    public override void Destroy()
    {
        if (_isDestroyed)
            return;

        _isDestroyed = true;

        foreach (ParallelogramPrimitive parallelogram in _parallelograms)
            parallelogram.Destroy();

        _parallelograms.Clear();
        _modelParallelograms.Clear();
        _baseQuad.Destroy();
    }

    public override IReadOnlyList<ProjectMerBlock> GetProjectMerBlocks
    (
        int modelObjectId,
        int startObjectId,
        Func<Vector3, Vector3> inverseTransformPoint,
        Quaternion modelRotation)
    {
        if (_isDestroyed || _parallelograms.Count == 0)
            return [];

        List<ProjectMerBlock> blocks = new(_parallelograms.Count * 2);
        int objectId = startObjectId;

        for (var parallelogramIndex = 0; parallelogramIndex < _parallelograms.Count; parallelogramIndex++)
        {
            ParallelogramPrimitive parallelogram = _parallelograms[parallelogramIndex];
            Primitive basePrimitive = parallelogram.BasePrimitive;
            Primitive quadPrimitive = parallelogram.QuadPrimitive;

            int baseId = objectId++;
            int quadId = objectId++;

            blocks.Add(new ProjectMerBlock
            {
                Name = $"(P.{parallelogramIndex + 1}).Base",
                ObjectId = baseId,
                ParentId = modelObjectId,
                Position = inverseTransformPoint(basePrimitive.Position),
                Rotation = (Quaternion.Inverse(modelRotation) * basePrimitive.Rotation).eulerAngles,
                Scale = basePrimitive.Scale,
                BlockType = 0,
                IsPrimitive = false,
                Static = false,
            });

            blocks.Add(new ProjectMerBlock
            {
                Name = $"(P.{parallelogramIndex + 1})",
                ObjectId = quadId,
                ParentId = baseId,
                Position = quadPrimitive.Transform.localPosition,
                Rotation = quadPrimitive.Transform.localRotation.eulerAngles,
                Scale = quadPrimitive.Transform.localScale,
                BlockType = 1,
                IsPrimitive = true,
                PrimitiveType = (int)PrimitiveType.Quad,
                PrimitiveColor = parallelogram.Color,
                PrimitiveFlags = parallelogram.Flags,
                Static = false,
            });
        }

        return blocks;
    }

    void BuildTriangles(AdminToys.PrimitiveFlags flags)
    {
        _flags = flags;
        _parallelograms.Clear();

        foreach (ModelParallelogram modelParallelogram in _modelParallelograms)
        {
            Vector3 vUp = _invertWinding ? -modelParallelogram.VUp : modelParallelogram.VUp;
            Vector3 vLeft = modelParallelogram.VLeft;
            Vector3 center = TransformPoint(modelParallelogram.Center);

            _parallelograms.Add(ParallelogramPrimitive.Create(
                vUp, 
                vLeft, 
                center, 
                modelParallelogram.Color, 
                flags));
        }
    }

    (ModelParallelogram para1, ModelParallelogram para2, ModelParallelogram para3) GetParallelograms(ModelTriangle localTriangle, Color color)
    {
        // Keep triangle data in model-local space; world transform is applied at build time.
        Vector3 p1 = localTriangle.P1;
        Vector3 p2 = localTriangle.P2;
        Vector3 p3 = localTriangle.P3;

        if (_invertWinding)
            (p2, p3) = (p3, p2);

        Vector3[][] data = TriangleParallelogramBuilder.GetParallelogramsInfo(p1, p2, p3);

        var para1 = new ModelParallelogram { VLeft = data[0][0], VUp = data[0][1], Center = data[0][2], Color = color };
        var para2 = new ModelParallelogram { VLeft = data[1][0], VUp = data[1][1], Center = data[1][2], Color = color };
        var para3 = new ModelParallelogram { VLeft = data[2][0], VUp = data[2][1], Center = data[2][2], Color = color };

        return (para1, para2, para3);
    }
}