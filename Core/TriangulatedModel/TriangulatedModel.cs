using Exiled.API.Features.Toys;
using TriangleScpSl.Core.Triangulation.Triangle;
using UnityEngine;

namespace TriangleScpSl.Core.TriangulatedModel;

// A 3-D mesh loaded from triangulated file data (STL/OBJ).
// The model stores local-space triangles and rebuilds TrianglePrimitive instances
// when its transform changes.
public class TriangulatedModel
{
    readonly Primitive _baseQuad;
    readonly List<ModelTriangle> _localTriangles = [];
    readonly List<TrianglePrimitive> _triangles = [];

    Vector3 _position;
    Quaternion _rotation;
    Vector3 _scale;
    readonly bool _invertWinding;
    bool _isDestroyed;

    public TriangulatedModel
    (
        IReadOnlyList<ModelTriangle> triangles,
        Vector3 worldPosition,
        AdminToys.PrimitiveFlags flags = AdminToys.PrimitiveFlags.Visible,
        float scale = 1f,
        bool invertWinding = false)
    {
        _position = worldPosition;
        _rotation = Quaternion.identity;
        _scale = Vector3.one * scale;
        _invertWinding = invertWinding;

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

        Vector3 modelCenter = CalculateCenter(triangles);

        foreach (ModelTriangle tri in triangles)
        {
            _localTriangles.Add(new ModelTriangle(tri.P1 - modelCenter, tri.P2 - modelCenter, tri.P3 - modelCenter, tri.Color));
        }

        BuildTriangles(flags);
    }

    public int Count => _triangles.Count;
    public int QuadCount => _isDestroyed ? 0 : Count * 6 + 1; // +1 for model base quad

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

    public Vector3 TransformPoint(Vector3 localPoint)
        => _position + (_rotation * Vector3.Scale(localPoint, _scale));

    public Vector3 InverseTransformPoint(Vector3 worldPoint)
    {
        Vector3 local = Quaternion.Inverse(_rotation) * (worldPoint - _position);

        return new Vector3(
            _scale.x != 0f ? local.x / _scale.x : 0f,
            _scale.y != 0f ? local.y / _scale.y : 0f,
            _scale.z != 0f ? local.z / _scale.z : 0f);
    }

    public Color Color
    {
        set
        {
            if (_isDestroyed)
                return;

            foreach (TrianglePrimitive triangle in _triangles) triangle.Color = value;
        }
    }

    public AdminToys.PrimitiveFlags Flags
    {
        set
        {
            if (_isDestroyed)
                return;

            foreach (TrianglePrimitive triangle in _triangles) triangle.Flags = value;
        }
    }

    public static TriangulatedModel Create
    (
        IReadOnlyList<ModelTriangle> triangles,
        Vector3 worldPosition,
        AdminToys.PrimitiveFlags flags = AdminToys.PrimitiveFlags.Visible,
        float scale = 1f,
        bool invertWinding = false)
        => new(triangles, worldPosition, flags, scale, invertWinding);

    public void Destroy()
    {
        if (_isDestroyed)
            return;

        _isDestroyed = true;

        foreach (TrianglePrimitive triangle in _triangles)
            triangle.Destroy();

        _triangles.Clear();
        _localTriangles.Clear();
        _baseQuad.Destroy();
    }

    public IReadOnlyList<(ModelTriangle Triangle, AdminToys.PrimitiveFlags Flags)> GetTriangleSnapshot()
    {
        if (_isDestroyed)
            return [];

        List<(ModelTriangle Triangle, AdminToys.PrimitiveFlags Flags)> snapshot = new(_triangles.Count);
        snapshot.AddRange(_triangles.Select(triangle => (new ModelTriangle(triangle.P1, triangle.P2, triangle.P3, triangle.Color), triangle.Flags)));

        return snapshot;
    }

    void BuildTriangles(AdminToys.PrimitiveFlags flags)
    {
        _triangles.Clear();

        foreach (ModelTriangle localTriangle in _localTriangles)
        {
            _triangles.Add(CreateTriangle(localTriangle, flags));
        }
    }

    TrianglePrimitive CreateTriangle(ModelTriangle localTriangle, AdminToys.PrimitiveFlags flags)
    {
        Vector3 p1 = TransformPoint(localTriangle.P1);
        Vector3 p2 = TransformPoint(localTriangle.P2);
        Vector3 p3 = TransformPoint(localTriangle.P3);

        if (_invertWinding)
            (p2, p3) = (p3, p2);

        return TrianglePrimitive.Create(p1, p2, p3, localTriangle.Color, flags);
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