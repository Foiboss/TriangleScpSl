using AdminToys;
using Exiled.API.Features.Toys;
using TriangleScpSl.Core.TriangulatedModel;
using TriangleScpSl.Core.Triangulation.Parallelogram;
using TriangleScpSl.Core.Triangulation.Triangle;
using UnityEngine;
using System.Collections;

namespace TriangleScpSl.ParallelogramSpace;

public class ParallelogramSpace
{
    readonly float _absoluteToleranceUnits;
    readonly Primitive _baseQuad;
    readonly List<ModelTriangle> _localTriangles = [];
    readonly StretchSpatialIndex _stretches;
    readonly List<Primitive> _parallelograms = [];
    readonly List<ParallelogramPrimitive> _fallbackParallelograms = [];
    readonly List<ParallelogramSnapshot> _parallelogramSnapshots = [];
    readonly bool _invertWinding;
    PrimitiveFlags _flags;

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
        bool invertWinding = false,
        bool buildImmediately = true)
    {
        _absoluteToleranceUnits = absoluteToleranceUnits;
        _position = worldPosition;
        _rotation = Quaternion.identity;
        _scale = Vector3.one * scale;
        _invertWinding = invertWinding;
        _flags = flags;

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
            0.05f,
            absoluteToleranceUnits / maxSize * 2f
        );

        if (buildImmediately)
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

            for (var i = 0; i < _parallelogramSnapshots.Count; i++)
            {
                ParallelogramSnapshot snapshot = _parallelogramSnapshots[i];
                _parallelogramSnapshots[i] = new ParallelogramSnapshot(snapshot.VUp, snapshot.VLeft, snapshot.Center, value, snapshot.Flags, snapshot.IsFallback);
            }
        }
    }

    public PrimitiveFlags Flags
    {
        set
        {
            if (_isDestroyed)
                return;

            _flags = value;

            foreach (Primitive parallelogram in _parallelograms) parallelogram.Flags = value;
            foreach (ParallelogramPrimitive parallelogram in _fallbackParallelograms) parallelogram.Flags = value;

            for (var i = 0; i < _parallelogramSnapshots.Count; i++)
            {
                ParallelogramSnapshot snapshot = _parallelogramSnapshots[i];
                _parallelogramSnapshots[i] = new ParallelogramSnapshot(snapshot.VUp, snapshot.VLeft, snapshot.Center, snapshot.Color, value, snapshot.IsFallback);
            }
        }
    }

    public Vector3 TransformPoint(Vector3 localPoint)
        => _position + _rotation * Vector3.Scale(localPoint, _scale);

    public Vector3 InverseTransformPoint(Vector3 worldPoint)
    {
        Vector3 local = Quaternion.Inverse(_rotation) * (worldPoint - _position);

        return new Vector3(
            _scale.x != 0f ? local.x / _scale.x : 0f,
            _scale.y != 0f ? local.y / _scale.y : 0f,
            _scale.z != 0f ? local.z / _scale.z : 0f);
    }

    public static ParallelogramSpace Create
    (
        IReadOnlyList<ModelTriangle> triangles,
        Vector3 worldPosition,
        PrimitiveFlags flags = PrimitiveFlags.Visible,
        float absoluteToleranceUnits = 0.001f,
        float scale = 1f,
        bool invertWinding = false)
        => new(triangles, worldPosition, flags, absoluteToleranceUnits, scale, invertWinding);

    public static ParallelogramSpace CreateDeferred
    (
        IReadOnlyList<ModelTriangle> triangles,
        Vector3 worldPosition,
        PrimitiveFlags flags = PrimitiveFlags.Visible,
        float absoluteToleranceUnits = 0.001f,
        float scale = 1f,
        bool invertWinding = false)
        => new(triangles, worldPosition, flags, absoluteToleranceUnits, scale, invertWinding, false);

    public IReadOnlyList<(ModelTriangle Triangle, PrimitiveFlags Flags)> GetTriangleSnapshot()
    {
        if (_isDestroyed)
            return [];

        List<(ModelTriangle Triangle, PrimitiveFlags Flags)> snapshot = new(_localTriangles.Count);

        foreach (ModelTriangle localTriangle in _localTriangles)
        {
            Vector3 p1 = TransformPoint(localTriangle.P1);
            Vector3 p2 = TransformPoint(localTriangle.P2);
            Vector3 p3 = TransformPoint(localTriangle.P3);

            if (_invertWinding)
                (p2, p3) = (p3, p2);

            snapshot.Add((new ModelTriangle(p1, p2, p3, localTriangle.Color), _flags));
        }

        return snapshot;
    }

    public IReadOnlyList<ParallelogramSnapshot> GetParallelogramSnapshot()
    {
        if (_isDestroyed)
            return [];

        return _parallelogramSnapshots.ToArray();
    }

    public IReadOnlyList<PrimitiveSnapshot> GetPrimitiveSnapshot()
    {
        if (_isDestroyed)
            return [];

        List<PrimitiveSnapshot> snapshot = new(QuadCount);
        Dictionary<Transform, int> indexByTransform = new(QuadCount);

        int modelBaseIndex = snapshot.Count;

        snapshot.Add(new PrimitiveSnapshot(
            _baseQuad.Position,
            _baseQuad.Rotation,
            _baseQuad.Scale,
            Vector3.zero,
            Quaternion.identity,
            Vector3.one,
            _baseQuad.Color,
            _baseQuad.Flags,
            "ModelBase",
            -1));
        indexByTransform[_baseQuad.Transform] = modelBaseIndex;

        foreach (StretchSpatialIndex.Entry entry in _stretches.All())
        {
            int stretchIndex = snapshot.Count;
            Transform stretchTransform = entry.Stretch.Transform;
            int parentIndex = indexByTransform.TryGetValue(stretchTransform.parent, out int foundParent) ? foundParent : modelBaseIndex;

            snapshot.Add(new PrimitiveSnapshot(
                entry.Stretch.Position,
                entry.Stretch.Rotation,
                entry.Stretch.Scale,
                stretchTransform.localPosition,
                stretchTransform.localRotation,
                stretchTransform.localScale,
                entry.Stretch.Color,
                entry.Stretch.Flags,
                "Stretch",
                parentIndex));

            indexByTransform[stretchTransform] = stretchIndex;
        }

        foreach (Primitive parallelogram in _parallelograms)
        {
            Transform parallelogramTransform = parallelogram.Transform;
            int parentIndex = indexByTransform.TryGetValue(parallelogramTransform.parent, out int foundParent) ? foundParent : modelBaseIndex;

            snapshot.Add(new PrimitiveSnapshot(
                parallelogram.Position,
                parallelogram.Rotation,
                parallelogram.Scale,
                parallelogramTransform.localPosition,
                parallelogramTransform.localRotation,
                parallelogramTransform.localScale,
                parallelogram.Color,
                parallelogram.Flags,
                "Parallelogram",
                parentIndex));
        }

        foreach (ParallelogramPrimitive fallback in _fallbackParallelograms)
        {
            Primitive fallbackBase = fallback.BasePrimitive;
            Transform fallbackBaseTransform = fallbackBase.Transform;
            int fallbackBaseParent = indexByTransform.TryGetValue(fallbackBaseTransform.parent, out int foundBaseParent) ? foundBaseParent : modelBaseIndex;

            int fallbackBaseIndex = snapshot.Count;

            snapshot.Add(new PrimitiveSnapshot(
                fallbackBase.Position,
                fallbackBase.Rotation,
                fallbackBase.Scale,
                fallbackBaseTransform.localPosition,
                fallbackBaseTransform.localRotation,
                fallbackBaseTransform.localScale,
                fallbackBase.Color,
                fallbackBase.Flags,
                "FallbackBase",
                fallbackBaseParent));

            indexByTransform[fallbackBaseTransform] = fallbackBaseIndex;

            Primitive fallbackQuad = fallback.QuadPrimitive;
            Transform fallbackQuadTransform = fallbackQuad.Transform;
            int fallbackQuadParent = indexByTransform.TryGetValue(fallbackQuadTransform.parent, out int foundQuadParent) ? foundQuadParent : fallbackBaseIndex;

            snapshot.Add(new PrimitiveSnapshot(
                fallbackQuad.Position,
                fallbackQuad.Rotation,
                fallbackQuad.Scale,
                fallbackQuadTransform.localPosition,
                fallbackQuadTransform.localRotation,
                fallbackQuadTransform.localScale,
                fallbackQuad.Color,
                fallbackQuad.Flags,
                "FallbackQuad",
                fallbackQuadParent));
        }

        return snapshot;
    }

    public void Destroy()
    {
        if (_isDestroyed)
            return;

        _isDestroyed = true;

        ClearAllPrimitives();
        _stretches.Clear();
        _parallelograms.Clear();
        _fallbackParallelograms.Clear();
        _parallelogramSnapshots.Clear();
        _localTriangles.Clear();
        _baseQuad.Destroy();
    }

    void BuildTriangles(PrimitiveFlags flags)
    {
        if (_isDestroyed)
            return;

        _flags = flags;

        // Clear previous data
        ClearAllPrimitives();
        _stretches.Clear();
        _parallelograms.Clear();
        _fallbackParallelograms.Clear();
        _parallelogramSnapshots.Clear();

        foreach (ModelTriangle localTriangle in _localTriangles)
            CreateTriangle(localTriangle, flags);
    }

    public IEnumerator BuildTrianglesCoroutine(PrimitiveFlags flags, int trianglesPerFrame)
    {
        if (_isDestroyed)
            yield break;

        trianglesPerFrame = Mathf.Max(1, trianglesPerFrame);
        _flags = flags;

        ClearAllPrimitives();
        _stretches.Clear();
        _parallelograms.Clear();
        _fallbackParallelograms.Clear();
        _parallelogramSnapshots.Clear();

        var processed = 0;

        foreach (ModelTriangle localTriangle in _localTriangles)
        {
            if (_isDestroyed)
                yield break;

            CreateTriangle(localTriangle, flags);
            processed++;

            if (processed >= trianglesPerFrame)
            {
                processed = 0;
                yield return null;
            }
        }
    }

    void ClearAllPrimitives()
    {
        foreach (StretchSpatialIndex.Entry entry in _stretches.All())
            entry.Stretch.Destroy();

        foreach (Primitive parallelogram in _parallelograms)
            parallelogram.Destroy();

        foreach (ParallelogramPrimitive parallelogram in _fallbackParallelograms)
            parallelogram.Destroy();
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

            if (parallelogram.Transform.parent != _baseQuad.Transform)
                parallelogram.Transform.SetParent(_baseQuad.Transform);

            _parallelogramSnapshots.Add(new ParallelogramSnapshot(vUp, vLeft, center, color, flags, true));
            return;
        }

        Primitive? bestStretch = null;
        float bestTheta = 0f, bestPhi = 0f;
        var bestErr = float.MaxValue;

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

            if (stretch.Transform.parent != _baseQuad.Transform)
                stretch.Transform.SetParent(_baseQuad.Transform);

            stretchTheta = theta;
            stretchPhi = phi;
        }

        Vector3 v1ForStretch = ParallelogramSpaceUtils.ForwardTransform(vLeft, stretchTheta, stretchPhi);
        Vector3 v2ForStretch = ParallelogramSpaceUtils.ForwardTransform(vUp, stretchTheta, stretchPhi);

        _parallelograms.Add(
            ParallelogramSpaceUtils.CreateParallelogram(center, v1ForStretch, v2ForStretch, stretch, flags, color));
        _parallelogramSnapshots.Add(new ParallelogramSnapshot(vUp, vLeft, center, color, flags, false));
    }

    /// <summary>
    ///     Computes the maximum parallelogram size (max diagonal: max of (v1+v2).magnitude, (v1-v2).magnitude)
    ///     across all parallelograms that will be generated from the local triangles
    ///     Used to derive the angular tolerance for stretch clustering
    /// </summary>
    float ComputeMaxParallelogramSize()
    {
        var maxSize = 0.01f; // avoid division by zero

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

    public sealed class ParallelogramSnapshot(Vector3 vUp, Vector3 vLeft, Vector3 center, Color color, PrimitiveFlags flags, bool isFallback)
    {
        public Vector3 VUp { get; } = vUp;
        public Vector3 VLeft { get; } = vLeft;
        public Vector3 Center { get; } = center;
        public Color Color { get; } = color;
        public PrimitiveFlags Flags { get; } = flags;
        public bool IsFallback { get; } = isFallback;
    }

    public sealed class PrimitiveSnapshot
    (
        Vector3 position,
        Quaternion rotation,
        Vector3 scale,
        Vector3 localPosition,
        Quaternion localRotation,
        Vector3 localScale,
        Color color,
        PrimitiveFlags flags,
        string kind,
        int parentIndex)
    {
        public Vector3 Position { get; } = position;
        public Quaternion Rotation { get; } = rotation;
        public Vector3 Scale { get; } = scale;
        public Vector3 LocalPosition { get; } = localPosition;
        public Quaternion LocalRotation { get; } = localRotation;
        public Vector3 LocalScale { get; } = localScale;
        public Color Color { get; } = color;
        public PrimitiveFlags Flags { get; } = flags;
        public string Kind { get; } = kind;
        public int ParentIndex { get; } = parentIndex;
    }
}