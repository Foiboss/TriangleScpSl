using AdminToys;
using Exiled.API.Features.Toys;
using UnityEngine;

namespace TriangleScpSl.Core.Triangulation.Parallelogram;

public class ParallelogramPrimitive
{
    Vector3 _p1, _p2, _p3, _p4;
    Color _color;

    public ParallelogramPrimitive
    (Vector3 vUp, Vector3 vLeft, Vector3 center, Color color, PrimitiveFlags flags)
    {
        _color = color;
        _p1 = center + vUp;
        _p2 = center + vLeft;
        _p3 = center - vUp;
        _p4 = center - vLeft;

        QuadPrimitive = Primitive.Create(PrimitiveType.Quad, flags, Vector3.zero, null, Vector3.one, true, color);
        BasePrimitive = Primitive.Create(PrimitiveType.Quad, PrimitiveFlags.None, Vector3.zero, null, Vector3.one, true, color);
        ParallelogramByPoints.Create(vUp, vLeft, center, QuadPrimitive, BasePrimitive);
    }

    public Color Color
    {
        get => _color;
        set
        {
            _color = value;
            QuadPrimitive.Color = value;
        }
    }

    public PrimitiveFlags Flags
    {
        get => QuadPrimitive.Flags;
        set => QuadPrimitive.Flags = value;
    }

    public bool IsStatic
    {
        get => BasePrimitive.IsStatic;
        set
        {
            BasePrimitive.IsStatic = value;   
            QuadPrimitive.IsStatic = value;   
        }
    }
    
    
    public Transform Transform => BasePrimitive.Transform;
    public Primitive BasePrimitive { get; }
    public Primitive QuadPrimitive { get; }

    public Vector3 Center => (_p1 + _p3) / 2f;

    public static ParallelogramPrimitive Create
    (Vector3 vUp, Vector3 vLeft, Vector3 center, Color color,
        PrimitiveFlags flags = PrimitiveFlags.Visible) => new(vUp, vLeft, center, color, flags);

    public void Rebuild(Vector3 vUp, Vector3 vLeft, Vector3 center)
    {
        _p1 = center + vUp;
        _p2 = center + vLeft;
        _p3 = center - vUp;
        _p4 = center - vLeft;

        ParallelogramByPoints.Create(vUp, vLeft, center, QuadPrimitive, BasePrimitive);
    }

    public void Destroy()
    {
        QuadPrimitive.Destroy();
        BasePrimitive.Destroy();
    }

    public List<Vector3> GetPoints() => [_p1, _p2, _p3, _p4];
}