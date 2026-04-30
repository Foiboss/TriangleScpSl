using UnityEngine;

namespace TriangleScpSl.Core.NGons;

// Raw n-gon from FBX: vertices and face color (Unity Color).
public struct NgonRaw(List<Vector3> vertices, Color color)
{
    public List<Vector3> Vertices = vertices;
    public Color Color = color;
}