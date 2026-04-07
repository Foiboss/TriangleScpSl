using Exiled.API.Features.Toys;
using UnityEngine;

namespace TriangleScpSl.Core.Triangulation.Parallelogram;

public static class ParallelogramByPoints
{
    public static void Create(Vector3 vUp, Vector3 vLeft, Vector3 center, Primitive quad, Primitive baseQuad)
    {
        if (Mathf.Abs(Vector3.Dot(vLeft, vUp)) > vUp.sqrMagnitude)
            (vUp, vLeft) = (vLeft, -vUp);

        (float a, float b, float x) = ParallelogramHelpUtils.GetAffineComponents(vUp, vLeft);
        float angleDeg = Mathf.Atan2(b, a) * Mathf.Rad2Deg;
        Vector3 vNormal = Vector3.Cross(ParallelogramHelpUtils.PerpSameHalfPlane(vUp, vLeft), vUp.normalized);

        baseQuad.Scale = new Vector3(x, 1f, 1f);
        baseQuad.Position = center;
        baseQuad.Rotation = Quaternion.LookRotation(vNormal, vUp);

        quad.Transform.SetParent(baseQuad.Transform, false);
        quad.Transform.localPosition = Vector3.zero;
        quad.Transform.localRotation = Quaternion.Euler(0f, 0f, -angleDeg);
        quad.Transform.localScale = new Vector3(b, a, 1f);
    }
}