using System.Collections;
using AdminToys;
using Exiled.API.Features.Toys;
using TriangleScpSl.Core.Decomposition.NGonDecomposition;
using TriangleScpSl.Core.ProjectMerExport;
using UnityEngine;

namespace TriangleScpSl.Core.Models;

public abstract class ModelBase
{
    protected readonly Primitive BaseQuad;
    protected readonly List<ModelPrimitive> ModelPrimitives = [];
    protected readonly List<Primitive> NativePrimitives = [];

    // One entry per native primitive: the invisible deformation base, or null
    // when the primitive has uniform scale and needs no base.
    protected readonly List<Primitive?> NativePrimitiveBases = [];
    protected readonly bool InvertWinding;
    protected PrimitiveFlags FlagsValue;
    protected Vector3 PositionValue;
    protected Quaternion RotationValue;
    protected Vector3 ScaleValue;
    protected bool IsDestroyedValue;

    protected ModelBase(Vector3 worldPosition, PrimitiveFlags flags, float scale, bool invertWinding)
    {
        PositionValue = worldPosition;
        RotationValue = Quaternion.identity;
        ScaleValue = Vector3.one * scale;
        InvertWinding = invertWinding;
        FlagsValue = flags;

        BaseQuad = Primitive.Create(
            PrimitiveType.Quad,
            PrimitiveFlags.None,
            PositionValue,
            Vector3.zero,
            ScaleValue,
            true,
            Color.clear);
    }

    public abstract int ParallelogramCount { get; }
    public abstract int PrimitiveCount { get; }
    public int NativePrimitiveCount => ModelPrimitives.Count;

    /// <summary>Number of spawned native primitive bases (null placeholders excluded).</summary>
    protected int NativePrimitiveBaseCount
    {
        get
        {
            var count = 0;

            foreach (Primitive? b in NativePrimitiveBases)
            {
                if (b != null)
                    count++;
            }

            return count;
        }
    }

    public Vector3 Position
    {
        get => PositionValue;
        set
        {
            if (IsDestroyedValue) return;
            PositionValue = value;
            BaseQuad.Position = value;
        }
    }

    public Quaternion Rotation
    {
        get => RotationValue;
        set
        {
            if (IsDestroyedValue) return;
            RotationValue = value;
            BaseQuad.Rotation = value;
        }
    }

    public Vector3 Scale
    {
        get => ScaleValue;
        set
        {
            if (IsDestroyedValue) return;
            ScaleValue = value;
            BaseQuad.Scale = value;
        }
    }

    public Transform Transform => BaseQuad.Transform;

    public abstract Color Color { set; }
    public abstract PrimitiveFlags Flags { get; set; }
    public abstract string ProjectMerDefaultName { get; }

    public Vector3 TransformPoint(Vector3 localPoint)
        => PositionValue + RotationValue * Vector3.Scale(localPoint, ScaleValue);

    protected Vector3 TransformVector(Vector3 localVector)
        => RotationValue * Vector3.Scale(localVector, ScaleValue);

    public Vector3 InverseTransformPoint(Vector3 worldPoint)
    {
        Vector3 local = Quaternion.Inverse(RotationValue) * (worldPoint - PositionValue);

        return new Vector3(
            ScaleValue.x != 0f ? local.x / ScaleValue.x : 0f,
            ScaleValue.y != 0f ? local.y / ScaleValue.y : 0f,
            ScaleValue.z != 0f ? local.z / ScaleValue.z : 0f);
    }

    public abstract IEnumerator BuildTrianglesCoroutine(PrimitiveFlags flags, int trianglesPerFrame);
    public abstract void Destroy();

    public abstract IReadOnlyList<ProjectMerBlock> GetProjectMerBlocks
    (
        int modelObjectId,
        int startObjectId,
        Func<Vector3, Vector3> inverseTransformPoint,
        Quaternion modelRotation);

    protected void BuildNativePrimitives(PrimitiveFlags flags)
    {
        DestroyNativePrimitives();

        foreach (ModelPrimitive mp in ModelPrimitives)
        {
            Vector3 worldCenter = TransformPoint(mp.Center);
            Quaternion worldRot = RotationValue * mp.Rotation;

            if (IsUniformScale(mp.Scale))
            {
                var shapePrim = Primitive.Create(
                    mp.Type, flags, worldCenter, worldRot.eulerAngles,
                    mp.Scale, true, mp.Color);

                NativePrimitives.Add(shapePrim);
                NativePrimitiveBases.Add(null);
            }
            else
            {
                var basePrim = Primitive.Create(
                    PrimitiveType.Quad, PrimitiveFlags.None,
                    worldCenter, worldRot.eulerAngles,
                    mp.Scale, true, Color.clear);

                var shapePrim = Primitive.Create(
                    mp.Type, flags, Vector3.zero, null,
                    Vector3.one, true, mp.Color);

                shapePrim.Transform.SetParent(basePrim.Transform, false);

                NativePrimitives.Add(shapePrim);
                NativePrimitiveBases.Add(basePrim);
            }
        }
    }

    protected void DestroyNativePrimitives()
    {
        foreach (Primitive p in NativePrimitives)
            p.Destroy();

        foreach (Primitive? b in NativePrimitiveBases)
            b?.Destroy();
        NativePrimitives.Clear();
        NativePrimitiveBases.Clear();
    }

    protected static bool IsUniformScale(Vector3 s)
    {
        const float eps = 1e-3f;

        return Mathf.Abs(s.x - s.y) < eps * Mathf.Max(1f, s.x)
            && Mathf.Abs(s.y - s.z) < eps * Mathf.Max(1f, s.y);
    }
}