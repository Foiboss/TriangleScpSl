using UnityEngine;

namespace Triangle.Core.Triangulation.Triangle;

public static class TriangleParallelogramBuilder
{
    public static Vector3[][] GetParallelogramsInfo(Vector3 a, Vector3 b, Vector3 c)
    {
        Vector3 halfAb = (a + b) / 2f;
        Vector3 halfAc = (a + c) / 2f;
        Vector3 halfBc = (b + c) / 2f;

        Vector3 aCenter = (halfBc + a) / 2f;
        Vector3 bCenter = (halfAc + b) / 2f;
        Vector3 cCenter = (halfAb + c) / 2f;

        return
        [
            [a - aCenter, halfAc - aCenter, aCenter],
            [b - bCenter, halfAb - bCenter, bCenter],
            [c - cCenter, halfBc - cCenter, cCenter],
        ];
    }
}
