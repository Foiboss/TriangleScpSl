using AdminToys;
using Exiled.API.Features.Toys;
using Triangle.Core.Triangulation.Parallelogram;
using Triangle.Core.Triangulation.Triangle;
using UnityEngine;

namespace Triangle.Core.TriangleMesh;

// A single triangle living inside a TriangleSpace.
// Unlike TrianglePrimitive it owns no root quad of its own —
// its three parallelograms are parented directly to one of the
// three shared roots in the enclosing TriangleSpace.
public class TriangleEntry
{
    readonly ParallelogramPrimitive _prim1;
    readonly ParallelogramPrimitive _prim2;
    readonly ParallelogramPrimitive _prim3;
    Color _color;

    // Only TriangleSpace should construct entries.
    internal TriangleEntry(Vector3 p1, Vector3 p2, Vector3 p3, Color color, PrimitiveFlags flags, Primitive root)
    {
        P1 = p1;
        P2 = p2;
        P3 = p3;
        _color = color;

        Vector3[][] d = TriangleParallelogramBuilder.GetParallelogramsInfo(p1, p2, p3);
        _prim1 = new ParallelogramPrimitive(d[0][0], d[0][1], d[0][2], color, flags, root);
        _prim2 = new ParallelogramPrimitive(d[1][0], d[1][1], d[1][2], color, flags, root);
        _prim3 = new ParallelogramPrimitive(d[2][0], d[2][1], d[2][2], color, flags, root);
    }

    public Vector3 P1 { get; private set; }
    public Vector3 P2 { get; private set; }
    public Vector3 P3 { get; private set; }

    public Vector3 Center => (P1 + P2 + P3) / 3f;
    public Vector3 Normal => Vector3.Cross(P2 - P1, P3 - P1).normalized;

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

    public void Rebuild(Vector3 p1, Vector3 p2, Vector3 p3)
    {
        P1 = p1;
        P2 = p2;
        P3 = p3;

        Vector3[][] d = TriangleParallelogramBuilder.GetParallelogramsInfo(p1, p2, p3);
        _prim1.Rebuild(d[0][0], d[0][1], d[0][2]);
        _prim2.Rebuild(d[1][0], d[1][1], d[1][2]);
        _prim3.Rebuild(d[2][0], d[2][1], d[2][2]);
    }

    public void Destroy()
    {
        _prim1.Destroy();
        _prim2.Destroy();
        _prim3.Destroy();
    }

    public List<Vector3> GetPoints() => [P1, P2, P3];
}