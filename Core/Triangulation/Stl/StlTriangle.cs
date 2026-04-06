using UnityEngine;

namespace Triangle.Core.Triangulation.Stl;

public readonly struct StlTriangle(Vector3 p1, Vector3 p2, Vector3 p3)
{
    public Vector3 P1 { get; } = p1;
    public Vector3 P2 { get; } = p2;
    public Vector3 P3 { get; } = p3;
}