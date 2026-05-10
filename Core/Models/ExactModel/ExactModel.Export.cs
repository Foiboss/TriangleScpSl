using Exiled.API.Features.Toys;
using TriangleScpSl.Core.NGons;
using TriangleScpSl.Core.ProjectMerExport;
using TriangleScpSl.Core.Triangulation.Parallelogram;
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

        for (var i = 0; i < NativePrimitives.Count; i++)
        {
            Primitive native = NativePrimitives[i];
            ModelPrimitive model = ModelPrimitives[i];

            bool hasBase = i < NativePrimitiveBases.Count && NativePrimitiveBases[i] != null;

            if (hasBase)
            {
                Primitive basePrim = NativePrimitiveBases[i];
                int baseId = objectId++;
                int shapeId = objectId++;

                blocks.Add(new ProjectMerBlock
                {
                    Name = $"(Native.{i + 1}).Base",
                    ObjectId = baseId,
                    ParentId = modelObjectId,
                    Position = inverseTransformPoint(basePrim.Position),
                    Rotation = (Quaternion.Inverse(modelRotation) * basePrim.Rotation).eulerAngles,
                    Scale = basePrim.Scale,
                    BlockType = 0,
                    IsPrimitive = false,
                    Static = false,
                });

                blocks.Add(new ProjectMerBlock
                {
                    Name = $"(Native.{i + 1})",
                    ObjectId = shapeId,
                    ParentId = baseId,
                    Position = native.Transform.localPosition,
                    Rotation = native.Transform.localRotation.eulerAngles,
                    Scale = native.Transform.localScale,
                    BlockType = 1,
                    IsPrimitive = true,
                    PrimitiveType = (int)model.Type,
                    PrimitiveColor = model.Color,
                    PrimitiveFlags = native.Flags,
                    Static = false,
                });
            }
            else
            {
                int shapeId = objectId++;

                blocks.Add(new ProjectMerBlock
                {
                    Name = $"(Native.{i + 1})",
                    ObjectId = shapeId,
                    ParentId = modelObjectId,
                    Position = inverseTransformPoint(native.Position),
                    Rotation = (Quaternion.Inverse(modelRotation) * native.Rotation).eulerAngles,
                    Scale = native.Scale,
                    BlockType = 1,
                    IsPrimitive = true,
                    PrimitiveType = (int)model.Type,
                    PrimitiveColor = model.Color,
                    PrimitiveFlags = native.Flags,
                    Static = false,
                });
            }
        }

        return blocks;
    }
}