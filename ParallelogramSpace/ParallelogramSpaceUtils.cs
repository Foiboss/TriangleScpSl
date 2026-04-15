using AdminToys;
using Exiled.API.Features.Toys;
using UnityEngine;

namespace TriangleScpSl.ParallelogramSpace;

public static class ParallelogramSpaceUtils
{
    public static Primitive CreateStretch(float phi)
    {
        return Primitive.Create(
            PrimitiveType.Quad,
            PrimitiveFlags.None,
            Vector3.zero,
            null,
            new Vector3(Mathf.Cos(phi) * VectorPhiSolver.F, Mathf.Sin(phi) * VectorPhiSolver.F, 1f),
            true,
            null
        );
    }

    public static Primitive CreateParallelogram(Vector3 position, Vector3 v1, Vector3 v2, Primitive stretch, PrimitiveFlags flags, Color color)
    {
        // Everything below is computed in the transformed space where |v1| == |v2| (rectangle)
        Vector3 normal = Vector3.Cross(v1, v2).normalized;

        float a = (v1 + v2).magnitude;
        float b = (v1 - v2).magnitude;

        var prim = Primitive.Create(
            PrimitiveType.Quad,
            flags,
            position,
            null,
            new Vector3(a, b, 1f),
            true,
            color
        );

        prim.Rotation = Quaternion.LookRotation(normal, (v1 - v2).normalized);
        prim.Transform.SetParent(stretch.Transform, true);
        prim.Transform.localScale = new Vector3(a, b, 1f);
        
        return prim;
    }
    
    
}