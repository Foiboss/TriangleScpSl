using AdminToys;
using Exiled.API.Features.Toys;
using UnityEngine;

namespace Triangle.Core.TriangleMesh;

// A shared coordinate space for a collection of triangles.
public class TriangleSpace
{
    static readonly Quaternion[] LocalRootRots =
    [
        Quaternion.LookRotation(Vector3.right, Vector3.up), // normal X
        Quaternion.LookRotation(Vector3.up, Vector3.forward), // normal Y
        Quaternion.LookRotation(Vector3.forward, Vector3.up), // normal Z
    ];

    readonly Primitive[] _roots = new Primitive[3];
    readonly List<TriangleEntry> _entries = [];

    Vector3 _origin;
    Quaternion _orientation;

    public TriangleSpace(Vector3 origin, Quaternion orientation)
    {
        _origin = origin;
        _orientation = orientation;

        for (var i = 0; i < 3; i++)
        {
            _roots[i] = Primitive.Create(
                PrimitiveType.Quad,
                PrimitiveFlags.None,
                origin,
                null,
                Vector3.one,
                true,
                Color.clear);
            _roots[i].Rotation = orientation * LocalRootRots[i];
        }
    }

    public TriangleSpace(Vector3 origin) : this(origin, Quaternion.identity) { }

    public TriangleEntry AddTriangle
    (Vector3 p1, Vector3 p2, Vector3 p3, Color color,
        PrimitiveFlags flags = PrimitiveFlags.Visible)
    {
        Vector3 normal = Vector3.Cross(p2 - p1, p3 - p1).normalized;
        Primitive root = SelectRoot(normal);

        var entry = new TriangleEntry(p1, p2, p3, color, flags, root);
        _entries.Add(entry);
        return entry;
    }

    public void Move(Vector3 delta)
    {
        _origin += delta;

        foreach (Primitive root in _roots)
            root.Position += delta;
    }

    public void SetTransform(Vector3 origin, Quaternion orientation)
    {
        _origin = origin;
        _orientation = orientation;

        for (var i = 0; i < 3; i++)
        {
            _roots[i].Position = origin;
            _roots[i].Rotation = orientation * LocalRootRots[i];
        }
    }

    public void Destroy()
    {
        foreach (TriangleEntry entry in _entries)
            entry.Destroy();
        _entries.Clear();

        foreach (Primitive root in _roots)
            root.Destroy();
    }

    // Returns the root whose forward axis is most aligned with the given normal
    Primitive SelectRoot(Vector3 normal)
    {
        var best = 0;
        float bestDot = -1f;

        for (var i = 0; i < 3; i++)
        {
            float dot = Mathf.Abs(Vector3.Dot(normal, _roots[i].Rotation * Vector3.forward));

            if (dot > bestDot)
            {
                bestDot = dot;
                best = i;
            }
        }

        return _roots[best];
    }
}