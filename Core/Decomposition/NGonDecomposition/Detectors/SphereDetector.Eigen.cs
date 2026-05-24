using UnityEngine;

namespace TriangleScpSl.Core.Decomposition.NGonDecomposition.Detectors;

public static partial class SphereDetector
{
    // Jacobi eigendecomposition for 3x3 symmetric matrix.
    // Input: upper triangle (m00, m01, m02, m11, m12, m22).
    // Output: eigenvalues sorted descending, corresponding eigenvectors.
    static void Eigen3X3
    (
        float m00, float m01, float m02,
        float m11, float m12,
        float m22,
        out Vector3 eigenvalues, out Vector3 ev0, out Vector3 ev1, out Vector3 ev2)
    {
        double a00 = m00, a01 = m01, a02 = m02;
        double a11 = m11, a12 = m12, a22 = m22;

        // Eigenvector matrix (starts as identity)
        double v00 = 1, v01 = 0, v02 = 0;
        double v10 = 0, v11 = 1, v12 = 0;
        double v20 = 0, v21 = 0, v22 = 1;

        for (var iter = 0; iter < 50; iter++)
        {
            double off = Math.Abs(a01) + Math.Abs(a02) + Math.Abs(a12);
            if (off < 1e-12) break;

            // Rotate to zero the largest off-diagonal
            double abs01 = Math.Abs(a01);
            double abs02 = Math.Abs(a02);
            double abs12 = Math.Abs(a12);

            int p, q;

            if (abs01 >= abs02 && abs01 >= abs12)
            {
                p = 0;
                q = 1;
            }
            else if (abs02 >= abs12)
            {
                p = 0;
                q = 2;
            }
            else
            {
                p = 1;
                q = 2;
            }

            double app = p == 0 ? a00 : p == 1 ? a11 : a22;
            double aqq = q == 0 ? a00 : q == 1 ? a11 : a22;
            double apq = p == 0 && q == 1 ? a01 : p == 0 && q == 2 ? a02 : a12;

            if (Math.Abs(apq) < 1e-15) continue;

            double tau = (aqq - app) / (2.0 * apq);
            double t = (tau >= 0 ? 1.0 : -1.0) / (Math.Abs(tau) + Math.Sqrt(1.0 + tau * tau));
            double cos = 1.0 / Math.Sqrt(1.0 + t * t);
            double sin = t * cos;

            ApplyJacobiRotation(ref a00, ref a01, ref a02, ref a11, ref a12, ref a22, p, q, cos, sin);

            ApplyRotationToColumns(ref v00, ref v01, ref v02,
                ref v10, ref v11, ref v12,
                ref v20, ref v21, ref v22, p, q, cos, sin);
        }

        // Sort eigenvalues descending
        double e0 = a00, e1 = a11, e2 = a22;
        Vector3 c0 = new((float)v00, (float)v10, (float)v20);
        Vector3 c1 = new((float)v01, (float)v11, (float)v21);
        Vector3 c2 = new((float)v02, (float)v12, (float)v22);

        // Bubble sort 3 elements
        if (e0 < e1)
        {
            (e0, e1) = (e1, e0);
            (c0, c1) = (c1, c0);
        }

        if (e1 < e2)
        {
            (e1, e2) = (e2, e1);
            (c1, c2) = (c2, c1);
        }

        if (e0 < e1)
        {
            (e0, e1) = (e1, e0);
            (c0, c1) = (c1, c0);
        }

        eigenvalues = new Vector3((float)e0, (float)e1, (float)e2);
        ev0 = c0.normalized;
        ev1 = c1.normalized;
        ev2 = c2.normalized;
    }

    static void ApplyJacobiRotation
    (
        ref double a00, ref double a01, ref double a02,
        ref double a11, ref double a12, ref double a22,
        int p, int q, double c, double s)
    {
        double[] a = [a00, a01, a02, a11, a12, a22];

        // Index mapping: (0,0)->0, (0,1)->1, (0,2)->2, (1,1)->3, (1,2)->4, (2,2)->5
        int Idx(int i, int j) => i <= j
            ? i == 0 ? j : i == 1 ? 3 + (j - 1) : 5
            : Idx(j, i);

        double app = a[Idx(p, p)];
        double aqq = a[Idx(q, q)];
        double apq = a[Idx(p, q)];

        a[Idx(p, p)] = c * c * app - 2 * s * c * apq + s * s * aqq;
        a[Idx(q, q)] = s * s * app + 2 * s * c * apq + c * c * aqq;
        a[Idx(p, q)] = 0;

        int r = 3 - p - q;
        double arp = a[Idx(r, p)];
        double arq = a[Idx(r, q)];
        a[Idx(r, p)] = c * arp - s * arq;
        a[Idx(r, q)] = s * arp + c * arq;

        a00 = a[0];
        a01 = a[1];
        a02 = a[2];
        a11 = a[3];
        a12 = a[4];
        a22 = a[5];
    }

    static void ApplyRotationToColumns
    (
        ref double v00, ref double v01, ref double v02,
        ref double v10, ref double v11, ref double v12,
        ref double v20, ref double v21, ref double v22,
        int p, int q, double c, double s)
    {
        double[] colP =
        [
            p == 0 ? v00 : p == 1 ? v01 : v02,
            p == 0 ? v10 : p == 1 ? v11 : v12,
            p == 0 ? v20 : p == 1 ? v21 : v22,
        ];

        double[] colQ =
        [
            q == 0 ? v00 : q == 1 ? v01 : v02,
            q == 0 ? v10 : q == 1 ? v11 : v12,
            q == 0 ? v20 : q == 1 ? v21 : v22,
        ];

        double[] newP = [c * colP[0] - s * colQ[0], c * colP[1] - s * colQ[1], c * colP[2] - s * colQ[2]];
        double[] newQ = [s * colP[0] + c * colQ[0], s * colP[1] + c * colQ[1], s * colP[2] + c * colQ[2]];

        SetCol(ref v00, ref v01, ref v02, ref v10, ref v11, ref v12, ref v20, ref v21, ref v22, p, newP);
        SetCol(ref v00, ref v01, ref v02, ref v10, ref v11, ref v12, ref v20, ref v21, ref v22, q, newQ);
    }

    static void SetCol
    (
        ref double v00, ref double v01, ref double v02,
        ref double v10, ref double v11, ref double v12,
        ref double v20, ref double v21, ref double v22,
        int col, double[] vals)
    {
        switch (col)
        {
            case 0:
                v00 = vals[0];
                v10 = vals[1];
                v20 = vals[2];
                break;
            case 1:
                v01 = vals[0];
                v11 = vals[1];
                v21 = vals[2];
                break;
            case 2:
                v02 = vals[0];
                v12 = vals[1];
                v22 = vals[2];
                break;
        }
    }
}