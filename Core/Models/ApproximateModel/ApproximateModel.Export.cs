using AdminToys;
using TriangleScpSl.Core.ProjectMerExport;
using UnityEngine;

namespace TriangleScpSl.Core.Models.ApproximateModel;

public partial class ApproximateModel
{
    public override IReadOnlyList<ProjectMerBlock> GetProjectMerBlocks
    (
        int modelObjectId,
        int startObjectId,
        Func<Vector3, Vector3> inverseTransformPoint,
        Quaternion modelRotation)
    {
        if (IsDestroyedValue) return [];

        IReadOnlyList<PrimitiveSnapshot> primitives = GetPrimitiveSnapshot();

        if (primitives.Count == 0) return [];

        List<ProjectMerBlock> blocks = new(primitives.Count);
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
                Name = $"(Q.{i + 1}){primitive.Kind}",
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

        return blocks;
    }
}