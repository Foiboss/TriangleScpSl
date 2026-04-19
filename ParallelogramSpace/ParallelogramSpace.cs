using AdminToys;
using Exiled.API.Features.Toys;
using TriangleScpSl.Core.TriangulatedModel;
using TriangleScpSl.Core.Triangulation.Parallelogram;
using TriangleScpSl.Core.Triangulation.Triangle;
using UnityEngine;

namespace TriangleScpSl.ParallelogramSpace;

public class ParallelogramSpace
{
    readonly float _absoluteToleranceUnits;
    readonly Primitive _baseQuad;
    readonly List<ModelTriangle> _localTriangles = [];
    readonly StretchSpatialIndex _stretches;
    readonly List<Primitive> _parallelograms = [];
    readonly List<ParallelogramPrimitive> _fallbackParallelograms = [];
    readonly bool _invertWinding;

    Vector3 _position;
    Quaternion _rotation;
    Vector3 _scale;
    bool _isDestroyed;

    public ParallelogramSpace
    (
        IReadOnlyList<ModelTriangle> triangles,
        Vector3 worldPosition,
        PrimitiveFlags flags = PrimitiveFlags.Visible,
        float absoluteToleranceUnits = 0.001f,
        float scale = 1f,
        bool invertWinding = false)
    {
        _absoluteToleranceUnits = absoluteToleranceUnits;
        _position = worldPosition;
        _rotation = Quaternion.identity;
        _scale = Vector3.one * scale;
        _invertWinding = invertWinding;

        _baseQuad = Primitive.Create(
            PrimitiveType.Quad,
            PrimitiveFlags.None,
            _position,
            Vector3.zero,
            _scale,
            true,
            Color.clear);

        if (triangles.Count == 0)
        {
            _stretches = new StretchSpatialIndex(0.05f, 0.1f);
            return;
        }

        Vector3 modelCenter = CalculateCenter(triangles);

        foreach (ModelTriangle tri in triangles)
            _localTriangles.Add(new ModelTriangle(tri.P1 - modelCenter, tri.P2 - modelCenter, tri.P3 - modelCenter, tri.Color));

        float maxSize = ComputeMaxParallelogramSize();
        _stretches = new StretchSpatialIndex(
            cellSize: 0.05f,
            maxAngularTolerance: absoluteToleranceUnits / maxSize * 2f
        );

        BuildTriangles(flags);
      
    }

    public int Count => _localTriangles.Count;
    public int QuadCount => _stretches.Count + _parallelograms.Count + _fallbackParallelograms.Count * 2 + 1; // +1 for model base quad

    public Vector3 Position
    {
        get => _position;
        set
        {
            if (_isDestroyed)
                return;

            _position = value;
            _baseQuad.Position = value;
        }
    }

    public Quaternion Rotation
    {
        get => _rotation;
        set
        {
            if (_isDestroyed)
                return;

            _rotation = value;
            _baseQuad.Rotation = value;
        }
    }

    public Vector3 Scale
    {
        get => _scale;
        set
        {
            if (_isDestroyed)
                return;

            _scale = value;
            _baseQuad.Scale = value;
        }
    }

    public Transform Transform => _baseQuad.Transform;

    public Color Color
    {
        set
        {
            if (_isDestroyed)
                return;

            foreach (Primitive parallelogram in _parallelograms) parallelogram.Color = value;
            foreach (ParallelogramPrimitive parallelogram in _fallbackParallelograms) parallelogram.Color = value;
        }
    }

    public PrimitiveFlags Flags
    {
        set
        {
            if (_isDestroyed)
                return;

            foreach (Primitive parallelogram in _parallelograms) parallelogram.Flags = value;
            foreach (ParallelogramPrimitive parallelogram in _fallbackParallelograms) parallelogram.Flags = value;
        }
    }

    public Vector3 TransformPoint(Vector3 localPoint)
        => _position + _rotation * Vector3.Scale(localPoint, _scale);

    public static ParallelogramSpace Create
    (
        IReadOnlyList<ModelTriangle> triangles,
        Vector3 worldPosition,
        PrimitiveFlags flags = PrimitiveFlags.Visible,
        float absoluteToleranceUnits = 0.001f,
        float scale = 1f,
        bool invertWinding = false)
        => new(triangles, worldPosition, flags, absoluteToleranceUnits, scale, invertWinding);

    public void Destroy()
    {
        if (_isDestroyed)
            return;

        _isDestroyed = true;

        foreach (StretchSpatialIndex.Entry entry in _stretches.All())
            entry.Stretch.Destroy();

        foreach (Primitive parallelogram in _parallelograms)
            parallelogram.Destroy();
        
        foreach (ParallelogramPrimitive parallelogram in _fallbackParallelograms)
            parallelogram.Destroy();

        _stretches.Clear();
        _parallelograms.Clear();
        _fallbackParallelograms.Clear();
        _localTriangles.Clear();
        _baseQuad.Destroy();
    }

