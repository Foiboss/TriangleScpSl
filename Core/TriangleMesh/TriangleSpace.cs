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

    readonly Primitive _baseRoot;
    readonly Primitive[] _roots = new Primitive[3];
    readonly List<TriangleEntry> _entries = [];

    public TriangleSpace(Vector3 origin, Quaternion orientation)
    {
        // Shared model-space anchor for all TriangleSpace roots.
        _baseRoot = Primitive.Create(
            PrimitiveType.Quad,
            PrimitiveFlags.None,
            origin,
            null,
            Vector3.one,
            true,
            Color.clear);

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
            _roots[i].Transform.SetParent(_baseRoot.Transform, true);
            _roots[i].Transform.localPosition = Vector3.zero;
            _roots[i].Transform.localRotation = LocalRootRots[i];
            
            
            _baseRoot.Rotation = orientation;
        }
    }

    public TriangleSpace(Vector3 origin) : this(origin, Quaternion.identity) { }

    public Vector3 Position
    {
        get => _baseRoot.Position;
        set => _baseRoot.Position = value;
    }

    public Quaternion Rotation
    {
        get => _baseRoot.Rotation;
        set => _baseRoot.Rotation = value;
    }

    public Transform Transform => _baseRoot.Transform;
    
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
    
    public void Destroy()
    {
        foreach (TriangleEntry entry in _entries)
            entry.Destroy();
        _entries.Clear();

        foreach (Primitive root in _roots)
            root.Destroy();

        _baseRoot.Destroy();
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