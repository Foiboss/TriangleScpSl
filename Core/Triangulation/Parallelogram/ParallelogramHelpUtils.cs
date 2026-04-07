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
}