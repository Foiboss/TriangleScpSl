using Exiled.API.Features.Toys;
using UnityEngine;

namespace Triangle.Core.Triangulation.Parallelogram;

public static class ParallelogramByPoints
{
    public static void Create(Vector3 vUp, Vector3 vLeft, Vector3 center, Primitive quad, Primitive baseQuad)
    {
        AffineTransformationsInfo info = GetInfo(vUp, vLeft);
        float angleDeg = Mathf.Atan2(info.B, info.A) * Mathf.Rad2Deg;

        Vector3 vNormal = Vector3.Cross(ParallelogramHelpUtils.PerpSameHalfPlane(vUp, vLeft), vUp.normalized);

        baseQuad.Scale = new Vector3(info.X, 1f, 1f);
        baseQuad.Position = center;
        baseQuad.Rotation = Quaternion.LookRotation(vNormal, vUp);

        quad.Transform.SetParent(baseQuad.Transform, false);
        quad.Transform.localPosition = Vector3.zero;
        quad.Transform.localRotation = Quaternion.Euler(0f, 0f, -angleDeg);
        quad.Transform.localScale = new Vector3(info.B, info.A, 1f);
    }

    static AffineTransformationsInfo GetInfo(Vector3 vUp, Vector3 vLeft)
    {
        float upLen = vUp.magnitude;
        Vector3 upN = vUp / upLen;
        float leftY = Mathf.Clamp(Vector3.Dot(vLeft, upN), -upLen, upLen);
        float leftX = Vector3.ProjectOnPlane(vLeft, upN).magnitude;

        float a = Mathf.Sqrt(2f * upLen * (upLen + leftY));
        float b = Mathf.Sqrt(2f * upLen * (upLen - leftY));

        return new AffineTransformationsInfo(a, b, leftX * 2f * upLen / (a * b));
    }

    struct AffineTransformationsInfo(float a, float b, float x)
    {
        public readonly float A = a;
        public readonly float B = b;
        public readonly float X = x;
    }
}
