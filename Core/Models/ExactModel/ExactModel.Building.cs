using System.Collections;
using Exiled.API.Features.Toys;
using TriangleScpSl.Core.Primitives.Parallelogram;
using TriangleScpSl.Core.Primitives.Triangle;
using UnityEngine;

namespace TriangleScpSl.Core.Models.ExactModel;

public partial class ExactModel
{
    public override IEnumerator BuildTrianglesCoroutine(AdminToys.PrimitiveFlags flags, int trianglesPerFrame)
    {
        if (IsDestroyedValue)
            yield break;

        FlagsValue = flags;
        trianglesPerFrame = Mathf.Max(1, trianglesPerFrame);

        foreach (ParallelogramPrimitive parallelogram in _parallelograms)
            parallelogram.Destroy();

        foreach (Primitive rect in _rectangles)
            rect.Destroy();

        _parallelograms.Clear();
        _rectangles.Clear();
        DestroyNativePrimitives();

        var processed = 0;

        foreach (ModelParallelogram modelParallelogram in _modelParallelograms)
        {
            if (IsDestroyedValue)
                yield break;

            Vector3 vUp = InvertWinding ? -modelParallelogram.VUp : modelParallelogram.VUp;
            Vector3 vLeft = modelParallelogram.VLeft;
            Vector3 center = TransformPoint(modelParallelogram.Center);

            if (modelParallelogram.IsRectangle)
            {
                CreateRectangle(vLeft, vUp, center, modelParallelogram.Color, flags);
            }
            else
            {
                _parallelograms.Add(ParallelogramPrimitive.Create(
                    vUp, vLeft, center, modelParallelogram.Color, flags));
            }

            processed++;

            if (processed >= trianglesPerFrame)
            {
                processed = 0;
                yield return null;
            }
        }

        BuildNativePrimitives(flags);
    }

    void BuildTriangles(AdminToys.PrimitiveFlags flags)
    {
        FlagsValue = flags;
        _parallelograms.Clear();
        _rectangles.Clear();

        foreach (ModelParallelogram modelParallelogram in _modelParallelograms)
        {
            Vector3 vUp = InvertWinding ? -modelParallelogram.VUp : modelParallelogram.VUp;
            Vector3 vLeft = modelParallelogram.VLeft;
            Vector3 center = TransformPoint(modelParallelogram.Center);

            if (modelParallelogram.IsRectangle)
            {
                CreateRectangle(vLeft, vUp, center, modelParallelogram.Color, flags);
            }
            else
            {
                _parallelograms.Add(ParallelogramPrimitive.Create(
                    vUp, vLeft, center, modelParallelogram.Color, flags));
            }
        }

        BuildNativePrimitives(flags);
    }

    void CreateRectangle(Vector3 vLeft, Vector3 vUp, Vector3 center, Color color, AdminToys.PrimitiveFlags flags)
    {
        // VLeft/VUp are half-diagonals. Edges are (VLeft+VUp) and (VLeft-VUp).
        Vector3 edgeA = vLeft + vUp;
        Vector3 edgeB = vLeft - vUp;
        float width = edgeB.magnitude;
        float height = edgeA.magnitude;

        // Manual normalization: Vector3.normalized snaps sub-1e-5 vectors to zero,
        // which would needlessly route small rectangles through the parallelogram path.
        Vector3 cross = Vector3.Cross(edgeB, edgeA);
        float crossMag = cross.magnitude;

        if (crossMag < 1e-12f || width < 1e-7f || height < 1e-7f)
        {
            _parallelograms.Add(ParallelogramPrimitive.Create(vUp, vLeft, center, color, flags));
            return;
        }

        Quaternion rotation = Quaternion.LookRotation(cross / crossMag, edgeA / height);

        var quad = Primitive.Create(PrimitiveType.Quad, flags, center, rotation.eulerAngles,
            new Vector3(width, height, 1f), true, color);
        _rectangles.Add(quad);
    }

    (ModelParallelogram para1, ModelParallelogram para2, ModelParallelogram para3) GetParallelograms(ModelTriangle localTriangle, Color color)
    {
        Vector3 p1 = localTriangle.P1;
        Vector3 p2 = localTriangle.P2;
        Vector3 p3 = localTriangle.P3;

        if (InvertWinding)
            (p2, p3) = (p3, p2);

        Vector3[][] data = TriangleParallelogramBuilder.GetParallelogramsInfo(p1, p2, p3);

        var para1 = new ModelParallelogram { VLeft = data[0][0], VUp = data[0][1], Center = data[0][2], Color = color };
        var para2 = new ModelParallelogram { VLeft = data[1][0], VUp = data[1][1], Center = data[1][2], Color = color };
        var para3 = new ModelParallelogram { VLeft = data[2][0], VUp = data[2][1], Center = data[2][2], Color = color };

        return (para1, para2, para3);
    }
}