    void BuildTriangles(PrimitiveFlags flags)
    {
        foreach (StretchSpatialIndex.Entry entry in _stretches.All())
            entry.Stretch.Destroy();

        foreach (Primitive parallelogram in _parallelograms)
            parallelogram.Destroy();
        
        foreach (ParallelogramPrimitive parallelogram in _fallbackParallelograms)
            parallelogram.Destroy();
        
        _stretches.Clear();
        _parallelograms.Clear();
        _fallbackParallelograms.Clear();

        foreach (ModelTriangle localTriangle in _localTriangles)
            CreateTriangle(localTriangle, flags);
    }

    void CreateTriangle(ModelTriangle localTriangle, PrimitiveFlags flags)
    {
        Vector3 p1 = TransformPoint(localTriangle.P1);
        Vector3 p2 = TransformPoint(localTriangle.P2);
        Vector3 p3 = TransformPoint(localTriangle.P3);

        if (_invertWinding)
            (p2, p3) = (p3, p2);

        Vector3[][] data = TriangleParallelogramBuilder.GetParallelogramsInfo(p1, p2, p3);
        CreateParallelogram(data[0][0], data[0][1], data[0][2], flags, localTriangle.Color);
        CreateParallelogram(data[1][0], data[1][1], data[1][2], flags, localTriangle.Color);
        CreateParallelogram(data[2][0], data[2][1], data[2][2], flags, localTriangle.Color);
    }

    void CreateParallelogram(Vector3 vLeft, Vector3 vUp, Vector3 center, PrimitiveFlags flags, Color color)
    {
        if (!VectorPhiSolver.TrySolve(vLeft, vUp, out float theta, out float phi))
        {
            var parallelogram = ParallelogramPrimitive.Create(vUp, vLeft, center, color, flags);
            _fallbackParallelograms.Add(parallelogram);
            parallelogram.Transform.SetParent(_baseQuad.Transform);
            return;
        }

        Primitive? bestStretch = null;
        float bestTheta = 0f, bestPhi = 0f;
        float bestErr = float.MaxValue;

        foreach (StretchSpatialIndex.Entry entry in _stretches.QueryNearby(theta, phi))
        {
            float err = ParallelogramSpaceUtils.MaxVertexError(
                vLeft, vUp, theta, phi, entry.Theta, entry.Phi);

            if (err <= _absoluteToleranceUnits && err < bestErr)
            {
                bestErr = err;
                bestStretch = entry.Stretch;
                bestTheta = entry.Theta;
                bestPhi = entry.Phi;
            }
        }

        Primitive stretch;
        float stretchTheta, stretchPhi;

        if (bestStretch != null)
        {
            stretch = bestStretch;
            stretchTheta = bestTheta;
            stretchPhi = bestPhi;
        }
        else
        {
            stretch = ParallelogramSpaceUtils.CreateStretch(theta, phi);
            _stretches.Add(theta, phi, stretch);
            stretch.Transform.SetParent(_baseQuad.Transform);
            stretchTheta = theta;
            stretchPhi = phi;
        }

        Vector3 v1ForStretch = ParallelogramSpaceUtils.ForwardTransform(vLeft, stretchTheta, stretchPhi);
        Vector3 v2ForStretch = ParallelogramSpaceUtils.ForwardTransform(vUp, stretchTheta, stretchPhi);

        _parallelograms.Add(
            ParallelogramSpaceUtils.CreateParallelogram(center, v1ForStretch, v2ForStretch, stretch, flags, color));
    }

    /// <summary>
    ///     Computes the maximum parallelogram size (max diagonal: max of (v1+v2).magnitude, (v1-v2).magnitude)
    ///     across all parallelograms that will be generated from the local triangles.
    ///     Used to derive the angular tolerance for stretch clustering.
    /// </summary>
    float ComputeMaxParallelogramSize()
    {
        float maxSize = 0.01f; // avoid division by zero
        foreach (ModelTriangle localTriangle in _localTriangles)
        {
            Vector3 p1 = TransformPoint(localTriangle.P1);
            Vector3 p2 = TransformPoint(localTriangle.P2);
            Vector3 p3 = TransformPoint(localTriangle.P3);

            if (_invertWinding) (p2, p3) = (p3, p2);

            Vector3[][] data = TriangleParallelogramBuilder.GetParallelogramsInfo(p1, p2, p3);
            for (var i = 0; i < 3; i++)
            {
                Vector3 v1 = data[i][0];
                Vector3 v2 = data[i][1];
                float size = Mathf.Max((v1 + v2).magnitude, (v1 - v2).magnitude);
                if (size > maxSize) maxSize = size;
            }
        }
        return maxSize;
    }

    static Vector3 CalculateCenter(IReadOnlyList<ModelTriangle> triangles)
    {
        Vector3 min = triangles[0].P1;
        Vector3 max = triangles[0].P1;

        foreach (ModelTriangle tri in triangles)
        {
            min = Vector3.Min(min, Vector3.Min(tri.P1, Vector3.Min(tri.P2, tri.P3)));
            max = Vector3.Max(max, Vector3.Max(tri.P1, Vector3.Max(tri.P2, tri.P3)));
        }

        return (min + max) / 2f;
    }
}
