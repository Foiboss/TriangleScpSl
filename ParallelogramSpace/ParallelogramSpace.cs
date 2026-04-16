using AdminToys;
using Exiled.API.Features.Toys;
using TriangleScpSl.Core.TriangulatedModel;
using TriangleScpSl.Core.Triangulation.Triangle;
using UnityEngine;

namespace TriangleScpSl.ParallelogramSpace;

public class ParallelogramSpace
{
    readonly Primitive _baseQuad;
    readonly List<ModelTriangle> _localTriangles = [];
    readonly Dictionary<float, Primitive> _angleToStretch = [];
    readonly List<Primitive> _parallelograms = [];
    readonly bool _invertWinding;

    Vector3 _position;
    Quaternion _rotation;
    Vector3 _scale;
    bool _isDestroyed;

    public ParallelogramSpace
    (
        IReadOnlyList<ModelTriangle> triangles,
        Vector3 worldPosition,
        PrimitiveFlags flags = PrimitiveFlags.Visible,
        float scale = 1f,
        bool invertWinding = false)
    {
        _position = worldPosition;
        _rotation = Quaternion.identity;
        _scale = Vector3.one * scale;
        _invertWinding = invertWinding;

        _baseQuad = Primitive.Create(
            PrimitiveType.Quad,
            PrimitiveFlags.None,
            _position,
            Vector3.zero,
            _scale,
            true,
            Color.clear);

        if (triangles.Count == 0)
            return;

        Vector3 modelCenter = CalculateCenter(triangles);

        foreach (ModelTriangle tri in triangles)
            _localTriangles.Add(new ModelTriangle(tri.P1 - modelCenter, tri.P2 - modelCenter, tri.P3 - modelCenter, tri.Color));

        BuildTriangles(flags);
    }

    public int Count => _localTriangles.Count;
    public int QuadCount => _angleToStretch.Count + _parallelograms.Count + 1; // +1 for model base quad

    public Vector3 Position
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

    public Quaternion Rotation
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

    public Vector3 Scale
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

    public Transform Transform => _baseQuad.Transform;

    public Color Color
    {
        set
        {
            if (_isDestroyed)
                return;

            foreach (Primitive parallelograms in _parallelograms) parallelograms.Color = value;
        }
    }

    public PrimitiveFlags Flags
    {
        set
        {
            if (_isDestroyed)
                return;

            foreach (Primitive parallelograms in _parallelograms) parallelograms.Flags = value;
        }
    }

    public Vector3 TransformPoint(Vector3 localPoint)
        => _position + _rotation * Vector3.Scale(localPoint, _scale);

    public Vector3 InverseTransformPoint(Vector3 worldPoint)
    {
        Vector3 local = Quaternion.Inverse(_rotation) * (worldPoint - _position);

        return new Vector3(
            _scale.x != 0f ? local.x / _scale.x : 0f,
            _scale.y != 0f ? local.y / _scale.y : 0f,
            _scale.z != 0f ? local.z / _scale.z : 0f);
    }

    public static ParallelogramSpace Create
    (
        IReadOnlyList<ModelTriangle> triangles,
        Vector3 worldPosition,
        PrimitiveFlags flags = PrimitiveFlags.Visible,
        float scale = 1f,
        bool invertWinding = false)
        => new(triangles, worldPosition, flags, scale, invertWinding);

    public void Destroy()
    {
        if (_isDestroyed)
            return;

        _isDestroyed = true;

        foreach (KeyValuePair<float, Primitive> stretch in _angleToStretch)
            stretch.Value.Destroy();

        foreach (Primitive parallelogram in _parallelograms)
            parallelogram.Destroy();

        _angleToStretch.Clear();
        _parallelograms.Clear();
        _localTriangles.Clear();
        _baseQuad.Destroy();
    }

    void BuildTriangles(PrimitiveFlags flags)
    {
        foreach (KeyValuePair<float, Primitive> stretch in _angleToStretch)
            stretch.Value.Destroy();

        foreach (Primitive parallelogram in _parallelograms)
            parallelogram.Destroy();

        _angleToStretch.Clear();
        _parallelograms.Clear();

        foreach (ModelTriangle localTriangle in _localTriangles)
            CreateTriangle(localTriangle, flags);
    }

    void CreateTriangle(ModelTriangle localTriangle, PrimitiveFlags flags)
    {
        Vector3 p1 = TransformPoint(localTriangle.P1);
        Vector3 p2 = TransformPoint(localTriangle.P2);
        Vector3 p3 = TransformPoint(localTriangle.P3);

        if (_invertWinding)
            (p2, p3) = (p3, p2);

        Vector3[][] data = TriangleParallelogramBuilder.GetParallelogramsInfo(p1, p2, p3);
        CreateParallelogram(data[0][0], data[0][1], data[0][2], flags, localTriangle.Color);
        CreateParallelogram(data[1][0], data[1][1], data[1][2], flags, localTriangle.Color);
        CreateParallelogram(data[2][0], data[2][1], data[2][2], flags, localTriangle.Color);
    }

    void CreateParallelogram(Vector3 vLeft, Vector3 vUp, Vector3 center, PrimitiveFlags flags, Color color)
    {
        //todo:
    }

    static Vector3 CalculateCenter(IReadOnlyList<ModelTriangle> triangles)
    {
        Vector3 min = triangles[0].P1;
        Vector3 max = triangles[0].P1;

        foreach (ModelTriangle tri in triangles)
        {
            min = Vector3.Min(min, Vector3.Min(tri.P1, Vector3.Min(tri.P2, tri.P3)));
            max = Vector3.Max(max, Vector3.Max(tri.P1, Vector3.Max(tri.P2, tri.P3)));
        }

        return (min + max) / 2f;
    }
}