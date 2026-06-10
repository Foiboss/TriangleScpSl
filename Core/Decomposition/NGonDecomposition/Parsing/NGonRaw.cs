using UnityEngine;

namespace TriangleScpSl.Core.Decomposition.NGonDecomposition.Parsing;

public struct NGonRaw(List<Vector3> vertices, Color color, int objectGroup = -1)
{
    public List<Vector3> Vertices = vertices;
    public Color Color = color;
    public int ObjectGroup = objectGroup;
}