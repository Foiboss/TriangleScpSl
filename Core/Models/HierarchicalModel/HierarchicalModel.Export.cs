using AdminToys;
using TriangleScpSl.Core.Decomposition.NGonDecomposition;
using TriangleScpSl.Core.ProjectMerExport;
using UnityEngine;

namespace TriangleScpSl.Core.Models.HierarchicalModel;

public partial class HierarchicalModel
{
    public override IReadOnlyList<ProjectMerBlock> GetProjectMerBlocks
    (
        int modelObjectId, int startObjectId,
        Func<Vector3, Vector3> inverseTransformPoint, Quaternion modelRotation)
    {
        if (IsDestroyedValue) return [];

        IReadOnlyList<PrimitiveSnapshot> primitives = GetPrimitiveSnapshotWithoutNatives();
        if (primitives.Count == 0 && ModelPrimitives.Count == 0) return [];

        var blocks = new List<ProjectMerBlock>(primitives.Count + ModelPrimitives.Count * 2);
        var primitiveObjectIds = new List<int>(primitives.Count);
        int objectId = startObjectId;

        for (var i = 0; i < primitives.Count; i++)
        {
            primitiveObjectIds.Add(objectId++);
        }

        for (var i = 0; i < primitives.Count; i++)
        {
            PrimitiveSnapshot p = primitives[i];

            int parentId = p.ParentIndex >= 0 && p.ParentIndex < primitiveObjectIds.Count
                ? primitiveObjectIds[p.ParentIndex] : modelObjectId;
            Vector3 pos = p.ParentIndex >= 0 ? p.LocalPosition : inverseTransformPoint(p.Position);

            Vector3 rot = p.ParentIndex >= 0 ? p.LocalRotation.eulerAngles
                : (Quaternion.Inverse(modelRotation) * p.Rotation).eulerAngles;
            Vector3 scl = p.ParentIndex >= 0 ? p.LocalScale : p.Scale;

            blocks.Add(new ProjectMerBlock
            {
                Name = $"(H.{i + 1}){p.Kind}", ObjectId = primitiveObjectIds[i], ParentId = parentId,
                Position = pos, Rotation = rot, Scale = scl, BlockType = 1, IsPrimitive = true,
                PrimitiveType = (int)p.PrimitiveType, PrimitiveColor = p.Color,
                PrimitiveFlags = p.Flags, Static = false,
            });
        }

        Quaternion invRot = Quaternion.Inverse(modelRotation);
        PrimitiveFlags curFlags = Flags;

        for (var i = 0; i < ModelPrimitives.Count; i++)
        {
            ModelPrimitive mp = ModelPrimitives[i];
            Vector3 wc = TransformPoint(mp.Center);
            Quaternion wr = Rotation * mp.Rotation;

            if (IsUniformScale(mp.Scale))
            {
                blocks.Add(new ProjectMerBlock
                {
                    Name = $"(Native.{i + 1})", ObjectId = objectId++, ParentId = modelObjectId,
                    Position = inverseTransformPoint(wc), Rotation = (invRot * wr).eulerAngles,
                    Scale = mp.Scale, BlockType = 1, IsPrimitive = true,
                    PrimitiveType = (int)mp.Type, PrimitiveColor = mp.Color,
                    PrimitiveFlags = curFlags, Static = false,
                });
            }
            else
            {
                int baseId = objectId++;
                int shapeId = objectId++;

                blocks.Add(new ProjectMerBlock
                {
                    Name = $"(Native.{i + 1}).Base", ObjectId = baseId, ParentId = modelObjectId,
                    Position = inverseTransformPoint(wc), Rotation = (invRot * wr).eulerAngles,
                    Scale = mp.Scale, BlockType = 0, IsPrimitive = false, Static = false,
                });

                blocks.Add(new ProjectMerBlock
                {
                    Name = $"(Native.{i + 1})", ObjectId = shapeId, ParentId = baseId,
                    Position = Vector3.zero, Rotation = Vector3.zero, Scale = Vector3.one,
                    BlockType = 1, IsPrimitive = true, PrimitiveType = (int)mp.Type,
                    PrimitiveColor = mp.Color, PrimitiveFlags = curFlags, Static = false,
                });
            }
        }

        return blocks;
    }
}