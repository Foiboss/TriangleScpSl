using AdminToys;
using Exiled.API.Features.Toys;
using UnityEngine;

namespace Triangle.Core.Triangulation.Parallelogram;

public class ParallelogramPrimitive
{
    Vector3 _p1, _p2, _p3, _p4;
    Color _color;

    public Color Color
    {
        get => _color;
        set
        {
            _color = value;
            _quad.Color = value;
        }
    }

    public PrimitiveFlags Flags
    {
        get => _quad.Flags;
        set => _quad.Flags = value;
    }
    
    public Vector3 Center => (_p1 + _p3) / 2f;

    readonly Primitive _quad;
    readonly Primitive _baseQuad;

    public static ParallelogramPrimitive Create(Vector3 vUp, Vector3 vLeft, Vector3 center, Color color,
        PrimitiveFlags flags = PrimitiveFlags.Visible, Primitive? parent = null) => new(vUp, vLeft, center, color, flags, parent);

    public ParallelogramPrimitive(Vector3 vUp, Vector3 vLeft, Vector3 center, Color color, PrimitiveFlags flags,
        Primitive? parent = null)
    {
        _color = color;
        _p1 = center + vUp;
        _p2 = center + vLeft;
        _p3 = center - vUp;
        _p4 = center - vLeft;

        _quad = Primitive.Create(PrimitiveType.Quad, flags, Vector3.zero, null, Vector3.one, true, color);
        _baseQuad = Primitive.Create(PrimitiveType.Quad, PrimitiveFlags.None, Vector3.zero, null, Vector3.one, true, color);
        ParallelogramByPoints.Create(vUp, vLeft, center, _quad, _baseQuad);

        if (parent != null)
            _baseQuad.Transform.SetParent(parent.Transform, worldPositionStays: true);
    }

    public void Rebuild(Vector3 vUp, Vector3 vLeft, Vector3 center)
    {
        _p1 = center + vUp;
        _p2 = center + vLeft;
        _p3 = center - vUp;
        _p4 = center - vLeft;

        ParallelogramByPoints.Create(vUp, vLeft, center, _quad, _baseQuad);
    }

    public void Destroy()
    {
        _quad.Destroy();
        _baseQuad.Destroy();
    }

    public List<Vector3> GetPoints() => [_p1, _p2, _p3, _p4];
}
