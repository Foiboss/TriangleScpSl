using AdminToys;
using Triangle.Core.Triangulation.Stl;
using Triangle.Core.TriangleMesh;
using UnityEngine;

namespace Triangle.Core.Triangulation.Triangle;

// A 3-D mesh loaded from an STL file
// All triangles share a single TriangleSpace
public class TriangulatedModel
{
    readonly TriangleSpace _space;
    readonly List<TriangleEntry> _entries = [];

    public TriangulatedModel
    (
        IReadOnlyList<StlTriangle> stlTriangles,
        Vector3 worldPosition,
        Color color,
        PrimitiveFlags flags = PrimitiveFlags.Visible,
        float scale = 1f,
        bool invertWinding = false)
    {
        _space = new TriangleSpace(worldPosition);

        if (stlTriangles.Count == 0) return;

        Vector3 modelCenter = CalculateCenter(stlTriangles);

        foreach (StlTriangle tri in stlTriangles)
        {
            // Map STL vertices to world space, centered on worldPosition
            // P2/P3 are swapped to convert from STL winding
            Vector3 p1 = (tri.P1 - modelCenter) * scale + worldPosition;
            Vector3 p2 = (tri.P3 - modelCenter) * scale + worldPosition;
            Vector3 p3 = (tri.P2 - modelCenter) * scale + worldPosition;

            if (invertWinding)
                (p2, p3) = (p3, p2);

            _entries.Add(_space.AddTriangle(p1, p2, p3, color, flags));
        }
    }

    public int Count => _entries.Count;
    public int QuadCount => Count * 3 + 4; // 3 per triangle + 3 shared roots + 1 model base root

    public Vector3 Position
    {
        get => _space.Position;
        set => _space.Position = value;
    }

    public Quaternion Rotation
    {
        get => _space.Rotation;
        set => _space.Rotation = value;
    }

    public Transform Transform => _space.Transform;

    public Color Color
    {
        set
        {
            foreach (TriangleEntry e in _entries) e.Color = value;
        }
    }

    public PrimitiveFlags Flags
    {
        set
        {
            foreach (TriangleEntry e in _entries) e.Flags = value;
        }
    }

    public static TriangulatedModel Create
    (
        IReadOnlyList<StlTriangle> stlTriangles,
        Vector3 worldPosition,
        Color color,
        PrimitiveFlags flags = PrimitiveFlags.Visible,
        float scale = 1f,
        bool invertWinding = false)
        => new(stlTriangles, worldPosition, color, flags, scale, invertWinding);
    
    public void Destroy() => _space.Destroy();

    static Vector3 CalculateCenter(IReadOnlyList<StlTriangle> triangles)
    {
        Vector3 min = triangles[0].P1;
        Vector3 max = triangles[0].P1;

        foreach (StlTriangle tri in triangles)
        {
            min = Vector3.Min(min, Vector3.Min(tri.P1, Vector3.Min(tri.P2, tri.P3)));
            max = Vector3.Max(max, Vector3.Max(tri.P1, Vector3.Max(tri.P2, tri.P3)));
        }

        return (min + max) / 2f;
    }
}