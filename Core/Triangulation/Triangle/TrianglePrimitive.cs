using AdminToys;
using Exiled.API.Features.Toys;
using TriangleScpSl.Core.Triangulation.Parallelogram;
using UnityEngine;

namespace TriangleScpSl.Core.Triangulation.Triangle;

public class TrianglePrimitive
{
    // Root quad at the centroid: invisible, provides a shared parent for all
    readonly Primitive _root;
    readonly ParallelogramPrimitive _prim1;
    readonly ParallelogramPrimitive _prim2;
    readonly ParallelogramPrimitive _prim3;
    Color _color;

    public TrianglePrimitive(Vector3 p1, Vector3 p2, Vector3 p3, Color color, PrimitiveFlags flags, Primitive? parent = null)
    {
        P1 = p1;
        P2 = p2;
        P3 = p3;
        _color = color;

        Vector3 centroid = (p1 + p2 + p3) / 3f;
        Vector3 normal = Vector3.Cross(p2 - p1, p3 - p1).normalized;
        Quaternion rootRot = Quaternion.LookRotation(normal, (p1 - centroid).normalized);

        _root = Primitive.Create(PrimitiveType.Quad, PrimitiveFlags.None, centroid, null, Vector3.one, true, color);
        _root.Rotation = rootRot;

        if (parent is not null)
            _root.Transform.SetParent(parent.Transform, true);

        Vector3[][] data = TriangleParallelogramBuilder.GetParallelogramsInfo(p1, p2, p3);
        _prim1 = new ParallelogramPrimitive(data[0][0], data[0][1], data[0][2], color, flags, _root);
        _prim2 = new ParallelogramPrimitive(data[1][0], data[1][1], data[1][2], color, flags, _root);
        _prim3 = new ParallelogramPrimitive(data[2][0], data[2][1], data[2][2], color, flags, _root);
    }

    public Vector3 P1 { get; private set; }
    public Vector3 P2 { get; private set; }
    public Vector3 P3 { get; private set; }

    public Vector3 Center => (P1 + P2 + P3) / 3f;
    public Vector3 Normal => Vector3.Cross(P2 - P1, P3 - P1).normalized;

    public Bounds Bounds
    {
        get
        {
            Vector3 min = Vector3.Min(Vector3.Min(P1, P2), P3);
            Vector3 max = Vector3.Max(Vector3.Max(P1, P2), P3);
            return new Bounds((min + max) / 2f, max - min);
        }
    }

    public Color Color
    {
        get => _color;
        set
        {
            _color = value;
            _prim1.Color = value;
            _prim2.Color = value;
            _prim3.Color = value;
        }
    }

    public PrimitiveFlags Flags
    {
        get => _prim1.Flags;
        set
        {
            _prim1.Flags = value;
            _prim2.Flags = value;
            _prim3.Flags = value;
        }
    }

    public static TrianglePrimitive Create
    (Vector3 p1, Vector3 p2, Vector3 p3, Color color,
        PrimitiveFlags flags = PrimitiveFlags.Visible, Primitive? parent = null) => new(p1, p2, p3, color, flags, parent);

    public void Rebuild(Vector3 p1, Vector3 p2, Vector3 p3)
    {
        P1 = p1;
        P2 = p2;
        P3 = p3;

        Vector3 centroid = (p1 + p2 + p3) / 3f;
        Vector3 normal = Vector3.Cross(p2 - p1, p3 - p1).normalized;
        _root.Position = centroid;
        _root.Rotation = Quaternion.LookRotation(normal, (p1 - centroid).normalized);

        Vector3[][] data = TriangleParallelogramBuilder.GetParallelogramsInfo(p1, p2, p3);
        _prim1.Rebuild(data[0][0], data[0][1], data[0][2]);
        _prim2.Rebuild(data[1][0], data[1][1], data[1][2]);
        _prim3.Rebuild(data[2][0], data[2][1], data[2][2]);
    }

    public void Move(Vector3 delta)
    {
        P1 += delta;
        P2 += delta;
        P3 += delta;
        _root.Position += delta;
    }

    public void Destroy()
    {
        _prim1.Destroy();
        _prim2.Destroy();
        _prim3.Destroy();
        _root.Destroy();
    }

    public List<Vector3> GetPoints() => [P1, P2, P3];
}