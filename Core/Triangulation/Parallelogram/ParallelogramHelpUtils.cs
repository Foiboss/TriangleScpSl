using UnityEngine;

namespace TriangleScpSl.Core.Triangulation.Parallelogram;

public static class ParallelogramHelpUtils
{
    public static Vector3 PerpSameHalfPlane(Vector3 vUp, Vector3 vLeft)
    {
        Vector3 res = Vector3.ProjectOnPlane(vLeft, vUp.normalized).normalized;

        if (Vector3.Dot(res, vLeft) < 0f)
            res = -res;

        return res;
    }

    public static (float A, float B, float X) GetAffineComponents(Vector3 vUp, Vector3 vLeft)
    {
        float upLen = vUp.magnitude;

        if (upLen <= Mathf.Epsilon)
            return (1f, 1f, 1f);

        Vector3 upN = vUp / upLen;
        float leftY = Mathf.Clamp(Vector3.Dot(vLeft, upN), -upLen, upLen);
        float leftX = Vector3.ProjectOnPlane(vLeft, upN).magnitude;

        float a = Mathf.Sqrt(Mathf.Max(2f * upLen * (upLen + leftY), Mathf.Epsilon));
        float b = Mathf.Sqrt(Mathf.Max(2f * upLen * (upLen - leftY), Mathf.Epsilon));
        float x = leftX * 2f * upLen / Mathf.Max(a * b, Mathf.Epsilon);

        return (a, b, x);
    }
}