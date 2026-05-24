using Exiled.API.Features.Toys;
using TriangleScpSl.Core.Models.ApproximateModel;
using TriangleScpSl.Core.Primitives.Parallelogram;
using TriangleScpSl.Core.Primitives.Triangle;
using UnityEngine;

namespace TriangleScpSl.Core.Models.HierarchicalModel;

public partial class HierarchicalModel
{
    bool IsStretchFreeInHierarchy(int idx)
    {
        int cur = idx;

        while (true)
        {
            if (_quadBuildInfos[cur].Stretch != null)
                return false;

            if (!_hierarchicalParents.TryGetValue(cur, out int parent))
                return true;
            cur = parent;
        }
    }

    bool TryFitUnderQuad
    (Transform parentTransform,
        Vector3 wc0, Vector3 wc1, Vector3 wc2, Vector3 wc3,
        out Vector3 localPos, out Quaternion localRot, out Vector3 localScale, out float error)
    {
        localPos = Vector3.zero;
        localRot = Quaternion.identity;
        localScale = Vector3.one;
        error = float.MaxValue;

        Vector3 lc0 = parentTransform.InverseTransformPoint(wc0);
        Vector3 lc1 = parentTransform.InverseTransformPoint(wc1);
        Vector3 lc2 = parentTransform.InverseTransformPoint(wc2);
        Vector3 lc3 = parentTransform.InverseTransformPoint(wc3);

        Vector3 center = (lc0 + lc1 + lc2 + lc3) * 0.25f;
        Vector3 hd1 = lc0 - center, hd2 = lc1 - center;
        Vector3 e1 = hd1 + hd2, e2 = hd1 - hd2;
        float e1M = e1.magnitude, e2M = e2.magnitude;
        if (e1M < 1e-7f || e2M < 1e-7f) return false;

        Vector3 n = Vector3.Cross(e1, e2);
        if (n.sqrMagnitude < 1e-12f) return false;

        Quaternion rot = Quaternion.LookRotation(n.normalized, e2.normalized);
        var scale = new Vector3(e1M, e2M, 1f);

        float err = MeasureCornerError(parentTransform, center, rot, scale, wc0, wc1, wc2, wc3);
        if (err > _absoluteToleranceUnits) return false;

        localPos = center;
        localRot = rot;
        localScale = scale;
        error = err;
        return true;
    }

    static float MeasureCornerError
    (Transform pt, Vector3 lp, Quaternion lr, Vector3 ls,
        Vector3 t0, Vector3 t1, Vector3 t2, Vector3 t3)
    {
        var max = 0f;

        for (int cx = -1; cx <= 1; cx += 2)
        for (int cy = -1; cy <= 1; cy += 2)
        {
            Vector3 lc = lp + lr * new Vector3(cx * ls.x * 0.5f, cy * ls.y * 0.5f, 0f);
            Vector3 wc = pt.TransformPoint(lc);
            float d0 = (wc - t0).sqrMagnitude, d1 = (wc - t1).sqrMagnitude;
            float d2 = (wc - t2).sqrMagnitude, d3 = (wc - t3).sqrMagnitude;
            float m = Mathf.Min(Mathf.Min(d0, d1), Mathf.Min(d2, d3));
            if (m > max) max = m;
        }

        return Mathf.Sqrt(max);
    }

    void MarkUsedStretches()
    {
        _usedStretches.Clear();

        for (var i = 0; i < _parallelograms.Count; i++)
        {
            if (_hierarchicalParents.ContainsKey(i)) continue;
            Primitive? s = _quadBuildInfos[i].Stretch;
            if (s != null) _usedStretches.Add(s);
        }
    }

    void DestroyUnusedStretches()
    {
        foreach (StretchSpatialIndex.Entry entry in _stretches.All())
            if (!_usedStretches.Contains(entry.Stretch))
                entry.Stretch.Destroy();
    }

    float ComputeMaxParallelogramSize()
    {
        var max = 0.01f;

        foreach (ModelTriangle lt in _localTriangles)
        {
            Vector3 p1 = TransformPoint(lt.P1), p2 = TransformPoint(lt.P2), p3 = TransformPoint(lt.P3);
            if (InvertWinding) (p2, p3) = (p3, p2);
            Vector3[][] d = TriangleParallelogramBuilder.GetParallelogramsInfo(p1, p2, p3);

            for (var i = 0; i < 3; i++)
            {
                float s = Mathf.Max((d[i][0] + d[i][1]).magnitude, (d[i][0] - d[i][1]).magnitude);
                if (s > max) max = s;
            }
        }

        foreach (ModelParallelogram p in _localParallelograms)
        {
            Vector3 vLeft = TransformVector(p.VLeft);
            Vector3 vUp = TransformVector(p.VUp);
            float s = Mathf.Max((vLeft + vUp).magnitude, (vLeft - vUp).magnitude);
            if (s > max) max = s;
        }

        return max;
    }
}