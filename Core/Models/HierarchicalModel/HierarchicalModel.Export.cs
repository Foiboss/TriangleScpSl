using AdminToys;
using TriangleScpSl.Core.NGons;
using TriangleScpSl.Core.ProjectMerExport;
using UnityEngine;

namespace TriangleScpSl.Core.Models.HierarchicalModel;

public partial class HierarchicalModel
{
    public override IReadOnlyList<ProjectMerBlock> GetProjectMerBlocks
    (
        int modelObjectId,
        int startObjectId,
        Func<Vector3, Vector3> inverseTransformPoint,
        Quaternion modelRotation)
    {
        if (IsDestroyedValue) return [];

        IReadOnlyList<PrimitiveSnapshot> primitives = GetPrimitiveSnapshotWithoutNatives();

        if (primitives.Count == 0 && ModelPrimitives.Count == 0) return [];

        List<ProjectMerBlock> blocks = new(primitives.Count + ModelPrimitives.Count * 2);
        List<int> primitiveObjectIds = new(primitives.Count);
        int objectId = startObjectId;

        for (var i = 0; i < primitives.Count; i++)
        {
            primitiveObjectIds.Add(objectId++);
        }

        for (var i = 0; i < primitives.Count; i++)
        {
            PrimitiveSnapshot primitive = primitives[i];

            int parentId = primitive.ParentIndex >= 0 && primitive.ParentIndex < primitiveObjectIds.Count
                ? primitiveObjectIds[primitive.ParentIndex]
                : modelObjectId;

            Vector3 position = primitive.ParentIndex >= 0
                ? primitive.LocalPosition
                : inverseTransformPoint(primitive.Position);

            Vector3 rotation = primitive.ParentIndex >= 0
                ? primitive.LocalRotation.eulerAngles
                : (Quaternion.Inverse(modelRotation) * primitive.Rotation).eulerAngles;

            Vector3 scale = primitive.ParentIndex >= 0
                ? primitive.LocalScale
                : primitive.Scale;

            blocks.Add(new ProjectMerBlock
            {
                Name = $"(H.{i + 1}){primitive.Kind}",
                ObjectId = primitiveObjectIds[i],
                ParentId = parentId,
                Position = position,
                Rotation = rotation,
                Scale = scale,
                BlockType = 1,
                IsPrimitive = true,
                PrimitiveType = (int)primitive.PrimitiveType,
                PrimitiveColor = primitive.Color,
                PrimitiveFlags = primitive.Flags,
                Static = false,
            });
        }

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