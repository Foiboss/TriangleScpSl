using AdminToys;
using Exiled.API.Features.Toys;
using TriangleScpSl.Core.Decomposition.NGonDecomposition;
using TriangleScpSl.Core.Primitives.Parallelogram;
using TriangleScpSl.Core.ProjectMerExport;
using UnityEngine;

namespace TriangleScpSl.Core.Models.ExactModel;

public partial class ExactModel
{
    public override IReadOnlyList<ProjectMerBlock> GetProjectMerBlocks
    (
        int modelObjectId,
        int startObjectId,
        Func<Vector3, Vector3> inverseTransformPoint,
        Quaternion modelRotation)
    {
        if (IsDestroyedValue || (_parallelograms.Count == 0 && _rectangles.Count == 0 && NativePrimitives.Count == 0))
            return [];

        List<ProjectMerBlock> blocks = new(_parallelograms.Count * 2 + _rectangles.Count + NativePrimitives.Count * 2);
        int objectId = startObjectId;

        for (var parallelogramIndex = 0; parallelogramIndex < _parallelograms.Count; parallelogramIndex++)
        {
            ParallelogramPrimitive parallelogram = _parallelograms[parallelogramIndex];
            Primitive basePrimitive = parallelogram.BasePrimitive;
            Primitive quadPrimitive = parallelogram.QuadPrimitive;

            int baseId = objectId++;
            int quadId = objectId++;

            blocks.Add(new ProjectMerBlock
            {
                Name = $"(P.{parallelogramIndex + 1}).Base",
                ObjectId = baseId,
                ParentId = modelObjectId,
                Position = inverseTransformPoint(basePrimitive.Position),
                Rotation = (Quaternion.Inverse(modelRotation) * basePrimitive.Rotation).eulerAngles,
                Scale = basePrimitive.Scale,
                BlockType = 0,
                IsPrimitive = false,
                Static = false,
            });

            blocks.Add(new ProjectMerBlock
            {
                Name = $"(P.{parallelogramIndex + 1})",
                ObjectId = quadId,
                ParentId = baseId,
                Position = quadPrimitive.Transform.localPosition,
                Rotation = quadPrimitive.Transform.localRotation.eulerAngles,
                Scale = quadPrimitive.Transform.localScale,
                BlockType = 1,
                IsPrimitive = true,
                PrimitiveType = (int)PrimitiveType.Quad,
                PrimitiveColor = parallelogram.Color,
                PrimitiveFlags = parallelogram.Flags,
                Static = false,
            });
        }

        for (var ri = 0; ri < _rectangles.Count; ri++)
        {
            Primitive rect = _rectangles[ri];
            int rectId = objectId++;

            blocks.Add(new ProjectMerBlock
            {
                Name = $"(R.{ri + 1})",
                ObjectId = rectId,
                ParentId = modelObjectId,
                Position = inverseTransformPoint(rect.Position),
                Rotation = (Quaternion.Inverse(modelRotation) * rect.Rotation).eulerAngles,
                Scale = rect.Scale,
                BlockType = 1,
                IsPrimitive = true,
                PrimitiveType = (int)PrimitiveType.Quad,
                PrimitiveColor = rect.Color,
                PrimitiveFlags = rect.Flags,
                Static = false,
            });
        }

        // Export native primitives from ModelPrimitive data directly,
        // without relying on spawned Unity objects (which may not exist during export).
        Quaternion inverseRotation = Quaternion.Inverse(modelRotation);
        PrimitiveFlags currentFlags = Flags;

        for (var i = 0; i < ModelPrimitives.Count; i++)
        {
            ModelPrimitive mp = ModelPrimitives[i];

            Vector3 worldCenter = TransformPoint(mp.Center);
            Quaternion worldRot = Rotation * mp.Rotation;

            if (IsUniformScale(mp.Scale))
            {
                int shapeId = objectId++;

                blocks.Add(new ProjectMerBlock
                {
                    Name = $"(Native.{i + 1})",
                    ObjectId = shapeId,
                    ParentId = modelObjectId,
                    Position = inverseTransformPoint(worldCenter),
                    Rotation = (inverseRotation * worldRot).eulerAngles,
                    Scale = mp.Scale,
                    BlockType = 1,
                    IsPrimitive = true,
                    PrimitiveType = (int)mp.Type,
                    PrimitiveColor = mp.Color,
                    PrimitiveFlags = currentFlags,
                    Static = false,
                });
            }
            else
            {
                int baseId = objectId++;
                int shapeId = objectId++;

                blocks.Add(new ProjectMerBlock
                {
                    Name = $"(Native.{i + 1}).Base",
                    ObjectId = baseId,
                    ParentId = modelObjectId,
                    Position = inverseTransformPoint(worldCenter),
                    Rotation = (inverseRotation * worldRot).eulerAngles,
                    Scale = mp.Scale,
                    BlockType = 0,
                    IsPrimitive = false,
                    Static = false,
                });

                blocks.Add(new ProjectMerBlock
                {
                    Name = $"(Native.{i + 1})",
                    ObjectId = shapeId,
                    ParentId = baseId,
                    Position = Vector3.zero,
                    Rotation = Vector3.zero,
                    Scale = Vector3.one,
                    BlockType = 1,
                    IsPrimitive = true,
                    PrimitiveType = (int)mp.Type,
                    PrimitiveColor = mp.Color,
                    PrimitiveFlags = currentFlags,
                    Static = false,
                });
            }
        }

        return blocks;
    }
}