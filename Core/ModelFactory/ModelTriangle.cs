using UnityEngine;

namespace TriangleScpSl.Core.ModelFactory;

public readonly struct ModelTriangle(Vector3 p1, Vector3 p2, Vector3 p3, Color color)
{
    public Vector3 P1 { get; } = p1;
    public Vector3 P2 { get; } = p2;
    public Vector3 P3 { get; } = p3;
    public Color Color { get; } = color;
}