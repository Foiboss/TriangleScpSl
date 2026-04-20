using AdminToys;
using Exiled.API.Features.Toys;
using UnityEngine;

namespace TriangleScpSl.Core.ParallelogramSpace;

public static class ParallelogramSpaceUtils
{
    public static Primitive CreateStretch(float theta, float phi)
    {
        var stretch = Primitive.Create(
            PrimitiveType.Quad,
            PrimitiveFlags.None,
            Vector3.zero,
            null,
            new Vector3(Mathf.Cos(phi) * VectorPhiSolver.F, Mathf.Sin(phi) * VectorPhiSolver.F, 1f),
            true,
            null
        );
        stretch.Rotation = Quaternion.Euler(0f, 0f, theta * Mathf.Rad2Deg);
        return stretch;
    }

    /// <summary>
    ///     Applies the full forward phi-transform (rotate -theta, then scale 1/(cos(phi)*F), 1/(sin(phi)*F)
    ///     World vector → local vector in the stretch space
    /// </summary>
    public static Vector3 ForwardTransform(Vector3 v, float theta, float phi)
    {
        // Validate phi to avoid division by zero
        double cp = Math.Cos(phi);
        double sp = Math.Sin(phi);

        if (Math.Abs(cp) < 1e-10 || Math.Abs(sp) < 1e-10)
            return v; // Degenerate case, return unchanged

        double c = Math.Cos(theta), s = Math.Sin(theta);
        double rx = v.x * c + v.y * s;
        double ry = -v.x * s + v.y * c;
        const double f = VectorPhiSolver.F;

        return new Vector3(
            (float)(rx / (cp * f)),
            (float)(ry / (sp * f)),
            v.z
        );
    }

    /// <summary>
    ///     Applies the inverse stretch (as Unity would apply to a child with scale (cos(phi)*F, sin(phi)*F, 1) and rotZ=θ)
    ///     Local vector → world
    /// </summary>
    public static Vector3 InverseTransform(Vector3 vLocal, float theta, float phi)
    {
        double cp = Math.Cos(phi), sp = Math.Sin(phi);
        const double f = VectorPhiSolver.F;
        double sx = vLocal.x * (cp * f);
        double sy = vLocal.y * (sp * f);
        double c = Math.Cos(theta), s = Math.Sin(theta);

        // Validate to catch NaN
        if (double.IsNaN(sx) || double.IsNaN(sy))
            return vLocal; // Safety fallback

        // Inverse of R(-theta) is R(theta)
        return new Vector3(
            (float)(sx * c - sy * s),
            (float)(sx * s + sy * c),
            vLocal.z
        );
    }

    /// <summary>
    ///     Maximum displacement of the 4 vertices of the parallelogram when using
    ///     a candidateStretch instead of the ideal (trueTheta, truePhi)
    ///     v1World, v2World are the world-space edge vectors from center to corner
    ///     (the parallelogram spans ±v1 ± v2 around its center)
    /// </summary>
    public static float MaxVertexError
    (
        Vector3 v1World, Vector3 v2World,
        float trueTheta, float truePhi,
        float candTheta, float candPhi)
    {
        // Local coordinates of the 4 vertices in the TRUE stretch space
        Vector3 v1Local = ForwardTransform(v1World, trueTheta, truePhi);
        Vector3 v2Local = ForwardTransform(v2World, trueTheta, truePhi);

        // The 4 corners of the parallelogram (local, relative to center): ±v1Local ± v2Local
        Vector3[] localVerts =
        [
            v1Local + v2Local, v1Local - v2Local,
            -v1Local + v2Local, -v1Local - v2Local,
        ];

        var maxErr = 0f;

        foreach (Vector3 lv in localVerts)
        {
            // Where the vertex should be
            Vector3 trueWorld = InverseTransform(lv, trueTheta, truePhi);
            // Where it will end up with the candidate stretch
            Vector3 candWorld = InverseTransform(lv, candTheta, candPhi);
            float err = (trueWorld - candWorld).magnitude;
            if (err > maxErr) maxErr = err;
        }

        return maxErr;
    }

    public static Primitive CreateParallelogram
    (
        Vector3 position, Vector3 v1, Vector3 v2,
        Primitive stretch, PrimitiveFlags flags, Color color)
    {
        Vector3 normal = Vector3.Cross(v1, v2).normalized;
        float a = (v1 + v2).magnitude;
        float b = (v1 - v2).magnitude;

        var prim = Primitive.Create(
            PrimitiveType.Quad, flags, position, null, Vector3.one, true, color);

        prim.Transform.SetParent(stretch.Transform, true);
        prim.Transform.localRotation = Quaternion.LookRotation(normal, (v1 - v2).normalized);
        prim.Transform.localScale = new Vector3(a, b, 1f);
        return prim;
    }
}