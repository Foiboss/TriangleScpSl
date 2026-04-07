using AdminToys;
using TriangleScpSl.Core.TriangleMesh;
using UnityEngine;

namespace TriangleScpSl.Core.TriangulatedModel;

// A 3-D mesh loaded from triangulated file data (STL/OBJ)
// All triangles share a single TriangleSpace
public class TriangulatedModel
{
    readonly TriangleSpace _space;
    readonly List<TriangleEntry> _entries = [];

    public TriangulatedModel
    (
        IReadOnlyList<ModelTriangle> triangles,
        Vector3 worldPosition,
        PrimitiveFlags flags = PrimitiveFlags.Visible,
        float scale = 1f,
        bool swapSecondAndThirdVertices = false,
        bool invertWinding = false)
    {
        _space = new TriangleSpace(worldPosition);

        if (triangles.Count == 0) return;

        Vector3 modelCenter = CalculateCenter(triangles);

        foreach (ModelTriangle tri in triangles)
        {
            // Map model vertices to world space, centered on worldPosition.
            Vector3 p1 = (tri.P1 - modelCenter) * scale + worldPosition;
            Vector3 p2 = (tri.P2 - modelCenter) * scale + worldPosition;
            Vector3 p3 = (tri.P3 - modelCenter) * scale + worldPosition;

            if (swapSecondAndThirdVertices)
                (p2, p3) = (p3, p2);

            if (invertWinding)
                (p2, p3) = (p3, p2);

            _entries.Add(_space.AddTriangle(p1, p2, p3, tri.Color, flags));
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

    public Vector3 Scale
    {
        get => _space.Scale;
        set => _space.Scale = value;
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
        IReadOnlyList<ModelTriangle> triangles,
        Vector3 worldPosition,
        PrimitiveFlags flags = PrimitiveFlags.Visible,
        float scale = 1f,
        bool swapSecondAndThirdVertices = false,
        bool invertWinding = false)
        => new(triangles, worldPosition, flags, scale, swapSecondAndThirdVertices, invertWinding);

    public void Destroy() => _space.Destroy();

    public IReadOnlyList<(ModelTriangle Triangle, PrimitiveFlags Flags)> GetTriangleSnapshot()
    {
        List<(ModelTriangle Triangle, PrimitiveFlags Flags)> snapshot = new(_entries.Count);

        foreach (TriangleEntry entry in _entries)
        {
            snapshot.Add((
                new ModelTriangle(entry.P1, entry.P2, entry.P3, entry.Color),
                entry.Flags));
        }

        return snapshot;
    }

    static Vector3 CalculateCenter(IReadOnlyList<ModelTriangle> triangles)
    {
        Vector3 min = triangles[0].P1;
        Vector3 max = triangles[0].P1;

        foreach (ModelTriangle tri in triangles)
        {
            min = Vector3.Min(min, Vector3.Min(tri.P1, Vector3.Min(tri.P2, tri.P3)));
            max = Vector3.Max(max, Vector3.Max(tri.P1, Vector3.Max(tri.P2, tri.P3)));
        }

        return (min + max) / 2f;
    }
}