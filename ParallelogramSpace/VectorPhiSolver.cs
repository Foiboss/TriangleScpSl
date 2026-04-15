using UnityEngine;

namespace TriangleScpSl.ParallelogramSpace;

/// <summary>
/// Finds angle phi such that after dividing x/cos(phi)*f and y/sin(phi)*f,
/// the lengths of both transformed vectors are equal.
/// </summary>
public static class VectorPhiSolver
{
    public const float F = 2f;

    /// <summary>
    /// Applies the transform to a single vector:
    /// x' = x / (cos(phi) * f)
    /// y' = y / (sin(phi) * f)
    /// z' = z  (unchanged)
    /// </summary>
    static Vector3 Transform(Vector3 v, double phi)
    {
        double cosPhi = Math.Cos(phi);
        double sinPhi = Math.Sin(phi);

        Vector3 returnVector;

        if (Math.Abs(cosPhi) < 1e-12) // cos = 0
        {
            returnVector = v with { y = (float)(v.y / (sinPhi * F)) };
        }
        else if (Math.Abs(sinPhi) < 1e-12) // sin = 0
        {
            returnVector = v with { x = (float)(v.x / (cosPhi * F)) };
        }
        else // cos & sin != 0
        {
            returnVector = new Vector3(
                (float)(v.x / (cosPhi * F)),
                (float)(v.y / (sinPhi * F)),
                v.z
            );
        }

        return returnVector;
    }

    static double LengthSquaredDiff(Vector3 v1, Vector3 v2, double phi)
    {
        double c2 = Math.Cos(phi) * Math.Cos(phi);
        double s2 = Math.Sin(phi) * Math.Sin(phi);
        const double f2 = F * F;

        double ScaledLenSq(Vector3 v)
        {
            double xPart = c2 < 1e-24 ? v.x * v.x             : v.x * v.x / (c2 * f2);
            double yPart = s2 < 1e-24 ? v.y * v.y             : v.y * v.y / (s2 * f2);
            return xPart + yPart + v.z * v.z;
        }

        return ScaledLenSq(v1) - ScaledLenSq(v2);
    }

    /// <param name="v1">First input vector</param>
    /// <param name="v2">Second input vector</param>
    /// <param name="phi">Found angle in radians</param>
    /// <param name="resultV1">Transformed v1</param>
    /// <param name="resultV2">Transformed v2</param>
    public static void Solve
    (
        Vector3 v1, Vector3 v2,
        out float phi,
        out Vector3 resultV1, out Vector3 resultV2)
    {
        // Note: the equation |v1'|^2 = |v2'|^2 depends only on cos^2(phi) and sin^2(phi),
        // so it is periodic with period pi/2. We search one root in (eps, pi/2 - eps),
        // then check all quadrant variants to avoid divisions by zero on boundary.

        const double eps = 1e-9;
        const double hi = Math.PI / 2 - eps;
        const int maxIter = 200;

        double fLo = LengthSquaredDiff(v1, v2, eps);
        double fHi = LengthSquaredDiff(v1, v2, hi);

        double? root = null;

        if (Math.Abs(fLo) < 1e-9)
            root = eps;
        else if (Math.Abs(fHi) < 1e-9)
            root = hi;
        else if (fLo * fHi < 0)
        {
            // Bisection in (lo, hi)
            double a = eps, b = hi;

            for (var i = 0; i < maxIter; i++)
            {
                double mid = (a + b) / 2.0;
                double fMid = LengthSquaredDiff(v1, v2, mid);

                if (Math.Abs(fMid) < 1e-12 || b - a < 1e-14)
                {
                    root = mid;
                    break;
                }

                if (fLo * fMid < 0)
                {
                    b = mid;
                }
                else
                {
                    a = mid;
                    fLo = fMid;
                }
            }

            root ??= (a + b) / 2.0;
        }

        if (root == null)
        {
            // No sign change: vectors may already have the same length regardless of phi,
            // or no solution exists in this quadrant. Try pi/4 as a fallback.
            double diff = LengthSquaredDiff(v1, v2, Math.PI / 4);

            if (Math.Abs(diff) < 1e-6)
                root = Math.PI / 4;
            else
            {
                throw new InvalidOperationException(
                    "No phi found in (0, pi/2) that equalizes the vector lengths. " +
                    "Check that v1 and v2 are not parallel projections of each other on x/y axes.");
            }
        }

        // The equation is the same for phi and pi-phi (cos^2 and sin^2 are symmetric),
        // so any of these four angles is a valid solution; pick the principal one.
        double phiD = root.Value;

        phi = (float)phiD;
        resultV1 = Transform(v1, phiD);
        resultV2 = Transform(v2, phiD);
    }
}