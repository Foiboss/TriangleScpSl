using AdminToys;
using Exiled.API.Features.Toys;
using UnityEngine;

namespace TriangleScpSl.Core.Models.ApproximateModel;

public static class ApproximateModelUtils
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

        if (double.IsNaN(sx) || double.IsNaN(sy))
            throw new InvalidOperationException($"InverseTransform produced NaN: sx={sx}, sy={sy}, vLocal={vLocal}, theta={theta}, phi={phi}");

        // Inverse of R(-theta) is R(theta)
        return new Vector3(
            (float)(sx * c - sy * s),
            (float)(sx * s + sy * c),
            vLocal.z
        );
    }

    // Measures the world-space vertex displacement of the rendered parallelogram
    // when CreateParallelogram is run with this candidate stretch instead of one
    // that exactly fits (vLeft, vUp). Returns absolute world units, scales linearly
    // with parallelogram size, and equals 0 whenever the candidate would render
    // (vLeft, vUp) exactly - including non-trivial cases where (candidateTheta, candidatePhi)
    // differs from the "true" solver output but still satisfies |v1C| = |v2C|.
    public static float MaxVertexError
    (
        Vector3 vLeft, Vector3 vUp,
        float candidateTheta, float candidatePhi)
    {
        // Project the half-diagonals into the candidate stretch's local space -
        // these are exactly the v1ForStretch / v2ForStretch CreateParallelogram uses.
        Vector3 v1C = ForwardTransform(vLeft, candidateTheta, candidatePhi);
        Vector3 v2C = ForwardTransform(vUp, candidateTheta, candidatePhi);

        Vector3 sumLocal = v1C + v2C;
        Vector3 diffLocal = v1C - v2C;
        float a = sumLocal.magnitude;
        float b = diffLocal.magnitude;

        if (a < 1e-12f || b < 1e-12f) return float.MaxValue;

        Vector3 normalLocal = Vector3.Cross(v1C, v2C);
        if (normalLocal.sqrMagnitude < 1e-24f) return float.MaxValue;
        normalLocal = normalLocal.normalized;

        // Same orientation CreateParallelogram applies via LookRotation:
        // local Y along (v1-v2), local Z along normal, local X = Y × Z.
        Vector3 yAxis = diffLocal / b;
        Vector3 xAxis = Vector3.Cross(yAxis, normalLocal);

        // The unit quad after scale (a, b, 1) and that rotation has 4 corners at
        // (±a/2)X + (±b/2)Y in candidate-local. Two of them; their negatives
        // give the other two and produce symmetric errors.
        Vector3 cornerA = a * 0.5f * xAxis + b * 0.5f * yAxis;
        Vector3 cornerB = a * 0.5f * xAxis - b * 0.5f * yAxis;

        // Apply the candidate stretch's parent transform: candidate-local -> world.
        Vector3 worldA = InverseTransform(cornerA, candidateTheta, candidatePhi);
        Vector3 worldB = InverseTransform(cornerB, candidateTheta, candidatePhi);

        // Geometric matching is fixed: cornerA always falls on the ±vUp corner pair,
        // cornerB on the ±vLeft pair (this drops out of LookRotation's basis choice).
        // Min(±) just picks which side of the pair.
        float dA = Mathf.Min((worldA - vUp).magnitude, (worldA + vUp).magnitude);
        float dB = Mathf.Min((worldB - vLeft).magnitude, (worldB + vLeft).magnitude);
        return Mathf.Max(dA, dB);
    }

    /// <summary>
    ///     Determines whether a parallelogram can safely use the stretch mechanism
    ///     without significant shear-induced rendering errors after TRS decomposition.
    ///     Combines the stretch's scale non-uniformity with the face's tilt from XY.
    ///     When both are large, the combined matrix has shear that lossy TRS decomposition
    ///     (euler angles + scale) cannot represent, causing wrong face orientation on clients.
    /// </summary>
    /// <param name="v1">First half-diagonal in stretch-local space.</param>
    /// <param name="v2">Second half-diagonal in stretch-local space.</param>
    /// <param name="phi">The phi angle of the stretch (determines scale non-uniformity).</param>
    public static bool IsStretchSafe(Vector3 v1, Vector3 v2, float phi)
    {
        // Scale non-uniformity: how different sx and sy are.
        // sx = cos(phi)*F, sy = sin(phi)*F  →  ratio = |cos(phi) - sin(phi)| / max(cos,sin)
        float cp = Mathf.Abs(Mathf.Cos(phi));
        float sp = Mathf.Abs(Mathf.Sin(phi));
        float maxCS = Mathf.Max(cp, sp);

        if (maxCS < 1e-8f)
            return false;

        float scaleRatio = Mathf.Abs(cp - sp) / maxCS;

        // If the stretch is nearly uniform (cp ≈ sp, i.e. phi ≈ π/4),
        // any tilt is fine — uniform scale + rotation = no shear.
        if (scaleRatio < 0.01f)
            return true;

        // Tilt from XY: how far the face normal deviates from +Z.
        // normal = cross(v1, v2); tilt = sqrt(nx² + ny²) / |normal|
        Vector3 normal = Vector3.Cross(v1, v2);
        float normalMagSq = normal.sqrMagnitude;

        if (normalMagSq < 1e-20f)
            return false;

        float tiltFromZ = Mathf.Sqrt((normal.x * normal.x + normal.y * normal.y) / normalMagSq);

        // Combined shear estimate: product of non-uniformity and tilt.
        // 0 when either the stretch is uniform OR the face is in the XY plane.
        // Large when both contribute.
        float shearEstimate = scaleRatio * tiltFromZ;

        const float threshold = 0.15f;
        return shearEstimate < threshold;
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