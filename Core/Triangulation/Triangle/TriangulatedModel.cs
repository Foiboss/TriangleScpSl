using AdminToys;
using Triangle.Core.Triangulation.Stl;
using UnityEngine;

namespace Triangle.Core.Triangulation.Triangle;

public class TriangulatedModel
{
    const int QuadsPerTriangle = 4;

    readonly List<TrianglePrimitive> _triangles = [];

    public int Count => _triangles.Count;
    public int QuadCount => Count * QuadsPerTriangle;

    public PrimitiveFlags Flags
    {
        set
        {
            foreach (TrianglePrimitive triangle in _triangles)
                triangle.Flags = value;
        }
    }

    public Color Color
    {
        set
        {
            foreach (TrianglePrimitive triangle in _triangles)
                triangle.Color = value;
        }
    }

    public static TriangulatedModel Create(
        IReadOnlyList<StlTriangle> stlTriangles,
        Vector3 worldPosition,
        Color color,
        PrimitiveFlags flags = PrimitiveFlags.Visible,
        float scale = 1f,
        bool invertWinding = false)
    {
        return new TriangulatedModel(stlTriangles, worldPosition, color, flags, scale, invertWinding);
    }

    public TriangulatedModel(
        IReadOnlyList<StlTriangle> stlTriangles,
        Vector3 worldPosition,
        Color color,
        PrimitiveFlags flags = PrimitiveFlags.Visible,
        float scale = 1f,
        bool invertWinding = false)
    {
        if (stlTriangles.Count == 0)
            return;

        Vector3 center = CalculateCenter(stlTriangles);

        foreach (StlTriangle triangle in stlTriangles)
        {
            Vector3 p1 = (triangle.P1 - center) * scale + worldPosition;
            Vector3 p2 = (triangle.P3 - center) * scale + worldPosition;
            Vector3 p3 = (triangle.P2 - center) * scale + worldPosition;

            if (invertWinding)
            {
                (p2, p3) = (p3, p2);
            }

            Vector3 cross = Vector3.Cross(p2 - p1, p3 - p1);
            if (cross.sqrMagnitude < 1e-8f)
                continue;

            _triangles.Add(new TrianglePrimitive(p1, p2, p3, color, flags));
        }
    }

    public void Destroy()
    {
        foreach (TrianglePrimitive triangle in _triangles)
            triangle.Destroy();

        _triangles.Clear();
    }

    static Vector3 CalculateCenter(IReadOnlyList<StlTriangle> triangles)
    {
        Vector3 min = triangles[0].P1;
        Vector3 max = triangles[0].P1;

        foreach (StlTriangle triangle in triangles)
        {
            ExpandBounds(triangle.P1, ref min, ref max);
            ExpandBounds(triangle.P2, ref min, ref max);
            ExpandBounds(triangle.P3, ref min, ref max);
        }

        return (min + max) / 2f;
    }

    static void ExpandBounds(Vector3 point, ref Vector3 min, ref Vector3 max)
    {
        min = Vector3.Min(min, point);
        max = Vector3.Max(max, point);
    }
}




