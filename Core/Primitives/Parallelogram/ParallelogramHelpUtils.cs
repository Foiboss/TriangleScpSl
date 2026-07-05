using Exiled.API.Features.Toys;
using UnityEngine;

namespace TriangleScpSl.Core.Primitives.Parallelogram;

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

    public static void Create(Vector3 vUp, Vector3 vLeft, Vector3 center, Primitive quad, Primitive baseQuad)
    {
        // Make the longer half-diagonal the up axis. GetAffineComponents needs
        // |Dot(vLeft, vUp)| strictly below vUp.sqrMagnitude with margin.
        // Longer-up maximizes the margin
        if (vUp.sqrMagnitude < vLeft.sqrMagnitude)
            (vUp, vLeft) = (vLeft, -vUp);

        (float a, float b, float x) = GetAffineComponents(vUp, vLeft);
        float angleDeg = Mathf.Atan2(b, a) * Mathf.Rad2Deg;
        Vector3 vNormal = Vector3.Cross(PerpSameHalfPlane(vUp, vLeft), vUp.normalized);

        baseQuad.Scale = new Vector3(x, 1f, 1f);
        baseQuad.Position = center;
        baseQuad.Rotation = Quaternion.LookRotation(vNormal, vUp);

        quad.Transform.SetParent(baseQuad.Transform, false);
        quad.Transform.localPosition = Vector3.zero;
        quad.Transform.localRotation = Quaternion.Euler(0f, 0f, -angleDeg);
        quad.Transform.localScale = new Vector3(b, a, 1f);
    }
}